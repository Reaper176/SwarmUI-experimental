# Classic Inpaint ZITS Support Gating Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Expose `ZITS` in SwarmUI classic inpaint only when the installed IOPaint CLI actually supports it, and remove it otherwise.

**Architecture:** Add a small server-side capability check for classic inpaint backends, make the server the authority for which backends are accepted, and feed that capability into the image editor backend dropdown. Keep `LaMa` and `MAT` unchanged and avoid adding any non-IOPaint-specific `ZITS` execution path.

**Tech Stack:** C# 12, ASP.NET API routes, external IOPaint CLI probing, browser-side JavaScript in the image editor

---

### Task 1: Add Server-Side Classic Inpaint Backend Capability Detection

**Files:**
- Modify: `src/WebAPI/T2IAPI.cs`
- Test: manual rebuild and server log inspection

- [ ] **Step 1: Add a helper that reports supported classic inpaint backends**

In `src/WebAPI/T2IAPI.cs`, add a helper near the existing IOPaint helpers that returns a `HashSet<string>` of supported backends. The helper should always start with `lama` and `mat`, then conditionally add `zits` if the installed IOPaint CLI reports support for it.

Use a lightweight probe command shape such as:

```csharp
public static async Task<HashSet<string>> GetSupportedClassicInpaintBackends()
{
    HashSet<string> supported = ["lama", "mat"];
    foreach (string candidate in GetIOPaintCommandCandidates())
    {
        bool isDirectIopaint = Path.GetFileName(candidate).ToLowerInvariant().StartsWith("iopaint");
        string[] args = isDirectIopaint ? ["run", "--help"] : ["-m", "iopaint", "run", "--help"];
        try
        {
            (int exitCode, string outputText) = await RunProcessCapture(candidate, args);
            if (exitCode == 0 && outputText.ToLowerInvariant().Contains("zits"))
            {
                supported.Add("zits");
                return supported;
            }
        }
        catch (Exception)
        {
        }
    }
    return supported;
}
```

- [ ] **Step 2: Add a focused capability diagnostic log**

When `zits` support is detected or not detected, add an info log so runtime behavior is visible:

```csharp
Logs.Info($"ClassicInpaint backend probe resolved support: {string.Join(", ", supported)}");
```

- [ ] **Step 3: Rebuild and verify the server can probe support**

Run your normal Swarm rebuild/restart flow.

Manual check:
- Start the server.
- Trigger any path that causes classic inpaint backend evaluation, or inspect logs after startup if the helper is invoked then.

Expected:
- The server log shows whether `zits` is supported by the active IOPaint installation.

- [ ] **Step 4: Commit the backend capability helper**

```bash
git add src/WebAPI/T2IAPI.cs
git commit -m "feat: detect supported classic inpaint backends"
```

### Task 2: Make ClassicInpaint Accept Only Supported Backends

**Files:**
- Modify: `src/WebAPI/T2IAPI.cs`
- Test: manual request verification through the image editor

- [ ] **Step 1: Replace the hardcoded backend allowlist with the detected capability set**

Inside `ClassicInpaint(...)`, replace:

```csharp
if (backend != "lama" && backend != "mat" && backend != "zits")
{
    return new JObject() { ["error"] = $"Unsupported Classic Inpaint backend '{backend}'." };
}
if (backend == "zits")
{
    return new JObject() { ["error"] = "ZITS is listed for future support but is not available in the local IOPaint bridge yet." };
}
```

with:

```csharp
HashSet<string> supportedBackends = await GetSupportedClassicInpaintBackends();
if (!supportedBackends.Contains(backend))
{
    return new JObject() { ["error"] = $"Classic Inpaint backend '{backend}' is not supported by the installed IOPaint version. Supported backends: {supportedBackends.OrderBy(x => x).JoinString(", ")}" };
}
```

- [ ] **Step 2: Preserve existing `LaMa` and `MAT` behavior**

Do not change the existing command generation logic:

```csharp
commands.Add(["run", "--model", backend, "--device", device, "--image", imagePath, "--mask", maskPath, "--output", outputDir]);
commands.Add(["run", "--model", backend, "--device", device, "--input", imagePath, "--mask", maskPath, "--output", outputDir]);
```

Expected:
- `LaMa` and `MAT` keep using the current flow.
- `ZITS` is now controlled by capability detection rather than a hardcoded block.

- [ ] **Step 3: Rebuild and verify unsupported backends return a readable JSON error**

Run your normal Swarm rebuild/restart flow.

Manual check:
- If `zits` is unsupported, force a request for backend `zits` by temporarily selecting it through dev tools or any direct API call path you use for debugging.

Expected:
- The server returns a readable error listing the supported backends instead of a “future support” placeholder.

- [ ] **Step 4: Commit the server-side gating**

```bash
git add src/WebAPI/T2IAPI.cs
git commit -m "fix: gate classic inpaint backends by iopaint support"
```

### Task 3: Feed Supported Backends Into the Image Editor UI

**Files:**
- Modify: `src/WebAPI/T2IAPI.cs`
- Modify: `src/wwwroot/js/genpage/helpers/image_editor_tools.js`
- Test: manual image editor verification

- [ ] **Step 1: Add a lightweight API route or route field for classic inpaint backend availability**

Extend an existing route or add a small API route in `T2IAPI.cs` that returns the supported classic inpaint backend list to the frontend.

Example shape:

```csharp
public static async Task<JObject> GetClassicInpaintBackends(Session session)
{
    HashSet<string> supportedBackends = await GetSupportedClassicInpaintBackends();
    return new JObject()
    {
        ["backends"] = new JArray(supportedBackends.OrderBy(x => x).ToArray())
    };
}
```

Register it in `Register()` with existing image-generation-adjacent permissions.

- [ ] **Step 2: Replace the hardcoded ZITS placeholder in the brush config**

In `src/wwwroot/js/genpage/helpers/image_editor_tools.js`, replace the hardcoded select contents:

```html
<option value="lama">LaMa</option>
<option value="mat">MAT</option>
<option value="zits" disabled>ZITS (Unavailable)</option>
```

with a base empty select:

```html
<select class="auto-dropdown id-classic-inpaint-backend" style="flex-grow: 1;"></select>
```

- [ ] **Step 3: Populate the backend selector from the server-reported list**

In the brush tool setup code, after `this.classicInpaintBackendSelector` is assigned, fetch the supported backend list and render options:

```javascript
genericRequest('GetClassicInpaintBackends', {}, (data) => {
    let backends = data.backends || ['lama', 'mat'];
    this.classicInpaintBackendSelector.innerHTML = backends.map((backend) => {
        let label = backend == 'lama' ? 'LaMa' : backend.toUpperCase();
        return `<option value="${backend}">${label}</option>`;
    }).join('');
    if (!backends.includes(this.classicInpaintBackend)) {
        this.classicInpaintBackend = backends[0] || 'lama';
    }
    this.syncBrushConfigInputs();
}, 0, () => {
    this.classicInpaintBackendSelector.innerHTML = '<option value="lama">LaMa</option><option value="mat">MAT</option>';
    this.syncBrushConfigInputs();
});
```

- [ ] **Step 4: Remove the hardcoded frontend `zits` guard**

Delete the early return in `runClassicInpaint()`:

```javascript
if (this.classicInpaintBackend == 'zits') {
    this.clearBrushState();
    showError('ZITS is listed for future support but is not available yet.');
    return;
}
```

Expected:
- The frontend no longer has its own stale `zits` support assumption.
- The server remains the authority.

- [ ] **Step 5: Rebuild and verify the dropdown matches actual support**

Run your normal Swarm rebuild/restart flow.

Manual check:
- Open the image editor classic inpaint backend dropdown.
- If `zits` is unsupported, confirm it is absent.
- If `zits` is supported, confirm it appears as a normal option.

Expected:
- No disabled `ZITS (Unavailable)` placeholder remains.

- [ ] **Step 6: Commit the UI gating**

```bash
git add src/WebAPI/T2IAPI.cs src/wwwroot/js/genpage/helpers/image_editor_tools.js
git commit -m "feat: show only supported classic inpaint backends"
```

### Task 4: Final Verification of ZITS Support Gating

**Files:**
- Inspect: `swarmui.log`
- Inspect: `src/WebAPI/T2IAPI.cs`
- Inspect: `src/wwwroot/js/genpage/helpers/image_editor_tools.js`
- Test: manual runtime verification

- [ ] **Step 1: Verify unsupported-ZITS behavior end to end**

Manual check for an install without `zits` support:

```text
1. Open classic inpaint backend dropdown.
2. Confirm ZITS is absent.
3. Confirm LaMa still works.
4. Confirm MAT still appears and behaves as before.
```

Expected:
- Unsupported `zits` is not shown anywhere in the editor UI.

- [ ] **Step 2: Verify supported-ZITS behavior if available in the active environment**

Manual check for an install with `zits` support:

```text
1. Open classic inpaint backend dropdown.
2. Confirm ZITS is present.
3. Select ZITS and run classic inpaint once.
4. Inspect the returned result or readable server-side error.
```

Expected:
- `ZITS` behaves like a normal supported backend option.

- [ ] **Step 3: Confirm logs show the capability result**

Inspect `swarmui.log` for the capability probe line:

```text
ClassicInpaint backend probe resolved support: ...
```

Expected:
- The log reflects the same backend list the UI is using.

- [ ] **Step 4: Commit only if any final small verification-driven tweak was required**

If no further code changes were needed, do not create a new commit.

If a tiny follow-up tweak was required during verification:

```bash
git add src/WebAPI/T2IAPI.cs src/wwwroot/js/genpage/helpers/image_editor_tools.js
git commit -m "chore: refine classic inpaint backend support gating"
```
