import ast
import importlib
import pathlib
import re
import sys
import types
import unittest


COMMON_NODE_DIRECTORY = pathlib.Path(__file__).resolve().parents[1]
REPOSITORY_ROOT = pathlib.Path(__file__).resolve().parents[6]
TEST_PACKAGE_NAME = "_swarm_comfy_common_anima38_registration_tests"
test_package = types.ModuleType(TEST_PACKAGE_NAME)
test_package.__path__ = [str(COMMON_NODE_DIRECTORY)]
sys.modules[TEST_PACKAGE_NAME] = test_package

anima38_module = importlib.import_module(f"{TEST_PACKAGE_NAME}.SwarmAnima38")
anima38_lora_module = importlib.import_module(f"{TEST_PACKAGE_NAME}.SwarmAnima38Lora")


EXPECTED_CLASSES = {
    "SwarmLoadAnima38Qwen35": anima38_module.SwarmLoadAnima38Qwen35,
    "SwarmAnima38Conditioning": anima38_module.SwarmAnima38Conditioning,
    "SwarmAnima38LoraLoader": anima38_lora_module.SwarmAnima38LoraLoader,
    "SwarmAnima38LoraLoaderModelOnly": anima38_lora_module.SwarmAnima38LoraLoaderModelOnly,
    "SwarmAnima38CreateHookLora": anima38_lora_module.SwarmAnima38CreateHookLora,
}

EXPECTED_DISPLAY_NAMES = {
    "SwarmLoadAnima38Qwen35": "Swarm Load Anima 3.8B Qwen3.5-4B",
    "SwarmAnima38Conditioning": "Swarm Anima 3.8B Conditioning",
    "SwarmAnima38LoraLoader": "Swarm Load LoRA (Anima 3.8)",
    "SwarmAnima38LoraLoaderModelOnly": "Swarm Load LoRA Model Only (Anima 3.8)",
    "SwarmAnima38CreateHookLora": "Create Hook LoRA (Anima 3.8)",
}


def mapping_module_names(source, assignment_name):
    """Return module names whose mappings are merged into a package mapping."""
    tree = ast.parse(source)
    for node in tree.body:
        if not isinstance(node, ast.Assign):
            continue
        if not any(isinstance(target, ast.Name) and target.id == assignment_name for target in node.targets):
            continue
        names = []
        for child in ast.walk(node.value):
            if (
                isinstance(child, ast.Attribute)
                and child.attr == assignment_name
                and isinstance(child.value, ast.Name)
            ):
                names.append(child.value.id)
        return set(names)
    raise AssertionError(f"Missing assignment for {assignment_name}")


class Anima38RegistrationTests(unittest.TestCase):
    def test_anima_modules_export_exact_node_and_display_mappings(self):
        actual_classes = anima38_module.NODE_CLASS_MAPPINGS | getattr(
            anima38_lora_module, "NODE_CLASS_MAPPINGS", {}
        )
        actual_display_names = (
            anima38_module.NODE_DISPLAY_NAME_MAPPINGS
            | getattr(anima38_lora_module, "NODE_DISPLAY_NAME_MAPPINGS", {})
        )

        self.assertEqual(actual_classes, EXPECTED_CLASSES)
        self.assertEqual(actual_display_names, EXPECTED_DISPLAY_NAMES)

    def test_package_merges_both_anima_modules_into_public_mappings(self):
        package_source = (COMMON_NODE_DIRECTORY / "__init__.py").read_text(encoding="utf-8")

        self.assertIn("SwarmAnima38", mapping_module_names(package_source, "NODE_CLASS_MAPPINGS"))
        self.assertIn("SwarmAnima38Lora", mapping_module_names(package_source, "NODE_CLASS_MAPPINGS"))
        self.assertIn("SwarmAnima38", mapping_module_names(package_source, "NODE_DISPLAY_NAME_MAPPINGS"))
        self.assertIn("SwarmAnima38Lora", mapping_module_names(package_source, "NODE_DISPLAY_NAME_MAPPINGS"))

    def test_conditioning_adapter_input_matches_csharp_contract(self):
        python_source = (COMMON_NODE_DIRECTORY / "SwarmAnima38.py").read_text(encoding="utf-8")
        csharp_source = (
            REPOSITORY_ROOT
            / "src/BuiltinExtensions/ComfyUIBackend/ComfyNodeInputNames.cs"
        ).read_text(encoding="utf-8")
        conditioning_class = re.search(
            r"public static class Anima38Conditioning\s*\{(?P<body>.*?)\n    \}",
            csharp_source,
            re.DOTALL,
        )

        self.assertIsNotNone(conditioning_class)
        self.assertRegex(python_source, r'"adapter_name"\s*:')
        self.assertRegex(
            conditioning_class.group("body"),
            r'public const string Adapter = "adapter_name";',
        )

    def test_manual_choices_require_matching_backend_value_capabilities(self):
        extension_source = (
            REPOSITORY_ROOT
            / "src/BuiltinExtensions/ComfyUIBackend/ComfyUIBackendExtension.cs"
        ).read_text(encoding="utf-8")
        catalog_source = (
            REPOSITORY_ROOT
            / "src/BuiltinExtensions/ComfyUIBackend/ComfyCapabilityCatalog.cs"
        ).read_text(encoding="utf-8")

        expected_flows = {
            "Anima38Qwen35": "Anima38Qwen35Encoders",
            "Anima38Adapter": "Anima38Adapters",
        }
        for feature_stem, delta_field in expected_flows.items():
            with self.subTest(feature_stem=feature_stem):
                self.assertIn(f"{feature_stem}ValueFeaturePrefix", catalog_source)
                self.assertIn(f"{feature_stem}ValueFeature(string value)", catalog_source)
                self.assertIn(
                    f'delta.{delta_field}.Where(value => value != "auto").Select('
                    f"ComfyCapabilityCatalog.{feature_stem}ValueFeature)",
                    extension_source,
                )
                self.assertIn(
                    f"RemoveWhere(flag => flag.StartsWith(ComfyCapabilityCatalog."
                    f"{feature_stem}ValueFeaturePrefix",
                    extension_source,
                )
                self.assertRegex(
                    extension_source,
                    rf'!= "auto"\)\s*\{{\s*input\.RequiredFlags\.Add\('
                    rf"ComfyCapabilityCatalog\.{feature_stem}ValueFeature",
                )

    def test_anima_parameter_scope_uses_exact_model_architecture(self):
        frontend_source = (REPOSITORY_ROOT / "src/wwwroot/js/genpage/main.js").read_text(
            encoding="utf-8"
        )

        exact_scope_block = """    if (currentModelHelper.curArch == 'anima-3_8b') {
        addMe.push('anima-3_8b');
    }
    else {
        removeMe.push('anima-3_8b');
    }"""
        self.assertIn(exact_scope_block, frontend_source)
        self.assertNotIn(
            "doAnyArchFeature(['anima-3_8b'], 'anima-3_8b');",
            frontend_source,
        )


if __name__ == "__main__":
    unittest.main()
