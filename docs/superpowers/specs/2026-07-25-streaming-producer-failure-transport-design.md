# Streaming Producer Failure Transport Design

**Status:** Implemented and maintainer-validated

**Date:** 2026-07-25

## Purpose

Correct the confirmed failure-transport gap in `API.RunWebsocketHandlerCallWS<T>` without redesigning WebSocket routing or producer callbacks.

Before implementation, an unexpected producer exception was logged after already-enqueued frames drained, but the helper then completed normally. Its route owner could not distinguish that fault from success. The client could therefore receive progress followed by a clean close, a final status, or an explicit success frame without any failure frame.

The selected change gives the shared helper an explicit Boolean result. After preserving all previously queued output, an unexpected producer fault produces exactly one generic `internal_error` frame for that helper invocation and returns `false`. Normal producer completion returns `true`. Route owners use that result to suppress only the success/finalization work that would contradict the failure.

## Original Boundary and Evidence

At the approved design boundary, `API.RunWebsocketHandlerCallWS<T>`:

1. creates a per-invocation `ConcurrentQueue<JObject>` and `AsyncAutoResetEvent`;
2. gives the producer an `Action<JObject>` that enqueues non-null frames and signals the drain loop;
3. starts the producer as a `Task`;
4. drains queued frames to the socket in FIFO order until the producer is complete and its queue is empty;
5. logs `t.Exception.ReadableString()` when the producer task is faulted; and
6. returns normally without sending a failure frame or exposing the producer fault to its caller.

An error object deliberately enqueued by a producer already reaches the client and is not treated as a task fault. Socket-send exceptions escape the helper. `API.HandleAsyncRequest` separately logs unexpected route exceptions and uses the generic client identity/message `error_id: "internal_error"` and `error: "An internal error occurred"`. It also gives `ConnectionClosedPrematurely` its established remote-disconnect handling and normally closes a WebSocket after its route returns.

The exact maintained helper-call inventory is six calls:

1. `T2IAPI.GenerateText2ImageWS` starts the initial `GenT2I_Internal` producer;
2. the same route starts a later `GenT2I_Internal` producer for each accepted socket-reuse request;
3. `ModelsAPI.SelectModelWS` runs `SelectModelInternal`;
4. `ComfyUIWebAPI.DoTensorRTCreateWS` runs its TensorRT producer;
5. `ComfyUIWebAPI.DoLoraExtractionWS` runs its LoRA producer; and
6. `ImageBatchToolExtension.ImageBatchRun` runs `GenBatchRun_Internal`.

There are five route owners because the T2I route contains two call sites. `RunWebsocketHandlerCallDirect<T>` is a separate direct-call helper and is outside this change.

## Pre-Implementation Caller Control Flow

`ModelsAPI.SelectModelWS` awaited the helper and then sent the current server status. An unexpected model-selection producer fault therefore appeared as a normal final status.

The TensorRT wrapper awaited the helper and returned `null`. Its refresh and `"Complete!"` frame occurred inside the producer after successful artifact movement. A fault before those steps was logged only on the server, after which the route returned as though its producer completed normally.

The LoRA wrapper awaited the helper, refreshed the LoRA model set, checked for the expected output, and then emitted a success or readable missing-output failure. An unexpected producer fault could therefore be obscured by post-fault refresh and derived output handling.

Image Batch awaited the helper, logged `"Image Batcher completed successfully"`, and sent `{ "success": "complete" }`. That contradictory success after an unexpected producer fault was statically confirmed.

T2I stored the initial and socket-reuse helper tasks in a concurrent task set. It removed completed tasks without observing a result, emitted `{ "socket_intention": "close" }` when the set became empty, allowed a two-second reuse window, and finally sent the current status. A producer fault was therefore unavailable to stop new reuse work or suppress the close-intention/final-status success path.

## Selected Approach

Change `API.RunWebsocketHandlerCallWS<T>` to return `Task<bool>`.

The helper retains one owner for queue transport:

- producer callbacks enqueue frames exactly as they do now;
- the helper drains the queue completely in its current FIFO order;
- after the producer is complete and its queue is empty, a fault is logged server-side with the detailed readable exception;
- the helper sends `Utilities.ErrorObj("An internal error occurred", "internal_error")` exactly once for that fault;
- the helper then returns `false`; and
- normal completion returns `true`.

“Exactly once” is per faulted helper invocation. The design does not add a global deduplication layer across independent concurrent T2I producers. Existing per-invocation FIFO ordering is preserved; the project does not invent a total order between separate producers that already share one socket.

This is preferred over:

- rethrowing the producer exception after draining, which would delegate failure transport to the outer dispatcher but would not give T2I's collected producer tasks a reliable explicit result and could change when queued output and route cleanup occur;
- adding a second error callback, which would duplicate transport ownership and expand every producer signature; and
- having each wrapper inspect or catch producer internals, which would duplicate the queue/fault policy and cannot work with the current swallowed fault.

## Public Helper Compatibility

The public helper's return type changes from `Task` to `Task<bool>`. Existing source callers that use `await API.RunWebsocketHandlerCallWS(...)` and discard the awaited result remain valid C#. Existing source callers that assign the result to `Task` also remain valid because `Task<bool>` derives from `Task`. The six maintained callers will be updated to consume the Boolean where required.

This return-type change is not claimed to preserve arbitrary precompiled binary ABI identity. CLR method metadata includes the return type in the method signature, so a precompiled consumer bound to the prior `Task` signature may require recompilation. Swarm's managed extension path builds extension source against the current core, and `ExtensionsManager.BuildExtension` includes the current core version and module version ID in its cache target, preventing reuse of a cached managed extension assembly built against a different core identity. That bounds the maintained source-extension risk but is not a universal guarantee for independently precompiled binaries.

A compatibility facade cannot keep the same method name and parameter list with both return types because C# cannot overload on return type alone. A differently named Boolean helper plus a legacy facade would preserve the old binary signature, but it would split the public transport entry point and alter the approved `RunWebsocketHandlerCallWS<T>` result contract. The selected design keeps one transport owner and records the exact source/binary tradeoff rather than falsely claiming ABI preservation.

## Failure and Security Semantics

The generic client frame contains only:

```json
{
  "error": "An internal error occurred",
  "error_id": "internal_error"
}
```

The producer exception type, message, stack, submitted values, paths, backend responses, and other detailed context remain server-log data. The detailed server entry continues to use the established `Logs.Error` plus `ReadableString()` path.

The helper sends the generic fault frame only after every frame already present in that invocation's queue has been sent. A progress frame enqueued before the exception therefore remains before the generic failure. No success or readable error already enqueued by the producer is rewritten, removed, classified, or redacted.

An explicitly enqueued readable error followed by normal producer completion remains a normal helper completion and returns `true`; the Boolean reports unexpected task failure, not the semantic content of producer frames. This project does not infer success or failure by inspecting arbitrary queued JSON.

Established cancellation behavior is not reclassified as an internal producer fault. Producers remain responsible for their current readable/cancellation handling; a task that reaches the canceled state without faulting retains the helper's current non-fault treatment, receives no new generic frame, and returns `true`. The Boolean is specifically an unexpected-producer-fault indicator, and the new `false` result is limited to `Task.IsFaulted`.

Socket sends remain outside a new catch. A timeout, send failure, or remote disconnect while draining either an existing frame or the generic failure frame continues to escape through the established route/dispatcher behavior. The helper does not retry a generic failure frame, synthesize a second error, or convert `ConnectionClosedPrematurely` into a producer result.

## Route-Owner Behavior

Each non-T2I wrapper stores the result in an explicit `bool` and branches immediately:

- `ModelsAPI.SelectModelWS` returns `null` on `false` before sending the final current-status frame.
- `ComfyUIWebAPI.DoTensorRTCreateWS` returns `null` on `false`. Its existing refresh and completion frame remain inside the producer and are reached only along that producer's successful path; no duplicate wrapper success frame is introduced.
- `ComfyUIWebAPI.DoLoraExtractionWS` returns `null` on `false` before `Program.RefreshModelSet("LoRA")`, output verification, success/failure logs, and success/readable-failure frames.
- `ImageBatchToolExtension.ImageBatchRun` returns `null` on `false` before the successful-completion log and `{ "success": "complete" }` frame.

On `true`, every wrapper continues its existing post-await flow unchanged.

The route methods continue returning `null`, so `API.HandleAsyncRequest` retains ownership of the normal WebSocket close. Route names, registration, permissions, request parameters, response shapes on successful flows, and public route signatures remain unchanged.

## T2I Socket-Reuse Coordination

The approved pre-final concept changed the initial and socket-reuse calls plus their tracked set to `Task<bool>` and proposed one route-local producer-failure state for a successfully completed helper that returned `false`. Final implementation refined that concept to two distinct one-way states: `producerFailed` for a successfully completed `Task<bool>` whose result is `false`, and `helperTaskFailed` plus the first captured `ExceptionDispatchInfo` in `helperTaskException` for a tracked helper task that faults or is canceled.

When a completed helper returns `false`, T2I:

1. marks `producerFailed`;
2. stops accepting follow-on generation requests;
3. cancels/wakes the receive loop through its existing route-local cancellation path without canceling already active generation producers;
4. retains every already-started helper in the task set until it completes;
5. observes, drains, and removes all active tracked helper tasks; and
6. returns `null` after that drain without sending `socket_intention: "close"` or the final current-status frame.

The helper returns `false` only after its queued progress and one generic producer-failure frame have been sent. The T2I route therefore returns normally after all tracked helpers drain, and the outer dispatcher performs the established normal WebSocket close. Other active producers may still finish and drain their existing outputs. Their completion does not clear `producerFailed` or restore follow-on acceptance.

When a tracked helper task faults or is canceled, T2I awaits it to observe the exception, captures an `ExceptionDispatchInfo` into `helperTaskException` only if no earlier capture has been retained, marks `helperTaskFailed`, and cancels/wakes the receive loop. It does not translate that helper-task exception into `producerFailed`, send another generic frame, or abandon the other tracked helpers. After all active tracked helpers have been observed, drained, and removed, `helperTaskException.Throw()` rethrows the first captured helper-task exception before any final current-status send. This preserves socket-send and helper-task exception propagation to `API.HandleAsyncRequest`; if both states were observed, the captured helper-task exception takes precedence after the common drain.

The receive loop checks both `producerFailed` and `helperTaskFailed` before waiting for input, after receiving data, and immediately before creating a new helper. A reuse request that was already accepted and started before either state became visible is treated as active work and is drained. A request observed after either state is set is not started. This bounds the unavoidable race without canceling unrelated active work or redesigning the socket-reuse protocol.

## Concurrency and Race Considerations

- Each helper invocation remains the sole consumer of its own concurrent output queue.
- The producer continuation continues waking the drain loop so a fault cannot leave it waiting for the two-second poll interval indefinitely.
- The queue-empty check remains paired with producer completion; the generic error is appended only after the producer can enqueue no more frames.
- Independent T2I helper invocations may interleave socket sends as they do today. No new cross-invocation serialization or ordering guarantee is claimed.
- `producerFailed` and `helperTaskFailed` are distinct one-way states. A later successful helper resets neither state.
- Observation of either state stops new reuse work but does not cancel or abandon helpers already in the tracked set.
- A reuse receive racing with either state is checked before producer creation; already-created work is observed and drained, while later work is rejected by the stopped receive loop.
- The normal success path retains the two-second socket-reuse window, close-intention frame, and final status.
- Both non-success paths suppress T2I's advisory close-intention and final status. The producer-`false` path returns normally for dispatcher-owned closure; the helper-task-exception path rethrows only after the common active-task drain.

## Compatibility Requirements and Non-Goals

The implementation must preserve:

- all API route names, registrations, permissions, parameter binding, and public route signatures;
- the six maintained helper call sites and their producer callback signatures;
- producer-enqueued readable errors and normal result/progress frames;
- FIFO order within each helper invocation's queued frames;
- normal model, TensorRT, LoRA, Image Batch, and T2I success frames;
- T2I's successful socket-reuse behavior, batch offsets, two-second reuse window, close intention, and final status;
- `API.HandleAsyncRequest` normal closure and its existing route-exception and premature-remote-disconnect behavior;
- socket-send timeout and exception behavior;
- direct-call behavior in `RunWebsocketHandlerCallDirect<T>`;
- browser request, progress, cleanup, retry, and error handling without browser-code changes; and
- detailed internal diagnostics only in server logs, with the generic `internal_error` identity/message on the new client failure frame.

This project does not redesign WebSockets, change producer signatures, interpret queued JSON, change expected readable errors, add retries, alter cancellation policy outside T2I's receive stop, serialize independent T2I producers, change route responses on successful flows, modify browser code, or claim a performance improvement.

The C# implementation must follow repository conventions: explicit types rather than `var`; full braced blocks; `else` on its own line; an updated `///` XML summary for the public helper's Boolean result; and explicit `Task<bool>`, `bool`, and concurrent-collection types in T2I.

## Migration Stages

1. Change the shared helper to `Task<bool>`, retain its existing queue/drain loop, add the post-drain detailed log plus one generic error send, and return `false` for a producer fault or `true` for normal producer completion.
2. Update Models, TensorRT, LoRA, and Image Batch to consume the Boolean and return before their applicable final status, refresh, success/failure derivation, logs, or success frames when it is `false`.
3. Change both T2I call sites and the tracked task set to `Task<bool>`; distinguish one-way `producerFailed` for a successful `false` result from one-way `helperTaskFailed` plus the first captured `ExceptionDispatchInfo` for a faulted or canceled helper task; stop follow-on acceptance for either state; observe and drain all active tasks; return normally after the drain for producer `false`; rethrow the captured helper-task exception after the drain and before final status; and suppress close intention/final status on both non-success paths without sending a duplicate generic frame.
4. Repeat the complete static inventory and control-flow review before maintainer runtime validation.

These stages form one behavior unit. Landing a Boolean helper without updating wrappers would preserve contradictory success paths; updating wrappers before the helper result exists would not compile.

## Static Verification

Agents will not build, launch, run tests, open sockets, inject faults, or exercise a browser. Static verification must:

1. repeat the exact six-call inventory and prove there are no additional maintained calls;
2. confirm `RunWebsocketHandlerCallDirect<T>` is unchanged;
3. trace enqueue, wake, FIFO drain, producer completion, detailed server log, one generic error send, and Boolean return ordering;
4. confirm the generic frame is exactly `Utilities.ErrorObj("An internal error occurred", "internal_error")`;
5. prove producer-enqueued readable errors and normal frames are not inspected or changed;
6. prove canceled producer tasks retain non-fault treatment and socket-send exceptions remain outside the producer-fault result path;
7. prove Models branches on `false` before final status;
8. prove LoRA branches on `false` before refresh, output inspection, terminal logs, and terminal frames;
9. prove TensorRT returns on `false` without adding duplicate completion work and retains its successful internal refresh/completion order;
10. prove Image Batch branches on `false` before its success log and success frame;
11. trace both T2I `Task<bool>` call sites; distinct one-way `producerFailed` and `helperTaskFailed` states; first captured `ExceptionDispatchInfo`; receive-loop stop for either state; active-task observation and drain; race checks; producer-`false` normal return after drain; helper-task exception rethrow after drain and before final status; no duplicate generic frame; and close-intention/final-status suppression on both non-success paths;
12. prove successful T2I still retains socket reuse, batch offsets, the two-second reuse window, close intention, and final status;
13. confirm route registrations/signatures, producer signatures, dispatcher close/error behavior, browser code, and direct helper are unchanged;
14. inspect extension-cache core-identity handling and record source compatibility without claiming arbitrary binary ABI compatibility;
15. inspect the exact changed-file and commit range; and
16. run `git diff --check`.

## Implementation Record

**Implementation status:** **Implemented and maintainer-validated.** The approved design was recorded in commit `47d32f4e`. Production is the exact range `5511d3a6^..84df8dfe`, whose parent is `51268f31` and whose final source commit is `84df8dfe`:

1. `5511d3a6` — `fix: transport streaming producer failures`
2. `f5f041b4` — `fix: stop streaming wrappers after producer faults`
3. `a1e32947` — `refactor: clarify streaming producer result`
4. `f02bb90d` — `fix: stop generation finalization after producer faults`
5. `84df8dfe` — `fix: drain generation helpers before rethrow`

The range changes exactly five source files:

- `src/WebAPI/API.cs`
- `src/WebAPI/ModelsAPI.cs`
- `src/BuiltinExtensions/ComfyUIBackend/ComfyUIWebAPI.cs`
- `src/BuiltinExtensions/ImageBatchTool/ImageBatchToolExtension.cs`
- `src/WebAPI/T2IAPI.cs`

The final helper inventory is exactly one `RunWebsocketHandlerCallWS<T>` definition plus six maintained calls: the initial and socket-reuse T2I calls, Models, TensorRT, LoRA, and Image Batch. `RunWebsocketHandlerCallDirect<T>`, its callers, producer callback signatures, dispatcher handling, route registrations, public route signatures, request parameters, permissions, and browser code are unchanged.

`API.RunWebsocketHandlerCallWS<T>` now returns `Task<bool>`. Its existing loop first drains every queued producer frame in FIFO order. Only after the producer is complete and that invocation's queue is empty does it inspect `t.IsFaulted`, log the detailed `t.Exception.ReadableString()`, send exactly one `Utilities.ErrorObj("An internal error occurred", "internal_error")`, and return `false`. Normal and canceled producer tasks are not faulted and return `true`. Producer-enqueued readable errors and other frames are transported without interpretation, so an explicit readable error followed by normal producer completion remains `true`. Socket sends remain outside a new catch; a send timeout, disconnect, or other send exception escapes rather than becoming `false`.

The four non-T2I owners use the accurate result name `producerCompletedWithoutFault` and return immediately when it is `false`. Models suppresses its final current-status frame. LoRA suppresses model-set refresh, output inspection, terminal logs, and terminal success/readable-failure frames. Image Batch suppresses its wrapper success log and success frame. TensorRT returns from the wrapper on `false`; its existing artifact move, model refresh, and `"Complete!"` frame remain internal to the producer and unchanged on the successful path. All four wrappers retain their prior post-await behavior on `true`.

T2I now tracks both helper call sites as `Task<bool>` and deliberately separates `producerFailed` from `helperTaskFailed`. A successful helper task returning `false` sets the one-way producer-failure state and cancels the receive wait; a faulted or canceled helper task—for example, one faulted by a socket-send exception—captures an exception in `helperTaskException` with `ExceptionDispatchInfo` only when no earlier capture has been retained, sets the separate helper-task-failure state, and also cancels the receive wait. The receive loop checks both failure states before waiting, after receiving, and immediately before starting a reuse helper, which bounds the receive/failure race: already-started helpers remain tracked, while work observed after failure is not started. The task loop continues removing and observing every active helper. Only after the tracked set drains does it rethrow the first captured helper-task exception. Both failure paths suppress T2I's advisory close-intention and final current-status work; the producer-`false` path returns normally for dispatcher-owned closure, while a helper-task exception reaches the dispatcher after the drain. On success, batch offsets, follow-on acceptance, the two-second reuse window, advisory close intention, final status, and dispatcher closure remain in their existing flow.

Static review used the following bounded commands and results:

- `git log --oneline --reverse 5511d3a6^..84df8dfe` returned exactly the five source commits above.
- `git diff --name-status 5511d3a6^ 84df8dfe` and `git diff --stat 5511d3a6^ 84df8dfe` returned exactly the five source files above, with 68 insertions and 16 deletions.
- `rg -n "RunWebsocketHandlerCallWS" src` with user-data, build-output, and protected frontend exclusions returned exactly one definition and six maintained calls; the separately named direct helper was unchanged.
- Fixed-range diffs and current-source inspection confirmed post-drain log/error/result ordering, uninterpreted readable errors, canceled-task `true`, escaping send exceptions, every wrapper branch, both T2I result/fault paths, the receive race checks, active-helper drain, drain-before-rethrow ordering, and successful-flow retention.
- `git diff --name-only 5511d3a6^ 84df8dfe -- 'src/Pages/**' 'src/wwwroot/**'` returned no browser changes.
- `git diff --check 5511d3a6^ 84df8dfe` returned no whitespace errors.

The public helper return type is source-compatible for maintained source calls in the forms used here, but arbitrary precompiled binary ABI compatibility is not guaranteed. The managed source-extension build path in `ExtensionsManager.BuildExtension` derives its cache target from both extension source identity and the current core assembly identity, including `ManifestModule.ModuleVersionId`, so a new core MVID prevents reuse of a cached extension assembly built for the prior core. This is evidence for recompilation of that maintained source-extension path, not a guarantee for independently supplied precompiled binaries.

No agent build, test, launcher, server, socket, browser, backend, Comfy, fault injection, performance measurement, or platform runtime exercise was performed. Windows behavior, other platform behavior, runtime ordering, failure frequency, and performance remain unvalidated. The legacy Image Batch behavior in which a producer can enqueue a readable error and then complete normally, allowing the wrapper's existing success continuation, is unchanged and outside this unexpected-task-fault project.

## Maintainer Validation

**Result:** **Passed on Linux.** Maintainer Reaper176 explicitly confirmed the complete matrix on 2026-07-25.

The maintainer injected one unexpected producer fault after at least one progress frame in all six maintained flows: initial generation; socket-reuse generation while another generation was active; model selection; LoRA extraction; TensorRT creation; and Image Batch. For every faulted helper invocation, prior progress remained ordered, exactly one client frame contained `error_id: "internal_error"` and `error: "An internal error occurred"`, detailed exception context remained server-side, the owning wrapper emitted no contradictory final status, refresh-derived terminal result, success log, success frame, close intention, or final status, the dispatcher performed normal WebSocket closure, and no duplicate generic frame followed.

The T2I-specific coordination matrix also passed: no follow-on request was accepted after the failure became visible; work already started before that transition drained; a later successful producer did not reset the failure state; the failure path emitted neither `socket_intention: "close"` nor final current status; and the socket-reuse race did not abandon an already-tracked helper.

The preserved-behavior and security matrix passed: producer-enqueued readable errors remained unchanged without gaining a generic frame on non-fault completion; established producer cancellation did not become `internal_error`; socket-send failure and remote disconnect followed the existing dispatcher path without a duplicate generic error; premature remote disconnect retained its established server diagnostic; route names, permissions, and browser error/progress cleanup remained functional; and no client fault frame exposed exception type, message, stack, submitted values, paths, or backend-response details.

Every successful-flow category passed: model selection retained progress and final current status; TensorRT retained progress, artifact movement, model refresh, and its `"Complete!"` frame; LoRA retained progress, model refresh, output verification, success/error logs, and terminal frame; Image Batch retained progress, successful-completion log, and `{ "success": "complete" }`; initial generation retained progress/images, final status, and normal closure; and socket-reuse generation retained batch offsets, follow-on acceptance, concurrent active work, the two-second reuse window, `socket_intention: "close"`, final status, and normal closure.

Windows and other-platform runtime behavior and performance remain unvalidated. Arbitrary precompiled binary ABI compatibility remains unclaimed; the maintained source-extension path retains core-MVID cache invalidation. The legacy Image Batch readable-error-then-success behavior remains unchanged and outside this project. Agents performed no build, test, launcher, server, socket, browser, backend, Comfy, fault injection, performance measurement, platform runtime exercise, or other runtime validation.

## Rollback

Rollback must restore the helper return type, remove its generic producer-fault frame/result branch, restore the four non-T2I wrappers' unconditional post-await behavior, and restore T2I's `Task` tracking and normal close/final-status flow together.

A partial rollback is invalid: retaining wrapper failure branches without the Boolean result does not compile, while retaining the Boolean/failure frame without wrapper and T2I coordination restores contradictory success or reuse behavior. The rollback changes no route registration, producer signature, browser contract, or dispatcher close policy.
