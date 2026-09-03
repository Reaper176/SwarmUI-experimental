# Additional Standard LoRA Layouts Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Safely classify the reported SDXL, Stable Diffusion 1.5, and Flux.2 Klein 4B LoRAs without misclassifying the misplaced IP-Adapter or unsupported Anima dual-KV style networks.

**Architecture:** Extend the existing model-class predicates with explicit base-model metadata hints and narrow architecture-specific tensor signatures. Retain the global LoRA gate and existing cache-provenance protections, then advance the targeted cache revision so previously unknown LoRAs are rechecked.

**Tech Stack:** C# 12, .NET 8, Newtonsoft.Json `JObject`, the existing standalone Python/C# classification harness.

---

### Task 1: Add failing classification regressions

**Files:**
- Modify: `src/BuiltinExtensions/ComfyUIBackend/ExtraNodes/SwarmComfyCommon/tests/Anima38ClassificationHarness.cs.txt`
- Test: `src/BuiltinExtensions/ComfyUIBackend/ExtraNodes/SwarmComfyCommon/tests/test_anima38_classification.py`

- [ ] **Step 1: Add shaped tensor, fixture, and direct-predicate helpers**

Replace `TensorDescriptor()` with a `params long[]` form and add focused fixtures:

```csharp
/// <summary>Creates a tensor descriptor with the requested shape.</summary>
private static JObject TensorDescriptor(params long[] shape)
{
    long[] actualShape = shape.Length == 0 ? [1] : shape;
    return new JObject
    {
        ["shape"] = new JArray(actualShape)
    };
}

/// <summary>Creates a representative flattened Diffusers LoRA tensor.</summary>
private static JObject SingleLoraTensor(string semanticName, params long[] shape)
{
    return new JObject
    {
        [$"lora_unet_{semanticName.Replace('.', '_')}.lora_up.weight"] = TensorDescriptor(shape)
    };
}

/// <summary>Creates the dimensional signature of the Flux.2 Klein 4B outpaint LoRA.</summary>
private static JObject Flux2Klein4BOutpaintHeader()
{
    return new JObject
    {
        ["diffusion_model.double_blocks.4.img_attn.proj.lora_A.weight"] = TensorDescriptor(16, 3072),
        ["diffusion_model.double_stream_modulation_img.lin.lora_B.weight"] = TensorDescriptor(18432, 16),
        ["diffusion_model.single_blocks.19.linear2.lora_A.weight"] = TensorDescriptor(16, 12288),
        ["diffusion_model.txt_in.lora_A.weight"] = TensorDescriptor(16, 7680)
    };
}

/// <summary>Creates a malformed Flux.2 Klein 4B-like LoRA header with a scalar modulation descriptor.</summary>
private static JObject MalformedFlux2Klein4BHeader()
{
    return new JObject
    {
        ["diffusion_model.double_blocks.4.img_attn.proj.lora_A.weight"] = TensorDescriptor(16, 3072),
        ["diffusion_model.double_stream_modulation_img.lin.lora_B.weight"] = new JValue(18432),
        ["diffusion_model.single_blocks.19.linear2.lora_A.weight"] = TensorDescriptor(16, 12288),
        ["diffusion_model.txt_in.lora_A.weight"] = TensorDescriptor(16, 7680)
    };
}

/// <summary>Creates the shared and 9B-specific tensor signature of a Flux.2 Klein 9B LoRA.</summary>
private static JObject Flux2Klein9BHeader()
{
    return new JObject
    {
        ["diffusion_model.double_blocks.4.img_attn.proj.lora_A.weight"] = TensorDescriptor(),
        ["diffusion_model.double_blocks.4.txt_mlp.2.lora_A.weight"] = TensorDescriptor(),
        ["diffusion_model.single_blocks.18.linear1.lora_A.weight"] = TensorDescriptor(),
        ["diffusion_model.single_blocks.19.linear2.lora_A.weight"] = TensorDescriptor(),
        ["diffusion_model.single_blocks.23.linear1.lora_A.weight"] = TensorDescriptor(),
        ["diffusion_model.double_stream_modulation_img.lin.lora_B.weight"] = TensorDescriptor(24576, 16)
    };
}

/// <summary>Creates the dimensional signature of a Flux.2 Dev LoRA.</summary>
private static JObject Flux2DevHeader()
{
    return new JObject
    {
        ["diffusion_model.double_stream_modulation_img.lin.lora_B.weight"] = TensorDescriptor(36864, 16),
        ["diffusion_model.single_blocks.47.linear2.lora_A.weight"] = TensorDescriptor(16, 16384)
    };
}

/// <summary>Adds a nested external base-model declaration to a representative LoRA header.</summary>
private static JObject WithNestedBaseModel(JObject header, string baseModel)
{
    header["__metadata__"] = new JObject
    {
        ["BaseModel"] = baseModel
    };
    return header;
}

/// <summary>Throws when a model-class predicate directly matches a synthetic header.</summary>
private static void AssertDoesNotMatchClass(string scenario, JObject header, string classId)
{
    T2IModelClass modelClass = T2IModelClassSorter.ModelClasses[classId];
    T2IModel model = new(null, null, "classification.safetensors", "classification");
    if (modelClass.IsThisModelOfClass(model, header))
    {
        throw new InvalidOperationException($"{scenario}: predicate '{classId}' unexpectedly matched.");
    }
}
```

- [ ] **Step 2: Add positive SD-family cases**

Add these assertions in `Main()`:

```csharp
AssertLoraClass(
    "Pony sidecar LoRA",
    WithBaseModel(SingleLoraTensor("down_blocks.0.attentions.0.transformer_blocks.0.attn1.to_q"), "pOnY"),
    "stable-diffusion-xl-v1-base/lora");
AssertLoraClass(
    "Illustrious sidecar LyCORIS LoRA",
    WithNestedBaseModel(new JObject
    {
        ["lora_unet_down_blocks_0_attentions_0_transformer_blocks_0_attn1_to_q.a1.weight"] = TensorDescriptor()
    }, "iLlUsTrIoUs"),
    "stable-diffusion-xl-v1-base/lora");
AssertLoraClass(
    "SD 1.5 sidecar LoRA",
    WithBaseModel(SingleLoraTensor("down_blocks.0.attentions.0.transformer_blocks.0.attn1.to_q"), "sD 1.5"),
    "stable-diffusion-v1/lora");
AssertLoraClass(
    "Metadata-free SDXL multi-transformer-block LoRA",
    SingleLoraTensor("down_blocks.1.attentions.0.transformer_blocks.1.attn1.to_q"),
    "stable-diffusion-xl-v1-base/lora");
AssertLoraClass(
    "Metadata-free SD 1.5 fourth-up-block LoRA",
    SingleLoraTensor("up_blocks.3.attentions.2.transformer_blocks.0.attn2.to_v"),
    "stable-diffusion-v1/lora");
```

- [ ] **Step 3: Add Flux.2 positive, malformed-descriptor, and separation cases**

```csharp
AssertLoraClass("Flux.2 Klein 4B dimensional LoRA", Flux2Klein4BOutpaintHeader(), "flux.2-klein-4b/lora");
AssertLoraClass("Malformed Flux.2 Klein 4B-like LoRA", MalformedFlux2Klein4BHeader(), null);
JObject flux1 = new()
{
    ["transformer.single_transformer_blocks.0.attn.to_k.lora_A.weight"] = TensorDescriptor(16, 3072)
};
AssertLoraClass("Flux.1 LoRA", flux1, "Flux.1-dev/lora");
AssertDoesNotMatchClass("Flux.1 LoRA is not Flux.2 Klein 4B", flux1, "flux.2-klein-4b/lora");
JObject flux2Klein9B = Flux2Klein9BHeader();
AssertLoraClass("Flux.2 Klein 9B", flux2Klein9B, "flux.2-klein-9b/lora");
AssertDoesNotMatchClass("Flux.2 Klein 9B is not 4B", flux2Klein9B, "flux.2-klein-4b/lora");
JObject flux2Dev = Flux2DevHeader();
AssertLoraClass("Flux.2 Dev", flux2Dev, "flux.2-dev/lora");
AssertDoesNotMatchClass("Flux.2 Dev is not Klein 4B", flux2Dev, "flux.2-klein-4b/lora");
```

- [ ] **Step 4: Add excluded-format and ambiguity cases**

```csharp
AssertLoraClass(
    "BaseModel does not turn IP-Adapter into LoRA",
    WithBaseModel(new JObject
    {
        ["image_proj.proj.weight"] = TensorDescriptor(3072, 1024),
        ["ip_adapter.1.to_k_ip.weight"] = TensorDescriptor(320, 768)
    }, "SD 1.5"),
    null);
AssertLoraClass(
    "Anima dual-KV style network is not a LoRA",
    new JObject
    {
        ["__metadata__"] = new JObject { ["modelspec.architecture"] = "anima-preview/style-dual-kv-network" },
        ["style_kv_dit_blocks_0_self_attn.out_down.weight"] = TensorDescriptor(16, 2048),
        ["style_kv_dit_blocks_27_self_attn.out_up.weight"] = TensorDescriptor(2048, 16)
    },
    null);
AssertLoraClass(
    "Ambiguous sparse SD-family LoRA",
    SingleLoraTensor("down_blocks.0.attentions.0.transformer_blocks.0.attn1.to_q"),
    null);
```

Keep the existing Anima, Cosmos, cache-selection, and persistence assertions unchanged except for the revision expectation in Task 3.

- [ ] **Step 5: Run the harness and verify RED**

Run:

```bash
python3 src/BuiltinExtensions/ComfyUIBackend/ExtraNodes/SwarmComfyCommon/tests/test_anima38_classification.py -v
```

Expected: FAIL on the first new supported case because the production classifier does not yet recognize the sidecar/tensor layout. Confirm the failure is a classification mismatch rather than compilation failure.

- [ ] **Step 6: Commit the failing regressions**

```bash
git add src/BuiltinExtensions/ComfyUIBackend/ExtraNodes/SwarmComfyCommon/tests/Anima38ClassificationHarness.cs.txt
git commit -m "test: cover additional standard LoRA layouts"
```

### Task 2: Implement narrow SD and Flux.2 classification

**Files:**
- Modify: `src/Text2Image/T2IModelClassSorter.cs`
- Test: `src/BuiltinExtensions/ComfyUIBackend/ExtraNodes/SwarmComfyCommon/tests/test_anima38_classification.py`

- [ ] **Step 1: Generalize the base-model metadata helper**

Replace the Anima-only helper with:

```csharp
bool hasBaseModel(JObject h, string expected) => string.Equals(h.Value<string>("BaseModel"), expected, StringComparison.OrdinalIgnoreCase)
    || string.Equals(h["__metadata__"]?.Value<string>("BaseModel"), expected, StringComparison.OrdinalIgnoreCase);
```

Update the Anima predicate to call `hasBaseModel(h, "Anima")` without changing its other branches.

- [ ] **Step 2: Add SD-family metadata and tensor signatures**

Extend the existing predicates, retaining every current branch:

```csharp
bool isV1Lora(JObject h) => h.ContainsKey("lora_unet_up_blocks_3_attentions_2_transformer_blocks_0_ff_net_2.lora_up.weight")
    || hasLoraKey(h, "up_blocks.3.attentions.2.transformer_blocks.0.attn2.to_v")
    || hasBaseModel(h, "SD 1.5");

bool isXLLora(JObject h) => h.ContainsKey("lora_unet_output_blocks_5_1_transformer_blocks_1_ff_net_2.lora_up.weight")
    || h.ContainsKey("lora_unet_down_blocks_2_attentions_1_transformer_blocks_9_attn2_to_v.lora_up.weight")
    || (h.ContainsKey("lora_te1_text_model_encoder_layers_0_self_attn_v_proj.lora_up.weight")
        && !h.ContainsKey("lora_unet_double_blocks_0_img_attn_proj.lora_down.weight")
        && !h.ContainsKey("lora_unet_single_blocks_0_linear1.lora_down.weight"))
    || hasLoraKey(h, "down_blocks.1.attentions.0.transformer_blocks.1.attn1.to_q")
    || hasBaseModel(h, "Pony")
    || hasBaseModel(h, "Illustrious");
```

Place `hasBaseModel` before the first predicate that uses it. Do not add title, path, or filename matching. Do not broaden `hasLoraKey` globally.

- [ ] **Step 3: Add the Flux.2 Klein 4B dimensional signature safely**

Add a local predicate next to the existing Flux.2 helpers:

```csharp
bool hasShapeDimension(JToken tok, int index, long expected) => tok is JObject descriptor
    && descriptor["shape"] is JArray shape
    && shape.Count > index
    && shape[index].Type == JTokenType.Integer
    && shape[index].Value<long>() == expected;

bool isFlux2Klein4BDimensionalLora(JObject h)
{
    return tryGetKey(h, "double_stream_modulation_img.lin.lora_B.weight", out JToken modulation)
        && hasShapeDimension(modulation, 0, 18432)
        && tryGetKey(h, "txt_in.lora_A.weight", out JToken textInput)
        && hasShapeDimension(textInput, 1, 7680)
        && hasLoraKey(h, "double_blocks.4.img_attn.proj")
        && hasLoraKey(h, "single_blocks.19.linear2");
}
```

Then extend only the shared Klein LoRA predicate:

```csharp
bool isFlux2KleinLora(JObject h) => (hasLoraKey(h, "double_blocks.4.img_attn.proj")
        && hasLoraKey(h, "double_blocks.4.txt_mlp.2")
        && hasLoraKey(h, "single_blocks.18.linear1")
        && hasLoraKey(h, "single_blocks.19.linear2"))
    || isFlux2Klein4BDimensionalLora(h);
```

Do not directly index a descriptor's `shape`: `hasShapeDimension` must reject scalar, absent, short, and non-integer descriptors. The malformed-descriptor regression in Task 1 is the RED case for this robustness requirement. The existing 9B and Dev exclusions remain unchanged.

- [ ] **Step 4: Run the harness and verify classifier GREEN**

Run the standalone harness. Expected: every new classifier assertion passes, the unchanged revision-3 assertion also passes, and the harness reports `OK`.

- [ ] **Step 5: Commit the classifier change**

```bash
git add src/Text2Image/T2IModelClassSorter.cs
git commit -m "fix: recognize additional standard LoRA layouts"
```

### Task 3: Advance the targeted model-class cache revision

**Files:**
- Modify: `src/Text2Image/T2IModelHandler.cs`
- Test: `src/BuiltinExtensions/ComfyUIBackend/ExtraNodes/SwarmComfyCommon/tests/Anima38ClassificationHarness.cs.txt`

- [ ] **Step 1: Update the regression expectation to revision 4**

Change the expected constant and diagnostic text in `VerifyCacheRevision()` from 3 to 4. No cache-policy assertion should otherwise change.

- [ ] **Step 2: Verify RED for the revision mismatch**

Run the harness. Expected: FAIL with `Expected model-class cache revision 4`, confirming the migration check detects the unchanged production constant.

- [ ] **Step 3: Increment the production revision**

In `T2IModelHandler`, change only:

```csharp
private const int ModelClassCacheRevision = 4;
```

- [ ] **Step 4: Run the harness and verify GREEN**

Run the harness. Expected: `Ran 1 test` and `OK`.

- [ ] **Step 5: Commit the migration**

```bash
git add src/Text2Image/T2IModelHandler.cs src/BuiltinExtensions/ComfyUIBackend/ExtraNodes/SwarmComfyCommon/tests/Anima38ClassificationHarness.cs.txt
git commit -m "fix: refresh newly supported LoRA classes"
```

### Task 4: Final static and build verification

**Files:**
- Verify: `src/Text2Image/T2IModelClassSorter.cs`
- Verify: `src/Text2Image/T2IModelHandler.cs`
- Verify: `src/BuiltinExtensions/ComfyUIBackend/ExtraNodes/SwarmComfyCommon/tests/Anima38ClassificationHarness.cs.txt`

- [ ] **Step 1: Run the complete regression harness**

```bash
python3 src/BuiltinExtensions/ComfyUIBackend/ExtraNodes/SwarmComfyCommon/tests/test_anima38_classification.py -v
```

Expected: one test passes with `OK`.

- [ ] **Step 2: Run formatting verification**

```bash
dotnet format src/SwarmUI.csproj --no-restore --verify-no-changes --include src/Text2Image/T2IModelClassSorter.cs src/Text2Image/T2IModelHandler.cs
```

Expected: exit code 0. A workspace-loading warning is acceptable if no formatting change is requested.

- [ ] **Step 3: Run a full build**

```bash
dotnet build src/SwarmUI.csproj --no-restore
```

Expected: build succeeds with zero errors.

- [ ] **Step 4: Check scope and repository hygiene**

```bash
git diff --check ac82465c..HEAD
git diff --name-status ac82465c..HEAD
git status --short
```

Expected: only the design, plan, classification harness, sorter, and handler files are changed; the worktree is clean after commits.

- [ ] **Step 5: Request final review**

Review the cumulative diff for accidental filename/path inference, global LoRA-suffix broadening, SDXL/SD1.5 overlap, Flux.2 4B/9B/Dev overlap, and regression in the existing Anima/Cosmos cache-provenance rules. Fix any findings test-first and rerun all verification commands.
