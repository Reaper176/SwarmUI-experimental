# Comfy Node Input Contract Catalog Design

## Goal

Complete the static Swarm-maintained Comfy contract boundary by cataloging every Python-schema input name currently consumed by the approved built-in C# Comfy backend surface. Replace duplicated C# input-key literals with public, documented, node-scoped constants without changing workflow JSON, object-info interpretation, optional-node behavior, or Python runtime authority.

This is the input-key follow-up intentionally deferred by the class-name catalog project. It completes the low-risk contract foundation before later frontend-facade, application-service, or workflow-generator decomposition work.

## Current State

`ComfyNodeNames` catalogs all 65 Swarm-maintained Python registration IDs, and maintained built-in C# consumers use those constants for class names. The same consumers still repeat node input names as string literals in:

- `ComfyUIBackendExtension.cs` object-info lookups;
- `ComfyUIAPIAbstractBackend.cs` model-list tracking;
- `ComfyUIWebAPI.cs` direct LoRA-extraction workflow JSON;
- `WorkflowGenerator.cs` static and dynamically assembled inputs;
- `WorkflowGeneratorSteps.cs` static and dynamically assembled inputs;
- `WorkflowGeneratorModelSupport.cs` model and latent helpers;
- `WGNodeData.cs` media and save helpers.

The approved surface consumes schema-backed inputs from 46 of the 65 registered nodes. Common names such as `image`, `model`, and `mask` recur across unrelated node schemas, so a flat shared-key catalog would hide ownership and allow one node's schema to drift without an obvious review boundary.

Python `INPUT_TYPES` or `define_schema` declarations remain authoritative for whether a key is part of a maintained node contract. Comfy object-info remains authoritative for which optional nodes and inputs are actually available at runtime.

## Selected Architecture

Create `src/BuiltinExtensions/ComfyUIBackend/ComfyNodeInputNames.cs` in the existing `SwarmUI.Builtin_ComfyUIBackend` namespace.

`ComfyNodeInputNames` is a documented `public static class`. Each C#-consumed Swarm node has one documented nested `public static class`, and each consumed Python-schema input has one documented `public const string` within that node class.

Example:

```csharp
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
```

Repeated values remain node-scoped. `ComfyNodeInputNames.MaskBlur.Mask` and `ComfyNodeInputNames.MaskThreshold.Mask` deliberately have the same value but represent different node contracts.

The catalog contains constants only. It exposes no dictionaries, collections, reflection, runtime parsing, validation, or availability state.

## Naming Rules

- Python snake-case input names become PascalCase members.
- Established acronyms remain capitalized: `CFG`, `CLIP`, `FPS`, `LLLite`, `LTXV`, `VAE`, and `BBox`.
- `json` becomes `Json`, matching existing `ComfyNodeNames` casing.
- `save_rawpath` becomes `SaveRawPath` for readable C# while preserving the exact value.
- Simple names such as `image`, `mask`, `model`, `x`, `y`, `a`, and `b` become `Image`, `Mask`, `Model`, `X`, `Y`, `A`, and `B`.
- Member names must be unique within their nested node class; values may repeat across node classes.

## Complete Catalog Manifest

The catalog contains 46 nested node classes and 184 entries. The following manifest is exhaustive; each `Member = value` pair becomes one public constant except the two explicitly identified prefixes.

- `AnimaLLLite`: `EndPercent = end_percent`, `Image = image`, `LLLiteName = lllite_name`, `Mask = mask`, `Model = model`, `StartPercent = start_percent`, `Strength = strength`.
- `AttentionCouple`: `BaseCondition = base_cond`, `BaseMask = base_mask`, `ConditionPrefix = cond_`, `MaskPrefix = mask_`, `Model = model`, `RegionsJson = regions_json`.
- `CleanOverlapMasksExceptSelf`: `MaskMerged = mask_merged`, `MaskSelf = mask_self`.
- `ClipSeg`: `Images = images`, `MatchText = match_text`, `Threshold = threshold`.
- `ClipTextEncodeAdvanced`: `CLIP = clip`, `CLIPVisionOutput = clip_vision_output`, `Guidance = guidance`, `Height = height`, `Images = images`, `LlamaTemplate = llama_template`, `Prompt = prompt`, `Steps = steps`, `TargetHeight = target_height`, `TargetWidth = target_width`, `TokenNormalization = token_normalization`, `WeightInterpretation = weight_interpretation`, `Width = width`.
- `CountFrames`: `Image = image`.
- `DetailDaemonOptions`: `Bias = bias`, `CFGScaleOverride = cfg_scale_override`, `DetailAmount = detail_amount`, `End = end`, `EndOffset = end_offset`, `Exponent = exponent`, `Fade = fade`, `Smooth = smooth`, `Start = start`, `StartOffset = start_offset`.
- `EmbedLoaderListProvider`: `EmbedName = embed_name`.
- `EnsureAudio`: `Audio = audio`, `TargetDuration = target_duration`.
- `ExcludeFromMask`: `ExcludeMask = exclude_mask`, `MainMask = main_mask`.
- `ExtractLora`: `BaseModel = base_model`, `Metadata = metadata`, `OtherModel = other_model`, `Rank = rank`, `SaveFilename = save_filename`, `SaveRawPath = save_rawpath`.
- `ImageCompositeMaskedColorCorrecting`: `CorrectionMethod = correction_method`, `Destination = destination`, `Mask = mask`, `Source = source`, `X = x`, `Y = y`.
- `ImageCrop`: `Height = height`, `Image = image`, `Width = width`, `X = x`, `Y = y`.
- `ImageHeight`: `Image = image`.
- `ImageNoise`: `Amount = amount`, `Image = image`, `Mask = mask`, `Seed = seed`.
- `ImageScaleForMP`: `CanShrink = can_shrink`, `Height = height`, `Image = image`, `Width = width`.
- `ImageWidth`: `Image = image`.
- `IntAdd`: `A = a`, `B = b`.
- `JustLoadTheModelPlease`: `CLIP = clip`, `Model = model`, `VAE = vae`.
- `KSampler`: `AddNoise = add_noise`, `CFG = cfg`, `DetailDaemon = detail_daemon`, `EndAtStep = end_at_step`, `LatentImage = latent_image`, `Model = model`, `ModelNegative = model_negative`, `Negative = negative`, `NoiseSeed = noise_seed`, `Positive = positive`, `Previews = previews`, `ReturnWithLeftoverNoise = return_with_leftover_noise`, `Rho = rho`, `SamplerName = sampler_name`, `Scheduler = scheduler`, `SigmaMax = sigma_max`, `SigmaMin = sigma_min`, `StartAtStep = start_at_step`, `Steps = steps`, `TileSample = tile_sample`, `TileSize = tile_size`, `VarSeed = var_seed`, `VarSeedStrength = var_seed_strength`.
- `LatentBlendMasked`: `BlendFactor = blend_factor`, `Mask = mask`, `Samples0 = samples0`, `Samples1 = samples1`.
- `LoadAudioB64`: `AudioBase64 = audio_base64`.
- `LoadImageB64`: `ImageBase64 = image_base64`.
- `LoadVideoB64`: `VideoBase64 = video_base64`.
- `LTXVAudioVAELoader`: `VAEName = vae_name`.
- `MaskBlur`: `BlurRadius = blur_radius`, `Mask = mask`, `Sigma = sigma`.
- `MaskBounds`: `AspectX = aspect_x`, `AspectY = aspect_y`, `Grow = grow`, `Mask = mask`.
- `MaskGrow`: `Grow = grow`, `Mask = mask`.
- `MaskThreshold`: `Mask = mask`, `Max = max`, `Min = min`.
- `ModelTiling`: `Model = model`, `TileAxis = tile_axis`.
- `OffsetEmptyLatentImage`: `BatchSize = batch_size`, `Height = height`, `OffA = off_a`, `OffB = off_b`, `OffC = off_c`, `OffD = off_d`, `Width = width`.
- `OverMergeMasksForOverlapFix`: `MaskA = mask_a`, `MaskB = mask_b`.
- `ReferenceOnly`: `Latent = latent`, `Model = model`, `Reference = reference`.
- `RemBg`: `Images = images`.
- `Sam3BBoxFromJson`: `BBoxJson = bbox_json`, `Image = image`.
- `Sam3MaskPostProcess`: `FillHoles = fill_holes`, `HoleKernelSize = hole_kernel_size`, `Mask = mask`.
- `Sam3PointsFromJson`: `Image = image`, `IsForeground = is_foreground`, `PointsJson = points_json`.
- `SaveAnimationWS`: `Audio = audio`, `Format = format`, `FPS = fps`, `Images = images`, `Lossless = lossless`, `Method = method`, `Quality = quality`.
- `SaveImageWS`: `BitDepth = bit_depth`, `Images = images`.
- `SquareMaskFromPercent`: `Height = height`, `Strength = strength`, `Width = width`, `X = x`, `Y = y`.
- `TileableVAE`: `TileAxis = tile_axis`, `VAE = vae`.
- `TrimFrames`: `Image = image`, `TrimEnd = trim_end`, `TrimStart = trim_start`.
- `Unsampler`: `LatentImage = latent_image`, `Model = model`, `Negative = negative`, `Positive = positive`, `Previews = previews`, `SamplerName = sampler_name`, `Scheduler = scheduler`, `StartAtStep = start_at_step`, `Steps = steps`.
- `VideoBoomerang`: `Images = images`.
- `VideoResampleFPS`: `FPSIn = fps_in`, `FPSOut = fps_out`, `Images = images`, `Method = method`.
- `YoloDetection`: `ClassFilter = class_filter`, `Image = image`, `Index = index`, `ModelName = model_name`, `SortOrder = sort_order`, `Threshold = threshold`.

`AttentionCouple.ConditionPrefix` and `AttentionCouple.MaskPrefix` are prefix constants rather than complete Python input names. They represent the maintained schema's numbered `cond_1` through `cond_8` and `mask_1` through `mask_8` inputs. C# continues appending the existing one-based index and adds no range validation.

## Explicit Compatibility Extras

Five C# keys occur in Swarm-node construction contexts but are not declared by the corresponding maintained Python schema:

- `control_after_generate` in the object shared by `SwarmKSampler` and the upstream fallback;
- `resize_source` in the object shared by `SwarmImageCompositeMaskedColorCorrecting` and the upstream composite node;
- `base_model_clip`, `other_model_clip`, and `save_clip` in the direct `SwarmExtractLora` workflow.

These keys remain byte-for-byte literals in their existing positions. They are not added to `ComfyNodeInputNames`, removed from workflows, validated, or otherwise reinterpreted. Their presence and cleanup are separate behavior questions outside this static schema-backed catalog.

Workflow envelope keys such as `class_type` and `inputs` are also excluded because they belong to Comfy workflow transport rather than a Swarm node schema.

## Consumer Migration

Every schema-backed input-key expression in the approved built-in surface migrates to the corresponding node-scoped constant.

### Backend Discovery

- `ComfyUIBackendExtension` uses `KSampler.SamplerName`, `KSampler.Scheduler`, and `YoloDetection.ModelName` for object-info lookups.
- `ComfyUIAPIAbstractBackend` uses `AnimaLLLite.LLLiteName` and `EmbedLoaderListProvider.EmbedName` for model tracking.
- Input extraction, warning text, feature behavior, model categories, and object-info ordering remain unchanged.

### Direct Web API Workflow

`ComfyUIWebAPI` migrates the six schema-backed `ExtractLora` keys. Its `class_type` and `inputs` envelope keys and the three undeclared compatibility extras remain literals. Node IDs, array references, property ordering, file paths, metadata content, and execution behavior remain unchanged.

### Workflow Generation

- `WorkflowGeneratorModelSupport.cs` migrates offset-latent and LTXV audio-VAE input keys.
- `WGNodeData.cs` migrates frame-count, audio, image-save, boomerang, and animation-save input keys.
- `WorkflowGenerator.cs` migrates media loading, masks, crop/scale/composite, sampling, advanced text, frame trimming, and regional-input keys.
- `WorkflowGeneratorSteps.cs` migrates tiling, masks, noise, unsampling, blending, reference, video, Anima, SAM3, model-only loading, segmentation, media helpers, and integer-math keys.

Only C# property-name expressions change. Node class values, graph IDs, reserved IDs, `JObject` property order, input values, output references, conditions, feature gates, priorities, and errors remain unchanged.

## Dynamic Input Builders

Four maintained nodes use input objects assembled separately from the `CreateNode` call:

- `ImageNoise` uses its node-scoped constants for `image`, `amount`, `seed`, and optional `mask`.
- `AnimaLLLite` uses its node-scoped constants for model, model name, control image, strength, percentage bounds, and optional mask.
- `KSampler` uses its node-scoped constants for every schema-backed key. The same object may still be passed to the upstream `KSamplerAdvanced` fallback, with identical string values and branching. `control_after_generate` remains literal.
- `AttentionCouple` uses constants for fixed keys and interpolates the unchanged one-based integer onto `ConditionPrefix` and `MaskPrefix`.

The dynamically selected composite builder uses `ImageCompositeMaskedColorCorrecting` constants for the six schema-backed keys. The same object may target the upstream composite node; identical string values preserve compatibility. Its upstream-only `resize_source` key remains literal.

## Runtime Data Flow

### Workflow Construction

1. Existing generator logic chooses a Swarm node and constructs its existing `JObject`.
2. A compile-time constant supplies the same property-name string previously supplied by a literal.
3. Newtonsoft.Json retains the same property order and serialized names.
4. Comfy resolves the same class type and passes the same input values to Python.

### Object-Info Interpretation

1. Comfy supplies its runtime object-info document.
2. Existing discovery code selects a node through `ComfyNodeNames`.
3. The corresponding `ComfyNodeInputNames` constant selects the same required input list.
4. Existing parameter/model-list behavior continues unchanged.

### Extension Consumption

Extensions may use the public constants when constructing or interpreting Swarm-owned nodes. The catalog does not promise that an optional node is installed; extensions must continue using object-info or existing feature detection for availability.

## Migration Stages

### Stage 1: Input Name Catalog

Add the complete 46-node, 184-entry catalog. Compare every entry with its maintained Python schema and verify the five compatibility extras remain excluded.

### Stage 2: Backend and Direct API Consumers

Migrate object-info lookups, model trackers, and the direct LoRA-extraction workflow. Preserve transport envelope keys and undeclared compatibility extras.

### Stage 3: Model and Media Helpers

Migrate `WorkflowGeneratorModelSupport.cs` and `WGNodeData.cs`, preserving all input sequence and output wiring.

### Stage 4: Core Workflow Generator

Migrate `WorkflowGenerator.cs`, including dynamic KSampler, composite, and Attention Couple builders. Review the shared upstream-fallback paths independently.

### Stage 5: Workflow Steps

Migrate `WorkflowGeneratorSteps.cs`, including dynamic ImageNoise and Anima builders.

### Stage 6: Whole-Surface Audit

Compare pre/post effective node/key manifests, remaining literals in known Swarm contexts, direct JSON, object-info lookups, changed-file scope, documentation, and representative workflow paths.

## Static Verification

Repository policy prohibits agent-run builds, tests, the live server, browser automation, and Comfy execution. Static verification consists of:

- confirming exactly 46 nested node classes and 184 documented entries;
- confirming every complete input-name value exists in the corresponding maintained Python `required` or `optional` schema, including modern `define_schema` nodes;
- confirming the two Attention Couple prefixes generate the existing one-based schema names;
- confirming the five undeclared compatibility extras are absent from the catalog and unchanged at their call sites;
- comparing pre/post node/member occurrence counts for every migrated key;
- normalizing constants back to their values and comparing effective `JObject` key sequences, dynamic input assignments, object-info lookups, and model trackers;
- confirming direct workflow envelope keys, node IDs, property order, input values, outputs, feature gates, and errors remain unchanged;
- auditing remaining literals only in known Swarm-node input contexts rather than mechanically replacing generic upstream keys;
- checking public XML documentation, C# conventions, whitespace, commit scope, and preservation of unrelated maintainer changes;
- independent specification and code-quality review for each migration stage and the complete range.

## Manual Validation

The maintainer will validate:

1. Build and launch, common-node connection, object-info parsing, and feature/model lists.
2. Startup and capability behavior with optional ClipSeg, background removal, YOLO, SAM3, and animation nodes present and absent.
3. Standard and Swarm sampling, variation seed, Detail Daemon, previews, metadata, and final image saving.
4. Base64 image/audio/video inputs, init images, masks, crop/scale/composite, regional prompting, and tiling.
5. SAM3 points/boxes/post-processing, ClipSeg, YOLO, background removal, mask blur/grow/threshold, and overlap handling.
6. Offset latents, model-only loading, audio VAE loading, model tracking, references, unsampling, and latent blending.
7. Frame counting, trimming, FPS resampling, boomerang, audio attachment, image saving, and animation saving.
8. LoRA extraction through the Web API, confirming the existing compatibility extras and output remain unchanged.
9. Existing error behavior when optional or core Swarm nodes are unavailable.

## Non-Goals

- No Python source or schema changes.
- No catalog entries for Python-only or C#-unused inputs.
- No catalog entries for upstream, third-party, workflow-envelope, or undeclared compatibility keys.
- No removal or reinterpretation of the five undeclared compatibility extras.
- No runtime schema comparison, warning, rejection, or validation.
- No object-info authority or optional-node availability change.
- No class-name, capability-map, feature-ID, or public-field change.
- No generated-source tooling, reflection, typed workflow builder, or node descriptor objects.
- No workflow-generator decomposition or behavior cleanup.
- No external-extension migration.
- No build, automated test, live-server, browser, or Comfy execution by agents.

## Success Criteria

- `ComfyNodeInputNames` contains exactly the 184 approved schema-backed entries under 46 node-scoped classes.
- Every cataloged complete key matches the corresponding maintained Python schema, and numbered Attention Couple keys retain their exact format.
- Every approved built-in C# reference to those keys uses the matching public constant.
- The five undeclared compatibility extras remain unchanged literals and are not represented as Python schema contracts.
- Generated workflows, direct workflow JSON, object-info interpretation, model tracking, errors, and optional-node behavior remain unchanged.
- Python and object-info remain authoritative for runtime schema and availability.
- Static verification and independent reviews pass.
- The maintainer completes the manual validation matrix without regression.
