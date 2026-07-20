# SwarmUI Maintainability Architecture Audit

## Executive Summary

SwarmUI has recognizable subsystem boundaries, but its most expensive maintenance problems occur where those boundaries are represented only by convention: `Program.*` is a process-wide service locator, browser globals and script order form an implicit dependency graph, and the Comfy adapter duplicates node contracts across C# and Python. The code is not best served by a wholesale rewrite. Several high-value seams can be extracted while preserving routes, globals, extension hooks, and runtime behavior.

The recommended first project is to move the Image Editing tab's UI coordinator out of `currentimagehandler.js`. It is a contiguous, feature-owned block of roughly 2,440 lines with a small, identifiable compatibility surface: Razor inline handlers, the lazy-tab activation hook, Bootstrap tab events, and the Generate-tab editor bridge. A behavior-preserving move would materially reduce the largest frontend ownership collision while exercising the exact compatibility pattern needed for later frontend facades.

The ranked roadmap then separates API route capabilities, image-history collaborators, and Swarm-maintained Comfy node contracts before approaching the higher-risk process services, backend scheduler, and workflow generator. The recurring migration rule is: establish a narrow owner, delegate through the old public surface, migrate consumers in stages, and remove compatibility exports only after core and extension usage is known.

## Scope and Method

This audit covers maintained core SwarmUI browser code, Razor integration, CSS ownership, the C# server, built-in extensions, and SwarmUI-managed Python nodes. It uses static inspection only; repository policy prohibits agents from running builds or tests. `docs/project-memory.md` contained no reusable notes at the start of the audit.

Excluded targets are external extensions under `src/Extensions`; upstream code under `dlbackend` and `src/BuiltinExtensions/ComfyUIBackend/DLNodes`; generated paths including `src/bin`, `src/obj`, `.vs`, and `.git`; local user data under `Data`, `Output`, and `Models`; and vendored libraries except where maintained integration code depends on them.

Findings are ranked qualitatively by maintainability payoff, leverage across the system, feasibility of incremental migration, and regression risk. File and symbol counts direct inspection but are not independent reasons to refactor.

## Current Architecture

### Browser Frontend

The shared Razor layout loads vendor scripts followed by `util.js`, `translator.js`, `permissions.js`, and `site.js`; page-specific scripts are classic scripts appended through the Razor `Scripts` section (`src/Pages/Shared/_Layout.cshtml:1-49`). The generation page injects server-derived feature flags, parameter remaps, lazy-tab metadata, and lazy script manifests onto `window`, then loads the main generation scripts in a hand-maintained order and finishes with `finalscript.js`, whose sole job is to call `genpageLoad` after the preceding scripts execute (`src/Pages/Text2Image.cshtml:21-98`, `src/Pages/Text2Image.cshtml:154-186`, and `src/wwwroot/js/genpage/helpers/finalscript.js:1-3`).

`site.js` provides session-aware HTTP/WebSocket transport plus shared form, prompt, media-input, modal, and slider behavior. `util.js` provides lower-level transport, DOM, escaping, parsing, formatting, cookie, file, and media helpers. Both expose top-level classic-script declarations. The recently added `window.SwarmUtil` object is a compatibility export over selected existing `util.js` functions, not yet an independent module (`src/wwwroot/js/util.js:1286-1318`).

`main.js` holds generation-page state and boot orchestration. It obtains a session, loads model/parameter metadata, builds inputs, initializes callbacks, starts status polling, and coordinates lazy tab markup/scripts (`src/wwwroot/js/genpage/main.js:1-180`, `src/wwwroot/js/genpage/main.js:863-1221`, and `src/wwwroot/js/genpage/main.js:1352-1436`). Feature files communicate through shared top-level functions and variables: parameter rendering and collection in `params.js`, models in `models.js`, generation transport in `GenerateHandler`, current/batch image UI in `currentimagehandler.js`, and saved-output browsing in `outputhistory.js`.

Styling follows the same global-page model. `_Layout.cshtml` loads site and theme styles; `Text2Image.cshtml` adds `genpage.css`. The latter is 4,314 lines and contains prompt lab, model browser, image history, current-image, image-editor, server, utility, and responsive layout styles in one cascade.

### C# Server

`Program.Main` is the manual composition root and lifecycle owner. It constructs the central model handlers, `BackendHandler`, `SessionHandler`, and `WebServer`; invokes extension phases; registers the core API; launches the web host; and later shuts the same services down. Most of those instances, settings, cancellation signals, and lifecycle events are exposed as mutable static members on `Program` (`src/Core/Program.cs:27-110`, `src/Core/Program.cs:313-351`, and `src/Core/Program.cs:558-595`).

`WebServer` builds the ASP.NET application and owns middleware, Razor Pages, top-level routes, static output/extension serving, theme registration, and extension-provided page fragments (`src/Core/WebServer.cs:31-176`, `src/Core/WebServer.cs:176-365`, and `src/Core/WebServer.cs:367-777`). `/API/{*Call}` is dispatched to the static `API` registry. `BasicAPIFeatures.Register` registers its own account/session routes and delegates to the other route groups (`src/WebAPI/BasicAPIFeatures.cs:24-67`); `API.HandleAsyncRequest` handles transport parsing, session resolution, permission checks, reflective argument binding, and response transport (`src/WebAPI/API.cs:16-224`).

The generation domain is centered on `T2IParamTypes`, `T2IParamInput`, model handlers, `T2IEngine`, and backend abstractions. `BackendHandler` combines backend-type registration, configured-instance persistence and lifecycle, availability monitoring, autoscaling hooks, model-load pressure, and request scheduling (`src/Backends/BackendHandler.cs:17-817` and `src/Backends/BackendHandler.cs:862-1468`). Built-in backends adapt that domain to concrete engines; the ComfyUI backend converts `T2IParamInput` into JSON workflows through `WorkflowGenerator` and its ordered step registry.

### Built-in Extensions and Managed Python

`ExtensionsManager` discovers built-in extension types from the main assembly and external extension types from isolated load contexts, instantiates them, and drives the lifecycle phases that `Program` orders (`src/Core/ExtensionsManager.cs:11-385` and `src/Core/Program.cs:204-351`). The `Extension` base is a contribution container: implementations add scripts, styles, tabs, page injections, settings, and other assets during `OnPreInit`, `OnInit`, and `OnPreLaunch`; APIs, permissions, parameter types, backend types, and workflow steps are added through global registries. `WebServer.GatherExtensionPageAdditions` turns the contribution lists into global page fragments (`src/Core/Extension.cs:5-159` and `src/Core/WebServer.cs:367-414`).

Small built-ins generally use that seam directly. Image Batch Tool contributes a script and API; Grid Generator contributes script/style, parameters, APIs, and generation/history logic; Dynamic Thresholding contributes parameters and a workflow step. ComfyUIBackend is substantially larger: `ComfyUIBackendExtension` owns workflow-file storage, feature detection from Comfy object metadata, parameter registration, backend registration/update hooks, scripts/styles, and lifecycle coordination (`src/BuiltinExtensions/ComfyUIBackend/ComfyUIBackendExtension.cs:18-1060`). `ComfyUIAPIAbstractBackend` handles Comfy transport and turns `T2IParamInput` into a `WorkflowGenerator` workflow before sending it to the backend.

SwarmUI's maintained Python surface contains about 4,993 lines under `ExtraNodes`. `SwarmComfyCommon/__init__.py` composes feature-level `NODE_CLASS_MAPPINGS`; files are already divided into image, mask, sampler, text, input, audio/video, model, and integration nodes. `SwarmComfyExtra` makes dependency-heavy RemBG, animation, YOLO, and tunable-operation modules optional. The C# generator selects these nodes by string class name and builds their JSON input dictionaries; Python maps the same names to classes and declares input/output schemas.

### Cross-Layer Data Flow

The primary generation path is:

1. `GenerateHandler` gathers values through the global `getGenInput` surface and opens `GenerateText2ImageWS` through `makeWSRequestT2I`/`makeWSRequest` (`src/wwwroot/js/genpage/helpers/generatehandler.js:1-631`, `src/wwwroot/js/genpage/gentab/generatecontrols.js:1-17`, and `src/wwwroot/js/site.js:105-151`).
2. `WebServer` maps `/API/{*Call}` to `API.HandleAsyncRequest`. `API` parses JSON, resolves `Session`, checks the registered `PermInfo`, reflectively binds arguments, and calls `T2IAPI.GenerateText2ImageWS` (`src/Core/WebServer.cs:327-335` and `src/WebAPI/API.cs:16-224`).
3. `T2IAPI` converts raw keys through `T2IParamTypes` into `T2IParamInput`, expands each image request, acquires a `Session.GenClaim`, and schedules `T2IEngine.CreateImageTask` (`src/WebAPI/T2IAPI.cs:92-493`).
4. `T2IEngine` applies prompt/tool preprocessing, asks `Program.Backends.GetNextT2IBackend` for a claimed compatible backend, and calls `AbstractT2IBackend.GenerateLive` (`src/Text2Image/T2IEngine.cs:185-369` and `src/Backends/BackendHandler.cs:862-1468`).
5. For Comfy, `ComfyUIAPIAbstractBackend` builds a JSON graph using `WorkflowGenerator`, submits it, and translates Comfy progress/output events. Swarm-maintained nodes can send previews/final media through `SwarmSaveImageWS` and animation nodes (`src/BuiltinExtensions/ComfyUIBackend/ComfyUIAPIAbstractBackend.cs:762-1109`, `src/BuiltinExtensions/ComfyUIBackend/WorkflowGenerator.cs:907-918`, and `src/BuiltinExtensions/ComfyUIBackend/ExtraNodes/SwarmComfyCommon/SwarmSaveImageWS.py:11-155`).
6. `T2IEngine` and `T2IAPI` turn backend objects into WebSocket progress, image, status, and discard messages. `GenerateHandler` updates `currentimagehandler.js`; saved outputs and metadata are later indexed through `OutputMetadataTracker` and browsed/mutated by `outputhistory.js` and the image-history routes still housed in `T2IAPI`.

The intended direction—browser feature to transport, API dispatch, application orchestration, backend abstraction, adapter/workflow, then output path—is visible. The main deviations are cross-layer access through `Program.*`, browser global state shared laterally between features, and duplicated string/JSON contracts between the Comfy adapter and managed Python nodes.

## Evidence-Backed Findings

### Process composition is also a global service locator

- Evidence: `Program` publicly exposes `Backends`, `Sessions`, `T2IModelSets`, `Extensions`, `ServerSettings`, `Web`, `GlobalProgramCancel`, and lifecycle events (`src/Core/Program.cs:27-110`). Static inspection found `Program.ServerSettings` referenced from 40 maintained C# files, `Program.GlobalProgramCancel` from 23, `Program.Backends` from 18, and `Program.T2IModelSets` from 16. Consumers span accounts, model handling, APIs, backend implementations, built-in extensions, and utilities rather than a single infrastructure layer.
- Impact: dependencies and lifecycle requirements are implicit. A class can acquire mutable process state without declaring it, and changes to startup, shutdown, settings, or model ownership require repository-wide reasoning.
- Boundary opportunity: retain `Program` as the executable entry point but introduce a small application-services/lifecycle context with explicit, read-only service access. Migrate one dependency cluster at a time behind compatibility properties before changing construction or extension contracts.
- Caveat: `Program.*` is a de facto internal extension API. Removing static members directly would create wide regressions; an incremental facade is mandatory.

### `T2IAPI` owns several unrelated application capabilities

- Evidence: `T2IAPI.Register` registers 21 routes covering generation, classic inpainting, image history, file operations, Krita integration, parameter listing, and model refresh (`src/WebAPI/T2IAPI.cs:27-51`). Generation transport and orchestration occupy `GenerateText2ImageWS`, `GenerateText2Image`, `RequestToParams`, and `GenT2I_Internal` (`src/WebAPI/T2IAPI.cs:92-493`). Classic inpaint and process launching occupy `RunProcessCapture` through `ClassicInpaint` (`src/WebAPI/T2IAPI.cs:547-767`). History filtering, indexing, listing, file operations, metadata mutation, and warmup span `MetadataIsHidden` through `SetImageNotes` (`src/WebAPI/T2IAPI.cs:769-2542`). Krita routes are interleaved at `src/WebAPI/T2IAPI.cs:2033-2110`.
- Impact: image-history changes share a 2,694-line static class and registration surface with the most critical generation path. Navigation is costly, ownership is unclear, and routine history or desktop-integration work increases the review surface around generation.
- Boundary opportunity: first extract route groups without changing route names or reflective signatures: `ImageHistoryAPI`, `KritaAPI`, and `ClassicInpaintAPI`, leaving generation request conversion and orchestration in `T2IAPI`. Move private helpers with their sole consumers and preserve a compatibility registration order through `BasicAPIFeatures.Register`.
- Caveat: route names derive from method names and permissions are assigned at registration. Moving methods must preserve names, attributes, permission objects, history-index synchronization, and WebSocket behavior.

### `BackendHandler` combines catalog, lifecycle, persistence, and scheduling state

- Evidence: the same class owns `AllBackends`, `BackendTypes`, IDs, save state, initialization signals, and autoscaling callbacks (`src/Backends/BackendHandler.cs:17-115`); registers and instantiates backend types (`src/Backends/BackendHandler.cs:232-412`); loads, reloads, monitors, persists, and shuts down configured instances (`src/Backends/BackendHandler.cs:413-840`); and implements `ModelRequestPressure`, `T2IBackendRequest`, the request loop, backend selection, and model-loading heuristics (`src/Backends/BackendHandler.cs:862-1445`).
- Impact: configuration edits, backend lifecycle, and generation scheduling share locks, signals, dictionaries, and shutdown state. Changes to one policy require understanding the others, and scheduler behavior cannot be reasoned about through a narrow contract.
- Boundary opportunity: define an internal backend catalog/lifecycle owner and a request scheduler that consumes a read-only availability interface. Begin by extracting query and pressure-selection policy while keeping storage and thread ownership in `BackendHandler` as a facade.
- Caveat: this code coordinates concurrency, usage claims, model loading, autoscaling, and cancellation. Any extraction must preserve lock order, signals, `T2IBackendAccess.Dispose`, and current scheduling heuristics exactly before policy changes are considered.

### Comfy workflow generation is split physically but not by state ownership

- Evidence: `WorkflowGenerator` is a partial class totaling 4,898 lines across `WorkflowGenerator.cs` and `WorkflowGeneratorModelSupport.cs`, while the 2,451-line `WorkflowGeneratorSteps.Register` fills global ordered `Steps` and `ModelGenSteps`. The generator exposes broad mutable workflow state, current model/media trackers, mode flags, node IDs, and compatibility properties (`src/BuiltinExtensions/ComfyUIBackend/WorkflowGenerator.cs:25-190`). Its methods cover node creation, LoRA schedules, media loading/cropping, samplers, prompt conditioning, regional prompting, video, and graph rewriting. Model-specific predicates and loaders add another large responsibility surface (`src/BuiltinExtensions/ComfyUIBackend/WorkflowGeneratorModelSupport.cs:14-1493`).
- Impact: adding model support or a workflow feature often changes shared mutable state and ordered global steps. Physical partial-file separation helps navigation only slightly because responsibilities still communicate through the entire generator object.
- Boundary opportunity: establish focused collaborators around the existing JSON/node primitives—model loading, conditioning, media initialization, sampling, and output assembly—while `WorkflowGenerator` remains the stateful orchestrator and compatibility facade. Replace anonymous registration blocks with named step objects only after collaborators exist.
- Caveat: step priority and reserved node IDs are behavioral contracts, and built-in/external extensions can call public generator members. This is high-value but high-risk and should follow safer boundary extractions.

### Parameter catalog, serialization, validation, and file resolution share one static registry

- Evidence: `T2IParamTypes` owns the global `Types` and `ParameterRemaps` registries, registration and naming helpers, hundreds of public static registered-parameter fields, group definitions, extension fake-type providers, and the 600-plus-line `RegisterDefaults` catalog (`src/Text2Image/T2IParamTypes.cs:214-995`). It also performs network serialization, option lookup, user-aware validation, output-file resolution, and application into `T2IParamInput` (`src/Text2Image/T2IParamTypes.cs:106-177` and `src/Text2Image/T2IParamTypes.cs:996-1293`).
- Impact: defining a parameter, rendering it for clients, validating user input, resolving model/file values, and applying it all depend on one static type. Catalog growth increases cognitive load, and validation acquires sessions, model registries, filesystem rules, and output-history behavior implicitly.
- Boundary opportunity: first separate default catalog registration into cohesive category registrars while preserving every public static field. Later isolate validation/application behind a service that receives model and file resolvers explicitly.
- Caveat: extensions register parameters and reference the public static fields directly. The registry and field identities must remain stable through any file or helper extraction.

### `Utilities` remains a cross-domain dependency despite prior helper extraction

- Evidence: `Utilities` contains tick-loop/lifecycle helpers, filename and random utilities, timers, WebSocket and HTTP JSON transport, JSON conversion, process control, downloads and hashing, path operations, resolution math, Python cache cleanup, hardware/memory operations, .NET checks, Git execution, recycle-bin integration, exception formatting, and password hashing (`src/Utils/Utilities.cs:27-1484`). More focused helpers such as `WebUtil`, `PythonLaunchHelper`, `MetadataHelper`, and `OutputMetadataTracker` already exist alongside it.
- Impact: unrelated code depends on a broad static namespace, making ownership and safe reuse harder to infer. The class also mixes pure operations with I/O and process-wide side effects.
- Boundary opportunity: continue opportunistic extraction by domain when a ranked project already touches a coherent cluster; do not perform a standalone bulk move.
- Caveat: these helpers have broad call-site compatibility value. A repository-wide utility reorganization would create churn without a proportionate immediate payoff.

### Classic-script globals make load order the frontend dependency graph

- Evidence: `Text2Image.cshtml` carries an ordered list of generation scripts and `finalscript.js` intentionally loads last. Static inventory found roughly 817 top-level `let`, `class`, `function`, or explicit `window.*` declarations across maintained browser scripts; `currentimagehandler.js`, `outputhistory.js`, and `main.js` alone contribute 197, 136, and 102. Shared functions such as `genericRequest`, `getGenInput`, `setCurrentImage`, and `sessionReadyCallbacks` are referenced across 21, 8, 5, and 3 files respectively without import declarations. Lazy tabs add a second implicit contract between Razor-provided `window.genpageLazyTabs`/`window.genpageLazyScriptGroups`, `main.js` loading state/hooks, and functions that appear only after scripts and partial markup load (`src/Pages/Text2Image.cshtml:33-98` and `src/wwwroot/js/genpage/main.js:863-1221`).
- Impact: a file's dependencies, initialization timing, and public surface cannot be determined from the file itself. Moving a script or declaration can fail only on a particular tab-opening or session-ready path, and ownership tends to accumulate in already-loaded files because they are convenient global hosts.
- Boundary opportunity: introduce explicit singleton namespaces by feature, starting at existing seams rather than converting the whole frontend to modules. Each feature facade should own initialization and a documented compatibility export; keep the Razor order stable until all consumers of that feature have migrated.
- Caveat: inline Razor/extension scripts and extension-provided page content rely on globals. Native-module conversion or global removal would require an extension compatibility design and is not a safe first step.

### Image Editing tab orchestration is embedded in the current-image file

- Evidence: `currentimagehandler.js` begins with image-card indexing, full-view behavior, batch/current-image actions, metadata copying, and save/star operations (`src/wwwroot/js/genpage/gentab/currentimagehandler.js:1-1554`). Lines 1560-3999 then define Image Editing tab state, more than one hundred `imageEditing*` DOM getters/actions, layer adjustments, selection/effect controls, splitters, zoom/color controls, editor initialization, and tab-to-tab transfer before current-image handling resumes at `getImageFullSrc`. Across maintained scripts, only `currentimagehandler.js` and the lazy hook in `main.js` reference the `imageEditing*` surface; the editor engine and tool class files do not own that UI coordinator.
- Impact: a 5,402-line file nominally responsible for current generated images also owns most Image Editing tab presentation and lifecycle. Current-image, batch preview, image history, full-view, editor, and comparison changes therefore collide in one global declaration surface.
- Boundary opportunity: extract the contiguous Image Editing tab coordinator into `helpers/image_editor_ui.js`, loaded with `color_picker.js`, `image_editor_tools.js`, and `image_editor.js`. Preserve the existing `imageEditing*` function names initially, then wrap them behind one singleton after the file move is stable.
- Caveat: the coordinator bridges the Generate-tab editor and Image Editing tab and listens to Bootstrap tab events. Its load position must preserve the `main.js` lazy hook, `window.imageEditor`, partial-markup timing, and tab transfer behavior.

### Image history combines transport, indexing UI, comparison, bulk actions, and rendering

- Evidence: `outputhistory.js` contains request retry/watchdog state and browser-shell initialization (`src/wwwroot/js/genpage/gentab/outputhistory.js:1-567`), client-side filter parsing (`src/wwwroot/js/genpage/gentab/outputhistory.js:568-871`), comparison modal state and rendering (`src/wwwroot/js/genpage/gentab/outputhistory.js:892-1338`), sorting and selection (`src/wwwroot/js/genpage/gentab/outputhistory.js:1344-1753`), bulk export/edit operations (`src/wwwroot/js/genpage/gentab/outputhistory.js:1754-2230`), server mutations, list loading, and item description (`src/wwwroot/js/genpage/gentab/outputhistory.js:2231-2818`). It shares `imageHistoryBrowser` and `setCurrentImage` with `currentimagehandler.js`.
- Impact: server-list performance work, filter behavior, comparison UI, and metadata mutations share 136 top-level declarations and mutable selection/request state. The browser adapter is not separated from optional tools, so changes amplify across unrelated history capabilities.
- Boundary opportunity: retain one `ImageHistoryController` facade but extract comparison, filtering, and bulk-action collaborators in that order. The controller should remain the sole owner of `GenPageBrowserClass`, request tokens, selection, and refresh scheduling.
- Caveat: client filtering mirrors server filtering in `T2IAPI`, and optimistic saved-image insertion/mutation must stay synchronized with `SwarmImageCard` metadata. Splits should not change algorithms until those contracts are documented.

### `genpage.css` obscures component ownership in one global cascade

- Evidence: `src/wwwroot/css/genpage.css` contains 4,314 lines versus 1,323 in `site.css`. Named selector clusters for Prompt Lab start near line 164, Image Editing tools near 1060 and again from 3112, current-image/batch UI near 1334, models near 1552, and image-history comparison near 1779, with other feature and responsive rules interleaved. JavaScript also changes classes/styles heavily in `currentimagehandler.js`, `layout.js`, the editor files, and `outputhistory.js`.
- Impact: feature markup, behavior, and styling do not live behind the same boundary. Cascade/order effects make a feature extraction harder to review and encourage unrelated CSS edits in the same file.
- Boundary opportunity: whenever a JS feature boundary is extracted, move its uniquely prefixed selector clusters to an adjacent stylesheet loaded at the same position. Begin with Image Editing because its selectors are strongly prefixed and its JS coordinator already has a clear extraction seam.
- Caveat: theme overrides and responsive selectors may depend on current order/specificity. CSS should move feature-by-feature with computed-style/manual viewport checks, not by mechanical selector prefix alone.

### `SwarmUtil` is a migration seam, not a final utility architecture

- Evidence: `window.SwarmUtil` re-exports selected functions defined earlier in `util.js` and is currently consumed by five maintained files. Direct calls remain widespread: static search found direct `escapeHtml` calls in 20 files versus namespaced calls in four, `createDiv` in 18 versus two, and `imageToData` in five versus one. Meanwhile `util.js` and `site.js` still mix transport, DOM creation, parsing, formatting, session handling, prompt behavior, and input construction.
- Impact: the namespace makes dependencies more visible but, if expanded indiscriminately, would reproduce the same global utility surface behind one object and blur browser infrastructure with feature helpers.
- Boundary opportunity: finish compatibility migration only for truly general primitives, then stop growing `SwarmUtil`. Feature-specific helpers should move to their owning singleton as feature boundaries are created.
- Caveat: direct globals remain required compatibility exports until all core and extension consumers are known. Namespace adoption alone does not justify deleting original functions.

### The Comfy C#/Python boundary is an untyped duplicated contract

- Evidence: Python registers more than fifty `Swarm*` node class names in per-file `NODE_CLASS_MAPPINGS`, including `SwarmLoadImageB64`, `SwarmKSampler`, mask helpers, SAM helpers, `SwarmSaveImageWS`, and media utilities. C# repeats those literal names and input keys in `WorkflowGenerator.cs`, `WorkflowGeneratorSteps.cs`, `WorkflowGeneratorModelSupport.cs`, `WGNodeData.cs`, `ComfyUIAPIAbstractBackend.cs`, and `ComfyUIWebAPI.cs`. For example, `WorkflowGenerator.CreateNode("SwarmLoadImageB64", ...)` must match `SwarmLoadImageB64.py` registration and `INPUT_TYPES`; the same coupling exists for dozens of graph nodes. `ComfyUIBackendExtension.FeatureMap` separately maps selected node names to feature flags (`src/BuiltinExtensions/ComfyUIBackend/ComfyUIBackendExtension.cs:39-70`).
- Impact: renaming a node or input can silently produce backend workflow errors because there is no maintained shared declaration or static validation at the C# boundary. Capability detection, generation, and Python registration can drift independently.
- Boundary opportunity: add a C# `ComfyNodeContract` catalog for Swarm-maintained class names, required input keys, and feature IDs, then migrate generator calls to it. Keep Python mappings authoritative at runtime and compare the catalog against Comfy's existing object-info response during startup/manual verification.
- Caveat: upstream Comfy nodes are intentionally dynamic and must remain string-addressed. The catalog should cover only Swarm-maintained nodes and must not imply compile-time knowledge of optional Python dependencies.

### `ComfyUIBackendExtension` is both extension entry point and subsystem container

- Evidence: the class loads/caches custom workflows (`LoadWorkflowFiles`, `GetWorkflowByName`, `Refresh`), coordinates backend restart/model-path changes and arbitrary workflows, interprets Comfy object-info capabilities (`AssignValuesFromRaw`), declares a large parameter surface, registers backend types, manages install/update behavior, and implements four lifecycle hooks (`src/BuiltinExtensions/ComfyUIBackend/ComfyUIBackendExtension.cs:18-1060`). Those responsibilities then feed static members consumed by workflow generation and Web APIs.
- Impact: a change to workflow storage, feature detection, parameter catalog, or backend installation shares one extension instance and static state surface. The extension lifecycle methods obscure which subsystem must be ready at each phase.
- Boundary opportunity: keep the extension class as composition root while extracting `ComfyWorkflowStore`, `ComfyCapabilityCatalog`, and backend-install/update coordination. Start with the capability catalog because it aligns with the Swarm-node contract work.
- Caveat: extension lifecycle ordering and static parameter identities are compatibility requirements. Extraction should delegate from existing members before changing consumers.

### The general extension seam is broad but currently useful

- Evidence: `Extension` exposes separate lists for scripts, styles, tabs, generic pages, settings, backend hooks, and lifecycle methods (`src/Core/Extension.cs:5-159`), while built-ins register APIs and parameters through global registries. Despite its breadth, the smaller built-ins remain locally understandable and use the same contribution points as external extensions.
- Impact: global registries and lifecycle timing contribute to system-wide implicit dependencies, but changing this seam would affect the widest compatibility surface in the repository.
- Boundary opportunity: improve core composition and feature facades behind the existing extension API first. Later, typed contribution registries could delegate from the current lists and lifecycle hooks.
- Caveat: extension API redesign is not justified as an early roadmap item; current extension behavior is a constraint on other refactors, not itself the highest-pressure implementation target.

## Ranked Refactoring Roadmap

The ordering favors prerequisite boundaries, then maintainability payoff and cross-system leverage, then incremental feasibility and lower regression risk. It is intentionally qualitative: a lower-ranked project can be locally valuable, but should not bypass a boundary above it when that would duplicate compatibility work or expose unstable shared state.

### 1. Extract the Image Editing UI coordinator

- Boundary: move Image Editing tab state, DOM coordination, initialization, actions, and tab-transfer behavior from `currentimagehandler.js` to `helpers/image_editor_ui.js`; retain current-image, batch, full-view, and comparison behavior in the original file.
- Evidence: the coordinator is a contiguous block around lines 1560-3999 of a 5,402-line file. Its maintained consumers are the Image Editing Razor partial, `main.js`'s lazy activation hook, and later current-image actions that open an editor.
- Payoff: High because it removes the clearest ownership collision from the largest frontend host file.
- Leverage: High because it establishes the facade/load-order pattern for other browser features.
- Feasibility: High because the code and consumers form a contiguous, identifiable seam.
- Risk: Medium because lazy initialization and two editor instances create timing regressions.
- Prerequisites: inventory the complete global surface and capture both editor-transfer directions; keep the committed lazy-script behavior as the baseline and account for overlapping uncommitted Razor/main changes during implementation.
- Migration: (1) move the block without renaming declarations; (2) load the new file after `image_editor.js`; (3) preserve inline-handler and `main.js` globals; (4) statically trace all moved symbols; (5) only after manual validation, consider wrapping internals in an `ImageEditingUI` singleton and moving uniquely owned CSS.
- Manual validation: first-open and repeat-open Image Editing, every tool/control group, layers, selection/crop/effects, zoom and splitters, current/history image transfer, both directions of Generate-editor transfer, tab switching, narrow/mobile layout, and unaffected current/batch/full-view actions.

### 2. Split `T2IAPI` into capability route groups

- Boundary: extract `ImageHistoryAPI`, `ClassicInpaintAPI`, and `KritaAPI`; leave generation request conversion and generation orchestration in `T2IAPI` initially.
- Evidence: 21 registered methods span generation, history/indexing/file mutation, process-backed inpaint, and Krita integration. Image-history code alone occupies most of the 2,694-line class.
- Payoff: High because routine history and integration changes stop sharing the critical generation class.
- Leverage: High because frontend history work, built-ins, permissions, and future application services consume these APIs.
- Feasibility: High because route names and compatibility forwarders can remain stable.
- Risk: Medium because reflection metadata and history synchronization are runtime contracts.
- Prerequisites: record method names, `APIDescription`/permission metadata, reflective parameter names, synchronization helpers, and non-HTTP callers. Grid Generator currently calls `T2IAPI.DeleteImage` directly and needs a forwarding method or coordinated migration.
- Migration: (1) extract private helpers with their sole capability; (2) delegate existing public methods where a direct C# consumer exists; (3) register the new owners with unchanged route names and order; (4) migrate direct consumers; (5) remove forwarding methods only when no maintained or extension consumer remains.
- Manual validation: generation over WebSocket, history list/filter/page/warmup, star/notes/delete/move, Grid Generator cleanup, classic-inpaint backend discovery and execution, Krita polling/send, permissions, and failure responses.

### 3. Decompose image-history browser behavior behind one controller

- Boundary: retain an `ImageHistoryController` as sole owner of `GenPageBrowserClass`, request tokens, selection, and refresh scheduling; extract comparison, filtering, and bulk-action collaborators.
- Evidence: `outputhistory.js` mixes request retry/watchdog behavior, filter parsing, comparison, sort/selection, bulk export/edit, mutations, list loading, and rendering across 2,827 lines and 136 top-level declarations.
- Payoff: High because several independently changing UI capabilities currently share request and selection state.
- Leverage: Medium because the improvement is concentrated in the history feature, though it also clarifies current-image integration.
- Feasibility: Medium because collaborators can delegate through existing globals but state ownership must be established first.
- Risk: Medium because optimistic updates and server/client filtering can drift.
- Prerequisites: document the `SwarmImageCard`, optimistic insertion, metadata, saved-path notification, `setCurrentImage`, and server-filter contracts.
- Migration: (1) name the controller around existing state; (2) extract comparison rendering; (3) extract pure filter parsing/evaluation; (4) extract bulk-action orchestration; (5) retain compatibility functions until Razor and cross-feature callers migrate.
- Manual validation: pagination and watchdog recovery, live generated-image insertion, all filters/sorts/views, multi-select, compare metadata/diff modes, bulk export/edit/delete, star/notes, folder changes, and refresh during active generation.

### 4. Catalog Swarm-maintained Comfy node contracts

- Boundary: introduce a C# contract catalog for Swarm-owned node class names, input keys, and related feature IDs; align capability interpretation in a focused `ComfyCapabilityCatalog` delegated to by `ComfyUIBackendExtension`.
- Evidence: more than fifty Python `NODE_CLASS_MAPPINGS` strings and schemas are repeated as C# literals across workflow generation, node-data helpers, backend transport, Web API, and feature detection.
- Payoff: High because duplicated node names and keys become reviewable contracts.
- Leverage: High because capability detection, workflow generation, transport, and managed Python all meet at this seam.
- Feasibility: Medium because literals can migrate incrementally while object-info stays authoritative.
- Risk: Low because the first stages centralize existing values without changing generated graphs.
- Prerequisites: explicitly limit the catalog to Swarm-maintained nodes; inventory optional nodes and aliases; preserve Python object-info as runtime authority.
- Migration: (1) catalog class names only; (2) migrate feature detection; (3) add input-key constants for stable Swarm nodes; (4) migrate generator call sites by feature; (5) compare catalog expectations with object-info during developer/manual verification without rejecting optional modules.
- Manual validation: startup with common-only and optional Extra nodes, Comfy feature flags, still image generation, masks/SAM, previews/final saves, video/animation paths, and useful failure behavior when an optional node is absent.

### 5. Establish frontend feature facades and document compatibility globals

- Boundary: give each large browser feature one singleton owner and initialization entry point while retaining narrowly documented global compatibility functions for Razor and extensions. Keep `SwarmUtil` limited to genuinely general primitives.
- Evidence: roughly 817 top-level declarations and ordered classic-script loading make dependencies implicit; the Image Editing and image-history projects provide concrete first adopters.
- Payoff: High because dependencies and initialization ownership become visible without breaking compatibility.
- Leverage: High because every large generation-page feature currently relies on the same global model.
- Feasibility: Medium because delegating globals provide a migration seam after physical extraction.
- Risk: Medium because inline Razor and external extensions may use globals not visible in core searches.
- Prerequisites: complete at least one physical feature extraction, inventory inline Razor and extension consumers, and define naming/initialization conventions in a repository skill or architecture note.
- Migration: (1) wrap feature-internal state; (2) export the existing public functions as delegating shims; (3) change core cross-feature callers to the facade; (4) document remaining extension globals; (5) consider native modules only after the compatibility boundary is stable.
- Manual validation: cold load, session reconnect, first-open lazy tabs, hash/deep-link navigation, inline handlers, extension tabs/scripts, and all compatibility entry points for each migrated feature.

### 6. Introduce a read-only application-services context behind `Program`

- Boundary: retain `Program` as composition/lifecycle root but expose settings, backends, sessions, models, extension state, and cancellation through explicit narrow interfaces or an immutable service context.
- Evidence: central `Program` contracts have hundreds of maintained references across layers; individual mutable services reach 40 files for settings and 23 for process cancellation.
- Payoff: High because service and lifecycle dependencies become explicit.
- Leverage: High because APIs, backends, models, accounts, utilities, and extensions consume `Program.*`.
- Feasibility: Medium because compatibility properties can delegate to a context one cluster at a time.
- Risk: High because startup, shutdown, and extensions depend on current static identities and timing.
- Prerequisites: stabilize nearer capability boundaries first and identify which `Program.*` members are extension contracts. Do not change construction and service access in one step.
- Migration: (1) create read-only service interfaces; (2) have `Program` construct and expose a compatibility context; (3) inject it into one leaf capability such as an extracted API group; (4) expand by dependency cluster; (5) deprecate, but do not abruptly remove, static access used by extensions.
- Manual validation: startup/shutdown, cancellation, settings reload, user/session lifecycle, model refresh, extension lifecycle ordering, backend initialization, and restart paths.

### 7. Separate backend scheduling policy from backend lifecycle

- Boundary: make scheduling consume a read-only backend availability/catalog interface while `BackendHandler` initially remains the facade and owner of threads, signals, persistence, and instances.
- Evidence: registration, configured-instance persistence, monitor/autoscale lifecycle, model pressure, claims, queueing, and selection all share one class and synchronization surface.
- Payoff: High because lifecycle changes and scheduling-policy changes gain independent ownership.
- Leverage: High because every generation request and backend type passes through this state.
- Feasibility: Low because concurrency behavior cannot be isolated mechanically.
- Risk: High because lock, signal, claim, and cancellation mistakes can stall or misroute work.
- Prerequisites: specify current lock order, signals, claim disposal, model-load preference, cancellation, and autoscaling behavior; establish an application-services boundary for consumers.
- Migration: (1) extract pure ranking/eligibility queries; (2) introduce a scheduler interface; (3) delegate the existing request loop through it; (4) separate availability snapshots only after parity; (5) consider policy changes as a distinct later project.
- Manual validation: multiple backends, disabled/errored backends, concurrent queues, model already loaded versus load required, cancellation, backend removal during work, autoscaling, shutdown, and `T2IBackendAccess.Dispose` behavior.

### 8. Decompose `WorkflowGenerator` around state-preserving collaborators

- Boundary: keep `WorkflowGenerator` as the stateful compatibility facade while moving model loading, media initialization, conditioning, sampling, and output assembly into collaborators that operate on its node-building context.
- Evidence: nearly 7,350 lines across the generator partials and step registry share mutable fields, ordered steps, reserved IDs, and extension-visible methods.
- Payoff: High because model and workflow features stop coupling through the generator's full mutable surface.
- Leverage: High because most Comfy generation capabilities and extension steps use it.
- Feasibility: Low because ordered steps, reserved IDs, and stateful graph construction constrain moves.
- Risk: High because subtle graph changes affect many model families and workflows.
- Prerequisites: complete the Swarm-node contract catalog; document step priority/reserved-ID behavior and external extension entry points; avoid simultaneous workflow behavior changes.
- Migration: (1) extract stateless node-contract helpers; (2) move one cohesive path such as output assembly; (3) delegate through existing methods; (4) migrate model/media/conditioning paths separately; (5) replace anonymous global steps with named step objects only after state ownership is narrower.
- Manual validation: representative model families, LoRAs and schedules, init/mask/regional inputs, ControlNet/adapters, sampler variants, previews, video, custom workflows, extension workflow steps, reproducible seeds, and graph comparison where available.

### 9. Separate parameter catalog registration from validation/application

- Boundary: divide default parameter registration into category registrars while preserving every `T2IParamTypes` public static field; later introduce explicit model/file resolvers for validation and application.
- Evidence: one static type owns the global registry, hundreds of parameter fields, a 600-plus-line default catalog, network serialization, option lookup, validation, output-file resolution, and input application.
- Payoff: Medium because catalog navigation improves before the later validation boundary yields its full benefit.
- Leverage: High because UI metadata, presets, extensions, request parsing, and generation share parameter identities.
- Feasibility: Medium because fields and registration order can remain stable while helpers move.
- Risk: Medium because serialization and extension-visible identity are compatibility contracts.
- Prerequisites: map field identity and extension registration dependencies; preserve registration order, names, remaps, groups, feature flags, and serialized metadata.
- Migration: (1) extract category registration methods with fields unchanged; (2) centralize catalog construction order; (3) isolate pure serialization; (4) introduce resolver-backed validation behind existing methods; (5) migrate callers only after parity.
- Manual validation: parameter metadata load, presets/metadata reuse, feature-gated options, model and wildcard selection, init images, output-file references and permissions, remaps, extension parameters, and generation request parsing.

## Recommended First Project

### Scope

Create `src/wwwroot/js/genpage/helpers/image_editor_ui.js` and move the contiguous Image Editing tab coordinator currently between `defaultButtonChoices` and `getImageFullSrc` in `currentimagehandler.js`. The extraction includes its state, DOM getters, control wiring, editor creation, tools/layers/selection/crop/effects/color/zoom/splitter actions, Generate-editor transfer helpers, and Bootstrap top-tab handlers. `currentimagehandler.js` continues to own current/batch image cards, full view, save/star/copy actions, comparison, and the call that opens the Generate-tab editor.

Add the coordinator after `image_editor.js` in the committed `imageediting` lazy script group. During implementation, reconcile rather than overwrite the user's current uncommitted eager-loading changes: if that experiment remains, the same coordinator must also appear after `image_editor.js` in its eager list without being evaluated twice.

### Non-Goals

- No UI or editor behavior changes.
- No conversion to native JavaScript modules and no removal or renaming of global functions.
- No refactor of `ImageEditor`, editor tool classes, current-image rendering, output history, or the server APIs.
- No lazy-tab architecture change; current working-tree eager-loading experimentation remains independently owned by the user.
- No CSS split in the first behavior-preserving move. Image Editing CSS can follow after computed-style and viewport validation is available.

### Known Consumers and Compatibility Requirements

- `_Generate/ImageEditingTab.cshtml` calls `imageEditingToggleInputSection`, `imageEditingZoomOut`, `imageEditingZoomReset`, and `imageEditingZoomIn` from inline handlers.
- `main.js` calls `imageEditingEnsureUiReady` after the Image Editing partial and helper scripts load.
- The coordinator consumes the global `ImageEditor`/tool classes and `window.imageEditor`, and exposes editor-transfer helpers used by later current-image actions.
- Bootstrap `shown.bs.tab` behavior must continue to activate/deactivate the correct editor exactly once and restore the Generate editor when returning to that tab.
- The script must tolerate partial markup not existing before the lazy tab loads and must not install duplicate handlers under repeat activation.
- Global declaration names are compatibility API for maintained Razor and potentially external extensions even when static search finds no other core caller.

### Migration Stages

1. Inventory every declaration in the contiguous block and every reference crossing either boundary; classify each as moved, imported global, or compatibility export.
2. Move the block verbatim into `image_editor_ui.js`, adding only the repository-required file/class/function documentation needed without changing control flow.
3. Add the file immediately after `image_editor.js` in the Image Editing load group and any active user-owned eager equivalent. Keep `currentimagehandler.js` loaded before `main.js` as today.
4. Remove the original block, then statically verify each `imageEditing*` definition is unique and every reference resolves through the same global surface.
5. Have the developer run the manual matrix below. Fix extraction regressions without combining them with facade, styling, or behavior improvements.
6. In a separately reviewed follow-up, wrap internal state in an `ImageEditingUI` singleton while leaving thin global delegates for Razor/extensions; only then consider moving uniquely owned CSS.

### Risks

- A missing or changed load-order edge can surface only on first lazy activation.
- Moving only part of the editor-transfer bridge can create a temporal dependency on `window.imageEditor` or leave a later current-image action unresolved.
- Re-registering Bootstrap or DOM handlers can cause duplicated actions after repeat tab opens.
- The current working tree changes lazy partials/scripts to eager loading, so an implementation based blindly on either tree state could omit or duplicate the new script.
- Editor state, sidebar persistence, and active/deactivated canvases can diverge while switching tabs even if individual controls appear functional.

### Manual Validation

The developer should validate this project in the live application because repository policy prohibits agent-run builds and tests:

1. Load Generate normally; open Image Editing for the first time, close/reopen it, and deep-link/hash-open it if supported.
2. Exercise paint and selection tools, layer add/delete/reorder/opacity, crop, effects/presets, color picker, undo/redo, zoom, and both sidebar splitters.
3. Send a current image and a history image to Image Editing; send the Image Editing layers back to the Generate editor; edit a current image in the Generate editor.
4. Switch repeatedly between Generate and Image Editing and confirm only the visible editor is active, resized correctly, and retains expected state.
5. Check desktop, narrow, and mobile layouts plus relevant light/dark themes.
6. Recheck unaffected current image, batch view, full view, save/star/copy, comparison, and Krita actions.

### Success Criteria

- `currentimagehandler.js` loses the roughly 2,440-line Image Editing coordinator and retains a coherent current/batch-image responsibility.
- Each moved global is defined exactly once, all existing names and call signatures remain available, and the new script loads after its editor dependencies in every active load path.
- No server route, API payload, Razor markup contract, persisted local-storage key, or user-visible behavior changes.
- The developer completes the manual validation matrix without regression.
- The new boundary is clear enough that a later singleton facade and adjacent CSS extraction do not require reopening current-image internals.

## Deferred and Rejected Refactors

- Wholesale frontend conversion to native modules is deferred until Razor and extension global compatibility has a designed bridge.
- A general extension API redesign is rejected as an early project; the existing seam is broad but functional and is a compatibility constraint for nearer refactors.
- A standalone `Utilities` breakup is rejected. Extract coherent helpers only when an approved capability project already owns their consumers.
- A mechanical split of `genpage.css` is rejected. Move uniquely owned rules alongside feature boundaries and preserve cascade order with manual visual checks.
- Refactoring upstream ComfyUI code, downloaded nodes, or external extensions is out of scope. Only SwarmUI-managed adapters and `ExtraNodes` contracts are candidates.
- File-size-only partial classes or file moves that leave shared state and ownership unchanged are not roadmap outcomes.
- Scheduler-policy changes and workflow output changes are explicitly separate from their structural extractions.
- Agent-run builds and automated tests are not proposed because repository policy forbids them. Static validation and a developer-run live matrix are required for every implementation project.
