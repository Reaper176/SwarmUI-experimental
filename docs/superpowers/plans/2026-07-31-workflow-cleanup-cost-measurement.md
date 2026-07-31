# Generated-Workflow Cleanup Cost Measurement Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use
> `superpowers:executing-plans` to implement this plan task-by-task.

**Goal:** Temporarily measure the existing priority-200 generated-workflow
cleanup, make a qualitative optimization decision from parity-checked evidence,
and remove all instrumentation so production source exactly matches the
approved base.

**Architecture:** An opt-in Rank-27 recorder binds one attempt to
`WorkflowGenerator` while each priority-200 action executes. The existing
`WorkflowGraphEditor` operations add counters and timings only when that attempt
is present; their disabled branches retain the approved expressions and control
flow. One bounded aggregate is emitted per action. Evidence and builds stay
outside the repository.

**Tech stack:** C# 12, .NET 8, Newtonsoft.Json, Bash static gates, an isolated
`/tmp` build/harness, and Git tree projections.

---

## Authority and Safety Boundary

Maintainer Reaper176 authorized autonomous completion and self-testing as a
repository-policy override. The override covers this isolated build and
synthetic graph matrix. It does not permit reading or writing repository
`Data`, `Models`, `Output`, `src/Data`, generated `src/bin`/`src/obj`, external
extensions, or protected primary-worktree contents. The primary dirty path set
is compared as status metadata only.

Approved base: `020f619ce54918e074892da2e9756822807cefca`.

## File Responsibility Map

Temporary source phase:

- `src/Core/Settings.cs`: two opt-in performance fields.
- `src/BuiltinExtensions/ComfyUIBackend/WorkflowCleanupCostMeasurement.cs`:
  rank-specific attempt, schema, timing, allocation, normalization, and
  nonthrowing emission.
- `src/BuiltinExtensions/ComfyUIBackend/WorkflowGenerator.cs`: generator-bound
  attempt and priority-200 wrapper only.
- `src/BuiltinExtensions/ComfyUIBackend/WorkflowGraphEditor.cs`: enabled-only
  scan, replacement, connectivity, and fixed-point counters/timers.

Permanent documentation:

- the approved design;
- this plan; and
- `docs/superpowers/audits/2026-07-21-maintainability-architecture-refresh.md`.

The final `src` tree must equal the approved base `src` tree exactly.

## Task 1: Freeze the Base and Establish RED

- [ ] Confirm the worktree is clean at the approved design/plan history and
  descends from the approved base.
- [ ] Capture the primary `git status --short` path set without reading dirty
  contents.
- [ ] Inventory the one stock priority-200 action, its six class scans, public
  facade declarations, editor bodies, and all maintained consumers.
- [ ] Assert `WorkflowCleanupMeasurementEnabled`,
  `WorkflowCleanupMeasurementScenario`, `WorkflowCleanupCostMeasurement`, and
  `[Rank27WorkflowCleanup]` are absent.
- [ ] Create the external harness assertions first and run the focused compile
  or static gate. Record the expected RED result caused only by the absent
  measurement surface; do not write production code before this failure.
- [ ] Record `RANK27_STATIC_BASELINE_CAPTURED` in execution notes outside the
  repository.

Commit this plan before instrumentation:

```text
docs: plan workflow cleanup cost measurement
```

## Task 2: Add Temporary Settings and Recorder

- [ ] Add `WorkflowCleanupMeasurementEnabled = false` and empty
  `WorkflowCleanupMeasurementScenario` after `ModelListSanityCap`, with XML
  docs and `ConfigComment` text that identifies them as temporary measurement
  controls.
- [ ] Add the internal recorder with monotonic IDs, safe scenario normalization
  (lowercase ASCII letters, digits, `/`, `_`, `-`, 96-character cap), bounded
  outcomes, high-resolution microseconds, and current-thread allocation.
- [ ] Define a bounded attempt with exactly six stock scan slots plus overflow
  counters and aggregate replacement/fixed-point/connectivity fields.
- [ ] Capture durations before JSON construction/logging and swallow all
  measurement failures without exception details.
- [ ] Emit compact schema-1 JSON with `[Rank27WorkflowCleanup]` and no raw graph,
  class, node, model, user, path, input, or error values.
- [ ] Run focused privacy/schema/static assertions and `git diff --check`.

Commit:

```text
measure: add workflow cleanup cost recorder
```

## Task 3: Wrap Priority-200 Actions Without Changing the Disabled Path

- [ ] Add one internal nullable attempt field to `WorkflowGenerator`; do not
  add or change any public member.
- [ ] In `Generate`, preserve `Workflow = []`, step enumeration/order, the exact
  disabled `step.Action(this)` call, the following `SkipFurtherSteps` check, and
  return identity.
- [ ] Only when enabled and `step.Priority == 200`, begin one attempt, bind it
  immediately before the action, and clear it in `finally`.
- [ ] Distinguish same-priority actions by a bounded ordinal, never action
  identity. The stock action must yield one record with six scan slots.
- [ ] On success, collect enabled-only before/after counts, compact serialization
  time/allocation/character count, then emit after measured timers stop.
- [ ] On action failure, best-effort emit bounded `failed`, skip serialization,
  clear the field, and rethrow with `throw;`.
- [ ] Measurement enumeration/serialization/emission failures must not change
  the action result, graph, exception, or later field cleanup.

## Task 4: Instrument Editor Operations With Exact Original Disabled Branches

- [ ] `RunOnNodesOfClass`: preserve the original snapshot-before-callback body
  when no attempt is active; when active, count examined workflow nodes,
  matches/callbacks, elapsed time, and allocation without a second timed scan.
- [ ] `ReplaceNodeConnection`: preserve the exact original nested enumeration,
  two-token comparisons, and supplied-reference assignment when disabled;
  measure calls, nodes, direct inputs, matches/assignments, time, and allocation
  during the same enabled visits.
- [ ] `NodeIsConnectedAnywhere`: retain null-triggered cache construction,
  exclude behavior, wildcard/output entries, and cache reuse; count only actual
  enabled rebuild visits.
- [ ] `RemoveClassesIfUnused`: retain the exact original fixed-point loop when
  disabled; enabled logic records passes, snapshot candidates, removals, time,
  and allocation without changing pass/removal order.
- [ ] Do not instrument `RemoveClassIfUnused`, other editor methods, or any
  non-priority-200 use when no bound attempt exists.
- [ ] Check public declarations, original disabled expressions, one predicate
  invocation per original visit, timing scopes, privacy, and whitespace.

Commit Tasks 3-4 together if an intermediate state would not compile:

```text
measure: instrument workflow cleanup traversal
```

## Task 5: External Build and Independent Source Reviews

- [ ] Copy no user data. Build `src/SwarmUI.csproj` in Release with output and
  intermediate paths under a fresh `/tmp/rank27-build-*` directory.
- [ ] Confirm repository `src/bin` and `src/obj` status/path metadata did not
  change.
- [ ] Verify the temporary inventory is exactly the four source paths above,
  public declarations match the approved base, and disabled bodies preserve
  semantics.
- [ ] Obtain independent specification review against the design and quality
  review focused on exception isolation, timer/allocation fidelity, bounded
  collections, reference identity, and disabled-path behavior.
- [ ] Diagnose and correct every substantive issue, rebuilding externally.

Expected tokens:

```text
RANK27_SOURCE_SPEC_APPROVED
RANK27_SOURCE_QUALITY_APPROVED
```

## Task 6: Run the Isolated Parity and Scaling Harness

- [ ] Build a `/tmp` .NET 8 harness against the external artifact. Locate the
  actual registered priority-200 action and invoke it on fresh deterministic
  `WorkflowGenerator` graphs; do not invoke models, full `Generate`, backends,
  server, GPU, network, or repository data.
- [ ] Run disabled and enabled empty/no-op controls.
- [ ] Cover still, refiner, ControlNet-style fan-out, regional shared inputs,
  video encode/decode, LTX video/audio separate/concat, audio-only,
  intermediate-save retention, and Dynamic Thresholding connections.
- [ ] Cover 16/128/512-node graphs, cascade depths 1/4/16/32, repeated exact
  replacements, non-array/wrong-length/nonmatching inputs, snapshot-removal
  order, shared replacement reference, and malformed shapes.
- [ ] Run at least one warm-up followed by exactly three measured repetitions
  for each family/scaling topology; mark warm-ups explicitly.
- [ ] For each successful fixture compare compact graph bytes, property order,
  workflow identity, replacement references, `UsedInputs`, `NodeHelpers`,
  `LastID`, and exercised public state between disabled and enabled clones.
- [ ] For malformed fixtures compare success/failure, exception type/message,
  partial graph, and state externally; never log error details in evidence.
- [ ] Exercise unsafe scenario normalization and recorder/emitter failure
  isolation.
- [ ] Run a repeated direct disabled-versus-enabled control with graph creation
  and cloning outside the measured scope.
- [ ] Report exact passed/failed/unrun totals. Any failure triggers systematic
  diagnosis before analysis.

## Task 7: Validate and Analyze Evidence

- [ ] Extract only prefixed JSON to `/tmp/rank27-records.jsonl` and write an
  external summary JSON.
- [ ] Validate schema, bounded outcomes/scans, safe scenarios, nonnegative
  counts/durations, correlation IDs, and forbidden key/value absence.
- [ ] Confirm disabled runs emitted no records, warm-ups are excluded, every
  scaled group has r1/r2/r3, stock success has six scan slots, and graph parity
  assertions passed.
- [ ] Record SHA-256, line/type/scenario counts, matrix totals, Garuda/Arch,
  Btrfs-repository, architecture, and .NET versions.
- [ ] Use nearest-rank `x[ceil(pN)]` without interpolation for p50/p95/max of
  cleanup, six scans, replacement, fixed-point/connectivity, allocation, and
  diagnostic serialization. Keep graph nodes/inputs/passes/removals beside
  time. Do not sum nested scopes.
- [ ] Separate enabled instrumentation/serialization from existing cleanup and
  use the direct control to bound contamination.
- [ ] Decide `GO`, `NO-GO`, or `INSUFFICIENT` qualitatively. Do not infer real
  model, full-generation, backend-claim, production-GC, or unrun environments.

## Task 8: Record the Decision and Remove Instrumentation

- [ ] Update the design and audit with exact commits, reviews, environment,
  matrix totals, evidence hashes/counts, metrics, limitations, and decision.
- [ ] If Rank 27 closes, make Rank 28 the sole Recommended Next Project;
  otherwise keep Rank 27. Do not claim Rank 28 is designed or implemented.
- [ ] Commit the evidence record before source removal:

```text
docs: record workflow cleanup measurement evidence
```

- [ ] Using focused `apply_patch` edits, remove both settings, recorder file,
  generator field/wrapper, and every editor hook.
- [ ] Assert every Rank-27 token is absent and `HEAD:src` equals the approved
  base `src` tree OID exactly.
- [ ] Commit:

```text
refactor: remove workflow cleanup measurement instrumentation
```

## Task 9: Post-Removal Validation and Final Documentation

- [ ] Build the restored source externally under fresh `/tmp` output paths.
- [ ] Run an uninstrumented priority-200 graph cleanup sanity covering scan,
  replacement, cascade, `UsedInputs`, and exception behavior supported by the
  harness. Mark unsupported real-generation/backend cases unrun.
- [ ] Record the removal commit, exact tree OID, build warnings/errors, sanity
  result, and any harness-only corrections without implying a production bug.
- [ ] Commit:

```text
docs: finalize workflow cleanup measurement decision
```

## Task 10: Final Verification and Local Integration

- [ ] Use `superpowers:verification-before-completion` and rerun the complete
  source tree, token, docs, evidence, hash, external build, sanity, clean-status,
  and primary dirty-path checks from final HEAD.
- [ ] Obtain final independent specification and quality/integrated reviews.
- [ ] Fast-forward local `master` only if its HEAD and protected dirty path set
  still match the recorded baseline. Do not push without current authorization.
- [ ] Recheck the primary status, remove the clean Rank-27 worktree, delete the
  merged branch, mark Rank 27 complete, and start Rank 28 autonomously.

Expected final tokens:

```text
RANK27_VALIDATION_SPEC_APPROVED
RANK27_VALIDATION_QUALITY_APPROVED
RANK27_FINAL_INTEGRATED_APPROVED
```
