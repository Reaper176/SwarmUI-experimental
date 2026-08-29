import pathlib
import subprocess
import tempfile
import textwrap
import unittest


REPOSITORY_ROOT = pathlib.Path(__file__).resolve().parents[6]
PROJECT_PATH = REPOSITORY_ROOT / "src/SwarmUI.csproj"
ASSEMBLY_PATH = REPOSITORY_ROOT / "src/bin/Debug/net8.0/SwarmUI.dll"

HARNESS_SOURCE = r"""
using System.Collections;
using System.Reflection;

Assembly assembly = Assembly.LoadFrom(args[0]);
Type extensionType = assembly.GetType("SwarmUI.Builtin_ComfyUIBackend.ComfyUIBackendExtension", true)!;
Type? registryType = assembly.GetType("SwarmUI.Builtin_ComfyUIBackend.Anima38ChoiceRegistry");
if (registryType is null)
{
    MethodInfo oldMerge = extensionType.GetMethod("MergeAnima38Choices", BindingFlags.NonPublic | BindingFlags.Static)!;
    List<string> first = (List<string>)oldMerge.Invoke(null, new object[] { new List<string> { "auto" }, new List<string> { "auto", "A" } })!;
    List<string> refreshed = (List<string>)oldMerge.Invoke(null, new object[] { first, new List<string> { "auto", "B" } })!;
    Console.Error.WriteLine($"Current additive refresh retains stale values: {string.Join(",", refreshed)}");
    return 1;
}

object registry = Activator.CreateInstance(registryType, nonPublic: true)!;
MethodInfo replace = registryType.GetMethod("Replace", BindingFlags.Instance | BindingFlags.NonPublic)!;
MethodInfo remove = registryType.GetMethod("Remove", BindingFlags.Instance | BindingFlags.NonPublic)!;

(List<string> Qwen, List<string> Adapters) Read(object result)
{
    Type resultType = result.GetType();
    List<string> qwen = [.. (IEnumerable<string>)resultType.GetProperty("Qwen")!.GetValue(result)!];
    List<string> adapters = [.. (IEnumerable<string>)resultType.GetProperty("Adapters")!.GetValue(result)!];
    return (qwen, adapters);
}

void Expect(string name, object result, string[] qwen, string[] adapters)
{
    (List<string> actualQwen, List<string> actualAdapters) = Read(result);
    if (!actualQwen.SequenceEqual(qwen) || !actualAdapters.SequenceEqual(adapters))
    {
        throw new InvalidOperationException(
            $"{name}: qwen=[{string.Join(",", actualQwen)}], adapters=[{string.Join(",", actualAdapters)}]"
        );
    }
}

object ownerA = new();
object ownerB = new();
object result = replace.Invoke(registry, new object[] { ownerA, new[] { "auto", "A" }, new[] { "auto", "controlnet::A" } })!;
result = replace.Invoke(registry, new object[] { ownerA, new[] { "auto", "B" }, new[] { "auto", "controlnet::B" } })!;
Expect("sole owner refresh replaces A with B", result, ["auto", "B"], ["auto", "controlnet::B"]);

result = remove.Invoke(registry, new[] { ownerA })!;
Expect("sole owner removal returns auto", result, ["auto"], ["auto"]);

replace.Invoke(registry, new object[] { ownerA, new[] { "auto", "A" }, Array.Empty<string>() });
replace.Invoke(registry, new object[] { ownerB, new[] { "A" }, Array.Empty<string>() });
result = remove.Invoke(registry, new[] { ownerA })!;
Expect("shared value survives one removal", result, ["auto", "A"], ["auto"]);

replace.Invoke(registry, new object[] { ownerA, new[] { "A", "z", "a", "A" }, new[] { "text_encoders::z", "controlnet::A" } });
result = replace.Invoke(registry, new object[] { ownerB, Array.Empty<string>(), Array.Empty<string>() })!;
Expect(
    "malformed refresh clears only its owner and union is deterministic",
    result,
    ["auto", "A", "a", "z"],
    ["auto", "controlnet::A", "text_encoders::z"]
);
return 0;
"""


class Anima38ChoiceRegistryTests(unittest.TestCase):
    def test_owner_refresh_removal_and_deterministic_union(self):
        build = subprocess.run(
            ["dotnet", "build", str(PROJECT_PATH), "--no-restore"],
            cwd=REPOSITORY_ROOT,
            capture_output=True,
            text=True,
            check=False,
        )
        self.assertEqual(build.returncode, 0, build.stdout + build.stderr)
        with tempfile.TemporaryDirectory() as temporary_directory:
            temporary_path = pathlib.Path(temporary_directory)
            (temporary_path / "ChoiceRegistryHarness.csproj").write_text(
                textwrap.dedent(
                    """
                    <Project Sdk="Microsoft.NET.Sdk">
                      <PropertyGroup>
                        <OutputType>Exe</OutputType>
                        <TargetFramework>net8.0</TargetFramework>
                        <ImplicitUsings>enable</ImplicitUsings>
                        <Nullable>enable</Nullable>
                      </PropertyGroup>
                    </Project>
                    """
                ).strip(),
                encoding="utf-8",
            )
            (temporary_path / "Program.cs").write_text(HARNESS_SOURCE, encoding="utf-8")
            harness = subprocess.run(
                ["dotnet", "run", "--project", str(temporary_path), "--", str(ASSEMBLY_PATH)],
                cwd=REPOSITORY_ROOT,
                capture_output=True,
                text=True,
                check=False,
            )

        self.assertEqual(harness.returncode, 0, harness.stdout + harness.stderr)


if __name__ == "__main__":
    unittest.main()
