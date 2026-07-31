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
}
