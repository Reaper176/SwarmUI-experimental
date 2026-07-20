# Comfy Node Contract Catalog Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Centralize every Swarm-maintained Comfy node class name and the existing node-to-feature behavior without changing runtime contracts or generated workflows.

**Architecture:** Add a flat `ComfyNodeNames` constant catalog containing the 65 maintained Python registration IDs, plus a `ComfyCapabilityCatalog` that constructs and applies the existing capability map. Preserve `ComfyUIBackendExtension.NodeToFeatureMap` as the same public mutable field, then migrate maintained C# consumers to constants in small commits.

**Tech Stack:** C# 12, .NET 8, Newtonsoft.Json workflow objects, Python `NODE_CLASS_MAPPINGS` as runtime authority, Git, and ripgrep static checks.

---

## Execution Constraints

- Work directly on the existing `master` checkout, per maintainer instruction; do not create a worktree.
- Preserve unrelated working-tree changes and never stage them.
- Do not modify Python, input-key strings, third-party node literals outside the existing capability map, or generated/user-data directories.
- Repository policy prohibits agent-run builds, tests, the live server, browser automation, and Comfy execution. Use the static checks below, then hand the manual validation matrix to the maintainer.
- Use explicit C# types, full braced blocks, and XML documentation in accordance with `AGENTS.md`.

## File Map

- Create `src/BuiltinExtensions/ComfyUIBackend/ComfyNodeNames.cs`: complete C# catalog of Swarm-maintained Comfy class names.
- Create `src/BuiltinExtensions/ComfyUIBackend/ComfyCapabilityCatalog.cs`: construction and application of the existing node-to-feature mapping.
- Modify `src/BuiltinExtensions/ComfyUIBackend/ComfyUIBackendExtension.cs`: retain the compatibility field, delegate capability application, and consume catalog constants.
- Modify `src/BuiltinExtensions/ComfyUIBackend/ComfyUIAPIAbstractBackend.cs`: catalog-backed model tracking and sampler detection.
- Modify `src/BuiltinExtensions/ComfyUIBackend/ComfyUIWebAPI.cs`: catalog-backed LoRA extraction workflow class type.
- Modify `src/BuiltinExtensions/ComfyUIBackend/WorkflowGenerator.cs`: catalog-backed core workflow node creation.
- Modify `src/BuiltinExtensions/ComfyUIBackend/WorkflowGeneratorSteps.cs`: catalog-backed workflow-step node creation and class matching.
- Modify `src/BuiltinExtensions/ComfyUIBackend/WorkflowGeneratorModelSupport.cs`: catalog-backed model/latent helper creation.
- Modify `src/BuiltinExtensions/ComfyUIBackend/WGNodeData.cs`: catalog-backed media/save helper creation.

### Task 1: Add the Complete Node Name Catalog

**Files:**
- Create: `src/BuiltinExtensions/ComfyUIBackend/ComfyNodeNames.cs`
- Reference: `src/BuiltinExtensions/ComfyUIBackend/ExtraNodes/**/*.py`

- [ ] **Step 1: Record the authoritative Python registration count**

Run:

```bash
rg -o '"Swarm[^"]+"[[:space:]]*:' src/BuiltinExtensions/ComfyUIBackend/ExtraNodes --glob '*.py' \
    | sed -E 's/.*"(Swarm[^"]+)"[[:space:]]*:/\1/' \
    | sort -u \
    | wc -l
```

Expected: `65`.

- [ ] **Step 2: Create the catalog with all documented constants**

Create `src/BuiltinExtensions/ComfyUIBackend/ComfyNodeNames.cs` with this complete value manifest. Retain one XML summary per constant; descriptions may be refined, but member names and values must remain exact.

```csharp
namespace SwarmUI.Builtin_ComfyUIBackend;

/// <summary>Canonical class names for Comfy nodes maintained by SwarmUI.</summary>
public static class ComfyNodeNames
{
    /// <summary>Comfy class name for the metadata-aware websocket save helper.</summary>
    public const string AddSaveMetadataWS = "SwarmAddSaveMetadataWS";
    /// <summary>Comfy class name for the AnimateDiff LLLite helper.</summary>
    public const string AnimaLLLite = "SwarmAnimaLLLite";
    /// <summary>Comfy class name for attention-coupled regional prompting.</summary>
    public const string AttentionCouple = "SwarmAttentionCouple";
    /// <summary>Comfy class name for overlap-mask cleaning.</summary>
    public const string CleanOverlapMasks = "SwarmCleanOverlapMasks";
    /// <summary>Comfy class name for overlap-mask cleaning that preserves the current mask.</summary>
    public const string CleanOverlapMasksExceptSelf = "SwarmCleanOverlapMasksExceptSelf";
    /// <summary>Comfy class name for CLIP segmentation.</summary>
    public const string ClipSeg = "SwarmClipSeg";
    /// <summary>Comfy class name for advanced CLIP text encoding.</summary>
    public const string ClipTextEncodeAdvanced = "SwarmClipTextEncodeAdvanced";
    /// <summary>Comfy class name for media frame counting.</summary>
    public const string CountFrames = "SwarmCountFrames";
    /// <summary>Comfy class name for audio debugging.</summary>
    public const string DebugAudio = "SwarmDebugAudio";
    /// <summary>Comfy class name for Detail Daemon options.</summary>
    public const string DetailDaemonOptions = "SwarmDetailDaemonOptions";
    /// <summary>Comfy class name for embedding list provision.</summary>
    public const string EmbedLoaderListProvider = "SwarmEmbedLoaderListProvider";
    /// <summary>Comfy class name for ensuring an audio stream exists.</summary>
    public const string EnsureAudio = "SwarmEnsureAudio";
    /// <summary>Comfy class name for excluding one mask from another.</summary>
    public const string ExcludeFromMask = "SwarmExcludeFromMask";
    /// <summary>Comfy class name for LoRA extraction.</summary>
    public const string ExtractLora = "SwarmExtractLora";
    /// <summary>Comfy class name for color-corrected masked image compositing.</summary>
    public const string ImageCompositeMaskedColorCorrecting = "SwarmImageCompositeMaskedColorCorrecting";
    /// <summary>Comfy class name for image cropping.</summary>
    public const string ImageCrop = "SwarmImageCrop";
    /// <summary>Comfy class name for reading image height.</summary>
    public const string ImageHeight = "SwarmImageHeight";
    /// <summary>Comfy class name for adding image noise.</summary>
    public const string ImageNoise = "SwarmImageNoise";
    /// <summary>Comfy class name for megapixel-based image scaling.</summary>
    public const string ImageScaleForMP = "SwarmImageScaleForMP";
    /// <summary>Comfy class name for reading image width.</summary>
    public const string ImageWidth = "SwarmImageWidth";
    /// <summary>Comfy class name for workflow audio input.</summary>
    public const string InputAudio = "SwarmInputAudio";
    /// <summary>Comfy class name for workflow Boolean input.</summary>
    public const string InputBoolean = "SwarmInputBoolean";
    /// <summary>Comfy class name for workflow checkpoint input.</summary>
    public const string InputCheckpoint = "SwarmInputCheckpoint";
    /// <summary>Comfy class name for workflow dropdown input.</summary>
    public const string InputDropdown = "SwarmInputDropdown";
    /// <summary>Comfy class name for workflow floating-point input.</summary>
    public const string InputFloat = "SwarmInputFloat";
    /// <summary>Comfy class name for workflow input grouping.</summary>
    public const string InputGroup = "SwarmInputGroup";
    /// <summary>Comfy class name for workflow image input.</summary>
    public const string InputImage = "SwarmInputImage";
    /// <summary>Comfy class name for workflow integer input.</summary>
    public const string InputInteger = "SwarmInputInteger";
    /// <summary>Comfy class name for workflow model-name input.</summary>
    public const string InputModelName = "SwarmInputModelName";
    /// <summary>Comfy class name for workflow text input.</summary>
    public const string InputText = "SwarmInputText";
    /// <summary>Comfy class name for workflow video input.</summary>
    public const string InputVideo = "SwarmInputVideo";
    /// <summary>Comfy class name for integer addition.</summary>
    public const string IntAdd = "SwarmIntAdd";
    /// <summary>Comfy class name for model-only loading.</summary>
    public const string JustLoadTheModelPlease = "SwarmJustLoadTheModelPlease";
    /// <summary>Comfy class name for Swarm sampling.</summary>
    public const string KSampler = "SwarmKSampler";
    /// <summary>Comfy class name for the LTX-Video audio VAE loader.</summary>
    public const string LTXVAudioVAELoader = "SwarmLTXVAudioVAELoader";
    /// <summary>Comfy class name for masked latent blending.</summary>
    public const string LatentBlendMasked = "SwarmLatentBlendMasked";
    /// <summary>Comfy class name for base64 audio loading.</summary>
    public const string LoadAudioB64 = "SwarmLoadAudioB64";
    /// <summary>Comfy class name for base64 image loading.</summary>
    public const string LoadImageB64 = "SwarmLoadImageB64";
    /// <summary>Comfy class name for base64 video loading.</summary>
    public const string LoadVideoB64 = "SwarmLoadVideoB64";
    /// <summary>Comfy class name for Swarm LoRA loading.</summary>
    public const string LoraLoader = "SwarmLoraLoader";
    /// <summary>Comfy class name for mask blurring.</summary>
    public const string MaskBlur = "SwarmMaskBlur";
    /// <summary>Comfy class name for reading mask bounds.</summary>
    public const string MaskBounds = "SwarmMaskBounds";
    /// <summary>Comfy class name for mask growth.</summary>
    public const string MaskGrow = "SwarmMaskGrow";
    /// <summary>Comfy class name for mask thresholding.</summary>
    public const string MaskThreshold = "SwarmMaskThreshold";
    /// <summary>Comfy class name for model tiling.</summary>
    public const string ModelTiling = "SwarmModelTiling";
    /// <summary>Comfy class name for offset empty latent images.</summary>
    public const string OffsetEmptyLatentImage = "SwarmOffsetEmptyLatentImage";
    /// <summary>Comfy class name for merging masks during overlap correction.</summary>
    public const string OverMergeMasksForOverlapFix = "SwarmOverMergeMasksForOverlapFix";
    /// <summary>Comfy class name for reference-only conditioning.</summary>
    public const string ReferenceOnly = "SwarmReferenceOnly";
    /// <summary>Comfy class name for background removal.</summary>
    public const string RemBg = "SwarmRemBg";
    /// <summary>Comfy class name for parsing SAM2 boxes from JSON.</summary>
    public const string Sam2BBoxFromJson = "SwarmSam2BBoxFromJson";
    /// <summary>Comfy class name for SAM2 mask post-processing.</summary>
    public const string Sam2MaskPostProcess = "SwarmSam2MaskPostProcess";
    /// <summary>Comfy class name for parsing SAM3 boxes from JSON.</summary>
    public const string Sam3BBoxFromJson = "SwarmSam3BBoxFromJson";
    /// <summary>Comfy class name for SAM3 mask post-processing.</summary>
    public const string Sam3MaskPostProcess = "SwarmSam3MaskPostProcess";
    /// <summary>Comfy class name for parsing SAM3 points from JSON.</summary>
    public const string Sam3PointsFromJson = "SwarmSam3PointsFromJson";
    /// <summary>Comfy class name for websocket animated WebP saving.</summary>
    public const string SaveAnimatedWebpWS = "SwarmSaveAnimatedWebpWS";
    /// <summary>Comfy class name for websocket animation saving.</summary>
    public const string SaveAnimationWS = "SwarmSaveAnimationWS";
    /// <summary>Comfy class name for websocket image saving.</summary>
    public const string SaveImageWS = "SwarmSaveImageWS";
    /// <summary>Comfy class name for creating percentage-based square masks.</summary>
    public const string SquareMaskFromPercent = "SwarmSquareMaskFromPercent";
    /// <summary>Comfy class name for tileable VAE behavior.</summary>
    public const string TileableVAE = "SwarmTileableVAE";
    /// <summary>Comfy class name for trimming media frames.</summary>
    public const string TrimFrames = "SwarmTrimFrames";
    /// <summary>Comfy class name for reverse sampling.</summary>
    public const string Unsampler = "SwarmUnsampler";
    /// <summary>Comfy class name for video boomerang generation.</summary>
    public const string VideoBoomerang = "SwarmVideoBoomerang";
    /// <summary>Comfy class name for video frame-rate resampling.</summary>
    public const string VideoResampleFPS = "SwarmVideoResampleFPS";
    /// <summary>Comfy class name for embedded workflow descriptions.</summary>
    public const string WorkflowDescription = "SwarmWorkflowDescription";
    /// <summary>Comfy class name for YOLO detection.</summary>
    public const string YoloDetection = "SwarmYoloDetection";
}
```

- [ ] **Step 3: Compare catalog values to Python registrations**

Run:

```bash
comm -3 \
    <(rg -o 'public const string [A-Za-z0-9]+ = "Swarm[^"]+";' src/BuiltinExtensions/ComfyUIBackend/ComfyNodeNames.cs \
        | sed -E 's/.*"(Swarm[^"]+)".*/\1/' | sort -u) \
    <(rg -o '"Swarm[^"]+"[[:space:]]*:' src/BuiltinExtensions/ComfyUIBackend/ExtraNodes --glob '*.py' \
        | sed -E 's/.*"(Swarm[^"]+)"[[:space:]]*:/\1/' | sort -u)
rg -c 'public const string [A-Za-z0-9]+ = "Swarm[^"]+";' src/BuiltinExtensions/ComfyUIBackend/ComfyNodeNames.cs
git diff --check -- src/BuiltinExtensions/ComfyUIBackend/ComfyNodeNames.cs
```

Expected: no comparison output, then `65`, then no whitespace-error output.

- [ ] **Step 4: Commit only the catalog**

```bash
git add -- src/BuiltinExtensions/ComfyUIBackend/ComfyNodeNames.cs
git diff --cached --name-status
git diff --cached --check
git commit -m "refactor: catalog maintained Comfy node names"
```

Expected staged scope before commit: only `A src/BuiltinExtensions/ComfyUIBackend/ComfyNodeNames.cs`.

### Task 2: Extract the Capability Catalog Without Breaking the Public Field

**Files:**
- Create: `src/BuiltinExtensions/ComfyUIBackend/ComfyCapabilityCatalog.cs`
- Modify: `src/BuiltinExtensions/ComfyUIBackend/ComfyUIBackendExtension.cs:44-74,516-525,567-592`

- [ ] **Step 1: Create the capability catalog with the exact existing map**

Create `src/BuiltinExtensions/ComfyUIBackend/ComfyCapabilityCatalog.cs`:

```csharp
namespace SwarmUI.Builtin_ComfyUIBackend;

/// <summary>Defines Comfy node capability mappings used by backend feature detection.</summary>
public static class ComfyCapabilityCatalog
{
    /// <summary>Creates a mutable copy of the default Comfy node-to-feature mapping.</summary>
    public static Dictionary<string, string> CreateNodeToFeatureMap()
    {
        return new()
        {
            [ComfyNodeNames.LoadImageB64] = "comfy_loadimage_b64",
            [ComfyNodeNames.SaveImageWS] = "comfy_saveimage_ws",
            [ComfyNodeNames.JustLoadTheModelPlease] = "comfy_just_load_model",
            [ComfyNodeNames.LatentBlendMasked] = "comfy_latent_blend_masked",
            [ComfyNodeNames.KSampler] = "variation_seed",
            ["FreeU"] = "freeu",
            ["AITemplateLoader"] = "aitemplate",
            ["IPAdapter"] = "ipadapter",
            ["IPAdapterApply"] = "ipadapter",
            ["IPAdapterModelLoader"] = "cubiqipadapter",
            ["IPAdapterUnifiedLoader"] = "cubiqipadapterunified",
            ["MiDaS-DepthMapPreprocessor"] = "controlnetpreprocessors",
            ["RIFE VFI"] = "frameinterps",
            ["GIMMVFI_interpolate"] = "frameinterps_gimmvfi",
            ["SAM3Segmentation"] = "sam3",
            ["SAM3Grounding"] = "sam3",
            [ComfyNodeNames.YoloDetection] = "yolov8",
            ["PixArtCheckpointLoader"] = "extramodelspixart",
            ["SanaCheckpointLoader"] = "extramodelssana",
            ["CheckpointLoaderNF4"] = "bnb_nf4",
            ["UnetLoaderGGUF"] = "gguf",
            ["NunchakuFluxDiTLoader"] = "nunchaku",
            ["TensorRTLoader"] = "tensorrt",
            ["TeaCache"] = "teacache",
            ["TeaCacheForVidGen"] = "teacache",
            ["TeaCacheForImgGen"] = "teacache_oldvers",
            ["OverrideCLIPDevice"] = "set_clip_device",
            ["INPAINT_LoadInpaintModel"] = "inpaintnodes",
            ["INPAINT_InpaintWithModel"] = "inpaintnodes"
        };
    }

    /// <summary>Applies the feature associated with an available Comfy node, if one is mapped.</summary>
    public static void ApplyDetectedNodeFeature(string nodeName, Dictionary<string, string> nodeToFeatureMap, HashSet<string> featuresSupported, HashSet<string> featuresDiscardIfNotFound)
    {
        if (nodeToFeatureMap.TryGetValue(nodeName, out string featureId))
        {
            featuresSupported.Add(featureId);
            featuresDiscardIfNotFound.Remove(featureId);
        }
    }
}
```

- [ ] **Step 2: Preserve the public compatibility field while delegating construction**

In `ComfyUIBackendExtension.cs`, replace only the initializer block with:

```csharp
/// <summary>Extensible map of ComfyUI Node IDs to supported feature IDs.</summary>
public static Dictionary<string, string> NodeToFeatureMap = ComfyCapabilityCatalog.CreateNodeToFeatureMap();
```

Do not change the field to a property, add `readonly`, rename it, or move its declaration.

- [ ] **Step 3: Delegate feature application at the existing loop position**

In the existing `foreach ((string key, JToken data) in rawObjectInfo)` loop, replace only the inline `NodeToFeatureMap.TryGetValue` block with:

```csharp
ComfyCapabilityCatalog.ApplyDetectedNodeFeature(key, NodeToFeatureMap, FeaturesSupported, FeaturesDiscardIfNotFound);
```

Keep the call after preprocessor classification and before `DetectHookLoraSchedulingSupport(rawObjectInfo)`.

- [ ] **Step 4: Migrate the three object-info node-name lookups**

Make these exact expression replacements in `ComfyUIBackendExtension.cs`, leaving input-key literals unchanged:

```text
"SwarmKSampler"      -> ComfyNodeNames.KSampler
"SwarmYoloDetection" -> ComfyNodeNames.YoloDetection
```

There are two `SwarmKSampler` lookup occurrences and one `SwarmYoloDetection` lookup occurrence.

- [ ] **Step 5: Verify mapping and compatibility structure statically**

Run:

```bash
rg -n 'public static Dictionary<string, string> NodeToFeatureMap = ComfyCapabilityCatalog\.CreateNodeToFeatureMap\(\);' src/BuiltinExtensions/ComfyUIBackend/ComfyUIBackendExtension.cs
rg -n 'ApplyDetectedNodeFeature\(key, NodeToFeatureMap, FeaturesSupported, FeaturesDiscardIfNotFound\);' src/BuiltinExtensions/ComfyUIBackend/ComfyUIBackendExtension.cs
rg -c '^            \[(ComfyNodeNames\.[A-Za-z0-9]+|"[^"]+")\] = "[^"]+",?$' src/BuiltinExtensions/ComfyUIBackend/ComfyCapabilityCatalog.cs
git diff --check -- src/BuiltinExtensions/ComfyUIBackend/ComfyCapabilityCatalog.cs src/BuiltinExtensions/ComfyUIBackend/ComfyUIBackendExtension.cs
```

Expected: one field declaration, one application call, mapping count `29`, and no whitespace errors. Manually compare all 29 pairs with Step 1.

- [ ] **Step 6: Commit the capability extraction**

```bash
git add -- src/BuiltinExtensions/ComfyUIBackend/ComfyCapabilityCatalog.cs src/BuiltinExtensions/ComfyUIBackend/ComfyUIBackendExtension.cs
git diff --cached --name-status
git diff --cached --check
git commit -m "refactor: centralize Comfy capability mappings"
```

Expected staged scope: exactly the two listed files.

### Task 3: Migrate Backend and Web API Consumers

**Files:**
- Modify: `src/BuiltinExtensions/ComfyUIBackend/ComfyUIAPIAbstractBackend.cs:97-120`
- Modify: `src/BuiltinExtensions/ComfyUIBackend/ComfyUIWebAPI.cs:610-616`

- [ ] **Step 1: Replace backend model-tracker and sampler literals**

```text
ComfyUIAPIAbstractBackend.cs
"SwarmAnimaLLLite"             -> ComfyNodeNames.AnimaLLLite
"SwarmEmbedLoaderListProvider" -> ComfyNodeNames.EmbedLoaderListProvider
"SwarmKSampler"                -> ComfyNodeNames.KSampler
```

Do not change tracker category names, input keys, conditionals, or feature IDs.

- [ ] **Step 2: Replace the direct LoRA-extraction class type**

In `ComfyUIWebAPI.cs`, change only the `class_type` value:

```csharp
["class_type"] = ComfyNodeNames.ExtractLora,
```

Do not change the surrounding `JObject`, inputs, node ID, or response handling.

- [ ] **Step 3: Verify this consumer group**

Run:

```bash
rg -n '"Swarm(AnimaLLLite|EmbedLoaderListProvider|KSampler|ExtractLora)"' src/BuiltinExtensions/ComfyUIBackend/ComfyUIAPIAbstractBackend.cs src/BuiltinExtensions/ComfyUIBackend/ComfyUIWebAPI.cs
rg -n 'ComfyNodeNames\.(AnimaLLLite|EmbedLoaderListProvider|KSampler|ExtractLora)' src/BuiltinExtensions/ComfyUIBackend/ComfyUIAPIAbstractBackend.cs src/BuiltinExtensions/ComfyUIBackend/ComfyUIWebAPI.cs
git diff --check -- src/BuiltinExtensions/ComfyUIBackend/ComfyUIAPIAbstractBackend.cs src/BuiltinExtensions/ComfyUIBackend/ComfyUIWebAPI.cs
```

Expected: the first command has no output; the second shows four migrated references; the whitespace check is silent.

- [ ] **Step 4: Commit the backend/Web API migration**

```bash
git add -- src/BuiltinExtensions/ComfyUIBackend/ComfyUIAPIAbstractBackend.cs src/BuiltinExtensions/ComfyUIBackend/ComfyUIWebAPI.cs
git diff --cached --name-status
git diff --cached --check
git commit -m "refactor: use Comfy node catalog in backend APIs"
```

Expected staged scope: exactly the two listed files.

### Task 4: Migrate Model-Support and Node-Data Consumers

**Files:**
- Modify: `src/BuiltinExtensions/ComfyUIBackend/WorkflowGeneratorModelSupport.cs:460,547`
- Modify: `src/BuiltinExtensions/ComfyUIBackend/WGNodeData.cs:322-527`

- [ ] **Step 1: Replace all model-support literals**

```text
WorkflowGeneratorModelSupport.cs
"SwarmOffsetEmptyLatentImage" -> ComfyNodeNames.OffsetEmptyLatentImage
"SwarmLTXVAudioVAELoader"     -> ComfyNodeNames.LTXVAudioVAELoader
```

- [ ] **Step 2: Replace all node-data class literals**

```text
WGNodeData.cs
"SwarmCountFrames"     -> ComfyNodeNames.CountFrames
"SwarmEnsureAudio"     -> ComfyNodeNames.EnsureAudio
"SwarmSaveImageWS"     -> ComfyNodeNames.SaveImageWS
"SwarmVideoBoomerang"  -> ComfyNodeNames.VideoBoomerang
"SwarmSaveAnimationWS" -> ComfyNodeNames.SaveAnimationWS
```

Do not alter `SwarmReadableErrorException`, the existing save-audio follow-up comment, filename prefixes, inputs, or output references.

- [ ] **Step 3: Verify this consumer group**

Run:

```bash
rg -n '"Swarm(OffsetEmptyLatentImage|LTXVAudioVAELoader|CountFrames|EnsureAudio|SaveImageWS|VideoBoomerang|SaveAnimationWS)"' src/BuiltinExtensions/ComfyUIBackend/WorkflowGeneratorModelSupport.cs src/BuiltinExtensions/ComfyUIBackend/WGNodeData.cs
rg -n 'ComfyNodeNames\.(OffsetEmptyLatentImage|LTXVAudioVAELoader|CountFrames|EnsureAudio|SaveImageWS|VideoBoomerang|SaveAnimationWS)' src/BuiltinExtensions/ComfyUIBackend/WorkflowGeneratorModelSupport.cs src/BuiltinExtensions/ComfyUIBackend/WGNodeData.cs
git diff --check -- src/BuiltinExtensions/ComfyUIBackend/WorkflowGeneratorModelSupport.cs src/BuiltinExtensions/ComfyUIBackend/WGNodeData.cs
```

Expected: the first command has no output; the second shows every migrated occurrence; the whitespace check is silent.

- [ ] **Step 4: Commit the model/media migration**

```bash
git add -- src/BuiltinExtensions/ComfyUIBackend/WorkflowGeneratorModelSupport.cs src/BuiltinExtensions/ComfyUIBackend/WGNodeData.cs
git diff --cached --name-status
git diff --cached --check
git commit -m "refactor: catalog Comfy model and media helpers"
```

Expected staged scope: exactly the two listed files.

### Task 5: Migrate Core Workflow Generator Consumers

**Files:**
- Modify: `src/BuiltinExtensions/ComfyUIBackend/WorkflowGenerator.cs`

- [ ] **Step 1: Replace every maintained node literal using the exhaustive manifest**

Replace every occurrence, not just the first occurrence, of these values:

```text
"SwarmAttentionCouple"                     -> ComfyNodeNames.AttentionCouple
"SwarmCleanOverlapMasksExceptSelf"         -> ComfyNodeNames.CleanOverlapMasksExceptSelf
"SwarmClipTextEncodeAdvanced"              -> ComfyNodeNames.ClipTextEncodeAdvanced
"SwarmDetailDaemonOptions"                 -> ComfyNodeNames.DetailDaemonOptions
"SwarmExcludeFromMask"                     -> ComfyNodeNames.ExcludeFromMask
"SwarmImageCompositeMaskedColorCorrecting" -> ComfyNodeNames.ImageCompositeMaskedColorCorrecting
"SwarmImageCrop"                           -> ComfyNodeNames.ImageCrop
"SwarmImageScaleForMP"                     -> ComfyNodeNames.ImageScaleForMP
"SwarmKSampler"                            -> ComfyNodeNames.KSampler
"SwarmLoadAudioB64"                        -> ComfyNodeNames.LoadAudioB64
"SwarmLoadImageB64"                        -> ComfyNodeNames.LoadImageB64
"SwarmLoadVideoB64"                        -> ComfyNodeNames.LoadVideoB64
"SwarmMaskBounds"                          -> ComfyNodeNames.MaskBounds
"SwarmMaskThreshold"                       -> ComfyNodeNames.MaskThreshold
"SwarmOverMergeMasksForOverlapFix"         -> ComfyNodeNames.OverMergeMasksForOverlapFix
"SwarmSquareMaskFromPercent"               -> ComfyNodeNames.SquareMaskFromPercent
"SwarmTrimFrames"                          -> ComfyNodeNames.TrimFrames
```

Only replace node-class string expressions. Preserve `CreateNode` IDs, input objects and order, output indexes, sampling conditions, feature gates, and `SwarmReadableErrorException` references.

- [ ] **Step 2: Audit every changed call site in context**

Run:

```bash
git diff --word-diff=plain -- src/BuiltinExtensions/ComfyUIBackend/WorkflowGenerator.cs
```

Expected: each changed expression removes a quoted `Swarm*` node value and adds the corresponding `ComfyNodeNames.*` member. No input key, node ID, condition, or exception change appears.

- [ ] **Step 3: Verify no maintained node literal remains in this file**

Run:

```bash
rg -n '"Swarm[A-Za-z0-9]+"' src/BuiltinExtensions/ComfyUIBackend/WorkflowGenerator.cs
git diff --check -- src/BuiltinExtensions/ComfyUIBackend/WorkflowGenerator.cs
```

Expected: the literal scan has no output; the whitespace check is silent. Unquoted exception type references are intentionally unaffected.

- [ ] **Step 4: Commit the core generator migration**

```bash
git add -- src/BuiltinExtensions/ComfyUIBackend/WorkflowGenerator.cs
git diff --cached --name-status
git diff --cached --check
git commit -m "refactor: catalog core workflow node names"
```

Expected staged scope: only `M src/BuiltinExtensions/ComfyUIBackend/WorkflowGenerator.cs`.

### Task 6: Migrate Workflow Step Consumers

**Files:**
- Modify: `src/BuiltinExtensions/ComfyUIBackend/WorkflowGeneratorSteps.cs`

- [ ] **Step 1: Replace every maintained node literal using the exhaustive manifest**

Replace every occurrence of:

```text
"SwarmAnimaLLLite"            -> ComfyNodeNames.AnimaLLLite
"SwarmClipSeg"                -> ComfyNodeNames.ClipSeg
"SwarmClipTextEncodeAdvanced" -> ComfyNodeNames.ClipTextEncodeAdvanced
"SwarmCountFrames"            -> ComfyNodeNames.CountFrames
"SwarmImageHeight"            -> ComfyNodeNames.ImageHeight
"SwarmImageNoise"             -> ComfyNodeNames.ImageNoise
"SwarmImageWidth"             -> ComfyNodeNames.ImageWidth
"SwarmIntAdd"                 -> ComfyNodeNames.IntAdd
"SwarmJustLoadTheModelPlease" -> ComfyNodeNames.JustLoadTheModelPlease
"SwarmKSampler"               -> ComfyNodeNames.KSampler
"SwarmLatentBlendMasked"      -> ComfyNodeNames.LatentBlendMasked
"SwarmMaskBlur"               -> ComfyNodeNames.MaskBlur
"SwarmMaskGrow"               -> ComfyNodeNames.MaskGrow
"SwarmMaskThreshold"          -> ComfyNodeNames.MaskThreshold
"SwarmModelTiling"            -> ComfyNodeNames.ModelTiling
"SwarmReferenceOnly"          -> ComfyNodeNames.ReferenceOnly
"SwarmRemBg"                  -> ComfyNodeNames.RemBg
"SwarmSam3BBoxFromJson"       -> ComfyNodeNames.Sam3BBoxFromJson
"SwarmSam3MaskPostProcess"    -> ComfyNodeNames.Sam3MaskPostProcess
"SwarmSam3PointsFromJson"     -> ComfyNodeNames.Sam3PointsFromJson
"SwarmTileableVAE"            -> ComfyNodeNames.TileableVAE
"SwarmTrimFrames"             -> ComfyNodeNames.TrimFrames
"SwarmUnsampler"              -> ComfyNodeNames.Unsampler
"SwarmVideoResampleFPS"       -> ComfyNodeNames.VideoResampleFPS
"SwarmYoloDetection"          -> ComfyNodeNames.YoloDetection
```

This includes `CreateNode` calls, `RunOnNodesOfClass`, and static class-name collections. Preserve all inputs, priorities, feature conditions, and collection order.

- [ ] **Step 2: Audit every changed call site in context**

Run:

```bash
git diff --word-diff=plain -- src/BuiltinExtensions/ComfyUIBackend/WorkflowGeneratorSteps.cs
```

Expected: expression-only node-name replacements. Pay particular attention to the `RunOnNodesOfClass` call and class-name collection near the end; both must retain their original values and ordering through constants.

- [ ] **Step 3: Verify no maintained node literal remains in this file**

Run:

```bash
rg -n '"Swarm[A-Za-z0-9]+"' src/BuiltinExtensions/ComfyUIBackend/WorkflowGeneratorSteps.cs
git diff --check -- src/BuiltinExtensions/ComfyUIBackend/WorkflowGeneratorSteps.cs
```

Expected: the literal scan has no output; the whitespace check is silent.

- [ ] **Step 4: Commit the workflow-step migration**

```bash
git add -- src/BuiltinExtensions/ComfyUIBackend/WorkflowGeneratorSteps.cs
git diff --cached --name-status
git diff --cached --check
git commit -m "refactor: catalog workflow step node names"
```

Expected staged scope: only `M src/BuiltinExtensions/ComfyUIBackend/WorkflowGeneratorSteps.cs`.

### Task 7: Perform the Whole-Surface Contract Audit

**Files:**
- Verify: `src/BuiltinExtensions/ComfyUIBackend/ComfyNodeNames.cs`
- Verify: `src/BuiltinExtensions/ComfyUIBackend/ComfyCapabilityCatalog.cs`
- Verify: all modified C# consumer files from Tasks 2-6
- Reference: `docs/superpowers/specs/2026-07-20-comfy-node-contract-catalog-design.md`

- [ ] **Step 1: Re-run the complete catalog/Python manifest comparison**

Run:

```bash
printf 'Catalog constants: '
rg -c 'public const string [A-Za-z0-9]+ = "Swarm[^"]+";' src/BuiltinExtensions/ComfyUIBackend/ComfyNodeNames.cs
printf 'Python registrations: '
rg -o '"Swarm[^"]+"[[:space:]]*:' src/BuiltinExtensions/ComfyUIBackend/ExtraNodes --glob '*.py' \
    | sed -E 's/.*"(Swarm[^"]+)"[[:space:]]*:/\1/' | sort -u | wc -l
printf 'Symmetric differences: '
comm -3 \
    <(rg -o 'public const string [A-Za-z0-9]+ = "Swarm[^"]+";' src/BuiltinExtensions/ComfyUIBackend/ComfyNodeNames.cs \
        | sed -E 's/.*"(Swarm[^"]+)".*/\1/' | sort -u) \
    <(rg -o '"Swarm[^"]+"[[:space:]]*:' src/BuiltinExtensions/ComfyUIBackend/ExtraNodes --glob '*.py' \
        | sed -E 's/.*"(Swarm[^"]+)"[[:space:]]*:/\1/' | sort -u) | wc -l
```

Expected: `65`, `65`, and `0`.

- [ ] **Step 2: Audit remaining C# Swarm literals outside the catalog**

Run:

```bash
rg -n '"Swarm[A-Za-z0-9]+"' src/BuiltinExtensions/ComfyUIBackend --glob '*.cs' --glob '!ComfyNodeNames.cs'
```

Expected in the migrated consumer surface: no registered node-name string literals. Review any output individually; `SwarmReadableErrorException` XML `cref` text is a type reference, while UI/product strings such as `SwarmUI` are outside the node catalog. Do not mechanically migrate either category.

- [ ] **Step 3: Confirm public compatibility and capability-map contents**

Run:

```bash
rg -n 'public static Dictionary<string, string> NodeToFeatureMap = ComfyCapabilityCatalog\.CreateNodeToFeatureMap\(\);' src/BuiltinExtensions/ComfyUIBackend/ComfyUIBackendExtension.cs
rg -n 'ApplyDetectedNodeFeature\(key, NodeToFeatureMap, FeaturesSupported, FeaturesDiscardIfNotFound\);' src/BuiltinExtensions/ComfyUIBackend/ComfyUIBackendExtension.cs
rg -c '^            \[(ComfyNodeNames\.[A-Za-z0-9]+|"[^"]+")\] = "[^"]+",?$' src/BuiltinExtensions/ComfyUIBackend/ComfyCapabilityCatalog.cs
```

Expected: one public-field match, one apply-call match, and `29` mapping entries. Inspect loop context to confirm the apply call remains before hook scheduling detection and presumptive-feature removal.

- [ ] **Step 4: Confirm changed scope and whitespace**

Run after Tasks 1-6 have produced their six implementation commits:

```bash
git diff --check HEAD~6..HEAD
git diff --name-only HEAD~6..HEAD
git status --short
```

Expected committed range: the two new catalog files and seven intended consumer files only. Status may still show pre-existing maintainer changes, but none may be staged or included in these commits.

- [ ] **Step 5: Trace representative value-preservation paths**

Run:

```bash
rg -n 'ComfyNodeNames\.(LoadImageB64|KSampler|MaskBlur|Sam3BBoxFromJson|SaveImageWS|LoadAudioB64|VideoResampleFPS|ExtractLora|OffsetEmptyLatentImage)' src/BuiltinExtensions/ComfyUIBackend --glob '*.cs'
```

Expected coverage: image loading, sampling, masks, SAM, saving, audio, video, direct Web API JSON, and model/latent support. Confirm each constant resolves to the exact original Python class ID. No runtime availability check should have been added; Python object-info remains authoritative.

- [ ] **Step 6: Hand the manual validation matrix to the maintainer**

Ask the maintainer to validate in the live application:

1. Common-node connection, object-info parsing, feature flags, and missing optional-node behavior.
2. Optional ClipSeg, background removal, YOLO, SAM3, and animation-save capability detection.
3. Still-image generation with standard/Swarm sampling, variation seed, previews, metadata, and final save.
4. Base64 image/audio/video inputs, init image, masking, regional prompting, and tiling.
5. SAM/segmentation points, boxes, post-processing, blur, grow, threshold, and YOLO paths.
6. Model-support helpers, latent creation, model tracking, and LoRA use.
7. Animation/video/audio trim, frame count, resampling, boomerang, decode, and save paths.
8. LoRA extraction Web API workflow and direct `class_type` resolution.
9. Existing capability IDs and install/feature UI behavior with optional nodes present and absent.

Do not claim runtime validation until the maintainer reports the results.
