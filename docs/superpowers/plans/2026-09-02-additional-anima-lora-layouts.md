# Additional Anima LoRA Layouts Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Classify the observed sparse, LyCORIS, and remapped Anima LoRA layouts without using filenames or misclassifying structurally unidentified files.

**Architecture:** Expand the existing semantic LoRA-key helper for flattened Kohya LoHa/LoKr suffixes, then add separate multi-tensor Anima signatures for sparse cross-attention, sparse self-attention/MLP, and remapped block-39 exports. Advance the targeted unknown-LoRA cache revision so revision-2 misses are reconsidered once.

**Tech Stack:** C# 12, .NET 8, Newtonsoft.Json `JObject`, the existing manual Anima classification harness, Git.

---

## File Map

- `src/Text2Image/T2IModelClassSorter.cs`: production tensor-key normalization and Anima LoRA signatures.
- `src/Text2Image/T2IModelHandler.cs`: targeted classifier-cache migration revision.
- `src/BuiltinExtensions/ComfyUIBackend/ExtraNodes/SwarmComfyCommon/tests/Anima38ClassificationHarness.cs.txt`: production-facing regression cases and cache-policy assertions.

### Task 1: Add failing regression cases for the observed layouts

**Files:**
- Modify: `src/BuiltinExtensions/ComfyUIBackend/ExtraNodes/SwarmComfyCommon/tests/Anima38ClassificationHarness.cs.txt`

- [ ] **Step 1: Add a generic flattened Kohya header helper**

Add this helper after `AnimaLoraHeader`:

```csharp
    /// <summary>Creates a flattened Kohya header containing the requested semantic Anima modules.</summary>
    private static JObject KohyaAnimaHeader(string suffix, params string[] semanticNames)
    {
        JObject header = new();
        foreach (string semanticName in semanticNames)
        {
            header[$"lora_unet_{semanticName.Replace('.', '_')}{suffix}"] = TensorDescriptor();
        }
        return header;
    }
```

- [ ] **Step 2: Add positive sparse and LyCORIS cases**

Add these assertions after the existing complete Kohya assertion:

```csharp
        AssertLoraClass(
            "Sparse Anima cross-attention LoRA",
            KohyaAnimaHeader(
                ".lora_up.weight",
                "blocks.27.cross_attn.k_proj",
                "blocks.27.cross_attn.q_proj",
                "blocks.27.cross_attn.v_proj",
                "blocks.27.cross_attn.output_proj"),
            "anima/lora");
        AssertLoraClass(
            "Sparse Anima self-attention and MLP LoRA",
            KohyaAnimaHeader(
                ".lora_up.weight",
                "blocks.27.self_attn.k_proj",
                "blocks.27.self_attn.q_proj",
                "blocks.27.self_attn.v_proj",
                "blocks.27.self_attn.output_proj",
                "blocks.27.mlp.layer1",
                "blocks.27.mlp.layer2"),
            "anima/lora");
        AssertLoraClass(
            "Anima LoHa",
            KohyaAnimaHeader(
                ".hada_w1_a",
                "blocks.27.self_attn.v_proj",
                "blocks.27.cross_attn.output_proj",
                "blocks.27.mlp.layer2"),
            "anima/lora");
        AssertLoraClass(
            "Anima LoKr",
            KohyaAnimaHeader(
                ".lokr_w1",
                "blocks.27.self_attn.v_proj",
                "blocks.27.cross_attn.output_proj",
                "blocks.27.mlp.layer2"),
            "anima/lora");
```

- [ ] **Step 3: Add the remapped block-39 case**

Add this assertion with the other positives:

```csharp
        AssertLoraClass(
            "Remapped block-39 Anima LoRA",
            new JObject
            {
                ["diffusion_model.llm_adapter.blocks.5.self_attn.v_proj.lora_A.weight"] = TensorDescriptor(),
                ["diffusion_model.blocks.39.self_attn.v_proj.lora_A.weight"] = TensorDescriptor(),
                ["diffusion_model.blocks.39.adaln_modulation_cross_attn.1.lora_A.weight"] = TensorDescriptor()
            },
            "anima/lora");
```

- [ ] **Step 4: Add sparse and unrelated negative cases**

Keep the existing incomplete two-key negative and add:

```csharp
        AssertLoraClass(
            "Single Anima-like cross-attention module",
            KohyaAnimaHeader(".lora_up.weight", "blocks.27.cross_attn.output_proj"),
            null);
        AssertLoraClass(
            "Unidentified up-block LoRA",
            new JObject
            {
                ["lora_unet_up_blocks_24_attentions_0_transformer_blocks_0_attn1_to_q.lora_up.weight"] = TensorDescriptor()
            },
            null);
```

The second negative represents the structural evidence available in `modelo_1mb2.safetensors`; do not use its filename or directory in production logic.

- [ ] **Step 5: Pin the next cache migration**

In `VerifyCacheRevision`, change the explicit revision assertion from 2 to 3 and update its message:

```csharp
        if (currentRevision != 3)
        {
            throw new InvalidOperationException($"Expected model-class cache revision 3 for additional unknown LoRA migration coverage, found {currentRevision}.");
        }
```

Because stale records use `currentRevision - 1`, this makes the harness exercise revision 2 to revision 3 without changing the existing stale-policy matrix.

- [ ] **Step 6: Run permitted static checks**

```bash
git diff --check -- src/BuiltinExtensions/ComfyUIBackend/ExtraNodes/SwarmComfyCommon/tests/Anima38ClassificationHarness.cs.txt
git diff -- src/BuiltinExtensions/ComfyUIBackend/ExtraNodes/SwarmComfyCommon/tests/Anima38ClassificationHarness.cs.txt
```

Expected: only the helper, five positive layout cases, two negatives, and the revision-3 expectation are added.

- [ ] **Step 7: Commit the failing coverage**

```bash
git add -- src/BuiltinExtensions/ComfyUIBackend/ExtraNodes/SwarmComfyCommon/tests/Anima38ClassificationHarness.cs.txt
git commit -m "test: cover additional Anima LoRA layouts"
```

- [ ] **Step 8: Maintainer verifies RED**

The agent must not run tests under `AGENTS.md`. Reaper176 runs:

```bash
python3 -m unittest src/BuiltinExtensions/ComfyUIBackend/ExtraNodes/SwarmComfyCommon/tests/test_anima38_classification.py -v
```

Expected: failure begins at an added positive because the production classifier does not yet recognize the layout. If it errors for compilation/setup reasons instead, correct the harness before production work.

### Task 2: Expand semantic key matching and Anima signatures

**Files:**
- Modify: `src/Text2Image/T2IModelClassSorter.cs:148,303-304`
- Test: `src/BuiltinExtensions/ComfyUIBackend/ExtraNodes/SwarmComfyCommon/tests/Anima38ClassificationHarness.cs.txt`

- [ ] **Step 1: Recognize flattened Kohya LoHa and LoKr module keys**

Extend only the end of `hasLoraKey`:

```csharp
        bool hasLoraKey(JObject h, string key) => hasKey(h, $"{key}.lora_A.weight") || hasKey(h, $"{key}.lora_A") || hasKey(h, $"{key}.lora_A.default.weight") || hasKey(h, $"{key}.lora_up.weight") || hasKey(h, $"{key}.lora.up.weight") || hasKey(h, $"{key}.lokr_w1")
            || hasKey(h, $"lora_unet_{key.Replace('.', '_')}.lora_up.weight") || hasKey(h, $"lora_unet_{key.Replace('.', '_')}.lokr_w1") || hasKey(h, $"lora_unet_{key.Replace('.', '_')}.hada_w1_a");
```

Do not generalize to arbitrary suffixes. The two additions correspond to observed LyCORIS module markers.

- [ ] **Step 2: Add the three multi-tensor Anima signatures**

Extend `isAnimaLora` while preserving its first two branches:

```csharp
        bool isAnimaLora(JObject h) => (hasLoraKey(h, "llm_adapter.blocks.5.self_attn.v_proj") && hasLoraKey(h, "blocks.27.self_attn.v_proj") && hasLoraKey(h, "blocks.27.adaln_modulation_cross_attn.1"))
                                    || (hasLoraKey(h, "blocks.27.self_attn.v_proj") && hasLoraKey(h, "blocks.27.cross_attn.output_proj") && hasLoraKey(h, "blocks.27.mlp.layer2"))
                                    || (hasLoraKey(h, "blocks.27.cross_attn.k_proj") && hasLoraKey(h, "blocks.27.cross_attn.q_proj") && hasLoraKey(h, "blocks.27.cross_attn.v_proj") && hasLoraKey(h, "blocks.27.cross_attn.output_proj"))
                                    || (hasLoraKey(h, "blocks.27.self_attn.k_proj") && hasLoraKey(h, "blocks.27.self_attn.q_proj") && hasLoraKey(h, "blocks.27.self_attn.v_proj") && hasLoraKey(h, "blocks.27.self_attn.output_proj") && hasLoraKey(h, "blocks.27.mlp.layer1") && hasLoraKey(h, "blocks.27.mlp.layer2"))
                                    || (hasLoraKey(h, "llm_adapter.blocks.5.self_attn.v_proj") && hasLoraKey(h, "blocks.39.self_attn.v_proj") && hasLoraKey(h, "blocks.39.adaln_modulation_cross_attn.1"));
```

- [ ] **Step 3: Trace every observed family statically**

Confirm from source:

```text
standard sparse cross-attention -> four block-27 cross-attention checks
standard sparse self/MLP -> four self-attention plus two MLP checks
flattened LoHa -> lora_unet_<semantic>.hada_w1_a
flattened LoKr -> lora_unet_<semantic>.lokr_w1
remapped Diffusers -> LLM-adapter plus block-39 checks
single cross-attention module -> no branch can match
unrelated up_blocks_* key -> no branch can match
```

- [ ] **Step 4: Run permitted static checks**

```bash
dotnet format src/SwarmUI.csproj --no-restore --verify-no-changes --include src/Text2Image/T2IModelClassSorter.cs
git diff --check -- src/Text2Image/T2IModelClassSorter.cs
git diff -- src/Text2Image/T2IModelClassSorter.cs
```

Expected: formatter and whitespace checks exit 0; only the helper suffixes and three predicate branches change.

- [ ] **Step 5: Commit the classifier expansion**

```bash
git add -- src/Text2Image/T2IModelClassSorter.cs
git commit -m "fix: recognize additional Anima LoRA layouts"
```

- [ ] **Step 6: Maintainer verifies classifier GREEN and cache RED**

Reaper176 reruns:

```bash
python3 -m unittest src/BuiltinExtensions/ComfyUIBackend/ExtraNodes/SwarmComfyCommon/tests/test_anima38_classification.py -v
```

Expected: all new and existing classifier cases pass; the harness then fails because production cache revision is still 2 while the harness requires 3.

### Task 3: Re-evaluate revision-2 unknown LoRAs once

**Files:**
- Modify: `src/Text2Image/T2IModelHandler.cs:18`
- Test: `src/BuiltinExtensions/ComfyUIBackend/ExtraNodes/SwarmComfyCommon/tests/Anima38ClassificationHarness.cs.txt`

- [ ] **Step 1: Advance the classifier cache revision**

Change only:

```csharp
    /// <summary>Revision of model-class cache decisions that require targeted re-evaluation.</summary>
    private const int ModelClassCacheRevision = 3;
```

Do not change `IsModelClassCacheStale`, `ShouldAdvanceModelClassCacheRevisionWithoutClassification`, `ShouldReplaceStaleModelClassCache`, or `SelectModelClassForCache`. Their existing model-type and raw-header guards implement the approved policy.

- [ ] **Step 2: Trace the migration statically**

Confirm:

```text
revision 2 + null class + LoRA handler + recognized new layout
  -> stale -> raw header classification -> non-null replacement -> revision 3

revision 2 + null class + Stable-Diffusion handler
  -> not stale -> no forced recheck

revision 2 + null class + LoRA handler + modelo_1mb2-style header
  -> stale -> null classification -> no replacement -> revision remains 2

revision 3 + null class + LoRA handler
  -> not stale -> no repeated recheck
```

- [ ] **Step 3: Run permitted static checks**

```bash
dotnet format src/SwarmUI.csproj --no-restore --verify-no-changes --include src/Text2Image/T2IModelHandler.cs
git diff --check -- src/Text2Image/T2IModelHandler.cs
git diff -- src/Text2Image/T2IModelHandler.cs
```

Expected: the only production change in this task is `2` to `3`.

- [ ] **Step 4: Commit the migration**

```bash
git add -- src/Text2Image/T2IModelHandler.cs
git commit -m "fix: refresh additional unknown LoRA classes"
```

- [ ] **Step 5: Maintainer verifies GREEN**

Reaper176 runs:

```bash
python3 -m unittest src/BuiltinExtensions/ComfyUIBackend/ExtraNodes/SwarmComfyCommon/tests/test_anima38_classification.py -v
```

Expected: all existing Anima/Cosmos cases, all new layout cases, both new negatives, and the revision-3 cache matrix pass with `OK`.

### Task 4: Final static review and live-validation handoff

**Files:**
- Review: `src/Text2Image/T2IModelClassSorter.cs`
- Review: `src/Text2Image/T2IModelHandler.cs`
- Review: `src/BuiltinExtensions/ComfyUIBackend/ExtraNodes/SwarmComfyCommon/tests/Anima38ClassificationHarness.cs.txt`

- [ ] **Step 1: Verify final formatting, whitespace, and scope**

```bash
dotnet format src/SwarmUI.csproj --no-restore --verify-no-changes --include src/Text2Image/T2IModelClassSorter.cs src/Text2Image/T2IModelHandler.cs
git diff --check 0e999ae3..HEAD
git diff --name-status 0e999ae3..HEAD
git log --oneline 0e999ae3..HEAD
```

Expected: only the two production files and existing harness changed; commits are focused; checks exit 0.

- [ ] **Step 2: Reconfirm the unidentified negative has no fallback**

```bash
rg -n "modelo_1mb2|Models/Lora/anima|RawFilePath.*anima|Name.*anima" src/Text2Image/T2IModelClassSorter.cs
```

Expected: no filename or directory classification rule was added.

- [ ] **Step 3: Perform final code review**

Review the cumulative diff against the approved design, focusing on false-positive risk, preservation of existing signatures, LyCORIS suffix scope, and cache retry behavior. Resolve all Critical and Important findings before handoff.

- [ ] **Step 4: Maintainer performs live validation**

After merging, Reaper176 rebuilds/restarts through Stability Matrix and refreshes the LoRA list. Confirm these families resolve to `anima/lora`:

```text
Anima-2.9B_Turbo-BF16.safetensors
dksnpromax_v1d_epoch19.safetensors
Skin-tone-Slider-Anima.safetensors
ThighsSliderAnima4.safetensors
Fat-mons-Slider7.safetensors
Testicle-slider4.safetensors
dksnpromax_v1_epoch50.safetensors
suujiniku_v1_epoch40.safetensors
hagi_v1.safetensors
```

The repeated warnings for those evidence-backed layouts must disappear. `modelo_1mb2.safetensors` remains unmatched unless future evidence identifies its actual architecture.
