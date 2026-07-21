# SwarmUI Maintainability Architecture Refresh Design

## Goal

Reaudit the current SwarmUI maintained codebase and produce an evidence-backed, dependency-ordered refactoring roadmap that improves compatibility and reliability first, ownership and maintainability second, measurable performance third, and cosmetic consistency last.

The refresh starts from the repository after completion of the Image Editing coordinator extraction, T2I API capability split, image-history frontend decomposition, and Swarm-maintained Comfy class/input contract catalogs. The earlier architecture audit is a historical baseline, not current authority. Every retained finding must be revalidated against the present source.

The audit itself changes documentation only. Every production refactor remains a separately designed, reviewed, implemented, and manually validated project.

## Maintainer and Workspace Constraints

- The approved maintainer is Reaper176.
- Work occurs directly on the existing `master` checkout; the maintainer explicitly declined worktrees.
- Existing unrelated working-tree changes remain unstaged, unmodified, and outside audit scope.
- `Data.pre-restore-2026-07-19/` remains uninspected and untouched.
- Repository policy prohibits agents from running builds, tests, the live server, browser automation, or Comfy execution.
- Static parsers, linters, source inventories, call-site searches, Git history inspection, and read-only structural analysis are allowed.
- Audit conclusions must label anything that requires runtime evidence as a measurement or maintainer-validation requirement rather than a confirmed fact.

## Scope

The audit covers all SwarmUI-maintained core surfaces:

- root launchers, installation, update, and launch-tool code;
- Razor layout/page composition and inline browser integration;
- maintained JavaScript, CSS, browser globals, initialization, and script ordering;
- C# composition, lifecycle, settings, permissions, sessions, users, models, parameters, utilities, and Web APIs;
- backend registration, persistence, initialization, availability, scheduling, cancellation, autoscaling, and shutdown;
- built-in extensions and their lifecycle, API, frontend, parameter, backend, and workflow contributions;
- the ComfyUI adapter, object-info interpretation, workflow generation, transport, and maintained contract catalogs;
- maintained Python nodes under `src/BuiltinExtensions/ComfyUIBackend/ExtraNodes`;
- cross-layer serialization, string/JSON contracts, error propagation, logging, configuration, and extension compatibility;
- structurally evidenced performance costs such as repeated scans, synchronous work on critical paths, unnecessary serialization or graph rebuilding, excessive allocation, or lock contention.

The audit may inspect documentation and recent Git history when they explain intended contracts or completed migrations.

## Exclusions

The following are excluded except as read-only dependency context where repository rules permit:

- external extensions under `src/Extensions`;
- downloaded upstream repositories under `dlbackend` and `src/BuiltinExtensions/ComfyUIBackend/DLNodes`;
- generated API documentation and build artifacts;
- `.git`, `.vs`, `src/bin`, and `src/obj`;
- user data under `Data`, `Output`, and `Models`;
- the untracked pre-restore backup directory;
- vendored libraries whose internals are not maintained by SwarmUI.

No audit recommendation may require editing an excluded surface as its first migration step. If a maintained boundary depends on an excluded surface, that dependency becomes an explicit compatibility constraint.

## Priority Model

Findings and roadmap projects are ranked in this order:

1. Compatibility and reliability.
2. Clear ownership and maintainability.
3. Measurable or structurally demonstrable performance improvement.
4. Cosmetic consistency.

Within that order, each candidate is assessed qualitatively for:

- payoff: how materially it reduces faults, cognitive load, or proven cost;
- leverage: how many maintained flows and future changes benefit;
- feasibility: whether it can migrate incrementally behind existing surfaces;
- regression risk: especially serialized, extension-facing, concurrency, lifecycle, and ordering risks;
- prerequisites: contracts or seams that must be established first;
- reversibility: whether stages are independently reviewable and rollback-friendly.

High line count, high symbol count, stylistic age, or unconventional code is supporting evidence only. None is sufficient by itself to justify a refactor.

## Evidence Standard

Every retained finding must state:

- exact files and symbols;
- known maintained consumers and cross-layer callers;
- the current data and control flow;
- the concrete problem or risk;
- whether the conclusion is confirmed statically, strongly inferred, or requires runtime measurement;
- relevant existing project patterns;
- public, extension-facing, serialized, concurrency, lifecycle, registration-order, node-ID, script-order, or other compatibility contracts;
- a bounded boundary opportunity rather than a wholesale rewrite;
- staged migration and rollback considerations;
- permitted static verification and required maintainer validation.

Consumer counts and file metrics must include their search boundaries and exclusions. Findings based on literal or symbol searches must distinguish definitions, actual consumers, comments, documentation, generated content, and coincidental strings.

Performance findings require one of:

- a structurally unavoidable repeated operation visible in source;
- synchronous I/O or process work on a documented critical path;
- repeated serialization, parsing, scanning, graph rebuilding, or allocation whose multiplicity is traceable;
- a lock, queue, polling loop, or lifecycle interaction with a concrete contention or delay mechanism;
- maintainer-provided profiling or runtime measurements.

Speculative micro-optimization, assumed browser/runtime behavior, and file-size-based performance claims are rejected. Where static evidence establishes a candidate cost but not its material impact, the roadmap must require measurement before behavior changes.

## Architecture Inventory

The refresh records the current ownership and compatibility surfaces for:

- process composition and shutdown;
- extension discovery and lifecycle;
- Web/Razor composition and static asset loading;
- browser boot, session connection, lazy tabs, generation, editing, history, models, and shared utilities;
- API registration, reflection binding, permissions, sessions, and response transport;
- parameter registration, serialization, remapping, validation, and application;
- model registries, metadata, refresh, and file resolution;
- backend catalogs, configured instances, lifecycle, availability, claims, scheduling, and cancellation;
- generation orchestration from browser request through output/history updates;
- Comfy capability discovery, model lists, workflow generation, node contracts, transport, and output translation;
- maintained Python node registration and schema ownership;
- installer, updater, launcher, process, path, and platform-specific code.

The inventory identifies registries, mutable statics, singleton objects, extension hooks, compatibility facades, public fields, global browser declarations, inline handlers, serializers, background loops, locks, signals, and reserved ordering/ID schemes.

## Primary Flow Tracing

The audit traces at least these end-to-end flows:

1. Install/update and first launch.
2. Normal startup, extension phases, Web host creation, and shutdown/cancellation.
3. Browser layout loading, generation-page boot, lazy tabs, session reconnect, and extension asset contribution.
4. API request dispatch, permission/session handling, reflective binding, error conversion, and WebSocket/HTTP response paths.
5. Text-to-image request collection, parameter conversion, generation orchestration, backend acquisition, progress, completion, and discard paths.
6. Backend registration, configuration reload, availability monitoring, selection, model pressure/loading, request claims, cancellation, and removal.
7. Comfy object-info interpretation, workflow construction, submission, progress/output translation, and optional-node behavior.
8. Output naming, metadata, saving, history indexing, filtering, mutation, deletion/move, and browser refresh.
9. Extension registration of APIs, parameters, scripts, styles, tabs, backends, and workflow steps.

Each trace identifies ownership boundaries, shared state, error propagation, cancellation, serialization, and compatibility handoffs.

## Completed-Work Assessment

The earlier roadmap is reconciled explicitly. Each former item is marked:

- completed and validated;
- completed but leaving a documented follow-up seam;
- still valid with updated evidence;
- superseded by current architecture;
- rejected because its payoff or assumptions no longer hold.

At minimum, the refresh reassesses the completed Image Editing coordinator extraction, T2I API capability split, image-history controller/collaborators, Comfy capability catalog, Comfy node-name catalog, and Comfy input-name catalog. It verifies whether compatibility facades, globals, forwarders, catalogs, and ownership boundaries now match their designs and whether any planned follow-up is still justified.

Completed work is not reopened for cosmetic uniformity. A follow-up is retained only when current evidence shows a reliability, ownership, compatibility, or measurable-performance benefit.

## Finding Categories

The audit checks each subsystem for:

- unclear or overlapping ownership;
- broad mutable global/process state;
- hidden initialization, registration, script-load, or lifecycle ordering;
- duplicated class, route, input, event, setting, capability, or serialization contracts;
- unsafe compatibility assumptions across core, built-ins, Razor, browser globals, and extensions;
- concurrency, cancellation, lock-order, signal, disposal, and shutdown risks;
- inconsistent error classification, loss of context, swallowed failures, or user-hostile fallback behavior;
- unbounded caches, queues, registries, listeners, timers, or reconnect work;
- repeated scanning, parsing, serialization, filesystem, network, or process work on important paths;
- oversized owners where cohesive responsibilities and consumers can be proven;
- general utilities that mix pure logic with process or I/O side effects;
- public surfaces whose mutability or naming conflicts with established project patterns;
- documentation or repository guidance gaps that repeatedly cause unsafe changes.

Security-sensitive observations are reported when source evidence supports them, but the audit does not present itself as a complete security assessment.

## Roadmap Requirements

Each retained roadmap project contains:

- boundary and intended owner;
- exact evidence and known consumers;
- compatibility and reliability impact;
- maintainability and performance payoff;
- feasibility and regression risk;
- prerequisites and dependency ordering;
- staged migration through existing facades or public surfaces;
- explicit non-goals;
- static verification strategy;
- maintainer-run manual validation matrix;
- success and rollback criteria.

Independent subsystems become separate roadmap projects. A roadmap item must be small enough for one focused design specification. Large architectural directions, such as application services or workflow-generator decomposition, must be divided into prerequisite and first-extraction projects rather than proposed as one implementation.

The roadmap selects one recommended next project. It must offer high value under the priority model, have all known maintained consumers inventoried, preserve current compatibility surfaces, and be feasible as a bounded behavior-preserving migration.

## Deliverable

Create `docs/superpowers/audits/2026-07-21-maintainability-architecture-refresh.md` with:

1. Executive Summary.
2. Scope, Exclusions, and Method.
3. Current Architecture.
4. Primary Cross-Layer Flows.
5. Completed-Refactor Assessment.
6. Evidence-Backed Findings.
7. Compatibility and Reliability Risk Register.
8. Performance Opportunities and Measurement Requirements.
9. Ranked Refactoring Roadmap.
10. Recommended Next Project.
11. Deferred, Superseded, and Rejected Ideas.
12. Static Verification and Maintainer Validation Boundaries.

The report must distinguish current source facts from historical design intent. It should be detailed enough that the next project can enter brainstorming without repeating repository-wide discovery.

## Audit Execution Stages

### Stage 1: Baseline and Inventory

Record branch, protected working-tree state, maintained file inventory, language/subsystem size indicators, recent architectural commits, current public catalogs/facades, and the previous audit roadmap.

### Stage 2: Frontend and Web Composition

Inspect Razor, JavaScript, CSS, script/lazy loading, browser globals, session/reconnect behavior, feature ownership, inline handlers, extension contribution points, and evidenced client-side performance costs.

### Stage 3: Core Server and APIs

Inspect composition/lifecycle, extension management, Web/API dispatch, permissions, sessions/users, route groups, parameters, models, settings, utilities, error handling, and serialization boundaries.

### Stage 4: Backends and Generation

Inspect backend catalog/lifecycle/scheduling, concurrency and cancellation, generation orchestration, claims and disposal, model pressure/loading, autoscaling, output handling, and failure propagation.

### Stage 5: Comfy and Managed Python

Inspect capability/catalog boundaries, object-info, model lists, transport, workflow generator state and steps, maintained Python registration/schemas, optional modules, error paths, and graph-building performance candidates.

### Stage 6: Cross-System Synthesis

Trace retained candidates across all consumers, reconcile the earlier audit, remove unsupported findings, construct the risk register, identify measurement needs, rank the roadmap, and specify the recommended next project.

### Stage 7: Independent Review

Review the completed report independently for source accuracy, consumer completeness, compatibility omissions, performance overclaims, unsafe migration ordering, and consistency with the priority model. Correct verified defects before maintainer review.

## Error and Uncertainty Handling

- A missing or ambiguous consumer prevents a boundary from being presented as implementation-ready.
- Conflicting source evidence is documented rather than resolved through assumption.
- External-extension compatibility that cannot be inventoried statically is treated as a preservation constraint.
- Runtime-dependent performance, browser timing, concurrency frequency, or optional-backend behavior becomes a measurement/validation item.
- Findings that cannot name a bounded owner and migration seam are deferred as architectural directions, not ranked implementation projects.
- Any proposed change to launchers, persistence, permissions, serialization, public statics, browser globals, extension hooks, scheduling, or workflow IDs receives an explicit high-risk compatibility note.

## Non-Goals

- No production-code, configuration, launcher, schema, or user-data changes during the audit.
- No build, test, live-server, browser, Comfy, installer, updater, or backend execution by agents.
- No wholesale rewrite, framework migration, native-module conversion, dependency-injection conversion, or extension-API redesign.
- No speculative micro-optimization or style-only repository sweep.
- No automatic removal of compatibility facades, forwarding methods, public fields, browser globals, or legacy paths.
- No work inside external extensions or downloaded upstream repositories.
- No assumption that consistency means identical structure across subsystems with different runtime or compatibility needs.

## Static Verification

The audit is verified through:

- exact deliverable-section checks;
- source-path and symbol attribution review;
- consumer-boundary searches for every retained finding;
- comparison with current Git history and completed designs;
- clean documentation diff and protected working-tree audit;
- placeholder, contradiction, ambiguity, and unsupported-claim scans;
- confirmation that performance claims identify evidence or required measurement;
- confirmation that every roadmap item includes boundary, consumers, risks, prerequisites, migration, non-goals, validation, success, and rollback;
- independent specification/evidence and report-quality reviews.

## Maintainer Validation Boundary

The audit does not require the maintainer to exercise runtime behavior because it makes no production changes. Each future roadmap project, however, must include a concrete maintainer validation matrix proportionate to its affected flows. The recommended next project must identify that matrix before its design is considered implementation-ready.

## Success Criteria

- Every maintained subsystem in scope is inventoried and represented in the architecture map.
- Primary startup, browser, API, generation, backend, Comfy, output, and extension flows are traced across their ownership boundaries.
- The prior roadmap is fully reconciled with current source and completed refactors.
- Every retained finding has exact evidence, known consumers, calibrated certainty, compatibility constraints, and a bounded opportunity.
- Performance recommendations are evidence-backed or explicitly gated on measurement.
- The roadmap follows the approved priority order and dependency constraints.
- Each roadmap item is independently designable and safely staged.
- One next project is recommended with complete scope, consumers, non-goals, risks, migration, and validation requirements.
- No production or protected working-tree content changes during the audit.
- Independent review finds no unresolved source, compatibility, ranking, or overclaim defects.
