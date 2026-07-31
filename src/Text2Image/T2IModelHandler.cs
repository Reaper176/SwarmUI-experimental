using FreneticUtilities.FreneticExtensions;
using FreneticUtilities.FreneticToolkit;
using LiteDB;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using SwarmUI.Accounts;
using SwarmUI.Core;
using SwarmUI.Media;
using SwarmUI.Utils;
using SwarmUI.WebAPI;
using System.Diagnostics;
using System.IO;
using System.Threading;

namespace SwarmUI.Text2Image;

/// <summary>Central manager for Text2Image models.</summary>
public class T2IModelHandler
{
    /// <summary>All models known to this handler.</summary>
    public ConcurrentDictionary<string, T2IModel> Models = new();

    /// <summary>Lock used when modifying the model list.</summary>
    public LockObject ModificationLock = new();

    /// <summary>If true, the engine is shutting down.</summary>
    public bool IsShutdown = false;

    /// <summary>Internal model metadata cache data (per folder).</summary>
    public static ConcurrentDictionary<string, ModelDatabase> ModelMetadataCachePerFolder = [];

    /// <summary>Lock for metadata processing.</summary>
    public LockObject MetadataLock = new();

    /// <summary>The type of model this handler is tracking (eg Stable-Diffusion, LoRA, VAE, Embedding, ...).</summary>
    public string ModelType;

    /// <summary>The full folder path for relevant models.</summary>
    public string[] FolderPaths = [];

    /// <summary>The full folder path to download models to.</summary>
    public string DownloadFolderPath;

    /// <summary>Quick internal tracker for unauthorized access errors, to aggregate the warning.</summary>
    public ConcurrentQueue<string> UnathorizedAccessSet = new();

    /// <summary>Paths found during the most recent scan for this model handler that contain risky model-name characters.</summary>
    public ConcurrentDictionary<string, byte> SpecialCharacterReportPaths = [];

    /// <summary>Lock for writing the shared special-character model path report.</summary>
    public static LockObject SpecialCharacterReportLock = new();

    /// <summary>Path to the report listing model files and folders with characters that may cause parsing issues.</summary>
    public static string SpecialCharacterReportPath => $"{Program.DataDir}/Temp/model_special_character_paths.txt";

    /// <summary>Temporary Rank 32 deterministic contract hook after sidecar fingerprint capture.</summary>
    private static Action Rank32AfterFingerprintHook = null;

    public record class ModelDatabase(string Folder, T2IModelHandler Handler, LiteDatabase Database, ILiteCollection<ModelMetadataStore> Metadata)
    {
        public volatile int Errors = 0;

        public void HadNewError()
        {
            int newCount = Interlocked.Increment(ref Errors);
            if (newCount < 10)
            {
                return;
            }
            lock (Handler.MetadataLock)
            {
                try
                {
                    Database.Dispose();
                    Errors = -1000;
                }
                catch (Exception) { }
                try
                {
                    File.Delete($"{Folder}/model_metadata.ldb");
                }
                catch (Exception) { }
                try
                {
                    File.Delete($"{Folder}/model_metadata-log.ldb");
                }
                catch (Exception) { }
                ModelMetadataCachePerFolder.TryRemove(Folder, out _);
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

    /// <summary>Helper, data store for model metadata.</summary>
    public class ModelMetadataStore
    {
        [BsonId]
        public string ModelName { get; set; }

        public long ModelFileVersion { get; set; }

        /// <summary>Ordered filesystem fingerprint of the supported model metadata sidecars.</summary>
        public string ModelSidecarFingerprint { get; set; }

        public string ModelClassType { get; set; }

        public string Title { get; set; }

        public string Author { get; set; }

        public string Description { get; set; }

        public string PreviewImage { get; set; }

        public int StandardWidth { get; set; }

        public int StandardHeight { get; set; }

        public bool IsNegativeEmbedding { get; set; }

        public string LoraDefaultWeight { get; set; }

        public string LoraDefaultConfinement { get; set; }

        public string License { get; set; }

        public string UsageHint { get; set; }

        public string TriggerPhrase { get; set; }

        public string[] Tags { get; set; }

        public string MergedFrom { get; set; }

        public string Date { get; set; }

        public string Preprocessor { get; set; }

        /// <summary>Time this model was last modified.</summary>
        public long TimeModified { get; set; }

        /// <summary>Time this model was created.</summary>
        public long TimeCreated { get; set; }

        public string Hash { get; set; }

        public string PredictionType { get; set; }

        /// <summary>Special cache of what text encoders the model appears to contain. Primarily for SD3 which has optional text encoders.</summary>
        public string TextEncoders { get; set; }

        /// <summary>Special format indicators, such as "bnb_nf4".</summary>
        public string SpecialFormat { get; set; }
    }

    public T2IModelHandler()
    {
        Program.ModelRefreshEvent += Refresh;
    }

    /// <summary>Marks this handler shut down and removes its model-refresh subscription without disposing the shared metadata cache.</summary>
    internal bool DetachFromModelRefresh()
    {
        if (IsShutdown)
        {
            return false;
        }
        IsShutdown = true;
        Program.ModelRefreshEvent -= Refresh;
        return true;
    }

    /// <summary>Removes and disposes every shared model metadata cache entry.</summary>
    internal static void DisposeSharedMetadataCache()
    {
        foreach (string folder in ModelMetadataCachePerFolder.Keys)
        {
            if (ModelMetadataCachePerFolder.TryRemove(folder, out ModelDatabase database))
            {
                database.Dispose();
            }
        }
    }

    public void Shutdown()
    {
        if (!DetachFromModelRefresh())
        {
            return;
        }
        lock (MetadataLock)
        {
            DisposeSharedMetadataCache();
        }
    }


    /// <summary>Utility to destroy all stored metadata files.</summary>
    public void MassRemoveMetadata()
    {
        lock (MetadataLock)
        {
            foreach (ModelDatabase db in ModelMetadataCachePerFolder.Values)
            {
                try
                {
                    db.Database.Dispose();
                }
                catch (Exception) { }
            }
            ModelMetadataCachePerFolder.Clear();
            static void ClearFolder(string folder)
            {
                try
                {
                    if (File.Exists($"{folder}/model_metadata.ldb"))
                    {
                        File.Delete($"{folder}/model_metadata.ldb");
                    }
                    if (File.Exists($"{folder}/model_metadata-log.ldb"))
                    {
                        File.Delete($"{folder}/model_metadata-log.ldb");
                    }
                }
                catch (Exception) { }
                try
                {
                    foreach (string subFolder in Directory.GetDirectories(folder))
                    {
                        ClearFolder(subFolder);
                    }
                }
                catch (Exception) { }
            }
            foreach (string path in FolderPaths)
            {
                ClearFolder(path);
            }
            ClearFolder(Program.DataDir);
        }
    }

    public HashSet<string> AllModelNames => [.. Models.Keys, .. ModelsAPI.InternalExtraModels(ModelType).Keys];

    public List<T2IModel> ListModelsFor(Session session)
    {
        Dictionary<string, JObject> extra = ModelsAPI.InternalExtraModels(ModelType);
        List<string> names = ListModelNamesFor(session);
        return [.. names.Where(n => n != "(None)").Select(m => GetModel(m, extra))];
    }

    public List<string> ListModelNamesFor(Session session)
    {
        if (IsShutdown)
        {
            return [];
        }
        if (session is null || session.User.IsAllowedAllModels)
        {
            return ["(None)", .. AllModelNames];
        }
        return ["(None)", .. AllModelNames.Where(session.User.IsAllowedModel)];
    }

    public T2IModel GetModel(string name, Dictionary<string, JObject> extra = null)
    {
        if (Models.TryGetValue(name, out T2IModel model) || Models.TryGetValue(name + ".safetensors", out model))
        {
            return model;
        }
        extra ??= ModelsAPI.InternalExtraModels(ModelType);
        if (extra.TryGetValue(name, out JObject extraModelData) || extra.TryGetValue(name + ".safetensors", out extraModelData))
        {
            return T2IModel.FromNetObject(extraModelData);
        }
        return null;
    }

    /// <summary>Refresh the model list.</summary>
    public void Refresh()
    {
        if (IsShutdown)
        {
            return;
        }
        SpecialCharacterReportPaths = [];
        try
        {
            List<string> usableFolderPaths = [];
            foreach (string path in FolderPaths)
            {
                if (Utilities.EnsureDirectory(path))
                {
                    usableFolderPaths.Add(path);
                }
                else
                {
                    Logs.Warning($"Skipping {ModelType} model path '{path}' because a non-directory file or broken symlink exists there.");
                }
            }
            ConcurrentDictionary<string, T2IModel> newModels = new();
            foreach (string path in usableFolderPaths)
            {
                AddAllFromFolder(path, "", newModels);
            }
            lock (ModificationLock)
            {
                Models = newModels;
            }
            Logs.Debug($"Have {Models.Count} {ModelType} models.");
            T2IModel[] dupped = [.. Models.Values.Where(m => m.OtherPaths.Count > 0)];
            if (dupped.Length > 0)
            {
                Logs.Debug($"There are {dupped.Length} {ModelType} models that have exactly matched filenames across different folders: '{dupped[0].RawFilePath}' is also stored in '{dupped[0].OtherPaths.JoinString("', '")}'");
            }
            if (UnathorizedAccessSet.Any())
            {
                Logs.Warning($"Got UnauthorizedAccessException while loading {ModelType} model paths: {UnathorizedAccessSet.Select(m => $"'{m}'").JoinString(", ")}");
                UnathorizedAccessSet.Clear();
            }
            WriteSpecialCharacterReport();
        }
        catch (Exception e)
        {
            Logs.Error($"Error while refreshing {ModelType} models: {e}");
        }
    }

    /// <summary>Records all file and folder paths for a model name that contain characters known to cause parser issues.</summary>
    public void RecordSpecialCharacterPaths(T2IModel model)
    {
        if (model is null || string.IsNullOrWhiteSpace(model.Name) || string.IsNullOrWhiteSpace(model.OriginatingFolderPath))
        {
            return;
        }
        string[] nameParts = model.Name.Replace('\\', '/').Split('/', StringSplitOptions.RemoveEmptyEntries);
        string path = model.OriginatingFolderPath.Replace('\\', '/').TrimEnd('/');
        foreach (string namePart in nameParts)
        {
            path = $"{path}/{namePart}";
            if (T2IModel.DangerousModelNameChars.ContainsAnyMatch(namePart))
            {
                SpecialCharacterReportPaths[path] = 0;
            }
        }
    }

    /// <summary>Writes the shared model special-character path report from every model handler's most recent scan.</summary>
    public static void WriteSpecialCharacterReport()
    {
        lock (SpecialCharacterReportLock)
        {
            try
            {
                string folder = SpecialCharacterReportPath.BeforeLast('/');
                Directory.CreateDirectory(folder);
                string[] paths = [.. Program.T2IModelSets.Values.SelectMany(h => h.SpecialCharacterReportPaths.Keys).Distinct().OrderBy(p => p.ToLowerFast())];
                File.WriteAllText(SpecialCharacterReportPath, paths.JoinString("\n") + (paths.Length > 0 ? "\n" : ""));
            }
            catch (Exception ex)
            {
                Logs.Warning($"Failed to write model special-character path report '{SpecialCharacterReportPath}': {ex.ReadableString()}");
            }
        }
    }

    /// <summary>Get (or create) the metadata cache for a given model folder.</summary>
    public ModelDatabase GetCacheForFolder(string folder)
    {
        lock (MetadataLock)
        {
            try
            {
                return ModelMetadataCachePerFolder.GetOrCreate(folder, () =>
                {
                    string path = $"{folder}/model_metadata.ldb";
                    LiteDatabase ldb;
                    try
                    {
                        ldb = new(path);
                    }
                    catch (Exception ex)
                    {
                        Logs.Verbose($"Failed to read Lite Database for '{folder}' and will reset it: {ex}");
                        File.Delete(path);
                        ldb = new(path);
                    }
                    return new(folder, this, ldb, ldb.GetCollection<ModelMetadataStore>("models"));
                });
            }
            catch (Exception ex)
            {
                Logs.Warning($"Internal error, some model metadata caches will be missing or invalid, see debug log for details.");
                Logs.Debug($"Failed to read Lite Database for '{folder}' and cannot reset it: {ex}");
                return null;
            }
        }
    }

    /// <summary>Updates the metadata cache database to the metadata assigned to this model object.</summary>
    public void ResetMetadataFrom(T2IModel model)
    {
        if (Program.NoPersist)
        {
            return;
        }
        ModelDatabase cache = null;
        try
        {
            bool perFolder = Program.ServerSettings.Metadata.ModelMetadataPerFolder;
            long modified = ((DateTimeOffset)File.GetLastWriteTimeUtc(model.RawFilePath)).ToUnixTimeMilliseconds();
            string folder = model.RawFilePath.Replace('\\', '/').BeforeAndAfterLast('/', out string fileName);
            string altModelPrefix = $"{model.OriginatingFolderPath}/{model.Name.BeforeLast('.')}";
            string sidecarFingerprint = GetModelSidecarFingerprint(altModelPrefix);
            cache = GetCacheForFolder(perFolder ? folder : Program.DataDir);
            if (cache is null)
            {
                return;
            }
            lock (ModificationLock)
            {
                model.Metadata ??= new();
                ModelMetadataStore metadata = model.Metadata;
                metadata.ModelFileVersion = modified;
                metadata.ModelSidecarFingerprint = sidecarFingerprint;
                metadata.ModelName = perFolder ? fileName : model.RawFilePath;
                metadata.Title = model.Title;
                metadata.Description = model.Description;
                metadata.ModelClassType = model.ModelClass?.ID;
                metadata.StandardWidth = model.StandardWidth;
                metadata.StandardHeight = model.StandardHeight;
                lock (MetadataLock)
                {
                    cache.Metadata.Upsert(metadata);
                }
            }
        }
        catch (Exception ex)
        {
            Logs.Error($"Failed to reset metadata for model '{model.RawFilePath}': {ex.ReadableString()}");
            cache?.HadNewError();
            throw;
        }
    }

    public static readonly string[] AutoImageFormatSuffixes = [".jpg", ".png", ".preview.png", ".preview.jpg", ".jpeg", ".preview.jpeg", ".thumb.jpg", ".thumb.png"];

    public static readonly string[] AltModelMetadataJsonFileSuffixes = [".swarm.json", ".json", ".cm-info.json", ".civitai.info"];

    /// <summary>Builds a stable ordered filesystem fingerprint for all supported model metadata sidecars.</summary>
    private static string GetModelSidecarFingerprint(string altModelPrefix)
    {
        List<string> entries = [];
        foreach (string altSuffix in AltModelMetadataJsonFileSuffixes)
        {
            FileInfo sidecar = new($"{altModelPrefix}{altSuffix}");
            if (!sidecar.Exists)
            {
                entries.Add($"{altSuffix}:missing");
            }
            else
            {
                entries.Add(FormattableString.Invariant($"{altSuffix}:{sidecar.Length}:{sidecar.LastWriteTimeUtc.Ticks}"));
            }
        }
        return entries.JoinString("|");
    }

    public static readonly string[] AllModelAttachedExtensions = [.. AutoImageFormatSuffixes.Concat(AltModelMetadataJsonFileSuffixes)];

    public static readonly string[] AltMetadataDescriptionKeys = ["VersionName", "VersionDescription", "ModelDescription", "description"];

    public static readonly string[] AltMetadataTriggerWordsKeys = ["TrainedWords", "trainedWords", "ss_tag_frequency"];

    public static readonly string[] AltMetadataNameKeys = ["UserTitle", "ModelName", "name"];

    public string GetAutoFormatImage(T2IModel model)
    {
        string prefix = $"{model.OriginatingFolderPath}/{model.Name.BeforeLast('.')}";
        foreach (string suffix in AutoImageFormatSuffixes)
        {
            try
            {
                if (File.Exists(prefix + suffix))
                {
                    ImageFile loaded = new Image(File.ReadAllBytes(prefix + suffix), MediaType.GetByExtension(suffix.AfterLast('.')));
                    return loaded.ToMetadataFormat();
                }
            }
            catch (Exception ex)
            {
                Logs.Error($"Caught an exception trying to load legacy model thumbnail at '{prefix}{suffix}'");
                Logs.Debug($"Details for above error {ex.ReadableString()}");
            }
        }
        return null;
    }

    /// <summary>Model compat-class IDs that have variable text encoder content.</summary>
    public static HashSet<string> VariableTextEncModelClasses = ["stable-diffusion-v3-medium", "stable-diffusion-v3.5-large", "stable-diffusion-v3.5-medium", "flux-1"];

    /// <summary>Force-load the metadata for a model.</summary>
    public void LoadMetadata(T2IModel model)
    {
        if (model is null)
        {
            Logs.Warning($"Tried to load metadata for a null model?:\n{Environment.StackTrace}");
            return;
        }
        if (model.ModelClass is not null || (model.Title is not null && model.Title != model.Name.AfterLast('/')))
        {
            Logs.Debug($"Not loading metadata for {model.Name} as it is already loaded.");
            return;
        }
        using ModelSidecarCostMeasurement.Operation measurement = ModelSidecarCostMeasurement.Begin();
        bool recomputed = false;
        string folder = model.RawFilePath.Replace('\\', '/').BeforeAndAfterLast('/', out string fileName);
        long modified = new DateTimeOffset(File.GetLastWriteTimeUtc(model.RawFilePath)).ToUnixTimeMilliseconds();
        string altModelPrefix = $"{model.OriginatingFolderPath}/{model.Name.BeforeLast('.')}";
        ModelSidecarCostMeasurement.PhaseToken phase = default;
        string sidecarFingerprint;
        if (measurement is null)
        {
            sidecarFingerprint = GetModelSidecarFingerprint(altModelPrefix);
        }
        else
        {
            phase = ModelSidecarCostMeasurement.BeginPhase(measurement, "fingerprint");
            sidecarFingerprint = GetModelSidecarFingerprint(altModelPrefix);
            ModelSidecarCostMeasurement.EndPhase(measurement, phase, "fingerprint");
            ModelSidecarCostMeasurement.NoteFingerprint(measurement, AltModelMetadataJsonFileSuffixes.Length);
            ModelSidecarCostMeasurement.RunAfterFingerprintHook(measurement, Rank32AfterFingerprintHook);
        }
        bool perFolder = Program.ServerSettings.Metadata.ModelMetadataPerFolder;
        ModelDatabase cache = GetCacheForFolder(perFolder ? folder : Program.DataDir);
        if (cache is null)
        {
            if (measurement is not null)
            {
                ModelSidecarCostMeasurement.MarkCacheUnavailable(measurement);
            }
            return;
        }
        ModelMetadataStore metadata;
        string modelCacheId = perFolder ? fileName : model.RawFilePath;
        bool cacheLookupFailed = false;
        if (measurement is not null)
        {
            phase = ModelSidecarCostMeasurement.BeginPhase(measurement, "cache_lookup");
        }
        lock (MetadataLock)
        {
            try
            {
                metadata = cache.Metadata.FindById(modelCacheId);
                if (measurement is not null)
                {
                    ModelSidecarCostMeasurement.EndPhase(measurement, phase, "cache_lookup");
                }
            }
            catch (Exception ex)
            {
                if (measurement is not null)
                {
                    ModelSidecarCostMeasurement.EndPhase(measurement, phase, "cache_lookup");
                    ModelSidecarCostMeasurement.NoteCaught(measurement, "cache_lookup_caught");
                }
                cacheLookupFailed = true;
                Logs.Debug($"Failed to load metadata for {model.Name} from cache:\n{ex.ReadableString()}");
                metadata = null;
            }
        }
        bool legacyTextEncoders = metadata is not null && metadata.TextEncoders is null && VariableTextEncModelClasses.Contains(metadata.ModelClassType);
        bool legacyNullFingerprint = metadata is not null && metadata.ModelSidecarFingerprint is null;
        if (legacyTextEncoders)
        {
            metadata = null;
        }
        if (metadata is null || metadata.ModelFileVersion != modified || metadata.ModelSidecarFingerprint != sidecarFingerprint)
        {
            recomputed = true;
            string invalidation = cacheLookupFailed ? "cache_lookup_fault"
                : legacyTextEncoders ? "legacy_text_encoders"
                : metadata is null ? "cache_missing"
                : metadata.ModelFileVersion != modified ? "model_mtime"
                : legacyNullFingerprint ? "legacy_null_fingerprint"
                : "sidecar_fingerprint";
            if (measurement is not null)
            {
                ModelSidecarCostMeasurement.BeginRecompute(measurement, invalidation);
            }
            string autoImg = GetAutoFormatImage(model);
            if (autoImg is not null)
            {
                model.PreviewImage = autoImg;
            }
            JObject headerData = [];
            JObject metaHeader = [];
            string textEncs = null;
            if (model.Name.EndsWith(".safetensors") || model.Name.EndsWith(".sft") || model.Name.EndsWith(".gguf"))
            {
                if (measurement is not null)
                {
                    phase = ModelSidecarCostMeasurement.BeginPhase(measurement, "embedded_header");
                }
                try
                {
                    headerData = T2IModel.GetMetadataHeaderFrom(model.RawFilePath);
                    if (headerData is not null)
                    {
                        metaHeader = headerData["__metadata__"] as JObject ?? [];
                        textEncs = "";
                        string[] keys = [.. headerData.Properties().Select(p => p.Name).Where(k => k.StartsWith("text_encoders."))];
                        if (keys.Any(k => k.StartsWith("text_encoders.clip_g."))) { textEncs += "clip_g,"; }
                        if (keys.Any(k => k.StartsWith("text_encoders.clip_l."))) { textEncs += "clip_l,"; }
                        if (keys.Any(k => k.StartsWith("text_encoders.t5xxl."))) { textEncs += "t5xxl,"; }
                        textEncs = textEncs.TrimEnd(',');
                    }
                    if (measurement is not null)
                    {
                        ModelSidecarCostMeasurement.EndPhase(measurement, phase, "embedded_header");
                    }
                }
                catch (Exception ex)
                {
                    if (measurement is not null)
                    {
                        ModelSidecarCostMeasurement.EndPhase(measurement, phase, "embedded_header");
                        ModelSidecarCostMeasurement.NoteCaught(measurement, "embedded_header_caught");
                    }
                    Logs.Warning($"Failed to load embedded metadata header for {model.Name}, continuing with sidecar metadata only:\n{ex.ReadableString()}");
                }
            }
            if (measurement is null)
            {
                foreach (string altSuffix in AltModelMetadataJsonFileSuffixes)
                {
                    if (File.Exists(altModelPrefix + altSuffix))
                    {
                        JObject altMetadata = File.ReadAllText(altModelPrefix + altSuffix).ParseToJson();
                        foreach (JProperty prop in altMetadata.Properties())
                        {
                            metaHeader[prop.Name] = prop.Value;
                            headerData[prop.Name] = prop.Value;
                        }
                    }
                }
            }
            else
            {
                foreach (string altSuffix in AltModelMetadataJsonFileSuffixes)
                {
                    string altPath = altModelPrefix + altSuffix;
                    phase = ModelSidecarCostMeasurement.BeginPhase(measurement, "first_exists");
                    bool exists = File.Exists(altPath);
                    ModelSidecarCostMeasurement.EndPhase(measurement, phase, "first_exists");
                    ModelSidecarCostMeasurement.NoteExists(measurement, true, exists);
                    if (exists)
                    {
                        phase = ModelSidecarCostMeasurement.BeginPhase(measurement, "first_read");
                        string rawMetadata = File.ReadAllText(altPath);
                        ModelSidecarCostMeasurement.EndPhase(measurement, phase, "first_read");
                        ModelSidecarCostMeasurement.NoteRead(measurement, true, rawMetadata.Length);
                        phase = ModelSidecarCostMeasurement.BeginPhase(measurement, "first_parse");
                        JObject altMetadata = rawMetadata.ParseToJson();
                        ModelSidecarCostMeasurement.EndPhase(measurement, phase, "first_parse");
                        ModelSidecarCostMeasurement.NoteParse(measurement, true, altMetadata.Count);
                        phase = ModelSidecarCostMeasurement.BeginPhase(measurement, "first_merge");
                        ModelSidecarCostMeasurement.NoteProcessing(measurement, true);
                        foreach (JProperty prop in altMetadata.Properties())
                        {
                            metaHeader[prop.Name] = prop.Value;
                            headerData[prop.Name] = prop.Value;
                        }
                        ModelSidecarCostMeasurement.EndPhase(measurement, phase, "first_merge");
                    }
                }
            }
            if (metaHeader.Count == 0)
            {
                Logs.Debug($"Not loading metadata for {model.Name} as it lacks a proper header (path='{altModelPrefix}').");
            }
            string altDescription = "", altName = null;
            HashSet<string> triggerPhrases = [];
            void procAltHeader(JObject altMetadata)
            {
                if (altMetadata.TryGetValue("model", out JToken modelSection) && modelSection is JObject modelSectionObj && modelSectionObj.TryGetValue("name", out JToken subNameTok))
                {
                    altName ??= subNameTok.Value<string>();
                }
                foreach (string nameKey in AltMetadataNameKeys)
                {
                    if (altMetadata.TryGetValue(nameKey, out JToken nameTok) && nameTok.Type != JTokenType.Null)
                    {
                        altName ??= nameTok.Value<string>();
                    }
                }
                foreach (string descKey in AltMetadataDescriptionKeys)
                {
                    if (altMetadata.TryGetValue(descKey, out JToken descTok) && descTok.Type != JTokenType.Null)
                    {
                        altDescription += descTok.Value<string>() + "\n";
                    }
                }
                if (Program.ServerSettings.Paths.UseSecondaryTriggerPhraseSources)
                {
                    foreach (string wordsKey in AltMetadataTriggerWordsKeys)
                    {
                        static string[] procWordsFrom(JToken tok)
                        {
                            if (tok.Type == JTokenType.Array)
                            {
                                return tok.ToObject<string[]>();
                            }
                            else if (tok is JObject jobj)
                            {
                                IEnumerable<string[]> wordSets = jobj.Properties().Select(p => p.Value is JObject subData ? procWordsFrom(subData) : [p.Name]);
                                return [.. wordSets.Flatten()];
                            }
                            else if (tok.Type == JTokenType.String)
                            {
                                string trainedWordsTok = tok.Value<string>();
                                if (trainedWordsTok.StartsWithFast('{') && trainedWordsTok.EndsWithFast('}'))
                                {
                                    try
                                    {
                                        return procWordsFrom(trainedWordsTok.ParseToJson());
                                    }
                                    catch (Exception) { } // Ignored
                                }
                                return trainedWordsTok.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
                            }
                            return null;
                        }
                        if (triggerPhrases.IsEmpty() && altMetadata.TryGetValue(wordsKey, out JToken wordsTok) && wordsTok.Type != JTokenType.Null)
                        {
                            string[] trainedWords = procWordsFrom(wordsTok);
                            if (trainedWords is not null && trainedWords.Length > 0)
                            {
                                triggerPhrases.UnionWith(trainedWords);
                            }
                        }
                    }
                }
                if (triggerPhrases.IsEmpty() && altMetadata.TryGetValue("activation text", out JToken actTok) && actTok.Type != JTokenType.Null)
                {
                    triggerPhrases.Add(actTok.Value<string>());
                }
            }
            if (measurement is null)
            {
                procAltHeader(metaHeader);
            }
            else
            {
                phase = ModelSidecarCostMeasurement.BeginPhase(measurement, "meta_extract");
                procAltHeader(metaHeader);
                ModelSidecarCostMeasurement.EndPhase(measurement, phase, "meta_extract");
            }
            if (measurement is null)
            {
                foreach (string altSuffix in AltModelMetadataJsonFileSuffixes)
                {
                    if (File.Exists(altModelPrefix + altSuffix))
                    {
                        JObject altMetadata = File.ReadAllText(altModelPrefix + altSuffix).ParseToJson();
                        procAltHeader(altMetadata);
                    }
                }
            }
            else
            {
                foreach (string altSuffix in AltModelMetadataJsonFileSuffixes)
                {
                    string altPath = altModelPrefix + altSuffix;
                    phase = ModelSidecarCostMeasurement.BeginPhase(measurement, "second_exists");
                    bool exists = File.Exists(altPath);
                    ModelSidecarCostMeasurement.EndPhase(measurement, phase, "second_exists");
                    ModelSidecarCostMeasurement.NoteExists(measurement, false, exists);
                    if (exists)
                    {
                        phase = ModelSidecarCostMeasurement.BeginPhase(measurement, "second_read");
                        string rawMetadata = File.ReadAllText(altPath);
                        ModelSidecarCostMeasurement.EndPhase(measurement, phase, "second_read");
                        ModelSidecarCostMeasurement.NoteRead(measurement, false, rawMetadata.Length);
                        phase = ModelSidecarCostMeasurement.BeginPhase(measurement, "second_parse");
                        JObject altMetadata = rawMetadata.ParseToJson();
                        ModelSidecarCostMeasurement.EndPhase(measurement, phase, "second_parse");
                        ModelSidecarCostMeasurement.NoteParse(measurement, false, altMetadata.Count);
                        phase = ModelSidecarCostMeasurement.BeginPhase(measurement, "second_proc");
                        ModelSidecarCostMeasurement.NoteProcessing(measurement, false);
                        procAltHeader(altMetadata);
                        ModelSidecarCostMeasurement.EndPhase(measurement, phase, "second_proc");
                    }
                }
            }
            if (measurement is not null)
            {
                ModelSidecarCostMeasurement.SetStage(measurement, "record_construction");
            }
            string altTriggerPhrase = triggerPhrases.JoinString(", ");
            T2IModelClass clazz = T2IModelClassSorter.IdentifyClassFor(model, headerData, ModelType);
            string specialFormat = null;
            foreach (string key in headerData.Properties().Select(p => p.Name))
            {
                if (key.Contains("bitsandbytes__nf4"))
                {
                    specialFormat = "bnb_nf4";
                    break;
                }
                if (key.Contains("bitsandbytes__fp4"))
                {
                    specialFormat = "bnb_fp4";
                    break;
                }
                if (key.EndsWith(".scale_weight") || key.EndsWith(".weight_scale"))
                {
                    specialFormat = "fp8_scaled";
                    break;
                }
                if (key.EndsWith(".wscales"))
                {
                    specialFormat = "nunchaku";
                    break;
                }
                if (key.EndsWith(".wtscale"))
                {
                    specialFormat = "nunchaku-fp4";
                    break;
                }
            }
            if (model.Name.EndsWith(".gguf"))
            {
                specialFormat = "gguf";
            }
            if (model.Name.EndsWith("/transformer_blocks.safetensors") && File.Exists(model.RawFilePath.Replace('\\', '/').BeforeLast('/') + "/comfy_config.json"))
            {
                specialFormat = "nunchaku";
                if (headerData.ContainsKey("single_transformer_blocks.0.mlp_fc1.wtscale"))
                {
                    specialFormat = "nunchaku-fp4";
                }
                altName ??= model.Name.BeforeLast('/').AfterLast('/');
            }
            if (specialFormat is not null)
            {
                Logs.Debug($"Model {model.Name} has special format '{specialFormat}'");
            }
            string img = metaHeader?.Value<string>("modelspec.preview_image") ?? metaHeader?.Value<string>("modelspec.thumbnail") ?? metaHeader?.Value<string>("thumbnail") ?? metaHeader?.Value<string>("preview_image");
            if (img is not null && !img.StartsWith("data:image/") && img != "imgs/model_placeholder.jpg")
            {
                Logs.Warning($"Ignoring image in metadata of {model.Name} '{img}'");
                img = null;
            }
            int width, height;
            string res = metaHeader?.Value<string>("modelspec.resolution") ?? metaHeader?.Value<string>("resolution");
            if (res is not null)
            {
                width = int.Parse(res.BeforeAndAfter('x', out string h));
                height = int.Parse(h);
            }
            else
            {
                width = (metaHeader?.ContainsKey("standard_width") ?? false) ? metaHeader.Value<int>("standard_width") : (clazz?.StandardWidth ?? 0);
                height = (metaHeader?.ContainsKey("standard_height") ?? false) ? metaHeader.Value<int>("standard_height") : (clazz?.StandardHeight ?? 0);
            }
            img ??= autoImg;
            if (img is not null && img.Length > 1024 * 1024 * 8)
            {
                Logs.Warning($"Ignoring image in metadata of {model.Name} as it is too large (over {img.Length / 1024 / 1024} megabytes)!");
                img = null;
            }
            string[] tags = null;
            JToken tagsTok = metaHeader.Property("modelspec.tags")?.Value;
            if (tagsTok is not null && tagsTok.Type != JTokenType.Null)
            {
                if (tagsTok.Type == JTokenType.Array)
                {
                    tags = tagsTok.ToObject<string[]>();
                }
                else
                {
                    tags = tagsTok.Value<string>().Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
                }
            }
            static string[] limitSize(string[] arr, int max)
            {
                if (arr is null || arr.Length <= max)
                {
                    return arr;
                }
                return arr[0..max];
            }
            static string limitLength(string str, int max)
            {
                if (str is null || str.Length <= max)
                {
                    return str;
                }
                return str[..(max - 3)] + "...";
            }
            static string pickBest(params string[] options)
            {
                string nonNull = null;
                foreach (string opt in options)
                {
                    if (opt is not null)
                    {
                        nonNull = opt;
                    }
                    if (!string.IsNullOrWhiteSpace(opt))
                    {
                        return opt;
                    }
                }
                if (options.Length > 0)
                {
                    return options[0];
                }
                return nonNull;
            }
            const int basicLimit = 4096;
            metadata = new()
            {
                ModelFileVersion = modified,
                ModelSidecarFingerprint = sidecarFingerprint,
                TimeModified = modified,
                TimeCreated = new DateTimeOffset(File.GetCreationTimeUtc(model.RawFilePath)).ToUnixTimeMilliseconds(),
                ModelName = modelCacheId,
                ModelClassType = clazz?.ID,
                Title = limitLength(pickBest(metaHeader?.Value<string>("modelspec.title"), metaHeader?.Value<string>("title"), altName, fileName.BeforeLast('.')), basicLimit),
                Author = limitLength(pickBest(metaHeader?.Value<string>("modelspec.author"), metaHeader?.Value<string>("author")), basicLimit),
                Description = limitLength(pickBest(metaHeader?.Value<string>("modelspec.description"), metaHeader?.Value<string>("description"), altDescription), 1024 * 1024 * 4),
                PreviewImage = img,
                StandardWidth = width,
                StandardHeight = height,
                UsageHint = limitLength(pickBest(metaHeader?.Value<string>("modelspec.usage_hint"), metaHeader?.Value<string>("usage_hint")), 8192 * 5),
                MergedFrom = limitLength(pickBest(metaHeader?.Value<string>("modelspec.merged_from"), metaHeader?.Value<string>("merged_from")), 8192 * 10),
                TriggerPhrase = limitLength(pickBest(metaHeader?.Value<string>("modelspec.trigger_phrase"), metaHeader?.Value<string>("trigger_phrase")) ?? altTriggerPhrase, 8192 * 5),
                License = limitLength(pickBest(metaHeader?.Value<string>("modelspec.license"), metaHeader?.Value<string>("license")), basicLimit),
                Date = limitLength(pickBest(metaHeader?.Value<string>("modelspec.date"), metaHeader?.Value<string>("date")), basicLimit),
                Preprocessor = limitLength(pickBest(metaHeader?.Value<string>("modelspec.preprocessor"), metaHeader?.Value<string>("preprocessor")), basicLimit),
                Tags = limitSize(tags, 128),
                IsNegativeEmbedding = (pickBest(metaHeader?.Value<string>("modelspec.is_negative_embedding"), metaHeader?.Value<string>("is_negative_embedding")) ?? "false").ToLowerFast() == "true",
                LoraDefaultWeight = limitLength(pickBest(metaHeader?.Value<string>("modelspec.lora_default_weight"), metaHeader?.Value<string>("lora_default_weight")), basicLimit),
                LoraDefaultConfinement = limitLength(pickBest(metaHeader?.Value<string>("modelspec.lora_default_confinement"), metaHeader?.Value<string>("lora_default_confinement")), basicLimit),
                PredictionType = limitLength(pickBest(metaHeader?.Value<string>("modelspec.prediction_type"), metaHeader?.Value<string>("prediction_type")), basicLimit),
                Hash = limitLength(pickBest(metaHeader?.Value<string>("modelspec.hash_sha256"), metaHeader?.Value<string>("hash_sha256")), basicLimit),
                TextEncoders = textEncs,
                SpecialFormat = limitLength(pickBest(metaHeader?.Value<string>("modelspec.special_format"), metaHeader?.Value<string>("special_format"), specialFormat), basicLimit)
            };
            if (measurement is not null)
            {
                measurement.RecordConstructed = true;
            }
            if (measurement is not null)
            {
                ModelSidecarCostMeasurement.SetStage(measurement, "none");
            }
            lock (MetadataLock)
            {
                try
                {
                    if (measurement is not null)
                    {
                        measurement.UpsertAttempted = true;
                    }
                    if (measurement is not null)
                    {
                        ModelSidecarCostMeasurement.SetStage(measurement, "upsert_caught");
                    }
                    cache.Metadata.Upsert(metadata);
                    if (measurement is not null)
                    {
                        ModelSidecarCostMeasurement.SetStage(measurement, "none");
                    }
                }
                catch (Exception ex)
                {
                    if (measurement is not null)
                    {
                        ModelSidecarCostMeasurement.NoteCaught(measurement, "upsert_caught");
                    }
                    Logs.Warning($"Error handling metadata database for model {model.RawFilePath}: {ex.ReadableString()}");
                    cache.HadNewError();
                }
            }
            if (measurement is not null)
            {
                ModelSidecarCostMeasurement.EndRecompute(measurement);
            }
        }
        if (!string.IsNullOrWhiteSpace(metadata.ModelClassType))
        {
            metadata.ModelClassType = T2IModelClassSorter.Remaps.GetValueOrDefault(metadata.ModelClassType, metadata.ModelClassType);
        }
        if (metadata.TimeModified == 0)
        {
            metadata.TimeModified = modified;
            metadata.TimeCreated = modified;
        }
        if (measurement is not null)
        {
            ModelSidecarCostMeasurement.SetStage(measurement, "publication");
        }
        lock (ModificationLock)
        {
            model.Title = metadata.Title;
            model.Description = metadata.Description;
            model.ModelClass = T2IModelClassSorter.ModelClasses.GetValueOrDefault((metadata.ModelClassType ?? "").ToLowerFast());
            model.PreviewImage = string.IsNullOrWhiteSpace(metadata.PreviewImage) ? "imgs/model_placeholder.jpg" : metadata.PreviewImage;
            model.StandardWidth = metadata.StandardWidth;
            model.StandardHeight = metadata.StandardHeight;
            model.Metadata = metadata;
        }
        if (measurement is not null)
        {
            measurement.Published = true;
        }
        if (measurement is not null)
        {
            ModelSidecarCostMeasurement.MarkCompleted(measurement, recomputed);
        }
    }

    /// <summary>Internal model adder route. Do not call.</summary>
    public void AddAllFromFolder(string pathBase, string folder, ConcurrentDictionary<string, T2IModel> dict)
    {
        if (IsShutdown)
        {
            return;
        }
        Logs.Verbose($"[Model Scan] Add all {ModelType} from folder {folder}");
        string prefix = folder == "" ? "" : $"{folder}/";
        string actualFolder = $"{pathBase}/{folder}";
        if (!Directory.Exists(actualFolder))
        {
            Logs.Verbose($"[Model Scan] Skipping folder {actualFolder}");
            return;
        }
        Parallel.ForEach(Directory.EnumerateDirectories(actualFolder), subfolder =>
        {
            string simpleName = subfolder.Replace('\\', '/').AfterLast('/');
            string path = $"{prefix}{simpleName}";
            if (simpleName == ".git")
            {
                Logs.Warning($"You have a .git folder in your {ModelType} model folder '{pathBase}/{path}'! That's not supposed to be there.");
                return;
            }
            if (simpleName.StartsWithFast('.'))
            {
                Logs.Verbose($"[Model Scan] Skipping hidden folder {subfolder}");
                return;
            }
            try
            {
                AddAllFromFolder(pathBase, path, dict);
            }
            catch (UnauthorizedAccessException)
            {
                UnathorizedAccessSet.Enqueue(path);
            }
            catch (Exception ex)
            {
                Logs.Warning($"Error while scanning model {ModelType} subfolder '{path}': {ex.ReadableString()}");
            }
        });
        Parallel.ForEach(Directory.EnumerateFiles(actualFolder), file =>
        {
            if (Program.GlobalProgramCancel.IsCancellationRequested)
            {
                return;
            }
            string fixedFileName = file.Replace('\\', '/');
            string fn = fixedFileName.AfterLast('/');
            if (fn.StartsWithFast('.'))
            {
                Logs.Verbose($"[Model Scan] Skipping hidden file {fixedFileName}");
                return;
            }
            string fullFilename = $"{prefix}{fn}";
            if (dict.TryGetValue(fullFilename, out T2IModel existingModel))
            {
                lock (existingModel.OtherPaths)
                {
                    existingModel.OtherPaths.Add(fixedFileName);
                }
            }
            else if (T2IModel.NativelySupportedModelExtensions.Contains(fn.AfterLast('.')))
            {
                if (fixedFileName.EndsWith("/unquantized_layers.safetensors") && File.Exists(fixedFileName.BeforeLast('/') + "/comfy_config.json"))
                {
                    return; // Nunchaku secondary file
                }
                T2IModel model = new(this, pathBase, fixedFileName, fullFilename)
                {
                    Title = fullFilename.AfterLast('/'),
                    Description = "(Metadata not yet loaded.)",
                    PreviewImage = "imgs/model_placeholder.jpg",
                };
                dict[fullFilename] = model;
                try
                {
                    LoadMetadata(model);
                }
                catch (UnauthorizedAccessException)
                {
                    UnathorizedAccessSet.Enqueue(fullFilename);
                }
                catch (Exception ex)
                {
                    if (Program.GlobalProgramCancel.IsCancellationRequested)
                    {
                        throw;
                    }
                    Logs.Warning($"Failed to load metadata for {fullFilename}:\n{ex.ReadableString()}");
                }
                model.AutoWarn();
            }
            else if (T2IModel.LegacyModelExtensions.Contains(fn.AfterLast('.')))
            {
                T2IModel model = new(this, pathBase, fixedFileName, fullFilename)
                {
                    Description = "(None, use '.safetensors' to enable metadata descriptions)",
                    PreviewImage = "imgs/legacy_ckpt.jpg",
                };
                model.PreviewImage = GetAutoFormatImage(model) ?? model.PreviewImage;
                dict[fullFilename] = model;
                model.AutoWarn();
            }
        });
    }
\n+    /// <summary>Temporary, opt-in Rank 32 duplicate model-sidecar parsing cost recorder.</summary>
    private static class ModelSidecarCostMeasurement
    {
        /// <summary>Prefix used for every Rank 32 measurement record.</summary>
        private const string Prefix = "[Rank32ModelSidecar]";

        /// <summary>Schema version for Rank 32 records.</summary>
        private const int Schema = 1;

        /// <summary>Exact privacy-safe scenario bases accepted by the recorder.</summary>
        private static readonly HashSet<string> AllowedScenarioBases =
        [
            "contract", "cache_central", "cache_per_folder", "suffix_single", "suffix_all", "invalid_json",
            "concurrent_change", "cache_unavailable", "cache_fault", "header_fault", "single_1k_1", "single_1k_4",
            "single_64k_1", "single_64k_4", "stress_single_1m_1", "stress_single_1m_4", "batch16_4k_4",
            "batch16_64k_4", "batch128_4k_4", "batch128_64k_1", "control_enabled", "control_disabled"
        ];

        /// <summary>Process-local source for record identifiers.</summary>
        private static long NextRecordId;

        /// <summary>One phase start snapshot.</summary>
        internal readonly record struct PhaseToken(long Timestamp, long Allocation);

        /// <summary>One measured LoadMetadata call.</summary>
        internal sealed class Operation : IDisposable
        {
            /// <summary>Process-local record identifier.</summary>
            internal long Id;

            /// <summary>Validated scenario label.</summary>
            internal string Scenario;

            /// <summary>Validated scenario base.</summary>
            internal string ScenarioBase;

            /// <summary>Bounded central/per-folder cache category.</summary>
            internal string CacheMode;

            /// <summary>Bounded model count implied by the fixed scenario.</summary>
            internal int ModelCount;

            /// <summary>Whole-call start timestamp.</summary>
            internal long StartTimestamp;

            /// <summary>Whole-call start current-thread allocation.</summary>
            internal long StartAllocation;

            /// <summary>Recomputation start timestamp.</summary>
            internal long RecomputeTimestamp;

            /// <summary>Recomputation start current-thread allocation.</summary>
            internal long RecomputeAllocation;

            /// <summary>Current bounded failure stage.</summary>
            internal string Stage = "none";

            /// <summary>Active phase start timestamp, or zero when no phase is active.</summary>
            internal long ActivePhaseTimestamp;

            /// <summary>Active phase start current-thread allocation.</summary>
            internal long ActivePhaseAllocation;

            /// <summary>Final bounded result.</summary>
            internal string Result;

            /// <summary>Bounded invalidation category.</summary>
            internal string Invalidation = "unknown";

            /// <summary>Whether a caught cache lookup failure occurred.</summary>
            internal bool CacheLookupCaught;

            /// <summary>Whether a caught embedded-header failure occurred.</summary>
            internal bool EmbeddedHeaderCaught;

            /// <summary>Whether a caught cache upsert failure occurred.</summary>
            internal bool UpsertCaught;

            /// <summary>Whether metadata record construction was reached.</summary>
            internal bool RecordConstructed;

            /// <summary>Whether persistent upsert was attempted.</summary>
            internal bool UpsertAttempted;

            /// <summary>Whether final model publication completed.</summary>
            internal bool Published;

            /// <summary>Number of fingerprint suffix inspections.</summary>
            internal long FingerprintInspections;

            /// <summary>First-pass existence checks.</summary>
            internal long FirstExists;

            /// <summary>First-pass existing suffixes.</summary>
            internal long FirstPresent;

            /// <summary>First-pass supported-suffix presence bitmask in established order.</summary>
            internal long FirstPresentMask;

            /// <summary>First-pass reads.</summary>
            internal long FirstReads;

            /// <summary>First-pass top-level parses.</summary>
            internal long FirstParses;

            /// <summary>First-pass merged property count.</summary>
            internal long FirstProperties;

            /// <summary>First-pass property-copy invocations begun.</summary>
            internal long FirstProcessingCalls;

            /// <summary>First-pass source character count.</summary>
            internal long FirstCharacters;

            /// <summary>Second-pass existence checks.</summary>
            internal long SecondExists;

            /// <summary>Second-pass existing suffixes.</summary>
            internal long SecondPresent;

            /// <summary>Second-pass supported-suffix presence bitmask in established order.</summary>
            internal long SecondPresentMask;

            /// <summary>Second-pass reads.</summary>
            internal long SecondReads;

            /// <summary>Second-pass top-level parses.</summary>
            internal long SecondParses;

            /// <summary>Second-pass processed property count.</summary>
            internal long SecondProperties;

            /// <summary>Second-pass procAltHeader invocations begun.</summary>
            internal long SecondProcessingCalls;

            /// <summary>Second-pass source character count.</summary>
            internal long SecondCharacters;

            /// <summary>Fingerprint elapsed microseconds and allocation.</summary>
            internal long FingerprintUs, FingerprintBytes;

            /// <summary>Cache lookup elapsed microseconds and allocation.</summary>
            internal long CacheUs, CacheBytes;

            /// <summary>Embedded-header elapsed microseconds and allocation.</summary>
            internal long HeaderUs, HeaderBytes;

            /// <summary>First-pass existence elapsed microseconds and allocation.</summary>
            internal long FirstExistsUs, FirstExistsBytes;

            /// <summary>First-pass read elapsed microseconds and allocation.</summary>
            internal long FirstReadUs, FirstReadBytes;

            /// <summary>First-pass parse elapsed microseconds and allocation.</summary>
            internal long FirstParseUs, FirstParseBytes;

            /// <summary>First-pass merge elapsed microseconds and allocation.</summary>
            internal long FirstMergeUs, FirstMergeBytes;

            /// <summary>Merged-header extraction elapsed microseconds and allocation.</summary>
            internal long MetaExtractUs, MetaExtractBytes;

            /// <summary>Second-pass existence elapsed microseconds and allocation.</summary>
            internal long SecondExistsUs, SecondExistsBytes;

            /// <summary>Second-pass read elapsed microseconds and allocation.</summary>
            internal long SecondReadUs, SecondReadBytes;

            /// <summary>Second-pass parse elapsed microseconds and allocation.</summary>
            internal long SecondParseUs, SecondParseBytes;

            /// <summary>Second-pass procAltHeader elapsed microseconds and allocation.</summary>
            internal long SecondProcUs, SecondProcBytes;

            /// <summary>Whole recomputation elapsed microseconds and allocation.</summary>
            internal long RecomputeUs, RecomputeBytes;

            /// <summary>Marks the operation complete and emits it without allowing failures to escape.</summary>
            public void Dispose()
            {
                Complete(this);
            }
        }

        /// <summary>Begins a measurement only for a valid enabled scenario.</summary>
        internal static Operation Begin()
        {
            try
            {
                if (!(Program.ServerSettings?.Performance?.ModelSidecarMeasurementEnabled ?? false))
                {
                    return null;
                }
                string scenario = Program.ServerSettings.Performance.ModelSidecarMeasurementScenario ?? "";
                if (!TryValidateScenario(scenario, out string scenarioBase))
                {
                    return null;
                }
                return new()
                {
                    Id = Interlocked.Increment(ref NextRecordId),
                    Scenario = scenario,
                    ScenarioBase = scenarioBase,
                    CacheMode = Program.ServerSettings.Metadata.ModelMetadataPerFolder ? "per_folder" : "central",
                    ModelCount = ScenarioModelCount(scenarioBase),
                    StartTimestamp = Stopwatch.GetTimestamp(),
                    StartAllocation = GC.GetAllocatedBytesForCurrentThread()
                };
            }
            catch
            {
                return null;
            }
        }

        /// <summary>Begins one named phase.</summary>
        internal static PhaseToken BeginPhase(Operation operation, string stage)
        {
            if (operation is null)
            {
                return default;
            }
            operation.Stage = stage;
            PhaseToken token = new(Stopwatch.GetTimestamp(), GC.GetAllocatedBytesForCurrentThread());
            operation.ActivePhaseTimestamp = token.Timestamp;
            operation.ActivePhaseAllocation = token.Allocation;
            return token;
        }

        /// <summary>Ends and accumulates one named phase.</summary>
        internal static void EndPhase(Operation operation, PhaseToken token, string stage)
        {
            if (operation is null)
            {
                return;
            }
            long endTimestamp = Stopwatch.GetTimestamp();
            long endAllocation = GC.GetAllocatedBytesForCurrentThread();
            AccumulatePhase(operation, stage, token.Timestamp, token.Allocation, endTimestamp, endAllocation);
            operation.ActivePhaseTimestamp = 0;
            operation.ActivePhaseAllocation = 0;
            operation.Stage = "none";
        }

        /// <summary>Accumulates one completed or failure-finalized phase.</summary>
        private static void AccumulatePhase(Operation operation, string stage, long startTimestamp, long startAllocation, long endTimestamp, long endAllocation)
        {
            long microseconds = ToMicroseconds(startTimestamp, endTimestamp);
            long allocation = Math.Max(0, endAllocation - startAllocation);
            switch (stage)
            {
                case "fingerprint": operation.FingerprintUs += microseconds; operation.FingerprintBytes += allocation; break;
                case "cache_lookup": operation.CacheUs += microseconds; operation.CacheBytes += allocation; break;
                case "embedded_header": operation.HeaderUs += microseconds; operation.HeaderBytes += allocation; break;
                case "first_exists": operation.FirstExistsUs += microseconds; operation.FirstExistsBytes += allocation; break;
                case "first_read": operation.FirstReadUs += microseconds; operation.FirstReadBytes += allocation; break;
                case "first_parse": operation.FirstParseUs += microseconds; operation.FirstParseBytes += allocation; break;
                case "first_merge": operation.FirstMergeUs += microseconds; operation.FirstMergeBytes += allocation; break;
                case "meta_extract": operation.MetaExtractUs += microseconds; operation.MetaExtractBytes += allocation; break;
                case "second_exists": operation.SecondExistsUs += microseconds; operation.SecondExistsBytes += allocation; break;
                case "second_read": operation.SecondReadUs += microseconds; operation.SecondReadBytes += allocation; break;
                case "second_parse": operation.SecondParseUs += microseconds; operation.SecondParseBytes += allocation; break;
                case "second_proc": operation.SecondProcUs += microseconds; operation.SecondProcBytes += allocation; break;
            }
        }

        /// <summary>Notes the fixed fingerprint inspection count.</summary>
        internal static void NoteFingerprint(Operation operation, int suffixes)
        {
            if (operation is not null)
            {
                operation.FingerprintInspections = Math.Max(0, suffixes);
            }
        }

        /// <summary>Notes one sidecar existence result.</summary>
        internal static void NoteExists(Operation operation, bool firstPass, bool exists)
        {
            if (operation is null)
            {
                return;
            }
            if (firstPass)
            {
                long slot = operation.FirstExists;
                operation.FirstExists++;
                operation.FirstPresent += exists ? 1 : 0;
                if (exists && slot is >= 0 and < 4)
                {
                    operation.FirstPresentMask |= 1L << (int)slot;
                }
            }
            else
            {
                long slot = operation.SecondExists;
                operation.SecondExists++;
                operation.SecondPresent += exists ? 1 : 0;
                if (exists && slot is >= 0 and < 4)
                {
                    operation.SecondPresentMask |= 1L << (int)slot;
                }
            }
        }

        /// <summary>Notes that one established per-sidecar processing call has begun.</summary>
        internal static void NoteProcessing(Operation operation, bool firstPass)
        {
            if (operation is null)
            {
                return;
            }
            if (firstPass)
            {
                operation.FirstProcessingCalls++;
            }
            else
            {
                operation.SecondProcessingCalls++;
            }
        }

        /// <summary>Notes one read without retaining its content.</summary>
        internal static void NoteRead(Operation operation, bool firstPass, int characters)
        {
            if (operation is null)
            {
                return;
            }
            if (firstPass)
            {
                operation.FirstReads++;
                operation.FirstCharacters += Math.Max(0, characters);
            }
            else
            {
                operation.SecondReads++;
                operation.SecondCharacters += Math.Max(0, characters);
            }
        }

        /// <summary>Notes one top-level parse and its already available property count.</summary>
        internal static void NoteParse(Operation operation, bool firstPass, int properties)
        {
            if (operation is null)
            {
                return;
            }
            if (firstPass)
            {
                operation.FirstParses++;
                operation.FirstProperties += Math.Max(0, properties);
            }
            else
            {
                operation.SecondParses++;
                operation.SecondProperties += Math.Max(0, properties);
            }
        }

        /// <summary>Starts recomputation and records its bounded invalidation cause.</summary>
        internal static void BeginRecompute(Operation operation, string invalidation)
        {
            if (operation is null)
            {
                return;
            }
            operation.Invalidation = invalidation is "cache_missing" or "cache_lookup_fault" or "legacy_text_encoders" or "model_mtime"
                or "legacy_null_fingerprint" or "sidecar_fingerprint"
                ? invalidation : "unknown";
            operation.RecomputeTimestamp = Stopwatch.GetTimestamp();
            operation.RecomputeAllocation = GC.GetAllocatedBytesForCurrentThread();
        }

        /// <summary>Ends the recomputation interval.</summary>
        internal static void EndRecompute(Operation operation)
        {
            if (operation is null || operation.RecomputeTimestamp == 0)
            {
                return;
            }
            operation.RecomputeUs = ToMicroseconds(operation.RecomputeTimestamp, Stopwatch.GetTimestamp());
            operation.RecomputeBytes = Math.Max(0, GC.GetAllocatedBytesForCurrentThread() - operation.RecomputeAllocation);
        }

        /// <summary>Marks a bounded caught failure while preserving continued production flow.</summary>
        internal static void NoteCaught(Operation operation, string stage)
        {
            if (operation is null)
            {
                return;
            }
            if (stage == "cache_lookup_caught")
            {
                operation.CacheLookupCaught = true;
            }
            else if (stage == "embedded_header_caught")
            {
                operation.EmbeddedHeaderCaught = true;
            }
            else if (stage == "upsert_caught")
            {
                operation.UpsertCaught = true;
            }
            operation.Stage = "none";
        }

        /// <summary>Marks cache unavailability before the existing return.</summary>
        internal static void MarkCacheUnavailable(Operation operation)
        {
            if (operation is not null)
            {
                operation.Result = "cache_unavailable";
                operation.Invalidation = "cache_unavailable";
                operation.Stage = "none";
            }
        }

        /// <summary>Marks a successful cache hit or recomputation.</summary>
        internal static void MarkCompleted(Operation operation, bool recomputed)
        {
            if (operation is not null)
            {
                operation.Result = recomputed ? "recomputed" : "cache_hit";
                operation.Invalidation = recomputed ? operation.Invalidation : "cache_hit";
                operation.Stage = "none";
            }
        }

        /// <summary>Sets a bounded current stage for untimed construction/publication work.</summary>
        internal static void SetStage(Operation operation, string stage)
        {
            if (operation is not null)
            {
                operation.Stage = stage;
            }
        }

        /// <summary>Runs the private deterministic post-fingerprint contract hook.</summary>
        internal static void RunAfterFingerprintHook(Operation operation, Action hook)
        {
            if (operation is not null && operation.ScenarioBase == "concurrent_change")
            {
                hook?.Invoke();
            }
        }

        /// <summary>Validates the exact scenario allowlist and bounded iteration suffix.</summary>
        private static bool TryValidateScenario(string scenario, out string scenarioBase)
        {
            scenarioBase = scenario.Before('/');
            if (!AllowedScenarioBases.Contains(scenarioBase))
            {
                return false;
            }
            if (scenario == scenarioBase)
            {
                return true;
            }
            if (!scenario.StartsWith($"{scenarioBase}/"))
            {
                return false;
            }
            string suffix = scenario[(scenarioBase.Length + 1)..];
            if (suffix.StartsWith("warmup_") && suffix.Length == 8 && suffix[7] is >= '0' and <= '4')
            {
                return true;
            }
            if (!suffix.StartsWith("measured_") || !int.TryParse(suffix[9..], out int iteration))
            {
                return false;
            }
            return iteration is >= 0 and <= 29 && suffix == $"measured_{iteration}";
        }

        /// <summary>Returns the fixed model count implied by an allowlisted scenario.</summary>
        private static int ScenarioModelCount(string scenarioBase)
        {
            if (scenarioBase.StartsWith("batch128_"))
            {
                return 128;
            }
            if (scenarioBase.StartsWith("batch16_"))
            {
                return 16;
            }
            return 1;
        }

        /// <summary>Converts stopwatch ticks to nonnegative whole microseconds.</summary>
        private static long ToMicroseconds(long start, long end)
        {
            if (end <= start)
            {
                return 0;
            }
            return (long)((end - start) * 1_000_000d / Stopwatch.Frequency);
        }

        /// <summary>Finalizes and queues one privacy-safe immutable record.</summary>
        private static void Complete(Operation operation)
        {
            try
            {
                long endTimestamp = Stopwatch.GetTimestamp();
                long endAllocation = GC.GetAllocatedBytesForCurrentThread();
                string result = operation.Result ?? "failed";
                string failureStage = result == "failed" ? BoundFailureStage(operation.Stage) : "none";
                if (result == "failed" && operation.ActivePhaseTimestamp > 0)
                {
                    AccumulatePhase(operation, failureStage, operation.ActivePhaseTimestamp, operation.ActivePhaseAllocation, endTimestamp, endAllocation);
                }
                if (result == "failed" && operation.RecomputeTimestamp > 0 && operation.RecomputeUs == 0)
                {
                    operation.RecomputeUs = ToMicroseconds(operation.RecomputeTimestamp, endTimestamp);
                    operation.RecomputeBytes = Math.Max(0, endAllocation - operation.RecomputeAllocation);
                }
                object record = new
                {
                    schema = Schema,
                    record = "load_metadata",
                    id = operation.Id,
                    scenario = operation.Scenario,
                    cache_mode = operation.CacheMode,
                    model_count = operation.ModelCount,
                    result,
                    cache_hit = result == "cache_hit",
                    recomputed = result == "recomputed",
                    invalidation = operation.Invalidation,
                    failure_stage = failureStage,
                    cache_lookup_caught = operation.CacheLookupCaught,
                    embedded_header_caught = operation.EmbeddedHeaderCaught,
                    upsert_caught = operation.UpsertCaught,
                    record_constructed = operation.RecordConstructed,
                    upsert_attempted = operation.UpsertAttempted,
                    published = operation.Published,
                    fingerprint_inspections = operation.FingerprintInspections,
                    first_exists = operation.FirstExists,
                    first_present = operation.FirstPresent,
                    first_present_mask = operation.FirstPresentMask,
                    first_reads = operation.FirstReads,
                    first_parses = operation.FirstParses,
                    first_properties = operation.FirstProperties,
                    first_processing_calls = operation.FirstProcessingCalls,
                    first_characters = operation.FirstCharacters,
                    second_exists = operation.SecondExists,
                    second_present = operation.SecondPresent,
                    second_present_mask = operation.SecondPresentMask,
                    second_reads = operation.SecondReads,
                    second_parses = operation.SecondParses,
                    second_properties = operation.SecondProperties,
                    second_processing_calls = operation.SecondProcessingCalls,
                    second_characters = operation.SecondCharacters,
                    fingerprint_us = operation.FingerprintUs,
                    fingerprint_bytes = operation.FingerprintBytes,
                    cache_us = operation.CacheUs,
                    cache_bytes = operation.CacheBytes,
                    header_us = operation.HeaderUs,
                    header_bytes = operation.HeaderBytes,
                    first_exists_us = operation.FirstExistsUs,
                    first_exists_bytes = operation.FirstExistsBytes,
                    first_read_us = operation.FirstReadUs,
                    first_read_bytes = operation.FirstReadBytes,
                    first_parse_us = operation.FirstParseUs,
                    first_parse_bytes = operation.FirstParseBytes,
                    first_merge_us = operation.FirstMergeUs,
                    first_merge_bytes = operation.FirstMergeBytes,
                    first_total_us = operation.FirstExistsUs + operation.FirstReadUs + operation.FirstParseUs + operation.FirstMergeUs,
                    first_total_bytes = operation.FirstExistsBytes + operation.FirstReadBytes + operation.FirstParseBytes + operation.FirstMergeBytes,
                    meta_extract_us = operation.MetaExtractUs,
                    meta_extract_bytes = operation.MetaExtractBytes,
                    second_exists_us = operation.SecondExistsUs,
                    second_exists_bytes = operation.SecondExistsBytes,
                    second_read_us = operation.SecondReadUs,
                    second_read_bytes = operation.SecondReadBytes,
                    second_parse_us = operation.SecondParseUs,
                    second_parse_bytes = operation.SecondParseBytes,
                    second_proc_us = operation.SecondProcUs,
                    second_proc_bytes = operation.SecondProcBytes,
                    second_total_us = operation.SecondExistsUs + operation.SecondReadUs + operation.SecondParseUs + operation.SecondProcUs,
                    second_total_bytes = operation.SecondExistsBytes + operation.SecondReadBytes + operation.SecondParseBytes + operation.SecondProcBytes,
                    sidecar_total_us = operation.FirstExistsUs + operation.FirstReadUs + operation.FirstParseUs + operation.FirstMergeUs
                        + operation.SecondExistsUs + operation.SecondReadUs + operation.SecondParseUs + operation.SecondProcUs,
                    sidecar_total_bytes = operation.FirstExistsBytes + operation.FirstReadBytes + operation.FirstParseBytes + operation.FirstMergeBytes
                        + operation.SecondExistsBytes + operation.SecondReadBytes + operation.SecondParseBytes + operation.SecondProcBytes,
                    recompute_us = operation.RecomputeUs,
                    recompute_bytes = operation.RecomputeBytes,
                    total_us = ToMicroseconds(operation.StartTimestamp, endTimestamp),
                    allocation_bytes = Math.Max(0, endAllocation - operation.StartAllocation)
                };
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

        /// <summary>Bounds any propagating failure stage to the approved schema.</summary>
        private static string BoundFailureStage(string stage)
        {
            return stage is "fingerprint" or "first_exists" or "first_read" or "first_parse" or "first_merge" or "meta_extract"
                or "second_exists" or "second_read" or "second_parse" or "second_proc" or "record_construction" or "publication"
                ? stage : "none";
        }
    }
}
