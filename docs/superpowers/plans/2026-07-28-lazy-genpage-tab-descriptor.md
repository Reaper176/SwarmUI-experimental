# Lazy Generation-Page Tab Descriptor Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Establish one ordered C# owner for core generation-page lazy-tab identity while preserving the existing server allowlist, Razor/browser compatibility surfaces, explicit behavior maps, and both clean lazy-shell and maintainer-protected eager-render modes.

**Architecture:** Add an ordered descriptor list to `UtilAPI`, derive its existing public partial/permission dictionary from that list, project the same descriptors into Razor headers and the unchanged browser manifest, and generate client state for every manifest entry. Keep hooks, script groups, hash sub-tabs, extension tabs, API control flow, and public contracts explicit and unchanged.

**Tech Stack:** C# 12, .NET 8, ASP.NET Core Razor Pages, browser JavaScript, Bootstrap tabs, FreneticUtilities, Git/static source inspection, maintainer-run Firefox validation.

---

## Repository Policy and Fixed Boundaries

- Approved maintainer: Reaper176.
- Approved source/audit base: `1b2d769d14f9a62c2d0e754b768b487701ae41c6`.
- Approved design commit: `431290254e2ab889795428457f9061d6e77bbfa3`.
- Approved design: `docs/superpowers/specs/2026-07-28-lazy-genpage-tab-descriptor-design.md`.
- Baseline production blobs:
  - `src/WebAPI/UtilAPI.cs` — `ad501350e43e382966c2f0eebfcb36af33523ce2`
  - `src/Pages/Text2Image.cshtml` — `8beb72bd9303d67c7ccec513a5714048caa53bf4`
  - `src/wwwroot/js/genpage/main.js` — `39131fb91cd2c022fc15e09d6e9376886ecf49e1`
- Baseline audit blob:
  - `docs/superpowers/audits/2026-07-21-maintainability-architecture-refresh.md` — `54b86687a7f9b821678ff40df8e869110a949ca3`
- Production scope:
  - `src/WebAPI/UtilAPI.cs`
  - `src/Pages/Text2Image.cshtml`
  - `src/wwwroot/js/genpage/main.js`
- Documentation scope:
  - the approved design;
  - this implementation plan; and
  - Rank 23/Frontend F3 status passages in `docs/superpowers/audits/2026-07-21-maintainability-architecture-refresh.md`.
- No production change is authorized in partial views, CSS, extensions, settings, launchers, generated API documentation, data, models, outputs, downloaded repositories, backends, or other JavaScript/C# files.
- Do not generalize explicit hooks, `window.genpageLazyScriptGroups`, `hashSubTabMapping`, extension tabs, or `openGenPageTab`.
- Do not change keys, DOM IDs, button IDs, labels, loading text, partial paths, permissions, API errors, script paths/order, Bootstrap behavior, hashes, retry behavior, or server resource/log loops.
- Agents must not build, test, execute scripts or test-running linters, launch SwarmUI or its backends, automate browsers, call live APIs, or perform runtime/platform/filesystem exercises.
- This repository is intentionally being handled on direct `master` to preserve and validate the maintainer's approved dirty-tree eager mode. Do not create or switch worktrees during this plan.

### Protected working tree

Before production implementation, preserve:

- `src/Data/Settings.fds` — `83 insertions / 3 deletions`;
- `src/Pages/Text2Image.cshtml` — `9 insertions / 2 deletions`;
- `src/wwwroot/js/genpage/gentab/loras.js` — `2 insertions / 0 deletions`;
- `src/wwwroot/js/genpage/main.js` — `4 insertions / 5 deletions`; and
- untracked `Data.pre-restore-2026-07-19/`.

The maintainer approved replacing only the four local `lazyTabState` `loaded: true` rows with generic DOM-derived state. After that source commit, the expected protected `main.js` working diff becomes only the unrelated `featureSetChangedCallbacks` declaration deletion, normally `0 insertions / 1 deletion`.

The Razor eager partial-render and eager script-tag changes remain protected and unstaged. Their expected `Text2Image.cshtml` working numstat remains `9 insertions / 2 deletions` after selectively committing the descriptor projection and header changes.

## File Responsibility Map

- `src/WebAPI/UtilAPI.cs`
  - Own the shared core lazy-tab descriptors.
  - Derive the existing public server partial/permission allowlist.
  - Leave `GetGenPageTabPartial` behavior unchanged.
- `src/Pages/Text2Image.cshtml`
  - Project descriptors into the existing local tuple shape.
  - Emit descriptor-driven navigation headers.
  - Continue emitting the unchanged window manifests and composing bodies/extensions in the existing order.
- `src/wwwroot/js/genpage/main.js`
  - Detect whether descriptor targets contain a lazy shell or eager markup.
  - Build tab-ID lookup and state generically from `window.genpageLazyTabs`.
  - Leave explicit hooks and activation behavior unchanged.
- `docs/superpowers/audits/2026-07-21-maintainability-architecture-refresh.md`
  - Record the exact implementation/review/validation boundary and advance the roadmap only after maintainer validation.

### Task 1: Reconfirm the approved boundary and literal-owner inventory

**Files:**
- Inspect: `AGENTS.md`
- Inspect: `docs/project-memory.md`
- Inspect: `docs/superpowers/specs/2026-07-28-lazy-genpage-tab-descriptor-design.md`
- Inspect: `docs/superpowers/audits/2026-07-21-maintainability-architecture-refresh.md`
- Inspect: `src/WebAPI/UtilAPI.cs`
- Inspect: `src/Pages/Text2Image.cshtml`
- Inspect: `src/wwwroot/js/genpage/main.js`
- Inspect: `src/Pages/_ViewImports.cshtml`
- Inspect: `src/Pages/_Generate/ImageEditingTab.cshtml`
- Inspect: `src/Pages/_Generate/UtilitiesTab.cshtml`
- Inspect: `src/Pages/_Generate/UserTab.cshtml`
- Inspect: `src/Pages/_Generate/ServerTab.cshtml`

- [ ] **Step 1: Verify the approved commits and baseline blobs**

Run:

```bash
git merge-base --is-ancestor 1b2d769d14f9a62c2d0e754b768b487701ae41c6 HEAD
git merge-base --is-ancestor 431290254e2ab889795428457f9061d6e77bbfa3 HEAD
git rev-parse HEAD:src/WebAPI/UtilAPI.cs
git rev-parse HEAD:src/Pages/Text2Image.cshtml
git rev-parse HEAD:src/wwwroot/js/genpage/main.js
git rev-parse HEAD:docs/superpowers/audits/2026-07-21-maintainability-architecture-refresh.md
```

Expected: both ancestry checks exit `0`; the four blobs exactly match the fixed-boundary values above.

- [ ] **Step 2: Re-read instructions and the complete approved design**

Read `AGENTS.md`, `docs/project-memory.md`, the complete approved design, and every relevant repository skill under `.agents/skills/` if one exists. Confirm that agents are limited to static inspection and that no build/test/runtime command is allowed.

- [ ] **Step 3: Record the protected working state**

Run:

```bash
git status --short
git diff --numstat -- src/Data/Settings.fds src/Pages/Text2Image.cshtml src/wwwroot/js/genpage/gentab/loras.js src/wwwroot/js/genpage/main.js
git diff --unified=4 -- src/Pages/Text2Image.cshtml src/wwwroot/js/genpage/main.js
git diff --cached --name-only
```

Expected: the protected paths and numstats exactly match the pre-production list; the untracked backup exists; the index is empty. Stop rather than overwrite or stage any unexpected user change.

- [ ] **Step 4: Inventory every core lazy-tab identity and behavior consumer**

Run:

```bash
rg -n 'imageediting|utilities|user|server|genpageLazyTabs|genpageLazyScriptGroups|lazyTabState|lazyTabHooks|LazyGenPageTabPartials|GetGenPageTabPartial|hashSubTabMapping|openGenPageTab|T2ITabHeader|T2ITabBody' src/Pages/Text2Image.cshtml src/wwwroot/js/genpage/main.js src/WebAPI/UtilAPI.cs src/Core/WebServer.cs
```

Expected: four literal identity rows in Razor, four literal state rows and four explicit hooks in JavaScript, four literal server allowlist rows, explicit script/hash behavior, and extension insertion surfaces.

- [ ] **Step 5: Confirm eager-root and lazy-shell detection inputs**

Inspect the four partial starts and committed Razor body:

```bash
head -n 18 src/Pages/_Generate/ImageEditingTab.cshtml src/Pages/_Generate/UtilitiesTab.cshtml src/Pages/_Generate/UserTab.cshtml src/Pages/_Generate/ServerTab.cshtml
git show HEAD:src/Pages/Text2Image.cshtml | nl -ba | sed -n '136,154p'
```

Expected: every eager partial emits a real first element after Razor directives/code; every clean committed core lazy pane begins with `.tab-loading-shell`.

- [ ] **Step 6: Report the fixed boundary before editing**

Report the approved base/design, three production blobs, four descriptor rows, behavior-map owners, API/public dictionary contract, and protected-state numstats. Do not commit in this task.

### Task 2: Add the canonical C# descriptor and derive the server allowlist

**Files:**
- Modify: `src/WebAPI/UtilAPI.cs:15-55`

- [ ] **Step 1: Replace the literal dictionary block with the descriptor architecture**

Use `apply_patch` to replace the current `LazyGenPageTabPartials` literal block with:

```csharp
    /// <summary>Shared identity for one core lazy-loaded generation-page tab.</summary>
    public record class LazyGenPageTabDescriptor(string Key, string TabId, string ButtonId, string DisplayLabel,
        string Partial, string LoadingText, PermInfo Permission, bool ShowPermissionOnHeader);

    /// <summary>Ordered shared descriptors for the core lazy-loaded generation-page tabs.</summary>
    public static readonly IReadOnlyList<LazyGenPageTabDescriptor> LazyGenPageTabs = new List<LazyGenPageTabDescriptor>()
    {
        new("imageediting", "ImageEditing", "imageeditingtabbutton", "Image Editing", "_Generate/ImageEditingTab",
            "Loading image editor...", Permissions.FundamentalGenerateTabAccess, false),
        new("utilities", "utilities_tab", "utilitiestabbutton", "Utilities", "_Generate/UtilitiesTab",
            "Loading utilities...", Permissions.UtilitiesTab, true),
        new("user", "user_tab", "usersettingstabbutton", "User", "_Generate/UserTab",
            "Loading user settings...", Permissions.UserTab, true),
        new("server", "server_tab", "servertabbutton", "Server", "_Generate/ServerTab",
            "Loading server tools...", Permissions.ViewServerTab, true)
    }.AsReadOnly();

    /// <summary>Builds the compatible server-rendered partial allowlist from the shared lazy-tab descriptors.</summary>
    private static IReadOnlyDictionary<string, (string PartialView, PermInfo Permission)> BuildLazyGenPageTabPartials()
    {
        Dictionary<string, (string PartialView, PermInfo Permission)> partials = new();
        foreach (LazyGenPageTabDescriptor tab in LazyGenPageTabs)
        {
            partials.Add(tab.Key, ($"/Pages/{tab.Partial}.cshtml", tab.Permission));
        }
        return partials;
    }

    /// <summary>Allowlisted lazy genpage tab partials keyed by client-safe tab identifiers.</summary>
    public static readonly IReadOnlyDictionary<string, (string PartialView, PermInfo Permission)> LazyGenPageTabPartials
        = BuildLazyGenPageTabPartials();
```

Do not alter `Register`, `GetGenPageTabPartial`, or any later method.

- [ ] **Step 2: Inspect the C# diff and exact descriptor values**

Run:

```bash
git diff -- src/WebAPI/UtilAPI.cs
rg -n 'LazyGenPageTabDescriptor|LazyGenPageTabs|BuildLazyGenPageTabPartials|LazyGenPageTabPartials|imageediting|utilities|usersettingstabbutton|servertabbutton' src/WebAPI/UtilAPI.cs
git diff --check -- src/WebAPI/UtilAPI.cs
```

Expected:

- exactly four descriptors in Image Editing, Utilities, User, Server order;
- exact approved IDs, labels, partials, loading strings, permissions, and header flags;
- one helper derives `/Pages/{Partial}.cshtml`;
- the existing public dictionary declaration retains its name and exact declared type; and
- whitespace check exits `0`.

- [ ] **Step 3: Prove the API control flow is textually unchanged**

Run:

```bash
git diff --unified=0 HEAD -- src/WebAPI/UtilAPI.cs
git show HEAD:src/WebAPI/UtilAPI.cs | sed -n '27,55p'
sed -n '55,84p' src/WebAPI/UtilAPI.cs
```

Expected: changes are confined to declarations/helper construction before `Register`; `GetGenPageTabPartial` retains key normalization, `TryGetValue`, permission check, exact errors, model construction, rendering, and JSON response.

- [ ] **Step 4: Stage and inspect only the C# file**

Run:

```bash
git add -- src/WebAPI/UtilAPI.cs
git diff --cached --name-status
git diff --cached --check
git diff --cached -- src/WebAPI/UtilAPI.cs
```

Expected: exactly `src/WebAPI/UtilAPI.cs` is staged and the cached diff contains only the descriptor/list/helper/dictionary projection.

- [ ] **Step 5: Commit the server ownership stage**

Run:

```bash
git commit -m "refactor: centralize lazy tab descriptors"
git rev-parse HEAD
git rev-parse HEAD:src/WebAPI/UtilAPI.cs
```

Record the returned commit and blob. The index must be empty after commit.

### Task 3: Project descriptors through Razor without staging eager-mode work

**Files:**
- Modify: `src/Pages/Text2Image.cshtml:2-13`
- Modify: `src/Pages/Text2Image.cshtml:99-128`
- Preserve unstaged: `src/Pages/Text2Image.cshtml:142-152`
- Preserve unstaged: eager script additions under `@section Scripts`

- [ ] **Step 1: Replace the Razor literal list with a descriptor-derived tuple projection**

Use `apply_patch` to replace the current four-row `lazyTabs` initializer with:

```csharp
    IReadOnlyList<UtilAPI.LazyGenPageTabDescriptor> lazyTabDescriptors = UtilAPI.LazyGenPageTabs;
    List<(string Key, string TabId, string Partial, string LoadingText)> lazyTabs = [];
    foreach (UtilAPI.LazyGenPageTabDescriptor lazyTab in lazyTabDescriptors)
    {
        lazyTabs.Add((lazyTab.Key, lazyTab.TabId, lazyTab.Partial, lazyTab.LoadingText));
    }
```

Do not edit the manifest loop or script-group manifest.

- [ ] **Step 2: Replace only the four literal core navigation headers**

Replace the current Image Editing header, `@WebServer.T2ITabHeader`, and the Utilities/User/Server literal headers with:

```cshtml
@for (int i = 0; i < lazyTabDescriptors.Count; i++)
{
    UtilAPI.LazyGenPageTabDescriptor lazyTab = lazyTabDescriptors[i];
    string requiredPermission = lazyTab.ShowPermissionOnHeader ? lazyTab.Permission.ID : null;
    <li class="nav-item" role="presentation" data-requiredpermission="@requiredPermission">
        <a class="nav-link translate" data-bs-toggle="tab" href="#@lazyTab.TabId" id="@lazyTab.ButtonId" aria-selected="false" tabindex="-1" role="tab">@lazyTab.DisplayLabel</a>
    </li>
    @if (i == 0)
    {
        @WebServer.T2ITabHeader
    }
}
```

ASP.NET Core Razor omits the conditional `data-requiredpermission` attribute when `requiredPermission` is null. The first descriptor therefore preserves Image Editing's lack of that attribute; the remaining three emit their existing permission IDs.

- [ ] **Step 3: Inspect the whole working diff before staging**

Run:

```bash
git diff -- src/Pages/Text2Image.cshtml
git diff --check -- src/Pages/Text2Image.cshtml
```

Expected: the working diff includes both the planned descriptor/header changes and the maintainer's pre-existing eager body/script changes. Do not use whole-file `git add`.

- [ ] **Step 4: Selectively stage only the planned Razor hunks**

Run:

```bash
git add -p -- src/Pages/Text2Image.cshtml
```

At the prompts:

- stage the top descriptor-projection hunk;
- stage the navigation-header hunk;
- do not stage eager partial rendering;
- do not stage any eager script-tag addition.

If Git combines planned and protected changes into one interactive hunk, answer `s` to split it before answering `y` or `n`. If Git cannot split a mixed hunk, answer `e` and remove every protected `+`/`-` line from the editable patch before accepting it.

Then run:

```bash
git diff --cached --name-status
git diff --cached --check
git diff --cached --unified=0 -- src/Pages/Text2Image.cshtml
git diff --cached --unified=0 -- src/Pages/Text2Image.cshtml | rg '^[+-].*(PartialAsync\\(lazyTabs|server/backends|server/servertab|server/logs|color_picker|image_editor_tools|image_editor\\.js|image_editor_ui)' || true
```

Expected: only `Text2Image.cshtml` is staged; the final search emits nothing.

- [ ] **Step 5: Verify the cached Razor projection preserves compatibility**

Inspect the cached file:

```bash
git show :src/Pages/Text2Image.cshtml | nl -ba | sed -n '1,160p'
```

Confirm:

- the local tuple list is derived with no tab identity literals;
- `window.genpageLazyTabs` retains the exact object/property shape;
- `window.genpageLazyScriptGroups` is byte-identical to the prior committed version;
- Image Editing remains before `T2ITabHeader`;
- the other three headers remain after it;
- clean committed bodies still contain `.tab-loading-shell`; and
- both extension insertion points remain in their prior positions.

- [ ] **Step 6: Commit the Razor projection stage**

Run:

```bash
git commit -m "refactor: derive lazy tab markup descriptors"
git rev-parse HEAD
git rev-parse HEAD:src/Pages/Text2Image.cshtml
git diff --numstat -- src/Pages/Text2Image.cshtml
```

Expected: the committed projection contains only planned Razor changes; the remaining protected Razor working diff is still `9 insertions / 2 deletions`.

### Task 4: Generate client lookup and state from the manifest

**Files:**
- Modify: `src/wwwroot/js/genpage/main.js:884-899`
- Preserve unstaged: `src/wwwroot/js/genpage/main.js:53-56`

- [ ] **Step 1: Replace the lookup/state declarations with generic initialization**

Use `apply_patch` to replace the current `lazyTabInfoById` loop and four-row `lazyTabState` literal with:

```javascript
/** Returns whether a lazy tab target already contains rendered markup rather than its loading shell. */
function isLazyTabMarkupLoaded(tabInfo) {
    if (!tabInfo) {
        return false;
    }
    let target = document.getElementById(tabInfo.tabId);
    if (!target || !target.firstElementChild) {
        return false;
    }
    return !target.firstElementChild.classList.contains('tab-loading-shell');
}

let lazyTabInfoById = {};
let lazyTabState = {};
for (let [tabKey, tabInfo] of Object.entries(window.genpageLazyTabs || {})) {
    lazyTabInfoById[tabInfo.tabId] = tabKey;
    lazyTabState[tabKey] = {
        loaded: isLazyTabMarkupLoaded(tabInfo),
        loading: null,
        initDone: false,
        activation: null
    };
}

let lazyScriptLoaders = {};
let lazyScriptGroupState = {};
let latestTopTabOpenRequestId = 0;
let suppressHashUpdateDepth = 0;
```

Remove the later four-entry `lazyTabState` literal completely. Do not change `lazyTabHooks`, script loading, activation, hashes, server-loop behavior, or the unrelated callback declaration area.

- [ ] **Step 2: Inspect JavaScript syntax and the focused diff statically**

Run:

```bash
git diff -- src/wwwroot/js/genpage/main.js
git diff --check -- src/wwwroot/js/genpage/main.js
rg -n 'isLazyTabMarkupLoaded|lazyTabInfoById|lazyTabState|imageediting: \\{ loaded|utilities: \\{ loaded|user: \\{ loaded|server: \\{ loaded' src/wwwroot/js/genpage/main.js
```

Expected:

- one documented detector;
- one generic manifest iteration constructing lookup and state;
- no four-row literal state map;
- explicit hooks still exist later; and
- whitespace check exits `0`.

- [ ] **Step 3: Selectively stage the approved state hunk**

Run:

```bash
git add -p -- src/wwwroot/js/genpage/main.js
```

At the prompts:

- do not stage the unrelated `featureSetChangedCallbacks` deletion;
- stage the detector/generic-state hunk.

Then run:

```bash
git diff --cached --name-status
git diff --cached --check
git diff --cached --unified=0 -- src/wwwroot/js/genpage/main.js
git diff --cached --unified=0 -- src/wwwroot/js/genpage/main.js | rg '^[+-].*featureSetChangedCallbacks' || true
```

Expected: only `main.js` is staged; the final search emits nothing.

- [ ] **Step 4: Trace clean lazy, eager, missing-target, and retry behavior**

Using source inspection only, trace:

1. clean pane with first child `.tab-loading-shell` → `loaded: false` → existing API fetch;
2. eager pane with a real partial root → `loaded: true` → no API fetch;
3. missing/empty target → `loaded: false` → existing missing-target rejection during fetch;
4. repeated activation → existing loaded/init state suppresses duplicate work;
5. concurrent activation → existing pending promises remain shared;
6. failed script/partial → existing pending state clears and later activation can retry; and
7. eager script tags → `loadScript` detects their exact existing `src` and does not append duplicates.

Expected: no changed control flow outside initial state construction.

- [ ] **Step 5: Commit the generic client-state stage**

Run:

```bash
git commit -m "refactor: derive lazy tab client state"
git rev-parse HEAD
git rev-parse HEAD:src/wwwroot/js/genpage/main.js
git diff --numstat -- src/wwwroot/js/genpage/main.js
```

Expected: the remaining protected `main.js` working diff is only the unrelated declaration deletion, normally `0 insertions / 1 deletion`.

### Task 5: Perform integrated static source and compatibility review

**Files:**
- Inspect: `src/WebAPI/UtilAPI.cs`
- Inspect: `src/Pages/Text2Image.cshtml`
- Inspect: `src/wwwroot/js/genpage/main.js`
- Inspect: `src/Core/WebServer.cs`
- Inspect: `src/Pages/_Generate/ImageEditingTab.cshtml`
- Inspect: `src/Pages/_Generate/UtilitiesTab.cshtml`
- Inspect: `src/Pages/_Generate/UserTab.cshtml`
- Inspect: `src/Pages/_Generate/ServerTab.cshtml`
- Modify only if a finding requires it: one of the three production files

- [ ] **Step 1: Record the exact production range and projection**

Run:

```bash
git diff --name-status 431290254e2ab889795428457f9061d6e77bbfa3..HEAD -- src
git diff --stat 431290254e2ab889795428457f9061d6e77bbfa3..HEAD -- src
git diff --check 431290254e2ab889795428457f9061d6e77bbfa3..HEAD -- src/WebAPI/UtilAPI.cs src/Pages/Text2Image.cshtml src/wwwroot/js/genpage/main.js
git rev-parse HEAD:src/WebAPI/UtilAPI.cs
git rev-parse HEAD:src/Pages/Text2Image.cshtml
git rev-parse HEAD:src/wwwroot/js/genpage/main.js
```

Expected: exactly the three approved production files, a focused diff, clean whitespace, and three recorded result blobs.

- [ ] **Step 2: Prove one shared identity owner and bounded explicit behavior**

Run focused searches:

```bash
rg -n 'new\\(\"(imageediting|utilities|user|server)\"' src/WebAPI/UtilAPI.cs
rg -n 'imageediting|utilities|user|server' src/Pages/Text2Image.cshtml src/wwwroot/js/genpage/main.js src/WebAPI/UtilAPI.cs
rg -n 'genpageLazyScriptGroups|lazyTabHooks|hashSubTabMapping|openGenPageTab|T2ITabHeader|T2ITabBody' src/Pages/Text2Image.cshtml src/wwwroot/js/genpage/main.js src/Core/WebServer.cs
```

Expected:

- shared descriptor literals exist only in the canonical C# list;
- Razor has no independent four-row identity literal;
- JavaScript has no independent four-row state literal;
- explicit script groups, hooks, and hash mappings remain present under their existing owners; and
- no extension enters the core descriptor/allowlist.

- [ ] **Step 3: Prove the public/API compatibility boundary**

Inspect:

```bash
rg -n 'public static readonly IReadOnlyDictionary<string, \\(string PartialView, PermInfo Permission\\)> LazyGenPageTabPartials|GetGenPageTabPartial|invalid_tab|bad_permissions|RenderPartialViewToString' src/WebAPI/UtilAPI.cs
git diff --unified=0 431290254e2ab889795428457f9061d6e77bbfa3..HEAD -- src/WebAPI/UtilAPI.cs
```

Expected: exact public field name/type and API behavior remain; only its initializer data source changes.

- [ ] **Step 4: Prove Razor compatibility and protected-hunk isolation**

Run:

```bash
git diff --unified=0 431290254e2ab889795428457f9061d6e77bbfa3..HEAD -- src/Pages/Text2Image.cshtml
git diff --unified=0 431290254e2ab889795428457f9061d6e77bbfa3..HEAD -- src/Pages/Text2Image.cshtml | rg '^[+-].*(genpageLazyScriptGroups|tab-loading-shell|PartialAsync\\(lazyTabs|@section Scripts|server/backends|color_picker)' || true
```

Expected: the second command emits nothing. The committed source changes only descriptor acquisition and navigation headers; manifest shape, script groups, clean lazy shells, body order, extension body insertion, and script section remain unchanged.

- [ ] **Step 5: Prove JavaScript compatibility and detector bounds**

Inspect the fixed-range JavaScript diff and surrounding functions:

```bash
git diff --unified=0 431290254e2ab889795428457f9061d6e77bbfa3..HEAD -- src/wwwroot/js/genpage/main.js
nl -ba src/wwwroot/js/genpage/main.js | sed -n '850,1210p'
rg -n 'loaded: false|loading = null|delete lazyScriptLoaders|lazyTabHooks|activateLazyTab|refreshServerResourceLoopState|hashSubTabMapping|openGenPageTab' src/wwwroot/js/genpage/main.js
```

Expected: only initialization is generalized; explicit hooks and all loading/activation/hash/server-loop/retry bodies remain unchanged.

- [ ] **Step 6: Perform specification-conformance review**

Review every Goals, Non-Goals, Compatibility, Protected Maintainer Work, and Static Verification item in the approved design against the exact production range.

Return exactly `SOURCE_SPEC_APPROVED` if no finding remains. Otherwise report each finding with file, line, violated requirement, and minimum correction. Correct findings with `apply_patch`, selectively stage only the correction, commit it separately, and rerun Steps 1-6.

- [ ] **Step 7: Perform code-quality and risk review**

Review:

- static initialization order;
- duplicate-key behavior;
- tuple path derivation;
- Razor null conditional-attribute behavior;
- descriptor ordering and extension insertion;
- empty/missing target behavior;
- first-element shell detection;
- browser compatibility and repository JavaScript style;
- public-member additions;
- rollback independence; and
- protected-hunk staging.

Return exactly `SOURCE_QUALITY_APPROVED` if no finding remains. Otherwise report and correct findings as in Step 6, then rerun both reviews.

- [ ] **Step 8: Reconfirm protected state and empty index**

Run:

```bash
git diff --cached --name-only
git status --short
git diff --numstat -- src/Data/Settings.fds src/Pages/Text2Image.cshtml src/wwwroot/js/genpage/gentab/loras.js src/wwwroot/js/genpage/main.js
```

Expected:

- empty index;
- Settings remains `83/3`;
- Text2Image remains `9/2`;
- LoRA remains `2/0`;
- main.js now contains only the callback declaration deletion, normally `0/1`; and
- the backup directory remains untracked.

### Task 6: Record implementation and static reviews in the audit

**Files:**
- Modify: `docs/superpowers/audits/2026-07-21-maintainability-architecture-refresh.md`
- Inspect: `docs/superpowers/specs/2026-07-28-lazy-genpage-tab-descriptor-design.md`

- [ ] **Step 1: Capture exact implementation evidence**

Run:

```bash
git log --reverse --format='%H %s' 431290254e2ab889795428457f9061d6e77bbfa3..HEAD
git diff --name-status 431290254e2ab889795428457f9061d6e77bbfa3..HEAD -- src
git diff --numstat 431290254e2ab889795428457f9061d6e77bbfa3..HEAD -- src/WebAPI/UtilAPI.cs src/Pages/Text2Image.cshtml src/wwwroot/js/genpage/main.js
git rev-parse HEAD:src/WebAPI/UtilAPI.cs
git rev-parse HEAD:src/Pages/Text2Image.cshtml
git rev-parse HEAD:src/wwwroot/js/genpage/main.js
```

Record the exact source commits, source projection, numstats, and result blobs produced by these commands. Do not estimate them.

- [ ] **Step 2: Update Frontend F3**

Use `apply_patch` to replace Frontend F3's pre-design status with an implementation record that states:

- **Implemented; awaiting maintainer validation.**
- The approved base is `1b2d769d14f9a62c2d0e754b768b487701ae41c6`.
- The approved design is `431290254e2ab889795428457f9061d6e77bbfa3`.
- The exact source commits/range, three files, numstats, and blobs are the values captured in Step 1.
- `UtilAPI.LazyGenPageTabs` is the ordered literal identity owner.
- `LazyGenPageTabPartials` retains its exact public declaration and is derived.
- Razor projects descriptors into the unchanged manifest and header/body/extension order.
- JavaScript derives lookup/state generically and recognizes clean shells versus protected eager markup.
- Hooks, script groups, hashes, API behavior, permissions, server loops, `openGenPageTab`, and extensions are unchanged.
- The maintainer-authorized four loaded-state rows were absorbed; the callback deletion and all other protected hunks remain unstaged.
- Static reviews returned `SOURCE_SPEC_APPROVED` and `SOURCE_QUALITY_APPROVED`.
- Agents performed no build/test/runtime/browser/API/platform/filesystem/performance exercise.
- The exact 20-case matrix remains awaiting maintainer execution.

- [ ] **Step 3: Update Rank 23 roadmap and recommendation passages**

Use `apply_patch` to mark Rank 23 **Implemented; awaiting maintainer validation** everywhere it is currently described as neither designed nor implemented.

Do not advance Rank 24 to recommended-next yet. Rank 23 remains the active recommended project until its exact maintainer matrix is recorded.

- [ ] **Step 4: Review and commit only the audit update**

Run:

```bash
git diff --check -- docs/superpowers/audits/2026-07-21-maintainability-architecture-refresh.md
git diff -- docs/superpowers/audits/2026-07-21-maintainability-architecture-refresh.md
git add -- docs/superpowers/audits/2026-07-21-maintainability-architecture-refresh.md
git diff --cached --name-status
git diff --cached --check
git commit -m "docs: record lazy tab descriptor implementation"
```

Expected: exactly the audit is committed; no validation result is claimed.

- [ ] **Step 5: Perform documentation specification and quality reviews**

Check every implementation fact against Git and source. Check every status occurrence for consistency, exact static/runtime separation, no inferred runtime result, no omitted protected-state disclosure, and no premature Rank 24 promotion.

Return `DOCS_SPEC_APPROVED` and `DOCS_QUALITY_APPROVED` if no finding remains. Correct any finding in a separate documentation commit and rerun both reviews.

### Task 7: Hand off and record maintainer validation

**Files:**
- Modify after maintainer result: `docs/superpowers/specs/2026-07-28-lazy-genpage-tab-descriptor-design.md`
- Modify after maintainer result: `docs/superpowers/audits/2026-07-21-maintainability-architecture-refresh.md`

- [ ] **Step 1: Present the exact approved 20-case matrix**

Present these unchanged cases:

1. In clean lazy-shell mode, load the generation page with all four authorized tabs and verify labels, order, IDs, and normal initial selection.
2. Verify the emitted compatibility manifest retains all four keys and exact per-entry values.
3. Open Image Editing by click; verify one partial request, exact four-script order, and one initialization.
4. Open Utilities by click; verify one partial request and one initialization/refresh.
5. Open User by click; verify one partial request and one settings initialization/refresh.
6. Open Server by click; verify one partial request, exact three-script order, one initialization, and normal resource/log visibility behavior.
7. Revisit all four tabs; verify no markup, script, or one-time hook repeats.
8. Switch rapidly among core tabs; verify the final request wins without wrong or stale content.
9. Load each supported top-level lazy-tab hash directly; verify the intended tab opens.
10. Load representative Utilities, User, and Server child hashes; verify the intended sub-tab opens.
11. Fail one lazy partial request, restore it, retry, and verify success without page reload.
12. Fail one lazy asset request, restore it, retry, and verify ordered loading without duplicate successful assets.
13. Deny Utilities permission; verify unchanged header/access behavior, API denial, and no unauthorized markup.
14. Repeat the denied-permission case for User.
15. Repeat the denied-permission case for Server.
16. Request an unknown lazy key; verify the existing invalid-tab result and no arbitrary partial render.
17. Exercise an installed extension top tab; verify header/body placement, scripts, applicable hashes, and existing top-tab helpers.
18. In protected eager-render mode, open all four tabs and verify no redundant `GetGenPageTabPartial` request.
19. Verify eager Image Editing/Server scripts are not appended or executed twice and all four explicit hooks initialize once.
20. In eager mode, repeat activation, rapid switching, top-level hashes, representative child hashes, server visibility behavior, and extension-tab checks; verify parity with lazy mode except for intentionally eager markup/assets.

Ask the maintainer to report:

- exact pass/fail/unrun case numbers;
- date;
- OS/distribution;
- filesystem;
- browser and version if known; and
- whether both loading modes were exercised.

Do not run or simulate any case.

- [ ] **Step 2: Wait for maintainer evidence**

Do not mark Rank 23 validated until Reaper176 supplies an explicit result. Preserve the raw wording exactly. Do not infer a browser version, mode, platform, filesystem, case procedure, or outcome that was not supplied.

- [ ] **Step 3: Normalize only explicit evidence**

Calculate passed, failed, and unrun counts from the supplied case statement. Disclose any harmless textual normalization. If either mode or any case is unrun, record it as unvalidated rather than failed or silently passed.

- [ ] **Step 4: Update the design validation record**

Use `apply_patch` to update the design status and append a validation record containing:

- the exact raw maintainer evidence;
- disclosed normalization;
- exact counts;
- supplied environment/browser/mode details;
- unprovided details explicitly marked not provided;
- static-only agent boundary; and
- remaining browser/platform/filesystem/extension/performance caveats.

- [ ] **Step 5: Update audit status and advance the roadmap only if complete**

If all 20 exact cases passed, update Frontend F3 and every Rank 23 status occurrence to **Implemented and maintainer-validated** and make Rank 24, **Extract a facade-preserving workflow graph editor**, the sole formal **Recommended Next Project**.

If any case failed or was unrun, retain **Implemented; awaiting maintainer validation** with the exact partial result and do not advance the recommendation.

- [ ] **Step 6: Review and commit the validation record**

Run:

```bash
git diff --check -- docs/superpowers/specs/2026-07-28-lazy-genpage-tab-descriptor-design.md docs/superpowers/audits/2026-07-21-maintainability-architecture-refresh.md
git diff -- docs/superpowers/specs/2026-07-28-lazy-genpage-tab-descriptor-design.md docs/superpowers/audits/2026-07-21-maintainability-architecture-refresh.md
git add -- docs/superpowers/specs/2026-07-28-lazy-genpage-tab-descriptor-design.md docs/superpowers/audits/2026-07-21-maintainability-architecture-refresh.md
git diff --cached --name-status
git diff --cached --check
git commit -m "docs: validate lazy tab descriptor ownership"
```

Expected: exactly the design and audit are committed; raw evidence, normalization, counts, environment, mode coverage, and caveats agree.

- [ ] **Step 7: Complete final integrated static review**

Review the full range from approved design through validation documentation. Confirm:

- all prior source/documentation review approvals remain current;
- the exact source projection contains only the three production files;
- source is unchanged after the final production correction, if any;
- the public dictionary/API and browser globals remain compatible;
- the validation claim does not exceed supplied evidence;
- all Rank 23 status occurrences agree;
- Rank 24 is promoted only after all 20 cases pass;
- the index is empty; and
- protected working changes remain accounted for.

Return exactly `FINAL_INTEGRATED_APPROVED` if no finding remains. Correct any finding in a separate documentation-only commit and rerun the integrated review.

## Completion Evidence

Rank 23 is complete only when all of the following are recorded:

- approved design commit;
- implementation plan commit;
- exact production commits/range and result blobs;
- `SOURCE_SPEC_APPROVED`;
- `SOURCE_QUALITY_APPROVED`;
- `DOCS_SPEC_APPROVED`;
- `DOCS_QUALITY_APPROVED`;
- maintainer's raw validation evidence and disclosed normalization;
- exact pass/fail/unrun counts and environment/mode boundary;
- `FINAL_INTEGRATED_APPROVED`;
- empty index; and
- preserved protected working state.

No agent-run build, test, script, launch, browser, live API, runtime, platform, filesystem, or performance result may appear in the completion record.
