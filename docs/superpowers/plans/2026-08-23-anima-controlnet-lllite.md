# Anima ControlNet-LLLite Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Generate working Anima ControlNet workflows with the bundled Anima LLLite node and clearly reject incompatible control weights.

**Architecture:** The C# workflow generator will use `SwarmAnimaLLLite` directly for Anima-compatible ControlNets. The Python node will inspect the selected file's state-dict keys before constructing its LLLite adapter, providing a domain-specific error for files that do not use Anima LLLite's named-key format.

**Tech Stack:** C# 12/.NET 8 workflow generator; Python 3 ComfyUI custom nodes; safetensors/PyTorch state dictionaries.

---

### Task 1: Generate the direct Anima LLLite node

**Files:**
- Modify: `src/BuiltinExtensions/ComfyUIBackend/WorkflowGeneratorSteps.cs:1176-1194`

- [ ] **Step 1: Replace the generic model-patch chain in the Anima ControlNet branch**

Replace the `ModelPatchLoader` and `AnimaLLLiteApply` construction with this single node construction, using the existing constants:

```csharp
JObject animaInputs = new()
{
    [ComfyNodeInputNames.AnimaLLLite.Model] = g.CurrentModel.Path,
    [ComfyNodeInputNames.AnimaLLLite.LLLiteName] = controlModel.ToString(g.ModelFolderFormat),
    [ComfyNodeInputNames.AnimaLLLite.Image] = imageNodeActual.Path,
    [ComfyNodeInputNames.AnimaLLLite.Mask] = g.FinalMask,
    [ComfyNodeInputNames.AnimaLLLite.Strength] = controlStrength,
    [ComfyNodeInputNames.AnimaLLLite.StartPercent] = g.UserInput.Get(controlnetParams.Start, 0),
    [ComfyNodeInputNames.AnimaLLLite.EndPercent] = g.UserInput.Get(controlnetParams.End, 1)
};
string animaApplyNode = g.CreateNode(ComfyNodeNames.AnimaLLLite, animaInputs);
g.CurrentModel = g.CurrentModel.WithPath([animaApplyNode, 0]);
continue;
```

- [ ] **Step 2: Statically inspect the generated-node contract**

Confirm each `ComfyNodeInputNames.AnimaLLLite` constant matches the keys in `SwarmAnimaLLLite.INPUT_TYPES`, and confirm `ComfyNodeNames.AnimaLLLite` is mapped by `SwarmAnimaLLLite.NODE_CLASS_MAPPINGS`.

- [ ] **Step 3: Commit the workflow change**

```bash
git add src/BuiltinExtensions/ComfyUIBackend/WorkflowGeneratorSteps.cs
git commit -m "fix: use Anima LLLite control node"
```

### Task 2: Reject non-LLLite ControlNet weights clearly

**Files:**
- Modify: `src/BuiltinExtensions/ComfyUIBackend/ExtraNodes/SwarmComfyCommon/SwarmAnimaLLLiteCore.py:518-548`
- Modify: `src/BuiltinExtensions/ComfyUIBackend/ExtraNodes/SwarmComfyCommon/SwarmAnimaLLLite.py:139-143`

- [ ] **Step 1: Add a named-key format check in the LLLite core**

Add `is_anima_lllite_weights(file: str) -> bool` next to `read_lllite_metadata`. It must open `.safetensors` with `safe_open(...).keys()` and return true only when the keys contain both `lllite_conditioning1.` and a named LLLite adapter key beginning with `lllite_` and ending in `.down.weight`. For other extensions, load the state dict on CPU and apply the same key test.

- [ ] **Step 2: Reject incompatible files before creating the adapter**

Import `is_anima_lllite_weights` in `SwarmAnimaLLLite.py`. Immediately after confirming `weights_path` exists, add:

```python
if not is_anima_lllite_weights(weights_path):
    raise ValueError(
        f"ControlNet '{lllite_name}' is not an Anima ControlNet-LLLite model. "
        "Select compatible Anima LLLite weights from the ControlNet folder."
    )
```

This executes before metadata parsing, model construction, or patching. Preserve the existing missing-file, legacy-format, and inpaint-mask errors.

- [ ] **Step 3: Statically inspect the data flow**

Confirm a valid named-key LLLite file reaches `read_lllite_metadata`, `ControlNetLLLiteDiT`, and `load_lllite_weights`; confirm a regular ControlNet exits through the new `ValueError` before those operations.

- [ ] **Step 4: Commit the validation change**

```bash
git add src/BuiltinExtensions/ComfyUIBackend/ExtraNodes/SwarmComfyCommon/SwarmAnimaLLLiteCore.py src/BuiltinExtensions/ComfyUIBackend/ExtraNodes/SwarmComfyCommon/SwarmAnimaLLLite.py
git commit -m "fix: explain incompatible Anima control weights"
```

### Task 3: Final static review and manual handoff

**Files:**
- Review: `src/BuiltinExtensions/ComfyUIBackend/WorkflowGeneratorSteps.cs`
- Review: `src/BuiltinExtensions/ComfyUIBackend/ExtraNodes/SwarmComfyCommon/SwarmAnimaLLLite.py`
- Review: `src/BuiltinExtensions/ComfyUIBackend/ExtraNodes/SwarmComfyCommon/SwarmAnimaLLLiteCore.py`

- [ ] **Step 1: Run whitespace and change-scope checks**

```bash
git diff --check master...HEAD
git diff --stat master...HEAD
```

Expected: no whitespace errors and changes limited to the generated workflow plus the Anima LLLite validator.

- [ ] **Step 2: Hand off manual verification**

Do not run automated tests or builds: repository policy reserves execution verification for the developer. In SwarmUI, generate with `anima_baseV10` and a known Anima LLLite ControlNet. Then select a regular ControlNet and verify it reports the new incompatibility message rather than `UnboundLocalError`.
