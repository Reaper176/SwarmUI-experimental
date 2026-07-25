# Runtime Model Catalog Refresh Boundary Design

**Status:** Implemented and maintainer-validated

**Date:** 2026-07-24

**Maintainer:** Reaper176

## Goal

Publish runtime model-path changes under one catalog refresh boundary so maintained readers observe either the complete prior model catalog or the complete replacement catalog. Preserve the public model dictionary, handler and model semantics, synchronous settings behavior, model category names, event contracts, and extension compatibility.

Correctness and compatibility take priority over minimizing the bounded pause while a path rebuild holds the existing model refresh write claim.

## Current Problem

`Program.T2IModelSets` is a public mutable `Dictionary<string, T2IModelHandler>`. `Program.BuildModelLists` currently shuts down every existing handler, clears that dictionary, and adds the seven replacement handlers one at a time. `AdminAPI.ChangeServerSettings` invokes `BuildModelLists`, `RefreshAllModelSets`, and `ModelPathsChangedEvent` after a committed runtime path change without acquiring `Program.RefreshLock`.

Maintained runtime readers concurrently resolve handlers, enumerate categories, read model maps, convert request parameters, construct workflows, serialize remote model information, browse models, inspect metadata, and calculate download destinations. Existing read claims cover only part of the Models API surface, and those claims cannot exclude the settings-path writer because that writer does not take the corresponding write claim.

Readers can therefore encounter:

- an empty outer dictionary;
- a dictionary containing only some of the seven categories;
- a handler that was resolved before it was shut down;
- incomplete parameter or model responses; or
- lookup, enumeration, or conversion failures.

The unsafe mutation and incomplete coordination are statically confirmed. Runtime overlap frequency is not known and is not asserted.

## Scope

This project coordinates the process-wide runtime model catalog and its maintained readers. It includes:

- `Program.T2IModelSets` and the derived `Program.MainSDModels` access path;
- model-handler construction, replacement, refresh, and retirement;
- `Program.RefreshLock` ownership;
- the server-settings model-path mutation side effect;
- maintained direct catalog consumers in core, Text2Image, Web API, backend, and built-in-extension code;
- model download and helper flows that refresh model state after filesystem changes;
- `ModelPathsChangedEvent` publication after a complete replacement;
- compatibility documentation for external direct dictionary consumers; and
- static verification plus maintainer-run live validation.

The initial maintained inventory contains the direct or derived catalog consumers in:

- `src/Backends/BackendHandler.cs`
- `src/Backends/SwarmSwarmBackend.cs`
- `src/BuiltinExtensions/AutoWebUIBackend/AutoWebUIAPIAbstractBackend.cs`
- `src/BuiltinExtensions/ComfyUIBackend/ComfyUIAPIAbstractBackend.cs`
- `src/BuiltinExtensions/ComfyUIBackend/ComfyUIBackendExtension.cs`
- `src/BuiltinExtensions/ComfyUIBackend/ComfyUIWebAPI.cs`
- `src/BuiltinExtensions/ComfyUIBackend/WorkflowGenerator.cs`
- `src/BuiltinExtensions/ComfyUIBackend/WorkflowGeneratorModelSupport.cs`
- `src/BuiltinExtensions/ComfyUIBackend/WorkflowGeneratorSteps.cs`
- `src/Core/Installation.cs`
- `src/Core/WebServer.cs`
- `src/Text2Image/CommonModels.cs`
- `src/Text2Image/T2IModelHandler.cs`
- `src/Text2Image/T2IParamInput.cs`
- `src/Text2Image/T2IParamSet.cs`
- `src/Text2Image/T2IParamTypes.cs`
- `src/Text2Image/T2IPromptHandling.cs`
- `src/WebAPI/ModelsAPI.cs`
- `src/WebAPI/T2IAPI.cs`
- `src/WebAPI/UtilAPI.cs`
- `src/Pages/_Generate/UtilitiesTab.cshtml`

The corrected implementation baseline contains 21 maintained consumer files and 71 matching source lines beyond `Program.cs`; `src/WebAPI/ModelsAPI.cs:375` contains two literal identifiers on one matching line, for 72 literal identifier references. The 71 matching source lines classify as two startup-only lines, three write-side lines, five protected lines, and 61 uncovered lines, including the runtime Razor category enumeration at `src/Pages/_Generate/UtilitiesTab.cshtml:50`.

The implementation inventory must be repeated against the implementation baseline. This list is evidence, not permission to ignore a newly discovered maintained consumer.

## Out of Scope

This project does not:

- redesign `T2IModel`, `T2IModelHandler`, model classes, metadata formats, or database schemas;
- replace the public dictionary with an immutable collection or a new extension API;
- reassign the public `T2IModelSets` dictionary instance;
- rename or add model categories;
- change request or response payloads;
- make `ChangeServerSettings` asynchronous beyond its existing contract;
- add a filesystem transaction for model directories;
- alter model download URLs, destinations, hash behavior, or formats;
- change extension event delegate types or ordering;
- optimize model scanning or make a performance claim;
- coordinate external extensions that bypass the documented `RefreshLock`; or
- address unrelated catalog, persistence, backend, workflow, or authentication findings.

## Considered Approaches

### 1. Existing read/write boundary with one catalog operation

Centralize runtime path rebuild, refresh, publication, and path-change notification behind the existing `RefreshLock` write boundary. Add read claims to maintained runtime catalog consumers. Preserve the public dictionary instance and existing handler behavior.

This is the selected approach. It directly repairs the confirmed race with the established synchronization mechanism and has the smallest compatibility surface.

### 2. Stage and atomically swap the dictionary

Build and scan a replacement dictionary away from readers, then swap the public reference. This could shorten reader blocking but would break dictionary-instance identity for extensions that retain the public object. Handler event subscriptions and the shared metadata cache also complicate safe old-generation disposal.

This approach is rejected for this project.

### 3. Reconfigure existing handlers in place

Retain handler identity and change folder paths and model maps category by category. This avoids replacing handlers but permits mixed category generations unless substantially more per-handler state and coordination are introduced.

This approach is rejected because it expands the change while weakening the catalog-level boundary.

## Architecture

`Program` remains the catalog owner. The public declaration remains a mutable `Dictionary<string, T2IModelHandler>`, and `MainSDModels` remains its Stable-Diffusion convenience property.

The implementation separates public lock-owning operations from private lock-free core helpers:

- a core builder constructs and configures all seven handlers in a temporary dictionary;
- a core publisher detaches the displaced handlers, retires their shared metadata cache once, and repopulates the existing public dictionary while a write claim excludes readers;
- a core refresher scans every published handler with the existing per-handler exception isolation;
- public compatibility entry points acquire the required write claim before invoking their core behavior; and
- one runtime path-change operation composes candidate construction, publication, refresh, and notification without recursively acquiring the write lock.

Startup may use the same core operations before concurrent runtime readers exist. The runtime settings path must use the centralized lock-owning operation.

## Runtime Path-Change Flow

After `ChangeServerSettings` has durably saved and published the settings candidate, its model-path side effect performs this sequence:

1. acquire `Program.RefreshLock` for writing;
2. construct all seven replacement handlers and configure their paths in a temporary dictionary;
3. if candidate construction fails, detach every constructed candidate without touching the shared metadata cache and retain the complete prior public catalog and cache;
4. reserve enough capacity in the existing public dictionary before retiring the prior generation;
5. after the write claim has excluded protected readers, detach every displaced handler and then remove and dispose the shared metadata-cache entries once;
6. clear and repopulate that same dictionary with the complete seven-category candidate;
7. refresh every published handler, preserving per-handler logging and continuation;
8. invoke `ModelPathsChangedEvent` only after complete catalog publication; and
9. release the write claim after the composed runtime transition finishes.

The established keys remain:

- `Stable-Diffusion`
- `VAE`
- `LoRA`
- `Embedding`
- `ControlNet`
- `Clip`
- `ClipVision`

The public dictionary object is not replaced. Its identity, mutability, key spelling, values, and direct-access compatibility remain intact.

## Candidate Construction and Publication

Candidate handlers are constructed before the current catalog is retired. This prevents an ordinary path/configuration exception from destroying the last usable catalog.

`T2IModelHandler` construction subscribes each candidate to `ModelRefreshEvent`. A failed candidate must therefore be detached so its subscription is removed, but it must not run full shutdown because the metadata cache is static and belongs to the still-published generation. Candidate detachment is best-effort and must not replace the original failure in logs or the settings warning.

Candidates are not refreshed while the prior generation is live. After every displaced handler is detached, publication removes every shared cache entry with `TryRemove` and calls the entry's exception-isolating disposal operation. Removal occurs before disposal so one database disposal problem cannot retain stale entries or skip later entries. Replacement handlers scan only after this one shared-cache retirement.

Publication mutates the existing dictionary only while the write claim excludes maintained readers. It reserves capacity before retiring the prior generation, then inserts all seven configured categories before the first replacement refresh begins. If an unexpected ownership-transfer failure occurs, every replacement handler not present in the public dictionary is detached best-effort; retired handlers are not presented as restorable. A scan failure may leave one complete category with an empty or partial inner model map according to existing `T2IModelHandler.Refresh` behavior, but it cannot expose an incomplete outer category map.

## Public Compatibility Entry Points

`BuildModelLists` and `RefreshAllModelSets` remain public compatibility entry points, while `RefreshModelSet` and `RebuildModelListsForPathChange` provide focused maintained write paths. Each public operation acquires the catalog write claim, so callers must not already hold a catalog read or write claim. Private core helpers allow the centralized runtime path operation and startup sequence to reuse their behavior without nested write claims.

The implementation must preserve the distinction between:

- building handler/path configuration;
- refreshing handler model maps;
- publishing `ModelRefreshEvent`, which also refreshes non-model subscribers; and
- publishing `ModelPathsChangedEvent`, which triggers path-specific side effects such as enabled Comfy self-start backend reload.

No existing event is silently substituted for another.

External extensions retain direct access to `T2IModelSets`, `MainSDModels`, and `RefreshLock`. XML documentation will state that runtime outer-catalog enumeration or handler resolution must participate in `RefreshLock`. This project cannot make an extension safe if it bypasses that public coordination contract.

## Maintained Reader Coverage

Every maintained runtime access through either `T2IModelSets` or `MainSDModels` must be classified as:

- startup-only before concurrent readers;
- write-side code already executing under the catalog write claim;
- a protected read whose complete handler/catalog use is covered by a read claim; or
- a copied value or model result that no longer requires the handler after the claim is released.

Read claims begin before outer-dictionary lookup or enumeration. A handler must not be resolved before acquiring the claim and then used after an intervening replacement.
A copied `T2IModel` is not independent if subsequent work calls methods that re-enter its `Handler` or handler-owned metadata state, notably tensor hashing and model resaving; those operations remain inside the read claim.
The maintained explicit backend model-load flow is a safe copied-model exception: after selection under a read claim, backend loaders consume the model's copied name, path, and already-loaded metadata without calling its handler, tensor hashing, or resave operations, so WebSocket dispatch and backend/GPU work proceed after the claim is released.
Typed model values retained in parameter input can likewise outlive their catalog generation. Metadata serialization copies an already-populated hash directly, but generates a missing hash only when the model's handler is reference-identical to the currently published handler under the active read claim. A stale model without a copied hash retains its metadata entry with a null hash rather than re-entering a retired handler.
When handler-dependent work is intentionally deferred to a background callback, the synchronous path transfers exclusive ownership of its existing read claim to that callback before scheduling. Because `ReadClaim` is a mutable struct, the claim is boxed once as `IDisposable`: synchronous early-return paths dispose it in `finally`, successful transfer nulls the synchronous owner, and the callback or scheduling-exception path atomically clears and disposes the transferred owner exactly once. The callback must not acquire a nested read claim because a queued writer may already be draining prior readers.

Claims cover the shortest complete synchronous catalog operation. They may include:

- resolving a category and enumerating its model map;
- selecting a model and copying its path or metadata reference;
- converting a parameter against the current model names;
- constructing the part of a workflow that reads the catalog;
- forming parameter-list model data;
- producing remote category serialization; or
- validating a known-model destination.

Catalog read claims are not reentrant. A queued writer can hold the reader gate while it drains existing read permits, so a reader that attempts to acquire another read claim can deadlock against that writer. Maintained call chains must establish one outer read boundary and invoke catalog-dependent helpers without reacquiring. In particular, `T2IAPI.ListT2IParams` owns the read claim around parameter value-provider serialization, while branch-scoped claims in `T2IParamTypes.ValidateParam` protect the other maintained value-provider and model-validation paths without spanning media file reads. Catalog-backed `GetValues` delegates remain lock-free and must run within one of those boundaries; external direct delegate callers must coordinate through `Program.RefreshLock`.

Prompt contexts can span several parsing blocks, so cached embedding and LoRA name arrays are not generation-stable. Each focused prompt catalog phase rebuilds its name snapshot under the same claim used for matching and resolution, then copies any metadata string needed after the claim.

Claims should not remain held during work that no longer reads the handler generation, including:

- independent filesystem reads such as wildcard loading or metadata-header inspection after the required path or branch has been copied;
- external HTTP requests;
- file downloads;
- child-process waits;
- WebSocket progress streaming;
- backend or GPU execution; or
- user-visible process launching after the required model path has been copied.

This keeps the accepted model-path edit pause bounded by actual catalog use rather than unrelated long-running work.

## Download and Refresh Flows

Several maintained operations copy a model destination, perform I/O, then refresh or verify the catalog. These paths must not attempt to upgrade a read claim to a write claim.

They use explicit phases:

1. acquire a read claim, resolve the handler, validate the required catalog state, and copy the destination/path data;
2. release the read claim;
3. perform download, extraction, conversion, or other external/file work;
4. invoke a public write-owning refresh operation when model state changed; and
5. reacquire a read claim if the operation must verify that the new model is present.

No read claim may be live when the same flow requests the write claim. Workflow helpers that auto-download known VAE or Clip files must follow the same phase separation.

Workflow download-validity caches are destination-aware: each cache entry stores the complete resolved destination path and is a hit only when that path equals the current destination. A late download from a displaced path generation may overwrite an entry with its old destination, but it cannot make that entry a false hit for the current path.

## Lock Ownership and Ordering

The lock order is:

1. the server-settings transaction completes before a catalog write claim is acquired;
2. `T2IAPI.RefreshSemaphore` may be acquired before `RefreshLock` for the existing user-triggered refresh flow;
3. a catalog claim may be held while entering a handler's modification or metadata lock through existing handler operations;
4. code must not hold a handler-local modification or metadata lock while newly acquiring the catalog write claim; and
5. code must never upgrade directly from a catalog read claim to a catalog write claim; and
6. code must never acquire a nested catalog read claim, including from a catalog-backed helper or value-provider delegate.

The path-change operation is synchronous, matching the existing `ChangeServerSettings` side-effect contract.

`ModelRefreshEvent` is already published under the user-triggered write claim. The path-change event is likewise published only after the replacement catalog is structurally complete. Event consumers execute as write-side callbacks and must not reacquire `RefreshLock`.

## Failure Semantics

### Before publication

If candidate creation or path configuration throws:

- keep the prior public catalog unchanged and usable;
- detach every candidate handler that was constructed without disposing the prior generation's shared metadata cache;
- retain the already persisted server settings;
- log the runtime failure; and
- return the existing success-with-warning result from `ChangeServerSettings`.

Settings are not rolled back because persistence has already succeeded, matching the established transactional settings design.

### During refresh

`RefreshAllModelSets` already catches and logs each handler exception independently. Preserve that isolation so one category does not prevent later categories from scanning.

All seven outer categories remain published even when one handler scan fails. No exception path may leave the public dictionary empty or structurally partial after the write claim is released.

### During path-change notification

The replacement catalog remains authoritative if `ModelPathsChangedEvent` or a subscriber fails. The failure produces the existing runtime warning and does not:

- restore shut-down handlers;
- revert persisted settings;
- invoke destructive filesystem compensation; or
- claim that the runtime side effects fully succeeded.

### Shutdown

The public parameterless `T2IModelHandler.Shutdown()` remains compatible and idempotent: it detaches that handler and performs shared cache cleanup. Runtime replacement instead detaches all displaced handlers while its write claim excludes protected readers, then performs the shared cache cleanup exactly once.

## Compatibility Requirements

The implementation preserves:

- the `Program.T2IModelSets` public field and its object identity;
- `Program.MainSDModels`;
- all seven category names;
- `T2IModelHandler` and `T2IModel` object behavior;
- public `BuildModelLists`, `RefreshAllModelSets`, `RefreshModelSet`, `RebuildModelListsForPathChange`, and `RefreshLock` access;
- `ModelRefreshEvent` and `ModelPathsChangedEvent` delegate surfaces and relative purpose;
- the existing public `SwarmSwarmBackend.TriggerRefresh()` and `ReviseRemoteDataList(bool)` method signatures, with catalog snapshots passed through internal overloads;
- synchronous `ChangeServerSettings` completion;
- the settings route name, permission, inputs, success payload, and warning payload;
- model list and parameter response schemas;
- model metadata modes and files;
- download destinations and known-model behavior;
- extension lifecycle and subscriptions;
- remote-backend model serialization;
- Comfy workflow generation and self-start backend path reload; and
- the existing behavior of individual handler scan failures.

## Static Verification

Repository policy prohibits agent-run builds and tests. Implementation verification therefore uses source inspection, targeted searches, and safe lint-style checks.

Static verification must:

1. repeat the maintained inventory for both `Program.T2IModelSets` and `Program.MainSDModels`;
2. classify every access as startup-only, write-side, protected read, or a safe copied result;
3. enumerate every caller of `BuildModelLists`, `RefreshAllModelSets`, `ModelRefreshEvent`, and `ModelPathsChangedEvent`;
4. prove the runtime settings path uses one write-owning catalog operation;
5. prove no maintained runtime reader can observe dictionary clear or partial repopulation;
6. prove the public dictionary is never reassigned;
7. verify all seven established category keys and their path composition remain unchanged;
8. prove old-handler detachment and shared-cache retirement occur only after protected prior-generation readers finish;
9. prove candidate failure removes candidate event subscriptions while retaining the prior catalog and shared metadata cache;
10. prove shared metadata-cache retirement removes entries before best-effort per-entry disposal and publication detaches unpublished candidates after an unexpected transfer failure;
11. inspect every download/refresh path for a read-to-write upgrade;
12. confirm settings, refresh semaphore, catalog, handler, and event lock ordering;
13. verify no request/response, settings, model, event, or extension contract changed;
14. inspect the final changed-file set and staged commits so maintainer work is excluded; and
15. run `git diff --check` over the implementation range.

No build, launcher, automated test, GPU operation, or performance benchmark is run by the agent.

## Implementation Record

The integrated implementation range is `8c7585d9..4a2adf02`, ending at production commit `4a2adf02`. The thirteen production/source commits are `8c7084af`, `e485f504`, `080a1667`, `eb7cd91f`, `c7ccbb41`, `b921130b`, `d99730a1`, `d5559d5f`, `de28ddf7`, `4982d69b`, `048b8e6d`, `0852d085`, and `4a2adf02`; `8c7585d9`, `7253ad8e`, `6115f775`, and `56daa7c1` record the design, plan, and corrected inventory contracts. The range changes 25 files: 23 maintained source files plus this design and its implementation plan. The source files are `src/Backends/BackendHandler.cs`, `src/Backends/SwarmSwarmBackend.cs`, `src/BuiltinExtensions/AutoWebUIBackend/AutoWebUIAPIAbstractBackend.cs`, `src/BuiltinExtensions/ComfyUIBackend/ComfyUIAPIAbstractBackend.cs`, `src/BuiltinExtensions/ComfyUIBackend/ComfyUIBackendExtension.cs`, `src/BuiltinExtensions/ComfyUIBackend/ComfyUIWebAPI.cs`, `src/BuiltinExtensions/ComfyUIBackend/WorkflowGenerator.cs`, `src/BuiltinExtensions/ComfyUIBackend/WorkflowGeneratorModelSupport.cs`, `src/BuiltinExtensions/ComfyUIBackend/WorkflowGeneratorSteps.cs`, `src/Core/Installation.cs`, `src/Core/Program.cs`, `src/Core/WebServer.cs`, `src/Pages/_Generate/UtilitiesTab.cshtml`, `src/Text2Image/CommonModels.cs`, `src/Text2Image/T2IModelHandler.cs`, `src/Text2Image/T2IParamInput.cs`, `src/Text2Image/T2IParamSet.cs`, `src/Text2Image/T2IParamTypes.cs`, `src/Text2Image/T2IPromptHandling.cs`, `src/WebAPI/AdminAPI.cs`, `src/WebAPI/ModelsAPI.cs`, `src/WebAPI/T2IAPI.cs`, and `src/WebAPI/UtilAPI.cs`.

The final direct/derived catalog inventory contains 71 matching source lines across 20 maintained consumer files. The classification is two startup-only lines, two write-side lines, 57 lexically claimed lines, and ten centralized-provider lines whose callers establish the catalog boundary. The implementation contains 48 `RefreshLock` read claims and five `RefreshLock` write claims. Static call-chain inspection confirms that the centralized providers run only beneath a maintained outer read boundary, and that deferred handler-dependent work transfers an already-acquired claim rather than reacquiring behind a queued writer.

`Program` now owns the public write operations `BuildModelLists`, `RefreshAllModelSets`, `RefreshModelSet`, and `RebuildModelListsForPathChange`. Candidate construction, failed-candidate detachment, publication, build composition, and full refresh are implemented by the private `CreateModelLists`, `DetachUnpublishedModelLists`, `PublishModelLists`, `BuildModelListsCore`, and `RefreshAllModelSetsCore` helpers; the focused public refresh owns its claim directly. `AdminAPI.ChangeServerSettings` invokes one write-owning `RebuildModelListsForPathChange` transition after the settings transaction. The public `T2IModelSets` dictionary remains the same object and retains exactly `Stable-Diffusion`, `VAE`, `LoRA`, `Embedding`, `ControlNet`, `Clip`, and `ClipVision`.

The whole-project static conformance review ran these commands and recorded these results:

- `rg -n 'Program\.(T2IModelSets|MainSDModels)' src --glob '*.cs' --glob '*.cshtml' --glob '!src/bin/**' --glob '!src/obj/**' --glob '!src/Extensions/**'` returned 71 matching lines in 20 maintained consumer files; all lines classify as the two startup-only, two write-side, 57 lexically claimed, and ten centralized-provider lines above.
- `rg -n 'BuildModelLists\(|RefreshAllModelSets\(|RefreshModelSet\(|RebuildModelListsForPathChange\(|ModelRefreshEvent\?\.Invoke|ModelPathsChangedEvent\?\.Invoke|\.Refresh\(\)' src/Core src/WebAPI src/Text2Image src/Backends src/BuiltinExtensions --glob '*.cs' --glob '!src/bin/**' --glob '!src/obj/**'` proved that settings uses only `RebuildModelListsForPathChange`, each public catalog operation owns its write claim, private cores are called only by an owner or startup composition, `TriggerRefresh` preserves semaphore-before-write ordering, and no public write owner is called beneath another catalog claim.
- `rg -n -C 15 'LockRead\(\)|LockWrite\(\)|RefreshAllModelSets\(|RefreshModelSet\(|DownloadNow\(\)|DownloadModel\(|RunWebsocketHandlerCallWS|WaitForExitAsync|PostJson\(' src/Core src/WebAPI src/Text2Image src/Backends src/BuiltinExtensions src/Pages --glob '*.cs' --glob '*.cshtml' --glob '!src/Extensions/**'` found 48 `RefreshLock` read claims and five `RefreshLock` write claims and confirmed no nested catalog read, read-to-write upgrade, or claim spanning independent network, download, child-process, WebSocket, or backend/GPU work.
- `rg -n 'public static Dictionary<string, T2IModelHandler> T2IModelSets|T2IModelSets\s*=' src/Core/Program.cs` found the single public dictionary initialization and no reassignment.
- `rg -n 'AddModelSet\("(Stable-Diffusion|VAE|LoRA|Embedding|ControlNet|Clip|ClipVision)"' src/Core/Program.cs` found exactly the seven established keys with their preserved path expressions.
- `git diff 8c7585d9..4a2adf02 -- src/WebAPI/AdminAPI.cs src/Core/Program.cs` confirmed one `RebuildModelListsForPathChange` settings side effect, the four public write owners, same-object publication, unchanged settings request/response and warning behavior, and unchanged seven-key path composition.
- `git diff --check 8c7585d9..4a2adf02` reported no whitespace errors.
- `git status --short --untracked-files=no` and `git diff --name-only 8c7585d9..4a2adf02` showed the four protected maintainer files only as unstaged working-tree changes and the implementation range as exactly the 25 files recorded above; none of the protected files occur in the range.

Static review also confirmed unchanged public ABI surfaces, destination and metadata behavior, destination-aware workflow-cache validation, and explicit read/I/O/write/verification phase separation.

No agent ran a build, launcher, automated test, GPU operation, live concurrency exercise, or performance benchmark. No performance benefit is claimed. The maintainer validation record below is limited to the cases and platform scope Reaper176 confirmed.

## Maintainer Validation

On 2026-07-24, maintainer Reaper176 confirmed the following live matrix on the current Linux environment:

1. Normal build and launch, parameter listing, model browsing, and generation.
2. Idle model-root editing and restoration.
3. Overlapping path edits with parameter listing, browsing, conversion, metadata operations, Comfy workflow creation, remote serialization, and user-triggered refresh.
4. Generic and known-model downloads, LoRA extraction, TensorRT completion, and missing VAE/Clip download helpers.
5. Enabled Comfy self-start path reload.
6. Unavailable or unreadable path warning behavior.
7. Repeated edits and refreshes, shutdown, restart, and persisted/runtime agreement.
8. Unchanged payloads, category keys, visibility rules, download destinations, metadata behavior, built-in behavior, and applicable external-extension behavior.

This confirmation is Linux-only. Windows runtime behavior was not confirmed. No agent performed the live checks, no performance benchmark was run, and no performance improvement is claimed.

## Success Criteria

The project is complete when:

- one Program-owned write operation coordinates the maintained runtime path transition;
- maintained readers resolve and use handlers under the existing read boundary;
- the public dictionary object and all compatibility surfaces remain intact;
- readers cannot observe an empty or partially populated outer catalog;
- displaced handlers are not shut down while protected readers use them;
- candidate construction failure retains the prior complete catalog;
- refresh and download paths contain no read-to-write upgrade;
- the settings API retains its committed-success warning semantics;
- static verification finds no uncovered maintained runtime access; and
- the maintainer confirms the baseline, concurrency, download/refresh, failure, restart, event, and compatibility matrix.

## Rollback

The work is staged so each consumer group can return to its prior direct access while the write owner remains available. A full rollback must revert the centralized runtime operation and its reader claims together; retaining only one side would either restore the race or introduce blocking without writer coordination.

Rollback must not:

- reassign `T2IModelSets`;
- restore the settings-path clear-and-repopulate sequence without a write claim;
- leave public wrappers recursively acquiring the write lock;
- leave a read claim around a write-owning refresh call; or
- change the settings persistence guarantees established by the preceding project.
