# Anima LoRAs

Swarm's Generate page automatically remaps LoRA transformer blocks for base Anima (28 blocks), Anima 2.9B (40 blocks), and Anima 3.8B (52 blocks). Select LoRAs and set their strengths normally. No Anima-Remap installation is needed for this built-in LoRA path.

The connected model determines the target depth. Base LoRAs map to the original blocks within the larger models; 40-block LoRAs can also map to 52-block models. Native LoRAs retain their block positions. Detected larger-to-smaller mappings stop with an error instead of dropping trained layers.

Model and text-encoder strengths retain their normal meaning, including negative and zero values. Remapping does not multiply strengths, change alpha/rank tensors, or copy deltas into newly inserted layers. Visual influence can still differ between base models; the same strength is not a guarantee of identical appearance.

Ordinary, scheduled, regional, and dynamic prompt LoRAs use the same remapping logic. Dynamic hooks and conditioning caches include the connected model, so a model change cannot reuse a remap for a different host. Old saved workflows using the `SwarmAnima38*` nodes remain supported.

## Source-depth detection

For normal full-depth files, the highest referenced transformer block identifies the source depth: 27 means 28 blocks, 39 means 40, and 51 means 52. Supported key forms include `diffusion_model.blocks.N`, `model.diffusion_model.blocks.N`, `net.blocks.N`, `transformer.blocks.N`, bare `blocks.N`, and `lora_unet_blocks_N_`.

A partial-layer LoRA can be ambiguous. Its author should provide the safetensors metadata field `modelspec.anima_source_blocks` with string value `28`, `40`, or `52`. This overrides inference and is checked against the actual keys. Files whose highest block is not one of the normal boundaries require this field. Partial files ending at 27 or 39 also need it to avoid being mistaken for a smaller architecture.

LoRAs containing only CLIP or text-adapter tensors pass through without block remapping; those tensors still need to be compatible with the selected model. When expanding transformer LoRAs to 52 blocks, Swarm retains its existing 3.8B behavior of excluding legacy `llm_adapter` tensors. Native 52-block LoRAs retain those tensors.

## Activating an update

Build Swarm, then restart both Swarm and its ComfyUI backend. The new nodes are `SwarmAnimaLoraLoader`, `SwarmAnimaLoraLoaderModelOnly`, and `SwarmAnimaCreateHookLora`. Backend capability checks require the corresponding node before routing a request to it. A restart without rebuilding Swarm does not activate the C# workflow changes.

## Standard and semantic 3.8B checkpoints

A 52-block model does not automatically need the optional Qwen3.5 semantic pipeline. Plain expanded checkpoints such as `anima38B_base.safetensors` are classified as **Anima 3.8B (Standard Encoder)** and use the ordinary Qwen3-0.6B encoder. Their automatic sampler defaults follow standard Anima (`er_sde` / `simple`); explicit sampler selections are preserved. Ordinary checkpoints with a learned Qwen3.5 source projection retain projection-aware encoder selection.

Semantic connector tensors or an explicit `anima-3_8b` architecture declaration retain the semantic path and its encoder/adapter requirements. A model-cache revision refreshes older automatic 3.8B classifications from the checkpoint header after rebuilding and restarting Swarm. LoRA remapping supports both classes.
