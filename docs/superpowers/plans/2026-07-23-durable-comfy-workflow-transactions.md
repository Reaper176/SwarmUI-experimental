# Durable Comfy Workflow Transactions Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make stored Comfy workflow save, overwrite, rename, and deletion durable before publication, with deterministic recovery after interrupted transactions and no external API or file-format change.

**Architecture:** A dedicated static `ComfyWorkflowStore` owns one process-wide lock, existing lazy reads/inventory, candidate serialization, same-directory transaction artifacts, a flushed write-ahead journal, rollback/recovery, and publication into the unchanged public `CustomWorkflows` dictionary. `ComfyUIBackendExtension` and `ComfyUIWebAPI` remain compatibility facades, while every maintained list/read/parameter/generation/refresh/write path crosses the store boundary.

**Tech Stack:** C# 12, .NET 8, `ConcurrentDictionary`, Newtonsoft `JObject`, SHA-256 artifact identity, `FileStream.Flush(true)`, same-filesystem `File.Move`, existing FreneticUtilities filename/encoding helpers, and SwarmUI logging/API conventions.

---

## Repository Execution Constraints

- Work directly on `master`; maintainer Reaper176 explicitly declined a worktree.
- Do not run builds, tests, launchers, servers, browsers, backends, installers, or live workflow-storage mutations. `AGENTS.md` reserves runtime verification for the maintainer.
- Use static inspection, `rg`, `git diff --check`, and staged-diff review only.
- Preserve the existing user-owned modifications in:
  - `src/Data/Settings.fds`
  - `src/Pages/Text2Image.cshtml`
  - `src/wwwroot/js/genpage/gentab/loras.js`
  - `src/wwwroot/js/genpage/main.js`
- Never inspect, enumerate, stage, or modify `Data.pre-restore-2026-07-19/`.
- Stage only the files named by each task.
- Do not advance the audit roadmap or claim runtime success before the maintainer confirms the validation matrix.

## File Structure

- Create `src/BuiltinExtensions/ComfyUIBackend/ComfyWorkflowStore.cs`: own the lock, inventory, hydration, snapshots, transaction model, journal/path validation, save/delete commit, rollback, and recovery.
- Modify `src/BuiltinExtensions/ComfyUIBackend/ComfyUIBackendExtension.cs`: preserve `CustomWorkflows`, `LoadWorkflowFiles`, and `GetWorkflowByName` as public compatibility surfaces while delegating maintained access to the store.
- Modify `src/BuiltinExtensions/ComfyUIBackend/ComfyUIWebAPI.cs`: preserve route signatures and response shapes while delegating read/list/save/delete storage operations.
- Modify `docs/superpowers/specs/2026-07-23-durable-comfy-workflow-transactions-design.md`: record implementation and validation status without changing the approved design.
- Modify `docs/superpowers/audits/2026-07-21-maintainability-architecture-refresh.md`: reconcile Comfy F23/rank 6 and advance the recommendation only after maintainer validation.

Automated tests are intentionally absent because this repository prohibits agents from running any form of testing. Each production task instead contains a narrow static proof, and Task 7 supplies the maintainer runtime matrix.

### Task 1: Establish the coordinated workflow read boundary

**Files:**
- Create: `src/BuiltinExtensions/ComfyUIBackend/ComfyWorkflowStore.cs`
- Modify: `src/BuiltinExtensions/ComfyUIBackend/ComfyUIBackendExtension.cs:271-347`
- Modify: `src/BuiltinExtensions/ComfyUIBackend/ComfyUIBackendExtension.cs:726-730`
- Modify: `src/BuiltinExtensions/ComfyUIBackend/ComfyUIWebAPI.cs:81-125`

- [ ] **Step 1: Create the store lock, inventory, hydration, and snapshot owner**

Create `ComfyWorkflowStore.cs` with the following public surface and move the existing inventory/hydration logic behind it:

```csharp
using FreneticUtilities.FreneticExtensions;
using FreneticUtilities.FreneticToolkit;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using SwarmUI.Utils;

namespace SwarmUI.Builtin_ComfyUIBackend;

/// <summary>Owns durable Comfy custom-workflow storage and coordinated maintained access.</summary>
public static class ComfyWorkflowStore
{
    /// <summary>Serializes workflow transactions, recovery, inventory, hydration, and maintained readers.</summary>
    private static readonly object WorkflowLock = new();

    /// <summary>Loads the workflow inventory while retaining the public dictionary instance.</summary>
    public static void LoadWorkflowFiles(string extensionFilePath)
    {
        lock (WorkflowLock)
        {
            string workflowRoot = Path.Combine(extensionFilePath, "CustomWorkflows");
            Directory.CreateDirectory(workflowRoot);
            Directory.CreateDirectory(Path.Combine(workflowRoot, "Examples"));
            string[] GetCustomFlows(string path)
            {
                return [.. Directory.EnumerateFiles(Path.Combine(extensionFilePath, path), "*.*", new EnumerationOptions()
                {
                    RecurseSubdirectories = true
                }).Select(file => file.Replace('\\', '/').After($"/{path}/")).Order()];
            }
            ComfyUIBackendExtension.ExampleWorkflowNames = GetCustomFlows("ExampleWorkflows");
            string[] customFlows = GetCustomFlows("CustomWorkflows");
            bool anyCopied = false;
            foreach (string workflow in ComfyUIBackendExtension.ExampleWorkflowNames.Where(file => file.EndsWith(".json")))
            {
                if (!customFlows.Contains($"Examples/{workflow}") && !customFlows.Contains($"Examples/{workflow}.deleted"))
                {
                    File.Copy(Path.Combine(extensionFilePath, "ExampleWorkflows", workflow), Path.Combine(workflowRoot, "Examples", workflow));
                    anyCopied = true;
                }
            }
            if (anyCopied)
            {
                customFlows = GetCustomFlows("CustomWorkflows");
            }
            ComfyUIBackendExtension.CustomWorkflows.Clear();
            foreach (string workflow in customFlows.Where(file => file.EndsWith(".json")))
            {
                ComfyUIBackendExtension.CustomWorkflows.TryAdd(workflow.BeforeLast('.'), null);
            }
        }
    }

    /// <summary>Gets and, when needed, lazily hydrates one workflow under the store boundary.</summary>
    public static ComfyUIBackendExtension.ComfyCustomWorkflow GetWorkflowByName(string name)
    {
        lock (WorkflowLock)
        {
            return GetWorkflowByNameLocked(name);
        }
    }

    /// <summary>Gets and hydrates a workflow while the caller owns <see cref="WorkflowLock"/>.</summary>
    private static ComfyUIBackendExtension.ComfyCustomWorkflow GetWorkflowByNameLocked(string name)
    {
        if (!ComfyUIBackendExtension.CustomWorkflows.TryGetValue(name, out ComfyUIBackendExtension.ComfyCustomWorkflow workflow))
        {
            return null;
        }
        if (workflow is not null)
        {
            return workflow;
        }
        string path = Path.Combine(ComfyUIBackendExtension.Folder, "CustomWorkflows", $"{name}.json");
        if (!File.Exists(path))
        {
            ComfyUIBackendExtension.CustomWorkflows.TryRemove(name, out _);
            return null;
        }
        try
        {
            JObject json = ComfySubmittedJson.ParseObject(File.ReadAllText(path));
            string GetStringFor(string key)
            {
                if (!json.TryGetValue(key, out JToken data))
                {
                    return null;
                }
                if (data.Type == JTokenType.String)
                {
                    return data.ToString();
                }
                return data.ToString(Formatting.None);
            }
            workflow = new(name, GetStringFor("workflow"), GetStringFor("prompt"), GetStringFor("custom_params"),
                GetStringFor("param_values"), GetStringFor("image") ?? "/imgs/model_placeholder.jpg",
                GetStringFor("description"), json.TryGetValue("enable_in_simple", out JToken enabled) && enabled.ToObject<bool>());
            ComfyUIBackendExtension.CustomWorkflows[name] = workflow;
            return workflow;
        }
        catch (Exception)
        {
            Logs.Error("Error loading ComfyUI custom workflow (submitted content redacted).");
            return null;
        }
    }

    /// <summary>Gets a consistent, hydrated snapshot for maintained workflow listings.</summary>
    public static List<ComfyUIBackendExtension.ComfyCustomWorkflow> GetWorkflowSnapshot()
    {
        lock (WorkflowLock)
        {
            List<ComfyUIBackendExtension.ComfyCustomWorkflow> workflows = [];
            foreach (string name in ComfyUIBackendExtension.CustomWorkflows.Keys.ToList())
            {
                ComfyUIBackendExtension.ComfyCustomWorkflow workflow = GetWorkflowByNameLocked(name);
                if (workflow is not null)
                {
                    workflows.Add(workflow);
                }
            }
            return workflows;
        }
    }

    /// <summary>Gets a consistent sorted name snapshot for dynamic parameter values.</summary>
    public static string[] GetWorkflowNames()
    {
        lock (WorkflowLock)
        {
            return [.. ComfyUIBackendExtension.CustomWorkflows.Keys.Order()];
        }
    }

    /// <summary>Gets the parameter prompt while preserving the distinction between an absent and unloadable published name.</summary>
    public static bool TryGetWorkflowParameterPrompt(string name, out string prompt)
    {
        lock (WorkflowLock)
        {
            if (!ComfyUIBackendExtension.CustomWorkflows.ContainsKey(name))
            {
                prompt = null;
                return false;
            }
            prompt = GetWorkflowByNameLocked(name)?.Prompt;
            return true;
        }
    }
}
```

Keep the existing null placeholders, fixed redacted load diagnostic, JSON string normalization, placeholder image, and key enumeration behavior unchanged.

- [ ] **Step 2: Convert extension methods and parameter delegates into compatibility facades**

Replace the bodies of `LoadWorkflowFiles` and `GetWorkflowByName` with:

```csharp
    public void LoadWorkflowFiles()
    {
        ComfyWorkflowStore.LoadWorkflowFiles(FilePath);
    }

    public static ComfyCustomWorkflow GetWorkflowByName(string name)
    {
        return ComfyWorkflowStore.GetWorkflowByName(name);
    }
```

Replace the two `CustomWorkflowParam` delegates with:

```csharp
            GetValues: (_) => ComfyWorkflowStore.GetWorkflowNames(),
            Clean: (_, val) => ComfyWorkflowStore.TryGetWorkflowParameterPrompt(val, out string prompt) ? $"PARSED%{val}%{prompt}" : val,
```

This preserves the public methods and dictionary while preventing a maintained parameter consumer from splitting its existence check and read across a rename.

- [ ] **Step 3: Route read and list through consistent store snapshots**

Keep `ReadCustomWorkflow`'s response construction unchanged, but obtain the record with:

```csharp
        ComfyUIBackendExtension.ComfyCustomWorkflow workflow = ComfyWorkflowStore.GetWorkflowByName(path);
```

Replace the list route's dictionary enumeration with:

```csharp
            ["workflows"] = JToken.FromObject(ComfyWorkflowStore.GetWorkflowSnapshot().OrderBy(workflow => workflow.Name).Select(workflow => new JObject()
            {
                ["name"] = workflow.Name,
                ["image"] = workflow.Image ?? "/imgs/model_placeholder.jpg",
                ["description"] = workflow.Description,
                ["enable_in_simple"] = workflow.EnableInSimple
            }).ToList())
```

Generation remains coordinated through `ReadCustomWorkflow`, which now delegates to the store.

- [ ] **Step 4: Prove the read migration and commit it**

Run:

```bash
rg -n "CustomWorkflows\\.(Keys|ContainsKey)|CustomWorkflows\\[|GetWorkflowByName\\(" src/BuiltinExtensions/ComfyUIBackend --glob '*.cs'
rg -n "WorkflowLock|GetWorkflowSnapshot|GetWorkflowNames|TryGetWorkflowParameterPrompt" src/BuiltinExtensions/ComfyUIBackend/ComfyWorkflowStore.cs src/BuiltinExtensions/ComfyUIBackend/ComfyUIBackendExtension.cs src/BuiltinExtensions/ComfyUIBackend/ComfyUIWebAPI.cs
git diff --check -- src/BuiltinExtensions/ComfyUIBackend/ComfyWorkflowStore.cs src/BuiltinExtensions/ComfyUIBackend/ComfyUIBackendExtension.cs src/BuiltinExtensions/ComfyUIBackend/ComfyUIWebAPI.cs
```

Expected:

- direct maintained read/enumeration sites have moved into the store;
- extension methods remain as public facades;
- the write routes are the only remaining direct dictionary mutators outside the store at this intermediate commit; and
- `git diff --check` returns no output.

Review and commit:

```bash
git diff -- src/BuiltinExtensions/ComfyUIBackend/ComfyWorkflowStore.cs src/BuiltinExtensions/ComfyUIBackend/ComfyUIBackendExtension.cs src/BuiltinExtensions/ComfyUIBackend/ComfyUIWebAPI.cs
git status --short --untracked-files=no
git add -- src/BuiltinExtensions/ComfyUIBackend/ComfyWorkflowStore.cs src/BuiltinExtensions/ComfyUIBackend/ComfyUIBackendExtension.cs src/BuiltinExtensions/ComfyUIBackend/ComfyUIWebAPI.cs
git diff --cached --check
git commit -m "refactor: centralize comfy workflow reads"
```

### Task 2: Add the transaction and safe-path primitives

**Files:**
- Modify: `src/BuiltinExtensions/ComfyUIBackend/ComfyWorkflowStore.cs`

- [ ] **Step 1: Define the journal contract and prepared save value**

Add these private types and constants immediately after `WorkflowLock`:

```csharp
    /// <summary>Current workflow transaction journal format.</summary>
    private const int JournalVersion = 1;

    /// <summary>Reserved active-journal filename beneath the workflow root.</summary>
    private const string JournalFileName = ".swarm-workflow-transaction";

    /// <summary>Supported workflow transaction operations.</summary>
    private enum TransactionOperation
    {
        Save,
        Delete
    }

    /// <summary>Durable workflow transaction phases.</summary>
    private enum TransactionPhase
    {
        Prepared,
        Committed
    }

    /// <summary>Validated submitted values that do not depend on locked predecessor state.</summary>
    private sealed record PreparedSave(string DestinationName, string SourceName, bool HasReplacement,
        string Workflow, string Prompt, string CustomParams, string ParamValues, string SuppliedImage,
        bool InheritImage, string Description, bool EnableInSimple, JObject ParsedWorkflow,
        JObject ParsedPrompt, JObject ParsedCustomParams, JObject ParsedParamValues);

    /// <summary>Durable metadata required to commit or recover one workflow transaction.</summary>
    private sealed class WorkflowTransaction
    {
        public int Version { get; set; } = JournalVersion;

        public string Id { get; set; }

        public TransactionOperation Operation { get; set; }

        public TransactionPhase Phase { get; set; }

        public string SourceName { get; set; }

        public string DestinationName { get; set; }

        public string DestinationHash { get; set; }

        public string StagePath { get; set; }

        public string SourceBackupPath { get; set; }

        public string DestinationBackupPath { get; set; }

        public string MarkerStagePath { get; set; }

        public string MarkerBackupPath { get; set; }

        public bool SourceExisted { get; set; }

        public bool DestinationExisted { get; set; }

        public bool MarkerExisted { get; set; }

        public bool CreateMarker { get; set; }
    }
```

These are properties rather than undocumented fields. The journal carries names, the SHA-256 identity of the staged destination, relative artifact paths, existence flags, and phase metadata. It never contains workflow content.

- [ ] **Step 2: Add normalized root, workflow, marker, and artifact paths**

Add helpers with these exact contracts:

```csharp
    /// <summary>Gets the normalized workflow storage root.</summary>
    private static string GetWorkflowRoot()
    {
        return Path.GetFullPath(Path.Combine(ComfyUIBackendExtension.Folder, "CustomWorkflows"));
    }

    /// <summary>Gets a normalized workflow JSON path from an already-cleaned workflow name.</summary>
    private static string GetWorkflowPath(string cleanedName)
    {
        return GetContainedPath($"{cleanedName}.json");
    }

    /// <summary>Gets the deletion-marker path for an already-cleaned workflow name.</summary>
    private static string GetMarkerPath(string cleanedName)
    {
        return $"{GetWorkflowPath(cleanedName)}.deleted";
    }

    /// <summary>Resolves a relative transaction path and rejects escape from the workflow root.</summary>
    private static string GetContainedPath(string relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath) || Path.IsPathRooted(relativePath))
        {
            throw new InvalidDataException("Invalid workflow transaction path.");
        }
        string root = GetWorkflowRoot();
        string fullPath = Path.GetFullPath(Path.Combine(root, relativePath));
        string rootPrefix = root.EndsWith(Path.DirectorySeparatorChar) ? root : $"{root}{Path.DirectorySeparatorChar}";
        StringComparison comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        if (!fullPath.StartsWith(rootPrefix, comparison))
        {
            throw new InvalidDataException("Invalid workflow transaction path.");
        }
        return fullPath;
    }

    /// <summary>Creates a reserved sibling artifact path and returns it relative to the workflow root.</summary>
    private static string CreateArtifactPath(string finalPath, string id, string role)
    {
        string directory = Path.GetDirectoryName(finalPath);
        string artifact = Path.Combine(directory, $".swarm-workflow-{id}-{role}");
        return Path.GetRelativePath(GetWorkflowRoot(), artifact);
    }

    /// <summary>Verifies that a recorded artifact belongs to its transaction and role.</summary>
    private static string GetArtifactPath(WorkflowTransaction transaction, string relativePath, string role)
    {
        string expectedToken = $".swarm-workflow-{transaction.Id}-{role}";
        if (string.IsNullOrWhiteSpace(relativePath) || !Path.GetFileName(relativePath).Equals(expectedToken, StringComparison.Ordinal))
        {
            throw new InvalidDataException("Invalid workflow transaction artifact.");
        }
        return GetContainedPath(relativePath);
    }
```

Do not accept a journal-supplied absolute path or a filename merely because it has the reserved prefix.

- [ ] **Step 3: Add flushed writes and atomic journal installation**

Add:

```csharp
    /// <summary>Creates and durably flushes a new transaction-owned file.</summary>
    private static void WriteNewFlushedFile(string path, byte[] data)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path));
        using FileStream stream = new(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        stream.Write(data);
        stream.Flush(true);
    }

    /// <summary>Serializes the transaction without workflow content.</summary>
    private static byte[] SerializeTransaction(WorkflowTransaction transaction)
    {
        JObject data = new()
        {
            ["version"] = transaction.Version,
            ["id"] = transaction.Id,
            ["operation"] = transaction.Operation.ToString(),
            ["phase"] = transaction.Phase.ToString(),
            ["source_name"] = transaction.SourceName,
            ["destination_name"] = transaction.DestinationName,
            ["destination_hash"] = transaction.DestinationHash,
            ["stage_path"] = transaction.StagePath,
            ["source_backup_path"] = transaction.SourceBackupPath,
            ["destination_backup_path"] = transaction.DestinationBackupPath,
            ["marker_stage_path"] = transaction.MarkerStagePath,
            ["marker_backup_path"] = transaction.MarkerBackupPath,
            ["source_existed"] = transaction.SourceExisted,
            ["destination_existed"] = transaction.DestinationExisted,
            ["marker_existed"] = transaction.MarkerExisted,
            ["create_marker"] = transaction.CreateMarker
        };
        return data.ToString().EncodeUTF8();
    }

    /// <summary>Flushes and installs the requested complete journal phase.</summary>
    private static void WriteJournal(WorkflowTransaction transaction)
    {
        string root = GetWorkflowRoot();
        string journalPath = Path.Combine(root, JournalFileName);
        string temporaryPath = Path.Combine(root, $"{JournalFileName}-{transaction.Id}-new");
        if (transaction.Phase == TransactionPhase.Prepared && File.Exists(journalPath))
        {
            throw new InvalidDataException("A workflow transaction journal is already active.");
        }
        if (File.Exists(temporaryPath))
        {
            throw new InvalidDataException("Workflow transaction journal staging path is already occupied.");
        }
        WriteNewFlushedFile(temporaryPath, SerializeTransaction(transaction));
        try
        {
            File.Move(temporaryPath, journalPath, true);
        }
        catch
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
            throw;
        }
    }
```

Use one complete flushed journal image per phase. Do not modify a journal in place.

- [ ] **Step 4: Add strict journal parsing**

Add:

```csharp
    /// <summary>Reads and strictly validates the active workflow transaction journal.</summary>
    private static WorkflowTransaction ReadJournal(string path)
    {
        try
        {
            JObject data = ComfySubmittedJson.ParseObject(File.ReadAllText(path));
            T ReadRequired<T>(string key, JTokenType type)
            {
                if (!data.TryGetValue(key, out JToken token) || token.Type != type)
                {
                    throw new InvalidDataException("Invalid workflow transaction journal.");
                }
                return token.ToObject<T>();
            }
            string ReadOptionalString(string key)
            {
                if (!data.TryGetValue(key, out JToken token) || token.Type == JTokenType.Null)
                {
                    return null;
                }
                if (token.Type != JTokenType.String)
                {
                    throw new InvalidDataException("Invalid workflow transaction journal.");
                }
                return token.ToString();
            }
            string ReadCleanedName(string key)
            {
                string value = ReadOptionalString(key);
                if (value is not null && Utilities.StrictFilenameClean(value) != value)
                {
                    throw new InvalidDataException("Invalid workflow transaction journal.");
                }
                return value;
            }
            int version = ReadRequired<int>("version", JTokenType.Integer);
            string id = ReadRequired<string>("id", JTokenType.String);
            string operationText = ReadRequired<string>("operation", JTokenType.String);
            string phaseText = ReadRequired<string>("phase", JTokenType.String);
            if (version != JournalVersion || !Guid.TryParseExact(id, "N", out _)
                || !Enum.TryParse(operationText, false, out TransactionOperation operation)
                || !Enum.TryParse(phaseText, false, out TransactionPhase phase))
            {
                throw new InvalidDataException("Invalid workflow transaction journal.");
            }
            WorkflowTransaction transaction = new()
            {
                Version = version,
                Id = id,
                Operation = operation,
                Phase = phase,
                SourceName = ReadCleanedName("source_name"),
                DestinationName = ReadCleanedName("destination_name"),
                DestinationHash = ReadOptionalString("destination_hash"),
                StagePath = ReadOptionalString("stage_path"),
                SourceBackupPath = ReadOptionalString("source_backup_path"),
                DestinationBackupPath = ReadOptionalString("destination_backup_path"),
                MarkerStagePath = ReadOptionalString("marker_stage_path"),
                MarkerBackupPath = ReadOptionalString("marker_backup_path"),
                SourceExisted = ReadRequired<bool>("source_existed", JTokenType.Boolean),
                DestinationExisted = ReadRequired<bool>("destination_existed", JTokenType.Boolean),
                MarkerExisted = ReadRequired<bool>("marker_existed", JTokenType.Boolean),
                CreateMarker = ReadRequired<bool>("create_marker", JTokenType.Boolean)
            };
            if (transaction.Operation == TransactionOperation.Save)
            {
                if (transaction.SourceName is null || transaction.DestinationName is null || transaction.StagePath is null
                    || !IsSha256(transaction.DestinationHash))
                {
                    throw new InvalidDataException("Invalid workflow transaction journal.");
                }
                GetArtifactPath(transaction, transaction.StagePath, "stage");
                if (transaction.DestinationExisted)
                {
                    GetArtifactPath(transaction, transaction.DestinationBackupPath, "destination-backup");
                }
                else if (transaction.DestinationBackupPath is not null)
                {
                    throw new InvalidDataException("Invalid workflow transaction journal.");
                }
                bool distinctOwnedSource = transaction.SourceExisted
                    && !PathsEqual(GetWorkflowPath(transaction.SourceName), GetWorkflowPath(transaction.DestinationName));
                if (distinctOwnedSource)
                {
                    GetArtifactPath(transaction, transaction.SourceBackupPath, "source-backup");
                }
                else if (transaction.SourceBackupPath is not null)
                {
                    throw new InvalidDataException("Invalid workflow transaction journal.");
                }
            }
            else
            {
                if (transaction.SourceName is null || !transaction.SourceExisted || transaction.DestinationName is not null
                    || transaction.DestinationHash is not null || transaction.StagePath is not null
                    || transaction.DestinationBackupPath is not null || transaction.DestinationExisted)
                {
                    throw new InvalidDataException("Invalid workflow transaction journal.");
                }
                GetArtifactPath(transaction, transaction.SourceBackupPath, "source-backup");
            }
            if (transaction.CreateMarker)
            {
                if (transaction.SourceName is null || transaction.MarkerStagePath is null)
                {
                    throw new InvalidDataException("Invalid workflow transaction journal.");
                }
                GetArtifactPath(transaction, transaction.MarkerStagePath, "marker-stage");
                if (transaction.MarkerExisted)
                {
                    GetArtifactPath(transaction, transaction.MarkerBackupPath, "marker-backup");
                }
                else if (transaction.MarkerBackupPath is not null)
                {
                    throw new InvalidDataException("Invalid workflow transaction journal.");
                }
            }
            else if (transaction.MarkerStagePath is not null || transaction.MarkerBackupPath is not null
                || transaction.MarkerExisted)
            {
                throw new InvalidDataException("Invalid workflow transaction journal.");
            }
            return transaction;
        }
        catch (InvalidDataException)
        {
            throw;
        }
        catch (Exception exception)
        {
            throw new InvalidDataException("Invalid workflow transaction journal.", exception);
        }
    }

    /// <summary>Whether two normalized paths identify the same platform path.</summary>
    private static bool PathsEqual(string first, string second)
    {
        StringComparison comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        return string.Equals(Path.GetFullPath(first), Path.GetFullPath(second), comparison);
    }

    /// <summary>Whether text is one canonical lowercase SHA-256 identity.</summary>
    private static bool IsSha256(string hash)
    {
        return hash is not null && hash.Length == 64 && hash.All(character =>
            (character >= '0' && character <= '9') || (character >= 'a' && character <= 'f'));
    }
```

Do not log the wrapped parser exception or journal contents. The inner exception is retained only for explicit server-side maintainer diagnosis outside the fixed recovery diagnostic.

- [ ] **Step 5: Prove path/journal confinement and commit**

Run:

```bash
rg -n "JournalVersion|JournalFileName|PreparedSave|WorkflowTransaction|GetContainedPath|CreateArtifactPath|GetArtifactPath|WriteNewFlushedFile|WriteJournal|ReadJournal" src/BuiltinExtensions/ComfyUIBackend/ComfyWorkflowStore.cs
rg -n "Flush\\(true\\)|Path\\.GetFullPath|Path\\.IsPathRooted|OperatingSystem\\.IsWindows|Guid\\.TryParseExact" src/BuiltinExtensions/ComfyUIBackend/ComfyWorkflowStore.cs
git diff --check -- src/BuiltinExtensions/ComfyUIBackend/ComfyWorkflowStore.cs
```

Expected: the journal has two phases, every artifact is relative and role-bound, both data and journal writes are flushed, and whitespace checks are clean.

Review and commit only the store:

```bash
git diff -- src/BuiltinExtensions/ComfyUIBackend/ComfyWorkflowStore.cs
git add -- src/BuiltinExtensions/ComfyUIBackend/ComfyWorkflowStore.cs
git diff --cached --check
git commit -m "refactor: define workflow transaction journal"
```

### Task 3: Implement rollback and startup recovery

**Files:**
- Modify: `src/BuiltinExtensions/ComfyUIBackend/ComfyWorkflowStore.cs`

- [ ] **Step 1: Add idempotent pre-state restoration helpers**

Add `using System.Security.Cryptography;`, then add:

```csharp
    /// <summary>Gets the canonical lowercase SHA-256 identity of bytes.</summary>
    private static string GetDataHash(byte[] data)
    {
        return Convert.ToHexString(SHA256.HashData(data)).ToLowerFast();
    }

    /// <summary>Whether a file has the recorded transaction-owned SHA-256 identity.</summary>
    private static bool FileMatchesHash(string path, string expectedHash)
    {
        if (expectedHash is null || !File.Exists(path))
        {
            return false;
        }
        using FileStream stream = File.OpenRead(path);
        string actualHash = Convert.ToHexString(SHA256.HashData(stream)).ToLowerFast();
        return actualHash == expectedHash;
    }

    /// <summary>Restores one original file from a transaction backup when one was moved.</summary>
    private static void RestoreOriginal(string finalPath, string backupPath, bool originallyExisted,
        string installedHash)
    {
        if (backupPath is not null && File.Exists(backupPath))
        {
            if (File.Exists(finalPath))
            {
                if (!FileMatchesHash(finalPath, installedHash))
                {
                    throw new IOException("A workflow transaction final path contains unverified data.");
                }
                File.Delete(finalPath);
            }
            File.Move(backupPath, finalPath);
            return;
        }
        if (originallyExisted)
        {
            if (!File.Exists(finalPath))
            {
                throw new IOException("A workflow transaction original and its backup are both missing.");
            }
            return;
        }
        if (File.Exists(finalPath))
        {
            if (!FileMatchesHash(finalPath, installedHash))
            {
                throw new IOException("A workflow transaction final path contains unverified data.");
            }
            File.Delete(finalPath);
        }
    }

    /// <summary>Restores the original deletion-marker existence and content.</summary>
    private static void RestoreMarker(WorkflowTransaction transaction)
    {
        if (!transaction.CreateMarker)
        {
            return;
        }
        string markerPath = GetMarkerPath(transaction.SourceName);
        string markerHash = GetDataHash("deleted-by-user".EncodeUTF8());
        if (transaction.MarkerExisted)
        {
            string markerBackupPath = GetArtifactPath(transaction, transaction.MarkerBackupPath, "marker-backup");
            RestoreOriginal(markerPath, markerBackupPath, true, markerHash);
        }
        else if (File.Exists(markerPath))
        {
            RestoreOriginal(markerPath, null, false, markerHash);
        }
    }
```

An installed destination or marker is deleted only after its content matches the identity recorded or implied by the journal. Source restoration passes no installed hash, so finding both a source backup and an unexpected final source aborts rather than deleting either.

- [ ] **Step 2: Implement prepared rollback**

Add:

```csharp
    /// <summary>Restores the complete pre-state of an uncommitted workflow transaction.</summary>
    private static void RollbackPrepared(WorkflowTransaction transaction)
    {
        string stagePath = transaction.StagePath is null ? null : GetArtifactPath(transaction, transaction.StagePath, "stage");
        string markerStagePath = transaction.MarkerStagePath is null ? null
            : GetArtifactPath(transaction, transaction.MarkerStagePath, "marker-stage");
        RestoreMarker(transaction);
        if (transaction.Operation == TransactionOperation.Save)
        {
            string destinationPath = GetWorkflowPath(transaction.DestinationName);
            string destinationBackupPath = transaction.DestinationExisted
                ? GetArtifactPath(transaction, transaction.DestinationBackupPath, "destination-backup")
                : null;
            RestoreOriginal(destinationPath, destinationBackupPath, transaction.DestinationExisted,
                transaction.DestinationHash);
            string sourcePath = GetWorkflowPath(transaction.SourceName);
            if (transaction.SourceExisted && !PathsEqual(sourcePath, destinationPath))
            {
                string sourceBackupPath = GetArtifactPath(transaction, transaction.SourceBackupPath, "source-backup");
                RestoreOriginal(sourcePath, sourceBackupPath, true, null);
            }
        }
        else
        {
            string sourcePath = GetWorkflowPath(transaction.SourceName);
            string sourceBackupPath = GetArtifactPath(transaction, transaction.SourceBackupPath, "source-backup");
            RestoreOriginal(sourcePath, sourceBackupPath, true, null);
        }
        if (stagePath is not null && File.Exists(stagePath))
        {
            File.Delete(stagePath);
        }
        if (markerStagePath is not null && File.Exists(markerStagePath))
        {
            File.Delete(markerStagePath);
        }
        string journalPath = Path.Combine(GetWorkflowRoot(), JournalFileName);
        if (File.Exists(journalPath))
        {
            File.Delete(journalPath);
        }
    }
```

For a same-name save, only the destination restore runs. Any rollback failure propagates before journal deletion, leaving the recorded artifacts available for recovery retry.

- [ ] **Step 3: Implement committed cleanup**

Add:

```csharp
    /// <summary>Removes only verified artifacts from a committed transaction, deleting the journal last.</summary>
    private static void CleanupCommitted(WorkflowTransaction transaction)
    {
        List<(string Path, string Role)> artifacts = [];
        if (transaction.StagePath is not null)
        {
            artifacts.Add((transaction.StagePath, "stage"));
        }
        if (transaction.SourceBackupPath is not null)
        {
            artifacts.Add((transaction.SourceBackupPath, "source-backup"));
        }
        if (transaction.DestinationBackupPath is not null)
        {
            artifacts.Add((transaction.DestinationBackupPath, "destination-backup"));
        }
        if (transaction.MarkerStagePath is not null)
        {
            artifacts.Add((transaction.MarkerStagePath, "marker-stage"));
        }
        if (transaction.MarkerBackupPath is not null)
        {
            artifacts.Add((transaction.MarkerBackupPath, "marker-backup"));
        }
        foreach ((string relativePath, string role) in artifacts)
        {
            string artifactPath = GetArtifactPath(transaction, relativePath, role);
            if (File.Exists(artifactPath))
            {
                File.Delete(artifactPath);
            }
        }
        string journalPath = Path.Combine(GetWorkflowRoot(), JournalFileName);
        if (File.Exists(journalPath))
        {
            File.Delete(journalPath);
        }
    }
```

Never delete the journal before all owned artifacts are gone.

- [ ] **Step 4: Recover before inventory or example copying**

Add:

```csharp
    /// <summary>Ensures a committed example-deletion marker is durably installed.</summary>
    private static void EnsureCommittedMarker(WorkflowTransaction transaction)
    {
        if (!transaction.CreateMarker)
        {
            return;
        }
        string markerPath = GetMarkerPath(transaction.SourceName);
        if (File.Exists(markerPath))
        {
            return;
        }
        string markerStagePath = GetArtifactPath(transaction, transaction.MarkerStagePath, "marker-stage");
        if (!File.Exists(markerStagePath))
        {
            WriteNewFlushedFile(markerStagePath, "deleted-by-user".EncodeUTF8());
        }
        File.Move(markerStagePath, markerPath);
    }

    /// <summary>Verifies the authoritative post-state described by a committed journal.</summary>
    private static void VerifyCommittedState(WorkflowTransaction transaction)
    {
        if (transaction.Operation == TransactionOperation.Save)
        {
            string destinationPath = GetWorkflowPath(transaction.DestinationName);
            if (!File.Exists(destinationPath))
            {
                throw new IOException("Committed workflow destination is missing.");
            }
            if (!FileMatchesHash(destinationPath, transaction.DestinationHash))
            {
                throw new IOException("Committed workflow destination contains unverified data.");
            }
            string sourcePath = GetWorkflowPath(transaction.SourceName);
            if (transaction.SourceExisted && !PathsEqual(sourcePath, destinationPath) && File.Exists(sourcePath))
            {
                throw new IOException("Committed workflow predecessor is still present.");
            }
        }
        else if (File.Exists(GetWorkflowPath(transaction.SourceName)))
        {
            throw new IOException("Committed deleted workflow is still present.");
        }
        EnsureCommittedMarker(transaction);
    }

    /// <summary>Recovers the single active transaction before workflow inventory is published.</summary>
    private static void RecoverPendingTransactionLocked()
    {
        string journalPath = Path.Combine(GetWorkflowRoot(), JournalFileName);
        if (!File.Exists(journalPath))
        {
            return;
        }
        WorkflowTransaction transaction;
        try
        {
            transaction = ReadJournal(journalPath);
            if (transaction.Phase == TransactionPhase.Prepared)
            {
                RollbackPrepared(transaction);
                return;
            }
            VerifyCommittedState(transaction);
            CleanupCommitted(transaction);
        }
        catch (Exception)
        {
            Logs.Error("Failed to recover ComfyUI custom workflow transaction (details redacted).");
            throw new InvalidDataException("Stored workflow recovery could not be completed safely.");
        }
    }
```

Insert:

```csharp
            RecoverPendingTransactionLocked();
```

in `LoadWorkflowFiles` after both workflow directories are created but before example inventory, example copying, or `CustomWorkflows.Clear()`. Therefore a malformed journal aborts refresh without clearing the last published dictionary.

- [ ] **Step 5: Prove recovery ordering and commit**

Run:

```bash
rg -n -C 5 "RecoverPendingTransactionLocked|CustomWorkflows.Clear|ExampleWorkflowNames|File.Copy" src/BuiltinExtensions/ComfyUIBackend/ComfyWorkflowStore.cs
rg -n "RollbackPrepared|RestoreOriginal|RestoreMarker|VerifyCommittedState|CleanupCommitted" src/BuiltinExtensions/ComfyUIBackend/ComfyWorkflowStore.cs
rg -n "Directory\\.EnumerateFiles.*swarm-workflow|Delete.*swarm-workflow" src/BuiltinExtensions/ComfyUIBackend/ComfyWorkflowStore.cs
git diff --check -- src/BuiltinExtensions/ComfyUIBackend/ComfyWorkflowStore.cs
```

Expected:

- recovery precedes cache clear and example copying;
- prepared recovery restores while committed recovery only verifies/finishes known state and cleans verified artifacts;
- there is no broad reserved-file sweep; and
- whitespace checks are clean.

Review and commit:

```bash
git diff -- src/BuiltinExtensions/ComfyUIBackend/ComfyWorkflowStore.cs
git add -- src/BuiltinExtensions/ComfyUIBackend/ComfyWorkflowStore.cs
git diff --cached --check
git commit -m "refactor: recover interrupted workflow transactions"
```

### Task 4: Make workflow save and replacement transactional

**Files:**
- Modify: `src/BuiltinExtensions/ComfyUIBackend/ComfyWorkflowStore.cs`
- Modify: `src/BuiltinExtensions/ComfyUIBackend/ComfyUIWebAPI.cs:35-79`

- [ ] **Step 1: Prepare all request-controlled input before transaction creation**

Add:

```csharp
    /// <summary>Validates request-controlled save values before any transaction artifact is created.</summary>
    private static PreparedSave PrepareSave(string name, string workflow, string prompt, string customParams,
        string paramValues, string image, string description, bool enableInSimple, string replace)
    {
        string destinationName = Utilities.StrictFilenameClean(name);
        bool hasReplacement = !string.IsNullOrWhiteSpace(replace);
        string sourceName = Utilities.StrictFilenameClean(hasReplacement ? replace : name);
        JObject parsedWorkflow = ComfySubmittedJson.ParseObject(workflow);
        JObject parsedPrompt = ComfySubmittedJson.ParseObject(prompt);
        JObject parsedCustomParams = ComfySubmittedJson.ParseObject(customParams);
        JObject parsedParamValues = ComfySubmittedJson.ParseObject(paramValues);
        bool inheritImage = string.IsNullOrWhiteSpace(image);
        string suppliedImage = image;
        if (!inheritImage)
        {
            if (image == "clear")
            {
                suppliedImage = null;
            }
            else
            {
                suppliedImage = ImageFile.FromDataString(image).ToMetadataFormat();
            }
        }
        return new(destinationName, sourceName, hasReplacement, workflow, prompt, customParams, paramValues,
            suppliedImage, inheritImage, description, enableInSimple, parsedWorkflow, parsedPrompt,
            parsedCustomParams, parsedParamValues);
    }
```

Add `using SwarmUI.Media;`. Parsing and supplied-image conversion must remain outside `WorkflowLock`.

- [ ] **Step 2: Resolve inherited state and serialize once under the lock**

Add:

```csharp
    /// <summary>Builds the exact cache record and durable JSON bytes from one locked predecessor state.</summary>
    private static (ComfyUIBackendExtension.ComfyCustomWorkflow Record, byte[] Data) CompleteSaveLocked(PreparedSave prepared)
    {
        string image = prepared.SuppliedImage;
        if (prepared.InheritImage && ComfyUIBackendExtension.CustomWorkflows.ContainsKey(prepared.SourceName))
        {
            image = GetWorkflowByNameLocked(prepared.SourceName)?.Image;
        }
        if (string.IsNullOrWhiteSpace(image))
        {
            image = "/imgs/model_placeholder.jpg";
        }
        ComfyUIBackendExtension.ComfyCustomWorkflow record = new(prepared.DestinationName, prepared.Workflow,
            prepared.Prompt, prepared.CustomParams, prepared.ParamValues, image, prepared.Description,
            prepared.EnableInSimple);
        JObject data = new()
        {
            ["workflow"] = prepared.ParsedWorkflow,
            ["prompt"] = prepared.ParsedPrompt,
            ["custom_params"] = prepared.ParsedCustomParams,
            ["param_values"] = prepared.ParsedParamValues,
            ["image"] = image,
            ["description"] = prepared.Description ?? "",
            ["enable_in_simple"] = prepared.EnableInSimple
        };
        return (record, data.ToString().EncodeUTF8());
    }
```

This preserves immediate raw submitted strings in the cache and existing durable field order/format.

- [ ] **Step 3: Build and execute the save journal**

Add the shared marker helpers:

```csharp
    /// <summary>Stages the canonical example-deletion marker and records any original marker backup.</summary>
    private static void PrepareMarkerArtifacts(WorkflowTransaction transaction)
    {
        if (!transaction.CreateMarker)
        {
            return;
        }
        string markerPath = GetMarkerPath(transaction.SourceName);
        transaction.MarkerStagePath = CreateArtifactPath(markerPath, transaction.Id, "marker-stage");
        if (transaction.MarkerExisted)
        {
            transaction.MarkerBackupPath = CreateArtifactPath(markerPath, transaction.Id, "marker-backup");
        }
        WriteNewFlushedFile(GetArtifactPath(transaction, transaction.MarkerStagePath, "marker-stage"),
            "deleted-by-user".EncodeUTF8());
    }

    /// <summary>Installs the already-flushed marker stage while preserving any original marker.</summary>
    private static void InstallPreparedMarker(WorkflowTransaction transaction)
    {
        if (!transaction.CreateMarker)
        {
            return;
        }
        string markerPath = GetMarkerPath(transaction.SourceName);
        if (transaction.MarkerExisted)
        {
            File.Move(markerPath, GetArtifactPath(transaction, transaction.MarkerBackupPath, "marker-backup"));
        }
        File.Move(GetArtifactPath(transaction, transaction.MarkerStagePath, "marker-stage"), markerPath);
    }

    /// <summary>Best-effort cleanup for stages created before a prepared journal could be installed.</summary>
    private static void CleanupUnjournaledStages(WorkflowTransaction transaction)
    {
        try
        {
            if (transaction.StagePath is not null)
            {
                string stagePath = GetArtifactPath(transaction, transaction.StagePath, "stage");
                if (File.Exists(stagePath))
                {
                    File.Delete(stagePath);
                }
            }
            if (transaction.MarkerStagePath is not null)
            {
                string markerStagePath = GetArtifactPath(transaction, transaction.MarkerStagePath, "marker-stage");
                if (File.Exists(markerStagePath))
                {
                    File.Delete(markerStagePath);
                }
            }
        }
        catch (Exception)
        {
            Logs.Error("Unjournaled ComfyUI workflow staging cleanup is pending (details redacted).");
        }
    }
```

Add public `SaveWorkflow` and the complete locked transaction:

```csharp
    /// <summary>Validates, durably commits, and publishes a workflow save or replacement.</summary>
    public static void SaveWorkflow(string name, string workflow, string prompt, string customParams,
        string paramValues, string image, string description, bool enableInSimple, string replace)
    {
        PreparedSave prepared = PrepareSave(name, workflow, prompt, customParams, paramValues, image,
            description, enableInSimple, replace);
        lock (WorkflowLock)
        {
            SaveWorkflowLocked(prepared);
        }
    }

    /// <summary>Commits a prepared save while the caller owns <see cref="WorkflowLock"/>.</summary>
    private static void SaveWorkflowLocked(PreparedSave prepared)
    {
        (ComfyUIBackendExtension.ComfyCustomWorkflow record, byte[] data) = CompleteSaveLocked(prepared);
        string destinationPath = GetWorkflowPath(prepared.DestinationName);
        string sourcePath = GetWorkflowPath(prepared.SourceName);
        Directory.CreateDirectory(Path.GetDirectoryName(destinationPath));
        bool sourcePublished = prepared.HasReplacement
            && ComfyUIBackendExtension.CustomWorkflows.ContainsKey(prepared.SourceName);
        bool sourceExisted = sourcePublished && File.Exists(sourcePath);
        bool samePath = PathsEqual(sourcePath, destinationPath);
        bool destinationExisted = File.Exists(destinationPath);
        bool createMarker = sourceExisted && prepared.HasReplacement && IsExampleWorkflow(prepared.SourceName);
        string id = Guid.NewGuid().ToString("N");
        WorkflowTransaction transaction = new()
        {
            Id = id,
            Operation = TransactionOperation.Save,
            Phase = TransactionPhase.Prepared,
            SourceName = prepared.SourceName,
            DestinationName = prepared.DestinationName,
            DestinationHash = GetDataHash(data),
            SourceExisted = sourceExisted,
            DestinationExisted = destinationExisted,
            StagePath = CreateArtifactPath(destinationPath, id, "stage"),
            SourceBackupPath = sourceExisted && !samePath
                ? CreateArtifactPath(sourcePath, id, "source-backup")
                : null,
            DestinationBackupPath = destinationExisted
                ? CreateArtifactPath(destinationPath, id, "destination-backup")
                : null,
            CreateMarker = createMarker,
            MarkerExisted = createMarker && File.Exists(GetMarkerPath(prepared.SourceName))
        };
        WriteNewFlushedFile(GetArtifactPath(transaction, transaction.StagePath, "stage"), data);
        try
        {
            PrepareMarkerArtifacts(transaction);
            WriteJournal(transaction);
        }
        catch
        {
            CleanupUnjournaledStages(transaction);
            throw;
        }
        try
        {
            if (destinationExisted)
            {
                File.Move(destinationPath,
                    GetArtifactPath(transaction, transaction.DestinationBackupPath, "destination-backup"));
            }
            File.Move(GetArtifactPath(transaction, transaction.StagePath, "stage"), destinationPath);
            if (sourceExisted && !samePath)
            {
                File.Move(sourcePath, GetArtifactPath(transaction, transaction.SourceBackupPath, "source-backup"));
            }
            InstallPreparedMarker(transaction);
            transaction.Phase = TransactionPhase.Committed;
            WriteJournal(transaction);
        }
        catch (Exception operationException)
        {
            try
            {
                RollbackPrepared(transaction);
            }
            catch (Exception rollbackException)
            {
                throw new IOException("Workflow save failed and requires journal recovery.",
                    new AggregateException(operationException, rollbackException));
            }
            throw;
        }
        if (prepared.HasReplacement && prepared.SourceName != prepared.DestinationName)
        {
            ComfyUIBackendExtension.CustomWorkflows.TryRemove(prepared.SourceName, out _);
        }
        ComfyUIBackendExtension.CustomWorkflows[prepared.DestinationName] = record;
        try
        {
            CleanupCommitted(transaction);
        }
        catch (Exception)
        {
            Logs.Error("Committed ComfyUI workflow transaction cleanup is pending (details redacted).");
        }
    }

    /// <summary>Whether deleting this cleaned workflow name requires a built-in example marker.</summary>
    private static bool IsExampleWorkflow(string cleanedName)
    {
        return ComfyUIBackendExtension.ExampleWorkflowNames.Contains(cleanedName.After("Examples/") + ".json");
    }
```

The source is eligible for retirement only when an explicit replacement name is currently published and has a file, preserving the existing ignored-error behavior for missing replacements. Destination installation precedes distinct-source retirement, and committed journal installation precedes both dictionary mutations.

Use:

```csharp
        bool samePath = PathsEqual(sourcePath, destinationPath);
```

for filesystem ownership, but use exact workflow-name comparison for dictionary-key removal. This handles case-insensitive Windows paths without leaving the old differently-cased key published.

- [ ] **Step 4: Replace the save route body with store delegation**

Keep the method name, parameters, defaults, permission registration, and `Task<JObject>` return type. Replace its body with:

```csharp
    {
        ComfyWorkflowStore.SaveWorkflow(name, workflow, prompt, custom_params, param_values, image,
            description, enable_in_simple, replace);
        return new JObject() { ["success"] = true };
    }
```

The method may remain `async` for compatibility with neighboring route style even though the storage work is synchronous. Do not call `ComfyDeleteWorkflow` from save.

- [ ] **Step 5: Prove validate-stage-commit-publish ordering and commit**

Run:

```bash
rg -n -C 4 "PrepareSave|ParseObject|FromDataString|WriteNewFlushedFile|TransactionPhase.Prepared|File.Move|TransactionPhase.Committed|CustomWorkflows\\[" src/BuiltinExtensions/ComfyUIBackend/ComfyWorkflowStore.cs
rg -n "ComfyDeleteWorkflow\\(session, replace\\)|File.WriteAllBytes|CustomWorkflows\\[" src/BuiltinExtensions/ComfyUIBackend/ComfyUIWebAPI.cs
git diff --check -- src/BuiltinExtensions/ComfyUIBackend/ComfyWorkflowStore.cs src/BuiltinExtensions/ComfyUIBackend/ComfyUIWebAPI.cs
```

Expected:

- all submitted parsing/conversion precedes the lock and artifact creation;
- stage and prepared journal precede original moves;
- destination installation precedes distinct-source retirement;
- committed journal precedes dictionary mutation;
- the route contains no direct delete, write, or cache publication; and
- whitespace checks are clean.

Review and commit:

```bash
git diff -- src/BuiltinExtensions/ComfyUIBackend/ComfyWorkflowStore.cs src/BuiltinExtensions/ComfyUIBackend/ComfyUIWebAPI.cs
git add -- src/BuiltinExtensions/ComfyUIBackend/ComfyWorkflowStore.cs src/BuiltinExtensions/ComfyUIBackend/ComfyUIWebAPI.cs
git diff --cached --check
git commit -m "fix: commit comfy workflows before publication"
```

### Task 5: Make standalone workflow deletion transactional

**Files:**
- Modify: `src/BuiltinExtensions/ComfyUIBackend/ComfyWorkflowStore.cs`
- Modify: `src/BuiltinExtensions/ComfyUIBackend/ComfyUIWebAPI.cs:128-147`

- [ ] **Step 1: Implement durable delete**

Add:

```csharp
    /// <summary>Durably deletes a workflow before removing its published cache entry.</summary>
    public static bool DeleteWorkflow(string name)
    {
        string cleanedName = Utilities.StrictFilenameClean(name);
        lock (WorkflowLock)
        {
            if (!ComfyUIBackendExtension.CustomWorkflows.ContainsKey(cleanedName))
            {
                return false;
            }
            string sourcePath = GetWorkflowPath(cleanedName);
            if (!File.Exists(sourcePath))
            {
                return false;
            }
            string id = Guid.NewGuid().ToString("N");
            bool createMarker = IsExampleWorkflow(cleanedName);
            WorkflowTransaction transaction = new()
            {
                Id = id,
                Operation = TransactionOperation.Delete,
                Phase = TransactionPhase.Prepared,
                SourceName = cleanedName,
                SourceExisted = true,
                SourceBackupPath = CreateArtifactPath(sourcePath, id, "source-backup"),
                CreateMarker = createMarker,
                MarkerExisted = createMarker && File.Exists(GetMarkerPath(cleanedName))
            };
            try
            {
                PrepareMarkerArtifacts(transaction);
                WriteJournal(transaction);
            }
            catch
            {
                CleanupUnjournaledStages(transaction);
                throw;
            }
            try
            {
                File.Move(sourcePath, GetArtifactPath(transaction, transaction.SourceBackupPath, "source-backup"));
                InstallPreparedMarker(transaction);
                transaction.Phase = TransactionPhase.Committed;
                WriteJournal(transaction);
            }
            catch (Exception operationException)
            {
                try
                {
                    RollbackPrepared(transaction);
                }
                catch (Exception rollbackException)
                {
                    throw new IOException("Workflow deletion failed and requires journal recovery.",
                        new AggregateException(operationException, rollbackException));
                }
                throw;
            }
            ComfyUIBackendExtension.CustomWorkflows.TryRemove(cleanedName, out _);
            try
            {
                CleanupCommitted(transaction);
            }
            catch (Exception)
            {
                Logs.Error("Committed ComfyUI workflow deletion cleanup is pending (details redacted).");
            }
            return true;
        }
    }
```

Reuse the marker helpers introduced with save. Both return immediately when `CreateMarker` is false, keeping save and delete on one exact marker protocol.

- [ ] **Step 2: Delegate the delete route without changing responses**

Replace the delete body with:

```csharp
    {
        if (!ComfyWorkflowStore.DeleteWorkflow(name))
        {
            return new JObject() { ["error"] = "Unknown custom workflow name." };
        }
        return new JObject() { ["success"] = true };
    }
```

Keep the existing route signature and permissions. A cache entry whose file is missing now remains published when the route returns the existing error, satisfying the failed-operation no-change invariant.

- [ ] **Step 3: Prove every maintained mutation is store-owned**

Run:

```bash
rg -n "CustomWorkflows\\.(Clear|TryAdd|TryRemove|Remove)|CustomWorkflows\\[" src/BuiltinExtensions/ComfyUIBackend --glob '*.cs'
rg -n "File\\.(WriteAllBytes|WriteAllText|Delete|Move).*CustomWorkflows|CustomWorkflows.*File\\." src/BuiltinExtensions/ComfyUIBackend --glob '*.cs'
rg -n -C 4 "DeleteWorkflow|TransactionPhase.Prepared|TransactionPhase.Committed|TryRemove" src/BuiltinExtensions/ComfyUIBackend/ComfyWorkflowStore.cs src/BuiltinExtensions/ComfyUIBackend/ComfyUIWebAPI.cs
git diff --check -- src/BuiltinExtensions/ComfyUIBackend/ComfyWorkflowStore.cs src/BuiltinExtensions/ComfyUIBackend/ComfyUIWebAPI.cs
```

Expected:

- every maintained dictionary mutation is inside `ComfyWorkflowStore`;
- workflow transaction file writes/moves/deletes are store-owned;
- committed journal installation precedes `TryRemove`;
- the route has no direct file/cache mutation; and
- whitespace checks are clean.

Review and commit:

```bash
git diff -- src/BuiltinExtensions/ComfyUIBackend/ComfyWorkflowStore.cs src/BuiltinExtensions/ComfyUIBackend/ComfyUIWebAPI.cs
git add -- src/BuiltinExtensions/ComfyUIBackend/ComfyWorkflowStore.cs src/BuiltinExtensions/ComfyUIBackend/ComfyUIWebAPI.cs
git diff --cached --check
git commit -m "fix: make comfy workflow deletion durable"
```

### Task 6: Perform the complete static protocol review

**Files:**
- Inspect: `src/BuiltinExtensions/ComfyUIBackend/ComfyWorkflowStore.cs`
- Inspect: `src/BuiltinExtensions/ComfyUIBackend/ComfyUIBackendExtension.cs`
- Inspect: `src/BuiltinExtensions/ComfyUIBackend/ComfyUIWebAPI.cs`
- Modify: `docs/superpowers/specs/2026-07-23-durable-comfy-workflow-transactions-design.md`

- [ ] **Step 1: Trace every maintained reader and writer**

Run:

```bash
rg -n "CustomWorkflows|GetWorkflowByName|LoadWorkflowFiles|ComfySaveWorkflow|ComfyDeleteWorkflow|ReadCustomWorkflow|ComfyListWorkflows" src --glob '*.cs' --glob '!bin/**' --glob '!obj/**'
rg -n "GetRawWorkflowFrom|CustomWorkflowParam" src/BuiltinExtensions/ComfyUIBackend --glob '*.cs'
```

Expected: all core maintained workflow storage access is either inside the store or passes through an extension/API facade that delegates to it. Record and correct any missed direct maintained access before continuing.

- [ ] **Step 2: Review the transaction state table**

For each operation, trace these states in the source:

| Operation | Before `Prepared` | Prepared rollback owns | Committed authority | Cache publication |
|---|---|---|---|---|
| New save | no destination | stage/new destination | destination JSON | add destination |
| Overwrite | old destination | destination backup + stage | new destination JSON | replace destination |
| A → B | A and optional old B | A backup, B backup, stage, marker | new B + retired A | remove A/add B |
| Same-name replace | old destination | one destination backup, stage, marker | new destination | replace one key |
| Delete | source | source backup + marker | absent source + marker | remove source |

Confirm that every pre-commit exception leaves a `Prepared` recovery path, every original has only one owner, and cleanup deletes the journal last.

- [ ] **Step 3: Review platform and confidentiality rules**

Run:

```bash
rg -n "File\\.Replace|Flush\\(true\\)|File\\.Move|Path\\.GetFullPath|Path\\.GetRelativePath|Path\\.IsPathRooted" src/BuiltinExtensions/ComfyUIBackend/ComfyWorkflowStore.cs
rg -n "Logs\\.(Error|Warning|Info).*\\$|ReadableString|\\.Message|workflow.*content|journal.*content" src/BuiltinExtensions/ComfyUIBackend/ComfyWorkflowStore.cs
rg -n "var |\\}\\s*else\\s*\\{|if \\([^\\n]+\\) [^{]" src/BuiltinExtensions/ComfyUIBackend/ComfyWorkflowStore.cs
```

Expected:

- no `File.Replace`;
- staged data, marker, and journal images use `Flush(true)`;
- every journal path is normalized and contained;
- diagnostics are fixed/redacted and do not interpolate submitted content, raw paths, or exception text;
- no `var`, same-line `else`, or unbraced conditional is introduced.

- [ ] **Step 4: Review exact external compatibility**

Compare the staged implementation with the pre-rank-6 route/file behavior:

```bash
git show b417ace9^:src/BuiltinExtensions/ComfyUIBackend/ComfyUIWebAPI.cs | sed -n '35,147p'
git diff b417ace9..HEAD -- src/BuiltinExtensions/ComfyUIBackend/ComfyUIWebAPI.cs src/BuiltinExtensions/ComfyUIBackend/ComfyUIBackendExtension.cs
```

Confirm unchanged:

- route registrations, signatures, defaults, permissions, success and unknown-name responses;
- strict cleaned names and missing-`replace` continuation;
- JSON field order/format and image behavior;
- public `CustomWorkflows` declaration/object identity;
- public extension facade names;
- example-name comparison;
- lazy record normalization and fixed invalid-file diagnostic.

- [ ] **Step 5: Run permitted mechanical verification**

Run:

```bash
git diff --check b417ace9..HEAD
git status --short --untracked-files=no
git log --oneline --decorate -8
```

Expected: no whitespace errors; only the four pre-existing user files are dirty; the production commits are narrow and ordered after the approved design.

- [ ] **Step 6: Record implementation awaiting maintainer validation**

Change the design status to:

```markdown
**Status:** Implemented; awaiting maintainer validation
```

Append an implementation note containing the exact production commit range and stating that static verification passed while builds/tests/runtime execution remain maintainer-owned.

Commit only the design document:

```bash
git add -- docs/superpowers/specs/2026-07-23-durable-comfy-workflow-transactions-design.md
git diff --cached --check
git commit -m "docs: record durable workflow implementation"
```

### Task 7: Maintainer runtime validation

**Files:**
- No agent edits until the maintainer reports results.

- [ ] **Step 1: Ask the maintainer to run the normal build and launch workflow**

The agent does not run this command. Maintainer Reaper176 runs the repository's normal workflow and reports whether build, launch, and ordinary Comfy workflow browsing succeed.

- [ ] **Step 2: Validate ordinary save/delete compatibility**

Maintainer confirms:

1. new workflow save;
2. ordinary same-name overwrite;
3. explicit same-name replacement;
4. cross-name rename;
5. rename onto an existing destination;
6. replacement naming a missing predecessor;
7. standalone delete;
8. read/list/browser refresh after each operation; and
9. generation using the saved workflow.

Expected: responses and UI behavior match the prior contract, while refresh/restart retains every successful result.

- [ ] **Step 3: Validate submitted-data and image failures**

Maintainer submits invalid JSON independently for `workflow`, `prompt`, `custom_params`, and `param_values`, plus invalid image data.

Expected: each request fails without changing source/destination files, markers, list results, or generation behavior.

Maintainer also confirms image inheritance, image replacement, explicit clear, and placeholder fallback.

- [ ] **Step 4: Validate example marker transactions**

Using disposable example-workflow copies, maintainer confirms example deletion, same-name replacement, cross-name rename, refresh, and restart.

Expected: `.deleted` markers match committed operations; a failed operation restores both prior workflow and prior marker state.

- [ ] **Step 5: Validate persistence failures**

On disposable workflow names/storage, maintainer causes failure at the available filesystem boundaries: staging, journal creation, destination installation, predecessor retirement, marker installation, commit-journal replacement, and cleanup.

Expected:

- before commit, the API fails and prior source/destination/marker/cache state remains;
- after commit, cleanup failure is logged but success remains durable and visible;
- no workflow content or sensitive path appears in client errors/logs.

- [ ] **Step 6: Validate concurrent maintained readers**

While repeatedly saving, renaming, and deleting disposable workflows, maintainer exercises list, read, parameter selection, and generation.

Expected: each reader sees a complete pre-state or complete post-state, never a memory-only destination or a partial A-to-B rename.

- [ ] **Step 7: Validate interruption recovery**

With disposable workflows, interrupt the process after installing a `Prepared` journal and after installing a `Committed` journal, then relaunch or refresh.

Expected:

- `Prepared` restores the complete pre-state;
- `Committed` retains the complete post-state and cleans verified artifacts;
- a malformed/path-unsafe disposable journal logs a redacted recovery error and does not clear the previously published map or delete user files.

- [ ] **Step 8: Record platform coverage honestly**

Record Linux results and Windows results when available. If Windows is unavailable, state that Windows received static path/file-operation review only; do not claim runtime coverage.

Do not continue to Task 8 until maintainer Reaper176 explicitly confirms the agreed validation result.

### Task 8: Reconcile status and advance the roadmap

**Files:**
- Modify: `docs/superpowers/specs/2026-07-23-durable-comfy-workflow-transactions-design.md`
- Modify: `docs/superpowers/audits/2026-07-21-maintainability-architecture-refresh.md`

- [ ] **Step 1: Mark the design maintainer-validated**

Change the design status to:

```markdown
**Status:** Implemented and maintainer-validated
```

Update the implementation note with the maintainer-confirmed matrix and exact platform coverage. Do not imply Windows runtime validation if only static review occurred.

- [ ] **Step 2: Reconcile Comfy F23 and rank 6**

In the audit:

- mark Comfy F23 mitigated by the committed workflow-store transaction protocol;
- add `**Implementation status:** **Implemented and maintainer-validated.**` to rank 6;
- state that store delegation, read coordination, journal/recovery, durable commit, marker transaction, and post-commit cache publication are one rollback unit; and
- retain Comfy P8 as a separate measurement prerequisite with no performance claim.

- [ ] **Step 3: Advance Recommended Next Project to rank 7**

Replace the rank-6 Recommended Next section with the existing rank-7 roadmap entry, preserving its wording and evidence:

```markdown
### 7. Publish runtime model catalogs under one refresh boundary

- **Boundary and owner:** `Program.T2IModelSets`, `BuildModelLists`, `RefreshAllModelSets`, `AdminAPI.ChangeServerSettings`, and `RefreshLock`. **Evidence/consumers:** Core F7 enumerates 16 maintained consumer files across APIs, parameters, validation, workflows, remote serialization, metadata, downloads, and viewing.
- **Payoff:** high generation/model reliability and strong catalog ownership; no performance claim. Leverage very high, feasibility medium, concurrency regression risk high. **Prerequisites:** preserve keys, handler identities, synchronous settings behavior, refresh events, and old-handler lifetime.
- **Stages/non-goals:** acquire the existing write boundary for mutation, cover maintained readers, then evaluate snapshot replacement; do not redesign model types/extensions. **Verification/validation:** prove every named access is protected and edit roots during listing, browsing, conversion, workflow creation, metadata/download, remote serialization, and refresh. **Success:** no partial publication or premature handler shutdown. **Rollback:** revert per-stage claims/snapshot publication.
```

- [ ] **Step 4: Verify documentation consistency**

Run:

```bash
rg -n -C 3 "Comfy F23|### 6\\.|### 7\\.|Recommended Next Project|Implementation status|P8" docs/superpowers/audits/2026-07-21-maintainability-architecture-refresh.md docs/superpowers/specs/2026-07-23-durable-comfy-workflow-transactions-design.md
git diff --check -- docs/superpowers/audits/2026-07-21-maintainability-architecture-refresh.md docs/superpowers/specs/2026-07-23-durable-comfy-workflow-transactions-design.md
```

Expected: rank 6 and F23 have one consistent validated status, rank 7 appears once as Recommended Next, P8 remains measurement-only, and whitespace checks are clean.

- [ ] **Step 5: Commit the validation record**

```bash
git diff -- docs/superpowers/specs/2026-07-23-durable-comfy-workflow-transactions-design.md docs/superpowers/audits/2026-07-21-maintainability-architecture-refresh.md
git add -- docs/superpowers/specs/2026-07-23-durable-comfy-workflow-transactions-design.md docs/superpowers/audits/2026-07-21-maintainability-architecture-refresh.md
git diff --cached --check
git commit -m "docs: record durable workflow validation"
```

Expected: the commit contains only the design and audit status updates.

## Final Static Reconciliation

After Task 8, run:

```bash
git diff --check b417ace9..HEAD
git status --short --untracked-files=no
git log --oneline --decorate -12
```

Expected:

- the complete rank-6 range has no whitespace errors;
- only the four pre-existing user-owned files remain dirty;
- the public workflow schema/routes/dictionary identity are unchanged;
- implementation and validation documentation agree; and
- no build, test, launcher, server, browser, backend, installer, or live storage command was run by the agent.
