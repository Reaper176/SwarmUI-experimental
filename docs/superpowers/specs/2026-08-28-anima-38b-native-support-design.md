# Anima 3.8B Native Support Design

## Goal

Add full Swarm-native support for 52-block Anima 3.8B models, including their paired Qwen3.5-4B progressive conditioning adapter and transparent compatibility for 28-block Anima Base, 40-block Anima 2.9B, and native 52-block LoRAs.

The integration must use models already installed in Swarm and Comfy model folders. It must not download, move, rename, or modify model files.

## Confirmed Local Layout

Read-only inspection confirmed the components needed for live validation:

- `DiffusionModels/animaDeltaMix38B_deltaMixV10.safetensors` is a 52-block Anima DiT.
- `Clip/anima38B_base_txt.safetensors` and `Clip/anima2BQwen354BText_base.safetensors` are 32-layer Qwen3.5-4B encoders.
- `ControlNet/anima38B_base.safetensors` is an 88 MB progressive adapter with `architecture=anima_progressive_qwen35_cross_adapter_v1` metadata.
- The existing native `qwen_3_06b_base.safetensors` remains the standard Anima text encoder.

The design therefore supports metadata-based adapter discovery in both Comfy's `text_encoders` and `controlnet` folders instead of requiring the adapter to be moved.

## Scope

The implementation covers:

- a distinct `Anima 3.8B` Swarm model class under the existing Anima compatibility family;
- Comfy model detection for a 52-block Anima DiT;
- loading a Qwen3.5-4B encoder through Comfy's native Qwen3.5 implementation;
- raw-prompt encoding through both native Qwen3-0.6B and Qwen3.5-4B paths;
- application of the separately stored progressive cross-attention adapter;
- generated positive and negative expanded conditioning;
- model, adapter, and encoder discovery with advanced overrides;
- normal and scheduled LoRA remapping from 28 or 40 blocks to 52 blocks;
- unchanged loading for native 52-block LoRAs;
- model-specific sampling defaults and user documentation;
- clear rejection of incompatible models, encoders, adapters, LoRAs, and controls.

Automatic model downloads and modification of downloaded ComfyUI sources are outside scope.

## Architecture

### Swarm model classification

Register an `Anima 3.8B` model class before the generic Anima class. Its detector requires the normal Anima signature and a block-51 tensor. It retains the existing Anima compatibility class and Qwen Image VAE family, so shared Anima behavior continues to work while workflow generation can select the 3.8B-specific path.

Generic 28- and 40-block Anima checkpoints remain in the existing `Anima` class. Existing Anima LoRA and ControlNet classifications remain compatible.

### Swarm-owned Comfy nodes

Add focused modules under `src/BuiltinExtensions/ComfyUIBackend/ExtraNodes/SwarmComfyCommon` and register their node mappings in the package initializer. The modules may adapt the MIT-licensed companion implementations, but should use current Comfy primitives rather than duplicate functionality that Comfy already supplies.

The nodes have four responsibilities:

1. Idempotently correct Anima `num_blocks` detection from checkpoint keys so 52-block models are constructed with all blocks.
2. Load and validate one native Qwen3.5-4B encoder, using raw prompt tokenization rather than chat or thinking templates.
3. Encode one prompt through the native Anima and Qwen3.5 paths, apply the selected progressive adapter, and return expanded plus native conditioning.
4. Remap legacy Anima LoRAs for ordinary loading and hook-based scheduled loading.

No downloaded ComfyUI file is edited.

### Workflow-generator state

For Anima 3.8B, the workflow generator retains separate references to:

- the loaded 52-block model;
- the native Qwen3-0.6B CLIP;
- the Qwen3.5-4B CLIP;
- the selected progressive adapter and its strength.

The native CLIP remains the primary `LoadingClip` so existing LoRA text-encoder handling and prompt hooks operate against the standard Anima encoder. The additional Qwen3.5 CLIP is used only by 3.8B expanded conditioning.

## Discovery and Parameters

### Qwen3.5 encoder

Add an advanced `Anima Qwen3.5 Encoder` model parameter scoped to the 3.8B path. Compatible candidates must be 32-layer, 2560-wide Qwen3.5-4B encoders.

When no override is set, prefer candidates in this order:

1. a compatible filename associated with Anima 3.8B, such as one containing `anima38b`;
2. the canonical `qwen35_4b.safetensors` name;
3. a sole remaining compatible candidate.

If multiple candidates remain without a preferred match, fail with an actionable message asking the user to set the advanced selector. Never silently load an encoder of another Qwen size.

### Progressive adapter

Add advanced `Anima 3.8B Adapter` and `Anima 3.8B Adapter Strength` parameters. Strength defaults to `1.0` and accepts the companion implementation's supported range.

Adapter candidates are discovered by safetensors metadata, not filename. Search both `text_encoders` and `controlnet`; represent the source folder unambiguously in the node and parameter value. A compatible adapter must:

- declare `architecture=anima_progressive_qwen35_cross_adapter_v1`;
- omit timestep-gate tensors;
- omit learnable-anchor or de-anchoring tensors;
- contain the complete expected progressive-adapter state.

If exactly one candidate exists, select it automatically. If none or several exist and no override resolves the choice, fail before sampling with an actionable message.

### Sampling defaults

With model-specific enhancements enabled, Anima 3.8B defaults to `res_multistep` with the `beta` scheduler. Explicit user selections always take precedence. The standard resolution remains approximately one megapixel; documentation will record the release's recommended resolution, CFG, step range, and prompt format without forcibly rewriting user prompts.

## Conditioning Data Flow

For each positive or negative prompt segment:

1. Encode the raw prompt with native Qwen3-0.6B and retain Anima's T5 token IDs and weights.
2. Tokenize the same raw prompt for Qwen3.5-4B without a chat template, assistant prefix, or thinking tokens.
3. Read Qwen3.5 semantic features from layers 7, 15, 23, and 31.
4. Load the selected progressive adapter and combine the four semantic layers for each of its six inserted cross-attention stages.
5. Run the six frozen native Anima adapter stages and their learned progressive residuals.
6. Blend expanded and native contexts according to adapter strength; `1.0` is trained strength and `0.0` is exactly native conditioning.
7. Apply native T5 token weights, pad or truncate to 512 conditioning tokens, and return expanded conditioning.
8. Supply expanded conditioning to the sampler for both positive and negative prompts.

The node also returns native conditioning for workflow compatibility and future high-resolution specialization, but this change does not automatically replace second-pass conditioning.

Swarm's `<break>` handling, regional prompts, conditioning multipliers, and Attention Couple continue to work by creating expanded conditioning independently for each prompt segment before their existing combination or masking operations.

## LoRA Compatibility

### Layout mapping

Use the verified expansion manifests from the Anima 3.8B LoRA Bridge:

- Anima Base to 2.9B insertions: `2, 5, 8, 11, 14, 17, 21, 24, 27, 30, 33, 36`.
- Anima 2.9B to 3.8B insertions: `3, 7, 11, 15, 19, 23, 27, 31, 35, 39, 43, 47`.

A 28-block LoRA maps through both insertion layouts. A 40-block LoRA maps through the second layout. A 52-block LoRA uses an identity mapping.

Support Comfy generic `diffusion_model.blocks.N` and Kohya `lora_unet_blocks_N` keys. Reject layouts with no supported block keys or references beyond block 51.

### Safe legacy behavior

For 28- and 40-block LoRAs:

- apply model patches only to inherited DiT blocks;
- leave the 24 expansion blocks untouched;
- skip internal `diffusion_model.llm_adapter` tensors because the 3.8B text adapter differs;
- preserve unrelated compatible keys and text-encoder keys for Comfy's normal loader.

This behavior avoids applying legacy weights to semantically different inserted blocks.

### Swarm loading paths

When the target model is Anima 3.8B, replace ordinary `LoraLoader` and `LoraLoaderModelOnly` nodes with Swarm bridge nodes. Do the same for `CreateHookLora` so confined, scheduled, and ramped LoRAs receive identical remapping before hook creation.

Only the 3.8B model path uses these nodes. Base Anima, Anima 2.9B, the existing Qwen3.5-2B compatibility path, and non-Anima models retain their current LoRA behavior.

## Unsupported Controls

Existing Anima LLLite weights target the original block layout and cannot be assumed safe on the twice-expanded 52-block model. If a user selects an Anima LLLite/ControlNet while using Anima 3.8B, fail with a clear unsupported-layout error instead of applying it to incorrect block numbers.

Adding a verified LLLite remapper is outside this change.

## Validation and Failure Handling

Fail before sampling for any of these conditions:

- a supposed 3.8B model is not a 52-block Anima model;
- the installed Comfy version lacks native Qwen3.5-4B support;
- a selected encoder is not the complete 32-layer, 2560-wide Qwen3.5-4B layout;
- encoder selection is unresolved or ambiguous;
- adapter selection is unresolved or ambiguous;
- adapter architecture metadata, state keys, or tensor shapes are incompatible;
- a LoRA has an unsupported block layout;
- legacy LoRA keys reference blocks outside their detected source layout;
- an original-layout Anima LLLite/ControlNet is selected.

Errors should name the failing file or selector and state the expected layout. There is no fallback to generic SD1 CLIP, another Qwen size, a filename-only adapter guess, or the ordinary unremapped LoRA loader.

## Compatibility Boundaries

The implementation must preserve:

- standard Anima generation with Qwen3-0.6B;
- existing projection-enabled Qwen3.5-2B Anima checkpoints;
- generic Anima LoRA and LLLite behavior on original-layout models;
- all non-Anima model loading and conditioning;
- user-selected samplers, schedulers, VAEs, encoders, LoRA weights, text-encoder weights, confinements, and schedules;
- model metadata and user files unchanged on disk.

## Documentation

Update `docs/Model Support.md` to distinguish base Anima from Anima 3.8B and document:

- the required DiT, native encoder, Qwen3.5 encoder, progressive adapter, and VAE;
- accepted model-folder locations;
- automatic discovery and advanced override parameters;
- prompt format and recommended sampling starting points;
- legacy LoRA remapping behavior;
- the current lack of 3.8B LLLite support.

## Verification

The maintainer granted a one-time override to the repository's normal prohibition on agent-run builds and tests for this task.

Automated verification should include:

- relevant C# formatting or lint checks available in the repository;
- Python syntax compilation for changed Swarm-managed ExtraNodes;
- a repository build;
- focused deterministic tests or scripts for 28-to-52, 40-to-52, and 52-to-52 key mapping;
- validation of rejection paths for unsupported LoRA layouts and malformed adapter metadata;
- generated-workflow inspection for 3.8B with no LoRA, ordinary LoRAs, scheduled LoRAs, positive and negative prompts, and explicit encoder/adapter overrides;
- regression inspection for base Anima and non-Anima workflows;
- final diff review that excludes unrelated worktree and user-data changes.

The maintainer will perform live GPU generation using the confirmed local model files. A successful live check should cover the base 3.8B path plus representative 28-, 40-, and 52-block LoRAs.

## Source Attribution

The implementation may adapt MIT-licensed code and mapping data from:

- `GumGum10/comfyui-anima-3-8B` for Qwen3.5-4B progressive conditioning;
- `Lakeside529/ComfyUI-Anima-3.8B-LoRA-Bridge` for expansion mappings and safe legacy-LoRA behavior.

Any adapted source file must retain appropriate attribution and license notices.
