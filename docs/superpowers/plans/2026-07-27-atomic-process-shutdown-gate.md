# Atomic Process Shutdown Gate Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make `Program.Shutdown` admit exactly one process-wide shutdown owner with approved first-caller exit-code semantics while preserving every caller and the complete shutdown body.

**Architecture:** Replace the private volatile Boolean check/set in `Program.Shutdown` with one documented integer gate acquired through `Interlocked.CompareExchange`. The zero-to-one winner executes the unchanged body and owns its supplied exit-code behavior; every losing caller returns before any shutdown side effect.

**Tech Stack:** C# 12, .NET 8, `System.Threading.Interlocked`, SwarmUI process lifecycle and static API routes.

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
- Commit `3d244ede` is the approved pre-production design boundary.
- Preserve these public signatures exactly:

```csharp
public static void RequestRestart()
public static void Shutdown(int code = 0)
```

- Preserve the complete shutdown body after the gate, including the existing two-minute webhook bound, exit-code block, operation order, temp cleanup catch, and log flush.
- First-caller-wins is approved: a losing caller cannot change `Environment.ExitCode`, including a later restart request with code `42`.

## File Map

- Modify `src/Core/Program.cs`
  - replace the private volatile Boolean with one documented integer gate;
  - replace the separate shutdown check/set with one compare-exchange.
- Modify after static source review `docs/superpowers/specs/2026-07-27-atomic-process-shutdown-gate-design.md`
  - record the exact implementation and static review evidence.
- Modify after static source review `docs/superpowers/audits/2026-07-21-maintainability-architecture-refresh.md`
  - mark Core F10/rank 13 implemented awaiting maintainer validation.

Expected unchanged maintained consumers:

- `src/Core/WebServer.cs`;
- `src/WebAPI/AdminAPI.cs`;
- `src/Utils/Utilities.cs`;
- `src/Utils/NvidiaUtil.cs`; and
- `src/BuiltinExtensions/ComfyUIBackend/ComfyUIAPIAbstractBackend.cs`.

## Task 1: Implement the Atomic Process Gate

**Files:**

- Modify: `src/Core/Program.cs:674-689`

- [ ] **Step 1: Reconfirm the complete caller and exit-code inventory**

Run:

```bash
rg -n "Shutdown\\(|RequestRestart\\(|ApplicationStopping.Register|Unloading \\+=|ProcessExit \\+=|Environment.ExitCode\\s*=" \
  src/Core/Program.cs \
  src/Core/WebServer.cs \
  src/WebAPI/AdminAPI.cs \
  src/Utils/Utilities.cs \
  src/Utils/NvidiaUtil.cs \
  src/Utils/Logs.cs \
  src/BuiltinExtensions/ComfyUIBackend/ComfyUIAPIAbstractBackend.cs
```

Expected direct process-shutdown entry points:

- assembly unload;
- process exit;
- ASP.NET `ApplicationStopping`;
- main thread after `WaitForShutdown`;
- CI delayed shutdown;
- remote-control timeout; and
- delayed admin shutdown.

Expected restart-owner paths:

- `RequestRestart` supplies literal code `42`;
- maintenance auto-restart;
- update-and-restart;
- Windows .NET update;
- NVIDIA critical error; and
- Comfy critical GPU error.

Expected `Environment.ExitCode` writers:

- `Program.Shutdown` writes a winning nonzero code; and
- `Logs.Error` writes `1` in CI mode.

- [ ] **Step 2: Capture the exact pre-change shutdown body**

Run:

```bash
git show 3d244ede:src/Core/Program.cs | sed -n '674,740p'
nl -ba src/Core/Program.cs | sed -n '674,740p'
```

Confirm the current working-tree source matches the approved design boundary for:

- private `HasShutdown`;
- `RequestRestart`;
- `Shutdown` signature;
- separate guard;
- webhook and exit-code order;
- every shutdown operation; and
- final log flush.

If the committed source changed after `3d244ede`, stop and return to design review rather than applying the literal patch blindly.

- [ ] **Step 3: Replace the private Boolean with the documented integer gate**

Replace:

```csharp
    private volatile static bool HasShutdown = false;
```

with:

```csharp
    /// <summary>Zero before process shutdown begins; atomically changed to one by the single shutdown owner.</summary>
    private static int HasShutdown;
```

Do not add a lock, task, exit-code field, completion state, or reset path.

- [ ] **Step 4: Replace only the separate check/set**

Replace:

```csharp
        if (HasShutdown)
        {
            return;
        }
        HasShutdown = true;
```

with:

```csharp
        if (Interlocked.CompareExchange(ref HasShutdown, 1, 0) != 0)
        {
            return;
        }
```

This compare-exchange must remain the first operation in `Shutdown`. Do not move exit-code assignment before the webhook or otherwise modify the post-gate body.

- [ ] **Step 5: Review first-caller exit-code semantics**

Trace these cases against the source:

1. winning `Shutdown(42)` passes the gate, waits for the webhook, logs, and assigns `42`;
2. winning `Shutdown(0)` passes the gate and does not assign `Environment.ExitCode`;
3. losing `Shutdown(42)` returns before the webhook and cannot upgrade a zero-code winner;
4. losing `Shutdown(0)` returns before the webhook and cannot affect a restart winner; and
5. a prior CI `Environment.ExitCode = 1` survives a winning zero-code shutdown.

Do not add logic that changes these approved outcomes.

- [ ] **Step 6: Prove the post-gate body and consumers are unchanged**

Run:

```bash
git diff -- src/Core/Program.cs
git diff -- src/Core/Program.cs | rg -n '^[+-]'
git diff -- \
  src/Core/WebServer.cs \
  src/WebAPI/AdminAPI.cs \
  src/Utils/Utilities.cs \
  src/Utils/NvidiaUtil.cs \
  src/BuiltinExtensions/ComfyUIBackend/ComfyUIAPIAbstractBackend.cs
```

Expected:

- only the field declaration/documentation and guard lines change in `Program.cs`;
- no caller changes;
- no public signature change;
- no shutdown-body operation changes; and
- no exit-code block change.

- [ ] **Step 7: Run permitted static checks**

Run:

```bash
rg -n "HasShutdown|CompareExchange|RequestRestart|public static void Shutdown|Environment.ExitCode" src/Core/Program.cs
git diff --check -- src/Core/Program.cs
git diff -- src/Core/Program.cs | rg -n '^[+-].*(public|protected)\\b' || true
git status --short --branch
```

Expected:

- one documented integer gate;
- one zero-to-one `CompareExchange`;
- unchanged public signatures;
- clean whitespace;
- only `src/Core/Program.cs` added to the protected pre-existing worktree state; and
- no staged file.

Do not build, test, launch, invoke shutdown, or call a live API.

- [ ] **Step 8: Commit the focused production change**

Run:

```bash
git add src/Core/Program.cs
git diff --cached --name-only
git diff --cached --check
git commit -m "fix: atomically gate process shutdown"
```

Expected staged scope: exactly `src/Core/Program.cs`.

## Task 2: Perform Integrated Static and Independent Source Review

**Files:**

- Review: `src/Core/Program.cs`
- Review: `src/Core/WebServer.cs`
- Review: `src/WebAPI/AdminAPI.cs`
- Review: `src/Utils/Utilities.cs`
- Review: `src/Utils/NvidiaUtil.cs`
- Review: `src/Utils/Logs.cs`
- Review: `src/BuiltinExtensions/ComfyUIBackend/ComfyUIAPIAbstractBackend.cs`
- Review: `docs/superpowers/specs/2026-07-27-atomic-process-shutdown-gate-design.md`

- [ ] **Step 1: Pin the design and production boundaries**

Run:

```bash
rank13_design_base="$(git rev-parse 3d244ede)"
rank13_source_head="$(git log -1 --format=%H -- src/Core/Program.cs)"

printf 'design_base=%s\nsource_head=%s\n' "$rank13_design_base" "$rank13_source_head"
git log --oneline "$rank13_design_base..$rank13_source_head" -- src/Core/Program.cs
git diff --name-only "$rank13_design_base..$rank13_source_head" -- src/Core/Program.cs
git diff --stat "$rank13_design_base..$rank13_source_head" -- src/Core/Program.cs
git diff --check "$rank13_design_base..$rank13_source_head" -- src/Core/Program.cs
```

Expected production projection: only `src/Core/Program.cs`, with one focused source commit.

- [ ] **Step 2: Run the design-conformance inventory**

Run:

```bash
rg -n "Shutdown\\(|RequestRestart\\(|ApplicationStopping.Register|Unloading \\+=|ProcessExit \\+=|HasShutdown|CompareExchange|Environment.ExitCode\\s*=" \
  src/Core/Program.cs \
  src/Core/WebServer.cs \
  src/WebAPI/AdminAPI.cs \
  src/Utils/Utilities.cs \
  src/Utils/NvidiaUtil.cs \
  src/Utils/Logs.cs \
  src/BuiltinExtensions/ComfyUIBackend/ComfyUIAPIAbstractBackend.cs
```

Compare every result with the approved design's direct callers, restart callers, and exit-code owners.

- [ ] **Step 3: Compare the complete shutdown body**

Run:

```bash
git diff 3d244ede..HEAD -- src/Core/Program.cs
git show 3d244ede:src/Core/Program.cs | sed -n '676,740p'
sed -n '677,740p' src/Core/Program.cs
```

Normalize only the expected field documentation/type and guard lines mentally. Confirm all text after the winning guard is unchanged through `Logs.Info("Process should end now.");`.

- [ ] **Step 4: Check ABI and protected scope**

Run:

```bash
git diff 3d244ede..HEAD -- src/Core/Program.cs | rg -n '^[+-].*(public|protected)\\b' || true
git diff --name-only 3d244ede..HEAD -- \
  src/Core/WebServer.cs \
  src/WebAPI/AdminAPI.cs \
  src/Utils/Utilities.cs \
  src/Utils/NvidiaUtil.cs \
  src/BuiltinExtensions/ComfyUIBackend/ComfyUIAPIAbstractBackend.cs \
  src/wwwroot \
  src/Pages \
  docs/APIRoutes \
  src/Extensions
```

Expected: both searches are silent.

- [ ] **Step 5: Dispatch a fresh specification reviewer**

Require the reviewer to verify:

- exactly one atomic zero-to-one owner;
- gate placement as the first `Shutdown` operation;
- first-caller exit-code ownership;
- losing-call return before every side effect;
- preservation of code `42` and prior CI code under a zero winner;
- complete direct/restart caller inventory;
- unchanged public signatures and callers;
- unchanged post-gate body, order, waits, exception behavior, and log flush;
- no completion task, reset, retry, or rank-14 work;
- exact one-file production scope;
- repository C# style and field XML documentation; and
- no agent runtime claim.

Correct specification findings in a focused source commit, then repeat until approved.

- [ ] **Step 6: Dispatch a fresh code-quality reviewer**

Require the reviewer to inspect:

- clarity and minimality;
- correct `Interlocked.CompareExchange` usage;
- absence of volatile-by-reference misuse;
- meaningful zero/one field documentation;
- no unnecessary state, lock, allocation, or blocking work;
- no reset path after partial shutdown;
- no later exit-code mutation by losers;
- unchanged shutdown-body readability and order;
- exact caller/source scope; and
- clean fixed-range whitespace.

Correct quality findings in a focused source commit, repeat specification review if behavior changes, and repeat quality review until approved.

## Task 3: Record Static Implementation Closure

**Files:**

- Modify: `docs/superpowers/specs/2026-07-27-atomic-process-shutdown-gate-design.md`
- Modify: `docs/superpowers/audits/2026-07-21-maintainability-architecture-refresh.md`

- [ ] **Step 1: Add the implementation record to the design**

Record:

- exact design base and source head;
- exact source commit list and one-file production projection;
- final integer gate and compare-exchange;
- approved first-caller exit-code behavior;
- complete unchanged caller/body/ordering boundary;
- specification and quality review outcomes;
- static commands and results;
- no agent build/test/runtime/platform/performance claim;
- unvalidated platforms and runtime interleavings; and
- status `Implemented; awaiting maintainer validation`.

- [ ] **Step 2: Update every current Core F10/rank 13 audit status**

In the audit:

- mark Core F10 and roadmap rank 13 implemented, awaiting maintainer validation;
- retain original baseline evidence as historical evidence;
- record the exact one-file source boundary and review outcomes;
- retain rank 13 as Recommended Next Project until live validation passes;
- do not advance, design, or implement rank 14;
- leave ranks 1, 2, and 8 awaiting their own validation;
- retain every validated rank;
- preserve all 12 top-level audit sections and 32 ranked roadmap entries; and
- preserve rank 12's Garuda Linux/Btrfs validation record.

- [ ] **Step 3: Self-review documentation**

Run:

```bash
rg -n "TBD|TODO|placeholder|Core F10|Rank 13|rank 13|Rank 14|rank 14|awaiting maintainer" \
  docs/superpowers/specs/2026-07-27-atomic-process-shutdown-gate-design.md \
  docs/superpowers/audits/2026-07-21-maintainability-architecture-refresh.md
git diff --check -- \
  docs/superpowers/specs/2026-07-27-atomic-process-shutdown-gate-design.md \
  docs/superpowers/audits/2026-07-21-maintainability-architecture-refresh.md
```

Resolve stale current-status wording while preserving explicitly historical plan/audit provenance.

- [ ] **Step 4: Commit documentation-only closure**

Run:

```bash
git add \
  docs/superpowers/specs/2026-07-27-atomic-process-shutdown-gate-design.md \
  docs/superpowers/audits/2026-07-21-maintainability-architecture-refresh.md
git diff --cached --name-only
git diff --cached --check
git commit -m "docs: record atomic shutdown ownership"
```

Expected staged scope: exactly the two documentation files.

- [ ] **Step 5: Dispatch fresh documentation specification and quality reviews**

Require both reviewers to verify:

- exact design/source boundary and commit facts;
- accurate atomic and exit-code semantics;
- current-versus-historical wording;
- complete compatibility and caveat boundary;
- accurate static/runtime distinction;
- rank 13 remains awaiting validation and recommended;
- rank 14 is not advanced;
- ranks 1, 2, and 8 remain awaiting validation;
- audit structure and roadmap count;
- documentation-only commit scope; and
- clean whitespace.

Correct documentation findings in focused commits and repeat both reviews.

## Task 4: Maintainer Validation and Final Record

**Files:**

- Modify after explicit maintainer result: `docs/superpowers/specs/2026-07-27-atomic-process-shutdown-gate-design.md`
- Modify after explicit maintainer result: `docs/superpowers/audits/2026-07-21-maintainability-architecture-refresh.md`

- [ ] **Step 1: Hand the maintainer the exact 16-case matrix**

Use the approved design's “Maintainer Validation Matrix” without shortening it:

1. Ordinary Web stop completes the existing shutdown sequence once.
2. A direct ordinary `Shutdown(0)` completes the sequence once.
3. `AdminAPI.ShutdownServer` retains its permission requirement, immediate success response, and half-second-delayed call.
4. An uncontested restart request produces exit code `42` and retains launcher restart behavior.
5. A winning ordinary shutdown ignores a later restart request and does not upgrade to `42`.
6. A winning restart ignores a later ordinary shutdown and retains code `42`.
7. A winning `Shutdown(0)` preserves a pre-existing CI error exit code rather than overwriting it.
8. Web stop and admin shutdown collision produces one webhook, event, cancellation/disposal, cleanup, and log-flush sequence.
9. Web stop and restart collision follows the approved first-caller exit-code rule and produces one sequence.
10. Admin shutdown and restart collision in both winner orders follows the approved response, delay, code, and one-sequence rules.
11. Control-timeout and restart collision follows the approved first-caller rule and produces one sequence.
12. Assembly-unload or process-exit callbacks arriving after shutdown begins return without duplicating work.
13. The shutdown webhook still runs before exit-code assignment, logs, events, cancellation, and disposal, with the same two-minute bound.
14. `PreShutdownEvent`, global cancellation, webserver, backends, sessions, proxy, model handlers, extensions, metadata, temp cleanup, and log flush retain their existing order.
15. Extension shutdown remains isolated per extension.
16. A shutdown-body failure does not reopen the gate or allow a later caller to retry the partial sequence.

Record the operating system/filesystem and maintainer's exact result. Do not infer a pass.

- [ ] **Step 2: If validation fails, diagnose before editing**

Use `superpowers:systematic-debugging`. Reproduce only through maintainer-supplied evidence, trace gate ownership and the responsible caller/body boundary, update the design if behavior changes, implement the smallest correction, repeat source reviews, and return affected matrix cases to the maintainer.

- [ ] **Step 3: If validation passes, update current status**

Only after explicit confirmation:

- mark Core F10/rank 13 implemented and maintainer-validated on the confirmed platform;
- record maintainer name, date, matrix scope, and outcomes;
- retain unvalidated-platform and no-performance caveats;
- retain first-caller exit-code semantics and partial-shutdown/no-retry caveat;
- keep agent evidence static-only;
- advance the audit's Recommended Next Project pointer to rank 14 without implementing or designing it; and
- leave ranks 1, 2, and 8 unchanged.

- [ ] **Step 4: Commit validation documentation only**

Run:

```bash
git add \
  docs/superpowers/specs/2026-07-27-atomic-process-shutdown-gate-design.md \
  docs/superpowers/audits/2026-07-21-maintainability-architecture-refresh.md
git diff --cached --name-only
git diff --cached --check
git commit -m "docs: validate atomic shutdown gate"
```

- [ ] **Step 5: Run final fresh reviews and static verification**

Dispatch fresh validation-record specification and quality reviewers. Then run:

```bash
rank13_design_base="$(git rev-parse 3d244ede)"
rank13_integrated_head="$(git rev-parse HEAD)"
rank13_source_head="$(git log -1 --format=%H -- src/Core/Program.cs)"
rank13_validation_commit="$(git log -1 --format=%H -- docs/superpowers/specs/2026-07-27-atomic-process-shutdown-gate-design.md)"

git log --oneline "$rank13_design_base..$rank13_integrated_head"
git diff --check "$rank13_design_base..$rank13_integrated_head"

git log --oneline "$rank13_design_base..$rank13_source_head" -- src/Core/Program.cs
git diff --name-only "$rank13_design_base..$rank13_source_head" -- src/Core/Program.cs
git diff --stat "$rank13_design_base..$rank13_source_head" -- src/Core/Program.cs
git diff --check "$rank13_design_base..$rank13_source_head" -- src/Core/Program.cs

git show --check --oneline "$rank13_validation_commit"
git diff --cached --name-only
git status --short --branch
```

Completion requires:

- fresh specification and quality reviews pass;
- integrated history and source-only projection remain distinct;
- production contains only the reviewed `Program.cs` gate change;
- validation documentation contains only the two approved docs;
- rank 14 is recommended but not implemented or designed;
- ranks 1, 2, and 8 remain pending;
- index is empty;
- unrelated working-tree changes remain untouched; and
- no agent build/test/runtime/performance claim appears.
