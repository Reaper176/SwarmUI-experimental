# Immutable Per-Backend Comfy Capabilities Design

**Status:** Approved

**Date:** 2026-07-25

**Maintainer:** Reaper176

## Goal

Publish one immutable capability snapshot per Comfy backend so backend selection, generation, workflow construction, raw-workflow validation, and per-backend reporting use only the capabilities of the selected backend.

Retain a separately coordinated global aggregate for parameter visibility and extension compatibility without allowing one backend's discovered nodes to become another backend's eligibility.

Correctness and compatibility take priority over minimizing the short in-process work performed while publishing an already-fetched capability candidate.

## Current Problem

Each `ComfyUIAPIAbstractBackend` fetches its own `object_info` document and already assigns instance-local `RawObjectInfo`, `NodeTypes`, `Models`, and `ModelFolderFormat`. It then calls `ComfyUIBackendExtension.AssignValuesFromRaw`.

`AssignValuesFromRaw` serializes writers with `ValueAssignmentLocker`, but it interprets backend-local node availability into the static mutable `FeaturesSupported` and `FeaturesDiscardIfNotFound` sets. `ComfyUIAPIAbstractBackend.SupportedFeatures` returns that same global set for every local/API/self-start Comfy backend, with only its instance folder-separator feature appended.

This creates two confirmed structural defects:

- a feature found on one Comfy backend can be advertised by another backend that does not contain its required node; and
- maintained readers can enumerate a mutable `HashSet<string>` without holding the writer lock.

The first backend that detects a presumptive feature also removes it from the global discard set. A later backend that lacks that node therefore cannot retract the already-advertised feature. Backend matching can accept the wrong instance, and workflow construction can then emit a node unavailable on the selected backend.

Linked `SwarmSwarmBackend` instances already receive a remote feature list and a separately fetched Comfy `object_info`, but the remote feature map is updated in multiple mutations and the fetched object info contributes only to the same global Comfy interpreter. Remote node validation is published through mutable extension data independently of the feature update.

Actual heterogeneous installations and reader/writer interleavings are runtime-dependent. Their frequency is not asserted.

## Scope

This project coordinates Comfy capability discovery and publication for:

- local API Comfy backends;
- self-start Comfy backends;
- linked non-real Comfy backends exposed through `SwarmSwarmBackend`;
- `ComfyUIBackendExtension.AssignValuesFromRaw`;
- `ComfyCapabilityCatalog`;
- each backend's `SupportedFeatures` and node-validation surface;
- maintained backend matching, generation, model loading, workflow creation, direct workflow validation, parameter visibility, status, and backend-list consumers;
- the legacy global `FeaturesSupported`, `FeaturesDiscardIfNotFound`, `NodeToFeatureMap`, `RawObjectInfoParsers`, and `ValueAssignmentLocker` extension surfaces;
- remote `RemoteFeatureCombo` compatibility;
- backend refresh, reload, idle, deletion, shutdown, and remote revision lifecycles; and
- static verification plus maintainer-run live validation.

The implementation inventory must include every maintained read and write of:

- `FeaturesSupported`;
- `FeaturesDiscardIfNotFound`;
- `NodeToFeatureMap`;
- `RawObjectInfoParsers`;
- `ValueAssignmentLocker`;
- `AssignValuesFromRaw`;
- `NodeTypes`;
- `RemoteFeatureCombo`;
- `ExtensionData["ComfyNodeTypes"]`; and
- `SupportedFeatures`.

This list is evidence, not permission to ignore a newly discovered maintained consumer.

## Out of Scope

This project does not:

- change Comfy feature IDs or node names;
- infer optional capability availability without `object_info` or the existing authoritative remote feature response;
- redesign T2I parameter feature flags;
- change backend selection policy beyond giving it accurate per-backend data;
- change workflow graphs, node input contracts, parser callback order, model catalogs, or custom-workflow storage;
- change API route names, request fields, response fields, permissions, or status values;
- replace the public mutable compatibility fields with immutable or concurrent types;
- require external extensions to adopt a new API;
- redesign the `SwarmSwarmBackend` forwarding protocol;
- add multi-layer remote forwarding;
- change backend persistence;
- run capability discovery while no backend load or remote revision occurs solely to poll for public field mutation;
- optimize capability parsing or make a performance claim; or
- authorize agent-run builds, tests, launchers, backends, browsers, or Comfy processes.

## Compatibility Requirements

The implementation preserves:

- the names, public field types, and object identities of `FeaturesSupported`, `FeaturesDiscardIfNotFound`, and `NodeToFeatureMap`;
- the public `ValueAssignmentLocker`, `RawObjectInfoParsers`, and `AssignValuesFromRaw` surfaces;
- direct extension additions and removals through all three public compatibility collections;
- `RawObjectInfoParsers` invocation order and per-callback exception isolation;
- every established feature ID and node-to-feature mapping;
- initial universal feature defaults and presumptive local-install hints;
- object-info authority for node-derived local capabilities;
- remote status authority for linked-remote scheduling features;
- `RawObjectInfo`, `NodeTypes`, `Models`, `ModelFolderFormat`, `RemoteFeatureCombo`, and `ExtensionData` compatibility surfaces;
- `SupportedFeatures` as an `IEnumerable<string>`;
- the `folderbackslash` and `folderslash` feature IDs;
- backend IDs, parent/linked-remote relationships, and exact-backend forwarding;
- global parameter visibility as an aggregate concern;
- API schemas and the order/meaning of status and backend-list fields; and
- extension lifecycle and parser registration timing.

Direct mutations of the three compatibility collections remain supported inputs to both per-backend snapshots and the global aggregate. External extensions that mutate those non-concurrent collections concurrently must coordinate with the already-public `ValueAssignmentLocker`; maintained code will consistently do so.

## Chosen Architecture

### Immutable backend snapshot

Each Comfy backend has one immutable capability snapshot containing:

- its effective feature IDs, including exactly one folder-separator feature where applicable;
- its frozen node-type set;
- the node-derived feature evidence needed to reevaluate compatibility-map changes; and
- the publication generation needed to prevent stale refresh completion from overwriting newer state.

The snapshot is built as a candidate and published by atomic reference replacement. Maintained readers obtain one snapshot reference and use only that immutable state for the duration of their decision or operation.

Local/API/self-start Comfy backends use this snapshot for `SupportedFeatures`, backend eligibility, generation feature branches, model-load feature branches, workflow construction, generated-workflow preview, and node validation.

Public mutable instance fields remain compatibility mirrors. Maintained correctness does not depend on reading a sequence of those fields as an atomic unit.

### Pure capability interpretation

`ComfyCapabilityCatalog` becomes the interpretation boundary for built-in capability rules. Given:

- one backend's frozen node set;
- a snapshot of the effective universal feature baseline;
- a snapshot of presumptive/discard feature hints;
- a snapshot of `NodeToFeatureMap`; and
- that backend's folder format,

it produces a fresh immutable feature set without mutating process-wide state.

The interpreter preserves:

- mapped node detection;
- the established variation-seed, FreeU, YOLO, IPAdapter, preprocessing, interpolation, SAM, model-loader, cache, device, and inpaint feature IDs;
- hook LoRA scheduling and interpolated-hook scheduling rules;
- presumptive feature removal when the backend lacks required evidence; and
- folder-separator behavior.

No backend's previously published aggregate values are used as another backend's detection baseline.

### Compatibility-aware registry

A Comfy capability registry owned by `ComfyUIBackendExtension` runs under `ValueAssignmentLocker`. It stores each currently registered backend owner's last-good discovery evidence and immutable snapshot.

The registry distinguishes:

- established built-in universal features;
- established presumptive/discard hints;
- features detected for each backend;
- direct extension additions;
- direct extension removals; and
- the last aggregate that maintained code published.

Before a publication or maintained snapshot retrieval, the registry compares the public compatibility collections with their last maintained publication. A difference is recorded as an extension override instead of being mistaken for backend discovery.

An extension addition to `FeaturesSupported` is an explicit universal compatibility addition. An extension removal is an explicit compatibility suppression until the extension restores it. Changes to `FeaturesDiscardIfNotFound` alter whether a presumptive feature requires backend evidence. Changes to `NodeToFeatureMap` are applied to the stored node sets so existing backend snapshots can be reevaluated without another network fetch.

Mutations performed by a legacy `RawObjectInfoParsers` callback remain global compatibility overrides because that callback surface does not identify a backend owner. They are applied consistently rather than silently ignored. Extensions that need node-local interpretation can continue to contribute a node-to-feature mapping.

### Same-object global aggregate

The registry computes the legacy aggregate as:

- the effective compatibility baseline and overrides; plus
- the union of last-good snapshots for all currently registered Comfy backend owners.

It publishes that aggregate by clearing and repopulating the existing `FeaturesSupported` object under `ValueAssignmentLocker`. The field is not reassigned.

Maintained aggregate readers receive a stable copied or immutable result from a registry helper. They never enumerate the public mutable `HashSet<string>` while a maintained writer can change it.

Temporary loading, idle, refresh, reload, or shutdown-preparation states do not remove a contribution. Deleting a backend unregisters its owner and removes only features no longer supplied by another current snapshot or compatibility override.

### Linked remote snapshots

`SwarmSwarmBackend` retains public `RemoteFeatureCombo` compatibility but publishes an immutable remote feature snapshot from each complete status response. `SupportedFeatures` reads that immutable snapshot, so a reader cannot observe the add/remove loops used to mirror the public dictionary.

For a linked Comfy backend:

- the remote status response remains authoritative for scheduling features;
- the fetched backend-specific `object_info` produces its frozen node set and Comfy aggregate contribution;
- raw workflow validation uses the linked backend's frozen node set;
- exact-backend forwarding and remote IDs remain unchanged; and
- removal unregisters both remote discovery evidence and aggregate contribution.

A remote status refresh and a remote object-info refresh are separate candidates. Failure of either retains its corresponding last-good snapshot.

## Publication Data Flow

### Local/API/self-start backend

1. Enter the backend's private discovery-refresh gate.
2. Fetch `object_info` without holding `ValueAssignmentLocker`.
3. Build candidate raw info, node types, model lists, and folder format in local variables.
4. Build candidate maintained shared dropdown/preprocessor values without changing their published values.
5. Enter `ValueAssignmentLocker`.
6. Reconcile direct compatibility collection changes.
7. Run the established extension parser callbacks in their established order with per-callback exception isolation.
8. Reconcile any callback-driven compatibility changes.
9. Interpret the backend's immutable capability candidate.
10. Publish the backend snapshot and its public compatibility mirrors.
11. Store the registry entry and republish the same-object global aggregate.
12. Release the global lock, then release the backend refresh gate.

No global capability lock spans HTTP, WebSocket, child-process, backend, model, or GPU work.

### Linked remote backend

Remote status revision builds a complete feature candidate before atomically replacing the internal remote feature snapshot. It then mirrors the same result into `RemoteFeatureCombo` for compatibility.

Remote Comfy object-info revision:

1. fetches backend-specific `object_info` without the global lock;
2. builds node and capability candidates;
3. publishes them through the same registry boundary under `ValueAssignmentLocker`; and
4. mirrors the frozen node set to the established extension-data surface.

Per-owner serialization or generation ordering prevents an earlier response from overwriting a newer successful publication.

### Compatibility mutation reconciliation

Because direct collection mutation raises no event, maintained registry getters reconcile mutations while holding `ValueAssignmentLocker`. A detected change can reevaluate all stored entries from their immutable node evidence and republish the aggregate without a network fetch.

This preserves direct extension mutation as a supported input. It does not make an extension's unsynchronized concurrent `HashSet` or `Dictionary` mutation safe; doing that would require breaking the public field types.

## Reader Migration

Every maintained consumer is classified before implementation.

The migration rules are:

- backend matching copies or reads one backend snapshot and checks all required flags against it;
- generation and model loading use the already-selected backend's snapshot;
- generated-workflow APIs choose a backend first, then pass only that snapshot's features and folder format;
- raw/direct workflow validation uses the same backend snapshot's node types;
- backend-list responses serialize a stable feature snapshot;
- global status and parameter listing use the stable current aggregate produced from eligible backend snapshots through existing status filtering;
- shared UI dropdown/preprocessor catalogs remain aggregate values populated by the existing parser order;
- linked remotes use their immutable remote status feature snapshot for scheduling; and
- no maintained decision falls back to `ComfyUIBackendExtension.FeaturesSupported`.

If a workflow or helper chooses a backend after deriving feature behavior, the flow must be reordered so backend selection precedes snapshot-dependent construction.

## Locking and Ordering

The lock order is:

1. a per-backend or per-remote asynchronous discovery gate, when a refresh is in progress;
2. `ValueAssignmentLocker` for short interpretation/publication work.

No code acquires a backend lifecycle, scheduler, model refresh, request, WebSocket, process, or GPU claim while holding `ValueAssignmentLocker`.

Snapshot readers do not acquire the per-backend discovery gate. They read the last fully published immutable reference.

Backend deletion/removal notification enters the registry only after the backend has left the handler's registered set. Reload and edit paths that retain the backend do not unregister it. Registry removal does not call backend shutdown or perform network work.

## Failure Semantics

### Fetch failure

A local or remote fetch failure changes no discovery snapshot, public mirror, registry entry, or aggregate. The last-good snapshot remains authoritative.

### Candidate interpretation failure

If a backend already has a published snapshot, malformed built-in discovery data or an interpretation failure logs the existing readable diagnostic boundary and retains the complete prior snapshot and mirrors.

If first initialization has no prior snapshot, interpretation fails closed and returns through normal backend initialization retry/error handling. The backend must not become schedulable with guessed or partially interpreted capabilities.

### Extension parser failure

Each `RawObjectInfoParsers` callback retains its existing isolated exception handling. One callback failure does not suppress later callbacks and does not invalidate an otherwise complete built-in capability candidate.

Maintained shared dropdown/preprocessor values are candidate-first. An extension callback can retain its own side effects before throwing because the public callback contract is not transactional.

### Publication failure

Built-in state is candidate-first. An exception before the commit point leaves the prior backend snapshot and aggregate authoritative. Same-object public mirrors are updated only within the coordinated publication section.

If an unexpected exception occurs after an atomic snapshot reference replacement, publication completes or restores the prior registry reference before releasing `ValueAssignmentLocker`; readers never receive a partially populated mutable snapshot.

### Removal and repetition

Removing one backend deletes only that registry entry. Aggregate recomputation retains features supplied by other backends or compatibility overrides.

Repeated refresh, reload, idle, and shutdown preparation retain the last-good entry. Final process shutdown does not require aggregate cleanup.

## Static Verification

Repository policy prohibits agent-run builds and tests. Implementation verification therefore uses source inspection, targeted searches, and safe lint-style checks.

Static verification must:

1. enumerate every maintained read and write of the ten named capability surfaces;
2. classify each `SupportedFeatures` consumer as per-backend or aggregate;
3. prove all maintained backend eligibility and workflow branches use the chosen backend's immutable snapshot;
4. prove raw-workflow validation uses that same backend's frozen node set;
5. prove no local backend snapshot derives from another backend's detected features;
6. prove remote scheduling retains remote-status authority;
7. prove public compatibility collection field types and identities remain unchanged;
8. prove direct additions, removals, discard changes, and node-map changes are reconciled;
9. prove parser callback order and failure isolation remain unchanged;
10. prove maintained readers cannot enumerate a set during maintained mutation;
11. prove fetch and candidate failure retain last-good state;
12. prove first-load interpretation failure cannot publish a guessed snapshot;
13. prove stale refresh completion cannot overwrite newer publication;
14. prove reload/idle retain and deletion unregisters the correct owner;
15. prove the global lock does not span independent I/O or backend work;
16. verify feature IDs, node mappings, folder flags, API schemas, parameter visibility, and remote forwarding remain unchanged;
17. inspect the final changed-file and commit ranges so protected maintainer work is excluded; and
18. run `git diff --check` over the implementation range.

No build, launcher, automated test, GPU operation, live concurrency exercise, or performance benchmark is run by the agent.

## Maintainer Validation

The maintainer will validate the implementation in the live application.

### Baseline

- Build and launch normally.
- List parameters and backend status.
- Browse models and complete representative generation and workflow-preview operations.
- Confirm unchanged API payloads, feature IDs, folder behavior, and parameter visibility.

### Heterogeneous local backends

Configure two local/API or self-start Comfy backends with deliberately different node sets, including representative combinations of:

- Swarm core nodes;
- FreeU;
- IPAdapter;
- ControlNet preprocessors;
- frame interpolation;
- SAM;
- hook LoRA scheduling; and
- optional loader/cache/device nodes.

Confirm each backend reports only its own capability snapshot. Exercise exact-backend selection and normal scheduling with a request requiring a feature unique to one backend. The other backend must be rejected rather than borrowing the feature.

### Workflow and validation authority

- Generate and preview workflows whose branches depend on backend features.
- Submit raw workflows with common nodes and nodes unique to one backend.
- Confirm validation and generation use the same selected backend's feature and node snapshots.
- Confirm model-load feature behavior uses the selected backend snapshot.

### Refresh and lifecycle

- Add and remove representative nodes, then refresh/reload.
- Exercise idle and resume.
- Restart and confirm discovery is rebuilt consistently.
- Delete one backend and confirm only its unique aggregate contribution disappears.
- Confirm transient loading, refresh, and reload do not make global parameter visibility flicker.

### Concurrency

Overlap capability refresh with:

- global status;
- backend listing;
- parameter listing;
- workflow preview;
- raw-workflow validation; and
- generation/backend selection.

Each operation must observe a complete prior or replacement snapshot. No collection-modified failure, partial remote feature set, or cross-backend feature borrowing is acceptable.

### Failure

- Cause an available object-info fetch failure after a successful publication and confirm the last-good snapshot remains.
- Exercise an available malformed interpretation case after a successful publication and confirm the last-good snapshot remains.
- Exercise a first-load malformed interpretation case and confirm the backend fails closed through normal initialization handling.
- Confirm one failing extension parser callback is logged and later callbacks still execute.

### Linked remotes

- Connect linked remote Comfy backends with different feature and node sets.
- Confirm remote status remains scheduling authority and raw-workflow validation uses the matching linked backend's nodes.
- Refresh remote status and object info concurrently with listing and selection.
- Remove one linked remote and confirm only its contribution disappears.

### Extension compatibility

Under `ValueAssignmentLocker`, exercise direct extension additions and removals in:

- `FeaturesSupported`;
- `FeaturesDiscardIfNotFound`; and
- `NodeToFeatureMap`.

Confirm existing backend snapshots and the global aggregate reconcile without another network fetch. Confirm the same public collection instances remain visible. Confirm parser order, callback failure isolation, and global parameter visibility remain compatible.

## Success Criteria

The project is complete when:

- every local/API/self-start Comfy backend publishes one immutable capability snapshot;
- linked remotes publish stable feature and node snapshots under their existing authorities;
- backend selection never accepts an instance based only on another backend's discovery;
- generation, model loading, workflow construction, preview, and raw validation use the selected backend snapshot;
- maintained readers never enumerate a collection during maintained mutation;
- the public compatibility collections retain their field types and object identities;
- direct extension additions and removals remain effective inputs;
- the global aggregate is the current registered union plus compatibility overrides;
- temporary lifecycle states retain last-good snapshots and deletion removes the correct contribution;
- fetch/interpretation failure retains the prior snapshot, while first-load interpretation failure fails closed;
- stale refresh completion cannot overwrite newer state;
- static verification finds no uncovered maintained capability decision; and
- the maintainer confirms the heterogeneous, concurrency, remote, failure, lifecycle, and compatibility matrix.

## Rollback

The implementation is staged so per-backend `SupportedFeatures` getters can delegate to the legacy global set again while the additive snapshot types remain present.

A full rollback must revert together:

- local immutable snapshot publication;
- remote immutable feature/node publication;
- registry aggregation and compatibility override tracking;
- maintained reader migration;
- deletion/removal registration hooks; and
- candidate-first discovery publication.

Rollback must not:

- leave backend matching on snapshots while workflow construction reads the global aggregate;
- leave remote selection and validation on different generations;
- reassign the three public compatibility collections;
- remove parser callbacks or reorder them;
- retain registry entries without removal hooks; or
- claim a performance or runtime result not established by maintainer validation.
