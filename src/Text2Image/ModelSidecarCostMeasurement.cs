using Newtonsoft.Json;
using SwarmUI.Core;
using SwarmUI.Utils;
using System.Diagnostics;
using System.Threading;

namespace SwarmUI.Text2Image;

/// <summary>Temporary, opt-in Rank 32 duplicate model-sidecar parsing cost recorder.</summary>
internal static class ModelSidecarCostMeasurement
{
    /// <summary>Prefix used for every Rank 32 measurement record.</summary>
    private const string Prefix = "[Rank32ModelSidecar]";

    /// <summary>Schema version for Rank 32 records.</summary>
    private const int Schema = 1;

    /// <summary>Exact privacy-safe scenario bases accepted by the recorder.</summary>
    private static readonly HashSet<string> AllowedScenarioBases =
    [
        "contract", "cache_central", "cache_per_folder", "suffix_single", "suffix_all", "invalid_json",
        "concurrent_change", "cache_unavailable", "cache_fault", "header_fault", "single_1k_1", "single_1k_4",
        "single_64k_1", "single_64k_4", "stress_single_1m_1", "stress_single_1m_4", "batch16_4k_4",
        "batch16_64k_4", "batch128_4k_4", "batch128_64k_1", "control_enabled", "control_disabled"
    ];

    /// <summary>Process-local source for record identifiers.</summary>
    private static long NextRecordId;

    /// <summary>One phase start snapshot.</summary>
    internal readonly record struct PhaseToken(long Timestamp, long Allocation);

    /// <summary>One measured LoadMetadata call.</summary>
    internal sealed class Operation : IDisposable
    {
        /// <summary>Process-local record identifier.</summary>
        internal long Id;

        /// <summary>Validated scenario label.</summary>
        internal string Scenario;

        /// <summary>Validated scenario base.</summary>
        internal string ScenarioBase;

        /// <summary>Bounded central/per-folder cache category.</summary>
        internal string CacheMode;

        /// <summary>Bounded model count implied by the fixed scenario.</summary>
        internal int ModelCount;

        /// <summary>Whole-call start timestamp.</summary>
        internal long StartTimestamp;

        /// <summary>Whole-call start current-thread allocation.</summary>
        internal long StartAllocation;

        /// <summary>Recomputation start timestamp.</summary>
        internal long RecomputeTimestamp;

        /// <summary>Recomputation start current-thread allocation.</summary>
        internal long RecomputeAllocation;

        /// <summary>Current bounded failure stage.</summary>
        internal string Stage = "none";

        /// <summary>Final bounded result.</summary>
        internal string Result;

        /// <summary>Bounded invalidation category.</summary>
        internal string Invalidation = "unknown";

        /// <summary>Whether a caught cache lookup failure occurred.</summary>
        internal bool CacheLookupCaught;

        /// <summary>Whether a caught embedded-header failure occurred.</summary>
        internal bool EmbeddedHeaderCaught;

        /// <summary>Whether a caught cache upsert failure occurred.</summary>
        internal bool UpsertCaught;

        /// <summary>Whether metadata record construction was reached.</summary>
        internal bool RecordConstructed;

        /// <summary>Whether persistent upsert was attempted.</summary>
        internal bool UpsertAttempted;

        /// <summary>Whether final model publication completed.</summary>
        internal bool Published;

        /// <summary>Number of fingerprint suffix inspections.</summary>
        internal long FingerprintInspections;

        /// <summary>First-pass existence checks.</summary>
        internal long FirstExists;

        /// <summary>First-pass existing suffixes.</summary>
        internal long FirstPresent;

        /// <summary>First-pass reads.</summary>
        internal long FirstReads;

        /// <summary>First-pass top-level parses.</summary>
        internal long FirstParses;

        /// <summary>First-pass merged property count.</summary>
        internal long FirstProperties;

        /// <summary>First-pass source character count.</summary>
        internal long FirstCharacters;

        /// <summary>Second-pass existence checks.</summary>
        internal long SecondExists;

        /// <summary>Second-pass existing suffixes.</summary>
        internal long SecondPresent;

        /// <summary>Second-pass reads.</summary>
        internal long SecondReads;

        /// <summary>Second-pass top-level parses.</summary>
        internal long SecondParses;

        /// <summary>Second-pass processed property count.</summary>
        internal long SecondProperties;

        /// <summary>Second-pass source character count.</summary>
        internal long SecondCharacters;

        /// <summary>Fingerprint elapsed microseconds and allocation.</summary>
        internal long FingerprintUs, FingerprintBytes;

        /// <summary>Cache lookup elapsed microseconds and allocation.</summary>
        internal long CacheUs, CacheBytes;

        /// <summary>Embedded-header elapsed microseconds and allocation.</summary>
        internal long HeaderUs, HeaderBytes;

        /// <summary>First-pass existence elapsed microseconds and allocation.</summary>
        internal long FirstExistsUs, FirstExistsBytes;

        /// <summary>First-pass read elapsed microseconds and allocation.</summary>
        internal long FirstReadUs, FirstReadBytes;

        /// <summary>First-pass parse elapsed microseconds and allocation.</summary>
        internal long FirstParseUs, FirstParseBytes;

        /// <summary>First-pass merge elapsed microseconds and allocation.</summary>
        internal long FirstMergeUs, FirstMergeBytes;

        /// <summary>Merged-header extraction elapsed microseconds and allocation.</summary>
        internal long MetaExtractUs, MetaExtractBytes;

        /// <summary>Second-pass existence elapsed microseconds and allocation.</summary>
        internal long SecondExistsUs, SecondExistsBytes;

        /// <summary>Second-pass read elapsed microseconds and allocation.</summary>
        internal long SecondReadUs, SecondReadBytes;

        /// <summary>Second-pass parse elapsed microseconds and allocation.</summary>
        internal long SecondParseUs, SecondParseBytes;

        /// <summary>Second-pass procAltHeader elapsed microseconds and allocation.</summary>
        internal long SecondProcUs, SecondProcBytes;

        /// <summary>Whole recomputation elapsed microseconds and allocation.</summary>
        internal long RecomputeUs, RecomputeBytes;

        /// <summary>Marks the operation complete and emits it without allowing failures to escape.</summary>
        public void Dispose()
        {
            Complete(this);
        }
    }

    /// <summary>Begins a measurement only for a valid enabled scenario.</summary>
    internal static Operation Begin()
    {
        try
        {
            if (!(Program.ServerSettings?.Performance?.ModelSidecarMeasurementEnabled ?? false))
            {
                return null;
            }
            string scenario = Program.ServerSettings.Performance.ModelSidecarMeasurementScenario ?? "";
            if (!TryValidateScenario(scenario, out string scenarioBase))
            {
                return null;
            }
            return new()
            {
                Id = Interlocked.Increment(ref NextRecordId),
                Scenario = scenario,
                ScenarioBase = scenarioBase,
                CacheMode = Program.ServerSettings.Metadata.ModelMetadataPerFolder ? "per_folder" : "central",
                ModelCount = ScenarioModelCount(scenarioBase),
                StartTimestamp = Stopwatch.GetTimestamp(),
                StartAllocation = GC.GetAllocatedBytesForCurrentThread()
            };
        }
        catch
        {
            return null;
        }
    }

    /// <summary>Begins one named phase.</summary>
    internal static PhaseToken BeginPhase(Operation operation, string stage)
    {
        if (operation is null)
        {
            return default;
        }
        operation.Stage = stage;
        return new(Stopwatch.GetTimestamp(), GC.GetAllocatedBytesForCurrentThread());
    }

    /// <summary>Ends and accumulates one named phase.</summary>
    internal static void EndPhase(Operation operation, PhaseToken token, string stage)
    {
        if (operation is null)
        {
            return;
        }
        long endTimestamp = Stopwatch.GetTimestamp();
        long endAllocation = GC.GetAllocatedBytesForCurrentThread();
        long microseconds = ToMicroseconds(token.Timestamp, endTimestamp);
        long allocation = Math.Max(0, endAllocation - token.Allocation);
        switch (stage)
        {
            case "fingerprint": operation.FingerprintUs += microseconds; operation.FingerprintBytes += allocation; break;
            case "cache_lookup": operation.CacheUs += microseconds; operation.CacheBytes += allocation; break;
            case "embedded_header": operation.HeaderUs += microseconds; operation.HeaderBytes += allocation; break;
            case "first_exists": operation.FirstExistsUs += microseconds; operation.FirstExistsBytes += allocation; break;
            case "first_read": operation.FirstReadUs += microseconds; operation.FirstReadBytes += allocation; break;
            case "first_parse": operation.FirstParseUs += microseconds; operation.FirstParseBytes += allocation; break;
            case "first_merge": operation.FirstMergeUs += microseconds; operation.FirstMergeBytes += allocation; break;
            case "meta_extract": operation.MetaExtractUs += microseconds; operation.MetaExtractBytes += allocation; break;
            case "second_exists": operation.SecondExistsUs += microseconds; operation.SecondExistsBytes += allocation; break;
            case "second_read": operation.SecondReadUs += microseconds; operation.SecondReadBytes += allocation; break;
            case "second_parse": operation.SecondParseUs += microseconds; operation.SecondParseBytes += allocation; break;
            case "second_proc": operation.SecondProcUs += microseconds; operation.SecondProcBytes += allocation; break;
        }
        operation.Stage = "none";
    }

    /// <summary>Notes the fixed fingerprint inspection count.</summary>
    internal static void NoteFingerprint(Operation operation, int suffixes)
    {
        if (operation is not null)
        {
            operation.FingerprintInspections = Math.Max(0, suffixes);
        }
    }

    /// <summary>Notes one sidecar existence result.</summary>
    internal static void NoteExists(Operation operation, bool firstPass, bool exists)
    {
        if (operation is null)
        {
            return;
        }
        if (firstPass)
        {
            operation.FirstExists++;
            operation.FirstPresent += exists ? 1 : 0;
        }
        else
        {
            operation.SecondExists++;
            operation.SecondPresent += exists ? 1 : 0;
        }
    }

    /// <summary>Notes one read without retaining its content.</summary>
    internal static void NoteRead(Operation operation, bool firstPass, int characters)
    {
        if (operation is null)
        {
            return;
        }
        if (firstPass)
        {
            operation.FirstReads++;
            operation.FirstCharacters += Math.Max(0, characters);
        }
        else
        {
            operation.SecondReads++;
            operation.SecondCharacters += Math.Max(0, characters);
        }
    }

    /// <summary>Notes one top-level parse and its already available property count.</summary>
    internal static void NoteParse(Operation operation, bool firstPass, int properties)
    {
        if (operation is null)
        {
            return;
        }
        if (firstPass)
        {
            operation.FirstParses++;
            operation.FirstProperties += Math.Max(0, properties);
        }
        else
        {
            operation.SecondParses++;
            operation.SecondProperties += Math.Max(0, properties);
        }
    }

    /// <summary>Starts recomputation and records its bounded invalidation cause.</summary>
    internal static void BeginRecompute(Operation operation, string invalidation)
    {
        if (operation is null)
        {
            return;
        }
        operation.Invalidation = invalidation is "cache_missing" or "cache_lookup_fault" or "legacy_text_encoders" or "model_mtime" or "sidecar_fingerprint"
            ? invalidation : "unknown";
        operation.RecomputeTimestamp = Stopwatch.GetTimestamp();
        operation.RecomputeAllocation = GC.GetAllocatedBytesForCurrentThread();
    }

    /// <summary>Ends the recomputation interval.</summary>
    internal static void EndRecompute(Operation operation)
    {
        if (operation is null || operation.RecomputeTimestamp == 0)
        {
            return;
        }
        operation.RecomputeUs = ToMicroseconds(operation.RecomputeTimestamp, Stopwatch.GetTimestamp());
        operation.RecomputeBytes = Math.Max(0, GC.GetAllocatedBytesForCurrentThread() - operation.RecomputeAllocation);
    }

    /// <summary>Marks a bounded caught failure while preserving continued production flow.</summary>
    internal static void NoteCaught(Operation operation, string stage)
    {
        if (operation is null)
        {
            return;
        }
        if (stage == "cache_lookup_caught")
        {
            operation.CacheLookupCaught = true;
        }
        else if (stage == "embedded_header_caught")
        {
            operation.EmbeddedHeaderCaught = true;
        }
        else if (stage == "upsert_caught")
        {
            operation.UpsertCaught = true;
        }
        operation.Stage = "none";
    }

    /// <summary>Marks cache unavailability before the existing return.</summary>
    internal static void MarkCacheUnavailable(Operation operation)
    {
        if (operation is not null)
        {
            operation.Result = "cache_unavailable";
            operation.Invalidation = "cache_unavailable";
            operation.Stage = "none";
        }
    }

    /// <summary>Marks a successful cache hit or recomputation.</summary>
    internal static void MarkCompleted(Operation operation, bool recomputed)
    {
        if (operation is not null)
        {
            operation.Result = recomputed ? "recomputed" : "cache_hit";
            operation.Invalidation = recomputed ? operation.Invalidation : "cache_hit";
            operation.Stage = "none";
        }
    }

    /// <summary>Sets a bounded current stage for untimed construction/publication work.</summary>
    internal static void SetStage(Operation operation, string stage)
    {
        if (operation is not null)
        {
            operation.Stage = stage;
        }
    }

    /// <summary>Runs the private deterministic post-fingerprint contract hook.</summary>
    internal static void RunAfterFingerprintHook(Operation operation, Action hook)
    {
        if (operation is not null && operation.ScenarioBase == "concurrent_change")
        {
            hook?.Invoke();
        }
    }

    /// <summary>Validates the exact scenario allowlist and bounded iteration suffix.</summary>
    private static bool TryValidateScenario(string scenario, out string scenarioBase)
    {
        scenarioBase = scenario.Before('/');
        if (!AllowedScenarioBases.Contains(scenarioBase))
        {
            return false;
        }
        if (scenario == scenarioBase)
        {
            return true;
        }
        if (!scenario.StartsWith($"{scenarioBase}/"))
        {
            return false;
        }
        string suffix = scenario[(scenarioBase.Length + 1)..];
        if (suffix.StartsWith("warmup_") && suffix.Length == 8 && suffix[7] is >= '0' and <= '4')
        {
            return true;
        }
        if (!suffix.StartsWith("measured_") || !int.TryParse(suffix[9..], out int iteration))
        {
            return false;
        }
        return iteration is >= 0 and <= 29 && suffix == $"measured_{iteration}";
    }

    /// <summary>Returns the fixed model count implied by an allowlisted scenario.</summary>
    private static int ScenarioModelCount(string scenarioBase)
    {
        if (scenarioBase.StartsWith("batch128_"))
        {
            return 128;
        }
        if (scenarioBase.StartsWith("batch16_"))
        {
            return 16;
        }
        return 1;
    }

    /// <summary>Converts stopwatch ticks to nonnegative whole microseconds.</summary>
    private static long ToMicroseconds(long start, long end)
    {
        if (end <= start)
        {
            return 0;
        }
        return (long)((end - start) * 1_000_000d / Stopwatch.Frequency);
    }

    /// <summary>Finalizes and queues one privacy-safe immutable record.</summary>
    private static void Complete(Operation operation)
    {
        try
        {
            long endTimestamp = Stopwatch.GetTimestamp();
            long endAllocation = GC.GetAllocatedBytesForCurrentThread();
            string result = operation.Result ?? "failed";
            string failureStage = result == "failed" ? BoundFailureStage(operation.Stage) : "none";
            object record = new
            {
                schema = Schema,
                record = "load_metadata",
                id = operation.Id,
                scenario = operation.Scenario,
                cache_mode = operation.CacheMode,
                model_count = operation.ModelCount,
                result,
                invalidation = operation.Invalidation,
                failure_stage = failureStage,
                cache_lookup_caught = operation.CacheLookupCaught,
                embedded_header_caught = operation.EmbeddedHeaderCaught,
                upsert_caught = operation.UpsertCaught,
                record_constructed = operation.RecordConstructed,
                upsert_attempted = operation.UpsertAttempted,
                published = operation.Published,
                fingerprint_inspections = operation.FingerprintInspections,
                first_exists = operation.FirstExists,
                first_present = operation.FirstPresent,
                first_reads = operation.FirstReads,
                first_parses = operation.FirstParses,
                first_properties = operation.FirstProperties,
                first_characters = operation.FirstCharacters,
                second_exists = operation.SecondExists,
                second_present = operation.SecondPresent,
                second_reads = operation.SecondReads,
                second_parses = operation.SecondParses,
                second_properties = operation.SecondProperties,
                second_characters = operation.SecondCharacters,
                fingerprint_us = operation.FingerprintUs,
                fingerprint_bytes = operation.FingerprintBytes,
                cache_us = operation.CacheUs,
                cache_bytes = operation.CacheBytes,
                header_us = operation.HeaderUs,
                header_bytes = operation.HeaderBytes,
                first_exists_us = operation.FirstExistsUs,
                first_exists_bytes = operation.FirstExistsBytes,
                first_read_us = operation.FirstReadUs,
                first_read_bytes = operation.FirstReadBytes,
                first_parse_us = operation.FirstParseUs,
                first_parse_bytes = operation.FirstParseBytes,
                first_merge_us = operation.FirstMergeUs,
                first_merge_bytes = operation.FirstMergeBytes,
                first_total_us = operation.FirstExistsUs + operation.FirstReadUs + operation.FirstParseUs + operation.FirstMergeUs,
                first_total_bytes = operation.FirstExistsBytes + operation.FirstReadBytes + operation.FirstParseBytes + operation.FirstMergeBytes,
                meta_extract_us = operation.MetaExtractUs,
                meta_extract_bytes = operation.MetaExtractBytes,
                second_exists_us = operation.SecondExistsUs,
                second_exists_bytes = operation.SecondExistsBytes,
                second_read_us = operation.SecondReadUs,
                second_read_bytes = operation.SecondReadBytes,
                second_parse_us = operation.SecondParseUs,
                second_parse_bytes = operation.SecondParseBytes,
                second_proc_us = operation.SecondProcUs,
                second_proc_bytes = operation.SecondProcBytes,
                second_total_us = operation.SecondExistsUs + operation.SecondReadUs + operation.SecondParseUs + operation.SecondProcUs,
                second_total_bytes = operation.SecondExistsBytes + operation.SecondReadBytes + operation.SecondParseBytes + operation.SecondProcBytes,
                sidecar_total_us = operation.FirstExistsUs + operation.FirstReadUs + operation.FirstParseUs + operation.FirstMergeUs
                    + operation.SecondExistsUs + operation.SecondReadUs + operation.SecondParseUs + operation.SecondProcUs,
                sidecar_total_bytes = operation.FirstExistsBytes + operation.FirstReadBytes + operation.FirstParseBytes + operation.FirstMergeBytes
                    + operation.SecondExistsBytes + operation.SecondReadBytes + operation.SecondParseBytes + operation.SecondProcBytes,
                recompute_us = operation.RecomputeUs,
                recompute_bytes = operation.RecomputeBytes,
                total_us = ToMicroseconds(operation.StartTimestamp, endTimestamp),
                allocation_bytes = Math.Max(0, endAllocation - operation.StartAllocation)
            };
            ThreadPool.UnsafeQueueUserWorkItem(static state =>
            {
                try
                {
                    Logs.Info($"{Prefix}{JsonConvert.SerializeObject(state, Formatting.None)}");
                }
                catch
                {
                }
            }, record, false);
        }
        catch
        {
        }
    }

    /// <summary>Bounds any propagating failure stage to the approved schema.</summary>
    private static string BoundFailureStage(string stage)
    {
        return stage is "fingerprint" or "first_exists" or "first_read" or "first_parse" or "first_merge" or "meta_extract"
            or "second_exists" or "second_read" or "second_parse" or "second_proc" or "record_construction" or "publication"
            ? stage : "none";
    }
}
