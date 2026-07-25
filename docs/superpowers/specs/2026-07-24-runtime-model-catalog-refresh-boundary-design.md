# Runtime Model Catalog Refresh Boundary Design

**Status:** Approved for implementation

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

The corrected implementation baseline contains 21 maintained consumer files and 71 matching source lines beyond `Program.cs`; `src/WebAPI/ModelsAPI.cs:375` contains two literal identifiers on one matching line, for 72 literal identifier references. The baseline classification is two startup-only accesses, three write-side accesses, five protected accesses, and 61 uncovered accesses, including the runtime Razor category enumeration at `src/Pages/_Generate/UtilitiesTab.cshtml:50`.

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
- a core publisher retires the displaced handlers and repopulates the existing public dictionary while a write claim excludes readers;
- a core refresher scans every published handler with the existing per-handler exception isolation;
- public compatibility entry points acquire the required write claim before invoking their core behavior; and
- one runtime path-change operation composes candidate construction, publication, refresh, and notification without recursively acquiring the write lock.

Startup may use the same core operations before concurrent runtime readers exist. The runtime settings path must use the centralized lock-owning operation.

## Runtime Path-Change Flow

After `ChangeServerSettings` has durably saved and published the settings candidate, its model-path side effect performs this sequence:

1. acquire `Program.RefreshLock` for writing;
2. construct all seven replacement handlers and configure their paths in a temporary dictionary;
3. if candidate construction fails, shut down every constructed candidate and retain the complete prior public catalog;
4. after the write claim has excluded protected readers, shut down the displaced handlers;
5. clear and repopulate the existing public dictionary with the complete seven-category candidate;
6. refresh every published handler, preserving per-handler logging and continuation;
7. invoke `ModelPathsChangedEvent` only after complete catalog publication; and
8. release the write claim after the composed runtime transition finishes.

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

`T2IModelHandler` construction subscribes each candidate to `ModelRefreshEvent`. A failed candidate must therefore be shut down so its subscription is removed. Candidate cleanup is best-effort and must not replace the original failure in logs or the settings warning.

Candidates are not refreshed while the prior generation is live. The existing static metadata-cache behavior requires the displaced handlers to shut down and clear their shared cache before replacement handlers scan and acquire fresh cache entries.

Publication mutates the existing dictionary only while the write claim excludes maintained readers. All seven configured categories are inserted before the first replacement refresh begins. A scan failure may leave one complete category with an empty or partial inner model map according to existing `T2IModelHandler.Refresh` behavior, but it cannot expose an incomplete outer category map.

## Public Compatibility Entry Points

`BuildModelLists` and `RefreshAllModelSets` remain public compatibility entry points. Their externally callable forms become write-side owners. Private core helpers allow the centralized runtime path operation and startup sequence to reuse their behavior without nested write claims.

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

Claims cover the shortest complete synchronous catalog operation. They may include:

- resolving a category and enumerating its model map;
- selecting a model and copying its path or metadata reference;
- converting a parameter against the current model names;
- constructing the part of a workflow that reads the catalog;
- forming parameter-list model data;
- producing remote category serialization; or
- validating a known-model destination.

Claims should not remain held during work that no longer reads the handler generation, including:

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

## Lock Ownership and Ordering

The lock order is:

1. the server-settings transaction completes before a catalog write claim is acquired;
2. `T2IAPI.RefreshSemaphore` may be acquired before `RefreshLock` for the existing user-triggered refresh flow;
3. a catalog claim may be held while entering a handler's modification or metadata lock through existing handler operations;
4. code must not hold a handler-local modification or metadata lock while newly acquiring the catalog write claim; and
5. code must never upgrade directly from a catalog read claim to a catalog write claim.

The path-change operation is synchronous, matching the existing `ChangeServerSettings` side-effect contract.

`ModelRefreshEvent` is already published under the user-triggered write claim. The path-change event is likewise published only after the replacement catalog is structurally complete. Event consumers execute as write-side callbacks and must not reacquire `RefreshLock`.

## Failure Semantics

### Before publication

If candidate creation or path configuration throws:

- keep the prior public catalog unchanged and usable;
- shut down every candidate handler that was constructed;
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

Normal shutdown retains final handler-disposal ownership. Runtime replacement shuts down only the displaced generation while its write claim excludes protected readers.

## Compatibility Requirements

The implementation preserves:

- the `Program.T2IModelSets` public field and its object identity;
- `Program.MainSDModels`;
- all seven category names;
- `T2IModelHandler` and `T2IModel` object behavior;
- public `BuildModelLists`, `RefreshAllModelSets`, and `RefreshLock` access;
- `ModelRefreshEvent` and `ModelPathsChangedEvent` delegate surfaces and relative purpose;
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
8. prove old-handler shutdown occurs only after protected prior-generation readers finish;
9. prove candidate failure removes candidate event subscriptions and retains the prior catalog;
10. inspect every download/refresh path for a read-to-write upgrade;
11. confirm settings, refresh semaphore, catalog, handler, and event lock ordering;
12. verify no request/response, settings, model, event, or extension contract changed;
13. inspect the final changed-file set and staged commits so maintainer work is excluded; and
14. run `git diff --check` over the implementation range.

No build, launcher, automated test, GPU operation, or performance benchmark is run by the agent.

## Maintainer Validation

The maintainer will validate the implementation in the live application.

### Baseline

- Build and launch normally.
- List parameters and every model category.
- Browse models and complete a representative generation.
- Confirm existing category names, visibility filtering, payloads, and model paths.

### Normal path transition

- Change model roots while the server is idle.
- Confirm the settings request completes synchronously.
- Confirm all seven categories describe the new roots.
- Restore the original roots and confirm the original catalog returns.
- Confirm enabled Comfy self-start backends receive the existing path-change reload.

### Concurrent readers

Overlap a model-root edit with:

- parameter listing;
- model browsing and special model viewing;
- generation request conversion;
- model metadata read/edit operations;
- Comfy workflow construction;
- remote-backend model serialization; and
- a user-triggered full refresh.

Each operation must either complete against the prior catalog or resume against the complete replacement catalog. No operation may report a missing built-in category, throw because the outer dictionary is empty/partial, or continue using a shut-down handler resolved outside a claim.

### Download and refresh deadlock checks

Exercise:

- generic model download;
- known-model auto-download;
- LoRA extraction;
- TensorRT completion and refresh; and
- any workflow path that downloads a missing VAE or Clip model.

Confirm progress remains live, refresh completes, verification sees the new model, and a concurrent path edit does not deadlock.

### Failure and repetition

- Use an unavailable or unreadable model location that reaches maintained validation/refresh behavior.
- Confirm warning behavior is bounded and the outer dictionary remains structurally complete.
- Repeat path edits and full refreshes.
- Shut down after an edit or refresh.
- Restart and confirm final persisted paths and model lists agree.

### Compatibility

- Confirm unchanged API request and response schemas.
- Confirm unchanged category keys and model visibility rules.
- Confirm unchanged download destinations and metadata behavior.
- Confirm built-in extension model lists and Comfy workflows still resolve.
- Exercise any installed external extension known to enumerate `T2IModelSets` under the public coordination contract.

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
