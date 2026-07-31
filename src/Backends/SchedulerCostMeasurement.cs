using Newtonsoft.Json;
using SwarmUI.Core;
using SwarmUI.Utils;
using System.Diagnostics;

namespace SwarmUI.Backends;

/// <summary>Temporary, opt-in Rank 26 scheduler-cost measurement recorder.</summary>
internal static class SchedulerCostMeasurement
{
    /// <summary>Prefix used for every Rank 26 scheduler measurement record.</summary>
    private const string Prefix = "[Rank26Scheduler]";

    /// <summary>Schema version for Rank 26 scheduler measurement records.</summary>
    private const int Schema = 1;

    /// <summary>Process-local source for monotonically increasing scheduler pass identifiers.</summary>
    private static long NextPassId = 0;

    /// <summary>Snapshot of the latest maintained scheduler signal.</summary>
    internal readonly record struct SignalSnapshot(long Sequence, long Timestamp, string Source);

    /// <summary>Measurements accumulated for one scheduler loop pass.</summary>
    internal sealed class PassAttempt
    {
        /// <summary>Unique process-local identifier for this pass.</summary>
        public long PassId;

        /// <summary>Normalized operator-supplied scenario.</summary>
        public string Scenario;

        /// <summary>Timestamp at which this pass started.</summary>
        public long StartTimestamp;

        /// <summary>Current-thread allocated bytes at pass start.</summary>
        public long StartAllocation;

        /// <summary>Wake source category observed at pass start.</summary>
        public string SignalSource;

        /// <summary>Number of coalesced maintained signals observed at pass start.</summary>
        public long SignalCount;

        /// <summary>Age of the latest maintained signal at pass start, or -1 when absent.</summary>
        public long SignalAgeUs;

        /// <summary>Timestamp of a maintained signal observed by this pass, or zero when absent.</summary>
        public long SignalTimestamp;

        /// <summary>Pending request count observed at pass start.</summary>
        public int PendingCount;

        /// <summary>T2I backend count observed at pass start.</summary>
        public int BackendCount;

        /// <summary>Model pressure count observed at pass start.</summary>
        public int PressureCount;

        /// <summary>Total pressure membership observed at pass start.</summary>
        public int PressureMembershipCount;

        /// <summary>Number of requests visited by this pass.</summary>
        public int Visits;

        /// <summary>Number of requests cancelled before search.</summary>
        public int Cancelled;

        /// <summary>Number of successful claims made by this pass.</summary>
        public int Claimed;

        /// <summary>Number of request-search failures observed by this pass.</summary>
        public int Failed;

        /// <summary>Number of visited requests left waiting by this pass.</summary>
        public int Waiting;

        /// <summary>Total matcher invocations in this pass.</summary>
        public int MatcherCalls;

        /// <summary>Total matcher time in this pass.</summary>
        public long MatcherUs;

        /// <summary>Total pressure-selection time in this pass.</summary>
        public long PressureUs;

        /// <summary>Timestamp of the first successful claim, or zero when absent.</summary>
        public long FirstClaimTimestamp;

        /// <summary>Timestamp at which active scheduler processing completed, or zero until the pass waits or finishes.</summary>
        public long ActiveEndTimestamp;

        /// <summary>Records a cancellation before request-search work.</summary>
        public void RecordCancelled()
        {
            Cancelled++;
        }

        /// <summary>Records completed request-search aggregates for this pass.</summary>
        public void RecordTryFind(TryFindAttempt attempt, string outcome)
        {
            Visits++;
            MatcherCalls += attempt.MatcherCalls;
            MatcherUs += attempt.MatcherUs;
            PressureUs += attempt.PressureUs;
            if (outcome == "failed")
            {
                Failed++;
            }
            else if (!outcome.StartsWith("claim_"))
            {
                Waiting++;
            }
        }

        /// <summary>Records the first existing successful claim in this pass.</summary>
        public void NotifyClaim()
        {
            Claimed++;
            if (FirstClaimTimestamp == 0)
            {
                FirstClaimTimestamp = Stopwatch.GetTimestamp();
            }
        }
    }

    /// <summary>Measurements accumulated for one request backend search.</summary>
    internal sealed class TryFindAttempt
    {
        /// <summary>Containing pass, when the search came from the scheduler loop.</summary>
        public PassAttempt Pass;

        /// <summary>One-based request ordinal within the containing pass.</summary>
        public int Ordinal;

        /// <summary>Whether the request specifies a model.</summary>
        public bool HasModel;

        /// <summary>Whether the request supplies a backend matcher.</summary>
        public bool HasFilter;

        /// <summary>Timestamp at which this search started.</summary>
        public long StartTimestamp;

        /// <summary>Current-thread allocated bytes at search start.</summary>
        public long StartAllocation;

        /// <summary>Number of extension-gate predicate invocations.</summary>
        public int GateCalls;

        /// <summary>Number of request matcher predicate invocations.</summary>
        public int MatcherCalls;

        /// <summary>Backend count observed by this search.</summary>
        public int BackendCount;

        /// <summary>Possible backend count observed by this search.</summary>
        public int PossibleCount;

        /// <summary>Backend count accepted by the request matcher.</summary>
        public int MatcherAcceptedCount;

        /// <summary>Available backend count observed by this search.</summary>
        public int AvailableCount;

        /// <summary>Model pressure count observed by this search.</summary>
        public int PressureCount;

        /// <summary>Total pressure membership observed by this search.</summary>
        public int PressureMembershipCount;

        /// <summary>Extension-gate elapsed microseconds.</summary>
        public long GateUs;

        /// <summary>Backend snapshot/filter elapsed microseconds.</summary>
        public long SnapshotFilterUs;

        /// <summary>Request matcher elapsed microseconds.</summary>
        public long MatcherUs;

        /// <summary>Availability filter and sort elapsed microseconds.</summary>
        public long AvailabilitySortUs;

        /// <summary>Loaded-model search elapsed microseconds.</summary>
        public long ModelSearchUs;

        /// <summary>Pressure-selection elapsed microseconds.</summary>
        public long PressureUs;

        /// <summary>Records completed pressure-selection elapsed time.</summary>
        public void RecordPressure(long elapsedUs)
        {
            PressureUs += elapsedUs;
        }
    }

    /// <summary>Measurements accumulated for one pressure-selection attempt.</summary>
    internal sealed class PressureAttempt
    {
        /// <summary>Containing request search, when applicable.</summary>
        public TryFindAttempt RequestAttempt;

        /// <summary>Normalized operator-supplied scenario.</summary>
        public string Scenario;

        /// <summary>Containing pass identifier, or zero for direct callers.</summary>
        public long PassId;

        /// <summary>Timestamp at which this pressure selection started.</summary>
        public long StartTimestamp;

        /// <summary>Current-thread allocated bytes at pressure-selection start.</summary>
        public long StartAllocation;

        /// <summary>Possible backend count.</summary>
        public int PossibleCount;

        /// <summary>Available backend count.</summary>
        public int AvailableCount;

        /// <summary>Available model-loader count.</summary>
        public int LoaderCount;

        /// <summary>Pressure count.</summary>
        public int PressureCount;

        /// <summary>Total pressure membership count.</summary>
        public int PressureMembershipCount;

        /// <summary>Snapshot and sort elapsed microseconds.</summary>
        public long SnapshotSortUs;

        /// <summary>Compatibility matcher invocation count.</summary>
        public int CompatibilityCalls;

        /// <summary>Compatibility matcher elapsed microseconds.</summary>
        public long CompatibilityUs;

        /// <summary>Perfect-compatibility matcher invocation count.</summary>
        public int PerfectCalls;

        /// <summary>Perfect-compatibility matcher elapsed microseconds.</summary>
        public long PerfectUs;

        /// <summary>Post-selection filter elapsed microseconds.</summary>
        public long PostSelectionUs;
    }

    /// <summary>Returns whether Rank 26 scheduler measurement is enabled without allowing settings failures to escape.</summary>
    internal static bool IsEnabled()
    {
        try
        {
            return Program.ServerSettings?.Performance?.SchedulerMeasurementEnabled ?? false;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>Begins a scheduler pass measurement when enabled.</summary>
    internal static PassAttempt BeginPass(BackendHandler handler, ref long lastSignalSequence)
    {
        if (!IsEnabled())
        {
            return null;
        }
        try
        {
            SignalSnapshot signal = handler.CaptureSchedulerSignalSnapshot();
            long now = Stopwatch.GetTimestamp();
            bool hasSignal = signal.Sequence > lastSignalSequence;
            long signalCount = hasSignal ? signal.Sequence - Math.Max(lastSignalSequence, 0) : 0;
            string source = hasSignal ? signal.Source : lastSignalSequence < 0 ? "startup" : "timeout_or_external";
            long signalAgeUs = hasSignal && signal.Timestamp > 0 ? ToMicroseconds(signal.Timestamp, now) : -1;
            lastSignalSequence = signal.Sequence;
            int pressureCount = 0;
            int pressureMembershipCount = 0;
            foreach (BackendHandler.ModelRequestPressure pressure in handler.ModelRequests.Values)
            {
                pressureCount++;
                pressureMembershipCount += Math.Max(0, pressure.Count);
            }
            return new()
            {
                PassId = Interlocked.Increment(ref NextPassId),
                Scenario = GetScenario(),
                StartTimestamp = now,
                StartAllocation = GC.GetAllocatedBytesForCurrentThread(),
                SignalSource = NormalizeSignalSource(source),
                SignalCount = signalCount,
                SignalAgeUs = signalAgeUs,
                SignalTimestamp = hasSignal ? signal.Timestamp : 0,
                PendingCount = handler.T2IBackendRequests.Count,
                BackendCount = handler.EnumerateT2IBackends.Count(),
                PressureCount = pressureCount,
                PressureMembershipCount = pressureMembershipCount
            };
        }
        catch
        {
            return null;
        }
    }

    /// <summary>Begins a request-search measurement when enabled.</summary>
    internal static TryFindAttempt BeginTryFind(PassAttempt pass, int ordinal, bool hasModel, bool hasFilter)
    {
        if (pass is null && !IsEnabled())
        {
            return null;
        }
        try
        {
            return new()
            {
                Pass = pass,
                Ordinal = ordinal,
                HasModel = hasModel,
                HasFilter = hasFilter,
                StartTimestamp = Stopwatch.GetTimestamp(),
                StartAllocation = GC.GetAllocatedBytesForCurrentThread()
            };
        }
        catch
        {
            return null;
        }
    }

    /// <summary>Begins a pressure-selection measurement when enabled.</summary>
    internal static PressureAttempt BeginPressure(TryFindAttempt requestAttempt, int possibleCount, int availableCount)
    {
        if (requestAttempt is null && !IsEnabled())
        {
            return null;
        }
        try
        {
            return new()
            {
                RequestAttempt = requestAttempt,
                Scenario = GetScenario(),
                PassId = requestAttempt?.Pass?.PassId ?? 0,
                StartTimestamp = Stopwatch.GetTimestamp(),
                StartAllocation = GC.GetAllocatedBytesForCurrentThread(),
                PossibleCount = possibleCount,
                AvailableCount = availableCount
            };
        }
        catch
        {
            return null;
        }
    }

    /// <summary>Completes and emits one scheduler pass record.</summary>
    internal static void FinishPass(PassAttempt attempt, string outcome)
    {
        if (attempt is null)
        {
            return;
        }
        try
        {
            long endTimestamp = Stopwatch.GetTimestamp();
            long endAllocation = GC.GetAllocatedBytesForCurrentThread();
            long activeEndTimestamp = attempt.ActiveEndTimestamp == 0 ? endTimestamp : attempt.ActiveEndTimestamp;
            long signalToClaimUs = attempt.SignalTimestamp > 0 && attempt.FirstClaimTimestamp > 0 ? ToMicroseconds(attempt.SignalTimestamp, attempt.FirstClaimTimestamp) : -1;
            object record = new
            {
                schema = Schema,
                record = "pass",
                pass_id = attempt.PassId,
                scenario = attempt.Scenario,
                outcome,
                signal_source = attempt.SignalSource,
                signal_count = attempt.SignalCount,
                signal_age_us = attempt.SignalAgeUs,
                pending_count = attempt.PendingCount,
                backend_count = attempt.BackendCount,
                pressure_count = attempt.PressureCount,
                pressure_membership_count = attempt.PressureMembershipCount,
                visits = attempt.Visits,
                cancelled = attempt.Cancelled,
                claimed = attempt.Claimed,
                failed = attempt.Failed,
                waiting = attempt.Waiting,
                matcher_calls = attempt.MatcherCalls,
                matcher_us = attempt.MatcherUs,
                pressure_us = attempt.PressureUs,
                active_us = ToMicroseconds(attempt.StartTimestamp, activeEndTimestamp),
                wait_us = ToMicroseconds(activeEndTimestamp, endTimestamp),
                total_us = ToMicroseconds(attempt.StartTimestamp, endTimestamp),
                allocation_bytes = endAllocation - attempt.StartAllocation,
                signal_to_first_claim_us = signalToClaimUs
            };
            Emit(record);
        }
        catch
        {
        }
    }

    /// <summary>Marks the end of active scheduler work before the existing event wait.</summary>
    internal static void MarkPassActiveComplete(PassAttempt attempt)
    {
        if (attempt is null)
        {
            return;
        }
        try
        {
            attempt.ActiveEndTimestamp = Stopwatch.GetTimestamp();
        }
        catch
        {
        }
    }

    /// <summary>Completes and emits one request-search record.</summary>
    internal static void FinishTryFind(TryFindAttempt attempt, string outcome)
    {
        if (attempt is null)
        {
            return;
        }
        try
        {
            long endTimestamp = Stopwatch.GetTimestamp();
            long endAllocation = GC.GetAllocatedBytesForCurrentThread();
            attempt.Pass?.RecordTryFind(attempt, outcome);
            object record = new
            {
                schema = Schema,
                record = "try_find",
                pass_id = attempt.Pass?.PassId ?? 0,
                scenario = attempt.Pass?.Scenario ?? GetScenario(),
                outcome,
                ordinal = attempt.Ordinal,
                has_model = attempt.HasModel,
                has_filter = attempt.HasFilter,
                backend_count = attempt.BackendCount,
                possible_count = attempt.PossibleCount,
                matcher_accepted_count = attempt.MatcherAcceptedCount,
                available_count = attempt.AvailableCount,
                pressure_count = attempt.PressureCount,
                pressure_membership_count = attempt.PressureMembershipCount,
                gate_calls = attempt.GateCalls,
                matcher_calls = attempt.MatcherCalls,
                gate_us = attempt.GateUs,
                snapshot_filter_us = attempt.SnapshotFilterUs,
                matcher_us = attempt.MatcherUs,
                availability_sort_us = attempt.AvailabilitySortUs,
                model_search_us = attempt.ModelSearchUs,
                pressure_us = attempt.PressureUs,
                total_us = ToMicroseconds(attempt.StartTimestamp, endTimestamp),
                allocation_bytes = endAllocation - attempt.StartAllocation
            };
            Emit(record);
        }
        catch
        {
        }
    }

    /// <summary>Completes and emits one pressure-selection record.</summary>
    internal static void FinishPressure(PressureAttempt attempt, string outcome)
    {
        if (attempt is null)
        {
            return;
        }
        try
        {
            long endTimestamp = Stopwatch.GetTimestamp();
            long endAllocation = GC.GetAllocatedBytesForCurrentThread();
            long totalUs = ToMicroseconds(attempt.StartTimestamp, endTimestamp);
            attempt.RequestAttempt?.RecordPressure(totalUs);
            object record = new
            {
                schema = Schema,
                record = "pressure",
                pass_id = attempt.PassId,
                scenario = attempt.Scenario,
                outcome,
                possible_count = attempt.PossibleCount,
                available_count = attempt.AvailableCount,
                loader_count = attempt.LoaderCount,
                pressure_count = attempt.PressureCount,
                pressure_membership_count = attempt.PressureMembershipCount,
                snapshot_sort_us = attempt.SnapshotSortUs,
                compatibility_calls = attempt.CompatibilityCalls,
                compatibility_us = attempt.CompatibilityUs,
                perfect_calls = attempt.PerfectCalls,
                perfect_us = attempt.PerfectUs,
                post_selection_us = attempt.PostSelectionUs,
                total_us = totalUs,
                allocation_bytes = endAllocation - attempt.StartAllocation
            };
            Emit(record);
        }
        catch
        {
        }
    }

    /// <summary>Normalizes an operator-supplied scenario to the bounded safe schema category.</summary>
    private static string GetScenario()
    {
        try
        {
            string input = Program.ServerSettings?.Performance?.SchedulerMeasurementScenario ?? "";
            StringBuilder output = new(Math.Min(input.Length, 96));
            bool previousReplacement = false;
            foreach (char c in input)
            {
                bool allowed = c is >= 'a' and <= 'z' or >= '0' and <= '9' or '/' or '_' or '-';
                if (allowed)
                {
                    output.Append(c);
                    previousReplacement = false;
                }
                else if (!previousReplacement)
                {
                    output.Append('_');
                    previousReplacement = true;
                }
                if (output.Length >= 96)
                {
                    break;
                }
            }
            return output.Length == 0 ? "unspecified" : output.ToString();
        }
        catch
        {
            return "unspecified";
        }
    }

    /// <summary>Normalizes a maintained scheduler signal source to the bounded schema catalog.</summary>
    internal static string NormalizeSignalSource(string source)
    {
        return source is "shutdown" or "request" or "release" ? source : "timeout_or_external";
    }

    /// <summary>Converts a monotonic timestamp range to integer microseconds.</summary>
    internal static long ToMicroseconds(long startTimestamp, long endTimestamp)
    {
        long ticks = Math.Max(0, endTimestamp - startTimestamp);
        return (ticks / Stopwatch.Frequency) * 1_000_000 + ((ticks % Stopwatch.Frequency) * 1_000_000 / Stopwatch.Frequency);
    }

    /// <summary>Serializes and emits a compact measurement record without allowing logger failures to escape.</summary>
    private static void Emit(object record)
    {
        try
        {
            string json = JsonConvert.SerializeObject(record, Formatting.None);
            Logs.Info($"{Prefix}{json}");
        }
        catch
        {
        }
    }
}
