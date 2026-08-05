"""Compatibility support for Qwen3.5-2B-conditioned Anima checkpoints."""

from __future__ import annotations

import functools
import logging

import comfy.ldm.anima.model
import comfy.model_detection


logger = logging.getLogger(__name__)

_SOURCE_PROJECTION_CONFIG_KEY = "swarm_anima_source_projection_shape"
_SOURCE_PROJECTION_WEIGHT_SUFFIX = "llm_adapter.source_proj.weight"
_ADAPTER_KEY_PROJECTION_SUFFIX = "llm_adapter.blocks.0.cross_attn.k_proj.weight"
_PATCH_SENTINEL = "_swarm_anima_qwen35_projection_patch"


def _add_source_projection_config(config, state_dict, key_prefix):
    """Record a compatible optional Anima source projection in model config."""
    if config is None or config.get("image_model") != "anima":
        return config
    projection_key = f"{key_prefix}{_SOURCE_PROJECTION_WEIGHT_SUFFIX}"
    if projection_key not in state_dict:
        return config
    projection_weight = state_dict[projection_key]
    if projection_weight.ndim != 2:
        raise ValueError(
            f"Anima source projection '{projection_key}' must be a 2D linear weight, "
            f"got shape {tuple(projection_weight.shape)}."
        )
    adapter_key = f"{key_prefix}{_ADAPTER_KEY_PROJECTION_SUFFIX}"
    if adapter_key not in state_dict:
        raise ValueError(
            f"Anima source projection is present, but adapter weight '{adapter_key}' is missing."
        )
    adapter_source_width = state_dict[adapter_key].shape[1]
    projection_output_width, projection_input_width = projection_weight.shape
    if projection_output_width != adapter_source_width:
        raise ValueError(
            "Anima source projection output width does not match the LLM adapter: "
            f"projection {projection_input_width}->{projection_output_width}, "
            f"adapter expects {adapter_source_width}."
        )
    updated_config = config.copy()
    updated_config[_SOURCE_PROJECTION_CONFIG_KEY] = (
        projection_input_width,
        projection_output_width,
    )
    return updated_config


def install_anima_source_projection_compatibility():
    """Teach ComfyUI Anima loading about an optional learned source projection."""
    if getattr(comfy.model_detection.detect_unet_config, _PATCH_SENTINEL, False):
        return

    original_detect_unet_config = comfy.model_detection.detect_unet_config
    original_anima_init = comfy.ldm.anima.model.Anima.__init__
    original_adapter_forward = comfy.ldm.anima.model.LLMAdapter.forward

    @functools.wraps(original_detect_unet_config)
    def detect_unet_config(state_dict, key_prefix, metadata=None):
        config = original_detect_unet_config(state_dict, key_prefix, metadata=metadata)
        return _add_source_projection_config(config, state_dict, key_prefix)

    @functools.wraps(original_anima_init)
    def anima_init(self, *args, **kwargs):
        projection_shape = kwargs.pop(_SOURCE_PROJECTION_CONFIG_KEY, None)
        original_anima_init(self, *args, **kwargs)
        if projection_shape is None:
            return
        projection_input_width, projection_output_width = projection_shape
        operations = kwargs.get("operations")
        if operations is None:
            raise RuntimeError("Cannot construct Anima source projection without ComfyUI operations.")
        self.llm_adapter.source_proj = operations.Linear(
            projection_input_width,
            projection_output_width,
            bias=False,
            device=kwargs.get("device"),
            dtype=kwargs.get("dtype"),
        )

    @functools.wraps(original_adapter_forward)
    def adapter_forward(
        self,
        source_hidden_states,
        target_input_ids,
        target_attention_mask=None,
        source_attention_mask=None,
    ):
        source_projection = getattr(self, "source_proj", None)
        if source_projection is not None:
            source_hidden_states = source_projection(source_hidden_states)
        return original_adapter_forward(
            self,
            source_hidden_states,
            target_input_ids,
            target_attention_mask=target_attention_mask,
            source_attention_mask=source_attention_mask,
        )

    setattr(detect_unet_config, _PATCH_SENTINEL, True)
    comfy.model_detection.detect_unet_config = detect_unet_config
    comfy.ldm.anima.model.Anima.__init__ = anima_init
    comfy.ldm.anima.model.LLMAdapter.forward = adapter_forward
    logger.info("[Swarm] Anima optional text-encoder source projection support active")


install_anima_source_projection_compatibility()
