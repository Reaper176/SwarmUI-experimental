# Idle Memory Models And Simple Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Reduce idle browser memory in model browsers and the Simple tab batch strip without changing active Generate behavior or adding disk-backed caching.

**Architecture:** Add one shared browser-media window manager for model browsers that dehydrates off-window preview images while preserving placeholders and scroll state. Add a separate Simple batch-strip manager that dehydrates hidden or far-off horizontal thumbnails while keeping workflow history data and the current main preview intact.

**Tech Stack:** Vanilla JavaScript, existing `GenPageBrowserClass`, existing SwarmUI lazyload image flow.

---

### Task 1: Add shared browser media windowing support

**Files:**
- Modify: `src/wwwroot/js/genpage/helpers/browsers.js`

- [ ] **Step 1: Add a reusable browser media window manager class**

Create a helper that can attach to a browser content container, compute visible row bands from entry `offsetTop`, keep a configurable row buffer hydrated, and dehydrate far-away entry images by removing `src` while preserving `dataset.origSrc`.

- [ ] **Step 2: Let `GenPageBrowserClass` own an optional media window manager**

Add a `mediaWindowManager` property initialized to `null`, queue its updates on content scroll, and attach it during `build()` after `contentDiv` exists.

- [ ] **Step 3: Verify syntax**

Run: `node --check src/wwwroot/js/genpage/helpers/browsers.js`
Expected: exit code `0`

### Task 2: Enable windowed preview dehydration for model browsers

**Files:**
- Modify: `src/wwwroot/js/genpage/gentab/models.js`

- [ ] **Step 1: Give each `ModelBrowserWrapper` a media window manager**

When constructing each wrapper browser, assign a `BrowserMediaWindowManager` with a moderate row buffer suitable for model cards and thumbnails.

- [ ] **Step 2: Refresh hydration on tab reveal**

When a model-related tab is shown, queue the browser media window manager update after the browser becomes visible so off-window previews stay unloaded and nearby rows rehydrate.

- [ ] **Step 3: Verify syntax**

Run: `node --check src/wwwroot/js/genpage/gentab/models.js`
Expected: exit code `0`

### Task 3: Add Simple batch-strip thumbnail dehydration

**Files:**
- Modify: `src/wwwroot/js/genpage/simpletab.js`

- [ ] **Step 1: Add a Simple batch-strip window manager**

Create a small helper that attaches to `simple_current_image_batch_wrapper`, computes the visible horizontal range from `scrollLeft` and `clientWidth`, keeps a horizontal buffer hydrated, and dehydrates far-off batch images by removing `src` while preserving a stored original source.

- [ ] **Step 2: Preserve original sources on batch entries**

When adding or restoring Simple batch entries, stamp each batch image with `dataset.origSrc` so it can be rehydrated later.

- [ ] **Step 3: Update Simple lifecycle hooks**

Queue batch-strip updates when:
- Simple loads a workflow batch history
- a new preview or result is added
- the batch strip scrolls
- the top tab changes

When Simple becomes hidden, dehydrate all batch thumbnails in the strip. Do not clear workflow history data or the main current preview image.

- [ ] **Step 4: Verify syntax**

Run: `node --check src/wwwroot/js/genpage/simpletab.js`
Expected: exit code `0`

### Task 4: Final static verification

**Files:**
- Modify: `src/wwwroot/js/genpage/helpers/browsers.js`
- Modify: `src/wwwroot/js/genpage/gentab/models.js`
- Modify: `src/wwwroot/js/genpage/simpletab.js`

- [ ] **Step 1: Run diff formatting verification**

Run: `git diff --check`
Expected: no output

- [ ] **Step 2: Manual runtime checks for the developer**

Ask the developer to verify:
- model tabs still load and scroll normally
- scrolling back through model lists reloads far-off previews as they re-enter view
- Simple workflow switching still works
- Simple batch thumbnails reload when scrolled back into view
- hiding Simple does not clear the current main preview or workflow inputs
