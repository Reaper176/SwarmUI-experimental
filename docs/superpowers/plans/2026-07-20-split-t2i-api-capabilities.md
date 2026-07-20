# T2I API Capability Split Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Move classic inpainting, Krita integration, and image-history implementations into capability-owned API classes while preserving every route and the legacy `T2IAPI` C# surface.

**Architecture:** Add `ClassicInpaintAPI`, `KritaAPI`, and `ImageHistoryAPI` as implementation owners. Keep `T2IAPI.Register()` as the exact ordered composition list, retain public history data contracts there, and replace moved public implementations with annotation-preserving forwarders.

**Tech Stack:** C# 12, .NET 8 source conventions, ASP.NET HTTP/WebSocket API reflection, Newtonsoft JSON, ImageSharp, Git, `rg`, `sed`, `awk`, and static source inspection.

---

## Execution Constraints

- Work directly on `master`; the maintainer explicitly declined a worktree.
- Do not run builds, tests, browser automation, the live server, package installation, formatters, or code generators.
- Use `apply_patch` for all working-tree file edits.
- Before each stage, stop if any stage-owned `src/WebAPI` file is already dirty.
- Do not modify or stage `src/Data/Settings.fds`, `src/Pages/Text2Image.cshtml`, `src/wwwroot/js/genpage/gentab/loras.js`, `src/wwwroot/js/genpage/main.js`, or `Data.pre-restore-2026-07-19/`.
- Do not inspect local user-data directories.
- Preserve method bodies during relocation except for the explicitly listed `T2IAPI.` qualifications required by retained history contracts and `RequestToParams`.
- Preserve method names, visibility, return types, parameter names/types/order/defaults, API annotations, route order, permissions, and `isUserUpdate` values.
- Retain API annotations on both registered owner methods and legacy forwarders. Only owner methods are registered.
- Do not introduce services, interfaces, dependency injection, new validation, renamed symbols, algorithm cleanup, or public contract migration.

## File Structure

- Create: `src/WebAPI/ClassicInpaintAPI.cs` — IOPaint discovery, process execution, mask preparation, and classic-inpaint routes.
- Create: `src/WebAPI/KritaAPI.cs` — Krita send/import/poll/session routes.
- Create: `src/WebAPI/ImageHistoryAPI.cs` — direct insertion, listing/filtering/indexing, file operations, and metadata-mutation routes.
- Modify: `src/WebAPI/T2IAPI.cs` — exact ordered registration composition, retained generation/model behavior, retained public history data contracts, and compatibility forwarders.
- Reference only: `src/WebAPI/BackendAPI.cs` — continues calling `T2IAPI.RunProcessCapture`.
- Reference only: `src/BuiltinExtensions/GridGenerator/GridGeneratorExtension.cs` — continues calling `T2IAPI.DeleteImage`.
- Reference only: browser route consumers under `src/wwwroot/js` — route strings remain unchanged.

### Task 1: Capture the Baseline Contracts

**Files:**
- Read: `src/WebAPI/T2IAPI.cs`
- Read: `src/WebAPI/API.cs`
- Read: `src/WebAPI/BackendAPI.cs`
- Read: `src/BuiltinExtensions/GridGenerator/GridGeneratorExtension.cs`

- [ ] **Step 1: Confirm the branch and protected working state**

Run:

```bash
git branch --show-current
git status --short
git diff --quiet -- src/WebAPI/T2IAPI.cs src/WebAPI/BackendAPI.cs
```

Expected: branch `master`; known maintainer changes remain visible; the final command exits zero. Stop if either Web API file is already modified.

- [ ] **Step 2: Record the ordered route manifest**

Run:

```bash
sed -n '27,52p' src/WebAPI/T2IAPI.cs
```

Expected: 21 `API.RegisterAPICall` entries in this order:

```text
GenerateText2Image
GenerateText2ImageWS
AddImageToHistory
ListImages
RescanImageMetadata
ToggleImageStarred
ToggleImageHidden
SetImageRating
SetImageTags
SetImageNotes
OpenImageFolder
DeleteImage
BulkMoveImages
SendImageToKrita
ImportKritaImage
CheckPendingKritaImage
GetActiveKritaSession
ClassicInpaint
GetClassicInpaintBackends
ListT2IParams
TriggerRefresh
```

- [ ] **Step 3: Record public consumers and compatibility contracts**

Run:

```bash
rg -n 'T2IAPI\.(RunProcessCapture|DeleteImage)' src --glob '*.cs' --glob '!src/Extensions/**' --glob '!src/bin/**' --glob '!src/obj/**'
rg -n '^\s*public static (HashSet<string> HistoryExtensions|string\[\] DeletableFileExtensions|ConcurrentDictionary<string, byte> ImageHistoryIndexWarmups)|^\s*public (enum ImageHistorySortMode|record struct ImageHistoryHelper)' src/WebAPI/T2IAPI.cs
```

Expected: `BackendAPI` has three `RunProcessCapture` calls; Grid Generator has one `DeleteImage` call; all five legacy history contracts are present.

- [ ] **Step 4: Record moved method signatures and annotations**

Run:

```bash
for name in AddImageToHistory ListImages RescanImageMetadata OpenImageFolder DeleteImage BulkMoveImages ToggleImageStarred ToggleImageHidden SetImageRating SetImageTags SetImageNotes SendImageToKrita ImportKritaImage CheckPendingKritaImage GetActiveKritaSession GetIOPaintCommandCandidates RunProcessCapture PrepareClassicInpaintMask GetSupportedClassicInpaintBackends GetClassicInpaintBackends ClassicInpaint; do
    rg -n -B 12 -A 12 "public static .* ${name}\\(" src/WebAPI/T2IAPI.cs
done
```

Expected: all 21 public methods are found with their existing annotations, parameters, and defaults available for exact comparison during later tasks.

### Task 2: Extract `ClassicInpaintAPI`

**Files:**
- Create: `src/WebAPI/ClassicInpaintAPI.cs`
- Modify: `src/WebAPI/T2IAPI.cs:47-48,547-756`
- Reference: `src/WebAPI/BackendAPI.cs:157-245`

- [ ] **Step 1: Create the capability owner**

Use `apply_patch` to create `ClassicInpaintAPI.cs` with this file shell:

```csharp
using FreneticUtilities.FreneticExtensions;
using Newtonsoft.Json.Linq;
using SixLabors.ImageSharp.Processing;
using SwarmUI.Accounts;
using SwarmUI.Core;
using SwarmUI.Media;
using SwarmUI.Utils;
using System.Diagnostics;
using System.Linq;
using ISImageRGBA = SixLabors.ImageSharp.Image<SixLabors.ImageSharp.PixelFormats.Rgba32>;

namespace SwarmUI.WebAPI;

[API.APIClass("API routes for classic image inpainting.")]
public static class ClassicInpaintAPI
{
    // Exact relocated methods, in original order.
}
```

Replace the comment with exact copies of the six implementations currently spanning `GetIOPaintCommandCandidates` through `ClassicInpaint` in `T2IAPI.cs`. Do not change their bodies, internal calls, signatures, defaults, or annotations. The copied methods must call the new owner's sibling methods by their existing unqualified names.

- [ ] **Step 2: Replace old implementations with exact forwarders**

Using `apply_patch`, retain every existing annotation and declaration in `T2IAPI`, but replace each method body with only:

```csharp
return ClassicInpaintAPI.GetIOPaintCommandCandidates();
```

```csharp
return await ClassicInpaintAPI.RunProcessCapture(fileName, args, workingDirectory);
```

```csharp
ClassicInpaintAPI.PrepareClassicInpaintMask(maskImage, expandMask, feather);
```

```csharp
return await ClassicInpaintAPI.GetSupportedClassicInpaintBackends();
```

```csharp
return await ClassicInpaintAPI.GetClassicInpaintBackends(session);
```

```csharp
return await ClassicInpaintAPI.ClassicInpaint(session, imageData, maskData, backend, feather, expandMask);
```

The declarations remain, respectively: `string[]`, `Task<(int, string)>`, `void`, `Task<HashSet<string>>`, and two `Task<JObject>` methods with their original parameters/defaults.

- [ ] **Step 3: Point only the two routes at the new owner**

Use `apply_patch` so the existing registration positions become:

```csharp
API.RegisterAPICall(ClassicInpaintAPI.ClassicInpaint, true, Permissions.BasicImageGeneration);
API.RegisterAPICall(ClassicInpaintAPI.GetClassicInpaintBackends, false, Permissions.FundamentalGenerateTabAccess);
```

- [ ] **Step 4: Verify classic-inpaint ownership and compatibility**

Run:

```bash
rg -n 'API.RegisterAPICall\(ClassicInpaintAPI\.' src/WebAPI/T2IAPI.cs
rg -n 'ClassicInpaintAPI\.(GetIOPaintCommandCandidates|RunProcessCapture|PrepareClassicInpaintMask|GetSupportedClassicInpaintBackends|GetClassicInpaintBackends|ClassicInpaint)' src/WebAPI/T2IAPI.cs
rg -n 'T2IAPI\.RunProcessCapture' src/WebAPI/BackendAPI.cs
rg -n '^\s*(public|private) static .+\(' src/WebAPI/ClassicInpaintAPI.cs
git diff --check
```

Expected: two registered owner delegates; six forwarder calls; the three Backend API calls remain unchanged; exactly six public owner methods; no whitespace errors.

- [ ] **Step 5: Commit the classic-inpaint extraction**

Run:

```bash
git add -- src/WebAPI/ClassicInpaintAPI.cs src/WebAPI/T2IAPI.cs
git diff --cached --name-only
git diff --cached --check
git commit -m "Extract classic inpaint API"
```

Expected: exactly the two listed files are committed; maintainer changes remain unstaged.

### Task 3: Extract `KritaAPI`

**Files:**
- Create: `src/WebAPI/KritaAPI.cs`
- Modify: `src/WebAPI/T2IAPI.cs:43-46,2032-2109` before prior extraction shifts

- [ ] **Step 1: Confirm no stage-owned dirty files**

Run:

```bash
git diff --quiet -- src/WebAPI/T2IAPI.cs
test ! -e src/WebAPI/KritaAPI.cs
```

Expected: both commands exit zero.

- [ ] **Step 2: Create the Krita owner with exact implementations**

Use `apply_patch` to create `KritaAPI.cs` with this shell and replace the comment with exact relocated methods:

```csharp
using Microsoft.AspNetCore.Http;
using Newtonsoft.Json.Linq;
using SwarmUI.Accounts;
using SwarmUI.Core;
using SwarmUI.Media;
using SwarmUI.Utils;

namespace SwarmUI.WebAPI;

[API.APIClass("API routes for local Krita integration.")]
public static class KritaAPI
{
    // Exact SendImageToKrita, ImportKritaImage, CheckPendingKritaImage,
    // and GetActiveKritaSession implementations with annotations.
}
```

Do not change logic, logs, errors, loopback checks, signatures, parameter annotations, or API descriptions.

- [ ] **Step 3: Replace the four old implementations with forwarders**

Retain the original declarations and annotations in `T2IAPI`, using these bodies:

```csharp
return await KritaAPI.SendImageToKrita(session, imageData);
```

```csharp
return await KritaAPI.ImportKritaImage(session, imageBase64, targetSession);
```

```csharp
return await KritaAPI.CheckPendingKritaImage(session);
```

```csharp
return await KritaAPI.GetActiveKritaSession(context);
```

- [ ] **Step 4: Update the four delegates in place**

Use `apply_patch` so the registration block reads:

```csharp
API.RegisterAPICall(KritaAPI.SendImageToKrita, true, Permissions.LocalKritaBridge);
API.RegisterAPICall(KritaAPI.ImportKritaImage, true, Permissions.FundamentalGenerateTabAccess);
API.RegisterAPICall(KritaAPI.CheckPendingKritaImage, false, Permissions.FundamentalGenerateTabAccess);
API.RegisterAPICall(KritaAPI.GetActiveKritaSession, false, Permissions.FundamentalGenerateTabAccess);
```

- [ ] **Step 5: Verify and commit the Krita stage**

Run:

```bash
rg -n 'API.RegisterAPICall\(KritaAPI\.' src/WebAPI/T2IAPI.cs
rg -n 'return await KritaAPI\.' src/WebAPI/T2IAPI.cs
rg -n '^\s*public static async Task<JObject>' src/WebAPI/KritaAPI.cs
git diff --check
git add -- src/WebAPI/KritaAPI.cs src/WebAPI/T2IAPI.cs
git diff --cached --name-only
git diff --cached --check
git commit -m "Extract Krita API"
```

Expected: four registered owner delegates, four annotated forwarders, four owner implementations, an exact two-file commit, and preserved maintainer changes.

### Task 4: Extract `ImageHistoryAPI`

**Files:**
- Create: `src/WebAPI/ImageHistoryAPI.cs`
- Modify: `src/WebAPI/T2IAPI.cs:32-42,35-39,509-545,769-1648,1655-1906,1910-2030,2111-2529` before prior extraction shifts
- Reference: `src/BuiltinExtensions/GridGenerator/GridGeneratorExtension.cs:322`

- [ ] **Step 1: Confirm no stage-owned dirty files and identify retained contracts**

Run:

```bash
git diff --quiet -- src/WebAPI/T2IAPI.cs
test ! -e src/WebAPI/ImageHistoryAPI.cs
rg -n '^\s*public static (HashSet<string> HistoryExtensions|string\[\] DeletableFileExtensions|ConcurrentDictionary<string, byte> ImageHistoryIndexWarmups)|^\s*public (enum ImageHistorySortMode|record struct ImageHistoryHelper)' src/WebAPI/T2IAPI.cs
```

Expected: no dirty/missing-file conflict and all five contracts remain in `T2IAPI`.

- [ ] **Step 2: Create the history owner and relocate implementation clusters**

Use `apply_patch` to create `ImageHistoryAPI.cs` with this shell:

```csharp
using FreneticUtilities.FreneticExtensions;
using Newtonsoft.Json.Linq;
using SwarmUI.Accounts;
using SwarmUI.Core;
using SwarmUI.Media;
using SwarmUI.Text2Image;
using SwarmUI.Utils;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using Image = SwarmUI.Utils.Image;

namespace SwarmUI.WebAPI;

[API.APIClass("API routes for saved image history and metadata.")]
public static class ImageHistoryAPI
{
    // Relocated implementation clusters in their original relative order.
}
```

Replace the comment with exact copies of:

1. `AddImageToHistory` with its annotations;
2. private methods `MetadataIsHidden` through `GetListAPIInternal`;
3. private methods `CanIndexHistoryPrefix` through `TryStartImageHistoryIndexWarmup`;
4. public `ListImages`, `RescanImageMetadata`, and `OpenImageFolder` with annotations;
5. private `IsKdeDesktop`, `TrySelectFileInDolphin`, and `TryShowFileInLinuxFileManager`;
6. public `DeleteImage` and `BulkMoveImages` with annotations;
7. public methods `ToggleImageStarred` through `SetImageNotes` with annotations.

Do not copy the five retained public history contracts or any Krita method.

- [ ] **Step 3: Qualify only the retained cross-owner contracts**

Within `ImageHistoryAPI.cs`, use `apply_patch` for these mechanical replacements throughout the moved implementation:

```text
RequestToParams                 -> T2IAPI.RequestToParams
HistoryExtensions               -> T2IAPI.HistoryExtensions
ImageHistorySortMode            -> T2IAPI.ImageHistorySortMode
ImageHistoryHelper              -> T2IAPI.ImageHistoryHelper
ImageHistoryIndexWarmups        -> T2IAPI.ImageHistoryIndexWarmups
DeletableFileExtensions         -> T2IAPI.DeletableFileExtensions
```

Do not qualify local method calls such as `GetListAPIInternal`, `RefreshHistoryIndexForPath`, or `IndexImageHistoryFolder`.

- [ ] **Step 4: Replace eleven public implementations with annotated forwarders**

Retain each original declaration, defaults, `APIDescription`, and `APIParameter` attributes in `T2IAPI`. Replace only method bodies with:

```csharp
return await ImageHistoryAPI.AddImageToHistory(session, image, rawInput);
return await ImageHistoryAPI.ListImages(session, path, depth, sortBy, sortReverse, includeHidden, fastFirst, fastFirstLimit, forceScan, filter);
return await ImageHistoryAPI.RescanImageMetadata(session, path, rebuild);
return await ImageHistoryAPI.OpenImageFolder(session, path);
return await ImageHistoryAPI.DeleteImage(session, path);
return await ImageHistoryAPI.BulkMoveImages(session, paths, targetFolder, mode);
return await ImageHistoryAPI.ToggleImageStarred(session, path);
return await ImageHistoryAPI.ToggleImageHidden(session, path);
return await ImageHistoryAPI.SetImageRating(session, path, rating);
return await ImageHistoryAPI.SetImageTags(session, path, tags, mode);
return await ImageHistoryAPI.SetImageNotes(session, path, notes);
```

Use one matching statement per method; do not combine methods or change async signatures.

- [ ] **Step 5: Remove moved private helpers from `T2IAPI`**

Using `apply_patch`, delete the private implementation clusters listed in Step 2. Retain, in `T2IAPI`, exactly these authoritative declarations:

```csharp
public static HashSet<string> HistoryExtensions = ...;
public enum ImageHistorySortMode { ... }
public record struct ImageHistoryHelper(...);
public static ConcurrentDictionary<string, byte> ImageHistoryIndexWarmups = [];
public static string[] DeletableFileExtensions = ...;
```

Expected: no private history filter/index/file-manager helper remains in `T2IAPI`.

- [ ] **Step 6: Update the eleven history delegates in their current positions**

Qualify only the registered delegate names, preserving every third argument and Boolean flag:

```csharp
ImageHistoryAPI.AddImageToHistory
ImageHistoryAPI.ListImages
ImageHistoryAPI.RescanImageMetadata
ImageHistoryAPI.ToggleImageStarred
ImageHistoryAPI.ToggleImageHidden
ImageHistoryAPI.SetImageRating
ImageHistoryAPI.SetImageTags
ImageHistoryAPI.SetImageNotes
ImageHistoryAPI.OpenImageFolder
ImageHistoryAPI.DeleteImage
ImageHistoryAPI.BulkMoveImages
```

Do not reorder the lines.

- [ ] **Step 7: Verify history ownership and compatibility**

Run:

```bash
rg -n 'API.RegisterAPICall\(ImageHistoryAPI\.' src/WebAPI/T2IAPI.cs
rg -n 'return await ImageHistoryAPI\.' src/WebAPI/T2IAPI.cs
rg -n 'T2IAPI\.(RequestToParams|HistoryExtensions|ImageHistorySortMode|ImageHistoryHelper|ImageHistoryIndexWarmups|DeletableFileExtensions)' src/WebAPI/ImageHistoryAPI.cs
rg -n '^\s*private static .+\(' src/WebAPI/T2IAPI.cs
rg -n 'T2IAPI\.DeleteImage' src/BuiltinExtensions/GridGenerator/GridGeneratorExtension.cs
git diff --check
```

Expected: 11 registered owner delegates; 11 legacy forwarders; every retained contract reference is qualified; no moved history helper remains in `T2IAPI`; Grid Generator is unchanged; no whitespace errors.

- [ ] **Step 8: Commit the image-history extraction**

Run:

```bash
git add -- src/WebAPI/ImageHistoryAPI.cs src/WebAPI/T2IAPI.cs
git diff --cached --name-only
git diff --cached --check
git commit -m "Extract image history API"
```

Expected: exactly the two listed files are committed and maintainer changes remain unstaged.

### Task 5: Audit the Complete Route and Compatibility Surface

**Files:**
- Inspect: `src/WebAPI/T2IAPI.cs`
- Inspect: `src/WebAPI/ClassicInpaintAPI.cs`
- Inspect: `src/WebAPI/KritaAPI.cs`
- Inspect: `src/WebAPI/ImageHistoryAPI.cs`
- Inspect: `src/WebAPI/BackendAPI.cs`
- Inspect: `src/BuiltinExtensions/GridGenerator/GridGeneratorExtension.cs`

- [ ] **Step 1: Verify the complete ordered registration manifest**

Run:

```bash
sed -n '/public static void Register()/,/^    }/p' src/WebAPI/T2IAPI.cs | rg 'API.RegisterAPICall'
```

Expected: the same 21 route names in Task 1 order. The 17 moved routes are qualified with their new owner; generation, parameter listing, and refresh remain unqualified. Every Boolean update flag and permission matches the Task 1 baseline.

- [ ] **Step 2: Verify owner and forwarder counts**

Run:

```bash
rg -c '^\s*public static .+\(' src/WebAPI/ClassicInpaintAPI.cs
rg -c '^\s*public static .+\(' src/WebAPI/KritaAPI.cs
rg -c '^\s*public static .+\(' src/WebAPI/ImageHistoryAPI.cs
rg -c 'return (await )?(ClassicInpaintAPI|KritaAPI|ImageHistoryAPI)\.' src/WebAPI/T2IAPI.cs
```

Expected: counts are 6, 4, 11, and 20 return-based forwarders respectively; `PrepareClassicInpaintMask` is the twenty-first forwarder and is a void call without `return`.

- [ ] **Step 3: Verify route annotations and signatures manually**

For every registered moved route, compare the owner and legacy declarations side by side:

```bash
for name in AddImageToHistory ListImages RescanImageMetadata OpenImageFolder DeleteImage BulkMoveImages ToggleImageStarred ToggleImageHidden SetImageRating SetImageTags SetImageNotes SendImageToKrita ImportKritaImage CheckPendingKritaImage GetActiveKritaSession GetClassicInpaintBackends ClassicInpaint; do
    rg -n -B 12 -A 12 "public static .* ${name}\\(" src/WebAPI/T2IAPI.cs src/WebAPI/ImageHistoryAPI.cs src/WebAPI/KritaAPI.cs src/WebAPI/ClassicInpaintAPI.cs
done
```

Expected: each route has matching names, return types, parameter names/types/order/defaults, `APIDescription`, `APINonfinalMark` if present, and `APIParameter` annotations in both locations.

- [ ] **Step 4: Verify direct callers and singular history state**

Run:

```bash
rg -n 'T2IAPI\.RunProcessCapture' src/WebAPI/BackendAPI.cs
rg -n 'T2IAPI\.DeleteImage' src/BuiltinExtensions/GridGenerator/GridGeneratorExtension.cs
rg -n '^\s*public static (HashSet<string> HistoryExtensions|string\[\] DeletableFileExtensions|ConcurrentDictionary<string, byte> ImageHistoryIndexWarmups)|^\s*public (enum ImageHistorySortMode|record struct ImageHistoryHelper)' src/WebAPI/T2IAPI.cs
rg -n '^\s*public static (HashSet<string> HistoryExtensions|string\[\] DeletableFileExtensions|ConcurrentDictionary<string, byte> ImageHistoryIndexWarmups)|^\s*public (enum ImageHistorySortMode|record struct ImageHistoryHelper)' src/WebAPI/ImageHistoryAPI.cs
```

Expected: original callers remain; five contracts exist in `T2IAPI`; zero are redeclared in `ImageHistoryAPI`.

- [ ] **Step 5: Check final source scope and structure**

Run:

```bash
git diff --check HEAD~3..HEAD
git status --short
git log -4 --oneline --decorate
git show --stat --oneline HEAD~3..HEAD
```

Expected: three implementation commits follow the plan commit; only the four intended Web API source files occur across the implementation range; known maintainer changes remain unstaged. No build or test is run.

### Task 6: Maintainer Live Validation Handoff

**Files:**
- No source modifications expected

- [ ] **Step 1: Ask the maintainer to run the live matrix**

1. Generate over WebSocket and use `AddImageToHistory`.
2. Exercise history pagination, filters, every sort mode, fast-first loading, warmup, and metadata rescan.
3. Star/unstar, hide/unhide, set/clear rating, tags, and notes.
4. Delete, bulk copy/move, open a containing folder, and run Grid Generator cleanup.
5. Send to Krita, import from Krita, poll pending imports, query the active session, and confirm forwarded/non-loopback rejection.
6. Discover classic-inpaint backends; exercise success, disabled IOPaint, unsupported backend, invalid payload, and failed-process cleanup.
7. Confirm permissions, update behavior, errors, and JSON payloads remain unchanged.

Expected: no behavioral regression. If a failure occurs, invoke `superpowers:systematic-debugging` before changing code.

- [ ] **Step 2: Report verification boundaries accurately**

Report static checks and independent reviews separately from the pending manual live result. Do not claim runtime behavior is verified until the maintainer confirms this matrix.
