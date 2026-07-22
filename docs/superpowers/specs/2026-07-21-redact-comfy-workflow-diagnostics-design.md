# Redact Comfy Workflow Diagnostics Design

**Date:** 2026-07-21

**Status:** Approved revision; submitted-JSON boundary expansion pending implementation

## Goal

Prevent SwarmUI's maintained Comfy integration from writing filled workflow values, complete submitted workflows/prompts, typed-input values, raw WebSocket messages, or submitted/private JSON fragments embedded in parser exceptions to server logs or downstream error surfaces while retaining complete graph topology and useful failure context.

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

A complete exception-flow trace found that sink-only formatting is insufficient. `Utilities.ParseToJson` embeds up to 256 cleaned characters of its input in a replacement `JsonReaderException`, and several Comfy-owned private-input parsers allow that exception to reach generic loggers before or after the nine local statements. One browser-to-Swarm WebSocket catch also logs the complete raw message directly. The expanded boundary therefore includes 12 parsing or unescaping invocations:

1. filled workflow parsing in `ComfyUIAPIAbstractBackend.AwaitJobLive`;
2. raw or stored workflow validation in `ComfyUIAPIAbstractBackend.TryIsValid`;
3. workflow-tag JSON unescaping in `ComfyUIAPIAbstractBackend.CreateWorkflow`;
4. submitted dynamic parameter metadata in `ComfyUIBackendExtension.ParameterMetadataCacheHelper`;
5. persisted private custom-workflow container parsing in `ComfyUIBackendExtension.GetWorkflowByName`;
6. the `workflow` field submitted to `ComfyUIWebAPI.ComfySaveWorkflow`;
7. the `prompt` field submitted to the same endpoint;
8. the `custom_params` field submitted to the same endpoint;
9. the `param_values` field submitted to the same endpoint;
10. direct-proxy prompt-body parsing in `ComfyUIRedirectHelper.ComfyBackendDirectHandler`;
11. direct-proxy interrupt-body parsing in the same handler; and
12. browser-to-Swarm Comfy WebSocket message parsing in `ComfyUser.RunClientReceiveTask`.

The stored custom-workflow container remains in scope because it originated as private user-authored content even though it is parsed from disk. Comfy backend responses—including `object_info`, backend WebSocket output, and returned metadata—have different provenance and remain outside this project. Generic WebAPI and T2I submitted-input disclosures found by the trace are a separate immediate follow-up project.

`AwaitJobLive` also logs the response returned by Comfy's `/prompt` endpoint. Output/history diagnostics log backend response or output data. Those are not confirmed submitted-input sinks and are outside this project.

## Chosen Architecture

Use two internal, stateless classes with separate responsibilities inside the Comfy integration:

- `ComfyDiagnostics` owns value-eliding workflow, typed-parameter, workflow-tag, and exception representations for logs.
- `ComfySubmittedJson` owns parsing of Comfy-maintained submitted/private JSON and prevents content-bearing parser exceptions from escaping their source boundary.

The formatter is pure:

- it does not mutate a supplied `JToken`, workflow string, or `T2IParamInput`;
- it has no mutable static state;
- it performs no logging itself;
- it returns strings for the existing log calls;
- its workflow, parameter, and tag representations never return submitted scalar values, and its exception representation redacts identified JSON parser content; and
- it catches malformed diagnostic input and returns a fixed value-free fallback rather than throwing or replacing the original request failure.

The formatter stays inside the Comfy integration. This project does not add Comfy-specific policy to `Utilities.cs` or introduce a general logging framework.

`ComfySubmittedJson.ParseObject(string)` returns the same `JObject` data as the existing object parsers for valid input. If `JObject.Parse` throws `JsonReaderException`, it throws a new `JsonReaderException` with the fixed message `JSON parsing failed (submitted content redacted).`. The replacement exception does not retain the original exception as an inner exception and does not include the source fragment, parser message, JSON path, or line details.

`ComfySubmittedJson.UnescapeString(string)` performs the existing workflow-tag JSON-string unescape through the same safe exception boundary. Both methods preserve non-parser exception types and details. The class performs no logging, has no mutable state, and does not change valid JSON or operational control flow.

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

This formatter remains defense-in-depth at local diagnostic sinks. It is not the primary propagation boundary because a content-bearing parser exception can cross into a generic logger that never calls `ComfyDiagnostics`. `ComfySubmittedJson` therefore removes submitted content at each maintained Comfy source boundary before the exception can propagate.

## Diagnostic Sink Migration

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

The browser-to-Swarm Comfy WebSocket catch also stops interpolating `rawText`. It retains fixed operation context and routes exception detail through `ComfyDiagnostics.DescribeException`. A malformed frame continues through the same post-catch forwarding behavior; only its diagnostic representation changes.

## Submitted-JSON Boundary Migration

All 12 identified private-input parsing or unescaping invocations migrate to `ComfySubmittedJson`. The migration changes only the parser entry point:

- valid inputs produce the same `JObject` or unescaped string;
- malformed JSON still fails at the same operation with `JsonReaderException`;
- existing catch locations and propagation remain unchanged;
- save fields are still all parsed before the workflow file is written;
- no malformed input is converted into a new response contract; and
- unrelated file I/O, cache, routing, validation, and transport errors retain their existing exception details.

The direct-prompt catch continues to use `ComfyDiagnostics.DescribeException`. The interrupt parser intentionally remains uncaught locally: the safe exception may follow its existing framework path because it no longer contains submitted content. The dynamic-parameter, stored-workflow, matching, and generation paths retain their existing generic or local logging; source-boundary sanitization makes the parser details safe before those consumers receive them.

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

Diagnostic formatting and parsing must not mask the error being diagnosed. The formatter therefore returns a fixed safe fallback for null, malformed, or unexpected data and does not expose parser exception messages. `DescribeException` special-cases a `JsonReaderException` anywhere in the linear inner chain, preserves `ReadableString()` for other exceptions, and catches its own formatting failures.

The submitted/private parser catches only `JsonReaderException`. It replaces that exception with a new exception of the same type and a fixed value-free message, deliberately omitting the original as an inner exception. This prevents later `ReadableString()`, exception interpolation, generic API logging, or framework error handling from recovering a source preview or native parser details. Other exceptions pass through unchanged.

The direct-workflow string entry point owns parsing and requires the parsed root to be a graph object. It never inspects or unwraps a `prompt` property. The prompt-envelope string entry point owns parsing and delegates successfully parsed data to the prompt-envelope token overload, which requires an object root with an object `prompt` graph. Both paths delegate only the explicitly selected graph to the private summary formatter. The formatter reads only graph property names, node objects, their string `class_type`, their `inputs` property names, and candidate two-element connection arrays. It does not recursively copy arbitrary values.

If a structural identifier is missing or has an unexpected JSON type, the summary uses a fixed marker. It does not fall back to raw serialization.

## Files and Ownership

- Existing implementation: `src/BuiltinExtensions/ComfyUIBackend/ComfyDiagnostics.cs`
- Create: `src/BuiltinExtensions/ComfyUIBackend/ComfySubmittedJson.cs`
- Modify: `src/BuiltinExtensions/ComfyUIBackend/ComfyUIAPIAbstractBackend.cs`
- Modify: `src/BuiltinExtensions/ComfyUIBackend/ComfyUIBackendExtension.cs`
- Modify: `src/BuiltinExtensions/ComfyUIBackend/ComfyUIWebAPI.cs`
- Modify: `src/BuiltinExtensions/ComfyUIBackend/ComfyUIRedirectHelper.cs`
- Modify: `src/BuiltinExtensions/ComfyUIBackend/ComfyUser.cs`
- Modify: `src/BuiltinExtensions/ComfyUIBackend/WorkflowGeneratorSteps.cs`

No Python, JavaScript, Razor, CSS, core utility, API contract, workflow schema, or extension-facing surface changes.

## Compatibility Requirements

- Preserve every targeted statement's current log level.
- Preserve useful operation/error context around the structural summary.
- Preserve all submitted workflow bytes and typed inputs.
- Preserve all exception types and request results.
- Preserve the `JsonReaderException` type and existing propagation for malformed submitted/private JSON while replacing content-bearing details with fixed text.
- Preserve direct-proxy routing and prompt handling.
- Preserve malformed browser WebSocket message forwarding after its diagnostic catch.
- Preserve valid custom-workflow save/load data and the rule that every submitted JSON field parses before writing.
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
- Redacting Comfy backend-response parsers solely because a backend might reflect submitted input; reflected-output trust is a separate concern.
- Addressing generic WebAPI request parsing, follow-up T2I WebSocket frames, dynamic media objects, successful-generation parameter logging, or other non-Comfy submitted-input findings. These are the next separate project.

## Static Verification

Repository policy prohibits agents from running builds, automated tests, browsers, Comfy, or the live application. Static verification will:

1. inventory the nine original diagnostic statements and 12 submitted/private parsing or unescaping invocations before and after;
2. prove the old raw tag, filled value, workflow/prompt, `T2IParamInput`, `ToJSON()`, two local parser `ex.ReadableString()` expressions, and browser WebSocket `rawText` interpolation are absent from the protected diagnostics;
3. prove all original diagnostic owners call `ComfyDiagnostics` and all submitted/private parser owners call `ComfySubmittedJson`;
4. prove `ComfySubmittedJson` catches `JsonReaderException`, throws a new exception of the same type with only the fixed message, and never retains the original as an inner exception;
5. trace downstream matching, generation, dynamic-parameter, stored-workflow, endpoint, redirect, and WebSocket error paths to verify the fixed exception is the only parser detail available to local and generic consumers;
6. trace every formatter branch and verify that only approved identifiers, non-negative 32-bit connection coordinates from syntactically valid source nodes, counts, and fixed markers reach the result;
7. verify retained identifiers are JSON-escaped and no raw scalar, nested value, parser exception text, or source JSON is appended;
8. verify valid source tokens, strings, saved data, and submission bytes are never mutated;
9. verify backend-response parser calls remain unchanged and generic WebAPI/T2I findings remain outside the production diff;
10. verify log levels, catch locations, exception type, successful parse results, save-before-write ordering, and surrounding control flow are unchanged;
11. run repository-permitted whitespace and diff checks; and
12. verify the implementation commit range contains only the two Comfy safety helpers, identified Comfy callers, and their approved documentation.

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
8. malformed saved-workflow `workflow`, `prompt`, `custom_params`, and `param_values` fields with distinct sentinels;
9. malformed dynamic workflow parameter metadata;
10. a malformed persisted custom-workflow container containing a sentinel;
11. a malformed workflow tag escape containing a sentinel;
12. a malformed direct interrupt body containing a sentinel;
13. a malformed browser-to-Swarm Comfy WebSocket message containing a sentinel; and
14. supported verbose/debug log-level combinations.

Confirm:

- no sentinel, filled tag value, submitted scalar, or base64 prefix appears;
- every malformed submitted/private JSON path exposes only `JSON parsing failed (submitted content redacted).` without its sentinel, source preview, native parser message, JSON path, or line detail, while non-parser exceptions retain their prior readable detail;
- every expected node ID, class type, input name, and valid connection edge remains visible;
- parameter names and counts remain visible without values;
- the normalized tag identifier remains visible without its raw default/suffix/value;
- invalid structures produce fixed safe markers;
- endpoint, failure, and operation context remains actionable;
- the existing ControlNet missing-image exception is unchanged;
- generation, preview, direct proxy, and failure behavior are otherwise unchanged; and
- valid workflow save/load, dynamic parameter, tag, interrupt, and WebSocket behavior is unchanged, including forwarding a malformed browser WebSocket frame after the existing catch.

Sentinels belong in values, not the structural identifiers that this design intentionally retains.

## Success Criteria

- All nine original submitted-value diagnostics use the shared safe formatter, normalized tag representation, or parser-exception redaction.
- All 12 maintained Comfy submitted/private JSON parsing or unescaping invocations use `ComfySubmittedJson`.
- No filled tag value, workflow scalar, prompt content, typed-input value, media/base64 prefix, or extension-defined value is added to logs by those paths.
- No raw WebSocket message, submitted JSON preview, native parser message, JSON path, or line detail escapes from the protected parsing boundaries.
- Complete node topology and parameter-name context remain available.
- Parser exception text is value-free, malformed JSON retains its `JsonReaderException` type and propagation, non-parser details are preserved, and diagnostic failures cannot replace the original operational error.
- No valid workflow, request, save/load, cache, tag, WebSocket, routing, preview, or ControlNet behavior changes.
- No external caller or extension migration is required.

## Risks and Rollback

The main risks are accidentally retaining a submitted value as structure, misclassifying a content array as a connection, confusing a direct graph's `prompt` node with an envelope, retaining parser content through an inner exception, changing malformed-input propagation, applying submitted-input policy to backend responses, throwing from the formatter during an existing failure, or removing non-parser diagnostic context. Provenance-specific formatter and parser entry points, construction of a new value-free parser exception without an inner exception, validation against syntactically valid same-graph source nodes with non-negative 32-bit indexes, construction of new summary objects, explicit backend-response exclusions, and preservation of non-parser details bound these risks.

Each caller migration is mechanically reversible. If a particular structural representation proves unusable, that sink may fall back to a fixed value-free message while the formatter is corrected. If a safe parser migration causes a compatibility problem, its valid-input behavior must be corrected without restoring a content-bearing exception path. Rollback must never restore raw filled values, workflows, prompts, typed inputs, raw WebSocket messages, or parser exception text as the accepted end state.
