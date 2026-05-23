# Attention Couple implementation adapted with permission from ComfyUI-ppm.
# Original implementation by laksjdjf, hako-mikan, Haoming02.
# Swarm integration keeps this code as a Swarm-owned Comfy node.

import itertools
import math
from typing import Any

import torch
import torch.nn.functional as F
import comfy.model_management
from comfy.model_base import SDXL, Anima, BaseModel, SDXLRefiner
from comfy.model_patcher import ModelPatcher

COND = 0
UNCOND = 1
COND_UNCOND_COUPLE_KEY = "swarm_cond_or_uncond_couple"


def lcm_for_list(numbers: list[int]) -> int:
    current_lcm = numbers[0]
    for number in numbers[1:]:
        current_lcm = math.lcm(current_lcm, number)
    return current_lcm


def reshape_mask(mask: torch.Tensor, size: tuple[int, int], bs: int, num_tokens: int) -> torch.Tensor:
    num_conds = mask.shape[0]
    mask_downsample = F.interpolate(mask, size=size, mode="nearest")
    return mask_downsample.view(num_conds, num_tokens, 1).repeat_interleave(bs, dim=0)


def split_kv_cond(cond: torch.Tensor) -> tuple[torch.Tensor, torch.Tensor]:
    return cond, cond


def unet_attn2_couple_wrapper(base_cond, cond_inputs: list, device, dtype: torch.dtype):
    conds = [cond[0][0].to(device, dtype=dtype) for cond in cond_inputs]
    base_strength = base_cond[0][1].get("strength", 1.0)
    strengths = [cond[0][1].get("strength", 1.0) for cond in cond_inputs]
    conds_kv = [split_kv_cond(cond) for cond in conds]
    num_tokens_k = [cond[0].shape[1] for cond in conds_kv]
    num_tokens_v = [cond[1].shape[1] for cond in conds_kv]

    def attn2_patch(q: torch.Tensor, k: torch.Tensor, v: torch.Tensor, extra_options):
        cond_or_uncond = extra_options["cond_or_uncond"]
        cond_or_uncond_couple = extra_options[COND_UNCOND_COUPLE_KEY] = list(cond_or_uncond)
        num_chunks = len(cond_or_uncond)
        bs = q.shape[0] // num_chunks
        if len(conds_kv) == 0:
            return q, k, v
        q_chunks = q.chunk(num_chunks, dim=0)
        k_chunks = k.chunk(num_chunks, dim=0)
        v_chunks = v.chunk(num_chunks, dim=0)
        lcm_tokens_k = lcm_for_list(num_tokens_k + [k.shape[1]])
        lcm_tokens_v = lcm_for_list(num_tokens_v + [v.shape[1]])
        conds_k_tensor = torch.cat([cond[0].repeat(bs, lcm_tokens_k // num_tokens_k[i], 1) * strengths[i] for i, cond in enumerate(conds_kv)], dim=0)
        conds_v_tensor = torch.cat([cond[1].repeat(bs, lcm_tokens_v // num_tokens_v[i], 1) * strengths[i] for i, cond in enumerate(conds_kv)], dim=0)
        qs = []
        ks = []
        vs = []
        cond_or_uncond_couple.clear()
        for i, cond_type in enumerate(cond_or_uncond):
            q_target = q_chunks[i]
            k_target = k_chunks[i].repeat(1, lcm_tokens_k // k.shape[1], 1)
            v_target = v_chunks[i].repeat(1, lcm_tokens_v // v.shape[1], 1)
            if cond_type == UNCOND:
                qs.append(q_target)
                ks.append(k_target)
                vs.append(v_target)
                cond_or_uncond_couple.append(UNCOND)
            else:
                qs.append(q_target.repeat(len(cond_inputs) + 1, 1, 1))
                ks.append(torch.cat([k_target * base_strength, conds_k_tensor], dim=0))
                vs.append(torch.cat([v_target * base_strength, conds_v_tensor], dim=0))
                cond_or_uncond_couple.extend(itertools.repeat(COND, len(cond_inputs) + 1))
        return torch.cat(qs, dim=0), torch.cat(ks, dim=0), torch.cat(vs, dim=0)

    return attn2_patch


def unet_attn2_output_couple_wrapper(mask: torch.Tensor):
    def attn2_output_patch(out: torch.Tensor, extra_options: dict[str, Any]):
        cond_or_uncond = extra_options[COND_UNCOND_COUPLE_KEY]
        size = tuple(extra_options["activations_shape"][-2:])
        bs = out.shape[0] // len(cond_or_uncond)
        num_tokens = out.shape[1]
        mask_downsample = reshape_mask(mask, size, bs, num_tokens)
        outputs = []
        cond_outputs = []
        i_cond = 0
        for i, cond_type in enumerate(cond_or_uncond):
            pos = i * bs
            next_pos = (i + 1) * bs
            if cond_type == UNCOND:
                outputs.append(out[pos:next_pos])
            else:
                pos_cond = i_cond * bs
                next_pos_cond = (i_cond + 1) * bs
                cond_outputs.append(out[pos:next_pos] * mask_downsample[pos_cond:next_pos_cond])
                i_cond += 1
        if len(cond_outputs) > 0:
            outputs.append(torch.stack(cond_outputs).sum(0))
        return torch.cat(outputs, dim=0)

    return attn2_output_patch


class SwarmAttentionCouple:
    @classmethod
    def INPUT_TYPES(cls):
        return {
            "required": {
                "model": ("MODEL",),
                "base_cond": ("CONDITIONING",),
                "base_mask": ("MASK",),
                "regions_json": ("STRING", {"default": "[]"}),
            },
            "optional": {
                "cond_1": ("CONDITIONING",),
                "mask_1": ("MASK",),
                "cond_2": ("CONDITIONING",),
                "mask_2": ("MASK",),
                "cond_3": ("CONDITIONING",),
                "mask_3": ("MASK",),
                "cond_4": ("CONDITIONING",),
                "mask_4": ("MASK",),
                "cond_5": ("CONDITIONING",),
                "mask_5": ("MASK",),
                "cond_6": ("CONDITIONING",),
                "mask_6": ("MASK",),
                "cond_7": ("CONDITIONING",),
                "mask_7": ("MASK",),
                "cond_8": ("CONDITIONING",),
                "mask_8": ("MASK",),
            },
        }

    RETURN_TYPES = ("MODEL",)
    FUNCTION = "patch"
    CATEGORY = "SwarmUI/model"

    def patch(self, model: ModelPatcher, base_cond, base_mask, regions_json, **kwargs):
        m = model.clone()
        model_type = type(m.model)
        if not (issubclass(model_type, SDXL) or issubclass(model_type, SDXLRefiner) or issubclass(model_type, Anima) or model_type == BaseModel):
            raise ValueError("Swarm Attention Couple only supports SD1-style BaseModel, SDXL, SDXL Refiner, and Anima models.")
        cond_inputs = []
        mask_inputs = []
        for i in range(1, 9):
            cond = kwargs.get(f"cond_{i}", None)
            mask = kwargs.get(f"mask_{i}", None)
            if cond is not None and mask is not None:
                cond_inputs.append(cond)
                mask_inputs.append(mask)
        if len(cond_inputs) == 0:
            return (m,)
        dtype = m.model.diffusion_model.dtype
        device = comfy.model_management.get_torch_device()
        masks = [base_mask] + mask_inputs
        mask = torch.stack(masks, dim=0).to(device, dtype=dtype)
        if mask.sum(dim=0).min() <= 0:
            raise ValueError("Swarm Attention Couple masks contain non-filled areas.")
        mask = mask / mask.sum(dim=0, keepdim=True)
        m.set_model_attn2_patch(unet_attn2_couple_wrapper(base_cond, cond_inputs, device, dtype))
        m.set_model_attn2_output_patch(unet_attn2_output_couple_wrapper(mask))
        return (m,)


NODE_CLASS_MAPPINGS = {
    "SwarmAttentionCouple": SwarmAttentionCouple,
}
