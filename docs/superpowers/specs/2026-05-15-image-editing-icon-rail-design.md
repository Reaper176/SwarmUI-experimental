# Image Editing Icon Rail Design

## Goal

Make the Image Editing tab feel more like a focused image editor by replacing the long text-button tool list with a compact grouped icon rail and a contextual options panel.

This is a structural UI polish pass, not a behavior rewrite. Existing editor tools, layer operations, sliders, color controls, and send-to-generate workflows should keep their current behavior.

## Current Context

The Image Editing tab is defined primarily in `src/Pages/_Generate/ImageEditingTab.cshtml`, with interaction and layout logic in `src/wwwroot/js/genpage/gentab/currentimagehandler.js`. The reusable image editor lives in `src/wwwroot/js/genpage/helpers/image_editor.js` and `src/wwwroot/js/genpage/helpers/image_editor_tools.js`. Shared and tab-specific styling lives in `src/wwwroot/css/genpage.css`.

The current tab has three major regions:

- Left sidebar: text tool buttons, editor actions, layer/image controls, selection/crop controls, effects/presets, and the persistent color selector.
- Center: zoom controls and the editor canvas.
- Right sidebar: the image editor right bar with layer creation buttons and layer previews.

The left sidebar works, but the tool picker and settings areas read as one long control stack. This makes common workflows harder to scan and makes the tab feel less like a dedicated editing workspace.

## Proposed Layout

Keep the three-zone workspace:

- Left: tool rail plus context panel.
- Center: canvas/editor area.
- Right: layers panel.

The left sidebar becomes a two-column internal layout:

- `Tool Rail`: compact icon buttons grouped by task.
- `Context Panel`: active tool settings and related actions.

The center canvas remains the primary visual focus. Zoom controls remain above the canvas, with minor styling cleanup only.

The right sidebar remains layer-focused. Layer creation controls stay there, but can be styled as a compact row/grid above the layer list.

## Tool Groups

Use existing image assets from `src/wwwroot/imgs` for the icon rail. Do not add a new icon system for this pass.

Initial groups:

- `Paint`: Brush, Eraser, Bucket, Shape, Color Picker.
- `Select`: Rect Select, Ellipse Select, Lasso, Polygon Lasso, Magic Wand, Color Select.
- `Transform`: Move, Crop.
- `AI Mask`: SAM3 Points, SAM3 BBox.

Each icon button should expose the current tool name through `title` and `aria-label`. Hotkeys should remain in tooltips where already available.

## Context Panel

The context panel shows the controls most relevant to the active tool.

- Paint tools show pen/color controls. Brush and Eraser continue using the existing pen options mount.
- Selection tools show selection controls and clear-selection action.
- Crop shows crop display size and crop commit/cancel/reset controls.
- Transform tools show relevant editor or layer actions.
- AI Mask tools show SAM3-specific state and controls where the existing tools provide them.
- General editor actions remain available in the panel, but should no longer compete visually with the primary tool picker.

Where practical, keep existing element IDs and wiring functions so behavior changes are limited. The implementation should prefer moving existing DOM blocks into the new panel structure over replacing control logic.

## Layers Panel

The right sidebar stays dedicated to layers.

Layer creation buttons should remain above the layer list:

- New Image
- New Mask
- New Adjustment
- Send To Generate Tab

The layer list should keep the existing drag/reorder behavior and active-layer styling. Visual cleanup may include more consistent spacing, clearer active borders, and better button sizing.

## Styling

Use existing theme variables such as `--background-panel`, `--background-panel-subtle`, `--button-background`, `--button-background-hover`, `--light-border`, `--box-selected-border`, and `--text-soft`.

Keep the existing SwarmUI visual language:

- No new dominant color palette.
- No decorative gradients or ornamental backgrounds.
- Border radius should stay at or below the existing 8px style.
- Icon buttons should have stable square dimensions.
- Text labels should fit in compact panels without wrapping into clutter.

The selected tool state should be visually clear but consistent with existing selected-button styling.

## Implementation Notes

Likely files:

- `src/Pages/_Generate/ImageEditingTab.cshtml`: introduce the left sidebar rail/panel containers and move/group existing sections as needed.
- `src/wwwroot/js/genpage/gentab/currentimagehandler.js`: build grouped icon buttons, update active states, and route active tool changes to the context panel.
- `src/wwwroot/css/genpage.css`: style the rail, group labels, icon buttons, context panel, and cleaned-up layers panel.

The reusable `ImageEditor` and tool classes should stay mostly unchanged unless a small helper is needed to expose metadata already present on tools.

## Validation

Agents cannot run builds or automated tests in this repository. Validation should be static and manual:

- Confirm the tab initializes without changing editor construction order.
- Confirm each tool button activates the expected existing tool.
- Confirm active tool state updates when using buttons or hotkeys.
- Confirm brush/eraser pen options still mount correctly.
- Confirm selection and crop controls still affect the active editor state.
- Confirm layer creation, selection, reorder, and send-to-generate workflows still use existing code paths.
- Confirm layout remains usable at narrow sidebar widths and does not overlap text or controls.

Manual live-software verification by a developer is required.
