# Generic Submitted-Input Diagnostics Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Put all confirmed generic API/T2I browser-submitted JSON and successful-generation diagnostics behind a value-safe boundary without changing valid transport, generation, media conversion, or client error contracts.

**Architecture:** Add a dedicated `SubmittedInputJson` owner that preserves `JObject.Parse` success behavior and replaces only submitted parser failures with a fresh fixed-message exception. Migrate the three generic JSON entry paths, keep valid-JSON media interpretation sanitization local to `T2IParamSet`, and reduce the successful-generation diagnostic to a parameter count. General JSON utilities, backend responses, persisted data, and Comfy parsing remain unchanged.

**Tech Stack:** C# 12, .NET 8, Newtonsoft.Json 13, ASP.NET Core HTTP/WebSocket transport, existing SwarmUI media/T2I/Grid flows.

**Execution context:** Work directly on `master` as requested by maintainer Reaper176. Do not create a worktree. Preserve the existing modified files and untracked `Data.pre-restore-2026-07-19/`; never inspect or stage that directory.

**Repository verification constraint:** `AGENTS.md` prohibits agents from running builds, automated tests, browsers, servers, backends, launchers, or live Grid generation. The maintainer supplies compilation and runtime validation. Agents use static source tracing, exact-count assertions, committed-range/whitespace checks, and independent reviews.

**Approved design:** `docs/superpowers/specs/2026-07-22-generic-submitted-input-diagnostics-design.md` at commit `43824aba`.

**Implementation outcome:** Production changes were implemented through `518134b7`; maintainer validation remains pending.

---

### Task 1: Add the submitted-input JSON boundary

**Files:**
- Create: `src/Utils/SubmittedInputJson.cs`
- Reference: `src/Utils/Utilities.cs:310-349`
- Reference: `src/Utils/Utilities.cs:607-618`

- [ ] **Step 1: Confirm the source boundary and clean target**

Run:

```bash
git status --short --branch --untracked-files=normal
test ! -e src/Utils/SubmittedInputJson.cs
sed -n '310,349p' src/Utils/Utilities.cs
sed -n '607,618p' src/Utils/Utilities.cs
```

Expected: the new file does not exist; the only working-tree changes are the maintainer-owned four tracked files and untracked backup directory; `ReceiveData`, `ReceiveJson`, and `ParseToJson` remain the current reference behavior.

- [ ] **Step 2: Create the narrow helper**

Use `apply_patch` to create `src/Utils/SubmittedInputJson.cs` with exactly:

```csharp
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.Net.WebSockets;
using System.Text;

namespace SwarmUI.Utils;

/// <summary>Parses browser/user-submitted JSON without retaining submitted content in parser failures.</summary>
public static class SubmittedInputJson
{
    /// <summary>Fixed parser failure text that contains no submitted content or native parser detail.</summary>
    private const string ParserFailureMessage = "JSON parsing failed (submitted content redacted).";

    /// <summary>Parses a submitted JSON object while replacing content-bearing JSON reader failures.</summary>
    public static JObject ParseObject(string input)
    {
        try
        {
            return JObject.Parse(input);
        }
        catch (JsonReaderException)
        {
            throw new JsonReaderException(ParserFailureMessage);
        }
    }

    /// <summary>Receives and parses one submitted WebSocket JSON object with the existing transport limits.</summary>
    public static async Task<JObject> ReceiveObject(WebSocket socket, TimeSpan maxDuration, long maxBytes)
    {
        byte[] data = await socket.ReceiveData(maxDuration, maxBytes);
        return ParseObject(Encoding.UTF8.GetString(data));
    }
}
```

- [ ] **Step 3: Statically verify the helper contract**

Run:

```bash
test "$(rg -o 'JObject\.Parse\(input\)' src/Utils/SubmittedInputJson.cs | wc -l)" = "1"
test "$(rg -o 'throw new JsonReaderException\(ParserFailureMessage\)' src/Utils/SubmittedInputJson.cs | wc -l)" = "1"
if rg -n 'CleanTrashTextForDebug|ReadableString|InnerException|input\.Replace|\{input\}' src/Utils/SubmittedInputJson.cs; then exit 1; fi
rg -n 'ReceiveData\(maxDuration, maxBytes\)|Encoding\.UTF8\.GetString|ParseObject' src/Utils/SubmittedInputJson.cs
git diff --check -- src/Utils/SubmittedInputJson.cs
```

Expected: successful parsing delegates to `JObject.Parse`; the replacement exception is fresh, fixed, and inner-free; receive behavior composes the existing transport owner; no submitted text is formatted.

- [ ] **Step 4: Commit the helper**

```bash
git add src/Utils/SubmittedInputJson.cs
git diff --cached --check
test "$(git diff --cached --name-only)" = "src/Utils/SubmittedInputJson.cs"
git commit -m "refactor: add submitted input JSON boundary"
```

---

### Task 2: Migrate initial API HTTP and WebSocket parsing

**Files:**
- Modify: `src/WebAPI/API.cs:63-88`
- Reference: `src/WebAPI/API.cs:189-196`

- [ ] **Step 1: Replace only the two initial parser calls**

Use `apply_patch` to replace:

```csharp
input = await socket.ReceiveJson(TimeSpan.FromMinutes(1), Program.ServerSettings.Network.MaxReceiveBytes);
```

with:

```csharp
input = await SubmittedInputJson.ReceiveObject(socket, TimeSpan.FromMinutes(1), Program.ServerSettings.Network.MaxReceiveBytes);
```

Replace:

```csharp
input = JObject.Parse(Encoding.UTF8.GetString(rawData));
```

with:

```csharp
input = SubmittedInputJson.ParseObject(Encoding.UTF8.GetString(rawData));
```

Do not change socket acceptance, HTTP validation, allocation, exact body reading, UTF-8 decoding, null handling, dispatch, the outer catch, response IDs/messages, or socket closure.

- [ ] **Step 2: Trace transport and error-contract preservation**

Run:

```bash
test "$(rg -o 'SubmittedInputJson\.(ReceiveObject|ParseObject)' src/WebAPI/API.cs | wc -l)" = "2"
if rg -n 'socket\.ReceiveJson\(TimeSpan\.FromMinutes\(1\)|JObject\.Parse\(Encoding\.UTF8\.GetString\(rawData\)\)' src/WebAPI/API.cs; then exit 1; fi
rg -n 'AcceptWebSocketAsync|HasJsonContentType|ContentLength|ReadExactlyAsync|SubmittedInputJson|internal_error|An internal error occurred|CloseAsync' src/WebAPI/API.cs
git diff --check -- src/WebAPI/API.cs
git diff -- src/WebAPI/API.cs
```

Expected: exactly two submitted-input calls; every surrounding transport, dispatch, catch, and client-response statement is unchanged.

- [ ] **Step 3: Commit the initial API migration**

```bash
git add src/WebAPI/API.cs
git diff --cached --check
test "$(git diff --cached --name-only)" = "src/WebAPI/API.cs"
git commit -m "fix: sanitize initial API JSON failures"
```

---

### Task 3: Migrate follow-up generation parsing and count-only diagnostics

**Files:**
- Modify: `src/WebAPI/T2IAPI.cs:108-130`
- Modify: `src/WebAPI/T2IAPI.cs:310-317`

- [ ] **Step 1: Replace the follow-up submitted parser**

Use `apply_patch` to replace:

```csharp
JObject newInput = StringConversionHelper.UTF8Encoding.GetString(rec).ParseToJson();
```

with:

```csharp
JObject newInput = SubmittedInputJson.ParseObject(StringConversionHelper.UTF8Encoding.GetString(rec));
```

Keep `ReceiveData`, `retain`, socket/cancellation/ended checks, `newImages`, task registration, reset, batch-offset calculation, and `finally` ordering unchanged.

- [ ] **Step 2: Replace the complete typed-input diagnostic**

Use `apply_patch` to replace:

```csharp
Logs.Verbose($"User {session.User.UserID} above image request had parameters: {user_input}");
```

with:

```csharp
Logs.Verbose($"User {session.User.UserID} above image request had parameter count: {user_input.InternalSet.ValuesInput.Count}");
```

Keep the existing `Logs.MinimumLevel <= Logs.LogLevel.Verbose` guard, session context, adjacent info log, and generation behavior unchanged.

- [ ] **Step 3: Trace follow-up control flow and diagnostic value access**

Run:

```bash
test "$(rg -o 'SubmittedInputJson\.ParseObject' src/WebAPI/T2IAPI.cs | wc -l)" = "1"
if rg -n 'GetString\(rec\)\.ParseToJson|above image request had parameters|\{user_input\}' src/WebAPI/T2IAPI.cs; then exit 1; fi
rg -n 'ReceiveData|Volatile\.Write\(ref retain|socket\.State|SubmittedInputJson|newImages|tasks\.TryAdd|batchOffset|InternalSet\.ValuesInput\.Count' src/WebAPI/T2IAPI.cs
git diff --check -- src/WebAPI/T2IAPI.cs
git diff -- src/WebAPI/T2IAPI.cs
```

Expected: the frame sequence is textually unchanged except for the parser owner; the success diagnostic reads only `Count` and retains its guard/log level.

- [ ] **Step 4: Commit the T2I migration**

```bash
git add src/WebAPI/T2IAPI.cs
git diff --cached --check
test "$(git diff --cached --name-only)" = "src/WebAPI/T2IAPI.cs"
git commit -m "fix: redact T2I request diagnostics"
```

---

### Task 4: Sanitize dynamic media object parsing and interpretation

**Files:**
- Modify: `src/Text2Image/T2IParamSet.cs:147-196`
- Reference: `src/BuiltinExtensions/GridGenerator/GridGenCore.cs:350-357`
- Reference: `src/BuiltinExtensions/GridGenerator/GridGeneratorExtension.cs:426-443`
- Reference: `src/BuiltinExtensions/GridGenerator/GridGeneratorExtension.cs:545-557`
- Reference: `src/BuiltinExtensions/GridGenerator/GridGeneratorExtension.cs:686-708`

- [ ] **Step 1: Add the source-owned post-parse boundary**

Use `apply_patch` to insert this local function immediately before `imageFor`:

```csharp
T mediaFromJson<T>(string input, Func<string, T> fromDataString) where T : MediaFile
{
    JObject parsed = SubmittedInputJson.ParseObject(input);
    try
    {
        T result = fromDataString(parsed["data"].ToString());
        result.SourceFilePath = parsed["filename"].ToString();
        return result;
    }
    catch (Exception)
    {
        throw new InvalidOperationException("Failed to process submitted media object (content redacted).");
    }
}
```

The parser call must remain outside the `try` so its fresh fixed `JsonReaderException` keeps the parser type. The catch must not retain the original exception as an inner exception or attach it to `Data`.

- [ ] **Step 2: Route all three JSON-looking media branches through the boundary**

Replace the JSON branch in `imageFor` with:

```csharp
if (canJson && val.StartsWithFast('{'))
{
    return mediaFromJson<ImageFile>(val, ImageFile.FromDataString);
}
```

Replace the JSON branch in `audioFor` with:

```csharp
if (val.StartsWithFast('{'))
{
    return mediaFromJson<AudioFile>(val, AudioFile.FromDataString);
}
```

Replace the JSON branch in `videoFor` with:

```csharp
if (val.StartsWithFast('{'))
{
    return mediaFromJson<VideoFile>(val, VideoFile.FromDataString);
}
```

Do not change data-URL handling, raw-base64 fallback media types, image-list behavior, the parameter switch, null handling, or publication to `ValuesInput`.

- [ ] **Step 3: Trace confidentiality and Grid compatibility**

Run:

```bash
test "$(rg -o 'SubmittedInputJson\.ParseObject' src/Text2Image/T2IParamSet.cs | wc -l)" = "1"
for media_type in ImageFile AudioFile VideoFile; do
    test "$(rg -F -o "return mediaFromJson<$media_type>(val, $media_type.FromDataString);" src/Text2Image/T2IParamSet.cs | wc -l)" = "1"
done
if rg -n 'JObject parsed = val\.ParseToJson|throw new InvalidOperationException\([^)]*,|catch \(Exception ex\)' src/Text2Image/T2IParamSet.cs; then exit 1; fi
rg -n 'Failed to process submitted media object|Task\.Run|throw mainRun\.Exception|ExToError|ReadableString|Failed due to internal error' src/Text2Image/T2IParamSet.cs src/BuiltinExtensions/GridGenerator/GridGeneratorExtension.cs
git diff --check -- src/Text2Image/T2IParamSet.cs
git diff -- src/Text2Image/T2IParamSet.cs
```

Expected: one private parser owner serves all three media types; post-parse failures are fixed and inner-free; Grid still classifies the new `InvalidOperationException` as an internal error and returns its existing generic response.

- [ ] **Step 4: Commit the media boundary**

```bash
git add src/Text2Image/T2IParamSet.cs
git diff --cached --check
test "$(git diff --cached --name-only)" = "src/Text2Image/T2IParamSet.cs"
git commit -m "fix: sanitize submitted media object failures"
```

---

### Task 5: Reconcile the design and architecture audit

**Files:**
- Modify: `docs/superpowers/specs/2026-07-22-generic-submitted-input-diagnostics-design.md`
- Modify: `docs/superpowers/audits/2026-07-21-maintainability-architecture-refresh.md`
- Review: `docs/superpowers/plans/2026-07-22-generic-submitted-input-diagnostics.md`

- [ ] **Step 1: Record implementation status in the design**

Use `apply_patch` to replace:

```markdown
**Status:** Approved design; implementation pending
```

with:

```markdown
**Status:** Implemented; awaiting maintainer validation
```

Do not claim build, runtime, or sentinel validation.

- [ ] **Step 2: Update only the S1 audit records**

Use `apply_patch` to update the S1 finding, its risk-register row, the roadmap preamble, and Recommended Next Project section with these exact implementation facts:

```markdown
**Implemented, awaiting maintainer validation.** `SubmittedInputJson` preserves successful `JObject.Parse` results while replacing submitted `JsonReaderException` failures with a fresh fixed-message exception with no inner exception. Initial HTTP and WebSocket parsing and follow-up generation-frame parsing use that boundary. `T2IParamSet` preserves valid image/audio/video object conversion while replacing later private interpretation failures with a fresh fixed generic exception. Successful-generation verbose logging reports only the total parameter count. General JSON utilities, backend-response/persisted-data parsers, Comfy boundaries, transport, routes, response contracts, and Grid's generic client error remain unchanged.
```

Retain the five-source inventory, Grid direct-`Set`/`Task.Run`/`ExToError` trace, unranked identity, unchanged 24+8/32 numbered roadmap counts, backend-response exclusion, maintainer sentinel matrix, and the rule that numbered rank 3 remains blocked until S1 maintainer validation completes. Do not rewrite unrelated findings or ranks.

- [ ] **Step 3: Verify documentation and source agreement**

Run:

```bash
rg -n 'Status:|SubmittedInputJson|five|parameter count|media object|Grid|maintainer validation|S1|rank 3|24.*eight|32' docs/superpowers/specs/2026-07-22-generic-submitted-input-diagnostics-design.md docs/superpowers/audits/2026-07-21-maintainability-architecture-refresh.md docs/superpowers/plans/2026-07-22-generic-submitted-input-diagnostics.md
if rg -n 'S1.*design.*pending|complete T2IParamInput|stringifies the complete|current mitigation.*do not provide' docs/superpowers/specs/2026-07-22-generic-submitted-input-diagnostics-design.md docs/superpowers/audits/2026-07-21-maintainability-architecture-refresh.md; then exit 1; fi
git diff --check -- docs/superpowers/specs/2026-07-22-generic-submitted-input-diagnostics-design.md docs/superpowers/audits/2026-07-21-maintainability-architecture-refresh.md
```

- [ ] **Step 4: Commit the architecture record**

```bash
git add docs/superpowers/specs/2026-07-22-generic-submitted-input-diagnostics-design.md docs/superpowers/audits/2026-07-21-maintainability-architecture-refresh.md
git diff --cached --check
test "$(git diff --cached --name-only | wc -l)" = "2"
git commit -m "docs: record generic submitted input redaction"
```

---

### Task 6: Complete whole-project review and maintainer handoff

**Files:**
- Review: `src/Utils/SubmittedInputJson.cs`
- Review: `src/WebAPI/API.cs`
- Review: `src/WebAPI/T2IAPI.cs`
- Review: `src/Text2Image/T2IParamSet.cs`
- Review: the design, plan, and S1 audit records
- Modify: only an approved file if review proves a defect

- [ ] **Step 1: Run independent specification review**

Review the complete implementation range and prove:

- all five submitted-input sources are protected;
- valid JSON still originates from `JObject.Parse`;
- replacement parser exceptions are fresh, fixed, same-type, and inner-free;
- initial transport/dispatch/error responses remain unchanged;
- follow-up frame ordering, cancellation, `retain`, tasks, and batch offsets remain unchanged;
- valid media conversion and filename publication remain unchanged;
- valid-JSON media failures cannot retain private values and remain generic to Grid clients;
- successful-generation diagnostics read only the total parameter count;
- general JSON, backend-response, persisted-data, metadata, webhook, and Comfy paths remain unchanged; and
- documentation matches source without claiming maintainer validation.

Correct and re-review every Critical or Important finding before continuing.

- [ ] **Step 2: Run independent code-quality/security review**

Review for alternate submitted-preview paths, exception `InnerException`/`Data` retention, null/wrong-type media fields, method-group/type inference, transport ordering, response classification, C# conventions, unnecessary scope, and exact committed files. Correct and re-review every Critical or Important finding.

- [ ] **Step 3: Run final agent-permitted static verification**

Run:

```bash
test "$(rg -o 'SubmittedInputJson\.(ParseObject|ReceiveObject)' src/WebAPI/API.cs src/WebAPI/T2IAPI.cs src/Text2Image/T2IParamSet.cs | wc -l)" = "4"
for media_type in ImageFile AudioFile VideoFile; do
    test "$(rg -F -o "return mediaFromJson<$media_type>(val, $media_type.FromDataString);" src/Text2Image/T2IParamSet.cs | wc -l)" = "1"
done
if rg -n 'ReceiveJson\(TimeSpan\.FromMinutes\(1\)|JObject\.Parse\(Encoding\.UTF8\.GetString\(rawData\)\)|GetString\(rec\)\.ParseToJson|JObject parsed = val\.ParseToJson|above image request had parameters|\{user_input\}' src/WebAPI/API.cs src/WebAPI/T2IAPI.cs src/Text2Image/T2IParamSet.cs; then exit 1; fi
if rg -n 'CleanTrashTextForDebug|ReadableString|InnerException|\{input\}' src/Utils/SubmittedInputJson.cs; then exit 1; fi
rg -n 'JSON parsing failed \(submitted content redacted\)|Failed to process submitted media object \(content redacted\)|user_input\.InternalSet\.ValuesInput\.Count|internal_error|Failed due to internal error' src/Utils/SubmittedInputJson.cs src/WebAPI/API.cs src/WebAPI/T2IAPI.cs src/Text2Image/T2IParamSet.cs src/BuiltinExtensions/GridGenerator/GridGeneratorExtension.cs
git diff --check
git status --short --branch --untracked-files=normal
git log --oneline --decorate -12
```

Then verify the committed implementation range changes only the four approved production files and approved documents. Confirm `src/Utils/Utilities.cs`, other `ReceiveJson`/`ParseToJson` callers, Comfy production files, backend-response parsers, and protected dirty files are unchanged. Do not inspect the backup directory.

- [ ] **Step 4: Hand off maintainer compilation and sentinel validation**

Ask the maintainer to run their normal build/launch command and all cases in the approved design:

1. malformed initial API WebSocket JSON;
2. malformed initial API HTTP JSON;
3. malformed follow-up generation frames;
4. malformed JSON-looking image, audio, and video values through Grid dynamic axes;
5. valid media JSON with missing/wrong fields or invalid data; and
6. successful generation containing prompt, model, media, filename, seed, and extension-defined sentinels.

Confirm no sentinel, source preview, native parser/conversion message, JSON path/line detail, filename, media/base64 prefix, parameter key/value, or extension-defined value reaches protected logs or client errors. Confirm correct parameter counts, unchanged valid requests/media/Grid/generation, unchanged cancellation/cleanup, and unchanged client error IDs/messages/statuses.

Do not mark S1 validated or resume numbered rank 3 until the maintainer confirms these results.

**Rollback:** restore only fixed operation context or value-free counts. Never restore submitted previews, parser/conversion details, filenames, media/base64 content, parameter keys/values, or content-bearing inner exceptions.
