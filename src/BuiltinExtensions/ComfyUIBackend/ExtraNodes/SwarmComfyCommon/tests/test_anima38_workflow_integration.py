import pathlib
import re
import unittest


REPOSITORY_ROOT = pathlib.Path(__file__).resolve().parents[6]
MODEL_SUPPORT_PATH = (
    REPOSITORY_ROOT
    / "src/BuiltinExtensions/ComfyUIBackend/WorkflowGeneratorModelSupport.cs"
)
WORKFLOW_PATH = (
    REPOSITORY_ROOT / "src/BuiltinExtensions/ComfyUIBackend/WorkflowGenerator.cs"
)
STEPS_PATH = (
    REPOSITORY_ROOT
    / "src/BuiltinExtensions/ComfyUIBackend/WorkflowGeneratorSteps.cs"
)
NODE_DATA_PATH = (
    REPOSITORY_ROOT / "src/BuiltinExtensions/ComfyUIBackend/WGNodeData.cs"
)
EXTENSION_PATH = (
    REPOSITORY_ROOT
    / "src/BuiltinExtensions/ComfyUIBackend/ComfyUIBackendExtension.cs"
)
CAPABILITY_PATH = (
    REPOSITORY_ROOT
    / "src/BuiltinExtensions/ComfyUIBackend/ComfyCapabilityCatalog.cs"
)


def method_body(source, signature):
    """Return the braced body following an exact method-signature fragment."""
    signature_start = source.index(signature)
    brace_start = source.index("{", signature_start)
    depth = 0
    for index in range(brace_start, len(source)):
        if source[index] == "{":
            depth += 1
        elif source[index] == "}":
            depth -= 1
            if depth == 0:
                return source[brace_start + 1 : index]
    raise AssertionError(f"Unclosed method body for {signature}")


class Anima38WorkflowIntegrationTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.model_support = MODEL_SUPPORT_PATH.read_text(encoding="utf-8")
        cls.workflow = WORKFLOW_PATH.read_text(encoding="utf-8")
        cls.steps = STEPS_PATH.read_text(encoding="utf-8")
        cls.node_data = NODE_DATA_PATH.read_text(encoding="utf-8")
        cls.extension = EXTENSION_PATH.read_text(encoding="utf-8")
        cls.capabilities = CAPABILITY_PATH.read_text(encoding="utf-8")

    def test_exact_subtype_helper_precedes_generic_anima_paths(self):
        self.assertRegex(
            self.model_support,
            r"public bool IsAnima38\(\)\s*=>\s*CurrentModelClass\(\)\?\.ID == \"anima-3_8b\";",
        )
        self.assertRegex(
            self.model_support,
            r"public bool IsAnima\(\)\s*=>\s*IsModelCompatClass\(T2IModelClassSorter\.CompatAnima\);",
        )

        loader = method_body(self.model_support, "CreateModelLoader(T2IModel model")
        self.assertLess(loader.index("if (IsAnima38())"), loader.index("else if (IsAnima())"))

        sampler = method_body(self.workflow, "CreateKSampler(JArray model")
        self.assertLess(sampler.index("else if (IsAnima38())"), sampler.index("else if (IsAnima())"))

    def test_model_loader_keeps_native_clip_and_caches_one_semantic_clip_per_loader_identity(self):
        loader = method_body(self.model_support, "CreateModelLoader(T2IModel model")
        exact_start = loader.index("if (IsAnima38())")
        generic_start = loader.index("else if (IsAnima())", exact_start)
        exact_branch = loader[exact_start:generic_start]

        self.assertIn('helpers.LoadClip("stable_diffusion", helpers.GetQwen3_600mModel());', exact_branch)
        self.assertIn("ComfyNodeNames.LoadAnima38Qwen35", exact_branch)
        self.assertIn("ComfyNodeInputNames.LoadAnima38Qwen35.QwenFilename", exact_branch)
        self.assertIn("ComfyUIBackendExtension.Anima38Qwen35Encoder", exact_branch)
        self.assertIn("Anima38SemanticClips[helper]", exact_branch)
        self.assertNotIn("LoadingClip = [semantic", exact_branch)
        self.assertEqual(
            exact_branch.count("CreateNode(ComfyNodeNames.LoadAnima38Qwen35"), 1
        )

        generic_end = loader.index("else if", generic_start + len("else if"))
        generic_branch = loader[generic_start:generic_end]
        self.assertNotIn("LoadAnima38Qwen35", generic_branch)
        self.assertNotIn("Anima38Conditioning", generic_branch)

        cache_hit_start = loader.index("if (NodeHelpers.TryGetValue(helper")
        cache_hit_end = loader.index("IsDifferentialDiffusion = false", cache_hit_start)
        self.assertIn("Anima38SemanticClips", loader[cache_hit_start:cache_hit_end])

    def test_direct_conditioning_wires_current_model_both_clips_raw_prompt_and_expanded_output(self):
        conditioning = method_body(self.workflow, "CreateConditioningDirect(string prompt")
        exact_start = conditioning.index("if (IsAnima38())")
        next_branch = conditioning.index("else if", exact_start)
        exact_branch = conditioning[exact_start:next_branch]

        expected_inputs = {
            "SourceModel": "CurrentModel.Path",
            "CLIP": "clip",
            "Qwen35CLIP": "semanticClip",
            "Adapter": "UserInput.Get(ComfyUIBackendExtension.Anima38Adapter)",
            "Prompt": "prompt",
            "AdapterStrength": "UserInput.Get(ComfyUIBackendExtension.Anima38AdapterStrength)",
        }
        self.assertIn("ComfyNodeNames.Anima38Conditioning", exact_branch)
        for input_name, value in expected_inputs.items():
            with self.subTest(input_name=input_name):
                self.assertIn(
                    f"[ComfyNodeInputNames.Anima38Conditioning.{input_name}] = {value}",
                    exact_branch,
                )

        multiplier = conditioning.index("ConditioningMultiply", exact_start)
        self.assertLess(next_branch, multiplier)
        self.assertIn("return [node, 0];", conditioning[multiplier:])
        self.assertNotIn("return [node, 1]", conditioning)

        self.assertIn(
            "g.FinalPrompt = g.CreateConditioning(", self.steps
        )
        self.assertIn(
            "g.FinalNegativePrompt = g.CreateConditioning(", self.steps
        )

    def test_break_segments_and_regions_remain_downstream_of_direct_conditioning(self):
        line = method_body(self.workflow, "CreateConditioningLine(string prompt")
        self.assertIn('prompt.Split("<break>"', line)
        self.assertEqual(line.count("CreateConditioningDirect(breaks["), 2)
        self.assertIn('CreateNode("ConditioningConcat"', line)
        self.assertNotIn("ConditioningMultiply", line)
        direct = method_body(self.workflow, "CreateConditioningDirect(string prompt")
        self.assertEqual(direct.count('CreateNode("ConditioningMultiply"'), 1)
        variation_return = line.index('Features.Contains("variation_seed")')
        split = line.index('prompt.Split("<break>"')
        self.assertLess(split, variation_return)
        self.assertRegex(
            line[variation_return:],
            r'Features\.Contains\("variation_seed"\)\s*&&\s*!IsAnima38\(\)',
        )

        create_conditioning = method_body(self.workflow, "CreateConditioning(string prompt")
        self.assertIn("CreateConditioningLine(part.Prompt", create_conditioning)
        self.assertIn('CreateNode("ConditioningSetMask"', create_conditioning)
        self.assertIn("CreateAttentionCouplePlan", create_conditioning)

    def test_semantic_clip_is_owned_by_model_node_data_across_cache_and_path_copies(self):
        self.assertRegex(
            self.node_data,
            r"public JArray Anima38SemanticClip \{ get; \} = _anima38SemanticClip;",
        )
        self.assertIn("JArray _anima38SemanticClip = null", self.node_data)
        self.assertIn("MemberwiseClone()", self.node_data)

        loader = method_body(self.model_support, "CreateModelLoader(T2IModel model")
        cache_hit_start = loader.index("if (NodeHelpers.TryGetValue(helper")
        cache_hit_end = loader.index("IsDifferentialDiffusion = false", cache_hit_start)
        cache_hit = loader[cache_hit_start:cache_hit_end]
        self.assertIn("Anima38SemanticClips.TryGetValue(helper", cache_hit)
        self.assertRegex(
            cache_hit,
            r"new\(LoadingModel, this, WGNodeData\.DT_MODEL, CurrentCompat\(\), semanticClip\)",
        )

        final_model_start = loader.rindex("Anima38SemanticClips.TryGetValue(helper")
        final_model = loader[final_model_start:]
        self.assertIn("Anima38SemanticClips.TryGetValue(helper", final_model)
        self.assertRegex(
            final_model,
            r"new\(LoadingModel, this, WGNodeData\.DT_MODEL, CurrentCompat\(\), finalSemanticClip\)",
        )

        conditioning = method_body(self.workflow, "CreateConditioningDirect(string prompt")
        exact_start = conditioning.index("if (IsAnima38())")
        next_branch = conditioning.index("else if", exact_start)
        exact_branch = conditioning[exact_start:next_branch]
        self.assertIn("CurrentModel.Anima38SemanticClip", exact_branch)
        self.assertNotIn("LoadingModelType", exact_branch)
        self.assertNotIn("GetAnima38SemanticClip", exact_branch)

        segment = method_body(self.steps, "void RunSegmentationProcessing(WorkflowGenerator g")
        self.assertIn("(t2iModel, model, clip, vae) = g.CreateModelLoader", segment)
        self.assertIn("g.CurrentModel = model;", segment)
        self.assertIn("model = model.WithPath(newModel);", segment)
        path_copy = segment.index("model = model.WithPath(newModel);")
        self.assertIn("g.CurrentModel = model;", segment[path_copy:])

        refiner_start = self.steps.index("g.FinalLoadedModel = refineModel;")
        refiner_end = self.steps.index("g.NoVAEOverride = false;", refiner_start)
        self.assertIn("g.CurrentModel", self.steps[refiner_start:refiner_end])

        sampler = method_body(self.workflow, "CreateKSampler(JArray model")
        negative_start = sampler.index("T2IParamTypes.NegativeModel")
        negative_end = sampler.index("if (IsVideoModel())", negative_start)
        negative_load = sampler[negative_start:negative_end]
        self.assertIn("T2IModel priorLoadedModel = FinalLoadedModel", negative_load)
        self.assertIn("FinalLoadedModel = negModel", negative_load)
        self.assertIn("finally", negative_load)
        self.assertIn("FinalLoadedModel = priorLoadedModel", negative_load)

    def test_exact_defaults_are_res_multistep_beta_without_overriding_explicit_values(self):
        sampler = method_body(self.workflow, "CreateKSampler(JArray model")
        exact_start = sampler.index("else if (IsAnima38())")
        generic_start = sampler.index("else if (IsAnima())", exact_start)
        exact_branch = sampler[exact_start:generic_start]
        generic_end = sampler.index("else if", generic_start + len("else if"))
        generic_branch = sampler[generic_start:generic_end]

        self.assertIn('defsampler ??= "res_multistep";', exact_branch)
        self.assertIn('defscheduler ??= "beta";', exact_branch)
        self.assertIn('defsampler ??= "er_sde";', generic_branch)
        self.assertIn('defscheduler ??= "simple";', generic_branch)
        self.assertRegex(sampler, r"explicitSampler\s*\?\?")
        self.assertRegex(sampler, r"explicitScheduler\s*\?\?")

    def test_exact_subtype_requires_both_runtime_nodes_before_backend_selection(self):
        for node_name, feature_name in (
            ("LoadAnima38Qwen35", "Anima38Qwen35NodeFeature"),
            ("Anima38Conditioning", "Anima38ConditioningNodeFeature"),
        ):
            with self.subTest(node_name=node_name):
                self.assertIn(f"public const string {feature_name}", self.capabilities)
                self.assertIn(
                    f"[ComfyNodeNames.{node_name}] = {feature_name}", self.capabilities
                )

        routing = method_body(self.extension, "RecomputeBackendRoutingRequirements(T2IParamInput input)")
        expected_roles = (
            "Model",
            "RefinerModel",
            "SegmentModel",
            "NegativeModel",
            "VideoModel",
            "VideoSwapModel",
            "VideoExtendModel",
            "VideoExtendSwapModel",
        )
        for role in expected_roles:
            with self.subTest(role=role):
                self.assertIn(f"hasAnima38(input, T2IParamTypes.{role})", routing)
        self.assertIn('ModelClass?.ID == "anima-3_8b"', routing)
        self.assertIn("input.SectionParamOverrides.Values", routing)
        self.assertIn("section.TryGet(T2IParamTypes.NegativeModel", routing)
        self.assertIn("RequiredFlags.Add(ComfyCapabilityCatalog.Anima38Qwen35NodeFeature)", routing)
        self.assertIn("RequiredFlags.Add(ComfyCapabilityCatalog.Anima38ConditioningNodeFeature)", routing)
        self.assertNotIn("CompatAnima", routing)


if __name__ == "__main__":
    unittest.main()
