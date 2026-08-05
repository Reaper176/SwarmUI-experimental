# Anima Qwen3.5-2B Compatibility Design

## Goal

Allow modified Anima checkpoints with a learned `llm_adapter.source_proj.weight` to consume raw-prompt Qwen3.5-2B conditioning while preserving existing behavior for standard Anima checkpoints and text encoders.

## Scope

The implementation belongs in SwarmUI-managed ComfyUI ExtraNodes. It must not modify downloaded ComfyUI sources, model files, user data, or external extensions.

The feature covers:

- recognizing an optional Anima LLM-adapter source projection from checkpoint state;
- constructing and applying that projection before the standard Anima LLM adapter;
- loading Qwen3.5-2B text-encoder weights through ComfyUI's native implementation;
- using raw prompt tokenization without a Qwen chat or thinking template;
- generating the T5 token IDs and weights expected by Anima's LLM adapter;
- producing clear errors for incompatible checkpoint or encoder dimensions.

Automatic Swarm generation-parameter integration, support for other Qwen3.5 sizes, chat-template prompting, and changes to upstream ComfyUI are outside this scope.

## Architecture

Add one focused Python module under `src/BuiltinExtensions/ComfyUIBackend/ExtraNodes/SwarmComfyCommon` and register its node mappings in that package's `__init__.py`.

The module has two responsibilities with separate internal units:

1. An idempotent compatibility installer wraps ComfyUI's Anima model detection and construction paths. When model detection finds `llm_adapter.source_proj.weight`, it records the weight's input and output dimensions in the detected UNet configuration. The Anima constructor consumes that private configuration, creates `source_proj` before checkpoint weights load, and leaves the ordinary 1024-wide adapter unchanged when the weight is absent. The adapter applies the projection to source embeddings before its existing cross-attention blocks.
2. A dedicated `Swarm Load Anima Qwen3.5-2B CLIP` node loads a text encoder from ComfyUI's `text_encoders` model folder. It uses the native Qwen3.5-2B model implementation but an Anima-specific tokenizer wrapper that tokenizes the raw prompt for Qwen3.5 and separately tokenizes the same prompt for T5. Its encoded conditioning metadata includes `t5xxl_ids` and `t5xxl_weights` so the existing Anima preprocessing path invokes the LLM adapter.

The installer must be safe when imported more than once and must retain references to original ComfyUI callables. It must avoid modifying behavior for non-Anima models and Anima checkpoints without the projection weight.

## Data Flow

For each positive or negative prompt:

1. The raw prompt is tokenized directly with the Qwen3.5 tokenizer. No `<|im_start|>`, assistant role, or `<think>` tokens are added.
2. Qwen3.5-2B produces conditioning with a final width of 2048.
3. The same raw prompt is tokenized by Anima's existing T5 tokenizer; token IDs and weights are attached to the conditioning metadata.
4. Anima preprocessing receives the 2048-wide conditioning and T5 metadata.
5. The checkpoint's learned `source_proj` maps 2048 to the source width expected by the existing LLM-adapter cross-attention layers, normally 1024.
6. The standard Anima LLM adapter produces the 1024-wide context consumed by the diffusion transformer.

## Compatibility and Validation

The model-side patch activates only when all of the following are true:

- ComfyUI identifies the diffusion model as Anima;
- the checkpoint contains `llm_adapter.source_proj.weight`;
- the projection is a two-dimensional linear weight;
- its output width matches the input width of the existing LLM-adapter cross-attention projections.

The loader accepts only weights detected as Qwen3.5-2B. Unsupported layouts or hidden sizes produce an error that reports the detected shape and expected 2048-wide architecture. Missing native Qwen3.5 support produces an actionable ComfyUI-version error instead of silently falling back to SD1 CLIP.

Standard Anima checkpoints continue using the existing identity path and their standard Qwen3-0.6B loader. Non-Anima checkpoints are unaffected.

## Failure Handling

- A malformed or incompatible `source_proj` raises during model loading, before sampling.
- A text encoder that is not Qwen3.5-2B raises during text-encoder loading.
- Missing tokenizer resources surface the native tokenizer-loading error with Anima/Qwen3.5 context.
- The loader must never fall back to `SD1ClipModel`.

## Verification

Repository policy prohibits builds and automated tests. Verification is limited to:

- reviewing the final diff for minimal scope and preservation of unrelated worktree changes;
- Python syntax compilation or an equivalent non-executing parser check only if the repository policy permits it as a linter; otherwise manual syntax review;
- static tracing of checkpoint detection, model construction, state-dict key matching, prompt tokenization, conditioning metadata, and sampling data flow;
- confirming standard Anima and non-Anima branches remain unchanged by inspection.

The developer will perform live ComfyUI verification with the modified checkpoint and Qwen3.5-2B encoder.
