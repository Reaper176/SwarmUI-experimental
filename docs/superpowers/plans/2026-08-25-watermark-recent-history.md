# Watermark Recent History Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Persist the five most recently submitted custom watermark images as clickable Generate-tab thumbnails and change watermark defaults to bottom-left alignment, 20% width, and 100% opacity.

**Architecture:** A watermark-specific frontend helper owns IndexedDB persistence, deduplication, eviction, thumbnail rendering, and restoration into the existing image input. The existing parameter renderer mounts the helper only for `watermarkimage`, while `GenerateHandler` records the collected custom image only immediately before a non-preview generation request is sent. Default values remain synchronized in C# parameter metadata, C# workflow fallbacks, and the managed Python Comfy node.

**Tech Stack:** C# 12 parameter/workflow metadata, browser JavaScript with IndexedDB and DOM APIs, CSS, Razor script loading, Python Comfy node metadata.

**Repository constraint:** `AGENTS.md` prohibits agents from running builds or tests. Every task uses static inspection and `git diff --check`; the maintainer performs the live verification checklist.

---

### Task 1: Synchronize Watermark Defaults

**Files:**
- Modify: `src/Text2Image/T2IParamTypes.cs:1020-1030`
- Modify: `src/BuiltinExtensions/ComfyUIBackend/WorkflowGenerator.cs:779-783`
- Modify: `src/BuiltinExtensions/ComfyUIBackend/ExtraNodes/SwarmComfyCommon/SwarmWatermark.py:203-236`

- [ ] **Step 1: Change Generate parameter defaults**

In `T2IParamTypes.cs`, keep resize at `20`, change alignment to `bottom-left`, and change opacity to `100`:

```csharp
WatermarkAlignment = Register<string>(new("Watermark Alignment", "Where to place the watermark on the image.",
    "bottom-left", GetValues: _ => ["bottom-right", "center", "top-center", "top-left", "top-right", "center-right", "center-left", "bottom-center", "bottom-left"], OrderPriority: 4, Group: GroupWatermark, FeatureFlag: "swarm_watermark", DoNotPreview: true
    ));
// WatermarkResizePercentage remains "20".
WatermarkOpacity = Register<double>(new("Watermark Opacity", "The watermark opacity percentage.",
    "100", Min: 0, Max: 100, Step: 1, ViewType: ParamViewType.SLIDER, OrderPriority: 7, Group: GroupWatermark, FeatureFlag: "swarm_watermark", DoNotPreview: true
    ));
```

- [ ] **Step 2: Change workflow fallback defaults**

In `WorkflowGenerator.ApplyFinalWatermark`, use the same fallbacks:

```csharp
[ComfyNodeInputNames.Watermark.Alignment] = UserInput.Get(T2IParamTypes.WatermarkAlignment, "bottom-left"),
[ComfyNodeInputNames.Watermark.OffsetPercentage] = UserInput.Get(T2IParamTypes.WatermarkOffsetPercentage, 2.0),
[ComfyNodeInputNames.Watermark.ResizePercentage] = UserInput.Get(T2IParamTypes.WatermarkResizePercentage, 20.0),
[ComfyNodeInputNames.Watermark.Opacity] = UserInput.Get(T2IParamTypes.WatermarkOpacity, 100.0)
```

- [ ] **Step 3: Change managed Comfy node defaults and defensive fallback**

Make `bottom-left` the first alignment choice, retain resize `20.0`, and use opacity `100.0` in both `INPUT_TYPES` and `_clamp_number`:

```python
"alignment": ([
    "bottom-left",
    "bottom-right",
    "center",
    "top-center",
    "top-left",
    "top-right",
    "center-right",
    "center-left",
    "bottom-center",
],),
"offset_percentage": ("FLOAT", {"default": 2.0, "min": 0.0, "max": 100.0, "step": 0.1}),
"resize_percentage": ("FLOAT", {"default": 20.0, "min": 0.1, "max": 200.0, "step": 0.1}),
"opacity": ("FLOAT", {"default": 100.0, "min": 0.0, "max": 100.0, "step": 1.0}),
```

```python
opacity_value = _clamp_number(opacity, 0.0, 100.0, 100.0) / 100.0
```

- [ ] **Step 4: Statically verify and commit**

Run:

```bash
git diff --check
rg -n 'bottom-left|20(\.0)?|100(\.0)?' src/Text2Image/T2IParamTypes.cs src/BuiltinExtensions/ComfyUIBackend/WorkflowGenerator.cs src/BuiltinExtensions/ComfyUIBackend/ExtraNodes/SwarmComfyCommon/SwarmWatermark.py
git diff -- src/Text2Image/T2IParamTypes.cs src/BuiltinExtensions/ComfyUIBackend/WorkflowGenerator.cs src/BuiltinExtensions/ComfyUIBackend/ExtraNodes/SwarmComfyCommon/SwarmWatermark.py
```

Expected: no whitespace errors; alignment, resize, and opacity defaults agree in all three layers; no unrelated changes.

Commit:

```bash
git add src/Text2Image/T2IParamTypes.cs src/BuiltinExtensions/ComfyUIBackend/WorkflowGenerator.cs src/BuiltinExtensions/ComfyUIBackend/ExtraNodes/SwarmComfyCommon/SwarmWatermark.py
git commit -m "Update watermark defaults"
```

### Task 2: Add Persistent Recent-Watermark Thumbnails

**Files:**
- Create: `src/wwwroot/js/genpage/gentab/watermarkhistory.js`
- Modify: `src/wwwroot/js/genpage/gentab/params.js:453-454`
- Modify: `src/wwwroot/css/site.css:124-137`
- Modify: `src/Pages/Text2Image.cshtml:175-181`

- [ ] **Step 1: Add the watermark history helper**

Create `watermarkhistory.js` as a singleton class. Use only `let`, full braces, and documented methods per repository JavaScript conventions. The complete public behavior is:

```javascript
class WatermarkRecentHistory {

    constructor() {
        this.databaseName = 'swarm-watermark-history';
        this.storeName = 'recent-watermarks';
        this.maxEntries = 5;
        this.containerId = 'watermark_recent_history';
        this.inputId = 'input_watermarkimage';
        this.writeQueue = Promise.resolve();
    }

    /** Opens the browser-local watermark history database. */
    openDatabase() {
        return new Promise((resolve, reject) => {
            let request = indexedDB.open(this.databaseName, 1);
            request.onupgradeneeded = () => {
                let database = request.result;
                if (!database.objectStoreNames.contains(this.storeName)) {
                    database.createObjectStore(this.storeName, { keyPath: 'id' });
                }
            };
            request.onsuccess = () => resolve(request.result);
            request.onerror = () => reject(request.error);
        });
    }

    /** Resolves after an IndexedDB transaction finishes. */
    waitForTransaction(transaction) {
        return new Promise((resolve, reject) => {
            transaction.oncomplete = () => resolve();
            transaction.onerror = () => reject(transaction.error);
            transaction.onabort = () => reject(transaction.error);
        });
    }

    /** Reads recent entries newest-first. */
    async getEntries() {
        let database = await this.openDatabase();
        try {
            let transaction = database.transaction(this.storeName, 'readonly');
            let request = transaction.objectStore(this.storeName).getAll();
            let entries = await new Promise((resolve, reject) => {
                request.onsuccess = () => resolve(request.result || []);
                request.onerror = () => reject(request.error);
            });
            await this.waitForTransaction(transaction);
            entries.sort((left, right) => right.updated - left.updated);
            return entries.slice(0, this.maxEntries);
        }
        finally {
            database.close();
        }
    }

    /** Converts a stored source into a browser-preview URL. */
    getPreviewSource(source) {
        return isValidMediaPath(source) ? `${getImageOutPrefix()}/${source}` : source;
    }

    /** Adds the thumbnail container beneath the Watermark Image input. */
    attach() {
        let input = document.getElementById(this.inputId);
        if (!input) {
            return;
        }
        let parent = findParentOfClass(input, 'auto-input');
        if (!parent || parent.querySelector(`#${this.containerId}`)) {
            return;
        }
        let container = createDiv(this.containerId, 'watermark-recent-history');
        container.setAttribute('aria-label', 'Recent watermark images');
        parent.appendChild(container);
        this.render().catch(error => console.warn('Failed to load recent watermark history.', error));
    }

    /** Restores one stored entry into the existing image input. */
    restoreEntry(entry) {
        let input = document.getElementById(this.inputId);
        if (!input) {
            return;
        }
        let previewSource = this.getPreviewSource(entry.source);
        let name = entry.name || 'Recent watermark';
        setMediaFileDirect(input, previewSource, 'image', name, name, () => {
            input.dataset.filedata = entry.source;
        });
    }

    /** Deletes a stale or evicted entry. */
    async deleteEntry(id) {
        let database = await this.openDatabase();
        try {
            let transaction = database.transaction(this.storeName, 'readwrite');
            transaction.objectStore(this.storeName).delete(id);
            await this.waitForTransaction(transaction);
        }
        finally {
            database.close();
        }
    }

    /** Renders up to five newest-first clickable thumbnails. */
    async render() {
        let container = document.getElementById(this.containerId);
        if (!container) {
            return;
        }
        let entries = await this.getEntries();
        container.innerHTML = '';
        container.style.display = entries.length == 0 ? 'none' : '';
        for (let entry of entries) {
            let button = document.createElement('button');
            button.type = 'button';
            button.className = 'watermark-recent-thumbnail';
            button.title = entry.name || 'Use recent watermark';
            let image = document.createElement('img');
            image.alt = entry.name || 'Recent watermark';
            image.src = this.getPreviewSource(entry.source);
            image.onerror = () => {
                this.deleteEntry(entry.id).then(() => this.render()).catch(error => console.warn('Failed to remove stale watermark history.', error));
            };
            button.addEventListener('click', () => this.restoreEntry(entry));
            button.appendChild(image);
            container.appendChild(button);
        }
    }

    /** Queues a submitted custom watermark for deduplicated persistence. */
    recordSubmitted(actualInput) {
        let source = actualInput?.watermarkimage;
        if (typeof source != 'string' || source.length == 0) {
            return Promise.resolve();
        }
        this.writeQueue = this.writeQueue.catch(() => { }).then(() => this.recordSource(source));
        return this.writeQueue;
    }

    /** Stores/promotes one source and evicts entries beyond the newest five. */
    async recordSource(source) {
        let entries = await this.getEntries();
        let existing = entries.find(entry => entry.source == source);
        let input = document.getElementById(this.inputId);
        let entry = {
            id: existing?.id || `${Date.now()}-${Math.random().toString(36).substring(2)}`,
            source: source,
            name: input?.dataset.filename || existing?.name || 'Watermark',
            updated: Date.now()
        };
        let retained = entries.filter(item => item.id != entry.id);
        retained.unshift(entry);
        let evicted = retained.slice(this.maxEntries);
        let database = await this.openDatabase();
        try {
            let transaction = database.transaction(this.storeName, 'readwrite');
            let store = transaction.objectStore(this.storeName);
            store.put(entry);
            for (let oldEntry of evicted) {
                store.delete(oldEntry.id);
            }
            await this.waitForTransaction(transaction);
        }
        finally {
            database.close();
        }
        await this.render();
    }
}

let watermarkRecentHistory = new WatermarkRecentHistory();
```

During implementation, preserve this interface and behavior. If static review finds an IndexedDB lifecycle race, fix it inside the helper without expanding feature scope.

- [ ] **Step 2: Mount history only for the main Watermark Image parameter**

In the `case 'image'` branch of `getHtmlForParam` in `params.js`, retain the existing input markup and add a runnable only when the main, non-preset parameter is `watermarkimage`:

```javascript
case 'image':
    return {
        html: makeImageInput(param.feature_flag, `${prefix}${param.id}`, param.id, param.name, param.description, param.toggleable, !param.no_popover, !isPreset) + pop,
        runnable: param.id == 'watermarkimage' && !isPreset ? () => watermarkRecentHistory.attach() : null
    };
```

Confirm the runnable collector already tolerates `null`; if it does not, conditionally omit or use an empty documented function. Do not add history to `watermarkmask` or any other image parameter.

- [ ] **Step 3: Style a compact responsive thumbnail row**

Add class-only CSS near `.auto-input-preview` in `site.css`:

```css
.watermark-recent-history {
    display: flex;
    flex-wrap: wrap;
    gap: 0.4rem;
    justify-content: center;
    margin: 0.5rem auto 0;
}
.watermark-recent-thumbnail {
    width: 3.25rem;
    height: 3.25rem;
    padding: 0.15rem;
    overflow: hidden;
    border: 1px solid var(--bs-border-color);
    border-radius: 0.25rem;
    background: var(--bs-body-bg);
}
.watermark-recent-thumbnail:hover,
.watermark-recent-thumbnail:focus-visible {
    border-color: var(--bs-primary);
}
.watermark-recent-thumbnail img {
    width: 100%;
    height: 100%;
    object-fit: contain;
}
```

- [ ] **Step 4: Load the helper before parameter rendering**

In the `Text2Image.cshtml` Scripts section, add the new script immediately before `params.js`:

```html
<script src="js/genpage/gentab/watermarkhistory.js?vary=@Utilities.VaryID"></script>
<script src="js/genpage/gentab/params.js?vary=@Utilities.VaryID"></script>
```

- [ ] **Step 5: Statically verify and commit**

Run:

```bash
git diff --check
rg -n "watermarkRecentHistory|watermark-recent|watermarkhistory.js" src/wwwroot/js/genpage/gentab/watermarkhistory.js src/wwwroot/js/genpage/gentab/params.js src/wwwroot/css/site.css src/Pages/Text2Image.cshtml
git diff -- src/wwwroot/js/genpage/gentab/watermarkhistory.js src/wwwroot/js/genpage/gentab/params.js src/wwwroot/css/site.css src/Pages/Text2Image.cshtml
```

Expected: the helper is loaded before `params.js`; only `watermarkimage` mounts the row; five-entry IndexedDB persistence is bounded, deduplicated, and failure-tolerant; CSS uses classes; no unrelated changes.

Commit:

```bash
git add src/wwwroot/js/genpage/gentab/watermarkhistory.js src/wwwroot/js/genpage/gentab/params.js src/wwwroot/css/site.css src/Pages/Text2Image.cshtml
git commit -m "Add recent watermark thumbnails"
```

### Task 3: Record Only Submitted Non-Preview Watermarks

**Files:**
- Modify: `src/wwwroot/js/genpage/helpers/generatehandler.js:532-593`

- [ ] **Step 1: Hook persistence at the request boundary**

Inside the `run` closure of `GenerateHandler.doGenerate`, after model validation/preloading has reached `run` and immediately before reusing or creating the generation WebSocket, queue history persistence without awaiting it:

```javascript
if (!isPreview) {
    watermarkRecentHistory.recordSubmitted(actualInput).catch(error => {
        console.warn('Failed to store recent watermark history.', error);
    });
}
if (this.sockets[socketId] && this.sockets[socketId].readyState == WebSocket.OPEN) {
```

This placement means selection alone, parameter collection elsewhere, preview generation, and failed model validation do not change history. `recordSubmitted` checks `actualInput.watermarkimage`, so a disabled Watermark group or preset-only submission is a no-op. Generation must continue regardless of IndexedDB outcome.

- [ ] **Step 2: Trace all submission conditions statically**

Inspect the completed `doGenerate` flow and confirm:

- normal Generate, Generate Forever, and other callers that reach `doGenerate` record the submitted custom watermark;
- `_preview` calls never record;
- no model / installer-aborted flows never reach the hook;
- a disabled Watermark group omits `watermarkimage` from `actualInput`;
- failures in the history promise cannot prevent the WebSocket send.

- [ ] **Step 3: Commit the submission hook**

```bash
git add src/wwwroot/js/genpage/helpers/generatehandler.js
git commit -m "Record submitted watermark history"
```

- [ ] **Step 4: Run final static verification**

Run:

```bash
git diff --check HEAD~3..HEAD
git status --short
git diff --stat HEAD~3..HEAD
rg -n "bottom-left|WatermarkOpacity|resize_percentage|opacity|watermarkRecentHistory|watermark-recent|watermarkhistory.js" src/Text2Image/T2IParamTypes.cs src/BuiltinExtensions/ComfyUIBackend/WorkflowGenerator.cs src/BuiltinExtensions/ComfyUIBackend/ExtraNodes/SwarmComfyCommon/SwarmWatermark.py src/wwwroot/js/genpage/gentab/watermarkhistory.js src/wwwroot/js/genpage/gentab/params.js src/wwwroot/js/genpage/helpers/generatehandler.js src/wwwroot/css/site.css src/Pages/Text2Image.cshtml
```

Expected: only the eight planned implementation files changed, the worktree is clean after commits, and all history/default references are consistent.

### Task 4: Maintainer Live Verification Checklist

**Files:** No code changes.

- [ ] **Step 1: Hand the maintainer this checklist**

After the agent's static review, the maintainer should restart/rebuild SwarmUI and verify:

1. Watermark defaults display `bottom-left`, `20`, and `100` in Generate and in the raw Comfy node.
2. With Watermark disabled, submitting a generation does not add history.
3. With Watermark enabled but no custom image, a preset-only generation does not add history.
4. Selecting/uploading/pasting a custom image without generating does not add history.
5. Submitting a custom-watermark generation adds one thumbnail beneath `Watermark Image`.
6. Reloading the browser and restarting SwarmUI retains the thumbnail.
7. Clicking a thumbnail restores its preview and the next submitted generation uses it.
8. Reusing an older thumbnail promotes it to newest without duplication.
9. Six unique submitted watermarks retain only the newest five.
10. Deleting a referenced `inputs/...` file causes its stale thumbnail to disappear after load failure.
11. Custom masks do not appear in recent watermark history.
12. Preview generations do not reorder or add history.
13. IndexedDB denial/failure logs a warning but generation still submits.
14. Still images and video outputs use bottom-left placement at 20% width and 100% opacity unless overridden.
