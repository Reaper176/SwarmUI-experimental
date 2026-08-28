"""Native Qwen3.5-4B progressive conditioning for Anima 3.8B."""

from __future__ import annotations

from contextlib import contextmanager
import functools
import json
import logging
import os
import threading


logger = logging.getLogger(__name__)

THIRD_PARTY_LICENSE_NOTICE = """MIT License

Copyright (c) 2026 GumGum10 contributors

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.
"""
# The progressive semantic adapter below is substantially adapted from the
# project covered by this notice. No tokenizer assets are copied.

ADAPTER_ARCHITECTURE = "anima_progressive_qwen35_cross_adapter_v1"
SEMANTIC_LAYER_TAPS = (8, 16, 24, 32)
QWEN35_4B_MARKERS = (
    "qwen35_4b",
    "qwen3.5-4b",
    "qwen3_5_4b",
    "anima38",
    "anima2bqwen35",
)
ADAPTER_ROOTS = ("text_encoders", "controlnet")
_MODEL_DETECTION_SENTINEL = "_swarm_anima38_52_block_patch"
_MODEL_DETECTION_REGISTRY = "_swarm_model_detection_patch_registry"
_PROGRESSIVE_ADAPTER_CLASS = None
_COMPANION_PROJECTION_KEYS = frozenset(
    {
        "norm.0.weight",
        "norm.0.bias",
        "norm.1.weight",
        "norm.3.weight",
        "norm.3.bias",
    }
)


class LastAdapterCache:
    """Bounded one-entry cache with explicit disposal on replacement."""

    def __init__(self, disposer=None):
        self.key = None
        self.managed_adapter = None
        self.disposer = disposer

    def get(self, key):
        if self.key == key:
            return self.managed_adapter
        return None

    def store(self, key, managed_adapter):
        if self.managed_adapter is not None and self.managed_adapter is not managed_adapter:
            if self.disposer is not None:
                self.disposer(self.managed_adapter)
        self.key = key
        self.managed_adapter = managed_adapter


class AdapterInferenceLease:
    """Serialize cache mutation and the complete inference using its entry."""

    def __init__(self, cache):
        self.cache = cache
        self.lock = threading.RLock()

    @contextmanager
    def locked(self):
        with self.lock:
            yield

    @contextmanager
    def use(self, key, factory):
        with self.lock:
            managed_adapter = self.cache.get(key)
            if managed_adapter is None:
                managed_adapter = factory()
                self.cache.store(key, managed_adapter)
            yield managed_adapter


def _dispose_managed_adapter(managed_adapter):
    """Release a replaced semantic adapter through Comfy's model manager."""
    import comfy.model_management

    comfy.model_management.unload_model_and_clones(
        managed_adapter,
        all_devices=True,
    )


_ADAPTER_CACHE = LastAdapterCache(_dispose_managed_adapter)
_ADAPTER_INFERENCE_LEASE = AdapterInferenceLease(_ADAPTER_CACHE)


def is_qwen35_4b_candidate(filename):
    """Return whether a safetensors filename identifies a Qwen3.5-4B candidate."""
    normalized = str(filename).replace("\\", "/").lower()
    basename = normalized.rsplit("/", 1)[-1]
    return basename.endswith(".safetensors") and any(
        marker in basename for marker in QWEN35_4B_MARKERS
    )


def _stable_name(value):
    """Create a case-insensitive stable lexical sorting key."""
    value = str(value).replace("\\", "/")
    return value.lower(), value


def _qwen_preference(filename):
    """Rank an encoder filename for deterministic automatic selection."""
    normalized = str(filename).replace("\\", "/").lower()
    basename = normalized.rsplit("/", 1)[-1]
    if "anima38" in basename:
        rank = 0
    elif basename == "qwen35_4b.safetensors":
        rank = 1
    else:
        rank = 2
    return rank, *_stable_name(filename)


def select_qwen35_candidate(filenames, selection="auto"):
    """Resolve a filtered Qwen3.5-4B filename, including deterministic auto."""
    candidates = sorted(
        {str(name) for name in filenames if is_qwen35_4b_candidate(name)},
        key=_stable_name,
    )
    if selection == "auto":
        if not candidates:
            markers = ", ".join(QWEN35_4B_MARKERS)
            raise ValueError(
                "No Qwen3.5-4B text encoder candidate was found in text_encoders. "
                f"Expected a .safetensors filename containing one of: {markers}."
            )
        return min(candidates, key=_qwen_preference)
    if selection not in candidates:
        raise ValueError(
            f"Selected Qwen3.5-4B encoder '{selection}' is unavailable or its "
            "filename does not identify the required 32-layer, 2560-wide model."
        )
    return selection


def adapter_tag(root, relative_name):
    """Tag a relative adapter filename with its Comfy model-folder root."""
    if root not in ADAPTER_ROOTS:
        raise ValueError(
            f"Unsupported adapter root '{root}'; expected one of {ADAPTER_ROOTS}."
        )
    return f"{root}::{str(relative_name).replace(chr(92), '/')}"


def _split_adapter_tag(value):
    """Split a tagged adapter selector and validate its source root."""
    root, separator, relative_name = str(value).partition("::")
    if not separator or root not in ADAPTER_ROOTS or not relative_name:
        raise ValueError(
            f"Adapter selector '{value}' must be tagged as "
            "text_encoders::<relative path> or controlnet::<relative path>."
        )
    return root, relative_name


def _adapter_record_valid(metadata, keys):
    """Validate metadata and forbidden key families without importing safetensors."""
    if (metadata or {}).get("architecture") != ADAPTER_ARCHITECTURE:
        return False
    key_names = keys.keys() if hasattr(keys, "keys") else keys
    for key in key_names:
        if str(key).startswith("timestep_gates.") or str(key).startswith(
            "anchor_deviation"
        ):
            return False
    return True


def discover_adapter_candidates(records):
    """Filter ``(root, name, metadata, keys)`` records and return tagged choices."""
    candidates = set()
    for root, name, metadata, keys in records:
        if root not in ADAPTER_ROOTS or not str(name).lower().endswith(".safetensors"):
            continue
        if _adapter_record_valid(metadata, keys):
            candidates.add(adapter_tag(root, name))
    return sorted(candidates, key=_stable_name)


def _adapter_preference(tagged_name):
    """Rank adapter selectors for deterministic automatic selection."""
    _, relative_name = _split_adapter_tag(tagged_name)
    basename = relative_name.rsplit("/", 1)[-1].lower()
    rank = 0 if "anima38" in basename else 1
    return rank, *_stable_name(relative_name), *_stable_name(tagged_name)


def select_adapter_candidate(candidates, selection="auto"):
    """Resolve a tagged progressive adapter selector with actionable errors."""
    candidates = sorted({str(candidate) for candidate in candidates}, key=_stable_name)
    if selection == "auto":
        if not candidates:
            raise ValueError(
                "No compatible Anima 3.8B adapter was found under text_encoders "
                f"or controlnet. Expected safetensors architecture metadata "
                f"'{ADAPTER_ARCHITECTURE}'."
            )
        return min(candidates, key=_adapter_preference)
    if "::" in selection:
        _split_adapter_tag(selection)
        if selection not in candidates:
            raise ValueError(
                f"Selected Anima 3.8B adapter '{selection}' is unavailable or "
                f"does not declare architecture '{ADAPTER_ARCHITECTURE}'."
            )
        return selection
    matches = [candidate for candidate in candidates if candidate.partition("::")[2] == selection]
    if len(matches) > 1:
        raise ValueError(
            f"Adapter name '{selection}' is ambiguous across model roots; use a "
            "tagged text_encoders:: or controlnet:: selector."
        )
    if len(matches) == 1:
        return matches[0]
    raise ValueError(
        f"Selected Anima 3.8B adapter '{selection}' was not found in text_encoders "
        "or controlnet."
    )


def normalize_qwen35_state_dict(state_dict):
    """Normalize companion Qwen keys and remove only its root output projection."""
    normalized = {}
    ignored = []
    for key, value in state_dict.items():
        if key in _COMPANION_PROJECTION_KEYS:
            ignored.append(key)
            continue
        if key.startswith("embed_tokens."):
            key = f"model.{key}"
        elif key.startswith("layers."):
            key = f"model.{key}"
        elif key.startswith("model.language_model."):
            key = f"model.{key[len('model.language_model.'):]}"
        normalized[key] = value
    return normalized, tuple(sorted(ignored))


def format_unified_prompt(prompt):
    """Preserve raw tag/Description prompt text exactly for both encoders."""
    return prompt


def truncate_semantic_token_pairs(token_pairs, limit=1024):
    """Return one raw token sequence capped like the installed companion."""
    flattened = []
    for chunk in token_pairs:
        remaining = limit - len(flattened)
        if remaining <= 0:
            break
        flattened.extend(chunk[:remaining])
    return [flattened]


def _mixed_semantic_source(hidden_states, layer_mix_logits, block_index):
    """Apply one stage's softmax routing to the four semantic sources."""
    mix = layer_mix_logits[block_index].float().softmax(dim=-1)
    mix = mix.to(dtype=hidden_states[0].dtype)
    return sum(
        state * mix[layer_index]
        for layer_index, state in enumerate(hidden_states)
    )


def _expanded_conditioning_metadata(native_metadata, selected, strength, adapter_metadata):
    """Copy safe native metadata and annotate expanded conditioning."""
    output = {
        key: value
        for key, value in native_metadata.items()
        if key not in {"t5xxl_ids", "t5xxl_weights", "attention_mask"}
    }
    output.update(
        {
            "qwen35_expanded_adapter": selected,
            "qwen35_expanded_strength": float(strength),
            "qwen35_expanded_architecture": ADAPTER_ARCHITECTURE,
            "qwen35_expanded_step": adapter_metadata.get("step", ""),
        }
    )
    return output


def _expected_progressive_adapter_shapes():
    """Return the exact trainable tensor layout for the six-stage adapter."""
    expected = {"layer_mix_logits": (6, 4)}
    attention_shapes = {
        "q_proj.weight": (1024, 1024),
        "q_norm.weight": (64,),
        "k_proj.weight": (1024, 2560),
        "k_norm.weight": (64,),
        "v_proj.weight": (1024, 2560),
        "o_proj.weight": (1024, 1024),
    }
    for index in range(6):
        expected[f"query_norms.{index}.weight"] = (1024,)
        expected[f"source_norms.{index}.weight"] = (2560,)
        for suffix, shape in attention_shapes.items():
            expected[f"semantic_attentions.{index}.{suffix}"] = shape
    return expected


def validate_progressive_adapter_shapes(state_dict, selected_name):
    """Reject incomplete, extra, or incorrectly shaped progressive adapter state."""
    expected = _expected_progressive_adapter_shapes()
    for key in sorted(set(expected) & set(state_dict)):
        expected_shape = expected[key]
        actual_shape = tuple(state_dict[key].shape)
        if actual_shape != expected_shape:
            raise ValueError(
                f"Anima 3.8B adapter '{selected_name}' tensor '{key}' has shape "
                f"{actual_shape}; expected {expected_shape}."
            )
    missing = sorted(set(expected) - set(state_dict))
    unexpected = sorted(set(state_dict) - set(expected))
    if missing or unexpected:
        raise ValueError(
            f"Anima 3.8B adapter '{selected_name}' has incompatible state keys: "
            f"missing={missing}, unexpected={unexpected}. Expected the complete "
            "six-stage progressive Qwen3.5 adapter state."
        )


def _anima38_signature(state_dict, key_prefix):
    """Return whether state keys contain exactly the contiguous 52-block signature."""
    block_prefix = f"{key_prefix}blocks."
    indices = set()
    for key in state_dict:
        if not key.startswith(block_prefix):
            continue
        index = key[len(block_prefix) :].split(".", 1)[0]
        if index.isdigit():
            indices.add(int(index))
    return indices == set(range(52))


def install_anima38_model_detection():
    """Narrowly and idempotently correct Comfy's Anima block count to 52."""
    import comfy.model_detection

    registry = getattr(comfy.model_detection, _MODEL_DETECTION_REGISTRY, set())
    if _MODEL_DETECTION_SENTINEL in registry:
        return
    current = comfy.model_detection.detect_unet_config

    @functools.wraps(current)
    def detect_unet_config(state_dict, key_prefix, metadata=None):
        config = current(state_dict, key_prefix, metadata=metadata)
        if (
            config is not None
            and config.get("image_model") == "anima"
            and _anima38_signature(state_dict, key_prefix)
            and config.get("num_blocks") != 52
        ):
            config = config.copy()
            config["num_blocks"] = 52
            logger.info("[Swarm] Detected native 52-block Anima 3.8B model")
        return config

    setattr(detect_unet_config, _MODEL_DETECTION_SENTINEL, True)
    comfy.model_detection.detect_unet_config = detect_unet_config
    updated_registry = set(registry)
    updated_registry.add(_MODEL_DETECTION_SENTINEL)
    setattr(comfy.model_detection, _MODEL_DETECTION_REGISTRY, updated_registry)


def _qwen35_4b_expected_shapes(include_final_norm=True, companion_format=False):
    """Build mandatory native Qwen3.5-4B text-backbone shapes."""
    expected = {"model.embed_tokens.weight": (248320, 2560)}
    if include_final_norm:
        expected["model.norm.weight"] = (2560,)
    common = {
        "input_layernorm.weight": (2560,),
        "post_attention_layernorm.weight": (2560,),
        "mlp.gate_proj.weight": (9216, 2560),
        "mlp.up_proj.weight": (9216, 2560),
        "mlp.down_proj.weight": (2560, 9216),
    }
    linear_attention = {
        "linear_attn.in_proj_qkv.weight": (8192, 2560),
        "linear_attn.in_proj_z.weight": (4096, 2560),
        "linear_attn.in_proj_b.weight": (32, 2560),
        "linear_attn.in_proj_a.weight": (32, 2560),
        "linear_attn.out_proj.weight": (2560, 4096),
        "linear_attn.dt_bias": (32,),
        "linear_attn.A_log": (32,),
        "linear_attn.conv1d.weight": (8192, 1, 4),
        "linear_attn.norm.weight": (128,),
    }
    full_attention = {
        "self_attn.q_proj.weight": (8192, 2560),
        "self_attn.k_proj.weight": (1024, 2560),
        "self_attn.v_proj.weight": (1024, 2560),
        "self_attn.o_proj.weight": (2560, 4096),
        "self_attn.q_norm.weight": (256,),
        "self_attn.k_norm.weight": (256,),
    }
    for index in range(32):
        prefix = f"model.layers.{index}."
        layer_common = common
        if companion_format and index == 31:
            layer_common = {
                "input_layernorm.weight": common["input_layernorm.weight"],
            }
        for suffix, shape in layer_common.items():
            expected[f"{prefix}{suffix}"] = shape
        layer_attention = full_attention if (index + 1) % 4 == 0 else linear_attention
        for suffix, shape in layer_attention.items():
            expected[f"{prefix}{suffix}"] = shape
    return expected


def _round_up(value, multiple):
    return ((value + multiple - 1) // multiple) * multiple


def _quant_config(state_dict, quant_key, weight_key):
    import torch

    metadata = state_dict[quant_key]
    if not isinstance(metadata, torch.Tensor) or metadata.ndim != 1 or metadata.dtype != torch.uint8:
        raise ValueError(
            f"Qwen3.5-4B quantization metadata '{quant_key}' must be a 1D byte tensor."
        )
    try:
        config = json.loads(bytes(metadata.tolist()))
    except (TypeError, ValueError, UnicodeDecodeError) as error:
        raise ValueError(
            f"Qwen3.5-4B quantization metadata '{quant_key}' is not valid JSON."
        ) from error
    if not isinstance(config, dict):
        raise ValueError(f"Qwen3.5-4B quantization metadata '{quant_key}' must contain a JSON object.")
    quant_format = config.get("format")
    supported = ("float8_e4m3fn", "float8_e5m2", "int8_tensorwise", "mxfp8", "nvfp4")
    if quant_format not in supported:
        raise ValueError(
            f"Qwen3.5-4B weight '{weight_key}' uses unsupported Comfy quantization format '{quant_format}'."
        )
    return config, quant_format


def _validate_quant_shape(state_dict, key, shape, weight_key, quant_format):
    import torch

    tensor = state_dict[key]
    if not isinstance(tensor, torch.Tensor) or tuple(tensor.shape) != shape:
        actual = tuple(tensor.shape) if isinstance(tensor, torch.Tensor) else "non-tensor"
        raise ValueError(
            f"Qwen3.5-4B weight '{weight_key}' uses {quant_format}, but tensor "
            f"'{key}' has shape {actual}; required shape is {shape}."
        )


def _validate_quant_scalar(state_dict, key, weight_key, quant_format):
    import torch

    tensor = state_dict[key]
    if not isinstance(tensor, torch.Tensor) or tensor.numel() != 1:
        actual = tuple(tensor.shape) if isinstance(tensor, torch.Tensor) else "non-tensor"
        raise ValueError(
            f"Qwen3.5-4B weight '{weight_key}' uses {quant_format}, but tensor "
            f"'{key}' must contain exactly one element; got {actual}."
        )


def _validate_qwen35_quantized_weight(state_dict, weight_key, logical_shape):
    """Validate current Comfy FP8/INT8/MXFP8/NVFP4 serialization."""
    import torch

    prefix = weight_key.removesuffix("weight")
    quant_key = f"{prefix}comfy_quant"
    if quant_key not in state_dict:
        return False
    config, quant_format = _quant_config(state_dict, quant_key, weight_key)
    if not weight_key.endswith(".weight") or len(logical_shape) != 2:
        raise ValueError(
            f"Qwen3.5-4B quantization marker '{quant_key}' targets non-2D weight "
            f"'{weight_key}' with logical shape {logical_shape}."
        )
    if weight_key == "model.embed_tokens.weight" and quant_format not in (
        "float8_e4m3fn",
        "float8_e5m2",
    ):
        raise ValueError(
            f"Qwen3.5-4B embedding '{weight_key}' supports only Comfy FP8, not '{quant_format}'."
        )
    required = {
        "float8_e4m3fn": () if weight_key == "model.embed_tokens.weight" else ("weight_scale",),
        "float8_e5m2": () if weight_key == "model.embed_tokens.weight" else ("weight_scale",),
        "int8_tensorwise": ("weight_scale",),
        "mxfp8": ("weight_scale",),
        "nvfp4": ("weight_scale", "weight_scale_2"),
    }[quant_format]
    missing = [f"{prefix}{name}" for name in required if f"{prefix}{name}" not in state_dict]
    if missing:
        raise ValueError(
            f"Qwen3.5-4B weight '{weight_key}' uses {quant_format} but is missing "
            f"required quantization tensors: {', '.join(missing)}."
        )
    rows, columns = logical_shape
    if quant_format in ("float8_e4m3fn", "float8_e5m2", "int8_tensorwise"):
        storage_shape = logical_shape
    elif quant_format == "mxfp8":
        storage_shape = (_round_up(rows, 32), _round_up(columns, 32))
    else:
        storage_shape = (_round_up(rows, 16), _round_up(columns, 16) // 2)
    actual_storage = tuple(state_dict[weight_key].shape)
    if actual_storage != storage_shape:
        raise ValueError(
            f"Qwen3.5-4B quantized weight '{weight_key}' uses {quant_format} storage "
            f"shape {actual_storage}; logical shape {logical_shape} requires storage shape {storage_shape}."
        )
    scale_key = f"{prefix}weight_scale"
    if quant_format in ("float8_e4m3fn", "float8_e5m2"):
        if scale_key in state_dict:
            _validate_quant_scalar(state_dict, scale_key, weight_key, quant_format)
    elif quant_format == "int8_tensorwise":
        scale = state_dict[scale_key]
        if not isinstance(scale, torch.Tensor) or not (
            tuple(scale.shape) == (rows, 1) or scale.numel() == 1
        ):
            raise ValueError(
                f"Qwen3.5-4B INT8 weight '{weight_key}' scale '{scale_key}' must be scalar or {(rows, 1)}."
            )
        params = config.get("params", {}) if isinstance(config.get("params", {}), dict) else {}
        convrot = config.get("convrot", params.get("convrot", False))
        if convrot:
            raw_group = config.get("convrot_groupsize", params.get("convrot_groupsize", 256))
            try:
                group = int(raw_group)
            except (TypeError, ValueError) as error:
                raise ValueError(f"Qwen3.5-4B INT8 metadata has invalid convrot_groupsize {raw_group!r}.") from error
            power_of_four = group >= 4 and (group & (group - 1)) == 0 and (group.bit_length() - 1) % 2 == 0
            if not power_of_four or columns % group != 0 or tuple(scale.shape) != (rows, 1):
                raise ValueError(
                    f"Qwen3.5-4B INT8 ConvRot group {group} or scale shape {tuple(scale.shape)} is invalid for {logical_shape}."
                )
    elif quant_format == "mxfp8":
        _validate_quant_shape(
            state_dict,
            scale_key,
            (_round_up(rows, 128), _round_up(_round_up(columns, 32) // 32, 4)),
            weight_key,
            quant_format,
        )
    else:
        _validate_quant_shape(
            state_dict,
            scale_key,
            (_round_up(rows, 128), _round_up(_round_up(columns, 16) // 16, 4)),
            weight_key,
            quant_format,
        )
        _validate_quant_scalar(state_dict, f"{prefix}weight_scale_2", weight_key, quant_format)
    input_scale = f"{prefix}input_scale"
    if quant_format != "int8_tensorwise" and input_scale in state_dict:
        _validate_quant_scalar(state_dict, input_scale, weight_key, quant_format)
    return True


def _quant_auxiliary_keys(state_dict, weight_key):
    prefix = weight_key.removesuffix("weight")
    quant_key = f"{prefix}comfy_quant"
    if quant_key not in state_dict:
        return set()
    _, quant_format = _quant_config(state_dict, quant_key, weight_key)
    parameters = {
        "float8_e4m3fn": {"weight_scale", "input_scale"},
        "float8_e5m2": {"weight_scale", "input_scale"},
        "int8_tensorwise": {"weight_scale"},
        "mxfp8": {"weight_scale", "input_scale"},
        "nvfp4": {"weight_scale", "weight_scale_2", "input_scale"},
    }[quant_format]
    known_parameters = {"weight_scale", "weight_scale_2", "input_scale"}
    present = {
        name for name in known_parameters if f"{prefix}{name}" in state_dict
    }
    unsupported = sorted(present - parameters)
    if unsupported:
        raise ValueError(
            f"Qwen3.5-4B weight '{weight_key}' uses {quant_format} but contains "
            "unsupported quantization auxiliary tensors: "
            f"{', '.join(f'{prefix}{name}' for name in unsupported)}."
        )
    return {quant_key, *(f"{prefix}{name}" for name in present)}


def _validate_qwen35_4b_state_dict(state_dict, selected_name, companion_format):
    """Validate the complete 32-layer, 2560-wide native text backbone."""
    expected = _qwen35_4b_expected_shapes(
        include_final_norm=not companion_format,
        companion_format=companion_format,
    )
    missing = sorted(set(expected) - set(state_dict))
    if missing:
        sample = ", ".join(missing[:8])
        remainder = len(missing) - 8
        suffix = f"; plus {remainder} more" if remainder > 0 else ""
        raise ValueError(
            f"Qwen3.5-4B encoder '{selected_name}' is incomplete or has the wrong "
            f"architecture. Missing {len(missing)} mandatory weights: {sample}{suffix}. "
            "Expected 32 layers, hidden width 2560, and intermediate width 9216."
        )
    for key, shape in expected.items():
        if _validate_qwen35_quantized_weight(state_dict, key, shape):
            continue
        actual = tuple(state_dict[key].shape)
        if actual != shape:
            raise ValueError(
                f"Qwen3.5-4B encoder '{selected_name}' tensor '{key}' has shape "
                f"{actual}; expected {shape}."
            )
    allowed = set(expected)
    for key in expected:
        allowed.update(_quant_auxiliary_keys(state_dict, key))
    unexpected = sorted(key for key in state_dict if key not in allowed)
    if unexpected:
        raise ValueError(
            f"Qwen3.5-4B encoder '{selected_name}' contains unexpected text-model "
            f"weights after normalization: {unexpected[:12]}. Only companion root "
            "projection keys norm.* may be ignored."
        )


def _qwen_candidates():
    """Read filtered Qwen choices from Comfy's text-encoder roots."""
    import folder_paths

    return sorted(
        {
            name
            for name in folder_paths.get_filename_list("text_encoders")
            if is_qwen35_4b_candidate(name)
        },
        key=_stable_name,
    )


def _runtime_adapter_records(require_complete=True):
    """Inspect compatible safetensors headers across both approved roots."""
    import folder_paths
    from safetensors import SafetensorError, safe_open

    records = []
    for root in ADAPTER_ROOTS:
        for name in folder_paths.get_filename_list(root):
            if not str(name).lower().endswith(".safetensors"):
                continue
            path = folder_paths.get_full_path(root, name)
            if path is None or not os.path.isfile(path):
                continue
            try:
                with safe_open(path, framework="pt", device="cpu") as checkpoint:
                    metadata = checkpoint.metadata() or {}
                    shapes = {
                        key: _ShapeOnly(checkpoint.get_slice(key).get_shape())
                        for key in checkpoint.keys()
                    }
                if not _adapter_record_valid(metadata, shapes):
                    continue
                if require_complete:
                    validate_progressive_adapter_shapes(shapes, adapter_tag(root, name))
            except (OSError, ValueError, SafetensorError):
                continue
            records.append((root, name, metadata, shapes))
    return records


class _ShapeOnly:
    """Small shape carrier used while inspecting safetensors headers."""

    def __init__(self, shape):
        self.shape = tuple(shape)


def _adapter_choices():
    """Return dropdown choices with automatic selection first."""
    return ["auto", *discover_adapter_candidates(_runtime_adapter_records())]


def _load_adapter_header(root, relative_name):
    """Read one explicitly named adapter's header without loading its weights."""
    import folder_paths
    from safetensors import safe_open

    path = folder_paths.get_full_path_or_raise(root, relative_name)
    with safe_open(path, framework="pt", device="cpu") as checkpoint:
        metadata = checkpoint.metadata() or {}
        shapes = {
            key: _ShapeOnly(checkpoint.get_slice(key).get_shape())
            for key in checkpoint.keys()
        }
    return path, metadata, shapes


def _validate_adapter_header(metadata, shapes, selected):
    """Provide precise diagnostics for an explicitly selected adapter header."""
    architecture = (metadata or {}).get("architecture")
    if architecture != ADAPTER_ARCHITECTURE:
        actual = architecture or "no architecture metadata"
        raise ValueError(
            f"Anima 3.8B adapter '{selected}' uses '{actual}'; expected architecture "
            f"'{ADAPTER_ARCHITECTURE}'."
        )
    for key in shapes:
        if str(key).startswith("timestep_gates."):
            raise ValueError(
                f"Anima 3.8B adapter '{selected}' contains forbidden tensor '{key}'; "
                "the native progressive format must not contain timestep gates."
            )
        if str(key).startswith("anchor_deviation"):
            raise ValueError(
                f"Anima 3.8B adapter '{selected}' contains forbidden tensor '{key}'; "
                "the native progressive format must not contain anchor deviation."
            )
    validate_progressive_adapter_shapes(shapes, selected)


def _resolve_adapter_path(selection, header_loader=None):
    """Resolve and precisely revalidate a selected adapter path and metadata."""
    loader = _load_adapter_header if header_loader is None else header_loader
    if selection != "auto" and "::" in selection:
        selected = selection
        root, relative_name = _split_adapter_tag(selected)
    else:
        candidates = discover_adapter_candidates(_runtime_adapter_records())
        selected = select_adapter_candidate(candidates, selection)
        root, relative_name = _split_adapter_tag(selected)
    path, metadata, shapes = loader(root, relative_name)
    _validate_adapter_header(metadata, shapes, selected)
    return selected, path, metadata


def _qwen_runtime_classes(companion_format=False):
    """Create CLIP wrapper classes around Comfy's native raw Qwen3.5 tokenizer."""
    import torch
    import comfy.sd1_clip
    import comfy.text_encoders.qwen35 as comfy_qwen35

    class SwarmAnima38Qwen35Tokenizer:
        """Expose raw Qwen3.5-4B tokens without image/chat templates."""

        def __init__(self, embedding_directory=None, tokenizer_data={}):
            self.qwen35_4b = comfy_qwen35.Qwen35Tokenizer(
                embedding_directory=embedding_directory,
                tokenizer_data=tokenizer_data,
                embedding_size=2560,
                embedding_key="qwen35_4b",
            )

        def tokenize_with_weights(self, text, return_word_ids=False, **kwargs):
            token_pairs = self.qwen35_4b.tokenize_with_weights(
                text, return_word_ids, **kwargs
            )
            return {
                "qwen35_4b": truncate_semantic_token_pairs(token_pairs)
            }

        def untokenize(self, token_weight_pair):
            return self.qwen35_4b.untokenize(token_weight_pair)

        def state_dict(self):
            return {}

        def decode(self, token_ids, **kwargs):
            return self.qwen35_4b.decode(token_ids, **kwargs)

    class SwarmAnima38Qwen35ClipModel(comfy_qwen35.Qwen35ClipModel):
        """Native Qwen3.5-4B model with direct intermediate-state access."""

        def __init__(
            self,
            device="cpu",
            layer="hidden",
            layer_idx=-2,
            dtype=None,
            attention_mask=True,
            model_options={},
        ):
            class Qwen35NoFinalNorm(comfy_qwen35.Qwen35):
                """Retain the checkpoint norm weight but bypass it at inference."""

                model_type = "qwen35_4b"

                def __init__(self, config_dict, dtype, device, operations):
                    super().__init__(config_dict, dtype, device, operations)

                    if companion_format:
                        original_final = self.model.layers[-1]

                        class AttentionOnlyFinalBlock(torch.nn.Module):
                            """Match the companion's intentionally truncated layer 31."""

                            def __init__(self, original):
                                super().__init__()
                                self.layer_type = original.layer_type
                                self.self_attn = original.self_attn
                                self.input_layernorm = original.input_layernorm

                            def forward(
                                self,
                                x,
                                attention_mask=None,
                                freqs_cis=None,
                                optimized_attention=None,
                                past_key_value=None,
                            ):
                                hidden, present = self.self_attn(
                                    self.input_layernorm(x),
                                    attention_mask=attention_mask,
                                    freqs_cis=freqs_cis,
                                    optimized_attention=optimized_attention,
                                    past_key_value=past_key_value,
                                )
                                return x + hidden, present

                        self.model.layers[-1] = AttentionOnlyFinalBlock(original_final)
                        self.model.norm = None
                        return

                    class LoadedIdentityNorm(torch.nn.Module):
                        """Load model.norm.weight while leaving final states raw."""

                        def __init__(self, original_norm):
                            super().__init__()
                            self.weight = original_norm.weight

                        def forward(self, value):
                            return value

                    self.model.norm = LoadedIdentityNorm(self.model.norm)

            comfy.sd1_clip.SDClipModel.__init__(
                self,
                device=device,
                layer=layer,
                layer_idx=layer_idx,
                textmodel_json_config={},
                dtype=dtype,
                special_tokens={"pad": 248044},
                layer_norm_hidden_state=False,
                model_class=Qwen35NoFinalNorm,
                enable_attention_masks=attention_mask,
                return_attention_masks=attention_mask,
                model_options=model_options,
            )

        def raw_hidden_states(self, token_pairs, execution_device):
            if len(token_pairs) != 1:
                raise RuntimeError(
                    "Anima 3.8B semantic conditioning expects one raw prompt."
                )
            token_ids = []
            for item in token_pairs[0][:1024]:
                token = item[0] if isinstance(item, (tuple, list)) else item
                if not isinstance(token, int):
                    raise RuntimeError(
                        "Textual-inversion embeddings are unsupported by the "
                        "Anima 3.8B Qwen3.5 semantic encoder."
                    )
                token_ids.append(token)
            embeds, attention_mask, num_tokens, embeds_info = self.process_tokens(
                [token_ids], execution_device
            )
            outputs = self.transformer(
                None,
                attention_mask,
                embeds=embeds,
                num_tokens=num_tokens,
                intermediate_output=list(SEMANTIC_LAYER_TAPS),
                final_layer_norm_intermediate=False,
                dtype=torch.float32,
                embeds_info=embeds_info,
            )
            intermediate = outputs[1]
            if not isinstance(intermediate, torch.Tensor) or intermediate.ndim != 4:
                raise RuntimeError(
                    "Native Qwen3.5 did not return four requested intermediate layers."
                )
            if intermediate.shape[1] != len(SEMANTIC_LAYER_TAPS):
                raise RuntimeError(
                    "Native Qwen3.5 returned "
                    f"{intermediate.shape[1]} semantic layers; expected 4 at taps "
                    f"{SEMANTIC_LAYER_TAPS}."
                )
            return [state.float() for state in intermediate.unbind(dim=1)], attention_mask

    class SwarmAnima38Qwen35TEModel(comfy.sd1_clip.SD1ClipModel):
        """Single raw Qwen3.5-4B text encoder exposed as a CLIP model."""

        def __init__(self, device="cpu", dtype=None, model_options={}):
            super().__init__(
                device=device,
                dtype=dtype,
                name="qwen35_4b",
                clip_model=SwarmAnima38Qwen35ClipModel,
                model_options=model_options,
            )

    return SwarmAnima38Qwen35Tokenizer, SwarmAnima38Qwen35TEModel


def _configured_qwen_te(base_class, dtype_llama=None, llama_quantization_metadata=None):
    """Bind detected dtype and Comfy quantization metadata to the TE wrapper."""
    class ConfiguredQwenTE(base_class):
        def __init__(self, device="cpu", dtype=None, model_options={}):
            if dtype_llama is not None:
                dtype = dtype_llama
            options = model_options.copy()
            if llama_quantization_metadata is not None:
                options["quantization_metadata"] = llama_quantization_metadata
            super().__init__(device=device, dtype=dtype, model_options=options)

    return ConfiguredQwenTE


def _install_clip_reload_factory(
    clip,
    path,
    selected_name,
    embedding_directory,
    model_options,
    loader=None,
):
    """Install Comfy's CoreModelPatcher recreation callback on a loaded CLIP."""
    factory = load_anima38_qwen35_clip_model_patcher
    if loader is not None:
        factory = functools.partial(
            load_anima38_qwen35_clip_model_patcher,
            loader=loader,
        )

    clip.patcher.cached_patcher_init = (
        factory,
        (path, selected_name, embedding_directory, model_options),
    )


def load_anima38_qwen35_clip_model_patcher(
    path,
    selected_name,
    embedding_directory=None,
    model_options=None,
    disable_dynamic=False,
    loader=None,
):
    """Recreate this loader's patcher for Comfy's non-dynamic delegate."""
    reload_loader = load_anima38_qwen35_clip if loader is None else loader
    clip = reload_loader(
        path,
        selected_name,
        embedding_directory=embedding_directory,
        model_options=model_options,
        disable_dynamic=disable_dynamic,
    )
    return clip.patcher


def load_anima38_qwen35_clip(
    path,
    selected_name,
    embedding_directory=None,
    model_options=None,
    disable_dynamic=False,
):
    """Load and validate one native raw-prompt Qwen3.5-4B CLIP object."""
    import comfy.sd
    import comfy.text_encoders.hunyuan_video
    import comfy.utils
    import folder_paths
    from comfy.supported_models_base import ClipTarget

    try:
        import comfy.text_encoders.qwen35
    except ModuleNotFoundError as error:
        raise RuntimeError(
            "Anima 3.8B requires current ComfyUI native Qwen3.5 support "
            "(comfy.text_encoders.qwen35)."
        ) from error
    if model_options is None:
        model_options = {}
    if embedding_directory is None:
        embedding_directory = folder_paths.get_folder_paths("embeddings")
    state_dict, metadata = comfy.utils.load_torch_file(
        path, safe_load=True, return_metadata=True
    )
    if (
        model_options.get("custom_operations") is None
        and hasattr(comfy.utils, "convert_old_quants")
    ):
        state_dict, metadata = comfy.utils.convert_old_quants(
            state_dict, model_prefix="", metadata=metadata
        )
    companion_format = any(
        key.startswith("embed_tokens.") or key.startswith("layers.")
        for key in state_dict
    )
    state_dict, ignored = normalize_qwen35_state_dict(state_dict)
    _validate_qwen35_4b_state_dict(state_dict, selected_name, companion_format)
    if ignored:
        logger.info(
            "[Swarm] Ignored companion Qwen output projection keys for %s: %s",
            selected_name,
            ", ".join(ignored),
        )
    detection = comfy.text_encoders.hunyuan_video.llama_detect(state_dict)
    tokenizer_class, te_class = _qwen_runtime_classes(companion_format)
    target = ClipTarget(
        tokenizer_class,
        _configured_qwen_te(te_class, **detection),
    )
    clip = comfy.sd.CLIP(
        target,
        embedding_directory=embedding_directory,
        parameters=comfy.utils.calculate_parameters(state_dict),
        state_dict=[state_dict],
        model_options=model_options,
        disable_dynamic=disable_dynamic,
    )
    _install_clip_reload_factory(
        clip,
        path,
        selected_name,
        embedding_directory,
        model_options,
    )
    return clip


def _progressive_adapter_type():
    """Lazily define the companion-compatible progressive adapter module."""
    global _PROGRESSIVE_ADAPTER_CLASS
    if _PROGRESSIVE_ADAPTER_CLASS is not None:
        return _PROGRESSIVE_ADAPTER_CLASS

    import torch
    from torch import nn
    from comfy.ldm.anima.model import Attention

    class ProgressiveQwen35CrossAdapter(nn.Module):
        """Semantic-only learned injections, managed independently by Comfy."""

        def __init__(
            self,
            model_dim,
            num_heads,
            device,
            dtype,
            operations,
            stage_count=6,
        ):
            super().__init__()
            head_dim = model_dim // num_heads
            self.query_norms = nn.ModuleList(
                [
                    operations.RMSNorm(
                        model_dim, eps=1e-6, device=device, dtype=dtype
                    )
                    for _ in range(stage_count)
                ]
            )
            self.source_norms = nn.ModuleList(
                [
                    operations.RMSNorm(
                        2560, eps=1e-6, device=device, dtype=dtype
                    )
                    for _ in range(stage_count)
                ]
            )
            self.semantic_attentions = nn.ModuleList(
                [
                    Attention(
                        query_dim=model_dim,
                        context_dim=2560,
                        n_heads=num_heads,
                        head_dim=head_dim,
                        device=device,
                        dtype=dtype,
                        operations=operations,
                    )
                    for _ in range(stage_count)
                ]
            )
            self.layer_mix_logits = nn.Parameter(
                torch.empty(
                    stage_count,
                    len(SEMANTIC_LAYER_TAPS),
                    device=device,
                    dtype=dtype,
                )
            )

        def forward(
            self,
            block_index,
            query,
            semantic_hidden_states,
            semantic_source_mask=None,
            query_rope=None,
            semantic_rope=None,
        ):
            if len(semantic_hidden_states) != len(SEMANTIC_LAYER_TAPS):
                raise ValueError(
                    f"Expected 4 Qwen3.5 semantic states, got "
                    f"{len(semantic_hidden_states)}."
                )
            if semantic_source_mask is not None:
                semantic_source_mask = semantic_source_mask.to(torch.bool)
                if semantic_source_mask.ndim == 2:
                    semantic_source_mask = semantic_source_mask.unsqueeze(1).unsqueeze(1)
            semantic_source = _mixed_semantic_source(
                semantic_hidden_states,
                self.layer_mix_logits,
                block_index,
            )
            semantic_source = self.source_norms[block_index](semantic_source)
            return self.semantic_attentions[block_index](
                self.query_norms[block_index](query),
                mask=semantic_source_mask,
                context=semantic_source,
                position_embeddings=query_rope,
                position_embeddings_context=semantic_rope,
            )

    _PROGRESSIVE_ADAPTER_CLASS = ProgressiveQwen35CrossAdapter
    return _PROGRESSIVE_ADAPTER_CLASS


def _native_anima38_adapter(model):
    """Resolve and validate the native six-stage adapter on a 52-block model."""
    base_model = getattr(model, "model", None)
    diffusion_model = getattr(base_model, "diffusion_model", None)
    blocks = getattr(diffusion_model, "blocks", None)
    if blocks is None or len(blocks) != 52:
        count = "missing" if blocks is None else len(blocks)
        raise RuntimeError(
            f"SwarmAnima38Conditioning requires a 52-block Anima 3.8B MODEL; "
            f"got {count} diffusion blocks."
        )
    adapter = getattr(diffusion_model, "llm_adapter", None)
    if adapter is None or len(getattr(adapter, "blocks", ())) != 6:
        count = "missing" if adapter is None else len(getattr(adapter, "blocks", ()))
        raise RuntimeError(
            "The selected Anima 3.8B MODEL must contain its native six-stage LLM "
            f"adapter; got {count} stages."
        )
    return adapter


def _managed_progressive_adapter(source_model, native_adapter, selected, path):
    """Load or reuse one CPU-offloadable, semantic-only adapter patcher."""
    import comfy.model_management
    import comfy.model_patcher
    import comfy.ops
    import comfy.utils

    dtype = native_adapter.embed.weight.dtype
    load_device = source_model.load_device
    offload_device = comfy.model_management.text_encoder_offload_device()
    cache_key = (path, str(load_device), str(offload_device), str(dtype))
    cached = _ADAPTER_CACHE.get(cache_key)
    if cached is not None:
        return cached
    adapter_type = _progressive_adapter_type()
    semantic_adapter = adapter_type(
        model_dim=native_adapter.embed.weight.shape[1],
        num_heads=native_adapter.blocks[0].self_attn.n_heads,
        device=offload_device,
        dtype=dtype,
        operations=comfy.ops.disable_weight_init,
        stage_count=len(native_adapter.blocks),
    )
    state_dict = comfy.utils.load_torch_file(
        path,
        safe_load=True,
        device=offload_device,
    )
    validate_progressive_adapter_shapes(state_dict, selected)
    incompatible = semantic_adapter.load_state_dict(state_dict, strict=True)
    missing = sorted(incompatible.missing_keys)
    unexpected = sorted(incompatible.unexpected_keys)
    if missing or unexpected:
        raise RuntimeError(
            f"Anima 3.8B adapter '{selected}' could not be applied: "
            f"missing={missing}, unexpected={unexpected}."
        )
    semantic_adapter.eval()
    semantic_adapter.requires_grad_(False)
    managed_adapter = comfy.model_patcher.ModelPatcher(
        semantic_adapter,
        load_device=load_device,
        offload_device=offload_device,
        size=comfy.model_management.module_size(semantic_adapter),
    )
    _ADAPTER_CACHE.store(cache_key, managed_adapter)
    return managed_adapter


def _run_progressive_adapter(
    native_adapter,
    semantic_adapter,
    native_source,
    target_input_ids,
    semantic_hidden_states,
    target_attention_mask=None,
    native_source_mask=None,
    semantic_source_mask=None,
):
    """Run native stages and independently managed semantic residuals together."""
    import torch

    def attention_mask(mask):
        if mask is None:
            return None
        mask = mask.to(torch.bool)
        return mask.unsqueeze(1).unsqueeze(1) if mask.ndim == 2 else mask

    target_attention_mask = attention_mask(target_attention_mask)
    native_source_mask = attention_mask(native_source_mask)
    x = native_adapter.in_proj(
        native_adapter.embed(target_input_ids, out_dtype=native_source.dtype)
    )
    query_positions = torch.arange(x.shape[1], device=x.device).unsqueeze(0)
    native_positions = torch.arange(native_source.shape[1], device=x.device).unsqueeze(0)
    semantic_positions = torch.arange(
        semantic_hidden_states[0].shape[1], device=x.device
    ).unsqueeze(0)
    query_rope = native_adapter.rotary_emb(x, query_positions)
    native_rope = native_adapter.rotary_emb(x, native_positions)
    semantic_rope = native_adapter.rotary_emb(x, semantic_positions)
    for index, native_block in enumerate(native_adapter.blocks):
        x = native_block(
            x,
            native_source,
            target_attention_mask=target_attention_mask,
            source_attention_mask=native_source_mask,
            position_embeddings=query_rope,
            position_embeddings_context=native_rope,
        )
        x = x + semantic_adapter(
            index,
            x,
            semantic_hidden_states,
            semantic_source_mask=semantic_source_mask,
            query_rope=query_rope,
            semantic_rope=semantic_rope,
        )
    return native_adapter.norm(native_adapter.out_proj(x))


def _pad_context(context, length=512):
    """Pad or truncate Anima adapter context to its fixed conditioning length."""
    import torch.nn.functional as functional

    if context.shape[1] >= length:
        return context[:, :length]
    return functional.pad(context, (0, 0, 0, length - context.shape[1]))


class SwarmLoadAnima38Qwen35:
    """Load the separate raw-prompt Qwen3.5-4B semantic encoder."""

    @classmethod
    def INPUT_TYPES(cls):
        return {"required": {"qwen_filename": (["auto", *_qwen_candidates()],)}}

    RETURN_TYPES = ("CLIP",)
    FUNCTION = "load_clip"
    CATEGORY = "SwarmUI/loaders"
    DESCRIPTION = "Loads native raw-prompt Qwen3.5-4B semantics for Anima 3.8B."

    def load_clip(self, qwen_filename):
        import folder_paths

        selected = select_qwen35_candidate(_qwen_candidates(), qwen_filename)
        path = folder_paths.get_full_path_or_raise("text_encoders", selected)
        try:
            clip = load_anima38_qwen35_clip(path, selected)
        except Exception as error:
            raise RuntimeError(
                f"Failed to load Anima 3.8B Qwen3.5-4B encoder '{selected}': {error}"
            ) from error
        logger.info("[Swarm] Loaded Anima 3.8B semantic encoder %s", selected)
        return (clip,)


class SwarmAnima38Conditioning:
    """Combine native Anima and Qwen3.5 semantics through the progressive adapter."""

    @classmethod
    def INPUT_TYPES(cls):
        return {
            "required": {
                "source_model": ("MODEL",),
                "clip": ("CLIP",),
                "qwen35_clip": ("CLIP",),
                "adapter_name": (_adapter_choices(),),
                "prompt": (
                    "STRING",
                    {"multiline": True, "dynamicPrompts": True},
                ),
                "adapter_strength": (
                    "FLOAT",
                    {"default": 1.0, "min": 0.0, "max": 2.0, "step": 0.05},
                ),
            }
        }

    RETURN_TYPES = ("CONDITIONING", "CONDITIONING")
    RETURN_NAMES = ("expanded", "native")
    FUNCTION = "encode"
    CATEGORY = "SwarmUI/conditioning"

    @staticmethod
    def _encode_native(clip, prompt):
        conditioning = clip.encode_from_tokens_scheduled(clip.tokenize(prompt))
        if len(conditioning) != 1:
            raise RuntimeError(
                "Anima 3.8B conditioning expects one Swarm-presegmented prompt; "
                "scheduled prompt syntax must be resolved before this node."
            )
        return conditioning

    @staticmethod
    def _encode_semantic_layers(clip, prompt):
        import torch
        import comfy.model_management

        tokens = clip.tokenize(prompt)
        token_pairs = tokens.get("qwen35_4b")
        if token_pairs is None:
            raise RuntimeError(
                "qwen35_clip must come from SwarmLoadAnima38Qwen35 and expose "
                "raw qwen35_4b tokens."
            )
        clip.load_model(tokens)
        execution_device = clip.patcher.load_device
        try:
            inner = clip.cond_stage_model.qwen35_4b
        except AttributeError as error:
            raise RuntimeError(
                "qwen35_clip must come from SwarmLoadAnima38Qwen35."
            ) from error
        inner.set_clip_options({"execution_device": execution_device})
        with comfy.model_management.cuda_device_context(execution_device):
            states, attention_mask = inner.raw_hidden_states(
                token_pairs, execution_device
            )
        intermediate_device = comfy.model_management.intermediate_device()
        states = [
            state.to(device=intermediate_device, dtype=torch.bfloat16)
            for state in states
        ]
        return states, attention_mask.to(intermediate_device)

    def encode(
        self,
        source_model,
        clip,
        qwen35_clip,
        adapter_name,
        prompt,
        adapter_strength,
    ):
        import torch
        import comfy.model_management

        raw_prompt = format_unified_prompt(prompt)
        native = self._encode_native(clip, raw_prompt)
        if float(adapter_strength) == 0.0:
            return native, native

        selected, adapter_path, adapter_metadata = _resolve_adapter_path(adapter_name)
        native_source, native_metadata = native[0]
        if native_source.ndim != 3 or native_source.shape[-1] != 1024:
            raise RuntimeError(
                "The native clip input must be Anima's Qwen3-0.6B encoder with "
                f"1024-wide conditioning; got {tuple(native_source.shape)}."
            )
        target_ids = native_metadata.get("t5xxl_ids")
        if target_ids is None:
            raise RuntimeError(
                "Native Anima conditioning is missing required t5xxl_ids metadata."
            )
        semantic_states, semantic_mask = self._encode_semantic_layers(
            qwen35_clip, raw_prompt
        )
        for tap, state in zip(SEMANTIC_LAYER_TAPS, semantic_states):
            if state.ndim != 3 or state.shape[-1] != 2560:
                raise RuntimeError(
                    f"Qwen3.5 semantic encoder tap {tap} returned shape "
                    f"{tuple(state.shape)}; expected [batch, tokens, 2560]."
                )
        with _ADAPTER_INFERENCE_LEASE.locked():
            native_adapter = _native_anima38_adapter(source_model)
            managed_adapter = _managed_progressive_adapter(
                source_model,
                native_adapter,
                selected,
                adapter_path,
            )
            comfy.model_management.load_models_gpu(
                [source_model, managed_adapter], force_full_load=True
            )
            native_adapter = _native_anima38_adapter(source_model)
            device = native_adapter.embed.weight.device
            dtype = native_adapter.embed.weight.dtype
            source = native_source.to(device=device, dtype=dtype)
            semantic_states = [
                state.to(device=device, dtype=dtype) for state in semantic_states
            ]
            semantic_mask = semantic_mask.to(device=device, dtype=torch.bool)
            target_ids = torch.as_tensor(
                target_ids, device=device, dtype=torch.long
            ).reshape(1, -1)[:, :512]
            semantic_adapter = managed_adapter.model
            with torch.no_grad():
                expanded_context = _run_progressive_adapter(
                    native_adapter,
                    semantic_adapter,
                    source,
                    target_ids,
                    semantic_states,
                    semantic_source_mask=semantic_mask,
                )
                strength = float(adapter_strength)
                if strength != 1.0:
                    native_context = native_adapter(source, target_ids)
                    expanded_context = native_context + strength * (
                        expanded_context - native_context
                    )
                target_weights = native_metadata.get("t5xxl_weights")
                if target_weights is not None:
                    weights = torch.as_tensor(
                        target_weights,
                        device=device,
                        dtype=expanded_context.dtype,
                    ).reshape(1, -1, 1)[:, : expanded_context.shape[1]]
                    expanded_context = expanded_context * weights
                expanded_context = _pad_context(expanded_context)
                expanded_context = expanded_context.to(
                    comfy.model_management.intermediate_device()
                )
        output_metadata = _expanded_conditioning_metadata(
            native_metadata,
            selected,
            adapter_strength,
            adapter_metadata,
        )
        logger.info(
            "[Swarm] Encoded Anima 3.8B prompt with %s at strength %.2f",
            selected,
            adapter_strength,
        )
        return [[expanded_context, output_metadata]], native


NODE_CLASS_MAPPINGS = {
    "SwarmLoadAnima38Qwen35": SwarmLoadAnima38Qwen35,
    "SwarmAnima38Conditioning": SwarmAnima38Conditioning,
}

NODE_DISPLAY_NAME_MAPPINGS = {
    "SwarmLoadAnima38Qwen35": "Swarm Load Anima 3.8B Qwen3.5-4B",
    "SwarmAnima38Conditioning": "Swarm Anima 3.8B Conditioning",
}


try:
    import comfy.model_detection  # noqa: F401
except ModuleNotFoundError as error:
    if error.name != "comfy":
        raise
else:
    install_anima38_model_detection()
