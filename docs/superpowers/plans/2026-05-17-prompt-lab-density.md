# Prompt Lab Density Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Reduce Prompt Lab visual density with progressive disclosure while preserving existing Prompt Lab behavior.

**Architecture:** Keep the existing `PromptLab` class and server APIs. Restructure the Razor markup into section headers and collapsible bodies, add small UI state helpers to `promptlab.js`, and style the new section patterns in `genpage.css`.

**Tech Stack:** Razor `.cshtml`, plain JavaScript using existing SwarmUI conventions, standard CSS in `src/wwwroot/css/genpage.css`.

---

## File Structure

- Modify `src/Pages/_Generate/PromptLabTab.cshtml`: reorganize the existing Prompt Lab controls into compact section headers and collapsible bodies.
- Modify `src/wwwroot/js/genpage/promptlab.js`: add section toggle state, initialize collapsed defaults, and auto-open Warnings when warnings exist.
- Modify `src/wwwroot/css/genpage.css`: style Prompt Lab section headers, compact actions, collapsed bodies, and more stable column spacing.

## Task 1: Markup Progressive Disclosure

**Files:**
- Modify: `src/Pages/_Generate/PromptLabTab.cshtml`

- [ ] **Step 1: Replace the left library toolbar/list markup**

Replace the contents of `<div class="prompt-lab-column prompt-lab-library">...</div>` with:

```html
<div class="prompt-lab-column prompt-lab-library">
    <div class="prompt-lab-panel-header">
        <span class="prompt-lab-panel-title translate">Library</span>
        <div class="prompt-lab-actions">
            <button class="basic-button translate" onclick="promptLab.exportLibrary()">Export</button>
            <button class="basic-button translate" onclick="promptLab.openImportPicker()">Import</button>
        </div>
    </div>
    <label class="prompt-lab-autosave"><input type="checkbox" id="prompt_lab_autosave" autocomplete="off"> Auto Save</label>
    <input id="prompt_lab_import_file" type="file" accept="application/json,.json" class="prompt-lab-hidden-file" onchange="promptLab.importLibraryFile(this)" />
    <input class="auto-text prompt-lab-search" id="prompt_lab_search" placeholder="Search library" oninput="promptLab.scheduleSearchRender('all')" />

    <div class="prompt-lab-section" data-prompt-lab-section="saved_prompts">
        <button type="button" class="prompt-lab-section-header" onclick="promptLab.toggleSection('saved_prompts')">
            <span class="prompt-lab-section-title translate">Saved Prompts</span>
            <span class="prompt-lab-section-state" id="prompt_lab_section_saved_prompts_state">-</span>
        </button>
        <div class="prompt-lab-section-actions">
            <button class="basic-button translate" onclick="promptLab.newPrompt()">New</button>
            <button class="basic-button translate" onclick="promptLab.savePrompt()">Save</button>
            <button class="basic-button translate" onclick="promptLab.savePromptVariant()">Variant</button>
            <button class="basic-button translate" onclick="promptLab.duplicatePrompt()">Duplicate</button>
            <button class="basic-button translate" onclick="promptLab.deletePrompt()">Delete</button>
            <button class="basic-button translate" onclick="promptLab.togglePromptFavorite()">Favorite</button>
        </div>
        <div class="prompt-lab-section-body" id="prompt_lab_section_saved_prompts_body">
            <div id="prompt_lab_prompt_list" class="prompt-lab-list"></div>
        </div>
    </div>

    <div class="prompt-lab-section" data-prompt-lab-section="fragments">
        <button type="button" class="prompt-lab-section-header" onclick="promptLab.toggleSection('fragments')">
            <span class="prompt-lab-section-title translate">Fragments</span>
            <span class="prompt-lab-section-state" id="prompt_lab_section_fragments_state">+</span>
        </button>
        <div class="prompt-lab-section-actions">
            <button class="basic-button translate" onclick="promptLab.newFragment()">New</button>
            <button class="basic-button translate" onclick="promptLab.saveFragment()">Save</button>
            <button class="basic-button translate" onclick="promptLab.deleteFragment()">Delete</button>
            <button class="basic-button translate" onclick="promptLab.toggleFragmentFavorite()">Favorite</button>
        </div>
        <div class="prompt-lab-section-body" id="prompt_lab_section_fragments_body">
            <input class="auto-text prompt-lab-search" id="prompt_lab_fragment_search" placeholder="Search fragments" oninput="promptLab.scheduleSearchRender('fragments')" />
            <div id="prompt_lab_fragment_list" class="prompt-lab-list"></div>
        </div>
    </div>

    <div class="prompt-lab-section" data-prompt-lab-section="wildcards">
        <button type="button" class="prompt-lab-section-header" onclick="promptLab.toggleSection('wildcards')">
            <span class="prompt-lab-section-title translate">Wildcard Sets</span>
            <span class="prompt-lab-section-state" id="prompt_lab_section_wildcards_state">+</span>
        </button>
        <div class="prompt-lab-section-actions">
            <button class="basic-button translate" onclick="promptLab.newWildcardSet()">New</button>
            <button class="basic-button translate" onclick="promptLab.saveWildcardSet()">Save</button>
            <button class="basic-button translate" onclick="promptLab.deleteWildcardSet()">Delete</button>
            <button class="basic-button translate" onclick="promptLab.toggleWildcardFavorite()">Favorite</button>
        </div>
        <div class="prompt-lab-section-body" id="prompt_lab_section_wildcards_body">
            <input class="auto-text prompt-lab-search" id="prompt_lab_wildcard_search" placeholder="Search wildcards" oninput="promptLab.scheduleSearchRender('wildcards')" />
            <div id="prompt_lab_wildcard_list" class="prompt-lab-list"></div>
        </div>
    </div>

    <div class="prompt-lab-section" data-prompt-lab-section="history">
        <button type="button" class="prompt-lab-section-header" onclick="promptLab.toggleSection('history')">
            <span class="prompt-lab-section-title translate">Recent Prompts</span>
            <span class="prompt-lab-section-state" id="prompt_lab_section_history_state">+</span>
        </button>
        <div class="prompt-lab-section-actions">
            <button class="basic-button translate" onclick="promptLab.clearHistory()">Clear</button>
        </div>
        <div class="prompt-lab-section-body" id="prompt_lab_section_history_body">
            <div id="prompt_lab_history_list" class="prompt-lab-list"></div>
        </div>
    </div>
</div>
```

- [ ] **Step 2: Replace the middle editor markup**

Replace the contents of `<div class="prompt-lab-column prompt-lab-editor">...</div>` with:

```html
<div class="prompt-lab-column prompt-lab-editor">
    <div class="prompt-lab-editor-header">
        <input class="auto-text prompt-lab-title" id="prompt_lab_name" placeholder="Prompt name" oninput="promptLab.scheduleAutoSave()" />
        <div class="prompt-lab-actions">
            <button class="basic-button translate" onclick="promptLab.savePrompt()">Save</button>
            <button class="basic-button translate" onclick="promptLab.savePromptVariant()">Variant</button>
            <button class="basic-button translate" onclick="promptLab.resetPrompt()">Reset</button>
        </div>
    </div>
    <textarea class="auto-text prompt-lab-textarea" id="prompt_lab_positive" placeholder="Positive prompt" oninput="promptLab.refreshPreview(); promptLab.scheduleAutoSave()"></textarea>
    <textarea class="auto-text prompt-lab-textarea prompt-lab-negative" id="prompt_lab_negative" placeholder="Negative prompt" oninput="promptLab.refreshPreview(); promptLab.scheduleAutoSave()"></textarea>
    <input class="auto-text prompt-lab-tags" id="prompt_lab_tags" placeholder="Tags, comma-separated" oninput="promptLab.scheduleAutoSave()" />
    <textarea class="auto-text prompt-lab-notes" id="prompt_lab_notes" placeholder="Notes" oninput="promptLab.scheduleAutoSave()"></textarea>

    <div class="prompt-lab-section" data-prompt-lab-section="wildcard_editor">
        <button type="button" class="prompt-lab-section-header" onclick="promptLab.toggleSection('wildcard_editor')">
            <span class="prompt-lab-section-title translate">Wildcard Set Editor</span>
            <span class="prompt-lab-section-state" id="prompt_lab_section_wildcard_editor_state">+</span>
        </button>
        <div class="prompt-lab-section-body" id="prompt_lab_section_wildcard_editor_body">
            <input class="auto-text prompt-lab-title" id="prompt_lab_wildcard_name" placeholder="Wildcard name" />
            <textarea class="auto-text prompt-lab-wildcard-values" id="prompt_lab_wildcard_values" placeholder="Wildcard values, one per line"></textarea>
            <input class="auto-text" id="prompt_lab_wildcard_tags" placeholder="Wildcard tags, comma-separated" />
            <button class="basic-button translate" onclick="promptLab.insertSelectedWildcard()">Insert Token</button>
        </div>
    </div>

    <div class="prompt-lab-section" data-prompt-lab-section="fragment_editor">
        <button type="button" class="prompt-lab-section-header" onclick="promptLab.toggleSection('fragment_editor')">
            <span class="prompt-lab-section-title translate">Fragment Editor</span>
            <span class="prompt-lab-section-state" id="prompt_lab_section_fragment_editor_state">+</span>
        </button>
        <div class="prompt-lab-section-body" id="prompt_lab_section_fragment_editor_body">
            <input class="auto-text prompt-lab-title" id="prompt_lab_fragment_name" placeholder="Fragment name" />
            <textarea class="auto-text prompt-lab-fragment-text" id="prompt_lab_fragment_text" placeholder="Fragment text"></textarea>
            <input class="auto-text" id="prompt_lab_fragment_category" placeholder="Fragment category" />
            <input class="auto-text" id="prompt_lab_fragment_tags" placeholder="Fragment tags, comma-separated" />
            <button class="basic-button translate" onclick="promptLab.insertSelectedFragment()">Insert Fragment</button>
        </div>
    </div>
</div>
```

- [ ] **Step 3: Replace the right preview markup**

Replace the contents of `<div class="prompt-lab-column prompt-lab-preview">...</div>` with:

```html
<div class="prompt-lab-column prompt-lab-preview">
    <div class="prompt-lab-panel-header">
        <span class="prompt-lab-panel-title translate">Preview</span>
        <div class="prompt-lab-actions">
            <button class="basic-button translate" onclick="promptLab.sendToGenerate()">Send</button>
            <button class="basic-button translate" onclick="promptLab.previewWildcards()">Preview</button>
            <button class="basic-button translate" onclick="promptLab.generateWildcardCombinations()">Generate</button>
        </div>
    </div>
    <div class="prompt-lab-section-actions prompt-lab-export-actions">
        <button class="basic-button translate" onclick="promptLab.exportWildcardCombinations('txt')">Export Text</button>
        <button class="basic-button translate" onclick="promptLab.exportWildcardCombinations('json')">Export JSON</button>
        <button class="basic-button translate" onclick="promptLab.exportWildcardCombinations('csv')">Export CSV</button>
    </div>
    <div class="prompt-lab-wildcard-controls">
        <select class="auto-dropdown" id="prompt_lab_wildcard_mode">
            <option value="all">All Combinations</option>
            <option value="random_single">Random Single</option>
            <option value="random_batch">Random Batch</option>
            <option value="sample">Sample N</option>
        </select>
        <input class="auto-text prompt-lab-small-input" id="prompt_lab_sample_count" type="number" min="1" value="25" />
        <input class="auto-text prompt-lab-small-input" id="prompt_lab_max_combinations" type="number" min="1" value="1000" />
        <label><input type="checkbox" id="prompt_lab_shuffle_results" autocomplete="off"> Shuffle</label>
    </div>

    <div class="prompt-lab-section" data-prompt-lab-section="detected_wildcards">
        <button type="button" class="prompt-lab-section-header" onclick="promptLab.toggleSection('detected_wildcards')">
            <span class="prompt-lab-section-title translate">Detected Wildcards</span>
            <span class="prompt-lab-section-state" id="prompt_lab_section_detected_wildcards_state">-</span>
        </button>
        <div class="prompt-lab-section-body" id="prompt_lab_section_detected_wildcards_body">
            <div id="prompt_lab_wildcards" class="prompt-lab-output"></div>
        </div>
    </div>

    <div class="prompt-lab-section" data-prompt-lab-section="preview">
        <button type="button" class="prompt-lab-section-header" onclick="promptLab.toggleSection('preview')">
            <span class="prompt-lab-section-title translate">Preview</span>
            <span class="prompt-lab-section-state" id="prompt_lab_section_preview_state">-</span>
        </button>
        <div class="prompt-lab-section-body" id="prompt_lab_section_preview_body">
            <div id="prompt_lab_preview" class="prompt-lab-output"></div>
        </div>
    </div>

    <div class="prompt-lab-section" data-prompt-lab-section="warnings">
        <button type="button" class="prompt-lab-section-header" onclick="promptLab.toggleSection('warnings')">
            <span class="prompt-lab-section-title translate">Warnings</span>
            <span class="prompt-lab-section-state" id="prompt_lab_section_warnings_state">+</span>
        </button>
        <div class="prompt-lab-section-body" id="prompt_lab_section_warnings_body">
            <div id="prompt_lab_warnings" class="prompt-lab-output"></div>
        </div>
    </div>

    <div class="prompt-lab-section" data-prompt-lab-section="diff">
        <button type="button" class="prompt-lab-section-header" onclick="promptLab.toggleSection('diff')">
            <span class="prompt-lab-section-title translate">Diff</span>
            <span class="prompt-lab-section-state" id="prompt_lab_section_diff_state">+</span>
        </button>
        <div class="prompt-lab-section-body" id="prompt_lab_section_diff_body">
            <select class="auto-dropdown prompt-lab-compare-select" id="prompt_lab_compare_select" onchange="promptLab.refreshPreview()"></select>
            <div id="prompt_lab_diff" class="prompt-lab-output"></div>
        </div>
    </div>
</div>
```

- [ ] **Step 4: Commit markup**

Run:

```bash
git add src/Pages/_Generate/PromptLabTab.cshtml
git commit -m "Restructure Prompt Lab layout"
```

## Task 2: Prompt Lab Collapse State

**Files:**
- Modify: `src/wwwroot/js/genpage/promptlab.js`

- [ ] **Step 1: Add section defaults in the constructor**

Add this after `this.autoSaveTimeout = null;`:

```javascript
this.sectionState = {
    saved_prompts: true,
    fragments: false,
    wildcards: false,
    history: false,
    wildcard_editor: false,
    fragment_editor: false,
    detected_wildcards: true,
    preview: true,
    warnings: false,
    diff: false
};
```

- [ ] **Step 2: Initialize section state during `init()`**

Add this before `this.load();`:

```javascript
this.applyAllSectionStates();
```

- [ ] **Step 3: Make search all render all library lists**

In `scheduleSearchRender(kind)`, add this branch before the `kind == 'prompts'` branch:

```javascript
if (kind == 'all') {
    this.renderPromptList();
    this.renderFragmentList();
    this.renderWildcardList();
}
```

- [ ] **Step 4: Add section helper methods before `enablePromptDrop(box)`**

Add:

```javascript
/** Toggles a Prompt Lab collapsible section. */
toggleSection(section) {
    this.sectionState[section] = !this.sectionState[section];
    this.applySectionState(section);
}

/** Forces a Prompt Lab collapsible section open or closed. */
setSectionOpen(section, isOpen) {
    this.sectionState[section] = !!isOpen;
    this.applySectionState(section);
}

/** Applies all Prompt Lab collapsible section states. */
applyAllSectionStates() {
    for (let section of Object.keys(this.sectionState)) {
        this.applySectionState(section);
    }
}

/** Applies one Prompt Lab collapsible section state. */
applySectionState(section) {
    let body = document.getElementById(`prompt_lab_section_${section}_body`);
    let state = document.getElementById(`prompt_lab_section_${section}_state`);
    let isOpen = this.sectionState[section] == true;
    if (body) {
        body.style.display = isOpen ? '' : 'none';
    }
    if (state) {
        state.innerText = isOpen ? '-' : '+';
    }
}
```

- [ ] **Step 5: Auto-open Warnings when warnings exist in `refreshPreview()`**

After `warningBox.innerHTML = this.renderWarnings(warnings);`, add:

```javascript
this.setSectionOpen('warnings', warnings.length > 0);
```

- [ ] **Step 6: Auto-open Warnings when preview wildcard warnings exist**

In `previewWildcards()`, after `warningBox.innerHTML = this.renderWarnings(warnings);`, add the same line:

```javascript
this.setSectionOpen('warnings', warnings.length > 0);
```

- [ ] **Step 7: Run static JS check**

Run:

```bash
node --check src/wwwroot/js/genpage/promptlab.js
```

Expected: no output and exit code 0.

- [ ] **Step 8: Commit JS**

Run:

```bash
git add src/wwwroot/js/genpage/promptlab.js
git commit -m "Add Prompt Lab section toggles"
```

## Task 3: Prompt Lab Density CSS

**Files:**
- Modify: `src/wwwroot/css/genpage.css`

- [ ] **Step 1: Replace the Prompt Lab CSS block**

Replace the existing `.prompt-lab-wrapper` through `.prompt-lab-diff-same` block with:

```css
.prompt-lab-wrapper {
    display: grid;
    grid-template-columns: minmax(15rem, 0.85fr) minmax(25rem, 1.45fr) minmax(18rem, 0.95fr);
    gap: 0.7rem;
    height: 100%;
    padding: 0.7rem;
}

.prompt-lab-column {
    min-height: 0;
    overflow: auto;
    border: 1px solid var(--light-border);
    background: var(--background);
    padding: 0.65rem;
}

.prompt-lab-panel-header,
.prompt-lab-editor-header {
    display: flex;
    align-items: center;
    justify-content: space-between;
    gap: 0.5rem;
    margin-bottom: 0.55rem;
}

.prompt-lab-panel-title {
    font-size: 1.05rem;
    font-weight: 700;
}

.prompt-lab-actions,
.prompt-lab-section-actions {
    display: flex;
    flex-wrap: wrap;
    gap: 0.3rem;
}

.prompt-lab-actions .basic-button,
.prompt-lab-section-actions .basic-button {
    min-height: 1.9rem;
    padding: 0.2rem 0.45rem;
}

.prompt-lab-section {
    border: 1px solid var(--light-border);
    background: var(--background-panel-subtle);
    margin-bottom: 0.55rem;
}

.prompt-lab-section-header {
    display: flex;
    align-items: center;
    justify-content: space-between;
    width: 100%;
    border: 0;
    background: var(--button-background);
    color: var(--button-foreground);
    padding: 0.35rem 0.5rem;
    text-align: left;
}

.prompt-lab-section-header:hover {
    background: var(--button-background-hover);
    color: var(--button-foreground-hover);
}

.prompt-lab-section-title {
    font-weight: 700;
}

.prompt-lab-section-state {
    min-width: 1ch;
    text-align: center;
    font-weight: 700;
}

.prompt-lab-section-actions {
    padding: 0.4rem 0.45rem 0;
}

.prompt-lab-section-body {
    padding: 0.45rem;
}

.prompt-lab-hidden-file {
    display: none;
}

.prompt-lab-autosave {
    display: block;
    margin-bottom: 0.45rem;
}

.prompt-lab-search,
.prompt-lab-title,
.prompt-lab-tags {
    width: 100%;
    margin-bottom: 0.45rem;
}

.prompt-lab-editor-header .prompt-lab-title {
    flex: 1 1 16rem;
    margin-bottom: 0;
}

.prompt-lab-textarea {
    width: 100%;
    min-height: 11rem;
    margin-bottom: 0.5rem;
}

.prompt-lab-negative {
    min-height: 7rem;
}

.prompt-lab-notes {
    width: 100%;
    min-height: 4rem;
    margin-bottom: 0.55rem;
}

.prompt-lab-wildcard-values {
    width: 100%;
    min-height: 9rem;
    margin-bottom: 0.5rem;
    resize: vertical;
}

.prompt-lab-fragment-text {
    width: 100%;
    min-height: 5rem;
    margin-bottom: 0.5rem;
    resize: vertical;
}

.prompt-lab-list {
    display: flex;
    flex-direction: column;
    gap: 0.25rem;
}

.prompt-lab-list-item {
    width: 100%;
    text-align: left;
    border: 1px solid var(--light-border);
    background: var(--input-background);
    color: var(--text);
    padding: 0.4rem 0.45rem;
}

.prompt-lab-list-item-selected {
    border-color: var(--emphasis);
}

.prompt-lab-history-preview {
    color: var(--text-soft);
    display: block;
    font-size: 0.85rem;
    overflow: hidden;
    text-overflow: ellipsis;
    white-space: nowrap;
}

.prompt-lab-favorite-marker {
    color: var(--emphasis);
    font-size: 0.8rem;
    text-transform: uppercase;
}

.prompt-lab-output {
    border: 1px solid var(--light-border);
    min-height: 3rem;
    max-height: 18rem;
    overflow: auto;
    padding: 0.5rem;
    white-space: pre-wrap;
    background: var(--background);
}

.prompt-lab-expanded-prompt {
    border-top: 1px solid var(--light-border);
    padding-top: 0.4rem;
    margin-top: 0.4rem;
}

.prompt-lab-count {
    color: var(--emphasis);
    float: right;
    font-size: 0.85rem;
}

.prompt-lab-wildcard-controls {
    display: grid;
    gap: 0.4rem;
    grid-template-columns: minmax(0, 1fr) 5rem 5rem;
    margin-bottom: 0.55rem;
}

.prompt-lab-export-actions {
    padding: 0;
    margin-bottom: 0.55rem;
}

.prompt-lab-small-input {
    min-width: 0;
}

.prompt-lab-compare-select {
    width: 100%;
    margin-bottom: 0.4rem;
}

.prompt-lab-diff-added {
    color: #62d98e;
}

.prompt-lab-diff-removed {
    color: #ff7a7a;
}

.prompt-lab-diff-same {
    color: var(--text-soft);
}
```

Keep the existing `@media (max-width: 900px)` rule, but ensure it still targets `.prompt-lab-wrapper`.

- [ ] **Step 2: Run CSS/static diff check**

Run:

```bash
git diff --check
```

Expected: no output and exit code 0.

- [ ] **Step 3: Commit CSS**

Run:

```bash
git add src/wwwroot/css/genpage.css
git commit -m "Tighten Prompt Lab density styling"
```

## Task 4: Final Static Verification

**Files:**
- Verify: `src/Pages/_Generate/PromptLabTab.cshtml`
- Verify: `src/wwwroot/js/genpage/promptlab.js`
- Verify: `src/wwwroot/css/genpage.css`

- [ ] **Step 1: Run JS syntax check**

Run:

```bash
node --check src/wwwroot/js/genpage/promptlab.js
```

Expected: no output and exit code 0.

- [ ] **Step 2: Run diff whitespace/conflict check**

Run:

```bash
git diff --check HEAD~3..HEAD
```

Expected: no output and exit code 0.

- [ ] **Step 3: Check working tree**

Run:

```bash
git status --short --branch
```

Expected: only existing untracked `.superpowers/` local companion files, unless the developer chooses to push or commit additional work.

## Manual Validation Checklist

- Prompt Lab opens with Saved Prompts visible and Fragments/Wildcards/History collapsed.
- Prompt name, positive prompt, negative prompt, tags, and notes are immediately usable.
- Save, Variant, Reset, Send, Preview, Generate, and Export actions remain visible or reachable.
- Fragment and wildcard editors expand and their insert buttons still work.
- Warnings opens automatically after preview when warnings exist.
- Diff expands and updates when the compare select changes.
