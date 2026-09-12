using FreneticUtilities.FreneticExtensions;
using FreneticUtilities.FreneticToolkit;
using Microsoft.AspNetCore.Builder;
using Newtonsoft.Json.Linq;
using SwarmUI.Accounts;
using SwarmUI.Backends;
using SwarmUI.Core;
using SwarmUI.Text2Image;
using SwarmUI.Utils;
using SwarmUI.WebAPI;
using System.Collections.Frozen;
using System.IO;
using System.Net.Http;

namespace SwarmUI.Builtin_ComfyUIBackend;

/// <summary>Main class for the ComfyUI Backend extension.</summary>
public class ComfyUIBackendExtension : Extension
{
    /// <summary>Copy of <see cref="Extension.FilePath"/> for ComfyUI.</summary>
    public static string Folder;

    public static PermInfoGroup ComfyPermGroup = new("ComfyUI", "Permissions related to direct interaction with the ComfyUI backend.");

    public static PermInfo PermDirectCalls = Permissions.Register(new("comfy_direct_calls", "ComfyUI Direct Calls", "Allows the user to make direct calls to the ComfyUI backend. Required for most ComfyUI features.", PermissionDefault.POWERUSERS, ComfyPermGroup));
    public static PermInfo PermBackendGenerate = Permissions.Register(new("comfy_backend_generate", "ComfyUI Backend Generate", "Allows the user to generate directly from the ComfyUI backend.", PermissionDefault.POWERUSERS, ComfyPermGroup));
    public static PermInfo PermDynamicCustomWorkflows = Permissions.Register(new("comfy_dynamic_custom_workflows", "ComfyUI Dynamic Custom Workflows", "Allows the user to use dynamic custom workflows via Generate tab parameters.", PermissionDefault.POWERUSERS, ComfyPermGroup));
    public static PermInfo PermStoredCustomWorkflows = Permissions.Register(new("comfy_stored_custom_workflows", "ComfyUI Stored Custom Workflows", "Allows the user to use stored (already saved by a user with direct access) custom workflows via Generate tab parameters.", PermissionDefault.POWERUSERS, ComfyPermGroup));
    public static PermInfo PermReadWorkflows = Permissions.Register(new("comfy_read_workflows", "ComfyUI Read Workflows", "Allows the user read stored workflow data.", PermissionDefault.POWERUSERS, ComfyPermGroup));
    public static PermInfo PermEditWorkflows = Permissions.Register(new("comfy_edit_workflows", "ComfyUI Edit Workflows", "Allows the save, delete, or edit stored workflows.", PermissionDefault.POWERUSERS, ComfyPermGroup));

    public record class ComfyCustomWorkflow(string Name, string Workflow, string Prompt, string CustomParams, string ParamValues, string Image, string Description, bool EnableInSimple);

    /// <summary>All current custom workflow IDs mapped to their data.</summary>
    public static ConcurrentDictionary<string, ComfyCustomWorkflow> CustomWorkflows = new();

    /// <summary>Set of all feature-ids supported by ComfyUI backends.</summary>
    public static HashSet<string> FeaturesSupported = ["comfyui", "refiners", "controlnet", "endstepsearly", "seamless", "video", "variation_seed", "freeu", "yolov8"];

    /// <summary>Set of feature-ids that were added presumptively during loading and should be removed if the backend turns out to be missing them.</summary>
    public static HashSet<string> FeaturesDiscardIfNotFound = ["variation_seed", "freeu", "yolov8"];

    /// <summary>Extensible map of ComfyUI Node IDs to supported feature IDs.</summary>
    public static Dictionary<string, string> NodeToFeatureMap = ComfyCapabilityCatalog.CreateNodeToFeatureMap();

    /// <summary>Per-linked-backend gates that prevent stale object-info publication.</summary>
    private static readonly ConcurrentDictionary<SwarmSwarmBackend, SemaphoreSlim> RemoteCapabilityGates = new();

    /// <inheritdoc/>
    public override void OnPreInit()
    {
        Folder = FilePath;
        LoadWorkflowFiles();
        Program.ModelRefreshEvent += Refresh;
        Program.ModelPathsChangedEvent += OnModelPathsChanged;
        ScriptFiles.Add("Assets/comfy_workflow_editor_helper.js");
        StyleSheetFiles.Add("Assets/comfy_workflow_editor.css");
        T2IParamTypes.FakeTypeProviders.Add(DynamicParamGenerator);
        // Temporary: remove old pycache files where we used to have python files, to prevent Comfy boot errors
        Utilities.RemoveBadPycacheFrom($"{FilePath}ExtraNodes");
        Utilities.RemoveBadPycacheFrom($"{FilePath}ExtraNodes/SwarmWebHelper");
        T2IAPI.AlwaysTopKeys.Add("comfyworkflowraw");
        T2IAPI.AlwaysTopKeys.Add("comfyworkflowparammetadata");
        if (Directory.Exists($"{FilePath}DLNodes/ComfyUI_IPAdapter_plus"))
        {
            FeaturesSupported.UnionWith(["ipadapter", "cubiqipadapterunified"]);
            FeaturesDiscardIfNotFound.UnionWith(["ipadapter", "cubiqipadapterunified"]);
        }
        if (Directory.Exists($"{FilePath}DLNodes/comfyui_controlnet_aux"))
        {
            FeaturesSupported.UnionWith(["controlnetpreprocessors"]);
            FeaturesDiscardIfNotFound.UnionWith(["controlnetpreprocessors"]);
        }
        if (Directory.Exists($"{FilePath}DLNodes/ComfyUI-Frame-Interpolation"))
        {
            FeaturesSupported.UnionWith(["frameinterps"]);
            FeaturesDiscardIfNotFound.UnionWith(["frameinterps"]);
        }
        if (Directory.Exists($"{FilePath}DLNodes/ComfyUI-GIMM-VFI"))
        {
            FeaturesSupported.UnionWith(["frameinterps_gimmvfi"]);
            FeaturesDiscardIfNotFound.UnionWith(["frameinterps_gimmvfi"]);
        }
        if (Directory.Exists($"{FilePath}DLNodes/ComfyUI-SAM3"))
        {
            FeaturesSupported.UnionWith(["sam3"]);
            FeaturesDiscardIfNotFound.UnionWith(["sam3"]);
        }
        if (Directory.Exists($"{FilePath}DLNodes/ComfyUI_bitsandbytes_NF4"))
        {
            FeaturesSupported.UnionWith(["bnb_nf4"]);
            FeaturesDiscardIfNotFound.UnionWith(["bnb_nf4"]);
        }
        if (Directory.Exists($"{FilePath}DLNodes/ComfyUI-GGUF"))
        {
            FeaturesSupported.UnionWith(["gguf"]);
            FeaturesDiscardIfNotFound.UnionWith(["gguf"]);
        }
        if (Directory.Exists($"{FilePath}DLNodes/ComfyUI-TeaCache"))
        {
            FeaturesSupported.UnionWith(["teacache"]);
            FeaturesDiscardIfNotFound.UnionWith(["teacache"]);
        }
        if (Directory.Exists($"{FilePath}DLNodes/comfyui-inpaint-nodes"))
        {
            FeaturesSupported.UnionWith(["inpaintnodes"]);
            FeaturesDiscardIfNotFound.UnionWith(["inpaintnodes"]);
        }
        ComfyCapabilityRegistry.Initialize();
        T2IParamTypes.ConcatDropdownValsClean(ref UpscalerModels, InternalListModelsFor("upscale_models", true).Select(u => $"model-{u}///Model: {u}"));
        T2IParamTypes.ConcatDropdownValsClean(ref UpscalerModels, InternalListModelsFor("latent_upscale_models", true).Select(u => $"latentmodel-{u}///Latent Model: {u}"));
        T2IParamTypes.ConcatDropdownValsClean(ref YoloModels, InternalListModelsFor("yolov8", false));
        T2IParamTypes.ConcatDropdownValsClean(ref GligenModels, InternalListModelsFor("gligen", false));
        T2IParamTypes.ConcatDropdownValsClean(ref StyleModels, InternalListModelsFor("style_models", true));
        SwarmSwarmBackend.OnSwarmBackendAdded += OnSwarmBackendAdded;
        SwarmSwarmBackend.ReviseRemotesEvent += OnSwarmBackendRevised;
    }

    /// <summary>Helper to quickly read a list of model files in a model subfolder, for prepopulating model lists during startup.</summary>
    public static string[] InternalListModelsFor(string subpath, bool createDir)
    {
        static bool isModelFile(string f) => T2IModel.LegacyModelExtensions.Contains(f.AfterLast('.')) || T2IModel.NativelySupportedModelExtensions.Contains(f.AfterLast('.'));
        List<string> results = [];
        foreach (string root in Program.ServerSettings.Paths.ActualModelRoots)
        {
            string path = Utilities.CombinePathWithAbsolute(root, subpath);
            if (createDir)
            {
                Utilities.EnsureDirectory(path);
            }
            else if (!Directory.Exists(path))
            {
                continue;
            }
            results.AddRange(Directory.EnumerateFiles(path, "*.*", SearchOption.AllDirectories).Where(isModelFile).Select(f => Path.GetRelativePath(path, f)));
            createDir = false; // Only first root autocreates
        }
        return [.. results];
    }

    /// <inheritdoc/>
    public override void OnShutdown()
    {
        Program.Backends.BackendRemovedEvent -= OnBackendRemoved;
        T2IParamTypes.FakeTypeProviders.Remove(DynamicParamGenerator);
        T2IParamInput.UnregisterFinalRequiredFlagsHandler(RecomputeBackendRoutingRequirementsHandler);
    }

    /// <summary>Forces all currently running comfy backends to restart.</summary>
    public static async Task RestartAllComfyBackends()
    {
        List<Task> tasks = [];
        foreach (ComfyUIAPIAbstractBackend backend in RunningComfyBackends)
        {
            tasks.Add(Program.Backends.ReloadBackend(backend.BackendData));
        }
        await Task.WhenAll(tasks);
    }

    public static T2IParamType FakeRawInputType = new("comfyworkflowraw", "", "", Type: T2IParamDataType.TEXT, ID: "comfyworkflowraw", FeatureFlag: "comfyui", HideFromMetadata: true), // TODO: Setting to toggle metadata
        FakeParameterMetadata = new("comfyworkflowparammetadata", "", "", Type: T2IParamDataType.TEXT, ID: "comfyworkflowparammetadata", FeatureFlag: "comfyui", HideFromMetadata: true);

    public static SingleCacheAsync<string, JObject> ParameterMetadataCacheHelper = new(s => ComfySubmittedJson.ParseObject(s));

    public T2IParamType DynamicParamGenerator(string name, T2IParamInput context)
    {
        try
        {
            if (!context.SourceSession?.User?.HasPermission(PermDynamicCustomWorkflows) ?? false)
            {
                return null;
            }
            if (name == "comfyworkflowraw")
            {
                return FakeRawInputType;
            }
            if (name == "comfyworkflowparammetadata")
            {
                return FakeParameterMetadata;
            }
            if (context.TryGetRaw(FakeParameterMetadata, out object paramMetadataObj))
            {
                JObject paramMetadata = ParameterMetadataCacheHelper.GetValue((string)paramMetadataObj);
                if (paramMetadata.TryGetValue(name, out JToken paramTok))
                {
                    T2IParamType type = T2IParamType.FromNet((JObject)paramTok);
                    if (type.Type == T2IParamDataType.INTEGER && type.ViewType == ParamViewType.SEED)
                    {
                        string seedClean(string prior, string newVal)
                        {
                            long parsed = long.Parse(newVal);
                            if (parsed == -1)
                            {
                                int max = (int)type.Max;
                                parsed = Random.Shared.Next(0, max <= 0 ? int.MaxValue : max);
                            }
                            return parsed.ToString();
                        }
                        type = type with { Clean = seedClean };
                    }
                    return type;
                }
            }
            if (name.StartsWith("comfyrawworkflowinput") && (context.InternalSet.ValuesInput.ContainsKey("comfyworkflowraw") || context.InternalSet.ValuesInput.ContainsKey("comfyuicustomworkflow")))
            {
                string nameNoPrefix = name.After("comfyrawworkflowinput");
                T2IParamDataType type = FakeRawInputType.Type;
                ParamViewType numberType = ParamViewType.BIG;
                Func<string, string, string> cleaner = null;
                if (nameNoPrefix.StartsWith("seed"))
                {
                    type = T2IParamDataType.INTEGER;
                    numberType = ParamViewType.SEED;
                    nameNoPrefix = nameNoPrefix.After("seed");
                    string seedClean(string prior, string newVal)
                    {
                        long parsed = long.Parse(newVal);
                        if (parsed == -1)
                        {
                            parsed = Random.Shared.Next(0, int.MaxValue);
                        }
                        return parsed.ToString();
                    }
                    cleaner = seedClean;
                }
                else
                {
                    foreach (T2IParamDataType possible in Enum.GetValues<T2IParamDataType>())
                    {
                        string typeId = possible.ToString().ToLowerFast();
                        if (nameNoPrefix.StartsWith(typeId))
                        {
                            nameNoPrefix = nameNoPrefix.After(typeId);
                            type = possible;
                            break;
                        }
                    }
                }
                T2IParamType resType = FakeRawInputType with { Name = nameNoPrefix, ID = name, HideFromMetadata = false, Type = type, ViewType = numberType, Clean = cleaner };
                if (type == T2IParamDataType.MODEL)
                {
                    static string cleanup(string _, string val)
                    {
                        val = val.Replace('\\', '/');
                        while (val.Contains("//"))
                        {
                            val = val.Replace("//", "/");
                        }
                        val = val.Replace('/', Path.DirectorySeparatorChar);
                        return val;
                    }
                    resType = resType with { Clean = cleanup };
                }
                return resType;
            }
        }
        catch (Exception)
        {
            Logs.Error("Error processing dynamic Comfy parameter metadata (content redacted).");
        }
        return null;
    }

    public static IEnumerable<ComfyUIAPIAbstractBackend> RunningComfyBackends => Program.Backends.RunningBackendsOfType<ComfyUIAPIAbstractBackend>();

    public static string[] ExampleWorkflowNames;

    public void LoadWorkflowFiles()
    {
        ComfyWorkflowStore.LoadWorkflowFiles(FilePath);
    }

    public static ComfyCustomWorkflow GetWorkflowByName(string name)
    {
        return ComfyWorkflowStore.GetWorkflowByName(name);
    }

    public void Refresh()
    {
        List<Task> tasks = [];
        try
        {
            ComfyUIRedirectHelper.ObjectInfoReadCacher.ForceExpire();
            LoadWorkflowFiles();
            foreach (ComfyUIAPIAbstractBackend backend in RunningComfyBackends.ToArray())
            {
                tasks.Add(backend.LoadValueSet(5));
            }
        }
        catch (Exception ex)
        {
            Logs.Error($"Error refreshing ComfyUI: {ex.ReadableString()}");
        }
        if (!tasks.Any())
        {
            return;
        }
        try
        {
            using CancellationTokenSource cancel = Utilities.TimedCancel(TimeSpan.FromMinutes(0.5));
            Task.WaitAll([.. tasks], cancel.Token);
        }
        catch (Exception ex)
        {
            Logs.Debug("ComfyUI refresh failed, will retry in background");
            Logs.Verbose($"Error refreshing ComfyUI: {ex.ReadableString()}");
            Utilities.RunCheckedTask(() =>
            {
                using CancellationTokenSource cancel = Utilities.TimedCancel(TimeSpan.FromMinutes(5));
                Task.WaitAll([.. tasks], cancel.Token);
            }, "refreshing ComfyUI");
        }
    }

    public void OnModelPathsChanged()
    {
        ComfyUISelfStartBackend.IsComfyModelFileEmitted = false;
        foreach (ComfyUISelfStartBackend backend in Program.Backends.RunningBackendsOfType<ComfyUISelfStartBackend>())
        {
            if (backend.IsEnabled)
            {
                Program.Backends.ReloadBackend(backend.BackendData).Wait(Program.GlobalProgramCancel);
            }
        }
    }

    public static async Task RunArbitraryWorkflowOnFirstBackend(string workflow, Action<object> takeRawOutput, bool allowRemote = true)
    {
        ComfyUIAPIAbstractBackend backend = RunningComfyBackends.FirstOrDefault(b => allowRemote || b is ComfyUISelfStartBackend) ?? throw new SwarmUserErrorException("No available ComfyUI Backend to run this operation");
        await backend.AwaitJobLive(workflow, "0", takeRawOutput, new(null), Program.GlobalProgramCancel);
    }

    public static void OnSwarmBackendAdded(SwarmSwarmBackend backend)
    {
        // TODO: Multi-layered forwarding? (Swarm connects to Swarm connects to Comfy)
        if (backend.LinkedRemoteBackendType?.StartsWith("comfyui_") != true)
        {
            return;
        }
        Utilities.RunCheckedTask(() => RefreshRemoteCapabilities(backend));
    }

    /// <summary>Refreshes linked Comfy capability evidence after its remote status is revised.</summary>
    private static void OnSwarmBackendRevised(SwarmSwarmBackend backend)
    {
        if (backend.IsAControlInstance || backend.LinkedRemoteBackendType?.StartsWith("comfyui_") != true)
        {
            return;
        }
        Utilities.RunCheckedTask(() => RefreshRemoteCapabilities(backend));
    }

    /// <summary>Fetches and publishes one linked Comfy backend's last-good object-info snapshot.</summary>
    private static async Task RefreshRemoteCapabilities(SwarmSwarmBackend backend)
    {
        SemaphoreSlim gate = RemoteCapabilityGates.GetOrAdd(backend, _ => new(1, 1));
        await gate.WaitAsync(Program.GlobalProgramCancel);
        try
        {
            using HttpRequestMessage request = new(HttpMethod.Get, $"{backend.Address}/ComfyBackendDirect/object_info");
            backend.RequestAdapter()?.Invoke(request);
            request.Headers.Add("X-Swarm-Backend-ID", $"{backend.LinkedRemoteBackendID}");
            using HttpResponseMessage response = await SwarmSwarmBackend.HttpClient.SendAsync(request, Program.GlobalProgramCancel);
            response.EnsureSuccessStatusCode();
            JObject rawObjectInfo = (await response.Content.ReadAsStringAsync()).ParseToJson();
            HashSet<string> nodeTypes = [.. rawObjectInfo.Properties().Select(property => property.Name)];
            ComfyBackendCapabilitySnapshot snapshot = AssignValuesFromRaw(backend, rawObjectInfo, nodeTypes, "/", () =>
            {
                return Program.Backends.AllBackends.TryGetValue(backend.BackendData.ID, out BackendHandler.BackendData registered)
                    && ReferenceEquals(registered.AbstractBackend, backend);
            });
            if (snapshot is not null)
            {
                backend.ExtensionData["ComfyNodeTypes"] = snapshot.NodeTypes;
            }
        }
        finally
        {
            gate.Release();
        }
    }

    /// <summary>Removes capability state for a backend that has left the handler registry.</summary>
    private static void OnBackendRemoved(BackendHandler.BackendData data)
    {
        object owner = data.AbstractBackend;
        if (owner is ComfyUIAPIAbstractBackend
            || owner is SwarmSwarmBackend remote && remote.LinkedRemoteBackendType?.StartsWith("comfyui_") == true)
        {
            lock (ValueAssignmentLocker)
            {
                Anima38ChoiceRegistry.Aggregate aggregate = Anima38Choices.Remove(owner);
                SharedValueCandidate sharedCandidate = MergeSharedValueDelta(new());
                ApplyAnima38Choices(sharedCandidate, aggregate);
                PublishSharedValues(sharedCandidate);
                ComfyCapabilityRegistry.Remove(owner);
            }
        }
        if (owner is SwarmSwarmBackend swarm)
        {
            RemoteCapabilityGates.TryRemove(swarm, out _);
        }
    }

    /// <summary>Coordinates publication of shared values discovered from ComfyUI backends.</summary>
    public static LockObject ValueAssignmentLocker = new();

    /// <summary>Add handlers here to do additional parsing of RawObjectInfo data.</summary>
    public static List<Action<JObject>> RawObjectInfoParsers = [];

    /// <summary>Sentinel registry owner used to retain accumulated node evidence from legacy object-info callers.</summary>
    private static readonly object LegacyObjectInfoOwner = new();

    /// <summary>Accumulated node evidence published through the legacy object-info facade.</summary>
    private static FrozenSet<string> LegacyObjectInfoNodeTypes = Array.Empty<string>().ToFrozenSet();

    /// <summary>Accumulated backend-local value capabilities published through the legacy object-info facade.</summary>
    private static FrozenSet<string> LegacyObjectInfoFeatures = Array.Empty<string>().ToFrozenSet();

    /// <summary>Owner-aware Anima 3.8B choice state synchronized by <see cref="ValueAssignmentLocker"/>.</summary>
    private static readonly Anima38ChoiceRegistry Anima38Choices = new();

    /// <summary>Backend-local shared values parsed without reading or mutating published aggregate state.</summary>
    private sealed class SharedValueDelta
    {
        /// <summary>Discovered upscale model values.</summary>
        public List<string> UpscalerModels = [];

        /// <summary>Discovered latent upscale model values.</summary>
        public List<string> LatentUpscalerModels = [];

        /// <summary>Whether SwarmKSampler sampler values were discovered.</summary>
        public bool HasSwarmKSamplerNames;

        /// <summary>Discovered unformatted SwarmKSampler sampler names.</summary>
        public List<string> SwarmKSamplerNames = [];

        /// <summary>Discovered SwarmKSampler scheduler values.</summary>
        public List<string> SwarmKSamplerSchedulers = [];

        /// <summary>Discovered standard KSampler sampler values.</summary>
        public List<string> KSamplerSamplers = [];

        /// <summary>Discovered standard KSampler scheduler values.</summary>
        public List<string> KSamplerSchedulers = [];

        /// <summary>Discovered IP-Adapter unified or legacy model values.</summary>
        public List<string> IPAdapterModels = [];

        /// <summary>Discovered IP-Adapter file model values.</summary>
        public List<string> IPAdapterFileModels = [];

        /// <summary>Discovered IP-Adapter weight type values.</summary>
        public List<string> IPAdapterWeightTypes = [];

        /// <summary>Discovered IP-Adapter FaceID model values.</summary>
        public List<string> IPAdapterFaceModels = [];

        /// <summary>Discovered GLIGEN model values.</summary>
        public List<string> GligenModels = [];

        /// <summary>Discovered style model values.</summary>
        public List<string> StyleModels = [];

        /// <summary>Discovered YOLO model values.</summary>
        public List<string> YoloModels = [];

        /// <summary>Discovered ControlNet union type values.</summary>
        public List<string> ControlnetUnionTypes = [];

        /// <summary>Discovered CLIP device values.</summary>
        public List<string> SetClipDevices = [];

        /// <summary>Discovered model attention backend values.</summary>
        public List<string> ModelAttentionBackends = [];

        /// <summary>Discovered Anima 3.8B Qwen3.5 encoder values.</summary>
        public List<string> Anima38Qwen35Encoders = [];

        /// <summary>Discovered Anima 3.8B progressive adapter values.</summary>
        public List<string> Anima38Adapters = [];

        /// <summary>Discovered ControlNet preprocessor definitions.</summary>
        public Dictionary<string, JToken> ControlNetPreprocessors = [];
    }

    /// <summary>Complete replacement lists and additive preprocessor entries prepared from the latest published state.</summary>
    private sealed class SharedValueCandidate
    {
        /// <summary>Candidate upscale model values.</summary>
        public List<string> UpscalerModels;

        /// <summary>Candidate sampler values.</summary>
        public List<string> Samplers;

        /// <summary>Candidate scheduler values.</summary>
        public List<string> Schedulers;

        /// <summary>Candidate IP-Adapter model values.</summary>
        public List<string> IPAdapterModels;

        /// <summary>Candidate IP-Adapter weight type values.</summary>
        public List<string> IPAdapterWeightTypes;

        /// <summary>Candidate GLIGEN model values.</summary>
        public List<string> GligenModels;

        /// <summary>Candidate YOLO model values.</summary>
        public List<string> YoloModels;

        /// <summary>Candidate style model values.</summary>
        public List<string> StyleModels;

        /// <summary>Candidate ControlNet union type values.</summary>
        public List<string> ControlnetUnionTypes;

        /// <summary>Candidate CLIP device values.</summary>
        public List<string> SetClipDevices;

        /// <summary>Candidate model attention backend values.</summary>
        public List<string> ModelAttentionBackends;

        /// <summary>Candidate Anima 3.8B Qwen3.5 encoder values.</summary>
        public List<string> Anima38Qwen35Encoders;

        /// <summary>Candidate Anima 3.8B progressive adapter values.</summary>
        public List<string> Anima38Adapters;

        /// <summary>Candidate additive ControlNet preprocessor definitions.</summary>
        public Dictionary<string, JToken> ControlNetPreprocessors;
    }

    public static bool TryGetRequiredInputs(JObject raw, string node, string id, out JToken list)
    {
        if (!raw.TryGetValue(node, out JToken key))
        {
            list = null;
            return false;
        }
        JToken req = key["input"]["required"][id];
        foreach (JToken val in req)
        {
            if (val.Type == JTokenType.String) { continue; } // Some have "COMBO" as first string now
            else if (val.Type == JTokenType.Array) { list = val; return true; }
            else if (val.Type == JTokenType.Object && val["options"] is JToken opts && opts.Type == JTokenType.Array) { list = opts; return true; }
            else { Logs.Warning($"Invalid JSON data type in object_info entry for node '{node}' input '{id}': {val.Type} ... {val}"); }
        }
        Logs.Warning($"object_info has node '{node}' but the input '{id}' is missing or invalid");
        list = null;
        return false;
    }

    /// <summary>Reads optional string choices from one ComfyUI node input without failing discovery for older or malformed backends.</summary>
    /// <param name="rawObjectInfo">The raw ComfyUI object-info response to parse.</param>
    /// <param name="nodeName">The node whose required input contains the choices.</param>
    /// <param name="inputName">The required input whose choices should be read.</param>
    /// <returns>Valid string choices from the input, or an empty list when the node is unavailable or invalid.</returns>
    private static List<string> ReadOptionalStringChoices(JObject rawObjectInfo, string nodeName, string inputName)
    {
        if (!rawObjectInfo.ContainsKey(nodeName))
        {
            return [];
        }
        try
        {
            if (!TryGetRequiredInputs(rawObjectInfo, nodeName, inputName, out JToken choices))
            {
                return [];
            }
            List<string> result = [];
            foreach (JToken choice in choices)
            {
                if (choice.Type == JTokenType.String)
                {
                    result.Add($"{choice}");
                }
                else
                {
                    Logs.Warning($"Invalid JSON data type in object_info choices for node '{nodeName}' input '{inputName}': {choice.Type}");
                }
            }
            return result;
        }
        catch (Exception ex)
        {
            Logs.Warning($"Unable to read object_info choices for node '{nodeName}' input '{inputName}': {ex.GetType().Name}");
            return [];
        }
    }

    /// <summary>Builds a backend-local delta for shared values discovered in raw ComfyUI object info.</summary>
    /// <param name="rawObjectInfo">The raw ComfyUI object-info response to parse.</param>
    /// <returns>A backend-local shared-value delta that has not read or changed published state.</returns>
    private static SharedValueDelta BuildSharedValueDelta(JObject rawObjectInfo)
    {
        SharedValueDelta delta = new();
        if (TryGetRequiredInputs(rawObjectInfo, "UpscaleModelLoader", "model_name", out JToken upscaleModels))
        {
            delta.UpscalerModels = [.. upscaleModels.Select(u => $"model-{u}///Model: {u}")];
        }
        if (TryGetRequiredInputs(rawObjectInfo, "LatentUpscaleModelLoader", "model_name", out JToken latentUpscaleModels))
        {
            delta.LatentUpscalerModels = [.. latentUpscaleModels.Select(u => $"latentmodel-{u}///Latent Model: {u}")];
        }
        if (TryGetRequiredInputs(rawObjectInfo, ComfyNodeNames.KSampler, ComfyNodeInputNames.KSampler.SamplerName, out JToken swarmksamplerNames))
        {
            delta.HasSwarmKSamplerNames = true;
            delta.SwarmKSamplerNames = [.. swarmksamplerNames.Select(u => $"{u}")];
        }
        if (TryGetRequiredInputs(rawObjectInfo, ComfyNodeNames.KSampler, ComfyNodeInputNames.KSampler.Scheduler, out JToken swarmksamplerSchedulers))
        {
            delta.SwarmKSamplerSchedulers = [.. swarmksamplerSchedulers.Select(u => $"{u}///{u} (New)")];
        }
        if (TryGetRequiredInputs(rawObjectInfo, "KSampler", "sampler_name", out JToken ksamplerSamplers))
        {
            delta.KSamplerSamplers = [.. ksamplerSamplers.Select(u => $"{u}///{u} (New in KS)")];
        }
        if (TryGetRequiredInputs(rawObjectInfo, "KSampler", "scheduler", out JToken ksamplerSchedulers))
        {
            delta.KSamplerSchedulers = [.. ksamplerSchedulers.Select(u => $"{u}///{u} (New in KS)")];
        }
        if (TryGetRequiredInputs(rawObjectInfo, "IPAdapterUnifiedLoader", "preset", out JToken ipadapterCubiqUnified))
        {
            delta.IPAdapterModels = [.. ipadapterCubiqUnified.Select(m => $"{m}")];
        }
        else if (rawObjectInfo.TryGetValue("IPAdapter", out JToken ipadapter) && (ipadapter["input"]["required"] as JObject).TryGetValue("model_name", out JToken ipAdapterModelName))
        {
            delta.IPAdapterModels = [.. ipAdapterModelName[0].Select(m => $"{m}")];
        }
        if (TryGetRequiredInputs(rawObjectInfo, "IPAdapterModelLoader", "ipadapter_file", out JToken ipadapterCubiq))
        {
            HashSet<string> native = ["ip-adapter-faceid-portrait-v11_sd15.bin", "ip-adapter-faceid-portrait_sdxl.bin", "ip-adapter-faceid-portrait_sdxl_unnorm.bin", "ip-adapter-faceid-plusv2_sd15.bin", "ip-adapter-faceid-plusv2_sdxl.bin", "ip-adapter-faceid-plus_sd15.bin", "ip-adapter-faceid_sd15.bin", "ip-adapter-faceid_sdxl.bin", "full_face_sd15.safetensors", "ip-adapter-plus-face_sd15.safetensors", "ip-adapter-plus-face_sdxl_vit-h.safetensors", "ip-adapter-plus_sd15.safetensors", "ip-adapter-plus_sdxl_vit-h.safetensors", "ip-adapter_sd15_vit-G.safetensors", "ip-adapter_sdxl.safetensors", "ip-adapter_sd15.safetensors", "ip-adapter_sdxl_vit-h.safetensors", "sd15_light_v11.bin"];
            string[] models = [.. ipadapterCubiq.Select(m => $"{m}").Where(m => !native.Contains(m))];
            delta.IPAdapterFileModels = [.. models.Select(m => $"file:{m}///Model File: {m}")];
        }
        if (rawObjectInfo.TryGetValue("IPAdapter", out JToken ipadapter2) && (ipadapter2["input"]["required"] as JObject).TryGetValue("weight_type", out JToken ipAdapterWeightType))
        {
            delta.IPAdapterWeightTypes = [.. ipAdapterWeightType[0].Select(m => $"{m}///{m} (New)")];
        }
        if (TryGetRequiredInputs(rawObjectInfo, "IPAdapterUnifiedLoaderFaceID", "preset", out JToken ipadapterCubiqUnifiedFace))
        {
            delta.IPAdapterFaceModels = [.. ipadapterCubiqUnifiedFace.Select(m => $"{m}")];
        }
        if (TryGetRequiredInputs(rawObjectInfo, "GLIGENLoader", "gligen_name", out JToken gligenLoader))
        {
            delta.GligenModels = [.. gligenLoader.Select(m => $"{m}")];
        }
        if (TryGetRequiredInputs(rawObjectInfo, "StyleModelLoader", "style_model_name", out JToken styleModelLoader))
        {
            delta.StyleModels = [.. styleModelLoader.Select(m => $"{m}")];
        }
        if (TryGetRequiredInputs(rawObjectInfo, ComfyNodeNames.YoloDetection, ComfyNodeInputNames.YoloDetection.ModelName, out JToken yoloDetection))
        {
            delta.YoloModels = [.. yoloDetection.Select(m => $"{m}")];
        }
        if (TryGetRequiredInputs(rawObjectInfo, "SetUnionControlNetType", "type", out JToken unionCtrlNet))
        {
            delta.ControlnetUnionTypes = [.. unionCtrlNet.Select(m => $"{m}///{m} (New)")];
        }
        if (TryGetRequiredInputs(rawObjectInfo, "OverrideCLIPDevice", "device", out JToken overrideClipDevice))
        {
            delta.SetClipDevices = [.. overrideClipDevice.Select(m => $"{m}")];
        }
        if (TryGetRequiredInputs(rawObjectInfo, ComfyNodeNames.ModelAttentionBackend, ComfyNodeInputNames.ModelAttentionBackend.Attention, out JToken modelAttentionBackends))
        {
            delta.ModelAttentionBackends = [.. modelAttentionBackends.Select(m => $"{m}")];
        }
        delta.Anima38Qwen35Encoders = ReadOptionalStringChoices(rawObjectInfo, ComfyNodeNames.LoadAnima38Qwen35, ComfyNodeInputNames.LoadAnima38Qwen35.QwenFilename);
        delta.Anima38Adapters = ReadOptionalStringChoices(rawObjectInfo, ComfyNodeNames.Anima38Conditioning, ComfyNodeInputNames.Anima38Conditioning.Adapter);
        foreach ((string key, JToken data) in rawObjectInfo)
        {
            if (data is JObject dataObject && $"{dataObject["category"]}" == "image/preprocessors")
            {
                delta.ControlNetPreprocessors[key] = data;
            }
            else if (key.EndsWith("Preprocessor") && key != "MeshGraphormer+ImpactDetector-DepthMapPreprocessor")
            {
                delta.ControlNetPreprocessors[key] = data;
            }
        }
        return delta;
    }

    /// <summary>Builds backend-local routing features from values discovered in one object-info response.</summary>
    /// <param name="delta">The backend-local values parsed from object info.</param>
    /// <returns>An immutable set of exact-value routing capabilities.</returns>
    private static FrozenSet<string> BuildBackendValueFeatures(SharedValueDelta delta)
    {
        HashSet<string> features = [.. delta.ModelAttentionBackends.Select(ComfyCapabilityCatalog.ModelAttentionBackendValueFeature)];
        features.UnionWith(delta.Anima38Qwen35Encoders.Where(value => value != "auto").Select(ComfyCapabilityCatalog.Anima38Qwen35ValueFeature));
        features.UnionWith(delta.Anima38Adapters.Where(value => value != "auto").Select(ComfyCapabilityCatalog.Anima38AdapterValueFeature));
        if (IsAnima38QwenAutoResolvable(delta.Anima38Qwen35Encoders))
        {
            features.Add(ComfyCapabilityCatalog.Anima38Qwen35AutoFeature);
        }
        if (IsAnima38AdapterAutoResolvable(delta.Anima38Adapters))
        {
            features.Add(ComfyCapabilityCatalog.Anima38AdapterAutoFeature);
        }
        return features.ToFrozenSet();
    }

    /// <summary>Returns the automatic-selection priority for one advertised Qwen3.5 encoder filename.</summary>
    private static int Anima38QwenAutoPriority(string value)
    {
        string normalized = value.Replace('\\', '/').ToLowerFast();
        int slash = normalized.LastIndexOf('/');
        string basename = slash < 0 ? normalized : normalized[(slash + 1)..];
        if (basename.Contains("anima38"))
        {
            return 0;
        }
        if (basename == "qwen35_4b.safetensors")
        {
            return 1;
        }
        return 2;
    }

    /// <summary>Returns whether one backend's Qwen choices resolve automatically under the Python node's priority rules.</summary>
    private static bool IsAnima38QwenAutoResolvable(IEnumerable<string> values)
    {
        List<string> candidates = [.. values.Where(value => value != "auto").Distinct(StringComparer.Ordinal)];
        if (candidates.Count == 1)
        {
            return true;
        }
        if (candidates.Count == 0)
        {
            return false;
        }
        int bestPriority = candidates.Min(Anima38QwenAutoPriority);
        return bestPriority < 2 && candidates.Count(candidate => Anima38QwenAutoPriority(candidate) == bestPriority) == 1;
    }

    /// <summary>Returns whether one backend advertises exactly one compatible progressive adapter.</summary>
    private static bool IsAnima38AdapterAutoResolvable(IEnumerable<string> values)
    {
        return values.Where(value => value != "auto").Distinct(StringComparer.Ordinal).Take(2).Count() == 1;
    }

    /// <summary>Merges a backend-local delta into copies of the latest published shared values.</summary>
    /// <remarks>The caller must own <see cref="ValueAssignmentLocker"/>.</remarks>
    /// <param name="delta">The backend-local values to merge.</param>
    /// <returns>A complete list replacement candidate and additive preprocessor entries.</returns>
    private static SharedValueCandidate MergeSharedValueDelta(SharedValueDelta delta)
    {
        SharedValueCandidate candidate = new()
        {
            UpscalerModels = [.. UpscalerModels],
            Samplers = [.. Samplers],
            Schedulers = [.. Schedulers],
            IPAdapterModels = [.. IPAdapterModels],
            IPAdapterWeightTypes = [.. IPAdapterWeightTypes],
            GligenModels = [.. GligenModels],
            YoloModels = [.. YoloModels],
            StyleModels = [.. StyleModels],
            ControlnetUnionTypes = [.. ControlnetUnionTypes],
            SetClipDevices = [.. SetClipDevices],
            ModelAttentionBackends = [.. ModelAttentionBackends],
            Anima38Qwen35Encoders = ["auto"],
            Anima38Adapters = ["auto"],
            ControlNetPreprocessors = new(delta.ControlNetPreprocessors)
        };
        T2IParamTypes.ConcatDropdownValsClean(ref candidate.UpscalerModels, delta.UpscalerModels);
        T2IParamTypes.ConcatDropdownValsClean(ref candidate.UpscalerModels, delta.LatentUpscalerModels);
        if (delta.HasSwarmKSamplerNames)
        {
            string[] dropped = [.. candidate.Samplers.Select(s => s.Before("///")).Except(delta.SwarmKSamplerNames)];
            if (dropped.Any())
            {
                Logs.Warning($"Samplers are listed, but not included in SwarmKSampler internal list: {dropped.JoinString(", ")}");
            }
            T2IParamTypes.ConcatDropdownValsClean(ref candidate.Samplers, delta.SwarmKSamplerNames.Select(u => $"{u}///{u} (New)"));
        }
        T2IParamTypes.ConcatDropdownValsClean(ref candidate.Schedulers, delta.SwarmKSamplerSchedulers);
        T2IParamTypes.ConcatDropdownValsClean(ref candidate.Samplers, delta.KSamplerSamplers);
        T2IParamTypes.ConcatDropdownValsClean(ref candidate.Schedulers, delta.KSamplerSchedulers);
        T2IParamTypes.ConcatDropdownValsClean(ref candidate.IPAdapterModels, delta.IPAdapterModels);
        T2IParamTypes.ConcatDropdownValsClean(ref candidate.IPAdapterModels, delta.IPAdapterFileModels);
        T2IParamTypes.ConcatDropdownValsClean(ref candidate.IPAdapterWeightTypes, delta.IPAdapterWeightTypes);
        T2IParamTypes.ConcatDropdownValsClean(ref candidate.IPAdapterModels, delta.IPAdapterFaceModels);
        T2IParamTypes.ConcatDropdownValsClean(ref candidate.GligenModels, delta.GligenModels);
        T2IParamTypes.ConcatDropdownValsClean(ref candidate.StyleModels, delta.StyleModels);
        T2IParamTypes.ConcatDropdownValsClean(ref candidate.YoloModels, delta.YoloModels);
        T2IParamTypes.ConcatDropdownValsClean(ref candidate.ControlnetUnionTypes, delta.ControlnetUnionTypes);
        T2IParamTypes.ConcatDropdownValsClean(ref candidate.SetClipDevices, delta.SetClipDevices);
        T2IParamTypes.ConcatDropdownValsClean(ref candidate.ModelAttentionBackends, delta.ModelAttentionBackends);
        return candidate;
    }

    /// <summary>Applies a current-owner Anima 3.8B aggregate to a shared-value publication candidate.</summary>
    /// <param name="candidate">The shared-value candidate being prepared.</param>
    /// <param name="aggregate">The fresh current-owner Anima choices.</param>
    private static void ApplyAnima38Choices(SharedValueCandidate candidate, Anima38ChoiceRegistry.Aggregate aggregate)
    {
        candidate.Anima38Qwen35Encoders = aggregate.Qwen;
        candidate.Anima38Adapters = aggregate.Adapters;
    }

    /// <summary>Publishes a fully built shared-value candidate.</summary>
    /// <param name="candidate">The candidate values to publish.</param>
    private static void PublishSharedValues(SharedValueCandidate candidate)
    {
        UpscalerModels = candidate.UpscalerModels;
        Samplers = candidate.Samplers;
        Schedulers = candidate.Schedulers;
        IPAdapterModels = candidate.IPAdapterModels;
        IPAdapterWeightTypes = candidate.IPAdapterWeightTypes;
        GligenModels = candidate.GligenModels;
        YoloModels = candidate.YoloModels;
        StyleModels = candidate.StyleModels;
        ControlnetUnionTypes = candidate.ControlnetUnionTypes;
        SetClipDevices = candidate.SetClipDevices;
        ModelAttentionBackends = candidate.ModelAttentionBackends;
        Anima38Qwen35Encoders = candidate.Anima38Qwen35Encoders;
        Anima38Adapters = candidate.Anima38Adapters;
        foreach ((string key, JToken data) in candidate.ControlNetPreprocessors)
        {
            ControlNetPreprocessors[key] = data;
        }
    }

    /// <summary>Runs extension object-info parsers in registration order with isolated failures.</summary>
    /// <param name="rawObjectInfo">The raw ComfyUI object-info response to pass to each parser.</param>
    private static void RunRawObjectInfoParsers(JObject rawObjectInfo)
    {
        foreach (Action<JObject> parser in RawObjectInfoParsers)
        {
            try
            {
                parser(rawObjectInfo);
            }
            catch (Exception ex)
            {
                Logs.Error($"Error while running extension parsing on raw object info: {ex.ReadableString()}");
            }
        }
    }

    /// <summary>Publishes object-info values and accumulated capability evidence for legacy callers.</summary>
    /// <param name="rawObjectInfo">The raw ComfyUI object-info response to parse.</param>
    public static void AssignValuesFromRaw(JObject rawObjectInfo)
    {
        SharedValueDelta sharedDelta = BuildSharedValueDelta(rawObjectInfo);
        FrozenSet<string> rawValueFeatures = BuildBackendValueFeatures(sharedDelta);
        FrozenSet<string> rawNodeTypes = rawObjectInfo.Properties().Select(property => property.Name).ToFrozenSet();
        lock (ValueAssignmentLocker)
        {
            HashSet<string> accumulatedNodeTypes = [.. LegacyObjectInfoNodeTypes];
            accumulatedNodeTypes.UnionWith(rawNodeTypes);
            FrozenSet<string> frozenAccumulatedNodeTypes = accumulatedNodeTypes.ToFrozenSet();
            HashSet<string> accumulatedValueFeatures = [.. LegacyObjectInfoFeatures];
            accumulatedValueFeatures.RemoveWhere(feature => feature.StartsWith(ComfyCapabilityCatalog.Anima38Qwen35ValueFeaturePrefix, StringComparison.Ordinal));
            accumulatedValueFeatures.RemoveWhere(feature => feature.StartsWith(ComfyCapabilityCatalog.Anima38AdapterValueFeaturePrefix, StringComparison.Ordinal));
            accumulatedValueFeatures.Remove(ComfyCapabilityCatalog.Anima38Qwen35AutoFeature);
            accumulatedValueFeatures.Remove(ComfyCapabilityCatalog.Anima38AdapterAutoFeature);
            accumulatedValueFeatures.UnionWith(rawValueFeatures);
            FrozenSet<string> frozenAccumulatedValueFeatures = accumulatedValueFeatures.ToFrozenSet();
            SharedValueCandidate sharedCandidate = MergeSharedValueDelta(sharedDelta);
            ComfyCapabilityRegistry.RegistryCandidate capabilityCandidate = ComfyCapabilityRegistry.PreparePublish(LegacyObjectInfoOwner, frozenAccumulatedNodeTypes, "/", frozenAccumulatedValueFeatures);
            Anima38ChoiceRegistry.Aggregate aggregate = Anima38Choices.Replace(LegacyObjectInfoOwner, sharedDelta.Anima38Qwen35Encoders, sharedDelta.Anima38Adapters);
            ApplyAnima38Choices(sharedCandidate, aggregate);
            PublishSharedValues(sharedCandidate);
            ComfyCapabilityRegistry.Commit(capabilityCandidate);
            LegacyObjectInfoNodeTypes = frozenAccumulatedNodeTypes;
            LegacyObjectInfoFeatures = frozenAccumulatedValueFeatures;
            RunRawObjectInfoParsers(rawObjectInfo);
            ComfyCapabilityRegistry.GetSnapshot(LegacyObjectInfoOwner);
            ComfyCapabilityRegistry.GetAggregateSnapshot();
        }
    }

    /// <summary>Publishes object-info values and capability evidence for a specific backend owner.</summary>
    /// <param name="owner">The backend object whose identity owns the capability snapshot.</param>
    /// <param name="rawObjectInfo">The raw ComfyUI object-info response to parse.</param>
    /// <param name="nodeTypes">The ComfyUI node types exposed by the backend.</param>
    /// <param name="modelFolderFormat">The path separator format used by the backend's model folders.</param>
    /// <returns>The final immutable capability snapshot published for the owner.</returns>
    public static ComfyBackendCapabilitySnapshot AssignValuesFromRaw(object owner, JObject rawObjectInfo, IReadOnlySet<string> nodeTypes, string modelFolderFormat)
    {
        return AssignValuesFromRaw(owner, rawObjectInfo, nodeTypes, modelFolderFormat, null);
    }

    /// <summary>Publishes owner-aware object info only if its owner remains registered when publication begins.</summary>
    internal static ComfyBackendCapabilitySnapshot AssignValuesFromRaw(object owner, JObject rawObjectInfo, IReadOnlySet<string> nodeTypes, string modelFolderFormat, Func<bool> canPublish)
    {
        SharedValueDelta sharedDelta = BuildSharedValueDelta(rawObjectInfo);
        FrozenSet<string> backendValueFeatures = BuildBackendValueFeatures(sharedDelta);
        FrozenSet<string> frozenNodeTypes = nodeTypes.ToFrozenSet();
        lock (ValueAssignmentLocker)
        {
            if (canPublish is not null && !canPublish())
            {
                return null;
            }
            SharedValueCandidate sharedCandidate = MergeSharedValueDelta(sharedDelta);
            ComfyCapabilityRegistry.RegistryCandidate capabilityCandidate = ComfyCapabilityRegistry.PreparePublish(owner, frozenNodeTypes, modelFolderFormat, backendValueFeatures);
            Anima38ChoiceRegistry.Aggregate aggregate = Anima38Choices.Replace(owner, sharedDelta.Anima38Qwen35Encoders, sharedDelta.Anima38Adapters);
            ApplyAnima38Choices(sharedCandidate, aggregate);
            PublishSharedValues(sharedCandidate);
            ComfyCapabilityRegistry.Commit(capabilityCandidate);
            RunRawObjectInfoParsers(rawObjectInfo);
            return ComfyCapabilityRegistry.GetSnapshot(owner);
        }
    }

    public static T2IRegisteredParam<string> CustomWorkflowParam, SamplerParam, SchedulerParam, RefinerSamplerParam, RefinerSchedulerParam, RefinerUpscaleMethod, UseIPAdapterForRevision, IPAdapterWeightType, VideoPreviewType, VideoFrameInterpolationMethod, GligenModel, RegionalPromptingMethod, YoloModelInternal, PreferredDType, UseStyleModel, TeaCacheMode, EasyCacheMode, SetClipDevice, UseSparseAttention, EnableReferenceLatents, TextEncodedImage;

    /// <summary>Parameter that selects a backend-supported model attention implementation.</summary>
    public static T2IRegisteredParam<string> ModelAttentionBackend;

    /// <summary>Parameter that selects the Anima 3.8B Qwen3.5 semantic encoder.</summary>
    public static T2IRegisteredParam<string> Anima38Qwen35Encoder;

    /// <summary>Parameter that selects the Anima 3.8B progressive semantic adapter.</summary>
    public static T2IRegisteredParam<string> Anima38Adapter;

    /// <summary>Recomputes backend-local routing requirements from the finalized generation input.</summary>
    private static readonly Action<T2IParamInput> RecomputeBackendRoutingRequirementsHandler = RecomputeBackendRoutingRequirements;

    /// <summary>Parameter that selects the SeedVR pre-upscale method.</summary>
    public static T2IRegisteredParam<string> SeedVRUpscaleMethod;

    /// <summary>Parameter that selects the SeedVR color-correction behavior.</summary>
    public static T2IRegisteredParam<string> SeedVRColorCorrectionBehavior;

    public static T2IRegisteredParam<bool> AITemplateParam, DebugRegionalPrompting, ShiftedLatentAverageInit, UseCfgZeroStar, UseTCFG, DetailDaemonSmooth;

    /// <summary>Parameter that enables VRAM-aware temporal chunking for SeedVR video restoration.</summary>
    public static T2IRegisteredParam<bool> SeedVRSplitLatent;

    public static T2IRegisteredParam<double> IPAdapterWeight, IPAdapterStart, IPAdapterEnd, SelfAttentionGuidanceScale, SelfAttentionGuidanceSigmaBlur, PerturbedAttentionGuidanceScale, StyleModelMergeStrength, StyleModelApplyStart, StyleModelMultiplyStrength, RescaleCFGMultiplier, TeaCacheThreshold, TeaCacheStart, NunchakuCacheThreshold, EasyCacheThreshold, EasyCacheStart, EasyCacheEnd, RenormCFG, NormalizedAttentionGuidanceScale, NormalizedAttentionGuidanceAlpha, NormalizedAttentionGuidanceTau, DetailDaemonAmount, DetailDaemonStart, DetailDaemonEnd, DetailDaemonBias, DetailDaemonExponent, DetailDaemonStartOffset, DetailDaemonEndOffset, DetailDaemonFade, DetailDaemonCFGScaleOverride;

    /// <summary>Parameter that controls the Anima 3.8B progressive semantic adapter strength.</summary>
    public static T2IRegisteredParam<double> Anima38AdapterStrength;

    /// <summary>Parameter that controls the SeedVR upscale factor.</summary>
    public static T2IRegisteredParam<double> SeedVRUpscale;

    /// <summary>Parameter that controls the optional SeedVR pre-downscale factor.</summary>
    public static T2IRegisteredParam<double> SeedVRPreDownscale;

    public static T2IRegisteredParam<int> RefinerHyperTile, VideoFrameInterpolationMultiplier;

    /// <summary>Parameter that controls temporal overlap between SeedVR latent chunks.</summary>
    public static T2IRegisteredParam<int> SeedVRTemporalVideoOverlap;

    public static T2IRegisteredParam<T2IModel> PixelDecoderModel;

    /// <summary>Parameter that selects the SeedVR restoration model.</summary>
    public static T2IRegisteredParam<T2IModel> SeedVRModel;

    /// <summary>Per-ControlNet preprocessor resolution parameters.</summary>
    public static T2IRegisteredParam<int>[] ControlNetPreprocessorResolutionParams = new T2IRegisteredParam<int>[3];

    public static T2IRegisteredParam<string>[] ControlNetPreprocessorParams = new T2IRegisteredParam<string>[3], ControlNetUnionTypeParams = new T2IRegisteredParam<string>[3];

    public static List<string> UpscalerModels = ["pixel-lanczos///Pixel: Lanczos (cheap + high quality)", "pixel-bicubic///Pixel: Bicubic (Basic)", "pixel-area///Pixel: Area", "pixel-bilinear///Pixel: Bilinear", "pixel-nearest-exact///Pixel: Nearest-Exact (Pixel art)", "latent-bislerp///Latent: Bislerp", "latent-bicubic///Latent: Bicubic", "latent-area///Latent: Area", "latent-bilinear///Latent: Bilinear", "latent-nearest-exact///Latent: Nearest-Exact"],
        Samplers =
        [
            // K-Diffusion
            "euler///Euler", "euler_ancestral///Euler Ancestral (Randomizing)", "heun///Heun (2x Slow)", "heunpp2///Heun++ 2 (2x Slow)", "dpm_2///DPM-2 (Diffusion Probabilistic Model) (2x Slow)", "dpm_2_ancestral///DPM-2 Ancestral (2x Slow)",
            "lms///LMS (Linear Multi-Step)", "dpm_fast///DPM Fast (DPM without the DPM2 slowdown)", "dpm_adaptive///DPM Adaptive (Dynamic Steps)",
            "dpmpp_2s_ancestral///DPM++ 2S Ancestral (2nd Order Single-Step) (2x Slow)", "dpmpp_sde///DPM++ SDE (Stochastic / randomizing) (2x Slow)", "dpmpp_sde_gpu///DPM++ SDE, GPU Seeded (2x Slow)",
            "dpmpp_2m///DPM++ 2M (2nd Order Multi-Step)", "dpmpp_2m_sde///DPM++ 2M SDE", "dpmpp_2m_sde_gpu///DPM++ 2M SDE, GPU Seeded", "dpmpp_3m_sde///DPM++ 3M SDE (3rd Order Multi-Step)", "dpmpp_3m_sde_gpu///DPM++ 3M SDE, GPU Seeded",
            "ddim///DDIM (Denoising Diffusion Implicit Models) (Identical to Euler)", "ddpm///DDPM (Denoising Diffusion Probabilistic Models)",
            // Unique tack-ons
             "lcm///LCM (for LCM models)", "uni_pc///UniPC (Unified Predictor-Corrector)", "uni_pc_bh2///UniPC BH2", "res_multistep///Res MultiStep (for Cosmos)", "res_multistep_ancestral///Res MultiStep Ancestral (randomizing, for Cosmos)",
            "ipndm///iPNDM (Improved Pseudo-Numerical methods for Diffusion Models)", "ipndm_v///iPNDM-V (Variable-Step)", "deis///DEIS (Diffusion Exponential Integrator Sampler)", "gradient_estimation///Gradient Estimation (Improving from Optimization Perspective)",
            "er_sde///ER-SDE-Solver (used with AlignYourSteps schedule)", "seeds_2///SEEDS 2 (Exponential SDE Solvers, variant of DPM++ SDE)", "seeds_3///SEEDS 3", "sa_solver///SA-Solver (Stochastic Adams)", "sa_solver_pece///SA-Solver PECE",
            "exp_heun_2_x0///EXP Heun 2 x0", "exp_heun_2_x0_sde///EXP Heun 2 x0 SDE", "dpmpp_2m_sde_heun///DPM++ 2M SDE Heun", "dpmpp_2m_sde_heun_gpu///DPM++ 2M SDE Heun, GPU Seeded",
            // CFG++ variants
            "euler_cfg_pp///Euler CFG++ (Manifold-constrained CFG)", "euler_ancestral_cfg_pp///Euler Ancestral CFG++", "dpmpp_2m_cfg_pp///DPM++ 2M CFG++", "dpmpp_2s_ancestral_cfg_pp///DPM++ 2S Ancestral CFG++ (2x Slow)", "res_multistep_cfg_pp///Res MultiStep CFG++", "res_multistep_ancestral_cfg_pp///Res MultiStep Ancestral CFG++", "gradient_estimation_cfg_pp///Gradient Estimation CFG++"
        ],
        Schedulers = ["normal///Normal", "karras///Karras", "exponential///Exponential", "simple///Simple", "ddim_uniform///DDIM Uniform", "sgm_uniform///SGM Uniform", "turbo///Turbo (for turbo models, max 10 steps)", "align_your_steps///Align Your Steps (Model-specific behavior)", "beta///Beta", "linear_quadratic///Linear Quadratic (Mochi)", "ltxv///LTX-Video", "ltxv-image///LTXV-Image", "kl_optimal///KL Optimal (Nvidia AYS)", "flux2///Flux.2", "ideogram4///Ideogram 4 Default", "ideogram4turbo///Ideogram4 Turbo"];

    /// <summary>Lists PiD decoder models.</summary>
    public static List<string> PidUpscaleModels(Session session)
    {
        return [.. Program.MainSDModels.ListModelsFor(session)
            .Where(m => m.ModelClass?.CompatClass?.ID == "pid")
            .OrderBy(m => m.Name)
            .Select(m => $"pidmodel-{m.Name}///PiD Model: {m.Name}")];
    }

    /// <summary>Resolves a PiD model from a model name.</summary>
    public static T2IModel GetPidModel(string name, Session session)
    {
        using ManyReadOneWriteLock.ReadClaim claim = Program.RefreshLock.LockRead();
        string matched = T2IParamTypes.GetBestModelInList(name, Program.MainSDModels.ListModelNamesFor(session));
        if (matched is not null && matched.EndsWith(".safetensors"))
        {
            matched = matched.BeforeLast('.');
        }
        T2IModel model = matched is null ? null : Program.MainSDModels.GetModel(matched);
        if (model is null || model.ModelClass?.CompatClass?.ID != "pid")
        {
            throw new SwarmUserErrorException($"PiD model '{name}' could not be found, or is not a valid PiD model.");
        }
        return model;
    }

    public static List<string> IPAdapterModels = ["None"], IPAdapterWeightTypes = ["standard", "prompt is more important", "style transfer"];

    public static List<string> GligenModels = ["None"], YoloModels = [], StyleModels = ["None"], SetClipDevices = ["cpu"];

    /// <summary>Model attention implementations discovered from connected ComfyUI backends.</summary>
    public static List<string> ModelAttentionBackends = ["pytorch attention"];

    /// <summary>Anima 3.8B Qwen3.5 encoders discovered from connected ComfyUI backends.</summary>
    public static List<string> Anima38Qwen35Encoders = ["auto"];

    /// <summary>Anima 3.8B progressive adapters discovered from connected ComfyUI backends.</summary>
    public static List<string> Anima38Adapters = ["auto"];

    public static List<string> ControlnetUnionTypes = ["auto", "openpose", "depth", "hed/pidi/scribble/ted", "canny/lineart/anime_lineart/mlsd", "normal", "segment", "tile", "repaint"];

    public static ConcurrentDictionary<string, JToken> ControlNetPreprocessors = new() { ["None"] = null };

    public static T2IParamGroup ComfyAdvancedGroup, DetailDaemonGroup;

    /// <summary>Parameter group for SeedVR restoration settings.</summary>
    public static T2IParamGroup GroupSeedVR;

    public static T2IRegisteredParam<string> Sam3PointCoordsPositive, Sam3PointCoordsNegative, Sam3BBox, Sam3MaskPadding, Sam3SegmentPrompt, Sam3SegmentConfidence;

    public static T2IRegisteredParam<string> Sam2PointCoordsPositive, Sam2PointCoordsNegative, Sam2BBox;

    /// <summary>Whether ComfyUI backend validation and API hooks have been registered.</summary>
    public static bool BackendHandlersRegistered = false;

    /// <summary>Creates the standard input set for a LoadSAM3Model node.</summary>
    public static JObject Sam3ModelInputs()
    {
        return new JObject()
        {
            ["precision"] = "auto",
            ["compile"] = false
        };
    }

    /// <summary>Creates the standard input set for a DownloadAndLoadSAM2Model node.</summary>
    public static JObject Sam2ModelInputs(string size = "base_plus", string segmentor = "single_image")
    {
        return new JObject()
        {
            ["model"] = $"sam2_hiera_{size}.safetensors",
            ["segmentor"] = segmentor,
            ["device"] = "cuda", // TODO: This should really be decided by the python, not by swarm's workflow generator - the python knows what the GPU supports, swarm does not
            ["precision"] = "bf16"
        };
    }

    /// <inheritdoc/>
    public override void OnInit()
    {
        RegisterBackendTypes();
        Program.Backends.BackendRemovedEvent += OnBackendRemoved;
        Sam3PointCoordsPositive = T2IParamTypes.Register<string>(new("SAM3 Positive Points", "Internal: JSON list of positive point coordinates for SAM3 point masking.",
            "[]", IgnoreIf: "[]", FeatureFlag: "sam3", VisibleNormally: false, ExtraHidden: true, DoNotSave: true, DoNotPreview: true, AlwaysRetain: true, Toggleable: true
            ));
        Sam3PointCoordsNegative = T2IParamTypes.Register<string>(new("SAM3 Negative Points", "Internal: JSON list of negative point coordinates for SAM3 point masking.",
            "[]", IgnoreIf: "[]", FeatureFlag: "sam3", VisibleNormally: false, ExtraHidden: true, DoNotSave: true, DoNotPreview: true, AlwaysRetain: true, Toggleable: true
            ));
        Sam3BBox = T2IParamTypes.Register<string>(new("SAM3 BBox", "Internal: JSON bounding box [x1,y1,x2,y2] for SAM3 bbox masking.",
            "", IgnoreIf: "", FeatureFlag: "sam3", VisibleNormally: false, ExtraHidden: true, DoNotSave: true, DoNotPreview: true, AlwaysRetain: true, Toggleable: true
            ));
        Sam3MaskPadding = T2IParamTypes.Register<string>(new("SAM3 Mask Padding", "Internal: Number of pixels to dilate/expand the SAM3 mask boundary.",
            "0", IgnoreIf: "0", FeatureFlag: "sam3", VisibleNormally: false, ExtraHidden: true, DoNotSave: true, DoNotPreview: true, AlwaysRetain: true, Toggleable: true
            ));
        Sam3SegmentPrompt = T2IParamTypes.Register<string>(new("SAM3 Segment Prompt", "Internal: text prompt for SAM3 image editor text masking.",
            "", IgnoreIf: "", FeatureFlag: "sam3", VisibleNormally: false, ExtraHidden: true, DoNotSave: true, DoNotPreview: true, AlwaysRetain: true, Toggleable: true
            ));
        Sam3SegmentConfidence = T2IParamTypes.Register<string>(new("SAM3 Segment Confidence", "Internal: confidence threshold for SAM3 image editor text masking.",
            "0.2", IgnoreIf: "0.2", FeatureFlag: "sam3", VisibleNormally: false, ExtraHidden: true, DoNotSave: true, DoNotPreview: true, AlwaysRetain: true, Toggleable: true
            ));
        Sam2PointCoordsPositive = T2IParamTypes.Register<string>(new("SAM2 Positive Points", "Internal: JSON list of positive point coordinates for SAM2 point masking.",
            "[]", IgnoreIf: "[]", FeatureFlag: "sam2", VisibleNormally: false, ExtraHidden: true, DoNotSave: true, DoNotPreview: true, AlwaysRetain: true, Toggleable: true, ID: "sam2positivepoints"
            ));
        Sam2PointCoordsNegative = T2IParamTypes.Register<string>(new("SAM2 Negative Points", "Internal: JSON list of negative point coordinates for SAM2 point masking.",
            "[]", IgnoreIf: "[]", FeatureFlag: "sam2", VisibleNormally: false, ExtraHidden: true, DoNotSave: true, DoNotPreview: true, AlwaysRetain: true, Toggleable: true, ID: "sam2negativepoints"
            ));
        Sam2BBox = T2IParamTypes.Register<string>(new("SAM2 BBox", "Internal: JSON bounding box [x1,y1,x2,y2] for SAM2 bbox masking.",
            "", IgnoreIf: "", FeatureFlag: "sam2", VisibleNormally: false, ExtraHidden: true, DoNotSave: true, DoNotPreview: true, AlwaysRetain: true, Toggleable: true, ID: "sam2bbox"
            ));
        UseIPAdapterForRevision = T2IParamTypes.Register<string>(new("Use IP-Adapter", $"Select an IP-Adapter model to use IP-Adapter for image-prompt input handling.\nModels will automatically be downloaded when you first use them.\nNote if you use a custom model, you must also set your CLIP-Vision Model under Advanced Model Addons, otherwise CLIP Vision G will be presumed.\n<a target=\"_blank\" href=\"{Utilities.RepoDocsRoot}/Features/ImagePrompting.md\">See more docs here.</a>",
            "None", IgnoreIf: "None", FeatureFlag: "ipadapter,model_has_ipadapter", GetValues: _ => IPAdapterModels, Group: T2IParamTypes.GroupImagePrompting, OrderPriority: 15, ChangeWeight: 1
            ));
        IPAdapterWeight = T2IParamTypes.Register<double>(new("IP-Adapter Weight", "Weight to use with IP-Adapter (if enabled).",
            "1", Min: -1, Max: 3, Step: 0.05, IgnoreIf: "1", FeatureFlag: "ipadapter", Group: T2IParamTypes.GroupImagePrompting, ViewType: ParamViewType.SLIDER, OrderPriority: 16, DependNonDefault: UseIPAdapterForRevision.Type.ID
            ));
        IPAdapterStart = T2IParamTypes.Register<double>(new("IP-Adapter Start", "When to start applying IP-Adapter, as a fraction of steps (if enabled).\nFor example, 0.25 starts applying a quarter (25%) of the way through. Must be less than IP-Adapter End.",
            "0", IgnoreIf: "0", Min: 0.0, Max: 1.0, Step: 0.05, FeatureFlag: "ipadapter", Group: T2IParamTypes.GroupImagePrompting, ViewType: ParamViewType.SLIDER, OrderPriority: 17, IsAdvanced: true, Examples: ["0", "0.2", "0.5"], DependNonDefault: UseIPAdapterForRevision.Type.ID
            ));
        IPAdapterEnd = T2IParamTypes.Register<double>(new("IP-Adapter End", "When to stop applying IP-Adapter, as a fraction of steps (if enabled).\nFor example, 0.5 stops applying halfway (50%) through. Must be greater than IP-Adapter Start.",
            "1", IgnoreIf: "1", Min: 0.0, Max: 1.0, Step: 0.05, FeatureFlag: "ipadapter", Group: T2IParamTypes.GroupImagePrompting, ViewType: ParamViewType.SLIDER, OrderPriority: 18, IsAdvanced: true, Examples: ["1", "0.8", "0.5"], DependNonDefault: UseIPAdapterForRevision.Type.ID
            ));
        IPAdapterWeightType = T2IParamTypes.Register<string>(new("IP-Adapter Weight Type", "How to shift the weighting of the IP-Adapter.\nThis can produce subtle but useful different effects.",
            "standard", FeatureFlag: "ipadapter", Group: T2IParamTypes.GroupImagePrompting, ViewType: ParamViewType.SLIDER, OrderPriority: 19, IsAdvanced: true, GetValues: _ => IPAdapterWeightTypes, DependNonDefault: UseIPAdapterForRevision.Type.ID
            ));
        EnableReferenceLatents = T2IParamTypes.Register<string>(new("Enable Reference Latents", "How to feed prompt images as Reference Latents to the model.\nNone leaves images on the text encoder only (correct for the Krea 2 base model).\nIndex Timestep Zero is for Ostris-style edit LoRAs.\nIndex is for Identity Edit LoRAs.",
            "none", IgnoreIf: "none", FeatureFlag: "optional_reference_latent", Group: T2IParamTypes.GroupImagePrompting, OrderPriority: 13, GetValues: _ => ["none///None (Text Encoder Only)", "index_timestep_zero///Index Timestep Zero (Ostris)", "index///Index (Identity Edit)"]
            ));
        TextEncodedImage = T2IParamTypes.Register<string>(new("Text Encoded Image", "How to feed prompt images into the text encoder.\nAutomatic uses the model's default (usually this is large or exact-size-as-input).\nNone skips text-encoder images (reference latents can still apply).\nSmall targets 384px, Large targets 1024px.",
            "auto", IgnoreIf: "auto", Group: T2IParamTypes.GroupImagePrompting, OrderPriority: 13.5, GetValues: _ => ["auto///Automatic", "none///None (Do Not Encode)", "small///Small Image", "large///Large Image"]
            ));
        UseStyleModel = T2IParamTypes.Register<string>(new("Use Style Model", $"Select a Style model to use it for image-prompt input handling.\nFlux.1 Redux is an example of a style model.\nPlace these models in `(Swarm)/Models/style_models`.",
            "None", IgnoreIf: "None", GetValues: _ => StyleModels, Group: T2IParamTypes.GroupImagePrompting, OrderPriority: 14, ChangeWeight: 1, FeatureFlag: "flux-dev"
            ));
        StyleModelMergeStrength = T2IParamTypes.Register<double>(new("Style Model Merge Strength", "How strongly to merge in the effects of the style model.\nAt 1, the style model is fully used.\nAt 0, the style model is ignored.\nFor Flux Redux, very low values (eg 0.1) are recommended.",
            "1", IgnoreIf: "1", Min: 0.0, Max: 1.0, Step: 0.01, FeatureFlag: "comfyui", Group: T2IParamTypes.GroupImagePrompting, ViewType: ParamViewType.SLIDER, OrderPriority: 14.5, IsAdvanced: true, Examples: ["0", "0.25", "0.5", "0.75", "1"], DependNonDefault: UseStyleModel.Type.ID
            ));
        StyleModelMultiplyStrength = T2IParamTypes.Register<double>(new("Style Model Multiply Strength", "How strongly to multiply the effects of the style model.\nAt 1, the style model is fully used.\nAt 0, the style model is ignored.\nFor Flux Redux, very low values (eg 0.1) are recommended.",
            "1", IgnoreIf: "1", Min: 0.0, Max: 10.0, ViewMax: 2, Step: 0.01, FeatureFlag: "comfyui", Group: T2IParamTypes.GroupImagePrompting, ViewType: ParamViewType.SLIDER, OrderPriority: 14.6, IsAdvanced: true, Examples: ["0", "0.25", "0.5", "0.75", "1", "2"], DependNonDefault: UseStyleModel.Type.ID
            ));
        StyleModelApplyStart = T2IParamTypes.Register<double>(new("Style Model Apply Start", "When to start applying the Style Model, as a fraction of steps (if enabled).\nFor example, 0.25 starts applying a quarter (25%) of the way through.\nThis is probably off-scale due to scheduler behavior in ComfyUI internals. Very low values are recommend for practical usage.",
            "0", IgnoreIf: "0", Min: 0.0, Max: 1.0, Step: 0.01, FeatureFlag: "comfyui", Group: T2IParamTypes.GroupImagePrompting, ViewType: ParamViewType.SLIDER, OrderPriority: 14.7, IsAdvanced: true, Examples: ["0", "0.2", "0.5"], DependNonDefault: UseStyleModel.Type.ID
            ));
        ComfyAdvancedGroup = new("ComfyUI Advanced", Toggles: false, IsAdvanced: true, Open: false);
        CustomWorkflowParam = T2IParamTypes.Register<string>(new("ComfyUI Custom Workflow", "What custom workflow to use in ComfyUI (built in the Comfy Workflow Editor tab).\nGenerally, do not use this directly.",
            "", Toggleable: true, FeatureFlag: "comfyui", Group: T2IParamTypes.GroupSwarmInternal, IsAdvanced: true, ValidateValues: false, ChangeWeight: 8, Permission: PermStoredCustomWorkflows,
            GetValues: (_) => [.. ComfyWorkflowStore.GetWorkflowNames()],
            Clean: (_, val) => ComfyWorkflowStore.TryGetWorkflowParameterPrompt(val, out string prompt) ? $"PARSED%{val}%{prompt}" : val,
            MetadataFormat: v => v.StartsWith("PARSED%") ? v.After("%").Before("%") : v
            ));
        SamplerParam = T2IParamTypes.Register<string>(new("Sampler", "Sampler type (for ComfyUI backends).\nGenerally, 'Euler' is fine, but for SD1 and SDXL 'DPM++ 2M' is popular when paired with the 'Karras' scheduler.\n'Ancestral' and 'SDE' samplers only work with non-rectified models (eg SD1/SDXL) and randomly move over time.\nSome special model variants require specific Samplers or Schedulers.\n'CFG++' samplers have a different CFG range than normal (between 0 to 2, depending).",
            "euler", Toggleable: true, FeatureFlag: "comfyui", Group: T2IParamTypes.GroupSampling, OrderPriority: -5, CanSectionalize: true, GetValues: (_) => Samplers
            ));
        SchedulerParam = T2IParamTypes.Register<string>(new("Scheduler", "Scheduler type (for ComfyUI backends).\nGoes with the Sampler parameter above.",
            "normal", Toggleable: true, FeatureFlag: "comfyui", Group: T2IParamTypes.GroupSampling, OrderPriority: -4, CanSectionalize: true, GetValues: (_) => Schedulers
            ));
        DetailDaemonGroup = new("Detail Daemon", Toggles: true, Open: false, IsAdvanced: true, OrderPriority: 11.5, Description: "Native SwarmKSampler support for Detail Daemon sigma adjustment. Based on Jonseed's ComfyUI-Detail-Daemon, from muerrilla's original Detail Daemon concept.");
        DetailDaemonAmount = T2IParamTypes.Register<double>(new("[DD] Detail Amount", "[Detail Daemon] Main detail adjustment amount. Positive values lower sigmas during the selected portion of sampling, generally increasing detail.",
            "0.1", Min: -2, Max: 2, Step: 0.01, Group: DetailDaemonGroup, FeatureFlag: "comfyui", ViewType: ParamViewType.SLIDER, OrderPriority: 1, Examples: ["0", "0.1", "0.25", "0.5", "1"]
            ));
        DetailDaemonStart = T2IParamTypes.Register<double>(new("[DD] Start", "[Detail Daemon] Fraction of sampling progress where adjustment starts.",
            "0.2", Min: 0, Max: 1, Step: 0.01, Group: DetailDaemonGroup, FeatureFlag: "comfyui", ViewType: ParamViewType.SLIDER, OrderPriority: 2
            ));
        DetailDaemonEnd = T2IParamTypes.Register<double>(new("[DD] End", "[Detail Daemon] Fraction of sampling progress where adjustment ends.",
            "0.8", Min: 0, Max: 1, Step: 0.01, Group: DetailDaemonGroup, FeatureFlag: "comfyui", ViewType: ParamViewType.SLIDER, OrderPriority: 3
            ));
        DetailDaemonBias = T2IParamTypes.Register<double>(new("[DD] Bias", "[Detail Daemon] Moves the peak adjustment earlier or later between Start and End.",
            "0.5", Min: 0, Max: 1, Step: 0.01, Group: DetailDaemonGroup, FeatureFlag: "comfyui", ViewType: ParamViewType.SLIDER, OrderPriority: 4
            ));
        DetailDaemonExponent = T2IParamTypes.Register<double>(new("[DD] Exponent", "[Detail Daemon] Changes the curve shape of the adjustment schedule.",
            "1", Min: 0, Max: 10, Step: 0.05, Group: DetailDaemonGroup, FeatureFlag: "comfyui", ViewType: ParamViewType.SLIDER, OrderPriority: 5
            ));
        DetailDaemonStartOffset = T2IParamTypes.Register<double>(new("[DD] Start Offset", "[Detail Daemon] Adjustment amount before Start. Usually leave at 0.",
            "0", Min: -1, Max: 1, Step: 0.01, Group: DetailDaemonGroup, FeatureFlag: "comfyui", ViewType: ParamViewType.SLIDER, OrderPriority: 6, IsAdvanced: true
            ));
        DetailDaemonEndOffset = T2IParamTypes.Register<double>(new("[DD] End Offset", "[Detail Daemon] Adjustment amount after End. Usually leave at 0.",
            "0", Min: -1, Max: 1, Step: 0.01, Group: DetailDaemonGroup, FeatureFlag: "comfyui", ViewType: ParamViewType.SLIDER, OrderPriority: 7, IsAdvanced: true
            ));
        DetailDaemonFade = T2IParamTypes.Register<double>(new("[DD] Fade", "[Detail Daemon] Reduces the full adjustment curve.",
            "0", Min: 0, Max: 1, Step: 0.05, Group: DetailDaemonGroup, FeatureFlag: "comfyui", ViewType: ParamViewType.SLIDER, OrderPriority: 8, IsAdvanced: true
            ));
        DetailDaemonSmooth = T2IParamTypes.Register<bool>(new("[DD] Smooth", "[Detail Daemon] Smooth the adjustment curve.",
            "true", Group: DetailDaemonGroup, FeatureFlag: "comfyui", OrderPriority: 9, IsAdvanced: true
            ));
        DetailDaemonCFGScaleOverride = T2IParamTypes.Register<double>(new("[DD] CFG Scale Override", "[Detail Daemon] If enabled and greater than 0, overrides the CFG scale used by Detail Daemon. Leave disabled or 0 to use the generation CFG scale.",
            "0", Min: 0, Max: 100, Step: 0.5, Group: DetailDaemonGroup, FeatureFlag: "comfyui", Toggleable: true, OrderPriority: 10, IsAdvanced: true
            ));
        AITemplateParam = T2IParamTypes.Register<bool>(new("Enable AITemplate", "If checked, enables AITemplate for ComfyUI generations (UNet only). Only compatible with some GPUs.",
            "false", IgnoreIf: "false", FeatureFlag: "aitemplate", Group: T2IParamTypes.GroupAlternateGuidance, ChangeWeight: 5
            ));
        PreferredDType = T2IParamTypes.Register<string>(new("Preferred DType", "Preferred data type for models, when a choice is available.\n(Notably primarily affects Flux.1 models currently).\nIf disabled, will automatically decide.\n'fp8_e43fn' is recommended for large models.\n'Default' uses global default type, usually fp16 or bf16.",
            "automatic", FeatureFlag: "comfyui", Group: T2IParamTypes.GroupAdvancedSampling, IsAdvanced: true, Toggleable: true, OrderPriority: 9, GetValues: (_) => ["automatic///Automatic (decide by model)", "default///Default (16 bit)", "fp8_e4m3fn///FP8 e4m3fn (8 bit)", "fp8_e5m2///FP8 e5m2 (alt 8 bit)"]
            ));
        SelfAttentionGuidanceScale = T2IParamTypes.Register<double>(new("Self-Attention Guidance Scale", "Scale for Self-Attention Guidance.\n''Self-Attention Guidance (SAG) uses the intermediate self-attention maps of diffusion models to enhance their stability and efficacy.\nSpecifically, SAG adversarially blurs only the regions that diffusion models attend to at each iteration and guides them accordingly.''\nDefaults to 0.5.\nThis is only expected to work on older unet-based models (eg SDXL) and not on newer models.",
            "0.5", Min: -2, Max: 5, Step: 0.1, FeatureFlag: "comfyui", Group: T2IParamTypes.GroupAlternateGuidance, IsAdvanced: true, Toggleable: true, ViewType: ParamViewType.SLIDER, OrderPriority: 12
            ));
        SelfAttentionGuidanceSigmaBlur = T2IParamTypes.Register<double>(new("Self-Attention Guidance Sigma Blur", "Blur-sigma for Self-Attention Guidance.\nDefaults to 2.0.",
            "2", Min: 0, Max: 10, Step: 0.25, FeatureFlag: "comfyui", Group: T2IParamTypes.GroupAlternateGuidance, IsAdvanced: true, Toggleable: true, ViewType: ParamViewType.SLIDER, OrderPriority: 12.5, DependNonDefault: SelfAttentionGuidanceScale.Type.ID
            ));
        PerturbedAttentionGuidanceScale = T2IParamTypes.Register<double>(new("Perturbed-Attention Guidance Scale", "Scale for Perturbed-Attention Guidance (PAG).\n''PAG is designed to progressively enhance the structure of synthesized samples throughout the denoising process by considering the self-attention mechanisms' ability to capture structural information.\nIt involves generating intermediate samples with degraded structure by substituting selected self-attention maps in diffusion U-Net with an identity matrix, and guiding the denoising process away from these degraded samples.''\nDefaults to 3.\nThis is only expected to work on older unet-based models (eg SDXL) and not on newer models.",
            "3", Min: 0, Max: 100, Step: 0.1, FeatureFlag: "comfyui", Group: T2IParamTypes.GroupAlternateGuidance, IsAdvanced: true, Toggleable: true, ViewType: ParamViewType.SLIDER, OrderPriority: 13
            ));
        RescaleCFGMultiplier = T2IParamTypes.Register<double>(new("Rescale CFG Multiplier", "If enabled, use Comfy's native version of RescaleCFG.\nThis is only expected to work on certain vpred models.\nThis is, generally, pointless.\nThe value specified is the multiplier rate.",
            "0.7", Min: 0, Max: 1, Step: 0.01, FeatureFlag: "comfyui", Group: T2IParamTypes.GroupAlternateGuidance, IsAdvanced: true, Toggleable: true, ViewType: ParamViewType.SLIDER, OrderPriority: 14
            ));
        RenormCFG = T2IParamTypes.Register<double>(new("Renorm CFG", "If enabled, use 'Renorm CFG', a technique developed for use with Lumina 2.\nAt 0, this does nothing. Lumina 2 reference code sets this to 1.\nThis parameter only works on some models, and will corrupt others.",
            "0", Min: 0, Max: 100, Step: 0.1, IgnoreIf: "0", ViewMax: 2, FeatureFlag: "comfyui", Group: T2IParamTypes.GroupAlternateGuidance, IsAdvanced: true, ViewType: ParamViewType.SLIDER, OrderPriority: 15
            ));
        UseCfgZeroStar = T2IParamTypes.Register<bool>(new("Use CFG Zero Star", "If enabled, use 'CFG Zero Star' (CFG-Zero*, defined <a target=\"_blank\" href=\"https://arxiv.org/abs/2503.18886\">in this paper</a>).\nThis may slightly improve quality on modern 'Flow' models when using CFG.",
            "false", IgnoreIf: "false", FeatureFlag: "comfyui", Group: T2IParamTypes.GroupAlternateGuidance, IsAdvanced: true, OrderPriority: 16
            ));
        UseTCFG = T2IParamTypes.Register<bool>(new("Use TCFG", "If enabled, use 'TCFG' (Tangential Damping Classifier-Free Guidance, defined <a target=\"_blank\" href=\"https://arxiv.org/abs/2503.18137\">in this paper</a>).\nThis may reduce CFG artifacts. Compatible with modern 'Flow' models.",
            "false", IgnoreIf: "false", FeatureFlag: "comfyui", Group: T2IParamTypes.GroupAlternateGuidance, IsAdvanced: true, OrderPriority: 17
            ));
        NormalizedAttentionGuidanceScale = T2IParamTypes.Register<double>(new("Normalized Attention Guidance Scale", "Scale for Normalized Attention Guidance, defined <a target=\"_blank\" href=\"https://arxiv.org/abs/2505.21179\">in this paper</a>).\nDesigned to when CFG Scale is set to 1 (CFG disabled), and gives back some negative prompting support.\n5 is a reasonable starter value for using this.\nDefaults to 0 (disabled).",
            "0", IgnoreIf: "0", Min: 0, Max: 50, Step: 1, FeatureFlag: "comfyui", Group: T2IParamTypes.GroupAlternateGuidance, IsAdvanced: true, ViewType: ParamViewType.SLIDER, OrderPriority: 18
            ));
        NormalizedAttentionGuidanceAlpha = T2IParamTypes.Register<double>(new("Normalized Attention Guidance Alpha", "Alpha value for Normalized Attention Guidance, aka blending scale.\nIn other words, how strongly to mix NAG with the base generation.\n1 means fully NAG, 0 means fully base, 0.5 means half-n-half. 0.5 is a safe default.",
            "0.5", Min: 0, Max: 1, Step: 0.01, FeatureFlag: "comfyui", Group: T2IParamTypes.GroupAlternateGuidance, IsAdvanced: true, Toggleable: true, ViewType: ParamViewType.SLIDER, OrderPriority: 18.1, DependNonDefault: NormalizedAttentionGuidanceScale.Type.ID
            ));
        NormalizedAttentionGuidanceTau = T2IParamTypes.Register<double>(new("Normalized Attention Guidance Tau", "Tau value for Normalized Attention Guidance.\nThis is a more internal value which modifies the guidance scaling.",
            "1.5", Min: 0.5, Max: 10, Step: 0.01, FeatureFlag: "comfyui", Group: T2IParamTypes.GroupAlternateGuidance, IsAdvanced: true, Toggleable: true, ViewType: ParamViewType.SLIDER, OrderPriority: 18.2, DependNonDefault: NormalizedAttentionGuidanceScale.Type.ID
            ));
        RefinerUpscaleMethod = T2IParamTypes.Register<string>(new("Refiner Upscale Method", "How to upscale the image, if upscaling is used.",
            "pixel-lanczos", Group: T2IParamTypes.GroupRefiners, OrderPriority: -1, FeatureFlag: "comfyui", ChangeWeight: 1,
            GetValues: (session) => [.. UpscalerModels, .. PidUpscaleModels(session)], DependNonDefault: T2IParamTypes.RefinerUpscale.Type.ID
            ));
        PixelDecoderModel = T2IParamTypes.Register<T2IModel>(new("Pixel Decoder Model", "Optionally use a PiD (Pixel Diffusion Decoder) model.",
            "", Toggleable: true, FeatureFlag: "comfyui", Group: T2IParamTypes.GroupAdvancedModelAddons, IsAdvanced: true, Subtype: "Stable-Diffusion", ChangeWeight: 4, DoNotPreview: true, OrderPriority: 14,
            GetValues: (session) => T2IParamTypes.CleanModelList(Program.MainSDModels.ListModelsFor(session).Where(m => m.ModelClass?.CompatClass?.ID == "pid").OrderBy(m => m.Name).Select(m => m.Name))
            ));
        RefinerSamplerParam = T2IParamTypes.Register<string>(new("Refiner Sampler", SamplerParam.Type.Description + "\nThis is an override to only affect the Refine/Upscale stage.",
            "euler", Toggleable: true, FeatureFlag: "comfyui", Group: T2IParamTypes.GroupRefinerOverrides, OrderPriority: -2,
            GetValues: (_) => Samplers
            ));
        RefinerSchedulerParam = T2IParamTypes.Register<string>(new("Refiner Scheduler", SchedulerParam.Type.Description + "\nThis is an override to only affect the Refine/Upscale stage.",
            "normal", Toggleable: true, FeatureFlag: "comfyui", Group: T2IParamTypes.GroupRefinerOverrides, OrderPriority: -1.5,
            GetValues: (_) => Schedulers
            ));
        for (int i = 0; i < 3; i++)
        {
            ControlNetPreprocessorParams[i] = T2IParamTypes.Register<string>(new($"ControlNet{T2IParamTypes.Controlnets[i].NameSuffix} Preprocessor", "The preprocessor to use on the ControlNet input image.\nIf toggled off, will be automatically selected.\nUse 'None' to disable preprocessing.",
                "None", Toggleable: true, FeatureFlag: "controlnet", Permission: Permissions.ParamControlNet, Group: T2IParamTypes.Controlnets[i].Group, OrderPriority: 3, GetValues: (_) => [.. ControlNetPreprocessors.Keys.Order().OrderBy(v => v == "None" ? -1 : 0)], ChangeWeight: 2
                ));
            ControlNetPreprocessorResolutionParams[i] = T2IParamTypes.Register<int>(new($"ControlNet{T2IParamTypes.Controlnets[i].NameSuffix} Preprocessor Resolution", "The resolution used by ControlNet preprocessors that support a resolution input. Lower values use less VRAM.",
                "1024", Min: 64, Max: 4096, Step: 64, FeatureFlag: "controlnet", Permission: Permissions.ParamControlNet, Group: T2IParamTypes.Controlnets[i].Group, ViewType: ParamViewType.SLIDER, OrderPriority: 3.1, ChangeWeight: 2
                ));
            ControlNetUnionTypeParams[i] = T2IParamTypes.Register<string>(new($"ControlNet{T2IParamTypes.Controlnets[i].NameSuffix} Union Type", "For Union ControlNets, you can optionally manually specify the union controlnet type.",
                "auto", Toggleable: true, IsAdvanced: true, FeatureFlag: "controlnet", Permission: Permissions.ParamControlNet, Group: T2IParamTypes.Controlnets[i].Group, OrderPriority: 4, GetValues: (_) => ControlnetUnionTypes
                ));
        }
        DebugRegionalPrompting = T2IParamTypes.Register<bool>(new("Debug Regional Prompting", "If checked, outputs masks from regional prompting for debug reasons.",
            "false", IgnoreIf: "false", FeatureFlag: "comfyui", VisibleNormally: false, Group: T2IParamTypes.GroupRegionalPrompting
            ));
        RegionalPromptingMethod = T2IParamTypes.Register<string>(new("Regional Prompting Method", "How to apply '<region:>' prompt syntax.\n'Standard' uses Swarm's masked conditioning behavior.\n'Attention Couple' patches cross-attention for supported models and may give stronger regional separation.",
            "Standard", IgnoreIf: "Standard", FeatureFlag: "comfyui", Group: T2IParamTypes.GroupRegionalPrompting, IsAdvanced: true, OrderPriority: -6,
            GetValues: (_) => ["Standard", "Attention Couple"]
            ));
        RefinerHyperTile = T2IParamTypes.Register<int>(new("Refiner HyperTile", "The size of hypertiles to use for the refining stage.\nHyperTile is a technique to speed up sampling of large images by tiling the image and batching the tiles.\nThis is useful when using SDv1 models as the refiner. SDXL-Base models do not benefit as much.",
            "256", Min: 64, Max: 2048, Step: 32, Toggleable: true, IsAdvanced: true, FeatureFlag: "comfyui,supports_hypertile", ViewType: ParamViewType.POT_SLIDER, Group: T2IParamTypes.GroupAdvancedSampling, OrderPriority: 20
            ));
        List<string> interpolators = ["RIFE", "FILM", "GIMM-VFI"];
        VideoPreviewType = T2IParamTypes.Register<string>(new("Video Preview Type", "How to display previews for generating videos.\n'Animate' shows a low-res animated video preview.\n'iterate' shows one frame at a time while it goes.\n'one' displays just the first frame.\n'none' disables previews.",
            "animate", IgnoreIf: "animate", FeatureFlag: "comfyui", Group: T2IParamTypes.GroupAdvancedVideo, Permission: Permissions.ParamVideo, IsAdvanced: true, GetValues: (_) => ["animate", "iterate", "one", "none"]
            ));
        VideoFrameInterpolationMultiplier = T2IParamTypes.Register<int>(new("Video Frame Interpolation Multiplier", "How many frames to interpolate between each frame in the video.\nHigher values are smoother, but make take significant time to save the output, and may have quality artifacts.",
            "1", IgnoreIf: "1", Min: 1, Max: 10, Step: 1, FeatureFlag: "frameinterps", Group: T2IParamTypes.GroupAdvancedVideo, Permission: Permissions.ParamVideo, OrderPriority: 32, IsAdvanced: true
            ));
        VideoFrameInterpolationMethod = T2IParamTypes.Register<string>(new("Video Frame Interpolation Method", "How to interpolate frames in the video.\n'RIFE' or 'FILM' are two different decent interpolation model options.",
            "RIFE", FeatureFlag: "frameinterps", Group: T2IParamTypes.GroupAdvancedVideo, Permission: Permissions.ParamVideo, GetValues: (_) => interpolators, OrderPriority: 33, DependNonDefault: VideoFrameInterpolationMultiplier.Type.ID, IsAdvanced: true
            ));
        GligenModel = T2IParamTypes.Register<string>(new("GLIGEN Model", "Optionally use a GLIGEN model.\nGLIGEN is only compatible with SDv1 at time of writing.",
            "None", IgnoreIf: "None", FeatureFlag: "comfyui", Group: T2IParamTypes.GroupRegionalPrompting, GetValues: (_) => GligenModels, IsAdvanced: true
            ));
        ShiftedLatentAverageInit = T2IParamTypes.Register<bool>(new("Shifted Latent Average Init", "If checked, shifts the empty latent to use a mean-average per-channel latent value (as calculated by Birchlabs).\nIf unchecked, default behavior of zero-init latents are used.\nThis can potentially improve the color range or even general quality on SDv1, SDv2, and SDXL models.\nNote that the effect is very minor.",
            "false", IgnoreIf: "false", FeatureFlag: "comfyui", Group: T2IParamTypes.GroupAdvancedSampling, IsAdvanced: true
            ));
        YoloModelInternal = T2IParamTypes.Register<string>(new("YOLO Model Internal", "Parameter for internally tracking YOLOv8 models.\nThis is not for real usage, it is just to expose the list to the UI handler.",
            "", IgnoreIf: "", FeatureFlag: "yolov8", Group: ComfyAdvancedGroup, GetValues: (_) => YoloModels, Toggleable: true, IsAdvanced: true, AlwaysRetain: true, VisibleNormally: false
            ));
        EasyCacheMode = T2IParamTypes.Register<string>(new("EasyCache Mode", "When to use EasyCache.\nEasyCache is a trick to accelerate diffusion models, especially video models.\nThat is: generation runs faster, but loses some quality.\nYou can leave this disabled, enabled for all model sampling stages, or only enabled for certain model sampling stages.\n(This separation is so eg you can accelerate your video generation, without losing quality of an initial image).",
            "disabled", IgnoreIf: "disabled", FeatureFlag: "comfyui", Group: T2IParamTypes.GroupAdvancedSampling, IsAdvanced: true, GetValues: (_) => ["disabled", "all", "base gen only///base gen only (no refiner or video)", "video only"], OrderPriority: 14
            ));
        EasyCacheThreshold = T2IParamTypes.Register<double>(new("EasyCache Threshold", "What threshold to use with EasyCache.\nSet to 0 to disable.\nHigher values skip more steps.",
            "0.2", Min: 0, Max: 1, Step: 0.05, FeatureFlag: "comfyui", Group: T2IParamTypes.GroupAdvancedSampling, IsAdvanced: true, ViewType: ParamViewType.SLIDER, OrderPriority: 14.5, DependNonDefault: EasyCacheMode.Type.ID
            ));
        EasyCacheStart = T2IParamTypes.Register<double>(new("EasyCache Start", "When to start applying EasyCache, as a fraction of steps (if enabled).\n0 or 0.15 is the recommended default for most models.",
            "0.15", IgnoreIf: "0", Min: 0, Max: 1, Step: 0.05, FeatureFlag: "comfyui", Group: T2IParamTypes.GroupAdvancedSampling, IsAdvanced: true, ViewType: ParamViewType.SLIDER, OrderPriority: 14.6, DependNonDefault: EasyCacheMode.Type.ID
            ));
        EasyCacheEnd = T2IParamTypes.Register<double>(new("EasyCache End", "When to stop applying EasyCache, as a fraction of steps (if enabled).\n1 or 0.95 is the recommended default for most models.",
            "0.95", IgnoreIf: "1", Min: 0, Max: 1, Step: 0.05, FeatureFlag: "comfyui", Group: T2IParamTypes.GroupAdvancedSampling, IsAdvanced: true, ViewType: ParamViewType.SLIDER, OrderPriority: 14.7, DependNonDefault: EasyCacheMode.Type.ID
            ));
        TeaCacheMode = T2IParamTypes.Register<string>(new("TeaCache Mode", "When to use TeaCache.\nTeaCache is a trick to accelerate diffusion models, especially video models.\nThat is: generation runs faster, but loses some quality.\nSee <a target=\"_blank\" href=\"https://liewfeng.github.io/TeaCache/\">here for more info</a>.\nYou can leave this disabled, enabled for all model sampling stages, or only enabled for certain model sampling stages.\n(This separation is so eg you can accelerate your video generation, without losing quality of an initial image).",
            "disabled", IgnoreIf: "disabled", FeatureFlag: "teacache", Group: T2IParamTypes.GroupAdvancedSampling, IsAdvanced: true, GetValues: (_) => ["disabled", "all", "base gen only///base gen only (no refiner or video)", "video only"], OrderPriority: 15
            ));
        TeaCacheThreshold = T2IParamTypes.Register<double>(new("TeaCache Threshold", "What threshold to use with TeaCache.\nSee 'TeaCache Mode' parameter above.\n0.4 might work well with Flux image generation, and 0.15 might work well with video generation.\n0.25 is a good stable default for most purposes - decent acceleration but little visual change.",
            "0.25", Min: 0, Max: 1, Step: 0.01, FeatureFlag: "teacache", Group: T2IParamTypes.GroupAdvancedSampling, IsAdvanced: true, ViewType: ParamViewType.SLIDER, OrderPriority: 15.5, DependNonDefault: TeaCacheMode.Type.ID
            ));
        TeaCacheStart = T2IParamTypes.Register<double>(new("TeaCache Start", "When to start applying TeaCache, as a fraction of steps (if enabled).\n0 is the recommended default for most models.\nComfy-TeaCache node pack author recommends a slightly higher setting for some models (eg 0.1 for some Wan variants and HiDream Full, never above 0.2).",
            "0", IgnoreIf: "0", Min: 0, Max: 1, Step: 0.01, FeatureFlag: "teacache", Group: T2IParamTypes.GroupAdvancedSampling, IsAdvanced: true, ViewType: ParamViewType.SLIDER, OrderPriority: 15.6, DependNonDefault: TeaCacheMode.Type.ID
            ));
        NunchakuCacheThreshold = T2IParamTypes.Register<double>(new("Nunchaku Cache Threshold", "What threshold to use with Nunchaku block caching.\nThis makes Nunchaku gens faster at the cost of quality.\nOnly applicable to Nunchaku models.\nGenerally 0 to 0.2 is the reasonable range, above that you can start noticing quality drop.",
            "0", IgnoreIf: "0", Min: 0, Max: 1, Step: 0.01, FeatureFlag: "nunchaku", Group: T2IParamTypes.GroupAdvancedSampling, IsAdvanced: true, ViewType: ParamViewType.SLIDER, OrderPriority: 16
            ));
        ModelAttentionBackend = T2IParamTypes.Register<string>(new("Model Attention Backend", "Override which attention implementation the model uses.\n'pytorch attention' is the standard default.\n'comfy kitchen attention' is a new sage-like attention impl from Comfy directly that has better performance, but may not work on all machines.",
            "pytorch attention", FeatureFlag: "model_attention_backend", Group: T2IParamTypes.GroupAdvancedModelAddons, IsAdvanced: true, Toggleable: true, GetValues: (_) => ModelAttentionBackends, OrderPriority: 41
            ));
        Anima38Qwen35Encoder = T2IParamTypes.Register<string>(new("Anima Qwen3.5 Encoder", "Select the Qwen3.5-4B semantic encoder for Anima 3.8B. Automatic selection prefers the best compatible encoder.",
            "auto", FeatureFlag: "anima-3_8b", Permission: Permissions.ModelParams, Group: T2IParamTypes.GroupAdvancedModelAddons, IsAdvanced: true, GetValues: (_) => Anima38Qwen35Encoders, OrderPriority: 35
            ));
        Anima38Adapter = T2IParamTypes.Register<string>(new("Anima 3.8B Adapter", "Select the progressive semantic adapter for Anima 3.8B. Automatic selection uses the compatible adapter when unambiguous.",
            "auto", FeatureFlag: "anima-3_8b", Permission: Permissions.ModelParams, Group: T2IParamTypes.GroupAdvancedModelAddons, IsAdvanced: true, GetValues: (_) => Anima38Adapters, OrderPriority: 36
            ));
        Anima38AdapterStrength = T2IParamTypes.Register<double>(new("Anima 3.8B Adapter Strength", "Controls how strongly the selected Anima 3.8B progressive semantic adapter affects conditioning.",
            "1", Min: 0, Max: 2, Step: 0.05, FeatureFlag: "anima-3_8b", Permission: Permissions.ModelParams, Group: T2IParamTypes.GroupAdvancedModelAddons, IsAdvanced: true, ViewType: ParamViewType.SLIDER, OrderPriority: 37
            ));
        UseSparseAttention = T2IParamTypes.Register<string>(new("Use Sparse Attention", "Apply block-sparse attention to speed up generation with large inputs (especially video model such as H3).\nSol-Attn (adaptive tau) (TODO: Explain this) a training-free adaptive threshold (good general default).\n'Top-K (SLA)' (TODO: Explain this)\n'VSA' is Video Sparse Attention, (TODO: Explain this) only for VSA trained models.",
            "None", IgnoreIf: "None", Group: T2IParamTypes.GroupAdvancedModelAddons, IsAdvanced: true, Toggleable: true, GetValues: (_) => ["None", "sol///Sol-Attn", "topk///Top-K (SLA)", "vsa///VSA"], OrderPriority: 42
            ));
        SetClipDevice = T2IParamTypes.Register<string>(new("Set CLIP Device", "Override the hardware device that text encoders run on.",
            "cpu", FeatureFlag: "set_clip_device", Group: T2IParamTypes.GroupAdvancedModelAddons, IsAdvanced: true, Toggleable: true, GetValues: (_) => SetClipDevices, OrderPriority: 70
            ));
        // ================================================ SeedVR ================================================
        GroupSeedVR = new T2IParamGroup("SeedVR", Toggles: true, Open: false, OrderPriority: -2.5, Description: "SeedVR2 is a one-step restoration model, run over the result of the normal generation.");
        SeedVRModel = T2IParamTypes.Register<T2IModel>(new("SeedVR Model", "Which SeedVR2 model to restore with.",
            "None", IgnoreIf: "None", FeatureFlag: "seedvr2", Group: GroupSeedVR, Subtype: "Stable-Diffusion", ChangeWeight: 9, DoNotPreview: true, OrderPriority: -10,
            GetValues: (session) => ["None", .. T2IParamTypes.CleanModelList(Program.MainSDModels.ListModelsFor(session).Where(m => m.ModelClass?.CompatClass?.ID == "seedvr2").OrderBy(m => m.Name).Select(m => m.Name))]
            ));
        SeedVRPreDownscale = T2IParamTypes.Register<double>(new("SeedVR Pre-Downscale", "Optional downscale of the image immediately before upscaling it back.\nSetting to '1' disables this behavior.\nPre-downscaling to degrade the image can help improve quality (reduces oversharpening).",
            "1", IgnoreIf: "1", Min: 0.05, Max: 1, Step: 0.05, OrderPriority: -9.5, ViewType: ParamViewType.SLIDER, FeatureFlag: "seedvr2", Group: GroupSeedVR, DoNotPreview: true, Examples: ["1", "0.5", "0.25"]
            ));
        SeedVRUpscale = T2IParamTypes.Register<double>(new("SeedVR Upscale", "Optional upscale of the image before SeedVR2 runs over it.\nSetting to '1' disables the upscale, and just restores at the current size.",
            "1", IgnoreIf: "1", Min: 0.25, Max: 8, ViewMax: 4, Step: 0.25, OrderPriority: -9, ViewType: ParamViewType.SLIDER, FeatureFlag: "seedvr2", Group: GroupSeedVR, DoNotPreview: true, Examples: ["1", "1.5", "2"]
            ));
        SeedVRUpscaleMethod = T2IParamTypes.Register<string>(new("SeedVR Upscale Method", "How to upscale the image before SeedVR2 runs over it, if upscaling is used.",
            "pixel-lanczos", OrderPriority: -8, FeatureFlag: "seedvr2", Group: GroupSeedVR, ChangeWeight: 1,
            GetValues: (session) => RefinerUpscaleMethod.Type.GetValues(session), DependNonDefault: SeedVRUpscale.Type.ID
            ));
        SeedVRColorCorrectionBehavior = T2IParamTypes.Register<string>(new("SeedVR Color Correction Behavior", "How to match the colors of a SeedVR2 restore back to the image it was given.\n'None' = Do not attempt color correction, only align the geometry.\n'CIELAB' = Transfer the color in CIELAB space, preserving detail.\n'Wavelet' = Transfer the low-frequency color, keeping the upscaled high-frequency detail.\n'AdaIN' = Match the per-channel mean and standard deviation.",
            "lab", FeatureFlag: "seedvr2", Group: GroupSeedVR, IsAdvanced: true, OrderPriority: 1, GetValues: (_) => ["none///None", "lab///CIELAB", "wavelet///Wavelet", "adain///AdaIN"]
            ));
        SeedVRSplitLatent = T2IParamTypes.Register<bool>(new("SeedVR Split Latent", "If enabled, samples a SeedVR2 video restore as chunks of frames instead of all at once, sized to fit in free VRAM.\nChunking reduces VRAM consumption.\nDoes nothing to a single image, or to a video that already fits.",
            "false", IgnoreIf: "false", FeatureFlag: "seedvr2,seedvr2_temporal_chunking", Group: GroupSeedVR, IsAdvanced: true, OrderPriority: 2
            ));
        SeedVRTemporalVideoOverlap = T2IParamTypes.Register<int>(new("SeedVR Temporal Video Overlap", "How many latent frames of overlap to keep between 'SeedVR Split Latent' chunks.\nHigher overlap hides the chunk seams better but takes longer.",
            "0", Min: 0, Max: 4096, Step: 1, IsAdvanced: true, FeatureFlag: "seedvr2,seedvr2_temporal_chunking", Group: GroupSeedVR, OrderPriority: 3, DependNonDefault: SeedVRSplitLatent.Type.ID
            ));
        T2IParamInput.RegisterFinalRequiredFlagsHandler(RecomputeBackendRoutingRequirementsHandler);
    }

    /// <summary>Recomputes dynamic feature requirements that depend on selected values or model families.</summary>
    /// <param name="input">The finalized generation input to prepare for backend routing.</param>
    private static void RecomputeBackendRoutingRequirements(T2IParamInput input)
    {
        input.RequiredFlags.RemoveWhere(flag => flag.StartsWith(ComfyCapabilityCatalog.ModelAttentionBackendValueFeaturePrefix, StringComparison.Ordinal));
        input.RequiredFlags.RemoveWhere(flag => flag.StartsWith(ComfyCapabilityCatalog.Anima38Qwen35ValueFeaturePrefix, StringComparison.Ordinal));
        input.RequiredFlags.RemoveWhere(flag => flag.StartsWith(ComfyCapabilityCatalog.Anima38AdapterValueFeaturePrefix, StringComparison.Ordinal));
        input.RequiredFlags.Remove(ComfyCapabilityCatalog.Anima38Qwen35AutoFeature);
        input.RequiredFlags.Remove(ComfyCapabilityCatalog.Anima38AdapterAutoFeature);
        input.RequiredFlags.Remove(ComfyCapabilityCatalog.EmptyMiniMaxH3LatentAVFeature);
        input.RequiredFlags.Remove(ComfyCapabilityCatalog.Anima38Qwen35NodeFeature);
        input.RequiredFlags.Remove(ComfyCapabilityCatalog.Anima38ConditioningNodeFeature);
        input.RequiredFlags.Remove(ComfyCapabilityCatalog.Anima38LoraLoaderNodeFeature);
        input.RequiredFlags.Remove(ComfyCapabilityCatalog.Anima38LoraLoaderModelOnlyNodeFeature);
        input.RequiredFlags.Remove(ComfyCapabilityCatalog.Anima38CreateHookLoraNodeFeature);
        if (input.TryGet(ModelAttentionBackend, out string attentionBackend))
        {
            input.RequiredFlags.Add(ComfyCapabilityCatalog.ModelAttentionBackendValueFeature(attentionBackend));
        }
        static bool isAnima38(T2IModel model)
        {
            return model?.ModelClass?.ID == "anima-3_8b";
        }
        static bool hasAnima38(T2IParamInput input, T2IRegisteredParam<T2IModel> param)
        {
            return input.TryGet(param, out T2IModel model) && isAnima38(model);
        }
        bool hasSectionalAnima38Negative = input.SectionParamOverrides.Values.Any(section =>
            section.TryGet(T2IParamTypes.NegativeModel, out T2IModel sectionalNegativeModel) && isAnima38(sectionalNegativeModel));
        bool hasAnyAnima38 = hasAnima38(input, T2IParamTypes.Model)
            || hasAnima38(input, T2IParamTypes.RefinerModel)
            || hasAnima38(input, T2IParamTypes.SegmentModel)
            || hasAnima38(input, T2IParamTypes.NegativeModel)
            || hasAnima38(input, T2IParamTypes.VideoModel)
            || hasAnima38(input, T2IParamTypes.VideoSwapModel)
            || hasAnima38(input, T2IParamTypes.VideoExtendModel)
            || hasAnima38(input, T2IParamTypes.VideoExtendSwapModel)
            || hasSectionalAnima38Negative;
        if (hasAnyAnima38)
        {
            input.RequiredFlags.Add(ComfyCapabilityCatalog.Anima38Qwen35NodeFeature);
            input.RequiredFlags.Add(ComfyCapabilityCatalog.Anima38ConditioningNodeFeature);
            string anima38Qwen35Encoder = input.Get(Anima38Qwen35Encoder, "auto");
            input.RequiredFlags.Add(anima38Qwen35Encoder == "auto"
                ? ComfyCapabilityCatalog.Anima38Qwen35AutoFeature
                : ComfyCapabilityCatalog.Anima38Qwen35ValueFeature(anima38Qwen35Encoder));
            string anima38Adapter = input.Get(Anima38Adapter, "auto");
            input.RequiredFlags.Add(anima38Adapter == "auto"
                ? ComfyCapabilityCatalog.Anima38AdapterAutoFeature
                : ComfyCapabilityCatalog.Anima38AdapterValueFeature(anima38Adapter));
        }
        WorkflowGenerator.Anima38LoraNodeRequirement animaLoraRequirements = WorkflowGenerator.GetRequiredAnima38LoraNodes(input);
        foreach (string feature in GetAnima38LoraCapabilityRequirements(animaLoraRequirements))
        {
            input.RequiredFlags.Add(feature);
        }
        static bool isMiniMaxH3(T2IParamInput input, T2IRegisteredParam<T2IModel> param)
        {
            return input.TryGet(param, out T2IModel model) && model?.ModelClass?.CompatClass?.ID == T2IModelClassSorter.CompatMiniMaxH3.ID;
        }
        if (isMiniMaxH3(input, T2IParamTypes.Model) || isMiniMaxH3(input, T2IParamTypes.VideoModel) || isMiniMaxH3(input, T2IParamTypes.VideoExtendModel))
        {
            input.RequiredFlags.Add(ComfyCapabilityCatalog.EmptyMiniMaxH3LatentAVFeature);
        }
    }

    /// <summary>Maps exact emitted Anima 3.8B LoRA bridge nodes to backend capability IDs.</summary>
    private static string[] GetAnima38LoraCapabilityRequirements(WorkflowGenerator.Anima38LoraNodeRequirement requirements)
    {
        List<string> features = [];
        if (requirements.HasFlag(WorkflowGenerator.Anima38LoraNodeRequirement.FullLoader))
        {
            features.Add(ComfyCapabilityCatalog.Anima38LoraLoaderNodeFeature);
        }
        if (requirements.HasFlag(WorkflowGenerator.Anima38LoraNodeRequirement.ModelOnlyLoader))
        {
            features.Add(ComfyCapabilityCatalog.Anima38LoraLoaderModelOnlyNodeFeature);
        }
        if (requirements.HasFlag(WorkflowGenerator.Anima38LoraNodeRequirement.HookLoader))
        {
            features.Add(ComfyCapabilityCatalog.Anima38CreateHookLoraNodeFeature);
        }
        return [.. features];
    }

    /// <summary>Registers backend types that must exist before saved backend entries can load.</summary>
    public void RegisterBackendTypes()
    {
        if (!Program.Backends.BackendTypes.TryGetValue("comfyui_api", out BackendApiType))
        {
            BackendApiType = Program.Backends.RegisterBackendType<ComfyUIAPIBackend>("comfyui_api", "ComfyUI API By URL", "A backend powered by a pre-existing installation of ComfyUI, referenced via API base URL.", true);
        }
        if (!Program.Backends.BackendTypes.TryGetValue("comfyui_selfstart", out BackendSelfStartType))
        {
            BackendSelfStartType = Program.Backends.RegisterBackendType<ComfyUISelfStartBackend>("comfyui_selfstart", "ComfyUI Self-Starting", "A backend powered by a pre-existing installation of the ComfyUI, automatically launched and managed by this UI server.", isStandard: true);
        }
        if (!BackendHandlersRegistered)
        {
            SwarmSwarmBackend.ValidityChecks[BackendApiType.ID] = (backend, input) => ComfyUIAPIAbstractBackend.TryIsValid(input, backend.ExtensionData.GetValueOrDefault("ComfyNodeTypes", null) as IReadOnlySet<string>);
            SwarmSwarmBackend.ValidityChecks[BackendSelfStartType.ID] = SwarmSwarmBackend.ValidityChecks[BackendApiType.ID];
            ComfyUIWebAPI.Register();
            AdminAPI.CheckForBackendUpdates.Add(CheckForUpdates);
            AdminAPI.DoBackendUpdates.Add(DoBackendUpdates);
            BackendHandlersRegistered = true;
        }
    }

    /// <summary>Enumerates all folders that have a ComfyUI install managed by swarm.</summary>
    public static IEnumerable<string> ComfyInstallDirs()
    {
        foreach (ComfyUISelfStartBackend backend in Program.Backends.EnumerateT2IBackends.Select(b => b.Backend as ComfyUISelfStartBackend).Where(b => b is not null).DistinctBy(b => b.Settings.StartScript))
        {
            string script = backend.Settings.StartScript;
            if (string.IsNullOrWhiteSpace(script))
            {
                continue;
            }
            yield return Path.GetFullPath(Directory.GetParent(script).FullName);
        }
    }

    public async Task CheckForUpdates(LockObject locker, JObject backendsData)
    {
        string[] folders = [.. ComfyInstallDirs()];
        // TODO: is a bunch of git fetches in parallel a rate limit issue? GitHub seems pretty chill about a `fetch` call (vs API is strict limits).
        List<Task> tasks = [];
        foreach (string folder in folders)
        {
            tasks.Add(Utilities.RunCheckedTask(async () =>
            {
                JObject updates = await AdminAPI.GetUpdatesDataFor(folder, true);
                if (updates is null)
                {
                    Logs.Debug($"Check for updates found no updates for ComfyUI install at {folder}");
                    return;
                }
                string altName = folders.Length == 1 ? "ComfyUI" : $"ComfyUI In Folder: {folder.Replace('\\', '/')}";
                lock (locker)
                {
                    backendsData[altName] = updates;
                }
                Logs.Debug($"Check for updates found {updates["count"]} updates for ComfyUI install at {folder}");
            }));
        }
        await Task.WhenAll(tasks); // Intentional force order for Comfy folders to be before nodes
        tasks = [];
        if (Directory.Exists($"{FilePath}DLNodes"))
        {
            foreach (string folder in Directory.EnumerateDirectories($"{FilePath}DLNodes"))
            {
                tasks.Add(Utilities.RunCheckedTask(async () =>
                {
                    if (!Directory.Exists($"{folder}/.git"))
                    {
                        return;
                    }
                    string nodeName = Path.GetFileName(folder);
                    string latestTarget = ComfyUISelfStartBackend.ComfyNodeGitPins.TryGetValue(nodeName, out string pinCommit) ? pinCommit : null;
                    JObject nodeUpdates = await AdminAPI.GetUpdatesDataFor(folder, true, latestTarget: latestTarget);
                    if (nodeUpdates is null)
                    {
                        Logs.Debug($"Check for updates found no updates for ComfyUI node at {folder}");
                        return;
                    }
                    lock (locker)
                    {
                        backendsData[$"Comfy Node: {nodeName}"] = nodeUpdates;
                    }
                    Logs.Debug($"Check for updates found {nodeUpdates["count"]} updates for ComfyUI node at {folder}");
                }));
            }
            await Task.WhenAll(tasks);
        }
    }

    public async Task DoBackendUpdates(Action didWork, Action<string> didFail, bool aggressive, string[] toUpdate)
    {
        string[] folders = [.. ComfyInstallDirs()];
        List<Task> tasks = [];
        foreach (string folder in folders)
        {
            string altName = folders.Length == 1 ? "ComfyUI" : $"ComfyUI In Folder: {folder.Replace('\\', '/')}";
            if (toUpdate.Contains(altName))
            {
                tasks.Add(Utilities.RunCheckedTask(async () =>
                {
                    await AdminAPI.DoGitUpdate(folder, aggressive, didWork, didFail);
                }));
            }
        }
        foreach (string folder in Directory.EnumerateDirectories($"{FilePath}DLNodes"))
        {
            string nodeName = Path.GetFileName(folder);
            if (toUpdate.Contains($"Comfy Node: {nodeName}"))
            {
                tasks.Add(Utilities.RunCheckedTask(async () =>
                {
                    if (!Directory.Exists($"{folder}/.git"))
                    {
                        return;
                    }
                    string headTarget = ComfyUISelfStartBackend.ComfyNodeGitPins.TryGetValue(nodeName, out string pinCommit) ? pinCommit : null;
                    await AdminAPI.DoGitUpdate(folder, aggressive, didWork, didFail, targetCommit: headTarget);
                }));
            }
        }
    }

    public BackendHandler.BackendType BackendApiType, BackendSelfStartType;

    public override void OnPreLaunch()
    {
        WebServer.WebApp.Map("/ComfyBackendDirect/{*Path}", ComfyUIRedirectHelper.ComfyBackendDirectHandler);
    }

    public record struct ComfyBackendData(HttpClient Client, string APIAddress, string WebAddress, AbstractT2IBackend Backend);

    public static IEnumerable<ComfyBackendData> ComfyBackendsDirect()
    {
        foreach (ComfyUIAPIAbstractBackend backend in RunningComfyBackends)
        {
            yield return new(ComfyUIAPIAbstractBackend.HttpClient, backend.APIAddress, backend.WebAddress, backend);
        }
        foreach (SwarmSwarmBackend swarmBackend in Program.Backends.RunningBackendsOfType<SwarmSwarmBackend>().Where(b => b.LinkedRemoteBackendType is not null && b.LinkedRemoteBackendType.StartsWith("comfyui_")))
        {
            string addr = $"{swarmBackend.Address}/ComfyBackendDirect";
            yield return new(SwarmSwarmBackend.HttpClient, addr, addr, swarmBackend);
        }
    }
}
