"""Anima LoRA loaders that remap to the connected model's actual block depth."""

import logging
import re

from .SwarmAnima38Lora import (
    SwarmAnima38LoraLoader,
    SwarmAnima38LoraLoaderModelOnly,
    SwarmAnima38CreateHookLora,
)
from .SwarmAnima38LoraMapping import (
    BASE_TO_29_INSERTIONS,
    _LLM_ADAPTER_TOKENS,
    _apply_insertions,
    map_block_index_to_anima38,
)

logger = logging.getLogger(__name__)

# Match transformer blocks without confusing llm_adapter.blocks with diffusion blocks.
_BLOCK_PATTERNS = (
    re.compile(r"(?:diffusion_model\.|^net\.|^transformer\.|^)blocks\.(\d+)(?=\.)"),
    re.compile(r"lora_unet_blocks_(\d+)(?=_)"),
)


def get_target_block_count(model):
    """Read the actual diffusion depth without materializing the model's weights."""
    count = len(model.get_model_object("diffusion_model").blocks)
    if count not in (28, 40, 52):
        raise ValueError(f"Unsupported Anima model depth: {count}; expected 28, 40, or 52 blocks")
    return count


def prepare_anima_lora(state_dict, target_block_count, source_block_count=None):
    """Move existing deltas to their corresponding blocks without scaling tensors."""
    if target_block_count not in (28, 40, 52):
        raise ValueError(f"Unsupported Anima target depth: {target_block_count}")
    indices = [int(match.group(1)) for key in state_dict for pattern in _BLOCK_PATTERNS for match in pattern.finditer(key)]
    if not indices:
        # CLIP-only and adapter-only LoRAs have no transformer indices to relocate.
        return state_dict
    highest = max(indices)
    if source_block_count is None:
        if highest not in (27, 39, 51):
            raise ValueError(
                "Cannot infer the source depth of a partial-layer Anima LoRA. "
                "Set its modelspec.anima_source_blocks safetensors metadata to 28, 40, or 52."
            )
        source_block_count = highest + 1
    else:
        source_block_count = int(source_block_count)
    if source_block_count not in (28, 40, 52) or highest >= source_block_count:
        raise ValueError(f"Invalid Anima source depth {source_block_count} for highest block {highest}")
    if source_block_count > target_block_count:
        raise ValueError(
            f"Cannot apply a {source_block_count}-block Anima LoRA to a "
            f"{target_block_count}-block model: down-mapping would lose trained layers"
        )
    if source_block_count == target_block_count:
        return state_dict

    mapped = {}
    for key, value in state_dict.items():
        # Preserve the established 3.8 bridge's treatment of legacy text adapters.
        if target_block_count == 52 and any(token in key for token in _LLM_ADAPTER_TOKENS):
            continue

        def replace_block(match):
            source_index = int(match.group(1))
            target_index = (
                map_block_index_to_anima38(source_index, source_block_count)
                if target_block_count == 52
                else _apply_insertions(source_index, BASE_TO_29_INSERTIONS)
            )
            return match.group(0)[:-len(match.group(1))] + str(target_index)

        mapped_key = key
        for pattern in _BLOCK_PATTERNS:
            mapped_key = pattern.sub(replace_block, mapped_key)
        mapped[mapped_key] = value
    return mapped


class _AnimaLoraCache:
    """Cache raw weights only, so changing host depth cannot reuse the wrong remap."""

    def __init__(self):
        self.loaded_lora = None

    def load_for_model(self, model, lora_name):
        import comfy.utils
        import folder_paths

        target = get_target_block_count(model)
        path = folder_paths.get_full_path_or_raise("loras", lora_name)
        if self.loaded_lora is None or self.loaded_lora[0] != path:
            self.loaded_lora = None
            state_dict, metadata = comfy.utils.load_torch_file(path, safe_load=True, return_metadata=True)
            self.loaded_lora = (path, state_dict, metadata)
        _, state_dict, metadata = self.loaded_lora
        try:
            prepared = prepare_anima_lora(state_dict, target, (metadata or {}).get("modelspec.anima_source_blocks"))
        except ValueError as error:
            raise ValueError(f"Cannot load Anima LoRA '{lora_name}': {error}") from error
        if prepared is not state_dict:
            logger.info("Remapping Anima LoRA '%s' to %d target blocks", lora_name, target)
        return prepared, metadata


class SwarmAnimaLoraLoader(_AnimaLoraCache, SwarmAnima38LoraLoader):
    """Apply a LoRA with unchanged model/CLIP strengths after runtime remapping."""

    DESCRIPTION = "Automatically maps Anima LoRAs to the connected 28-, 40-, or 52-block model."

    def load_lora(self, model, clip, lora_name, strength_model, strength_clip):
        if strength_model == 0 and strength_clip == 0:
            return model, clip
        import comfy.sd

        lora, metadata = self.load_for_model(model, lora_name)
        return comfy.sd.load_lora_for_models(
            model, clip, lora, strength_model, strength_clip, lora_metadata=metadata
        )


class SwarmAnimaLoraLoaderModelOnly(_AnimaLoraCache, SwarmAnima38LoraLoaderModelOnly):
    """Apply a remapped LoRA only to the diffusion model."""

    DESCRIPTION = SwarmAnimaLoraLoader.DESCRIPTION

    def load_lora_model_only(self, model, lora_name, strength_model):
        if strength_model == 0:
            return (model,)
        import comfy.sd

        lora, metadata = self.load_for_model(model, lora_name)
        model_lora, _ = comfy.sd.load_lora_for_models(
            model, None, lora, strength_model, 0, lora_metadata=metadata
        )
        return (model_lora,)


class SwarmAnimaCreateHookLora(_AnimaLoraCache, SwarmAnima38CreateHookLora):
    """Create a scheduled/regional LoRA hook for the connected model's layout."""

    NodeId = "SwarmAnimaCreateHookLora"
    NodeName = "Create Hook LoRA (Anima Auto Remap)"

    @classmethod
    def INPUT_TYPES(cls):
        inputs = super().INPUT_TYPES()
        inputs["required"]["model"] = ("MODEL",)
        return inputs

    def create_hook(self, model, lora_name, strength_model, strength_clip, prev_hooks=None):
        import comfy.hooks

        if strength_model == 0 and strength_clip == 0:
            return (prev_hooks if prev_hooks is not None else comfy.hooks.HookGroup(),)
        lora, _ = self.load_for_model(model, lora_name)
        hooks = comfy.hooks.create_hook_lora(
            lora=lora, strength_model=strength_model, strength_clip=strength_clip
        )
        if prev_hooks is not None:
            hooks = prev_hooks.clone_and_combine(hooks)
        return (hooks,)


NODE_CLASS_MAPPINGS = {
    "SwarmAnimaLoraLoader": SwarmAnimaLoraLoader,
    "SwarmAnimaLoraLoaderModelOnly": SwarmAnimaLoraLoaderModelOnly,
    "SwarmAnimaCreateHookLora": SwarmAnimaCreateHookLora,
}
NODE_DISPLAY_NAME_MAPPINGS = {
    "SwarmAnimaLoraLoader": "Swarm Load LoRA (Anima Auto Remap)",
    "SwarmAnimaLoraLoaderModelOnly": "Swarm Load LoRA Model Only (Anima Auto Remap)",
    "SwarmAnimaCreateHookLora": SwarmAnimaCreateHookLora.NodeName,
}
