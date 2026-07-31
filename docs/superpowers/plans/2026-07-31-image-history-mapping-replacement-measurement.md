# Rank 30 Image-History Mapping and Replacement Measurement Plan

> Execute in the isolated Rank 30 worktree. Repository source is instrumented
> temporarily; every generated fixture, browser profile, and result stays under
> `/tmp`.

**Goal:** Determine whether full image-history response mapping, client sorting,
and browser replacement materially block representative foreground/background
interactions, then remove all instrumentation and record a bounded decision.

**Approved base:** `89681d5d83e8e7092aa967264add3a78d1ba48d8`

## 1. Freeze the boundary

- Record the clean worktree head and exact source paths.
- Inventory foreground/background entry points, mapping/sorting helpers,
  replacement/build ownership, token checks, selection, and optimistic insert.
- Commit this design and plan before source instrumentation.

## 2. Add nonthrowing opt-in instrumentation

- Add one private recorder in `outputhistory.js`, gated by exact local-storage
  keys and disabled by default.
- Wrap existing foreground and background response handling without moving
  behavior statements.
- Emit count-only context and separate request, map, synchronous build, handler,
  and server timings.
- Run static syntax and negative behavior-diff review.

## 3. Build the external browser fixture

- Publish SwarmUI into `/tmp`, launch it with a clean temporary runtime/output
  root, and open the full production Text2Image page in headless Chromium.
- Generate only synthetic history files below the temporary output root and
  exercise the production scripts, styles, custom elements, card descriptions,
  mapping/sorting, deferred owners, and `GenPageBrowserClass.build` through
  `replaceBrowserContents`. Fail inconclusive if any rendering owner is shimmed.
- Observe browser long tasks and validate exact output order/count/map identity,
  stale-response rejection, unchanged replacement, single/multi-selection,
  scroll/filter-focus restoration, callback ordering, fast-first/full, refresh,
  navigation/depth, and optimistic insertion contracts.

## 4. Collect the matrix

- Run desktop/mobile-sized viewport, sizes 32/128/512/1,000, minimal/rich
  metadata, Thumbnails/Details List, every supported/client sort family,
  reverse/filter/grid, ordinary/refresh/fast-first/background groups.
- Use five warmups and thirty measured iterations per performance group.
- Join recorder intervals to asynchronously delivered long-task entries after
  two animation frames and one zero-delay task, recording post-build settle.
- Run fresh-state, harness-timed enabled/disabled blocks in counterbalanced
  `ABBA` order with identical fixture seeds.
- Save JSONL and summary files under `/tmp`; hash every evidence artifact.

## 5. Review and decide

- Verify record completeness, repetitions, percentiles, server/client
  separation, long-task association, and parity assertions.
- Obtain independent specification and quality review of source and evidence.
- Apply the design's exact decision rule and record `GO`, `NO-GO`, or
  inconclusive without authorizing broader work.

## 6. Remove and prove rollback

- Delete the recorder, keys, and every hook.
- Prove final `src` tree identity against approved base.
- Run fresh external static syntax and uninstrumented browser sanity.
- Update the design and audit with evidence, limits, decision, removal commit,
  hashes, and the next ranked project.

## 7. Close locally

- Obtain final independent specification and quality approval.
- Fast-forward local `master`, verifying the maintainer's existing dirty paths
  are unchanged.
- Remove the clean Rank 30 worktree and branch, then begin Rank 31.
