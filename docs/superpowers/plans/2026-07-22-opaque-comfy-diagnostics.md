# Opaque Comfy Diagnostics Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace user-controlled Comfy diagnostic identifiers with opaque graph aliases and sanitize every confirmed valid-JSON private-data failure while preserving successful behavior.

**Architecture:** `ComfyDiagnostics` will emit only ordinal node/input aliases, counts, fixed JSON-kind/status markers, and aliased source edges parsed without date coercion. `ComfySubmittedJson` remains the narrow malformed-JSON boundary; source owners add fixed, inner-free handling for failures that occur while interpreting syntactically valid private data. Backend-response parsers and generic API/T2I prerequisite `S1` remain unchanged.

**Tech Stack:** C# 12, .NET 8, Newtonsoft.Json 13, existing SwarmUI logging, Comfy workflow JSON, `T2IParamInput`.

**Execution context:** Work directly on `master` as requested by maintainer Reaper176. Do not create a worktree. Preserve the existing modified files and untracked `Data.pre-restore-2026-07-19/`; never inspect or stage that directory.

**Repository verification constraint:** `AGENTS.md` prohibits agents from running builds, automated tests, browsers, Comfy, backends, or the live application. Use static source tracing, exact-count assertions, diff/whitespace checks, independent reviews, and maintainer-run sentinel validation.

**Starting state:** The earlier implementation through `cb207936` remains committed. Final review found one Critical, four Important, and two Minor gaps. The approved second-revision design is committed in `76b35098` and corrected count in `39fe7304`. This plan implements only that second revision; do not repeat earlier parser migrations.

---

### Task 1: Rewrite the formatter as an opaque graph summary

**Files:**
- Modify: `src/BuiltinExtensions/ComfyUIBackend/ComfyDiagnostics.cs`
- Reference: `docs/superpowers/specs/2026-07-21-redact-comfy-workflow-diagnostics-design.md`

- [ ] **Step 1: Confirm the formatter is clean and inventory current callers**

Run:

```bash
git status --short --branch --untracked-files=normal
git diff -- src/BuiltinExtensions/ComfyUIBackend/ComfyDiagnostics.cs
rg -n 'ComfyDiagnostics\.(DescribeWorkflow|DescribePromptEnvelope|DescribeParameters|DescribeNormalizedTagName|DescribeException)' src/BuiltinExtensions/ComfyUIBackend --glob '*.cs'
```

Expected: no uncommitted formatter diff; current callers still total ten protected statement lines and eleven formatter calls before this revision.

- [ ] **Step 2: Replace the formatter with the complete opaque implementation**

Use `apply_patch` to replace `src/BuiltinExtensions/ComfyUIBackend/ComfyDiagnostics.cs` with:

```csharp
using System.Globalization;
using System.IO;
using FreneticUtilities.FreneticExtensions;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using SwarmUI.Text2Image;

namespace SwarmUI.Builtin_ComfyUIBackend;

/// <summary>Builds opaque, value-free diagnostic descriptions for Comfy workflows, typed inputs, and exceptions.</summary>
internal static class ComfyDiagnostics
{
    /// <summary>Describes a raw direct workflow graph without reproducing user-controlled lexemes.</summary>
    public static string DescribeWorkflow(string workflow)
    {
        if (string.IsNullOrWhiteSpace(workflow))
        {
            return FixedStatus("empty-workflow");
        }
        JToken parsed;
        try
        {
            parsed = ParseWithoutDateCoercion(workflow);
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

    /// <summary>Describes a raw Comfy prompt envelope without reproducing user-controlled lexemes.</summary>
    public static string DescribePromptEnvelope(string envelope)
    {
        if (string.IsNullOrWhiteSpace(envelope))
        {
            return FixedStatus("empty-prompt-envelope");
        }
        JToken parsed;
        try
        {
            parsed = ParseWithoutDateCoercion(envelope);
        }
        catch
        {
            return FixedStatus("invalid-prompt-envelope-json");
        }
        if (parsed is not JObject root || root["prompt"] is not JObject graph)
        {
            return FixedStatus("invalid-prompt-envelope-shape");
        }
        try
        {
            return DescribeGraph(graph);
        }
        catch
        {
            return FixedStatus("unavailable-prompt-envelope-summary");
        }
    }

    /// <summary>Describes a typed-input collection by count without reading its names or values.</summary>
    public static string DescribeParameters(T2IParamInput input)
    {
        try
        {
            if (input?.InternalSet?.ValuesInput is null)
            {
                return FixedStatus("unavailable-parameter-summary");
            }
            JObject summary = new()
            {
                ["parameter_count"] = input.InternalSet.ValuesInput.Count
            };
            return summary.ToString(Formatting.None);
        }
        catch
        {
            return FixedStatus("unavailable-parameter-summary");
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

    /// <summary>Parses JSON for diagnostics without converting ISO-looking strings to date tokens.</summary>
    private static JToken ParseWithoutDateCoercion(string input)
    {
        using StringReader stringReader = new(input);
        using JsonTextReader reader = new(stringReader)
        {
            DateParseHandling = DateParseHandling.None
        };
        return JToken.ReadFrom(reader);
    }

    /// <summary>Describes a parsed direct workflow graph with deterministic aliases and no source lexemes.</summary>
    private static string DescribeGraph(JObject graph)
    {
        List<JProperty> nodeProperties = [.. graph.Properties()];
        Dictionary<string, string> nodeAliases = [];
        HashSet<string> validNodeIds = [];
        for (int i = 0; i < nodeProperties.Count; i++)
        {
            JProperty property = nodeProperties[i];
            nodeAliases[property.Name] = $"node_{i + 1}";
            if (property.Value is JObject node && node["class_type"]?.Type == JTokenType.String && node["inputs"] is JObject)
            {
                validNodeIds.Add(property.Name);
            }
        }
        JArray nodes = [];
        for (int i = 0; i < nodeProperties.Count; i++)
        {
            JProperty nodeProperty = nodeProperties[i];
            JObject nodeSummary = new()
            {
                ["node"] = nodeAliases[nodeProperty.Name]
            };
            JArray inputsSummary = [];
            if (nodeProperty.Value is not JObject node)
            {
                nodeSummary["status"] = "invalid-node-shape";
            }
            else
            {
                nodeSummary["status"] = node["class_type"]?.Type != JTokenType.String
                    ? "invalid-class-type"
                    : node["inputs"] is not JObject ? "invalid-inputs-shape" : "valid-node-shape";
                if (node["inputs"] is JObject inputs)
                {
                    int inputIndex = 0;
                    foreach (JProperty inputProperty in inputs.Properties())
                    {
                        inputIndex++;
                        inputsSummary.Add(DescribeInput(inputProperty.Value, inputIndex, validNodeIds, nodeAliases));
                    }
                }
            }
            nodeSummary["input_count"] = inputsSummary.Count;
            nodeSummary["inputs"] = inputsSummary;
            nodes.Add(nodeSummary);
        }
        JObject summary = new()
        {
            ["node_count"] = nodes.Count,
            ["nodes"] = nodes
        };
        return summary.ToString(Formatting.None);
    }

    /// <summary>Describes one workflow input with an ordinal alias and no source lexemes.</summary>
    private static JObject DescribeInput(JToken input, int inputIndex, HashSet<string> validNodeIds, Dictionary<string, string> nodeAliases)
    {
        JObject summary = new()
        {
            ["input"] = $"input_{inputIndex}"
        };
        if (TryGetConnectionSourceAlias(input, validNodeIds, nodeAliases, out string sourceAlias))
        {
            summary["kind"] = "connection";
            summary["source_node"] = sourceAlias;
        }
        else
        {
            summary["kind"] = $"redacted:{GetValueKind(input)}";
        }
        return summary;
    }

    /// <summary>Recognizes a structurally valid connection and returns only its generated source alias.</summary>
    private static bool TryGetConnectionSourceAlias(JToken input, HashSet<string> validNodeIds, Dictionary<string, string> nodeAliases, out string sourceAlias)
    {
        sourceAlias = null;
        if (input is not JArray array || array.Count != 2)
        {
            return false;
        }
        JToken sourceToken = array[0];
        JToken outputToken = array[1];
        if (sourceToken is null
            || (sourceToken.Type != JTokenType.String && sourceToken.Type != JTokenType.Integer)
            || outputToken?.Type != JTokenType.Integer
            || !int.TryParse(outputToken.ToString(Formatting.None), NumberStyles.Integer, CultureInfo.InvariantCulture, out int outputIndex)
            || outputIndex < 0)
        {
            return false;
        }
        string sourceNode = sourceToken.Type == JTokenType.String ? sourceToken.Value<string>() : sourceToken.ToString(Formatting.None);
        if (!validNodeIds.Contains(sourceNode))
        {
            return false;
        }
        sourceAlias = nodeAliases[sourceNode];
        return true;
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

Do not retain a parsed-token prompt overload, `DescribeNormalizedTagName`, raw identifiers, class types, input names, parameter names, output indexes, hashes, lengths, or schema/backend dependencies.

- [ ] **Step 3: Statically inspect the value boundary and topology behavior**

Run:

```bash
if rg -n 'parameter_names|DescribeNormalizedTagName|class_type\"] =|nodes\[nodeProperty\.Name\]|inputsSummary\[inputProperty\.Name\]|output_index|new JValue\(tagName|DescribePromptEnvelope\(JToken' src/BuiltinExtensions/ComfyUIBackend/ComfyDiagnostics.cs; then exit 1; fi
rg -n 'DateParseHandling\.None|node_[{]|input_[{]|parameter_count|source_node|redacted:|valid-node-shape|invalid-class-type|TryGetConnectionSourceAlias' src/BuiltinExtensions/ComfyUIBackend/ComfyDiagnostics.cs
git diff --check -- src/BuiltinExtensions/ComfyUIBackend/ComfyDiagnostics.cs
git diff -- src/BuiltinExtensions/ComfyUIBackend/ComfyDiagnostics.cs
```

Expected: source identifiers appear only as internal dictionary/set keys; output contains generated aliases, counts, fixed statuses/kinds, and aliased source edges only. Date coercion is disabled for both string entry points.

- [ ] **Step 4: Commit the opaque formatter**

Run:

```bash
git add src/BuiltinExtensions/ComfyUIBackend/ComfyDiagnostics.cs
git diff --cached --check
test "$(git diff --cached --name-only)" = "src/BuiltinExtensions/ComfyUIBackend/ComfyDiagnostics.cs"
git commit -m "fix: make Comfy diagnostics opaque"
```

### Task 2: Migrate fixed tag and submitted-request diagnostics

**Files:**
- Modify: `src/BuiltinExtensions/ComfyUIBackend/ComfyUIAPIAbstractBackend.cs:895-900`
- Modify: `src/BuiltinExtensions/ComfyUIBackend/ComfyUIRedirectHelper.cs:328-405`
- Modify: `src/BuiltinExtensions/ComfyUIBackend/ComfyUser.cs:126-145`

- [ ] **Step 1: Replace the tag diagnostic with fixed text**

Use `apply_patch` to replace the guarded tag statement with:

```csharp
Logs.Verbose("Filled workflow tag with redacted name and value.");
```

Keep the existing `Logs.MinimumLevel` guard and tag result unchanged.

- [ ] **Step 2: Preserve the original direct-prompt string for diagnostics**

Use `apply_patch` to decode once:

```csharp
string promptText = StringConversionHelper.UTF8Encoding.GetString(data);
JObject parsed = ComfySubmittedJson.ParseObject(promptText);
```

Replace the fallback summary with:

```csharp
Logs.Verbose($"Above is for opaque prompt structure: {ComfyDiagnostics.DescribePromptEnvelope(promptText)}");
```

Replace the catch declaration and log with:

```csharp
catch (Exception)
{
    Logs.Debug("ComfyUI redirection failed - submitted prompt processing failed (content redacted).");
}
```

Do not change parsed routing, `Utilities.JSONContent(parsed)`, fallback, locking, or response behavior.

- [ ] **Step 3: Make the browser WebSocket failure diagnostic fixed**

Replace only its catch with:

```csharp
catch (Exception)
{
    Logs.Error("Failed to process ComfyUI user message (content redacted).");
}
```

Keep `rawText` only as the parser input and preserve `NewMessageToServers` after the catch.

- [ ] **Step 4: Verify eight formatter invocations and caller compatibility**

Run:

```bash
test "$(rg -o 'ComfyDiagnostics\.(DescribeWorkflow|DescribePromptEnvelope|DescribeParameters|DescribeException)' src/BuiltinExtensions/ComfyUIBackend/ComfyUIAPIAbstractBackend.cs src/BuiltinExtensions/ComfyUIBackend/ComfyUIWebAPI.cs src/BuiltinExtensions/ComfyUIBackend/ComfyUIRedirectHelper.cs src/BuiltinExtensions/ComfyUIBackend/ComfyUser.cs src/BuiltinExtensions/ComfyUIBackend/WorkflowGeneratorSteps.cs | wc -l)" = "8"
if rg -n 'DescribeNormalizedTagName|DescribePromptEnvelope\(parsed\)|ComfyUI redirection failed.*DescribeException|Failed to parse ComfyUI user message.*DescribeException|Failed to process ComfyUI user message.*rawText' src/BuiltinExtensions/ComfyUIBackend --glob '*.cs'; then exit 1; fi
rg -n 'Filled workflow tag with redacted name and value|DescribePromptEnvelope\(promptText\)|submitted prompt processing failed|Failed to process ComfyUI user message|NewMessageToServers' src/BuiltinExtensions/ComfyUIBackend/ComfyUIAPIAbstractBackend.cs src/BuiltinExtensions/ComfyUIBackend/ComfyUIRedirectHelper.cs src/BuiltinExtensions/ComfyUIBackend/ComfyUser.cs
git diff --check -- src/BuiltinExtensions/ComfyUIBackend/ComfyUIAPIAbstractBackend.cs src/BuiltinExtensions/ComfyUIBackend/ComfyUIRedirectHelper.cs src/BuiltinExtensions/ComfyUIBackend/ComfyUser.cs
```

- [ ] **Step 5: Commit the diagnostic callers**

```bash
git add src/BuiltinExtensions/ComfyUIBackend/ComfyUIAPIAbstractBackend.cs src/BuiltinExtensions/ComfyUIBackend/ComfyUIRedirectHelper.cs src/BuiltinExtensions/ComfyUIBackend/ComfyUser.cs
git diff --cached --check
test "$(git diff --cached --name-only | wc -l)" = "3"
git commit -m "fix: remove Comfy diagnostic identifiers"
```

### Task 3: Sanitize workflow tag processing and backend validation

**Files:**
- Modify: `src/BuiltinExtensions/ComfyUIBackend/ComfyUIAPIAbstractBackend.cs:802-903`
- Modify: `src/BuiltinExtensions/ComfyUIBackend/ComfyUIAPIAbstractBackend.cs:1009-1030`

- [ ] **Step 1: Add a fixed failure boundary around raw/stored tag filling**

Use `apply_patch` to add `try` immediately inside the raw-workflow branch before `Logs.Verbose("Will fill a workflow...");`, retain the complete existing fill logic inside it, and add this catch immediately after `Logs.Verbose("Workflow filled.");`:

```csharp
catch (Exception)
{
    throw new SwarmUserErrorException("Failed to process custom workflow data (content redacted).");
}
```

The new exception must not include an inner exception. Do not alter any successful tag lookup, default, suffix, seed, model, media, boolean, list, substitution, or escaping statement.

- [ ] **Step 2: Replace `TryIsValid` with shape-safe fixed refusal handling**

Use `apply_patch` to replace the method with:

```csharp
public static bool TryIsValid(T2IParamInput input, HashSet<string> nodeTypes)
{
    if (nodeTypes is null)
    {
        return true;
    }
    string workflowRaw = GetRawWorkflowFrom(input);
    if (string.IsNullOrWhiteSpace(workflowRaw))
    {
        return true;
    }
    try
    {
        workflowRaw = StringConversionHelper.QuickSimpleTagFiller(workflowRaw, "${", "}", (tag) =>
        {
            return "null";
        });
        JObject workflow = ComfySubmittedJson.ParseObject(workflowRaw);
        int nodeIndex = 0;
        foreach (JProperty nodeProperty in workflow.Properties())
        {
            nodeIndex++;
            if (nodeProperty.Value is not JObject node
                || node["class_type"]?.Type != JTokenType.String
                || !nodeTypes.Contains(node["class_type"].Value<string>()))
            {
                input.RefusalReasons.Add($"The custom workflow contains an unsupported or invalid node type at node_{nodeIndex} (identifier redacted).");
                return false;
            }
        }
        return true;
    }
    catch (JsonReaderException)
    {
        throw;
    }
    catch (Exception)
    {
        throw new SwarmUserErrorException("Custom workflow validation failed (content redacted).");
    }
}
```

This deliberately preserves a sanitized malformed-JSON exception while replacing other private preprocessing/interpretation failures. Do not inspect `inputs` shape or alter supported string class matching.

- [ ] **Step 3: Statically trace the two private failure boundaries**

Run:

```bash
if rg -n "Unknown param type request.*tagBasic|from '\{tag\}'|unsupported node type.*class_type|RefusalReasons.Add\(.*class_type" src/BuiltinExtensions/ComfyUIBackend/ComfyUIAPIAbstractBackend.cs; then exit 1; fi
rg -n 'Failed to process custom workflow data|Custom workflow validation failed|unsupported or invalid node type at node_|catch \(JsonReaderException\)' src/BuiltinExtensions/ComfyUIBackend/ComfyUIAPIAbstractBackend.cs
git diff --check -- src/BuiltinExtensions/ComfyUIBackend/ComfyUIAPIAbstractBackend.cs
git diff -- src/BuiltinExtensions/ComfyUIBackend/ComfyUIAPIAbstractBackend.cs
```

Expected: private exceptions are discarded; fixed replacements have no inner exception; successful filler statements are unchanged; only ordinal node aliases reach refusal reasons.

- [ ] **Step 4: Commit workflow failure sanitization**

```bash
git add src/BuiltinExtensions/ComfyUIBackend/ComfyUIAPIAbstractBackend.cs
git diff --cached --check
test "$(git diff --cached --name-only)" = "src/BuiltinExtensions/ComfyUIBackend/ComfyUIAPIAbstractBackend.cs"
git commit -m "fix: sanitize Comfy workflow validation failures"
```

### Task 4: Sanitize metadata and stored-workflow conversion failures

**Files:**
- Modify: `src/BuiltinExtensions/ComfyUIBackend/ComfyUIBackendExtension.cs:174-267`
- Modify: `src/BuiltinExtensions/ComfyUIBackend/ComfyUIBackendExtension.cs:311-348`

- [ ] **Step 1: Replace the dynamic metadata catch**

Replace:

```csharp
catch (Exception e)
{
    Logs.Error($"Error generating dynamic Comfy param {name}: {e}");
}
```

with:

```csharp
catch (Exception)
{
    Logs.Error("Error processing dynamic Comfy parameter metadata (content redacted).");
}
```

Keep permission checking, cache lookup, `T2IParamType.FromNet`, cleaners, and the final `return null` unchanged.

- [ ] **Step 2: Replace the stored-workflow catch**

Replace:

```csharp
catch (Exception ex)
{
    Logs.Error($"Error loading ComfyUI custom workflow '{name}': {ex.ReadableString()}");
    return null;
}
```

with:

```csharp
catch (Exception)
{
    Logs.Error("Error loading ComfyUI custom workflow (submitted content redacted).");
    return null;
}
```

Keep file reading, parsing, extraction, `enable_in_simple` conversion, caching, and success behavior unchanged.

- [ ] **Step 3: Verify valid-JSON conversion errors cannot expose content**

Run:

```bash
if rg -n 'Error generating dynamic Comfy param.*\{name\}|Error generating dynamic Comfy param.*\{e\}|Error loading ComfyUI custom workflow.*\{name\}|Error loading ComfyUI custom workflow.*ReadableString' src/BuiltinExtensions/ComfyUIBackend/ComfyUIBackendExtension.cs; then exit 1; fi
rg -n 'Error processing dynamic Comfy parameter metadata \(content redacted\)|Error loading ComfyUI custom workflow \(submitted content redacted\)|T2IParamType\.FromNet|enableInSimpleTok\.ToObject<bool>' src/BuiltinExtensions/ComfyUIBackend/ComfyUIBackendExtension.cs
git diff --check -- src/BuiltinExtensions/ComfyUIBackend/ComfyUIBackendExtension.cs
git diff -- src/BuiltinExtensions/ComfyUIBackend/ComfyUIBackendExtension.cs
```

- [ ] **Step 4: Commit the conversion-failure diagnostics**

```bash
git add src/BuiltinExtensions/ComfyUIBackend/ComfyUIBackendExtension.cs
git diff --cached --check
test "$(git diff --cached --name-only)" = "src/BuiltinExtensions/ComfyUIBackend/ComfyUIBackendExtension.cs"
git commit -m "fix: redact Comfy private conversion failures"
```

### Task 5: Reconcile design, plan, and architecture audit

**Files:**
- Modify: `docs/superpowers/specs/2026-07-21-redact-comfy-workflow-diagnostics-design.md`
- Modify: `docs/superpowers/audits/2026-07-21-maintainability-architecture-refresh.md`
- Review: `docs/superpowers/plans/2026-07-22-opaque-comfy-diagnostics.md`

- [ ] **Step 1: Record the implemented opaque boundary**

Use `apply_patch` to:

- set design status to `Implemented, awaiting maintainer validation`;
- retain historical nine/ten/eleven counts while stating the final target is ten statements and eight formatter calls;
- update F25, its risk-register row, and rank 2 with ordinal aliases, omitted semantic identifiers/output indexes, date-neutral diagnostics, parameter-count-only output, fixed tag text, and fixed valid-JSON private conversion failures;
- record the Critical/Important review paths as corrected evidence rather than deleting their history;
- include the explicit `TryIsValid` malformed raw/stored validation exercise in the audit's maintainer-validation record; and
- preserve `S1` identity, evidence, ordering, risk/disposition, Grid trace, and unchanged production scope.

Do not renumber findings or roadmap entries, claim live validation, or rewrite unrelated audit sections.

- [ ] **Step 2: Verify documentation consistency**

Run:

```bash
rg -n 'Status:|opaque|node_[0-9]|input_[0-9]|DateParseHandling|parameter count|tag.*redacted|TryIsValid|enable_in_simple|ten.*eight|S1' docs/superpowers/specs/2026-07-21-redact-comfy-workflow-diagnostics-design.md docs/superpowers/audits/2026-07-21-maintainability-architecture-refresh.md docs/superpowers/plans/2026-07-22-opaque-comfy-diagnostics.md
if rg -n 'every expected node ID|parameter names remain|normalized tag identifier remains|complete node topology.*output index|non-parser exceptions retain their prior readable detail' docs/superpowers/specs/2026-07-21-redact-comfy-workflow-diagnostics-design.md docs/superpowers/audits/2026-07-21-maintainability-architecture-refresh.md; then exit 1; fi
git diff --check -- docs/superpowers/specs/2026-07-21-redact-comfy-workflow-diagnostics-design.md docs/superpowers/audits/2026-07-21-maintainability-architecture-refresh.md
```

- [ ] **Step 3: Commit the architecture record**

```bash
git add docs/superpowers/specs/2026-07-21-redact-comfy-workflow-diagnostics-design.md docs/superpowers/audits/2026-07-21-maintainability-architecture-refresh.md
git diff --cached --check
test "$(git diff --cached --name-only | wc -l)" = "2"
git commit -m "docs: record opaque Comfy diagnostics"
```

### Task 6: Complete whole-project review and maintainer handoff

**Files:**
- Review: the eight approved Comfy production files
- Review: the design, this plan, and the architecture audit
- Modify: only an approved file if review proves a defect

- [ ] **Step 1: Run independent specification review**

Review the complete Comfy range and prove:

- ten protected statements contain eight formatter calls;
- all 12 parser calls remain behind `ComfySubmittedJson`;
- formatter output contains no source lexeme and only aliases/counts/statuses/kinds/edges;
- date-neutral parsing preserves ISO-ID connectivity;
- tag, class/refusal, metadata conversion, stored conversion, direct prompt, and browser WebSocket failures are fixed and value-free;
- successful behavior and all named control-flow constraints remain;
- backend-response and `S1` production paths remain unchanged; and
- documentation matches source without claiming maintainer validation.

Correct and re-review every Critical or Important finding before continuing.

- [ ] **Step 2: Run independent code-quality/security review**

Review for alternate source-lexeme paths, exception `InnerException`/`Data` retention, malformed shapes, connection spoofing, date coercion, valid-input evaluation changes, C# conventions, unnecessary scope, and exact committed files. Correct and re-review every Critical or Important finding.

- [ ] **Step 3: Run final agent-permitted static verification**

Run:

```bash
test "$(rg -o 'ComfyDiagnostics\.(DescribeWorkflow|DescribePromptEnvelope|DescribeParameters|DescribeException)' src/BuiltinExtensions/ComfyUIBackend/ComfyUIAPIAbstractBackend.cs src/BuiltinExtensions/ComfyUIBackend/ComfyUIWebAPI.cs src/BuiltinExtensions/ComfyUIBackend/ComfyUIRedirectHelper.cs src/BuiltinExtensions/ComfyUIBackend/ComfyUser.cs src/BuiltinExtensions/ComfyUIBackend/WorkflowGeneratorSteps.cs | wc -l)" = "8"
test "$(rg -o 'ComfySubmittedJson\.(ParseObject|UnescapeString)' src/BuiltinExtensions/ComfyUIBackend/ComfyUIAPIAbstractBackend.cs src/BuiltinExtensions/ComfyUIBackend/ComfyUIBackendExtension.cs src/BuiltinExtensions/ComfyUIBackend/ComfyUIWebAPI.cs src/BuiltinExtensions/ComfyUIBackend/ComfyUIRedirectHelper.cs src/BuiltinExtensions/ComfyUIBackend/ComfyUser.cs | wc -l)" = "12"
if rg -n 'DescribeNormalizedTagName|parameter_names|output_index|Failed to parse ComfyUI user message.*rawText|Error generating dynamic Comfy param.*\{(name|e)\}|Error loading ComfyUI custom workflow.*(ReadableString|\{name\})|RefusalReasons.Add\(.*class_type' src/BuiltinExtensions/ComfyUIBackend --glob '*.cs'; then exit 1; fi
rg -n 'DateParseHandling\.None|node_[{]|input_[{]|source_node|parameter_count|content redacted|identifier redacted' src/BuiltinExtensions/ComfyUIBackend --glob '*.cs'
git diff --check
git status --short --branch --untracked-files=normal
git log --oneline --decorate -16
```

Then verify the committed correction range changes only approved production and documentation files. Do not include protected dirty working-tree files, run builds/tests, or inspect the backup.

- [ ] **Step 4: Hand off maintainer sentinel validation**

Ask the maintainer to execute all cases in the second-revision design, especially literal and tag-expanded secrets in every structural position, unsupported/non-string/newline class types, unknown tag suffix/newline, valid dynamic metadata conversion failure, stored `enable_in_simple` conversion failure, malformed `TryIsValid` raw/stored workflows, and ISO-looking connected node IDs.

Do not claim completion until the maintainer confirms no sentinel/user lexeme appears and valid behavior plus opaque topology remain correct.

**Rollback:** a failing protected diagnostic may fall back to fixed value-free text. Never restore real identifiers, class/input/parameter/tag names, output indexes, raw values/messages, conversion messages, parser previews, or content-bearing inner exceptions.
