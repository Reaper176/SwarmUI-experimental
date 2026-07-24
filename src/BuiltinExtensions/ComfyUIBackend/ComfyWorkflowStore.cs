using FreneticUtilities.FreneticExtensions;
using FreneticUtilities.FreneticToolkit;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using SwarmUI.Media;
using SwarmUI.Utils;
using System.Collections.Concurrent;
using System.IO;
using System.Security.Cryptography;

namespace SwarmUI.Builtin_ComfyUIBackend;

/// <summary>Coordinates access to stored ComfyUI workflows.</summary>
public static class ComfyWorkflowStore
{
    /// <summary>Current workflow transaction journal format version.</summary>
    private const int JournalVersion = 1;

    /// <summary>Reserved workflow transaction journal filename.</summary>
    private const string JournalFileName = ".swarm-workflow-transaction";

    /// <summary>Canonical content for a workflow deletion marker.</summary>
    private const string DeletedMarkerContent = "deleted-by-user";

    /// <summary>Serializes workflow transactions, recovery, inventory, hydration, publication, and maintained readers.</summary>
    private static readonly object WorkflowLock = new();

    /// <summary>Whether maintained workflow access is blocked pending recovery and refresh. Direct public dictionary access is a compatibility limit and cannot be gated.</summary>
    private static bool RecoveryRequired = true;

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
        bool InheritImage,
        string Description,
        bool EnableInSimple,
        JObject ParsedWorkflow,
        JObject ParsedPrompt,
        JObject ParsedCustomParams,
        JObject ParsedParamValues);

    /// <summary>Validates and parses submitted workflow data before taking the workflow lock.</summary>
    private static PreparedSave PrepareSave(string name, string workflow, string prompt, string customParams, string paramValues, string image, string description, bool enableInSimple, string replace)
    {
        string destinationName = Utilities.StrictFilenameClean(name);
        bool hasReplacement = !string.IsNullOrWhiteSpace(replace);
        string sourceName = hasReplacement ? Utilities.StrictFilenameClean(replace) : destinationName;
        JObject parsedWorkflow = ComfySubmittedJson.ParseObject(workflow);
        JObject parsedPrompt = ComfySubmittedJson.ParseObject(prompt);
        JObject parsedCustomParams = ComfySubmittedJson.ParseObject(customParams);
        JObject parsedParamValues = ComfySubmittedJson.ParseObject(paramValues);
        bool inheritImage = string.IsNullOrWhiteSpace(image);
        string suppliedImage = null;
        if (!inheritImage)
        {
            suppliedImage = image == "clear" ? null : ImageFile.FromDataString(image).ToMetadataFormat();
        }
        return new(destinationName, sourceName, hasReplacement, workflow, prompt, customParams, paramValues, suppliedImage, inheritImage,
            description, enableInSimple, parsedWorkflow, parsedPrompt, parsedCustomParams, parsedParamValues);
    }

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

    /// <summary>Rejects maintained workflow access while recovery and refresh are required.</summary>
    private static void EnsureRecoveryReadyLocked()
    {
        if (RecoveryRequired)
        {
            throw new InvalidDataException("Stored workflows are unavailable pending safe recovery.");
        }
    }

    /// <summary>Marks maintained workflow access as requiring recovery and refresh.</summary>
    private static void MarkRecoveryRequiredLocked()
    {
        RecoveryRequired = true;
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
        EnsureNoReparsePointAncestors(root, fullPath);
        return fullPath;
    }

    /// <summary>Gets path attributes while distinguishing true absence from filesystem access failures.</summary>
    private static bool TryGetAttributesStrict(string path, out FileAttributes attributes)
    {
        try
        {
            attributes = File.GetAttributes(path);
            return true;
        }
        catch (FileNotFoundException)
        {
            attributes = default;
            return false;
        }
        catch (DirectoryNotFoundException)
        {
            attributes = default;
            return false;
        }
    }

    /// <summary>Checks for a file without treating filesystem errors or directories as absence.</summary>
    private static bool FileExistsStrict(string path)
    {
        if (!TryGetAttributesStrict(path, out FileAttributes attributes))
        {
            return false;
        }
        if ((attributes & FileAttributes.Directory) != 0)
        {
            throw new IOException("Stored workflow transaction expected a file.");
        }
        return true;
    }

    /// <summary>Checks for a directory and returns its attributes without hiding filesystem errors.</summary>
    private static bool DirectoryExistsStrict(string path, out FileAttributes attributes)
    {
        if (!TryGetAttributesStrict(path, out attributes))
        {
            return false;
        }
        if ((attributes & FileAttributes.Directory) == 0)
        {
            throw new IOException("Stored workflow transaction expected a directory.");
        }
        return true;
    }

    /// <summary>Rejects existing reparse-point directories beneath the trusted workflow root.</summary>
    private static void EnsureNoReparsePointAncestors(string root, string fullPath)
    {
        StringComparison comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        string rootPrefix = $"{root}{Path.DirectorySeparatorChar}";
        string currentPath = Path.GetDirectoryName(fullPath);
        while (currentPath is not null && !PathsEqual(currentPath, root))
        {
            if (!currentPath.StartsWith(rootPrefix, comparison))
            {
                throw new InvalidDataException("Invalid workflow transaction path.");
            }
            if (DirectoryExistsStrict(currentPath, out FileAttributes attributes) && (attributes & FileAttributes.ReparsePoint) != 0)
            {
                throw new InvalidDataException("Invalid workflow transaction path.");
            }
            string parentPath = Path.GetDirectoryName(currentPath);
            if (parentPath is null || PathsEqual(parentPath, currentPath))
            {
                throw new InvalidDataException("Invalid workflow transaction path.");
            }
            currentPath = parentPath;
        }
        if (currentPath is null)
        {
            throw new InvalidDataException("Invalid workflow transaction path.");
        }
    }

    /// <summary>Revalidates that a full path still resolves to its canonical location beneath the workflow root.</summary>
    private static string RevalidateContainedPath(string fullPath)
    {
        // WorkflowLock coordinates maintained operations; portable Path APIs cannot make external adversarial replacement atomic.
        string root = GetWorkflowRoot();
        string normalizedPath = Path.GetFullPath(fullPath);
        string containedPath = GetContainedPath(Path.GetRelativePath(root, normalizedPath));
        if (!PathsEqual(normalizedPath, containedPath))
        {
            throw new InvalidDataException("Invalid workflow transaction path.");
        }
        return containedPath;
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
        string canonicalRelativePath = Path.GetRelativePath(GetWorkflowRoot(), expectedPath);
        if (!string.Equals(relativePath, canonicalRelativePath, StringComparison.Ordinal))
        {
            throw new InvalidDataException("Invalid workflow transaction artifact path.");
        }
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
        path = RevalidateContainedPath(path);
        Directory.CreateDirectory(Path.GetDirectoryName(path));
        path = RevalidateContainedPath(path);
        FileStream stream = new(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        try
        {
            stream.Write(data);
            stream.Flush(true);
        }
        catch
        {
            try
            {
                stream.Dispose();
            }
            catch (Exception)
            {
            }
            try
            {
                File.Delete(RevalidateContainedPath(path));
            }
            catch (Exception)
            {
            }
            throw;
        }
        stream.Dispose();
    }

    /// <summary>Completes a prepared save using workflow state protected by the workflow lock.</summary>
    private static (ComfyUIBackendExtension.ComfyCustomWorkflow Record, byte[] Data) CompleteSaveLocked(PreparedSave preparedSave)
    {
        string image = preparedSave.SuppliedImage;
        if (preparedSave.InheritImage && ComfyUIBackendExtension.CustomWorkflows.ContainsKey(preparedSave.SourceName))
        {
            image = GetWorkflowByNameLocked(preparedSave.SourceName)?.Image;
        }
        if (string.IsNullOrWhiteSpace(image))
        {
            image = "/imgs/model_placeholder.jpg";
        }
        ComfyUIBackendExtension.ComfyCustomWorkflow record = new(preparedSave.DestinationName, preparedSave.Workflow, preparedSave.Prompt,
            preparedSave.CustomParams, preparedSave.ParamValues, image, preparedSave.Description, preparedSave.EnableInSimple);
        JObject data = new()
        {
            ["workflow"] = preparedSave.ParsedWorkflow,
            ["prompt"] = preparedSave.ParsedPrompt,
            ["custom_params"] = preparedSave.ParsedCustomParams,
            ["param_values"] = preparedSave.ParsedParamValues,
            ["image"] = image,
            ["description"] = preparedSave.Description ?? "",
            ["enable_in_simple"] = preparedSave.EnableInSimple
        };
        return (record, data.ToString().EncodeUTF8());
    }

    /// <summary>Checks whether a cleaned workflow name identifies a bundled example workflow.</summary>
    private static bool IsExampleWorkflow(string cleanedName)
    {
        return ComfyUIBackendExtension.ExampleWorkflowNames.Contains(cleanedName.After("Examples/") + ".json");
    }

    /// <summary>Creates the exact deletion-marker artifacts needed by a prepared transaction.</summary>
    private static void PrepareMarkerArtifacts(WorkflowTransaction transaction)
    {
        if (!transaction.CreateMarker)
        {
            return;
        }
        string markerPath = RevalidateContainedPath(GetMarkerPath(transaction.SourceName));
        transaction.MarkerStagePath = CreateArtifactPath(markerPath, transaction.Id, "marker-stage");
        transaction.MarkerBackupPath = transaction.MarkerExisted ? CreateArtifactPath(markerPath, transaction.Id, "marker-backup") : null;
        WriteNewFlushedFile(GetArtifactPath(transaction, transaction.MarkerStagePath, markerPath, "marker-stage"), DeletedMarkerContent.EncodeUTF8());
    }

    /// <summary>Installs a prepared deletion marker using freshly revalidated exact paths.</summary>
    private static void InstallPreparedMarker(WorkflowTransaction transaction)
    {
        if (!transaction.CreateMarker)
        {
            return;
        }
        if (transaction.MarkerExisted)
        {
            string markerPath = RevalidateContainedPath(GetMarkerPath(transaction.SourceName));
            string markerBackupPath = RevalidateContainedPath(GetArtifactPath(transaction, transaction.MarkerBackupPath, markerPath, "marker-backup"));
            File.Move(RevalidateContainedPath(GetMarkerPath(transaction.SourceName)), markerBackupPath);
        }
        string freshMarkerPath = RevalidateContainedPath(GetMarkerPath(transaction.SourceName));
        string markerStagePath = RevalidateContainedPath(GetArtifactPath(transaction, transaction.MarkerStagePath, freshMarkerPath, "marker-stage"));
        File.Move(markerStagePath, RevalidateContainedPath(GetMarkerPath(transaction.SourceName)));
    }

    /// <summary>Best-effort removes only the exact transaction stages created before a journal was published.</summary>
    private static void CleanupUnjournaledStages(WorkflowTransaction transaction)
    {
        try
        {
            if (transaction.StagePath is not null)
            {
                DeleteVerifiedStage(transaction, transaction.StagePath, GetWorkflowPath(transaction.DestinationName), "stage", transaction.DestinationHash);
            }
        }
        catch (Exception)
        {
            Logs.Error("Error cleaning unjournaled workflow transaction artifacts (workflow content redacted).");
        }
        try
        {
            if (transaction.MarkerStagePath is not null)
            {
                string markerHash = GetDataHash(DeletedMarkerContent.EncodeUTF8());
                DeleteVerifiedStage(transaction, transaction.MarkerStagePath, GetMarkerPath(transaction.SourceName), "marker-stage", markerHash);
            }
        }
        catch (Exception)
        {
            Logs.Error("Error cleaning unjournaled workflow transaction artifacts (workflow content redacted).");
        }
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
        if (transaction.Phase == TransactionPhase.Prepared && FileExistsStrict(journalPath))
        {
            throw new InvalidOperationException("A workflow transaction journal is already active.");
        }
        if (FileExistsStrict(temporaryPath))
        {
            throw new InvalidOperationException("A workflow transaction journal temporary file already exists.");
        }
        try
        {
            WriteNewFlushedFile(temporaryPath, SerializeTransaction(transaction));
            File.Move(RevalidateContainedPath(temporaryPath), RevalidateContainedPath(journalPath), true);
        }
        catch
        {
            File.Delete(RevalidateContainedPath(temporaryPath));
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
                bool samePath = PathsEqual(sourcePath, destinationPath);
                if (samePath && transaction.SourceExisted && !transaction.DestinationExisted)
                {
                    throw new InvalidDataException("Invalid workflow transaction journal.");
                }
                GetArtifactPath(transaction, transaction.StagePath, destinationPath, "stage");
                if (transaction.DestinationExisted != (transaction.DestinationBackupPath is not null))
                {
                    throw new InvalidDataException("Invalid workflow transaction journal.");
                }
                if (transaction.DestinationBackupPath is not null)
                {
                    GetArtifactPath(transaction, transaction.DestinationBackupPath, destinationPath, "destination-backup");
                }
                bool sourceBackupRequired = transaction.SourceExisted && !samePath;
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
                if (transaction.SourceName is null || !transaction.SourceExisted || transaction.MarkerStagePath is null
                    || transaction.MarkerExisted != (transaction.MarkerBackupPath is not null))
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

    /// <summary>Gets the canonical lowercase SHA-256 hash for data.</summary>
    private static string GetDataHash(byte[] data)
    {
        return Convert.ToHexString(SHA256.HashData(data)).ToLowerFast();
    }

    /// <summary>Checks whether a file exists and has the expected canonical SHA-256 hash.</summary>
    private static bool FileMatchesHash(string path, string expectedHash)
    {
        path = RevalidateContainedPath(path);
        if (expectedHash is null || !FileExistsStrict(path))
        {
            return false;
        }
        using FileStream stream = File.OpenRead(RevalidateContainedPath(path));
        string actualHash = Convert.ToHexString(SHA256.HashData(stream)).ToLowerFast();
        return string.Equals(actualHash, expectedHash, StringComparison.Ordinal);
    }

    /// <summary>Deletes an exact transaction stage only when its content proves transaction ownership.</summary>
    private static void DeleteVerifiedStage(WorkflowTransaction transaction, string relativePath, string finalPath, string role, string expectedHash)
    {
        string stagePath = GetArtifactPath(transaction, relativePath, finalPath, role);
        if (!FileExistsStrict(stagePath))
        {
            return;
        }
        if (!FileMatchesHash(stagePath, expectedHash))
        {
            throw new IOException("Stored workflow transaction encountered unexpected stage content.");
        }
        File.Delete(RevalidateContainedPath(GetArtifactPath(transaction, relativePath, finalPath, role)));
    }

    /// <summary>Safely restores or removes an original file without overwriting unexpected content.</summary>
    private static void RestoreOriginal(string finalPath, string backupPath, bool originallyExisted, string installedHash)
    {
        finalPath = RevalidateContainedPath(finalPath);
        backupPath = backupPath is null ? null : RevalidateContainedPath(backupPath);
        if (backupPath is not null && FileExistsStrict(backupPath))
        {
            if (FileExistsStrict(finalPath))
            {
                if (!FileMatchesHash(finalPath, installedHash))
                {
                    throw new IOException("Stored workflow recovery encountered unexpected file content.");
                }
                File.Delete(RevalidateContainedPath(finalPath));
            }
            File.Move(RevalidateContainedPath(backupPath), RevalidateContainedPath(finalPath));
            return;
        }
        if (originallyExisted)
        {
            if (!FileExistsStrict(finalPath))
            {
                throw new IOException("Stored workflow recovery could not find an original file.");
            }
            return;
        }
        if (FileExistsStrict(finalPath))
        {
            if (!FileMatchesHash(finalPath, installedHash))
            {
                throw new IOException("Stored workflow recovery encountered unexpected file content.");
            }
            File.Delete(RevalidateContainedPath(finalPath));
        }
    }

    /// <summary>Restores a transaction's original deletion-marker state.</summary>
    private static void RestoreMarker(WorkflowTransaction transaction)
    {
        if (!transaction.CreateMarker)
        {
            return;
        }
        string markerPath = GetMarkerPath(transaction.SourceName);
        string markerHash = GetDataHash(DeletedMarkerContent.EncodeUTF8());
        if (transaction.MarkerExisted)
        {
            string markerBackupPath = GetArtifactPath(transaction, transaction.MarkerBackupPath, markerPath, "marker-backup");
            RestoreOriginal(GetMarkerPath(transaction.SourceName), markerBackupPath, true, markerHash);
        }
        else if (FileExistsStrict(GetMarkerPath(transaction.SourceName)))
        {
            RestoreOriginal(GetMarkerPath(transaction.SourceName), null, false, markerHash);
        }
    }

    /// <summary>Rolls back a prepared workflow transaction and removes its journal last.</summary>
    private static void RollbackPrepared(WorkflowTransaction transaction)
    {
        string sourcePath = GetWorkflowPath(transaction.SourceName);
        string destinationPath = transaction.DestinationName is null ? null : GetWorkflowPath(transaction.DestinationName);
        if (transaction.StagePath is not null)
        {
            GetArtifactPath(transaction, transaction.StagePath, destinationPath, "stage");
        }
        if (transaction.MarkerStagePath is not null)
        {
            GetArtifactPath(transaction, transaction.MarkerStagePath, GetMarkerPath(transaction.SourceName), "marker-stage");
        }
        RestoreMarker(transaction);
        if (transaction.Operation == TransactionOperation.Save)
        {
            string destinationBackupPath = transaction.DestinationExisted
                ? GetArtifactPath(transaction, transaction.DestinationBackupPath, destinationPath, "destination-backup")
                : null;
            RestoreOriginal(GetWorkflowPath(transaction.DestinationName), destinationBackupPath, transaction.DestinationExisted, transaction.DestinationHash);
            if (transaction.SourceExisted && !PathsEqual(sourcePath, destinationPath))
            {
                string sourceBackupPath = GetArtifactPath(transaction, transaction.SourceBackupPath, sourcePath, "source-backup");
                RestoreOriginal(GetWorkflowPath(transaction.SourceName), sourceBackupPath, true, null);
            }
        }
        else
        {
            string sourceBackupPath = GetArtifactPath(transaction, transaction.SourceBackupPath, sourcePath, "source-backup");
            RestoreOriginal(GetWorkflowPath(transaction.SourceName), sourceBackupPath, true, null);
        }
        if (transaction.StagePath is not null)
        {
            DeleteVerifiedStage(transaction, transaction.StagePath, GetWorkflowPath(transaction.DestinationName), "stage", transaction.DestinationHash);
        }
        if (transaction.MarkerStagePath is not null)
        {
            string markerHash = GetDataHash(DeletedMarkerContent.EncodeUTF8());
            DeleteVerifiedStage(transaction, transaction.MarkerStagePath, GetMarkerPath(transaction.SourceName), "marker-stage", markerHash);
        }
        File.Delete(RevalidateContainedPath(GetContainedPath(JournalFileName)));
    }

    /// <summary>Removes the exact artifacts recorded by a committed transaction and its journal last.</summary>
    private static void CleanupCommitted(WorkflowTransaction transaction)
    {
        string sourcePath = GetWorkflowPath(transaction.SourceName);
        string destinationPath = transaction.DestinationName is null ? null : GetWorkflowPath(transaction.DestinationName);
        string markerPath = GetMarkerPath(transaction.SourceName);
        List<(string RelativePath, string FinalPath, string Role)> artifacts = [];
        if (transaction.StagePath is not null)
        {
            DeleteVerifiedStage(transaction, transaction.StagePath, GetWorkflowPath(transaction.DestinationName), "stage", transaction.DestinationHash);
        }
        if (transaction.MarkerStagePath is not null)
        {
            string markerHash = GetDataHash(DeletedMarkerContent.EncodeUTF8());
            DeleteVerifiedStage(transaction, transaction.MarkerStagePath, GetMarkerPath(transaction.SourceName), "marker-stage", markerHash);
        }
        if (transaction.SourceBackupPath is not null)
        {
            artifacts.Add((transaction.SourceBackupPath, sourcePath, "source-backup"));
        }
        if (transaction.DestinationBackupPath is not null)
        {
            artifacts.Add((transaction.DestinationBackupPath, destinationPath, "destination-backup"));
        }
        if (transaction.MarkerBackupPath is not null)
        {
            artifacts.Add((transaction.MarkerBackupPath, markerPath, "marker-backup"));
        }
        foreach ((string relativePath, string finalPath, string role) in artifacts)
        {
            File.Delete(RevalidateContainedPath(GetArtifactPath(transaction, relativePath, finalPath, role)));
        }
        File.Delete(RevalidateContainedPath(GetContainedPath(JournalFileName)));
    }

    /// <summary>Ensures a committed transaction's deletion marker is installed.</summary>
    private static void EnsureCommittedMarker(WorkflowTransaction transaction)
    {
        if (!transaction.CreateMarker)
        {
            return;
        }
        byte[] markerData = DeletedMarkerContent.EncodeUTF8();
        string markerHash = GetDataHash(markerData);
        if (FileExistsStrict(GetMarkerPath(transaction.SourceName)))
        {
            if (!FileMatchesHash(GetMarkerPath(transaction.SourceName), markerHash))
            {
                throw new IOException("Stored workflow recovery encountered unexpected marker content.");
            }
            return;
        }
        if (FileExistsStrict(GetArtifactPath(transaction, transaction.MarkerStagePath, GetMarkerPath(transaction.SourceName), "marker-stage")))
        {
            if (!FileMatchesHash(GetArtifactPath(transaction, transaction.MarkerStagePath, GetMarkerPath(transaction.SourceName), "marker-stage"), markerHash))
            {
                throw new IOException("Stored workflow recovery encountered unexpected marker content.");
            }
        }
        else
        {
            WriteNewFlushedFile(GetArtifactPath(transaction, transaction.MarkerStagePath, GetMarkerPath(transaction.SourceName), "marker-stage"), markerData);
        }
        File.Move(
            RevalidateContainedPath(GetArtifactPath(transaction, transaction.MarkerStagePath, GetMarkerPath(transaction.SourceName), "marker-stage")),
            RevalidateContainedPath(GetMarkerPath(transaction.SourceName)));
    }

    /// <summary>Verifies the intended committed workflow state without removing unexpected final files.</summary>
    private static void VerifyCommittedState(WorkflowTransaction transaction)
    {
        string sourcePath = GetWorkflowPath(transaction.SourceName);
        if (transaction.Operation == TransactionOperation.Save)
        {
            string destinationPath = GetWorkflowPath(transaction.DestinationName);
            if (!FileMatchesHash(destinationPath, transaction.DestinationHash))
            {
                throw new IOException("Stored workflow recovery could not verify the committed workflow.");
            }
            if (transaction.SourceExisted && !PathsEqual(sourcePath, destinationPath) && FileExistsStrict(GetWorkflowPath(transaction.SourceName)))
            {
                throw new IOException("Stored workflow recovery encountered an unexpected workflow file.");
            }
        }
        else if (FileExistsStrict(GetWorkflowPath(transaction.SourceName)))
        {
            throw new IOException("Stored workflow recovery encountered an unexpected workflow file.");
        }
        EnsureCommittedMarker(transaction);
    }

    /// <summary>Recovers the single active workflow transaction, if present.</summary>
    private static void RecoverPendingTransactionLocked()
    {
        try
        {
            string journalPath = GetContainedPath(JournalFileName);
            if (!FileExistsStrict(journalPath))
            {
                return;
            }
            WorkflowTransaction transaction = ReadJournal(journalPath);
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
            MarkRecoveryRequiredLocked();
            Logs.Error("Error recovering stored workflow transaction (workflow content redacted).");
            throw new InvalidDataException("Stored workflow recovery could not be completed safely.");
        }
    }

    /// <summary>Saves a custom workflow using a durable filesystem transaction.</summary>
    public static void SaveWorkflow(string name, string workflow, string prompt, string customParams, string paramValues, string image, string description, bool enableInSimple, string replace)
    {
        PreparedSave preparedSave = PrepareSave(name, workflow, prompt, customParams, paramValues, image, description, enableInSimple, replace);
        try
        {
            lock (WorkflowLock)
            {
                SaveWorkflowLocked(preparedSave);
            }
        }
        catch (Exception)
        {
            Logs.Error("Error persisting workflow save transaction (workflow content redacted).");
            throw new IOException("Workflow save failed.");
        }
    }

    /// <summary>Deletes a custom workflow using a durable filesystem transaction.</summary>
    public static bool DeleteWorkflow(string name)
    {
        string cleanedName = Utilities.StrictFilenameClean(name);
        try
        {
            lock (WorkflowLock)
            {
                EnsureRecoveryReadyLocked();
                if (!ComfyUIBackendExtension.CustomWorkflows.ContainsKey(cleanedName))
                {
                    return false;
                }
                string sourcePath = GetWorkflowPath(cleanedName);
                if (!FileExistsStrict(sourcePath))
                {
                    return false;
                }
                Guid id = Guid.NewGuid();
                bool createMarker = IsExampleWorkflow(cleanedName);
                WorkflowTransaction transaction = new()
                {
                    Id = id,
                    Operation = TransactionOperation.Delete,
                    Phase = TransactionPhase.Prepared,
                    SourceName = cleanedName,
                    SourceBackupPath = CreateArtifactPath(sourcePath, id, "source-backup"),
                    SourceExisted = true,
                    CreateMarker = createMarker,
                    MarkerExisted = createMarker && FileExistsStrict(GetMarkerPath(cleanedName))
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
                    string freshSourcePath = RevalidateContainedPath(GetWorkflowPath(transaction.SourceName));
                    string sourceBackupPath = RevalidateContainedPath(GetArtifactPath(transaction, transaction.SourceBackupPath, freshSourcePath, "source-backup"));
                    File.Move(freshSourcePath, sourceBackupPath);
                    InstallPreparedMarker(transaction);
                    transaction.Phase = TransactionPhase.Committed;
                    WriteJournal(transaction);
                }
                catch
                {
                    try
                    {
                        RollbackPrepared(transaction);
                    }
                    catch (Exception)
                    {
                        MarkRecoveryRequiredLocked();
                        Logs.Error("Error rolling back workflow deletion transaction (workflow content redacted).");
                        throw new IOException("Workflow deletion failed and requires journal recovery.");
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
                    Logs.Error("Error cleaning committed workflow deletion transaction (workflow content redacted).");
                }
                return true;
            }
        }
        catch (Exception)
        {
            Logs.Error("Error persisting workflow deletion transaction (workflow content redacted).");
            throw new IOException("Workflow deletion failed.");
        }
    }

    /// <summary>Commits a prepared custom workflow save while the workflow lock is held.</summary>
    private static void SaveWorkflowLocked(PreparedSave preparedSave)
    {
        EnsureRecoveryReadyLocked();
        (ComfyUIBackendExtension.ComfyCustomWorkflow Record, byte[] Data) completedSave = CompleteSaveLocked(preparedSave);
        string destinationPath = GetWorkflowPath(preparedSave.DestinationName);
        string sourcePath = GetWorkflowPath(preparedSave.SourceName);
        Directory.CreateDirectory(Path.GetDirectoryName(destinationPath));
        destinationPath = RevalidateContainedPath(GetWorkflowPath(preparedSave.DestinationName));
        sourcePath = RevalidateContainedPath(GetWorkflowPath(preparedSave.SourceName));
        bool sourcePublished = preparedSave.HasReplacement && ComfyUIBackendExtension.CustomWorkflows.ContainsKey(preparedSave.SourceName);
        bool sourceExisted = sourcePublished && FileExistsStrict(sourcePath);
        bool samePath = PathsEqual(sourcePath, destinationPath);
        bool destinationExisted = FileExistsStrict(destinationPath);
        bool createMarker = sourceExisted && preparedSave.HasReplacement && IsExampleWorkflow(preparedSave.SourceName);
        Guid id = Guid.NewGuid();
        WorkflowTransaction transaction = new()
        {
            Id = id,
            Operation = TransactionOperation.Save,
            Phase = TransactionPhase.Prepared,
            SourceName = preparedSave.SourceName,
            DestinationName = preparedSave.DestinationName,
            DestinationHash = GetDataHash(completedSave.Data),
            StagePath = CreateArtifactPath(destinationPath, id, "stage"),
            SourceBackupPath = sourceExisted && !samePath ? CreateArtifactPath(sourcePath, id, "source-backup") : null,
            DestinationBackupPath = destinationExisted ? CreateArtifactPath(destinationPath, id, "destination-backup") : null,
            SourceExisted = sourceExisted,
            DestinationExisted = destinationExisted,
            CreateMarker = createMarker,
            MarkerExisted = createMarker && FileExistsStrict(GetMarkerPath(preparedSave.SourceName))
        };
        WriteNewFlushedFile(GetArtifactPath(transaction, transaction.StagePath, destinationPath, "stage"), completedSave.Data);
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
            if (transaction.DestinationExisted)
            {
                string freshDestinationPath = RevalidateContainedPath(GetWorkflowPath(transaction.DestinationName));
                string destinationBackupPath = RevalidateContainedPath(GetArtifactPath(transaction, transaction.DestinationBackupPath, freshDestinationPath, "destination-backup"));
                File.Move(freshDestinationPath, destinationBackupPath);
            }
            string installedDestinationPath = RevalidateContainedPath(GetWorkflowPath(transaction.DestinationName));
            string stagePath = RevalidateContainedPath(GetArtifactPath(transaction, transaction.StagePath, installedDestinationPath, "stage"));
            File.Move(stagePath, installedDestinationPath);
            if (transaction.SourceExisted && !samePath)
            {
                string freshSourcePath = RevalidateContainedPath(GetWorkflowPath(transaction.SourceName));
                string sourceBackupPath = RevalidateContainedPath(GetArtifactPath(transaction, transaction.SourceBackupPath, freshSourcePath, "source-backup"));
                File.Move(freshSourcePath, sourceBackupPath);
            }
            InstallPreparedMarker(transaction);
            transaction.Phase = TransactionPhase.Committed;
            WriteJournal(transaction);
        }
        catch
        {
            try
            {
                RollbackPrepared(transaction);
            }
            catch (Exception)
            {
                MarkRecoveryRequiredLocked();
                Logs.Error("Error rolling back workflow save transaction (workflow content redacted).");
                throw new IOException("Workflow save failed and requires journal recovery.");
            }
            throw;
        }
        if (preparedSave.HasReplacement && !string.Equals(preparedSave.SourceName, preparedSave.DestinationName, StringComparison.Ordinal))
        {
            ComfyUIBackendExtension.CustomWorkflows.TryRemove(preparedSave.SourceName, out _);
        }
        ComfyUIBackendExtension.CustomWorkflows[preparedSave.DestinationName] = completedSave.Record;
        try
        {
            CleanupCommitted(transaction);
        }
        catch (Exception)
        {
            Logs.Error("Error cleaning committed workflow save transaction (workflow content redacted).");
        }
    }

    /// <summary>Loads the available workflow files from the extension directory.</summary>
    public static void LoadWorkflowFiles(string extensionFilePath)
    {
        lock (WorkflowLock)
        {
            try
            {
                Directory.CreateDirectory($"{extensionFilePath}CustomWorkflows");
                Directory.CreateDirectory($"{extensionFilePath}CustomWorkflows/Examples");
                RecoverPendingTransactionLocked();
                string[] getCustomFlows(string path) => [.. Directory.EnumerateFiles($"{extensionFilePath}/{path}", "*.*", new EnumerationOptions() { RecurseSubdirectories = true }).Select(f => f.Replace('\\', '/').After($"/{path}/")).Order()];
                string[] exampleWorkflowNames = getCustomFlows("ExampleWorkflows");
                string[] customFlows = getCustomFlows("CustomWorkflows");
                bool anyCopied = false;
                foreach (string workflow in exampleWorkflowNames.Where(f => f.EndsWith(".json")))
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
                ConcurrentDictionary<string, ComfyUIBackendExtension.ComfyCustomWorkflow> refreshedWorkflows = new();
                foreach (string workflow in customFlows.Where(f => f.EndsWith(".json")))
                {
                    refreshedWorkflows.TryAdd(workflow.BeforeLast('.'), null);
                }
                ComfyUIBackendExtension.ExampleWorkflowNames = exampleWorkflowNames;
                ComfyUIBackendExtension.CustomWorkflows = refreshedWorkflows;
                RecoveryRequired = false;
            }
            catch
            {
                MarkRecoveryRequiredLocked();
                throw;
            }
        }
    }

    /// <summary>Gets a workflow by its stored name.</summary>
    public static ComfyUIBackendExtension.ComfyCustomWorkflow GetWorkflowByName(string name)
    {
        lock (WorkflowLock)
        {
            EnsureRecoveryReadyLocked();
            return GetWorkflowByNameLocked(name);
        }
    }

    /// <summary>Gets a workflow by its stored name while the workflow lock is held.</summary>
    private static ComfyUIBackendExtension.ComfyCustomWorkflow GetWorkflowByNameLocked(string name)
    {
        EnsureRecoveryReadyLocked();
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
            EnsureRecoveryReadyLocked();
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
            EnsureRecoveryReadyLocked();
            return [.. ComfyUIBackendExtension.CustomWorkflows.Keys.Order()];
        }
    }

    /// <summary>Gets a workflow prompt while distinguishing unknown names from unloaded workflows.</summary>
    public static bool TryGetWorkflowParameterPrompt(string name, out string prompt)
    {
        lock (WorkflowLock)
        {
            EnsureRecoveryReadyLocked();
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
