# Output Save Transient Lifetimes Design

**Status:** Approved design; implementation pending

**Date:** 2026-07-25

**Roadmap scope:** Backend F19 and Backend F20, rank 12

## Summary

`Session.SaveImage` publishes two process-wide transient entries before its background persistence work finishes:

- `StillSavingFiles[fullPath]` lets immediate output readers use pending or recently completed bytes; and
- `RecentlyBlockedFilenames[fullPath]` prevents a concurrent save from selecting the same output name.

The pending-byte entry is currently removed only after the entire background operation and a ten-second delay. A conversion, file, sidecar, preview, history-index, or logging failure skips that removal. Filename reservations have no normal completion or expiry path. Maintained saves and deletions can therefore retain transient state for the process lifetime.

Rank 12 gives these structures explicit, owner-aware lifetimes. It preserves active-save collision protection, the successful ten-second read-through window, existing output and error contracts, current path and metadata behavior, and the public dictionary fields used by extensions. A private coordinator records both generation and lifecycle state so stale cleanup cannot erase a newer owner and an administrative RAM clear cannot expose a path still used by an active save or deletion.

The integrated-review correction distinguishes successful deletion from partial or failed deletion. A successful deletion retains its inactive reservation for ten seconds. A partial or failed deletion retains an inactive, non-expiring reservation until an explicit system-RAM clear or process restart, because the primary file may be gone while a double-extension sidecar remains invisible to `SaveImage`'s extensionless collision scan.

## Goals

1. Remove each maintained pending-byte entry after the successful ten-second read-through window.
2. Remove a maintained pending-byte entry immediately when its save pipeline fails.
3. Keep every maintained save reservation active for the entire background operation, including the successful read-through delay.
4. Keep a deletion reservation active for its entire primary, sidecar, metadata/index, and response-construction operation.
5. Give successful deletions and failed saves a ten-second inactive reservation window.
6. Retain partial or failed deletion reservations until explicit system-RAM clear or restart.
7. Remove expiring inactive reservations even when the server is otherwise idle.
8. Prevent cleanup from an older operation from removing a newer pending task or reservation.
9. Preserve active maintained reservations across system-RAM clear while clearing legacy public and inactive maintained reservations.
10. Preserve filename collision behavior for active saves, rapid batches, deletions, multiple formats, and `[number]`/suffix paths.
11. Preserve public field ABI, source compatibility, dictionary identity, path keys, reader behavior, URLs, error responses, and fire-and-forget save reporting.
12. Keep the change bounded to output transient-state ownership.

## Non-Goals

Rank 12 does not:

- make background save failure observable through a new API response or status frame;
- wait for background persistence before returning or streaming the output URL;
- reorder conversion, media write, sidecar write, preview creation, history-index upsert, or debug logging;
- change output templates, numbering, extensions, path normalization, user output roots, or disk collision checks;
- replace `RecentlyBlockedFilenames` with the history index or a directory cache;
- change `WebServer` or `T2IParamTypes` pending-byte reader behavior;
- redesign deletion, recycling, metadata deletion, history-index deletion, or background save cancellation;
- repair partial media, sidecar, preview, metadata, or history-index state;
- automatically inspect or remove orphaned sidecars after deletion failure;
- promise automatic expiry for direct external-extension writes that bypass the maintained coordinator;
- close the exact-key race when a direct extension mutates the public dictionary concurrently with a maintained operation;
- optimize or make a performance claim for the P6 output-directory scan prerequisite; or
- change generated API documentation, frontend code, launchers, settings, user data, extensions, or upstream code.

The direct-extension exact-key race is deliberately outside this correction. Preserving the public dictionaries' existing types, values, and object identities means the coordinator cannot attach ownership metadata to arbitrary extension writes or intercept them atomically.

## Existing Flow and Confirmed Consumers

### Save producers

The maintained persisted-output callers are normal T2I output through `T2IAPI`, Image History add through `ImageHistoryAPI`, and Grid Generator persisted output through `GridGeneratorExtension`.

Their existing no-save branches return data URLs and do not enter `Session.SaveImage`. `Session.SaveImage` also returns a data URL before path selection when the user's `SaveFiles` setting is false. Those bypasses remain unchanged.

### Pending-byte readers

`WebServer` output delivery and `T2IParamTypes.FilePathToDataString` normalize the requested path, prefer `Session.StillSavingFiles`, await the stored task, and fall back to disk only when the dictionary has no entry. A stranded entry can retain bytes or repeatedly surface a stale task failure instead of allowing a later disk read.

Both readers remain unchanged. Rank 12 corrects lifetime at the publishing owner.

### Filename-reservation producers and consumers

`Session.SaveImage` publishes each chosen persisted path to `RecentlyBlockedFilenames`. `ImageHistoryAPI.DeleteImage` publishes the standardized deleted path. Later saves combine on-disk files with public reservation keys and compare extensionless names while holding the user's `UserLock`.

`BackendAPI.FreeBackendMemory(system_ram: true)` is the only maintained administrative clear. It is changed to clear legacy public and inactive maintained reservations while preserving active maintained reservations and their public keys.

## Compatibility Boundary

The following public fields retain their names, types, static field form, and object identity:

```csharp
public static ConcurrentDictionary<string, string> RecentlyBlockedFilenames;
public static ConcurrentDictionary<string, Task<byte[]>> StillSavingFiles;
```

They are not converted to properties, wrapped in replacement collection types, or reassigned during normal operation or RAM clear. Existing external extension binaries can continue resolving the same fields, and source extensions can continue reading or mutating the dictionaries.

Maintained lifecycle handling applies only to reservations created through the private coordinator. A direct extension write remains a legacy public entry. Maintained operations are protected from one another, but concurrent external mutation of the exact same key has no new ownership guarantee because the public value still contains only the path string.

No existing method signature, output URL, API response, reader contract, or save-error delivery mechanism changes.

## Reservation Coordinator

`Session` owns a private coordinator adjacent to the public dictionaries. It contains:

- a monotonic generation counter;
- a private map from normalized full path to a state containing the current maintained generation and whether that generation is active; and
- one synchronization boundary covering maintained publication, state transitions, generation comparison, public reservation publication/removal, and coordinated clear.

The coordinator returns an opaque handle containing normalized path and generation. Only a matching handle may transition or release a maintained reservation.

Generation numbers never reset during RAM clear. Active generations survive clear. An inactive or absent pre-clear handle therefore cannot match or remove a later reservation for the same path.

All new fields follow the repository XML-documentation rule. The implementation uses explicit C# types, full braced blocks, and existing utility/task patterns.

### Lifecycle states

| Private state | Meaning | Automatic expiry | System-RAM clear |
| --- | --- | --- | --- |
| Active save | Background save is running, including successful read-through delay | Never | Preserve private state and public key |
| Active deletion | Delete/recycle, companion cleanup, metadata/index cleanup, or success-response construction is running | Never | Preserve private state and public key |
| Inactive expiring | Failed save or successful deletion completed | Ten seconds from synchronous transition | Remove |
| Inactive retained | Deletion partially completed or failed | None | Remove |
| Legacy public only | Direct public write with no private state | None maintained by coordinator | Remove |

Restart naturally discards all process-local states.

### Save reservation acquisition

Path selection continues under `User.UserLock`. On-disk extensionless names are collected exactly as today. The final maintained acquisition atomically checks the public reservation set for the candidate extensionless path and publishes an active private state plus the public full-path reservation.

If another maintained or legacy public reservation appeared after the earlier collision snapshot, acquisition rejects the candidate and path selection advances to the next existing `[number]` or suffix candidate. If public publication throws, acquisition restores the exact prior private state. This closes the maintained publication race without changing the collision namespace.

### Delete reservation acquisition

`ImageHistoryAPI.DeleteImage` requests a new active generation for the standardized full path before invoking the configured delete/recycle action. A deletion may replace the maintained state for that exact path. It becomes the newer owner, so cleanup from a save that published the file earlier cannot erase deletion protection.

The existing file-existence check, configured action, sidecar loop, metadata removal, history-index removal, success response, and exception propagation remain unchanged.

## Pending-Byte Lifecycle

After a save path and reservation are selected, `SaveImage` creates the exact `Task<byte[]>` that readers will await and publishes it to `StillSavingFiles`. The same task reference is captured by cleanup.

The background operation retains its existing ordered work:

1. await `image.ActualFileTask` when present;
2. write primary media bytes;
3. write eligible sidecar metadata;
4. create or load the preview;
5. upsert the history index;
6. emit the existing success debug log; and
7. on full success, wait ten seconds for immediate read-through.

The operation is enclosed by `try/finally`. The `finally` atomically removes the path only if `StillSavingFiles` still contains the exact captured task. It cannot remove a newer task published under the same path.

On full success, cleanup runs after the ten-second delay, removes the exact pending task, and immediately releases the still-matching active save reservation. The completed disk file remains in the ordinary collision set.

If conversion returns null, conversion faults, or any media, sidecar, preview, index, logging, or delay step throws, `finally` removes the exact pending task immediately. It then calls delayed reservation release, which synchronously changes the matching active state to inactive before scheduling its ten-second expiry. The original exception continues to `Utilities.RunCheckedTask`.

Any synchronous setup failure after reservation or pending-task publication uses the same owner-aware cleanup before the existing outer `SaveImage` catch returns `("ERROR", null)`.

Failed saves remain expiring rather than retained-until-clear. Companion metadata is written only after the primary media write, so either no companion exists or the primary disk file exists and continues blocking extensionless stem reuse through the unchanged directory scan.

## Filename-Reservation Lifecycle

### Successful save

The save reservation stays active through the entire successful pipeline and read-through delay. It never expires or becomes clearable while active. After the delay, the matching handle removes its private state and public key immediately. A completed disk file continues to block reuse.

If a deletion or another maintained operation installed a newer generation, old save cleanup sees a mismatch and performs no mutation.

### Failed save

The delayed-release helper first locks the coordinator, verifies the exact generation, and synchronously transitions it from active to inactive. It then schedules ten-second exact-generation removal. A partial primary file continues blocking reuse through the disk scan; without a primary file, the name becomes reusable after the window.

### Successful deletion

`DeleteImage` sets `deletionSucceeded` only after primary deletion, all discovered sidecar deletions, metadata removal, history-index removal, and successful response construction complete. Its `finally` then invokes delayed release. The matching deletion generation becomes inactive synchronously and expires after ten seconds.

The deleted stem can be selected again after that window if no disk file, public legacy entry, or newer reservation blocks it.

### Partial or failed deletion

If any post-acquisition deletion step or success-response construction throws, `deletionSucceeded` remains false. `finally` calls the generation-checked `RetainOutputFilenameReservationUntilClear` helper. It transitions the matching active generation to inactive without scheduling expiry, then lets the original exception propagate unchanged.

This boundedness exception is necessary because the primary media may already have been deleted while a later double-extension sidecar such as `name.png.swarm.json` remains. `SaveImage` strips only the final extension during disk collision discovery, yielding `name.png`; it can therefore miss the original extensionless stem `name` and inherit stale sidecar state if the reservation expires automatically.

The retained state lasts only until explicit `system_ram: true` clear or process restart. Administrative clear is the deliberate safety override.

### Delayed-release scheduling

`ReleaseOutputFilenameReservationAfterDelay` does not leave an active state while arranging expiry. Under the coordinator lock it verifies the exact generation and transitions active to inactive. A missing, stale, or already-inactive handle is a no-op.

It then schedules a ten-second delay followed by exact-generation removal. If scheduling itself throws, it performs immediate exact-generation removal as a bounded best-effort fallback. The delayed callback is nonthrowing at its caller boundary.

The number of delayed expiry tasks is bounded by failed saves and successful deletions that became inactive during the preceding ten seconds. Successful saves reuse their existing read-through delay and add no second delay. Failed deletions add no expiry task.

## Cleanup and Error Boundaries

Pending-task removal, reservation state transition, delayed expiry, retention, and coordinated clear are cleanup operations. Each helper is nonthrowing at its caller boundary:

- an old or repeated handle is a no-op;
- a generation mismatch is a no-op;
- pending cleanup removes only the captured task value;
- reservation transition and removal compare the captured generation;
- cleanup failure cannot replace the original save or delete exception; and
- failure in a best-effort cleanup diagnostic cannot interrupt remaining cleanup.

The implementation must not convert the checked background task into a silently successful task. After `finally`, the original background exception continues to `Utilities.RunCheckedTask`. Deletion likewise preserves existing exception behavior.

## Coordinated RAM Clear

`BackendAPI.FreeBackendMemory(system_ram: true)` calls a `Session` helper that operates under the coordinator lock. It:

1. identifies every active maintained path;
2. removes every inactive maintained private state and its public key;
3. removes every legacy public entry that is not an active maintained path; and
4. preserves every active maintained private state and its public key.

Maintained acquisition uses the same lock, so it cannot observe or publish through a clear/repopulate gap. The helper never clears the public dictionary wholesale and later repopulates it. Direct external mutations do not share the coordinator lock and retain the documented exact-key caveat.

The public dictionary object is not replaced. The generation counter is not reset. `StillSavingFiles` remains unchanged and stays owned by save-pipeline cleanup.

System-RAM clear deliberately releases inactive retained deletion failures. If stale sidecars remain, the administrator has chosen to override that protection and accepts the existing disk state.

## Concurrency Cases

| Interleaving | Required result |
| --- | --- |
| Old save succeeds after a newer delete reserves the same path | Old save removes only its exact pending task; delete state remains |
| Old save fails after a newer delete reserves the same path | Old save cannot transition or expire the newer delete generation |
| Old delayed expiry fires after a newer save reserves the same path | Expiry sees a generation mismatch and does nothing |
| RAM clear overlaps an active save | Active private state and public key remain continuously visible |
| RAM clear overlaps an active deletion | Active private state and public key remain continuously visible |
| RAM clear removes an inactive expiring reservation | Later delayed cleanup sees no matching generation and does nothing |
| RAM clear removes an inactive retained deletion failure | The explicit administrative override releases the path |
| New reservation follows RAM clear for the same path | Monotonic generation prevents old handles from matching it |
| New pending task appears before old task cleanup | Atomic key-and-value removal retains the newer task |
| Concurrent maintained saves choose the same extensionless name | Atomic acquisition rejects one candidate and advances numbering/suffix |
| Legacy extension reservation exists for a candidate | Maintained acquisition treats its public key as blocked |
| Direct extension mutation races maintained cleanup on the exact key | Public compatibility remains; no new cross-owner guarantee is claimed |

## Files and Components

Expected production scope:

- `src/Accounts/Session.cs`
  - retain public dictionaries;
  - add generation and active/inactive reservation state;
  - make pending cleanup task-owned;
  - make save reservations generation-owned;
  - expose delayed-release, retain-until-clear, and coordinated-clear operations.
- `src/WebAPI/ImageHistoryAPI.cs`
  - distinguish fully successful deletion from partial/failure cleanup.
- `src/WebAPI/BackendAPI.cs`
  - clear only inactive maintained and legacy public reservations.

Expected unchanged consumers:

- `src/Core/WebServer.cs`;
- `src/Text2Image/T2IParamTypes.cs`;
- `src/WebAPI/T2IAPI.cs`;
- `src/BuiltinExtensions/GridGenerator/GridGeneratorExtension.cs`; and
- existing no-save branches.

Design, plan, and audit records are documentation-only companions. `docs/APIRoutes` is generated and must not be edited.

## Static Verification

Repository policy forbids agents from building, launching, or testing SwarmUI. Agent verification is source-only and must:

1. inventory every maintained read, write, remove, and clear of both public dictionaries;
2. trace every synchronous throw after reservation or pending-task publication;
3. trace every asynchronous throw from conversion through successful delay;
4. prove each asynchronous path reaches pending-task `finally`;
5. prove pending cleanup compares the exact captured task;
6. prove every save/delete/expiry/retention cleanup compares the exact generation;
7. prove acquisition publishes active state and restores the prior full state on rollback;
8. prove delayed release changes active to inactive synchronously before scheduling;
9. prove scheduling failure performs immediate exact-generation removal;
10. prove deletion success is assigned only after all deletion work and response construction;
11. prove deletion failure becomes inactive and non-expiring without replacing its exception;
12. prove successful cache retention is still ten seconds;
13. prove failed pending cleanup has no additional ten-second wait;
14. prove active saves and deletions have no time-based expiry;
15. prove RAM clear preserves active private/public pairs without a maintained-acquisition gap;
16. prove RAM clear removes inactive maintained and legacy public entries without resetting the counter or touching `StillSavingFiles`;
17. prove failed saves still expire and failed deletions do not;
18. prove collision selection preserves extensionless `[number]`/suffix behavior;
19. prove public field names, types, static form, instances, and reader paths are unchanged;
20. prove save work order, URLs, error responses, checked-task logging, deletion behavior, and no-save bypasses are unchanged;
21. prove the direct external same-key caveat remains explicit; and
22. prove no frontend, settings, launcher, extension, generated, upstream, user-data, or protected working-tree file entered the production diff.

Permitted verification is limited to source inventory, numbered source review, exact-range diff inspection, static lint where configured, and `git diff --check`.

## Maintainer Validation Matrix

The maintainer performs all builds and live validation.

### Successful flow

1. Normal persisted T2I output returns its URL immediately, supports immediate `/Output` or `/View` read, and remains readable from disk after ten seconds.
2. `FilePathToDataString` reads pending bytes before persistence completes and disk bytes after cleanup.
3. Image History add and Grid persisted output retain existing behavior.
4. `DoNotSave`, `DoNotSaveIntermediates`, and `SaveFiles = false` bypass transient publication.
5. PNG/JPEG/WebP and non-image media retain extension, sidecar, preview, and index behavior.
6. `[number]` and suffix templates retain selected names.

### Failure cleanup

7. Null conversion and faulted conversion remove pending state immediately and expire the failed-save reservation after ten seconds.
8. Primary media write failure has the same lifetime.
9. Sidecar write failure has the same lifetime, while any primary file blocks reuse on disk.
10. Preview failure has the same lifetime.
11. History-index failure has the same lifetime.
12. Existing server logging occurs, no client contract changes, and later reads are not pinned to a stranded pending task.
13. A partial primary file continues blocking reuse through directory scanning.

### Deletion outcomes

14. A fully successful deletion remains blocked for ten seconds and then becomes reusable when nothing else blocks it.
15. Primary delete/recycle failure retains an inactive reservation until RAM clear or restart.
16. Sidecar deletion failure after primary removal retains an inactive reservation and prevents stale sidecar stem inheritance.
17. Metadata or history-index deletion failure retains an inactive reservation.
18. Deletion exceptions and error responses remain unchanged.

### Ownership and races

19. Rapid same-template saves receive distinct collision-free names.
20. Concurrent same-name saves cannot share a maintained reservation.
21. Delete during or immediately after save installs the newer reservation, and old save cleanup does not erase it.
22. A stale failed-save or successful-delete expiry cannot remove a newer save/delete reservation.
23. A stale pending cleanup cannot remove a newer pending task.
24. Multiple users and folders do not remove one another's reservations.

### Administrative and compatibility behavior

25. `system_ram: true` removes legacy public and inactive maintained reservations without replacing the public dictionary.
26. `system_ram: true` preserves active save private/public state and collision protection.
27. `system_ram: true` preserves active deletion private/public state and collision protection.
28. Clearing a retained failed-deletion reservation deliberately permits reuse subject to disk state.
29. `system_ram: false` does not clear reservations.
30. Pre-clear delayed cleanup cannot remove a post-clear reservation for the same path.
31. Existing external extension reads and writes against both public dictionary fields continue to load and function.
32. Maintained pending and expiring-inactive counts return to baseline after successful windows; retained failed-deletion count remains until RAM clear or restart.

Validation records the operating system and filesystem, distinguishes observed behavior from static evidence, and avoids performance claims unless separately measured.

## Rollback

The production change is one coordinated ownership unit across `Session`, deletion, and RAM clear:

1. restore direct save reservation publication and pending removal;
2. restore direct deletion reservation publication;
3. restore direct whole-public-map RAM clearing; and
4. remove private generation/lifecycle state and its helpers.

Partial rollback is unsafe. Restoring whole-map clear while active-state acquisition remains would reopen same-path save/write races, and restoring unconditional delete expiry while sidecar cleanup remains non-transactional would reopen stale-sidecar inheritance. The unchanged public fields and readers make a full rollback mechanically bounded.
