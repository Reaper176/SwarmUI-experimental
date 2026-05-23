# Attention Couple Regional Prompting Design

## Goal

Port the Attention Couple regional prompting technique into SwarmUI as a native Comfy backend feature, exposed as an optional regional prompting method.

The feature should improve region prompt strength and isolation for supported model families while preserving the current regional prompting behavior as the default.

## Existing Behavior

Swarm regional prompting currently parses prompt syntax through `PromptRegion` and builds masked conditionings in `WorkflowGenerator.CreateConditioning`.

Current regional prompt syntax includes:

- `<region:x,y,width,height,strength> prompt`
- `<region:background> prompt`
- `<object:x,y,width,height,strength,strength2> prompt`

The current Comfy workflow uses mask nodes plus `ConditioningSetMask` and `ConditioningCombine`. This remains the `Standard` method.

## New Parameter

Add an advanced Comfy regional prompting parameter:

```text
Regional Prompting Method
```

Values:

- `Standard`
- `Attention Couple`

Default:

- `Standard`

The parameter belongs in the existing `Regional Prompting` group.

## Attention Couple Behavior

When `Regional Prompting Method` is `Attention Couple`, Swarm will:

1. Parse positive prompt regions through the existing `PromptRegion` path.
2. Build the base/global positive conditioning from the normal global prompt, or from `<region:background>` if specified.
3. Build one positive conditioning per rectangular `<region:...>` and `<object:...>` part.
4. Build masks for each region with existing Swarm mask nodes.
5. Build a base mask for all non-region areas.
6. Normalize masks so every pixel has coverage and overlaps are divided consistently.
7. Patch the model with a native Swarm Comfy node before sampler execution.
8. Pass the base/global positive conditioning to the sampler as the normal positive conditioning.

Negative prompts are not regionalized by Attention Couple in this design. Negative prompt handling stays current/global.

## Model Support

The first implementation supports the same model families targeted by the PPM Attention Couple node:

- SD1-style `BaseModel`
- SDXL
- SDXL Refiner
- Anima

Unsupported models must fail clearly when `Attention Couple` is selected. Swarm should not silently fall back to `Standard`.

Expected unsupported families include Flux, SD3, video models, Cascade, and other DiT families unless explicitly added later.

## GLIGEN Interaction

GLIGEN and Attention Couple are separate regional engines. If `Attention Couple` is selected and a GLIGEN model is also selected, generation should fail with a clear error asking the user to choose one regional method.

## Strength Handling

`Global Region Factor` remains relevant:

- Base/global conditioning strength is controlled by `Global Region Factor`.
- Region conditioning strength uses the regional side of the blend.
- Per-region syntax strength still affects the region mask value.

This keeps the new method aligned with existing user expectations.

## Debug Masks

`Debug Regional Prompting` continues to emit the generated masks. The Attention Couple path should emit the base mask and each normalized region mask.

## Python Node Shape

Add a native Swarm Comfy node under:

```text
src/BuiltinExtensions/ComfyUIBackend/ExtraNodes/SwarmComfyCommon/
```

The node should be registered from `SwarmComfyCommon/__init__.py`.

The node will accept:

- `model`
- `base_cond`
- `base_mask`
- region conditioning and mask pairs

The node will output:

- patched `MODEL`

The implementation may use the approved Attention Couple PPM logic as source material, with Swarm naming, registration, and attribution/license notes.

## C# Workflow Shape

Refactor regional conditioning generation enough to separate:

- standard masked conditioning output
- Attention Couple model patch inputs

The sampler path must use the patched model only when:

- the method is `Attention Couple`
- the positive prompt contains regional parts

If there are no positive regional parts, the method has no effect.

## Out Of Scope

This design does not include:

- Regional negative prompts through Attention Couple.
- Flux/SD3/DiT Attention Couple support.
- Segment-derived masks for Attention Couple.
- UI redesign for drawing regions.
- Automatic fallback to Standard.
- Dependence on an externally installed `comfyui-ppm` custom node.

## Acceptance Criteria

- Default regional prompting remains unchanged.
- Selecting `Attention Couple` emits Swarm-owned Attention Couple model patch nodes for supported models with positive prompt regions.
- Unsupported models error clearly.
- Negative prompts remain global.
- GLIGEN conflict errors clearly.
- Debug regional mask output still works.
- No external `comfyui-ppm` installation is required.
