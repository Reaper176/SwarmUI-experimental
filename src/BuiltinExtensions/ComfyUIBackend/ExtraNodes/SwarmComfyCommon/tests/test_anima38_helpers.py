import pathlib
import inspect
import json
import sys
import threading
import types
import unittest

import torch


COMMON_NODE_DIRECTORY = pathlib.Path(__file__).resolve().parents[1]
TEST_PACKAGE_NAME = "_swarm_comfy_common_anima38_tests"
test_package = types.ModuleType(TEST_PACKAGE_NAME)
test_package.__path__ = [str(COMMON_NODE_DIRECTORY)]
sys.modules[TEST_PACKAGE_NAME] = test_package

from _swarm_comfy_common_anima38_tests.SwarmAnima38 import (  # noqa: E402
    ADAPTER_ARCHITECTURE,
    AdapterInferenceLease,
    LastAdapterCache,
    SEMANTIC_LAYER_TAPS,
    SwarmAnima38Conditioning,
    THIRD_PARTY_LICENSE_NOTICE,
    _adapter_record_valid,
    _expanded_conditioning_metadata,
    _install_clip_reload_factory,
    _resolve_adapter_path,
    _mixed_semantic_source,
    _dispose_managed_adapter,
    _qwen35_4b_expected_shapes,
    _qwen_runtime_classes,
    _validate_qwen35_4b_state_dict,
    _validate_qwen35_quantized_weight,
    adapter_tag,
    discover_adapter_candidates,
    format_unified_prompt,
    is_qwen35_4b_candidate,
    install_anima38_model_detection,
    normalize_qwen35_state_dict,
    select_adapter_candidate,
    select_qwen35_candidate,
    truncate_semantic_token_pairs,
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

    def test_explicit_invalid_adapter_surfaces_precise_header_error(self):
        selection = "controlnet::broken.safetensors"
        cases = (
            (
                {"architecture": ADAPTER_ARCHITECTURE},
                {"timestep_gates.0": FakeTensor((1,))},
                "timestep_gates",
            ),
            (
                {"architecture": ADAPTER_ARCHITECTURE},
                {"anchor_deviation.weight": FakeTensor((1,))},
                "anchor_deviation",
            ),
            ({"architecture": "wrong"}, {}, "expected architecture"),
        )
        for metadata, shapes, message in cases:
            with self.subTest(message=message):
                def header_loader(root, name):
                    self.assertEqual(
                        (root, name),
                        ("controlnet", "broken.safetensors"),
                    )
                    return "/models/broken.safetensors", metadata, shapes

                with self.assertRaisesRegex(
                    ValueError,
                    rf"broken.safetensors.*{message}",
                ):
                    _resolve_adapter_path(selection, header_loader=header_loader)


class Anima38QwenStateTests(unittest.TestCase):
    def test_accepts_exact_426_tensor_companion_layout_after_projection_filter(self):
        companion = {
            key.removeprefix("model."): FakeTensor(shape)
            for key, shape in _qwen35_4b_expected_shapes().items()
            if key != "model.norm.weight"
            and not key.startswith("model.layers.31.mlp.")
            and key != "model.layers.31.post_attention_layernorm.weight"
        }
        for key in (
            "norm.0.weight",
            "norm.0.bias",
            "norm.1.weight",
            "norm.3.weight",
            "norm.3.bias",
        ):
            companion[key] = FakeTensor((1,))
        self.assertEqual(len(companion), 426)

        normalized, ignored = normalize_qwen35_state_dict(companion)
        self.assertEqual(len(ignored), 5)
        _validate_qwen35_4b_state_dict(normalized, "companion", True)

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

    @staticmethod
    def quant_metadata(quant_format, **extra):
        config = {"format": quant_format, **extra}
        return torch.tensor(list(json.dumps(config).encode("utf-8")), dtype=torch.uint8)

    def test_validates_packed_mxfp8_and_nvfp4_4b_weights(self):
        weight_key = "model.layers.0.mlp.down_proj.weight"
        logical_shape = (2560, 9216)
        prefix = "model.layers.0.mlp.down_proj."
        mxfp8 = {
            weight_key: FakeTensor((2560, 9216)),
            f"{prefix}comfy_quant": self.quant_metadata("mxfp8"),
            f"{prefix}weight_scale": torch.empty((2560, 288), device="meta"),
        }
        nvfp4 = {
            weight_key: FakeTensor((2560, 4608)),
            f"{prefix}comfy_quant": self.quant_metadata("nvfp4"),
            f"{prefix}weight_scale": torch.empty((2560, 576), device="meta"),
            f"{prefix}weight_scale_2": torch.empty((), device="meta"),
        }

        self.assertTrue(_validate_qwen35_quantized_weight(mxfp8, weight_key, logical_shape))
        self.assertTrue(_validate_qwen35_quantized_weight(nvfp4, weight_key, logical_shape))

    def test_rejects_malformed_packed_quant_metadata_storage_and_scales(self):
        weight_key = "model.layers.0.mlp.down_proj.weight"
        logical_shape = (2560, 9216)
        prefix = "model.layers.0.mlp.down_proj."
        cases = (
            (
                {
                    weight_key: FakeTensor((2560, 9216)),
                    f"{prefix}comfy_quant": torch.tensor([255], dtype=torch.uint8),
                },
                "valid JSON",
            ),
            (
                {
                    weight_key: FakeTensor((1, 1)),
                    f"{prefix}comfy_quant": self.quant_metadata("mxfp8"),
                    f"{prefix}weight_scale": torch.empty((2560, 288), device="meta"),
                },
                "storage shape",
            ),
            (
                {
                    weight_key: FakeTensor((2560, 4608)),
                    f"{prefix}comfy_quant": self.quant_metadata("nvfp4"),
                    f"{prefix}weight_scale": torch.empty((1, 1), device="meta"),
                    f"{prefix}weight_scale_2": torch.empty((), device="meta"),
                },
                "weight_scale.*required shape",
            ),
            (
                {
                    weight_key: FakeTensor((2560, 9216)),
                    f"{prefix}comfy_quant": self.quant_metadata("unknown"),
                },
                "unsupported.*unknown",
            ),
            (
                {
                    weight_key: FakeTensor((2560, 9216)),
                    f"{prefix}comfy_quant": self.quant_metadata("mxfp8"),
                    f"{prefix}weight_scale": torch.empty((2560, 288), device="meta"),
                    f"{prefix}weight_scale_2": torch.empty((), device="meta"),
                },
                "mixes mxfp8.*NVFP4",
            ),
        )
        for state, message in cases:
            with self.subTest(message=message):
                with self.assertRaisesRegex(ValueError, message):
                    if "mixes" in message:
                        complete = {
                            key: FakeTensor(shape)
                            for key, shape in _qwen35_4b_expected_shapes().items()
                        }
                        complete.update(state)
                        _validate_qwen35_4b_state_dict(complete, "mixed", False)
                    else:
                        _validate_qwen35_quantized_weight(
                            state,
                            weight_key,
                            logical_shape,
                        )


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

    def test_semantic_token_cap_preserves_boundary_and_truncates_overflow(self):
        boundary = [[(index, 1.0) for index in range(1024)]]
        overflow = [[(index, 1.0) for index in range(1030)]]
        self.assertEqual(truncate_semantic_token_pairs(boundary), boundary)
        truncated = truncate_semantic_token_pairs(overflow)
        self.assertEqual(len(truncated), 1)
        self.assertEqual(len(truncated[0]), 1024)
        self.assertEqual(truncated[0][0][0], 0)
        self.assertEqual(truncated[0][-1][0], 1023)
        attention_mask = [1] * len(truncated[0])
        self.assertEqual(len(attention_mask), len(truncated[0]))

    def test_fake_native_tokenizer_and_attention_mask_are_capped_together(self):
        fake_comfy = types.ModuleType("comfy")
        fake_comfy.__path__ = []
        fake_sd1_clip = types.ModuleType("comfy.sd1_clip")
        fake_qwen = types.ModuleType("comfy.text_encoders.qwen35")
        fake_text_encoders = types.ModuleType("comfy.text_encoders")
        fake_text_encoders.__path__ = []

        class FakeNativeTokenizer:
            def __init__(self, **kwargs):
                pass

            def tokenize_with_weights(self, text, return_word_ids=False, **kwargs):
                return [[(index, 1.0) for index in range(int(text))]]

        class FakeSD1ClipModel:
            def __init__(self, clip_model=None, **kwargs):
                self.qwen35_4b = clip_model.__new__(clip_model)

        fake_qwen.Qwen35Tokenizer = FakeNativeTokenizer
        fake_qwen.Qwen35ClipModel = object
        fake_qwen.Qwen35 = object
        fake_sd1_clip.SD1ClipModel = FakeSD1ClipModel
        fake_comfy.sd1_clip = fake_sd1_clip
        fake_comfy.text_encoders = fake_text_encoders
        modules = {
            "comfy": fake_comfy,
            "comfy.sd1_clip": fake_sd1_clip,
            "comfy.text_encoders": fake_text_encoders,
            "comfy.text_encoders.qwen35": fake_qwen,
        }
        saved = {name: sys.modules.get(name) for name in modules}
        sys.modules.update(modules)
        try:
            tokenizer_class, clip_class = _qwen_runtime_classes()
            tokens = tokenizer_class().tokenize_with_weights("1030")["qwen35_4b"]
            self.assertEqual(len(tokens[0]), 1024)

            clip_model = clip_class().qwen35_4b
            clip_model.process_tokens = lambda token_ids, device: (
                torch.empty((1, len(token_ids[0]), 1)),
                torch.ones((1, len(token_ids[0])), dtype=torch.bool),
                [len(token_ids[0])],
                {},
            )
            clip_model.transformer = lambda *args, **kwargs: (
                None,
                torch.empty((1, 4, len(tokens[0]), 1)),
            )
            states, attention_mask = clip_model.raw_hidden_states(tokens, "cpu")
            self.assertEqual(attention_mask.shape, (1, 1024))
            self.assertEqual([state.shape[1] for state in states], [1024] * 4)
        finally:
            for name, module in saved.items():
                if module is None:
                    del sys.modules[name]
                else:
                    sys.modules[name] = module

    def test_progressive_mixing_matches_softmax_weighted_sources(self):
        states = [torch.tensor([[[float(index)]]]) for index in range(4)]
        logits = torch.zeros((6, 4))
        mixed = _mixed_semantic_source(states, logits, 2)
        self.assertTrue(torch.equal(mixed, torch.tensor([[[1.5]]])))

    def test_expanded_metadata_is_copied_without_mutating_native(self):
        native = {
            "pooled_output": object(),
            "regional": "keep",
            "t5xxl_ids": [1],
            "t5xxl_weights": [1.0],
            "attention_mask": [1],
        }
        before = native.copy()
        expanded = _expanded_conditioning_metadata(
            native,
            "controlnet::adapter.safetensors",
            0.75,
            {"step": "42"},
        )
        self.assertEqual(native, before)
        self.assertIs(expanded["pooled_output"], native["pooled_output"])
        self.assertEqual(expanded["regional"], "keep")
        self.assertNotIn("t5xxl_ids", expanded)
        self.assertEqual(expanded["qwen35_expanded_step"], "42")


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
    def test_companion_mit_notice_retains_required_terms(self):
        self.assertIn("Copyright (c) 2026 GumGum10 contributors", THIRD_PARTY_LICENSE_NOTICE)
        self.assertIn("Permission is hereby granted, free of charge", THIRD_PARTY_LICENSE_NOTICE)
        self.assertIn('THE SOFTWARE IS PROVIDED "AS IS"', THIRD_PARTY_LICENSE_NOTICE)

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

    def test_dynamic_clip_reload_factory_returns_recreated_patcher(self):
        recreated_patcher = object()

        class FakeClip:
            def __init__(self):
                self.patcher = types.SimpleNamespace(cached_patcher_init=None)

        clip = FakeClip()

        def fake_loader(path, selected_name, embedding_directory=None, model_options=None, disable_dynamic=False):
            self.assertEqual(path, "/models/qwen.safetensors")
            self.assertEqual(selected_name, "qwen35_4b.safetensors")
            self.assertTrue(disable_dynamic)
            return types.SimpleNamespace(patcher=recreated_patcher)

        _install_clip_reload_factory(
            clip,
            "/models/qwen.safetensors",
            "qwen35_4b.safetensors",
            ["/embeddings"],
            {"load_device": "cpu"},
            loader=fake_loader,
        )
        factory, arguments = clip.patcher.cached_patcher_init
        self.assertTrue(callable(factory))
        self.assertIs(factory(*arguments, disable_dynamic=True), recreated_patcher)

    def test_detection_install_registry_survives_alternating_wrapper_order(self):
        fake_detection = types.ModuleType("comfy.model_detection")

        def base_detection(state_dict, key_prefix, metadata=None):
            return {"image_model": "other"}

        fake_detection.detect_unet_config = base_detection
        fake_comfy = types.ModuleType("comfy")
        fake_comfy.__path__ = []
        fake_comfy.model_detection = fake_detection
        original_comfy = sys.modules.get("comfy")
        original_detection = sys.modules.get("comfy.model_detection")
        sys.modules["comfy"] = fake_comfy
        sys.modules["comfy.model_detection"] = fake_detection
        try:
            install_anima38_model_detection()
            first_wrapper = fake_detection.detect_unet_config

            def other_wrapper(state_dict, key_prefix, metadata=None):
                return first_wrapper(state_dict, key_prefix, metadata=metadata)

            fake_detection.detect_unet_config = other_wrapper
            install_anima38_model_detection()
            self.assertIs(fake_detection.detect_unet_config, other_wrapper)
        finally:
            if original_comfy is None:
                del sys.modules["comfy"]
            else:
                sys.modules["comfy"] = original_comfy
            if original_detection is None:
                del sys.modules["comfy.model_detection"]
            else:
                sys.modules["comfy.model_detection"] = original_detection


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

    def test_inference_lease_blocks_replacement_until_first_use_exits(self):
        disposed = []
        cache = LastAdapterCache(lambda managed: disposed.append(managed))
        lease = AdapterInferenceLease(cache)
        first_entered = threading.Event()
        release_first = threading.Event()
        second_entered = threading.Event()
        first = object()
        second = object()

        def use_first():
            with lease.use("first", lambda: first):
                first_entered.set()
                release_first.wait(timeout=5)

        def replace_with_second():
            first_entered.wait(timeout=5)
            with lease.use("second", lambda: second):
                second_entered.set()

        first_thread = threading.Thread(target=use_first)
        second_thread = threading.Thread(target=replace_with_second)
        first_thread.start()
        second_thread.start()
        self.assertTrue(first_entered.wait(timeout=5))
        self.assertFalse(second_entered.wait(timeout=0.05))
        self.assertEqual(disposed, [])
        release_first.set()
        first_thread.join(timeout=5)
        second_thread.join(timeout=5)

        self.assertFalse(first_thread.is_alive())
        self.assertFalse(second_thread.is_alive())
        self.assertTrue(second_entered.is_set())
        self.assertEqual(disposed, [first])


if __name__ == "__main__":
    unittest.main()
