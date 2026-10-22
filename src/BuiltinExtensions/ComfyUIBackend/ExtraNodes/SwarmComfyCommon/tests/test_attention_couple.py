"""CPU-only regressions: no Comfy runtime, models, app or CUDA initialization.

Run with CUDA_VISIBLE_DEVICES='' PYTHONDONTWRITEBYTECODE=1 python -m unittest
-v test_attention_couple.py. SWARM_ATTENTION_COUPLE_PATH selects another copy.
"""
import contextlib
import copy
import importlib.util
import math
import os
from pathlib import Path
import sys
import types
import unittest
from unittest.mock import patch

import torch
import torch.nn.functional as F


class WrappersMP:
    SAMPLER_SAMPLE = "sampler_sample"
    DIFFUSION_MODEL = "diffusion_model"


class WrapperExecutor:
    """Comfy's continuation and class_obj contract, without runtime imports."""
    def __init__(self, original, class_obj, wrappers, idx=0):
        self.original, self.class_obj = original, class_obj
        self.wrappers, self.idx = wrappers, idx

    def __call__(self, *args, **kwargs):
        return type(self)(self.original, self.class_obj, self.wrappers, self.idx + 1).execute(*args, **kwargs)

    def execute(self, *args, **kwargs):
        if self.idx == len(self.wrappers):
            return self.original(*args, **kwargs)
        return self.wrappers[self.idx](self, *args, **kwargs)

    @classmethod
    def new_class_executor(cls, original, class_obj, wrappers, idx=0):
        return cls(original, class_obj, wrappers, idx)

    @classmethod
    def new_executor(cls, original, wrappers, idx=0):
        return cls(original, None, wrappers, idx)


def get_all_wrappers(kind, options, is_model_options=False):
    if is_model_options:
        options = options.get("transformer_options", {})
    return [fn for group in options.get("wrappers", {}).get(kind, {}).values() for fn in group]


class Attention(torch.nn.Module):
    """Deterministic attention oracle with current Cosmos hook convention."""
    def __init__(self):
        super().__init__()
        self.is_selfattn = False
        self.calls = []

    def forward(self, x, context=None, rope_emb=None, transformer_options=None):
        options = transformer_options or {}
        q, k, v = x, context, context
        for hook in options.get("patches", {}).get("attn2_patch", []):
            # The original node fails here because its old hook rejects pe.
            result = hook(q, k, v, pe=rope_emb, attn_mask=None, extra_options=dict(options))
            q, k, v = result.get("q", q), result.get("k", k), result.get("v", v)
        self.calls.append((k.detach().clone(), rope_emb, dict(options)))
        return v.mean(dim=1, keepdim=True).expand(q.shape[0], q.shape[1], 1).clone()


class TinyDiffusion(torch.nn.Module):
    def __init__(self):
        super().__init__()
        self.dtype = torch.float32
        self.patch_spatial = 2
        self.patch_temporal = 1
        self.blocks = torch.nn.ModuleList([torch.nn.Module()])
        self.blocks[0].cross_attn = Attention()
        self.blocks[0].self_attn = Attention()
        self.blocks[0].self_attn.is_selfattn = True

    def forward(self, x, timesteps, context, fps=None, padding_mask=None, **kwargs):
        options = kwargs.get("transformer_options", {})
        return WrapperExecutor.new_class_executor(
            self._forward, self, get_all_wrappers(WrappersMP.DIFFUSION_MODEL, options)
        ).execute(x, timesteps, context, fps, padding_mask, **kwargs)

    def _forward(self, x, timesteps, context, fps=None, padding_mask=None, **kwargs):
        tokens = math.ceil(x.shape[-3] / self.patch_temporal)
        tokens *= math.ceil(x.shape[-2] / self.patch_spatial) * math.ceil(x.shape[-1] / self.patch_spatial)
        q = torch.zeros((x.shape[0], tokens, 1), dtype=x.dtype, device=x.device)
        return self.blocks[0].cross_attn(
            q, context, rope_emb="test-rope", transformer_options=kwargs.get("transformer_options", {})
        )


class BaseModel:
    def __init__(self):
        self.diffusion_model = TinyDiffusion()


class Anima(BaseModel):
    pass


class SDXL(BaseModel):
    pass


class SDXLRefiner(BaseModel):
    pass


class ModelPatcher:
    def __init__(self, model):
        self.model = model
        self.model_options = {"transformer_options": {}}
        self.object_patches = {}
        self.wrappers = {}
        self.load_device = torch.device("cpu")

    def clone(self):
        cloned = type(self)(self.model)
        cloned.model_options = copy.deepcopy(self.model_options)
        cloned.object_patches = self.object_patches.copy()
        cloned.wrappers = {kind: {key: fns[:] for key, fns in groups.items()} for kind, groups in self.wrappers.items()}
        return cloned

    def get_model_object(self, name):
        if name in self.object_patches:
            return self.object_patches[name]
        obj = self.model
        for segment in name.split("."):
            obj = getattr(obj, segment)
        return obj

    def add_object_patch(self, name, obj):
        self.object_patches[name] = obj

    def add_wrapper_with_key(self, kind, key, wrapper):
        self.wrappers.setdefault(kind, {}).setdefault(key, []).append(wrapper)

    def get_all_wrappers(self, kind):
        return [fn for group in self.wrappers.get(kind, {}).values() for fn in group]

    def set_model_attn2_patch(self, fn):
        self.model_options["transformer_options"].setdefault("patches", {}).setdefault("attn2_patch", []).append(fn)

    def set_model_attn2_output_patch(self, fn):
        self.model_options["transformer_options"].setdefault("patches", {}).setdefault("attn2_output_patch", []).append(fn)


class CondValue:
    def __init__(self, cond):
        self.cond = cond

    def process_cond(self, batch_size, device, area=None, **kwargs):
        value = self.cond.to(device)
        return type(self)(value if value.shape[0] == batch_size else value.repeat(batch_size, 1, 1))


PROCESS_CALLS = []


def convert_cond(conds):
    return [dict(meta, cross_attn=value, model_conds=dict(meta.get("model_conds", {}))) for value, meta in conds]


def process_conds(model, noise, conds, device, latent_image=None, denoise_mask=None, seed=None, latent_shapes=None):
    PROCESS_CALLS.append((model, noise, conds, device))
    result = {}
    for name, entries in conds.items():
        result[name] = []
        for entry in entries:
            item = dict(entry)
            value = item["cross_attn"]
            # Native text passes through Anima's adapter; expanded text is unchanged.
            if item.get("t5xxl_ids") is not None:
                value = value + 10
            item["model_conds"] = dict(item.get("model_conds", {}), c_crossattn=CondValue(value))
            result[name].append(item)
    return result


@contextlib.contextmanager
def comfy_stub_scope(modules):
    """Restore Comfy imports while retaining unrelated lazy Torch imports."""
    original_names = set(sys.modules)
    imported = {}
    try:
        with patch.dict(sys.modules, modules):
            try:
                yield
            finally:
                imported = {name: value for name, value in sys.modules.items()
                            if name not in original_names and name not in modules}
    finally:
        # Reloading native Torch modules would register their operators twice.
        sys.modules.update(imported)


def load_node():
    names = ["comfy", "comfy.model_management", "comfy.model_base", "comfy.model_patcher",
             "comfy.patcher_extension", "comfy.sampler_helpers", "comfy.samplers", "comfy.ldm",
             "comfy.ldm.cosmos", "comfy.ldm.cosmos.predict2"]
    modules = {name: types.ModuleType(name) for name in names}
    for name, module in modules.items():
        module.__path__ = []
        if "." in name:
            parent, child = name.rsplit(".", 1)
            setattr(modules[parent], child, module)
    modules["comfy.model_management"].get_torch_device = lambda: torch.device("cpu")
    modules["comfy.model_base"].__dict__.update(BaseModel=BaseModel, Anima=Anima, SDXL=SDXL, SDXLRefiner=SDXLRefiner)
    modules["comfy.model_patcher"].ModelPatcher = ModelPatcher
    modules["comfy.patcher_extension"].__dict__.update(WrappersMP=WrappersMP, WrapperExecutor=WrapperExecutor, get_all_wrappers=get_all_wrappers)
    modules["comfy.sampler_helpers"].convert_cond = convert_cond
    modules["comfy.samplers"].process_conds = process_conds
    modules["comfy.ldm.cosmos.predict2"].Attention = Attention
    here = Path(__file__).resolve().parent
    adjacent = here / "SwarmAttentionCouple.py"
    source = Path(os.environ.get("SWARM_ATTENTION_COUPLE_PATH", adjacent if adjacent.exists() else here.parent / "SwarmAttentionCouple.py"))
    spec = importlib.util.spec_from_file_location("attention_couple_under_test", source)
    module = importlib.util.module_from_spec(spec)
    with comfy_stub_scope(modules):
        spec.loader.exec_module(module)
    return module, modules


NODE, COMFY_STUBS = load_node()


def conditioning(value, tokens=2, strength=1.0, raw=False):
    values = torch.tensor(value, dtype=torch.float32).reshape(-1, 1, 1).expand(-1, tokens, 1).clone()
    metadata = {"strength": strength}
    if raw:
        metadata["t5xxl_ids"] = torch.arange(tokens)
    return [[values, metadata]]


@contextlib.contextmanager
def installed_object_patches(patcher):
    saved = []
    for path, value in patcher.object_patches.items():
        owner = patcher.model
        segments = path.split(".")
        for segment in segments[:-1]:
            owner = getattr(owner, segment)
        saved.append((owner, segments[-1], getattr(owner, segments[-1])))
        setattr(owner, segments[-1], value)
    try:
        yield
    finally:
        for owner, name, old in reversed(saved):
            setattr(owner, name, old)


class AttentionCoupleTests(unittest.TestCase):
    def setUp(self):
        # Lazy imports stay isolated to each test and restore any existing modules.
        stub_scope = comfy_stub_scope(COMFY_STUBS)
        stub_scope.__enter__()
        self.addCleanup(stub_scope.__exit__, None, None, None)
        PROCESS_CALLS.clear()
        self.original = ModelPatcher(Anima())

    def patched(self, base=None, region=None, size=(4, 4), masks=None, model=None):
        base = conditioning(2) if base is None else base
        region = conditioning(6, tokens=3) if region is None else region
        if masks is None:
            base_mask = torch.zeros((1, *size))
            base_mask[..., :size[1] // 2] = 1
            masks = (base_mask, 1 - base_mask)
        result, = NODE.SwarmAttentionCouple().patch(model or self.original, base, masks[0], "[]", cond_1=region, mask_1=masks[1])
        return result

    def sample(self, patcher, order=(1, 0), bs=1, size=(4, 4), base=2, uncond=-2, context_dtype=torch.float32):
        noise = torch.zeros((bs, 1, 1, *size), device="cpu")
        options = copy.deepcopy(patcher.model_options)
        options["transformer_options"]["wrappers"] = patcher.wrappers
        model_wrap = types.SimpleNamespace(
            inner_model=patcher.model, model_patcher=patcher, model_options=options,
            conds={"positive": convert_cond(conditioning(base)), "negative": convert_cond(conditioning(uncond))},
        )
        sampler = types.SimpleNamespace()
        extra_args = {"model_options": options, "seed": 17}

        def sample_impl(model_wrap, sigmas, extra_args, callback, noise, latent_image, denoise_mask, disable_pbar):
            context = torch.cat([conditioning(uncond if kind else base)[0][0].repeat(bs, 1, 1) for kind in order]).to(context_dtype)
            x = noise.repeat(len(order), 1, 1, 1, 1)
            transformer_options = dict(extra_args["model_options"]["transformer_options"], cond_or_uncond=list(order), original_shape=x.shape)
            return patcher.model.diffusion_model(x, torch.ones(x.shape[0]), context, transformer_options=transformer_options)

        with installed_object_patches(patcher):
            return WrapperExecutor.new_class_executor(
                sample_impl, sampler, patcher.get_all_wrappers(WrappersMP.SAMPLER_SAMPLE)
            ).execute(model_wrap, torch.tensor([1., 0.]), extra_args, None, noise, noise.clone(), None, True)

    def expected_conditional(self, size=(4, 4), base=2., region=6.):
        mask = torch.zeros((1, 1, *size))
        mask[..., :size[1] // 2] = 1
        grid = (math.ceil(size[0] / 2), math.ceil(size[1] / 2))
        mask = F.interpolate(mask, size=grid, mode="nearest").flatten(2).transpose(1, 2)
        return mask * base + (1 - mask) * region

    def assertTensor(self, actual, expected):
        self.assertEqual(actual.device.type, "cpu")
        torch.testing.assert_close(actual, expected, rtol=0, atol=1e-6)

    def test_anima_registers_sampler_diffusion_cross_forward_without_legacy_hooks(self):
        patched = self.patched()
        self.assertTrue(patched.get_all_wrappers(WrappersMP.SAMPLER_SAMPLE))
        self.assertTrue(patched.get_all_wrappers(WrappersMP.DIFFUSION_MODEL))
        self.assertTrue(any(path.endswith("cross_attn.forward") for path in patched.object_patches))
        self.assertFalse(any(path.endswith("self_attn.forward") for path in patched.object_patches))
        self.assertNotIn("attn2_patch", patched.model_options["transformer_options"].get("patches", {}))
        self.assertEqual(self.original.object_patches, {})
        self.assertEqual(self.original.wrappers, {})

    def test_sample_diffusion_attention_couples_spatial_regions(self):
        actual = self.sample(self.patched())
        self.assertTensor(actual, torch.cat([torch.full((1, 4, 1), -2.), self.expected_conditional()]))

    def test_native_region_context_runs_through_model_condition_processing(self):
        actual = self.sample(self.patched(region=conditioning(3, raw=True)), order=(0,))
        self.assertTensor(actual, self.expected_conditional(region=13))
        self.assertTrue(PROCESS_CALLS, "Native Anima context must pass through process_conds")
        self.assertIs(PROCESS_CALLS[0][0], self.original.model)

    def test_already_expanded_region_context_values_are_preserved(self):
        actual = self.sample(self.patched(region=conditioning(23, tokens=5)), order=(0,))
        self.assertTensor(actual, self.expected_conditional(region=23))

    def test_processed_region_converts_to_live_context_dtype(self):
        patched = self.patched(region=conditioning(3, raw=True))
        actual = self.sample(patched, order=(0,), context_dtype=torch.float64)
        self.assertTensor(actual, self.expected_conditional(region=13).to(torch.float64))
        raw_region = PROCESS_CALLS[0][2]["positive"][0]["cross_attn"]
        self.assertEqual(raw_region.dtype, torch.float32)
        calls = patched.model.diffusion_model.blocks[0].cross_attn.calls
        self.assertTrue(calls)
        self.assertTrue(all(context.dtype == torch.float64 for context, _, _ in calls))

    def test_two_regions_normalize_overlapping_masks_and_merge_each_batch(self):
        base_mask = torch.ones((1, 4, 4))
        region_one = torch.zeros_like(base_mask)
        region_one[..., :2] = 3
        region_two = torch.zeros_like(base_mask)
        region_two[..., 2:] = 2
        patched, = NODE.SwarmAttentionCouple().patch(
            self.original, conditioning(2), base_mask, "[]",
            cond_1=conditioning([6, 10], tokens=3), mask_1=region_one,
            cond_2=conditioning([10, 14], tokens=5), mask_2=region_two,
        )
        conditional = torch.tensor([[[5.], [22 / 3], [5.], [22 / 3]],
                                    [[8.], [10.], [8.], [10.]]])
        for order in [(1, 0), (0, 1)]:
            with self.subTest(order=order):
                actual = self.sample(patched, order=order, bs=2)
                expected = torch.cat([torch.full((2, 4, 1), -2.) if kind else conditional for kind in order])
                self.assertTensor(actual, expected)

    def test_base_and_region_strengths_scale_their_contexts(self):
        actual = self.sample(self.patched(base=conditioning(2, strength=.5), region=conditioning(6, strength=2)), order=(0,))
        self.assertTensor(actual, self.expected_conditional(base=1, region=12))

    def test_cfg_chunk_order_is_preserved_in_both_directions(self):
        for order in [(1, 0), (0, 1)]:
            with self.subTest(order=order):
                actual = self.sample(self.patched(), order=order)
                parts = [torch.full((1, 4, 1), -2.) if kind else self.expected_conditional() for kind in order]
                self.assertTensor(actual, torch.cat(parts))

    def test_cond_only_and_uncond_only_batches(self):
        for order in [(0,), (1,)]:
            with self.subTest(order=order):
                actual = self.sample(self.patched(), order=order)
                expected = self.expected_conditional() if order == (0,) else torch.full((1, 4, 1), -2.)
                self.assertTensor(actual, expected)

    def test_batch_two_distinct_region_values_and_different_text_lengths(self):
        for order in [(1, 0), (0, 1)]:
            with self.subTest(order=order):
                actual = self.sample(self.patched(region=conditioning([5, 9], tokens=3)), order=order, bs=2)
                cond = torch.cat([self.expected_conditional(region=5), self.expected_conditional(region=9)])
                self.assertTensor(actual, torch.cat([torch.full((2, 4, 1), -2.) if kind else cond for kind in order]))

    def test_odd_latents_use_ceil_padded_spatial_token_grid(self):
        size = (5, 7)
        actual = self.sample(self.patched(size=size), order=(0,), size=size)
        self.assertTensor(actual, self.expected_conditional(size=size))
        self.assertEqual(actual.shape, (1, 12, 1))

    def test_existing_forward_object_patch_is_chained(self):
        calls = []
        old = self.original.get_model_object("diffusion_model.blocks.0.cross_attn.forward")
        def prior_forward(x, context=None, rope_emb=None, transformer_options=None):
            calls.append(rope_emb)
            return old(x, context, rope_emb, transformer_options) + 3
        self.original.add_object_patch("diffusion_model.blocks.0.cross_attn.forward", prior_forward)
        actual = self.sample(self.patched(), order=(0,))
        self.assertTensor(actual, self.expected_conditional() + 3)
        self.assertTrue(calls)
        self.assertTrue(all(rope == "test-rope" for rope in calls))

    def test_no_regions_returns_clone_without_installing_hooks(self):
        patched, = NODE.SwarmAttentionCouple().patch(self.original, conditioning(2), torch.ones((1, 4, 4)), "[]")
        self.assertIsNot(patched, self.original)
        self.assertEqual(patched.object_patches, {})
        self.assertEqual(patched.wrappers, {})
        self.assertEqual(patched.model_options, self.original.model_options)

    def test_sdxl_keeps_existing_legacy_attention_route(self):
        patched = self.patched(model=ModelPatcher(SDXL()))
        hooks = patched.model_options["transformer_options"]["patches"]
        self.assertEqual(len(hooks["attn2_patch"]), 1)
        self.assertEqual(len(hooks["attn2_output_patch"]), 1)
        self.assertEqual(patched.object_patches, {})
        self.assertEqual(patched.wrappers, {})
        options = {"cond_or_uncond": [1, 0], "activations_shape": (1, 1, 2, 2)}
        q = torch.zeros((2, 4, 1))
        kv = torch.cat([torch.full((1, 2, 1), -2.), torch.full((1, 2, 1), 2.)])
        q, k, v = hooks["attn2_patch"][0](q, kv, kv, options)
        outputs = v.mean(dim=1, keepdim=True).expand(q.shape[0], q.shape[1], 1)
        actual = hooks["attn2_output_patch"][0](outputs, options)
        self.assertTensor(actual, torch.cat([torch.full((1, 4, 1), -2.), self.expected_conditional()]))


if __name__ == "__main__":
    unittest.main(verbosity=2)
