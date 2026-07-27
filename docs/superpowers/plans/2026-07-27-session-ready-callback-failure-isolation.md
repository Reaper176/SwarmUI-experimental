# Session-Ready Callback Failure Isolation Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Isolate every synchronous session-ready callback failure so later callbacks and the unchanged generation-page startup tail still run, while preserving the public callback array and synchronous extension contract.

**Architecture:** Add one documented synchronous dispatcher beside `sessionReadyCallbacks` in `src/wwwroot/js/genpage/main.js`. The dispatcher iterates the live array by index, catches and reports each entry independently through `console.error` and guarded `showError`, and replaces only the existing direct loop call site. Preserve callback registration, insertion order, dynamic append behavior, promise ownership, and all startup work before and after dispatch.

**Tech Stack:** Browser JavaScript, classic-script globals, standard indexed arrays, `try`/`catch`, existing `showError`, existing SwarmUI generation-page startup.

---

## Repository Constraints

- Work directly on `master`; do not create or use a worktree.
- Reaper176 is an approved maintainer under `AGENTS.md`.
- Agents must not build, launch, test, automate a browser, start a backend, call a live API, or run test-executing lint. The maintainer performs all runtime/browser validation.
- Agents may run source searches, committed-source inspection, exact-range diff inspection, syntax-only static inspection if available, `git diff --check`, and Git scope/index checks.
- Do not add automated tests: repository policy states that automated tests are not used and agents cannot run any form of testing.
- Never edit generated `docs/APIRoutes`, external extensions, downloaded upstream code, build output, user data, or launchers.
- Preserve the existing uncommitted maintainer files:
  - `src/Data/Settings.fds`;
  - `src/Pages/Text2Image.cshtml`;
  - `src/wwwroot/js/genpage/gentab/loras.js`;
  - `src/wwwroot/js/genpage/main.js`; and
  - `Data.pre-restore-2026-07-19/`.
- The Rank 17 design authority is commit `a70efd32` and
  `docs/superpowers/specs/2026-07-27-session-ready-callback-failure-isolation-design.md`.
- The approved committed source/audit base is `44e3317a177978926d4b7b280f514d4cf1fcce6b`; `a70efd32` changes only the design document, so its committed production source is identical.
- `src/wwwroot/js/genpage/main.js` is both the approved Rank 17 production target and a protected dirty maintainer file. Never stage the whole file indiscriminately.
- The current protected `main.js` hunks are separate from Rank 17:
  - maintainer removal of `featureSetChangedCallbacks` near the top of the file; and
  - maintainer `lazyTabState` initial `loaded` changes.
- Before editing, re-inspect the working diff. If a protected hunk now overlaps the callback declaration, dispatcher insertion point, direct loop, or startup tail, stop and ask Reaper176 rather than merging semantics by assumption.
- Use `apply_patch` for the source edit.
- Use interactive partial staging for `main.js`: stage only the dispatcher-addition hunk and direct-loop replacement hunk; reject every protected maintainer hunk.
- Preserve these declarations and registrations exactly:

```js
let sessionReadyCallbacks = [];

sessionReadyCallbacks.push(...)
```

- Preserve all six maintained registrations and direct external extension `push` compatibility.
- Preserve synchronous insertion-order, no-argument plain-function invocation.
- Preserve live-array traversal: entries appended during dispatch remain eligible in the same dispatch.
- Preserve callback-owned promise handling. Do not await, inspect, wrap, or attach handlers to callback return values.
- Preserve the complete successful `ListT2IParams` body before dispatch.
- Preserve this tail exactly and in order:

```js
startPendingKritaImportPoll();
automaticWelcomeMessage();
autoTitle();
swarmHasLoaded = true;
scheduleInitialImageHistoryLoad(250);
```

- Do not change another callback collection, request route, script list, extension registration, lazy-tab/hash behavior, Krita behavior, welcome/title behavior, loaded-state meaning, history scheduling, server API, persistence format, or public ABI.
- Do not add retry, rollback, callback metadata, priority, registration helpers, async dispatch, telemetry, benchmarks, or performance claims.

## File Map

- Modify `src/wwwroot/js/genpage/main.js`
  - add `runSessionReadyCallbacks()` immediately after the existing array declaration;
  - isolate each live-array entry under its own synchronous error boundary;
  - guard toast rendering under a secondary boundary;
  - replace the existing direct loop with `runSessionReadyCallbacks();`;
  - partially stage only those two Rank 17 hunks.
- Review but do not modify:
  - `src/wwwroot/js/genpage/promptlab.js`;
  - `src/wwwroot/js/genpage/helpers/settings_editor.js`;
  - `src/BuiltinExtensions/ComfyUIBackend/Assets/comfy_workflow_editor_helper.js`;
  - `src/BuiltinExtensions/GridGenerator/Assets/grid_gen.js`;
  - `src/BuiltinExtensions/ImageBatchTool/Assets/image_batcher.js`;
  - `src/Pages/Text2Image.cshtml`;
  - `src/Core/WebServer.cs`; and
  - `src/wwwroot/js/site.js`.
- Modify after approved source review:
  - `docs/superpowers/specs/2026-07-27-session-ready-callback-failure-isolation-design.md`; and
  - `docs/superpowers/audits/2026-07-21-maintainability-architecture-refresh.md`.

## Task 1: Add the Synchronous Per-Callback Dispatcher

**Files:**

- Modify: `src/wwwroot/js/genpage/main.js:13-15`
- Modify: `src/wwwroot/js/genpage/main.js:1416-1418`

- [ ] **Step 1: Reconfirm the approved committed source boundary**

Run:

```bash
git diff a70efd32 -- src/wwwroot/js/genpage/main.js

git show a70efd32:src/wwwroot/js/genpage/main.js | nl -ba | sed -n '1,45p'
git show a70efd32:src/wwwroot/js/genpage/main.js | nl -ba | sed -n '1165,1190p'
git show a70efd32:src/wwwroot/js/genpage/main.js | nl -ba | sed -n '1390,1435p'

git diff --unified=0 -- src/wwwroot/js/genpage/main.js
```

Expected:

- committed source has no Rank 17 change;
- line 13 declares `let sessionReadyCallbacks = [];`;
- the maintained `main.js` registration is unchanged;
- the success path still contains the direct callback loop followed by the five-statement tail;
- the working diff contains only the protected feature-set declaration removal and lazy-tab loaded-state changes; and
- neither protected hunk overlaps the Rank 17 insertion point or call site.

If the working diff has changed from that boundary or overlaps Rank 17, stop with `BLOCKED` and report the exact overlapping hunk.

- [ ] **Step 2: Reconfirm all maintained registrations and composition order**

Run:

```bash
git grep -n -F 'sessionReadyCallbacks.push' a70efd32 \
  -- 'src/**' \
  ':(exclude)src/Extensions/**' \
  ':(exclude)**/*.bak'

git grep -n -F 'sessionReadyCallbacks' a70efd32 \
  -- 'src/**' \
  ':(exclude)src/Extensions/**' \
  ':(exclude)**/*.bak'

git show a70efd32:src/Pages/Text2Image.cshtml | nl -ba | sed -n '150,190p'
git show a70efd32:src/Core/WebServer.cs | nl -ba | sed -n '368,415p'
git show a70efd32:src/wwwroot/js/site.js | nl -ba | sed -n '65,105p'
```

Expected:

- exactly six maintained non-backup registrations;
- one array declaration and one direct dispatch loop in `main.js`;
- base `main.js`, Prompt Lab, Settings Editor, extension scripts, and `finalscript.js` retain their committed order;
- extension scripts continue to use direct classic-script registration; and
- `showError` remains the existing toast owner.

- [ ] **Step 3: Add the exact approved dispatcher**

Immediately after:

```js
let sessionReadyCallbacks = [];
```

add:

```js
/** Runs all session-ready callbacks in insertion order while isolating synchronous failures. */
function runSessionReadyCallbacks() {
    for (let i = 0; i < sessionReadyCallbacks.length; i++) {
        let callback = sessionReadyCallbacks[i];
        try {
            callback();
        }
        catch (e) {
            let callbackName = typeof callback == 'function' && callback.name ? ` (${callback.name})` : '';
            let message = `Session-ready callback #${i + 1}${callbackName} failed: ${e}`;
            console.error(message, e);
            try {
                showError(message);
            }
            catch (showErrorException) {
                console.error(`Failed to display error for session-ready callback #${i + 1}: ${showErrorException}`, showErrorException);
            }
        }
    }
}
```

This exact shape provides:

- a documented function;
- standard indexed iteration;
- live length on each iteration;
- one local callback lookup;
- plain no-argument invocation;
- one per-entry synchronous catch;
- safe name formatting for anonymous or malformed entries;
- one-based position and readable error in the diagnostic;
- original exception detail in the console;
- existing toast visibility; and
- a secondary guard that cannot abort later callback dispatch.

Do not:

- move or replace the array;
- copy or snapshot the array;
- use `forEach`;
- use `const` or `var`;
- use `await`, `async`, `Promise.resolve`, `.then`, or `.catch` on callback results;
- add a registration helper;
- rethrow;
- retry; or
- add rollback.

- [ ] **Step 4: Replace only the direct loop**

Replace:

```js
for (let callback of sessionReadyCallbacks) {
    callback();
}
```

with:

```js
runSessionReadyCallbacks();
```

Keep the preceding `loadUserData(...)` call and all five following tail statements textually unchanged.

- [ ] **Step 5: Trace every synchronous outcome**

Trace the edited function and call site in execution order:

1. an ordinary named callback runs once and dispatch advances;
2. an ordinary anonymous callback runs once and dispatch advances;
3. a callback that appends another callback increases the live length and the appended entry is later visited;
4. a callback that throws before mutation is reported and dispatch advances;
5. a callback that mutates and then throws retains that mutation, is reported, and dispatch advances;
6. a malformed non-function entry throws on invocation, is safely identified by position, and dispatch advances;
7. a second callback can throw and receive a separate diagnostic;
8. a successful toast report returns to dispatch;
9. a throwing `showError` is caught, logged, and dispatch advances;
10. a callback return value is ignored;
11. a returned pending or rejected promise is neither awaited nor inspected;
12. after the final current entry, the dispatcher returns synchronously; and
13. the five unchanged tail statements then execute in order.

- [ ] **Step 6: Prove source compatibility and exact scope**

Run:

```bash
git diff -- src/wwwroot/js/genpage/main.js

rg -n -C 18 \
  "let sessionReadyCallbacks|function runSessionReadyCallbacks|Session-ready callback|runSessionReadyCallbacks\\(\\)|startPendingKritaImportPoll|swarmHasLoaded|scheduleInitialImageHistoryLoad" \
  src/wwwroot/js/genpage/main.js

rg -n \
  "await.*callback|Promise\\.resolve\\(callback|callback\\(\\)\\.then|callback\\(\\)\\.catch|sessionReadyCallbacks\\s*=|sessionReadyCallbacks\\.slice|\\[\\.\\.\\.sessionReadyCallbacks\\]" \
  src/wwwroot/js/genpage/main.js \
  || true

git diff a70efd32..HEAD -- \
  src/wwwroot/js/genpage/promptlab.js \
  src/wwwroot/js/genpage/helpers/settings_editor.js \
  src/BuiltinExtensions/ComfyUIBackend/Assets/comfy_workflow_editor_helper.js \
  src/BuiltinExtensions/GridGenerator/Assets/grid_gen.js \
  src/BuiltinExtensions/ImageBatchTool/Assets/image_batcher.js \
  src/Pages/Text2Image.cshtml \
  src/Core/WebServer.cs \
  src/wwwroot/js/site.js
```

Expected:

- the working `main.js` diff contains the two Rank 17 hunks plus the two protected maintainer hunks;
- one dispatcher exists;
- one dispatcher call replaces the old loop;
- negative async/snapshot/reassignment searches are silent except for the original array declaration;
- every reviewed registration/composition/reporting file has no committed diff since the approved design; and
- all protected working-file changes remain visible but untouched.

- [ ] **Step 7: Run permitted static checks**

Run:

```bash
git diff --check -- src/wwwroot/js/genpage/main.js

git diff --unified=0 -- src/wwwroot/js/genpage/main.js
git diff --name-only
git diff --cached --name-only
git status --short --branch
```

Expected:

- clean whitespace;
- four logical working-file hunks in `main.js`: two Rank 17 and two protected maintainer hunks;
- the other three protected tracked files and backup remain present;
- no file is staged; and
- no build, test, browser, server, backend, or live API was run.

- [ ] **Step 8: Partially stage only Rank 17**

Run:

```bash
git add -p src/wwwroot/js/genpage/main.js
```

At each prompt, inspect the displayed content:

- stage the dispatcher addition;
- reject the protected `featureSetChangedCallbacks` deletion;
- reject the protected `lazyTabState` changes; and
- stage the direct-loop replacement.

Do not rely only on prompt order if Git combines or splits hunks. Select by content. If a Rank 17 and protected maintainer change appear in one inseparable hunk, abort staging and return `BLOCKED` rather than staging protected work.

Then run:

```bash
git diff --cached --name-only
git diff --cached --check
git diff --cached --stat
git diff --cached -- src/wwwroot/js/genpage/main.js

git diff -- src/wwwroot/js/genpage/main.js
git status --short
```

Expected staged scope:

```text
src/wwwroot/js/genpage/main.js
```

Expected staged content:

- dispatcher addition only; and
- direct-loop replacement only.

Expected unstaged `main.js` content:

- protected `featureSetChangedCallbacks` deletion; and
- protected `lazyTabState` initial-state changes.

- [ ] **Step 9: Commit the focused production change**

Run:

```bash
git commit -m "fix: isolate session-ready callback failures"
```

Then verify:

```bash
git show --check --stat --oneline HEAD
git show --format= --name-only HEAD
git show HEAD -- src/wwwroot/js/genpage/main.js

git diff -- src/wwwroot/js/genpage/main.js
git diff --cached --name-only
git status --short --branch
```

Expected:

- production commit changes only `src/wwwroot/js/genpage/main.js`;
- commit contains only the two approved Rank 17 hunks;
- the two protected maintainer `main.js` hunks remain unstaged after the commit;
- the other protected state remains untouched; and
- the index is empty.

## Task 2: Perform Integrated Static and Source Review

**Files:**

- Review: `src/wwwroot/js/genpage/main.js`
- Review: all six maintained registration owners
- Review: `src/Pages/Text2Image.cshtml`
- Review: `src/Core/WebServer.cs`
- Review: `src/wwwroot/js/site.js`
- Review: `docs/superpowers/specs/2026-07-27-session-ready-callback-failure-isolation-design.md`

- [ ] **Step 1: Pin design and production boundaries**

Run:

```bash
rank17_design_commit="$(git rev-parse a70efd32)"
rank17_source_head="$(git log -1 --format=%H -- src/wwwroot/js/genpage/main.js)"

printf 'design_commit=%s\nsource_head=%s\n' "$rank17_design_commit" "$rank17_source_head"
git log --oneline "$rank17_design_commit..$rank17_source_head"
git log --oneline "$rank17_design_commit..$rank17_source_head" -- src/wwwroot/js/genpage/main.js
git diff --name-only "$rank17_design_commit..$rank17_source_head" -- src/wwwroot/js/genpage/main.js
git diff --stat "$rank17_design_commit..$rank17_source_head" -- src/wwwroot/js/genpage/main.js
git diff --check "$rank17_design_commit..$rank17_source_head" -- src/wwwroot/js/genpage/main.js
```

Expected:

- integrated history contains the plan and source commits;
- source-path-filtered history returns only the focused production commit;
- production projection contains one file;
- whitespace check is silent; and
- protected working-tree hunks do not appear in the commit projection.

- [ ] **Step 2: Run the complete owner and consumer inventory**

Run:

```bash
git grep -n -F 'sessionReadyCallbacks.push' "$rank17_source_head" \
  -- 'src/**' \
  ':(exclude)src/Extensions/**' \
  ':(exclude)**/*.bak'

git grep -n -F 'sessionReadyCallbacks' "$rank17_source_head" \
  -- 'src/**' \
  ':(exclude)src/Extensions/**' \
  ':(exclude)**/*.bak'

git grep -n -F 'runSessionReadyCallbacks' "$rank17_source_head" \
  -- src/wwwroot/js/genpage/main.js

git show "$rank17_source_head":src/Pages/Text2Image.cshtml | nl -ba | sed -n '150,190p'
git show "$rank17_source_head":src/Core/WebServer.cs | nl -ba | sed -n '368,415p'
git show "$rank17_source_head":src/wwwroot/js/site.js | nl -ba | sed -n '65,105p'
```

Confirm:

- exactly six maintained non-backup registrations remain;
- declaration identity and direct registration surface remain;
- the old dispatch loop is gone;
- one dispatcher definition and one dispatcher call exist;
- script and extension order are unchanged; and
- existing `showError` behavior is unchanged.

- [ ] **Step 3: Compare complete source regions with the design boundary**

Run:

```bash
git show a70efd32:src/wwwroot/js/genpage/main.js | nl -ba | sed -n '1,55p'
git show "$rank17_source_head":src/wwwroot/js/genpage/main.js | nl -ba | sed -n '1,85p'

git show a70efd32:src/wwwroot/js/genpage/main.js | nl -ba | sed -n '1390,1435p'
git show "$rank17_source_head":src/wwwroot/js/genpage/main.js | nl -ba | sed -n '1410,1465p'

git diff a70efd32.."$rank17_source_head" -- src/wwwroot/js/genpage/main.js
```

Confirm:

- array declaration unchanged;
- dispatcher exact and documented;
- live indexed loop;
- callback invocation plain and synchronous;
- per-entry catch;
- safe optional name formatting;
- original exception console detail;
- guarded toast;
- secondary diagnostic;
- no return-value use;
- direct loop replaced by one call;
- complete pre-dispatch body unchanged;
- complete five-statement tail unchanged; and
- no protected maintainer hunk entered the committed diff.

- [ ] **Step 4: Check negative boundaries and unchanged files**

Run:

```bash
git grep -n -E \
  'await.*callback|Promise\.resolve\(callback|callback\(\)\.then|callback\(\)\.catch|sessionReadyCallbacks\.slice|\[\.\.\.sessionReadyCallbacks\]' \
  "$rank17_source_head" \
  -- src/wwwroot/js/genpage/main.js \
  || true

git diff --name-only a70efd32.."$rank17_source_head" -- \
  src/wwwroot/js/genpage/promptlab.js \
  src/wwwroot/js/genpage/helpers/settings_editor.js \
  src/BuiltinExtensions/ComfyUIBackend/Assets/comfy_workflow_editor_helper.js \
  src/BuiltinExtensions/GridGenerator/Assets/grid_gen.js \
  src/BuiltinExtensions/ImageBatchTool/Assets/image_batcher.js \
  src/Pages/Text2Image.cshtml \
  src/Core/WebServer.cs \
  src/wwwroot/js/site.js

git diff a70efd32.."$rank17_source_head" -- src/wwwroot/js/genpage/main.js \
  | rg -n '^[+-].*(window\.|class |function .*async|sessionReadyCallbacks\s*=)' \
  || true
```

Expected:

- negative async/snapshot search is silent;
- unchanged-file list is silent;
- no new window property, class, async function, or array reassignment;
- only the documented ordinary function declaration is added.

- [ ] **Step 5: Obtain a fresh source specification review**

Under the selected execution workflow, require a fresh reviewer to return `SOURCE_SPEC_APPROVED` only after independently verifying:

- exact one-file/two-hunk production scope;
- protected dirty `main.js` hunks are absent from the source commit and remain unstaged;
- one documented dispatcher and one call site;
- same array declaration, identity, direct pushes, and six maintained registration sites;
- synchronous plain-function invocation and insertion order;
- live length and appended-during-dispatch behavior;
- one error boundary per entry;
- callback position, optional name, readable error, console detail, and toast;
- secondary toast guard;
- multiple failures continue independently;
- no retry, rollback, snapshot, metadata, promise inspection, or async conversion;
- all later callbacks and the exact five-statement tail remain reachable after a caught callback failure;
- complete pre-dispatch and post-dispatch text remains unchanged;
- request, script order, extension contribution, other callback arrays, server/API/persistence/ABI behavior unchanged;
- explicit external-extension, async-rejection, browser/platform/filesystem, and performance caveats; and
- no agent runtime claim.

If the reviewer finds a concrete source defect, have the implementation agent correct only that defect, partially stage only Rank 17, commit a focused source correction, and repeat source specification review.

- [ ] **Step 6: Obtain a fresh source code-quality review**

Only after specification approval, require a fresh reviewer to return `SOURCE_QUALITY_APPROVED` after verifying:

- dispatcher is the smallest clear ownership boundary;
- function documentation, `let`, braces, catch placement, loop style, and naming follow repository JavaScript conventions;
- error formatting is readable and cannot throw for an anonymous or non-function entry;
- toast reporting failure cannot abort dispatch;
- no swallowed secondary error;
- no unnecessary helper, class, allocation, metadata, priority, event, or public surface;
- no confusing claim of async failure isolation;
- no unrelated source or protected maintainer change;
- fixed-range whitespace is clean; and
- implementation is statically production-ready for maintainer validation.

Correct concrete quality findings in focused source commits, repeat specification review if behavior changes, and repeat quality review.

## Task 3: Record Static Implementation Closure

**Files:**

- Modify: `docs/superpowers/specs/2026-07-27-session-ready-callback-failure-isolation-design.md`
- Modify: `docs/superpowers/audits/2026-07-21-maintainability-architecture-refresh.md`

- [ ] **Step 1: Add the design implementation record**

Update the top status to:

```text
Implemented; awaiting maintainer validation
```

Add `## Implementation Record` immediately before `## Maintainer Validation Matrix` containing:

- approved source/audit base `44e3317a177978926d4b7b280f514d4cf1fcce6b`;
- design commit `a70efd32`;
- exact plan commit;
- final production source head and source commit(s);
- integrated history versus `main.js` path-filtered history;
- exact production file and diff statistics;
- dispatcher function and call-site boundaries;
- live indexed traversal and dynamic append semantics;
- synchronous exception, multiple failure, console, toast, and secondary-reporting behavior;
- unchanged promise ownership, retry, rollback, registration, script order, startup body, tail, requests, extensions, other arrays, APIs, persistence, and ABI;
- protected dirty-file partial-staging evidence;
- `SOURCE_SPEC_APPROVED` and `SOURCE_QUALITY_APPROVED`;
- exact static commands and observed results;
- no agent build/test/browser/server/backend/runtime/platform/filesystem/performance claim; and
- unvalidated browser timing, toast rendering, external extension, and async rejection behavior.

Do not alter or shorten the exact 14-case matrix.

- [ ] **Step 2: Update every current Frontend F2/rank 17 audit status**

In `docs/superpowers/audits/2026-07-21-maintainability-architecture-refresh.md`:

- mark Frontend F2 implemented, awaiting maintainer validation;
- mark the ranked-roadmap Rank 17 entry implemented, awaiting maintainer validation;
- mark the Recommended Next Rank 17 copy implemented, awaiting maintainer validation;
- retain the former unguarded-loop defect as explicitly historical evidence;
- record exact source scope, protected-hunk exclusion, and review outcomes;
- retain Rank 17 as the sole formal Recommended Next Project until live validation passes;
- keep Rank 18 neither designed nor implemented;
- leave ranks 1, 2, and 8 awaiting validation;
- preserve ranks 12 through 16 as maintainer-validated on their recorded environments;
- preserve all 12 top-level sections and 32 ranked roadmap entries; and
- preserve static/runtime, browser/platform/filesystem, and performance evidence boundaries.

- [ ] **Step 3: Self-review documentation**

Run:

```bash
rg -n \
  "TBD|TODO|placeholder|Frontend F2|Rank 17|rank 17|Rank 18|rank 18|Recommended Next|awaiting maintainer|SOURCE_SPEC_APPROVED|SOURCE_QUALITY_APPROVED|protected|session-ready" \
  docs/superpowers/specs/2026-07-27-session-ready-callback-failure-isolation-design.md \
  docs/superpowers/audits/2026-07-21-maintainability-architecture-refresh.md

git diff --check -- \
  docs/superpowers/specs/2026-07-27-session-ready-callback-failure-isolation-design.md \
  docs/superpowers/audits/2026-07-21-maintainability-architecture-refresh.md

printf 'top_level_sections='
rg -c '^## ' docs/superpowers/audits/2026-07-21-maintainability-architecture-refresh.md

printf 'roadmap_entries='
awk '/^## Ranked Refactoring Roadmap$/{inside=1; next} inside && /^## /{exit} inside && /^### [0-9]+\./{count++} END{print count+0}' \
  docs/superpowers/audits/2026-07-21-maintainability-architecture-refresh.md

printf 'matrix_cases='
sed -n '/^## Maintainer Validation Matrix$/,/^## Success Criteria$/p' \
  docs/superpowers/specs/2026-07-27-session-ready-callback-failure-isolation-design.md \
  | rg -c '^[0-9]+\.'
```

Expected:

- no placeholder;
- no current statement that Rank 17 is unimplemented;
- historical defect text is explicitly historical;
- Rank 17 is implemented awaiting validation and remains recommended;
- Rank 18 is neither designed nor implemented;
- audit has 12 top-level sections;
- roadmap has 32 entries;
- matrix has the exact unchanged 14 cases; and
- whitespace is clean.

- [ ] **Step 4: Commit documentation-only closure**

Run:

```bash
git add \
  docs/superpowers/specs/2026-07-27-session-ready-callback-failure-isolation-design.md \
  docs/superpowers/audits/2026-07-21-maintainability-architecture-refresh.md

git diff --cached --name-only
git diff --cached --check
git diff --cached --stat
git commit -m "docs: record session-ready callback isolation"
```

Expected staged scope: exactly the two documentation files.

- [ ] **Step 5: Obtain fresh documentation specification and quality reviews**

Require `DOCS_SPEC_APPROVED` and `DOCS_QUALITY_APPROVED` only after fresh reviewers verify:

- exact base/design/plan/source provenance and source projection;
- accurate dispatcher, live-array, sync exception, reporting, and tail semantics;
- accurate public array/extension/promise/partial-effect compatibility;
- explicit protected-dirty-file partial staging and exclusion;
- accurate current-versus-historical wording;
- accurate static-only versus pending runtime evidence;
- Rank 17 remains awaiting validation and recommended;
- Rank 18 remains undesigned and unimplemented;
- ranks 1, 2, and 8 remain pending;
- prior validation records remain intact;
- 12 audit sections, 32 roadmap entries, and exact 14-case matrix;
- documentation-only commit scope; and
- clean whitespace.

Correct concrete documentation findings in focused documentation commits and repeat both reviews.

## Task 4: Maintainer Validation and Final Record

**Files:**

- Modify after explicit maintainer result: `docs/superpowers/specs/2026-07-27-session-ready-callback-failure-isolation-design.md`
- Modify after explicit maintainer result: `docs/superpowers/audits/2026-07-21-maintainability-architecture-refresh.md`

- [ ] **Step 1: Hand Reaper176 the exact 14-case matrix**

Use the design's `Maintainer Validation Matrix` without shortening:

1. Normal startup with all maintained callbacks succeeds in the existing insertion order and produces no new error diagnostic.
2. A named callback throwing in the first position produces a console diagnostic and visible toast containing its one-based position, name, and readable error.
3. An anonymous callback throwing between maintained callbacks produces a diagnostic with its position and does not require a function name.
4. A callback throwing in the final original position is isolated and the core startup tail still runs.
5. Multiple callbacks throwing in one dispatch each produce their own console diagnostic and toast-reporting attempt.
6. Every non-throwing callback after a failed callback still runs once and in order.
7. A callback appended during dispatch runs later in that same dispatch in the expected dynamic-array order.
8. A callback that mutates state and then throws is not retried and its partial effects are not generically rolled back.
9. A forced `showError` failure produces a secondary console diagnostic without preventing later callbacks or the core startup tail.
10. A callback returning pending asynchronous work does not delay later callbacks or the startup tail; the callback retains ownership of its eventual completion/failure.
11. Prompt Lab, Settings Editor, the server lazy-tab check, Comfy workflow preparation, Grid Generator, and Image Batch Tool retain their normal initialization behavior.
12. A directly registered extension callback retains the same array registration, no-argument invocation, synchronous timing, and insertion-order behavior.
13. After an isolated callback failure, the Krita poll, welcome message, title update, `swarmHasLoaded` publication, and initial image-history scheduling all still occur in their existing order.
14. Normal page navigation, lazy tabs, image history, and image generation remain functional after ordinary startup and after an isolated synthetic callback failure.

Record maintainer, date, operating system, filesystem, browser, and exact result. Do not infer a pass.

- [ ] **Step 2: If validation fails, diagnose before editing**

Use `superpowers:systematic-debugging`. Work from maintainer-supplied evidence, reproduce only through maintainer-run steps, trace the responsible dispatcher/reporting/callback/tail boundary, update the design if behavior changes, implement the smallest correction, repeat source reviews, and return affected matrix cases to Reaper176.

- [ ] **Step 3: If validation passes, update current status**

Only after explicit maintainer confirmation:

- mark Frontend F2/rank 17 implemented and maintainer-validated on the confirmed operating system/filesystem/browser;
- record maintainer Reaper176, date, exact 14-case scope, and exact outcome;
- keep static agent evidence separate from maintainer browser/runtime evidence;
- retain direct-array, dynamic-mutation, partial-effect, async-rejection, external-extension, reporting, and tail caveats;
- retain unvalidated browser/platform/filesystem and no-performance caveats;
- advance the sole formal Recommended Next Project pointer to rank 18, `Preserve WebSocket lifecycle handlers across session renewal`;
- state that Rank 18 remains neither designed nor implemented; and
- leave ranks 1, 2, and 8 unchanged.

- [ ] **Step 4: Commit validation documentation only**

Run:

```bash
git add \
  docs/superpowers/specs/2026-07-27-session-ready-callback-failure-isolation-design.md \
  docs/superpowers/audits/2026-07-21-maintainability-architecture-refresh.md

git diff --cached --name-only
git diff --cached --check
git commit -m "docs: validate session-ready callback isolation"
```

Expected staged scope: exactly the two documentation files.

- [ ] **Step 5: Run final fresh reviews and static verification**

Require fresh `VALIDATION_SPEC_APPROVED`, `VALIDATION_QUALITY_APPROVED`, and final integrated review approval.

Then run:

```bash
rank17_design_commit="$(git rev-parse a70efd32)"
rank17_integrated_head="$(git rev-parse HEAD)"
rank17_source_head="$(git log -1 --format=%H -- src/wwwroot/js/genpage/main.js)"
rank17_validation_commit="$(git log -1 --format=%H -- \
  docs/superpowers/specs/2026-07-27-session-ready-callback-failure-isolation-design.md)"

git log --oneline "$rank17_design_commit..$rank17_integrated_head"
git diff --name-only "$rank17_design_commit..$rank17_integrated_head"
git diff --check "$rank17_design_commit..$rank17_integrated_head"

git log --oneline "$rank17_design_commit..$rank17_source_head" -- src/wwwroot/js/genpage/main.js
git diff --name-only "$rank17_design_commit..$rank17_source_head" -- src/wwwroot/js/genpage/main.js
git diff --stat "$rank17_design_commit..$rank17_source_head" -- src/wwwroot/js/genpage/main.js
git diff --check "$rank17_design_commit..$rank17_source_head" -- src/wwwroot/js/genpage/main.js

git show --check --oneline "$rank17_validation_commit"

git grep -n -F 'sessionReadyCallbacks.push' "$rank17_source_head" \
  -- 'src/**' \
  ':(exclude)src/Extensions/**' \
  ':(exclude)**/*.bak'

git show "$rank17_source_head":src/wwwroot/js/genpage/main.js \
  | nl -ba \
  | sed -n '1,90p'

git show "$rank17_source_head":src/wwwroot/js/genpage/main.js \
  | nl -ba \
  | sed -n '1410,1465p'

git diff --cached --name-only
git status --short --branch
```

Completion requires:

- all fresh specification, quality, validation, and integrated reviews pass;
- integrated and source-path-filtered histories remain distinct;
- production contains only the reviewed dispatcher and call-site hunks;
- protected maintainer `main.js` hunks remain unstaged and absent from Rank 17 commits;
- validation documentation contains only the two approved docs;
- exact 14-case maintainer result is recorded with browser/environment;
- Rank 18 is recommended but not designed or implemented;
- ranks 1, 2, and 8 remain pending;
- ranks 12 through 17 validation records remain intact;
- audit retains 12 sections and 32 roadmap entries;
- index is empty;
- all protected working-tree changes remain untouched; and
- no agent build/test/browser/server/backend/runtime/platform/filesystem/performance claim appears.
