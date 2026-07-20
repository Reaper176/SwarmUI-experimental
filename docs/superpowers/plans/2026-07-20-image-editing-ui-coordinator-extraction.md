# Image Editing UI Coordinator Extraction Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Move the Image Editing tab UI coordinator into a focused classic-script helper without changing globals, behavior, initialization timing, persisted state, or UI appearance.

**Architecture:** Relocate the exact coordinator block from `currentimagehandler.js` into `helpers/image_editor_ui.js`. Preserve the existing global API and load the new helper after the editor engine in the committed lazy path; also integrate it after `currentimagehandler.js` in the maintainer's current eager path while staging only the lazy-path change with the refactor.

**Tech Stack:** Browser JavaScript, Razor Pages, Bootstrap/jQuery tab events, Git, `rg`, `sed`, `diff`, and Node's static syntax checker when available.

---

## Execution Constraints

- Work directly on `master`; the maintainer explicitly declined a worktree.
- Do not run builds, tests, browser automation, the live server, package installation, formatters, or code generators.
- Static syntax checking and source inspection are permitted by repository policy.
- Use `apply_patch` for every working-tree file edit.
- Do not modify or stage `src/Data/Settings.fds`, `src/wwwroot/js/genpage/gentab/loras.js`, `src/wwwroot/js/genpage/main.js`, or `Data.pre-restore-2026-07-19/`.
- Preserve the maintainer's existing uncommitted changes in `src/Pages/Text2Image.cshtml`. The eager coordinator script line must remain in the working tree with that experiment, while only the independently valid lazy-manifest line is staged in the refactor commit.
- Do not inspect or modify local user-data directories.
- Keep the relocation atomic: do not commit the new helper without both removing the old block and adding the committed lazy load entry.
- Do not introduce an `ImageEditingUI` singleton, move CSS, rename globals, change local-storage keys/defaults, or alter retry limits and error messages.

## File Structure

- Create: `src/wwwroot/js/genpage/helpers/image_editor_ui.js` — sole implementation owner for Image Editing tab UI state, controls, editor transfers, and tab lifecycle.
- Modify: `src/wwwroot/js/genpage/gentab/currentimagehandler.js:1559-3998` — remove the coordinator while retaining `defaultButtonChoices` before it and `getImageFullSrc` plus current/batch-image behavior after it.
- Modify: `src/Pages/Text2Image.cshtml:61-67` — add the new helper to the committed lazy Image Editing script group after `image_editor.js`.
- Modify working tree only: `src/Pages/Text2Image.cshtml:176-177` — add the new helper after `currentimagehandler.js` for the maintainer's uncommitted eager-loading experiment; intentionally do not stage this eager-only line with the refactor.
- Reference: `src/Pages/_Generate/ImageEditingTab.cshtml` — inline global consumers; do not modify.
- Reference: `src/wwwroot/js/genpage/main.js:1115-1123` — lazy activation consumer; do not modify.

### Task 1: Reconfirm the Safe Extraction Baseline

**Files:**
- Read: `src/wwwroot/js/genpage/gentab/currentimagehandler.js`
- Read: `src/Pages/Text2Image.cshtml`
- Read: `src/Pages/_Generate/ImageEditingTab.cshtml`
- Read: `src/wwwroot/js/genpage/main.js`

- [ ] **Step 1: Confirm the branch and preserve the maintainer's dirty files**

Run:

```bash
git branch --show-current
git status --short
git diff --quiet -- src/wwwroot/js/genpage/gentab/currentimagehandler.js
```

Expected: the branch is `master`; the known maintainer changes remain visible; the final command exits zero because `currentimagehandler.js` has no pre-existing modification. Stop and ask the maintainer if that file is already modified.

- [ ] **Step 2: Confirm the exact extraction markers and size**

Run:

```bash
nl -ba src/wwwroot/js/genpage/gentab/currentimagehandler.js | sed -n '1555,1565p;3953,4003p'
git show HEAD:src/wwwroot/js/genpage/gentab/currentimagehandler.js | sed -n '1559,3998p' | wc -l
```

Expected: `defaultButtonChoices` is at line 1557; the coordinator starts with the documentation for `imageEditingZoomLevel` at line 1559; the Bootstrap handler closes at line 3998; `getImageFullSrc` begins at line 4000; the extraction contains 2,440 lines.

- [ ] **Step 3: Confirm known consumers and both script paths**

Run:

```bash
rg -n "imageEditing[A-Za-z0-9_]*|openGenerateTabEditorForImage|openGenerateTabEditorForEditorData|sendToImageEditingTabPreview|sendImageEditingLayersToGenerateEditor" src/Pages src/wwwroot/js src/BuiltinExtensions --glob '*.js' --glob '*.cshtml' --glob '!**/*.bak' --glob '!src/Extensions/**'
rg -n 'imageediting:|image_editor\.js|currentimagehandler\.js|main\.js' src/Pages/Text2Image.cshtml
```

Expected: inline handlers occur in `ImageEditingTab.cshtml`; the activation hook occurs in `main.js`; the implementation and two later transfer calls occur in `currentimagehandler.js`; editor dependencies precede `currentimagehandler.js` in the current eager sequence.

### Task 2: Relocate the Coordinator and Integrate Both Load Paths

**Files:**
- Create: `src/wwwroot/js/genpage/helpers/image_editor_ui.js`
- Modify: `src/wwwroot/js/genpage/gentab/currentimagehandler.js:1559-3998`
- Modify: `src/Pages/Text2Image.cshtml:61-67,176-177`

- [ ] **Step 1: Create the focused helper with the exact existing block**

Use `apply_patch` to create `src/wwwroot/js/genpage/helpers/image_editor_ui.js`. Its complete contents must be exactly the 2,440 lines currently selected by:

```bash
git show HEAD:src/wwwroot/js/genpage/gentab/currentimagehandler.js | sed -n '1559,3998p'
```

The file therefore starts with:

```javascript
/**
 * Current zoom value for the Image Editing tab editor.
 */
let imageEditingZoomLevel = 1;
```

and ends with the complete top-tab handler:

```javascript
        }
    }
});
```

Do not rename, reformat, wrap, or otherwise edit the moved code.

- [ ] **Step 2: Remove only the relocated block from the host file**

Use `apply_patch` to delete the same block from `currentimagehandler.js`. The resulting join must be exactly:

```javascript
defaultButtonChoices = 'Use As Init,Edit Image,Send To Image Edit Tab,Star,Reuse Parameters,Save Image';

function getImageFullSrc(src) {
```

Do not move `defaultButtonChoices` or `getImageFullSrc`.

- [ ] **Step 3: Add the committed lazy load entry**

Use `apply_patch` to change the Image Editing lazy group in `Text2Image.cshtml` to:

```javascript
        imageediting: [
            "js/genpage/helpers/color_picker.js?vary=@Utilities.VaryID",
            "js/genpage/helpers/image_editor_tools.js?vary=@Utilities.VaryID",
            "js/genpage/helpers/image_editor.js?vary=@Utilities.VaryID",
            "js/genpage/helpers/image_editor_ui.js?vary=@Utilities.VaryID"
        ],
```

This line is independently correct on a clean checkout and will be staged.

- [ ] **Step 4: Integrate the maintainer's active eager sequence**

Use `apply_patch` to insert the new helper immediately after `currentimagehandler.js` in the current working tree:

```html
    <script src="js/genpage/gentab/currentimagehandler.js?vary=@Utilities.VaryID"></script>
    <script src="js/genpage/helpers/image_editor_ui.js?vary=@Utilities.VaryID"></script>
    <script src="js/genpage/gentab/outputhistory.js?vary=@Utilities.VaryID"></script>
```

This preserves coordinator evaluation between `currentimagehandler.js` and `main.js`. Because the surrounding eager-loading changes are maintainer-owned and uncommitted, this line remains unstaged with them.

- [ ] **Step 5: Prove the new file is relocation-equivalent before staging**

Run:

```bash
diff -u <(git show HEAD:src/wwwroot/js/genpage/gentab/currentimagehandler.js | sed -n '1559,3998p') src/wwwroot/js/genpage/helpers/image_editor_ui.js
wc -l src/wwwroot/js/genpage/helpers/image_editor_ui.js src/wwwroot/js/genpage/gentab/currentimagehandler.js
```

Expected: `diff` produces no output and exits zero; the new helper has 2,440 lines; `currentimagehandler.js` has 2,962 lines.

Because this is an exact diff against the original block, success also proves that local-storage keys/defaults, retry limits, error messages, event bindings, and transfer behavior were not edited during relocation.

### Task 3: Perform Static Compatibility Verification

**Files:**
- Inspect: `src/wwwroot/js/genpage/helpers/image_editor_ui.js`
- Inspect: `src/wwwroot/js/genpage/gentab/currentimagehandler.js`
- Inspect: `src/Pages/Text2Image.cshtml`
- Inspect: `src/Pages/_Generate/ImageEditingTab.cshtml`
- Inspect: `src/wwwroot/js/genpage/main.js`

- [ ] **Step 1: Check JavaScript syntax without executing application code**

Run:

```bash
node --check src/wwwroot/js/genpage/helpers/image_editor_ui.js
node --check src/wwwroot/js/genpage/gentab/currentimagehandler.js
```

Expected: both commands exit zero with no output. If `node` is unavailable, record that and continue with the remaining static checks; do not install it.

- [ ] **Step 2: Confirm each moved top-level declaration exists exactly once**

Run:

```bash
declaration_names=$(rg -o '^(?:async )?function [A-Za-z_$][A-Za-z0-9_$]*|^let [A-Za-z_$][A-Za-z0-9_$]*' src/wwwroot/js/genpage/helpers/image_editor_ui.js | sed -E 's/^(async )?function //; s/^let //')
declaration_failure=0
while IFS= read -r declaration_name; do
    declaration_count=$(rg -n "^(let ${declaration_name}[ =]|(async )?function ${declaration_name}\\()" src/wwwroot/js/genpage --glob '*.js' | wc -l)
    if [ "$declaration_count" -ne 1 ]; then
        printf '%s=%s\n' "$declaration_name" "$declaration_count"
        declaration_failure=1
    fi
done <<< "$declaration_names"
test "$declaration_failure" = 0
```

Expected: no declaration count is printed and the final command exits zero.

- [ ] **Step 3: Confirm the host retains only intended coordinator consumers**

Run:

```bash
rg -n "imageEditing[A-Za-z0-9_]*|openGenerateTabEditorForImage|openGenerateTabEditorForEditorData|sendToImageEditingTabPreview|sendImageEditingLayersToGenerateEditor" src/wwwroot/js/genpage/gentab/currentimagehandler.js
```

Expected: only the later current-image action calls to `openGenerateTabEditorForImage` and `sendToImageEditingTabPreview` remain; no `imageEditing*` declaration or UI implementation remains.

- [ ] **Step 4: Confirm Razor and activation compatibility**

Run:

```bash
rg -n 'imageEditingToggleInputSection|imageEditingZoomOut|imageEditingZoomReset|imageEditingZoomIn' src/Pages/_Generate/ImageEditingTab.cshtml src/wwwroot/js/genpage/helpers/image_editor_ui.js
rg -n -C 3 'imageEditingEnsureUiReady' src/wwwroot/js/genpage/main.js src/wwwroot/js/genpage/helpers/image_editor_ui.js
```

Expected: every Razor inline handler has one matching helper definition; `main.js` still conditionally invokes the same global activation function.

- [ ] **Step 5: Confirm lazy and eager load ordering**

Run:

```bash
rg -n 'imageediting:|image_editor\.js|currentimagehandler\.js|image_editor_ui\.js|main\.js' src/Pages/Text2Image.cshtml
```

Expected: the lazy group lists `image_editor_ui.js` immediately after `image_editor.js`; the eager sequence lists it immediately after `currentimagehandler.js` and before `main.js`; each path contains exactly one entry.

- [ ] **Step 6: Check whitespace and scope**

Run:

```bash
git diff --check
git status --short
git diff --stat
```

Expected: no whitespace errors; the new helper plus the two intended source files are the only implementation paths changed by this project; all pre-existing maintainer changes remain visible.

### Task 4: Stage and Commit Only the Atomic Refactor

**Files:**
- Stage: `src/wwwroot/js/genpage/helpers/image_editor_ui.js`
- Stage: `src/wwwroot/js/genpage/gentab/currentimagehandler.js`
- Partially stage: `src/Pages/Text2Image.cshtml` lazy-manifest line only

- [ ] **Step 1: Stage the files that had no pre-existing maintainer changes**

Run:

```bash
git add -- src/wwwroot/js/genpage/helpers/image_editor_ui.js src/wwwroot/js/genpage/gentab/currentimagehandler.js
```

Expected: the new helper and host-file deletion are staged in full.

- [ ] **Step 2: Stage only the independently valid lazy-manifest addition**

Do not run `git add src/Pages/Text2Image.cshtml`. Apply this exact patch to the index only:

```bash
git apply --cached <<'PATCH'
diff --git a/src/Pages/Text2Image.cshtml b/src/Pages/Text2Image.cshtml
--- a/src/Pages/Text2Image.cshtml
+++ b/src/Pages/Text2Image.cshtml
@@ -62,7 +62,8 @@
         imageediting: [
             "js/genpage/helpers/color_picker.js?vary=@Utilities.VaryID",
             "js/genpage/helpers/image_editor_tools.js?vary=@Utilities.VaryID",
-            "js/genpage/helpers/image_editor.js?vary=@Utilities.VaryID"
+            "js/genpage/helpers/image_editor.js?vary=@Utilities.VaryID",
+            "js/genpage/helpers/image_editor_ui.js?vary=@Utilities.VaryID"
         ],
         utilities: [
         ],
PATCH
```

Expected: the index gains only the lazy group change. The eager coordinator line remains in the working tree with the maintainer's eager-loading changes.

- [ ] **Step 3: Audit the staged and unstaged halves separately**

Run:

```bash
git diff --cached --name-only
git diff --cached -- src/Pages/Text2Image.cshtml
git diff -- src/Pages/Text2Image.cshtml
git diff --cached --check
```

Expected: exactly these three paths are staged:

```text
src/Pages/Text2Image.cshtml
src/wwwroot/js/genpage/gentab/currentimagehandler.js
src/wwwroot/js/genpage/helpers/image_editor_ui.js
```

The cached Razor diff contains only the lazy-manifest addition. The unstaged Razor diff retains the maintainer's partial/eager changes and the eager `image_editor_ui.js` line. No whitespace errors are reported.

- [ ] **Step 4: Commit the atomic extraction**

Run:

```bash
git commit -m "Extract image editing UI coordinator"
```

Expected: one commit contains the new helper, removal from the host, and committed lazy-manifest entry without any maintainer-owned changes.

- [ ] **Step 5: Verify post-commit working-tree preservation**

Run:

```bash
git show --stat --oneline --no-renames HEAD
git show --format= --name-only HEAD
git status --short
git diff -- src/Pages/Text2Image.cshtml src/wwwroot/js/genpage/main.js src/wwwroot/js/genpage/gentab/loras.js src/Data/Settings.fds
```

Expected: the commit names only the three refactor paths. The maintainer's settings, LoRA, main, Razor eager-loading work, eager coordinator line, and local backup directory remain uncommitted.

### Task 5: Maintainer Live Validation Handoff

**Files:**
- No source modifications expected

- [ ] **Step 1: Provide the manual validation matrix**

Ask the maintainer to run these workflows in the live application:

1. Load Generate; open Image Editing for the first time, close it, and reopen it.
2. Exercise paint and selection tools, layer add/delete/reorder/opacity, crop, effects/presets, color picker, undo/redo, zoom, and both sidebar splitters.
3. Send current and history images to Image Editing.
4. Send Image Editing layers to the Generate editor and edit a current image in the Generate editor.
5. Switch repeatedly between Generate and Image Editing and confirm only the visible editor is active and correctly resized.
6. Check desktop, narrow, and mobile layouts plus relevant light/dark themes.
7. Recheck current image, batch view, full view, save/star/copy, comparison, and Krita actions.

Expected: the maintainer reports no behavioral or visual regression. If a failure occurs, use `superpowers:systematic-debugging` before changing code.

- [ ] **Step 2: Record completion accurately**

Do not claim live behavior is verified until the maintainer reports the matrix result. Report agent-completed static checks separately from pending manual validation.
