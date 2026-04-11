# History Fast-First Load Design

## Summary

The History tab should remain eagerly available during UI startup, but it should stop waiting on a full deep history scan before becoming useful.

Instead, history loading should become a two-phase process:

1. A fast startup pass that returns only the newest history slice and renders it immediately.
2. A background follow-up pass that merges in older history entries over time.

This preserves the current "history is already there when the UI opens" behavior while prioritizing the user's most recent work.

## Problem

The current startup path triggers an eager history load from `src/wwwroot/js/genpage/main.js`, which immediately calls into the normal History browser listing flow. That flow currently waits for a single `ListImages` response before the browser can render anything.

In practice, recent upstream merges and local changes have made that initial history load feel too slow. The user intent is not to delay or disable eager history loading. The user wants:

- History to be loaded at startup.
- Recent images to begin appearing immediately.
- Older history to continue loading afterward.
- The user's most recent work to be prioritized so they can quickly resume.

## Goals

- Keep eager history loading on startup.
- Render a useful first batch as quickly as possible.
- Prioritize newest outputs first.
- Allow the rest of history to load in the background without blocking the initial visible batch.
- Minimize code churn and preserve the current browser, selection, and retry behavior where possible.

## Non-Goals

- Full pagination or cursor-based browsing redesign.
- Replacing the existing History browser UI.
- Reworking preview generation behavior.
- General-purpose server-side history indexing changes unrelated to fast-first loading.

## Relevant Existing Behavior

- Startup currently triggers `imageHistoryBrowser.navigate('')` from `src/wwwroot/js/genpage/main.js`.
- The history browser gets its content from `listOutputHistoryFolderAndFiles()` in `src/wwwroot/js/genpage/gentab/outputhistory.js`.
- That function currently performs a single `ListImages` request and only renders after the full response is received.
- `ListImages` is implemented in `src/WebAPI/T2IAPI.cs` and currently returns one full result set.
- The default outpath format is date-based in `src/Core/Settings.cs`:
  `raw/[year]-[month]-[day]/[hour][minute][request_time_inc]-[prompt]-[model]`

That default path pattern strongly supports a recent-first bootstrap strategy because the newest work tends to live in the newest folders and the newest file names.

## Approaches Considered

### Approach 1: Defer history loading until the History tab opens

Pros:

- Fastest page startup.
- Small implementation.

Cons:

- Violates the intended behavior of eager startup history loading.
- Delays recovery of recent work until the tab is opened.

Rejected because it does not match the desired UX.

### Approach 2: Keep eager load but split it into fast-first plus background completion

Pros:

- Matches the desired UX exactly.
- Keeps startup history available.
- Allows newest entries to appear quickly.
- Can be added with limited changes to the current API and browser.

Cons:

- Requires staged loading coordination between frontend and API.
- Needs merge logic so later loads do not wipe the first fast batch.

Recommended.

### Approach 3: Only optimize the existing single-pass server scan

Pros:

- May improve overall load time.
- Smaller than a full paging redesign.

Cons:

- Still blocks first render on one full request.
- Does not guarantee recent work becomes visible quickly.

Useful as a follow-up optimization, but insufficient for the requested behavior on its own.

## Proposed Design

### High-Level Flow

At startup, the History browser should still begin loading immediately, but it should no longer wait for a full deep scan to produce the first visible content.

Instead:

1. Startup issues a fast-first history request.
2. The server returns only a bounded recent slice intended for quick first render.
3. The frontend renders that slice immediately.
4. A second background request loads the fuller history set.
5. The frontend merges the second result into the browser content without losing the initial recent items, selection state, or current controls.

### Server Behavior

Add lightweight support to `ListImages` for a startup-oriented fast-first mode.

That mode should:

- Prefer the newest folders and newest file names first.
- Stop after a small configured cap suitable for immediate rendering.
- Avoid doing the broader full-history work required for the normal load.

This should be implemented as an extension of the existing `ListImages` path rather than a separate API route, to keep the change contained.

The fast-first response only needs to be "recent enough to resume work quickly", not globally complete.

Because the default output structure is date-based, a recent-first partial scan can provide high-value results cheaply by favoring descending folder order and descending file order in the newest directories.

### Frontend Behavior

The History tab loader in `outputhistory.js` should gain staged startup logic:

- When the initial eager history load begins, request fast-first mode.
- Render the returned files immediately into the browser.
- Mark history as partially loaded.
- Immediately queue a follow-up full load.
- Merge new entries into the browser as they arrive, deduplicating by full relative path.

The user should see recent thumbnails start loading at once, while older items continue to appear over time.

### Merge Rules

The merge behavior should be conservative:

- Deduplicate by the existing full history path key.
- Preserve already-rendered recent entries.
- Append or integrate older entries according to the current browser sort rules.
- Preserve selected state for entries that already exist.
- Preserve current folder and current control settings.

If the second pass includes an entry already shown in the first pass, the later copy should replace metadata in-memory if needed but should not create a duplicate browser entry.

### Sorting Expectations

The fast-first startup pass is explicitly optimized for recent work, not for producing a perfectly complete globally sorted dataset before first render.

The second pass remains responsible for filling in the fuller list. Once the second pass completes, the displayed browser state should match the normal current behavior for the selected sort mode as closely as practical.

For startup responsiveness, the first visible batch should favor recent paths over strict completeness.

### Error Handling

Existing history request status and retry UI should remain in place.

Behavior expectations:

- If the fast-first pass fails, the current error and retry flow should still work.
- If the fast-first pass succeeds but the background completion pass fails, the user should keep the already-rendered recent batch.
- For the first implementation, keep the current status UI behavior unless the staged load requires a targeted wording update for clarity.

### Scope Boundaries

This change should be kept minimal:

- Reuse `ListImages`.
- Reuse the existing history browser shell.
- Avoid introducing full infinite scroll or formal paging semantics.
- Avoid modifying unrelated preview-generation code.

## Expected Files

- Modify `src/WebAPI/T2IAPI.cs`
  Add fast-first request handling to `ListImages` and the internal list path.

- Modify `src/wwwroot/js/genpage/gentab/outputhistory.js`
  Add staged startup loading, result merge behavior, and partial/full load coordination.

- Modify `src/wwwroot/js/genpage/helpers/browsers.js` only if `outputhistory.js` cannot perform the merge cleanly with existing browser APIs.
  Any such change should be limited to a small helper for merging or replacing browser file lists without forcing a full reset.

- Modify `src/AGENTS.md` only if implementation uncovers a reusable repository rule worth preserving.
  No AGENTS update is currently planned from the design alone.

## Verification Strategy

This repository's `AGENTS.md` states that agents do not run builds or tests here and that developers manually verify the live software.

Verification for implementation should therefore rely on:

- Static review of startup flow and merge behavior.
- Manual UI verification by the developer.

Manual verification targets:

1. Open the UI and confirm recent History thumbnails begin appearing quickly.
2. Confirm additional older entries continue filling in afterward.
3. Confirm opening History immediately after page load shows recent work first.
4. Confirm existing sort controls still behave sensibly after the full pass completes.
5. Confirm selection, hide/unhide, delete, and current-image actions still work with merged results.

## Risks

- A fast-first partial result could differ from the final full result ordering for some sort modes until the second pass completes.
- Merge logic could accidentally duplicate entries if path deduplication is incomplete.
- A rerender-heavy implementation could reduce some of the perceived performance gain if it rebuilds the whole browser too often.

These risks are acceptable for the first pass as long as:

- The newest work appears quickly.
- Duplicates are prevented.
- The second pass converges to the normal full history state.

## Recommendation

Implement the two-phase eager history load:

- Fast-first startup request for recent work.
- Full background completion request immediately afterward.
- Merge results by path on the client.

This is the best fit for the requested UX while keeping the change focused and incremental.
