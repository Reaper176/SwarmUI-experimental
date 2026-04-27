# Image Editing Healing Brush Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add a simple local healing brush to the Image Editing tab that uses an `Alt`-click sample point and softly blends sampled pixels back onto the active layer.

**Architecture:** Implement the healing brush as a new editor tool in the existing image-editor helper stack, reusing the brush-like pointer flow, selection clipping, and preview buffer pattern already used by `ImageEditorToolBrush`. Keep it entirely frontend-local and layer-local, with no backend or AI dependency.

**Tech Stack:** Browser JavaScript, existing canvas-based image editor, existing SwarmUI Image Editing tab UI

---

### Task 1: Add The Healing Tool Skeleton

**Files:**
- Modify: `src/wwwroot/js/genpage/helpers/image_editor_tools.js`
- Modify: `src/wwwroot/js/genpage/helpers/image_editor.js`

- [ ] **Step 1: Add a healing tool class**

Create `ImageEditorToolHealing` near the other painting tools in `src/wwwroot/js/genpage/helpers/image_editor_tools.js`.

- [ ] **Step 2: Reuse existing brush-style config**

Give it radius and opacity controls, plus a short status label for whether a source has been set.

- [ ] **Step 3: Register the tool**

Add it in `src/wwwroot/js/genpage/helpers/image_editor.js` near brush/eraser registration:

```js
this.addTool(new ImageEditorToolHealing(this));
```

- [ ] **Step 4: Give it a hotkey and description**

Use a distinct id and description such as:

```js
super(editor, 'healing', 'paintbrush', 'Healing Brush', 'Blend sampled pixels into the active layer.\nAlt-click to set sample source.\nHotKey: H', 'h');
```

### Task 2: Implement Sample Source And Painting Flow

**Files:**
- Modify: `src/wwwroot/js/genpage/helpers/image_editor_tools.js`

- [ ] **Step 1: Add source-point state**

Track source layer coordinates, whether a source exists, and the current stroke offset from sample source.

- [ ] **Step 2: Support `Alt`-click sample placement**

In `onMouseDown(e)`, if `e.altKey` is set, store the source point and return without painting.

- [ ] **Step 3: Start a healing stroke**

When a normal stroke starts:
- require an active layer
- require a sample source
- clone the active layer into a buffer
- capture the offset between sample source and stroke start

- [ ] **Step 4: Blend sampled pixels during stroke**

Sample pixels from the source region on the active layer canvas and paint them onto a stroke layer using a soft circular mask, then composite into the preview buffer.

- [ ] **Step 5: Commit on pointer release**

Follow the same preview-to-layer commit pattern used by `ImageEditorToolBrush`.

### Task 3: Add Overlay Feedback

**Files:**
- Modify: `src/wwwroot/js/genpage/helpers/image_editor_tools.js`

- [ ] **Step 1: Draw source marker**

When the healing tool is active and a sample source exists, draw a small crosshair/ring at the source position.

- [ ] **Step 2: Draw linked offset preview while brushing**

While painting, draw a line from the cursor to the sampled source position so the relationship is visible.

- [ ] **Step 3: Update the status label**

Show `No sample set` until `Alt`-click defines one, then show `Sample set`.

### Task 4: Static Verification

**Files:**
- Modify: `src/wwwroot/js/genpage/helpers/image_editor_tools.js`
- Modify: `src/wwwroot/js/genpage/helpers/image_editor.js`

- [ ] **Step 1: Run syntax verification**

Run: `node --check src/wwwroot/js/genpage/helpers/image_editor_tools.js`

Expected: no output

Run: `node --check src/wwwroot/js/genpage/helpers/image_editor.js`

Expected: no output

- [ ] **Step 2: Review final diff**

Run: `git diff -- src/wwwroot/js/genpage/helpers/image_editor_tools.js src/wwwroot/js/genpage/helpers/image_editor.js`

Expected: only the new healing tool and its registration are changed

- [ ] **Step 3: Hand off manual verification**

Manual verification for the developer:
- Open `Image Editing`
- Activate `Healing Brush`
- `Alt`-click to set a source point
- Paint nearby and verify pixels are copied and softly blended
- Confirm selection clipping still limits the stroke when a selection exists

