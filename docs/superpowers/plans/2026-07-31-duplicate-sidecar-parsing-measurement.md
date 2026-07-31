# Rank 32 Duplicate Sidecar Parsing Measurement Implementation Plan

> Execute in the isolated `measurement/rank32-sidecar-parsing` worktree. Never
> read or modify repository user/generated paths. Builds, harnesses, fixtures,
> caches, logs, and runtime state belong under `/tmp` only.

**Goal:** Measure the materiality of duplicate model-sidecar reads/parses during
`T2IModelHandler.LoadMetadata` recomputation, decide the approved gate, remove
all instrumentation, and close the final numbered roadmap prerequisite.

**Approved base:** `5b536e1ff2d876834419d0138d8929dae3f74909`

## Task 1: Freeze and review the design

- Verify the two sidecar loops, Rank 22 fingerprint/cache contract, settings
  owner, refresh entry points, and central/per-folder LiteDB identity.
- Commit the design and this plan.
- Obtain independent specification and quality approval before source edits.

## Task 2: Establish the contract harness

- Create a `/tmp` C# harness that references only an external Release publish.
- Initialize `Program.DataDir`, settings, logging, and model metadata caches only
  under fresh `/tmp` roots.
- Generate deterministic `.engine` models and supported JSON sidecars.
- First prove the uninstrumented behavior for suffix precedence,
  `procAltHeader`, cache hit, every invalidation family, invalid/corrected JSON,
  conservative concurrent change, cache-unavailable and caught lookup/header/
  upsert failures, both cache modes, exact publication fields, and clean
  shutdown/disposal.
- Record a failing instrumentation-contract expectation before adding hooks.

## Task 3: Add minimal temporary instrumentation

- Add the two documented `PerformanceData` fields with XML/config docs.
- Add one private call-local recorder in `T2IModelHandler.cs`.
- Add one private null-by-default reflection-set synchronization hook solely for
  the deterministic fingerprint-to-read concurrent-change contract case.
- Preserve the exact disabled expressions and all production control flow.
- Instrument fingerprint, cache, embedded header, both sidecar passes,
  recomputation, upsert reachability, publication, outcome, time, allocation,
  counts, and character volume.
- Emit bounded schema-1 JSON with `[Rank32ModelSidecar]`; suppress invalid
  scenarios and swallow recorder failures.
- Run static inventories, `git diff --check`, and no-output disabled checks.
- Commit instrumentation and obtain independent source specification/quality
  approval before collection.

## Task 4: Build and execute the reviewed matrix

- Publish Release externally under `/tmp` with intermediate/package/cache paths
  also under `/tmp`; do not create `src/bin` or `src/obj`.
- Build the harness under `/tmp` against that artifact.
- Run all contract cases and privacy/schema checks.
- Run five warmups and thirty enabled/disabled samples per group
  in counterbalanced `ABBA` blocks.
- Cover 1/4 sidecars at ~1 KiB, 64 KiB, and 1 MiB; 16/128-model batches at 4
  KiB and 64 KiB; central/per-folder modes; and all invalidation families.
- Capture exact raw JSONL, summary, harness, artifact, and source hashes.
- For production `Refresh()` batches, require exact per-iteration record
  completeness; aggregate per-model phases as overlapping CPU-work and record
  one separate external wall interval per iteration.

## Task 5: Decide and review evidence

- Compute nearest-rank p50/p95/max and chronological-half p95 values.
- Apply the exact 25%-of-larger stability formula independently to every gate
  numerator and denominator; only the three frozen gate-eligible groups may
  authorize `GO`.
- Report first pass, second pass, combined sidecar, recomputation, whole-call,
  allocation, and batch aggregates without subtracting overhead.
- Apply only the frozen gate and record `GO`, scoped `GO`, `NO-GO`, or
  inconclusive.
- Document parity, warnings, override scope, synthetic limitations, and exact
  evidence provenance.
- Obtain independent evidence specification and quality approval.

## Task 6: Remove instrumentation and close

- Revert every temporary setting, recorder, timer, counter, stage, and prefix.
- Prove `HEAD:src` equals approved-base `src` exactly and all tokens are absent.
- Run static syntax/whitespace checks.
- Perform a fresh external post-removal Release publish and uninstrumented
  synthetic parity sanity under `/tmp`.
- Update audit Rank 32 and remove the formal Recommended Next Project: the 32
  numbered ranks are complete, though existing separately recorded validation
  gaps and any scoped future designs remain.
- Obtain final independent closure specification and quality approval.
- Fast-forward only local `master`, preserve the primary dirty-state list
  exactly, remove the clean worktree/branch, and do not push.
