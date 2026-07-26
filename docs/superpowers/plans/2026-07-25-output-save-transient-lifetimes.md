# Output Save Transient Lifetimes Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Give pending output bytes and maintained filename reservations ownership-safe lifetimes without changing public extension fields, save/read/delete behavior, or output naming, while retaining partial or failed deletions until an explicit safety override.

**Architecture:** `Session` retains the two public concurrent dictionaries and owns a private generation-and-lifecycle reservation coordinator. Active saves and deletions never expire and survive system-RAM clear; failed saves and successful deletions transition synchronously to inactive ten-second reservations, while partial or failed deletions transition to inactive non-expiring reservations. `SaveImage` conditionally removes only its exact pending task, and `ImageHistoryAPI` plus `BackendAPI` use the same ownership state for deletion and coordinated RAM clear.

**Tech Stack:** C# 12, .NET 8, `ConcurrentDictionary`, `LockObject`, `Task`, FreneticUtilities, SwarmUI static API routes.

---

## Repository Constraints

- Work directly on `master`; do not create or use a worktree.
- Reaper176 is an approved maintainer under `AGENTS.md`.
- Agents must not build, launch, or test SwarmUI. The maintainer performs all runtime validation.
- Agents may run source searches, diff inspection, `git diff --check`, and permitted static linters.
- Never edit generated `docs/APIRoutes`.
- Never edit or stage user data, generated/build state, extensions, upstream code, or unrelated working-tree changes.
- Preserve the public field ABI and identity of:

```csharp
public static ConcurrentDictionary<string, string> RecentlyBlockedFilenames;
public static ConcurrentDictionary<string, Task<byte[]>> StillSavingFiles;
```

- Commit `e5fcebf7` is the stable pre-production design baseline. The current `docs/superpowers/specs/2026-07-25-output-save-transient-lifetimes-design.md` supersedes its lifecycle details with the integrated-review correction.

## File Map

- Modify `src/Accounts/Session.cs`
  - own private reservation generations and active/inactive lifecycle;
  - own atomic pending-task cleanup;
  - integrate reservation acquisition and terminal cleanup into `SaveImage`.
- Modify `src/WebAPI/ImageHistoryAPI.cs`
  - distinguish successful deletion expiry from partial/failed deletion retention.
- Modify `src/WebAPI/BackendAPI.cs`
  - clear legacy and inactive reservations while preserving active reservations.
- Modify `docs/superpowers/specs/2026-07-25-output-save-transient-lifetimes-design.md`
  - record the exact reviewed implementation and static evidence after source review.
- Modify `docs/superpowers/audits/2026-07-21-maintainability-architecture-refresh.md`
  - move Backend F19/F20/rank 12 to implemented, awaiting maintainer validation without advancing rank 13.

Expected unchanged source consumers:

- `src/Core/WebServer.cs`
- `src/Text2Image/T2IParamTypes.cs`
- `src/WebAPI/T2IAPI.cs`
- `src/BuiltinExtensions/GridGenerator/GridGeneratorExtension.cs`

## Task 1: Add Ownership-Safe Reservation Primitives

**Files:**

- Modify: `src/Accounts/Session.cs:209-214`

- [ ] **Step 1: Reconfirm the public surface and complete call-site inventory**

Run:

```bash
rg -n "StillSavingFiles|RecentlyBlockedFilenames" src \
  --glob '*.cs' \
  --glob '!src/bin/**' \
  --glob '!src/obj/**' \
  --glob '!src/Extensions/**'
```

Expected maintained inventory:

- public field definitions and `SaveImage` publication/removal in `Session.cs`;
- reads in `WebServer.cs` and `T2IParamTypes.cs`;
- deletion publication in `ImageHistoryAPI.cs`; and
- system-RAM clear in `BackendAPI.cs`.

- [ ] **Step 2: Add the opaque handle and private coordinator state**

Immediately after the two unchanged public dictionary fields, add:

```csharp
    /// <summary>Opaque ownership handle for one maintained output filename reservation.</summary>
    internal readonly record struct OutputFilenameReservation(string Path, long Generation);

    /// <summary>Serializes maintained output filename reservation publication, release, and clearing.</summary>
    private static readonly LockObject OutputFilenameReservationLock = new();

    /// <summary>Generation and lifecycle state for one maintained output filename reservation.</summary>
    private readonly record struct OutputFilenameReservationState(long Generation, bool IsActive);

    /// <summary>Current maintained output filename reservation state for each normalized full path.</summary>
    private static readonly Dictionary<string, OutputFilenameReservationState> MaintainedOutputFilenameReservations = [];

    /// <summary>Monotonic generation source for maintained output filename reservations.</summary>
    private static long OutputFilenameReservationGeneration;

    /// <summary>How long a failed save or successful deletion keeps an expiring inactive filename reservation.</summary>
    private static readonly TimeSpan InactiveOutputFilenameReservationLifetime = TimeSpan.FromSeconds(10);
```

Do not rename, retype, convert, or reassign either public dictionary.

- [ ] **Step 3: Add nonthrowing cleanup diagnostics**

Add this private helper before `SaveImage`:

```csharp
    /// <summary>Best-effort diagnostic for output transient-state cleanup failures.</summary>
    private static void LogOutputCleanupFailure(string action, Exception ex)
    {
        try
        {
            Logs.Error($"Internal error while {action}: {ex.ReadableString()}");
        }
        catch
        {
            // Cleanup diagnostics must not replace the save or deletion failure being handled.
        }
    }
```

Every later cleanup catch must call only this helper, not direct throwable formatting or logging.

- [ ] **Step 4: Add atomic maintained save reservation acquisition**

Add:

```csharp
    /// <summary>Attempts to reserve a persisted output path without colliding with any extensionless public reservation.</summary>
    private static bool TryReserveOutputFilename(string fullPath, out OutputFilenameReservation reservation)
    {
        lock (OutputFilenameReservationLock)
        {
            string fullPathNoExt = fullPath.BeforeLast('.');
            if (RecentlyBlockedFilenames.Keys.Any(path => path.BeforeLast('.') == fullPathNoExt))
            {
                reservation = default;
                return false;
            }
            bool hadPreviousState = MaintainedOutputFilenameReservations.TryGetValue(fullPath, out OutputFilenameReservationState previousState);
            long generation = Interlocked.Increment(ref OutputFilenameReservationGeneration);
            MaintainedOutputFilenameReservations[fullPath] = new OutputFilenameReservationState(generation, true);
            try
            {
                RecentlyBlockedFilenames[fullPath] = fullPath;
            }
            catch
            {
                if (hadPreviousState)
                {
                    MaintainedOutputFilenameReservations[fullPath] = previousState;
                }
                else
                {
                    MaintainedOutputFilenameReservations.Remove(fullPath);
                }
                throw;
            }
            reservation = new OutputFilenameReservation(fullPath, generation);
            return true;
        }
    }
```

This final acquisition check is under the coordinator lock. It preserves the existing extensionless collision namespace and catches a reservation published after the earlier directory/public snapshot.

- [ ] **Step 5: Add deletion reservation acquisition**

Add:

```csharp
    /// <summary>Publishes a newer maintained reservation for a path being deleted.</summary>
    internal static OutputFilenameReservation ReserveDeletedOutputFilename(string fullPath)
    {
        lock (OutputFilenameReservationLock)
        {
            bool hadPreviousState = MaintainedOutputFilenameReservations.TryGetValue(fullPath, out OutputFilenameReservationState previousState);
            long generation = Interlocked.Increment(ref OutputFilenameReservationGeneration);
            MaintainedOutputFilenameReservations[fullPath] = new OutputFilenameReservationState(generation, true);
            try
            {
                RecentlyBlockedFilenames[fullPath] = fullPath;
            }
            catch
            {
                if (hadPreviousState)
                {
                    MaintainedOutputFilenameReservations[fullPath] = previousState;
                }
                else
                {
                    MaintainedOutputFilenameReservations.Remove(fullPath);
                }
                throw;
            }
            return new OutputFilenameReservation(fullPath, generation);
        }
    }
```

Deletion intentionally replaces the maintained generation for the exact path so cleanup from the save that created the file cannot erase the new delete window.

- [ ] **Step 6: Add generation-checked release**

Add:

```csharp
    /// <summary>Best-effort removal of a maintained filename reservation when the handle still owns the path.</summary>
    private static void RemoveOutputFilenameReservation(OutputFilenameReservation reservation)
    {
        try
        {
            lock (OutputFilenameReservationLock)
            {
                if (!MaintainedOutputFilenameReservations.TryGetValue(reservation.Path, out OutputFilenameReservationState currentState)
                    || currentState.Generation != reservation.Generation)
                {
                    return;
                }
                MaintainedOutputFilenameReservations.Remove(reservation.Path);
                RecentlyBlockedFilenames.TryRemove(reservation.Path, out _);
            }
        }
        catch (Exception ex)
        {
            LogOutputCleanupFailure($"removing output filename reservation '{reservation.Path}'", ex);
        }
    }

    /// <summary>Immediately releases a matching successful-save filename reservation.</summary>
    private static void ReleaseOutputFilenameReservation(OutputFilenameReservation reservation)
    {
        RemoveOutputFilenameReservation(reservation);
    }
```

Do not retry a mismatched handle and do not remove a public key outside the matching-generation branch.

- [ ] **Step 7: Add synchronous inactive transition, delayed expiry, and retained-failure handling**

Add:

```csharp
    /// <summary>Transitions a matching active reservation to inactive before its terminal policy is selected.</summary>
    private static bool TryTransitionOutputFilenameReservationToInactive(OutputFilenameReservation reservation)
    {
        try
        {
            lock (OutputFilenameReservationLock)
            {
                if (!MaintainedOutputFilenameReservations.TryGetValue(reservation.Path, out OutputFilenameReservationState currentState)
                    || currentState.Generation != reservation.Generation
                    || !currentState.IsActive)
                {
                    return false;
                }
                MaintainedOutputFilenameReservations[reservation.Path] = currentState with { IsActive = false };
                return true;
            }
        }
        catch (Exception ex)
        {
            LogOutputCleanupFailure($"transitioning output filename reservation '{reservation.Path}' to inactive", ex);
            return false;
        }
    }

    /// <summary>Transitions a matching failed-save or successful-deletion reservation to inactive and releases it after the safety window.</summary>
    internal static void ReleaseOutputFilenameReservationAfterDelay(OutputFilenameReservation reservation)
    {
        if (!TryTransitionOutputFilenameReservationToInactive(reservation))
        {
            return;
        }
        try
        {
            _ = Utilities.RunCheckedTask(async () =>
            {
                await Task.Delay(InactiveOutputFilenameReservationLifetime);
                RemoveOutputFilenameReservation(reservation);
            }, "output filename reservation expiry");
        }
        catch (Exception ex)
        {
            LogOutputCleanupFailure($"scheduling output filename reservation expiry for '{reservation.Path}'", ex);
            RemoveOutputFilenameReservation(reservation);
        }
    }

    /// <summary>Transitions a matching failed-deletion reservation to inactive without automatic expiry.</summary>
    internal static void RetainOutputFilenameReservationUntilClear(OutputFilenameReservation reservation)
    {
        TryTransitionOutputFilenameReservationToInactive(reservation);
    }
```

The transition happens under the coordinator lock before scheduling, so an inactive reservation can be removed by system-RAM clear and no scheduling interval leaves it falsely active. The immediate exact-generation fallback is intentional: scheduling failure may shorten the failed-save or successful-deletion window but cannot strand it. `RetainOutputFilenameReservationUntilClear` schedules nothing; explicit system-RAM clear or restart is its only release.

- [ ] **Step 8: Add atomic pending-task cleanup**

Add:

```csharp
    /// <summary>Removes a pending output entry only when it still contains the captured task.</summary>
    private static void RemoveStillSavingFile(string fullPath, Task<byte[]> pendingTask)
    {
        try
        {
            StillSavingFiles.TryRemove(new KeyValuePair<string, Task<byte[]>>(fullPath, pendingTask));
        }
        catch (Exception ex)
        {
            LogOutputCleanupFailure($"removing pending output bytes for '{fullPath}'", ex);
        }
    }
```

The .NET 8 `TryRemove(KeyValuePair<TKey, TValue>)` overload requires both key and value to match. Do not replace it with `TryGetValue` followed by `TryRemove(key, out _)`, which would have a race.

- [ ] **Step 9: Add coordinated administrative clear that preserves active ownership**

Add:

```csharp
    /// <summary>Clears inactive maintained and legacy public reservations while preserving active maintained ownership.</summary>
    internal static void ClearOutputFilenameReservations()
    {
        try
        {
            lock (OutputFilenameReservationLock)
            {
                HashSet<string> activePaths = [.. MaintainedOutputFilenameReservations
                    .Where(pair => pair.Value.IsActive)
                    .Select(pair => pair.Key)];
                foreach (KeyValuePair<string, OutputFilenameReservationState> pair in MaintainedOutputFilenameReservations.ToArray())
                {
                    if (!pair.Value.IsActive)
                    {
                        MaintainedOutputFilenameReservations.Remove(pair.Key);
                        RecentlyBlockedFilenames.TryRemove(pair.Key, out _);
                    }
                }
                foreach (string path in RecentlyBlockedFilenames.Keys)
                {
                    if (!activePaths.Contains(path))
                    {
                        RecentlyBlockedFilenames.TryRemove(path, out _);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            LogOutputCleanupFailure("clearing output filename reservations", ex);
        }
    }
```

The public dictionary is never cleared and repopulated, so maintained acquisition cannot observe a same-path gap under the shared lock. Do not clear `StillSavingFiles`, remove active private/public pairs, replace the public dictionary instance, or reset `OutputFilenameReservationGeneration`. Direct external exact-key mutation remains outside the lock and retains the approved compatibility caveat.

- [ ] **Step 10: Perform static syntax and contract review**

Run:

```bash
rg -n "OutputFilenameReservation|OutputFilenameReservationState|OutputFilenameReservationGeneration|MaintainedOutputFilenameReservations|InactiveOutputFilenameReservationLifetime|TryReserveOutputFilename|ReserveDeletedOutputFilename|TryTransitionOutputFilenameReservationToInactive|ReleaseOutputFilenameReservation|RetainOutputFilenameReservationUntilClear|RemoveStillSavingFile|ClearOutputFilenameReservations" src/Accounts/Session.cs
git diff --check -- src/Accounts/Session.cs
git diff -- src/Accounts/Session.cs | rg -n '^[+-].*public static ConcurrentDictionary'
```

Expected:

- every new field has XML documentation;
- every generation comparison reads `.Generation`, and acquisition rollback restores the prior complete state;
- maintained acquisition and coordinated clear share the same lock;
- explicit types and full braces are used;
- the two public dictionary declaration lines are unchanged; and
- whitespace is clean.

- [ ] **Step 11: Commit the coordinator primitives**

```bash
git add src/Accounts/Session.cs
git diff --cached --name-only
git commit -m "refactor: own output filename reservations"
```

Expected staged scope: only `src/Accounts/Session.cs`.

## Task 2: Give `SaveImage` Exact Pending and Reservation Lifetimes

**Files:**

- Modify: `src/Accounts/Session.cs:217-292`

- [ ] **Step 1: Separate disk collision discovery from atomic reservation acquisition**

Replace the current union of directory files and public reservation keys:

```csharp
HashSet<string> existingFiles = [.. Directory.EnumerateFiles(folderRoute).Union(RecentlyBlockedFilenames.Keys.Where(f => f.StartsWith(folderRoute))).Select(f => f.BeforeLast('.'))];
```

with disk-only discovery:

```csharp
                HashSet<string> existingFiles = [.. Directory.EnumerateFiles(folderRoute).Select(f => f.BeforeLast('.'))];
```

Initialize the handle before the numbering loop:

```csharp
                OutputFilenameReservation reservation = default;
```

Change the loop condition to:

```csharp
                while (existingFiles.Contains(fullPathNoExt) || !TryReserveOutputFilename(fullPath, out reservation))
```

Retain the loop body exactly, including the existing `num` progression, `[number]` replacement, suffix fallback, path normalization, and extension.

Delete the direct line:

```csharp
RecentlyBlockedFilenames[fullPath] = fullPath;
```

The successful exit from the loop now owns the reservation.

- [ ] **Step 2: Capture and publish the exact pending task under a setup boundary**

Replace direct pending publication and unchecked scheduling with:

```csharp
                Task<byte[]> pendingTask = null;
                try
                {
                    pendingTask = image.ActualFileTask is null
                        ? Task.FromResult(image.File.RawData)
                        : Task.Run(async () => (await image.ActualFileTask).RawData);
                    StillSavingFiles[fullPath] = pendingTask;
                    _ = Utilities.RunCheckedTask(async () =>
                    {
                        bool saveSucceeded = false;
                        try
                        {
                            MediaFile actualFile = image.ActualFileTask is null ? image.File : await image.ActualFileTask;
                            File.WriteAllBytes(fullPath, actualFile.RawData);
                            if ((User.Settings.FileFormat.SaveTextFileMetadata || extension == "webp" || !OutputMetadataTracker.ExtensionsWithMetadata.Contains(extension)) && !string.IsNullOrWhiteSpace(metadata))
                            {
                                if (extension == "webp" && actualFile is ImageFile imageFile && imageFile.ToIS.Frames.Count == 1)
                                {
                                    // no .json write for still-image webps
                                }
                                else
                                {
                                    File.WriteAllBytes(fullPathNoExt + ".swarm.json", metadata.EncodeUTF8());
                                }
                            }
                            OutputMetadataTracker.GetOrCreatePreviewFor(fullPath.Replace('\\', '/'));
                            OutputMetadataTracker.UpsertHistoryIndexForFile(fullPath.Replace('\\', '/'), root, User.Settings.StarNoFolders);
                            Logs.Debug($"Saved an output file as '{fullPath}'");
                            await Task.Delay(TimeSpan.FromSeconds(10));
                            saveSucceeded = true;
                        }
                        finally
                        {
                            RemoveStillSavingFile(fullPath, pendingTask);
                            if (saveSucceeded)
                            {
                                ReleaseOutputFilenameReservation(reservation);
                            }
                            else
                            {
                                ReleaseOutputFilenameReservationAfterDelay(reservation);
                            }
                        }
                    }, "output file save");
                }
                catch
                {
                    if (pendingTask is not null)
                    {
                        RemoveStillSavingFile(fullPath, pendingTask);
                    }
                    ReleaseOutputFilenameReservationAfterDelay(reservation);
                    throw;
                }
```

The existing outer `try/catch` remains responsible for logging the synchronous error and returning `("ERROR", null)`. Failed-save cleanup intentionally uses delayed release: companion writes follow the primary media write, so a companion cannot survive without a primary disk path that independently blocks stem reuse.

- [ ] **Step 3: Move the unchanged background save body into the inner `try`**

The complete inner body before the delay must remain:

```csharp
                            MediaFile actualFile = image.ActualFileTask is null ? image.File : await image.ActualFileTask;
                            File.WriteAllBytes(fullPath, actualFile.RawData);
                            if ((User.Settings.FileFormat.SaveTextFileMetadata || extension == "webp" || !OutputMetadataTracker.ExtensionsWithMetadata.Contains(extension)) && !string.IsNullOrWhiteSpace(metadata))
                            {
                                if (extension == "webp" && actualFile is ImageFile imageFile && imageFile.ToIS.Frames.Count == 1)
                                {
                                    // no .json write for still-image webps
                                }
                                else
                                {
                                    File.WriteAllBytes(fullPathNoExt + ".swarm.json", metadata.EncodeUTF8());
                                }
                            }
                            OutputMetadataTracker.GetOrCreatePreviewFor(fullPath.Replace('\\', '/'));
                            OutputMetadataTracker.UpsertHistoryIndexForFile(fullPath.Replace('\\', '/'), root, User.Settings.StarNoFolders);
                            Logs.Debug($"Saved an output file as '{fullPath}'");
```

Do not catch the background exception inside this inner `try`. Its `finally` must run, then the exception must continue to `Utilities.RunCheckedTask` for the existing readable server log.

- [ ] **Step 4: Remove the old unconditional cleanup**

Delete the old final line:

```csharp
StillSavingFiles.TryRemove(fullPath, out _);
```

There must be exactly one pending removal helper call in background `finally` and one guarded setup-failure call.

- [ ] **Step 5: Trace every terminal path statically**

Use numbered source output:

```bash
nl -ba src/Accounts/Session.cs | sed -n '205,380p'
```

Confirm:

1. no-save returns before reservation;
2. invalid-format fallback remains before reservation;
3. directory/path setup failures occur before reservation;
4. successful loop exit owns exactly one reservation;
5. pending creation/publication/scheduling failure reaches the setup catch;
6. conversion-null/fault, media write, sidecar write, preview, index, log, and delay exceptions reach background `finally`;
7. success waits ten seconds before exact pending removal and immediate reservation release;
8. failure removes pending immediately, transitions its matching reservation inactive, and schedules exact-generation expiry;
9. background exceptions still escape to `RunCheckedTask`; and
10. the method returns the same URL/path tuple immediately after scheduling.

- [ ] **Step 6: Verify public and caller contracts remain unchanged**

Run:

```bash
git diff -- src/Accounts/Session.cs
git diff -- src/Accounts/Session.cs | rg -n '^[+-].*(public|protected)\\b'
rg -n "SaveImage\\(" src \
  --glob '*.cs' \
  --glob '!src/bin/**' \
  --glob '!src/obj/**' \
  --glob '!src/Extensions/**'
git diff --check -- src/Accounts/Session.cs
```

Expected public diff: only the new internal nested handle and unchanged public dictionary context; `SaveImage` signature and both dictionary declarations remain unchanged.

- [ ] **Step 7: Commit save lifetime ownership**

```bash
git add src/Accounts/Session.cs
git diff --cached --name-only
git commit -m "fix: bound pending output save state"
```

Expected staged scope: only `src/Accounts/Session.cs`.

## Task 3: Integrate Deletion and RAM Clear

**Files:**

- Modify: `src/WebAPI/ImageHistoryAPI.cs:1190-1227`
- Modify: `src/WebAPI/BackendAPI.cs:823-831`

- [ ] **Step 1: Replace permanent deletion publication with an owned handle**

In `DeleteImage`, replace:

```csharp
        Session.RecentlyBlockedFilenames[standardizedPath] = standardizedPath;
        Action<string> deleteFile = Program.ServerSettings.Paths.RecycleDeletedImages ? Utilities.SendFileToRecycle : File.Delete;
```

with:

```csharp
        Action<string> deleteFile = Program.ServerSettings.Paths.RecycleDeletedImages ? Utilities.SendFileToRecycle : File.Delete;
        Session.OutputFilenameReservation reservation = Session.ReserveDeletedOutputFilename(standardizedPath);
```

- [ ] **Step 2: Enclose existing deletion work in outcome-aware `try/finally`**

The configured delete action must be selected before reservation acquisition. Initialize `deletionSucceeded` to false, and set it true only after primary deletion, sidecar deletion, metadata removal, index removal, and success-response construction:

```csharp
        bool deletionSucceeded = false;
        try
        {
            deleteFile(path);
            string fileBase = path.BeforeLast('.');
            foreach (string str in T2IAPI.DeletableFileExtensions)
            {
                string altFile = $"{fileBase}{str}";
                if (File.Exists(altFile))
                {
                    deleteFile(altFile);
                }
            }
            OutputMetadataTracker.RemoveMetadataFor(path);
            RemoveHistoryIndexForPath(root, path);
            JObject response = new() { ["success"] = true };
            deletionSucceeded = true;
            return response;
        }
        finally
        {
            if (deletionSucceeded)
            {
                Session.ReleaseOutputFilenameReservationAfterDelay(reservation);
            }
            else
            {
                Session.RetainOutputFilenameReservationUntilClear(reservation);
            }
        }
```

Do not add a catch or change existing exception propagation, error objects, recycle selection, sidecar list, metadata, or index operations. A successful deletion transitions inactive and expires after ten seconds. A partial or failed deletion transitions inactive without expiry so a removed primary cannot expose a stale double-extension sidecar to same-stem reuse. The original exception remains unchanged.

- [ ] **Step 3: Coordinate system-RAM clearing**

In `BackendAPI.FreeBackendMemory`, replace:

```csharp
Session.RecentlyBlockedFilenames.Clear();
```

with:

```csharp
Session.ClearOutputFilenameReservations();
```

Do not change `system_ram: false`, backend selection, backend memory calls, result counts, or `Utilities.CleanRAM`.

- [ ] **Step 4: Re-run the complete transient-state inventory**

Run:

```bash
rg -n "StillSavingFiles|RecentlyBlockedFilenames|ReserveDeletedOutputFilename|RetainOutputFilenameReservationUntilClear|ClearOutputFilenameReservations" src \
  --glob '*.cs' \
  --glob '!src/bin/**' \
  --glob '!src/obj/**' \
  --glob '!src/Extensions/**'
```

Expected:

- direct maintained reservation writes exist only inside the coordinator;
- maintained public removal exists only inside the coordinator;
- both pending readers are unchanged;
- deletion and RAM clear use internal coordinator methods.

- [ ] **Step 5: Review exception and race behavior**

Statically trace:

1. missing-file deletion returns before reserving;
2. every post-reservation deletion success or throw enters `finally`;
3. `deletionSucceeded` becomes true only after all deletion work and response construction;
4. successful deletion becomes inactive and expires after ten seconds;
5. partial or failed deletion becomes inactive, does not expire, and preserves the original exception;
6. a newer deletion generation defeats older save cleanup;
7. delayed delete cleanup cannot erase a newer save/delete generation;
8. RAM clear removes inactive and legacy entries but continuously preserves active private/public pairs under one lock;
9. RAM clear does not touch `StillSavingFiles`, replace the public instance, or reset the counter; and
10. pre-clear delayed cleanup cannot match a newer post-clear generation.

- [ ] **Step 6: Run permitted static checks**

```bash
git diff --check -- src/Accounts/Session.cs src/WebAPI/ImageHistoryAPI.cs src/WebAPI/BackendAPI.cs
git diff -- src/WebAPI/ImageHistoryAPI.cs src/WebAPI/BackendAPI.cs
git diff --name-only e5fcebf7..HEAD
```

Do not build, test, launch, or call live APIs.

- [ ] **Step 7: Commit consumer integration**

```bash
git add src/WebAPI/ImageHistoryAPI.cs src/WebAPI/BackendAPI.cs
git diff --cached --name-only
git commit -m "fix: own deletion reservation outcomes"
```

Expected staged scope: exactly the two API source files.

## Task 4: Integrated Static and Independent Review

**Files:**

- Review: `src/Accounts/Session.cs`
- Review: `src/WebAPI/ImageHistoryAPI.cs`
- Review: `src/WebAPI/BackendAPI.cs`
- Review: `docs/superpowers/specs/2026-07-25-output-save-transient-lifetimes-design.md`

- [ ] **Step 1: Pin the stable Rank 12 range**

Use the approved pre-production design commit as the stable base. Do not recompute the base from the latest plan commit: later design, plan, source, and focused correction commits must all remain visible in the final Rank 12 range.

```bash
rank12_range_base="$(git rev-parse e5fcebf7)"
rank12_range_head="$(git rev-parse HEAD)"
git log --oneline --decorate -8
git log --oneline "$rank12_range_base..$rank12_range_head"
git diff --name-only "$rank12_range_base..$rank12_range_head" -- \
  src/Accounts/Session.cs src/WebAPI/ImageHistoryAPI.cs src/WebAPI/BackendAPI.cs
git diff --stat "$rank12_range_base..$rank12_range_head"
git diff --check "$rank12_range_base..$rank12_range_head"
```

Expected production source scope within the complete range: exactly:

```text
src/Accounts/Session.cs
src/WebAPI/ImageHistoryAPI.cs
src/WebAPI/BackendAPI.cs
```

- [ ] **Step 2: Run the design conformance inventory**

```bash
rg -n "StillSavingFiles|RecentlyBlockedFilenames|OutputFilenameReservation|OutputFilenameReservationState|TryReserveOutputFilename|ReserveDeletedOutputFilename|ReleaseOutputFilenameReservation|RetainOutputFilenameReservationUntilClear|RemoveStillSavingFile|ClearOutputFilenameReservations|Task.Delay|RunCheckedTask" \
  src/Accounts/Session.cs \
  src/WebAPI/ImageHistoryAPI.cs \
  src/WebAPI/BackendAPI.cs \
  src/Core/WebServer.cs \
  src/Text2Image/T2IParamTypes.cs
```

Compare every result to the approved design's ownership, timing, and unchanged-reader rules.

- [ ] **Step 3: Check public ABI and protected scope**

```bash
rank12_range_base="$(git rev-parse e5fcebf7)"
rank12_range_head="$(git rev-parse HEAD)"
git diff "$rank12_range_base..$rank12_range_head" -- src/Accounts/Session.cs | rg -n '^[+-].*(public|protected)\\b'
git diff "$rank12_range_base..$rank12_range_head" -- src/Core/WebServer.cs src/Text2Image/T2IParamTypes.cs src/WebAPI/T2IAPI.cs src/BuiltinExtensions/GridGenerator/GridGeneratorExtension.cs
git diff --name-only "$rank12_range_base..$rank12_range_head" -- src/wwwroot src/Pages docs/APIRoutes src/Extensions
```

Expected:

- no removal/type/form change to either public dictionary;
- no `SaveImage` signature change;
- no expected-unchanged consumer diff; and
- no frontend, generated API, or extension diff.

- [ ] **Step 4: Dispatch a fresh specification reviewer**

The reviewer must verify:

- all approved goals and non-goals;
- exact successful/failure timing;
- setup and asynchronous exception coverage;
- exact task-value removal;
- generation and active/inactive ownership across save/delete/expiry/retention/RAM clear;
- synchronous inactive transition before delayed scheduling;
- successful-deletion expiry versus partial/failed-deletion retention;
- continuous preservation of active public/private pairs during RAM clear;
- extension ABI/source boundary;
- collision behavior and direct-extension caveat;
- unchanged URLs, readers, metadata order, delete behavior, and error contract;
- exact three-file production scope; and
- no agent runtime claims.

Correct findings in a new focused source commit, then repeat this review until it passes.

- [ ] **Step 5: Dispatch a fresh code-quality reviewer**

The reviewer must inspect:

- clarity and minimality;
- field documentation and repository C# style;
- lock ordering and absence of blocking work under the coordinator lock;
- generation monotonicity;
- public/private map consistency and complete-state rollback;
- nonthrowing cleanup diagnostics;
- delayed-task failure fallback;
- non-expiring failed-deletion safety and its RAM-clear override;
- stale task/reservation races;
- direct extension mutation boundary;
- duplication and naming; and
- clean exact-range whitespace.

Correct findings in a focused source commit, rerun specification review if behavior changed, then repeat quality review until approved.

## Task 4A: Apply the Integrated-Review Safety Correction

**Files:**

- Review and, if still needed, modify: `src/Accounts/Session.cs`
- Review and, if still needed, modify: `src/WebAPI/ImageHistoryAPI.cs`
- Review and, if still needed, modify: `src/WebAPI/BackendAPI.cs`
- Record: `docs/superpowers/specs/2026-07-25-output-save-transient-lifetimes-design.md`

This task records an approved correction to be implemented and reviewed; it is not evidence that source code is already corrected.

- [ ] **Step 1: Confirm the two integrated-review findings against source**

Use numbered source and the transient-state inventory to demonstrate whether:

1. a system-RAM clear can remove an active maintained save or deletion reservation and permit same-path reuse; and
2. a deletion that removes primary media but fails on a double-extension sidecar can later expose that stem to `SaveImage`.

Do not edit until both findings are traced through the actual source state.

- [ ] **Step 2: Correct lifecycle state and RAM clear if required**

Require the Task 1 state record and helpers exactly: active save/deletion state never expires, delayed release transitions inactive before scheduling, failed-deletion retention is inactive without expiry, and clear removes only inactive/legacy entries while preserving active private/public pairs continuously under the coordinator lock.

- [ ] **Step 3: Correct deletion outcome handling if required**

Require the Task 3 `deletionSucceeded` branch exactly. Success is assigned only after primary, sidecar, metadata/index, and response construction complete; `finally` selects delayed release on success and `RetainOutputFilenameReservationUntilClear` on failure without replacing the original exception.

- [ ] **Step 4: Commit only the focused source correction**

```bash
git add src/Accounts/Session.cs src/WebAPI/ImageHistoryAPI.cs src/WebAPI/BackendAPI.cs
git diff --cached --name-only
git diff --cached --check
git commit -m "fix: preserve active output reservations"
```

Stage only files that actually require correction. If one of the three source files is unchanged, do not stage it. This correction commit remains inside the stable `e5fcebf7..HEAD` Rank 12 range.

- [ ] **Step 5: Repeat both fresh reviews**

Repeat Task 4's specification and code-quality reviews over the complete stable range. The correction is accepted only when both reviews find no remaining contradiction among active-state clearing, deletion failure retention, successful deletion expiry, failed-save expiry, and the direct-extension exact-key caveat.

## Task 5: Record Static Implementation Closure

**Files:**

- Modify: `docs/superpowers/specs/2026-07-25-output-save-transient-lifetimes-design.md`
- Modify: `docs/superpowers/audits/2026-07-21-maintainability-architecture-refresh.md`

- [ ] **Step 1: Add the implementation record to the design**

Append:

- exact production base/head and every production commit;
- exact three-file source scope and diff stat;
- final generation/activity coordinator, save, deletion-outcome, and active-preserving RAM-clear behavior;
- the integrated-review findings and focused correction commit;
- specification and quality review outcomes;
- permitted static commands and their results;
- unchanged public fields/readers/contracts;
- direct-extension same-key race caveat;
- successful-delete and failed-save ten-second expiry plus the failed-deletion retain-until-clear exception;
- no agent build/test/runtime/performance claim; and
- status `Implemented; awaiting maintainer validation`.

- [ ] **Step 2: Update every current Rank 12 audit status**

In the audit:

- mark Backend F19 and Backend F20/rank 12 consistently as implemented, awaiting maintainer validation;
- preserve the original problem evidence and bounded caveats;
- record exact source range, scope, timing, ownership, compatibility, and static reviews;
- keep ranks 1, 2, and 8 awaiting their own validation;
- retain every already validated rank;
- keep rank 12 as the recommended next project until its maintainer matrix passes;
- do not advance or design rank 13; and
- preserve all 12 top-level audit sections and all 32 numbered roadmap entries.

- [ ] **Step 3: Self-review documentation**

```bash
rg -n "TBD|TODO|placeholder|Rank 12|rank 12|Backend F19|Backend F20|awaiting maintainer" \
  docs/superpowers/specs/2026-07-25-output-save-transient-lifetimes-design.md \
  docs/superpowers/audits/2026-07-21-maintainability-architecture-refresh.md
git diff --check -- \
  docs/superpowers/specs/2026-07-25-output-save-transient-lifetimes-design.md \
  docs/superpowers/audits/2026-07-21-maintainability-architecture-refresh.md
```

Resolve stale current-status wording while retaining historical plan provenance.

- [ ] **Step 4: Commit documentation-only closure**

```bash
git add \
  docs/superpowers/specs/2026-07-25-output-save-transient-lifetimes-design.md \
  docs/superpowers/audits/2026-07-21-maintainability-architecture-refresh.md
git diff --cached --name-only
git commit -m "docs: record output transient lifetime ownership"
```

Expected staged scope: exactly the two documentation files.

- [ ] **Step 5: Dispatch fresh documentation specification and quality reviews**

Require both reviewers to verify:

- exact production facts;
- accurate static/runtime distinction;
- all compatibility and failure caveats;
- Rank 12 still awaiting validation;
- rank 13 not advanced;
- ranks 1, 2, and 8 still pending;
- audit structure and roadmap count;
- documentation-only commit scope; and
- clean whitespace.

Correct and recommit documentation findings, then repeat both reviews.

## Task 6: Maintainer Validation and Final Record

**Files:**

- Modify after explicit maintainer result: `docs/superpowers/specs/2026-07-25-output-save-transient-lifetimes-design.md`
- Modify after explicit maintainer result: `docs/superpowers/audits/2026-07-21-maintainability-architecture-refresh.md`

- [ ] **Step 1: Hand the maintainer the exact 32-case matrix**

Use the approved design's “Maintainer Validation Matrix” without shortening it. The maintainer must build and exercise:

- successful T2I, history-add, Grid, no-save, format, template, pending-read, and disk-read flows;
- conversion-null/fault, media, sidecar, preview, and history-index failures;
- exact error/logging and partial-file behavior;
- rapid/concurrent saves;
- delete/save/expiry/pending-task ownership races;
- successful-deletion ten-second reuse;
- partial and failed deletion retain-until-clear behavior, including stale double-extension sidecars;
- users/folders;
- RAM clear true/false, active-save preservation, active-deletion preservation, and pre/post-clear ownership;
- extension field compatibility; and
- expiring dictionary counts returning to baseline while retained failed-deletion counts remain until clear or restart.

Record operating system/filesystem and the maintainer's exact result. Do not infer a pass.

- [ ] **Step 2: If validation fails, diagnose before editing**

Use `superpowers:systematic-debugging`. Reproduce only through maintainer-supplied evidence, trace the responsible lifecycle, update the design if the contract changes, implement the smallest correction, repeat source reviews, and return the affected matrix cases to the maintainer.

- [ ] **Step 3: If validation passes, update current status**

Only after explicit confirmation:

- mark Rank 12 implemented and maintainer-validated on the confirmed platform;
- record maintainer name, date, matrix scope, and outcomes;
- retain unvalidated platforms and performance caveats;
- retain direct-extension same-key, partial-output, failed-deletion retention, and administrative-override caveats;
- keep agent evidence static-only;
- advance the audit's recommended-next pointer to rank 13 without implementing or designing it; and
- leave ranks 1, 2, and 8 unchanged.

- [ ] **Step 4: Commit validation documentation only**

```bash
git add \
  docs/superpowers/specs/2026-07-25-output-save-transient-lifetimes-design.md \
  docs/superpowers/audits/2026-07-21-maintainability-architecture-refresh.md
git diff --cached --name-only
git commit -m "docs: validate output transient lifetimes"
```

- [ ] **Step 5: Run final fresh reviews and static verification**

Dispatch fresh validation-record specification and quality reviewers. Then run:

```bash
rank12_range_base="$(git rev-parse e5fcebf7)"
rank12_range_head="$(git rev-parse HEAD)"
rank12_validation_commit="$(git log -1 --format=%H -- docs/superpowers/specs/2026-07-25-output-save-transient-lifetimes-design.md)"
git log --oneline "$rank12_range_base..$rank12_range_head"
git diff --check "$rank12_range_base..$rank12_range_head"
git show --check --oneline "$rank12_validation_commit"
git diff --name-only "$rank12_range_base..$rank12_range_head" -- \
  src/Accounts/Session.cs src/WebAPI/ImageHistoryAPI.cs src/WebAPI/BackendAPI.cs
git diff --cached --name-only
git status --short --branch
```

Completion requires:

- both fresh reviews pass;
- exact source and documentation scopes are correct;
- index is empty;
- unrelated working-tree changes remain untouched;
- platform/performance caveats remain accurate; and
- no agent build/test/runtime claim appears.
