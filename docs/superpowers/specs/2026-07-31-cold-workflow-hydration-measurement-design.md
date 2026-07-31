# Cold Workflow Hydration Measurement Design

**Date:** 2026-07-31
**Rank:** 29
**Status:** Design approved for temporary measurement
**Approved base:** `503fb7244375db0e20bcfa36bfc556f6d3c3d20b`

## Decision Authority

Maintainer Reaper176 instructed the agent to continue through completion, use
best judgment without further questions, and self-test under a repository-policy
override. This authorizes bounded Rank 29 instrumentation, external builds, and
synthetic workflow-store validation. It does not authorize repository user-data
access, a live server/backend/browser run, or a production hydration change.

The selected approach instruments the existing `ComfyWorkflowStore` ownership
boundary. A file-read microbenchmark was rejected because it cannot measure the
store lock, lazy cache publication, invalid omission, list response preparation,
or refresh-to-first-list behavior. A full server profile was rejected because
the route adds little beyond the synchronous store work and would broaden the
test boundary unnecessarily.

## Problem and Consumer Boundary

`LoadWorkflowFiles` inventories user and example files and republishes the
existing public `CustomWorkflows` dictionary with null records. The first
`GetWorkflowSnapshot` after that inventory calls `GetWorkflowByNameLocked` for
every key. Each null record synchronously checks existence, reads the complete
JSON file, parses it, converts all workflow fields to retained strings, constructs
a complete `ComfyCustomWorkflow`, and publishes it under `WorkflowLock`.

`ComfyListWorkflows` consumes the complete snapshot but returns only name, image,
description, and `enable_in_simple`. Later lists reuse the complete records until
the next load. Direct reads and standard custom-workflow generation can hydrate
individual entries before listing; parameter cleaning hydrates the selected
prompt; refresh and startup rebuild the null inventory; bundled examples follow
the same cache contract. The public dictionary identity and direct external
access remain compatibility limits established by Rank 6.

## Goals

- Measure inventory, cold/warm snapshot, direct lookup, and per-file hydration.
- Include store-lock acquisition in public-operation elapsed time.
- Split file existence/read, parse/extract, record construction/publication, and
  total hydration time without changing stock ordering.
- Record file count, source bytes, retained field characters, hydrated, cached,
  missing, invalid, and example categories without names or contents.
- Record refresh-to-first-snapshot elapsed time and the number of null records at
  public-operation entry.
- Measure current-thread allocation and an external forced-GC retained-memory
  delta while clearly separating the two.
- Exercise cold/warm listing, direct-first hydration, invalid/missing omission,
  examples, refresh, immediate saved-record visibility, and concurrent readers.
- Preserve results, ordering, lazy hydration, logging/omission, recovery gating,
  durable-store ownership, dictionary identity, and exception behavior.
- Decide `GO` or `NO-GO`, then remove every temporary source change so final
  `src` exactly matches the approved base.

## Non-Goals

Rank 29 does not authorize descriptor caching, split records, schema changes,
eager startup hydration, background reads, lock changes, invalid-file retries,
changed example restoration, changed save/delete/recovery behavior, or a new
public API. It does not claim real user-library frequency, network-filesystem
behavior, live route serialization cost, browser behavior, generation/backend
behavior, other-platform results, or production GC impact.

## Temporary Configuration and Recorder

Two temporary performance fields are added after `ModelListSanityCap`:

- `WorkflowHydrationMeasurementEnabled`, default `false`; and
- `WorkflowHydrationMeasurementScenario`, default empty.

`WorkflowHydrationCostMeasurement` is an internal Rank 29 recorder. It emits
compact schema-1 JSON records prefixed `[Rank29WorkflowHydration]`. Scenario,
operation, outcome, and source values are normalized to bounded literals. It
never records a workflow name, path, JSON key/value, submitted content, image,
description, exception message, stack, user/session identity, or file content.

Operation records cover `inventory`, `snapshot`, and `lookup`. They contain a
monotonic ID, bounded scenario, entry/exit inventory counts, entry-null count,
hydrated/cached/missing/invalid/example counts, aggregate bytes and retained
characters, refresh age when applicable, total microseconds, current-thread
allocated bytes, and completed/failed outcome. Snapshot and lookup timers begin
before `WorkflowLock`, so total time includes lock acquisition; nested hydration
times must not be summed with the parent operation.

Hydration records contain only bounded source/outcome, example classification,
source byte count, retained field-character count, existence/read, parse/extract,
publication, total microseconds, and current-thread allocation. Missing and
invalid records expose no name or exception detail. Inventory completion stores
only a monotonic timestamp; the first later snapshot consumes it to report
refresh age. Disabled execution follows an explicit original body without
timers, allocation reads, counters, record construction, or emission.

Records are serialized after timing endpoints and queued with execution-context
flow suppressed so console logging does not block the store lock. A direct
disabled/enabled control bounds instrumentation contamination. Recorder failures
are swallowed and cannot alter store results or exceptions.

## Instrumentation Placement

- `LoadWorkflowFiles` retains one exact disabled body. The enabled body adds an
  outer inventory operation and counts discovered JSON files, examples, copied
  examples, and published null records while preserving recovery and publication.
- `GetWorkflowByName` retains one exact disabled body and adds an enabled lookup
  operation whose elapsed time begins before the lock.
- `GetWorkflowSnapshot` retains one exact disabled body and adds an enabled
  snapshot operation beginning before the lock and observing entry null count.
- `GetWorkflowByNameLocked` keeps cached/missing branches and exception handling
  in their original order. Only the enabled null-record path splits the existing
  read/parse/extract/publish work into timed stages.
- `GetWorkflowNames`, `TryGetWorkflowParameterPrompt`, save/delete/recovery, and
  the API/extension facades are unchanged; their maintained behavior is covered
  through existing owner calls and static tracing.

## Compatibility Invariants

- Public declarations, method signatures, and `CustomWorkflows` object identity
  remain unchanged.
- `WorkflowLock`, `RecoveryRequired`, journal/recovery, and fail-closed ordering
  remain unchanged.
- Inventory ordering, example copy/deletion-marker rules, null placeholders, and
  same-instance clear/repopulation remain unchanged.
- Cached records return without file access; null records synchronously read and
  parse once under the lock; missing files are removed; invalid files remain null,
  log the same content-redacted error, and are omitted from snapshots.
- All field conversions, placeholder image fallback, description/default values,
  sort order, and response fields remain unchanged.
- Save publication remains immediately complete and visible without hydration.
- Exceptions and recovery failures retain their caller-visible types and text.

## Synthetic Validation Matrix

All roots, workflow files, build artifacts, logs, and harness projects live under
fresh `/tmp` directories. No repository `Data`, `Models`, `Output`, `src/Data`,
generated `src/bin`/`src/obj`, or external extension content is read or written.
The synthetic extension root is assigned to both the load argument and
`ComfyUIBackendExtension.Folder` because stock hydration uses the latter.

The matrix uses one warm-up plus three measured repetitions where applicable:

- 1, 16, and 128 valid workflows with bounded small graph payloads;
- 16 workflows with larger graph/prompt/custom-parameter payloads;
- one large workflow to expose byte scaling;
- cold snapshot immediately after inventory and a second warm snapshot;
- direct lookup before listing, followed by a partly cold snapshot;
- missing-after-inventory and invalid JSON omission/logging;
- bundled example restoration and example classification;
- refresh-to-list delay and repeated refresh/list cycles;
- prepublished complete records representing immediate save visibility;
- concurrent snapshots and direct lookups, whose serialized stock results must
  match a sequential baseline; and
- disabled/enabled controls on identical synthetic files.

The harness validates names/order/result fields only against its own synthetic
expectations. Evidence records are checked for schema, bounded categories,
nonnegative metrics, count conservation, and absence of every synthetic name,
path component, JSON sentinel, and content fragment.

Live APIs/server, repository workflows, real presets, standard generation,
backend acquisition/submission, browser, network, GPU, network filesystems,
other platforms/filesystems, and production workload/GC remain explicitly unrun.

## Decision Gate and Rollback

The qualitative gate is whether cold snapshot excess is materially dominated by
complete-record read/parse/allocation/retention across repeated or scaled
synthetic libraries, while warm snapshots and instrumentation contamination are
substantially smaller. A single slow filesystem event or structural count alone
does not authorize change. `GO` permits only a separate descriptor/lazy-record
design preserving Rank 6 durability and visibility contracts. `NO-GO` permits no
optimization.

After evidence review, remove both settings, the recorder, and every hook. The
final `src` tree OID must exactly equal approved base
`503fb7244375db0e20bcfa36bfc556f6d3c3d20b:src`. Only this design, its plan, the
audit disposition, and final evidence documentation may remain.
