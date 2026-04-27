# History Fast-First Load Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make SwarmUI eagerly load History at startup while showing the newest recent work first and filling older history in afterward.

**Architecture:** Keep the existing eager startup trigger in `main.js`, but change `ListImages` and `outputhistory.js` into a two-phase pipeline. The first request returns a bounded newest-first bootstrap slice for quick rendering; the second request fetches the normal full dataset and replaces the bootstrap list once it arrives. Because `AGENTS.md` forbids agent-run builds and tests in this repo, implementation verification here is limited to static checks plus developer-run manual UI validation.

**Tech Stack:** C# 12 / .NET 8 WebAPI, browser JavaScript in `src/wwwroot/js/genpage`, existing `GenPageBrowserClass` browser rendering.

---

### Task 1: Add Fast-First Startup Support To `ListImages`

**Files:**
- Modify: `src/WebAPI/T2IAPI.cs`
- Static check: `src/WebAPI/T2IAPI.cs`
- Manual verification: developer-run live UI validation after implementation

- [ ] **Step 1: Extend the `ListImages` API signature with fast-first request parameters**

Add two new parameters to the public API and thread them through to `GetListAPIInternal`.

```csharp
public static async Task<JObject> ListImages(Session session,
    [API.APIParameter("The folder path to start the listing in. Use an empty string for root.")] string path,
    [API.APIParameter("Maximum depth (number of recursive folders) to search.")] int depth,
    [API.APIParameter("What to sort the list by - `Name` or `Date`.")] string sortBy = "Name",
    [API.APIParameter("If true, the sorting should be done in reverse.")] bool sortReverse = false,
    [API.APIParameter("If true, include images marked as hidden.")] bool includeHidden = false,
    [API.APIParameter("If true, return only a bounded startup slice biased toward newest work.")] bool fastFirst = false,
    [API.APIParameter("Maximum number of files to return when fastFirst is enabled.")] int fastFirstLimit = 128)
{
    if (!Enum.TryParse(sortBy, true, out ImageHistorySortMode sortMode))
    {
        return new JObject() { ["error"] = $"Invalid sort mode '{sortBy}'." };
    }
    string root = Utilities.CombinePathWithAbsolute(Environment.CurrentDirectory, session.User.OutputDirectory);
    return GetListAPIInternal(session, path, root, HistoryExtensions, f => true, depth, sortMode, sortReverse, includeHidden, fastFirst, fastFirstLimit);
}
```

Update the private helper signature to match:

```csharp
private static JObject GetListAPIInternal(Session session, string rawPath, string root, HashSet<string> extensions, Func<string, bool> isAllowed, int depth, ImageHistorySortMode sortBy, bool sortReverse, bool includeHidden, bool fastFirst = false, int fastFirstLimit = 128)
```

- [ ] **Step 2: Add a bounded newest-first bootstrap branch inside `GetListAPIInternal`**

Keep the existing directory discovery logic, then add a fast-first file collection branch before the current parallel full-load branch. The bootstrap branch should:

- Clamp to a small bounded limit.
- Walk newest directories first.
- Walk newest file names first inside each directory.
- Reuse the existing metadata filtering rules.
- Return immediately once the bounded startup slice is filled.

Use code shaped like this:

```csharp
int limit = fastFirst ? Math.Max(1, Math.Min(fastFirstLimit, maxInHistory)) : sortBy == ImageHistorySortMode.Name ? maxInHistory : Math.Max(maxInHistory, maxScanned);

List<ImageHistoryHelper> collectFastFirstFiles(List<string> knownDirs, bool starNoFoldersLocal)
{
    List<ImageHistoryHelper> fastFiles = [];
    foreach (string folder in knownDirs.Append(""))
    {
        int localLimit = limit - fastFiles.Count;
        if (localLimit <= 0)
        {
            break;
        }
        string prefix = folder == "" ? "" : folder + "/";
        string actualPath = $"{path}/{prefix}";
        actualPath = UserImageHistoryHelper.GetRealPathFor(session.User, actualPath, root: root);
        if (!Directory.Exists(actualPath))
        {
            continue;
        }
        IEnumerable<string> orderedFiles = Directory.EnumerateFiles(actualPath)
            .Select(f => f.Replace('\\', '/'))
            .Where(isAllowed)
            .Where(f => !f.AfterLast('/').StartsWithFast('.') && extensions.Contains(f.AfterLast('.')) && !f.EndsWith(".swarmpreview.jpg") && !f.EndsWith(".swarmpreview.webp"))
            .OrderDescending()
            .Take(localLimit);
        fastFiles.AddRange(orderedFiles
            .Select(f => new ImageHistoryHelper(prefix + f.AfterLast('/'), OutputMetadataTracker.GetMetadataFor(f, root, starNoFoldersLocal)))
            .Where(f => f.Metadata is not null)
            .Where(f => includeHidden || !MetadataIsHidden(f.Metadata)));
    }
    return fastFiles;
}
```

Then branch before the existing `Parallel.ForEach` path:

```csharp
bool starNoFolders = session.User.Settings.StarNoFolders;
List<ImageHistoryHelper> files;
if (fastFirst)
{
    files = collectFastFirstFiles(dirs, starNoFolders);
}
else
{
    ConcurrentDictionary<int, List<ImageHistoryHelper>> filesConc = [];
    int id = 0;
    int remaining = limit;
    Parallel.ForEach(dirs.Append(""), new ParallelOptions() { MaxDegreeOfParallelism = 5, CancellationToken = Program.GlobalProgramCancel }, folder =>
    {
        int localId = Interlocked.Increment(ref id);
        int localLimit = Interlocked.CompareExchange(ref remaining, 0, 0);
        if (localLimit <= 0)
        {
            return;
        }
        string prefix = folder == "" ? "" : folder + "/";
        string actualPath = $"{path}/{prefix}";
        actualPath = UserImageHistoryHelper.GetRealPathFor(session.User, actualPath, root: root);
        if (!Directory.Exists(actualPath))
        {
            return;
        }
        List<string> subFiles = [.. Directory.EnumerateFiles(actualPath).Take(localLimit)];
        IEnumerable<string> newFileNames = subFiles.Select(f => f.Replace('\\', '/')).Where(isAllowed).Where(f => !f.AfterLast('/').StartsWithFast('.') && extensions.Contains(f.AfterLast('.')) && !f.EndsWith(".swarmpreview.jpg") && !f.EndsWith(".swarmpreview.webp"));
        List<ImageHistoryHelper> localFiles = [.. newFileNames.Select(f => new ImageHistoryHelper(prefix + f.AfterLast('/'), OutputMetadataTracker.GetMetadataFor(f, root, starNoFolders))).Where(f => f.Metadata is not null).Where(f => includeHidden || !MetadataIsHidden(f.Metadata))];
        int leftOver = Interlocked.Add(ref remaining, -localFiles.Count);
        sortList(localFiles);
        filesConc.TryAdd(localId, localFiles);
        if (leftOver <= 0)
        {
            return;
        }
    });
    files = [.. filesConc.Values.SelectMany(f => f).Take(limit)];
}
```

- [ ] **Step 3: Preserve the existing final response shape and starred-path cleanup**

Do not change the response contract consumed by the frontend. Keep the starred dedupe cleanup and final `JObject` return intact after the fast-first/full branch, so both modes still return:

```csharp
return new JObject()
{
    ["folders"] = JToken.FromObject(dirs.Union(finalDirs.Keys).ToList()),
    ["files"] = JToken.FromObject(files.Take(maxInHistory).Select(f => new JObject() { ["src"] = f.Name, ["metadata"] = f.Metadata.Metadata }).ToList())
};
```

The important behavior change is only *which* files are collected when `fastFirst` is enabled, not the response schema.

- [ ] **Step 4: Run static checks for the backend change**

Run:

```bash
rg -n "fastFirst|fastFirstLimit|collectFastFirstFiles" src/WebAPI/T2IAPI.cs
```

Expected:

- The public `ListImages` signature includes `fastFirst` and `fastFirstLimit`.
- `GetListAPIInternal` includes the fast-first parameters.
- The fast-first collection branch exists before the current full-load branch.

Run:

```bash
git diff --check -- src/WebAPI/T2IAPI.cs
```

Expected:

- No output.

- [ ] **Step 5: Commit the backend change**

```bash
git add src/WebAPI/T2IAPI.cs
git commit -m "feat: add fast-first history bootstrap mode"
```

### Task 2: Stage The Startup History Load In `outputhistory.js`

**Files:**
- Modify: `src/wwwroot/js/genpage/gentab/outputhistory.js`
- Static check: `src/wwwroot/js/genpage/gentab/outputhistory.js`
- Manual verification: developer-run live UI validation after implementation

- [ ] **Step 1: Add explicit startup-stage state and a bounded bootstrap limit**

Add the state near the top of `outputhistory.js` with the other history globals:

```javascript
let imageHistoryStartupStage = 'pending';
let imageHistoryLoadToken = 0;
const IMAGE_HISTORY_FAST_FIRST_LIMIT = 128;
```

Use the stages exactly as follows:

- `'pending'`: initial eager startup load has not yet produced the bootstrap slice.
- `'recent_loaded'`: bootstrap slice is rendered and the full background fill is still pending.
- `'complete'`: the full history replacement pass has finished successfully.

- [ ] **Step 2: Refactor the existing request-mapping logic into reusable helpers**

Extract the repeated ordering and mapping work out of `listOutputHistoryFolderAndFiles()` so the fast-first callback and background full-load callback both use the same logic.

Add helpers shaped like this:

```javascript
function orderHistoryFilesForDisplay(files) {
    function isPreSortFile(file) {
        return file.src == 'index.html';
    }
    let preFiles = files.filter(file => isPreSortFile(file));
    let postFiles = files.filter(file => !isPreSortFile(file));
    return preFiles.concat(postFiles);
}

function mapHistoryFiles(prefix, files) {
    return files.map(file => {
        let fullSrc = `${prefix}${file.src}`;
        return { 'name': fullSrc, 'data': { 'src': getHistoryImageSrc(fullSrc), 'fullsrc': fullSrc, 'name': file.src, 'metadata': safeInterpretHistoryMetadata(file.metadata, fullSrc) } };
    });
}

function replaceHistoryBrowserContents(path, folders, mapped) {
    if (!imageHistoryBrowser) {
        return;
    }
    imageHistoryBrowser.lastListCache = { folder: path, folders, files: mapped };
    imageHistoryBrowser.build(path, folders, mapped);
}
```

`replaceHistoryBrowserContents()` intentionally replaces the bootstrap slice with the full response once it arrives. That keeps ordering correct for the selected sort mode and avoids duplicate-merge complexity.

- [ ] **Step 3: Convert `listOutputHistoryFolderAndFiles()` into a two-phase startup loader**

Replace the single `genericRequest('ListImages', ...)` path with staged logic that:

- Uses `fastFirst` only on the initial eager root-path load.
- Immediately renders the fast-first result via the existing `callback`.
- Leaves the request status in a loading state while the full background fill is still pending.
- Replaces the bootstrap list with the full response when the background request returns.
- Ignores stale background responses by comparing against `imageHistoryLoadToken` and the current folder.

Use code shaped like this:

```javascript
function queueFullImageHistoryLoad(path, depth, sortBy, reverse, showHidden, loadToken) {
    setTimeout(() => {
        genericRequest('ListImages', { 'path': path, 'depth': depth, 'sortBy': sortBy, 'sortReverse': reverse, 'includeHidden': showHidden }, data => {
            if (loadToken != imageHistoryLoadToken || !imageHistoryBrowser || imageHistoryBrowser.folder != path) {
                return;
            }
            let prefix = path == '' ? '' : (path.endsWith('/') ? path : `${path}/`);
            let folders = data.folders.sort((a, b) => b.toLowerCase().localeCompare(a.toLowerCase()));
            let mapped = mapHistoryFiles(prefix, orderHistoryFilesForDisplay(data.files));
            imageHistoryStartupStage = 'complete';
            replaceHistoryBrowserContents(path, folders, mapped);
            setImageHistoryRequestStatus('idle');
        }, 0, error => {
            if (loadToken != imageHistoryLoadToken) {
                return;
            }
            console.log(`Background history fill failed: ${error}`);
            setImageHistoryRequestStatus('idle');
        });
    }, 0);
}
```

Then rewrite the main request body like this:

```javascript
let prefix = path == '' ? '' : (path.endsWith('/') ? path : `${path}/`);
let isRetryLoad = imageHistoryNextLoadIsRetry;
let loadToken = ++imageHistoryLoadToken;
let useFastFirst = imageHistoryStartupStage == 'pending' && path == '' && !isRefresh;
let request = { 'path': path, 'depth': depth, 'sortBy': sortBy, 'sortReverse': reverse, 'includeHidden': showHidden };
if (useFastFirst) {
    request.fastFirst = true;
    request.fastFirstLimit = IMAGE_HISTORY_FAST_FIRST_LIMIT;
}
setImageHistoryRequestStatus(isRetryLoad ? 'retrying' : 'loading', isRetryLoad ? 'Retrying history load...' : 'Loading history...');
genericRequest('ListImages', request, data => {
    clearImageHistoryAutoRetry();
    imageHistoryHasLoadedOnce = true;
    let folders = data.folders.sort((a, b) => b.toLowerCase().localeCompare(a.toLowerCase()));
    let mapped = mapHistoryFiles(prefix, orderHistoryFilesForDisplay(data.files));
    callback(folders, mapped);
    if (useFastFirst) {
        imageHistoryStartupStage = 'recent_loaded';
        queueFullImageHistoryLoad(path, depth, sortBy, reverse, showHidden, loadToken);
        return;
    }
    imageHistoryStartupStage = 'complete';
    setImageHistoryRequestStatus('idle');
}, 0, error => {
    showError(error);
    let shouldRetry = !isRetryLoad && scheduleImageHistoryAutoRetry();
    let errorMessage = `History failed to load: ${error}`;
    if (shouldRetry) {
        errorMessage += ' Retrying once...';
    }
    setImageHistoryRequestStatus('error', errorMessage);
    if (onError) {
        onError(error);
    }
});
```

Important behavior notes for this task:

- Keep the existing eager startup call in `main.js` unchanged.
- Do not use fast-first on folder navigation or manual refresh.
- Do not show a blocking error if the background fill returns after the user has already navigated elsewhere.
- Leave selection state handling alone; it already lives outside the browser file list and will be reapplied by `describeOutputFile()`.

- [ ] **Step 4: Run static checks for the frontend change**

Run:

```bash
rg -n "IMAGE_HISTORY_FAST_FIRST_LIMIT|imageHistoryStartupStage|queueFullImageHistoryLoad|mapHistoryFiles|replaceHistoryBrowserContents" src/wwwroot/js/genpage/gentab/outputhistory.js
```

Expected:

- The new stage state and bootstrap limit exist.
- The file-ordering and mapping helpers exist.
- The background full-load helper exists.
- The main history request path references `fastFirst` and queues the full-load pass.

Run:

```bash
git diff --check -- src/wwwroot/js/genpage/gentab/outputhistory.js
```

Expected:

- No output.

- [ ] **Step 5: Commit the frontend change**

```bash
git add src/wwwroot/js/genpage/gentab/outputhistory.js
git commit -m "feat: stage startup history loading"
```

### Task 3: Developer-Run Manual Validation And Final Cleanup

**Files:**
- Verify: `src/WebAPI/T2IAPI.cs`
- Verify: `src/wwwroot/js/genpage/gentab/outputhistory.js`
- Manual validation only: live SwarmUI startup flow

- [ ] **Step 1: Run final static sanity checks before handing off for manual UI validation**

Run:

```bash
git diff --check
```

Expected:

- No output.

Run:

```bash
git status --short
```

Expected:

- Only the intended implementation files are modified or committed.

- [ ] **Step 2: Have the developer validate the staged startup behavior in the live UI**

Developer validation checklist:

1. Start SwarmUI and leave the default eager History startup path enabled.
2. Confirm the first recent thumbnails appear quickly rather than waiting for the full history load.
3. Confirm more history appears afterward without requiring a tab reopen.
4. Confirm the History tab is immediately useful for resuming recent work.
5. Confirm hide, unhide, delete, selection, and current-image selection still work after the full replacement pass.
6. Confirm a manual refresh still loads the normal full history path without repeating the bootstrap stage.

- [ ] **Step 3: Commit any final fixups from manual validation**

If manual validation required no follow-up changes, no extra commit is needed.

If manual validation required a small follow-up fix, commit it with one of:

```bash
git add src/WebAPI/T2IAPI.cs src/wwwroot/js/genpage/gentab/outputhistory.js
git commit -m "fix: stabilize staged history startup loading"
```

or

```bash
git add src/wwwroot/js/genpage/gentab/outputhistory.js
git commit -m "fix: preserve history bootstrap replacement flow"
```
