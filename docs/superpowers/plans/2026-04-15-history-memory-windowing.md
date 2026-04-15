# History Memory Windowing Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Reduce idle browser memory use in the History tab by unloading image media for rows far outside the viewport while preserving placeholders, scroll height, and scroll position.

**Architecture:** Keep the existing History file list and browser DOM intact. Add a History-only viewport manager that computes row bands from rendered entry positions, keeps a buffer of nearby rows hydrated, and dehydrates far-away rows by removing loaded image sources while retaining original URLs for later rehydration.

**Tech Stack:** Vanilla JavaScript, existing `GenPageBrowserClass` browser helper, existing SwarmUI browser lazyload conventions.

---

### Task 1: Preserve original browser image URLs

**Files:**
- Modify: `src/wwwroot/js/genpage/helpers/browsers.js`

- [ ] **Step 1: Update browser entry image setup to preserve the original media URL even after lazyload hydrates**

Add a durable dataset field alongside the existing lazyload field when each browser entry image is created:

```js
img.classList.add('lazyload');
img.dataset.src = desc.image;
img.dataset.origSrc = desc.image;
```

- [ ] **Step 2: Keep the existing lazyload behavior unchanged except that it should continue to work when `dataset.src` is restored later**

Do not change `BrowserUtil.makeVisible` semantics beyond continuing to load `img.dataset.src` into `img.src` and deleting `dataset.src`.

- [ ] **Step 3: Verify no syntax regressions**

Run: `node --check src/wwwroot/js/genpage/helpers/browsers.js`
Expected: exit code `0`

### Task 2: Add a History viewport window manager

**Files:**
- Modify: `src/wwwroot/js/genpage/gentab/outputhistory.js`

- [ ] **Step 1: Add History windowing constants and a singleton manager near the top-level History state**

Add constants for the row buffer and a small unload threshold, then add a singleton class that tracks:
- the browser content element
- a queued update flag
- resize/scroll listeners
- the currently computed row tops

Use a shape like:

```js
const IMAGE_HISTORY_UNLOAD_ROW_BUFFER = 10;
const IMAGE_HISTORY_MIN_MEDIA_ROWS_TO_UNLOAD = 2;

class ImageHistoryWindowManager {
    constructor() {
        this.content = null;
        this.updateQueued = false;
        this.boundScroll = null;
        this.boundResize = null;
    }
}

let imageHistoryWindowManager = new ImageHistoryWindowManager();
```

- [ ] **Step 2: Implement content attachment and throttled updates**

Add methods to:
- attach to `imageHistoryBrowser.contentDiv`
- register one `scroll` listener on the content container
- register one `resize` listener on `window`
- queue updates with `requestAnimationFrame`

Use methods like:

```js
attach(content) { ... }
queueUpdate() { ... }
updateVisibleWindow() { ... }
```

- [ ] **Step 3: Implement row-band calculation using placeholder positions**

Inside `updateVisibleWindow()`, gather rendered History entries with `dataset.name`, compute row bands from `offsetTop`, determine the visible row range from `scrollTop` and `clientHeight`, then expand by `IMAGE_HISTORY_UNLOAD_ROW_BUFFER`.

Use the entry placeholders as the layout source. Do not remove entry nodes.

- [ ] **Step 4: Implement media hydration and dehydration helpers**

Add methods that only touch the image element under a History entry:

```js
hydrateEntry(entry) {
    let img = entry.querySelector('img.image-block-img-inner');
    if (!img || img.src || !img.dataset.origSrc) {
        return;
    }
    img.dataset.src = img.dataset.origSrc;
    img.classList.add('lazyload');
}

dehydrateEntry(entry) {
    let img = entry.querySelector('img.image-block-img-inner');
    if (!img || !img.dataset.origSrc) {
        return;
    }
    img.classList.add('lazyload');
    img.dataset.src = img.dataset.origSrc;
    img.removeAttribute('src');
}
```

After hydrating nearby entries, call `browserUtil.queueMakeVisible(this.content)` so the existing lazyload path loads them.

- [ ] **Step 5: Keep the visible band and nearby rows hydrated, unload only far-away rows**

For each entry row:
- keep hydrated if row index is between `visibleStart - buffer` and `visibleEnd + buffer`
- otherwise dehydrate only if the row is at least `IMAGE_HISTORY_MIN_MEDIA_ROWS_TO_UNLOAD` beyond that retained band

This avoids churn right at the boundary.

- [ ] **Step 6: Attach the manager on History builds and tab reveal**

Hook the new manager into the existing History lifecycle:
- in `imageHistoryBrowser.builtEvent`, attach to `imageHistoryBrowser.contentDiv` and queue an update
- in the existing `shown.bs.tab` handler for `imagehistorytabclickable`, queue another update after `browserUtil.queueMakeVisible(...)`

- [ ] **Step 7: Preserve current behaviors**

Do not change:
- History selection logic
- History delete/hide actions
- current-image interactions
- folder navigation
- full History list loading logic

The only new behavior is off-window image media unloading.

- [ ] **Step 8: Verify no syntax regressions**

Run: `node --check src/wwwroot/js/genpage/gentab/outputhistory.js`
Expected: exit code `0`

### Task 3: Final static verification

**Files:**
- Modify: `src/wwwroot/js/genpage/helpers/browsers.js`
- Modify: `src/wwwroot/js/genpage/gentab/outputhistory.js`

- [ ] **Step 1: Run diff formatting verification**

Run: `git diff --check`
Expected: no output

- [ ] **Step 2: Manual runtime checks for the developer**

Ask the developer to verify:
- open History and scroll deep into a large folder
- pause on a deep scroll position and confirm nearby rows remain visible
- scroll farther and confirm far-away rows reload when revisited
- verify selection, hide, delete, and current-image click behavior still work
