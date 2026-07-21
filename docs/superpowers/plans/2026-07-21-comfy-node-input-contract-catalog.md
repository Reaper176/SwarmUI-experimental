# Comfy Node Input Contract Catalog Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add a public, node-scoped catalog for every maintained Python-schema input name consumed by the approved built-in C# Comfy surface, then migrate those consumers without changing emitted workflows or runtime behavior.

**Architecture:** Add one constants-only `ComfyNodeInputNames` class beside `ComfyNodeNames`, with one nested class per consumed Swarm node. Migrate consumers in bounded layers while retaining Python and object-info as runtime authorities and deliberately leaving transport keys and five undeclared compatibility keys as literals.

**Tech Stack:** C# 12, .NET 8, Newtonsoft.Json `JObject`, maintained Comfy Python node schemas, Git, `rg`, and static source-analysis scripts. Repository policy forbids agent-run builds, tests, live-server runs, browser automation, and Comfy execution.

---

## Preconditions and protected state

- Work directly on the existing `master` checkout; the maintainer explicitly declined a worktree.
- Read `AGENTS.md`, `docs/project-memory.md`, and `docs/superpowers/specs/2026-07-21-comfy-node-input-contract-catalog-design.md` before editing.
- Preserve these unrelated maintainer changes and never stage them:
  - `src/Data/Settings.fds`
  - `src/Pages/Text2Image.cshtml`
  - `src/wwwroot/js/genpage/gentab/loras.js`
  - `src/wwwroot/js/genpage/main.js`
  - `Data.pre-restore-2026-07-19/`
- Never inspect or modify `Data.pre-restore-2026-07-19/`.
- Do not modify maintained Python schemas, downloaded upstream code, generated API docs, extensions, or project files. SDK-style project inclusion will discover the new C# file automatically.
- Never run `dotnet build`, tests, the live server, browser automation, or Comfy. Use only static inspection and linters permitted by `AGENTS.md`; the maintainer performs runtime validation.

## File map

- Create `src/BuiltinExtensions/ComfyUIBackend/ComfyNodeInputNames.cs`: the only public catalog of schema-backed input strings.
- Modify `src/BuiltinExtensions/ComfyUIBackend/ComfyUIBackendExtension.cs`: object-info input lookups.
- Modify `src/BuiltinExtensions/ComfyUIBackend/ComfyUIAPIAbstractBackend.cs`: model-list tracking input lookups.
- Modify `src/BuiltinExtensions/ComfyUIBackend/ComfyUIWebAPI.cs`: direct Extract LoRA workflow inputs.
- Modify `src/BuiltinExtensions/ComfyUIBackend/WorkflowGeneratorModelSupport.cs`: model/latent helper workflows.
- Modify `src/BuiltinExtensions/ComfyUIBackend/WGNodeData.cs`: media and save helper workflows.
- Modify `src/BuiltinExtensions/ComfyUIBackend/WorkflowGenerator.cs`: core and dynamic workflow construction.
- Modify `src/BuiltinExtensions/ComfyUIBackend/WorkflowGeneratorSteps.cs`: workflow step construction.

## Exact catalog contract

Create `ComfyNodeInputNames.cs` in namespace `SwarmUI.Builtin_ComfyUIBackend` with this shape:

```csharp
namespace SwarmUI.Builtin_ComfyUIBackend;

/// <summary>Canonical input names for Swarm-maintained Comfy nodes consumed by built-in C# code.</summary>
public static class ComfyNodeInputNames
{
    /// <summary>Canonical input names for the Swarm mask-blur node.</summary>
    public static class MaskBlur
    {
        /// <summary>Input name for the mask to blur.</summary>
        public const string Mask = "mask";

        /// <summary>Input name for the blur radius.</summary>
        public const string BlurRadius = "blur_radius";

        /// <summary>Input name for the blur sigma.</summary>
        public const string Sigma = "sigma";
    }
}
```

Follow that exact formatting pattern for every nested class and field: public static classes, public const strings, XML summaries on the outer class, every nested class, and every constant; no dictionaries, collections, reflection, validation, or runtime initialization. Use the following exhaustive `NestedClass.Member = "value"` declarations:

```text
AnimaLLLite.EndPercent="end_percent"; Image="image"; LLLiteName="lllite_name"; Mask="mask"; Model="model"; StartPercent="start_percent"; Strength="strength"
AttentionCouple.BaseCondition="base_cond"; BaseMask="base_mask"; ConditionPrefix="cond_"; MaskPrefix="mask_"; Model="model"; RegionsJson="regions_json"
CleanOverlapMasksExceptSelf.MaskMerged="mask_merged"; MaskSelf="mask_self"
ClipSeg.Images="images"; MatchText="match_text"; Threshold="threshold"
ClipTextEncodeAdvanced.CLIP="clip"; CLIPVisionOutput="clip_vision_output"; Guidance="guidance"; Height="height"; Images="images"; LlamaTemplate="llama_template"; Prompt="prompt"; Steps="steps"; TargetHeight="target_height"; TargetWidth="target_width"; TokenNormalization="token_normalization"; WeightInterpretation="weight_interpretation"; Width="width"
CountFrames.Image="image"
DetailDaemonOptions.Bias="bias"; CFGScaleOverride="cfg_scale_override"; DetailAmount="detail_amount"; End="end"; EndOffset="end_offset"; Exponent="exponent"; Fade="fade"; Smooth="smooth"; Start="start"; StartOffset="start_offset"
EmbedLoaderListProvider.EmbedName="embed_name"
EnsureAudio.Audio="audio"; TargetDuration="target_duration"
ExcludeFromMask.ExcludeMask="exclude_mask"; MainMask="main_mask"
ExtractLora.BaseModel="base_model"; Metadata="metadata"; OtherModel="other_model"; Rank="rank"; SaveFilename="save_filename"; SaveRawPath="save_rawpath"
ImageCompositeMaskedColorCorrecting.CorrectionMethod="correction_method"; Destination="destination"; Mask="mask"; Source="source"; X="x"; Y="y"
ImageCrop.Height="height"; Image="image"; Width="width"; X="x"; Y="y"
ImageHeight.Image="image"
ImageNoise.Amount="amount"; Image="image"; Mask="mask"; Seed="seed"
ImageScaleForMP.CanShrink="can_shrink"; Height="height"; Image="image"; Width="width"
ImageWidth.Image="image"
IntAdd.A="a"; B="b"
JustLoadTheModelPlease.CLIP="clip"; Model="model"; VAE="vae"
KSampler.AddNoise="add_noise"; CFG="cfg"; DetailDaemon="detail_daemon"; EndAtStep="end_at_step"; LatentImage="latent_image"; Model="model"; ModelNegative="model_negative"; Negative="negative"; NoiseSeed="noise_seed"; Positive="positive"; Previews="previews"; ReturnWithLeftoverNoise="return_with_leftover_noise"; Rho="rho"; SamplerName="sampler_name"; Scheduler="scheduler"; SigmaMax="sigma_max"; SigmaMin="sigma_min"; StartAtStep="start_at_step"; Steps="steps"; TileSample="tile_sample"; TileSize="tile_size"; VarSeed="var_seed"; VarSeedStrength="var_seed_strength"
LatentBlendMasked.BlendFactor="blend_factor"; Mask="mask"; Samples0="samples0"; Samples1="samples1"
LoadAudioB64.AudioBase64="audio_base64"
LoadImageB64.ImageBase64="image_base64"
LoadVideoB64.VideoBase64="video_base64"
LTXVAudioVAELoader.VAEName="vae_name"
MaskBlur.BlurRadius="blur_radius"; Mask="mask"; Sigma="sigma"
MaskBounds.AspectX="aspect_x"; AspectY="aspect_y"; Grow="grow"; Mask="mask"
MaskGrow.Grow="grow"; Mask="mask"
MaskThreshold.Mask="mask"; Max="max"; Min="min"
ModelTiling.Model="model"; TileAxis="tile_axis"
OffsetEmptyLatentImage.BatchSize="batch_size"; Height="height"; OffA="off_a"; OffB="off_b"; OffC="off_c"; OffD="off_d"; Width="width"
OverMergeMasksForOverlapFix.MaskA="mask_a"; MaskB="mask_b"
ReferenceOnly.Latent="latent"; Model="model"; Reference="reference"
RemBg.Images="images"
Sam3BBoxFromJson.BBoxJson="bbox_json"; Image="image"
Sam3MaskPostProcess.FillHoles="fill_holes"; HoleKernelSize="hole_kernel_size"; Mask="mask"
Sam3PointsFromJson.Image="image"; IsForeground="is_foreground"; PointsJson="points_json"
SaveAnimationWS.Audio="audio"; Format="format"; FPS="fps"; Images="images"; Lossless="lossless"; Method="method"; Quality="quality"
SaveImageWS.BitDepth="bit_depth"; Images="images"
SquareMaskFromPercent.Height="height"; Strength="strength"; Width="width"; X="x"; Y="y"
TileableVAE.TileAxis="tile_axis"; VAE="vae"
TrimFrames.Image="image"; TrimEnd="trim_end"; TrimStart="trim_start"
Unsampler.LatentImage="latent_image"; Model="model"; Negative="negative"; Positive="positive"; Previews="previews"; SamplerName="sampler_name"; Scheduler="scheduler"; StartAtStep="start_at_step"; Steps="steps"
VideoBoomerang.Images="images"
VideoResampleFPS.FPSIn="fps_in"; FPSOut="fps_out"; Images="images"; Method="method"
YoloDetection.ClassFilter="class_filter"; Image="image"; Index="index"; ModelName="model_name"; SortOrder="sort_order"; Threshold="threshold"
```

The two `AttentionCouple` prefix values count among the 184 catalog entries but are not complete schema keys. They must only be used by appending the existing one-based index. Do not add `control_after_generate`, `resize_source`, `base_model_clip`, `other_model_clip`, `save_clip`, `class_type`, or `inputs` to the catalog.

### Task 1: Add the input-name catalog

**Files:**
- Create: `src/BuiltinExtensions/ComfyUIBackend/ComfyNodeInputNames.cs`

- [ ] **Step 1: Record the protected working-tree baseline**

Run:

```bash
git status --short --branch
```

Expected: `master` is ahead of `origin/master`; only the known maintainer files and backup directory are dirty. Do not stage, rewrite, or inspect their contents.

- [ ] **Step 2: Create the complete catalog**

Use `apply_patch` to add the file using the exact class shape and exhaustive contract above. Give each member a short description tied to its containing node; for the two prefixes, explicitly document that the caller appends a one-based index.

- [ ] **Step 3: Verify catalog structure and exclusions statically**

Run:

```bash
rg -c '^    public static class ' src/BuiltinExtensions/ComfyUIBackend/ComfyNodeInputNames.cs
rg -c '^        public const string ' src/BuiltinExtensions/ComfyUIBackend/ComfyNodeInputNames.cs
rg -n 'control_after_generate|resize_source|base_model_clip|other_model_clip|save_clip|class_type|"inputs"' src/BuiltinExtensions/ComfyUIBackend/ComfyNodeInputNames.cs
rg -n '\b(var|const)\b|===|!==' src/BuiltinExtensions/ComfyUIBackend/ComfyNodeInputNames.cs
```

Expected: counts are `46` and `184`; exclusion search and convention search produce no output. Inspect every constant against the complete contract and the corresponding maintained Python `INPUT_TYPES`/`define_schema`; `cond_` and `mask_` must expand to the existing `1` through `8` keys.

- [ ] **Step 4: Review and commit the catalog alone**

Run:

```bash
git diff --check -- src/BuiltinExtensions/ComfyUIBackend/ComfyNodeInputNames.cs
git diff -- src/BuiltinExtensions/ComfyUIBackend/ComfyNodeInputNames.cs
git add src/BuiltinExtensions/ComfyUIBackend/ComfyNodeInputNames.cs
git diff --cached --check
git commit -m "refactor: catalog Comfy node input names"
```

Expected: only the new catalog is staged and committed; all protected maintainer changes remain unstaged.

### Task 2: Migrate backend discovery and the direct Web API workflow

**Files:**
- Modify: `src/BuiltinExtensions/ComfyUIBackend/ComfyUIBackendExtension.cs`
- Modify: `src/BuiltinExtensions/ComfyUIBackend/ComfyUIAPIAbstractBackend.cs`
- Modify: `src/BuiltinExtensions/ComfyUIBackend/ComfyUIWebAPI.cs`

- [ ] **Step 1: Replace the five lookup literals**

Use `apply_patch` for these exact expression replacements, changing no surrounding arguments, messages, categories, conditions, or ordering:

```csharp
// ComfyUIBackendExtension.cs
"sampler_name" -> ComfyNodeInputNames.KSampler.SamplerName
"scheduler"    -> ComfyNodeInputNames.KSampler.Scheduler
"model_name"   -> ComfyNodeInputNames.YoloDetection.ModelName

// ComfyUIAPIAbstractBackend.cs
"lllite_name"  -> ComfyNodeInputNames.AnimaLLLite.LLLiteName
"embed_name"   -> ComfyNodeInputNames.EmbedLoaderListProvider.EmbedName
```

- [ ] **Step 2: Replace only the six Extract LoRA schema keys**

Within the `ComfyNodeNames.ExtractLora` workflow object in `ComfyUIWebAPI.cs`, make exactly these property-name replacements and preserve property order and values:

```csharp
["base_model"]     -> [ComfyNodeInputNames.ExtractLora.BaseModel]
["other_model"]    -> [ComfyNodeInputNames.ExtractLora.OtherModel]
["rank"]           -> [ComfyNodeInputNames.ExtractLora.Rank]
["save_rawpath"]   -> [ComfyNodeInputNames.ExtractLora.SaveRawPath]
["save_filename"]  -> [ComfyNodeInputNames.ExtractLora.SaveFilename]
["metadata"]       -> [ComfyNodeInputNames.ExtractLora.Metadata]
```

Keep these exact literals unchanged in that object:

```csharp
["class_type"]
["inputs"]
["base_model_clip"]
["other_model_clip"]
["save_clip"]
```

- [ ] **Step 3: Statically review behavior and commit**

Run:

```bash
git diff --check -- src/BuiltinExtensions/ComfyUIBackend/ComfyUIBackendExtension.cs src/BuiltinExtensions/ComfyUIBackend/ComfyUIAPIAbstractBackend.cs src/BuiltinExtensions/ComfyUIBackend/ComfyUIWebAPI.cs
git diff --word-diff=plain -- src/BuiltinExtensions/ComfyUIBackend/ComfyUIBackendExtension.cs src/BuiltinExtensions/ComfyUIBackend/ComfyUIAPIAbstractBackend.cs src/BuiltinExtensions/ComfyUIBackend/ComfyUIWebAPI.cs
rg -n 'base_model_clip|other_model_clip|save_clip' src/BuiltinExtensions/ComfyUIBackend/ComfyUIWebAPI.cs
git add src/BuiltinExtensions/ComfyUIBackend/ComfyUIBackendExtension.cs src/BuiltinExtensions/ComfyUIBackend/ComfyUIAPIAbstractBackend.cs src/BuiltinExtensions/ComfyUIBackend/ComfyUIWebAPI.cs
git diff --cached --check
git commit -m "refactor: use Comfy input catalog in backend APIs"
```

Expected: word diff shows property-name expressions only; all three Extract LoRA compatibility literals remain; no protected file is staged.

### Task 3: Migrate model and media helpers

**Files:**
- Modify: `src/BuiltinExtensions/ComfyUIBackend/WorkflowGeneratorModelSupport.cs`
- Modify: `src/BuiltinExtensions/ComfyUIBackend/WGNodeData.cs`

- [ ] **Step 1: Migrate model-support node inputs**

Use `apply_patch` to replace property-name literals only in these `CreateNode` objects:

```text
OffsetEmptyLatentImage: BatchSize, Height, OffA, OffB, OffC, OffD, Width
LTXVAudioVAELoader: VAEName
```

For example, `["off_a"] = offA` becomes `[ComfyNodeInputNames.OffsetEmptyLatentImage.OffA] = offA`. Preserve every value, property position, branch, returned node reference, and node class.

- [ ] **Step 2: Migrate media/save helper node inputs**

Use the same exact transformation in `WGNodeData.cs` for:

```text
CountFrames: Image
EnsureAudio: Audio, TargetDuration
SaveImageWS: BitDepth, Images
VideoBoomerang: Images
SaveAnimationWS: Audio, Format, FPS, Images, Lossless, Method, Quality
```

Do not alter the transport lookup in `SourceNodeData`; its `class_type` and `inputs` keys are workflow-envelope keys.

- [ ] **Step 3: Normalize the diff mentally and commit**

Run:

```bash
git diff --check -- src/BuiltinExtensions/ComfyUIBackend/WorkflowGeneratorModelSupport.cs src/BuiltinExtensions/ComfyUIBackend/WGNodeData.cs
git diff --word-diff=plain -- src/BuiltinExtensions/ComfyUIBackend/WorkflowGeneratorModelSupport.cs src/BuiltinExtensions/ComfyUIBackend/WGNodeData.cs
rg -n '\["(class_type|inputs)"\]' src/BuiltinExtensions/ComfyUIBackend/WGNodeData.cs
git add src/BuiltinExtensions/ComfyUIBackend/WorkflowGeneratorModelSupport.cs src/BuiltinExtensions/ComfyUIBackend/WGNodeData.cs
git diff --cached --check
git commit -m "refactor: use Comfy input catalog in model and media helpers"
```

Expected: substituting each constant with its declared string reconstructs the old key sequence exactly; envelope literals remain unchanged.

### Task 4: Migrate the core workflow generator

**Files:**
- Modify: `src/BuiltinExtensions/ComfyUIBackend/WorkflowGenerator.cs`

- [ ] **Step 1: Migrate direct Swarm-node objects**

Use `apply_patch` to replace only property-name expressions belonging to these node-scoped contracts, retaining the existing property order and values:

```text
LoadAudioB64: AudioBase64
LoadImageB64: ImageBase64
LoadVideoB64: VideoBase64
MaskThreshold: Mask, Max, Min
MaskBounds: AspectX, AspectY, Grow, Mask
ImageCrop: Height, Image, Width, X, Y
ImageScaleForMP: CanShrink, Height, Image, Width
DetailDaemonOptions: Bias, CFGScaleOverride, DetailAmount, End, EndOffset, Exponent, Fade, Smooth, Start, StartOffset
TrimFrames: Image, TrimEnd, TrimStart
ClipTextEncodeAdvanced: CLIP, CLIPVisionOutput, Guidance, Height, Images, LlamaTemplate, Prompt, Steps, TargetHeight, TargetWidth, TokenNormalization, WeightInterpretation, Width
SquareMaskFromPercent: Height, Strength, Width, X, Y
OverMergeMasksForOverlapFix: MaskA, MaskB
ExcludeFromMask: ExcludeMask, MainMask
CleanOverlapMasksExceptSelf: MaskMerged, MaskSelf
```

The exact syntax is `["literal"]` to `[ComfyNodeInputNames.Node.Member]`. Do not replace equal-looking keys on upstream or third-party node objects merely because their strings match.

- [ ] **Step 2: Migrate both dynamic composite builders**

For each object whose selected class may be `ComfyNodeNames.ImageCompositeMaskedColorCorrecting`, replace only these six keys:

```csharp
["correction_method"] -> [ComfyNodeInputNames.ImageCompositeMaskedColorCorrecting.CorrectionMethod]
["destination"]       -> [ComfyNodeInputNames.ImageCompositeMaskedColorCorrecting.Destination]
["mask"]              -> [ComfyNodeInputNames.ImageCompositeMaskedColorCorrecting.Mask]
["source"]            -> [ComfyNodeInputNames.ImageCompositeMaskedColorCorrecting.Source]
["x"]                 -> [ComfyNodeInputNames.ImageCompositeMaskedColorCorrecting.X]
["y"]                 -> [ComfyNodeInputNames.ImageCompositeMaskedColorCorrecting.Y]
```

Keep `["resize_source"] = false` unchanged in both builders because it is not declared by the maintained Swarm schema and the same object may target an upstream fallback.

- [ ] **Step 3: Migrate the dynamic KSampler builder**

Replace every schema-backed key in the shared sampler `inputs` object with its member from the 23-entry `KSampler` contract. Use the same constants for indexer assignments added later, including:

```csharp
inputs[ComfyNodeInputNames.KSampler.DetailDaemon] = new JArray() { detailDaemonOptions, 0 };
```

Do not invent a catalog member for the compatibility key. Keep this existing assignment byte-for-byte literal because it is outside the maintained schema:

```csharp
inputs["control_after_generate"] = "fixed";
```

Preserve the branch that sends the same `JObject` to upstream `KSamplerAdvanced`, all feature gates, node IDs, values, and sequencing.

- [ ] **Step 4: Migrate regional workflow inputs**

Replace fixed keys with the relevant `SquareMaskFromPercent`, `OverMergeMasksForOverlapFix`, `ExcludeFromMask`, `CleanOverlapMasksExceptSelf`, and `AttentionCouple` constants. Replace the numbered assignments exactly as follows:

```csharp
inputs[$"{ComfyNodeInputNames.AttentionCouple.ConditionPrefix}{i + 1}"] = plan.Regions[i].Cond;
inputs[$"{ComfyNodeInputNames.AttentionCouple.MaskPrefix}{i + 1}"] = plan.Regions[i].Mask;
```

Use `AttentionCouple.BaseCondition`, `BaseMask`, `Model`, and `RegionsJson` for its fixed properties. Keep the existing one-based index and add no bounds checking.

- [ ] **Step 5: Audit exclusions, effective keys, and commit**

Run:

```bash
git diff --check -- src/BuiltinExtensions/ComfyUIBackend/WorkflowGenerator.cs
git diff --word-diff=plain -- src/BuiltinExtensions/ComfyUIBackend/WorkflowGenerator.cs
rg -n 'control_after_generate|resize_source' src/BuiltinExtensions/ComfyUIBackend/WorkflowGenerator.cs
rg -n '\$"cond_|\$"mask_' src/BuiltinExtensions/ComfyUIBackend/WorkflowGenerator.cs
git add src/BuiltinExtensions/ComfyUIBackend/WorkflowGenerator.cs
git diff --cached --check
git commit -m "refactor: use Comfy input catalog in workflow generation"
```

Expected: both compatibility keys still occur in their original positions; raw numbered-prefix interpolation no longer occurs; normalizing constant expressions to their values yields the original key and property sequence.

### Task 5: Migrate workflow generator steps

**Files:**
- Modify: `src/BuiltinExtensions/ComfyUIBackend/WorkflowGeneratorSteps.cs`

- [ ] **Step 1: Migrate direct node objects**

Use `apply_patch` to replace property-name expressions for all occurrences of these node contracts:

```text
ModelTiling: Model, TileAxis
TileableVAE: TileAxis, VAE
MaskGrow: Grow, Mask
MaskBlur: BlurRadius, Mask, Sigma
MaskThreshold: Mask, Max, Min
Unsampler: LatentImage, Model, Negative, Positive, Previews, SamplerName, Scheduler, StartAtStep, Steps
LatentBlendMasked: BlendFactor, Mask, Samples0, Samples1
CountFrames: Image
ReferenceOnly: Latent, Model, Reference
VideoResampleFPS: FPSIn, FPSOut, Images, Method
Sam3PointsFromJson: Image, IsForeground, PointsJson
Sam3MaskPostProcess: FillHoles, HoleKernelSize, Mask
Sam3BBoxFromJson: BBoxJson, Image
JustLoadTheModelPlease: CLIP, Model, VAE
YoloDetection: ClassFilter, Image, Index, ModelName, SortOrder, Threshold
ClipSeg: Images, MatchText, Threshold
RemBg: Images
TrimFrames: Image, TrimEnd, TrimStart
ImageWidth: Image
ImageHeight: Image
IntAdd: A, B
```

Do not migrate keys on upstream/third-party nodes or workflow-envelope reads. Preserve every branch, value, node class, reference, error, and property order.

- [ ] **Step 2: Migrate the dynamic ImageNoise object**

Change its assignments exactly by key ownership:

```csharp
["image"]  -> [ComfyNodeInputNames.ImageNoise.Image]
["amount"] -> [ComfyNodeInputNames.ImageNoise.Amount]
["seed"]   -> [ComfyNodeInputNames.ImageNoise.Seed]
["mask"]   -> [ComfyNodeInputNames.ImageNoise.Mask]
```

The optional mask assignment remains conditional and at the same point in construction.

- [ ] **Step 3: Migrate the dynamic Anima LLLite object**

Use `ComfyNodeInputNames.AnimaLLLite` members for `model`, `lllite_name`, `image`, `strength`, `start_percent`, `end_percent`, and optional `mask`. Preserve optional-mask behavior, percentages, values, and node selection.

- [ ] **Step 4: Review effective behavior and commit**

Run:

```bash
git diff --check -- src/BuiltinExtensions/ComfyUIBackend/WorkflowGeneratorSteps.cs
git diff --word-diff=plain -- src/BuiltinExtensions/ComfyUIBackend/WorkflowGeneratorSteps.cs
rg -n '\["(class_type|inputs)"\]' src/BuiltinExtensions/ComfyUIBackend/WorkflowGeneratorSteps.cs
git add src/BuiltinExtensions/ComfyUIBackend/WorkflowGeneratorSteps.cs
git diff --cached --check
git commit -m "refactor: use Comfy input catalog in workflow steps"
```

Expected: every changed token is an input-key expression or necessary formatting around it; constant normalization reconstructs every old `JObject` key sequence.

### Task 6: Perform the whole-surface static audit and hand off runtime validation

**Files:**
- Inspect: all eight changed C# files
- Inspect: `docs/superpowers/specs/2026-07-21-comfy-node-input-contract-catalog-design.md`
- Modify: none unless the audit identifies a concrete defect

- [ ] **Step 1: Confirm commit and working-tree scope**

Run:

```bash
git status --short --branch
git log --oneline --decorate -8
git diff --name-only origin/master...HEAD
git diff --check origin/master...HEAD
```

Expected: the five implementation commits follow the approved design commit; the implementation range contains only the catalog and seven approved consumers. The pre-existing maintainer changes and backup directory remain unstaged and untouched.

- [ ] **Step 2: Audit catalog structure, documentation, and forbidden entries**

Run:

```bash
rg -c '^    public static class ' src/BuiltinExtensions/ComfyUIBackend/ComfyNodeInputNames.cs
rg -c '^        public const string ' src/BuiltinExtensions/ComfyUIBackend/ComfyNodeInputNames.cs
rg -c '^    /// <summary>' src/BuiltinExtensions/ComfyUIBackend/ComfyNodeInputNames.cs
rg -c '^        /// <summary>' src/BuiltinExtensions/ComfyUIBackend/ComfyNodeInputNames.cs
rg -n 'control_after_generate|resize_source|base_model_clip|other_model_clip|save_clip|class_type|"inputs"' src/BuiltinExtensions/ComfyUIBackend/ComfyNodeInputNames.cs
```

Expected: `46`, `184`, `46`, `184`, and no forbidden-entry output. The outer class also has its own XML summary.

- [ ] **Step 3: Audit intentional literals and dynamic prefixes**

Run:

```bash
rg -n 'control_after_generate|resize_source|base_model_clip|other_model_clip|save_clip' src/BuiltinExtensions/ComfyUIBackend --glob '*.cs'
rg -n '\$"cond_|\$"mask_' src/BuiltinExtensions/ComfyUIBackend --glob '*.cs'
rg -n 'ConditionPrefix|MaskPrefix' src/BuiltinExtensions/ComfyUIBackend --glob '*.cs'
```

Expected: exactly the five named compatibility key values remain literals at the approved construction sites; raw Attention Couple interpolation is absent; each prefix is declared once and consumed in the existing one-based assignments.

- [ ] **Step 4: Audit all approved consumers and non-goals**

For every `CreateNode(ComfyNodeNames.<Node>` occurrence and separately assembled input object in the seven consumer files, compare the node against the exhaustive catalog contract. Confirm each cataloged input reference uses its matching nested class, and confirm remaining literals belong only to upstream/third-party nodes, workflow transport, or the five explicit extras. Also run:

```bash
git diff --name-only 08cb21d0..HEAD
git diff --stat 08cb21d0..HEAD
git diff --word-diff=plain 08cb21d0..HEAD -- src/BuiltinExtensions/ComfyUIBackend
```

Expected: no Python, frontend, project, generated-doc, extension, data, launcher, class-name, or capability-map changes. Replacing each catalog expression mentally with its constant value yields identical property order, keys, values, IDs, output references, conditions, feature gates, warning/error text, and fallback routing.

- [ ] **Step 5: Request independent review before claiming completion**

Use `superpowers:requesting-code-review` to review the complete implementation range against the design. Resolve only verified defects within scope, using `superpowers:receiving-code-review` for feedback. If a fix is required, apply it with `apply_patch`, repeat all affected static checks, and commit it separately with a precise message.

- [ ] **Step 6: Hand off the approved manual validation matrix**

Ask the maintainer to perform the nine manual validation groups in the design specification: startup/object-info, optional nodes present/absent, sampling, media/masks/regions, SAM3/segmentation, model/latent helpers, video/audio/save flows, Extract LoRA, and unavailable-node errors. Do not claim runtime success until the maintainer reports it.

## Completion criteria

- The catalog has exactly 46 nested classes and 184 documented entries.
- Every complete catalog value matches its maintained Python schema; the two prefixes retain one-based numbered behavior.
- All approved built-in C# consumers use the matching node-scoped constants.
- The five undeclared compatibility keys and workflow-envelope keys remain unchanged literals and absent from the catalog.
- Static diffs demonstrate property-name-only migration with no behavioral, ordering, class-name, capability, or authority changes.
- Independent review finds no unresolved specification or code-quality defects.
- The maintainer completes runtime validation before the work is described as fully verified.
