# Model-Load Cleanup Ownership Design

**Status:** Approved for implementation; awaiting plan

**Date:** 2026-07-25

**Fixed design boundary:** `513367f33d25eb482ac478c59511e12fd3ea63a0`

## Purpose

Make the state published for a scheduled backend model load return to baseline exactly once even when request cancellation prevents the scheduled delegate from starting.

At the fixed boundary, `BackendHandler.LoadHighestPressureNow` sets the selected `ModelRequestPressure.IsLoading`, creates one `Session.GenClaim(modelLoads: 1)` for every participating session, and then schedules all remaining work through cancellation-gated `Task.Factory.StartNew`. The delegate owns reservation, load execution, failure classification, claim disposal, and loaded-model reassignment. If its cancellation token is already canceled, or becomes canceled before the delegate starts, the task can enter the canceled state without executing that owner. The pressure can remain marked as loading and the session claims and `LoadingModels` counters can remain published indefinitely.

The selected design retains the cancellation-gated scheduling policy and introduces one idempotent cleanup owner. The started delegate, a cancellation-only synchronous continuation, and the synchronous scheduling-failure path all invoke that owner. Only the first invocation performs cleanup, and the cleanup actions depend on whether model-load work actually started.

## Boundary and Current Ownership

The production boundary is one method in one file:

- `BackendHandler.LoadHighestPressureNow` in `src/Backends/BackendHandler.cs`.

The method currently:

1. filters available model-capable backends;
2. orders non-loading model pressure by the existing count/wait heuristic;
3. preserves the existing all-request match preference;
4. selects a loader outside the pressure's failed-backend set;
5. applies the existing unused-backend and configured load-order preference;
6. sets `highestPressure.IsLoading = true`;
7. creates a model-load `Session.GenClaim` for each session in `highestPressure.Sessions`;
8. calls `Task.Factory.StartNew(action, cancel)`;
9. has the action reserve the backend, wait for its usages, clean RAM, and load or select `(none)`;
10. classifies load exceptions and model-name mismatch;
11. clears the reservation and `IsLoading`, disposes the claims, and calls `ReassignLoadedModelsList`.

`T2IBackendRequest.ReleasePressure` separately owns each request's pressure-count release. `T2IBackendRequest.Complete` invokes that release when a waiting request ends. It does not own the model-load claims created for the shared pressure, and this project will not transfer that ownership to it.

`Session.GenClaim.Dispose` removes the claim from `Session.Claims`, subtracts its counters, and disposes its local cancellation source. It is therefore the correct existing primitive for retracting each published model-load claim, but it must have one effective owner.

## Selected Approach

Introduce one cleanup owner local to the scheduled load. Its concrete implementation may be a private local function or a small private helper, provided it retains the boundary and semantics in this document.

The owner uses an interlocked gate. Each exit path may invoke it, but only the invocation that atomically wins the gate mutates state. The owner receives or otherwise knows whether the load delegate started:

- **Started cleanup** is invoked from the delegate's `finally`.
- **Pre-start cancellation cleanup** is invoked by a continuation attached to the scheduled task with `CancellationToken.None` and `TaskContinuationOptions.OnlyOnCanceled | TaskContinuationOptions.ExecuteSynchronously`.
- **Synchronous scheduling cleanup** is invoked from a catch around the scheduling operation if `Task.Factory.StartNew` throws before returning a task.

The original `Task.Factory.StartNew(action, cancel)` call and delegate policy remain in place. The design does not replace it with unconditional scheduling, `Task.Run`, an async delegate, or a different scheduler.

The cancellation continuation is deliberately non-cancelable. Reusing the already-canceled request token for cleanup would reproduce the defect by allowing cleanup itself to be skipped. `OnlyOnCanceled` prevents the continuation from competing with normal, faulted, or successfully completed started work; `ExecuteSynchronously` minimizes the interval between the canceled task transition and state retraction without introducing a blocking wait.

The synchronous scheduling catch performs pre-start cleanup and then preserves exception propagation. It does not reinterpret a scheduling failure as a backend load attempt.

## Cleanup Policies

### Pre-start policy

If the delegate never starts, the owner retracts only state published before scheduling:

1. set `highestPressure.IsLoading = false`; and
2. dispose every captured model-load claim.

It must not:

- clear `availableBackend.ReserveModelLoad`, because the delegate never set it;
- add the backend to `BadBackends`;
- add a load reason to `BackendFailReasons`;
- invoke `releasePressure`, because each request still owns its pressure count;
- call `ReassignLoadedModelsList`, because no model load or model selection was attempted; or
- emit a model-load success, failure, or mismatch classification.

### Started policy

Once the delegate starts, its cleanup preserves the existing order and classification policy:

1. clear `availableBackend.ReserveModelLoad`;
2. compare `availableBackend.Backend.CurrentModelName` with the requested model;
3. on mismatch, retain the existing warning and add the backend ID to `highestPressure.BadBackends` under the existing pressure lock;
4. set `highestPressure.IsLoading = false`;
5. dispose every captured model-load claim; and
6. call `ReassignLoadedModelsList`.

The existing exception path remains responsible for unwrapping aggregate exceptions, logging the readable failure, and adding its readable text to `BackendFailReasons`. The cleanup owner does not change which exception types are caught, the messages recorded, or the mismatch rule.

The started policy also remains authoritative when cancellation occurs while waiting for backend usages or during `LoadModel`. Those paths began model-load work, including ownership of the reservation, so they retain the established reservation release, mismatch classification, claim disposal, and model-list refresh.

## State and Transition Table

| State/trigger | Work known to have started? | Cleanup caller | Effective cleanup | Explicitly unchanged/not performed |
|---|---:|---|---|---|
| Pressure selected; `IsLoading` and claims published | No | None yet | None; scheduling follows immediately under the existing pressure lock | Heuristic, selected backend, request pressure, and session set remain unchanged |
| `Task.Factory.StartNew` throws before returning a task | No | Synchronous scheduling catch | Win gate; clear `IsLoading`; dispose captured claims; rethrow | No reservation clear, backend failure classification, pressure release, or model-list refresh |
| Returned task is canceled before delegate entry | No | Non-cancelable `OnlyOnCanceled \| ExecuteSynchronously` continuation | Win gate; clear `IsLoading`; dispose captured claims | No reservation clear, backend failure classification, pressure release, or model-list refresh |
| Delegate starts, waits for active usages, then global shutdown causes its existing early return | Yes | Delegate `finally` | Win gate; run full started cleanup in existing order, including model-list reassignment | Existing global-cancellation check and wait cadence remain unchanged |
| Delegate starts and request cancellation interrupts `LoadModel(...).Wait(cancel)` | Yes | Delegate `finally` after existing catch | Win gate; preserve failure-reason handling and full started cleanup | Cancellation token semantics and exception classification remain unchanged |
| Delegate loads `(none)` or the requested model successfully | Yes | Delegate `finally` | Win gate; full started cleanup; model-name match avoids bad-backend classification | Successful selection/load logging and later request progress remain unchanged |
| Delegate catches a backend load failure or ends with model mismatch | Yes | Delegate `finally` | Win gate; preserve reason/mismatch classification and full started cleanup | Failure text, refusal reasoning, and retry eligibility remain unchanged |
| Any losing cleanup invocation or repeated invocation | Either | Any of the three callers | Interlocked gate returns without mutation | No second claim disposal, counter decrement, reservation change, classification, or refresh |

## Concurrency and Exactly-Once Rationale

The cleanup gate protects the cleanup unit, not only `Session.GenClaim.Dispose`. This is necessary because double invocation could otherwise subtract session counters twice, race `IsLoading`, duplicate bad-backend classification/logging, or refresh loaded-model flags more than once.

The scheduled task cannot both be canceled before execution and execute its delegate. Therefore `OnlyOnCanceled` normally selects the pre-start path while delegate `finally` selects the started path. The interlocked gate remains required as a defensive ownership invariant across scheduling, continuation dispatch, exceptional control flow, and future maintenance.

The claims list is completely captured before scheduling. No cleanup path iterates the mutable `highestPressure.Sessions` set to reconstruct ownership. This keeps disposal paired with exactly the claim instances created for this load attempt.

`highestPressure.Locker` continues to serialize selection and publication. The new gate does not replace that lock, broaden it around backend work, or change pressure collection ordering. Cleanup continues to update `BadBackends` under the existing pressure lock.

The pre-start path does not clear `ReserveModelLoad`: that flag is acquired only at delegate entry, and clearing it from a non-owner could release a separate reservation if surrounding behavior changes or another owner has since acted. The started path does clear it because the delegate remains its owner.

The pre-start path does not call `ReassignLoadedModelsList`: no backend model state changed. Avoiding that refresh preserves current work and notification behavior while making cleanup ownership truthful.

Request-pressure release remains request-owned. A sole canceled request can remove its pressure through `Complete`; a shared pressure can remain registered for other requests. In either case, retracting `IsLoading` lets remaining or later eligible work select a replacement load rather than observing a permanently active load.

## Preserved Behavior and Compatibility

Implementation must preserve:

- the pressure heuristic, including count weighting and first-request wait time;
- pressure filtering, all-request match preference, bad-backend exclusion, and backend selection order;
- the 1.5-second pressure-age rule when multiple loaders are available;
- the configured `last_used` and `first_free` model-load order behavior and invalid-setting error;
- cancellation-gated `Task.Factory.StartNew(action, cancel)` and its current scheduler selection;
- the existing 100-millisecond usage-wait polling and one-second scheduler/handler behavior;
- global-shutdown return behavior while waiting for usages;
- request-token behavior during `LoadModel(...).Wait(cancel)`;
- the timing of `highestPressure.IsLoading` and session model-load claim publication before scheduling;
- `NotifyWillLoad` and session status timing derived from that state;
- backend reservation ownership after delegate entry;
- readable load-failure logging, `BackendFailReasons`, `BadBackends`, and eventual all-loaders-failed refusal reasons;
- each request's ownership of `releasePressure`;
- successful model selection/loading and subsequent waiter progress;
- public methods, fields, signatures, types, and external extension ABI; and
- repository C# conventions, including explicit types, full braced blocks, and XML documentation for any new field if a field is required.

This is a reliability correction, not a performance optimization. It makes no throughput, latency, memory, or scheduling-fairness claim.

## Explicit Non-Goals

This project does not:

- address the adjacent `LoadModelOnAll` shutdown/reservation issue;
- clean stale `ModelRequestPressure.Requests` or `Sessions`;
- redesign cancellation responsiveness while waiting for backend usages;
- alter the underlying asynchronous `AbstractT2IBackend.LoadModel` continuation or cancellation contract;
- optimize or instrument the scheduler under P5;
- change pressure scoring, backend preference, retries, autoscaling, or the one-second handler loop;
- move request-pressure release into model-load cleanup;
- redesign `Session.GenClaim` or make its general `Dispose` implementation idempotent;
- introduce a broader scheduler/lifecycle service;
- change browser, API, backend persistence, launcher, or extension code; or
- claim that the static review proves runtime thread interleavings.

## Implementation Stages

1. In `LoadHighestPressureNow`, capture the already-created claims and selected pressure/backend in one local cleanup owner with an interlocked exactly-once gate.
2. Move the current delegate-finalization actions into the started policy while retaining their existing ordering, locking, messages, classification, and `ReassignLoadedModelsList` placement.
3. Keep `Task.Factory.StartNew(action, cancel)`, invoke started cleanup from its delegate `finally`, attach the non-cancelable synchronous cancellation-only continuation for the pre-start path, and cover a synchronous scheduling exception with pre-start cleanup plus rethrow.
4. Perform the bounded static inventory and control-flow review before asking the maintainer to build or exercise runtime cancellation.

The production patch should change only `src/Backends/BackendHandler.cs`. Documentation updates after implementation and validation are separate records.

## Static Verification

Agents will not build, launch, run tests, start the server, load a model, interrupt a request, or perform any other runtime validation. Static verification must:

1. confirm the production diff changes only `src/Backends/BackendHandler.cs`;
2. enumerate every assignment to `ModelRequestPressure.IsLoading`, `T2IBackendData.ReserveModelLoad`, `BadBackends`, `BackendFailReasons`, and every creation/disposal of the captured model-load claims;
3. trace state publication through successful scheduling, pre-start cancellation, synchronous scheduling failure, usage-wait shutdown, mid-load cancellation, load failure, model mismatch, and success;
4. prove every post-publication path reaches one cleanup caller;
5. prove the interlocked gate surrounds the complete cleanup unit and every losing invocation returns without mutation;
6. prove the pre-start policy performs only `IsLoading` reset and captured-claim disposal;
7. prove the started policy preserves reservation release, mismatch classification, `IsLoading` reset, claim disposal, and `ReassignLoadedModelsList` in the existing order;
8. prove exception unwrapping, logs, `BackendFailReasons`, `BadBackends`, and all-loaders-failed refusal text remain unchanged;
9. prove `releasePressure` remains request-owned and is not called by either new cleanup policy;
10. prove the original cancellation token remains on `Task.Factory.StartNew` and `LoadModel(...).Wait(cancel)`, while the cancellation-only cleanup continuation uses `CancellationToken.None`;
11. prove pressure ordering/filtering, backend selection, reservation acquisition, wait cadence, status timing, successful loading, the scheduler loop, and public API/ABI are unchanged;
12. confirm no change to `LoadModelOnAll`, `Session.GenClaim`, browser code, launchers, extensions, generated content, user data, or excluded adjacent findings;
13. inspect the exact commit/file range; and
14. run `git diff --check`.

Static review can establish ownership, branch ordering, token placement, call-site scope, and signature preservation. It cannot prove the cancellation race, runtime counter values, backend behavior, thread timing, performance, or platform behavior.

## Maintainer Validation Matrix

The maintainer will build and run the live software. For every case, record the affected session's `Claims` membership and `LoadingModels`, the selected pressure's `IsLoading` and request count, the loader's reservation/availability, backend failure reasons, visible status, and whether a later request progresses.

1. **Pre-start cancellation, sole pressure:** cancel in the window after `IsLoading` and claims publish but before delegate entry. Confirm `IsLoading`, claims, and `LoadingModels` return to baseline exactly once; no backend is marked bad; no reservation is cleared as a load-owned action; and a later request can load the model.
2. **Pre-start cancellation, shared pressure:** cancel one participating request while another still wants the same model. Confirm request pressure reflects request ownership, model-load claims retract exactly once, `IsLoading` clears, and the remaining request can schedule and complete a replacement load.
3. **Cancellation while waiting for usage:** hold the selected backend busy, start the delegate, then interrupt/shut down through the existing supported path. Confirm full started cleanup releases the reservation, classifies mismatch exactly as before, clears claims/counters once, refreshes model flags, and later work is not stranded.
4. **Cancellation during a slow `LoadModel`:** interrupt after the backend load begins. Confirm existing failure reason/mismatch behavior, full cleanup, no duplicate counter decrement, and eventual backend/request progress.
5. **Successful load:** exercise both ordinary model loading and `(none)` selection. Confirm current status timing, reservation lifetime, model identity, loaded-model reassignment, claim/counter return, and waiting-request progress are unchanged.
6. **Backend load failure:** force a readable backend failure and a model-name mismatch. Confirm each existing reason and bad-backend entry appears once, cleanup returns to baseline once, retry/refusal behavior is unchanged, and no pre-start classification is introduced.
7. **Repeated cleanup pressure:** stress cancellation close to delegate dispatch and repeat the race. Confirm no negative `LoadingModels`, no duplicate claim removal/disposal symptom, no duplicate bad-backend classification or refresh, and no permanent `IsLoading`.
8. **Shutdown:** cancel during the pre-start window and after delegate entry during shutdown. Confirm each follows its respective pre-start or started policy, the handler does not hang, and reservations/claims/counters finish at baseline.
9. **Compatibility flow:** run normal generation with sole and shared model pressure and observe status frames/`NotifyWillLoad`, pressure order, selected loader order, retries, final images, and subsequent generations.

Linux, Windows, and other-platform runtime results must be recorded separately. No runtime or performance result is implied by implementation or static review.

## Success Criteria

Rank 11 succeeds when:

- cancellation or scheduling failure cannot leave pre-published `IsLoading`, claims, or `LoadingModels` state stranded;
- the complete cleanup unit executes effectively once for every scheduled attempt;
- pre-start cleanup does not mutate state owned only by a started load;
- started cleanup and failure reasoning retain their current order and semantics;
- sole and shared pressure requests can make later progress;
- successful loading, status timing, selection, retry, reservation, and refusal behavior remain compatible;
- the production patch is confined to `src/Backends/BackendHandler.cs`; and
- static review and the maintainer matrix pass without an agent runtime claim.

## Rollback

Rollback is one bounded ownership change in `BackendHandler.LoadHighestPressureNow`: remove the interlocked cleanup gate, cancellation-only continuation, and synchronous scheduling catch; restore the original delegate-local cleanup block and post-finally `ReassignLoadedModelsList`.

No data migration, configuration rollback, public API restoration, browser change, or extension rebuild contract is involved. A partial rollback is invalid because retaining multiple cleanup callers without their shared gate can double-dispose claims or mutate state twice, while retaining the gate without complete exit-path coverage can restore the leak.
