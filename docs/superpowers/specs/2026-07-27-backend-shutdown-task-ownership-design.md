# Backend Shutdown Task Ownership Design

**Status:** Implemented; awaiting maintainer validation

**Date:** 2026-07-27

**Roadmap scope:** Backend F18, rank 14

## Summary

`BackendHandler.Shutdown()` currently creates one wrapper task per configured backend, but each wrapper later appends the backend's real `DoShutdownNow()` task to the same `List<(BackendData, Task)>` that the shutdown thread concurrently enumerates, filters, and replaces. The monitor can race with those worker additions, miss a newly published task, return before a backend has actually shut down, or encounter unsafe list mutation.

Rank 14 replaces that two-stage shared-list publication with one stable handler-owned task per backend captured when handler shutdown begins. Each task owns the complete existing per-backend sequence: usage-grace polling, the existing forced-after-grace message, and direct awaiting of `DoShutdownNow()`. All tasks are published before monitoring starts, workers never mutate the collection, and the controller derives pending views from the stable task set.

An individual backend failure is logged with backend identity and readable exception detail, then isolated so every other backend, done-generating webhook, final persistence attempt, and later `Program.Shutdown()` owner can continue. The design adds no backend timeout, forced cancellation, late-add coordination, autoscaling redesign, persistence change, or performance claim.

## Goals

1. Give every backend in the shutdown-entry snapshot exactly one stable handler-owned task.
2. Publish the complete task collection before monitoring begins.
3. Keep usage-grace waiting and actual backend shutdown inside the same task.
4. Prevent worker threads from mutating the controller's task collection.
5. Prevent `BackendHandler.Shutdown()` from returning before every handler-owned backend task completes.
6. Preserve parallel backend shutdown, current grace behavior, progress logging, webhook ordering, and final persistence.
7. Log and isolate each backend-specific shutdown failure without aborting later cleanup.
8. Confine production changes to `BackendHandler.Shutdown()` in `src/Backends/BackendHandler.cs`.

## Non-Goals

Rank 14 does not:

- change the public `BackendHandler.Shutdown()` signature;
- change or atomically redesign the handler's existing `HasShutdown` guard;
- change Rank 13's process-wide atomic shutdown gate;
- prevent, reject, or coordinate backend additions, reloads, edits, or deletions after the entry snapshot;
- change `ShutdownBackendCleanly`, `DeleteById`, or any individual backend implementation;
- redesign autoscaling parent/controlled-child shutdown ownership;
- guarantee that an underlying backend implementation is invoked only once by all possible internal owners;
- make backend shutdown sequential;
- change the current 100-millisecond usage polling, counter threshold, `MaxUsages > 0` condition, or forced-after-grace message;
- add a timeout, cancellation token, forced task termination, retry, or rollback for `DoShutdownNow()`;
- change done-generating webhook behavior;
- change backend mutation generations, save acknowledgment, storage format, or final-save outcomes;
- change `Program.Shutdown()` ordering or any later shutdown owner;
- add instrumentation, benchmarks, or a performance claim; or
- edit frontend, settings, launchers, extensions, upstream code, generated files, user data, or protected maintainer work.

## Existing Boundary and Failure

### Current handler flow

After its existing idempotence check, `BackendHandler.Shutdown()`:

1. marks `HasShutdown`;
2. signals backend initialization and selection loops;
3. creates a mutable list of `(BackendData, Task)` entries;
4. starts one wrapper `Task.Run` per value enumerated from `AllBackends`;
5. inside each wrapper, waits while the backend is in use, applies the existing grace counter, then appends the real `DoShutdownNow()` task to the shared list;
6. on the shutdown thread, repeatedly logs pending backends, delays, filters completed entries, and replaces the list;
7. waits for `WebhookManager.TryMarkDoneGenerating()`; and
8. performs the existing pending final-save handling.

The wrapper task is initially the monitored entry, but it completes after publishing rather than awaiting the backend shutdown task. The real task is therefore a second ownership object whose visibility depends on an unsynchronized worker-side list mutation.

### Confirmed race

`List<T>` does not support concurrent mutation and enumeration. A worker can execute `tasks.Add(...)` while the controller evaluates `tasks.Any()`, builds its progress message, or evaluates `tasks.Where(...)`. The controller can also build and assign a replacement list from an older view after a worker publishes the real task, dropping that task from subsequent monitoring.

The unsafe publication and monitor overlap are statically confirmed. Runtime frequency, exact interleavings, and whether a given early return has occurred in production are not inferred.

### Maintained consumers

`Program.Shutdown()` is the maintained composition-root caller. It invokes backend shutdown after global cancellation and Web shutdown, and before sessions, proxy, model handlers, extensions, output metadata, temporary-directory cleanup, and final log flushing.

Within `BackendHandler.Shutdown()`, consumers of complete backend-task observation are:

- every real configured backend;
- every non-real/autoscaled backend present in the entry snapshot;
- the existing usage-grace and forced-shutdown logs;
- periodic pending-backend logs;
- `WebhookManager.TryMarkDoneGenerating()`;
- the final pending backend persistence attempt; and
- every later `Program.Shutdown()` owner.

## Chosen Architecture

### Immutable entry snapshot

After the existing guard and wake-up signals, capture the current `AllBackends.Values` enumeration once as a `BackendData[]`. This is the complete Rank 14 ownership boundary. A backend added after that capture is not added to the handler's shutdown task set, and this rank does not add a late-registration barrier.

The snapshot is an enumeration result from the existing `ConcurrentDictionary`; Rank 14 does not claim a broader transaction with concurrent add, edit, reload, or delete operations.

### One stable handler-owned task per snapshot entry

The controller synchronously iterates the snapshot and creates one task for each backend. Each task contains the entire handler-owned sequence:

1. poll the existing in-use predicate with the current cadence and threshold;
2. emit the existing forced-after-grace message when applicable;
3. directly `await backend.AbstractBackend.DoShutdownNow()`; and
4. catch and log any exception with backend ID, backend type name, and `ReadableString()` detail.

The controller alone builds the task-entry collection. Monitoring starts only after that build finishes. Worker tasks never append to, remove from, replace, or otherwise mutate the stable task-entry collection.

"One task per backend" means one `BackendHandler.Shutdown()`-owned task per entry in the captured snapshot. Existing backend-specific internals may retain their own task and parent/child behavior.

### Pending-task monitoring

The synchronous `Shutdown()` method retains its current polling shape. It derives a controller-local pending view from the stable task entries and periodically replaces only that private view with the incomplete entries.

Progress logs retain the current cadence and message structure, but backend IDs and type names come only from incomplete stable tasks. A task remains incomplete throughout its usage grace and its awaited `DoShutdownNow()`, so the controller cannot discard it merely because a wrapper finished publishing a second task.

No `Task.WaitAll` propagation is introduced. Backend faults are handled inside their owner tasks, allowing the synchronous monitor to observe completion without aborting the composition-root shutdown sequence.

## Data Flow and Ordering

The approved order is:

1. existing `HasShutdown` check and assignment;
2. existing `NewBackendInitSignal.Set()`;
3. existing `CheckBackendsSignal.Set()`;
4. one captured backend-entry snapshot;
5. synchronous publication of one handler-owned task per snapshot entry;
6. parallel usage-grace polling and backend shutdown inside those tasks;
7. controller-only monitoring and periodic pending logs until every task completes;
8. existing `WebhookManager.TryMarkDoneGenerating().Wait()`;
9. existing final backend-save decision and result handling; and
10. return to the unchanged later `Program.Shutdown()` sequence.

The task collection becomes stable before step 6 can affect its ownership. Task bodies may begin running during publication, but they have no reference or path that mutates the collection, so their scheduling order cannot alter which tasks the controller will monitor.

## Failure Handling

### Backend-specific failure

The owner task catches exceptions covering its complete grace-and-shutdown body. It logs one error that identifies the backend by ID and handler type and includes the readable exception.

After logging, that owner task completes. Other backend tasks remain independent and continue running. The controller continues until all handler-owned tasks have completed, then proceeds to the done-generating webhook and final persistence.

This is an intentional improvement over the current effectively unobserved inner-task fault. Rank 14 does not rethrow a backend fault into `Program.Shutdown()`, because doing so could skip sessions, proxy, model handlers, extensions, metadata, temp cleanup, and log flushing.

### Non-completing backend

A backend whose `DoShutdownNow()` never completes remains pending indefinitely. The controller continues emitting the existing periodic progress logs. Rank 14 adds no timeout or cancellation because neither is part of the current backend shutdown contract.

### Final persistence failure

The existing generation-aware save protocol and isolated final-save outcomes remain textually and behaviorally unchanged. A backend shutdown failure does not skip that final persistence attempt. A persistence failure remains logged and isolated according to the already implemented backend persistence protocol.

## Compatibility

The production change preserves:

- `public void BackendHandler.Shutdown()`;
- the existing handler `HasShutdown` field and sequential guard behavior;
- the two wake-up signals;
- `AllBackends` public identity and type;
- backend snapshot enumeration through `AllBackends.Values`;
- `ShutdownBackendCleanly` and `DeleteById`;
- every `AbstractBackend` and concrete backend shutdown signature;
- parallel backend shutdown;
- the in-use predicate, `MaxUsages` rule, polling cadence, counter threshold, and forced-shutdown message;
- periodic pending-backend diagnostics;
- done-generating webhook placement;
- backend persistence state, format, save ordering, and failure isolation;
- Rank 13 first-caller process-shutdown ownership; and
- all later `Program.Shutdown()` ordering and public extension ABI.

No public field, property, method, event, or extension contract changes.

## Alternatives Considered

### `Task.WhenAll` plus separate progress monitoring

The stable owner tasks could be combined with `Task.WhenAll`, while another loop reports progress. This can be correct, but it introduces a second aggregate-completion owner or requires exception propagation suppression around the aggregate. The controller already has a clear synchronous polling contract, so a stable entry set with one pending view is simpler and preserves more local structure.

### Lock the existing shared list

A lock could protect worker additions and controller enumeration/replacement. This retains the fragile wrapper-task/real-task split, requires careful coordination around list replacement, and leaves task ownership harder to prove. It treats unsafe publication as a locking problem instead of removing the second publication entirely.

### Sequential shutdown

Awaiting one backend at a time would remove collection concurrency, but it would change shutdown latency and existing parallel behavior. One slow or stuck backend would prevent all later backends from even starting.

### New timeouts or cancellation

Timing out or canceling `DoShutdownNow()` would require a new lifecycle contract for partially shut-down backends and resources. The current abstract method promises not to return until resources are cleared and accepts no cancellation token. Rank 14 has no evidence or authorization for that redesign.

### Reject backend changes after shutdown begins

A handler-wide registration barrier could make the entry snapshot exhaustive against concurrent additions, reloads, or edits. That expands Rank 14 into API/lifecycle coordination across unrelated mutation paths. The approved scope fixes task ownership for the captured shutdown set only.

## Production Scope

Expected production modification:

- `src/Backends/BackendHandler.cs`
  - change only `BackendHandler.Shutdown()` task creation and monitoring;
  - create one entry snapshot;
  - create one stable task per snapshot backend;
  - await the real backend shutdown inside that task;
  - contain and log backend-specific failures; and
  - monitor a controller-owned pending view.

Expected unchanged source includes:

- `src/Core/Program.cs`;
- `src/Backends/AbstractBackend.cs`;
- every concrete backend implementation;
- `ShutdownBackendCleanly`;
- `DeleteById`;
- backend persistence helpers; and
- webhook infrastructure.

No other source file belongs in the production commit.

## Static Verification

Agents must not build, launch, or test SwarmUI. Static verification must:

1. enumerate every maintained `BackendHandler.Shutdown()` caller;
2. capture the complete pre-change handler flow and task ownership;
3. confirm the production diff changes only `BackendHandler.Shutdown()`;
4. confirm `AllBackends.Values` is captured once before task publication;
5. confirm exactly one handler-owned task is created per snapshot entry;
6. confirm every task is added by the controller before monitoring begins;
7. confirm no worker task can mutate the stable task-entry collection or pending view;
8. confirm the usage predicate, cadence, counter threshold, `MaxUsages` condition, and forced-shutdown log are unchanged;
9. confirm each task directly awaits `DoShutdownNow()`;
10. confirm the catch logs backend identity and readable exception detail without rethrowing;
11. confirm progress logs use only incomplete stable task entries;
12. compare the complete post-monitor webhook and persistence body with the pinned baseline;
13. confirm public signatures, fields, callers, save protocol, and `Program.Shutdown()` are unchanged;
14. inspect the exact commit and fixed-range diff; and
15. run `git diff --check`.

Static evidence can establish collection ownership, publication order, direct awaiting, fault containment, source scope, signatures, and unchanged downstream text. It cannot prove runtime thread schedules, backend timing, external extension behavior, process/resource release, filesystem outcomes, platform behavior, or exactly-once effects inside backend-specific parent/child implementations.

## Implementation Record

**Implementation status:** **Implemented; awaiting maintainer validation.**

The approved design base is `f17e09557157f9c3ed13841c368885711636d96f`. The integrated design-to-source history through production head `efe5e32ad9ed93ae1727f613e19c91f27bbbfe03` contains documentation-plan commit `880d03df131977126c2557dbc5844568289e06be` followed by production commit `efe5e32ad9ed93ae1727f613e19c91f27bbbfe03`; path-filtering that history to `src/Backends/BackendHandler.cs` returns only the production commit. The production projection changes exactly `src/Backends/BackendHandler.cs`, with `24 insertions(+), 14 deletions(-)`, and is confined to `BackendHandler.Shutdown()`.

The implemented method captures one `BackendData[]` snapshot from `AllBackends.Values`, then the controller synchronously publishes one stable named-tuple task entry per snapshot backend before it begins monitoring. Each task owns the complete usage-grace body and directly awaits `backend.AbstractBackend.DoShutdownNow()`. Workers have no path to mutate either the stable `shutdownTasks` collection or the controller-local `pendingTasks` view. The controller alone derives and replaces the pending view from incomplete stable tasks, so a slow real shutdown remains pending until its awaited lifetime completes. Each owner task catches its own grace-or-shutdown exception, logs backend ID, backend type name, and `ReadableString()` detail, and completes without rethrowing, canceling, or suppressing other backend tasks or downstream shutdown work.

The existing `HasShutdown` guard, both wake-up signals, in-use predicate, `MaxUsages > 0` condition, 100-millisecond polling, counter threshold, forced-after-grace message, progress cadence and message content, done-generating webhook placement, final persistence body and outcomes, maintained callers, individual backend implementations, autoscaling parent/controlled-child internals, `Program.Shutdown()` ordering, and public source/binary extension ABI remain unchanged. A backend added after the entry snapshot remains outside this task set. A backend whose shutdown task never completes still causes an indefinite wait with periodic progress logs because no timeout or cancellation was added. One handler-owned task per snapshot entry does not assert exactly-once invocation across backend-specific parent/child owners.

The exact static boundary commands reported: `git rev-parse f17e0955` → `f17e09557157f9c3ed13841c368885711636d96f`; `git rev-parse 880d03df` → `880d03df131977126c2557dbc5844568289e06be`; `git rev-parse efe5e32a` → `efe5e32ad9ed93ae1727f613e19c91f27bbbfe03`; `git log --format='%H %s' f17e0955..efe5e32a` → plan then production; the same log filtered to `src/Backends/BackendHandler.cs` → only `efe5e32a`; `git show --numstat --format='' efe5e32a -- src/Backends/BackendHandler.cs` → `24	14	src/Backends/BackendHandler.cs`; and `git diff --check f17e0955..efe5e32a -- src/Backends/BackendHandler.cs` → no output. Method-boundary, caller/signature, collection-ownership, direct-await, fault-containment, downstream-text, and protected-path inspection also matched the approved scope. Independent source reviews returned `SOURCE_SPEC_APPROVED` and `SOURCE_QUALITY_APPROVED`, both with no findings.

Agents performed no build, test, launch, runtime, thread-interleaving, platform, filesystem, or performance exercise and make no such claim. All runtime behavior and all platforms remain unvalidated until the maintainer records the exact matrix below; no benchmark or performance claim is made.

## Maintainer Validation Matrix

The maintainer performs all builds and live validation. Record operating system/filesystem and exact outcomes.

1. No configured backends completes handler shutdown, done-generating webhook handling, and clean final-save handling without error.
2. One idle or immediately completing backend receives one handler-owned shutdown task and completes before handler return.
3. One active backend that becomes available during the grace period shuts down after availability without the forced-after-grace message.
4. One backend that remains active beyond the existing grace threshold emits the existing forced-shutdown message and then enters `DoShutdownNow()`.
5. Mixed idle, active, slow, and immediately completing backends all start in parallel and are all observed through completion.
6. An asynchronously slow `DoShutdownNow()` remains in pending diagnostics and prevents early handler return until it actually completes.
7. One faulting backend emits one backend-specific readable diagnostic while every other backend, webhook, final persistence, and later process-shutdown owner continues.
8. Multiple faulting backends each emit their own diagnostic without suppressing one another or later shutdown work.
9. Real and non-real/autoscaled backends retain their existing backend-specific behavior while every entry in the handler snapshot has one observed handler-owned task.
10. Periodic pending-backend logs list only tasks that have not completed and retain backend IDs, type names, and cadence.
11. Clean, pending-save, newer-change-pending, and failed final-save outcomes retain their existing logs, acknowledgment rules, and continuation behavior after backend tasks complete.
12. Repeated sequential or direct handler shutdown calls retain the existing guard behavior and do not publish a second task set.
13. Web, admin, restart, control-timeout, unload, and process-exit overlap remains owned by Rank 13's outer gate while backend task monitoring runs once.
14. Repeated stress runs with mixed task timing produce no collection-mutation exception, missed handler-owned task, or return before every handler-owned task completes.

Validation distinguishes observed behavior from agent static evidence. Windows and other operating-system/filesystem combinations remain separately unvalidated unless explicitly recorded, and no performance claim is made.

## Success Criteria

Rank 14 is successful when:

- the handler publishes one stable owner task per shutdown-entry backend;
- worker tasks never mutate the monitored collection;
- the monitor cannot miss the real `DoShutdownNow()` lifetime;
- backend failures are individually logged and isolated;
- all other backend tasks and downstream handler work continue after a fault;
- parallelism, grace behavior, progress logs, webhook ordering, and persistence behavior remain;
- the production diff is confined to `BackendHandler.Shutdown()`;
- static specification and quality review pass; and
- the maintainer confirms the exact validation matrix on a recorded platform.

## Rollback

Rollback is confined to the task construction and monitoring block in `BackendHandler.Shutdown()`: restore the former wrapper-task/shared-list flow. No data migration, persistence rollback, public API change, backend implementation rollback, Rank 13 rollback, or extension change is required.
