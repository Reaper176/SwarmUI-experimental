# Additional Anima LoRA Layout Classification Design

## Problem

The existing Anima LoRA classifier recognizes complete Diffusers and standard
Kohya exports built around three block-27 modules. Real files in the local
model library also include sparse attention-only LoRAs, LyCORIS LoHa/LoKr
exports, a remapped block-39 layout, and an unusual `up_blocks_*` export.

The sparse tensor names are not sufficient proof of Anima. Anima inherits the
Cosmos Predict2 transformer structure, and Cosmos Predict2 2B also uses 2,048
channels and 28 blocks. A tensor-only sparse predicate could therefore assign
genuine Cosmos LoRAs to the incompatible Anima class.

Every warned local file has a matching `.cm-info.json` sidecar whose
case-sensitive `BaseModel` property is the string `Anima`. This includes
`modelo_1mb2.safetensors`, whose tensors alone do not identify its architecture.

## Classification Design

Keep the existing complete tensor signatures. Retain a strong remapped-layout
signature requiring the LLM-adapter marker together with representative
block-39 attention and adaptive-normalization modules.

Add an Anima LoRA metadata signature that accepts `BaseModel: Anima` from the
combined model metadata, using a case-insensitive value comparison. Check both
the top-level object and `__metadata__` because sidecars are merged into both
locations. This signature is evaluated only among LoRA classes after the
existing LoRA detector has confirmed the file contains LoRA tensors.

Do not classify from directory names, filenames, a single sparse tensor, or
generic Cosmos-compatible tensor dimensions. Do not globally broaden the LoRA
key helper for flattened LoHa/LoKr suffixes; those module names are shared with
Cosmos and would reintroduce the collision.

## Cache Migration

Advance `ModelClassCacheRevision` from 2 to 3. Preserve the targeted stale
policy: only stale Cosmos classifications and stale null or whitespace LoRA
classifications are re-evaluated.

Refine stale-result selection by provenance:

- a stale non-null classification such as Cosmos continues to trust only the
  raw model-header result, preventing sidecars from overriding the correction;
- a stale unknown LoRA may use the combined raw-header and sidecar result,
  allowing explicit `BaseModel: Anima` evidence to resolve it;
- normal non-stale classification continues to use the combined result.

Persistence still requires a successfully loaded raw model header and a
non-null selected class. Failed reads and still-unknown results remain at their
old revision and are retryable.

## Regression Coverage

Extend the existing Anima classification harness with representative headers
for:

- sparse cross-attention LoRA plus `BaseModel: Anima`;
- sparse self-attention/MLP LoRA plus `BaseModel: Anima`;
- LoHa and LoKr exports plus `BaseModel: Anima`;
- an unusual `up_blocks_*` LoRA plus `BaseModel: Anima`;
- remapped block-39 Diffusers LoRA with its LLM-adapter marker;
- equivalent sparse and unusual layouts without Anima metadata, which must
  remain null;
- a non-Anima `BaseModel` value, which must remain null;
- cache selection that accepts combined metadata only for stale unknowns;
- cache migration from revision 2 to revision 3.

The harness invokes production classification rather than reproducing its
decision logic.

## Scope and Validation

Production changes are limited to the class sorter and model-handler cache
selection/revision. Regression changes are limited to the existing manual
harness. Model and sidecar files are read only for diagnosis.

The user has explicitly authorized necessary builds and tests for this task.
The agent will run the harness through RED and GREEN, run focused formatting
and static checks, and obtain specification and quality reviews. After merge,
SwarmUI will be rebuilt/restarted and its LoRA list refreshed to verify the
reported warnings disappear without misclassifying metadata-free sparse files.
