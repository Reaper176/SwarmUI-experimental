# Ranks 1–32 Full Validation Plan

**Execution status (2026-07-31):** Complete. All available checks were run;
the final disposition is 298 passed, 1 failed, with dependency-bound coverage
explicitly unrun. See the companion audit for the rank-31 Firefox failure.

> **For agentic workers:** REQUIRED SUB-SKILL: Use
> superpowers:executing-plans to execute this plan task-by-task. Steps use
> checkbox (`- [ ]`) syntax for tracking.

**Goal:** Rebuild current `master` and execute every safely runnable validation
contract for roadmap ranks 1–32 under Reaper176's one-time test/build override,
with explicit pass/fail/unrun accounting.

**Architecture:** Use the clean `validation/ranks-1-32` worktree only as pinned
source. Archive maintained source to `/tmp`, publish there, and keep every
fixture, database, browser profile, server data root, log, and harness under a
fresh `/tmp/swarmui-ranks-1-32-*` root. Combine exact static source contracts,
one synthetic C# harness, isolated HTTP/WebSocket/server smoke, and Firefox
browser checks; never infer unavailable Windows, provider, GPU, backend, or
external-extension outcomes.

**Tech Stack:** Git, .NET 8/C# 12, Bash, Node.js, Playwright/Firefox, LiteDB,
temporary local HTTP/WebSocket services, JSON evidence.

---

### Task 1: Freeze scope and environment

**Files:**
- Read: `AGENTS.md`
- Read: `docs/superpowers/audits/2026-07-21-maintainability-architecture-refresh.md`
- Read: the 32 matching design records under `docs/superpowers/specs/`
- Create externally: `/tmp/swarmui-ranks-1-32-*/environment.json`

- [ ] Record exact source commit, source-tree OID, OS, kernel, CPU, filesystem,
  .NET SDK/runtime, Node, Firefox, and Playwright versions.
- [ ] Inventory each rank's current source owner and historical validation
  matrix; map every case to `static`, `synthetic-runtime`, `live-local`, or
  `environment-unavailable` before execution.
- [ ] Confirm the validation worktree is clean and the primary dirty-state list
  is unchanged.

### Task 2: External clean build and launch smoke

**Files:**
- Source: current committed tree excluding `Data`, `Models`, `Output`,
  `src/Data`, `src/Extensions`, `src/bin`, and `src/obj`
- Create externally: `/tmp/swarmui-ranks-1-32-*/source/`
- Create externally: `/tmp/swarmui-ranks-1-32-*/publish/`

- [ ] Archive current maintained source to the external root.
- [ ] Run `dotnet publish ... -c Release` with restore, intermediate, output,
  and package paths below the external root; require exit 0, zero warnings, and
  zero errors.
- [ ] Launch the published server against a fresh external data root, wait for
  the local health/UI endpoint, request the root page and core static assets,
  then shut it down normally; retain stdout/stderr and response hashes.

### Task 3: Static contract suite for all ranks

**Files:**
- Create externally: `/tmp/swarmui-ranks-1-32-*/static-contracts.sh`
- Create externally: `/tmp/swarmui-ranks-1-32-*/static-results.json`

- [ ] Assert the exact production owner, required symbols/callers, public
  compatibility surface, and forbidden pre-fix pattern for ranks 1–24.
- [ ] Assert rank 1/2/S1 diagnostic redaction contains no raw submitted-payload
  serialization at the protected sinks.
- [ ] Assert ranks 3–23 retain their documented cookie, persistence,
  transaction, catalog, capability, cancellation, transport, cleanup,
  reservation, shutdown, ceiling, callback, renewal, OAuth, platform,
  fallback, fingerprint, and lazy-tab owners.
- [ ] Assert rank 24's editor facade/public declarations and six-stage delegate
  boundary remain present.
- [ ] Assert ranks 25–32 contain no temporary instrumentation tokens and their
  current production source equals the recorded post-removal projection.
- [ ] Require every static assertion to emit a rank, case, and pass/fail result.

### Task 4: Synthetic C# runtime suite

**Files:**
- Create externally: `/tmp/swarmui-ranks-1-32-*/csharp/Validation.csproj`
- Create externally: `/tmp/swarmui-ranks-1-32-*/csharp/Program.cs`
- Create externally: `/tmp/swarmui-ranks-1-32-*/csharp-results.json`

- [ ] Exercise ranks 3–7 transaction/cookie/catalog/workflow-store contracts
  with disposable settings, databases, workflows, and model roots.
- [ ] Exercise ranks 8–11 capability snapshot, download replacement/
  cancellation, streaming producer-fault, and model-load cleanup state
  transitions without a GPU or external network.
- [ ] Exercise ranks 12–16 reservation/delete/clear, atomic shutdown primitive,
  backend shutdown ownership, request-local ceiling predicates, and loading
  phase completion/failure isolation.
- [ ] Exercise ranks 19–22 OAuth tracker lifecycle, Linux autoscaling extension
  truth table, warm object-info fallback, and every sidecar add/edit/delete/
  invalid/unreadable/cache-mode contract.
- [ ] Exercise rank 24 graph-editor primitives and public-state parity for cases
  1–24, retaining cases 25–42 as unrun unless representative real workflows,
  models, nodes, and extensions exist in the isolated environment.
- [ ] Rerun current-production post-removal sanity harnesses for ranks 25–29 and
  32 against the new external publish, not their historical assemblies.

### Task 5: Firefox and local transport suite

**Files:**
- Create externally: `/tmp/swarmui-ranks-1-32-*/browser/validation.mjs`
- Create externally: `/tmp/swarmui-ranks-1-32-*/browser-results.json`

- [ ] Run rank 1 browser diagnostic redaction and UI error/callback checks with
  sentinel request values.
- [ ] Run rank 17 callback fault-isolation/order and startup-tail checks.
- [ ] Run rank 18 invalid-session WebSocket renewal, open/error handler,
  installer cleanup, and retry-socket cancellation checks.
- [ ] Run rank 23 lazy/eager descriptor order, permission, script/style,
  callback, translation, and failure-isolation checks.
- [ ] Rerun rank 30's uninstrumented image-history mapping/replacement browser
  sanity and rank 31's uninstrumented visible-layout browser sanity against the
  current external publish using Firefox where the harness is browser-neutral.

### Task 6: Environment-dependent matrix and honest disposition

**Files:**
- Create externally: `/tmp/swarmui-ranks-1-32-*/availability.json`

- [ ] Probe for Windows, configured OAuth credentials/provider reachability,
  multiple real Comfy backends with different node sets, GPU/model libraries,
  required representative workflows, and external extensions without reading
  protected user data.
- [ ] Run only cases whose dependencies can be supplied entirely by synthetic
  fixtures or already configured non-protected test resources.
- [ ] Mark every unavailable case `unrun` with its exact missing dependency;
  never convert an unavailable case into a pass.

### Task 7: Aggregate, verify, and report

**Files:**
- Create: `docs/superpowers/audits/2026-07-31-ranks-1-32-validation.md`
- Modify only if evidence requires correction:
  `docs/superpowers/audits/2026-07-21-maintainability-architecture-refresh.md`

- [ ] Aggregate per-rank passed, failed, and unrun counts with exact commands,
  logs, artifact hashes, environment, and limitations.
- [ ] Rerun the complete build/static/C#/browser command set from fresh output
  roots before any completion claim.
- [ ] Confirm current committed source was not modified, the validation branch
  contains documentation only, and the primary dirty-state list is unchanged.
- [ ] If any case fails, preserve evidence and report the failure without
  implementing a fix unless separately authorized.
