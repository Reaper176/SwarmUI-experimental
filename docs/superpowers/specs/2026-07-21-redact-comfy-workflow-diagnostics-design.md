# Redact Comfy Workflow Diagnostics Design

**Date:** 2026-07-21

**Second revision:** 2026-07-22

**Status:** Implemented, awaiting maintainer validation

## Goal

Prevent SwarmUI's maintained Comfy integration from writing any user-controlled workflow, parameter, tag, WebSocket, or private stored-data lexeme to protected logs or downstream error surfaces. Retain value-free operation categories, graph shape through deterministic aliases, and failure behavior where confidentiality permits.

## Confirmed Boundary and Review Findings

The original project identified nine submitted-value diagnostic statements across `ComfyUIAPIAbstractBackend`, `ComfyUIWebAPI`, `ComfyUIRedirectHelper`, and `WorkflowGeneratorSteps`. It added `ComfyDiagnostics` and migrated those statements. The browser-to-Swarm raw WebSocket statement in `ComfyUser` is additional to the original nine, producing ten protected statement lines and eleven formatter invocations after that migration.

A later propagation trace identified 12 maintained Comfy submitted/private parsing or unescaping invocations and introduced `ComfySubmittedJson`:

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
12. browser-to-Swarm Comfy WebSocket parsing in `ComfyUser.RunClientReceiveTask`.

Final security review showed that syntax-level parsing and a structural identifier allowlist are not sufficient:

- workflow tags are expanded over the complete JSON text, so API keys, prompts, media, or extension-private values can become node IDs, input names, class types, connection sources, or output indexes before diagnostic formatting;
- literal submitted secrets can be placed directly in the same identifier positions without using tags;
- syntactically valid dynamic metadata and stored workflow containers can expose scalar values through later conversion exceptions;
- unknown-tag errors can reproduce raw tags, suffixes, and decoded newlines;
- `TryIsValid` can serialize an unsupported or non-string `class_type` into refusal logs and responses; and
- Newtonsoft's default date coercion can convert ISO-looking connection source IDs and lose valid topology.

The root cause is architectural: every retained workflow or parameter identifier remains user-controlled, and errors arising after successful parsing can also contain private values. JSON position, token type, and escaping do not establish trusted provenance.

The stored custom-workflow container remains in scope because it originated as private user-authored content. Comfy backend responses—including `object_info`, backend WebSocket output, `/prompt` responses, output/history diagnostics, and returned metadata—remain excluded by provenance and unchanged. Reflected backend output is a separate trust concern.

## Chosen Architecture

Keep two internal, stateless classes with narrower contracts:

- `ComfyDiagnostics` produces opaque, value-free diagnostic representations. It may inspect user-controlled data to derive counts, JSON kinds, and graph relationships, but it never returns a source lexeme.
- `ComfySubmittedJson` preserves valid operational parsing while replacing `JsonReaderException` with a fresh same-type exception containing fixed text and no original inner exception.

Source owners additionally sanitize failures that occur after valid parsing. Parser sanitization and diagnostic formatting are defense-in-depth layers; neither substitutes for the other.

No Comfy-specific policy is added to `Utilities.cs`, no general logging framework is introduced, and no backend-response parser migrates.

## Opaque Workflow Structural Summary

`DescribeWorkflow(string)` accepts only a direct graph object. `DescribePromptEnvelope(string)` accepts only an envelope whose `prompt` property is the graph. Both string entry points parse with `JsonTextReader.DateParseHandling = DateParseHandling.None` so source identifiers retain their lexical string form for internal relationship matching, then advance the reader through the complete document so trailing non-comment content produces the fixed invalid-JSON status. A direct graph node named `prompt` is never auto-unwrapped.

The public parsed-token prompt-envelope overload is removed because no maintained caller remains. Quality review found that retaining and reparsing the decoded direct-proxy request duplicated operational parsing and unnecessarily extended the lifetime of raw submitted content. The implemented caller therefore uses the design's fixed-message rollback path for the direct-proxy fallback; operational parsing and routing continue to use the existing `JObject`.

The summary includes every node without a node-count cap. Nodes receive deterministic aliases from graph property order: `node_1`, `node_2`, and so on. Inputs receive per-node aliases from input property order: `input_1`, `input_2`, and so on. Raw node IDs and input names are used only inside the formatter and are never copied into the result.

Each node reports only:

- its ordinal alias;
- a fixed node-shape status;
- its input count; and
- an ordered list of aliased inputs.

The actual `class_type` is never returned. The summary reports only fixed markers such as `valid-node-shape`, `invalid-node-shape`, `invalid-class-type`, or `invalid-inputs-shape`.

A value is internally recognized as a connection only when it is a two-element array whose source is a string or integer matching a syntactically valid node in the same graph and whose output is a non-negative integer representable by a 32-bit signed `int`. A syntactically valid node must be an object with a string `class_type` and an object `inputs` property. A recognized connection reports only `connection` and the source node's ordinal alias. The source identifier and output index are never returned.

Non-connection inputs report fixed JSON-kind markers such as `redacted:string`, `redacted:integer`, `redacted:float`, `redacted:boolean`, `redacted:null`, `redacted:array`, or `redacted:object`. No scalar, nested property, array element, identifier, path, prompt, model name, seed, media data, or extension-defined value is copied.

Invalid JSON, incorrect roots, malformed nodes, and formatter failures produce fixed entry-point-specific status objects. The formatter constructs a new summary object and never mutates or reuses a supplied token.

This preserves graph-theoretic node order and source-edge relationships. It intentionally does not preserve semantic labels, class names, input names, or output ports because those fields are user-controlled.

## Typed-Parameter and Tag Summaries

`DescribeParameters(T2IParamInput)` returns only the total parameter count. It may enumerate `InternalSet.ValuesInput` to count entries but never returns, formats, hashes, or classifies a key or value. It does not call `T2IParamInput.ToString()`, `ToJSON()`, `SimplifyParamVal`, a parameter value's `ToString()`, or a parameter key's `ToString()`.

The tag-fill diagnostic becomes fixed text stating that a workflow tag name and value were redacted. It does not call `DescribeNormalizedTagName`, and that formatter method is removed when it has no maintained caller. Raw tags, normalized names, defaults, suffixes, decoded control characters, and filled values never enter the log.

## Submitted/Private Failure Boundary

`ComfySubmittedJson.ParseObject(string)` and `UnescapeString(string)` retain their current valid behavior. On `JsonReaderException`, each throws a fresh `JsonReaderException` with exactly `JSON parsing failed (submitted content redacted).` and no original inner exception, source fragment, parser message, JSON path, or line details. Non-parser exceptions from these two narrow parsing operations retain their existing behavior.

Failures arising while interpreting already-valid submitted/private data use fixed operation-specific handling:

- Raw/stored workflow tag filling is enclosed by a private-data boundary. Any exception from preprocessing, unescaping, lookup, conversion, substitution, or escaping becomes a fresh `SwarmUserErrorException` with fixed content-free text and no inner exception.
- `TryIsValid` keeps a sanitized `JsonReaderException` from malformed JSON. Other tag-preprocessing, shape, or interpretation failures become a fresh fixed `SwarmUserErrorException`. Unsupported or invalid nodes add a fixed refusal reason containing only the node ordinal alias, never the class token or identifier.
- Dynamic parameter metadata failures retain the existing caught-and-return-`null` behavior but log only fixed operation context. The parameter name and exception are omitted.
- Stored custom-workflow load failures retain the existing return-`null` behavior but log only fixed read/processing context. Workflow names, paths, tokens, and exception details are omitted.
- Direct-prompt failures retain the existing swallowed/fallback behavior but use a fixed submitted-data failure message for every exception type.
- Browser-to-Swarm Comfy WebSocket inspection failures use fixed operation context for every exception type and retain the existing post-catch forwarding behavior.

The direct-interrupt path continues to propagate its already-sanitized JSON parser exception. Its maintained post-parse operations do not convert submitted tokens through value-bearing exception messages. Save-field parsing, in-memory publication, parse-before-write behavior, body handling, prompt-ID mapping, routing, caching, and successful generation remain unchanged.

`ComfyDiagnostics.DescribeException` remains available for broad operational catches. It returns fixed text for a `JsonReaderException` anywhere in the linear inner chain and preserves existing non-parser `ReadableString()` detail only after private-data sources have sanitized their own interpretation failures. Private catches do not use this broad formatter when a valid submitted token could have caused the exception.

## Diagnostic Migration

The final ten protected statement lines use seven formatter calls. This preserves the historical sequence of nine original statements, ten protected lines, and eleven formatter invocations after the first migration while recording the second-revision outcome:

1. tag fill uses fixed name-and-value-redacted text;
2. `AwaitJobLive` verbose submission logging uses an opaque prompt-envelope summary;
3. its prompt-error debug logging uses the same summary;
4. `GenerateLive` failure logging combines the parameter count with an opaque direct-workflow summary;
5. its broad catch uses `DescribeException` after source sanitization;
6. direct-proxy fallback logging uses fixed value-free text because the raw prompt structure is not retained;
7. its catch uses fixed submitted-data failure text;
8. generated-workflow preview logging uses only the parameter count;
9. ControlNet missing-image logging uses only the parameter count; and
10. browser-to-Swarm WebSocket failure logging uses fixed submitted-data failure text.

Existing log levels remain. Wording states that summaries are opaque/redacted. No protected diagnostic contains a user-controlled lexeme.

The implemented private-data boundary also covers raw/stored tag processing and `TryIsValid`: malformed JSON retains the sanitized same-type parser exception, other private interpretation failures become fresh fixed errors, and unsupported/invalid nodes expose only an ordinal alias. Dynamic metadata and stored-workflow conversion failures use fixed logs, and the dormant metadata-log example that interpolated submitted names and metadata keys was removed rather than left as an unsafe future template.

## Files and Ownership

- Opaque diagnostic representation: `src/BuiltinExtensions/ComfyUIBackend/ComfyDiagnostics.cs`
- Safe JSON parser boundary: `src/BuiltinExtensions/ComfyUIBackend/ComfySubmittedJson.cs`
- Source and diagnostic owners: `src/BuiltinExtensions/ComfyUIBackend/ComfyUIAPIAbstractBackend.cs`, `src/BuiltinExtensions/ComfyUIBackend/ComfyUIBackendExtension.cs`, `src/BuiltinExtensions/ComfyUIBackend/ComfyUIWebAPI.cs`, `src/BuiltinExtensions/ComfyUIBackend/ComfyUIRedirectHelper.cs`, `src/BuiltinExtensions/ComfyUIBackend/ComfyUser.cs`, and `src/BuiltinExtensions/ComfyUIBackend/WorkflowGeneratorSteps.cs`

No Python, JavaScript, Razor, CSS, core utility, API schema, workflow schema, backend-response, or extension-facing change is included.

## Compatibility Requirements

- Preserve valid workflow bytes, tag substitution results, node/property order, request bodies, and typed inputs.
- Preserve target log levels and fixed operation categories.
- Preserve malformed-JSON `JsonReaderException` type and propagation after removing content-bearing details.
- Permit private post-parse failures to become fresh fixed `SwarmUserErrorException` or fixed local diagnostics where confidentiality requires it.
- Preserve direct-prompt swallowed/fallback flow and interrupt propagation.
- Preserve malformed browser WebSocket frame forwarding.
- Preserve custom-workflow save/load success behavior and parse-before-write ordering.
- Preserve dynamic parameter success behavior and caught failure returning `null`.
- Preserve generated-workflow preview, ControlNet branching, and the ControlNet missing-image error.
- Preserve optional-node and external-extension workflow execution compatibility.
- Retain node order and source-edge topology through ordinal aliases, including ISO-looking node IDs.
- Do not require external caller or extension migration.

The following diagnostic detail is intentionally removed: real node IDs, class types, input names, connection output indexes, parameter names, tag names, workflow names/paths in protected failure logs, and private-data exception messages. User-visible private workflow errors may become fixed content-redacted messages.

## Non-Goals

- Redacting Comfy backend responses, output, history, `object_info`, or returned metadata without separate reflected-data evidence.
- Changing log configuration or retention.
- Changing successful workflow generation, graph normalization, node order, IDs, inputs, property order, tag syntax, defaults, suffix behavior, dynamic parameter lookup, or supported tags.
- Changing `T2IParamInput`, `ToDenseDebugString`, or general JSON utilities.
- Adding a general redaction framework, configurable secret list, hashing, schema allowlist, or taint-tracking engine.
- Capping node or input summaries.
- Addressing generic WebAPI/T2I submitted-input findings covered by unranked prerequisite `S1`.

## Confirmed Separate Follow-Up (`S1`)

`S1`, **Redact generic server API/T2I submitted-input diagnostics**, remains separate and must be designed before ranked roadmap work resumes at rank 3. This Comfy revision does not modify:

- `src/WebAPI/API.cs:70`, initial WebSocket `ReceiveJson`;
- `src/WebAPI/API.cs:85-87`, initial HTTP `JObject.Parse`;
- `src/WebAPI/T2IAPI.cs:116-124`, follow-up generation WebSocket parsing;
- `src/Text2Image/T2IParamSet.cs:158-190`, dynamic media parsing reached through Grid's direct `T2IParamInput.Set` at `GridGenCore.cs:355`; or
- `src/WebAPI/T2IAPI.cs:316`, successful-generation `T2IParamInput.ToString()` logging.

The Grid malformed-media path remains `Task.Run` → fault rethrow → `ExToError`/`ReadableString()` logging → WebSocket error response. Ordinary `ApplyParameter`/`ValidateParam` rejects the malformed JSON-looking media first and is not the direct trigger. `S1` remains outside the unchanged 24 production plus eight measurement numbered roadmap entries.

## Static Verification

Repository policy prohibits agents from running builds, automated tests, browsers, Comfy, backends, or the live application. Static verification will:

1. inventory the ten protected statements, seven formatter invocations, and 12 submitted/private parser calls;
2. prove no protected diagnostic or private catch interpolates a source node ID, class type, input name, output index, parameter name/value, tag/name/suffix, workflow name/path, raw text/token, or private exception;
3. verify workflow summaries contain only ordinal aliases, counts, fixed statuses, JSON kinds, and aliased source edges;
4. verify connection matching uses raw identifiers only internally and never emits the raw source or output index;
5. verify diagnostic string parsing sets `DateParseHandling.None`, validates the complete document through EOF, and no direct-prompt diagnostic retains or reparses the original decoded string;
6. verify parameter formatting reads only the count and tag logging is fixed;
7. verify parser exceptions remain fresh, fixed, same-type, and inner-free;
8. trace tag filling, validation, dynamic metadata, stored workflow, direct prompt, interrupt, save, and browser WebSocket failure paths through local, generic, and framework consumers;
9. verify private post-parse failures cannot expose original messages or values;
10. verify log levels, save ordering, routing, prompt-ID mapping, frame forwarding, cache behavior, valid results, and ControlNet behavior remain unchanged;
11. verify backend-response parsers and all `S1` production paths remain unchanged;
12. run repository-permitted whitespace and committed-range checks over only the eight approved production files and three approved documents.

## Maintainer Validation

Use distinct sentinels in ordinary workflow values and in literal or tag-expanded structural positions. Exercise:

1. successful ordinary, raw, and stored workflow generation with verbose/debug logging;
2. node IDs, input names, class types, connection sources, and output indexes containing literal sentinels;
3. the same structural positions filled by `${stability_api_key}`, prompt, media, numeric, and extension-private tags;
4. prompt validation and transport/processing failures;
5. generated-workflow preview and ControlNet enabled without a usable ControlNet/init image;
6. malformed raw workflow parsing in `AwaitJobLive`;
7. malformed raw/stored workflow validation in `TryIsValid`;
8. unsupported string, newline-bearing, and non-string `class_type` values;
9. unknown tags containing suffix sentinels and decoded newlines;
10. valid JSON dynamic metadata whose later list/type conversion contains a sentinel;
11. a valid stored container whose `enable_in_simple` conversion contains a sentinel;
12. malformed saved-workflow fields, direct prompt, direct interrupt, tag escape, and browser WebSocket JSON;
13. an ISO-looking node ID used as a connection source; and
14. supported verbose/debug log-level combinations.

Confirm:

- no sentinel or other user-controlled lexeme appears in protected logs, refusal reasons, surfaced errors, or exception chains;
- graph summaries retain the correct node count/order, input count/order, fixed JSON kinds, and aliased source edges;
- the ISO-looking source ID remains a recognized aliased edge;
- parameter summaries expose only counts and tag logs expose no name or value;
- every private interpretation failure uses fixed context without its token, tag, name, path, or original exception;
- malformed JSON retains the fixed `JSON parsing failed (submitted content redacted).` message;
- dynamic metadata still returns `null` on failure, stored workflow loading still returns `null`, direct prompt still follows its fallback, and malformed browser frames still forward;
- valid workflow generation, save/load, tag filling, routing, interrupt, preview, and ControlNet behavior remains unchanged; and
- backend-response diagnostics remain unchanged.

## Success Criteria

- All ten protected statements contain only fixed text or opaque summaries, with seven expected formatter invocations.
- All 12 maintained submitted/private parsing or unescaping invocations remain behind `ComfySubmittedJson`.
- No user-controlled workflow, parameter, tag, WebSocket, stored-data, or exception lexeme reaches a protected diagnostic or downstream error surface.
- Complete node order and source-edge topology remain available through deterministic aliases; semantic labels and output ports are intentionally absent.
- ISO-looking node IDs retain correct aliased connectivity.
- Private post-parse failures are fixed and value-free; broad non-private operational errors retain useful detail.
- Valid workflow, request, save/load, cache, tag-result, WebSocket-forwarding, routing, preview, and ControlNet behavior remains unchanged.
- No backend-response or `S1` production path changes.

## Risks and Rollback

The main risks are accidentally returning a user-controlled identifier, treating a private conversion exception as operational, changing valid tag behavior while adding the failure boundary, losing topology through JSON coercion, or applying submitted-input policy to backend responses. Ordinal aliases, omission of semantic labels/output ports, fixed private failure handling, date-neutral diagnostic parsing, provenance-specific callers, and explicit exclusions bound these risks.

If an opaque summary proves unusable, a protected sink may fall back to a fixed value-free message while the formatter is corrected. If a private failure boundary changes successful behavior, correct that success path without restoring original exception content. Rollback must never restore raw identifiers, class types, input/parameter/tag names, output indexes, values, raw messages, parser previews, conversion messages, or content-bearing inner exceptions.
