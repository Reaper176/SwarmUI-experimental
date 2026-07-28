# Lazy Generation-Page Tab Descriptor Design

**Status:** Implemented and maintainer-validated on Garuda Linux (Arch-based), Btrfs, using Firefox (version not provided); loading-mode details not provided

**Date:** 2026-07-28

**Rank:** 23 — Establish one lazy-tab descriptor/parity owner

**Approved source/audit base:** `1b2d769d14f9a62c2d0e754b768b487701ae41c6`

## Summary

At the approved source/audit base, the generation page repeated the identities of four core lazy tabs across three composition owners:

- `src/Pages/Text2Image.cshtml` defined the Razor tab list and emitted `window.genpageLazyTabs`;
- `src/wwwroot/js/genpage/main.js` separately defined client state and explicit activation hooks; and
- `src/WebAPI/UtilAPI.cs` independently allowlisted server-rendered partials and their permissions.

The approved-base mappings agreed, so Rank 23 was framed as a maintainability and future-compatibility project rather than a current runtime-failure fix. Without a canonical owner, a future add, removal, or rename could nevertheless leave a tab without state, point the client at an invalid API key, apply the wrong permission, or leave a loading shell uninitialized.

Rank 23 establishes one ordered C# descriptor list as the literal owner of core lazy-tab identity. The existing server allowlist and Razor compatibility manifest are derived from that list, while JavaScript derives generic state from the emitted manifest. Explicit activation hooks, script groups, hash sub-tab mappings, and extension tabs retain their current owners because they describe tab-specific behavior rather than shared identity.

The existing public globals, public server dictionary type, API route and payload, keys, IDs, permissions, partial paths, labels, Bootstrap behavior, hash behavior, script order, and `openGenPageTab` surface remain compatible.

## Approved-Base Boundary

At the approved base, the four core lazy tabs were:

| Key | Tab ID | Button ID | Label | Partial | API permission |
| --- | --- | --- | --- | --- | --- |
| `imageediting` | `ImageEditing` | `imageeditingtabbutton` | Image Editing | `_Generate/ImageEditingTab` | `Permissions.FundamentalGenerateTabAccess` |
| `utilities` | `utilities_tab` | `utilitiestabbutton` | Utilities | `_Generate/UtilitiesTab` | `Permissions.UtilitiesTab` |
| `user` | `user_tab` | `usersettingstabbutton` | User | `_Generate/UserTab` | `Permissions.UserTab` |
| `server` | `server_tab` | `servertabbutton` | Server | `_Generate/ServerTab` | `Permissions.ViewServerTab` |

At the approved base, `Text2Image.cshtml` also owned:

- loading text for each tab;
- the generated `window.genpageLazyTabs` compatibility object;
- the separate `window.genpageLazyScriptGroups` behavior manifest;
- the four navigation headers;
- core lazy shells or maintainer-local eager partial rendering;
- the `WebServer.T2ITabHeader` and `WebServer.T2ITabBody` extension insertion points; and
- page script tags and their order.

At the approved base, `main.js` consumed `window.genpageLazyTabs` for:

- tab-ID-to-key lookup;
- top-tab activation;
- partial retrieval through `GetGenPageTabPartial`;
- Bootstrap display;
- hash navigation;
- the server resource loop; and
- `openGenPageTab`.

It separately owned explicit hooks for Image Editing, Utilities, User, and Server because each tab had different initialization work. It also consumed `window.genpageLazyScriptGroups`; Image Editing and Server had ordered lazy script groups, while Utilities and User had empty groups.

At the approved base, `UtilAPI.LazyGenPageTabPartials` was a public read-only dictionary whose values contained the fully qualified Razor partial path and `PermInfo`. `GetGenPageTabPartial` normalized a supplied key, rejected an unknown key, checked the mapped permission, rendered the mapped partial with a `GeneratePageModel`, and returned `{ "html": ... }`.

At the approved base, no external extension participated in this four-key allowlist. Extension top tabs remained composed through `WebServer.T2ITabHeader`, `WebServer.T2ITabBody`, and existing extension script surfaces.

## Approved-Base Duplication and Risk

At the approved base:

1. Razor literally defined all four keys, DOM IDs, partials, and loading strings.
2. JavaScript literally defined four corresponding state entries and four explicit hooks.
3. C# literally defined all four keys, server partial paths, and permissions.
4. Razor navigation headers separately repeated DOM IDs, button IDs, labels, and three permission IDs.

The approved-base literal sets matched. The defect was therefore duplicated ownership and the ability for a future edit to drift silently, not an observed incorrect tab at the approved base.

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

## Implementation and Review Record

The approved source/audit base is `1b2d769d14f9a62c2d0e754b768b487701ae41c6`, the approved design is commit `431290254e2ab889795428457f9061d6e77bbfa3` with design blob `fef25ff81ef17ebe20f6d99f95b2f1b98dedbbe8`, and the implementation plan is commit `b18906cbfb63e2f392aafa8db24049ee1b80558d` with plan blob `1c776cf878c6c0aa549fafdddc9ef51b3ded3842`.

The exact path-filtered production history is:

1. `85a2a0aa2c3f2538576f68d320636fc907e46e73` — `refactor: centralize lazy tab descriptors`
2. `e804078516301fbf1fb1c0d9e439554a077245f8` — `refactor: derive lazy tab markup descriptors`
3. `a3dab98e07350bab202f70f5dc04ad378587b102` — `fix: omit empty lazy tab permission attribute`
4. `0bbe7d38d889f072790e97dcd44186140a118456` — `refactor: derive lazy tab client state`
5. `66102f52d4cb8e22c56b018e7b95d66f59fda801` — `refactor: document lazy tab descriptor properties`
6. `b94bbbaed9a2209310e591fb4753e453e322e23a` — `fix: preserve positional lazy tab descriptor contract`

Source head `b94bbbaed9a2209310e591fb4753e453e322e23a` produces exactly the three-file `107 insertions / 33 deletions` projection:

- `src/WebAPI/UtilAPI.cs` — `63/7`, blob `73a03b86135854301682ad5f59d54c8568c57180`;
- `src/Pages/Text2Image.cshtml` — `25/19`, blob `557d2589f3dbae033fd3d4d8b4aac5844a03f65e`; and
- `src/wwwroot/js/genpage/main.js` — `19/7`, blob `a6b3bb3f8209d7f1bb6a596c9b298c78881614a8`.

Implementation documentation commit `753d647698a096178e984267248baeec7249aece` recorded the source state. Audit-status correction `55d982fa302f9a70b9dc3618041fac097c6e321c`, design-status correction `90e94f4e3966417e50f565e14ff6aee3c7ddd72d`, design-tense correction `5d8db186ae28f0c93cea04b74128ae6ae0b3ecba`, and approved-base boundary correction `31d9740dc5f9d2c37487023d0da54d88e6a85319` brought the pre-validation documentation to design blob `14d0865baf4d6308963aff8a35446a9d17855bb5` and audit blob `c93fe04b45e09a306db7bff8155b5da0fb03acec`.

Task 3 review found that a null-valued permission attribute did not preserve the no-attribute contract, producing correction `a3dab98e07350bab202f70f5dc04ad378587b102`. The first integrated Task 5 review found missing per-property XML documentation, producing `66102f52d4cb8e22c56b018e7b95d66f59fda801`; specification re-review then found that revision lost the positional public-record contract, producing `b94bbbaed9a2209310e591fb4753e453e322e23a`. Every corrected source and documentation state was re-reviewed. The current approval tokens are `SOURCE_SPEC_APPROVED`, `SOURCE_QUALITY_APPROVED`, `TASK5_SPEC_APPROVED`, `TASK5_QUALITY_APPROVED`, `TASK6_SPEC_APPROVED`, and `TASK6_QUALITY_APPROVED`, with no remaining findings.

At the validation-documentation base, the index was empty. The remaining protected working-tree deltas were `src/Data/Settings.fds` `83/3`, `src/Pages/Text2Image.cshtml` `9/2` containing eager partial rendering and eager Image Editing/Server scripts, `src/wwwroot/js/genpage/gentab/loras.js` `2/0`, `src/wwwroot/js/genpage/main.js` `0/1` containing only the protected `featureSetChangedCallbacks` declaration deletion, and untracked `Data.pre-restore-2026-07-19/`. None is part of the committed production projection or this documentation record.

The exact matrix below is byte-identical to the matrix in approved design commit `431290254e2ab889795428457f9061d6e77bbfa3` and contains exactly 20 numbered cases.

## Maintainer Validation Record

On 2026-07-28, maintainer Reaper176 supplied this exact raw evidence sequence:

1. `ll 14 passed on Garuda Linux (Arch-based), Btrfs, using`
2. After the controller asked whether all 20 or only cases 1–14 passed, requested browser/version details, and asked whether both loading modes were exercised: `all passed on Garuda Linux (Arch-based), Btrfs, using firefox`
3. After the controller stated that the result would be recorded as all 20 and requested final confirmation whether both modes were exercised: `unknown`

The second response directly answered the count clarification and superseded/corrected the first partial `ll 14...` count. The normalized result is all exact Rank 23 cases 1–20 passed: **20 passed, 0 failed, and 0 unrun**. Lowercase `firefox` is normalized to Firefox. No browser version was supplied or inferred.

The recorded environment is Garuda Linux (Arch-based), Btrfs, using Firefox (version not provided). The separate loading-mode detail is explicitly unknown. The record establishes the maintainer's asserted outcome for all 20 numbered matrix entries, but it does not establish that both clean lazy-shell and protected eager-render setups were actually exercised and does not attribute any unreported loading-mode setup, separate checkout/worktree arrangement, per-case procedure, failure-injection method, permission configuration, hash path, or installed-extension identity.

This validation is limited to the exact unchanged matrix and recorded environment/browser. Other browsers and browser versions, platforms, filesystems, loading-mode arrangements, extension identities outside the asserted numbered outcomes, and performance remain unvalidated or unmeasured. Agents performed static review only: no agent ran a build, test, test-running lint, script, launcher, browser, server/backend, live API, runtime, platform, filesystem, or performance exercise.

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
