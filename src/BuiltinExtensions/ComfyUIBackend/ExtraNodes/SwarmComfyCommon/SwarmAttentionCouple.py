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
ANIMA_COUPLE_WRAPPER_KEY = "swarm_attention_couple_anima"
ANIMA_COUPLE_CONDS_KEY = "swarm_anima_couple_conds"
ANIMA_COUPLE_MASK_KEY = "swarm_anima_couple_mask"


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


def anima_couple_sample_wrapper(cond_inputs: list, device):
    """Run regional text through Anima's native adapter before sampling."""
    from comfy.sampler_helpers import convert_cond
    from comfy.samplers import process_conds

    converted_conds = [convert_cond(cond)[0] for cond in cond_inputs]

    def sample_wrapper(executor, *args, **kwargs):
        guider, _, extra_args, _, noise, latent_image, denoise_mask = args[:7]
        latent_shape = latent_image.shape if latent_image is not None else noise.shape
        processed = process_conds(
            guider.inner_model,
            noise,
            {"positive": [cond.copy() for cond in converted_conds]},
            device,
            latent_image,
            denoise_mask,
            extra_args.get("seed"),
            latent_shapes=getattr(guider.inner_model, "latent_shapes", None) or [latent_shape],
        )["positive"]
        model_options = extra_args["model_options"]
        options = model_options.get("transformer_options", {}).copy()
        options[ANIMA_COUPLE_CONDS_KEY] = [cond["model_conds"]["c_crossattn"].cond for cond in processed]
        model_options["transformer_options"] = options
        # The global factor is already represented in the attention masks.
        # Letting Comfy apply it to the entire positive prediction would erase
        # every region when the global factor is zero.
        original_conds = guider.conds
        if "positive" not in original_conds:
            return executor(*args, **kwargs)
        guider.conds = original_conds.copy()
        guider.conds["positive"] = [dict(cond, strength=1.0) for cond in original_conds["positive"]]
        try:
            return executor(*args, **kwargs)
        finally:
            guider.conds = original_conds

    return sample_wrapper


def anima_couple_diffusion_wrapper(mask: torch.Tensor):
    def diffusion_wrapper(executor, *args, **kwargs):
        shape = list(args[0].shape)
        patch_spatial = executor.class_obj.patch_spatial
        # Cosmos pads the input before creating image tokens.
        shape[-2] = (shape[-2] + patch_spatial - 1) // patch_spatial
        shape[-1] = (shape[-1] + patch_spatial - 1) // patch_spatial
        options = (kwargs.get("transformer_options") or {}).copy()
        options["activations_shape"] = tuple(shape)
        options[ANIMA_COUPLE_MASK_KEY] = mask
        kwargs["transformer_options"] = options
        return executor(*args, **kwargs)

    return diffusion_wrapper


def anima_couple_forward_wrapper(previous_forward):
    """Expand positive cross-attention by region, then restore its batch order."""
    def forward_wrapper(x, context=None, rope_emb=None, transformer_options=None):
        options = (transformer_options or {}).copy()
        conds = options.get(ANIMA_COUPLE_CONDS_KEY, [])
        cond_or_uncond = options.get("cond_or_uncond", [])
        if context is None or not conds or COND not in cond_or_uncond:
            return previous_forward(x, context, rope_emb, options)

        if any(cond.ndim != 3 or cond.shape[-1] != context.shape[-1] for cond in conds):
            raise ValueError("Swarm Attention Couple requires matching, preprocessed Anima text conditioning for every region.")
        num_conds = len(conds) + 1
        bs = x.shape[0] // len(cond_or_uncond)
        num_tokens = lcm_for_list([context.shape[1]] + [cond.shape[1] for cond in conds])
        regional_contexts = torch.cat([
            cond.to(context).repeat(
                (bs + cond.shape[0] - 1) // cond.shape[0], num_tokens // cond.shape[1], 1
            )[:bs]
            for cond in conds
        ], dim=0)
        x_chunks = x.chunk(len(cond_or_uncond), dim=0)
        context_chunks = context.chunk(len(cond_or_uncond), dim=0)
        xs = []
        contexts = []
        expanded_flags = []
        for flag, x_chunk, context_chunk in zip(cond_or_uncond, x_chunks, context_chunks):
            context_chunk = context_chunk.repeat(1, num_tokens // context.shape[1], 1)
            if flag == UNCOND:
                xs.append(x_chunk)
                contexts.append(context_chunk)
                expanded_flags.append(UNCOND)
            else:
                xs.append(x_chunk.repeat(num_conds, 1, 1))
                contexts.append(torch.cat([context_chunk, regional_contexts], dim=0))
                expanded_flags.extend(itertools.repeat(COND, num_conds))
        options["cond_or_uncond"] = expanded_flags
        out = previous_forward(torch.cat(xs, dim=0), torch.cat(contexts, dim=0), rope_emb, options)

        mask = options[ANIMA_COUPLE_MASK_KEY]
        size = tuple(options["activations_shape"][-2:])
        mask_downsample = reshape_mask(mask, size, bs, out.shape[1]).to(out)
        outputs = []
        offset = 0
        for flag in cond_or_uncond:
            count = bs if flag == UNCOND else bs * num_conds
            chunk = out[offset:offset + count]
            if flag != UNCOND:
                chunk = (chunk * mask_downsample).reshape(num_conds, bs, *out.shape[1:]).sum(dim=0)
            outputs.append(chunk)
            offset += count
        return torch.cat(outputs, dim=0)

    return forward_wrapper


def patch_anima_couple(patcher: ModelPatcher, base_cond, cond_inputs: list, mask: torch.Tensor, global_factor=None):
    """Anima/Cosmos route adapted from ComfyUI-ppm's cosmos_couple.py.

    https://github.com/pamparamm/ComfyUI-ppm/blob/master/src/attention_couple/cosmos_couple.py
    """
    from comfy.ldm.cosmos.predict2 import Attention as CosmosAttention
    from comfy.patcher_extension import WrappersMP

    # Region strength is mask coverage. The global factor retains shared
    # context within those regions, rather than scaling Anima's embeddings.
    if global_factor is None:
        global_factor = base_cond[0][1].get("strength", 0.0)
    global_factor = max(0.0, min(1.0, global_factor))
    shared_mask = mask[:1] + mask[1:].sum(dim=0, keepdim=True) * global_factor
    mask = torch.cat([shared_mask, mask[1:] * (1.0 - global_factor)], dim=0)
    patcher.add_wrapper_with_key(
        WrappersMP.SAMPLER_SAMPLE, ANIMA_COUPLE_WRAPPER_KEY,
        anima_couple_sample_wrapper(cond_inputs, mask.device),
    )
    patcher.add_wrapper_with_key(
        WrappersMP.DIFFUSION_MODEL, ANIMA_COUPLE_WRAPPER_KEY,
        anima_couple_diffusion_wrapper(mask),
    )
    for name, module in patcher.get_model_object("diffusion_model").named_modules():
        if isinstance(module, CosmosAttention) and "cross_attn" in name:
            forward_name = f"diffusion_model.{name}.forward"
            previous_forward = patcher.get_model_object(forward_name)
            patcher.add_object_patch(forward_name, anima_couple_forward_wrapper(previous_forward))


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
                "global_factor": ("FLOAT", {"default": 0.5, "min": 0.0, "max": 1.0, "step": 0.05, "tooltip": "For Anima, how much shared prompt to retain inside regions: 0 is regional, 1 is shared only."}),
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

    def patch(self, model: ModelPatcher, base_cond, base_mask, regions_json, global_factor=None, **kwargs):
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
        if issubclass(model_type, Anima):
            patch_anima_couple(m, base_cond, cond_inputs, mask, global_factor)
        else:
            m.set_model_attn2_patch(unet_attn2_couple_wrapper(base_cond, cond_inputs, device, dtype))
            m.set_model_attn2_output_patch(unet_attn2_output_couple_wrapper(mask))
        return (m,)


NODE_CLASS_MAPPINGS = {
    "SwarmAttentionCouple": SwarmAttentionCouple,
}
