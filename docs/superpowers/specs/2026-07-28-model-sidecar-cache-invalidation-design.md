# Model Sidecar Cache Invalidation Design

**Status:** Implemented and maintainer-validated on Garuda Linux (Arch-based), Btrfs, using Firefox (version not provided)

**Date:** 2026-07-28

**Rank:** 22 — Invalidate model metadata cache when supported sidecars change

**Approved source/audit base:** `d2508564975c5ca149048e29f57e428dde6d96f2`

## Summary

`T2IModelHandler.LoadMetadata` caches file-derived model metadata in LiteDB. At the approved source/audit base, the cache used only the model file's last-write timestamp and one legacy `TextEncoders` condition to decide whether a record could be reused. Four ordered JSON sidecars—`.swarm.json`, `.json`, `.cm-info.json`, and `.civitai.info`—also contributed model class, title, description, trigger phrases, preview data, dimensions, and other metadata, but their existence and freshness were absent from that approved-base reuse decision.

Rank 22 adds a compatible ordered sidecar fingerprint to each `ModelMetadataStore` record. A record is reusable only when its model-file version and sidecar fingerprint both match the current filesystem state and the legacy condition does not invalidate it. The fingerprint uses normal filesystem identity signals: existence, byte length, and UTC last-write ticks for each supported suffix in the established order. This detects ordinary add, edit, and delete operations without reading and hashing every sidecar on every refresh.

The change is a cache-correctness fix, not a metadata-pipeline redesign. It preserves the existing recomputation, ordered merge, extraction, invalid-JSON, database, edit, download, duplicate-model, refresh, and consumer behavior.

## Current Boundary

The production owner is `T2IModelHandler` in `src/Text2Image/T2IModelHandler.cs`.

`T2IModelHandler.Refresh` recursively discovers models and calls `LoadMetadata` for each new `T2IModel`. `LoadMetadata`:

1. derives the central or per-folder LiteDB database and record ID;
2. reads a `ModelMetadataStore`;
3. rejects a legacy variable-text-encoder record when required;
4. reuses the record only when it exists, the legacy condition does not invalidate it, its `ModelFileVersion` matches the model file's last-write timestamp, and its non-null `ModelSidecarFingerprint` matches one current pre-read fingerprint capture;
5. on recomputation, reads embedded metadata where supported;
6. processes the four JSON suffixes in `AltModelMetadataJsonFileSuffixes`;
7. builds and upserts a new record; and
8. publishes the selected record into the newly discovered `T2IModel`.

`ResetMetadataFrom` is the maintained direct cache-update path used after application metadata edits and selected hash updates. It captures the current sidecar fingerprint into the in-memory metadata, then attempts to write that record into the same central or per-folder database.

The existing supported sidecar order is:

1. `.swarm.json`
2. `.json`
3. `.cm-info.json`
4. `.civitai.info`

`AllModelAttachedExtensions` also includes preview-image attachments. Rank 22 fingerprints only the four JSON metadata suffixes because preview freshness is not part of Core F12 or this approved project.

## Confirmed Defect at the Approved Base

Before Rank 22, when a cache record was present, its `ModelFileVersion` matched the model file, and the legacy `TextEncoders` condition did not apply, `LoadMetadata` skipped every sidecar existence check, read, parse, merge, and extraction step.

Therefore, at the approved base and with the model file unchanged:

- adding a supported sidecar could leave its metadata absent;
- editing a supported sidecar could leave old values visible; and
- deleting a supported sidecar could leave formerly derived values visible.

Those stale values could flow through model listing and description APIs, parameter data, prompt metadata, workflow/model-support decisions, remote serialization, viewing, and related model consumers. The approved-base defect was the missing cache-invalidation input, not a defect in those consumers; current production adds the fingerprint gate described above.

## Goals

- Detect ordinary add, edit, and delete changes for every supported JSON metadata sidecar without requiring a model-file timestamp change.
- Preserve the cache fast path when the model file and supported sidecars are unchanged.
- Upgrade old LiteDB records compatibly through one recomputation.
- Preserve existing sidecar merge order, extraction, parsing, and error behavior.
- Preserve central and per-folder metadata database modes and record IDs.
- Keep application edit, bulk edit, download, duplicate propagation, and refresh timing behavior unchanged.
- Ensure a sidecar change during recomputation cannot be falsely recorded as already incorporated.

## Non-Goals

- Hash sidecar contents on every refresh.
- Detect deliberately manipulated content changes that preserve sidecar existence, byte length, and modification time.
- Detect permission-only changes when the filesystem reports unchanged fingerprint inputs.
- Add a filesystem watcher.
- Redesign the metadata database, refresh pipeline, model catalog, or public API.
- Optimize the duplicate sidecar reads and parses described by Core P4.
- Add preview-image attachment freshness to the metadata-cache key.
- Change sidecar precedence, JSON error recovery, or model metadata limits.
- Change model-file hashing, embedded-header rewriting, or cache cleanup behavior.
- Make performance claims or measurements.

## Considered Approaches

### Ordered fingerprint in the existing record

Add one optional fingerprint property to `ModelMetadataStore`, compute the current ordered sidecar fingerprint from filesystem metadata, and include equality in the existing cache-reuse decision.

This is the selected approach. It directly repairs the missing freshness contract, persists across restarts, preserves the existing record/key owner, and keeps unchanged refreshes on bounded metadata-stat operations.

### Content hash on every refresh

Read and hash every existing sidecar before deciding whether the cache can be reused.

This would detect content changes that preserve size and timestamp, but it would require sidecar reads on every refresh and weaken the cache fast path. The approved contract does not justify that cost.

### Invalidate only from maintained application writers

Clear or update cache records when SwarmUI writes model metadata.

This is insufficient because external add, edit, and delete operations are part of the confirmed defect. It also would not establish correct freshness after restart without another persisted signal.

## Chosen Architecture

`ModelMetadataStore` gains one nullable string property for the sidecar fingerprint. LiteDB records written before Rank 22 lack the property and deserialize it as null.

`T2IModelHandler` gains one focused helper that accepts the established model sidecar prefix and returns a deterministic fingerprint. It visits `AltModelMetadataJsonFileSuffixes` in array order and emits one unambiguous entry per suffix:

- the suffix identity and an explicit missing marker when the sidecar does not exist; or
- the suffix identity, byte length, and UTC last-write ticks when it exists.

Numeric formatting and separators are culture-independent and stable across restarts. The all-missing state still produces a non-null fingerprint distinct from a legacy record.

No separate collection, public cache, watcher, or new lock is introduced.

## Cache Decision Flow

`LoadMetadata` derives the sidecar prefix using the same selected model path semantics as the existing sidecar readers. It computes the current fingerprint once before the cache-reuse decision.

A cached record is reusable only when all of the following remain true:

1. the record exists;
2. the legacy variable-text-encoder condition does not invalidate it;
3. `ModelFileVersion` equals the current model-file last-write timestamp; and
4. the stored sidecar fingerprint is non-null and equals the captured current fingerprint.

A legacy null fingerprint, a sidecar add/edit/delete, a model-file timestamp change, or the existing legacy condition enters the unchanged recomputation branch.

The current fingerprint is captured once and retained through that recomputation. The new record stores that exact pre-read value rather than restatting after parsing. This is intentionally conservative:

- if a sidecar changes after capture but before or during its read, the resulting record retains the earlier fingerprint;
- the next refresh observes the later filesystem fingerprint and recomputes again; and
- the code does not falsely assert that a concurrently changed sidecar was incorporated.

The normal cache-hit path performs filesystem metadata inspection only. It does not read or parse sidecar content.

## Recomputation and Publication

Rank 22 does not restructure the recomputation body. Embedded-header reads, both existing sidecar passes, merge order, `procAltHeader`, metadata limits, class identification, image validation, record construction, LiteDB upsert, and final model publication retain their current control flow.

The captured fingerprint is assigned only to the new record that is constructed after successful metadata processing. A sidecar read, parse, or conversion failure before record construction therefore does not attach or publish a new fingerprint.

Persistent upsert and in-memory publication retain their distinct existing error boundaries. If the caught LiteDB upsert fails, the prior persistent record and its old fingerprint can remain stored, while `LoadMetadata` continues with the newly constructed metadata and fingerprint and publishes them to `model.Metadata`. `HadNewError` may also dispose and delete the affected database after its existing error threshold, so a later refresh either reloads and reevaluates a retained old record or rebuilds after that cache reset; it does not treat the failed upsert as durable. Successful upsert is not a prerequisite for this existing in-memory publication path.

## Application Writer Interaction

`ResetMetadataFrom` captures the fingerprint currently visible for the model's selected sidecar prefix, assigns it to the existing in-memory metadata, and then attempts the LiteDB upsert. If that upsert fails, the existing log-and-throw path runs and the persistent record is not updated, while the already-mutated in-memory metadata may retain the captured fingerprint.

This preserves existing application timing:

- `EditModelMetadata` and `BulkEditModelMetadata` continue to update the model and LiteDB record before their deferred resave completes.
- With `EditMetadataWriteJSON` enabled, the later `.swarm.json` write changes the filesystem fingerprint. The next refresh recomputes from that written sidecar.
- With `EditMetadataWriteJSON` disabled, embedded metadata rewriting changes the model-file timestamp, retaining the existing model-file invalidation path.
- A download-provided `.swarm.json` remains visible to the first refresh of the downloaded model.
- Duplicate-model attachment propagation and `EditMetadataAcrossAllDups` behavior remain owned by the existing writer code.

No writer receives a new invalidation call, transaction, response delay, or exception policy.

## Error Handling

The fingerprint helper does not convert a surfaced filesystem inspection exception into cache reuse. Such a failure follows the existing `LoadMetadata`/scan error path.

When a changed sidecar is invalid JSON or cannot be read, the fingerprint mismatch enters recomputation and the existing sidecar read/parse exception behavior applies. Stale cached metadata is not returned as a successful refresh result.

If the sidecar is later corrected through an ordinary filesystem change, its fingerprint still differs from the last successful record and a later refresh can recover.

Filesystem APIs can report some inaccessible paths as nonexistent, matching the broad behavior of the existing `File.Exists` checks. Permission-only changes with otherwise unchanged fingerprint inputs are not promised as an invalidation signal.

No new catch, fallback, retry loop, log content, or user-facing error contract is introduced.

## Compatibility

The new public `ModelMetadataStore.ModelSidecarFingerprint` persistence property is additive and optional in stored LiteDB records. Existing records remain readable and recompute once because their fingerprint is null. Newer records remain readable by a rollback that ignores the extra property.

The following remain unchanged:

- `ModelMetadataStore.ModelName` identity;
- central versus per-folder database selection;
- database filenames, collection names, and cleanup behavior;
- `ModelFileVersion` and the legacy `TextEncoders` condition;
- all pre-existing public fields and methods, wire/model JSON shapes, API routes and payloads, permissions, and refresh signals; the additive public persistence property above is the sole public-member addition;
- suffix list, order, paths, merge precedence, extraction order, and invalid JSON behavior;
- `EditMetadataWriteJSON`, `EditMetadataAcrossAllDups`, and stray-attachment cleanup settings;
- single edit, bulk edit, download, rename, delete, and resave response timing;
- duplicate model-name selection and propagation behavior;
- model class, prompt, parameter, workflow, remote, viewing, and download consumers; and
- model-file and sidecar contents.

## Concurrency and Filesystem Semantics

Folder discovery remains parallel and LiteDB access retains `MetadataLock`. The fingerprint is local immutable calculation data for one `LoadMetadata` or `ResetMetadataFrom` invocation.

No attempt is made to lock external filesystem writers. Capturing the fingerprint before recomputation prevents the cache from recording a later sidecar state as consumed when the reads may have observed an earlier state. A normal subsequent refresh converges.

Within `ResetMetadataFrom`, the fingerprint capture textually precedes that method's own `ModificationLock` and `MetadataLock` statements, and Rank 22 introduces no lock or lock-order change. This does not mean the inspection is always outside `ModificationLock`: maintained `GetOrGenerateTensorHashSha256` and `ResaveModel` paths may call `ResetMetadataFrom` while already holding the existing reentrant modification lock.

The contract uses the timestamp and file-size resolution supplied by the active filesystem and .NET runtime. Changes that intentionally restore both values, or occur within a filesystem's indistinguishable metadata resolution while retaining the same byte length, are outside the approved bounded guarantee.

## Static Verification

Static verification must:

1. inventory every `ModelMetadataStore` construction, read, mutation, and upsert;
2. inventory every cache-reuse and legacy invalidation predicate;
3. inventory all `AltModelMetadataJsonFileSuffixes` reads and writers;
4. inventory `ResetMetadataFrom` and its maintained callers;
5. inventory central and per-folder database selection and record IDs;
6. inventory model refresh entry points and the selected model path/prefix construction;
7. prove the fingerprint contains exactly the four ordered suffixes and distinguishes missing, size, and UTC last-write ticks;
8. prove an all-missing fingerprint is non-null and legacy null records recompute;
9. prove one captured fingerprint controls both the decision and the successfully constructed record;
10. distinguish failure before record construction, caught persistent-upsert failure, and subsequent in-memory publication, including `ResetMetadataFrom`'s pre-upsert in-memory mutation;
11. prove unchanged fingerprints retain the existing cache-reuse path;
12. prove model-file and legacy invalidation remain effective;
13. prove sidecar merge, parsing, extraction, limits, and publication code are unchanged;
14. prove edit/download/duplicate writers retain their timing and ownership, and that maintained reentrant callers introduce no new lock or lock-order behavior;
15. prove model/API/parameter/prompt/workflow/remote consumers are unchanged;
16. prove Core P4's duplicate sidecar read/parse loops are not optimized or otherwise changed;
17. inspect the public/member and source-projection diffs;
18. run fixed-range whitespace checks; and
19. confirm the index and protected maintainer work remain isolated.

Agents perform static review only. Repository policy forbids agents from building, running tests, launching SwarmUI or its backends, executing launch scripts, automating browsers, calling live APIs, or performing platform/filesystem/runtime exercises.

### Implementation and Review Record

The approved source/audit base is `d2508564975c5ca149048e29f57e428dde6d96f2`, the approved design is `2048a2bb67e9c8da31233f729695e5d7469683fb`, the implementation plan is `e68ffce03535ff8d5948226e4a23d0c062a8cce6`, the production implementation is `207c595c01eefd27987159b770fc99ca7b26ae0d`, the post-production precision/quality correction is `ccb016228e071fc06ce09b364b7796be98511da3`, the post-implementation record is `77f8cf47d44afd6ebe94b1cbeb3e645ce5808db1`, and the implementation-record quality correction is `bd81cdaf15bcc3efb4d40519161a7c30ec8461f8`. The exact source projection from the approved source/audit base through the corrected documentation head changes only `src/Text2Image/T2IModelHandler.cs`, with `29 insertions(+), 2 deletions(-)` and resulting blob `1db9a1d609911cbfc98277904ca481635aa52aa5`; source is unchanged after the production commit.

Production adds the optional public persistence property `ModelSidecarFingerprint` to `ModelMetadataStore` and one handler-local helper. The helper emits exactly four ordered entries for `.swarm.json`, `.json`, `.cm-info.json`, and `.civitai.info`: each entry records either an explicit missing marker or file length plus UTC last-write ticks. `LoadMetadata` captures that fingerprint once before cache lookup/reuse and stores the same conservative pre-read value on a successfully constructed record; legacy null records, model timestamp changes, the legacy `TextEncoders` condition, and fingerprint mismatches recompute. `ResetMetadataFrom` captures the same selected-path fingerprint before its own lock statements and mutates the in-memory record before attempting the unchanged central/per-folder upsert. A caught `LoadMetadata` persistent-upsert failure can therefore leave the old durable record while the newly constructed record is still published in memory, but existing `HadNewError` threshold handling may instead dispose/delete that database; a later refresh reevaluates a retained old record or rebuilds after cache reset. A `ResetMetadataFrom` upsert failure retains its existing log-and-throw boundary after the prior in-memory mutation. Maintained hash/resave callers may already hold the reentrant `ModificationLock`, so the textual capture position does not establish a universal outside-lock guarantee.

Static design reviews returned `TASK1_SPEC_APPROVED` and `TASK1_QUALITY_APPROVED`; source reviews returned `SOURCE_SPEC_APPROVED` and `SOURCE_QUALITY_APPROVED`; post-implementation documentation reviews returned `TASK3_SPEC_APPROVED` and `TASK3_QUALITY_APPROVED`; validation-document reviews returned `VALIDATION_SPEC_APPROVED` and `VALIDATION_QUALITY_APPROVED`; and review-provenance documentation reviews returned `DOCS_SPEC_APPROVED` and `DOCS_QUALITY_APPROVED`. All ten approvals are current and have no findings after the post-production precision/quality and implementation-record quality corrections. Static review confirmed the nullable legacy upgrade, exact suffix order and invariant fields, central and per-folder record selection, unchanged merge/extraction/error flows, conservative capture semantics, unchanged wire/model JSON and API contracts, the single additive public persistence property, and the unchanged Core P4 duplicate read/parse loops. It does not establish runtime behavior, content-hash equivalence, preview freshness, watcher behavior, performance, permission-only invalidation, or detection of same-length changes whose UTC timestamp is unchanged or indistinguishable at the active filesystem's resolution. Invalid or unreadable changed sidecars retain the existing recomputation failure behavior rather than returning stale metadata as current.

Maintainer Reaper176 supplied the exact raw message `all passed on Garuda Linux (Arch-based), Btrfs, using firefox` on 2026-07-28. Under the controller's disclosed normalization against the exact unchanged 28-case matrix, capitalization is normalized to `All` and `Firefox`, and the outcome is 28 passed, 0 failed, and 0 unrun. No browser version was provided. Cache-mode and model/sidecar arrangement details were not separately provided; the record establishes that all exact matrix cases passed but does not attribute unreported per-case procedure, injected sidecar contents, duplicate topology, or other arrangement detail to the maintainer. Validation is limited to Garuda Linux (Arch-based), Btrfs, using Firefox (version not provided); other platforms, filesystems, browsers, and browser versions remain unvalidated. Agents performed static review only and claim no agent-run runtime, platform, filesystem, browser, cache-mode/arrangement, or performance result. Final integrated review remains pending because the prior attempt returned this review-provenance finding.

## Maintainer Validation Matrix

The maintainer performs runtime validation and records the date, operating system, filesystem, browser if applicable, cache mode, and model/sidecar arrangement actually exercised.

1. In central-cache mode, a cold load with no supported sidecars builds normal model metadata and persists a non-legacy all-missing fingerprint.
2. In central-cache mode, an unchanged refresh and restart preserve the same metadata and reuse the valid record without sidecar parsing.
3. A central-cache record created before Rank 22 recomputes once because its fingerprint is absent, then becomes reusable.
4. Cases 1 through 3 retain equivalent behavior in per-folder cache mode and use the existing folder-relative record ID.
5. Adding `.swarm.json` with the model-file timestamp unchanged invalidates the warm record and exposes the added metadata in both cache modes.
6. Editing `.swarm.json` with the model-file timestamp unchanged invalidates the warm record and replaces its derived metadata in both cache modes.
7. Deleting `.swarm.json` with the model-file timestamp unchanged invalidates the warm record and removes its contribution or restores the existing lower-priority fallback in both cache modes.
8. Adding `.json` with the model-file timestamp unchanged invalidates the warm record and exposes the added metadata in both cache modes.
9. Editing `.json` with the model-file timestamp unchanged invalidates the warm record and replaces its derived metadata in both cache modes.
10. Deleting `.json` with the model-file timestamp unchanged invalidates the warm record and removes its contribution or restores the existing lower-priority fallback in both cache modes.
11. Adding `.cm-info.json` with the model-file timestamp unchanged invalidates the warm record and exposes the added metadata in both cache modes.
12. Editing `.cm-info.json` with the model-file timestamp unchanged invalidates the warm record and replaces its derived metadata in both cache modes.
13. Deleting `.cm-info.json` with the model-file timestamp unchanged invalidates the warm record and removes its contribution or restores the existing lower-priority fallback in both cache modes.
14. Adding `.civitai.info` with the model-file timestamp unchanged invalidates the warm record and exposes the added metadata in both cache modes.
15. Editing `.civitai.info` with the model-file timestamp unchanged invalidates the warm record and replaces its derived metadata in both cache modes.
16. Deleting `.civitai.info` with the model-file timestamp unchanged invalidates the warm record and removes its contribution or restores the existing lower-priority fallback in both cache modes.
17. With multiple supported sidecars present and conflicting fields, refresh retains the established suffix merge and extraction precedence before and after one sidecar changes.
18. Replacing a valid warm sidecar with changed invalid JSON enters the existing recomputation failure path and does not return the stale record as current.
19. A sidecar whose fingerprint changed and which is then made unreadable enters the existing read failure path and does not return stale metadata as current.
20. Correcting the invalid or unreadable sidecar through a normal filesystem change allows a later refresh to recover and publish the corrected metadata.
21. A model-file timestamp change continues to invalidate an otherwise fingerprint-matching record.
22. `EditModelMetadata` with `EditMetadataWriteJSON` disabled retains immediate in-memory/cache behavior, embedded resave behavior, refresh behavior, and restart persistence.
23. `EditModelMetadata` with `EditMetadataWriteJSON` enabled retains immediate in-memory/cache behavior, deferred `.swarm.json` resave behavior, refresh behavior, and restart persistence.
24. `BulkEditModelMetadata` retains its result counts, deferred resave timing, metadata results, refresh behavior, and restart persistence with `EditMetadataWriteJSON` disabled and enabled.
25. A model download with supplied `.swarm.json` metadata exposes that metadata on its first refresh and after restart.
26. Exact-name duplicate model paths retain their current selected-path and `OtherPaths` behavior, and `EditMetadataAcrossAllDups` disabled and enabled retain their current attachment propagation.
27. After an external supported-sidecar change and refresh, model list/describe data, parameter data, prompt metadata, Comfy workflow/model-support behavior, remote serialization, and viewing observe the refreshed model metadata without API-shape changes.
28. Preview-image-only attachment changes, deliberate same-size/same-timestamp sidecar replacement, and Core P4 duplicate parsing retain their documented non-goal or unchanged status rather than being claimed fixed.

The matrix does not claim content-hash equivalence, permission-change detection, atomic snapshots across external writers, preview freshness, performance improvement, or behavior outside the recorded environment and arrangements.

## Success Criteria

Rank 22 succeeds when:

- every ordinary add, edit, or delete of a supported JSON sidecar changes the cache decision without requiring a model-file timestamp change;
- unchanged model and sidecar state retains record reuse;
- legacy records upgrade compatibly;
- changed invalid or unreadable sidecars do not silently return stale records as current;
- concurrent ordinary changes converge on a later refresh without a false-current fingerprint;
- central/per-folder modes and maintained writer/consumer behavior remain compatible;
- sidecar ordering and recomputation semantics are unchanged;
- Core P4 and all stated non-goals remain outside the production change; and
- static review and the maintainer matrix pass with no unexplained regression.

## Rollback

Rollback removes the fingerprint comparison, capture, and assignments together. The optional property may remain in LiteDB records without affecting older code, or may be removed from the C# model in the same rollback because LiteDB tolerates the extra stored field.

Rollback must not partially retain a legacy-null invalidation without persisting new fingerprints, because that would force every record to recompute indefinitely. It must not alter sidecar order, metadata records, application-written files, database keys, or model/API contracts.
