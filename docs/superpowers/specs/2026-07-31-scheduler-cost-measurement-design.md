# Scheduler Snapshot/Filter/Sort Measurement Design

**Date:** 2026-07-31
**Rank:** 26
**Status:** Approved by delegated maintainer judgment; implementation pending
**Approved base:** `7dfc73022684f1d51b89fc7bd3235c90535ab8c9`

## Decision Authority

Maintainer Reaper176 instructed the agent to continue through completion, use
best judgment, ask no further questions, and self-test under a repository-policy
override. That instruction delegates the design choice and approves isolated
build and workload validation for the remaining refactor measurements. It does
not authorize access to real user data or changes outside the bounded project.

The selected approach is temporary, opt-in, structured instrumentation at the
existing scheduler boundaries. Direct ad-hoc log timers were rejected because
they cannot reliably correlate a wake, request visit, pressure selection, and
claim. An external profiler alone was rejected because it cannot distinguish
queue scenarios, matcher cardinality, branch outcomes, or signal-to-claim
latency. A scheduler optimization is not part of this design.

## Problem and Evidence Boundary

`BackendHandler.RequestHandlingLoop` processes a snapshot of every pending T2I
backend request on each pass. Every `T2IBackendRequest.TryFind` then:

1. snapshots all T2I backends;
2. filters enabled, running, unreserved backends with capacity;
3. runs the request matcher when present;
4. filters and sorts available backends by usage;
5. searches for an already-loaded requested model; and
6. on a model miss, enters `LoadHighestPressureNow`.

The pressure path snapshots and sorts non-loading model pressures, then performs
nested request-filter/backend compatibility checks before selecting a model and
loader. Pending requests that do not progress repeat this work after later
signals or the one-second fallback wake.

Those mechanisms and their multiplicity are statically confirmed. Their
materiality is not. Rank 26 is therefore a measurement prerequisite only.

## Goals

- Measure scheduler pass, request-search, matcher, and pressure-selection time.
- Record the scaling inputs: pending requests, backends, available candidates,
  distinct pressures, pressure membership, matcher invocations, and signals.
- Measure attributable current-thread allocation for the pass and its nested
  request/pressure operations.
- Relate a maintained signal or timeout wake to the first claim in that pass.
- Distinguish idle, loaded-model, same-model, distinct-model, refusal,
  autoscaling, cancellation, and backend-release scenarios.
- Preserve the latest capacity, usage, reservation, status, loaded-model,
  matcher, refusal, autoscaling, cancellation, timeout, and ordering behavior.
- Produce privacy-safe machine-readable evidence with a stable schema.
- Remove every temporary setting, recorder, signal hook, counter, and timer
  after recording the decision.

## Non-Goals

Rank 26 does not authorize:

- sharing a stale per-pass availability or capacity snapshot between requests;
- caching matcher results, pressure compatibility, model state, or backend
  selection;
- changing request iteration, extension `CanTryFindNow`, usage ordering, model
  pressure heuristic/order, refusal reasons, autoscaler behavior, or claims;
- changing `RequestHandlingLoop`, `TryFind`, `LoadHighestPressureNow`,
  `GetNextT2IBackend`, or `CheckBackendsSignal` public compatibility;
- changing scheduler threading, locks, task ownership, cancellation, or timeout
  semantics;
- a generic telemetry framework or instrumentation for later ranks;
- raw user/session/model/backend/request identity, prompts, parameters, paths,
  exception messages, or settings in measurement output;
- an invented numeric materiality threshold; or
- retaining dormant instrumentation after collection.

## Configuration

Two temporary fields are added to `ServerSettings.Performance`:

- `SchedulerMeasurementEnabled`, a Boolean defaulting to `false`;
- `SchedulerMeasurementScenario`, a string defaulting to empty.

The scenario is operator-supplied, normalized to a bounded safe character set,
and must describe topology/workload rather than identity, for example
`synthetic/32-pending/8-backends/distinct-model`. The existing settings surface
is reused. No API route, browser control, or persistence mechanism is added.

When disabled, no measurement object, identifier, timestamp, allocation
counter, record, or measurement-only collection is created beyond guarded
setting checks. Expressions that execute matchers and enumerate scheduler state
retain their original disabled form.

## Measurement Architecture

### Rank-specific recorder

`src/Backends/SchedulerCostMeasurement.cs` owns:

- nonthrowing enable and scenario lookup;
- monotonic process-local pass IDs;
- high-resolution timestamp conversion;
- privacy-safe scenario normalization;
- pass, request-search, and pressure-selection attempts;
- bounded outcome and signal-source categories;
- compact JSON construction and serialization; and
- nonthrowing emission with prefix `[Rank26Scheduler]`.

The recorder is internal and specific to Rank 26. Measurement failures are
swallowed without emitting exception text and cannot alter scheduler outcomes.
Durations are captured before JSON construction and logging. Allocation values
use `GC.GetAllocatedBytesForCurrentThread` and are explicitly attributable only
to the measured thread and enabled instrumentation context.

### Public compatibility and timer scopes

The existing public `TryFind()` and `LoadHighestPressureNow(...)` declarations
remain intact. Each delegates to an internal overload that accepts the current
optional pass attempt. Normal callers and extensions continue through the same
public surface.

`RequestHandlingLoop` creates at most one pass attempt per loop iteration and
passes it to the internal request-search route. Request-search and pressure
records are emitted after their existing control-flow result is known. Static
review must prove that every timer stops before record serialization/logging and
that the enabled wrapper never invokes a matcher more times than the original
expression.

### Signals and claims

The three maintained `CheckBackendsSignal.Set()` sites are classified as:

- `shutdown` for scheduler shutdown wake-up;
- `request` for new backend-request publication; and
- `release` for `T2IBackendAccess.Dispose`.

A small internal `SignalScheduler(string source)` helper records the latest
bounded source, monotonic timestamp, and sequence only while measurement is
enabled, then always calls the unchanged auto-reset event. The public event
field remains available. Signals made directly by an external extension or by
untraced backend-state code remain behaviorally valid but are classified as
`external_or_timeout` if the recorder cannot observe them.

At pass start, the recorder compares signal sequence values. No observed new
sequence is classified as `startup` on the first pass and `timeout_or_external`
later. Multiple coalesced maintained signals report their count and latest
source; they are not represented as separate scheduler passes.

The first successful existing backend claim in a pass records elapsed time from
the observed latest maintained signal. This is signal-to-first-claim latency,
not request end-to-end latency and not backend generation time. Passes without a
claim emit no synthetic latency.

## Record Schema

Every line is one compact JSON object after `[Rank26Scheduler]`. All durations
are integer microseconds and all counts are integers. Common keys are:

- `schema`, fixed at `1`;
- `record`, one of `pass`, `try_find`, or `pressure`;
- `pass_id`;
- `scenario`; and
- `outcome`, from a bounded internal catalog.

No record contains raw model names, backend IDs/types, request IDs, user/session
identity, filters, prompts, paths, or exception text.

### Pass record

A pass record contains:

- wake source and coalesced signal count;
- signal age at pass start;
- pending/backend/pressure counts at pass start;
- total pressure membership at pass start;
- requests visited, cancelled, claimed, failed, and still waiting;
- aggregate matcher calls/time and pressure-selection time;
- pass elapsed time and current-thread allocated bytes;
- signal-to-first-claim time when a maintained signal was observed; and
- `idle`, `progress`, `waiting`, `error`, or `shutdown` outcome.

The idle scenario is represented by timeout/startup passes with zero pending
requests. Measurement output frequency is bounded to one pass record per loop
iteration; the existing one-second wait bounds idle frequency.

### Request-search record

A `try_find` record contains:

- ordinal within the current pass;
- whether a model and request matcher are present;
- backend, possible, matcher-accepted, available, pressure, and pressure-member
  counts observed on that visit;
- extension-gate, backend snapshot/filter, request-matcher, availability/sort,
  loaded-model search, pressure-selection, and total elapsed time;
- extension gate and matcher call counts;
- current-thread allocated bytes; and
- a bounded outcome such as `gate_wait`, `scaling_wait`, `no_backend`,
  `no_match`, `claim_any`, `claim_loaded_model`, `pressure_wait`,
  `load_started`, or `failed`.

Early returns still emit exactly one request-search record when enabled.

### Pressure-selection record

A `pressure` record contains:

- possible, available, loader, pressure, and total pressure-member counts;
- snapshot/sort time;
- compatibility and perfect-compatibility matcher calls/time;
- post-selection filter time;
- total elapsed time and current-thread allocated bytes; and
- a bounded outcome such as `no_loader`, `no_pressure`, `no_compatible`,
  `already_loading`, `too_new`, `all_refused`, `already_loaded`, or
  `load_started`.

Compatibility call counts include only actual predicate invocations; null
filters do not count as matcher calls.

## Collection Matrix

Collection uses an isolated copy or external harness and synthetic identities;
it never points at the primary repository's `Data`, `Models`, or `Output`.
Build outputs, logs, evidence, and temporary workload files live under `/tmp`.

The matrix covers:

1. disabled control and idle enabled control;
2. one request with an already-loaded model;
3. increasing pending counts sharing one model;
4. increasing pending counts across distinct models;
5. increasing backend counts and mixed usage;
6. cheap accept/refuse matchers and a deliberately bounded expensive matcher;
7. no possible backend and autoscaling-attempt branches;
8. cancellation before a visit and while pending;
9. claim, release signal, and next-claim progression;
10. pressure selection with single/multiple loaders, refusals, and failure sets;
11. concurrent publication/signaling sanity; and
12. malformed/unsafe scenario normalization and recorder-failure isolation.

At least three measured repetitions follow warm-up for scaling cases. Records
are analyzed by scenario and outcome. Nearest-rank p50/p95 and maxima may be
reported, but no result is promoted beyond the exercised synthetic topology.

## Decision Gate

The result is one of:

- `GO`: repeated scheduler work is a material contributor to scheduler CPU,
  allocation, or claim latency in a representative exercised queue, and a
  separately designed bounded optimization is justified;
- `NO-GO`: the measured operations are not repeatedly material in exercised
  representative queues, so no optimization is authorized; or
- `INSUFFICIENT`: evidence integrity, scenario coverage, or runtime fidelity is
  inadequate for either conclusion.

The decision is qualitative and evidence-backed. A `GO` does not itself
authorize sharing availability or changing scheduler policy; it authorizes a
new design with interleaving and parity proofs.

## Static and Runtime Validation

Static validation must prove:

- the approved base and exact instrumentation file inventory;
- unchanged public declarations and maintained signal ordering;
- disabled original expressions for snapshot/filter/sort/matcher paths;
- no matcher double invocation;
- timer scope excludes record serialization/logging;
- bounded privacy-safe schema and no identity/path/error fields;
- no instrumentation outside the selected source/settings files;
- no production optimization; and
- complete instrumentation removal after the decision.

Under the maintainer's explicit override, the agent may build and execute the
isolated matrix. Runtime validation records environment, exact passed/failed/
unrun counts, evidence digest, record count, scenario coverage, and limitations.
Failures stop the decision and are diagnosed before proceeding.

## Removal and Final Projection

After evidence and decision are recorded, remove:

- both temporary performance settings;
- `SchedulerCostMeasurement.cs`;
- both internal measurement overloads;
- signal classification state/helper and all signal hooks;
- pass/request/pressure attempts, counters, timers, outcomes, and log prefix.

The final committed `src` tree must equal the approved base `src` tree exactly.
Only this design, its implementation plan, the architecture audit, and final
evidence/decision documentation may remain. An isolated post-removal scheduler
sanity must pass or be truthfully marked unrun. The protected primary dirty
state must remain unchanged before and after local integration.

## Rollback

Before collection, revert the temporary instrumentation commits in reverse
order. During collection, disable `SchedulerMeasurementEnabled` for immediate
rollback. After collection, the planned removal commit is the normal final
state regardless of `GO`, `NO-GO`, or `INSUFFICIENT`.
