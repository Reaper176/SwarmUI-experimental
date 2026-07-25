# Model-Load Cleanup Ownership Design

**Status:** Implemented; awaiting maintainer validation

**Date:** 2026-07-25

**Original fixed design boundary:** `513367f33d25eb482ac478c59511e12fd3ea63a0`

**Final reviewed design boundary:** `c67c0b8b64f4570b09cd2f36dca22532b52ce109`

**Final reviewed implementation plan:** `42467376b9da88bfe8ae2760c81929ec400bd072`

**Production range:** `42467376..25859d6e` (one production commit, `25859d6e`)

## Purpose

Make cleanup ownership unavoidable and exclusive when the scheduling caller's cancellation prevents the scheduled delegate from starting or a later publication/scheduling setup step fails. For state successfully returned/captured by the owner, baseline is restored through one ordered attempt when its cleanup primitives succeed.

At the fixed boundary, `BackendHandler.LoadHighestPressureNow` sets the globally selected `ModelRequestPressure.IsLoading`, creates one `Session.GenClaim(modelLoads: 1)` for every session recorded on that selected pressure, and then schedules all remaining work through cancellation-gated `Task.Factory.StartNew`. The delegate owns reservation, load execution, failure classification, claim disposal, and loaded-model reassignment. If the scheduling caller's token is already canceled, or becomes canceled before the delegate starts, the task can enter the canceled state without executing that owner. The selected pressure can remain marked as loading and its session claims and `LoadingModels` counters can remain published indefinitely.

The selected design retains the cancellation-gated scheduling policy and introduces one cleanup owner backed by an atomic lifecycle gate. The gate and captured-claims list exist before `IsLoading` or any claim is published. The started delegate, a cancellation-only synchronous-preferred continuation, and the publication/scheduling-setup failure path all coordinate through that owner. Only the path that atomically acquires the applicable cleanup-ownership transition attempts cleanup, and the ordered actions depend on whether model-load work actually started.

This guarantee covers every `Session.GenClaim` instance that construction successfully returns and the implementation captures. It cannot transactionally recover counters or other mutations if construction throws before return. If cleanup `Dispose` throws, the Rank 11 cleanup owner does not retry it; it separately attempts `GC.SuppressFinalize(claim)` so the finalizer cannot normally repeat a partial counter mutation. Failed disposal may retain partial claim/resource/session state, and failed finalizer suppression leaves a later finalizer-invoked `Dispose` possible. Neither condition has a transactional baseline guarantee.

## Boundary and Pre-Implementation Ownership

The production boundary is one method in one file:

- `BackendHandler.LoadHighestPressureNow` in `src/Backends/BackendHandler.cs`.

At the original fixed design boundary, the method:

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

`Session.GenClaim.Dispose` subtracts counters, removes the claim, disposes its local cancellation source, and only then calls `GC.SuppressFinalize(this)`. Its finalizer calls `Dispose` again. A disposal exception before the final suppression can therefore leave the finalizer armed to repeat already-partial mutation. Rank 11 keeps `Session.cs` unchanged and handles this only for captured model-load claims in `BackendHandler`.

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

This coupling is existing scheduling policy and is preserved. Rank 11 does not make the selected pressure provide its own token/callback, choose a representative selected request, or realign status notification. Cleanup always attempts the selected `highestPressure.IsLoading` reset and one disposal per successfully captured claim from that selected pressure's sessions. Cleanup does not call `releasePressure`, append the generic caller refusal, release the scheduling caller's pressure, or classify that different pressure merely because its request supplied the scheduling token.

The pre-scheduling all-loaders-failed branch retains its existing `releasePressure()` callback coupling. If scheduling caller A differs from selected pressure B and A's `Pressure` is non-null on callback entry, the callback first decrements/removes that pressure and sets `A.Pressure = null`; only after that, when `A.UserInput` is also non-null, it appends `"All backends failed to load model."` to A's `UserInput.RefusalReasons`. If A's `Pressure` is null on entry, the callback returns immediately and does not append the generic refusal even when `UserInput` exists.

B retains `BackendFailReasons`, `BadBackends`, and backend retry/exclusion state, and B's model/reasons supply the content of the detailed readable exception. The exception itself is not stored as selected-pressure state. It propagates out of A's `TryFind`; `RequestHandlingLoop` catches it and assigns `A.Failure`; `GetNextT2IBackend` later logs and throws `A.Failure` to A's waiter. That split behavior is outside the cleanup owner and is neither expanded nor corrected here.

## Selected Approach

Introduce one cleanup owner local to the scheduled load. Its concrete implementation may be a private local function or a small private helper, provided it retains the boundary and semantics in this document. Construct its atomic lifecycle gate and empty captured-claims list before setting `highestPressure.IsLoading` or constructing any claim.

The owner uses one interlocked lifecycle gate that distinguishes unstarted published work, a started delegate, and two exclusive terminal cleanup-ownership states conceptually named `PreStartCleanupOwned` and `StartedCleanupOwned`. Delegate entry and pre-start cleanup compete atomically on that same state:

- **Started cleanup** is invoked from the delegate's `finally`.
- **Pre-start cancellation cleanup** is invoked by the four-argument `ContinueWith` overload with `CancellationToken.None`, `TaskContinuationOptions.OnlyOnCanceled | TaskContinuationOptions.ExecuteSynchronously`, and `TaskScheduler.Default`.
- **Publication/setup failure cleanup** is invoked from an outer catch around `IsLoading` publication, claim construction/capture, `Task.Factory.StartNew`, and cancellation-continuation attachment.

The original `Task.Factory.StartNew(action, cancel)` call and delegate policy remain in place. The design does not replace it with unconditional scheduling, `Task.Run`, an async delegate, or a different scheduler.

The two terminal values mean only that one path exclusively owns the ordered cleanup attempts. They do not mean that every cleanup primitive completed successfully or that baseline was transactionally restored.

The cancellation continuation is deliberately non-cancelable. Reusing the already-canceled scheduling-caller token for cleanup would reproduce the defect by allowing cleanup itself to be skipped. `OnlyOnCanceled` prevents the continuation from running for normal, faulted, or successfully completed started work. `ExecuteSynchronously` requests synchronous execution when the selected scheduler permits; it is a synchronous-preferred hint, not a guarantee of inline execution. `TaskScheduler.Default` prevents an ambient/custom current scheduler from delaying or rejecting the cleanup continuation, while the original `StartNew(action, cancel)` deliberately retains its current scheduler selection.

The outer catch performs pre-start cleanup only if it atomically wins before delegate entry. If the scheduled delegate already won the started transition—for example, if continuation attachment fails after `StartNew` returned and the delegate began—the catch leaves started cleanup to the delegate's `finally`. If pre-start cleanup wins after a task was returned but before its delegate enters, delegate entry observes `PreStartCleanupOwned` and exits without reserving or loading. The original setup/attachment exception is rethrown in every case and is not reinterpreted as a backend load failure.

Within the outer try, each successfully returned `GenClaim` is captured immediately. If a later claim construction, setup step, `StartNew`, or continuation attachment throws, the winning cleanup owner attempts `IsLoading` reset and one disposal per captured claim. If the delegate already started, it retains started cleanup ownership. This is best-effort recovery for state available to the owner, not a transactional redesign of claim construction or cleanup primitives.

Both cleanup functions must be non-throwing. A method-local best-effort cleanup-diagnostic helper encloses its complete diagnostic operation—including cleanup context/message construction, exception formatting, and the `Logs.Error` call—in its own try/catch and swallows any diagnostic failure. Every cleanup primitive, claim-disposal, and finalizer-suppression catch calls only this helper for its diagnostic; no direct throwable cleanup logging may escape a catch. Each independent ordered action and each individual captured `GenClaim.Dispose` still runs inside its own try/catch, and cleanup advances even when either the primitive or its diagnostic fails. After a claim's `Dispose` throws, the same claim receives one separate non-throwing `GC.SuppressFinalize(claim)` attempt before advancing to the next captured claim. A suppression failure receives the same best-effort diagnostic treatment; the Rank 11 cleanup owner does not call `Dispose` again. If suppression fails, a later finalizer-invoked `Dispose` remains possible. Pre-start cleanup therefore cannot replace the original publication/setup/attachment exception, and the cancellation continuation cannot fault because a cleanup primitive or cleanup diagnostic failed.

## Cleanup Policies

### Pre-start policy

If the delegate never starts, or publication/setup fails before delegate entry wins the lifecycle transition, the `PreStartCleanupOwned` path attempts only state published before model-load work, in this order:

1. set `highestPressure.IsLoading = false` if it was published; and
2. have the Rank 11 cleanup owner call `Dispose` once on every successfully returned and captured model-load claim, in captured order; after a thrown disposal, attempt `GC.SuppressFinalize` once for that claim before advancing.

The `IsLoading` attempt, every claim disposal, and every post-failure finalizer-suppression attempt are isolated by separate try/catch blocks. Each catch calls the non-throwing cleanup-diagnostic helper, whose own failure is swallowed, and later claims are still attempted. The Rank 11 cleanup owner does not dispose a failed claim again; if suppression fails, a later finalizer-invoked `Dispose` remains possible.

It must not:

- clear `availableBackend.ReserveModelLoad`, because the delegate never set it;
- add the backend to `BadBackends`;
- add a load reason to `BackendFailReasons`;
- invoke `releasePressure` or append its generic caller refusal, because cleanup does not own the scheduling caller's pressure/UserInput and the existing pre-scheduling all-loaders-failed callback remains separate;
- call `ReassignLoadedModelsList`, because no model load or model selection was attempted; or
- emit a model-load success, failure, or mismatch classification.

### Started policy

Once the delegate starts, the `StartedCleanupOwned` path preserves the existing action order and classification policy as ordered independent attempts:

1. clear `availableBackend.ReserveModelLoad`;
2. compare `availableBackend.Backend.CurrentModelName` with the requested model;
3. on mismatch, retain the existing warning and add the backend ID to `highestPressure.BadBackends` under the existing pressure lock;
4. set `highestPressure.IsLoading = false`;
5. have the Rank 11 cleanup owner call `Dispose` once on every captured model-load claim, in captured order, with one post-failure `GC.SuppressFinalize` attempt before advancing; and
6. call `ReassignLoadedModelsList`.

Reservation release, mismatch classification, `IsLoading` reset, each claim disposal, each required post-failure suppression, and model-list reassignment are isolated from one another with per-step try/catch. If any attempt throws, its catch calls the non-throwing cleanup-diagnostic helper and cleanup continues in order even if that diagnostic fails. The existing load-exception path remains responsible for unwrapping aggregate exceptions, logging the readable failure, and adding its readable text to `BackendFailReasons`; that existing non-cleanup logging path is not redirected through the cleanup helper.

`Session.GenClaim.Dispose` is not idempotent and its finalizer calls it again. The exclusive ownership state therefore permits at most one Rank 11 cleanup-owner `Dispose` call per captured instance. After a thrown disposal, cleanup attempts its diagnostic through the non-throwing helper and separately attempts `GC.SuppressFinalize(claim)` once even if that diagnostic fails; this is not a cleanup-owner disposal retry. Cleanup then continues to later claims and steps. Successful suppression trades an unsafe finalizer-invoked `Dispose`/double decrement for possible retained partial claim/resource/session state until later session/process cleanup. If suppression itself throws, its best-effort diagnostic cannot interrupt later cleanup, and a later finalizer-invoked `Dispose` cannot be ruled out. No transactional repair or baseline guarantee is made for the failed claim.

The started policy remains authoritative after delegate entry, including a global-shutdown return while waiting for backend usages or cancellation of the scheduling-caller token observed by `LoadModel(...).Wait(cancel)`. Those paths began work for the selected pressure, including ownership of the reservation, so they retain the established reservation release, selected-pressure mismatch/failure classification, selected-pressure claim disposal, and model-list refresh.

Scheduling-caller cancellation alone does not interrupt the existing busy-backend loop. That loop observes only `Program.GlobalProgramCancel`. If the scheduling-caller token is canceled while the backend remains busy, the reservation, selected pressure's `IsLoading`, and selected-pressure session claims remain published as they do now. After usage frees, the ordinary-model branch calls `LoadModel` for the selected pressure and its subsequent `Wait(cancel)` observes the scheduling-caller token; the `(none)` branch has no scheduling-token wait and retains its existing behavior. This is true whether the scheduling caller belongs to the selected pressure or a different pressure. This project adds no immediate cleanup, wake, or progress guarantee for scheduling-caller cancellation while the backend is still busy.

Global shutdown during `LoadModel` also does not independently cancel its `Wait(cancel)`: that wait is governed by the passed scheduling-caller token, which may belong to a request outside the selected pressure. If that token is canceled, the existing catch records its readable failure and the finalization policy classifies model mismatch on the selected pressure; otherwise started cleanup occurs only when the existing load/wait exits normally or with an exception. The underlying asynchronous `LoadModel` continuation remains outside this project.

## State and Transition Table

| State/trigger | Work known to have started? | Cleanup caller | Cleanup ownership/ordered attempts | Explicitly unchanged/not performed |
|---|---:|---|---|---|
| All loaders already failed; scheduling caller belongs to selected pressure | No | Existing `releasePressure()` callback before cleanup owner exists | If caller `Pressure` is non-null, decrement/remove/null it and then add generic refusal only if `UserInput` is non-null; construct detailed exception from selected model/reasons | Exception escapes caller `TryFind`, becomes caller `Failure`, and is delivered to caller waiter; no selected exception state or Rank 11 cleanup |
| All loaders already failed; scheduling caller A differs from selected pressure B | No | Same existing callback | If `A.Pressure` is non-null, decrement/remove/null it and then conditionally add A's generic refusal; construct detail from B model/reasons | Exception escapes A's `TryFind`, is assigned to `A.Failure`, and is delivered to A; B retains only reasons/exclusions/retry inputs, not exception state |
| Pressure/backend selected; gate and empty captured-claims list created | No | None yet | No published state exists | Heuristic, selected backend, request pressure, and session set remain unchanged |
| `IsLoading` publishes; zero or more claims successfully return and are captured | No | None yet | Outer setup try continues | Claim/status timing remains before scheduling |
| A later claim construction/capture or setup step throws before a task starts | No | Outer setup catch | Acquire `PreStartCleanupOwned`; make one cleanup-owner attempt for `IsLoading`, each captured disposal, and post-failure suppression where needed; route cleanup failures through the non-throwing diagnostic helper; rethrow original | Primitive and diagnostic failures never replace the original |
| `Task.Factory.StartNew` throws before returning a task | No | Outer setup catch | Acquire `PreStartCleanupOwned`; run the same non-throwing ordered attempts; rethrow original scheduling exception | No reservation attempt, backend failure classification, pressure release, or model-list refresh |
| Four-argument continuation attachment throws before delegate entry | No | Outer setup catch | Acquire `PreStartCleanupOwned`; non-throwingly attempt pre-start cleanup; rethrow original attachment exception; later delegate entry exits | Cleanup cannot replace original; task is not treated as a backend attempt |
| Continuation attachment throws after delegate entry wins | Yes | Outer setup catch and later delegate `finally` | Catch cannot acquire pre-start ownership and rethrows original; delegate later acquires `StartedCleanupOwned` and runs non-throwing ordered attempts | No premature pre-start cleanup while load work is active |
| Scheduling caller belongs to selected pressure; its token cancels task before delegate entry | No | Non-cancelable continuation on `TaskScheduler.Default` | Acquire `PreStartCleanupOwned`; make one cleanup-owner attempt for selected `IsLoading`, each disposal, and required post-failure suppression; diagnose failures only through the non-throwing helper | Primitive or diagnostic failure cannot fault the continuation |
| Scheduling caller belongs to a different pressure; its token cancels selected pressure's task before delegate entry | No | Same cancellation continuation | Acquire `PreStartCleanupOwned` for selected pressure; run the same non-throwing attempts | Do not release/classify caller pressure, append generic refusal, assign `Failure`, or fault continuation |
| A returned task's delegate is dispatched after pre-start cleanup ownership was acquired | No | Delegate entry | Atomic started transition fails; exit without reservation or model-load work | Terminal ownership does not assert every pre-start cleanup primitive succeeded; no cleanup-owner retry |
| Selected-pressure scheduling caller's token is canceled while delegate waits for backend usage | Yes | None at cancellation time | No immediate transition; existing busy wait continues until usage frees or global shutdown is observed | Busy loop observes only `Program.GlobalProgramCancel`; selected status/claims remain published |
| Different-pressure scheduling caller's token is canceled while selected-pressure delegate waits for backend usage | Yes | None at cancellation time | Same: no immediate transition; selected load continues waiting | The unrelated caller's cancellation does not retract selected state until existing load flow later exits |
| Global shutdown is observed while the started delegate waits for backend usage | Yes | Delegate `finally` after existing early return | Acquire `StartedCleanupOwned`; attempt reservation, mismatch, `IsLoading`, each claim, and refresh in order, isolating every failure and diagnosing it only through the non-throwing helper | Existing global-cancellation check and wait cadence remain unchanged |
| Selected-pressure scheduling caller's token cancels `LoadModel(...).Wait(cancel)` | Yes | Delegate `finally` after existing catch | Preserve readable failure input, then acquire `StartedCleanupOwned` and run all ordered attempts | Scheduling caller's request-pressure release remains separately owned |
| Different-pressure scheduling caller's token cancels selected pressure's `LoadModel(...).Wait(cancel)` | Yes | Delegate `finally` after existing catch | Record selected backend failure input, then acquire `StartedCleanupOwned` and run all ordered attempts | Cleanup does not mutate/deliver caller failure channels; failed cleanup primitives receive only best-effort diagnostics |
| Global shutdown occurs during `LoadModel` while scheduling-caller token is not canceled | Yes | Delegate `finally` only after existing load/wait exits | No immediate transition from shutdown alone; then acquire `StartedCleanupOwned` and run all ordered attempts | `Wait(cancel)` remains governed by scheduling-caller cancellation |
| Delegate loads `(none)` or the requested model successfully | Yes | Delegate `finally` | Acquire `StartedCleanupOwned`; run all ordered attempts; matching model avoids mismatch attempt body | Success logging/flow remains; baseline claim is conditional on cleanup primitives succeeding |
| Delegate catches a backend load failure or ends with model mismatch | Yes | Delegate `finally` | Preserve selected load-failure inputs; acquire `StartedCleanupOwned`; run all ordered attempts | No caller release/refusal/`Failure` delivery; cleanup-step or diagnostic failure does not abort later steps |
| Captured `GenClaim.Dispose` throws | Either | Owning cleanup function | Attempt disposal diagnostic through the non-throwing helper; regardless of diagnostic outcome, separately attempt `GC.SuppressFinalize(claim)` once; then continue captured order; the Rank 11 cleanup owner does not call `Dispose` again | Partial claim/resource/session state may remain; successful suppression prevents a finalizer-invoked `Dispose` but does not repair state |
| Post-failure `GC.SuppressFinalize` throws | Either | Owning cleanup function | Attempt suppression diagnostic through the non-throwing helper; regardless of diagnostic outcome, continue later claims/steps; the Rank 11 cleanup owner does not retry disposal or suppression | A later finalizer-invoked `Dispose` and repeated partial mutation cannot be ruled out |
| Any other cleanup primitive throws | Either | Owning cleanup function | Call the non-throwing cleanup-diagnostic helper; regardless of diagnostic outcome, continue later steps/claims; the Rank 11 cleanup owner does not retry the failed primitive | Terminal state records ownership, not successful restoration |
| Cleanup diagnostic message construction, exception formatting, or `Logs.Error` throws | Either | Method-local cleanup-diagnostic helper | Catch and swallow the diagnostic failure inside the helper; return to the cleanup catch | Original exception boundary, suppression attempt, later claims/steps, and cancellation-continuation completion remain unaffected |
| Any losing cleanup invocation or repeated invocation | Either | Any caller | Interlocked transition fails without mutation | No second cleanup-owner attempt of any claim, suppression, or step |

## Concurrency and Exclusive-Ownership Rationale

The lifecycle gate protects delegate entry and exclusive ownership of the complete ordered cleanup attempt, not successful completion of every primitive. This is necessary because a task returned before continuation setup fails can race the outer catch, while double cleanup could subtract session counters twice, race `IsLoading`, duplicate bad-backend classification/logging, or refresh loaded-model flags more than once.

The scheduled task cannot both be canceled before execution and execute its delegate. Therefore `OnlyOnCanceled` normally selects pre-start ownership while delegate `finally` selects started ownership. Setup failure after `StartNew` returns can race delegate entry; atomic competition ensures the outer catch either wins `PreStartCleanupOwned` and prevents later work, or observes delegate-started state and leaves `StartedCleanupOwned` attempts to `finally`.

The captured-claims list exists before publication. The implementation assigns each claim returned by construction and immediately attempts to add it before the next claim or scheduling step. The guarantee applies once that returned instance is in the captured list. No cleanup path iterates the mutable `highestPressure.Sessions` set to reconstruct ownership. This keeps disposal paired with the successfully returned/captured claim instances available to this owner. It does not claim recovery for an exception thrown inside a `GenClaim` constructor before the instance returns or during a failed capture before the owner records the returned instance.

Because `GenClaim.Dispose` can mutate counters before throwing and its armed finalizer calls `Dispose` again, doing nothing after failure also risks a later double decrement. The owner visits each captured claim once. On disposal failure it invokes the non-throwing cleanup-diagnostic helper, attempts `GC.SuppressFinalize(claim)` once in a separate catch boundary regardless of whether the diagnostic was emitted, then advances without another cleanup-owner disposal call. Losing Rank 11 cleanup callers retry neither disposal nor suppression. If suppression fails, this owner-scoped guarantee does not rule out a later finalizer-invoked `Dispose`.

Successful suppression prevents the known finalizer-invoked `Dispose` but may retain whatever claim entry, cancellation source, counters, or other state the failed disposal did not finish cleaning. This bounded leak/partial-state risk is safer than knowingly repeating non-idempotent mutation. If suppression throws, cleanup attempts a diagnostic and continues even if diagnostic construction, formatting, or emission also throws, but a later finalizer-invoked `Dispose` cannot be ruled out.

Non-throwing cleanup preserves the surrounding exception boundary. Publication/setup/attachment catches rethrow their original exception after best-effort cleanup. The cancellation continuation completes without a cleanup fault. Started cleanup does not abandon later attempts because reservation, mismatch, `IsLoading`, disposal, suppression, refresh, cleanup diagnostic construction/formatting, or `Logs.Error` threw.

The four-argument continuation overload fixes all cleanup-continuation inputs: `CancellationToken.None`, `OnlyOnCanceled | ExecuteSynchronously`, and `TaskScheduler.Default`. The default scheduler isolates cleanup dispatch from the ambient scheduler used by the caller. The `StartNew(action, cancel)` call is intentionally not given a replacement scheduler and retains its current behavior.

`highestPressure.Locker` continues to serialize selection and publication. The new gate does not replace that lock, broaden it around backend work, or change pressure collection ordering. Cleanup continues to update `BadBackends` under the existing pressure lock.

The pre-start path does not clear `ReserveModelLoad`: that flag is acquired only at delegate entry, and clearing it from a non-owner could release a separate reservation if surrounding behavior changes or another owner has since acted. The started path does clear it because the delegate remains its owner.

The pre-start path does not call `ReassignLoadedModelsList`: no backend model state changed. Avoiding that refresh preserves current work and notification behavior while making cleanup ownership truthful.

Request-pressure release remains request-owned. A sole canceled request can remove its pressure through `Complete`; a shared pressure can remain registered for other requests. When the selected `IsLoading` reset attempt succeeds, remaining or later eligible work can select a replacement load rather than observing a permanently active load.

When the scheduling caller and selected pressure differ, caller completion releases only caller pressure, while cleanup attempts only the globally selected pressure's `IsLoading` reset and captured claim disposals. Selected failure reasons, exclusions, retry, and detailed-error inputs remain selected-pressure state. The resulting all-loaders-failed exception becomes caller-owned after escaping `TryFind`, assignment to caller `Failure`, and delivery by `GetNextT2IBackend`; no exception state is stored on selected pressure.

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
- original publication/setup/attachment exception identity despite any cleanup-step failure;
- best-effort server-side cleanup-failure diagnostics, with diagnostic construction/formatting/emission failure contained, and continuation through later ordered attempts;
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
- edit `src/Accounts/Session.cs` or change the general `GenClaim` finalizer contract;
- have the Rank 11 cleanup owner retry a failed cleanup primitive/claim or promise transactional repair after a cleanup primitive partially mutates and throws;
- introduce a broader scheduler/lifecycle service;
- change browser, API, backend persistence, launcher, or extension code; or
- claim that the static review proves runtime thread interleavings.

## Implementation Stages

1. In `LoadHighestPressureNow`, establish one atomic lifecycle/cleanup gate with exclusive terminal states `PreStartCleanupOwned` and `StartedCleanupOwned`, plus an empty captured-claims list for the globally selected `highestPressure`, before publishing its `IsLoading` or constructing claims.
2. Add one method-local best-effort cleanup-diagnostic helper. Put cleanup context/message construction, exception formatting, and `Logs.Error` inside the helper's own try/catch; swallow any failure. Require every cleanup primitive, disposal, and suppression catch to call this helper rather than perform direct throwable logging.
3. Implement non-throwing pre-start cleanup as isolated ordered attempts: selected `IsLoading` reset, then one `Dispose` attempt per captured claim. After disposal failure, call the diagnostic helper and separately attempt `GC.SuppressFinalize(claim)` once before advancing regardless of diagnostic outcome; diagnose suppression failure through the same helper, and have the Rank 11 cleanup owner retry neither call. A suppression failure leaves a later finalizer-invoked `Dispose` possible.
4. Wrap `IsLoading` publication, immediate claim capture, `Task.Factory.StartNew(action, cancel)`, and continuation attachment in one outer try/catch. On failure, atomically select pre-start ownership or defer to started ownership, invoke non-throwing cleanup, then rethrow the original exception unchanged even when cleanup diagnostics fail.
5. Implement non-throwing started cleanup as isolated ordered attempts: reservation release, mismatch classification, selected `IsLoading` reset, one `Dispose` attempt per captured claim with the same post-failure suppression and non-throwing diagnostic policy, and `ReassignLoadedModelsList`.
6. Make delegate entry atomically decline work if pre-start ownership already won. Do not add `releasePressure`, generic caller refusal, caller `Failure` assignment, or failure delivery to cleanup.
7. Attach the pre-start path with the four-argument `ContinueWith` overload using `CancellationToken.None`, `TaskContinuationOptions.OnlyOnCanceled | TaskContinuationOptions.ExecuteSynchronously`, and `TaskScheduler.Default`; ensure neither cleanup primitives nor cleanup diagnostics can fault the continuation, while retaining the original `StartNew(action, cancel)` scheduler/token behavior.
8. Perform the bounded static inventory and control-flow review before asking the maintainer to build or exercise runtime cancellation.

The production patch must change only `src/Backends/BackendHandler.cs`; it must not edit `src/Accounts/Session.cs`. Documentation updates after implementation and validation are separate records.

## Static Verification

Agents will not build, launch, run tests, start the server, load a model, interrupt a request, or perform any other runtime validation. Static verification must:

1. confirm the production diff changes only `src/Backends/BackendHandler.cs`;
2. prove `TryFind` supplies its own `Cancel` and `ReleasePressure(true)` callback while `LoadHighestPressureNow` independently selects `highestPressure` from global `ModelRequests`; confirm `ReleasePressure(true)` returns without mutation when caller `Pressure` is null, otherwise decrements/removes/nulls it before conditionally adding the exact generic refusal only when `UserInput` is non-null;
3. enumerate every selected-state assignment, captured claim creation/disposal, post-failure `GC.SuppressFinalize`, and confirm no second cleanup-owner `Dispose` call;
4. prove the lifecycle gate and captured-claims list exist before publication, terminal states are exclusive `PreStartCleanupOwned`/`StartedCleanupOwned`, and neither terminal state is interpreted as proof of primitive success;
5. prove capture of each returned claim is attempted immediately, explicitly bounding recovery to successfully returned/captured claims and not claiming recovery from constructor/capture failure before ownership;
6. trace scheduling through `StartNew` throw, continuation-attachment throw before/after delegate entry, pre-start cancellation, and delegate dispatch after pre-start ownership;
7. prove interlocked transitions prevent backend work after `PreStartCleanupOwned`, prevent pre-start cleanup after delegate entry, and permit exactly one owning ordered attempt;
8. prove every post-publication path reaches one cleanup owner and every losing/repeated cleanup-owner invocation returns without retrying any step or claim;
9. prove the Rank 11 cleanup owner attempts selected `IsLoading` and each captured `Dispose` once; on disposal failure it separately attempts suppression once before later claims;
10. prove one method-local cleanup-diagnostic helper encloses cleanup context/message construction, exception formatting, and `Logs.Error` in its own try/catch, swallows every diagnostic failure, and cannot recursively diagnose its own failure;
11. enumerate every cleanup primitive, captured-disposal, and post-failure-suppression catch; prove each calls only the non-throwing helper for diagnostics, contains no direct throwable logging/formatting, and continues its required control flow if diagnostic construction, formatting, or `Logs.Error` fails;
12. inject each diagnostic-failure point in the static control-flow trace and prove the original publication/setup/attachment exception is still rethrown, a cancellation continuation still completes without fault, disposal-diagnostic failure cannot skip required suppression, and suppression-diagnostic failure cannot skip later claims/steps;
13. prove publication/setup/attachment catches always rethrow their original exception after cleanup attempts, and the cancellation continuation cannot fault due to either cleanup primitives or cleanup diagnostics;
14. prove started cleanup preserves reservation, mismatch, `IsLoading`, captured-order disposal/suppression, and refresh order with isolated catches;
15. prove a throwing `GenClaim.Dispose` receives a best-effort diagnostic, is not retried by the Rank 11 cleanup owner, is followed by one non-throwing `GC.SuppressFinalize(claim)` attempt regardless of diagnostic outcome, and cannot prevent later claims/refresh;
16. prove suppression failure receives a best-effort diagnostic and is not retried by the Rank 11 cleanup owner, later work continues regardless of diagnostic outcome, and a later finalizer-invoked `Dispose` is explicitly left possible;
17. separately trace scheduling-caller token owner equal to/different from selected pressure for pre-start cancellation, busy waiting, load cancellation/success/failure, selected retry/detail inputs, and caller refusal/`Failure` delivery;
18. separately trace global shutdown while waiting for usages and during `LoadModel` with and without scheduling-caller cancellation;
19. prove the busy loop still observes only `Program.GlobalProgramCancel`, while the load wait still observes only scheduling-caller `cancel`;
20. prove load exception unwrapping/logs, selected `BackendFailReasons`/`BadBackends`, retry/exclusion, and detailed-exception inputs remain unchanged;
21. prove cleanup invokes no `releasePressure`, generic refusal, all-loaders exception delivery, caller `Failure`, or unrelated-pressure classification;
22. prove all-loaders-failed retains exact callback conditions/order when `highestPressure` differs;
23. trace the detailed exception from selected inputs through caller `TryFind`, caller `Failure`, and caller waiter; prove no selected exception state;
24. prove caller-local notification/refusal/`Failure` delivery and selected session/failure/retry/detail-input ownership remain unchanged;
25. prove original scheduling-caller token/scheduler behavior remains on `StartNew` and `Wait(cancel)`;
26. prove four-argument cleanup continuation inputs remain exact and `ExecuteSynchronously` is only preferred;
27. prove pressure ordering/filtering, selection, reservation acquisition, wait cadence, successful loading, scheduler loop, and public API/ABI remain unchanged;
28. confirm production changes only `src/Backends/BackendHandler.cs`, with no `Session.cs`, `LoadModelOnAll`, browser, launcher, extension, generated, user-data, or adjacent change;
29. inspect the exact commit/file range; and
30. run `git diff --check`.

Static review can establish ownership, branch ordering, token placement, call-site scope, and signature preservation. It cannot prove the cancellation race, runtime counter values, backend behavior, thread timing, performance, or platform behavior.

## Implementation Record

Rank 11 is implemented in production commit `25859d6e` (`fix: make model-load cleanup ownership atomic`). The exact production range `42467376..25859d6e` changes only `src/Backends/BackendHandler.cs`, with 148 insertions and 40 deletions; `src/Accounts/Session.cs` is unchanged. The final design was reviewed through `c67c0b8b`, and the corrected implementation plan and review criteria were reviewed through `42467376`.

`LoadHighestPressureNow` now creates its lifecycle state and captured-claims list before publication. Delegate entry, pre-start cancellation, and publication/scheduling failure arbitrate through one interlocked lifecycle. The winning terminal state owns one ordered cleanup pass: pre-start cleanup resets only published selected-pressure state and captured claims, while started cleanup retains reservation release, mismatch classification, selected-pressure reset, captured-claim disposal, and loaded-model reassignment. A delegate dispatched after pre-start ownership returns before reserving or loading.

Both cleanup paths use the same nonthrowing diagnostic and per-step cleanup boundaries. Each independent cleanup primitive is attempted even if an earlier primitive or its diagnostic fails. The winning owner makes one `Dispose` attempt per captured claim in captured order; after a disposal failure it makes one separately guarded `GC.SuppressFinalize` attempt and then continues without a cleanup-owner retry. Post-failure finalizer suppression reduces the known double-disposal risk but does not transactionally repair partial claim/resource/session mutation. A suppression failure still leaves a later finalizer-invoked `Dispose` possible, and a constructor that throws before returning a claim remains outside the owner's recovery boundary.

The production change preserves the cancellation-gated `Task.Factory.StartNew` policy, busy-loop and load-wait token domains, pressure heuristic and selection, reservation/load behavior, failure classification, and caller-local notification/refusal/failure-delivery behavior. The scheduling caller still supplies `cancel` and `releasePressure` even when `LoadHighestPressureNow` selects a different global pressure; cleanup owns only the selected pressure's published state and captured claims. No caller pressure release, generic refusal, `Failure` assignment, or detailed-exception delivery moved into cleanup.

Task specification and code-quality review both passed. Source-only static review confirmed the exact one-file production range, lifecycle/continuation inputs, cleanup ordering, caller/selected-pressure coupling, unchanged public signatures, unchanged `Session.cs`, and clean fixed-range whitespace. In accordance with repository policy, agents performed no build, test, launch, model load, cancellation injection, runtime validation, platform validation, or performance measurement. Linux, Windows, other-platform, concurrency, backend, counter, and performance results remain pending maintainer validation.

The implementation record used these source-only commands:

```bash
git log --oneline 42467376..25859d6e
git diff --name-only 42467376..25859d6e
git diff --stat 42467376..25859d6e
git diff --check 42467376..25859d6e
rg -n "Published|Started|PreStartCleanupOwned|StartedCleanupOwned|logCleanupFailure|attemptCleanup|GC\.SuppressFinalize|IsLoading =|ReserveModelLoad =|BackendFailReasons|BadBackends|sess\.Claim|claim\.Dispose|StartNew|ContinueWith|Wait\(cancel\)|GlobalProgramCancel" src/Backends/BackendHandler.cs
git diff --name-only 42467376..25859d6e -- src/Accounts/Session.cs
git diff --name-only 42467376..25859d6e -- src/wwwroot src/Pages
git diff 42467376..25859d6e -- src/Backends/BackendHandler.cs | rg -n '^[+-].*(public|protected|internal)\b' || true
git show --check --stat --oneline 25859d6e
git show --format=fuller --no-ext-diff 25859d6e -- src/Backends/BackendHandler.cs
git status --short
```

The range log returned only `25859d6e`; the name/stat checks returned only `src/Backends/BackendHandler.cs` with 148 insertions and 40 deletions; and the range whitespace check was silent. The lifecycle inventory located the four states, both nonthrowing helpers, captured-claim disposal and post-failure suppression, both cleanup transitions, publication, delegate-entry arbitration, the cancellation continuation, reservation/classification/reset/refresh operations, and the preserved global-shutdown and scheduling-caller load-wait token sites. The scoped `Session.cs`, browser/Razor, and changed public/protected/internal declaration searches were silent. The committed-source show displayed the reviewed one-file patch, `git show --check` reported no whitespace error, and status showed only the pre-existing protected working-tree files and excluded backup. These observations are static source evidence only and add no runtime result.

## Maintainer Validation Matrix

The maintainer will build and run the live software. For every case, identify both the scheduling caller/token owner and the globally selected pressure. Record both pressure counts, selected `IsLoading`, selected session claims/counters, loader reservation, selected failure/retry inputs, caller refusal/`Failure` delivery, emitted cleanup-failure diagnostics or intentionally injected diagnostic failures, and later progress. Baseline-restoration expectations in ordinary cases assume cleanup primitives do not themselves throw; dedicated cleanup- and diagnostic-failure cases verify the bounded best-effort behavior.

1. **Partial publication/setup failure:** inject failure after `IsLoading`, captured claims, `StartNew`, and continuation attachment. Confirm one `PreStartCleanupOwned` attempt restores baseline when primitives succeed, and the original setup/attachment exception propagates unchanged.
2. **Continuation-attachment/delegate race:** inject attachment failure as the delegate enters. Confirm exactly one of `PreStartCleanupOwned` or `StartedCleanupOwned` wins; later delegate work is suppressed after pre-start ownership; terminal ownership is not reported as proof of primitive success.
3. **Pre-start cancellation, token owner equals selected pressure:** exercise sole/shared pressure. Confirm one pre-start ordered attempt, baseline with successful primitives, no backend classification/refresh, and later progress.
4. **Pre-start cancellation, token owner differs from selected pressure:** arrange caller A and selected B. Confirm one pre-start ordered attempt touches only B state/claims, no A release/refusal/`Failure`, and later B progress when primitives succeed.
5. **Busy wait, token owner equals selected pressure:** hold the loader busy, start the delegate, and cancel the selected-pressure caller token. Confirm there is no immediate cleanup while busy; after usage frees, confirm existing load-wait cancellation and one started ordered attempt, with baseline only when primitives succeed.
6. **Busy wait, token owner differs from selected pressure:** hold the loader busy while caller pressure A's token governs selected pressure B's task, then cancel A. Confirm B's reservation, `IsLoading`, claims, and visible loading state remain until usage frees or global shutdown; A's cancellation does not immediately release/classify B or cause cleanup to mutate A; after release, existing B load/failure/retry ownership is preserved.
7. **Global shutdown while waiting for usage:** exercise matching/different owners. Confirm the global check causes return and one `StartedCleanupOwned` ordered attempt; with successful primitives, reservation/state/counters/refresh reach baseline.
8. **Load cancellation, token owner equals selected pressure:** confirm existing selected failure/mismatch behavior and one started ordered attempt; with successful primitives, no duplicate counter decrement and later retry progress.
9. **Load cancellation, token owner differs from selected pressure:** confirm B receives failure/retry inputs and one started ordered attempt, while cleanup does not mutate A's refusal/`Failure` channels; baseline claims are conditional on primitive success.
10. **Global shutdown during `LoadModel`:** exercise matching/different owners with scheduling token uncanceled and canceled. Confirm global shutdown alone does not interrupt `Wait(cancel)` and each eventual exit receives one started ordered attempt.
11. **Successful load:** exercise ordinary and `(none)` with matching/different owners. Confirm unchanged success behavior and, with successful cleanup primitives, reservation/state/claims/refresh baseline.
12. **Backend load failure and retry/detail ownership:** force readable failure/mismatch with matching/different owners. Confirm selected reasons/exclusions once, one started ordered attempt, baseline when primitives succeed, and no generic caller refusal from cleanup.
13. **Existing callback, refusal, and failure-delivery coupling:** arrange active scheduling caller pressure A to invoke the all-loaders-failed branch while global pressure B is selected. Confirm `ReleasePressure(true)` decrements/removes/nulls A's pressure before, when `A.UserInput` is non-null, appending exactly `"All backends failed to load model."` to A's `RefusalReasons`; repeat with null `UserInput` and confirm pressure release without generic mutation. Then instrument null `A.Pressure` with non-null `UserInput` and confirm immediate callback return with neither pressure nor generic mutation. Confirm B retains `BackendFailReasons`, `BadBackends`, retry/exclusion, and detailed-error model/content inputs, but no exception state. Confirm the detailed B-content exception escapes A's `TryFind`, is assigned to `A.Failure`, and is delivered/thrown to A's waiter. Confirm neither cleanup policy adds another release, generic refusal, failure assignment/delivery, or classification. This case records existing behavior; it does not approve a redesign.
14. **Repeated cleanup pressure:** stress dispatch races for matching/different owners. Confirm exclusive terminal ownership, at most one cleanup-owner attempt per claim/step, and—when primitives succeed—no negative counters, duplicates, or permanent selected `IsLoading`.
15. **Compatibility flow:** run normal generation with sole/shared and competing-model pressure. Observe pressure order, globally selected loader/model, caller-local status frames/`NotifyWillLoad`, selected-session loading status, selected backend retry/detail inputs, caller generic refusal and detailed-failure delivery, final images, subsequent generations, and pre-start/started shutdown behavior.
16. **Pre-start cleanup primitive failures:** inject selected `IsLoading` and early/middle/late disposal failures. Confirm the Rank 11 cleanup owner calls each claim's `Dispose` once, follows each failed disposal with one suppression attempt, continues later claims in captured order, preserves the original setup/attachment exception, does not fault the cancellation continuation, and emits diagnostics when the diagnostic path itself succeeds.
17. **Started cleanup primitive failures:** inject reservation, mismatch, `IsLoading`, disposal, and refresh failures. Confirm each failed cleanup-owner disposal receives one suppression attempt, later claims/refresh continue in order, diagnostics identify each failure when diagnostic emission succeeds, and no cleanup-owner call is retried.
18. **Partial disposal with successful suppression:** make `GenClaim.Dispose` partially mutate then throw. Confirm one `GC.SuppressFinalize(claim)` attempt succeeds, no later finalizer disposal occurs, later claims/refresh continue, and retained partial claim/resource/session state is reported without baseline claim.
19. **Suppression failure:** make disposal throw and the subsequent suppression attempt throw. Confirm both failures receive best-effort diagnostic attempts, neither cleanup-owner call is retried, later claims/steps continue, and validation explicitly records that a later finalizer-invoked `Dispose` cannot be ruled out.
20. **Cleanup diagnostic failure containment:** independently inject failure into cleanup diagnostic context/message construction, exception formatting, and `Logs.Error`. Exercise publication/setup and continuation-attachment failure, pre-start cancellation cleanup, a disposal failure that requires suppression, suppression failure, and started cleanup with later claims/refresh. Confirm the original setup/attachment exception identity survives unchanged; the cancellation continuation completes without fault; a failed disposal's suppression attempt still occurs; and later claims/steps still run in order. Diagnostic emission is best-effort, so this case requires containment and continued cleanup, not a replacement diagnostic for the diagnostic failure.

Linux, Windows, and other-platform runtime results must be recorded separately. No runtime or performance result is implied by implementation or static review.

## Success Criteria

Rank 11 succeeds when:

- absent cleanup-primitive failure, pre-start cancellation/setup failure restores selected `IsLoading` and all successfully captured claims to baseline;
- `PreStartCleanupOwned` and `StartedCleanupOwned` identify exclusive cleanup ownership, not successful completion;
- each cleanup step and captured `Dispose` receives at most one Rank 11 cleanup-owner attempt;
- one method-local cleanup-diagnostic helper contains context/message construction, exception formatting, and `Logs.Error` in its own try/catch and swallows diagnostic failure;
- every cleanup primitive, disposal, and suppression catch calls only that helper for diagnostics, so no direct throwable cleanup logging escapes;
- both cleanup functions are non-throwing, make a best-effort server-side diagnostic attempt for each failed primitive, and continue attempting every later independent step/claim even when the diagnostic fails;
- a failed `GenClaim.Dispose` is not retried by the Rank 11 cleanup owner and receives one separate non-throwing `GC.SuppressFinalize(claim)` attempt before later claims regardless of diagnostic outcome;
- successful suppression prevents the known finalizer-invoked `Dispose` but may retain partial state/resources, with no transactional/baseline guarantee;
- suppression failure receives a best-effort diagnostic and is not retried by the Rank 11 cleanup owner, while a later finalizer-invoked `Dispose` remains explicitly possible;
- original setup/attachment exceptions are preserved and cancellation continuations cannot fault from cleanup primitives or cleanup diagnostics;
- continuation-attachment failure cannot allow both pre-start cleanup and active delegate work;
- pre-start cleanup does not mutate state owned only by a started load;
- started cleanup and failure reasoning retain their current order and semantics;
- cleanup always attempts the globally selected pressure's `IsLoading` reset and each successfully captured claim regardless of which request supplied `cancel`;
- cleanup never releases or classifies a different scheduling-token owner's pressure, appends its generic refusal, assigns its `Failure`, or delivers the detailed exception;
- the separate all-loaders-failed callback returns without mutation for null caller `Pressure`; otherwise it decrements/removes/nulls caller pressure before conditionally appending the generic refusal for non-null `UserInput`;
- sole/shared later progress is required when the relevant cleanup primitives succeed; cleanup failure is diagnosed best-effort and bounded rather than falsely reported as restored;
- matching and different scheduling-token/selected-pressure cases retain caller-local status/generic-refusal/`Failure` delivery ownership and selected-pressure backend failure/retry/detailed-error-input ownership;
- scheduling-caller cancellation while usage is busy and global shutdown during `LoadModel` retain their distinct existing token behavior;
- successful loading, status timing, selection, retry, reservation, and refusal behavior remain compatible;
- the production patch is confined to `src/Backends/BackendHandler.cs` and does not edit `Session.cs`; and
- static review and the maintainer matrix pass without an agent runtime claim.

## Rollback

Rollback is one bounded ownership change in `BackendHandler.LoadHighestPressureNow`: remove the lifecycle gate, captured-claims owner, method-local non-throwing cleanup-diagnostic helper and its catch call sites, non-throwing per-step/per-claim cleanup functions, post-disposal-failure `GC.SuppressFinalize` attempts/diagnostics, outer setup catch, delegate-entry arbitration, and cancellation-only continuation; restore the original delegate-local cleanup.

No `Session.cs` rollback is involved because it is not changed. Existing token, callback, refusal, selected failure-input, and caller failure-delivery ownership remains unchanged. A partial rollback is invalid: retaining owner-invoked disposal without post-failure suppression reopens an unsafe finalizer-invoked `Dispose`, while retaining suppression without exclusive one-disposal ownership can hide a separately scheduled legitimate owner.
