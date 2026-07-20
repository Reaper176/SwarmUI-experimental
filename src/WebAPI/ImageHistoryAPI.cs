using FreneticUtilities.FreneticExtensions;
using Newtonsoft.Json.Linq;
using SwarmUI.Accounts;
using SwarmUI.Core;
using SwarmUI.Media;
using SwarmUI.Text2Image;
using SwarmUI.Utils;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using Image = SwarmUI.Utils.Image;

namespace SwarmUI.WebAPI;

[API.APIClass("API routes for saved image history and metadata.")]
public static class ImageHistoryAPI
{
    [API.APIDescription("Takes an image and stores it directly in the user's history.\nBehaves identical to GenerateText2Image but never queues a generation.",
        """
            "images":
            [
                {
                    "image": "View/local/raw/2024-01-02/0304-a photo of a cat-etc-1.png", // the image file path, GET this path to read the image content
                    "batch_index": "0", // which image index within the batch this is
                    "metadata": "{ ... }" // image metadata string, usually a JSON blob stringified. Not guaranteed to be.
                }
            ]
        """)]
    public static async Task<JObject> AddImageToHistory(Session session,
        [API.APIParameter("Data URL of the image to save.")] string image,
        [API.APIParameter("Raw mapping of input should contain general T2I parameters (see listing on Generate tab of main interface) to values, eg `{ \"prompt\": \"a photo of a cat\", \"model\": \"OfficialStableDiffusion/sd_xl_base_1.0\", \"steps\": 20, ... }`. Note that this is the root raw map, ie all params go on the same level as `images`, `session_id`, etc.")] JObject rawInput)
    {
        // TODO: Recognize audio/video inputs properly
        ImageFile img = ImageFile.FromDataString(image);
        T2IParamInput user_input;
        rawInput.Remove("image");
        try
        {
            user_input = T2IAPI.RequestToParams(session, rawInput);
        }
        catch (SwarmReadableErrorException ex)
        {
            return new() { ["error"] = ex.Message };
        }
        // This endpoint doesn't run a generation pipeline, so no params are naturally "queried".
        // Keep full parameter metadata instead of collapsing most fields into `unused_parameters`.
        user_input.NoUnusedParams = true;
        user_input.ApplySpecialLogic();
        Logs.Info($"User {session.User.UserID} stored an image to history.");
        (Task<MediaFile> imgTask, string metadata) = user_input.SourceSession.ApplyMetadata(img, user_input, 1);
        T2IEngine.ImageOutput outputImage = new() { File = img as Image, ActualFileTask = imgTask };
        (string path, _) = session.SaveImage(outputImage, 0, user_input, metadata);
        return new() { ["images"] = new JArray() { new JObject() { ["image"] = path, ["batch_index"] = "0", ["request_id"] = $"{user_input.UserRequestId}", ["metadata"] = metadata } } };
    }

    private static bool MetadataIsHidden(OutputMetadataTracker.OutputMetadataEntry metadata)
    {
        if (metadata is null || string.IsNullOrWhiteSpace(metadata.Metadata) || !metadata.Metadata.Contains("\"is_hidden\""))
        {
            return false;
        }
        try
        {
            JToken hidden = metadata.Metadata.ParseToJson()["is_hidden"];
            return hidden?.Type == JTokenType.Boolean && hidden.Value<bool>();
        }
        catch (Exception)
        {
            return false;
        }
    }

    private static double MetadataRating(OutputMetadataTracker.OutputMetadataEntry metadata)
    {
        if (metadata is null || string.IsNullOrWhiteSpace(metadata.Metadata) || !metadata.Metadata.Contains("\"rating\""))
        {
            return 0;
        }
        try
        {
            JToken rating = metadata.Metadata.ParseToJson()["rating"];
            return rating is null ? 0 : rating.Value<double>();
        }
        catch (Exception)
        {
            return 0;
        }
    }

    private static long MetadataResolutionPixels(OutputMetadataTracker.OutputMetadataEntry metadata)
    {
        if (metadata is null || string.IsNullOrWhiteSpace(metadata.Metadata) || !metadata.Metadata.Contains("\"sui_image_params\""))
        {
            return 0;
        }
        try
        {
            JObject parsed = metadata.Metadata.ParseToJson();
            JObject parameters = parsed["sui_image_params"] as JObject;
            long width = parameters?["width"]?.Value<long>() ?? 0;
            long height = parameters?["height"]?.Value<long>() ?? 0;
            JObject extra = parsed["sui_extra_data"] as JObject;
            width = extra?["final_width"]?.Value<long>() ?? width;
            height = extra?["final_height"]?.Value<long>() ?? height;
            return width * height;
        }
        catch (Exception)
        {
            return 0;
        }
    }

    private static string MetadataModel(OutputMetadataTracker.OutputMetadataEntry metadata)
    {
        if (metadata is null || string.IsNullOrWhiteSpace(metadata.Metadata) || !metadata.Metadata.Contains("\"model\""))
        {
            return "";
        }
        try
        {
            JToken model = metadata.Metadata.ParseToJson()["sui_image_params"]?["model"];
            return $"{model}".ToLowerFast();
        }
        catch (Exception)
        {
            return "";
        }
    }

    private static long MetadataSeed(OutputMetadataTracker.OutputMetadataEntry metadata)
    {
        if (metadata is null || string.IsNullOrWhiteSpace(metadata.Metadata) || !metadata.Metadata.Contains("\"seed\""))
        {
            return 0;
        }
        try
        {
            JToken seed = metadata.Metadata.ParseToJson()["sui_image_params"]?["seed"];
            return seed is null ? 0 : seed.Value<long>();
        }
        catch (Exception)
        {
            return 0;
        }
    }

    /// <summary>Splits an image history filter query into terms while preserving quoted text.</summary>
    private static List<string> SplitImageHistoryFilterQuery(string filter)
    {
        List<string> terms = [];
        string current = "";
        char quote = '\0';
        foreach (char character in filter ?? "")
        {
            if ((character == '"' || character == '\'') && (quote == '\0' || quote == character))
            {
                quote = quote == character ? '\0' : character;
                continue;
            }
            if (quote == '\0' && char.IsWhiteSpace(character))
            {
                if (!string.IsNullOrWhiteSpace(current))
                {
                    terms.Add(current.Trim());
                }
                current = "";
                continue;
            }
            current += character;
        }
        if (!string.IsNullOrWhiteSpace(current))
        {
            terms.Add(current.Trim());
        }
        return terms;
    }

    /// <summary>Normalizes aliases for image history structured search fields.</summary>
    private static string NormalizeImageHistoryFilterField(string field)
    {
        return field switch
        {
            "fav" => "favorite",
            "starred" => "favorite",
            "hide" => "hidden",
            "is_hidden" => "hidden",
            "tag" => "tags",
            "neg" => "negative",
            "negativeprompt" => "negative",
            "loras" => "lora",
            "res" => "resolution",
            "file_size" => "filesize",
            "size" => "filesize",
            "final_width" => "finalwidth",
            "final_height" => "finalheight",
            "prompt_lab" => "promptlab",
            "prompt_lab_id" => "promptlab",
            "ext" => "filetype",
            "file_type" => "filetype",
            "has_metadata" => "has",
            "wildcard_values" => "wildcard",
            _ => field
        };
    }

    /// <summary>Compiles image history filter terms for repeated matching.</summary>
    private static List<(string Field, string Value, string Raw)> CompileImageHistoryFilterQuery(string filter)
    {
        List<(string Field, string Value, string Raw)> terms = [];
        foreach (string term in SplitImageHistoryFilterQuery(filter))
        {
            int fieldSplit = term.IndexOf(':');
            if (fieldSplit > 0)
            {
                string field = NormalizeImageHistoryFilterField(term[..fieldSplit].ToLowerFast());
                string value = term[(fieldSplit + 1)..].ToLowerFast();
                if (!string.IsNullOrWhiteSpace(value))
                {
                    terms.Add((field, value, term.ToLowerFast()));
                }
            }
            else
            {
                string value = term.ToLowerFast();
                if (!string.IsNullOrWhiteSpace(value))
                {
                    terms.Add((null, value, value));
                }
            }
        }
        return terms;
    }

    /// <summary>Returns searchable text for a boolean history filter field.</summary>
    private static string ImageHistoryBoolSearchText(bool value, string trueWord, string falseWord)
    {
        return value ? $"true yes {trueWord}" : $"false no {falseWord}";
    }

    /// <summary>Returns searchable text for an indexed history field.</summary>
    private static string GetIndexedHistoryFilterField(OutputMetadataTracker.OutputHistoryIndexEntry entry, string field)
    {
        return field switch
        {
            "name" => entry.RelativePath.AfterLast('/'),
            "path" => entry.RelativePath,
            "folder" => entry.Folder ?? "",
            "type" or "filetype" => entry.Extension ?? "",
            "filesize" => entry.FileSize.ToString(),
            "date" => $"{DateTimeOffset.FromUnixTimeSeconds(Math.Max(0, entry.FileTime)):O} {entry.RelativePath}",
            "metadata" or "prompt" or "negative" or "lora" or "vae" or "sampler" or "scheduler" or "resolution" or "width" or "height" or "finalwidth" or "finalheight" or "steps" or "cfg" or "notes" or "tags" or "session" or "wildcard" or "promptlab" => entry.Metadata ?? "",
            "model" => entry.Model ?? "",
            "seed" => entry.Seed.ToString(),
            "rating" => entry.Rating.ToString(),
            "hidden" => ImageHistoryBoolSearchText(entry.IsHidden, "hidden", "visible"),
            "favorite" => ImageHistoryBoolSearchText(entry.IsStarred || entry.RelativePath.StartsWith("Starred/"), "starred favorite", "unstarred"),
            "has" => ImageHistoryBoolSearchText(!string.IsNullOrWhiteSpace(entry.Metadata), "metadata", "none"),
            _ => null
        };
    }

    /// <summary>Returns searchable text for a scanned history field.</summary>
    private static string GetScannedHistoryFilterField(T2IAPI.ImageHistoryHelper entry, string field)
    {
        bool isStarred = entry.Name.StartsWith("Starred/");
        bool isHidden = MetadataIsHidden(entry.Metadata);
        return field switch
        {
            "name" => entry.Name.AfterLast('/'),
            "path" => entry.Name,
            "folder" => entry.Name.Contains('/') ? entry.Name.BeforeLast('/') : "",
            "type" or "filetype" => entry.Name.AfterLast('.').ToLowerFast(),
            "filesize" => entry.FileSize.ToString(),
            "date" => $"{DateTimeOffset.FromUnixTimeSeconds(Math.Max(0, entry.Metadata?.FileTime ?? 0)):O} {entry.Name}",
            "metadata" or "prompt" or "negative" or "lora" or "vae" or "sampler" or "scheduler" or "resolution" or "width" or "height" or "finalwidth" or "finalheight" or "steps" or "cfg" or "notes" or "tags" or "session" or "wildcard" or "promptlab" => entry.Metadata?.Metadata ?? "",
            "model" => MetadataModel(entry.Metadata),
            "seed" => MetadataSeed(entry.Metadata).ToString(),
            "rating" => MetadataRating(entry.Metadata).ToString(),
            "hidden" => ImageHistoryBoolSearchText(isHidden, "hidden", "visible"),
            "favorite" => ImageHistoryBoolSearchText(isStarred, "starred favorite", "unstarred"),
            "has" => ImageHistoryBoolSearchText(!string.IsNullOrWhiteSpace(entry.Metadata?.Metadata), "metadata", "none"),
            _ => null
        };
    }

    /// <summary>Checks a numeric comparison term against a field value, or null when the term is not numeric.</summary>
    private static bool? ImageHistoryNumericFilterMatches(string fieldText, string value)
    {
        string op = value.StartsWith(">=") || value.StartsWith("<=") ? value[..2] : value.Length > 0 && "<>=".Contains(value[0]) ? value[..1] : null;
        if (op is null || !double.TryParse(value[op.Length..], out double expected) || !double.TryParse(fieldText, out double actual))
        {
            return null;
        }
        return op switch
        {
            ">=" => actual >= expected,
            "<=" => actual <= expected,
            ">" => actual > expected,
            "<" => actual < expected,
            _ => actual == expected
        };
    }

    /// <summary>Checks a date comparison term against a field value, or null when the term is not a date comparison.</summary>
    private static bool? ImageHistoryDateFilterMatches(string fieldText, string value)
    {
        string op = value.StartsWith(">=") || value.StartsWith("<=") ? value[..2] : value.Length > 0 && "<>=".Contains(value[0]) ? value[..1] : null;
        if (op is null || !DateTimeOffset.TryParse(value[op.Length..], out DateTimeOffset expected))
        {
            return null;
        }
        int split = fieldText.IndexOf(' ');
        string actualText = split < 0 ? fieldText : fieldText[..split];
        if (!DateTimeOffset.TryParse(actualText, out DateTimeOffset actual))
        {
            return false;
        }
        return op switch
        {
            ">=" => actual >= expected,
            "<=" => actual <= expected,
            ">" => actual > expected,
            "<" => actual < expected,
            _ => actual == expected
        };
    }

    /// <summary>Checks whether searchable history text matches compiled filter terms.</summary>
    private static bool ImageHistoryFilterMatches(string allFields, Func<string, string> getField, List<(string Field, string Value, string Raw)> filterTerms)
    {
        if (filterTerms.Count == 0)
        {
            return true;
        }
        allFields = (allFields ?? "").ToLowerFast();
        foreach ((string field, string value, string raw) in filterTerms)
        {
            if (field is null)
            {
                if (!allFields.Contains(value))
                {
                    return false;
                }
                continue;
            }
            string fieldText = getField(field);
            if (fieldText is null)
            {
                if (!allFields.Contains(raw))
                {
                    return false;
                }
                continue;
            }
            fieldText = fieldText.ToLowerFast();
            bool? numericMatch = ImageHistoryNumericFilterMatches(fieldText, value);
            if (numericMatch is not null)
            {
                if (numericMatch != true)
                {
                    return false;
                }
                continue;
            }
            bool? dateMatch = ImageHistoryDateFilterMatches(fieldText, value);
            if (dateMatch is not null)
            {
                if (dateMatch != true)
                {
                    return false;
                }
                continue;
            }
            if (!fieldText.Contains(value))
            {
                return false;
            }
        }
        return true;
    }

    /// <summary>Checks whether an indexed history entry matches compiled filter terms.</summary>
    private static bool IndexedImageHistoryFilterMatches(OutputMetadataTracker.OutputHistoryIndexEntry entry, List<(string Field, string Value, string Raw)> filterTerms)
    {
        string allFields = $"{entry.RelativePath} {entry.Extension} {entry.Metadata} {entry.Model} {entry.Seed} {entry.Rating}";
        return ImageHistoryFilterMatches(allFields, field => GetIndexedHistoryFilterField(entry, field), filterTerms);
    }

    /// <summary>Checks whether a scanned history entry matches compiled filter terms.</summary>
    private static bool ScannedImageHistoryFilterMatches(T2IAPI.ImageHistoryHelper entry, List<(string Field, string Value, string Raw)> filterTerms)
    {
        string allFields = $"{entry.Name} {entry.Metadata?.Metadata} {MetadataModel(entry.Metadata)} {MetadataSeed(entry.Metadata)} {MetadataRating(entry.Metadata)}";
        return ImageHistoryFilterMatches(allFields, field => GetScannedHistoryFilterField(entry, field), filterTerms);
    }

    private static T2IAPI.ImageHistoryHelper GetImageHistoryHelper(string name, string file, string root, bool starNoFolders)
    {
        OutputMetadataTracker.OutputMetadataEntry metadata = OutputMetadataTracker.GetMetadataFor(file, root, starNoFolders);
        long fileSize = 0;
        long fileCreatedTime = 0;
        try
        {
            FileInfo info = new(file);
            fileSize = info.Length;
            fileCreatedTime = ((DateTimeOffset)info.CreationTimeUtc).ToUnixTimeSeconds();
        }
        catch (Exception)
        {
        }
        return new(name, metadata, fileSize, fileCreatedTime);
    }

    /// <summary>Sorts history index entries with the requested sort mode.</summary>
    private static List<OutputMetadataTracker.OutputHistoryIndexEntry> SortHistoryIndexEntries(List<OutputMetadataTracker.OutputHistoryIndexEntry> entries, T2IAPI.ImageHistorySortMode sortBy, bool sortReverse)
    {
        if (sortBy == T2IAPI.ImageHistorySortMode.Name)
        {
            entries.Sort((a, b) => b.RelativePath.CompareTo(a.RelativePath));
        }
        else if (sortBy == T2IAPI.ImageHistorySortMode.Date || sortBy == T2IAPI.ImageHistorySortMode.DateEdited)
        {
            entries.Sort((a, b) => b.FileTime.CompareTo(a.FileTime));
        }
        else if (sortBy == T2IAPI.ImageHistorySortMode.DateCreated)
        {
            entries.Sort((a, b) => b.FileCreatedTime.CompareTo(a.FileCreatedTime));
        }
        else if (sortBy == T2IAPI.ImageHistorySortMode.Rating)
        {
            entries.Sort((a, b) => b.Rating.CompareTo(a.Rating));
        }
        else if (sortBy == T2IAPI.ImageHistorySortMode.Resolution)
        {
            entries.Sort((a, b) => b.ResolutionPixels.CompareTo(a.ResolutionPixels));
        }
        else if (sortBy == T2IAPI.ImageHistorySortMode.Model)
        {
            entries.Sort((a, b) => (b.Model ?? "").CompareTo(a.Model ?? ""));
        }
        else if (sortBy == T2IAPI.ImageHistorySortMode.Seed)
        {
            entries.Sort((a, b) => b.Seed.CompareTo(a.Seed));
        }
        else if (sortBy == T2IAPI.ImageHistorySortMode.FileSize)
        {
            entries.Sort((a, b) => b.FileSize.CompareTo(a.FileSize));
        }
        if (sortReverse)
        {
            entries.Reverse();
        }
        return entries;
    }

    /// <summary>Attempts to answer a history list request from the persistent history index.</summary>
    private static JObject TryGetListFromHistoryIndex(Session session, string rawRefPath, int depth, T2IAPI.ImageHistorySortMode sortBy, bool sortReverse, bool includeHidden, bool fastFirst, int fastFirstLimit, int maxInHistory, List<(string Field, string Value, string Raw)> filterTerms, long timeStart)
    {
        string root = Utilities.CombinePathWithAbsolute(Environment.CurrentDirectory, session.User.OutputDirectory);
        int limit = fastFirst ? Math.Max(1, Math.Min(fastFirstLimit, maxInHistory)) : maxInHistory;
        string requestPrefix = rawRefPath == "./" ? "" : rawRefPath;
        requestPrefix = requestPrefix.Replace('\\', '/').Trim('/');
        if (!OutputMetadataTracker.IsHistoryIndexPrefixComplete(root, requestPrefix))
        {
            return null;
        }
        List<OutputMetadataTracker.OutputHistoryIndexEntry> indexed = OutputMetadataTracker.GetHistoryIndexForPrefix(root, requestPrefix);
        if (indexed.Count == 0)
        {
            return null;
        }
        if (!string.IsNullOrWhiteSpace(requestPrefix))
        {
            requestPrefix += "/";
        }
        HashSet<string> folders = [];
        List<OutputMetadataTracker.OutputHistoryIndexEntry> files = [];
        string specialPrefix = requestPrefix == "" ? "" : $"{requestPrefix}/";
        foreach (string specialFolder in UserImageHistoryHelper.SharedSpecialFolders.Keys)
        {
            if (specialFolder.StartsWith(specialPrefix))
            {
                string relativeSpecialFolder = specialFolder[specialPrefix.Length..];
                relativeSpecialFolder = relativeSpecialFolder.EndsWith('/') ? relativeSpecialFolder[..^1] : relativeSpecialFolder;
                if (!string.IsNullOrWhiteSpace(relativeSpecialFolder))
                {
                    folders.Add(relativeSpecialFolder);
                }
            }
        }
        foreach (OutputMetadataTracker.OutputHistoryIndexEntry entry in indexed)
        {
            if (!includeHidden && entry.IsHidden)
            {
                continue;
            }
            if (filterTerms.Count > 0 && !IndexedImageHistoryFilterMatches(entry, filterTerms))
            {
                continue;
            }
            if (!entry.RelativePath.StartsWith(requestPrefix))
            {
                continue;
            }
            string relative = entry.RelativePath[requestPrefix.Length..];
            if (string.IsNullOrWhiteSpace(relative) || relative.StartsWith('/'))
            {
                continue;
            }
            string[] parts = relative.Split('/', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0)
            {
                continue;
            }
            int folderDepth = parts.Length - 1;
            int folderLimit = Math.Min(folderDepth, depth);
            string folderPath = "";
            for (int i = 0; i < folderLimit; i++)
            {
                folderPath = folderPath == "" ? parts[i] : $"{folderPath}/{parts[i]}";
                folders.Add(folderPath);
            }
            if (folderDepth <= depth)
            {
                files.Add(entry);
            }
        }
        if (files.Count == 0 && folders.Count == 0)
        {
            return null;
        }
        HashSet<string> included = [.. files.Select(f => f.RelativePath)];
        files = [.. files.Where(f =>
        {
            if (f.RelativePath.StartsWith("Starred/"))
            {
                return true;
            }
            string starPath = session.User.Settings.StarNoFolders ? $"Starred/{f.RelativePath.Replace("/", "")}" : $"Starred/{f.RelativePath}";
            return !included.Contains(starPath);
        })];
        if (sortBy == T2IAPI.ImageHistorySortMode.DateCreated && files.Any(f => f.FileCreatedTime <= 0))
        {
            return null;
        }
        files = SortHistoryIndexEntries(files, sortBy, sortReverse);
        long timeEnd = Environment.TickCount64;
        Logs.Verbose($"Listed {files.Count} indexed images from {folders.Count} indexed folder entries in {(timeEnd - timeStart) / 1000.0:0.###} seconds.");
        return new JObject()
        {
            ["folders"] = JToken.FromObject(folders.OrderDescending().ToList()),
            ["files"] = JToken.FromObject(files.Take(limit).Select(f =>
            {
                string src = requestPrefix == "" ? f.RelativePath : f.RelativePath[requestPrefix.Length..];
                return new JObject() { ["src"] = src, ["metadata"] = f.Metadata, ["file_size"] = f.FileSize, ["file_time"] = f.FileTime, ["file_created_time"] = f.FileCreatedTime };
            }).ToList()),
            ["perf"] = new JObject()
            {
                ["total_ms"] = timeEnd - timeStart,
                ["dir_scan_ms"] = 0,
                ["file_scan_ms"] = 0,
                ["final_sort_ms"] = 0,
                ["fast_first"] = fastFirst,
                ["indexed"] = true
            }
        };
    }

    /// <summary>Removes a file path from the persistent history index.</summary>
    private static void RemoveHistoryIndexForPath(string root, string path)
    {
        OutputMetadataTracker.RemoveHistoryIndexForPrefix(root, OutputMetadataTracker.GetRelativePath(path, root));
    }

    /// <summary>Refreshes the persistent history index entry for a file path.</summary>
    private static void RefreshHistoryIndexForPath(string root, string path, bool starNoFolders)
    {
        RemoveHistoryIndexForPath(root, path);
        OutputMetadataTracker.UpsertHistoryIndexForFile(path.Replace('\\', '/'), root, starNoFolders);
    }

    private static JObject GetListAPIInternal(Session session, string rawPath, string root, HashSet<string> extensions, Func<string, bool> isAllowed, int depth, T2IAPI.ImageHistorySortMode sortBy, bool sortReverse, bool includeHidden, bool fastFirst = false, int fastFirstLimit = 128, bool forceScan = false, string filter = "")
    {
        int maxInHistory = session.User.Settings.MaxImagesInHistory;
        int maxScanned = session.User.Settings.MaxImagesScannedInHistory;
        List<(string Field, string Value, string Raw)> filterTerms = CompileImageHistoryFilterQuery(filter);
        Logs.Verbose($"User {session.User.UserID} wants to list images in '{rawPath}', maxDepth={depth}, sortBy={sortBy}, reverse={sortReverse}, includeHidden={includeHidden}, fastFirst={fastFirst}, fastFirstLimit={fastFirstLimit}, maxInHistory={maxInHistory}, maxScanned={maxScanned}, filterTerms={filterTerms.Count}");
        long timeStart = Environment.TickCount64;
        long dirScanMs = 0;
        long fileScanMs = 0;
        long finalSortMs = 0;
        int limit = sortBy == T2IAPI.ImageHistorySortMode.Name && filterTerms.Count == 0 ? maxInHistory : Math.Max(maxInHistory, maxScanned);
        (string path, string consoleError, string userError) = WebServer.CheckFilePath(root, rawPath);
        path = UserImageHistoryHelper.GetRealPathFor(session.User, path, root: root);
        if (consoleError is not null)
        {
            Logs.Error(consoleError);
            return new JObject() { ["error"] = userError };
        }
        try
        {
            ConcurrentDictionary<string, string> finalDirs = [];
            bool starNoFolders = session.User.Settings.StarNoFolders;
            string rawRefPath = Path.GetRelativePath(root, path).Replace('\\', '/');
            if (!rawRefPath.EndsWith('/'))
            {
                rawRefPath += '/';
            }
            if (rawRefPath == "./")
            {
                rawRefPath = "";
            }
            if (forceScan)
            {
                OutputMetadataTracker.RemoveHistoryIndexPrefixComplete(root, rawRefPath);
                OutputMetadataTracker.RemoveHistoryIndexForPrefix(root, rawRefPath);
            }
            TryStartImageHistoryIndexWarmup(session, root, path, rawRefPath);
            JObject indexedResult = forceScan ? null : TryGetListFromHistoryIndex(session, rawRefPath, depth, sortBy, sortReverse, includeHidden, fastFirst, fastFirstLimit, maxInHistory, filterTerms, timeStart);
            if (indexedResult is not null)
            {
                return indexedResult;
            }
            List<string> specialSeedDirs = [.. UserImageHistoryHelper.SharedSpecialFolders.Keys
                .Where(f => f.StartsWith(rawRefPath))
                .Select(f => f[rawRefPath.Length..])
                .Select(f => f.EndsWith('/') ? f[..^1] : f)
                .Where(f => !string.IsNullOrEmpty(f))
                .Distinct()
                .OrderDescending()];
            Dictionary<OutputMetadataTracker.OutputMetadataEntry, double> ratingSortCache = [];
            Dictionary<OutputMetadataTracker.OutputMetadataEntry, long> resolutionSortCache = [];
            Dictionary<OutputMetadataTracker.OutputMetadataEntry, string> modelSortCache = [];
            Dictionary<OutputMetadataTracker.OutputMetadataEntry, long> seedSortCache = [];
            double cachedRating(OutputMetadataTracker.OutputMetadataEntry metadata)
            {
                if (metadata is null)
                {
                    return 0;
                }
                if (!ratingSortCache.TryGetValue(metadata, out double result))
                {
                    result = MetadataRating(metadata);
                    ratingSortCache[metadata] = result;
                }
                return result;
            }
            long cachedResolution(OutputMetadataTracker.OutputMetadataEntry metadata)
            {
                if (metadata is null)
                {
                    return 0;
                }
                if (!resolutionSortCache.TryGetValue(metadata, out long result))
                {
                    result = MetadataResolutionPixels(metadata);
                    resolutionSortCache[metadata] = result;
                }
                return result;
            }
            string cachedModel(OutputMetadataTracker.OutputMetadataEntry metadata)
            {
                if (metadata is null)
                {
                    return "";
                }
                if (!modelSortCache.TryGetValue(metadata, out string result))
                {
                    result = MetadataModel(metadata);
                    modelSortCache[metadata] = result;
                }
                return result;
            }
            long cachedSeed(OutputMetadataTracker.OutputMetadataEntry metadata)
            {
                if (metadata is null)
                {
                    return 0;
                }
                if (!seedSortCache.TryGetValue(metadata, out long result))
                {
                    result = MetadataSeed(metadata);
                    seedSortCache[metadata] = result;
                }
                return result;
            }
            void sortList(List<T2IAPI.ImageHistoryHelper> list)
            {
                if (sortBy == T2IAPI.ImageHistorySortMode.Name)
                {
                    list.Sort((a, b) => b.Name.CompareTo(a.Name));
                }
                else if (sortBy == T2IAPI.ImageHistorySortMode.Date || sortBy == T2IAPI.ImageHistorySortMode.DateEdited)
                {
                    list.Sort((a, b) => b.Metadata.FileTime.CompareTo(a.Metadata.FileTime));
                }
                else if (sortBy == T2IAPI.ImageHistorySortMode.DateCreated)
                {
                    list.Sort((a, b) => b.FileCreatedTime.CompareTo(a.FileCreatedTime));
                }
                else if (sortBy == T2IAPI.ImageHistorySortMode.Rating)
                {
                    list.Sort((a, b) => cachedRating(b.Metadata).CompareTo(cachedRating(a.Metadata)));
                }
                else if (sortBy == T2IAPI.ImageHistorySortMode.Resolution)
                {
                    list.Sort((a, b) => cachedResolution(b.Metadata).CompareTo(cachedResolution(a.Metadata)));
                }
                else if (sortBy == T2IAPI.ImageHistorySortMode.Model)
                {
                    list.Sort((a, b) => cachedModel(b.Metadata).CompareTo(cachedModel(a.Metadata)));
                }
                else if (sortBy == T2IAPI.ImageHistorySortMode.Seed)
                {
                    list.Sort((a, b) => cachedSeed(b.Metadata).CompareTo(cachedSeed(a.Metadata)));
                }
                else if (sortBy == T2IAPI.ImageHistorySortMode.FileSize)
                {
                    list.Sort((a, b) => b.FileSize.CompareTo(a.FileSize));
                }
                if (sortReverse)
                {
                    list.Reverse();
                }
            }
            List<string> dirs;
            List<T2IAPI.ImageHistoryHelper> files;
            if (fastFirst)
            {
                long fastStart = Environment.TickCount64;
                int startupLimit = Math.Max(1, Math.Min(fastFirstLimit, maxInHistory));
                HashSet<string> seenDirs = [];
                HashSet<string> traversedDirs = [];
                dirs = [];
                files = [];
                void collectFastFirstFiles(string dir, int subDepth)
                {
                    if (!traversedDirs.Add(dir))
                    {
                        return;
                    }
                    if (files.Count >= startupLimit)
                    {
                        return;
                    }
                    string prefix = dir == "" ? "" : dir + "/";
                    string actualPath = $"{path}/{prefix}";
                    actualPath = UserImageHistoryHelper.GetRealPathFor(session.User, actualPath, root: root);
                    if (!Directory.Exists(actualPath))
                    {
                        return;
                    }
                    IEnumerable<string> orderedFiles = Directory.EnumerateFiles(actualPath)
                        .Select(f => f.Replace('\\', '/'))
                        .Where(isAllowed)
                        .Where(f => !f.AfterLast('/').StartsWithFast('.') && extensions.Contains(f.AfterLast('.')) && !f.EndsWith(".swarmpreview.jpg") && !f.EndsWith(".swarmpreview.webp"))
                        .OrderDescending()
                        .Take(startupLimit - files.Count);
                    files.AddRange(orderedFiles
                        .Select(f => GetImageHistoryHelper(prefix + f.AfterLast('/'), f, root, starNoFolders))
                        .Where(f => f.Metadata is not null)
                        .Where(f => includeHidden || !MetadataIsHidden(f.Metadata))
                        .Where(f => filterTerms.Count == 0 || ScannedImageHistoryFilterMatches(f, filterTerms)));
                    if (files.Count >= startupLimit || subDepth <= 0)
                    {
                        return;
                    }
                    IEnumerable<string> subDirs;
                    if (dir == "")
                    {
                        subDirs = Directory.EnumerateDirectories(actualPath).Select(Path.GetFileName).Concat(specialSeedDirs).Distinct().OrderDescending();
                    }
                    else
                    {
                        subDirs = Directory.EnumerateDirectories(actualPath).Select(Path.GetFileName).OrderDescending();
                    }
                    foreach (string subDir in subDirs)
                    {
                        if (subDir.StartsWithFast('.'))
                        {
                            continue;
                        }
                        string subPath = dir == "" ? subDir : $"{dir}/{subDir}";
                        if (!isAllowed(subPath))
                        {
                            continue;
                        }
                        if (seenDirs.Add(subPath))
                        {
                            dirs.Add(subPath);
                        }
                        collectFastFirstFiles(subPath, subDepth - 1);
                        if (files.Count >= startupLimit)
                        {
                            return;
                        }
                    }
                }
                collectFastFirstFiles("", depth);
                fileScanMs = Environment.TickCount64 - fastStart;
            }
            else
            {
                long dirStart = Environment.TickCount64;
                ConcurrentDictionary<string, string> dirsConc = [];
                HashSet<string> traversedDirs = [];
                void addDirs(string dir, int subDepth)
                {
                    if (dir.EndsWith('/'))
                    {
                        dir = dir[..^1];
                    }
                    if (!traversedDirs.Add(dir))
                    {
                        return;
                    }
                    if (dir != "")
                    {
                        (subDepth == 0 ? finalDirs : dirsConc).TryAdd(dir, dir);
                    }
                    if (subDepth > 0)
                    {
                        string actualPath = $"{path}/{dir}";
                        actualPath = UserImageHistoryHelper.GetRealPathFor(session.User, actualPath, root: root);
                        if (!Directory.Exists(actualPath))
                        {
                            return;
                        }
                        IEnumerable<string> subDirs = Directory.EnumerateDirectories(actualPath).Select(Path.GetFileName).OrderDescending();
                        foreach (string subDir in subDirs)
                        {
                            if (subDir.StartsWithFast('.'))
                            {
                                continue;
                            }
                            string subPath = dir == "" ? subDir : $"{dir}/{subDir}";
                            if (isAllowed(subPath))
                            {
                                addDirs(subPath, subDepth - 1);
                            }
                        }
                    }
                }
                addDirs("", depth);
                foreach (string specialFolder in UserImageHistoryHelper.SharedSpecialFolders.Keys)
                {
                    if (specialFolder.StartsWith(rawRefPath))
                    {
                        addDirs(specialFolder[rawRefPath.Length..], 1);
                    }
                }
                dirs = [.. dirsConc.Keys.OrderDescending()];
                if (sortReverse)
                {
                    dirs.Reverse();
                }
                dirScanMs = Environment.TickCount64 - dirStart;
                long fileStart = Environment.TickCount64;
                ConcurrentDictionary<int, List<T2IAPI.ImageHistoryHelper>> filesConc = [];
                int id = 0;
                int remaining = limit;
                int remainingScans = filterTerms.Count == 0 ? 0 : maxScanned;
                Parallel.ForEach(dirs.Append(""), new ParallelOptions() { MaxDegreeOfParallelism = 5, CancellationToken = Program.GlobalProgramCancel }, folder =>
                {
                    int localId = Interlocked.Increment(ref id);
                    int localLimit = filterTerms.Count == 0 ? Interlocked.CompareExchange(ref remaining, 0, 0) : Interlocked.CompareExchange(ref remainingScans, 0, 0);
                    if (localLimit <= 0)
                    {
                        return;
                    }
                    string prefix = folder == "" ? "" : folder + "/";
                    string actualPath = $"{path}/{prefix}";
                    actualPath = UserImageHistoryHelper.GetRealPathFor(session.User, actualPath, root: root);
                    if (!Directory.Exists(actualPath))
                    {
                        return;
                    }
                    List<string> subFiles = [.. Directory.EnumerateFiles(actualPath).Take(localLimit)];
                    IEnumerable<string> newFileNames = subFiles.Select(f => f.Replace('\\', '/')).Where(isAllowed).Where(f => !f.AfterLast('/').StartsWithFast('.') && extensions.Contains(f.AfterLast('.')) && !f.EndsWith(".swarmpreview.jpg") && !f.EndsWith(".swarmpreview.webp"));
                    List<T2IAPI.ImageHistoryHelper> localFiles = [.. newFileNames.Select(f => GetImageHistoryHelper(prefix + f.AfterLast('/'), f, root, starNoFolders)).Where(f => f.Metadata is not null).Where(f => includeHidden || !MetadataIsHidden(f.Metadata)).Where(f => filterTerms.Count == 0 || ScannedImageHistoryFilterMatches(f, filterTerms))];
                    int leftOver = filterTerms.Count == 0 ? Interlocked.Add(ref remaining, -localFiles.Count) : Interlocked.Add(ref remainingScans, -subFiles.Count);
                    sortList(localFiles);
                    filesConc.TryAdd(localId, localFiles);
                    if (leftOver <= 0)
                    {
                        return;
                    }
                });
                files = [.. filesConc.Values.SelectMany(f => f).Take(limit)];
                fileScanMs = Environment.TickCount64 - fileStart;
            }
            long finalSortStart = Environment.TickCount64;
            HashSet<string> included = [.. files.Select(f => f.Name)];
            for (int i = 0; i < files.Count; i++)
            {
                if (!files[i].Name.StartsWith("Starred/"))
                {
                    string starPath = $"Starred/{(session.User.Settings.StarNoFolders ? files[i].Name.Replace("/", "") : files[i].Name)}";
                    if (included.Contains(starPath))
                    {
                        files[i] = files[i] with { Name = null };
                    }
                }
            }
            files = [.. files.Where(f => f.Name is not null)];
            sortList(files);
            finalSortMs = Environment.TickCount64 - finalSortStart;
            long timeEnd = Environment.TickCount64;
            Logs.Verbose($"Listed {files.Count} images from {dirs.Count} folder entries in {(timeEnd - timeStart) / 1000.0:0.###} seconds (dirScan={dirScanMs / 1000.0:0.###}s, fileScan={fileScanMs / 1000.0:0.###}s, finalSort={finalSortMs / 1000.0:0.###}s, fastFirst={fastFirst}).");
            return new JObject()
            {
                ["folders"] = JToken.FromObject(dirs.Union(finalDirs.Keys).ToList()),
                ["files"] = JToken.FromObject(files.Take(maxInHistory).Select(f => new JObject() { ["src"] = f.Name, ["metadata"] = f.Metadata.Metadata, ["file_size"] = f.FileSize, ["file_time"] = f.Metadata.FileTime, ["file_created_time"] = f.FileCreatedTime }).ToList()),
                ["perf"] = new JObject()
                {
                    ["total_ms"] = timeEnd - timeStart,
                    ["dir_scan_ms"] = dirScanMs,
                    ["file_scan_ms"] = fileScanMs,
                    ["final_sort_ms"] = finalSortMs,
                    ["fast_first"] = fastFirst
                }
            };
        }
        catch (Exception ex)
        {
            if (ex is FileNotFoundException || ex is DirectoryNotFoundException || ex is PathTooLongException)
            {
                return new JObject() { ["error"] = "404, path not found." };
            }
            else
            {
                Logs.Error($"Error reading file list: {ex.ReadableString()}");
                return new JObject() { ["error"] = "Error reading file list." };
            }
        }
    }

    /// <summary>Returns whether a relative image history path is safe to index under the output root.</summary>
    private static bool CanIndexHistoryPrefix(string relativePrefix)
    {
        relativePrefix = (relativePrefix ?? "").Replace('\\', '/').Trim('/');
        return relativePrefix != ".." && !relativePrefix.StartsWith("../");
    }

    /// <summary>Indexes one image history folder.</summary>
    private static (int Indexed, int Skipped) IndexImageHistoryFolder(string root, string checkedPath, bool starNoFolders, bool clearIndex, bool rebuildMetadata)
    {
        int indexed = 0;
        int skipped = 0;
        string relativePrefix = OutputMetadataTracker.GetRelativePath(checkedPath, root);
        if (!CanIndexHistoryPrefix(relativePrefix))
        {
            return (indexed, skipped);
        }
        if (clearIndex)
        {
            OutputMetadataTracker.RemoveHistoryIndexPrefixComplete(root, relativePrefix);
            OutputMetadataTracker.RemoveHistoryIndexForPrefix(root, relativePrefix);
        }
        foreach (string rawFile in Directory.EnumerateFiles(checkedPath, "*", SearchOption.AllDirectories))
        {
            string file = rawFile.Replace('\\', '/');
            string filename = file.AfterLast('/');
            string ext = file.AfterLast('.').ToLowerFast();
            if (filename.StartsWithFast('.') || !T2IAPI.HistoryExtensions.Contains(ext) || file.EndsWith(".swarmpreview.jpg") || file.EndsWith(".swarmpreview.webp"))
            {
                skipped++;
                continue;
            }
            if (rebuildMetadata)
            {
                OutputMetadataTracker.RemoveMetadataFor(file);
            }
            OutputMetadataTracker.OutputHistoryIndexEntry entry = OutputMetadataTracker.UpsertHistoryIndexForFile(file, root, starNoFolders);
            if (entry is null)
            {
                skipped++;
                continue;
            }
            indexed++;
        }
        OutputMetadataTracker.MarkHistoryIndexPrefixComplete(root, relativePrefix);
        return (indexed, skipped);
    }

    /// <summary>Starts a background history index warm-up for a folder if needed.</summary>
    private static void TryStartImageHistoryIndexWarmup(Session session, string root, string checkedPath, string relativePrefix)
    {
        relativePrefix = (relativePrefix ?? "").Replace('\\', '/').Trim('/');
        if (!CanIndexHistoryPrefix(relativePrefix) || OutputMetadataTracker.IsHistoryIndexPrefixComplete(root, relativePrefix))
        {
            return;
        }
        string warmupKey = $"{root}|{relativePrefix}";
        if (!T2IAPI.ImageHistoryIndexWarmups.TryAdd(warmupKey, 1))
        {
            return;
        }
        string userId = session.User.UserID;
        bool starNoFolders = session.User.Settings.StarNoFolders;
        _ = Utilities.RunCheckedTask(async () =>
        {
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(2), Program.GlobalProgramCancel);
                if (!Directory.Exists(checkedPath))
                {
                    return;
                }
                long timeStart = Environment.TickCount64;
                (int indexed, int skipped) = IndexImageHistoryFolder(root, checkedPath, starNoFolders, true, false);
                long timeEnd = Environment.TickCount64;
                Logs.Verbose($"Warmed image history index for user '{userId}' path '{relativePrefix}' with {indexed} indexed, {skipped} skipped in {(timeEnd - timeStart) / 1000.0:0.###} seconds.");
            }
            finally
            {
                T2IAPI.ImageHistoryIndexWarmups.TryRemove(warmupKey, out _);
            }
        }, "image history index warm-up");
    }

    [API.APIDescription("Gets a list of images in a saved image history folder.",
        """
            "folders": ["Folder1", "Folder2"],
            "files":
            [
                {
                    "src": "path/to/image.jpg",
                    "metadata": "some-metadata" // usually a JSON blob encoded as a string. Not guaranteed.
                }
            ]
        """)]
    public static async Task<JObject> ListImages(Session session,
        [API.APIParameter("The folder path to start the listing in. Use an empty string for root.")] string path,
        [API.APIParameter("Maximum depth (number of recursive folders) to search.")] int depth,
        [API.APIParameter("What to sort the list by - `Name`, `DateCreated`, `DateEdited`, `Date`, `Rating`, `Resolution`, `Model`, `Seed`, or `FileSize`. `Date` is accepted as an alias for `DateEdited`.")] string sortBy = "Name",
        [API.APIParameter("If true, the sorting should be done in reverse.")] bool sortReverse = false,
        [API.APIParameter("If true, include images marked as hidden.")] bool includeHidden = false,
        [API.APIParameter("If true, return only a bounded startup slice biased toward newest work.")] bool fastFirst = false,
        [API.APIParameter("Maximum number of files to return when fastFirst is enabled.")] int fastFirstLimit = 128,
        [API.APIParameter("If true, bypass the persistent history index and scan the folder directly.")] bool forceScan = false,
        [API.APIParameter("Optional text or field:value filter to apply server-side before limiting results.")] string filter = "")
    {
        if (!Enum.TryParse(sortBy, true, out T2IAPI.ImageHistorySortMode sortMode))
        {
            return new JObject() { ["error"] = $"Invalid sort mode '{sortBy}'." };
        }
        string root = Utilities.CombinePathWithAbsolute(Environment.CurrentDirectory, session.User.OutputDirectory);
        return GetListAPIInternal(session, path, root, T2IAPI.HistoryExtensions, f => true, depth, sortMode, sortReverse, includeHidden, fastFirst, fastFirstLimit, forceScan, filter);
    }

    [API.APIDescription("Rescan cached image history metadata for a folder.", "{ \"success\": true, \"indexed\": 10, \"skipped\": 0 }")]
    public static async Task<JObject> RescanImageMetadata(Session session,
        [API.APIParameter("The folder path to rescan. Use an empty string for root.")] string path,
        [API.APIParameter("If true, clear cached metadata for each file before rereading.")] bool rebuild)
    {
        string root = Utilities.CombinePathWithAbsolute(Environment.CurrentDirectory, session.User.OutputDirectory);
        (string checkedPath, string consoleError, string userError) = WebServer.CheckFilePath(root, path);
        if (consoleError is not null)
        {
            Logs.Error(consoleError);
            return new JObject() { ["error"] = userError };
        }
        checkedPath = UserImageHistoryHelper.GetRealPathFor(session.User, checkedPath, root: root);
        if (!Directory.Exists(checkedPath))
        {
            return new JObject() { ["error"] = "That folder does not exist." };
        }
        try
        {
            (int indexed, int skipped) = IndexImageHistoryFolder(root, checkedPath, session.User.Settings.StarNoFolders, rebuild, rebuild);
            return new JObject() { ["success"] = true, ["indexed"] = indexed, ["skipped"] = skipped };
        }
        catch (Exception ex)
        {
            Logs.Warning($"Error rescanning image history metadata for '{path}': {ex.ReadableString()}");
            return new JObject() { ["error"] = "Error rescanning image history metadata." };
        }
    }

    [API.APIDescription("Open an image folder in the file explorer. Used for local users directly.", "\"success\": true")]
    public static async Task<JObject> OpenImageFolder(Session session,
        [API.APIParameter("The path to the image to show in the image folder.")] string path)
    {
        string origPath = path;
        string root = Utilities.CombinePathWithAbsolute(Environment.CurrentDirectory, session.User.OutputDirectory);
        (path, string consoleError, string userError) = WebServer.CheckFilePath(root, path);
        if (consoleError is not null)
        {
            Logs.Error(consoleError);
            return new JObject() { ["error"] = userError };
        }
        path = UserImageHistoryHelper.GetRealPathFor(session.User, path, root: root);
        if (!File.Exists(path))
        {
            Logs.Warning($"User {session.User.UserID} tried to open image path '{origPath}' which maps to '{path}', but cannot as the image does not exist.");
            return new JObject() { ["error"] = "That file does not exist, cannot open." };
        }
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            Process.Start("explorer.exe", $"/select,\"{Path.GetFullPath(path)}\"");
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            if (!(IsKdeDesktop() && TrySelectFileInDolphin(path)) && !TryShowFileInLinuxFileManager(path))
            {
                ProcessStartInfo info = new("xdg-open")
                {
                    UseShellExecute = false
                };
                info.ArgumentList.Add(Path.GetDirectoryName(Path.GetFullPath(path)));
                Process.Start(info);
            }
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            Process.Start("open", $"-R \"{Path.GetFullPath(path)}\"");
        }
        else
        {
            Logs.Warning("Cannot open image path on unrecognized OS type.");
            return new JObject() { ["error"] = "Cannot open image folder on this OS." };
        }
        return new JObject() { ["success"] = true };
    }

    /// <summary>Returns whether the current desktop environment is KDE/Plasma.</summary>
    private static bool IsKdeDesktop()
    {
        string currentDesktop = Environment.GetEnvironmentVariable("XDG_CURRENT_DESKTOP") ?? "";
        string desktopSession = Environment.GetEnvironmentVariable("DESKTOP_SESSION") ?? "";
        return currentDesktop.Contains("KDE", StringComparison.OrdinalIgnoreCase) || desktopSession.Contains("kde", StringComparison.OrdinalIgnoreCase) || desktopSession.Contains("plasma", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Attempts to select a file in KDE Dolphin, returning false if unsupported.</summary>
    private static bool TrySelectFileInDolphin(string path)
    {
        try
        {
            ProcessStartInfo info = new("dolphin")
            {
                UseShellExecute = false
            };
            info.ArgumentList.Add("--select");
            info.ArgumentList.Add(Path.GetFullPath(path));
            Process process = Process.Start(info);
            if (process is null)
            {
                return false;
            }
            return !process.WaitForExit(1000) || process.ExitCode == 0;
        }
        catch (Exception ex)
        {
            Logs.Verbose($"Could not select file in Dolphin: {ex.ReadableString()}");
            return false;
        }
    }

    /// <summary>Attempts to select a file in the Linux file manager, returning false if unsupported.</summary>
    private static bool TryShowFileInLinuxFileManager(string path)
    {
        try
        {
            string fileUri = new Uri(Path.GetFullPath(path)).AbsoluteUri;
            ProcessStartInfo info = new("dbus-send")
            {
                UseShellExecute = false
            };
            info.ArgumentList.Add("--session");
            info.ArgumentList.Add("--dest=org.freedesktop.FileManager1");
            info.ArgumentList.Add("--type=method_call");
            info.ArgumentList.Add("/org/freedesktop/FileManager1");
            info.ArgumentList.Add("org.freedesktop.FileManager1.ShowItems");
            info.ArgumentList.Add($"array:string:{fileUri}");
            info.ArgumentList.Add("string:");
            Process process = Process.Start(info);
            if (process is null)
            {
                return false;
            }
            return process.WaitForExit(1000) && process.ExitCode == 0;
        }
        catch (Exception ex)
        {
            Logs.Verbose($"Could not select file in Linux file manager: {ex.ReadableString()}");
            return false;
        }
    }

    [API.APIDescription("Delete an image from history.", "\"success\": true")]
    public static async Task<JObject> DeleteImage(Session session,
        [API.APIParameter("The path to the image to delete.")] string path)
    {
        string origPath = path;
        string root = Utilities.CombinePathWithAbsolute(Environment.CurrentDirectory, session.User.OutputDirectory);
        (path, string consoleError, string userError) = WebServer.CheckFilePath(root, path);
        if (consoleError is not null)
        {
            Logs.Error(consoleError);
            return new JObject() { ["error"] = userError };
        }
        path = UserImageHistoryHelper.GetRealPathFor(session.User, path, root: root);
        if (!File.Exists(path))
        {
            Logs.Warning($"User {session.User.UserID} tried to delete image path '{origPath}' which maps to '{path}', but cannot as the image does not exist.");
            return new JObject() { ["error"] = "That file does not exist, cannot delete." };
        }
        string standardizedPath = Path.GetFullPath(path);
        Session.RecentlyBlockedFilenames[standardizedPath] = standardizedPath;
        Action<string> deleteFile = Program.ServerSettings.Paths.RecycleDeletedImages ? Utilities.SendFileToRecycle : File.Delete;
        deleteFile(path);
        string fileBase = path.BeforeLast('.');
        foreach (string str in T2IAPI.DeletableFileExtensions)
        {
            string altFile = $"{fileBase}{str}";
            if (File.Exists(altFile))
            {
                deleteFile(altFile);
            }
        }
        OutputMetadataTracker.RemoveMetadataFor(path);
        RemoveHistoryIndexForPath(root, path);
        return new JObject() { ["success"] = true };
    }

    [API.APIDescription("Copy or move selected images to another output subfolder.", "\"success\": true")]
    public static async Task<JObject> BulkMoveImages(Session session,
        [API.APIParameter("Image paths to copy or move.")] string[] paths,
        [API.APIParameter("Output subfolder to copy or move into.")] string targetFolder,
        [API.APIParameter("Either copy or move.")] string mode)
    {
        bool copyMode = mode == "copy";
        bool moveMode = mode == "move";
        if (!copyMode && !moveMode)
        {
            return new JObject() { ["error"] = "Mode must be copy or move." };
        }
        string root = Utilities.CombinePathWithAbsolute(Environment.CurrentDirectory, session.User.OutputDirectory);
        targetFolder = (targetFolder ?? "").Replace('\\', '/').Trim('/');
        (string targetRoot, string consoleError, string userError) = WebServer.CheckFilePath(root, targetFolder);
        if (consoleError is not null)
        {
            Logs.Error(consoleError);
            return new JObject() { ["error"] = userError };
        }
        targetRoot = UserImageHistoryHelper.GetRealPathFor(session.User, targetRoot, root: root);
        Directory.CreateDirectory(targetRoot);
        int changed = 0;
        int failed = 0;
        foreach (string pathToken in paths ?? [])
        {
            string rawPath = pathToken.Replace('\\', '/').Trim('/');
            (string sourcePath, string pathConsoleError, _) = WebServer.CheckFilePath(root, rawPath);
            if (pathConsoleError is not null)
            {
                failed++;
                continue;
            }
            sourcePath = UserImageHistoryHelper.GetRealPathFor(session.User, sourcePath, root: root);
            if (!File.Exists(sourcePath))
            {
                failed++;
                continue;
            }
            string targetPath = $"{targetRoot}/{Path.GetFileName(sourcePath)}";
            if (File.Exists(targetPath))
            {
                string beforeDot = targetPath.BeforeLast('.');
                string ext = targetPath.AfterLast('.');
                int suffix = 1;
                while (File.Exists($"{beforeDot}-{suffix}.{ext}"))
                {
                    suffix++;
                }
                targetPath = $"{beforeDot}-{suffix}.{ext}";
            }
            Directory.CreateDirectory(Path.GetDirectoryName(targetPath));
            if (copyMode)
            {
                File.Copy(sourcePath, targetPath);
            }
            else
            {
                File.Move(sourcePath, targetPath);
                OutputMetadataTracker.RemoveMetadataFor(sourcePath);
                RemoveHistoryIndexForPath(root, sourcePath);
            }
            string sourceBase = sourcePath.BeforeLast('.');
            string targetBase = targetPath.BeforeLast('.');
            foreach (string ext in T2IAPI.DeletableFileExtensions)
            {
                string sourceAlt = $"{sourceBase}{ext}";
                if (!File.Exists(sourceAlt))
                {
                    continue;
                }
                if (copyMode)
                {
                    File.Copy(sourceAlt, $"{targetBase}{ext}", true);
                }
                else
                {
                    File.Move(sourceAlt, $"{targetBase}{ext}", true);
                }
            }
            RefreshHistoryIndexForPath(root, targetPath, session.User.Settings.StarNoFolders);
            changed++;
        }
        return new JObject() { ["success"] = true, ["changed"] = changed, ["failed"] = failed };
    }

    [API.APIDescription("Toggle whether an image is starred or not.", "\"new_state\": true")]
    public static async Task<JObject> ToggleImageStarred(Session session,
        [API.APIParameter("The path to the image to star.")] string path)
    {
        bool wasStar = false;
        path = path.Replace('\\', '/').Trim('/');
        if (path.StartsWith("Starred/"))
        {
            wasStar = true;
            path = path["Starred/".Length..];
        }
        string origPath = path;
        string root = Utilities.CombinePathWithAbsolute(Environment.CurrentDirectory, session.User.OutputDirectory);
        (path, string consoleError, string userError) = WebServer.CheckFilePath(root, path);
        if (consoleError is not null)
        {
            Logs.Error(consoleError);
            return new JObject() { ["error"] = userError };
        }
        path = UserImageHistoryHelper.GetRealPathFor(session.User, path, root: root);
        string pathBeforeDot = path.BeforeLast('.');
        string starPath = $"Starred/{(session.User.Settings.StarNoFolders ? origPath.Replace("/", "") : origPath)}";
        (starPath, _, _) = WebServer.CheckFilePath(root, starPath);
        starPath = UserImageHistoryHelper.GetRealPathFor(session.User, starPath, root: root);
        string starBeforeDot = starPath.BeforeLast('.');
        if (!File.Exists(path))
        {
            if (wasStar && File.Exists(starPath))
            {
                Logs.Debug($"User {session.User.UserID} un-starred '{path}' without a raw, moving back to raw");
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                File.Move(starPath, path);
                foreach (string ext in T2IAPI.DeletableFileExtensions)
                {
                    if (File.Exists($"{starBeforeDot}{ext}"))
                    {
                        File.Move($"{starBeforeDot}{ext}", $"{pathBeforeDot}{ext}");
                    }
                }
                OutputMetadataTracker.RemoveMetadataFor(path);
                OutputMetadataTracker.RemoveMetadataFor(starPath);
                RemoveHistoryIndexForPath(root, starPath);
                RefreshHistoryIndexForPath(root, path, session.User.Settings.StarNoFolders);
                return new JObject() { ["new_state"] = false };
            }
            Logs.Warning($"User {session.User.UserID} tried to star image path '{origPath}' which maps to '{path}', but cannot as the image does not exist.");
            return new JObject() { ["error"] = "That file does not exist, cannot star." };
        }
        if (File.Exists(starPath))
        {
            Logs.Debug($"User {session.User.UserID} un-starred '{path}'");
            File.Delete(starPath);
            foreach (string ext in T2IAPI.DeletableFileExtensions)
            {
                if (File.Exists($"{starBeforeDot}{ext}"))
                {
                    File.Delete($"{starBeforeDot}{ext}");
                }
            }
            OutputMetadataTracker.RemoveMetadataFor(path);
            OutputMetadataTracker.RemoveMetadataFor(starPath);
            RemoveHistoryIndexForPath(root, starPath);
            RefreshHistoryIndexForPath(root, path, session.User.Settings.StarNoFolders);
            return new JObject() { ["new_state"] = false };
        }
        else
        {
            Logs.Debug($"User {session.User.UserID} starred '{path}'");
            Directory.CreateDirectory(Path.GetDirectoryName(starPath));
            File.Copy(path, starPath);
            foreach (string ext in T2IAPI.DeletableFileExtensions)
            {
                if (File.Exists($"{pathBeforeDot}{ext}"))
                {
                    File.Copy($"{pathBeforeDot}{ext}", $"{starBeforeDot}{ext}");
                }
            }
            OutputMetadataTracker.RemoveMetadataFor(path);
            OutputMetadataTracker.RemoveMetadataFor(starPath);
            RefreshHistoryIndexForPath(root, path, session.User.Settings.StarNoFolders);
            RefreshHistoryIndexForPath(root, starPath, session.User.Settings.StarNoFolders);
            return new JObject() { ["new_state"] = true };
        }
    }

    [API.APIDescription("Toggle whether an image is hidden in history or not.", "\"new_state\": true")]
    public static async Task<JObject> ToggleImageHidden(Session session,
        [API.APIParameter("The path to the image to hide or unhide.")] string path)
    {
        bool wasStar = false;
        path = path.Replace('\\', '/').Trim('/');
        if (path.StartsWith("Starred/"))
        {
            wasStar = true;
            path = path["Starred/".Length..];
        }
        string origPath = path;
        string root = Utilities.CombinePathWithAbsolute(Environment.CurrentDirectory, session.User.OutputDirectory);
        (path, string consoleError, string userError) = WebServer.CheckFilePath(root, path);
        if (consoleError is not null)
        {
            Logs.Error(consoleError);
            return new JObject() { ["error"] = userError };
        }
        string rawPath = UserImageHistoryHelper.GetRealPathFor(session.User, path, root: root);
        string starPath = $"Starred/{(session.User.Settings.StarNoFolders ? origPath.Replace("/", "") : origPath)}";
        (starPath, _, _) = WebServer.CheckFilePath(root, starPath);
        starPath = UserImageHistoryHelper.GetRealPathFor(session.User, starPath, root: root);
        string primaryPath = wasStar ? starPath : rawPath;
        bool rawExists = File.Exists(rawPath);
        bool starExists = File.Exists(starPath);
        if (!File.Exists(primaryPath) && !rawExists && !starExists)
        {
            Logs.Warning($"User {session.User.UserID} tried to hide image path '{origPath}' which maps to '{primaryPath}', but cannot as the image does not exist.");
            return new JObject() { ["error"] = "That file does not exist, cannot hide." };
        }
        string rawMarker = $"{rawPath.BeforeLast('.')}{OutputMetadataTracker.HiddenMarkerExtension}";
        string starMarker = $"{starPath.BeforeLast('.')}{OutputMetadataTracker.HiddenMarkerExtension}";
        bool currentlyHidden = (rawExists && File.Exists(rawMarker)) || (starExists && File.Exists(starMarker));
        bool newState = !currentlyHidden;
        static void setMarker(string marker, bool fileExists, bool hidden)
        {
            if (!fileExists)
            {
                return;
            }
            if (hidden)
            {
                File.WriteAllText(marker, "1");
            }
            else if (File.Exists(marker))
            {
                File.Delete(marker);
            }
        }
        setMarker(rawMarker, rawExists, newState);
        if (rawPath != starPath)
        {
            setMarker(starMarker, starExists, newState);
        }
        if (rawExists)
        {
            OutputMetadataTracker.RemoveMetadataFor(rawPath.Replace('\\', '/'));
            RefreshHistoryIndexForPath(root, rawPath, session.User.Settings.StarNoFolders);
        }
        if (starExists)
        {
            OutputMetadataTracker.RemoveMetadataFor(starPath.Replace('\\', '/'));
            RefreshHistoryIndexForPath(root, starPath, session.User.Settings.StarNoFolders);
        }
        Logs.Debug($"User {session.User.UserID} {(newState ? "hid" : "unhid")} image '{origPath}'");
        return new JObject() { ["new_state"] = newState };
    }

    [API.APIDescription("Set a user rating for an image in history.", "\"rating\": 5")]
    public static async Task<JObject> SetImageRating(Session session,
        [API.APIParameter("The path to the image to rate.")] string path,
        [API.APIParameter("Rating value from 0 through 5.")] int rating)
    {
        if (rating < 0 || rating > 5)
        {
            return new JObject() { ["error"] = "Rating must be from 0 through 5." };
        }
        bool wasStar = false;
        path = path.Replace('\\', '/').Trim('/');
        if (path.StartsWith("Starred/"))
        {
            wasStar = true;
            path = path["Starred/".Length..];
        }
        string origPath = path;
        string root = Utilities.CombinePathWithAbsolute(Environment.CurrentDirectory, session.User.OutputDirectory);
        (path, string consoleError, string userError) = WebServer.CheckFilePath(root, path);
        if (consoleError is not null)
        {
            Logs.Error(consoleError);
            return new JObject() { ["error"] = userError };
        }
        string rawPath = UserImageHistoryHelper.GetRealPathFor(session.User, path, root: root);
        string starPath = $"Starred/{(session.User.Settings.StarNoFolders ? origPath.Replace("/", "") : origPath)}";
        (starPath, _, _) = WebServer.CheckFilePath(root, starPath);
        starPath = UserImageHistoryHelper.GetRealPathFor(session.User, starPath, root: root);
        string primaryPath = wasStar ? starPath : rawPath;
        bool rawExists = File.Exists(rawPath);
        bool starExists = File.Exists(starPath);
        if (!File.Exists(primaryPath) && !rawExists && !starExists)
        {
            Logs.Warning($"User {session.User.UserID} tried to rate image path '{origPath}' which maps to '{primaryPath}', but cannot as the image does not exist.");
            return new JObject() { ["error"] = "That file does not exist, cannot rate." };
        }
        static void setRating(string imagePath, bool fileExists, int newRating)
        {
            if (!fileExists)
            {
                return;
            }
            string sidecarPath = $"{imagePath.BeforeLast('.')}.swarm.json";
            JObject metadata = new();
            if (File.Exists(sidecarPath))
            {
                try
                {
                    metadata = File.ReadAllText(sidecarPath).ParseToJson();
                }
                catch (Exception)
                {
                    metadata = new();
                }
            }
            metadata["rating"] = newRating;
            File.WriteAllText(sidecarPath, metadata.ToString(Newtonsoft.Json.Formatting.Indented));
        }
        setRating(rawPath, rawExists, rating);
        if (rawPath != starPath)
        {
            setRating(starPath, starExists, rating);
        }
        if (rawExists)
        {
            OutputMetadataTracker.RemoveMetadataFor(rawPath.Replace('\\', '/'));
            RefreshHistoryIndexForPath(root, rawPath, session.User.Settings.StarNoFolders);
        }
        if (starExists)
        {
            OutputMetadataTracker.RemoveMetadataFor(starPath.Replace('\\', '/'));
            RefreshHistoryIndexForPath(root, starPath, session.User.Settings.StarNoFolders);
        }
        return new JObject() { ["rating"] = rating };
    }

    [API.APIDescription("Add or remove user tags for an image in history.", "\"tags\": [\"tag\"]")]
    public static async Task<JObject> SetImageTags(Session session,
        [API.APIParameter("The path to the image to tag.")] string path,
        [API.APIParameter("Comma-separated tags to add or remove.")] string tags,
        [API.APIParameter("Tag mode, either add or remove.")] string mode)
    {
        List<string> tagList = [.. (tags ?? "").Split(',').Select(t => t.Trim()).Where(t => !string.IsNullOrWhiteSpace(t)).Distinct(StringComparer.OrdinalIgnoreCase)];
        if (tagList.Count == 0)
        {
            return new JObject() { ["error"] = "No tags were provided." };
        }
        bool shouldAdd = mode == "add";
        bool shouldRemove = mode == "remove";
        if (!shouldAdd && !shouldRemove)
        {
            return new JObject() { ["error"] = "Tag mode must be add or remove." };
        }
        bool wasStar = false;
        path = path.Replace('\\', '/').Trim('/');
        if (path.StartsWith("Starred/"))
        {
            wasStar = true;
            path = path["Starred/".Length..];
        }
        string origPath = path;
        string root = Utilities.CombinePathWithAbsolute(Environment.CurrentDirectory, session.User.OutputDirectory);
        (path, string consoleError, string userError) = WebServer.CheckFilePath(root, path);
        if (consoleError is not null)
        {
            Logs.Error(consoleError);
            return new JObject() { ["error"] = userError };
        }
        string rawPath = UserImageHistoryHelper.GetRealPathFor(session.User, path, root: root);
        string starPath = $"Starred/{(session.User.Settings.StarNoFolders ? origPath.Replace("/", "") : origPath)}";
        (starPath, _, _) = WebServer.CheckFilePath(root, starPath);
        starPath = UserImageHistoryHelper.GetRealPathFor(session.User, starPath, root: root);
        string primaryPath = wasStar ? starPath : rawPath;
        bool rawExists = File.Exists(rawPath);
        bool starExists = File.Exists(starPath);
        if (!File.Exists(primaryPath) && !rawExists && !starExists)
        {
            Logs.Warning($"User {session.User.UserID} tried to tag image path '{origPath}' which maps to '{primaryPath}', but cannot as the image does not exist.");
            return new JObject() { ["error"] = "That file does not exist, cannot tag." };
        }
        static JArray getTags(JObject metadata)
        {
            if (metadata["tags"] is JArray arr)
            {
                return arr;
            }
            if (metadata["tags"] is JValue value && value.Value is string text)
            {
                return new JArray(text.Split(',').Select(t => t.Trim()).Where(t => !string.IsNullOrWhiteSpace(t)));
            }
            return new JArray();
        }
        static JArray setTags(string imagePath, bool fileExists, List<string> changedTags, bool addMode)
        {
            if (!fileExists)
            {
                return null;
            }
            string sidecarPath = $"{imagePath.BeforeLast('.')}.swarm.json";
            JObject metadata = new();
            if (File.Exists(sidecarPath))
            {
                try
                {
                    metadata = File.ReadAllText(sidecarPath).ParseToJson();
                }
                catch (Exception)
                {
                    metadata = new();
                }
            }
            List<string> existing = [.. getTags(metadata).Select(t => $"{t}").Where(t => !string.IsNullOrWhiteSpace(t))];
            if (addMode)
            {
                foreach (string tag in changedTags)
                {
                    if (!existing.Any(t => t.Equals(tag, StringComparison.OrdinalIgnoreCase)))
                    {
                        existing.Add(tag);
                    }
                }
            }
            else
            {
                existing = [.. existing.Where(t => !changedTags.Any(r => r.Equals(t, StringComparison.OrdinalIgnoreCase)))];
            }
            JArray result = new(existing);
            metadata["tags"] = result;
            File.WriteAllText(sidecarPath, metadata.ToString(Newtonsoft.Json.Formatting.Indented));
            return result;
        }
        JArray currentTags = setTags(rawPath, rawExists, tagList, shouldAdd);
        if (rawPath != starPath)
        {
            JArray starTags = setTags(starPath, starExists, tagList, shouldAdd);
            if (currentTags is null)
            {
                currentTags = starTags;
            }
        }
        if (rawExists)
        {
            OutputMetadataTracker.RemoveMetadataFor(rawPath.Replace('\\', '/'));
            RefreshHistoryIndexForPath(root, rawPath, session.User.Settings.StarNoFolders);
        }
        if (starExists)
        {
            OutputMetadataTracker.RemoveMetadataFor(starPath.Replace('\\', '/'));
            RefreshHistoryIndexForPath(root, starPath, session.User.Settings.StarNoFolders);
        }
        return new JObject() { ["tags"] = currentTags ?? new JArray() };
    }

    [API.APIDescription("Set user notes for an image in history.", "\"notes\": \"text\"")]
    public static async Task<JObject> SetImageNotes(Session session,
        [API.APIParameter("The path to the image to annotate.")] string path,
        [API.APIParameter("Note text to save.")] string notes)
    {
        notes ??= "";
        bool wasStar = false;
        path = path.Replace('\\', '/').Trim('/');
        if (path.StartsWith("Starred/"))
        {
            wasStar = true;
            path = path["Starred/".Length..];
        }
        string origPath = path;
        string root = Utilities.CombinePathWithAbsolute(Environment.CurrentDirectory, session.User.OutputDirectory);
        (path, string consoleError, string userError) = WebServer.CheckFilePath(root, path);
        if (consoleError is not null)
        {
            Logs.Error(consoleError);
            return new JObject() { ["error"] = userError };
        }
        string rawPath = UserImageHistoryHelper.GetRealPathFor(session.User, path, root: root);
        string starPath = $"Starred/{(session.User.Settings.StarNoFolders ? origPath.Replace("/", "") : origPath)}";
        (starPath, _, _) = WebServer.CheckFilePath(root, starPath);
        starPath = UserImageHistoryHelper.GetRealPathFor(session.User, starPath, root: root);
        string primaryPath = wasStar ? starPath : rawPath;
        bool rawExists = File.Exists(rawPath);
        bool starExists = File.Exists(starPath);
        if (!File.Exists(primaryPath) && !rawExists && !starExists)
        {
            Logs.Warning($"User {session.User.UserID} tried to annotate image path '{origPath}' which maps to '{primaryPath}', but cannot as the image does not exist.");
            return new JObject() { ["error"] = "That file does not exist, cannot annotate." };
        }
        static void setNotes(string imagePath, bool fileExists, string newNotes)
        {
            if (!fileExists)
            {
                return;
            }
            string sidecarPath = $"{imagePath.BeforeLast('.')}.swarm.json";
            JObject metadata = new();
            if (File.Exists(sidecarPath))
            {
                try
                {
                    metadata = File.ReadAllText(sidecarPath).ParseToJson();
                }
                catch (Exception)
                {
                    metadata = new();
                }
            }
            metadata["notes"] = newNotes;
            File.WriteAllText(sidecarPath, metadata.ToString(Newtonsoft.Json.Formatting.Indented));
        }
        setNotes(rawPath, rawExists, notes);
        if (rawPath != starPath)
        {
            setNotes(starPath, starExists, notes);
        }
        if (rawExists)
        {
            OutputMetadataTracker.RemoveMetadataFor(rawPath.Replace('\\', '/'));
            RefreshHistoryIndexForPath(root, rawPath, session.User.Settings.StarNoFolders);
        }
        if (starExists)
        {
            OutputMetadataTracker.RemoveMetadataFor(starPath.Replace('\\', '/'));
            RefreshHistoryIndexForPath(root, starPath, session.User.Settings.StarNoFolders);
        }
        return new JObject() { ["notes"] = notes };
    }
}
