# WebSocket Lifecycle Handler Session-Renewal Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Preserve caller-supplied WebSocket error and open handlers through invalid-session renewal without changing any other transport, callback, socket-ownership, or caller behavior.

**Architecture:** Modify only the existing invalid-session recursive call in `src/wwwroot/js/site.js::makeWSRequest` so it forwards `errorHandle` and `onOpenHandle` after the incremented depth. Keep the public signature, initial socket return, mutable request object, send/open ordering, streaming callback, depth gate, session acquisition, and ignored recursive return value unchanged.

**Tech Stack:** Browser JavaScript, classic-script globals, native `WebSocket`, existing SwarmUI session renewal and request helpers.

---

## Repository Constraints

- Work directly on `master`; do not create or use a worktree.
- Reaper176 is an approved maintainer under `AGENTS.md`.
- Agents must not build, launch, test, automate a browser, start a server/backend, call a live API, or run test-executing lint. The maintainer performs every browser/runtime validation case.
- Do not add automated tests. Repository policy states that automated tests are not used and agents cannot run any form of testing.
- Agents may run source searches, committed-source inspection, exact-range diff inspection, `git diff --check`, and Git scope/index checks.
- Never edit generated `docs/APIRoutes`, downloaded upstream code, external extensions, backup files, build output, user data, or launchers.
- Preserve the existing uncommitted maintainer files:
  - `src/Data/Settings.fds`;
  - `src/Pages/Text2Image.cshtml`;
  - `src/wwwroot/js/genpage/gentab/loras.js`;
  - `src/wwwroot/js/genpage/main.js`; and
  - `Data.pre-restore-2026-07-19/`.
- The Rank 18 approved source/audit base is `9080e620c76e6101adb7af2544eacc5ab43460c1`.
- The Rank 18 design authority is commit `646552d4` and `docs/superpowers/specs/2026-07-27-websocket-lifecycle-handler-session-renewal-design.md`.
- Focused documentation commit `887a4323` corrects only the old recursive call's argument count from five to four; it does not change the approved behavior or production boundary.
- The design commit changes only the specification, so its committed `site.js` is identical to the approved source base.
- `src/wwwroot/js/site.js` is clean at the design boundary. If it becomes dirty before implementation, stop and inspect the exact diff. Do not combine unrelated changes by assumption.
- Use `apply_patch` for every file edit.
- Stage explicit paths only. Confirm the index is empty before and after each commit.
- Keep production scope to this exact replacement:

```js
makeWSRequest(url, in_data, callback, depth + 1);
```

becomes:

```js
makeWSRequest(url, in_data, callback, depth + 1, errorHandle, onOpenHandle);
```

- Do not change the `makeWSRequest` declaration, positional defaults, local `fail`, WebSocket construction, `onopen`, `onmessage`, `onerror`, return statement, or surrounding invalid-session branch.
- Preserve `onOpenHandle` as a per-physical-socket callback. It runs for the initially opened socket and every retry socket that opens.
- Preserve send-before-open-handler timing.
- Preserve the same mutable `in_data` object and overwrite its `session_id` on every socket open.
- Preserve the exact `depth > 3` comparison and `depth + 1` increment.
- Preserve `getSession` placement and behavior.
- Preserve the ignored recursive return value. Do not return or publish the retry socket to the original caller.
- Do not explicitly close the rejected socket or redesign socket ownership.
- Do not change `makeWSRequestT2I`, `genericRequest`, any caller, server route, payload, permission, persistence behavior, or public ABI.
- Do not add handler-exception isolation, new diagnostics, retry state, context objects, options objects, classes, helpers, cancellation protocols, telemetry, benchmarks, or performance claims.

## File Map

- Modify `src/wwwroot/js/site.js`
  - forward `errorHandle` and `onOpenHandle` through the invalid-session recursive call;
  - make no other production change.
- Review but do not modify:
  - `src/wwwroot/js/genpage/gentab/generatecontrols.js`;
  - `src/wwwroot/js/genpage/helpers/generatehandler.js`;
  - `src/wwwroot/js/genpage/gentab/models.js`;
  - `src/wwwroot/js/genpage/utiltab.js`;
  - `src/wwwroot/js/installer.js`;
  - maintained `makeWSRequestT2I` callers under `src/wwwroot/js` and `src/BuiltinExtensions`;
  - `src/wwwroot/js/site.js.bak`; and
  - external extension paths.
- Modify after source review:
  - `docs/superpowers/specs/2026-07-27-websocket-lifecycle-handler-session-renewal-design.md`; and
  - `docs/superpowers/audits/2026-07-21-maintainability-architecture-refresh.md`.

## Task 1: Forward Lifecycle Handlers Through Session-Renewal Recursion

**Files:**

- Modify: `src/wwwroot/js/site.js:105-150`

- [ ] **Step 1: Reconfirm the approved committed and working boundaries**

Run:

```bash
git rev-parse HEAD
git branch --show-current
git status --short
git diff --cached --name-only

git diff 646552d4 -- src/wwwroot/js/site.js
git show 646552d4:src/wwwroot/js/site.js | nl -ba | sed -n '95,155p'
git diff --unified=0 -- src/wwwroot/js/site.js
```

Expected:

- branch is `master`;
- HEAD contains the committed Rank 18 design and may contain only a later documentation plan before the production edit;
- the index is empty;
- committed and working `site.js` match the design boundary;
- line 105 retains the six-argument declaration;
- line 137 retains the four-argument recursive call;
- the protected tracked paths remain unstaged; and
- the backup directory remains untracked.

If `site.js` has an unrelated working diff or the committed branch no longer matches the design semantics, stop and report the exact divergence before editing.

- [ ] **Step 2: Reconfirm maintained transport and caller ownership**

Run:

```bash
git grep -n -E 'makeWSRequest\(' 646552d4 \
  -- 'src/**' \
  ':(exclude)src/Extensions/**' \
  ':(exclude)**/*.bak'

git grep -n -E 'makeWSRequestT2I\(' 646552d4 \
  -- 'src/**' \
  ':(exclude)src/Extensions/**' \
  ':(exclude)**/*.bak'

git grep -n -E 'errorHandle|onOpenHandle' 646552d4 \
  -- src/wwwroot/js/site.js \
  src/wwwroot/js/genpage/gentab/generatecontrols.js

git show 646552d4:src/wwwroot/js/genpage/gentab/models.js | nl -ba | sed -n '1658,1690p'
git show 646552d4:src/wwwroot/js/genpage/utiltab.js | nl -ba | sed -n '145,195p;832,880p'
git show 646552d4:src/wwwroot/js/installer.js | nl -ba | sed -n '166,206p'
git show 646552d4:src/wwwroot/js/genpage/helpers/generatehandler.js | nl -ba | sed -n '544,596p'
```

Confirm:

- five maintained non-backup direct callers exist outside `site.js`;
- the one direct recursive call is the only handler-loss site;
- `makeWSRequestT2I` forwards its optional error handler;
- `GenerateHandler` owns generation cleanup and preview retry;
- TensorRT owns button/result restoration;
- LoRA extraction owns progress/error restoration;
- model download owns error/retry UI and the only maintained open handler;
- installer owns confirmation-button restoration; and
- external classic-script callers remain an unenumerated compatibility surface.

- [ ] **Step 3: Apply the exact production change**

Use `apply_patch` to replace only:

```js
makeWSRequest(url, in_data, callback, depth + 1);
```

with:

```js
makeWSRequest(url, in_data, callback, depth + 1, errorHandle, onOpenHandle);
```

Do not reformat the line, branch, function, or file.

- [ ] **Step 4: Inspect the complete working source diff**

Run:

```bash
git diff -- src/wwwroot/js/site.js
git diff --numstat -- src/wwwroot/js/site.js
git diff --unified=0 -- src/wwwroot/js/site.js
git diff --check -- src/wwwroot/js/site.js
git status --short
```

Expected:

- exactly one source file;
- exactly `1 insertion(+), 1 deletion(-)`;
- exactly one hunk;
- the deleted line is the four-argument recursion;
- the added line is the six-argument call carrying both handlers;
- whitespace check is silent; and
- protected paths remain unstaged and byte-preserved.

- [ ] **Step 5: Run focused static behavior checks**

Run:

```bash
git diff 646552d4 -- src/wwwroot/js/site.js

git show 646552d4:src/wwwroot/js/site.js | nl -ba | sed -n '102,152p'
nl -ba src/wwwroot/js/site.js | sed -n '102,152p'

rg -n -F \
  'makeWSRequest(url, in_data, callback, depth + 1, errorHandle, onOpenHandle);' \
  src/wwwroot/js/site.js

rg -n -F \
  'makeWSRequest(url, in_data, callback, depth + 1);' \
  src/wwwroot/js/site.js \
  || true

git diff 646552d4 -- src/wwwroot/js/site.js \
  | rg -n '^[+-].*(function makeWSRequest|depth > 3|getSession|new WebSocket|socket\.send|onOpenHandle\(socket\)|callback\(data\)|socket\.onerror|return socket|socket\.close|structuredClone|Object\.assign)'
```

Expected:

- the new recursive call appears exactly once;
- the old recursive call is absent from the working source;
- the only added/deleted behavior line is recursion argument propagation;
- function signature/defaults are unchanged;
- `depth > 3`, `getSession`, and `depth + 1` remain in the same branch;
- open handling still writes the current session, sends, then calls the open handler;
- data callback and error paths remain unchanged;
- no close, clone, context, or retry return propagation is added.

- [ ] **Step 6: Stage and commit only `site.js`**

Run:

```bash
test -z "$(git diff --cached --name-only)"

git add -- src/wwwroot/js/site.js
git diff --cached --name-only
git diff --cached --numstat
git diff --cached --check
git diff --cached -- src/wwwroot/js/site.js

git commit -m "fix: preserve websocket retry lifecycle handlers"
```

Expected staged and committed scope: exactly `src/wwwroot/js/site.js`, one insertion and one deletion.

- [ ] **Step 7: Verify the production commit and protected state**

Run:

```bash
rank18_source_commit="$(git rev-parse HEAD)"

git show --check --oneline --stat "$rank18_source_commit"
git show --format= --name-only "$rank18_source_commit"
git show --format= --numstat "$rank18_source_commit"
git show --format= --unified=12 "$rank18_source_commit" -- src/wwwroot/js/site.js

git diff --cached --name-only
git status --short
git diff --numstat -- \
  src/Data/Settings.fds \
  src/Pages/Text2Image.cshtml \
  src/wwwroot/js/genpage/gentab/loras.js \
  src/wwwroot/js/genpage/main.js
```

Confirm:

- the production commit contains exactly `site.js`;
- it has one insertion and one deletion in one hunk;
- the index is empty;
- all protected tracked diffs retain their pre-edit numstat;
- the backup remains untracked; and
- no agent runtime action occurred.

## Task 2: Complete Independent Static Source Review

**Files:**

- Review only: `src/wwwroot/js/site.js`
- Review only: maintained callers listed in the file map

- [ ] **Step 1: Pin integrated and source-only histories**

Run:

```bash
rank18_design_commit="$(git rev-parse 646552d4)"
rank18_integrated_head="$(git rev-parse HEAD)"
rank18_source_head="$(git log -1 --format=%H -- src/wwwroot/js/site.js)"

printf 'design=%s\nintegrated=%s\nsource=%s\n' \
  "$rank18_design_commit" \
  "$rank18_integrated_head" \
  "$rank18_source_head"

git log --oneline "$rank18_design_commit..$rank18_integrated_head"
git log --oneline "$rank18_design_commit..$rank18_source_head" -- src/wwwroot/js/site.js
git diff --name-only "$rank18_design_commit..$rank18_source_head" -- src/wwwroot/js/site.js
git diff --numstat "$rank18_design_commit..$rank18_source_head" -- src/wwwroot/js/site.js
git diff --check "$rank18_design_commit..$rank18_source_head" -- src/wwwroot/js/site.js
```

Expected:

- integrated history contains the plan and focused source commit;
- the `site.js` path-filtered history contains only the production commit;
- production projection contains exactly `site.js`;
- production projection is `1 insertion(+), 1 deletion(-)`; and
- whitespace check is silent.

- [ ] **Step 2: Compare exact source and negative boundaries**

Run:

```bash
git diff --unified=20 "$rank18_design_commit..$rank18_source_head" -- src/wwwroot/js/site.js

git diff --unified=0 "$rank18_design_commit..$rank18_source_head" -- src/wwwroot/js/site.js \
  | rg -c '^@@'

git diff "$rank18_design_commit..$rank18_source_head" -- src/wwwroot/js/site.js \
  | rg -n '^\+.*(socket\.close|return makeWSRequest|structuredClone|Object\.assign|class |function |let .*Handler|window\.|Promise|async|await)' \
  || true

git diff --name-only "$rank18_design_commit..$rank18_source_head" -- \
  src/wwwroot/js/genpage/gentab/generatecontrols.js \
  src/wwwroot/js/genpage/helpers/generatehandler.js \
  src/wwwroot/js/genpage/gentab/models.js \
  src/wwwroot/js/genpage/utiltab.js \
  src/wwwroot/js/installer.js \
  src/BuiltinExtensions \
  src/wwwroot/js/site.js.bak
```

Expected:

- exactly one semantic hunk;
- only the two lifecycle arguments are added;
- no close, recursive return, clone, new owner, global, promise, or async behavior;
- all callers, built-in extensions, and backup files are unchanged.

- [ ] **Step 3: Re-run the complete maintained caller inventory**

Run:

```bash
git grep -n -E 'makeWSRequest\(' "$rank18_source_head" \
  -- 'src/**' \
  ':(exclude)src/Extensions/**' \
  ':(exclude)**/*.bak'

git grep -n -E 'makeWSRequestT2I\(' "$rank18_source_head" \
  -- 'src/**' \
  ':(exclude)src/Extensions/**' \
  ':(exclude)**/*.bak'

git grep -n -F 'onOpenHandle' "$rank18_source_head" \
  -- src/wwwroot/js/site.js

git grep -n -F 'socket => {' "$rank18_source_head" \
  -- src/wwwroot/js/genpage/utiltab.js
```

Confirm the inventories match the design and no maintained caller migration occurred.

- [ ] **Step 4: Obtain source specification approval**

Require a fresh independent reviewer to return `SOURCE_SPEC_APPROVED` only after verifying:

- approved base and design authority;
- exact one-file/one-hunk/one-line production boundary;
- both lifecycle handlers cross only the invalid-session recursive call;
- per-physical-socket open-handler semantics;
- retry failure ownership through the original error handler;
- model-download cancellation rebinding through the retry socket;
- unchanged function signature, defaults, and external direct-call contract;
- unchanged initial returned socket and ignored recursive return;
- unchanged mutable request identity and session overwrite;
- unchanged send-before-open-handler timing;
- unchanged depth comparison/increment and `getSession` placement;
- unchanged streaming callback, application-error conversion, socket error text, and generic handler-free behavior;
- unchanged `makeWSRequestT2I`, generation tracking, TensorRT, LoRA, model-download UI, installer, callers, built-in extensions, server APIs, persistence, and ABI;
- no socket close, handler catch, request clone, context, helper, new state, telemetry, benchmark, or performance claim;
- protected work absent from the production commit and still unstaged;
- whitespace and index checks clean; and
- no agent runtime claim.

If the reviewer finds a source defect, correct only that defect in a focused source commit and repeat specification review.

- [ ] **Step 5: Obtain source quality approval**

After specification approval, require a fresh reviewer to return `SOURCE_QUALITY_APPROVED` only after verifying:

- the one-line change is the smallest clear fix;
- argument order exactly matches the existing function declaration;
- no caller-specific transport logic is introduced;
- no comment or helper is needed to explain the direct propagation;
- repository JavaScript style is unchanged;
- the diff contains no formatting churn or unrelated source;
- external compatibility and deferred socket ownership are clear; and
- implementation is statically ready for maintainer validation.

Correct concrete quality findings in focused commits. Repeat specification review if behavior changes, then repeat quality review.

## Task 3: Record Static Implementation Closure

**Files:**

- Modify: `docs/superpowers/specs/2026-07-27-websocket-lifecycle-handler-session-renewal-design.md`
- Modify: `docs/superpowers/audits/2026-07-21-maintainability-architecture-refresh.md`

- [ ] **Step 1: Update the design implementation status and record**

Change the design status to:

```text
Implemented; awaiting maintainer validation
```

Add an `## Implementation Record` immediately before `## Maintainer Validation Matrix`. Record:

- approved source/audit base `9080e620c76e6101adb7af2544eacc5ab43460c1`;
- design commit `646552d4`;
- design terminology correction `887a4323`;
- the committed plan SHA and message;
- the final production commit SHA and message;
- integrated history versus `site.js` source projection;
- exact one-file, one-hunk, one-insertion/one-deletion source scope;
- exact old and new recursive calls;
- `SOURCE_SPEC_APPROVED` and `SOURCE_QUALITY_APPROVED`;
- complete maintained caller and lifecycle-owner inventory;
- preserved signature, depth, session, request, send/open, streaming, failure, return, socket-ownership, caller, extension, server, persistence, and ABI boundaries;
- deferred explicit close and retry-return ownership;
- static-only agent evidence;
- protected working-tree preservation; and
- no browser, runtime, platform, filesystem, or performance claim.

Do not change the approved 14-case matrix.

- [ ] **Step 2: Update Frontend F1 and Rank 18 audit status**

In `docs/superpowers/audits/2026-07-21-maintainability-architecture-refresh.md`:

- mark Frontend F1 and Rank 18 `Implemented; awaiting maintainer validation`;
- preserve the historical defect wording as historical;
- record the exact base/design/plan/source provenance and one-line source projection;
- record handler propagation and every preserved compatibility boundary;
- record both source review tokens;
- keep Rank 18 as the sole Recommended Next Project until maintainer validation passes;
- state Rank 19 remains neither designed nor implemented;
- leave ranks 1, 2, and 8 awaiting their existing validation;
- preserve exactly 12 top-level audit sections and 32 sequential roadmap entries; and
- do not claim runtime or performance evidence.

- [ ] **Step 3: Inspect documentation-only closure**

Run:

```bash
git diff --check -- \
  docs/superpowers/specs/2026-07-27-websocket-lifecycle-handler-session-renewal-design.md \
  docs/superpowers/audits/2026-07-21-maintainability-architecture-refresh.md

rg -n '^## ' docs/superpowers/audits/2026-07-21-maintainability-architecture-refresh.md

awk '
  /^## Ranked Refactoring Roadmap$/ { on = 1; next }
  /^## Recommended Next Project$/ { on = 0 }
  on && /^### [0-9]+\./ {
      split($2, rank, ".");
      count++;
      if (rank[1] != count) {
          bad = 1;
      }
  }
  END {
      printf "roadmap_count=%d\n", count;
      exit bad || count != 32;
  }
' docs/superpowers/audits/2026-07-21-maintainability-architecture-refresh.md

awk '
  /^## Maintainer Validation Matrix$/ { on = 1; next }
  /^## / { if (on) exit }
  on && /^[0-9]+\./ { count++ }
  END { printf "matrix_cases=%d\n", count; exit count != 14 }
' docs/superpowers/specs/2026-07-27-websocket-lifecycle-handler-session-renewal-design.md

git status --short
git diff --cached --name-only
```

Expected:

- whitespace check is silent;
- audit has 12 H2 sections;
- roadmap has 32 sequential entries;
- validation matrix has 14 cases;
- only the two intended docs are modified by the closure;
- index is empty before staging; and
- protected files remain unchanged and unstaged.

- [ ] **Step 4: Commit documentation closure only**

Run:

```bash
git add -- \
  docs/superpowers/specs/2026-07-27-websocket-lifecycle-handler-session-renewal-design.md \
  docs/superpowers/audits/2026-07-21-maintainability-architecture-refresh.md

git diff --cached --name-only
git diff --cached --check
git commit -m "docs: record websocket lifecycle handler renewal"
```

Expected staged and committed scope: exactly the two documentation files.

- [ ] **Step 5: Obtain documentation approvals**

Require fresh independent reviewers to return `DOCS_SPEC_APPROVED` and `DOCS_QUALITY_APPROVED`.

They must verify:

- exact provenance, source projection, review tokens, and static evidence;
- accurate historical/current behavior;
- accurate per-physical-socket open semantics and retry error ownership;
- complete preserved and deferred boundaries;
- no runtime, cross-browser, platform, filesystem, or performance overclaim;
- Rank 18 implemented-awaiting-validation and still Recommended Next;
- Rank 19 neither designed nor implemented;
- ranks 1, 2, and 8 unchanged;
- 12/32/14 invariants;
- byte-identical approved matrix;
- exact two-document closure scope;
- clean whitespace and index; and
- protected dirty state preserved.

Fix concrete documentation findings in focused documentation commits and repeat both reviews.

## Task 4: Hand Off and Record Maintainer Validation

**Files:**

- Modify after explicit maintainer results:
  - `docs/superpowers/specs/2026-07-27-websocket-lifecycle-handler-session-renewal-design.md`
  - `docs/superpowers/audits/2026-07-21-maintainability-architecture-refresh.md`

- [ ] **Step 1: Hand the exact matrix to Reaper176**

Provide the 14 numbered cases verbatim from the design's `## Maintainer Validation Matrix`. Ask the maintainer to report:

- pass/fail for all 14;
- browser and version when available;
- operating system;
- filesystem;
- exact failure details for any failed case; and
- confirmation that ordinary generation and page behavior remain functional.

Do not summarize Rank 18 as validated before explicit maintainer confirmation.

- [ ] **Step 2: Record only the supplied runtime evidence**

After explicit results:

- preserve the exact matrix unchanged;
- add a separate maintainer validation record outside the numbered matrix;
- record maintainer Reaper176 and the confirmation date;
- record the exact pass/fail outcome;
- record browser/version, operating system, and filesystem exactly as supplied;
- explicitly say when a browser version was not provided rather than inferring one;
- separate maintainer browser/runtime evidence from static agent evidence;
- retain the per-physical-socket callback, external-extension, get-session-failure, explicit-close, retry-return, generation-tracking, other-browser/platform/filesystem, and no-performance caveats.

If any case fails, keep Rank 18 awaiting validation, diagnose statically, and do not claim completion.

- [ ] **Step 3: Advance roadmap only after all 14 cases pass**

After all cases pass:

- mark the design, Frontend F1, and every current Rank 18 status `Implemented and maintainer-validated` on the recorded environment;
- remove Rank 18 from Recommended Next while retaining its roadmap position;
- make Rank 19, `Bound and atomically consume OAuth registration challenges`, the sole Recommended Next Project;
- state Rank 19 remains neither designed nor implemented;
- leave ranks 1, 2, and 8 awaiting their existing validation;
- retain exactly 12 audit H2 sections, 32 sequential roadmap entries, and the byte-identical 14-case matrix; and
- make no broader browser, platform, filesystem, external-extension, or performance claim.

- [ ] **Step 4: Commit validation documentation only**

Run:

```bash
git add -- \
  docs/superpowers/specs/2026-07-27-websocket-lifecycle-handler-session-renewal-design.md \
  docs/superpowers/audits/2026-07-21-maintainability-architecture-refresh.md

git diff --cached --name-only
git diff --cached --check
git commit -m "docs: validate websocket lifecycle handler renewal"
```

Expected scope: exactly the two documentation files.

- [ ] **Step 5: Obtain validation-document approvals**

Require fresh reviewers to return `VALIDATION_SPEC_APPROVED` and `VALIDATION_QUALITY_APPROVED` after verifying:

- exact maintainer evidence and environment;
- no inferred browser version or expanded result;
- static/runtime separation;
- all caveats retained;
- every Rank 18 current status validated;
- Rank 19 is the sole recommended next project and remains undesigned/unimplemented;
- ranks 1, 2, and 8 remain pending;
- 12/32/14 invariants and matrix identity;
- exact documentation scope;
- whitespace and index cleanliness; and
- protected state preservation.

Correct findings in focused documentation commits and repeat both reviews.

## Task 5: Run Final Integrated Static Verification

**Files:**

- Review only: complete Rank 18 design-to-validation range

- [ ] **Step 1: Obtain final integrated approval**

Require a fresh independent reviewer to inspect the complete range from `646552d4` through final HEAD and return `FINAL_INTEGRATED_APPROVED` only with zero Critical, Important, or Minor findings.

The review must cover:

- plan, source, closure, validation, correction, and review-record commits;
- exact one-commit `site.js` source projection;
- exact one-file/one-hunk/one-insertion/one-deletion production scope;
- complete approved behavior and preserved boundaries;
- all six source/docs/validation review tokens;
- exact maintainer result and environment;
- 12/32/14 invariants and matrix identity;
- Rank 19 next-project state;
- ranks 1, 2, and 8 pending state;
- clean fixed-range whitespace and empty index; and
- protected worktree preservation.

- [ ] **Step 2: Run fresh controller verification**

Run:

```bash
rank18_design_commit="$(git rev-parse 646552d4)"
rank18_integrated_head="$(git rev-parse HEAD)"
rank18_source_head="$(git log -1 --format=%H -- src/wwwroot/js/site.js)"

git branch --show-current
git log --oneline "$rank18_design_commit..$rank18_integrated_head"
git diff --name-status "$rank18_design_commit..$rank18_integrated_head"
git diff --check "$rank18_design_commit..$rank18_integrated_head"

git log --oneline "$rank18_design_commit..$rank18_source_head" -- src/wwwroot/js/site.js
git diff --name-only "$rank18_design_commit..$rank18_source_head" -- src/wwwroot/js/site.js
git diff --numstat "$rank18_design_commit..$rank18_source_head" -- src/wwwroot/js/site.js
git diff --unified=0 "$rank18_design_commit..$rank18_source_head" -- src/wwwroot/js/site.js
git diff --check "$rank18_design_commit..$rank18_source_head" -- src/wwwroot/js/site.js

git show "$rank18_source_head":src/wwwroot/js/site.js | nl -ba | sed -n '102,152p'

rg -n \
  'SOURCE_SPEC_APPROVED|SOURCE_QUALITY_APPROVED|DOCS_SPEC_APPROVED|DOCS_QUALITY_APPROVED|VALIDATION_SPEC_APPROVED|VALIDATION_QUALITY_APPROVED' \
  docs/superpowers/specs/2026-07-27-websocket-lifecycle-handler-session-renewal-design.md \
  docs/superpowers/audits/2026-07-21-maintainability-architecture-refresh.md

git diff --cached --name-only
git status --short
git diff --numstat -- \
  src/Data/Settings.fds \
  src/Pages/Text2Image.cshtml \
  src/wwwroot/js/genpage/gentab/loras.js \
  src/wwwroot/js/genpage/main.js
```

Confirm:

- branch remains `master`;
- only intended Rank 18 commits appear in the integrated range;
- source-path history contains only the focused production commit;
- source projection is exactly one file, one hunk, one insertion, and one deletion;
- recursive call carries both lifecycle handlers in declaration order;
- every preserved branch remains textually unchanged;
- all review tokens are accurately recorded;
- documentation invariants and maintainer evidence are exact;
- index is empty;
- protected files remain unstaged with their original diffs;
- backup remains untracked; and
- no agent build, test, lint, browser, launch, server/backend, live API, runtime, platform, filesystem, or performance result is claimed.

## Execution Boundary

The implementation workflow ends only after:

1. the one-line production change is committed;
2. source specification and quality reviews approve it;
3. static closure documentation and its reviews are committed;
4. Reaper176 reports the exact 14-case maintainer result;
5. validation documentation and reviews are committed;
6. final integrated review returns `FINAL_INTEGRATED_APPROVED`;
7. fresh controller static verification succeeds; and
8. protected maintainer work remains untouched and unstaged.
