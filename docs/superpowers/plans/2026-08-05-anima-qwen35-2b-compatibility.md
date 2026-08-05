# Anima Qwen3.5-2B Compatibility Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task. SwarmUI repository policy prohibits builds and automated tests, so use only the static verification steps listed here.

**Goal:** Load raw-prompt Qwen3.5-2B conditioning into modified Anima checkpoints that carry a learned 2048-to-1024 `llm_adapter.source_proj` weight.

**Architecture:** Add one Swarm-managed ComfyUI ExtraNodes module. At import time it installs a narrow, idempotent compatibility wrapper that exposes an optional checkpoint projection to Anima before state-dict loading; the same module registers a dedicated CLIP loader using ComfyUI's native Qwen3.5-2B model with Anima's dual Qwen/T5 conditioning metadata.

**Tech Stack:** Python 3.13, PyTorch, ComfyUI model detection/model patching/text encoders, SwarmUI ComfyUI ExtraNodes.

---

## Repository-policy override

`AGENTS.md` explicitly prohibits automated tests and builds. Do not create or run tests and do not launch ComfyUI. Use AST parsing, `git diff --check`, and static call/shape tracing. Reaper176 will perform live verification.

### Task 1: Add checkpoint-driven Anima source projection support

**Files:**

- Create: `src/BuiltinExtensions/ComfyUIBackend/ExtraNodes/SwarmComfyCommon/SwarmAnimaQwen35.py`

- [ ] **Step 1: Add imports, constants, and projection-shape detection**

Create the module with the following imports and helper. The helper validates the optional checkpoint weight and records only a private constructor value; checkpoints without the key return unchanged.

```python
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
```

- [ ] **Step 2: Install idempotent model-detection and construction wrappers**

Add the installer below. It creates `source_proj` before ComfyUI loads checkpoint weights, preserves the checkpoint key name, and projects only adapters that actually have the optional module.

```python
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
```

- [ ] **Step 3: Statically trace model loading**

Confirm by inspection:

- the wrapper calls the original detector first;
- non-Anima and ordinary Anima configurations return unchanged;
- a `[1024, 2048]` weight creates `Linear(2048, 1024)`;
- the registered module name is exactly `llm_adapter.source_proj`, matching the checkpoint key;
- the projection runs before each existing LLM-adapter cross-attention block;
- no downloaded ComfyUI file is modified.

### Task 2: Add raw-prompt Qwen3.5-2B Anima text encoding

**Files:**

- Modify: `src/BuiltinExtensions/ComfyUIBackend/ExtraNodes/SwarmComfyCommon/SwarmAnimaQwen35.py`

- [ ] **Step 1: Add the dual raw-prompt tokenizer**

Append the tokenizer below. It intentionally uses the low-level raw Qwen tokenizer rather than `Qwen35ImageTokenizer`, which inserts a chat template.

```python
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
```

- [ ] **Step 2: Add the Anima conditioning wrapper and factory**

Append a Qwen3.5-2B CLIP wrapper that attaches the T5 metadata consumed by `comfy.model_base.Anima.extra_conds`.

```python
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
```

- [ ] **Step 3: Add strict state-dict detection and loading**

Append helpers that accept official or already-normalized Qwen3.5 key prefixes, reject non-2B weights, preserve quantization metadata, and construct `comfy.sd.CLIP` directly rather than allowing SD1 fallback.

```python
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


def _validate_qwen35_2b_state_dict(state_dict):
    """Reject weights that are not the native 2048-wide Qwen3.5-2B layout."""
    layer_key = "model.layers.0.linear_attn.A_log"
    norm_key = "model.layers.0.input_layernorm.weight"
    if layer_key not in state_dict or norm_key not in state_dict:
        raise ValueError(
            "Selected text encoder is not a supported Qwen3.5 model: required "
            f"weights '{layer_key}' and '{norm_key}' were not found."
        )
    hidden_width = state_dict[norm_key].shape[0]
    if hidden_width != 2048:
        raise ValueError(
            f"Selected Qwen3.5 encoder has hidden width {hidden_width}; "
            "this Anima loader requires Qwen3.5-2B with width 2048."
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
        state_dict=state_dict,
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
```

- [ ] **Step 4: Add the ComfyUI loader node**

Append the node and mappings:

```python
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
```

- [ ] **Step 5: Statically trace conditioning**

Confirm by inspection:

- `Qwen35ImageTokenizer` is never used;
- raw text reaches both tokenizers unchanged;
- the Qwen dictionary key matches `Qwen35TEModel`'s configured name;
- T5 IDs and weights are stored in output metadata;
- invalid weights raise before `comfy.sd.CLIP` construction;
- no path can select `SD1ClipModel`.

### Task 3: Register the module and perform permitted verification

**Files:**

- Modify: `src/BuiltinExtensions/ComfyUIBackend/ExtraNodes/SwarmComfyCommon/__init__.py`
- Verify: `src/BuiltinExtensions/ComfyUIBackend/ExtraNodes/SwarmComfyCommon/SwarmAnimaQwen35.py`

- [ ] **Step 1: Import and register the new module**

Add `SwarmAnimaQwen35` to the relative import list and merge both mappings:

```python
from . import SwarmAnimaLLLite, SwarmAnimaQwen35, SwarmAttentionCouple, SwarmBlending, SwarmImages, SwarmInternalUtil, SwarmKSampler, SwarmLoadImageB64, SwarmLoraLoader, SwarmMasks, SwarmSaveImageWS, SwarmTiling, SwarmExtractLora, SwarmUnsampler, SwarmLatents, SwarmInputNodes, SwarmTextHandling, SwarmReference, SwarmMath, SwarmSam2, SwarmSam3, SwarmAudio, SwarmVideo, SwarmModels
```

Add the mapping immediately after `SwarmAnimaLLLite`:

```python
NODE_CLASS_MAPPINGS = (
    SwarmAnimaLLLite.NODE_CLASS_MAPPINGS
    | SwarmAnimaQwen35.NODE_CLASS_MAPPINGS
    | SwarmAttentionCouple.NODE_CLASS_MAPPINGS
```

Merge the display-name mapping:

```python
NODE_DISPLAY_NAME_MAPPINGS = (
    SwarmAnimaLLLite.NODE_DISPLAY_NAME_MAPPINGS
    | SwarmAnimaQwen35.NODE_DISPLAY_NAME_MAPPINGS
)
```

- [ ] **Step 2: Parse the changed Python without importing ComfyUI**

Run:

```bash
python -c 'import ast, pathlib; paths = [pathlib.Path("src/BuiltinExtensions/ComfyUIBackend/ExtraNodes/SwarmComfyCommon/SwarmAnimaQwen35.py"), pathlib.Path("src/BuiltinExtensions/ComfyUIBackend/ExtraNodes/SwarmComfyCommon/__init__.py")]; [ast.parse(path.read_text()) for path in paths]; print("AST parse passed")'
```

Expected output:

```text
AST parse passed
```

- [ ] **Step 3: Check whitespace and review only scoped changes**

Run:

```bash
git diff --check -- src/BuiltinExtensions/ComfyUIBackend/ExtraNodes/SwarmComfyCommon/SwarmAnimaQwen35.py src/BuiltinExtensions/ComfyUIBackend/ExtraNodes/SwarmComfyCommon/__init__.py
git diff -- src/BuiltinExtensions/ComfyUIBackend/ExtraNodes/SwarmComfyCommon/SwarmAnimaQwen35.py src/BuiltinExtensions/ComfyUIBackend/ExtraNodes/SwarmComfyCommon/__init__.py
```

Expected result: no `diff --check` output; the content diff contains only the new compatibility module and its registration.

- [ ] **Step 4: Perform final static shape and regression review**

Trace these cases manually:

1. Standard Anima without `source_proj`: no private config, no added module, original adapter receives 1024-wide source embeddings.
2. Modified Anima with `[1024, 2048]` projection: private config is passed, `Linear(2048, 1024)` is constructed, checkpoint weight loads under the exact expected key, and the original adapter receives 1024-wide projected embeddings.
3. Non-Anima checkpoint carrying a similarly named key: ignored because `image_model` is not `anima`.
4. Qwen3.5-2B text encoder: native architecture produces 2048-wide raw-prompt embeddings and attaches T5 metadata.
5. Wrong encoder: strict key/width validation raises instead of creating SD1 conditioning.

- [ ] **Step 5: Commit only the implementation files**

Run:

```bash
git add src/BuiltinExtensions/ComfyUIBackend/ExtraNodes/SwarmComfyCommon/SwarmAnimaQwen35.py src/BuiltinExtensions/ComfyUIBackend/ExtraNodes/SwarmComfyCommon/__init__.py
git commit -m "Add Anima Qwen3.5-2B compatibility"
```

Do not stage or modify any pre-existing dirty-worktree files.

## Manual developer verification

After implementation handoff, Reaper176 should restart the ComfyUI backend, replace the generic `CLIPLoader` with `Swarm Load Anima Qwen3.5-2B CLIP`, select `qwen3.5-2B-ntuned.safetensors`, retain the modified Anima checkpoint, and execute the workflow. Successful startup should not report `llm_adapter.source_proj.weight` as unexpected; sampling should receive 2048-wide encoder output projected to the adapter's 1024-wide input.
