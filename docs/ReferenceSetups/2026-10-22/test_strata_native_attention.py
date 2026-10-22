"""Isolated Strata adapter regressions: no Torch, Comfy runtime, or model loads.

STRATA_IMAGE_CONTROLS_PATH selects an installed or alternate extension file.
"""
import asyncio
import importlib.util
import inspect
import os
from pathlib import Path
import sys
import types
import unittest
from unittest.mock import patch


class Anima:
    pass


class CosmosPredict2:
    pass


class SDXL:
    pass


class FakePatcher:
    def __init__(self, model):
        self.model = model
        self.clones = []

    def clone(self):
        result = object()
        self.clones.append(result)
        return result


class StrataNativeAttentionTests(unittest.TestCase):
    def setUp(self):
        self.native_calls = []
        self.native_result = (object(),)
        calls, result = self.native_calls, self.native_result

        class NativeSwarmAttentionCouple:
            def patch(self, *args, **kwargs):
                calls.append((args, kwargs))
                return result

        self.native = NativeSwarmAttentionCouple
        self.original_native_patch = self.native.patch
        self.other_node = object()
        registry = {"SwarmAttentionCouple": self.native, "UnrelatedNode": self.other_node}
        # A foreign SAM3 node must not load the native model cache or Torch.
        registry["SAM3Grounding"] = type("ForeignSAM3Grounding", (), {"__module__": "foreign_sam3"})
        self.startup = []

        class Masks:
            def __init__(self, data):
                self.data = data

        self.masks = Masks
        names = ["nodes", "comfy", "comfy.model_base", "ultralytics",
                 "ultralytics.engine", "ultralytics.engine.results", "server"]
        self.modules = {name: types.ModuleType(name) for name in names}
        for name, module in self.modules.items():
            module.__path__ = []
            if "." in name:
                parent, child = name.rsplit(".", 1)
                setattr(self.modules[parent], child, module)
        self.modules["nodes"].NODE_CLASS_MAPPINGS = registry
        self.modules["comfy.model_base"].__dict__.update(Anima=Anima, CosmosPredict2=CosmosPredict2)
        self.modules["ultralytics.engine.results"].Masks = Masks
        self.modules["server"].PromptServer = types.SimpleNamespace(
            instance=types.SimpleNamespace(app=types.SimpleNamespace(on_startup=self.startup)))
        scope = patch.dict(sys.modules, self.modules)
        scope.start()
        self.addCleanup(scope.stop)
        source = Path(os.environ.get("STRATA_IMAGE_CONTROLS_PATH", Path(__file__).with_name("strata_image_controls.py")))
        spec = importlib.util.spec_from_file_location("strata_image_controls_under_test", source)
        self.extension = importlib.util.module_from_spec(spec)
        spec.loader.exec_module(self.extension)
        self.registry = registry

    def run_startup(self):
        async def run():
            app = self.modules["server"].PromptServer.instance.app
            for callback in list(self.startup):
                result = callback(app)
                if inspect.isawaitable(result):
                    await result
        asyncio.run(run())

    def test_import_and_all_startup_callbacks_preserve_native_patch_identity(self):
        self.assertIs(self.native.patch, self.original_native_patch)
        self.run_startup()
        self.assertIs(self.native.patch, self.original_native_patch)

    def test_anima_delegates_all_arguments_to_native_without_ppm(self):
        model, base_cond, base_mask = FakePatcher(Anima()), object(), object()
        region_cond, region_mask = object(), object()
        options = {"global_factor": .5, "cond_1": region_cond, "mask_1": region_mask}
        actual = self.extension.StrataAttentionCouple().patch(model, base_cond, base_mask, "region payload", **options)
        self.assertIs(actual, self.native_result)
        self.assertEqual(len(self.native_calls), 1)
        args, forwarded = self.native_calls[0]
        self.assertEqual(args, (model, base_cond, base_mask, "region payload"))
        self.assertEqual(forwarded, options)
        self.assertIs(forwarded["cond_1"], region_cond)
        self.assertIs(forwarded["mask_1"], region_mask)
        self.assertNotIn("AttentionCouplePPM", self.registry)

    def test_sdxl_delegates_to_native_unchanged(self):
        model, base_cond, base_mask = FakePatcher(SDXL()), object(), object()
        options = {"global_factor": .25, "cond_1": object(), "mask_1": object()}
        actual = self.extension.StrataAttentionCouple().patch(model, base_cond, base_mask, "[]", **options)
        self.assertIs(actual, self.native_result)
        self.assertEqual(self.native_calls, [((model, base_cond, base_mask, "[]"), options)])
        self.assertIs(self.native.patch, self.original_native_patch)

    def test_explicit_cosmos_retains_ppm_route_and_region_compaction(self):
        ppm_calls, ppm_result = [], (object(),)
        class PPM:
            @classmethod
            def execute(cls, **kwargs):
                ppm_calls.append(kwargs)
                return ppm_result
        self.registry["AttentionCouplePPM"] = PPM
        model, base_cond, base_mask = FakePatcher(CosmosPredict2()), object(), object()
        cond, mask = object(), object()
        actual = self.extension.StrataAttentionCouple().patch(
            model, base_cond, base_mask, "[]", global_factor=.5,
            cond_3=cond, mask_3=mask, cond_1=object())
        self.assertIs(actual, ppm_result)
        self.assertEqual(ppm_calls, [{"model": model, "base_cond": base_cond,
                                     "base_mask": base_mask, "cond_1": cond, "mask_1": mask}])
        self.assertEqual(self.native_calls, [])

    def test_cosmos_without_complete_region_pairs_returns_clone_without_ppm(self):
        model = FakePatcher(CosmosPredict2())
        actual = self.extension.StrataAttentionCouple().patch(model, object(), object(), "[]", cond_1=object())
        self.assertEqual(len(model.clones), 1)
        self.assertEqual(actual, (model.clones[0],))
        self.assertEqual(self.native_calls, [])
        self.assertNotIn("AttentionCouplePPM", self.registry)

    def test_unrelated_registrations_masks_alias_and_sam3_callback_are_preserved(self):
        self.assertIs(self.registry["UnrelatedNode"], self.other_node)
        self.assertIs(self.registry["SwarmAttentionCouple"], self.native)
        self.assertEqual(set(self.extension.NODE_CLASS_MAPPINGS), {"StrataBoundedImageUpscale", "StrataAttentionCouple"})
        self.assertEqual(self.extension.NODE_CLASS_MAPPINGS["StrataBoundedImageUpscale"].FUNCTION, "upscale")
        self.assertEqual(self.extension.NODE_DISPLAY_NAME_MAPPINGS["StrataBoundedImageUpscale"], "Strata bounded model upscale")
        data = types.SimpleNamespace(shape=(2, 3, 4))
        self.assertEqual(self.masks(data).shape, data.shape)
        self.assertIsNone(self.masks.shape.fset)
        self.assertIn(self.extension.install_sam3_normal_parameters, self.startup)
        self.run_startup()
        self.assertIs(self.registry["UnrelatedNode"], self.other_node)
        self.assertEqual(self.registry["SAM3Grounding"].__module__, "foreign_sam3")


if __name__ == "__main__":
    unittest.main(verbosity=2)
