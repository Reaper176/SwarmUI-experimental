# Streaming Producer Failure Transport Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Transport unexpected streaming-producer faults as one ordered generic client failure and stop route owners from emitting contradictory success or finalization behavior.

**Architecture:** `API.RunWebsocketHandlerCallWS<T>` remains the sole queue/drain transport owner but returns `Task<bool>`, logs detailed producer faults server-side, and appends one generic `internal_error` frame after all prior queued frames. Models, Comfy utilities, Image Batch, and T2I consume that result; T2I additionally coordinates its initial and socket-reuse tasks through a one-way failure state while retaining dispatcher-owned normal closure.

**Tech Stack:** C# 12, .NET 8 `Task`/`Task<bool>`, `ConcurrentQueue<JObject>`, `ConcurrentDictionary<Task<bool>, Task<bool>>`, `AsyncAutoResetEvent`, `System.Net.WebSockets`, Newtonsoft.Json, existing SwarmUI logging and JSON helpers.

---

## Repository Constraints

- Work directly on `master`; the user explicitly forbids a worktree.
- User `Reaper176` is approved for agentic changes.
- Agents never build, launch, run tests, open sockets, inject faults, or exercise a browser in this repository.
- Static source searches, control-flow tracing, exact diff inspection, staged scope review, and `git diff --check` are the permitted agent verification.
- The maintainer performs every runtime and fault-injection case in Task 5.
- Never inspect, edit, stage, or commit:
  - `src/Data/Settings.fds`
  - `src/Pages/Text2Image.cshtml`
  - `src/wwwroot/js/genpage/gentab/loras.js`
  - `src/wwwroot/js/genpage/main.js`
  - `Data.pre-restore-2026-07-19/`
- Follow repository C# conventions: explicit types rather than `var`, full braced blocks, `else` on its own line, and updated `///` documentation for changed public members.
- The fixed approved-design boundary is commit `47d32f4e` and file `docs/superpowers/specs/2026-07-25-streaming-producer-failure-transport-design.md`.

## File Map

- Modify: `src/WebAPI/API.cs:203-230`
  - Retains queue/drain ownership, changes the public helper result to `Task<bool>`, logs detailed producer faults, sends one generic post-drain error, and returns the producer-fault result.
- Modify: `src/WebAPI/ModelsAPI.cs:343-349`
  - Suppresses the final model status after a producer fault.
- Modify: `src/BuiltinExtensions/ComfyUIBackend/ComfyUIWebAPI.cs:428-475,594-629`
  - Makes TensorRT and LoRA wrappers consume the result; LoRA stops before refresh/output-derived terminal behavior after a producer fault.
- Modify: `src/BuiltinExtensions/ImageBatchTool/ImageBatchToolExtension.cs:64-68`
  - Suppresses the success log and success frame after a producer fault.
- Modify: `src/WebAPI/T2IAPI.cs:94-162`
  - Tracks `Task<bool>` for initial/reuse producers, stops reuse after the first producer fault, drains active work, propagates helper/socket faults, and suppresses failure-path close intention/final status.
- Modify after integrated static review: `docs/superpowers/specs/2026-07-25-streaming-producer-failure-transport-design.md`
  - Records the exact production commits/files and static evidence as implemented awaiting maintainer validation, then records only explicitly confirmed maintainer results.

No browser file, route registration, producer signature, direct-call helper, new transport abstraction, or test file is part of this plan.

### Task 1: Return and Transport the Shared Producer-Fault Result

**Files:**
- Modify: `src/WebAPI/API.cs:203-230`

- [ ] **Step 1: Reconfirm the helper boundary and exact caller count**

Run:

```bash
rg -n 'RunWebsocketHandlerCallWS' \
  src \
  --glob '*.cs' \
  --glob '!src/Extensions/**' \
  --glob '!src/bin/**' \
  --glob '!src/obj/**'
```

Expected: seven matches total—one definition and exactly six calls:

- two in `src/WebAPI/T2IAPI.cs`;
- one in `src/WebAPI/ModelsAPI.cs`;
- two in `src/BuiltinExtensions/ComfyUIBackend/ComfyUIWebAPI.cs`; and
- one in `src/BuiltinExtensions/ImageBatchTool/ImageBatchToolExtension.cs`.

- [ ] **Step 2: Replace the helper with the Boolean result contract**

Replace the current helper:

```csharp
/// <summary>Helper to run simple websocket-multiresult action API calls.</summary>
public static async Task RunWebsocketHandlerCallWS<T>(Func<Session, T, Action<JObject>, bool, Task> handler, Session session, T val, WebSocket socket)
{
    ConcurrentQueue<JObject> outputs = new();
    AsyncAutoResetEvent signal = new(false);
    void takeOutput(JObject obj)
    {
        if (obj is not null)
        {
            outputs.Enqueue(obj);
        }
        signal.Set();
    }
    Task t = handler(session, val, takeOutput, true);
    Task _ = t.ContinueWith((t) => signal.Set());
    while (!t.IsCompleted || outputs.Any())
    {
        while (outputs.TryDequeue(out JObject output))
        {
            await socket.SendJson(output, WebsocketTimeout);
        }
        await signal.WaitAsync(TimeSpan.FromSeconds(2));
    }
    if (t.IsFaulted)
    {
        Logs.Error($"Error in websocket handler: {t.Exception.ReadableString()}");
    }
}
```

with:

```csharp
/// <summary>Runs a simple websocket-multiresult action API call and returns whether its producer completed without faulting.</summary>
public static async Task<bool> RunWebsocketHandlerCallWS<T>(Func<Session, T, Action<JObject>, bool, Task> handler, Session session, T val, WebSocket socket)
{
    ConcurrentQueue<JObject> outputs = new();
    AsyncAutoResetEvent signal = new(false);
    void takeOutput(JObject obj)
    {
        if (obj is not null)
        {
            outputs.Enqueue(obj);
        }
        signal.Set();
    }
    Task t = handler(session, val, takeOutput, true);
    Task _ = t.ContinueWith((t) => signal.Set());
    while (!t.IsCompleted || outputs.Any())
    {
        while (outputs.TryDequeue(out JObject output))
        {
            await socket.SendJson(output, WebsocketTimeout);
        }
        await signal.WaitAsync(TimeSpan.FromSeconds(2));
    }
    if (t.IsFaulted)
    {
        Logs.Error($"Error in websocket handler: {t.Exception.ReadableString()}");
        await socket.SendJson(Utilities.ErrorObj("An internal error occurred", "internal_error"), WebsocketTimeout);
        return false;
    }
    return true;
}
```

Do not inspect queued JSON for `"error"`. A producer-enqueued readable error followed by non-faulted completion still returns `true`. A canceled producer task remains non-faulted for this result, receives no new generic frame, and returns `true`.

- [ ] **Step 3: Statically trace ordering and security**

Run:

```bash
rg -n -C 16 'RunWebsocketHandlerCallWS|outputs\.TryDequeue|t\.IsFaulted|internal_error|return false|return true' \
  src/WebAPI/API.cs
```

Confirm:

1. the producer callback still enqueues non-null frames and signals;
2. every queued frame drains before the `IsFaulted` branch;
3. the detailed `ReadableString()` appears only in `Logs.Error`;
4. the client receives exactly `Utilities.ErrorObj("An internal error occurred", "internal_error")`;
5. the generic send occurs exactly once per faulted helper invocation;
6. `false` is returned only for `Task.IsFaulted`;
7. normal and canceled non-faulted producer tasks return `true`; and
8. `socket.SendJson` remains outside a new catch, so send/timeout/remote-disconnect exceptions still escape.

- [ ] **Step 4: Verify the helper-only diff**

Run:

```bash
git diff --check -- src/WebAPI/API.cs
git diff -- src/WebAPI/API.cs
```

Expected: only the XML summary, return type, generic fault send, and `false`/`true` returns change. `RunWebsocketHandlerCallDirect<T>`, `HandleAsyncRequest`, `WebsocketTimeout`, and all route registrations remain unchanged.

- [ ] **Step 5: Commit the transport result**

```bash
git add src/WebAPI/API.cs
git diff --cached --check
git diff --cached --name-only
git commit -m "fix: transport streaming producer failures"
```

Expected staged file: only `src/WebAPI/API.cs`.

### Task 2: Stop Non-T2I Wrappers Before Contradictory Work

**Files:**
- Modify: `src/WebAPI/ModelsAPI.cs:343-349`
- Modify: `src/BuiltinExtensions/ComfyUIBackend/ComfyUIWebAPI.cs:428-475,594-629`
- Modify: `src/BuiltinExtensions/ImageBatchTool/ImageBatchToolExtension.cs:64-68`

- [ ] **Step 1: Branch model selection before final status**

Replace:

```csharp
await API.RunWebsocketHandlerCallWS(SelectModelInternal, session, (model, (string)null), socket);
await socket.SendJson(BasicAPIFeatures.GetCurrentStatusRaw(session), API.WebsocketTimeout);
return null;
```

with:

```csharp
bool producerSucceeded = await API.RunWebsocketHandlerCallWS(SelectModelInternal, session, (model, (string)null), socket);
if (!producerSucceeded)
{
    return null;
}
await socket.SendJson(BasicAPIFeatures.GetCurrentStatusRaw(session), API.WebsocketTimeout);
return null;
```

- [ ] **Step 2: Branch TensorRT on the helper result**

Change the TensorRT helper assignment and append the explicit result branch:

```csharp
long ticks = Environment.TickCount64;
bool producerSucceeded = await API.RunWebsocketHandlerCallWS<object>(async (s, t, a, b) =>
{
    await backend.AwaitJobLive(workflow.ToString(), "0", data =>
    {
        if (data is JObject jData && jData.ContainsKey("overall_percent"))
        {
            long newTicks = Environment.TickCount64;
            if (newTicks - ticks > 500)
            {
                ticks = newTicks;
                a(new() { ["status"] = $"Running, monitor Server logs for precise progress...\nOverall progress estimate: {jData["overall_percent"]}%" });
            }
        }
    }, new(null), Program.GlobalProgramCancel);
    a(new() { ["status"] = "Process completed, moving engine..." });
    string directory = $"{backend.ComfyPathBase}/output/swarmtemptrt/{prefix}/";
    if (!Directory.Exists(directory))
    {
        a(new() { ["error"] = "Process completed but TensorRT model did not save. Something went wrong?" });
        return;
    }
    string file = Directory.EnumerateFiles(directory, "*.engine").FirstOrDefault();
    if (!File.Exists(file))
    {
        a(new() { ["error"] = "Process completed but TensorRT model did not save. Something went wrong?" });
        return;
    }
    string outPathRaw = $"{Program.ServerSettings.Paths.ActualModelRoot}/tensorrt/{modelData.Name}_TensorRT";
    string outPath = outPathRaw;
    int id = 0;
    while (File.Exists($"{outPath}.engine"))
    {
        id++;
        outPath = $"{outPathRaw}_{id}";
    }
    Directory.CreateDirectory(Path.GetDirectoryName(outPath));
    JObject metadata = modelData.ToNetObject();
    metadata["architecture"] += "/tensorrt";
    metadata["title"] = $"{modelData.Title ?? modelData.Name} (TensorRT)";
    File.WriteAllText($"{outPath}.json", metadata.ToString());
    File.Copy(file, $"{outPath}.engine", true);
    File.Delete(file);
    Directory.Delete(directory, true);
    Program.RefreshAllModelSets();
    a(new() { ["status"] = "Complete!", ["complete"] = true });
}, session, null, ws);
if (!producerSucceeded)
{
    return null;
}
return null;
```

The explicit branch is intentionally present even though the wrapper has no additional success work after the helper today. TensorRT's refresh and completion frame remain inside the producer and are not duplicated or reordered.

- [ ] **Step 3: Branch LoRA before refresh and terminal derivation**

Replace:

```csharp
await API.RunWebsocketHandlerCallWS<object>(async (s, t, a, b) =>
{
    await backend.AwaitJobLive(workflow.ToString(), "0", data =>
    {
        if (data is JObject jData && jData.ContainsKey("overall_percent"))
        {
            long newTicks = Environment.TickCount64;
            if (newTicks - ticks > 500)
            {
                ticks = newTicks;
                a(jData);
            }
        }
    }, new(null), Program.GlobalProgramCancel);
}, session, null, ws);
Program.RefreshModelSet("LoRA");
```

with:

```csharp
bool producerSucceeded = await API.RunWebsocketHandlerCallWS<object>(async (s, t, a, b) =>
{
    await backend.AwaitJobLive(workflow.ToString(), "0", data =>
    {
        if (data is JObject jData && jData.ContainsKey("overall_percent"))
        {
            long newTicks = Environment.TickCount64;
            if (newTicks - ticks > 500)
            {
                ticks = newTicks;
                a(jData);
            }
        }
    }, new(null), Program.GlobalProgramCancel);
}, session, null, ws);
if (!producerSucceeded)
{
    return null;
}
Program.RefreshModelSet("LoRA");
```

Leave the existing output inspection, success log/frame, and missing-output error log/frame byte-for-byte unchanged after `Program.RefreshModelSet("LoRA")`.

- [ ] **Step 4: Branch Image Batch before success**

Replace:

```csharp
await API.RunWebsocketHandlerCallWS(GenBatchRun_Internal, session, (rawInput, input_folder, output_folder, init_image, revision, controlnet, imageFiles, resMode, append_filename_to_prompt), socket);
Logs.Info("Image Batcher completed successfully");
await socket.SendJson(new JObject() { ["success"] = "complete" }, API.WebsocketTimeout);
return null;
```

with:

```csharp
bool producerSucceeded = await API.RunWebsocketHandlerCallWS(GenBatchRun_Internal, session, (rawInput, input_folder, output_folder, init_image, revision, controlnet, imageFiles, resMode, append_filename_to_prompt), socket);
if (!producerSucceeded)
{
    return null;
}
Logs.Info("Image Batcher completed successfully");
await socket.SendJson(new JObject() { ["success"] = "complete" }, API.WebsocketTimeout);
return null;
```

- [ ] **Step 5: Verify every non-T2I post-await branch**

Run:

```bash
rg -n -C 8 'producerSucceeded|RunWebsocketHandlerCallWS|GetCurrentStatusRaw|RefreshModelSet|RefreshAllModelSets|completed successfully|\\[\"success\"\\]' \
  src/WebAPI/ModelsAPI.cs \
  src/BuiltinExtensions/ComfyUIBackend/ComfyUIWebAPI.cs \
  src/BuiltinExtensions/ImageBatchTool/ImageBatchToolExtension.cs
git diff --check -- \
  src/WebAPI/ModelsAPI.cs \
  src/BuiltinExtensions/ComfyUIBackend/ComfyUIWebAPI.cs \
  src/BuiltinExtensions/ImageBatchTool/ImageBatchToolExtension.cs
git diff -- \
  src/WebAPI/ModelsAPI.cs \
  src/BuiltinExtensions/ComfyUIBackend/ComfyUIWebAPI.cs \
  src/BuiltinExtensions/ImageBatchTool/ImageBatchToolExtension.cs
```

Confirm:

- Models returns before final status on `false`;
- TensorRT consumes the result without moving or duplicating its successful refresh/completion work;
- LoRA returns before refresh, output inspection, terminal logs, and terminal frames on `false`;
- Image Batch returns before success log/frame on `false`;
- all `true` paths are otherwise unchanged; and
- route names, signatures, registrations, permissions, and producer lambdas are unchanged.

- [ ] **Step 6: Commit the wrapper branches**

```bash
git add \
  src/WebAPI/ModelsAPI.cs \
  src/BuiltinExtensions/ComfyUIBackend/ComfyUIWebAPI.cs \
  src/BuiltinExtensions/ImageBatchTool/ImageBatchToolExtension.cs
git diff --cached --check
git diff --cached --name-only
git commit -m "fix: stop streaming wrappers after producer faults"
```

Expected staged files: exactly the three listed files.

### Task 3: Coordinate T2I Initial and Socket-Reuse Failure

**Files:**
- Modify: `src/WebAPI/T2IAPI.cs:94-162`

- [ ] **Step 1: Replace the T2I task/result coordination block**

Replace the block from the route-local Boolean declarations through the final status send with:

```csharp
using CancellationTokenSource cancelTok = new();
bool retain = false, ended = false, producerFailed = false;
using CancellationTokenSource linked = CancellationTokenSource.CreateLinkedTokenSource(Program.GlobalProgramCancel, cancelTok.Token);
SharedGenT2IData data = new();
ConcurrentDictionary<Task<bool>, Task<bool>> tasks = [];
static int guessBatchSize(JObject input)
{
    if (input.TryGetValue("batchsize", out JToken batch))
    {
        return batch.Value<int>();
    }
    return 1;
}
_ = Utilities.RunCheckedTask(async () =>
{
    try
    {
        int batchOffset = images * guessBatchSize(rawInput);
        while (!cancelTok.IsCancellationRequested && !Volatile.Read(ref producerFailed))
        {
            byte[] rec = await socket.ReceiveData(Program.ServerSettings.Network.MaxReceiveBytes, linked.Token);
            Volatile.Write(ref retain, true);
            if (socket.State != WebSocketState.Open
                || cancelTok.IsCancellationRequested
                || Volatile.Read(ref ended)
                || Volatile.Read(ref producerFailed))
            {
                return;
            }
            JObject newInput = SubmittedInputJson.ParseObject(StringConversionHelper.UTF8Encoding.GetString(rec));
            int newImages = newInput.Value<int>("images");
            if (Volatile.Read(ref producerFailed))
            {
                return;
            }
            Task<bool> handleMore = API.RunWebsocketHandlerCallWS(GenT2I_Internal, session, (newImages, newInput, data, batchOffset), socket);
            tasks.TryAdd(handleMore, handleMore);
            Volatile.Write(ref retain, false);
            batchOffset += newImages * guessBatchSize(newInput);
        }
    }
    catch (TaskCanceledException)
    {
        return;
    }
    finally
    {
        Volatile.Write(ref retain, false);
    }
});
Task<bool> handle = API.RunWebsocketHandlerCallWS(GenT2I_Internal, session, (images, rawInput, data, 0), socket);
tasks.TryAdd(handle, handle);
while (Volatile.Read(ref retain) || tasks.Any())
{
    if (tasks.Any())
    {
        await Task.WhenAny(tasks.Keys.ToList());
    }
    foreach (Task<bool> task in tasks.Keys.Where(task => task.IsCompleted).ToList())
    {
        if (!task.IsCompletedSuccessfully)
        {
            await task;
        }
        if (!task.Result)
        {
            Volatile.Write(ref producerFailed, true);
            cancelTok.Cancel();
        }
        tasks.TryRemove(task, out _);
    }
    if (!Volatile.Read(ref producerFailed) && tasks.IsEmpty())
    {
        await socket.SendJson(new JObject() { ["socket_intention"] = "close" }, API.WebsocketTimeout);
        await Task.Delay(TimeSpan.FromSeconds(2)); // Give 2 seconds to allow a new gen request before actually closing
        if (tasks.IsEmpty())
        {
            Volatile.Write(ref ended, true);
        }
    }
}
if (!Volatile.Read(ref producerFailed))
{
    await socket.SendJson(BasicAPIFeatures.GetCurrentStatusRaw(session), API.WebsocketTimeout);
}
return null;
```

Keep the method signature, `SharedGenT2IData`, `guessBatchSize`, batch-offset arithmetic, submitted-input parsing, and two-second reuse comment unchanged.

- [ ] **Step 2: Trace the one-way failure and receive-stop race**

Confirm by line-by-line static execution:

1. both helper calls create `Task<bool>`;
2. the task set accepts only `Task<bool>`;
3. only a successfully completed helper result is read;
4. the first `false` sets `producerFailed` and cancels the receive loop;
5. the receive loop checks `producerFailed` before receiving and again before task creation;
6. work created before the failure becomes visible remains in `tasks`;
7. no successful task resets `producerFailed`;
8. all already-tracked tasks complete and are removed before the outer loop exits; and
9. cancellation of `cancelTok` affects the receive loop's linked token, not already-running generation producers.

- [ ] **Step 3: Trace socket-send exception propagation**

The shared helper returns `false` for a producer fault only after it successfully sends the generic frame. It can itself fault if an ordinary queued-frame or generic-frame `SendJson` fails.

Confirm:

```csharp
if (!task.IsCompletedSuccessfully)
{
    await task;
}
```

propagates that helper exception to `API.HandleAsyncRequest` rather than translating it into `producerFailed`, synthesizing another generic frame, or silently removing it. `ConnectionClosedPrematurely` therefore retains the dispatcher's established remote-disconnect handling.

- [ ] **Step 4: Trace failure and success finalization**

Confirm:

- after any `false`, T2I accepts no later reuse request;
- already-active helpers still drain their queued frames;
- failure suppresses `socket_intention: "close"` and final current status;
- returning `null` leaves actual normal closure to `API.HandleAsyncRequest`;
- successful initial/reuse flows retain the close intention, two-second reuse window, final status, and dispatcher normal close; and
- independent helper queues retain their existing per-invocation FIFO order without a new cross-producer ordering claim.

- [ ] **Step 5: Verify the T2I-only diff**

Run:

```bash
rg -n -C 10 'producerFailed|ConcurrentDictionary<Task<bool>|Task<bool>|IsCompletedSuccessfully|cancelTok\\.Cancel|socket_intention|GetCurrentStatusRaw' \
  src/WebAPI/T2IAPI.cs
git diff --check -- src/WebAPI/T2IAPI.cs
git diff -- src/WebAPI/T2IAPI.cs
```

Expected: only `GenerateText2ImageWS` task/result/reuse coordination changes. No generation producer, HTTP route, parameter binding, image/status payload, or browser file changes.

- [ ] **Step 6: Commit T2I failure coordination**

```bash
git add src/WebAPI/T2IAPI.cs
git diff --cached --check
git diff --cached --name-only
git commit -m "fix: stop generation finalization after producer faults"
```

Expected staged file: only `src/WebAPI/T2IAPI.cs`.

### Task 4: Perform Integrated Static Review and Record Implementation

**Files:**
- Inspect: `src/WebAPI/API.cs`
- Inspect: `src/WebAPI/ModelsAPI.cs`
- Inspect: `src/BuiltinExtensions/ComfyUIBackend/ComfyUIWebAPI.cs`
- Inspect: `src/BuiltinExtensions/ImageBatchTool/ImageBatchToolExtension.cs`
- Inspect: `src/WebAPI/T2IAPI.cs`
- Inspect: `src/Core/ExtensionsManager.cs:128-140,208-218`
- Modify: `docs/superpowers/specs/2026-07-25-streaming-producer-failure-transport-design.md`

- [ ] **Step 1: Inspect the complete fixed-boundary range**

Run:

```bash
git log --oneline --reverse 47d32f4e..HEAD
git diff --name-only 47d32f4e..HEAD
git diff --name-only 47d32f4e..HEAD -- src
git diff --check 47d32f4e..HEAD
```

Expected:

- the full range contains this plan plus the three planned production commits;
- the production subset changes exactly:
  - `src/WebAPI/API.cs`
  - `src/WebAPI/ModelsAPI.cs`
  - `src/BuiltinExtensions/ComfyUIBackend/ComfyUIWebAPI.cs`
  - `src/BuiltinExtensions/ImageBatchTool/ImageBatchToolExtension.cs`
  - `src/WebAPI/T2IAPI.cs`; and
- no whitespace errors are reported.

- [ ] **Step 2: Repeat the exact helper inventory**

Run:

```bash
rg -n 'RunWebsocketHandlerCallWS' \
  src \
  --glob '*.cs' \
  --glob '!src/Extensions/**' \
  --glob '!src/bin/**' \
  --glob '!src/obj/**'
```

Expected: one `Task<bool>` definition and the same six maintained calls—two T2I, one Models, two Comfy, one Image Batch. No call was added, removed, renamed, or moved to the direct helper.

- [ ] **Step 3: Verify shared transport and security invariants**

Run:

```bash
rg -n -C 18 'RunWebsocketHandlerCallWS|outputs\\.TryDequeue|t\\.IsFaulted|ReadableString|Utilities\\.ErrorObj|return false|return true' \
  src/WebAPI/API.cs
```

Confirm:

- prior queued frames drain before the generic frame;
- detailed exception context remains server-side;
- the client identity/message is exactly `internal_error` / `An internal error occurred`;
- one generic frame is attempted per faulted helper invocation;
- expected producer-enqueued error frames remain uninterpreted;
- canceled producer tasks retain their non-fault treatment; and
- socket-send exceptions escape.

- [ ] **Step 4: Verify every post-await owner**

Run:

```bash
rg -n -C 10 'producerSucceeded|producerFailed|RunWebsocketHandlerCallWS|RefreshModelSet|RefreshAllModelSets|socket_intention|GetCurrentStatusRaw|completed successfully|\\[\"success\"\\]' \
  src/WebAPI/ModelsAPI.cs \
  src/BuiltinExtensions/ComfyUIBackend/ComfyUIWebAPI.cs \
  src/BuiltinExtensions/ImageBatchTool/ImageBatchToolExtension.cs \
  src/WebAPI/T2IAPI.cs
```

Trace all six calls and confirm the exact false/true behavior from Tasks 2 and 3. In particular, prove no Models final status, LoRA refresh/terminal derivation, Image Batch success log/frame, or T2I close-intention/final status can follow the owning producer's `false`.

- [ ] **Step 5: Verify dispatcher, route, direct-helper, browser, and ABI boundaries**

Run:

```bash
rg -n -C 8 'HandleAsyncRequest|ConnectionClosedPrematurely|CloseAsync|RunWebsocketHandlerCallDirect' \
  src/WebAPI/API.cs
rg -n 'RegisterAPICall\\(.*(GenerateText2ImageWS|SelectModelWS|DoLoraExtractionWS|DoTensorRTCreateWS|ImageBatchRun)' \
  src \
  --glob '*.cs'
rg -n -C 6 'coreIdentity|ModuleVersionId|targetName' src/Core/ExtensionsManager.cs
git diff --name-only 47d32f4e..HEAD -- src/wwwroot
```

Expected:

- dispatcher error/normal-close code and direct helper are unchanged;
- all five route registrations and public route signatures remain;
- no browser file changed;
- managed extension cache identity includes the current core module version ID;
- source callers that await/discard `Task<bool>` or store it as `Task` remain source-compatible; and
- the implementation record does not claim arbitrary precompiled-binary ABI compatibility for the public return-type change.

- [ ] **Step 6: Update the design status and add the implementation record**

In `docs/superpowers/specs/2026-07-25-streaming-producer-failure-transport-design.md`, change:

```markdown
**Status:** Approved; awaiting implementation
```

to:

```markdown
**Status:** Implemented; awaiting maintainer validation
```

Before `## Maintainer Validation`, add `## Implementation Record` containing:

- the exact three production commit hashes/messages emitted by Step 1;
- the exact five-source-file production list;
- the repeated one-definition/six-call inventory;
- post-drain detailed-log/generic-frame/Boolean ordering;
- non-T2I wrapper branch results;
- T2I one-way failure, receive stop, active drain, send-exception propagation, and finalization suppression;
- successful-flow, explicit-readable-error, cancellation, socket-send, dispatcher-close, route, direct-helper, and browser preservation;
- the source-compatible/precompiled-binary caveat and core-MVID cache evidence;
- every static command/result used in this task;
- protected-file exclusion; and
- an explicit statement that agents ran no build, test, launcher, socket, fault injection, browser exercise, runtime validation, or benchmark.

Do not mark any runtime, platform, or performance behavior validated.

- [ ] **Step 7: Review and commit the implementation record**

Run:

```bash
rg -n 'TBD|TODO|FIXME|PLACEHOLDER|Implemented|maintainer validation|agent|binary|ModuleVersionId' \
  docs/superpowers/specs/2026-07-25-streaming-producer-failure-transport-design.md
git diff --check -- docs/superpowers/specs/2026-07-25-streaming-producer-failure-transport-design.md
git diff -- docs/superpowers/specs/2026-07-25-streaming-producer-failure-transport-design.md
```

Expected: no placeholders, no runtime claim, and one design-document change.

Commit:

```bash
git add docs/superpowers/specs/2026-07-25-streaming-producer-failure-transport-design.md
git diff --cached --check
git diff --cached --name-only
git commit -m "docs: record streaming producer failure transport"
```

Expected staged file: only the design document.

### Task 5: Maintainer Fault Injection, Successful-Flow Validation, and Closure

**Files:**
- Modify only after explicit maintainer confirmation: `docs/superpowers/specs/2026-07-25-streaming-producer-failure-transport-design.md`

- [ ] **Step 1: Present the exact injected-fault matrix**

Maintainer Reaper176 injects one unexpected producer fault after at least one progress frame in:

1. initial generation;
2. socket-reuse generation while another generation is already active;
3. model selection;
4. LoRA extraction;
5. TensorRT creation; and
6. Image Batch.

For each faulted helper invocation, confirm:

- all prior progress arrives in order;
- exactly one client frame has `error_id: "internal_error"` and `error: "An internal error occurred"`;
- detailed exception context appears in server logs but not in the client frame;
- the owning wrapper emits no contradictory final status, refresh-derived terminal result, success log, success frame, close intention, or final status identified in the design;
- the dispatcher performs normal WebSocket closure; and
- no duplicate generic frame follows.

For T2I specifically, also confirm:

- no follow-on request is accepted after `producerFailed` becomes visible;
- work already started before that transition drains;
- the failure state cannot be reset by another successful producer;
- the failure path emits no `socket_intention: "close"` and no final current status; and
- the socket-reuse race does not abandon an already-tracked helper.

- [ ] **Step 2: Validate preserved error, cancellation, and transport behavior**

Maintainer Reaper176 confirms:

1. a producer-enqueued readable error is unchanged and does not gain the generic frame when the producer completes without fault;
2. established producer cancellation does not become `internal_error`;
3. a socket-send failure or remote disconnect is not translated into `false` and follows the existing dispatcher path;
4. premature remote disconnect retains its established server diagnostic;
5. route names, permissions, and browser error/progress cleanup remain functional; and
6. no client fault frame contains exception type, message, stack, submitted values, paths, or backend-response details.

- [ ] **Step 3: Validate every successful flow**

Maintainer Reaper176 confirms:

- model selection retains its progress and final current status;
- TensorRT retains progress, artifact move, model refresh, and `"Complete!"` frame;
- LoRA retains progress, model refresh, output verification, success/error logs, and terminal frame;
- Image Batch retains progress, successful-completion log, and `{ "success": "complete" }`;
- initial generation retains progress/images, final status, and normal close; and
- socket-reuse generation retains batch offsets, follow-on acceptance, concurrent active work, the two-second reuse window, `socket_intention: "close"`, final status, and normal close.

- [ ] **Step 4: Record only explicit maintainer results**

After the maintainer explicitly reports the matrix:

- change the design status to `Implemented and maintainer-validated` only if the required matrix passed;
- record maintainer name, date, platform, and every passed, failed, skipped, or unvalidated case;
- preserve the source-vs-precompiled-binary compatibility caveat;
- make no performance or unvalidated-platform claim; and
- retain the explicit statement that agents did not perform runtime validation.

- [ ] **Step 5: Commit validation closure**

If and only if the maintainer explicitly confirms the required matrix:

```bash
git add docs/superpowers/specs/2026-07-25-streaming-producer-failure-transport-design.md
git diff --cached --check
git diff --cached --name-only
git commit -m "docs: validate streaming producer failure transport"
```

Expected staged file: only the design document.

- [ ] **Step 6: Report final scope without pushing**

Run:

```bash
git status --short --branch --untracked-files=no
git log --oneline -10
git diff --check 47d32f4e..HEAD
```

Report:

- final `master` HEAD;
- the exact three-commit production range and five source files;
- static verification results;
- every confirmed and unvalidated maintainer case;
- the public helper source/precompiled-binary compatibility boundary; and
- unpushed state.

Do not push unless the maintainer explicitly requests it.
