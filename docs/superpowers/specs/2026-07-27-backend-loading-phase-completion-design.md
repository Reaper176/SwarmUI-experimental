# Backend Loading Phase Completion Design

**Status:** Design approved; not implemented

**Date:** 2026-07-27

**Roadmap scope:** Backend F15, rank 16

## Summary

`BackendHandler.LoadInternal()` starts the backend initialization monitor and then loads configured backends from `Backends.fds`. The static startup-phase flag `BackendHandler.IsLoading` begins as `true` and is currently set to `false` only after a non-null file has been enumerated successfully.

Missing storage, an unreadable or malformed file handled by the existing read catch, or a null parser result returns before that assignment. An unexpected exception during entry processing also bypasses it. The startup storage phase has terminated in each case, but the flag remains true. Later fast-capable backend additions or reloads therefore continue to capture the startup-only anti-thrash delay.

Rank 16 moves completion publication into a `finally` around only the storage-load phase after successful monitor startup. Every return or exception from that phase sets `IsLoading = false` exactly once. Existing tolerated outcomes remain tolerated, unexpected exceptions still propagate, startup-file entries still capture their existing anti-thrash decision while the phase is active, and all later backend initialization paths remain unchanged.

## Goals

1. Publish startup storage-phase completion after every return or exception once `InternalInitMonitor` has started successfully.
2. Keep `IsLoading` true while configured backend entries are being enumerated and scheduled.
3. Preserve the first-boot anti-thrash delay decision for fast-capable entries loaded from the startup file.
4. Ensure later API-created, extension-created, edited, or reloaded fast backends do not inherit a completed startup phase.
5. Preserve existing missing-file tolerance, unreadable-file diagnostics, null-result behavior, entry ordering, initialization, retries, monitor startup, request-loop startup, and exception propagation.
6. Confine production changes to `BackendHandler.LoadInternal()` in `src/Backends/BackendHandler.cs`.
7. Keep rollback limited to restoring the trailing completion assignment.

## Non-Goals

Rank 16 does not:

- change `BackendHandler.Load()` or its duplicate-call guard;
- clear `IsLoading` if the initialization log or monitor-thread startup itself throws before the storage phase begins;
- catch, translate, suppress, or continue past unexpected entry-processing exceptions;
- make malformed individual backend entries independently recoverable;
- change `FDSUtility.ReadFile`, the `Backends.fds` format, parsing rules, or null semantics;
- change unknown-backend-type handling;
- reorder backend entries or make startup enumeration asynchronous;
- wait for backend initialization tasks to finish before completing the storage phase;
- change `DoInitBackend`, `LoadBackendDirect`, retry counts, retry delays, or status transitions;
- change `CountBackendsFastLoaded`, reset it, or alter its process-wide lifetime;
- change the startup anti-thrash delay formula or which backend types are fast-capable;
- change queued non-fast backend behavior;
- change `InternalInitMonitor`, `NewBackendInitSignal`, `RequestHandlingLoop`, or loaded-model reassignment;
- change API add, edit, reload, non-real, autoscaling, or extension backend creation paths;
- change the public static `IsLoading` field's name, type, static identity, or memory qualifier;
- add a new phase type, helper, lock, interlocked state, cancellation token, event, or public member;
- alter persistence, backend IDs, settings, enabled state, titles, or public source/binary extension ABI;
- add instrumentation, benchmarks, or a performance claim; or
- edit frontend code, settings, launchers, external extensions, upstream code, generated files, user data, or protected maintainer work.

## Existing Boundary and Failure

### Maintained startup owner

`Program.Main` constructs one `BackendHandler`, sets its `SaveFilePath`, and calls `Backends.Load()` during startup. `Load()`:

1. returns early if `AllBackends` is already nonempty as a duplicate-call guard;
2. calls `LoadInternal()`;
3. signals `NewBackendInitSignal`;
4. reassigns loaded-model flags; and
5. starts `RequestHandlingLoop`.

`LoadInternal()` is public but documented as internal and has no other maintained caller.

### Current storage flow

`LoadInternal()` currently:

1. emits the backend-loading initialization log;
2. starts `InternalInitMonitor` on a named thread;
3. calls `FDSUtility.ReadFile(SaveFilePath)`;
4. silently returns for `FileNotFoundException` or `DirectoryNotFoundException`;
5. logs and returns for other read exceptions;
6. silently returns if the parsed section is null;
7. enumerates root keys in file order;
8. logs and skips unknown backend types;
9. instantiates and populates each accepted backend;
10. adds it to `AllBackends`;
11. calls `DoInitBackend(data)`; and
12. assigns `IsLoading = false` only after the complete non-null enumeration.

The three tolerated early returns and every unexpected exception after monitor startup bypass step 12.

### Startup-only delay consumer

The only maintained consumer of the startup-phase `BackendHandler.IsLoading` field is `DoInitBackend`. For a backend whose type can load fast, or when all backends are configured to load fast, it:

1. increments `CountBackendsFastLoaded`;
2. synchronously captures `shouldWait = count > 1 && IsLoading`; and
3. starts a task that applies `1 + min(5, count / 10)` seconds of delay when `shouldWait` is true before calling `LoadBackendDirect`.

Configured entries call `DoInitBackend` before the current trailing completion assignment, so their delay decisions are captured while `IsLoading` is true. Later add, non-real add, edit, reload, API restart, and extension registration paths also call `DoInitBackend`, but should do so after the startup storage phase is complete.

When a tolerated storage outcome returns early, `Load()` still starts its signal, loaded-model reassignment, and request loop. Because the phase flag remains true, the second and later fast-capable initialization performed afterward can receive the startup-only delay even though no startup file enumeration remains active.

### Unexpected entry-processing failure

Operations such as section access, numeric ID parsing, backend instantiation, settings creation/loading, or `DoInitBackend` may throw after some entries have been processed. The exception currently escapes `LoadInternal()` and prevents the remainder of `Load()` from running.

Rank 16 does not change that propagation or partial-progress behavior. It changes only the stale phase flag: the storage phase is no longer reported active after control exits it exceptionally.

## Chosen Architecture

### Storage-phase `try/finally`

Keep the initialization log and successful monitor-thread start before the new boundary. Place the complete existing storage-read, tolerated-error, null-result, and entry-enumeration body inside:

```csharp
        try
        {
            // Existing storage read, tolerated returns, and entry enumeration.
        }
        finally
        {
            IsLoading = false;
        }
```

Remove the former trailing `IsLoading = false` from the successful path.

This gives the storage phase one lexical owner and one completion publication. C# executes the `finally` before completing a `return` or propagating an exception, so all phase exits converge without duplicating assignments or changing control flow.

### Boundary begins after monitor startup

The new `try` begins only after:

```csharp
        Logs.Init("Loading backends from file...");
        new Thread(InternalInitMonitor) { Name = "BackendHandler_Init_Monitor" }.Start();
```

If logging or thread startup fails, the storage phase did not begin under the approved boundary and `IsLoading` remains unchanged. This preserves the audit's owner definition and avoids claiming lifecycle completion for a monitor that never started.

### Completion is storage scheduling, not backend readiness

`IsLoading = false` means that reading, enumerating, and scheduling configured entries has terminated. It does not mean every backend has initialized successfully or reached a running state.

Fast backend tasks capture `shouldWait` synchronously inside `DoInitBackend` while startup enumeration is still in progress. Clearing the shared flag afterward does not cancel or change those already captured delays. Non-fast backends remain queued for `InternalInitMonitor` exactly as before.

## Data Flow and Ordering

The approved successful/tolerated order is:

1. existing initialization log;
2. existing successful monitor-thread start;
3. enter the storage-phase `try`;
4. execute the existing read behavior;
5. for a tolerated missing, unreadable, or null outcome, begin method return;
6. otherwise enumerate entries in the existing order;
7. instantiate, register, and schedule each accepted entry unchanged;
8. leave the `try`;
9. execute `finally` and set `IsLoading = false`;
10. return normally to `Load()`;
11. execute the existing `NewBackendInitSignal.Set()`;
12. execute the existing `ReassignLoadedModelsList()`; and
13. start the existing `RequestHandlingLoop`.

For an unexpected exception after step 3:

1. stop the existing storage operation at the same point;
2. execute `finally` and set `IsLoading = false`;
3. propagate the original exception unchanged; and
4. retain the current behavior in which later `Load()` statements are not reached.

No catch is added around entry processing, and no cleanup of already registered or scheduled entries is added.

## Outcome Matrix

| Storage outcome after monitor startup | Existing control flow retained | Rank 16 phase result |
| --- | --- | --- |
| Missing file | Silent return to `Load()` | `IsLoading` becomes false before return |
| Missing directory | Silent return to `Load()` | `IsLoading` becomes false before return |
| Other read/parse exception handled by current catch | Existing readable error log, then return | `IsLoading` becomes false before return |
| Null parsed section | Silent return to `Load()` | `IsLoading` becomes false before return |
| Empty valid section | Complete empty enumeration | `IsLoading` becomes false after enumeration |
| Unknown backend type | Existing error and skip | Phase remains true for later entries, then becomes false |
| Valid entries | Existing ordered instantiate/register/schedule flow | Phase remains true through scheduling, then becomes false |
| Unexpected entry-processing exception | Original exception propagates | `IsLoading` becomes false before propagation |
| Log or monitor startup exception | Original exception propagates before storage phase | `IsLoading` remains unchanged |
| Duplicate `Load()` guard before `LoadInternal()` | Existing immediate return | Outside Rank 16 storage-phase ownership |

## Failure Handling

### Tolerated storage outcomes

The existing nested read catch remains unchanged:

- `FileNotFoundException` and `DirectoryNotFoundException` return silently.
- Every other exception caught around `FDSUtility.ReadFile` retains the existing `Could not read Backends save file` diagnostic with `ReadableString()`, then returns.
- A null result retains its silent return.

The outer `finally` runs after each return. It does not log, throw, retry, create default storage, or alter the return.

### Unexpected storage or entry failure

The outer `finally` does not include a catch. An exception from root-key enumeration, section access, ID conversion, backend construction, settings loading, collection insertion, or initialization scheduling propagates with its original type and stack behavior after the phase flag is cleared.

Rank 16 does not guarantee that `Load()` completes after such an exception, that request handling starts, or that partially registered backends are rolled back.

### Completion assignment

The Boolean assignment is nonthrowing and unconditional. It does not depend on diagnostics, cleanup primitives, backend state, cancellation, or another service. No secondary failure policy is needed.

## Compatibility

The production change preserves:

- `public void BackendHandler.Load()`;
- `public void BackendHandler.LoadInternal()`;
- `public static bool BackendHandler.IsLoading`;
- `public static long BackendHandler.CountBackendsFastLoaded`;
- the duplicate-load guard;
- initialization log text and placement;
- monitor thread type, target, name, and start placement;
- `SaveFilePath` and `FDSUtility.ReadFile`;
- missing-file and missing-directory silence;
- unreadable-file diagnostic text and exception formatting;
- null-result behavior;
- root-key order and unknown-type diagnostics;
- backend type lookup, instantiation, ID assignment, settings loading, enabled state, title, handler assignment, collection insertion, and `DoInitBackend` call order;
- fast-capability selection, counter increment, delay predicate, delay formula, task creation, and direct load;
- non-fast initialization queueing;
- backend retry and final-error behavior;
- signal, loaded-model reassignment, and request-loop ordering in `Load()`;
- add, non-real add, edit, reload, API restart, autoscaling, and extension callers;
- persistence format and behavior; and
- all public source and binary extension interfaces.

## Alternatives Considered

### Caller-level `try/finally` in `Load()`

`Load()` could wrap its call to `LoadInternal()` and publish completion in its own `finally`. That would protect the maintained caller, but it would separate the phase state from the storage owner, would not protect any direct `LoadInternal()` call, and could broaden completion to failures before the approved post-monitor storage boundary. Keeping ownership inside `LoadInternal()` is more direct and auditable.

### Explicit assignment on each return and terminal path

Each current return could set `IsLoading = false`, with another assignment retained after successful enumeration. Covering unexpected exceptions would then require an additional catch/rethrow or still require a `finally`. Duplicated completion calls are easier to omit when future returns are added and obscure the single lifecycle boundary.

### New phase abstraction or interlocked state

An enum, disposable phase guard, interlocked transition, or event could model startup more generally. There is one maintained owner, one Boolean consumer, and no evidence of competing phase writers. Additional state machinery would expand public/lifecycle semantics without improving this bounded correction.

### Catch and continue per malformed backend entry

Per-entry isolation could let later entries and the remainder of `Load()` continue after malformed settings or construction failures. That changes parsing, partial-registration, diagnostics, and startup availability policy. Rank 16 preserves exception propagation and addresses only phase completion.

## Production Scope

Expected production modification:

- `src/Backends/BackendHandler.cs`
  - modify only `BackendHandler.LoadInternal()`;
  - wrap the existing storage body after monitor startup in `try/finally`;
  - move the successful-path `IsLoading = false` assignment into `finally`.

Expected unchanged source includes:

- `src/Core/Program.cs`;
- every other `BackendHandler` method and field;
- `AbstractBackend` and all concrete backend implementations;
- `src/WebAPI/BackendAPI.cs`;
- extension registration code;
- FreneticDataSyntax parsing code; and
- frontend, settings, launchers, generated API docs, external extensions, and user data.

No public declaration or persisted format changes.

## Static Verification

Agents must not build, launch, or test SwarmUI. Static verification must:

1. confirm `Program.Main` remains the sole maintained `Load()` caller;
2. confirm `Load()` remains the sole maintained `LoadInternal()` caller;
3. enumerate every maintained reference to the startup `BackendHandler.IsLoading` field and distinguish unrelated model-pressure `IsLoading` fields;
4. inspect every `LoadInternal()` return and throwable operation after monitor startup;
5. confirm the outer `try` starts after the existing monitor-thread start;
6. confirm every tolerated return crosses the `finally`;
7. confirm unexpected entry-processing exceptions cross the `finally` and remain uncaught;
8. confirm `IsLoading = false` occurs once as storage-phase completion;
9. confirm configured-entry `DoInitBackend` calls remain inside the active phase;
10. confirm `DoInitBackend` captures `shouldWait` synchronously before starting its task;
11. confirm the fast-load predicate, counter, delay formula, task, and direct load are unchanged;
12. confirm queued initialization, monitor processing, retry behavior, and statuses are unchanged;
13. compare the complete `Load()` and post-`LoadInternal()` body with the design base;
14. inspect add, non-real add, edit, reload, API restart, autoscaling, and extension call paths without changing them;
15. confirm the production diff changes only `LoadInternal()` in `BackendHandler.cs`;
16. confirm no public/protected declaration or ABI surface changes;
17. inspect the exact fixed-range source diff and commit scope; and
18. run `git diff --check`.

Static evidence can prove lexical completion, control-flow preservation, consumer inventory, captured delay decisions, source scope, and unchanged declarations. It cannot prove runtime thread scheduling, observed delay timing, backend process behavior, filesystem/parser outcomes, external extension behavior, platform behavior, or performance.

## Maintainer Validation Matrix

The maintainer performs all builds and live validation. Record operating system, filesystem, and exact outcomes.

1. Missing `Backends.fds` completes startup without a backend-file error and leaves the storage phase complete.
2. Missing parent directory retains the same tolerant startup behavior and leaves the storage phase complete.
3. Unreadable or malformed storage that throws during `FDSUtility.ReadFile` retains the existing readable error and leaves the storage phase complete.
4. A null parser result, where reproducible, retains its silent return and leaves the storage phase complete.
5. Empty valid storage completes enumeration and leaves the storage phase complete.
6. An unknown backend type retains its existing diagnostic, is skipped, and does not prevent phase completion.
7. One valid fast backend retains its existing initialization and status behavior.
8. Multiple valid fast backends retain the first-boot anti-thrash delay decisions captured during startup enumeration.
9. Mixed fast and queued backends retain configured order, queueing, monitor processing, and status behavior.
10. Adding multiple fast backends after each tolerated terminal storage outcome does not apply the startup-only delay.
11. Reloading fast backends after storage completion does not apply the startup-only delay.
12. An injected unexpected entry-processing failure still propagates through the existing startup path while inspection confirms `IsLoading` was set false before propagation.
13. Backend initialization failure, retry delay/count, final error, and later retry behavior remain unchanged.
14. Normal multi-backend startup retains monitor activity, request-loop startup, backend statuses, model-state reassignment, and successful generation behavior.

## Success Criteria

Rank 16 is successful when:

- every return or exception from the post-monitor storage phase publishes `IsLoading = false`;
- completion occurs exactly once through one lexical `finally`;
- configured startup entries still capture the existing anti-thrash decision while the phase is true;
- later add and reload paths no longer inherit a terminated startup phase;
- all named tolerant outcomes, diagnostics, ordering, initialization, retry, monitor, request-loop, persistence, and API/ABI behavior remain unchanged;
- unexpected entry-processing exceptions still propagate;
- production changes are confined to `LoadInternal()` in one file; and
- no unperformed runtime, platform, filesystem, or performance result is claimed.

## Rollback

Rollback removes the outer storage-phase `try/finally` and restores the single trailing `IsLoading = false` assignment after successful non-null enumeration. No data migration, configuration conversion, persisted-format change, or public API rollback is required.
