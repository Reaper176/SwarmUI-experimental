# Image Editing Icon Rail Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace the Image Editing tab's long text-button tool list with a compact grouped icon rail and contextual options panel while preserving existing editor behavior.

**Architecture:** Keep the existing `ImageEditor` tool system intact and build a tab-specific presentation layer around it in `currentimagehandler.js`. The Razor partial provides stable mount points; JavaScript moves/builds grouped controls into those mounts; CSS handles the editor-like rail, context panel, and cleaned-up layers panel.

**Tech Stack:** C# Razor Pages for static tab markup, browser JavaScript in the existing genpage style, CSS in `src/wwwroot/css/genpage.css`, existing PNG icons from `src/wwwroot/imgs`.

---

## File Structure

- Modify `src/Pages/_Generate/ImageEditingTab.cshtml`
  - Replace the left sidebar's single stacked input column with a two-column `imageediting_tool_workspace`.
  - Preserve existing control IDs for layer options, image options, selection/crop, effects, color selector, and action containers.
  - Add new mount IDs for the icon rail and context panel.

- Modify `src/wwwroot/js/genpage/gentab/currentimagehandler.js`
  - Add grouped tool metadata for Paint, Select, Transform, and AI Mask.
  - Build icon buttons from existing `ImageEditorTool` metadata.
  - Refresh active states for both the new icon rail and existing hidden logical button maps.
  - Route active tool changes to the context panel by showing the relevant existing control sections.
  - Keep existing layer, crop, selection, effect, color, zoom, and send-to-generate logic intact.

- Modify `src/wwwroot/css/genpage.css`
  - Style the left workspace, icon rail, group labels, icon buttons, context panel, and layer panel cleanup.
  - Keep theme variables and existing SwarmUI visual language.

- Do not modify `src/wwwroot/js/genpage/helpers/image_editor.js` or `src/wwwroot/js/genpage/helpers/image_editor_tools.js` unless implementation reveals a missing metadata hook. Existing `tool.icon`, `tool.name`, `tool.description`, and `tool.hotkey` should be enough.

## Repository Constraints

AGENTS.md overrides generic TDD/build guidance:

- Do not run builds.
- Do not run automated tests.
- Use static checks and manual live-software verification.
- Keep changes minimal and localized.
- JavaScript uses `let`, full braced blocks, and no inline `if` bodies.

## Task 1: Add Left Workspace Mount Points

**Files:**
- Modify: `src/Pages/_Generate/ImageEditingTab.cshtml`

- [ ] **Step 1: Replace the left sidebar content wrapper structure**

In `src/Pages/_Generate/ImageEditingTab.cshtml`, inside `#imageediting_main_inputs_area_wrapper`, wrap the existing input groups in a new workspace:

```html
<div class="imageediting_tool_workspace">
    <div class="imageediting_tool_rail" id="imageediting_tool_rail"></div>
    <div class="imageediting_context_panel" id="imageediting_context_panel">
        <!-- existing imageediting_input_group blocks move here -->
    </div>
</div>
```

The existing groups for these IDs must remain present inside `imageediting_context_panel`:

```text
imageediting_tools_header
imageediting_tool_buttons
imageediting_pen_options_header
imageediting_pen_options_body
imageediting_actions_header
imageediting_option_buttons
imageediting_layer_options_header
imageediting_layer_options_body
imageediting_image_options_header
imageediting_image_options_body
imageediting_selection_crop_header
imageediting_selection_crop_body
imageediting_effects_presets_header
imageediting_effects_presets_body
```

- [ ] **Step 2: Keep the persistent color selector at the bottom**

Leave `imageediting_permanent_controls` after `imageediting_tool_workspace`, still inside the left sidebar wrapper. The color selector is shared across tools and should remain pinned below the scrollable context panel.

- [ ] **Step 3: Static check the markup IDs**

Run:

```bash
rg -n "imageediting_tool_rail|imageediting_context_panel|imageediting_tool_workspace|imageediting_tool_buttons|imageediting_pen_options_body" src/Pages/_Generate/ImageEditingTab.cshtml
```

Expected:

```text
The new rail, context panel, and workspace IDs appear once.
The existing tool and pen option IDs still appear once.
```

- [ ] **Step 4: Commit**

```bash
git add src/Pages/_Generate/ImageEditingTab.cshtml
git commit -m "Restructure image editing sidebar mounts"
```

## Task 2: Build Grouped Icon Rail

**Files:**
- Modify: `src/wwwroot/js/genpage/gentab/currentimagehandler.js`

- [ ] **Step 1: Add state and group metadata near existing Image Editing globals**

Add these near `imageEditingToolButtons` and `imageEditingSelectionToolButtons`:

```javascript
let imageEditingToolRailButtons = {};
let imageEditingToolGroupDefinitions = [
    { id: 'paint', label: 'Paint', toolIds: ['brush', 'eraser', 'paintbucket', 'shape', 'picker'] },
    { id: 'select', label: 'Select', toolIds: ['select', 'ellipse-select', 'lasso-select', 'polygon-select', 'magic-wand', 'color-select'] },
    { id: 'transform', label: 'Transform', toolIds: ['move', 'crop'] },
    { id: 'ai_mask', label: 'AI Mask', toolIds: ['sam3points', 'sam3bbox'] }
];
```

- [ ] **Step 2: Add rail getter**

Add this near the existing Image Editing getter functions:

```javascript
/**
 * Gets the Image Editing grouped tool rail.
 */
function imageEditingGetToolRail() {
    return document.getElementById('imageediting_tool_rail');
}
```

- [ ] **Step 3: Add icon button builder**

Add this before `imageEditingBuildToolButtons()`:

```javascript
/**
 * Builds the grouped icon rail for the Image Editing tab.
 */
function imageEditingBuildToolRail() {
    if (!imageEditingTabEditor) {
        return;
    }
    let rail = imageEditingGetToolRail();
    if (!rail) {
        return;
    }
    rail.innerHTML = '';
    imageEditingToolRailButtons = {};
    for (let group of imageEditingToolGroupDefinitions) {
        let groupDiv = document.createElement('div');
        groupDiv.className = 'imageediting_tool_rail_group';
        let label = document.createElement('div');
        label.className = 'imageediting_tool_rail_group_label translate';
        label.innerText = group.label;
        groupDiv.appendChild(label);
        let buttonGrid = document.createElement('div');
        buttonGrid.className = 'imageediting_tool_rail_grid';
        for (let toolId of group.toolIds) {
            let tool = imageEditingTabEditor.tools[toolId];
            if (!tool || tool.isTempTool) {
                continue;
            }
            let button = document.createElement('button');
            button.className = 'basic-button imageediting_tool_icon_button';
            button.type = 'button';
            button.style.backgroundImage = `url(imgs/${tool.icon}.png)`;
            button.setAttribute('aria-label', tool.name);
            button.title = tool.hotkey ? `${tool.name}\nHotKey: ${tool.hotkey.toUpperCase()}` : tool.name;
            button.addEventListener('click', () => {
                imageEditingTabEditor.activateTool(tool.id);
                imageEditingRefreshToolButtons();
            });
            buttonGrid.appendChild(button);
            imageEditingToolRailButtons[tool.id] = button;
        }
        groupDiv.appendChild(buttonGrid);
        rail.appendChild(groupDiv);
    }
    imageEditingRefreshToolButtons();
}
```

- [ ] **Step 4: Call the rail builder during editor setup**

In `imageEditingEnsureEditorReady()`, after `imageEditingBuildToolButtons();`, add:

```javascript
imageEditingBuildToolRail();
```

- [ ] **Step 5: Extend active-state refresh**

In `imageEditingRefreshToolButtons()`, after the existing loops over `imageEditingToolButtons` and `imageEditingSelectionToolButtons`, add:

```javascript
for (let [toolId, button] of Object.entries(imageEditingToolRailButtons)) {
    let tool = imageEditingTabEditor.tools[toolId];
    if (!tool) {
        button.style.display = 'none';
        continue;
    }
    if (tool.div && tool.div.style.display == 'none') {
        button.style.display = 'none';
    }
    else {
        button.style.display = '';
    }
    button.classList.toggle('imageediting_tool_icon_button_active', imageEditingTabEditor.activeTool && imageEditingTabEditor.activeTool.id == toolId);
}
```

- [ ] **Step 6: Static syntax/style check**

Run:

```bash
rg -n "const |var |} else \\{|if \\([^)]*\\) [^{]" src/wwwroot/js/genpage/gentab/currentimagehandler.js
```

Expected:

```text
No new violations introduced by the changed area. Existing unrelated matches, if any, should not be edited as part of this task.
```

- [ ] **Step 7: Commit**

```bash
git add src/wwwroot/js/genpage/gentab/currentimagehandler.js
git commit -m "Add grouped image editing tool rail"
```

## Task 3: Route Context Panel Sections

**Files:**
- Modify: `src/wwwroot/js/genpage/gentab/currentimagehandler.js`
- Modify: `src/Pages/_Generate/ImageEditingTab.cshtml` only if a missing context section mount is discovered

- [ ] **Step 1: Add context routing metadata**

Add near `imageEditingToolGroupDefinitions`:

```javascript
let imageEditingPaintToolIds = ['brush', 'eraser', 'paintbucket', 'shape', 'picker'];
let imageEditingSelectionContextToolIds = ['select', 'ellipse-select', 'lasso-select', 'polygon-select', 'magic-wand', 'color-select'];
let imageEditingCropContextToolIds = ['crop'];
let imageEditingTransformContextToolIds = ['move'];
let imageEditingAiMaskContextToolIds = ['sam3points', 'sam3bbox'];
```

- [ ] **Step 2: Add section visibility helper**

Add before `imageEditingRefreshToolButtons()`:

```javascript
/**
 * Shows or hides an Image Editing input group.
 */
function imageEditingSetInputGroupVisible(element, visible) {
    if (!element || !element.parentElement) {
        return;
    }
    element.parentElement.style.display = visible ? '' : 'none';
}
```

- [ ] **Step 3: Add context refresh function**

Add after `imageEditingRefreshPenOptions()`:

```javascript
/**
 * Refreshes which control sections appear in the Image Editing context panel.
 */
function imageEditingRefreshContextPanel() {
    if (!imageEditingTabEditor || !imageEditingTabEditor.activeTool) {
        return;
    }
    let toolId = imageEditingTabEditor.activeTool.id;
    let isPaint = imageEditingPaintToolIds.includes(toolId);
    let isSelection = imageEditingSelectionContextToolIds.includes(toolId);
    let isCrop = imageEditingCropContextToolIds.includes(toolId);
    let isTransform = imageEditingTransformContextToolIds.includes(toolId);
    let isAiMask = imageEditingAiMaskContextToolIds.includes(toolId);
    imageEditingSetInputGroupVisible(imageEditingGetToolsHeader(), false);
    imageEditingSetInputGroupVisible(imageEditingGetPenOptionsHeader(), isPaint || isAiMask);
    imageEditingSetInputGroupVisible(imageEditingGetActionsHeader(), isTransform || isAiMask);
    imageEditingSetInputGroupVisible(imageEditingGetLayerOptionsHeader(), true);
    imageEditingSetInputGroupVisible(imageEditingGetImageOptionsHeader(), isPaint || isTransform);
    imageEditingSetInputGroupVisible(imageEditingGetSelectionCropHeader(), isSelection || isCrop);
    imageEditingSetInputGroupVisible(imageEditingGetEffectsPresetsHeader(), isPaint || isTransform);
}
```

Note: hiding `imageediting_tools_header` only hides the old text-button list. The new icon rail remains visible.

- [ ] **Step 4: Call context refresh from active-state paths**

In `imageEditingRefreshToolButtons()`, after `imageEditingRefreshPenOptions();`, add:

```javascript
imageEditingRefreshContextPanel();
```

In `imageEditingEnsureUiReady()`, after `imageEditingApplyInputSectionState();`, add:

```javascript
imageEditingRefreshContextPanel();
```

- [ ] **Step 5: Preserve collapse-state behavior**

Do not remove `imageEditingApplyInputSectionState()` or the existing collapse functions. Context routing controls whether a group is relevant; collapse state still controls whether a visible group's body is expanded.

- [ ] **Step 6: Static check context functions**

Run:

```bash
rg -n "imageEditingRefreshContextPanel|imageEditingSetInputGroupVisible|imageEditingPaintToolIds|imageEditingAiMaskContextToolIds" src/wwwroot/js/genpage/gentab/currentimagehandler.js
```

Expected:

```text
All new helpers and arrays are present.
imageEditingRefreshContextPanel is called from refresh and UI initialization paths.
```

- [ ] **Step 7: Commit**

```bash
git add src/wwwroot/js/genpage/gentab/currentimagehandler.js
git commit -m "Route image editing context controls"
```

## Task 4: Style Rail, Context Panel, and Layers Cleanup

**Files:**
- Modify: `src/wwwroot/css/genpage.css`

- [ ] **Step 1: Add left workspace layout styles**

Add near the existing `#ImageEditing .imageediting_input_group` styles:

```css
#ImageEditing .imageediting_tool_workspace {
    display: grid;
    grid-template-columns: 5.4rem minmax(0, 1fr);
    gap: 0.5rem;
    min-height: 0;
}
#ImageEditing .imageediting_tool_rail {
    display: flex;
    flex-direction: column;
    gap: 0.6rem;
    min-width: 0;
}
#ImageEditing .imageediting_context_panel {
    min-width: 0;
}
#ImageEditing .imageediting_tool_rail_group {
    border: 1px solid var(--light-border);
    border-radius: 0.4rem;
    background-color: var(--background-panel-subtle);
    padding: 0.35rem;
}
#ImageEditing .imageediting_tool_rail_group_label {
    color: var(--text-soft);
    font-size: 0.75rem;
    font-weight: 700;
    line-height: 1.1;
    margin-bottom: 0.35rem;
    text-align: center;
}
#ImageEditing .imageediting_tool_rail_grid {
    display: grid;
    grid-template-columns: repeat(2, 2rem);
    gap: 0.25rem;
    justify-content: center;
}
```

- [ ] **Step 2: Add icon button styles**

Add near `.imageediting_tool_button` styles:

```css
#ImageEditing .imageediting_tool_icon_button {
    width: 2rem;
    height: 2rem;
    min-width: 2rem;
    padding: 0;
    border-radius: 0.4rem;
    background-repeat: no-repeat;
    background-position: center;
    background-size: 1.45rem 1.45rem;
}
#ImageEditing .imageediting_tool_icon_button_active {
    background-color: var(--danger-button-background-hover);
    border-color: var(--box-selected-border);
}
#ImageEditing .imageediting_tool_icon_button:focus-visible {
    outline: 2px solid var(--box-selected-border-stronger);
    outline-offset: 2px;
}
```

- [ ] **Step 3: Soften old context groups**

Update existing Image Editing section styles so context groups are less visually heavy:

```css
#ImageEditing .imageediting_context_panel .imageediting_input_group {
    margin-bottom: 0.6rem;
}
#ImageEditing .imageediting_context_panel .imageediting_section_header {
    padding: 0.28rem 0.42rem;
}
```

- [ ] **Step 4: Clean up right layer controls**

Add near the existing right sidebar layer styles:

```css
#ImageEditing .imageediting_editor_sidebar_content .image_editor_rightbar {
    gap: 0.35rem;
}
#ImageEditing .imageediting_editor_sidebar_content .image_editor_newlayer_button {
    border-radius: 0.4rem;
    margin: 0;
    min-height: 2rem;
    padding: 0.35rem 0.45rem;
}
#ImageEditing .imageediting_editor_sidebar_content .image_editor_layer_preview {
    border-radius: 0.4rem;
    overflow: hidden;
}
#ImageEditing .imageediting_editor_sidebar_content .image_editor_layer_preview-active {
    border-color: var(--box-selected-border);
}
```

- [ ] **Step 5: Static CSS check**

Run:

```bash
rg -n "#ImageEditing \\.imageediting_tool_rail|#ImageEditing \\.imageediting_tool_icon_button|#ImageEditing \\.imageediting_context_panel" src/wwwroot/css/genpage.css
```

Expected:

```text
The new rail, icon button, and context panel selectors are present.
All selectors use class references except existing element IDs required by the tab.
```

- [ ] **Step 6: Commit**

```bash
git add src/wwwroot/css/genpage.css
git commit -m "Style image editing icon rail"
```

## Task 5: Static End-to-End Review

**Files:**
- Review: `src/Pages/_Generate/ImageEditingTab.cshtml`
- Review: `src/wwwroot/js/genpage/gentab/currentimagehandler.js`
- Review: `src/wwwroot/css/genpage.css`

- [ ] **Step 1: Confirm no accidental user files are staged**

Run:

```bash
git status --short
```

Expected:

```text
Only intentional implementation files are modified or staged. Existing unrelated .gitignore and .superpowers changes remain separate.
```

- [ ] **Step 2: Check all new IDs and functions resolve by text search**

Run:

```bash
rg -n "imageediting_tool_rail|imageediting_context_panel|imageEditingGetToolRail|imageEditingBuildToolRail|imageEditingRefreshContextPanel" src/Pages/_Generate/ImageEditingTab.cshtml src/wwwroot/js/genpage/gentab/currentimagehandler.js src/wwwroot/css/genpage.css
```

Expected:

```text
Markup, JavaScript getters/builders, refresh calls, and CSS selectors are all present.
```

- [ ] **Step 3: Check existing critical IDs are still present**

Run:

```bash
rg -n "imageediting_layer_opacity_slider|imageediting_selection_tolerance_slider|imageediting_crop_commit_button|imageediting_effect_preset_select|imageediting_color_text|imageediting_inline_color_picker" src/Pages/_Generate/ImageEditingTab.cshtml
```

Expected:

```text
All existing control IDs still exist exactly once.
```

- [ ] **Step 4: Check JavaScript style in changed area**

Run:

```bash
rg -n "const |var |} else \\{|\\.forEach\\(|if \\([^)]*\\) [^{]" src/wwwroot/js/genpage/gentab/currentimagehandler.js
```

Expected:

```text
No new violations from the implementation. Existing unrelated violations should be left untouched.
```

- [ ] **Step 5: Manual live verification checklist for developer**

Ask Reaper176 to verify in the live app:

```text
1. Open Image Editing tab.
2. Confirm the left rail shows Paint, Select, Transform, and AI Mask groups.
3. Click every icon and confirm the correct tool activates.
4. Use Brush and Eraser; confirm pen options appear and update brush behavior.
5. Use Rect Select, Magic Wand, and Crop; confirm their context controls appear and still work.
6. Add Image, Mask, and Adjustment layers; confirm the right layer list updates.
7. Drag-reorder layers and confirm active layer highlighting remains clear.
8. Use Send To Generate Tab and confirm existing workflow still works.
9. Resize left and right sidebars and confirm controls do not overlap.
```

- [ ] **Step 6: Commit final review adjustments if needed**

If manual review reveals small polish fixes, commit them separately:

```bash
git add src/Pages/_Generate/ImageEditingTab.cshtml src/wwwroot/js/genpage/gentab/currentimagehandler.js src/wwwroot/css/genpage.css
git commit -m "Polish image editing icon rail"
```

If no fixes are needed, do not create an empty commit.
