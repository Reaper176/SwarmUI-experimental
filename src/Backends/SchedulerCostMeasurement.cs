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

    /// <summary>Immutable bounded data for one maintained scheduler signal.</summary>
    internal sealed class SignalSample
    {
        /// <summary>Monotonic timestamp at which this maintained signal was recorded.</summary>
        public long Timestamp { get; }

        /// <summary>Monotonic process-local sequence of this maintained signal.</summary>
        public long Sequence { get; }

        /// <summary>Bounded category for this maintained signal.</summary>
        public string Source { get; }

        /// <summary>Constructs an immutable maintained scheduler signal sample.</summary>
        public SignalSample(long sequence, long timestamp, string source)
        {
            Sequence = sequence;
            Timestamp = timestamp;
            Source = source;
        }
    }

    /// <summary>Bounded latest maintained scheduler signal read from the fixed signal ring.</summary>
    internal readonly record struct SignalSnapshot(long Sequence, long Timestamp, string Source, bool IsComplete);

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

        /// <summary>Bounded latest signal source separately captured only while finishing a terminal shutdown pass.</summary>
        public string TerminalSignalSource = "none";

        /// <summary>Coalesced maintained signal count separately captured only while finishing a terminal shutdown pass.</summary>
        public long TerminalSignalCount;

        /// <summary>Age of the latest separately captured terminal signal, or -1 when absent.</summary>
        public long TerminalSignalAgeUs = -1;

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

        /// <summary>Timestamp immediately before the existing scheduler event wait starts.</summary>
        public long WaitStartTimestamp;

        /// <summary>Timestamp immediately after the existing scheduler event wait returns or throws.</summary>
        public long WaitEndTimestamp;

        /// <summary>Bounded source category observed while this pass waited, when any.</summary>
        public string WakeSource = "none";

        /// <summary>Timestamp at which this pass endpoint was captured.</summary>
        public long EndTimestamp;

        /// <summary>Current-thread allocated bytes captured at this pass endpoint.</summary>
        public long EndAllocation;

        /// <summary>Bounded final pass outcome captured at this pass endpoint.</summary>
        public string Outcome;

        /// <summary>Completed request-search attempts whose record emission is deferred until this pass endpoint is captured.</summary>
        public List<TryFindAttempt> TryFindAttempts = [];

        /// <summary>References to requests classified as waiting during this pass, used only for exact timeout reclassification.</summary>
        public HashSet<BackendHandler.T2IBackendRequest> WaitingRequests = [];

        /// <summary>Records a cancellation before request-search work.</summary>
        public void RecordCancelled()
        {
            Cancelled++;
        }

        /// <summary>Records completed request-search aggregates for this pass.</summary>
        public void RecordRequestState(BackendHandler.T2IBackendRequest request, bool claimed, bool failed)
        {
            Visits++;
            if (claimed)
            {
                Claimed++;
            }
            else if (failed)
            {
                Failed++;
            }
            else
            {
                Waiting++;
                WaitingRequests.Add(request);
            }
        }

        /// <summary>Reclassifies a previously waiting request when the existing pass-level timeout assigns its failure.</summary>
        public void RecordWaitingFailure(BackendHandler.T2IBackendRequest request)
        {
            if (WaitingRequests.Remove(request))
            {
                Waiting--;
                Failed++;
            }
        }

        /// <summary>Records the first existing successful claim in this pass.</summary>
        public void NotifyClaim()
        {
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

        /// <summary>Bounded pressure-selection outcome propagated to this request search.</summary>
        public string PressureOutcome;

        /// <summary>Pressure compatibility matcher invocations accumulated for pass-level aggregation only.</summary>
        public int PressureMatcherCalls;

        /// <summary>Pressure compatibility matcher elapsed microseconds accumulated for pass-level aggregation only.</summary>
        public long PressureMatcherUs;

        /// <summary>Timestamp at which this request-search endpoint was captured.</summary>
        public long EndTimestamp;

        /// <summary>Current-thread allocated bytes captured at this request-search endpoint.</summary>
        public long EndAllocation;

        /// <summary>Bounded final request-search outcome captured at this endpoint.</summary>
        public string Outcome;

        /// <summary>Completed pressure attempts whose record emission is deferred until this request endpoint is captured.</summary>
        public List<PressureAttempt> PressureAttempts = [];

        /// <summary>Records completed pressure-selection elapsed time.</summary>
        public void RecordPressure(long elapsedUs, string outcome, int matcherCalls, long matcherUs)
        {
            PressureUs += elapsedUs;
            PressureOutcome = outcome;
            PressureMatcherCalls += matcherCalls;
            PressureMatcherUs += matcherUs;
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

        /// <summary>Timestamp at which this pressure-selection endpoint was captured.</summary>
        public long EndTimestamp;

        /// <summary>Current-thread allocated bytes captured at this pressure-selection endpoint.</summary>
        public long EndAllocation;

        /// <summary>Bounded final pressure-selection outcome captured at this endpoint.</summary>
        public string Outcome;
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
    internal static PassAttempt BeginPass(BackendHandler handler, ref long lastObservedSignalSequence, ref bool hasMeasuredPass)
    {
        if (!IsEnabled())
        {
            return null;
        }
        try
        {
            SignalSnapshot signal = handler.CaptureSchedulerSignalSample();
            long now = Stopwatch.GetTimestamp();
            bool firstPass = !hasMeasuredPass;
            bool hasSignal = signal.IsComplete && signal.Sequence > lastObservedSignalSequence;
            long signalCount = hasSignal ? signal.Sequence - lastObservedSignalSequence : 0;
            string source = hasSignal ? signal.Source : firstPass && signal.Sequence <= lastObservedSignalSequence ? "startup" : "timeout_or_external";
            long signalAgeUs = hasSignal && signal.Timestamp > 0 ? ToMicroseconds(signal.Timestamp, now) : -1;
            if (hasSignal)
            {
                lastObservedSignalSequence = signal.Sequence;
            }
            int pressureCount = 0;
            int pressureMembershipCount = 0;
            foreach (BackendHandler.ModelRequestPressure pressure in handler.ModelRequests.Values)
            {
                pressureCount++;
                pressureMembershipCount += Math.Max(0, pressure.Count);
            }
            PassAttempt attempt = new()
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
            hasMeasuredPass = true;
            return attempt;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>Captures a bounded terminal signal snapshot without overwriting the pass-start wake metadata.</summary>
    internal static void CaptureTerminalSignal(BackendHandler handler, PassAttempt attempt, ref long lastObservedSignalSequence)
    {
        if (attempt is null)
        {
            return;
        }
        try
        {
            SignalSnapshot signal = handler.CaptureSchedulerSignalSample();
            if (!signal.IsComplete || signal.Sequence <= lastObservedSignalSequence)
            {
                return;
            }
            long now = Stopwatch.GetTimestamp();
            attempt.TerminalSignalSource = NormalizeSignalSource(signal.Source);
            attempt.TerminalSignalCount = signal.Sequence - lastObservedSignalSequence;
            attempt.TerminalSignalAgeUs = ToMicroseconds(signal.Timestamp, now);
            lastObservedSignalSequence = signal.Sequence;
        }
        catch
        {
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
            TryFindAttempt attempt = new()
            {
                Pass = pass,
                Ordinal = ordinal,
                HasModel = hasModel,
                HasFilter = hasFilter,
                StartTimestamp = Stopwatch.GetTimestamp(),
                StartAllocation = GC.GetAllocatedBytesForCurrentThread()
            };
            pass?.TryFindAttempts.Add(attempt);
            return attempt;
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
            PressureAttempt attempt = new()
            {
                RequestAttempt = requestAttempt,
                Scenario = GetScenario(),
                PassId = requestAttempt?.Pass?.PassId ?? 0,
                StartTimestamp = Stopwatch.GetTimestamp(),
                StartAllocation = GC.GetAllocatedBytesForCurrentThread(),
                PossibleCount = possibleCount,
                AvailableCount = availableCount
            };
            requestAttempt?.PressureAttempts.Add(attempt);
            return attempt;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>Captures a scheduler pass endpoint before any nested record construction or emission.</summary>
    internal static void CompletePass(PassAttempt attempt, string outcome)
    {
        if (attempt is null)
        {
            return;
        }
        if (attempt.EndTimestamp != 0)
        {
            return;
        }
        try
        {
            attempt.EndTimestamp = Stopwatch.GetTimestamp();
            attempt.EndAllocation = GC.GetAllocatedBytesForCurrentThread();
            attempt.Outcome = outcome;
            foreach (TryFindAttempt tryFindAttempt in attempt.TryFindAttempts)
            {
                attempt.MatcherCalls += tryFindAttempt.MatcherCalls + tryFindAttempt.PressureMatcherCalls;
                attempt.MatcherUs += tryFindAttempt.MatcherUs + tryFindAttempt.PressureMatcherUs;
                attempt.PressureUs += tryFindAttempt.PressureUs;
            }
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

    /// <summary>Marks the exact boundaries of the existing scheduler event wait.</summary>
    internal static void MarkPassWaitStart(PassAttempt attempt)
    {
        if (attempt is null)
        {
            return;
        }
        try
        {
            attempt.WaitStartTimestamp = Stopwatch.GetTimestamp();
        }
        catch
        {
        }
    }

    /// <summary>Marks the exact boundaries of the existing scheduler event wait.</summary>
    internal static void MarkPassWaitEnd(PassAttempt attempt)
    {
        if (attempt is null)
        {
            return;
        }
        try
        {
            attempt.WaitEndTimestamp = Stopwatch.GetTimestamp();
        }
        catch
        {
        }
    }

    /// <summary>Captures a request-search endpoint before any nested record construction or emission.</summary>
    internal static void CompleteTryFind(TryFindAttempt attempt, string outcome)
    {
        if (attempt is null)
        {
            return;
        }
        try
        {
            attempt.EndTimestamp = Stopwatch.GetTimestamp();
            attempt.EndAllocation = GC.GetAllocatedBytesForCurrentThread();
            attempt.Outcome = outcome;
            if (attempt.Pass is null)
            {
                EmitCompletedTryFind(attempt);
            }
        }
        catch
        {
        }
    }

    /// <summary>Completes a pressure-selection record, emitting only when it has no enclosing request search.</summary>
    internal static void FinishPressure(PressureAttempt attempt, string outcome)
    {
        if (attempt is null)
        {
            return;
        }
        try
        {
            attempt.EndTimestamp = Stopwatch.GetTimestamp();
            attempt.EndAllocation = GC.GetAllocatedBytesForCurrentThread();
            attempt.Outcome = outcome;
            int matcherCalls = attempt.CompatibilityCalls + attempt.PerfectCalls;
            long matcherUs = attempt.CompatibilityUs + attempt.PerfectUs;
            attempt.RequestAttempt?.RecordPressure(ToMicroseconds(attempt.StartTimestamp, attempt.EndTimestamp), outcome, matcherCalls, matcherUs);
            if (attempt.RequestAttempt is null)
            {
                EmitCompletedPressure(attempt);
            }
        }
        catch
        {
        }
    }

    /// <summary>Emits completed nested records followed by a completed pass record after all enclosing endpoints are captured.</summary>
    internal static void EmitCompletedPass(PassAttempt attempt)
    {
        if (attempt is null)
        {
            return;
        }
        try
        {
            foreach (TryFindAttempt tryFindAttempt in attempt.TryFindAttempts)
            {
                EmitCompletedTryFind(tryFindAttempt);
            }
            long activeEndTimestamp = attempt.ActiveEndTimestamp == 0 ? attempt.EndTimestamp : attempt.ActiveEndTimestamp;
            long waitUs = attempt.WaitStartTimestamp > 0 && attempt.WaitEndTimestamp > 0 ? ToMicroseconds(attempt.WaitStartTimestamp, attempt.WaitEndTimestamp) : 0;
            Dictionary<string, object> record = new()
            {
                ["schema"] = Schema,
                ["record"] = "pass",
                ["pass_id"] = attempt.PassId,
                ["scenario"] = attempt.Scenario,
                ["outcome"] = attempt.Outcome,
                ["signal_source"] = attempt.SignalSource,
                ["wake_source"] = attempt.WakeSource,
                ["signal_count"] = attempt.SignalCount,
                ["signal_age_us"] = attempt.SignalAgeUs,
                ["pending_count"] = attempt.PendingCount,
                ["backend_count"] = attempt.BackendCount,
                ["pressure_count"] = attempt.PressureCount,
                ["pressure_membership_count"] = attempt.PressureMembershipCount,
                ["visits"] = attempt.Visits,
                ["cancelled"] = attempt.Cancelled,
                ["claimed"] = attempt.Claimed,
                ["failed"] = attempt.Failed,
                ["waiting"] = attempt.Waiting,
                ["matcher_calls"] = attempt.MatcherCalls,
                ["matcher_us"] = attempt.MatcherUs,
                ["pressure_us"] = attempt.PressureUs,
                ["active_us"] = ToMicroseconds(attempt.StartTimestamp, activeEndTimestamp),
                ["wait_us"] = waitUs,
                ["total_us"] = ToMicroseconds(attempt.StartTimestamp, attempt.EndTimestamp),
                ["allocation_bytes"] = attempt.EndAllocation - attempt.StartAllocation
            };
            if (attempt.SignalTimestamp > 0 && attempt.FirstClaimTimestamp >= attempt.SignalTimestamp)
            {
                record["signal_to_first_claim_us"] = ToMicroseconds(attempt.SignalTimestamp, attempt.FirstClaimTimestamp);
            }
            if (attempt.TerminalSignalCount > 0)
            {
                record["terminal_signal_source"] = attempt.TerminalSignalSource;
                record["terminal_signal_count"] = attempt.TerminalSignalCount;
                record["terminal_signal_age_us"] = attempt.TerminalSignalAgeUs;
            }
            Emit(record);
        }
        catch
        {
        }
    }

    /// <summary>Emits one completed request-search record after its endpoint is captured.</summary>
    private static void EmitCompletedTryFind(TryFindAttempt attempt)
    {
        try
        {
            foreach (PressureAttempt pressureAttempt in attempt.PressureAttempts)
            {
                EmitCompletedPressure(pressureAttempt);
            }
            object record = new
            {
                schema = Schema,
                record = "try_find",
                pass_id = attempt.Pass?.PassId ?? 0,
                scenario = attempt.Pass?.Scenario ?? GetScenario(),
                outcome = attempt.Outcome,
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
                total_us = ToMicroseconds(attempt.StartTimestamp, attempt.EndTimestamp),
                allocation_bytes = attempt.EndAllocation - attempt.StartAllocation
            };
            Emit(record);
        }
        catch
        {
        }
    }

    /// <summary>Emits one completed pressure-selection record after its endpoint is captured.</summary>
    private static void EmitCompletedPressure(PressureAttempt attempt)
    {
        try
        {
            object record = new
            {
                schema = Schema,
                record = "pressure",
                pass_id = attempt.PassId,
                scenario = attempt.Scenario,
                outcome = attempt.Outcome,
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
                total_us = ToMicroseconds(attempt.StartTimestamp, attempt.EndTimestamp),
                allocation_bytes = attempt.EndAllocation - attempt.StartAllocation
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
        return source is "startup" or "shutdown" or "request" or "release" ? source : "timeout_or_external";
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
