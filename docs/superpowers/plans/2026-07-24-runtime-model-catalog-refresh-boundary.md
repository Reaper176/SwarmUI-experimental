# Runtime Model Catalog Refresh Boundary Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Publish runtime model-path changes under one write boundary and protect every maintained runtime model-catalog reader without changing public catalog identity or behavior.

**Architecture:** `Program` keeps ownership of the existing public `T2IModelSets` dictionary and separates lock-owning public operations from private core helpers. Runtime path changes construct a complete candidate, exclude readers with `RefreshLock`, retire and publish the replacement, refresh it, and notify path subscribers before releasing the write claim. Maintained consumers acquire short read claims around outer-catalog resolution and handler use, with explicit phase breaks before downloads, processes, WebSocket streaming, or write-side refreshes.

**Tech Stack:** C# 12, .NET 8, FreneticUtilities `ManyReadOneWriteLock`, Newtonsoft.Json, SwarmUI core/Text2Image/Web API/built-in backend code.

**Repository constraints:** Work directly on `master`; do not create a worktree. Do not build, launch, or run tests. Use source inspection, targeted `rg`, `git diff --check`, and maintainer-run live validation. Never stage the maintainer's existing changes in `src/Data/Settings.fds`, `src/Pages/Text2Image.cshtml`, `src/wwwroot/js/genpage/gentab/loras.js`, or `src/wwwroot/js/genpage/main.js`.

**Design:** `docs/superpowers/specs/2026-07-24-runtime-model-catalog-refresh-boundary-design.md`

---

## Planned File Responsibilities

- `src/Core/Program.cs`: own candidate creation, in-place publication, write-side refresh APIs, the runtime path-change transaction, and public coordination documentation.
- `src/WebAPI/AdminAPI.cs`: call the single path-change owner after committed settings publication.
- `src/WebAPI/ModelsAPI.cs`: protect model API lookups and split download/process/refresh phases.
- `src/WebAPI/T2IAPI.cs`: protect the complete parameter-list serialization boundary.
- `src/WebAPI/UtilAPI.cs`: protect conversion-path discovery and metadata-handler use without holding claims over child processes or backend-drain waits.
- `src/Core/WebServer.cs`: protect special-model preview lookup and copy result data before awaiting output.
- `src/Pages/_Generate/UtilitiesTab.cshtml`: snapshot model category names under a short read claim before rendering the utilities metadata-scanner options.
- `src/Core/Installation.cs`: use the write-owning Stable-Diffusion refresh operation.
- `src/Backends/BackendHandler.cs`: protect loaded-model annotation and pass a write-side category snapshot into refresh-event remote work.
- `src/Backends/SwarmSwarmBackend.cs`: accept an optional caller-owned category snapshot and otherwise acquire one before asynchronous remote requests.
- `src/BuiltinExtensions/AutoWebUIBackend/AutoWebUIAPIAbstractBackend.cs`: protect local model matching after remote discovery.
- `src/Text2Image/CommonModels.cs`: protect destination and category validation.
- `src/Text2Image/T2IModelHandler.cs`: retain report aggregation as write-side refresh code and document its ownership if needed.
- `src/Text2Image/T2IParamInput.cs`: protect metadata reconstruction lookups.
- `src/Text2Image/T2IParamSet.cs`: protect model parameter conversion.
- `src/Text2Image/T2IParamTypes.cs`: protect model cleaning and model-backed value providers.
- `src/Text2Image/T2IPromptHandling.cs`: protect embedding and LoRA lookup phases.
- `src/BuiltinExtensions/ComfyUIBackend/ComfyUIAPIAbstractBackend.cs`: protect custom-workflow LoRA expansion.
- `src/BuiltinExtensions/ComfyUIBackend/ComfyUIBackendExtension.cs`: protect PiD model providers and resolvers.
- `src/BuiltinExtensions/ComfyUIBackend/ComfyUIWebAPI.cs`: split model selection, long-running work, refresh, and verification into non-upgrading phases.
- `src/BuiltinExtensions/ComfyUIBackend/WorkflowGenerator.cs`: protect synchronous LoRA, cascade, and VAE catalog reads.
- `src/BuiltinExtensions/ComfyUIBackend/WorkflowGeneratorModelSupport.cs`: protect model-helper reads and separate known-model downloads from write-side refresh.
- `src/BuiltinExtensions/ComfyUIBackend/WorkflowGeneratorSteps.cs`: protect VAE step lookup.
- `docs/superpowers/specs/2026-07-24-runtime-model-catalog-refresh-boundary-design.md`: record implementation and later maintainer-validation status.
- `docs/superpowers/audits/2026-07-21-maintainability-architecture-refresh.md`: update Core F7 disposition and roadmap only after implementation evidence exists.

---

### Task 1: Freeze the Implementation Inventory and Coordination Contract

**Files:**
- Inspect: `src/Core/Program.cs`
- Inspect: all maintained `*.cs` and `*.cshtml` files returned by the commands below
- Modify later: `docs/superpowers/specs/2026-07-24-runtime-model-catalog-refresh-boundary-design.md`

- [ ] **Step 1: Capture the direct and derived consumer inventory**

Run:

```bash
rg -n 'Program\.(T2IModelSets|MainSDModels)' src \
  --glob '*.cs' \
  --glob '*.cshtml' \
  --glob '!src/bin/**' \
  --glob '!src/obj/**' \
  --glob '!src/Extensions/**'

rg -l 'Program\.(T2IModelSets|MainSDModels)' src \
  --glob '*.cs' \
  --glob '*.cshtml' \
  --glob '!src/bin/**' \
  --glob '!src/obj/**' \
  --glob '!src/Extensions/**' | sort
```

Expected baseline: 21 maintained consumer files and 71 matching source lines beyond the declaration in `Program.cs`. `ModelsAPI.cs:375` has two literal references on one matching line, for 72 literal identifier references. Classify the 71 matching source lines as startup-only 2, write-side 3, protected 5, and uncovered 61; the new uncovered matching source line is `src/Pages/_Generate/UtilitiesTab.cshtml:50`. Record every matching source line in the task notes as startup-only, existing write-side, protected already, or uncovered.

- [ ] **Step 2: Capture every writer and event publisher**

Run:

```bash
rg -n 'BuildModelLists\(|RefreshAllModelSets\(|ModelRefreshEvent\?\.Invoke|ModelPathsChangedEvent\?\.Invoke|\.Refresh\(\)' \
  src/Core src/WebAPI src/Text2Image src/Backends src/BuiltinExtensions \
  --glob '*.cs' \
  --glob '!src/bin/**' \
  --glob '!src/obj/**'
```

Expected baseline:

- startup calls `BuildModelLists` and `RefreshAllModelSets`;
- `AdminAPI.ChangeServerSettings` calls build, refresh, and path notification separately;
- `T2IAPI.TriggerRefresh` owns a write claim around `ModelRefreshEvent`;
- Comfy TensorRT and workflow helpers call `RefreshAllModelSets`;
- Installation and LoRA extraction directly refresh resolved handlers; and
- Models API refreshes a handler after download while holding its own write claim.

- [ ] **Step 3: Confirm the public identity and key contract**

Run:

```bash
rg -n 'public static Dictionary<string, T2IModelHandler> T2IModelSets|T2IModelSets\s*=' src/Core/Program.cs
rg -n 'T2IModelSets\["(Stable-Diffusion|VAE|LoRA|Embedding|ControlNet|Clip|ClipVision)"\]' src/Core/Program.cs
```

Expected: one field initialization, no later reassignment, and exactly the seven established category assignments.

- [ ] **Step 4: Confirm the protected maintainer changes are unstaged**

Run:

```bash
git status --short --untracked-files=no
git diff --cached --name-only
```

Expected: the four maintainer files may remain modified, and none is staged.

No commit is needed for this inspection-only task.

---

### Task 2: Centralize Catalog Construction, Publication, and Write Ownership

**Files:**
- Modify: `src/Core/Program.cs`
- Modify: `src/Text2Image/T2IModelHandler.cs`

- [ ] **Step 1: Separate handler detachment from shared metadata-cache retirement**

In `T2IModelHandler`, preserve the public parameterless `Shutdown()` signature while separating event detachment from shared cache cleanup:

```csharp
/// <summary>Marks this handler shut down and removes its model-refresh subscription without disposing the shared metadata cache.</summary>
internal bool DetachFromModelRefresh()
{
    if (IsShutdown)
    {
        return false;
    }
    IsShutdown = true;
    Program.ModelRefreshEvent -= Refresh;
    return true;
}

/// <summary>Removes and disposes every shared model metadata cache entry.</summary>
internal static void DisposeSharedMetadataCache()
{
    foreach (string folder in ModelMetadataCachePerFolder.Keys)
    {
        if (ModelMetadataCachePerFolder.TryRemove(folder, out ModelDatabase database))
        {
            database.Dispose();
        }
    }
}

public void Shutdown()
{
    if (!DetachFromModelRefresh())
    {
        return;
    }
    lock (MetadataLock)
    {
        DisposeSharedMetadataCache();
    }
}
```

The static cleanup removes each entry before calling the existing exception-isolating `ModelDatabase.Dispose()`, so one database disposal failure cannot retain that entry or skip later entries. Candidate cleanup must call only `DetachFromModelRefresh`; publication performs shared cleanup once after all displaced handlers detach.

- [ ] **Step 2: Split candidate creation from public publication**

Replace the mutating body of `BuildModelLists` with private candidate and publication helpers. Keep the existing directory and path rules byte-for-byte:

```csharp
/// <summary>Constructs a complete replacement model-handler catalog from current settings.</summary>
private static Dictionary<string, T2IModelHandler> CreateModelLists()
{
    Dictionary<string, T2IModelHandler> result = [];
    try
    {
        void EnsureModelDirectory(string path)
        {
            try
            {
                if (!Utilities.EnsureDirectory(path))
                {
                    Logs.Warning($"Model directory path '{path}' already exists as a non-directory file or broken symlink.");
                }
            }
            catch (IOException ex)
            {
                Logs.Error($"Failed to create directories for models. You may need to check your ModelRoot or SDModelFolder settings. {ex.Message}");
            }
        }
        string actualModelRoot = ServerSettings.Paths.ActualModelRoot;
        foreach (string path in ServerSettings.Paths.SDModelFolder.Split(';'))
        {
            EnsureModelDirectory(Utilities.CombinePathWithAbsolute(actualModelRoot, path));
        }
        EnsureModelDirectory($"{actualModelRoot}/upscale_models");
        EnsureModelDirectory($"{actualModelRoot}/clip");
        string[] roots = [.. ServerSettings.Paths.ModelRoot.Split(';').Where(p => !string.IsNullOrWhiteSpace(p))];
        if (roots.Length == 0)
        {
            Logs.Error("No ModelRoot paths defined! You must set at least one model root path. Presuming default value. Please correct your settings.");
            roots = ["Models"];
        }
        int downloadRootId = Math.Abs(ServerSettings.Paths.DownloadToRootID) % roots.Length;
        void BuildPathList(string folder, T2IModelHandler handler)
        {
            Dictionary<string, string> paths = [];
            int rootCount = 0;
            foreach (string modelRoot in roots)
            {
                int sfCount = 0;
                string[] subfolders = [.. folder.Split(';').Where(p => !string.IsNullOrWhiteSpace(p))];
                if (subfolders.Length == 0)
                {
                    Logs.Error($"Model set {handler.ModelType} has no subfolders defined! You cannot set a path to empty.");
                    return;
                }
                if (rootCount == downloadRootId)
                {
                    handler.DownloadFolderPath = Utilities.CombinePathWithAbsolute(Environment.CurrentDirectory, modelRoot.Trim(), subfolders[0].Trim());
                }
                foreach (string subfolder in subfolders)
                {
                    string patched = Utilities.CombinePathWithAbsolute(Environment.CurrentDirectory, modelRoot.Trim(), subfolder.Trim());
                    if ((sfCount > 0 || rootCount > 0) && rootCount != downloadRootId && !Directory.Exists(patched))
                    {
                        continue;
                    }
                    paths[patched] = patched;
                    sfCount++;
                }
                rootCount++;
            }
            handler.FolderPaths = [.. paths.Keys];
        }
        void AddModelSet(string type, string folders)
        {
            T2IModelHandler handler = new() { ModelType = type };
            result[type] = handler;
            BuildPathList(folders, handler);
        }
        EnsureModelDirectory(actualModelRoot + "/tensorrt");
        EnsureModelDirectory(actualModelRoot + "/diffusion_models");
        AddModelSet("Stable-Diffusion", ServerSettings.Paths.SDModelFolder + ";tensorrt;diffusion_models;unet");
        AddModelSet("VAE", ServerSettings.Paths.SDVAEFolder);
        AddModelSet("LoRA", ServerSettings.Paths.SDLoraFolder);
        AddModelSet("Embedding", ServerSettings.Paths.SDEmbeddingFolder);
        AddModelSet("ControlNet", ServerSettings.Paths.SDControlNetsFolder);
        AddModelSet("Clip", ServerSettings.Paths.SDClipFolder);
        AddModelSet("ClipVision", ServerSettings.Paths.SDClipVisionFolder);
        return result;
    }
    catch
    {
        DetachUnpublishedModelLists(result);
        throw;
    }
}

/// <summary>Best-effort detaches replacement handlers that are not currently published.</summary>
private static void DetachUnpublishedModelLists(Dictionary<string, T2IModelHandler> replacement)
{
    foreach ((string type, T2IModelHandler handler) in replacement)
    {
        if (T2IModelSets.TryGetValue(type, out T2IModelHandler published) && ReferenceEquals(published, handler))
        {
            continue;
        }
        try
        {
            handler.DetachFromModelRefresh();
        }
        catch (Exception ex)
        {
            Logs.Error($"Failed to detach an unpublished {handler.ModelType} model handler: {ex.ReadableString()}");
        }
    }
}

/// <summary>Publishes a complete replacement catalog while the caller owns <see cref="RefreshLock"/> for writing.</summary>
private static void PublishModelLists(Dictionary<string, T2IModelHandler> replacement)
{
    try
    {
        T2IModelSets.EnsureCapacity(replacement.Count);
        foreach (T2IModelHandler handler in T2IModelSets.Values)
        {
            try
            {
                handler.DetachFromModelRefresh();
            }
            catch (Exception ex)
            {
                Logs.Error($"Failed to detach a displaced {handler.ModelType} model handler: {ex.ReadableString()}");
            }
        }
        T2IModelHandler.DisposeSharedMetadataCache();
        T2IModelSets.Clear();
        foreach ((string type, T2IModelHandler handler) in replacement)
        {
            T2IModelSets[type] = handler;
        }
    }
    catch
    {
        DetachUnpublishedModelLists(replacement);
        throw;
    }
}

/// <summary>Builds and publishes model handlers while the caller owns <see cref="RefreshLock"/> for writing.</summary>
private static void BuildModelListsCore()
{
    Dictionary<string, T2IModelHandler> replacement = CreateModelLists();
    PublishModelLists(replacement);
}

/// <summary>Builds the main model list from settings under a newly acquired <see cref="RefreshLock"/> write claim. Callers must not already hold a read or write claim.</summary>
public static void BuildModelLists()
{
    using ManyReadOneWriteLock.WriteClaim claim = RefreshLock.LockWrite();
    BuildModelListsCore();
}
```

Keep C# style compliant: explicit types, braced blocks, and XML docs on every new field if any are introduced.

- [ ] **Step 3: Split full refresh into public and core operations**

Replace the current `RefreshAllModelSets` body with:

```csharp
/// <summary>Refreshes all model sets while the caller owns <see cref="RefreshLock"/> for writing.</summary>
private static void RefreshAllModelSetsCore()
{
    RebuildDataDir();
    foreach (T2IModelHandler handler in T2IModelSets.Values)
    {
        try
        {
            handler.Refresh();
        }
        catch (Exception ex)
        {
            Logs.Error($"Failed to load models for {handler.ModelType}: {ex.Message}");
        }
    }
}

/// <summary>Refreshes all model sets from file source under a newly acquired <see cref="RefreshLock"/> write claim. Callers must not already hold a read or write claim.</summary>
public static void RefreshAllModelSets()
{
    using ManyReadOneWriteLock.WriteClaim claim = RefreshLock.LockWrite();
    RefreshAllModelSetsCore();
}

/// <summary>Refreshes one published model set under a newly acquired <see cref="RefreshLock"/> write claim. Callers must not already hold a read or write claim.</summary>
public static bool RefreshModelSet(string modelType)
{
    using ManyReadOneWriteLock.WriteClaim claim = RefreshLock.LockWrite();
    if (!T2IModelSets.TryGetValue(modelType, out T2IModelHandler handler))
    {
        return false;
    }
    handler.Refresh();
    return true;
}
```

- [ ] **Step 4: Add the single runtime path-change operation**

Add beside the refresh methods:

```csharp
/// <summary>Rebuilds, refreshes, and publishes path-change notification under a newly acquired <see cref="RefreshLock"/> write claim. Callers and event callbacks must not already hold or reacquire a read or write claim.</summary>
public static void RebuildModelListsForPathChange()
{
    using ManyReadOneWriteLock.WriteClaim claim = RefreshLock.LockWrite();
    BuildModelListsCore();
    RefreshAllModelSetsCore();
    ModelPathsChangedEvent?.Invoke();
}
```

Update the XML docs on `T2IModelSets`, `MainSDModels`, and `RefreshLock` to say that external and maintained runtime lookup/enumeration participates in `RefreshLock`, and that event callbacks already executing under the write boundary must not reacquire it. The public `BuildModelLists`, `RefreshAllModelSets`, `RefreshModelSet`, and `RebuildModelListsForPathChange` docs must state that each acquires the write claim and callers must not already hold a read or write claim.

- [ ] **Step 5: Statically verify owner shape**

Run:

```bash
rg -n 'CreateModelLists|DetachUnpublishedModelLists|PublishModelLists|BuildModelListsCore|RefreshAllModelSetsCore|RefreshModelSet|RebuildModelListsForPathChange' src/Core/Program.cs
rg -n 'DetachFromModelRefresh|DisposeSharedMetadataCache|public void Shutdown\\(\\)' src/Text2Image/T2IModelHandler.cs
rg -n 'T2IModelSets\s*=' src/Core/Program.cs
git diff --check -- src/Core/Program.cs src/Text2Image/T2IModelHandler.cs
```

Expected:

- one public dictionary initialization and no reassignment;
- private core helpers plus four public write-owning operations;
- candidate failure detaches subscriptions without disposing the prior shared cache;
- publication reserves capacity, detaches displaced handlers, cleans the shared cache once, and detaches any unpublished replacement after an unexpected transfer failure;
- the public parameterless `T2IModelHandler.Shutdown()` remains present and idempotent;
- no whitespace errors.

- [ ] **Step 6: Commit the catalog owner**

```bash
git add -- src/Core/Program.cs src/Text2Image/T2IModelHandler.cs
git diff --cached --check
git commit -m "refactor: own runtime model catalog publication"
```

---

### Task 3: Route Maintained Writers Through the Catalog Owner

**Files:**
- Modify: `src/WebAPI/AdminAPI.cs`
- Modify: `src/Core/Installation.cs`
- Modify: `src/WebAPI/ModelsAPI.cs`
- Modify: `src/BuiltinExtensions/ComfyUIBackend/ComfyUIWebAPI.cs`

- [ ] **Step 1: Replace the settings-path call sequence**

In `AdminAPI.ChangeServerSettings`, replace:

```csharp
Program.BuildModelLists();
Program.RefreshAllModelSets();
Program.ModelPathsChangedEvent?.Invoke();
```

with:

```csharp
Program.RebuildModelListsForPathChange();
```

Keep the existing surrounding `try`, warning log, `runtimeWarning`, and success response unchanged.

- [ ] **Step 2: Replace direct handler refreshes**

In `Installation`, replace:

```csharp
Program.MainSDModels.Refresh();
```

with:

```csharp
Program.RefreshModelSet("Stable-Diffusion");
```

In the Models API post-download refresh branch, replace manual `RefreshLock.LockWrite()` plus `handler.Refresh()` with:

```csharp
Program.RefreshModelSet(type);
```

The handler used to choose the destination must not escape from the earlier read phase into this refresh phase.

In Comfy LoRA extraction, replace direct `loras.Refresh()` with:

```csharp
Program.RefreshModelSet("LoRA");
```

Move presence verification into a later read claim as specified in Task 7.

- [ ] **Step 3: Confirm all maintained direct refresh writers**

Run:

```bash
rg -n 'Program\.(T2IModelSets|MainSDModels).*\.Refresh\(|\bhandler\.Refresh\(|\bloras\.Refresh\(' \
  src/Core src/WebAPI src/Text2Image src/Backends src/BuiltinExtensions \
  --glob '*.cs' \
  --glob '!src/Extensions/**'
```

Expected: direct handler refresh remains only inside Program's write-side core, `T2IModelHandler` event subscription, and any reviewed handler-local context already under a write claim. No maintained runtime caller may resolve a handler before taking the catalog write claim and then refresh that stale reference.

- [ ] **Step 4: Verify no recursive write acquisition**

Run:

```bash
rg -n -C 6 'LockWrite\(\)|BuildModelLists\(|RefreshAllModelSets\(|RefreshModelSet\(|RebuildModelListsForPathChange\(' \
  src/Core/Program.cs src/WebAPI/AdminAPI.cs src/Core/Installation.cs src/WebAPI/ModelsAPI.cs \
  src/BuiltinExtensions/ComfyUIBackend/ComfyUIWebAPI.cs
```

Expected: public write-owning operations are never called while the same flow holds `RefreshLock` for reading or writing.

- [ ] **Step 5: Commit writer migration**

```bash
git add -- \
  src/WebAPI/AdminAPI.cs \
  src/Core/Installation.cs \
  src/WebAPI/ModelsAPI.cs \
  src/BuiltinExtensions/ComfyUIBackend/ComfyUIWebAPI.cs
git diff --cached --check
git commit -m "refactor: route model catalog writes through owner"
```

---

### Task 4: Protect Core Model API and Utility Readers

**Files:**
- Modify: `src/WebAPI/ModelsAPI.cs`
- Modify: `src/WebAPI/T2IAPI.cs`
- Modify: `src/WebAPI/UtilAPI.cs`
- Modify: `src/Core/WebServer.cs`
- Modify: `src/Pages/_Generate/UtilitiesTab.cshtml`

- [ ] **Step 1: Move Models API claims before handler resolution**

For `DescribeModel`, `ListModels`, `BulkEditModelMetadata`, `EditModelMetadata`, `GetModelHeaders`, `OpenModelFolder`, `DeleteModel`, and `RenameModel`, ensure the method acquires:

```csharp
using ManyReadOneWriteLock.ReadClaim claim = Program.RefreshLock.LockRead();
```

before the first `Program.T2IModelSets.TryGetValue(...)`. Remove later duplicate claims in the same synchronous handler-use scope.

For `OpenModelFolder`, copy the selected path under the claim and release it before `Process.Start`:

```csharp
string modelPath;
using (ManyReadOneWriteLock.ReadClaim claim = Program.RefreshLock.LockRead())
{
    if (!Program.T2IModelSets.TryGetValue(subtype, out T2IModelHandler handler))
    {
        return new JObject() { ["error"] = "Invalid sub-type." };
    }
    if (TryGetRefusalForModel(session, modelName, out JObject refusal))
    {
        return refusal;
    }
    T2IModel match = null;
    if (handler.Models.TryGetValue(modelName, out T2IModel model)
        || handler.Models.TryGetValue(modelName + ".safetensors", out model))
    {
        match = model;
    }
    modelPath = match?.RawFilePath;
}
```

Use `modelPath` for the existing existence and OS process-launch behavior.

- [ ] **Step 2: Split download lookup from long-running I/O**

In `DoModelDownloadWS`, resolve and copy only the destination folder under a read claim:

```csharp
string folder = null;
using (ManyReadOneWriteLock.ReadClaim claim = Program.RefreshLock.LockRead())
{
    if (Program.T2IModelSets.TryGetValue(type, out T2IModelHandler handler))
    {
        folder = handler.DownloadFolderPath;
    }
}
if (folder is null)
{
    await ws.SendJson(new JObject() { ["error"] = "Invalid type." }, API.WebsocketTimeout);
    return null;
}
```

Do not retain `handler`. Keep URL processing, metadata writing, download progress, and WebSocket sends outside the claim. Refresh later through `Program.RefreshModelSet(type)`.

- [ ] **Step 3: Split hash lookup from file hashing**

In `GetModelHash`, select the `T2IModel` under a read claim, release it, then run the existing hash calculation:

```csharp
T2IModel match = null;
using (ManyReadOneWriteLock.ReadClaim claim = Program.RefreshLock.LockRead())
{
    if (!Program.T2IModelSets.TryGetValue(subtype, out T2IModelHandler handler))
    {
        return new JObject() { ["error"] = "Invalid sub-type." };
    }
    if (session.User.IsAllowedModel(modelName))
    {
        if (handler.Models.TryGetValue(modelName + ".safetensors", out T2IModel model)
            || handler.Models.TryGetValue(modelName, out model))
        {
            match = model;
        }
    }
}
```

Keep the not-found response and `match.GetOrGenerateTensorHashSha256()` result unchanged.

- [ ] **Step 4: Protect complete parameter-list serialization**

At the start of `T2IAPI.ListT2IParams`, add:

```csharp
using ManyReadOneWriteLock.ReadClaim claim = Program.RefreshLock.LockRead();
```

Retain it through `T2IParamType.ToNet(session)` and group serialization because model-backed value providers run during this method. The method performs no awaited I/O.

- [ ] **Step 5: Protect utility operations without spanning long waits**

In `UtilAPI.Pickle2SafeTensor`, copy the folder array before starting processes:

```csharp
string[] folderPaths;
using (ManyReadOneWriteLock.ReadClaim claim = Program.RefreshLock.LockRead())
{
    if (!Program.T2IModelSets.TryGetValue(type, out T2IModelHandler models))
    {
        return new JObject() { ["error"] = $"Invalid type '{type}'." };
    }
    folderPaths = [.. models.FolderPaths];
}
```

Iterate `folderPaths` outside the claim.

In `WipeMetadata`, retain the existing backend-drain wait without a catalog claim. Immediately before the handler loop, acquire a read claim around only:

```csharp
using (ManyReadOneWriteLock.ReadClaim claim = Program.RefreshLock.LockRead())
{
    foreach (T2IModelHandler handler in Program.T2IModelSets.Values)
    {
        handler.MassRemoveMetadata();
    }
}
```

- [ ] **Step 6: Protect special-model preview lookup**

In `WebServer`, resolve the preview data under a read claim, copy the string, release the claim, and only then await `yieldResult`:

```csharp
string previewImage = null;
using (ManyReadOneWriteLock.ReadClaim claim = Program.RefreshLock.LockRead())
{
    if (Program.T2IModelSets.TryGetValue(subtype, out T2IModelHandler handler)
        && (handler.Models.TryGetValue(name + ".safetensors", out T2IModel model)
            || handler.Models.TryGetValue(name, out model))
        && (model.Metadata?.PreviewImage?.StartsWithFast("data:") ?? false))
    {
        previewImage = model.Metadata.PreviewImage;
    }
}
if (previewImage is not null)
{
    await yieldResult(previewImage);
    return;
}
```

- [ ] **Step 7: Snapshot Utilities tab category names before rendering**

Before the existing metadata-scanner `<option>` loop in `UtilitiesTab.cshtml`, snapshot the category names while a short claim is active, then render from the snapshot after the claim is disposed:

```cshtml
@{
    string[] modelTypes;
    using (FreneticUtilities.FreneticToolkit.ManyReadOneWriteLock.ReadClaim claim = Program.RefreshLock.LockRead())
    {
        modelTypes = [.. Program.T2IModelSets.Keys];
    }
}
```

Replace the direct enumeration with:

```cshtml
@foreach (string key in modelTypes)
{
    <option value="@key" class="translate">@key</option>
}
```

The claim must be released before markup rendering begins.

- [ ] **Step 8: Verify API reader coverage and claim duration**

Run:

```bash
rg -n -C 8 'Program\.(T2IModelSets|MainSDModels)' \
  src/WebAPI/ModelsAPI.cs src/WebAPI/T2IAPI.cs src/WebAPI/UtilAPI.cs src/Core/WebServer.cs \
  src/Pages/_Generate/UtilitiesTab.cshtml

rg -n -C 5 'LockRead\(\)|LockWrite\(\)|RefreshModelSet\(' \
  src/WebAPI/ModelsAPI.cs src/WebAPI/T2IAPI.cs src/WebAPI/UtilAPI.cs src/Core/WebServer.cs \
  src/Pages/_Generate/UtilitiesTab.cshtml

git diff --check -- \
  src/WebAPI/ModelsAPI.cs src/WebAPI/T2IAPI.cs src/WebAPI/UtilAPI.cs src/Core/WebServer.cs \
  src/Pages/_Generate/UtilitiesTab.cshtml
```

Expected: every lookup precedes handler use under a read claim; the Utilities tab copies category names before rendering markup; no read claim spans child-process waits, model-download HTTP work, or preview output awaits; no read claim encloses `RefreshModelSet`.

- [ ] **Step 9: Commit core API coverage**

```bash
git add -- \
  src/WebAPI/ModelsAPI.cs \
  src/WebAPI/T2IAPI.cs \
  src/WebAPI/UtilAPI.cs \
  src/Core/WebServer.cs \
  src/Pages/_Generate/UtilitiesTab.cshtml
git diff --cached --check
git commit -m "fix: protect core model catalog readers"
```

---

### Task 5: Protect Core, Backend, and Parameter Conversion Readers

**Files:**
- Modify: `src/Backends/BackendHandler.cs`
- Modify: `src/Backends/SwarmSwarmBackend.cs`
- Modify: `src/BuiltinExtensions/AutoWebUIBackend/AutoWebUIAPIAbstractBackend.cs`
- Modify: `src/Text2Image/CommonModels.cs`
- Modify: `src/Text2Image/T2IParamInput.cs`
- Modify: `src/Text2Image/T2IParamSet.cs`
- Modify: `src/Text2Image/T2IParamTypes.cs`
- Modify: `src/Text2Image/T2IPromptHandling.cs`

- [ ] **Step 1: Protect backend annotations and remote category snapshots**

Wrap the complete model-map use in `BackendHandler.ReassignLoadedModelsList`:

```csharp
using ManyReadOneWriteLock.ReadClaim claim = Program.RefreshLock.LockRead();
foreach (T2IModel model in Program.MainSDModels.Models.Values)
{
    model.AnyBackendsHaveLoaded = false;
}
foreach (T2IBackendData backend in EnumerateT2IBackends)
{
    if (backend.Backend is not null
        && backend.Backend.CurrentModelName is not null
        && Program.MainSDModels.Models.TryGetValue(backend.Backend.CurrentModelName, out T2IModel model))
    {
        model.AnyBackendsHaveLoaded = true;
    }
}
```

`ModelRefreshEvent` already runs under `RefreshLock` for writing, so its asynchronous remote tasks must not try to acquire a read claim. In the `BackendHandler` event callback, capture the keys directly as write-side code and pass them to each backend:

```csharp
Program.ModelRefreshEvent += () =>
{
    string[] modelTypes = [.. Program.T2IModelSets.Keys];
    List<Task> waitFor = [];
    foreach (SwarmSwarmBackend backend in RunningBackendsOfType<SwarmSwarmBackend>())
    {
        waitFor.Add(backend.TriggerRefresh(modelTypes));
    }
    Task.WaitAll([.. waitFor]);
};
```

Change the remote methods to accept the optional snapshot:

```csharp
public Task TriggerRefresh(string[] modelTypes = null)
```

and:

```csharp
public async Task ReviseRemoteDataList(bool fullLoad, string[] modelTypes = null)
```

Pass `modelTypes` from `TriggerRefresh` to the full-load `ReviseRemoteDataList` call. At the start of `ReviseRemoteDataList`, before its first `await`, acquire a snapshot only when the caller did not supply one and this invocation will enumerate local categories:

```csharp
string[] effectiveModelTypes = modelTypes;
if (IsAControlInstance && fullLoad && effectiveModelTypes is null)
{
    using ManyReadOneWriteLock.ReadClaim claim = Program.RefreshLock.LockRead();
    effectiveModelTypes = [.. Program.T2IModelSets.Keys];
}
```

Use `effectiveModelTypes` inside the existing `IsAControlInstance && fullLoad` branch:

```csharp
foreach (string type in effectiveModelTypes)
{
    string runType = type;
    tasks.Add(Task.Run(async () =>
    {
        try
        {
            JObject modelsData = await HttpClient.PostJson(
                $"{Address}/API/ListModels",
                new()
                {
                    ["session_id"] = Session,
                    ["path"] = "",
                    ["depth"] = 999,
                    ["subtype"] = runType,
                    ["allowRemote"] = Settings.AllowForwarding,
                    ["dataImages"] = true
                },
                RequestAdapter());
            JToken[] remoteModels = [.. modelsData["files"]];
            if (fullLoad)
            {
                Logs.Verbose($"{HandlerTypeData.Name} {BackendData.ID} Got {runType} model list, {remoteModels.Length} models");
            }
            Dictionary<string, JObject> remoteModelsParsed = [];
            foreach (JToken token in remoteModels)
            {
                JObject data = token.DeepClone() as JObject;
                data["local"] = false;
                remoteModelsParsed[data["name"].ToString()] = data;
            }
            RemoteModels[runType] = remoteModelsParsed;
            Models[runType] = [.. remoteModelsParsed.Keys];
        }
        catch (Exception ex)
        {
            Logs.Error($"Failed to get {runType} models from remote Swarm at {Address}: {ex.ReadableString()}");
        }
    }));
}
```

Initial backend loading, idle validation, and other non-event callers omit the argument and acquire a short snapshot before network work. The write-side event callback supplies the snapshot and never reacquires the lock from its worker tasks.

In `AutoWebUIAPIAbstractBackend.InitInternal`, keep the remote query outside the claim and acquire a read claim only around matching `targetClean`/`targetBackup` against `Program.MainSDModels.Models.Values`.

- [ ] **Step 2: Protect known-model destination and registration validation**

In `CommonModels.ModelInfo.DownloadNow`, copy the folder under a read claim:

```csharp
string folder;
using (ManyReadOneWriteLock.ReadClaim claim = Program.RefreshLock.LockRead())
{
    folder = Program.T2IModelSets[FolderType].DownloadFolderPath;
}
```

Release before file checks and `Utilities.DownloadFile`.

In `CommonModels.Register`, acquire a read claim around the category existence check and error-key formatting. Release before mutating `Known`.

- [ ] **Step 3: Protect parameter conversion and metadata reconstruction**

In `T2IParamSet.Set`, make the local `getModel` function acquire a read claim before resolving the handler and keep it through best-name matching, model resolution, `AutoWarn`, and optional eager hash loading. Hash generation uses the model's handler and metadata cache, so it remains protected:

```csharp
using ManyReadOneWriteLock.ReadClaim claim = Program.RefreshLock.LockRead();
T2IModelHandler handler = Program.T2IModelSets[param.Subtype ?? "Stable-Diffusion"];
```

In the model branch of `T2IParamTypes.Clean`, acquire a read claim before `TryGetValue` and retain it through `ListModelNamesFor`.

In `T2IParamInput` metadata generation, acquire a read claim around the `ModelListExtraKeys` loop that resolves string names back to models and calls `addModel` for those resolved objects. Hash generation can re-enter the model's handler and must finish before that handler can be displaced. Keep unrelated processing outside the claim.

- [ ] **Step 4: Protect model-backed parameter providers**

`T2IAPI.ListT2IParams` now provides an outer read boundary for `T2IParamType.ToNet` value-provider execution. For public provider helpers that are also callable elsewhere, add focused read claims inside named helper functions:

```csharp
static List<string> listRefinerModels(Session session)
{
    using ManyReadOneWriteLock.ReadClaim claim = Program.RefreshLock.LockRead();
    List<T2IModel> baseList = [.. Program.MainSDModels.ListModelsFor(session).OrderBy(m => m.Name)];
    List<T2IModel> refinerList = [.. baseList.Where(m => m.ModelClass is not null
        && (m.ModelClass.Name.Contains("Refiner") || m.ModelClass.ID.Contains("wan-2_1-text2video-14b")))];
    List<string> bases = CleanModelList(baseList.Select(m => m.Name));
    return ["(Use Base)", .. CleanModelList(refinerList.Select(m => m.Name)), "-----", .. bases];
}
```

Convert expression lambdas that directly access `MainSDModels` into named local functions with the same return values and a read claim. Apply the same pattern to VAE and LoRA providers that directly access `T2IModelSets`.

Nested read claims during `ListT2IParams` are acceptable; no provider may call a write-owning refresh.

- [ ] **Step 5: Protect prompt embedding and LoRA phases**

In `T2IPromptHandling`, add focused read claims around each block that:

- initializes `context.Embeds` from the Embedding handler;
- resolves a matched embedding;
- initializes `context.Loras` from the LoRA handler;
- resolves a matched LoRA; or
- resolves LoRA metadata trigger phrases.

Keep prompt parsing and unrelated processing outside the claims. A typical block is:

```csharp
using (ManyReadOneWriteLock.ReadClaim claim = Program.RefreshLock.LockRead())
{
    context.Loras ??= [.. Program.T2IModelSets["LoRA"].ListModelNamesFor(context.Input.SourceSession)];
}
```

When matching and resolving must use one generation, keep both operations in the same claim.

- [ ] **Step 6: Verify conversion coverage**

Run:

```bash
rg -n -C 7 'Program\.(T2IModelSets|MainSDModels)' \
  src/Backends/BackendHandler.cs \
  src/Backends/SwarmSwarmBackend.cs \
  src/BuiltinExtensions/AutoWebUIBackend/AutoWebUIAPIAbstractBackend.cs \
  src/Text2Image/CommonModels.cs \
  src/Text2Image/T2IParamInput.cs \
  src/Text2Image/T2IParamSet.cs \
  src/Text2Image/T2IParamTypes.cs \
  src/Text2Image/T2IPromptHandling.cs

git diff --check -- \
  src/Backends/BackendHandler.cs \
  src/Backends/SwarmSwarmBackend.cs \
  src/BuiltinExtensions/AutoWebUIBackend/AutoWebUIAPIAbstractBackend.cs \
  src/Text2Image/CommonModels.cs \
  src/Text2Image/T2IParamInput.cs \
  src/Text2Image/T2IParamSet.cs \
  src/Text2Image/T2IParamTypes.cs \
  src/Text2Image/T2IPromptHandling.cs
```

Expected: remote HTTP and known-model downloads occur after copied snapshots release their claims; parameter conversion sees a live handler generation; no block upgrades to a write claim.

- [ ] **Step 7: Commit core and conversion coverage**

```bash
git add -- \
  src/Backends/BackendHandler.cs \
  src/Backends/SwarmSwarmBackend.cs \
  src/BuiltinExtensions/AutoWebUIBackend/AutoWebUIAPIAbstractBackend.cs \
  src/Text2Image/CommonModels.cs \
  src/Text2Image/T2IParamInput.cs \
  src/Text2Image/T2IParamSet.cs \
  src/Text2Image/T2IParamTypes.cs \
  src/Text2Image/T2IPromptHandling.cs
git diff --cached --check
git commit -m "fix: protect model conversion catalog reads"
```

---

### Task 6: Protect Comfy Providers and Workflow Construction

**Files:**
- Modify: `src/BuiltinExtensions/ComfyUIBackend/ComfyUIAPIAbstractBackend.cs`
- Modify: `src/BuiltinExtensions/ComfyUIBackend/ComfyUIBackendExtension.cs`
- Modify: `src/BuiltinExtensions/ComfyUIBackend/WorkflowGenerator.cs`
- Modify: `src/BuiltinExtensions/ComfyUIBackend/WorkflowGeneratorModelSupport.cs`
- Modify: `src/BuiltinExtensions/ComfyUIBackend/WorkflowGeneratorSteps.cs`

- [ ] **Step 1: Protect custom-workflow LoRA expansion**

In `ComfyUIAPIAbstractBackend`'s local `getLoras`, acquire a read claim around LoRA name enumeration and best-match selection:

```csharp
string[] matches;
using (ManyReadOneWriteLock.ReadClaim claim = Program.RefreshLock.LockRead())
{
    string[] loraNames = [.. Program.T2IModelSets["LoRA"].ListModelNamesFor(user_input.SourceSession)];
    matches = [.. user_input.Get(T2IParamTypes.Loras, [])
        .Select(lora => T2IParamTypes.GetBestModelInList(lora, loraNames))];
}
```

Keep validation and string joining unchanged.

- [ ] **Step 2: Protect PiD providers and resolver**

Convert `PidUpscaleModels` from an expression body to:

```csharp
public static List<string> PidUpscaleModels(Session session)
{
    using ManyReadOneWriteLock.ReadClaim claim = Program.RefreshLock.LockRead();
    return [.. Program.MainSDModels.ListModelsFor(session)
        .Where(m => m.ModelClass?.CompatClass?.ID == "pid")
        .OrderBy(m => m.Name)
        .Select(m => $"pidmodel-{m.Name}///PiD Model: {m.Name}")];
}
```

In `GetPidModel`, hold one read claim across list matching and `GetModel`.

Replace the Pixel Decoder `GetValues` expression with a named helper that acquires a read claim and returns the same cleaned list.

- [ ] **Step 3: Protect synchronous WorkflowGenerator reads**

Add focused read claims in the methods/step actions containing:

- LoRA handler resolution at both confinement-loading sites;
- Stable Cascade stage-B lookup;
- SVD VAE fallback lookup;
- Stable Cascade stage-C lookup;
- TensorRT same-architecture enumeration; and
- the VAE selection step registered in `WorkflowGeneratorSteps`.

For each block, acquire before the first `MainSDModels`/`T2IModelSets` access and release after the last use of the resolved handler or model map. Do not wrap `WorkflowGenerator.Generate` as a whole because known-model helpers can download and invoke a write-side refresh.

The VAE step pattern is:

```csharp
using ManyReadOneWriteLock.ReadClaim claim = Program.RefreshLock.LockRead();
T2IModelHandler vaeHandler = Program.T2IModelSets["VAE"];
string match = T2IParamTypes.GetBestModelInList(
    vaeName,
    vaeHandler.ListModelNamesFor(g.UserInput.SourceSession));
if (match is not null)
{
    T2IModel vaeModel = vaeHandler.Models[match];
    g.LoadingVAE = g.CreateVAELoader(
        vaeModel.ToString(g.ModelFolderFormat),
        g.HasNode("11") ? null : "11");
}
```

- [ ] **Step 4: Split known-VAE download and refresh phases**

For each `knownFile.DownloadNow().Wait(); Program.RefreshAllModelSets();` path:

1. acquire a read claim and determine whether the file is absent;
2. release the read claim;
3. download only when absent;
4. call `Program.RefreshAllModelSets()` with no read claim live; and
5. continue using copied names, reacquiring a read claim if model verification is required.

Use a Boolean phase marker:

```csharp
bool downloadRequired;
using (ManyReadOneWriteLock.ReadClaim claim = Program.RefreshLock.LockRead())
{
    downloadRequired = !Program.T2IModelSets["VAE"].Models.ContainsKey(vaeFile);
}
if (downloadRequired)
{
    knownFile.DownloadNow().Wait();
    Program.RefreshAllModelSets();
}
```

For the default-VAE path that also selects a compatibility match, perform all current reads under one claim, release it, then download/refresh only if selection still requires the known file.

- [ ] **Step 5: Split Clip destination lookup from download**

In `RequireClipModel`, read existing presence and copy `DownloadFolderPath` under one claim:

```csharp
string filePath;
using (ManyReadOneWriteLock.ReadClaim claim = Program.RefreshLock.LockRead())
{
    T2IModelHandler clipHandler = Program.T2IModelSets["Clip"];
    if (clipHandler.Models.ContainsKey(name))
    {
        ClipModelsValid.TryAdd(name, name);
        return name;
    }
    filePath = $"{clipHandler.DownloadFolderPath}/{name}";
}
g.DownloadModel(name, filePath, url, hash);
```

Protect the Clip-L and Clip-G fallback existence checks with focused read claims. Do not hold claims across `g.DownloadModel`.

- [ ] **Step 6: Verify workflow lock phases**

Run:

```bash
rg -n -C 8 'Program\.(T2IModelSets|MainSDModels)|RefreshAllModelSets\(|DownloadNow\(\)\.Wait|DownloadModel\(' \
  src/BuiltinExtensions/ComfyUIBackend/ComfyUIAPIAbstractBackend.cs \
  src/BuiltinExtensions/ComfyUIBackend/ComfyUIBackendExtension.cs \
  src/BuiltinExtensions/ComfyUIBackend/WorkflowGenerator.cs \
  src/BuiltinExtensions/ComfyUIBackend/WorkflowGeneratorModelSupport.cs \
  src/BuiltinExtensions/ComfyUIBackend/WorkflowGeneratorSteps.cs

git diff --check -- \
  src/BuiltinExtensions/ComfyUIBackend/ComfyUIAPIAbstractBackend.cs \
  src/BuiltinExtensions/ComfyUIBackend/ComfyUIBackendExtension.cs \
  src/BuiltinExtensions/ComfyUIBackend/WorkflowGenerator.cs \
  src/BuiltinExtensions/ComfyUIBackend/WorkflowGeneratorModelSupport.cs \
  src/BuiltinExtensions/ComfyUIBackend/WorkflowGeneratorSteps.cs
```

Expected: no read claim lexically contains `DownloadNow().Wait`, `DownloadModel`, or `RefreshAllModelSets`; all direct catalog reads are protected or occur in a documented write-side callback.

- [ ] **Step 7: Commit Comfy reader coverage**

```bash
git add -- \
  src/BuiltinExtensions/ComfyUIBackend/ComfyUIAPIAbstractBackend.cs \
  src/BuiltinExtensions/ComfyUIBackend/ComfyUIBackendExtension.cs \
  src/BuiltinExtensions/ComfyUIBackend/WorkflowGenerator.cs \
  src/BuiltinExtensions/ComfyUIBackend/WorkflowGeneratorModelSupport.cs \
  src/BuiltinExtensions/ComfyUIBackend/WorkflowGeneratorSteps.cs
git diff --cached --check
git commit -m "fix: protect comfy model catalog reads"
```

---

### Task 7: Finish Comfy API Long-Running Phase Separation

**Files:**
- Modify: `src/BuiltinExtensions/ComfyUIBackend/ComfyUIWebAPI.cs`

- [ ] **Step 1: Protect TensorRT input model selection**

Acquire a read claim around the `MainSDModels` best-match and model lookup. Copy the selected `T2IModel` result, release the claim, then send any not-found response and perform the existing WebSocket/backend work.

Do not hold a read claim through `RunWebsocketHandlerCallWS`. The final `Program.RefreshAllModelSets()` remains a write-owning call.

- [ ] **Step 2: Protect LoRA extraction setup**

Acquire one read claim around:

- best-match selection for both input models;
- both `T2IModel` lookups;
- validation that both models exist; and
- copying `Program.T2IModelSets["LoRA"].DownloadFolderPath`.

Use the copied LoRA output folder when building the workflow JSON. Release the claim before sending any validation response or entering `RunWebsocketHandlerCallWS`.

- [ ] **Step 3: Refresh and verify in separate phases**

After extraction completes:

```csharp
Program.RefreshModelSet("LoRA");
bool outputExists;
using (ManyReadOneWriteLock.ReadClaim claim = Program.RefreshLock.LockRead())
{
    outputExists = Program.T2IModelSets.TryGetValue("LoRA", out T2IModelHandler loras)
        && loras.Models.ContainsKey($"{outName}.safetensors");
}
```

Use `outputExists` in the unchanged success/error response branch. No handler reference escapes the read phase.

- [ ] **Step 4: Verify no long-running claim**

Run:

```bash
rg -n -C 12 'Program\.(T2IModelSets|MainSDModels)|LockRead\(\)|RefreshModelSet\(|RefreshAllModelSets\(|RunWebsocketHandlerCallWS' \
  src/BuiltinExtensions/ComfyUIBackend/ComfyUIWebAPI.cs

git diff --check -- src/BuiltinExtensions/ComfyUIBackend/ComfyUIWebAPI.cs
```

Expected: model selection and copied destinations are protected; no read claim spans WebSocket streaming; refresh occurs with no read claim; verification reacquires a read claim.

- [ ] **Step 5: Commit Comfy API phase separation**

```bash
git add -- src/BuiltinExtensions/ComfyUIBackend/ComfyUIWebAPI.cs
git diff --cached --check
git commit -m "fix: separate comfy model refresh phases"
```

---

### Task 8: Perform Whole-Project Static Conformance Review

**Files:**
- Inspect: all implementation files
- Modify if required: only files already in the approved design scope

- [ ] **Step 1: Repeat the complete consumer inventory**

Run:

```bash
rg -n 'Program\.(T2IModelSets|MainSDModels)' src \
  --glob '*.cs' \
  --glob '*.cshtml' \
  --glob '!src/bin/**' \
  --glob '!src/obj/**' \
  --glob '!src/Extensions/**'
```

For every result, prove one of:

- startup occurs before concurrent readers;
- the code is inside Program's write-side core or an event callback already under the write boundary;
- a read claim begins before handler resolution and ends after handler use; or
- only a copied independent result remains after claim disposal.

Fix any uncovered maintained access before continuing.

- [ ] **Step 2: Repeat the writer inventory**

Run:

```bash
rg -n 'BuildModelLists\(|RefreshAllModelSets\(|RefreshModelSet\(|RebuildModelListsForPathChange\(|ModelRefreshEvent\?\.Invoke|ModelPathsChangedEvent\?\.Invoke|\.Refresh\(\)' \
  src/Core src/WebAPI src/Text2Image src/Backends src/BuiltinExtensions \
  --glob '*.cs' \
  --glob '!src/bin/**' \
  --glob '!src/obj/**'
```

Prove:

- settings uses only `RebuildModelListsForPathChange`;
- public build/full/single refresh operations own write claims;
- private core operations are called only by write owners or startup composition;
- `T2IAPI.TriggerRefresh` retains its existing `RefreshSemaphore` then write-claim order;
- no stale handler is resolved before a later write claim; and
- no public write owner is called from inside another catalog claim.

- [ ] **Step 3: Audit lock upgrades and long claim spans**

Run:

```bash
rg -n -C 15 'LockRead\(\)|LockWrite\(\)|RefreshAllModelSets\(|RefreshModelSet\(|DownloadNow\(\)|DownloadModel\(|RunWebsocketHandlerCallWS|WaitForExitAsync|PostJson\(' \
  src/Core src/WebAPI src/Text2Image src/Backends src/BuiltinExtensions src/Pages \
  --glob '*.cs' \
  --glob '*.cshtml' \
  --glob '!src/Extensions/**'
```

Manually confirm:

- no read claim contains a write-owning refresh call;
- no claim spans network download or remote HTTP merely because a path/key could have been copied;
- no claim spans child-process waits;
- no new claim spans GPU/backend execution;
- event callbacks do not reacquire `RefreshLock`; and
- settings transaction ownership ends before the runtime catalog write.

- [ ] **Step 4: Verify public identity, key, and contract preservation**

Run:

```bash
rg -n 'public static Dictionary<string, T2IModelHandler> T2IModelSets|T2IModelSets\s*=' src/Core/Program.cs
rg -n 'AddModelSet\("(Stable-Diffusion|VAE|LoRA|Embedding|ControlNet|Clip|ClipVision)"' src/Core/Program.cs
git diff 8c7585d9..HEAD -- src/WebAPI/AdminAPI.cs src/Core/Program.cs
```

Expected:

- the public dictionary is initialized once and never reassigned;
- all seven keys and path expressions match the baseline;
- settings request/response and warning behavior are unchanged except for the centralized side-effect call.

- [ ] **Step 5: Run repository-safe diff checks**

Run:

```bash
git diff --check 8c7585d9..HEAD
git status --short --untracked-files=no
git diff --name-only 8c7585d9..HEAD
```

Expected:

- no whitespace errors;
- the four maintainer files remain modified but absent from rank 7 commits;
- only approved production and documentation files occur in the implementation range.

- [ ] **Step 6: Commit any review corrections**

If review required changes, explicitly stage only the corrected approved-scope files listed by `git diff --name-only`, run `git diff --cached --check`, inspect `git diff --cached --name-only`, and commit them with:

```bash
git commit -m "fix: complete model catalog coordination"
```

If no changes were required, do not create an empty commit.

---

### Task 9: Record Implemented State and Prepare Maintainer Validation

**Files:**
- Modify: `docs/superpowers/specs/2026-07-24-runtime-model-catalog-refresh-boundary-design.md`
- Modify: `docs/superpowers/audits/2026-07-21-maintainability-architecture-refresh.md`

- [ ] **Step 1: Update the design status conservatively**

Change the design status to:

```markdown
**Status:** Implemented; awaiting maintainer validation
```

Add an implementation record containing:

- the exact production commit range;
- the final maintained reader and writer inventory counts;
- the central owner method names;
- confirmation that public dictionary identity and seven keys remain;
- the static conformance commands and results;
- the fact that no build, launch, test, GPU operation, or performance benchmark was run; and
- the complete pending live-validation matrix.

- [ ] **Step 2: Update Core F7 in the architecture audit**

Change only Core F7 and the roadmap/recommended-next sections that depend on its disposition. Record:

- **Implemented; awaiting maintainer validation**;
- exact production commits and files;
- one write-owning path transition;
- complete maintained direct/derived reader coverage;
- explicit download/refresh phase separation;
- static evidence and remaining runtime uncertainty; and
- no performance claim.

Do not mark Core F7 maintainer-validated yet.

- [ ] **Step 3: Verify documentation consistency**

Run:

```bash
rg -n 'Status:|Implemented; awaiting maintainer validation|Core F7|runtime model|Recommended Next' \
  docs/superpowers/specs/2026-07-24-runtime-model-catalog-refresh-boundary-design.md \
  docs/superpowers/audits/2026-07-21-maintainability-architecture-refresh.md

rg -n 'TBD|placeholder|performance improvement|maintainer-validated' \
  docs/superpowers/specs/2026-07-24-runtime-model-catalog-refresh-boundary-design.md

git diff --check -- \
  docs/superpowers/specs/2026-07-24-runtime-model-catalog-refresh-boundary-design.md \
  docs/superpowers/audits/2026-07-21-maintainability-architecture-refresh.md
```

Expected: no placeholders, no premature validation claim, no benchmark claim, and consistent Core F7 status.

- [ ] **Step 4: Commit implemented-state documentation**

```bash
git add -- \
  docs/superpowers/specs/2026-07-24-runtime-model-catalog-refresh-boundary-design.md \
  docs/superpowers/audits/2026-07-21-maintainability-architecture-refresh.md
git diff --cached --check
git commit -m "docs: record model catalog coordination"
```

- [ ] **Step 5: Hand the exact live matrix to Reaper176**

Request maintainer validation of:

1. normal build, launch, parameter listing, model browsing, and generation;
2. idle model-root edit and restoration;
3. path edits overlapping parameter listing, browsing, request conversion, metadata work, Comfy workflow construction, remote serialization, and user-triggered refresh;
4. generic download, known-model auto-download, LoRA extraction, TensorRT completion, and missing VAE/Clip helper paths;
5. enabled Comfy self-start backend path reload;
6. unavailable/unreadable model location warning behavior;
7. repeated edits, refreshes, shutdown, and restart agreement; and
8. unchanged payloads, keys, visibility, destinations, metadata, built-ins, and applicable external extension behavior.

Stop and wait for the maintainer's result. Do not infer runtime success from static review.

---

### Task 10: Record Maintainer Validation and Close Rank 7

**Files:**
- Modify after confirmation: `docs/superpowers/specs/2026-07-24-runtime-model-catalog-refresh-boundary-design.md`
- Modify after confirmation: `docs/superpowers/audits/2026-07-21-maintainability-architecture-refresh.md`

- [ ] **Step 1: Record only the matrix actually confirmed**

After Reaper176 reports results, change status to:

```markdown
**Status:** Implemented and maintainer-validated
```

Record the validation date, platform scope, exact confirmed cases, any inapplicable cases, and any remaining platform/performance limitations. Do not broaden the maintainer's statement.

- [ ] **Step 2: Update Core F7 disposition and recommended next work**

Mark Core F7 completed only if the required matrix passed. Recompute the audit's ranked roadmap without changing unrelated findings. Preserve measurement-gated performance candidates.

- [ ] **Step 3: Run final static verification**

Run:

```bash
git diff --check 8c7585d9..HEAD
git status --short --untracked-files=no
git log --oneline 8c7585d9..HEAD
rg -n 'Implemented and maintainer-validated|Core F7|Recommended Next' \
  docs/superpowers/specs/2026-07-24-runtime-model-catalog-refresh-boundary-design.md \
  docs/superpowers/audits/2026-07-21-maintainability-architecture-refresh.md
rg -n 'Program\.(T2IModelSets|MainSDModels)' src \
  --glob '*.cs' \
  --glob '*.cshtml' \
  --glob '!src/bin/**' \
  --glob '!src/obj/**' \
  --glob '!src/Extensions/**'
```

Expected: clean implementation range, only protected maintainer files dirty, exact validation status, and no uncovered maintained catalog access.

- [ ] **Step 4: Commit validation documentation**

```bash
git add -- \
  docs/superpowers/specs/2026-07-24-runtime-model-catalog-refresh-boundary-design.md \
  docs/superpowers/audits/2026-07-21-maintainability-architecture-refresh.md
git diff --cached --check
git commit -m "docs: validate runtime model catalog boundary"
```

- [ ] **Step 5: Report closure**

Report:

- production and documentation commit ranges;
- the exact runtime scope confirmed by Reaper176;
- static verification results;
- protected maintainer files left untouched;
- current `master` HEAD;
- whether anything is unpushed; and
- the audit's next recommended rank.

Do not push unless the maintainer explicitly requests it.
