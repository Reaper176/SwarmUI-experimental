using FreneticUtilities.FreneticExtensions;
using FreneticUtilities.FreneticToolkit;
using LiteDB;
using Newtonsoft.Json.Linq;
using SwarmUI.Accounts;
using SwarmUI.Core;
using SwarmUI.Media;
using System.IO;

namespace SwarmUI.Utils;

/// <summary>Helper class to track output file metadata.</summary>
public static class OutputMetadataTracker
{
    /// <summary>BSON database entry for image metadata.</summary>
    public class OutputMetadataEntry
    {
        [BsonId]
        public string FileName { get; set; }

        public string Metadata { get; set; }

        public long FileTime { get; set; }

        public long LastVerified { get; set; } // Reading file time can be slow, so don't do more than once per day per file.
    }

    /// <summary>BSON database entry for image preview thumbnails.</summary>
    public class OutputPreviewEntry
    {
        [BsonId]
        public string FileName { get; set; }

        public long FileTime { get; set; }

        public long LastVerified { get; set; }

        public byte[] PreviewData { get; set; }

        /// <summary>If PreviewData is animated, SimplifiedData is non-animated. SimplifiedData is often null.</summary>
        public byte[] SimplifiedData { get; set; }
    }

    /// <summary>BSON database entry for fast image history listing.</summary>
    public class OutputHistoryIndexEntry
    {
        /// <summary>Relative image path from the output root.</summary>
        [BsonId]
        public string RelativePath { get; set; }

        /// <summary>Relative folder path from the output root.</summary>
        public string Folder { get; set; }

        /// <summary>Lowercase file extension, without dot.</summary>
        public string Extension { get; set; }

        /// <summary>Raw metadata JSON string.</summary>
        public string Metadata { get; set; }

        /// <summary>Best available file modified timestamp.</summary>
        public long FileTime { get; set; }

        /// <summary>Best available file created timestamp.</summary>
        public long FileCreatedTime { get; set; }

        /// <summary>File size in bytes.</summary>
        public long FileSize { get; set; }

        /// <summary>Whether the image is marked hidden.</summary>
        public bool IsHidden { get; set; }

        /// <summary>Whether the image is in the starred folder.</summary>
        public bool IsStarred { get; set; }

        /// <summary>User rating value.</summary>
        public double Rating { get; set; }

        /// <summary>Final image resolution as width times height.</summary>
        public long ResolutionPixels { get; set; }

        /// <summary>Lowercase model name from metadata.</summary>
        public string Model { get; set; }

        /// <summary>Seed from metadata.</summary>
        public long Seed { get; set; }

        /// <summary>Unix timestamp for when this index entry was last verified.</summary>
        public long LastVerified { get; set; }
    }

    /// <summary>BSON database entry tracking completed fast history index prefixes.</summary>
    public class OutputHistoryIndexState
    {
        /// <summary>Relative output path prefix that has been fully indexed.</summary>
        [BsonId]
        public string RelativePrefix { get; set; }

        /// <summary>Unix timestamp for when this prefix was last fully indexed.</summary>
        public long LastRebuilt { get; set; }
    }

    public record class OutputDatabase(string Folder, LockObject Lock, LiteDatabase Database, ILiteCollection<OutputMetadataEntry> Metadata, ILiteCollection<OutputPreviewEntry> Previews, ILiteCollection<OutputHistoryIndexEntry> HistoryIndex, ILiteCollection<OutputHistoryIndexState> HistoryIndexStates)
    {
        public volatile int Errors = 0;

        public void HadNewError()
        {
            int newCount = Interlocked.Increment(ref Errors);
            if (newCount < 10)
            {
                return;
            }
            lock (Lock)
            {
                try
                {
                    Database.Dispose();
                    Errors = -1000;
                }
                catch (Exception) { }
                try
                {
                    File.Delete($"{Folder}/swarm_metadata.ldb");
                }
                catch (Exception) { }
                Databases.TryRemove(Folder, out _);
            }
        }

        public void Dispose()
        {
            try
            {
                Database.Dispose();
            }
            catch (Exception ex)
            {
                Logs.Error($"Error disposing image metadata database for folder '{Folder}': {ex.ReadableString()}");
            }
        }
    }

    /// <summary>Set of all image metadatabases, as a map from folder name to database.</summary>
    public static ConcurrentDictionary<string, OutputDatabase> Databases = new();

    /// <summary>Lock used to serialize LiteDB creation and index setup per process.</summary>
    public static LockObject DatabaseCreationLock = new();

    public class PreviewMemoryCacheEntry
    {
        public OutputPreviewEntry Entry;

        public long ExpiresAt;

        public LinkedListNode<string> LruNode;
    }

    /// <summary>In-memory LRU cache for generated previews to avoid repeated DB/disk hits during history browsing bursts.</summary>
    public static Dictionary<string, PreviewMemoryCacheEntry> PreviewMemoryCache = [];

    /// <summary>Linked-list ordering for <see cref="PreviewMemoryCache"/> where the tail is most recently used.</summary>
    public static LinkedList<string> PreviewMemoryCacheLru = [];

    /// <summary>Lock for <see cref="PreviewMemoryCache"/> and <see cref="PreviewMemoryCacheLru"/>.</summary>
    public static LockObject PreviewMemoryCacheLock = new();

    /// <summary>Maximum number of image previews to hold in-memory.</summary>
    public static int PreviewMemoryCacheMaxEntries = 512;

    /// <summary>How long preview entries stay alive in memory.</summary>
    public static int PreviewMemoryCacheMaxAgeSeconds = 120;

    public static void RemovePreviewFromMemoryCache(string file)
    {
        file = file.Replace('\\', '/');
        lock (PreviewMemoryCacheLock)
        {
            if (PreviewMemoryCache.TryGetValue(file, out PreviewMemoryCacheEntry existing))
            {
                PreviewMemoryCacheLru.Remove(existing.LruNode);
                PreviewMemoryCache.Remove(file);
            }
        }
    }

    public static bool TryGetPreviewFromMemoryCache(string file, out OutputPreviewEntry entry)
    {
        file = file.Replace('\\', '/');
        long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        lock (PreviewMemoryCacheLock)
        {
            if (!PreviewMemoryCache.TryGetValue(file, out PreviewMemoryCacheEntry existing))
            {
                entry = null;
                return false;
            }
            if (existing.ExpiresAt < now || existing.Entry is null)
            {
                PreviewMemoryCacheLru.Remove(existing.LruNode);
                PreviewMemoryCache.Remove(file);
                entry = null;
                return false;
            }
            PreviewMemoryCacheLru.Remove(existing.LruNode);
            PreviewMemoryCacheLru.AddLast(existing.LruNode);
            entry = existing.Entry;
            return true;
        }
    }

    public static void SetPreviewToMemoryCache(string file, OutputPreviewEntry entry)
    {
        if (entry is null)
        {
            return;
        }
        file = file.Replace('\\', '/');
        long expiry = DateTimeOffset.UtcNow.ToUnixTimeSeconds() + PreviewMemoryCacheMaxAgeSeconds;
        lock (PreviewMemoryCacheLock)
        {
            if (PreviewMemoryCache.TryGetValue(file, out PreviewMemoryCacheEntry existing))
            {
                existing.Entry = entry;
                existing.ExpiresAt = expiry;
                PreviewMemoryCacheLru.Remove(existing.LruNode);
                PreviewMemoryCacheLru.AddLast(existing.LruNode);
            }
            else
            {
                LinkedListNode<string> node = PreviewMemoryCacheLru.AddLast(file);
                PreviewMemoryCache[file] = new() { Entry = entry, ExpiresAt = expiry, LruNode = node };
            }
            while (PreviewMemoryCache.Count > PreviewMemoryCacheMaxEntries && PreviewMemoryCacheLru.First is not null)
            {
                string oldest = PreviewMemoryCacheLru.First.Value;
                PreviewMemoryCacheLru.RemoveFirst();
                PreviewMemoryCache.Remove(oldest);
            }
        }
    }

    /// <summary>Returns the database corresponding to the given folder path.</summary>
    public static OutputDatabase GetDatabaseForFolder(string folder)
    {
        if (!Program.ServerSettings.Metadata.ImageMetadataPerFolder)
        {
            folder = Program.DataDir;
        }
        else
        {
            folder = Path.GetFullPath(folder);
        }
        if (Databases.TryGetValue(folder, out OutputDatabase existingDatabase))
        {
            return existingDatabase;
        }
        lock (DatabaseCreationLock)
        {
            if (Databases.TryGetValue(folder, out existingDatabase))
            {
                return existingDatabase;
            }
            OutputDatabase database = CreateDatabaseForFolder(folder);
            Databases[folder] = database;
            return database;
        }
    }

    /// <summary>Creates and initializes the metadata database for a folder.</summary>
    private static OutputDatabase CreateDatabaseForFolder(string folder)
    {
        string path = $"{folder}/swarm_metadata.ldb";
        try
        {
            return TryCreateDatabaseForFolder(folder, path);
        }
        catch (Exception ex)
        {
            Logs.Warning($"Swarm output metadata store at '{path}' is corrupt or unavailable, deleting it and rebuilding: {ex.Message}");
            DeleteDatabaseFiles(folder, path);
            return TryCreateDatabaseForFolder(folder, path);
        }
    }

    /// <summary>Deletes a LiteDB metadata file and its paired log file.</summary>
    private static void DeleteDatabaseFiles(string folder, string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
            string logPath = $"{path.BeforeLast('.')}-log.{path.AfterLast('.')}";
            if (File.Exists(logPath))
            {
                File.Delete(logPath);
            }
            // TODO: TEMP 0.9.7: Clear out old image_metadata files.
            if (File.Exists($"{folder}/image_metadata.ldb"))
            {
                File.Delete($"{folder}/image_metadata.ldb");
            }
            if (File.Exists($"{folder}/image_metadata-log.ldb"))
            {
                File.Delete($"{folder}/image_metadata-log.ldb");
            }
        }
        catch (Exception deleteEx)
        {
            Logs.Warning($"Failed to delete corrupt output metadata store at '{path}': {deleteEx.ReadableString()}");
        }
    }

    /// <summary>Attempts to create and initialize the metadata database for a folder.</summary>
    private static OutputDatabase TryCreateDatabaseForFolder(string folder, string path)
    {
        LiteDatabase ldb = null;
        try
        {
            ldb = new(path);
            // TODO: TEMP 0.9.7: Clear out old image_metadata files.
            if (File.Exists($"{folder}/image_metadata.ldb"))
            {
                File.Delete($"{folder}/image_metadata.ldb");
            }
            ILiteCollection<OutputHistoryIndexEntry> historyIndex = ldb.GetCollection<OutputHistoryIndexEntry>("output_history");
            ILiteCollection<OutputHistoryIndexState> historyIndexStates = ldb.GetCollection<OutputHistoryIndexState>("output_history_state");
            historyIndex.EnsureIndex(e => e.Folder);
            historyIndex.EnsureIndex(e => e.FileTime);
            historyIndex.EnsureIndex(e => e.FileCreatedTime);
            historyIndex.EnsureIndex(e => e.Extension);
            return new(folder, new(), ldb, ldb.GetCollection<OutputMetadataEntry>("output_metadata"), ldb.GetCollection<OutputPreviewEntry>("output_previews"), historyIndex, historyIndexStates);
        }
        catch (Exception)
        {
            ldb?.Dispose();
            throw;
        }
    }

    /// <summary>File format extensions that even can have metadata on them.</summary>
    public static HashSet<string> ExtensionsWithMetadata = ["png", "jpg"];

    /// <summary>File format extensions that require ffmpeg to process image data.</summary>
    public static HashSet<string> ExtensionsForFfmpegables = ["webm", "mp4", "mov"];

    /// <summary>File format extensions that are animations in an image file format.</summary>
    public static HashSet<string> ExtensionsForAnimatedImages = ["webp", "gif"];

    /// <summary>Extra sidecar extension used to mark an image as hidden in history.</summary>
    public const string HiddenMarkerExtension = ".swarm.hidden";

    /// <summary>Applies a true/false boolean flag to metadata JSON if possible.</summary>
    public static string ApplyBooleanFlagToMetadata(string metadata, string key, bool enabled)
    {
        if (!enabled)
        {
            return metadata;
        }
        if (string.IsNullOrWhiteSpace(metadata))
        {
            return $"{{ \"{key}\": true }}";
        }
        try
        {
            JObject obj = metadata.ParseToJson();
            obj[key] = true;
            return obj.ToString();
        }
        catch (Exception)
        {
            return metadata;
        }
    }

    /// <summary>Merges top-level sidecar metadata over embedded image metadata.</summary>
    private static string MergeSidecarMetadata(string fileData, string sidecarPath)
    {
        if (!File.Exists(sidecarPath))
        {
            return fileData;
        }
        string sidecarData = File.ReadAllText(sidecarPath);
        if (string.IsNullOrWhiteSpace(fileData))
        {
            return sidecarData;
        }
        if (string.IsNullOrWhiteSpace(sidecarData))
        {
            return fileData;
        }
        try
        {
            JObject baseData = fileData.ParseToJson();
            JObject sidecar = sidecarData.ParseToJson();
            foreach (JProperty property in sidecar.Properties())
            {
                baseData[property.Name] = property.Value.DeepClone();
            }
            return baseData.ToString();
        }
        catch (Exception)
        {
            return fileData;
        }
    }

    /// <summary>Returns a normalized relative path from root to a file.</summary>
    public static string GetRelativePath(string file, string root)
    {
        file = file.Replace('\\', '/');
        root = root.Replace('\\', '/').TrimEnd('/');
        string relative = file == root ? "" : (file.StartsWith($"{root}/") ? file[(root.Length + 1)..] : Path.GetRelativePath(root, file));
        return relative.Replace('\\', '/').Trim('/');
    }

    /// <summary>Gets a top-level numeric metadata value.</summary>
    private static double GetMetadataDouble(JObject parsed, string key)
    {
        JToken token = parsed?[key];
        if (token is null)
        {
            return 0;
        }
        if (token.Type == JTokenType.Float || token.Type == JTokenType.Integer)
        {
            return token.Value<double>();
        }
        if (double.TryParse($"{token}", out double result))
        {
            return result;
        }
        return 0;
    }

    /// <summary>Gets a nested numeric metadata value.</summary>
    private static long GetMetadataLong(JObject parsed, string key)
    {
        JToken token = parsed?[key];
        if (token is null)
        {
            return 0;
        }
        if (token.Type == JTokenType.Integer || token.Type == JTokenType.Float)
        {
            return token.Value<long>();
        }
        if (long.TryParse($"{token}", out long result))
        {
            return result;
        }
        return 0;
    }

    /// <summary>Gets a top-level boolean metadata value.</summary>
    private static bool GetMetadataBool(JObject parsed, string key)
    {
        JToken token = parsed?[key];
        if (token is null)
        {
            return false;
        }
        if (token.Type == JTokenType.Boolean)
        {
            return token.Value<bool>();
        }
        return bool.TryParse($"{token}", out bool result) && result;
    }

    /// <summary>Gets the preferred final pixel count from parsed metadata.</summary>
    private static long GetMetadataResolutionPixels(JObject parsed)
    {
        JObject parameters = parsed?["sui_image_params"] as JObject;
        long width = GetMetadataLong(parameters, "width");
        long height = GetMetadataLong(parameters, "height");
        JObject extra = parsed?["sui_extra_data"] as JObject;
        long finalWidth = GetMetadataLong(extra, "final_width");
        long finalHeight = GetMetadataLong(extra, "final_height");
        width = finalWidth == 0 ? width : finalWidth;
        height = finalHeight == 0 ? height : finalHeight;
        return width * height;
    }

    /// <summary>Creates or updates the fast history index entry for a file.</summary>
    public static OutputHistoryIndexEntry UpsertHistoryIndexForFile(string file, string root, bool starNoFolders)
    {
        file = file.Replace('\\', '/');
        if (!File.Exists(file))
        {
            return null;
        }
        OutputMetadataEntry metadata = GetMetadataFor(file, root, starNoFolders);
        if (metadata is null)
        {
            return null;
        }
        string relative = GetRelativePath(file, root);
        string folder = relative.Contains('/') ? relative.BeforeLast('/') : "";
        string extension = relative.AfterLast('.').ToLowerFast();
        long timeNow = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        long fileSize = 0;
        try
        {
            fileSize = new FileInfo(file).Length;
        }
        catch (Exception)
        {
        }
        long fileCreatedTime = 0;
        try
        {
            fileCreatedTime = ((DateTimeOffset)File.GetCreationTimeUtc(file)).ToUnixTimeSeconds();
        }
        catch (Exception)
        {
        }
        JObject parsed = null;
        try
        {
            parsed = metadata.Metadata?.ParseToJson();
        }
        catch (Exception)
        {
        }
        JObject parameters = parsed?["sui_image_params"] as JObject;
        OutputHistoryIndexEntry entry = new()
        {
            RelativePath = relative,
            Folder = folder,
            Extension = extension,
            Metadata = metadata.Metadata,
            FileTime = metadata.FileTime,
            FileCreatedTime = fileCreatedTime,
            FileSize = fileSize,
            IsHidden = GetMetadataBool(parsed, "is_hidden"),
            IsStarred = GetMetadataBool(parsed, "is_starred"),
            Rating = GetMetadataDouble(parsed, "rating"),
            ResolutionPixels = GetMetadataResolutionPixels(parsed),
            Model = $"{parameters?["model"]}".ToLowerFast(),
            Seed = GetMetadataLong(parameters, "seed"),
            LastVerified = timeNow
        };
        OutputDatabase database = GetDatabaseForFolder(root);
        lock (database.Lock)
        {
            database.HistoryIndex.Upsert(entry);
        }
        return entry;
    }

    /// <summary>Returns all fast history index entries for an output root.</summary>
    public static List<OutputHistoryIndexEntry> GetHistoryIndexForRoot(string root)
    {
        OutputDatabase database = GetDatabaseForFolder(root);
        lock (database.Lock)
        {
            return [.. database.HistoryIndex.FindAll()];
        }
    }

    /// <summary>Returns fast history index entries under a relative folder prefix.</summary>
    public static List<OutputHistoryIndexEntry> GetHistoryIndexForPrefix(string root, string relativePrefix)
    {
        relativePrefix = (relativePrefix ?? "").Replace('\\', '/').Trim('/');
        if (string.IsNullOrWhiteSpace(relativePrefix))
        {
            return GetHistoryIndexForRoot(root);
        }
        string folderPrefix = $"{relativePrefix}/";
        OutputDatabase database = GetDatabaseForFolder(root);
        lock (database.Lock)
        {
            return [.. database.HistoryIndex.Find(Query.Or(Query.EQ(nameof(OutputHistoryIndexEntry.Folder), relativePrefix), Query.StartsWith(nameof(OutputHistoryIndexEntry.Folder), folderPrefix)))];
        }
    }

    /// <summary>Normalizes a relative prefix for storage as a non-empty LiteDB ID.</summary>
    private static string NormalizeHistoryIndexStatePrefix(string relativePrefix)
    {
        relativePrefix = (relativePrefix ?? "").Replace('\\', '/').Trim('/');
        return string.IsNullOrWhiteSpace(relativePrefix) ? "." : relativePrefix;
    }

    /// <summary>Normalizes a stored history index state prefix for comparison.</summary>
    private static string CompareHistoryIndexStatePrefix(string relativePrefix)
    {
        relativePrefix = (relativePrefix ?? "").Replace('\\', '/').Trim('/');
        return relativePrefix == "." ? "" : relativePrefix;
    }

    /// <summary>Marks a history index prefix as fully rebuilt.</summary>
    public static void MarkHistoryIndexPrefixComplete(string root, string relativePrefix)
    {
        relativePrefix = NormalizeHistoryIndexStatePrefix(relativePrefix);
        OutputDatabase database = GetDatabaseForFolder(root);
        OutputHistoryIndexState state = new()
        {
            RelativePrefix = relativePrefix,
            LastRebuilt = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
        };
        lock (database.Lock)
        {
            database.HistoryIndexStates.Upsert(state);
        }
    }

    /// <summary>Returns whether the history index is complete for a requested relative prefix.</summary>
    public static bool IsHistoryIndexPrefixComplete(string root, string relativePrefix)
    {
        relativePrefix = (relativePrefix ?? "").Replace('\\', '/').Trim('/');
        OutputDatabase database = GetDatabaseForFolder(root);
        lock (database.Lock)
        {
            return database.HistoryIndexStates.FindAll().Any(s =>
            {
                string statePrefix = CompareHistoryIndexStatePrefix(s.RelativePrefix);
                return string.IsNullOrWhiteSpace(statePrefix) || relativePrefix == statePrefix || relativePrefix.StartsWith($"{statePrefix}/");
            });
        }
    }

    /// <summary>Removes completed history index state markers under a prefix.</summary>
    public static void RemoveHistoryIndexPrefixComplete(string root, string relativePrefix)
    {
        relativePrefix = CompareHistoryIndexStatePrefix(relativePrefix);
        OutputDatabase database = GetDatabaseForFolder(root);
        lock (database.Lock)
        {
            List<string> ids = [.. database.HistoryIndexStates.FindAll()
                .Where(s =>
                {
                    string statePrefix = CompareHistoryIndexStatePrefix(s.RelativePrefix);
                    return string.IsNullOrWhiteSpace(relativePrefix) || statePrefix == relativePrefix || statePrefix.StartsWith($"{relativePrefix}/");
                })
                .Select(s => s.RelativePrefix)];
            foreach (string id in ids)
            {
                database.HistoryIndexStates.Delete(id);
            }
        }
    }

    /// <summary>Removes fast history index entries under a relative path prefix.</summary>
    public static int RemoveHistoryIndexForPrefix(string root, string relativePrefix)
    {
        relativePrefix = (relativePrefix ?? "").Replace('\\', '/').Trim('/');
        string folderPrefix = string.IsNullOrWhiteSpace(relativePrefix) ? "" : $"{relativePrefix}/";
        OutputDatabase database = GetDatabaseForFolder(root);
        lock (database.Lock)
        {
            List<string> ids = [.. database.HistoryIndex.FindAll()
                .Where(e => string.IsNullOrWhiteSpace(relativePrefix) || e.RelativePath == relativePrefix || e.RelativePath.StartsWith(folderPrefix))
                .Select(e => e.RelativePath)];
            foreach (string id in ids)
            {
                database.HistoryIndex.Delete(id);
            }
            return ids.Count;
        }
    }

    /// <summary>Deletes any tracked metadata for the given filepath.</summary>
    public static void RemoveMetadataFor(string file)
    {
        RemovePreviewFromMemoryCache(file);
        string folder = file.BeforeAndAfterLast('/', out string filename);
        OutputDatabase metadata;
        try
        {
            metadata = GetDatabaseForFolder(folder);
        }
        catch (Exception ex)
        {
            Logs.Warning($"Error opening image metadata database to remove file '{file}': {ex.ReadableString()}");
            return;
        }
        if (!Program.ServerSettings.Metadata.ImageMetadataPerFolder)
        {
            filename = file;
        }
        lock (metadata.Lock)
        {
            metadata.Metadata.Delete(filename);
            metadata.Previews.Delete(filename);
        }
    }

    /// <summary>Get the preview bytes for the given image, going through a cache manager.</summary>
    public static OutputPreviewEntry GetOrCreatePreviewFor(string file)
    {
        file = file.Replace('\\', '/');
        string ext = file.AfterLast('.');
        string folder = file.BeforeAndAfterLast('/', out string filename);
        MediaType expectedMediaType = MediaType.GetByExtension(ext);
        if (expectedMediaType is not null && expectedMediaType.MetaType == MediaMetaType.Audio)
        {
            return null;
        }
        if (TryGetPreviewFromMemoryCache(file, out OutputPreviewEntry cached))
        {
            return cached;
        }
        if (!Program.ServerSettings.Metadata.ImageMetadataPerFolder)
        {
            filename = file;
        }
        OutputDatabase metadata;
        try
        {
            metadata = GetDatabaseForFolder(folder);
        }
        catch (Exception ex)
        {
            Logs.Warning($"Error opening image preview database for file '{file}': {ex.ReadableString()}");
            return null;
        }
        long timeNow = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        try
        {
            OutputPreviewEntry entry;
            lock (metadata.Lock)
            {
                entry = metadata.Previews.FindById(filename);
            }
            if (entry is not null)
            {
                if (Math.Abs(timeNow - entry.LastVerified) > 60 * 60 * 24)
                {
                    float chance = Program.ServerSettings.Performance.ImageDataValidationChance;
                    if (chance == 0 || Random.Shared.NextDouble() > chance)
                    {
                        SetPreviewToMemoryCache(file, entry);
                        return entry;
                    }
                    long fTime = ((DateTimeOffset)File.GetLastWriteTimeUtc(file)).ToUnixTimeSeconds();
                    if (entry.FileTime != fTime)
                    {
                        entry = null;
                    }
                    else
                    {
                        entry.LastVerified = timeNow;
                        lock (metadata.Lock)
                        {
                            metadata.Previews.Upsert(entry);
                        }
                    }
                }
                if (entry is not null)
                {
                    SetPreviewToMemoryCache(file, entry);
                    return entry;
                }
            }
        }
        catch (Exception ex)
        {
            Logs.Warning($"Error reading image metadata for file '{file}' from database: {ex.ReadableString()}");
            metadata.HadNewError();
        }
        if (!File.Exists(file))
        {
            return null;
        }
        long fileTime = ((DateTimeOffset)File.GetLastWriteTimeUtc(file)).ToUnixTimeSeconds();
        byte[] fileData = null;
        byte[] simplifiedData = null;
        try
        {
            string animPreview = $"{file.BeforeLast('.')}.swarmpreview.webp";
            string jpegPreview = $"{file.BeforeLast('.')}.swarmpreview.jpg";
            string altPreview = animPreview;
            bool altExists = false;
            if (ExtensionsForFfmpegables.Contains(ext) || ExtensionsForAnimatedImages.Contains(ext))
            {
                altExists = Program.ServerSettings.UI.AllowAnimatedPreviews && File.Exists(altPreview);
                if (!altExists)
                {
                    altPreview = jpegPreview;
                    altExists = File.Exists(altPreview);
                }
            }
            if ((ExtensionsForFfmpegables.Contains(ext) || ExtensionsForAnimatedImages.Contains(ext) || !ExtensionsWithMetadata.Contains(ext)) && !altExists)
            {
                altPreview = animPreview;
                if (ExtensionsForAnimatedImages.Contains(ext))
                {
                    byte[] data = File.ReadAllBytes(file);
                    fileData = data;
                    ImageFile img = new Image(data, MediaType.GetByExtension(ext));
                    if (ext == "webp" && img.ToIS.Frames.Count == 1)
                    {
                        fileData = img.ToMetadataJpg()?.RawData;
                    }
                    else
                    {
                        simplifiedData = img.ToMetadataJpg().RawData;
                        File.WriteAllBytes(jpegPreview, simplifiedData);
                        ImageFile webpAnim = img.ToWebpPreviewAnim();
                        if (webpAnim is null)
                        {
                            fileData = simplifiedData;
                            simplifiedData = null;
                            altPreview = jpegPreview;
                            altExists = true;
                        }
                        else
                        {
                            fileData = webpAnim.RawData;
                            File.WriteAllBytes(animPreview, fileData);
                            altExists = true;
                        }
                    }
                }
                else if (ExtensionsForFfmpegables.Contains(ext))
                {
                    UserImageHistoryHelper.DoFfmpegPreviewGeneration(file).Wait();
                    altExists = Program.ServerSettings.UI.AllowAnimatedPreviews && File.Exists(altPreview);
                    if (!altExists)
                    {
                        altPreview = jpegPreview;
                        altExists = File.Exists(altPreview);
                    }
                }
                else
                {
                    return null;
                }
            }
            if (fileData is null)
            {
                byte[] data = File.ReadAllBytes(altExists ? altPreview : file);
                if (data.Length == 0)
                {
                    return null;
                }
                if (altExists && altPreview.EndsWith(".webp"))
                {
                    fileData = data;
                    if (File.Exists(jpegPreview))
                    {
                        simplifiedData = File.ReadAllBytes(jpegPreview);
                    }
                }
                else
                {
                    ImageFile newFile = new Image(data, MediaType.GetByExtension(ext));
                    fileData = newFile.ToMetadataJpg()?.RawData;
                }
            }
        }
        catch (Exception ex)
        {
            Logs.Warning($"Error reading image preview for file '{file}': {ex.ReadableString()}");
            return null;
        }
        try
        {
            OutputPreviewEntry entry = new() { FileName = filename, PreviewData = fileData, SimplifiedData = simplifiedData, LastVerified = timeNow, FileTime = fileTime };
            lock (metadata.Lock)
            {
                metadata.Previews.Upsert(entry);
            }
            SetPreviewToMemoryCache(file, entry);
            return entry;
        }
        catch (Exception ex)
        {
            Logs.Debug($"Error saving image preview for file '{file}' to database: {ex.ReadableString()}");
            metadata.HadNewError();
            return null;
        }
    }

    /// <summary>Get the metadata text for the given file, going through a cache manager.</summary>
    public static OutputMetadataEntry GetMetadataFor(string file, string root, bool starNoFolders)
    {
        file = file.Replace('\\', '/');
        string ext = file.AfterLast('.');
        string folder = file.BeforeAndAfterLast('/', out string filename);
        if (!Program.ServerSettings.Metadata.ImageMetadataPerFolder)
        {
            filename = file;
        }
        OutputDatabase metadata;
        try
        {
            metadata = GetDatabaseForFolder(folder);
        }
        catch (Exception ex)
        {
            Logs.Warning($"Error opening image metadata database for file '{file}': {ex.ReadableString()}");
            return null;
        }
        long timeNow = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        try
        {
            OutputMetadataEntry existingEntry;
            lock (metadata.Lock)
            {
                existingEntry = metadata.Metadata.FindById(filename);
            }
            if (existingEntry is not null)
            {
                float chance = Program.ServerSettings.Performance.ImageDataValidationChance;
                if (chance == 0 || Random.Shared.NextDouble() > chance)
                {
                    return existingEntry;
                }
                if (Math.Abs(timeNow - existingEntry.LastVerified) > 60 * 60 * 24)
                {
                    long fTime = ((DateTimeOffset)File.GetLastWriteTimeUtc(file)).ToUnixTimeSeconds();
                    if (existingEntry.FileTime != fTime)
                    {
                        existingEntry = null;
                    }
                    else
                    {
                        existingEntry.LastVerified = timeNow;
                        lock (metadata.Lock)
                        {
                            metadata.Metadata.Upsert(existingEntry);
                        }
                    }
                }
                if (existingEntry is not null)
                {
                    return existingEntry;
                }
            }
        }
        catch (Exception ex)
        {
            Logs.Warning($"Error reading image metadata for file '{file}' from database: {ex.ReadableString()}");
            metadata.HadNewError();
        }
        if (!File.Exists(file))
        {
            return null;
        }
        long fileTime = ((DateTimeOffset)File.GetLastWriteTimeUtc(file)).ToUnixTimeSeconds();
        string fileData = null;
        try
        {
            string altMetaPath = $"{file.BeforeLast('.')}.swarm.json";
            if (ExtensionsWithMetadata.Contains(ext))
            {
                byte[] data = File.ReadAllBytes(file);
                if (data.Length == 0)
                {
                    return null;
                }
                fileData = new Image(data, MediaType.GetByExtension(ext)).GetMetadata();
            }
            if (string.IsNullOrWhiteSpace(fileData) && File.Exists(altMetaPath))
            {
                fileData = File.ReadAllText(altMetaPath);
            }
            else
            {
                fileData = MergeSidecarMetadata(fileData, altMetaPath);
            }
            string subPath = file.StartsWith(root) ? file[root.Length..] : Path.GetRelativePath(root, file);
            subPath = subPath.Replace('\\', '/').Trim('/');
            string rawSubPath = subPath;
            if (starNoFolders)
            {
                subPath = subPath.Replace("/", "");
            }
            string starPath = $"{root}/Starred/{subPath}";
            bool isStarred = rawSubPath.StartsWith("Starred/") || File.Exists(starPath);
            fileData = ApplyBooleanFlagToMetadata(fileData, "is_starred", isStarred);
            bool isHidden = File.Exists($"{file.BeforeLast('.')}{HiddenMarkerExtension}");
            fileData = ApplyBooleanFlagToMetadata(fileData, "is_hidden", isHidden);
        }
        catch (Exception ex)
        {
            Logs.Warning($"Error reading image metadata for file '{file}': {ex.ReadableString()}");
            return null;
        }
        OutputMetadataEntry entry = new() { FileName = filename, Metadata = fileData, LastVerified = timeNow, FileTime = fileTime };
        try
        {
            lock (metadata.Lock)
            {
                metadata.Metadata.Upsert(entry);
            }
        }
        catch (Exception ex)
        {
            Logs.Debug($"Error writing image metadata for file '{file}' to database: {ex.ReadableString()}");
            metadata.HadNewError();
        }
        return entry;
    }

    /// <summary>Shuts down and stores metadata helper files.</summary>
    public static void Shutdown()
    {
        OutputDatabase[] dbs = [.. Databases.Values];
        Databases.Clear();
        foreach (OutputDatabase db in dbs)
        {
            lock (db.Lock)
            {
                db.Dispose();
            }
        }
    }

    public static void MassRemoveMetadata()
    {
        KeyValuePair<string, OutputDatabase>[] dbs = [.. Databases];
        static void remove(string name)
        {
            try
            {
                if (File.Exists($"{name}/swarm_metadata.ldb"))
                {
                    File.Delete($"{name}/swarm_metadata.ldb");
                }
                if (File.Exists($"{name}/swarm_metadata-log.ldb"))
                {
                    File.Delete($"{name}/swarm_metadata-log.ldb");
                }
                // TODO: TEMP: 0.9.7: "image_metadata" used to be the name of these files.
                if (File.Exists($"{name}/image_metadata.ldb"))
                {
                    File.Delete($"{name}/image_metadata.ldb");
                }
                if (File.Exists($"{name}/image_metadata-log.ldb"))
                {
                    File.Delete($"{name}/image_metadata-log.ldb");
                }
            }
            catch (IOException) { }
        }
        foreach ((string name, OutputDatabase db) in dbs)
        {
            lock (db.Lock)
            {
                db.Dispose();
                remove(name);
                Databases.TryRemove(name, out _);
            }
        }
        static void ClearFolder(string folder)
        {
            if (!Directory.Exists(folder))
            {
                return;
            }
            remove(folder);
            foreach (string subFolder in Directory.GetDirectories(folder))
            {
                ClearFolder(subFolder);
            }
        }
        ClearFolder(Utilities.CombinePathWithAbsolute(Environment.CurrentDirectory, Program.ServerSettings.Paths.OutputPath));
        ClearFolder(Program.DataDir);
    }
}
