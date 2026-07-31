# Rank 31 Visible-History Layout Measurement Plan

> Execute this plan under the maintainer's one-time self-testing override. Keep
> all generated build, runtime, fixture, browser-profile, trace, and evidence
> artifacts under `/tmp` and restore the exact approved source projection before
> integration.

**Goal:** Determine whether production image-history visible-window layout work
is material enough to justify a separate optimization design.

**Approved base:** `b34816189c4184c5072ed2199b31f1bee2a67cd0`

## Task 1: Approve the measurement contract

- Review the design against the exact window-manager implementation, build/tab
  owners, browser chunking, and the Rank 30 handoff.
- Correct scope, workload, contamination controls, gates, and parity contracts.
- Commit design and plan before source instrumentation.

## Task 2: Add temporary opt-in instrumentation

- Add one nonthrowing, privacy-safe recorder adjacent to
  `ImageHistoryWindowManager`.
- Time existing entry collection, row/layout construction, remaining work, and
  total without adding work inside the measured per-entry loops.
- Preserve exact control flow and return values.
- Run `node --check`, token/scope inventories, diff checks, and independent
  specification and quality review before collection.

## Task 3: Build the isolated production-page fixture

- Publish Release output with both output and intermediate paths under a fresh
  `/tmp` root.
- Assemble a complete runnable `/tmp` app with built-in extensions and an empty
  external-extension directory.
- Generate valid still and animated media plus sidecars below a fresh `/tmp`
  runtime root; never use repository user data.
- Load the full Text2Image page in headless Chromium and disclose unrelated
  backend-free errors.

## Task 4: Prove compatibility contracts

- Run the contract harness for event ownership/coalescing/fallback, row and
  buffer boundaries, hydration/dehydration/idempotence, disconnected/empty
  behavior, selection/current/filter/build preservation, background replacement,
  recorder privacy/gating/failure isolation, and exact public state.
- Treat any parity failure as blocking evidence, not as permission to adjust
  production behavior.

## Task 5: Collect and review the matrix

- Run five warmups plus `ABBA` fifteen-sample blocks for every exact group.
- Collect recorder records, long-task intervals, external action duration, and
  DevTools layout/style/task deltas without retaining response or DOM graphs.
- Hash all harnesses, fixtures, raw records, and summaries.
- Compute whole-group and chronological-half p50/p95/max statistics and apply
  the written gates exactly.
- Obtain independent evidence specification and quality review; rerun from
  scratch if either finds contamination or missing coverage.

## Task 6: Record the decision and remove instrumentation

- Record `GO`, `NO-GO`, or inconclusive with exact hashes, statistics, limits,
  and authorized scope.
- Revert all temporary source commits in reverse order and commit removal.
- Verify the final `src` tree OID equals approved base and all Rank 31 tokens are
  absent.

## Task 7: Verify closure and integrate locally

- Run a fresh external Release publish, static syntax checks, and an
  uninstrumented full-page browser sanity covering representative layout and
  hydration behavior.
- Update the audit and design with removal commit/tree/artifact evidence.
- Obtain final specification and quality approval.
- Fast-forward local `master`, confirm the pre-existing dirty user paths are
  unchanged, then remove the clean worktree and merged branch. Do not push.
