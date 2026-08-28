# Image History Scroll Anchor Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Keep the same History image at the same viewport offset across every browser rebuild, refresh, and page reload unless the user deliberately scrolls elsewhere.

**Architecture:** Extend the shared browser with opt-in lifecycle hooks, a refresh-scroll opt-out, and a requested prebuild target while preserving all existing defaults. Add a History-owned scroll manager that persists a stable image-path anchor, suppresses rebuild-generated scroll events, ensures the anchor is rendered, and restores its viewport-relative offset after layout.

**Tech Stack:** Browser JavaScript, DOM events and geometry, `localStorage`, `requestAnimationFrame`, existing `GenPageBrowserClass` and `ImageHistoryController` infrastructure.

---

### Task 1: Add opt-in browser rebuild controls

**Files:**
- Modify: `src/wwwroot/js/genpage/helpers/browsers.js:251-310`
- Modify: `src/wwwroot/js/genpage/helpers/browsers.js:596-630`
- Modify: `src/wwwroot/js/genpage/helpers/browsers.js:820-890`
- Modify: `src/wwwroot/js/genpage/helpers/browsers.js:1175-1527`

- [ ] **Step 1: Add default-safe browser properties**

Add these properties to `GenPageBrowserClass`'s constructor near the existing event callbacks and rendering state:

```javascript
this.beforeBuildEvent = null;
this.resetScrollOnRefresh = true;
this.preBuildTarget = null;
```

The defaults preserve every existing browser's behavior.

- [ ] **Step 2: Make explicit refresh-to-top optional**

Replace the unconditional assignment in `update` with:

```javascript
if (isRefresh) {
    this.tree = new BrowserTreePart('', false, null, null, '');
    if (this.resetScrollOnRefresh) {
        this.contentDiv.scrollTop = 0;
    }
    this.describeCache.clear();
}
```

- [ ] **Step 3: Let an owner prepare immediately before a real rebuild**

In `build`, invoke the lifecycle callback only after the unchanged-signature early return and after `lastFiles` has been assigned, but before any existing DOM is cleared:

```javascript
if (this.beforeBuildEvent) {
    this.beforeBuildEvent(path, folders, files);
}
```

This placement avoids suspending scroll tracking for builds that do not mutate the DOM.

- [ ] **Step 4: Ensure a requested anchor is included in the synchronous render**

In `buildContentList`, after filtering and sorting `entries` but before computing the normal render limit, locate the optional target:

```javascript
let preBuildTargetIndex = -1;
if (this.preBuildTarget) {
    preBuildTargetIndex = entries.findIndex(entry => entry.file?.name == this.preBuildTarget);
}
```

After the existing `maxBuildNow` calculation, expand the limit only when the target exists:

```javascript
if (preBuildTargetIndex >= 0) {
    maxBuildNow = Math.max(maxBuildNow, preBuildTargetIndex);
}
```

After the top-level `buildContentList(this.contentDiv, files)` call in `build`, clear the one-build request:

```javascript
this.preBuildTarget = null;
```

This renders enough preceding entries to calculate the anchor's real layout position without changing lazy chunking for ordinary browsers or builds with a missing anchor.

- [ ] **Step 5: Review the shared-browser boundary statically**

Confirm by inspection that `beforeBuildEvent`, `resetScrollOnRefresh`, and `preBuildTarget` are unused unless explicitly enabled, and that the existing `builtEvent` still fires after content rendering.

### Task 2: Implement History's persistent scroll manager

**Files:**
- Modify: `src/wwwroot/js/genpage/gentab/outputhistory.js:1-160`

- [ ] **Step 1: Add the persistence key**

Add a single storage key near the other image-history constants:

```javascript
let IMAGE_HISTORY_SCROLL_POSITION_KEY = 'image_history_scroll_position';
```

- [ ] **Step 2: Add `ImageHistoryScrollManager`**

Place the class before `ImageHistoryWindowManager`. Implement these responsibilities:

```javascript
class ImageHistoryScrollManager {
    constructor() {
        this.browser = null;
        this.content = null;
        this.anchorPath = null;
        this.anchorOffset = 0;
        this.fallbackScrollTop = 0;
        this.captureQueued = false;
        this.restoring = false;
        this.restoreToken = 0;
        this.boundQueueCapture = this.queueCapture.bind(this);
        this.load();
    }

    /** Loads the persisted user-selected History position. */
    load() {
        try {
            let saved = JSON.parse(localStorage.getItem(IMAGE_HISTORY_SCROLL_POSITION_KEY) || 'null');
            if (!saved || typeof saved != 'object') {
                return;
            }
            this.anchorPath = typeof saved.anchorPath == 'string' ? saved.anchorPath : null;
            this.anchorOffset = Number.isFinite(saved.anchorOffset) ? saved.anchorOffset : 0;
            this.fallbackScrollTop = Number.isFinite(saved.scrollTop) ? Math.max(0, saved.scrollTop) : 0;
        }
        catch (error) {
            localStorage.removeItem(IMAGE_HISTORY_SCROLL_POSITION_KEY);
        }
    }

    /** Persists the current user-selected History position. */
    save() {
        try {
            localStorage.setItem(IMAGE_HISTORY_SCROLL_POSITION_KEY, JSON.stringify({
                anchorPath: this.anchorPath,
                anchorOffset: this.anchorOffset,
                scrollTop: this.fallbackScrollTop
            }));
        }
        catch (error) {
        }
    }

    /** Attaches scroll tracking to the current History content element. */
    attach(browser) {
        this.browser = browser;
        let content = browser?.contentDiv || null;
        if (this.content == content) {
            return;
        }
        if (this.content) {
            this.content.removeEventListener('scroll', this.boundQueueCapture);
        }
        this.content = content;
        if (this.content) {
            this.content.addEventListener('scroll', this.boundQueueCapture);
        }
    }

    /** Queues at most one user-position capture per animation frame. */
    queueCapture() {
        if (this.restoring || this.captureQueued) {
            return;
        }
        this.captureQueued = true;
        let run = () => {
            this.captureQueued = false;
            if (!this.restoring) {
                this.capture();
            }
        };
        if (window.requestAnimationFrame) {
            requestAnimationFrame(run);
        }
        else {
            setTimeout(run, 16);
        }
    }

    /** Captures the first visible image and its viewport-relative offset. */
    capture() {
        if (!this.content || !this.content.isConnected) {
            return;
        }
        let entries = Array.from(this.content.children).filter(entry => entry?.dataset?.name);
        if (entries.length == 0) {
            return;
        }
        let scrollTop = this.content.scrollTop;
        let anchor = entries[entries.length - 1];
        for (let entry of entries) {
            if (entry.offsetTop + entry.offsetHeight > scrollTop) {
                anchor = entry;
                break;
            }
        }
        this.anchorPath = anchor.dataset.name;
        this.anchorOffset = anchor.offsetTop - scrollTop;
        this.fallbackScrollTop = scrollTop;
        this.save();
    }

    /** Suspends capture and asks the browser to synchronously render the saved anchor. */
    beforeBuild() {
        this.attach(this.browser);
        this.restoring = true;
        this.restoreToken++;
        this.browser.preBuildTarget = this.anchorPath;
    }

    /** Restores the saved image after the rebuilt layout becomes measurable. */
    afterBuild() {
        this.attach(this.browser);
        if (!this.restoring || !this.content) {
            return;
        }
        let token = this.restoreToken;
        let run = () => {
            if (token != this.restoreToken || !this.content?.isConnected) {
                return;
            }
            let anchor = this.anchorPath ? this.browser.getVisibleEntry(this.anchorPath) : null;
            let targetScrollTop = anchor ? anchor.offsetTop - this.anchorOffset : this.fallbackScrollTop;
            let maxScrollTop = Math.max(0, this.content.scrollHeight - this.content.clientHeight);
            this.content.scrollTop = Math.max(0, Math.min(targetScrollTop, maxScrollTop));
            let finish = () => {
                if (token == this.restoreToken) {
                    this.restoring = false;
                }
            };
            if (window.requestAnimationFrame) {
                requestAnimationFrame(finish);
            }
            else {
                setTimeout(finish, 16);
            }
        };
        if (window.requestAnimationFrame) {
            requestAnimationFrame(() => requestAnimationFrame(run));
        }
        else {
            setTimeout(run, 32);
        }
    }
}
```

Keep full `/** ... */` documentation on the class methods, use `let`, and retain the repository's braced-block and `else` formatting conventions.

- [ ] **Step 3: Check the manager's state transitions**

Trace these sequences on paper:

1. User scroll event -> capture anchor -> persist.
2. Build begins -> `restoring = true` -> content clear event ignored.
3. Build renders through anchor -> double-animation-frame restore -> restoration event ignored -> capture resumes.
4. Fast-first load lacks old anchor -> fallback offset is clamped without replacing the anchor -> background full load can restore the original image.
5. Filter hides anchor -> fallback is used -> clearing filter restores anchor unless the user deliberately scrolled while filtered.

### Task 3: Wire preservation into Image History only

**Files:**
- Modify: `src/wwwroot/js/genpage/gentab/outputhistory.js:145-190`
- Modify: `src/wwwroot/js/genpage/gentab/outputhistory.js:1560-1625`

- [ ] **Step 1: Own the scroll manager in `ImageHistoryController`**

Add this constructor field next to `windowManager`:

```javascript
this.scrollManager = new ImageHistoryScrollManager();
```

- [ ] **Step 2: Configure the History browser lifecycle**

Immediately after creating `this.browser` in `initialize`, add:

```javascript
this.scrollManager.attach(this.browser);
this.browser.resetScrollOnRefresh = false;
this.browser.beforeBuildEvent = () => {
    this.scrollManager.beforeBuild();
};
```

Extend the existing `builtEvent` callback without removing its current initialization:

```javascript
this.browser.builtEvent = () => {
    this.handleBrowserBuilt();
    this.scrollManager.afterBuild();
};
```

This makes the lifecycle apply to `ensureBuilt`, normal request callbacks, direct replacement, rerender, saved-image insertion, and background fill without wrapping each caller separately.

- [ ] **Step 3: Preserve existing History viewport hydration**

Do not remove `ImageHistoryWindowManager`. After restoration, queue its existing update through `handleBrowserBuilt`/the attached scroll event so media hydration follows the restored viewport. Confirm the scroll manager does not call `browserUtil.makeVisible` directly or duplicate image-loading policy.

### Task 4: Perform repository-approved static verification

**Files:**
- Verify: `src/wwwroot/js/genpage/helpers/browsers.js`
- Verify: `src/wwwroot/js/genpage/gentab/outputhistory.js`
- Verify: `docs/superpowers/specs/2026-08-27-image-history-scroll-anchor-design.md`

- [ ] **Step 1: Run JavaScript syntax checks**

Run:

```bash
node --check src/wwwroot/js/genpage/helpers/browsers.js
node --check src/wwwroot/js/genpage/gentab/outputhistory.js
```

Expected: both commands exit successfully with no output.

- [ ] **Step 2: Run whitespace and targeted source checks**

Run:

```bash
git diff --check
rg -n "beforeBuildEvent|resetScrollOnRefresh|preBuildTarget|ImageHistoryScrollManager|IMAGE_HISTORY_SCROLL_POSITION_KEY" src/wwwroot/js/genpage/helpers/browsers.js src/wwwroot/js/genpage/gentab/outputhistory.js
rg -n "contentDiv\.scrollTop = 0" src/wwwroot/js/genpage/helpers/browsers.js
```

Expected: no whitespace errors; all new symbols appear only in the intended shared hooks and History wiring; the remaining top-reset assignment is guarded by `resetScrollOnRefresh`.

- [ ] **Step 3: Inspect the final diff without touching user data**

Run:

```bash
git diff -- src/wwwroot/js/genpage/helpers/browsers.js src/wwwroot/js/genpage/gentab/outputhistory.js
git status --short
```

Expected: implementation edits are limited to the two JavaScript files. Existing `src/Data`, backup, and temporary worktree changes remain untouched.

- [ ] **Step 4: Commit the implementation files only**

```bash
git add src/wwwroot/js/genpage/helpers/browsers.js src/wwwroot/js/genpage/gentab/outputhistory.js
git commit -m "Preserve image history scroll anchor"
```

- [ ] **Step 5: Hand off live verification to the developer**

Do not run builds or automated tests. Ask the developer to start SwarmUI and confirm the same recognizable image remains at the same viewport offset after explicit refresh, fast-to-full background replacement, new image insertion, filter/clear-filter, sort/reverse, view-format change, tab switching, and full page reload.
