# Tab-Aware Background Loops Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Reduce idle browser activity by stopping recurring Server and Logs polling loops when their owning tabs are not active, without affecting Generate-critical status polling.

**Architecture:** Keep the existing polling logic, but move interval ownership behind explicit start/stop helpers keyed off actual tab visibility. Reuse existing click-driven top-tab behavior and current server-subtab activation so loops only run when users are looking at the relevant UI.

**Tech Stack:** Vanilla JavaScript, existing SwarmUI tab handlers, existing polling functions.

---

### Task 1: Make server resource polling tab-aware

**Files:**
- Modify: `src/wwwroot/js/genpage/main.js`

- [ ] **Step 1: Add start/stop helpers around the server resource interval**
- [ ] **Step 2: Start the loop only when the Server top tab becomes active**
- [ ] **Step 3: Stop the loop when the user leaves the Server top tab or the page becomes hidden**
- [ ] **Step 4: Verify syntax**

Run: `node --check src/wwwroot/js/genpage/main.js`
Expected: exit code `0`

### Task 2: Make server logs polling tab-aware

**Files:**
- Modify: `src/wwwroot/js/genpage/server/logs.js`

- [ ] **Step 1: Replace the unconditional logs interval startup with explicit start/stop helpers**
- [ ] **Step 2: Re-evaluate loop state on Logs tab click, Server top-tab click, server subtab clicks, and page visibility changes**
- [ ] **Step 3: Keep the first-load behavior for log type loading unchanged**
- [ ] **Step 4: Verify syntax**

Run: `node --check src/wwwroot/js/genpage/server/logs.js`
Expected: exit code `0`

### Task 3: Final static verification

**Files:**
- Modify: `src/wwwroot/js/genpage/main.js`
- Modify: `src/wwwroot/js/genpage/server/logs.js`

- [ ] **Step 1: Run diff formatting verification**

Run: `git diff --check`
Expected: no output

- [ ] **Step 2: Manual runtime checks for the developer**

Ask the developer to verify:
- Server resource cards update while the Server tab is active
- leaving the Server tab stops those updates
- opening Logs starts log updates
- leaving Logs or leaving the Server top tab stops log updates
