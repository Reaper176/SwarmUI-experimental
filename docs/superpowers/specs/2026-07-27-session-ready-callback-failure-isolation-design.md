# Session-Ready Callback Failure Isolation Design

**Status:** Implemented; awaiting maintainer validation

**Date:** 2026-07-27

**Roadmap scope:** Frontend F2, rank 17

**Approved source and audit base:** `44e3317a177978926d4b7b280f514d4cf1fcce6b`

## Summary

The generation page exposes the classic-script global `sessionReadyCallbacks` array. At the approved source base, after a successful `ListT2IParams` response built the model and parameter UI, `genpageLoad()` invoked every entry synchronously in insertion order. That historical loop had no per-callback exception boundary.

A synchronous exception from one callback therefore historically escaped the response callback, prevented every later callback from running, and skipped the remaining core startup tail: the Krita import poll, welcome-message work, automatic title update, `swarmHasLoaded = true`, and initial image-history scheduling.

Rank 17 implements one synchronous dispatcher in `src/wwwroot/js/genpage/main.js`. It invokes each existing array entry under an individual `try`/`catch`, reports a failure through the browser console and the existing error toast, and continues to later callbacks. The existing array, direct `push` registration contract, insertion order, synchronous execution, dynamic-array behavior, and startup-tail order remain unchanged.

## Goals

1. Prevent one synchronous session-ready callback exception from suppressing unrelated later initializers.
2. Ensure the unchanged core startup tail runs after callback dispatch even when one or more callbacks throw.
3. Give maintainers a visible and diagnosable error containing the callback position, function name when available, and readable exception text.
4. Preserve the existing classic-script and extension registration surface.
5. Keep the production change local, synchronous, and reversible.

## Non-Goals

Rank 17 does not:

- replace, rename, freeze, copy, or hide `sessionReadyCallbacks`;
- require callbacks to register through a new helper;
- add callback metadata or priorities;
- change script or extension ordering;
- make `genpageLoad()`, the dispatcher, or callbacks asynchronous;
- await or inspect callback return values;
- take ownership of rejected promises or later asynchronous failures;
- retry a failed callback;
- roll back partial callback effects;
- isolate failures in the startup tail itself;
- change `ListT2IParams`, `getSession`, or other request failure behavior;
- change lazy-tab activation, hash handling, Krita polling, welcome messages, page titles, loaded-state publication, or image-history scheduling;
- change another frontend callback collection;
- change extension APIs, server APIs, permissions, persistence, or public C# ABI;
- add telemetry, benchmarking, or performance claims; or
- edit generated, external-extension, upstream, user-data, launcher, or protected unrelated files.

## Existing Architecture

### Public callback owner

Committed `src/wwwroot/js/genpage/main.js` declares:

```js
let sessionReadyCallbacks = [];
```

The array is a classic-script global used directly by core and extension scripts. There is no registration helper and no post-session-ready callback collection.

### Maintained registrations

The approved committed tree contains six maintained `sessionReadyCallbacks.push(...)` registrations:

1. `src/wwwroot/js/genpage/main.js` registers the active server-lazy-tab activation check.
2. `src/wwwroot/js/genpage/promptlab.js` registers `promptLab.init`.
3. `src/wwwroot/js/genpage/helpers/settings_editor.js` registers `loadSettingsEditor`.
4. `src/BuiltinExtensions/ComfyUIBackend/Assets/comfy_workflow_editor_helper.js` registers `comfyCheckPrep`.
5. `src/BuiltinExtensions/GridGenerator/Assets/grid_gen.js` registers its permission-gated Grid Generator setup.
6. `src/BuiltinExtensions/ImageBatchTool/Assets/image_batcher.js` registers its Image Batch Tool setup.

External extensions can also push callbacks directly. Their identities and behavior cannot be exhaustively inventoried and remain a compatibility constraint.

### Script and registration order

Committed `src/Pages/Text2Image.cshtml` loads base generation scripts in order. It loads `main.js`, then Prompt Lab, Settings Editor, UI Improvements, and media controls. `WebServer.PageFooterExtra` appends extension scripts afterward, and `finalscript.js` schedules `genpageLoad()` last.

The callback array therefore reflects classic-script execution and extension contribution order. Rank 17 preserves the actual insertion order rather than introducing a separate priority or registration order.

### Approved-base dispatch and tail (historical)

At the approved source base, inside the successful `ListT2IParams` response callback, the page constructed models, parameters, tools, persisted controls, and initial user-data work. It then executed:

```js
for (let callback of sessionReadyCallbacks) {
    callback();
}
```

The following statements ran only if the historical loop completed:

1. `startPendingKritaImportPoll();`
2. `automaticWelcomeMessage();`
3. `autoTitle();`
4. `swarmHasLoaded = true;`
5. `scheduleInitialImageHistoryLoad(250);`

`genericRequest` does not catch an exception thrown by its successful response callback. Before Rank 17, one callback exception therefore suppressed all later callback owners and the complete tail.

## Approved Design

### Synchronous dispatcher

Add one documented function in `src/wwwroot/js/genpage/main.js` with one responsibility: synchronously dispatch the current `sessionReadyCallbacks` array while isolating each entry's synchronous exception.

The dispatcher:

1. uses a standard indexed `for` loop;
2. re-evaluates `sessionReadyCallbacks.length` on each iteration;
3. reads the current callback at that index;
4. invokes it immediately with `callback()`;
5. catches only synchronous exceptions from that entry;
6. reports the failure; and
7. advances to the next current array index.

An indexed loop preserves the current observable dynamic-array behavior. A callback appended during dispatch is eligible to run later in the same dispatch. Current-index mutation and shifting retain ordinary JavaScript array traversal semantics; Rank 17 does not snapshot or lock the array.

The dispatcher does not return a promise and does not await callback results. A callback that starts asynchronous work continues to own its promise handling. Existing maintained asynchronous starters, such as the server lazy-tab check, retain their own `.catch(...)` behavior.

### Call-site replacement

Replace only the direct `for ... of` loop in the successful `ListT2IParams` branch with one call to the dispatcher.

The dispatcher call remains:

- after the existing model, parameter, tool, control, and user-data startup work;
- before `startPendingKritaImportPoll()`; and
- on the same synchronous call stack.

The five tail statements remain textually ordered and execute after the dispatcher returns.

### Failure identity and message

For each caught exception, derive:

- a one-based callback position from the current index;
- the callback's `name` when the entry is a function and JavaScript exposes a non-empty name; and
- readable error text through string interpolation, consistent with nearby frontend initialization catches.

The message format must clearly identify the failure as a session-ready callback failure. Anonymous callbacks still receive their one-based position. A malformed non-function array entry is naturally treated as a failed entry and remains isolated; identity formatting must not itself assume that the entry is callable.

### Console and toast reporting

The catch path:

1. calls `console.error` with the diagnostic message and original exception;
2. calls the existing `showError(message)` so the failure is visible to the user; and
3. guards toast rendering with its own `try`/`catch`.

If toast rendering itself fails, the dispatcher logs that secondary reporting failure and continues. A reporting-path exception must not recreate the original startup-abort behavior.

Multiple callback failures are reported independently. Rank 17 does not aggregate, suppress, or deduplicate them. Existing toast behavior determines which message remains visually current; the console retains each diagnostic.

### Exception and partial-effect semantics

The original callback exception is contained after reporting and is not rethrown. Later callbacks and the startup tail continue.

Any state a callback changed before throwing remains changed. The dispatcher does not retry the callback, invoke compensation, or restore state. This is deliberate: no generic rollback can safely understand independent core or extension initializer ownership.

Exceptions from later asynchronous work remain outside this synchronous boundary. Rank 17 does not convert promise rejections into callback-loop failures and does not delay the startup tail for a returned promise.

## Production Scope

Implemented production modification:

- `src/wwwroot/js/genpage/main.js`
  - add the documented synchronous dispatcher;
  - replace the direct session-ready loop with one dispatcher call; and
  - change no other production behavior.

Confirmed unchanged source includes:

- all six maintained callback registration sites;
- `sessionReadyCallbacks` declaration and array identity;
- `src/Pages/Text2Image.cshtml` script order;
- `src/Core/WebServer.cs` extension script contribution;
- `src/wwwroot/js/site.js::showError`;
- `genericRequest`, `getSession`, and `ListT2IParams` request behavior;
- the complete startup work before dispatch;
- the complete tail after dispatch;
- other callback arrays such as `featureSetChangedCallbacks`;
- built-in and external extension registration contracts;
- all server routes, permissions, persistence, and public source/binary ABI; and
- all CSS, Razor, C#, Python, launchers, generated content, extensions, upstream code, and user data.

## Protected Working-Tree Boundary

At the approved base, the maintainer has unrelated unstaged changes in:

- `src/Data/Settings.fds`;
- `src/Pages/Text2Image.cshtml`;
- `src/wwwroot/js/genpage/gentab/loras.js`;
- `src/wwwroot/js/genpage/main.js`; and
- untracked `Data.pre-restore-2026-07-19/`.

The committed `44e3317a177978926d4b7b280f514d4cf1fcce6b` snapshot is the Rank 17 design authority. The implementation workflow:

1. confirm the callback declaration, maintained registration, direct loop, and startup tail still match the approved committed boundary;
2. confirm the intended working-file edit does not overlap the maintainer's unrelated `main.js` hunks;
3. stop for maintainer direction if the target region overlaps or its semantics differ;
4. use `apply_patch` for the source edit; and
5. stage and commit only the Rank 17 hunk, leaving every unrelated maintainer hunk unstaged and byte-preserved.

No design or implementation commit may include the other protected paths or the pre-restore backup.

## Compatibility Analysis

### Direct extension registration

Extensions continue to call `sessionReadyCallbacks.push(callback)` directly. No wrapper, metadata object, or callback type conversion is introduced.

### Order and timing

Callbacks remain synchronous and run in current insertion order on the same successful response stack. The startup tail still begins only after synchronous callback dispatch ends.

### Dynamic mutation

The dispatcher does not snapshot the array. Callbacks appended during dispatch retain eligibility to run in that dispatch, matching the current iterator's dynamic-length behavior.

### Function invocation

Callbacks are still invoked as plain functions with no arguments and no explicit receiver. No callback input, `this` binding, return-value use, or completion signal is added.

### Browser support

The design uses standard indexed loops, `try`/`catch`, `typeof`, function `name`, template strings, and existing `console.error`/`showError` facilities already compatible with the maintained modern desktop and mobile browser baseline.

### Performance

The dispatcher adds one `try` boundary per startup callback and performs formatting only on failure. No material performance improvement or regression is claimed, and Rank 17 is not a performance project.

## Static Verification

Agents may perform static inspection only. They must not build, launch, test, automate a browser, start a backend, call live APIs, or execute test-running linters.

Static verification must:

1. pin the approved base and final source commit;
2. distinguish integrated documentation history from the `main.js` source projection;
3. inventory every maintained declaration, push, and dispatch use;
4. inspect committed Text2Image and extension script order;
5. compare the complete old loop, new dispatcher, call site, and startup tail;
6. prove one per-entry synchronous `try`/`catch`;
7. prove a caught exception advances to later entries;
8. prove callback-appended entries remain eligible through dynamic length;
9. prove returned promises are not awaited or inspected;
10. prove toast reporting has a secondary guard;
11. prove the five tail statements remain after dispatch in their original order;
12. prove all six maintained registration files are unchanged;
13. prove `sessionReadyCallbacks` remains the same direct array surface;
14. prove `showError`, request handling, script composition, and other callback collections are unchanged;
15. inspect the exact source diff and public extension compatibility boundary;
16. confirm the production commit contains only the intended `main.js` hunk;
17. confirm protected maintainer hunks remain unstaged and absent from commits; and
18. run `git diff --check`.

Static evidence can establish lexical isolation, invocation order, synchronous behavior, source scope, and compatibility surfaces. It cannot prove browser rendering, actual callback timing, external extension behavior, toast visibility, runtime initialization success, platform behavior, or performance.

## Implementation Record

Rank 17 is statically implemented and remains awaiting maintainer browser/runtime validation.

### Provenance and committed scope

- The approved source/audit base is `44e3317a177978926d4b7b280f514d4cf1fcce6b`.
- The design commit is `a70efd3280d09b3e457d66f2e8ef62c16259ce4b` (`a70efd32`, `docs: design session-ready callback isolation`).
- The plan commit is `e6bf67c6bc576254b4ac2e6d81252399dc36db37` (`e6bf67c6`, `docs: plan session-ready callback isolation`).
- The production source head and sole source commit are `12604a8f29444730edce871056e39c2ab523efa2` (`12604a8f`, `fix: isolate session-ready callback failures`).
- Integrated history from the design through the source head contains the plan commit followed by the production commit. Path-filtering that history to `src/wwwroot/js/genpage/main.js` returns only the production commit.
- The production projection changes exactly `src/wwwroot/js/genpage/main.js`, with `22 insertions(+), 3 deletions(-)` in two semantic hunks: the documented `runSessionReadyCallbacks()` addition immediately after the unchanged array declaration, and replacement of the former three-line direct loop with one dispatcher call.

### Implemented behavior and preserved boundaries

`runSessionReadyCallbacks()` uses a standard indexed loop whose condition reads the live `sessionReadyCallbacks.length` on every iteration. It reads the current entry once and invokes it as plain `callback()` with no arguments, receiver, return-value use, or completion signal. An entry appended during dispatch is therefore eligible later in that same dispatch; the array is not copied, snapshotted, locked, frozen, renamed, replaced, or reassigned.

Each entry has its own synchronous `try`/`catch`. A synchronous exception—including invocation of a non-function entry—is contained after a diagnostic with its one-based position, optional function name, and `${e}` text. `console.error(message, e)` retains the original exception; `showError(message)` uses the existing toast path; and a position-specific secondary `try`/`catch` logs a toast-rendering failure without aborting later entries. Multiple callback failures are independently reported and later callbacks continue.

The dispatcher does not use `async`, `await`, `Promise.resolve`, `.then`, or `.catch` on callback results. A returned pending or rejected promise remains owned by its callback and does not delay dispatch or the startup tail. A callback is not retried, its partial effects are not rolled back, and no compensation, metadata, priority, registration helper, telemetry, instrumentation, or new public surface was added.

The `sessionReadyCallbacks` declaration and exact six maintained non-backup direct `push` registrations are unchanged, so external classic-script direct registration remains compatible. Text2Image script order, `WebServer.PageFooterExtra` extension contribution, `site.js::showError`, the other callback arrays, and extension composition remain unchanged. The complete successful `ListT2IParams` work before dispatch is unchanged. After the dispatcher returns, the exact five-statement tail remains textually ordered as `startPendingKritaImportPoll()`, `automaticWelcomeMessage()`, `autoTitle()`, `swarmHasLoaded = true`, and `scheduleInitialImageHistoryLoad(250)`. The failure branch and request behavior are unchanged. Lazy-tab, hash, Krita, welcome, title, and history behavior outside the new synchronous exception boundary, plus all server APIs, permissions, persistence, and public source/binary ABI, are unchanged.

### Protected working state

The source commit used partial staging. It excluded the maintainer's two unrelated unstaged `src/wwwroot/js/genpage/main.js` hunks: removal of the `featureSetChangedCallbacks` declaration and four `lazyTabState` changes from `loaded: false` to `loaded: true` for `imageediting`, `utilities`, `user`, and `server`. Both hunks remain unstaged after the source commit. The other protected tracked changes in `src/Data/Settings.fds`, `src/Pages/Text2Image.cshtml`, and `src/wwwroot/js/genpage/gentab/loras.js`, plus untracked `Data.pre-restore-2026-07-19/`, were not staged or included. The committed source projection contains neither protected `main.js` hunk.

### Static evidence

Fresh independent source reviews returned `SOURCE_SPEC_APPROVED` and `SOURCE_QUALITY_APPROVED` with no findings. The following static commands and observed results bound that approval:

```bash
git log --oneline a70efd32..12604a8f29444730edce871056e39c2ab523efa2
git log --oneline a70efd32..12604a8f29444730edce871056e39c2ab523efa2 -- src/wwwroot/js/genpage/main.js
```

The integrated command returned `12604a8f` and `e6bf67c6`; the path-filtered command returned only `12604a8f`.

```bash
git diff --name-only a70efd32..12604a8f29444730edce871056e39c2ab523efa2 -- src/wwwroot/js/genpage/main.js
git diff --stat a70efd32..12604a8f29444730edce871056e39c2ab523efa2 -- src/wwwroot/js/genpage/main.js
git diff --check a70efd32..12604a8f29444730edce871056e39c2ab523efa2 -- src/wwwroot/js/genpage/main.js
```

These returned only `src/wwwroot/js/genpage/main.js`, the exact `1 file changed, 22 insertions(+), 3 deletions(-)` statistic, and silent whitespace success. Direct inspection of that projection counted two `@@` hunks and no protected-hunk text.

```bash
git grep -n -F 'sessionReadyCallbacks.push' 12604a8f29444730edce871056e39c2ab523efa2 \
  -- 'src/**' ':(exclude)src/Extensions/**' ':(exclude)**/*.bak'
```

This returned exactly six maintained registrations: Comfy workflow preparation, Grid Generator, Image Batch Tool, Settings Editor, the `main.js` server-lazy-tab check, and Prompt Lab. The same committed-tree inventory found one unchanged array declaration, one dispatcher definition, and one dispatcher call.

```bash
git diff a70efd32..12604a8f29444730edce871056e39c2ab523efa2 -- src/wwwroot/js/genpage/main.js \
  | rg -n '^\+.*(await.*callback|Promise\.resolve\(callback|callback\(\)\.then|callback\(\)\.catch|sessionReadyCallbacks\.slice|\[\.\.\.sessionReadyCallbacks\])'
```

This returned no matches (`rg` exit 1), confirming that the added source contains no async/promise-result or snapshot behavior. The unchanged-file diff for the five registration-owner files outside `main.js`, plus `Text2Image.cshtml`, `WebServer.cs`, and `site.js`, was silent; separate inspection confirmed that the maintained registration inside `main.js` was unchanged.

```bash
git diff --unified=0 -- src/wwwroot/js/genpage/main.js
git diff --cached --name-only
git status --short
```

Before this documentation edit, the first command showed exactly the two protected unstaged `main.js` hunks described above, the cached-name command was empty, and status retained all four protected tracked files plus the untracked backup directory. `git show --check --oneline --stat 12604a8f29444730edce871056e39c2ab523efa2` also completed cleanly, and `git show --format= --name-only 12604a8f29444730edce871056e39c2ab523efa2` returned only `src/wwwroot/js/genpage/main.js`.

All agent evidence is static. No agent build, test, test-executing lint, browser automation, launch, server/backend execution, live API call, runtime timing, platform/filesystem validation, or performance measurement was performed, and no such result is claimed. Browser timing and rendering, actual toast visibility, external-extension behavior, asynchronous rejection behavior, platform/filesystem behavior, and the exact validation cases below remain pending maintainer exercise.

## Maintainer Validation Matrix

The maintainer performs all builds and live/browser validation. Record the operating system, filesystem, browser, and exact outcomes.

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

## Success Criteria

Rank 17 is successful when:

- one synchronous session-ready callback exception cannot suppress later callbacks;
- one or more isolated callback exceptions cannot suppress the unchanged core startup tail;
- every callback retains synchronous, insertion-order, no-argument invocation;
- callback identity and error detail are visible in the console and existing toast path;
- toast-rendering failure cannot recreate the dispatch abort;
- direct array registration and dynamic append behavior remain compatible;
- async ownership, retry, rollback, and unrelated startup behavior remain unchanged;
- production scope is limited to the dispatcher and call-site replacement in `main.js`;
- protected maintainer work remains unstaged and absent from Rank 17 commits; and
- documentation does not claim unperformed runtime, platform, browser, filesystem, or performance results.

## Alternatives Considered

### Inline `try`/`catch` in `genpageLoad()`

This is slightly smaller textually, but leaves error formatting and dispatch ownership embedded inside the already broad startup callback. A dedicated dispatcher gives the lifecycle one named, reviewable boundary without changing behavior elsewhere.

### Registration wrapper with metadata

A helper could collect explicit owner names and richer metadata. Existing core and extension code directly pushes functions into the public array, so requiring or preferring a wrapper would create partial coverage and a migration surface that Rank 17 does not need.

### Promise-aware or sequential async dispatcher

Awaiting callback results could isolate rejected promises, but it would change startup timing, ordering relative to the core tail, and extension behavior. Rank 17 addresses the confirmed synchronous exception gap only.

### Snapshot dispatch

Copying the array before iteration would simplify a fixed dispatch set, but it would change the current behavior for callbacks appended during traversal. The approved design retains dynamic-array semantics.

### Console-only reporting

Console-only reporting would be quieter but would fail the approved requirement that an initializer failure be visible to the user. The design uses the existing toast and keeps full diagnostic detail in the console.

## Rollback

Rollback removes the dispatcher and restores the direct `for ... of` callback loop. No data migration, configuration conversion, persisted-format change, extension registration conversion, server change, or public API rollback is required.
