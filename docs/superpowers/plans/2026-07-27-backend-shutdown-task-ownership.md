# Backend Shutdown Task Ownership Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make `BackendHandler.Shutdown()` publish and monitor one stable task per shutdown-entry backend, await each real backend shutdown lifetime, and isolate backend-specific failures without changing downstream shutdown behavior.

**Architecture:** Capture one `AllBackends.Values` entry snapshot, synchronously build one handler-owned task per backend, and keep the existing usage grace plus awaited `DoShutdownNow()` inside that task. Monitor a controller-owned pending view derived from the stable task collection; each task logs and contains its own failure so webhook, persistence, and later process cleanup continue.

**Tech Stack:** C# 12, .NET 8, `Task`, `ConcurrentDictionary`, SwarmUI backend lifecycle, FreneticUtilities readable exception formatting.

---

## Repository Constraints

- Work directly on `master`; do not create or use a worktree.
- Reaper176 is an approved maintainer under `AGENTS.md`.
- Agents must not build, launch, or test SwarmUI. The maintainer performs all runtime validation.
- Agents may run source searches, numbered inspection, exact-range diff inspection, `git diff --check`, and permitted static linters.
- Never edit generated `docs/APIRoutes`.
- Never edit or stage user data, generated/build state, extensions, upstream code, or unrelated working-tree changes.
- Preserve the existing uncommitted maintainer files:
  - `src/Data/Settings.fds`;
  - `src/Pages/Text2Image.cshtml`;
  - `src/wwwroot/js/genpage/gentab/loras.js`;
  - `src/wwwroot/js/genpage/main.js`; and
  - `Data.pre-restore-2026-07-19/`.
- Commit `f17e0955` is the approved pre-production design boundary.
- Preserve this public signature exactly:

```csharp
public void Shutdown()
```

- Preserve the existing handler `HasShutdown` field/check/set, both wake-up signals, usage predicate, `MaxUsages > 0` condition, 100-millisecond polling, counter threshold, forced-shutdown message, progress cadence, done-generating webhook placement, final persistence body, and `Program.Shutdown()` ordering.
- The approved entry boundary is one captured `AllBackends.Values` snapshot. Do not add a late-backend registration barrier.
- One task per backend means one `BackendHandler.Shutdown()`-owned task per snapshot entry. Do not redesign autoscaling parent/child internals or claim exactly-once invocation across all internal owners.
- Backend-task failures must log backend ID, type name, and readable exception detail, then remain isolated from every other backend and downstream shutdown owner.
- Do not add a timeout, cancellation token, retry, rollback, sequential shutdown, new field, lock, aggregate completion task, or public member.

## File Map

- Modify `src/Backends/BackendHandler.cs`
  - change only `BackendHandler.Shutdown()` task construction and monitoring;
  - capture the backend entry snapshot;
  - publish one stable task per snapshot entry;
  - await each real backend shutdown inside its owner task;
  - contain and log backend-specific failures; and
  - monitor a controller-owned pending view.
- Modify after static source review `docs/superpowers/specs/2026-07-27-backend-shutdown-task-ownership-design.md`
  - record the exact implementation and static review evidence.
- Modify after static source review `docs/superpowers/audits/2026-07-21-maintainability-architecture-refresh.md`
  - mark Backend F18/rank 14 implemented awaiting maintainer validation.

Expected unchanged maintained source:

- `src/Core/Program.cs`;
- `src/Backends/AbstractBackend.cs`;
- every concrete backend implementation;
- `BackendHandler.ShutdownBackendCleanly`;
- `BackendHandler.DeleteById`;
- backend persistence helpers; and
- webhook infrastructure.

## Task 1: Implement Stable Backend Shutdown Tasks

**Files:**

- Modify: `src/Backends/BackendHandler.cs:977-1037`

- [ ] **Step 1: Reconfirm the complete handler boundary and maintained caller inventory**

Run:

```bash
rg -n -C 12 "private volatile bool HasShutdown|public void Shutdown\\(\\)|List<\\(BackendData, Task\\)> tasks|tasks\\.Add|DoShutdownNow\\(\\)|TryMarkDoneGenerating|TrySavePending" \
  src/Backends/BackendHandler.cs

rg -n "Backends\\?\\.Shutdown\\(\\)|BackendHandler\\.Shutdown\\(" \
  src \
  --glob '*.cs' \
  --glob '!src/bin/**' \
  --glob '!src/obj/**' \
  --glob '!src/Extensions/**'
```

Expected maintained composition-root caller:

```csharp
Backends?.Shutdown();
```

in `Program.Shutdown()`.

Confirm the current handler flow:

- checks and assigns `HasShutdown`;
- signals `NewBackendInitSignal` and `CheckBackendsSignal`;
- publishes wrapper tasks;
- lets workers append real shutdown tasks to the shared list;
- monitors and replaces that same list;
- waits for `TryMarkDoneGenerating`; and
- runs the generation-aware final-save body.

- [ ] **Step 2: Capture and compare the approved pre-change method**

Run:

```bash
git show f17e0955:src/Backends/BackendHandler.cs | sed -n '977,1038p'
nl -ba src/Backends/BackendHandler.cs | sed -n '977,1038p'
```

The committed source must match the approved design boundary for:

- field and public method signature;
- guard and wake-up signals;
- current shared-list ownership failure;
- grace predicate, counter, sleep, and message;
- progress loop and message;
- done-generating webhook; and
- complete final persistence body.

If committed source changed after `f17e0955`, stop and return to design review rather than applying the literal replacement blindly.

- [ ] **Step 3: Replace only task construction and monitoring**

Keep the guard and wake-up signals unchanged. Replace the block beginning with:

```csharp
        List<(BackendData, Task)> tasks = [];
```

and ending immediately before:

```csharp
        WebhookManager.TryMarkDoneGenerating().Wait();
```

with:

```csharp
        BackendData[] shutdownBackends = [.. AllBackends.Values];
        List<(BackendData Backend, Task Task)> shutdownTasks = [];
        foreach (BackendData backend in shutdownBackends)
        {
            Task shutdownTask = Task.Run(async () =>
            {
                try
                {
                    int backTicks = 0;
                    while (backend.CheckIsInUse && backend.AbstractBackend.MaxUsages > 0)
                    {
                        if (backTicks++ > 50)
                        {
                            Logs.Info($"Backend {backend.ID} ({backend.AbstractBackend.HandlerTypeData.Name}) has been locked in use for at least 5 seconds after shutdown, giving up and killing anyway.");
                            break;
                        }
                        Thread.Sleep(100);
                    }
                    await backend.AbstractBackend.DoShutdownNow();
                }
                catch (Exception ex)
                {
                    Logs.Error($"Backend {backend.ID} ({backend.AbstractBackend.HandlerTypeData.Name}) failed to shut down cleanly: {ex.ReadableString()}");
                }
            });
            shutdownTasks.Add((backend, shutdownTask));
        }
        List<(BackendData Backend, Task Task)> pendingTasks = shutdownTasks;
        int ticks = 0;
        while (pendingTasks.Any())
        {
            if (ticks++ > 20)
            {
                ticks = 0;
                Logs.Info($"Still waiting for {pendingTasks.Count} backends to shut down ({string.Join(", ", pendingTasks.Select(p => p.Backend).Select(b => $"{b.ID}: {b.AbstractBackend.HandlerTypeData.Name}"))})...");
            }
            Task.Delay(TimeSpan.FromMilliseconds(100)).Wait();
            pendingTasks = [.. pendingTasks.Where(t => !t.Task.IsCompleted)];
        }
```

Do not:

- change code before the entry snapshot;
- change code at or after `TryMarkDoneGenerating`;
- add worker access to `shutdownTasks` or `pendingTasks`;
- replace `Thread.Sleep(100)` with cancellation-aware delay;
- replace the monitor with `Task.WaitAll` or `Task.WhenAll`;
- rethrow the caught exception; or
- edit another method or file.

- [ ] **Step 4: Trace publication and completion ownership**

Trace the source in execution order and confirm:

1. `shutdownBackends` captures one entry enumeration;
2. the controller creates one `shutdownTask` for each snapshot entry;
3. only the controller appends to `shutdownTasks`;
4. the complete `foreach` finishes before `pendingTasks` is assigned and monitoring begins;
5. each worker owns grace polling and directly awaits `DoShutdownNow()`;
6. a worker may start or complete during publication but cannot change collection membership;
7. `pendingTasks` is reassigned only by the controller;
8. a slow real backend remains incomplete and visible until `DoShutdownNow()` returns; and
9. the handler reaches the webhook only after all handler-owned tasks complete.

- [ ] **Step 5: Trace backend-failure behavior**

Trace these cases against the source:

1. a grace-polling exception is caught, logged with backend identity and readable detail, and contained;
2. a synchronous throw while invoking `DoShutdownNow()` is caught and contained;
3. an asynchronous `DoShutdownNow()` fault is observed by the direct `await`, caught, logged, and contained;
4. one fault does not cancel or block other backend tasks;
5. multiple faults each execute their own catch;
6. the caught task completes and leaves the pending view;
7. the done-generating webhook still runs; and
8. final persistence and later `Program.Shutdown()` owners remain reachable.

Do not add a shared exception collection, aggregate throw, retry, or fallback shutdown call.

- [ ] **Step 6: Prove the compatibility boundary**

Run:

```bash
git diff -- src/Backends/BackendHandler.cs

git diff f17e0955 -- \
  src/Core/Program.cs \
  src/Backends/AbstractBackend.cs \
  src/Backends/AutoScalingBackend.cs \
  src/Backends/SwarmSwarmBackend.cs \
  src/BuiltinExtensions/ComfyUIBackend/ComfyUISelfStartBackend.cs

git diff -- src/Backends/BackendHandler.cs | rg -n '^[+-].*(public|protected)\\b' || true

rg -n "public void Shutdown\\(\\)|private volatile bool HasShutdown|NewBackendInitSignal\\.Set|CheckBackendsSignal\\.Set|Thread\\.Sleep\\(100\\)|backTicks\\+\\+ > 50|TryMarkDoneGenerating|TrySavePending" \
  src/Backends/BackendHandler.cs
```

Expected:

- the diff is confined to `BackendHandler.Shutdown()` task construction and monitoring;
- expected unchanged source files have no diff from the design boundary;
- no public or protected declaration changes;
- handler guard and wake-up signals remain;
- usage polling and threshold remain; and
- webhook and final-save calls remain in their prior order.

- [ ] **Step 7: Run permitted static checks**

Run:

```bash
rg -n "shutdownBackends|shutdownTasks|pendingTasks|Task\\.Run|DoShutdownNow\\(\\)|failed to shut down cleanly" \
  src/Backends/BackendHandler.cs

git diff --check -- src/Backends/BackendHandler.cs
git diff --name-only
git status --short --branch
```

Expected:

- one entry snapshot;
- one controller-side `shutdownTasks.Add`;
- one direct awaited `DoShutdownNow()` in the owner task;
- no worker-side collection mutation;
- one backend-specific readable error log;
- clean whitespace;
- only `src/Backends/BackendHandler.cs` added to the protected pre-existing worktree state; and
- no staged file.

Do not build, test, launch, invoke shutdown, or call a live API.

- [ ] **Step 8: Commit the focused production change**

Run:

```bash
git add src/Backends/BackendHandler.cs
git diff --cached --name-only
git diff --cached --check
git commit -m "fix: stabilize backend shutdown tasks"
```

Expected staged scope: exactly `src/Backends/BackendHandler.cs`.

## Task 2: Perform Integrated Static and Independent Source Review

**Files:**

- Review: `src/Backends/BackendHandler.cs`
- Review: `src/Core/Program.cs`
- Review: `src/Backends/AbstractBackend.cs`
- Review: `src/Backends/AutoScalingBackend.cs`
- Review: `src/Backends/SwarmSwarmBackend.cs`
- Review: `src/BuiltinExtensions/ComfyUIBackend/ComfyUISelfStartBackend.cs`
- Review: `docs/superpowers/specs/2026-07-27-backend-shutdown-task-ownership-design.md`

- [ ] **Step 1: Pin the design and production boundaries**

Run:

```bash
rank14_design_base="$(git rev-parse f17e0955)"
rank14_source_head="$(git log -1 --format=%H -- src/Backends/BackendHandler.cs)"

printf 'design_base=%s\nsource_head=%s\n' "$rank14_design_base" "$rank14_source_head"
git log --oneline "$rank14_design_base..$rank14_source_head" -- src/Backends/BackendHandler.cs
git diff --name-only "$rank14_design_base..$rank14_source_head" -- src/Backends/BackendHandler.cs
git diff --stat "$rank14_design_base..$rank14_source_head" -- src/Backends/BackendHandler.cs
git diff --check "$rank14_design_base..$rank14_source_head" -- src/Backends/BackendHandler.cs
```

Expected production projection: only `src/Backends/BackendHandler.cs`, with one focused source commit unless review corrections were required.

- [ ] **Step 2: Run the design-conformance inventory**

Run:

```bash
rg -n -C 10 "private volatile bool HasShutdown|public void Shutdown\\(\\)|shutdownBackends|shutdownTasks|pendingTasks|Thread\\.Sleep\\(100\\)|backTicks\\+\\+ > 50|await backend\\.AbstractBackend\\.DoShutdownNow|failed to shut down cleanly|TryMarkDoneGenerating|TrySavePending" \
  src/Backends/BackendHandler.cs

rg -n "Backends\\?\\.Shutdown\\(\\)|BackendHandler\\.Shutdown\\(" \
  src \
  --glob '*.cs' \
  --glob '!src/bin/**' \
  --glob '!src/obj/**' \
  --glob '!src/Extensions/**'
```

Compare every result with the approved design's entry snapshot, owner task, monitor, failure, caller, and downstream boundaries.

- [ ] **Step 3: Compare the complete handler method with the design boundary**

Run:

```bash
git diff f17e0955..HEAD -- src/Backends/BackendHandler.cs
git show f17e0955:src/Backends/BackendHandler.cs | sed -n '977,1038p'
sed -n '977,1048p' src/Backends/BackendHandler.cs
```

Confirm:

- guard and wake-up signals are unchanged;
- only task construction and monitoring changed;
- usage predicate, counter, sleep, and forced message are unchanged;
- one stable task directly awaits the real backend shutdown;
- backend faults are identified, readable, and contained;
- progress cadence and message information are preserved; and
- everything from `TryMarkDoneGenerating()` through method return is textually unchanged.

- [ ] **Step 4: Check public ABI and protected scope**

Run:

```bash
git diff f17e0955..HEAD -- src/Backends/BackendHandler.cs | rg -n '^[+-].*(public|protected)\\b' || true

git diff --name-only f17e0955..HEAD -- \
  src/Core/Program.cs \
  src/Backends/AbstractBackend.cs \
  src/Backends/AutoScalingBackend.cs \
  src/Backends/SwarmSwarmBackend.cs \
  src/BuiltinExtensions/ComfyUIBackend \
  src/wwwroot \
  src/Pages \
  docs/APIRoutes \
  src/Extensions
```

Expected: both searches are silent.

- [ ] **Step 5: Dispatch a fresh specification reviewer**

Require the reviewer to verify:

- one entry snapshot of `AllBackends.Values`;
- one handler-owned task per snapshot entry;
- controller-only task publication before monitoring;
- no worker mutation of the stable task set or pending view;
- direct awaiting of the complete `DoShutdownNow()` lifetime;
- preserved parallelism, usage predicate, cadence, threshold, message, and progress diagnostics;
- backend identity plus readable error detail;
- fault isolation without cancellation, retry, rethrow, or downstream skip;
- unchanged webhook and final persistence body;
- unchanged handler guard, signals, callers, public API/ABI, and backend implementations;
- explicit late-add and autoscaling parent/child caveats;
- exact one-method/one-file production scope; and
- no agent runtime or performance claim.

Correct specification findings in a focused source commit, then repeat until approved.

- [ ] **Step 6: Dispatch a fresh code-quality reviewer**

Require the reviewer to inspect:

- clarity and minimality of the snapshot/task/pending ownership;
- named tuple readability and explicit C# types;
- no shared collection access from worker tasks;
- no race hidden by task scheduling during publication;
- correct async unwrapping from `Task.Run(async ...)`;
- direct fault observation through `await`;
- useful non-sensitive failure diagnostic;
- no unnecessary helper, state, lock, aggregate task, timeout, or cancellation;
- no synchronous deadlock introduced beyond the preserved monitor contract;
- unchanged downstream body and source scope;
- repository C# syntax conventions; and
- clean fixed-range whitespace.

Correct quality findings in a focused source commit, repeat specification review if behavior changes, and repeat quality review until approved.

## Task 3: Record Static Implementation Closure

**Files:**

- Modify: `docs/superpowers/specs/2026-07-27-backend-shutdown-task-ownership-design.md`
- Modify: `docs/superpowers/audits/2026-07-21-maintainability-architecture-refresh.md`

- [ ] **Step 1: Add the implementation record to the design**

Record:

- exact approved design base and final source head;
- integrated history versus source-path-filtered history;
- exact production commit list, source file, and numstat;
- final entry snapshot, stable task, direct-await, pending-view, and fault-isolation behavior;
- unchanged guard, signals, grace behavior, progress logs, webhook, persistence, callers, backend implementations, and ABI;
- late-add, non-completing-backend, and autoscaling parent/child caveats;
- specification and quality review outcomes;
- exact static commands and results;
- no agent build/test/runtime/platform/performance claim;
- unvalidated platforms and runtime interleavings; and
- status `Implemented; awaiting maintainer validation`.

Do not alter or shorten the exact 14-case validation matrix.

- [ ] **Step 2: Update every current Backend F18/rank 14 audit status**

In the audit:

- mark Backend F18 and roadmap rank 14 implemented, awaiting maintainer validation;
- retain original baseline evidence as historical evidence;
- record the exact one-method/one-file source boundary and review outcomes;
- retain rank 14 as Recommended Next Project until live validation passes;
- do not advance, design, or implement rank 15;
- leave ranks 1, 2, and 8 awaiting their own validation;
- retain every validated rank, especially ranks 12 and 13 on Garuda Linux/Btrfs;
- preserve all 12 top-level audit sections and 32 ranked roadmap entries; and
- preserve the static/runtime and platform/performance evidence boundaries.

- [ ] **Step 3: Self-review documentation**

Run:

```bash
rg -n "TBD|TODO|placeholder|Backend F18|Rank 14|rank 14|Rank 15|rank 15|Recommended Next|awaiting maintainer|14-case" \
  docs/superpowers/specs/2026-07-27-backend-shutdown-task-ownership-design.md \
  docs/superpowers/audits/2026-07-21-maintainability-architecture-refresh.md

git diff --check -- \
  docs/superpowers/specs/2026-07-27-backend-shutdown-task-ownership-design.md \
  docs/superpowers/audits/2026-07-21-maintainability-architecture-refresh.md

printf 'top_level_sections='
rg -c '^## ' docs/superpowers/audits/2026-07-21-maintainability-architecture-refresh.md

printf 'roadmap_entries='
awk '/^## Ranked Refactoring Roadmap$/{inside=1; next} inside && /^## /{exit} inside && /^### [0-9]+\\./{count++} END{print count+0}' \
  docs/superpowers/audits/2026-07-21-maintainability-architecture-refresh.md

printf 'matrix_cases='
sed -n '/^## Maintainer Validation Matrix$/,/^## Success Criteria$/p' \
  docs/superpowers/specs/2026-07-27-backend-shutdown-task-ownership-design.md \
  | rg -c '^[0-9]+\\.'
```

Expected:

- no placeholder or stale unimplemented Rank 14 wording outside explicitly historical evidence;
- Rank 14 remains implemented awaiting validation and recommended;
- Rank 15 remains neither designed nor implemented;
- 12 audit sections;
- 32 ranked roadmap entries;
- 14 unchanged matrix cases; and
- clean whitespace.

- [ ] **Step 4: Commit documentation-only closure**

Run:

```bash
git add \
  docs/superpowers/specs/2026-07-27-backend-shutdown-task-ownership-design.md \
  docs/superpowers/audits/2026-07-21-maintainability-architecture-refresh.md
git diff --cached --name-only
git diff --cached --check
git commit -m "docs: record backend shutdown task ownership"
```

Expected staged scope: exactly the two documentation files.

- [ ] **Step 5: Dispatch fresh documentation specification and quality reviews**

Require both reviewers to verify:

- exact design/source boundary and commit facts;
- accurate snapshot, task ownership, direct-await, monitoring, and fault-isolation semantics;
- current-versus-historical wording;
- complete compatibility and caveat boundary;
- accurate static/runtime distinction;
- rank 14 remains awaiting validation and recommended;
- rank 15 is not advanced;
- ranks 1, 2, and 8 remain awaiting validation;
- ranks 12 and 13 validation records remain intact;
- audit structure, roadmap count, and exact matrix;
- documentation-only commit scope; and
- clean whitespace.

Correct documentation findings in focused commits and repeat both reviews.

## Task 4: Maintainer Validation and Final Record

**Files:**

- Modify after explicit maintainer result: `docs/superpowers/specs/2026-07-27-backend-shutdown-task-ownership-design.md`
- Modify after explicit maintainer result: `docs/superpowers/audits/2026-07-21-maintainability-architecture-refresh.md`

- [ ] **Step 1: Hand the maintainer the exact 14-case matrix**

Use the approved design's “Maintainer Validation Matrix” without shortening it:

1. No configured backends completes handler shutdown, done-generating webhook handling, and clean final-save handling without error.
2. One idle or immediately completing backend receives one handler-owned shutdown task and completes before handler return.
3. One active backend that becomes available during the grace period shuts down after availability without the forced-after-grace message.
4. One backend that remains active beyond the existing grace threshold emits the existing forced-shutdown message and then enters `DoShutdownNow()`.
5. Mixed idle, active, slow, and immediately completing backends all start in parallel and are all observed through completion.
6. An asynchronously slow `DoShutdownNow()` remains in pending diagnostics and prevents early handler return until it actually completes.
7. One faulting backend emits one backend-specific readable diagnostic while every other backend, webhook, final persistence, and later process-shutdown owner continues.
8. Multiple faulting backends each emit their own diagnostic without suppressing one another or later shutdown work.
9. Real and non-real/autoscaled backends retain their existing backend-specific behavior while every entry in the handler snapshot has one observed handler-owned task.
10. Periodic pending-backend logs list only tasks that have not completed and retain backend IDs, type names, and cadence.
11. Clean, pending-save, newer-change-pending, and failed final-save outcomes retain their existing logs, acknowledgment rules, and continuation behavior after backend tasks complete.
12. Repeated sequential or direct handler shutdown calls retain the existing guard behavior and do not publish a second task set.
13. Web, admin, restart, control-timeout, unload, and process-exit overlap remains owned by Rank 13's outer gate while backend task monitoring runs once.
14. Repeated stress runs with mixed task timing produce no collection-mutation exception, missed handler-owned task, or return before every handler-owned task completes.

Record the operating system/filesystem and maintainer's exact result. Do not infer a pass.

- [ ] **Step 2: If validation fails, diagnose before editing**

Use `superpowers:systematic-debugging`. Reproduce only through maintainer-supplied evidence, trace the snapshot/task/pending owner and responsible backend/downstream boundary, update the design if behavior changes, implement the smallest correction, repeat source reviews, and return affected matrix cases to the maintainer.

- [ ] **Step 3: If validation passes, update current status**

Only after explicit confirmation:

- mark Backend F18/rank 14 implemented and maintainer-validated on the confirmed platform;
- record maintainer name, date, exact matrix scope, and outcomes;
- retain late-add, non-completing-backend, and autoscaling parent/child caveats;
- retain unvalidated-platform and no-performance caveats;
- keep agent evidence static-only;
- advance the audit's Recommended Next Project pointer to rank 15 without implementing or designing it; and
- leave ranks 1, 2, and 8 unchanged.

- [ ] **Step 4: Commit validation documentation only**

Run:

```bash
git add \
  docs/superpowers/specs/2026-07-27-backend-shutdown-task-ownership-design.md \
  docs/superpowers/audits/2026-07-21-maintainability-architecture-refresh.md
git diff --cached --name-only
git diff --cached --check
git commit -m "docs: validate backend shutdown task ownership"
```

- [ ] **Step 5: Run final fresh reviews and static verification**

Dispatch fresh validation-record specification and quality reviewers. Then run:

```bash
rank14_design_base="$(git rev-parse f17e0955)"
rank14_integrated_head="$(git rev-parse HEAD)"
rank14_source_head="$(git log -1 --format=%H -- src/Backends/BackendHandler.cs)"
rank14_validation_commit="$(git log -1 --format=%H -- docs/superpowers/specs/2026-07-27-backend-shutdown-task-ownership-design.md)"

git log --oneline "$rank14_design_base..$rank14_integrated_head"
git diff --check "$rank14_design_base..$rank14_integrated_head"

git log --oneline "$rank14_design_base..$rank14_source_head" -- src/Backends/BackendHandler.cs
git diff --name-only "$rank14_design_base..$rank14_source_head" -- src/Backends/BackendHandler.cs
git diff --stat "$rank14_design_base..$rank14_source_head" -- src/Backends/BackendHandler.cs
git diff --check "$rank14_design_base..$rank14_source_head" -- src/Backends/BackendHandler.cs

git show --check --oneline "$rank14_validation_commit"
git diff --cached --name-only
git status --short --branch
```

Completion requires:

- fresh specification and quality reviews pass;
- integrated history and source-only projection remain distinct;
- production contains only the reviewed `BackendHandler.Shutdown()` change;
- validation documentation contains only the two approved docs;
- rank 15 is recommended but not implemented or designed;
- ranks 1, 2, and 8 remain pending;
- ranks 12 and 13 validation records remain intact;
- index is empty;
- unrelated working-tree changes remain untouched; and
- no agent build/test/runtime/performance claim appears.
