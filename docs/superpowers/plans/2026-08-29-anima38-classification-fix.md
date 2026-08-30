# Anima 3.8B Classification Fix Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Correctly classify released Anima 3.8B checkpoints as `anima-3_8b`, automatically repair stale cached Cosmos classifications, and thereby activate Swarm's existing native Anima workflow.

**Architecture:** Extract reusable Anima tensor-signature helpers in the core classifier, use them to narrowly override the released checkpoint's incorrect Cosmos Predict2 architecture metadata, and make the overlapping Cosmos tensor predicate mutually exclusive. Add a versioned, Cosmos-only metadata-cache recheck so existing indexed models are corrected once without rescanning unrelated models.

**Tech Stack:** C# 12/.NET 8, Newtonsoft.Json `JObject`, LiteDB model metadata, Python `unittest`, Roslyn behavior harnesses.

---

## File Map

- Create `src/BuiltinExtensions/ComfyUIBackend/ExtraNodes/SwarmComfyCommon/tests/Anima38ClassificationHarness.cs.txt`: exercise production classification with representative Anima and Cosmos headers.
- Create `src/BuiltinExtensions/ComfyUIBackend/ExtraNodes/SwarmComfyCommon/tests/test_anima38_classification.py`: build and execute the C# behavior harness.
- Modify `src/Text2Image/T2IModelClassSorter.cs`: share Anima signature detection, override only the incorrect Cosmos architecture ID when the signature proves Anima, and exclude Anima from the Cosmos predicate.
- Modify `src/Text2Image/T2IModelHandler.cs`: version affected cached classifications and trigger a one-time targeted recheck.

### Task 1: Reproduce the metadata and tensor-predicate classification collision

**Files:**
- Create: `src/BuiltinExtensions/ComfyUIBackend/ExtraNodes/SwarmComfyCommon/tests/Anima38ClassificationHarness.cs.txt`
- Create: `src/BuiltinExtensions/ComfyUIBackend/ExtraNodes/SwarmComfyCommon/tests/test_anima38_classification.py`

- [ ] **Step 1: Write the production behavior harness**

Create `Anima38ClassificationHarness.cs.txt` with representative headers that deliberately include the real overlap:

```csharp
using System;
using Newtonsoft.Json.Linq;
using SwarmUI.Text2Image;

internal static class Anima38ClassificationHarness
{
    private const string CosmosID = "nvidia-cosmos-predict2-t2i-14b";

    private static JObject Tensor()
    {
        return new JObject { ["shape"] = new JArray(1) };
    }

    private static JObject CosmosHeader(bool declaredArchitecture)
    {
        JObject header = new()
        {
            ["net.blocks.0.adaln_modulation_cross_attn.1.weight"] = Tensor(),
            ["net.pos_embedder.dim_temporal_range"] = Tensor(),
            ["net.x_embedder.proj.1.weight"] = Tensor(),
            ["net.blocks.35.adaln_modulation_mlp.2.weight"] = Tensor()
        };
        if (declaredArchitecture)
        {
            header["__metadata__"] = new JObject { ["modelspec.architecture"] = CosmosID };
        }
        return header;
    }

    private static JObject Anima38Header(bool declaredArchitecture)
    {
        JObject header = CosmosHeader(declaredArchitecture);
        header["net.t_embedder.1.linear_2.weight"] = Tensor();
        header["net.llm_adapter.blocks.0.self_attn.v_proj.weight"] = Tensor();
        header["net.blocks.27.adaln_modulation_cross_attn.2.weight"] = Tensor();
        header["net.blocks.51.adaln_modulation_cross_attn.2.weight"] = Tensor();
        return header;
    }

    private static string Identify(string name, JObject header)
    {
        T2IModel model = new(null, "", $"{name}.safetensors", $"{name}.safetensors");
        return T2IModelClassSorter.IdentifyClassFor(model, header, "Stable-Diffusion")?.ID;
    }

    private static void Expect(string name, string expected, string actual)
    {
        if (actual != expected)
        {
            throw new InvalidOperationException($"{name}: expected {expected}, got {actual ?? "null"}");
        }
    }

    public static int Main()
    {
        if (T2IModelClassSorter.ModelClasses.Count == 0)
        {
            T2IModelClassSorter.Init();
        }
        Expect("declared Anima", "anima-3_8b", Identify("declared-anima", Anima38Header(true)));
        Expect("signature Anima", "anima-3_8b", Identify("signature-anima", Anima38Header(false)));
        Expect("declared Cosmos", CosmosID, Identify("declared-cosmos", CosmosHeader(true)));
        Expect("signature Cosmos", CosmosID, Identify("signature-cosmos", CosmosHeader(false)));
        return 0;
    }
}
```

- [ ] **Step 2: Write the Python harness runner**

Create `test_anima38_classification.py`:

```python
import pathlib
import shutil
import subprocess
import tempfile
import unittest


REPOSITORY_ROOT = pathlib.Path(__file__).resolve().parents[6]
OUTPUT_DIR = REPOSITORY_ROOT / "src/bin/Debug/net8.0"
HARNESS_SOURCE = pathlib.Path(__file__).with_name("Anima38ClassificationHarness.cs.txt")


class Anima38ClassificationTests(unittest.TestCase):
    def test_production_classifier_distinguishes_anima38_from_cosmos(self):
        build = subprocess.run(
            ["dotnet", "build", "src/SwarmUI.csproj", "--no-restore"],
            cwd=REPOSITORY_ROOT,
            capture_output=True,
            text=True,
            check=False,
        )
        self.assertEqual(build.returncode, 0, build.stdout + build.stderr)
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
            command = [
                "dotnet",
                str(compiler),
                "-noconfig",
                "-nostdlib",
                "-langversion:latest",
                "-target:exe",
                f"-out:{harness_dll}",
                *[f"-r:{path}" for path in sorted(reference_dir.glob("*.dll"))],
                f"-r:{OUTPUT_DIR / 'SwarmUI.dll'}",
                f"-r:{OUTPUT_DIR / 'Newtonsoft.Json.dll'}",
                str(HARNESS_SOURCE),
            ]
            compiled = subprocess.run(command, capture_output=True, text=True, check=False)
            self.assertEqual(compiled.returncode, 0, compiled.stdout + compiled.stderr)
            for source in OUTPUT_DIR.glob("*.dll"):
                shutil.copy2(source, temp_dir / source.name)
            executed = subprocess.run(
                [
                    "dotnet",
                    "exec",
                    "--runtimeconfig",
                    str(OUTPUT_DIR / "SwarmUI.runtimeconfig.json"),
                    "--depsfile",
                    str(OUTPUT_DIR / "SwarmUI.deps.json"),
                    str(harness_dll),
                ],
                cwd=temp_dir,
                capture_output=True,
                text=True,
                check=False,
            )
        self.assertEqual(executed.returncode, 0, executed.stdout + executed.stderr)


if __name__ == "__main__":
    unittest.main()
```

- [ ] **Step 3: Run the new test and verify the expected failure**

Run:

```bash
python3 -m unittest src/BuiltinExtensions/ComfyUIBackend/ExtraNodes/SwarmComfyCommon/tests/test_anima38_classification.py -v
```

Expected: `FAIL`; the harness reports that `declared Anima` resolved to `nvidia-cosmos-predict2-t2i-14b` rather than `anima-3_8b`.

- [ ] **Step 4: Commit the failing regression test**

```bash
git add src/BuiltinExtensions/ComfyUIBackend/ExtraNodes/SwarmComfyCommon/tests/Anima38ClassificationHarness.cs.txt src/BuiltinExtensions/ComfyUIBackend/ExtraNodes/SwarmComfyCommon/tests/test_anima38_classification.py
git commit -m "Test Anima Cosmos classification collision"
```

### Task 2: Make Anima and Cosmos classification mutually exclusive

**Files:**
- Modify: `src/Text2Image/T2IModelClassSorter.cs:9-30,125-135,224-225,278-280,1024-1085`
- Test: `src/BuiltinExtensions/ComfyUIBackend/ExtraNodes/SwarmComfyCommon/tests/test_anima38_classification.py`

- [ ] **Step 1: Extract reusable Anima signature helpers**

Add these private helpers near the top of `T2IModelClassSorter`:

```csharp
    private static bool HasModelKey(JObject header, string key)
    {
        return header.ContainsKey(key)
            || header.ContainsKey($"diffusion_model.{key}")
            || header.ContainsKey($"model.diffusion_model.{key}")
            || header.ContainsKey($"net.{key}")
            || header.ContainsKey($"transformer.{key}");
    }

    private static bool IsAnima(JObject header)
    {
        return HasModelKey(header, "t_embedder.1.linear_2.weight")
            && HasModelKey(header, "llm_adapter.blocks.0.self_attn.v_proj.weight")
            && HasModelKey(header, "blocks.27.adaln_modulation_cross_attn.2.weight");
    }

    private static bool IsAnima38(JObject header)
    {
        return IsAnima(header) && HasModelKey(header, "blocks.51.adaln_modulation_cross_attn.2.weight");
    }
```

Inside `Init`, replace the local `hasKey` body with `HasModelKey(h, key)`, remove the local `isAnima` and `isAnima38` functions, and update the Anima registrations to call `IsAnima` and `IsAnima38`.

- [ ] **Step 2: Exclude Anima from the overlapping Cosmos predicate**

Change `isCosmosPredict2_14B` to:

```csharp
        bool isCosmosPredict2_14B(JObject h) => h.ContainsKey("net.blocks.0.adaln_modulation_cross_attn.1.weight")
            && h.ContainsKey("net.pos_embedder.dim_temporal_range")
            && h.ContainsKey("net.x_embedder.proj.1.weight")
            && h.ContainsKey("net.blocks.35.adaln_modulation_mlp.2.weight")
            && !IsAnima(h);
```

- [ ] **Step 3: Narrowly disregard the released checkpoint's incorrect architecture ID**

After remaps are applied in `IdentifyClassFor`, compute whether the embedded ID must be ignored:

```csharp
            bool isMisdeclaredAnima = arch == CompatCosmosPredict2_14b.ID && IsAnima(header);
            if (isMisdeclaredAnima)
            {
                Logs.Debug($"{modelType} Model {model.Name} declares Cosmos Predict2 14B but has an Anima tensor signature; using tensor classification");
            }
            else if (ModelClasses.TryGetValue(arch, out T2IModelClass clazz))
```

Keep the existing resolution and unknown-architecture branches under this `else if` chain. This override must compare only `CompatCosmosPredict2_14b.ID`; do not generalize it to other metadata IDs.

- [ ] **Step 4: Build and rerun the focused classifier test**

Run:

```bash
dotnet build src/SwarmUI.csproj --no-restore
python3 -m unittest src/BuiltinExtensions/ComfyUIBackend/ExtraNodes/SwarmComfyCommon/tests/test_anima38_classification.py -v
```

Expected: build succeeds with 0 errors and the test reports `OK` for all four classification cases.

- [ ] **Step 5: Commit the classifier correction**

```bash
git add src/Text2Image/T2IModelClassSorter.cs
git commit -m "Fix Anima 3.8B model classification"
```

### Task 3: Recheck stale cached Cosmos classifications once

**Files:**
- Modify: `src/BuiltinExtensions/ComfyUIBackend/ExtraNodes/SwarmComfyCommon/tests/Anima38ClassificationHarness.cs.txt`
- Modify: `src/Text2Image/T2IModelHandler.cs:95-120,410-440,540-552,790-830`

- [ ] **Step 1: Extend the harness with cache-revision expectations**

Add `using System.Reflection;` and this method to the harness:

```csharp
    private static void VerifyCacheRevision()
    {
        Type handler = typeof(T2IModelHandler);
        FieldInfo revisionField = handler.GetField("ModelClassCacheRevision", BindingFlags.Static | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("ModelClassCacheRevision is missing");
        MethodInfo staleMethod = handler.GetMethod("IsModelClassCacheStale", BindingFlags.Static | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("IsModelClassCacheStale is missing");
        int revision = (int)revisionField.GetValue(null);
        T2IModelHandler.ModelMetadataStore staleCosmos = new()
        {
            ModelClassType = CosmosID,
            ModelClassRevision = revision - 1
        };
        T2IModelHandler.ModelMetadataStore freshCosmos = new()
        {
            ModelClassType = CosmosID,
            ModelClassRevision = revision
        };
        T2IModelHandler.ModelMetadataStore staleAnima = new()
        {
            ModelClassType = "anima-3_8b",
            ModelClassRevision = revision - 1
        };
        bool IsStale(T2IModelHandler.ModelMetadataStore value)
        {
            return (bool)staleMethod.Invoke(null, new object[] { value });
        }
        if (!IsStale(staleCosmos) || IsStale(freshCosmos) || IsStale(staleAnima))
        {
            throw new InvalidOperationException("The targeted model-class cache revision policy is incorrect");
        }
    }
```

Call `VerifyCacheRevision();` before returning from `Main`.

- [ ] **Step 2: Run the focused test and verify the second expected failure**

Run:

```bash
python3 -m unittest src/BuiltinExtensions/ComfyUIBackend/ExtraNodes/SwarmComfyCommon/tests/test_anima38_classification.py -v
```

Expected: `FAIL`; the harness reports `ModelClassCacheRevision is missing`.

- [ ] **Step 3: Add the targeted cache revision policy**

Add to `T2IModelHandler`:

```csharp
    /// <summary>Revision of model-class cache decisions that require targeted re-evaluation.</summary>
    private const int ModelClassCacheRevision = 1;

    private static bool IsModelClassCacheStale(ModelMetadataStore metadata)
    {
        return metadata is not null
            && metadata.ModelClassRevision < ModelClassCacheRevision
            && metadata.ModelClassType == T2IModelClassSorter.CompatCosmosPredict2_14b.ID;
    }
```

Add this persisted property to `ModelMetadataStore`:

```csharp
        /// <summary>Revision of the classifier decision stored for this model.</summary>
        public int ModelClassRevision { get; set; }
```

In `ResetMetadataFrom`, set:

```csharp
                metadata.ModelClassRevision = ModelClassCacheRevision;
```

Before the existing metadata-invalidity condition in `LoadMetadata`, add:

```csharp
        if (IsModelClassCacheStale(metadata))
        {
            Logs.Debug($"Rechecking stale model classification for {model.Name}");
            metadata = null;
        }
```

When creating a new `ModelMetadataStore`, set:

```csharp
                ModelClassRevision = ModelClassCacheRevision,
```

- [ ] **Step 4: Build and rerun the focused test**

Run:

```bash
dotnet build src/SwarmUI.csproj --no-restore
python3 -m unittest src/BuiltinExtensions/ComfyUIBackend/ExtraNodes/SwarmComfyCommon/tests/test_anima38_classification.py -v
```

Expected: build succeeds with 0 errors; classifier and cache-revision behavior both report `OK`.

- [ ] **Step 5: Commit the cache correction**

```bash
git add src/Text2Image/T2IModelHandler.cs src/BuiltinExtensions/ComfyUIBackend/ExtraNodes/SwarmComfyCommon/tests/Anima38ClassificationHarness.cs.txt
git commit -m "Refresh stale Anima model classifications"
```

### Task 4: Verify the complete Anima path and hand off live validation

**Files:**
- Test: `src/BuiltinExtensions/ComfyUIBackend/ExtraNodes/SwarmComfyCommon/tests/test_anima38_classification.py`
- Test: `src/BuiltinExtensions/ComfyUIBackend/ExtraNodes/SwarmComfyCommon/tests/test_anima38_workflow_integration.py`
- Test: `src/BuiltinExtensions/ComfyUIBackend/ExtraNodes/SwarmComfyCommon/tests/test_anima38_registration.py`

- [ ] **Step 1: Run formatting and static checks on changed files**

Run:

```bash
dotnet format src/SwarmUI.csproj --no-restore --verify-no-changes --include src/Text2Image/T2IModelClassSorter.cs src/Text2Image/T2IModelHandler.cs
python3 -m py_compile src/BuiltinExtensions/ComfyUIBackend/ExtraNodes/SwarmComfyCommon/tests/test_anima38_classification.py
git diff --check
```

Expected: all commands exit 0 with no formatting or syntax errors.

- [ ] **Step 2: Run the complete existing Anima suite plus the new regression**

Run:

```bash
dotnet build src/SwarmUI.csproj --no-restore
python3 -m unittest discover -s src/BuiltinExtensions/ComfyUIBackend/ExtraNodes/SwarmComfyCommon/tests -p 'test_anima38*.py'
```

Expected: build succeeds with 0 errors and all 97 Anima tests pass.

- [ ] **Step 3: Inspect the final diff and commits**

Run:

```bash
git diff --check master...HEAD
git diff --stat master...HEAD
git log --oneline master..HEAD
```

Expected: only the two core C# files, the new test runner, the new harness, and this plan are changed; commits correspond to the regression, classifier correction, cache correction, and plan.

- [ ] **Step 4: Perform live verification after integration and Swarm restart**

After merging the branch, building the main worktree, and restarting Swarm, refresh the Stable-Diffusion model list and verify `anima38B_base.safetensors` reports architecture `anima-3_8b`. Export a workflow from Generate and confirm it contains:

```text
SwarmLoadAnima38Qwen35
SwarmAnima38Conditioning
```

and does not contain:

```text
old_t5xxl_cosmos.safetensors
```

Then run one image generation and confirm the backend logs load the Anima Qwen3.5 encoder and Anima adapter rather than `CosmosTEModel_`.
