import pathlib
import shutil
import subprocess
import tempfile
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
REQUIREMENT_HARNESS_PATH = pathlib.Path(__file__).with_name(
    "Anima38LoraRequirementHarness.cs.txt"
)
NODE_DATA_CONSTRUCTOR_HARNESS_PATH = pathlib.Path(__file__).with_name(
    "WGNodeDataConstructorHarness.cs.txt"
)
AUTO_CAPABILITY_HARNESS_PATH = pathlib.Path(__file__).with_name(
    "Anima38AutoCapabilityHarness.cs.txt"
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

    def test_wg_node_data_preserves_four_argument_constructor_abi(self):
        output_dir = REPOSITORY_ROOT / "src/bin/Debug/net8.0"
        swarm_dll = output_dir / "SwarmUI.dll"
        newtonsoft_dll = output_dir / "Newtonsoft.Json.dll"
        self.assertTrue(swarm_dll.exists(), "Build SwarmUI before running the constructor ABI harness")
        self.assertTrue(newtonsoft_dll.exists(), "Build SwarmUI before running the constructor ABI harness")
        sdk_root = pathlib.Path("/usr/share/dotnet/sdk")
        compiler = sorted(sdk_root.glob("*/Roslyn/bincore/csc.dll"))[-1]
        reference_roots = [
            pathlib.Path("/usr/share/dotnet/packs/Microsoft.NETCore.App.Ref"),
            pathlib.Path.home() / ".nuget/packages/microsoft.netcore.app.ref",
        ]
        reference_dirs = [
            path for root in reference_roots for path in root.glob("*/ref/net8.0")
        ]
        reference_dir = sorted(reference_dirs)[-1]
        with tempfile.TemporaryDirectory(prefix="anima38-node-data-abi-") as temp_raw:
            temp_dir = pathlib.Path(temp_raw)
            harness_dll = temp_dir / "WGNodeDataConstructorHarness.dll"
            compile_command = [
                "dotnet",
                str(compiler),
                "-noconfig",
                "-nostdlib",
                "-langversion:latest",
                "-target:exe",
                f"-out:{harness_dll}",
                *[f"-r:{path}" for path in sorted(reference_dir.glob("*.dll"))],
                f"-r:{swarm_dll}",
                f"-r:{newtonsoft_dll}",
                str(NODE_DATA_CONSTRUCTOR_HARNESS_PATH),
            ]
            compiled = subprocess.run(compile_command, capture_output=True, text=True)
            self.assertEqual(compiled.returncode, 0, compiled.stdout + compiled.stderr)
            for source in output_dir.glob("*.dll"):
                shutil.copy2(source, temp_dir / source.name)
            executed = subprocess.run(
                [
                    "dotnet",
                    "exec",
                    "--runtimeconfig",
                    str(output_dir / "SwarmUI.runtimeconfig.json"),
                    "--depsfile",
                    str(output_dir / "SwarmUI.deps.json"),
                    str(harness_dll),
                ],
                cwd=temp_dir,
                capture_output=True,
                text=True,
            )
            self.assertEqual(executed.returncode, 0, executed.stdout + executed.stderr)

    def test_owner_local_auto_asset_capabilities_and_request_routing(self):
        output_dir = REPOSITORY_ROOT / "src/bin/Debug/net8.0"
        swarm_dll = output_dir / "SwarmUI.dll"
        self.assertTrue(swarm_dll.exists(), "Build SwarmUI before running the auto capability harness")
        sdk_root = pathlib.Path("/usr/share/dotnet/sdk")
        compiler = sorted(sdk_root.glob("*/Roslyn/bincore/csc.dll"))[-1]
        reference_roots = [
            pathlib.Path("/usr/share/dotnet/packs/Microsoft.NETCore.App.Ref"),
            pathlib.Path.home() / ".nuget/packages/microsoft.netcore.app.ref",
        ]
        reference_dirs = [
            path for root in reference_roots for path in root.glob("*/ref/net8.0")
        ]
        reference_dir = sorted(reference_dirs)[-1]
        with tempfile.TemporaryDirectory(prefix="anima38-auto-capability-") as temp_raw:
            temp_dir = pathlib.Path(temp_raw)
            harness_dll = temp_dir / "Anima38AutoCapabilityHarness.dll"
            compile_command = [
                "dotnet",
                str(compiler),
                "-noconfig",
                "-nostdlib",
                "-langversion:latest",
                "-target:exe",
                f"-out:{harness_dll}",
                *[f"-r:{path}" for path in sorted(reference_dir.glob("*.dll"))],
                f"-r:{swarm_dll}",
                str(AUTO_CAPABILITY_HARNESS_PATH),
            ]
            compiled = subprocess.run(compile_command, capture_output=True, text=True)
            self.assertEqual(compiled.returncode, 0, compiled.stdout + compiled.stderr)
            for source in output_dir.glob("*.dll"):
                shutil.copy2(source, temp_dir / source.name)
            executed = subprocess.run(
                [
                    "dotnet",
                    "exec",
                    "--runtimeconfig",
                    str(output_dir / "SwarmUI.runtimeconfig.json"),
                    "--depsfile",
                    str(output_dir / "SwarmUI.deps.json"),
                    str(harness_dll),
                ],
                cwd=temp_dir,
                capture_output=True,
                text=True,
            )
            self.assertEqual(executed.returncode, 0, executed.stdout + executed.stderr)

    def test_conditioning_cache_keys_exact_anima_by_model_and_semantic_edges(self):
        conditioning = method_body(self.workflow, "CreateConditioningDirect(string prompt")
        cache_lookup = conditioning.index("NodeHelpers.TryGetValue(trackerId")
        exact_context = conditioning.index("string anima38TrackerContext")
        tracker = conditioning.index("string trackerId")
        self.assertLess(exact_context, tracker)
        self.assertLess(tracker, cache_lookup)
        context_block = conditioning[exact_context:tracker]
        for edge in (
            "CurrentModel.Path[0]",
            "CurrentModel.Path[1]",
            "semanticClip[0]",
            "semanticClip[1]",
        ):
            with self.subTest(edge=edge):
                self.assertIn(edge, context_block)
        self.assertIn("{anima38TrackerContext}", conditioning[tracker:cache_lookup])
        self.assertLess(
            context_block.index("if (IsAnima38())"),
            context_block.rindex("anima38TrackerContext ="),
        )
        self.assertRegex(
            context_block,
            r'string anima38TrackerContext\s*=\s*"";',
        )

    def test_video_conditioning_scopes_loaded_model_context_and_restores_it(self):
        prep = method_body(self.workflow, "void PrepModelAndCond(WorkflowGenerator g)")
        self.assertIn("T2IModel priorLoadedModel = g.FinalLoadedModel;", prep)
        self.assertIn("WGNodeData priorCurrentModel = g.CurrentModel;", prep)
        self.assertIn("try", prep)
        self.assertIn("g.CurrentModel = Model;", prep)
        self.assertLess(
            prep.index("g.CurrentModel = Model;"),
            prep.index("PosCond = g.CreateConditioning"),
        )
        finally_start = prep.index("finally")
        self.assertIn("g.FinalLoadedModel = priorLoadedModel;", prep[finally_start:])
        self.assertIn("g.CurrentModel = priorCurrentModel;", prep[finally_start:])

        image_to_video_wrapper = method_body(
            self.workflow, "void CreateImageToVideo(ImageToVideoGenInfo genInfo)"
        )
        self.assertIn("CreateImageToVideoInternal(genInfo);", image_to_video_wrapper)
        outer_finally = image_to_video_wrapper.rindex("finally")
        self.assertIn("FinalLoadedModel = priorLoadedModel;", image_to_video_wrapper[outer_finally:])
        self.assertIn("CurrentModel = priorCurrentModel;", image_to_video_wrapper[outer_finally:])

        image_to_video = method_body(
            self.workflow, "void CreateImageToVideoInternal(ImageToVideoGenInfo genInfo)"
        )
        self.assertLess(
            image_to_video.index("CurrentModel = genInfo.Model;"),
            image_to_video.index("CreateKSampler(genInfo.Model.Path"),
        )
        swap_start = image_to_video.index("if (genInfo.VideoSwapModel is not null)", 1000)
        swap = image_to_video[swap_start:]
        self.assertIn("T2IModel priorSwapLoadedModel = FinalLoadedModel;", swap)
        self.assertIn("WGNodeData priorSwapCurrentModel = CurrentModel;", swap)
        self.assertIn("FinalLoadedModel = swapModel;", swap)
        self.assertIn("CurrentModel = swapVideoModel;", swap)
        self.assertLess(
            swap.index("CurrentModel = swapVideoModel;"),
            swap.index("genInfo.PosCond = CreateConditioning"),
        )
        swap_finally = swap.index("finally")
        self.assertIn("FinalLoadedModel = priorSwapLoadedModel;", swap[swap_finally:])
        self.assertIn("CurrentModel = priorSwapCurrentModel;", swap[swap_finally:])

        extend_start = self.steps.index("T2IModel extendModel =")
        extend_end = self.steps.index("g.CurrentMedia = g.CurrentMedia.AsRawImage(genInfo.Vae);", extend_start)
        extend = self.steps[extend_start:extend_end]
        self.assertIn("VideoModel = extendModel", extend)
        self.assertIn("VideoSwapModel = g.UserInput.Get(T2IParamTypes.VideoExtendSwapModel", extend)
        self.assertIn("g.CreateImageToVideo(genInfo);", extend)

    def test_model_loader_cache_identity_includes_section_and_loading_state(self):
        loader = method_body(self.model_support, "CreateModelLoader(T2IModel model")
        cache_lookup = loader.index("NodeHelpers.TryGetValue(helper")
        prefix = loader[:cache_lookup]
        self.assertIn("LoadingModelType = type;", prefix)
        self.assertIn("LoadingModelSectionID = ResolveModelLoadingSection(sectionId);", prefix)
        self.assertIn("LoadingModelLoraSectionID = ResolveModelLoraSection(sectionId);", prefix)
        self.assertIn(
            "ModelLoaderCacheKey(model, type, LoadingModelSectionID, noCascadeFix, NoVAEOverride, IsRefinerStage, IsPixelDecoderStage, IsImageToVideo, IsImageToVideoSwap)",
            prefix,
        )

        cache_key = method_body(self.model_support, "string ModelLoaderCacheKey(T2IModel model")
        for component in (
            "sectionId",
            "noCascadeFix",
            "noVaeOverride",
            "isRefinerStage",
            "isPixelDecoderStage",
            "isImageToVideo",
            "isImageToVideoSwap",
        ):
            with self.subTest(component=component):
                self.assertIn(component, cache_key)

        self.assertIn("LoadedModelLists.TryGetValue(helper", loader)
        self.assertIn("FinalLoadedModelList = [.. cachedModelList]", loader)
        self.assertIn("LoadedModelLists[helper] = [.. FinalLoadedModelList]", loader)

        segment = method_body(self.steps, "void RunSegmentationProcessing(WorkflowGenerator g")
        self.assertIn("g.FinalLoadedModelList = [segmentModel];", segment)
        self.assertIn('CreateModelLoader(t2iModel, "Refiner", sectionId: parts[0].ContextID)', segment)

        lora_section = method_body(self.model_support, "int ResolveModelLoraSection(int sectionId)")
        self.assertLess(lora_section.index("if (IsImageToVideo)"), lora_section.index("return T2IParamInput.SectionID_BaseOnly;"))
        model_steps = method_body(self.steps, "public static void Register()")
        self.assertIn("g.LoadingModelLoraSectionID", model_steps)
        self.assertNotIn("LoadLorasForConfinement(g.LoadingModelSectionID", model_steps)

    def test_negative_loader_uses_positive_segment_section_for_confined_loras(self):
        lora_section = method_body(self.model_support, "int ResolveModelLoraSection(int sectionId)")
        negative_branch = 'if (LoadingModelType == "negative" && sectionId > 0)'
        self.assertIn(negative_branch, lora_section)
        self.assertLess(
            lora_section.index("if (IsImageToVideo)"),
            lora_section.index(negative_branch),
        )
        self.assertLess(
            lora_section.index(negative_branch),
            lora_section.index("return T2IParamInput.SectionID_BaseOnly;"),
        )
        self.assertIn("return sectionId;", lora_section[lora_section.index(negative_branch):])

        sampler = method_body(self.workflow, "CreateKSampler(JArray model")
        negative_start = sampler.index("T2IParamTypes.NegativeModel")
        negative_end = sampler.index("if (IsVideoModel())", negative_start)
        self.assertIn(
            'CreateModelLoader(negModel, "negative", sectionId: sectionId)',
            sampler[negative_start:negative_end],
        )

    def test_video_swap_uses_role_specific_section_for_load_settings_and_sampling(self):
        self.assertIn("public int SwapContextID", self.workflow)
        image_to_video = method_body(
            self.workflow, "void CreateImageToVideoInternal(ImageToVideoGenInfo genInfo)"
        )
        swap_start = image_to_video.index("if (genInfo.VideoSwapModel is not null)", 1000)
        swap = image_to_video[swap_start:]
        self.assertIn("int swapSectionId = genInfo.SwapContextID;", swap)
        self.assertIn("sectionId: swapSectionId", swap)
        self.assertNotIn(
            'CreateModelLoader(genInfo.VideoSwapModel, "image2video", null, true, sectionId: genInfo.ContextID)',
            swap,
        )
        self.assertNotIn("sectionId: T2IParamInput.SectionID_VideoSwap", swap)

        main_video = self.steps[self.steps.index("VideoModel = vidModel"):]
        self.assertIn("SwapContextID = T2IParamInput.SectionID_VideoSwap", main_video)
        extend_start = self.steps.index("VideoModel = extendModel")
        extend_end = self.steps.index("g.CreateImageToVideo(genInfo);", extend_start)
        self.assertIn("SwapContextID = part.ContextID", self.steps[extend_start:extend_end])

    def test_scoped_model_lists_are_fresh_and_restored_for_negative_and_video_models(self):
        sampler = method_body(self.workflow, "CreateKSampler(JArray model")
        negative_start = sampler.index("T2IParamTypes.NegativeModel")
        negative_end = sampler.index("if (IsVideoModel())", negative_start)
        negative = sampler[negative_start:negative_end]
        self.assertIn("List<T2IModel> priorLoadedModelList = FinalLoadedModelList;", negative)
        self.assertIn("FinalLoadedModelList = [negModel];", negative)
        self.assertIn("FinalLoadedModelList = priorLoadedModelList;", negative)

        prep = method_body(self.workflow, "void PrepModelAndCond(WorkflowGenerator g)")
        self.assertIn("List<T2IModel> priorLoadedModelList = g.FinalLoadedModelList;", prep)
        self.assertIn("g.FinalLoadedModelList = [VideoModel];", prep)
        self.assertIn("ModelList = [.. g.FinalLoadedModelList];", prep)
        self.assertIn("g.FinalLoadedModelList = priorLoadedModelList;", prep)

        wrapper = method_body(self.workflow, "void CreateImageToVideo(ImageToVideoGenInfo genInfo)")
        self.assertIn("List<T2IModel> priorLoadedModelList = FinalLoadedModelList;", wrapper)
        self.assertIn("FinalLoadedModelList = priorLoadedModelList;", wrapper)

        internal = method_body(
            self.workflow, "void CreateImageToVideoInternal(ImageToVideoGenInfo genInfo)"
        )
        self.assertIn("FinalLoadedModelList = genInfo.ModelList", internal)
        self.assertLess(
            internal.index("FinalLoadedModelList = genInfo.ModelList"),
            internal.index("genInfo.PrepFullCond(this, srcImage)"),
        )
        swap_start = internal.index("if (genInfo.VideoSwapModel is not null)", 1000)
        swap = internal[swap_start:]
        self.assertIn("List<T2IModel> priorSwapLoadedModelList = FinalLoadedModelList;", swap)
        self.assertIn("FinalLoadedModelList = [genInfo.VideoSwapModel];", swap)
        self.assertIn("FinalLoadedModelList = priorSwapLoadedModelList;", swap)

    def test_anima_sampler_defaults_override_inherited_video_defaults_only_without_explicit_input(self):
        sampler = method_body(self.workflow, "CreateKSampler(JArray model")
        exact_start = sampler.index("else if (IsAnima38())")
        generic_start = sampler.index("else if (IsAnima())", exact_start)
        exact = sampler[exact_start:generic_start]
        generic_end = sampler.index("else if", generic_start + len("else if"))
        generic = sampler[generic_start:generic_end]
        self.assertIn("if (explicitSampler is null)", exact)
        self.assertIn('defsampler = "res_multistep";', exact)
        self.assertIn("if (explicitScheduler is null)", exact)
        self.assertIn('defscheduler = "beta";', exact)
        self.assertNotIn("defsampler ??=", exact)
        self.assertIn('defsampler = "er_sde";', generic)
        self.assertIn('defscheduler = "simple";', generic)

        video = method_body(
            self.workflow, "void CreateImageToVideoInternal(ImageToVideoGenInfo genInfo)"
        )
        swap_start = video.index("if (genInfo.VideoSwapModel is not null)", 1000)
        swap = video[swap_start:]
        self.assertIn("string swapExplicitSampler =", swap)
        self.assertIn("string swapExplicitScheduler =", swap)
        self.assertNotIn("?? explicitSampler", swap)
        self.assertNotIn("?? explicitScheduler", swap)
        self.assertIn("explicitSampler: swapExplicitSampler", swap)
        self.assertIn("explicitScheduler: swapExplicitScheduler", swap)
        self.assertIn("swapModel.ModelClass?.ID == genInfo.VideoModel.ModelClass?.ID", swap)
        self.assertIn("defsampler: swapDefaultSampler", swap)
        self.assertIn("defscheduler: swapDefaultScheduler", swap)

    def test_break_conditioning_assigns_explicit_id_only_to_first_segment(self):
        line = method_body(self.workflow, "CreateConditioningLine(string prompt")
        first = line.index("CreateConditioningDirect(breaks[0]")
        loop = line.index("for (int i = 1", first)
        self.assertIn("id, attachImages", line[first:loop])
        self.assertNotIn("id, attachImages", line[loop:])

    def test_exact_defaults_are_res_multistep_beta_without_overriding_explicit_values(self):
        sampler = method_body(self.workflow, "CreateKSampler(JArray model")
        exact_start = sampler.index("else if (IsAnima38())")
        generic_start = sampler.index("else if (IsAnima())", exact_start)
        exact_branch = sampler[exact_start:generic_start]
        generic_end = sampler.index("else if", generic_start + len("else if"))
        generic_branch = sampler[generic_start:generic_end]

        self.assertIn('defsampler = "res_multistep";', exact_branch)
        self.assertIn('defscheduler = "beta";', exact_branch)
        self.assertIn('defsampler = "er_sde";', generic_branch)
        self.assertIn('defscheduler = "simple";', generic_branch)
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

    def test_exact_anima_lora_paths_use_bridge_nodes_without_changing_generic_fallbacks(self):
        ordinary = method_body(self.workflow, "LoadLorasForConfinement(int confinement")
        self.assertIn("SelectLoraNodeKind(FinalLoadedModel", ordinary)
        self.assertIn("ComfyNodeNames.Anima38LoraLoaderModelOnly", ordinary)
        self.assertIn("ComfyNodeNames.Anima38LoraLoader", ordinary)
        self.assertIn('"LoraLoaderModelOnly"', ordinary)
        self.assertIn('"LoraLoader"', ordinary)
        for input_name in (
            "Anima38LoraLoader.Model",
            "Anima38LoraLoader.CLIP",
            "Anima38LoraLoader.LoraName",
            "Anima38LoraLoader.StrengthModel",
            "Anima38LoraLoader.StrengthClip",
            "Anima38LoraLoaderModelOnly.Model",
            "Anima38LoraLoaderModelOnly.LoraName",
            "Anima38LoraLoaderModelOnly.StrengthModel",
        ):
            with self.subTest(input_name=input_name):
                self.assertIn(f"ComfyNodeInputNames.{input_name}", ordinary)
        self.assertIn("model = [newId, 0];", ordinary)
        self.assertIn("clip = [newId, 1];", ordinary)
        self.assertLess(
            ordinary.index("kind == LoraNodeKind.ModelOnlyLoader"),
            ordinary.index("ComfyNodeNames.Anima38LoraLoaderModelOnly"),
        )
        self.assertNotIn("source_block_count", ordinary)
        self.assertNotIn("28", ordinary)
        self.assertNotIn("40", ordinary)
        self.assertNotIn("52", ordinary)

        hooks = method_body(self.workflow, "CreateHookLorasForConfinement(int confinement")
        self.assertIn("SelectLoraNodeKind(FinalLoadedModel", hooks)
        self.assertIn("ComfyNodeNames.Anima38CreateHookLora", hooks)
        self.assertIn('"CreateHookLora"', hooks)
        for input_name in (
            "Anima38CreateHookLora.PrevHooks",
            "Anima38CreateHookLora.LoraName",
            "Anima38CreateHookLora.StrengthModel",
            "Anima38CreateHookLora.StrengthClip",
        ):
            with self.subTest(input_name=input_name):
                self.assertIn(f"ComfyNodeInputNames.{input_name}", hooks)
        self.assertIn("[ComfyNodeInputNames.Anima38CreateHookLora.PrevHooks] = last", hooks)
        self.assertIn("JArray currentHooks = [newId, 0];", hooks)
        self.assertIn('["hooks"] = currentHooks', hooks)
        self.assertIn("last = currentHooks;", hooks)

    def test_exact_anima_lora_bridge_capabilities_follow_shared_workflow_applicability(self):
        bridge_capabilities = (
            ("Anima38LoraLoader", "Anima38LoraLoaderNodeFeature"),
            ("Anima38LoraLoaderModelOnly", "Anima38LoraLoaderModelOnlyNodeFeature"),
            ("Anima38CreateHookLora", "Anima38CreateHookLoraNodeFeature"),
        )
        for node_name, feature_name in bridge_capabilities:
            with self.subTest(node_name=node_name):
                self.assertIn(f"public const string {feature_name}", self.capabilities)
                self.assertIn(
                    f"[ComfyNodeNames.{node_name}] = {feature_name}", self.capabilities
                )

        routing = method_body(self.extension, "RecomputeBackendRoutingRequirements(T2IParamInput input)")
        self.assertIn("WorkflowGenerator.GetRequiredAnima38LoraNodes(input)", routing)
        self.assertNotIn("hasAnyAnima38 && hasAnyLoras", routing)
        bridge_requirement = routing.index("WorkflowGenerator.GetRequiredAnima38LoraNodes(input)")
        bridge_block = routing[bridge_requirement:]
        capability_mapper = method_body(
            self.extension, "private static string[] GetAnima38LoraCapabilityRequirements"
        )
        for (_, feature_name), requirement_name in zip(
            bridge_capabilities, ("FullLoader", "ModelOnlyLoader", "HookLoader")
        ):
            with self.subTest(feature_name=feature_name):
                self.assertIn(
                    f"HasFlag(WorkflowGenerator.Anima38LoraNodeRequirement.{requirement_name})",
                    capability_mapper,
                )
                self.assertIn(
                    f"features.Add(ComfyCapabilityCatalog.{feature_name})",
                    capability_mapper,
                )
        self.assertIn("GetAnima38LoraCapabilityRequirements(animaLoraRequirements)", bridge_block)

        applicability = method_body(
            self.workflow, "internal static Anima38LoraNodeRequirement GetRequiredAnima38LoraNodes(T2IParamInput input)"
        )
        for activation_rule in (
            "NegativeModelIncludeLoras",
            "RefinerMethod",
            "RefinerControl",
            "PromptRegion.PartType.Segment",
            "PromptRegion.PartType.Extend",
            "VideoModel",
        ):
            with self.subTest(activation_rule=activation_rule):
                self.assertIn(activation_rule, applicability)
        for role in (
            "Model",
            "RefinerModel",
            "SegmentModel",
            "NegativeModel",
            "VideoModel",
            "VideoSwapModel",
            "VideoExtendModel",
            "VideoExtendSwapModel",
        ):
            with self.subTest(role=role):
                self.assertIn(f"T2IParamTypes.{role}", applicability)
        for section in (
            "SectionID_BaseOnly",
            "SectionID_Refiner",
            "SectionID_Video",
            "SectionID_VideoSwap",
        ):
            with self.subTest(section=section):
                self.assertIn(section, applicability)
        self.assertIn("addOrdinary", applicability)
        self.assertIn("addHooks", applicability)
        self.assertIn("segmentParts.Length > 0", applicability)
        self.assertIn("extendParts.Length > 0", applicability)
        self.assertIn("videoActive", applicability)
        self.assertIn("includeNegativeLoras", applicability)

        confinement = method_body(
            self.workflow, "private static int GetLoraConfinementAt(IReadOnlyList<string> confinements"
        )
        self.assertIn("int.Parse(confinements[index])", confinement)
        self.assertIn(
            "private static bool LoraAppliesToConfinements(IReadOnlyList<string> confinements",
            self.workflow,
        )
        applies_to_confinement = method_body(
            self.workflow,
            "private static bool LoraAppliesToConfinements(IReadOnlyList<string> confinements",
        )
        self.assertIn("GetLoraConfinementAt(confinements, index)", applies_to_confinement)
        self.assertIn("ResolveLoraScheduleAt(schedules, i)", applicability)
        self.assertIn("LoraAppliesToConfinements(confinements, i", applicability)
        ordinary = method_body(self.workflow, "LoadLorasForConfinement(int confinement")
        hooks = method_body(self.workflow, "CreateHookLorasForConfinement(int confinement")
        self.assertIn("LoraAppliesToConfinements(confinements, i, confinement)", ordinary)
        self.assertIn("LoraAppliesToConfinements(confinements, i, confinement)", hooks)

    def test_bridge_routing_excludes_inapplicable_exact_roles(self):
        self.assertIn(
            "internal static Anima38LoraNodeRequirement GetRequiredAnima38LoraNodes(T2IParamInput input)",
            self.workflow,
        )
        applicability = method_body(
            self.workflow, "internal static Anima38LoraNodeRequirement GetRequiredAnima38LoraNodes(T2IParamInput input)"
        )
        cases = {
            "negative LoRAs disabled": (
                "includeNegativeLoras",
                "if (includeNegativeLoras)",
            ),
            "refiner-only LoRA cannot reach exact base": (
                "SectionID_BaseOnly",
                "SectionID_Refiner",
            ),
            "selected segment model is inactive without segments": (
                "segmentParts.Length > 0",
                "SegmentModel",
            ),
            "selected extend model is inactive without extend blocks": (
                "extendParts.Length > 0",
                "VideoExtendModel",
            ),
        }
        for case, expected in cases.items():
            with self.subTest(case=case):
                for fragment in expected:
                    self.assertIn(fragment, applicability)

    def test_bridge_routing_includes_active_exact_roles_with_applicable_loras(self):
        self.assertIn(
            "internal static Anima38LoraNodeRequirement GetRequiredAnima38LoraNodes(T2IParamInput input)",
            self.workflow,
        )
        applicability = method_body(
            self.workflow, "internal static Anima38LoraNodeRequirement GetRequiredAnima38LoraNodes(T2IParamInput input)"
        )
        positive_paths = {
            "base": "SectionID_BaseOnly",
            "included negative": "includeNegativeLoras",
            "refiner": "refinerActive",
            "segment": "segmentParts",
            "video": "videoActive",
            "extend": "extendParts",
        }
        for case, fragment in positive_paths.items():
            with self.subTest(case=case):
                self.assertIn(fragment, applicability)

    def test_segment_bridge_routing_matches_phase_model_and_explicit_loader_passes(self):
        applicability = method_body(
            self.workflow, "internal static Anima38LoraNodeRequirement GetRequiredAnima38LoraNodes(T2IParamInput input)"
        )
        self.assertIn('string segmentApplyAfter = input.Get(T2IParamTypes.SegmentApplyAfter, "Refiner")', applicability)
        self.assertIn(
            'T2IModel segmentPhaseModel = segmentApplyAfter == "Base" ? baseModel : modelAfterRefiner',
            applicability,
        )
        explicit_start = applicability.index("if (explicitSegmentModel is not null)")
        implicit_start = applicability.index("else", explicit_start)
        explicit_branch = applicability[explicit_start:implicit_start]
        implicit_branch = applicability[implicit_start:]
        self.assertIn("T2IParamInput.SectionID_BaseOnly", explicit_branch)
        self.assertIn("segmentContextConfinements", explicit_branch)
        self.assertIn("segmentPhaseModel", implicit_branch)
        self.assertIn("segmentContextConfinements", implicit_branch)

    def test_schedule_aware_bridge_routing_matches_ordinary_and_hook_emission_sites(self):
        self.assertIn(
            "public string GetLoraScheduleAt(List<string> schedules, int index)",
            self.workflow,
        )
        self.assertIn(
            "private static string ResolveLoraScheduleAt(IReadOnlyList<string> schedules",
            self.workflow,
        )
        schedule = method_body(
            self.workflow, "private static string ResolveLoraScheduleAt(IReadOnlyList<string> schedules"
        )
        self.assertIn('schedule.Equals("none", StringComparison.OrdinalIgnoreCase)', schedule)
        applicability = method_body(
            self.workflow, "internal static Anima38LoraNodeRequirement GetRequiredAnima38LoraNodes(T2IParamInput input)"
        )
        self.assertIn("addLoader(baseModel, T2IParamInput.SectionID_BaseOnly)", applicability)
        self.assertIn("addHooks(baseModel, false, baseRegionalConfinements)", applicability)
        for role_section in ("SectionID_Refiner", "SectionID_Video", "SectionID_VideoSwap"):
            with self.subTest(role_section=role_section):
                self.assertIn(f"T2IParamInput.{role_section}", applicability)
        self.assertIn("addOrdinary(segmentPhaseModel", applicability)
        self.assertIn("addHooks(segmentPhaseModel", applicability)

    def test_negative_bridge_routing_requires_shared_actual_sampler_activity(self):
        self.assertIn("internal static BaseSamplerRange GetBaseSamplerRange", self.workflow)
        sampler_range = method_body(self.workflow, "internal static BaseSamplerRange GetBaseSamplerRange")
        for condition in (
            "InitImageCreativity",
            "DenoiseStrength",
            "RefinerMethod",
            "RefinerControl",
            "EndStepsEarly",
        ):
            with self.subTest(condition=condition):
                self.assertIn(condition, sampler_range)
        sampler_range_record = method_body(self.workflow, "internal record struct BaseSamplerRange")
        self.assertIn("Math.Min(EndStep, Steps) > StartStep", sampler_range_record)
        model_steps = method_body(self.steps, "public static void Register()")
        self.assertIn("WorkflowGenerator.GetBaseSamplerRange(g.UserInput, g.IsPiD())", model_steps)
        applicability = method_body(
            self.workflow, "internal static Anima38LoraNodeRequirement GetRequiredAnima38LoraNodes(T2IParamInput input)"
        )
        self.assertIn("GetBaseSamplerRange(input", applicability)
        self.assertIn("if (baseSamplerRange.Runs)", applicability)

    def test_regional_hook_bridge_routing_uses_active_conditioning_model_contexts(self):
        self.assertIn("private static int[] GetRegionalHookConfinements", self.workflow)
        regional = method_body(
            self.workflow, "private static int[] GetRegionalHookConfinements"
        )
        self.assertIn("PromptRegion.PartType.Object", regional)
        self.assertIn("PromptRegion.PartType.Region", regional)
        self.assertIn("RegionalPromptingMethod", regional)
        self.assertIn('regionalMethod == "Attention Couple" && !isPositive', regional)
        self.assertIn('GligenModel', regional)
        self.assertIn("part.ContextID > 1", regional)
        applicability = method_body(
            self.workflow, "internal static Anima38LoraNodeRequirement GetRequiredAnima38LoraNodes(T2IParamInput input)"
        )
        self.assertIn("positiveRegionalConfinements", applicability)
        self.assertIn("negativeRegionalConfinements", applicability)
        self.assertIn("UnsamplerPrompt", applicability)
        self.assertIn("baseRegionalConfinements", applicability)
        self.assertIn("addHooks(refinerModel, false, mainRegionalConfinements)", applicability)
        self.assertIn("segmentPhaseModel", applicability)
        self.assertIn("videoModel", applicability)
        self.assertIn("extendModel", applicability)
        conditioning = method_body(self.workflow, "public JArray CreateConditioning(string prompt")
        self.assertIn("GetRegionalHookConfinements(UserInput, prompt, isPositive)", conditioning)

    def test_negative_bridge_routing_covers_every_sampler_section_and_refiner_exit(self):
        applicability = method_body(
            self.workflow, "internal static Anima38LoraNodeRequirement GetRequiredAnima38LoraNodes(T2IParamInput input)"
        )
        for section in (
            "SectionID_PixelDecoder",
            "SectionID_SeedVR",
            "SectionID_Refiner",
        ):
            with self.subTest(section=section):
                self.assertIn(section, applicability)
        self.assertIn("RefinerSamplerRuns(input", applicability)
        self.assertIn("PixelDecoderSamplerRuns(input", applicability)
        self.assertIn("SeedVRSamplerRuns(input", applicability)
        refiner_activation = method_body(
            self.workflow, "internal static bool RefinerSamplerRuns"
        )
        self.assertIn('compat == "pid"', refiner_activation)
        self.assertIn('compat == "seedvr2"', refiner_activation)
        self.assertIn("RefinerControl", refiner_activation)
        self.assertIn("RefinerUpscale", refiner_activation)
        self.assertIn("RefinerUpscaleMethod", refiner_activation)
        steps = method_body(self.steps, "public static void Register()")
        self.assertIn("WorkflowGenerator.RefinerSamplerRuns(g.UserInput", steps)

    def test_preview_termination_keeps_only_pre_preview_bridge_emission_sites(self):
        self.assertIn("private static bool WorkflowTerminatesBeforeSampling", self.workflow)
        termination = method_body(
            self.workflow, "private static bool WorkflowTerminatesBeforeSampling"
        )
        self.assertIn("IsControlNetPreviewActive(input)", termination)
        self.assertIn("IsSam3PointPreviewActive(input)", termination)
        self.assertIn("IsSam3BBoxPreviewActive(input)", termination)
        self.assertIn("IsSam3PromptPreviewActive(input)", termination)
        control_preview = method_body(self.workflow, "private static bool IsControlNetPreviewActive")
        self.assertIn("ControlNetPreviewOnly", control_preview)
        self.assertIn("ResolveControlNetPreprocessor(input, i)", control_preview)
        for helper, parameter in (
            ("IsSam3PointPreviewActive", "Sam3PointCoordsPositive"),
            ("IsSam3BBoxPreviewActive", "Sam3BBox"),
            ("IsSam3PromptPreviewActive", "Sam3SegmentPrompt"),
        ):
            preview = method_body(self.workflow, f"internal static bool {helper}")
            self.assertIn(parameter, preview)
            self.assertIn("TryGetBasicInputImage(input, out Image _)", preview)
        applicability = method_body(
            self.workflow, "internal static Anima38LoraNodeRequirement GetRequiredAnima38LoraNodes(T2IParamInput input)"
        )
        termination_check = applicability.index("WorkflowTerminatesBeforeSampling(input)")
        base_loader_check = applicability.index("addLoader(baseModel")
        base_regions_check = applicability.index("baseRegionalConfinements")
        refiner_check = applicability.index("refinerActive")
        self.assertLess(base_loader_check, termination_check)
        self.assertLess(base_regions_check, termination_check)
        self.assertLess(termination_check, refiner_check)
        steps = method_body(self.steps, "public static void Register()")
        self.assertIn("WorkflowGenerator.ResolveControlNetPreprocessor(g.UserInput, i)", steps)
        self.assertIn("WorkflowGenerator.IsSam3PointPreviewActive(g.UserInput)", steps)
        self.assertIn("WorkflowGenerator.IsSam3BBoxPreviewActive(g.UserInput)", steps)
        self.assertIn("WorkflowGenerator.IsSam3PromptPreviewActive(g.UserInput)", steps)

    def test_unsampler_regional_hooks_are_active_and_base_model_only(self):
        applicability = method_body(
            self.workflow, "internal static Anima38LoraNodeRequirement GetRequiredAnima38LoraNodes(T2IParamInput input)"
        )
        self.assertIn("mainRegionalConfinements", applicability)
        self.assertIn("baseRegionalConfinementSet", applicability)
        self.assertIn("TryGetBasicInputImage(input, out Image _)", applicability)
        unsampler_check = applicability.index("T2IParamTypes.UnsamplerPrompt")
        base_check = applicability.index("addHooks(baseModel, false, baseRegionalConfinements")
        later_roles = applicability.index("bool refinerActive")
        self.assertLess(unsampler_check, base_check)
        self.assertLess(base_check, later_roles)
        self.assertNotIn("baseRegionalConfinements", applicability[later_roles:])
        self.assertIn("mainRegionalConfinements", applicability[later_roles:])

    def test_basic_input_image_eligibility_is_shared_by_emission_and_previews(self):
        self.assertIn("internal static bool TryGetBasicInputImage", self.workflow)
        eligibility = method_body(
            self.workflow, "internal static bool TryGetBasicInputImage"
        )
        self.assertIn("T2IParamTypes.Model", eligibility)
        self.assertIn("CompatClass?.IsAudioModel", eligibility)
        self.assertIn("T2IParamTypes.InitImage", eligibility)
        steps = method_body(self.steps, "public static void Register()")
        self.assertIn(
            "WorkflowGenerator.TryGetBasicInputImage(g.UserInput, out Image img)", steps
        )
        control_preview = method_body(
            self.workflow, "private static bool IsControlNetPreviewActive"
        )
        self.assertIn("TryGetBasicInputImage(input, out Image _)", control_preview)
        for helper in (
            "IsSam3PointPreviewActive",
            "IsSam3BBoxPreviewActive",
            "IsSam3PromptPreviewActive",
        ):
            preview = method_body(self.workflow, f"internal static bool {helper}")
            self.assertIn("TryGetBasicInputImage(input, out Image _)", preview)
            self.assertNotIn("input.TryGet(T2IParamTypes.InitImage", preview)

    def test_exact_anima_rejects_lllite_before_graph_emission(self):
        steps = method_body(self.steps, "public static void Register()")
        anima_control_start = steps.index(
            "controlModel.ModelClass?.CompatClass?.ID == T2IModelClassSorter.CompatAnima.ID"
        )
        anima_control_end = steps.index('CreateNode("ControlNetLoader"', anima_control_start)
        anima_control = steps[anima_control_start:anima_control_end]
        self.assertIn("if (g.IsAnima38())", anima_control)
        guard = anima_control.index("if (g.IsAnima38())")
        error = anima_control.index("throw new SwarmUserErrorException", guard)
        emission = anima_control.index("CreateNode(ComfyNodeNames.AnimaLLLite", error)
        self.assertLess(guard, error)
        self.assertLess(error, emission)
        self.assertIn("Anima 3.8B LLLite", anima_control[error:emission])
        self.assertIn("block mapping is not known-safe", anima_control[error:emission])

    def test_exact_lora_requirement_prediction_uses_production_csharp_decisions(self):
        output_dir = REPOSITORY_ROOT / "src/bin/Debug/net8.0"
        swarm_dll = output_dir / "SwarmUI.dll"
        self.assertTrue(swarm_dll.exists(), "Build SwarmUI before running the C# behavior harness")
        sdk_root = pathlib.Path("/usr/share/dotnet/sdk")
        compiler = sorted(sdk_root.glob("*/Roslyn/bincore/csc.dll"))[-1]
        reference_roots = [
            pathlib.Path("/usr/share/dotnet/packs/Microsoft.NETCore.App.Ref"),
            pathlib.Path.home() / ".nuget/packages/microsoft.netcore.app.ref",
        ]
        reference_dirs = [
            path for root in reference_roots for path in root.glob("*/ref/net8.0")
        ]
        reference_dir = sorted(reference_dirs)[-1]
        with tempfile.TemporaryDirectory(prefix="anima38-routing-") as temp_raw:
            temp_dir = pathlib.Path(temp_raw)
            harness_dll = temp_dir / "Anima38LoraRequirementHarness.dll"
            compile_command = [
                "dotnet",
                str(compiler),
                "-noconfig",
                "-nostdlib",
                "-langversion:latest",
                "-target:exe",
                f"-out:{harness_dll}",
                *[f"-r:{path}" for path in sorted(reference_dir.glob("*.dll"))],
                f"-r:{swarm_dll}",
                str(REQUIREMENT_HARNESS_PATH),
            ]
            compiled = subprocess.run(compile_command, capture_output=True, text=True)
            self.assertEqual(compiled.returncode, 0, compiled.stdout + compiled.stderr)
            for source in output_dir.glob("*.dll"):
                shutil.copy2(source, temp_dir / source.name)
            executed = subprocess.run(
                [
                    "dotnet",
                    "exec",
                    "--runtimeconfig",
                    str(output_dir / "SwarmUI.runtimeconfig.json"),
                    "--depsfile",
                    str(output_dir / "SwarmUI.deps.json"),
                    str(harness_dll),
                ],
                cwd=temp_dir,
                capture_output=True,
                text=True,
            )
            self.assertEqual(executed.returncode, 0, executed.stdout + executed.stderr)


if __name__ == "__main__":
    unittest.main()
