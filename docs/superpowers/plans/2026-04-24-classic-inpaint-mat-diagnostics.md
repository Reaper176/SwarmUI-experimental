# Classic Inpaint MAT Diagnostics Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make `MAT` failures in the classic inpaint path produce actionable server logs and readable JSON errors without regressing the working `LaMa` backend.

**Architecture:** Keep the existing classic inpaint route and CLI invocation structure, but add deterministic diagnostics around command candidate selection, process execution, and result-file discovery. Preserve `LaMa` behavior and use the same route to surface `MAT`-specific failure information back to the editor.

**Tech Stack:** C# 12, ASP.NET API route handling, SwarmUI logging, external IOPaint CLI process execution

---

### Task 1: Map the Classic Inpaint Failure Surface

**Files:**
- Modify: `src/WebAPI/T2IAPI.cs`
- Test: manual runtime verification by reproducing `LaMa` success and `MAT` failure after rebuild

- [ ] **Step 1: Add a focused request-entry log for classic inpaint**

Add or keep a single entry log at the start of `ClassicInpaint()` that captures backend choice and request sizing so server logs show whether the route is reached:

```csharp
Logs.Info($"ClassicInpaint request received from user '{session.User?.UserID ?? "unknown"}' with backend '{backend}', feather={feather}, expandMask={expandMask}, imageBytes={imageData?.Length ?? 0}, maskBytes={maskData?.Length ?? 0}.");
```

- [ ] **Step 2: Rebuild and verify the route entry log appears**

Run your normal Swarm rebuild/restart flow.

Manual check:
- Trigger `LaMa` once from the image editor.
- Confirm `swarmui.log` contains `ClassicInpaint request received`.

Expected:
- `LaMa` still succeeds.
- The route entry is now visible in the active runtime log.

- [ ] **Step 3: Commit the route-entry observability change**

```bash
git add src/WebAPI/T2IAPI.cs
git commit -m "feat: log classic inpaint request entry"
```

### Task 2: Log Command Candidate Resolution and Execution Attempts

**Files:**
- Modify: `src/WebAPI/T2IAPI.cs`
- Test: manual runtime verification by reproducing `MAT` with fresh logs

- [ ] **Step 1: Add per-candidate execution logging before process launch**

Inside the nested candidate/args loop in `ClassicInpaint()`, add an info log just before `RunProcessCapture(...)`:

```csharp
Logs.Info($"ClassicInpaint attempting backend '{backend}' via candidate '{candidate}' with args '{actualArgs.JoinString(" ")}'.");
```

- [ ] **Step 2: Add success and non-success execution logs around process results**

After `RunProcessCapture(...)`, log exit code and whether the expected output file exists:

```csharp
Logs.Info($"ClassicInpaint candidate '{candidate}' finished with exitCode={exitCode}, outputExists={File.Exists(outPath)}.");
```

For failure cases, log the captured output text before appending to `errors`:

```csharp
Logs.Warning($"ClassicInpaint candidate '{candidate}' failed for backend '{backend}'. Output: {outputText}");
```

For thrown exceptions, log the exception message:

```csharp
Logs.Warning($"ClassicInpaint candidate '{candidate}' threw for backend '{backend}': {ex.Message}");
```

- [ ] **Step 3: Rebuild and verify the per-candidate logs on `MAT`**

Run your normal Swarm rebuild/restart flow.

Manual check:
- Trigger `MAT` once from the image editor.
- Inspect `swarmui.log`.

Expected:
- The log shows which executable candidate ran.
- The log shows the exact args, exit code, and whether the expected output image file was produced.

- [ ] **Step 4: Commit the candidate execution logging**

```bash
git add src/WebAPI/T2IAPI.cs
git commit -m "feat: log classic inpaint command attempts"
```

### Task 3: Normalize MAT Failures into Readable API Errors

**Files:**
- Modify: `src/WebAPI/T2IAPI.cs`
- Test: manual runtime verification from the image editor

- [ ] **Step 1: Expand the returned failure text to preserve the most useful diagnostics**

Keep the current `errors` list aggregation, but make sure the final returned error is clear that it includes command-attempt details:

```csharp
return new JObject()
{
    ["error"] = $"Classic Inpaint failed for backend '{backend}'. Make sure IOPaint is installed and that this backend is supported by the installed IOPaint version. Details: {errors.JoinString(" | ")}"
};
```

- [ ] **Step 2: Keep a top-level exception catch that returns readable JSON**

Keep the catch block and make it backend-aware:

```csharp
catch (Exception ex)
{
    Logs.Error($"ClassicInpaint crashed for backend '{backend}': {ex}");
    return new JObject() { ["error"] = $"Classic Inpaint crashed for backend '{backend}': {ex.Message}" };
}
```

- [ ] **Step 3: Rebuild and verify the UI now shows a readable `MAT` error**

Run your normal Swarm rebuild/restart flow.

Manual check:
- Trigger `MAT` once from the image editor.
- Observe the returned UI error text.

Expected:
- The UI no longer falls back to only a generic browser-side `ProgressEvent`.
- The error identifies either unsupported backend/version mismatch, process failure, or missing output generation.

- [ ] **Step 4: Commit the error normalization**

```bash
git add src/WebAPI/T2IAPI.cs
git commit -m "fix: return readable classic inpaint mat errors"
```

### Task 4: Evaluate Whether MAT Needs a Follow-Up Invocation Change

**Files:**
- Inspect: `src/WebAPI/T2IAPI.cs`
- Inspect: `swarmui.log`
- Test: manual runtime verification only

- [ ] **Step 1: Compare `LaMa` and `MAT` runtime behavior from logs**

Use the new logs to answer:

```text
1. Did both backends launch the same executable candidate?
2. Did MAT exit nonzero while LaMa succeeded?
3. Did MAT succeed but write its output to a different filename or location?
4. Did the installed IOPaint reject the model name or command shape?
```

- [ ] **Step 2: Record the exact failure mode before changing invocation logic**

Capture the concrete failure string from the returned UI error or `swarmui.log`, for example:

```text
- unsupported model name
- missing model download
- invalid CLI flag combination
- output file written to an unexpected path
- process start failure
```

Expected:
- The failure mode is concrete enough to justify the next code change.

- [ ] **Step 3: Commit only if a no-code diagnostics pass is complete**

If no further code changes are made in this task, do not create a new commit.

If you add a tiny follow-up diagnostic tweak while reviewing logs, commit it separately:

```bash
git add src/WebAPI/T2IAPI.cs
git commit -m "chore: refine classic inpaint mat diagnostics"
```
