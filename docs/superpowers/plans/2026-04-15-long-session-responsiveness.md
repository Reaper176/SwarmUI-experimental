# Long Session Responsiveness Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Improve long-session responsiveness by bounding client-side transient state growth and cleaning up stale generation/log bookkeeping without affecting the visible recent stream.

**Architecture:** Use a hybrid strategy: lifecycle cleanup for state that becomes stale when batches finish or sockets close, plus explicit count-based caps for long-lived in-memory collections. Keep visible recent images and active generation behavior unchanged.

**Tech Stack:** Vanilla JavaScript, existing GenerateHandler flow, existing Server Logs cache.

---

### Task 1: Bound server log cache growth correctly

**Files:**
- Modify: `src/wwwroot/js/genpage/server/logs.js`

- [ ] **Step 1: Fix the per-type log cache trimming condition to use actual key count instead of object `.length`**
- [ ] **Step 2: Keep only a bounded recent window per log type, for example the newest 1536 entries after crossing a 2048-entry threshold**
- [ ] **Step 3: Verify syntax**

Run: `node --check src/wwwroot/js/genpage/server/logs.js`
Expected: exit code `0`

### Task 2: Replace lifetime generation timing totals with a rolling window

**Files:**
- Modify: `src/wwwroot/js/genpage/helpers/generatehandler.js`
- Modify: `src/wwwroot/js/genpage/main.js`

- [ ] **Step 1: Change generation timing stats from unbounded totals to a rolling sample window in `GenerateHandler`**
- [ ] **Step 2: Update the status-bar estimate in `main.js` to use the rolling average if samples exist**
- [ ] **Step 3: Verify syntax**

Run: `node --check src/wwwroot/js/genpage/helpers/generatehandler.js`
Expected: exit code `0`

Run: `node --check src/wwwroot/js/genpage/main.js`
Expected: exit code `0`

### Task 3: Clean up stale socket references on error/close paths

**Files:**
- Modify: `src/wwwroot/js/genpage/helpers/generatehandler.js`

- [ ] **Step 1: Clear dead socket references when generation sockets close or error so the sockets map does not retain stale objects indefinitely**
- [ ] **Step 2: Preserve current preview/result handling and recent visible batch behavior**
- [ ] **Step 3: Verify syntax**

Run: `node --check src/wwwroot/js/genpage/helpers/generatehandler.js`
Expected: exit code `0`

### Task 4: Final static verification

**Files:**
- Modify: `src/wwwroot/js/genpage/server/logs.js`
- Modify: `src/wwwroot/js/genpage/helpers/generatehandler.js`
- Modify: `src/wwwroot/js/genpage/main.js`

- [ ] **Step 1: Run diff formatting verification**

Run: `git diff --check`
Expected: no output

- [ ] **Step 2: Manual runtime checks for the developer**

Ask the developer to verify:
- recent preview/result stream still behaves the same during active generation
- status time estimates still update sensibly after many generations
- Server Logs still shows recent messages and remains responsive during long viewing sessions
