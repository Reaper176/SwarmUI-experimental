# Persisted-Output Filename-Selection Measurement Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Temporarily instrument persisted-output filename selection, collect a privacy-safe maintainer-run workload profile, record a qualitative decision, and remove every instrumentation source change.

**Architecture:** A Rank-25-specific internal recorder emits opt-in structured JSON records from the existing save and bypass branches without changing `Session.SaveImage`'s public declaration. `Session.SaveImage` captures synchronous selection and background phase timings, while maintained callers provide only a privacy-safe source/backend-claim context. After maintainer collection and a `GO`, `NO-GO`, or `INSUFFICIENT` decision, all settings, recorder types, contexts, and hooks are removed so the final production tree equals the approved base.

**Tech Stack:** C# 12, .NET 8, FreneticUtilities, Newtonsoft.Json, Git static projection checks, maintainer-run SwarmUI on Garuda Linux/Btrfs.

---

## Repository-Policy Override of Generic Test Steps

Root `AGENTS.md` forbids agents from running builds, tests, launchers, servers,
browser automation, backends, or performance workloads. That repository policy
overrides generic TDD/build instructions in implementation skills.

For Rank 25:

- agent RED/GREEN gates are exact static assertions, source projection checks,
  line-by-line control-flow traces, privacy scans, and independent specification
  and quality reviews;
- maintainer Reaper176 performs the build, launch, behavior matrix, and
  performance collection;
- no prior Rank 24 test override carries forward;
- no agent reads or writes `Data`, `Models`, `Output`, `src/Data`, or generated
  media.

## File Responsibility Map

Temporary instrumentation phase:

- Create `src/Accounts/OutputFilenameSelectionMeasurement.cs`: Rank-25-only
  context, attempt, monotonic timing, schema, and nonthrowing structured log
  emission.
- Modify `src/Core/Settings.cs`: two temporary opt-in performance settings.
- Modify `src/Accounts/Session.cs`: user no-save record, path/lock/folder/hash/
  reservation/probe timing, background phase timing, and unchanged public
  facade.
- Modify `src/WebAPI/T2IAPI.cs`: normal-generation/mini-grid context and
  request/intermediate bypass records.
- Modify `src/WebAPI/ImageHistoryAPI.cs`: Image History source context.
- Modify
  `src/BuiltinExtensions/GridGenerator/GridGeneratorExtension.cs`: iteration,
  final-grid, and Grid no-save context/records.

Permanent documentation:

- Modify
  `docs/superpowers/specs/2026-07-30-output-filename-selection-measurement-design.md`:
  approval, evidence, decision, removal, and final status.
- Modify
  `docs/superpowers/audits/2026-07-21-maintainability-architecture-refresh.md`:
  Rank 25 provenance, evidence boundary, decision, recommendation, and final
  source projection.

The final source tree must contain no Rank 25 instrumentation. The two
documentation files and this plan are the only intended final tree difference
from approved base `30448884415c44f446136fa3e11fb06cefe375d6`.

### Task 1: Freeze the Approved Base and Static RED Gate

**Files:**
- Read:
  `docs/superpowers/specs/2026-07-30-output-filename-selection-measurement-design.md`
- Read: `docs/superpowers/audits/2026-07-21-maintainability-architecture-refresh.md`
- Read: `src/Accounts/Session.cs`
- Read: `src/Core/Settings.cs`
- Read: `src/WebAPI/T2IAPI.cs`
- Read: `src/WebAPI/ImageHistoryAPI.cs`
- Read:
  `src/BuiltinExtensions/GridGenerator/GridGeneratorExtension.cs`

- [ ] **Step 1: Verify the isolated baseline and protected primary state**

Run in the Rank 25 worktree:

```bash
git rev-parse HEAD
git merge-base 30448884415c44f446136fa3e11fb06cefe375d6 HEAD
git diff --quiet 30448884415c44f446136fa3e11fb06cefe375d6 -- src
git status --short
```

Expected from `merge-base`:

```text
30448884415c44f446136fa3e11fb06cefe375d6
```

`HEAD` must be the committed Rank 25 plan descendant, the source-tree
comparison and status must both be clean, and the approved base must remain its
merge base.

Run in the primary worktree:

```bash
git rev-parse HEAD
git status --short
git diff --numstat -- \
  src/Data/Settings.fds \
  src/Pages/Text2Image.cshtml \
  src/wwwroot/js/genpage/gentab/loras.js \
  src/wwwroot/js/genpage/main.js
```

Expected protected primary state:

```text
 M src/Data/Settings.fds
 M src/Pages/Text2Image.cshtml
 M src/wwwroot/js/genpage/gentab/loras.js
 M src/wwwroot/js/genpage/main.js
?? Data.pre-restore-2026-07-19/
83	3	src/Data/Settings.fds
9	2	src/Pages/Text2Image.cshtml
2	0	src/wwwroot/js/genpage/gentab/loras.js
0	1	src/wwwroot/js/genpage/main.js
```

- [ ] **Step 2: Capture the exact maintained save/caller inventory**

Run:

```bash
rg -n "SaveImage\\(" \
  src/Accounts/Session.cs \
  src/WebAPI/T2IAPI.cs \
  src/WebAPI/ImageHistoryAPI.cs \
  src/BuiltinExtensions/GridGenerator/GridGeneratorExtension.cs
rg -n "TryReserveOutputFilename|Directory\\.EnumerateFiles|RecentlyBlockedFilenames\\.Keys|User\\.UserLock" \
  src/Accounts/Session.cs
```

Expected:

- one public `Session.SaveImage` declaration;
- four maintained source files containing five direct call sites;
- one private `TryReserveOutputFilename`;
- one target-folder enumeration;
- one reservation-key collision predicate;
- one `User.UserLock` around selection/publication.

- [ ] **Step 3: Run the static RED assertion**

Run:

```bash
test ! -e src/Accounts/OutputFilenameSelectionMeasurement.cs
test -z "$(rg -l \
  'OutputFilenameMeasurementEnabled|OutputFilenameMeasurementScenario|Rank25OutputFilename|OutputFilenameSelectionContext' \
  src)"
```

Expected: exit `0`, proving the temporary measurement surface is absent.

- [ ] **Step 4: Record the no-runtime boundary**

Add no source or test artifact. Record in the implementation notes:

```text
RANK25_STATIC_BASELINE_CAPTURED
Agent build/test/runtime/measurement: not run by repository policy.
```

Do not commit a generated baseline file.

### Task 2: Add the Temporary Setting and Structured Recorder

**Files:**
- Modify: `src/Core/Settings.cs:209-223`
- Create: `src/Accounts/OutputFilenameSelectionMeasurement.cs`

- [ ] **Step 1: Run the focused RED assertion**

Run:

```bash
test -z "$(rg -n \
  'OutputFilenameMeasurementEnabled|OutputFilenameMeasurementScenario|class OutputFilenameSelectionMeasurement' \
  src/Core/Settings.cs src/Accounts 2>/dev/null)"
```

Expected: exit `0`.

- [ ] **Step 2: Add the temporary performance settings**

Append these fields to `PerformanceData` after `ModelListSanityCap`:

```csharp
/// <summary>Whether temporary persisted-output filename-selection measurement records are enabled.</summary>
[ConfigComment("Temporarily enables privacy-safe Rank 25 persisted-output filename-selection measurement logs.\nDefaults to false.\nEnable only while collecting the approved measurement matrix.")]
public bool OutputFilenameMeasurementEnabled = false;

/// <summary>Privacy-safe operator label attached to temporary persisted-output filename-selection records.</summary>
[ConfigComment("Privacy-safe scenario label for Rank 25 output filename measurement, for example 'btrfs-nvme/large-flat/repeated-name'.\nDo not include users, prompts, filenames, or filesystem paths.\nDefaults to empty.")]
public string OutputFilenameMeasurementScenario = "";
```

Do not add settings outside `PerformanceData`.

- [ ] **Step 3: Create the recorder types**

Create `src/Accounts/OutputFilenameSelectionMeasurement.cs` with this exact
interface and schema ownership:

```csharp
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using SwarmUI.Core;
using SwarmUI.Media;
using SwarmUI.Utils;
using System.Diagnostics;

namespace SwarmUI.Accounts;

/// <summary>Privacy-safe caller classification for temporary persisted-output filename measurement.</summary>
internal readonly record struct OutputFilenameSelectionContext(string Source, bool BackendClaimed)
{
    /// <summary>Fallback context for the unchanged public save facade.</summary>
    internal static OutputFilenameSelectionContext Direct => new("direct", false);

    /// <summary>Normal generated output while the selected backend remains claimed.</summary>
    internal static OutputFilenameSelectionContext NormalGeneration => new("normal_generation", true);

    /// <summary>Normal post-batch mini-grid outside the engine backend claim.</summary>
    internal static OutputFilenameSelectionContext NormalMiniGrid => new("normal_mini_grid", false);

    /// <summary>Image History add request.</summary>
    internal static OutputFilenameSelectionContext ImageHistoryAdd => new("image_history_add", false);

    /// <summary>Grid Generator iteration output.</summary>
    internal static OutputFilenameSelectionContext GridIteration => new("grid_iteration", false);

    /// <summary>Grid Generator final output.</summary>
    internal static OutputFilenameSelectionContext GridFinal => new("grid_final", false);
}

/// <summary>One enabled temporary persisted-output filename measurement attempt.</summary>
internal sealed class OutputFilenameSelectionAttempt
{
    internal long ID { get; init; }
    internal string Scenario { get; init; }
    internal string Source { get; init; }
    internal bool BackendClaimed { get; init; }
    internal string MediaCategory { get; init; }
    internal int BatchSize { get; init; }
    internal int FolderDepth { get; set; }
    internal int FolderFileCount { get; set; }
    internal int ReservationKeyCount { get; set; }
    internal int ReservationKeysExamined { get; set; }
    internal int ReservationCollisionMatches { get; set; }
    internal int CandidateProbeCount { get; set; }
    internal string NamingCategory { get; set; } = "direct";
    internal long PathResolutionMicroseconds { get; set; }
    internal long UserLockWaitMicroseconds { get; set; }
    internal long UserLockHoldMicroseconds { get; set; }
    internal long DirectoryEnumerationMicroseconds { get; set; }
    internal long ExtensionlessHashMicroseconds { get; set; }
    internal long ReservationScanMicroseconds { get; set; }
    internal long CandidateProbeMicroseconds { get; set; }
    internal long SynchronousSelectionMicroseconds { get; set; }
}

/// <summary>Temporary Rank 25 persisted-output filename-selection measurement recorder.</summary>
internal static class OutputFilenameSelectionMeasurement
{
    /// <summary>Stable prefix for extracting temporary Rank 25 records.</summary>
    internal const string LogPrefix = "[Rank25OutputFilename]";

    /// <summary>Schema version for temporary Rank 25 records.</summary>
    private const int SchemaVersion = 1;

    /// <summary>Monotonic process-local measurement ID source.</summary>
    private static long NextMeasurementID;

    /// <summary>Whether temporary Rank 25 measurement is currently enabled.</summary>
    internal static bool IsEnabled => Program.ServerSettings.Performance.OutputFilenameMeasurementEnabled;

    /// <summary>Returns a monotonic high-resolution timestamp.</summary>
    internal static long Timestamp()
    {
        return Stopwatch.GetTimestamp();
    }

    /// <summary>Converts a timestamp interval to integer microseconds.</summary>
    internal static long ElapsedMicroseconds(long start)
    {
        return (long)(Stopwatch.GetElapsedTime(start).TotalMilliseconds * 1000);
    }

    /// <summary>Normalizes an operator-provided scenario without adding runtime identities.</summary>
    private static string NormalizeScenario(string scenario)
    {
        if (string.IsNullOrWhiteSpace(scenario))
        {
            return "unlabeled";
        }
        char[] normalized = [.. scenario.Take(96).Select(character =>
            char.IsLetterOrDigit(character) || character == '-' || character == '_'
                || character == '.' || character == '/' ? character : '_')];
        return new string(normalized);
    }

    /// <summary>Starts one enabled measurement attempt, or returns null when disabled.</summary>
    internal static OutputFilenameSelectionAttempt Begin(OutputFilenameSelectionContext context, MediaFile file, int batchSize)
    {
        if (!IsEnabled)
        {
            return null;
        }
        return new OutputFilenameSelectionAttempt()
        {
            ID = Interlocked.Increment(ref NextMeasurementID),
            Scenario = NormalizeScenario(Program.ServerSettings.Performance.OutputFilenameMeasurementScenario),
            Source = context.Source,
            BackendClaimed = context.BackendClaimed,
            MediaCategory = $"{file.Type.MetaType}",
            BatchSize = batchSize
        };
    }

    /// <summary>Builds the common privacy-safe fields for one record.</summary>
    private static JObject Common(OutputFilenameSelectionAttempt attempt, string record, string outcome)
    {
        return new JObject()
        {
            ["schema"] = SchemaVersion,
            ["record"] = record,
            ["measurement_id"] = attempt.ID,
            ["scenario"] = attempt.Scenario,
            ["source"] = attempt.Source,
            ["backend_claimed"] = attempt.BackendClaimed,
            ["media_category"] = attempt.MediaCategory,
            ["batch_size"] = attempt.BatchSize,
            ["outcome"] = outcome
        };
    }

    /// <summary>Emits one record without affecting the observed save path.</summary>
    private static void Emit(JObject record)
    {
        try
        {
            Logs.Info($"{LogPrefix} {record.ToString(Formatting.None)}");
        }
        catch
        {
            // Temporary measurement diagnostics must not affect output behavior.
        }
    }
}
```

Insert these complete methods immediately before the recorder class's final
brace. No method accepts a path, username, request ID, prompt, parameter object,
or `Exception`:

```csharp
internal static void EmitBypass(OutputFilenameSelectionContext context, MediaFile file, int batchSize, string reason)
{
    OutputFilenameSelectionAttempt attempt = Begin(context, file, batchSize);
    if (attempt is null)
    {
        return;
    }
    Emit(Common(attempt, "bypass", reason));
}

internal static void EmitSelection(OutputFilenameSelectionAttempt attempt, string outcome)
{
    if (attempt is null)
    {
        return;
    }
    JObject record = Common(attempt, "selection", outcome);
    record["folder_depth"] = attempt.FolderDepth;
    record["folder_file_count"] = attempt.FolderFileCount;
    record["reservation_key_count"] = attempt.ReservationKeyCount;
    record["reservation_keys_examined"] = attempt.ReservationKeysExamined;
    record["reservation_collision_matches"] = attempt.ReservationCollisionMatches;
    record["candidate_probe_count"] = attempt.CandidateProbeCount;
    record["naming_category"] = attempt.NamingCategory;
    record["path_resolution_us"] = attempt.PathResolutionMicroseconds;
    record["user_lock_wait_us"] = attempt.UserLockWaitMicroseconds;
    record["user_lock_hold_us"] = attempt.UserLockHoldMicroseconds;
    record["directory_enumeration_us"] = attempt.DirectoryEnumerationMicroseconds;
    record["extensionless_hash_us"] = attempt.ExtensionlessHashMicroseconds;
    record["reservation_scan_us"] = attempt.ReservationScanMicroseconds;
    record["candidate_probe_us"] = attempt.CandidateProbeMicroseconds;
    record["synchronous_selection_us"] = attempt.SynchronousSelectionMicroseconds;
    Emit(record);
}

internal static void EmitBackground(OutputFilenameSelectionAttempt attempt, string outcome,
    long conversionWaitMicroseconds, long primaryWriteMicroseconds,
    long metadataWriteMicroseconds, long previewMicroseconds,
    long historyIndexMicroseconds, long retentionMicroseconds,
    long totalMicroseconds)
{
    if (attempt is null)
    {
        return;
    }
    JObject record = Common(attempt, "background", outcome);
    record["conversion_wait_us"] = conversionWaitMicroseconds;
    record["primary_write_us"] = primaryWriteMicroseconds;
    record["metadata_write_us"] = metadataWriteMicroseconds;
    record["preview_us"] = previewMicroseconds;
    record["history_index_us"] = historyIndexMicroseconds;
    record["retention_us"] = retentionMicroseconds;
    record["total_us"] = totalMicroseconds;
    Emit(record);
}
```

- [ ] **Step 4: Run the focused GREEN and privacy assertions**

Run:

```bash
test "$(rg -c 'OutputFilenameMeasurementEnabled' src/Core/Settings.cs)" -eq 1
test "$(rg -c 'OutputFilenameMeasurementScenario' src/Core/Settings.cs)" -eq 1
test "$(rg -c 'internal static void EmitBypass' src/Accounts/OutputFilenameSelectionMeasurement.cs)" -eq 1
test "$(rg -c 'internal static void EmitSelection' src/Accounts/OutputFilenameSelectionMeasurement.cs)" -eq 1
test "$(rg -c 'internal static void EmitBackground' src/Accounts/OutputFilenameSelectionMeasurement.cs)" -eq 1
test -z "$(rg -n 'UserID|UserRequestId|fullPath|folderRoute|Exception ex|ReadableString|Prompt' \
  src/Accounts/OutputFilenameSelectionMeasurement.cs)"
git diff --check
```

Expected: every command exits `0`.

- [ ] **Step 5: Commit the recorder foundation**

```bash
git add src/Core/Settings.cs src/Accounts/OutputFilenameSelectionMeasurement.cs
git diff --cached --check
git commit -m "measure: add output filename diagnostic schema"
```

Expected: exact two-file commit.

### Task 3: Instrument Synchronous Selection Behind the Public Facade

**Files:**
- Modify: `src/Accounts/Session.cs:246-280`
- Modify: `src/Accounts/Session.cs:456-563`

- [ ] **Step 1: Run the synchronous instrumentation RED assertion**

Run:

```bash
test "$(rg -c 'public \\(string, string\\) SaveImage' src/Accounts/Session.cs)" -eq 1
test -z "$(rg -n \
  'OutputFilenameSelectionAttempt|OutputFilenameSelectionContext|EmitSelection|DirectoryEnumerationMicroseconds' \
  src/Accounts/Session.cs)"
```

Expected: exit `0`.

- [ ] **Step 2: Add measurement to the existing reservation predicate without a second scan**

Change the private signature to:

```csharp
private static bool TryReserveOutputFilename(string fullPath,
    out OutputFilenameReservation reservation,
    OutputFilenameSelectionAttempt measurement)
```

Replace only the existing `RecentlyBlockedFilenames.Keys.Any` check with:

```csharp
string fullPathNoExt = fullPath.BeforeLast('.');
int examined = 0;
long scanStart = measurement is null ? 0 : OutputFilenameSelectionMeasurement.Timestamp();
bool hasCollision = RecentlyBlockedFilenames.Keys.Any(path =>
{
    if (measurement is not null)
    {
        examined++;
    }
    return path.BeforeLast('.') == fullPathNoExt;
});
if (measurement is not null)
{
    measurement.ReservationKeyCount = Math.Max(measurement.ReservationKeyCount, RecentlyBlockedFilenames.Count);
    measurement.ReservationKeysExamined += examined;
    measurement.ReservationScanMicroseconds += OutputFilenameSelectionMeasurement.ElapsedMicroseconds(scanStart);
    if (hasCollision)
    {
        measurement.ReservationCollisionMatches++;
    }
}
if (hasCollision)
{
    reservation = default;
    return false;
}
```

The reservation publication/rollback body after this check remains byte-for-byte
unchanged.

- [ ] **Step 3: Preserve the public facade and add the internal context route**

Keep the exact public declaration and make it delegate:

```csharp
public (string, string) SaveImage(T2IEngine.ImageOutput image, int batchIndex, T2IParamInput user_input, string metadata)
{
    return SaveImage(image, batchIndex, user_input, metadata, OutputFilenameSelectionContext.Direct);
}

/// <summary>Internal save route carrying only temporary privacy-safe measurement context.</summary>
internal (string, string) SaveImage(T2IEngine.ImageOutput image, int batchIndex,
    T2IParamInput user_input, string metadata,
    OutputFilenameSelectionContext measurementContext)
{
    OutputFilenameSelectionAttempt measurement = null;
    if (OutputFilenameSelectionMeasurement.IsEnabled)
    {
        measurement = OutputFilenameSelectionMeasurement.Begin(
            measurementContext,
            image.File,
            user_input.Get(T2IParamTypes.BatchSize, 1));
    }
    if (!User.Settings.SaveFiles)
    {
        OutputFilenameSelectionMeasurement.EmitBypass(
            measurement, "user_save_files_disabled");
        return (image.File.AsDataString(), null);
    }
    string rawImagePath = User.BuildImageOutputPath(user_input, batchIndex);
    string imagePath = rawImagePath.Replace("[number]", "1");
    string format = user_input.Get(
        T2IParamTypes.ImageFormat, User.Settings.FileFormat.ImageFormat);
    string extension;
    try
    {
        extension = ImageFile.ImageFormatToExtension(format);
    }
    catch (Exception ex)
    {
        Logs.Debug($"Invalid file format extension: {ex.GetType().Name}: {ex.Message}");
        extension = "jpg";
    }
    if (image.File.Type.MetaType != MediaMetaType.Image)
    {
        Logs.Verbose($"Image is type {image.File.Type} and will save with extension '{image.File.Type.Extension}'.");
        extension = image.File.Type.Extension;
    }
    string fullPathNoExt = Path.GetFullPath(UserImageHistoryHelper.GetRealPathFor(
        User, $"{User.OutputDirectory}/{imagePath}"));
    string pathFolder = imagePath.Contains('/') ? imagePath.BeforeLast('/') : "";
    string folderRoute = Path.GetFullPath(UserImageHistoryHelper.GetRealPathFor(
        User, $"{User.OutputDirectory}/{pathFolder}"));
    string fullPath = $"{fullPathNoExt}.{extension}";
    string root = Utilities.CombinePathWithAbsolute(
        Environment.CurrentDirectory, User.OutputDirectory);
}
```

For this Task 3 commit, retain approved-base `Session.cs` lines 487–560
(the block beginning `Task<byte[]> pendingTask = null;` and ending at its
background-task `catch`) byte-for-byte immediately after `root`; do not add a
marker comment. Task 4 replaces that retained block with its complete timed
equivalent. Avoid allocating two IDs for the user no-save record by adding this
overload to the recorder:

```csharp
internal static void EmitBypass(OutputFilenameSelectionAttempt attempt, string reason)
{
    if (attempt is null)
    {
        return;
    }
    Emit(Common(attempt, "bypass", reason));
}
```

The context-based overload in Task 2 delegates through `Begin` to this overload.
Use `EmitBypass(measurement, "user_save_files_disabled")` in `Session`.

- [ ] **Step 4: Add timed path, lock, folder, hash, and probe branches**

Use these exact measurement rules inside the internal route:

```csharp
long synchronousStart = measurement is null ? 0 : OutputFilenameSelectionMeasurement.Timestamp();
long pathStart = measurement is null ? 0 : OutputFilenameSelectionMeasurement.Timestamp();
string rawImagePath = User.BuildImageOutputPath(user_input, batchIndex);
string imagePath = rawImagePath.Replace("[number]", "1");
string format = user_input.Get(
    T2IParamTypes.ImageFormat, User.Settings.FileFormat.ImageFormat);
string extension;
try
{
    extension = ImageFile.ImageFormatToExtension(format);
}
catch (Exception ex)
{
    Logs.Debug($"Invalid file format extension: {ex.GetType().Name}: {ex.Message}");
    extension = "jpg";
}
if (image.File.Type.MetaType != MediaMetaType.Image)
{
    Logs.Verbose($"Image is type {image.File.Type} and will save with extension '{image.File.Type.Extension}'.");
    extension = image.File.Type.Extension;
}
string fullPathNoExt = Path.GetFullPath(UserImageHistoryHelper.GetRealPathFor(
    User, $"{User.OutputDirectory}/{imagePath}"));
string pathFolder = imagePath.Contains('/') ? imagePath.BeforeLast('/') : "";
string folderRoute = Path.GetFullPath(UserImageHistoryHelper.GetRealPathFor(
    User, $"{User.OutputDirectory}/{pathFolder}"));
string fullPath = $"{fullPathNoExt}.{extension}";
string root = Utilities.CombinePathWithAbsolute(
    Environment.CurrentDirectory, User.OutputDirectory);
if (measurement is not null)
{
    measurement.PathResolutionMicroseconds =
        OutputFilenameSelectionMeasurement.ElapsedMicroseconds(pathStart);
    measurement.FolderDepth = pathFolder.Split(
        '/', StringSplitOptions.RemoveEmptyEntries).Length;
}

bool saveFailed = false;
long lockWaitStart = measurement is null ? 0 : OutputFilenameSelectionMeasurement.Timestamp();
lock (User.UserLock)
{
    long lockHoldStart = 0;
    if (measurement is not null)
    {
        measurement.UserLockWaitMicroseconds =
            OutputFilenameSelectionMeasurement.ElapsedMicroseconds(lockWaitStart);
        lockHoldStart = OutputFilenameSelectionMeasurement.Timestamp();
    }
    try
    {
        Directory.CreateDirectory(folderRoute);
        HashSet<string> existingFiles;
        if (measurement is null)
        {
            existingFiles = [.. Directory.EnumerateFiles(folderRoute)
                .Select(file => file.BeforeLast('.'))];
        }
        else
        {
            long enumerationStart = OutputFilenameSelectionMeasurement.Timestamp();
            string[] folderFiles = [.. Directory.EnumerateFiles(folderRoute)];
            measurement.DirectoryEnumerationMicroseconds =
                OutputFilenameSelectionMeasurement.ElapsedMicroseconds(enumerationStart);
            measurement.FolderFileCount = folderFiles.Length;
            long hashStart = OutputFilenameSelectionMeasurement.Timestamp();
            existingFiles = [.. folderFiles.Select(file => file.BeforeLast('.'))];
            measurement.ExtensionlessHashMicroseconds =
                OutputFilenameSelectionMeasurement.ElapsedMicroseconds(hashStart);
        }
        OutputFilenameReservation reservation = default;
        int num = 0;
        if (measurement is null)
        {
            while (existingFiles.Contains(fullPathNoExt)
                || !TryReserveOutputFilename(fullPath, out reservation, null))
            {
                num++;
                imagePath = rawImagePath.Contains("[number]")
                    ? rawImagePath.Replace("[number]", $"{num}")
                    : $"{rawImagePath}-{num}";
                fullPathNoExt = Path.GetFullPath(UserImageHistoryHelper.GetRealPathFor(
                    User, $"{User.OutputDirectory}/{imagePath}"));
                fullPath = $"{fullPathNoExt}.{extension}";
            }
        }
        else
        {
            while (true)
            {
                measurement.CandidateProbeCount++;
                long probeStart = OutputFilenameSelectionMeasurement.Timestamp();
                bool diskCollision = existingFiles.Contains(fullPathNoExt);
                bool reserved = !diskCollision
                    && TryReserveOutputFilename(fullPath, out reservation, measurement);
                measurement.CandidateProbeMicroseconds +=
                    OutputFilenameSelectionMeasurement.ElapsedMicroseconds(probeStart);
                if (reserved)
                {
                    break;
                }
                num++;
                imagePath = rawImagePath.Contains("[number]")
                    ? rawImagePath.Replace("[number]", $"{num}")
                    : $"{rawImagePath}-{num}";
                fullPathNoExt = Path.GetFullPath(UserImageHistoryHelper.GetRealPathFor(
                    User, $"{User.OutputDirectory}/{imagePath}"));
                fullPath = $"{fullPathNoExt}.{extension}";
            }
            measurement.NamingCategory = num == 0
                ? "direct"
                : rawImagePath.Contains("[number]") ? "number_token" : "suffix";
        }
        Task<byte[]> pendingTask = null;
    }
    catch (Exception ex)
    {
        Logs.Error($"Could not save user '{User.UserID}' image (to '{fullPath}'): error '{ex.Message}'");
        saveFailed = true;
    }
    finally
    {
        if (measurement is not null)
        {
            measurement.UserLockHoldMicroseconds =
                OutputFilenameSelectionMeasurement.ElapsedMicroseconds(lockHoldStart);
        }
    }
}
if (measurement is not null)
{
    measurement.SynchronousSelectionMicroseconds =
        OutputFilenameSelectionMeasurement.ElapsedMicroseconds(synchronousStart);
    OutputFilenameSelectionMeasurement.EmitSelection(
        measurement, saveFailed ? "error" : "scheduled");
}
if (saveFailed)
{
    return ("ERROR", null);
}
```

For this Task 3 commit, retain the approved-base pending-task publication and
background block byte-for-byte immediately after
`Task<byte[]> pendingTask = null;`. Task 4 replaces that retained block with the
complete code shown there. Do not add a marker comment to source.

Keep all pending-task/reservation cleanup code inside the original `try`.
Moving the existing error return after the lock is authorized only to emit after
lock release; the error log text and returned tuple remain unchanged.

- [ ] **Step 5: Run synchronous GREEN/parity assertions**

Run:

```bash
test "$(rg -c 'public \\(string, string\\) SaveImage' src/Accounts/Session.cs)" -eq 1
test "$(rg -c 'internal \\(string, string\\) SaveImage' src/Accounts/Session.cs)" -eq 1
test "$(rg -c 'Directory\\.EnumerateFiles' src/Accounts/Session.cs)" -eq 2
test "$(rg -c 'RecentlyBlockedFilenames\\.Keys\\.Any' src/Accounts/Session.cs)" -eq 1
test "$(rg -c 'EmitSelection' src/Accounts/Session.cs)" -eq 1
git diff --check
git diff 30448884415c44f446136fa3e11fb06cefe375d6 -- \
  src/Accounts/Session.cs
```

Expected:

- the second directory-enumeration expression exists only in the mutually
  exclusive enabled branch;
- no second reservation scan exists;
- public declaration text is unchanged;
- reservation publication/rollback and background cleanup order are unchanged.

- [ ] **Step 6: Commit synchronous selection instrumentation**

```bash
git add src/Accounts/Session.cs src/Accounts/OutputFilenameSelectionMeasurement.cs
git diff --cached --check
git commit -m "measure: instrument output filename selection"
```

Expected: exact two-file commit.

### Task 4: Instrument Background Save Phases

**Files:**
- Modify: `src/Accounts/Session.cs:503-543`

- [ ] **Step 1: Run the background RED assertion**

Run:

```bash
test -z "$(rg -n \
  'conversionWaitMicroseconds|primaryWriteMicroseconds|historyIndexMicroseconds|EmitBackground' \
  src/Accounts/Session.cs)"
```

Expected: exit `0`.

- [ ] **Step 2: Add phase-local timing without moving operations**

Inside the existing `Utilities.RunCheckedTask` lambda, declare:

```csharp
long backgroundStart = measurement is null ? 0 : OutputFilenameSelectionMeasurement.Timestamp();
long conversionWaitMicroseconds = 0;
long primaryWriteMicroseconds = 0;
long metadataWriteMicroseconds = 0;
long previewMicroseconds = 0;
long historyIndexMicroseconds = 0;
long retentionMicroseconds = 0;
string backgroundOutcome = "error";
```

Wrap each existing operation in place:

```csharp
long phaseStart = measurement is null ? 0 : OutputFilenameSelectionMeasurement.Timestamp();
MediaFile actualFile = image.ActualFileTask is null ? image.File : await image.ActualFileTask;
if (measurement is not null)
{
    conversionWaitMicroseconds =
        OutputFilenameSelectionMeasurement.ElapsedMicroseconds(phaseStart);
}

phaseStart = measurement is null ? 0 : OutputFilenameSelectionMeasurement.Timestamp();
File.WriteAllBytes(fullPath, actualFile.RawData);
if (measurement is not null)
{
    primaryWriteMicroseconds =
        OutputFilenameSelectionMeasurement.ElapsedMicroseconds(phaseStart);
}

phaseStart = measurement is null ? 0 : OutputFilenameSelectionMeasurement.Timestamp();
if ((User.Settings.FileFormat.SaveTextFileMetadata || extension == "webp"
    || !OutputMetadataTracker.ExtensionsWithMetadata.Contains(extension))
    && !string.IsNullOrWhiteSpace(metadata))
{
    if (extension == "webp" && actualFile is ImageFile imageFile
        && imageFile.ToIS.Frames.Count == 1)
    {
        // no .json write for still-image webps
    }
    else
    {
        File.WriteAllBytes(
            fullPathNoExt + ".swarm.json", metadata.EncodeUTF8());
    }
}
if (measurement is not null)
{
    metadataWriteMicroseconds =
        OutputFilenameSelectionMeasurement.ElapsedMicroseconds(phaseStart);
}

phaseStart = measurement is null ? 0 : OutputFilenameSelectionMeasurement.Timestamp();
OutputMetadataTracker.GetOrCreatePreviewFor(fullPath.Replace('\\', '/'));
if (measurement is not null)
{
    previewMicroseconds =
        OutputFilenameSelectionMeasurement.ElapsedMicroseconds(phaseStart);
}

phaseStart = measurement is null ? 0 : OutputFilenameSelectionMeasurement.Timestamp();
OutputMetadataTracker.UpsertHistoryIndexForFile(
    fullPath.Replace('\\', '/'), root, User.Settings.StarNoFolders);
if (measurement is not null)
{
    historyIndexMicroseconds =
        OutputFilenameSelectionMeasurement.ElapsedMicroseconds(phaseStart);
}

Logs.Debug($"Saved an output file as '{fullPath}'");
phaseStart = measurement is null ? 0 : OutputFilenameSelectionMeasurement.Timestamp();
await Task.Delay(TimeSpan.FromSeconds(10));
if (measurement is not null)
{
    retentionMicroseconds =
        OutputFilenameSelectionMeasurement.ElapsedMicroseconds(phaseStart);
}
saveSucceeded = true;
backgroundOutcome = "success";
```

In the existing `finally`, keep `RemoveStillSavingFile` and reservation
release/expiry first, then emit:

```csharp
OutputFilenameSelectionMeasurement.EmitBackground(
    measurement,
    backgroundOutcome,
    conversionWaitMicroseconds,
    primaryWriteMicroseconds,
    metadataWriteMicroseconds,
    previewMicroseconds,
    historyIndexMicroseconds,
    retentionMicroseconds,
    measurement is null
        ? 0
        : OutputFilenameSelectionMeasurement.ElapsedMicroseconds(backgroundStart));
```

Do not add an exception parameter or message to the record.

- [ ] **Step 3: Run background GREEN/order assertions**

Run:

```bash
test "$(rg -c 'EmitBackground' src/Accounts/Session.cs)" -eq 1
test "$(rg -c 'File.WriteAllBytes\\(fullPath,' src/Accounts/Session.cs)" -eq 1
test "$(rg -c 'GetOrCreatePreviewFor' src/Accounts/Session.cs)" -eq 1
test "$(rg -c 'UpsertHistoryIndexForFile' src/Accounts/Session.cs)" -eq 1
test "$(rg -c 'Task.Delay\\(TimeSpan.FromSeconds\\(10\\)\\)' src/Accounts/Session.cs)" -eq 1
git diff --check
```

Numbered inspection must show:

1. conversion await;
2. primary write;
3. optional metadata;
4. preview;
5. history index;
6. debug log;
7. ten-second delay;
8. success flag;
9. pending-file removal;
10. exact reservation release/expiry;
11. nonthrowing measurement emission.

- [ ] **Step 4: Commit background instrumentation**

```bash
git add src/Accounts/Session.cs
git diff --cached --check
git commit -m "measure: separate output background phases"
```

Expected: one-file commit.

### Task 5: Classify Maintained Callers and Bypasses

**Files:**
- Modify: `src/WebAPI/T2IAPI.cs:370-392`
- Modify: `src/WebAPI/T2IAPI.cs:451-468`
- Modify: `src/WebAPI/T2IAPI.cs:507-515`
- Modify: `src/WebAPI/ImageHistoryAPI.cs:51-55`
- Modify:
  `src/BuiltinExtensions/GridGenerator/GridGeneratorExtension.cs:254-271`
- Modify:
  `src/BuiltinExtensions/GridGenerator/GridGeneratorExtension.cs:663-679`

- [ ] **Step 1: Run the caller RED assertion**

Run:

```bash
test -z "$(rg -n \
  'NormalGeneration|NormalMiniGrid|ImageHistoryAdd|GridIteration|GridFinal|EmitBypass' \
  src/WebAPI/T2IAPI.cs \
  src/WebAPI/ImageHistoryAPI.cs \
  src/BuiltinExtensions/GridGenerator/GridGeneratorExtension.cs)"
```

Expected: exit `0`.

- [ ] **Step 2: Classify normal generation and mini-grid without changing save policy**

Change the local `saveImage` signature to accept
`OutputFilenameSelectionContext measurementContext`.

Keep policy order and use:

```csharp
bool requestNoSave = thisParams.Get(T2IParamTypes.DoNotSave, false);
bool intermediateNoSave = !image.IsReal
    && thisParams.Get(T2IParamTypes.DoNotSaveIntermediates, false);
bool noSave = requestNoSave || intermediateNoSave;
```

In the existing no-save branch, after any existing transient reformat wait and
before tuple assignment, add:

```csharp
if (OutputFilenameSelectionMeasurement.IsEnabled)
{
    OutputFilenameSelectionMeasurement.EmitBypass(
        measurementContext,
        file,
        thisParams.Get(T2IParamTypes.BatchSize, 1),
        intermediateNoSave ? "intermediate_policy" : "request_do_not_save");
}
```

In the persisted branch call:

```csharp
(url, filePath) = session.SaveImage(
    image, actualIndex, thisParams, metadata, measurementContext);
```

Pass `OutputFilenameSelectionContext.NormalGeneration` from the
`CreateImageTask` callback and
`OutputFilenameSelectionContext.NormalMiniGrid` from the post-batch mini-grid.

- [ ] **Step 3: Classify Image History add**

Replace only the call with:

```csharp
(string path, _) = session.SaveImage(
    outputImage,
    0,
    user_input,
    metadata,
    OutputFilenameSelectionContext.ImageHistoryAdd);
```

- [ ] **Step 4: Expand the two Grid ternaries without changing results**

For the iteration branch:

```csharp
string url, filePath;
if (thisParams.Get(T2IParamTypes.DoNotSave, false))
{
    if (OutputFilenameSelectionMeasurement.IsEnabled)
    {
        OutputFilenameSelectionMeasurement.EmitBypass(
            OutputFilenameSelectionContext.GridIteration,
            image.File,
            thisParams.Get(T2IParamTypes.BatchSize, 1),
            "grid_do_not_save");
    }
    (url, filePath) = (image.File.AsDataString(), null);
}
else
{
    (url, filePath) = data.Session.SaveImage(
        image,
        iteration,
        thisParams,
        metadata,
        OutputFilenameSelectionContext.GridIteration);
}
```

For the final-grid branch:

```csharp
string url, filePath;
if (initialParams.Get(T2IParamTypes.DoNotSave, false))
{
    if (OutputFilenameSelectionMeasurement.IsEnabled)
    {
        OutputFilenameSelectionMeasurement.EmitBypass(
            OutputFilenameSelectionContext.GridFinal,
            outImg,
            initialParams.Get(T2IParamTypes.BatchSize, 1),
            "grid_do_not_save");
    }
    (url, filePath) = (outImg.AsDataString(), null);
}
else
{
    (url, filePath) = data.Session.SaveImage(
        imageOut,
        batchId,
        initialParams,
        metadata,
        OutputFilenameSelectionContext.GridFinal);
}
```

Keep all error, output, webhook, generated-output, WebSocket, and metadata
statements after each original ternary in their existing order.

- [ ] **Step 5: Run caller GREEN/parity/privacy assertions**

Run:

```bash
test "$(rg -c 'NormalGeneration' src/WebAPI/T2IAPI.cs)" -eq 1
test "$(rg -c 'NormalMiniGrid' src/WebAPI/T2IAPI.cs)" -eq 1
test "$(rg -c 'ImageHistoryAdd' src/WebAPI/ImageHistoryAPI.cs)" -eq 1
test "$(rg -c 'GridIteration' src/BuiltinExtensions/GridGenerator/GridGeneratorExtension.cs)" -eq 2
test "$(rg -c 'GridFinal' src/BuiltinExtensions/GridGenerator/GridGeneratorExtension.cs)" -eq 2
test "$(rg -c 'EmitBypass' src/WebAPI/T2IAPI.cs)" -eq 1
test "$(rg -c 'EmitBypass' src/BuiltinExtensions/GridGenerator/GridGeneratorExtension.cs)" -eq 2
git diff --check
```

Inspect every emitted record call and confirm no `UserID`, `UserRequestId`,
prompt, filename, path, metadata, or exception is passed.

- [ ] **Step 6: Commit caller classification**

```bash
git add \
  src/WebAPI/T2IAPI.cs \
  src/WebAPI/ImageHistoryAPI.cs \
  src/BuiltinExtensions/GridGenerator/GridGeneratorExtension.cs
git diff --cached --check
git commit -m "measure: classify output save contexts"
```

Expected: exact three-file commit.

### Task 6: Complete Static Review Before Maintainer Collection

**Files:**
- Review all six temporary source files.
- Do not modify source unless a review finding requires correction.

- [ ] **Step 1: Verify exact temporary source projection**

Run:

```bash
git diff --name-only 30448884415c44f446136fa3e11fb06cefe375d6..HEAD -- src
```

Expected exact sorted set:

```text
src/Accounts/OutputFilenameSelectionMeasurement.cs
src/Accounts/Session.cs
src/BuiltinExtensions/GridGenerator/GridGeneratorExtension.cs
src/Core/Settings.cs
src/WebAPI/ImageHistoryAPI.cs
src/WebAPI/T2IAPI.cs
```

- [ ] **Step 2: Verify public compatibility and disabled dominance**

Run:

```bash
git show 30448884415c44f446136fa3e11fb06cefe375d6:src/Accounts/Session.cs \
  | rg '^    public \\(string, string\\) SaveImage' > /tmp/rank25-base-public.txt
rg '^    public \\(string, string\\) SaveImage' src/Accounts/Session.cs \
  > /tmp/rank25-candidate-public.txt
cmp /tmp/rank25-base-public.txt /tmp/rank25-candidate-public.txt
rg -n 'if \\(!IsEnabled\\)|if \\(measurement is null\\)|if \\(measurement is not null\\)' \
  src/Accounts/OutputFilenameSelectionMeasurement.cs src/Accounts/Session.cs
```

Expected: public declarations compare byte-for-byte and every timestamp/record
path is dominated by the enabled attempt.

- [ ] **Step 3: Verify privacy and schema**

Run:

```bash
rg -n 'Rank25OutputFilename|\\["(schema|record|measurement_id|scenario|source|backend_claimed|media_category|batch_size|outcome)"\\]' \
  src/Accounts/OutputFilenameSelectionMeasurement.cs
test -z "$(rg -n \
  'UserID|UserRequestId|fullPath|fullPathNoExt|folderRoute|rawImagePath|imagePath|metadata|ReadableString|Exception' \
  src/Accounts/OutputFilenameSelectionMeasurement.cs)"
```

Expected: one stable prefix/schema and no prohibited data-bearing parameter.

- [ ] **Step 4: Verify control-flow parity**

Use numbered source inspection to compare approved base and candidate for:

- every bypass condition and result;
- path/extension calculation;
- `Directory.CreateDirectory`;
- folder enumeration and extension stripping;
- disk collision before reservation scan;
- reservation lock/publication/rollback;
- `[number]` and suffix updates;
- `StillSavingFiles` publication;
- background operation order;
- success/failure cleanup;
- returned URL/path and `ERROR`;
- all downstream caller output/webhook/error behavior.

Do not approve if an enabled record requires a second filesystem or reservation
collision scan.

- [ ] **Step 5: Run formatting and repository-contamination checks**

Run:

```bash
git diff --check 30448884415c44f446136fa3e11fb06cefe375d6..HEAD
test -z "$(find src -type d \( -name bin -o -name obj \) -print -quit)"
git status --short
```

Expected: clean whitespace, no `src/bin`/`src/obj`, clean worktree.

- [ ] **Step 6: Obtain independent source reviews**

Fresh specification review must return:

```text
RANK25_SOURCE_SPEC_APPROVED
```

Fresh quality/privacy review must return:

```text
RANK25_SOURCE_QUALITY_APPROVED
```

If either review reports a finding, send it to the implementing worker, apply
the minimum correction, repeat all affected static gates, and re-run both
reviews.

Agents do not build or launch.

### Task 7: Maintainer Build, Behavior Matrix, and Measurement Collection

**Files:**
- Runtime evidence supplied by maintainer outside repository user-data paths.
- Read-only design matrix:
  `docs/superpowers/specs/2026-07-30-output-filename-selection-measurement-design.md`

- [ ] **Step 1: Give the maintainer the pre-collection controls**

Ask Reaper176 to:

1. build and launch the exact reviewed Rank 25 branch;
2. leave `Performance.OutputFilenameMeasurementEnabled = false`;
3. exercise one normal persisted save and one data-URL bypass;
4. confirm no `[Rank25OutputFilename]` record appears;
5. confirm the normal output and data URL remain correct.

Expected maintainer response:

```text
RANK25_DISABLED_CONTROL_PASSED
```

- [ ] **Step 2: Enable privacy-safe collection**

Ask the maintainer to:

1. enable `Performance.OutputFilenameMeasurementEnabled`;
2. set `Performance.OutputFilenameMeasurementScenario` separately for each
   workload group using only storage/workload labels, for example
   `btrfs-nvme/empty/direct`;
3. ensure the normal log level shows `Info`;
4. never place a username, request ID, prompt, filename, or path in the label.

- [ ] **Step 3: Run all 25 matrix cases**

Run the numbered matrix in the approved design exactly:

1. disabled control;
2. request `DoNotSave`;
3. intermediate-output policy;
4. Grid no-save;
5. user `SaveFiles = false`;
6. empty folder;
7. typical folder;
8. large flat folder;
9. deeply partitioned folder;
10. direct naming;
11. `[number]` collision;
12. suffix collision;
13. repeated names;
14. concurrent same-folder saves;
15. concurrent different-scope saves;
16. delete/regenerate;
17. normal claimed generation;
18. normal mini-grid;
19. Image History add;
20. Grid iteration;
21. Grid final;
22. single output;
23. batch output;
24. conversion-heavy output;
25. metadata/preview/index-enabled output.

For each case, the maintainer records pass/fail/unrun for behavioral parity and
preserves all matching `[Rank25OutputFilename]` lines.

- [ ] **Step 4: Extract and submit evidence**

The maintainer extracts only lines containing:

```text
[Rank25OutputFilename]
```

The evidence submission must include the actual integer counts in this exact
shape:

```text
Environment: Garuda Linux (Arch-based), Btrfs, storage classification named in the scenario labels
Cases: actual passed count, actual failed count, actual unrun count
RANK25_COLLECTION_COMPLETE
```

The attached/external log contains the structured lines. It must not be placed
under repository `Data`, `Models`, `Output`, or `src/Data` for agent analysis.

If any behavior case fails, stop collection analysis and diagnose before making
a performance decision.

### Task 8: Analyze Evidence and Record the Decision

**Files:**
- External read-only structured log supplied by maintainer.
- Modify:
  `docs/superpowers/specs/2026-07-30-output-filename-selection-measurement-design.md`
- Modify:
  `docs/superpowers/audits/2026-07-21-maintainability-architecture-refresh.md`

- [ ] **Step 1: Validate record safety and completeness**

For the supplied external evidence file, strip the normal log prefix into an
external JSONL copy and verify:

```bash
jq -e '.schema == 1
  and (.record == "bypass" or .record == "selection" or .record == "background")
  and (.measurement_id | type == "number")
  and (.scenario | type == "string")
  and (.source | type == "string")
  and (.backend_claimed | type == "boolean")
  and (.outcome | type == "string")' rank25-records.jsonl
```

Search both the field names and values for submitted raw usernames, request IDs,
prompts, filenames, or paths. A privacy finding blocks further use and requires
deleting/redacting the affected evidence without copying it into the repo.

- [ ] **Step 2: Check matrix/source coverage**

Use `jq -s` to group records by:

- `scenario`;
- `source`;
- `record`;
- `outcome`;
- `backend_claimed`;
- naming category;
- folder file-count band;
- reservation-key-count band;
- batch size.

Confirm every maintainer-run case has the required bypass record or correlated
selection/background pair. Preserve missing cases as unrun; do not infer them.

- [ ] **Step 3: Produce descriptive comparisons**

For each representative group, report:

- sample count;
- median and upper-range synchronous selection time;
- median and upper-range user-lock wait/hold;
- enumeration, hash, reservation scan, and probe contributions;
- folder/reservation cardinality and collision-probe scaling;
- claimed versus non-claimed sources;
- synchronous selection versus conversion/write/preview/index background work.

Do not invent a universal threshold or claim causality beyond the records.

- [ ] **Step 4: Select exactly one approved decision**

Use the approved qualitative gate:

```text
GO
```

only when selection is repeatedly material, scales with cardinality, and has a
bounded dominant component;

```text
NO-GO
```

when selection is minor/inconsistent or dominated by other work;

```text
INSUFFICIENT
```

when coverage or records do not support either conclusion.

- [ ] **Step 5: Draft the evidence record without claiming removal yet**

Append to the design and update Backend P6/roadmap Rank 25 with:

- exact branch/source commit range;
- exact source-file projection;
- static review tokens;
- exact raw maintainer completion message;
- environment and passed/failed/unrun counts;
- schema/prefix and evidence digest if reproducibly stored outside the repo;
- descriptive profile;
- exact decision and rationale;
- all unrun/unsupported boundaries;
- explicit statement that instrumentation removal is still pending.

Do not yet call Rank 25 complete.

### Task 9: Remove Every Instrumentation Source Change

**Files:**
- Delete: `src/Accounts/OutputFilenameSelectionMeasurement.cs`
- Restore Rank-25 edits in: `src/Core/Settings.cs`
- Restore Rank-25 edits in: `src/Accounts/Session.cs`
- Restore Rank-25 edits in: `src/WebAPI/T2IAPI.cs`
- Restore Rank-25 edits in: `src/WebAPI/ImageHistoryAPI.cs`
- Restore Rank-25 edits in:
  `src/BuiltinExtensions/GridGenerator/GridGeneratorExtension.cs`

- [ ] **Step 1: Run the removal RED assertion**

Run:

```bash
test -n "$(rg -l \
  'OutputFilenameMeasurementEnabled|OutputFilenameMeasurementScenario|Rank25OutputFilename|OutputFilenameSelectionContext' \
  src)"
```

Expected: exit `0`, proving the temporary surface still exists.

- [ ] **Step 2: Remove the recorder and settings with `apply_patch`**

Delete `src/Accounts/OutputFilenameSelectionMeasurement.cs`.

Remove exactly:

```csharp
public bool OutputFilenameMeasurementEnabled = false;
public string OutputFilenameMeasurementScenario = "";
```

and their XML/config comments from `PerformanceData`.

- [ ] **Step 3: Restore `Session` to the approved-base tree**

With `apply_patch`:

- restore the original two-parameter private
  `TryReserveOutputFilename(string, out OutputFilenameReservation)`;
- restore the original single `Any` predicate;
- restore the public `SaveImage` body rather than a delegate;
- remove the internal context overload;
- restore the single direct directory/hash expression;
- restore the original short-circuit `while`;
- remove all timing locals and record emission;
- restore the catch-local `return ("ERROR", null)`;
- restore the untimed background lambda exactly.

Compare against:

```bash
git show 30448884415c44f446136fa3e11fb06cefe375d6:src/Accounts/Session.cs
```

- [ ] **Step 4: Restore every maintained caller**

With `apply_patch`, restore the exact approved-base call/bypass expressions in
T2IAPI, ImageHistoryAPI, and GridGenerator. Preserve unrelated changes only if
they were explicitly added after the approved base outside Rank 25; otherwise
the six temporary source paths must equal the approved base.

- [ ] **Step 5: Run the removal GREEN and exact-tree gate**

Run:

```bash
test ! -e src/Accounts/OutputFilenameSelectionMeasurement.cs
test -z "$(rg -l \
  'OutputFilenameMeasurementEnabled|OutputFilenameMeasurementScenario|Rank25OutputFilename|OutputFilenameSelectionContext|OutputFilenameSelectionAttempt' \
  src)"
git diff --quiet 30448884415c44f446136fa3e11fb06cefe375d6 -- src
git diff --check
test -z "$(find src -type d \( -name bin -o -name obj \) -print -quit)"
```

Expected: every command exits `0`. The final production tree is byte-identical
to the approved base.

- [ ] **Step 6: Commit removal**

```bash
git add \
  src/Accounts/OutputFilenameSelectionMeasurement.cs \
  src/Core/Settings.cs \
  src/Accounts/Session.cs \
  src/WebAPI/T2IAPI.cs \
  src/WebAPI/ImageHistoryAPI.cs \
  src/BuiltinExtensions/GridGenerator/GridGeneratorExtension.cs
git diff --cached --check
git commit -m "refactor: remove output filename measurement instrumentation"
```

Expected: inverse source projection; post-commit `git diff
30448884..HEAD -- src` is empty.

- [ ] **Step 7: Ask the maintainer for final uninstrumented sanity**

The maintainer builds/runs the final uninstrumented tree and confirms one normal
persisted save plus one data-URL bypass.

Expected response:

```text
RANK25_INSTRUMENTATION_REMOVAL_PASSED
```

If unavailable, record final runtime removal validation as unrun; static
byte-equality remains the only removal evidence.

### Task 10: Finalize Decision Documentation and Recommendation

**Files:**
- Modify:
  `docs/superpowers/specs/2026-07-30-output-filename-selection-measurement-design.md`
- Modify:
  `docs/superpowers/audits/2026-07-21-maintainability-architecture-refresh.md`

- [ ] **Step 1: Record final instrumentation history and source equality**

Record:

- design commit `30448884415c44f446136fa3e11fb06cefe375d6`;
- plan commit;
- each temporary instrumentation commit;
- removal commit;
- temporary six-file source projection;
- final empty source projection relative to `30448884`;
- static review tokens;
- maintainer collection/removal messages and normalized counts;
- decision and evidence limits.

- [ ] **Step 2: Apply the exact recommendation rule**

For `GO`, write:

```text
Rank 25 completed with a GO decision. The sole Recommended Next Project is a
new, separately brainstormed bounded filename-selection optimization design.
No cache, allocator, or production behavior change is yet authorized.
```

For `NO-GO`, write:

```text
Rank 25 completed with a NO-GO decision and no optimization is authorized.
Rank 26 becomes the sole Recommended Next Project as the next bounded
measurement prerequisite.
```

For `INSUFFICIENT`, write:

```text
Rank 25 completed the current collection with an INSUFFICIENT decision.
Rank 25 remains the sole Recommended Next Project for a newly planned bounded
recollection; dormant instrumentation was not retained.
```

Use exactly the branch supported by evidence; remove the other two from final
prose.

- [ ] **Step 3: Mark the design final**

Replace the design status with one of:

```text
**Status:** Measurement completed; GO; instrumentation removed
**Status:** Measurement completed; NO-GO; instrumentation removed
**Status:** Measurement completed; INSUFFICIENT; instrumentation removed
```

Append the evidence summary, decision rationale, removal proof, and remaining
caveats. Do not rewrite the approved pre-collection design as if outcomes had
been known in advance.

- [ ] **Step 4: Run documentation consistency checks**

Run:

```bash
test "$(rg -c '^## ' docs/superpowers/audits/2026-07-21-maintainability-architecture-refresh.md)" -eq 12
rg -n 'Rank 25|Recommended Next Project|GO|NO-GO|INSUFFICIENT|instrumentation removed' \
  docs/superpowers/specs/2026-07-30-output-filename-selection-measurement-design.md \
  docs/superpowers/audits/2026-07-21-maintainability-architecture-refresh.md
test -z "$(rg -n 'written-spec review pending|instrumentation removal is still pending' \
  docs/superpowers/specs/2026-07-30-output-filename-selection-measurement-design.md \
  docs/superpowers/audits/2026-07-21-maintainability-architecture-refresh.md)"
git diff --check
```

Expected: twelve audit sections, one consistent decision/recommendation, no
pending-state text, clean whitespace.

- [ ] **Step 5: Commit final documentation**

```bash
git add \
  docs/superpowers/specs/2026-07-30-output-filename-selection-measurement-design.md \
  docs/superpowers/audits/2026-07-21-maintainability-architecture-refresh.md
git diff --cached --check
git commit -m "docs: record output filename measurement decision"
```

Expected: exact two-document commit.

### Task 11: Final Integrated Review and Branch Completion

**Files:**
- Review the full range from `30448884415c44f446136fa3e11fb06cefe375d6`
  through final Rank 25 head.

- [ ] **Step 1: Verify final tree and history**

Run:

```bash
git status --short
git diff --quiet 30448884415c44f446136fa3e11fb06cefe375d6 -- src
git log --oneline --decorate \
  30448884415c44f446136fa3e11fb06cefe375d6..HEAD
git diff --name-only \
  30448884415c44f446136fa3e11fb06cefe375d6..HEAD
git diff --check \
  30448884415c44f446136fa3e11fb06cefe375d6..HEAD
```

Expected:

- clean Rank 25 worktree;
- no final source-tree difference;
- temporary instrumentation and removal commits visible in history;
- only plan/design/audit documentation in the final tree projection;
- clean whitespace.

- [ ] **Step 2: Obtain validation-document reviews**

Fresh specification review returns:

```text
RANK25_VALIDATION_SPEC_APPROVED
```

Fresh quality/evidence review returns:

```text
RANK25_VALIDATION_QUALITY_APPROVED
```

- [ ] **Step 3: Obtain final integrated review**

A fresh reviewer checks:

- Rank 24 disposition remained 24 passed, 0 failed, 18 deliberately unrun;
- Rank 25 did not implement an optimization;
- temporary instrumentation matched the approved schema/scope;
- behavioral failures, unrun cases, and privacy boundaries are truthful;
- the decision follows the qualitative gate;
- all instrumentation is removed;
- the final recommendation matches the decision;
- primary protected changes remain untouched.

Required token:

```text
RANK25_FINAL_INTEGRATED_APPROVED
```

- [ ] **Step 4: Use verification-before-completion**

Independently rerun the full static final-tree gate and inspect the maintainer
evidence before making any completion claim.

- [ ] **Step 5: Use finishing-a-development-branch**

Offer exactly:

1. merge locally;
2. push and create a Pull Request;
3. keep the branch as-is;
4. discard.

Do not merge, push, delete, or clean the worktree until the maintainer chooses.
