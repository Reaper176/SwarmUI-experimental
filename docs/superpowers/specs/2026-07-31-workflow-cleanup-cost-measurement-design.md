# Generated-Workflow Cleanup Cost Measurement Design

**Date:** 2026-07-31
**Rank:** 27
**Status:** Design and implementation plan approved; ready for temporary instrumentation
**Approved base:** `020f619ce54918e074892da2e9756822807cefca`

## Decision Authority

Maintainer Reaper176 instructed the agent to continue through completion, use
best judgment without further questions, and self-test under a repository-policy
override. That instruction approves this bounded measurement design and permits
isolated external builds and workload validation. It does not authorize access
to real user data, production generation, or a cleanup optimization.

The selected approach is temporary, opt-in, rank-specific instrumentation at
the existing priority-200 action and `WorkflowGraphEditor` boundaries. A generic
profiler alone was rejected because it cannot attribute time and allocation to
the six class scans, connection-rewrite visits, or fixed-point passes. Replacing
the graph algorithm before measuring was rejected because materiality remains
unknown and the graph's mutation semantics are compatibility-sensitive.

## Problem and Evidence Boundary

The stock priority-200 workflow-generation step performs post-generation graph
cleanup. It:

1. snapshots and visits `KSampler` nodes;
2. snapshots and visits `LTXVSeparateAVLatent` nodes;
3. snapshots and visits `VAEDecode` nodes;
4. snapshots and visits `VAEDecodeTiled` nodes;
5. snapshots and visits `VAEEncode` nodes;
6. snapshots and visits `VAEEncodeTiled` nodes;
7. rewrites matching direct input connections while some callbacks remove
   nodes; and
8. repeatedly rebuilds connectivity and removes unused cleanup-class nodes
   until a complete pass makes no removal.

The six scans and fixed-point rebuilds are statically confirmed. Their cost and
materiality across generated-workflow families and graph sizes are not. Rank 27
is therefore a measurement prerequisite only. Standard workflows produced by
`WorkflowGenerator.Generate()` consume this cleanup. Stored or raw workflows
that bypass generation are outside the measured path.

## Goals

- Measure one aggregate for each executed priority-200 action.
- Measure the exact six stock `RunOnNodesOfClass` scans without changing their
  snapshot or callback behavior.
- Count nodes and direct input properties before and after cleanup.
- Measure connection-replacement calls, examined nodes, examined direct input
  properties, exact two-token matches, and replacements.
- Measure fixed-point passes, candidates, connectivity-index rebuilds, nodes
  and input properties scanned for connectivity, and removals.
- Measure cleanup elapsed time and attributable current-thread allocation.
- Measure post-cleanup serialization time, allocation, and compact serialized
  character count without retaining or logging the serialized graph.
- Compare disabled and enabled graph bytes, exceptions, public state, ordering,
  and references on deterministic synthetic graphs.
- Produce privacy-safe, machine-readable evidence and a qualitative decision.
- Remove every temporary setting, recorder, attempt, counter, and timer after
  the decision, restoring the exact approved-base `src` tree.

## Non-Goals

Rank 27 does not authorize:

- changing cleanup algorithms, combining scans, caching graph indexes, or
  replacing the fixed-point loop;
- changing workflow generation steps, their priorities, or extension order;
- changing the public `WorkflowGenerator` facade, method signatures, fields,
  return values, or ABI;
- changing node IDs, property order, callback order, removal order, connection
  matching, replacement reference identity, or `UsedInputs` behavior;
- measuring stored/raw workflow submission paths that do not run generation;
- a generic telemetry framework or reusable instrumentation for later ranks;
- raw node IDs, class names, model names, user/session identity, paths, prompts,
  parameters, input values, graph JSON, or exception text in records;
- a real model load, full `Generate()` claim, backend submission, server, GPU,
  filesystem, or production-workload claim;
- an invented numeric materiality threshold; or
- retaining dormant instrumentation after collection.

## Configuration

Two temporary fields are added to `ServerSettings.Performance`:

- `WorkflowCleanupMeasurementEnabled`, a Boolean defaulting to `false`; and
- `WorkflowCleanupMeasurementScenario`, a string defaulting to empty.

The scenario is an operator-supplied workload label. The recorder bounds its
length and accepts only a small safe character set suitable for labels such as
`synthetic/video/512-nodes/16-depth/r2`; invalid input becomes a fixed sentinel.
It must describe topology, never identity. No API route, browser control, or
new persistence mechanism is added.

The setting is checked only when a generation step has priority exactly `200`.
When disabled, the original `step.Action(this)` expression and subsequent
`SkipFurtherSteps` check execute in their original order. No attempt, graph
enumeration, timer, allocation counter, serialization, record, or
measurement-only collection is created. Every editor method retains its exact
original expression and control flow when no active attempt is bound.

## Measurement Architecture

### Rank-specific recorder

`src/BuiltinExtensions/ComfyUIBackend/WorkflowCleanupCostMeasurement.cs` owns:

- nonthrowing enable/scenario lookup;
- a monotonic process-local measurement ID;
- high-resolution timestamp conversion;
- bounded scenario normalization and outcome categories;
- the generator-bound priority-200 attempt and operation aggregates;
- compact JSON construction and serialization; and
- nonthrowing emission with prefix `[Rank27WorkflowCleanup]`.

The recorder is internal and specific to Rank 27. Measurement failures are
swallowed without exception text and cannot alter cleanup outcomes. Durations
are captured before record construction and logging. Allocation uses
`GC.GetAllocatedBytesForCurrentThread`; reported values are explicitly limited
to the synchronous measured thread and include enabled instrumentation that
runs inside the corresponding scope.

### Generator-bound attempt

`WorkflowGenerator` temporarily holds an internal active attempt only while a
step whose priority equals `200` executes. The attempt is installed immediately
before the action and cleared in `finally`. `WorkflowGraphEditor` reads that
attempt through its owning generator; it does not own or mirror workflow state.
The public generator methods remain the only facade exposed to callers.

The stock tree has one priority-200 action. If an extension registers another
action at the same priority, each action receives a separate aggregate with a
bounded ordinal, never an action name or type. Such extension records are not
silently combined with the stock cleanup record. The isolated harness asserts
that the stock action produces exactly six `RunOnNodesOfClass` scan entries.

On normal completion, the wrapper captures post-cleanup counts, serializes the
same `JObject` to compact JSON for measurement, discards the string after
recording its character count, and emits one aggregate. Measurement-only
serialization or emission failures are swallowed. If the cleanup action
throws, the wrapper best-effort emits a bounded `failed` outcome, skips
post-cleanup serialization, clears the attempt, and rethrows with `throw;` so
the original exception instance, stack behavior, and caller-visible result are
preserved.

### Editor operation scopes

When an attempt is active, `RunOnNodesOfClass` records one scan ordinal, nodes
examined by `NodesOfClass`, matching snapshot entries, callback invocations,
elapsed time, and allocation. The original snapshot is still completed before
the first callback. Measurement does not enumerate the workflow a second time
inside the timed operation.

`ReplaceNodeConnection` records calls, workflow nodes examined, direct input
properties examined, exact two-token matches, assignments, elapsed time, and
allocation. It retains the original workflow/input order and assigns the same
supplied `newNode` `JArray` reference to every match; it does not clone or
normalize either connection.

`RemoveClassesIfUnused` records fixed-point passes and candidate snapshots.
The existing `NodeIsConnectedAnywhere` path records each actual connectivity
index rebuild plus nodes and direct input properties examined. It does not
invalidate, eagerly construct, or replace `UsedInputs`; each pass still sets it
to `null`, the first lookup rebuilds it, later lookups reuse it, and the final
state is exactly the state produced by the original loop. Candidate and removal
order remains workflow-property order.

All graph counts are collected from existing visits where possible. Any
before/after aggregate count uses its own enabled-only traversal outside the
cleanup timer and is separately identified so it cannot be mistaken for
production cleanup work.

## Preserved Semantics

Instrumentation must preserve all of the following in disabled and enabled
modes:

- `WorkflowGenerator.Generate()` resets `Workflow`, executes sorted steps in
  order, checks `SkipFurtherSteps` after each action, and returns the same
  authoritative `JObject` reference;
- `WorkflowGenerator` remains the public facade and state owner;
- `RunOnNodesOfClass` snapshots matching `JProperty` references before any
  callback and invokes callbacks in snapshot order even when callbacks remove
  current or later properties;
- connection matching remains `JArray` only, count exactly two, with both
  tokens compared through their existing string conversion;
- every matching input receives the exact supplied replacement `JArray`
  reference in workflow/input-property order;
- cleanup-class candidates are snapshotted in workflow order on every pass;
- fixed-point termination occurs only after a complete pass removes nothing;
- connectivity matching, the `exclude` rule, `-1` wildcard entries, and
  `UsedInputs` invalidation/reuse/final-state behavior remain unchanged;
- node/property order, node IDs, `NodeHelpers`, `LastID`, and unrelated
  generator state remain unchanged; and
- malformed graphs retain the same success or exception behavior.

## Record Schema

Each emitted line is one compact JSON object after
`[Rank27WorkflowCleanup]`. The schema is fixed at version `1`. Durations are
nonnegative integer microseconds and counts are nonnegative integers. One
aggregate contains:

- `schema`, `record`, `measurement_id`, `scenario`, and priority-200 ordinal;
- bounded `completed` or `failed` outcome;
- pre/post node and direct-input-property counts when available;
- cleanup elapsed time and current-thread allocated bytes;
- `run_scan_count` and a six-entry bounded scan array for the stock action,
  containing only ordinal, examined/matched/callback counts, time, and bytes;
- aggregate replacement call, node, input-property, match, assignment, time,
  and byte counts;
- fixed-point pass, candidate, connectivity-rebuild, connectivity node/input,
  removal, time, and byte counts;
- post-cleanup serialization time, allocation, and compact character count; and
- explicit validity flags for fields unavailable after an exception or an
  isolated measurement failure.

No record contains graph content, raw identifiers/classes, model or user data,
paths, inputs, exception type/message/stack, or arbitrary object strings.
Recorder collections are bounded to the six expected stock scan slots; extra
same-priority behavior is represented by aggregate overflow counts rather than
unbounded detail.

## Collection Matrix

Collection uses an isolated external build and harness under `/tmp`. It never
points at the primary repository's `Data`, `Models`, or `Output`, does not load
models, and does not invoke a backend, server, or GPU. The harness invokes the
actual registered priority-200 action against synthetic `WorkflowGenerator`
graphs and uses fresh deterministic clones for each run.

The matrix covers:

1. disabled instrumentation and enabled empty/no-op controls;
2. still-image graphs with direct sampler and VAE paths;
3. refiner graphs with matched and unmatched VAE pairs;
4. ControlNet-style branching and fan-out;
5. regional-prompting-style branching and shared inputs;
6. video encode/decode graphs;
7. LTX video/audio separate/concat graphs and audio-only branches;
8. intermediate-save graphs that keep otherwise removable nodes connected;
9. graphs containing Dynamic Thresholding model connections;
10. controlled graph sizes of 16, 128, and 512 nodes;
11. removable dependency cascades of depth 1, 4, 16, and 32;
12. multiple exact replacement matches in one input object and across nodes;
13. non-array, wrong-length, and nonmatching direct input values;
14. snapshot-removal order and shared replacement-reference assertions;
15. malformed graph cases with enabled/disabled exception parity;
16. unsafe scenario normalization and recorder-failure isolation; and
17. a direct disabled-versus-enabled timing/allocation control around the
    priority-200 action, with graph construction and cloning outside the scope.

At least one warm-up precedes three measured repetitions for every scaling
topology and family/branch fixture. Warm-up records are explicitly marked and
excluded from reported percentiles. Every successful enabled run is compared
byte for byte with a disabled run from the same input graph. The harness also
compares node/property order, `Workflow` identity, replacement reference
identity, `UsedInputs`, `NodeHelpers`, `LastID`, and other exercised public
state. Malformed cases compare success/failure, exception type and message, and
post-failure graph/state externally; exception details never enter evidence.

## Decision Gate

The result is one of:

- `GO`: repeated priority-200 cleanup traversal is a material contributor to
  generation-side latency or allocation in representative exercised synthetic
  workflows, and a separately designed bounded editor optimization is
  justified;
- `NO-GO`: the measured cleanup is not repeatedly material in the exercised
  representative graphs, so no optimization is authorized; or
- `INSUFFICIENT`: evidence integrity, parity, coverage, or runtime fidelity is
  inadequate for either conclusion.

The decision is qualitative and evidence-backed. A `GO` authorizes only a new
design. It does not authorize combining scans, changing the fixed-point
algorithm, caching connectivity across mutations, or altering graph semantics.

## Static and Runtime Validation

Static validation must prove:

- the approved base and exact temporary instrumentation inventory;
- unchanged public declarations and `WorkflowGenerator` facade ownership;
- exact original disabled action/editor expressions and control flow;
- one generator-bound attempt per executed priority-200 action;
- exactly six stock class scans and correct operation-counter placement;
- timer/allocation boundaries that exclude JSON record construction/logging;
- exception parity and nonthrowing measurement cleanup/emission;
- bounded privacy-safe schema with no raw graph/class/ID/input/path/error data;
- no production algorithm, policy, cache, ordering, or reference change; and
- complete instrumentation removal after the decision.

Under the maintainer's explicit override, the agent may build and execute the
isolated matrix. Runtime validation records the environment, exact passed,
failed, and unrun totals, record count, evidence and summary digests, warm-up
separation, scenario coverage, direct-control results, and limitations. A
parity failure or unexplained instrumentation failure stops the decision and is
diagnosed before proceeding.

## Evidence Limits

The resulting evidence is synthetic graph-cleanup evidence only. It does not
establish real model availability, a complete real-model `Generate()` path,
backend claim/submission behavior, GPU performance, user-data behavior,
filesystem effects, production graph distribution, production GC impact, or
other platform/filesystem results. Serialization is an enabled-only diagnostic
of the post-cleanup graph and its cost must not be presented as existing cleanup
cost. Nested operation scopes overlap and must not be summed.

## Removal and Rollback

Before collection, reverting the temporary instrumentation commits in reverse
order is the rollback. During collection,
`WorkflowCleanupMeasurementEnabled = false` immediately restores the normal
path. After the decision, the normal final state removes both settings, the
rank-specific recorder, the generator-bound attempt, every editor hook, and all
Rank 27 prefixes. The final committed `src` tree must be byte-for-byte and tree-
OID identical to approved base `020f619ce54918e074892da2e9756822807cefca`.

Only this design, its later implementation plan, the audit status, and final
evidence/decision documentation may remain. No Rank 28 status is advanced until
Rank 27 collection, decision, removal, and final projection validation close.
