# Immutable Per-Backend Comfy Capabilities Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Publish immutable backend-local Comfy feature/node snapshots so selection and workflow behavior never borrow another backend's discovery, while preserving the existing mutable extension fields and global aggregate.

**Architecture:** `ComfyCapabilityCatalog` becomes a pure interpreter, and a new `ComfyCapabilityRegistry` stores last-good discovery evidence per local or linked-remote backend owner. Local and remote discovery build complete candidates outside the global lock, then publish immutable snapshots and same-object compatibility mirrors under `ValueAssignmentLocker`. Maintained readers use one backend snapshot or a stable aggregate copy; direct extension collection mutations are reconciled as compatibility overrides.

**Tech Stack:** C# 12, .NET 8 `System.Collections.Frozen`, Newtonsoft.Json, FreneticUtilities `LockObject`, SwarmUI backend lifecycle and built-in Comfy extension code.

**Repository constraints:** Work directly on `master`; do not create a worktree. Do not build, launch, or run tests. Use source inspection, targeted `rg`, `git diff --check`, and maintainer-run live validation. Never stage the maintainer's existing changes in `src/Data/Settings.fds`, `src/Pages/Text2Image.cshtml`, `src/wwwroot/js/genpage/gentab/loras.js`, or `src/wwwroot/js/genpage/main.js`.

**Design:** `docs/superpowers/specs/2026-07-25-immutable-per-backend-comfy-capabilities-design.md`

---

## Planned File Responsibilities

- `src/BuiltinExtensions/ComfyUIBackend/ComfyBackendCapabilitySnapshot.cs`: immutable feature/node/folder publication value.
- `src/BuiltinExtensions/ComfyUIBackend/ComfyCapabilityCatalog.cs`: pure node-to-feature interpretation with no process-wide mutation.
- `src/BuiltinExtensions/ComfyUIBackend/ComfyCapabilityRegistry.cs`: compatibility override tracking, per-owner last-good evidence, candidate-first registry publication, stable aggregate copies, and owner removal.
- `src/BuiltinExtensions/ComfyUIBackend/ComfyUIBackendExtension.cs`: established public compatibility fields, shared object-info parsing, registry initialization, local/remote publication facades, parser callbacks, and linked-remote refresh hooks.
- `src/BuiltinExtensions/ComfyUIBackend/ComfyUIAPIAbstractBackend.cs`: serialized object-info fetch, candidate construction, local snapshot publication, last-good behavior, and snapshot-based generation/model loading/validation.
- `src/BuiltinExtensions/ComfyUIBackend/ComfyUIWebAPI.cs`: snapshot-based generated workflow, node-list, and folder-format reads.
- `src/BuiltinExtensions/ComfyUIBackend/ComfyUser.cs`: snapshot-based direct-workflow node filtering and prompt path fixup.
- `src/Backends/SwarmSwarmBackend.cs`: immutable remote feature publication while retaining `RemoteFeatureCombo`, plus linked-owner lifecycle events.
- `src/Backends/BackendHandler.cs`: generic post-removal notification after the backend leaves `AllBackends`.
- `src/Text2Image/T2IEngine.cs`: retain backend-local matching and verify it consumes one stable enumerable.
- `src/WebAPI/BackendAPI.cs`: serialize one stable per-backend feature snapshot.
- `docs/superpowers/specs/2026-07-25-immutable-per-backend-comfy-capabilities-design.md`: implementation record and maintainer-validation status.
- `docs/superpowers/audits/2026-07-21-maintainability-architecture-refresh.md`: update Comfy F22/rank 8 only after implementation evidence exists.

---

### Task 1: Freeze the Capability Inventory and Compatibility Contract

**Files:**
- Inspect: `src/BuiltinExtensions/ComfyUIBackend/**/*.cs`
- Inspect: `src/Backends/AbstractBackend.cs`
- Inspect: `src/Backends/BackendHandler.cs`
- Inspect: `src/Backends/SwarmSwarmBackend.cs`
- Inspect: `src/Text2Image/T2IEngine.cs`
- Inspect: `src/WebAPI/BackendAPI.cs`
- Record implementation in: `docs/superpowers/specs/2026-07-25-immutable-per-backend-comfy-capabilities-design.md`

- [ ] **Step 1: Capture every compatibility-field reader and writer**

Run:

```bash
rg -n 'FeaturesSupported|FeaturesDiscardIfNotFound|NodeToFeatureMap|RawObjectInfoParsers|ValueAssignmentLocker|AssignValuesFromRaw' \
  src/BuiltinExtensions/ComfyUIBackend \
  --glob '*.cs'
```

Expected baseline:

- `FeaturesSupported`, `FeaturesDiscardIfNotFound`, and `NodeToFeatureMap` are declared in `ComfyUIBackendExtension.cs`;
- local install hints add to both feature sets during `OnPreInit`;
- `AssignValuesFromRaw` owns built-in capability mutations and parser callback dispatch;
- local and linked-remote object-info paths call `AssignValuesFromRaw`;
- `LoadModel` directly checks the global set; and
- local `SupportedFeatures` returns the global set with a folder flag.

Classify every line as public compatibility declaration, startup hint writer, object-info writer, parser surface, backend-local reader, or aggregate reader.

- [ ] **Step 2: Capture every backend feature consumer**

Run:

```bash
rg -n '\bSupportedFeatures\b|GetAllSupportedFeatures' \
  src/Backends src/Core src/Text2Image src/WebAPI src/BuiltinExtensions \
  --glob '*.cs' \
  --glob '!src/Extensions/**' \
  --glob '!src/bin/**' \
  --glob '!src/obj/**'
```

Expected maintained decision consumers include:

- `BackendHandler.GetAllSupportedFeatures`;
- `T2IEngine.BackendMatcherFor`;
- `BackendAPI` serialization;
- `ComfyUIAPIAbstractBackend` workflow and model-load paths;
- `ComfyUIWebAPI.ComfyGetGeneratedWorkflow`;
- `ComfyUser` folder fixup; and
- the local and remote backend getters.

Record whether each consumer needs one backend snapshot or the eligible-backend aggregate.

- [ ] **Step 3: Capture node and remote publication consumers**

Run:

```bash
rg -n '\bNodeTypes\b|ComfyNodeTypes|RemoteFeatureCombo|ReviseRemotesEvent|OnSwarmBackendAdded' \
  src/BuiltinExtensions/ComfyUIBackend src/Backends \
  --glob '*.cs'
```

Expected baseline:

- local `NodeTypes` is replaced before global capability parsing;
- `ComfyGetNodeTypesForBackend`, `TryIsValid`, and `ComfyUser` read mutable node sets;
- linked remotes publish node types through `ExtensionData`;
- `RemoteFeatureCombo` is mirrored with separate add and remove loops; and
- linked-remote add/revise hooks fetch object info independently.

- [ ] **Step 4: Capture lifecycle and protected-file state**

Run:

```bash
rg -n 'AddNewNonrealBackend|DeleteById|EditById|ReloadBackend|ShutdownBackendCleanly' src/Backends/BackendHandler.cs
git status --short --untracked-files=no
git diff --cached --name-only
```

Expected: deletion removes from `AllBackends` before shutdown; reload/edit retains the registered backend object. The four protected maintainer files may remain modified, and none is staged.

No commit is needed for this inspection-only task.

---

### Task 2: Add the Immutable Snapshot and Pure Interpreter

**Files:**
- Create: `src/BuiltinExtensions/ComfyUIBackend/ComfyBackendCapabilitySnapshot.cs`
- Modify: `src/BuiltinExtensions/ComfyUIBackend/ComfyCapabilityCatalog.cs`

- [ ] **Step 1: Add the immutable snapshot value**

Create `ComfyBackendCapabilitySnapshot.cs`:

```csharp
using System.Collections.Frozen;

namespace SwarmUI.Builtin_ComfyUIBackend;

/// <summary>Immutable capability and node discovery published for one Comfy backend.</summary>
public sealed class ComfyBackendCapabilitySnapshot
{
    /// <summary>Empty snapshot used before a backend has completed its first successful discovery.</summary>
    public static ComfyBackendCapabilitySnapshot Empty { get; } = new([], [], "/", 0);

    /// <summary>Feature IDs supported by this backend, including its folder-separator feature.</summary>
    public FrozenSet<string> Features { get; }

    /// <summary>Comfy node class IDs present in this backend's object-info document.</summary>
    public FrozenSet<string> NodeTypes { get; }

    /// <summary>Path separator used by model names reported by this backend.</summary>
    public string ModelFolderFormat { get; }

    /// <summary>Monotonic in-process publication generation.</summary>
    public long Generation { get; }

    /// <summary>Constructs one immutable capability snapshot from complete candidate values.</summary>
    public ComfyBackendCapabilitySnapshot(IEnumerable<string> features, IEnumerable<string> nodeTypes, string modelFolderFormat, long generation)
    {
        Features = features.ToFrozenSet();
        NodeTypes = nodeTypes.ToFrozenSet();
        ModelFolderFormat = modelFolderFormat;
        Generation = generation;
    }
}
```

- [ ] **Step 2: Replace the mutating node helper with a pure interpreter**

Keep `CreateNodeToFeatureMap()` byte-for-byte equivalent, remove `ApplyDetectedNodeFeature`, and add:

```csharp
/// <summary>Builds the complete feature set for one Comfy backend from its own node discovery and compatibility inputs.</summary>
public static HashSet<string> Interpret(
    IReadOnlySet<string> nodeTypes,
    IEnumerable<string> baselineFeatures,
    IReadOnlySet<string> discardIfNotFound,
    IReadOnlyDictionary<string, string> nodeToFeatureMap,
    IReadOnlySet<string> suppressedFeatures,
    string modelFolderFormat)
{
    HashSet<string> result = [.. baselineFeatures];
    HashSet<string> unresolvedDiscard = [.. discardIfNotFound];
    foreach (string nodeType in nodeTypes)
    {
        if (nodeToFeatureMap.TryGetValue(nodeType, out string featureId))
        {
            result.Add(featureId);
            unresolvedDiscard.Remove(featureId);
        }
    }
    foreach (string featureId in unresolvedDiscard)
    {
        result.Remove(featureId);
    }
    string hookFeature = "hook_lora_scheduling";
    string interpolatedHookFeature = "hook_lora_interpolated_scheduling";
    string[] requiredHookNodes = ["CreateHookLora", "CreateHookKeyframe", "SetHookKeyframes", "SetClipHooks"];
    if (requiredHookNodes.All(nodeTypes.Contains))
    {
        result.Add(hookFeature);
        if (nodeTypes.Contains("CreateHookKeyframesInterpolated"))
        {
            result.Add(interpolatedHookFeature);
        }
        else
        {
            result.Remove(interpolatedHookFeature);
        }
    }
    else
    {
        result.Remove(hookFeature);
        result.Remove(interpolatedHookFeature);
    }
    result.ExceptWith(suppressedFeatures);
    result.Remove("folderbackslash");
    result.Remove("folderslash");
    result.Add(modelFolderFormat == "\\" ? "folderbackslash" : "folderslash");
    return result;
}
```

- [ ] **Step 3: Verify purity and mapping parity**

Run:

```bash
rg -n 'FeaturesSupported|FeaturesDiscardIfNotFound' src/BuiltinExtensions/ComfyUIBackend/ComfyCapabilityCatalog.cs
rg -n 'LoadImageB64|SaveImageWS|JustLoadTheModelPlease|LatentBlendMasked|KSampler|YoloDetection|TeaCache|INPAINT_' \
  src/BuiltinExtensions/ComfyUIBackend/ComfyCapabilityCatalog.cs
git diff --check
```

Expected: the catalog has no process-wide feature-set reference; every existing mapping remains; no whitespace errors.

- [ ] **Step 4: Commit the snapshot and interpreter**

```bash
git add \
  src/BuiltinExtensions/ComfyUIBackend/ComfyBackendCapabilitySnapshot.cs \
  src/BuiltinExtensions/ComfyUIBackend/ComfyCapabilityCatalog.cs
git commit -m "refactor: define Comfy capability snapshots"
```

---

### Task 3: Add the Compatibility-Aware Registry

**Files:**
- Create: `src/BuiltinExtensions/ComfyUIBackend/ComfyCapabilityRegistry.cs`
- Modify: `src/BuiltinExtensions/ComfyUIBackend/ComfyUIBackendExtension.cs`

- [ ] **Step 1: Define per-owner evidence and registry state**

Create `ComfyCapabilityRegistry.cs` with a private entry that stores only complete discovery evidence:

```csharp
using System.Collections.Frozen;

namespace SwarmUI.Builtin_ComfyUIBackend;

/// <summary>Coordinates immutable per-backend Comfy capabilities and the legacy global aggregate.</summary>
public static class ComfyCapabilityRegistry
{
    /// <summary>Complete discovery evidence retained for one registered backend owner.</summary>
    internal sealed record CapabilityEntry(FrozenSet<string> NodeTypes, string ModelFolderFormat, ComfyBackendCapabilitySnapshot Snapshot);

    /// <summary>Backend-owner identity mapped to its complete last-good discovery evidence.</summary>
    private static readonly Dictionary<object, CapabilityEntry> Entries = new(ReferenceEqualityComparer.Instance);

    /// <summary>Feature values present when compatibility tracking initializes.</summary>
    private static HashSet<string> InitialFeatures = [];

    /// <summary>Direct compatibility additions detected after initialization.</summary>
    private static HashSet<string> ForcedFeatures = [];

    /// <summary>Direct compatibility removals detected after initialization.</summary>
    private static HashSet<string> SuppressedFeatures = [];

    /// <summary>Last aggregate written by maintained code, used to distinguish external mutation.</summary>
    private static HashSet<string> LastPublishedAggregate = [];

    /// <summary>Last observed discard-hint set.</summary>
    private static HashSet<string> LastDiscardHints = [];

    /// <summary>Last observed node-to-feature map.</summary>
    private static Dictionary<string, string> LastNodeMap = [];

    /// <summary>Monotonic snapshot generation.</summary>
    private static long NextGeneration;

    /// <summary>Whether startup compatibility state has been captured.</summary>
    private static bool IsInitialized;
```

All fields receive XML documentation, and no `var` is used.

- [ ] **Step 2: Initialize and reconcile compatibility state**

Add `Initialize()` and a lock-required reconciliation helper:

```csharp
/// <summary>Captures the established public compatibility collections after built-in startup hints are applied.</summary>
public static void Initialize()
{
    lock (ComfyUIBackendExtension.ValueAssignmentLocker)
    {
        if (IsInitialized)
        {
            return;
        }
        InitialFeatures = [.. ComfyUIBackendExtension.FeaturesSupported];
        LastPublishedAggregate = [.. ComfyUIBackendExtension.FeaturesSupported];
        LastDiscardHints = [.. ComfyUIBackendExtension.FeaturesDiscardIfNotFound];
        LastNodeMap = new(ComfyUIBackendExtension.NodeToFeatureMap);
        IsInitialized = true;
    }
}

/// <summary>Captures extension collection mutations and returns whether snapshots need reevaluation.</summary>
private static bool CaptureCompatibilityChanges()
{
    bool changed = false;
    foreach (string feature in ComfyUIBackendExtension.FeaturesSupported.Except(LastPublishedAggregate))
    {
        ForcedFeatures.Add(feature);
        SuppressedFeatures.Remove(feature);
        changed = true;
    }
    foreach (string feature in LastPublishedAggregate.Except(ComfyUIBackendExtension.FeaturesSupported))
    {
        SuppressedFeatures.Add(feature);
        ForcedFeatures.Remove(feature);
        changed = true;
    }
    if (!LastDiscardHints.SetEquals(ComfyUIBackendExtension.FeaturesDiscardIfNotFound))
    {
        LastDiscardHints = [.. ComfyUIBackendExtension.FeaturesDiscardIfNotFound];
        changed = true;
    }
    if (LastNodeMap.Count != ComfyUIBackendExtension.NodeToFeatureMap.Count
        || LastNodeMap.Any(pair => !ComfyUIBackendExtension.NodeToFeatureMap.TryGetValue(pair.Key, out string value) || value != pair.Value))
    {
        LastNodeMap = new(ComfyUIBackendExtension.NodeToFeatureMap);
        changed = true;
    }
    return changed;
}
```

Call `ComfyCapabilityRegistry.Initialize()` in `OnPreInit` immediately after the built-in directory-hint mutations and before backend discovery can publish.

- [ ] **Step 3: Build registry candidates before committing**

Add a private `BuildCandidates` helper that:

1. builds `baselineFeatures` from `InitialFeatures` and `ForcedFeatures`, then removes `SuppressedFeatures`;
2. interprets every entry from its own `NodeTypes` and `ModelFolderFormat`;
3. assigns one new generation per candidate snapshot;
4. builds the global aggregate from the same baseline with unresolved discard hints removed;
5. unions entry features while excluding `folderbackslash` and `folderslash`; and
6. returns candidate dictionaries without mutating `Entries` or public fields.

Use this exact result shape:

```csharp
/// <summary>Complete candidate registry and aggregate produced before publication.</summary>
internal sealed record RegistryCandidate(Dictionary<object, CapabilityEntry> Entries, HashSet<string> Aggregate);
```

The aggregate base must be interpreted with an empty node set and no folder flag, rather than directly unioning presumptive features.

- [ ] **Step 4: Add publication, retrieval, and removal APIs**

Expose:

```csharp
/// <summary>Publishes complete discovery evidence for one backend owner and returns its immutable snapshot.</summary>
public static ComfyBackendCapabilitySnapshot Publish(object owner, IEnumerable<string> nodeTypes, string modelFolderFormat)

/// <summary>Returns the current immutable snapshot for one backend owner after reconciling compatibility mutations.</summary>
public static ComfyBackendCapabilitySnapshot GetSnapshot(object owner)

/// <summary>Returns a stable copy of the legacy Comfy aggregate after reconciling compatibility mutations.</summary>
public static FrozenSet<string> GetAggregateSnapshot()

/// <summary>Removes one deleted backend owner and republishes the remaining aggregate.</summary>
public static void Remove(object owner)
```

Each public method locks `ValueAssignmentLocker`, calls `Initialize()` if needed, captures compatibility changes, builds a complete `RegistryCandidate`, and only then:

```csharp
Entries.Clear();
foreach ((object key, CapabilityEntry value) in candidate.Entries)
{
    Entries[key] = value;
}
ComfyUIBackendExtension.FeaturesSupported.Clear();
ComfyUIBackendExtension.FeaturesSupported.UnionWith(candidate.Aggregate);
LastPublishedAggregate = [.. candidate.Aggregate];
```

`GetSnapshot` returns `ComfyBackendCapabilitySnapshot.Empty` for an unknown owner. `Remove` is a no-op for an unknown owner. Do not reassign any public compatibility collection.

Also expose lock-required preparation and commit helpers so `ComfyUIBackendExtension` can include shared-value publication in the same outer critical section:

```csharp
/// <summary>Builds a complete candidate that adds or replaces one owner's discovery evidence.</summary>
internal static RegistryCandidate PreparePublish(object owner, IEnumerable<string> nodeTypes, string modelFolderFormat)

/// <summary>Commits one previously completed registry candidate and the same-object global aggregate.</summary>
internal static void Commit(RegistryCandidate candidate)
```

`PreparePublish` performs no public mutation. `Commit` contains only the `Entries` and same-object aggregate replacement shown above.

- [ ] **Step 5: Verify registry invariants**

Run:

```bash
rg -n 'FeaturesSupported\s*=|FeaturesDiscardIfNotFound\s*=|NodeToFeatureMap\s*=' \
  src/BuiltinExtensions/ComfyUIBackend
rg -n 'lock \\(ComfyUIBackendExtension.ValueAssignmentLocker\\)|Publish\\(|GetSnapshot\\(|GetAggregateSnapshot\\(|Remove\\(' \
  src/BuiltinExtensions/ComfyUIBackend/ComfyCapabilityRegistry.cs
git diff --check
```

Expected: only the three established declaration initializers assign those fields; registry operations are lock-owned and candidate-first.

- [ ] **Step 6: Commit the registry owner**

```bash
git add \
  src/BuiltinExtensions/ComfyUIBackend/ComfyCapabilityRegistry.cs \
  src/BuiltinExtensions/ComfyUIBackend/ComfyUIBackendExtension.cs
git commit -m "refactor: add Comfy capability registry"
```

---

### Task 4: Make Shared Object-Info Parsing Candidate-First

**Files:**
- Modify: `src/BuiltinExtensions/ComfyUIBackend/ComfyUIBackendExtension.cs`

- [ ] **Step 1: Separate shared-value parsing from capability interpretation**

Replace built-in feature mutation in `AssignValuesFromRaw` with a private shared-value candidate. The candidate owns copies of every maintained collection touched by object-info parsing:

```csharp
/// <summary>Complete candidate for aggregate Comfy dropdown and preprocessor values.</summary>
private sealed class SharedValueCandidate
{
    /// <summary>Candidate upscaler values.</summary>
    public List<string> UpscalerModels;
    /// <summary>Candidate sampler values.</summary>
    public List<string> Samplers;
    /// <summary>Candidate scheduler values.</summary>
    public List<string> Schedulers;
    /// <summary>Candidate IPAdapter model values.</summary>
    public List<string> IPAdapterModels;
    /// <summary>Candidate IPAdapter weight-type values.</summary>
    public List<string> IPAdapterWeightTypes;
    /// <summary>Candidate GLIGEN model values.</summary>
    public List<string> GligenModels;
    /// <summary>Candidate YOLO model values.</summary>
    public List<string> YoloModels;
    /// <summary>Candidate style-model values.</summary>
    public List<string> StyleModels;
    /// <summary>Candidate union-ControlNet values.</summary>
    public List<string> ControlnetUnionTypes;
    /// <summary>Candidate CLIP-device values.</summary>
    public List<string> SetClipDevices;
    /// <summary>Candidate ControlNet preprocessor values.</summary>
    public ConcurrentDictionary<string, JToken> ControlNetPreprocessors;
}
```

Create `BuildSharedValueCandidate(JObject rawObjectInfo)` from copies of the current published collections. Apply the existing `TryGetRequiredInputs`, warning, concatenation, model filtering, union-type, device, and preprocessor rules to the candidate fields in their current order.

- [ ] **Step 2: Publish shared values in one lock-owned block**

Add:

```csharp
/// <summary>Publishes a complete shared-value candidate without reassigning the preprocessor dictionary.</summary>
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
    ControlNetPreprocessors.Clear();
    foreach ((string key, JToken value) in candidate.ControlNetPreprocessors)
    {
        ControlNetPreprocessors[key] = value;
    }
}
```

List fields retain their established public types and values; `ControlNetPreprocessors` retains object identity.

- [ ] **Step 3: Preserve the public legacy facade and add owner-aware publication**

Keep:

```csharp
public static void AssignValuesFromRaw(JObject rawObjectInfo)
```

as a compatibility facade. It builds/publishes shared values, invokes parser callbacks in order with the existing per-callback `try/catch`, then reconciles the registry aggregate. It must not create a schedulable backend owner.

Add:

```csharp
/// <summary>Publishes shared values and immutable capability evidence for one backend owner.</summary>
public static ComfyBackendCapabilitySnapshot AssignValuesFromRaw(object owner, JObject rawObjectInfo, IReadOnlySet<string> nodeTypes, string modelFolderFormat)
```

The owner-aware overload:

1. builds the shared candidate before taking the lock;
2. enters `ValueAssignmentLocker`;
3. calls `ComfyCapabilityRegistry.PreparePublish` without mutating published state;
4. publishes the shared candidate;
5. commits the prepared capability candidate;
6. runs `RawObjectInfoParsers` in list order with isolated exceptions;
7. calls `ComfyCapabilityRegistry.GetSnapshot(owner)` so callback-driven compatibility mutations are reconciled before the lock is released; and
8. returns that final immutable snapshot.

Delete `DetectHookLoraSchedulingSupport`; that rule now belongs only to the pure catalog interpreter.

- [ ] **Step 4: Verify parser and public-surface compatibility**

Run:

```bash
rg -n 'public static void AssignValuesFromRaw\\(JObject|RawObjectInfoParsers|Error while running extension parsing' \
  src/BuiltinExtensions/ComfyUIBackend/ComfyUIBackendExtension.cs
rg -n 'FeaturesSupported\\.(Add|Remove)|ApplyDetectedNodeFeature|DetectHookLoraSchedulingSupport' \
  src/BuiltinExtensions/ComfyUIBackend/ComfyUIBackendExtension.cs
git diff --check
```

Expected: the legacy facade and callback error boundary remain; built-in object-info parsing no longer directly adds/removes global features.

- [ ] **Step 5: Commit candidate-first shared parsing**

```bash
git add src/BuiltinExtensions/ComfyUIBackend/ComfyUIBackendExtension.cs
git commit -m "refactor: stage Comfy object info parsing"
```

---

### Task 5: Publish and Consume Local Backend Snapshots

**Files:**
- Modify: `src/BuiltinExtensions/ComfyUIBackend/ComfyUIAPIAbstractBackend.cs`
- Modify: `src/BuiltinExtensions/ComfyUIBackend/ComfyUIWebAPI.cs`
- Modify: `src/BuiltinExtensions/ComfyUIBackend/ComfyUser.cs`

- [ ] **Step 1: Serialize local discovery and build candidates before publication**

Add to `ComfyUIAPIAbstractBackend`:

```csharp
/// <summary>Serializes object-info discovery for this backend.</summary>
private readonly SemaphoreSlim CapabilityRefreshGate = new(1, 1);

/// <summary>Returns this backend's current immutable capability snapshot.</summary>
public ComfyBackendCapabilitySnapshot CapabilitySnapshot => ComfyCapabilityRegistry.GetSnapshot(this);
```

Wrap the complete `LoadValueSet` body:

```csharp
await CapabilityRefreshGate.WaitAsync(Program.GlobalProgramCancel);
try
{
    // Existing fetch and candidate construction.
}
finally
{
    CapabilityRefreshGate.Release();
}
```

Do not assign `RawObjectInfo`, `NodeTypes`, `Models`, or `ModelFolderFormat` while constructing. Use explicit local candidates:

```csharp
JObject rawObjectInfoCandidate = result;
HashSet<string> nodeTypesCandidate = [.. rawObjectInfoCandidate.Properties().Select(property => property.Name)];
ConcurrentDictionary<string, List<string>> modelsCandidate = [];
string modelFolderFormatCandidate = firstBackSlash is not null ? "\\" : "/";
ComfyBackendCapabilitySnapshot capabilityCandidate = ComfyUIBackendExtension.AssignValuesFromRaw(
    this,
    rawObjectInfoCandidate,
    nodeTypesCandidate,
    modelFolderFormatCandidate);
```

Only after the owner-aware call succeeds, publish the compatibility mirrors:

```csharp
RawObjectInfo = rawObjectInfoCandidate;
NodeTypes = [.. capabilityCandidate.NodeTypes];
Models = modelsCandidate;
ModelFolderFormat = capabilityCandidate.ModelFolderFormat;
```

Remove the catch that logs an `AssignValuesFromRaw` failure and continues. A first-load interpretation failure must escape to normal initialization retry/error handling; a failure after a prior publication leaves the prior mirrors because no assignment occurred.

- [ ] **Step 2: Use one snapshot throughout generation and model loading**

At the start of each snapshot-dependent operation, capture:

```csharp
ComfyBackendCapabilitySnapshot capabilitySnapshot = CapabilitySnapshot;
```

Then replace:

```csharp
ModelFolderFormat
[.. SupportedFeatures]
ComfyUIBackendExtension.FeaturesSupported.Contains("comfy_just_load_model")
```

with:

```csharp
capabilitySnapshot.ModelFolderFormat
[.. capabilitySnapshot.Features]
capabilitySnapshot.Features.Contains("comfy_just_load_model")
```

Apply this to `GenerateLive`, `IsValidForThisBackend`, `LoadModel`, and any other local workflow construction returned by the Task 1 inventory. Change the shared validator signature to accept immutable node sets:

```csharp
public static bool TryIsValid(T2IParamInput input, IReadOnlySet<string> nodeTypes)
```

`SupportedFeatures` becomes:

```csharp
/// <inheritdoc/>
public override IEnumerable<string> SupportedFeatures => CapabilitySnapshot.Features;
```

- [ ] **Step 3: Migrate Comfy Web API and direct-user consumers**

In `ComfyGetGeneratedWorkflow`, capture one backend snapshot and use it for both folder format and workflow features:

```csharp
ComfyBackendCapabilitySnapshot capabilitySnapshot = backend.CapabilitySnapshot;
string flow = ComfyUIAPIAbstractBackend.CreateWorkflow(
    input,
    workflow => workflow,
    capabilitySnapshot.ModelFolderFormat,
    features: [.. capabilitySnapshot.Features]);
```

In `ComfyGetNodeTypesForBackend`, serialize `comfyBack.CapabilitySnapshot.NodeTypes`.

In `ComfyUser`, replace the local-backend `NodeTypes` cast/read with `CapabilitySnapshot.NodeTypes`, and capture one `SupportedFeatures` set before prompt path fixup. Do not alter queue choice, reservation, error, or WebSocket behavior.

- [ ] **Step 4: Verify no local decision reads the global set**

Run:

```bash
rg -n 'ComfyUIBackendExtension\\.FeaturesSupported|\\bNodeTypes\\b|ModelFolderFormat|\\bSupportedFeatures\\b' \
  src/BuiltinExtensions/ComfyUIBackend/ComfyUIAPIAbstractBackend.cs \
  src/BuiltinExtensions/ComfyUIBackend/ComfyUIWebAPI.cs \
  src/BuiltinExtensions/ComfyUIBackend/ComfyUser.cs
git diff --check
```

Expected:

- no local decision reads `ComfyUIBackendExtension.FeaturesSupported`;
- compatibility mirror declarations/assignments remain;
- maintained workflow decisions use one captured snapshot; and
- no whitespace errors.

- [ ] **Step 5: Commit local snapshot publication and readers**

```bash
git add \
  src/BuiltinExtensions/ComfyUIBackend/ComfyUIAPIAbstractBackend.cs \
  src/BuiltinExtensions/ComfyUIBackend/ComfyUIWebAPI.cs \
  src/BuiltinExtensions/ComfyUIBackend/ComfyUser.cs
git commit -m "refactor: publish local Comfy capabilities"
```

---

### Task 6: Publish Stable Remote Features and Linked Comfy Evidence

**Files:**
- Modify: `src/Backends/SwarmSwarmBackend.cs`
- Modify: `src/Backends/BackendHandler.cs`
- Modify: `src/BuiltinExtensions/ComfyUIBackend/ComfyUIBackendExtension.cs`
- Modify: `src/BuiltinExtensions/ComfyUIBackend/ComfyUser.cs`

- [ ] **Step 1: Add an immutable remote feature snapshot**

In `SwarmSwarmBackend`, add:

```csharp
using System.Collections.Frozen;

/// <summary>Immutable feature IDs from the last complete remote status response.</summary>
private volatile FrozenSet<string> RemoteFeatureSnapshot = Array.Empty<string>().ToFrozenSet();

/// <summary>Returns the immutable feature snapshot from the last complete remote status response.</summary>
public FrozenSet<string> GetRemoteFeatureSnapshot()
{
    return RemoteFeatureSnapshot;
}

/// <inheritdoc/>
public override IEnumerable<string> SupportedFeatures => GetRemoteFeatureSnapshot();
```

After `features` is fully built in `ReviseRemoteDataList`, publish:

```csharp
FrozenSet<string> featureCandidate = features.ToFrozenSet();
RemoteFeatureSnapshot = featureCandidate;
foreach (string feature in featureCandidate)
{
    RemoteFeatureCombo.TryAdd(feature, feature);
}
foreach (string feature in RemoteFeatureCombo.Keys.Where(feature => !featureCandidate.Contains(feature)))
{
    RemoteFeatureCombo.TryRemove(feature, out _);
}
```

The immutable reference changes before the compatibility mirror loops. Scheduling and serialization never read the loops.

- [ ] **Step 2: Add a post-removal lifecycle notification**

In `BackendHandler`, add:

```csharp
/// <summary>Subscribers notified after a backend has been removed from <see cref="AllBackends"/>.</summary>
public Action<BackendData> BackendRemovedEvent;
```

In `DeleteById`, invoke it after successful `TryRemove` and before shutdown:

```csharp
Action<BackendData> backendRemovedEvent = BackendRemovedEvent;
if (backendRemovedEvent is not null)
{
    foreach (Action<BackendData> subscriber in backendRemovedEvent.GetInvocationList().Cast<Action<BackendData>>())
    {
        try
        {
            subscriber(data);
        }
        catch (Exception ex)
        {
            Logs.Error($"Backend removal subscriber failed: {ex.ReadableString()}");
        }
    }
}
```

Subscriber failure must not prevent backend shutdown, persistence generation publication, or loaded-model reassignment.

- [ ] **Step 3: Serialize linked object-info refreshes**

In `ComfyUIBackendExtension`, add:

```csharp
/// <summary>Per-linked-backend gates that prevent stale object-info publication.</summary>
private static readonly ConcurrentDictionary<SwarmSwarmBackend, SemaphoreSlim> RemoteCapabilityGates = new();
```

Create one helper used by both `OnSwarmBackendAdded` and `ReviseRemotesEvent`:

```csharp
/// <summary>Fetches and publishes one linked Comfy backend's last-good object-info snapshot.</summary>
private static async Task RefreshRemoteCapabilities(SwarmSwarmBackend backend)
{
    SemaphoreSlim gate = RemoteCapabilityGates.GetOrAdd(backend, _ => new(1, 1));
    await gate.WaitAsync(Program.GlobalProgramCancel);
    try
    {
        HttpRequestMessage request = new(HttpMethod.Get, $"{backend.Address}/ComfyBackendDirect/object_info");
        backend.RequestAdapter()?.Invoke(request);
        request.Headers.Add("X-Swarm-Backend-ID", $"{backend.LinkedRemoteBackendID}");
        HttpResponseMessage response = await SwarmSwarmBackend.HttpClient.SendAsync(request, Program.GlobalProgramCancel);
        JObject rawObjectInfo = (await response.Content.ReadAsStringAsync()).ParseToJson();
        HashSet<string> nodeTypes = [.. rawObjectInfo.Properties().Select(property => property.Name)];
        ComfyBackendCapabilitySnapshot snapshot = AssignValuesFromRaw(backend, rawObjectInfo, nodeTypes, "/");
        backend.ExtensionData["ComfyNodeTypes"] = snapshot.NodeTypes;
    }
    finally
    {
        gate.Release();
    }
}
```

Do not derive linked scheduling features from this snapshot; `SwarmSwarmBackend.SupportedFeatures` remains remote-status authoritative.

- [ ] **Step 4: Remove linked and local registry owners only on deletion**

Subscribe once during Comfy `OnInit`, after `Program.Backends` has been constructed:

```csharp
Program.Backends.BackendRemovedEvent += OnBackendRemoved;
```

Unsubscribe in `OnShutdown`.

Implement:

```csharp
/// <summary>Removes capability state for a backend that has left the handler registry.</summary>
private static void OnBackendRemoved(BackendHandler.BackendData data)
{
    object owner = data.AbstractBackend;
    if (owner is ComfyUIAPIAbstractBackend
        || owner is SwarmSwarmBackend remote && remote.LinkedRemoteBackendType?.StartsWith("comfyui_") == true)
    {
        ComfyCapabilityRegistry.Remove(owner);
    }
    if (owner is SwarmSwarmBackend swarm)
    {
        RemoteCapabilityGates.TryRemove(swarm, out _);
    }
}
```

Reload/edit do not invoke `BackendRemovedEvent`, so last-good snapshots remain.

- [ ] **Step 5: Migrate linked node readers**

Store `snapshot.NodeTypes` in `ExtensionData["ComfyNodeTypes"]`. Update `ValidityChecks` and `ComfyUser` to accept `IReadOnlySet<string>` rather than casting to `HashSet<string>`:

```csharp
IReadOnlySet<string> nodes = backend.ExtensionData.GetValueOrDefault("ComfyNodeTypes", null) as IReadOnlySet<string>;
```

Keep the existing null-means-no-node-validation behavior in `TryIsValid`.

- [ ] **Step 6: Verify remote authority and lifecycle**

Run:

```bash
rg -n 'RemoteFeatureCombo\\.Keys|RemoteFeatureSnapshot|GetRemoteFeatureSnapshot|SupportedFeatures' \
  src/Backends/SwarmSwarmBackend.cs
rg -n 'OnSwarmBackendAdded|ReviseRemotesEvent|RefreshRemoteCapabilities|BackendRemovedEvent|ComfyNodeTypes' \
  src/Backends src/BuiltinExtensions/ComfyUIBackend \
  --glob '*.cs'
git diff --check
```

Expected: scheduling reads the immutable remote status snapshot; both object-info triggers use one helper; deletion removes owners; reload does not.

- [ ] **Step 7: Commit remote publication and removal**

```bash
git add \
  src/Backends/SwarmSwarmBackend.cs \
  src/Backends/BackendHandler.cs \
  src/BuiltinExtensions/ComfyUIBackend/ComfyUIBackendExtension.cs \
  src/BuiltinExtensions/ComfyUIBackend/ComfyUser.cs
git commit -m "refactor: publish linked Comfy capabilities"
```

---

### Task 7: Complete Maintained Reader and Contract Conformance

**Files:**
- Modify if required by inventory: `src/Text2Image/T2IEngine.cs`
- Modify if required by inventory: `src/Backends/BackendHandler.cs`
- Modify if required by inventory: `src/WebAPI/BackendAPI.cs`
- Modify if required by inventory: files returned by the Task 1 searches

- [ ] **Step 1: Make each generic reader enumerate one stable value**

Where a generic consumer can enumerate `SupportedFeatures` more than once, capture it once:

```csharp
string[] featureSnapshot = [.. backend.AbstractBackend.SupportedFeatures];
```

Use `featureSnapshot` for the complete response or decision. Preserve:

- `BackendHandler.GetAllSupportedFeatures` enabled/status filtering;
- `T2IEngine` exact-backend, type, refusal-reason, and disregarded-feature behavior; and
- `BackendAPI` payload keys and feature values.

- [ ] **Step 2: Repeat the complete inventories**

Run:

```bash
rg -n 'FeaturesSupported|FeaturesDiscardIfNotFound|NodeToFeatureMap|RawObjectInfoParsers|ValueAssignmentLocker|AssignValuesFromRaw' \
  src/BuiltinExtensions/ComfyUIBackend \
  --glob '*.cs'

rg -n '\bSupportedFeatures\b|GetAllSupportedFeatures' \
  src/Backends src/Core src/Text2Image src/WebAPI src/BuiltinExtensions \
  --glob '*.cs' \
  --glob '!src/Extensions/**' \
  --glob '!src/bin/**' \
  --glob '!src/obj/**'

rg -n '\bNodeTypes\b|ComfyNodeTypes|RemoteFeatureCombo' \
  src/BuiltinExtensions/ComfyUIBackend src/Backends \
  --glob '*.cs'
```

Expected classification:

- public compatibility declarations and explicitly locked mutations remain;
- every maintained local decision uses a backend snapshot;
- linked scheduling uses remote-status snapshot authority;
- linked raw validation uses its frozen node set;
- aggregate readers receive stable results; and
- no uncovered maintained mutable enumeration remains.

- [ ] **Step 3: Trace lock and I/O ordering**

Run:

```bash
rg -n -C 12 'CapabilityRefreshGate|RemoteCapabilityGates|ValueAssignmentLocker|SendAsync|SendGet|PostJson|AwaitJobLive|DeleteById' \
  src/BuiltinExtensions/ComfyUIBackend src/Backends \
  --glob '*.cs'
```

Expected:

- per-owner gate may span its own fetch;
- `ValueAssignmentLocker` spans no network, WebSocket, process, backend shutdown, model, or GPU work;
- registry removal performs no I/O; and
- no reverse global-lock-to-refresh-gate acquisition exists.

- [ ] **Step 4: Verify public identity, values, and wire contracts**

Run:

```bash
git diff -- \
  src/BuiltinExtensions/ComfyUIBackend/ComfyUIBackendExtension.cs \
  src/BuiltinExtensions/ComfyUIBackend/ComfyCapabilityCatalog.cs \
  src/BuiltinExtensions/ComfyUIBackend/ComfyUIWebAPI.cs \
  src/Backends/SwarmSwarmBackend.cs \
  src/WebAPI/BackendAPI.cs

rg -n 'public static HashSet<string> FeaturesSupported|public static HashSet<string> FeaturesDiscardIfNotFound|public static Dictionary<string, string> NodeToFeatureMap' \
  src/BuiltinExtensions/ComfyUIBackend/ComfyUIBackendExtension.cs
```

Confirm:

- the three public collection fields retain declarations and are never reassigned;
- all feature IDs and node mappings are unchanged;
- parser callback order remains after maintained shared parsing;
- API route names, request/response keys, status filtering, and permissions are unchanged; and
- folder flags remain exactly `folderbackslash` and `folderslash`.

- [ ] **Step 5: Commit any final reader corrections**

If this task changes source:

```bash
git add \
  src/Text2Image/T2IEngine.cs \
  src/Backends/BackendHandler.cs \
  src/WebAPI/BackendAPI.cs
git commit -m "fix: complete Comfy capability readers"
```

If no source changes are required, record the inspection results and make no commit.

---

### Task 8: Perform Whole-Project Static Review and Record Implementation

**Files:**
- Modify: `docs/superpowers/specs/2026-07-25-immutable-per-backend-comfy-capabilities-design.md`
- Modify: `docs/superpowers/audits/2026-07-21-maintainability-architecture-refresh.md`

- [ ] **Step 1: Inspect the integrated source range**

The approved design commit `efa1e48b` is the fixed pre-plan boundary. Run:

```bash
git diff --name-only efa1e48b..HEAD
git diff --check efa1e48b..HEAD
git log --oneline efa1e48b..HEAD
```

Expected: only the planned maintained source files plus the approved design/plan documents appear; no whitespace errors.

- [ ] **Step 2: Repeat every static verification item from the design**

Run the Task 1 and Task 7 searches against the final source. Additionally run:

```bash
rg -n 'ComfyUIBackendExtension\\.FeaturesSupported' src --glob '*.cs' --glob '!src/Extensions/**'
rg -n 'RemoteFeatureCombo\\.Keys' src --glob '*.cs' --glob '!src/Extensions/**'
rg -n 'as HashSet<string>.*ComfyNodeTypes|\\(HashSet<string>\\).*ComfyNodeTypes' src --glob '*.cs' --glob '!src/Extensions/**'
rg -n 'FeaturesSupported\\s*=|FeaturesDiscardIfNotFound\\s*=|NodeToFeatureMap\\s*=' \
  src/BuiltinExtensions/ComfyUIBackend --glob '*.cs'
```

Expected:

- no maintained backend-local decision reads the global set;
- no maintained remote feature reader enumerates `RemoteFeatureCombo.Keys`;
- no linked node reader requires a mutable `HashSet`;
- only established declaration initializers assign the public compatibility fields; and
- the complete reader/writer inventory is classified.

- [ ] **Step 3: Update the design implementation record**

Change status from `Approved` to:

```markdown
**Status:** Implemented; awaiting maintainer validation
```

Add an `## Implementation Record` before `## Maintainer Validation` containing:

- exact production commit range and commit list;
- exact changed source-file list;
- final inventory counts and classifications;
- immutable local/remote publication and compatibility-owner summary;
- static commands and results;
- protected-file exclusion; and
- explicit statement that no agent ran a build, test, launcher, backend, browser, Comfy process, concurrency exercise, or benchmark.

- [ ] **Step 4: Update only Comfy F22/rank 8 audit dispositions**

In the architecture audit:

- mark Comfy F22 and rank 8 `Implemented; awaiting maintainer validation`;
- record the exact integrated range and static evidence;
- retain the original finding as historical evidence;
- move `Recommended Next Project` to rank 9 without changing its scope;
- keep ranks 1 and 2 awaiting their own validation;
- make no performance, Windows-runtime, or live-behavior claim.

- [ ] **Step 5: Commit the implementation record**

```bash
git add \
  docs/superpowers/specs/2026-07-25-immutable-per-backend-comfy-capabilities-design.md \
  docs/superpowers/audits/2026-07-21-maintainability-architecture-refresh.md
git diff --cached --check
git commit -m "docs: record Comfy capability coordination"
```

---

### Task 9: Maintainer Live Validation and Closure

**Files:**
- Modify after maintainer confirmation: `docs/superpowers/specs/2026-07-25-immutable-per-backend-comfy-capabilities-design.md`
- Modify after maintainer confirmation: `docs/superpowers/audits/2026-07-21-maintainability-architecture-refresh.md`

- [ ] **Step 1: Present the exact live matrix**

Ask maintainer Reaper176 to run the matrix already recorded in the design:

1. normal build/launch/listing/browsing/generation/workflow preview;
2. heterogeneous local/API/self-start backends;
3. exact and normal scheduling for backend-unique features;
4. Swarm/FreeU/IPAdapter/preprocessor/interpolation/SAM/hook/loader/cache/device behavior;
5. generated/raw workflow authority and model loading;
6. node add/remove, refresh, reload, idle, restart, and deletion;
7. concurrent status/backend/parameter/workflow/raw-validation/generation reads;
8. last-good fetch/parse failure and first-load fail-closed behavior;
9. heterogeneous linked remotes, refresh, and removal;
10. direct compatibility mutations plus parser order/failure isolation;
11. stable parameter visibility and unchanged payloads; and
12. unique aggregate contribution removal.

- [ ] **Step 2: Record only confirmed runtime scope**

After explicit maintainer confirmation:

- change design and audit status to `Implemented and maintainer-validated`;
- record the date, platform, and exact confirmed cases;
- record unvalidated platforms/cases explicitly;
- do not claim performance improvement or an agent-run runtime result; and
- keep any partial or failed case pending instead of broadening the confirmation.

- [ ] **Step 3: Commit validation closure**

```bash
git add \
  docs/superpowers/specs/2026-07-25-immutable-per-backend-comfy-capabilities-design.md \
  docs/superpowers/audits/2026-07-21-maintainability-architecture-refresh.md
git diff --cached --check
git commit -m "docs: validate per-backend Comfy capabilities"
```

- [ ] **Step 4: Report final repository state**

Run:

```bash
git status --short --branch --untracked-files=no
git rev-list --left-right --count origin/master...master
git log --oneline -12
```

Report the final `master` HEAD, ahead/behind counts, unpushed state, protected maintainer files, production range, validation scope, and the audit's recommended next project. Do not push unless the maintainer explicitly requests it.
