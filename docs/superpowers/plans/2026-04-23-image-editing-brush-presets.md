# Image Editing Brush Presets Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add a `Brush Preset` selector to the Image Editing tab `Pen Options` section and implement lightweight brush behavior modes that make 10 common Krita-style presets feel distinct without replacing the current brush engine.

**Architecture:** Extend `ImageEditorToolBrush` with a small mode system and preset definitions, then expose preset selection through the existing Image Editing tab sidebar mount used for pen options. Keep the change local to the image editor frontend and reuse the existing brush/eraser tools rather than introducing new tool classes or a separate brush engine.

**Tech Stack:** Razor partials, browser JavaScript, CSS, existing SwarmUI image editor helpers

---

### Task 1: Add Brush Mode And Preset Model

**Files:**
- Modify: `src/wwwroot/js/genpage/helpers/image_editor_tools.js`

- [ ] **Step 1: Add preset and mode fields to the brush tool**

Add brush state for preset id, mode, and mode-specific settings near the existing pressure fields in `ImageEditorToolBrush`.

- [ ] **Step 2: Add preset definitions**

Define a small static preset table on `ImageEditorToolBrush` for 10 presets:

```js
ImageEditorToolBrush.PRESETS = {
    hard_round: { name: 'Hard Round', mode: 'hard_round', radius: 10, opacity: 1, pressureAffectsSize: true, pressureAffectsOpacity: false, pressureMin: 0.2, pressureCurve: 1 },
    soft_round: { name: 'Soft Round', mode: 'soft_round', radius: 18, opacity: 0.65, pressureAffectsSize: true, pressureAffectsOpacity: true, pressureMin: 0.15, pressureCurve: 1.2 },
    airbrush: { name: 'Airbrush', mode: 'airbrush', radius: 26, opacity: 0.12, pressureAffectsSize: false, pressureAffectsOpacity: true, pressureMin: 0.08, pressureCurve: 1.6 },
    pixel: { name: 'Pixel', mode: 'pixel', radius: 1, opacity: 1, pressureAffectsSize: false, pressureAffectsOpacity: false, pressureMin: 1, pressureCurve: 1 },
    ink_fine: { name: 'Ink Fine', mode: 'ink', radius: 3, opacity: 1, pressureAffectsSize: true, pressureAffectsOpacity: false, pressureMin: 0.1, pressureCurve: 0.85 },
    ink_thick: { name: 'Ink Thick', mode: 'ink', radius: 8, opacity: 1, pressureAffectsSize: true, pressureAffectsOpacity: false, pressureMin: 0.12, pressureCurve: 0.9 },
    marker: { name: 'Marker', mode: 'marker', radius: 20, opacity: 0.45, pressureAffectsSize: false, pressureAffectsOpacity: false, pressureMin: 1, pressureCurve: 1 },
    chalk: { name: 'Chalk', mode: 'chalk', radius: 14, opacity: 0.55, pressureAffectsSize: true, pressureAffectsOpacity: true, pressureMin: 0.18, pressureCurve: 1.1 },
    smudge_soft: { name: 'Smudge-like Soft', mode: 'soft_round', radius: 22, opacity: 0.22, pressureAffectsSize: true, pressureAffectsOpacity: true, pressureMin: 0.1, pressureCurve: 1.4 },
    eraser_soft: { name: 'Eraser Soft', mode: 'soft_round', radius: 24, opacity: 0.45, pressureAffectsSize: true, pressureAffectsOpacity: true, pressureMin: 0.18, pressureCurve: 1.2 }
};
```

- [ ] **Step 3: Add preset application helpers**

Implement methods like:

```js
applyPreset(presetId) { ... }
syncBrushConfigInputs() { ... }
getPresetList() { ... }
```

These should update the existing radius/opacity/pressure controls and queue an overlay redraw.

- [ ] **Step 4: Commit**

```bash
git add src/wwwroot/js/genpage/helpers/image_editor_tools.js
git commit -m "feat: add image editor brush preset model"
```

### Task 2: Implement Lightweight Brush Modes

**Files:**
- Modify: `src/wwwroot/js/genpage/helpers/image_editor_tools.js`
- Reference: `src/wwwroot/js/genpage/helpers/image_editor.js`

- [ ] **Step 1: Add mode-aware stamp helpers**

Add focused helpers in `ImageEditorToolBrush` such as:

```js
drawBrushStamp(ctx, x, y, radius, color, opacityForce) { ... }
drawSoftStamp(ctx, x, y, radius, color) { ... }
drawAirbrushStamp(ctx, x, y, radius, color, opacityForce) { ... }
```

- [ ] **Step 2: Update stroke rendering to use modes**

Replace the current fixed `drawFilledCircle` calls inside `brush(sizeForce, opacityForce)` with a switch on `this.brushMode`:

```js
if (this.brushMode == 'pixel') { ... }
else if (this.brushMode == 'soft_round') { ... }
else if (this.brushMode == 'airbrush') { ... }
else if (this.brushMode == 'marker') { ... }
else if (this.brushMode == 'ink') { ... }
else if (this.brushMode == 'chalk') { ... }
else { ... }
```

Use the existing line-between-points flow so pointer and pressure behavior remain intact.

- [ ] **Step 3: Keep eraser support explicit**

Ensure eraser presets still use `destination-out` and that `eraser_soft` maps to the same soft-round renderer but with eraser compositing.

- [ ] **Step 4: Run syntax verification**

Run: `node --check src/wwwroot/js/genpage/helpers/image_editor_tools.js`

Expected: no output

- [ ] **Step 5: Commit**

```bash
git add src/wwwroot/js/genpage/helpers/image_editor_tools.js
git commit -m "feat: add lightweight image editor brush modes"
```

### Task 3: Expose Preset Selection In Pen Options

**Files:**
- Modify: `src/wwwroot/js/genpage/helpers/image_editor_tools.js`
- Modify: `src/wwwroot/js/genpage/gentab/currentimagehandler.js`
- Modify: `src/wwwroot/css/genpage.css`

- [ ] **Step 1: Add preset selector UI to brush config**

Insert a preset dropdown block into the brush tool config markup:

```html
<div class="image-editor-tool-block tool-block-nogrow id-preset-block">
    <label>Brush:&nbsp;</label>
    <select class="id-brush-preset" style="width: 170px;"></select>
</div>
```

- [ ] **Step 2: Wire preset selector behavior**

Populate the dropdown from `ImageEditorToolBrush.PRESETS`, listen for `change`, and call `applyPreset`.

- [ ] **Step 3: Mount preset UI into the left sidebar pen section**

Update `imageEditingSetupPenOptions()` so it moves the preset block alongside the existing pen controls into the Image Editing sidebar mount.

- [ ] **Step 4: Add minimal styling**

Add CSS so the preset selector and moved pen controls stack cleanly in the left sidebar section at narrow widths.

- [ ] **Step 5: Run syntax verification**

Run: `node --check src/wwwroot/js/genpage/gentab/currentimagehandler.js`

Expected: no output

- [ ] **Step 6: Commit**

```bash
git add src/wwwroot/js/genpage/helpers/image_editor_tools.js src/wwwroot/js/genpage/gentab/currentimagehandler.js src/wwwroot/css/genpage.css
git commit -m "feat: add image editing brush preset controls"
```

### Task 4: Final Static Verification

**Files:**
- Modify: `src/wwwroot/js/genpage/helpers/image_editor_tools.js`
- Modify: `src/wwwroot/js/genpage/gentab/currentimagehandler.js`
- Modify: `src/wwwroot/css/genpage.css`
- Modify: `src/Pages/_Generate/ImageEditingTab.cshtml`

- [ ] **Step 1: Review the final diff**

Run: `git diff -- src/Pages/_Generate/ImageEditingTab.cshtml src/wwwroot/css/genpage.css src/wwwroot/js/genpage/gentab/currentimagehandler.js src/wwwroot/js/genpage/helpers/image_editor_tools.js`

Expected: only the intended Image Editing tab and brush preset changes appear

- [ ] **Step 2: Re-run syntax checks**

Run: `node --check src/wwwroot/js/genpage/helpers/image_editor_tools.js`

Expected: no output

Run: `node --check src/wwwroot/js/genpage/gentab/currentimagehandler.js`

Expected: no output

- [ ] **Step 3: Hand off manual verification**

Manual verification for the developer:
- Open `Image Editing`
- Confirm `Pen Options` is directly under `Editor Tools`
- Select `brush` and `eraser` and confirm the preset dropdown appears
- Switch through the 10 presets and confirm the controls update
- Draw with mouse and pen to confirm distinct feel across hard round, soft round, airbrush, pixel, marker, ink, chalk, and soft eraser

