# File-Media Conversion Measurement Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use
> `superpowers:executing-plans` to implement this plan task-by-task.

**Goal:** Measure conditional request and late-tag path-backed media conversion,
make a qualitative optimization decision, and restore production source exactly.

**Architecture:** An opt-in rank-specific recorder owns privacy-safe per-call
records and nested `AsyncLocal` scopes. Existing conversion, preset, late-tag,
and pre-backend owners receive enabled-only hooks without public API changes.

**Tech stack:** C# 12, .NET 8, Newtonsoft.Json, Bash static gates, and an isolated
`/tmp` harness using synthetic files only.

## Authority and Safety

Reaper176 authorized autonomous completion and self-testing. Do not access
repository `Data`, `Models`, `Output`, `src/Data`, generated `src/bin`/`src/obj`,
or external extensions. Preserve the primary dirty path set as metadata only.

Approved base: `bdbce25dc023e0661d50fe1b66094741128c8271`.

## Task 1: Freeze Base and Establish RED

- [ ] Confirm the isolated worktree is clean and based exactly on the approved
  base; capture primary status metadata.
- [ ] Inventory all maintained conversion, preset, late, clone, claim, and
  backend-request owners.
- [ ] Assert all Rank 28 settings, recorder, and prefix tokens are absent.
- [ ] Write external schema/privacy/parity assertions first and demonstrate the
  expected failure caused only by the absent temporary measurement surface.
- [ ] Record `RANK28_STATIC_BASELINE_CAPTURED` outside the repository.

Commit design and plan before source instrumentation.

## Task 2: Add Recorder and File-Call Measurement

- [ ] Add the two temporary settings with XML documentation and comments.
- [ ] Add the internal recorder, bounded scenario/context/outcome categories,
  monotonic IDs, path ordinals, nested scopes, timers, allocation counters, and
  compact nonthrowing emission.
- [ ] Instrument media item/bypass accounting and `FilePathToDataString` timing
  while preserving exact path-check/source/read/base64 behavior.
- [ ] Run privacy, public-signature, disabled-path, and whitespace gates.

Commit: `measure: add file-media conversion recorder`.

## Task 3: Add Context and Placement Scopes

- [ ] Wrap `RequestToParams`, `T2IPreset.ApplyTo`, and
  `ApplyLateSpecialLogic` without changing their original body order.
- [ ] Add the engine-pre-backend outer scope without moving late handling,
  events, claims, status, matcher construction, or backend acquisition.
- [ ] Ensure nested inclusive records cannot be incorrectly summed and caller
  classification uses only enabled fixed literals.
- [ ] Verify exception isolation and exact disabled control flow.

Commit: `measure: attribute file-media conversion contexts`.

## Task 4: External Build and Source Review

- [ ] Build Release entirely under a fresh `/tmp` output/intermediate root.
- [ ] Confirm protected/generated repository paths did not change.
- [ ] Review source against the design, privacy boundary, disabled-path cost,
  exception behavior, timing scopes, `AsyncLocal` nesting, and public ABI.
- [ ] Correct every substantive issue and rebuild externally.

Expected gates: `RANK28_SOURCE_SPEC_APPROVED` and
`RANK28_SOURCE_QUALITY_APPROVED`.

## Task 5: Synthetic Collection

- [ ] Create only `/tmp` synthetic files and an isolated session/root.
- [ ] Run disabled/enabled parity, source, scale, repetition, pending-wait,
  bypass/list, error/privacy, clone/late, concurrency, and contamination cases.
- [ ] Use warm-ups plus exactly three measured repetitions for repeated groups.
- [ ] Validate schema, bounded values, nonnegative metrics, no paths/content/user
  data, source counts, path-ordinal reuse, result bytes, and exception parity.
- [ ] Hash evidence, summary, harness source, and instrumented artifact.

## Task 6: Decide and Document

- [ ] Compute nearest-rank p50/p95/max and clearly label nested scope overlap.
- [ ] Separate stock work from instrumentation/logging contamination.
- [ ] Apply the qualitative gate without inventing a numeric threshold.
- [ ] Record executed and unrun boundaries and obtain independent evidence
  review before changing the audit disposition.

Expected gate: `RANK28_EVIDENCE_VALID` plus `RANK28_DECISION_GO` or
`RANK28_DECISION_NO_GO`.

## Task 7: Remove Instrumentation and Verify Projection

- [ ] Remove every temporary setting, recorder, hook, token, and source file.
- [ ] Prove final `src` tree OID equals the approved base exactly.
- [ ] Run a fresh external Release build and focused uninstrumented synthetic
  sanity under the override.
- [ ] Finalize design and audit status, making Rank 29 the sole Recommended Next
  Project without beginning it in the Rank 28 branch.
- [ ] Obtain final specification, quality, and integrated-state approval.

Commit removal separately, then final documentation. Fast-forward local master
only after all gates pass; preserve the primary dirty status exactly and do not
push without current authorization.
