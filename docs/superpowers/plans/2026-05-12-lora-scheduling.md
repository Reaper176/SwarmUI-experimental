# LoRA Scheduling Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add native ComfyUI hook LoRA scheduling to SwarmUI's default generation UI and generated Comfy workflows.

**Architecture:** Add a hidden index-aligned `LoraSchedules` parameter, expose it through the existing LoRA helper UI, detect Comfy hook scheduling support from `object_info`, and route only scheduled LoRAs through hook LoRA workflow nodes. Unscheduled LoRAs continue using the current loader path.

**Tech Stack:** C# 12 / .NET 8 server workflow generation, Newtonsoft JSON workflow nodes, browser JavaScript for genpage LoRA UI, existing SwarmUI parameter and metadata systems.

---

### Task 1: Register And Normalize `LoraSchedules`

**Files:**
- Modify: `src/Text2Image/T2IParamTypes.cs`
- Modify: `src/Text2Image/T2IParamInput.cs`

- [ ] **Step 1: Add the registered parameter field**

In `src/Text2Image/T2IParamTypes.cs`, extend the existing LoRA parameter declaration:

```cs
public static T2IRegisteredParam<List<string>> Loras, LoraWeights, LoraTencWeights, LoraSectionConfinement, LoraSchedules;
```

- [ ] **Step 2: Register `LoraSchedules` beside the existing LoRA params**

Add this immediately after `LoraSectionConfinement` registration:

```cs
LoraSchedules = Register<List<string>>(new("LoRA Schedules", "Optional per-LoRA schedule strings.\nEach entry must align with the LoRAs input and use the format percent:multiplier;percent:multiplier, for example 0:0.25;0.5:1.\nEmpty UI schedules are serialized internally as 'none'.",
    "", IgnoreIf: "", IsAdvanced: true, Group: GroupAdvancedModelAddons, VisibleNormally: false, FeatureFlag: "hook_lora_scheduling"
    ));
```

- [ ] **Step 3: Normalize schedules with the existing LoRA list repair handler**

In `src/Text2Image/T2IParamInput.cs`, inside the existing handler that fixes LoRA weight lengths, add this after the `LoraTencWeights` normalization:

```cs
if (input.TryGet(T2IParamTypes.LoraSchedules, out List<string> schedules) && schedules.Count != loras.Count)
{
    Logs.Warning($"Input has {loras.Count} loras, but {schedules.Count} schedules - the two lists must match to work properly. Applying an automatic fix.");
    schedules = [.. schedules.Take(loras.Count)];
    while (schedules.Count < loras.Count)
    {
        schedules.Add("none");
    }
    input.Set(T2IParamTypes.LoraSchedules, schedules);
}
```

- [ ] **Step 4: Static verification**

Run:

```bash
git diff -- src/Text2Image/T2IParamTypes.cs src/Text2Image/T2IParamInput.cs
```

Expected: only the new param declaration, registration, and length normalization changed.

---

### Task 2: Add Comfy Feature Detection

**Files:**
- Modify: `src/BuiltinExtensions/ComfyUIBackend/ComfyUIBackendExtension.cs`

- [ ] **Step 1: Add a feature detector helper**

Add this private helper near `AssignValuesFromRaw`:

```cs
private static void DetectHookLoraSchedulingSupport(JObject rawObjectInfo)
{
    string feature = "hook_lora_scheduling";
    string[] requiredNodes = ["CreateHookLora", "CreateHookKeyframe", "SetHookKeyframes", "SetClipHooks"];
    bool supported = requiredNodes.All(rawObjectInfo.ContainsKey);
    if (supported)
    {
        FeaturesSupported.Add(feature);
    }
    else
    {
        FeaturesSupported.Remove(feature);
    }
}
```

- [ ] **Step 2: Call the detector from `AssignValuesFromRaw`**

In `AssignValuesFromRaw`, after the loop that updates `FeaturesSupported` from `NodeToFeatureMap` and before `FeaturesDiscardIfNotFound` removals, call:

```cs
DetectHookLoraSchedulingSupport(rawObjectInfo);
```

- [ ] **Step 3: Static verification**

Run:

```bash
git diff -- src/BuiltinExtensions/ComfyUIBackend/ComfyUIBackendExtension.cs
```

Expected: a single helper and a single call. No feature is added unconditionally.

---

### Task 3: Add LoRA Schedule UI State And Serialization

**Files:**
- Modify: `src/wwwroot/js/genpage/gentab/loras.js`
- Modify: `src/wwwroot/js/genpage/gentab/params.js`

- [ ] **Step 1: Extend `SelectedLora`**

In `src/wwwroot/js/genpage/gentab/loras.js`, update the constructor and add a setter:

```js
constructor(name, weight, confinement, schedule, model) {
    this.name = name;
    this.weight = weight === 0 ? 0 : (weight || (model && model.lora_default_weight ? parseFloat(model.lora_default_weight) : null) || loraHelper.loraWeightPref[name] || 1);
    this.confinement = confinement === 0 ? 0 : (confinement || (model && model.lora_default_confinement ? parseInt(model.lora_default_confinement) : null) || loraHelper.loraConfinementPref[name] || 0);
    this.schedule = schedule || loraHelper.loraSchedulePref[name] || '';
    this.model = model;
}

/** Sets the scheduling string used by this LoRA. */
setSchedule(schedule) {
    this.schedule = schedule || '';
    loraHelper.loraSchedulePref[this.name] = this.schedule;
}
```

- [ ] **Step 2: Add `loraSchedulePref` and input helper**

Add the schedule preference map beside the existing maps:

```js
/** Map of LoRA names to their last-used schedules. */
loraSchedulePref = {};
```

Add:

```js
/** Get the "LoRA Schedules" parameter input element. */
getLoraSchedulesInput() {
    return document.getElementById('input_loraschedules');
}
```

- [ ] **Step 3: Load schedules from params**

In `loadFromParams()`, read schedules from `input_loraschedules`, treating `none` as blank:

```js
let schedulesRaw = this.getLoraSchedulesInput()?.value || '';
let schedules = schedulesRaw ? schedulesRaw.split(',') : [];
let schedule = schedules[i] == 'none' ? '' : (schedules[i] || '');
this.selected.push(new SelectedLora(loraVals[i], weight, confinement, schedule, null));
```

- [ ] **Step 4: Render the schedule field under each LoRA row**

When creating a new row in `rebuildUI()`, add a text input only if `currentBackendFeatureSet.includes('hook_lora_scheduling')`:

```js
let scheduleInput = null;
if (currentBackendFeatureSet.includes('hook_lora_scheduling')) {
    scheduleInput = document.createElement('input');
    scheduleInput.type = 'text';
    scheduleInput.className = 'auto-input lora-schedule-input';
    scheduleInput.placeholder = '0:0.25;0.5:1';
    scheduleInput.title = 'Format: percent:multiplier; percent:multiplier';
    scheduleInput.value = lora.schedule || '';
    scheduleInput.addEventListener('input', () => {
        lora.setSchedule(scheduleInput.value.trim());
        this.rebuildParams();
    });
    div.append(scheduleInput);
}
```

Store it in `this.rendered[lora.name]` as `scheduleInput`.

- [ ] **Step 5: Serialize schedules in `rebuildParams()`**

In `rebuildParams()`, collect schedules in index order:

```js
let schedulesInput = this.getLoraSchedulesInput();
let scheduleVals = this.selected.map(lora => lora.schedule || '');
let anySchedules = scheduleVals.some(s => s);
let scheduleStr = anySchedules ? scheduleVals.map(s => s || 'none').join(',') : '';
```

Set the hidden input when present:

```js
if (schedulesInput) {
    schedulesInput.value = scheduleStr;
    triggerChangeFor(schedulesInput);
}
```

- [ ] **Step 6: Listen for schedule param changes**

In `src/wwwroot/js/genpage/gentab/params.js`, extend the LoRA param change loop:

```js
for (let loraParam of ['loras', 'loraweights', 'lorasectionconfinement', 'loraschedules']) {
```

- [ ] **Step 7: Static verification**

Run:

```bash
git diff -- src/wwwroot/js/genpage/gentab/loras.js src/wwwroot/js/genpage/gentab/params.js
```

Expected: selected LoRA schedules load, render, serialize, and listen for changes without affecting unscheduled LoRAs.

---

### Task 4: Parse Schedules And Emit Hook Scheduling Nodes

**Files:**
- Modify: `src/BuiltinExtensions/ComfyUIBackend/WorkflowGenerator.cs`
- Modify: `src/BuiltinExtensions/ComfyUIBackend/WorkflowGeneratorSteps.cs`

- [ ] **Step 1: Add a small schedule keyframe record**

In `WorkflowGenerator.cs`, near the LoRA helper methods, add:

```cs
public record struct LoraScheduleKeyframe(double StartPercent, double StrengthMult);
```

- [ ] **Step 2: Add schedule lookup helpers**

Add helpers that treat missing, empty, and `none` as no schedule:

```cs
public string GetLoraScheduleAt(List<string> schedules, int index)
{
    if (schedules is null || index >= schedules.Count)
    {
        return null;
    }
    string schedule = schedules[index]?.Trim();
    if (string.IsNullOrWhiteSpace(schedule) || schedule == "none")
    {
        return null;
    }
    return schedule;
}
```

- [ ] **Step 3: Add parser**

Add:

```cs
public List<LoraScheduleKeyframe> ParseLoraSchedule(string rawSchedule, string loraName)
{
    Dictionary<double, LoraScheduleKeyframe> deduped = [];
    string[] pieces = rawSchedule.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    foreach (string piece in pieces)
    {
        string[] pair = piece.Split(':', StringSplitOptions.TrimEntries);
        if (pair.Length != 2 || string.IsNullOrWhiteSpace(pair[0]) || string.IsNullOrWhiteSpace(pair[1]))
        {
            throw new SwarmUserErrorException($"Invalid LoRA schedule for '{loraName}': keyframe '{piece}' must use percent:multiplier format.");
        }
        if (!double.TryParse(pair[0], System.Globalization.CultureInfo.InvariantCulture, out double percent))
        {
            throw new SwarmUserErrorException($"Invalid LoRA schedule for '{loraName}': percent '{pair[0]}' is not a number.");
        }
        if (!double.TryParse(pair[1], System.Globalization.CultureInfo.InvariantCulture, out double strength))
        {
            throw new SwarmUserErrorException($"Invalid LoRA schedule for '{loraName}': multiplier '{pair[1]}' is not a number.");
        }
        if (percent < 0 || percent > 1)
        {
            throw new SwarmUserErrorException($"Invalid LoRA schedule for '{loraName}': percent '{pair[0]}' must be between 0 and 1.");
        }
        if (strength < -20 || strength > 20)
        {
            throw new SwarmUserErrorException($"Invalid LoRA schedule for '{loraName}': multiplier '{pair[1]}' must be between -20 and 20.");
        }
        deduped[percent] = new(percent, strength);
    }
    return [.. deduped.Values.OrderBy(k => k.StartPercent)];
}
```

- [ ] **Step 4: Add keyframe node creation**

Add:

```cs
public JArray CreateHookKeyframesForLoraSchedule(List<LoraScheduleKeyframe> keyframes, int loraIndex)
{
    JArray last = null;
    for (int i = 0; i < keyframes.Count; i++)
    {
        LoraScheduleKeyframe keyframe = keyframes[i];
        string keyframeId = CreateNode("CreateHookKeyframe", new JObject()
        {
            ["strength_mult"] = keyframe.StrengthMult,
            ["start_percent"] = keyframe.StartPercent,
            ["prev_hook_kf"] = last
        }, GetStableDynamicID(2600 + loraIndex, i), false);
        last = [keyframeId, 0];
    }
    return last;
}
```

- [ ] **Step 5: Update `CreateHookLorasForConfinement`**

Read schedules:

```cs
List<string> schedules = UserInput.Get(T2IParamTypes.LoraSchedules);
```

After `CreateHookLora`, wrap the current hook output when a schedule is present:

```cs
JArray currentHooks = [newId, 0];
string rawSchedule = GetLoraScheduleAt(schedules, i);
if (rawSchedule is not null)
{
    if (!Features.Contains("hook_lora_scheduling"))
    {
        throw new SwarmUserErrorException("LoRA scheduling requires a recent ComfyUI backend with hook scheduling nodes.");
    }
    List<LoraScheduleKeyframe> keyframes = ParseLoraSchedule(rawSchedule, loras[i]);
    JArray hookKeyframes = CreateHookKeyframesForLoraSchedule(keyframes, i);
    string scheduledId = CreateNode("SetHookKeyframes", new JObject()
    {
        ["hooks"] = currentHooks,
        ["hook_kf"] = hookKeyframes
    }, GetStableDynamicID(2700, i), false);
    currentHooks = [scheduledId, 0];
}
last = currentHooks;
```

- [ ] **Step 6: Skip scheduled LoRAs in normal loading**

In `LoadLorasForConfinement(...)`, read schedules and skip scheduled entries:

```cs
List<string> schedules = UserInput.Get(T2IParamTypes.LoraSchedules);
```

After the confinement check and before model lookup:

```cs
if (GetLoraScheduleAt(schedules, i) is not null)
{
    continue;
}
```

- [ ] **Step 7: Apply global scheduled hook LoRAs after normal LoRA loading**

In `src/BuiltinExtensions/ComfyUIBackend/WorkflowGeneratorSteps.cs`, in the model-gen LoRA step after all `LoadLorasForConfinement(...)` calls, add:

```cs
g.LoadingClip = g.CreateHookLorasForConfinement(0, g.LoadingClip);
```

This applies global scheduled hooks to the main clip path. Unscheduled global LoRAs have already been loaded normally and are not emitted in this hook pass.

- [ ] **Step 8: Static verification**

Run:

```bash
git diff -- src/BuiltinExtensions/ComfyUIBackend/WorkflowGenerator.cs src/BuiltinExtensions/ComfyUIBackend/WorkflowGeneratorSteps.cs
```

Expected: scheduled LoRAs are parsed, gated, emitted as hook nodes, and skipped by normal loading.

---

### Task 5: Preserve Schedules In Metadata, Reuse, And Preset Additions

**Files:**
- Modify: `src/wwwroot/js/genpage/helpers/metadatahelpers.js`
- Modify: `src/wwwroot/js/genpage/gentab/currentimagehandler.js`
- Modify: `src/wwwroot/js/genpage/gentab/presets.js`
- Modify: `src/wwwroot/js/genpage/gentab/generatecontrols.js`

- [ ] **Step 1: Display schedules in metadata LoRA summary**

In `metadatahelpers.js`, read schedules beside weights:

```js
let loraSchedules = data.sui_image_params['loraschedules'];
```

When building `weight`, append a schedule if present:

```js
let schedule = loraSchedules && loraSchedules[i] != 'none' ? loraSchedules[i] : '';
if (schedule) {
    weight = `${weight} schedule="${schedule}"`;
}
```

Delete the raw schedules param after simplifying:

```js
delete data.sui_image_params['loraschedules'];
```

- [ ] **Step 2: Keep schedules aligned during reuse filtering**

In `currentimagehandler.js`, where prompted LoRAs are filtered out, read and write schedules with the same indices:

```js
let schedules = metadata.loraschedules || [];
let newSchedules = [];
...
if (schedules.length) {
    newSchedules.push(schedules[i] || 'none');
}
...
if (schedules.length) {
    metadata.loraschedules = newSchedules;
}
```

- [ ] **Step 3: Keep schedules aligned during LoRA UI reorder on reuse**

In the reorder block that creates `newLoras` and `newWeights`, also create `newSchedules` and push `metadata.loraschedules[index] || 'none'` when schedules exist. Assign `metadata.loraschedules = newSchedules` if schedules were present.

- [ ] **Step 4: Preserve schedule slots when preset logic appends LoRAs**

In both `presets.js` and `generatecontrols.js`, where `loras` and `loraweights` append raw values, add:

```js
else if (key == 'loraschedules' && rawVal) {
    val = rawVal + "," + val;
}
```

Preset authors can include `loraschedules` directly. Blank schedule slots should use `none`.

- [ ] **Step 5: Static verification**

Run:

```bash
git diff -- src/wwwroot/js/genpage/helpers/metadatahelpers.js src/wwwroot/js/genpage/gentab/currentimagehandler.js src/wwwroot/js/genpage/gentab/presets.js src/wwwroot/js/genpage/gentab/generatecontrols.js
```

Expected: metadata display and reuse reorder schedules in lockstep with LoRA names and weights.

---

### Task 6: Final Static Review And Commit

**Files:**
- Review all modified files.

- [ ] **Step 1: Search for schedule references**

Run:

```bash
rg -n "LoraSchedules|loraschedules|hook_lora_scheduling|CreateHookKeyframe|SetHookKeyframes|loraSchedule" src docs/superpowers
```

Expected: references appear only in the new param, UI, metadata/reuse, feature detection, workflow generation, and docs.

- [ ] **Step 2: Check C# style around changed code**

Run:

```bash
git diff -- '*.cs'
```

Expected: no `var` in new C# code, all `if` blocks use braces, and any new field has XML docs if it is a field.

- [ ] **Step 3: Check JavaScript style around changed code**

Run:

```bash
git diff -- '*.js'
```

Expected: new JS uses `let`, not `var` or `const`; all control blocks use braces; new functions have `/** ... */` docs.

- [ ] **Step 4: Check status**

Run:

```bash
git status -sb
```

Expected: only intended tracked files are modified. Existing unrelated untracked files such as `.codex`, `docs/additions.md`, and `docs/project-memory.md` remain uncommitted unless the user explicitly asks otherwise.

- [ ] **Step 5: Commit implementation**

Run:

```bash
git add src/Text2Image/T2IParamTypes.cs src/Text2Image/T2IParamInput.cs src/BuiltinExtensions/ComfyUIBackend/ComfyUIBackendExtension.cs src/BuiltinExtensions/ComfyUIBackend/WorkflowGenerator.cs src/BuiltinExtensions/ComfyUIBackend/WorkflowGeneratorSteps.cs src/wwwroot/js/genpage/gentab/loras.js src/wwwroot/js/genpage/gentab/params.js src/wwwroot/js/genpage/helpers/metadatahelpers.js src/wwwroot/js/genpage/gentab/currentimagehandler.js src/wwwroot/js/genpage/gentab/presets.js src/wwwroot/js/genpage/gentab/generatecontrols.js
git commit -m "Add Comfy LoRA scheduling support"
```

Expected: implementation commit is created. Do not include unrelated untracked files.
