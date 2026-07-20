# Maintainability Architecture Audit Design

## Purpose

Audit core SwarmUI architecture and produce an evidence-backed, ranked refactoring roadmap. The audit prioritizes maintainability and clear module boundaries. It may recommend deeper architectural changes when their payoff justifies their migration risk.

## Scope

The audit covers the core code maintained in this repository:

- The browser frontend under `src/wwwroot/js`, its Razor page integration under `src/Pages`, and relevant CSS ownership boundaries.
- The C# server under `src`, including core services, Web API routes, generation logic, backend coordination, and built-in extensions.
- SwarmUI-managed Python nodes under `src/BuiltinExtensions/ComfyUIBackend/ExtraNodes` where they participate in cross-layer architecture.

The audit excludes:

- External extensions under `src/Extensions`.
- Downloaded upstream code under `dlbackend` and `src/BuiltinExtensions/ComfyUIBackend/DLNodes`.
- Generated paths such as `src/bin`, `src/obj`, `.vs`, and `.git`.
- Local user-data paths, including `Data`, `Output`, and `Models`.
- Vendored libraries except where their integration affects a maintained module boundary.

## Audit Method

Use a boundary-first static analysis in four passes:

1. Map the major runtime layers, initialization paths, script loading order, extension seams, and communication paths between browser, server, and Python backend.
2. Identify architectural pressure through global mutable state, implicit load-order dependencies, dependency-direction violations, mixed responsibilities, duplicated abstractions, oversized modules, and unclear ownership.
3. Trace representative call and data flows through the highest-pressure areas to distinguish structural problems from files that are merely large.
4. Group findings into coherent refactor projects and order them by payoff, prerequisites, regression risk, and the availability of safe compatibility seams.

File size and symbol counts are supporting indicators, not standalone reasons to refactor. Each roadmap item must cite concrete code evidence and explain the boundary it improves.

## Deliverables

The audit report will contain:

- A concise map of current architecture and dependency direction.
- Evidence-backed findings, including affected components and maintainability impact.
- A ranked roadmap of bounded refactor projects.
- For each project: intended boundary, expected payoff, dependencies, migration strategy, principal risks, and manual validation needs.
- A recommended first project with explicit scope, non-goals, and success criteria suitable for a separate implementation design.
- Cross-cutting recommendations only when they directly enable or de-risk ranked projects.

## Ranking Model

Rank projects using four qualitative factors:

- Maintainability payoff: reduction in coupling, ambiguity, duplication, or cognitive load.
- Leverage: number and importance of downstream areas improved by the new boundary.
- Feasibility: ability to migrate incrementally while preserving compatibility.
- Risk: likelihood and impact of behavior regressions, especially in generation workflows and extension integration.

The roadmap should favor prerequisite boundaries and high-leverage seams over cosmetic cleanup. A deeper redesign may rank highly, but its roadmap entry must split the migration into reviewable, behavior-preserving stages.

## Constraints

- Perform read-only static analysis for the audit itself.
- Do not run builds or tests; repository policy reserves live verification for the maintainer.
- Preserve all existing uncommitted work and do not inspect or modify local user-data content.
- Follow current repository conventions when evaluating feasible migration paths.
- Do not treat external extensions or upstream implementation details as refactoring targets.

## Success Criteria

The audit is complete when a maintainer can use the report to choose and scope the next refactor without repeating repository-wide discovery. The top recommendation must be small enough for one implementation design, identify all known consumers and compatibility requirements, and state how the maintainer can manually verify behavior.
