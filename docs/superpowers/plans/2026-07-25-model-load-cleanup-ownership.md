# Model-Load Cleanup Ownership Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development to implement this plan task-by-task, with a fresh implementer and specification/quality review gates for each implementation or documentation task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make model-load scheduling publication return to baseline exactly once when cancellation prevents the scheduled delegate from starting or publication/scheduling setup fails, without changing established scheduling, request-pressure, failure-delivery, or successful-load behavior.

**Architecture:** Add one method-local atomic lifecycle owner inside `BackendHandler.LoadHighestPressureNow`. The pre-start path owns only selected-pressure `IsLoading` and captured model-load claims; once the delegate atomically enters the started state, its cleanup retains the existing reservation, backend classification, selected-pressure cleanup, and loaded-model refresh order.

**Tech Stack:** C# 12, .NET 8 task scheduling and `Interlocked`, existing `BackendHandler` pressure coordination, existing `Session.GenClaim` accounting.

---

## Execution Constraints and Fixed Boundary

- The user is approved maintainer **Reaper176**.
- Work directly on `master`; do not create or use a worktree.
- The reviewed design boundary is commit `c3a7f45dc4a55e045949836646b7b42dfd042d6c` (`docs: clarify model-load failure delivery`). Treat that design as fixed; if implementation requires a materially different lifecycle or ownership policy, stop and return to design review.
- The production patch is limited to `src/Backends/BackendHandler.cs`.
- Do not read, edit, stage, commit, restore, or otherwise disturb these user-owned paths:
  - `src/Data/Settings.fds`
  - `src/Pages/Text2Image.cshtml`
  - `src/wwwroot/js/genpage/gentab/loras.js`
  - `src/wwwroot/js/genpage/main.js`
  - `Data.pre-restore-2026-07-19/`
- Follow `AGENTS.md`: agents must not build, launch, run automated tests, start the server, load a model, or perform runtime validation. Only the maintainer performs those actions.
- Static checks are limited to source inspection, scoped Git inspection, `rg`, and `git diff --check`. Do not invoke `dotnet`, launch scripts, test runners, or runtime fault injection.
- Do not promise recovery for a `Session.GenClaim` constructor that mutates counters and then throws before returning. This owner can retract only `IsLoading` and claims that successfully returned and were captured.

## File and Line Map at `c3a7f45d`

| File | Lines | Role | Planned action |
|---|---:|---|---|
| `src/Backends/BackendHandler.cs` | 1083-1138 | `T2IBackendRequest.ReleasePressure` and request-owned pressure/generic-refusal behavior | Read-only compatibility reference |
| `src/Backends/BackendHandler.cs` | 1229-1234 | Scheduling caller passes its callback/token; caller-local `NotifyWillLoad` | Read-only compatibility reference |
| `src/Backends/BackendHandler.cs` | 1261-1305 | Request construction, token substitution, failure delivery, and request completion | Read-only compatibility reference |
| `src/Backends/BackendHandler.cs` | 1313-1376 | `RequestHandlingLoop` catches a selected-pressure scheduling error into the scheduling caller's `Failure` | Read-only compatibility reference |
| `src/Backends/BackendHandler.cs` | 1445-1614 | `LoadHighestPressureNow`, including selection, publication, scheduling, load, and cleanup | Only production edit |
| `src/Accounts/Session.cs` | 88-150 | `Session.Claim`, `GenClaim` counter publication, registration, and non-idempotent disposal | Read-only compatibility reference |
| `docs/superpowers/specs/2026-07-25-model-load-cleanup-ownership-design.md` | 1-313 | Approved design, static proof obligations, validation matrix, and closure state | Update status/evidence only after implementation review and again after validation |
| `docs/superpowers/audits/2026-07-21-maintainability-architecture-refresh.md` | 470-477, 764-771, 900-907, 970 onward | Backend F17, Rank 11 recommendation/status, and ranked closure summary | Update status/evidence only after implementation review and again after validation |

No production helper type or new file is warranted. A method-local lifecycle keeps the correction inside its single ownership boundary and avoids a public-member or extension ABI change.

### Lifecycle State Model

Use explicit integer states, or a mechanically equivalent atomic representation:

```text
Published = 0
Started = 1
PreStartCleaned = 2
StartedCleaned = 3

delegate entry:         CAS Published -> Started
pre-start cleanup:      CAS Published -> PreStartCleaned
started cleanup:        CAS Started -> StartedCleaned
```

Consequences:

- If pre-start cleanup wins, later delegate dispatch observes a non-`Published` state and returns before reserving or doing backend work.
- If delegate entry wins, pre-start cleanup cannot retract live state; the delegate owns full cleanup.
- A synchronous publication/scheduling exception uses the same pre-start CAS, so it races safely with delegate entry.
- Repeated continuation/catch/cleanup calls lose their CAS and make no mutation.
- `ExecuteSynchronously` is only a scheduling preference. Correctness comes from the gate, not from assuming inline continuation execution.

## Task 1: Implement the Atomic Model-Load Lifecycle

**Files:**
- Modify: `src/Backends/BackendHandler.cs:1540-1606`
- Reference only: `src/Accounts/Session.cs:88-150`

- [ ] **Step 1: Reconfirm the protected working tree and fixed design boundary**

Run only:

```bash
git status --short
git rev-parse HEAD
git show -s --format='%H %s' c3a7f45d
```

Expected:

- HEAD is at or descends from the reviewed boundary.
- The known protected paths may remain dirty/untracked.
- No unrelated staged changes exist. If another agent has an in-scope change in `BackendHandler.cs`, stop rather than overwrite it.

- [ ] **Step 2: Inspect the exact ownership boundary without reading protected paths**

Run:

```bash
nl -ba src/Backends/BackendHandler.cs | sed -n '1083,1138p;1220,1238p;1260,1306p;1313,1377p;1445,1615p'
nl -ba src/Accounts/Session.cs | sed -n '88,151p'
```

Confirm before editing:

- `releasePressure` and `cancel` belong to the scheduling caller.
- `highestPressure` is independently selected from global `ModelRequests`.
- selected-pressure backend reasons, exclusions, sessions, and model remain selected-pressure state.
- `GenClaim.Dispose` is not generally idempotent, so only the winning cleanup owner may call it.

- [ ] **Step 3: Establish the gate and cleanup owners before publication**

Immediately after backend selection/logging and before `highestPressure.IsLoading = true`, create:

```csharp
const int lifecyclePublished = 0;
const int lifecycleStarted = 1;
const int lifecyclePreStartCleaned = 2;
const int lifecycleStartedCleaned = 3;
int lifecycle = lifecyclePublished;
bool isLoadingPublished = false;
List<Session.GenClaim> claims = [];

void disposeClaims()
{
    foreach (Session.GenClaim claim in claims)
    {
        claim.Dispose();
    }
}

void cleanupPreStart()
{
    if (Interlocked.CompareExchange(ref lifecycle, lifecyclePreStartCleaned, lifecyclePublished) != lifecyclePublished)
    {
        return;
    }
    if (isLoadingPublished)
    {
        highestPressure.IsLoading = false;
    }
    disposeClaims();
}

void cleanupStarted()
{
    if (Interlocked.CompareExchange(ref lifecycle, lifecycleStartedCleaned, lifecycleStarted) != lifecycleStarted)
    {
        return;
    }
    availableBackend.ReserveModelLoad = false;
    if (availableBackend.Backend.CurrentModelName != highestPressure.Model.Name)
    {
        Logs.Warning($"[BackendHandler] backend #{availableBackend.ID} failed to load model {highestPressure.Model.Name}");
        lock (highestPressure.Locker)
        {
            highestPressure.BadBackends.Add(availableBackend.ID);
            Logs.Debug($"Will deny backends: {highestPressure.BadBackends.JoinString(", ")}");
        }
    }
    highestPressure.IsLoading = false;
    disposeClaims();
    ReassignLoadedModelsList();
}
```

This is method-local state, so no XML-documented field is added. Preserve the current started cleanup order exactly: reservation release, mismatch classification, selected `IsLoading` reset, claim disposal, then loaded-model reassignment.

- [ ] **Step 4: Wrap publication, scheduling, and attachment in one setup try/catch**

Replace the current direct publication and fire-and-forget scheduling with this structure:

```csharp
try
{
    highestPressure.IsLoading = true;
    isLoadingPublished = true;
    foreach (Session sess in highestPressure.Sessions)
    {
        Session.GenClaim claim = sess.Claim(0, 1, 0, 0);
        claims.Add(claim);
    }
    Task loadTask = Task.Factory.StartNew(() =>
    {
        if (Interlocked.CompareExchange(ref lifecycle, lifecycleStarted, lifecyclePublished) != lifecyclePublished)
        {
            return;
        }
        try
        {
            availableBackend.ReserveModelLoad = true;
            int ticks = 0;
            while (availableBackend.CheckIsInUseNoModelReserve && availableBackend.Backend.MaxUsages > 0)
            {
                if (Program.GlobalProgramCancel.IsCancellationRequested)
                {
                    return;
                }
                if (ticks++ % 5 == 0)
                {
                    Logs.Debug($"[BackendHandler] model loader is waiting for backend #{availableBackend.ID} to be released from use ({availableBackend.Usages}/{availableBackend.Backend.MaxUsages})...");
                }
                Thread.Sleep(100);
            }
            Utilities.CleanRAM();
            if (highestPressure.Model.Name.ToLowerFast() == "(none)")
            {
                availableBackend.Backend.CurrentModelName = highestPressure.Model.Name;
            }
            else
            {
                availableBackend.Backend.LoadModel(highestPressure.Model, highestPressure.Requests.FirstOrDefault()?.UserInput).Wait(cancel);
            }
            Logs.Debug($"[BackendHandler] backend #{availableBackend.ID} loaded model, returning to pool");
        }
        catch (Exception ex)
        {
            while (ex is AggregateException ae && ae.InnerException is not null)
            {
                ex = ae.InnerException;
            }
            Logs.Error($"[BackendHandler] backend #{availableBackend.ID} failed to load model with error: {ex.ReadableString()}");
            lock (highestPressure.Locker)
            {
                highestPressure.BackendFailReasons.Add(ex.ReadableString());
            }
        }
        finally
        {
            cleanupStarted();
        }
    }, cancel);
    loadTask.ContinueWith(
        _ => cleanupPreStart(),
        CancellationToken.None,
        TaskContinuationOptions.OnlyOnCanceled | TaskContinuationOptions.ExecuteSynchronously,
        TaskScheduler.Default);
}
catch
{
    cleanupPreStart();
    throw;
}
```

Do not alter the two token domains:

- the busy loop still observes only `Program.GlobalProgramCancel`;
- `Task.Factory.StartNew(action, cancel)` and `LoadModel(...).Wait(cancel)` still use the scheduling caller's token.

Do not add `releasePressure`, generic refusal text, `Failure` assignment/delivery, or selected-pressure exception state to either cleanup function.

- [ ] **Step 5: Statically trace setup and dispatch races before committing**

Read the edited block and account for each path:

1. failure immediately after `IsLoading`;
2. failure after one or more claims returned and were captured;
3. a claim constructor that throws before returning, noting that its internal mutation is outside the owner's recovery guarantee;
4. `StartNew` throwing;
5. continuation attachment throwing before delegate entry;
6. continuation attachment throwing after delegate entry;
7. task cancellation before delegate entry;
8. delegate dispatch after pre-start cleanup;
9. started success, caught load failure, global-shutdown return, and cancellation from `Wait(cancel)`.

The outer catch must rethrow the original setup exception after attempting pre-start cleanup. If delegate entry already won, the catch loses the CAS and leaves live state for started cleanup.

- [ ] **Step 6: Run source-only static checks**

Run only:

```bash
git diff -- src/Backends/BackendHandler.cs
git diff --check -- src/Backends/BackendHandler.cs
rg -n "lifecyclePublished|lifecycleStarted|lifecyclePreStartCleaned|lifecycleStartedCleaned|IsLoading =|ReserveModelLoad =|BackendFailReasons|BadBackends|sess\\.Claim|claim\\.Dispose|StartNew|ContinueWith|Wait\\(cancel\\)|GlobalProgramCancel" src/Backends/BackendHandler.cs
git diff --name-only
```

Expected:

- `git diff --check` is silent.
- The in-scope production diff names only `src/Backends/BackendHandler.cs`.
- Known protected files may also appear in the full working-tree name list; they remain untouched and unstaged.
- No build or runtime result is claimed.

- [ ] **Step 7: Commit only the production file**

```bash
git add -- src/Backends/BackendHandler.cs
git diff --cached --name-only
git diff --cached --check
git commit -m "fix: make model-load cleanup ownership atomic"
```

Expected cached file list:

```text
src/Backends/BackendHandler.cs
```

Record the production commit and its parent. That exact `parent..commit` range is the Rank 11 production range.

## Task 2: Independent Static Specification and Quality Review

**Files:**
- Inspect: `src/Backends/BackendHandler.cs:1083-1614`
- Inspect: `src/Accounts/Session.cs:88-150`
- Inspect: `docs/superpowers/specs/2026-07-25-model-load-cleanup-ownership-design.md`
- Do not modify production unless a reviewer identifies a concrete defect

- [ ] **Step 1: Run the subagent-driven specification review**

Dispatch a fresh specification reviewer with the fixed design boundary `c3a7f45d`, the exact production range, and the constraints above. Require an explicit `PASS` or a list of requirement deviations.

The reviewer must trace all 24 static-verification obligations in the approved design, especially:

- caller/token owner equal to and different from selected `highestPressure`;
- partial publication and the constructor-before-return recovery limit;
- attachment/start races and all lifecycle CAS winners/losers;
- started cleanup ordering;
- unchanged `ReleasePressure(true)`, generic refusal, `Failure` assignment/delivery, `NotifyWillLoad`, detailed-error input, retry, and exclusion ownership;
- unchanged global-shutdown versus scheduling-token behavior.

- [ ] **Step 2: Run the subagent-driven quality review**

After specification review passes, dispatch a fresh quality reviewer. Require inspection for:

- C# 12 correctness and repository explicit-type/full-brace conventions;
- closure capture and `Interlocked.CompareExchange(ref lifecycle, ...)` consistency;
- no possible double `GenClaim.Dispose`;
- no backend reservation/classification/refresh on the pre-start path;
- no active backend work after pre-start cleanup wins;
- no silent swallowing of setup exceptions;
- exact four-argument continuation overload and explicit `TaskScheduler.Default`;
- no public API/ABI or adjacent-scope change.

- [ ] **Step 3: Correct findings with narrowly scoped commits**

For each accepted finding:

1. use `superpowers:receiving-code-review` rigor to verify the finding against the fixed design;
2. modify only `src/Backends/BackendHandler.cs`;
3. rerun the exact source-only checks from Task 1;
4. commit with a finding-specific message, for example:

```bash
git add -- src/Backends/BackendHandler.cs
git diff --cached --name-only
git diff --cached --check
git commit -m "fix: close model-load cleanup race"
```

Then send the full corrected production range back through both specification and quality review until both pass. Do not squash; the final production range is the parent of the first production commit through the final source correction commit.

- [ ] **Step 4: Record static evidence without runtime claims**

Run only:

```bash
rank11_first_source_commit="$(git log --format=%H --reverse c3a7f45d..HEAD -- src/Backends/BackendHandler.cs | sed -n '1p')"
rank11_production_tip="$(git log --format=%H c3a7f45d..HEAD -- src/Backends/BackendHandler.cs | sed -n '1p')"
rank11_production_parent="$(git rev-parse "${rank11_first_source_commit}^")"
rank11_production_range="${rank11_production_parent}..${rank11_production_tip}"
git diff --name-only "${rank11_production_range}"
git diff --stat "${rank11_production_range}"
git diff --check "${rank11_production_range}"
git log --oneline "${rank11_production_range}"
rg -n "IsLoading =|ReserveModelLoad =|BackendFailReasons|BadBackends|sess\\.Claim|claim\\.Dispose|StartNew|ContinueWith|Wait\\(cancel\\)|GlobalProgramCancel" src/Backends/BackendHandler.cs
```

Expected:

- exact production range changes one source file: `src/Backends/BackendHandler.cs`;
- fixed-range whitespace check is silent;
- reviewers pass;
- no claim is made about runtime races, counters, platform behavior, performance, builds, or tests.

## Task 3: Record Integrated Implementation Status Awaiting Validation

**Files:**
- Modify: `docs/superpowers/specs/2026-07-25-model-load-cleanup-ownership-design.md`
- Modify: `docs/superpowers/audits/2026-07-21-maintainability-architecture-refresh.md`

- [ ] **Step 1: Update the design record**

Change its status to **Implemented; awaiting maintainer validation** and add:

- exact production range and one-file scope;
- lifecycle states/transitions actually implemented;
- pre-start versus started cleanup policies;
- fixed four-argument continuation details;
- static review commands and both review outcomes;
- explicit statement that agents did not build, test, launch, inject faults, or validate runtime behavior;
- explicit claim-constructor-before-return limitation;
- preserved caller-versus-selected coupling and adjacent non-goals.

- [ ] **Step 2: Update Backend F17 and Rank 11 audit records**

Record Rank 11 as **implemented; awaiting maintainer validation**, without advancing Rank 12. Include:

- exact production range;
- exact one-source-file scope;
- static ownership proof and reviewer outcomes;
- pending Linux/Windows/other-platform/runtime/performance status;
- unchanged adjacent `LoadModelOnAll`, async `LoadModel`, pressure/session staleness, and P5 scope.

- [ ] **Step 3: Review and commit only the two records**

Run:

```bash
git diff -- docs/superpowers/specs/2026-07-25-model-load-cleanup-ownership-design.md docs/superpowers/audits/2026-07-21-maintainability-architecture-refresh.md
git diff --check -- docs/superpowers/specs/2026-07-25-model-load-cleanup-ownership-design.md docs/superpowers/audits/2026-07-21-maintainability-architecture-refresh.md
git add -- docs/superpowers/specs/2026-07-25-model-load-cleanup-ownership-design.md docs/superpowers/audits/2026-07-21-maintainability-architecture-refresh.md
git diff --cached --name-only
git commit -m "docs: record model-load cleanup ownership"
```

Expected cached files are exactly the design and audit records.

- [ ] **Step 4: Review the documentation task**

Dispatch fresh specification and quality reviewers. They must confirm the records match the actual fixed production range, describe static evidence rather than runtime proof, preserve the constructor recovery boundary, and leave Rank 11 awaiting validation. Correct and recommit documentation-only findings, then re-review.

## Task 4: Maintainer Runtime Validation Matrix

**Files:**
- No agent source edits during validation
- Maintainer later updates: design and audit records

- [ ] **Step 1: Hand the full matrix to Reaper176**

The maintainer—not an agent—builds and runs the software. For every case, record:

- scheduling caller/token owner and globally selected pressure;
- both pressure counts;
- selected `IsLoading`;
- selected sessions' claim membership and `LoadingModels`;
- loader reservation/availability;
- selected failure reason, exclusion, retry, and detailed-error inputs;
- caller generic refusal, `Failure`, delivery, and `NotifyWillLoad`;
- whether later requests for both pressures progress.

- [ ] **Step 2: Validate publication and pre-start ownership**

Maintainer cases:

1. failure after `IsLoading`;
2. failure after one or more returned/captured claims;
3. injected `StartNew` failure;
4. continuation-attachment failure before delegate entry;
5. continuation-attachment failure racing delegate entry;
6. pre-start cancellation with token owner equal to selected pressure, sole and shared;
7. pre-start cancellation with token owner different from selected pressure.

Expected: one policy wins; captured selected state returns to baseline exactly once; no backend classification/refresh occurs when pre-start cleanup wins; later selected-pressure requests progress; original setup exception propagates. A constructor failure before claim return is recorded as outside the transactional recovery guarantee.

- [ ] **Step 3: Validate started cancellation and shutdown behavior**

Maintainer cases:

1. busy usage with equal owner, then cancellation and later usage release;
2. busy usage with different owner, then cancellation and later usage release;
3. `(none)` while busy;
4. global shutdown while waiting for usage, equal and different owner;
5. cancellation after `LoadModel` begins, equal and different owner;
6. global shutdown during `LoadModel` with caller token uncanceled, then with caller token canceled.

Expected: busy polling still reacts only to global shutdown; ordinary load wait still reacts only to scheduling-caller `cancel`; `(none)` keeps its existing branch; selected full cleanup occurs once after started work exits; no claim is made that canceling the wait cancels the backend's underlying asynchronous load.

- [ ] **Step 4: Validate success, failure, retry, and caller coupling**

Maintainer cases:

1. successful ordinary load and `(none)`, equal and different owner;
2. readable load failure, equal and different owner;
3. model-name mismatch, equal and different owner;
4. all-loaders-failed callback with caller pressure present and non-null `UserInput`;
5. same callback with null `UserInput`;
6. same callback with null caller pressure and non-null `UserInput`;
7. repeated cancellation close to delegate dispatch;
8. normal generation under sole/shared and competing-model pressure.

Expected:

- selected reasons and exclusions appear once;
- selected retry/detail inputs remain selected-pressure state;
- callback release/null/generic-refusal order remains caller-owned;
- null caller pressure causes immediate callback return with no generic mutation;
- detailed selected-content exception still becomes and is delivered as scheduling caller `Failure`;
- no negative `LoadingModels`, duplicate disposal symptom, duplicate refresh/classification, permanent `IsLoading`, or unintended token-owner pressure mutation;
- normal selection, reservation, status, images, and later generation remain compatible.

- [ ] **Step 5: Record platforms and limitations**

Record Linux, Windows, and other-platform results separately. Record performance as unmeasured unless the maintainer explicitly measured it. A pass on one platform must not be generalized to another.

## Task 5: Close Validation Records

**Files:**
- Modify: `docs/superpowers/specs/2026-07-25-model-load-cleanup-ownership-design.md`
- Modify: `docs/superpowers/audits/2026-07-21-maintainability-architecture-refresh.md`

- [ ] **Step 1: Update records only after explicit maintainer results**

If and only if Reaper176 explicitly confirms the full matrix, change Rank 11 to **Implemented and maintainer-validated**. Record:

- maintainer identity and date;
- exact cases and observed outcomes;
- exact platform(s);
- unvalidated platforms and performance;
- constructor-before-return limitation;
- unchanged adjacent non-goals;
- exact production range, separate validation-record commit(s), and reviewer results.

If any case fails, keep status awaiting validation, capture the exact failure, and return to `superpowers:systematic-debugging` before any source correction.

- [ ] **Step 2: Commit validation records**

```bash
git diff --check -- docs/superpowers/specs/2026-07-25-model-load-cleanup-ownership-design.md docs/superpowers/audits/2026-07-21-maintainability-architecture-refresh.md
git add -- docs/superpowers/specs/2026-07-25-model-load-cleanup-ownership-design.md docs/superpowers/audits/2026-07-21-maintainability-architecture-refresh.md
git diff --cached --name-only
git commit -m "docs: validate model-load cleanup ownership"
```

Expected cached files are exactly the design and audit records.

- [ ] **Step 3: Review closure documentation**

Dispatch fresh specification and quality reviewers. Require confirmation that every claimed result was explicitly supplied by the maintainer, platform qualifiers are accurate, no agent runtime claim appears, the production range remains separate from documentation commits, and the next rank advances only if the audit's prerequisites permit it.

## Task 6: Final Static Evidence and Reporting

**Files:**
- Inspect only: exact production range and Rank 11 documentation commits

- [ ] **Step 1: Verify the final ranges without touching protected files**

Run:

```bash
rank11_first_source_commit="$(git log --format=%H --reverse c3a7f45d..HEAD -- src/Backends/BackendHandler.cs | sed -n '1p')"
rank11_production_tip="$(git log --format=%H c3a7f45d..HEAD -- src/Backends/BackendHandler.cs | sed -n '1p')"
rank11_production_parent="$(git rev-parse "${rank11_first_source_commit}^")"
rank11_production_range="${rank11_production_parent}..${rank11_production_tip}"
rank11_implementation_record="$(git log -1 --format=%H --grep='^docs: record model-load cleanup ownership$')"
rank11_validation_record="$(git log -1 --format=%H --grep='^docs: validate model-load cleanup ownership$')"
git diff --name-only "${rank11_production_range}"
git diff --stat "${rank11_production_range}"
git diff --check "${rank11_production_range}"
git log --oneline "${rank11_production_range}"
git show --stat --oneline "${rank11_implementation_record}"
git show --stat --oneline "${rank11_validation_record}"
git status --short
```

Expected:

- the production range changes exactly one source file, `src/Backends/BackendHandler.cs`;
- documentation commits are reported separately and do not inflate the production-file count;
- fixed-range whitespace is clean;
- protected user changes remain untouched and unstaged.

- [ ] **Step 2: Produce the final handoff**

Report:

- outcome and maintainer-validation status;
- exact `production-parent..production-tip`;
- production commit list;
- **one source file changed** in the production range, with insertion/deletion counts from the actual stat;
- implementation-record and validation-record commit IDs separately;
- specification and quality review outcomes;
- static commands and their observed results;
- maintainer identity, date, matrix, and platform results exactly as confirmed;
- explicit no-agent-build/test/runtime statement;
- remaining platform, performance, constructor-before-return, and adjacent-scope limitations;
- protected working-tree changes still present and untouched;
- ahead/behind and push status, without pushing unless the user separately asks.

Do not report an arbitrary multi-file count from documentation or earlier ranks as Rank 11's production scope.
