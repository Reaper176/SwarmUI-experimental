import pathlib
import shutil
import subprocess
import tempfile
import unittest


REPOSITORY_ROOT = pathlib.Path(__file__).resolve().parents[6]
HARNESS_PATH = (
    pathlib.Path(__file__).resolve().parent / "Anima38ClassificationHarness.cs.txt"
)


class Anima38ClassificationTests(unittest.TestCase):
    def test_anima38_tensor_identity_overrides_colliding_cosmos_metadata(self):
        build = subprocess.run(
            [
                "dotnet",
                "build",
                "src/SwarmUI.csproj",
                "--no-restore",
            ],
            cwd=REPOSITORY_ROOT,
            capture_output=True,
            text=True,
        )
        self.assertEqual(build.returncode, 0, build.stdout + build.stderr)

        output_dir = REPOSITORY_ROOT / "src/bin/Debug/net8.0"
        swarm_dll = output_dir / "SwarmUI.dll"
        newtonsoft_dll = output_dir / "Newtonsoft.Json.dll"
        self.assertTrue(swarm_dll.exists(), "SwarmUI build did not produce SwarmUI.dll")
        self.assertTrue(
            newtonsoft_dll.exists(), "SwarmUI build did not produce Newtonsoft.Json.dll"
        )

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

        with tempfile.TemporaryDirectory(prefix="anima38-classification-") as temp_raw:
            temp_dir = pathlib.Path(temp_raw)
            harness_dll = temp_dir / "Anima38ClassificationHarness.dll"
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
                str(HARNESS_PATH),
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
