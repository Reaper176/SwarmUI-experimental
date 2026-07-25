# Shared Download Replacement and Cancellation Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make shared downloads replace destinations with exact bytes and honor caller cancellation during initial and retry request acquisition without changing the public API or existing download pipeline.

**Architecture:** `Utilities.DownloadFile` remains the sole production owner. It will validate the initial response before opening the destination, open successful replacements with `FileMode.Create`, and pass the existing linked caller/global token through every request and response-stream acquisition while retaining the established chunk, retry, progress, hash, cleanup, and caller contracts.

**Tech Stack:** C# 12, .NET 8 `HttpClient`, `FileStream`, `CancellationTokenSource`, task-based chunk queues, SHA-256, existing SwarmUI logging and error types.

---

## Repository Constraints

- Work directly on `master`; do not create or use a worktree.
- User `Reaper176` is approved for agentic changes.
- Agents never build, launch, or run tests in this repository.
- Static searches, source tracing, staged diff inspection, and `git diff --check` are the permitted verification.
- Never inspect, edit, stage, or commit:
  - `src/Data/Settings.fds`
  - `src/Pages/Text2Image.cshtml`
  - `src/wwwroot/js/genpage/gentab/loras.js`
  - `src/wwwroot/js/genpage/main.js`
  - `Data.pre-restore-2026-07-19/`
- The approved design is commit `23c77542` and file `docs/superpowers/specs/2026-07-25-shared-download-replacement-cancellation-design.md`.

## File Structure

- Modify: `src/Utils/Utilities.cs`
  - Retains complete ownership of request acquisition, range continuation, chunk production, file writing, progress reporting, validation, and cleanup.
- Modify after static implementation review: `docs/superpowers/specs/2026-07-25-shared-download-replacement-cancellation-design.md`
  - Records the exact production range and static evidence as implemented awaiting maintainer validation.
- Modify after static implementation review: `docs/superpowers/audits/2026-07-21-maintainability-architecture-refresh.md`
  - Updates only Core F11/rank 9 disposition and advances the unchanged recommendation to rank 10.

No new production class or abstraction is warranted for this two-defect boundary.

### Task 1: Freeze the Caller and Contract Inventory

**Files:**
- Inspect: `src/Utils/Utilities.cs`
- Inspect: `src/Core/Installation.cs`
- Inspect: `src/Text2Image/CommonModels.cs`
- Inspect: `src/WebAPI/ModelsAPI.cs`
- Inspect: `src/BuiltinExtensions/ComfyUIBackend/WorkflowGenerator.cs`
- Inspect: `src/BuiltinExtensions/ComfyUIBackend/WorkflowGeneratorModelSupport.cs`

- [ ] **Step 1: Confirm the direct caller inventory**

Run:

```bash
rg -n 'Utilities\.DownloadFile\(' \
  src \
  --glob '*.cs' \
  --glob '!src/Extensions/**' \
  --glob '!src/bin/**' \
  --glob '!src/obj/**'
```

Expected: seven direct call lines:

- four in `src/Core/Installation.cs`;
- one in `src/Text2Image/CommonModels.cs`;
- one in `src/WebAPI/ModelsAPI.cs`; and
- one in `src/BuiltinExtensions/ComfyUIBackend/WorkflowGenerator.cs`.

- [ ] **Step 2: Confirm the indirect common-model flows**

Run:

```bash
rg -n '\.DownloadNow\(' \
  src/Core/Installation.cs \
  src/BuiltinExtensions/ComfyUIBackend/WorkflowGeneratorModelSupport.cs
```

Expected: three maintained indirect calls—one installation flow and two Comfy model-support flows.

- [ ] **Step 3: Record each compatibility precondition**

Trace and record:

```bash
rg -n -C 18 'DownloadFile\(|DownloadNow\(|download\.tmp|tmpPath|RefreshAllModelSets|File\.Move' \
  src/Core/Installation.cs \
  src/Text2Image/CommonModels.cs \
  src/WebAPI/ModelsAPI.cs \
  src/BuiltinExtensions/ComfyUIBackend/WorkflowGenerator.cs \
  src/BuiltinExtensions/ComfyUIBackend/WorkflowGeneratorModelSupport.cs
```

Expected classification:

- model UI and `WorkflowGenerator.DownloadModel` pre-delete temporary targets;
- `ModelInfo.DownloadNow` refuses a pre-existing final destination;
- installation uses fixed destinations and progress reporting;
- caller-owned moves, cleanup, and model refreshes remain outside the helper; and
- only model UI supplies a maintained caller cancellation source.

- [ ] **Step 4: Trace the current helper phases**

Inspect `Utilities.DownloadFile` from request construction through `Task.WhenAll`. Record the exact:

- initial `OK` requirement;
- retry `PartialContent` requirement;
- four-retry limit;
- range values;
- chunk thresholds;
- five-minute stalled-read policy;
- progress cadence and tuple values;
- length/hash validation;
- cleanup branches; and
- global/caller cancellation use.

Make no source changes and no commit for this task.

### Task 2: Establish Exact Replacement Semantics

**Files:**
- Modify: `src/Utils/Utilities.cs` in `DownloadFile`

- [ ] **Step 1: Move destination creation after initial response acceptance**

Replace the opening portion of `DownloadFile`:

```csharp
Directory.CreateDirectory(Path.GetDirectoryName(filepath));
using FileStream writer = File.OpenWrite(filepath);
HttpRequestMessage request = new(HttpMethod.Get, url);
if (headers is not null)
{
    foreach ((string key, string value) in headers)
    {
        request.Headers.Add(key, value);
    }
}
HttpResponseMessage response = await UtilWebClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, Program.GlobalProgramCancel);
long length = response.Content.Headers.ContentLength ?? 0;
ConcurrentQueue<byte[]> chunks = new();
ConcurrentQueue<(long, long, long, bool)> progUpdates = new();
if (response.StatusCode != HttpStatusCode.OK)
{
    throw new SwarmReadableErrorException($"Failed to download {altUrl}: got response code {(int)response.StatusCode} {response.StatusCode}");
}
using Stream dlStream = await response.Content.ReadAsStreamAsync();
```

with:

```csharp
Directory.CreateDirectory(Path.GetDirectoryName(filepath));
using HttpRequestMessage request = new(HttpMethod.Get, url);
if (headers is not null)
{
    foreach ((string key, string value) in headers)
    {
        request.Headers.Add(key, value);
    }
}
using HttpResponseMessage response = await UtilWebClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, Program.GlobalProgramCancel);
if (response.StatusCode != HttpStatusCode.OK)
{
    throw new SwarmReadableErrorException($"Failed to download {altUrl}: got response code {(int)response.StatusCode} {response.StatusCode}");
}
long length = response.Content.Headers.ContentLength ?? 0;
ConcurrentQueue<byte[]> chunks = new();
ConcurrentQueue<(long, long, long, bool)> progUpdates = new();
using Stream dlStream = await response.Content.ReadAsStreamAsync();
using FileStream writer = new(filepath, FileMode.Create, FileAccess.Write, FileShare.None);
```

This ordering must ensure a failed status or pre-stream acquisition failure occurs before destination replacement.

- [ ] **Step 2: Give retry requests explicit ownership**

Inside the range-retry catch, replace reassignment of the outer request:

```csharp
request = new(HttpMethod.Get, url);
if (headers is not null)
{
    foreach ((string key, string value) in headers)
    {
        request.Headers.Add(key, value);
    }
}
request.Headers.Range = new(totalRead, length);
workingResponse = await UtilWebClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, Program.GlobalProgramCancel);
```

with:

```csharp
using HttpRequestMessage retryRequest = new(HttpMethod.Get, url);
if (headers is not null)
{
    foreach ((string key, string value) in headers)
    {
        retryRequest.Headers.Add(key, value);
    }
}
retryRequest.Headers.Range = new(totalRead, length);
workingResponse = await UtilWebClient.SendAsync(retryRequest, HttpCompletionOption.ResponseHeadersRead, Program.GlobalProgramCancel);
```

Do not change the range values, retry count, or response-status check.

- [ ] **Step 3: Statically trace resource and cleanup ownership**

Confirm:

- the initial request is disposed after response acquisition;
- the initial response and stream remain alive through all three tasks;
- `loadData` may dispose the active response/stream and the outer `using` declarations tolerate repeated disposal;
- every retry request is disposed after response acquisition;
- retry responses/streams remain owned by `workingResponse`/`workingStream`;
- `writer` exists before `removeFile` can run;
- `removeFile` still disposes the writer before deleting; and
- successful completion leaves `writer` to its outer `using`.

- [ ] **Step 4: Verify the replacement-only diff**

Run:

```bash
rg -n -C 12 'FileMode\.Create|File\.OpenWrite|HttpRequestMessage request|HttpRequestMessage retryRequest|StatusCode != HttpStatusCode\.OK' \
  src/Utils/Utilities.cs
git diff --check -- src/Utils/Utilities.cs
git diff -- src/Utils/Utilities.cs
```

Expected:

- no `File.OpenWrite(filepath)` remains in `DownloadFile`;
- the destination opens after the initial `OK` check and stream acquisition;
- it uses `FileMode.Create`, `FileAccess.Write`, and `FileShare.None`;
- request ownership changes are the only adjacent refactor; and
- cancellation tokens are not changed in this commit.

- [ ] **Step 5: Commit exact replacement semantics**

```bash
git add src/Utils/Utilities.cs
git diff --cached --check
git commit -m "fix: truncate shared download replacements"
```

### Task 3: Propagate Linked Cancellation Through Request Acquisition

**Files:**
- Modify: `src/Utils/Utilities.cs` in `DownloadFile`

- [ ] **Step 1: Use the linked token for the initial request**

Change:

```csharp
using HttpResponseMessage response = await UtilWebClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, Program.GlobalProgramCancel);
```

to:

```csharp
using HttpResponseMessage response = await UtilWebClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, combinedCancel.Token);
```

- [ ] **Step 2: Use the linked token for initial stream acquisition**

Change:

```csharp
using Stream dlStream = await response.Content.ReadAsStreamAsync();
```

to:

```csharp
using Stream dlStream = await response.Content.ReadAsStreamAsync(combinedCancel.Token);
```

- [ ] **Step 3: Use the linked token for retry request acquisition**

Change:

```csharp
workingResponse = await UtilWebClient.SendAsync(retryRequest, HttpCompletionOption.ResponseHeadersRead, Program.GlobalProgramCancel);
```

to:

```csharp
workingResponse = await UtilWebClient.SendAsync(retryRequest, HttpCompletionOption.ResponseHeadersRead, combinedCancel.Token);
```

- [ ] **Step 4: Use the linked token for retry stream acquisition**

Change:

```csharp
workingStream = await workingResponse.Content.ReadAsStreamAsync();
```

to:

```csharp
workingStream = await workingResponse.Content.ReadAsStreamAsync(combinedCancel.Token);
```

- [ ] **Step 5: Trace all cancellation-sensitive phases**

Run:

```bash
rg -n -C 5 'combinedCancel|SendAsync|ReadAsStreamAsync|ReadAsync|WriteAsync|Task\.Delay' \
  src/Utils/Utilities.cs
```

Confirm:

- initial and retry sends use `combinedCancel.Token`;
- initial and retry stream acquisition use `combinedCancel.Token`;
- network reads and file writes still use `combinedCancel.Token`;
- chunk/progress polling delays still use `combinedCancel.Token`;
- the independent stalled-read watchdog retains its local cleanup token and remains responsive because cancellation completes the linked network read;
- `Program.GlobalProgramCancel` remains one input to `combinedCancel`;
- the caller source remains the other input; and
- the helper never disposes a caller-owned source.

- [ ] **Step 6: Trace cancellation outcomes**

By static control flow, confirm:

1. cancellation during the initial send or initial stream acquisition occurs before `FileMode.Create`;
2. cancellation during a retry unwinds `loadData`, enqueues the terminal chunk, and reaches existing incomplete-download cleanup;
3. cancellation during a read reaches the same producer failure/terminal path;
4. cancellation during write or queue polling reaches the writer/progress failure path;
5. `Task.WhenAll` propagates phase failure; and
6. no caller catch/move/refresh behavior changes.

- [ ] **Step 7: Verify and commit linked cancellation**

Run:

```bash
git diff --check -- src/Utils/Utilities.cs
git diff -- src/Utils/Utilities.cs
```

Expected: exactly four token substitutions in `DownloadFile`.

Commit:

```bash
git add src/Utils/Utilities.cs
git diff --cached --check
git commit -m "fix: cancel shared download requests"
```

### Task 4: Perform Whole-Range Static Conformance Review

**Files:**
- Inspect: `src/Utils/Utilities.cs`
- Inspect: all Task 1 caller files

- [ ] **Step 1: Inspect the production range**

Set the approved design as the fixed boundary:

```bash
git diff --name-only 23c77542..HEAD
git diff --check 23c77542..HEAD
git log --oneline 23c77542..HEAD
```

Expected: the fixed-boundary range contains this approved plan document plus `src/Utils/Utilities.cs`; the production subset is exactly the two planned source commits and one source file.

- [ ] **Step 2: Repeat the caller inventory**

Run the Task 1 direct and indirect searches again. Expected counts and classifications remain seven direct and three indirect flows with no caller edits.

- [ ] **Step 3: Verify exact replacement ordering**

Run:

```bash
rg -n -C 18 'SendAsync\(request|StatusCode != HttpStatusCode\.OK|ReadAsStreamAsync|FileMode\.Create' \
  src/Utils/Utilities.cs
```

Confirm the maintained sequence is:

1. initial send;
2. `OK` validation;
3. stream acquisition;
4. truncating destination creation; and
5. task startup.

- [ ] **Step 4: Verify token propagation and unchanged policy**

Run:

```bash
rg -n -C 8 'combinedCancel|tryCount < 4|Headers\.Range|PartialContent|progressUpdate|TransformFinalBlock|verifyHash|removeFile' \
  src/Utils/Utilities.cs
```

Confirm:

- all request/stream/read/write/queue-poll phases use or are unblocked by the linked token;
- retry limit, range values, `PartialContent`, progress, length, and hash behavior are unchanged;
- incomplete/invalid destinations retain established deletion paths; and
- no internal temporary/atomic-replace redesign was introduced.

- [ ] **Step 5: Review caller compatibility**

Trace all callers and prove:

- no signature or argument changes;
- installation destinations and progress callbacks are unchanged;
- `ModelInfo.DownloadNow` still refuses an existing final file;
- model UI still owns cancellation, temporary cleanup, move, and refresh;
- workflow generation still owns temporary cleanup and move;
- both Comfy model-support paths still call `RefreshAllModelSets`; and
- no API payload or user-visible success contract changes.

- [ ] **Step 6: Run final static hygiene checks**

```bash
git diff --check 23c77542..HEAD
git status --short
```

Expected: no whitespace errors; only protected maintainer files and the protected backup remain outside committed work.

### Task 5: Record Static Implementation

**Files:**
- Modify: `docs/superpowers/specs/2026-07-25-shared-download-replacement-cancellation-design.md`
- Modify: `docs/superpowers/audits/2026-07-21-maintainability-architecture-refresh.md`

- [ ] **Step 1: Update the design status**

Change:

```markdown
**Status:** Approved
```

to:

```markdown
**Status:** Implemented; awaiting maintainer validation
```

- [ ] **Step 2: Add an implementation record**

Before `## Maintainer Validation`, add:

- the exact two-commit production range and commit list;
- the exact changed source-file list;
- the seven-direct/three-indirect caller inventory;
- replacement ordering and token-propagation results;
- preserved retry/progress/hash/cleanup/caller contracts;
- static commands and results;
- protected-file exclusion; and
- an explicit statement that no agent ran a build, test, launcher, download, network fault injection, cancellation exercise, or benchmark.

- [ ] **Step 3: Update only Core F11/rank 9 audit dispositions**

In `docs/superpowers/audits/2026-07-21-maintainability-architecture-refresh.md`:

- retain the original F11 evidence in historical/past-tense form;
- mark Core F11 and rank 9 `Implemented; awaiting maintainer validation`;
- record the exact production range and static evidence;
- preserve all unvalidated runtime/platform caveats;
- move `Recommended Next Project` to rank 10 without changing rank 10's scope; and
- do not change rank 8's awaiting-validation status.

- [ ] **Step 4: Verify and commit the record**

```bash
git add \
  docs/superpowers/specs/2026-07-25-shared-download-replacement-cancellation-design.md \
  docs/superpowers/audits/2026-07-21-maintainability-architecture-refresh.md
git diff --cached --check
git diff --cached
git commit -m "docs: record shared download lifecycle fixes"
```

### Task 6: Maintainer Live Validation and Closure

**Files:**
- Modify after explicit maintainer confirmation: `docs/superpowers/specs/2026-07-25-shared-download-replacement-cancellation-design.md`
- Modify after explicit maintainer confirmation: `docs/superpowers/audits/2026-07-21-maintainability-architecture-refresh.md`

- [ ] **Step 1: Present the exact maintainer matrix**

Ask maintainer Reaper176 to validate all 14 cases in the approved design:

1. shorter successful response over a longer destination;
2. caller cancellation before headers without destination mutation;
3. midstream caller cancellation with incomplete replacement deletion;
4. global shutdown cancellation;
5. successful range continuation;
6. failed/non-partial range continuation;
7. content-length mismatch;
8. SHA-256 mismatch;
9. fixed-path installer flows;
10. installation-selected common models;
11. model UI success/progress/cancel/cleanup/move/refresh;
12. `WorkflowGenerator.DownloadModel`;
13. both Comfy model-support common-model paths; and
14. ordinary successful download/progress behavior.

- [ ] **Step 2: Record only confirmed scope**

After explicit maintainer confirmation:

- change the design and audit status to `Implemented and maintainer-validated`;
- record the date, platform, and exact confirmed cases;
- retain failed, skipped, or unvalidated cases explicitly;
- make no Windows-runtime or performance claim unless separately confirmed; and
- do not reinterpret rank 8's separate pending validation.

- [ ] **Step 3: Commit validation closure**

```bash
git add \
  docs/superpowers/specs/2026-07-25-shared-download-replacement-cancellation-design.md \
  docs/superpowers/audits/2026-07-21-maintainability-architecture-refresh.md
git diff --cached --check
git commit -m "docs: validate shared download lifecycle fixes"
```

- [ ] **Step 4: Report final repository state**

```bash
git status --short --branch --untracked-files=no
git rev-list --left-right --count origin/master...master
git log --oneline -12
```

Report:

- final `master` HEAD;
- ahead/behind counts and unpushed state;
- protected maintainer files;
- exact production range;
- exact validated and unvalidated scope;
- rank 8's still-pending validation status unless separately completed; and
- the audit's recommended next project.

Do not push unless the maintainer explicitly requests it.
