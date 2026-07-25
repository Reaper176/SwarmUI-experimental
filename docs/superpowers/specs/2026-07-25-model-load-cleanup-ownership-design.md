# Model-Load Cleanup Ownership Design

**Status:** Approved for implementation; awaiting plan

**Date:** 2026-07-25

**Fixed design boundary:** `513367f33d25eb482ac478c59511e12fd3ea63a0`

## Purpose

Make the state published for a scheduled backend model load return to baseline exactly once when the scheduling caller's cancellation prevents the scheduled delegate from starting or a later publication/scheduling setup step fails.

At the fixed boundary, `BackendHandler.LoadHighestPressureNow` sets the globally selected `ModelRequestPressure.IsLoading`, creates one `Session.GenClaim(modelLoads: 1)` for every session recorded on that selected pressure, and then schedules all remaining work through cancellation-gated `Task.Factory.StartNew`. The delegate owns reservation, load execution, failure classification, claim disposal, and loaded-model reassignment. If the scheduling caller's token is already canceled, or becomes canceled before the delegate starts, the task can enter the canceled state without executing that owner. The selected pressure can remain marked as loading and its session claims and `LoadingModels` counters can remain published indefinitely.

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

`T2IBackendRequest.ReleasePressure` separately owns each request's pressure-count release. If the request's `Pressure` is already null it returns without mutation. Otherwise it decrements/removes that pressure and clears the request's reference; when called with `failed: true` and a non-null `UserInput`, it then appends the generic `"All backends failed to load model."` entry to that request's `UserInput.RefusalReasons`. `T2IBackendRequest.Complete` invokes `ReleasePressure(false)` when a waiting request ends, so ordinary completion does not add that generic refusal. Neither method owns the model-load claims created for the selected pressure, and this project will not transfer that ownership to it.

`Session.GenClaim.Dispose` removes the claim from `Session.Claims`, subtracts its counters, and disposes its local cancellation source. It is therefore the correct existing primitive for retracting each published model-load claim, but it must have one effective owner.

## Existing Scheduling-Caller and Selected-Pressure Coupling

`T2IBackendRequest.TryFind` calls:

```csharp
Handler.LoadHighestPressureNow(possible, available, () => ReleasePressure(true), Pressure, Cancel);
```

The `cancel` token and `releasePressure` callback therefore belong to the request whose `TryFind` invocation happened to call the method. This document calls that request the **scheduling caller** and calls `cancel` the **scheduling-caller token**.

`LoadHighestPressureNow` does not necessarily load that caller's `pressure`. It independently orders all eligible entries in `ModelRequests` and assigns `highestPressure` from the global result. This document calls that entry the **selected pressure**. The scheduling caller can belong to the selected pressure, but it can also belong to a different model/pressure with different requests and sessions.

The selected pressure owns:

- `highestPressure.IsLoading`;
- the model being loaded;
- the session set from which model-load claims are created;
- the user input passed from `highestPressure.Requests.FirstOrDefault()` to `LoadModel`;
- `BackendFailReasons` and `BadBackends`; and
- backend retry/exclusion state and the model/content inputs used to construct the detailed all-loaders-failed readable exception.

The scheduling caller owns:

- the `cancel` token supplied to `Task.Factory.StartNew` and `LoadModel(...).Wait(cancel)`;
- the passed `releasePressure` callback, including immediate return for null caller `Pressure` or, otherwise, pressure decrement/removal/null followed by generic refusal mutation only for non-null caller `UserInput`; and
- the caller-local `Pressure.IsLoading` check that controls its immediate `NotifyWillLoad` callback after `LoadHighestPressureNow` returns;
- the exception that propagates out of the scheduling caller's `TryFind`; and
- `RequestHandlingLoop` assignment of that exception to the scheduling caller's `Failure`, followed by `GetNextT2IBackend` logging and throwing that caller-owned failure to its waiter.

This coupling is existing scheduling policy and is preserved. Rank 11 does not make the selected pressure provide its own token/callback, choose a representative selected request, or realign status notification. Cleanup always retracts the selected `highestPressure.IsLoading` and the successfully captured claims created from that selected pressure's sessions. Cleanup does not call `releasePressure`, append the generic caller refusal, release the scheduling caller's pressure, or classify that different pressure merely because its request supplied the scheduling token.

The pre-scheduling all-loaders-failed branch retains its existing `releasePressure()` callback coupling. If scheduling caller A differs from selected pressure B and A's `Pressure` is non-null on callback entry, the callback first decrements/removes that pressure and sets `A.Pressure = null`; only after that, when `A.UserInput` is also non-null, it appends `"All backends failed to load model."` to A's `UserInput.RefusalReasons`. If A's `Pressure` is null on entry, the callback returns immediately and does not append the generic refusal even when `UserInput` exists.

B retains `BackendFailReasons`, `BadBackends`, and backend retry/exclusion state, and B's model/reasons supply the content of the detailed readable exception. The exception itself is not stored as selected-pressure state. It propagates out of A's `TryFind`; `RequestHandlingLoop` catches it and assigns `A.Failure`; `GetNextT2IBackend` later logs and throws `A.Failure` to A's waiter. That split behavior is outside the cleanup owner and is neither expanded nor corrected here.

## Selected Approach

Introduce one cleanup owner local to the scheduled load. Its concrete implementation may be a private local function or a small private helper, provided it retains the boundary and semantics in this document. Construct its atomic lifecycle gate and empty captured-claims list before setting `highestPressure.IsLoading` or constructing any claim.

The owner uses one interlocked lifecycle gate that distinguishes unstarted published work, a started delegate, pre-start cleanup ownership, and completed started cleanup. Delegate entry and pre-start cleanup must compete atomically on that same state:

- **Started cleanup** is invoked from the delegate's `finally`.
- **Pre-start cancellation cleanup** is invoked by the four-argument `ContinueWith` overload with `CancellationToken.None`, `TaskContinuationOptions.OnlyOnCanceled | TaskContinuationOptions.ExecuteSynchronously`, and `TaskScheduler.Default`.
- **Publication/setup failure cleanup** is invoked from an outer catch around `IsLoading` publication, claim construction/capture, `Task.Factory.StartNew`, and cancellation-continuation attachment.

The original `Task.Factory.StartNew(action, cancel)` call and delegate policy remain in place. The design does not replace it with unconditional scheduling, `Task.Run`, an async delegate, or a different scheduler.

The cancellation continuation is deliberately non-cancelable. Reusing the already-canceled scheduling-caller token for cleanup would reproduce the defect by allowing cleanup itself to be skipped. `OnlyOnCanceled` prevents the continuation from running for normal, faulted, or successfully completed started work. `ExecuteSynchronously` requests synchronous execution when the selected scheduler permits; it is a synchronous-preferred hint, not a guarantee of inline execution. `TaskScheduler.Default` prevents an ambient/custom current scheduler from delaying or rejecting the cleanup continuation, while the original `StartNew(action, cancel)` deliberately retains its current scheduler selection.

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
- invoke `releasePressure` or append its generic caller refusal, because cleanup does not own the scheduling caller's pressure/UserInput and the existing pre-scheduling all-loaders-failed callback remains separate;
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

The started policy remains authoritative after delegate entry, including a global-shutdown return while waiting for backend usages or cancellation of the scheduling-caller token observed by `LoadModel(...).Wait(cancel)`. Those paths began work for the selected pressure, including ownership of the reservation, so they retain the established reservation release, selected-pressure mismatch/failure classification, selected-pressure claim disposal, and model-list refresh.

Scheduling-caller cancellation alone does not interrupt the existing busy-backend loop. That loop observes only `Program.GlobalProgramCancel`. If the scheduling-caller token is canceled while the backend remains busy, the reservation, selected pressure's `IsLoading`, and selected-pressure session claims remain published as they do now. After usage frees, the ordinary-model branch calls `LoadModel` for the selected pressure and its subsequent `Wait(cancel)` observes the scheduling-caller token; the `(none)` branch has no scheduling-token wait and retains its existing behavior. This is true whether the scheduling caller belongs to the selected pressure or a different pressure. This project adds no immediate cleanup, wake, or progress guarantee for scheduling-caller cancellation while the backend is still busy.

Global shutdown during `LoadModel` also does not independently cancel its `Wait(cancel)`: that wait is governed by the passed scheduling-caller token, which may belong to a request outside the selected pressure. If that token is canceled, the existing catch records its readable failure and the finalization policy classifies model mismatch on the selected pressure; otherwise started cleanup occurs only when the existing load/wait exits normally or with an exception. The underlying asynchronous `LoadModel` continuation remains outside this project.

## State and Transition Table

| State/trigger | Work known to have started? | Cleanup caller | Effective cleanup | Explicitly unchanged/not performed |
|---|---:|---|---|---|
| All loaders already failed; scheduling caller belongs to selected pressure | No | Existing `releasePressure()` callback before cleanup owner exists | If caller `Pressure` is non-null, decrement/remove/null it and then add generic refusal only if `UserInput` is non-null; construct detailed exception from selected model/reasons | Exception escapes caller `TryFind`, becomes caller `Failure`, and is delivered to caller waiter; no selected exception state or Rank 11 cleanup |
| All loaders already failed; scheduling caller A differs from selected pressure B | No | Same existing callback | If `A.Pressure` is non-null, decrement/remove/null it and then conditionally add A's generic refusal; construct detail from B model/reasons | Exception escapes A's `TryFind`, is assigned to `A.Failure`, and is delivered to A; B retains only reasons/exclusions/retry inputs, not exception state |
| Pressure/backend selected; gate and empty captured-claims list created | No | None yet | No published state exists | Heuristic, selected backend, request pressure, and session set remain unchanged |
| `IsLoading` publishes; zero or more claims successfully return and are captured | No | None yet | Outer setup try continues | Claim/status timing remains before scheduling |
| A later claim construction/capture or setup step throws before a task starts | No | Outer setup catch | Atomically acquire pre-start cleanup; clear published `IsLoading`; dispose all successfully returned/captured claims; rethrow | No guarantee for mutation internal to a `GenClaim` constructor that throws before returning; no load classification |
| `Task.Factory.StartNew` throws before returning a task | No | Outer setup catch | Atomically acquire pre-start cleanup; clear `IsLoading`; dispose captured claims; rethrow | No reservation clear, backend failure classification, pressure release, or model-list refresh |
| Four-argument continuation attachment throws before delegate entry | No | Outer setup catch | Atomically acquire pre-start cleanup; clear `IsLoading`; dispose captured claims; rethrow; any later delegate entry observes cleanup ownership and exits | Original task is not treated as a backend attempt |
| Continuation attachment throws after delegate entry wins | Yes | Outer setup catch and later delegate `finally` | Catch cannot acquire pre-start cleanup; rethrow setup exception; delegate retains full started cleanup ownership | No premature claim disposal or `IsLoading` reset while load work is active |
| Scheduling caller belongs to selected pressure; its token cancels task before delegate entry | No | Non-cancelable `OnlyOnCanceled \| ExecuteSynchronously` continuation on `TaskScheduler.Default` | Atomically acquire pre-start cleanup; clear selected `IsLoading`; dispose selected-pressure captured claims | Scheduling caller retains its own request-pressure release; no load classification or refresh |
| Scheduling caller belongs to a different pressure; its token cancels selected pressure's task before delegate entry | No | Same cancellation continuation | Atomically acquire pre-start cleanup for the selected pressure; clear selected `IsLoading`; dispose selected-pressure captured claims | Do not release/classify the scheduling caller's different pressure or append its generic refusal; caller-local status ownership remains unchanged |
| A returned task's delegate is dispatched after pre-start cleanup won | No | Delegate entry | Atomic started transition fails; exit without reservation or model-load work | No second cleanup invocation mutates state |
| Selected-pressure scheduling caller's token is canceled while delegate waits for backend usage | Yes | None at cancellation time | No immediate transition; existing busy wait continues until usage frees or global shutdown is observed | Busy loop observes only `Program.GlobalProgramCancel`; selected status/claims remain published |
| Different-pressure scheduling caller's token is canceled while selected-pressure delegate waits for backend usage | Yes | None at cancellation time | Same: no immediate transition; selected load continues waiting | The unrelated caller's cancellation does not retract selected state until existing load flow later exits |
| Global shutdown is observed while the started delegate waits for backend usage | Yes | Delegate `finally` after existing early return | Win started-cleanup transition; run full started cleanup in existing order, including model-list reassignment | Existing global-cancellation check and wait cadence remain unchanged |
| Selected-pressure scheduling caller's token cancels `LoadModel(...).Wait(cancel)` | Yes | Delegate `finally` after existing catch | Preserve readable failure and mismatch classification on selected pressure, then full selected cleanup | Scheduling caller's request-pressure release remains separately owned |
| Different-pressure scheduling caller's token cancels selected pressure's `LoadModel(...).Wait(cancel)` | Yes | Delegate `finally` after existing catch | Preserve existing coupling: record backend failure/mismatch and retry/exclusion inputs on selected pressure, then clear selected state/claims | Cleanup does not release caller pressure, add generic refusal, construct/deliver the later detailed failure, or assign caller `Failure` |
| Global shutdown occurs during `LoadModel` while scheduling-caller token is not canceled | Yes | Delegate `finally` only after existing load/wait exits | No immediate transition from global shutdown alone; then full selected started cleanup | `Wait(cancel)` is governed by scheduling-caller cancellation, not `Program.GlobalProgramCancel`, regardless of pressure identity |
| Delegate loads `(none)` or the requested model successfully | Yes | Delegate `finally` | Win gate; full started cleanup; model-name match avoids bad-backend classification | Successful selection/load logging and later request progress remain unchanged |
| Delegate catches a backend load failure or ends with model mismatch | Yes | Delegate `finally` | Win gate; preserve selected-pressure reason/mismatch classification and full started cleanup | No `ReleasePressure(true)`, generic refusal, all-loaders exception construction, caller `Failure` assignment, or delivery here |
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

When the scheduling caller and selected pressure differ, those effects apply to different owners: caller completion releases only the caller's request pressure, while pre-start or started cleanup retracts only the globally selected pressure's `IsLoading` and captured session claims. Selected-pressure failure reasons, bad-backend exclusions, retry, and detailed-error content inputs remain selected-pressure state even when cancellation came from the different scheduling caller's token. The resulting all-loaders-failed exception is caller-owned after it escapes `TryFind`: `RequestHandlingLoop` assigns it to the caller's `Failure`, and `GetNextT2IBackend` delivers it to that caller's waiter. No detailed-exception state is stored on the selected pressure.

The generic `"All backends failed to load model."` refusal is scheduling-caller `UserInput` state only when the separate existing `ReleasePressure(true)` callback enters with non-null caller `Pressure`, performs its decrement/removal/null assignment, and then finds non-null `UserInput`. A null caller `Pressure` causes immediate return before the generic append. Cleanup never uses token identity as a reason to classify/release the token owner's pressure, append that generic refusal, assign caller `Failure`, or deliver the detailed exception.

Immediate status notification also remains caller-local. After `LoadHighestPressureNow` returns, `TryFind` checks the scheduling caller's `Pressure.IsLoading`, not the globally selected `highestPressure.IsLoading`. Rank 11 neither transfers `NotifyWillLoad` to the selected pressure nor promises the scheduling caller receives a load notification when its invocation scheduled another pressure.

## Preserved Behavior and Compatibility

Implementation must preserve:

- the pressure heuristic, including count weighting and first-request wait time;
- pressure filtering, all-request match preference, bad-backend exclusion, and backend selection order;
- the 1.5-second pressure-age rule when multiple loaders are available;
- the configured `last_used` and `first_free` model-load order behavior and invalid-setting error;
- cancellation-gated `Task.Factory.StartNew(action, cancel)` with the scheduling caller's token and its current scheduler selection;
- cleanup-continuation dispatch through the explicit four-argument overload on `TaskScheduler.Default`;
- the existing 100-millisecond usage-wait polling and one-second scheduler/handler behavior;
- the fact that the busy-usage loop observes global shutdown but not scheduling-caller cancellation;
- global-shutdown return behavior while waiting for usages and the lack of a new global-shutdown interrupt for an active `Wait(cancel)`;
- scheduling-caller-token behavior during the selected pressure's `LoadModel(...).Wait(cancel)`, including when those owners differ;
- the timing of `highestPressure.IsLoading` and session model-load claim publication before scheduling;
- caller-local `NotifyWillLoad`, selected-pressure session status, and their existing timing/ownership when the caller and selected pressure differ;
- backend reservation ownership after delegate entry;
- readable load-failure logging, selected-pressure `BackendFailReasons`/`BadBackends`, backend retry/exclusion, and selected model/reason content used to construct the detailed all-loaders-failed exception;
- scheduling-caller `Failure` assignment in `RequestHandlingLoop` and its later delivery by `GetNextT2IBackend`;
- each request's ownership of pressure release and generic `UserInput.RefusalReasons` mutation only when `ReleasePressure(true)` enters with non-null `Pressure` and then observes non-null `UserInput`;
- successful model selection/loading and subsequent waiter progress;
- public methods, fields, signatures, types, and external extension ABI; and
- repository C# conventions, including explicit types, full braced blocks, and XML documentation for any new field if a field is required.

This is a reliability correction, not a performance optimization. It makes no throughput, latency, memory, or scheduling-fairness claim.

## Explicit Non-Goals

This project does not:

- address the adjacent `LoadModelOnAll` shutdown/reservation issue;
- clean stale `ModelRequestPressure.Requests` or `Sessions`;
- realign the scheduling-caller token, `releasePressure` callback, generic caller refusal, `Failure` delivery, or `NotifyWillLoad` with the globally selected pressure;
- redesign cancellation responsiveness while waiting for backend usages;
- alter the underlying asynchronous `AbstractT2IBackend.LoadModel` continuation or cancellation contract;
- make scheduling-caller cancellation interrupt the busy-backend usage loop or make global shutdown independently interrupt `Wait(cancel)`;
- optimize or instrument the scheduler under P5;
- change pressure scoring, backend preference, retries, autoscaling, or the one-second handler loop;
- move request-pressure release into model-load cleanup;
- redesign `Session.GenClaim` or make its general `Dispose` implementation idempotent;
- introduce a broader scheduler/lifecycle service;
- change browser, API, backend persistence, launcher, or extension code; or
- claim that the static review proves runtime thread interleavings.

## Implementation Stages

1. In `LoadHighestPressureNow`, establish one atomic lifecycle/cleanup gate and an empty captured-claims list for the globally selected `highestPressure` before publishing its `IsLoading` or constructing its session claims; do not derive cleanup ownership from the scheduling-caller token.
2. Wrap `IsLoading` publication, immediate capture of each successfully returned claim, `Task.Factory.StartNew(action, cancel)`, and cleanup-continuation attachment in one outer try/catch. On failure, atomically select pre-start cleanup or defer to already-started ownership, then rethrow.
3. Move the current delegate-finalization actions into the started policy while retaining their existing ordering, locking, selected-pressure messages/classification, and `ReassignLoadedModelsList` placement. Make delegate entry atomically decline work if pre-start cleanup already won, and do not add `releasePressure`, generic caller refusal, caller `Failure` assignment, or failure delivery to cleanup.
4. Attach the pre-start path with the four-argument `ContinueWith` overload using `CancellationToken.None`, `TaskContinuationOptions.OnlyOnCanceled | TaskContinuationOptions.ExecuteSynchronously`, and `TaskScheduler.Default`; retain the original `StartNew(action, cancel)` scheduler and token behavior.
5. Perform the bounded static inventory and control-flow review before asking the maintainer to build or exercise runtime cancellation.

The production patch should change only `src/Backends/BackendHandler.cs`. Documentation updates after implementation and validation are separate records.

## Static Verification

Agents will not build, launch, run tests, start the server, load a model, interrupt a request, or perform any other runtime validation. Static verification must:

1. confirm the production diff changes only `src/Backends/BackendHandler.cs`;
2. prove `TryFind` supplies its own `Cancel` and `ReleasePressure(true)` callback while `LoadHighestPressureNow` independently selects `highestPressure` from global `ModelRequests`; confirm `ReleasePressure(true)` returns without mutation when caller `Pressure` is null, otherwise decrements/removes/nulls it before conditionally adding the exact generic refusal only when `UserInput` is non-null;
3. enumerate every assignment to selected-pressure `IsLoading`, loader `ReserveModelLoad`, selected-pressure `BadBackends`/`BackendFailReasons`, and every creation/disposal of selected-pressure captured claims;
4. prove the lifecycle gate and captured-claims list exist before `IsLoading` or claim publication, and capture of each returned claim is attempted immediately before later work;
5. trace partial publication when first/later claim construction or capture/setup fails, explicitly bounding recovery to successfully returned/captured claims and not claiming recovery from an internal constructor exception before return;
6. trace scheduling through `StartNew` throw, continuation-attachment throw before delegate entry, continuation-attachment throw after delegate entry, pre-start task cancellation, and delegate dispatch after pre-start cleanup;
7. prove the interlocked lifecycle transitions prevent backend work after pre-start cleanup and prevent pre-start cleanup from retracting state after delegate entry;
8. prove every post-publication path reaches one cleanup caller and every losing/repeated invocation returns without mutation;
9. prove pre-start cleanup resets only selected `highestPressure.IsLoading` and disposes only its captured session claims;
10. prove started cleanup preserves reservation release, selected-pressure mismatch classification, selected `IsLoading` reset, selected claim disposal, and `ReassignLoadedModelsList` in the existing order;
11. separately trace scheduling-caller token owner equal to and different from selected pressure for pre-start cancellation, busy-usage waiting, cancellation at `LoadModel(...).Wait(cancel)`, load success/failure, selected backend retry/detail-input ownership, and caller generic-refusal/`Failure` delivery ownership;
12. separately trace global shutdown while waiting for usages and during `LoadModel` with and without cancellation of the scheduling-caller token;
13. prove the busy loop still observes only `Program.GlobalProgramCancel`, while the load wait still observes only the scheduling-caller `cancel`, with no new immediate cleanup or progress guarantee;
14. prove exception unwrapping, logs, selected-pressure `BackendFailReasons`/`BadBackends`, backend retry/exclusion, and selected model/reason inputs for the detailed all-loaders-failed exception remain unchanged;
15. prove neither cleanup policy invokes `releasePressure`, appends `"All backends failed to load model."`, constructs/delivers the all-loaders exception, assigns caller `Failure`, or classifies/releases a different scheduling-token owner's pressure;
16. prove the pre-scheduling all-loaders-failed branch retains its existing scheduling-caller `ReleasePressure(true)` callback even when `highestPressure` differs: null caller `Pressure` returns before every mutation; otherwise caller pressure is decremented/removed/null before caller `UserInput.RefusalReasons` conditionally gains the exact generic text;
17. trace the detailed exception constructed from selected model/reasons out of scheduling caller `TryFind`, into `RequestHandlingLoop`'s scheduling-caller `Failure` assignment, and through `GetNextT2IBackend` logging/throwing to that caller's waiter; prove no selected-pressure exception state is introduced;
18. prove caller-local `NotifyWillLoad`, generic refusal, and `Failure` delivery ownership, plus selected-pressure session status, backend failure reasons, retry/exclusion, and detailed-error input ownership remain unchanged when caller and selected pressure differ;
19. prove the original scheduling-caller token and scheduler behavior remain on `Task.Factory.StartNew(action, cancel)` and `LoadModel(...).Wait(cancel)`;
20. prove cleanup attachment uses the four-argument `ContinueWith` overload with `CancellationToken.None`, `OnlyOnCanceled | ExecuteSynchronously`, and `TaskScheduler.Default`, without claiming `ExecuteSynchronously` guarantees inline execution;
21. prove pressure ordering/filtering, backend selection, reservation acquisition, wait cadence, successful loading, the scheduler loop, and public API/ABI are unchanged;
22. confirm no change to `LoadModelOnAll`, `Session.GenClaim`, browser code, launchers, extensions, generated content, user data, or excluded adjacent findings;
23. inspect the exact commit/file range; and
24. run `git diff --check`.

Static review can establish ownership, branch ordering, token placement, call-site scope, and signature preservation. It cannot prove the cancellation race, runtime counter values, backend behavior, thread timing, performance, or platform behavior.

## Maintainer Validation Matrix

The maintainer will build and run the live software. For every case, identify both the scheduling caller/token owner and the globally selected pressure. Record both pressure counts, the selected pressure's `IsLoading`, the selected sessions' `Claims` membership and `LoadingModels`, the loader's reservation/availability, selected backend failure/retry/detail-input state, caller `UserInput.RefusalReasons`, caller `Failure` assignment/delivery, caller-local status notification, and whether later requests for each pressure progress.

1. **Partial publication/setup failure:** inject a failure after `IsLoading`, after one or more claims have successfully returned and been captured, at `StartNew`, and during continuation attachment before delegate entry. Confirm `IsLoading`, every captured claim, and corresponding `LoadingModels` return to baseline exactly once; no backend is classified or refreshed; and the original setup exception propagates. Do not treat an exception injected inside `GenClaim` construction before it returns as transactionally recoverable by this owner.
2. **Continuation-attachment/delegate race:** inject attachment failure as the delegate enters. Confirm either pre-start cleanup wins and the later delegate performs no reservation/load work, or started ownership wins and the delegate performs full cleanup; never observe premature claim disposal, duplicate cleanup, or active work after pre-start ownership.
3. **Pre-start cancellation, token owner equals selected pressure:** exercise sole and shared selected pressure. Cancel the scheduling caller in the window after selected `IsLoading`/claims publish but before delegate entry. Confirm selected state and captured claims return to baseline exactly once, the caller retains separate request-pressure release, no backend is classified/refreshed, and sole/shared later requests progress.
4. **Pre-start cancellation, token owner differs from selected pressure:** arrange caller pressure A to invoke scheduling while global pressure B is selected, then cancel A's token before delegate entry. Confirm cleanup clears only B's `IsLoading` and captured session claims; it neither releases nor classifies A's pressure; B can schedule a replacement load; and caller-local notification/status remains under the existing A-side check.
5. **Busy wait, token owner equals selected pressure:** hold the loader busy, start the delegate, and cancel the selected-pressure caller token. Confirm there is no immediate cleanup, wake, or progress change while usage remains busy. After usage frees, confirm the ordinary-model branch reaches existing `LoadModel(...).Wait(cancel)` cancellation and full selected cleanup. Separately confirm `(none)` retains its no-scheduling-token-wait behavior.
6. **Busy wait, token owner differs from selected pressure:** hold the loader busy while caller pressure A's token governs selected pressure B's task, then cancel A. Confirm B's reservation, `IsLoading`, claims, and visible loading state remain until usage frees or global shutdown; A's cancellation does not immediately release/classify B or cause cleanup to mutate A; after release, existing B load/failure/retry ownership is preserved.
7. **Global shutdown while waiting for usage:** exercise both matching and different token/selected-pressure ownership. Confirm the existing busy-loop check—not either caller token—causes return and one full selected-pressure cleanup with the established reservation release, selected mismatch classification, counter cleanup, and model-list refresh.
8. **Load cancellation, token owner equals selected pressure:** cancel the scheduling-caller token after the selected model's `LoadModel` begins. Confirm existing selected-pressure `BackendFailReasons` and mismatch behavior, full cleanup, no duplicate counter decrement, and eventual retry/request progress, without claiming the underlying asynchronous load was canceled.
9. **Load cancellation, token owner differs from selected pressure:** arrange caller pressure A's token to govern selected pressure B's `LoadModel(...).Wait(cancel)`, then cancel A. Confirm the existing coupling records backend failure/mismatch and retry/exclusion/detailed-error inputs on B, cleanup clears B's loading state/claims, and cleanup neither classifies/releases A, appends A's generic refusal, assigns `A.Failure`, nor delivers an all-loaders exception merely because A supplied the token.
10. **Global shutdown during `LoadModel`:** for both matching and different token/selected-pressure ownership, leave the scheduling-caller token uncanceled and confirm global shutdown alone does not newly interrupt `Wait(cancel)`; cleanup occurs when the existing load/wait exits. Then cancel the scheduling-caller token during shutdown and confirm the same existing token-governed catch/finally behavior. Every case performs one selected-pressure cleanup.
11. **Successful load:** exercise ordinary model loading and `(none)` with matching and different scheduling-token ownership. Confirm selected model/status, reservation lifetime, loaded-model reassignment, selected claim/counter return, caller-local `NotifyWillLoad` behavior, and later requests for both pressures are unchanged.
12. **Backend load failure and retry/detail ownership:** force a readable failure and model-name mismatch with matching and different scheduling-token ownership. Confirm each reason and bad-backend entry appears once only on the selected pressure, selected cleanup returns to baseline once, selected retry/exclusion and later detailed error inputs remain selected-model state, and cleanup adds no generic caller refusal.
13. **Existing callback, refusal, and failure-delivery coupling:** arrange active scheduling caller pressure A to invoke the all-loaders-failed branch while global pressure B is selected. Confirm `ReleasePressure(true)` decrements/removes/nulls A's pressure before, when `A.UserInput` is non-null, appending exactly `"All backends failed to load model."` to A's `RefusalReasons`; repeat with null `UserInput` and confirm pressure release without generic mutation. Then instrument null `A.Pressure` with non-null `UserInput` and confirm immediate callback return with neither pressure nor generic mutation. Confirm B retains `BackendFailReasons`, `BadBackends`, retry/exclusion, and detailed-error model/content inputs, but no exception state. Confirm the detailed B-content exception escapes A's `TryFind`, is assigned to `A.Failure`, and is delivered/thrown to A's waiter. Confirm neither cleanup policy adds another release, generic refusal, failure assignment/delivery, or classification. This case records existing behavior; it does not approve a redesign.
14. **Repeated cleanup pressure:** stress cancellation close to delegate dispatch for both matching and different token owners. Confirm no negative selected-session `LoadingModels`, duplicate claim removal/disposal symptom, duplicate bad-backend classification/refresh, unintended token-owner pressure mutation, or permanent selected `IsLoading`.
15. **Compatibility flow:** run normal generation with sole/shared and competing-model pressure. Observe pressure order, globally selected loader/model, caller-local status frames/`NotifyWillLoad`, selected-session loading status, selected backend retry/detail inputs, caller generic refusal and detailed-failure delivery, final images, subsequent generations, and pre-start/started shutdown behavior.

Linux, Windows, and other-platform runtime results must be recorded separately. No runtime or performance result is implied by implementation or static review.

## Success Criteria

Rank 11 succeeds when:

- pre-start cancellation or scheduling/setup failure cannot leave `IsLoading` or any successfully returned/captured claim and its `LoadingModels` contribution stranded;
- failure after publication retracts `IsLoading` and every successfully returned/captured claim available to the owner, without overstating recovery from an internal claim-construction exception before return;
- the complete cleanup unit executes effectively once for every scheduled attempt;
- continuation-attachment failure cannot allow both pre-start cleanup and active delegate work;
- pre-start cleanup does not mutate state owned only by a started load;
- started cleanup and failure reasoning retain their current order and semantics;
- cleanup always retracts the globally selected pressure's `IsLoading` and successfully captured session claims, regardless of which request supplied `cancel`;
- cleanup never releases or classifies a different scheduling-token owner's pressure, appends its generic refusal, assigns its `Failure`, or delivers the detailed exception;
- the separate all-loaders-failed callback returns without mutation for null caller `Pressure`; otherwise it decrements/removes/nulls caller pressure before conditionally appending the generic refusal for non-null `UserInput`;
- sole and shared pressure requests can make later progress;
- matching and different scheduling-token/selected-pressure cases retain caller-local status/generic-refusal/`Failure` delivery ownership and selected-pressure backend failure/retry/detailed-error-input ownership;
- scheduling-caller cancellation while usage is busy and global shutdown during `LoadModel` retain their distinct existing token behavior;
- successful loading, status timing, selection, retry, reservation, and refusal behavior remain compatible;
- the production patch is confined to `src/Backends/BackendHandler.cs`; and
- static review and the maintainer matrix pass without an agent runtime claim.

## Rollback

Rollback is one bounded ownership change in `BackendHandler.LoadHighestPressureNow`: remove the pre-publication lifecycle gate/captured-claims owner, outer publication/setup catch, delegate-entry arbitration, and four-argument cancellation-only continuation; restore the original claim publication, `Task.Factory.StartNew(action, cancel)`, delegate-local cleanup block, and post-finally `ReassignLoadedModelsList`.

No data migration, configuration rollback, public API restoration, browser change, extension rebuild contract, or scheduling-token/selected-pressure realignment is involved. The original scheduling caller still supplies `cancel` and `releasePressure`; `ReleasePressure(true)` still immediately returns for null caller `Pressure`, otherwise releases/nulls caller pressure before conditionally adding the generic caller refusal for non-null `UserInput`; global selection still owns model/backend failure/retry/detail inputs; and the thrown detailed exception still becomes the scheduling caller's `Failure` and is delivered to that caller. A partial rollback is invalid because retaining multiple cleanup callers without their shared gate can double-dispose selected-pressure claims or mutate selected state twice, while retaining the gate without complete exit-path coverage can restore the leak.
