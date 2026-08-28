import pathlib
import sys
import unittest


COMMON_NODE_DIRECTORY = pathlib.Path(__file__).resolve().parents[1]
sys.path.insert(0, str(COMMON_NODE_DIRECTORY))

from SwarmAnima38LoraMapping import (  # noqa: E402
    ANIMA_29_TO_38_INSERTIONS,
    BASE_TO_29_INSERTIONS,
    infer_source_block_count,
    map_block_index_to_anima38,
    map_lora_state_dict_to_anima38,
)


class Anima38LoraMappingTests(unittest.TestCase):
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


if __name__ == "__main__":
    unittest.main()
