# Generic Submitted-Input Diagnostics Design

**Date:** 2026-07-22

**Status:** Implemented and maintainer-validated

## Goal

Prevent browser/user-submitted JSON and dynamic media objects from being reproduced in generic Swarm API/T2I parser/error surfaces, and replace the complete typed-input successful-generation Verbose dump with a count-only diagnostic. Preserve valid parsing, transport, generation, media conversion, established client error contracts, and the adjacent Info-level selected-model operational log.

This project is security prerequisite `S1` from the maintainability architecture refresh. It continues the submitted-input confidentiality boundary outside Comfy ownership. Its maintainer validation is complete, unblocking numbered roadmap work at rank 3.

## Confirmed Boundary

The project contains five submitted-input sources:

1. the initial API WebSocket frame parsed through `API.HandleAsyncRequest`;
2. the initial API HTTP body parsed in the same handler;
3. follow-up generation WebSocket frames parsed in `T2IAPI.GenerateText2ImageWS`;
4. JSON-looking image, audio, and video values interpreted by `T2IParamSet`, including the Grid Generator direct-`Set` path; and
5. the successful-generation verbose log that formerly stringified the complete `T2IParamInput`.

Initial API parser failures reach the shared request catch, which logs `ReadableString()` through the generic `[WebAPI]` error logger. Follow-up frame parser failures reach the generic asynchronous-task logger. Dynamic Grid media failures propagate through `Task.Run`, fault rethrow, `GridGeneratorExtension.ExToError`, its `ReadableString()` log, and the generic WebSocket error response. Before implementation, the successful-generation statement directly wrote parameter names and values.

Ordinary T2I request validation rejects malformed JSON-looking media before `T2IParamSet` parsing and is not the confirmed dynamic-media trigger. Grid Generator applies dynamic-axis values through `T2IParamInput.Set` directly and remains the required validation path.

## Chosen Architecture

Add a dedicated `SubmittedInputJson` helper under `src/Utils`. It owns only browser/user-submitted JSON parsing and remains separate from `Utilities.ParseToJson` and ordinary `ReceiveJson` behavior.

The helper has two narrow responsibilities:

- `ParseObject(string)` preserves successful `JObject.Parse` results. If `JObject.Parse` throws `JsonReaderException`, it throws a fresh `JsonReaderException` with the fixed message `JSON parsing failed (submitted content redacted).`, no original inner exception, and no submitted preview, native parser text, JSON path, or line details.
- `ReceiveObject(WebSocket socket, TimeSpan maxDuration, long maxBytes)` composes the existing `ReceiveData` behavior with UTF-8 decoding and `ParseObject`. It preserves the initial API frame's timeout, maximum-byte enforcement, cancellation behavior, frame aggregation, and nonempty-object expectation.

The initial API WebSocket and HTTP paths use this boundary. Follow-up generation retains its existing receive/state sequence and calls `ParseObject` only where it currently decodes and parses the received bytes. Dynamic media parsing also calls `ParseObject` without importing WebAPI policy into `T2IParamSet`.

General JSON utilities are not changed. Backend responses, output/history metadata, webhooks, persisted files, model metadata, and Comfy-specific input and response parsers retain their existing provenance and behavior.

## Data and Control Flow

### Initial API input

The WebSocket branch continues accepting the socket and enforcing the existing one-minute and `MaxReceiveBytes` limits. The HTTP branch retains content-type validation, content-length validation, exact body reading, UTF-8 decoding, and `JObject` parsing order. Only the submitted-input parser owner changes.

Successful objects proceed unchanged through null checking, route lookup, session lookup, permission checking, handler dispatch, output handling, and socket closure. Parser failures continue into the existing outer catch.

### Follow-up generation frames

The existing receive call, `retain` publication, socket/cancellation/ended checks, image-count extraction, handler task registration, batch-offset update, and final reset remain in their current order. The decoded frame is passed to `SubmittedInputJson.ParseObject` instead of `ParseToJson`.

Malformed JSON still faults the existing checked asynchronous task. Its generic logger receives only the fresh fixed parser exception, so no submitted frame content or native parser detail survives.

### Dynamic media objects

The image, audio, and video branches retain their established handling for data URLs, JSON-looking objects, and raw base64 values. A JSON-looking object is parsed through `SubmittedInputJson.ParseObject`.

Post-parse private interpretation is a separate boundary. Accessing `data` and `filename`, decoding data, constructing the media object, and assigning `SourceFilePath` retain their existing success order. If any of those operations fails after valid JSON parsing, the source owner discards the original exception and throws a fresh generic, inner-free `InvalidOperationException` with exactly `Failed to process submitted media object (content redacted).` and no inner exception.

The fixed post-parse exception deliberately does not derive from `SwarmReadableErrorException`. Grid therefore retains its current internal-error classification, fixed client response, and `ExToError` control flow while its server log becomes value-safe.

### Successful-generation diagnostics

The existing verbose-level guard and operation category remain. The complete `T2IParamInput` interpolation is replaced by `Logs.Verbose($"User {session.User.UserID} above image request had parameter count: {user_input.InternalSet.ValuesInput.Count}");`. The statement does not enumerate, format, hash, classify, or stringify parameter keys or values. The existing session user ID is operational context and is not derived from the submitted parameter collection.

No prompt, model, filename, media content, base64 prefix, seed, extension-defined name/value, or nested object reaches this protected Verbose diagnostic. The adjacent pre-existing Info-level operational log remains unchanged and intentionally reports the selected model name.

## Error Contracts

Initial HTTP and WebSocket malformed-JSON failures retain the existing outer catch, `internal_error` identifier, generic user-facing message, HTTP/WebSocket status behavior, and `[WebAPI]` operation context. Only the internal exception detail becomes fixed and redacted.

Follow-up generation frames retain their checked-task fault handling and cancellation behavior. Only parser exception content changes.

Dynamic Grid media failures retain the existing generic internal-error WebSocket response. The server-side exception message becomes fixed and contains no submitted value, filename, media data, parser message, or original inner exception.

Non-parser exceptions outside these submitted/private boundaries retain their current `ReadableString()` detail. This project does not create a general exception-redaction policy.

## Compatibility Requirements

- Preserve valid `JObject.Parse` behavior, including token typing and object property order.
- Preserve initial HTTP content-type/content-length validation and exact body reading.
- Preserve WebSocket byte limits, timeouts, cancellation, frame aggregation, and route/session/permission dispatch.
- Preserve existing HTTP and WebSocket error identifiers, messages, status codes, and closure behavior.
- Preserve follow-up frame ordering, `retain`, cancellation, batch offsets, and task tracking.
- Preserve valid image/audio/video data conversion and `SourceFilePath` assignment.
- Preserve Grid fault propagation, generic client response, and downstream cleanup.
- Preserve successful generation, the existing verbose log level, and the adjacent Info-level selected-model operational log.
- Preserve general `ParseToJson`/`ReceiveJson`, backend-response, persisted-data, metadata, webhook, and Comfy behavior.
- Do not require external caller or extension migration.

The intentional compatibility changes are limited to fixed submitted-parser exception messages, fixed valid-JSON media interpretation failure details, and removal of typed parameter names and values from the successful-generation verbose diagnostic.

## Files and Ownership

- New submitted JSON boundary: `src/Utils/SubmittedInputJson.cs`
- Initial API source owner: `src/WebAPI/API.cs`
- Follow-up generation and success-diagnostic owner: `src/WebAPI/T2IAPI.cs`
- Dynamic media interpretation owner: `src/Text2Image/T2IParamSet.cs`
- Architecture record: `docs/superpowers/audits/2026-07-21-maintainability-architecture-refresh.md`

No JavaScript, Razor, CSS, Python, Comfy production file, API schema, payload schema, backend-response parser, persistence format, or extension-facing contract is included.

## Static Verification

Repository policy prohibits agents from running builds, automated tests, browsers, servers, backends, or live Grid generation. Static verification will:

1. inventory all five submitted-input sources and their downstream log/error consumers;
2. prove initial HTTP, initial WebSocket, follow-up WebSocket, and dynamic-media JSON parsing use `SubmittedInputJson`;
3. prove successful results still originate from `JObject.Parse` and successful source control flow is unchanged;
4. prove replacement parser exceptions are fresh, same-type, fixed-message, and inner-free;
5. prove post-parse media failures discard original exceptions and remain generic to preserve Grid response classification;
6. prove the protected successful-generation Verbose diagnostic reads only the parameter count while the adjacent Info-level selected-model line remains unchanged;
7. prove no submitted parser/error surface can emit a submitted preview, native parser message, JSON path/line detail, filename, media/base64 content, prompt, model, seed, parameter key/value, or extension-defined value, and no typed-input sentinel reaches the protected count-only Verbose diagnostic;
8. prove general JSON helpers, backend-response parsers, Comfy boundaries, routes, response objects, and persistence owners are unchanged; and
9. run repository-permitted diff, whitespace, exact-call-site, and approved-file-scope checks.

## Maintainer Validation

Use distinct sentinels in:

1. malformed initial API WebSocket JSON;
2. malformed initial API HTTP JSON;
3. malformed follow-up generation WebSocket JSON;
4. malformed JSON-looking image, audio, and video Grid-axis values;
5. valid media JSON whose missing/wrong fields or invalid data causes later interpretation failure; and
6. successful-generation parameters containing prompts, models, media, filenames, seeds, and extension-defined fields.

For dynamic media, use Grid Generator's direct-axis `Set` path rather than an ordinary T2I request. Observe `Task.Run` fault rethrow, `ExToError`, the server log, and WebSocket response.

Confirm that no sentinel, source preview, parser message, JSON path, line detail, filename, media/base64 prefix, prompt, model, seed, parameter key/value, or extension-defined value appears in submitted parser/error surfaces. Confirm that prompt, media, filename, seed, and extension-defined sentinels do not appear in the protected successful-generation Verbose diagnostic; retain a model sentinel and expect it only in the pre-existing Info-level selected-model line, never in that count-only Verbose diagnostic or any submitted parser/error surface. Confirm that valid HTTP/WebSocket requests, follow-up frames, Grid axes, media conversions, generation, responses, cancellation, and cleanup behave unchanged. Confirm the protected Verbose diagnostic reports the correct count and nothing else from the submitted parameters.

**Validation result:** Maintainer Reaper176 confirmed the named build, runtime, and sentinel matrix. No benchmark or performance measurement is part of this result.

## Non-Goals

- No transport, route, payload-schema, media-format, or response-contract redesign.
- No changes to general `Utilities.ParseToJson`, ordinary `ReceiveJson`, or `ReadableString`.
- No removal or redaction of the adjacent Info-level selected-model operational log.
- No general logging, telemetry, taint-tracking, hashing, secret-list, or redaction framework.
- No backend-response, output/history, persisted metadata, webhook, model metadata, or Comfy parser migration.
- No new detailed client validation errors for malformed media objects.
- No reopening of completed browser or Comfy diagnostic projects.
- No numbered rank-3 persistence or OAuth work in this project.

## Success Criteria

- All five confirmed generic submitted-input sources are behind value-safe parsing or diagnostics.
- Valid parsing, media conversion, generation, transport, and established client error responses remain unchanged.
- Parser failures expose only fixed content-redacted detail through fresh same-type exceptions.
- Valid-JSON media interpretation failures retain no original exception or submitted content and remain generic to clients.
- The protected successful-generation Verbose diagnostic exposes only total parameter count; the adjacent Info-level selected-model operational log remains unchanged.
- General, backend-response, persisted-data, metadata, webhook, and Comfy parsing behavior remains unchanged.

## Risks and Rollback

The main risks are broadening submitted-input policy into trusted provenance, changing valid token/media behavior, altering WebSocket state ordering, changing Grid's client-visible error classification, or leaving an alternate generic logger able to recover original exception content.

The dedicated helper, source-owned media boundary, count-only diagnostic, and explicit provenance exclusions contain those risks. If fixed context proves insufficient, protected sinks may add fixed operation categories or value-free counts. Rollback must never restore submitted previews, native parser/conversion messages, JSON paths/line details, filenames, media/base64 content, parameter keys/values, or content-bearing inner exceptions.
