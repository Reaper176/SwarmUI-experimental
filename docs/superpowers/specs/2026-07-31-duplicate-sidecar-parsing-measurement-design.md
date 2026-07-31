# Rank 32 Duplicate Sidecar Parsing Measurement Design

**Date:** 2026-07-31
**Rank:** 32
**Status:** Proposed for review
**Approved base:** `5b536e1ff2d876834419d0138d8929dae3f74909`

## Decision Authority

Maintainer Reaper176 authorized bounded agent-run tests for the remaining
refactor work as a one-time repository-policy override and directed the work to
continue without further questions. Rank 32 may use temporary opt-in C#
instrumentation, external-output builds, synthetic model/sidecar fixtures under
`/tmp`, and an external harness. It may not read or mutate repository user data
or write below `Data`, `Models`, `Output`, `src/Data`, `src/bin`, or `src/obj`.

This is the final numbered measurement prerequisite. It must end with a
recorded `GO`, scoped `GO`, or `NO-GO`, removal of every temporary source hook,
and an exact approved-base `src` tree.

## Question and Existing Owner

Core P4 asks whether the two ordered sidecar passes inside
`T2IModelHandler.LoadMetadata()` perform material duplicate read/parse work
during metadata recomputation.

For each existing suffix in `.swarm.json`, `.json`, `.cm-info.json`, and
`.civitai.info`, the first pass performs `File.Exists`, `File.ReadAllText`, JSON
parse, and property copy into `metaHeader` and `headerData`. After
`procAltHeader(metaHeader)`, the second pass repeats existence, full read, and
full parse, then calls `procAltHeader` on the per-file object. The duplicate
work occurs only on recomputation. Rank 22's fingerprinted cache-hit path uses
filesystem metadata and performs neither pass.

The two passes have different observable roles. The first establishes ordered
last-writer-wins merged header data used for class identification and primary
metadata fields. The second preserves per-file extraction behavior for nested
names, descriptions, and secondary trigger-word sources. This measurement may
observe those roles but may not combine them or reuse parsed objects.

## Goals

- Attribute synchronous time, current-thread allocation, read/parse counts, and
  source-character volume to each existing sidecar pass and the whole
  recomputation.
- Measure one-model and batch-refresh scaling by sidecar count and payload size.
- Distinguish cache reuse, cold/missing recomputation, model-mtime invalidation,
  sidecar-fingerprint invalidation, legacy-null-fingerprint invalidation, and
  legacy `TextEncoders` invalidation.
- Prove exact metadata, suffix precedence, cache/publication, and error parity
  with instrumentation disabled and enabled.
- Produce bounded privacy-safe schema-1 evidence and a fixed decision.

## Non-Goals

- Reuse a parsed sidecar, merge the two passes, change `procAltHeader`, or make
  any production optimization.
- Change Rank 22's fingerprint, capture timing, cache decision, legacy upgrade,
  or concurrent-change convergence behavior.
- Change suffixes, paths, precedence, invalid-JSON/read failure propagation,
  embedded-header handling, metadata limits, LiteDB selection/upsert behavior,
  model publication, API shape, or public ABI.
- Read real model libraries, repository data, network filesystems, or GPU data.
- Infer production frequency, retained memory, another platform/filesystem, or
  benefit from a hypothetical implementation.

## Considered Measurement Approaches

### Whole-call timing only

This has the lowest source impact but cannot distinguish duplicate second-pass
cost from fingerprint inspection, cache access, embedded-header work, class
identification, record construction, or persistence. It cannot answer P4.

### Temporarily reuse first-pass parsed objects

This would directly estimate an optimized path, but it changes object lifetime
and can change merge/extraction or error behavior. It would be an unapproved
implementation rather than observation.

### Selected: opt-in observational phase recorder

Add two temporary fields under `ServerSettings.Performance` and one private
recorder local to `T2IModelHandler.cs`. When disabled, both pass expressions and
control flow remain textually original. When enabled, the recorder times and
counts the existing operations without retaining parsed objects across passes.
This is selected because it directly attributes P4 while preserving a clean
disabled control and a complete rollback.

## Temporary Configuration and Privacy

Temporary fields:

- `ModelSidecarMeasurementEnabled`, default `false`; and
- `ModelSidecarMeasurementScenario`, default empty.

The scenario must match `[a-z0-9_/-]{1,96}` or records are suppressed. Evidence
is compact JSON prefixed `[Rank32ModelSidecar]`. No path, file/model name,
sidecar content, metadata value, exception message, stack, user/session, cache
key, or database path is emitted. Recorder failures are swallowed without
details and never change production behavior.

Each record contains only:

- schema, fixed scenario, result category, invalidation category, cache mode,
  and bounded failure stage;
- suffix slots present, model count supplied by the harness scenario, cache
  hit/recompute flags, and fingerprint inspection count;
- first- and second-pass exists/read/parse/proc/property counts and total source
  characters;
- monotonic microseconds and current-thread allocated bytes for fingerprint,
  cache lookup, embedded header, each pass's exists/read/parse/merge-or-proc,
  total first pass, total second pass, recomputation, and whole call; and
- whether record construction, upsert attempt, and model publication were
  reached.

Durations use `Stopwatch.GetTimestamp`; allocation uses
`GC.GetAllocatedBytesForCurrentThread`. Negative allocation deltas clamp to
zero. Timers exclude log formatting and emission. The record is emitted in a
nonthrowing final boundary on success or failure; the original exception still
propagates unchanged.

## Instrumentation Placement

One call-local measurement scope begins after the existing null/already-loaded
guards. It observes:

1. fingerprint capture;
2. cache lookup and the existing legacy/cache predicates;
3. optional embedded-header work;
4. first-pass existence, read, parse, and property-copy work;
5. `procAltHeader(metaHeader)` separately;
6. second-pass existence, read, parse, and `procAltHeader` work;
7. remaining recomputation and upsert reachability; and
8. final model-publication reachability and whole-call outcome.

Instrumentation must not add a catch around production reads/parses, reorder a
read or parse, pre-read a suffix, reuse a `JObject`, move fingerprint capture,
move cache access, or change lock boundaries. Disabled execution must evaluate
the original two compound read/parse expressions.

## Synthetic Matrix

All fixtures live under fresh `/tmp` roots and use minimal synthetic `.engine`
model files so embedded model-header parsing does not contaminate sidecar cost.
JSON contains deterministic, non-secret fields exercising title, description,
nested model name, tags, resolution, activation text, and trained words.

Required contract cases:

1. central and per-folder cache modes;
2. no sidecars, each suffix alone, and all four suffixes;
3. exact established suffix precedence with conflicting fields;
4. `procAltHeader` nested name, descriptions, activation text, array/object/
   string trained words, and secondary-source disabled/enabled behavior;
5. unchanged cache hit emits no sidecar reads/parses;
6. cold/missing cache, model-mtime mismatch, sidecar-fingerprint mismatch,
   legacy-null fingerprint, and legacy `TextEncoders` recompute;
7. valid-to-invalid JSON and corrected recovery preserve Rank 22 behavior;
8. changed/unreadable sidecar retains the existing failure boundary where the
   host permits the permission case;
9. record fields, `ModelFileVersion`, captured fingerprint, metadata/public
   model fields, upsert/publication reachability, and failure isolation;
10. disabled mode emits no records and exact parity holds.

Performance groups use payloads of approximately 1 KiB, 64 KiB, and 1 MiB,
with one and four sidecars. Batch groups use 16 and 128 models at 4 KiB and 64
KiB per sidecar. Each representative group receives five warmups and thirty
enabled plus thirty disabled samples in counterbalanced `ABBA` blocks. A fresh
model object and deliberate recomputation precondition are established for each
sample. Enabled and disabled outcomes are compared exactly before using timing.

For every representative group, report nearest-rank p50/p95/max and both
chronological-half p95 values for whole call, recomputation, first pass, second
pass, combined sidecar work, and current-thread allocation. Batch summaries
also report aggregate second-pass and whole-refresh time. Report raw enabled
values and enabled-minus-disabled external controls separately; do not subtract
recorder overhead from production phase values.

## Decision Gate

A `GO` requires exact parity, zero privacy/schema failures, complete required
coverage, and at least one representative non-pathological group meeting one of
these thresholds in both chronological halves with half p95 values within 25%:

- duplicate second-pass p95 is at least 5 ms and at least 20% of recomputation;
- duplicate second-pass current-thread allocation p95 is at least 1 MiB and at
  least 20% of recomputation allocation; or
- a 128-model batch's aggregate duplicate second-pass time is at least 25 ms
  and at least 15% of aggregate refresh time.

Only the dominant measured phase may scope a later design. A `GO` can authorize
a separate parsed-object reuse design that preserves the two logical passes;
it does not authorize implementation in Rank 32. If no threshold qualifies,
the decision is `NO-GO`. Failed parity, unstable qualifying halves, missing
coverage, or material recorder contamination makes the result inconclusive and
authorizes no optimization.

## Evidence and Removal Gate

Preserve exact base/design/plan/instrumented/removal/documentation heads; source
and artifact hashes; host/runtime/filesystem identity; harness and raw/summary
hashes; record/assertion/failure counts; percentile method; gate result; and all
limitations. Independent review must approve the source before collection and
the evidence/decision before removal.

Removal deletes both settings, the recorder, every hook/timer/counter/stage, and
the log prefix. The final `src` tree OID must equal approved base exactly.
Static syntax/token/whitespace checks, a fresh external post-removal Release
publish, and an uninstrumented synthetic parity sanity must pass before the
audit can close. Rank 32 implements no production optimization.
