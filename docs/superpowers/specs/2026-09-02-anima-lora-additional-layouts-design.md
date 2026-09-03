# Additional Anima LoRA Layout Classification Design

## Problem

The existing Anima LoRA classifier recognizes complete Diffusers and standard
Kohya LoRA exports built around three block-27 modules. Real Anima LoRAs in the
local model library use additional legitimate layouts:

- sparse LoRAs that train only cross-attention modules;
- sparse LoRAs that train only self-attention and MLP modules;
- LyCORIS LoHa and LoKr exports with flattened Kohya names and non-LoRA tensor
  suffixes;
- a remapped 40-block Anima layout with an LLM adapter but no block 27.

One warned file, `modelo_1mb2.safetensors`, has neither Anima tensor names nor
identifying metadata. Its directory and filename are not sufficient evidence
for an Anima classification.

## Classification Design

Keep classification based on model contents, never directory or filename.

Extend the shared LoRA-key matcher to recognize flattened Kohya LyCORIS module
keys ending in `.hada_w1_a` and `.lokr_w1`. Existing Diffusers and standard
Kohya forms remain unchanged.

Extend the Anima LoRA predicate with three evidence-backed signatures:

1. A complete block-27 cross-attention projection group: key, query, value,
   and output projections.
2. A complete block-27 self-attention projection group plus both MLP layers.
3. The remapped layout's LLM-adapter marker together with representative
   block-39 attention and adaptive-normalization modules.

Each signature requires multiple architecture-specific tensors. A single
block-27 tensor remains insufficient, and the existing incomplete near-miss
must remain unclassified.

## Cache Migration

Advance `ModelClassCacheRevision` from 2 to 3. Preserve the existing targeted
policy: only stale Cosmos classifications and stale null or whitespace LoRA
classifications are re-evaluated. Other null model types are not forced to
recheck. A failed header read or a still-null result is not persisted, so it
remains retryable.

This causes LoRAs that were cached as unknown under revision 2 to receive one
classification attempt using the expanded signatures.

## Regression Coverage

Extend the existing Anima classification harness with representative headers
for:

- sparse cross-attention standard Kohya LoRA;
- sparse self-attention/MLP standard Kohya LoRA;
- full LoHa and LoKr flattened Kohya layouts;
- remapped block-39 Diffusers LoRA;
- the existing complete Diffusers, nested Diffusers, and standard Kohya
  layouts;
- incomplete and unrelated sparse layouts that must remain null;
- cache migration from revision 2 to revision 3.

The harness invokes production classification rather than duplicating the
predicate.

## Scope and Validation

Production changes are limited to the class sorter and cache revision.
Regression changes are limited to the existing manual harness. Model files are
read only for diagnosis and are not modified.

Per repository policy, the agent may run formatters and static checks but not
builds or tests. The maintainer will run the harness, rebuild and restart
SwarmUI, refresh the LoRA list, and confirm that evidence-backed files resolve
to `anima/lora`. `modelo_1mb2.safetensors` is expected to retain its warning.
