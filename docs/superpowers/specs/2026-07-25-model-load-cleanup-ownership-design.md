# Model-Load Cleanup Ownership Design

**Status:** Approved for implementation; awaiting plan

**Date:** 2026-07-25

**Fixed design boundary:** `513367f33d25eb482ac478c59511e12fd3ea63a0`

## Purpose

Make the state published for a scheduled backend model load return to baseline exactly once when request cancellation prevents the scheduled delegate from starting or a later publication/scheduling setup step fails.

At the fixed boundary, `BackendHandler.LoadHighestPressureNow` sets the selected `ModelRequestPressure.IsLoading`, creates one `Session.GenClaim(modelLoads: 1)` for every participating session, and then schedules all remaining work through cancellation-gated `Task.Factory.StartNew`. The delegate owns reservation, load execution, failure classification, claim disposal, and loaded-model reassignment. If its cancellation token is already canceled, or becomes canceled before the delegate starts, the task can enter the canceled state without executing that owner. The pressure can remain marked as loading and the session claims and `LoadingModels` counters can remain published indefinitely.

The selected design retains the cancellation-gated scheduling policy and introduces one idempotent cleanup owner backed by an atomic lifecycle gate. The gate and captured-claims list exist before `IsLoading` or any claim is published. The started delegate, a cancellation-only synchronous-preferred continuation, and the publication/scheduling-setup failure path all coordinate through that owner. Only the path that atomically acquires the applicable lifecycle transition performs cleanup, and the cleanup actions depend on whether model-load work actually started.

This guarantee covers every `Session.GenClaim` instance that construction successfully returns and the implementation captures. It cannot transactionally recover counters or other mutations if `Session.GenClaim` construction itself throws internally before returning the instance to this owner.

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

Introduce one cleanup owner local to the scheduled load. Its concrete implementation may be a private local function or a small private helper, provided it retains the boundary and semantics in this document. Construct its atomic lifecycle gate and empty captured-claims list before setting `highestPressure.IsLoading` or constructing any claim.

The owner uses one interlocked lifecycle gate that distinguishes unstarted published work, a started delegate, pre-start cleanup ownership, and completed started cleanup. Delegate entry and pre-start cleanup must compete atomically on that same state:

- **Started cleanup** is invoked from the delegate's `finally`.
- **Pre-start cancellation cleanup** is invoked by the four-argument `ContinueWith` overload with `CancellationToken.None`, `TaskContinuationOptions.OnlyOnCanceled | TaskContinuationOptions.ExecuteSynchronously`, and `TaskScheduler.Default`.
- **Publication/setup failure cleanup** is invoked from an outer catch around `IsLoading` publication, claim construction/capture, `Task.Factory.StartNew`, and cancellation-continuation attachment.

The original `Task.Factory.StartNew(action, cancel)` call and delegate policy remain in place. The design does not replace it with unconditional scheduling, `Task.Run`, an async delegate, or a different scheduler.

The cancellation continuation is deliberately non-cancelable. Reusing the already-canceled request token for cleanup would reproduce the defect by allowing cleanup itself to be skipped. `OnlyOnCanceled` prevents the continuation from running for normal, faulted, or successfully completed started work. `ExecuteSynchronously` requests synchronous execution when the selected scheduler permits; it is a synchronous-preferred hint, not a guarantee of inline execution. `TaskScheduler.Default` prevents an ambient/custom current scheduler from delaying or rejecting the cleanup continuation, while the original `StartNew(action, cancel)` deliberately retains its current scheduler selection.

The outer catch performs pre-start cleanup only if it atomically wins before delegate entry. If the scheduled delegate already won the started transition—for example, if continuation attachment fails after `StartNew` returned and the delegate began—the catch leaves started cleanup to the delegate's `finally`. If pre-start cleanup wins after a task was returned but before its delegate enters, delegate entry observes the pre-start-cleaned lifecycle state and exits without reserving or loading. The original setup exception is rethrown in every case and is not reinterpreted as a backend load failure.

Within the outer try, each successfully returned `GenClaim` is captured immediately. If a later claim construction, capture/setup step, `StartNew`, or continuation attachment throws, the cleanup owner retracts `IsLoading` and every claim already captured unless the delegate has started and therefore owns full cleanup. This is recovery for state available to the owner, not a transactional redesign of `GenClaim` construction.

## Cleanup Policies

### Pre-start policy

If the delegate never starts, or publication/setup fails before delegate entry wins the lifecycle transition, the owner retracts only state published before model-load work:

1. set `highestPressure.IsLoading = false` if it was published; and
2. dispose every successfully returned and captured model-load claim.

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

The started policy remains authoritative after delegate entry, including a global-shutdown return while waiting for backend usages or cancellation observed by `LoadModel(...).Wait(cancel)`. Those paths began model-load work, including ownership of the reservation, so they retain the established reservation release, mismatch classification, claim disposal, and model-list refresh.

Request cancellation alone does not interrupt the existing busy-backend loop. That loop observes only `Program.GlobalProgramCancel`. If the request token is canceled while the backend remains busy, the reservation, `IsLoading`, and claims remain published as they do now. After usage frees, the ordinary-model branch calls `LoadModel` and its subsequent `Wait(cancel)` observes the request token; the `(none)` branch has no request-token wait and retains its existing behavior. This project adds no immediate cleanup, wake, or progress guarantee for request cancellation while the backend is still busy.

Global shutdown during `LoadModel` also does not independently cancel its `Wait(cancel)`: that wait is governed by the passed request cancellation token. If that token is canceled, the existing catch/finally path runs; otherwise started cleanup occurs only when the existing load/wait exits normally or with an exception. The underlying asynchronous `LoadModel` continuation remains outside this project.

## State and Transition Table

| State/trigger | Work known to have started? | Cleanup caller | Effective cleanup | Explicitly unchanged/not performed |
|---|---:|---|---|---|
| Pressure/backend selected; gate and empty captured-claims list created | No | None yet | No published state exists | Heuristic, selected backend, request pressure, and session set remain unchanged |
| `IsLoading` publishes; zero or more claims successfully return and are captured | No | None yet | Outer setup try continues | Claim/status timing remains before scheduling |
| A later claim construction/capture or setup step throws before a task starts | No | Outer setup catch | Atomically acquire pre-start cleanup; clear published `IsLoading`; dispose all successfully returned/captured claims; rethrow | No guarantee for mutation internal to a `GenClaim` constructor that throws before returning; no load classification |
| `Task.Factory.StartNew` throws before returning a task | No | Outer setup catch | Atomically acquire pre-start cleanup; clear `IsLoading`; dispose captured claims; rethrow | No reservation clear, backend failure classification, pressure release, or model-list refresh |
| Four-argument continuation attachment throws before delegate entry | No | Outer setup catch | Atomically acquire pre-start cleanup; clear `IsLoading`; dispose captured claims; rethrow; any later delegate entry observes cleanup ownership and exits | Original task is not treated as a backend attempt |
| Continuation attachment throws after delegate entry wins | Yes | Outer setup catch and later delegate `finally` | Catch cannot acquire pre-start cleanup; rethrow setup exception; delegate retains full started cleanup ownership | No premature claim disposal or `IsLoading` reset while load work is active |
| Returned task is canceled before delegate entry | No | Non-cancelable `OnlyOnCanceled \| ExecuteSynchronously` continuation on `TaskScheduler.Default` | Atomically acquire pre-start cleanup; clear `IsLoading`; dispose captured claims | `ExecuteSynchronously` is synchronous-preferred, not guaranteed inline; no load classification or refresh |
| A returned task's delegate is dispatched after pre-start cleanup won | No | Delegate entry | Atomic started transition fails; exit without reservation or model-load work | No second cleanup invocation mutates state |
| Request token is canceled while the started delegate waits for backend usage | Yes | None at cancellation time | No immediate transition; existing busy wait continues until usage frees or global shutdown is observed | Busy loop observes only `Program.GlobalProgramCancel`; no new wake, cleanup, or progress guarantee |
| Global shutdown is observed while the started delegate waits for backend usage | Yes | Delegate `finally` after existing early return | Win started-cleanup transition; run full started cleanup in existing order, including model-list reassignment | Existing global-cancellation check and wait cadence remain unchanged |
| Delegate starts and request cancellation interrupts `LoadModel(...).Wait(cancel)` | Yes | Delegate `finally` after existing catch | Win gate; preserve failure-reason handling and full started cleanup | Cancellation token semantics and exception classification remain unchanged |
| Global shutdown occurs during `LoadModel` while request token is not canceled | Yes | Delegate `finally` only after existing load/wait exits | No immediate transition from global shutdown alone; then full started cleanup | `Wait(cancel)` is governed by request cancellation, not `Program.GlobalProgramCancel` |
| Delegate loads `(none)` or the requested model successfully | Yes | Delegate `finally` | Win gate; full started cleanup; model-name match avoids bad-backend classification | Successful selection/load logging and later request progress remain unchanged |
| Delegate catches a backend load failure or ends with model mismatch | Yes | Delegate `finally` | Win gate; preserve reason/mismatch classification and full started cleanup | Failure text, refusal reasoning, and retry eligibility remain unchanged |
| Any losing cleanup invocation or repeated invocation | Either | Any of the three callers | Interlocked gate returns without mutation | No second claim disposal, counter decrement, reservation change, classification, or refresh |

## Concurrency and Exactly-Once Rationale

The lifecycle gate protects delegate entry and the complete cleanup unit, not only `Session.GenClaim.Dispose`. This is necessary because a task returned before continuation setup fails can race the outer catch, while double cleanup could subtract session counters twice, race `IsLoading`, duplicate bad-backend classification/logging, or refresh loaded-model flags more than once.

The scheduled task cannot both be canceled before execution and execute its delegate. Therefore `OnlyOnCanceled` normally selects the pre-start path while delegate `finally` selects the started path. Setup failure after `StartNew` returns is less exclusive: the task may be pending or its delegate may already be running. Atomic competition on the lifecycle state ensures the outer catch either wins pre-start ownership and prevents later delegate work, or observes started ownership and leaves full cleanup to `finally`.

The captured-claims list exists before publication. The implementation assigns each claim returned by construction and immediately attempts to add it before the next claim or scheduling step. The guarantee applies once that returned instance is in the captured list. No cleanup path iterates the mutable `highestPressure.Sessions` set to reconstruct ownership. This keeps disposal paired with the successfully returned/captured claim instances available to this owner. It does not claim recovery for an exception thrown inside a `GenClaim` constructor before the instance returns or during a failed capture before the owner records the returned instance.

The four-argument continuation overload fixes all cleanup-continuation inputs: `CancellationToken.None`, `OnlyOnCanceled | ExecuteSynchronously`, and `TaskScheduler.Default`. The default scheduler isolates cleanup dispatch from the ambient scheduler used by the caller. The `StartNew(action, cancel)` call is intentionally not given a replacement scheduler and retains its current behavior.

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
- cleanup-continuation dispatch through the explicit four-argument overload on `TaskScheduler.Default`;
- the existing 100-millisecond usage-wait polling and one-second scheduler/handler behavior;
- the fact that the busy-usage loop observes global shutdown but not request cancellation;
- global-shutdown return behavior while waiting for usages and the lack of a new global-shutdown interrupt for an active `Wait(cancel)`;
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
- make request cancellation interrupt the busy-backend usage loop or make global shutdown independently interrupt `Wait(cancel)`;
- optimize or instrument the scheduler under P5;
- change pressure scoring, backend preference, retries, autoscaling, or the one-second handler loop;
- move request-pressure release into model-load cleanup;
- redesign `Session.GenClaim` or make its general `Dispose` implementation idempotent;
- introduce a broader scheduler/lifecycle service;
- change browser, API, backend persistence, launcher, or extension code; or
- claim that the static review proves runtime thread interleavings.

## Implementation Stages

1. In `LoadHighestPressureNow`, establish one atomic lifecycle/cleanup gate and an empty captured-claims list before publishing `IsLoading` or constructing claims.
2. Wrap `IsLoading` publication, immediate capture of each successfully returned claim, `Task.Factory.StartNew(action, cancel)`, and cleanup-continuation attachment in one outer try/catch. On failure, atomically select pre-start cleanup or defer to already-started ownership, then rethrow.
3. Move the current delegate-finalization actions into the started policy while retaining their existing ordering, locking, messages, classification, and `ReassignLoadedModelsList` placement. Make delegate entry atomically decline work if pre-start cleanup already won.
4. Attach the pre-start path with the four-argument `ContinueWith` overload using `CancellationToken.None`, `TaskContinuationOptions.OnlyOnCanceled | TaskContinuationOptions.ExecuteSynchronously`, and `TaskScheduler.Default`; retain the original `StartNew(action, cancel)` scheduler and token behavior.
5. Perform the bounded static inventory and control-flow review before asking the maintainer to build or exercise runtime cancellation.

The production patch should change only `src/Backends/BackendHandler.cs`. Documentation updates after implementation and validation are separate records.

## Static Verification

Agents will not build, launch, run tests, start the server, load a model, interrupt a request, or perform any other runtime validation. Static verification must:

1. confirm the production diff changes only `src/Backends/BackendHandler.cs`;
2. enumerate every assignment to `ModelRequestPressure.IsLoading`, `T2IBackendData.ReserveModelLoad`, `BadBackends`, `BackendFailReasons`, and every creation/disposal of the captured model-load claims;
3. prove the lifecycle gate and captured-claims list exist before `IsLoading` or claim publication, and capture of each returned claim is attempted immediately before later work;
4. trace partial publication when first/later claim construction or capture/setup fails, explicitly bounding recovery to successfully returned/captured claims and not claiming recovery from an internal constructor exception before return;
5. trace scheduling through `StartNew` throw, continuation-attachment throw before delegate entry, continuation-attachment throw after delegate entry, pre-start task cancellation, and delegate dispatch after pre-start cleanup;
6. prove the interlocked lifecycle transitions prevent backend work after pre-start cleanup and prevent pre-start cleanup from retracting state after delegate entry;
7. prove every post-publication path reaches one cleanup caller and every losing/repeated invocation returns without mutation;
8. prove the pre-start policy performs only `IsLoading` reset and captured-claim disposal;
9. prove the started policy preserves reservation release, mismatch classification, `IsLoading` reset, claim disposal, and `ReassignLoadedModelsList` in the existing order;
10. separately trace request cancellation while waiting for usages, global shutdown while waiting for usages, request cancellation at `LoadModel(...).Wait(cancel)`, and global shutdown during `LoadModel` with and without cancellation of the governing request token;
11. prove the busy loop still observes only `Program.GlobalProgramCancel`, while the load wait still observes only `cancel`, with no new immediate cleanup or progress guarantee;
12. prove exception unwrapping, logs, `BackendFailReasons`, `BadBackends`, and all-loaders-failed refusal text remain unchanged;
13. prove `releasePressure` remains request-owned and is not called by either new cleanup policy;
14. prove the original cancellation token and scheduler behavior remain on `Task.Factory.StartNew(action, cancel)` and `LoadModel(...).Wait(cancel)`;
15. prove cleanup attachment uses the four-argument `ContinueWith` overload with `CancellationToken.None`, `OnlyOnCanceled | ExecuteSynchronously`, and `TaskScheduler.Default`, without claiming `ExecuteSynchronously` guarantees inline execution;
16. prove pressure ordering/filtering, backend selection, reservation acquisition, wait cadence, status timing, successful loading, the scheduler loop, and public API/ABI are unchanged;
17. confirm no change to `LoadModelOnAll`, `Session.GenClaim`, browser code, launchers, extensions, generated content, user data, or excluded adjacent findings;
18. inspect the exact commit/file range; and
19. run `git diff --check`.

Static review can establish ownership, branch ordering, token placement, call-site scope, and signature preservation. It cannot prove the cancellation race, runtime counter values, backend behavior, thread timing, performance, or platform behavior.

## Maintainer Validation Matrix

The maintainer will build and run the live software. For every case, record the affected session's `Claims` membership and `LoadingModels`, the selected pressure's `IsLoading` and request count, the loader's reservation/availability, backend failure reasons, visible status, and whether a later request progresses.

1. **Partial publication/setup failure:** inject a failure after `IsLoading`, after one or more claims have successfully returned and been captured, at `StartNew`, and during continuation attachment before delegate entry. Confirm `IsLoading`, every captured claim, and corresponding `LoadingModels` return to baseline exactly once; no backend is classified or refreshed; and the original setup exception propagates. Do not treat an exception injected inside `GenClaim` construction before it returns as transactionally recoverable by this owner.
2. **Continuation-attachment/delegate race:** inject attachment failure as the delegate enters. Confirm either pre-start cleanup wins and the later delegate performs no reservation/load work, or started ownership wins and the delegate performs full cleanup; never observe premature claim disposal, duplicate cleanup, or active work after pre-start ownership.
3. **Pre-start cancellation, sole pressure:** cancel in the window after `IsLoading` and claims publish but before delegate entry. Confirm `IsLoading`, captured claims, and `LoadingModels` return to baseline exactly once; no backend is marked bad; no reservation is cleared as a load-owned action; and a later request can load the model.
4. **Pre-start cancellation, shared pressure:** cancel one participating request while another still wants the same model. Confirm request pressure reflects request ownership, model-load claims retract exactly once, `IsLoading` clears, and the remaining request can schedule and complete a replacement load.
5. **Request cancellation while waiting for usage:** hold the selected backend busy, let the delegate start, and cancel only the request token. Confirm there is no new immediate cleanup, wake, or progress behavior while usage remains busy. After usage frees, confirm the ordinary-model branch reaches its existing `LoadModel(...).Wait(cancel)` cancellation behavior and then performs full started cleanup. Separately confirm `(none)` retains its existing no-request-wait behavior.
6. **Global shutdown while waiting for usage:** hold the selected backend busy, let the delegate start, and request global shutdown. Confirm the existing busy-loop check returns and full started cleanup releases the reservation, performs the existing mismatch classification, clears claims/counters once, and refreshes model flags.
7. **Request cancellation during a slow `LoadModel`:** cancel the governing request token after the backend load begins. Confirm existing exception/failure-reason and mismatch behavior, full cleanup, no duplicate counter decrement, and eventual backend/request progress, without claiming the underlying asynchronous load was canceled.
8. **Global shutdown during `LoadModel`:** first leave the request token uncanceled and confirm global shutdown alone does not newly interrupt `Wait(cancel)`; cleanup occurs when the existing load/wait exits. Then exercise shutdown with the governing request token canceled and confirm the existing request-token catch/finally behavior. In both cases, confirm one full started cleanup.
9. **Successful load:** exercise both ordinary model loading and `(none)` selection. Confirm current status timing, reservation lifetime, model identity, loaded-model reassignment, claim/counter return, and waiting-request progress are unchanged.
10. **Backend load failure:** force a readable backend failure and a model-name mismatch. Confirm each existing reason and bad-backend entry appears once, cleanup returns to baseline once, retry/refusal behavior is unchanged, and no pre-start classification is introduced.
11. **Repeated cleanup pressure:** stress cancellation close to delegate dispatch and repeat the race. Confirm no negative `LoadingModels`, no duplicate claim removal/disposal symptom, no duplicate bad-backend classification or refresh, and no permanent `IsLoading`.
12. **Compatibility flow:** run normal generation with sole and shared model pressure and observe status frames/`NotifyWillLoad`, pressure order, selected loader order, retries, final images, subsequent generations, and pre-start/started shutdown behavior.

Linux, Windows, and other-platform runtime results must be recorded separately. No runtime or performance result is implied by implementation or static review.

## Success Criteria

Rank 11 succeeds when:

- pre-start cancellation or scheduling/setup failure cannot leave `IsLoading` or any successfully returned/captured claim and its `LoadingModels` contribution stranded;
- failure after publication retracts `IsLoading` and every successfully returned/captured claim available to the owner, without overstating recovery from an internal claim-construction exception before return;
- the complete cleanup unit executes effectively once for every scheduled attempt;
- continuation-attachment failure cannot allow both pre-start cleanup and active delegate work;
- pre-start cleanup does not mutate state owned only by a started load;
- started cleanup and failure reasoning retain their current order and semantics;
- sole and shared pressure requests can make later progress;
- request cancellation while usage is busy and global shutdown during `LoadModel` retain their distinct existing token behavior;
- successful loading, status timing, selection, retry, reservation, and refusal behavior remain compatible;
- the production patch is confined to `src/Backends/BackendHandler.cs`; and
- static review and the maintainer matrix pass without an agent runtime claim.

## Rollback

Rollback is one bounded ownership change in `BackendHandler.LoadHighestPressureNow`: remove the pre-publication lifecycle gate/captured-claims owner, outer publication/setup catch, delegate-entry arbitration, and four-argument cancellation-only continuation; restore the original claim publication, `Task.Factory.StartNew(action, cancel)`, delegate-local cleanup block, and post-finally `ReassignLoadedModelsList`.

No data migration, configuration rollback, public API restoration, browser change, or extension rebuild contract is involved. A partial rollback is invalid because retaining multiple cleanup callers without their shared gate can double-dispose claims or mutate state twice, while retaining the gate without complete exit-path coverage can restore the leak.
