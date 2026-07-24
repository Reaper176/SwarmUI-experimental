# Durable Comfy Workflow Transactions Design

**Date:** 2026-07-23

**Status:** Designed; awaiting implementation planning

## Goal

Make stored Comfy workflow save, overwrite, rename, and deletion durable before their results are published to maintained readers. A failed request must leave the prior durable and published state intact, a successful response must correspond to state that survives refresh or restart, and an interrupted multi-file operation must be recoverable without guessing which files belong to the transaction.

This project addresses rank 6 and Comfy F23 from the maintainability architecture refresh. It introduces one focused workflow-store transaction boundary behind the existing extension and API surfaces. It does not claim a performance improvement.

## Confirmed Boundary

At the source baseline, `ComfyUIWebAPI.ComfySaveWorkflow`:

1. cleans the source and destination names;
2. converts or inherits the workflow image;
3. calls `ComfyDeleteWorkflow` when `replace` is present;
4. publishes the new `ComfyCustomWorkflow` in `CustomWorkflows`;
5. parses the four submitted JSON fields; and
6. writes the destination JSON file.

Replacement deletion is therefore a separately completed mutation. The predecessor can be deleted before the candidate is known to be valid, and the candidate becomes visible before its file is written. A parse or persistence exception can return an API error while leaving a memory-only workflow, and a failed replacement can additionally destroy the prior durable workflow.

`ComfyDeleteWorkflow` has the corresponding inverse ordering: it removes the cache entry before confirming or deleting the file and before writing a built-in example's `.deleted` marker.

The maintained consumers of the shared workflow state are:

- the permission-gated save, read, list, and delete routes;
- `ComfyUIBackendExtension.LoadWorkflowFiles` and `GetWorkflowByName`;
- `CustomWorkflowParam` value enumeration and cleaning;
- generation-time resolution through `ComfyUIAPIAbstractBackend.GetRawWorkflowFrom`; and
- refresh/startup inventory, lazy hydration, and built-in example restoration.

`CustomWorkflows` is a public `ConcurrentDictionary<string, ComfyCustomWorkflow>`. Its object identity and public compatibility surface must remain unchanged.

## Design Decision

Add a dedicated internal `ComfyWorkflowStore` that owns:

- submitted candidate preparation and serialization;
- maintained-reader coordination;
- save, overwrite, rename, and delete transactions;
- same-directory staging and recoverable backups;
- the single active crash journal;
- startup/refresh recovery;
- lazy hydration and consistent list/name snapshots; and
- publication into the existing `CustomWorkflows` dictionary.

Existing API and extension methods remain facades over the store. Transaction code does not remain duplicated between route handlers.

A single process-wide workflow-store lock is used instead of keyed locks. Rename and collision overwrite span two names, while refresh spans the complete inventory. Workflow mutations are infrequent, and one lock gives these operations one auditable ordering without lock ordering or snapshot gaps.

The design does not replace the public dictionary with an immutable repository and does not fold unrelated Comfy state into the store.

## Compatibility Contract

The project preserves:

- route names, arguments, permissions, and success/error response shapes;
- strict filename cleaning and nested workflow-name behavior;
- JSON field names, field order, formatting, and UTF-8 encoding;
- `workflow`, `prompt`, `custom_params`, and `param_values` parsing requirements;
- image metadata conversion, explicit clearing, inheritance, and placeholder behavior;
- save-without-replace overwrite behavior;
- same-name replacement behavior;
- cross-name rename behavior, including overwriting an existing destination;
- the current behavior in which a missing `replace` predecessor does not prevent saving the destination;
- built-in example `.deleted` marker semantics;
- successful immediate visibility;
- lazy full-record hydration and invalid-file omission/logging;
- public `CustomWorkflows` dictionary identity; and
- Windows, Linux, and modern .NET 8 compatibility.

The cache record published immediately after a save retains the submitted string fields as it does today. The durable JSON continues to contain parsed JSON objects and the remaining scalar metadata, and restart hydration retains its current compact string conversion.

Direct external access to the public dictionary cannot participate in a new multi-key transaction without breaking the compatibility surface. The atomic pre-state/post-state guarantee applies to maintained consumers routed through the store. External enumeration retains the thread-safety level supplied by `ConcurrentDictionary`.

## Non-Goals

This project does not:

- change the stored workflow schema or migrate existing workflow files;
- optimize cold-list hydration or implement Comfy P8;
- change workflow generation, dynamic parameter interpretation, or graph semantics;
- change workflow UI requests or success handling;
- change example workflow contents or inventory rules;
- add a general-purpose filesystem transaction framework;
- make unrelated extension state use this lock; or
- promise atomic directory-entry persistence beyond the guarantees available from the host filesystem and .NET.

## Candidate Preparation

Request-controlled data is validated before any durable or published mutation.

The save facade first:

1. strictly cleans the requested destination and effective predecessor names;
2. parses all four submitted JSON strings with `ComfySubmittedJson.ParseObject`;
3. converts a non-empty submitted image, or records that the committed predecessor image must be inherited;
4. constructs the durable `JObject` in the existing field order; and
5. serializes it once using the existing `JObject.ToString().EncodeUTF8()` behavior.

Explicit `"clear"` continues to select the normal placeholder rather than inheritance. A null or whitespace image inherits from the effective predecessor when that workflow exists; otherwise it resolves to the existing placeholder. Because inheritance may require lazy hydration, final image resolution and final serialization occur under the store lock against one consistent predecessor state. All JSON parsing and any supplied-image conversion happen before entering the transaction.

Invalid JSON or image data cannot create a staging file, journal, backup, marker, or cache mutation.

## Transaction-Owned Files

All transaction artifacts live beneath the unchanged `CustomWorkflows` root. Each transaction receives an unpredictable identifier generated by the server.

Artifacts use reserved filenames that do not end in `.json`, so normal workflow inventory cannot mistake them for saved workflows. The journal records only normalized paths relative to the workflow root. Before an artifact is opened, moved, restored, or deleted, the store verifies:

- the identifier and reserved filename pattern;
- that the resolved full path remains beneath the workflow root;
- that the path matches the role recorded in the journal; and
- that a user workflow path is derived from a strictly cleaned workflow name.

There is one active journal because the store lock permits only one writer transaction. User JSON files, unreferenced files, and artifacts whose ownership cannot be proven are never swept or deleted.

## Journal Model

The active journal records:

- a format version;
- transaction identifier;
- operation type;
- cleaned source and destination names where applicable;
- the SHA-256 identity of the staged destination, used only to verify transaction ownership during recovery;
- relative staging and backup paths;
- whether the original source and destination files existed;
- original source and destination `.deleted` marker states; and
- transaction phase.

The two durable phases are:

- `Prepared`: destructive file moves may have begun, but the operation is not committed and recovery must restore the complete pre-transaction state.
- `Committed`: the intended files and markers are authoritative; recovery must retain that state and finish cleanup.

Each journal version is written to its own same-directory temporary file, flushed with `FileStream.Flush(true)`, and moved over the active journal using the same-filesystem overwrite primitive available in .NET 8. The journal is durably `Prepared` before an original file or marker can be moved or changed. It is durably `Committed` only after the new authoritative file and all required predecessor/marker mutations have completed.

If the process stops while a phase update is being installed, recovery sees either the preceding complete journal or the following complete journal. A surviving unverifiable journal temporary is not treated as authority.

## Save and Replacement Algorithm

While holding the workflow-store lock, a save transaction:

1. resolves inherited image state and completes the candidate record and serialized bytes;
2. creates the destination directory if needed;
3. writes the complete candidate to a unique sibling staging file;
4. flushes the staging file with `FileStream.Flush(true)`;
5. snapshots source/destination file existence and relevant marker states;
6. writes and flushes the `Prepared` journal;
7. moves any existing destination into its recorded backup;
8. moves the staged candidate into the final destination path;
9. when source and destination differ, moves the predecessor into its recorded backup, retiring the old name last;
10. writes and flushes the built-in example marker changes required by the existing replacement-delete semantics;
11. writes and flushes the `Committed` journal;
12. removes the predecessor key and publishes the complete destination cache record while still holding the lock; and
13. attempts cleanup of backups and the journal.

The ordering preserves all originals until the new candidate is staged and described by a durable journal. For A-to-B replacement when B already exists, both A and the prior B remain recoverable until commit. For same-name replacement or ordinary overwrite, the source and destination are treated as one original file rather than backed up twice.

Cross-name predecessor retirement is part of the transaction, not a call to the public delete route. A failure to retire the predecessor or update a required marker rolls the destination back as well.

A supplied `replace` name that does not exist retains current API behavior: the valid destination save proceeds. Image inheritance falls back to the placeholder when no effective predecessor can be loaded.

## Delete Algorithm

Standalone deletion uses the same journal protocol:

1. clean the name and verify the workflow and authoritative file exist;
2. snapshot the file and relevant marker state;
3. write and flush a `Prepared` journal;
4. move the workflow file to the recorded backup;
5. create and flush the built-in example `.deleted` marker when required;
6. write and flush the `Committed` journal;
7. remove the cache entry while holding the store lock; and
8. attempt cleanup of the backup and journal.

An unknown or missing authoritative workflow returns the existing unknown-workflow error without removing a cache entry. This intentionally corrects the failed-operation inconsistency while retaining the external error contract.

## Rollback

Any exception before `Committed` journal installation triggers rollback while the store lock remains held.

Rollback:

- removes an installed candidate destination when the pre-state had no destination, but only after its content matches the journaled candidate identity;
- restores the prior destination from its verified backup when one existed, verifying any installed candidate before removing it;
- restores the predecessor from its verified backup;
- restores both marker states exactly;
- removes the verified staging file; and
- leaves `CustomWorkflows` unchanged.

Rollback is idempotent and checks actual file presence at every step so the same logic can be used after a process interruption. If rollback itself encounters an error, the `Prepared` journal and all still-owned recovery artifacts remain for the next startup or refresh. The original operation fails through the existing API exception boundary and no speculative cache repair is published.

No backup is deleted until the `Committed` journal is installed. Consequently, predecessor recovery remains possible throughout every uncommitted step.

## Commit and Cleanup Semantics

Installing the `Committed` journal is the durable commit point. Maintained cache publication follows immediately while holding the same lock.

After commit:

- a failure to clean a backup, staging remnant, or journal is logged with redacted context;
- cleanup failure does not convert a durably committed request into an ambiguous API error;
- the committed journal remains sufficient for later cleanup; and
- a subsequent refresh or restart reconstructs the committed cache state from authoritative JSON files.

The API returns success only after durable commit and cache publication. A process termination after durable commit but before the response can still make the client uncertain, as with any request interrupted after server-side commit, but recovery deterministically retains the committed workflow.

## Crash Recovery

`LoadWorkflowFiles` acquires the workflow-store lock and performs recovery before clearing or repopulating `CustomWorkflows` and before copying missing built-in examples.

For a valid `Prepared` journal, recovery restores the complete recorded pre-state:

- original source and destination files are restored from verified backups;
- a newly installed destination that had no predecessor is removed;
- original marker states are restored; and
- verified staging artifacts are removed.

For a valid `Committed` journal, recovery retains the intended post-state, completes any recorded predecessor retirement or marker state that is not yet reflected, and removes verified transaction artifacts.

Cleanup is idempotent. Recovery can stop and retry on a later refresh if storage remains unavailable.

If the active journal is malformed, unsupported, path-unsafe, or inconsistent with its owned artifacts, recovery does not guess. It logs a content-redacted error and aborts workflow inventory before mutating the existing dictionary or copying examples. On refresh, the last published dictionary therefore remains intact. On initial startup, workflows remain unpublished until the journal is repaired or safely recoverable, which is preferable to deleting or publishing ambiguous user data.

Unjournaled reserved-looking artifacts are ignored rather than removed automatically.

## Reader and Publication Coordination

All maintained state access uses the same store boundary:

- save and delete transactions hold the writer lock through cache publication;
- `GetWorkflowByName` checks and lazily hydrates an entry while holding the lock;
- read and generation obtain one complete record through the store;
- list obtains and hydrates a consistent record snapshot under the lock before sorting and response construction;
- `CustomWorkflowParam.GetValues` obtains a key snapshot through the store;
- parameter cleaning performs its existence/read decision through the store; and
- refresh/recovery/inventory hold the lock through dictionary repopulation.

The monitor used by C# `lock` is reentrant, so a maintained facade may safely call an internal locked lookup without opening a second state window. Response formatting and other work that does not require shared state occurs after the required snapshot is captured.

During a rename, maintained readers see either:

- the complete pre-state, including A and any prior B; or
- the complete committed post-state, with A retired and the new B visible.

They cannot observe the interval between filesystem moves or the two dictionary-key mutations.

`LoadWorkflowFiles` clears and repopulates the existing dictionary instance rather than assigning a replacement object. It retains null placeholders and existing lazy-hydration behavior after recovery.

## Filesystem and Platform Behavior

Staging and backup files are siblings of the affected workflow wherever possible, keeping moves on one filesystem. The implementation uses `FileStream` flushes and `File.Move` operations available on the project's .NET 8 target rather than requiring `File.Replace`, whose support and semantics vary by platform and filesystem.

Same-filesystem moves provide the atomic name transition used by the protocol on supported local filesystems. The write-ahead journal and recoverable backups provide the fallback when a later move, marker write, cleanup step, or process lifetime fails.

The implementation uses `Path` APIs for normalization and containment checks, accepts both platform separator conventions through existing cleaned names, and never constructs recovery targets from unchecked journal or request strings.

## Error Handling and Diagnostics

Candidate validation errors occur before transaction creation. Persistence errors before commit trigger rollback and then flow to the existing shared API error boundary. The project does not add route-specific response schemas.

Internal logs may identify the operation, phase, and cleaned workflow name. They must not log:

- submitted workflow, prompt, parameter, or image contents;
- journal contents containing unchecked data;
- sensitive absolute filesystem paths; or
- raw exception output that violates the existing submitted-content diagnostic boundary.

Malformed workflow files retain the existing fixed, content-redacted load diagnostic. Recovery diagnostics use the same confidentiality posture.

## Static Verification

Repository-permitted static verification will prove:

1. every maintained workflow file/cache mutation is owned by the store;
2. all four submitted JSON fields and submitted image data are validated before a transaction begins;
3. the complete candidate is flushed before any original is moved;
4. `Prepared` is flushed before destructive mutation;
5. no backup is deleted before `Committed`;
6. cache publication occurs only after durable commit;
7. pre-commit failure cannot mutate the maintained cache;
8. same-name overwrite does not double-handle one path;
9. A-to-B collision overwrite can restore both original A and original B;
10. standalone delete cannot remove the cache before durable commit;
11. example marker state is committed or rolled back with its workflow operation;
12. maintained list, read, parameter, generation, refresh, save, and delete paths share the store boundary;
13. refresh performs recovery before cache clear and example copying;
14. malformed recovery state cannot trigger broad deletion or partial republication;
15. recovery verifies an installed candidate's journaled identity before deleting it;
16. the public dictionary instance is never replaced;
17. workflow JSON structure and API surfaces are unchanged; and
18. no cold-list hydration optimization is introduced.

Permitted mechanical checks are whitespace/format inspection and `git diff --check`. Per repository policy, the agent does not run builds, automated tests, launchers, the server, or a Comfy backend.

## Maintainer Validation

The maintainer validation matrix covers:

1. new workflow save;
2. ordinary same-name overwrite;
3. explicit same-name replacement;
4. cross-name rename;
5. rename onto an existing destination;
6. replacement naming a missing predecessor;
7. standalone delete;
8. invalid `workflow`, `prompt`, `custom_params`, and `param_values` JSON independently;
9. image inheritance, replacement, explicit clearing, invalid data, and placeholder fallback;
10. built-in example deletion, replacement, rename, restart restoration, and `.deleted` markers;
11. unwritable or full storage during staging, prepared-journal installation, destination installation, predecessor retirement, marker mutation, commit, and cleanup;
12. concurrent maintained list, read, parameter resolution, and generation during save, rename, and delete;
13. refresh and restart after every successful operation;
14. simulated interruption in each prepared and committed transaction phase followed by restart;
15. malformed or path-unsafe journal handling using disposable test data; and
16. normal Linux operation plus Windows validation where available.

The maintainer performs runtime validation manually because this repository does not permit agent-run tests or builds.

## Success Criteria

The project is successful when:

- an API failure leaves prior durable and maintained published state unchanged;
- an API success corresponds to durable state that survives refresh or restart;
- coordinated readers never observe a partial rename or delete;
- interrupted transactions recover deterministically from their journal;
- recovery never deletes or overwrites an unverified user-owned file;
- example marker and workflow state commit together;
- existing clients and stored workflow files require no migration; and
- no behavior from the separately measured Comfy P8 hydration boundary changes.

## Rollback

The production change is one coordinated rollback unit: store delegation, reader coordination, transaction files, recovery, and post-commit cache publication must be reverted together.

Rollback restores the prior inline API and extension file methods. Existing workflow JSON files and example markers require no migration. Before rollback, any active valid journal should be allowed to recover or be resolved using its documented phase; removing recovery code while transaction artifacts remain could strand a recoverable operation.
