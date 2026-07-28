# WebSocket Lifecycle Handler Session-Renewal Design

**Status:** Implemented and maintainer-validated on Garuda Linux (Arch-based), Btrfs, using Firefox (version not provided)

**Date:** 2026-07-27

**Roadmap scope:** Frontend F1, rank 18

**Approved source and audit base:** `9080e620c76e6101adb7af2544eacc5ab43460c1`

## Summary

At the approved base, `src/wwwroot/js/site.js::makeWSRequest(url, in_data, callback, depth, errorHandle, onOpenHandle)` preserved the caller's data callback when it renewed an invalid session, but its recursive call dropped `errorHandle` and `onOpenHandle`.

The first physical WebSocket therefore used the caller's lifecycle handlers, while a retry after session renewal did not. A retry failure fell back to generic error reporting instead of caller-owned cleanup. A successful model-download retry did not rebind cancellation to the active retry socket because its open handler remained attached only to the initially rejected socket.

Rank 18 forwards both existing lifecycle handlers through only the invalid-session recursive call. The public signature, initial behavior, retry depth, request mutation, data callback, streaming delivery, and returned initial socket remain unchanged.

## Goals

1. Preserve a caller-supplied `errorHandle` across every invalid-session retry.
2. Preserve a caller-supplied `onOpenHandle` across every physical retry socket.
3. Rebind model-download cancellation to the active retry socket after session renewal.
4. Retain caller-owned cleanup and UI restoration when a retry fails.
5. Keep the production change to the smallest compatible transport boundary.

## Non-Goals

Rank 18 does not:

- change the `makeWSRequest` signature, defaults, name, or global availability;
- change `makeWSRequestT2I` or add an open-handler parameter to it;
- add a request context, options object, class, registry, or new public helper;
- change the data callback or streaming-message behavior;
- change the invalid-session depth limit or comparison;
- change `getSession`, session throttling, impersonation, cookies, permissions, or version handling;
- clone, freeze, replace, or otherwise change the mutable request object;
- explicitly close an invalid-session socket;
- return the retry socket to the original caller;
- replace the caller's retained reference to the initially returned socket;
- change generation socket tracking or preview-retry ownership;
- change WebSocket URL construction, request serialization, message parsing, or error text;
- change generic error behavior for callers that do not provide a lifecycle handler;
- catch lifecycle-handler exceptions or change their timing;
- change server routes, payloads, permissions, persistence, or public C# ABI;
- redesign socket ownership or cancellation protocol;
- add telemetry, instrumentation, benchmarks, or performance claims; or
- edit callers, extensions, backup files, generated content, user data, or unrelated protected files.

## Existing Architecture

### Transport owner

At the approved base, `site.js` defines:

```js
function makeWSRequest(url, in_data, callback, depth = 0, errorHandle = null, onOpenHandle = null) {
```

The function:

1. derives the WebSocket address;
2. creates and returns the initial `WebSocket`;
3. writes the current `session_id` into `in_data` on open;
4. sends the serialized request;
5. calls `onOpenHandle(socket)` when supplied;
6. parses every message;
7. renews and retries an invalid session;
8. sends non-error messages to `callback`;
9. sends application errors through the local `fail` helper; and
10. assigns either the supplied error handler or generic error UI to `socket.onerror`.

### Approved-base invalid-session recursion

The approved base contains:

```js
if (data.error_id && data.error_id == 'invalid_session_id') {
    if (depth > 3) {
        fail(failedDepth.get());
        return;
    }
    console.log('Session refused, will get new one and try again.');
    getSession(() => {
        makeWSRequest(url, in_data, callback, depth + 1);
    });
    return;
}
```

The recursive call preserves the URL, mutable input object, data callback, and incremented depth. It does not preserve the two lifecycle handlers.

### Established HTTP retry pattern

`genericRequest` already forwards its caller-owned failure and timeout behavior through both invalid-session and bad-impersonation recursion:

```js
genericRequest(url, in_data, callback, depth + 1, errorHandle, timeoutMs);
```

Rank 18 follows that existing transport pattern without coupling the HTTP and WebSocket implementations.

## Maintained Consumer Inventory

The approved committed tree contains five maintained non-backup direct `makeWSRequest` callers plus the function's own recursive call:

1. `src/wwwroot/js/genpage/gentab/generatecontrols.js::makeWSRequestT2I` forwards an optional generation error handler.
2. `src/wwwroot/js/genpage/gentab/models.js::trt_modal_create` restores the create button and writes an error result through its error handler.
3. `src/wwwroot/js/genpage/utiltab.js::LoraExtractorUtil.run` resets progress and writes an error through its error handler.
4. `src/wwwroot/js/genpage/utiltab.js::ModelDownloaderUtil.download` owns retry/error UI and supplies the only maintained `onOpenHandle`.
5. `src/wwwroot/js/installer.js::InstallerClass.submit` re-enables the confirmation button through its error handler.

`makeWSRequestT2I` is also used by maintained generation, Grid Generator, Image Batch Tool, model selection, image editor, and related T2I flows. `GenerateHandler.doGenerate` depends on its supplied error handler to clear tracked socket/card state, mark failures, and retain preview-retry behavior.

External classic-script callers can invoke the global function directly and cannot be exhaustively inventoried. Its full positional signature and timing are compatibility constraints.

## Approved Design

Change only the recursive call to:

```js
makeWSRequest(url, in_data, callback, depth + 1, errorHandle, onOpenHandle);
```

No wrapper, new state, helper, or caller edit is required. Each invocation already closes over the correct lifecycle handlers in its local `fail`, `onopen`, and `onerror` functions.

## Data Flow

### Initial socket

The initial call returns its newly created socket exactly as before. When that socket opens, it writes the current session ID into the original `in_data` object, sends the request, and calls `onOpenHandle(initialSocket)` when supplied.

### Session rejection and renewal

An `invalid_session_id` message retains the existing `depth > 3` gate. If retry remains allowed, `getSession` obtains a new session and invokes its callback. Rank 18 then recursively passes all six current arguments, incrementing only `depth`.

### Retry socket

The recursive call creates a new physical socket. Its open event overwrites `in_data.session_id` with the renewed value, sends the same mutable request object, and calls `onOpenHandle(retrySocket)`.

The approved semantic choice is explicit: `onOpenHandle` is a per-physical-socket callback. It continues to run for the initially opened socket and also runs for every retry socket that opens. For model download, each call replaces the cancel button's click handler with a closure over the newest socket, so cancellation targets the active retry socket.

### Streaming and completion

Non-error messages from the retry continue to the original `callback`. Rank 18 does not buffer, reorder, deduplicate, await, or otherwise change streaming delivery.

## Error Handling

Forwarding `errorHandle` makes the retry invocation's existing failure paths caller-owned:

- an unavailable WebSocket address calls the original handler through `fail`;
- retry depth exhaustion calls the original handler through `fail`;
- a retry application-error payload calls the original handler through `fail`; and
- a retry socket error invokes the original handler through `socket.onerror`.

Callers without an `errorHandle` retain the existing console/toast behavior. Rank 18 does not catch exceptions thrown by either lifecycle handler.

`getSession` acquisition failure behavior is unchanged. The original invalid-session socket is not explicitly closed. The socket returned by a recursive call remains ignored inside the `getSession` callback, so the outer caller still retains only the initially returned socket. Those ownership questions are deliberately deferred.

## Compatibility Analysis

### Public and extension surface

The global name, six positional parameters, defaults, return type, and direct-call behavior remain unchanged. Existing callers do not need modification. External callers that already supply lifecycle handlers gain consistent retry behavior without migration.

### Callback timing

`onOpenHandle` still runs synchronously in the socket's open event after `socket.send`. The data callback still runs synchronously from `onmessage` for each non-error message. The error handler still runs from the existing failure branch or socket error event.

### Retry semantics

The initial depth remains zero. Every invalid-session retry increments by one. The existing `depth > 3` check and maximum sequence remain textually unchanged.

### Request identity

The same `in_data` object crosses retries. Its `session_id` continues to be overwritten at each physical socket open. No new cloning or ownership rule is introduced.

### Returned socket and tracking

The outer call still returns only its initial socket. Rank 18 does not update `GenerateHandler`'s retained socket reference or change how generation reuses, clears, or compares it. Model-download cancellation is corrected through the already approved per-socket open callback, not through a return-value change.

### Browser support

The implementation adds no syntax and changes only existing positional arguments. It remains within the repository's modern Chrome, Firefox, Safari, Android Chrome, and iOS Safari compatibility baseline.

### Performance

The change adds no new work outside passing two references during the retry branch. Rank 18 makes no performance improvement or regression claim.

## Protected Working-Tree Boundary

At the approved base, the maintainer has unrelated unstaged changes in:

- `src/Data/Settings.fds`;
- `src/Pages/Text2Image.cshtml`;
- `src/wwwroot/js/genpage/gentab/loras.js`;
- `src/wwwroot/js/genpage/main.js`; and
- untracked `Data.pre-restore-2026-07-19/`.

The target `src/wwwroot/js/site.js` is clean at the approved base. Design, plan, source, and closure commits must exclude every protected path and the backup directory.

## Static Verification

Agents may perform static inspection only. They must not build, launch, test, automate a browser, start a server/backend, call live APIs, or execute test-running linters.

Static verification must:

1. pin the approved base and final production commit;
2. distinguish integrated documentation history from the one-file source projection;
3. inventory the global function, direct maintained callers, `makeWSRequestT2I` consumers, and lifecycle-handler consumers;
4. compare the complete old and new recursive calls;
5. prove only `errorHandle` and `onOpenHandle` were added to recursion;
6. prove the function signature and defaults are unchanged;
7. prove the depth comparison, increment, and `getSession` placement are unchanged;
8. prove URL, input object, data callback, serialization, message parsing, and streaming branches are unchanged;
9. prove initial and retry `onopen` behavior remains send-then-open-handler;
10. prove retry failure paths now close over the original error handler;
11. prove no explicit socket close, retry return propagation, request clone, context object, or caller edit was added;
12. prove `genericRequest` is unchanged;
13. prove all maintained callers and extension surfaces are unchanged;
14. prove production scope is exactly `src/wwwroot/js/site.js`;
15. prove protected maintainer work remains unstaged and absent from commits; and
16. run `git diff --check`.

Static evidence can establish argument propagation, callback placement, preserved branches, caller signatures, and source scope. It cannot prove browser event ordering, live renewal, UI restoration, cancellation delivery, external-extension behavior, platform behavior, or performance.

## Implementation Record

- **Provenance and source projection:** the approved source/audit base is `9080e620c76e6101adb7af2544eacc5ab43460c1`; the approved design is `646552d48248ef37c01a52d0b998b3087b11af0c`; terminology correction `887a4323545aaab56e68ee6f1b0a7b144906f82d` corrects the rollback description from five arguments to four; plan commit `63cce31e6fe00776be97f72c6bdb71766d199846` (`docs: plan websocket lifecycle handler renewal`) records the static implementation plan; production commit/source head `9f8f93af4b3d7d6c7869da8ff89d385b25a2d2e0` (`fix: preserve websocket retry lifecycle handlers`) implements the approved call; implementation closure `f08b278a` (`docs: record websocket lifecycle handler renewal`) records the completed source boundary; specification correction `ddb0902b` (`docs: correct websocket renewal closure provenance`) corrects that closure provenance; quality correction `f9e3eadc` (`docs: clarify websocket renewal historical boundary`) clarifies the historical-versus-current boundary; validation commit `709e72aa` (`docs: validate websocket lifecycle handler renewal`) records the maintainer result; and validation-quality correction `7b17a97f` (`docs: clarify websocket renewal pre-fix behavior`) makes the historical pre-fix consequences explicit. The integrated history from the approved base through the source head contains the design, terminology correction, plan, and production commits. Path-filtering that history to `src/wwwroot/js/site.js` returns only the production commit; the closure, corrections, and validation records are documentation-only.
- **Exact production boundary:** production changes exactly `src/wwwroot/js/site.js`, one file and one hunk, with `1 insertion, 1 deletion`. It replaces the old recursion `makeWSRequest(url, in_data, callback, depth + 1);` with `makeWSRequest(url, in_data, callback, depth + 1, errorHandle, onOpenHandle);`. No other source file, caller, extension, generated file, or protected path is part of the production commit.
- **Implemented behavior:** every recursive invalid-session invocation now closes over the original `errorHandle` and `onOpenHandle`. The open handler therefore retains its approved per-physical-socket meaning: it runs after `socket.send` for the initially opened socket and for every retry socket that opens. The retry invocation's existing unavailable-address, depth-exhaustion, application-error, and socket-error paths now retain caller-owned failure handling. This statically preserves model-download rebinding to the newest opened socket and the maintained cleanup handlers for generation, TensorRT, LoRA extraction, model download, and installer flows.
- **Maintained caller and handler inventory:** the unchanged maintained direct callers remain `makeWSRequestT2I`, `trt_modal_create`, `LoraExtractorUtil.run`, `ModelDownloaderUtil.download`, and `InstallerClass.submit`, plus the function's own recursive call. The unchanged `makeWSRequestT2I` consumers remain generation, Grid Generator, Image Batch Tool, model selection, image editor, and related T2I flows. Model download remains the sole maintained `onOpenHandle` consumer; the maintained custom error-handler consumers and the external classic-script compatibility boundary remain as documented above.
- **Preserved contracts and deferred ownership:** the global name, six-parameter signature and defaults, initial depth, `depth > 3` gate, `depth + 1` increment, `getSession` placement and behavior, mutable `in_data` identity and session overwrite, URL construction, request serialization and send-before-open-handler order, parsing, data callback, streaming delivery, error conversion and generic handler-free behavior, initial returned socket, ignored recursive return, maintained callers, extension surface, server routes and payloads, permissions, persistence, and public C# ABI are unchanged. Explicit closure of the invalid-session socket and propagation or reassignment of the retry socket return remain deferred; the outer caller still retains only the initially returned socket.
- **Review and static evidence:** independent source reviews returned `SOURCE_SPEC_APPROVED` and `SOURCE_QUALITY_APPROVED`, both with no findings; documentation reviews returned `DOCS_SPEC_APPROVED` and `DOCS_QUALITY_APPROVED`, both with no findings; validation-document reviews returned `VALIDATION_SPEC_APPROVED` and `VALIDATION_QUALITY_APPROVED`, both with no remaining findings. Static inspection confirmed the integrated-versus-path-filtered history, exact one-file/one-hunk `1/1` source projection, complete old/new recursion, unchanged surrounding function and `genericRequest`, maintained caller/handler inventory, unchanged source surfaces, clean fixed-range whitespace, empty index before this documentation edit, and isolation of the protected working state.
- **Validation boundary and protected state:** all agent evidence is static-only. Agents performed no build, test, test-executing lint, browser automation, launch, server/backend execution, live API call, runtime timing, platform or filesystem validation, or performance measurement, and no such result is claimed. Maintainer runtime evidence is recorded separately after the exact unchanged 14-case matrix below. The unrelated unstaged changes in `src/Data/Settings.fds`, `src/Pages/Text2Image.cshtml`, `src/wwwroot/js/genpage/gentab/loras.js`, and `src/wwwroot/js/genpage/main.js`, plus untracked `Data.pre-restore-2026-07-19/`, remain protected and outside the Rank 18 commits.

## Maintainer Validation Matrix

The maintainer performs all browser/runtime validation and records the browser, browser version when available, operating system, and filesystem.

1. An ordinary successful request with both lifecycle handlers invokes the open handler once for the initial socket, streams data through the original callback, and does not call the error handler.
2. An ordinary application or socket failure without session renewal reaches the supplied error handler and retains its caller-specific cleanup.
3. One invalid-session response followed by successful renewal invokes the open handler for both the initially rejected socket and the retry socket, streams retry data through the original callback, and does not call the error handler.
4. Multiple invalid-session renewals within the existing depth allowance invoke the same open handler once per opened physical socket and retain the same data and error handlers through final success.
5. Exhausting the existing retry depth invokes the original error handler once for the terminal depth error and performs no further retry.
6. A retry-socket transport failure invokes the original error handler and does not fall back to generic error UI.
7. A retry application-error payload invokes the original error handler with the existing readable error conversion.
8. Generation renewal success retains normal status/progress/output streaming, while renewal failure retains tracked-socket/card cleanup, ordinary failure reporting, and preview-retry behavior.
9. TensorRT renewal success retains status/complete behavior, while renewal failure re-enables the create button and writes the error result.
10. LoRA extraction renewal success retains progress/completion behavior, while renewal failure writes the error and resets both progress bars.
11. Model-download renewal success rebinds the cancel button to the retry socket; cancel sends the existing signal through that socket, and success/error/retry/remove UI remains functional.
12. Installer renewal success retains progress and navigation behavior, while renewal failure re-enables the confirmation button and displays the error.
13. A caller without custom lifecycle handlers retains the existing generic error path before and after session renewal.
14. A direct extension-style caller retains the same positional signature, initial returned socket, send-before-open-handler timing, streaming callback behavior, per-physical-socket open calls, and error callback behavior.

## Maintainer Validation Record

- **Maintainer and date:** Reaper176, 2026-07-27.
- **Raw evidence:** the first message was exactly `ll 14 passed on Garuda Linux (Arch-based), Btrfs, using`; the second message was exactly `firefox.`.
- **Disclosed interpretation and normalized outcome:** the controller disclosed that the omitted initial `A` is normalized to `All` and that the second message completes the browser field. The normalized outcome is `All 14 passed on Garuda Linux (Arch-based), Btrfs, using Firefox (version not provided).`
- **Browser version:** no browser version was supplied or inferred.
- **Evidence boundary:** this maintainer runtime result validates the exact unchanged 14-case matrix above only on the recorded environment and browser. Agent evidence remains static-only; agents performed no build, test, test-executing lint, browser automation, launch, server/backend execution, live API call, runtime timing, platform or filesystem validation, or performance measurement.
- **Retained caveats:** `onOpenHandle` retains per-physical-socket semantics; external direct callers are not exhaustively inventoried; `getSession` acquisition-failure behavior is unchanged; the rejected socket is not explicitly closed; the retry return is ignored and the outer caller retains the initial socket; generation tracking is unchanged; lifecycle-handler exceptions are not caught; mutable request identity and session overwrite behavior are unchanged; other browsers, platforms, and filesystems remain unvalidated; and no performance claim is made.

## Success Criteria

Rank 18 is successful when:

- both lifecycle handlers cross every invalid-session recursive call;
- every opened physical socket invokes the supplied open handler after sending;
- every retry failure uses the original supplied error handler;
- model-download cancellation targets the active retry socket;
- generation, TensorRT, LoRA extraction, model download, installer, and handler-free flows retain their documented behavior;
- the global function signature and external direct-call contract remain unchanged;
- depth, session acquisition, request mutation, streaming, and returned-initial-socket behavior remain unchanged;
- production scope is one recursive call in `site.js`;
- protected maintainer work remains unstaged and absent from Rank 18 commits; and
- documentation does not claim unperformed browser, runtime, platform, filesystem, or performance results.

## Alternatives Considered

### Forward lifecycle handlers through the recursive call

This is the approved approach. It follows `genericRequest`'s existing retry pattern, changes one call site, and repairs all maintained and external direct callers without migration.

### Introduce a request context object

A context could group the callbacks, retry depth, and socket state. It would expand the public and internal surface, require broader caller analysis, and risk extension compatibility without providing a Rank 18 benefit.

### Repair callers individually

Callers could detect renewal or rebind their own cleanup and cancellation. That would duplicate transport knowledge, miss external callers, and leave the root recursive argument loss intact.

## Rollback

Rollback restores the four-argument recursive call:

```js
makeWSRequest(url, in_data, callback, depth + 1);
```

No data migration, server rollback, or caller change is required.
