# Redact Shared Browser API Diagnostics Design

**Date:** 2026-07-21

**Status:** Approved design, awaiting written-spec review

## Goal

Prevent `genericRequest` from adding arbitrary HTTP request values to browser diagnostics when the server returns an API error, while preserving the existing server error text, transport behavior, callbacks, retry behavior, and user-visible error handling.

## Evidence and Current Behavior

`src/wwwroot/js/site.js::genericRequest(url, in_data, callback, depth = 0, errorHandle = null, timeoutMs = null)` adds `session_id` to `in_data` and sends the object to `API/<url>`. When the response contains `data.error`, the current branch:

1. logs the endpoint and verbatim server-provided error;
2. logs `Input was ${JSON.stringify(in_data)}`;
3. passes the unchanged server error through the existing caller-specific or default failure path.

The maintained source contains 175 `genericRequest(` occurrences, including the definition and callers. These callers can submit credentials, password prehashes, prompts, media, metadata, settings, workflow data, and extension-defined fields. The shared payload log therefore exposes arbitrary caller values as well as the injected session ID whenever a server error reaches this branch.

A static diagnostic inventory found no other client console statement that logs `in_data`. The WebSocket `JSON.stringify(in_data)` occurrence sends the request over the transport and is not a diagnostic sink.

## Chosen Design

Delete only the request-payload console statement in the `data.error` branch of `genericRequest`.

Keep the preceding endpoint/error diagnostic unchanged:

```js
console.log(`Tried making generic request ${url} but failed with error: ${data.error}`);
```

Keep `fail(data.error)` and the branch return unchanged. Do not replace the removed statement with a formatter, structural summary, redaction list, or fixed redaction message. The existing endpoint/error line already provides the diagnostic context needed at this layer without adding client request content.

## Error and Data Flow

After the change, a response containing `data.error` will:

1. log the endpoint and verbatim server-provided error;
2. pass the same `data.error` to `errorHandle`, when supplied, or to the existing default `console.error` and `showError` path;
3. return without invoking the success callback.

The client will no longer serialize or add `in_data`, its nested values, or the injected `session_id` to this diagnostic path.

Server-generated error text remains unchanged and may itself contain a submitted value. For example, Image History can return an invalid sort mode in `data.error`. Inventorying or redacting endpoint-generated error content is a separate concern and is not part of this project.

## Compatibility Requirements

The implementation must preserve:

- the exact six-argument function signature and defaults: `url`, `in_data`, `callback`, `depth = 0`, `errorHandle = null`, and `timeoutMs = null`;
- `session_id` mutation before sending;
- the `sendJsonToServer` request and timeout arguments;
- missing-response handling;
- invalid-session retry depth and positional forwarding;
- bad-impersonation recovery and positional forwarding;
- endpoint and verbatim `data.error` logging;
- caller-provided `errorHandle` timing and arguments;
- the default `console.error` and `showError` behavior;
- success callback behavior;
- network, abort, and timeout descriptions;
- arbitrary extension payload compatibility; and
- all WebSocket behavior and serialization.

No caller migration is required.

## Scope

The production change is limited to `src/wwwroot/js/site.js::genericRequest` and removes one console statement.

Static inspection will cover the complete maintained `genericRequest` boundary and all client request-payload diagnostics to prove the change is complete. If inspection discovers an unrelated endpoint-specific diagnostic, it will be documented for separate consideration rather than added to this implementation.

## Non-Goals

- Changing server endpoint error messages or their contents.
- Changing UI error messages or callback behavior.
- Modifying `genericRequest` callers or payload schemas.
- Redesigning API-key or password storage.
- Changing WebSocket request transport or diagnostics.
- Addressing server-side Comfy workflow logging.
- Introducing a browser logging abstraction, telemetry framework, formatter, configuration option, or field-name redaction list.
- Combining invalid-session callback handling or other transport refactors with this change.

## Static Verification

Repository policy prohibits agent-run builds, automated tests, browsers, and live-server execution. Static verification will:

1. prove the `Input was ${JSON.stringify(in_data)}` statement is absent;
2. enumerate maintained `genericRequest` definitions and callers;
3. search client diagnostics for serialization or interpolation of `in_data` and nested request values;
4. compare the function signature, recursive calls, request call, error paths, and callback paths before and after;
5. run whitespace/diff checks permitted by repository policy; and
6. confirm the implementation commit contains only the intended `site.js` change.

Transport serialization required to send a request is expected to remain and must not be mistaken for diagnostic logging.

## Maintainer Validation

The maintainer should validate the live application with browser developer tools:

1. Submit an invalid API key and force a server rejection. Confirm the client diagnostic adds no key or session ID.
2. Force an ordinary small API error. Confirm the endpoint, verbatim server error, UI error, and callback behavior remain unchanged.
3. Force an image- or metadata-bearing request error. Confirm the client diagnostic adds none of the submitted request content.
4. Request an invalid Image History sort mode. Confirm the server-provided sort value still appears exactly as before.
5. Exercise invalid-session retry and a network or timeout failure. Confirm their behavior and diagnostics remain unchanged.

## Success Criteria

- The shared HTTP server-error path retains endpoint identity and verbatim `data.error`.
- Client diagnostics do not serialize or add request values beyond the server-provided error text.
- Existing UI, callback, retry, timeout, and network behavior is unchanged.
- No caller or extension migration is required.
- Static verification finds no remaining client diagnostic of `in_data` in the maintained boundary.

## Risks and Rollback

The primary regression risk is accidental removal or alteration of endpoint/error context, callback behavior, retry behavior, or positional arguments. The deliberately minimal implementation avoids those paths.

Rollback is mechanically limited to the removed console statement. If maintainers find endpoint/failure identity insufficient, the change should be reverted temporarily and a value-free structural diagnostic designed separately. Arbitrary request serialization is not an acceptable permanent fallback.
