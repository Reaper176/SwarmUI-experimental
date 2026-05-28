# Latent Tools Extension Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add a self-contained SwarmUI extension that registers `Machines-of-Disruption/latent-tools` as an installable ComfyUI node pack.

**Architecture:** The extension is an installer and feature-detection wrapper only. It registers one installable feature and maps a representative upstream node class to that feature flag, while leaving all latent graph behavior inside the upstream ComfyUI node pack.

**Tech Stack:** C# 12 extension entrypoint, SwarmUI `Extension`, `InstallableFeatures`, `ComfyUIBackendExtension.NodeToFeatureMap`, standard SwarmUI extension `.csproj`, Markdown README.

---

## File Structure

- Create `src/Extensions/SwarmUI-LatentTools/LatentToolsExtension.cs`
  - Extension entrypoint.
  - Registers the `latent_tools` installable feature.
  - Maps upstream node class `LTPreviewLatent` to feature id `latent_tools`.
- Create `src/Extensions/SwarmUI-LatentTools/SwarmUI-LatentTools.csproj`
  - Standard extension project file importing `../../SwarmUI.extension.props`.
- Create `src/Extensions/SwarmUI-LatentTools/README.md`
  - Documents the purpose, upstream repo, install behavior, and current scope.
- Modify no core SwarmUI files.
- Modify no `Data/`, `Output/`, `Models/`, `dlbackend/`, `src/bin`, or `src/obj` files.

## Task 1: Add Extension Entry Point

**Files:**
- Create: `src/Extensions/SwarmUI-LatentTools/LatentToolsExtension.cs`

Repository instructions say agents do not run tests or builds for SwarmUI. Use static validation for this task.

- [ ] **Step 1: Create the extension class**

Add this file:

```cs
using SwarmUI.Builtin_ComfyUIBackend;
using SwarmUI.Core;

namespace MachinesOfDisruption.Extensions.LatentTools;

public class LatentToolsExtension : Extension
{
    public const string FeatureId = "latent_tools";

    public override void OnInit()
    {
        InstallableFeatures.RegisterInstallableFeature(new("Latent Tools", FeatureId, "https://github.com/Machines-of-Disruption/latent-tools", "Machines-of-Disruption"));
        ComfyUIBackendExtension.NodeToFeatureMap["LTPreviewLatent"] = FeatureId;
    }
}
```

- [ ] **Step 2: Static-check extension conventions**

Run:

```bash
sed -n '1,120p' src/Extensions/SwarmUI-LatentTools/LatentToolsExtension.cs
```

Expected:

- Namespace does not start with `SwarmUI`.
- Class name matches file name.
- Class extends `Extension`.
- Feature id is lowercase and has no spaces.
- Installable feature URL is `https://github.com/Machines-of-Disruption/latent-tools`.
- Node mapping uses `LTPreviewLatent`.

- [ ] **Step 3: Commit**

Run:

```bash
git add src/Extensions/SwarmUI-LatentTools/LatentToolsExtension.cs
git commit -m "feat: add latent tools extension entrypoint"
```

## Task 2: Add Project File

**Files:**
- Create: `src/Extensions/SwarmUI-LatentTools/SwarmUI-LatentTools.csproj`

- [ ] **Step 1: Create the project file**

Add this file:

```xml
<Project Sdk="Microsoft.NET.Sdk.Web">
    <PropertyGroup>
        <AssemblyName>SwarmUI-LatentTools</AssemblyName>
    </PropertyGroup>
    <Import Project="../../SwarmUI.extension.props" />
</Project>
```

- [ ] **Step 2: Static-check project conventions**

Run:

```bash
sed -n '1,80p' src/Extensions/SwarmUI-LatentTools/SwarmUI-LatentTools.csproj
```

Expected:

- `Sdk` is `Microsoft.NET.Sdk.Web`.
- `AssemblyName` is `SwarmUI-LatentTools`.
- The shared extension props import is exactly `../../SwarmUI.extension.props`.

- [ ] **Step 3: Commit**

Run:

```bash
git add src/Extensions/SwarmUI-LatentTools/SwarmUI-LatentTools.csproj
git commit -m "build: add latent tools extension project"
```

## Task 3: Add README

**Files:**
- Create: `src/Extensions/SwarmUI-LatentTools/README.md`

- [ ] **Step 1: Create README**

Add this file:

```md
# SwarmUI Latent Tools

This extension registers the
[Machines-of-Disruption/latent-tools](https://github.com/Machines-of-Disruption/latent-tools)
ComfyUI custom node pack as an installable feature in SwarmUI.

Latent Tools provides ComfyUI nodes for previewing, generating, blending,
reshaping, concatenating, and mathematically manipulating latent tensors. It
also includes numeric helper nodes and a sampler variant that can accept
additional latent noise.

## Scope

This extension is currently an installer wrapper. It does not add new Generate
tab controls or modify SwarmUI-generated workflows.

After installing the feature, use the latent-tools nodes in ComfyUI workflows.

## Installation

1. Rebuild or relaunch SwarmUI so this extension is loaded.
2. Open SwarmUI's installable feature UI.
3. Install `Latent Tools`.
4. Restart or reload the ComfyUI backend if needed.
5. Confirm the latent-tools nodes are available in ComfyUI.

## Upstream

- Repository: https://github.com/Machines-of-Disruption/latent-tools
- Maintainer: Machines-of-Disruption
```

- [ ] **Step 2: Static-check README scope**

Run:

```bash
sed -n '1,160p' src/Extensions/SwarmUI-LatentTools/README.md
```

Expected:

- README says this is an installer wrapper.
- README does not claim Generate tab controls exist.
- README links the upstream repository.

- [ ] **Step 3: Commit**

Run:

```bash
git add src/Extensions/SwarmUI-LatentTools/README.md
git commit -m "docs: document latent tools extension"
```

## Task 4: Final Static Validation

**Files:**
- Inspect: `src/Extensions/SwarmUI-LatentTools/LatentToolsExtension.cs`
- Inspect: `src/Extensions/SwarmUI-LatentTools/SwarmUI-LatentTools.csproj`
- Inspect: `src/Extensions/SwarmUI-LatentTools/README.md`

- [ ] **Step 1: Confirm changed files**

Run:

```bash
git status --short
```

Expected:

- No unexpected edits outside the plan.
- `.superpowers/` may remain untracked from local tooling and should not be added.

- [ ] **Step 2: Search extension metadata**

Run:

```bash
rg "latent_tools|Latent Tools|LTPreviewLatent|Machines-of-Disruption/latent-tools" src/Extensions/SwarmUI-LatentTools
```

Expected:

- `LatentToolsExtension.cs` contains the feature id, display name, URL, and node mapping.
- `README.md` contains the display name and upstream URL.

- [ ] **Step 3: Confirm restricted paths were not touched**

Run:

```bash
git diff --name-only HEAD~3..HEAD
```

Expected:

- Only files under `src/Extensions/SwarmUI-LatentTools/` are listed for the implementation commits.
- No paths under `Data/`, `Output/`, `Models/`, `dlbackend/`, `src/bin`, or `src/obj`.

- [ ] **Step 4: Manual validation handoff**

Do not run a build or tests. Tell the developer:

```text
Per AGENTS.md, I did not run builds or tests. Please rebuild/relaunch SwarmUI, install Latent Tools from the installable feature UI, and confirm the latent-tools nodes appear in ComfyUI.
```
