# Safe Browser Followups Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add low-risk browser snappiness followups by caching expensive entry descriptions where safe and tightening in-place class refresh paths.

**Architecture:** Keep the shared browser behavior the same, but add opt-in description caching so metadata-heavy browsers can reuse derived entry descriptions across rerenders. Pair that with a cheaper visible-entry class refresh path for model selection updates.

**Tech Stack:** Vanilla JavaScript, existing `GenPageBrowserClass`, existing model browser wrappers.

---

### Task 1: Add opt-in description caching to shared browsers

**Files:**
- Modify: `src/wwwroot/js/genpage/helpers/browsers.js`

- [ ] **Step 1: Add an opt-in description cache map to `GenPageBrowserClass`**
- [ ] **Step 2: Cache and reuse `describe(...)` results only when the browser explicitly enables it**
- [ ] **Step 3: Clear that cache on refresh/light refresh so stale descriptions do not linger after data changes**
- [ ] **Step 4: Verify syntax**

Run: `node --check src/wwwroot/js/genpage/helpers/browsers.js`
Expected: exit code `0`

### Task 2: Enable caching for model browsers and tighten in-place class refresh

**Files:**
- Modify: `src/wwwroot/js/genpage/gentab/models.js`

- [ ] **Step 1: Enable description caching for model browsers only**
- [ ] **Step 2: Keep selection/star/load class updates in-place without forcing full rerenders**
- [ ] **Step 3: Verify syntax**

Run: `node --check src/wwwroot/js/genpage/gentab/models.js`
Expected: exit code `0`

### Task 3: Final static verification

**Files:**
- Modify: `src/wwwroot/js/genpage/helpers/browsers.js`
- Modify: `src/wwwroot/js/genpage/gentab/models.js`

- [ ] **Step 1: Run diff formatting verification**

Run: `git diff --check`
Expected: no output

- [ ] **Step 2: Manual runtime checks for the developer**

Ask the developer to verify:
- model tabs still render correctly across tab switches
- selecting models/LoRAs/embeddings updates highlight state without a visible full rerender
- metadata-heavy model cards reopen faster after the first render
