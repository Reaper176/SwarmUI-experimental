# File-Media Conversion Measurement Design

**Date:** 2026-07-31
**Rank:** 28
**Status:** Measurement completed with scoped `GO`; instrumentation removed and final projection verified
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

Resolved paths never enter records or retained identity maps. After
authorization, an HMAC-SHA256 fingerprint keyed by process-random bytes maps the
normalized path to a process-local monotonic ordinal; only that ordinal is
emitted. Enable transitions and identity assignment are serialized, and the
temporary fingerprint map is cleared when disabled and removed with the
instrumentation. No user/session/model/prompt/preset/tag value,
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
record construction and logging. Completed flowed scopes are pruned before
context or claim inheritance. Compact records are queued without flowing the
execution context so console logging stays off conversion/backend critical
paths. Nested outer scope time still includes inner record construction and
queueing; the direct control quantifies that contamination. Disabled execution performs no stack walk,
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

## Collected Evidence and Decision

### Provenance

Temporary source head `a2b1c0a6165d18dcfd1fce86b179b846928a79dd`
received `RANK28_SOURCE_SPEC_APPROVED` and
`RANK28_SOURCE_QUALITY_APPROVED`. Its external Release build under
`/tmp/swarmui-rank28-build-ulyW6O` completed with 0 warnings and 0 errors;
`SwarmUI.dll` SHA-256 is
`0d5c3b84bd05230d26e4e795bab7914960f053f26abf64efd383e9608a70e920`.

The corrected synthetic harness under `/tmp/rank28-harness-qjyiYq` passed 201
assertions with 0 failures. Harness-source SHA-256 is
`07034374de6a754910e5475092764ab597e3c0e3fa4dcd78660be51f7a9a2f53`.
The preserved `/tmp/rank28-final-records.jsonl` evidence file contains 105
records (92 file-call and 13 scope), 105 lines, and 30,339 bytes; SHA-256 is
`71ec7a687dbf255caf48419827a98259a5fe6885ae6f9a8b3ee0e130d2a67be2`.
Preserved `/tmp/rank28-final-summary.json` SHA-256 is
`3d61133cc03ffea5713b798cd381efeca99946a8c15f2fa4a02737632eabb1ec`.
The environment is user-identified Garuda Linux (Arch-based) on Btrfs; the
collection reported Linux 7.1.4-1-cachyos x86_64 and .NET 8.0.29.

### Results

Nearest-rank `x[ceil(pN)]` without interpolation gives 75 measured completed
file calls overall at p50/p95/max 1,423/7,132/31,305 microseconds and
6,642,008/6,642,216/53,129,120 current-thread allocated bytes. The exact
three-repetition disk scale was:

- 1 KiB: p50/max 68/79 microseconds;
- 1 MiB: p50/max 1,864/2,151 microseconds; and
- 8 MiB: p50/max 7,200/7,552 microseconds.

Three measured late `<param[Init Image]:...>` applications of the same synthetic
authorized 1 MiB PNG retained one opaque path ID. File resolution/read/encoding
was p50/p95/max 1,963/2,615/2,615 microseconds with 6,642,064 allocated bytes;
the inclusive late scope was p50/p95/max 10,353/10,519/10,519 microseconds. The
inclusive figure also contains media parsing and nested instrumentation and
must not be attributed wholly to file conversion.

A delayed pending-save task produced 31,259 microseconds of recorded synchronous
wait. Completed pending bytes won over different disk bytes exactly as before.
Data-URL and raw-base64 request values produced bypass items and no file calls.
The original path-backed `IMAGE_LIST` behavior converted the first path to a
data URL and then rejected that result in list validation; disabled and enabled
exception type/message matched. The corrected scope counts that one processed
item once (`media_items = 1`, `file_calls = 1`, `invalid_items = 1`) and does not
infer processing of the untouched second item. Rank 28 records this baseline
edge case but does not authorize changing it.

The 1 MiB direct control over 40 post-warm calls was mean 1,164.05 microseconds
and 6,642,132 bytes disabled versus 1,868.0 microseconds and 6,645,836 bytes
enabled. The elapsed difference is 703.95 microseconds and the allocation
difference is 3,704 bytes; run order, filesystem cache state, asynchronous
delivery, and measurement work make this a contamination/noise bound rather
than subtractable stock cost.
File-call endpoints exclude record construction and asynchronous log delivery,
while inclusive parent scopes can include nested record construction/queueing.

### Boundary and decision

The executed evidence covers deterministic synthetic disk and pending sources,
same/distinct paths, scale, repeated late parameter application, request/preset
scopes, data-URL/raw bypasses, list/error parity, clone retention, concurrent
independent calls, privacy/schema checks, and disabled/enabled controls. It does
not exercise real user data, a live API/server, real saved presets, model load,
backend acquisition/submission, generation/GPU, browser/network, network
filesystem, another platform/filesystem, or production frequency/GC.

Repeated authorized 1 MiB conversion is material in both elapsed time and
allocation after measured contamination, and the maintained late path repeats
that work across cloned tasks. Rank 28 therefore records a scoped `GO`: a
separate design may evaluate request/claim-scoped authorized content
pre-resolution or caching. This decision does not authorize an account/global
cache, freshness relaxation, raw-path key retention, a broad async rewrite, or
any production change in this branch.

## Decision Gate and Rollback

The qualitative gate is repeated authorized conversion that materially extends
claim-held request conversion or per-task pre-backend preparation after
instrumentation contamination is considered. A one-off large-file base64 cost
alone does not authorize caching. `GO` authorizes only a separate design for a
request/claim-scoped, authorization-preserving content-resolution strategy;
`NO-GO` authorizes no optimization.

The completed rollback removes both settings, the recorder, and all hooks. The
final `src` tree OID must exactly equal approved base
`bdbce25dc023e0661d50fe1b66094741128c8271:src`. Only this design, the plan, the
audit disposition, and final evidence documentation may remain.

### Removal and post-removal verification

Removal commit `ef64d64948d64f26bbf5a64ab48045c28d98a996` deletes the
recorder and both settings and restores every conversion, request, preset, late,
and engine body. Final `src` tree OID
`26f65adf96afc130baa8b6fedba84b437d7163dc` exactly equals approved base
`bdbce25dc023e0661d50fe1b66094741128c8271:src`. All
`FileMediaConversionMeasurementEnabled`,
`FileMediaConversionMeasurementScenario`, `FileMediaConversionMeasurement`,
and `[Rank28FileMedia]` source tokens are absent.

The fresh external post-removal Release build under
`/tmp/swarmui-rank28-post-ZXZBaO` completed with 0 warnings and 0 errors;
`SwarmUI.dll` SHA-256 is
`f1f8c369c09ad5076d5de517c8a1f03f562ef557e67ac142598d84dad21f21a7`.
The uninstrumented harness under `/tmp/rank28-post-harness-GFowAW` compiled
with 0 errors and one `MSB3277` `DiagnosticSource` assembly-unification warning,
then passed 18 assertions with 0 failures. Harness-source SHA-256 is
`305bc15231ba756d19249cde768b87936069e9e2d8f3ee05825e69df33799d6c`.
It covers disk and repeated reads, completed/delayed pending-save precedence,
missing-file determinism, request path/data-URL/raw/empty values, retained
path-backed image-list rejection, and repeated late-param/clone behavior.

Rank 28 is complete. The scoped `GO` authorizes only a separate design; no
production cache, pre-resolution, freshness, authorization, async, media-list,
or other behavior change is present in the final source projection.
