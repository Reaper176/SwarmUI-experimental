# File-Media Conversion Measurement Design

**Date:** 2026-07-31
**Rank:** 28
**Status:** Approved for temporary measurement
**Approved base:** `bdbce25dc023e0661d50fe1b66094741128c8271`

## Decision Authority

Maintainer Reaper176 instructed the agent to continue through completion, use
best judgment without further questions, and self-test under a repository-policy
override. That instruction approves this bounded measurement and isolated
external validation. It does not authorize access to repository user data,
production generation, or a file-media optimization before evidence is reviewed.

The selected approach is temporary opt-in instrumentation at the existing
conversion owner plus nested scopes around request conversion, preset
application, late prompt handling, and engine pre-backend preparation. A direct
microbenchmark alone was rejected because it cannot establish multiplicity or
claim/backend placement. Full server/browser/GPU profiling was rejected because
it exceeds the evidence needed and would involve unrelated systems.

## Problem and Consumer Boundary

`T2IParamTypes.ValidateParam` converts path-backed `IMAGE`, `AUDIO`, `VIDEO`, and
each path-backed `IMAGE_LIST` item through `FilePathToDataString`. That method:

1. resolves the user's configured output root;
2. performs the existing path authorization check;
3. resolves special history paths;
4. normalizes the resolved path;
5. synchronously waits on `Session.StillSavingFiles` when present, otherwise
   synchronously reads the file; and
6. allocates a base64 data URL.

The maintained outer consumers are core generation, Image History add, Comfy
generated-workflow preview, Grid Generator base parameters, and Image Batch base
parameters. Presets can apply during request conversion or per Grid cell. The
normal engine path runs late prompt handling per task before backend acquisition;
`<preset>` and `<param>` tags can re-enter conversion. Workflow preview and
`TestPromptFill` also run late handling. Nested scopes intentionally overlap and
must be reported as inclusive rather than summed.

## Goals

- Count request, preset, late, and engine-pre-backend scopes by bounded context.
- Count media items by path, data-URL, raw-base64, empty, and invalid category.
- Count path conversion calls, process-local unique/repeated path IDs, and bytes.
- Separate pending-save hits from filesystem reads.
- Measure authorization/resolution, pending wait, disk read, base64 encoding,
  total elapsed time, and current-thread allocation.
- Measure inclusive request-conversion time while a known generation claim is
  held and engine-start-to-backend-request time.
- Preserve results, exception identity/category, path freshness, source choice,
  tag/preset order, clone isolation, cancellation, metadata, and backend timing.
- Compare disabled and enabled behavior on deterministic synthetic files.
- Produce privacy-safe evidence, decide `GO` or `NO-GO`, then remove every
  temporary source change so final `src` exactly matches the approved base.

## Non-Goals

Rank 28 does not authorize a content cache, async rewrite, changed path roots,
changed special-history behavior, deduplication, altered pending-save precedence,
changed data-URL formatting, changed media parsing, or new public API. It does
not claim production frequencies, real user paths, network-filesystem behavior,
backend/GPU performance, or benefit on platforms not exercised.

## Temporary Configuration and Recorder

Two temporary fields are added after `ModelListSanityCap`:

- `FileMediaConversionMeasurementEnabled`, default `false`; and
- `FileMediaConversionMeasurementScenario`, default empty.

`FileMediaConversionMeasurement` is an internal rank-specific recorder. It uses
an `AsyncLocal` scope stack so nested request/preset/late/engine scopes follow
the current execution context without changing public signatures. Enabled-only
stack inspection classifies a fixed allowlist of maintained callers; everything
else becomes `other`. Context, phase, outcome, and source are bounded literals.

Resolved paths never enter records. After authorization, a process-local
monotonic ordinal is assigned to each normalized path; only that ordinal is
emitted. The temporary map is cleared when measurement is disabled and removed
with the instrumentation. No user/session/model/prompt/preset/tag value,
filename, directory, file content, base64 content, exception text, or stack text
is logged.

Every file-call record is schema 1 compact JSON prefixed
`[Rank28FileMedia]`. It contains a monotonic record ID, bounded scenario and
context, path ordinal when authorization succeeded, source category, byte count,
success/failure category, authorization/resolution, wait, read, encode, and total
microseconds, and current-thread allocated bytes. Every scope record contains
phase/context, claim-held classification, calls/items/unique/repeated counts,
pending/disk/bypass categories, inclusive time/allocation, and for the engine
scope the time from immediately before late handling to immediately before the
backend request.

Recorder failures are swallowed without exception details. Timers stop before
record construction and logging. Disabled execution performs no stack walk,
path-map lookup, timer, allocation read, JSON construction, or emission.

## Instrumentation Placement

- `ValidateParam` notes media-item categories while preserving the original
  branch order and return values. `IMAGE_LIST` records each item once.
- `FilePathToDataString` retains authorization before path identity assignment,
  pending-save precedence, synchronous `Task.Result`, disk fallback, content-type
  selection, and exact returned string.
- `RequestToParams` wraps its existing body with one inclusive scope and uses
  enabled-only caller classification for the five maintained outer contexts.
- `T2IPreset.ApplyTo` wraps its existing loop, distinguishing request, Grid-cell,
  late-tag, and other preset applications without recording preset identity.
- `ApplyLateSpecialLogic` wraps its existing handler loop and distinguishes
  engine, workflow-preview, prompt-fill, and other contexts.
- `T2IEngine.CreateImageTask` adds an enabled-only outer scope immediately before
  late handling and completes it immediately before the existing backend request.
  No await, claim operation, status call, matcher, or backend call is moved.

## Compatibility Invariants

- All public declarations and method signatures are unchanged.
- The disabled path retains the approved-base expressions and ordering.
- Authorization happens before any file content access or recordable path ID.
- `StillSavingFiles` remains preferred and `Task.Result` behavior is unchanged.
- Filesystem fallback reads fresh bytes on every call.
- Data URLs, raw base64, empty values, invalid values, and list separators retain
  exact parsing and result behavior.
- Presets and prompt tags apply in their existing order and may overwrite the
  same parameter exactly as before.
- Clones, cancellation tokens, claims, metadata, and backend selection are not
  changed or retained by the recorder.
- Exceptions remain caller-visible with the same type and message.

## Synthetic Validation Matrix

All files and build artifacts live under fresh `/tmp` directories. The matrix
uses synthetic authorized files at 1 KiB, 1 MiB, and 8 MiB, with one warm-up and
three measured repetitions for each applicable group:

- disabled/enabled direct parity for disk and completed pending-save sources;
- repeated same-path and distinct-path calls;
- delayed pending-save completion to expose synchronous wait time;
- data-URL, raw-base64, empty, and `IMAGE_LIST` bypass/item accounting;
- missing and escaping-path failures with no leaked path value;
- request conversion for each safely constructible maintained outer context;
- request preset, Grid-style preset, late `<preset>`, and late `<param>` media;
- cloned inputs and repeated late handling;
- cancellation-before-work and exception parity where constructible;
- concurrent independent conversions; and
- direct disabled/enabled controls to quantify instrumentation contamination.

Server launch, live APIs, real user data, real presets, model loading, backend
acquisition/submission, generation, browser, network, GPU, and repository data
are explicitly unrun. Contexts that cannot be safely invoked without those
systems remain static-only and are recorded as limitations rather than inferred.

## Decision Gate and Rollback

The qualitative gate is repeated authorized conversion that materially extends
claim-held request conversion or per-task pre-backend preparation after
instrumentation contamination is considered. A one-off large-file base64 cost
alone does not authorize caching. `GO` authorizes only a separate design for a
request/claim-scoped, authorization-preserving content-resolution strategy;
`NO-GO` authorizes no optimization.

After the decision, remove both settings, the recorder, and all hooks. The final
`src` tree OID must exactly equal approved base
`bdbce25dc023e0661d50fe1b66094741128c8271:src`. Only this design, the plan, the
audit disposition, and final evidence documentation may remain.
