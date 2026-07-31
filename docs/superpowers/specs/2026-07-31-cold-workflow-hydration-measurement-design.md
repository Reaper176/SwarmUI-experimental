# Cold Workflow Hydration Measurement Design

**Date:** 2026-07-31
**Rank:** 29
**Status:** Measurement completed with scoped `GO`; instrumentation removed and final projection verified
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

## Collected Evidence and Decision

### Provenance

Corrected temporary source head
`f8547e94` received `RANK29_SOURCE_SPEC_APPROVED`,
`RANK29_SOURCE_QUALITY_APPROVED`, and
`RANK29_FINAL_SOURCE_QUALITY_APPROVED`. Its fresh external Release build under
`/tmp/swarmui-rank29-build-final-csJZOB` completed with 0 warnings and 0 errors;
`SwarmUI.dll` SHA-256 is
`7b171ef4af28f654f4c70dbe4e49071c8b2c39efe76bc718a3dce535d104a8d0`.

The final synthetic harness under `/tmp/rank29-harness-BReemg` compiled with 0
errors and one `MSB3277` `DiagnosticSource` assembly-unification warning, then
passed 10,907 assertions with 0 failures. Harness-source SHA-256 is
`470521616bf11a12a65ede83c02c346a6a708652abcb1daaec47699da9b72579`.
The preserved `/tmp/rank29-final-records.jsonl` contains 1,331 schema-1 records
(168 operation and 1,163 hydration), 1,331 lines, and 406,310 bytes; SHA-256 is
`5722f4f51ab1c2d6dc76afba5a400321574cee961fc238d0e85708d2212f7ccc`.
Preserved `/tmp/rank29-final-summary.json` SHA-256 is
`965833e312bf26c1edd57cc8315e87b6e06de6a5d570ef11dc21211578c7a901`.
The environment is user-identified Garuda Linux (Arch-based) on Btrfs; the
collection reported Linux 7.1.4-1-cachyos x86_64. The harness targeted .NET 8;
the invoking SDK reported 10.0.110.

### Results

The 15 measured cold snapshots across five groups have nearest-rank
p50/p95/max 4,680/14,864/14,864 microseconds and
3,779,424/38,754,216/38,754,216 current-thread allocated bytes. The matching 15
warm snapshots were 19/41/41 microseconds and 1,008/5,560/5,560 bytes. Because
these groups intentionally mix counts and payload sizes, the group results are
the primary scaling evidence:

- 1 small workflow: cold p50/max 71/77 microseconds;
- 16 small workflows: 639/882 microseconds;
- 128 small workflows: 4,680/4,793 microseconds;
- 16 larger workflows: 7,341/14,864 microseconds; and
- 1 approximately 2.1 MiB serialized workflow: 6,183/7,883 microseconds.

Across 486 measured successful file hydrations, total p50/p95/max was
36/440/7,847 microseconds, read was 10/100/2,789 microseconds, parse/extract was
24/316/6,271 microseconds, and current-thread allocation was
29,336/2,421,432/37,832,440 bytes. Parse/extract rather than read dominates the
large-file maximum in this synthetic local-filesystem matrix.

The forced-GC 64-workflow retained-memory probe observed an 8,473,512-byte
increase after cold listing. That process-wide value includes GC/runtime noise
and is directional rather than an exact object-size measurement. The 20-call
direct control averaged 2,229 microseconds and 10,540,783 bytes
disabled versus 2,591 microseconds and 10,548,045 bytes enabled, a
362-microsecond and 7,262-byte difference. Run order, cache state, counters, and
deferred emission make that a
contamination/noise bound rather than subtractable stock cost; the one-file small
group is therefore not independently material evidence.

Direct-first lookup hydrated exactly one of eight null records; the following
snapshot hydrated seven and reused one. Missing-after-inventory removal changed
entry/exit counts from 3/3 null to 2/1, while the invalid record stayed null and
was retried/omitted on the warm snapshot with parse-stage time recorded. Two
bundled examples were copied, classified, and hydrated. A prepublished complete
record listed with one cache hit and no hydration. Concurrent lookup/snapshot
results matched the sequential synthetic baseline, and a 25 ms delayed first
snapshot recorded a 25,452-microsecond refresh age. Every record passed schema,
bounded-category, uniqueness, conservation, nonnegative-metric, and privacy
checks; synthetic roots, names, descriptions, and content sentinels were absent.

### Boundary and decision

The executed evidence covers synthetic local inventory, cold/warm listing,
count/payload scaling, direct-first and partially cold access, missing and invalid
omission, bundled examples, refresh age, prepublished visibility, concurrent
maintained readers compared exactly with a captured sequential result baseline,
retention direction, and disabled/enabled contamination. It
does not exercise repository or real user workflows, a live API/server, real
presets, standard generation, backend acquisition/submission, browser/network,
GPU, network filesystems, another platform/filesystem, or production frequency/GC.

Complete-record hydration scales with file count and payload, allocates tens of
megabytes for the larger synthetic groups, and retains graph/prompt/custom data
that the list response does not return. Rank 29 therefore records a scoped `GO`:
a separate design may evaluate metadata descriptors for list fields plus lazy
complete-record hydration while preserving Rank 6 durability, recovery, public
dictionary identity, immediate save visibility, invalid omission, and direct
read/generation behavior. This decision does not authorize that design or any
production/cache/schema/locking change in this branch.

## Decision Gate and Rollback

The qualitative gate is whether cold snapshot excess is materially dominated by
complete-record read/parse/allocation/retention across repeated or scaled
synthetic libraries, while warm snapshots and instrumentation contamination are
substantially smaller. A single slow filesystem event or structural count alone
does not authorize change. `GO` permits only a separate descriptor/lazy-record
design preserving Rank 6 durability and visibility contracts. `NO-GO` permits no
optimization.

The completed rollback removes both settings, the recorder, and every hook. The
final `src` tree OID exactly equals approved base
`503fb7244375db0e20bcfa36bfc556f6d3c3d20b:src`. Only this design, its plan, the
audit disposition, and final evidence documentation may remain.

### Removal and post-removal verification

Removal commit `f9abc2dd547180ae8a7f78f291933dcd2b64b9c7` deletes the recorder
and settings and restores the original inventory, lookup, locked hydration, and
snapshot bodies. Final `src` tree OID
`26f65adf96afc130baa8b6fedba84b437d7163dc` exactly equals approved base
`503fb7244375db0e20bcfa36bfc556f6d3c3d20b:src`. All
`WorkflowHydrationMeasurementEnabled`,
`WorkflowHydrationMeasurementScenario`, `WorkflowHydrationCostMeasurement`, and
`[Rank29WorkflowHydration]` source tokens are absent.

The fresh external post-removal Release build under
`/tmp/swarmui-rank29-post-VSZ02h` completed with 0 warnings and 0 errors;
`SwarmUI.dll` SHA-256 is
`c6b5305538d32df58f7a66c1c41644530f388992b2982271dbe369328e855fea`.
The uninstrumented harness under `/tmp/rank29-post-harness-H7V11Q` compiled with
0 errors and one `MSB3277` `DiagnosticSource` assembly-unification warning, then
passed 38 assertions with 0 failures. Harness-source SHA-256 is
`1af7ade5987cccdf7d8c82f6aca12ec985506eb914b2f162a15a9cc66afd0cdf`.
It covers lazy inventory, cold/warm sorted listing, example restoration, direct
and parameter-prompt reads, partially cold listing, missing/invalid omission,
prepublished visibility, exact sequential/concurrent parity, and public map
identity.

Rank 29 is complete. The scoped `GO` authorizes only a separate design; no
descriptor cache, record split, hydration, schema, lock, recovery, save/delete,
or other production behavior change is present in the final source projection.
