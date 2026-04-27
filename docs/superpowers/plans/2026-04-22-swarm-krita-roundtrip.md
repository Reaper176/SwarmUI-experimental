# SwarmUI Krita Round-Trip Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add a local-only image round-trip between SwarmUI and Krita: send the current Swarm image to Krita, then send a flattened Krita image back into the active Swarm init/editor image.

**Architecture:** Keep v1 image-only. SwarmUI adds one local launch API plus a session-targeted pending-import API flow, then wires a `Send to Krita` UI action into the existing current-image flow. A small bundled Krita plugin flattens the current document and POSTs a PNG back to SwarmUI on loopback, while the browser polls for pending Krita returns and applies them to the active init/editor state.

**Tech Stack:** C# 12/.NET 8, existing SwarmUI API framework, existing SwarmUI frontend JS, Krita Python plugin API, PNG file handoff, HTTP POST upload.

---

## File Structure

### Existing files to modify

- `src/WebAPI/T2IAPI.cs`
  - Register the new Krita-related API calls.
  - Implement local export-and-launch behavior.
  - Implement Krita image upload/import behavior and session-targeted pending import retrieval.
- `src/Accounts/Permissions.cs`
  - Add a dedicated permission for local Krita launch control if the existing local-folder permission is too broad.
- `src/Core/Settings.cs`
  - Add server settings for Krita executable path and temp export folder behavior.
- `src/Data/Settings.fds`
  - Mirror any new server settings with comments and defaults.
- `src/wwwroot/js/genpage/gentab/currentimagehandler.js`
  - Add the `Send to Krita` action to the current-image action list.
  - Add the JS helper that calls the new launch API.
- `src/wwwroot/js/genpage/main.js`
  - Add a helper that imports a returned Krita image into the current init/editor state using existing image-editor/init-image plumbing.
  - Add lightweight polling for pending Krita imports for the current session.

### New files to create

- `src/Utils/KritaImageBridge.cs`
  - Contain the small focused server-side helper logic for temp file export path generation, launch argument building, image decode/validation, and pending-import storage helpers.
- `tools/krita_plugin/swarm_krita_bridge.desktop`
  - Krita plugin metadata file.
- `tools/krita_plugin/swarm_krita_bridge/__init__.py`
  - Krita plugin registration entrypoint.
- `tools/krita_plugin/swarm_krita_bridge/swarm_krita_bridge.py`
  - Krita action implementation and minimal settings dialog behavior.
- `docs/Krita Integration.md`
  - User setup instructions for configuring Krita path in Swarm and installing the Krita plugin.

## Constraints To Preserve

- This repo does not use automated tests for this type of feature. Do not add test harnesses or mock-heavy coverage.
- Do not touch `src/Extensions/`; this work is for core SwarmUI plus a bundled external plugin source folder.
- Keep v1 image-only. No metadata sync, no layer sync, no AI Diffusion state integration.
- Keep local-only assumptions explicit. Do not broaden remote-control behavior.

### Task 1: Add server settings and shared helper

**Files:**
- Create: `src/Utils/KritaImageBridge.cs`
- Modify: `src/Core/Settings.cs`
- Modify: `src/Data/Settings.fds`

- [ ] **Step 1: Add server settings for Krita integration**

Add a new settings block under `ServerSettingsData` in `src/Core/Settings.cs` for explicit local configuration:

```csharp
    /// <summary>Settings related to local Krita integration.</summary>
    public class KritaBridgeData : AutoConfiguration
    {
        [ConfigComment("Optional full path to the Krita executable for local launch integration.\nIf empty, SwarmUI will attempt simple OS-default executable names.\nDefaults to empty.")]
        public string KritaExecutablePath = "";

        [ConfigComment("Relative path under the Data directory for temporary Swarm-to-Krita image exports.\nDefaults to 'Temp/KritaBridge'.")]
        public string TempPath = "Temp/KritaBridge";
    }

    [ConfigComment("Settings related to the local Krita round-trip integration.")]
    public KritaBridgeData KritaBridge = new();
```

Mirror the setting defaults and comments in `src/Data/Settings.fds` in the same style as the rest of the file:

```fds
#Settings related to local Krita round-trip integration.
KritaBridge:
	#Optional full path to the Krita executable for local launch integration.
	#If empty, SwarmUI will attempt simple OS-default executable names.
	#Defaults to empty.
	KritaExecutablePath: \x
	#Relative path under the Data directory for temporary Swarm-to-Krita image exports.
	#Defaults to 'Temp/KritaBridge'.
	TempPath: Temp/KritaBridge
```

- [ ] **Step 2: Add a focused Krita helper utility**

Create `src/Utils/KritaImageBridge.cs` with utility methods for temp file creation, executable resolution, launch construction, and PNG decode/validation:

```csharp
using FreneticUtilities.FreneticExtensions;
using Newtonsoft.Json.Linq;
using SwarmUI.Core;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace SwarmUI.Utils;

/// <summary>Helpers for the local SwarmUI/Krita image bridge.</summary>
public static class KritaImageBridge
{
    /// <summary>Gets the directory used for temporary Krita bridge image exports.</summary>
    public static string GetTempDirectory()
    {
        string relative = Program.ServerSettings.KritaBridge.TempPath;
        string baseDir = Utilities.CombinePathWithAbsolute(Environment.CurrentDirectory, "Data");
        string full = Utilities.CombinePathWithAbsolute(baseDir, relative);
        Directory.CreateDirectory(full);
        return full;
    }

    /// <summary>Gets a new temp PNG path for Swarm-to-Krita export.</summary>
    public static string CreateTempPngPath()
    {
        string stamp = $"{DateTimeOffset.UtcNow:yyyyMMdd-HHmmssfff}-{Guid.NewGuid().ToString()[..8]}";
        return Path.Combine(GetTempDirectory(), $"swarm-krita-{stamp}.png");
    }

    /// <summary>Resolves the Krita executable path for the current OS.</summary>
    public static string ResolveKritaExecutable()
    {
        string configured = Program.ServerSettings.KritaBridge.KritaExecutablePath.Trim();
        if (!string.IsNullOrWhiteSpace(configured))
        {
            return configured;
        }
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return "krita.exe";
        }
        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            return "/Applications/Krita.app/Contents/MacOS/krita";
        }
        return "krita";
    }

    /// <summary>Starts Krita with the given local image path.</summary>
    public static void LaunchKrita(string imagePath)
    {
        string executable = ResolveKritaExecutable();
        ProcessStartInfo start = new(executable, $"\"{Path.GetFullPath(imagePath)}\"");
        start.UseShellExecute = true;
        Process.Start(start);
    }

    /// <summary>Returns a compact API error object.</summary>
    public static JObject Error(string message)
    {
        return new JObject() { ["error"] = message };
    }
}
```

- [ ] **Step 3: Static review this helper task**

Run:

```bash
git diff -- src/Core/Settings.cs src/Data/Settings.fds src/Utils/KritaImageBridge.cs
```

Expected:

- New `KritaBridge` settings block exists in both config files.
- New helper file contains only local bridge responsibilities.

- [ ] **Step 4: Commit**

```bash
git add src/Core/Settings.cs src/Data/Settings.fds src/Utils/KritaImageBridge.cs
git commit -m "Add Krita bridge settings and helper"
```

### Task 2: Add SwarmUI APIs for launch and Krita return

**Files:**
- Modify: `src/WebAPI/T2IAPI.cs`
- Modify: `src/Accounts/Permissions.cs`
- Create: `src/Utils/KritaImageBridge.cs`

- [ ] **Step 1: Add a dedicated permission for local Krita launch**

In `src/Accounts/Permissions.cs`, add a permission near `LocalImageFolder`:

```csharp
    public static PermInfo LocalKritaBridge = Register(new("local_krita_bridge", "Local Krita Bridge", "Allows access to the local-only Krita launch and return bridge. Only functions if you're on the same PC as the server.", PermissionDefault.NOBODY, GroupSpecial, PermSafetyLevel.POWERFUL));
```

Use this permission for the launch API. The import API should use a safer existing generate-tab permission unless a stricter local check is implemented in the handler.

- [ ] **Step 2: Register the new API calls**

In `T2IAPI.Register()`, add registrations alongside other T2I-adjacent routes:

```csharp
        API.RegisterAPICall(SendImageToKrita, true, Permissions.LocalKritaBridge);
        API.RegisterAPICall(ImportKritaImage, true, Permissions.FundamentalGenerateTabAccess);
        API.RegisterAPICall(CheckPendingKritaImage, false, Permissions.FundamentalGenerateTabAccess);
```

- [ ] **Step 3: Implement Swarm-to-Krita export and launch**

Add an API method in `src/WebAPI/T2IAPI.cs` that accepts a data URL image, decodes it, writes a temp PNG via `KritaImageBridge.CreateTempPngPath()`, then launches Krita:

```csharp
    [API.APIDescription("Export the current Swarm image to a temporary PNG and open it in the local Krita application.", "\"success\": true")]
    public static async Task<JObject> SendImageToKrita(Session session,
        [API.APIParameter("The PNG-or-dataURL image content to export to Krita.")] string imageData)
    {
        if (string.IsNullOrWhiteSpace(imageData))
        {
            return KritaImageBridge.Error("No image was provided.");
        }
        try
        {
            Image image = new(imageData);
            string path = KritaImageBridge.CreateTempPngPath();
            await File.WriteAllBytesAsync(path, image.AsPngBytes());
            KritaImageBridge.LaunchKrita(path);
            return new JObject() { ["success"] = true, ["path"] = path };
        }
        catch (Exception ex)
        {
            Logs.Error($"Failed to export image to Krita: {ex.ReadableString()}");
            return KritaImageBridge.Error("Failed to open Krita with the exported image.");
        }
    }
```

- [ ] **Step 4: Implement Krita-to-Swarm import route**

Add an API method in `src/WebAPI/T2IAPI.cs` that accepts a base64 PNG string from the Krita plugin, validates it, and stores it as a pending per-session import without writing to history:

```csharp
    [API.APIDescription("Accept a flattened Krita image and store it as a pending import for the target Swarm session.", "\"success\": true")]
    public static async Task<JObject> ImportKritaImage(Session session,
        [API.APIParameter("Base64-encoded PNG bytes from the Krita bridge plugin.")] string imageBase64,
        [API.APIParameter("The session ID that should receive the returned Krita image.")] string targetSession)
    {
        if (string.IsNullOrWhiteSpace(imageBase64))
        {
            return KritaImageBridge.Error("No image payload was provided.");
        }
        if (string.IsNullOrWhiteSpace(targetSession))
        {
            return KritaImageBridge.Error("No target session was provided.");
        }
        try
        {
            byte[] bytes = Convert.FromBase64String(imageBase64);
            string dataUrl = $"data:image/png;base64,{Convert.ToBase64String(bytes)}";
            Image image = new(dataUrl);
            KritaImageBridge.StorePendingImport(targetSession, image.AsDataString());
            return new JObject() { ["success"] = true };
        }
        catch (Exception ex)
        {
            Logs.Error($"Failed to import Krita image: {ex.ReadableString()}");
            return KritaImageBridge.Error("Failed to import the Krita image.");
        }
    }
```

If the API framework already supports large string parameters cleanly, keep this base64 form for v1. Do not add multipart parsing or a second transport path in this task.

- [ ] **Step 5: Implement pending-import retrieval**

Add a read API in `src/WebAPI/T2IAPI.cs` that lets the browser poll for a pending Krita return for the current Swarm session:

```csharp
    [API.APIDescription("Check whether the current Swarm session has a pending Krita image import waiting to be applied.", "\"success\": true, \"image\": \"data:image/png;base64,...\"")]
    public static async Task<JObject> CheckPendingKritaImage(Session session)
    {
        string image = KritaImageBridge.TakePendingImport(session.ID);
        JObject result = new() { ["success"] = true };
        if (image is not null)
        {
            result["image"] = image;
        }
        return result;
    }
```

- [ ] **Step 6: Static review this API task**

Run:

```bash
git diff -- src/Accounts/Permissions.cs src/WebAPI/T2IAPI.cs src/Utils/KritaImageBridge.cs
```

Expected:

- The new permission is scoped to local Krita bridge behavior.
- The launch route only exports and launches.
- The import route only validates and returns a Swarm-ready image payload.

- [ ] **Step 7: Commit**

```bash
git add src/Accounts/Permissions.cs src/WebAPI/T2IAPI.cs src/Utils/KritaImageBridge.cs
git commit -m "Add Krita bridge API routes"
```

### Task 3: Wire the SwarmUI frontend action and pending-import poller

**Files:**
- Modify: `src/wwwroot/js/genpage/gentab/currentimagehandler.js`
- Modify: `src/wwwroot/js/genpage/main.js`

- [ ] **Step 1: Add a JS helper to send the current image to Krita**

In `src/wwwroot/js/genpage/gentab/currentimagehandler.js`, add a helper near the current-image action helpers:

```javascript
/**
 * Sends the specified image URL to the local Krita bridge.
 */
function sendImageToKrita(imgSrc) {
    toDataURL(imgSrc, (dataUrl) => {
        genericRequest('SendImageToKrita', { imageData: dataUrl }, data => {
            if (!data.success) {
                showError(data.error || 'Failed to send image to Krita.');
            }
        }, 0, error => {
            showError(`${error}`);
        });
    });
}
```

- [ ] **Step 2: Add the `Send to Krita` button to the current image action list**

In the same file, add the button alongside the existing image-only actions, near `Edit Image` and `Send To Image Edit Tab`:

```javascript
    includeButton('Send to Krita', () => {
        sendImageToKrita(img.src);
    }, '', 'Exports this image to a temporary PNG and opens it in Krita', ['image']);
```

Keep the label exactly `Send to Krita` so it matches the approved UX wording.

- [ ] **Step 3: Add a helper to import a Krita-returned image into the active editor state**

In `src/wwwroot/js/genpage/main.js`, add a helper near the image-editor bootstrapping helpers:

```javascript
/**
 * Imports a returned Krita image into the active init/editor image state.
 */
async function importReturnedKritaImage(imageData) {
    let image = new Image();
    image.onload = async () => {
        let initImageGroupToggle = document.getElementById('input_group_content_initimage_toggle');
        if (initImageGroupToggle) {
            initImageGroupToggle.checked = true;
            triggerChangeFor(initImageGroupToggle);
        }
        setCurrentImage(imageData, '', 'krita');
        if (await ensureGenerateImageEditorReady()) {
            imageEditor.clearVars();
            imageEditor.setBaseImage(image);
        }
    };
    image.src = imageData;
}
```

This helper is intentionally one-way and image-only. Do not add metadata reconstruction or history writes here.

- [ ] **Step 4: Add a pending-import poller**

Also in `src/wwwroot/js/genpage/main.js`, add a lightweight poller for the current Swarm session:

```javascript
/**
 * Checks whether Swarm has a pending Krita image for this session.
 */
let hasPendingKritaPoll = false;

function startPendingKritaImportPoll() {
    if (hasPendingKritaPoll) {
        return;
    }
    hasPendingKritaPoll = true;
    setInterval(() => {
        genericRequest('CheckPendingKritaImage', {}, data => {
            if (data && data.image) {
                importReturnedKritaImage(data.image);
            }
        }, 0, () => { });
    }, 2000);
}
```

Call `startPendingKritaImportPoll()` from an existing main-page startup path after session initialization. Keep the error handler quiet so transient polling failures do not spam the UI.

- [ ] **Step 5: Static review this frontend task**

Run:

```bash
git diff -- src/wwwroot/js/genpage/gentab/currentimagehandler.js src/wwwroot/js/genpage/main.js
```

Expected:

- The new button only appears for images.
- The new import helper reuses existing init/editor state instead of creating a history entry.

- [ ] **Step 6: Commit**

```bash
git add src/wwwroot/js/genpage/gentab/currentimagehandler.js src/wwwroot/js/genpage/main.js
git commit -m "Wire SwarmUI Krita bridge actions"
```

### Task 4: Bundle the Krita plugin source

**Files:**
- Create: `tools/krita_plugin/swarm_krita_bridge.desktop`
- Create: `tools/krita_plugin/swarm_krita_bridge/__init__.py`
- Create: `tools/krita_plugin/swarm_krita_bridge/swarm_krita_bridge.py`

- [ ] **Step 1: Add Krita plugin metadata**

Create `tools/krita_plugin/swarm_krita_bridge.desktop`:

```ini
[Desktop Entry]
Type=Service
ServiceTypes=Krita/PythonPlugin
X-KDE-Library=swarm_krita_bridge
X-Python-2-Compatible=false
X-Krita-Manual=Manual.html
Name=Swarm Krita Bridge
Comment=Send flattened Krita images back to SwarmUI
```

- [ ] **Step 2: Add the Krita plugin package entrypoint**

Create `tools/krita_plugin/swarm_krita_bridge/__init__.py`:

```python
from .swarm_krita_bridge import SwarmKritaBridge

app = Krita.instance()
extension = SwarmKritaBridge(parent=app)
app.addExtension(extension)
```

- [ ] **Step 3: Implement the Krita action**

Create `tools/krita_plugin/swarm_krita_bridge/swarm_krita_bridge.py`:

```python
from krita import *
from PyQt5.QtCore import QBuffer, QByteArray, QIODevice
from PyQt5.QtWidgets import QInputDialog, QMessageBox
from urllib import request
import base64
import json


class SwarmKritaBridge(Extension):
    def __init__(self, parent):
        super().__init__(parent)
        self.swarm_url = "http://127.0.0.1:7801/API/ImportKritaImage"
        self.target_session = ""

    def setup(self):
        pass

    def createActions(self, window):
        action = window.createAction("send_to_swarm", "Send to Swarm", "tools/scripts")
        action.triggered.connect(self.send_to_swarm)

    def send_to_swarm(self):
        window = Krita.instance().activeWindow()
        if window is None or window.activeView() is None:
            QMessageBox.warning(None, "Swarm Krita Bridge", "No active Krita document is open.")
            return
        document = window.activeView().document()
        if document is None:
            QMessageBox.warning(None, "Swarm Krita Bridge", "No active Krita document is open.")
            return
        merged = document.projection()
        byte_array = QByteArray()
        buffer = QBuffer(byte_array)
        buffer.open(QIODevice.WriteOnly)
        merged.save(buffer, "PNG")
        if not self.target_session:
            session_id, ok = QInputDialog.getText(None, "Swarm Krita Bridge", "Swarm session ID")
            if not ok or not session_id:
                return
            self.target_session = session_id
        payload = json.dumps({
            "imageBase64": base64.b64encode(bytes(byte_array)).decode("ascii"),
            "targetSession": self.target_session
        }).encode("utf-8")
        req = request.Request(self.swarm_url, data=payload, headers={"Content-Type": "application/json"})
        try:
            with request.urlopen(req) as response:
                data = json.loads(response.read().decode("utf-8"))
        except Exception as ex:
            QMessageBox.critical(None, "Swarm Krita Bridge", f"Failed to reach SwarmUI: {ex}")
            return
        if not data.get("success"):
            QMessageBox.critical(None, "Swarm Krita Bridge", data.get("error", "SwarmUI rejected the image."))
            return
        QMessageBox.information(None, "Swarm Krita Bridge", "Image sent to SwarmUI.")
```

Keep the plugin minimal in v1. Do not add settings persistence, metadata transfer, or background sync here. For v1, it is acceptable to prompt once per Krita session for the target Swarm session ID.

- [ ] **Step 4: Static review this plugin task**

Run:

```bash
git diff -- tools/krita_plugin/swarm_krita_bridge.desktop tools/krita_plugin/swarm_krita_bridge/__init__.py tools/krita_plugin/swarm_krita_bridge/swarm_krita_bridge.py
```

Expected:

- The plugin exposes a single `Send to Swarm` action.
- The plugin flattens the document and sends only PNG bytes encoded as base64 JSON.

- [ ] **Step 5: Commit**

```bash
git add tools/krita_plugin/swarm_krita_bridge.desktop tools/krita_plugin/swarm_krita_bridge/__init__.py tools/krita_plugin/swarm_krita_bridge/swarm_krita_bridge.py
git commit -m "Add Krita bridge plugin source"
```

### Task 5: Document setup and manual verification flow

**Files:**
- Create: `docs/Krita Integration.md`

- [ ] **Step 1: Add user-facing setup documentation**

Create `docs/Krita Integration.md` covering:

```md
# Krita Integration

## What v1 does

- `Send to Krita` exports the current Swarm image and opens it in Krita.
- `Send to Swarm` in Krita flattens the current document and sends it back to Swarm.

## Limits

- Image only
- Local machine only
- No layer sync
- No metadata sync

## Swarm setup

Set `KritaBridge.KritaExecutablePath` in server settings if Krita is not on the default OS path.

## Krita plugin install

Copy the contents of `tools/krita_plugin/` into Krita's Python plugin directory, then enable `Swarm Krita Bridge` in Krita's plugin manager.

## Manual verification

1. Open an image in SwarmUI.
2. Click `Send to Krita`.
3. Confirm Krita opens with the exported image.
4. Edit the image in Krita.
5. Copy the Swarm browser session ID into the Krita plugin when prompted.
6. Click `Send to Swarm`.
7. Confirm the active Swarm init/editor image updates within a few seconds.
```

- [ ] **Step 2: Static review docs**

Run:

```bash
git diff -- docs/Krita\ Integration.md
```

Expected:

- The doc explains setup, limits, and the exact v1 manual verification flow.

- [ ] **Step 3: Commit**

```bash
git add docs/Krita\ Integration.md
git commit -m "Document Krita bridge setup"
```

## Plan Self-Review

- Spec coverage:
  - Swarm button to Krita: covered by Task 3 plus Task 2 launch route.
  - Krita button back to Swarm: covered by Task 4 plus Task 2 import route.
  - Local-only launch and error handling: covered by Tasks 1 and 2.
  - Setup instructions: covered by Task 5.
- Placeholder scan:
  - No `TODO`, `TBD`, or deferred implementation steps are left inside the task list.
- Type consistency:
  - API names are consistent across the plan: `SendImageToKrita`, `ImportKritaImage`, `sendImageToKrita`, `importReturnedKritaImage`.
