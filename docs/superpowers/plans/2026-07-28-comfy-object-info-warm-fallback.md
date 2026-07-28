# Comfy Object-Info Warm Fallback Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make cached Comfy object-info survive a later first-backend selection, fetch, parse, or null-result failure without mutating the previously published snapshot or masking a first-ever failure.

**Architecture:** Keep the correction inside `ComfyUIRedirectHelper.ObjectInfoReadCacher`. Move first-backend selection into the existing failure boundary, treat a null fresh result as an explicit failure, and deep-clone one captured `LastObjectInfo` reference on warm failure before running the unchanged missing-only backend union and publication flow.

**Tech Stack:** C# 12, .NET 8, Newtonsoft.Json `JObject`, FreneticUtilities `SingleValueExpiringCacheAsync<T>`, Git, Markdown.

---

## Execution Constraints

- Work directly on `master`; maintainer Reaper176 previously approved this repository's direct-master rank workflow.
- User/maintainer identity is Reaper176, who is listed in `AGENTS.md`.
- Agents never build, launch, test, execute scripts, start SwarmUI or ComfyUI, start backends or services, automate a browser, call live APIs, or run test-executing lint in this repository.
- Runtime validation belongs exclusively to Reaper176.
- The repository's no-agent-test rule overrides the generic TDD and test-command guidance in the writing-plans skill.
- Use `apply_patch` for every file edit.
- Make the minimum possible source change.
- Do not create a worktree. The maintainer selected direct-master execution, and protected changes are isolated through exact staging.
- Do not touch `Data/`, `Output/`, `Models/`, `src/bin`, `src/obj`, `.vs`, `.git`, downloaded/upstream code, generated API documentation, or `src/Extensions`.
- Preserve the unrelated protected working changes and keep them unstaged:
  - `src/Data/Settings.fds`: `83 insertions / 3 deletions`;
  - `src/Pages/Text2Image.cshtml`: `9 insertions / 2 deletions`;
  - `src/wwwroot/js/genpage/gentab/loras.js`: `2 insertions / 0 deletions`;
  - `src/wwwroot/js/genpage/main.js`: `4 insertions / 5 deletions`;
  - untracked `Data.pre-restore-2026-07-19/`.
- The approved source/audit base is `0a814fc86942a15dcdfa3062834a6c7d9ef95718`.
- The approved design is `9369ef993583fc5e8bcc34b18c1a73de4d99e0f8` (`docs: design Comfy object-info warm fallback`).
- The approved-base and design source blob for `ComfyUIRedirectHelper.cs` is `8e3bdacc850e657e3f155591b6730ea9213ae9bf`.

## File Map

- Modify source: `src/BuiltinExtensions/ComfyUIBackend/ComfyUIRedirectHelper.cs`
  - Existing owner of `LastObjectInfo`, `ObjectInfoReadCacher`, cached object-info route handling, first-backend fetch, backend union, and publication.
- Modify closure design: `docs/superpowers/specs/2026-07-28-comfy-object-info-warm-fallback-design.md`
  - Records implementation provenance, static review, and later maintainer evidence.
- Modify architecture audit: `docs/superpowers/audits/2026-07-21-maintainability-architecture-refresh.md`
  - Updates Comfy F24, the risk register, roadmap Rank 21, current summaries, and recommended-next state.
- Review-only external manual-expiry callers:
  - `src/BuiltinExtensions/ComfyUIBackend/ComfyUIBackendExtension.cs`;
  - `src/BuiltinExtensions/ComfyUIBackend/ComfyUIWebAPI.cs`.
- Review-only backend-local object-info owner:
  - `src/BuiltinExtensions/ComfyUIBackend/ComfyUIAPIAbstractBackend.cs`.
- Review-only cache contract:
  - FreneticUtilities `SingleValueExpiringCacheAsync<JObject>` XML documentation in the installed package.

## Task 1: Pin the Baseline and Complete the Cache Inventory

**Files:**

- Review: `src/BuiltinExtensions/ComfyUIBackend/ComfyUIRedirectHelper.cs`
- Review: `src/BuiltinExtensions/ComfyUIBackend/ComfyUIBackendExtension.cs`
- Review: `src/BuiltinExtensions/ComfyUIBackend/ComfyUIWebAPI.cs`
- Review: `src/BuiltinExtensions/ComfyUIBackend/ComfyUIAPIAbstractBackend.cs`
- Review: `docs/superpowers/specs/2026-07-28-comfy-object-info-warm-fallback-design.md`
- Review: `docs/superpowers/audits/2026-07-21-maintainability-architecture-refresh.md`

- [ ] **Step 1: Confirm branch, design, index, and protected state**

Run:

```bash
git branch --show-current
git rev-parse HEAD
git merge-base --is-ancestor 0a814fc86942a15dcdfa3062834a6c7d9ef95718 HEAD
git merge-base --is-ancestor 9369ef993583fc5e8bcc34b18c1a73de4d99e0f8 HEAD
git log --oneline --reverse 0a814fc8..HEAD
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
- approved base and design are ancestors of HEAD;
- only the design, this plan, and any focused planning correction follow the approved source/audit base before Task 2;
- empty index;
- only the four protected tracked files and backup directory appear outside committed Rank 21 work;
- protected numstats remain `83/3`, `9/2`, `2/0`, and `4/5`.

- [ ] **Step 2: Pin the production source to the approved base**

Run:

```bash
git diff 0a814fc86942a15dcdfa3062834a6c7d9ef95718..HEAD -- \
  src/BuiltinExtensions/ComfyUIBackend/ComfyUIRedirectHelper.cs
git rev-parse 0a814fc86942a15dcdfa3062834a6c7d9ef95718:src/BuiltinExtensions/ComfyUIBackend/ComfyUIRedirectHelper.cs
git rev-parse 9369ef993583fc5e8bcc34b18c1a73de4d99e0f8:src/BuiltinExtensions/ComfyUIBackend/ComfyUIRedirectHelper.cs
git rev-parse HEAD:src/BuiltinExtensions/ComfyUIBackend/ComfyUIRedirectHelper.cs
```

Expected:

- no source diff since the approved base;
- each blob is `8e3bdacc850e657e3f155591b6730ea9213ae9bf`.

- [ ] **Step 3: Record the exact pre-change factory**

Run:

```bash
nl -ba src/BuiltinExtensions/ComfyUIBackend/ComfyUIRedirectHelper.cs \
  | sed -n '28,76p'
```

Record:

1. public volatile `LastObjectInfo`;
2. public `ObjectInfoReadCacher`;
3. first-backend `.First()` before `try`;
4. nullable `result`;
5. synchronous one-minute GET/read/parse;
6. existing error log;
7. cold-only rethrow check;
8. missing-only union over current direct backends;
9. `result.ContainsKey` and assignment while `result` may be null;
10. post-union `LastObjectInfo` publication;
11. return; and
12. ten-minute expiration.

- [ ] **Step 4: Inventory every maintained cache and backup consumer**

Run:

```bash
rg -n \
  'ObjectInfoReadCacher|LastObjectInfo|ComfyBackendsDirect|RawObjectInfo|object_info|api/object_info|ForceExpire' \
  src/BuiltinExtensions/ComfyUIBackend/ComfyUIRedirectHelper.cs \
  src/BuiltinExtensions/ComfyUIBackend/ComfyUIBackendExtension.cs \
  src/BuiltinExtensions/ComfyUIBackend/ComfyUIWebAPI.cs \
  src/BuiltinExtensions/ComfyUIBackend/ComfyUIAPIAbstractBackend.cs
```

Confirm:

- the factory is the only maintained `LastObjectInfo` writer;
- the cached route is the only maintained `ObjectInfoReadCacher.GetValue()` caller;
- three maintained `ForceExpire()` call sites exist: the two external callers in `ComfyUIBackendExtension.Refresh` and `ComfyUIWebAPI.ComfyEnsureRefreshable`, plus the internal null-data safeguard in `ComfyBackendDirectHandler`;
- the four cached route forms remain in one conditional;
- `RawObjectInfo` is backend-local input to the union and has separate publication ownership;
- capability snapshots do not consume `LastObjectInfo`;
- no maintained source outside the listed files owns this fallback.

- [ ] **Step 5: Pin the cache-wrapper contract**

Read the installed FreneticUtilities XML documentation for `SingleValueExpiringCacheAsync<T>` and record:

- calculation is async-safe;
- calculation runs only once per expiration;
- the value is retained until expiry;
- `ForceExpire()` makes the value immediately expired; and
- the wrapper does not itself establish the Rank 21 fallback semantics.

Do not edit the package or infer behavior beyond its documentation.

- [ ] **Step 6: Record the pre-change failure table**

Statically record:

| Factory condition | Prior `LastObjectInfo` | Approved-base result |
|---|---|---|
| Fresh fetch and parse succeeds | null or non-null | fresh object enters union and publishes |
| First-backend selection fails | null or non-null | throws before existing catch |
| Fetch or parse throws | null | logs and rethrows |
| Fetch or parse throws | non-null | logs and leaves `result` null; faults if the union reaches any property, otherwise skips publication and returns the prior object |
| Fresh parse yields null | non-null | leaves `result` null without entering the catch; faults if the union reaches any property, otherwise skips publication and returns the prior object |
| Fresh parse yields null | null | leaves `result` null without entering the catch; faults if the union reaches any property, otherwise returns null, after which the handler expires the cache and can fault at `data.ToString()` |
| Unexpired cache hit | any | wrapper returns retained value without calculation |

- [ ] **Step 7: Confirm protected scope**

Run:

```bash
git diff --name-only
git diff --cached --name-only
```

Do not edit or commit in Task 1. Return the complete evidence for independent specification and quality review before Task 2 starts.

## Task 2: Implement the Warm Fallback

**Files:**

- Modify: `src/BuiltinExtensions/ComfyUIBackend/ComfyUIRedirectHelper.cs:38-73`

- [ ] **Step 1: Reconfirm source and index immediately before editing**

Run:

```bash
test -z "$(git diff --cached --name-only)"
git diff 0a814fc86942a15dcdfa3062834a6c7d9ef95718 -- \
  src/BuiltinExtensions/ComfyUIBackend/ComfyUIRedirectHelper.cs
git rev-parse HEAD:src/BuiltinExtensions/ComfyUIBackend/ComfyUIRedirectHelper.cs
nl -ba src/BuiltinExtensions/ComfyUIBackend/ComfyUIRedirectHelper.cs \
  | sed -n '31,74p'
```

Expected: no source diff and blob `8e3bdacc850e657e3f155591b6730ea9213ae9bf`.

- [ ] **Step 2: Apply the exact minimal source correction**

Use `apply_patch` so the factory begins:

```csharp
    public static SingleValueExpiringCacheAsync<JObject> ObjectInfoReadCacher = new(() =>
    {
        JObject result = null;
        try
        {
            ComfyUIBackendExtension.ComfyBackendData backend = ComfyUIBackendExtension.ComfyBackendsDirect().First();
            using CancellationTokenSource cancel = Utilities.TimedCancel(TimeSpan.FromMinutes(1));
            result = backend.Client.GetAsync($"{backend.APIAddress}/object_info", cancel.Token).Result.Content.ReadAsStringAsync().Result.ParseToJson();
            if (result is null)
            {
                throw new InvalidOperationException("Comfy object_info read returned null.");
            }
        }
        catch (Exception ex)
        {
            Logs.Error($"object_info read failure: {ex.ReadableString()}");
            JObject priorObjectInfo = LastObjectInfo;
            if (priorObjectInfo is null)
            {
                throw;
            }
            result = (JObject)priorObjectInfo.DeepClone();
        }
```

Leave the existing union, publication, return, and ten-minute constructor argument textually unchanged.

- [ ] **Step 3: Inspect the complete source diff**

Run:

```bash
git diff -- src/BuiltinExtensions/ComfyUIBackend/ComfyUIRedirectHelper.cs
git diff --unified=30 -- \
  src/BuiltinExtensions/ComfyUIBackend/ComfyUIRedirectHelper.cs
git diff --check -- \
  src/BuiltinExtensions/ComfyUIBackend/ComfyUIRedirectHelper.cs
git diff --numstat -- \
  src/BuiltinExtensions/ComfyUIBackend/ComfyUIRedirectHelper.cs
nl -ba src/BuiltinExtensions/ComfyUIBackend/ComfyUIRedirectHelper.cs \
  | sed -n '31,82p'
```

Expected:

- `8 insertions / 2 deletions`;
- first-backend selection moved inside `try`;
- explicit null-result failure;
- one local capture of `LastObjectInfo`;
- cold rethrow;
- warm `DeepClone`;
- every line from the union through expiration unchanged.

- [ ] **Step 4: Statically evaluate the final failure table**

Confirm:

| Factory condition | Prior snapshot | Final behavior |
|---|---|---|
| Fresh non-null result | any | uses fresh private result, unions, publishes |
| Selection/fetch/parse/null-result failure | null | logs and rethrows active failure |
| Selection/fetch/parse/null-result failure | non-null | logs, deep-clones prior, unions, publishes |
| Union failure | any | propagates under existing boundary; no partial publication |
| Unexpired cache hit | any | unchanged wrapper returns retained value |

- [ ] **Step 5: Prove snapshot and union ownership**

Run:

```bash
rg -n \
  'LastObjectInfo|priorObjectInfo|DeepClone|result|ContainsKey|RawObjectInfo' \
  src/BuiltinExtensions/ComfyUIBackend/ComfyUIRedirectHelper.cs

git diff 0a814fc86942a15dcdfa3062834a6c7d9ef95718 -- \
  src/BuiltinExtensions/ComfyUIBackend/ComfyUIRedirectHelper.cs \
  | rg -n '^[+-].*(foreach|ContainsKey|property\\.Value|LastObjectInfo = result|return LastObjectInfo|TimeSpan\\.FromMinutes\\(10\\))' \
  || true
```

Expected:

- the prior public snapshot is only read into `priorObjectInfo`;
- `DeepClone()` produces the fallback mutation target;
- union writes only to `result`;
- no union, publication, return, or expiration line changed.

- [ ] **Step 6: Prove compatibility surfaces are unchanged**

Run:

```bash
git diff 0a814fc86942a15dcdfa3062834a6c7d9ef95718 -- \
  src/BuiltinExtensions/ComfyUIBackend/ComfyUIRedirectHelper.cs \
  | rg -n '^[+-].*(public |ObjectInfoReadCacher =|LastObjectInfo;|path ==|StatusCode|ContentType|X-Swarm-Backend-ID|DoBackendDataCache)' \
  || true

git diff 0a814fc86942a15dcdfa3062834a6c7d9ef95718 -- \
  src/BuiltinExtensions/ComfyUIBackend/ComfyUIBackendExtension.cs \
  src/BuiltinExtensions/ComfyUIBackend/ComfyUIWebAPI.cs \
  src/BuiltinExtensions/ComfyUIBackend/ComfyUIAPIAbstractBackend.cs
```

Expected:

- no public declaration, route, response, header, cache-setting, expiry caller, or backend-local owner change;
- no diff in the three review-only source files.

- [ ] **Step 7: Confirm C# and repository style**

Run:

```bash
git diff -- src/BuiltinExtensions/ComfyUIBackend/ComfyUIRedirectHelper.cs \
  | rg -n '^\+.*\bvar\b' \
  || true

git diff -- src/BuiltinExtensions/ComfyUIBackend/ComfyUIRedirectHelper.cs \
  | rg -n '^\+.*if \([^)]*\)[^{]*$' \
  || true
```

Confirm:

- explicit `JObject` and backend-data types, never `var`;
- full braced blocks;
- no new field, method, helper, schema, or setting;
- existing public fields retain XML documentation.

- [ ] **Step 8: Commit the source change**

Run:

```bash
test -z "$(git diff --cached --name-only)"
git add -- \
  src/BuiltinExtensions/ComfyUIBackend/ComfyUIRedirectHelper.cs
git diff --cached --check
git diff --cached
git commit -m "fix: preserve Comfy object-info warm fallback"
```

Expected committed scope: exactly `ComfyUIRedirectHelper.cs`.

Do not build or run tests. The maintainer matrix remains the runtime authority.

## Task 3: Complete Independent Source Review

**Files:**

- Review: `src/BuiltinExtensions/ComfyUIBackend/ComfyUIRedirectHelper.cs`
- Review: `src/BuiltinExtensions/ComfyUIBackend/ComfyUIBackendExtension.cs`
- Review: `src/BuiltinExtensions/ComfyUIBackend/ComfyUIWebAPI.cs`
- Review: `src/BuiltinExtensions/ComfyUIBackend/ComfyUIAPIAbstractBackend.cs`
- Review: `docs/superpowers/specs/2026-07-28-comfy-object-info-warm-fallback-design.md`

- [ ] **Step 1: Pin the integrated source history**

Run:

```bash
git log --oneline --reverse 9369ef99..HEAD
git log --oneline 9369ef99..HEAD -- \
  src/BuiltinExtensions/ComfyUIBackend/ComfyUIRedirectHelper.cs
git diff --name-only 0a814fc86942a15dcdfa3062834a6c7d9ef95718..HEAD -- '*.cs'
git diff --numstat 0a814fc86942a15dcdfa3062834a6c7d9ef95718..HEAD -- '*.cs'
git diff --check 9369ef99..HEAD
```

Expected:

- source-path history contains only `fix: preserve Comfy object-info warm fallback`;
- C# projection contains only `ComfyUIRedirectHelper.cs`;
- exact source numstat is `8/2`;
- whitespace check is clean.

- [ ] **Step 2: Trace every final control-flow branch**

Read the complete factory and record:

1. cold successful fetch;
2. cold selection failure;
3. cold HTTP/read failure;
4. cold parse failure;
5. cold explicit null result;
6. warm equivalents of cases 2-5;
7. fresh missing-only union;
8. fallback missing-only union;
9. post-union publication;
10. union exception before publication; and
11. unexpired cache hit under the wrapper contract.

- [ ] **Step 3: Prove downstream contracts**

Confirm from unchanged source:

- `ComfyBackendDirectHandler` still rejects missing users/permissions and missing backends before route proxying;
- `X-Swarm-Backend-ID` filtering remains unchanged;
- backend-data cache selection remains unchanged;
- cached response remains HTTP 200 JSON with the existing `StringContent`;
- cache-disabled requests still use the selected backend's direct GET;
- all three `ForceExpire` call sites are unchanged: the two external manual-expiry callers and the handler's internal null-data safeguard;
- backend-local `RawObjectInfo`, node types, model lists, feature snapshots, and publication remain unchanged;
- no workflow, generation, validation, model, or UI contract changed.

- [ ] **Step 4: Obtain source specification approval**

Dispatch a fresh read-only reviewer. Require exact token `SOURCE_SPEC_APPROVED` only when the reviewer confirms:

- exact design compliance;
- all cold/warm/null/zero-backend branches;
- deep clone before fallback union;
- no mutation of the prior published object;
- missing-only union and first-backend precedence;
- first-ever failure remains a failure;
- source projection exactly one file at `8/2`;
- public fields and downstream contracts unchanged;
- no unsupported runtime or performance claim; and
- protected state and index remain isolated.

Send findings to the same Task 2 implementer for focused correction and repeat this review until no findings remain.

- [ ] **Step 5: Obtain source quality approval**

After specification approval, dispatch a separate fresh read-only reviewer. Require exact token `SOURCE_QUALITY_APPROVED` only with no Critical or Important findings.

The reviewer checks:

- minimality;
- clarity of the captured prior and clone ownership;
- exception-boundary correctness;
- explicit null handling;
- C# style;
- no hidden mutation or publication-before-completion risk;
- no accidental broad catch expansion beyond the approved selection/fetch/null boundary;
- no ABI or schema drift; and
- clean static evidence.

Send findings to the same Task 2 implementer for focused correction and repeat the affected review.

## Task 4: Record Static Closure

**Files:**

- Modify: `docs/superpowers/specs/2026-07-28-comfy-object-info-warm-fallback-design.md`
- Modify: `docs/superpowers/audits/2026-07-21-maintainability-architecture-refresh.md`

- [ ] **Step 1: Update the design implementation record**

Use `apply_patch` after both source reviews approve. Record:

- approved source/audit base;
- design and plan commits;
- production source/head commit;
- exact one-file `8/2` source projection;
- `SOURCE_SPEC_APPROVED`;
- `SOURCE_QUALITY_APPROVED`;
- no remaining source findings;
- final cold/warm/null/zero-backend flow;
- preserved snapshot, union, cache, route, response, public, backend-local, and lifecycle contracts;
- agents performed static review only;
- status `Implemented; awaiting maintainer validation`; and
- the unchanged 20-case matrix remains runtime authority.

- [ ] **Step 2: Update Comfy F24 and roadmap Rank 21**

Use `apply_patch` in the audit to update:

- current architecture's broken-fallback statement;
- Comfy F24;
- the risk-register row;
- roadmap Rank 21;
- Recommended Next Project Rank 21;
- executive/current/conclusion summaries that still call Rank 21 undesigned or unimplemented.

Keep Rank 22, **Invalidate model metadata cache when supported sidecars change**, neither designed nor implemented. Do not advance Rank 22 before maintainer validation.

- [ ] **Step 3: Preserve document invariants**

Run:

```bash
audit=docs/superpowers/audits/2026-07-21-maintainability-architecture-refresh.md
design=docs/superpowers/specs/2026-07-28-comfy-object-info-warm-fallback-design.md

rg -c '^## ' "$audit"
sed -n '/^## Ranked Refactoring Roadmap$/,/^## Recommended Next Project$/p' "$audit" \
  | sed -n 's/^### \([0-9][0-9]*\)\..*/\1/p' \
  | paste -sd, -
sed -n '/^## Maintainer Validation Matrix$/,/^## Success Criteria$/p' "$design" \
  | rg -c '^[0-9]+\.'
diff -u \
  <(git show 9369ef99:"$design" | sed -n '/^## Maintainer Validation Matrix$/,/^## Success Criteria$/p') \
  <(sed -n '/^## Maintainer Validation Matrix$/,/^## Success Criteria$/p' "$design")
```

Expected:

- audit H2 count `12`;
- roadmap sequence `1` through `32`;
- design matrix count `20`;
- byte-identical matrix.

- [ ] **Step 4: Verify documentation consistency**

Run:

```bash
rg -n \
  'Rank 21|Rank 22|Recommended Next Project|Comfy F24|SOURCE_SPEC_APPROVED|SOURCE_QUALITY_APPROVED|8/2|warm fallback|first-ever|Windows|performance' \
  docs/superpowers/specs/2026-07-28-comfy-object-info-warm-fallback-design.md \
  docs/superpowers/audits/2026-07-21-maintainability-architecture-refresh.md

git diff --check -- \
  docs/superpowers/specs/2026-07-28-comfy-object-info-warm-fallback-design.md \
  docs/superpowers/audits/2026-07-21-maintainability-architecture-refresh.md
```

Confirm:

- Rank 21 is implemented awaiting validation and remains sole recommended-next;
- Rank 22 remains neither designed nor implemented;
- no runtime/platform/filesystem/browser/performance result is claimed;
- the design and audit agree on scope, source projection, statuses, and caveats.

- [ ] **Step 5: Commit static closure documentation**

Run:

```bash
test -z "$(git diff --cached --name-only)"
git add -- \
  docs/superpowers/specs/2026-07-28-comfy-object-info-warm-fallback-design.md \
  docs/superpowers/audits/2026-07-21-maintainability-architecture-refresh.md
git diff --cached --check
git diff --cached
git commit -m "docs: record Comfy object-info warm fallback"
```

Expected committed scope: exactly the two documentation files.

- [ ] **Step 6: Obtain documentation approvals**

Sequentially dispatch fresh read-only reviewers and require:

- `DOCS_SPEC_APPROVED`;
- `DOCS_QUALITY_APPROVED`.

Reviewers verify exact provenance, source projection, cold/warm behavior, snapshot/union/cache contracts, unchanged matrix, Rank 21 status, Rank 22 non-advancement, 12/32/20 invariants, platform/performance caveats, minimality, and protected state.

Send findings to the Task 4 documentation implementer, commit focused corrections, and repeat the affected review until no findings remain.

## Task 5: Hand Off Runtime Validation and Complete Rank 21

**Files:**

- Modify after maintainer evidence: `docs/superpowers/specs/2026-07-28-comfy-object-info-warm-fallback-design.md`
- Modify after maintainer evidence: `docs/superpowers/audits/2026-07-21-maintainability-architecture-refresh.md`

- [ ] **Step 1: Hand Reaper176 the exact unchanged matrix**

Provide all 20 cases from the design without rewriting them.

Request:

- result for each case;
- date;
- operating system;
- filesystem;
- browser and version if known;
- Comfy version if known;
- number/type of direct and linked backends;
- explicit statement about any cases that could not be exercised.

Do not infer results for an unavailable topology, failure injection, browser, platform, filesystem, or Comfy version. Record unavailable cases as unrun, not failed.

- [ ] **Step 2: Record maintainer evidence exactly**

After evidence is supplied, use `apply_patch` to record:

- the maintainer's raw message exactly;
- any explicitly disclosed normalization;
- normalized passed/failed/unrun counts;
- exact environment, browser, Comfy, and backend arrangement details without inference;
- exact matrix limitation;
- retained stale-data, public-external-mutation, wider-platform/topology/concurrency, and performance caveats;
- the agent static/runtime boundary.

- [ ] **Step 3: Advance roadmap status only after validation evidence**

If all available required cases pass:

- set Rank 21 to `Implemented and maintainer-validated` with the exact environment and topology limitation;
- make Rank 22, **Invalidate model metadata cache when supported sidecars change**, the sole Recommended Next Project;
- state Rank 22 is neither designed nor implemented.

If any required available case fails, keep Rank 21 open and record the actual result without advancing Rank 22.

Unrun cases remain unvalidated and are not failures. Do not broaden the status beyond the supplied evidence.

- [ ] **Step 4: Verify validation-document invariants**

Run the same 12/32/20 and byte-identical matrix checks from Task 4. Also run:

```bash
rg -n \
  'Rank 21|Rank 22|Recommended Next Project|SOURCE_SPEC_APPROVED|SOURCE_QUALITY_APPROVED|DOCS_SPEC_APPROVED|DOCS_QUALITY_APPROVED|raw message|normalization|unrun|unvalidated' \
  docs/superpowers/specs/2026-07-28-comfy-object-info-warm-fallback-design.md \
  docs/superpowers/audits/2026-07-21-maintainability-architecture-refresh.md
```

Confirm current statuses, counts, environment, topology, and limitations are consistent.

- [ ] **Step 5: Commit validation documentation**

Run:

```bash
test -z "$(git diff --cached --name-only)"
git add -- \
  docs/superpowers/specs/2026-07-28-comfy-object-info-warm-fallback-design.md \
  docs/superpowers/audits/2026-07-21-maintainability-architecture-refresh.md
git diff --cached --check
git diff --cached
git commit -m "docs: validate Comfy object-info warm fallback"
```

Expected committed scope: exactly the two documentation files.

- [ ] **Step 6: Obtain validation-document approvals**

Sequentially require fresh reviewers to return:

- `VALIDATION_SPEC_APPROVED`;
- `VALIDATION_QUALITY_APPROVED`.

Send findings to the Task 5 documentation implementer, commit focused corrections, and repeat the affected review until no findings remain.

- [ ] **Step 7: Record review provenance**

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
test -z "$(git diff --cached --name-only)"
git add -- \
  docs/superpowers/specs/2026-07-28-comfy-object-info-warm-fallback-design.md \
  docs/superpowers/audits/2026-07-21-maintainability-architecture-refresh.md
git diff --cached --check
git diff --cached
git commit -m "docs: record Comfy object-info validation reviews"
```

- [ ] **Step 8: Obtain and record final integrated approval**

Dispatch a fresh read-only reviewer over the complete design-to-head history. Require exact token `FINAL_INTEGRATED_APPROVED` only when the reviewer confirms:

- coherent commit history and scopes;
- exact one-file `8/2` production projection;
- complete cold/warm/null/zero-backend behavior;
- deep-clone ownership and unchanged missing-only union;
- unchanged cache, expiry, route, response, permission, header, public-field, backend-local, workflow, generation, and validation contracts;
- every source/document/validation approval;
- exact maintainer evidence and environment/topology limitations;
- 12/32/20 invariants and byte-identical matrix;
- correct Rank 21 and Rank 22 statuses;
- clean whitespace and empty index;
- protected state unchanged and unstaged.

After the token is returned, use `apply_patch` to replace only Rank 21's pending-final-review statements with `FINAL_INTEGRATED_APPROVED`, then run:

```bash
test -z "$(git diff --cached --name-only)"
git add -- \
  docs/superpowers/specs/2026-07-28-comfy-object-info-warm-fallback-design.md \
  docs/superpowers/audits/2026-07-21-maintainability-architecture-refresh.md
git diff --cached --check
git diff --cached
git commit -m "docs: record Comfy object-info final review"
```

Have the same final reviewer re-review that focused tail commit.

- [ ] **Step 9: Run final controller static verification**

Run:

```bash
git branch --show-current
git rev-parse HEAD
git log --oneline --reverse 9369ef99..HEAD
git diff --check 9369ef99..HEAD
git log --oneline 9369ef99..HEAD -- \
  src/BuiltinExtensions/ComfyUIBackend/ComfyUIRedirectHelper.cs
git diff --name-only 0a814fc86942a15dcdfa3062834a6c7d9ef95718..HEAD -- '*.cs'
git diff --numstat 0a814fc86942a15dcdfa3062834a6c7d9ef95718..HEAD -- '*.cs'
git diff --cached --name-only
git status --short
git diff --numstat -- \
  src/Data/Settings.fds \
  src/Pages/Text2Image.cshtml \
  src/wwwroot/js/genpage/gentab/loras.js \
  src/wwwroot/js/genpage/main.js
```

Also confirm:

- all seven approval tokens appear in both closure documents;
- raw evidence and disclosed normalization are exact;
- matrix and audit invariants remain exact;
- Rank 21 is closed only within the recorded validation boundary;
- Rank 22 is the sole recommended next project only if the available matrix passed;
- no stale Rank 21 awaiting/recommended-next statement remains;
- index is empty; and
- protected work remains exact.

Only after fresh evidence passes may the controller mark Rank 21 complete.
