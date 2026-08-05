"""Compatibility support for Qwen3.5-2B-conditioned Anima checkpoints."""

from __future__ import annotations

import functools
import logging

import torch

import comfy.ldm.anima.model
import comfy.model_detection
import comfy.sd
import comfy.text_encoders.anima
import comfy.text_encoders.hunyuan_video
import comfy.text_encoders.qwen35
import comfy.utils
import folder_paths
from comfy.supported_models_base import ClipTarget


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


class SwarmAnimaQwen35Tokenizer:
    """Tokenize one raw prompt for Qwen3.5-2B and Anima's T5 target IDs."""

    def __init__(self, embedding_directory=None, tokenizer_data={}):
        self.qwen35_2b = comfy.text_encoders.qwen35.Qwen35Tokenizer(
            embedding_directory=embedding_directory,
            tokenizer_data=tokenizer_data,
            embedding_size=2048,
            embedding_key="qwen35_2b",
        )
        self.t5xxl = comfy.text_encoders.anima.T5XXLTokenizer(
            embedding_directory=embedding_directory,
            tokenizer_data=tokenizer_data,
        )

    def tokenize_with_weights(self, text, return_word_ids=False, **kwargs):
        qwen_tokens = self.qwen35_2b.tokenize_with_weights(
            text,
            return_word_ids,
            **kwargs,
        )
        qwen_tokens = [
            [
                (token[0], 1.0, token[2]) if return_word_ids else (token[0], 1.0)
                for token in chunk
            ]
            for chunk in qwen_tokens
        ]
        return {
            "qwen35_2b": qwen_tokens,
            "t5xxl": self.t5xxl.tokenize_with_weights(text, return_word_ids, **kwargs),
        }

    def untokenize(self, token_weight_pair):
        return self.qwen35_2b.untokenize(token_weight_pair)

    def state_dict(self):
        return {}

    def decode(self, token_ids, **kwargs):
        return self.qwen35_2b.decode(token_ids, **kwargs)


class SwarmAnimaQwen35TEModel(comfy.text_encoders.qwen35.Qwen35TEModel):
    """Qwen3.5-2B text encoder that adds Anima LLM-adapter metadata."""

    def __init__(self, device="cpu", dtype=None, model_options={}):
        super().__init__(
            device=device,
            dtype=dtype,
            model_options=model_options,
            model_type="qwen35_2b",
        )

    def encode_token_weights(self, token_weight_pairs):
        output = super().encode_token_weights(token_weight_pairs)
        output[2]["t5xxl_ids"] = torch.tensor(
            [token[0] for token in token_weight_pairs["t5xxl"][0]],
            dtype=torch.int,
        )
        output[2]["t5xxl_weights"] = torch.tensor(
            [token[1] for token in token_weight_pairs["t5xxl"][0]],
        )
        return output


def _anima_qwen35_te(dtype_llama=None, llama_quantization_metadata=None):
    """Create the CLIP model class with detected dtype and quantization metadata."""
    class SwarmAnimaQwen35TEModel_(SwarmAnimaQwen35TEModel):
        def __init__(self, device="cpu", dtype=None, model_options={}):
            if dtype_llama is not None:
                dtype = dtype_llama
            if llama_quantization_metadata is not None:
                model_options = model_options.copy()
                model_options["quantization_metadata"] = llama_quantization_metadata
            super().__init__(device=device, dtype=dtype, model_options=model_options)

    return SwarmAnimaQwen35TEModel_


def _normalize_qwen35_state_dict(state_dict):
    """Normalize Hugging Face Qwen3.5 prefixes to ComfyUI's internal layout."""
    return comfy.utils.state_dict_prefix_replace(
        state_dict,
        {
            "model.language_model.": "model.",
            "model.visual.": "visual.",
            "lm_head.": "model.lm_head.",
        },
    )


def _qwen35_2b_required_text_keys():
    """Build the complete mandatory native Qwen3.5-2B text-model key set."""
    required_keys = [
        "model.embed_tokens.weight",
        "model.norm.weight",
    ]
    common_layer_keys = (
        "input_layernorm.weight",
        "post_attention_layernorm.weight",
        "mlp.gate_proj.weight",
        "mlp.up_proj.weight",
        "mlp.down_proj.weight",
    )
    linear_attention_keys = (
        "linear_attn.in_proj_qkv.weight",
        "linear_attn.in_proj_z.weight",
        "linear_attn.in_proj_b.weight",
        "linear_attn.in_proj_a.weight",
        "linear_attn.out_proj.weight",
        "linear_attn.dt_bias",
        "linear_attn.A_log",
        "linear_attn.conv1d.weight",
        "linear_attn.norm.weight",
    )
    full_attention_keys = (
        "self_attn.q_proj.weight",
        "self_attn.k_proj.weight",
        "self_attn.v_proj.weight",
        "self_attn.o_proj.weight",
        "self_attn.q_norm.weight",
        "self_attn.k_norm.weight",
    )
    for layer_index in range(24):
        layer_prefix = f"model.layers.{layer_index}."
        required_keys.extend(layer_prefix + key for key in common_layer_keys)
        attention_keys = (
            full_attention_keys
            if (layer_index + 1) % 4 == 0
            else linear_attention_keys
        )
        required_keys.extend(layer_prefix + key for key in attention_keys)
    return required_keys


def _validate_qwen35_2b_state_dict(state_dict):
    """Reject weights that are not the native 2048-wide Qwen3.5-2B layout."""
    required_keys = _qwen35_2b_required_text_keys()
    missing_keys = [key for key in required_keys if key not in state_dict]
    if missing_keys:
        missing_sample = ", ".join(missing_keys[:10])
        remaining_count = len(missing_keys) - 10
        remaining_message = f"; plus {remaining_count} more" if remaining_count > 0 else ""
        raise ValueError(
            "Selected text encoder is incomplete or is not the native 24-layer "
            f"Qwen3.5-2B layout. Missing {len(missing_keys)} mandatory text-model "
            f"weights: {missing_sample}{remaining_message}."
        )

    expected_shapes = {
        "model.embed_tokens.weight": (248320, 2048),
        "model.layers.0.input_layernorm.weight": (2048,),
        "model.layers.0.linear_attn.A_log": (16,),
        "model.layers.0.linear_attn.in_proj_qkv.weight": (6144, 2048),
        "model.layers.3.self_attn.k_proj.weight": (512, 2048),
        "model.layers.23.self_attn.q_proj.weight": (4096, 2048),
        "model.norm.weight": (2048,),
    }
    for key, expected_shape in expected_shapes.items():
        actual_shape = tuple(state_dict[key].shape)
        if actual_shape != expected_shape:
            raise ValueError(
                f"Selected text encoder weight '{key}' has shape {actual_shape}; "
                f"the native Qwen3.5-2B architecture requires {expected_shape}."
            )


def load_anima_qwen35_clip(
    clip_path,
    embedding_directory=None,
    model_options={},
    disable_dynamic=False,
):
    """Load one Qwen3.5-2B encoder without ComfyUI's generic CLIP fallback."""
    state_dict, metadata = comfy.utils.load_torch_file(
        clip_path,
        safe_load=True,
        return_metadata=True,
    )
    if model_options.get("custom_operations") is None:
        state_dict, metadata = comfy.utils.convert_old_quants(
            state_dict,
            model_prefix="",
            metadata=metadata,
        )
    state_dict = _normalize_qwen35_state_dict(state_dict)
    _validate_qwen35_2b_state_dict(state_dict)
    detect_options = comfy.text_encoders.hunyuan_video.llama_detect(state_dict)
    target = ClipTarget(
        SwarmAnimaQwen35Tokenizer,
        _anima_qwen35_te(**detect_options),
    )
    clip = comfy.sd.CLIP(
        target,
        embedding_directory=embedding_directory,
        parameters=comfy.utils.calculate_parameters(state_dict),
        state_dict=[state_dict],
        model_options=model_options,
        disable_dynamic=disable_dynamic,
    )
    clip.patcher.cached_patcher_init = (
        load_anima_qwen35_clip_model_patcher,
        (clip_path, embedding_directory, model_options),
    )
    return clip


def load_anima_qwen35_clip_model_patcher(
    clip_path,
    embedding_directory=None,
    model_options={},
    disable_dynamic=False,
):
    """Recreate this loader's patcher for ComfyUI's non-dynamic fallback."""
    clip = load_anima_qwen35_clip(
        clip_path,
        embedding_directory=embedding_directory,
        model_options=model_options,
        disable_dynamic=disable_dynamic,
    )
    return clip.patcher


class SwarmLoadAnimaQwen35Clip:
    """Load raw-prompt Qwen3.5-2B conditioning for projection-enabled Anima."""

    @classmethod
    def INPUT_TYPES(cls):
        return {
            "required": {
                "clip_name": (folder_paths.get_filename_list("text_encoders"),),
            },
            "optional": {
                "device": (["default", "cpu"], {"advanced": True}),
            },
        }

    RETURN_TYPES = ("CLIP",)
    FUNCTION = "load_clip"
    CATEGORY = "SwarmUI/loaders"
    DESCRIPTION = (
        "Loads raw-prompt Qwen3.5-2B conditioning for an Anima checkpoint "
        "containing llm_adapter.source_proj.weight."
    )

    def load_clip(self, clip_name, device="default"):
        clip_path = folder_paths.get_full_path_or_raise("text_encoders", clip_name)
        model_options = {}
        if device == "cpu":
            cpu_device = torch.device("cpu")
            model_options["load_device"] = cpu_device
            model_options["offload_device"] = cpu_device
        clip = load_anima_qwen35_clip(
            clip_path,
            embedding_directory=folder_paths.get_folder_paths("embeddings"),
            model_options=model_options,
        )
        return (clip,)


NODE_CLASS_MAPPINGS = {
    "SwarmLoadAnimaQwen35Clip": SwarmLoadAnimaQwen35Clip,
}

NODE_DISPLAY_NAME_MAPPINGS = {
    "SwarmLoadAnimaQwen35Clip": "Swarm Load Anima Qwen3.5-2B CLIP",
}
