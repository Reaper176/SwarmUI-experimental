# August 2026 Upstream Sync Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Merge all 64 upstream commits from `c28a34c5..fff8db6c` into the fork while preserving fork behavior and adapting upstream features to the refactored architecture.

**Architecture:** Perform one ancestry-preserving merge on `integration/upstream-2026-08`. Accept clean upstream changes, but resolve the 13 conflicted files by retaining the fork's ownership boundaries and porting each upstream feature to its current owner. Keep the merge uncommitted until all backend, frontend, documentation, and static verification gates pass.

**Tech Stack:** Git, C# 12/.NET 8 source, browser JavaScript, CSS/Razor, Python ComfyUI nodes, FDS configuration.

---

Repository policy forbids agents from running builds or tests. Commands below are limited to merge operations, static inspection, syntax checks that do not build the application, and Git validation. Reaper176 must perform live application validation after handoff.

### Task 1: Start the ancestry-preserving merge

**Files:**
- Merge: all files changed by `c28a34c5..fff8db6c`
- Preserve outside merge: local-data and primary-worktree changes listed in the design

- [ ] **Step 1: Confirm the isolated branch is clean and points at the approved design commit**

Run:

```bash
git status --short --branch
git merge-base --is-ancestor 84f422f6 HEAD
```

Expected: branch `integration/upstream-2026-08`, no working-tree entries, and the approved design commit is an ancestor of the committed implementation plan.

- [ ] **Step 2: Start the merge without committing**

Run:

```bash
git merge --no-ff --no-commit upstream/master
```

Expected: Git reports merge conflicts and leaves `MERGE_HEAD` equal to `fff8db6c`; no merge commit exists yet.

- [ ] **Step 3: Verify the conflict inventory**

Run:

```bash
git diff --name-only --diff-filter=U
```

Expected: exactly these 13 files:

```text
src/BuiltinExtensions/ComfyUIBackend/ComfyUIAPIAbstractBackend.cs
src/BuiltinExtensions/ComfyUIBackend/ComfyUIBackendExtension.cs
src/BuiltinExtensions/ComfyUIBackend/ExtraNodes/SwarmComfyCommon/SwarmModels.py
src/BuiltinExtensions/ComfyUIBackend/WorkflowGeneratorSteps.cs
src/Core/InstallableFeatures.cs
src/Pages/_Generate/ServerTab.cshtml
src/Text2Image/T2IParamTypes.cs
src/wwwroot/css/genpage.css
src/wwwroot/js/genpage/gentab/currentimagehandler.js
src/wwwroot/js/genpage/gentab/layout.js
src/wwwroot/js/genpage/main.js
src/wwwroot/js/genpage/server/servertab.js
src/wwwroot/js/genpage/utiltab.js
```

### Task 2: Resolve backend registration and retired API conflicts

**Files:**
- Modify: `src/BuiltinExtensions/ComfyUIBackend/ComfyUIAPIAbstractBackend.cs` in custom-workflow tag filling
- Modify: `src/BuiltinExtensions/ComfyUIBackend/ComfyUIBackendExtension.cs` in capability discovery, registered parameter declarations, `Register()` and update checks
- Modify: `src/BuiltinExtensions/ComfyUIBackend/ComfyNodeNames.cs` in model-node constants
- Modify: `src/BuiltinExtensions/ComfyUIBackend/ComfyNodeInputNames.cs` in model-node input constants
- Modify: `src/Core/InstallableFeatures.cs` in installable feature registration

- [ ] **Step 1: Preserve the refactored custom-workflow boundary**

Resolve `ComfyUIAPIAbstractBackend.cs` to keep the fork's redacted logging, `Utilities.EscapeJsonString(filled)`, and `SwarmUserErrorException` boundary. Do not restore upstream's deleted legacy Stability API path or its pre-refactor inline tag-filling block; upstream commit `39c9a1f1` only removes retired Stability API remnants, which are already absent from the fork path.

- [ ] **Step 2: Combine new Comfy parameters with fork parameters**

In `ComfyUIBackendExtension.cs`, retain all fork declarations including `RegionalPromptingMethod` and Detail Daemon parameters, and add upstream declarations for `ModelAttentionBackend`, `SeedVRUpscaleMethod`, `SeedVRColorCorrectionBehavior`, `SeedVRSplitLatent`, `SeedVRUpscale`, `SeedVRPreDownscale`, `SeedVRTemporalVideoOverlap`, `SeedVRModel`, and `GroupSeedVR`.

Add `public const string ModelAttentionBackend = "ModelAttentionBackend";` to the model-node region of `ComfyNodeNames.cs`. Add a documented `ModelAttentionBackend` nested class with `public const string Attention = "attention";` to the model-input region of `ComfyNodeInputNames.cs`. Integrate discovery into the fork's capability-delta construction using those constants instead of raw node/input string literals.

- [ ] **Step 3: Register Model Attention and SeedVR through current initialization ownership**

Keep the fork's split between parameter registration and `RegisterBackendTypes()`. Add upstream's Model Attention and SeedVR parameter registrations to the parameter-registration method, but do not duplicate backend-type registration, validity checks, web API registration, or update-handler subscriptions already owned by `RegisterBackendTypes()`.

- [ ] **Step 4: Preserve pinned-node update behavior with the upstream key fix**

Apply commit `5008b4c4` by looking up `ComfyNodeGitPins` with `Path.GetFileName(folder)`/`nodeName`, passing the pin to `latestTarget` for update checks and `targetCommit` for updates. Preserve the fork's current method names and error handling.

- [ ] **Step 5: Remove only obsolete Stability API registration**

Resolve `src/Core/InstallableFeatures.cs` according to upstream `39c9a1f1`: remove the retired Stability API feature entry while preserving every fork-only installable feature.

- [ ] **Step 6: Stage and statically inspect the backend registration group**

Run:

```bash
git add src/BuiltinExtensions/ComfyUIBackend/ComfyUIAPIAbstractBackend.cs src/BuiltinExtensions/ComfyUIBackend/ComfyUIBackendExtension.cs src/BuiltinExtensions/ComfyUIBackend/ComfyNodeNames.cs src/BuiltinExtensions/ComfyUIBackend/ComfyNodeInputNames.cs src/Core/InstallableFeatures.cs
git diff --cached --check
git diff --name-only --diff-filter=U
```

Expected: no whitespace errors; the three files disappear from the unmerged list.

### Task 3: Resolve Comfy model and workflow-generation conflicts

**Files:**
- Modify: `src/BuiltinExtensions/ComfyUIBackend/ExtraNodes/SwarmComfyCommon/SwarmModels.py`
- Review merged: `src/BuiltinExtensions/ComfyUIBackend/ExtraNodes/SwarmComfyCommon/SwarmLatents.py`
- Modify: `src/BuiltinExtensions/ComfyUIBackend/WorkflowGeneratorSteps.cs`
- Review merged: `src/BuiltinExtensions/ComfyUIBackend/WGNodeData.cs`
- Review merged: `src/BuiltinExtensions/ComfyUIBackend/WorkflowGenerator.cs`
- Review merged: `src/BuiltinExtensions/ComfyUIBackend/WorkflowGeneratorModelSupport.cs`

- [ ] **Step 1: Combine Python imports and H3 latent support**

In `SwarmModels.py`, retain `folder_paths`, `comfy.utils`, `comfy.sd`, and `node_helpers`; add upstream-required `torch`, `comfy`, and `nodes` imports without duplicates. Preserve the fork's corrected LTX audio latent frame dimension unless upstream's LTX 2.5 data shape proves the new index intentional. Add upstream's `align_frame_count`, `video_latent_t`, `temporal_shape`, `_empty_av_latent`, and `SwarmEmptyMiniMaxH3LatentAV`, and register the new node alongside all fork nodes.

- [ ] **Step 2: Review the cleanly merged audio latent node**

Inspect `SwarmLatents.py` and retain `SwarmAudioSilentMaskPrefixSuffix`, its silence encoding, noise-mask handling, and node mapping. Confirm no fork node mapping was removed.

- [ ] **Step 3: Port workflow features through graph abstractions**

In `WorkflowGeneratorSteps.cs`, integrate upstream behavior from `2db744ed`, `0ee02951`, `7c93341a`, `2d50413a`, and `e588d126`: Model Attention selection, stacked mask shrinking, consistent Video End Image naming, SeedVR2 restoration/pre-downscale, and audio-VAE propagation. Use the fork's `WorkflowGraphEditor`, Comfy capability catalog, and node/input constants rather than restoring direct JSON mutation or duplicated raw strings.

- [ ] **Step 4: Review auto-merged workflow support for feature completeness**

Inspect `WGNodeData.cs`, `WorkflowGenerator.cs`, and `WorkflowGeneratorModelSupport.cs`. Confirm the merged code includes LTX 2.5, MiniMax Music, SeedVR2, H3 single-latent/audio behavior, Anima text-encoder LoRAs, and the fork's Anima/Qwen 3.5 model handling. Correct only integration defects discovered in this review.

- [ ] **Step 5: Stage and inspect the workflow group**

Run:

```bash
git add src/BuiltinExtensions/ComfyUIBackend/ExtraNodes/SwarmComfyCommon/SwarmModels.py src/BuiltinExtensions/ComfyUIBackend/ExtraNodes/SwarmComfyCommon/SwarmLatents.py src/BuiltinExtensions/ComfyUIBackend/WGNodeData.cs src/BuiltinExtensions/ComfyUIBackend/WorkflowGenerator.cs src/BuiltinExtensions/ComfyUIBackend/WorkflowGeneratorModelSupport.cs src/BuiltinExtensions/ComfyUIBackend/WorkflowGeneratorSteps.cs
git diff --cached --check
git diff --name-only --diff-filter=U
```

Expected: no whitespace errors; no Comfy Python/workflow files remain unmerged.

### Task 4: Resolve shared parameter and model-support conflicts

**Files:**
- Modify: `src/Text2Image/T2IParamTypes.cs`
- Review merged: `src/Text2Image/CommonModels.cs`
- Review merged: `src/Text2Image/T2IModelClassSorter.cs`
- Review merged: `src/Text2Image/T2IParamInput.cs`

- [ ] **Step 1: Integrate upstream parameter behavior**

In `T2IParamTypes.cs`, preserve fork parameter IDs, ordering, groups, and refactored registration structure. Add upstream Audio Silent Prefix/Suffix parameters, retain the `Video End Image` naming update, and incorporate the reference-to-video guard from `624028d4` without weakening fork validation.

- [ ] **Step 2: Review auto-merged model classification**

Confirm `CommonModels.cs` and `T2IModelClassSorter.cs` classify SeedVR2, LTX 2.5, MiniMax Music/H3, and Anima LoRAs while retaining the fork's Qwen 3.5 compatibility classes and quantized schema validation. Confirm `T2IParamInput.cs` contains upstream preset-override behavior without physically disabling inputs.

- [ ] **Step 3: Stage and inspect the parameter group**

Run:

```bash
git add src/Text2Image/T2IParamTypes.cs src/Text2Image/CommonModels.cs src/Text2Image/T2IModelClassSorter.cs src/Text2Image/T2IParamInput.cs
git diff --cached --check
git diff --name-only --diff-filter=U
```

Expected: no whitespace errors; `T2IParamTypes.cs` is no longer unmerged.

### Task 5: Resolve server-tab presentation conflicts

**Files:**
- Modify: `src/Pages/_Generate/ServerTab.cshtml`
- Modify: `src/wwwroot/js/genpage/server/servertab.js`
- Modify: `src/wwwroot/css/genpage.css` for server resource styles

- [ ] **Step 1: Adapt upstream resource markup to the fork tab structure**

Port commits `076e153f`, `b961da96`, and `a030780c` into `ServerTab.cshtml`: use semantic non-`p` containers, the refined resource-usage block, and upstream labels while preserving all fork tab classes and extension points.

- [ ] **Step 2: Adapt resource rendering to current frontend ownership**

In `servertab.js`, preserve the fork's class/singleton organization and lazy-tab lifecycle. Port upstream's resource bar rendering, refined layout, and hiding of zero-valued bars. Keep compatibility exports used by Razor handlers or extensions.

- [ ] **Step 3: Preserve server styles for the final CSS resolution**

Record the upstream server resource selectors from `genpage.css` for inclusion in Task 7, together with the mobile and downloader conflict regions.

- [ ] **Step 4: Stage the resolved server files**

Run:

```bash
git add src/Pages/_Generate/ServerTab.cshtml src/wwwroot/js/genpage/server/servertab.js
git diff --cached --check
git diff --name-only --diff-filter=U
```

Expected: the Razor and JavaScript server files disappear from the unmerged list; `genpage.css` remains unmerged until Task 7.

### Task 6: Resolve model-downloader frontend conflicts

**Files:**
- Modify: `src/wwwroot/js/genpage/utiltab.js`
- Review merged: `src/Pages/_Generate/UtilitiesTab.cshtml`
- Modify: `src/wwwroot/css/genpage.css` for downloader styles

- [ ] **Step 1: Port downloader behavior into the utility-tab owner**

In `utiltab.js`, retain the fork's class/singleton ownership and compatibility surface. Port upstream's Civitai version selector, per-version file selection, paid-access warning, and visual row/state handling from commits `2065fee1`, `2e7fcfac`, `15a02d94`, and `165fb20d`.

- [ ] **Step 2: Verify Razor/JavaScript contracts**

Review `UtilitiesTab.cshtml` and ensure every element queried by the adapted utility-tab code exists once, class names match, and inline handlers still resolve through compatibility exports.

- [ ] **Step 3: Preserve downloader styles for the final CSS resolution**

Record the upstream file/version row, access-warning, and alternating table-row styles from `genpage.css` for inclusion in Task 7.

- [ ] **Step 4: Stage resolved utility files**

Run:

```bash
git add src/wwwroot/js/genpage/utiltab.js src/Pages/_Generate/UtilitiesTab.cshtml
git diff --cached --check
git diff --name-only --diff-filter=U
```

Expected: utility-tab files disappear from the unmerged list; `genpage.css` remains unmerged.

### Task 7: Adapt the complete upstream mobile layout

**Files:**
- Modify: `src/wwwroot/js/genpage/gentab/layout.js`
- Modify: `src/wwwroot/js/genpage/gentab/currentimagehandler.js`
- Modify: `src/wwwroot/js/genpage/main.js`
- Modify: `src/wwwroot/css/genpage.css`
- Review merged: `src/wwwroot/css/site.css`
- Review merged: `src/Pages/Shared/_Layout.cshtml`
- Review merged: `src/Pages/_Generate/UserTab.cshtml`
- Review merged: `src/wwwroot/js/site.js`

- [ ] **Step 1: Port mobile layout state into the refactored layout owner**

In `layout.js`, retain fork classes, ownership, event registration, and public compatibility exports. Port the full mobile range `b8c9718f..5fd11228` plus `23baf7d0`: mobile layout activation, prompt-area arrangement, bottom bar, full-view handling, popover/modal coordination, iOS handling, user-settings layout, top-bar behavior, and resizable browser folder bar. Avoid reintroducing upstream global state where the fork already has an owning class.

- [ ] **Step 2: Port current-image mobile and batch-menu behavior**

In `currentimagehandler.js`, integrate mobile current-image layout behavior and the corrected batch-view context-menu actions from `c362f0dc` and `bae49ace`. Preserve fork bulk-action, comparison, history-filter, and output-history module boundaries.

- [ ] **Step 3: Integrate main-page hooks without reversing extraction**

In `main.js`, retain the fork's extracted modules and initialization ordering. Add only the upstream hooks required by mobile layout/server refinements and MiniMax Music. Route behavior to existing owners instead of copying extracted implementations back into `main.js`.

- [ ] **Step 4: Complete CSS conflict resolution**

In `genpage.css`, combine all mobile, popover, bottom-bar, prompt-area, browser-folder, server-resource, and model-downloader styles with the fork's current styles. Preserve CSS class selectors, theme variables, desktop behavior, and responsive rules. Do not introduce ID selectors.

- [ ] **Step 5: Review cleanly merged shared layout files**

Inspect `site.css`, `_Layout.cshtml`, `UserTab.cshtml`, and `site.js` for consistent mobile viewport behavior, popover/modal handling, and DOM/class contracts. Correct only mismatches caused by the merge.

- [ ] **Step 6: Stage and inspect the mobile/frontend group**

Run:

```bash
git add src/wwwroot/js/genpage/gentab/layout.js src/wwwroot/js/genpage/gentab/currentimagehandler.js src/wwwroot/js/genpage/main.js src/wwwroot/css/genpage.css src/wwwroot/css/site.css src/Pages/Shared/_Layout.cshtml src/Pages/_Generate/UserTab.cshtml src/wwwroot/js/site.js
git diff --cached --check
git diff --name-only --diff-filter=U
```

Expected: none of these files remain unmerged and static whitespace validation succeeds.

### Task 8: Review all automatically merged upstream features

**Files:**
- Review all 41 paths from `git diff --name-only c28a34c5..upstream/master`
- Focus: `docs/*.md`, `launchtools/extension_list.fds`, `src/SwarmUI.csproj`, `src/WebAPI/AdminAPI.cs`, `src/Utils/OutputMetadataTracker.cs`, and remaining `src/wwwroot/js/genpage/**` files

- [ ] **Step 1: Confirm every upstream-touched path is represented**

Run:

```bash
git diff --name-only c28a34c5..upstream/master
git diff --name-only HEAD
```

Expected: all 41 upstream paths are represented in the merge result unless an upstream deletion is already represented by absence in the fork.

- [ ] **Step 2: Audit feature coverage by commit group**

Use `git log --reverse --oneline c28a34c5..upstream/master` and inspect the staged diff to confirm coverage of all 64 commits, including ffmpeg 9 compatibility, H3 LoRA/attention/audio/single-latent updates, LTX 2.5, MiniMax Music, audio silence controls, SeedVR2, downloader updates, pinned-node updates, resource display changes, preset override behavior, and documentation.

- [ ] **Step 3: Stage reviewed auto-merged files**

Run:

```bash
git add docs launchtools src
git diff --cached --check
```

Expected: no whitespace errors. This stages only the isolated worktree merge result; primary-checkout uncommitted files are not visible here.

### Task 9: Perform static merge validation

**Files:**
- Validate: complete staged merge result

- [ ] **Step 1: Confirm there are no unresolved conflicts or markers**

Run:

```bash
git diff --name-only --diff-filter=U
rg -n '^(<<<<<<<|=======|>>>>>>>)' -g '!docs/superpowers/plans/2026-08-20-upstream-sync.md'
```

Expected: both commands produce no output.

- [ ] **Step 2: Check repository conventions statically**

Run:

```bash
git diff --cached --check
git diff --cached -- '*.js' | rg '^\+.*\b(var|const)\b|^\+.*\}\s+else\s+\{' || true
git diff --cached -- '*.cs' | rg '^\+.*\bvar\s+[A-Za-z_]|^\+.*\}\s+else\s+\{' || true
git diff --cached -- '*.css' | rg '^\+[^+].*#[A-Za-z_][A-Za-z0-9_-]*' || true
```

Expected: `git diff --cached --check` succeeds. Review convention-scan output and remove newly introduced violations unless syntax is logically required or the line is non-code data.

- [ ] **Step 3: Review staged changes against both parents**

Run:

```bash
git diff --cached --stat
git diff --cached --summary
git status --short
```

Expected: no unmerged entries, no unrelated files, and a staged result limited to the upstream integration plus deliberate conflict adaptations.

- [ ] **Step 4: Commit the merge**

Run:

```bash
git commit -m "Merge upstream updates through August 18"
```

Expected: one merge commit whose first parent is the pre-merge integration-plan commit and whose second parent is `fff8db6c`.

- [ ] **Step 5: Verify ancestry and final cleanliness**

Run:

```bash
git merge-base --is-ancestor upstream/master HEAD
git show --no-patch --format='%P' HEAD
git status --short --branch
```

Expected: ancestor check exits 0; the merge commit has two parents; the isolated worktree is clean.

### Task 10: Prepare maintainer validation handoff

**Files:**
- Report only; do not modify source

- [ ] **Step 1: Summarize conflict adaptations and static evidence**

Record the merge commit, files manually adapted, static-check results, and uncertain runtime behavior. Explicitly state that builds and tests were not run because `AGENTS.md` reserves them for the maintainer.

- [ ] **Step 2: Provide focused live-validation scenarios**

Ask Reaper176 to validate desktop and mobile generation layouts; prompt popovers/modals; model downloader version/file selection; server resource bars; Anima text-encoder LoRAs; H3 video/audio/single-latent modes; LTX 2.5; MiniMax Music; SeedVR2 image/video restoration and pre-downscale; pinned Comfy node updates; and existing fork history/editor/refactor behavior.
