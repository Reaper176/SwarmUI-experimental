# Anima 3.8B Classification Correction Design

## Problem

Anima 3.8B checkpoints inherit tensor structure from NVIDIA Cosmos Predict2. The released checkpoint also declares `modelspec.architecture` as `nvidia-cosmos-predict2-t2i-14b`. Swarm currently trusts that embedded ID before examining tensor keys. Even when architecture metadata is rechecked, the existing Cosmos Predict2 14B predicate also matches Anima before the later, more specific Anima registration is considered. Swarm therefore records the checkpoint as Cosmos Predict2, and workflow generation selects the legacy Cosmos T5 encoder instead of the native Anima 3.8B Qwen3.5 conditioning path.

Already indexed checkpoints retain this incorrect class in the model metadata cache even after the classifier is corrected.

## Design

The Anima family and exact 52-block Anima signatures will be exposed as reusable classifier helpers. When a checkpoint declares the specific Cosmos Predict2 14B architecture ID but carries the Anima tensor signature, classification will disregard that embedded ID and continue through tensor-based detection. No other declared architecture ID will be overridden.

The Cosmos Predict2 14B tensor predicate will also explicitly exclude checkpoints matching the Anima family signature. This makes the predicates mutually exclusive and prevents registration order from determining the result. The exact 52-block Anima predicate will continue to classify Anima 3.8B as `anima-3_8b`; older supported Anima variants will continue to classify as `anima`.

Model metadata will carry a classifier revision. The revision will be incremented for this correction. During metadata loading, only cached entries with the affected Cosmos Predict2 14B class and an older revision will be treated as stale and have their checkpoint headers re-read. The refreshed result and current revision will be persisted. Genuine Cosmos Predict2 14B checkpoints will remain Cosmos and will not be repeatedly rescanned on later starts.

Newly indexed metadata and metadata explicitly reset from an in-memory model will always store the current classifier revision.

## Workflow Behavior

No workflow-generation behavior will be changed. Once the model resolves to `anima-3_8b`, the existing native Anima workflow implementation will select `SwarmLoadAnima38Qwen35` and `SwarmAnima38Conditioning` instead of `CLIPLoader` with `old_t5xxl_cosmos.safetensors`.

## Validation

Regression coverage will represent the real tensor-key overlap and verify that:

- An Anima 3.8B header that declares the Cosmos Predict2 14B architecture ID disregards that ID, does not match the Cosmos tensor predicate, and resolves to `anima-3_8b`.
- A genuine Cosmos Predict2 14B header with the same declared architecture ID still resolves to its existing class.
- An affected cached class from an older classifier revision is invalidated once.
- Refreshed metadata records the current revision and is not invalidated repeatedly.
- The generated workflow for an `anima-3_8b` model continues to contain the native Anima Qwen/conditioning nodes and no legacy Cosmos T5 loader.

The focused regression checks will be run first in a failing state, followed by the minimal implementation and the relevant Anima validation suite. Builds and broader checks remain covered by the user's explicit override for this ongoing Anima support task.

## Scope

This correction will not modify model files, user data, genuine Cosmos workflow behavior, or unrelated classifier predicates. It will not introduce a general full-cache invalidation or rescan.
