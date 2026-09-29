"""Regression coverage for runtime-selected Anima LoRA target depths."""
import pathlib
import sys
import types
import unittest

PACKAGE = "_swarm_anima_lora_tests"
package = types.ModuleType(PACKAGE)
package.__path__ = [str(pathlib.Path(__file__).resolve().parents[1])]
sys.modules[PACKAGE] = package

from _swarm_anima_lora_tests.SwarmAnimaLora import prepare_anima_lora, get_target_block_count


class AnimaLoraMappingTests(unittest.TestCase):
    def test_expansion_preserves_tensors_and_alpha(self):
        for target, expected in ((40, 3), (52, 4)):
            down, up, alpha = object(), object(), object()
            source = {
                "diffusion_model.blocks.2.attn.lora_down.weight": down,
                "diffusion_model.blocks.2.attn.lora_up.weight": up,
                "diffusion_model.blocks.2.attn.alpha": alpha,
                "diffusion_model.blocks.27.attn.alpha": object(),
            }
            mapped = prepare_anima_lora(source, target)
            for suffix, value in (("lora_down.weight", down), ("lora_up.weight", up), ("alpha", alpha)):
                self.assertIs(mapped[f"diffusion_model.blocks.{expected}.attn.{suffix}"], value)
            self.assertEqual(len(mapped), len(source))

    def test_40_to_52_and_native_identity(self):
        source = {"lora_unet_blocks_39_attn.alpha": object()}
        self.assertIs(prepare_anima_lora(source, 40), source)
        self.assertIs(prepare_anima_lora(source, 52)["lora_unet_blocks_51_attn.alpha"], next(iter(source.values())))
        native = {"diffusion_model.blocks.51.attn.alpha": object()}
        self.assertIs(prepare_anima_lora(native, 52), native)

    def test_down_mapping_and_unknown_targets_fail(self):
        for source, target in ((40, 28), (52, 28), (52, 40), (28, 41)):
            with self.subTest(source=source, target=target), self.assertRaises(ValueError):
                prepare_anima_lora({f"diffusion_model.blocks.{source - 1}.attn.alpha": object()}, target)

    def test_adapter_compatibility_matches_legacy_52_bridge(self):
        source = {"diffusion_model.blocks.27.attn.alpha": object(), "diffusion_model.llm_adapter.proj.alpha": object()}
        self.assertIn("diffusion_model.llm_adapter.proj.alpha", prepare_anima_lora(source, 40))
        self.assertNotIn("diffusion_model.llm_adapter.proj.alpha", prepare_anima_lora(source, 52))

    def test_blockless_lora_passes_through(self):
        source = {"diffusion_model.llm_adapter.blocks.0.proj.alpha": object(), "lora_te_text_model.alpha": object()}
        for target in (28, 40, 52):
            self.assertIs(prepare_anima_lora(source, target), source)

    def test_sparse_lora_requires_source_depth_and_preserves_native_indices(self):
        source = {"diffusion_model.blocks.2.attn.alpha": object()}
        with self.assertRaisesRegex(ValueError, "partial-layer"):
            prepare_anima_lora(source, 40)
        self.assertIs(prepare_anima_lora(source, 40, "40"), source)
        self.assertIn("diffusion_model.blocks.3.attn.alpha", prepare_anima_lora(source, 40, "28"))
        with self.assertRaises(ValueError):
            prepare_anima_lora(source, 40, "52")

    def test_dotted_training_prefixes_are_remapped(self):
        for prefix in ("", "net.", "transformer.", "diffusion_model.", "model.diffusion_model."):
            source = {f"{prefix}blocks.27.attn.alpha": object()}
            self.assertIn(f"{prefix}blocks.39.attn.alpha", prepare_anima_lora(source, 40))

    def test_runtime_host_depth_is_used(self):
        for count in (28, 40, 52):
            model = types.SimpleNamespace(get_model_object=lambda name, count=count: types.SimpleNamespace(blocks=[None] * count))
            self.assertEqual(get_target_block_count(model), count)


class AnimaLoraLoaderTests(unittest.TestCase):
    def setUp(self):
        from unittest.mock import Mock, patch
        from _swarm_anima_lora_tests.SwarmAnimaLora import SwarmAnimaLoraLoader, SwarmAnimaLoraLoaderModelOnly, SwarmAnimaCreateHookLora
        self.classes = (SwarmAnimaLoraLoader, SwarmAnimaLoraLoaderModelOnly, SwarmAnimaCreateHookLora)
        self.raw = {"diffusion_model.blocks.27.attn.alpha": object()}
        self.metadata = {"test": "metadata retained"}
        self.load = Mock(return_value=(self.raw, self.metadata))
        self.apply = Mock(return_value=("patched_model", "patched_clip"))
        self.create_hook = Mock(return_value="new_hook")
        comfy = types.ModuleType("comfy")
        comfy.utils = types.ModuleType("comfy.utils")
        comfy.utils.load_torch_file = self.load
        comfy.sd = types.ModuleType("comfy.sd")
        comfy.sd.load_lora_for_models = self.apply
        comfy.hooks = types.ModuleType("comfy.hooks")
        comfy.hooks.create_hook_lora = self.create_hook
        comfy.hooks.HookGroup = Mock()
        paths = types.ModuleType("folder_paths")
        paths.get_full_path_or_raise = Mock(return_value="/loras/example.safetensors")
        paths.get_filename_list = Mock(return_value=["example.safetensors"])
        self.modules = patch.dict(sys.modules, {"comfy": comfy, "comfy.utils": comfy.utils, "comfy.sd": comfy.sd, "comfy.hooks": comfy.hooks, "folder_paths": paths})
        self.modules.start()
        self.addCleanup(self.modules.stop)

    @staticmethod
    def model(count):
        return types.SimpleNamespace(get_model_object=lambda name: types.SimpleNamespace(blocks=[None] * count))

    def test_host_switch_reuses_raw_weights_but_not_remapped_keys(self):
        loader = self.classes[0]()
        for count in (40, 52, 28):
            model = self.model(count)
            result = loader.load_lora(model, "clip", "example", -0.7, 0.25)
            self.assertEqual(result, ("patched_model", "patched_clip"))
            args = self.apply.call_args.args
            self.assertIs(args[0], model)
            self.assertEqual(args[3:], (-0.7, 0.25))
            self.assertIs(args[2][f"diffusion_model.blocks.{count - 1}.attn.alpha"], next(iter(self.raw.values())))
            self.assertIs(self.apply.call_args.kwargs["lora_metadata"], self.metadata)
        self.assertEqual(self.load.call_count, 1)
        self.assertEqual(list(self.raw), ["diffusion_model.blocks.27.attn.alpha"])

    def test_model_only_passes_strength_without_clip(self):
        result = self.classes[1]().load_lora_model_only(self.model(40), "example", 0.6)
        self.assertEqual(result, ("patched_model",))
        self.assertIsNone(self.apply.call_args.args[1])
        self.assertEqual(self.apply.call_args.args[3:], (0.6, 0))

    def test_hook_keeps_strengths_and_previous_chain(self):
        from unittest.mock import Mock
        previous = Mock()
        previous.clone_and_combine.return_value = "combined"
        result = self.classes[2]().create_hook(self.model(52), "example", 0.8, -0.2, previous)
        self.assertEqual(result, ("combined",))
        previous.clone_and_combine.assert_called_once_with("new_hook")
        self.assertEqual(self.create_hook.call_args.kwargs["strength_model"], 0.8)
        self.assertEqual(self.create_hook.call_args.kwargs["strength_clip"], -0.2)
        self.assertIn("diffusion_model.blocks.51.attn.alpha", self.create_hook.call_args.kwargs["lora"])

    def test_zero_strength_does_not_load_or_remap(self):
        model, clip, hooks = object(), object(), object()
        self.assertEqual(self.classes[0]().load_lora(model, clip, "example", 0, 0), (model, clip))
        self.assertEqual(self.classes[1]().load_lora_model_only(model, "example", 0), (model,))
        self.assertEqual(self.classes[2]().create_hook(model, "example", 0, 0, hooks), (hooks,))
        self.load.assert_not_called()
        self.apply.assert_not_called()
        self.create_hook.assert_not_called()

    def test_hook_schema_requires_model(self):
        self.assertEqual(self.classes[2].INPUT_TYPES()["required"]["model"], ("MODEL",))


if __name__ == "__main__":
    unittest.main()
