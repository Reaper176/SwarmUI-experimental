"""Anima 3.8 LoRA loaders with compatibility mapping for legacy Anima LoRAs."""

import logging

from .SwarmAnima38LoraMapping import infer_source_block_count, map_lora_state_dict_to_anima38


logger = logging.getLogger(__name__)


def prepare_anima38_lora(state_dict):
    """Prepare a LoRA for Anima 3.8 and report its source depth and remap decision."""
    try:
        source_block_count = infer_source_block_count(state_dict)
        if source_block_count == 52:
            return state_dict, source_block_count, False
        return map_lora_state_dict_to_anima38(state_dict), source_block_count, True
    except ValueError as error:
        raise ValueError(
            "Expected a 28-, 40-, or 52-block Anima layout with keys containing "
            f"'diffusion_model.blocks.N.' or 'lora_unet_blocks_N_': {error}"
        ) from error


def get_cached_anima38_lora(loaded_lora, resolved_filename):
    """Return prepared cached LoRA data only when its resolved filename matches."""
    if loaded_lora is None or loaded_lora[0] != resolved_filename:
        return None
    return loaded_lora[1:]


class _SwarmAnima38LoraLoaderBase:
    """Shared resolved-path loading and compatibility preparation for Anima 3.8 nodes."""

    def __init__(self):
        self.loaded_lora = None

    def load_anima38_lora(self, lora_name):
        """Resolve, load, cache, and when needed remap an Anima LoRA."""
        import comfy.utils
        import folder_paths

        lora_path = folder_paths.get_full_path_or_raise("loras", lora_name)
        cached_lora = get_cached_anima38_lora(self.loaded_lora, lora_path)
        if cached_lora is not None:
            return cached_lora[0], cached_lora[1]

        self.loaded_lora = None
        state_dict, metadata = comfy.utils.load_torch_file(
            lora_path, safe_load=True, return_metadata=True
        )
        try:
            prepared_state_dict, source_block_count, was_remapped = prepare_anima38_lora(state_dict)
        except ValueError as error:
            raise ValueError(f"Cannot load Anima 3.8 LoRA '{lora_name}' ({lora_path}): {error}") from error

        if was_remapped:
            logger.info(
                "Remapping Anima LoRA '%s' from %d source blocks to 52 target blocks",
                lora_name,
                source_block_count,
            )
        self.loaded_lora = (
            lora_path,
            prepared_state_dict,
            metadata,
            source_block_count,
            was_remapped,
        )
        return prepared_state_dict, metadata


class SwarmAnima38LoraLoader(_SwarmAnima38LoraLoaderBase):
    """Apply a native or legacy-compatible Anima LoRA to a model and CLIP."""

    ESSENTIALS_CATEGORY = "Image Generation"

    @classmethod
    def INPUT_TYPES(cls):
        import folder_paths

        return {
            "required": {
                "model": ("MODEL", {"tooltip": "The diffusion model the LoRA will be applied to."}),
                "clip": ("CLIP", {"tooltip": "The CLIP model the LoRA will be applied to."}),
                "lora_name": (folder_paths.get_filename_list("loras"), {"tooltip": "The name of the LoRA."}),
                "strength_model": ("FLOAT", {"default": 1.0, "min": -100.0, "max": 100.0, "step": 0.01, "tooltip": "How strongly to modify the diffusion model. This value can be negative."}),
                "strength_clip": ("FLOAT", {"default": 1.0, "min": -100.0, "max": 100.0, "step": 0.01, "tooltip": "How strongly to modify the CLIP model. This value can be negative."}),
            }
        }

    RETURN_TYPES = ("MODEL", "CLIP")
    OUTPUT_TOOLTIPS = ("The modified diffusion model.", "The modified CLIP model.")
    FUNCTION = "load_lora"
    CATEGORY = "model/loaders"
    DESCRIPTION = "This LoRA loader maps legacy Anima LoRAs for Anima 3.8 and applies them to both diffusion and CLIP models."
    SEARCH_ALIASES = ["lora", "load lora", "apply lora", "lora loader", "lora model"]

    def load_lora(self, model, clip, lora_name, strength_model, strength_clip):
        """Apply the selected LoRA with independent model and CLIP strengths."""
        if strength_model == 0 and strength_clip == 0:
            return model, clip

        import comfy.sd

        lora, metadata = self.load_anima38_lora(lora_name)
        model_lora, clip_lora = comfy.sd.load_lora_for_models(
            model,
            clip,
            lora,
            strength_model,
            strength_clip,
            lora_metadata=metadata,
        )
        return model_lora, clip_lora


class SwarmAnima38LoraLoaderModelOnly(SwarmAnima38LoraLoader):
    """Apply a native or legacy-compatible Anima LoRA only to a diffusion model."""

    @classmethod
    def INPUT_TYPES(cls):
        import folder_paths

        return {
            "required": {
                "model": ("MODEL",),
                "lora_name": (folder_paths.get_filename_list("loras"),),
                "strength_model": ("FLOAT", {"default": 1.0, "min": -100.0, "max": 100.0, "step": 0.01}),
            }
        }

    RETURN_TYPES = ("MODEL",)
    DESCRIPTION = "This LoRA loader maps legacy Anima LoRAs for Anima 3.8 and applies them only to the diffusion model."
    FUNCTION = "load_lora_model_only"
    CATEGORY = "model/loaders"

    def load_lora_model_only(self, model, lora_name, strength_model):
        """Apply the selected LoRA only to the diffusion model."""
        if strength_model == 0:
            return (model,)

        import comfy.sd

        lora, metadata = self.load_anima38_lora(lora_name)
        model_lora, _ = comfy.sd.load_lora_for_models(
            model,
            None,
            lora,
            strength_model,
            0,
            lora_metadata=metadata,
        )
        return (model_lora,)


class SwarmAnima38CreateHookLora(_SwarmAnima38LoraLoaderBase):
    """Create a scheduled hook from a native or legacy-compatible Anima LoRA."""

    NodeId = "SwarmAnima38CreateHookLora"
    NodeName = "Create Hook LoRA (Anima 3.8)"

    @classmethod
    def INPUT_TYPES(cls):
        import folder_paths

        return {
            "required": {
                "lora_name": (folder_paths.get_filename_list("loras"),),
                "strength_model": ("FLOAT", {"default": 1.0, "min": -20.0, "max": 20.0, "step": 0.01}),
                "strength_clip": ("FLOAT", {"default": 1.0, "min": -20.0, "max": 20.0, "step": 0.01}),
            },
            "optional": {
                "prev_hooks": ("HOOKS",)
            },
        }

    EXPERIMENTAL = True
    RETURN_TYPES = ("HOOKS",)
    CATEGORY = "advanced/hooks/create"
    FUNCTION = "create_hook"

    def create_hook(self, lora_name, strength_model, strength_clip, prev_hooks=None):
        """Create and chain a LoRA hook using the selected strengths."""
        import comfy.hooks

        if prev_hooks is None:
            prev_hooks = comfy.hooks.HookGroup()
        prev_hooks.clone()

        if strength_model == 0 and strength_clip == 0:
            return (prev_hooks,)

        lora, _ = self.load_anima38_lora(lora_name)
        hooks = comfy.hooks.create_hook_lora(
            lora=lora,
            strength_model=strength_model,
            strength_clip=strength_clip,
        )
        return (prev_hooks.clone_and_combine(hooks),)
