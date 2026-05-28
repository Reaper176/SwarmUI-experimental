# Latent Tools Latent Init Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add Generate tab controls that replace the initial empty image latent with `LTGaussianLatent` or `LTUniformLatent`.

**Architecture:** Keep the integration entirely inside `LatentToolsExtension.cs`. Register a small parameter group, then add one workflow step after the initial media step and before sampling.

**Tech Stack:** C# 12, SwarmUI `T2IParamTypes`, ComfyUI `WorkflowGenerator`, latent-tools Comfy nodes.

---

## Task 1: Register Parameters

**Files:**
- Modify: `src/Extensions/SwarmUI-LatentTools/LatentToolsExtension.cs`

- [ ] Replace the internal marker parameter with real latent init parameters.
- [ ] Keep `ScriptFiles.Add("assets/latent_tools.js")` and the `LTPreviewLatent` feature mapping.
- [ ] Static-check that the mode parameter is default-disabled.

## Task 2: Add Workflow Step

**Files:**
- Modify: `src/Extensions/SwarmUI-LatentTools/LatentToolsExtension.cs`

- [ ] Add a `WorkflowGenerator.AddStep` after initial media creation.
- [ ] Skip when mode is disabled.
- [ ] Throw `SwarmUserErrorException` when mode is enabled but `latent_tools` is not installed.
- [ ] Skip when `Init Image` is set.
- [ ] Skip when `g.CurrentMedia.DataType` is not `WGNodeData.DT_LATENT_IMAGE`.
- [ ] Create `LTGaussianLatent` or `LTUniformLatent`.
- [ ] Replace `g.CurrentMedia` with the node output.

## Task 3: Static Validation

**Files:**
- Inspect: `src/Extensions/SwarmUI-LatentTools/LatentToolsExtension.cs`
- Inspect: `src/Extensions/SwarmUI-LatentTools/assets/latent_tools.js`

- [ ] Run `rg "LTGaussianLatent|LTUniformLatent|LatentInit|Init Image|latent_tools" src/Extensions/SwarmUI-LatentTools`.
- [ ] Run `git diff --name-only HEAD~1..HEAD` after commit.
- [ ] Do not run builds or tests per `AGENTS.md`.
