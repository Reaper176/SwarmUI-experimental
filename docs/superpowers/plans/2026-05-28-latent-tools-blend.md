# Latent Tools Blend Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add optional `LTBlendLatent` support to mix Swarm's normal empty latent with the selected Latent Tools generated latent.

**Architecture:** Extend the existing latent init workflow step in `LatentToolsExtension.cs`; update `assets/latent_tools.js` to show blend ratio only when blend mode is active.

**Tech Stack:** C# 12, SwarmUI workflow generator, latent-tools `LTBlendLatent`, JavaScript Generate tab visibility helper.

---

## Task 1: Register Blend Params

**Files:**
- Modify: `src/Extensions/SwarmUI-LatentTools/LatentToolsExtension.cs`

- [ ] Add `LTBlendLatent` to `NodeToFeatureMap`.
- [ ] Add `[LatentTools] Blend Mode`.
- [ ] Add `[LatentTools] Blend Ratio`.

## Task 2: Apply Blend Node

**Files:**
- Modify: `src/Extensions/SwarmUI-LatentTools/LatentToolsExtension.cs`

- [ ] Preserve the current Swarm latent before replacing it.
- [ ] Create Gaussian or Uniform latent as before.
- [ ] If blend mode is enabled, create `LTBlendLatent`.
- [ ] Set `g.CurrentMedia` to the final latent output.

## Task 3: Update UI Visibility

**Files:**
- Modify: `src/Extensions/SwarmUI-LatentTools/assets/latent_tools.js`

- [ ] Show Blend Mode only when init mode is Gaussian or Uniform.
- [ ] Show Blend Ratio only when blend mode is not disabled.

## Task 4: Static Validation

- [ ] Run `rg "LTBlendLatent|Blend Mode|Blend Ratio|latenttoolsblend" src/Extensions/SwarmUI-LatentTools`.
- [ ] Run `git diff --name-only HEAD~1..HEAD` after commit.
- [ ] Do not run builds or tests per `AGENTS.md`.
