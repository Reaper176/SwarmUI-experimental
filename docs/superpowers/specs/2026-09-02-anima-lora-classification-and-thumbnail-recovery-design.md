# Anima LoRA Classification and Thumbnail Recovery Design

**Date:** 2026-09-02

## Scope

Resolve two independent model-scan diagnostics:

1. A file named `kafka02_illu.thumb.jpg` is offered to the legacy thumbnail loader even though its bytes are not an image.
2. Anima LoRAs using the standard `diffusion_model.blocks.*.<adapter>.weight` tensor layout are not recognized by the existing `anima/lora` classifier.

The work is bounded to one recoverable user-data quarantine operation, the Anima LoRA predicate, targeted cached-classification invalidation, and classification-harness coverage. It does not alter model loading, LoRA application, thumbnail decoding, or unrelated classifier predicates.

## Evidence and Root Causes

### Invalid thumbnail

The reported 24,236-byte `.thumb.jpg` starts with `07 00`, not a JPEG start-of-image marker, and an independent decoder reports that it is not a JPEG. Read-only inspection shows serialized database-style records rather than image pixels. `T2IModelHandler.GetAutoFormatImage` therefore reaches the expected exception boundary when it passes the mislabeled bytes to the image decoder.

This is a bad sidecar file, not a decoder defect. The source handler already catches the failure, records details at debug level, and continues scanning.

### Anima LoRA classification

The reported LoRA declares `ss_base_model_version=anima` and contains 896 LoRA tensors across Anima blocks 0 through 27. Its representative keys include:

- `diffusion_model.blocks.27.self_attn.v_proj.lora_A.weight`
- `diffusion_model.blocks.27.cross_attn.output_proj.lora_A.weight`
- `diffusion_model.blocks.27.mlp.layer2.lora_A.weight`

`hasLoraKey` accepts semantic tensor names and expands them across maintained prefixes and adapter suffixes, including `diffusion_model.*`, `model.diffusion_model.*`, and Kohya-style `lora_unet_*`. The second `isAnimaLora` branch instead passes names already prefixed with `lora_unet_`. The helper consequently searches for double-expanded or otherwise nonexistent names and misses both the reported Diffusers-style model and the intended Kohya-style equivalents.

## Chosen Design

### Thumbnail recovery

With SwarmUI stopped, rename the invalid sidecar in place from `kafka02_illu.thumb.jpg` to `kafka02_illu.thumb.jpg.invalid`. This is recoverable, preserves the original bytes and timestamps, and makes the filename cease matching the maintained thumbnail suffix list. No image or model data is rewritten.

### Classifier correction

Retain the existing two-branch Anima LoRA predicate. Keep its first branch unchanged for LoRAs that target the Anima LLM adapter. Correct the second branch to pass these semantic names to `hasLoraKey`:

- `blocks.27.self_attn.v_proj`
- `blocks.27.cross_attn.output_proj`
- `blocks.27.mlp.layer2`

This is format-based rather than filename-based. It recognizes all LoRAs carrying that distinguishing triplet under any prefix/suffix convention already supported by `hasLoraKey`. Requiring all three tensors keeps the change narrow and avoids classifying unrelated or ambiguous adapters from a single common block key.

Do not add a filename rule, block-count heuristic, or metadata-only `ss_base_model_version` fallback. Those approaches would cover sparse adapters but would trust weaker evidence and increase false-positive risk. A later change can add a separately evidenced sparse-Anima signature if real samples require it.

### Cached classification refresh

Increment the model-class cache revision and extend targeted staleness detection so a prior null classification in the LoRA model handler is retried once. Preserve the existing targeted Cosmos recheck. Do not invalidate successfully classified LoRAs or null classifications in other model handlers.

The recheck must still require a readable model header before replacing cached classification data. If header loading or classification fails, retain the prior cache record and leave it retryable, matching the existing stale-classification safety policy.

## Verification Design

Extend the existing Anima classification harness with synthetic LoRA headers covering:

1. the reported `diffusion_model.*.lora_A.weight` convention resolves to `anima/lora`;
2. its corresponding `model.diffusion_model.*` prefix resolves to `anima/lora`;
3. the Kohya `lora_unet_*.lora_up.weight` convention resolves to `anima/lora`;
4. the existing LLM-adapter signature remains recognized;
5. near-miss headers missing each required semantic tensor remain unclassified as Anima;
6. representative non-Anima LoRAs retain their existing results;
7. stale null LoRA cache records are eligible for one targeted retry, while classified LoRAs and null non-LoRA records are not;
8. failed or null reclassification does not replace the existing cache record.

Repository policy leaves runtime/build/test execution to the maintainer. Agent verification is limited to source review, focused diff inspection, formatting/lint checks that do not build or execute tests, and verifying the quarantine filename no longer matches a supported thumbnail suffix.

## Compatibility and Rollback

No public API, model-class ID, compatibility class, tensor loader, or metadata schema changes. Existing `anima/lora` consumers receive the same class object. The only persistence change is a revision increment using the existing cache-recheck mechanism.

Rollback restores the prior predicate and cache revision logic, then renames the quarantined thumbnail back to its original name if retaining the diagnostic is desired. The invalid thumbnail bytes remain preserved throughout.
