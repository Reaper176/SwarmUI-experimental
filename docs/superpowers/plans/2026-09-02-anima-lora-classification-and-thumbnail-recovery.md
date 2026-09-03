# Anima LoRA Classification and Thumbnail Recovery Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Recognize the evidenced Anima LoRA tensor layouts, retry already-cached null LoRA classifications once, and quarantine the mislabeled legacy thumbnail without losing its bytes.

**Architecture:** Correct the existing Anima LoRA tensor predicate at its semantic-key boundary so all prefix and adapter-suffix variants already supported by `hasLoraKey` benefit. Advance the targeted classifier cache revision and use the handler's model type to retry only stale null LoRA results, while retaining the established Cosmos recheck and guarded cache replacement. Treat the invalid thumbnail as a recoverable user-data repair rather than changing the image decoder.

**Tech Stack:** C# 12/.NET 8, Newtonsoft.Json `JObject`, LiteDB model metadata, existing Roslyn classification harness, Git/static analysis, maintainer-run validation.

---

## File Map

- Modify `src/BuiltinExtensions/ComfyUIBackend/ExtraNodes/SwarmComfyCommon/tests/Anima38ClassificationHarness.cs.txt`: add representative Anima LoRA prefix/suffix cases, a near-miss case, and revision-2 cache expectations.
- Modify `src/Text2Image/T2IModelClassSorter.cs`: correct the second Anima LoRA predicate branch to supply semantic tensor names to `hasLoraKey`.
- Modify `src/Text2Image/T2IModelHandler.cs`: advance the classifier revision and target stale null classifications to the LoRA handler.
- Rename `/opt/stabilitymatrix/Data/Models/Lora/Il/Styles/kafka02_illu.thumb.jpg`: quarantine the non-image sidecar in place with an `.invalid` suffix; do not commit or rewrite it.

No production file is added. No thumbnail-decoder behavior, LoRA-loading behavior, unrelated classifier predicate, or model file is changed.

### Task 1: Record the missing Anima LoRA behaviors

**Files:**
- Modify: `src/BuiltinExtensions/ComfyUIBackend/ExtraNodes/SwarmComfyCommon/tests/Anima38ClassificationHarness.cs.txt:8-75,178-199`

- [ ] **Step 1: Generalize the harness classifier helper for LoRA inputs**

Replace the existing `Identify` helper with:

```csharp
    /// <summary>Identifies the production model class for a synthetic safetensors model and header.</summary>
    private static string Identify(JObject header, string modelType = "Stable-Diffusion")
    {
        T2IModel model = new(null, null, "classification.safetensors", "classification");
        return T2IModelClassSorter.IdentifyClassFor(model, header, modelType)?.ID;
    }
```

Keep `AssertClass` calling `Identify(header)` for the existing base-model cases.

- [ ] **Step 2: Add representative Anima LoRA header construction**

Add below `GenericAnimaHeader`:

```csharp
    /// <summary>Creates an Anima LoRA header using one maintained tensor-key convention.</summary>
    private static JObject AnimaLoraHeader(string prefix, string suffix, bool includeMlp = true)
    {
        string key(string semanticName)
        {
            if (prefix == "lora_unet_")
            {
                return $"{prefix}{semanticName.Replace('.', '_')}{suffix}";
            }
            return $"{prefix}{semanticName}{suffix}";
        }
        JObject header = new()
        {
            [key("blocks.27.self_attn.v_proj")] = TensorDescriptor(),
            [key("blocks.27.cross_attn.output_proj")] = TensorDescriptor()
        };
        if (includeMlp)
        {
            header[key("blocks.27.mlp.layer2")] = TensorDescriptor();
        }
        return header;
    }

    /// <summary>Asserts the class produced for a representative LoRA header.</summary>
    private static void AssertLoraClass(string scenario, JObject header, string expected)
    {
        string actual = Identify(header, "LoRA");
        if (actual != expected)
        {
            throw new InvalidOperationException($"{scenario}: expected '{expected}', but production resolved '{actual ?? "null"}'.");
        }
    }
```

- [ ] **Step 3: Add positive prefix/suffix cases and a negative near-miss**

In `Main`, after the existing base-model assertions and before `VerifyCacheRevision`, add:

```csharp
        AssertLoraClass(
            "Anima Diffusers LoRA",
            AnimaLoraHeader("diffusion_model.", ".lora_A.weight"),
            "anima/lora");
        AssertLoraClass(
            "Anima nested Diffusers LoRA",
            AnimaLoraHeader("model.diffusion_model.", ".lora_A.default.weight"),
            "anima/lora");
        AssertLoraClass(
            "Anima Kohya LoRA",
            AnimaLoraHeader("lora_unet_", ".lora_up.weight"),
            "anima/lora");
        AssertLoraClass(
            "Incomplete Anima-like LoRA",
            AnimaLoraHeader("diffusion_model.", ".lora_A.weight", false),
            null);
```

These cases deliberately contain no filename or metadata hint. They prove the fix is structural and that all three distinguishing tensors remain required.

- [ ] **Step 4: Perform the repository-permitted pre-implementation review**

Do not build or run the harness as an agent. Per `AGENTS.md`, inspect the changed harness statically:

```bash
git diff --check -- src/BuiltinExtensions/ComfyUIBackend/ExtraNodes/SwarmComfyCommon/tests/Anima38ClassificationHarness.cs.txt
git diff -- src/BuiltinExtensions/ComfyUIBackend/ExtraNodes/SwarmComfyCommon/tests/Anima38ClassificationHarness.cs.txt
```

Expected: no whitespace errors; the three complete layouts expect `anima/lora`, and the two-key near-miss expects null. The maintainer-run red/green command is recorded in Task 5.

- [ ] **Step 5: Commit the regression cases**

```bash
git add -- src/BuiltinExtensions/ComfyUIBackend/ExtraNodes/SwarmComfyCommon/tests/Anima38ClassificationHarness.cs.txt
git commit -m "test: cover Anima LoRA tensor layouts"
```

### Task 2: Correct the Anima LoRA semantic-key predicate

**Files:**
- Modify: `src/Text2Image/T2IModelClassSorter.cs:303-304`

- [ ] **Step 1: Replace only the broken second predicate branch**

Change `isAnimaLora` to:

```csharp
        bool isAnimaLora(JObject h) => (hasLoraKey(h, "llm_adapter.blocks.5.self_attn.v_proj") && hasLoraKey(h, "blocks.27.self_attn.v_proj") && hasLoraKey(h, "blocks.27.adaln_modulation_cross_attn.1"))
                                    || (hasLoraKey(h, "blocks.27.self_attn.v_proj") && hasLoraKey(h, "blocks.27.cross_attn.output_proj") && hasLoraKey(h, "blocks.27.mlp.layer2"));
```

Do not change `hasLoraKey`. Its final expansion already translates each semantic dotted name into `lora_unet_{underscored-name}.lora_up.weight`, while `HasModelKey` handles the Diffusers prefixes.

- [ ] **Step 2: Trace the production decision statically**

Verify all of the following from the source:

```text
diffusion_model.blocks.27.self_attn.v_proj.lora_A.weight
  -> hasLoraKey("blocks.27.self_attn.v_proj")
  -> HasModelKey("blocks.27.self_attn.v_proj.lora_A.weight")
  -> diffusion_model prefix match

lora_unet_blocks_27_self_attn_v_proj.lora_up.weight
  -> hasLoraKey("blocks.27.self_attn.v_proj")
  -> final Kohya expansion match
```

Also confirm the incomplete two-key header returns false and that the pre-existing LLM-adapter branch is byte-for-byte unchanged.

- [ ] **Step 3: Run permitted static checks**

```bash
git diff --check -- src/Text2Image/T2IModelClassSorter.cs
git diff -- src/Text2Image/T2IModelClassSorter.cs
```

Expected: exactly the three arguments in the second `isAnimaLora` branch lose their erroneous `lora_unet_` prefix; no unrelated classifier changes.

- [ ] **Step 4: Commit the predicate correction**

```bash
git add -- src/Text2Image/T2IModelClassSorter.cs
git commit -m "fix: recognize standard Anima LoRA keys"
```

### Task 3: Retry stale null LoRA classifications once

**Files:**
- Modify: `src/Text2Image/T2IModelHandler.cs:18,432-445,518-541,591-597`
- Modify: `src/BuiltinExtensions/ComfyUIBackend/ExtraNodes/SwarmComfyCommon/tests/Anima38ClassificationHarness.cs.txt:92-176`

- [ ] **Step 1: Update the cache-policy harness expectations**

In `VerifyCacheRevision`, resolve the two updated method signatures explicitly:

```csharp
        MethodInfo staleMethod = typeof(T2IModelHandler).GetMethod("IsModelClassCacheStale", BindingFlags.NonPublic | BindingFlags.Static, null, [typeof(T2IModelHandler.ModelMetadataStore), typeof(string)], null)
            ?? throw new InvalidOperationException("T2IModelHandler.IsModelClassCacheStale with model type was not found.");
        MethodInfo advanceMethod = typeof(T2IModelHandler).GetMethod("ShouldAdvanceModelClassCacheRevisionWithoutClassification", BindingFlags.NonPublic | BindingFlags.Static, null, [typeof(T2IModelHandler.ModelMetadataStore), typeof(string)], null)
            ?? throw new InvalidOperationException("T2IModelHandler.ShouldAdvanceModelClassCacheRevisionWithoutClassification with model type was not found.");
```

Add these cache records beside the existing stale/fresh cases:

```csharp
        T2IModelHandler.ModelMetadataStore staleNullLora = new();
        T2IModelHandler.ModelMetadataStore freshNullLora = new();
        metadataRevision.SetValue(staleNullLora, currentRevision - 1);
        metadataRevision.SetValue(freshNullLora, currentRevision);
```

Replace the stale-policy invocation and assertion block with:

```csharp
        bool isStale(T2IModelHandler.ModelMetadataStore metadata, string modelType)
        {
            return (bool)staleMethod.Invoke(null, [metadata, modelType]);
        }
        bool staleCosmosResult = isStale(staleCosmos, "Stable-Diffusion");
        bool freshCosmosResult = isStale(freshCosmos, "Stable-Diffusion");
        bool staleAnimaResult = isStale(staleAnima, "Stable-Diffusion");
        bool staleNullLoraResult = isStale(staleNullLora, "LoRA");
        bool staleNullCheckpointResult = isStale(staleNullLora, "Stable-Diffusion");
        bool freshNullLoraResult = isStale(freshNullLora, "LoRA");
        if (!staleCosmosResult || freshCosmosResult || staleAnimaResult || !staleNullLoraResult || staleNullCheckpointResult || freshNullLoraResult)
        {
            throw new InvalidOperationException($"Unexpected model-class cache policy: stale Cosmos={staleCosmosResult}, fresh Cosmos={freshCosmosResult}, stale Anima={staleAnimaResult}, stale null LoRA={staleNullLoraResult}, stale null checkpoint={staleNullCheckpointResult}, fresh null LoRA={freshNullLoraResult}.");
        }
```

Replace the existing advance-policy invocation and assertion block with:

```csharp
        bool mayAdvance(T2IModelHandler.ModelMetadataStore metadata, string modelType)
        {
            return (bool)advanceMethod.Invoke(null, [metadata, modelType]);
        }
        bool advanceStaleCosmos = mayAdvance(staleCosmos, "Stable-Diffusion");
        bool advanceFreshCosmos = mayAdvance(freshCosmos, "Stable-Diffusion");
        bool advanceStaleAnima = mayAdvance(staleAnima, "Stable-Diffusion");
        bool advanceStaleNullLora = mayAdvance(staleNullLora, "LoRA");
        bool advanceStaleNullCheckpoint = mayAdvance(staleNullLora, "Stable-Diffusion");
        if (advanceStaleCosmos || !advanceFreshCosmos || !advanceStaleAnima || advanceStaleNullLora || !advanceStaleNullCheckpoint)
        {
            throw new InvalidOperationException($"Unexpected unclassified revision policy: stale Cosmos={advanceStaleCosmos}, fresh Cosmos={advanceFreshCosmos}, stale Anima={advanceStaleAnima}, stale null LoRA={advanceStaleNullLora}, stale null checkpoint={advanceStaleNullCheckpoint}.");
        }
```

Preserve all existing guarded replacement, raw-header provenance, and wiring assertions.

- [ ] **Step 2: Advance and narrow the production cache policy**

In `T2IModelHandler`, change the revision and helper methods to:

```csharp
    /// <summary>Revision of model-class cache decisions that require targeted re-evaluation.</summary>
    private const int ModelClassCacheRevision = 2;

    /// <summary>Returns whether a cached model classification requires targeted re-evaluation.</summary>
    private static bool IsModelClassCacheStale(ModelMetadataStore metadata, string modelType)
    {
        return metadata is not null
            && metadata.ModelClassRevision < ModelClassCacheRevision
            && (metadata.ModelClassType == T2IModelClassSorter.CompatCosmosPredict2_14b.ID
                || (modelType == "LoRA" && string.IsNullOrWhiteSpace(metadata.ModelClassType)));
    }

    /// <summary>Returns whether a generic metadata update may advance the cached classifier revision.</summary>
    private static bool ShouldAdvanceModelClassCacheRevisionWithoutClassification(ModelMetadataStore metadata, string modelType)
    {
        return !IsModelClassCacheStale(metadata, modelType);
    }
```

Update the two production call sites:

```csharp
                bool advanceModelClassRevision = ShouldAdvanceModelClassCacheRevisionWithoutClassification(metadata, ModelType);
```

```csharp
        bool recheckStaleModelClass = canReadModelHeader && IsModelClassCacheStale(metadata, ModelType);
```

Do not change `ShouldReplaceStaleModelClassCache` or `SelectModelClassForCache`. Those helpers already ensure a stale result is replaced only when the raw model header loaded and produced a non-null class; failed or still-unknown classifications remain retryable.

- [ ] **Step 3: Confirm revision-2 behavior by static control-flow tracing**

Trace these cases through `LoadMetadata` and `ResetMetadataFrom`:

```text
revision 1 + null class + LoRA handler + readable header + anima/lora result
  -> stale -> raw header classification -> replace -> revision 2

revision 1 + null class + Stable-Diffusion handler
  -> not stale -> no forced recheck

revision 1 + null class + LoRA handler + unreadable/failed header
  -> no safe replacement -> revision remains 1 -> retryable

revision 1 + Cosmos class in any handler
  -> existing targeted recheck preserved

revision 2 + null class + LoRA handler
  -> not stale -> no repeated recheck
```

- [ ] **Step 4: Run permitted static checks**

```bash
git diff --check -- src/Text2Image/T2IModelHandler.cs src/BuiltinExtensions/ComfyUIBackend/ExtraNodes/SwarmComfyCommon/tests/Anima38ClassificationHarness.cs.txt
rg -n "ModelClassCacheRevision|IsModelClassCacheStale|ShouldAdvanceModelClassCacheRevisionWithoutClassification" src/Text2Image/T2IModelHandler.cs src/BuiltinExtensions/ComfyUIBackend/ExtraNodes/SwarmComfyCommon/tests/Anima38ClassificationHarness.cs.txt
```

Expected: revision is 2; every production/reflection invocation supplies model type; no one-argument call remains.

- [ ] **Step 5: Commit the cache refresh policy**

```bash
git add -- src/Text2Image/T2IModelHandler.cs src/BuiltinExtensions/ComfyUIBackend/ExtraNodes/SwarmComfyCommon/tests/Anima38ClassificationHarness.cs.txt
git commit -m "fix: refresh stale unknown LoRA classes"
```

### Task 4: Quarantine the invalid thumbnail sidecar

**Files:**
- Rename: `/opt/stabilitymatrix/Data/Models/Lora/Il/Styles/kafka02_illu.thumb.jpg` to `/opt/stabilitymatrix/Data/Models/Lora/Il/Styles/kafka02_illu.thumb.jpg.invalid`

- [ ] **Step 1: Confirm SwarmUI is stopped and the source file is unchanged**

```bash
pgrep -af '(^|/)(SwarmUI)( |$)|dotnet.*SwarmUI'
sha256sum '/opt/stabilitymatrix/Data/Models/Lora/Il/Styles/kafka02_illu.thumb.jpg'
```

Expected: no SwarmUI process; hash is `d45621381ac768ed56d778aa45c254b4d742b1d1b3887a6f9beb38e54ab863f9`.

- [ ] **Step 2: Rename the file without rewriting it**

```bash
mv '/opt/stabilitymatrix/Data/Models/Lora/Il/Styles/kafka02_illu.thumb.jpg' '/opt/stabilitymatrix/Data/Models/Lora/Il/Styles/kafka02_illu.thumb.jpg.invalid'
```

This operation requires the already approved user-data exception and may require sandbox escalation. Do not delete the file and do not alter any other path under `Models/`.

- [ ] **Step 3: Verify recoverability and scanner exclusion**

```bash
sha256sum '/opt/stabilitymatrix/Data/Models/Lora/Il/Styles/kafka02_illu.thumb.jpg.invalid'
test ! -e '/opt/stabilitymatrix/Data/Models/Lora/Il/Styles/kafka02_illu.thumb.jpg'
```

Expected: the renamed file retains hash `d45621381ac768ed56d778aa45c254b4d742b1d1b3887a6f9beb38e54ab863f9`, and the old scanner-matching path is absent. Recovery is the reverse rename.

Do not create a Git commit for this task; it is a local user-data repair outside the repository.

### Task 5: Review source scope and hand off runtime validation

**Files:**
- Review: `src/Text2Image/T2IModelClassSorter.cs`
- Review: `src/Text2Image/T2IModelHandler.cs`
- Review: `src/BuiltinExtensions/ComfyUIBackend/ExtraNodes/SwarmComfyCommon/tests/Anima38ClassificationHarness.cs.txt`

- [ ] **Step 1: Run agent-permitted formatting and scope checks**

```bash
dotnet format src/SwarmUI.csproj --no-restore --verify-no-changes --include src/Text2Image/T2IModelClassSorter.cs src/Text2Image/T2IModelHandler.cs
git diff --check HEAD~3..HEAD
git show --stat --oneline HEAD~3..HEAD
```

Expected: formatter and whitespace checks exit 0. Production scope is the two C# files; regression scope is the existing Anima harness. Existing unrelated worktree and user-data changes remain unstaged and unchanged.

- [ ] **Step 2: Record the maintainer-run red/green command without executing it as an agent**

Per `AGENTS.md`, the maintainer runs:

```bash
python3 -m unittest src/BuiltinExtensions/ComfyUIBackend/ExtraNodes/SwarmComfyCommon/tests/test_anima38_classification.py -v
```

Red expectation before Task 2: the three complete LoRA cases resolve to null. Green expectation after Tasks 2 and 3: all existing Anima/Cosmos cases, all three LoRA formats, the two-key near-miss, and all cache-revision cases pass with `OK`.

- [ ] **Step 3: Perform maintainer live validation after rebuilding and restarting SwarmUI**

The maintainer rebuilds/restarts through the normal Stability Matrix workflow, refreshes the LoRA model list, and verifies:

```text
anima/output/ann-d2-v1/ann-d2-v1_000002142.safetensors -> anima/lora
```

Confirm the class-sorter warning no longer appears for that model and the legacy-thumbnail error no longer references `kafka02_illu.thumb.jpg`. Also sample other previously warned Anima LoRAs using the same tensor layout; they must resolve to `anima/lora` without filename-specific handling.

- [ ] **Step 4: Inspect final repository state**

```bash
git status --short
git log -n 4 --oneline
```

Expected: only pre-existing unrelated user changes remain; the plan, regression coverage, classifier correction, and cache policy have focused commits. The invalid thumbnail remains recoverably quarantined outside Git.
