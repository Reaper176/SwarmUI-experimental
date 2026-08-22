# Grid First-Run Preview Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Preload a cold selected model before starting Grid Generator so its first run can emit live previews.

**Architecture:** Reuse `mainGenHandler.preloadCurrentModelForPreviews`, the existing normal-generation helper that waits for the selected model to be loaded. Invoke the grid request only from the helper callback.

**Tech Stack:** Browser JavaScript and the existing SwarmUI generation frontend.

---

### Task 1: Preload before starting a grid

**Files:**
- Modify: `src/BuiltinExtensions/GridGenerator/Assets/grid_gen.js:456-465`
- Test: Maintainer manual cold-model grid run. Automated tests and builds are prohibited by `AGENTS.md`.

- [ ] **Step 1: Record the failing reproduction**

Unload or switch away from the selected model, run a multi-cell Grid Generator request with Show Outputs enabled, and observe that no live preview appears. Interrupt it and rerun after the model has loaded; previews then appear.

- [ ] **Step 2: Add the minimal existing-helper call**

Within the successful `currentModelHelper.ensureCurrentModel` callback in `doGenWrapper`, replace the direct generation call:

```js
this.doGenerate();
```

with:

```js
mainGenHandler.preloadCurrentModelForPreviews(document.getElementById('current_model').value, () => this.doGenerate());
```

- [ ] **Step 3: Static callback-flow review**

Confirm that the selected-model-empty error still returns before preloading, preload completes or safely falls back through its existing callback behavior, and `this.doGenerate()` remains called exactly once from that callback.

- [ ] **Step 4: Maintainer live verification**

With a cold model, run a two-or-more-cell grid with Show Outputs enabled. Confirm previews appear on the first run and each final image replaces its preview.

- [ ] **Step 5: Commit implementation**

```bash
git add src/BuiltinExtensions/GridGenerator/Assets/grid_gen.js
git commit -m "fix: preload models before grid previews"
```
