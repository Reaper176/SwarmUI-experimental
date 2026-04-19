# Browser Header More Menu Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace the browser header's horizontal scrolling fallback with a live `More` menu that moves lower-priority right-side controls under a dropdown while keeping the filter, everything to its left, and the `Sort:` selector visible.

**Architecture:** Keep the browser header as a single row, but split it into measured control groups inside the shared browser helper. Controls to the right of the pinned boundary become overflow candidates and move into a dropdown from right to left as available width shrinks; they are restored in original order as width returns.

**Tech Stack:** Vanilla JavaScript in `src/wwwroot/js/genpage/helpers/browsers.js`, shared CSS in `src/wwwroot/css/genpage.css`, existing Swarm popover/button styles.

---

### Task 1: Define Control Group Boundaries

**Files:**
- Modify: `src/wwwroot/js/genpage/helpers/browsers.js`
- Review: `src/wwwroot/js/genpage/gentab/outputhistory.js`

- [ ] Identify the fixed left-side browser controls created in the shared helper: display format, refresh, up-folder, depth, filter.
- [ ] Parse `extraHeader` controls into stable DOM groups that preserve current label/control pairs and existing event listeners.
- [ ] Mark the `Sort:` label/select pair as pinned even though it appears to the right of the filter in tabs like Output History.
- [ ] Treat remaining groups to the right of that pinned boundary as overflow candidates in their existing DOM order so rightmost controls move first.

### Task 2: Add Shared More-Menu Layout Logic

**Files:**
- Modify: `src/wwwroot/js/genpage/helpers/browsers.js`

- [ ] Build a single-row header container with a main controls strip plus a hidden `More` button/menu host.
- [ ] Move only overflow candidate groups into the `More` menu when measured width exceeds the available row width.
- [ ] Restore groups from the `More` menu back into the main row in original order when space becomes available again.
- [ ] Recalculate on initial render, browser rebuild, window resize, and browser splitter width changes without requiring a page refresh.
- [ ] Keep the original DOM nodes so existing control values and listeners survive moves unchanged.

### Task 3: Style The Header And Dropdown

**Files:**
- Modify: `src/wwwroot/css/genpage.css`

- [ ] Replace the header's horizontal scrollbar behavior with overflow-hidden row layout plus a positioned dropdown menu for the `More` button.
- [ ] Keep the header height to one row so the content area does not gain a new vertical scrollbar.
- [ ] Ensure the dropdown renders above the card/content area and is not clipped by the header container.

### Task 4: Static Verification And Manual Test Notes

**Files:**
- Review: `src/wwwroot/js/genpage/helpers/browsers.js`
- Review: `src/wwwroot/css/genpage.css`

- [ ] Check that pinned groups cannot be moved into `More`.
- [ ] Check that overflow candidates move in right-to-left priority order and restore correctly when resizing wider.
- [ ] Confirm the resize triggers cover browser window changes and splitter movement.
- [ ] Leave manual verification notes for Output History and at least one model browser tab because repo policy forbids agent-run live testing here.
