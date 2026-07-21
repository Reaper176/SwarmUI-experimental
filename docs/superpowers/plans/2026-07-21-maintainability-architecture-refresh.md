# SwarmUI Maintainability Architecture Refresh Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Produce a current, evidence-backed architecture audit and dependency-ordered refactoring roadmap for every SwarmUI-maintained subsystem, prioritizing compatibility and reliability before maintainability, measurable performance, and cosmetic consistency.

**Architecture:** Build one audit report through independent frontend, core-server, backend/generation, and Comfy/Python evidence stages. Reconcile those findings with completed refactors only after all cross-layer consumers are traced, then rank bounded follow-up projects and select one implementation-ready next project.

**Tech Stack:** Git, Bash, `rg`, `wc`, static JavaScript/C#/Python source inspection, Markdown, existing SwarmUI design/audit documents. Repository policy prohibits agent-run builds, tests, launchers, installers, servers, browser automation, backend execution, and Comfy execution.

---

## Why this remains one plan

The audit spans several independent subsystems, but it does not implement changes in any of them. Its single product is a cross-project report whose ranking depends on comparing evidence and prerequisites across those subsystems. Every production refactor identified by the report becomes its own later brainstorming specification and implementation plan.

## Protected workspace and authority

- Work directly on the existing `master` checkout; Reaper176 explicitly approved direct-master work and declined worktrees.
- Read `AGENTS.md`, `docs/project-memory.md`, and `docs/superpowers/specs/2026-07-21-maintainability-architecture-refresh-design.md` before audit work.
- Treat current committed source as authority. Use the earlier audit only as a baseline whose findings can be completed, retained, superseded, reranked, or rejected.
- Preserve and never stage or modify:
  - `src/Data/Settings.fds`
  - `src/Pages/Text2Image.cshtml`
  - `src/wwwroot/js/genpage/gentab/loras.js`
  - `src/wwwroot/js/genpage/main.js`
  - `Data.pre-restore-2026-07-19/`
- Never inspect `Data.pre-restore-2026-07-19/`.
- Where the audit needs one of the four protected tracked source files, inspect the committed snapshot with `git show HEAD:<path>` rather than the working-tree file.
- Do not inspect or modify external extensions, downloaded upstream repositories, generated API documentation, build artifacts, or user data.
- Do not run any build, test, launcher, installer/updater, server, browser, backend, or Comfy command.
- Use `apply_patch` for every report edit.
- Stage and commit only the audit report at each documentation checkpoint.

## Deliverable file

- Create and progressively modify `docs/superpowers/audits/2026-07-21-maintainability-architecture-refresh.md`.
- Do not modify the prior audit. Quote or paraphrase it only when classifying its findings against current source.
- The final report must contain exactly these top-level sections:

```markdown
## Executive Summary
## Scope, Exclusions, and Method
## Current Architecture
## Primary Cross-Layer Flows
## Completed-Refactor Assessment
## Evidence-Backed Findings
## Compatibility and Reliability Risk Register
## Performance Opportunities and Measurement Requirements
## Ranked Refactoring Roadmap
## Recommended Next Project
## Deferred, Superseded, and Rejected Ideas
## Static Verification and Maintainer Validation Boundaries
```

## Evidence recording rules

For every finding retained in the final report, record:

```text
Boundary:
Exact source evidence:
Known maintained consumers:
Current data/control flow:
Problem or risk:
Certainty: Confirmed statically | Strongly inferred | Runtime measurement required
Compatibility constraints:
Existing project pattern:
Bounded opportunity:
Migration stages:
Rollback boundary:
Static verification:
Maintainer validation:
```

Do not keep a finding that lacks an exact source path/symbol, a concrete problem, known search boundary, bounded owner, and compatibility treatment. Raw line counts, global counts, or literal counts are supporting evidence only.

### Task 1: Establish the committed baseline and architecture inventory

**Files:**
- Create: `docs/superpowers/audits/2026-07-21-maintainability-architecture-refresh.md`
- Read: `AGENTS.md`
- Read: `docs/project-memory.md`
- Read: `docs/superpowers/audits/2026-07-20-maintainability-architecture-audit.md`
- Read: `docs/superpowers/specs/2026-07-21-maintainability-architecture-refresh-design.md`
- Read: current maintained source inventories only

- [ ] **Step 1: Confirm branch, protected state, and audit base**

Run:

```bash
git status --short --branch
git branch --show-current
git log -25 --oneline --decorate
git diff --name-only
git diff --cached --name-only
```

Expected: branch is `master`; the four known tracked files and backup directory are the only unrelated dirty paths; index is empty. Record the design commit SHA and current audit base SHA in the report. Do not inspect dirty contents.

- [ ] **Step 2: Inventory maintained files without entering excluded trees**

Run:

```bash
rg --files -g '!Data/**' -g '!Output/**' -g '!Models/**' -g '!src/Extensions/**' -g '!dlbackend/**' -g '!src/BuiltinExtensions/ComfyUIBackend/DLNodes/**' -g '!src/bin/**' -g '!src/obj/**' -g '!.vs/**' > /tmp/swarmui-maintained-files.txt
wc -l /tmp/swarmui-maintained-files.txt
awk -F. 'NF > 1 { ext=$NF; count[ext]++ } END { for (ext in count) print ext, count[ext] }' /tmp/swarmui-maintained-files.txt | sort
```

Expected: the inventory contains only repository-maintained paths and reports counts by extension. `/tmp/swarmui-maintained-files.txt` is a disposable read-only inventory, not a repository artifact.

- [ ] **Step 3: Record subsystem size indicators and public-surface indicators**

Run the following commands and record search boundaries with every count:

```bash
rg --files src -g '*.cs' -g '*.js' -g '*.css' -g '*.cshtml' -g '*.py' -g '!src/Extensions/**' -g '!src/BuiltinExtensions/ComfyUIBackend/DLNodes/**' -g '!src/bin/**' -g '!src/obj/**' | xargs wc -l | tail -1
rg -n '^public (static )?(class|interface|record|struct)|^    public (static )?' src --glob '*.cs' -g '!src/Extensions/**' -g '!src/BuiltinExtensions/ComfyUIBackend/DLNodes/**'
rg -n '^(let|class|function) |^window\.[A-Za-z_$][A-Za-z0-9_$]*' src/wwwroot/js --glob '*.js'
rg -n 'onclick=|onchange=|oninput=|onkeydown=|onkeyup=' src/Pages --glob '*.cshtml' -g '!src/Pages/Text2Image.cshtml'
git show HEAD:src/Pages/Text2Image.cshtml | rg -n 'onclick=|onchange=|oninput=|onkeydown=|onkeyup='
```

Expected: the output provides indicators for later consumer tracing. Do not present aggregate declaration counts as defects.

- [ ] **Step 4: Map composition roots, registries, and lifecycle owners**

Inspect definitions and direct consumers for at least:

```bash
rg -n 'class Program|static void Main|OnPreInit|OnInit|OnPreLaunch|OnShutdown' src/Core src/BuiltinExtensions --glob '*.cs' -g '!src/Extensions/**'
rg -n 'class WebServer|class API|RegisterAPICall|Register\(' src/Core src/WebAPI src/BuiltinExtensions --glob '*.cs' -g '!src/Extensions/**'
rg -n 'class ExtensionsManager|class Extension|Extension<' src/Core src/BuiltinExtensions --glob '*.cs' -g '!src/Extensions/**'
rg -n 'Program\.(Backends|Sessions|T2IModelSets|Extensions|ServerSettings|Web|GlobalProgramCancel)' src --glob '*.cs' -g '!src/Extensions/**'
rg -n 'static (Dictionary|ConcurrentDictionary|HashSet|List)|public static .*=' src --glob '*.cs' -g '!src/Extensions/**'
rg -n 'dotnet|python|git|update|install|launch|exec|process|venv' . -g '*.sh' -g '*.bat' -g '*.ps1' -g 'launchtools/**' -g '!Data/**' -g '!Output/**' -g '!Models/**'
```

Expected: the report can identify current composition, registration, global-state, lifecycle, and launcher/install/update surfaces with exact paths and consumers. Root launchers are read-only high-risk evidence; do not execute them.

- [ ] **Step 5: Create the report with factual baseline sections**

Use `apply_patch` to create the report. At this stage include:

```markdown
# SwarmUI Maintainability Architecture Refresh

## Scope, Exclusions, and Method

This refresh audits the current committed SwarmUI-maintained codebase through static source inspection. It excludes external extensions, downloaded upstream repositories, generated content, build artifacts, and user data. Protected tracked files with unrelated working-tree changes are inspected only through their committed `HEAD` snapshots. Findings are ranked by compatibility and reliability, ownership and maintainability, measurable performance, then cosmetic consistency.

## Current Architecture

### Repository and composition baseline

SwarmUI combines Razor and classic-script browser code, a C# server and generation domain, built-in extensions, and maintained Comfy Python nodes. This subsection records the exact current composition roots, registries, lifecycle owners, and subsystem boundaries found in Steps 2-4.

### Inventory indicators

This subsection records the actual inventory counts produced in Steps 2-3 together with their search boundaries. The measurements guide navigation and comparison; they are not independent evidence that a refactor is required.
```

Replace the generic second sentence under `Repository and composition baseline` with exact source paths and symbols found in Steps 2-4, and append the measured inventory values under `Inventory indicators`. Do not add other final report headings until they contain evidence.

- [ ] **Step 6: Verify and commit the baseline report**

Run:

```bash
rg -n 'TBD|TODO|FIXME|\[[A-Z][^]]*\]' docs/superpowers/audits/2026-07-21-maintainability-architecture-refresh.md
git diff --check -- docs/superpowers/audits/2026-07-21-maintainability-architecture-refresh.md
git diff --stat -- docs/superpowers/audits/2026-07-21-maintainability-architecture-refresh.md
git add docs/superpowers/audits/2026-07-21-maintainability-architecture-refresh.md
git diff --cached --check
git commit -m "docs: inventory current SwarmUI architecture"
```

Expected: placeholder scan has no output; only the new report is committed; protected paths remain unstaged.

### Task 2: Audit frontend, Razor, CSS, and Web composition

**Files:**
- Modify: `docs/superpowers/audits/2026-07-21-maintainability-architecture-refresh.md`
- Read: `src/Pages/**/*.cshtml`, using `git show HEAD:src/Pages/Text2Image.cshtml` for the protected page
- Read: `src/wwwroot/js/**/*.js`, using `git show HEAD:src/wwwroot/js/genpage/main.js` and `git show HEAD:src/wwwroot/js/genpage/gentab/loras.js` for protected scripts
- Read: `src/wwwroot/css/**/*.css`
- Read: relevant built-in extension frontend contributions

- [ ] **Step 1: Trace layout, page boot, classic-script order, and lazy loading**

Run and inspect:

```bash
sed -n '1,240p' src/Pages/Shared/_Layout.cshtml
git show HEAD:src/Pages/Text2Image.cshtml | sed -n '1,260p'
rg -n 'ScriptFiles|StyleSheetFiles|GenPage|Tabs|OnPreInit|OnInit' src/Core/Extension.cs src/Core/WebServer.cs src/BuiltinExtensions --glob '*.cs'
rg -n 'genpageLoad|loadScript|lazy|shown\.bs\.tab|sessionReadyCallbacks|postSessionReadyCallbacks' src/wwwroot/js --glob '*.js' -g '!src/wwwroot/js/genpage/main.js'
git show HEAD:src/wwwroot/js/genpage/main.js | rg -n 'genpageLoad|loadScript|lazy|shown\.bs\.tab|sessionReadyCallbacks|postSessionReadyCallbacks'
```

Expected: document the exact boot sequence, extension contribution path, lazy-tab contract, and ordering-sensitive globals.

- [ ] **Step 2: Reassess completed frontend refactors and their compatibility facades**

Inspect current definitions and consumers:

```bash
rg -n 'imageEditingEnsureUiReady|imageEditing[A-Z]|ImageEditing' src/Pages src/wwwroot/js --glob '*.cshtml' --glob '*.js' -g '!src/Pages/Text2Image.cshtml' -g '!src/wwwroot/js/genpage/main.js'
git show HEAD:src/Pages/Text2Image.cshtml | rg -n 'imageEditing|image_editor_ui'
git show HEAD:src/wwwroot/js/genpage/main.js | rg -n 'imageEditing'
rg -n 'class ImageHistoryController|imageHistoryController|ImageHistoryComparison|ImageHistoryFilter|ImageHistoryBulkActions|outputHistory' src/Pages src/wwwroot/js --glob '*.cshtml' --glob '*.js'
git log --oneline -- src/wwwroot/js/genpage/helpers/image_editor_ui.js src/wwwroot/js/genpage/gentab/outputhistory.js src/wwwroot/js/genpage/gentab/imagehistorycomparison.js src/wwwroot/js/genpage/gentab/imagehistoryfilter.js src/wwwroot/js/genpage/gentab/imagehistorybulkactions.js
```

Expected: classify each earlier frontend roadmap item as complete, complete with a justified follow-up, superseded, or reopened only by new concrete evidence.

- [ ] **Step 3: Inventory browser globals and cross-feature ownership**

For each large feature file, list top-level declarations and search every candidate cross-feature symbol across Razor and maintained JS. At minimum inspect:

```bash
rg -n '^(let|class|function) |^window\.' src/wwwroot/js/site.js src/wwwroot/js/util.js src/wwwroot/js/genpage/gentab/currentimagehandler.js src/wwwroot/js/genpage/gentab/outputhistory.js src/wwwroot/js/genpage/params.js src/wwwroot/js/genpage/models.js src/wwwroot/js/genpage/helpers/generatehandler.js
rg -n 'genericRequest|makeWSRequest|getGenInput|setCurrentImage|SwarmUtil|ImageHistoryController|GenerateHandler' src/Pages src/wwwroot/js src/BuiltinExtensions --glob '*.cshtml' --glob '*.js' --glob '*.cs'
```

Expected: retained facade/global findings name actual callers and distinguish intentional compatibility exports from unowned shared state.

- [ ] **Step 4: Audit frontend reliability, errors, listeners, timers, and performance candidates**

Run focused searches, then inspect each result in context:

```bash
rg -n 'setInterval|setTimeout|addEventListener|removeEventListener|MutationObserver|ResizeObserver|WebSocket|AbortController|fetch\(' src/wwwroot/js --glob '*.js'
rg -n 'catch\s*\(|\.catch\(|console\.(error|warn)|showError|genericRequest' src/wwwroot/js --glob '*.js'
rg -n 'JSON\.(parse|stringify)|querySelectorAll|getElementsBy|innerHTML|outerHTML|localStorage|sessionStorage' src/wwwroot/js/genpage src/wwwroot/js/site.js src/wwwroot/js/util.js --glob '*.js'
rg -n '@media|!important|position:\s*fixed|z-index' src/wwwroot/css --glob '*.css'
rg --files src/wwwroot/css -g '*.css' | xargs wc -l | tail -1
```

Expected: keep only lifecycle leaks, repeated critical-path work, unsafe error gaps, or ownership problems supported by contextual flow. CSS size and selector counts alone are not findings.

- [ ] **Step 5: Add frontend architecture, flow, completed-work, and candidate findings**

Use `apply_patch` to expand the report with:

- current browser/Razor/Web composition under `Current Architecture`;
- browser boot and generated-output/history flow under a new `Primary Cross-Layer Flows` section;
- completed Image Editing and image-history assessments under a new `Completed-Refactor Assessment` section;
- fully evidenced frontend findings under a new `Evidence-Backed Findings` section;
- confirmed reliability items under a new `Compatibility and Reliability Risk Register` section;
- evidenced or measurement-gated client opportunities under a new `Performance Opportunities and Measurement Requirements` section.

Every finding must use the evidence recording rules. Do not rank projects yet.

- [ ] **Step 6: Verify and commit frontend evidence**

Run:

```bash
rg -n 'TBD|TODO|FIXME|\[[A-Z][^]]*\]' docs/superpowers/audits/2026-07-21-maintainability-architecture-refresh.md
git diff --check -- docs/superpowers/audits/2026-07-21-maintainability-architecture-refresh.md
git diff --word-diff=plain -- docs/superpowers/audits/2026-07-21-maintainability-architecture-refresh.md
git add docs/superpowers/audits/2026-07-21-maintainability-architecture-refresh.md
git diff --cached --check
git commit -m "docs: audit SwarmUI frontend architecture"
```

Expected: only evidence-backed report prose is committed; no production or protected file is staged.

### Task 3: Audit core server, APIs, parameters, models, and utilities

**Files:**
- Modify: `docs/superpowers/audits/2026-07-21-maintainability-architecture-refresh.md`
- Read: `src/Core/**/*.cs`, `src/WebAPI/**/*.cs`, `src/Text2Image/**/*.cs`, `src/Utils/**/*.cs`, and maintained model/session/account code
- Read: completed T2I API split designs and current route owners

- [ ] **Step 1: Trace startup, lifecycle, settings, extension phases, and shutdown**

```bash
rg -n 'static void Main|OnPreInit|OnInit|OnPreLaunch|OnShutdown|GlobalProgramCancel|Cancel\(|Dispose\(|Shutdown' src/Core src/WebAPI src/Text2Image src/Utils src/BuiltinExtensions --glob '*.cs' -g '!src/Extensions/**'
rg -n 'Program\.(ServerSettings|Backends|Sessions|T2IModelSets|Extensions|Web|GlobalProgramCancel)' src --glob '*.cs' -g '!src/Extensions/**'
rg -n 'Save\(|Load\(|Reload|File\.(Read|Write)|Directory\.' src/Core/Settings.cs src/Core/Program.cs src/Core/ExtensionsManager.cs src/Core/Installation.cs src/Utils --glob '*.cs'
```

Expected: document construction order, mutable service access, cancellation/shutdown ownership, persistence, and extension timing with consumers.

- [ ] **Step 2: Trace API registration, binding, permissions, errors, and route ownership**

```bash
rg -n 'RegisterAPICall|APIDescription|Permission|HandleAsyncRequest|APICallReflectBuilder|WebSocket|HttpContext' src/WebAPI src/Core/WebServer.cs src/BuiltinExtensions --glob '*.cs'
rg -n 'public static.*(Generate|Image|History|Krita|Inpaint)|Register\(' src/WebAPI/T2IAPI.cs src/WebAPI/ImageHistoryAPI.cs src/WebAPI/KritaAPI.cs src/WebAPI/ClassicInpaintAPI.cs src/WebAPI/BasicAPIFeatures.cs
rg -n 'T2IAPI\.(DeleteImage|ListImages|Image|Krita|ClassicInpaint)|ImageHistoryAPI\.|KritaAPI\.|ClassicInpaintAPI\.' src --glob '*.cs' -g '!src/Extensions/**'
git log --oneline -- src/WebAPI/T2IAPI.cs src/WebAPI/ImageHistoryAPI.cs src/WebAPI/KritaAPI.cs src/WebAPI/ClassicInpaintAPI.cs
```

Expected: reassess the T2I API split, forwarders/direct callers, reflective contracts, registration order, and permission/error compatibility.

- [ ] **Step 3: Inspect parameter, model, session, and user boundaries**

```bash
rg -n 'class T2IParamTypes|RegisterDefaults|ParameterRemaps|Validate|Apply|ToNet|FromNet|ListModels|ModelHandler|SessionHandler|class Session|class User' src/Text2Image src/Models src/Core src/WebAPI --glob '*.cs'
rg -n 'T2IParamTypes\.|Program\.T2IModelSets|Program\.Sessions|Program\.MainSDModels' src --glob '*.cs' -g '!src/Extensions/**'
rg -n 'ConcurrentDictionary|LockObject|lock \(|Semaphore|CancellationToken|Task\.Run|Task\.Wait|\.Result\b|\.Wait\(' src/Text2Image src/Models src/Core src/WebAPI --glob '*.cs'
```

Expected: findings separate public registry identity from validation/application behavior and name session/model/concurrency consumers.

- [ ] **Step 4: Audit utilities, I/O, process, error, and repeated-work candidates**

```bash
rg -n '^    public (static )?' src/Utils/Utilities.cs src/Utils/*.cs
rg -n 'Utilities\.' src --glob '*.cs' -g '!src/Extensions/**'
rg -n 'File\.(Read|Write)|Directory\.|Process\.|HttpClient|WebClient|SHA|JSON|ParseToJson|ToString\(' src/Core src/WebAPI src/Text2Image src/Models src/Utils --glob '*.cs'
rg -n 'catch\s*\(|Logs\.(Error|Warning|Info)|throw new|throw;' src/Core src/WebAPI src/Text2Image src/Models src/Utils --glob '*.cs'
```

Expected: do not propose a bulk utility move. Retain only coherent domain clusters, critical-path I/O, repeated parsing/scans, or error-context issues with known consumers.

- [ ] **Step 5: Expand the report with core-server evidence**

Use `apply_patch` to add or expand:

- startup/shutdown, API dispatch, permissions, parameter/model/session, and extension flows;
- completed T2I API split assessment;
- core findings with full evidence records;
- compatibility risks involving public statics, reflective route signatures, persistence, permissions, serialization, cancellation, or extension lifecycle;
- performance candidates with static mechanism or explicit measurement requirement.

Do not rank projects or select the next project yet.

- [ ] **Step 6: Verify and commit core evidence**

```bash
rg -n 'TBD|TODO|FIXME|\[[A-Z][^]]*\]' docs/superpowers/audits/2026-07-21-maintainability-architecture-refresh.md
git diff --check -- docs/superpowers/audits/2026-07-21-maintainability-architecture-refresh.md
git add docs/superpowers/audits/2026-07-21-maintainability-architecture-refresh.md
git diff --cached --check
git commit -m "docs: audit SwarmUI core server architecture"
```

Expected: report only; protected state unchanged.

### Task 4: Audit backend lifecycle, scheduling, generation, and output paths

**Files:**
- Modify: `docs/superpowers/audits/2026-07-21-maintainability-architecture-refresh.md`
- Read: `src/Backends/**/*.cs`
- Read: generation orchestration under `src/Text2Image/**/*.cs`
- Read: output and metadata paths under maintained core/Web API code

- [ ] **Step 1: Map backend catalogs, instances, persistence, lifecycle, and availability**

```bash
rg -n 'class BackendHandler|BackendTypes|AllBackends|Load|Save|Reload|Init|Shutdown|Enabled|Status|Monitor|AutoScal' src/Backends --glob '*.cs'
rg -n 'Program\.Backends|BackendHandler\.' src --glob '*.cs' -g '!src/Extensions/**'
rg -n 'LockObject|lock \(|Semaphore|ManualResetEvent|AutoResetEvent|CancellationToken|Task\.Run|Thread' src/Backends --glob '*.cs'
```

Expected: record owner, locks/signals, persistence, monitor/autoscaling, and external consumers without suggesting synchronization changes before lock/order tracing.

- [ ] **Step 2: Trace scheduling, pressure, claims, disposal, cancellation, and failure paths**

```bash
rg -n 'ModelRequestPressure|T2IBackendRequest|GetNextT2IBackend|T2IBackendAccess|Claim|Dispose|Cancel|Signal|Queue|Priority' src/Backends src/Text2Image --glob '*.cs'
rg -n 'GenerateLive|CreateImageTask|GenClaim|RequestRefusal|RefusalReasons|Backend' src/Text2Image src/Backends src/WebAPI --glob '*.cs'
rg -n 'catch\s*\(|throw|Logs\.(Error|Warning)|TrySetException|TrySetCanceled' src/Backends src/Text2Image --glob '*.cs'
```

Expected: document request state transitions, claim lifetime, model preference/loading, cancellation, backend removal, and error propagation.

- [ ] **Step 3: Trace output, metadata, file naming, indexing, and history handoff**

```bash
rg -n 'OutputMetadataTracker|MetadataHelper|SaveImage|ImageMetadata|OutputPath|FileName|History|ImageHistoryAPI|SetImage' src --glob '*.cs' -g '!src/Extensions/**'
rg -n 'File\.(Read|Write|Move|Delete)|Directory\.(Enumerate|GetFiles)|EnumerateFiles|GetFiles' src/Text2Image src/WebAPI src/Utils src/Models --glob '*.cs'
```

Expected: identify exact persistence/index synchronization, permission, path, and repeated-scan boundaries; do not infer runtime materiality without evidence.

- [ ] **Step 4: Inspect structurally evidenced backend/generation performance candidates**

For each loop, poll, scan, sort, lock, model enumeration, queue traversal, or serialization candidate found in Steps 1-3, record:

```text
Invocation path and frequency source:
Input cardinality or unbounded dimension:
Synchronous/asynchronous behavior:
Lock or shared-state interaction:
Existing cache/snapshot/index:
Static certainty:
Required runtime measurement:
```

Reject candidates whose frequency/cardinality cannot be tied to a real path; retain them only as explicit measurements if the mechanism is plausible.

- [ ] **Step 5: Expand the report with backend/generation evidence**

Use `apply_patch` to add current backend/generation/output architecture and flows, full findings, concurrency/cancellation/claim risks, output compatibility constraints, and performance/measurement candidates. Keep scheduling-policy extraction separate from lifecycle extraction unless evidence shows one bounded first seam.

- [ ] **Step 6: Verify and commit backend evidence**

```bash
rg -n 'TBD|TODO|FIXME|\[[A-Z][^]]*\]' docs/superpowers/audits/2026-07-21-maintainability-architecture-refresh.md
git diff --check -- docs/superpowers/audits/2026-07-21-maintainability-architecture-refresh.md
git add docs/superpowers/audits/2026-07-21-maintainability-architecture-refresh.md
git diff --cached --check
git commit -m "docs: audit SwarmUI backend architecture"
```

Expected: report only; no runtime claim.

### Task 5: Audit Comfy integration, workflow generation, and managed Python

**Files:**
- Modify: `docs/superpowers/audits/2026-07-21-maintainability-architecture-refresh.md`
- Read: `src/BuiltinExtensions/ComfyUIBackend/**/*.cs`
- Read: `src/BuiltinExtensions/ComfyUIBackend/ExtraNodes/**/*.py`
- Exclude: `src/BuiltinExtensions/ComfyUIBackend/DLNodes/**`
- Read: completed Comfy catalog designs and implementation history

- [ ] **Step 1: Reassess the completed Comfy catalogs and capability boundary**

```bash
rg -n '^public static class|public const string|CreateNodeToFeatureMap|NodeToFeatureMap|ComfyNodeNames|ComfyNodeInputNames' src/BuiltinExtensions/ComfyUIBackend --glob '*.cs'
rg -n 'NODE_CLASS_MAPPINGS|INPUT_TYPES|define_schema' src/BuiltinExtensions/ComfyUIBackend/ExtraNodes --glob '*.py'
rg -n 'ComfyNodeNames\.|ComfyNodeInputNames\.' src --glob '*.cs' -g '!src/Extensions/**'
git log --oneline -- src/BuiltinExtensions/ComfyUIBackend/ComfyNodeNames.cs src/BuiltinExtensions/ComfyUIBackend/ComfyNodeInputNames.cs src/BuiltinExtensions/ComfyUIBackend/ComfyCapabilityCatalog.cs
```

Expected: verify current ownership, consumer scope, Python authority, optional-node behavior, and whether any follow-up is justified beyond cosmetic consistency.

- [ ] **Step 2: Trace Comfy extension lifecycle, workflow storage, object-info, model lists, install/update, and transport**

```bash
rg -n '^    public |^    private |OnPreInit|OnInit|OnPreLaunch|OnShutdown|LoadWorkflowFiles|GetWorkflowByName|Refresh|AssignValuesFromRaw|CheckForUpdates|DoBackendUpdates' src/BuiltinExtensions/ComfyUIBackend/ComfyUIBackendExtension.cs
rg -n 'CustomWorkflows|FeaturesSupported|RawObjectInfoParsers|RunningComfyBackends|AssignValuesFromRaw|LoadWorkflowFiles|GetWorkflowByName' src --glob '*.cs' -g '!src/Extensions/**'
rg -n 'GenerateLive|WebSocket|HttpClient|object_info|prompt|queue|history|interrupt|Dispose|Cancel' src/BuiltinExtensions/ComfyUIBackend --glob '*.cs'
```

Expected: distinguish composition-root responsibilities, storage, capability discovery, parameters, install/update, and transport with known consumers and lifecycle timing.

- [ ] **Step 3: Inventory WorkflowGenerator state, steps, public extension surface, and reserved contracts**

```bash
wc -l src/BuiltinExtensions/ComfyUIBackend/WorkflowGenerator.cs src/BuiltinExtensions/ComfyUIBackend/WorkflowGeneratorModelSupport.cs src/BuiltinExtensions/ComfyUIBackend/WorkflowGeneratorSteps.cs src/BuiltinExtensions/ComfyUIBackend/WGNodeData.cs
rg -n '^    public |^    internal |^    private |WorkflowGenStep|AddStep|AddModelGenStep|Steps|ModelGenSteps|LastID|GetStableDynamicID|CreateNode' src/BuiltinExtensions/ComfyUIBackend/WorkflowGenerator*.cs src/BuiltinExtensions/ComfyUIBackend/WGNodeData.cs
rg -n 'WorkflowGenerator\.(AddStep|AddModelGenStep)|new WorkflowGenerator|CreateNode\(|RunOnNodesOfClass|NodesOfClass' src --glob '*.cs' -g '!src/Extensions/**'
rg -n 'AddStep\(g =>|AddModelGenStep\(g =>' src/BuiltinExtensions src --glob '*.cs' -g '!src/Extensions/**'
```

Expected: identify extension-visible methods, mutable state, priority/order, IDs, graph rewrite paths, and cohesive collaborator seams. File size alone is not the finding.

- [ ] **Step 4: Trace C#/Python workflow contracts, optional modules, errors, and performance candidates**

```bash
rg -n 'CreateNode\(|ComfyNodeNames\.|ComfyNodeInputNames\.|class_type|inputs' src/BuiltinExtensions/ComfyUIBackend --glob '*.cs'
rg -n 'NODE_CLASS_MAPPINGS|NODE_DISPLAY_NAME_MAPPINGS|INPUT_TYPES|RETURN_TYPES|FUNCTION|CATEGORY' src/BuiltinExtensions/ComfyUIBackend/ExtraNodes --glob '*.py'
rg -n 'try:|except|raise |logging\.|print\(' src/BuiltinExtensions/ComfyUIBackend/ExtraNodes --glob '*.py'
rg -n 'ParseToJson|ToString\(|DeepClone|JObject|JArray|Properties\(\)|Descendants|SelectToken|lock \(' src/BuiltinExtensions/ComfyUIBackend --glob '*.cs'
```

Expected: retain contract, parsing, graph traversal/rebuild, error, or optional-module findings only with a traced invocation path and owner.

- [ ] **Step 5: Expand the report with Comfy/Python evidence**

Use `apply_patch` to add Comfy lifecycle/transport/workflow/Python architecture and flows, completed-catalog assessment, full findings, compatibility risks, and performance measurement candidates. Any WorkflowGenerator recommendation must be split into a bounded first collaborator extraction rather than a single broad decomposition project.

- [ ] **Step 6: Verify and commit Comfy evidence**

```bash
rg -n 'TBD|TODO|FIXME|\[[A-Z][^]]*\]' docs/superpowers/audits/2026-07-21-maintainability-architecture-refresh.md
git diff --check -- docs/superpowers/audits/2026-07-21-maintainability-architecture-refresh.md
git add docs/superpowers/audits/2026-07-21-maintainability-architecture-refresh.md
git diff --cached --check
git commit -m "docs: audit SwarmUI Comfy architecture"
```

Expected: report only; no Python or C# edits.

### Task 6: Synthesize cross-system risks, reconcile the old roadmap, and rank the refresh

**Files:**
- Modify: `docs/superpowers/audits/2026-07-21-maintainability-architecture-refresh.md`
- Read: all evidence already recorded in the report
- Read: `docs/superpowers/audits/2026-07-20-maintainability-architecture-audit.md`
- Read: completed design specs and commit history for former roadmap items 1-4

- [ ] **Step 1: Trace every retained boundary across all consumers**

For each finding, rerun exact symbol/literal searches across its complete maintained boundary. Record definitions separately from callers. Remove or defer any finding with unknown maintained consumers, ambiguous ownership, or a migration that begins by breaking a compatibility surface.

Use this required disposition vocabulary:

```text
Retain as roadmap project
Retain as measurement prerequisite
Merge into another bounded project
Defer as architectural direction
Superseded by completed work
Reject as unsupported or cosmetic
```

- [ ] **Step 2: Reconcile every previous roadmap item**

Create one assessment entry for each former item:

```text
Previous rank and title:
Current disposition:
Completed commits/files or current evidence:
Remaining justified seam:
Reason for retain/rerank/supersede/reject:
```

At minimum reconcile the nine earlier roadmap projects. Completed items 1-4 must not remain as if unimplemented.

- [ ] **Step 3: Build the compatibility/reliability risk register**

For every confirmed risk, record:

```text
Risk and affected flow:
Trigger:
Impact:
Current mitigation:
Static evidence:
Runtime uncertainty:
Recommended action or measurement:
Roadmap dependency:
```

Order the register by potential user/data/process impact, not ease of refactoring. Do not inflate maintainability concerns into reliability failures.

- [ ] **Step 4: Finalize performance opportunities and measurement gates**

Classify each candidate as:

```text
Confirmed structural waste suitable for behavior-preserving removal
Plausible material cost requiring measurement before change
Rejected speculative micro-optimization
```

For retained measurement items, name the exact operation, entry point, quantity/timing to measure, representative workload, and decision threshold qualitatively (for example, “retain only if it contributes materially to request latency or CPU under large histories”). Do not invent benchmark numbers.

- [ ] **Step 5: Rank bounded roadmap projects**

For every retained project include:

```text
Rank and title:
Boundary and owner:
Exact evidence:
Known maintained consumers:
Compatibility/reliability payoff:
Maintainability payoff:
Performance payoff or measurement gate:
Leverage:
Feasibility:
Regression risk:
Prerequisites:
Migration stages:
Non-goals:
Static verification:
Maintainer validation:
Success criteria:
Rollback criteria:
```

Apply the approved ranking priority before payoff/leverage/feasibility. Split any project too broad for one design.

- [ ] **Step 6: Select and fully specify one recommended next project**

Choose the highest-ranked project whose prerequisites are satisfied and whose first migration is bounded. Its report section must state exact files/symbols, all known consumers, scope, non-goals, compatibility requirements, stages, risks, static checks, maintainer validation, success criteria, and rollback. It must be ready to enter a separate brainstorming cycle without repository-wide rediscovery.

- [ ] **Step 7: Complete all final report sections**

Use `apply_patch` to add:

- `Executive Summary` at the beginning;
- finalized `Ranked Refactoring Roadmap`;
- `Recommended Next Project`;
- `Deferred, Superseded, and Rejected Ideas`;
- `Static Verification and Maintainer Validation Boundaries`.

Ensure the report now contains exactly all twelve required top-level sections. Consolidate duplicate subsystem observations into one cross-system finding where they share a real owner; otherwise keep them separate.

- [ ] **Step 8: Verify synthesis and commit**

```bash
rg -n '^## (Executive Summary|Scope, Exclusions, and Method|Current Architecture|Primary Cross-Layer Flows|Completed-Refactor Assessment|Evidence-Backed Findings|Compatibility and Reliability Risk Register|Performance Opportunities and Measurement Requirements|Ranked Refactoring Roadmap|Recommended Next Project|Deferred, Superseded, and Rejected Ideas|Static Verification and Maintainer Validation Boundaries)$' docs/superpowers/audits/2026-07-21-maintainability-architecture-refresh.md
rg -n 'TBD|TODO|FIXME|\[[A-Z][^]]*\]|implement later|fill in|similar to' docs/superpowers/audits/2026-07-21-maintainability-architecture-refresh.md
git diff --check -- docs/superpowers/audits/2026-07-21-maintainability-architecture-refresh.md
git add docs/superpowers/audits/2026-07-21-maintainability-architecture-refresh.md
git diff --cached --check
git commit -m "docs: rank refreshed SwarmUI refactoring roadmap"
```

Expected: first command returns exactly twelve headings; placeholder scan has no output; report-only commit.

### Task 7: Independently review and finalize the audit

**Files:**
- Review: `docs/superpowers/audits/2026-07-21-maintainability-architecture-refresh.md`
- Modify: the report only if review finds a verified defect
- Review: complete audit commit range from Task 1 through Task 6

- [ ] **Step 1: Run a source-attribution and consumer-completeness review**

Use `superpowers:requesting-code-review` with the report design as requirements. The reviewer must independently sample every finding and roadmap project against actual source, then deeply verify the recommended next project. Require exact file:line findings for unsupported claims, missed consumers, or misclassified compatibility boundaries.

- [ ] **Step 2: Run a ranking and performance-claims review**

Use a fresh reviewer to verify:

- compatibility/reliability actually outranks ownership, performance, and cosmetics;
- completed work is not accidentally reranked as pending;
- each performance claim has a static mechanism or measurement gate;
- no cosmetic inconsistency is described as a fault;
- broad directions are split into bounded design projects;
- roadmap dependencies and the recommended next project are internally consistent.

- [ ] **Step 3: Correct verified defects through the review loop**

For each accepted issue, use `superpowers:receiving-code-review`, verify it against source, update only the report with `apply_patch`, and ask the original reviewer to re-review. Do not accept suggestions that require production edits or speculative redesign.

- [ ] **Step 4: Run final static verification**

```bash
test "$(rg -c '^## (Executive Summary|Scope, Exclusions, and Method|Current Architecture|Primary Cross-Layer Flows|Completed-Refactor Assessment|Evidence-Backed Findings|Compatibility and Reliability Risk Register|Performance Opportunities and Measurement Requirements|Ranked Refactoring Roadmap|Recommended Next Project|Deferred, Superseded, and Rejected Ideas|Static Verification and Maintainer Validation Boundaries)$' docs/superpowers/audits/2026-07-21-maintainability-architecture-refresh.md)" = "12"
if rg -n 'TBD|TODO|FIXME|\[[A-Z][^]]*\]|implement later|fill in|similar to' docs/superpowers/audits/2026-07-21-maintainability-architecture-refresh.md; then exit 1; fi
git diff --check
git status --short --branch
git log --oneline --decorate -10
audit_first_commit=$(git log --reverse --format=%H -- docs/superpowers/audits/2026-07-21-maintainability-architecture-refresh.md | head -1)
audit_base_commit=$(git rev-parse "${audit_first_commit}^")
git diff --name-only "${audit_base_commit}"..HEAD
```

Expected: twelve required sections, no placeholders, clean documentation diff, only the audit report in the audit commit range, protected dirty files still unstaged, and backup directory untouched.

- [ ] **Step 5: Commit review corrections if any**

If and only if Step 3 changed the report:

```bash
git add docs/superpowers/audits/2026-07-21-maintainability-architecture-refresh.md
git diff --cached --check
git commit -m "docs: finalize architecture refresh audit"
```

Expected: report-only correction commit. If no correction was required, create no empty commit.

- [ ] **Step 6: Hand off the report for maintainer review**

Report the audit commit range, changed-file scope, static checks, review outcomes, top roadmap projects, and recommended next project. State explicitly that no production behavior changed and no runtime validation was performed or required for the audit itself. Ask the maintainer to approve or revise the report before beginning the recommended project’s separate brainstorming cycle.

## Completion criteria

- The current maintained repository, not the earlier report, is the factual authority.
- All in-scope subsystems and nine primary flows are represented.
- Every earlier roadmap item is reconciled.
- Every retained finding has exact evidence, calibrated certainty, consumers, compatibility constraints, a bounded owner, and validation requirements.
- Reliability risks are not conflated with maintainability concerns.
- Performance items are proven structurally or gated on explicit measurement.
- The roadmap follows the approved priority and dependency order.
- Every roadmap project is small enough for one later design.
- The recommended next project is implementation-design-ready.
- The report passes independent evidence and quality/ranking review.
- Only the report changes; protected state remains untouched.
