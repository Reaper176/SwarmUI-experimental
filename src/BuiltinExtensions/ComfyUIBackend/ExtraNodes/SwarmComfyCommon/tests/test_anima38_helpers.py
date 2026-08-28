import pathlib
import inspect
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
    LastAdapterCache,
    SEMANTIC_LAYER_TAPS,
    SwarmAnima38Conditioning,
    _adapter_record_valid,
    _dispose_managed_adapter,
    _qwen35_4b_expected_shapes,
    _validate_qwen35_4b_state_dict,
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


def valid_adapter_state():
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
    return state


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

    def test_rejects_companion_gate_and_anchor_adapter_keys(self):
        metadata = {"architecture": ADAPTER_ARCHITECTURE}
        self.assertFalse(_adapter_record_valid(metadata, ["timestep_gates.0"]))
        self.assertFalse(_adapter_record_valid(metadata, ["anchor_deviation"]))
        self.assertFalse(_adapter_record_valid(metadata, ["anchor_deviation.weight"]))
        self.assertTrue(_adapter_record_valid(metadata, ["layer_mix_logits"]))

    def test_rejects_missing_or_wrong_adapter_architecture_metadata(self):
        self.assertFalse(_adapter_record_valid({}, []))
        self.assertFalse(_adapter_record_valid({"architecture": "other"}, []))


class Anima38QwenStateTests(unittest.TestCase):
    def test_normalizes_companion_encoder_keys_and_ignores_exact_projection_norm_keys(self):
        embed = object()
        layer = object()
        model_norm = object()
        projection_keys = (
            "norm.0.weight",
            "norm.0.bias",
            "norm.1.weight",
            "norm.3.weight",
            "norm.3.bias",
        )
        unrelated = object()
        source = {
            "embed_tokens.weight": embed,
            "layers.0.input_layernorm.weight": layer,
            "model.norm.weight": model_norm,
            "other.weight": unrelated,
        }
        for key in projection_keys:
            source[key] = object()
        normalized, ignored = normalize_qwen35_state_dict(
            source
        )
        self.assertIs(normalized["model.embed_tokens.weight"], embed)
        self.assertIs(normalized["model.layers.0.input_layernorm.weight"], layer)
        self.assertIs(normalized["model.norm.weight"], model_norm)
        self.assertIs(normalized["other.weight"], unrelated)
        for key in projection_keys:
            self.assertNotIn(key, normalized)
        self.assertEqual(ignored, tuple(sorted(projection_keys)))

    def test_unknown_root_norm_survives_for_strict_validation(self):
        value = object()
        normalized, ignored = normalize_qwen35_state_dict(
            {"norm.unrelated.weight": value}
        )
        self.assertIs(normalized["norm.unrelated.weight"], value)
        self.assertEqual(ignored, ())

        complete = {
            key: FakeTensor(shape)
            for key, shape in _qwen35_4b_expected_shapes().items()
        }
        complete.update(normalized)
        with self.assertRaisesRegex(ValueError, r"unexpected.*norm.unrelated.weight"):
            _validate_qwen35_4b_state_dict(complete, "unknown-norm", False)


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
        validate_progressive_adapter_shapes(
            valid_adapter_state(), "controlnet::valid.safetensors"
        )

    def test_shape_validation_names_adapter_and_expected_shape(self):
        with self.assertRaisesRegex(ValueError, r"broken.safetensors.*layer_mix_logits.*\(6, 4\)"):
            validate_progressive_adapter_shapes(
                {"layer_mix_logits": FakeTensor((6, 5))},
                "controlnet::broken.safetensors",
            )

    def test_reports_missing_state_key_separately(self):
        state = valid_adapter_state()
        del state["query_norms.3.weight"]
        with self.assertRaisesRegex(ValueError, r"missing=.*query_norms.3.weight"):
            validate_progressive_adapter_shapes(state, "missing.safetensors")

    def test_reports_unexpected_state_key_separately(self):
        state = valid_adapter_state()
        state["unexpected.weight"] = FakeTensor((1,))
        with self.assertRaisesRegex(ValueError, r"unexpected=.*unexpected.weight"):
            validate_progressive_adapter_shapes(state, "unexpected.safetensors")


class Anima38NodeContractTests(unittest.TestCase):
    def test_conditioning_contract_uses_adapter_name(self):
        parameters = inspect.signature(SwarmAnima38Conditioning.encode).parameters
        self.assertIn("adapter_name", parameters)
        self.assertNotIn("adapter", parameters)

        module = sys.modules[SwarmAnima38Conditioning.__module__]
        original = module._adapter_choices
        module._adapter_choices = lambda: ["auto"]
        try:
            required = SwarmAnima38Conditioning.INPUT_TYPES()["required"]
        finally:
            module._adapter_choices = original
        self.assertIn("adapter_name", required)
        self.assertNotIn("adapter", required)


class Anima38AdapterCacheTests(unittest.TestCase):
    def test_cache_reuses_matching_key_and_replaces_previous_entry(self):
        disposed = []
        cache = LastAdapterCache(lambda managed: disposed.append(managed))
        first = object()
        second = object()

        cache.store("first", first)
        self.assertEqual(disposed, [])
        self.assertIs(cache.get("first"), first)
        self.assertIsNone(cache.get("other"))
        cache.store("first", first)
        self.assertEqual(disposed, [])
        cache.store("second", second)

        self.assertEqual(disposed, [first])
        self.assertIsNone(cache.get("first"))
        self.assertIs(cache.get("second"), second)

    def test_cache_entry_contains_only_managed_semantic_adapter(self):
        class SemanticOnly:
            def modules(self):
                return (self,)

        native_adapter = object()
        semantic = SemanticOnly()
        cache = LastAdapterCache()
        cache.store("adapter", semantic)

        self.assertNotIn(native_adapter, tuple(cache.get("adapter").modules()))

    def test_production_disposer_unloads_adapter_on_all_devices(self):
        calls = []
        fake_model_management = types.ModuleType("comfy.model_management")
        fake_model_management.unload_model_and_clones = (
            lambda managed, all_devices=False: calls.append((managed, all_devices))
        )
        fake_comfy = types.ModuleType("comfy")
        fake_comfy.__path__ = []
        fake_comfy.model_management = fake_model_management
        original_comfy = sys.modules.get("comfy")
        original_management = sys.modules.get("comfy.model_management")
        sys.modules["comfy"] = fake_comfy
        sys.modules["comfy.model_management"] = fake_model_management
        managed = object()
        try:
            _dispose_managed_adapter(managed)
        finally:
            if original_comfy is None:
                del sys.modules["comfy"]
            else:
                sys.modules["comfy"] = original_comfy
            if original_management is None:
                del sys.modules["comfy.model_management"]
            else:
                sys.modules["comfy.model_management"] = original_management

        self.assertEqual(calls, [(managed, True)])


if __name__ == "__main__":
    unittest.main()
