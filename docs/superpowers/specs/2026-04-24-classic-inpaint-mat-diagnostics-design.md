# Classic Inpaint MAT Diagnostics Design

## Goal

Make `Classic Inpaint` failures for the `MAT` backend observable and debuggable without changing the working `LaMa` path.

## Current Problem

`LaMa` works in the current SwarmUI image editor session, but selecting `MAT` produces a generic browser-side `ProgressEvent` failure instead of a readable server error. This means the actual failure mode is not visible in the editor UI or server log in a way that is sufficient to diagnose the backend mismatch.

## Scope

This change is limited to the classic inpaint backend path in `src/WebAPI/T2IAPI.cs`.

It should:

- preserve current `LaMa` behavior
- preserve current mask generation behavior
- add enough logging and error normalization to identify why `MAT` fails
- avoid unrelated refactors in the image editor frontend or general API pipeline

It should not:

- change the `LaMa` invocation unless needed for shared diagnostics
- change the UI surface beyond showing a better returned error when available
- add speculative `MAT`-specific model logic before the failure mode is confirmed

## Design

### Server Diagnostics

The `ClassicInpaint` route will emit explicit diagnostic log lines for requests and per-command execution attempts. The logs should include:

- selected backend
- resolved executable candidate
- resolved argument list
- process exit code when available
- captured output or exception message

The logging should be detailed enough to answer:

- which executable path was attempted
- whether `MAT` is accepted by the installed IOPaint CLI
- whether the process exited with an error versus failing to start
- whether the output file path expectation is wrong for `MAT`

### Error Normalization

If `ClassicInpaint` fails for `MAT`, the route should return a normal JSON error with the attempted command details and process output summary instead of allowing the browser to degrade to a transport-level generic error.

This keeps the debugging signal in the standard API response path and avoids reliance on browser-only failure wrappers.

### Behavioral Policy

- `LaMa` remains the known-good baseline and should continue to succeed unchanged.
- `MAT` remains selectable during diagnostics.
- No automatic fallback from `MAT` to `LaMa` should occur during this phase, because fallback would hide the true failure.

## Risks

- Logging too much raw output could create noisy logs. The implementation should keep diagnostics targeted to `ClassicInpaint`.
- Some failures may still happen outside the command execution block. The route should preserve a top-level exception catch so those still return readable JSON errors.

## Verification

After implementation, manual verification should check:

1. `LaMa` still succeeds in the same session.
2. `MAT` failure produces either:
   - a readable UI error, or
   - a corresponding `ClassicInpaint` diagnostic sequence in `swarmui.log`.
3. The returned/logged diagnostics reveal whether `MAT` is unsupported, misconfigured, or invoked incorrectly.
