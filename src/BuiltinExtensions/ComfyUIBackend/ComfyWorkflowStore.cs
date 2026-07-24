using FreneticUtilities.FreneticExtensions;
using FreneticUtilities.FreneticToolkit;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using SwarmUI.Utils;
using System.IO;

namespace SwarmUI.Builtin_ComfyUIBackend;

/// <summary>Coordinates access to stored ComfyUI workflows.</summary>
public static class ComfyWorkflowStore
{
    /// <summary>Current workflow transaction journal format version.</summary>
    private const int JournalVersion = 1;

    /// <summary>Reserved workflow transaction journal filename.</summary>
    private const string JournalFileName = ".swarm-workflow-transaction";

    /// <summary>Synchronizes workflow inventory and read operations.</summary>
    private static readonly object WorkflowLock = new();

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

    /// <summary>Validated inputs prepared for a workflow save.</summary>
    private sealed record PreparedSave(
        string DestinationName,
        string SourceName,
        bool HasReplacement,
        string Workflow,
        string Prompt,
        string CustomParams,
        string ParamValues,
        string SuppliedImage,
        string InheritImage,
        string Description,
        bool EnableInSimple,
        JObject ParsedWorkflow,
        JObject ParsedPrompt,
        JObject ParsedCustomParams,
        JObject ParsedParamValues);

    /// <summary>Content-free durable metadata for a workflow filesystem transaction.</summary>
    private sealed class WorkflowTransaction
    {
        public int Version { get; set; } = JournalVersion;

        public Guid Id { get; set; }

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

    /// <summary>Gets the normalized root directory for custom workflows.</summary>
    private static string GetWorkflowRoot()
    {
        return Path.TrimEndingDirectorySeparator(Path.GetFullPath(Path.Combine(ComfyUIBackendExtension.Folder, "CustomWorkflows")));
    }

    /// <summary>Gets a contained workflow path for an already-cleaned workflow name.</summary>
    private static string GetWorkflowPath(string cleanedName)
    {
        return GetContainedPath($"{cleanedName}.json");
    }

    /// <summary>Gets a contained deletion-marker path for an already-cleaned workflow name.</summary>
    private static string GetMarkerPath(string cleanedName)
    {
        return $"{GetWorkflowPath(cleanedName)}.deleted";
    }

    /// <summary>Resolves a relative path and requires it to remain beneath the workflow root.</summary>
    private static string GetContainedPath(string relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath) || Path.IsPathRooted(relativePath))
        {
            throw new InvalidDataException("Invalid workflow transaction path.");
        }
        string root = GetWorkflowRoot();
        string fullPath = Path.GetFullPath(Path.Combine(root, relativePath));
        StringComparison comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        string rootPrefix = $"{root}{Path.DirectorySeparatorChar}";
        if (!fullPath.StartsWith(rootPrefix, comparison))
        {
            throw new InvalidDataException("Invalid workflow transaction path.");
        }
        return fullPath;
    }

    /// <summary>Creates the relative path for a reserved transaction artifact beside a final path.</summary>
    private static string CreateArtifactPath(string finalPath, Guid id, string role)
    {
        string root = GetWorkflowRoot();
        string normalizedFinalPath = Path.GetFullPath(finalPath);
        string containedFinalPath = GetContainedPath(Path.GetRelativePath(root, normalizedFinalPath));
        if (!PathsEqual(normalizedFinalPath, containedFinalPath))
        {
            throw new InvalidDataException("Invalid workflow transaction path.");
        }
        string artifactPath = Path.Combine(Path.GetDirectoryName(containedFinalPath), $".swarm-workflow-{id}-{role}");
        string containedArtifactPath = GetContainedPath(Path.GetRelativePath(root, artifactPath));
        return Path.GetRelativePath(root, containedArtifactPath);
    }

    /// <summary>Validates and resolves a transaction artifact path for its expected role.</summary>
    private static string GetArtifactPath(WorkflowTransaction transaction, string relativePath, string finalPath, string role)
    {
        if (string.IsNullOrWhiteSpace(relativePath))
        {
            throw new InvalidDataException("Invalid workflow transaction artifact path.");
        }
        string expectedName = $".swarm-workflow-{transaction.Id}-{role}";
        if (!string.Equals(Path.GetFileName(relativePath), expectedName, StringComparison.Ordinal))
        {
            throw new InvalidDataException("Invalid workflow transaction artifact path.");
        }
        string artifactPath = GetContainedPath(relativePath);
        string expectedPath = Path.Combine(Path.GetDirectoryName(finalPath), expectedName);
        if (!PathsEqual(artifactPath, expectedPath))
        {
            throw new InvalidDataException("Invalid workflow transaction artifact path.");
        }
        return artifactPath;
    }

    /// <summary>Checks whether two normalized filesystem paths identify the same path.</summary>
    private static bool PathsEqual(string firstPath, string secondPath)
    {
        string normalizedFirst = Path.TrimEndingDirectorySeparator(Path.GetFullPath(firstPath));
        string normalizedSecond = Path.TrimEndingDirectorySeparator(Path.GetFullPath(secondPath));
        StringComparison comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        return string.Equals(normalizedFirst, normalizedSecond, comparison);
    }

    /// <summary>Creates, fully writes, and durably flushes a new file.</summary>
    private static void WriteNewFlushedFile(string path, byte[] data)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path));
        using FileStream stream = new(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        stream.Write(data);
        stream.Flush(true);
    }

    /// <summary>Serializes content-free workflow transaction metadata.</summary>
    private static byte[] SerializeTransaction(WorkflowTransaction transaction)
    {
        JObject json = new()
        {
            ["version"] = transaction.Version,
            ["id"] = transaction.Id.ToString("N"),
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
        return json.ToString().EncodeUTF8();
    }

    /// <summary>Atomically replaces the active journal with fully flushed transaction metadata.</summary>
    private static void WriteJournal(WorkflowTransaction transaction)
    {
        string journalPath = GetContainedPath(JournalFileName);
        string temporaryPath = GetContainedPath($"{JournalFileName}-{transaction.Id}-new");
        if (transaction.Phase == TransactionPhase.Prepared && File.Exists(journalPath))
        {
            throw new InvalidOperationException("A workflow transaction journal is already active.");
        }
        if (File.Exists(temporaryPath))
        {
            throw new InvalidOperationException("A workflow transaction journal temporary file already exists.");
        }
        try
        {
            WriteNewFlushedFile(temporaryPath, SerializeTransaction(transaction));
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

    /// <summary>Reads and strictly validates a workflow transaction journal.</summary>
    private static WorkflowTransaction ReadJournal(string path)
    {
        try
        {
            JObject json = ComfySubmittedJson.ParseObject(File.ReadAllText(path));
            if (json.Count != 16)
            {
                throw new InvalidDataException("Invalid workflow transaction journal.");
            }
            JToken requireScalar(string key, JTokenType type)
            {
                if (!json.TryGetValue(key, out JToken token) || token.Type != type)
                {
                    throw new InvalidDataException("Invalid workflow transaction journal.");
                }
                return token;
            }
            string requireString(string key)
            {
                return requireScalar(key, JTokenType.String).Value<string>();
            }
            string optionalString(string key)
            {
                if (!json.TryGetValue(key, out JToken token) || (token.Type != JTokenType.String && token.Type != JTokenType.Null))
                {
                    throw new InvalidDataException("Invalid workflow transaction journal.");
                }
                return token.Type == JTokenType.Null ? null : token.Value<string>();
            }
            int version = requireScalar("version", JTokenType.Integer).Value<int>();
            string idText = requireString("id");
            if (version != JournalVersion || !Guid.TryParseExact(idText, "N", out Guid id))
            {
                throw new InvalidDataException("Invalid workflow transaction journal.");
            }
            string operationText = requireString("operation");
            if (!Enum.TryParse(operationText, false, out TransactionOperation operation) || !Enum.IsDefined(typeof(TransactionOperation), operation)
                || !string.Equals(operation.ToString(), operationText, StringComparison.Ordinal))
            {
                throw new InvalidDataException("Invalid workflow transaction journal.");
            }
            string phaseText = requireString("phase");
            if (!Enum.TryParse(phaseText, false, out TransactionPhase phase) || !Enum.IsDefined(typeof(TransactionPhase), phase)
                || !string.Equals(phase.ToString(), phaseText, StringComparison.Ordinal))
            {
                throw new InvalidDataException("Invalid workflow transaction journal.");
            }
            WorkflowTransaction transaction = new()
            {
                Version = version,
                Id = id,
                Operation = operation,
                Phase = phase,
                SourceName = optionalString("source_name"),
                DestinationName = optionalString("destination_name"),
                DestinationHash = optionalString("destination_hash"),
                StagePath = optionalString("stage_path"),
                SourceBackupPath = optionalString("source_backup_path"),
                DestinationBackupPath = optionalString("destination_backup_path"),
                MarkerStagePath = optionalString("marker_stage_path"),
                MarkerBackupPath = optionalString("marker_backup_path"),
                SourceExisted = requireScalar("source_existed", JTokenType.Boolean).Value<bool>(),
                DestinationExisted = requireScalar("destination_existed", JTokenType.Boolean).Value<bool>(),
                MarkerExisted = requireScalar("marker_existed", JTokenType.Boolean).Value<bool>(),
                CreateMarker = requireScalar("create_marker", JTokenType.Boolean).Value<bool>()
            };
            void validateWorkflowName(string name)
            {
                if (name is not null && !string.Equals(Utilities.StrictFilenameClean(name), name, StringComparison.Ordinal))
                {
                    throw new InvalidDataException("Invalid workflow transaction journal.");
                }
            }
            validateWorkflowName(transaction.SourceName);
            validateWorkflowName(transaction.DestinationName);
            if (transaction.Operation == TransactionOperation.Save)
            {
                if (transaction.SourceName is null || transaction.DestinationName is null || transaction.StagePath is null || !IsSha256(transaction.DestinationHash))
                {
                    throw new InvalidDataException("Invalid workflow transaction journal.");
                }
                string sourcePath = GetWorkflowPath(transaction.SourceName);
                string destinationPath = GetWorkflowPath(transaction.DestinationName);
                GetArtifactPath(transaction, transaction.StagePath, destinationPath, "stage");
                if (transaction.DestinationExisted != (transaction.DestinationBackupPath is not null))
                {
                    throw new InvalidDataException("Invalid workflow transaction journal.");
                }
                if (transaction.DestinationBackupPath is not null)
                {
                    GetArtifactPath(transaction, transaction.DestinationBackupPath, destinationPath, "destination-backup");
                }
                bool sourceBackupRequired = transaction.SourceExisted && !PathsEqual(sourcePath, destinationPath);
                if (sourceBackupRequired != (transaction.SourceBackupPath is not null))
                {
                    throw new InvalidDataException("Invalid workflow transaction journal.");
                }
                if (transaction.SourceBackupPath is not null)
                {
                    GetArtifactPath(transaction, transaction.SourceBackupPath, sourcePath, "source-backup");
                }
            }
            else
            {
                if (transaction.SourceName is null || !transaction.SourceExisted || transaction.SourceBackupPath is null
                    || transaction.DestinationName is not null || transaction.DestinationHash is not null || transaction.StagePath is not null
                    || transaction.DestinationBackupPath is not null || transaction.DestinationExisted)
                {
                    throw new InvalidDataException("Invalid workflow transaction journal.");
                }
                string sourcePath = GetWorkflowPath(transaction.SourceName);
                GetArtifactPath(transaction, transaction.SourceBackupPath, sourcePath, "source-backup");
            }
            if (transaction.CreateMarker)
            {
                if (transaction.SourceName is null || transaction.MarkerStagePath is null || transaction.MarkerExisted != (transaction.MarkerBackupPath is not null))
                {
                    throw new InvalidDataException("Invalid workflow transaction journal.");
                }
                string markerPath = GetMarkerPath(transaction.SourceName);
                GetArtifactPath(transaction, transaction.MarkerStagePath, markerPath, "marker-stage");
                if (transaction.MarkerBackupPath is not null)
                {
                    GetArtifactPath(transaction, transaction.MarkerBackupPath, markerPath, "marker-backup");
                }
            }
            else if (transaction.MarkerStagePath is not null || transaction.MarkerBackupPath is not null || transaction.MarkerExisted)
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

    /// <summary>Checks whether a string is a canonical lowercase SHA-256 value.</summary>
    private static bool IsSha256(string value)
    {
        if (value is null || value.Length != 64)
        {
            return false;
        }
        foreach (char character in value)
        {
            if ((character < '0' || character > '9') && (character < 'a' || character > 'f'))
            {
                return false;
            }
        }
        return true;
    }

    /// <summary>Loads the available workflow files from the extension directory.</summary>
    public static void LoadWorkflowFiles(string extensionFilePath)
    {
        lock (WorkflowLock)
        {
            Directory.CreateDirectory($"{extensionFilePath}CustomWorkflows");
            Directory.CreateDirectory($"{extensionFilePath}CustomWorkflows/Examples");
            string[] getCustomFlows(string path) => [.. Directory.EnumerateFiles($"{extensionFilePath}/{path}", "*.*", new EnumerationOptions() { RecurseSubdirectories = true }).Select(f => f.Replace('\\', '/').After($"/{path}/")).Order()];
            ComfyUIBackendExtension.ExampleWorkflowNames = getCustomFlows("ExampleWorkflows");
            string[] customFlows = getCustomFlows("CustomWorkflows");
            bool anyCopied = false;
            foreach (string workflow in ComfyUIBackendExtension.ExampleWorkflowNames.Where(f => f.EndsWith(".json")))
            {
                if (!customFlows.Contains($"Examples/{workflow}") && !customFlows.Contains($"Examples/{workflow}.deleted"))
                {
                    File.Copy($"{extensionFilePath}ExampleWorkflows/{workflow}", $"{extensionFilePath}CustomWorkflows/Examples/{workflow}");
                    anyCopied = true;
                }
            }
            if (anyCopied)
            {
                customFlows = getCustomFlows("CustomWorkflows");
            }
            ComfyUIBackendExtension.CustomWorkflows.Clear();
            foreach (string workflow in customFlows.Where(f => f.EndsWith(".json")))
            {
                ComfyUIBackendExtension.CustomWorkflows.TryAdd(workflow.BeforeLast('.'), null);
            }
        }
    }

    /// <summary>Gets a workflow by its stored name.</summary>
    public static ComfyUIBackendExtension.ComfyCustomWorkflow GetWorkflowByName(string name)
    {
        lock (WorkflowLock)
        {
            return GetWorkflowByNameLocked(name);
        }
    }

    /// <summary>Gets a workflow by its stored name while the workflow lock is held.</summary>
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
        string path = $"{ComfyUIBackendExtension.Folder}/CustomWorkflows/{name}.json";
        if (!File.Exists(path))
        {
            ComfyUIBackendExtension.CustomWorkflows.TryRemove(name, out _);
            return null;
        }
        try
        {
            JObject json = ComfySubmittedJson.ParseObject(File.ReadAllText(path));
            string getStringFor(string key)
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
            string workflowData = getStringFor("workflow");
            string prompt = getStringFor("prompt");
            string customParams = getStringFor("custom_params");
            string paramValues = getStringFor("param_values");
            string image = getStringFor("image") ?? "/imgs/model_placeholder.jpg";
            string description = getStringFor("description");
            bool enableInSimple = json.TryGetValue("enable_in_simple", out JToken enableInSimpleTok) && enableInSimpleTok.ToObject<bool>();
            workflow = new(name, workflowData, prompt, customParams, paramValues, image, description, enableInSimple);
            ComfyUIBackendExtension.CustomWorkflows[name] = workflow;
            return workflow;
        }
        catch (Exception)
        {
            Logs.Error("Error loading ComfyUI custom workflow (submitted content redacted).");
            return null;
        }
    }

    /// <summary>Gets a hydrated snapshot of all currently known workflows.</summary>
    public static List<ComfyUIBackendExtension.ComfyCustomWorkflow> GetWorkflowSnapshot()
    {
        lock (WorkflowLock)
        {
            List<string> names = ComfyUIBackendExtension.CustomWorkflows.Keys.ToList();
            List<ComfyUIBackendExtension.ComfyCustomWorkflow> workflows = [];
            foreach (string name in names)
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

    /// <summary>Gets a sorted snapshot of all known workflow names.</summary>
    public static string[] GetWorkflowNames()
    {
        lock (WorkflowLock)
        {
            return [.. ComfyUIBackendExtension.CustomWorkflows.Keys.Order()];
        }
    }

    /// <summary>Gets a workflow prompt while distinguishing unknown names from unloaded workflows.</summary>
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
