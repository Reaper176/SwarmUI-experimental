# Rank 31 Visible-History Layout Measurement Design

**Date:** 2026-07-31
**Rank:** 31
**Status:** Completed with scoped design-only `GO`; instrumentation removed
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
serialization, and console output. Publication is deferred until the following
animation frame and then a zero-delay task so it cannot enlarge the joined next
frame interval. Timer, scheduling, construction, serialization, and output
failures are suppressed and invalidate or omit the sample without changing the
production return. Measurement adds no extra per-entry work to the timed loop. A browser harness independently observes hydrated/dehydrated
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
| D1000 tab show | 1440x900 | 1,000 | thumbnails/still/stable | tab show/top |
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
DevTools/frame state, trigger, await three animation frames followed by one
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

## Collected Evidence and Decision

The final instrumented source head is
`c7e007e4ce2af6e0b6ea727fecc11cc16b255cc1`. The external Release publish under
`/tmp/swarmui-rank31-final-instrumented-U6HYra` completed with 0 warnings and 0
errors. `SwarmUI.dll` SHA-256 is
`bf85dde0f3ac030fa5c271e73619f051882310adc01fef917fd87ece5a73ff4f` and the
served instrumented `outputhistory.js` SHA-256 is
`7bac11f1df75c179b96b7e89189a5ba4815cb003a8ef4ac955229cb38d3588fb`.

The separate contract harness passed 51 assertions with 0 failures and emitted
two expected privacy/gating records. Its SHA-256 is
`921783c42380741aef2f1af760afa2bcaef54e099f0f60b4903a56d2c5ac7e7f`.
The final full-page matrix used Chrome 148.0.7778.97 on Linux
7.1.4-1-cachyos x64. Across 23 exact groups it ran five warmups per group and
1,380 measured actions in fifteen-action `ABBA` blocks. All 24,540 matrix
assertions passed. The 690 enabled actions emitted 720 schema-1 records: 690
selected primary records plus 30 expected fast-first companion records in the
background group. The 690 disabled actions emitted none.

Evidence artifacts are:

- `/tmp/rank31-records.jsonl`: 1,380 lines, 1,840,046 bytes, SHA-256
  `2cad8a857879143150c45bb82b889f238817d7512d7ae6a9391e14b26f6ec9f5`;
- `/tmp/rank31-summary.json`: 72,687 bytes, SHA-256
  `bb6235ba707e00435b4512274ecc449b12317a32486f679e840d6fd7d01a54a7`;
- matrix harness: SHA-256
  `7e710142e1d7ca8678a48134c493b463a99efcba73e100cade82911ad23e7893`;
- contract harness: SHA-256
  `921783c42380741aef2f1af760afa2bcaef54e099f0f60b4903a56d2c5ac7e7f`;
  and
- animated-fixture generator: SHA-256
  `11c06d96c7f3879101d8b135b7eab440f93e379bd5f6c38627faf6dff6a74758`.

The exercised still image SHA-256 was
`de9134969753d60a33a2b598c8ac6fc465ee7833d34722acd37f2641c7758b34`.
The valid two-frame GIF SHA-256 was
`e7eb226f5dddb97be2d34930392b65f34dca79ad851aea4ab1be3caf5fa0da65`.
The final fixture exposed exact 128- and 512-entry still folders and used
1,004-file still and animated source pools so the endpoint could return its
configured 1,000 entries after the two per-folder metadata database files were
excluded. `DateEdited` sorting was fixed for every group because the production
filename-sort scan limit is applied before extension filtering and otherwise
counts those database files. This is workload control, not a production fix or
a Rank 31 conclusion.

Nine representative groups met one or more approved gates in both
chronological halves with the gated metrics stable to the approved 25% bound:

- desktop 1,000-entry thumbnail middle scroll met the row/layout gate;
- desktop 1,000-entry thumbnail bottom scroll: total/row/joined-frame p95
  17.2/10.4/50.1 ms, meeting update-plus-frame, row/layout, and 1,000-to-128
  growth gates;
- mobile-sized 1,000-entry thumbnail middle and bottom scroll groups met the
  update-plus-frame and row/layout gates;
- desktop 1,000-entry Details List middle scroll met the row/layout gate;
- desktop 1,000-entry Details List bottom scroll: 22.9/16.2/66.7 ms, meeting
  the frame-budget, update-plus-frame, and row/layout gates;
- desktop 1,000-entry animated/dehydrated top scroll met the frame-budget gate;
  and
- desktop 1,000-entry animated/dehydrated middle and bottom scroll groups met
  the update-plus-frame gate.

The strongest row/layout group, Details List bottom, had half p95 totals of
23.5 and 22.9 ms and half row/layout p95 values of 16.7 and 16.2 ms. The
mobile-sized bottom group's half total p95 values were 16.6 and 17.5 ms, so it
does not claim the frame-budget gate. The `GO` does not rely on a long-task gate. Resize
and tab-show externally joined intervals were large while the recorded manager
update stayed below the synchronous gates; DevTools counters and external time
remain disclosed process/action observations and are not added to manager time.

The decision is a scoped **GO** for a separate design addressing repeated
entry-layout reads and row reconstruction inside
`ImageHistoryWindowManager.updateVisibleWindow()`. It does not authorize
virtualization, different hydration buffers, event-owner changes, changed
selection/current behavior, or any production change in this measurement rank.
Rank 32 remains the final numbered measurement prerequisite.

The backend-free temporary page emitted 1,035 instances of two existing error
kinds across 115 fresh pages: `featureSetChangedCallbacks` was undefined and the
`clip` tokenset was absent. Neither prevented history initialization, any
production owner, recorder delivery, or a parity assertion. The mobile-sized
groups are responsive desktop Chromium, not mobile-device performance. No real
user history, ordinary generation/backend work, GPU, Firefox/Safari, mobile
device, network filesystem, other platform/filesystem, paint/decode, retained
memory, or production-frequency conclusion is claimed.

The final protocol correction computes every scroll target before a last
three-animation-frame-plus-task quiescence fence and passes only the scalar
target into the measured dispatch, so the trigger performs no harness layout
read. Build and background checks compare exact rendered prefixes against the
production list and assert explicit selection/current/checkbox outcomes rather
than exempting replacement flows. Eight resize actions recorded
`focus_preserved=false`: DevTools viewport emulation itself blurred the input.
This is disclosed as protocol-side behavior and is not attributed to the window
manager; the contract harness separately proves that a direct manager update
preserves the production filter input's focus and selection. Initial background
load correctly begins without an invented user-focus prestate.

## Evidence Provenance

The final record includes approved base, design, plan, instrumentation,
correction, removal, and final documentation heads; browser and host identity;
publish and served-source hashes; fixture, contract harness, matrix harness,
raw-record, and summary hashes; exact record, action, assertion, failure, and
page-error counts; and every warning or limitation needed to reproduce or bound
the decision.

## Removal and Closure

Independent final evidence reviews returned `RANK31_FINAL_SPEC_APPROVED` and
`RANK31_FINAL_QUALITY_APPROVED`. Removal commit
`831949065723e29b0ce43e6324c66e59c7ddb485` removed every recorder, key,
timestamp, and hook. Its `src` tree OID is
`26f65adf96afc130baa8b6fedba84b437d7163dc`, exactly equal to approved base
`b34816189c4184c5072ed2199b31f1bee2a67cd0`; `node --check` passed, every Rank
31 instrumentation token was absent, and neither `src/bin` nor `src/obj`
existed.

The fresh external post-removal Release publish under
`/tmp/swarmui-rank31-post-Mu95G6` completed with 0 warnings and 0 errors.
`SwarmUI.dll` SHA-256 is
`afd94e4dd7e44f40275c4886c28680a8c3f4f0e8557527cf7c4bd0a8707f523e` and
the served uninstrumented `outputhistory.js` SHA-256 is
`7925ff37427529e706c1b49945cfafc4ecb80e5302500e66596c9c27b34791b9`.
The uninstrumented full-page browser sanity passed 16 assertions with 0
failures while covering served-token absence, assets, exact 1,000-entry
list/map/cache/DOM order and identity, top/middle/bottom hydration,
invocation/coalescing/tab paths, and root fast-first/background prefix,
chunking, and identity. It observed the same nine bounded backend-free page
errors. Harness SHA-256 is
`fcdeedfb215d6bf711cd9d2588bd0e03e2a0a35e0ef75901aaf537d1fbaab16a`.

The maintainer's one-time override authorized these external builds, temporary
local server, synthetic image-history output, headless Chromium collection,
and post-removal sanity. It did not authorize repository user-data access or
establish real-user-history, ordinary backend/generation, GPU,
Firefox/Safari/mobile-device, network-filesystem, production-frequency/GC, or
other-platform behavior. No production optimization was implemented.

Design reviews returned `RANK31_DESIGN_SPEC_APPROVED` and
`RANK31_DESIGN_QUALITY_APPROVED`.
