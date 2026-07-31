using Newtonsoft.Json;
using SwarmUI.Core;
using SwarmUI.Utils;
using System.Diagnostics;
using System.Threading;

namespace SwarmUI.Builtin_ComfyUIBackend;

/// <summary>Temporary, opt-in Rank 29 cold workflow hydration cost recorder.</summary>
internal static class WorkflowHydrationCostMeasurement
{
    /// <summary>Prefix used for every Rank 29 measurement record.</summary>
    private const string Prefix = "[Rank29WorkflowHydration]";

    /// <summary>Schema version for Rank 29 measurement records.</summary>
    private const int Schema = 1;

    /// <summary>Maximum number of scenario characters inspected.</summary>
    private const int ScenarioInputCharacterLimit = 384;

    /// <summary>Maximum number of normalized scenario characters emitted.</summary>
    private const int ScenarioOutputCharacterLimit = 96;

    /// <summary>Process-local source for record identifiers.</summary>
    private static long NextRecordId;

    /// <summary>Timestamp at which the latest measured inventory completed.</summary>
    private static long LatestInventoryTimestamp;

    /// <summary>Current measured store operation for this execution context.</summary>
    private static readonly AsyncLocal<Operation> CurrentOperation = new();

    /// <summary>One measured inventory, snapshot, or lookup operation.</summary>
    internal sealed class Operation
    {
        /// <summary>Parent operation, if an unexpected nested caller exists.</summary>
        internal Operation Parent;

        /// <summary>Monotonic process-local operation identifier.</summary>
        internal long Id;

        /// <summary>Bounded operation category.</summary>
        internal string Kind;

        /// <summary>Bounded operator-supplied scenario.</summary>
        internal string Scenario;

        /// <summary>Operation start timestamp before lock acquisition.</summary>
        internal long StartTimestamp;

        /// <summary>Operation start current-thread allocation.</summary>
        internal long StartAllocation;

        /// <summary>Microseconds since the latest measured inventory, if consumed by this snapshot.</summary>
        internal long RefreshAgeUs;

        /// <summary>Dictionary entry count observed after lock acquisition.</summary>
        internal long EntryCount;

        /// <summary>Null record count observed after lock acquisition.</summary>
        internal long EntryNullCount;

        /// <summary>Dictionary entry count observed immediately before releasing the store lock.</summary>
        internal long ExitCount;

        /// <summary>Null record count observed immediately before releasing the store lock.</summary>
        internal long ExitNullCount;

        /// <summary>Inventory-completion timestamp observed by a snapshot under the store lock.</summary>
        internal long ObservedInventoryTimestamp;

        /// <summary>Discovered custom JSON files for inventory operations.</summary>
        internal long InventoryFiles;

        /// <summary>Discovered bundled example JSON files.</summary>
        internal long ExampleFiles;

        /// <summary>Bundled example files copied into the custom-workflow root.</summary>
        internal long CopiedExamples;

        /// <summary>Null records published by inventory.</summary>
        internal long PublishedNullRecords;

        /// <summary>Records hydrated from files.</summary>
        internal long HydratedRecords;

        /// <summary>Already-cached records observed.</summary>
        internal long CachedRecords;

        /// <summary>Missing records removed from the inventory.</summary>
        internal long MissingRecords;

        /// <summary>Invalid records omitted after content-redacted logging.</summary>
        internal long InvalidRecords;

        /// <summary>Hydrated bundled example records.</summary>
        internal long ExampleRecords;

        /// <summary>Aggregate UTF-8 source bytes for hydrated files.</summary>
        internal long SourceBytes;

        /// <summary>Aggregate retained record-field characters.</summary>
        internal long RetainedCharacters;

        /// <summary>Whether the operation has reached an endpoint.</summary>
        internal bool IsComplete;

        /// <summary>Completed hydration attempts deferred until the parent operation leaves the store lock.</summary>
        internal readonly List<HydrationAttempt> CompletedHydrations = [];
    }

    /// <summary>One measured null-record hydration attempt.</summary>
    internal sealed class HydrationAttempt
    {
        /// <summary>Owning public store operation, if any.</summary>
        internal Operation Owner;

        /// <summary>Whether the record belongs to the bundled example namespace.</summary>
        internal bool IsExample;

        /// <summary>Hydration start timestamp.</summary>
        internal long StartTimestamp;

        /// <summary>Hydration start current-thread allocation.</summary>
        internal long StartAllocation;

        /// <summary>Current stage start timestamp.</summary>
        internal long StageTimestamp;

        /// <summary>Active timing stage.</summary>
        internal string Stage;

        /// <summary>File existence and read elapsed microseconds.</summary>
        internal long ReadUs;

        /// <summary>Parse and field extraction elapsed microseconds.</summary>
        internal long ParseUs;

        /// <summary>Record construction and publication elapsed microseconds.</summary>
        internal long PublicationUs;

        /// <summary>UTF-8 bytes represented by the read string.</summary>
        internal long SourceBytes;

        /// <summary>Characters retained by the complete record fields.</summary>
        internal long RetainedCharacters;

        /// <summary>Whether the attempt has reached an endpoint.</summary>
        internal bool IsComplete;

        /// <summary>Bounded hydration outcome.</summary>
        internal string Outcome;

        /// <summary>Hydration endpoint timestamp.</summary>
        internal long EndTimestamp;

        /// <summary>Hydration endpoint current-thread allocation.</summary>
        internal long EndAllocation;
    }

    /// <summary>Returns whether Rank 29 measurement is enabled without allowing settings failures to escape.</summary>
    internal static bool IsEnabled()
    {
        try
        {
            return Program.ServerSettings?.Performance?.WorkflowHydrationMeasurementEnabled ?? false;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>Begins one public store operation before lock acquisition.</summary>
    internal static Operation BeginOperation(string kind)
    {
        if (!IsEnabled())
        {
            return null;
        }
        try
        {
            string boundedKind = kind is "inventory" or "snapshot" or "lookup" ? kind : "other";
            Operation operation = new()
            {
                Parent = GetActiveOperation(),
                Id = Interlocked.Increment(ref NextRecordId),
                Kind = boundedKind,
                Scenario = GetScenario(),
                StartTimestamp = Stopwatch.GetTimestamp(),
                StartAllocation = GC.GetAllocatedBytesForCurrentThread()
            };
            CurrentOperation.Value = operation;
            return operation;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>Records the dictionary state observed after lock acquisition.</summary>
    internal static void NoteEntry(Operation operation, long entries, long nullEntries)
    {
        if (operation is null)
        {
            return;
        }
        try
        {
            lock (operation)
            {
                operation.EntryCount = Math.Max(0, entries);
                operation.EntryNullCount = Math.Max(0, nullEntries);
                if (operation.Kind == "snapshot")
                {
                    operation.ObservedInventoryTimestamp = Volatile.Read(ref LatestInventoryTimestamp);
                }
            }
        }
        catch
        {
        }
    }

    /// <summary>Records the dictionary state immediately before releasing the store lock.</summary>
    internal static void NoteExit(Operation operation, long entries, long nullEntries)
    {
        if (operation is null)
        {
            return;
        }
        try
        {
            lock (operation)
            {
                operation.ExitCount = Math.Max(0, entries);
                operation.ExitNullCount = Math.Max(0, nullEntries);
            }
        }
        catch
        {
        }
    }

    /// <summary>Consumes the observed inventory timestamp after a snapshot has completed successfully under the store lock.</summary>
    internal static void CommitSnapshotRefresh(Operation operation)
    {
        if (operation is null || operation.Kind != "snapshot" || operation.ObservedInventoryTimestamp <= 0)
        {
            return;
        }
        try
        {
            long observed = operation.ObservedInventoryTimestamp;
            if (Interlocked.CompareExchange(ref LatestInventoryTimestamp, 0, observed) == observed)
            {
                operation.RefreshAgeUs = ToMicroseconds(observed, Stopwatch.GetTimestamp());
            }
        }
        catch
        {
        }
    }

    /// <summary>Records inventory counts without retaining any names.</summary>
    internal static void NoteInventory(Operation operation, long files, long examples, long copiedExamples, long publishedNullRecords)
    {
        if (operation is null)
        {
            return;
        }
        try
        {
            lock (operation)
            {
                operation.InventoryFiles = Math.Max(0, files);
                operation.ExampleFiles = Math.Max(0, examples);
                operation.CopiedExamples = Math.Max(0, copiedExamples);
                operation.PublishedNullRecords = Math.Max(0, publishedNullRecords);
            }
        }
        catch
        {
        }
    }

    /// <summary>Notes one already hydrated record.</summary>
    internal static void NoteCached()
    {
        try
        {
            Operation operation = GetActiveOperation();
            if (operation is null)
            {
                return;
            }
            lock (operation)
            {
                if (!operation.IsComplete)
                {
                    operation.CachedRecords++;
                }
            }
        }
        catch
        {
        }
    }

    /// <summary>Begins one null-record hydration attempt.</summary>
    internal static HydrationAttempt BeginHydration(bool isExample)
    {
        try
        {
            long timestamp = Stopwatch.GetTimestamp();
            return new()
            {
                Owner = GetActiveOperation(),
                IsExample = isExample,
                StartTimestamp = timestamp,
                StartAllocation = GC.GetAllocatedBytesForCurrentThread(),
                StageTimestamp = timestamp,
                Stage = "read"
            };
        }
        catch
        {
            return null;
        }
    }

    /// <summary>Completes the existence/read stage.</summary>
    internal static void CompleteRead(HydrationAttempt attempt, long sourceBytes)
    {
        if (attempt is null)
        {
            return;
        }
        try
        {
            long now = Stopwatch.GetTimestamp();
            attempt.ReadUs = ToMicroseconds(attempt.StageTimestamp, now);
            attempt.StageTimestamp = now;
            attempt.Stage = "parse";
            attempt.SourceBytes = Math.Max(0, sourceBytes);
        }
        catch
        {
        }
    }

    /// <summary>Completes the parse/extract stage.</summary>
    internal static void CompleteParse(HydrationAttempt attempt, long retainedCharacters)
    {
        if (attempt is null)
        {
            return;
        }
        try
        {
            long now = Stopwatch.GetTimestamp();
            attempt.ParseUs = ToMicroseconds(attempt.StageTimestamp, now);
            attempt.StageTimestamp = now;
            attempt.Stage = "publication";
            attempt.RetainedCharacters = Math.Max(0, retainedCharacters);
        }
        catch
        {
        }
    }

    /// <summary>Completes the record construction/publication stage and successful attempt.</summary>
    internal static void CompleteSuccess(HydrationAttempt attempt)
    {
        if (attempt is null || attempt.IsComplete)
        {
            return;
        }
        try
        {
            long now = Stopwatch.GetTimestamp();
            attempt.PublicationUs = ToMicroseconds(attempt.StageTimestamp, now);
            CompleteHydration(attempt, "completed", now);
        }
        catch
        {
        }
    }

    /// <summary>Completes a missing-file hydration attempt.</summary>
    internal static void CompleteMissing(HydrationAttempt attempt)
    {
        if (attempt is null || attempt.IsComplete)
        {
            return;
        }
        try
        {
            long now = Stopwatch.GetTimestamp();
            attempt.ReadUs = ToMicroseconds(attempt.StageTimestamp, now);
            CompleteHydration(attempt, "missing", now);
        }
        catch
        {
        }
    }

    /// <summary>Completes an invalid-file hydration attempt.</summary>
    internal static void CompleteInvalid(HydrationAttempt attempt)
    {
        if (attempt is null || attempt.IsComplete)
        {
            return;
        }
        try
        {
            long now = Stopwatch.GetTimestamp();
            if (attempt.Stage == "read")
            {
                attempt.ReadUs += ToMicroseconds(attempt.StageTimestamp, now);
            }
            else if (attempt.Stage == "parse")
            {
                attempt.ParseUs += ToMicroseconds(attempt.StageTimestamp, now);
            }
            else if (attempt.Stage == "publication")
            {
                attempt.PublicationUs += ToMicroseconds(attempt.StageTimestamp, now);
            }
            CompleteHydration(attempt, "invalid", now);
        }
        catch
        {
        }
    }

    /// <summary>Completes and emits one hydration record.</summary>
    private static void CompleteHydration(HydrationAttempt attempt, string outcome, long endTimestamp)
    {
        try
        {
            attempt.IsComplete = true;
            long endAllocation = GC.GetAllocatedBytesForCurrentThread();
            attempt.Outcome = outcome;
            attempt.EndTimestamp = endTimestamp;
            attempt.EndAllocation = endAllocation;
            Operation owner = attempt.Owner;
            if (owner is not null)
            {
                lock (owner)
                {
                    if (!owner.IsComplete)
                    {
                        owner.SourceBytes += attempt.SourceBytes;
                        owner.RetainedCharacters += attempt.RetainedCharacters;
                        if (outcome == "completed")
                        {
                            owner.HydratedRecords++;
                            if (attempt.IsExample)
                            {
                                owner.ExampleRecords++;
                            }
                        }
                        else if (outcome == "missing")
                        {
                            owner.MissingRecords++;
                        }
                        else
                        {
                            owner.InvalidRecords++;
                        }
                    }
                    owner.CompletedHydrations.Add(attempt);
                }
            }
            if (owner is null)
            {
                EmitHydration(attempt);
            }
        }
        catch
        {
        }
    }

    /// <summary>Completes and emits one public store operation.</summary>
    internal static void CompleteOperation(Operation operation, string outcome)
    {
        if (operation is null || operation.IsComplete)
        {
            return;
        }
        try
        {
            long endTimestamp = Stopwatch.GetTimestamp();
            long endAllocation = GC.GetAllocatedBytesForCurrentThread();
            List<HydrationAttempt> completedHydrations;
            lock (operation)
            {
                operation.IsComplete = true;
                completedHydrations = [.. operation.CompletedHydrations];
            }
            if (operation.Kind == "inventory" && outcome == "completed")
            {
                Interlocked.Exchange(ref LatestInventoryTimestamp, endTimestamp);
            }
            foreach (HydrationAttempt hydration in completedHydrations)
            {
                EmitHydration(hydration);
            }
            Emit(new
            {
                schema = Schema,
                record = "operation",
                id = operation.Id,
                scenario = operation.Scenario,
                operation = operation.Kind,
                outcome = outcome is "completed" ? "completed" : "failed",
                entry_count = operation.EntryCount,
                entry_null_count = operation.EntryNullCount,
                exit_count = operation.ExitCount,
                exit_null_count = operation.ExitNullCount,
                inventory_files = operation.InventoryFiles,
                example_files = operation.ExampleFiles,
                copied_examples = operation.CopiedExamples,
                published_null_records = operation.PublishedNullRecords,
                hydrated_records = operation.HydratedRecords,
                cached_records = operation.CachedRecords,
                missing_records = operation.MissingRecords,
                invalid_records = operation.InvalidRecords,
                example_records = operation.ExampleRecords,
                source_bytes = operation.SourceBytes,
                retained_characters = operation.RetainedCharacters,
                refresh_age_us = operation.RefreshAgeUs,
                total_us = ToMicroseconds(operation.StartTimestamp, endTimestamp),
                allocation_bytes = AllocationDelta(operation.StartAllocation, endAllocation)
            });
        }
        catch
        {
        }
        finally
        {
            if (ReferenceEquals(CurrentOperation.Value, operation))
            {
                CurrentOperation.Value = operation.Parent;
            }
        }
    }

    /// <summary>Constructs and emits one completed hydration record after its parent operation has left the store lock.</summary>
    private static void EmitHydration(HydrationAttempt attempt)
    {
        Emit(new
        {
            schema = Schema,
            record = "hydration",
            id = Interlocked.Increment(ref NextRecordId),
            operation_id = attempt.Owner?.Id ?? 0,
            scenario = attempt.Owner?.Scenario ?? GetScenario(),
            source = "file",
            outcome = attempt.Outcome,
            example = attempt.IsExample,
            source_bytes = attempt.SourceBytes,
            retained_characters = attempt.RetainedCharacters,
            read_us = attempt.ReadUs,
            parse_us = attempt.ParseUs,
            publication_us = attempt.PublicationUs,
            total_us = ToMicroseconds(attempt.StartTimestamp, attempt.EndTimestamp),
            allocation_bytes = AllocationDelta(attempt.StartAllocation, attempt.EndAllocation)
        });
    }

    /// <summary>Returns the active noncompleted operation for this context.</summary>
    private static Operation GetActiveOperation()
    {
        Operation operation = CurrentOperation.Value;
        while (operation is not null && operation.IsComplete)
        {
            operation = operation.Parent;
        }
        if (!ReferenceEquals(CurrentOperation.Value, operation))
        {
            CurrentOperation.Value = operation;
        }
        return operation;
    }

    /// <summary>Returns a bounded privacy-safe scenario label.</summary>
    private static string GetScenario()
    {
        string raw;
        try
        {
            raw = Program.ServerSettings?.Performance?.WorkflowHydrationMeasurementScenario ?? "";
        }
        catch
        {
            raw = "";
        }
        if (raw.Length > ScenarioInputCharacterLimit)
        {
            raw = raw[..ScenarioInputCharacterLimit];
        }
        char[] normalized = new char[Math.Min(raw.Length, ScenarioOutputCharacterLimit)];
        for (int i = 0; i < normalized.Length; i++)
        {
            char character = char.ToLowerInvariant(raw[i]);
            normalized[i] = char.IsAsciiLetterOrDigit(character) || character is '/' or '_' or '-' ? character : '_';
        }
        return normalized.Length == 0 ? "unspecified" : new string(normalized);
    }

    /// <summary>Converts stopwatch ticks to nonnegative whole microseconds.</summary>
    internal static long ToMicroseconds(long start, long end)
    {
        if (end <= start)
        {
            return 0;
        }
        return (long)((end - start) * 1_000_000d / Stopwatch.Frequency);
    }

    /// <summary>Returns a nonnegative current-thread allocation delta.</summary>
    private static long AllocationDelta(long start, long end)
    {
        return Math.Max(0, end - start);
    }

    /// <summary>Emits one compact record without allowing diagnostic failures to escape.</summary>
    private static void Emit(object record)
    {
        try
        {
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
}
