import pathlib
import sys
import types
import unittest


COMMON_NODE_DIRECTORY = pathlib.Path(__file__).resolve().parents[1]
MAPPING_SOURCE_PATH = COMMON_NODE_DIRECTORY / "SwarmAnima38LoraMapping.py"
TEST_PACKAGE_NAME = "_swarm_comfy_common_tests"
test_package = types.ModuleType(TEST_PACKAGE_NAME)
test_package.__path__ = [str(COMMON_NODE_DIRECTORY)]
sys.modules[TEST_PACKAGE_NAME] = test_package

from _swarm_comfy_common_tests.SwarmAnima38LoraMapping import (  # noqa: E402
    ANIMA_29_TO_38_INSERTIONS,
    BASE_TO_29_INSERTIONS,
    infer_source_block_count,
    map_block_index_to_anima38,
    map_lora_state_dict_to_anima38,
)
from _swarm_comfy_common_tests.SwarmAnima38Lora import (  # noqa: E402
    get_cached_anima38_lora,
    prepare_anima38_lora,
)


class Anima38LoraMappingTests(unittest.TestCase):
    def test_adapted_mapping_data_retains_upstream_mit_attribution(self):
        source = MAPPING_SOURCE_PATH.read_text(encoding="utf-8")

        self.assertIn("MIT License", source)
        self.assertIn("Copyright (c) 2026 Lakeside529", source)
        self.assertIn("Permission is hereby granted, free of charge", source)
        self.assertIn("THE SOFTWARE IS PROVIDED \"AS IS\"", source)

    def test_insertion_schedules_match_the_legacy_bridge(self):
        self.assertEqual(BASE_TO_29_INSERTIONS, (2, 5, 8, 11, 14, 17, 21, 24, 27, 30, 33, 36))
        self.assertEqual(ANIMA_29_TO_38_INSERTIONS, (3, 7, 11, 15, 19, 23, 27, 31, 35, 39, 43, 47))

    def test_base_block_mapping_uses_both_insertion_schedules(self):
        expected_indices = {
            0: 0,
            1: 1,
            2: 4,
            3: 5,
            4: 8,
            5: 9,
            6: 12,
            27: 51,
        }

        for source_index, expected_index in expected_indices.items():
            with self.subTest(source_index=source_index):
                self.assertEqual(map_block_index_to_anima38(source_index, 28), expected_index)

    def test_anima29_block_mapping_uses_second_insertion_schedule(self):
        expected_indices = {
            0: 0,
            2: 2,
            3: 4,
            6: 8,
            7: 9,
            10: 13,
            39: 51,
        }

        for source_index, expected_index in expected_indices.items():
            with self.subTest(source_index=source_index):
                self.assertEqual(map_block_index_to_anima38(source_index, 40), expected_index)

    def test_native_block_mapping_is_identity(self):
        for source_index in (0, 3, 27, 39, 40, 51):
            with self.subTest(source_index=source_index):
                self.assertEqual(map_block_index_to_anima38(source_index, 52), source_index)

    def test_infers_source_depth_from_generic_and_kohya_keys(self):
        self.assertEqual(infer_source_block_count({"diffusion_model.blocks.27.attn.weight": object()}), 28)
        self.assertEqual(infer_source_block_count({"prefix_lora_unet_blocks_39_to_q.lora_up.weight": object()}), 40)
        self.assertEqual(
            infer_source_block_count(
                {
                    "diffusion_model.blocks.8.attn.weight": object(),
                    "prefix_lora_unet_blocks_51_to_q.lora_up.weight": object(),
                }
            ),
            52,
        )

    def test_rewrites_generic_block_token_anywhere_in_key(self):
        value = object()
        source = {"adapter.diffusion_model.blocks.2.attn.to_q.weight": value}

        mapped = map_lora_state_dict_to_anima38(source)

        self.assertEqual(list(mapped), ["adapter.diffusion_model.blocks.4.attn.to_q.weight"])
        self.assertIs(mapped["adapter.diffusion_model.blocks.4.attn.to_q.weight"], value)

    def test_rewrites_kohya_block_token_anywhere_in_key(self):
        value = object()
        source = {
            "network.prefix_lora_unet_blocks_4_transformer_blocks_0_attn_to_q.lora_up.weight": value,
            "network.prefix_lora_unet_blocks_27_transformer_blocks_0_attn_to_q.lora_down.weight": object(),
        }

        mapped = map_lora_state_dict_to_anima38(source)

        expected_key = "network.prefix_lora_unet_blocks_8_transformer_blocks_0_attn_to_q.lora_up.weight"
        self.assertIn(expected_key, mapped)
        self.assertIs(mapped[expected_key], value)

    def test_legacy_mapping_omits_llm_adapter_tensors(self):
        block_value = object()
        source = {
            "diffusion_model.blocks.27.attn.weight": block_value,
            "diffusion_model.llm_adapter.source_proj.weight": object(),
            "prefix.diffusion_model.llm_adapter.blocks.0.cross_attn.k_proj.weight": object(),
        }

        mapped = map_lora_state_dict_to_anima38(source)

        self.assertEqual(mapped, {"diffusion_model.blocks.51.attn.weight": block_value})

    def test_legacy_mapping_omits_kohya_llm_adapter_tensors(self):
        block_value = object()
        source = {
            "lora_unet_blocks_27_attn_to_q.lora_up.weight": block_value,
            "lora_unet_llm_adapter_blocks_5_self_attn_v_proj.lora_up.weight": object(),
        }

        mapped = map_lora_state_dict_to_anima38(source)

        self.assertEqual(mapped, {"lora_unet_blocks_51_attn_to_q.lora_up.weight": block_value})

    def test_native_mapping_preserves_llm_adapter_tensors(self):
        adapter_value = object()
        source = {
            "diffusion_model.blocks.51.attn.weight": object(),
            "diffusion_model.llm_adapter.source_proj.weight": adapter_value,
        }

        mapped = map_lora_state_dict_to_anima38(source)

        self.assertIs(mapped["diffusion_model.llm_adapter.source_proj.weight"], adapter_value)

    def test_unrelated_keys_and_values_are_preserved(self):
        unrelated_value = object()
        source = {
            "diffusion_model.blocks.39.attn.weight": object(),
            "text_encoder.layer.0.weight": unrelated_value,
        }

        mapped = map_lora_state_dict_to_anima38(source)

        self.assertIn("text_encoder.layer.0.weight", mapped)
        self.assertIs(mapped["text_encoder.layer.0.weight"], unrelated_value)

    def test_near_match_block_keys_remain_unrelated(self):
        generic_value = object()
        kohya_value = object()
        source = {
            "diffusion_model.blocks.27.attn.weight": object(),
            "diffusion_model.blocks.27extra.weight": generic_value,
            "lora_unet_blocks_27extra": kohya_value,
        }

        mapped = map_lora_state_dict_to_anima38(source)

        self.assertIs(mapped["diffusion_model.blocks.27extra.weight"], generic_value)
        self.assertIs(mapped["lora_unet_blocks_27extra"], kohya_value)

    def test_rejects_state_dict_with_only_near_match_block_keys(self):
        source = {
            "diffusion_model.blocks.27extra.weight": object(),
            "lora_unet_blocks_27extra": object(),
        }

        with self.assertRaisesRegex(ValueError, "recognizable.*block"):
            map_lora_state_dict_to_anima38(source)

    def test_rejects_block_indices_beyond_native_depth(self):
        with self.assertRaisesRegex(ValueError, "52"):
            infer_source_block_count({"diffusion_model.blocks.52.attn.weight": object()})

    def test_rejects_state_dict_without_recognizable_block_keys(self):
        with self.assertRaisesRegex(ValueError, "recognizable.*block"):
            map_lora_state_dict_to_anima38({"text_encoder.layer.0.weight": object()})


class Anima38LoraLoadingDecisionTests(unittest.TestCase):
    def test_native_lora_bypasses_remapping_and_retains_state_dict(self):
        state_dict = {"diffusion_model.blocks.51.attn.weight": object()}

        prepared_state_dict, source_block_count, was_remapped = prepare_anima38_lora(state_dict)

        self.assertIs(prepared_state_dict, state_dict)
        self.assertEqual(source_block_count, 52)
        self.assertFalse(was_remapped)

    def test_legacy_loras_are_remapped(self):
        for source_block_count in (28, 40):
            with self.subTest(source_block_count=source_block_count):
                state_dict = {
                    f"diffusion_model.blocks.{source_block_count - 1}.attn.weight": object()
                }

                prepared_state_dict, reported_source_block_count, was_remapped = prepare_anima38_lora(state_dict)

                self.assertIsNot(prepared_state_dict, state_dict)
                self.assertEqual(reported_source_block_count, source_block_count)
                self.assertTrue(was_remapped)
                self.assertIn("diffusion_model.blocks.51.attn.weight", prepared_state_dict)

    def test_shared_preparation_reports_source_depth_and_remap_decision(self):
        prepared_state_dict, source_block_count, was_remapped = prepare_anima38_lora(
            {"lora_unet_blocks_27_attn_to_q.lora_up.weight": object()}
        )

        self.assertEqual(source_block_count, 28)
        self.assertTrue(was_remapped)
        self.assertIn("lora_unet_blocks_51_attn_to_q.lora_up.weight", prepared_state_dict)

    def test_shared_preparation_explains_supported_layouts_and_key_forms(self):
        with self.assertRaisesRegex(
            ValueError,
            r"28-, 40-, or 52-block.*diffusion_model\.blocks\.N.*lora_unet_blocks_N",
        ):
            prepare_anima38_lora({"text_encoder.layer.0.weight": object()})

    def test_cache_lookup_rejects_a_different_resolved_filename(self):
        state_dict = {"diffusion_model.blocks.51.attn.weight": object()}
        loaded_lora = ("/models/first.safetensors", state_dict, None, 52, False)

        cached = get_cached_anima38_lora(loaded_lora, "/models/second.safetensors")

        self.assertIsNone(cached)

    def test_cache_lookup_returns_prepared_data_for_matching_resolved_filename(self):
        state_dict = {"diffusion_model.blocks.51.attn.weight": object()}
        loaded_lora = ("/models/native.safetensors", state_dict, "metadata", 52, False)

        cached = get_cached_anima38_lora(loaded_lora, "/models/native.safetensors")

        self.assertEqual(cached, (state_dict, "metadata", 52, False))


if __name__ == "__main__":
    unittest.main()
