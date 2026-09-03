# Additional Standard LoRA Layouts Design

## Problem

SwarmUI reports valid LoRAs as unclassified when their tensor set is sparse, uses a trainer layout not covered by the current exact-key checks, or uses LyCORIS tensor names. The reported set contains multiple architectures and two files that should not be treated as ordinary LoRAs.

The supported targets are:

- Stable Diffusion XL LoRAs, including Pony and Illustrious derivatives.
- Stable Diffusion 1.5 LoRAs with attention-only or otherwise sparse tensor sets.
- The Flux.2 Klein 4B outpaint LoRA.

The excluded targets are:

- `SD_1.5.safetensors`, whose tensors are `image_proj.*` and `ip_adapter.*`; it is an IP-Adapter payload placed in the LoRA folder.
- `animaStyleTest02_*`, whose tensors are `style_kv_dit_blocks_*`; these are Anima dual-KV style networks for which SwarmUI has no corresponding loader path.

## Evidence

The affected SDXL files use SDXL UNet structure, including multiple transformer blocks per attention module, or provide `.cm-info.json` metadata with `BaseModel` equal to `Pony` or `Illustrious`. The affected SD 1.5 files use the fourth UNet up block, which SDXL does not have, or provide `BaseModel: SD 1.5`.

The Flux.2 Klein 4B file is a complete low-rank adapter over five double blocks and twenty single blocks. Its tensors show a 3072-wide hidden state, an 18432-wide double-stream modulation output, and a 7680-wide text input. It fails only because the existing classifier requires `double_blocks.4.txt_mlp.2`, a module the outpaint adapter does not target.

Folder and filename text is not architectural evidence and must not be used.

## Classification Design

### Sidecar base-model hints

Add a case-insensitive helper that reads `BaseModel` from either the combined metadata object's top level or `__metadata__`.

For LoRA class predicates:

- `Pony` and `Illustrious` select the existing `stable-diffusion-xl-v1-base/lora` class.
- `SD 1.5` selects the existing `stable-diffusion-v1/lora` class.

These predicates remain behind `IdentifyClassFor`'s global LoRA gate. A sidecar alone cannot turn a non-LoRA tensor file into a LoRA. In particular, the misplaced IP-Adapter must remain unclassified.

### Tensor-only SDXL and SD 1.5 signatures

Extend SDXL LoRA recognition with an architecture-specific Diffusers signature that requires LoRA tensors in a `transformer_blocks_1` module. Stable Diffusion 1.5 has one transformer block per attention module, while SDXL has multi-block attention modules.

Extend SD 1.5 LoRA recognition with a signature requiring a LoRA tensor in `up_blocks_3`. SDXL has three up blocks indexed 0 through 2, while Stable Diffusion 1.5 has four indexed 0 through 3.

The signatures must use actual LoRA tensor suffixes already supported by the classifier and must not infer from titles, directories, or filenames. Sparse tensor sets that lack either distinguishing structure remain unknown unless supported by an explicit base-model hint.

### Flux.2 Klein 4B signature

Keep the existing complete module-name signature. Add an alternative dimension-based signature requiring all of:

- a low-rank `double_stream_modulation_img.lin` tensor whose adapter output dimension is 18432;
- a low-rank `txt_in` tensor whose adapter input dimension is 7680;
- representative endpoints from the five-double-block and twenty-single-block 4B structure.

The signature must continue to distinguish Klein 4B from Klein 9B and Flux.2 Dev. It must not weaken those predicates or the Flux.1 exclusion.

## Cache Migration

Increase `ModelClassCacheRevision` from 3 to 4. Existing unknown LoRA cache entries will then be rechecked through the combined tensor and sidecar metadata path already established for stale unknown entries.

Known cached classes, including the protected Cosmos classification path, retain their current raw-header provenance rules. An inconclusive or failed recheck remains retryable and is not persisted as a successful revision-4 decision.

## Tests

Extend the existing standalone classification regression harness before production changes. It must first fail for the missing behavior and then pass after implementation.

Positive cases:

- Pony and Illustrious sidecar hints classify as SDXL LoRA, case-insensitively.
- `SD 1.5` sidecar hint classifies a genuine LoRA as Stable Diffusion 1 LoRA.
- Metadata-free SDXL multi-transformer-block layouts classify as SDXL LoRA.
- Metadata-free SD 1.5 fourth-up-block layouts classify as Stable Diffusion 1 LoRA.
- The Flux.2 Klein 4B dimensional layout classifies as the 4B LoRA class.

Negative and separation cases:

- An IP-Adapter tensor set with `BaseModel: SD 1.5` remains unknown.
- An Anima dual-KV style-network tensor set remains unknown.
- Ambiguous sparse SD-family tensor sets without sidecar metadata remain unknown.
- The new SDXL signature does not classify SD 1.5 and vice versa.
- Flux.2 Klein 9B and Flux.2 Dev cases do not classify as Klein 4B.
- Existing Anima/Cosmos provenance and cache-persistence regression cases remain green.
- The harness requires cache revision 4.

## Scope and Safety

The change is limited to the model-class predicates, the targeted cache revision, and regression fixtures. It does not add new model loader support, modify model files or sidecars, rename user files, suppress warnings globally, or alter unrelated metadata behavior.
