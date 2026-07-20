# Image History Frontend Decomposition Design

## Goal

Decompose `src/wwwroot/js/genpage/gentab/outputhistory.js` into a controller and three focused collaborators for comparison, filtering, and bulk actions without changing image-history behavior, script-loading semantics, inline handler names, extension-facing globals, server contracts, or user-visible results.

This is roadmap item 3 from the maintainability architecture audit. The project establishes explicit frontend ownership while preserving the classic-script compatibility surface used by the generation UI and potentially by extensions.

## Current State

`outputhistory.js` contains approximately 2,800 lines and combines several distinct responsibilities:

- the `GenPageBrowserClass` instance and browser callbacks;
- initial, foreground, and background list requests;
- load tokens, retry timers, watchdog state, and request status UI;
- optimistic saved-image insertion and scheduled refreshes;
- selected-path state and bulk-control synchronization;
- metadata parsing, query compilation, searchable-field construction, and filter matching;
- comparison modal construction, pan/zoom/reveal state, metadata comparison, and pixel-diff rendering;
- bulk exports, contact sheets, Prompt Lab transfer, metadata mutations, move/copy, hide, star, and delete operations;
- history card description and single-image actions.

`generatehandler.js` calls `notifyImageHistorySavedPath()` after generation results arrive. `currentimagehandler.js` reads `imageHistoryBrowser`, requests history refreshes while waiting for saved images, navigates the browser, and also calls the saved-path notification function. The browser header contains inline handlers for selection and bulk actions. These names and runtime call paths are compatibility requirements even though their implementations will move.

## Selected Architecture

Retain `outputhistory.js` as the composition root and home of `ImageHistoryController`. Add three class-definition files beside it:

- `imagehistorycomparison.js`, defining `ImageHistoryComparison`;
- `imagehistoryfilter.js`, defining `ImageHistoryFilter`;
- `imagehistorybulkactions.js`, defining `ImageHistoryBulkActions`.

The three collaborator scripts load before `outputhistory.js` as classic scripts. They define classes but do not construct application state. `outputhistory.js` creates the controller, constructs the collaborators with narrow callback objects, and connects the existing browser and global entry points.

The controller is the sole owner of orchestration state. Collaborators own only state intrinsic to their capability. Existing global functions remain thin delegates so inline HTML, other maintained scripts, and extensions do not need to change as part of this refactor.

This design intentionally avoids ES modules. Converting the generation page's classic-script graph and extension compatibility surface is a separate project.

## Controller Ownership

`ImageHistoryController` owns:

- the image-history `GenPageBrowserClass` instance;
- foreground and background request tokens;
- request keys, retry counts, retry timers, and watchdog timers;
- startup and initial-load scheduling state;
- queued refresh and saved-path refresh state;
- selected paths;
- the bulk-action busy flag;
- browser construction, browser event wiring, and folder-change selection clearing;
- request-status and header-control synchronization;
- optimistic insertion of newly saved images;
- list loading, background completion, sorting orchestration, and browser-content replacement;
- history-entry rendering and selection of an entry as the current image;
- delegation to comparison, filtering, and bulk-action collaborators.

The existing image window manager may remain a separate helper in `outputhistory.js`, but the controller owns its attachment lifecycle through browser build and tab visibility events.

Single-image card actions remain with the controller when they are coupled to entry rendering, current-image state, or browser cache mutation. Shared mutation primitives may be exposed to collaborators through callbacks, but the collaborators must not reach into controller fields.

## Filtering Collaborator

`ImageHistoryFilter` owns:

- the bounded parsed-metadata cache;
- the last compiled query text and compiled terms;
- metadata parsing for history entries;
- conversion of metadata values to searchable text;
- construction of structured search fields;
- quoted-term splitting and field-alias normalization;
- numeric and date comparison handling;
- complete entry matching;
- the history search-input hint.

Filtering remains synchronous. The browser's `filterMatcher` calls the existing global `imageHistoryFilterMatches()` delegate, which forwards to the collaborator and immediately returns the match result.

The controller and the other collaborators may request parsed metadata or searchable-value conversion through the filter collaborator's public methods. They do not access its cache or compiled-query fields directly.

Server-side filter text, request payloads, supported field names, aliases, fallback matching, parsing rules, date handling, and numeric comparison behavior remain unchanged.

## Comparison Collaborator

`ImageHistoryComparison` owns:

- the active pair of compared files;
- pan coordinates and pointer-drag state;
- compare modal creation and event binding;
- pair rendering, swapping, reveal position, zoom, and fit behavior;
- metadata comparison and same/different row rendering;
- diff-mode canvas generation;
- compare-side settings reuse, starring, and rating;
- modal opening, closing, cleanup, and return to the Generate image view.

It receives callbacks for resolving a loaded file, parsing metadata, converting metadata values to text, mutating metadata strings, requesting a history refresh, and selecting the current image. Generic UI and request utilities remain shared platform dependencies.

The collaborator receives paths rather than the controller's selected-path set. It cannot mutate selection. Missing files, invalid selection counts, feature-toggle defaults, modal behavior, error messages, notices, and mutation requests remain unchanged.

## Bulk Actions Collaborator

`ImageHistoryBulkActions` owns:

- user prompts for rating, tags, notes, and destination folders;
- metadata JSON export and path copying;
- contact-sheet image loading and canvas generation;
- Prompt Lab prompt conversion and transfer;
- batch star, rating, tag, note, move/copy, hide, and delete workflows;
- the existing success, skip, failure, and partial-success notices.

Before an action begins, the collaborator requests a snapshot of selected paths from the controller. It does not retain or mutate the controller's selected-path set. It resolves files and requests UI/cache changes through injected callbacks.

The controller owns the busy flag and bulk-control rendering. The collaborator sets busy state through a callback and uses `try`/`finally` for asynchronous workflows so an unexpected failure cannot leave the controls disabled. Existing explicit result handling, confirmation prompts, permissions, selection clearing, and refresh behavior remain unchanged.

The comparison button remains controller-mediated: the controller snapshots the first two selected paths and delegates display to `ImageHistoryComparison`. Comparison state is not part of bulk-action state.

## Compatibility Surface

The following compatibility behavior is retained:

- `notifyImageHistorySavedPath()` remains globally callable and forwards to the controller.
- `imageHistoryBrowser` remains readable through a global compatibility accessor backed by the controller's browser instance.
- Existing inline handler names for rescan, retry, selection, comparison, export, Prompt Lab transfer, star, rating, tags, notes, copy/move, hide, and delete remain globally callable.
- Existing filter and metadata helper names that are consumed outside their new owner remain delegates during this project.
- `registerMediaButton()` and the registered media-button collection retain their current extension-facing behavior.
- `storeImageToHistoryWithCurrentParams()` retains its existing generation integration.
- `currentimagehandler.js` and `generatehandler.js` do not need a coordinated API migration.

Compatibility delegates contain no alternate state or error handling. Each forwards directly to the controller or the appropriate collaborator. A global accessor must expose the exact browser object owned by the controller rather than a copied or separately assigned browser state.

## Runtime Data Flow

### History Loading

1. The history tab becomes visible and calls the existing scheduling entry point.
2. The entry point forwards to the controller.
3. The controller creates or readies the browser shell, captures request state, and calls the unchanged `ListImages` API payload.
4. Fast-first results are normalized and built into the browser.
5. The controller verifies request relevance before applying background results and manages retries or watchdog timeouts.
6. Browser build events ask the controller to synchronize status, selection, bulk controls, filter hints, and the visible-window manager.

### Saved-Image Insertion

1. Generation or current-image code calls `notifyImageHistorySavedPath()`.
2. The delegate passes the path and metadata to the controller.
3. The controller attempts the current optimistic insertion using the active folder, depth, sort, and filter rules.
4. If the image cannot be confirmed locally, the controller schedules the existing bounded refresh attempts.

### Filtering

1. The browser invokes `imageHistoryFilterMatches()` for an entry.
2. The delegate calls `ImageHistoryFilter.matches()`.
3. The collaborator reuses or compiles the query, evaluates structured fields, and returns a boolean synchronously.

### Comparison

1. A compatibility handler asks the controller to compare the current selection.
2. The controller snapshots at most the first two paths and calls the comparison collaborator.
3. The collaborator resolves both files through a callback, initializes its modal state, and renders the pair.
4. Mutations request controller refreshes without accessing browser or selection fields.

### Bulk Actions

1. A compatibility handler delegates to the bulk-action collaborator.
2. The collaborator snapshots selected paths through a controller callback.
3. The collaborator sets controller busy state, performs the existing action sequence, and reports unchanged results.
4. Controller callbacks update cards, cached files, selection, or browser contents as required by the existing behavior.
5. The collaborator clears busy state in `finally`.

## Script Loading

`src/Pages/Text2Image.cshtml` loads the new files immediately before `outputhistory.js`, with dependencies ordered as:

1. `imagehistoryfilter.js`;
2. `imagehistorycomparison.js`;
3. `imagehistorybulkactions.js`;
4. `outputhistory.js`.

The files remain non-module scripts and use the repository's established cache-vary query. Dependencies already loaded earlier, including browser helpers, metadata helpers, current-image handling, and shared utilities, remain in their current order.

The maintained `Text2Image.cshtml` file already contains unrelated local changes. The implementation must alter only the adjacent script-loading lines required by this design and must preserve all other work in that file.

## Error Handling and Behavioral Preservation

All current API route names, payloads, permissions, prompts, confirmations, error messages, notices, retry limits, timeouts, feature toggles, local-storage keys, DOM IDs, CSS classes, sort behavior, selection semantics, and partial-success handling remain unchanged.

Extraction must not add exception translation, change callback timing, reorder requests, retain live references to the controller's selected set, or introduce duplicated state. Collaborator callbacks propagate current return values and errors directly.

No cleanup, naming correction, UI redesign, filter-language extension, batching optimization, request consolidation, or algorithm change is combined with this work.

## Migration Stages

### Stage 1: Comparison

Add `ImageHistoryComparison`, move comparison state and implementation into it, load its class definition before `outputhistory.js`, and replace the existing global comparison implementations with compatibility delegates.

During this stage, metadata parsing and controller services are supplied as callbacks from their existing implementations. Comparison behavior must remain coherent before filtering is extracted.

### Stage 2: Filtering

Add `ImageHistoryFilter`, move cache and compiled-query state plus all filtering and metadata-search helpers into it, and replace existing global implementations with delegates. Rewire the comparison collaborator's metadata callbacks to the filter instance.

### Stage 3: Bulk Actions

Add `ImageHistoryBulkActions`, move prompts, exports, contact-sheet creation, Prompt Lab transfer, and batch mutation workflows into it. Keep selection and busy-state ownership in `outputhistory.js` during this stage and connect them through callbacks.

### Stage 4: Controller Consolidation

Introduce or complete `ImageHistoryController`, move browser/request/selection/refresh state into its fields, and convert the remaining global entry points to thin delegates. Expose the controller-owned browser through the compatibility accessor and verify that no duplicate legacy state remains.

Each stage is a separate reviewable commit and must leave a coherent classic-script runtime. The final stage includes a whole-surface compatibility and ownership audit.

## Static Verification

Repository policy prohibits agents from running builds, automated tests, browser automation, or the live server. Static verification therefore consists of:

- checking JavaScript syntax with an allowed static parser or linter only;
- confirming the new script tags are ordered before `outputhistory.js` and retain the cache-vary suffix;
- enumerating pre- and post-refactor top-level globals and confirming required compatibility names remain callable or readable;
- tracing every inline history handler to exactly one delegate and implementation owner;
- tracing `generatehandler.js` and `currentimagehandler.js` through saved-path, browser-access, refresh, and navigation flows;
- confirming the controller is the sole owner of browser, request, retry, refresh, selection, and busy state;
- confirming comparison, filter-cache/query, and bulk workflow state each have one owner;
- checking collaborator callback construction and ensuring callbacks do not expose controller fields or a mutable selected set;
- comparing request names, payload keys, callback ordering, errors, notices, feature toggles, local-storage keys, and DOM identifiers;
- checking JavaScript style, documentation comments, braces, staged scope, and preservation of unrelated maintainer changes;
- reviewing each extraction stage and the complete range before completion.

## Manual Validation

The maintainer will validate the completed decomposition in the live application:

1. Open image history from a cold page, verify initial loading, pagination, fast-first/background completion, retry controls, and watchdog recovery.
2. Generate and save images while history is closed and open; verify optimistic insertion, saved-path refresh, metadata display, and current-image navigation.
3. Exercise text and structured filters, quoted terms, numeric/date comparisons, all sort modes, reverse order, animation visibility, hidden images, grid hiding, folders, and available browser views.
4. Select individual images, select all, clear selection, change folders, rebuild/refresh during selection, and confirm permission-based bulk controls.
5. Compare two images, swap them, pan, zoom, change reveal position, enable diff and metadata modes, reuse settings, star, rate, and close back to the Generate image view.
6. Export metadata, copy paths, create a contact sheet, send prompts to Prompt Lab, and exercise bulk star, rating, tags, notes, copy, move, hide, unhide, and delete—including cancellation, partial success, and failure paths.
7. Exercise single-image star, hide, delete, folder-open, media-button, and current-image actions after bulk operations and refreshes.
8. Confirm history refresh and navigation continue to work while generation is active and after saving edited images.

## Non-Goals

- No user-visible feature or behavior change.
- No API route, payload, permission, or server change.
- No CSS or page-layout redesign.
- No ES-module conversion or bundler introduction.
- No removal or rename of compatibility globals or inline handlers.
- No filter-language, comparison, or bulk-action enhancement.
- No redesign of `GenPageBrowserClass`, `SwarmImageCard`, current-image handling, generation handling, or media-button registration.
- No broad cleanup of `outputhistory.js` outside ownership consolidation.
- No changes to extensions, upstream code, generated files, or user data.
- No build, test, browser automation, or live-server execution by agents.

## Success Criteria

- `ImageHistoryController` is the sole owner of browser, request lifecycle, refresh scheduling, selection, and busy state.
- `ImageHistoryComparison`, `ImageHistoryFilter`, and `ImageHistoryBulkActions` are the sole implementation owners of their respective capabilities.
- `outputhistory.js` is reduced to controller orchestration, browser/card integration, single-entry operations, composition, and compatibility delegates.
- Required globals, inline handlers, script ordering, request payloads, errors, notices, and user-visible behavior remain compatible.
- `generatehandler.js` and `currentimagehandler.js` retain their existing runtime integration.
- No collaborator directly mutates controller fields or retains the selected-path collection.
- Static verification and staged code review pass.
- The maintainer completes the manual validation matrix without regression.
