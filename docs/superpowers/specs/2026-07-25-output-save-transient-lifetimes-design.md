# Output Save Transient Lifetimes Design

**Status:** Approved design; implementation pending

**Date:** 2026-07-25

**Roadmap scope:** Backend F19 and Backend F20, rank 12

## Summary

`Session.SaveImage` publishes two process-wide transient entries before its background persistence work finishes:

- `StillSavingFiles[fullPath]` lets immediate output readers use the pending or recently completed bytes; and
- `RecentlyBlockedFilenames[fullPath]` prevents a concurrent save from selecting the same output name.

The pending-byte entry is currently removed only after the entire background operation and a ten-second delay. Conversion, file, sidecar, preview, or history-index failure skips that removal and can retain successful, faulted, or null-derived tasks indefinitely. Filename reservations have no completion or expiry path at all. Every maintained save and deletion therefore adds a process-lifetime key unless an administrator later requests a system-RAM clear.

Rank 12 gives both structures explicit lifetimes. It preserves active-save collision protection, the successful ten-second read-through window, existing output and error contracts, current path and metadata behavior, and the public dictionary fields used by extensions. It adds owner-aware cleanup so an older operation cannot erase a newer task or reservation for the same path.

## Goals

1. Remove each maintained pending-byte entry after the successful ten-second read-through window.
2. Remove a maintained pending-byte entry immediately when its save pipeline fails.
3. Keep every maintained filename reservation active for the entire save operation and successful read-through window.
4. Keep deletion and failed-save reservations for a conservative ten seconds after they become inactive.
5. Remove expired maintained reservations even when the server is otherwise idle.
6. Prevent cleanup from an older operation from removing a newer pending task or filename reservation.
7. Preserve filename collision behavior for active saves, rapid batches, deletions, multiple formats, and `[number]`/suffix paths.
8. Preserve public field ABI, source compatibility, dictionary identity, path keys, reader behavior, URLs, error responses, and fire-and-forget save reporting.
9. Keep the change bounded to output transient-state ownership.

## Non-Goals

Rank 12 does not:

- make background save failure observable through a new API response or status frame;
- wait for background persistence before returning or streaming the output URL;
- reorder conversion, media write, sidecar write, preview creation, history-index upsert, or debug logging;
- change output templates, numbering, extensions, path normalization, user output roots, or disk collision checks;
- replace `RecentlyBlockedFilenames` with the history index or a directory cache;
- change `WebServer` or `T2IParamTypes` pending-byte reader behavior;
- redesign deletion, recycling, metadata deletion, history-index deletion, or background save cancellation;
- repair partial media, sidecar, preview, or history-index state after a save failure;
- promise automatic expiry for direct external-extension writes that bypass the maintained reservation coordinator;
- optimize or make a performance claim for the P6 output-directory scan prerequisite; or
- change generated API documentation, frontend code, launchers, settings, user data, extensions, or upstream code.

## Existing Flow and Confirmed Consumers

### Save producers

The maintained persisted-output callers are:

- normal T2I output through `T2IAPI`;
- Image History add through `ImageHistoryAPI`; and
- Grid Generator persisted output through `GridGeneratorExtension`.

Their existing no-save branches return data URLs and do not enter `Session.SaveImage`. `Session.SaveImage` also returns a data URL before path selection when the user's `SaveFiles` setting is false. Those bypasses remain unchanged.

### Pending-byte readers

`WebServer` output delivery and `T2IParamTypes.FilePathToDataString` normalize the requested path, prefer `Session.StillSavingFiles`, await the stored task, and fall back to disk only when the dictionary has no entry. A stranded entry can therefore retain bytes or repeatedly surface a stale task failure instead of allowing a later disk read.

Both readers remain unchanged. Rank 12 corrects the lifetime at the publishing owner.

### Filename-reservation producers and consumers

`Session.SaveImage` publishes each chosen persisted path to `RecentlyBlockedFilenames`. `ImageHistoryAPI.DeleteImage` publishes the standardized deleted path. Later saves combine on-disk files with the public reservation keys and compare extensionless names while holding the user's `UserLock`.

`BackendAPI.FreeBackendMemory(system_ram: true)` is the only maintained whole-map clear. It remains an administrative clear and is extended to clear the new private maintained ownership records together with the public facade.

## Compatibility Boundary

The following public fields retain their names, types, static field form, and object identity:

```csharp
public static ConcurrentDictionary<string, string> RecentlyBlockedFilenames;
public static ConcurrentDictionary<string, Task<byte[]>> StillSavingFiles;
```

They are not converted to properties, wrapped in replacement collection types, or reassigned during normal operation or RAM clear. Existing external extension binaries can continue resolving the same fields, and source extensions can continue reading or mutating the dictionaries.

Maintained automatic expiry applies only to reservations created through the new private coordinator because only those entries have generation metadata. A direct extension write remains a legacy dictionary entry. Concurrent external mutation of the exact same key as an active maintained operation has no new ownership guarantee; maintained operations are protected from one another, and the public facade remains compatible rather than becoming an intercepting collection.

No existing method signature, output URL, API response, reader contract, or save-error delivery mechanism changes.

## Reservation Coordinator

`Session` owns a small private coordinator adjacent to the two public dictionaries. The coordinator contains:

- a monotonic generation counter;
- a private map from normalized full path to the current maintained generation; and
- one synchronization boundary covering maintained generation publication, generation comparison, public reservation publication/removal, and coordinated clear.

The coordinator returns an opaque handle containing the normalized path and generation. Only a matching handle may release its maintained reservation.

Generation numbers never reset during RAM clear. A pre-clear handle therefore cannot match or remove a post-clear reservation even if the same path is selected again.

All new fields follow the repository XML-documentation rule. The implementation uses explicit C# types, full braced blocks, and existing utility/task patterns.

### Save reservation acquisition

Path selection continues under `User.UserLock`. On-disk extensionless names are collected exactly as today. The final maintained reservation step atomically checks the public reservation set for the candidate extensionless path and publishes the new full-path reservation and private generation.

If another maintained or legacy public reservation appeared after the earlier collision snapshot, acquisition rejects the candidate and path selection advances to the next existing `[number]` or suffix candidate. This closes the publication race without introducing a directory cache or changing the collision namespace.

### Delete reservation acquisition

`ImageHistoryAPI.DeleteImage` requests a new generation for the standardized full path before invoking the configured delete/recycle action. A deletion is allowed to replace the reservation generation for that exact path. This makes the deletion the newer owner, so cleanup from a save that published the file earlier cannot erase the deletion safety window.

The existing file-existence check, configured delete action, sidecar loop, metadata removal, history-index removal, success response, and exception behavior remain unchanged.

## Pending-Byte Lifecycle

After a save path and reservation are selected, `SaveImage` creates the exact `Task<byte[]>` that readers will await and publishes it to `StillSavingFiles`. The same task reference is captured by cleanup.

The background operation retains its existing ordered work:

1. await `image.ActualFileTask` when present;
2. write the primary media bytes;
3. write eligible sidecar metadata;
4. create or load the preview;
5. upsert the history index;
6. emit the existing success debug log; and
7. on full success, wait ten seconds for immediate read-through.

The operation is enclosed by `try/finally`. The `finally` performs an atomic key-and-value removal: it removes the path only when `StillSavingFiles` still contains the exact captured task. It cannot remove a newer task published under the same path.

On full success, cleanup runs after the existing ten-second delay. Immediate readers therefore retain the same cache window, and later readers fall back to the completed disk file.

If conversion produces a null result, the conversion task faults, or any media, sidecar, preview, index, logging, or delay step throws, `finally` runs without waiting an additional ten seconds and removes the exact pending task. The original exception then reaches `Utilities.RunCheckedTask`, preserving its existing server-side checked-task logging and the existing fire-and-forget API contract.

Any synchronous setup failure after reservation or pending-task publication uses the same owner-aware cleanup before the existing outer `SaveImage` catch returns `("ERROR", null)`.

## Filename-Reservation Lifecycle

### Successful save

The save reservation remains active through the full successful pipeline and ten-second pending-byte window. The matching handle then removes its private generation and public reservation. The completed disk file remains in the existing directory collision set, so the name stays unavailable while that file exists.

If a deletion or another maintained operation has installed a newer generation for the same path, successful-save cleanup observes a mismatch and performs no mutation.

### Failed save

After pending-task cleanup, the matching save reservation becomes inactive and receives a ten-second expiry. This conservative window prevents immediate path reuse while failure logging and adjacent callers settle.

After ten seconds, expiry removes the private generation and public key only if the same generation still owns the path. A partial primary file, if present, continues to block reuse through the unchanged disk scan. If no file exists, the name becomes reusable after the safety window.

### Deletion

Deletion always transitions its matching generation to a ten-second inactive reservation in `finally`, whether the configured deletion and follow-up work succeed or throw. This preserves a short delete/regenerate safety window without retaining the name for process lifetime.

An older save or delete expiry cannot remove a newer generation. A repeated deletion replaces and restarts ownership for the exact path.

### Active expiry

Inactive failed-save and deletion reservations schedule an active ten-second expiry task. Expiry is not deferred until a later save, so an idle server releases maintained keys.

The delayed cleanup is generation-checked and nonthrowing. If delayed-task scheduling itself fails, the coordinator makes a best-effort immediate generation-checked removal rather than leaving a permanent reservation. This exceptional fallback may shorten the safety window but preserves the primary bounded-lifetime guarantee.

The number of delayed expiry tasks is bounded by failed saves and deletions that became inactive during the preceding ten seconds. Successful saves reuse their existing delay and do not add a second delayed task. Rank 12 makes no performance claim about this mechanism.

## Cleanup and Error Boundaries

Pending-task removal, reservation transition, delayed expiry, and coordinated clear are cleanup operations. Each cleanup helper is nonthrowing at its caller boundary:

- an old or repeated handle is a no-op;
- a generation mismatch is a no-op;
- pending cleanup removes only the captured task value;
- reservation cleanup removes only the captured generation;
- a cleanup failure cannot replace the original save or delete exception; and
- failure in a best-effort cleanup diagnostic cannot interrupt remaining cleanup.

The implementation must not convert the checked background task into a silently successful task. After `finally`, the original background exception continues to `Utilities.RunCheckedTask` for its existing log. Likewise, deletion retains its existing exception behavior.

Rank 12 bounds transient dictionaries; it does not claim transactional repair of files, sidecars, preview state, metadata state, or history-index state after a partially completed operation.

## Coordinated RAM Clear

`BackendAPI.FreeBackendMemory(system_ram: true)` calls a `Session` helper that clears:

1. the public `RecentlyBlockedFilenames` instance; and
2. the private maintained-generation map

inside the coordinator synchronization boundary. The public dictionary object is cleared, not replaced. The monotonic counter is not reset.

This preserves the existing administrator-visible behavior that RAM clear can discard current filename blocks, including active ones. Cleanup from an operation that began before the clear later sees no matching generation and cannot remove a newer post-clear reservation.

`StillSavingFiles` remains outside this administrative clear, matching current behavior. Its entries remain owned by their save pipelines and new `finally` cleanup.

## Concurrency Cases

The ownership protocol explicitly covers:

| Interleaving | Required result |
| --- | --- |
| Old save succeeds after a newer delete reserves the same path | Old save removes only its exact pending task; delete reservation remains |
| Old save fails after a newer delete reserves the same path | Old save cannot transition or expire the newer delete generation |
| Old delayed expiry fires after a newer save reserves the same path | Expiry sees a generation mismatch and does nothing |
| RAM clear occurs before old save cleanup | Old cleanup sees no matching generation and does nothing |
| New reservation occurs after RAM clear for the same path | Monotonic generation prevents the old handle from matching it |
| New pending task appears under a path before old task cleanup | Atomic key-and-value removal retains the newer task |
| Concurrent maintained save publishes a matching extensionless name | Atomic reservation acquisition rejects one candidate and advances its number/suffix |
| Legacy extension reservation exists for a candidate | Maintained acquisition treats its public key as blocked |
| Direct extension mutation races maintained cleanup on the same exact key | Public compatibility is preserved, but no new cross-owner guarantee is claimed |

## Files and Components

Expected production scope:

- `src/Accounts/Session.cs`
  - retain the public dictionaries;
  - add the private reservation coordinator and opaque handle;
  - make pending cleanup task-owned;
  - make save reservations generation-owned; and
  - expose the bounded internal operations required by deletion and RAM clear.
- `src/WebAPI/ImageHistoryAPI.cs`
  - acquire and terminally release a generation-owned deletion reservation.
- `src/WebAPI/BackendAPI.cs`
  - use coordinated reservation clearing for `system_ram`.

Expected unchanged consumers:

- `src/Core/WebServer.cs`;
- `src/Text2Image/T2IParamTypes.cs`;
- `src/WebAPI/T2IAPI.cs`;
- `src/BuiltinExtensions/GridGenerator/GridGeneratorExtension.cs`; and
- existing no-save branches.

Design, plan, and audit records are documentation-only companions. `docs/APIRoutes` is generated and must not be edited.

## Static Verification

Repository policy forbids agents from building, launching, or testing SwarmUI. Agent verification is source-only and must:

1. inventory every maintained `StillSavingFiles` and `RecentlyBlockedFilenames` read, write, remove, and clear;
2. trace every synchronous throw after reservation or pending-task publication;
3. trace every asynchronous throw from conversion through the successful delay;
4. prove each asynchronous path reaches pending-task `finally`;
5. prove pending cleanup compares the exact captured task;
6. prove save/delete/expiry cleanup compares the exact generation;
7. prove delayed scheduling failure cannot create a permanent maintained reservation;
8. prove successful cache retention is still ten seconds;
9. prove failed pending cleanup has no additional ten-second wait;
10. prove active saves have no time-based expiry;
11. prove successful saves, failed saves, deletions, repeated operations, and RAM clear have the lifecycle described above;
12. prove collision selection still compares extensionless disk and reservation paths and preserves `[number]`/suffix behavior;
13. prove public field names, field types, static field form, instances, and reader paths are unchanged;
14. prove existing save work order, URLs, error responses, checked-task logging, delete behavior, and no-save bypasses are unchanged;
15. prove no frontend, settings, launcher, extension, generated, upstream, user-data, or protected working-tree file entered the production diff; and
16. run the permitted whitespace/static-lint checks over the exact production range.

Static analysis cannot prove filesystem behavior, thread scheduling, extension runtime behavior, memory reclamation timing, platform behavior, or performance.

## Maintainer Validation Matrix

The maintainer performs all builds and live validation.

### Successful flow

1. Normal persisted T2I output returns its URL immediately, supports an immediate `/Output` or `/View` read, and remains readable from disk after ten seconds.
2. `FilePathToDataString` reads pending bytes before persistence completes and disk bytes after cleanup.
3. Image History add and Grid persisted output retain their existing behavior.
4. `DoNotSave`, `DoNotSaveIntermediates`, and user `SaveFiles = false` bypass transient publication exactly as before.
5. PNG/JPEG/WebP and non-image media retain extension, sidecar, preview, and index behavior.
6. `[number]` and suffix templates retain existing selected names.

### Failure cleanup

7. Null conversion result and faulted conversion remove the pending entry immediately and release the reservation after ten seconds.
8. Primary media write failure has the same cleanup behavior.
9. Sidecar write failure has the same cleanup behavior.
10. Preview failure has the same cleanup behavior.
11. History-index failure has the same cleanup behavior.
12. Existing server error logging occurs, no new client contract is introduced, and later reads are not pinned to a stranded pending task.
13. A partial disk file continues blocking reuse through the directory scan.

### Ownership and races

14. Rapid same-template saves receive distinct collision-free names.
15. Concurrent same-name saves cannot share a maintained reservation.
16. Delete during or immediately after a save installs the newer reservation, and older save cleanup does not erase it.
17. Immediate delete-then-regenerate remains blocked or advances its suffix during the ten-second window.
18. The deleted name becomes reusable after ten seconds when no disk file or newer reservation blocks it.
19. A stale failed-save or delete expiry cannot remove a newer save/delete reservation.
20. A stale pending cleanup cannot remove a newer pending task.
21. Multiple users and folders do not remove one another's reservations.

### Administrative and compatibility behavior

22. `FreeBackendMemory(system_ram: true)` clears current public/private reservations without replacing the public dictionary instance.
23. Pre-clear cleanup cannot remove a post-clear reservation for the same path.
24. `system_ram: false` does not clear reservations.
25. Existing external extension reads/writes against both public dictionary fields continue to load and function.
26. Maintained pending and inactive-reservation counts return to their expected baseline after successful windows and failed/delete expiry.

Validation should record operating system and filesystem, distinguish observed behavior from static evidence, and avoid a performance claim unless separately measured.

## Rollback

The production change is one coordinated ownership unit across `Session`, deletion, and RAM clear:

1. restore direct dictionary publication and removal in `Session.SaveImage`;
2. restore direct deletion reservation publication;
3. restore direct RAM-clear dictionary clearing; and
4. remove the private generation coordinator.

Partial rollback is unsafe because direct producers would bypass ownership metadata while owner-aware cleanup remains active. The unchanged public fields and readers make full rollback mechanically bounded.
