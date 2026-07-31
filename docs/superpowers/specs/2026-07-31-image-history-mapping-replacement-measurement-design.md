# Rank 30 Image-History Mapping and Replacement Measurement Design

**Date:** 2026-07-31
**Rank:** 30
**Status:** Approved for temporary instrumentation and collection
**Approved base:** `89681d5d83e8e7092aa967264add3a78d1ba48d8`

## Decision Authority

Maintainer Reaper176 authorized the agent to run this rank's bounded tests as a
one-time repository-policy override and directed the refactor to continue to
completion without further questions. The override permits temporary opt-in
instrumentation, a synthetic browser fixture outside the repository, headless
browser execution, static checks, and post-removal sanity. It does not permit
access to repository user data or changes under `Data`, `Models`, `Output`,
`src/Data`, `src/bin`, `src/obj`, downloaded backends, or external extensions.

## Question

Does client work after a `ListImages` response materially delay image-history
navigation, filtering, sorting, refresh, or fast-first background replacement at
the configured returned-history ceiling? The measurement separates:

1. response wait time and server-reported scan/sort time;
2. response mapping, client sort, and grid filtering;
3. synchronous `replaceBrowserContents` plus the production browser `build`
   path;
4. the delay through a two-animation-frame post-build settle point; and
5. observable main-thread long tasks covering response handling.

This rank does not assume that the linear passes are a problem. It measures the
existing implementation before authorizing any production optimization.

## Production Boundary

Temporary hooks may change only
`src/wwwroot/js/genpage/gentab/outputhistory.js`. They may observe
`ImageHistoryController.listFolderAndFiles`, `queueFullLoad`, and
`replaceBrowserContents`, but must not change requests, callbacks, mapped fields,
sort/filter order, browser ownership, rendering, selection, or token checks.

The final production projection must exactly match approved base. Only this
design, its execution plan, the audit disposition, and final evidence record may
remain.

## Preserved Contracts

- Foreground `loadToken` and background token/request-key rejection remain the
  sole stale-response authority.
- Root fast-first remains capped at 128 and schedules one full background load.
- Server-supported and client-only sort selection, reverse behavior, folder
  ordering, prefix construction, map fields, grid filtering, and callback order
  remain byte-for-byte behaviorally unchanged.
- `replaceBrowserContents` continues to publish `lastListCache` before calling
  the existing browser `build` method.
- Browser list/map identity, optimistic saved-image insertion, selected and
  multi-selected entries, scroll behavior, filter focus, update/built callback
  order, and unchanged-build skipping remain owned by existing code.
- No response filenames, paths, prompts, metadata, image bytes, or user values
  are written to measurement output.
- Recorder failure must never escape into image-history behavior.

## Temporary Recorder

Instrumentation is disabled unless local storage key
`image_history_measurement_enabled` is exactly `true`. Scenario text comes from
`image_history_measurement_scenario` but is accepted only when it matches the
fixed non-user pattern
`(desktop|mobile)_(32|128|512|1000)_(minimal|rich)_(thumbs|details)_(foreground|refresh|fast_first|background|control|contract)`;
every other value becomes `unspecified`. The recorder emits one JSON object per
completed foreground or background response with a stable
`[Rank30ImageHistory]` console prefix.

Each record contains only:

- schema version, scenario, flow, refresh/fast-first flags;
- depth, sort family, reverse/filter/grid flags;
- folder count, raw file count, mapped file count;
- request wait, map/filter/sort, synchronous replacement/build,
  response-handler start/end, and response-handler duration;
- server-reported total/directory/file/final-sort milliseconds when supplied;
- whether the synchronous response handler met the 50 ms long-task threshold;
  and
- long-task overlap count/duration joined later by the browser fixture's
  `PerformanceObserver`.

Timing uses `performance.now()`. Handler start/end use the same page time origin
as `PerformanceLongTaskTiming`. Hooks may add measurement calls around existing
statements but may not extract or reorder those statements. Record construction
and console publication occur after existing synchronous callback behavior and
are wrapped in a nonthrowing boundary. The fixture buffers console records and
long-task observer entries, waits for two animation frames followed by one
zero-delay task, then joins every intersecting interval. The same settle point
supplies an external post-build settle duration; it includes deferred page work
but is not described as paint, image-decode, or user-visible rendering time.

## Workload Matrix

The fixture runs a full externally published SwarmUI server from a clean `/tmp`
runtime root and opens the production Text2Image page in headless Chromium. It
loads the production HTML, scripts, styles, custom elements, image-history
controller, `GenPageBrowserClass.build`, card description, and deferred browser
owners. Generated non-user files live only below that temporary runtime's
output root. Authentication/bootstrap configuration and fixture generation are
recorded. If the full production page cannot be loaded, any rendering owner is
shimmed, or the image-history browser is not the production instance, the result
is inconclusive and authorizes no optimization.

Measured groups cover:

| Dimension | Values |
| --- | --- |
| Returned files | 32, 128, 512, 1,000 |
| Metadata | minimal, representative rich parameter/extra-data JSON |
| View | Thumbnails, Details List |
| Sort | server-supported Name, DateCreated, DateEdited; client Rating, Resolution, Model, Seed, FileSize |
| Reverse | false and true where applicable |
| Filter/grid | none; server-filter-shaped subset; hidden grid exclusion |
| Navigation | root, nested path, depth 1 and 3 |
| Flow | ordinary foreground, refresh, root fast-first, background full replace |
| Viewport | 1440x900 desktop and 390x844 mobile-sized viewport |

The matrix includes exact stale foreground and background response rejection,
unchanged-list replacement, single- and multi-selection preservation, scroll
restoration, filter-focus/selection restoration, update/built callback ordering,
optimistic insertion, and sequential repeat checks. Each performance group
receives five warmups and thirty recorded iterations. Server time is reported
separately so client cost is not inferred from request time. The mobile-sized
viewport establishes responsive-path behavior on the same desktop browser and
is not claimed as mobile-device performance.

## Evidence and Statistics

The evidence record includes fixture source and result hashes, browser and host
identity, source head, instrumentation head, record count, assertion count, and
all warnings or failures. Per group it reports median, p95, and maximum for map,
synchronous replacement/build, complete response handling, post-build settle,
server time, and observed long tasks. It
also reports the largest browser-heap directional delta when the browser exposes
the metric; this is directional only and not a retained-memory proof.

An instrumentation-disabled matched control brackets the same user action with
harness-owned `performance.now()` timestamps. Every group uses the same fixture
seed and a fresh page/state reset, then counterbalances enabled and disabled
blocks in `ABBA` order after identical warmups. Enabled and disabled samples are
therefore externally observable even though disabled mode emits no recorder
record. The difference is disclosed as measurement overhead/noise and is not
subtracted from individual records.

## Decision Rule

For the decision, a representative group is one exact combination whose source
response has 512 or 1,000 generated files, uses the full production page and
`GenPageBrowserClass.build` path, passes all parity assertions, and has thirty
measured samples after five warmups. Percentiles are calculated independently
within that exact combination. A phase share is the group's p95 phase duration
divided by the same group's p95 handler duration. A browser long task overlaps a
handler when the `PerformanceLongTaskTiming` interval and recorded handler
start/end interval intersect. Each threshold must hold in both chronological
fifteen-sample halves, and the two half p95 values must differ by no more than
25% of their larger value. The long-task condition additionally requires
overlap in at least three samples overall and at least one sample in each half.

Issue a scoped `GO` only when a representative group meets at least one
condition:

- response handling has p95 at or above 50 ms and at least three samples overlap
  observed browser long tasks;
- synchronous replacement/build has p95 at or above 35 ms and is at least 35% of the
  post-response client time; or
- mapping/client sorting has p95 at or above 20 ms and is at least 35% of the
  post-response client time.

The dominant measured phase controls the scope of any future design. A `GO`
authorizes only a separate design for that phase, with exact parity protections
for the preserved contracts above. A `NO-GO` authorizes no optimization. If the
fixture cannot exercise the production build path or parity assertions fail, the
result is inconclusive and authorizes no optimization.

## Removal and Closure

After independent source and evidence review, remove every key, recorder, and
hook. Confirm the final `src` tree OID exactly equals approved base, run a fresh
external-output static syntax check and browser sanity against uninstrumented
source, update the audit, and obtain final specification and quality approval
before local integration.
