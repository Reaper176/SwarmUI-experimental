# Comfy Node Contract Catalog Design

## Goal

Centralize the class-name contracts for every Swarm-maintained Comfy node and the existing node-to-feature mapping without changing generated workflows, capability detection, public extension state, Python registration, object-info authority, or runtime behavior.

This is roadmap item 4 from the maintainability architecture audit. The project establishes the class-name foundation first; input-key contracts and runtime catalog validation remain separate follow-up projects.

## Current State

SwarmUI's maintained Python packages register 65 `Swarm*` node IDs through `NODE_CLASS_MAPPINGS`. C# repeats 46 of those IDs as string literals across:

- `ComfyUIBackendExtension.cs` capability detection and object-info reads;
- `ComfyUIAPIAbstractBackend.cs` model tracking and sampler detection;
- `ComfyUIWebAPI.cs` direct workflow JSON;
- `WorkflowGenerator.cs`;
- `WorkflowGeneratorSteps.cs`;
- `WorkflowGeneratorModelSupport.cs`;
- `WGNodeData.cs`.

Nineteen registered Python node IDs currently have no maintained C# literal consumer. One C# string beginning with `Swarm`, `SwarmReadableErrorException`, names an exception type rather than a Comfy node and must not enter the catalog.

`ComfyUIBackendExtension.NodeToFeatureMap` is a public mutable `Dictionary<string, string>` containing both Swarm-maintained and third-party node names. Extension and maintained code may mutate or replace that field. `AssignValuesFromRaw` uses the current dictionary to add detected features and remove them from the presumptive-discard set. This public field identity and runtime mutability are compatibility requirements.

Python object-info is the authoritative runtime report of which nodes and inputs are actually available. Common-package nodes, optionally imported nodes such as ClipSeg, and Extra-package nodes with dependency-sensitive imports do not all have identical availability.

## Selected Architecture

Add two static classes in `src/BuiltinExtensions/ComfyUIBackend`:

- `ComfyNodeNames` owns documented string constants for all 65 Swarm-maintained Python registration IDs.
- `ComfyCapabilityCatalog` owns construction of the existing default node-to-feature mapping and the focused operation that applies a detected node name to the current feature sets.

The classes use the existing `SwarmUI.Builtin_ComfyUIBackend` namespace and do not introduce services, dependency injection, generated source, reflection, or runtime file parsing.

Every maintained C# literal that names a registered Swarm node is replaced with the corresponding `ComfyNodeNames` constant. The replacement changes only the expression supplying the string; graph node types, JSON, lookup keys, and execution order remain identical.

Python files remain unchanged and continue to define the runtime node mappings.

## Node Name Catalog

`ComfyNodeNames` declares one `public const string` per registered ID. C# member names are the exact node IDs without the leading `Swarm` prefix, preserving established acronym casing. For example:

```csharp
/// <summary>Comfy class name for the Swarm base64 image-loading node.</summary>
public const string LoadImageB64 = "SwarmLoadImageB64";
```

The complete catalog values are:

- `SwarmAddSaveMetadataWS`
- `SwarmAnimaLLLite`
- `SwarmAttentionCouple`
- `SwarmCleanOverlapMasks`
- `SwarmCleanOverlapMasksExceptSelf`
- `SwarmClipSeg`
- `SwarmClipTextEncodeAdvanced`
- `SwarmCountFrames`
- `SwarmDebugAudio`
- `SwarmDetailDaemonOptions`
- `SwarmEmbedLoaderListProvider`
- `SwarmEnsureAudio`
- `SwarmExcludeFromMask`
- `SwarmExtractLora`
- `SwarmImageCompositeMaskedColorCorrecting`
- `SwarmImageCrop`
- `SwarmImageHeight`
- `SwarmImageNoise`
- `SwarmImageScaleForMP`
- `SwarmImageWidth`
- `SwarmInputAudio`
- `SwarmInputBoolean`
- `SwarmInputCheckpoint`
- `SwarmInputDropdown`
- `SwarmInputFloat`
- `SwarmInputGroup`
- `SwarmInputImage`
- `SwarmInputInteger`
- `SwarmInputModelName`
- `SwarmInputText`
- `SwarmInputVideo`
- `SwarmIntAdd`
- `SwarmJustLoadTheModelPlease`
- `SwarmKSampler`
- `SwarmLTXVAudioVAELoader`
- `SwarmLatentBlendMasked`
- `SwarmLoadAudioB64`
- `SwarmLoadImageB64`
- `SwarmLoadVideoB64`
- `SwarmLoraLoader`
- `SwarmMaskBlur`
- `SwarmMaskBounds`
- `SwarmMaskGrow`
- `SwarmMaskThreshold`
- `SwarmModelTiling`
- `SwarmOffsetEmptyLatentImage`
- `SwarmOverMergeMasksForOverlapFix`
- `SwarmReferenceOnly`
- `SwarmRemBg`
- `SwarmSam2BBoxFromJson`
- `SwarmSam2MaskPostProcess`
- `SwarmSam3BBoxFromJson`
- `SwarmSam3MaskPostProcess`
- `SwarmSam3PointsFromJson`
- `SwarmSaveAnimatedWebpWS`
- `SwarmSaveAnimationWS`
- `SwarmSaveImageWS`
- `SwarmSquareMaskFromPercent`
- `SwarmTileableVAE`
- `SwarmTrimFrames`
- `SwarmUnsampler`
- `SwarmVideoBoomerang`
- `SwarmVideoResampleFPS`
- `SwarmWorkflowDescription`
- `SwarmYoloDetection`

The constants are grouped by functional region in the source file for navigation, but each value is unique and authoritative within C#. No collections exposed by `ComfyNodeNames` imply availability; object-info retains that responsibility.

## Capability Catalog

`ComfyCapabilityCatalog.CreateNodeToFeatureMap()` returns a new `Dictionary<string, string>` containing every existing entry and no additions. Swarm-owned keys use `ComfyNodeNames` constants. Third-party node keys remain exact string literals because this project does not create a general upstream-node catalog.

`ComfyUIBackendExtension` retains this exact public field declaration and mutability surface:

```csharp
public static Dictionary<string, string> NodeToFeatureMap = ComfyCapabilityCatalog.CreateNodeToFeatureMap();
```

The field is not replaced with a property, made read-only, wrapped, copied after initialization, or moved to another declaring type. External code can continue adding entries, removing entries, mutating values, or assigning a replacement dictionary.

`ComfyCapabilityCatalog.ApplyDetectedNodeFeature` receives:

- the detected node name;
- the current `NodeToFeatureMap` dictionary;
- the current `FeaturesSupported` set;
- the current `FeaturesDiscardIfNotFound` set.

It performs the existing operation only: when the dictionary maps the node, add its feature ID to `FeaturesSupported` and remove the same ID from `FeaturesDiscardIfNotFound`. It returns no alternate state, catches no exceptions, logs nothing, and does not inspect object-info schemas.

`AssignValuesFromRaw` continues iterating raw object-info in its existing order and calls this operation at the current point in that loop. Preprocessor discovery, input-list extraction, hook scheduling detection, raw parser callbacks, and final presumptive-feature removal remain in `ComfyUIBackendExtension`.

## C# Consumer Migration

All maintained C# node literals whose values occur in the catalog migrate to constants.

### Backend and Capability Consumers

- `ComfyUIBackendExtension.NodeToFeatureMap` uses catalog constants for Swarm-owned entries.
- Object-info input lookups for `SwarmKSampler` and `SwarmYoloDetection` use catalog constants while input-key strings remain literal.
- `ComfyUIAPIAbstractBackend` model trackers and node-type checks use catalog constants.
- `ComfyUIWebAPI` uses the catalog constant in the direct LoRA-extraction workflow JSON.

### Workflow Generation Consumers

- `WorkflowGenerator.cs` migrates Swarm-owned media loading, masking, image processing, conditioning, and sampling node-class arguments.
- `WorkflowGeneratorSteps.cs` migrates all Swarm-owned step node-class arguments, including tiling, segmentation, masking, SAM helpers, detail/sampling, video, and audio helpers.
- `WorkflowGeneratorModelSupport.cs` migrates Swarm-owned latent/model-support node-class arguments.
- `WGNodeData.cs` migrates Swarm-owned decode/save/media helper node-class arguments.

Only the node-class expression changes. `CreateNode` IDs, reserved IDs, input `JObject` construction and property order, output references, conditions, step priorities, and feature gates remain unchanged.

`SwarmReadableErrorException` remains unchanged as a type and string where applicable. A final literal audit distinguishes it from node-class strings rather than forcing it into `ComfyNodeNames`.

## Runtime Data Flow

### Workflow Generation

1. A workflow generator path selects an existing Swarm node.
2. The call passes a `ComfyNodeNames` constant instead of an inline string.
3. `CreateNode` writes the constant's unchanged value to `class_type`.
4. Comfy resolves that value against Python's runtime `NODE_CLASS_MAPPINGS` exactly as before.

### Capability Detection

1. Comfy object-info supplies the available node names.
2. `AssignValuesFromRaw` iterates the same object-info entries in the same order.
3. It calls `ComfyCapabilityCatalog.ApplyDetectedNodeFeature` with the current public mapping and feature sets.
4. The catalog performs the existing dictionary lookup, feature addition, and discard removal.
5. The extension completes all other existing object-info parsing and removes still-unconfirmed presumptive features.

### Extension Customization

1. Maintained or extension code reads, mutates, or replaces `ComfyUIBackendExtension.NodeToFeatureMap` as before.
2. Later capability detection passes that current dictionary instance to `ApplyDetectedNodeFeature`.
3. Custom entries therefore participate exactly as they do before extraction.

## Migration Stages

### Stage 1: Node Name Catalog

Create `ComfyNodeNames.cs` with all 65 documented constants. Compare its values against maintained Python mapping keys, including aliases and optionally imported nodes. Do not change consumers in this stage.

### Stage 2: Capability Catalog

Create `ComfyCapabilityCatalog.cs`, move the default mapping construction and detected-node feature operation, and initialize the legacy public `NodeToFeatureMap` field from the catalog. Preserve every mapping pair, the public field declaration, and mutation/reassignment behavior.

### Stage 3: Backend and Web API Consumers

Replace Swarm node-class literals in `ComfyUIBackendExtension`, `ComfyUIAPIAbstractBackend`, and `ComfyUIWebAPI`. Preserve object-info input keys, JSON property names, trackers, conditionals, and errors.

### Stage 4: Workflow Generator Consumers

Replace Swarm node-class literals in `WorkflowGenerator.cs`, `WorkflowGeneratorSteps.cs`, `WorkflowGeneratorModelSupport.cs`, and `WGNodeData.cs`. Partition this stage into reviewable commits by file or cohesive generator region if necessary, while keeping every intermediate source state coherent.

### Stage 5: Whole-Surface Audit

Compare catalog/Python manifests, remaining C# literals, feature-map entries, direct JSON workflows, node-call values, changed-file scope, and public compatibility state. Do not add runtime validation or input-key constants during cleanup.

## Static Verification

Repository policy prohibits agents from running builds, tests, the live server, browser automation, or Comfy execution. Static verification therefore consists of:

- extracting all maintained Python `NODE_CLASS_MAPPINGS` keys and comparing them with the 65 catalog constant values;
- confirming optional/alias mapping keys are included without treating them as always available;
- comparing the complete pre/post `NodeToFeatureMap` node/feature manifest and entry count;
- confirming `NodeToFeatureMap` remains a public mutable field with the same declared type and name;
- confirming `ApplyDetectedNodeFeature` is called at the original point in object-info iteration with the current runtime field and feature sets;
- enumerating pre-refactor maintained C# Swarm-node literal occurrences and proving each now references the matching constant;
- confirming the only intentional non-node `Swarm*` C# name is `SwarmReadableErrorException`;
- comparing effective `CreateNode`, direct `class_type`, model-tracker, node-type, and object-info lookup values;
- tracing representative image, mask, sampling, model, SAM, audio, video, save, and LoRA-extraction paths;
- checking C# conventions, XML documentation, whitespace, staged scope, and preservation of unrelated maintainer changes;
- independent specification and code-quality review for each migration stage and the complete range.

## Manual Validation

The maintainer will validate the completed catalog migration in the live application:

1. Start with common nodes only and confirm Comfy connection, object-info parsing, feature flags, and useful missing-optional-node behavior.
2. Start with supported optional Extra nodes and confirm ClipSeg, background removal, YOLO, animation saving, and related feature detection.
3. Generate a still image with standard and Swarm sampling, variation seed, previews, metadata, and final save.
4. Exercise base64 image/audio/video inputs, init image, mask processing, regional/attention behavior, and tiling.
5. Exercise SAM/segmentation paths, including points, boxes, post-processing, blur/grow/threshold, and YOLO where available.
6. Exercise model-support paths using Swarm latent/model helpers, LoRAs, and model tracking.
7. Exercise animation/video/audio trimming, frame counts, resampling, boomerang, decode, and save paths.
8. Exercise the LoRA extraction Web API workflow and confirm its direct `class_type` resolves.
9. Confirm all existing capability IDs and install/feature UI behavior remain unchanged when optional nodes are present or absent.

## Non-Goals

- No Python source or `NODE_CLASS_MAPPINGS` change.
- No input-key constant catalog or input-schema migration.
- No runtime object-info/catalog comparison, warning, rejection, or startup validation.
- No generated C# source or build tooling.
- No general catalog for upstream or third-party nodes.
- No change to feature IDs, presumptive feature sets, optional dependency handling, install detection, or object-info authority.
- No change to generated graph nodes, inputs, IDs, order, or conditions.
- No workflow-generator decomposition beyond replacing node-class expressions.
- No removal, property conversion, or encapsulation of `ComfyUIBackendExtension.NodeToFeatureMap`.
- No build, test, live-server, browser, or Comfy execution by agents.

## Success Criteria

- `ComfyNodeNames` contains exactly the 65 maintained Python registration IDs with documented constants and no non-node entries.
- Every registered Swarm-node reference in the approved built-in Comfy backend consumer surface uses the matching catalog constant.
- `ComfyCapabilityCatalog` owns the unchanged default node-to-feature mapping and detected-node feature operation.
- `ComfyUIBackendExtension.NodeToFeatureMap` retains its public mutable field compatibility and initializes from a new catalog dictionary.
- Python object-info remains runtime authority for actual node and input availability.
- Generated workflows, direct workflow JSON, capability results, feature IDs, errors, and optional-node behavior remain unchanged.
- Static manifest checks and independent reviews pass.
- The maintainer completes the manual validation matrix without regression.
