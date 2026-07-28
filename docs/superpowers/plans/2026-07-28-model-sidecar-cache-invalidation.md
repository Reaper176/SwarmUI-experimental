# Model Sidecar Cache Invalidation Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make model metadata cache reuse depend on the ordinary filesystem freshness of all four supported JSON sidecars, without changing metadata parsing, merge order, writers, consumers, or the cache database owner.

**Architecture:** Add one optional ordered sidecar fingerprint to `T2IModelHandler.ModelMetadataStore`. Compute a stable existence/length/UTC-last-write fingerprint before each cache decision, require it to match for reuse, assign the same captured value only to the newly constructed `LoadMetadata` record, attempt persistence through the existing caught upsert, and update the in-memory record before `ResetMetadataFrom` attempts its upsert.

**Tech Stack:** C# 12, .NET 8, LiteDB, `FileInfo`, FreneticUtilities string helpers, Git/static source inspection, maintainer-run SwarmUI validation.

---

## Repository Policy and Fixed Boundaries

- Approved maintainer: Reaper176.
- Approved source/audit base: `d2508564975c5ca149048e29f57e428dde6d96f2`.
- Approved design commit: `2048a2bb67e9c8da31233f729695e5d7469683fb`.
- Approved design: `docs/superpowers/specs/2026-07-28-model-sidecar-cache-invalidation-design.md`.
- Baseline `src/Text2Image/T2IModelHandler.cs` blob: `db15d8567c212885816df384ce859bd383f9e0df`.
- Production scope: only `src/Text2Image/T2IModelHandler.cs`.
- Documentation scope: the approved design and `docs/superpowers/audits/2026-07-21-maintainability-architecture-refresh.md`.
- Do not change `T2IModel.cs`, `ModelsAPI.cs`, `T2IAPI.cs`, `Program.cs`, Comfy workflow code, settings, APIs, or database files.
- Do not combine Core P4's duplicate sidecar read/parse optimization.
- Do not add preview-image freshness, content hashing, a watcher, a retry loop, or a new lock.
- Agents must not build, test, launch, execute scripts, start services/backends, automate browsers, call live APIs, or perform runtime/platform/filesystem exercises. The maintainer runs the exact validation matrix.
- Preserve these unrelated unstaged changes exactly:
  - `src/Data/Settings.fds` — `83 insertions / 3 deletions`
  - `src/Pages/Text2Image.cshtml` — `9 insertions / 2 deletions`
  - `src/wwwroot/js/genpage/gentab/loras.js` — `2 insertions / 0 deletions`
  - `src/wwwroot/js/genpage/main.js` — `4 insertions / 5 deletions`
  - untracked `Data.pre-restore-2026-07-19/`

### Task 1: Reconfirm the fixed source boundary and defect

**Files:**
- Inspect: `AGENTS.md`
- Inspect: `docs/project-memory.md`
- Inspect: `docs/superpowers/specs/2026-07-28-model-sidecar-cache-invalidation-design.md`
- Inspect: `docs/superpowers/audits/2026-07-21-maintainability-architecture-refresh.md`
- Inspect: `src/Text2Image/T2IModelHandler.cs`
- Inspect: `src/Text2Image/T2IModel.cs`
- Inspect: `src/WebAPI/ModelsAPI.cs`
- Inspect: `src/WebAPI/T2IAPI.cs`
- Inspect: `src/Core/Program.cs`

- [ ] **Step 1: Verify the approved commits and unchanged production blob**

Run:

```bash
git merge-base --is-ancestor d2508564975c5ca149048e29f57e428dde6d96f2 HEAD
git merge-base --is-ancestor 2048a2bb67e9c8da31233f729695e5d7469683fb HEAD
git hash-object src/Text2Image/T2IModelHandler.cs
git diff --exit-code d2508564975c5ca149048e29f57e428dde6d96f2 HEAD -- src/Text2Image/T2IModelHandler.cs
```

Expected: every command exits `0`; the blob is exactly `db15d8567c212885816df384ce859bd383f9e0df`; the source diff is empty.

- [ ] **Step 2: Re-read repository instructions and the approved design**

Read `AGENTS.md`, `docs/project-memory.md`, and the complete approved design. Confirm the agent prohibition on builds/tests/runtime actions and that no relevant `.agents/skills/` entry exists before proceeding.

- [ ] **Step 3: Trace the baseline reuse decision**

Inspect `T2IModelHandler.LoadMetadata` and record:

- central/per-folder database selection and record ID;
- `FindById`;
- legacy variable-`TextEncoders` invalidation;
- the current `metadata is null || metadata.ModelFileVersion != modified` recomputation predicate;
- both ordered sidecar loops;
- record construction/upsert; and
- final publication into `T2IModel`.

Expected: a reusable model-file timestamp bypasses every supported sidecar read, proving the approved add/edit/delete defect.

- [ ] **Step 4: Inventory cache reads, writes, and reset callers**

Run:

```bash
rg -n 'ModelMetadataStore|Metadata\.Upsert|FindById|ResetMetadataFrom|LoadMetadata\(' src/Text2Image/T2IModelHandler.cs src/Text2Image/T2IModel.cs src/WebAPI/ModelsAPI.cs src/BuiltinExtensions/ComfyUIBackend/ComfyUIWebAPI.cs
```

Expected maintained inventory:

- two `Metadata.Upsert` sites in `T2IModelHandler.cs`;
- one `FindById`;
- one `LoadMetadata` discovery call;
- two `ModelsAPI` reset callers; and
- two `T2IModel` reset callers.

- [ ] **Step 5: Inventory suffix readers and maintained writers**

Run:

```bash
rg -n 'AltModelMetadataJsonFileSuffixes|AllModelAttachedExtensions|\.swarm\.json|\.cm-info\.json|\.civitai\.info' src/Text2Image/T2IModelHandler.cs src/Text2Image/T2IModel.cs src/WebAPI/ModelsAPI.cs src/BuiltinExtensions/ComfyUIBackend/ComfyUIWebAPI.cs
```

Expected: the four-suffix declaration and two read loops are in `T2IModelHandler.cs`; application sidecar writing/attachment movement remains in existing callers and is not a production edit target.

- [ ] **Step 6: Inventory refresh entry points and protected state**

Run:

```bash
rg -n 'ModelRefreshEvent|RefreshModelSet|TriggerRefresh|BuildModelLists|\.Refresh\(\)' src/Core/Program.cs src/WebAPI/T2IAPI.cs src/WebAPI/ModelsAPI.cs src/Text2Image/T2IModelHandler.cs
git status --short
git diff --numstat -- src/Data/Settings.fds src/Pages/Text2Image.cshtml src/wwwroot/js/genpage/gentab/loras.js src/wwwroot/js/genpage/main.js
```

Expected: refresh ownership matches the design; the four protected numstats and untracked backup directory exactly match the fixed boundary.

- [ ] **Step 7: Report the baseline trace before editing**

The worker reports the cache predicate, four suffixes, two upserts, reset callers, refresh entry points, source blob, and protected state. Do not commit anything in this task.

### Task 2: Implement the ordered sidecar fingerprint

**Files:**
- Modify: `src/Text2Image/T2IModelHandler.cs:100-155`
- Modify: `src/Text2Image/T2IModelHandler.cs:402-449`
- Modify: `src/Text2Image/T2IModelHandler.cs:483-807`

- [ ] **Step 1: Add the optional LiteDB property**

Immediately after `ModelFileVersion`, add:

```csharp
        /// <summary>Ordered filesystem fingerprint of the supported model metadata sidecars.</summary>
        public string ModelSidecarFingerprint { get; set; }
```

Do not rename, remove, or change the type of any existing property.

- [ ] **Step 2: Add the focused fingerprint helper**

Immediately after `AltModelMetadataJsonFileSuffixes`, add:

```csharp
    /// <summary>Builds a stable ordered filesystem fingerprint for all supported model metadata sidecars.</summary>
    private static string GetModelSidecarFingerprint(string altModelPrefix)
    {
        List<string> entries = [];
        foreach (string altSuffix in AltModelMetadataJsonFileSuffixes)
        {
            FileInfo sidecar = new($"{altModelPrefix}{altSuffix}");
            if (!sidecar.Exists)
            {
                entries.Add($"{altSuffix}:missing");
            }
            else
            {
                entries.Add(FormattableString.Invariant($"{altSuffix}:{sidecar.Length}:{sidecar.LastWriteTimeUtc.Ticks}"));
            }
        }
        return entries.JoinString("|");
    }
```

The helper must remain private, ordered, culture-independent, non-hashing, and limited to the four JSON suffixes.

- [ ] **Step 3: Capture the fingerprint before `ResetMetadataFrom` mutates the record**

After deriving `folder` and `fileName`, and before retrieving/updating the cache record, add:

```csharp
            string altModelPrefix = $"{model.OriginatingFolderPath}/{model.Name.BeforeLast('.')}";
            string sidecarFingerprint = GetModelSidecarFingerprint(altModelPrefix);
```

Inside `lock (ModificationLock)`, after assigning `ModelFileVersion`, add:

```csharp
                metadata.ModelSidecarFingerprint = sidecarFingerprint;
```

The capture textually precedes `ResetMetadataFrom`'s own `ModificationLock` and `MetadataLock` statements. Do not add or reorder locks. Maintained `GetOrGenerateTensorHashSha256` and `ResaveModel` callers may already hold the existing reentrant `ModificationLock`, so do not claim the inspection is universally outside that lock.

- [ ] **Step 4: Capture the fingerprint once before the `LoadMetadata` cache decision**

After deriving `folder`, `fileName`, and `modified`, add:

```csharp
        string altModelPrefix = $"{model.OriginatingFolderPath}/{model.Name.BeforeLast('.')}";
        string sidecarFingerprint = GetModelSidecarFingerprint(altModelPrefix);
```

Remove the later duplicate declaration:

```csharp
            string altModelPrefix = $"{model.OriginatingFolderPath}/{model.Name.BeforeLast('.')}";
```

All existing sidecar reads must continue using the single earlier `altModelPrefix`.

- [ ] **Step 5: Extend only the existing recomputation predicate**

Replace:

```csharp
        if (metadata is null || metadata.ModelFileVersion != modified)
```

with:

```csharp
        if (metadata is null || metadata.ModelFileVersion != modified || metadata.ModelSidecarFingerprint != sidecarFingerprint)
```

A legacy null property therefore recomputes without a separate migration branch.

- [ ] **Step 6: Assign the captured fingerprint only in the newly constructed record**

In the `metadata = new()` initializer, immediately after `ModelFileVersion = modified`, add:

```csharp
                ModelSidecarFingerprint = sidecarFingerprint,
```

Do not assign the new fingerprint to the previously loaded record before recomputation. A read/parse/conversion failure before record construction must not attach or publish a new fingerprint. The existing LiteDB upsert catch remains after construction: if that upsert fails, the persistent old record remains, but existing control flow still publishes the newly constructed metadata and fingerprint to `model.Metadata`; a later refresh reloads and reevaluates persistent state. Do not claim successful upsert is a prerequisite for in-memory publication.

- [ ] **Step 7: Inspect the focused source diff**

Run:

```bash
git diff -- src/Text2Image/T2IModelHandler.cs
rg -n 'ModelSidecarFingerprint|GetModelSidecarFingerprint|altModelPrefix|sidecarFingerprint' src/Text2Image/T2IModelHandler.cs
git diff --check -- src/Text2Image/T2IModelHandler.cs
```

Expected:

- one optional property;
- one private helper;
- one fingerprint capture and assignment in `ResetMetadataFrom`;
- one fingerprint capture, predicate comparison, and new-record assignment in `LoadMetadata`;
- the existing two sidecar parse loops remain present; and
- whitespace check exits `0`.

- [ ] **Step 8: Perform local static control-flow review**

Trace these cases without executing code:

1. legacy record with null fingerprint;
2. all four sidecars missing;
3. unchanged record;
4. each suffix added, changed, or deleted;
5. sidecar changes after capture but before/during read;
6. invalid JSON or read failure;
7. `ResetMetadataFrom` before deferred JSON resave;
8. embedded resave changing model mtime; and
9. central/per-folder cache IDs.

Expected: no read/parse/conversion failure before record construction can attach or publish a new fingerprint. A caught `LoadMetadata` upsert failure leaves persistent state old but still permits existing in-memory publication of the newly constructed record; `ResetMetadataFrom` mutates its existing in-memory record before upsert, so its log-and-throw failure can leave that memory mutation in place. A later refresh reloads and reevaluates persistent state, and a concurrent ordinary change forces a later mismatch. No new lock or lock order is introduced, including when maintained callers already hold the reentrant `ModificationLock`.

- [ ] **Step 9: Commit the production change**

Stage only the production file:

```bash
git add -- src/Text2Image/T2IModelHandler.cs
git diff --cached --name-status
git diff --cached --check
git commit -m "fix: invalidate model metadata for sidecar changes"
```

Expected: exactly one source file is committed. Record the returned production commit SHA and resulting source blob for later documentation.

### Task 3: Complete independent source and controller review

**Files:**
- Inspect: `src/Text2Image/T2IModelHandler.cs`
- Inspect: `src/Text2Image/T2IModel.cs`
- Inspect: `src/WebAPI/ModelsAPI.cs`
- Inspect: `src/WebAPI/T2IAPI.cs`
- Inspect: `src/Core/Program.cs`
- Inspect: `src/BuiltinExtensions/ComfyUIBackend/ComfyUIWebAPI.cs`
- Modify only if a review finding requires it: `src/Text2Image/T2IModelHandler.cs`

- [ ] **Step 1: Run the fixed-range source projection**

Run:

```bash
git diff --name-status d2508564975c5ca149048e29f57e428dde6d96f2 HEAD -- src
git diff --numstat d2508564975c5ca149048e29f57e428dde6d96f2 HEAD -- src/Text2Image/T2IModelHandler.cs
git diff --check d2508564975c5ca149048e29f57e428dde6d96f2..HEAD -- src/Text2Image/T2IModelHandler.cs
git hash-object src/Text2Image/T2IModelHandler.cs
```

Expected: exactly one production file, a small focused diff, clean whitespace, and one recorded resulting blob.

- [ ] **Step 2: Prove the fingerprint contract statically**

Run focused searches and inspect numbered source to prove:

- exactly four ordered suffix entries feed the helper;
- missing entries are explicit and all-missing is non-null;
- existing entries include length and UTC last-write ticks;
- `FormattableString.Invariant` makes numeric formatting stable;
- `LoadMetadata` captures once before the decision;
- the same captured value reaches the new record;
- `ResetMetadataFrom` captures before its own lock statements and assigns under the existing lock, while maintained callers may already hold the reentrant `ModificationLock`;
- legacy null differs from every current fingerprint; and
- no content read/hash or preview suffix enters the helper.

- [ ] **Step 3: Prove parsing and writer non-regression**

Compare the production diff and inventories to confirm:

- both existing sidecar read/parse loops are textually unchanged apart from the moved prefix declaration;
- merge and `procAltHeader` order is unchanged;
- invalid JSON still propagates through the existing path;
- no new catch, fallback, retry, log, or lock exists;
- read/parse/conversion failure before construction cannot publish a new fingerprint, but caught `LoadMetadata` upsert failure leaves persistent state old and still publishes the constructed record in memory;
- `ResetMetadataFrom` mutates in-memory metadata before upsert, so upsert failure throws after that memory mutation and does not make persistent success a prerequisite for the mutation;
- cache DB selection and IDs are unchanged;
- `T2IModel`, `ModelsAPI`, `T2IAPI`, `Program`, settings, and Comfy files have no production diff; and
- Core P4 remains unimplemented.

- [ ] **Step 4: Request independent specification review**

Give a fresh reviewer the approved design, plan, source base, production commit, fixed boundaries, and current diff. Require exact token:

```text
SOURCE_SPEC_APPROVED
```

If findings are returned, send them to the same implementation worker, make the minimum source correction with `apply_patch`, commit a focused correction, and have the same specification reviewer re-review until approved.

- [ ] **Step 5: Request independent source-quality review**

After specification approval, give a different fresh reviewer the approved design/plan, complete production history, and current source. Require exact token:

```text
SOURCE_QUALITY_APPROVED
```

Review C# style, XML documentation, helper clarity, fingerprint stability, filesystem/TOCTOU reasoning, LiteDB compatibility, lock placement, error behavior, and scope. Any finding returns to the same implementation worker and then to the same quality reviewer.

- [ ] **Step 6: Run the controller source gate**

Repeat source projection, blob, whitespace, symbol inventory, empty-index, protected-numstat, and untracked-backup checks. Confirm both exact review tokens and no unresolved findings. Do not build or test.

### Task 4: Record implementation and prepare unchanged validation authority

**Files:**
- Modify: `docs/superpowers/specs/2026-07-28-model-sidecar-cache-invalidation-design.md`
- Modify: `docs/superpowers/audits/2026-07-21-maintainability-architecture-refresh.md`

- [ ] **Step 1: Update the design implementation record**

Change status to:

```markdown
**Status:** Implemented; awaiting maintainer validation
```

Record:

- approved source/audit base `d2508564975c5ca149048e29f57e428dde6d96f2`;
- approved design `2048a2bb67e9c8da31233f729695e5d7469683fb`;
- the actual plan commit SHA;
- the actual production commit/correction SHAs and resulting source blob;
- exact source projection;
- `SOURCE_SPEC_APPROVED` and `SOURCE_QUALITY_APPROVED`;
- static-only agent evidence and prohibited runtime actions; and
- any retained caveats from the approved design.

Do not alter the approved `## Maintainer Validation Matrix` through immediately before `## Success Criteria`.

- [ ] **Step 2: Update Core F12 and Rank 22 in the audit**

Change Core F12 and roadmap Rank 22 to **Implemented; awaiting maintainer validation** and record the same exact source/provenance facts. Keep Rank 22 as the sole Recommended Next Project until its maintainer matrix is completed. Do not advance Rank 23 yet.

Retain:

- ordinary existence/length/UTC-last-write contract;
- deliberate same-size/same-time and permission-only caveats;
- central/per-folder and legacy-record compatibility;
- invalid/unreadable changed-sidecar failure behavior;
- writer/consumer preservation;
- P4 separation; and
- no agent runtime/performance claims.

- [ ] **Step 3: Verify document structure and matrix identity**

Run:

```bash
diff <(git show 2048a2bb67e9c8da31233f729695e5d7469683fb:docs/superpowers/specs/2026-07-28-model-sidecar-cache-invalidation-design.md | sed -n '/^## Maintainer Validation Matrix$/,/^## Success Criteria$/p') <(sed -n '/^## Maintainer Validation Matrix$/,/^## Success Criteria$/p' docs/superpowers/specs/2026-07-28-model-sidecar-cache-invalidation-design.md)
test "$(sed -n '/^## Maintainer Validation Matrix$/,/^## Success Criteria$/p' docs/superpowers/specs/2026-07-28-model-sidecar-cache-invalidation-design.md | rg -c '^([0-9]|1[0-9]|2[0-8])\. ')" -eq 28
test "$(rg -c '^## ' docs/superpowers/specs/2026-07-28-model-sidecar-cache-invalidation-design.md)" -eq 17
test "$(rg -c '^## ' docs/superpowers/audits/2026-07-21-maintainability-architecture-refresh.md)" -eq 12
git diff --check -- docs/superpowers/specs/2026-07-28-model-sidecar-cache-invalidation-design.md docs/superpowers/audits/2026-07-21-maintainability-architecture-refresh.md
```

Expected: every command exits `0`; matrix remains byte-identical with 28 cases.

- [ ] **Step 4: Commit the implementation record**

Stage only the two documentation files and commit:

```bash
git add -- docs/superpowers/specs/2026-07-28-model-sidecar-cache-invalidation-design.md docs/superpowers/audits/2026-07-21-maintainability-architecture-refresh.md
git diff --cached --name-status
git diff --cached --check
git commit -m "docs: record model sidecar cache invalidation"
```

- [ ] **Step 5: Request independent documentation reviews**

Use two fresh reviewers sequentially:

1. specification review requiring exact `DOCS_SPEC_APPROVED`;
2. quality review requiring exact `DOCS_QUALITY_APPROVED`.

Any finding returns to the same documentation worker for a focused correction commit and then to the same reviewer. Re-run matrix identity after every documentation edit.

- [ ] **Step 6: Run the controller documentation gate**

Verify:

- exact source/provenance facts;
- status remains awaiting maintainer validation;
- Rank 22 remains sole recommended next;
- Rank 23 is not advanced;
- 17 design H2 sections, 12 audit H2 sections, 32 roadmap ranks, and 28 unchanged matrix cases;
- exact review tokens;
- clean whitespace and index; and
- unchanged protected work.

### Task 5: Record maintainer validation and close Rank 22

**Files:**
- Modify: `docs/superpowers/specs/2026-07-28-model-sidecar-cache-invalidation-design.md`
- Modify: `docs/superpowers/audits/2026-07-21-maintainability-architecture-refresh.md`

- [ ] **Step 1: Ask the maintainer to run the exact approved matrix**

Present the exact unchanged 28 cases from the approved design. Ask the maintainer to report:

- passed, failed, and unrun cases;
- date;
- operating system and filesystem;
- browser if applicable;
- central/per-folder cache modes actually exercised; and
- model/sidecar arrangements actually exercised.

Do not infer omitted details from earlier ranks.

- [ ] **Step 2: Normalize only explicitly supplied evidence**

Record the maintainer's raw response exactly and disclose any normalization. A general instruction such as “mark as passed” may be interpreted only against the exact matrix after explicitly disclosing that interpretation. Do not invent per-case procedure, failure injection, environment, cache mode, browser, model type, duplicate topology, or sidecar content.

- [ ] **Step 3: Update validation status and roadmap**

If all 28 cases pass, change Rank 22 to:

```markdown
**Status:** Implemented and maintainer-validated
```

Record exact passed/failed/unrun totals and only supplied environment/arrangement details. Advance Rank 23—**Establish one lazy-tab descriptor/parity owner**—to the sole Recommended Next Project and state it is neither designed nor implemented.

If any case fails or remains unrun, preserve the accurate partial status and do not overstate completion or advance the roadmap.

- [ ] **Step 4: Preserve the approved matrix byte-for-byte**

Re-run:

```bash
diff <(git show 2048a2bb67e9c8da31233f729695e5d7469683fb:docs/superpowers/specs/2026-07-28-model-sidecar-cache-invalidation-design.md | sed -n '/^## Maintainer Validation Matrix$/,/^## Success Criteria$/p') <(sed -n '/^## Maintainer Validation Matrix$/,/^## Success Criteria$/p' docs/superpowers/specs/2026-07-28-model-sidecar-cache-invalidation-design.md)
```

Expected: exit `0`. Put raw evidence, normalization, and environment records outside the matrix section.

- [ ] **Step 5: Commit the validation record**

Stage only the design and audit:

```bash
git add -- docs/superpowers/specs/2026-07-28-model-sidecar-cache-invalidation-design.md docs/superpowers/audits/2026-07-21-maintainability-architecture-refresh.md
git diff --cached --name-status
git diff --cached --check
git commit -m "docs: validate model sidecar cache invalidation"
```

- [ ] **Step 6: Request validation-document reviews**

Use two fresh reviewers sequentially:

1. validation specification review requiring exact `VALIDATION_SPEC_APPROVED`;
2. validation quality review requiring exact `VALIDATION_QUALITY_APPROVED`.

Correct findings with the same documentation worker and re-review with the same reviewer. Preserve raw evidence and matrix identity.

- [ ] **Step 7: Record review provenance and request final integrated review**

Record all exact review tokens and correction history in the two documentation files with a focused commit. Then ask a fresh final reviewer to inspect the complete design/plan/source/docs/validation history and require exact:

```text
FINAL_INTEGRATED_APPROVED
```

After recording that token in a focused documentation-only commit, have the same final reviewer re-review the tail commit.

- [ ] **Step 8: Run the final controller gate**

Verify:

- approved base/design/plan ancestry;
- exact production source scope, numstat, and blob;
- source unchanged after its final reviewed commit;
- all six source/docs/validation review tokens and final integrated token;
- matrix byte identity and 28 cases;
- accurate maintainer evidence and non-inference;
- correct Rank 22/Rank 23 status;
- 17 design H2 sections, 12 audit H2 sections, and 32 roadmap ranks;
- clean fixed-range whitespace;
- empty staged index; and
- exact protected working-tree state.

Do not claim agent-run build, test, runtime, platform, filesystem, browser, cache-mode, or performance evidence.
