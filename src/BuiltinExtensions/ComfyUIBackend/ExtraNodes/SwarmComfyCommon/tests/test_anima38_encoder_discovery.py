"""Regression coverage for header-based encoder discovery and file aliases."""

import importlib.util
import json
import os
import pathlib
import struct
import sys
import tempfile
import types
import unittest
from unittest.mock import patch

import torch  # Initialize PyTorch before safetensors' lazy framework import.


MODULE_PATH = pathlib.Path(__file__).resolve().parents[1] / "SwarmAnima38.py"
SPEC = importlib.util.spec_from_file_location("anima38_discovery_test", MODULE_PATH)
anima38 = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(anima38)


def write_checkpoint(path, tensors):
    """Write sparse safetensors fixtures; discovery must never read weights."""
    header = {}
    offset = 0
    for key, shape in tensors.items():
        count = 1
        for dimension in shape:
            count *= dimension
        end = offset + count * 2
        header[key] = {"dtype": "F16", "shape": shape, "data_offsets": [offset, end]}
        offset = end
    encoded = json.dumps(header).encode()
    encoded += b" " * (-len(encoded) % 8)
    with path.open("wb") as stream:
        stream.write(struct.pack("<Q", len(encoded)))
        stream.write(encoded)
        stream.truncate(8 + len(encoded) + offset)


def encoder_shapes(prefix=""):
    return {
        f"{prefix}embed_tokens.weight": [248320, 2560],
        **{f"{prefix}layers.{index}.input_layernorm.weight": [2560] for index in range(32)},
    }


class EncoderDiscoveryTests(unittest.TestCase):
    def setUp(self):
        self.directory = tempfile.TemporaryDirectory()
        self.addCleanup(self.directory.cleanup)
        self.root = pathlib.Path(self.directory.name)
        self.paths = types.ModuleType("folder_paths")
        self.paths.get_filename_list = lambda root: [p.name for p in self.root.iterdir()]
        self.paths.get_full_path = lambda root, name: str(self.root / name)
        self.folder_patch = patch.dict(sys.modules, {"folder_paths": self.paths})
        self.folder_patch.start()
        self.addCleanup(self.folder_patch.stop)

    def test_adapter_is_excluded_and_real_encoder_wins_over_alias(self):
        write_checkpoint(self.root / "qwen35_4b.safetensors", encoder_shapes())
        write_checkpoint(self.root / "anima38B_base.safetensors", {"layer_mix_logits": [6, 4]})
        (self.root / "anima38B_base_txt.safetensors").symlink_to("qwen35_4b.safetensors")
        choices = anima38._qwen_candidates()
        self.assertEqual(choices, ["qwen35_4b.safetensors"])
        self.assertEqual(anima38.select_qwen35_candidate(choices), "qwen35_4b.safetensors")

    def test_supported_key_prefixes_are_discovered(self):
        for prefix in ("", "model.", "model.language_model."):
            with self.subTest(prefix=prefix):
                write_checkpoint(self.root / "qwen35_4b.safetensors", encoder_shapes(prefix))
                self.assertEqual(anima38._qwen_candidates(), ["qwen35_4b.safetensors"])

    def test_hardlinks_are_one_encoder(self):
        write_checkpoint(self.root / "qwen35_4b.safetensors", encoder_shapes())
        os.link(self.root / "qwen35_4b.safetensors", self.root / "qwen3_5_4b-copy.safetensors")
        self.assertEqual(anima38._qwen_candidates(), ["qwen35_4b.safetensors"])

    def test_dangling_alias_is_ignored(self):
        (self.root / "anima38-missing.safetensors").symlink_to("missing.safetensors")
        self.assertEqual(anima38._qwen_candidates(), [])

    def test_wrong_width_incomplete_and_unreadable_files_are_excluded(self):
        wrong = encoder_shapes()
        wrong["embed_tokens.weight"] = [248320, 2048]
        write_checkpoint(self.root / "anima38-wrong.safetensors", wrong)
        incomplete = encoder_shapes()
        incomplete.pop("layers.31.input_layernorm.weight")
        write_checkpoint(self.root / "anima38-incomplete.safetensors", incomplete)
        (self.root / "anima38-broken.safetensors").write_bytes(b"broken")
        self.assertEqual(anima38._qwen_candidates(), [])

    def test_distinct_valid_encoders_still_require_explicit_selection(self):
        for name in ("anima38-first.safetensors", "anima38-second.safetensors"):
            write_checkpoint(self.root / name, encoder_shapes())
        choices = anima38._qwen_candidates()
        self.assertEqual(len(choices), 2)
        with self.assertRaisesRegex(ValueError, "ambiguous"):
            anima38.select_qwen35_candidate(choices)


if __name__ == "__main__":
    unittest.main()
