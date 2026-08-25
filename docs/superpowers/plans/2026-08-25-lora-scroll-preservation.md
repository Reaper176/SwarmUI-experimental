# LoRA Browser Scroll Preservation Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Keep the LoRA browser at its current scroll offset whenever a LoRA is activated or deactivated.

**Architecture:** Add a LoRA-only scroll guard around `ModelBrowserWrapper.selectModel`. Capture the active browser content element and its numeric offset before the selection callback, then restore that offset after the callback's already-queued layout recalculation, provided the same element is still connected.

**Tech Stack:** Browser JavaScript, DOM `scrollTop`, asynchronous `setTimeout` layout sequencing.

---

### Task 1: Preserve the LoRA Browser Scroll Offset

**Files:**
- Modify: `src/wwwroot/js/genpage/gentab/models.js:1490`

Repository policy does not permit agent-run tests or builds. Validation is therefore limited to static checks followed by the developer's live browser reproduction.

- [ ] **Step 1: Record the failing live behavior**

In the running SwarmUI page, open the LoRAs tab, scroll well below the first screen, and activate a LoRA. Confirm that the browser returns to the top. This is performed by the developer, not the agent.

- [ ] **Step 2: Add the targeted scroll guard**

Replace `ModelBrowserWrapper.selectModel` with:

```javascript
    selectModel(model) {
        let contentDiv = this.subType == 'LoRA' ? this.browser.contentDiv : null;
        let scrollTop = contentDiv ? contentDiv.scrollTop : null;
        this.selectOne(model);
        this.rebuildSelectedClasses();
        if (scrollTop != null) {
            setTimeout(() => {
                if (this.browser.contentDiv == contentDiv && contentDiv.isConnected) {
                    contentDiv.scrollTop = scrollTop;
                }
            }, 1);
        }
    }
```

The restoration timer is registered after `selectOne` queues the LoRA bottom-bar layout update, so it runs after that existing recalculation. The element identity and connection checks prevent stale restoration after navigation or replacement.

- [ ] **Step 3: Perform static validation**

Run:

```bash
git diff --check -- src/wwwroot/js/genpage/gentab/models.js
git diff -- src/wwwroot/js/genpage/gentab/models.js
```

Expected: no whitespace errors; the diff contains only the LoRA-specific capture and delayed restoration inside `selectModel`.

- [ ] **Step 4: Verify the source ordering**

Run:

```bash
sed -n '1484,1510p' src/wwwroot/js/genpage/gentab/models.js
```

Expected: `scrollTop` is captured before `this.selectOne(model)`, and restoration is scheduled after `this.rebuildSelectedClasses()`.

- [ ] **Step 5: Perform the live regression check**

After refreshing SwarmUI so the updated JavaScript loads, scroll down the LoRAs tab and activate at least three LoRAs at different positions. Confirm that the browser retains the same offset after each activation and that each selected card receives `model-selected`. Then deactivate one LoRA and confirm both the scroll offset and selection styling remain correct. This is performed by the developer, not the agent.

- [ ] **Step 6: Commit the implementation after live validation**

```bash
git add src/wwwroot/js/genpage/gentab/models.js
git commit -m "Fix LoRA browser scroll reset on selection"
```
