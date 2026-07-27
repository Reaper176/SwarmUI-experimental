# User Simultaneous-Generation Ceiling Design

**Status:** Design approved; not implemented

**Date:** 2026-07-27

**Roadmap scope:** Backend F16, rank 15

## Summary

`T2IAPI.GenT2I_Internal` and `ImageBatchToolExtension.GenBatchRun_Internal` each maintain a request-local list of active generation tasks. Both loops currently remove completed tasks, wait only while `tasks.Count > max_degrees`, and then add another task. When the count is exactly equal to the effective limit, the next task is admitted, so either loop can hold `limit + 1` active orchestration tasks.

Rank 15 corrects both predicates so a task is admitted only while the request-local active count is strictly below the effective limit. The change preserves the existing effective-limit calculation, task order, cancellation behavior, keep-alives, batch indices, error reporting, backend capacity, and route contracts.

The configured role field is described as a user limit, but the two audited counters are request-local. Concurrent HTTP requests, overlapping WebSocket producers, Image Batch requests, or separate sessions for one account can therefore exceed the configured value in aggregate even after this correction. The approved Rank 15 boundary fixes only the confirmed off-by-one in the two maintained loops; true account-wide admission control is a separate scheduler design and is not claimed here.

## Goals

1. Prevent normal T2I from admitting a new local task when its active task count already equals the captured effective limit.
2. Apply the identical boundary correction to Image Batch.
3. Preserve the existing behavior before, during, and after admission.
4. Keep the production change limited to the two audited comparison predicates.
5. Make the correction independently reversible by restoring those predicates.
6. Record the request-local nature of the guarantee without overstating it as account-wide enforcement.

## Non-Goals

Rank 15 does not:

- create account-wide, session-wide, or process-wide admission coordination;
- coordinate concurrent HTTP requests, overlapping WebSocket producers, Image Batch requests, or multiple sessions belonging to one user;
- redesign queue fairness, request ordering, backend selection, or backend capacity;
- change `RoleData.MaxT2ISimultaneous` or `User.CalcMaxT2ISimultaneous`;
- change when the effective limit is captured during a request;
- change `Session.GenClaim`, its counters, disposal, or cancellation tokens;
- change `T2IEngine.CreateImageTask` or its waiting/live-generation transitions;
- change Grid Generator, which has a separate scheduler;
- introduce a shared admission helper or generalized task-runner abstraction;
- add a cancellation wake-up while an admission loop is awaiting an active task;
- change fault propagation, logging, keep-alives, indices, seeds, file handling, webhooks, or response payloads;
- add instrumentation, benchmarks, or a performance claim; or
- edit frontend code, settings, launchers, external extensions, upstream code, generated files, user data, or protected maintainer work.

## Existing Boundary and Failure

### Normal T2I

`T2IAPI.GenT2I_Internal`:

1. creates one request-owned `Session.GenClaim`;
2. parses the request and adjusts the claim to the effective image count;
3. captures `session.User.CalcMaxT2ISimultaneous` in `max_degrees`;
4. maintains a local `List<Task>`;
5. removes completed tasks and logs task faults;
6. waits while `tasks.Count > max_degrees`;
7. checks request cancellation;
8. adds the next `T2IEngine.CreateImageTask`; and
9. drains the list while preserving periodic keep-alives.

At a limit of one, the first task makes the count one. The current comparison is false because `1 > 1` is false, so a second task is added. The same arithmetic produces `limit + 1` at every positive limit.

The WebSocket route can start multiple `GenT2I_Internal` producers on one connection, and the direct HTTP route can be called concurrently. Each producer owns a separate list and separately captures the effective limit.

### Image Batch

`ImageBatchToolExtension.GenBatchRun_Internal` follows the same admission structure around its local `List<Task>`. It removes completed work, waits only while `tasks.Count > max_degrees`, checks cancellation, prepares the next input image, and adds a `T2IEngine.CreateImageTask`.

Its equal-to-limit transition therefore has the same off-by-one. Image loading, parameter cloning, resolution selection, output naming, metadata, webhooks, streaming output, and the final task drain are downstream of the predicate and are not part of the defect.

### Effective limit

`User.CalcMaxT2ISimultaneous` returns at least one and combines the user's calculated role with the existing backend-derived ceiling unless unrestricted mode is enabled. Both loops capture that result once before admission begins. Rank 15 does not alter either the calculation or the capture timing.

### Claim transitions

Each route creates one claim for its requested work. Individual `CreateImageTask` calls move the existing claim through waiting and live counters and complete their portion when they finish. The enclosing lexical ownership releases any remainder on exit.

The local `tasks.Count` admission predicate does not read or mutate those counters. Rank 15 changes no claim transition and does not use claim counts as a new admission authority.

## Chosen Architecture

### Direct predicate correction

In both maintained loops, replace:

```csharp
while (tasks.Count > max_degrees)
```

with:

```csharp
while (tasks.Count >= max_degrees)
```

Completed tasks remain removed immediately before the comparison and after every `Task.WhenAny`. Because the effective limit is always at least one, entering the new loop implies that the local task list contains at least one task, so the existing `Task.WhenAny(tasks)` remains valid.

No helper is introduced. The duplicated condition is intentionally retained because the correction is a single local comparison in two different route owners, and a new abstraction would broaden the change without adding a new invariant.

### Admission invariant

Immediately before either loop adds a new task:

```text
remaining local active task count < captured effective limit
```

Adding one task can therefore produce a count no greater than the limit. A task that completes between cleanup and addition may cause the list temporarily to overstate active work, but it cannot cause over-admission. The next cleanup removes it through the existing path.

The invariant applies independently to each invocation of `GenT2I_Internal` or `GenBatchRun_Internal`. It is not an aggregate invariant across invocations, sessions, routes, or a user's account.

## Data Flow and Ordering

For every requested item, the preserved flow is:

1. remove locally tracked completed tasks;
2. while the remaining count is equal to or above the captured limit:
   1. await any locally tracked task;
   2. remove completed tasks and retain existing fault logging;
3. check the existing claim cancellation state;
4. prepare the existing per-item parameters and indices;
5. add exactly one generation task;
6. retain the normal T2I ordering delay where it already applies; and
7. continue until all requested work is admitted or cancellation stops admission.

After admission ends, each route retains its existing drain and completion behavior. Normal T2I continues to send keep-alives during its final drain and to build outputs using existing batch indices. Image Batch continues to stream outputs and send its existing final webhook/status/result.

Examples after the correction:

- With limit 1, a second task waits until the first completes.
- With limit 2, a third task waits until either of the first two completes.
- With a larger limit `N`, task `N + 1` waits until the cleaned count is below `N`.
- If fewer items than the limit are requested, no new wait is introduced.

## Cancellation and Failure Handling

The predicate correction adds no new cancellation or error path.

If cancellation occurs while a loop is waiting in `Task.WhenAny`, the existing wait continues until one tracked task completes; the existing cancellation check then prevents the next admission. Rank 15 deliberately does not add a cancellation task or token-aware wait because that would change responsiveness and lifecycle behavior beyond the audited defect.

Faulted tasks remain completed tasks. The existing cleanup function logs their exception and removes them, after which later work may be admitted under the corrected count. Route-specific `setError` behavior, claim interruption, continuation-after-error behavior, producer-failure transport, and final responses remain unchanged.

Exceptions during per-item preparation or task creation retain their current propagation and enclosing cleanup. The comparison does not catch, translate, or suppress them.

## Compatibility

The production correction preserves:

- both public API routes and internal handler signatures;
- HTTP and WebSocket request and response formats;
- Image Batch extension route and streaming result contract;
- the role setting name, description, stacking, and admin API;
- `User.CalcMaxT2ISimultaneous` and its minimum/backend-derived behavior;
- one captured effective limit per handler invocation;
- local task-list ownership;
- request and batch ordering;
- normal T2I's ordering delay and keep-alives;
- cancellation tokens and check placement;
- `Session.GenClaim` identity, counters, and disposal;
- batch offsets, image indices, seed progression, and discard behavior;
- Image Batch file order, output naming, metadata, and webhooks;
- task-fault logging and existing readable/user-facing errors;
- backend selection, capacity, queueing, and generation execution; and
- all public source and binary extension interfaces.

## Alternatives Considered

### Shared admission helper

A helper could express whether a local task list has room. This would centralize the comparison, but it would add an API and indirection for one condition while cleanup and waiting remain route-specific. It does not improve the bounded invariant enough to justify the extra surface.

### Shared scheduling utility

The two loops could share task removal, waiting, cancellation, and admission logic. Their surrounding behavior differs substantially: normal T2I manages seeds, keep-alives, grids, and WebSocket continuation, while Image Batch manages input files, resolution modes, and streamed file outputs. Consolidating them would expand Rank 15 into unrelated refactoring and increase regression risk.

### Account-wide admission coordinator

A per-user semaphore or lease coordinator could enforce the role value across concurrent routes and sessions. That would better match the setting's user-oriented description, but it would require new ownership for acquisition, cancellation, dynamic role/backend limits, fairness, cleanup, and every generation entry point. The user explicitly selected the bounded audit correction instead. Account-wide enforcement remains a separately designed future project.

## Production Scope

Expected production modifications:

- `src/WebAPI/T2IAPI.cs`
  - change only the `GenT2I_Internal` local admission comparison.
- `src/BuiltinExtensions/ImageBatchTool/ImageBatchToolExtension.cs`
  - change only the `GenBatchRun_Internal` local admission comparison.

The production diff is expected to contain exactly two one-character predicate additions and no other source edits.

Expected unchanged source includes:

- `src/Accounts/Role.cs`;
- `src/Accounts/User.cs`;
- `src/Accounts/Session.cs`;
- `src/Text2Image/T2IEngine.cs`;
- `src/WebAPI/API.cs`;
- Grid Generator;
- backend scheduling and capacity code; and
- frontend and route documentation.

## Static Verification

Agents must not build, launch, or test SwarmUI. Static verification must:

1. confirm the production diff changes only the two approved predicates;
2. confirm each predicate waits on equality as well as greater-than;
3. simulate limits 1, 2, and a larger value across cleanup, equality, wait, completion, and add transitions;
4. prove `CalcMaxT2ISimultaneous` remains at least one, keeping `Task.WhenAny(tasks)` valid whenever the loop is entered;
5. enumerate all maintained `CalcMaxT2ISimultaneous` consumers;
6. confirm Grid Generator remains unchanged and separately scheduled;
7. confirm completed-task cleanup and fault logging are unchanged in both routes;
8. confirm cancellation-check placement is unchanged;
9. compare the normal T2I ordering delay, final drain, keep-alives, indices, outputs, and errors against the design base;
10. compare Image Batch input order, preparation, final drain, outputs, webhooks, status, and errors against the design base;
11. confirm `Session.GenClaim`, `T2IEngine.CreateImageTask`, role calculation, backend selection, and public signatures are unchanged;
12. inspect the exact fixed-range production diff and commit scope; and
13. run `git diff --check` without building or testing.

Static evidence can establish the local arithmetic boundary and unchanged source flow. It cannot establish runtime scheduling, backend concurrency, cancellation timing, external extension behavior, platform behavior, filesystem outcomes, or an account-wide ceiling.

## Maintainer Validation Matrix

The maintainer performs all builds and live validation. Record the operating system, filesystem, and exact outcomes.

1. Normal T2I, limit 1, one requested item: one task completes normally.
2. Normal T2I, limit 1, multiple requested items: no second local task becomes active before the first completes.
3. Normal T2I, limit 2, two requested items: both may be active when backend capacity permits.
4. Normal T2I, limit 2, more than two requested items: no third local task becomes active before one completes.
5. Normal T2I, a larger limit, fewer/equal/more requested items: the observed local boundary matches the configured effective limit.
6. Normal T2I cancellation while admission is waiting: no later item is admitted after the existing cancellation check, and the route completes with its current cancellation behavior.
7. Normal T2I task fault while later work is waiting: the fault is reported through existing paths, completed work is removed, and continuation behavior matches the request setting.
8. Normal T2I successful multi-item output: batch indices, seed progression, keep-alives, discards, grid behavior, webhooks, and final response remain correct.
9. Image Batch, limit 1, one input file: one task completes normally.
10. Image Batch, limit 1, multiple input files: no second local task becomes active before the first completes.
11. Image Batch, limit 2, two input files: both may be active when backend capacity permits.
12. Image Batch, limit 2, more than two input files: no third local task becomes active before one completes.
13. Image Batch, a larger limit, fewer/equal/more input files: the observed local boundary matches the configured effective limit.
14. Image Batch cancellation while admission is waiting: no later file is admitted after the existing cancellation check, and current route completion behavior remains.
15. Image Batch generation fault while later work is waiting: existing logging/error output remains and the route does not deadlock.
16. Image Batch successful multi-file output: input order, resolution behavior, batch indices, filenames, metadata, streamed images, webhooks, status, and final success remain correct.
17. Two separate users generate simultaneously: each request applies its own limit and one user's local admission does not block the other.
18. One user runs normal T2I and Image Batch concurrently: each invocation respects its own local limit; the combined count may exceed the role value and is recorded as the approved account-wide caveat, not a failure of Rank 15.
19. One WebSocket submits overlapping normal T2I producers: each producer respects its own local limit; their aggregate may exceed it and is recorded as the same caveat.
20. Normal T2I and Image Batch with backend capacity below the role limit: backend capacity remains the independent lower constraint and both routes complete normally.

## Success Criteria

Rank 15 is successful when:

- neither audited handler invocation admits a new task while its cleaned local active count equals or exceeds its captured effective limit;
- local active task count therefore never exceeds that limit through either admission loop;
- both predicates use identical boundary semantics;
- all named existing route behavior remains preserved;
- production changes are confined to the two comparisons; and
- documentation and validation results do not claim account-wide enforcement, performance improvement, or unperformed platform/runtime coverage.

## Rollback

Rollback restores `>` in the two admission predicates together. No migration, persisted-data change, configuration conversion, or public API rollback is required.
