# Prompt Lab Density Design

## Goal

Reduce the visual density of the Prompt Lab tab while preserving the existing prompt, fragment, wildcard, preview, import/export, and generation workflows. The default view should prioritize writing and reusing prompts, with advanced editors and secondary outputs reachable through compact collapsed sections.

## Current Problems

- The tab shows library management, prompt editing, wildcard editing, fragment editing, preview, warnings, diff, export, and generation controls at once.
- Toolbars have many equal-weight buttons, so primary actions do not stand out.
- The middle editor column mixes the prompt editor with wildcard and fragment editors, making the main prompt workflow feel crowded.
- Preview-side sections occupy space even when they are empty or low priority.

## Design Direction

Use progressive disclosure.

- Keep the three-column structure because it already maps well to library, editor, and preview workflows.
- Make prompt editing the dominant middle-column task.
- Collapse lower-frequency library sections by default: Fragments, Wildcard Sets, and Recent Prompts.
- Collapse advanced middle-column editors by default: Wildcard Set Editor and Fragment Editor.
- Keep Detected Wildcards and Preview visible in the right column, but collapse Warnings when empty and Diff by default.
- Group actions by workflow instead of showing long button rows.

## Layout

The left column becomes a compact library panel:

- Header row with library-level actions such as Import and Export.
- One shared search field.
- Saved Prompts open by default.
- Fragments, Wildcard Sets, and Recent Prompts as collapsible sections.
- Each section header carries its local action buttons, such as New, Save, Delete, and Favorite, instead of using separate toolbar rows.

The middle column becomes the primary prompt editor:

- Prompt name and prompt actions share one compact header row.
- Positive and negative prompt textareas remain immediately visible.
- Tags and notes remain visible but use restrained spacing.
- Wildcard Set Editor and Fragment Editor are collapsed below the prompt fields.
- Insert Token and Insert Fragment stay inside their respective expanded editor sections.

The right column becomes the preview and execution panel:

- Primary actions are grouped at the top: Send to Generate, Preview Wildcards, Generate Combinations.
- Export actions are grouped behind a compact export control.
- Wildcard mode, sample count, max combinations, and shuffle remain visible near Preview Wildcards.
- Detected Wildcards and Preview remain open.
- Warnings opens automatically when warnings exist; otherwise it stays collapsed.
- Diff stays collapsed by default.

## Behavior

- Collapsed/open section state can be local browser state for this pass, similar to other Generate tab UI toggles.
- Existing Prompt Lab functions and API calls stay in place.
- The redesign should not change Prompt Lab storage formats, metadata, wildcard expansion behavior, or generation behavior.
- Buttons should remain normal buttons with explicit labels, not icon-only controls, because Prompt Lab actions are text-heavy and risk ambiguity.

## Validation

Agents may run static checks only:

- `node --check src/wwwroot/js/genpage/promptlab.js`
- `git diff --check`

Manual validation in the live UI:

- Prompt Lab opens with less visual clutter.
- Saved Prompts is visible by default.
- Fragments, Wildcard Sets, Recent Prompts, Wildcard Set Editor, Fragment Editor, Warnings, and Diff can be expanded/collapsed.
- Existing save, variant, duplicate, delete, favorite, import/export, fragment insert, wildcard insert, preview, generate, and send-to-generate flows still work.
