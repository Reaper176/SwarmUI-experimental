# Redact Comfy Workflow Diagnostics Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Remove submitted/private values from nine Comfy diagnostics and prevent content-bearing JSON parser failures from escaping 12 maintained Comfy input boundaries while preserving topology, operational behavior, and non-parser exception details.

**Architecture:** Keep the implemented internal `ComfyDiagnostics` formatter as the value-eliding log boundary, and add an internal `ComfySubmittedJson` parser as the source boundary for maintained Comfy submitted/private JSON. Valid input returns the same object or unescaped tag string; malformed JSON throws a new `JsonReaderException` with fixed value-free text and no content-bearing inner exception. Migrate the 12 identified input parsers and remove the explicit raw browser WebSocket message log without changing catch locations, propagation, routing, saving, forwarding, or backend-response parsers.

**Tech Stack:** C# 12, .NET 8, Newtonsoft.Json `JToken`/`JObject`/`JArray`, existing SwarmUI logging and Comfy workflow types.

**Execution context:** Work directly on `master` as requested by the maintainer. Do not create a worktree. Preserve the existing modified files and the untracked `Data.pre-restore-2026-07-19/` directory; never inspect or stage that directory.

**Repository verification constraint:** `AGENTS.md` prohibits agents from running builds, automated tests, browsers, Comfy, backends, or the live application. Each task therefore uses static source and diff verification, followed by explicit maintainer-run validation after all review gates pass.

**Progress at revision commit `c920a7e1`:** Tasks 1–3 are implemented in commits `852905d2`, `52e1fe8b`, `f26e778a`, `b16dbac6`, `6ce3300c`, and `0ba79cae`. The first final review discovered propagation paths outside the nine local sinks, so Tasks 4–7 implement and verify the approved source-boundary expansion. Existing completed steps are marked `[x]`; do not repeat or recommit them.

---

### Task 1: Add the pure Comfy diagnostic formatter

**Files:**
- Create: `src/BuiltinExtensions/ComfyUIBackend/ComfyDiagnostics.cs`
- Reference: `docs/superpowers/specs/2026-07-21-redact-comfy-workflow-diagnostics-design.md`
- Reference: `src/Text2Image/T2IParamInput.cs`
- Reference: `src/Utils/Utilities.cs`

- [x] **Step 1: Confirm the protected state and formatter inputs**

Run:

```bash
git status --short --branch --untracked-files=normal
test ! -e src/BuiltinExtensions/ComfyUIBackend/ComfyDiagnostics.cs
rg -n 'public JObject ToJSON\(|public override string ToString\(|ValuesInput' src/Text2Image/T2IParamInput.cs
sed -n '1038,1095p' src/Utils/Utilities.cs
sed -n '590,620p' src/Utils/Utilities.cs
```

Expected:

- `master` is active.
- `ComfyDiagnostics.cs` does not exist.
- The maintainer's pre-existing dirty paths remain visible and do not overlap this plan.
- `T2IParamInput` confirms the current value-bearing conversion paths.
- `ToDenseDebugString` confirms why the new formatter must serialize its newly constructed object with Newtonsoft JSON rather than interpolate unescaped property names.
- `ParseToJson` confirms its replacement `JsonReaderException` embeds the cleaned first 256 submitted characters and must be redacted before `ReadableString()` logging.

If any planned production file is already modified, stop and reconcile ownership with the maintainer before editing.

- [x] **Step 2: Create the formatter**

Use `apply_patch` to create `src/BuiltinExtensions/ComfyUIBackend/ComfyDiagnostics.cs` with this complete implementation:

```csharp
using System.Globalization;
using FreneticUtilities.FreneticExtensions;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using SwarmUI.Text2Image;

namespace SwarmUI.Builtin_ComfyUIBackend;

/// <summary>Builds value-eliding diagnostic descriptions for Comfy workflows, typed inputs, tags, and exceptions.</summary>
internal static class ComfyDiagnostics
{
    /// <summary>Describes a raw direct workflow graph without reproducing submitted input values.</summary>
    public static string DescribeWorkflow(string workflow)
    {
        if (string.IsNullOrWhiteSpace(workflow))
        {
            return FixedStatus("empty-workflow");
        }
        JToken parsed;
        try
        {
            parsed = JToken.Parse(workflow);
        }
        catch
        {
            return FixedStatus("invalid-workflow-json");
        }
        if (parsed is not JObject graph)
        {
            return FixedStatus("invalid-workflow-shape");
        }
        try
        {
            return DescribeGraph(graph);
        }
        catch
        {
            return FixedStatus("unavailable-workflow-summary");
        }
    }

    /// <summary>Describes a raw Comfy prompt envelope without reproducing submitted input values.</summary>
    public static string DescribePromptEnvelope(string envelope)
    {
        if (string.IsNullOrWhiteSpace(envelope))
        {
            return FixedStatus("empty-prompt-envelope");
        }
        try
        {
            return DescribePromptEnvelope(JToken.Parse(envelope));
        }
        catch
        {
            return FixedStatus("invalid-prompt-envelope-json");
        }
    }

    /// <summary>Describes a parsed Comfy prompt envelope without reproducing submitted input values.</summary>
    public static string DescribePromptEnvelope(JToken envelope)
    {
        try
        {
            if (envelope is not JObject root || root["prompt"] is not JObject graph)
            {
                return FixedStatus("invalid-prompt-envelope-shape");
            }
            return DescribeGraph(graph);
        }
        catch
        {
            return FixedStatus("unavailable-prompt-envelope-summary");
        }
    }

    /// <summary>Describes typed-input parameter names without reading or formatting their values.</summary>
    public static string DescribeParameters(T2IParamInput input)
    {
        try
        {
            if (input?.InternalSet?.ValuesInput is null)
            {
                return FixedStatus("unavailable-parameter-summary");
            }
            JArray names = [];
            foreach (string name in input.InternalSet.ValuesInput.Keys)
            {
                names.Add(name);
            }
            JObject summary = new()
            {
                ["parameter_count"] = names.Count,
                ["parameter_names"] = names
            };
            return summary.ToString(Formatting.None);
        }
        catch
        {
            return FixedStatus("unavailable-parameter-summary");
        }
    }

    /// <summary>Describes a normalized workflow-tag name without retaining defaults, suffixes, or values.</summary>
    public static string DescribeNormalizedTagName(string tagName)
    {
        try
        {
            return new JValue(tagName ?? "invalid-tag-name").ToString(Formatting.None);
        }
        catch
        {
            return "\"invalid-tag-name\"";
        }
    }

    /// <summary>Describes an exception while redacting submitted content from JSON parser failures.</summary>
    public static string DescribeException(Exception exception)
    {
        try
        {
            if (exception is null)
            {
                return "Unknown error.";
            }
            if (ContainsJsonReaderException(exception))
            {
                return "JSON parsing failed (submitted content redacted).";
            }
            return exception.ReadableString();
        }
        catch
        {
            return "Error details unavailable.";
        }
    }

    /// <summary>Describes a parsed direct workflow graph without reproducing submitted input values.</summary>
    private static string DescribeGraph(JObject graph)
    {
        HashSet<string> validNodeIds = [.. graph.Properties()
            .Where(property => property.Value is JObject node && node["class_type"]?.Type == JTokenType.String && node["inputs"] is JObject)
            .Select(property => property.Name)];
        JObject nodes = [];
        foreach (JProperty nodeProperty in graph.Properties())
        {
            JObject nodeSummary = [];
            if (nodeProperty.Value is not JObject node)
            {
                nodeSummary["status"] = "invalid-node-shape";
                nodes[nodeProperty.Name] = nodeSummary;
                continue;
            }
            JToken classType = node["class_type"];
            nodeSummary["class_type"] = classType?.Type == JTokenType.String ? classType.Value<string>() : "invalid-class-type";
            if (node["inputs"] is JObject inputs)
            {
                JObject inputSummary = [];
                foreach (JProperty inputProperty in inputs.Properties())
                {
                    inputSummary[inputProperty.Name] = DescribeInput(inputProperty.Value, validNodeIds);
                }
                nodeSummary["inputs"] = inputSummary;
            }
            else
            {
                nodeSummary["inputs"] = "invalid-inputs-shape";
            }
            nodes[nodeProperty.Name] = nodeSummary;
        }
        JObject summary = new()
        {
            ["node_count"] = nodes.Count,
            ["nodes"] = nodes
        };
        return summary.ToString(Formatting.None);
    }

    /// <summary>Describes one workflow input as a validated connection or fixed value-kind marker.</summary>
    private static JToken DescribeInput(JToken input, HashSet<string> validNodeIds)
    {
        if (input is JArray array && array.Count == 2)
        {
            JToken sourceToken = array[0];
            JToken outputToken = array[1];
            if (sourceToken is not null
                && (sourceToken.Type == JTokenType.String || sourceToken.Type == JTokenType.Integer)
                && outputToken?.Type == JTokenType.Integer
                && int.TryParse(outputToken.ToString(Formatting.None), NumberStyles.Integer, CultureInfo.InvariantCulture, out int outputIndex)
                && outputIndex >= 0)
            {
                string sourceNode = sourceToken.ToString();
                if (validNodeIds.Contains(sourceNode))
                {
                    return new JObject()
                    {
                        ["source_node"] = sourceNode,
                        ["output_index"] = new JValue(outputIndex)
                    };
                }
            }
        }
        return $"redacted:{GetValueKind(input)}";
    }

    /// <summary>Maps a JSON token to a fixed value-kind name without reading its content.</summary>
    private static string GetValueKind(JToken input)
    {
        if (input is null)
        {
            return "null";
        }
        return input.Type switch
        {
            JTokenType.String => "string",
            JTokenType.Integer => "integer",
            JTokenType.Float => "float",
            JTokenType.Boolean => "boolean",
            JTokenType.Null or JTokenType.Undefined => "null",
            JTokenType.Array => "array",
            JTokenType.Object => "object",
            _ => "other"
        };
    }

    /// <summary>Returns whether an exception or any exception in its linear inner chain is a JSON reader failure.</summary>
    private static bool ContainsJsonReaderException(Exception exception)
    {
        Exception current = exception;
        while (current is not null)
        {
            if (current is JsonReaderException)
            {
                return true;
            }
            current = current.InnerException;
        }
        return false;
    }

    /// <summary>Creates a fixed, value-free status object.</summary>
    private static string FixedStatus(string status)
    {
        return new JObject() { ["status"] = status }.ToString(Formatting.None);
    }
}
```

Do not add configuration, mutable state, recursive value copying, a general utility, or a node cap.

- [x] **Step 3: Statically inspect the formatter's value boundary**

Run:

```bash
rg -n 'ToJSON|SimplifyParamVal|ToDenseDebugString|InternalSet\.ValuesInput\[[^]]+\]|\.Values\b' src/BuiltinExtensions/ComfyUIBackend/ComfyDiagnostics.cs
rg -n 'DescribeWorkflow|DescribePromptEnvelope|DescribeNormalizedTagName|DescribeException|ContainsJsonReaderException|ReadableString|validNodeIds|NumberStyles\.Integer|class_type|Properties\(\)|parameter_names|source_node|output_index|redacted:|FixedStatus|Formatting\.None' src/BuiltinExtensions/ComfyUIBackend/ComfyDiagnostics.cs
git diff --check -- src/BuiltinExtensions/ComfyUIBackend/ComfyDiagnostics.cs
git diff -- src/BuiltinExtensions/ComfyUIBackend/ComfyDiagnostics.cs
```

Expected:

- The first scan has no output except the permitted `ValuesInput` collection/key access if matched by a broader environment-specific regex; no value indexer, `ToJSON`, `SimplifyParamVal`, or dense raw serialization is present.
- Direct workflow input is never auto-unwrapped, and a direct graph node named `prompt` remains in the summary; only explicit prompt-envelope entry points select `root["prompt"]`.
- The formatter reads graph property names, approved `class_type`, parameter keys, and structurally validated connection coordinates only.
- Connection sources must be syntactically valid same-graph nodes, and output indexes must be non-negative 32-bit integers; schema-backed output validation remains intentionally out of scope.
- `DescribeException` returns fixed text for a `JsonReaderException` anywhere in the linear inner chain, preserves `ReadableString()` for non-parser exceptions, and uses fixed null/formatting fallbacks.
- Workflow, parameter, and tag strings come from newly constructed JSON or fixed JSON/string fallbacks; exception strings are fixed for parser/null/formatting failures or preserve the existing non-parser `ReadableString()` result.
- The new file follows explicit-type and XML-documentation conventions.

- [x] **Step 4: Commit the formatter**

Run:

```bash
git add src/BuiltinExtensions/ComfyUIBackend/ComfyDiagnostics.cs
git diff --cached --check
test "$(git diff --cached --name-only)" = "src/BuiltinExtensions/ComfyUIBackend/ComfyDiagnostics.cs"
git commit -m "refactor: add redacted Comfy diagnostics"
```

Expected: one new C# file only; protected user changes remain unstaged.

### Task 2: Migrate workflow generation and tag diagnostics

**Files:**
- Modify: `src/BuiltinExtensions/ComfyUIBackend/ComfyUIAPIAbstractBackend.cs:324-337`
- Modify: `src/BuiltinExtensions/ComfyUIBackend/ComfyUIAPIAbstractBackend.cs:802-899`
- Modify: `src/BuiltinExtensions/ComfyUIBackend/ComfyUIAPIAbstractBackend.cs:978-988`
- Reference: `src/BuiltinExtensions/ComfyUIBackend/ComfyDiagnostics.cs`

- [x] **Step 1: Confirm the five target statements are unchanged**

Run:

```bash
rg -n 'Will use workflow:|Error came from prompt:|Filled tag .* with|Logs\.Verbose\(\$"Error: \{ex\.ReadableString\(\)\}"\)|Failed to process comfy workflow for inputs' src/BuiltinExtensions/ComfyUIBackend/ComfyUIAPIAbstractBackend.cs
git diff -- src/BuiltinExtensions/ComfyUIBackend/ComfyUIAPIAbstractBackend.cs
```

Expected: five existing raw-value statements and no uncommitted diff.

- [x] **Step 2: Migrate all five statements with `apply_patch`**

Make these exact replacements while leaving all surrounding branches and log levels unchanged:

```csharp
Logs.Verbose($"Will use workflow structure: {ComfyDiagnostics.DescribePromptEnvelope(workflow)}");
```

```csharp
Logs.Debug($"Error came from prompt structure: {ComfyDiagnostics.DescribePromptEnvelope(workflow)}");
```

```csharp
Logs.Verbose($"Filled tag {ComfyDiagnostics.DescribeNormalizedTagName(tagBasic)} with redacted value.");
```

```csharp
Logs.Verbose($"Error: {ComfyDiagnostics.DescribeException(ex)}");
```

```csharp
Logs.Debug($"Failed to process comfy workflow for parameters {ComfyDiagnostics.DescribeParameters(user_input)} with workflow structure {ComfyDiagnostics.DescribeWorkflow(workflow)}");
```

The tag statement must remain inside the existing `Logs.MinimumLevel <= Logs.LogLevel.Verbose` guard. Do not change tag parsing, `filled`, escaping, workflow parsing/submission, the catch/throw/finally control flow, or any response log.

- [x] **Step 3: Verify generation-path compatibility and redaction**

Run:

```bash
rg -n 'ComfyDiagnostics\.(DescribeWorkflow|DescribePromptEnvelope|DescribeParameters|DescribeNormalizedTagName|DescribeException)' src/BuiltinExtensions/ComfyUIBackend/ComfyUIAPIAbstractBackend.cs
if rg -n 'Will use workflow:|Error came from prompt:|Filled tag .* with .*filled|Logs\.Verbose\(\$"Error: \{ex\.ReadableString\(\)\}"\)|Failed to process comfy workflow for inputs' src/BuiltinExtensions/ComfyUIBackend/ComfyUIAPIAbstractBackend.cs; then exit 1; fi
rg -n 'ComfyUI prompt said:|PostJSONString\(.*prompt|return Utilities\.EscapeJsonString\(filled\)|catch \(Exception ex\)' src/BuiltinExtensions/ComfyUIBackend/ComfyUIAPIAbstractBackend.cs
git diff --check -- src/BuiltinExtensions/ComfyUIBackend/ComfyUIAPIAbstractBackend.cs
git diff -- src/BuiltinExtensions/ComfyUIBackend/ComfyUIAPIAbstractBackend.cs
```

Expected:

- Exactly five migrated diagnostic statement lines containing six formatter invocations appear in this file; the `GenerateLive` structural failure statement contains both `DescribeParameters` and `DescribeWorkflow`.
- The raw-value statement patterns are absent.
- The Comfy response diagnostic, submission, tag return value, exception boundary, and surrounding flow remain unchanged.
- The malformed-workflow parser exception uses `DescribeException`, while non-parser detail, exception propagation, and cleanup remain unchanged.
- The diff contains five statement replacements only.

- [x] **Step 4: Commit the generation-path migration**

Run:

```bash
git add src/BuiltinExtensions/ComfyUIBackend/ComfyUIAPIAbstractBackend.cs
git diff --cached --check
test "$(git diff --cached --name-only)" = "src/BuiltinExtensions/ComfyUIBackend/ComfyUIAPIAbstractBackend.cs"
git commit -m "fix: redact Comfy generation diagnostics"
```

Expected: only the API abstract backend is committed; protected user changes remain unstaged.

### Task 3: Migrate preview, direct-proxy, and ControlNet diagnostics

**Files:**
- Modify: `src/BuiltinExtensions/ComfyUIBackend/ComfyUIWebAPI.cs:160-170`
- Modify: `src/BuiltinExtensions/ComfyUIBackend/ComfyUIRedirectHelper.cs:392-400`
- Modify: `src/BuiltinExtensions/ComfyUIBackend/WorkflowGeneratorSteps.cs:1006-1014`
- Reference: `src/BuiltinExtensions/ComfyUIBackend/ComfyDiagnostics.cs`

- [x] **Step 1: Confirm the remaining four target statements are unchanged**

Run:

```bash
rg -n 'ComfyGetWorkflow for input:|Above is for prompt:|ComfyUI redirection failed - prompt json parse: .*ex\.ReadableString|Following error relates to parameters:' \
  src/BuiltinExtensions/ComfyUIBackend/ComfyUIWebAPI.cs \
  src/BuiltinExtensions/ComfyUIBackend/ComfyUIRedirectHelper.cs \
  src/BuiltinExtensions/ComfyUIBackend/WorkflowGeneratorSteps.cs
git diff -- \
  src/BuiltinExtensions/ComfyUIBackend/ComfyUIWebAPI.cs \
  src/BuiltinExtensions/ComfyUIBackend/ComfyUIRedirectHelper.cs \
  src/BuiltinExtensions/ComfyUIBackend/WorkflowGeneratorSteps.cs
```

Expected: four existing raw-value statements and no uncommitted diff.

- [x] **Step 2: Migrate all four statements with `apply_patch`**

Replace the preview statement with:

```csharp
Logs.Verbose($"ComfyGetWorkflow for parameters: {ComfyDiagnostics.DescribeParameters(input)}");
```

Replace the direct-proxy fallback statement with:

```csharp
Logs.Verbose($"Above is for prompt structure: {ComfyDiagnostics.DescribePromptEnvelope(parsed)}");
```

Replace the direct-prompt parser-exception statement with:

```csharp
Logs.Debug($"ComfyUI redirection failed - prompt json parse: {ComfyDiagnostics.DescribeException(ex)}");
```

Replace the ControlNet statement with:

```csharp
Logs.Verbose($"Following error relates to parameters: {ComfyDiagnostics.DescribeParameters(g.UserInput)}");
```

Do not change the preview result, proxy routing, parsed prompt, parser catch/fallback flow, ControlNet branching, or missing-image exception.

- [x] **Step 3: Verify the three caller migrations**

Run:

```bash
rg -n 'ComfyDiagnostics\.(DescribePromptEnvelope|DescribeParameters|DescribeException)' \
  src/BuiltinExtensions/ComfyUIBackend/ComfyUIWebAPI.cs \
  src/BuiltinExtensions/ComfyUIBackend/ComfyUIRedirectHelper.cs \
  src/BuiltinExtensions/ComfyUIBackend/WorkflowGeneratorSteps.cs
if rg -n 'ComfyGetWorkflow for input:|Above is for prompt: .*ToDenseDebugString|ComfyUI redirection failed - prompt json parse: .*ex\.ReadableString|Following error relates to parameters: .*ToJSON' \
  src/BuiltinExtensions/ComfyUIBackend/ComfyUIWebAPI.cs \
  src/BuiltinExtensions/ComfyUIBackend/ComfyUIRedirectHelper.cs \
  src/BuiltinExtensions/ComfyUIBackend/WorkflowGeneratorSteps.cs; then exit 1; fi
rg -n 'Must specify either a ControlNet Image|ComfyGetGeneratedWorkflow|Was not able to redirect' \
  src/BuiltinExtensions/ComfyUIBackend/ComfyUIWebAPI.cs \
  src/BuiltinExtensions/ComfyUIBackend/ComfyUIRedirectHelper.cs \
  src/BuiltinExtensions/ComfyUIBackend/WorkflowGeneratorSteps.cs
git diff --check -- \
  src/BuiltinExtensions/ComfyUIBackend/ComfyUIWebAPI.cs \
  src/BuiltinExtensions/ComfyUIBackend/ComfyUIRedirectHelper.cs \
  src/BuiltinExtensions/ComfyUIBackend/WorkflowGeneratorSteps.cs
git diff -- \
  src/BuiltinExtensions/ComfyUIBackend/ComfyUIWebAPI.cs \
  src/BuiltinExtensions/ComfyUIBackend/ComfyUIRedirectHelper.cs \
  src/BuiltinExtensions/ComfyUIBackend/WorkflowGeneratorSteps.cs
```

Expected:

- Four migrated diagnostic statement lines containing four formatter invocations appear across the three files; `ComfyUIRedirectHelper` contains two.
- The four raw-value expressions are absent.
- Preview, proxy failure context, and ControlNet exception text remain.
- The malformed direct-prompt parser exception uses `DescribeException`, while the existing swallowed/fallback flow remains.
- One statement changes in `ComfyUIWebAPI` and `WorkflowGeneratorSteps`; two change in `ComfyUIRedirectHelper`.

- [x] **Step 4: Commit the remaining caller migrations**

Run:

```bash
git add \
  src/BuiltinExtensions/ComfyUIBackend/ComfyUIWebAPI.cs \
  src/BuiltinExtensions/ComfyUIBackend/ComfyUIRedirectHelper.cs \
  src/BuiltinExtensions/ComfyUIBackend/WorkflowGeneratorSteps.cs
git diff --cached --check
test "$(git diff --cached --name-only | wc -l)" = "3"
git commit -m "fix: redact Comfy workflow input diagnostics"
```

Expected: exactly the three approved callers are committed; protected user changes remain unstaged.

### Task 4: Add the submitted/private JSON parsing boundary

**Files:**
- Create: `src/BuiltinExtensions/ComfyUIBackend/ComfySubmittedJson.cs`
- Reference: `src/Utils/Utilities.cs:515-518`
- Reference: `src/Utils/Utilities.cs:607-615`
- Reference: `docs/superpowers/specs/2026-07-21-redact-comfy-workflow-diagnostics-design.md`

- [ ] **Step 1: Confirm the protected state and existing parsing behavior**

Run:

```bash
git status --short --branch --untracked-files=normal
test ! -e src/BuiltinExtensions/ComfyUIBackend/ComfySubmittedJson.cs
sed -n '510,520p' src/Utils/Utilities.cs
sed -n '600,618p' src/Utils/Utilities.cs
```

Expected:

- `master` is active and the known maintainer-owned dirty paths remain untouched.
- `ComfySubmittedJson.cs` does not exist.
- `UnescapeJsonString` parses a constructed object and returns its `value` string.
- `ParseToJson` returns `JObject.Parse(input)` for valid JSON but embeds a cleaned 256-character input preview in malformed-JSON exceptions.

Do not run a test or build; repository policy requires static equivalence review and later maintainer validation.

- [ ] **Step 2: Create the focused parser**

Use `apply_patch` to create `src/BuiltinExtensions/ComfyUIBackend/ComfySubmittedJson.cs` with this complete implementation:

```csharp
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace SwarmUI.Builtin_ComfyUIBackend;

/// <summary>Parses Comfy-maintained submitted or private JSON without propagating content-bearing parser errors.</summary>
internal static class ComfySubmittedJson
{
    /// <summary>Fixed parser failure text that contains no submitted JSON or parser-derived location details.</summary>
    private const string ParserFailureMessage = "JSON parsing failed (submitted content redacted).";

    /// <summary>Parses a submitted or private JSON object through a value-free failure boundary.</summary>
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

    /// <summary>Unescapes a submitted workflow-tag string through a value-free failure boundary.</summary>
    public static string UnescapeString(string input)
    {
        try
        {
            return JObject.Parse("{ \"value\": \"" + input + "\" }")["value"].ToString();
        }
        catch (JsonReaderException)
        {
            throw new JsonReaderException(ParserFailureMessage);
        }
    }
}
```

Do not add an inner exception, source preview, native parser message, JSON path, line information, logging, configuration, or a general utility abstraction. Keep the constructed tag object byte-for-byte equivalent to `Utilities.UnescapeJsonString`.

- [ ] **Step 3: Statically verify the parser boundary**

Run:

```bash
test "$(rg -c 'catch \(JsonReaderException\)' src/BuiltinExtensions/ComfyUIBackend/ComfySubmittedJson.cs)" = "2"
test "$(rg -c 'throw new JsonReaderException\(ParserFailureMessage\);' src/BuiltinExtensions/ComfyUIBackend/ComfySubmittedJson.cs)" = "2"
if rg -n 'InnerException|CleanTrashTextForDebug|ReadableString|ex\.Message|input\[|input\.Replace|Logs\.' src/BuiltinExtensions/ComfyUIBackend/ComfySubmittedJson.cs; then exit 1; fi
rg -n 'ParseObject|string UnescapeString|JObject\.Parse|ParserFailureMessage' src/BuiltinExtensions/ComfyUIBackend/ComfySubmittedJson.cs
git diff --check -- src/BuiltinExtensions/ComfyUIBackend/ComfySubmittedJson.cs
git diff -- src/BuiltinExtensions/ComfyUIBackend/ComfySubmittedJson.cs
```

Expected:

- Both methods catch only `JsonReaderException` and throw a fresh same-type exception containing the shared fixed message.
- The old exception is not named or retained, so no inner chain can recover submitted content.
- Valid object parsing and tag unescaping use the same Newtonsoft operations as the old paths.
- The class follows explicit-type, braces, and XML-documentation conventions.

- [ ] **Step 4: Commit the parser boundary**

Run:

```bash
git add src/BuiltinExtensions/ComfyUIBackend/ComfySubmittedJson.cs
git diff --cached --check
test "$(git diff --cached --name-only)" = "src/BuiltinExtensions/ComfyUIBackend/ComfySubmittedJson.cs"
git commit -m "refactor: add safe Comfy JSON parser"
```

Expected: one new C# file only; protected maintainer changes remain unstaged.

### Task 5: Migrate workflow, tag, metadata, and stored-workflow parsing

**Files:**
- Modify: `src/BuiltinExtensions/ComfyUIBackend/ComfyUIAPIAbstractBackend.cs:237`
- Modify: `src/BuiltinExtensions/ComfyUIBackend/ComfyUIAPIAbstractBackend.cs:812`
- Modify: `src/BuiltinExtensions/ComfyUIBackend/ComfyUIAPIAbstractBackend.cs:1022`
- Modify: `src/BuiltinExtensions/ComfyUIBackend/ComfyUIBackendExtension.cs:168`
- Modify: `src/BuiltinExtensions/ComfyUIBackend/ComfyUIBackendExtension.cs:319`
- Reference: `src/BuiltinExtensions/ComfyUIBackend/ComfySubmittedJson.cs`

- [ ] **Step 1: Confirm the five source boundaries and clean caller diffs**

Run:

```bash
rg -n 'workflowJson = Utilities\.ParseToJson\(workflow\)|fixedTag = Utilities\.UnescapeJsonString\(tag\)|workflow = Utilities\.ParseToJson\(workflowRaw\)' src/BuiltinExtensions/ComfyUIBackend/ComfyUIAPIAbstractBackend.cs
rg -n 'ParameterMetadataCacheHelper = new\(s => s\.ParseToJson\(\)\)|JObject json = File\.ReadAllText\(path\)\.ParseToJson\(\)' src/BuiltinExtensions/ComfyUIBackend/ComfyUIBackendExtension.cs
git diff -- src/BuiltinExtensions/ComfyUIBackend/ComfyUIAPIAbstractBackend.cs src/BuiltinExtensions/ComfyUIBackend/ComfyUIBackendExtension.cs
```

Expected: exactly three API-backend matches, two extension matches, and no uncommitted caller diff.

- [ ] **Step 2: Replace the API-backend parser entry points**

Use `apply_patch` for these exact replacements:

```csharp
JObject workflowJson = ComfySubmittedJson.ParseObject(workflow);
```

```csharp
string fixedTag = ComfySubmittedJson.UnescapeString(tag);
```

```csharp
JObject workflow = ComfySubmittedJson.ParseObject(workflowRaw);
```

Do not change workflow preprocessing, tag splitting/defaulting/filling/escaping, node validation, rejection reasons, matching, generation, catches, or propagation.

- [ ] **Step 3: Replace the metadata and stored-container parser entry points**

Use `apply_patch` for these exact replacements:

```csharp
public static SingleCacheAsync<string, JObject> ParameterMetadataCacheHelper = new(s => ComfySubmittedJson.ParseObject(s));
```

```csharp
JObject json = ComfySubmittedJson.ParseObject(File.ReadAllText(path));
```

Do not change cache keys/lifetime, dynamic parameter construction, custom-workflow field extraction, file I/O, fallback image, caching, catch logging, or null return behavior.

- [ ] **Step 4: Verify valid and malformed flow equivalence**

Run:

```bash
test "$(rg -o 'ComfySubmittedJson\.(ParseObject|UnescapeString)' src/BuiltinExtensions/ComfyUIBackend/ComfyUIAPIAbstractBackend.cs src/BuiltinExtensions/ComfyUIBackend/ComfyUIBackendExtension.cs | wc -l)" = "5"
if rg -n 'workflowJson = Utilities\.ParseToJson\(workflow\)|fixedTag = Utilities\.UnescapeJsonString\(tag\)|workflow = Utilities\.ParseToJson\(workflowRaw\)|ParameterMetadataCacheHelper = new\(s => s\.ParseToJson\(\)\)|JObject json = File\.ReadAllText\(path\)\.ParseToJson\(\)' src/BuiltinExtensions/ComfyUIBackend/ComfyUIAPIAbstractBackend.cs src/BuiltinExtensions/ComfyUIBackend/ComfyUIBackendExtension.cs; then exit 1; fi
rg -n 'return Utilities\.EscapeJsonString\(filled\)|RefusalReasons\.Add|catch \(Exception (ex|e)\)|CustomWorkflows\[name\] = workflow|return null;' src/BuiltinExtensions/ComfyUIBackend/ComfyUIAPIAbstractBackend.cs src/BuiltinExtensions/ComfyUIBackend/ComfyUIBackendExtension.cs
git diff --check -- src/BuiltinExtensions/ComfyUIBackend/ComfyUIAPIAbstractBackend.cs src/BuiltinExtensions/ComfyUIBackend/ComfyUIBackendExtension.cs
git diff -- src/BuiltinExtensions/ComfyUIBackend/ComfyUIAPIAbstractBackend.cs src/BuiltinExtensions/ComfyUIBackend/ComfyUIBackendExtension.cs
```

Expected:

- Exactly five safe-parser invocations replace the five identified source boundaries.
- All surrounding operational statements and catches remain unchanged.
- Backend-response parses in these files remain on their existing parsers.
- The diff is five parser-expression replacements only.

- [ ] **Step 5: Commit the workflow and storage migrations**

Run:

```bash
git add src/BuiltinExtensions/ComfyUIBackend/ComfyUIAPIAbstractBackend.cs src/BuiltinExtensions/ComfyUIBackend/ComfyUIBackendExtension.cs
git diff --cached --check
test "$(git diff --cached --name-only | wc -l)" = "2"
git commit -m "fix: sanitize Comfy workflow parser failures"
```

Expected: exactly the two approved callers are committed; protected maintainer changes remain unstaged.

### Task 6: Migrate submitted endpoint and browser WebSocket parsing

**Files:**
- Modify: `src/BuiltinExtensions/ComfyUIBackend/ComfyUIWebAPI.cs:69-72`
- Modify: `src/BuiltinExtensions/ComfyUIBackend/ComfyUIRedirectHelper.cs:335`
- Modify: `src/BuiltinExtensions/ComfyUIBackend/ComfyUIRedirectHelper.cs:413`
- Modify: `src/BuiltinExtensions/ComfyUIBackend/ComfyUser.cs:132-140`
- Reference: `src/BuiltinExtensions/ComfyUIBackend/ComfySubmittedJson.cs`
- Reference: `src/BuiltinExtensions/ComfyUIBackend/ComfyDiagnostics.cs`

- [ ] **Step 1: Confirm the seven parser calls and raw-message log**

Run:

```bash
rg -n '\["(workflow|prompt|custom_params|param_values)"\] = (workflow|prompt|custom_params|param_values)\.ParseToJson\(\)' src/BuiltinExtensions/ComfyUIBackend/ComfyUIWebAPI.cs
rg -n 'JObject parsed = StringConversionHelper\.UTF8Encoding\.GetString\(data\)\.ParseToJson\(\)|JObject interruptData = StringConversionHelper\.UTF8Encoding\.GetString\(inputBytes\)\.ParseToJson\(\)' src/BuiltinExtensions/ComfyUIBackend/ComfyUIRedirectHelper.cs
rg -n 'JObject parsed = rawText\.ParseToJson\(\)|Failed to parse ComfyUI user message.*rawText.*ex\.ReadableString' src/BuiltinExtensions/ComfyUIBackend/ComfyUser.cs
git diff -- src/BuiltinExtensions/ComfyUIBackend/ComfyUIWebAPI.cs src/BuiltinExtensions/ComfyUIBackend/ComfyUIRedirectHelper.cs src/BuiltinExtensions/ComfyUIBackend/ComfyUser.cs
```

Expected: four save-field calls, two redirect calls, one browser WebSocket call, one raw-message diagnostic, and no uncommitted caller diff.

- [ ] **Step 2: Replace all four save-field parsers**

Use `apply_patch` to make the `data` initializer contain exactly:

```csharp
["workflow"] = ComfySubmittedJson.ParseObject(workflow),
["prompt"] = ComfySubmittedJson.ParseObject(prompt),
["custom_params"] = ComfySubmittedJson.ParseObject(custom_params),
["param_values"] = ComfySubmittedJson.ParseObject(param_values),
```

Do not change the in-memory publication, optional replacement deletion, image/description fields, file path, serialization, write ordering, or response.

- [ ] **Step 3: Replace the direct prompt and interrupt parsers**

Use `apply_patch` for these exact replacements:

```csharp
JObject parsed = ComfySubmittedJson.ParseObject(StringConversionHelper.UTF8Encoding.GetString(data));
```

```csharp
JObject interruptData = ComfySubmittedJson.ParseObject(StringConversionHelper.UTF8Encoding.GetString(inputBytes));
```

Do not add a new catch or response. Preserve prompt routing/fallback, prompt-ID remapping, interrupt fan-out, body bytes, and existing exception propagation.

- [ ] **Step 4: Replace the browser WebSocket parser and diagnostic**

Use `apply_patch` for these exact replacements:

```csharp
JObject parsed = ComfySubmittedJson.ParseObject(rawText);
```

```csharp
Logs.Error($"Failed to parse ComfyUI user message: {ComfyDiagnostics.DescribeException(ex)}");
```

Keep `rawText` as the parser input but never interpolate it into diagnostics. Preserve frame-size/type checks, feature-flag handling, the catch location, and the unconditional post-catch `NewMessageToServers` forwarding call.

- [ ] **Step 5: Verify all seven boundaries and the raw-log removal**

Run:

```bash
test "$(rg -o 'ComfySubmittedJson\.ParseObject' src/BuiltinExtensions/ComfyUIBackend/ComfyUIWebAPI.cs src/BuiltinExtensions/ComfyUIBackend/ComfyUIRedirectHelper.cs src/BuiltinExtensions/ComfyUIBackend/ComfyUser.cs | wc -l)" = "7"
if rg -n '\["(workflow|prompt|custom_params|param_values)"\] = (workflow|prompt|custom_params|param_values)\.ParseToJson\(\)|JObject parsed = StringConversionHelper\.UTF8Encoding\.GetString\(data\)\.ParseToJson\(\)|JObject interruptData = StringConversionHelper\.UTF8Encoding\.GetString\(inputBytes\)\.ParseToJson\(\)|JObject parsed = rawText\.ParseToJson\(\)|Failed to parse ComfyUI user message.*rawText' src/BuiltinExtensions/ComfyUIBackend/ComfyUIWebAPI.cs src/BuiltinExtensions/ComfyUIBackend/ComfyUIRedirectHelper.cs src/BuiltinExtensions/ComfyUIBackend/ComfyUser.cs; then exit 1; fi
rg -n 'ComfySubmittedJson\.ParseObject|Failed to parse ComfyUI user message:|NewMessageToServers\(recvBuf\.AsMemory|prompt_id|File\.WriteAllBytes' src/BuiltinExtensions/ComfyUIBackend/ComfyUIWebAPI.cs src/BuiltinExtensions/ComfyUIBackend/ComfyUIRedirectHelper.cs src/BuiltinExtensions/ComfyUIBackend/ComfyUser.cs
git diff --check -- src/BuiltinExtensions/ComfyUIBackend/ComfyUIWebAPI.cs src/BuiltinExtensions/ComfyUIBackend/ComfyUIRedirectHelper.cs src/BuiltinExtensions/ComfyUIBackend/ComfyUser.cs
git diff -- src/BuiltinExtensions/ComfyUIBackend/ComfyUIWebAPI.cs src/BuiltinExtensions/ComfyUIBackend/ComfyUIRedirectHelper.cs src/BuiltinExtensions/ComfyUIBackend/ComfyUser.cs
```

Expected:

- Exactly seven safe-parser calls replace the submitted endpoint and browser WebSocket parsers.
- No protected old parser expression or raw browser message log remains.
- The WebSocket catch uses the existing diagnostic formatter, and its frame is still forwarded afterward.
- Backend-to-browser and backend-output parsers in the redirect helper and `ComfyUser` remain unchanged.
- The diff contains seven parser-expression replacements and one diagnostic replacement only.

- [ ] **Step 6: Commit the endpoint and WebSocket migrations**

Run:

```bash
git add src/BuiltinExtensions/ComfyUIBackend/ComfyUIWebAPI.cs src/BuiltinExtensions/ComfyUIBackend/ComfyUIRedirectHelper.cs src/BuiltinExtensions/ComfyUIBackend/ComfyUser.cs
git diff --cached --check
test "$(git diff --cached --name-only | wc -l)" = "3"
git commit -m "fix: sanitize submitted Comfy JSON failures"
```

Expected: exactly the three approved callers are committed; protected maintainer changes remain unstaged.

### Task 7: Reconcile the architecture record

**Files:**
- Modify: `docs/superpowers/specs/2026-07-21-redact-comfy-workflow-diagnostics-design.md`
- Modify: `docs/superpowers/audits/2026-07-21-maintainability-architecture-refresh.md`
- Review: `docs/superpowers/plans/2026-07-21-redact-comfy-workflow-diagnostics.md`

- [ ] **Step 1: Update implementation status and exact evidence**

Use `apply_patch` to:

- set the design status to `Implemented, awaiting maintainer validation`;
- retain the exact count of nine original diagnostic statements and 12 submitted/private parsing or unescaping invocations;
- update Comfy F25, its risk-register row, and roadmap rank 2 to name `ComfySubmittedJson`, the raw browser WebSocket diagnostic, and the newly protected propagation paths;
- record generic API/T2I parsing and successful-generation parameter disclosures as a distinct immediate follow-up rather than claiming they were fixed; and
- leave all finding and roadmap numbering stable unless a new separately evidenced finding is explicitly added.

Do not rewrite unrelated audit findings or ranks.

The audit update must state these concrete facts rather than use generic completion language:

- F25's original nine sink inventory remains valid, but an exception-flow trace found 12 Comfy-owned submitted/private parsing or unescaping invocations whose failures can reach local, generic, or framework diagnostics.
- The implemented boundary consists of `ComfyDiagnostics` for value-eliding representations and `ComfySubmittedJson` for fresh, fixed-message `JsonReaderException` failures without inner exceptions.
- The expanded owners are `ComfyUIAPIAbstractBackend`, `ComfyUIBackendExtension`, `ComfyUIWebAPI`, `ComfyUIRedirectHelper`, `ComfyUser`, and `WorkflowGeneratorSteps`.
- Backend-response parsing is excluded by provenance and unchanged.
- The next-project record names the separately confirmed generic API initial WebSocket parser at `src/WebAPI/API.cs:70`, HTTP body parser at `src/WebAPI/API.cs:85-87`, follow-up T2I WebSocket parser at `src/WebAPI/T2IAPI.cs:116-124`, dynamic media parameter parsing at `src/Text2Image/T2IParamSet.cs:158-190`, and successful-generation `T2IParamInput.ToString()` diagnostic at `src/WebAPI/T2IAPI.cs:316`. It must explicitly say the current Comfy change does not fix those paths.

- [ ] **Step 2: Verify documentation consistency**

Run:

```bash
rg -n 'Status:|nine|12|ComfySubmittedJson|WebSocket|generic WebAPI|T2I' docs/superpowers/specs/2026-07-21-redact-comfy-workflow-diagnostics-design.md docs/superpowers/audits/2026-07-21-maintainability-architecture-refresh.md
if rg -n '\bTBD\b|implement later|fill in details|only two malformed|four existing owners|nine submitted/private' docs/superpowers/specs/2026-07-21-redact-comfy-workflow-diagnostics-design.md docs/superpowers/audits/2026-07-21-maintainability-architecture-refresh.md; then exit 1; fi
git diff --check -- docs/superpowers/specs/2026-07-21-redact-comfy-workflow-diagnostics-design.md docs/superpowers/audits/2026-07-21-maintainability-architecture-refresh.md
git diff -- docs/superpowers/specs/2026-07-21-redact-comfy-workflow-diagnostics-design.md docs/superpowers/audits/2026-07-21-maintainability-architecture-refresh.md
```

Expected: the specification and audit agree on scope, counts, implementation state, exclusions, and the separate generic follow-up.

- [ ] **Step 3: Commit the architecture record**

Run:

```bash
git add docs/superpowers/specs/2026-07-21-redact-comfy-workflow-diagnostics-design.md docs/superpowers/audits/2026-07-21-maintainability-architecture-refresh.md
git diff --cached --check
test "$(git diff --cached --name-only | wc -l)" = "2"
git commit -m "docs: record Comfy JSON redaction boundary"
```

Expected: exactly the design and audit are committed; protected maintainer changes remain unstaged.

### Task 8: Review the complete implementation and hand off runtime validation

**Files:**
- Review: `src/BuiltinExtensions/ComfyUIBackend/ComfyDiagnostics.cs`
- Review: `src/BuiltinExtensions/ComfyUIBackend/ComfySubmittedJson.cs`
- Review: `src/BuiltinExtensions/ComfyUIBackend/ComfyUIAPIAbstractBackend.cs`
- Review: `src/BuiltinExtensions/ComfyUIBackend/ComfyUIBackendExtension.cs`
- Review: `src/BuiltinExtensions/ComfyUIBackend/ComfyUIWebAPI.cs`
- Review: `src/BuiltinExtensions/ComfyUIBackend/ComfyUIRedirectHelper.cs`
- Review: `src/BuiltinExtensions/ComfyUIBackend/ComfyUser.cs`
- Review: `src/BuiltinExtensions/ComfyUIBackend/WorkflowGeneratorSteps.cs`
- Review: `docs/superpowers/specs/2026-07-21-redact-comfy-workflow-diagnostics-design.md`
- Review: `docs/superpowers/audits/2026-07-21-maintainability-architecture-refresh.md`
- Modify: only a file above if review finds a verified defect

- [ ] **Step 1: Run an independent specification-compliance review**

Review the complete implementation range against the design and verify:

- exactly nine confirmed submitted-value statements migrated;
- all four original owners use the one internal formatter;
- only approved identifiers, counts, fixed markers, and validated connections are retained;
- direct graphs and prompt envelopes use distinct entry points, and direct graph nodes named `prompt` cannot be auto-unwrapped;
- connection recognition requires a two-element array, a non-negative 32-bit integer output index, and a syntactically valid source node present in the same graph;
- connection validation is structural rather than schema-backed and has no `object_info`, node-schema, global, or backend dependency;
- typed-parameter formatting reads keys without formatting values;
- tag formatting passes only `tagBasic` to `DescribeNormalizedTagName` and safely escapes it;
- exception formatting replaces parser exception details with fixed value-free text when a `JsonReaderException` appears anywhere in the linear inner chain, preserves non-parser `ReadableString()` output, and has fixed null/formatting fallbacks;
- exactly 12 submitted/private parsing or unescaping invocations use `ComfySubmittedJson`;
- both parser methods catch only `JsonReaderException` and throw a fresh same-type exception with fixed text and no inner exception;
- all valid parser operations use equivalent Newtonsoft parsing and return shapes;
- the browser-to-Swarm WebSocket catch does not log `rawText`, uses safe exception formatting, and still forwards the frame afterward;
- save-before-write ordering, catch locations, failure propagation, cache behavior, matching, routing, interruption, and tag processing remain unchanged;
- invalid/malformed diagnostic inputs cannot throw or expose parser messages;
- all log levels and operational control flow remain unchanged;
- backend-response/output/history parsers and diagnostics remain unchanged;
- generic WebAPI/T2I disclosures are documented as a separate follow-up; and
- no unrelated behavior or file was added.

If the review finds a defect, correct it with `apply_patch`, repeat the affected task's static checks, commit the correction, and return it to the same reviewer.

- [ ] **Step 2: Run an independent code-quality and security review**

After specification approval, review for:

- accidental content retention through identifiers, arrays, exception text, `ToString`, or token reuse;
- accidental content retention through the replacement exception's message, inner chain, source, or data;
- mutation of source tokens;
- malformed/null input safety;
- Newtonsoft ownership/escaping correctness;
- exact valid-input equivalence for object parsing and tag unescaping;
- C# explicit-type, braces, and XML-documentation conventions;
- unnecessary abstraction or generalized utility scope;
- diagnostic usefulness and log-level preservation; and
- exact commit/file scope.

Resolve all critical or important findings through the implementer/re-review loop before proceeding.

- [ ] **Step 3: Run final agent-permitted static verification**

Run:

```bash
target_files='src/BuiltinExtensions/ComfyUIBackend/ComfyDiagnostics.cs src/BuiltinExtensions/ComfyUIBackend/ComfySubmittedJson.cs src/BuiltinExtensions/ComfyUIBackend/ComfyUIAPIAbstractBackend.cs src/BuiltinExtensions/ComfyUIBackend/ComfyUIBackendExtension.cs src/BuiltinExtensions/ComfyUIBackend/ComfyUIWebAPI.cs src/BuiltinExtensions/ComfyUIBackend/ComfyUIRedirectHelper.cs src/BuiltinExtensions/ComfyUIBackend/ComfyUser.cs src/BuiltinExtensions/ComfyUIBackend/WorkflowGeneratorSteps.cs'
rg -n 'ComfyDiagnostics\.(DescribeWorkflow|DescribePromptEnvelope|DescribeParameters|DescribeNormalizedTagName|DescribeException)' \
  src/BuiltinExtensions/ComfyUIBackend/ComfyUIAPIAbstractBackend.cs \
  src/BuiltinExtensions/ComfyUIBackend/ComfyUIWebAPI.cs \
  src/BuiltinExtensions/ComfyUIBackend/ComfyUIRedirectHelper.cs \
  src/BuiltinExtensions/ComfyUIBackend/ComfyUser.cs \
  src/BuiltinExtensions/ComfyUIBackend/WorkflowGeneratorSteps.cs
# Count the nine original migrated statements plus the browser WebSocket statement.
test "$(rg -c 'ComfyDiagnostics\.(DescribeWorkflow|DescribePromptEnvelope|DescribeParameters|DescribeNormalizedTagName|DescribeException)' \
  src/BuiltinExtensions/ComfyUIBackend/ComfyUIAPIAbstractBackend.cs \
  src/BuiltinExtensions/ComfyUIBackend/ComfyUIWebAPI.cs \
  src/BuiltinExtensions/ComfyUIBackend/ComfyUIRedirectHelper.cs \
  src/BuiltinExtensions/ComfyUIBackend/ComfyUser.cs \
  src/BuiltinExtensions/ComfyUIBackend/WorkflowGeneratorSteps.cs | awk -F: '{ total += $2 } END { print total }')" = "10"
# Count all eleven formatter invocations; the GenerateLive structural statement contains two.
test "$(rg -o 'ComfyDiagnostics\.(DescribeWorkflow|DescribePromptEnvelope|DescribeParameters|DescribeNormalizedTagName|DescribeException)' \
  src/BuiltinExtensions/ComfyUIBackend/ComfyUIAPIAbstractBackend.cs \
  src/BuiltinExtensions/ComfyUIBackend/ComfyUIWebAPI.cs \
  src/BuiltinExtensions/ComfyUIBackend/ComfyUIRedirectHelper.cs \
  src/BuiltinExtensions/ComfyUIBackend/ComfyUser.cs \
  src/BuiltinExtensions/ComfyUIBackend/WorkflowGeneratorSteps.cs | awk 'END { print NR }')" = "11"
test "$(rg -o 'ComfySubmittedJson\.(ParseObject|UnescapeString)' \
  src/BuiltinExtensions/ComfyUIBackend/ComfyUIAPIAbstractBackend.cs \
  src/BuiltinExtensions/ComfyUIBackend/ComfyUIBackendExtension.cs \
  src/BuiltinExtensions/ComfyUIBackend/ComfyUIWebAPI.cs \
  src/BuiltinExtensions/ComfyUIBackend/ComfyUIRedirectHelper.cs \
  src/BuiltinExtensions/ComfyUIBackend/ComfyUser.cs | wc -l)" = "12"
if rg -n 'Filled tag .* with .*filled|Will use workflow: .*ToDenseDebugString|Error came from prompt: .*ToDenseDebugString|Logs\.Verbose\(\$"Error: \{ex\.ReadableString\(\)\}"\)|Failed to process comfy workflow for inputs|ComfyGetWorkflow for input:|Above is for prompt: .*ToDenseDebugString|ComfyUI redirection failed - prompt json parse: .*ex\.ReadableString|Following error relates to parameters: .*ToJSON|Failed to parse ComfyUI user message.*rawText' \
  src/BuiltinExtensions/ComfyUIBackend/ComfyUIAPIAbstractBackend.cs \
  src/BuiltinExtensions/ComfyUIBackend/ComfyUIWebAPI.cs \
  src/BuiltinExtensions/ComfyUIBackend/ComfyUIRedirectHelper.cs \
  src/BuiltinExtensions/ComfyUIBackend/ComfyUser.cs \
  src/BuiltinExtensions/ComfyUIBackend/WorkflowGeneratorSteps.cs; then exit 1; fi
test "$(rg -c 'throw new JsonReaderException\(ParserFailureMessage\);' src/BuiltinExtensions/ComfyUIBackend/ComfySubmittedJson.cs)" = "2"
if rg -n 'InnerException|CleanTrashTextForDebug|ReadableString|ex\.Message|Logs\.' src/BuiltinExtensions/ComfyUIBackend/ComfySubmittedJson.cs; then exit 1; fi
git diff --check
git status --short --branch --untracked-files=normal
git log --oneline --decorate -8
```

The first diagnostic count verifies the nine original statement lines plus the corrected browser WebSocket statement. The second verifies eleven total formatter invocations because the `GenerateLive` structural failure statement combines two summaries. The parser count verifies all 12 submitted/private boundaries.

Then determine the implementation base commit and verify the committed range changes exactly the eight approved production files and approved documentation. Do not include the pre-existing dirty working-tree changes in that range check.

Do not run a build, automated test, browser, live server, launcher, installer, or backend/Comfy process.

- [ ] **Step 4: Hand off maintainer validation**

This is the required **Maintainer Validation** gate; agent-permitted static verification does not replace it.

Ask the maintainer to place recognizable sentinels near the start of API-key, positive/negative prompt, media, dynamic/default, extension-private, and direct-workflow values. Use distinct sentinels in each malformed submitted/private JSON source, then validate:

1. successful ordinary and stored/raw workflow generation at verbose level;
2. Comfy prompt validation and transport/processing failures at debug level;
3. generated-workflow preview;
4. direct-proxy prompt fallback;
5. ControlNet strength without a usable ControlNet/init image;
6. malformed raw-workflow parsing and its `GenerateLive` catch;
7. malformed direct-prompt parsing and its redirect catch;
8. malformed saved-workflow `workflow`, `prompt`, `custom_params`, and `param_values` fields;
9. malformed dynamic workflow parameter metadata;
10. a malformed persisted custom-workflow container;
11. a malformed workflow-tag escape;
12. a malformed direct interrupt body;
13. a malformed browser-to-Swarm Comfy WebSocket message; and
14. supported verbose/debug log-level combinations.

Confirm no sentinel, filled value, submitted scalar, base64 prefix, raw WebSocket body, source preview, native parser message, JSON path, or line detail appears. Every malformed submitted/private JSON path must expose only `JSON parsing failed (submitted content redacted).`; non-parser exceptions must retain their prior readable detail. Confirm every node ID/class/input/connection and parameter name/count remains useful, normalized tag names contain no raw default/suffix, fixed malformed-input markers are safe, valid save/load/dynamic/tag/interrupt/WebSocket behavior is unchanged, malformed browser WebSocket frames retain their existing forwarding behavior, and all other operational behavior and the ControlNet error are unchanged.

Do not claim runtime completion until the maintainer reports these scenarios pass. If validation fails, collect the exact path, log level, structural summary, leaked/missing field, operational result, and reproduction steps before proposing a correction.

**Rollback:** if a migrated sink leaks content or its formatter cannot safely describe the encountered shape, replace that sink with a fixed value-free message while correcting the shared formatter. If a safe parser migration changes valid behavior, correct its valid-input equivalence without restoring content-bearing exceptions. Never restore raw filled tags, workflows, prompts, typed inputs, base64 content, raw WebSocket messages, or parser exception text as the accepted fallback.
