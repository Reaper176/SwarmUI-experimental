# Lazy Generation-Page Tab Descriptor Design

**Status:** Designed; awaiting implementation

**Date:** 2026-07-28

**Rank:** 23 — Establish one lazy-tab descriptor/parity owner

**Approved source/audit base:** `1b2d769d14f9a62c2d0e754b768b487701ae41c6`

## Summary

The generation page currently repeats the identities of four core lazy tabs across three composition owners:

- `src/Pages/Text2Image.cshtml` defines the Razor tab list and emits `window.genpageLazyTabs`;
- `src/wwwroot/js/genpage/main.js` separately defines client state and explicit activation hooks; and
- `src/WebAPI/UtilAPI.cs` independently allowlists server-rendered partials and their permissions.

The current mappings agree, so Rank 23 is a maintainability and future-compatibility project rather than a current runtime-failure fix. A future add, removal, or rename can nevertheless leave a tab without state, point the client at an invalid API key, apply the wrong permission, or leave a loading shell uninitialized.

Rank 23 establishes one ordered C# descriptor list as the literal owner of core lazy-tab identity. The existing server allowlist and Razor compatibility manifest are derived from that list, while JavaScript derives generic state from the emitted manifest. Explicit activation hooks, script groups, hash sub-tab mappings, and extension tabs retain their current owners because they describe tab-specific behavior rather than shared identity.

The existing public globals, public server dictionary type, API route and payload, keys, IDs, permissions, partial paths, labels, Bootstrap behavior, hash behavior, script order, and `openGenPageTab` surface remain compatible.

## Current Boundary

The four current core lazy tabs are:

| Key | Tab ID | Button ID | Label | Partial | API permission |
| --- | --- | --- | --- | --- | --- |
| `imageediting` | `ImageEditing` | `imageeditingtabbutton` | Image Editing | `_Generate/ImageEditingTab` | `Permissions.FundamentalGenerateTabAccess` |
| `utilities` | `utilities_tab` | `utilitiestabbutton` | Utilities | `_Generate/UtilitiesTab` | `Permissions.UtilitiesTab` |
| `user` | `user_tab` | `usersettingstabbutton` | User | `_Generate/UserTab` | `Permissions.UserTab` |
| `server` | `server_tab` | `servertabbutton` | Server | `_Generate/ServerTab` | `Permissions.ViewServerTab` |

`Text2Image.cshtml` also owns:

- loading text for each tab;
- the generated `window.genpageLazyTabs` compatibility object;
- the separate `window.genpageLazyScriptGroups` behavior manifest;
- the four navigation headers;
- core lazy shells or maintainer-local eager partial rendering;
- the `WebServer.T2ITabHeader` and `WebServer.T2ITabBody` extension insertion points; and
- page script tags and their order.

`main.js` consumes `window.genpageLazyTabs` for:

- tab-ID-to-key lookup;
- top-tab activation;
- partial retrieval through `GetGenPageTabPartial`;
- Bootstrap display;
- hash navigation;
- the server resource loop; and
- `openGenPageTab`.

It separately owns explicit hooks for Image Editing, Utilities, User, and Server because each tab has different initialization work. It also consumes `window.genpageLazyScriptGroups`; Image Editing and Server have ordered lazy script groups, while Utilities and User currently have empty groups.

`UtilAPI.LazyGenPageTabPartials` is a public read-only dictionary whose values contain the fully qualified Razor partial path and `PermInfo`. `GetGenPageTabPartial` normalizes a supplied key, rejects an unknown key, checks the mapped permission, renders the mapped partial with a `GeneratePageModel`, and returns `{ "html": ... }`.

No external extension participates in this four-key allowlist. Extension top tabs remain composed through `WebServer.T2ITabHeader`, `WebServer.T2ITabBody`, and existing extension script surfaces.

## Confirmed Duplication and Risk

At the approved base:

1. Razor literally defines all four keys, DOM IDs, partials, and loading strings.
2. JavaScript literally defines four corresponding state entries and four explicit hooks.
3. C# literally defines all four keys, server partial paths, and permissions.
4. Razor navigation headers separately repeat DOM IDs, button IDs, labels, and three permission IDs.

The literal sets currently match. The defect is therefore duplicated ownership and the ability for a future edit to drift silently, not an observed incorrect tab at the approved base.

The explicit hook map, script-group manifest, and hash sub-tab map are not equivalent copies:

- hooks encode distinct one-time initialization behavior;
- script groups encode distinct ordered asset dependencies; and
- hash sub-tab mappings encode child navigation behavior for only the applicable tabs.

Rank 23 keeps those behavior maps explicit and statically proves that every referenced core key is valid.

## Goals

- Make one ordered C# descriptor list the literal owner of each core lazy tab's shared identity.
- Derive the existing public server partial allowlist from that descriptor list without changing its public type.
- Derive Razor headers, local tab data, and `window.genpageLazyTabs` from the descriptor list.
- Derive JavaScript lookup and state entries from `window.genpageLazyTabs`.
- Preserve clean-tree lazy loading and the maintainer's protected eager-render working mode.
- Preserve permissions at both navigation and API enforcement boundaries.
- Preserve all public globals, routes, payloads, keys, DOM IDs, labels, partials, loading text, and call surfaces.
- Preserve Bootstrap, hash, script-loading, retry, server-loop, extension-tab, and rapid-switch behavior.
- Make a future descriptor addition unable to omit generic client state or the server allowlist silently.

## Non-Goals

- Generalize or remove the explicit lazy-tab activation hooks.
- Generalize `hashSubTabMapping`.
- Add extension tabs to the core lazy-tab allowlist or descriptor list.
- Redesign tab navigation, Bootstrap integration, hashes, `openGenPageTab`, or the generation-page startup sequence.
- Change which tabs are lazy or eager in the committed production tree.
- Commit, revert, or otherwise absorb the maintainer's protected eager partial/script changes.
- Change script paths, grouping, order, loading semantics, or execution timing.
- Change permissions, permission registration, session behavior, API errors, or partial rendering.
- Add a new API route, client payload, endpoint parameter, retry loop, fallback, or user-facing message.
- Convert explicit tab-specific behavior into descriptor callbacks or reflection.
- Claim a current runtime bug, performance benefit, or measured result.

## Considered Approaches

### Canonical C# descriptor with compatible projections

Define one ordered descriptor list in `UtilAPI`, derive the existing public dictionary from it, expose the list to Razor, and derive generic JavaScript state from the existing window manifest.

This is the selected approach. C# is the natural owner because it already enforces the security-sensitive partial allowlist, Razor can consume its data directly, and JavaScript already consumes a Razor-emitted compatibility manifest.

### Razor ownership plus parity validation

Keep Razor as the manifest owner and add a static check against the independent C# allowlist.

This can detect drift but retains two runtime authorities. The server cannot naturally consume a Razor-local list, so this does not achieve the one-owner goal.

### Validation without runtime-owner changes

Leave all three literal maps intact and add only fixed parity checks.

This is the smallest patch, but every future edit still requires synchronized literal changes. It detects some mistakes after the fact rather than making the common omission structurally impossible.

## Chosen Architecture

### Canonical descriptor

`UtilAPI` gains one ordered descriptor type and one ordered read-only descriptor list. Each descriptor contains:

- core lazy-tab key;
- tab DOM ID;
- navigation button DOM ID;
- display label;
- Razor partial name in the existing `_Generate/...` form;
- loading text;
- API `PermInfo`; and
- whether the navigation header emits that permission requirement.

The header-permission flag preserves the current distinction: Image Editing has no explicit `data-requiredpermission` attribute on its navigation item, while Utilities, User, and Server expose their mapped permission IDs there. API enforcement still uses the descriptor's permission for all four entries, including fundamental generation access for Image Editing.

The list order remains Image Editing, Utilities, User, Server. This order controls the compatibility manifest and the core Razor tab composition.

The descriptor type and every public field or property receive the repository-required XML documentation. The implementation uses explicit C# types and full braced blocks.

### Compatible public server allowlist

The existing field remains:

`public static readonly IReadOnlyDictionary<string, (string PartialView, PermInfo Permission)> LazyGenPageTabPartials`

Its name, visibility, declared type, tuple member names, key comparer behavior, values, and consumer remain unchanged. Its entries are constructed from the canonical descriptor list:

- the key remains the descriptor key;
- the full server path is derived as `/Pages/{Partial}.cshtml`; and
- the permission is the descriptor permission.

`GetGenPageTabPartial` continues to consume this dictionary. Its normalization, unknown-key response, permission response, model construction, partial rendering, and returned JSON are unchanged.

This preserves source and managed-extension compatibility for consumers compiled against the existing public field type. Rank 23 adds descriptor members but does not remove or change an existing public member.

### Razor projection and composition

`Text2Image.cshtml` replaces its four-row literal list with data derived from `UtilAPI`'s ordered descriptors.

The view retains a tuple projection with the existing `Key`, `TabId`, `Partial`, and `LoadingText` shape. This lets the existing body composition continue to consume `lazyTabs` without changing the maintainer's protected eager-render lines. The projection contains no repeated identity literals.

The four core navigation headers derive their tab IDs, button IDs, labels, and applicable permission IDs from the descriptors. Image Editing remains before `WebServer.T2ITabHeader`; Utilities, User, and Server remain after it. The corresponding body placement remains Image Editing, then `WebServer.T2ITabBody`, then the other three core tabs.

Razor continues to emit `window.genpageLazyTabs` with the same four keys and the same per-entry properties:

```text
tabId
partial
loadingText
```

The global name, JavaScript object shape, property spellings, values, and order remain unchanged.

`window.genpageLazyScriptGroups` remains an explicit Razor behavior manifest. Its four keys and exact asset arrays remain unchanged. It is not folded into the canonical identity descriptor.

### Generic JavaScript state

`main.js` continues to build `lazyTabInfoById` from `window.genpageLazyTabs`. In the same manifest iteration, or an immediately adjacent generic iteration, it constructs one `lazyTabState` entry per manifest key with the established fields:

```text
loaded
loading
initDone
activation
```

There is no four-key state literal after Rank 23.

Initial `loaded` state is derived from the current DOM:

1. if the descriptor's target tab element is absent, it is not loaded;
2. if the target's first element child has class `tab-loading-shell`, it is not loaded; and
3. any other existing target markup is loaded.

Using the first element child distinguishes the committed loading placeholder from real partial markup without treating formatting whitespace as content. It also avoids a broad descendant query that could confuse a coincidental nested loading-shell class with the top-level placeholder.

This rule supports both authorized modes:

- clean committed Razor shells begin with `.tab-loading-shell`, so their state remains unloaded and the first activation requests the partial; and
- the maintainer's protected eager-rendered partials do not begin with that shell, so their state begins loaded and no redundant partial request occurs.

The generic initializer follows repository JavaScript conventions: `let`, full braced blocks, documented functions where introduced, and browser-compatible syntax.

### Explicit behavior remains explicit

`lazyTabHooks` remains a four-key behavior map. Each hook continues to perform its current tab-specific initialization.

`window.genpageLazyScriptGroups` remains explicit and ordered. `loadScript` continues to recognize an already present matching script `src`, so the maintainer's eager script tags are not appended a second time.

`hashSubTabMapping` remains explicit because its child IDs do not represent the same identity contract as the top-level descriptor. Image Editing has no corresponding sub-tab mapping. Utilities, User, and Server retain their current child lists and descriptor-derived top-level IDs where already used.

Server resource-loop and log-loop ownership, document-visibility gating, session-ready behavior, and current activation hooks are unchanged.

## Loading and Activation Flow

At page evaluation:

1. Razor emits the descriptor-derived compatibility manifest.
2. JavaScript iterates the manifest.
3. Each tab ID is added to `lazyTabInfoById`.
4. Each key receives a state entry whose `loaded` value reflects the shell/eager DOM rule.
5. Explicit script groups and hooks remain available under their existing keys.

On activation:

1. existing request sequencing identifies the lazy key;
2. the tab's ordered script group is ensured;
3. `ensureLazyTabMarkup` returns immediately for eager-loaded state or requests the server partial for a lazy shell;
4. the explicit initialization hook runs under the existing one-time state;
5. the current stale-request, Bootstrap, hash, and requested-sub-tab behavior runs; and
6. server visibility/resource behavior refreshes under its existing conditions.

Repeated activation reuses loaded markup, loaded scripts, and completed initialization. Concurrent activation continues to share the existing pending script, markup, and activation promises.

## Error Handling and Retry

Rank 23 introduces no new error or fallback contract.

- An unknown API key still returns `invalid_tab`.
- A denied mapped permission still returns `bad_permissions`.
- A missing target still rejects through the existing lazy-markup error.
- A failed partial request clears the existing pending markup state and remains retryable.
- A failed script clears the existing loader entry and remains retryable.
- A failed activation continues through the existing activation error path.
- A key absent from the compatibility manifest receives no generic state and remains a safe no-op at existing lookup guards.

The canonical list reduces the chance that Razor emits a key which the server rejects. Static review still checks explicit hooks, script groups, and hash consumers because those behavior maps intentionally remain separate.

## Compatibility

The following remain unchanged:

- `window.genpageLazyTabs` and its per-tab object shape;
- `window.genpageLazyScriptGroups` and exact script ordering;
- all four keys, tab IDs, button IDs, labels, partials, and loading strings;
- `UtilAPI.LazyGenPageTabPartials` name and declared public type;
- `GetGenPageTabPartial` registration, parameter, responses, rendering, and permissions;
- navigation permission outcomes;
- Bootstrap tab selection and events;
- top-level and child hash behavior;
- `openGenPageTab` call shape and behavior;
- rapid-switch request sequencing and stale-request suppression;
- one-time hook behavior and explicit initialization bodies;
- server resource/log visibility behavior;
- extension header/body placement and extension script participation;
- page script paths and order; and
- clean-tree lazy loading.

The descriptor type/list are additive C# members. No existing field, property, method, route, payload, global, ID, or extension surface is removed or renamed.

## Protected Maintainer Work

The approved working tree contains unrelated maintainer changes:

- `src/Data/Settings.fds` — `83 insertions / 3 deletions`;
- `src/Pages/Text2Image.cshtml` — `9 insertions / 2 deletions`;
- `src/wwwroot/js/genpage/gentab/loras.js` — `2 insertions / 0 deletions`;
- `src/wwwroot/js/genpage/main.js` — `4 insertions / 5 deletions`; and
- untracked `Data.pre-restore-2026-07-19/`.

The maintainer explicitly approved Rank 23 replacing only the four `lazyTabState` entries whose local `loaded` values are `true`. Generic DOM-derived initialization absorbs those four lines while preserving their eager-mode behavior.

The following remain protected and unstaged:

- the `featureSetChangedCallbacks` declaration deletion;
- eager partial rendering in `Text2Image.cshtml`;
- eager Image Editing and Server script tags and their placement;
- settings changes;
- LoRA changes; and
- the untracked backup directory.

Razor uses a descriptor-derived tuple projection specifically so the protected eager partial-render expressions and script section need not be edited. Every commit must be inspected against both the index and the working tree so documentation and production commits contain only their declared scope.

## Production and Documentation Boundary

The intended production files are:

- `src/WebAPI/UtilAPI.cs`;
- `src/Pages/Text2Image.cshtml`; and
- `src/wwwroot/js/genpage/main.js`.

The intended documentation files are:

- this design;
- the Rank 23 implementation plan; and
- the Frontend F3, roadmap Rank 23, recommendation, and final-status passages in `docs/superpowers/audits/2026-07-21-maintainability-architecture-refresh.md`.

No CSS, partial view, API documentation, extension, data, generated, downloaded, launch, backend, model, or settings file is a production target.

## Static Verification

Static verification must:

1. pin the approved base and baseline blobs for all three production files;
2. inventory all literals and consumers for the four lazy keys;
3. inventory tab IDs, button IDs, labels, partials, loading text, permissions, script groups, hooks, hash mappings, server-loop references, and `openGenPageTab`;
4. prove the canonical list contains exactly the four approved descriptors in the approved order;
5. prove no shared identity literal remains independently owned in Razor, generic JavaScript state, or the server allowlist;
6. prove the existing public dictionary retains its exact declaration and values;
7. prove `GetGenPageTabPartial` control flow and responses are unchanged;
8. prove the emitted `window.genpageLazyTabs` shape and values are unchanged;
9. prove Razor header and body order and both extension insertion points are unchanged;
10. prove `window.genpageLazyScriptGroups`, asset paths, and exact order are unchanged;
11. prove explicit hooks and their bodies are unchanged;
12. prove hash sub-tab mappings and `openGenPageTab` behavior are unchanged;
13. prove the generic state initializer handles every emitted manifest key;
14. prove a direct top-level `.tab-loading-shell` starts unloaded and eager partial markup starts loaded;
15. prove missing targets do not begin loaded;
16. prove existing markup/script/activation promise sharing and retry clearing are unchanged;
17. prove server resource/log visibility behavior is unchanged;
18. prove no extension is added to the core allowlist and extension composition is unchanged;
19. inspect public-member, source-projection, and fixed-range whitespace diffs;
20. prove the staged production projection excludes every protected maintainer hunk; and
21. confirm the index is empty after each intended commit and protected working-tree numstats remain accounted for.

Agents perform static review only. Repository policy forbids agents from building, running tests, executing test-running linters or scripts, launching SwarmUI or its backends, automating browsers, calling live APIs, or performing runtime/platform/filesystem exercises.

## Maintainer Validation Matrix

The maintainer records the date, operating system, filesystem, browser and browser version if known, loading mode, and pass/fail/unrun outcome.

### Clean committed lazy-shell mode

1. Load the generation page with all four authorized tabs and confirm their labels, order, IDs, and normal initial page selection.
2. Inspect the emitted compatibility manifest and confirm the four existing keys and per-entry values remain available to existing browser consumers.
3. Open Image Editing by click; confirm its partial loads once, its four scripts execute in exact order, and its controls/editor initialize once.
4. Open Utilities by click; confirm its partial loads once and its utilities initialization runs once.
5. Open User by click; confirm its partial loads once and its settings refresh/initialization runs once.
6. Open Server by click; confirm its partial and three scripts load in exact order, its server/log initialization runs once, and resource/log visibility behavior remains normal.
7. Revisit all four tabs and confirm markup, scripts, and one-time hooks are not repeated.
8. Switch rapidly among core tabs and confirm the final requested tab wins without showing wrong or stale content.
9. Load each supported top-level lazy-tab hash directly and confirm the intended tab opens after startup.
10. Load representative Utilities, User, and Server child hashes and confirm existing sub-tab selection remains correct.
11. Cause one lazy partial request to fail, restore availability, retry the tab, and confirm the existing retry path succeeds without a page reload.
12. Cause one lazy asset request to fail, restore availability, retry the tab, and confirm ordered loading resumes without duplicate successful assets.
13. With Utilities permission denied, confirm the header visibility/access outcome and API denial remain unchanged and unauthorized markup is not returned.
14. Repeat the permission case for User.
15. Repeat the permission case for Server.
16. Request an unknown lazy-tab key and confirm the existing invalid-tab API result without rendering an arbitrary partial.
17. Exercise an installed extension top tab and confirm its header/body placement, script behavior, hashes if applicable, and calls through existing top-tab helpers remain unaffected.

### Protected eager-render mode

18. With the maintainer's protected eager partials present, open all four tabs and confirm no redundant `GetGenPageTabPartial` request is made.
19. Confirm already present Image Editing and Server scripts are not appended or executed a second time and all four explicit hooks still initialize once.
20. Exercise repeat activation, rapid switching, top-level hashes, representative child hashes, server visibility behavior, and extension tabs; confirm behavior matches the clean lazy-shell mode except for the intentionally eager markup/assets.

## Success Criteria

Rank 23 succeeds when:

- one ordered descriptor edit controls the shared identity used by C#, Razor, and generic JavaScript state;
- the three prior literal owners cannot silently drift on key, tab ID, partial, or permission;
- the explicit behavior maps remain bounded and parity-checked;
- clean lazy and protected eager modes both retain their intended loading behavior;
- all compatibility and protected-work boundaries pass static review; and
- the maintainer completes and records the approved runtime matrix.

## Rollback

The production stages remain independently reversible:

1. JavaScript generic state can return to the four-entry literal map.
2. Razor can return to its local literal descriptor list and literal headers.
3. The server dictionary can return to literal entries while retaining the same public declaration.

Rollback must continue to preserve the maintainer's unrelated working-tree changes through selective staging. Documentation status must distinguish a design rollback, source rollback, and maintainer-validation state.
