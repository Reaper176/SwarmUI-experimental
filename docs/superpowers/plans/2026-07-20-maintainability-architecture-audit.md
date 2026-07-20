# Maintainability Architecture Audit Execution Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Produce a static, evidence-backed architecture audit and ranked refactoring roadmap for core SwarmUI, ending with one bounded recommended first refactor project.

**Architecture:** Inspect the repository boundary-first: establish the runtime map, analyze each maintained layer, trace representative cross-layer flows, and then synthesize findings using the approved ranking model. Store the durable result in one audit report; do not change runtime code while executing this plan.

**Tech Stack:** C# 12/.NET 8, Razor Pages, browser JavaScript/CSS, SwarmUI-managed ComfyUI Python nodes, Git, `rg`, `awk`, `sed`, and static source inspection.

---

## File Structure

- Read: `AGENTS.md` — repository constraints and maintained-code boundaries.
- Read: `docs/project-memory.md` — reusable architecture context; it may be empty and is not an audit deliverable.
- Read: `docs/superpowers/specs/2026-07-20-maintainability-architecture-audit-design.md` — approved scope, ranking model, and success criteria.
- Create: `docs/superpowers/audits/2026-07-20-maintainability-architecture-audit.md` — current architecture map, evidence, ranked roadmap, and recommended first project.
- Do not modify runtime source, generated files, external extensions, upstream code, or user-data paths.

## Execution Constraints

- Run no build, test, browser automation, server launch, package installation, formatter, or code generator.
- Treat `src/Extensions`, `dlbackend`, `src/BuiltinExtensions/ComfyUIBackend/DLNodes`, `src/bin`, `src/obj`, `.vs`, `.git`, `Data`, `Output`, and `Models` as excluded targets.
- Do not inspect `Data.pre-restore-2026-07-19/`; it is local data outside the audit.
- Preserve the existing modifications to `src/Data/Settings.fds`, `src/Pages/Text2Image.cshtml`, `src/wwwroot/js/genpage/gentab/loras.js`, and `src/wwwroot/js/genpage/main.js`.
- Use file size and symbol counts only as signals. A finding requires responsibility or dependency evidence from source.

### Task 1: Establish the Audit Baseline and Report Skeleton

**Files:**
- Read: `AGENTS.md`
- Read: `docs/project-memory.md`
- Read: `docs/superpowers/specs/2026-07-20-maintainability-architecture-audit-design.md`
- Create: `docs/superpowers/audits/2026-07-20-maintainability-architecture-audit.md`

- [ ] **Step 1: Reconfirm repository state and exclusions**

Run:

```bash
git status --short
git log -5 --oneline --decorate
git ls-files 'src/**' ':!src/Extensions/**' ':!src/bin/**' ':!src/obj/**' | wc -l
```

Expected: the known local changes remain visible; recent history includes the `SwarmUtil` migration; the tracked-source count is non-zero.

- [ ] **Step 2: Read the governing documents completely**

Run:

```bash
sed -n '1,360p' AGENTS.md
sed -n '1,260p' docs/project-memory.md
sed -n '1,260p' docs/superpowers/specs/2026-07-20-maintainability-architecture-audit-design.md
```

Expected: the audit scope and the prohibition on builds/tests are confirmed. An empty `project-memory.md` is acceptable and must be recorded as “no reusable notes available,” not treated as an error.

- [ ] **Step 3: Create the report with the final section structure**

Use `apply_patch` to create the report with these headings:

```markdown
# SwarmUI Maintainability Architecture Audit

## Executive Summary
## Scope and Method
## Current Architecture
### Browser Frontend
### C# Server
### Built-in Extensions and Managed Python
### Cross-Layer Data Flow
## Evidence-Backed Findings
## Ranked Refactoring Roadmap
## Recommended First Project
### Scope
### Non-Goals
### Known Consumers and Compatibility Requirements
### Migration Stages
### Risks
### Manual Validation
### Success Criteria
## Deferred and Rejected Refactors
```

Add the approved exclusions and ranking factors under `Scope and Method`. Do not add speculative findings yet.

- [ ] **Step 4: Verify only the report was introduced**

Run:

```bash
git status --short
git diff -- docs/superpowers/audits/2026-07-20-maintainability-architecture-audit.md
```

Expected: the new report is the only audit-created file; all pre-existing working-tree changes are unchanged.

### Task 2: Map the C# Server Boundaries and Pressure Points

**Files:**
- Read: `src/SwarmUI.csproj`
- Read: `src/Core/Program.cs`
- Read: `src/Core/WebServer.cs`
- Read: `src/Core/ExtensionsManager.cs`
- Read: `src/WebAPI/*.cs`
- Read: `src/Text2Image/*.cs`
- Read: `src/Backends/*.cs`
- Read: `src/Accounts/*.cs`
- Read: `src/Utils/*.cs`
- Modify: `docs/superpowers/audits/2026-07-20-maintainability-architecture-audit.md`

- [ ] **Step 1: Inventory namespaces, types, and oversized files**

Run:

```bash
git ls-files 'src/**/*.cs' ':!src/Extensions/**' ':!src/bin/**' ':!src/obj/**' | xargs wc -l | sort -nr | head -40
rg -n '^(namespace|public (static )?(class|interface|record|enum)|internal (static )?(class|interface|record|enum))' src --glob '*.cs' --glob '!src/Extensions/**' --glob '!src/bin/**' --glob '!src/obj/**'
```

Expected: a source-backed inventory showing the main server areas and high-complexity candidates, including Web API, generation, backend, core, and built-in-extension code.

- [ ] **Step 2: Identify global state and service-locator coupling**

Run:

```bash
rg -n 'public static|internal static|Program\.(Backends|Sessions|T2IModelSets|Extensions|ServerSettings|Web|GlobalProgramCancel|ModelRefreshEvent|Tick)' src --glob '*.cs' --glob '!src/Extensions/**' --glob '!src/bin/**' --glob '!src/obj/**'
rg -l 'Program\.' src --glob '*.cs' --glob '!src/Extensions/**' --glob '!src/bin/**' --glob '!src/obj/**' | sort
```

Expected: concrete consumers of process-wide state and events. Distinguish immutable utilities from mutable coordination state in the report.

- [ ] **Step 3: Trace startup, route registration, extension registration, and shutdown ownership**

Run:

```bash
rg -n 'Main\(|PrepExtensions|LoadExtensions|Register|Add.*Route|Map|WebServer|Shutdown\(|PreShutdownEvent|GlobalProgramCancel' src/Core src/WebAPI src/BuiltinExtensions --glob '*.cs' --glob '!**/DLNodes/**'
sed -n '1,260p' src/Core/WebServer.cs
rg -n 'APICall|APIKey|Register.*API|Add.*API|WebSocket|GenericDataHolder' src/WebAPI src/Core --glob '*.cs'
```

Expected: enough evidence to describe who creates core services, how API surfaces are registered, how built-in extensions connect, and where lifecycle responsibilities cross class boundaries.

- [ ] **Step 4: Inspect the highest-pressure C# candidates by responsibility**

Read the type and method outlines, then inspect only relevant ranges:

```bash
rg -n '^\s*(public|private|protected|internal) .+\(' src/WebAPI/T2IAPI.cs src/BuiltinExtensions/ComfyUIBackend/WorkflowGenerator.cs src/BuiltinExtensions/ComfyUIBackend/WorkflowGeneratorSteps.cs src/Utils/Utilities.cs src/Backends/BackendHandler.cs src/Text2Image/T2IParamTypes.cs
rg -n '^\s*#?region|TODO|HACK|legacy|compat' src/WebAPI/T2IAPI.cs src/BuiltinExtensions/ComfyUIBackend/WorkflowGenerator.cs src/Utils/Utilities.cs src/Backends/BackendHandler.cs src/Text2Image/T2IParamTypes.cs
```

Expected: responsibility clusters and dependency seams, not a conclusion based solely on line count.

- [ ] **Step 5: Add the C# architecture map and findings**

Update the report using `apply_patch`. Give every finding a heading that states its concrete boundary problem, then include:

```markdown
- Evidence: exact files, types, members, and dependency direction.
- Impact: the concrete maintenance cost or change amplification.
- Boundary opportunity: the responsibility and interface that could be isolated.
- Caveat: compatibility, lifecycle, or extension risk that constrains the refactor.
```

Expected: no generic “file too large” findings; each entry identifies ownership or coupling.

### Task 3: Map Frontend Ownership, Globals, and Load-Order Coupling

**Files:**
- Read: `src/Pages/Shared/_Layout.cshtml`
- Read: `src/Pages/Text2Image.cshtml`
- Read: `src/wwwroot/css/**/*.css`
- Read: `src/wwwroot/js/util.js`
- Read: `src/wwwroot/js/site.js`
- Read: `src/wwwroot/js/genpage/**/*.js`
- Modify: `docs/superpowers/audits/2026-07-20-maintainability-architecture-audit.md`

- [ ] **Step 1: Record the browser boot and script dependency order**

Run:

```bash
rg -n '<script|RenderSection|section Scripts' src/Pages/Shared/_Layout.cshtml src/Pages/Text2Image.cshtml
sed -n '1,220p' src/Pages/Text2Image.cshtml
rg -n 'DOMContentLoaded|window\.onload|addEventListener\(.?(load|DOMContentLoaded)|finalscript|sessionReady|loadInit' src/wwwroot/js --glob '*.js' --glob '!lib/**'
```

Expected: an explicit account of base scripts, generation-page scripts, dynamic loading, and final initialization order.

- [ ] **Step 2: Map stylesheet loading and browser ownership overlap**

Run:

```bash
rg -n '<link|css/' src/Pages/Shared/_Layout.cshtml src/Pages/Text2Image.cshtml src/Pages --glob '*.cshtml'
git ls-files 'src/wwwroot/css/**/*.css' | xargs wc -l | sort -nr | head -30
rg -n 'classList\.|\.className|\.style\.|setAttribute\(.?class' src/wwwroot/js --glob '*.js' --glob '!lib/**'
```

Expected: an account of base/page stylesheet ordering and areas where JavaScript owns presentation state across stylesheet boundaries. Treat dynamic class changes as evidence only after locating the corresponding maintained CSS selectors.

- [ ] **Step 3: Inventory maintained globals and compatibility namespaces**

Run:

```bash
rg -n '^(let|class|function) [A-Za-z_$][A-Za-z0-9_$]*|window\.[A-Za-z_$][A-Za-z0-9_$]*\s*=' src/wwwroot/js --glob '*.js' --glob '!lib/**'
rg -n 'SwarmUtil|window\.Swarm|globalThis\.|window\.' src/wwwroot/js src/Pages --glob '*.js' --glob '*.cshtml' --glob '!**/lib/**'
```

Expected: evidence separating deliberate compatibility exports from accidental global sharing. Note that classic scripts make top-level declarations cross-file dependencies even without `window.` assignments.

- [ ] **Step 4: Find responsibility clusters in the largest maintained frontend files**

Run:

```bash
git ls-files 'src/wwwroot/js/**/*.js' ':!src/wwwroot/js/lib/**' | xargs wc -l | sort -nr | head -30
rg -n '^(class|function) |^\s{4}(async )?[A-Za-z_$][A-Za-z0-9_$]*\(' src/wwwroot/js/genpage/gentab/currentimagehandler.js src/wwwroot/js/genpage/helpers/image_editor_tools.js src/wwwroot/js/genpage/helpers/image_editor.js src/wwwroot/js/genpage/gentab/outputhistory.js src/wwwroot/js/genpage/gentab/models.js src/wwwroot/js/genpage/gentab/params.js src/wwwroot/js/site.js src/wwwroot/js/util.js
rg -n 'TODO|HACK|legacy|compat|deprecated' src/wwwroot/js --glob '*.js' --glob '!lib/**'
```

Expected: named responsibility clusters and their collaborators, with size used only to prioritize inspection.

- [ ] **Step 5: Trace two representative frontend flows**

Trace generation submission and result display:

```bash
rg -n 'generate|Generate|doGenerate|makeWSRequest|genericRequest|append.*image|setCurrentImage|gotImage|outputHistory' src/wwwroot/js/genpage src/wwwroot/js/site.js src/wwwroot/js/util.js --glob '*.js'
```

Trace model/parameter selection:

```bash
rg -n 'model|Model|param|Param|loras|preset|refresh' src/wwwroot/js/genpage/gentab/models.js src/wwwroot/js/genpage/gentab/params.js src/wwwroot/js/genpage/gentab/loras.js src/wwwroot/js/genpage/gentab/presets.js src/wwwroot/js/genpage/main.js
```

Expected: callers, shared state, DOM ownership, server boundary, and initialization assumptions for both flows.

- [ ] **Step 6: Add the frontend architecture map and findings**

Update the report with the same evidence/impact/boundary/caveat structure used for C#. Explicitly record the ongoing `SwarmUtil` migration as current context, not as proof that all frontend globals should move into one namespace.

Expected: findings identify coherent ownership boundaries and migration seams, including relevant script-order compatibility.

### Task 4: Map Built-in Extension and Managed Python Boundaries

**Files:**
- Read: `src/BuiltinExtensions/**/*.cs`
- Read: `src/BuiltinExtensions/ComfyUIBackend/ExtraNodes/**/*.py`
- Read: `src/BuiltinExtensions/ComfyUIBackend/Assets/*.js`
- Modify: `docs/superpowers/audits/2026-07-20-maintainability-architecture-audit.md`

- [ ] **Step 1: Inventory built-in extension entry points and integration contracts**

Run:

```bash
find src/BuiltinExtensions -mindepth 1 -maxdepth 2 -type d -not -path '*/DLNodes*' | sort
rg -n 'class .*Extension|OnInit|OnPreInit|Register|Add.*API|Script|Asset|Workflow' src/BuiltinExtensions --glob '*.cs' --glob '!**/DLNodes/**'
```

Expected: a map of extension lifecycle hooks, route registration, frontend assets, and workflow integration.

- [ ] **Step 2: Inventory the maintained Python node surface without reading upstream code**

Run:

```bash
git ls-files 'src/BuiltinExtensions/ComfyUIBackend/ExtraNodes/**/*.py' | xargs wc -l | sort -nr
rg -n '^(class|def) |NODE_CLASS_MAPPINGS|NODE_DISPLAY_NAME_MAPPINGS|INPUT_TYPES|RETURN_TYPES|FUNCTION|CATEGORY' src/BuiltinExtensions/ComfyUIBackend/ExtraNodes --glob '*.py'
```

Expected: the Swarm-maintained node contracts and registration points. No command may target `dlbackend` or `DLNodes`.

- [ ] **Step 3: Trace the C# workflow-generator to Python-node contract**

Run:

```bash
rg -n 'Swarm|ExtraNodes|class_type|inputs|Workflow|Comfy' src/BuiltinExtensions/ComfyUIBackend --glob '*.cs' --glob '*.py' --glob '!**/DLNodes/**'
rg -n 'new JObject|JObject|JArray|CreateNode|AddNode|class_type' src/BuiltinExtensions/ComfyUIBackend/WorkflowGenerator*.cs
```

Expected: concrete string-based or JSON-based contracts, their owners, and places where changes must remain synchronized.

- [ ] **Step 4: Add built-in-extension findings**

Update the report with findings that affect core maintainability. Keep extension-local cleanup out of the roadmap unless it reveals a repeated core contract problem.

Expected: the report distinguishes stable extension seams from built-in code that bypasses or overloads those seams.

### Task 5: Trace Cross-Layer Flows and Validate Candidate Boundaries

**Files:**
- Read: files identified by Tasks 2–4
- Modify: `docs/superpowers/audits/2026-07-20-maintainability-architecture-audit.md`

- [ ] **Step 1: Trace request transport from browser to API registration**

Run:

```bash
rg -n 'genericRequest|makeWSRequest|fetch\(|WebSocket|API/' src/wwwroot/js --glob '*.js' --glob '!lib/**'
rg -n 'WebSocket|APICall|Register|route|Route|T2IAPI|Generate' src/WebAPI src/Core --glob '*.cs'
```

Expected: transport helpers, endpoint naming, dispatch ownership, session/auth context, and representative handler entry points.

- [ ] **Step 2: Trace generation orchestration end to end**

Run:

```bash
rg -n 'Generate|T2IParamInput|Backend|Workflow|Comfy|Output|Metadata|WebSocket' src/WebAPI/T2IAPI.cs src/Text2Image src/Backends src/BuiltinExtensions/ComfyUIBackend src/Utils/OutputMetadataTracker.cs --glob '*.cs' --glob '!**/DLNodes/**'
```

Expected: the ownership chain from API input through parameter handling, backend selection, workflow creation, result/metadata tracking, and browser notification.

- [ ] **Step 3: Test each candidate boundary against its consumers**

Start with exact-name searches for the cross-layer contracts already identified during baseline inspection:

```bash
rg -n 'Program\.(Backends|Sessions)|SwarmUtil|T2IAPI|NODE_CLASS_MAPPINGS' src --glob '!src/Extensions/**' --glob '!src/bin/**' --glob '!src/obj/**' --glob '!**/DLNodes/**'
```

Then repeat the same `rg -n` form for every additional source type, function, global, event, API route, JSON key, and node class that supports a prospective roadmap item. Use the literal name observed in Tasks 2–4 rather than a conceptual synonym. Record all consumers and remove any proposed boundary whose consumers were not inspected.

Expected: every retained roadmap candidate has a known consumer set and compatibility constraints.

- [ ] **Step 4: Complete the current architecture and cross-layer data-flow sections**

Describe runtime direction in prose using concrete owners:

```text
Razor/script boot -> browser state and UI owners -> request transport -> Web API dispatch -> generation/domain orchestration -> backend/workflow adapter -> managed Python node or upstream backend -> output/metadata path -> browser result owners
```

Expected: deviations from this direction are called out only where source evidence shows a dependency inversion or shared-state shortcut.

### Task 6: Rank the Refactoring Roadmap and Select the First Project

**Files:**
- Modify: `docs/superpowers/audits/2026-07-20-maintainability-architecture-audit.md`

- [ ] **Step 1: Consolidate overlapping findings into bounded projects**

Give each project an integer rank and concrete title. Under that heading, use this complete schema:

```markdown
- Boundary: responsibility to isolate and the interface exposed to consumers.
- Evidence: exact files, symbols, state, and call paths motivating the project.
- Payoff: High, Medium, or Low, with a one-sentence reason.
- Leverage: High, Medium, or Low, with named downstream areas.
- Feasibility: High, Medium, or Low, with the available compatibility seam.
- Risk: High, Medium, or Low, with the most credible regression mode.
- Prerequisites: earlier roadmap ranks, or “None.”
- Migration: ordered behavior-preserving stages.
- Manual validation: concrete maintainer workflows required after implementation.
```

Expected: cosmetic cleanup and duplicate entries are removed; each project is independently understandable.

- [ ] **Step 2: Order projects using the approved qualitative model**

Apply this decision order:

```text
1. Required prerequisite boundaries.
2. High maintainability payoff and high leverage.
3. Incremental compatibility feasibility.
4. Lower regression risk when earlier factors are comparable.
```

Expected: rankings are justified in prose; they are not presented as fake numeric precision.

- [ ] **Step 3: Expand the highest-ranked project into the recommendation**

Complete every subsection under `Recommended First Project`. The recommendation must:

- fit one subsequent implementation design;
- list all known consumers and contracts found in Task 5;
- separate scope from non-goals;
- split deeper change into reviewable migration stages;
- specify manual workflows the maintainer can run;
- define success in terms of reduced coupling or clearer ownership without behavior change.

Expected: another engineer can begin a focused brainstorming/design cycle without repeating the repository-wide audit.

- [ ] **Step 4: Record deferred and rejected refactors**

Explain why noteworthy candidates were deferred: insufficient evidence, low leverage, excessive risk, upstream ownership, external-extension scope, or dependence on a higher-ranked project.

Expected: absence from the roadmap is distinguishable from accidental omission.

### Task 7: Verify the Audit Against the Approved Specification

**Files:**
- Read: `docs/superpowers/specs/2026-07-20-maintainability-architecture-audit-design.md`
- Modify: `docs/superpowers/audits/2026-07-20-maintainability-architecture-audit.md`

- [ ] **Step 1: Check required report sections and forbidden placeholders**

Run:

```bash
rg -n '^## (Executive Summary|Scope and Method|Current Architecture|Evidence-Backed Findings|Ranked Refactoring Roadmap|Recommended First Project|Deferred and Rejected Refactors)$' docs/superpowers/audits/2026-07-20-maintainability-architecture-audit.md
! rg -n 'T[B]D|T[O]DO|F[I]XME|PLACEHOLD[E]R' docs/superpowers/audits/2026-07-20-maintainability-architecture-audit.md
```

Expected: all seven required level-two headings are present and the placeholder scan exits successfully with no matches.

- [ ] **Step 2: Check source attribution and roadmap completeness manually**

Read the report from top to bottom and verify:

```text
- Every finding cites exact maintained files and symbols.
- Every finding states impact, boundary opportunity, and caveat.
- Every roadmap project states boundary, evidence, payoff, leverage, feasibility, risk, prerequisites, migration, and manual validation.
- The recommended first project states scope, non-goals, consumers, compatibility, stages, risks, validation, and success criteria.
- No finding targets excluded code or user data.
- No claim depends only on file size.
- No recommendation assumes automated tests or builds are available.
```

Expected: fix every gap inline with `apply_patch`, then reread the changed section.

- [ ] **Step 3: Verify repository cleanliness relative to the audit**

Run:

```bash
git diff --check
git status --short
git diff -- docs/superpowers/audits/2026-07-20-maintainability-architecture-audit.md
```

Expected: no whitespace errors; only the audit report is new from this plan; the maintainer’s pre-existing changes remain present and unstaged.

- [ ] **Step 4: Commit only the completed audit report**

Run:

```bash
git add -- docs/superpowers/audits/2026-07-20-maintainability-architecture-audit.md
git diff --cached --name-only
git diff --cached --check
git commit -m "Document maintainability architecture audit"
```

Expected: the staged-file list contains exactly `docs/superpowers/audits/2026-07-20-maintainability-architecture-audit.md`; the commit succeeds without including existing local changes.
