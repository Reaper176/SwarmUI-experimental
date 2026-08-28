import pathlib
import sys
import types
import unittest


COMMON_NODE_DIRECTORY = pathlib.Path(__file__).resolve().parents[1]
TEST_PACKAGE_NAME = "_swarm_comfy_common_anima38_tests"
test_package = types.ModuleType(TEST_PACKAGE_NAME)
test_package.__path__ = [str(COMMON_NODE_DIRECTORY)]
sys.modules[TEST_PACKAGE_NAME] = test_package

from _swarm_comfy_common_anima38_tests.SwarmAnima38 import (  # noqa: E402
    ADAPTER_ARCHITECTURE,
    SEMANTIC_LAYER_TAPS,
    adapter_tag,
    discover_adapter_candidates,
    format_unified_prompt,
    is_qwen35_4b_candidate,
    normalize_qwen35_state_dict,
    select_adapter_candidate,
    select_qwen35_candidate,
    validate_progressive_adapter_shapes,
)


class FakeTensor:
    def __init__(self, shape):
        self.shape = shape


class Anima38QwenCandidateTests(unittest.TestCase):
    def test_filters_supported_qwen35_4b_filename_markers(self):
        accepted = (
            "qwen35_4b.safetensors",
            "nested/Qwen3.5-4B-bf16.safetensors",
            "qwen3_5_4b_fp8.safetensors",
            "anima38B_base_txt.safetensors",
            "anima2BQwen354BText_base.safetensors",
        )
        for name in accepted:
            with self.subTest(name=name):
                self.assertTrue(is_qwen35_4b_candidate(name))

        rejected = (
            "qwen35_2b.safetensors",
            "anima-base.safetensors",
            "qwen35_4b.ckpt",
            "notes-qwen35_4b.txt",
        )
        for name in rejected:
            with self.subTest(name=name):
                self.assertFalse(is_qwen35_4b_candidate(name))

    def test_auto_prefers_anima38_then_canonical_then_lexical(self):
        candidates = [
            "z/qwen35_4b-alt.safetensors",
            "qwen35_4b.safetensors",
            "z/Anima38B_base_txt.safetensors",
            "a/anima38-custom.safetensors",
        ]
        self.assertEqual(
            select_qwen35_candidate(candidates, "auto"),
            "a/anima38-custom.safetensors",
        )
        self.assertEqual(
            select_qwen35_candidate(candidates[:2], "auto"),
            "qwen35_4b.safetensors",
        )
        self.assertEqual(
            select_qwen35_candidate(
                ["z/qwen3_5_4b.safetensors", "a/qwen3.5-4b.safetensors"],
                "auto",
            ),
            "a/qwen3.5-4b.safetensors",
        )

    def test_auto_excludes_noncandidates_and_reports_expected_markers(self):
        with self.assertRaisesRegex(ValueError, r"qwen35_4b.*anima38"):
            select_qwen35_candidate(
                ["qwen35_2b.safetensors", "anima-base.safetensors"],
                "auto",
            )


class Anima38AdapterDiscoveryTests(unittest.TestCase):
    def test_discovers_and_tags_valid_adapters_from_both_roots(self):
        records = [
            ("text_encoders", "nested/semantic.safetensors", {"architecture": ADAPTER_ARCHITECTURE}, []),
            ("controlnet", "anima38B_base.safetensors", {"architecture": ADAPTER_ARCHITECTURE}, []),
            ("controlnet", "wrong.safetensors", {"architecture": "other"}, []),
            ("text_encoders", "gated.safetensors", {"architecture": ADAPTER_ARCHITECTURE}, ["timestep_gates.0"]),
        ]
        self.assertEqual(
            discover_adapter_candidates(records),
            [
                "controlnet::anima38B_base.safetensors",
                "text_encoders::nested/semantic.safetensors",
            ],
        )

    def test_adapter_auto_prefers_anima38_then_stable_lexical(self):
        candidates = [
            adapter_tag("text_encoders", "z/semantic.safetensors"),
            adapter_tag("controlnet", "z/anima38B_base.safetensors"),
            adapter_tag("text_encoders", "a/anima38-expanded.safetensors"),
        ]
        self.assertEqual(
            select_adapter_candidate(candidates, "auto"),
            "text_encoders::a/anima38-expanded.safetensors",
        )
        self.assertEqual(
            select_adapter_candidate(candidates[:1], "auto"),
            "text_encoders::z/semantic.safetensors",
        )

    def test_adapter_selection_reports_missing_and_untagged_ambiguity(self):
        with self.assertRaisesRegex(ValueError, r"text_encoders.*controlnet.*architecture"):
            select_adapter_candidate([], "auto")
        with self.assertRaisesRegex(ValueError, r"ambiguous.*tagged"):
            select_adapter_candidate(
                [
                    "text_encoders::same.safetensors",
                    "controlnet::same.safetensors",
                ],
                "same.safetensors",
            )


class Anima38QwenStateTests(unittest.TestCase):
    def test_normalizes_companion_encoder_keys_and_only_ignores_projection_norm(self):
        embed = object()
        layer = object()
        model_norm = object()
        projection = object()
        unrelated = object()
        normalized, ignored = normalize_qwen35_state_dict(
            {
                "embed_tokens.weight": embed,
                "layers.0.input_layernorm.weight": layer,
                "model.norm.weight": model_norm,
                "norm.0.weight": projection,
                "other.weight": unrelated,
            }
        )
        self.assertIs(normalized["model.embed_tokens.weight"], embed)
        self.assertIs(normalized["model.layers.0.input_layernorm.weight"], layer)
        self.assertIs(normalized["model.norm.weight"], model_norm)
        self.assertIs(normalized["other.weight"], unrelated)
        self.assertNotIn("norm.0.weight", normalized)
        self.assertEqual(ignored, ("norm.0.weight",))


class Anima38PromptAndTapTests(unittest.TestCase):
    def test_unified_prompt_is_exact_raw_identity_for_tags_and_description(self):
        prompts = (
            "1girl, masterpiece, blue sky",
            "1girl, masterpiece\nDescription: A girl beneath a blue sky.",
            "  padded tags  \nDescription: padded prose  ",
            "   \n\t",
            "",
        )
        for prompt in prompts:
            with self.subTest(prompt=prompt):
                self.assertEqual(format_unified_prompt(prompt), prompt)

    def test_native_taps_match_companion_post_layers(self):
        self.assertEqual(SEMANTIC_LAYER_TAPS, (8, 16, 24, 32))
        self.assertEqual(tuple(index - 1 for index in SEMANTIC_LAYER_TAPS), (7, 15, 23, 31))


class Anima38AdapterShapeTests(unittest.TestCase):
    def test_accepts_complete_lightweight_progressive_state_shapes(self):
        state = {"layer_mix_logits": FakeTensor((6, 4))}
        for index in range(6):
            state[f"query_norms.{index}.weight"] = FakeTensor((1024,))
            state[f"source_norms.{index}.weight"] = FakeTensor((2560,))
            state[f"semantic_attentions.{index}.q_proj.weight"] = FakeTensor((1024, 1024))
            state[f"semantic_attentions.{index}.q_norm.weight"] = FakeTensor((64,))
            state[f"semantic_attentions.{index}.k_proj.weight"] = FakeTensor((1024, 2560))
            state[f"semantic_attentions.{index}.k_norm.weight"] = FakeTensor((64,))
            state[f"semantic_attentions.{index}.v_proj.weight"] = FakeTensor((1024, 2560))
            state[f"semantic_attentions.{index}.o_proj.weight"] = FakeTensor((1024, 1024))

        validate_progressive_adapter_shapes(state, "controlnet::valid.safetensors")

    def test_shape_validation_names_adapter_and_expected_shape(self):
        with self.assertRaisesRegex(ValueError, r"broken.safetensors.*layer_mix_logits.*\(6, 4\)"):
            validate_progressive_adapter_shapes(
                {"layer_mix_logits": FakeTensor((6, 5))},
                "controlnet::broken.safetensors",
            )


if __name__ == "__main__":
    unittest.main()
