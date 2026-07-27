# Backend Loading Phase Completion Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make `BackendHandler.LoadInternal()` publish startup storage-phase completion on every return or exception after monitor startup while preserving all existing loading, initialization, and failure behavior.

**Architecture:** Keep the initialization log and monitor-thread start before the lifecycle boundary, wrap the existing backend-file read and enumeration body in `try`, and publish `IsLoading = false` once from `finally`. Leave unexpected exceptions uncaught, retain startup-entry anti-thrash decisions while enumeration is active, and change no caller, counter, delay, monitor, request loop, or public contract.

**Tech Stack:** C# 12, .NET 8, `try`/`finally`, `Thread`, FreneticDataSyntax `FDSSection`, SwarmUI backend startup and initialization lifecycle.

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
- Commit `bcf96879d9d4769ce5913d8afd4f8a59d199d3eb` is the approved Rank 16 design boundary.
- Preserve these declarations exactly:

```csharp
public void Load()

public static bool IsLoading = true;

public void LoadInternal()

public static long CountBackendsFastLoaded = 0;
```

- Preserve `Load()`'s duplicate guard, call order, signal, loaded-model reassignment, and request-loop startup.
- Preserve the initialization log and successful `InternalInitMonitor` thread start before the new storage-phase boundary.
- Preserve every `FDSUtility.ReadFile` catch, diagnostic, return, null check, root-key order, unknown-type skip, backend population step, collection insertion, and `DoInitBackend` call.
- Preserve unexpected entry-processing exception propagation and partial-progress behavior.
- Preserve `DoInitBackend`'s synchronous `CountBackendsFastLoaded` increment and `shouldWait = count > 1 && IsLoading` capture.
- Preserve the anti-thrash delay formula, fast-capability predicate, queued backend path, retry behavior, statuses, and backend creation/reload callers.
- Do not change or add a field qualifier, helper, lock, interlocked state, event, cancellation path, parser recovery policy, per-entry catch, rollback, instrumentation, benchmark, or public member.
- Completion means storage enumeration/scheduling terminated; it does not mean backend initialization or readiness completed.

## File Map

- Modify `src/Backends/BackendHandler.cs`
  - change only `BackendHandler.LoadInternal()`;
  - place the existing post-monitor storage body inside `try`;
  - publish `IsLoading = false` from one `finally`;
  - remove the former successful-path trailing assignment.
- Modify after approved static source review `docs/superpowers/specs/2026-07-27-backend-loading-phase-completion-design.md`
  - record exact production provenance, lexical completion, compatibility, reviews, and static-only evidence.
- Modify after approved static source review `docs/superpowers/audits/2026-07-21-maintainability-architecture-refresh.md`
  - mark Backend F15/rank 16 implemented awaiting maintainer validation.

Expected unchanged maintained source:

- `src/Core/Program.cs`;
- every `BackendHandler` method outside `LoadInternal()`;
- `BackendHandler.IsLoading` and `CountBackendsFastLoaded` declarations;
- `DoInitBackend`, `LoadBackendDirect`, and `InternalInitMonitor`;
- `src/WebAPI/BackendAPI.cs`;
- every backend implementation;
- extension registration and backend creation code;
- FreneticDataSyntax parsing code; and
- all frontend, settings, launcher, generated, extension, upstream, and user-data paths.

## Task 1: Publish Storage-Phase Completion from `finally`

**Files:**

- Modify: `src/Backends/BackendHandler.cs:592-638`

- [ ] **Step 1: Reconfirm the approved source boundary**

Run:

```bash
git diff bcf96879 -- src/Backends/BackendHandler.cs

nl -ba src/Backends/BackendHandler.cs | sed -n '550,725p'

rg -n "public void Load\\(|public static bool IsLoading|public void LoadInternal\\(|IsLoading = false|public static long CountBackendsFastLoaded|bool shouldWait = count > 1 && IsLoading" \
  src/Backends/BackendHandler.cs
```

Expected:

- no source diff from `bcf96879`;
- `Load()` has its existing duplicate guard and post-`LoadInternal()` sequence;
- `LoadInternal()` starts `InternalInitMonitor` before storage access;
- missing file/directory, other read failure, and null result are the three tolerated early returns;
- accepted entries call `DoInitBackend` in the enumeration;
- one trailing `IsLoading = false` follows successful enumeration; and
- `DoInitBackend` captures `shouldWait` synchronously from the static flag.

If the committed source differs from this approved boundary, stop and return to design review rather than applying the literal replacement.

- [ ] **Step 2: Reconfirm owners, consumers, and later initialization paths**

Run:

```bash
rg -n "Backends\\.Load\\(" \
  src \
  --glob '*.cs' \
  --glob '!src/bin/**' \
  --glob '!src/obj/**' \
  --glob '!src/Extensions/**'

rg -n "LoadInternal\\(" \
  src \
  --glob '*.cs' \
  --glob '!src/bin/**' \
  --glob '!src/obj/**' \
  --glob '!src/Extensions/**'

rg -n "\\bIsLoading\\b" \
  src/Backends/BackendHandler.cs \
  src \
  --glob '*.cs' \
  --glob '!src/Backends/BackendHandler.cs' \
  --glob '!src/bin/**' \
  --glob '!src/obj/**' \
  --glob '!src/Extensions/**'

rg -n -C 10 "AddNewOfType\\(|AddNewNonrealBackend\\(|DoInitBackend\\(data\\)|ReloadBackend\\(" \
  src/Backends/BackendHandler.cs \
  src/WebAPI/BackendAPI.cs
```

Confirm:

- `Program.Main` is the sole maintained `Load()` caller;
- `Load()` is the sole maintained `LoadInternal()` caller;
- the startup static field is consumed only by `DoInitBackend`;
- unrelated `ModelRequestPressure.IsLoading` members are not part of Rank 16;
- add, non-real add, edit, reload, and API restart eventually call unchanged `DoInitBackend`; and
- no account-wide or cross-handler phase abstraction exists.

- [ ] **Step 3: Replace `LoadInternal()` with the approved lexical phase**

Replace only `BackendHandler.LoadInternal()` with:

```csharp
    /// <summary>Internal route for loading backends. Do not call directly.</summary>
    public void LoadInternal()
    {
        Logs.Init("Loading backends from file...");
        new Thread(InternalInitMonitor) { Name = "BackendHandler_Init_Monitor" }.Start();
        try
        {
            FDSSection file;
            try
            {
                file = FDSUtility.ReadFile(SaveFilePath);
            }
            catch (Exception ex)
            {
                if (ex is FileNotFoundException || ex is DirectoryNotFoundException)
                {
                    return;
                }
                Logs.Error($"Could not read Backends save file: {ex.ReadableString()}");
                return;
            }
            if (file is null)
            {
                return;
            }
            foreach (string idstr in file.GetRootKeys())
            {
                FDSSection section = file.GetSection(idstr);
                if (!BackendTypes.TryGetValue(section.GetString("type"), out BackendType type))
                {
                    Logs.Error($"Unknown backend type '{section.GetString("type")}' in save file, skipping backend #{idstr}.");
                    continue;
                }
                BackendData data = RawInstantiate(type);
                data.ID = int.Parse(idstr);
                data.AbstractBackend.AbstractBackendData = data;
                LastBackendID = Math.Max(LastBackendID, data.ID + 1);
                data.AbstractBackend.SettingsRaw = Activator.CreateInstance(type.SettingsClass) as AutoConfiguration;
                data.AbstractBackend.SettingsRaw.Load(section.GetSection("settings"));
                data.AbstractBackend.IsEnabled = section.GetBool("enabled", true).Value;
                data.AbstractBackend.Title = section.GetString("title", "");
                data.AbstractBackend.Handler = this;
                lock (CentralLock)
                {
                    AllBackends.TryAdd(data.ID, data);
                }
                DoInitBackend(data);
            }
        }
        finally
        {
            IsLoading = false;
        }
    }
```

Do not:

- include the initialization log or monitor-thread start inside the new `try`;
- add an outer `catch`;
- change the nested read catch or diagnostics;
- change entry processing or `DoInitBackend`;
- add cleanup for partially registered entries;
- edit `Load()`, `DoInitBackend`, another method, or another file; or
- change the field declaration or add memory/synchronization machinery.

- [ ] **Step 4: Trace every phase outcome**

Trace the edited source in execution order and confirm:

1. missing file returns through `finally`;
2. missing directory returns through `finally`;
3. another `FDSUtility.ReadFile` exception logs and returns through `finally`;
4. a null file result returns through `finally`;
5. empty valid storage reaches `finally`;
6. an unknown type logs/skips while the phase remains active for later entries, then reaches `finally`;
7. valid entries call `DoInitBackend` while `IsLoading` is still true;
8. an exception from root enumeration, section access, ID parsing, construction, settings load, insertion, or scheduling crosses `finally` and then propagates unchanged;
9. logging or monitor-start failure occurs before the new boundary and does not execute `finally`;
10. each phase entry executes one completion assignment; and
11. normal/tolerated return resumes the unchanged remainder of `Load()`, while an unexpected exception still prevents it.

- [ ] **Step 5: Trace captured delay and later initialization behavior**

Confirm from source:

1. `DoInitBackend` increments `CountBackendsFastLoaded` before reading `IsLoading`;
2. `shouldWait` is a local Boolean captured synchronously;
3. startup-file entries retain true phase state while being scheduled;
4. clearing the shared flag after enumeration does not alter already captured `shouldWait` values;
5. the first fast entry still avoids the delay because `count > 1` is false;
6. later startup fast entries retain the existing delay formula;
7. non-fast entries remain enqueued without consulting the flag;
8. later add/edit/reload calls see the completed phase after tolerated startup outcomes; and
9. the count is not reset or otherwise changed by phase completion.

- [ ] **Step 6: Prove the compatibility boundary**

Run:

```bash
git diff -- src/Backends/BackendHandler.cs

git diff bcf96879 -- \
  src/Core/Program.cs \
  src/WebAPI/BackendAPI.cs

git diff -- src/Backends/BackendHandler.cs \
  | rg -n '^[+-].*(public|protected)\\b' || true

rg -n "public void Load\\(|public static bool IsLoading = true|public void LoadInternal\\(|public static long CountBackendsFastLoaded = 0|bool shouldWait = count > 1 && IsLoading|TimeSpan.FromSeconds\\(1 \\+ Math.Min\\(5, count / 10\\.0\\)\\)|InternalInitMonitor|RequestHandlingLoop" \
  src/Backends/BackendHandler.cs
```

Expected:

- the diff is confined to `LoadInternal()`;
- `Program.cs` and `BackendAPI.cs` have no Rank 16 diff;
- no public/protected declaration change;
- the field declarations, delay capture/formula, monitor, and request-loop references remain; and
- `Load()` and `DoInitBackend()` are unchanged.

- [ ] **Step 7: Run permitted static checks**

Run:

```bash
sed -n '592,645p' src/Backends/BackendHandler.cs

rg -n "IsLoading = false" src/Backends/BackendHandler.cs

git diff --check -- src/Backends/BackendHandler.cs
git diff --name-only
git status --short --branch
```

Expected:

- one storage-phase `try/finally` after monitor startup;
- one startup `IsLoading = false` assignment in `LoadInternal()`'s `finally`;
- unrelated model-pressure assignments remain distinguishable later in the file;
- clean whitespace;
- only `src/Backends/BackendHandler.cs` added to the protected pre-existing working-tree state; and
- no staged file.

Do not build, test, launch, start a backend, or call a live API.

- [ ] **Step 8: Commit the focused production change**

Run:

```bash
git add src/Backends/BackendHandler.cs
git diff --cached --name-only
git diff --cached --check
git diff --cached --stat
git commit -m "fix: complete backend loading phase"
```

Expected staged scope:

```text
src/Backends/BackendHandler.cs
```

## Task 2: Perform Integrated Static and Source Review

**Files:**

- Review: `src/Backends/BackendHandler.cs`
- Review: `src/Core/Program.cs`
- Review: `src/WebAPI/BackendAPI.cs`
- Review: backend registration callers found by inventory
- Review: `docs/superpowers/specs/2026-07-27-backend-loading-phase-completion-design.md`

- [ ] **Step 1: Pin design and production boundaries**

Run:

```bash
rank16_design_base="$(git rev-parse bcf96879)"
rank16_source_head="$(git log -1 --format=%H -- src/Backends/BackendHandler.cs)"

printf 'design_base=%s\nsource_head=%s\n' "$rank16_design_base" "$rank16_source_head"
git log --oneline "$rank16_design_base..$rank16_source_head" -- src/Backends/BackendHandler.cs
git diff --name-only "$rank16_design_base..$rank16_source_head" -- src/Backends/BackendHandler.cs
git diff --stat "$rank16_design_base..$rank16_source_head" -- src/Backends/BackendHandler.cs
git diff --check "$rank16_design_base..$rank16_source_head" -- src/Backends/BackendHandler.cs
```

Expected production projection: one focused source commit changing only `src/Backends/BackendHandler.cs`, unless a review correction was required.

- [ ] **Step 2: Run the complete lifecycle inventory**

Run:

```bash
rg -n -C 14 "public void Load\\(|public static bool IsLoading|public void LoadInternal\\(|try|finally|IsLoading = false|public static long CountBackendsFastLoaded|bool shouldWait = count > 1 && IsLoading" \
  src/Backends/BackendHandler.cs

rg -n "Backends\\.Load\\(|LoadInternal\\(" \
  src \
  --glob '*.cs' \
  --glob '!src/bin/**' \
  --glob '!src/obj/**' \
  --glob '!src/Extensions/**'

rg -n -C 10 "AddNewOfType\\(|AddNewNonrealBackend\\(|DoInitBackend\\(data\\)|ReloadBackend\\(" \
  src/Backends/BackendHandler.cs \
  src/WebAPI/BackendAPI.cs
```

Compare every result with the approved owner, phase boundary, outcome, captured-delay, later-caller, and compatibility requirements.

- [ ] **Step 3: Compare complete methods with the design boundary**

Run:

```bash
git diff bcf96879..HEAD -- src/Backends/BackendHandler.cs

git show bcf96879:src/Backends/BackendHandler.cs | sed -n '575,670p'
sed -n '575,680p' src/Backends/BackendHandler.cs
```

Confirm:

- `Load()` is textually unchanged;
- log and monitor startup remain before the new `try`;
- the former storage body is unchanged apart from required indentation;
- all three tolerated returns cross `finally`;
- no outer catch changes unexpected exception propagation;
- `IsLoading = false` moved from the successful tail to `finally`;
- every configured-entry `DoInitBackend` call remains inside the active phase; and
- `DoInitBackend` and its delay behavior are textually unchanged.

- [ ] **Step 4: Check public ABI and protected scope**

Run:

```bash
git diff bcf96879..HEAD -- src/Backends/BackendHandler.cs \
  | rg -n '^[+-].*(public|protected)\\b' || true

git diff --name-only bcf96879..HEAD -- \
  src/Core/Program.cs \
  src/WebAPI \
  src/BuiltinExtensions \
  src/wwwroot \
  src/Pages \
  docs/APIRoutes \
  src/Extensions
```

Expected: both searches are silent.

- [ ] **Step 5: Obtain a fresh specification review**

Under the selected execution workflow, have a fresh reviewer return `SOURCE_SPEC_APPROVED` only after verifying:

- exact one-method/one-file production scope;
- log and successful monitor startup remain outside/before the phase;
- all tolerated read/null returns execute `finally`;
- all unexpected post-monitor storage/entry exceptions execute `finally` and propagate;
- no per-entry recovery, rollback, or new catch;
- startup entries call `DoInitBackend` while the phase is true;
- captured fast-delay decisions remain unchanged after phase completion;
- later add/edit/reload paths use unchanged initialization;
- `Load()`, counters, delay formula, monitor, request loop, retries, statuses, persistence, callers, and public ABI are unchanged;
- explicit pre-monitor-failure, duplicate-load, partial-progress, readiness, platform, and performance caveats; and
- no agent runtime claim.

If the reviewer identifies a concrete source defect, correct only that defect in a focused source commit and repeat specification review.

- [ ] **Step 6: Obtain a fresh code-quality review**

Under the selected execution workflow, have a fresh reviewer return `SOURCE_QUALITY_APPROVED` only after verifying:

- lexical `try/finally` ownership is clearer than duplicated assignments or a new abstraction;
- braces, indentation, explicit types, and naming follow repository C# conventions;
- no unreachable return, swallowed exception, double completion, or changed stack behavior;
- no unnecessary field, helper, synchronization, allocation, logging, or state;
- the phase name/meaning is not confused with backend readiness;
- the complete pre-existing storage body is preserved;
- no unrelated method or file changed;
- the fixed-range diff is whitespace-clean; and
- protected maintainer work was not staged or modified by Rank 16.

If the reviewer identifies a concrete quality defect, correct only that defect in a focused source commit, repeat specification review if behavior changed, and repeat quality review.

## Task 3: Record Static Implementation Closure

**Files:**

- Modify: `docs/superpowers/specs/2026-07-27-backend-loading-phase-completion-design.md`
- Modify: `docs/superpowers/audits/2026-07-21-maintainability-architecture-refresh.md`

- [ ] **Step 1: Add the implementation record to the design**

Add an `## Implementation Record` section before `## Maintainer Validation Matrix` containing:

- status `Implemented; awaiting maintainer validation`;
- exact approved design base, plan commit, and final source head;
- integrated history versus source-path-filtered history;
- exact production commit, source file, method boundary, and diff statistics;
- final post-monitor `try/finally` and one completion assignment;
- outcome tracing for missing, directory-missing, read error, null, empty, valid, unknown-type, and unexpected exception paths;
- unchanged pre-monitor failures and exception propagation;
- unchanged `Load()`, `DoInitBackend`, captured delay, count, monitor, request loop, retries, statuses, callers, persistence, and ABI;
- source specification and quality review outcomes;
- exact static commands/results;
- no agent build/test/runtime/platform/filesystem/performance claim; and
- unvalidated thread schedules, parser/filesystem outcomes, backend behavior, and platforms.

Do not alter or shorten the exact 14-case validation matrix.

- [ ] **Step 2: Update every current Backend F15/rank 16 audit status**

In the audit:

- mark Backend F15 and both rank 16 roadmap copies implemented, awaiting maintainer validation;
- retain the original trailing-assignment/early-return defect as explicitly historical;
- record exact one-file/one-method source scope and both source-review outcomes;
- retain rank 16 as Recommended Next Project until live validation passes;
- do not design or implement rank 17;
- leave ranks 1, 2, and 8 awaiting their own validation;
- retain every already validated rank, especially ranks 12 through 15 on Garuda Linux/Btrfs;
- preserve all 12 top-level audit sections and 32 ranked roadmap entries; and
- preserve static/runtime and platform/performance evidence boundaries.

- [ ] **Step 3: Self-review documentation**

Run:

```bash
rg -n "TBD|TODO|placeholder|Backend F15|Rank 16|rank 16|Rank 17|rank 17|Recommended Next|awaiting maintainer|post-monitor|finally|14-case" \
  docs/superpowers/specs/2026-07-27-backend-loading-phase-completion-design.md \
  docs/superpowers/audits/2026-07-21-maintainability-architecture-refresh.md

git diff --check -- \
  docs/superpowers/specs/2026-07-27-backend-loading-phase-completion-design.md \
  docs/superpowers/audits/2026-07-21-maintainability-architecture-refresh.md

printf 'top_level_sections='
rg -c '^## ' docs/superpowers/audits/2026-07-21-maintainability-architecture-refresh.md

printf 'roadmap_entries='
awk '/^## Ranked Refactoring Roadmap$/{inside=1; next} inside && /^## /{exit} inside && /^### [0-9]+\./{count++} END{print count+0}' \
  docs/superpowers/audits/2026-07-21-maintainability-architecture-refresh.md

printf 'matrix_cases='
sed -n '/^## Maintainer Validation Matrix$/,/^## Success Criteria$/p' \
  docs/superpowers/specs/2026-07-27-backend-loading-phase-completion-design.md \
  | rg -c '^[0-9]+\.'
```

Expected:

- no placeholder or stale unimplemented Rank 16 wording outside explicitly historical evidence;
- Rank 16 remains implemented awaiting validation and recommended;
- Rank 17 remains neither designed nor implemented;
- 12 audit sections;
- 32 ranked roadmap entries;
- 14 unchanged matrix cases; and
- clean whitespace.

- [ ] **Step 4: Commit documentation-only closure**

Run:

```bash
git add \
  docs/superpowers/specs/2026-07-27-backend-loading-phase-completion-design.md \
  docs/superpowers/audits/2026-07-21-maintainability-architecture-refresh.md

git diff --cached --name-only
git diff --cached --check
git commit -m "docs: record backend loading phase completion"
```

Expected staged scope: exactly the two documentation files.

- [ ] **Step 5: Obtain fresh documentation specification and quality reviews**

Under the selected execution workflow, require both reviewers to verify:

- exact design/source boundary and commit facts;
- accurate lexical phase, completion, exception, and captured-delay semantics;
- accurate current-versus-historical wording;
- complete compatibility and caveat boundary;
- accurate static/runtime distinction;
- rank 16 remains awaiting validation and recommended;
- rank 17 is not designed or implemented;
- ranks 1, 2, and 8 remain awaiting validation;
- ranks 12 through 15 retain their validation records;
- audit structure, roadmap count, and exact matrix;
- documentation-only commit scope; and
- clean whitespace.

Correct concrete documentation findings in focused documentation commits and repeat both reviews.

## Task 4: Maintainer Validation and Final Record

**Files:**

- Modify after explicit maintainer result: `docs/superpowers/specs/2026-07-27-backend-loading-phase-completion-design.md`
- Modify after explicit maintainer result: `docs/superpowers/audits/2026-07-21-maintainability-architecture-refresh.md`

- [ ] **Step 1: Hand the maintainer the exact 14-case matrix**

Use the approved design's `Maintainer Validation Matrix` without shortening it:

1. Missing `Backends.fds` completes startup without a backend-file error and leaves the storage phase complete.
2. Missing parent directory retains the same tolerant startup behavior and leaves the storage phase complete.
3. Unreadable or malformed storage that throws during `FDSUtility.ReadFile` retains the existing readable error and leaves the storage phase complete.
4. A null parser result, where reproducible, retains its silent return and leaves the storage phase complete.
5. Empty valid storage completes enumeration and leaves the storage phase complete.
6. An unknown backend type retains its existing diagnostic, is skipped, and does not prevent phase completion.
7. One valid fast backend retains its existing initialization and status behavior.
8. Multiple valid fast backends retain the first-boot anti-thrash delay decisions captured during startup enumeration.
9. Mixed fast and queued backends retain configured order, queueing, monitor processing, and status behavior.
10. Adding multiple fast backends after each tolerated terminal storage outcome does not apply the startup-only delay.
11. Reloading fast backends after storage completion does not apply the startup-only delay.
12. An injected unexpected entry-processing failure still propagates through the existing startup path while inspection confirms `IsLoading` was set false before propagation.
13. Backend initialization failure, retry delay/count, final error, and later retry behavior remain unchanged.
14. Normal multi-backend startup retains monitor activity, request-loop startup, backend statuses, model-state reassignment, and successful generation behavior.

Record the operating system, filesystem, and maintainer's exact result. Do not infer a pass.

- [ ] **Step 2: If validation fails, diagnose before editing**

Use `superpowers:systematic-debugging`. Work only from maintainer-supplied evidence, trace the responsible load/phase/initialization boundary, update the design if behavior changes, implement the smallest correction, repeat source reviews, and return affected matrix cases to the maintainer.

- [ ] **Step 3: If validation passes, update current status**

Only after explicit confirmation:

- mark Backend F15/rank 16 implemented and maintainer-validated on the confirmed platform/filesystem;
- record maintainer Reaper176, date, exact 14-case scope, and outcome;
- retain pre-monitor-failure, duplicate-load, partial-progress, readiness, and unexpected-exception caveats;
- retain unvalidated-platform and no-performance caveats;
- keep agent evidence static-only;
- advance the audit's Recommended Next Project pointer to rank 17, `Isolate session-ready callback failures`, without designing or implementing it; and
- leave ranks 1, 2, and 8 unchanged.

- [ ] **Step 4: Commit validation documentation only**

Run:

```bash
git add \
  docs/superpowers/specs/2026-07-27-backend-loading-phase-completion-design.md \
  docs/superpowers/audits/2026-07-21-maintainability-architecture-refresh.md

git diff --cached --name-only
git diff --cached --check
git commit -m "docs: validate backend loading phase completion"
```

Expected staged scope: exactly the two documentation files.

- [ ] **Step 5: Run final fresh reviews and static verification**

Under the selected execution workflow, obtain fresh validation-record specification and quality approvals. Then run:

```bash
rank16_design_base="$(git rev-parse bcf96879)"
rank16_integrated_head="$(git rev-parse HEAD)"
rank16_source_head="$(git log -1 --format=%H -- src/Backends/BackendHandler.cs)"
rank16_validation_commit="$(git log -1 --format=%H -- \
  docs/superpowers/specs/2026-07-27-backend-loading-phase-completion-design.md)"

git log --oneline "$rank16_design_base..$rank16_integrated_head"
git diff --check "$rank16_design_base..$rank16_integrated_head"

git log --oneline "$rank16_design_base..$rank16_source_head" -- src/Backends/BackendHandler.cs
git diff --name-only "$rank16_design_base..$rank16_source_head" -- src/Backends/BackendHandler.cs
git diff --stat "$rank16_design_base..$rank16_source_head" -- src/Backends/BackendHandler.cs
git diff --check "$rank16_design_base..$rank16_source_head" -- src/Backends/BackendHandler.cs

git show --check --oneline "$rank16_validation_commit"
git diff --cached --name-only
git status --short --branch
```

Completion requires:

- fresh specification and quality reviews pass;
- integrated history and source-only projection remain distinct;
- production contains only the reviewed `LoadInternal()` phase completion change;
- validation documentation contains only the two approved docs;
- rank 17 is recommended but not designed or implemented;
- ranks 1, 2, and 8 remain pending;
- ranks 12 through 16 validation records remain intact;
- index is empty;
- unrelated working-tree changes remain untouched; and
- no agent build/test/runtime/platform/filesystem/performance claim appears.
