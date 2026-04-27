# Spot Heal Improvements Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Improve the browser-side `Spot Heal` brush so it picks better source patches and blends them more cleanly without adding a heavier fill or backend-assisted system.

**Architecture:** Extend the existing `spot_heal` preset logic inside `ImageEditorToolBrush` with stronger patch scoring and softer edge-weighted blending. Keep all work inside the current canvas-based editor and expose any new spot-heal-specific knobs in the existing `Pen Options` section only when the `Spot Heal` preset is active.

**Tech Stack:** Browser JavaScript, HTML canvas image data operations, existing SwarmUI image editor helpers

---

### Task 1: Improve Candidate Patch Scoring

**Files:**
- Modify: `src/wwwroot/js/genpage/helpers/image_editor_tools.js`

- [ ] **Step 1: Add edge-aware neighborhood sampling**

Expand `scoreSpotHealOffset(...)` so it scores not just RGB difference, but also simple local structure by comparing luminance differences to neighboring pixels near the stroke boundary.

- [ ] **Step 2: Prefer offsets that stay on the same side of strong edges**

Penalize candidate offsets when the source patch crosses sharp luminance changes that do not match the target boundary neighborhood.

- [ ] **Step 3: Keep search bounded and fast**

Retain the existing local radius search and coarse stepping so the brush still feels immediate.

### Task 2: Improve Blending Quality

**Files:**
- Modify: `src/wwwroot/js/genpage/helpers/image_editor_tools.js`

- [ ] **Step 1: Replace flat alpha blend with feathered radial blend**

Use a soft edge falloff across the painted mask instead of a uniform alpha mix so the healed patch merges more naturally.

- [ ] **Step 2: Blend texture while preserving coarse tone**

For each healed pixel, mix sampled detail into the destination using the feather weight rather than a flat overwrite.

- [ ] **Step 3: Keep the operation local**

Only modify pixels inside the painted stroke mask and leave the rest of the layer untouched.

### Task 3: Expose One Additional Spot-Heal Option

**Files:**
- Modify: `src/wwwroot/js/genpage/helpers/image_editor_tools.js`
- Modify: `src/wwwroot/js/genpage/gentab/currentimagehandler.js`

- [ ] **Step 1: Add a `Blend Softness` control**

Show it only when `Spot Heal` is the active preset in the `Pen Options` section.

- [ ] **Step 2: Wire the control into blending**

Use the control to tune the mask falloff / feather strength in the improved blend path.

### Task 4: Static Verification

**Files:**
- Modify: `src/wwwroot/js/genpage/helpers/image_editor_tools.js`
- Modify: `src/wwwroot/js/genpage/gentab/currentimagehandler.js`

- [ ] **Step 1: Run syntax checks**

Run: `node --check src/wwwroot/js/genpage/helpers/image_editor_tools.js`

Expected: no output

Run: `node --check src/wwwroot/js/genpage/gentab/currentimagehandler.js`

Expected: no output

- [ ] **Step 2: Review final diff**

Run: `git diff -- src/wwwroot/js/genpage/helpers/image_editor_tools.js src/wwwroot/js/genpage/gentab/currentimagehandler.js`

Expected: only spot-heal scoring, blending, and pen-options wiring changes appear

