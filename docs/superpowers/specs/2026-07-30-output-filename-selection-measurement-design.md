# Persisted-Output Filename-Selection Measurement Design

**Status:** Measurement completed; NO-GO; instrumentation removed

**Date:** 2026-07-30

**Rank:** 25 — Measure persisted-output filename-selection cost

**Approved source/audit base:** `4b259c011fd4b5a0e93d998d0f78b5de01ed5e53`

## Summary

Rank 25 is a temporary measurement project for the persisted-output filename
selection path in `Session.SaveImage`. It is not an optimization or production
algorithm refactor.

The current path synchronously resolves the output path, waits for
`User.UserLock`, enumerates the target folder, builds an extensionless filename
set, scans process-lifetime reservation keys, and probes candidate names before
publishing a reservation. Normal generated-output and Grid iteration callbacks
may perform that work while a selected backend remains claimed, while the
object-tool early-output path can reach either source callback before backend
acquisition because both pass their save callback to the same public
`CreateImageTask`. Image History add and Grid Generator share the same save
path, while request, intermediate-output, Grid, and user-setting bypasses can
return data URLs without running the scans.

Static inspection confirms the repeated mechanisms but cannot establish their
materiality. Rank 25 therefore adds opt-in, privacy-safe structured diagnostic
records, has maintainer Reaper176 run the representative matrix, records a
qualitative `GO`, `NO-GO`, or `INSUFFICIENT` decision, and then removes all
instrumentation. A `GO` decision authorizes only a separate optimization
design.

## Rank 24 Disposition

Rank 24 remains implemented with 24 passed, 0 failed, and 18 unrun. Maintainer
Reaper176 supplied the exact instruction `mark as unrun and continue with the
refactor.` on 2026-07-30. That instruction separately dispositions cases 25–42
as deliberately unrun and closes the Rank 24 validation cycle without treating
any unrun case as passed, failed, executed, inferred, waived evidence, or full
maintainer validation.

At the approved pre-collection boundary, Rank 24 was no longer the Recommended
Next Project and Rank 25 became the sole formal Recommended Next Project as a
bounded measurement prerequisite. That historical status change did not weaken
any Rank 24 caveat concerning representative models, workflow families,
services/backends, optional dependencies, external extensions, concurrency,
platforms, filesystems, or performance. The final Rank 25 disposition and Rank
26 handoff are recorded below.

## Current Boundary

### Persisted path

`Session.SaveImage` currently:

1. returns a data URL immediately when `User.Settings.SaveFiles` is false;
2. resolves the output template, format, extension, target path, and folder;
3. waits for and enters `User.UserLock`;
4. creates the target directory;
5. enumerates every file in the target folder and builds an extensionless
   `HashSet<string>`;
6. probes the requested name and later suffixes while checking both the folder
   set and `RecentlyBlockedFilenames`;
7. publishes `StillSavingFiles` and starts the existing background save task;
8. releases the user lock and returns the persisted URL/path.

The background task separately waits for conversion, writes the primary media
and optional metadata, creates a preview, updates the history index, retains
pending bytes for ten seconds, and releases or expires the exact reservation.

### Consumers and bypasses

The maintained consumers are:

- normal T2I output callbacks, with dynamically observed backend-claim state;
- the later normal mini-grid, with backend-claim state false;
- Image History add, with backend-claim state false;
- Grid Generator iteration output, with dynamically observed backend-claim
  state;
- Grid Generator final-grid output, with backend-claim state false.

Both normal and Grid iteration callbacks can be reached from `GenerateLive`
lexically inside `using (backend)` or from the object-tool early-output path
before backend acquisition because both pass their save callback to the same
public `CreateImageTask`. Rank 25 records the actual lexical state rather than
assigning one static value to every output in either source category.

The maintained bypasses are:

- request `DoNotSave`;
- non-real output with `DoNotSaveIntermediates`;
- Grid Generator `DoNotSave`;
- user `SaveFiles = false` inside `Session.SaveImage`.

Rank 25 must distinguish these paths without changing their order or results.

## Goals

- Measure the synchronous filename-selection components and their scaling
  inputs for persisted outputs.
- Identify whether and where selection materially extends output publication
  or any selected backend claim.
- Keep background conversion, storage, preview, index, and retention work
  separate from synchronous selection.
- Record every data-URL bypass category so bypassed outputs are not counted as
  scan samples.
- Produce privacy-safe, machine-readable evidence with a stable schema.
- Preserve all save, collision, reservation, URL, error, and task behavior.
- Remove the complete measurement surface after the decision is recorded.

## Non-Goals

Rank 25 does not authorize:

- a directory cache, allocator, persisted snapshot, or history-index reuse;
- changes to naming, `[number]`, suffixes, extensions, collision handling, path
  cleaning, or unsafe-path policy;
- changes to `User.UserLock`, reservation locks, reservation ownership,
  `StillSavingFiles`, or `RecentlyBlockedFilenames`;
- changes to synchronous/background ordering, error returns, cleanup, preview,
  index, metadata, or ten-second retention behavior;
- changes to `Session.SaveImage`'s public signature or existing caller results;
- a general telemetry framework or instrumentation for ranks 26–32;
- raw usernames, request IDs, prompts, parameters, filenames, directories, or
  filesystem paths in measurement output;
- an optimization in the Rank 25 implementation;
- an invented numeric materiality threshold.

## Configuration

Two temporary fields are added to `ServerSettings.Performance`:

- `OutputFilenameMeasurementEnabled`, a Boolean defaulting to `false`;
- `OutputFilenameMeasurementScenario`, a string defaulting to empty.

The existing automatic server-settings surface exposes both fields. No new API
route, page, JavaScript module, permission, or persistence mechanism is added.

The scenario label is operator-supplied and privacy-safe. The documented format
combines storage classification and workload without a path or identity, for
example:

```text
btrfs-nvme/large-flat/repeated-name
```

When the Boolean is false, no measurement ID, timestamp, count, record, or
measurement-only allocation is created beyond the guarded setting checks.

## Measurement Architecture

### Internal recorder

One small internal recorder owns:

- the monotonic measurement ID;
- nonthrowing enable lookup and high-resolution timestamp conversion;
- the stable record prefix and schema version;
- privacy-safe scenario normalization;
- bounded media-category capture;
- compact JSON construction and serialization;
- nonthrowing emission through the existing logger.

The recorder is specific to Rank 25 and must not become a generic metrics
framework. Error isolation covers the enable check, timing, attempt allocation,
scenario normalization, media capture, record construction, serialization, and
logging. Each boundary catches its own failures without logging an exception
value, so diagnostics cannot turn a successful save into a failure or mask an
existing failure.

### Internal caller context

The existing public `Session.SaveImage` signature remains unchanged. An
internal context route identifies:

- normal generated output;
- normal mini-grid;
- Image History add;
- Grid Generator iteration output;
- Grid Generator final output;
- an otherwise direct/unknown caller.

The context also states whether synchronous selection runs while a selected
backend claim is known to be held. `T2IEngine.handleFileOutput` receives the
actual lexical claim state and, only while measurement is enabled, stores it in
one temporary internal `ImageOutput` marker before invoking the unchanged
public save callback. Normal and Grid iteration callers consume that dynamic
bit; mini-grid, Image History, Grid final, and direct contexts remain false.
No public `CreateImageTask` or callback signature changes. The context contains
no user, request, filename, or path identity.

Existing maintained callers use the internal route only when necessary to
classify the record. The public method preserves its current behavior and
remains available to extensions and already-compiled consumers.

### Correlation

When measurement is enabled, each attempted output receives one process-local
monotonic ID. The ID correlates bypass, selection, and background records. It
does not encode a user, request, path, or time and is not a stable identity
across restarts.

## Record Schema

Every record is one compact JSON object after a stable
`[Rank25OutputFilename]` log prefix. All durations use a monotonic
high-resolution clock and are emitted as integer microseconds. All counts use
integers. The schema contains 30 unique keys: 9 common, 14 selection, and 7
background. Schema keys remain stable for the complete collection.

Common fields are:

- `schema`;
- `record`;
- `measurement_id`;
- `scenario`;
- `source`;
- `backend_claimed`;
- `media_category`;
- `batch_size`;
- `outcome`.

`media_category` is exactly `image`, `animation`, `video`, `audio`, `text`, or
`unknown`, mapped by known `MediaMetaType` reference identity.
Extension-controlled `Name`/`ToString` is never emitted.

No exception message is included because it may contain a path. Failure
outcomes use bounded categories only.

### Bypass record

A `bypass` record states exactly one primary reason:

- `request_do_not_save`;
- `intermediate_policy`;
- `grid_do_not_save`;
- `user_save_files_disabled`.

If more than one policy applies, the record follows the existing branch
precedence rather than inventing a combined behavior.

### Selection record

A `selection` record contains:

- folder depth, without folder names;
- folder file count;
- reservation-key count at observation;
- reservation keys examined;
- reservation collision matches;
- candidate probe count;
- naming category: `number_token`, `suffix`, or `direct`;
- path-resolution time;
- user-lock wait and hold time;
- combined directory-enumeration and extensionless-hash time;
- reservation-key scan time;
- candidate-probe time;
- total synchronous selection time;
- success or the existing bounded error category.

Enabled instrumentation retains the existing streaming
`Directory.EnumerateFiles` → extensionless `Select` → `HashSet<string>`
pipeline. One outer timer covers that combined directory scan/hash operation,
and one inline counter records folder cardinality during the same traversal.
There is no intermediate O(n) array/copy or second traversal. Disabled execution
retains the original direct expression. Folder depth is computed only for an
enabled attempt with an explicit character loop rather than `Split`.

Disabled reservation scanning retains the original predicate with no measurement
closure or counter. Enabled scanning captures one `Keys` snapshot, times
snapshot acquisition plus the short-circuit `Any`, stops the timer immediately,
and then derives the key count and other bookkeeping from that same snapshot.
Neither branch performs a second collision scan.

`candidate_probe_us` includes `reservation_scan_us` whenever the candidate
reaches the reservation predicate, so those fields overlap and are not additive.

The record is emitted after `User.UserLock` is released so log formatting and
I/O are not included in lock hold time. Locals may carry the existing error
result out of the lock solely to permit post-lock emission; the returned tuple,
error log, and reservation cleanup remain identical.

### Background record

A correlated `background` record contains:

- conversion/`ActualFileTask` wait time;
- primary media write time;
- optional metadata write time;
- preview time;
- history-index time;
- fixed pending-byte retention time;
- total background time;
- bounded success/failure category.

The record does not move, await, combine, retry, or suppress any existing
operation. It observes the existing background task and is emitted from that
task without affecting reservation or `StillSavingFiles` cleanup.

Synchronous/background totals overlap their component timings, and background
emission follows cleanup. Totals and components are descriptive and must not be
summed as if they were disjoint intervals.

## Data Flow

1. A maintained caller checks the existing save/bypass policy in its current
   order.
2. If measurement is enabled, diagnostic-only batch-size lookup is locally
   caught with default `1`, then the caller creates the minimal privacy-safe
   context and ID.
3. A caller-side bypass emits one `bypass` record and returns the unchanged data
   URL.
4. A persisted caller enters `Session.SaveImage` through the internal context
   route.
5. A user-level no-save bypass emits `user_save_files_disabled` and returns the
   unchanged data URL.
6. Synchronous selection captures counts and timings around the existing
   operations.
7. The selection record is emitted after `User.UserLock` is released.
8. The existing background task emits its correlated background record after
   observing its stages.

No measurement record is required for an output rejected before any maintained
save/bypass branch is reached.

## Error and Concurrency Semantics

- Measurement collection and emission are end-to-end best-effort and
  nonthrowing, including enable lookup, timing, attempt/normalization/media
  capture, record construction, serialization, and logging.
- Diagnostic-only batch-size queries are caller-caught and default to `1`.
- Existing `ERROR` return behavior and existing save error messages are
  unchanged.
- Measurement records never include exception messages or raw path-bearing
  values.
- The global measurement ID uses atomic increment only when enabled.
- Records are independent immutable snapshots; no aggregate dictionary or
  cross-output mutable profile is retained.
- Counts over concurrent dictionaries are observational and may be approximate;
  the record describes the observed instant rather than a serialized global
  truth.
- No new lock is held while performing filesystem work or logging.
- No existing lock is widened.
- Concurrency, collision, deletion, and reservation behavior remain governed by
  the existing locks and exact-owner coordinator.

## Maintainer Collection Matrix

Repository policy reserves builds, launches, live server/backend work, browser
work, and performance collection to the maintainer. The agent supplies static
review and the exact collection instructions; maintainer Reaper176 performs the
live matrix and returns the extracted `[Rank25OutputFilename]` lines.

The matrix covers:

1. instrumentation disabled;
2. request `DoNotSave`;
3. intermediate-output no-save policy;
4. Grid Generator no-save;
5. user `SaveFiles = false`;
6. persisted output into an empty folder;
7. persisted output into a typical folder;
8. persisted output into a large flat folder;
9. persisted output into a deeply partitioned folder;
10. direct naming without collision;
11. `[number]` naming with collision;
12. suffix naming with collision;
13. repeated-name generation;
14. concurrent saves to the same target folder;
15. concurrent saves to different user/folder scopes;
16. delete then regenerate;
17. normal generated output with dynamically observed backend-claim state;
18. normal mini-grid outside the engine backend claim;
19. Image History add;
20. Grid Generator iteration output with dynamically observed backend-claim
    state;
21. Grid Generator final output;
22. representative single-output generation;
23. representative batch generation;
24. conversion-heavy output;
25. metadata/preview/index-enabled output.

Empty, typical, large, and partitioned folder counts are recorded from the
actual run rather than assigned invented universal sizes. The scenario label
records the operator's storage/workload classification for each group.

The maintainer also confirms:

- normal persisted files and URLs are unchanged;
- bypass outputs remain data URLs;
- collision suffixes and `[number]` results remain unchanged;
- concurrent saves do not overwrite;
- delete/regenerate behavior remains unchanged;
- background media, metadata, preview, and history index results remain
  unchanged;
- disabling instrumentation produces no Rank 25 records.

## Static Verification

Static verification must prove:

- exact definition and maintained caller inventory;
- unchanged public `Session.SaveImage` declaration;
- unchanged public reservation dictionaries and identities;
- unchanged path, extension, naming, probe, lock, publication, task, cleanup,
  return, and error order;
- one streaming directory scan/hash traversal with no intermediate O(n)
  materialization or second directory/reservation collision scan;
- no raw identity/path/request/exception-message field in the record schema;
- no log formatting or emission while `User.UserLock` is held;
- disabled guards dominate every measurement-only timestamp, ID, allocation,
  record, batch query, and temporary backend-claim marker mutation;
- every recorder entry point and timing/enable boundary catches failures and
  returns a false/zero/null/no-op fallback without exception logging;
- exactly one internal marker is set at `handleFileOutput`, with one pre-claim
  `false` call and two in-claim `true` calls, and no public callback signature
  change;
- exact temporary file scope;
- clean whitespace and no unrelated source changes.

Agents do not run builds, tests, launchers, servers, backends, browser
automation, or performance workloads for Rank 25 without a new exact
maintainer override.

## Decision Gate

Rank 25 produces one of three decisions:

- `GO`: filename selection is repeatedly material relative to output
  publication or backend release, scales with folder or reservation
  cardinality, and the records identify a bounded dominant component;
- `NO-GO`: selection remains minor, inconsistent, or dominated by conversion,
  storage, preview, index, or other background work;
- `INSUFFICIENT`: workload coverage or record quality cannot support either
  conclusion.

No fixed numeric threshold is invented. The decision compares the measured
component distribution, scaling behavior, lock contention, source context, and
the operator-observed workflow impact.

## Collection Evidence and Decision

The governing design commit is
`30448884415c44f446136fa3e11fb06cefe375d6`, and the plan commit is
`b90e5727ba7db1d8e1f7e50dafb34df32628e561`. The reviewed temporary source
range is
`30448884415c44f446136fa3e11fb06cefe375d6..42e8c3a127315e321f8845a92fc4235972880152`.
Its source projection is exactly:

- `src/Accounts/OutputFilenameSelectionMeasurement.cs`;
- `src/Accounts/Session.cs`;
- `src/BuiltinExtensions/GridGenerator/GridGeneratorExtension.cs`;
- `src/Core/Settings.cs`;
- `src/Text2Image/T2IEngine.cs`;
- `src/WebAPI/ImageHistoryAPI.cs`;
- `src/WebAPI/T2IAPI.cs`.

The path-filtered temporary instrumentation sequence is
`ad9c200ba075d716c8ce64f6fe62001454c714ef`,
`9940f6b6c2941a3f7ee30908490482c0150b0651`,
`b6aebaab9d8e09db44e039068e34507aab98ec9a`,
`0a65f7f1e154b6de41303a918a1f67c68a04757b`,
`e88cc1d93837dd3fe51cd695eb18ce70f885d1f1`,
`5d54370195e1fd6df9e8b17e1de605a555d43290`,
`63ecfe50e330bb9c99c19bc64e6835a2f39830e2`, and
`42e8c3a127315e321f8845a92fc4235972880152`.

Static review returned `RANK25_STATIC_SOURCE_GATE_PASSED`,
`RANK25_SOURCE_SPEC_APPROVED`, and `RANK25_SOURCE_QUALITY_APPROVED`. Under
maintainer Reaper176's exact one-time override, `user is authorizing you to do
these tests as a one time over ride of the repository rules.`, the agent built
the exact reviewed head outside the repository with zero warnings and zero
errors and ran the isolated collection. This is maintainer-authorized agent-run
evidence, not a maintainer-run result. The exact completion record is:

```text
Environment: Garuda Linux (Arch-based), Btrfs, storage classification named in the scenario labels
Cases: 23 passed, 0 failed, 2 unrun
RANK25_COLLECTION_COMPLETE
```

Case 3, intermediate-output no-save policy, was unrun because no known fixture
in the isolated one-model backend was proven to emit a non-real intermediate
output. Case 15, concurrent different user/folder scopes, was unrun because the
isolated authorization-disabled session could not establish a second real user
scope without expanding the collection boundary. Those cases are not inferred,
waived, or counted as passing. The disabled control produced a normal persisted
URL, a data URL for `DoNotSave`, and zero Rank 25 log records. Every other run
case preserved its expected URL/data-URL, collision, concurrency,
delete/regenerate, Grid, sidecar, preview, and history-index behavior.

The external privacy-reviewed evidence contains 73 schema-1 records: 34
selection records, their 34 correlated background records, and 5 bypass
records, spanning 39 unique measurement IDs. The external JSONL SHA-256 is
`7a9d23d7238a226e334e40baabb975716e414b975ea504a879dd1cfb9d839ccd`.
The field-name and string-value review found no raw username, request ID,
prompt, filename, filesystem path, or output-root value. The evidence remains
outside repository user-data and source paths.

All p50 and p95 values below use nearest-rank order statistics; for an even
sample count, p50 selects the `ceiling(0.50 * count)` ordered observation rather
than averaging the two middle observations. Across the 34 persisted selections,
synchronous selection had a 124 us p50, 2,157 us p95, and 2,667 us maximum.
`UserLock` wait had a 0 us p50, 116 us p95, and 210 us maximum; hold time had an
84 us p50, 1,357 us p95, and 2,128 us maximum. Directory scan/hash had a 26 us
p50, 102 us p95, and 2,019 us maximum; reservation scan had a 5 us p50, 24 us
p95, and 86 us maximum; candidate probe had a 10 us p50, 91 us p95, and
1,103 us maximum.
The probe interval includes reservation scanning when it runs, and neither
those components nor synchronous/background totals are additive.

The representative 25-file folder selected in 188 us, including 102 us for
directory scan/hash. The 5,000-file flat folder selected in 2,157 us, dominated
by its single 2,019 us directory scan/hash. Eight concurrent same-folder saves
returned eight unique URLs; their synchronous p50 was 84 us and maximum was
360 us, with lock-wait maximum 210 us and candidate-probe maximum 53 us.
Repeated suffix probes increased from one to five without exceeding 104 us in
that five-sample workload. Nine backend-claimed selections had a 158 us p50
and 381 us maximum. Twenty-five non-claimed selections had a 104 us p50;
their 2,667 us maximum includes the one-time empty-folder warm-up, while their
2,157 us upper-range sample is the 5,000-file scan.

For the 34 background records, conversion wait had a 28 us p50 and 28,097 us
p95, primary write had an 80 us p50 and 413 us p95, preview work had a
7,351 us p50 and 55,256 us p95, and history-index work had a 513 us p50
and 12,826 us p95. The maintained ten-second retention interval dominates the
background total by design and is reported separately rather than attributed to
filename selection.

The Rank 25 decision is `NO-GO`. Filename selection scaled with the 5,000-file
cardinality and identified directory scan/hash as a bounded dominant component,
but it was not repeatedly material: claimed paths remained below 0.4 ms in this
collection, the representative ordinary p50 was near 0.1 ms, and even the
large flat folder was about 2.2 ms while conversion, preview, and index work
regularly occupied larger ranges. No filename allocator, directory cache, or
other optimization is authorized by this result. Rank 25 completed with a
NO-GO decision and no optimization is authorized. Rank 26 becomes the sole
Recommended Next Project as the next bounded measurement prerequisite.

## Instrumentation Removal

Removal commit `ee4888f263d649d5888676610bc562ff6104d969`
deleted the recorder and restored every temporary setting, context, caller
hook, output marker, and timed `Session.SaveImage` branch. The complete
committed `src` tree at that removal commit has object ID
`26f65adf96afc130baa8b6fedba84b437d7163dc`, exactly matching the governing
design commit, so the final source projection relative to
`30448884415c44f446136fa3e11fb06cefe375d6` is empty.

The removal sequence returned `RANK25_REMOVAL_RED_CONFIRMED` before deletion and
`RANK25_INSTRUMENTATION_REMOVAL_STATIC_GATE_PASSED` after deletion. Under the
same exact one-time maintainer override, the agent then built the uninstrumented
removal commit outside the repository with zero warnings and zero errors. On
Garuda Linux (Arch-based), Btrfs, the isolated runtime identified commit
`ee4888f2`, returned one persisted PNG and one `DoNotSave` data URL, and emitted
no `Rank25OutputFilename` or `OutputFilenameMeasurement` record. The resulting
token was `RANK25_INSTRUMENTATION_REMOVAL_PASSED`; this remains
maintainer-authorized agent-run evidence rather than a maintainer-run result.
Startup also logged failed rebuild attempts for existing external extensions
against the external build-output layout, but those extensions are outside the
Rank 25 maintained-source projection and the maintained server/backend completed
both sanity calls.

The final Rank 25 state retains documentation evidence but no production
measurement surface.

## Rollback

Before collection, revert the temporary instrumentation commits in reverse
order. During collection, disable
`Performance.OutputFilenameMeasurementEnabled` for immediate operational
rollback. After collection, the planned removal commits are the normal final
state.

No rollback step modifies output data, user settings other than the temporary
measurement fields, or Rank 24 source.

## Success Criteria

Rank 25 succeeds when:

- Rank 24's 18 cases remain truthfully recorded as deliberately unrun and
  separately dispositioned;
- the temporary instrumentation passes static specification and quality review;
- maintainer Reaper176 completes or explicitly reports gaps in the exact
  collection matrix;
- the evidence supports a documented `GO`, `NO-GO`, or `INSUFFICIENT` result;
- no behavior or privacy regression is observed in the maintained matrix;
- all instrumentation is removed after the decision;
- the final audit names the evidence boundary and any authorized next design
  without claiming an unmeasured optimization or performance improvement.
