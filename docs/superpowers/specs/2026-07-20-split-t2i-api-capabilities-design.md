# T2I API Capability Split Design

## Goal

Separate image history, Krita integration, and classic inpainting from `T2IAPI` into three capability-owned API classes without changing route names, registration order, permissions, update flags, reflection metadata, request or response shapes, public C# compatibility, or runtime behavior.

This is roadmap item 2 from the maintainability architecture audit. The project moves implementation ownership while retaining `T2IAPI` as the ordered route composition point and a compatibility facade.

## Current State

`T2IAPI` registers 21 routes and contains 2,694 lines spanning generation, direct history insertion, image-history filtering/indexing/file operations, Krita round-trips, process-backed classic inpainting, parameter listing, and model refresh.

The three extraction targets have different dependency shapes:

- Classic inpainting is a contiguous process-and-image operation cluster with two routes. Its public `RunProcessCapture` helper is also called by `BackendAPI`.
- Krita integration is a compact four-route cluster delegated to `KritaImageBridge`.
- Image history owns most of the class: insertion, filtering, sorting, index warmup, scanning, file-manager integration, deletion/move operations, metadata mutations, and associated state contracts. Grid Generator directly calls `T2IAPI.DeleteImage`.

External extensions may call other public `T2IAPI` methods or inspect their API metadata even where maintained-source searches find no caller. Compatibility therefore cannot be limited to currently visible call sites.

## Selected Architecture

Create three static API owners in `src/WebAPI`:

- `ClassicInpaintAPI`
- `KritaAPI`
- `ImageHistoryAPI`

Each owner contains the moved implementation, private helpers, API descriptions, and parameter annotations for its capability. `T2IAPI.Register()` remains the single ordered registration list and registers delegates from these new classes at the exact positions of the current delegates.

`T2IAPI` retains a forwarding method for every moved public method. Forwarders preserve the existing method name, visibility, return type, parameter order, parameter names, parameter types, default values, and existing method/parameter attributes. They contain no error translation or alternate behavior; they delegate directly to the capability owner.

This design keeps route composition visible in one place, gives API documentation the new capability owners as declaring types, and preserves source/reflection compatibility for callers using `T2IAPI.*`.

## Capability Ownership

### `ClassicInpaintAPI`

Owns these implementations:

- `GetIOPaintCommandCandidates`
- `RunProcessCapture`
- `PrepareClassicInpaintMask`
- `GetSupportedClassicInpaintBackends`
- `GetClassicInpaintBackends`
- `ClassicInpaint`

The registered routes are `GetClassicInpaintBackends` and `ClassicInpaint`. `BackendAPI` continues calling `T2IAPI.RunProcessCapture`; the legacy method forwards to `ClassicInpaintAPI.RunProcessCapture`.

The new owner retains existing command selection, process output collection, cancellation, mask normalization/expansion/feathering, backend probing, temporary-directory handling, logging, JSON errors, and cleanup behavior.

### `KritaAPI`

Owns these implementations and routes:

- `SendImageToKrita`
- `ImportKritaImage`
- `CheckPendingKritaImage`
- `GetActiveKritaSession`

The new owner retains all `KritaImageBridge` state interactions, image conversion, temporary-file behavior, active-session targeting, loopback/forwarded-request rejection, logs, and JSON errors.

### `ImageHistoryAPI`

Owns these public implementations and routes:

- `AddImageToHistory`
- `ListImages`
- `RescanImageMetadata`
- `OpenImageFolder`
- `DeleteImage`
- `BulkMoveImages`
- `ToggleImageStarred`
- `ToggleImageHidden`
- `SetImageRating`
- `SetImageTags`
- `SetImageNotes`

It also owns the private helpers for metadata field extraction, filter parsing/evaluation, indexed and scanned listing, sorting, index refresh/removal/warmup, file-manager integration, and related path/file operations.

`AddImageToHistory` calls `T2IAPI.RequestToParams`. This is an intentional temporary bridge: request conversion remains part of generation orchestration during this project.

Grid Generator continues calling `T2IAPI.DeleteImage`; the forwarder delegates to `ImageHistoryAPI.DeleteImage`.

## Legacy Public Data Contracts

The following remain declared and authoritative in `T2IAPI` during this project:

- `HistoryExtensions`
- `ImageHistorySortMode`
- `ImageHistoryHelper`
- `ImageHistoryIndexWarmups`
- `DeletableFileExtensions`

`ImageHistoryAPI` references these as `T2IAPI` contracts instead of introducing duplicate fields or types. This preserves field identity, mutable object identity, nested type names, and extension source compatibility. Migrating or encapsulating these contracts is a separate breaking-change project.

Generation-owned contracts such as `AlwaysTopKeys`, `SharedGenT2IData`, `RequestToParams`, generation methods, `RefreshSemaphore`, `TriggerRefresh`, and `ListT2IParams` also remain in `T2IAPI`.

## Route Registration and Reflection Compatibility

`BasicAPIFeatures` continues calling only `T2IAPI.Register()`.

Within `T2IAPI.Register()`, each moved delegate is qualified with its capability owner while retaining the existing sequence:

1. generation routes;
2. first history block from `AddImageToHistory` through `BulkMoveImages`;
3. four Krita routes;
4. two classic-inpaint routes;
5. history metadata routes from `ToggleImageStarred` through `SetImageNotes`;
6. parameter listing and refresh.

Every registration retains its current `isUserUpdate` value and `Permissions` object. Route names remain stable because the new implementation methods retain the original names.

The implementation methods carry the existing `APIDescription`, `APINonfinalMark` where applicable, and `APIParameter` attributes so generated documentation remains complete under the new declaring classes. Legacy forwarders retain matching annotations for callers that reflect over `T2IAPI` directly, but they are not registered as routes.

## Runtime Data Flow

For HTTP and WebSocket API calls:

1. `BasicAPIFeatures` invokes `T2IAPI.Register()` during startup.
2. `T2IAPI.Register()` registers a delegate from the appropriate capability owner.
3. `API` derives the unchanged route name from that method and performs existing permission checks and reflective argument binding.
4. The capability owner executes the moved implementation and returns the unchanged `JObject` or task result.

For direct C# compatibility calls:

1. Existing code calls the original `T2IAPI` method.
2. The forwarding method calls the identically named capability-owner method with the same arguments.
3. The original task/result and exceptions propagate without translation.

Shared history state remains singular because the new owner reads and mutates the retained `T2IAPI` contracts rather than copies.

## Error Handling and Behavioral Preservation

All existing path validation, permission assignment, cancellation tokens, filesystem behavior, recycle-bin choice, metadata/index updates, process handling, loopback validation, logs, and returned JSON move unchanged with their implementation.

Forwarders do not catch exceptions, add logging, reshape `JObject` values, or allocate replacement state. Async forwarders return or await the capability task consistently with the original signature. Synchronous helpers delegate synchronously.

No method body cleanup, validation change, performance optimization, naming correction, or algorithm modification is combined with extraction.

## Migration Stages

### Stage 1: Classic Inpainting

Create `ClassicInpaintAPI`, move its six implementations, replace the six `T2IAPI` methods with annotated forwarders, and register the two new route delegates at their existing positions.

This stage must preserve the `BackendAPI.RunProcessCapture` call path through the legacy facade.

### Stage 2: Krita

Create `KritaAPI`, move its four implementations, replace the four `T2IAPI` methods with annotated forwarders, and register the new delegates at their existing positions.

### Stage 3: Image History

Create `ImageHistoryAPI`, move its eleven public implementations and all solely owned private helpers, replace the public methods with annotated forwarders, and register the new delegates at their existing positions.

Leave the five public history data contracts in `T2IAPI` and qualify their use from the new owner. Preserve `T2IAPI.RequestToParams` as the direct insertion bridge and `T2IAPI.DeleteImage` as the Grid Generator compatibility path.

### Stage 4: Whole-Surface Audit

Compare the pre- and post-extraction route manifests, delegate ordering, update flags, permissions, method/parameter annotations, signatures/defaults, direct C# consumers, shared state references, and error/cancellation paths. Remove only using directives proven unused by the extraction; do not perform unrelated cleanup.

Each capability stage is a separate reviewable commit and must leave a coherent source state.

## Static Verification

Repository policy prohibits agents from running builds, tests, browser automation, or the live server. Verification therefore consists of:

- capturing and comparing ordered route registration manifests before and after each stage;
- comparing public method signatures, parameter names/types/defaults, and API annotations;
- confirming registered delegates point to the new owners while legacy methods are not registered;
- confirming every legacy forwarder resolves to exactly one owner implementation;
- confirming private helpers have one implementation owner and no stale references in `T2IAPI`;
- tracing `BackendAPI` through the `RunProcessCapture` forwarder and Grid Generator through the `DeleteImage` forwarder;
- confirming history state references use the retained `T2IAPI` contracts;
- checking C# structure, braces, names, using dependencies, whitespace, staged scope, and preserved maintainer changes;
- independent specification and code-quality review before completion.

## Manual Validation

The maintainer will validate the completed split in the live application:

1. Generate over WebSocket and add an image directly to history.
2. List history with pagination, filters, sort modes, fast-first loading, warmup, and metadata rescan.
3. Star/unstar, hide/unhide, set/clear rating, tags, and notes.
4. Delete images, bulk copy/move, open the containing folder, and exercise Grid Generator cleanup.
5. Send an image to Krita, import it, poll pending imports, query the active session, and confirm non-loopback/forwarded requests are rejected.
6. Discover classic-inpaint backends and exercise successful processing, disabled IOPaint, unsupported backend, invalid image/mask data, and failed-process cleanup.
7. Confirm route permissions, update behavior, error messages, and JSON response shapes remain unchanged.

## Non-Goals

- No route rename, addition, or removal.
- No permission or user-update flag change.
- No request, response, error, logging, cancellation, filesystem, or algorithm change.
- No dependency-injection or application-services work.
- No migration or encapsulation of public history fields and nested types.
- No removal of `T2IAPI` compatibility methods.
- No change to generation request conversion, generation orchestration, parameter listing, or refresh behavior.
- No frontend change.
- No extension API redesign.
- No build or test execution by agents.

## Success Criteria

- `ClassicInpaintAPI`, `KritaAPI`, and `ImageHistoryAPI` are the sole implementation owners for their capabilities.
- `T2IAPI` retains generation/model responsibilities, ordered route composition, legacy public data contracts, and thin compatibility forwarders only for moved methods.
- All 21 routes keep their names, order, permissions, update flags, reflective parameters, annotations, and response behavior.
- `BackendAPI` and Grid Generator remain source-compatible through the legacy facade.
- No history state is duplicated or disconnected.
- Static verification and independent reviews pass.
- The maintainer completes the manual validation matrix without regression.
