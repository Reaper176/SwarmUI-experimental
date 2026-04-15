# Large List Snappiness Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make large browser-backed lists feel faster to open, filter, and scroll by avoiding unnecessary synchronous description work on the main thread.

**Architecture:** Keep the existing browser UI and chunk-loader behavior, but stop eagerly computing entry descriptions for unfiltered off-screen items. Only compute `describe(...)` when an item is actually being rendered or when filtering/sorting truly requires it.

**Tech Stack:** Vanilla JavaScript, existing `GenPageBrowserClass` render pipeline.

---

### Task 1: Lazy description generation for large unfiltered lists

**Files:**
- Modify: `src/wwwroot/js/genpage/helpers/browsers.js`

- [ ] **Step 1: Treat prewrapped browser entries as lazy if `desc` is absent, rather than forcing eager description generation**
- [ ] **Step 2: Only compute `describe(...)` eagerly when filtering or filter sorting requires it**
- [ ] **Step 3: Compute `describe(...)` just-in-time during actual DOM build for visible/chunked entries**
- [ ] **Step 4: Verify syntax**

Run: `node --check src/wwwroot/js/genpage/helpers/browsers.js`
Expected: exit code `0`

### Task 2: Slightly slow filter-triggered rebuilds for very large lists

**Files:**
- Modify: `src/wwwroot/js/genpage/helpers/browsers.js`

- [ ] **Step 1: Increase filter debounce modestly for very large current lists so repeated keystrokes do not trigger heavy rebuild churn**
- [ ] **Step 2: Keep small/medium list responsiveness unchanged**
- [ ] **Step 3: Verify syntax**

Run: `node --check src/wwwroot/js/genpage/helpers/browsers.js`
Expected: exit code `0`

### Task 3: Final static verification

**Files:**
- Modify: `src/wwwroot/js/genpage/helpers/browsers.js`

- [ ] **Step 1: Run diff formatting verification**

Run: `git diff --check`
Expected: no output

- [ ] **Step 2: Manual runtime checks for the developer**

Ask the developer to verify:
- opening large model/history browsers feels faster
- scrolling still hydrates deferred content correctly
- filtering large lists still works correctly, with no missing results
