# Image Editing UI Coordinator Extraction Design

## Goal

Extract the Image Editing tab's browser-side UI coordinator from `src/wwwroot/js/genpage/gentab/currentimagehandler.js` into a focused helper file without changing behavior, public globals, initialization timing, persisted state, or UI appearance.

This is the first implementation project from the maintainability architecture audit. It establishes a safe feature-boundary pattern for the classic-script frontend while materially reducing the responsibility overlap in `currentimagehandler.js`.

## Current State

`currentimagehandler.js` combines two distinct responsibilities:

- current and batch image cards, full-view behavior, metadata actions, comparisons, save/star/copy actions, and related image workflows;
- the Image Editing tab's state, DOM coordination, editor initialization, controls, layers, selections, effects, splitters, zoom, editor transfer, and Bootstrap tab lifecycle.

The Image Editing coordinator is a contiguous block of roughly 2,440 lines. It begins immediately after the assignment to `defaultButtonChoices` and ends before `getImageFullSrc`. Its known maintained consumers are:

- inline `imageEditing*` handlers in `src/Pages/_Generate/ImageEditingTab.cshtml`;
- the `imageEditingEnsureUiReady` first-open hook in `src/wwwroot/js/genpage/main.js`;
- later current-image actions in `currentimagehandler.js` that call the editor-transfer functions;
- the Generate-tab editor exposed through `window.imageEditor` and the global editor classes.

The committed page behavior lazily loads Image Editing markup and scripts. The maintainer's current uncommitted work changes those partials and scripts to eager loading. The extraction must support both paths without overwriting that work.

## Selected Approach

Create `src/wwwroot/js/genpage/helpers/image_editor_ui.js` and move the entire contiguous Image Editing coordinator into it. The move includes coordinator state, DOM getters, control wiring, editor creation, tools, layers, selections, crop/effects/color/zoom/splitter actions, Generate/Image Editing transfer helpers, and Bootstrap tab handlers.

All declarations remain classic-script globals with their existing names and call signatures. This project does not introduce a singleton or module boundary. A later project may wrap the implementation behind an `ImageEditingUI` singleton after the physical boundary has been manually validated.

This approach is preferred over a partial extraction because leaving transfer helpers in `currentimagehandler.js` would create mutual ownership between the files. It is preferred over a delegating placeholder because delegation without moving the implementation would not reduce the existing responsibility collision.

## File Responsibilities

### `helpers/image_editor_ui.js`

Owns:

- all `imageEditing*` state associated with the Image Editing tab;
- Image Editing DOM lookup and control synchronization;
- Image Editing editor initialization and lifecycle;
- tool, layer, adjustment, selection, crop, effect, color, zoom, and splitter behavior;
- copying an image or editor-layer state between the Image Editing and Generate editors;
- Image Editing and Generate top-tab activation/deactivation behavior.

Consumes existing globals including general DOM utilities, Bootstrap/jQuery events, `ImageEditor`, `ImageEditorLayer`, editor tools, `openGenPageTabAsync`, `ensureGenerateImageEditorReady`, and `window.imageEditor`.

### `gentab/currentimagehandler.js`

Continues to own:

- current and batch image presentation;
- full-view state and actions;
- image-card and metadata operations;
- save, star, reuse, copy, compare, and Krita actions;
- calls to the preserved editor-transfer globals when a current image action requests editing.

It no longer owns Image Editing tab state, controls, or lifecycle.

### `Pages/Text2Image.cshtml`

Declares the new coordinator after `image_editor.js` in the `imageediting` lazy script group. While the maintainer's eager-loading experiment remains in the working tree, it loads the coordinator immediately after `currentimagehandler.js` in the eager script sequence. At that point all editor dependencies have loaded, the coordinator executes near its current host-file timing, and `main.js` has not yet evaluated. The lazy activation state prevents a second evaluation in that configuration.

No markup or lazy-loading behavior is otherwise changed by this project.

## Runtime Flow

Under committed lazy behavior:

1. The generation page loads `currentimagehandler.js` and `main.js` during its normal script sequence.
2. Opening Image Editing loads its partial markup.
3. The lazy loader evaluates `color_picker.js`, `image_editor_tools.js`, `image_editor.js`, and then `image_editor_ui.js` in order.
4. `main.js` invokes the existing global `imageEditingEnsureUiReady` hook.
5. The coordinator initializes missing editor state and synchronizes controls.

Under the maintainer's current eager behavior:

1. The Image Editing partial is rendered during the initial Razor response.
2. The editor dependencies load in the regular script sequence, followed later by `currentimagehandler.js`, `image_editor_ui.js`, and `main.js`.
3. The lazy state is already marked loaded, so activation invokes initialization without evaluating the scripts again.

Current and history image actions continue calling the same transfer globals. Switching top tabs continues to deactivate the hidden editor, activate and resize the visible editor, and restore the Generate editor only when the coordinator previously paused it.

## Compatibility Requirements

- Preserve every moved global name and function signature.
- Preserve all inline Razor handler names.
- Preserve local-storage keys and their default values.
- Preserve Bootstrap and jQuery event names, binding order, and repeat-activation guards.
- Preserve all DOM IDs and class expectations.
- Preserve the retry limits and error messages used when Generate inputs or editor dependencies are unavailable.
- Preserve both directions of editor transfer, including layer properties, effects, masks, selection state, canvas dimensions, offsets, and active-layer choice.
- Load the coordinator after `image_editor.js` in every active script path and after `currentimagehandler.js` in the active eager path.
- Do not evaluate the coordinator twice on one page load.
- Preserve external-extension compatibility by leaving the global surface intact even where core searches find no additional consumer.

## Error Handling

Existing error behavior moves with the coordinator unchanged. This includes `showError` calls, image load failure handling, missing Init Image parameter retries, unavailable-editor checks, and asynchronous transfer rejection handling.

The extraction does not add silent fallbacks for missing dependencies. A load-order or declaration error should remain visible rather than being hidden by a second implementation path.

## Implementation Sequence

1. Inventory every declaration in the contiguous coordinator and every reference that crosses its start or end boundary.
2. Classify crossed symbols as moved declarations, consumed globals, or compatibility exports.
3. Create `helpers/image_editor_ui.js` and move the coordinator block without changing logic.
4. Add repository-required documentation only where a moved declaration lacks it; do not combine cleanup with relocation.
5. Add the new file after `image_editor.js` in the lazy manifest and after `currentimagehandler.js` in the active eager sequence.
6. Remove the original coordinator block from `currentimagehandler.js`.
7. Statically verify declaration uniqueness, consumer resolution, script ordering, relocation fidelity, and whitespace.
8. Commit only the extraction files after static checks pass.
9. Have the maintainer run the manual validation matrix before any singleton or CSS follow-up begins.

## Non-Goals

- No user-visible behavior or appearance changes.
- No native JavaScript modules.
- No `ImageEditingUI` singleton in this project.
- No removal or renaming of globals.
- No refactor of `ImageEditor`, `ImageEditorLayer`, or editor tool implementations.
- No refactor of current-image rendering, output history, server APIs, or Razor markup.
- No lazy-tab architecture change and no reinterpretation of the maintainer's eager-loading work.
- No Image Editing CSS extraction.
- No opportunistic formatting or cleanup of the moved implementation.

## Static Verification

Repository policy prohibits agents from running builds, tests, browser automation, or the live server. The implementation will therefore use static checks to confirm:

- the moved source block is relocation-equivalent except for necessary file-level documentation;
- every `imageEditing*` declaration is defined exactly once;
- known Razor and JavaScript consumers still resolve to the same global names;
- the new file appears after `image_editor.js` in the committed lazy path and after `currentimagehandler.js` in the active eager path;
- no unrelated maintainer files are staged;
- `git diff --check` reports no whitespace errors.

## Manual Validation

The maintainer will validate the completed extraction in the live application:

1. Load Generate and open Image Editing for the first time, then close and reopen it.
2. Exercise paint and selection tools, layer add/delete/reorder/opacity, crop, effects and presets, color picker, undo/redo, zoom, and both sidebar splitters.
3. Send current and history images to Image Editing.
4. Send Image Editing layers to the Generate editor and edit a current image in the Generate editor.
5. Switch repeatedly between Generate and Image Editing, confirming only the visible editor is active and correctly resized.
6. Check desktop, narrow, and mobile layouts and relevant light/dark themes.
7. Recheck current image, batch view, full view, save/star/copy, comparison, and Krita actions.

## Success Criteria

- `currentimagehandler.js` loses the roughly 2,440-line Image Editing coordinator and has a coherent current/batch-image responsibility.
- `image_editor_ui.js` is the sole implementation owner for Image Editing tab UI state and lifecycle.
- Existing globals, handlers, persisted keys, script timing, and behavior remain compatible.
- The coordinator loads exactly once and after its editor dependencies under both committed lazy behavior and the maintainer's active eager-loading work; eager evaluation remains between `currentimagehandler.js` and `main.js`.
- Static verification passes and the maintainer completes the live validation matrix without regression.
- Singleton encapsulation and CSS extraction remain independently reviewable follow-up projects.
