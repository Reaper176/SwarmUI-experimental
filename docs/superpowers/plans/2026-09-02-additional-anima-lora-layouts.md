# Additional Anima LoRA Layouts Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Use explicit Anima sidecar metadata and a strong remapped tensor signature to classify the warned LoRAs without confusing sparse Cosmos LoRAs.

**Architecture:** Keep complete tensor classification, add a narrowly scoped `BaseModel: Anima` LoRA predicate for combined metadata, and let stale unknown LoRAs use combined metadata while stale non-null Cosmos corrections remain raw-header-only. Advance the targeted cache revision so revision-2 misses are reconsidered once.

**Tech Stack:** C# 12, .NET 8, Newtonsoft.Json `JObject`, existing manual classification harness, Git.

---

### Task 1: Correct the regression harness for metadata-backed classification

**Files:**
- Modify: `src/BuiltinExtensions/ComfyUIBackend/ExtraNodes/SwarmComfyCommon/tests/Anima38ClassificationHarness.cs.txt`

- [ ] Add this helper after `KohyaAnimaHeader`:

```csharp
    /// <summary>Adds an external base-model declaration to a representative LoRA header.</summary>
    private static JObject WithBaseModel(JObject header, string baseModel = "Anima")
    {
        header["BaseModel"] = baseModel;
        return header;
    }
```

- [ ] Wrap the sparse cross, sparse self/MLP, LoHa, LoKr, and unusual
`up_blocks_24` positive headers in `WithBaseModel(...)`. Change the unusual
scenario name to `Sidecar-identified up-block Anima LoRA` and expect
`anima/lora`.

- [ ] Preserve metadata-free null cases for the single sparse module,
incomplete sparse signatures, incomplete block-39 signature, and unusual
`up_blocks_24` header. Add a complete four-projection sparse cross header
without `BaseModel` expecting null, plus the same header wrapped in
`WithBaseModel(..., "Cosmos Predict2")` expecting null.

- [ ] Keep the remapped block-39 positive unchanged because its LLM-adapter
marker is raw-header evidence.

- [ ] Change the reflected `SelectModelClassForCache` signature to:

```csharp
[typeof(bool), typeof(bool), typeof(T2IModelClass), typeof(T2IModelClass)]
```

Update cache selection assertions to verify:

```csharp
T2IModelClass staleSidecarOnly = (T2IModelClass)selectMethod.Invoke(null, [true, false, null, cosmosClass]);
T2IModelClass staleRawAnima = (T2IModelClass)selectMethod.Invoke(null, [true, false, animaClass, cosmosClass]);
T2IModelClass staleUnknownSidecar = (T2IModelClass)selectMethod.Invoke(null, [true, true, null, animaLoraClass]);
T2IModelClass normalCombined = (T2IModelClass)selectMethod.Invoke(null, [false, false, animaClass, cosmosClass]);
```

Require `staleSidecarOnly` null, `staleRawAnima` equal `anima-3_8b`,
`staleUnknownSidecar` equal `anima/lora`, and `normalCombined` equal Cosmos.
Resolve `animaLoraClass` from `ModelClasses["anima/lora"]`.

- [ ] Run the direct harness. Expected RED: a metadata-backed positive resolves
null because production does not yet consume `BaseModel`, or reflection cannot
find the planned four-argument selection helper.

```bash
python3 src/BuiltinExtensions/ComfyUIBackend/ExtraNodes/SwarmComfyCommon/tests/test_anima38_classification.py -v
```

- [ ] Run `git diff --check`, inspect the diff, and commit only the harness:

```bash
git add -- src/BuiltinExtensions/ComfyUIBackend/ExtraNodes/SwarmComfyCommon/tests/Anima38ClassificationHarness.cs.txt
git commit -m "test: require metadata-backed Anima classification"
```

### Task 2: Implement safe metadata and remapped-layout classification

**Files:**
- Modify: `src/Text2Image/T2IModelClassSorter.cs`

- [ ] Remove the rejected flattened `.lokr_w1` and `.hada_w1_a` additions from
`hasLoraKey`. Remove the rejected block-27 sparse cross and sparse self/MLP
branches from `isAnimaLora`.

- [ ] Add a local metadata predicate near `isAnimaLora`:

```csharp
        bool hasAnimaBaseModel(JObject h) => string.Equals(h.Value<string>("BaseModel"), "Anima", StringComparison.OrdinalIgnoreCase)
            || string.Equals(h["__metadata__"]?.Value<string>("BaseModel"), "Anima", StringComparison.OrdinalIgnoreCase);
```

- [ ] Keep the pre-existing block-27 branches and the new strong remapped
branch, then add metadata as the final branch:

```csharp
                                    || (hasLoraKey(h, "llm_adapter.blocks.5.self_attn.v_proj") && hasLoraKey(h, "blocks.39.self_attn.v_proj") && hasLoraKey(h, "blocks.39.adaln_modulation_cross_attn.1"))
                                    || hasAnimaBaseModel(h);
```

Do not add filename/directory inference or accept other `BaseModel` values.

- [ ] Run focused formatter and whitespace checks, then run the direct harness.
Expected: classification cases pass; failure is limited to the not-yet-updated
four-argument selection helper or revision 3.

```bash
dotnet format src/SwarmUI.csproj --no-restore --verify-no-changes --include src/Text2Image/T2IModelClassSorter.cs
git diff --check -- src/Text2Image/T2IModelClassSorter.cs
python3 src/BuiltinExtensions/ComfyUIBackend/ExtraNodes/SwarmComfyCommon/tests/test_anima38_classification.py -v
```

- [ ] Commit only the sorter:

```bash
git add -- src/Text2Image/T2IModelClassSorter.cs
git commit -m "fix: use Anima base-model metadata for LoRAs"
```

### Task 3: Permit combined metadata only for stale unknowns

**Files:**
- Modify: `src/Text2Image/T2IModelHandler.cs`

- [ ] Change `SelectModelClassForCache` to:

```csharp
    private static T2IModelClass SelectModelClassForCache(bool recheckStaleModelClass, bool staleModelClassWasUnknown, T2IModelClass modelHeaderClass, T2IModelClass combinedClass)
    {
        if (recheckStaleModelClass && !staleModelClassWasUnknown)
        {
            return modelHeaderClass;
        }
        return combinedClass;
    }
```

- [ ] Immediately after `recheckStaleModelClass` is calculated, add:

```csharp
        bool staleModelClassWasUnknown = recheckStaleModelClass && string.IsNullOrWhiteSpace(metadata.ModelClassType);
```

Pass that boolean to `SelectModelClassForCache` at its production call site.
Do not weaken `ShouldReplaceStaleModelClassCache`; it must still require a
successfully loaded raw header and a non-null selected class.

- [ ] Run formatter, whitespace check, and harness. Expected: all selection and
classification assertions pass; only revision 3 remains RED.

```bash
dotnet format src/SwarmUI.csproj --no-restore --verify-no-changes --include src/Text2Image/T2IModelHandler.cs
git diff --check -- src/Text2Image/T2IModelHandler.cs
python3 src/BuiltinExtensions/ComfyUIBackend/ExtraNodes/SwarmComfyCommon/tests/test_anima38_classification.py -v
```

- [ ] Commit only the handler:

```bash
git add -- src/Text2Image/T2IModelHandler.cs
git commit -m "fix: refresh unknown LoRAs from combined metadata"
```

### Task 4: Advance the targeted cache revision and verify GREEN

**Files:**
- Modify: `src/Text2Image/T2IModelHandler.cs:18`

- [ ] Change only `ModelClassCacheRevision` from 2 to 3. Preserve all stale,
selection, and guarded-replacement logic from Task 3.

- [ ] Run the complete direct harness:

```bash
python3 src/BuiltinExtensions/ComfyUIBackend/ExtraNodes/SwarmComfyCommon/tests/test_anima38_classification.py -v
```

Expected: one test passes with `OK`.

- [ ] Run focused formatter and cumulative static checks:

```bash
dotnet format src/SwarmUI.csproj --no-restore --verify-no-changes --include src/Text2Image/T2IModelClassSorter.cs src/Text2Image/T2IModelHandler.cs
git diff --check 0e999ae3..HEAD
git diff --name-status 0e999ae3..HEAD
```

Expected: implementation scope is the sorter, handler, and existing harness.

- [ ] Commit the revision change:

```bash
git add -- src/Text2Image/T2IModelHandler.cs
git commit -m "fix: refresh additional unknown LoRA classes"
```

### Task 5: Final review and live validation

- [ ] Obtain final cumulative specification and code-quality reviews. Resolve
all Critical and Important findings.

- [ ] Merge to `master`, rebuild/restart SwarmUI, and refresh the LoRA list.
The reported files with `BaseModel: Anima` sidecars and the remapped block-39
file must resolve to `anima/lora`. Metadata-free sparse Cosmos-compatible
headers must remain unclassified.

- [ ] Confirm no filename or directory fallback exists:

```bash
rg -n "modelo_1mb2|Models/Lora/anima|RawFilePath.*anima|Name.*anima" src/Text2Image/T2IModelClassSorter.cs
```

Expected: no match introduced by this implementation.
