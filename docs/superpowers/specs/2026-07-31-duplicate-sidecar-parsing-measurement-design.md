# Rank 32 Duplicate Sidecar Parsing Measurement Design

**Date:** 2026-07-31
**Rank:** 32
**Status:** Completed with design-only GO; instrumentation removed
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

The scenario must be one of the fixed labels defined by this design, optionally
followed only by `/warmup_[0-4]` or `/measured_[0-29]`; every other value
suppresses records. The allowlisted bases are `contract`, `cache_central`,
`cache_per_folder`, `suffix_single`, `suffix_all`, `invalid_json`,
`concurrent_change`, `cache_unavailable`, `cache_fault`, `header_fault`,
`single_1k_1`, `single_1k_4`, `single_64k_1`, `single_64k_4`,
`stress_single_1m_1`, `stress_single_1m_4`, `batch16_4k_4`,
`batch16_64k_4`, `batch128_4k_4`, `batch128_64k_1`, `control_enabled`, and
`control_disabled`. Every record with another value is suppressed. Evidence is
compact JSON prefixed `[Rank32ModelSidecar]`. No path, file/model name,
sidecar content, metadata value, exception message, stack, user/session, cache
key, or database path is emitted. Recorder failures are swallowed without
details and never change production behavior.

Each record contains only:

- schema, fixed scenario, result category, invalidation category, cache mode,
  and bounded failure stage;
- suffix slots present, a bounded numeric model count, cache
  hit/recompute flags, and fingerprint inspection count;
- first- and second-pass exists/read/parse/proc/property counts and total source
  characters;
- monotonic microseconds and current-thread allocated bytes for fingerprint,
  cache lookup, embedded header, each pass's exists/read/parse/merge-or-proc,
  total first pass, total second pass, recomputation, and whole call; and
- whether record construction, upsert attempt, and model publication were
  reached.

The result category is exactly one of `cache_hit`, `recomputed`,
`cache_unavailable` or `failed`. Failure stage is exactly one of
`none`, `fingerprint`, `cache_lookup_caught`, `embedded_header_caught`,
`first_exists`, `first_read`, `first_parse`, `first_merge`, `meta_extract`,
`second_exists`, `second_read`, `second_parse`, `second_proc`,
`record_construction`, `upsert_caught`, or `publication`. Caught lookup/header/
upsert stages are also bounded flags while the final result records the
continued outcome. A cache-unavailable return finalizes before returning; a
propagating exception finalizes then rethrows the original exception.

Durations use `Stopwatch.GetTimestamp`; allocation uses
`GC.GetAllocatedBytesForCurrentThread`. Negative allocation deltas clamp to
zero. Timers exclude log formatting and emission. The record is emitted in a
nonthrowing final boundary on success or failure; it cannot mask an existing
return or exception, and the original exception still propagates unchanged.

## Instrumentation Placement

One call-local measurement scope begins only after the existing null and
already-loaded guards. No setting lookup, timer, allocation read, recorder
construction, or record emission occurs before or within either guard. The
scope observes:

1. fingerprint capture;
2. cache lookup and the existing legacy/cache predicates;
3. optional embedded-header work;
4. first-pass existence, read, parse, and property-copy work;
5. `procAltHeader(metaHeader)` separately;
6. second-pass existence, read, parse, and `procAltHeader` work;
7. remaining recomputation and upsert reachability; and
8. final model-publication reachability and whole-call outcome.

The enabled path necessarily splits each existing compound
`File.ReadAllText(...).ParseToJson()` expression so read and top-level parse can
be timed independently. It does not reuse the result across passes. Top-level
parse counts include only the pass/suffix parses. JSON strings parsed inside
`procWordsFrom` remain part of `second_proc` time and are not top-level parses.
Property counts read the already parsed `JObject.Count` once outside the timed
property-copy loop; instrumentation adds neither per-property increments nor a
second enumeration.

Instrumentation must not add a catch around production reads/parses, reorder a
read or parse, pre-read a suffix, reuse a `JObject`, move fingerprint capture,
move cache access, or change lock boundaries. Disabled execution must evaluate
the original two compound read/parse expressions.

## Synthetic Matrix

All ordinary and performance fixtures live under fresh `/tmp` roots and use
minimal synthetic `.engine` model files so embedded model-header parsing does
not contaminate sidecar cost. The `header_fault` contract alone uses a malformed
synthetic `.safetensors` file to enter the production caught embedded-header
failure and prove sidecar continuation. JSON contains deterministic, non-secret
fields exercising title, description, nested model name, tags, resolution,
activation text, and trained words.

Required contract cases:

1. central and per-folder cache modes;
2. no sidecars, each suffix alone, and all four suffixes;
3. exact established suffix precedence with conflicting fields;
4. `procAltHeader` nested name, descriptions, activation text, array/object/
   string trained words, and secondary-source disabled/enabled behavior;
5. unchanged cache hit emits no sidecar reads/parses;
6. cold/missing cache, model-mtime mismatch, legacy-null fingerprint, and
   legacy `TextEncoders` recompute;
7. add, edit, and delete fingerprint invalidation separately for each of all
   four supported suffixes;
8. a private temporary reflection-set synchronization hook changes a sidecar
   after fingerprint capture and before the first read, proving the record
   retains the conservative captured fingerprint and the next refresh
   converges; the hook is null/no-op outside this contract case;
9. cache-unavailable return, caught cache-lookup failure followed by
   recomputation/publication, caught embedded-header failure followed by
   sidecar processing, and caught upsert failure followed by publication;
10. valid-to-invalid JSON and corrected recovery preserve Rank 22 behavior;
11. changed/unreadable sidecar retains the existing failure boundary where the
   host permits the permission case;
12. record fields, `ModelFileVersion`, captured fingerprint, metadata/public
   model fields, upsert/publication reachability, and failure isolation;
13. disabled mode emits no records and exact parity holds.

Performance groups use payloads of approximately 1 KiB, 64 KiB, and 1 MiB,
with one and four sidecars. Batch groups use 16 and 128 models at 4 KiB and 64
KiB per sidecar. The first two gates apply only to direct one-model
`single_64k_4` in central-cache mode. The batch gate applies only to production
`Refresh()` groups `batch128_4k_4` and `batch128_64k_1`, also in central-cache
mode. Per-folder mode is contract/parity evidence only. No gate combines cache
modes, scenarios, or direct and batch records. The 1 KiB, 16-model, and 1 MiB
groups are scaling/control or stress disclosure only and cannot authorize a
`GO`. Each group receives five warmups and thirty
enabled plus thirty disabled samples in counterbalanced `ABBA` blocks. A fresh
model object and deliberate recomputation precondition are established for each
sample. Enabled and disabled outcomes are compared exactly before using timing.

Batch samples call production `Refresh()`, including its existing parallel
discovery/load behavior. The harness records one external monotonic wall
interval per complete refresh iteration. Every per-model record carries the
fixed scenario/iteration label; the aggregation key is exactly scenario base,
fixed `central` cache mode, and numeric iteration. An iteration is usable only with the exact
expected record count and passing parity. Summed per-call phase durations are
reported as overlapping CPU-work, never wall time. For 128 models, first form
one wall value and one summed phase-work value per iteration, then calculate
p95 across the thirty aggregates and separately across iterations 0-14 and
15-29.

For every group, report nearest-rank p50/p95/max and both
chronological-half p95 values for whole call, recomputation, first pass, second
pass, combined sidecar work, and current-thread allocation. Batch summaries
also report summed second-pass/whole-call CPU-work and separate external refresh
wall time. Report raw enabled
values and enabled-minus-disabled external controls separately; do not subtract
recorder overhead from production phase values.

## Decision Gate

A `GO` requires exact parity, zero privacy/schema failures, complete required
coverage, and at least one frozen gate mapping meeting its threshold
independently in each chronological half:

- for central-cache direct `single_64k_4`, duplicate second-pass p95 is at least
  5 ms and at least 20% of recomputation;
- for central-cache direct `single_64k_4`, duplicate second-pass current-thread
  allocation p95 is at least 1 MiB and at least 20% of recomputation allocation;
  or
- for either central-cache `batch128_4k_4` or `batch128_64k_1`, aggregate
  duplicate second-pass CPU-work p95 is at least
  25 ms and at least 15% of aggregate whole-call CPU-work p95.

For the qualifying gate, every participating p95 metric is independently
stable by `abs(half1 - half2) / max(half1, half2) <= 0.25`: second-pass and
recomputation time for the first gate; second-pass and recomputation allocation
for the second; or aggregate second-pass and aggregate whole-call CPU-work for
the batch gate. The percentage threshold is recomputed and must pass in each
half, not only in the full run. Batch external wall time is separate and is
never the denominator for summed overlapping CPU-work.

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

Design reviews of exact head
`af198fc4400859e6e8a42232ac51619fd9042628` returned
`RANK32_DESIGN_SPEC_APPROVED` and `RANK32_DESIGN_QUALITY_APPROVED` with no
remaining findings. The approved-base harness passed four behavior assertions
and failed only its expected missing-instrumentation assertion.

## Collected Evidence and Decision

The temporary source was collected at exact instrumented head
`1863f4ba8b7e04ca2845d08ebc0e92da6a3b1c8e`. Independent source reviews of
that exact head returned `RANK32_SOURCE_SPEC_APPROVED` and
`RANK32_SOURCE_QUALITY_APPROVED` with no remaining findings. A fresh external
Release publish under `/tmp/swarmui-rank32-instrumented-4WuAQ3/app` completed
with 0 warnings and 0 errors; its `SwarmUI.dll` SHA-256 is
`2a81f1302c26a45df98e7e9f341c78c35f8dd9fe49a8078dffe8510f616da4be`.

Under Reaper176's one-time self-testing override, the authoritative synthetic
matrix ran on Garuda Linux (Arch-based), Linux `7.1.4-1-cachyos`, x86-64,
.NET SDK `10.0.110`, .NET runtime `Microsoft.NETCore.App 8.0.29`, and an AMD
Ryzen 7 7800X3D. Repository source was on Btrfs; all fixtures, databases,
build products, harness files, and evidence were isolated to `/tmp`, which was
tmpfs. No repository user data, model library, network filesystem, GPU data,
or external service was read.

The external harness passed 74,272 assertions with zero failures and emitted
10,335 complete privacy-safe schema-1 records. It covered every required
extraction shape, suffix add/edit/delete, legacy/cache/concurrent-change case,
the host-supported unreadable changed-sidecar failure and corrected recovery,
and exact same-input full metadata/stable-public-field parity across disabled
and enabled warmups and measured samples. Every performance summary contains
full and chronological-half nearest-rank distributions for first, second,
combined-sidecar, recomputation, whole-call, and allocation fields; batch
values are per-iteration aggregates. Raw external enabled/disabled wall and
current-thread-allocation samples and their paired enabled-minus-disabled
deltas are retained. The raw JSONL contains no synthetic-root string, sentinel
content, or model filename. Artifacts are:

- summary: `/tmp/rank32-summary.json`, 96,234 bytes, SHA-256
  `ba60814e00c6d2ff374ad6cb95896077a847c61befc3584619edd36f140fb77c`;
- raw records: `/tmp/rank32-records.jsonl`, 15,633,784 bytes, SHA-256
  `8667e78132552f2369f7f7b10cf5109a4a7219ada0fb01d0cf1db25b92f6dd9b`;
- harness source: `/tmp/rank32-harness/Program.cs`, 37,783 bytes, SHA-256
  `6ee732caa358b37e361d67fee29b5d31dd10b2fbcf19e0afc946043d2ecf2d2a`;
  and
- harness assembly: `/tmp/rank32-harness/bin/Release/net8.0/Rank32Harness.dll`,
  38,912 bytes, SHA-256
  `ecde12f3191f563ee1b1a983424be9d57344ec3e8620c02c72ab5939713c9f13`.

Nearest-rank order statistics produce a **GO** through the independently
authorizing direct-allocation gate. For central-cache `single_64k_4`, the
second-pass allocation p95 is 3,782,568 and 3,774,624 bytes in the two
chronological halves; recomputation allocation p95 is 7,622,224 and 7,620,576
bytes. The per-half ratios are 49.6255% and 49.5320%, above the frozen 20%
threshold, while both second-pass values exceed 1 MiB. The stability ratios
are 0.002100 for second-pass allocation and 0.000216 for recomputation
allocation, both below 0.25. Whole-call external allocation p95 is 7,767,528
bytes enabled and 7,766,960 bytes disabled; the paired enabled-minus-disabled
allocation p95 is only 6,832 bytes, separately bounding recorder contamination.
The direct time gate does not qualify: second-pass p95 is 1,827 us and 1,705 us,
below 5 ms. Neither batch group independently satisfies every frozen
threshold and stability condition, so neither contributes to the decision.

This is a design-only `GO`: a later, separately reviewed design may consider
reusing parsed sidecar objects while preserving both logical passes and every
Rank 22 contract. Rank 32 implements and authorizes no production optimization.
The measurements are synthetic, single-host, current-thread-allocation and
phase-attribution evidence; they do not establish production frequency,
retained-memory cost, another runtime/platform/filesystem, network-storage
behavior, or benefit from a proposed implementation.

Several non-authoritative harness attempts preceded the accepted collection.
The first compile found only a missing Frenetic extension namespace. The first
full run exposed two harness-fixture assertions: a sidecar architecture hint
does not itself seed the historical `ModelClassType` needed for the legacy
`TextEncoders` cache case. A later passing run was rejected by independent
review because the summary omitted external allocation controls and required
phase/half distributions, exact enabled/disabled parity was under-specified,
some extraction shapes were absent, and the permission case and runtime
identity were not recorded. The external harness alone was corrected. A
subsequent passing run was deliberately superseded after self-review expanded
the exact parity snapshot to every stable public model field. No repository or
instrumented source changed during any harness correction. Only the final
complete rerun and hashes above are authoritative.

## Removal and Closure

Independent reviews of exact evidence head
`b14b8074c59d52a4b0ec7f9a918406c2604c290a` returned
`RANK32_EVIDENCE_SPEC_APPROVED` and `RANK32_EVIDENCE_QUALITY_APPROVED` with no
remaining findings. Removal commit
`818a2d2ebe6ecc6262eaf1ef3a988c05f5bff58a` reverts the three source-only
instrumentation commits. The resulting `src` tree OID is
`26f65adf96afc130baa8b6fedba84b437d7163dc`, exactly equal to approved base
`5b536e1ff2d876834419d0138d8929dae3f74909`; its path diff is empty, and static
searches find no Rank 32 setting, recorder, hook, timer, counter, or log-prefix
token.

A fresh post-removal source archive excluded repository user-data and extension
paths and published externally under `/tmp/swarmui-rank32-post-zODPX2` with 0
warnings and 0 errors. Its `SwarmUI.dll` SHA-256 is
`dc53950935229d8763771c4d106584078bea1bbc2f358a2729fd81d431515252`.
The uninstrumented synthetic sanity passed 29 assertions with zero failures;
its source SHA-256 is
`ec29c4995dcabf431393ec238e765d9bcb8c44e77f9e864e8c0e39910d39cba4`
and assembly SHA-256 is
`a4cf32cd9d8430bb15941cc12bf878ef197341f86c372994723bd58f33a41f82`.
One preceding sanity attempt expected the first suffix's trigger word despite
the established merged-header last-suffix precedence; correcting that external
expectation produced the authoritative 29/0 run and changed no repository
source.

Final closure reviews of exact head
`05d4a18d34f5d64df8313eff91616601fccd6c91` returned
`RANK32_CLOSURE_SPEC_APPROVED` and `RANK32_CLOSURE_QUALITY_APPROVED` with no
remaining findings.
