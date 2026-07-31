using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using SwarmUI.Core;
using SwarmUI.Utils;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;

namespace SwarmUI.Builtin_ComfyUIBackend;

/// <summary>Temporary, opt-in Rank 27 generated-workflow cleanup cost recorder.</summary>
internal static class WorkflowCleanupCostMeasurement
{
    /// <summary>Prefix used for every Rank 27 generated-workflow cleanup measurement record.</summary>
    private const string Prefix = "[Rank27WorkflowCleanup]";

    /// <summary>Schema version for Rank 27 generated-workflow cleanup measurement records.</summary>
    private const int Schema = 1;

    /// <summary>Maximum number of operator-supplied scenario characters inspected.</summary>
    private const int ScenarioInputCharacterLimit = 384;

    /// <summary>Maximum number of normalized scenario characters emitted.</summary>
    private const int ScenarioOutputCharacterLimit = 96;

    /// <summary>Maximum number of individually recorded stock class scans.</summary>
    private const int ScanSlotCount = 6;

    /// <summary>Process-local source for monotonically increasing cleanup measurement identifiers.</summary>
    private static long NextMeasurementId = 0;

    /// <summary>Endpoint data for one timed, current-thread allocation operation.</summary>
    internal readonly record struct OperationStart(long Timestamp, long Allocation);

    /// <summary>Start data and bounded ordinal for one class scan.</summary>
    internal readonly record struct ScanStart(int Ordinal, long Timestamp, long Allocation);

    /// <summary>Bounded aggregate for one stock class scan.</summary>
    internal sealed class ScanSlot
    {
        /// <summary>One-based scan ordinal within the cleanup action.</summary>
        public int Ordinal;

        /// <summary>Workflow nodes examined while building the scan snapshot.</summary>
        public long Examined;

        /// <summary>Matching nodes captured in the scan snapshot.</summary>
        public long Matched;

        /// <summary>Callbacks invoked from the completed scan snapshot.</summary>
        public long Callbacks;

        /// <summary>Elapsed microseconds for this scan.</summary>
        public long ElapsedUs;

        /// <summary>Current-thread allocated bytes for this scan.</summary>
        public long AllocationBytes;
    }

    /// <summary>Bounded measurements accumulated for one priority-200 cleanup action.</summary>
    internal sealed class Attempt
    {
        /// <summary>Unique process-local identifier for this cleanup attempt.</summary>
        public long MeasurementId;

        /// <summary>Normalized operator-supplied scenario.</summary>
        public string Scenario;

        /// <summary>One-based ordinal among priority-200 actions in this generation.</summary>
        public int ActionOrdinal;

        /// <summary>Timestamp immediately before the cleanup action.</summary>
        public long CleanupStartTimestamp;

        /// <summary>Current-thread allocated bytes immediately before the cleanup action.</summary>
        public long CleanupStartAllocation;

        /// <summary>Timestamp immediately after the cleanup action endpoint.</summary>
        public long CleanupEndTimestamp;

        /// <summary>Current-thread allocated bytes immediately after the cleanup action endpoint.</summary>
        public long CleanupEndAllocation;

        /// <summary>Whether pre-cleanup graph counts were captured successfully.</summary>
        public bool PreCountsValid;

        /// <summary>Pre-cleanup workflow node count.</summary>
        public long PreNodeCount;

        /// <summary>Pre-cleanup direct input-property count.</summary>
        public long PreDirectInputCount;

        /// <summary>Whether post-cleanup graph counts were captured successfully.</summary>
        public bool PostCountsValid;

        /// <summary>Post-cleanup workflow node count.</summary>
        public long PostNodeCount;

        /// <summary>Post-cleanup direct input-property count.</summary>
        public long PostDirectInputCount;

        /// <summary>Total number of class scans recorded, including bounded overflow scans.</summary>
        public int RunScanCount;

        /// <summary>Exactly six bounded stock class scan slots.</summary>
        public readonly ScanSlot[] ScanSlots = [new(), new(), new(), new(), new(), new()];

        /// <summary>Number of class scans beyond the six bounded stock slots.</summary>
        public int ScanOverflowCount;

        /// <summary>Nodes examined by class scans beyond the bounded stock slots.</summary>
        public long ScanOverflowExamined;

        /// <summary>Matches captured by class scans beyond the bounded stock slots.</summary>
        public long ScanOverflowMatched;

        /// <summary>Callbacks invoked by class scans beyond the bounded stock slots.</summary>
        public long ScanOverflowCallbacks;

        /// <summary>Elapsed microseconds for class scans beyond the bounded stock slots.</summary>
        public long ScanOverflowUs;

        /// <summary>Current-thread allocated bytes for class scans beyond the bounded stock slots.</summary>
        public long ScanOverflowAllocationBytes;

        /// <summary>Connection-replacement calls.</summary>
        public long ReplacementCalls;

        /// <summary>Workflow nodes examined by connection replacement.</summary>
        public long ReplacementNodes;

        /// <summary>Direct input properties examined by connection replacement.</summary>
        public long ReplacementDirectInputs;

        /// <summary>Exact two-token matches found by connection replacement.</summary>
        public long ReplacementMatches;

        /// <summary>Connection assignments made by connection replacement.</summary>
        public long ReplacementAssignments;

        /// <summary>Aggregate connection-replacement elapsed microseconds.</summary>
        public long ReplacementUs;

        /// <summary>Aggregate connection-replacement current-thread allocated bytes.</summary>
        public long ReplacementAllocationBytes;

        /// <summary>Fixed-point passes executed.</summary>
        public long FixedPointPasses;

        /// <summary>Candidate nodes snapshotted across fixed-point passes.</summary>
        public long FixedPointCandidates;

        /// <summary>Nodes removed across fixed-point passes.</summary>
        public long FixedPointRemovals;

        /// <summary>Aggregate fixed-point-loop elapsed microseconds.</summary>
        public long FixedPointUs;

        /// <summary>Aggregate fixed-point-loop current-thread allocated bytes.</summary>
        public long FixedPointAllocationBytes;

        /// <summary>Connectivity indexes actually rebuilt.</summary>
        public long ConnectivityRebuilds;

        /// <summary>Workflow nodes examined while rebuilding connectivity indexes.</summary>
        public long ConnectivityNodes;

        /// <summary>Direct input properties examined while rebuilding connectivity indexes.</summary>
        public long ConnectivityDirectInputs;

        /// <summary>Aggregate connectivity-index rebuild elapsed microseconds.</summary>
        public long ConnectivityUs;

        /// <summary>Aggregate connectivity-index rebuild current-thread allocated bytes.</summary>
        public long ConnectivityAllocationBytes;

        /// <summary>Whether post-cleanup compact serialization was captured successfully.</summary>
        public bool SerializationValid;

        /// <summary>Post-cleanup compact serialization elapsed microseconds.</summary>
        public long SerializationUs;

        /// <summary>Post-cleanup compact serialization current-thread allocated bytes.</summary>
        public long SerializationAllocationBytes;

        /// <summary>Post-cleanup compact serialized character count.</summary>
        public int SerializationCharacters;

        /// <summary>Bounded final attempt outcome.</summary>
        public string Outcome = "failed";

        /// <summary>Whether this attempt has already reached a completion endpoint.</summary>
        public bool IsComplete;

        /// <summary>Begins one bounded class-scan measurement.</summary>
        public ScanStart BeginScan()
        {
            try
            {
                int ordinal = ++RunScanCount;
                return new(ordinal, Stopwatch.GetTimestamp(), GC.GetAllocatedBytesForCurrentThread());
            }
            catch
            {
                return new(0, 0, 0);
            }
        }

        /// <summary>Records one completed class scan without retaining class or graph data.</summary>
        public void CompleteScan(ScanStart scan, long examined, long matched, long callbacks)
        {
            try
            {
                long endTimestamp = Stopwatch.GetTimestamp();
                long endAllocation = GC.GetAllocatedBytesForCurrentThread();
                long elapsedUs = ToMicroseconds(scan.Timestamp, endTimestamp);
                long allocationBytes = AllocationDelta(scan.Allocation, endAllocation);
                if (scan.Ordinal is >= 1 and <= ScanSlotCount)
                {
                    ScanSlot slot = ScanSlots[scan.Ordinal - 1];
                    slot.Ordinal = scan.Ordinal;
                    slot.Examined = Math.Max(0, examined);
                    slot.Matched = Math.Max(0, matched);
                    slot.Callbacks = Math.Max(0, callbacks);
                    slot.ElapsedUs = elapsedUs;
                    slot.AllocationBytes = allocationBytes;
                }
                else
                {
                    ScanOverflowCount++;
                    ScanOverflowExamined += Math.Max(0, examined);
                    ScanOverflowMatched += Math.Max(0, matched);
                    ScanOverflowCallbacks += Math.Max(0, callbacks);
                    ScanOverflowUs += elapsedUs;
                    ScanOverflowAllocationBytes += allocationBytes;
                }
            }
            catch
            {
            }
        }

        /// <summary>Begins a generic measured editor operation.</summary>
        public OperationStart BeginOperation()
        {
            try
            {
                return new(Stopwatch.GetTimestamp(), GC.GetAllocatedBytesForCurrentThread());
            }
            catch
            {
                return new(0, 0);
            }
        }

        /// <summary>Records one completed connection-replacement operation.</summary>
        public void CompleteReplacement(OperationStart start, long nodes, long directInputs, long matches, long assignments)
        {
            try
            {
                long endTimestamp = Stopwatch.GetTimestamp();
                long endAllocation = GC.GetAllocatedBytesForCurrentThread();
                ReplacementCalls++;
                ReplacementNodes += Math.Max(0, nodes);
                ReplacementDirectInputs += Math.Max(0, directInputs);
                ReplacementMatches += Math.Max(0, matches);
                ReplacementAssignments += Math.Max(0, assignments);
                ReplacementUs += ToMicroseconds(start.Timestamp, endTimestamp);
                ReplacementAllocationBytes += AllocationDelta(start.Allocation, endAllocation);
            }
            catch
            {
            }
        }

        /// <summary>Records one completed fixed-point cleanup operation.</summary>
        public void CompleteFixedPoint(OperationStart start, long passes, long candidates, long removals)
        {
            try
            {
                long endTimestamp = Stopwatch.GetTimestamp();
                long endAllocation = GC.GetAllocatedBytesForCurrentThread();
                FixedPointPasses += Math.Max(0, passes);
                FixedPointCandidates += Math.Max(0, candidates);
                FixedPointRemovals += Math.Max(0, removals);
                FixedPointUs += ToMicroseconds(start.Timestamp, endTimestamp);
                FixedPointAllocationBytes += AllocationDelta(start.Allocation, endAllocation);
            }
            catch
            {
            }
        }

        /// <summary>Records one completed connectivity-index rebuild operation.</summary>
        public void CompleteConnectivity(OperationStart start, long nodes, long directInputs)
        {
            try
            {
                long endTimestamp = Stopwatch.GetTimestamp();
                long endAllocation = GC.GetAllocatedBytesForCurrentThread();
                ConnectivityRebuilds++;
                ConnectivityNodes += Math.Max(0, nodes);
                ConnectivityDirectInputs += Math.Max(0, directInputs);
                ConnectivityUs += ToMicroseconds(start.Timestamp, endTimestamp);
                ConnectivityAllocationBytes += AllocationDelta(start.Allocation, endAllocation);
            }
            catch
            {
            }
        }
    }

    /// <summary>Returns whether Rank 27 cleanup measurement is enabled without allowing settings failures to escape.</summary>
    internal static bool IsEnabled()
    {
        try
        {
            return Program.ServerSettings?.Performance?.WorkflowCleanupMeasurementEnabled ?? false;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>Begins one priority-200 cleanup attempt, including enabled-only pre-cleanup graph counts.</summary>
    internal static Attempt BeginAttempt(int actionOrdinal, JObject workflow)
    {
        if (!IsEnabled())
        {
            return null;
        }
        try
        {
            Attempt attempt = new()
            {
                MeasurementId = Interlocked.Increment(ref NextMeasurementId),
                Scenario = GetScenario(),
                ActionOrdinal = Math.Max(0, actionOrdinal)
            };
            CaptureGraphCounts(workflow, out attempt.PreCountsValid, out attempt.PreNodeCount, out attempt.PreDirectInputCount);
            attempt.CleanupStartTimestamp = Stopwatch.GetTimestamp();
            attempt.CleanupStartAllocation = GC.GetAllocatedBytesForCurrentThread();
            return attempt;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>Completes and emits a successful cleanup attempt without retaining serialized graph data.</summary>
    internal static void CompleteSuccess(Attempt attempt, JObject workflow)
    {
        if (attempt is null || attempt.IsComplete)
        {
            return;
        }
        try
        {
            CaptureCleanupEndpoint(attempt, "completed");
            CaptureGraphCounts(workflow, out attempt.PostCountsValid, out attempt.PostNodeCount, out attempt.PostDirectInputCount);
            try
            {
                long startTimestamp = Stopwatch.GetTimestamp();
                long startAllocation = GC.GetAllocatedBytesForCurrentThread();
                string compact = workflow.ToString(Formatting.None);
                long endTimestamp = Stopwatch.GetTimestamp();
                long endAllocation = GC.GetAllocatedBytesForCurrentThread();
                attempt.SerializationUs = ToMicroseconds(startTimestamp, endTimestamp);
                attempt.SerializationAllocationBytes = AllocationDelta(startAllocation, endAllocation);
                attempt.SerializationCharacters = compact.Length;
                attempt.SerializationValid = true;
            }
            catch
            {
                attempt.SerializationValid = false;
            }
            EmitCompleted(attempt);
        }
        catch
        {
        }
    }

    /// <summary>Completes and emits a failed cleanup attempt without capturing exception details.</summary>
    internal static void CompleteFailure(Attempt attempt)
    {
        if (attempt is null || attempt.IsComplete)
        {
            return;
        }
        try
        {
            CaptureCleanupEndpoint(attempt, "failed");
            EmitCompleted(attempt);
        }
        catch
        {
        }
    }

    /// <summary>Captures the cleanup endpoint before graph traversal, JSON construction, or logging.</summary>
    private static void CaptureCleanupEndpoint(Attempt attempt, string outcome)
    {
        attempt.CleanupEndTimestamp = Stopwatch.GetTimestamp();
        attempt.CleanupEndAllocation = GC.GetAllocatedBytesForCurrentThread();
        attempt.Outcome = NormalizeOutcome(outcome);
        attempt.IsComplete = true;
    }

    /// <summary>Best-effort counts workflow nodes and direct input properties without retaining graph data.</summary>
    private static void CaptureGraphCounts(JObject workflow, out bool valid, out long nodes, out long directInputs)
    {
        valid = false;
        nodes = 0;
        directInputs = 0;
        try
        {
            if (workflow is null)
            {
                return;
            }
            foreach (JProperty property in workflow.Properties())
            {
                nodes++;
                if (property.Value is not JObject node)
                {
                    nodes = 0;
                    directInputs = 0;
                    return;
                }
                if (node["inputs"] is JObject inputs)
                {
                    foreach (JProperty input in inputs.Properties())
                    {
                        directInputs++;
                    }
                }
            }
            valid = true;
        }
        catch
        {
            nodes = 0;
            directInputs = 0;
        }
    }

    /// <summary>Emits one bounded aggregate after all measurement endpoints have been captured.</summary>
    private static void EmitCompleted(Attempt attempt)
    {
        try
        {
            List<object> scans = new(ScanSlotCount);
            for (int i = 0; i < ScanSlotCount; i++)
            {
                ScanSlot slot = attempt.ScanSlots[i];
                if (slot.Ordinal == 0)
                {
                    continue;
                }
                scans.Add(new
                {
                    ordinal = slot.Ordinal,
                    examined = slot.Examined,
                    matched = slot.Matched,
                    callbacks = slot.Callbacks,
                    elapsed_us = slot.ElapsedUs,
                    allocation_bytes = slot.AllocationBytes
                });
            }
            object record = new
            {
                schema = Schema,
                record = "cleanup",
                measurement_id = attempt.MeasurementId,
                scenario = attempt.Scenario,
                action_ordinal = attempt.ActionOrdinal,
                outcome = NormalizeOutcome(attempt.Outcome),
                pre_counts_valid = attempt.PreCountsValid,
                pre_node_count = attempt.PreNodeCount,
                pre_direct_input_count = attempt.PreDirectInputCount,
                post_counts_valid = attempt.PostCountsValid,
                post_node_count = attempt.PostNodeCount,
                post_direct_input_count = attempt.PostDirectInputCount,
                cleanup_us = ToMicroseconds(attempt.CleanupStartTimestamp, attempt.CleanupEndTimestamp),
                cleanup_allocation_bytes = AllocationDelta(attempt.CleanupStartAllocation, attempt.CleanupEndAllocation),
                run_scan_count = attempt.RunScanCount,
                run_scans = scans,
                scan_overflow_count = attempt.ScanOverflowCount,
                scan_overflow_examined = attempt.ScanOverflowExamined,
                scan_overflow_matched = attempt.ScanOverflowMatched,
                scan_overflow_callbacks = attempt.ScanOverflowCallbacks,
                scan_overflow_us = attempt.ScanOverflowUs,
                scan_overflow_allocation_bytes = attempt.ScanOverflowAllocationBytes,
                replacement_calls = attempt.ReplacementCalls,
                replacement_nodes = attempt.ReplacementNodes,
                replacement_direct_inputs = attempt.ReplacementDirectInputs,
                replacement_matches = attempt.ReplacementMatches,
                replacement_assignments = attempt.ReplacementAssignments,
                replacement_us = attempt.ReplacementUs,
                replacement_allocation_bytes = attempt.ReplacementAllocationBytes,
                fixed_point_passes = attempt.FixedPointPasses,
                fixed_point_candidates = attempt.FixedPointCandidates,
                fixed_point_removals = attempt.FixedPointRemovals,
                fixed_point_us = attempt.FixedPointUs,
                fixed_point_allocation_bytes = attempt.FixedPointAllocationBytes,
                connectivity_rebuilds = attempt.ConnectivityRebuilds,
                connectivity_nodes = attempt.ConnectivityNodes,
                connectivity_direct_inputs = attempt.ConnectivityDirectInputs,
                connectivity_us = attempt.ConnectivityUs,
                connectivity_allocation_bytes = attempt.ConnectivityAllocationBytes,
                serialization_valid = attempt.SerializationValid,
                serialization_us = attempt.SerializationUs,
                serialization_allocation_bytes = attempt.SerializationAllocationBytes,
                serialization_characters = attempt.SerializationCharacters
            };
            string json = JsonConvert.SerializeObject(record, Formatting.None);
            Logs.Info($"{Prefix}{json}");
        }
        catch
        {
        }
    }

    /// <summary>Normalizes an operator-supplied scenario to a bounded privacy-safe category.</summary>
    private static string GetScenario()
    {
        try
        {
            string input = Program.ServerSettings?.Performance?.WorkflowCleanupMeasurementScenario ?? "";
            if (input.Length == 0)
            {
                return "unspecified";
            }
            if (input.Length > ScenarioInputCharacterLimit)
            {
                return "invalid";
            }
            int outputLength = Math.Min(input.Length, ScenarioOutputCharacterLimit);
            for (int i = 0; i < input.Length; i++)
            {
                char c = input[i];
                bool allowed = c is >= 'a' and <= 'z' or >= '0' and <= '9' or '/' or '_' or '-';
                if (!allowed)
                {
                    return "invalid";
                }
            }
            return input[..outputLength];
        }
        catch
        {
            return "unspecified";
        }
    }

    /// <summary>Normalizes final attempt outcomes to the bounded schema catalog.</summary>
    private static string NormalizeOutcome(string outcome)
    {
        return outcome == "completed" ? "completed" : "failed";
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
