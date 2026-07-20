# Image History Frontend Decomposition Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Split image-history comparison, filtering, and bulk actions into focused collaborators while making `ImageHistoryController` the sole owner of browser, request, refresh, selection, and busy state without changing runtime behavior or compatibility globals.

**Architecture:** Keep `outputhistory.js` as the classic-script composition root and controller implementation. Load three class-definition scripts before it, inject narrow callback objects into the collaborators, and retain existing top-level names as thin delegates backed by one controller-owned browser instance.

**Tech Stack:** Browser JavaScript, Razor script loading, Bootstrap DOM APIs, Canvas, `GenPageBrowserClass`, SwarmUI request utilities, Git, `rg`, `sed`, and Node's syntax-only `--check` parser.

---

## Execution Constraints

- Work directly on `master`; the maintainer explicitly declined a worktree.
- Do not run builds, automated tests, browser automation, the live server, package installation, formatters, or code generators.
- `node --check` is allowed only as a static syntax parser. It must not execute application code.
- Use `apply_patch` for all working-tree edits.
- Before each stage, stop if a stage-owned JavaScript file is unexpectedly dirty.
- Preserve the existing unrelated changes in `src/Data/Settings.fds`, `src/Pages/Text2Image.cshtml`, `src/wwwroot/js/genpage/gentab/loras.js`, `src/wwwroot/js/genpage/main.js`, and `Data.pre-restore-2026-07-19/`.
- `src/Pages/Text2Image.cshtml` is already dirty. Change only the adjacent image-history script tags, stage only those added lines, and inspect the cached diff before every commit.
- Do not inspect or modify `Data.pre-restore-2026-07-19/` or other user-data directories.
- Follow repository JavaScript style: `let`, never `var` or `const`; full braced blocks; `else` on its own line; documented functions/methods; no `===` or `!==` unless logically required.
- Preserve function names, parameters, defaults, callback timing, DOM IDs, CSS classes, local-storage keys, feature toggles, API names, payloads, prompts, confirmations, notices, errors, and selection semantics.
- Do not combine extraction with cleanup, renamed concepts, new validation, filter enhancements, request changes, or UI redesign.
- Each stage must be coherent when its commit is checked out independently.

## File Structure

- Create: `src/wwwroot/js/genpage/gentab/imagehistorycomparison.js` — comparison modal, active pair, pan/zoom/reveal, metadata comparison, and pixel diff.
- Create: `src/wwwroot/js/genpage/gentab/imagehistoryfilter.js` — metadata cache/parsing, structured searchable fields, query compilation, and matching.
- Create: `src/wwwroot/js/genpage/gentab/imagehistorybulkactions.js` — bulk prompts, export/copy/contact sheet/Prompt Lab, and batch mutation workflows.
- Modify: `src/wwwroot/js/genpage/gentab/outputhistory.js` — controller, browser/card orchestration, single-image primitives, composition, and compatibility delegates.
- Modify carefully: `src/Pages/Text2Image.cshtml` — three classic-script tags immediately before `outputhistory.js`.
- Reference only: `src/wwwroot/js/genpage/gentab/currentimagehandler.js` — reads `imageHistoryBrowser`, navigates and refreshes it, and notifies saved paths.
- Reference only: `src/wwwroot/js/genpage/helpers/generatehandler.js` — calls `notifyImageHistorySavedPath()`.
- Reference only: `src/wwwroot/js/genpage/main.js` and `src/wwwroot/js/site.js` — call retained history globals.

### Task 1: Capture the Compatibility and Ownership Baseline

**Files:**
- Read: `src/wwwroot/js/genpage/gentab/outputhistory.js`
- Read: `src/Pages/Text2Image.cshtml`
- Read: `src/wwwroot/js/genpage/gentab/currentimagehandler.js`
- Read: `src/wwwroot/js/genpage/helpers/generatehandler.js`
- Read: `src/wwwroot/js/genpage/main.js`
- Read: `src/wwwroot/js/site.js`

- [ ] **Step 1: Confirm branch and protected working state**

Run:

```bash
git branch --show-current
git status --short
git diff --quiet -- src/wwwroot/js/genpage/gentab/outputhistory.js
```

Expected: branch `master`; the known maintainer changes remain visible; `outputhistory.js` is clean. Do not inspect the backup directory.

- [ ] **Step 2: Record the current top-level state and declaration manifest**

Run:

```bash
sed -n '1,40p' src/wwwroot/js/genpage/gentab/outputhistory.js
rg -n "^(class |let [A-Za-z_$][A-Za-z0-9_$]* ?=|function |async function )" src/wwwroot/js/genpage/gentab/outputhistory.js
```

Expected: 22 orchestration-state declarations at the top, comparison state at lines 3-4, filter cache/compiled-query state, `ImageHistoryWindowManager`, 90-plus top-level functions, and the `imageHistoryBrowser` construction near the end.

- [ ] **Step 3: Record maintained cross-file consumers**

Run:

```bash
rg -n "\b(listOutputHistoryFolderAndFiles|ensureImageHistoryBrowserShellReady|scheduleInitialImageHistoryLoad|notifyImageHistorySavedPath|imageHistoryBrowser|registerMediaButton|storeImageToHistoryWithCurrentParams)\b" src/wwwroot/js src/Pages --glob '!genpage/gentab/outputhistory.js' --glob '!*.bak' --glob '!lib/**'
```

Expected: `site.js` calls the list function; `main.js` calls browser readiness, initial scheduling, and storage; generation and current-image code call saved-path notification; current-image code directly reads and navigates `imageHistoryBrowser`.

- [ ] **Step 4: Record script order and the dirty Razor diff**

Run:

```bash
sed -n '168,184p' src/Pages/Text2Image.cshtml
git diff -- src/Pages/Text2Image.cshtml
```

Expected: `currentimagehandler.js` loads before `outputhistory.js`; unrelated maintainer changes are present. Save the visible diff mentally as protected scope; never stage it wholesale.

- [ ] **Step 5: Confirm the baseline parses without executing it**

Run:

```bash
node --check src/wwwroot/js/genpage/gentab/outputhistory.js
```

Expected: no output and exit code zero. If Node is unavailable, record that static parsing is unavailable and continue with source inspection only; do not install it.

### Task 2: Extract `ImageHistoryComparison`

**Files:**
- Create: `src/wwwroot/js/genpage/gentab/imagehistorycomparison.js`
- Modify: `src/wwwroot/js/genpage/gentab/outputhistory.js:3-4,885-1343`
- Modify carefully: `src/Pages/Text2Image.cshtml` immediately before the `outputhistory.js` script tag

- [ ] **Step 1: Confirm stage-owned files are safe**

Run:

```bash
git diff --quiet -- src/wwwroot/js/genpage/gentab/outputhistory.js
test ! -e src/wwwroot/js/genpage/gentab/imagehistorycomparison.js
```

Expected: both commands exit zero.

- [ ] **Step 2: Create the comparison class and move the exact behavior**

Use `apply_patch` to create `imagehistorycomparison.js` with this state and service boundary:

```javascript
class ImageHistoryComparison {
    /** Builds an image-history comparison collaborator around controller-owned services. */
    constructor(services) {
        this.services = services;
        this.files = null;
        this.pan = { x: 0, y: 0, active: false, startX: 0, startY: 0, baseX: 0, baseY: 0 };
    }
}
```

Inside that class, relocate the bodies of the current comparison functions and use these exact method names:

```text
ensureModal, reuseSettings, starImage, rateImage, renderPair, swapImages,
getMetadataFields, setMetadataMode, renderMetadata, closeModal, cleanupModal,
showGenerateTabAfterClose, openModal, updateRevealFromPointer, startPan, endPan,
applyPan, setReveal, setDiffMode, renderDiff, setZoom, show
```

Replace `imageHistoryCompareFiles` with `this.files`, `imageHistoryComparePan` with `this.pan`, sibling function calls with `this.<method>()`, and controller dependencies with:

```javascript
this.services.getFile(path)
this.services.parseMetadata(metadata)
this.services.valueToSearchText(value)
this.services.setMetadataValue(metadata, key, value)
this.services.requestRefresh()
this.services.selectCurrentImage(src, metadata, batchId)
```

Keep shared platform calls such as `genericRequest`, `toggleStar`, `copy_current_image_params`, `createDiv`, `escapeHtml`, Bootstrap, Canvas, notices, and error display unchanged. Copy existing documentation comments and add `/** ... */` comments to moved methods that lack them.

- [ ] **Step 3: Construct the collaborator and retain every comparison global as a delegate**

Use `apply_patch` in `outputhistory.js` to remove the two comparison-state globals and the old implementation bodies, then construct one collaborator after `getImageHistoryFile()` and the hoisted metadata helpers are available:

```javascript
let imageHistoryComparison = new ImageHistoryComparison({
    getFile: path => getImageHistoryFile(path),
    parseMetadata: metadata => parseHistoryMetadata(metadata),
    valueToSearchText: value => imageHistoryValueToSearchText(value),
    setMetadataValue: (metadata, key, value) => setMetadataValue(metadata, key, value),
    requestRefresh: () => requestImageHistoryRefresh(),
    selectCurrentImage: (src, metadata, batchId) => setCurrentImage(src, metadata, batchId)
});
```

Replace each old comparison function with a documented thin delegate. The complete mapping is:

```text
ensureImageHistoryCompareModal -> ensureModal
reuseImageHistoryCompareSettings -> reuseSettings
starImageHistoryCompareImage -> starImage
rateImageHistoryCompareImage -> rateImage
renderImageHistoryComparePair -> renderPair
swapImageHistoryCompareImages -> swapImages
getImageHistoryCompareMetadataFields -> getMetadataFields
setImageHistoryCompareMetadataMode -> setMetadataMode
renderImageHistoryCompareMetadata -> renderMetadata
closeImageHistoryCompareModal -> closeModal
cleanupImageHistoryCompareModal -> cleanupModal
showGenerateTabAfterImageHistoryCompareClose -> showGenerateTabAfterClose
openImageHistoryCompareModal -> openModal
updateImageHistoryCompareRevealFromPointer -> updateRevealFromPointer
startImageHistoryComparePan -> startPan
endImageHistoryComparePan -> endPan
applyImageHistoryComparePan -> applyPan
setImageHistoryCompareReveal -> setReveal
setImageHistoryCompareDiffMode -> setDiffMode
renderImageHistoryCompareDiff -> renderDiff
setImageHistoryCompareZoom -> setZoom
showImageHistoryCompare -> show
```

Representative delegates must have exactly this shape:

```javascript
/** Shows the image-history comparison for two selected paths. */
function showImageHistoryCompare(paths) {
    return imageHistoryComparison.show(paths);
}

/** Updates comparison pan state from a pointer event. */
function updateImageHistoryCompareRevealFromPointer(e) {
    return imageHistoryComparison.updateRevealFromPointer(e);
}
```

- [ ] **Step 4: Add the comparison script without staging maintainer changes**

Use `apply_patch` to add only:

```html
    <script src="js/genpage/gentab/imagehistorycomparison.js?vary=@Utilities.VaryID"></script>
```

immediately before `outputhistory.js` in the working copy of `Text2Image.cshtml`.

- [ ] **Step 5: Verify the comparison stage statically**

Run:

```bash
node --check src/wwwroot/js/genpage/gentab/imagehistorycomparison.js
node --check src/wwwroot/js/genpage/gentab/outputhistory.js
rg -n "imageHistoryCompareFiles|imageHistoryComparePan" src/wwwroot/js/genpage/gentab/outputhistory.js src/wwwroot/js/genpage/gentab/imagehistorycomparison.js
rg -n "^function (ensureImageHistoryCompareModal|showImageHistoryCompare|renderImageHistoryCompareDiff)" src/wwwroot/js/genpage/gentab/outputhistory.js
git diff --check -- src/wwwroot/js/genpage/gentab/imagehistorycomparison.js src/wwwroot/js/genpage/gentab/outputhistory.js src/Pages/Text2Image.cshtml
```

Expected: both files parse; comparison state appears only as `this.files`/`this.pan` in the new owner; representative globals remain delegates; no whitespace errors.

- [ ] **Step 6: Commit only the comparison extraction and its one script line**

Stage both JavaScript files normally. Use `git add -p src/Pages/Text2Image.cshtml`; split or edit the patch so only the `imagehistorycomparison.js` line is staged. Then run:

```bash
git add -- src/wwwroot/js/genpage/gentab/imagehistorycomparison.js src/wwwroot/js/genpage/gentab/outputhistory.js
git add -p src/Pages/Text2Image.cshtml
git diff --cached -- src/Pages/Text2Image.cshtml
git diff --cached --name-only
git diff --cached --check
git commit -m "Extract image history comparison"
```

Expected: the cached Razor diff contains exactly one added script line; exactly three files are committed; all unrelated maintainer changes remain unstaged.

### Task 3: Extract `ImageHistoryFilter`

**Files:**
- Create: `src/wwwroot/js/genpage/gentab/imagehistoryfilter.js`
- Modify: `src/wwwroot/js/genpage/gentab/outputhistory.js` filtering region formerly at lines 568-884
- Modify carefully: `src/Pages/Text2Image.cshtml` immediately before `imagehistorycomparison.js`

- [ ] **Step 1: Confirm stage-owned files are safe**

Run:

```bash
git diff --quiet -- src/wwwroot/js/genpage/gentab/outputhistory.js src/wwwroot/js/genpage/gentab/imagehistorycomparison.js
test ! -e src/wwwroot/js/genpage/gentab/imagehistoryfilter.js
```

Expected: both commands exit zero.

- [ ] **Step 2: Create the filter class with singular cache/query ownership**

Use `apply_patch` to create `imagehistoryfilter.js` beginning with:

```javascript
class ImageHistoryFilter {
    /** Creates the image-history metadata and query cache. */
    constructor() {
        this.metadataCacheLimit = 1024;
        this.metadataCache = new Map();
        this.compiledFilterText = null;
        this.compiledFilterTerms = [];
    }
}
```

Relocate the exact implementations into these methods:

```text
parseMetadata, valueToSearchText, getSearchFields, splitQuery, normalizeField,
compileQuery, getSearchableText, numericMatches, dateMatches, matches, updateHint
```

Change only ownership references: use `this.metadataCacheLimit`, `this.metadataCache`, `this.compiledFilterText`, `this.compiledFilterTerms`, and `this.<method>()`. Preserve aliases, query syntax, cache eviction, parsing fallback, field values, numeric/date behavior, and hint text exactly. Add `/** ... */` comments to methods that lack them.

- [ ] **Step 3: Replace filter state and implementations with one instance and delegates**

Use `apply_patch` to remove `IMAGE_HISTORY_METADATA_CACHE_LIMIT`, `imageHistoryMetadataCache`, `imageHistoryCompiledFilterText`, and `imageHistoryCompiledFilterTerms` from `outputhistory.js`. Construct:

```javascript
let imageHistoryFilter = new ImageHistoryFilter();
```

Retain documented global delegates with this complete mapping:

```text
parseHistoryMetadata -> parseMetadata
imageHistoryValueToSearchText -> valueToSearchText
getImageHistorySearchFields -> getSearchFields
splitImageHistoryFilterQuery -> splitQuery
normalizeImageHistoryFilterField -> normalizeField
compileImageHistoryFilterQuery -> compileQuery
getImageHistorySearchableText -> getSearchableText
imageHistoryNumericFilterMatches -> numericMatches
imageHistoryDateFilterMatches -> dateMatches
imageHistoryFilterMatches -> matches
updateImageHistoryFilterHint -> updateHint
```

Representative delegates:

```javascript
/** Parses image-history metadata through the bounded collaborator cache. */
function parseHistoryMetadata(metadata) {
    return imageHistoryFilter.parseMetadata(metadata);
}

/** Matches one browser entry against the current history query. */
function imageHistoryFilterMatches(desc, filter) {
    return imageHistoryFilter.matches(desc, filter);
}
```

Because the comparison service callbacks call these globals, no comparison implementation change is required.

- [ ] **Step 4: Add the filter script in dependency order**

Use `apply_patch` so the working copy has:

```html
    <script src="js/genpage/gentab/imagehistoryfilter.js?vary=@Utilities.VaryID"></script>
    <script src="js/genpage/gentab/imagehistorycomparison.js?vary=@Utilities.VaryID"></script>
    <script src="js/genpage/gentab/outputhistory.js?vary=@Utilities.VaryID"></script>
```

- [ ] **Step 5: Verify and commit only the filter stage**

Run:

```bash
node --check src/wwwroot/js/genpage/gentab/imagehistoryfilter.js
node --check src/wwwroot/js/genpage/gentab/imagehistorycomparison.js
node --check src/wwwroot/js/genpage/gentab/outputhistory.js
rg -n "imageHistoryMetadataCache|imageHistoryCompiledFilter|IMAGE_HISTORY_METADATA_CACHE_LIMIT" src/wwwroot/js/genpage/gentab
rg -n "^function (parseHistoryMetadata|imageHistoryFilterMatches|updateImageHistoryFilterHint)" src/wwwroot/js/genpage/gentab/outputhistory.js
git diff --check
```

Expected: all files parse; the removed state names have no matches; representative compatibility delegates remain.

Stage the two JavaScript files normally and only the new filter script line interactively:

```bash
git add -- src/wwwroot/js/genpage/gentab/imagehistoryfilter.js src/wwwroot/js/genpage/gentab/outputhistory.js
git add -p src/Pages/Text2Image.cshtml
git diff --cached -- src/Pages/Text2Image.cshtml
git diff --cached --name-only
git diff --cached --check
git commit -m "Extract image history filtering"
```

Expected: the cached Razor diff adds only `imagehistoryfilter.js`; exactly three files are committed; unrelated changes remain unstaged.

### Task 4: Extract `ImageHistoryBulkActions`

**Files:**
- Create: `src/wwwroot/js/genpage/gentab/imagehistorybulkactions.js`
- Modify: `src/wwwroot/js/genpage/gentab/outputhistory.js` bulk workflows formerly at lines 1680-2148 and 2318-2401
- Modify carefully: `src/Pages/Text2Image.cshtml` between comparison and `outputhistory.js`

- [ ] **Step 1: Confirm stage-owned files are safe**

Run:

```bash
git diff --quiet -- src/wwwroot/js/genpage/gentab/outputhistory.js src/wwwroot/js/genpage/gentab/imagehistorycomparison.js src/wwwroot/js/genpage/gentab/imagehistoryfilter.js
test ! -e src/wwwroot/js/genpage/gentab/imagehistorybulkactions.js
```

Expected: both commands exit zero.

- [ ] **Step 2: Create the bulk-action class and its busy-state guard**

Use `apply_patch` to create `imagehistorybulkactions.js` with:

```javascript
class ImageHistoryBulkActions {
    /** Builds bulk workflows around snapshots and controller callbacks. */
    constructor(services) {
        this.services = services;
    }

    /** Runs an asynchronous action while the controller owns the busy flag. */
    async runBusy(action) {
        if (this.services.isBusy()) {
            return null;
        }
        this.services.setBusy(true);
        try {
            return await action();
        }
        finally {
            this.services.setBusy(false);
        }
    }
}
```

Relocate bulk behavior into these methods:

```text
hideSelected, unhideSelected, deleteSelected, starSelected, unstarSelected,
promptRating, promptTags, promptNotes, promptMove, exportMetadata, copyPaths,
loadContactSheetImage, createContactSheet, metadataToPromptLabPrompt,
sendToPromptLab, setStarred, setRating, setTags, setNotes, move,
setHidden, deleteImages
```

Do not move `compareSelectedImageHistory`, selection mutation, control rendering, `ensureImageHistoryBulkControlsReady`, `removeImageFromHistoryUI`, `deleteSingleHistoryImage`, or `toggleImageHidden`; those remain controller/single-image responsibilities.

Every asynchronous workflow must snapshot paths before calling `runBusy()` and must preserve all existing early returns and prompts. Wrap only the interval currently guarded by `imageHistoryBulkActionRunning`; for example, contact-sheet loading ends its busy interval before canvas rendering just as it does now. Remove only the repeated direct assignments to `imageHistoryBulkActionRunning` and repeated control updates now supplied by `runBusy()`.

- [ ] **Step 3: Construct the bulk collaborator with narrow callbacks**

Use `apply_patch` in `outputhistory.js` to construct exactly one instance:

```javascript
let imageHistoryBulkActions = new ImageHistoryBulkActions({
    getSelectedPaths: () => {
        syncImageHistorySelectionFromDOM();
        return [...imageHistorySelected];
    },
    getFile: path => getImageHistoryFile(path),
    getImageSrc: path => getHistoryImageSrc(path),
    parseMetadata: metadata => parseHistoryMetadata(metadata),
    setMetadataBoolValue: (metadata, key, value) => setMetadataBoolValue(metadata, key, value),
    setMetadataValue: (metadata, key, value) => setMetadataValue(metadata, key, value),
    isBusy: () => imageHistoryBulkActionRunning,
    setBusy: value => {
        imageHistoryBulkActionRunning = value;
        updateImageHistoryBulkControls();
    },
    clearSelection: () => clearImageHistorySelection(),
    requestRefresh: () => requestImageHistoryRefresh(),
    deleteSingle: (fullsrc, src, explicitEntry, errorHandle) => deleteSingleHistoryImage(fullsrc, src, explicitEntry, errorHandle),
    toggleHidden: (path, src, refreshAfter, errorHandle) => toggleImageHidden(path, src, refreshAfter, errorHandle),
    updateStarredCards: (src, starred) => {
        forEachSwarmImageCardForSrc(src, card => {
            if (card.setStarred) {
                card.setStarred(starred);
            }
            else {
                card.classList.toggle('image-block-starred', starred);
            }
        });
    }
});
```

The selected-path callback returns a new array, never the controller's mutable set.

- [ ] **Step 4: Replace bulk implementations with compatibility delegates**

Retain all current global names, parameters, defaults, and async return behavior. Use this complete mapping:

```text
hideSelectedImageHistory -> hideSelected
unhideSelectedImageHistory -> unhideSelected
deleteSelectedImageHistory -> deleteSelected
starSelectedImageHistory -> starSelected
unstarSelectedImageHistory -> unstarSelected
setSelectedImageHistoryRatingPrompt -> promptRating
setSelectedImageHistoryTagsPrompt -> promptTags
setSelectedImageHistoryNotesPrompt -> promptNotes
moveSelectedImageHistoryPrompt -> promptMove
exportSelectedImageHistoryMetadata -> exportMetadata
copySelectedImageHistoryPaths -> copyPaths
loadImageHistoryContactSheetImage -> loadContactSheetImage
createSelectedImageHistoryContactSheet -> createContactSheet
imageHistoryMetadataToPromptLabPrompt -> metadataToPromptLabPrompt
sendSelectedImageHistoryToPromptLab -> sendToPromptLab
setSelectedHistoryImagesStarred -> setStarred
setSelectedImageHistoryRating -> setRating
setSelectedImageHistoryTags -> setTags
setSelectedImageHistoryNotes -> setNotes
moveSelectedImageHistory -> move
setSelectedHistoryImagesHidden -> setHidden
deleteSelectedHistoryImages -> deleteImages
```

Async delegates must return the collaborator promise:

```javascript
/** Sets the rating of all selected history images. */
async function setSelectedImageHistoryRating(rating) {
    return await imageHistoryBulkActions.setRating(rating);
}
```

- [ ] **Step 5: Add the bulk script in final dependency order**

Use `apply_patch` so the working copy has:

```html
    <script src="js/genpage/gentab/imagehistoryfilter.js?vary=@Utilities.VaryID"></script>
    <script src="js/genpage/gentab/imagehistorycomparison.js?vary=@Utilities.VaryID"></script>
    <script src="js/genpage/gentab/imagehistorybulkactions.js?vary=@Utilities.VaryID"></script>
    <script src="js/genpage/gentab/outputhistory.js?vary=@Utilities.VaryID"></script>
```

- [ ] **Step 6: Verify and commit only the bulk stage**

Run:

```bash
node --check src/wwwroot/js/genpage/gentab/imagehistorybulkactions.js
node --check src/wwwroot/js/genpage/gentab/imagehistoryfilter.js
node --check src/wwwroot/js/genpage/gentab/imagehistorycomparison.js
node --check src/wwwroot/js/genpage/gentab/outputhistory.js
rg -n "imageHistorySelected|imageHistoryBulkActionRunning" src/wwwroot/js/genpage/gentab/imagehistorybulkactions.js
rg -n "^function (exportSelectedImageHistoryMetadata|setSelectedImageHistoryRating)|^async function (createSelectedImageHistoryContactSheet|deleteSelectedHistoryImages)" src/wwwroot/js/genpage/gentab/outputhistory.js
git diff --check
```

Expected: all files parse; the bulk class contains neither controller state name; compatibility delegates remain; no whitespace errors.

Stage the new class and controller facade normally, then interactively stage only the bulk script line:

```bash
git add -- src/wwwroot/js/genpage/gentab/imagehistorybulkactions.js src/wwwroot/js/genpage/gentab/outputhistory.js
git add -p src/Pages/Text2Image.cshtml
git diff --cached -- src/Pages/Text2Image.cshtml
git diff --cached --name-only
git diff --cached --check
git commit -m "Extract image history bulk actions"
```

Expected: exactly three files are committed and unrelated maintainer changes remain unstaged.

### Task 5: Consolidate `ImageHistoryController` State and Orchestration

**Files:**
- Modify: `src/wwwroot/js/genpage/gentab/outputhistory.js`
- Reference: `src/wwwroot/js/genpage/gentab/currentimagehandler.js`
- Reference: `src/wwwroot/js/genpage/helpers/generatehandler.js`
- Reference: `src/wwwroot/js/genpage/main.js`
- Reference: `src/wwwroot/js/site.js`

- [ ] **Step 1: Confirm all implementation files and Razor load lines are clean relative to the last stage**

Run:

```bash
git diff --quiet -- src/wwwroot/js/genpage/gentab/outputhistory.js src/wwwroot/js/genpage/gentab/imagehistorycomparison.js src/wwwroot/js/genpage/gentab/imagehistoryfilter.js src/wwwroot/js/genpage/gentab/imagehistorybulkactions.js
git diff --cached --quiet
```

Expected: both commands exit zero. Unrelated unstaged files remain present.

- [ ] **Step 2: Add the controller with all orchestration state in its constructor**

Use `apply_patch` to define `ImageHistoryController` in `outputhistory.js` after `ImageHistoryWindowManager`. Its constructor must contain the former globals as fields:

```javascript
class ImageHistoryController {
    /** Creates image-history state without starting a browser request. */
    constructor() {
        this.browser = null;
        this.selected = new Set();
        this.bulkActionRunning = false;
        this.showHidden = localStorage.getItem('image_history_show_hidden') == null ? window.userFeatureToggles?.imageHistoryShowHiddenDefault == true : localStorage.getItem('image_history_show_hidden') == 'true';
        this.hideGrids = localStorage.getItem('image_history_hide_grids') == null ? true : localStorage.getItem('image_history_hide_grids') == 'true';
        this.refreshQueued = false;
        this.hasLoadedOnce = false;
        this.initialAutoRetryUsed = false;
        this.autoRetryTimer = null;
        this.nextLoadIsRetry = false;
        this.startupStage = 'pending';
        this.loadToken = 0;
        this.backgroundLoadToken = 0;
        this.backgroundRetryCount = 0;
        this.backgroundRequestKey = null;
        this.backgroundWatchdog = null;
        this.backgroundRequestInFlight = false;
        this.initialLoadScheduled = false;
        this.savedRefreshTimer = null;
        this.savedRefreshAttempts = 0;
        this.savedRefreshTargets = new Set();
        this.registeredMediaButtons = [];
        this.windowManager = new ImageHistoryWindowManager();
        this.filter = null;
        this.comparison = null;
        this.bulkActions = null;
    }
}
```

Remove the corresponding top-level state declarations and standalone `imageHistoryWindowManager`/`registeredMediaButtons` instances. Keep threshold values as file-level configuration and change the touched `IMAGE_HISTORY_*` declarations from `const` to `let` to comply with repository JavaScript style; this changes no values or behavior.

- [ ] **Step 3: Move controller-owned functions into documented methods**

Mechanically relocate the current bodies, replace state globals with `this.<field>`, use `this.browser`, and change sibling calls to `this.<method>()`. The required method groups are:

```text
Extension/card registration:
registerMediaButton, getHistoryImageSrc

Refresh and saved insertion:
requestRefresh, hasFile, canIncludePath, addFoldersForPath, tryAddSavedImage,
scheduleSavedRefresh, notifySavedPath, rescanMetadata

Request lifecycle:
clearAutoRetry, clearBackgroundWatchdog, cancelBackgroundLoad, getRequestKey,
isBackgroundRequestRelevant, scheduleBackgroundWatchdog, retryManually,
ensureStatusReady, applyFeatureToggles, setRequestStatus, ensureBrowserShellReady,
scheduleInitialLoad, ensureHeaderControlsReady, scheduleAutoRetry

Sorting and list presentation:
orderFilesForDisplay, mapFiles, normalizeSortBy, sortSupportedByServer,
getSortNumber, getSortText, applyClientSort, sortFilesForDisplay, perfText,
isGridFolder, filterGridFiles, replaceBrowserContents, setMetadataBoolValue,
setMetadataValue, getFile

Selection and controls:
getEntries, pruneSelection, getCheckedPaths, syncSelectionFromDOM,
updateBulkControls, setSelection, clearSelection, selectAll, compareSelected,
ensureBulkControlsReady, getSelectedPaths, isBusy, setBusy

Single-image/UI primitives:
removeImageFromUI, deleteSingle, toggleHidden

Browser orchestration:
listFolderAndFiles, queueFullLoad, buttonsForImage, describeOutputFile,
selectOutput, storeWithCurrentParams, initialize
```

Pure method bodies remain otherwise unchanged. `getSelectedPaths()` must synchronize the DOM and return `[...this.selected]`. `setBusy(value)` must assign `this.bulkActionRunning` and call `this.updateBulkControls()`.

- [ ] **Step 4: Initialize the browser inside the controller**

Move the existing `GenPageBrowserClass` construction and configuration into `initialize(filter, comparison, bulkActions)`. Use bound arrows so callbacks retain controller ownership:

```javascript
/** Wires collaborators, builds the browser, and attaches history-tab events. */
initialize(filter, comparison, bulkActions) {
    this.filter = filter;
    this.comparison = comparison;
    this.bulkActions = bulkActions;
    this.browser = new GenPageBrowserClass(
        'image_history',
        (path, isRefresh, callback, depth, onError) => this.listFolderAndFiles(path, isRefresh, callback, depth, onError),
        'imagehistorybrowser',
        window.userFeatureToggles?.imageHistoryDefaultView || 'Thumbnails',
        image => this.describeOutputFile(image),
        (image, div) => this.selectOutput(image, div),
        IMAGE_HISTORY_HEADER_HTML
    );
    this.browser.allowMultiSelect = true;
    this.browser.maxPreBuild = IMAGE_HISTORY_FAST_FIRST_LIMIT;
    this.browser.filterMatcher = (desc, value) => this.filter.matches(desc, value);
    this.browser.filterServerSide = true;
    this.browser.folderSelectedEvent = () => this.clearSelection();
    this.browser.builtEvent = () => this.handleBrowserBuilt();
    getRequiredElementById('imagehistorytabclickable').addEventListener('shown.bs.tab', () => this.handleHistoryTabShown());
}
```

Define `IMAGE_HISTORY_HEADER_HTML` as a file-level `let` containing the existing header string unchanged. Add documented `handleBrowserBuilt()` and `handleHistoryTabShown()` methods containing the exact current event bodies and using `this.windowManager`/`this.browser`.

- [ ] **Step 5: Rewire collaborators to controller methods**

At the composition point, construct in this order and use controller callbacks only:

```javascript
let imageHistoryController = new ImageHistoryController();
let imageHistoryFilter = new ImageHistoryFilter();
let imageHistoryComparison = new ImageHistoryComparison({
    getFile: path => imageHistoryController.getFile(path),
    parseMetadata: metadata => imageHistoryFilter.parseMetadata(metadata),
    valueToSearchText: value => imageHistoryFilter.valueToSearchText(value),
    setMetadataValue: (metadata, key, value) => imageHistoryController.setMetadataValue(metadata, key, value),
    requestRefresh: () => imageHistoryController.requestRefresh(),
    selectCurrentImage: (src, metadata, batchId) => setCurrentImage(src, metadata, batchId)
});
let imageHistoryBulkActions = new ImageHistoryBulkActions({
    getSelectedPaths: () => imageHistoryController.getSelectedPaths(),
    getFile: path => imageHistoryController.getFile(path),
    getImageSrc: path => imageHistoryController.getHistoryImageSrc(path),
    parseMetadata: metadata => imageHistoryFilter.parseMetadata(metadata),
    setMetadataBoolValue: (metadata, key, value) => imageHistoryController.setMetadataBoolValue(metadata, key, value),
    setMetadataValue: (metadata, key, value) => imageHistoryController.setMetadataValue(metadata, key, value),
    isBusy: () => imageHistoryController.isBusy(),
    setBusy: value => imageHistoryController.setBusy(value),
    clearSelection: () => imageHistoryController.clearSelection(),
    requestRefresh: () => imageHistoryController.requestRefresh(),
    deleteSingle: (fullsrc, src, explicitEntry, errorHandle) => imageHistoryController.deleteSingle(fullsrc, src, explicitEntry, errorHandle),
    toggleHidden: (path, src, refreshAfter, errorHandle) => imageHistoryController.toggleHidden(path, src, refreshAfter, errorHandle),
    updateStarredCards: (src, starred) => imageHistoryController.updateStarredCards(src, starred)
});
imageHistoryController.initialize(imageHistoryFilter, imageHistoryComparison, imageHistoryBulkActions);
```

Add documented `updateStarredCards(src, starred)` to the controller using the exact callback body introduced in Task 4.

- [ ] **Step 6: Expose the controller-owned browser and retain compatibility delegates**

Remove the lexical `let imageHistoryBrowser` declaration. Immediately after controller initialization, expose the exact object through:

```javascript
Object.defineProperty(globalThis, 'imageHistoryBrowser', {
    configurable: true,
    get: () => imageHistoryController.browser
});
```

All former top-level functions must either be collaborator delegates from Tasks 2-4 or one-line controller delegates. Required cross-file delegates include:

```javascript
/** Lists image-history folders and files through the controller. */
function listOutputHistoryFolderAndFiles(path, isRefresh, callback, depth, onError = null) {
    return imageHistoryController.listFolderAndFiles(path, isRefresh, callback, depth, onError);
}

/** Ensures the image-history browser shell exists. */
function ensureImageHistoryBrowserShellReady() {
    return imageHistoryController.ensureBrowserShellReady();
}

/** Schedules the initial history load. */
function scheduleInitialImageHistoryLoad(delayMs = 0) {
    return imageHistoryController.scheduleInitialLoad(delayMs);
}

/** Notifies history that a saved path may now be available. */
function notifyImageHistorySavedPath(savedPath, metadata = null) {
    return imageHistoryController.notifySavedPath(savedPath, metadata);
}

/** Stores an image using the current generation parameters. */
function storeImageToHistoryWithCurrentParams(img) {
    return imageHistoryController.storeWithCurrentParams(img);
}
```

Retain delegates for every inline header handler and every previous top-level name unless an `rg` search proves it was private to an implementation now moved into a class. Do not retain any alternate state in a delegate.

- [ ] **Step 7: Prove singular ownership and cross-file compatibility statically**

Run:

```bash
node --check src/wwwroot/js/genpage/gentab/imagehistoryfilter.js
node --check src/wwwroot/js/genpage/gentab/imagehistorycomparison.js
node --check src/wwwroot/js/genpage/gentab/imagehistorybulkactions.js
node --check src/wwwroot/js/genpage/gentab/outputhistory.js
rg -n "^let imageHistory(Selected|BulkActionRunning|ShowHidden|HideGrids|RefreshQueued|HasLoadedOnce|LoadToken|Background|SavedRefresh|Browser|WindowManager)" src/wwwroot/js/genpage/gentab/outputhistory.js
rg -n "imageHistorySelected|imageHistoryBulkActionRunning|imageHistoryCompareFiles|imageHistoryComparePan|imageHistoryCompiledFilter|imageHistoryMetadataCache" src/wwwroot/js/genpage/gentab
rg -n "Object.defineProperty\(globalThis, 'imageHistoryBrowser'|^function (listOutputHistoryFolderAndFiles|ensureImageHistoryBrowserShellReady|scheduleInitialImageHistoryLoad|notifyImageHistorySavedPath|storeImageToHistoryWithCurrentParams)" src/wwwroot/js/genpage/gentab/outputhistory.js
rg -n "\b(imageHistoryBrowser|notifyImageHistorySavedPath|listOutputHistoryFolderAndFiles|ensureImageHistoryBrowserShellReady|scheduleInitialImageHistoryLoad|storeImageToHistoryWithCurrentParams)\b" src/wwwroot/js src/Pages --glob '!genpage/gentab/outputhistory.js' --glob '!*.bak' --glob '!lib/**'
git diff --check
```

Expected: all files parse; old mutable state globals have zero matches; the browser accessor and all required delegates exist; consumers are unchanged; no whitespace errors.

- [ ] **Step 8: Commit controller consolidation**

Run:

```bash
git add -- src/wwwroot/js/genpage/gentab/outputhistory.js
git diff --cached --name-only
git diff --cached --check
git commit -m "Consolidate image history controller"
```

Expected: only `outputhistory.js` is committed; unrelated maintainer changes remain unstaged.

### Task 6: Perform the Whole-Surface Static Audit

**Files:**
- Read: all four image-history JavaScript files
- Read: `src/Pages/Text2Image.cshtml`
- Read: maintained consumers found in Task 1

- [ ] **Step 1: Verify final script order from both HEAD and the working copy**

Run:

```bash
git show HEAD:src/Pages/Text2Image.cshtml | rg -n "imagehistory(filter|comparison|bulkactions)|outputhistory"
rg -n "imagehistory(filter|comparison|bulkactions)|outputhistory" src/Pages/Text2Image.cshtml
```

Expected in both views: filter, comparison, bulk actions, then output history, each exactly once with `?vary=@Utilities.VaryID`.

- [ ] **Step 2: Verify implementation ownership and delegate coverage**

Run:

```bash
rg -n "^class ImageHistory(Filter|Comparison|BulkActions|Controller)" src/wwwroot/js/genpage/gentab
rg -n "^(function |async function )" src/wwwroot/js/genpage/gentab/imagehistoryfilter.js src/wwwroot/js/genpage/gentab/imagehistorycomparison.js src/wwwroot/js/genpage/gentab/imagehistorybulkactions.js
rg -n "^(function |async function )" src/wwwroot/js/genpage/gentab/outputhistory.js
rg -n "genericRequest\('(ListImages|RescanImageMetadata|ToggleImageStarred|ToggleImageHidden|SetImageRating|SetImageTags|SetImageNotes|BulkMoveImages|DeleteImage|PromptLabSave|AddImageToHistory)'" src/wwwroot/js/genpage/gentab/imagehistory*.js src/wwwroot/js/genpage/gentab/outputhistory.js
```

Expected: one class per owner; no free-standing implementations in collaborator files; `outputhistory.js` free functions are compatibility delegates; each request remains in the intended single owner with unchanged route name.

- [ ] **Step 3: Compare protected contracts**

Run:

```bash
rg -n "image_history_(show_hidden|hide_grids|sort_by|sort_reverse|allow_anims)|imageHistory(Default|Advanced|Compare)|image_history_compare_|image_history_bulk_|image_history_request_" src/wwwroot/js/genpage/gentab/imagehistory*.js src/wwwroot/js/genpage/gentab/outputhistory.js
rg -n "genericRequest\(" src/wwwroot/js/genpage/gentab/imagehistory*.js src/wwwroot/js/genpage/gentab/outputhistory.js
rg -n "showError|doNoticePopover|confirm\(|prompt\(" src/wwwroot/js/genpage/gentab/imagehistory*.js src/wwwroot/js/genpage/gentab/outputhistory.js
```

Expected: identifiers, feature toggles, storage keys, request names, prompts, confirmations, notices, and errors match the baseline text and ownership map; no duplicate request workflow appears.

- [ ] **Step 4: Inspect the complete committed range and working-tree separation**

Run:

```bash
git log --oneline -5
git diff --stat HEAD~4..HEAD
git diff --check HEAD~4..HEAD
git diff --name-only HEAD~4..HEAD
git status --short
git diff --cached --quiet
```

Expected: the four implementation commits contain the three new collaborator files, `outputhistory.js`, and only the three intended script additions in `Text2Image.cshtml`; the index is empty; known maintainer changes and the backup directory remain unstaged and untouched.

- [ ] **Step 5: Prepare the maintainer validation checkpoint**

Do not run the application. Give the maintainer this exact live checklist:

```text
1. Cold-open history; verify fast-first loading, older-history completion, pagination,
   retry controls, and watchdog recovery.
2. Generate/save while history is closed and open; verify optimistic insertion,
   refresh fallback, metadata, current-image selection, and View In History.
3. Exercise text/structured/quoted/numeric/date filters, every sort/reverse/view,
   animation visibility, hidden images, grid hiding, and folders.
4. Exercise individual/select-all/clear/folder-change selection and refresh while selected.
5. Compare two images: swap, pan, zoom, reveal, diff, metadata, reuse, star, rate,
   and close back to the Generate image view.
6. Exercise metadata export, path copy, contact sheet, Prompt Lab, bulk star/rating/
   tags/notes/copy/move/hide/unhide/delete, including cancel/failure/partial success.
7. Recheck single-image star/hide/delete/open-folder/media buttons after bulk actions.
8. Generate and save edited images during refresh activity and confirm history navigation.
```

Expected: the maintainer reports the live result. Any regression starts a new `superpowers:systematic-debugging` pass; do not guess at a fix.
