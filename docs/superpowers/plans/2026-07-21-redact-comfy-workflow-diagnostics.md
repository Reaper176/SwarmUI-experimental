# Redact Comfy Workflow Diagnostics Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace seven Comfy server-log disclosures with one value-eliding diagnostic boundary that preserves complete workflow topology and parameter-name context.

**Architecture:** Add an internal, pure `ComfyDiagnostics` formatter owned by the Comfy integration. Provenance-specific direct-graph and prompt-envelope entry points build new JSON summaries containing only approved node IDs, class types, input names, structurally validated source node IDs/non-negative 32-bit output indexes, parameter names, counts, and fixed markers; four existing diagnostic owners then migrate to this formatter without changing workflow, request, routing, exception, or log-level behavior.

**Tech Stack:** C# 12, .NET 8, Newtonsoft.Json `JToken`/`JObject`/`JArray`, existing SwarmUI logging and Comfy workflow types.

**Execution context:** Work directly on `master` as requested by the maintainer. Do not create a worktree. Preserve the existing modified files and the untracked `Data.pre-restore-2026-07-19/` directory; never inspect or stage that directory.

**Repository verification constraint:** `AGENTS.md` prohibits agents from running builds, automated tests, browsers, Comfy, backends, or the live application. Each task therefore uses static source and diff verification, followed by explicit maintainer-run validation after all review gates pass.

---

### Task 1: Add the pure Comfy diagnostic formatter

**Files:**
- Create: `src/BuiltinExtensions/ComfyUIBackend/ComfyDiagnostics.cs`
- Reference: `docs/superpowers/specs/2026-07-21-redact-comfy-workflow-diagnostics-design.md`
- Reference: `src/Text2Image/T2IParamInput.cs`

- [ ] **Step 1: Confirm the protected state and formatter inputs**

Run:

```bash
git status --short --branch --untracked-files=normal
test ! -e src/BuiltinExtensions/ComfyUIBackend/ComfyDiagnostics.cs
rg -n 'public JObject ToJSON\(|public override string ToString\(|ValuesInput' src/Text2Image/T2IParamInput.cs
sed -n '1038,1095p' src/Utils/Utilities.cs
```

Expected:

- `master` is active.
- `ComfyDiagnostics.cs` does not exist.
- The maintainer's pre-existing dirty paths remain visible and do not overlap this plan.
- `T2IParamInput` confirms the current value-bearing conversion paths.
- `ToDenseDebugString` confirms why the new formatter must serialize its newly constructed object with Newtonsoft JSON rather than interpolate unescaped property names.

If any planned production file is already modified, stop and reconcile ownership with the maintainer before editing.

- [ ] **Step 2: Create the formatter**

Use `apply_patch` to create `src/BuiltinExtensions/ComfyUIBackend/ComfyDiagnostics.cs` with this complete implementation:

```csharp
using System.Globalization;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using SwarmUI.Text2Image;

namespace SwarmUI.Builtin_ComfyUIBackend;

/// <summary>Builds value-eliding diagnostic descriptions for Comfy workflows and typed inputs.</summary>
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

    /// <summary>Creates a fixed, value-free status object.</summary>
    private static string FixedStatus(string status)
    {
        return new JObject() { ["status"] = status }.ToString(Formatting.None);
    }
}
```

Do not add configuration, mutable state, recursive value copying, a general utility, or a node cap.

- [ ] **Step 3: Statically inspect the formatter's value boundary**

Run:

```bash
rg -n 'ToJSON|SimplifyParamVal|ToDenseDebugString|InternalSet\.ValuesInput\[[^]]+\]|\.Values\b' src/BuiltinExtensions/ComfyUIBackend/ComfyDiagnostics.cs
rg -n 'DescribeWorkflow|DescribePromptEnvelope|DescribeNormalizedTagName|validNodeIds|NumberStyles\.Integer|class_type|Properties\(\)|parameter_names|source_node|output_index|redacted:|FixedStatus|Formatting\.None' src/BuiltinExtensions/ComfyUIBackend/ComfyDiagnostics.cs
git diff --check -- src/BuiltinExtensions/ComfyUIBackend/ComfyDiagnostics.cs
git diff -- src/BuiltinExtensions/ComfyUIBackend/ComfyDiagnostics.cs
```

Expected:

- The first scan has no output except the permitted `ValuesInput` collection/key access if matched by a broader environment-specific regex; no value indexer, `ToJSON`, `SimplifyParamVal`, or dense raw serialization is present.
- Direct workflow input is never auto-unwrapped, and a direct graph node named `prompt` remains in the summary; only explicit prompt-envelope entry points select `root["prompt"]`.
- The formatter reads graph property names, approved `class_type`, parameter keys, and structurally validated connection coordinates only.
- Connection sources must be syntactically valid same-graph nodes, and output indexes must be non-negative 32-bit integers; schema-backed output validation remains intentionally out of scope.
- All final strings come from newly constructed JSON or fixed JSON/string fallbacks.
- The new file follows explicit-type and XML-documentation conventions.

- [ ] **Step 4: Commit the formatter**

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

- [ ] **Step 1: Confirm the four target statements are unchanged**

Run:

```bash
rg -n 'Will use workflow:|Error came from prompt:|Filled tag .* with|Failed to process comfy workflow for inputs' src/BuiltinExtensions/ComfyUIBackend/ComfyUIAPIAbstractBackend.cs
git diff -- src/BuiltinExtensions/ComfyUIBackend/ComfyUIAPIAbstractBackend.cs
```

Expected: four existing raw-value statements and no uncommitted diff.

- [ ] **Step 2: Migrate all four statements with `apply_patch`**

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
Logs.Debug($"Failed to process comfy workflow for parameters {ComfyDiagnostics.DescribeParameters(user_input)} with workflow structure {ComfyDiagnostics.DescribeWorkflow(workflow)}");
```

The tag statement must remain inside the existing `Logs.MinimumLevel <= Logs.LogLevel.Verbose` guard. Do not change tag parsing, `filled`, escaping, workflow parsing/submission, the catch block, or any response log.

- [ ] **Step 3: Verify generation-path compatibility and redaction**

Run:

```bash
rg -n 'ComfyDiagnostics\.(DescribeWorkflow|DescribePromptEnvelope|DescribeParameters|DescribeNormalizedTagName)' src/BuiltinExtensions/ComfyUIBackend/ComfyUIAPIAbstractBackend.cs
if rg -n 'Will use workflow:|Error came from prompt:|Filled tag .* with .*filled|Failed to process comfy workflow for inputs' src/BuiltinExtensions/ComfyUIBackend/ComfyUIAPIAbstractBackend.cs; then exit 1; fi
rg -n 'ComfyUI prompt said:|PostJSONString\(.*prompt|return Utilities\.EscapeJsonString\(filled\)|catch \(Exception ex\)' src/BuiltinExtensions/ComfyUIBackend/ComfyUIAPIAbstractBackend.cs
git diff --check -- src/BuiltinExtensions/ComfyUIBackend/ComfyUIAPIAbstractBackend.cs
git diff -- src/BuiltinExtensions/ComfyUIBackend/ComfyUIAPIAbstractBackend.cs
```

Expected:

- Exactly four migrated diagnostic statement lines containing five formatter invocations appear in this file; the `GenerateLive` failure statement contains both `DescribeParameters` and `DescribeWorkflow`.
- The raw-value statement patterns are absent.
- The Comfy response diagnostic, submission, tag return value, exception boundary, and surrounding flow remain unchanged.
- The diff contains four statement replacements only.

- [ ] **Step 4: Commit the generation-path migration**

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

- [ ] **Step 1: Confirm the remaining three target statements are unchanged**

Run:

```bash
rg -n 'ComfyGetWorkflow for input:|Above is for prompt:|Following error relates to parameters:' \
  src/BuiltinExtensions/ComfyUIBackend/ComfyUIWebAPI.cs \
  src/BuiltinExtensions/ComfyUIBackend/ComfyUIRedirectHelper.cs \
  src/BuiltinExtensions/ComfyUIBackend/WorkflowGeneratorSteps.cs
git diff -- \
  src/BuiltinExtensions/ComfyUIBackend/ComfyUIWebAPI.cs \
  src/BuiltinExtensions/ComfyUIBackend/ComfyUIRedirectHelper.cs \
  src/BuiltinExtensions/ComfyUIBackend/WorkflowGeneratorSteps.cs
```

Expected: three existing raw-value statements and no uncommitted diff.

- [ ] **Step 2: Migrate all three statements with `apply_patch`**

Replace the preview statement with:

```csharp
Logs.Verbose($"ComfyGetWorkflow for parameters: {ComfyDiagnostics.DescribeParameters(input)}");
```

Replace the direct-proxy fallback statement with:

```csharp
Logs.Verbose($"Above is for prompt structure: {ComfyDiagnostics.DescribePromptEnvelope(parsed)}");
```

Replace the ControlNet statement with:

```csharp
Logs.Verbose($"Following error relates to parameters: {ComfyDiagnostics.DescribeParameters(g.UserInput)}");
```

Do not change the preview result, proxy routing, parsed prompt, ControlNet branching, or missing-image exception.

- [ ] **Step 3: Verify the three caller migrations**

Run:

```bash
rg -n 'ComfyDiagnostics\.(DescribePromptEnvelope|DescribeParameters)' \
  src/BuiltinExtensions/ComfyUIBackend/ComfyUIWebAPI.cs \
  src/BuiltinExtensions/ComfyUIBackend/ComfyUIRedirectHelper.cs \
  src/BuiltinExtensions/ComfyUIBackend/WorkflowGeneratorSteps.cs
if rg -n 'ComfyGetWorkflow for input:|Above is for prompt: .*ToDenseDebugString|Following error relates to parameters: .*ToJSON' \
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

- One formatter call appears in each file.
- The three raw-value expressions are absent.
- Preview, proxy failure context, and ControlNet exception text remain.
- Only one statement changes in each file.

- [ ] **Step 4: Commit the remaining caller migrations**

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

### Task 4: Review the complete implementation and hand off runtime validation

**Files:**
- Review: `src/BuiltinExtensions/ComfyUIBackend/ComfyDiagnostics.cs`
- Review: `src/BuiltinExtensions/ComfyUIBackend/ComfyUIAPIAbstractBackend.cs`
- Review: `src/BuiltinExtensions/ComfyUIBackend/ComfyUIWebAPI.cs`
- Review: `src/BuiltinExtensions/ComfyUIBackend/ComfyUIRedirectHelper.cs`
- Review: `src/BuiltinExtensions/ComfyUIBackend/WorkflowGeneratorSteps.cs`
- Modify: only a file above if review finds a verified defect

- [ ] **Step 1: Run an independent specification-compliance review**

Review the complete implementation range against the design and verify:

- exactly seven confirmed submitted-value statements migrated;
- all four original owners use the one internal formatter;
- only approved identifiers, counts, fixed markers, and validated connections are retained;
- direct graphs and prompt envelopes use distinct entry points, and direct graph nodes named `prompt` cannot be auto-unwrapped;
- connection recognition requires a two-element array, a non-negative 32-bit integer output index, and a syntactically valid source node present in the same graph;
- connection validation is structural rather than schema-backed and has no `object_info`, node-schema, global, or backend dependency;
- typed-parameter formatting reads keys without formatting values;
- tag formatting passes only `tagBasic` to `DescribeNormalizedTagName` and safely escapes it;
- invalid/malformed diagnostic inputs cannot throw or expose parser messages;
- all log levels and operational control flow remain unchanged;
- response/output/history diagnostics remain unchanged; and
- no unrelated behavior or file was added.

If the review finds a defect, correct it with `apply_patch`, repeat the affected task's static checks, commit the correction, and return it to the same reviewer.

- [ ] **Step 2: Run an independent code-quality and security review**

After specification approval, review for:

- accidental content retention through identifiers, arrays, exception text, `ToString`, or token reuse;
- mutation of source tokens;
- malformed/null input safety;
- Newtonsoft ownership/escaping correctness;
- C# explicit-type, braces, and XML-documentation conventions;
- unnecessary abstraction or generalized utility scope;
- diagnostic usefulness and log-level preservation; and
- exact commit/file scope.

Resolve all critical or important findings through the implementer/re-review loop before proceeding.

- [ ] **Step 3: Run final agent-permitted static verification**

Run:

```bash
target_files='src/BuiltinExtensions/ComfyUIBackend/ComfyDiagnostics.cs src/BuiltinExtensions/ComfyUIBackend/ComfyUIAPIAbstractBackend.cs src/BuiltinExtensions/ComfyUIBackend/ComfyUIWebAPI.cs src/BuiltinExtensions/ComfyUIBackend/ComfyUIRedirectHelper.cs src/BuiltinExtensions/ComfyUIBackend/WorkflowGeneratorSteps.cs'
rg -n 'ComfyDiagnostics\.(DescribeWorkflow|DescribePromptEnvelope|DescribeParameters|DescribeNormalizedTagName)' \
  src/BuiltinExtensions/ComfyUIBackend/ComfyUIAPIAbstractBackend.cs \
  src/BuiltinExtensions/ComfyUIBackend/ComfyUIWebAPI.cs \
  src/BuiltinExtensions/ComfyUIBackend/ComfyUIRedirectHelper.cs \
  src/BuiltinExtensions/ComfyUIBackend/WorkflowGeneratorSteps.cs
# Count one matched line for each of the seven migrated diagnostic statements.
test "$(rg -c 'ComfyDiagnostics\.(DescribeWorkflow|DescribePromptEnvelope|DescribeParameters|DescribeNormalizedTagName)' \
  src/BuiltinExtensions/ComfyUIBackend/ComfyUIAPIAbstractBackend.cs \
  src/BuiltinExtensions/ComfyUIBackend/ComfyUIWebAPI.cs \
  src/BuiltinExtensions/ComfyUIBackend/ComfyUIRedirectHelper.cs \
  src/BuiltinExtensions/ComfyUIBackend/WorkflowGeneratorSteps.cs | awk -F: '{ total += $2 } END { print total }')" = "7"
# Count all eight formatter invocations; the GenerateLive statement contains two.
test "$(rg -o 'ComfyDiagnostics\.(DescribeWorkflow|DescribePromptEnvelope|DescribeParameters|DescribeNormalizedTagName)' \
  src/BuiltinExtensions/ComfyUIBackend/ComfyUIAPIAbstractBackend.cs \
  src/BuiltinExtensions/ComfyUIBackend/ComfyUIWebAPI.cs \
  src/BuiltinExtensions/ComfyUIBackend/ComfyUIRedirectHelper.cs \
  src/BuiltinExtensions/ComfyUIBackend/WorkflowGeneratorSteps.cs | awk 'END { print NR }')" = "8"
if rg -n 'Filled tag .* with .*filled|Will use workflow: .*ToDenseDebugString|Error came from prompt: .*ToDenseDebugString|Failed to process comfy workflow for inputs|ComfyGetWorkflow for input:|Above is for prompt: .*ToDenseDebugString|Following error relates to parameters: .*ToJSON' \
  src/BuiltinExtensions/ComfyUIBackend/ComfyUIAPIAbstractBackend.cs \
  src/BuiltinExtensions/ComfyUIBackend/ComfyUIWebAPI.cs \
  src/BuiltinExtensions/ComfyUIBackend/ComfyUIRedirectHelper.cs \
  src/BuiltinExtensions/ComfyUIBackend/WorkflowGeneratorSteps.cs; then exit 1; fi
git diff --check
git status --short --branch --untracked-files=normal
git log --oneline --decorate -8
```

The first count verifies seven migrated diagnostic statement lines. The second verifies eight total formatter invocations because the `GenerateLive` failure statement combines the parameter and direct-workflow summaries.

Then determine the implementation base commit and verify the committed range changes exactly the five approved production files. Do not include the pre-existing dirty working-tree changes in that range check.

Do not run a build, automated test, browser, live server, launcher, installer, or backend/Comfy process.

- [ ] **Step 4: Hand off maintainer validation**

This is the required **Maintainer Validation** gate; agent-permitted static verification does not replace it.

Ask the maintainer to place recognizable sentinels near the start of API-key, positive/negative prompt, media, dynamic/default, extension-private, and direct-workflow values, then validate:

1. successful ordinary and stored/raw workflow generation at verbose level;
2. Comfy prompt validation and transport/processing failures at debug level;
3. generated-workflow preview;
4. direct-proxy prompt fallback;
5. ControlNet strength without a usable ControlNet/init image;
6. malformed workflow diagnostic fallback; and
7. supported verbose/debug log-level combinations.

Confirm no sentinel, filled value, submitted scalar, or base64 prefix appears; every node ID/class/input/connection and parameter name/count remains useful; normalized tag names contain no raw default/suffix; fixed malformed-input markers are safe; and all operational behavior and the ControlNet error are unchanged.

Do not claim runtime completion until the maintainer reports these scenarios pass. If validation fails, collect the exact path, log level, structural summary, leaked/missing field, operational result, and reproduction steps before proposing a correction.

**Rollback:** if a migrated sink leaks content or its formatter cannot safely describe the encountered shape, replace that sink with a fixed value-free message while correcting the shared formatter. Never restore raw filled tags, workflows, prompts, typed inputs, base64 content, or parser exception text as the accepted fallback.
