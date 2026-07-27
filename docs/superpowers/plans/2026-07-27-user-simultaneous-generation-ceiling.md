# User Simultaneous-Generation Ceiling Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Correct the request-local off-by-one in normal T2I and Image Batch so neither loop admits a new generation task when its cleaned active-task count already equals the captured effective limit.

**Architecture:** Preserve both route-owned schedulers and change only their admission predicates from greater-than to greater-than-or-equal. Verify the two local invariants and all surrounding compatibility behavior statically, then have the maintainer run the approved live matrix; do not introduce account-wide coordination or claim a user-wide aggregate ceiling.

**Tech Stack:** C# 12, .NET 8, `Task`, SwarmUI `Session.GenClaim`, normal HTTP/WebSocket T2I, built-in Image Batch streaming route.

---

## Repository Constraints

- Work directly on `master`; do not create or use a worktree.
- Reaper176 is an approved maintainer under `AGENTS.md`.
- Agents must not build, launch, or test SwarmUI. The maintainer performs all runtime validation.
- Agents may run source searches, numbered inspection, exact-range diff inspection, `git diff --check`, and permitted static linters.
- Do not create automated tests: repository policy states that automated tests are not used and agents cannot run any form of testing.
- Never edit generated `docs/APIRoutes`.
- Never edit or stage user data, generated/build state, external extensions, upstream code, or unrelated working-tree changes.
- Preserve the existing uncommitted maintainer files:
  - `src/Data/Settings.fds`;
  - `src/Pages/Text2Image.cshtml`;
  - `src/wwwroot/js/genpage/gentab/loras.js`;
  - `src/wwwroot/js/genpage/main.js`; and
  - `Data.pre-restore-2026-07-19/`.
- Commit `e48920be` is the approved Rank 15 design boundary.
- Preserve these handler signatures exactly:

```csharp
public static async Task GenT2I_Internal(Session session, (int, JObject, SharedGenT2IData, int) input, Action<JObject> output, bool isWS)

public static async Task GenBatchRun_Internal(Session session, (JObject, string, string, bool, bool, bool, string[], string, bool) input, Action<JObject> output, bool isWS)
```

- Preserve `RoleData.MaxT2ISimultaneous`, `User.CalcMaxT2ISimultaneous`, the at-least-one effective limit, and one captured `max_degrees` value per handler invocation.
- Preserve each local task list, `removeDoneTasks`, `Task.WhenAny`, cancellation-check placement, fault logging, final drain, and route contract.
- Preserve normal T2I's seed/index behavior, ordering delay, keep-alives, outputs, discards, grid behavior, and webhooks.
- Preserve Image Batch input order, resolution handling, filenames, metadata, streamed outputs, status, and webhooks.
- Do not edit `Session.GenClaim`, `T2IEngine.CreateImageTask`, API transport helpers, Grid Generator, backend selection, or backend capacity.
- Do not add a helper, semaphore, lease, lock, shared scheduler, cancellation wake-up, instrumentation, benchmark, or public member.
- The guarantee is request-local. Do not claim aggregate enforcement across concurrent HTTP requests, overlapping WebSocket producers, Image Batch, or multiple sessions for one account.

## File Map

- Modify `src/WebAPI/T2IAPI.cs`
  - change only the `GenT2I_Internal` admission predicate.
- Modify `src/BuiltinExtensions/ImageBatchTool/ImageBatchToolExtension.cs`
  - change only the `GenBatchRun_Internal` admission predicate.
- Modify after approved static source review `docs/superpowers/specs/2026-07-27-user-simultaneous-generation-ceiling-design.md`
  - record the exact production boundary, local invariant, source-review outcomes, and static-only evidence.
- Modify after approved static source review `docs/superpowers/audits/2026-07-21-maintainability-architecture-refresh.md`
  - mark Backend F16/rank 15 implemented awaiting maintainer validation while retaining the request-local caveat.

Expected unchanged maintained source:

- `src/Accounts/Role.cs`;
- `src/Accounts/User.cs`;
- `src/Accounts/Session.cs`;
- `src/Text2Image/T2IEngine.cs`;
- `src/WebAPI/API.cs`;
- `src/BuiltinExtensions/GridGenerator/GridGeneratorExtension.cs`;
- backend scheduling and capacity code; and
- all frontend and route-documentation files.

## Task 1: Correct Both Admission Predicates

**Files:**

- Modify: `src/WebAPI/T2IAPI.cs:427`
- Modify: `src/BuiltinExtensions/ImageBatchTool/ImageBatchToolExtension.cs:125`

- [ ] **Step 1: Reconfirm the approved source boundary**

Run:

```bash
git diff e48920be -- \
  src/WebAPI/T2IAPI.cs \
  src/BuiltinExtensions/ImageBatchTool/ImageBatchToolExtension.cs

nl -ba src/WebAPI/T2IAPI.cs | sed -n '350,480p'
nl -ba src/BuiltinExtensions/ImageBatchTool/ImageBatchToolExtension.cs | sed -n '100,235p'
```

Expected:

- no source diff from `e48920be`;
- both handlers use a local `List<Task> tasks`;
- both call `removeDoneTasks()` immediately before admission;
- both currently use `while (tasks.Count > max_degrees)`;
- both await `Task.WhenAny(tasks)` and clean again inside the loop;
- both check `claim.ShouldCancel` after the loop; and
- all downstream route behavior matches the approved design.

If either predicate or its surrounding cleanup/wait/cancellation sequence differs, stop implementation and return to design review instead of applying the literal edit.

- [ ] **Step 2: Reconfirm every effective-limit consumer and the request-local caveat**

Run:

```bash
rg -n -C 8 "CalcMaxT2ISimultaneous|MaxT2ISimultaneous" \
  src \
  --glob '*.cs' \
  --glob '!src/bin/**' \
  --glob '!src/obj/**' \
  --glob '!src/Extensions/**'

rg -n -C 8 "RunWebsocketHandlerCallWS\\(GenT2I_Internal|RunWebsocketHandlerCallDirect\\(GenT2I_Internal|GenBatchRun_Internal" \
  src/WebAPI/T2IAPI.cs \
  src/BuiltinExtensions/ImageBatchTool/ImageBatchToolExtension.cs
```

Confirm:

- `User.CalcMaxT2ISimultaneous` remains at least one;
- normal T2I and Image Batch are the two audited `tasks.Count` consumers;
- Grid Generator has its own scheduler and is not edited;
- one WebSocket may own overlapping `GenT2I_Internal` invocations;
- direct HTTP calls and separate sessions may overlap; and
- no account-wide admission owner currently exists.

- [ ] **Step 3: Apply the exact normal T2I correction**

In `src/WebAPI/T2IAPI.cs`, replace only:

```csharp
            while (tasks.Count > max_degrees)
```

with:

```csharp
            while (tasks.Count >= max_degrees)
```

Do not change `removeDoneTasks`, `Task.WhenAny`, the cancellation check, task creation, the ordering delay, or the final drain.

- [ ] **Step 4: Apply the exact Image Batch correction**

In `src/BuiltinExtensions/ImageBatchTool/ImageBatchToolExtension.cs`, replace only:

```csharp
            while (tasks.Count > max_degrees)
```

with:

```csharp
            while (tasks.Count >= max_degrees)
```

Do not change image preparation, `removeDoneTasks`, `Task.WhenAny`, the cancellation check, task creation, output handling, or the final drain.

- [ ] **Step 5: Trace the admission arithmetic**

Trace each handler in source order and confirm:

1. completed tasks are removed before the comparison;
2. limit 1 with count 0 admits one task;
3. limit 1 with count 1 waits and cannot admit a second task;
4. limit 2 with count 1 admits one task;
5. limit 2 with count 2 waits and cannot admit a third task;
6. limit `N` admits only while the cleaned count is below `N`;
7. after `Task.WhenAny`, completed tasks are removed before reevaluation;
8. a task completing between cleanup and addition can only reduce real concurrency;
9. `CalcMaxT2ISimultaneous >= 1` means an entered wait loop always has at least one task for `Task.WhenAny`; and
10. each invariant applies only to that handler invocation's local task list.

- [ ] **Step 6: Prove the compatibility boundary**

Run:

```bash
git diff -- \
  src/WebAPI/T2IAPI.cs \
  src/BuiltinExtensions/ImageBatchTool/ImageBatchToolExtension.cs

git diff --numstat -- \
  src/WebAPI/T2IAPI.cs \
  src/BuiltinExtensions/ImageBatchTool/ImageBatchToolExtension.cs

git diff --word-diff=porcelain -- \
  src/WebAPI/T2IAPI.cs \
  src/BuiltinExtensions/ImageBatchTool/ImageBatchToolExtension.cs

git diff e48920be -- \
  src/Accounts/Role.cs \
  src/Accounts/User.cs \
  src/Accounts/Session.cs \
  src/Text2Image/T2IEngine.cs \
  src/WebAPI/API.cs \
  src/BuiltinExtensions/GridGenerator/GridGeneratorExtension.cs
```

Expected:

- exactly one one-character addition in each approved source file;
- no deletion and no surrounding source change;
- unchanged expected source files produce no output; and
- no public or protected declaration changes.

- [ ] **Step 7: Run permitted static checks**

Run:

```bash
rg -n -C 4 "while \\(tasks.Count >= max_degrees\\)" \
  src/WebAPI/T2IAPI.cs \
  src/BuiltinExtensions/ImageBatchTool/ImageBatchToolExtension.cs

rg -n "while \\(tasks.Count > max_degrees\\)" \
  src/WebAPI/T2IAPI.cs \
  src/BuiltinExtensions/ImageBatchTool/ImageBatchToolExtension.cs || true

git diff --check -- \
  src/WebAPI/T2IAPI.cs \
  src/BuiltinExtensions/ImageBatchTool/ImageBatchToolExtension.cs

git diff --name-only
git status --short --branch
```

Expected:

- exactly two corrected predicate matches;
- no old predicate match in either file;
- clean whitespace;
- only the two approved source files added to the protected pre-existing working-tree state; and
- no staged file.

Do not build, test, launch, generate an image, invoke Image Batch, or call a live API.

- [ ] **Step 8: Commit the focused production correction**

Run:

```bash
git add \
  src/WebAPI/T2IAPI.cs \
  src/BuiltinExtensions/ImageBatchTool/ImageBatchToolExtension.cs

git diff --cached --name-only
git diff --cached --check
git diff --cached --stat
git commit -m "fix: enforce simultaneous generation ceiling"
```

Expected staged scope:

```text
src/BuiltinExtensions/ImageBatchTool/ImageBatchToolExtension.cs
src/WebAPI/T2IAPI.cs
```

Expected line-based production numstat: two insertions and two deletions overall, one changed line in each file. The semantic source change is exactly two added `=` characters.

## Task 2: Perform Integrated Static and Source Review

**Files:**

- Review: `src/WebAPI/T2IAPI.cs`
- Review: `src/BuiltinExtensions/ImageBatchTool/ImageBatchToolExtension.cs`
- Review: `src/Accounts/Role.cs`
- Review: `src/Accounts/User.cs`
- Review: `src/Accounts/Session.cs`
- Review: `src/Text2Image/T2IEngine.cs`
- Review: `src/WebAPI/API.cs`
- Review: `src/BuiltinExtensions/GridGenerator/GridGeneratorExtension.cs`
- Review: `docs/superpowers/specs/2026-07-27-user-simultaneous-generation-ceiling-design.md`

- [ ] **Step 1: Pin the design and production boundaries**

Run:

```bash
rank15_design_base="$(git rev-parse e48920be)"
rank15_source_head="$(git log -1 --format=%H -- \
  src/WebAPI/T2IAPI.cs \
  src/BuiltinExtensions/ImageBatchTool/ImageBatchToolExtension.cs)"

printf 'design_base=%s\nsource_head=%s\n' "$rank15_design_base" "$rank15_source_head"
git log --oneline "$rank15_design_base..$rank15_source_head" -- \
  src/WebAPI/T2IAPI.cs \
  src/BuiltinExtensions/ImageBatchTool/ImageBatchToolExtension.cs
git diff --name-only "$rank15_design_base..$rank15_source_head" -- \
  src/WebAPI/T2IAPI.cs \
  src/BuiltinExtensions/ImageBatchTool/ImageBatchToolExtension.cs
git diff --numstat "$rank15_design_base..$rank15_source_head" -- \
  src/WebAPI/T2IAPI.cs \
  src/BuiltinExtensions/ImageBatchTool/ImageBatchToolExtension.cs
git diff --check "$rank15_design_base..$rank15_source_head" -- \
  src/WebAPI/T2IAPI.cs \
  src/BuiltinExtensions/ImageBatchTool/ImageBatchToolExtension.cs
```

Expected production projection:

- one focused source commit unless a review correction was required;
- exactly the two approved files;
- one insertion and one deletion in each file, representing one added `=` character per predicate; and
- clean whitespace.

- [ ] **Step 2: Run the design-conformance inventory**

Run:

```bash
rg -n -C 12 "removeDoneTasks|CalcMaxT2ISimultaneous|while \\(tasks.Count >= max_degrees\\)|Task.WhenAny\\(tasks\\)|claim.ShouldCancel|tasks.Add" \
  src/WebAPI/T2IAPI.cs \
  src/BuiltinExtensions/ImageBatchTool/ImageBatchToolExtension.cs

rg -n -C 6 "CalcMaxT2ISimultaneous|MaxT2ISimultaneous|Extend\\(|Complete\\(" \
  src/Accounts/Role.cs \
  src/Accounts/User.cs \
  src/Accounts/Session.cs \
  src/Text2Image/T2IEngine.cs \
  src/BuiltinExtensions/GridGenerator/GridGeneratorExtension.cs
```

Compare every result with the approved local admission invariant, claim-transition boundary, Grid Generator exclusion, and account-wide caveat.

- [ ] **Step 3: Compare complete route neighborhoods with the design boundary**

Run:

```bash
git diff e48920be..HEAD -- \
  src/WebAPI/T2IAPI.cs \
  src/BuiltinExtensions/ImageBatchTool/ImageBatchToolExtension.cs

git show e48920be:src/WebAPI/T2IAPI.cs | sed -n '350,480p'
sed -n '350,480p' src/WebAPI/T2IAPI.cs

git show e48920be:src/BuiltinExtensions/ImageBatchTool/ImageBatchToolExtension.cs | sed -n '100,235p'
sed -n '100,235p' src/BuiltinExtensions/ImageBatchTool/ImageBatchToolExtension.cs
```

Confirm:

- both cleanup functions are unchanged;
- both effective-limit captures are unchanged;
- only equality was added to the two waits;
- both `Task.WhenAny` and post-wait cleanup calls are unchanged;
- cancellation checks retain their exact placement;
- normal T2I task creation, ordering delay, final keep-alive drain, indices, and outputs are unchanged; and
- Image Batch preparation, task creation, output writing, webhooks, and final drain are unchanged.

- [ ] **Step 4: Check public ABI and protected scope**

Run:

```bash
git diff e48920be..HEAD -- \
  src/WebAPI/T2IAPI.cs \
  src/BuiltinExtensions/ImageBatchTool/ImageBatchToolExtension.cs \
  | rg -n '^[+-].*(public|protected)\b' || true

git diff --name-only e48920be..HEAD -- \
  src/Accounts \
  src/Text2Image \
  src/WebAPI/API.cs \
  src/BuiltinExtensions/GridGenerator \
  src/Backends \
  src/wwwroot \
  src/Pages \
  docs/APIRoutes \
  src/Extensions
```

Expected:

- the public/protected declaration search is silent; and
- the protected-scope search reports no file.

`src/WebAPI/T2IAPI.cs` is intentionally excluded from the second command because it is one of the two approved source files.

- [ ] **Step 5: Obtain a fresh specification review**

Under the selected execution workflow, have a fresh reviewer return `SOURCE_SPEC_APPROVED` only after verifying:

- both and only both audited predicates changed;
- equality now blocks admission at limits 1, 2, and larger;
- completed-task cleanup still precedes and follows the wait;
- the at-least-one effective limit keeps `Task.WhenAny(tasks)` valid;
- cancellation, task faults, ordering, keep-alives, indices, outputs, and route contracts are preserved;
- `Session.GenClaim`, `T2IEngine`, role calculation, Grid Generator, and backend capacity are unchanged;
- the guarantee is explicitly request-local;
- concurrent routes/sessions remain an account-wide caveat;
- exact two-file/two-character production scope; and
- no agent runtime, platform, filesystem, fairness, or performance claim.

If the reviewer identifies a concrete source defect, correct only that defect in a focused source commit and repeat the specification review.

- [ ] **Step 6: Obtain a fresh code-quality review**

Under the selected execution workflow, have a fresh reviewer return `SOURCE_QUALITY_APPROVED` only after verifying:

- the direct comparisons are clearer than a new helper;
- no duplicated behavior beyond the pre-existing two local loops was added;
- no unnecessary abstraction, synchronization, allocation, field, or public member appears;
- repository C# syntax conventions remain satisfied;
- the corrected waits cannot call `Task.WhenAny` with an empty list;
- no busy wait, deadlock, exception suppression, or new cancellation behavior was introduced;
- surrounding route code is byte-for-byte unchanged;
- the fixed-range diff is minimal and whitespace-clean; and
- protected maintainer work was not staged or modified by Rank 15.

If the reviewer identifies a concrete quality defect, correct only that defect in a focused source commit, repeat the specification review if behavior changed, and repeat quality review.

## Task 3: Record Static Implementation Closure

**Files:**

- Modify: `docs/superpowers/specs/2026-07-27-user-simultaneous-generation-ceiling-design.md`
- Modify: `docs/superpowers/audits/2026-07-21-maintainability-architecture-refresh.md`

- [ ] **Step 1: Add the implementation record to the design**

Add an `## Implementation Record` section before `## Maintainer Validation Matrix` containing:

- status `Implemented; awaiting maintainer validation`;
- exact approved design base and final source head;
- integrated history versus source-path-filtered history;
- exact production commit list, both source files, and numstat;
- the final greater-than-or-equal predicates and local admission invariant;
- preserved cleanup, wait, cancellation, ordering, keep-alive, index, error, output, webhook, claim, role, Grid Generator, backend, and ABI behavior;
- the account-wide concurrency caveat;
- `SOURCE_SPEC_APPROVED` and `SOURCE_QUALITY_APPROVED` outcomes;
- exact static commands and results;
- no agent build/test/runtime/platform/filesystem/performance claim; and
- unvalidated runtime scheduling and platform outcomes.

Do not alter or shorten the exact 20-case validation matrix.

- [ ] **Step 2: Update every current Backend F16/rank 15 audit status**

In the audit:

- mark Backend F16 and both rank 15 roadmap copies implemented, awaiting maintainer validation;
- retain the original `>` evidence as explicitly historical evidence;
- record the exact two-file/two-character source boundary and both source-review outcomes;
- state that the corrected invariant is request-local and does not aggregate concurrent invocations or sessions;
- retain rank 15 as Recommended Next Project until live validation passes;
- do not design or implement rank 16;
- leave ranks 1, 2, and 8 awaiting their own validation;
- retain every already validated rank, including ranks 12 through 14 on Garuda Linux/Btrfs;
- preserve all 12 top-level audit sections and 32 ranked roadmap entries; and
- preserve static/runtime and platform/performance evidence boundaries.

- [ ] **Step 3: Self-review documentation**

Run:

```bash
rg -n "TBD|TODO|placeholder|Backend F16|Rank 15|rank 15|Rank 16|rank 16|Recommended Next|awaiting maintainer|request-local|account-wide|20-case" \
  docs/superpowers/specs/2026-07-27-user-simultaneous-generation-ceiling-design.md \
  docs/superpowers/audits/2026-07-21-maintainability-architecture-refresh.md

git diff --check -- \
  docs/superpowers/specs/2026-07-27-user-simultaneous-generation-ceiling-design.md \
  docs/superpowers/audits/2026-07-21-maintainability-architecture-refresh.md

printf 'top_level_sections='
rg -c '^## ' docs/superpowers/audits/2026-07-21-maintainability-architecture-refresh.md

printf 'roadmap_entries='
awk '/^## Ranked Refactoring Roadmap$/{inside=1; next} inside && /^## /{exit} inside && /^### [0-9]+\./{count++} END{print count+0}' \
  docs/superpowers/audits/2026-07-21-maintainability-architecture-refresh.md

printf 'matrix_cases='
sed -n '/^## Maintainer Validation Matrix$/,/^## Success Criteria$/p' \
  docs/superpowers/specs/2026-07-27-user-simultaneous-generation-ceiling-design.md \
  | rg -c '^[0-9]+\.'
```

Expected:

- no placeholder or stale unimplemented Rank 15 wording outside explicitly historical evidence;
- Rank 15 remains implemented awaiting validation and recommended;
- Rank 16 remains neither designed nor implemented;
- account-wide caveats remain explicit;
- 12 audit sections;
- 32 ranked roadmap entries;
- 20 unchanged matrix cases; and
- clean whitespace.

- [ ] **Step 4: Commit documentation-only closure**

Run:

```bash
git add \
  docs/superpowers/specs/2026-07-27-user-simultaneous-generation-ceiling-design.md \
  docs/superpowers/audits/2026-07-21-maintainability-architecture-refresh.md

git diff --cached --name-only
git diff --cached --check
git commit -m "docs: record simultaneous generation ceiling"
```

Expected staged scope: exactly the two documentation files.

- [ ] **Step 5: Obtain fresh documentation specification and quality reviews**

Under the selected execution workflow, require both reviewers to verify:

- exact design/source boundary and commit facts;
- accurate two-predicate local admission semantics;
- accurate current-versus-historical wording;
- complete compatibility and account-wide caveat boundary;
- accurate static/runtime distinction;
- rank 15 remains awaiting validation and recommended;
- rank 16 is not designed or implemented;
- ranks 1, 2, and 8 remain awaiting validation;
- ranks 12 through 14 retain their validation records;
- audit structure, roadmap count, and exact matrix;
- documentation-only commit scope; and
- clean whitespace.

Correct concrete documentation findings in focused documentation commits and repeat both reviews.

## Task 4: Maintainer Validation and Final Record

**Files:**

- Modify after explicit maintainer result: `docs/superpowers/specs/2026-07-27-user-simultaneous-generation-ceiling-design.md`
- Modify after explicit maintainer result: `docs/superpowers/audits/2026-07-21-maintainability-architecture-refresh.md`

- [ ] **Step 1: Hand the maintainer the exact 20-case matrix**

Use the approved design's `Maintainer Validation Matrix` without shortening it:

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

Record the operating system, filesystem, and maintainer's exact result. Do not infer a pass.

- [ ] **Step 2: If validation fails, diagnose before editing**

Use `superpowers:systematic-debugging`. Work only from maintainer-supplied evidence, trace the responsible admission/cleanup/wait/cancellation boundary, update the design if behavior changes, implement the smallest correction, repeat source reviews, and return affected matrix cases to the maintainer.

- [ ] **Step 3: If validation passes, update current status**

Only after explicit confirmation:

- mark Backend F16/rank 15 implemented and maintainer-validated on the confirmed platform/filesystem;
- record maintainer Reaper176, date, exact 20-case scope, and outcome;
- retain the request-local/account-wide caveat;
- retain unvalidated-platform and no-performance caveats;
- keep agent evidence static-only;
- advance the audit's Recommended Next Project pointer to rank 16, `Complete backend loading phase on every storage outcome`, without designing or implementing it; and
- leave ranks 1, 2, and 8 unchanged.

- [ ] **Step 4: Commit validation documentation only**

Run:

```bash
git add \
  docs/superpowers/specs/2026-07-27-user-simultaneous-generation-ceiling-design.md \
  docs/superpowers/audits/2026-07-21-maintainability-architecture-refresh.md

git diff --cached --name-only
git diff --cached --check
git commit -m "docs: validate simultaneous generation ceiling"
```

Expected staged scope: exactly the two documentation files.

- [ ] **Step 5: Run final fresh reviews and static verification**

Under the selected execution workflow, obtain fresh validation-record specification and quality approvals. Then run:

```bash
rank15_design_base="$(git rev-parse e48920be)"
rank15_integrated_head="$(git rev-parse HEAD)"
rank15_source_head="$(git log -1 --format=%H -- \
  src/WebAPI/T2IAPI.cs \
  src/BuiltinExtensions/ImageBatchTool/ImageBatchToolExtension.cs)"
rank15_validation_commit="$(git log -1 --format=%H -- \
  docs/superpowers/specs/2026-07-27-user-simultaneous-generation-ceiling-design.md)"

git log --oneline "$rank15_design_base..$rank15_integrated_head"
git diff --check "$rank15_design_base..$rank15_integrated_head"

git log --oneline "$rank15_design_base..$rank15_source_head" -- \
  src/WebAPI/T2IAPI.cs \
  src/BuiltinExtensions/ImageBatchTool/ImageBatchToolExtension.cs
git diff --name-only "$rank15_design_base..$rank15_source_head" -- \
  src/WebAPI/T2IAPI.cs \
  src/BuiltinExtensions/ImageBatchTool/ImageBatchToolExtension.cs
git diff --numstat "$rank15_design_base..$rank15_source_head" -- \
  src/WebAPI/T2IAPI.cs \
  src/BuiltinExtensions/ImageBatchTool/ImageBatchToolExtension.cs
git diff --check "$rank15_design_base..$rank15_source_head" -- \
  src/WebAPI/T2IAPI.cs \
  src/BuiltinExtensions/ImageBatchTool/ImageBatchToolExtension.cs

git show --check --oneline "$rank15_validation_commit"
git diff --cached --name-only
git status --short --branch
```

Completion requires:

- fresh specification and quality reviews pass;
- integrated history and source-only projection remain distinct;
- production contains only the reviewed two one-character predicate additions;
- validation documentation contains only the two approved docs;
- request-local and account-wide boundaries remain explicit;
- rank 16 is recommended but not designed or implemented;
- ranks 1, 2, and 8 remain pending;
- ranks 12 through 15 validation records remain intact;
- index is empty;
- unrelated working-tree changes remain untouched; and
- no agent build/test/runtime/platform/filesystem/performance claim appears.
