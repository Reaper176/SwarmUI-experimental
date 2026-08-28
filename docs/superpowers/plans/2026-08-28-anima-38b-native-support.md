# Anima 3.8B Native Support Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan.

**Goal:** Add full Swarm-native generation support for Anima 3.8B, using the existing local Qwen3.5 encoder and progressive adapter files, plus transparent loading of legacy Anima Base/2.9B LoRAs through a block-remapping bridge.

**Architecture:** Register Anima 3.8B as a distinct Swarm model subtype while retaining Anima compatibility. Add Swarm-managed Comfy nodes for model identification, Qwen3.5 semantic encoding, progressive cross-attention conditioning, and legacy LoRA remapping. Route only the new subtype through these nodes, preserving existing Anima behavior and the native Qwen 0.6B first-stage conditioning path.

**Tech Stack:** C# 12/.NET 8, SwarmUI workflow generation, Python/ComfyUI custom nodes, PyTorch, safetensors, Python unittest.

**Design reference:** `docs/superpowers/specs/2026-08-28-anima-38b-native-support-design.md`

**Repository constraints:** Reaper176 is an approved maintainer. The user explicitly authorized model-directory inspection and granted a one-time build/test override for this task. Do not modify `Models/`, `Data/`, `Output/`, `dlbackend/`, or unrelated dirty files.

## Task 1: Register the Anima 3.8B subtype and Comfy node contracts

**Files:**

- Modify: `src/Text2Image/T2IModelClassSorter.cs`
- Modify: `src/BuiltinExtensions/ComfyUIBackend/ComfyNodeNames.cs`
- Modify: `src/BuiltinExtensions/ComfyUIBackend/ComfyNodeInputNames.cs`

- [ ] **Step 1: Add subtype detection expectations before implementation**

Document the intended classifier conditions next to the existing Anima classifier while editing: a 52-block Anima diffusion model must resolve to subtype ID `anima-3_8b`; generic/older Anima models must continue to resolve as `anima`. Keep `CompatAnima` on both classes.

- [ ] **Step 2: Implement the minimal classifier**

Add an Anima 3.8B model class immediately before the generic Anima class so the specific match wins. Reuse existing metadata/tensor inspection helpers. Match the Anima architecture markers plus evidence of block 51/52 blocks; do not classify arbitrary 52-block diffusion models as Anima.

- [ ] **Step 3: Define node and input-name constants**

Add constants for:

```text
SwarmLoadAnima38Qwen35
SwarmAnima38Conditioning
SwarmAnima38LoraLoader
SwarmAnima38LoraLoaderModelOnly
SwarmAnima38CreateHookLora
```

Add input constants for the Qwen filename, adapter selection, adapter strength, prompt, source model, LoRA name, model, clip, and hook inputs used by those nodes. Follow existing naming conventions instead of embedding repeated string literals in workflow code.

- [ ] **Step 4: Statically trace classification order**

Confirm a tensor key ending in `diffusion_model.blocks.51...` reaches `anima-3_8b`, while an older Anima checkpoint still reaches `anima`.

- [ ] **Step 5: Commit**

```bash
git add src/Text2Image/T2IModelClassSorter.cs src/BuiltinExtensions/ComfyUIBackend/ComfyNodeNames.cs src/BuiltinExtensions/ComfyUIBackend/ComfyNodeInputNames.cs
git commit -m "Register Anima 3.8B model subtype"
```

## Task 2: Build and test the pure legacy LoRA mapping layer

**Files:**

- Create: `src/BuiltinExtensions/ComfyUIBackend/ExtraNodes/SwarmComfyCommon/SwarmAnima38LoraMapping.py`
- Create: `src/BuiltinExtensions/ComfyUIBackend/ExtraNodes/SwarmComfyCommon/tests/__init__.py`
- Create: `src/BuiltinExtensions/ComfyUIBackend/ExtraNodes/SwarmComfyCommon/tests/test_anima38_lora_mapping.py`

- [ ] **Step 1: Write failing mapping tests**

Cover all required formats and edge cases:

```python
def test_base_28_block_mapping_reaches_52_blocks(): ...
def test_anima_29_40_block_mapping_reaches_52_blocks(): ...
def test_native_52_block_mapping_is_identity(): ...
def test_generic_diffusion_model_key_is_rewritten(): ...
def test_kohya_lora_unet_key_is_rewritten(): ...
def test_adapter_keys_are_skipped_for_legacy_sources(): ...
def test_unrelated_keys_are_unchanged(): ...
```

Assert the exact insertion schedules:

```python
BASE_TO_29_INSERTIONS = (2, 5, 8, 11, 14, 17, 21, 24, 27, 30, 33, 36)
ANIMA_29_TO_38_INSERTIONS = (3, 7, 11, 15, 19, 23, 27, 31, 35, 39, 43, 47)
```

Detect source depth from the highest referenced block: up to 27 means 28-block Base, up to 39 means 40-block Anima 2.9B, and up to 51 means native Anima 3.8B. Test boundary blocks around insertions rather than only first/last blocks.

- [ ] **Step 2: Run the tests and verify they fail for the missing module**

```bash
python3 -m unittest src.BuiltinExtensions.ComfyUIBackend.ExtraNodes.SwarmComfyCommon.tests.test_anima38_lora_mapping -v
```

Expected: import/module failure before implementation.

- [ ] **Step 3: Implement the pure mapper**

Implement side-effect-free helpers to:

- find source block count from generic `diffusion_model.blocks.N` and Kohya `lora_unet_blocks_N` keys;
- map 28 blocks through both insertion schedules;
- map 40 blocks through the second schedule;
- leave 52-block keys unchanged;
- omit `diffusion_model.llm_adapter.*` tensors when the source is legacy;
- preserve tensor objects and unrelated keys without copying tensor data.

Raise a clear error for an inferred architecture beyond supported depths instead of silently corrupting keys.

- [ ] **Step 4: Run the focused tests**

```bash
python3 -m unittest src.BuiltinExtensions.ComfyUIBackend.ExtraNodes.SwarmComfyCommon.tests.test_anima38_lora_mapping -v
python3 -m py_compile src/BuiltinExtensions/ComfyUIBackend/ExtraNodes/SwarmComfyCommon/SwarmAnima38LoraMapping.py
```

Expected: all mapping tests pass and compilation succeeds.

- [ ] **Step 5: Commit**

```bash
git add src/BuiltinExtensions/ComfyUIBackend/ExtraNodes/SwarmComfyCommon/SwarmAnima38LoraMapping.py src/BuiltinExtensions/ComfyUIBackend/ExtraNodes/SwarmComfyCommon/tests
git commit -m "Add Anima legacy LoRA block mapper"
```

## Task 3: Add ordinary and scheduled Anima 3.8B LoRA nodes

**Files:**

- Create: `src/BuiltinExtensions/ComfyUIBackend/ExtraNodes/SwarmComfyCommon/SwarmAnima38Lora.py`
- Modify: `src/BuiltinExtensions/ComfyUIBackend/ExtraNodes/SwarmComfyCommon/tests/test_anima38_lora_mapping.py`

- [ ] **Step 1: Add failing loader-decision tests**

Extract/test a small helper that decides whether remapping is required and verifies native 52-block LoRAs bypass rewriting. Keep tests independent of a running Comfy server.

- [ ] **Step 2: Implement shared LoRA loading**

Load the chosen LoRA with Comfy folder-path and safetensors utilities, run its state dict through the pure mapper, and preserve Comfy cache behavior where practical. Include the source depth in an informative log message whenever remapping occurs.

- [ ] **Step 3: Implement three Comfy nodes**

Implement:

- `SwarmAnima38LoraLoader`: model + clip loading equivalent to `LoraLoader`;
- `SwarmAnima38LoraLoaderModelOnly`: model-only equivalent;
- `SwarmAnima38CreateHookLora`: scheduled/hook loading using `comfy.hooks.create_hook_lora` after remapping.

Match upstream input/output types and strength semantics so workflow routing can substitute these nodes without changing user-facing LoRA behavior.

- [ ] **Step 4: Verify unit and syntax checks**

```bash
python3 -m unittest src.BuiltinExtensions.ComfyUIBackend.ExtraNodes.SwarmComfyCommon.tests.test_anima38_lora_mapping -v
python3 -m py_compile src/BuiltinExtensions/ComfyUIBackend/ExtraNodes/SwarmComfyCommon/SwarmAnima38Lora.py
```

- [ ] **Step 5: Commit**

```bash
git add src/BuiltinExtensions/ComfyUIBackend/ExtraNodes/SwarmComfyCommon/SwarmAnima38Lora.py src/BuiltinExtensions/ComfyUIBackend/ExtraNodes/SwarmComfyCommon/tests/test_anima38_lora_mapping.py
git commit -m "Add Anima 3.8B LoRA loader nodes"
```

## Task 4: Implement native Qwen3.5 semantic conditioning and adapter nodes

**Files:**

- Create: `src/BuiltinExtensions/ComfyUIBackend/ExtraNodes/SwarmComfyCommon/SwarmAnima38.py`
- Create: `src/BuiltinExtensions/ComfyUIBackend/ExtraNodes/SwarmComfyCommon/tests/test_anima38_helpers.py`

- [ ] **Step 1: Write failing helper tests**

Test pure helpers for:

- Qwen candidate filtering and deterministic `auto` selection;
- adapter discovery/tagging across `text_encoders` and `controlnet`;
- companion key normalization (`embed_tokens.*`/`layers.*` to native Qwen paths);
- ignoring companion-only projection `norm.*` keys;
- unified prompt formatting for tags plus `Description:`;
- exact semantic tap request `[8, 16, 24, 32]` corresponding to companion post-layers 7/15/23/31.

- [ ] **Step 2: Implement model detection patching**

Patch Comfy model detection narrowly enough to recognize the 52-block Anima 3.8B diffusion architecture. Make patch installation idempotent, preserve upstream behavior for all other models, and avoid editing downloaded Comfy sources.

- [ ] **Step 3: Implement `SwarmLoadAnima38Qwen35`**

Expose a dropdown containing `auto` plus filtered Qwen3.5 4B candidates. For companion-format files, normalize state-dict keys and construct Comfy's native Qwen3.5 4B encoder with 32 layers, hidden size 2560, intermediate size 9216, and final norm disabled. Use the raw/base Qwen3.5 tokenizer, not an image-chat template.

Fail with a clear message listing expected filename markers if `auto` cannot find a suitable file.

- [ ] **Step 4: Implement progressive adapter discovery/loading**

Search both text-encoder and ControlNet model roots. Read safetensors metadata and accept architecture `anima_progressive_qwen35_cross_adapter_v1`. Present tagged paths (`text_encoders::...`, `controlnet::...`) to avoid ambiguity. Implement the four semantic attentions, layer-mix logits, source/query norms, and strength scaling expected by the companion architecture.

- [ ] **Step 5: Implement `SwarmAnima38Conditioning`**

Inputs: diffusion model, native Qwen 0.6B clip, Qwen3.5 semantic clip, adapter choice, prompt, adapter strength. Encode the native conditioning normally, encode Qwen3.5 intermediate states at taps `[8,16,24,32]`, apply the progressive adapter, and attach the resulting model patches/conditioning in the exact form the 52-block Anima model expects.

Preserve masks, pooled outputs, and conditioning metadata needed by regions and Attention Couple. Make the adapter selection/shape errors actionable.

- [ ] **Step 6: Run focused verification**

```bash
python3 -m unittest src.BuiltinExtensions.ComfyUIBackend.ExtraNodes.SwarmComfyCommon.tests.test_anima38_helpers -v
python3 -m py_compile src/BuiltinExtensions/ComfyUIBackend/ExtraNodes/SwarmComfyCommon/SwarmAnima38.py
```

- [ ] **Step 7: Commit**

```bash
git add src/BuiltinExtensions/ComfyUIBackend/ExtraNodes/SwarmComfyCommon/SwarmAnima38.py src/BuiltinExtensions/ComfyUIBackend/ExtraNodes/SwarmComfyCommon/tests/test_anima38_helpers.py
git commit -m "Add Anima 3.8B semantic conditioning nodes"
```

## Task 5: Register nodes and publish dynamic model choices to Swarm

**Files:**

- Modify: `src/BuiltinExtensions/ComfyUIBackend/ExtraNodes/SwarmComfyCommon/__init__.py`
- Modify: `src/BuiltinExtensions/ComfyUIBackend/ComfyUIBackendExtension.cs`

- [ ] **Step 1: Register Python nodes**

Import the two new modules and add all five node classes/display names to the existing mapping dictionaries. Follow the module's current optional-import/error behavior.

- [ ] **Step 2: Add shared dynamic choice state**

Following the existing `SharedValueCandidate`/`SharedValueDelta` pattern, collect Qwen and adapter dropdown values from backend `object_info`. Merge choices across backends and publish stable lists beginning with `auto`.

- [ ] **Step 3: Register Swarm parameters**

Add model-scoped parameters shown only for subtype `anima-3_8b`:

```text
Anima Qwen3.5 Encoder       string choices, default auto
Anima 3.8B Adapter          string choices, default auto
Anima 3.8B Adapter Strength double 0..2, default 1
```

Place them with model/loading advanced parameters and provide concise descriptions explaining automatic selection.

- [ ] **Step 4: Compile/check the touched code**

```bash
python3 -m py_compile src/BuiltinExtensions/ComfyUIBackend/ExtraNodes/SwarmComfyCommon/__init__.py
dotnet build src/SwarmUI.csproj --no-restore
```

- [ ] **Step 5: Commit**

```bash
git add src/BuiltinExtensions/ComfyUIBackend/ExtraNodes/SwarmComfyCommon/__init__.py src/BuiltinExtensions/ComfyUIBackend/ComfyUIBackendExtension.cs
git commit -m "Expose Anima 3.8B model choices"
```

## Task 6: Generate the paired native/semantic conditioning workflow

**Files:**

- Modify: `src/BuiltinExtensions/ComfyUIBackend/WorkflowGeneratorModelSupport.cs`
- Modify: `src/BuiltinExtensions/ComfyUIBackend/WorkflowGenerator.cs`
- Modify: `src/BuiltinExtensions/ComfyUIBackend/WorkflowGeneratorSteps.cs`

- [ ] **Step 1: Add subtype helper and per-model semantic-clip cache**

Add `IsAnima38()` beside `IsAnima()`. Keep the normal Anima Qwen 0.6B clip as `LoadingClip`. Add a dictionary keyed consistently with the existing per-model cache to hold the separately loaded Qwen3.5 semantic clip.

- [ ] **Step 2: Load both encoders only for Anima 3.8B**

In model loading, keep the existing native Anima clip/VAE path, then create `SwarmLoadAnima38Qwen35` from the user's selected encoder. Do not alter generic Anima or unrelated model workflows.

- [ ] **Step 3: Route direct conditioning**

In `CreateConditioningDirect`, before the generic Anima path, create `SwarmAnima38Conditioning` with:

- current diffusion model;
- native clip;
- cached semantic clip;
- selected adapter;
- raw prompt;
- adapter strength.

Preserve prompt multiplier handling, `<break>` segmentation, regional masks, and Attention Couple integration. Both positive and negative conditioning must follow the same subtype-aware construction.

- [ ] **Step 4: Apply Anima 3.8B sampler defaults**

Place the specific branch before generic Anima defaults. Use sampler `res_multistep` and scheduler `beta` when the user has not explicitly chosen values. Do not override explicit sampler/scheduler input.

- [ ] **Step 5: Statically inspect generated JSON paths**

Trace one base generation and one regional-prompt generation. Confirm the graph contains both encoders, one adapter selection, subtype conditioning for positive/negative prompts, and no orphaned native conditioning nodes.

- [ ] **Step 6: Build**

```bash
dotnet build src/SwarmUI.csproj --no-restore
```

- [ ] **Step 7: Commit**

```bash
git add src/BuiltinExtensions/ComfyUIBackend/WorkflowGeneratorModelSupport.cs src/BuiltinExtensions/ComfyUIBackend/WorkflowGenerator.cs src/BuiltinExtensions/ComfyUIBackend/WorkflowGeneratorSteps.cs
git commit -m "Generate native Anima 3.8B workflows"
```

## Task 7: Route legacy LoRAs and guard incompatible Anima LLLite usage

**Files:**

- Modify: `src/BuiltinExtensions/ComfyUIBackend/WorkflowGeneratorSteps.cs`
- Modify: `src/BuiltinExtensions/ComfyUIBackend/WorkflowGenerator.cs`

- [ ] **Step 1: Route ordinary LoRA loading**

In `LoadLorasForConfinement`, substitute `SwarmAnima38LoraLoader` or its model-only variant only when `IsAnima38()` is true. Preserve clip-strength, model-strength, confinement, and user ordering semantics.

- [ ] **Step 2: Route scheduled/hook LoRAs**

In `CreateHookLorasForConfinement`, use `SwarmAnima38CreateHookLora` for the new subtype. Preserve hook chaining and schedule data exactly.

- [ ] **Step 3: Reject Anima LLLite on 3.8B**

Before generating `SwarmAnimaLLLite` nodes, throw a user-facing validation error for subtype `anima-3_8b`. Explain that block mapping for those adapters is not known-safe; do not silently ignore the user's selection.

- [ ] **Step 4: Statically trace four paths**

Check native 52-block LoRA, 40-block LoRA, 28-block LoRA, and scheduled LoRA workflows. Confirm only the selected subtype receives bridge nodes and ordinary Anima remains unchanged.

- [ ] **Step 5: Verify**

```bash
python3 -m unittest discover -s src/BuiltinExtensions/ComfyUIBackend/ExtraNodes/SwarmComfyCommon/tests -v
dotnet build src/SwarmUI.csproj --no-restore
```

- [ ] **Step 6: Commit**

```bash
git add src/BuiltinExtensions/ComfyUIBackend/WorkflowGeneratorSteps.cs src/BuiltinExtensions/ComfyUIBackend/WorkflowGenerator.cs
git commit -m "Route Anima LoRAs through compatibility bridge"
```

## Task 8: Document support and perform complete verification

**Files:**

- Modify: `docs/Model Support.md` (or the repository's current canonical model-support page found during implementation)
- Review: all files changed by Tasks 1–7

- [ ] **Step 1: Add concise user documentation**

Document:

- automatic recognition of 52-block Anima 3.8B checkpoints;
- required Qwen3.5 4B semantic encoder and progressive adapter;
- `auto` discovery behavior and manual selectors;
- native support for legacy Base/2.9B LoRAs and native 3.8B LoRAs;
- unsupported Anima LLLite mapping;
- recommended starting defaults: about 1 MP, CFG 7–8, 28–50 steps, `res_multistep` + `beta`.

Do not claim second-pass/high-resolution Qwen specialization in this initial implementation.

- [ ] **Step 2: Run full scoped Python verification**

```bash
python3 -m unittest discover -s src/BuiltinExtensions/ComfyUIBackend/ExtraNodes/SwarmComfyCommon/tests -v
python3 -m py_compile src/BuiltinExtensions/ComfyUIBackend/ExtraNodes/SwarmComfyCommon/SwarmAnima38.py src/BuiltinExtensions/ComfyUIBackend/ExtraNodes/SwarmComfyCommon/SwarmAnima38Lora.py src/BuiltinExtensions/ComfyUIBackend/ExtraNodes/SwarmComfyCommon/SwarmAnima38LoraMapping.py src/BuiltinExtensions/ComfyUIBackend/ExtraNodes/SwarmComfyCommon/__init__.py
```

- [ ] **Step 3: Run full scoped C# verification**

```bash
dotnet build src/SwarmUI.csproj --no-restore
dotnet format src/SwarmUI.csproj --verify-no-changes --no-restore
dotnet format style src/SwarmUI.csproj --verify-no-changes --no-restore
```

If repository-wide formatting reports pre-existing unrelated findings, record them and verify formatting only for touched files without editing unrelated code.

- [ ] **Step 4: Inspect the final diff**

```bash
git diff --check
git status --short
git diff --stat
git diff
```

Confirm no model/data files, downloaded Comfy sources, generated binaries, or unrelated dirty files are included.

- [ ] **Step 5: Perform implementation self-review**

Check every requirement in the design spec against the diff. Search touched code for placeholders, duplicated string literals that should use node constants, ambiguous tensor dimensions, broad monkey patches, and paths that accidentally affect generic Anima.

- [ ] **Step 6: Commit documentation/final corrections**

```bash
git add docs/Model\ Support.md
git commit -m "Document Anima 3.8B native support"
```

- [ ] **Step 7: Prepare live GPU verification handoff**

Provide a short manual checklist for the maintainer:

1. Restart all Comfy backends so new nodes/object-info are loaded.
2. Select the local 52-block Anima checkpoint and verify automatic subtype detection.
3. Confirm `auto` selects the local Qwen3.5 encoder and progressive adapter.
4. Generate one 832x1216 image with 28 steps, CFG 7, `res_multistep`/`beta`.
5. Repeat with one native 3.8B LoRA, one Anima 2.9B LoRA, and one Base LoRA.
6. Exercise a scheduled LoRA and a regional prompt.
7. Confirm generic Anima workflows are unchanged.

Do not run expensive GPU generation automatically unless the user separately requests that live run and confirms the active backend/device.
