# Redact Comfy Workflow Diagnostics Design

**Date:** 2026-07-21

**Status:** Implemented, awaiting maintainer validation

## Goal

Prevent SwarmUI's maintained Comfy integration from writing filled workflow values, complete submitted workflows/prompts, typed-input values, or submitted JSON fragments embedded in parser exceptions to server logs while retaining complete graph topology and useful failure context.

## Confirmed Diagnostic Boundary

Four maintained files own nine submitted-value diagnostic statements:

1. `ComfyUIAPIAbstractBackend.CreateWorkflow` logs the raw workflow tag and up to 512 characters of the filled value. `${stability_api_key}` is a concrete credential source; prompts, media, model names, defaults, and extension-defined parameters also pass through this path.
2. `ComfyUIAPIAbstractBackend.AwaitJobLive` logs the submitted workflow at verbose level.
3. The same method logs the submitted workflow again at debug level when Comfy's `/prompt` response contains an error.
4. `ComfyUIAPIAbstractBackend.GenerateLive` logs both `T2IParamInput` and the raw workflow after workflow-processing failure.
5. `ComfyUIRedirectHelper.ComfyBackendDirectHandler` logs an unredirected direct-proxy prompt at verbose level.
6. `ComfyUIWebAPI.ComfyGetGeneratedWorkflow` logs `T2IParamInput`; its current `ToString()` output includes every parameter value with per-value truncation.
7. The standard ControlNet step in `WorkflowGeneratorSteps` logs `T2IParamInput.ToJSON().ToDenseDebugString()` before throwing the missing-image error. Every parameter key is included, and scalar content—including base64 media prefixes—is retained up to the dense formatter's per-value limit.
8. The `GenerateLive` catch logs `JsonReaderException` through `ReadableString()` when `AwaitJobLive` rejects malformed direct-workflow JSON. `Utilities.ParseToJson` constructs that exception with the cleaned first 256 characters of the submitted workflow.
9. The direct-prompt parse catch in `ComfyUIRedirectHelper.ComfyBackendDirectHandler` also logs `JsonReaderException` through `ReadableString()`. Its `Utilities.ParseToJson` call embeds the cleaned first 256 characters of the submitted direct prompt.

These sinks cover stored/dynamic workflow generation, ordinary generated workflows, preview, prompt validation and processing errors, malformed direct-workflow parsing, direct Comfy proxy fallback and malformed direct-prompt parsing, and ControlNet validation. They are independent of the already-corrected browser `genericRequest` diagnostic.

`AwaitJobLive` also logs the response returned by Comfy's `/prompt` endpoint. Output/history diagnostics log backend response or output data. Those are not confirmed submitted-input sinks and are outside this project.

## Chosen Architecture

Add `src/BuiltinExtensions/ComfyUIBackend/ComfyDiagnostics.cs` containing one internal, stateless formatting class. It is the sole owner of value-eliding Comfy workflow, typed-parameter, workflow-tag, and parser-exception diagnostic representations.

The formatter is pure:

- it does not mutate a supplied `JToken`, workflow string, or `T2IParamInput`;
- it has no mutable static state;
- it performs no logging itself;
- it returns strings for the existing log calls;
- its workflow, parameter, and tag representations never return submitted scalar values, and its exception representation redacts identified JSON parser content; and
- it catches malformed diagnostic input and returns a fixed value-free fallback rather than throwing or replacing the original request failure.

The formatter stays inside the Comfy integration. This project does not add Comfy-specific policy to `Utilities.cs` or introduce a general logging framework.

## Workflow Structural Summary

The workflow formatter has explicit provenance-specific entry points. `DescribeWorkflow(string)` accepts only a direct graph object. `DescribePromptEnvelope(string)` and `DescribePromptEnvelope(JToken)` accept only a request envelope whose `prompt` property is the graph. The formatter never auto-detects or unwraps a direct graph based on a `prompt` property, so a direct graph node whose ID is `prompt` remains a node in the summary.

The summary includes every graph node without a node-count cap. For each node it retains:

- node ID;
- `class_type`;
- every input name; and
- source node ID and output index for a validated graph connection.

A value is a validated connection only when it is a two-element JSON array whose first element is a string or integer identifying a syntactically valid node in the same graph and whose second element is a non-negative integer representable by a 32-bit signed `int`. A syntactically valid source node must be an object with a string `class_type` and an object `inputs` property. This prevents malformed nodes, negative or oversized indexes, and arbitrary two-element content arrays from being reproduced as topology. Other arrays are treated as submitted values.

Connection validation is intentionally structural rather than schema-backed: it does not verify that the output index exists for the source node's class. The pure diagnostic formatter does not load Comfy `object_info`, node schemas, or other global/backend dependencies.

Every non-connection input becomes a fixed marker based only on its JSON kind, for example `redacted:string`, `redacted:integer`, `redacted:float`, `redacted:boolean`, `redacted:null`, `redacted:array`, or `redacted:object`. No string, number, boolean, nested property, array element, path, prompt, model name, seed, media data, or extension-defined value is copied.

Malformed node entries produce fixed structural markers such as `invalid-node-shape`. Invalid JSON, a non-object direct graph, or an envelope without an object `prompt` graph produces an entry-point-specific fixed status. Parser exception text is not included because it can contain source fragments or structural paths.

Retained identifiers are an explicit part of the approved diagnostic contract: node IDs, class types, and input names are structural metadata and remain visible. They must be serialized through Newtonsoft JSON from a newly constructed summary object so quotes, newlines, and other control characters are escaped. The existing `ToDenseDebugString` helper is not used to serialize retained property names because it does not provide this identifier-escaping contract.

Connection topology and node ordering remain diagnostic-only. The source graph and its property order are not modified.

## Typed-Parameter Summary

The parameter formatter reports:

- the total parameter count; and
- every `InternalSet.ValuesInput` parameter name.

It never calls `T2IParamInput.ToString()`, `ToJSON()`, `SimplifyParamVal`, or any value's `ToString()`. It does not include runtime value types, lengths, previews, hashes, or null/value state. Names are serialized through Newtonsoft JSON so control characters are escaped.

Parameter names are intentionally retained as structural metadata. Parameter values, including prompts, media, API keys, password material, model paths, and extension-defined private values, are always omitted.

## Workflow-Tag Summary

The tag-fill diagnostic retains only the normalized `tagBasic` identifier derived after unescaping and splitting the raw tag. It does not retain:

- the raw tag;
- a default value after `:`;
- a suffix after `+`; or
- any part of the filled value.

The normalized identifier is serialized through `DescribeNormalizedTagName` so it is safely escaped and the caller precondition is explicit in the method name. The log message states that the value was redacted. The existing `Logs.MinimumLevel` guard remains, avoiding formatting work when verbose logging is disabled.

## Exception Summary

`DescribeException` walks the supplied exception's linear `InnerException` chain. If any exception is a `JsonReaderException`, it returns the fixed value-free text `JSON parsing failed (submitted content redacted).` without reading or returning parser messages. This covers `Utilities.ParseToJson`, which replaces line feeds with two spaces, applies `CleanTrashTextForDebug` to strip invalid symbols and retain at most the first 256 cleaned characters, then constructs a new `JsonReaderException` containing that submitted preview plus the original parser message.

For non-parser exceptions, `DescribeException` preserves the existing `ReadableString()` result unchanged. A null exception returns `Unknown error.`, and any failure while formatting diagnostics returns `Error details unavailable.`. The helper has no state and never throws into the operational catch path.

## Sink Migration

All nine submitted-value statements migrate together:

1. The tag-fill statement uses the safe normalized tag summary and a fixed redacted-value message.
2. `AwaitJobLive` verbose submission logging uses the prompt-envelope structural summary.
3. `AwaitJobLive` prompt-error debug logging uses the same prompt-envelope structural summary.
4. `GenerateLive` failure logging combines the typed-parameter summary with the direct-workflow structural summary.
5. The `GenerateLive` catch routes exception details through `DescribeException`, redacting malformed-workflow parser content while retaining non-parser detail.
6. Direct-proxy fallback logging uses the parsed prompt-envelope structural summary.
7. The direct-prompt parse catch routes exception details through `DescribeException` before preserving its existing swallowed/fallback flow.
8. Generated-workflow preview logging uses the typed-parameter summary.
9. ControlNet missing-image logging uses the typed-parameter summary.

Existing log levels and equivalent surrounding context remain. The migrated wording must make clear that the output is structural/redacted rather than the submitted workflow or parameters.

The following behavior remains unchanged:

- workflow construction and JSON bytes;
- tag lookup, defaulting, substitution, and JSON escaping;
- Comfy `/prompt` submission and response processing;
- direct-proxy parsing, routing, and fallback;
- generated-workflow preview output;
- ControlNet branching and the readable missing-image exception;
- non-parser `ReadableString()` exception detail at the two migrated catch sites;
- exception propagation outside diagnostic formatting; and
- all non-targeted logs.

## Failure Behavior

Diagnostic formatting must not mask the error being diagnosed. The formatter therefore returns a fixed safe fallback for null, malformed, or unexpected data and does not expose parser exception messages. `DescribeException` special-cases a `JsonReaderException` anywhere in the linear inner chain, preserves `ReadableString()` for other exceptions, and catches its own formatting failures.

The direct-workflow string entry point owns parsing and requires the parsed root to be a graph object. It never inspects or unwraps a `prompt` property. The prompt-envelope string entry point owns parsing and delegates successfully parsed data to the prompt-envelope token overload, which requires an object root with an object `prompt` graph. Both paths delegate only the explicitly selected graph to the private summary formatter. The formatter reads only graph property names, node objects, their string `class_type`, their `inputs` property names, and candidate two-element connection arrays. It does not recursively copy arbitrary values.

If a structural identifier is missing or has an unexpected JSON type, the summary uses a fixed marker. It does not fall back to raw serialization.

## Files and Ownership

- Create: `src/BuiltinExtensions/ComfyUIBackend/ComfyDiagnostics.cs`
- Modify: `src/BuiltinExtensions/ComfyUIBackend/ComfyUIAPIAbstractBackend.cs`
- Modify: `src/BuiltinExtensions/ComfyUIBackend/ComfyUIWebAPI.cs`
- Modify: `src/BuiltinExtensions/ComfyUIBackend/ComfyUIRedirectHelper.cs`
- Modify: `src/BuiltinExtensions/ComfyUIBackend/WorkflowGeneratorSteps.cs`

No Python, JavaScript, Razor, CSS, core utility, API contract, workflow schema, or extension-facing surface changes.

## Compatibility Requirements

- Preserve every targeted statement's current log level.
- Preserve useful operation/error context around the structural summary.
- Preserve all submitted workflow bytes and typed inputs.
- Preserve all exception types and request results.
- Preserve direct-proxy routing and prompt handling.
- Preserve generated-workflow preview behavior.
- Preserve the ControlNet missing-image error and its trigger.
- Preserve optional-node and external-extension workflow compatibility.
- Retain complete topology for every node, including validated connection edges.
- Do not require external caller or extension migration.

## Non-Goals

- Redacting Comfy backend response, output, or history diagnostics without evidence that they contain submitted secrets.
- Changing log-level configuration or retention.
- Changing workflow generation, graph normalization, node order, IDs, inputs, or property order.
- Changing tag syntax, defaulting, dynamic parameter lookup, or supported tags.
- Changing `T2IParamInput`, `ToDenseDebugString`, or general JSON utilities.
- Adding a general-purpose redaction framework, configurable allowlist/denylist, or secret-name list.
- Capping the node topology summary.
- Addressing browser diagnostics, server endpoint error text, or unrelated logging findings.

## Static Verification

Repository policy prohibits agents from running builds, automated tests, browsers, Comfy, or the live application. Static verification will:

1. inventory the nine confirmed statements before and after;
2. prove the old raw tag, filled value, workflow/prompt, `T2IParamInput`, `ToJSON()`, and two parser `ex.ReadableString()` diagnostic expressions are absent from those paths;
3. prove all four existing owners call `ComfyDiagnostics`;
4. trace every formatter branch and verify that only approved identifiers, non-negative 32-bit connection coordinates from syntactically valid source nodes, counts, and fixed markers reach the result;
5. verify retained identifiers are JSON-escaped;
6. verify no raw scalar, nested value, parser exception text, or source JSON is appended;
7. verify source tokens and submission strings are never mutated;
8. verify log levels and surrounding request/error control flow are unchanged;
9. run repository-permitted whitespace and diff checks; and
10. verify the implementation commit range contains only the new formatter and four approved callers.

## Maintainer Validation

Use recognizable sentinel values near the beginning of each value so they would have appeared within the old truncation windows. Place sentinels in:

- Stability API key;
- positive and negative prompts;
- media/base64 content;
- dynamic workflow parameters and defaults;
- extension-defined private parameter values; and
- direct workflow input values.

Exercise:

1. successful ordinary and stored/raw workflow generation with verbose logging;
2. Comfy prompt validation failure and transport/processing failure with debug logging;
3. generated-workflow preview;
4. direct-proxy prompt fallback;
5. ControlNet strength enabled with neither a ControlNet image nor usable init/first image;
6. malformed raw workflow containing a sentinel within the first 256 characters;
7. malformed direct prompt containing a separate sentinel within the first 256 characters; and
8. supported verbose/debug log-level combinations.

Confirm:

- no sentinel, filled tag value, submitted scalar, or base64 prefix appears;
- both malformed JSON paths emit `JSON parsing failed (submitted content redacted).` without their sentinel or raw parser message, while non-parser exceptions retain their prior readable detail;
- every expected node ID, class type, input name, and valid connection edge remains visible;
- parameter names and counts remain visible without values;
- the normalized tag identifier remains visible without its raw default/suffix/value;
- invalid structures produce fixed safe markers;
- endpoint, failure, and operation context remains actionable;
- the existing ControlNet missing-image exception is unchanged; and
- generation, preview, direct proxy, and failure behavior are otherwise unchanged.

Sentinels belong in values, not the structural identifiers that this design intentionally retains.

## Success Criteria

- All nine confirmed submitted-value diagnostics use the shared safe formatter, normalized tag representation, or parser-exception redaction.
- No filled tag value, workflow scalar, prompt content, typed-input value, media/base64 prefix, or extension-defined value is added to logs by those paths.
- Complete node topology and parameter-name context remain available.
- Parser exception text is value-free, non-parser `ReadableString()` detail is preserved, and formatting failures cannot replace the original operational error.
- No workflow, request, exception, routing, preview, or ControlNet behavior changes.
- No external caller or extension migration is required.

## Risks and Rollback

The main risks are accidentally retaining a submitted value as structure, misclassifying a content array as a connection, confusing a direct graph's `prompt` node with an envelope, exposing parser content through an inner exception, throwing from the formatter during an existing failure, or removing non-parser diagnostic context. Provenance-specific entry points, validation against syntactically valid same-graph source nodes with non-negative 32-bit indexes, construction of a new summary object, linear inner-exception inspection, and preservation of non-parser `ReadableString()` output bound these risks.

Each caller migration is mechanically reversible. If a particular structural representation proves unusable, that sink may fall back to a fixed value-free message while the formatter is corrected. Rollback must never restore raw filled values, workflows, prompts, typed inputs, or parser exception text as the accepted end state.
