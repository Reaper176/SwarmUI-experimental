# Persisted-Output Filename-Selection Measurement Design

**Status:** Approved; implementation planning

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
publishing a reservation. Normal generated outputs perform that work while the
selected backend remains claimed. Image History add and Grid Generator share
the same save path, while request, intermediate-output, Grid, and user-setting
bypasses can return data URLs without running the scans.

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

Rank 24 is no longer the Recommended Next Project. Rank 25 becomes the sole
formal Recommended Next Project as a bounded measurement prerequisite. This
status change does not weaken any Rank 24 caveat concerning representative
models, workflow families, services/backends, optional dependencies, external
extensions, concurrency, platforms, filesystems, or performance.

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

- normal T2I output callbacks, while `T2IEngine.CreateImageTask` holds the
  selected backend claim;
- the later normal mini-grid, outside that engine backend claim;
- Image History add;
- Grid Generator iteration and final-grid output.

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
  or a normal generation backend claim.
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
- high-resolution timestamp conversion;
- the stable record prefix and schema version;
- privacy-safe scenario normalization;
- compact JSON serialization;
- nonthrowing emission through the existing logger.

The recorder is specific to Rank 25 and must not become a generic metrics
framework. Its emission path catches its own failures so diagnostics cannot
turn a successful save into a failure or mask an existing failure.

### Internal caller context

The existing public `Session.SaveImage` signature remains unchanged. An
internal context route identifies:

- normal generated output;
- normal mini-grid;
- Image History add;
- Grid Generator iteration output;
- Grid Generator final output;
- an otherwise direct/unknown caller.

The context also states whether synchronous selection runs while a backend
claim is known to be held. It contains no user, request, filename, or path
identity.

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
integers. Schema keys remain stable for the complete collection.

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
- directory-enumeration time;
- extensionless-hash construction time;
- reservation-key scan time;
- candidate-probe time;
- total synchronous selection time;
- success or the existing bounded error category.

Instrumentation may split the existing single-pass directory expression into
equivalent timed enumeration and hash-add operations only while measurement is
enabled. It must retain the same enumerated paths, extension stripping,
`HashSet<string>` semantics, and probe order. Disabled execution retains the
original direct path.

Reservation scanning retains the current key enumeration and short-circuit
collision result. Measurement counters and timestamps may be captured inside
the existing predicate; they must not perform a second collision scan.

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

## Data Flow

1. A maintained caller checks the existing save/bypass policy in its current
   order.
2. If measurement is enabled, it creates the minimal privacy-safe context and
   ID.
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

- Measurement collection and emission are best-effort and nonthrowing.
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
17. normal generated output under a backend claim;
18. normal mini-grid outside the engine backend claim;
19. Image History add;
20. Grid Generator iteration output;
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
- no second directory or reservation collision scan;
- no raw identity/path/request/exception-message field in the record schema;
- no log formatting or emission while `User.UserLock` is held;
- disabled guards dominate every measurement-only timestamp, ID, allocation,
  and record;
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

A `GO` decision does not authorize a cache or allocator. It authorizes a new
brainstorming/design cycle that must preserve external-file reconciliation,
cross-extension collisions, `[number]` and suffix semantics, multiple users,
unsafe-path policy, deletion races, all bypasses, and rollback to direct
enumeration.

## Instrumentation Removal

After the evidence and decision are recorded:

1. remove both temporary performance settings;
2. remove the internal recorder and record types;
3. remove every caller context and bypass hook;
4. restore the untimed direct `Session.SaveImage` flow;
5. statically verify that no Rank 25 symbol or log prefix remains;
6. record the final source projection and decision in the audit.

The final Rank 25 state retains documentation evidence but no production
measurement surface. If the result is `INSUFFICIENT`, a later collection attempt
requires a new bounded instrumentation plan rather than leaving dormant code.

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
