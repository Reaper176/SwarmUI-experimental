# Backend Persistence Generation Acknowledgement Design

**Date:** 2026-07-23

**Status:** Implemented; awaiting maintainer validation

## Goal

Make configured-backend persistence generation-acknowledged and failure-isolated so a real backend mutation cannot be lost by the periodic dirty-state race, a transient save failure cannot terminate the only periodic writer, and a final save failure cannot abort later shutdown cleanup.

This project addresses rank 5 by coordinating Backend F14 and Backend F21 from the maintainability architecture refresh. Their mutation publication, persistence acknowledgement, retry, and failure handling form one protocol and must be implemented and rolled back together.

## Confirmed Boundary

At the source baseline, `BackendHandler.BackendsEdited` is a public unsynchronized Boolean. `AddNewOfType` and `DeleteById` assign it before their catalog mutation, while edit and toggle assign it after their principal mutation. Program's ten-second task reads the Boolean, clears it, and then calls `BackendHandler.Save`.

The early add/delete publication permits this ordering:

1. a real mutation assigns `BackendsEdited = true`;
2. the periodic task observes and clears it;
3. the periodic task snapshots the catalog before the mutation is visible;
4. the mutation becomes visible without another dirty publication; and
5. the saved file permanently omits that mutation unless an unrelated later edit occurs.

`BackendHandler.Save` holds `SaveLock`, builds the real-backend FDS snapshot, and calls `FDSUtility.SaveToFile` without an exception boundary. A save exception exits the unretained periodic task, preventing later retries. The same exception can escape `BackendHandler.Shutdown` into `Program.Shutdown` before sessions, proxy state, model handlers, extensions, output metadata, temporary data, and logs finish cleanup.

The maintained persistence-relevant mutations are:

- real backend creation through `BackendHandler.AddNewOfType`, used by `BackendAPI.AddNewBackend` and installation;
- real backend deletion through `BackendAPI.DeleteBackend` and `BackendHandler.DeleteById`;
- backend settings/title/ID edits through `BackendHandler.EditById`;
- enable/disable through `BackendAPI.ToggleBackend`; and
- public compatibility callers that assign `BackendsEdited = true`.

`DeleteById` is also used for autoscaler and linked-remote runtime-only children. Those backends have `IsReal == false`, are excluded from `Backends.fds`, and must not independently schedule persistence.

## Design Decision

Use monotonic mutation and saved generations. Do not put asynchronous backend lifecycle operations under one persistence lock and do not replace the mutable backend catalog with an immutable store in this project.

The generation protocol preserves:

- immediate API response timing;
- the existing ten-second coalescing interval;
- `Backends.fds` structure, path, secret filtering, and ID keys;
- backend initialization, shutdown, and reload behavior;
- non-real backend exclusion;
- the public `void Save()` signature and force-save behavior; and
- the current requested process exit code during shutdown.

The public `BackendsEdited` field-to-property migration is source-compatible but not binary-compatible with an extension assembly compiled against the old field token. SwarmUI's managed external-extension loader caches each built DLL by the extension repository commit alone, so a core update does not currently force that assembly to rebuild. The compatibility correction therefore extends the bounded project to the managed extension build-cache identity and the touched periodic task's cancellation exit.

## Managed Extension Binary Compatibility

`ExtensionsManager.BuildExtension` keys each managed external-extension DLL by two independent identities:

- the existing extension repository commit; and
- the current SwarmUI core identity.

The core identity is the running core assembly version with dots replaced by hyphens, followed by that assembly module's full `ModuleVersionId` in 32-character `N` format. The assembly version keeps the cache name recognizable, while the module MVID identifies the actual compiled core binary that extensions reference.

The target DLL filename is `<dllName>-<extensionIdentity>-core-<version>-<moduleMvid>.dll`, and the build `TargetName` is the same value without `.dll`. An extension cache entry built against a different core binary therefore does not match, causing one normal rebuild through the existing extension build path. Later launches with the same core binary and extension identity reuse that rebuilt cache. Older unmatched DLLs retain the existing cache-retention behavior; cleanup policy is not changed in this project.

This intentionally favors automatic binary compatibility over retaining extension DLLs across core builds. It is independent of Git layout, branch, detached-HEAD, worktree, packed-ref, and packaged-install behavior. Deterministic builds may reuse an identical MVID for identical output; any changed running core binary identity selects a distinct extension target. The correction does not change extension discovery, source layout, dependency resolution, load contexts, build configuration, disabled-extension behavior, error reporting, or the extension repository commit calculation.

SwarmUI's managed source-build path is the compatibility owner. Independently supplied binary-only extensions that bypass this path remain outside scope.

The core repository guidance records that managed compiled-extension cache keys must change with the host identity whenever extensions can reference core public members. This prevents a future core ABI change from silently reusing a stale assembly.

## Generation Ownership

`BackendHandler` owns two monotonic `long` values:

- the mutation generation is the latest persistence-relevant in-memory catalog mutation; and
- the saved generation is the latest generation acknowledged as represented by the authoritative `Backends.fds`.

Both generations start at zero. Loading the existing backend file establishes the in-memory baseline and does not publish a mutation, so the generations remain equal until a maintained caller changes persistence-relevant state.

Generation reads use `Volatile.Read`. Mutation publication uses `Interlocked.Increment`. Saved acknowledgement occurs only while the existing `SaveLock` serializes the save owner.

The invariant is:

```text
saved generation <= mutation generation
```

Pending persistence exists exactly when:

```text
mutation generation > saved generation
```

An effectively unreachable signed-`long` overflow is not assigned a separate rollover protocol. No process can publish enough backend mutations within its lifetime to approach the boundary.

## `BackendsEdited` Compatibility Surface

The public Boolean field becomes a property without changing its name:

- `get` returns whether a generation is pending;
- `set true` publishes a new mutation generation; and
- `set false` is ignored.

Only the persistence owner may acknowledge saved state. Ignoring external `false` assignments prevents an extension or legacy caller from falsely declaring unsaved catalog data durable. The maintained Program loop no longer assigns `false`.

The property remains a compatibility facade. New maintained code uses a clearly named generation-publication method so the publication point is visible during review.

## Mutation Publication

Generation publication occurs only for a persistence-relevant real backend and only after its first visible catalog mutation.

### Real add

`AddNewOfType` publishes after successful insertion into `AllBackends`, before initialization. If later initialization throws, the visible inserted backend still has pending persistence rather than becoming untracked.

`AddNewNonrealBackend` does not publish.

### Delete

`DeleteById` attempts removal first. A missing ID publishes nothing. After successful removal, it publishes only if the removed backend is real, before awaiting clean shutdown.

If later shutdown work throws, the already visible real removal remains pending. Non-real autoscaler and linked-remote removal does not schedule a backend-file write unless a separate parent-setting mutation publishes its own generation.

### Edit

An edit may change ID, settings, title, and modification count after asynchronous shutdown. Generation publication happens after persistence-relevant changes are visible.

The edit path uses a guarded publication boundary so a partially visible change followed by an exception still leaves a pending generation. It must not publish before any edit attempt becomes visible, and it may conservatively publish after a partially applied attempt even when a later operation fails.

This project does not redesign edit atomicity or API error behavior.

### Toggle

Toggle publishes immediately after `IsEnabled` changes and before waiting for current usage to drain or reinitializing the backend. Cancellation or shutdown after the visible flag change therefore cannot leave it untracked.

### Public compatibility assignment

Assigning `BackendsEdited = true` publishes a generation. It may cause a conservative extra save, which is preferable to missing an extension-owned real catalog change.

## Save Result

The observable pending-save owner reports:

- `NoChanges`: the saved generation was already current;
- `Saved`: the captured generation was durably saved and no newer generation is pending;
- `SavedWithNewerChangesPending`: the captured generation was durably saved but a later mutation remains pending; or
- `Failed`: the intended authoritative snapshot was not confirmed and no generation was acknowledged.

The result type is owned by the backend persistence boundary and is not transported through existing backend mutation API responses.

## Save Algorithm

The pending-save owner:

1. acquires `SaveLock`;
2. reads the mutation generation as the target;
3. returns `NoChanges` when the target is already acknowledged;
4. serializes one complete FDS snapshot of real backends only;
5. writes the snapshot to the unchanged `SaveFilePath`;
6. confirms the authoritative persistence outcome;
7. advances the saved generation through the captured target only; and
8. rereads the mutation generation to select `Saved` or `SavedWithNewerChangesPending`.

If a mutation occurs after target capture:

- it may or may not appear in the weakly concurrent snapshot;
- its later generation is not acknowledged by this pass; and
- the next periodic pass writes a current snapshot.

A redundant later save is acceptable. A lost mutation or false acknowledgement is not.

`SaveLock` continues to prevent overlapping serialization and writes. No backend initialization, usage drain, or asynchronous shutdown work occurs while it is held.

## Authoritative File Classification

The intended FDS snapshot is serialized once. If `FDSUtility.SaveToFile` throws, the owner compares the authoritative `Backends.fds` itself with that exact intended serialization.

- An exact match means the authoritative move committed and a later journal-cleanup step failed. The exception is logged, but the captured generation may be acknowledged.
- A missing, mismatched, or unreadable authoritative file is `Failed`.
- The `~2` fallback is not treated as the newly committed authoritative state.

This mirrors the bounded server-settings persistence classification without introducing a new atomic-file writer. General filesystem atomic replacement remains outside this project.

## Public `Save()` Compatibility

The existing public `void Save()` remains a force-save facade:

- it writes even when no generation is pending;
- it preserves the historical exception behavior for external direct callers; and
- on success, it uses the same generation acknowledgement rules.

Maintained periodic and shutdown paths use the result-returning pending-save owner so they can isolate failures without losing retry state.

## Periodic Persistence

The existing ten-second background loop remains the sole maintained periodic owner.

It no longer clears `BackendsEdited`. Each interval asks the observable owner to save pending work:

- `NoChanges` does nothing;
- `Saved` acknowledges the captured current generation;
- `SavedWithNewerChangesPending` leaves the newer generation for the next interval; and
- `Failed` logs the error, acknowledges nothing, and leaves the loop alive for retry.

Persistent failures remain visible in server logs. This design does not add backoff, rate limiting, a new worker, or synchronous mutation responses.

Cancellation of the tokenized ten-second delay is handled with a `try`/`catch` around that delay only. An `OperationCanceledException` filtered by `GlobalProgramCancel.IsCancellationRequested` returns from the periodic task normally. The existing post-delay cancellation check remains, and persistence exceptions continue through `TrySavePending`; cancellation handling does not become a general exception boundary.

## Shutdown Persistence

`BackendHandler.Shutdown` retains its current backend-drain and webhook ordering. When a generation remains pending, it makes one final observable save attempt.

Final save failure:

- is logged with server-side detail;
- acknowledges no generation;
- does not escape `BackendHandler.Shutdown`;
- does not change the requested process exit code; and
- allows all later `Program.Shutdown` owners to continue.

The project does not add a shutdown retry delay. Periodic recovery is available before shutdown; once shutdown begins, cleanup availability takes precedence over waiting indefinitely for storage.

## API and Runtime Compatibility

Backend add, delete, edit, and toggle routes retain:

- route names and permissions;
- request and response structures;
- live mutation response timing;
- current readable/invalid-ID behavior;
- usage-drain, initialization, and shutdown behavior; and
- `LockSettings` fast-path behavior.

API success continues to mean the live mutation was accepted. Persistence remains coalesced and observable internally rather than becoming a synchronous API prerequisite.

No backend scheduler, model-loading, capability, autoscaling, remote-child, or process-lifecycle behavior is redesigned.

## Error Handling

Serialization, path, and file-write exceptions are caught only by the observable maintained owner. Logs include readable server-side detail and never enter existing API payloads.

A failed pass cannot:

- advance the saved generation;
- clear pending work;
- terminate the periodic writer; or
- abort later shutdown cleanup.

A subsequent successful pass may acknowledge the latest captured generation. Repeated failures remain pending until recovery or process exit.

## Static Verification

Repository-permitted static checks will prove:

1. every real add, successful real removal, edit, and toggle publishes after visible mutation;
2. missing and non-real deletion do not publish persistence work;
3. the maintained Program loop never assigns `BackendsEdited = false`;
4. only successful authoritative persistence advances the saved generation;
5. mutation during snapshot or write leaves a later generation pending;
6. save failure retains pending work and the periodic loop continues;
7. verified journal-cleanup failure acknowledges only the captured generation;
8. shutdown failure is contained before later `Program.Shutdown` owners;
9. public `BackendsEdited` and `Save()` source-level names and approved semantics remain;
10. FDS schema, secret filtering, and real-backend selection remain unchanged; and
11. managed extension cache target and build names contain both extension and core identities;
12. a changed core module MVID invalidates the managed compiled-extension cache while the same running binary reuses it;
13. token cancellation exits the periodic task normally without swallowing persistence failures; and
14. the implementation changes only the bounded backend persistence owners, managed extension cache identity, repository guidance, and documentation.

Agents will not run builds, tests, launchers, servers, browsers, backends, installers, or live storage mutations.

## Maintainer Validation

Maintainer Reaper176 should use the normal build/launch workflow and validate:

1. API add, edit, toggle, and delete for real backends;
2. installation-created real backend persistence;
3. autoscaler and linked-remote non-real removal without redundant persistence;
4. mutation during periodic snapshot/write followed by a second successful pass;
5. temporarily unwritable or full backend storage, repeated retry, recovery, and restart;
6. repeated rapid mutations with periodic-save overlap;
7. shutdown overlap with a pending mutation;
8. forced final-save failure while later sessions, proxy, models, extensions, output metadata, temporary-data, and log cleanup still run;
9. requested restart/nonzero exit-code preservation during final-save failure;
10. authoritative commit followed by journal-cleanup failure; and
11. external/public `Save()` and `BackendsEdited = true` source compatibility;
12. an external extension that references `BackendsEdited`, proving a changed core binary rebuilds its managed DLL before load across normal Git, detached, worktree, packed-ref, and packaged layouts;
13. same-core-binary restart proving the rebuilt extension DLL is reused; and
14. normal shutdown/restart proving periodic cancellation produces no unobserved task fault.

No benchmark or performance measurement is part of validation.

## Success Criteria

- No real backend mutation can be falsely acknowledged by a snapshot that predates it.
- A mutation concurrent with persistence always leaves a later generation pending.
- A transient failure cannot stop future periodic retries.
- A final failure cannot skip later shutdown cleanup or change the requested exit code.
- Runtime-only non-real removal does not independently dirty `Backends.fds`.
- Existing backend API timing, payloads, lifecycle behavior, FDS data, and public compatibility facades remain intact.
- Managed source extensions are rebuilt after a core identity change before they can load a stale field token.
- Normal periodic-task cancellation completes without an unobserved cancellation fault.
- Memory and the latest successfully acknowledged `Backends.fds` generation agree after recovery and restart.

## Non-Goals

- No synchronous durable-save requirement for backend mutation APIs.
- No immutable backend catalog or general repository/store framework.
- No backend lifecycle, scheduler, autoscaling, remote-child, or model-loading redesign.
- No FDS schema/path change or general atomic-file replacement utility.
- No retry backoff, new persistence thread, shutdown wait loop, or exit-code policy change.
- No attempt to persist non-real backend instances.
- No performance optimization or benchmark claim.
- No general extension build-system, dependency-loader, or binary-only extension redesign.

## Risks and Rollback

Primary risks are publishing before visibility, acknowledging a generation newer than the actual snapshot, missing partially visible edit failures, introducing a lock-order cycle, changing public Boolean/facade behavior, reusing a managed extension binary compiled against the old field token, or suppressing a failure without retaining retry state.

The monotonic counters, post-visibility publication, captured target, serialized save owner, exact authoritative comparison, explicit result, core-aware extension cache identity, targeted delay cancellation boundary, and unchanged asynchronous lifecycle boundary contain those risks.

Rollback must treat generation publication, acknowledgement, periodic retry, shutdown isolation, and managed-extension cache invalidation as one compatibility protocol. Reverting only generation acknowledgement can strand dirty work; reverting only failure isolation can restore writer termination; restoring clear-before-save recreates the original lost-update race; and retaining the property while reverting core-aware cache invalidation can reload a stale extension field token.
