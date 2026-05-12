# LoRA Scheduling Design

## Goal

Add native ComfyUI hook LoRA scheduling to SwarmUI's default generation UI and generated Comfy workflows. Users can assign an optional denoise-progress schedule to each selected LoRA, and Swarm will emit Comfy hook scheduling nodes when the active backend supports them.

## Scope

This MVP supports raw per-LoRA schedule strings in the existing LoRA helper UI. A schedule is a semicolon-separated list of `percent:multiplier` keyframes, for example:

```text
0:0.25;0.5:1;0.8:0.7
```

Percent values are denoise progress positions from `0` to `1`. Multiplier values are LoRA strength multipliers from `-20` to `20`, matching ComfyUI hook node bounds. Empty UI schedule fields mean no schedule for that LoRA.

The visual keyframe editor, presets such as fade-in/fade-out, interpolated keyframes, and CLIP-specific scheduling controls are follow-up work.

## Data Model

Add `T2IParamTypes.LoraSchedules` as a hidden `List<string>` parameter registered beside `Loras`, `LoraWeights`, `LoraTencWeights`, and `LoraSectionConfinement`.

The list is index-aligned with the other LoRA lists. Swarm's current list parser removes empty entries, so the UI will serialize empty schedule slots as the internal sentinel `none` when at least one selected LoRA has a schedule. The schedule parser treats `null`, empty strings, whitespace, and `none` as unscheduled. If no LoRAs have schedules, the parameter remains empty.

The existing LoRA list repair handler in `T2IParamInput` will also normalize `LoraSchedules` length to match `Loras`, padding with `none` and truncating extras.

## Backend Feature Gate

Add a Comfy feature flag such as `hook_lora_scheduling`, detected from `object_info` only when all required nodes exist:

- `CreateHookLora`
- `CreateHookKeyframe`
- `SetHookKeyframes`
- `SetClipHooks`

The UI only shows the schedule editor when this feature flag is present. The workflow generator also checks the flag before emitting scheduled hook nodes and throws a user-readable error if a schedule is requested without backend support.

## UI

Extend `SelectedLora` with a `schedule` property and `setSchedule(schedule)`.

Extend `LoraHelper` with:

- `loraSchedulePref`, mirroring weight and confinement preference maps.
- `getLoraSchedulesInput()`.
- schedule loading from `input_loraschedules`.
- schedule serialization in `rebuildParams()`.

Each selected LoRA row gets a compact schedule text input under the existing row controls. The placeholder and tooltip explain the format: `percent:multiplier; percent:multiplier`.

The field updates params live, persists with presets/metadata, and reloads correctly through parameter reuse.

## Workflow Generation

Scheduling extends the existing hook LoRA path instead of adding a separate scheduler engine.

Normal unscheduled LoRAs continue through `LoadLorasForConfinement(...)` and current Comfy loader nodes. Scheduled LoRAs are skipped by normal loading and emitted through hook LoRA nodes:

1. `CreateHookLora` creates the hook LoRA for the selected LoRA.
2. The parsed schedule creates a `CreateHookKeyframe` chain.
3. `SetHookKeyframes` applies that keyframe group to the current hook output.
4. `SetClipHooks` applies the accumulated hooks to CLIP with `apply_to_conds = true` and `schedule_clip = false`.

For section and region LoRAs, this reuses `CreateHookLorasForConfinement(...)`. For global scheduled LoRAs, workflow generation will add a model-loading step after normal LoRA loading to apply scheduled hook LoRAs to the main clip path. This is necessary because the existing hook helper is currently only used directly for prompt section context.

If Comfy behavior shows that `SetHookKeyframes` schedules prior accumulated hooks unintentionally, the implementation will adjust the helper to build each scheduled LoRA hook independently before merging/chaining. The helper boundaries should keep that change local.

## Parsing And Errors

Add a small parser in the C# workflow path:

- Empty or `none`: no schedule.
- Split keyframes by `;`.
- Split each keyframe by `:`.
- Parse decimals invariantly.
- Require percent in `0..1`.
- Require multiplier in `-20..20`.
- Sort by percent before node generation.
- Deduplicate repeated percent values by keeping the last user-entered keyframe.

Invalid input throws `SwarmUserErrorException` with the LoRA name and a readable message.

## Metadata And Reuse

Metadata display should include schedules in the simplified LoRA summary, for example:

```text
my_style : 0.8 schedule="0:0.25;0.5:1"
```

Reuse logic that reorders LoRAs to match available UI options must reorder `loraweights`, `lorasectionconfinement`, and `loraschedules` together so schedules remain attached to the intended LoRA.

Prompt-inserted LoRAs that are filtered during reuse should also have matching schedule entries removed.

## Verification

Per repository policy, agents do not run builds or automated tests in this repo. Static verification will include:

- Checking all edited C# for explicit types and full braced blocks.
- Checking edited JavaScript for `let`, full braced blocks, and existing style.
- Inspecting workflow JSON generation paths for unscheduled, scheduled, mixed, and section-confined LoRA cases.
- Confirming metadata/reuse logic preserves index alignment.

Manual validation for the developer:

1. Select one LoRA, set schedule `0:0.25;0.5:1`, generate, and inspect the Comfy workflow for `CreateHookKeyframe` and `SetHookKeyframes`.
2. Generate with mixed scheduled and unscheduled LoRAs and confirm unscheduled LoRAs still use existing loader nodes.
3. Reuse parameters from the output and confirm schedules remain attached to the correct LoRAs.
4. Compare output against an equivalent hand-authored Comfy workflow using the same seed, model, LoRA, and schedule.
