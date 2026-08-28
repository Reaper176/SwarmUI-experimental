"""Pure helpers for mapping legacy Anima LoRA block keys to Anima 3.8."""

import re


BASE_TO_29_INSERTIONS = (2, 5, 8, 11, 14, 17, 21, 24, 27, 30, 33, 36)
ANIMA_29_TO_38_INSERTIONS = (3, 7, 11, 15, 19, 23, 27, 31, 35, 39, 43, 47)

_BLOCK_PATTERNS = (
    re.compile(r"diffusion_model\.blocks\.(\d+)(?=\.)"),
    re.compile(r"lora_unet_blocks_(\d+)(?=_)")
)
_LLM_ADAPTER_TOKENS = ("diffusion_model.llm_adapter.", "lora_unet_llm_adapter_")


def _apply_insertions(block_index, insertions):
    """Shift a block index past each insertion encountered in the expanded model."""
    mapped_index = block_index
    for insertion_index in insertions:
        if insertion_index <= mapped_index:
            mapped_index += 1
    return mapped_index


def infer_source_block_count(state_dict):
    """Infer whether LoRA keys target the 28-, 40-, or native 52-block model."""
    highest_block_index = None
    for key in state_dict:
        for block_pattern in _BLOCK_PATTERNS:
            for match in block_pattern.finditer(key):
                block_index = int(match.group(1))
                if highest_block_index is None or block_index > highest_block_index:
                    highest_block_index = block_index
    if highest_block_index is None:
        raise ValueError("Cannot infer architecture: no recognizable transformer block key was found")
    if highest_block_index <= 27:
        return 28
    if highest_block_index <= 39:
        return 40
    if highest_block_index <= 51:
        return 52
    raise ValueError(f"Block index {highest_block_index} exceeds the supported native 52-block depth")


def map_block_index_to_anima38(block_index, source_block_count):
    """Map one source block index into the native 52-block Anima 3.8 layout."""
    if source_block_count not in (28, 40, 52):
        raise ValueError(f"Unsupported source block count: {source_block_count}")
    if block_index < 0 or block_index >= source_block_count:
        raise ValueError(f"Block index {block_index} is outside the {source_block_count}-block source layout")
    if source_block_count == 28:
        block_index = _apply_insertions(block_index, BASE_TO_29_INSERTIONS)
        return _apply_insertions(block_index, ANIMA_29_TO_38_INSERTIONS)
    if source_block_count == 40:
        return _apply_insertions(block_index, ANIMA_29_TO_38_INSERTIONS)
    return block_index


def map_lora_state_dict_to_anima38(state_dict):
    """Return LoRA keys mapped to Anima 3.8 while retaining original value objects."""
    source_block_count = infer_source_block_count(state_dict)
    mapped_state_dict = {}
    for key, value in state_dict.items():
        if source_block_count < 52 and any(token in key for token in _LLM_ADAPTER_TOKENS):
            continue

        def replace_block(match):
            mapped_index = map_block_index_to_anima38(int(match.group(1)), source_block_count)
            return match.group(0)[: -len(match.group(1))] + str(mapped_index)

        mapped_key = key
        for block_pattern in _BLOCK_PATTERNS:
            mapped_key = block_pattern.sub(replace_block, mapped_key)
        mapped_state_dict[mapped_key] = value
    return mapped_state_dict
