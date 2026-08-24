# ControlNet Preprocessor Resolution Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Provide a per-ControlNet preprocessor-resolution slider that controls compatible preprocessor nodes.

**Architecture:** Add one integer parameter per existing ControlNet slot and pass its value only when the selected ComfyUI preprocessor declares a `resolution` input. The generic preprocessor-node builder is shared by normal generation and preview, so both paths receive the setting without frontend-specific logic.

**Tech Stack:** C# 12/.NET 8; SwarmUI registered parameters; ComfyUI workflow JSON.

---

### Task 1: Register per-slot resolution parameters

**Files:**
- Modify: `src/BuiltinExtensions/ComfyUIBackend/ComfyUIBackendExtension.cs:812,1061-1068`

- [ ] **Step 1: Declare the parameter array**

Extend the existing ControlNet parameter-array declaration with:

```csharp
public static T2IRegisteredParam<int>[] ControlNetPreprocessorResolutionParams = new T2IRegisteredParam<int>[3];
```

- [ ] **Step 2: Register each slot's slider**

Inside the existing `for (int i = 0; i < 3; i++)` loop, immediately after the preprocessor selector, register:

```csharp
ControlNetPreprocessorResolutionParams[i] = T2IParamTypes.Register<int>(new($"ControlNet{T2IParamTypes.Controlnets[i].NameSuffix} Preprocessor Resolution", "The resolution used by ControlNet preprocessors that support a resolution input. Lower values use less VRAM.",
    "1024", Min: 64, Max: 4096, Step: 64, FeatureFlag: "controlnet", Permission: Permissions.ParamControlNet, Group: T2IParamTypes.Controlnets[i].Group, ViewType: ParamViewType.SLIDER, OrderPriority: 3.1, ChangeWeight: 2
    ));
```

The `OrderPriority` places the slider under the preprocessor selector and before Union Type.

- [ ] **Step 3: Commit the parameter registration**

```bash
git add src/BuiltinExtensions/ComfyUIBackend/ComfyUIBackendExtension.cs
git commit -m "feat: add ControlNet preprocessor resolution"
```

### Task 2: Pass the selected resolution to compatible preprocessors

**Files:**
- Modify: `src/BuiltinExtensions/ComfyUIBackend/WorkflowGenerator.cs:2520-2555`
- Modify: `src/BuiltinExtensions/ComfyUIBackend/WorkflowGeneratorSteps.cs:1120-1125`

- [ ] **Step 1: Extend the preprocessor builder input**

Change the `CreatePreprocessor` signature to accept `int resolution`. In the `key == "resolution"` branch, replace the output-size calculation with:

```csharp
n["inputs"]["resolution"] = resolution;
```

Do not change handling for `image`, `mask`, `bbox_detector`, or other discovered inputs.

- [ ] **Step 2: Supply the selected ControlNet slot value**

At the `CreatePreprocessor` call in the ControlNet loop, pass:

```csharp
JArray preprocActual = g.CreatePreprocessor(preprocessor, imageNodeActual,
    g.UserInput.Get(ComfyUIBackendExtension.ControlNetPreprocessorResolutionParams[i], 1024));
```

- [ ] **Step 3: Statically inspect generation and preview flow**

Confirm normal generation and `ControlNetPreviewOnly` both call the same ControlNet loop and therefore pass the configured resolution. Confirm preprocessors without a `resolution` input never receive that input.

- [ ] **Step 4: Commit the workflow change**

```bash
git add src/BuiltinExtensions/ComfyUIBackend/WorkflowGenerator.cs src/BuiltinExtensions/ComfyUIBackend/WorkflowGeneratorSteps.cs
git commit -m "feat: use selected ControlNet preprocessor resolution"
```

### Task 3: Final static review and manual handoff

**Files:**
- Review: `src/BuiltinExtensions/ComfyUIBackend/ComfyUIBackendExtension.cs`
- Review: `src/BuiltinExtensions/ComfyUIBackend/WorkflowGenerator.cs`
- Review: `src/BuiltinExtensions/ComfyUIBackend/WorkflowGeneratorSteps.cs`

- [ ] **Step 1: Run change-scope checks**

```bash
git diff --check master...HEAD
git diff --stat master...HEAD
```

Expected: no whitespace errors; only parameter registration and ControlNet preprocessor resolution flow change.

- [ ] **Step 2: Hand off manual verification**

Do not run builds or tests: repository policy reserves execution verification for the developer. In the ControlNet group, set Lineart's Preprocessor Resolution to 512 and generate or preview. Verify the workflow receives `resolution: 512`. Select a preprocessor that does not expose a `resolution` input and verify it remains unaffected.
