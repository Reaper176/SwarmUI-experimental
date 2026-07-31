# Rank 31 Visible-History Layout Measurement Design

**Date:** 2026-07-31
**Rank:** 31
**Status:** Approved for instrumentation
**Approved base:** `b34816189c4184c5072ed2199b31f1bee2a67cd0`

## Decision Authority

Maintainer Reaper176 authorized the agent to run bounded tests for the remaining
refactor ranks as a one-time repository-policy override and directed the work to
continue without further questions. Rank 31 may use temporary opt-in browser
instrumentation, external-output builds, a synthetic `/tmp` runtime, a local
server, and headless Chromium. It may not read or mutate repository user data or
write below `Data`, `Models`, `Output`, `src/Data`, `src/bin`, `src/obj`,
downloaded backends, or external extensions.

## Question

Does `ImageHistoryWindowManager.updateVisibleWindow()` consume a material part
of a browser frame as rendered image-history entry count grows? The method reads
layout for every rendered entry, constructs rows, scans both row boundaries, and
hydrates or dehydrates media after builds, scrolling, resizing, and History-tab
shows. This rank measures that existing behavior before authorizing any cached
geometry, observer, search, virtualization, or hydration-window redesign.

## Production Boundary

Temporary hooks may change only
`src/wwwroot/js/genpage/gentab/outputhistory.js`, within
`ImageHistoryWindowManager.updateVisibleWindow()` and one adjacent private
measurement owner. They may time the existing statements and publish fixed-shape
records after existing work. They must not change event registration,
request-animation-frame coalescing, entry or row construction, layout reads,
visible/keep boundaries, buffer constants, hydration/dehydration order, image
attributes, or the method's return value.

The final production projection must exactly match approved base. Only this
design, its execution plan, the audit disposition, and the final evidence record
may remain.

## Preserved Contracts

- `attach()` retains one scroll owner on the current content element and one
  window resize owner; attaching the same element queues rather than rewires.
- `queueUpdate()` coalesces while `updateQueued` is true, clears the flag before
  invoking the update, prefers one animation frame, and retains the 16 ms timer
  fallback.
- Disconnected, missing, and empty content remain no-ops.
- Entries remain direct children with `dataset.name`; row grouping continues to
  use `offsetTop` with the four-pixel tolerance and maximum row bottom.
- The visible range, ten-row keep buffer, and two-row unload hysteresis remain
  unchanged at both ends of the list.
- Hydration continues to remove lazyload/data-src and assign `src` from
  `data-orig-src`; dehydration restores lazyload/data-src and removes `src`.
- Existing selected, multi-selected, and current-entry DOM state, browser
  list/map/cache identity, build/built callback order, scroll position, filter
  focus/selection, responsive view, and root fast-first/background behavior are
  unchanged.
- No filename, path, prompt, metadata, media URL, image bytes, or user value is
  written to measurement output, and recorder failure never escapes.

## Temporary Recorder

Instrumentation is disabled unless local storage key
`image_history_layout_measurement_enabled` is exactly `true`. Scenario labels
must match a fixed allowlist assembled from viewport, rendered-entry bucket,
view, media, stable/dehydrated DOM state, trigger, and position; invalid labels
become `unspecified`. Each completed nonempty connected update emits one JSON
object with prefix
`[Rank31ImageHistoryLayout]` containing only:

- schema, fixed scenario, and trigger family;
- rendered entry and derived row counts;
- scroll top, client height, visible row count, and kept row count;
- monotonic update start/end timestamps;
- time for entry collection, row/layout construction, boundary scanning,
  hydration/dehydration traversal, and total synchronous update;
- whether total duration reached 50 ms.

Timing uses `performance.now()`. The total ends before record construction,
serialization, and console output. Measurement adds no extra per-entry work to
the timed loop. A browser harness independently observes hydrated/dehydrated
state and uses Chrome DevTools `Performance.getMetrics` immediately around each
action to record deltas for `LayoutCount`, `LayoutDuration`,
`RecalcStyleCount`, `RecalcStyleDuration`, and task duration. A
`PerformanceObserver` joins long tasks whose intervals intersect the recorded
update. A continuously sampled `requestAnimationFrame` timestamp series joins
each update to the interval from its containing frame callback to the next frame;
DevTools `TaskDuration` is not described as frame time. Recorder output, frame
intervals, and DevTools metrics are kept separate.

## Workload Matrix

The final fixture uses a fresh externally published SwarmUI server and synthetic
files below a `/tmp` runtime output root. It loads the production Text2Image
page, `GenPageBrowserClass`, real custom image cards, production History tab,
real browser build/chunk expansion, and the production window manager. The
matrix uses these applicable combinations rather than a Cartesian product:

| Exact group family | Viewport | Rendered entries | View/media/state | Trigger/position |
| --- | --- | ---: | --- | --- |
| D128 build | 1440x900 | 128 | thumbnails/still/dehydrated | initial build/top |
| D128 scroll | 1440x900 | 128 | thumbnails/still/stable | separate top, middle, bottom groups |
| D512 scroll | 1440x900 | 512 | thumbnails/still/stable | separate top, middle, bottom groups |
| D1000 scroll | 1440x900 | 1,000 | thumbnails/still/stable | separate top, middle, bottom groups |
| M1000 scroll | 390x844 | 1,000 | thumbnails/still/stable | separate top, middle, bottom groups |
| D1000 details | 1440x900 | 1,000 | Details List/still/stable | separate top, middle, bottom groups |
| D1000 animated | 1440x900 | 1,000 | thumbnails/animated GIF/dehydrated | separate top, middle, bottom groups |
| D1000 resize | 1440x900 ↔ 1024x720 | 1,000 | thumbnails/still/stable | resize/alternate |
| M1000 resize | 390x844 ↔ 844x390 | 1,000 | thumbnails/still/stable | resize/alternate |
| D1000 tab show | 1440x900 | 1,000 | thumbnails/still/dehydrated | tab show/top |
| D129 background | 1440x900 | production initial chunk | thumbnails/still/dehydrated | background/top |

The scenario allowlist and exact aggregation key include viewport, actual-entry
bucket, view, media, stable/dehydrated DOM state, trigger, and position; no
percentile mixes positions. Every sample also records or externally joins the
actual direct-child entry count, derived row count, and remaining section-loader
count. Initial and
root rebuilds naturally render the production initial chunk (129 entries when a
larger list crosses the `i > maxPreBuild` boundary) plus loaders. Section loaders
are expanded only through their production click handlers before later
512/1,000-entry scroll, resize, or tab updates. The root background group uses the
real 128-file fast-first response followed by the real full response. Animated
media are valid synthetic GIFs, not renamed still images. The mobile-sized
viewport exercises responsive layout in desktop Chromium and is not claimed as
mobile-device performance.

Each performance group receives five warmups and thirty enabled and thirty
disabled measured actions in counterbalanced `ABBA` blocks of fifteen. Enabled
and disabled actions use the identical harness fence: capture the pre-action
DevTools/frame state, trigger, await two animation frames followed by one
zero-delay task, drain performance observers, then capture the post-action state.
External elapsed means trigger through that completed fence; record arrival is
never the completion signal. Fresh pages bound retained fixture state. The
harness asserts exact entry order and
identity, row grouping, visible/keep range, hydration/dehydration state,
selection/current DOM preservation, scroll position, one update for a burst of
coalesced events, and one invocation when duplicate queue requests are absorbed.
Repeated unchanged positions belong to the queue/coalescing contract and are
not described as native scroll events.

`GenPageBrowserClass` also schedules `browserUtil.makeVisible()` on build, scroll,
and tab paths. Its separate animation-frame pass reads `getBoundingClientRect()`
for lazy elements, while image/animation loads and unknown aspect ratios can
reflow asynchronously. The harness reports this co-owner, keeps stable-layout
groups separate from dehydrated-DOM hydration groups, and performs all
post-settle parity/layout reads outside the manager's recorded interval.

Every warmup and sample establishes and verifies an equivalent pre-action state.
Stable groups use production hydration/build paths, wait for applicable media
load plus the common settle fence, and verify identical rendered-entry, row,
loader, hydration, and sampled-geometry state before timing. Dehydrated scroll or
tab groups use an unmeasured production scroll/update at a far-enough position to
dehydrate the target rows, then verify their exact attributes before the measured
trigger. Build and background groups perform a fresh production build/response
replacement per action and verify the new DOM prestate. No harness directly
rewrites media attributes. “Dehydrated” describes DOM hydration state only; it
does not claim a cold browser cache, network fetch, or decoder.

No harness layout read occurs after a measured trigger or while its manager
update is pending. Setup geometry checks finish before a quiescence fence, and
the measured trigger follows only afterward. For build and background samples,
the external pre-action fence is captured before replacement. Layout-derived
state is verified only from recorder values or after the common post-action
fence, never between DOM replacement and its queued manager update; pre-trigger
checks in that narrow window are limited to non-layout state.

A separate contract harness covers same-element reattach, content replacement
listener ownership, disconnected and empty no-ops, top/middle/bottom buffers,
hysteresis boundaries, already-hydrated and already-dehydrated idempotence,
missing media, four-pixel row tolerance, animation-frame coalescing, timer
fallback, recorder gating/privacy/failure isolation, tab-show ownership, build
callback order, filter focus, and root background replacement.
It also asserts the exact update return (`true` only when at least one entry is
newly hydrated), exact two-row deadband prior-state preservation, no mutation of
non-image or missing-`origSrc` children, and invocation-count-based coalescing for
connected, disconnected, and empty content.

## Statistics and Decision Rule

A representative group has at least 512 rendered entries, uses the full
production page and window manager, passes every parity assertion, and contains
thirty measured updates after warmup. Percentiles are calculated within one
exact group. Each gate must hold in both chronological fifteen-sample halves,
and half p95 values may differ by no more than 25% of the larger value. A long
task gate additionally requires at least three overlapping samples overall and
at least one in each half.

Issue a scoped `GO` only when one representative group meets at least one gate:

- synchronous `updateVisibleWindow()` p95 is at least 16.7 ms, consuming a full
  60 Hz frame budget;
- synchronous update p95 is at least 8 ms and the joined next-frame interval p95
  is at least 25 ms;
- recorded row/layout construction p95 is at least 4 ms, is at least 35% of the
  same group's p95 update, and update p95 is at least 8 ms; DevTools layout/style
  deltas are corroborative and are not added to the recorded phase;
- total update duration p95 reaches 50 ms with the required repeated long-task
  overlaps; or
- the 1,000-entry p95 is at least four times the matching D128 stable scroll p95
  for the same viewport, view, media, trigger, position, and DOM state, and is
  also at least 8 ms.

The enabled-minus-disabled externally timed difference is disclosed as
instrumentation/noise and is never subtracted. Layout/style counters are process
observations around one bounded action, not proof that every event belongs to the
window manager. Directional heap is optional and is not a retained-memory proof.

A `GO` authorizes only a separate design for the dominant measured phase and
must preserve every contract above. A `NO-GO` authorizes no production change.
Failed parity, missing production owners, uncontrollable unrelated page work, or
unstable halves makes the result inconclusive rather than a `GO`.

## Evidence Provenance

The final record includes approved base, design, plan, instrumentation,
correction, removal, and final documentation heads; browser and host identity;
publish and served-source hashes; fixture, contract harness, matrix harness,
raw-record, and summary hashes; exact record, action, assertion, failure, and
page-error counts; and every warning or limitation needed to reproduce or bound
the decision.

## Removal and Closure

After source and evidence review, remove every recorder, key, timestamp, and
hook. Confirm final `src` tree OID exactly equals approved base, run static syntax
and token inventories, perform a fresh external-output publish and uninstrumented
full-page browser sanity, update the audit, obtain final specification and
quality approval, and integrate only into local `master`.

Design reviews returned `RANK31_DESIGN_SPEC_APPROVED` and
`RANK31_DESIGN_QUALITY_APPROVED`.
