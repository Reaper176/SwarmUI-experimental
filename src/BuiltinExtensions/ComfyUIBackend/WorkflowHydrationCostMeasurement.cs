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
            long refreshAgeUs = 0;
            if (boundedKind == "snapshot")
            {
                long inventoryTimestamp = Interlocked.Exchange(ref LatestInventoryTimestamp, 0);
                if (inventoryTimestamp > 0)
                {
                    refreshAgeUs = ToMicroseconds(inventoryTimestamp, Stopwatch.GetTimestamp());
                }
            }
            Operation operation = new()
            {
                Parent = GetActiveOperation(),
                Id = Interlocked.Increment(ref NextRecordId),
                Kind = boundedKind,
                Scenario = GetScenario(),
                StartTimestamp = Stopwatch.GetTimestamp(),
                StartAllocation = GC.GetAllocatedBytesForCurrentThread(),
                RefreshAgeUs = refreshAgeUs
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
        lock (operation)
        {
            operation.EntryCount = Math.Max(0, entries);
            operation.EntryNullCount = Math.Max(0, nullEntries);
        }
    }

    /// <summary>Records inventory counts without retaining any names.</summary>
    internal static void NoteInventory(Operation operation, long files, long examples, long copiedExamples, long publishedNullRecords)
    {
        if (operation is null)
        {
            return;
        }
        lock (operation)
        {
            operation.InventoryFiles = Math.Max(0, files);
            operation.ExampleFiles = Math.Max(0, examples);
            operation.CopiedExamples = Math.Max(0, copiedExamples);
            operation.PublishedNullRecords = Math.Max(0, publishedNullRecords);
        }
    }

    /// <summary>Notes one already hydrated record.</summary>
    internal static void NoteCached()
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
                StageTimestamp = timestamp
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
        long now = Stopwatch.GetTimestamp();
        attempt.ReadUs = ToMicroseconds(attempt.StageTimestamp, now);
        attempt.StageTimestamp = now;
        attempt.SourceBytes = Math.Max(0, sourceBytes);
    }

    /// <summary>Completes the parse/extract stage.</summary>
    internal static void CompleteParse(HydrationAttempt attempt, long retainedCharacters)
    {
        if (attempt is null)
        {
            return;
        }
        long now = Stopwatch.GetTimestamp();
        attempt.ParseUs = ToMicroseconds(attempt.StageTimestamp, now);
        attempt.StageTimestamp = now;
        attempt.RetainedCharacters = Math.Max(0, retainedCharacters);
    }

    /// <summary>Completes the record construction/publication stage and successful attempt.</summary>
    internal static void CompleteSuccess(HydrationAttempt attempt)
    {
        if (attempt is null || attempt.IsComplete)
        {
            return;
        }
        long now = Stopwatch.GetTimestamp();
        attempt.PublicationUs = ToMicroseconds(attempt.StageTimestamp, now);
        CompleteHydration(attempt, "completed", now);
    }

    /// <summary>Completes a missing-file hydration attempt.</summary>
    internal static void CompleteMissing(HydrationAttempt attempt)
    {
        if (attempt is null || attempt.IsComplete)
        {
            return;
        }
        long now = Stopwatch.GetTimestamp();
        attempt.ReadUs = ToMicroseconds(attempt.StageTimestamp, now);
        CompleteHydration(attempt, "missing", now);
    }

    /// <summary>Completes an invalid-file hydration attempt.</summary>
    internal static void CompleteInvalid(HydrationAttempt attempt)
    {
        if (attempt is null || attempt.IsComplete)
        {
            return;
        }
        CompleteHydration(attempt, "invalid", Stopwatch.GetTimestamp());
    }

    /// <summary>Completes and emits one hydration record.</summary>
    private static void CompleteHydration(HydrationAttempt attempt, string outcome, long endTimestamp)
    {
        try
        {
            attempt.IsComplete = true;
            long endAllocation = GC.GetAllocatedBytesForCurrentThread();
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
                }
            }
            Emit(new
            {
                schema = Schema,
                record = "hydration",
                id = Interlocked.Increment(ref NextRecordId),
                operation_id = owner?.Id ?? 0,
                scenario = GetScenario(),
                source = "file",
                outcome,
                example = attempt.IsExample,
                source_bytes = attempt.SourceBytes,
                retained_characters = attempt.RetainedCharacters,
                read_us = attempt.ReadUs,
                parse_us = attempt.ParseUs,
                publication_us = attempt.PublicationUs,
                total_us = ToMicroseconds(attempt.StartTimestamp, endTimestamp),
                allocation_bytes = AllocationDelta(attempt.StartAllocation, endAllocation)
            });
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
            lock (operation)
            {
                operation.IsComplete = true;
            }
            if (operation.Kind == "inventory" && outcome == "completed")
            {
                Interlocked.Exchange(ref LatestInventoryTimestamp, endTimestamp);
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
