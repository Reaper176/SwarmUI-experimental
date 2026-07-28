# Autoscaling Launch-Script Platform Validation Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Correct `AutoScalingBackend.Init` so only the documented platform-appropriate final script extensions pass its launch-script guard.

**Architecture:** Keep platform validation inside its existing owner and validation position. Derive the lowercase dotted final extension, compute one positive Windows-versus-non-Windows appropriateness Boolean, and reject its negation without changing file validation, process launch, settings, logging, statuses, scaling, or lifecycle behavior.

**Tech Stack:** C# 12, .NET 8, `System.IO.Path`, `System.Runtime.InteropServices.RuntimeInformation`, Git, Markdown.

---

## Execution Constraints

- Work directly on `master`; Reaper176 explicitly approved the repository's direct-master workflow.
- User/maintainer identity is Reaper176, who is listed in `AGENTS.md`.
- Agents never build, launch, test, execute scripts, start SwarmUI or its backends, automate a browser, call live APIs, or run test-executing lint in this repository.
- Runtime validation belongs exclusively to Reaper176.
- Use `apply_patch` for every file edit.
- Make the minimum possible source change.
- Do not touch `Data/`, `Output/`, `Models/`, `src/bin`, `src/obj`, `.vs`, `.git`, downloaded/upstream code, generated API documentation, or `src/Extensions`.
- Preserve the unrelated protected working changes and keep them unstaged:
  - `src/Data/Settings.fds`: `83 insertions / 3 deletions`;
  - `src/Pages/Text2Image.cshtml`: `9 insertions / 2 deletions`;
  - `src/wwwroot/js/genpage/gentab/loras.js`: `2 insertions / 0 deletions`;
  - `src/wwwroot/js/genpage/main.js`: `4 insertions / 5 deletions`;
  - untracked `Data.pre-restore-2026-07-19/`.
- The approved source/audit base is `e65550b0b90b62504f0495723db95567fe192673`.
- The approved design is `984930be` (`docs: design autoscaling launch-script validation`).
- No worktree is created because the maintainer selected direct-master execution and protected changes are already isolated by exact staging.

## File Map

- Modify source: `src/Backends/AutoScalingBackend.cs`
  - Existing owner of `AutoScalingBackend.Init`, the platform guard, process launch, and scaling lifecycle.
- Modify closure design: `docs/superpowers/specs/2026-07-27-autoscaling-launch-script-platform-validation-design.md`
  - Records implementation provenance, static review, and later maintainer evidence.
- Modify architecture audit: `docs/superpowers/audits/2026-07-21-maintainability-architecture-refresh.md`
  - Updates Backend F13, the risk register, roadmap Rank 20, current summaries, and recommended-next state.
- Review-only compatibility surface: `docs/Features/AutoScalingBackend.md`
  - Documents the `.sh` launch-script contract and existing launch protocol.
- Review-only callers/owners:
  - `src/Backends/BackendHandler.cs`;
  - `src/WebAPI/BackendAPI.cs`;
  - `src/Backends/NetworkBackendUtils.cs`;
  - `src/Backends/SwarmSwarmBackend.cs`.

## Task 1: Pin the Baseline and Complete the Guard Inventory

**Files:**

- Review: `src/Backends/AutoScalingBackend.cs`
- Review: `src/Backends/BackendHandler.cs`
- Review: `src/WebAPI/BackendAPI.cs`
- Review: `src/Backends/NetworkBackendUtils.cs`
- Review: `src/Backends/SwarmSwarmBackend.cs`
- Review: `docs/Features/AutoScalingBackend.md`
- Review: `docs/superpowers/audits/2026-07-21-maintainability-architecture-refresh.md`

- [ ] **Step 1: Confirm branch, design, index, and protected state**

Run:

```bash
git branch --show-current
git rev-parse HEAD
git log -1 --format='%H %s'
git merge-base --is-ancestor 984930be HEAD
git merge-base --is-ancestor 58ef9983 HEAD
git log --oneline --decorate 984930be^..HEAD -- \
  docs/superpowers/specs/2026-07-27-autoscaling-launch-script-platform-validation-design.md \
  docs/superpowers/plans/2026-07-27-autoscaling-launch-script-platform-validation.md
git diff --cached --name-only
git status --short
git diff --numstat -- \
  src/Data/Settings.fds \
  src/Pages/Text2Image.cshtml \
  src/wwwroot/js/genpage/gentab/loras.js \
  src/wwwroot/js/genpage/main.js
```

Expected:

- branch `master`;
- design `984930be` and plan `58ef9983` are ancestors of the current HEAD;
- HEAD immediately before Task 2 may be `58ef9983` or a later focused planning correction;
- no source commit or source diff since the approved base is allowed before Task 2;
- empty index;
- only the four protected tracked files and backup directory appear outside committed Rank 20 work;
- protected numstats remain `83/3`, `9/2`, `2/0`, and `4/5`.

- [ ] **Step 2: Pin the source file to the approved base**

Run:

```bash
git diff e65550b0b90b62504f0495723db95567fe192673..HEAD -- \
  src/Backends/AutoScalingBackend.cs
git rev-parse e65550b0b90b62504f0495723db95567fe192673:src/Backends/AutoScalingBackend.cs
git rev-parse 984930be:src/Backends/AutoScalingBackend.cs
git rev-parse 58ef9983:src/Backends/AutoScalingBackend.cs
git rev-parse HEAD:src/Backends/AutoScalingBackend.cs
```

Expected:

- no source diff since the approved base;
- identical source blob IDs at the approved base, design, plan, and current HEAD.

- [ ] **Step 3: Record the exact pre-change initialization flow**

Run:

```bash
nl -ba src/Backends/AutoScalingBackend.cs | sed -n '75,112p'
```

Record:

1. disabled/blank-script return;
2. invalid-settings return;
3. extension derivation;
4. ineffective platform conditional;
5. unchanged platform error, `ERRORED`, and return;
6. file-existence check;
7. `LOADING`;
8. minimum fill;
9. three hook registrations;
10. `RUNNING`.

- [ ] **Step 4: Inventory every maintained platform, script, launch, and lifecycle edge**

Run:

```bash
rg -n \
  'AutoScalingBackend|StartScript|Path\.GetExtension|RuntimeInformation\.IsOSPlatform|OSPlatform\.Windows|ProcessStartInfo|Process\.Start|Program\.TickEvent|Program\.PreShutdownEvent|NewBackendNeededEvent' \
  src/Backends/AutoScalingBackend.cs \
  src/Backends/BackendHandler.cs \
  src/WebAPI/BackendAPI.cs \
  src/Backends/NetworkBackendUtils.cs \
  src/Backends/SwarmSwarmBackend.cs \
  docs/Features/AutoScalingBackend.md \
  docs/superpowers/audits/2026-07-21-maintainability-architecture-refresh.md
```

Confirm:

- `Init` is the only maintained extension validator;
- `LaunchOne` is the only maintained process-start owner for this backend;
- backend configuration/FDS/API supplies `StartScript`;
- `BackendHandler` retains the `autoscalingbackend` type ID;
- the feature guide documents `.sh` as the normal script;
- scaling and shutdown hooks remain downstream of validation.

- [ ] **Step 5: Record the pre-change truth table**

Using the source at lines 91-92, statically record that ordinary dotted extensions do not match any undotted literal and therefore the rejection body is not entered for `.bat`, `.ps1`, `.sh`, or unrelated ordinary extensions.

Record separately that merely adding dots would invert the intended result because the existing branch rejects a true expression.

- [ ] **Step 6: Confirm protected scope**

Run:

```bash
git diff --name-only
git diff --cached --name-only
```

Do not edit or commit in Task 1. Return the baseline evidence for independent specification and quality review before Task 2 starts.

## Task 2: Correct the Platform Predicate

**Files:**

- Modify: `src/Backends/AutoScalingBackend.cs:91-97`

- [ ] **Step 1: Reconfirm the source and index immediately before editing**

Run:

```bash
test -z "$(git diff --cached --name-only)"
git diff e65550b0b90b62504f0495723db95567fe192673 -- \
  src/Backends/AutoScalingBackend.cs
nl -ba src/Backends/AutoScalingBackend.cs | sed -n '85,105p'
```

Expected: no source diff and the original ineffective guard remains.

- [ ] **Step 2: Replace the conditional with the approved positive predicate**

Use `apply_patch` to replace:

```csharp
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? (scriptExt == "bat" || scriptExt == "ps1") : (scriptExt == "sh"))
```

with:

```csharp
        bool isAppropriate = RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
            ? scriptExt == ".bat" || scriptExt == ".ps1"
            : scriptExt == ".sh";
        if (!isAppropriate)
```

Do not change the `scriptExt` derivation or rejection block body.

- [ ] **Step 3: Inspect the complete source diff**

Run:

```bash
git diff -- src/Backends/AutoScalingBackend.cs
git diff --unified=20 -- src/Backends/AutoScalingBackend.cs
git diff --check -- src/Backends/AutoScalingBackend.cs
git diff --numstat -- src/Backends/AutoScalingBackend.cs
nl -ba src/Backends/AutoScalingBackend.cs | sed -n '75,112p'
```

Expected:

- `4 insertions / 1 deletion`;
- one local explicit Boolean;
- dotted `.bat`, `.ps1`, and `.sh`;
- rejection is `if (!isAppropriate)`;
- every surrounding line is unchanged.

- [ ] **Step 4: Statically evaluate the final truth table**

Confirm from source:

| Final extension | Windows | Non-Windows |
|---|---:|---:|
| `.bat` / mixed case | accept | reject |
| `.ps1` / mixed case | accept | reject |
| `.sh` / mixed case | reject | accept |
| empty | reject | reject |
| `.` | reject | reject |
| unrelated | reject | reject |

Confirm `Path.GetExtension` uses only the final extension, so `worker.launch.sh` is accepted on non-Windows and `worker.sh.txt` is rejected.

- [ ] **Step 5: Prove the validation and lifecycle boundaries are unchanged**

Run:

```bash
rg -n \
  'MaxBackends <= 0|string\.IsNullOrWhiteSpace|MinBackends > Settings\.MaxBackends|scriptExt|isAppropriate|File\.Exists|BackendStatus\.(DISABLED|ERRORED|LOADING|RUNNING)|FillToMin|Program\.TickEvent|Program\.PreShutdownEvent|NewBackendNeededEvent' \
  src/Backends/AutoScalingBackend.cs

git diff e65550b0b90b62504f0495723db95567fe192673 -- \
  src/Backends/AutoScalingBackend.cs \
  | rg -n '^[+-].*(Logs\.|BackendStatus|File\.Exists|FillToMin|ProcessStartInfo|Process\.Start|TickEvent|PreShutdownEvent|NewBackendNeededEvent|StartScript|ConfigComment|public )' \
  || true
```

Expected:

- only the intended predicate lines appear in the diff search;
- no error/status/file/process/hook/public/settings line changes.

- [ ] **Step 6: Confirm C# and repository style**

Run:

```bash
git diff -- src/Backends/AutoScalingBackend.cs | rg -n '^\+.*\bvar\b' || true
git diff -- src/Backends/AutoScalingBackend.cs | rg -n '^\+.*if \([^)]*\)[^{]*$' || true
```

Confirm:

- explicit `bool`, never `var`;
- the conditional retains a full braced block;
- no public field or schema member is added.

- [ ] **Step 7: Commit only the source correction**

Run:

```bash
test -z "$(git diff --cached --name-only)"
git add -- src/Backends/AutoScalingBackend.cs
git diff --cached --name-only
git diff --cached --check
git diff --cached --stat
git diff --cached -- src/Backends/AutoScalingBackend.cs
git commit -m "fix: validate autoscaling launch script platform"
```

Expected committed scope: exactly `src/Backends/AutoScalingBackend.cs`.

- [ ] **Step 8: Verify commit and protected state**

Run:

```bash
git show --check --oneline --stat HEAD
git show --format= --name-only HEAD
git diff --cached --name-only
git status --short
git diff --numstat -- \
  src/Data/Settings.fds \
  src/Pages/Text2Image.cshtml \
  src/wwwroot/js/genpage/gentab/loras.js \
  src/wwwroot/js/genpage/main.js
```

Confirm:

- commit scope is one source file;
- index is empty;
- protected state remains exact and unstaged;
- no runtime action occurred.

## Task 3: Complete Integrated Static Source Verification

**Files:**

- Review: `src/Backends/AutoScalingBackend.cs`
- Review: source compatibility owners from Task 1

- [ ] **Step 1: Separate integrated history from source projection**

Run:

```bash
rank20_design_commit="$(git rev-parse 984930be)"
rank20_source_head="$(git rev-parse HEAD)"

git log --oneline --reverse "$rank20_design_commit..$rank20_source_head"
git log --oneline "$rank20_design_commit..$rank20_source_head" -- \
  src/Backends/AutoScalingBackend.cs
git diff --name-only "$rank20_design_commit..$rank20_source_head" -- \
  src/Backends/AutoScalingBackend.cs
git diff --numstat "$rank20_design_commit..$rank20_source_head" -- \
  src/Backends/AutoScalingBackend.cs
git diff --check "$rank20_design_commit..$rank20_source_head" -- \
  src/Backends/AutoScalingBackend.cs
```

Expected:

- integrated history contains one focused source commit after the design;
- source-path history contains only that source commit;
- source projection is exactly `src/Backends/AutoScalingBackend.cs`;
- source numstat is `4/1`;
- whitespace check is silent.

- [ ] **Step 2: Prove settings and public ABI preservation**

Run:

```bash
git diff 984930be..HEAD -- src/Backends/AutoScalingBackend.cs \
  | rg -n '^[+-].*(public |ConfigComment|StartScript|AutoScalingBackendSettings|autoscalingbackend)' \
  || true

git show 984930be:src/Backends/AutoScalingBackend.cs \
  | rg -n 'public string StartScript = "";|public override async Task Init\(\)|public async Task LaunchOne'

rg -n 'public string StartScript = "";|public override async Task Init\(\)|public async Task LaunchOne' \
  src/Backends/AutoScalingBackend.cs
```

Expected:

- no public/settings declaration diff;
- signatures and field line remain at both endpoints.

- [ ] **Step 3: Prove only guard behavior changed**

Run:

```bash
git diff --unified=50 984930be..HEAD -- src/Backends/AutoScalingBackend.cs
nl -ba src/Backends/AutoScalingBackend.cs | sed -n '75,112p;220,332p'
```

Confirm:

- disabled/settings ordering unchanged;
- extension normalization unchanged;
- dotted positive predicate and negated rejection are correct;
- error/status/return unchanged;
- file check remains after the guard;
- loading/min-fill/hooks/running unchanged;
- `LaunchOne` and process construction are unchanged.

- [ ] **Step 4: Prove compatibility surfaces remain unchanged**

Run:

```bash
git diff --name-only 984930be..HEAD -- \
  docs/Features/AutoScalingBackend.md \
  src/Backends/BackendHandler.cs \
  src/WebAPI/BackendAPI.cs \
  src/Backends/NetworkBackendUtils.cs \
  src/Backends/SwarmSwarmBackend.cs \
  src/Extensions \
  '**/*.bak'
```

Expected: no output.

- [ ] **Step 5: Obtain independent source specification approval**

Dispatch a fresh read-only reviewer. The reviewer verifies:

- exact Windows/non-Windows truth table;
- dotted extensions and corrected polarity;
- final-extension and case-insensitive behavior;
- validation order and unchanged error/status/file boundary;
- unchanged launch/lifecycle/settings/public contracts;
- exact one-file `4/1` projection;
- protected state.

Require exact token `SOURCE_SPEC_APPROVED` only with no Critical or Important findings.

- [ ] **Step 6: Obtain independent source quality approval**

After specification approval, dispatch a separate fresh read-only reviewer. The reviewer checks:

- local name and predicate readability;
- explicit type and braced style;
- no unnecessary helper/collection;
- cross-platform clarity;
- no accidental launch or lifecycle drift;
- minimal commit scope and clean state.

Require exact token `SOURCE_QUALITY_APPROVED` only with no Critical or Important findings. Send findings back to the same Task 2 implementer for focused correction, then repeat the affected review.

## Task 4: Record Static Closure and Review Documentation

**Files:**

- Modify: `docs/superpowers/specs/2026-07-27-autoscaling-launch-script-platform-validation-design.md`
- Modify: `docs/superpowers/audits/2026-07-21-maintainability-architecture-refresh.md`

- [ ] **Step 1: Record implementation provenance**

Use `apply_patch` after both source reviews approve. Record:

- approved source/audit base `e65550b0b90b62504f0495723db95567fe192673`;
- design commit `984930be` and subject;
- source commit hash and subject;
- exact one-file `4/1` source projection;
- `SOURCE_SPEC_APPROVED` and `SOURCE_QUALITY_APPROVED`;
- static-only evidence and prohibited-runtime boundary;
- exact truth table and validation order;
- preserved settings, public ABI, logs, statuses, launch, and lifecycle contracts;
- accepted-script launchability/interpreter/permission caveat;
- Windows runtime remains unvalidated unless evidence is supplied;
- status `Implemented; awaiting maintainer validation`;
- Rank 20 remains the sole Recommended Next Project until maintainer evidence is recorded.

Do not change the approved 20-case matrix.

- [ ] **Step 2: Update directly affected audit contexts**

Update current-state wording for:

- Backend F13;
- the `Autoscaling platform guard is ineffective` risk-register row;
- roadmap Rank 20;
- Recommended Next Project Rank 20;
- executive/current/conclusion summaries that still call Rank 20 undesigned or unimplemented.

Keep Rank 21 neither designed nor implemented and do not advance it before maintainer validation.

- [ ] **Step 3: Verify document invariants**

Run:

```bash
design='docs/superpowers/specs/2026-07-27-autoscaling-launch-script-platform-validation-design.md'
audit='docs/superpowers/audits/2026-07-21-maintainability-architecture-refresh.md'

rg -c '^## ' "$audit"
sed -n '/^## Ranked Refactoring Roadmap$/,/^## Recommended Next Project$/p' "$audit" \
  | rg -c '^### [0-9]+\.'
sed -n '/^## Maintainer Validation Matrix$/,/^## Success Criteria$/p' "$design" \
  | rg -c '^[0-9]+\.'
cmp \
  <(git show 984930be:"$design" | sed -n '/^## Maintainer Validation Matrix$/,/^## Success Criteria$/p') \
  <(sed -n '/^## Maintainer Validation Matrix$/,/^## Success Criteria$/p' "$design")
git diff --check -- "$design" "$audit"
```

Expected:

- audit retains 12 H2 sections;
- roadmap retains 32 sequential entries;
- design retains 20 matrix cases;
- matrix section is byte-identical to the design commit;
- whitespace check is silent.

- [ ] **Step 4: Commit static closure documentation**

Run:

```bash
test -z "$(git diff --cached --name-only)"
git add -- \
  docs/superpowers/specs/2026-07-27-autoscaling-launch-script-platform-validation-design.md \
  docs/superpowers/audits/2026-07-21-maintainability-architecture-refresh.md
git diff --cached --name-only
git diff --cached --check
git diff --cached
git commit -m "docs: record autoscaling launch-script validation"
```

Expected committed scope: exactly the two documentation files.

- [ ] **Step 5: Obtain documentation specification approval**

Dispatch a fresh read-only reviewer to verify:

- provenance and source projection;
- exact status and Rank 20 recommended-next state;
- truth table and preserved contracts;
- source-review tokens;
- static/runtime limitations;
- unchanged matrix and 12/32/20 invariants;
- absence of stale current-state claims.

Require exact token `DOCS_SPEC_APPROVED`.

- [ ] **Step 6: Obtain documentation quality approval**

After specification approval, dispatch a separate fresh read-only reviewer to check:

- historical versus current wording;
- consistency across design, Backend F13, risk register, roadmap, recommendation, and conclusions;
- clarity of accepted-versus-launchable caveat;
- no unsupported runtime/platform/performance claim;
- minimality and formatting.

Require exact token `DOCS_QUALITY_APPROVED`. Fix findings in focused documentation commits and repeat the affected review.

## Task 5: Hand Off Runtime Validation and Complete Rank 20

**Files:**

- Modify after maintainer evidence: `docs/superpowers/specs/2026-07-27-autoscaling-launch-script-platform-validation-design.md`
- Modify after maintainer evidence: `docs/superpowers/audits/2026-07-21-maintainability-architecture-refresh.md`

- [ ] **Step 1: Hand Reaper176 the exact unchanged matrix**

Provide all 20 cases from the design without rewriting them.

Request:

- result for each case;
- date;
- operating system;
- filesystem;
- shell/runtime details;
- explicit statement whether Windows cases 19-20 were run.

Do not infer Windows results from Linux results. If Windows is unavailable, record cases 19-20 as unvalidated rather than failed.

- [ ] **Step 2: Record maintainer evidence exactly**

After evidence is supplied, use `apply_patch` to record:

- the maintainer's raw message exactly;
- any explicitly disclosed normalization;
- normalized passed/failed/unrun case counts;
- exact environment and shell/runtime details without inference;
- Windows runtime status;
- exact matrix limitation;
- retained launchability/interpreter/permission and broader-platform caveats;
- agent static/runtime boundary.

- [ ] **Step 3: Advance roadmap status only after validation evidence**

If the supplied result satisfies the agreed non-Windows matrix:

- set Rank 20 to `Implemented and maintainer-validated` with the exact environment limitation;
- make Rank 21, **Use cached object-info safely after fresh-fetch failure**, the sole Recommended Next Project;
- state Rank 21 is neither designed nor implemented.

If any required available-platform case fails, keep Rank 20 open and record the actual result without advancing Rank 21.

- [ ] **Step 4: Verify validation-document invariants**

Run the same 12/32/20 and byte-identical matrix checks from Task 4. Also run:

```bash
rg -n \
  'Rank 20|Rank 21|Recommended Next Project|SOURCE_SPEC_APPROVED|SOURCE_QUALITY_APPROVED|DOCS_SPEC_APPROVED|DOCS_QUALITY_APPROVED|Windows runtime|raw message|normalization' \
  docs/superpowers/specs/2026-07-27-autoscaling-launch-script-platform-validation-design.md \
  docs/superpowers/audits/2026-07-21-maintainability-architecture-refresh.md
```

Confirm current statuses and limitations are consistent.

- [ ] **Step 5: Commit validation documentation**

Run:

```bash
test -z "$(git diff --cached --name-only)"
git add -- \
  docs/superpowers/specs/2026-07-27-autoscaling-launch-script-platform-validation-design.md \
  docs/superpowers/audits/2026-07-21-maintainability-architecture-refresh.md
git diff --cached --check
git diff --cached
git commit -m "docs: validate autoscaling launch-script platform"
```

Expected committed scope: exactly the two documentation files.

- [ ] **Step 6: Obtain validation-document approvals**

Sequentially require fresh reviewers to return:

- `VALIDATION_SPEC_APPROVED`;
- `VALIDATION_QUALITY_APPROVED`.

Send findings to the Task 5 documentation implementer, commit focused corrections, and repeat the affected review until no findings remain.

- [ ] **Step 7: Record validation-review provenance**

Use `apply_patch` to record:

- `DOCS_SPEC_APPROVED`;
- `DOCS_QUALITY_APPROVED`;
- `VALIDATION_SPEC_APPROVED`;
- `VALIDATION_QUALITY_APPROVED`;
- any initial findings and focused correction commits;
- no remaining validation findings.

Do not record `FINAL_INTEGRATED_APPROVED` before it is returned.

Commit only the two documentation files:

```bash
git commit -m "docs: record autoscaling validation reviews"
```

- [ ] **Step 8: Obtain final integrated approval**

Dispatch a fresh read-only reviewer over the complete design-to-head history. Require exact token `FINAL_INTEGRATED_APPROVED` only when the reviewer confirms:

- coherent commit history and scopes;
- exact one-file `4/1` production projection;
- complete truth table and unchanged launch/lifecycle contracts;
- every source/document/validation approval;
- exact maintainer evidence and platform limitations;
- 12/32/20 invariants and byte-identical matrix;
- correct Rank 20 and Rank 21 statuses;
- clean whitespace and empty index;
- protected state unchanged and unstaged.

- [ ] **Step 9: Run final controller static verification**

Run:

```bash
git branch --show-current
git rev-parse HEAD
git log --oneline --reverse 984930be..HEAD
git diff --check 984930be..HEAD
git log --oneline 984930be..HEAD -- src/Backends/AutoScalingBackend.cs
git diff --name-only 984930be..HEAD -- src/Backends/AutoScalingBackend.cs
git diff --numstat 984930be..HEAD -- src/Backends/AutoScalingBackend.cs
git diff --cached --name-only
git status --short
git diff --numstat -- \
  src/Data/Settings.fds \
  src/Pages/Text2Image.cshtml \
  src/wwwroot/js/genpage/gentab/loras.js \
  src/wwwroot/js/genpage/main.js
```

Confirm every approval, exact source projection, maintainer result, document invariant, empty index, and protected state before claiming Rank 20 complete.
