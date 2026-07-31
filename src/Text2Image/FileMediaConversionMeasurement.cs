using Newtonsoft.Json;
using SwarmUI.Core;
using SwarmUI.Utils;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Threading;

namespace SwarmUI.Text2Image;

/// <summary>Temporary, opt-in Rank 28 file-media conversion cost recorder.</summary>
internal static class FileMediaConversionMeasurement
{
    /// <summary>Prefix used for every Rank 28 record.</summary>
    private const string Prefix = "[Rank28FileMedia]";

    /// <summary>Schema version for Rank 28 records.</summary>
    private const int Schema = 1;

    /// <summary>Maximum number of operator-supplied scenario characters inspected.</summary>
    private const int ScenarioInputCharacterLimit = 384;

    /// <summary>Maximum number of normalized scenario characters emitted.</summary>
    private const int ScenarioOutputCharacterLimit = 96;

    /// <summary>Process-local source for record identifiers.</summary>
    private static long NextRecordId;

    /// <summary>Process-local source for opaque authorized-path identifiers.</summary>
    private static long NextPathId;

    /// <summary>Tracks the last observed enable state so path identifiers are cleared on disable.</summary>
    private static int WasEnabled;

    /// <summary>Serializes measurement enable transitions and path identity assignment.</summary>
    private static readonly object PathIdentityLock = new();

    /// <summary>Process-random key used to avoid retaining normalized paths in the identity map.</summary>
    private static readonly byte[] PathIdentityKey = RandomNumberGenerator.GetBytes(32);

    /// <summary>Opaque process-local identifiers keyed by nonreversible salted path fingerprints.</summary>
    private static readonly ConcurrentDictionary<string, long> PathIds = new();

    /// <summary>Current nested measurement scope for this execution context.</summary>
    private static readonly AsyncLocal<Scope> CurrentScope = new();

    /// <summary>Start endpoints for one file conversion.</summary>
    internal readonly record struct CallStart(long Timestamp, long Allocation, string Context, bool IsValid);

    /// <summary>One nested inclusive request/preset/late/engine measurement scope.</summary>
    internal sealed class Scope : IDisposable
    {
        /// <summary>Parent inclusive scope, if any.</summary>
        internal Scope Parent;

        /// <summary>Bounded phase category.</summary>
        internal string Phase;

        /// <summary>Bounded maintained-caller category.</summary>
        internal string Context;

        /// <summary>Whether this context is known to execute while a generation claim is held.</summary>
        internal bool ClaimHeld;

        /// <summary>Start timestamp.</summary>
        internal long StartTimestamp;

        /// <summary>Start current-thread allocation.</summary>
        internal long StartAllocation;

        /// <summary>Number of media items observed.</summary>
        internal long MediaItems;

        /// <summary>Number of file conversion calls observed.</summary>
        internal long FileCalls;

        /// <summary>Number of pending-save source calls.</summary>
        internal long PendingCalls;

        /// <summary>Number of filesystem source calls.</summary>
        internal long DiskCalls;

        /// <summary>Number of data-URL bypass items.</summary>
        internal long DataUrlItems;

        /// <summary>Number of raw-base64 bypass items.</summary>
        internal long RawBase64Items;

        /// <summary>Number of empty bypass items.</summary>
        internal long EmptyItems;

        /// <summary>Number of invalid media items.</summary>
        internal long InvalidItems;

        /// <summary>Total source bytes observed by file calls.</summary>
        internal long SourceBytes;

        /// <summary>Opaque path identifiers observed by this inclusive scope.</summary>
        internal readonly HashSet<long> UniquePathIds = [];

        /// <summary>Number of file calls whose path identifier repeated in this scope.</summary>
        internal long RepeatedPathCalls;

        /// <summary>Whether this scope has already emitted.</summary>
        internal bool IsComplete;

        /// <summary>Completes this inclusive scope.</summary>
        public void Dispose()
        {
            CompleteScope(this);
        }
    }

    /// <summary>Returns whether Rank 28 measurement is enabled without allowing settings failures to escape.</summary>
    internal static bool IsEnabled()
    {
        try
        {
            bool enabled = ReadEnabledSetting();
            lock (PathIdentityLock)
            {
                if (enabled)
                {
                    WasEnabled = 1;
                }
                else if (WasEnabled == 1)
                {
                    PathIds.Clear();
                    WasEnabled = 0;
                }
            }
            return enabled;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>Begins one nested inclusive scope, or returns null while disabled.</summary>
    internal static Scope BeginScope(string phase)
    {
        if (!IsEnabled())
        {
            return null;
        }
        try
        {
            string normalizedPhase = NormalizePhase(phase);
            string context = ClassifyContext(normalizedPhase);
            Scope parent = GetActiveScope();
            Scope scope = new()
            {
                Parent = parent,
                Phase = normalizedPhase,
                Context = context,
                ClaimHeld = ContextHoldsClaim(context) || parent?.ClaimHeld == true,
                StartTimestamp = Stopwatch.GetTimestamp(),
                StartAllocation = GC.GetAllocatedBytesForCurrentThread()
            };
            CurrentScope.Value = scope;
            return scope;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>Begins one file conversion measurement.</summary>
    internal static CallStart BeginFileCall()
    {
        if (!IsEnabled())
        {
            return new(0, 0, "other", false);
        }
        try
        {
            return new(Stopwatch.GetTimestamp(), GC.GetAllocatedBytesForCurrentThread(), GetActiveScope()?.Context ?? "other", true);
        }
        catch
        {
            return new(0, 0, "other", false);
        }
    }

    /// <summary>Notes one media item that bypasses file conversion.</summary>
    internal static void NoteMediaItem(string category)
    {
        if (!IsEnabled())
        {
            return;
        }
        try
        {
            string bounded = NormalizeBypass(category);
            for (Scope scope = GetActiveScope(); scope is not null; scope = scope.Parent)
            {
                lock (scope)
                {
                    if (scope.IsComplete)
                    {
                        continue;
                    }
                    scope.MediaItems++;
                    if (bounded == "data_url")
                    {
                        scope.DataUrlItems++;
                    }
                    else if (bounded == "raw_base64")
                    {
                        scope.RawBase64Items++;
                    }
                    else if (bounded == "empty")
                    {
                        scope.EmptyItems++;
                    }
                    else if (bounded == "invalid")
                    {
                        scope.InvalidItems++;
                    }
                }
            }
        }
        catch
        {
        }
    }

    /// <summary>Notes invalid validation of an item already counted by a completed file call.</summary>
    internal static void NoteInvalidAfterFileCall()
    {
        if (!IsEnabled())
        {
            return;
        }
        try
        {
            for (Scope scope = GetActiveScope(); scope is not null; scope = scope.Parent)
            {
                lock (scope)
                {
                    if (!scope.IsComplete)
                    {
                        scope.InvalidItems++;
                    }
                }
            }
        }
        catch
        {
        }
    }

    /// <summary>Completes and emits one successful file conversion.</summary>
    internal static void CompleteFileCall(CallStart start, string normalizedPath, string source, long sourceBytes,
        long authorizationUs, long waitUs, long readUs, long encodeUs)
    {
        CompleteFileCall(start, normalizedPath, source, sourceBytes, authorizationUs, waitUs, readUs, encodeUs, "completed");
    }

    /// <summary>Completes and emits one failed file conversion without exception or path detail.</summary>
    internal static void FailFileCall(CallStart start, string normalizedPath, string source, long authorizationUs, long waitUs, long readUs, long encodeUs)
    {
        CompleteFileCall(start, normalizedPath, source, 0, authorizationUs, waitUs, readUs, encodeUs, "failed");
    }

    /// <summary>Completes one file call and updates every inclusive scope.</summary>
    private static void CompleteFileCall(CallStart start, string normalizedPath, string source, long sourceBytes,
        long authorizationUs, long waitUs, long readUs, long encodeUs, string outcome)
    {
        if (!start.IsValid)
        {
            return;
        }
        try
        {
            long endTimestamp = Stopwatch.GetTimestamp();
            long endAllocation = GC.GetAllocatedBytesForCurrentThread();
            long pathId = 0;
            lock (PathIdentityLock)
            {
                if (!ReadEnabledSetting())
                {
                    PathIds.Clear();
                    WasEnabled = 0;
                    return;
                }
                WasEnabled = 1;
                if (normalizedPath is not null)
                {
                    string fingerprint = Convert.ToHexString(HMACSHA256.HashData(PathIdentityKey, Encoding.UTF8.GetBytes(normalizedPath)));
                    pathId = PathIds.GetOrAdd(fingerprint, _ => Interlocked.Increment(ref NextPathId));
                }
            }
            string boundedSource = NormalizeSource(source);
            long boundedBytes = Math.Max(0, sourceBytes);
            for (Scope scope = GetActiveScope(); scope is not null; scope = scope.Parent)
            {
                lock (scope)
                {
                    if (scope.IsComplete)
                    {
                        continue;
                    }
                    scope.MediaItems++;
                    scope.FileCalls++;
                    scope.SourceBytes += boundedBytes;
                    if (boundedSource is "pending" or "pending_then_disk")
                    {
                        scope.PendingCalls++;
                    }
                    if (boundedSource is "disk" or "pending_then_disk")
                    {
                        scope.DiskCalls++;
                    }
                    if (pathId > 0 && !scope.UniquePathIds.Add(pathId))
                    {
                        scope.RepeatedPathCalls++;
                    }
                }
            }
            object record = new
            {
                schema = Schema,
                record = "file_call",
                record_id = Interlocked.Increment(ref NextRecordId),
                scenario = GetScenario(),
                context = NormalizeContext(start.Context),
                outcome = outcome == "completed" ? "completed" : "failed",
                path_id = pathId,
                source = boundedSource,
                source_bytes = boundedBytes,
                authorization_us = Math.Max(0, authorizationUs),
                wait_us = Math.Max(0, waitUs),
                read_us = Math.Max(0, readUs),
                encode_us = Math.Max(0, encodeUs),
                total_us = ToMicroseconds(start.Timestamp, endTimestamp),
                allocation_bytes = AllocationDelta(start.Allocation, endAllocation)
            };
            Emit(record);
        }
        catch
        {
        }
    }

    /// <summary>Completes and emits one nested inclusive scope.</summary>
    private static void CompleteScope(Scope scope)
    {
        if (scope is null)
        {
            return;
        }
        try
        {
            lock (scope)
            {
                if (scope.IsComplete)
                {
                    return;
                }
                long endTimestamp = Stopwatch.GetTimestamp();
                long endAllocation = GC.GetAllocatedBytesForCurrentThread();
                scope.IsComplete = true;
                if (ReferenceEquals(CurrentScope.Value, scope))
                {
                    CurrentScope.Value = GetFirstActive(scope.Parent);
                }
                object record = new
                {
                    schema = Schema,
                    record = "scope",
                    record_id = Interlocked.Increment(ref NextRecordId),
                    scenario = GetScenario(),
                    phase = NormalizePhase(scope.Phase),
                    context = NormalizeContext(scope.Context),
                    claim_held = scope.ClaimHeld,
                    media_items = Math.Max(0, scope.MediaItems),
                    file_calls = Math.Max(0, scope.FileCalls),
                    unique_paths = scope.UniquePathIds.Count,
                    repeated_path_calls = Math.Max(0, scope.RepeatedPathCalls),
                    pending_calls = Math.Max(0, scope.PendingCalls),
                    disk_calls = Math.Max(0, scope.DiskCalls),
                    data_url_items = Math.Max(0, scope.DataUrlItems),
                    raw_base64_items = Math.Max(0, scope.RawBase64Items),
                    empty_items = Math.Max(0, scope.EmptyItems),
                    invalid_items = Math.Max(0, scope.InvalidItems),
                    source_bytes = Math.Max(0, scope.SourceBytes),
                    inclusive_us = ToMicroseconds(scope.StartTimestamp, endTimestamp),
                    allocation_bytes = AllocationDelta(scope.StartAllocation, endAllocation)
                };
                Emit(record);
            }
        }
        catch
        {
            try
            {
                if (ReferenceEquals(CurrentScope.Value, scope))
                {
                    CurrentScope.Value = GetFirstActive(scope.Parent);
                }
            }
            catch
            {
            }
        }
    }

    /// <summary>Classifies the enabled caller stack into a fixed maintained-context catalog.</summary>
    private static string ClassifyContext(string phase)
    {
        try
        {
            StackFrame[] frames = new StackTrace().GetFrames();
            for (int i = 0; i < frames.Length; i++)
            {
                string type = frames[i].GetMethod()?.DeclaringType?.FullName ?? "";
                string method = frames[i].GetMethod()?.Name ?? "";
                if (type.Contains("T2IAPI") && (method == "GenT2I_Internal" || type.Contains("<GenT2I_Internal>")))
                {
                    return "core_generation";
                }
                if (type.Contains("ImageHistoryAPI"))
                {
                    return "image_history";
                }
                if (type.Contains("ComfyUIWebAPI"))
                {
                    return "workflow_preview";
                }
                if (type.Contains("GridGenerator"))
                {
                    return phase == "preset" ? "grid_cell" : "grid_outer";
                }
                if (type.Contains("ImageBatchTool"))
                {
                    return "image_batch";
                }
                if (type.Contains("ModelsAPI") && (method == "TestPromptFill" || type.Contains("<TestPromptFill>")))
                {
                    return "prompt_fill";
                }
                if (type.Contains("T2IPromptHandling"))
                {
                    return "late_tag";
                }
                if (type.Contains("T2IEngine"))
                {
                    return "engine_task";
                }
            }
        }
        catch
        {
        }
        return "other";
    }

    /// <summary>Returns the current noncompleted scope and prunes flowed completed scopes.</summary>
    private static Scope GetActiveScope()
    {
        Scope current = CurrentScope.Value;
        Scope active = GetFirstActive(current);
        if (!ReferenceEquals(current, active))
        {
            CurrentScope.Value = active;
        }
        return active;
    }

    /// <summary>Returns the first noncompleted scope in a parent chain.</summary>
    private static Scope GetFirstActive(Scope scope)
    {
        while (scope is not null)
        {
            Scope parent;
            lock (scope)
            {
                if (!scope.IsComplete)
                {
                    return scope;
                }
                parent = scope.Parent;
            }
            scope = parent;
        }
        return null;
    }

    /// <summary>Returns whether a maintained context is known to hold a generation claim.</summary>
    private static bool ContextHoldsClaim(string context)
    {
        return context is "core_generation" or "grid_outer" or "grid_cell" or "image_batch" or "engine_task";
    }

    /// <summary>Emits one compact record without allowing diagnostic failures to escape.</summary>
    private static void Emit(object record)
    {
        try
        {
            string message = $"{Prefix}{JsonConvert.SerializeObject(record, Formatting.None)}";
            ThreadPool.UnsafeQueueUserWorkItem(static state =>
            {
                try
                {
                    Logs.Info(state);
                }
                catch
                {
                }
            }, message, false);
        }
        catch
        {
        }
    }

    /// <summary>Reads the temporary enable setting without mutating recorder state.</summary>
    private static bool ReadEnabledSetting()
    {
        try
        {
            return Program.ServerSettings?.Performance?.FileMediaConversionMeasurementEnabled ?? false;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>Returns a bounded privacy-safe operator scenario.</summary>
    private static string GetScenario()
    {
        try
        {
            string input = Program.ServerSettings?.Performance?.FileMediaConversionMeasurementScenario ?? "";
            if (input.Length == 0)
            {
                return "unspecified";
            }
            if (input.Length > ScenarioInputCharacterLimit)
            {
                return "invalid";
            }
            for (int i = 0; i < input.Length; i++)
            {
                char c = input[i];
                if (!(c is >= 'a' and <= 'z' or >= '0' and <= '9' or '/' or '_' or '-'))
                {
                    return "invalid";
                }
            }
            return input[..Math.Min(input.Length, ScenarioOutputCharacterLimit)];
        }
        catch
        {
            return "unspecified";
        }
    }

    /// <summary>Normalizes scope phases.</summary>
    private static string NormalizePhase(string phase)
    {
        return phase is "request" or "preset" or "late" or "engine_pre_backend" ? phase : "other";
    }

    /// <summary>Normalizes maintained caller contexts.</summary>
    private static string NormalizeContext(string context)
    {
        return context is "core_generation" or "image_history" or "workflow_preview" or "grid_outer" or "grid_cell"
            or "image_batch" or "prompt_fill" or "late_tag" or "engine_task" ? context : "other";
    }

    /// <summary>Normalizes media bypass categories.</summary>
    private static string NormalizeBypass(string category)
    {
        return category is "data_url" or "raw_base64" or "empty" or "invalid" ? category : "invalid";
    }

    /// <summary>Normalizes file content source categories.</summary>
    private static string NormalizeSource(string source)
    {
        return source is "pending" or "disk" or "pending_then_disk" or "none" ? source : "none";
    }

    /// <summary>Converts a monotonic timestamp range to nonnegative integer microseconds.</summary>
    internal static long ToMicroseconds(long startTimestamp, long endTimestamp)
    {
        long ticks = Math.Max(0, endTimestamp - startTimestamp);
        return (ticks / Stopwatch.Frequency) * 1_000_000 + ((ticks % Stopwatch.Frequency) * 1_000_000 / Stopwatch.Frequency);
    }

    /// <summary>Returns a nonnegative current-thread allocation delta.</summary>
    private static long AllocationDelta(long startAllocation, long endAllocation)
    {
        return Math.Max(0, endAllocation - startAllocation);
    }
}
