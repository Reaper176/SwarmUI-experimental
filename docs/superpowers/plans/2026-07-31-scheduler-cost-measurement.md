# Scheduler Snapshot/Filter/Sort Measurement Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use
> `superpowers:executing-plans` to implement this plan task-by-task.

**Goal:** Temporarily instrument the existing T2I backend scheduler, collect an
isolated privacy-safe workload profile, record a qualitative decision, and
remove all instrumentation so the final production source equals the approved
base.

**Architecture:** A Rank-26-only recorder emits opt-in `pass`, `try_find`, and
`pressure` JSON records. Existing public methods delegate to internal measured
overloads, while the disabled branches retain the original scheduler
expressions. Maintained request, release, and shutdown signals are classified
without replacing the public auto-reset event. No scheduler behavior or cache
is changed.

**Tech stack:** C# 12, .NET 8, FreneticUtilities, Newtonsoft.Json, Bash static
gates, isolated `/tmp` build/harness, Git projection checks.

---

## Repository-Policy Override and Safety Boundary

Maintainer Reaper176 explicitly authorized the agent to continue until
completion and self-test under a one-time repository-rule override. For Rank 26
that authorizes the isolated build and workload matrix in this plan. It does not
authorize real user-data access. Never read or write repository `Data`,
`Models`, `Output`, `src/Data`, generated `src/bin`/`src/obj`, or protected
primary-worktree contents. Build, harness, logs, and evidence live under `/tmp`.

The primary worktree contains user-owned changes. Only status/path metadata and
a digest of that status are inspected. All implementation happens in
`.worktrees/rank26-scheduler-measurement`.

## File Responsibility Map

Temporary source phase:

- Create `src/Backends/SchedulerCostMeasurement.cs`: settings lookup, signal
  snapshot, attempts, schema, timing, allocation, normalization, and logging.
- Modify `src/Core/Settings.cs`: two temporary opt-in fields.
- Modify `src/Backends/BackendHandler.cs`: maintained signal classification and
  pass/request/pressure measurement hooks only.

Permanent documentation:

- Modify
  `docs/superpowers/specs/2026-07-31-scheduler-cost-measurement-design.md` with
  evidence, decision, validation, and removal outcome.
- Modify
  `docs/superpowers/audits/2026-07-21-maintainability-architecture-refresh.md`
  with Rank 26 disposition and the next recommendation.
- Retain this plan.

The final `src` tree must equal approved base
`7dfc73022684f1d51b89fc7bd3235c90535ab8c9:src` exactly.

### Task 1: Freeze the Base and Establish RED

**Files:** read the design, audit, settings, and scheduler source.

- [ ] Verify worktree `HEAD` descends from the approved base, the committed
  source tree equals the approved base, and worktree status contains only the
  committed design/plan history.
- [ ] Capture the primary `git status --short` digest without reading protected
  contents.
- [ ] Inventory public declarations and the three maintained
  `CheckBackendsSignal.Set()` call sites.
- [ ] Assert the following are absent from production source:
  `SchedulerMeasurementEnabled`, `SchedulerMeasurementScenario`,
  `Rank26Scheduler`, `SchedulerCostMeasurement`, and `SignalScheduler`.
- [ ] Record `RANK26_STATIC_BASELINE_CAPTURED` in execution notes, not a
  generated repository file.

Commit the plan before implementation:

```text
docs: plan scheduler cost measurement
```

### Task 2: Add Temporary Settings and Recorder

**Files:** modify `src/Core/Settings.cs`; create
`src/Backends/SchedulerCostMeasurement.cs`.

- [ ] Add `SchedulerMeasurementEnabled = false` and empty
  `SchedulerMeasurementScenario` fields after `ModelListSanityCap`, with XML
  docs and `ConfigComment` guidance.
- [ ] Create an internal static recorder and internal attempt classes.
- [ ] Implement nonthrowing enable/scenario lookup and scenario normalization:
  lowercase ASCII letters, digits, `/`, `_`, and `-`; replace other runs with
  `_`; cap at 96 characters; return `unspecified` when empty.
- [ ] Implement high-resolution monotonic microsecond conversion without wall
  clock output.
- [ ] Implement pass IDs with `Interlocked.Increment`.
- [ ] Implement `SchedulerMeasurementSignalSnapshot`, pass, request, and
  pressure attempts with bounded public-to-file visibility.
- [ ] Emit compact JSON with prefix `[Rank26Scheduler]`; catch all recorder and
  logger failures without exception text.
- [ ] Ensure finish methods capture elapsed time/allocation before JObject/
  serialization/logging.
- [ ] Add no raw identity/path/error fields.
- [ ] Run `dotnet format` only if it can target the changed files without
  writing repository build directories; otherwise use source-format review.
- [ ] Run `git diff --check` and focused schema/privacy assertions.

Commit:

```text
measure: add scheduler cost recorder
```

### Task 3: Classify Maintained Signals Without Changing Event Behavior

**File:** modify `src/Backends/BackendHandler.cs`.

- [ ] Add private measurement-only signal sequence/timestamp/source state and
  the last-observed sequence needed by the scheduler loop.
- [ ] Add internal `SignalScheduler(string source)` that records only when
  enabled, then always invokes `CheckBackendsSignal.Set()` once.
- [ ] Replace exactly the maintained shutdown, new-request, and access-release
  sites with `shutdown`, `request`, and `release` calls respectively.
- [ ] Preserve ordering at each site: existing state mutation remains before
  the signal, and no new await/lock is introduced.
- [ ] Keep the public `CheckBackendsSignal` field unchanged for extension
  compatibility.
- [ ] Add a recorder snapshot method that reads sequence/timestamp/source
  consistently enough for diagnostics and degrades to bounded
  `timeout_or_external`; it must not spin or block scheduler state.
- [ ] Assert there remain exactly three maintained semantic signal sites and
  each invokes the public event once through the helper.

Commit signal work together with Task 4 if intermediate source would not build.

### Task 4: Instrument Scheduler Passes

**File:** modify `src/Backends/BackendHandler.cs`.

- [ ] Begin at most one optional pass attempt per loop iteration after the
  shutdown check and before the pending-request snapshot.
- [ ] Retain the existing `Values.ToArray()` iteration order/source expression.
- [ ] Count visits, cancellations, claims, failures, and waiting outcomes from
  existing branches only.
- [ ] Pass the attempt and a one-based ordinal to the internal `TryFind`
  overload.
- [ ] Capture processing-active time before the existing wait and wait time
  around the existing `WaitAsync(...).Wait()` call.
- [ ] Preserve `MonitorTimes`/`BackendQueueTimer` behavior and ordering.
- [ ] Finish the pass in a nonthrowing `finally` path for ordinary, caught-error,
  and wait-error cases; shutdown before a pass creates no false record.
- [ ] Notify the pass only at the two existing successful `T2IBackendAccess`
  claim assignments.
- [ ] Do not alter any removal, completion-event, timeout, restart, or tick
  event behavior.

### Task 5: Instrument Request Search and Pressure Selection

**File:** modify `src/Backends/BackendHandler.cs`.

- [ ] Preserve public `TryFind()` and
  `LoadHighestPressureNow(List<...>, List<...>, Action, ...,
  CancellationToken)` declarations exactly; add internal overloads for pass
  correlation.
- [ ] Wrap enabled request search in `try/catch/finally` solely to record a
  bounded outcome and rethrow existing exceptions.
- [ ] Time extension gates, backend snapshot/possible filtering, request
  matcher, availability/sort, loaded-model search, pressure selection, and
  total request search.
- [ ] In disabled code paths retain direct `Where(Filter)` and nested original
  matcher expressions. In enabled paths wrap each matcher invocation once,
  preserving short-circuit order and refusal side effects.
- [ ] Count candidates from already-created lists; do not add a second catalog
  or matcher traversal.
- [ ] Time pressure snapshot/sort, compatible/perfect nested matcher work, and
  remaining bounded selection filters. Exclude asynchronous model loading.
- [ ] Set outcomes immediately before existing return/throw/load-publication
  boundaries. Outcome recording must not become a new branch condition.
- [ ] Aggregate matcher calls/time and pressure time into the pass without
  locking; the scheduler loop is the sole pass owner.
- [ ] Ensure all measurement timers stop before JSON/logging.
- [ ] Run public-signature, matcher-count, privacy, and control-flow static
  assertions plus `git diff --check`.

Commit Tasks 3–5:

```text
measure: instrument scheduler selection path
```

### Task 6: Isolated Build and Source Reviews

- [ ] Copy no user data. Create a fresh `/tmp/rank26-build-*` directory.
- [ ] Build `src/SwarmUI.csproj` with both `BaseOutputPath` and
  `BaseIntermediateOutputPath` outside the repository.
- [ ] Confirm no timestamps changed under repository `src/bin` or `src/obj` by
  comparing path/status metadata before and after.
- [ ] Run static assertions for exact settings, prefix, public declarations,
  signal order, original disabled matcher expressions, bounded schema, and
  measurement-only file inventory.
- [ ] Perform independent specification review against the approved design.
- [ ] Perform independent quality review focused on exception isolation,
  concurrency, allocation fidelity, matcher multiplicity, and disabled-path
  behavior.
- [ ] Fix every substantive finding and rebuild externally.
- [ ] Record `RANK26_SOURCE_SPEC_APPROVED` and
  `RANK26_SOURCE_QUALITY_APPROVED` only with fresh evidence.

### Task 7: Build and Run the Isolated Workload Harness

**Files:** harness exists only under `/tmp`; do not commit it.

- [ ] Create a minimal external .NET 8 console harness referencing the isolated
  SwarmUI build.
- [ ] Define a deterministic fake `AbstractT2IBackend` with no filesystem,
  network, GPU, model, or process dependencies.
- [ ] Construct `BackendHandler.T2IBackendData`, fake models, requests, filters,
  and pressures in memory. Use reflection only for internal measurement hooks;
  exercise public scheduler behavior wherever possible.
- [ ] Capture logs/evidence to `/tmp/rank26-records.jsonl` without reading
  repository logs or settings.
- [ ] Run warm-up and at least three measured repetitions for:
  disabled, idle, loaded-model claim, same-model queues, distinct-model queues,
  backend scaling, cheap accept/refuse matcher, bounded expensive matcher,
  no-backend/autoscale, cancellation, release progression, pressure refusals,
  and concurrent publication/signaling.
- [ ] Assert claims, usages, pressure counts, outcomes, filter side effects,
  ordering, and cancellations match an uninstrumented/disabled control.
- [ ] Exercise scenario normalization and force recorder/logging failure where
  safely injectable; scheduler outcomes must remain unchanged.
- [ ] Do not claim unsupported real Comfy/GPU, external extension, or production
  contention behavior.
- [ ] Report exact passed, failed, and unrun cases. Any failure triggers
  systematic diagnosis before analysis.

### Task 8: Validate and Analyze Evidence

- [ ] Extract only prefixed compact JSON lines to
  `/tmp/rank26-records.jsonl`.
- [ ] Validate every line parses and has schema 1, a known record/outcome, one
  nonempty safe scenario, nonnegative counts/durations, and no forbidden keys or
  path-like values.
- [ ] Confirm pass IDs correlate records and no disabled-control records exist.
- [ ] Record SHA-256, line count, record-type counts, scenario counts, and
  environment (`Garuda Linux`, Arch-based, Btrfs, .NET version).
- [ ] Compute nearest-rank p50/p95/max for active pass time, try-find total,
  matcher time, pressure-selection time, allocation, and signal-to-first-claim;
  retain cardinalities and outcomes beside timing.
- [ ] Separate measured operation time from instrumentation/logging and from
  intentional wait/model-load time.
- [ ] Make a qualitative `GO`, `NO-GO`, or `INSUFFICIENT` decision using only
  exercised evidence. Do not invent a threshold or infer unrun environments.

### Task 9: Record Decision and Remove Instrumentation

- [ ] Update the design with implementation commits, exact environment/matrix,
  evidence digest/counts, analysis, limitations, and decision.
- [ ] Update Rank 26 and Recommended Next in the architecture audit. If Rank 26
  closes, promote Rank 27 as the sole next project; do not imply Rank 27 is
  designed or implemented.
- [ ] Commit evidence documentation before removal:

```text
docs: record scheduler measurement evidence
```

- [ ] Revert/remove every temporary source/settings/signal change using a
  focused patch, not a destructive worktree reset.
- [ ] Assert all Rank 26 source tokens and recorder file are absent.
- [ ] Assert final `src` tree OID equals approved base exactly.
- [ ] Commit:

```text
refactor: remove scheduler measurement instrumentation
```

- [ ] Run an external post-removal build and the supported disabled scheduler
  behavior sanity. Mark unsupported runtime cases unrun rather than inferred.
- [ ] Update final docs with removal commit/tree OID and post-removal result.
- [ ] Commit:

```text
docs: finalize scheduler measurement decision
```

### Task 10: Final Rank-26 Verification and Local Integration

- [ ] Use `superpowers:verification-before-completion`.
- [ ] Confirm worktree clean, expected commit range, final source projection,
  no instrumentation tokens, documentation consistency, and evidence digest.
- [ ] Re-run final integrated specification and quality review.
- [ ] Compare primary protected-status digest with the baseline.
- [ ] Fast-forward local `master` only if its HEAD remains the approved base and
  the protected digest matches. Do not push unless separately authorized.
- [ ] Recheck protected status after integration, remove the clean Rank 26
  worktree, and delete the merged feature branch.
- [ ] Mark Rank 26 complete and start Rank 27 autonomously.

Expected final tokens:

```text
RANK26_VALIDATION_SPEC_APPROVED
RANK26_VALIDATION_QUALITY_APPROVED
RANK26_FINAL_INTEGRATED_APPROVED
```
