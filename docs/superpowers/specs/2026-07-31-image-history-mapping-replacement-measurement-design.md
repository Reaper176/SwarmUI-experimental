# Rank 30 Image-History Mapping and Replacement Measurement Design

**Date:** 2026-07-31
**Rank:** 30
**Status:** Completed with `NO-GO`; instrumentation removed
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

## Collected Evidence and Decision

The final instrumented source head is
`6abb61efaa762971ec5cdfb0d2b0664139fb33ce`. Independent source reviews returned
`RANK30_SOURCE_SPEC_APPROVED` and `RANK30_SOURCE_QUALITY_APPROVED`. The fresh
external Release publish completed with 0 warnings and 0 errors under
`/tmp/swarmui-rank30-measurement-u41GXI`; `SwarmUI.dll` SHA-256 is
`2de3e885520fbe07ddc036958c3dcae35fb66dbc8b4ad3041e0bcbb7ff818a57` and the
published instrumented `outputhistory.js` SHA-256 is
`16712e6c47d06ad66def25396759a8242052e589392687a63c2ed2dae5dd96f1`.

The full temporary server loaded the production Text2Image page with 40 scripts
and nine stylesheets in headless Chromium 148.0.7778.97 on Garuda Linux. The
fixture contained 4,376 generated PNG files and 2,672 generated sidecars below
the `/tmp` runtime output root. The final counterbalanced matrix ran five warmups
per group and 660 measured actions across eleven groups. It passed 7,740 assertions
with 0 failures and emitted 360 schema-1 records. A separate full-page contract
harness passed 23 assertions with 0 failures.

Evidence artifacts are:

- `/tmp/rank30-records.jsonl`: 360 lines, 262,672 bytes, SHA-256
  `79657bbefe45014f6417bd42d2773337b8ad428fa5dd7ae6b4a8f75e03e74e81`;
- `/tmp/rank30-summary.json`: SHA-256
  `72a1227b2306d48f2934b90862d4120662743aa1f72370153667bf1d0d7ff435`;
- `/tmp/rank30-contract-summary.json`: SHA-256
  `544ab8c0886f4a4348a48e24f4b7ce37dbe9e6cc6646d1f1ef4132198dccdd09`;
- matrix harness: SHA-256
  `e4ae91706887ee4f9b50b0582f892a39a8bc3648b32b71c7d8ee73e55775fb2c`;
- contract harness: SHA-256
  `132561316eb2a9c2580ad232540ca847cb77b02a18050b01090b6514e60f5c2f`;
  and
- fixture generator: SHA-256
  `ab0d46e6c87859c1bf3cf4af93de87f22b3694e028ac62d4c51f545f83505742`;
  and
- varied-size fixture pass: SHA-256
  `aad306b55260aa722ba200e1265cc70f693e9b2422f39b4e3e9bdadd6490870d`.

Across every representative 512- and 1,000-file group, the largest whole-group
p95 handler, synchronous build, and mapping durations were respectively 29.7,
27.9, and 3.0 milliseconds. The 1,000-file rich Details/Rating group supplied
the handler and build maxima; its chronological halves were 38.4/26.8 ms handler,
33.8/24.9 ms build, and 4.6/2.0 ms map. The 3.0 ms mapping maximum came from the
1,000-file rich refresh/Seed group. No threshold held in either half of any
representative group, and every representative group had zero
long-task overlaps. No representative group reached 50 ms p95
handler, 35 ms p95 synchronous build, or 20 ms p95 mapping.

The externally timed enabled-minus-disabled p50 range was -3.7 to +2.7 ms across
all groups; this is disclosed noise/overhead and is not subtracted. Browser heap
directional deltas ranged from -2,002,606 to +11,885,149 bytes and are not a
retained-memory proof. The largest post-build settle p95 was 135.3 ms on the root
fast-first foreground response. Settle includes two animation frames and deferred
page work, is not a paint or image-decode measure, is not a Rank 30 gate, and is
separately relevant to Rank 31's visible-history layout measurement.

The full-page matrix confirmed exact complete output ordering across all eight
sort families on every measured action, including a 100-of-1,000 server-filtered
group and a FileSize group with 251 distinct byte sizes. It asserted every
recorder's raw and mapped counts,
including fast-first's 128/127 foreground and 1,000/999 background raw/mapped
records. The contract run separately confirmed stale foreground/background rejection,
unchanged-build skipping, single/multi-selection, scroll and filter
focus/selection restoration, callback order, optimistic insertion, invalid-label
privacy, recorder failure isolation, responsive-path selection, and public
list/cache identity. The backend-free temporary page also emitted the existing
`featureSetChangedCallbacks` reference error and missing `clip` tokenset errors;
they were not suppressed, did not prevent image-history initialization or any
recorded assertion, and limit this evidence to the measured image-history path.

The decision is **NO-GO**. Rank 30 authorizes no mapping, sorting, replacement,
incremental-rendering, virtualization, response, or other production change.
The observed post-build settle interval is not reinterpreted as synchronous
replacement cost; Rank 31 remains the independent measurement prerequisite.

Independent evidence reviews returned `RANK30_EVIDENCE_QUALITY_APPROVED` and
reconciled the final hashes, parity coverage, threshold arithmetic, and decision.
The specification review's sole remaining finding was the audit's provisional
removal wording; the actual removal and sanity evidence below closes it.

Removal commit `afcfa0d13da4998e86077f380e5cb3c8bc7d3db7` removes every
Rank 30 recorder, key, timer, and hook. Final `src` tree OID
`26f65adf96afc130baa8b6fedba84b437d7163dc` exactly equals approved base.
`node --check` passed and the instrumentation-token inventory is empty. The
fresh external Release publish under `/tmp/swarmui-rank30-post-IslTok` completed
with 0 warnings and 0 errors; `SwarmUI.dll` SHA-256 is
`306e68c523412c5c60422b01ff7a55249edb96bae6b78b5174983de3d1e622c2`
and served `outputhistory.js` SHA-256 is
`7925ff37427529e706c1b49945cfafc4ecb80e5302500e66596c9c27b34791b9`.
The uninstrumented full-page browser sanity passed 19 assertions with 0
failures across 1,000-file varied-size ordering, 100-file server filtering,
root fast-first/background replacement, grid exclusion, list/map/cache counts,
asset loading, and served-source token absence. Harness SHA-256 is
`b75f3a68d09a01d4c0d4712240354c100bf59533bc6c425cb4a7c482e2b963dd`.
The same existing backend-free page errors remained and no broader runtime claim
is made. Final closure reviews returned `RANK30_FINAL_SPEC_APPROVED` and
`RANK30_FINAL_QUALITY_APPROVED`.
