# Attention Couple Regional Prompting Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add an optional native Swarm Attention Couple regional prompting method for supported Comfy model families while keeping current regional prompting as the default.

**Architecture:** Add one Swarm-owned Comfy Python node that patches cross-attention, then update C# workflow generation to build Attention Couple inputs from existing `PromptRegion` data and patch the model before sampling. Keep standard masked conditioning untouched unless the user explicitly selects `Attention Couple`.

**Tech Stack:** C# 12 / .NET 8 workflow generation, Newtonsoft `JObject`/`JArray`, ComfyUI Python custom nodes, PyTorch model patching.

---

## File Structure

- Create `src/BuiltinExtensions/ComfyUIBackend/ExtraNodes/SwarmComfyCommon/SwarmAttentionCouple.py`
  - Owns the Comfy node and helper functions adapted from the approved PPM Attention Couple implementation.
  - Registers `SwarmAttentionCouple` as a model patch node.
- Modify `src/BuiltinExtensions/ComfyUIBackend/ExtraNodes/SwarmComfyCommon/__init__.py`
  - Imports and registers `SwarmAttentionCouple`.
- Modify `src/BuiltinExtensions/ComfyUIBackend/ComfyUIBackendExtension.cs`
  - Adds `RegionalPromptingMethod` parameter with values `Standard` and `Attention Couple`.
- Modify `src/BuiltinExtensions/ComfyUIBackend/WorkflowGenerator.cs`
  - Adds regional plan records.
  - Splits regional prompt handling into standard and Attention Couple branches.
  - Adds model patch emission and unsupported-model validation.
- Modify `src/BuiltinExtensions/ComfyUIBackend/WorkflowGeneratorSteps.cs`
  - Uses the positive prompt Attention Couple plan before main sampler creation.
  - Applies same plan to refiner sampler only when selected and supported.
- Modify `docs/Features/Prompt Syntax.md`
  - Documents the new optional regional prompting method and constraints.

No automated tests or builds are run by agents in this repository per `AGENTS.md`. Verification is static inspection plus Python syntax checking where it does not execute Swarm builds or GPU workflows.

---

### Task 1: Add Swarm Attention Couple Python Node

**Files:**
- Create: `src/BuiltinExtensions/ComfyUIBackend/ExtraNodes/SwarmComfyCommon/SwarmAttentionCouple.py`

- [ ] **Step 1: Create the node file**

Implement a legacy Comfy custom node named `SwarmAttentionCouple`. Use only ASCII text. Preserve attribution at the top:

```python
# Attention Couple implementation adapted with permission from ComfyUI-ppm.
# Original implementation by laksjdjf, hako-mikan, Haoming02.
# Swarm integration keeps this code as a Swarm-owned Comfy node.
```

The file must contain these definitions:

```python
import itertools
import math
from typing import Any

import torch
import torch.nn.functional as F
import comfy.model_management
from comfy.model_base import SDXL, Anima, BaseModel, SDXLRefiner
from comfy.model_patcher import ModelPatcher

COND = 0
UNCOND = 1
COND_UNCOND_COUPLE_KEY = "swarm_cond_or_uncond_couple"


def lcm_for_list(numbers: list[int]) -> int:
    current_lcm = numbers[0]
    for number in numbers[1:]:
        current_lcm = math.lcm(current_lcm, number)
    return current_lcm


def reshape_mask(mask: torch.Tensor, size: tuple[int, int], bs: int, num_tokens: int) -> torch.Tensor:
    num_conds = mask.shape[0]
    mask_downsample = F.interpolate(mask, size=size, mode="nearest")
    return mask_downsample.view(num_conds, num_tokens, 1).repeat_interleave(bs, dim=0)


def split_kv_cond(cond: torch.Tensor) -> tuple[torch.Tensor, torch.Tensor]:
    return cond, cond


def unet_attn2_couple_wrapper(base_cond, cond_inputs: list, device, dtype: torch.dtype):
    conds = [cond[0][0].to(device, dtype=dtype) for cond in cond_inputs]
    base_strength = base_cond[0][1].get("strength", 1.0)
    strengths = [cond[0][1].get("strength", 1.0) for cond in cond_inputs]
    conds_kv = [split_kv_cond(cond) for cond in conds]
    num_tokens_k = [cond[0].shape[1] for cond in conds_kv]
    num_tokens_v = [cond[1].shape[1] for cond in conds_kv]

    def attn2_patch(q: torch.Tensor, k: torch.Tensor, v: torch.Tensor, extra_options):
        cond_or_uncond = extra_options["cond_or_uncond"]
        cond_or_uncond_couple = extra_options[COND_UNCOND_COUPLE_KEY] = list(cond_or_uncond)
        num_chunks = len(cond_or_uncond)
        bs = q.shape[0] // num_chunks
        if len(conds_kv) == 0:
            return q, k, v
        q_chunks = q.chunk(num_chunks, dim=0)
        k_chunks = k.chunk(num_chunks, dim=0)
        v_chunks = v.chunk(num_chunks, dim=0)
        lcm_tokens_k = lcm_for_list(num_tokens_k + [k.shape[1]])
        lcm_tokens_v = lcm_for_list(num_tokens_v + [v.shape[1]])
        conds_k_tensor = torch.cat([cond[0].repeat(bs, lcm_tokens_k // num_tokens_k[i], 1) * strengths[i] for i, cond in enumerate(conds_kv)], dim=0)
        conds_v_tensor = torch.cat([cond[1].repeat(bs, lcm_tokens_v // num_tokens_v[i], 1) * strengths[i] for i, cond in enumerate(conds_kv)], dim=0)
        qs = []
        ks = []
        vs = []
        cond_or_uncond_couple.clear()
        for i, cond_type in enumerate(cond_or_uncond):
            q_target = q_chunks[i]
            k_target = k_chunks[i].repeat(1, lcm_tokens_k // k.shape[1], 1)
            v_target = v_chunks[i].repeat(1, lcm_tokens_v // v.shape[1], 1)
            if cond_type == UNCOND:
                qs.append(q_target)
                ks.append(k_target)
                vs.append(v_target)
                cond_or_uncond_couple.append(UNCOND)
            else:
                qs.append(q_target.repeat(len(cond_inputs) + 1, 1, 1))
                ks.append(torch.cat([k_target * base_strength, conds_k_tensor], dim=0))
                vs.append(torch.cat([v_target * base_strength, conds_v_tensor], dim=0))
                cond_or_uncond_couple.extend(itertools.repeat(COND, len(cond_inputs) + 1))
        return torch.cat(qs, dim=0), torch.cat(ks, dim=0), torch.cat(vs, dim=0)

    return attn2_patch


def unet_attn2_output_couple_wrapper(mask: torch.Tensor):
    def attn2_output_patch(out: torch.Tensor, extra_options: dict[str, Any]):
        cond_or_uncond = extra_options[COND_UNCOND_COUPLE_KEY]
        size = tuple(extra_options["activations_shape"][-2:])
        bs = out.shape[0] // len(cond_or_uncond)
        num_tokens = out.shape[1]
        mask_downsample = reshape_mask(mask, size, bs, num_tokens)
        outputs = []
        cond_outputs = []
        i_cond = 0
        for i, cond_type in enumerate(cond_or_uncond):
            pos = i * bs
            next_pos = (i + 1) * bs
            if cond_type == UNCOND:
                outputs.append(out[pos:next_pos])
            else:
                pos_cond = i_cond * bs
                next_pos_cond = (i_cond + 1) * bs
                cond_outputs.append(out[pos:next_pos] * mask_downsample[pos_cond:next_pos_cond])
                i_cond += 1
        if len(cond_outputs) > 0:
            outputs.append(torch.stack(cond_outputs).sum(0))
        return torch.cat(outputs, dim=0)

    return attn2_output_patch


class SwarmAttentionCouple:
    @classmethod
    def INPUT_TYPES(cls):
        return {
            "required": {
                "model": ("MODEL",),
                "base_cond": ("CONDITIONING",),
                "base_mask": ("MASK",),
                "regions_json": ("STRING", {"default": "[]"}),
            },
            "optional": {
                "cond_1": ("CONDITIONING",),
                "mask_1": ("MASK",),
                "cond_2": ("CONDITIONING",),
                "mask_2": ("MASK",),
                "cond_3": ("CONDITIONING",),
                "mask_3": ("MASK",),
                "cond_4": ("CONDITIONING",),
                "mask_4": ("MASK",),
                "cond_5": ("CONDITIONING",),
                "mask_5": ("MASK",),
                "cond_6": ("CONDITIONING",),
                "mask_6": ("MASK",),
                "cond_7": ("CONDITIONING",),
                "mask_7": ("MASK",),
                "cond_8": ("CONDITIONING",),
                "mask_8": ("MASK",),
            },
        }

    RETURN_TYPES = ("MODEL",)
    FUNCTION = "patch"
    CATEGORY = "SwarmUI/model"

    def patch(self, model: ModelPatcher, base_cond, base_mask, regions_json, **kwargs):
        m = model.clone()
        model_type = type(m.model)
        if not (issubclass(model_type, SDXL) or issubclass(model_type, SDXLRefiner) or issubclass(model_type, Anima) or model_type == BaseModel):
            raise ValueError("Swarm Attention Couple only supports SD1-style BaseModel, SDXL, SDXL Refiner, and Anima models.")
        cond_inputs = []
        mask_inputs = []
        for i in range(1, 9):
            cond = kwargs.get(f"cond_{i}", None)
            mask = kwargs.get(f"mask_{i}", None)
            if cond is not None and mask is not None:
                cond_inputs.append(cond)
                mask_inputs.append(mask)
        if len(cond_inputs) == 0:
            return (m,)
        dtype = m.model.diffusion_model.dtype
        device = comfy.model_management.get_torch_device()
        masks = [base_mask] + mask_inputs
        mask = torch.stack(masks, dim=0).to(device, dtype=dtype)
        if mask.sum(dim=0).min() <= 0:
            raise ValueError("Swarm Attention Couple masks contain non-filled areas.")
        mask = mask / mask.sum(dim=0, keepdim=True)
        m.set_model_attn2_patch(unet_attn2_couple_wrapper(base_cond, cond_inputs, device, dtype))
        m.set_model_attn2_output_patch(unet_attn2_output_couple_wrapper(mask))
        return (m,)


NODE_CLASS_MAPPINGS = {
    "SwarmAttentionCouple": SwarmAttentionCouple,
}
```

- [ ] **Step 2: Syntax-check the new Python file**

Run:

```bash
python -m py_compile src/BuiltinExtensions/ComfyUIBackend/ExtraNodes/SwarmComfyCommon/SwarmAttentionCouple.py
```

Expected: no output and exit code `0`.

- [ ] **Step 3: Commit**

```bash
git add src/BuiltinExtensions/ComfyUIBackend/ExtraNodes/SwarmComfyCommon/SwarmAttentionCouple.py
git commit -m "Add Swarm Attention Couple node"
```

---

### Task 2: Register The Python Node

**Files:**
- Modify: `src/BuiltinExtensions/ComfyUIBackend/ExtraNodes/SwarmComfyCommon/__init__.py`

- [ ] **Step 1: Import the new module**

Change the import list to include `SwarmAttentionCouple`:

```python
from . import SwarmAnimaLLLite, SwarmAttentionCouple, SwarmBlending, SwarmClipSeg, SwarmImages, SwarmInternalUtil, SwarmKSampler, SwarmLoadImageB64, SwarmLoraLoader, SwarmMasks, SwarmSaveImageWS, SwarmTiling, SwarmExtractLora, SwarmUnsampler, SwarmLatents, SwarmInputNodes, SwarmTextHandling, SwarmReference, SwarmMath, SwarmSam3, SwarmAudio
```

- [ ] **Step 2: Add node mappings**

Add `SwarmAttentionCouple.NODE_CLASS_MAPPINGS` into the `NODE_CLASS_MAPPINGS` union immediately after `SwarmAnimaLLLite.NODE_CLASS_MAPPINGS`:

```python
NODE_CLASS_MAPPINGS = (
    SwarmAnimaLLLite.NODE_CLASS_MAPPINGS
    | SwarmAttentionCouple.NODE_CLASS_MAPPINGS
    | SwarmBlending.NODE_CLASS_MAPPINGS
```

- [ ] **Step 3: Syntax-check package init**

Run:

```bash
python -m py_compile src/BuiltinExtensions/ComfyUIBackend/ExtraNodes/SwarmComfyCommon/__init__.py
```

Expected: no output and exit code `0`.

- [ ] **Step 4: Commit**

```bash
git add src/BuiltinExtensions/ComfyUIBackend/ExtraNodes/SwarmComfyCommon/__init__.py
git commit -m "Register Swarm Attention Couple node"
```

---

### Task 3: Add Regional Prompting Method Parameter

**Files:**
- Modify: `src/BuiltinExtensions/ComfyUIBackend/ComfyUIBackendExtension.cs`

- [ ] **Step 1: Add static parameter field**

Update the string param field list to include `RegionalPromptingMethod`:

```csharp
public static T2IRegisteredParam<string> CustomWorkflowParam, SamplerParam, SchedulerParam, RefinerSamplerParam, RefinerSchedulerParam, RefinerUpscaleMethod, UseIPAdapterForRevision, IPAdapterWeightType, VideoPreviewType, VideoFrameInterpolationMethod, GligenModel, RegionalPromptingMethod, YoloModelInternal, PreferredDType, UseStyleModel, TeaCacheMode, EasyCacheMode, SetClipDevice;
```

- [ ] **Step 2: Register parameter**

Insert registration near `DebugRegionalPrompting` and before `GligenModel`:

```csharp
RegionalPromptingMethod = T2IParamTypes.Register<string>(new("Regional Prompting Method", "How to apply '<region:>' prompt syntax.\n'Standard' uses Swarm's masked conditioning behavior.\n'Attention Couple' patches cross-attention for supported models and may give stronger regional separation.",
    "Standard", IgnoreIf: "Standard", FeatureFlag: "comfyui", Group: T2IParamTypes.GroupRegionalPrompting, IsAdvanced: true, OrderPriority: -6,
    GetValues: (_) => ["Standard", "Attention Couple"]
    ));
```

- [ ] **Step 3: Static validation**

Run:

```bash
rg -n "RegionalPromptingMethod|Regional Prompting Method" src/BuiltinExtensions/ComfyUIBackend/ComfyUIBackendExtension.cs
```

Expected: one field declaration hit and one registration hit.

- [ ] **Step 4: Commit**

```bash
git add src/BuiltinExtensions/ComfyUIBackend/ComfyUIBackendExtension.cs
git commit -m "Add regional prompting method option"
```

---

### Task 4: Split Regional Conditioning Into Reusable Helpers

**Files:**
- Modify: `src/BuiltinExtensions/ComfyUIBackend/WorkflowGenerator.cs`

- [ ] **Step 1: Add record types near `RegionHelper`**

Replace the existing record with:

```csharp
public record struct RegionHelper(JArray PartCond, JArray Mask);

public record class AttentionCoupleRegion(JArray Cond, JArray Mask);

public record class AttentionCouplePlan(JArray BaseCond, JArray BaseMask, List<AttentionCoupleRegion> Regions);
```

- [ ] **Step 2: Add model support helper near `ShouldZeroNegative`**

Add:

```csharp
public bool SupportsAttentionCoupleRegionalPrompting()
{
    string compat = CurrentCompatClass();
    if (compat == T2IModelClassSorter.CompatAnima.ID || compat == T2IModelClassSorter.CompatSdxl.ID || compat == T2IModelClassSorter.CompatSdxlRefiner.ID)
    {
        return true;
    }
    string modelId = CurrentModelClass()?.ID ?? "";
    return modelId.StartsWith("stable-diffusion-v1") || modelId.StartsWith("stable-diffusion-v2");
}
```

Verify the exact compat constants with:

```bash
rg -n "CompatSdxl|CompatSdxlRefiner|CompatAnima" src/Text2Image/T2IModelClassSorter.cs src/BuiltinExtensions/ComfyUIBackend
```

- [ ] **Step 3: Extract mask construction**

Add a helper below `ShouldZeroNegative`:

```csharp
public JArray CreateRegionalPromptMask(PromptRegion.Part part)
{
    string regionNode = CreateNode("SwarmSquareMaskFromPercent", new JObject()
    {
        ["x"] = part.X,
        ["y"] = part.Y,
        ["width"] = part.Width,
        ["height"] = part.Height,
        ["strength"] = Math.Abs(part.Strength)
    });
    if (part.Strength < 0)
    {
        regionNode = CreateNode("InvertMask", new JObject()
        {
            ["mask"] = NodePath(regionNode, 0)
        });
    }
    return [regionNode, 0];
}
```

- [ ] **Step 4: Refactor standard branch to use helper**

Inside `CreateConditioning`, replace the inline `SwarmSquareMaskFromPercent` / `InvertMask` block with:

```csharp
RegionHelper region = new(partCond, CreateRegionalPromptMask(part));
```

Keep all existing standard behavior otherwise unchanged.

- [ ] **Step 5: Static validation**

Run:

```bash
rg -n "CreateRegionalPromptMask|SupportsAttentionCoupleRegionalPrompting|AttentionCouplePlan" src/BuiltinExtensions/ComfyUIBackend/WorkflowGenerator.cs
```

Expected: all three names appear.

- [ ] **Step 6: Commit**

```bash
git add src/BuiltinExtensions/ComfyUIBackend/WorkflowGenerator.cs
git commit -m "Prepare regional prompt helpers for attention couple"
```

---

### Task 5: Build Attention Couple Plans

**Files:**
- Modify: `src/BuiltinExtensions/ComfyUIBackend/WorkflowGenerator.cs`

- [ ] **Step 1: Add pending plan field**

Add to the workflow state fields near `FinalPrompt`:

```csharp
public AttentionCouplePlan PendingAttentionCouplePlan = null;
```

- [ ] **Step 2: Add plan builder**

Add this method below `CreateRegionalPromptMask`:

```csharp
public AttentionCouplePlan CreateAttentionCouplePlan(PromptRegion regionalizer, PromptRegion.Part[] parts, JArray clip, T2IModel model, bool isPositive)
{
    if (!isPositive)
    {
        return null;
    }
    if (!SupportsAttentionCoupleRegionalPrompting())
    {
        throw new SwarmUserErrorException($"Regional Prompting Method 'Attention Couple' only supports SD1, SD2, SDXL, SDXL Refiner, and Anima models. Current model is '{CurrentModelClass()?.Name ?? "Unknown"}'.");
    }
    if (UserInput.Get(ComfyUIBackendExtension.GligenModel, "None") != "None")
    {
        throw new SwarmUserErrorException("Regional Prompting Method 'Attention Couple' cannot be combined with GLIGEN Model. Set one of them back to its default value.");
    }
    JArray lastMergedMask = null;
    List<AttentionCoupleRegion> regions = [];
    foreach (PromptRegion.Part part in parts)
    {
        JArray subClip = part.ContextID <= 1 ? clip : CreateHookLorasForConfinement(part.ContextID, clip);
        JArray partCond = CreateConditioningLine(part.Prompt, subClip, model, true);
        JArray regionMask = CreateRegionalPromptMask(part);
        regions.Add(new(partCond, regionMask));
        if (lastMergedMask is null)
        {
            lastMergedMask = regionMask;
        }
        else
        {
            string overlapped = CreateNode("SwarmOverMergeMasksForOverlapFix", new JObject()
            {
                ["mask_a"] = lastMergedMask,
                ["mask_b"] = regionMask
            });
            lastMergedMask = [overlapped, 0];
        }
    }
    string globalMask = CreateNode("SwarmSquareMaskFromPercent", new JObject()
    {
        ["x"] = 0,
        ["y"] = 0,
        ["width"] = 1,
        ["height"] = 1,
        ["strength"] = 1
    });
    string maskBackground = CreateNode("SwarmExcludeFromMask", new JObject()
    {
        ["main_mask"] = NodePath(globalMask, 0),
        ["exclude_mask"] = lastMergedMask
    });
    string backgroundPrompt = string.IsNullOrWhiteSpace(regionalizer.BackgroundPrompt) ? regionalizer.GlobalPrompt : regionalizer.BackgroundPrompt;
    double globalStrength = UserInput.Get(T2IParamTypes.GlobalRegionFactor, 0.5);
    JArray baseCond = CreateConditioningLine(backgroundPrompt, clip, model, true);
    if (globalStrength != 1)
    {
        string baseStrength = CreateNode("ConditioningSetAreaStrength", new JObject()
        {
            ["conditioning"] = baseCond,
            ["strength"] = globalStrength
        });
        baseCond = [baseStrength, 0];
    }
    List<AttentionCoupleRegion> cleanedRegions = [];
    foreach (AttentionCoupleRegion region in regions)
    {
        string overlapped = CreateNode("SwarmCleanOverlapMasksExceptSelf", new JObject()
        {
            ["mask_self"] = region.Mask,
            ["mask_merged"] = lastMergedMask
        });
        JArray regionCond = region.Cond;
        double regionStrength = 1 - globalStrength;
        if (regionStrength != 1)
        {
            string regionStrengthNode = CreateNode("ConditioningSetAreaStrength", new JObject()
            {
                ["conditioning"] = regionCond,
                ["strength"] = regionStrength
            });
            regionCond = [regionStrengthNode, 0];
        }
        cleanedRegions.Add(new(regionCond, [overlapped, 0]));
    }
    return new(baseCond, [maskBackground, 0], cleanedRegions);
}
```

- [ ] **Step 3: Branch `CreateConditioning` for Attention Couple**

After `parts.IsEmpty()` check and before GLIGEN handling, add:

```csharp
if (isPositive && UserInput.Get(ComfyUIBackendExtension.RegionalPromptingMethod, "Standard") == "Attention Couple")
{
    PendingAttentionCouplePlan = CreateAttentionCouplePlan(regionalizer, parts, clip, model, true);
    return PendingAttentionCouplePlan.BaseCond;
}
```

- [ ] **Step 4: Static validation**

Run:

```bash
rg -n "PendingAttentionCouplePlan|CreateAttentionCouplePlan|RegionalPromptingMethod" src/BuiltinExtensions/ComfyUIBackend/WorkflowGenerator.cs
```

Expected: all names appear in the new workflow code.

- [ ] **Step 5: Commit**

```bash
git add src/BuiltinExtensions/ComfyUIBackend/WorkflowGenerator.cs
git commit -m "Build attention couple regional prompt plans"
```

---

### Task 6: Emit SwarmAttentionCouple Model Patch Before Sampling

**Files:**
- Modify: `src/BuiltinExtensions/ComfyUIBackend/WorkflowGenerator.cs`

- [ ] **Step 1: Add model patch helper**

Add below `CreateAttentionCouplePlan`:

```csharp
public JArray ApplyAttentionCouplePlanToModel(JArray model, AttentionCouplePlan plan)
{
    if (plan is null || plan.Regions.Count == 0)
    {
        return model;
    }
    if (plan.Regions.Count > 8)
    {
        throw new SwarmUserErrorException("Regional Prompting Method 'Attention Couple' currently supports up to 8 regions.");
    }
    JObject inputs = new()
    {
        ["model"] = model,
        ["base_cond"] = plan.BaseCond,
        ["base_mask"] = plan.BaseMask,
        ["regions_json"] = "[]"
    };
    for (int i = 0; i < plan.Regions.Count; i++)
    {
        inputs[$"cond_{i + 1}"] = plan.Regions[i].Cond;
        inputs[$"mask_{i + 1}"] = plan.Regions[i].Mask;
    }
    string patched = CreateNode("SwarmAttentionCouple", inputs);
    return [patched, 0];
}
```

- [ ] **Step 2: Apply helper inside `CreateKSampler`**

Near the top of `CreateKSampler`, after default model-type handling but before sampler node creation, add:

```csharp
AttentionCouplePlan attentionCouplePlan = PendingAttentionCouplePlan;
if (attentionCouplePlan is not null)
{
    model = ApplyAttentionCouplePlanToModel(model, attentionCouplePlan);
    PendingAttentionCouplePlan = null;
}
```

Place this after special conditioning blocks that may replace `model` only if those blocks are incompatible with Attention Couple; otherwise place it immediately before `string firstId = willCascadeFix ? null : id;` so it patches the final model path used by the sampler.

- [ ] **Step 3: Ensure no stale plan leaks**

At the end of `CreateKSampler`, before every early `return emitAsCustomAdvanced(...)`, clear `PendingAttentionCouplePlan = null;` if the branch is incompatible. The compatible main `KSamplerAdvanced`/`SwarmKSampler` path should apply the patch once.

- [ ] **Step 4: Static validation**

Run:

```bash
rg -n "ApplyAttentionCouplePlanToModel|SwarmAttentionCouple|PendingAttentionCouplePlan = null" src/BuiltinExtensions/ComfyUIBackend/WorkflowGenerator.cs
```

Expected: helper definition, node creation, and clear sites appear.

- [ ] **Step 5: Commit**

```bash
git add src/BuiltinExtensions/ComfyUIBackend/WorkflowGenerator.cs
git commit -m "Apply attention couple model patch during sampling"
```

---

### Task 7: Wire Refiner Handling And Conflict Cases

**Files:**
- Modify: `src/BuiltinExtensions/ComfyUIBackend/WorkflowGeneratorSteps.cs`
- Modify: `src/BuiltinExtensions/ComfyUIBackend/WorkflowGenerator.cs`

- [ ] **Step 1: Confirm positive prompt creates a fresh plan for each model**

In refiner prompt creation around the refiner step, keep:

```csharp
prompt = g.CreateConditioning(g.UserInput.Get(T2IParamTypes.Prompt), g.CurrentTextEnc.Path, g.FinalLoadedModel, true, isRefiner: true);
```

This call should create a new `PendingAttentionCouplePlan` for the refiner model when the option is selected.

- [ ] **Step 2: Add explicit cascade/video guard**

In `CreateAttentionCouplePlan`, after the support check, add a guard:

```csharp
if (IsVideoModel() || IsCascade())
{
    throw new SwarmUserErrorException("Regional Prompting Method 'Attention Couple' does not support video or Stable Cascade models.");
}
```

- [ ] **Step 3: Static validation**

Run:

```bash
rg -n "Attention Couple.*video|CreateConditioning\\(g.UserInput.Get\\(T2IParamTypes.Prompt\\).*isRefiner" src/BuiltinExtensions/ComfyUIBackend
```

Expected: one guard hit and the refiner prompt creation hit.

- [ ] **Step 4: Commit**

```bash
git add src/BuiltinExtensions/ComfyUIBackend/WorkflowGenerator.cs src/BuiltinExtensions/ComfyUIBackend/WorkflowGeneratorSteps.cs
git commit -m "Handle attention couple regional prompt compatibility"
```

---

### Task 8: Document The New Option

**Files:**
- Modify: `docs/Features/Prompt Syntax.md`

- [ ] **Step 1: Add documentation under Regional Prompting**

Add after the existing regional prompting bullets:

```markdown
### Regional Prompting Method

- In Advanced parameters under Regional Prompting, `Regional Prompting Method` can be set to `Attention Couple`.
    - `Standard` is the default and uses Swarm's existing masked-conditioning workflow.
    - `Attention Couple` patches model cross-attention for supported models and can give stronger regional separation.
    - Attention Couple only applies to positive prompt regions. Negative prompts remain global.
    - Attention Couple currently supports SD1/SD2-style models, SDXL, SDXL Refiner, and Anima.
    - Attention Couple cannot be combined with GLIGEN regional prompting.
```

- [ ] **Step 2: Static validation**

Run:

```bash
rg -n "Regional Prompting Method|Attention Couple" docs/Features/Prompt\\ Syntax.md
```

Expected: documentation lines appear.

- [ ] **Step 3: Commit**

```bash
git add docs/Features/Prompt\ Syntax.md
git commit -m "Document attention couple regional prompting"
```

---

### Task 9: Final Static Review

**Files:**
- Review: all files changed in this plan

- [ ] **Step 1: Inspect branch diff**

Run:

```bash
git diff master...HEAD -- src/BuiltinExtensions/ComfyUIBackend/ExtraNodes/SwarmComfyCommon/SwarmAttentionCouple.py src/BuiltinExtensions/ComfyUIBackend/ExtraNodes/SwarmComfyCommon/__init__.py src/BuiltinExtensions/ComfyUIBackend/ComfyUIBackendExtension.cs src/BuiltinExtensions/ComfyUIBackend/WorkflowGenerator.cs src/BuiltinExtensions/ComfyUIBackend/WorkflowGeneratorSteps.cs docs/Features/Prompt\ Syntax.md
```

Expected: only Attention Couple regional prompting changes plus documentation.

- [ ] **Step 2: Confirm no generated or forbidden paths were edited**

Run:

```bash
git diff --name-only master...HEAD
```

Expected: no changes under `Data/`, `Output/`, `Models/`, `src/bin`, `src/obj`, `.vs/`, `.git/`, or downloaded upstream `dlbackend/`.

- [ ] **Step 3: Syntax-check Python node files**

Run:

```bash
python -m py_compile src/BuiltinExtensions/ComfyUIBackend/ExtraNodes/SwarmComfyCommon/SwarmAttentionCouple.py src/BuiltinExtensions/ComfyUIBackend/ExtraNodes/SwarmComfyCommon/__init__.py
```

Expected: no output and exit code `0`.

- [ ] **Step 4: Static search for required behavior**

Run:

```bash
rg -n "RegionalPromptingMethod|SwarmAttentionCouple|Attention Couple|PendingAttentionCouplePlan|GLIGEN" src/BuiltinExtensions/ComfyUIBackend docs/Features/Prompt\\ Syntax.md
```

Expected: hits for parameter registration, Python node emission, unsupported-model errors, GLIGEN conflict, and docs.

- [ ] **Step 5: Manual verification request**

Ask the maintainer to run Swarm manually and verify:

```text
1. Standard regional prompting still works with the existing cat/dog SDXL example.
2. Attention Couple regional prompting works on SDXL with two rectangular regions.
3. Attention Couple with Flux errors clearly.
4. Attention Couple plus GLIGEN errors clearly.
5. Debug Regional Prompting outputs masks for Attention Couple.
```

- [ ] **Step 6: Commit any final fixes**

If static review finds an issue, make the minimal fix and commit the exact files touched by that fix:

```bash
git add src/BuiltinExtensions/ComfyUIBackend/WorkflowGenerator.cs src/BuiltinExtensions/ComfyUIBackend/WorkflowGeneratorSteps.cs src/BuiltinExtensions/ComfyUIBackend/ComfyUIBackendExtension.cs src/BuiltinExtensions/ComfyUIBackend/ExtraNodes/SwarmComfyCommon/SwarmAttentionCouple.py src/BuiltinExtensions/ComfyUIBackend/ExtraNodes/SwarmComfyCommon/__init__.py docs/Features/Prompt\ Syntax.md
git commit -m "Fix attention couple regional prompting integration"
```
