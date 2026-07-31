# Cold Workflow Hydration Measurement Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use
> `superpowers:executing-plans` to implement this plan task-by-task.

**Goal:** Measure cold workflow-list hydration, make a bounded optimization
decision, and restore production source exactly.

**Architecture:** Temporary opt-in instrumentation lives inside the existing
`ComfyWorkflowStore` lock/reader owner. Privacy-safe operation and hydration
records measure inventory, cold/warm snapshots, direct lookup, per-file stages,
allocation, and refresh age without public API changes.

**Tech stack:** C# 12, .NET 8, Newtonsoft.Json, Bash static gates, and an isolated
`/tmp` harness using synthetic workflow files only.

## Authority and Safety

Reaper176 authorized autonomous completion and self-testing. Do not access
repository `Data`, `Models`, `Output`, `src/Data`, generated `src/bin`/`src/obj`,
or external extensions. Preserve the primary dirty path set as metadata only.

Approved base: `503fb7244375db0e20bcfa36bfc556f6d3c3d20b`.

## Task 1: Freeze Base and Establish RED

- [ ] Confirm the isolated worktree is clean and based exactly on the approved
  base; capture primary status metadata.
- [ ] Inventory store lock, recovery, inventory, hydration, list, direct-read,
  parameter, generation, example, refresh, and save-publication consumers.
- [ ] Assert all Rank 29 settings, recorder, and prefix tokens are absent.
- [ ] Write external schema/privacy/parity assertions first and demonstrate the
  expected failure caused only by the absent temporary measurement surface.
- [ ] Record `RANK29_STATIC_BASELINE_CAPTURED` outside the repository.

Commit design and plan before source instrumentation.

## Task 2: Add Recorder and Inventory/Operation Measurement

- [ ] Add both temporary settings with XML documentation and comments.
- [ ] Add the internal recorder, bounded schema, operation state, timers,
  allocation counters, refresh timestamp, and nonthrowing async emission.
- [ ] Instrument enabled-only inventory, lookup, and snapshot operations while
  retaining explicit exact disabled bodies.
- [ ] Run privacy, public-signature, disabled-path, and whitespace gates.

Commit: `measure: add workflow hydration recorder`.

## Task 3: Add Per-File Hydration Measurement

- [ ] Split only the enabled null-record path into existence/read,
  parse/extract, and publication stages.
- [ ] Count cached, missing, invalid, example, source bytes, and retained field
  characters without recording names or contents.
- [ ] Preserve invalid logging/omission, missing removal, field conversion,
  fallback values, lock ownership, and cache publication ordering.
- [ ] Verify exact disabled control flow and exception isolation.

Commit: `measure: attribute workflow hydration stages`.

## Task 4: External Build and Source Review

- [ ] Build Release entirely under a fresh `/tmp` output/intermediate root.
- [ ] Confirm protected/generated repository paths did not change.
- [ ] Review source against privacy, store/recovery contracts, disabled cost,
  exception behavior, timing scopes, lock ownership, and public ABI.
- [ ] Correct every substantive issue and rebuild externally.

Expected gates: `RANK29_SOURCE_SPEC_APPROVED` and
`RANK29_SOURCE_QUALITY_APPROVED`.

## Task 5: Synthetic Collection

- [ ] Create only `/tmp` extension roots and workflow files.
- [ ] Run scale, cold/warm, direct-first, partial-cold, invalid/missing, example,
  refresh, prepublished, concurrency, and contamination cases.
- [ ] Use warm-ups plus exactly three measured repetitions for repeated groups.
- [ ] Validate result parity, sorting, cache state, schema, privacy, count
  conservation, nonnegative metrics, and deterministic error behavior.
- [ ] Measure forced-GC retained-memory deltas externally and label their noise.
- [ ] Hash evidence, summary, harness source, and instrumented artifact.

## Task 6: Decide and Document

- [ ] Compute nearest-rank p50/p95/max and keep nested timing non-additive.
- [ ] Separate stock work, asynchronous emission, and instrumentation
  contamination.
- [ ] Apply the qualitative gate without inventing a numeric threshold.
- [ ] Record executed/unrun boundaries and obtain independent evidence review.

Expected gates: `RANK29_EVIDENCE_VALID` plus `RANK29_DECISION_GO` or
`RANK29_DECISION_NO_GO`.

## Task 7: Remove Instrumentation and Verify Projection

- [ ] Remove every temporary setting, recorder, hook, token, and source file.
- [ ] Prove final `src` tree OID equals the approved base exactly.
- [ ] Run a fresh external Release build and focused uninstrumented synthetic
  sanity under the override.
- [ ] Finalize design and audit status, making Rank 30 the sole Recommended Next
  Project without beginning it in the Rank 29 branch.
- [ ] Obtain final specification, quality, and integrated-state approval.

Commit removal separately, then final documentation. Fast-forward local master
only after all gates pass; preserve primary dirty status exactly and do not push
without current authorization.
