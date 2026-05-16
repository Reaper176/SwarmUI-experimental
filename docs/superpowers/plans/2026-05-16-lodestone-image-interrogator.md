# Lodestone Image Interrogator Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build a self-contained SwarmUI extension that installs and runs the Lodestone tagger locally, exposes a dedicated Image Interrogator panel, and moves images/tags between that panel and the Generate tab.

**Architecture:** The extension owns its UI, API routes, setup state, Python runner, and downloaded model files. C# handles Swarm integration, permissions, setup/process orchestration, and API responses; Python handles model inference; JavaScript handles the panel workflow and Generate-tab bridge.

**Tech Stack:** SwarmUI extension C# on .NET 8, Newtonsoft JSON, Swarm `API.RegisterAPICall`, extension `Tabs/Text2Image`, browser JavaScript with Swarm `genericRequest`, Python 3 with `torch`, `torchvision`, `safetensors`, `Pillow`, and model files from Hugging Face.

---

## File Structure

- Create `src/Extensions/LodestoneImageInterrogator/LodestoneImageInterrogator.cs`: extension entry point, metadata, asset registration, API registration, shutdown cleanup.
- Create `src/Extensions/LodestoneImageInterrogator/LodestoneImageInterrogator.csproj`: standard Swarm extension project file.
- Create `src/Extensions/LodestoneImageInterrogator/WebAPI/LodestoneInterrogatorAPI.cs`: API route class and permission group.
- Create `src/Extensions/LodestoneImageInterrogator/src/LodestoneSetupManager.cs`: setup paths, status, dependency/model download orchestration, duplicate setup guard.
- Create `src/Extensions/LodestoneImageInterrogator/src/LodestoneRunner.cs`: C# bridge for invoking Python runner and parsing JSON results.
- Create `src/Extensions/LodestoneImageInterrogator/src/LodestoneModels.cs`: small DTOs for setup status, tag results, and API formatting.
- Create `src/Extensions/LodestoneImageInterrogator/Runner/requirements.txt`: Python dependencies.
- Create `src/Extensions/LodestoneImageInterrogator/Runner/lodestone_interrogate.py`: Python single-image inference CLI.
- Create `src/Extensions/LodestoneImageInterrogator/Tabs/Text2Image/Image Interrogator.html`: extension panel markup.
- Create `src/Extensions/LodestoneImageInterrogator/Assets/lodestone_interrogator.js`: panel logic, Generate-tab bridge, prompt actions.
- Create `src/Extensions/LodestoneImageInterrogator/Assets/lodestone_interrogator.css`: panel styles scoped by `.lodestone-interrogator`.
- Create `src/Extensions/LodestoneImageInterrogator/README.md`: install/setup/privacy notes.
- Create `src/Extensions/LodestoneImageInterrogator/LICENSE`: MIT license.

Agents must not run builds or automated tests in this repo. Use static inspection commands only, then leave live verification to the developer.

## Task 1: Extension Skeleton And Tab

**Files:**
- Create: `src/Extensions/LodestoneImageInterrogator/LodestoneImageInterrogator.csproj`
- Create: `src/Extensions/LodestoneImageInterrogator/LodestoneImageInterrogator.cs`
- Create: `src/Extensions/LodestoneImageInterrogator/Tabs/Text2Image/Image Interrogator.html`
- Create: `src/Extensions/LodestoneImageInterrogator/Assets/lodestone_interrogator.js`
- Create: `src/Extensions/LodestoneImageInterrogator/Assets/lodestone_interrogator.css`

- [ ] **Step 1: Create the extension project file**

Create `src/Extensions/LodestoneImageInterrogator/LodestoneImageInterrogator.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk.Web">
    <PropertyGroup>
        <AssemblyName>LodestoneImageInterrogator</AssemblyName>
    </PropertyGroup>
    <Import Project="../../SwarmUI.extension.props" />
</Project>
```

- [ ] **Step 2: Create the extension entry point**

Create `src/Extensions/LodestoneImageInterrogator/LodestoneImageInterrogator.cs`:

```csharp
using LodestoneImageInterrogatorExtension.WebAPI;
using SwarmUI.Core;
using SwarmUI.Utils;

namespace LodestoneImageInterrogatorExtension;

public class LodestoneImageInterrogator : Extension
{
    public override void OnPreInit()
    {
        ScriptFiles.Add("Assets/lodestone_interrogator.js");
        StyleSheetFiles.Add("Assets/lodestone_interrogator.css");
    }

    public override void OnInit()
    {
        Logs.Info("Lodestone Image Interrogator extension initializing.");
        LodestoneInterrogatorAPI.Register();
    }
}
```

- [ ] **Step 3: Create initial tab markup**

Create `src/Extensions/LodestoneImageInterrogator/Tabs/Text2Image/Image Interrogator.html`:

```html
<div class="lodestone-interrogator" id="lodestone_interrogator_panel">
    <div class="lodestone-interrogator-status">
        <div>
            <h2 class="translate">Lodestone Image Interrogator</h2>
            <div id="lodestone_interrogator_status_text" class="translate">Checking setup status...</div>
        </div>
        <button type="button" class="basic-button translate" id="lodestone_interrogator_setup_button">Setup</button>
    </div>
    <div class="lodestone-interrogator-main">
        <div class="lodestone-interrogator-image">
            <div class="lodestone-interrogator-preview" id="lodestone_interrogator_preview">No image selected</div>
            <input type="file" id="lodestone_interrogator_file" accept="image/*">
            <button type="button" class="basic-button translate" id="lodestone_interrogator_run_button">Interrogate</button>
        </div>
        <div class="lodestone-interrogator-controls">
            <label class="translate" for="lodestone_interrogator_threshold">Threshold</label>
            <input type="range" id="lodestone_interrogator_threshold" min="0.05" max="0.95" step="0.01" value="0.35">
            <label class="translate" for="lodestone_interrogator_max_tags">Max Tags</label>
            <input type="number" id="lodestone_interrogator_max_tags" min="1" max="300" value="80">
            <label><input type="checkbox" id="lodestone_interrogator_include_rating" checked> Include rating tags</label>
        </div>
        <div class="lodestone-interrogator-results">
            <textarea id="lodestone_interrogator_prompt" class="auto-text-block" rows="8" placeholder="Tags will appear here..."></textarea>
            <div class="lodestone-interrogator-actions">
                <button type="button" class="basic-button translate" id="lodestone_interrogator_copy_button">Copy</button>
                <button type="button" class="basic-button translate" id="lodestone_interrogator_replace_button">Replace Generate Prompt</button>
                <button type="button" class="basic-button translate" id="lodestone_interrogator_append_button">Append To Generate Prompt</button>
            </div>
            <div id="lodestone_interrogator_grouped_results"></div>
        </div>
    </div>
</div>
```

- [ ] **Step 4: Create placeholder JS class**

Create `src/Extensions/LodestoneImageInterrogator/Assets/lodestone_interrogator.js`:

```javascript
class LodestoneInterrogatorHelper {
    /** Initializes the panel once the DOM is ready. */
    init() {
        let panel = document.getElementById('lodestone_interrogator_panel');
        if (!panel) {
            return;
        }
        getRequiredElementById('lodestone_interrogator_status_text').innerText = 'Setup is required before first use.';
    }
}

let lodestoneInterrogator = new LodestoneInterrogatorHelper();

document.addEventListener('DOMContentLoaded', () => {
    lodestoneInterrogator.init();
});
```

- [ ] **Step 5: Create scoped placeholder CSS**

Create `src/Extensions/LodestoneImageInterrogator/Assets/lodestone_interrogator.css`:

```css
.lodestone-interrogator {
    display: flex;
    flex-direction: column;
    gap: 1rem;
    padding: 1rem;
    height: 100%;
    overflow: auto;
}

.lodestone-interrogator-status,
.lodestone-interrogator-main,
.lodestone-interrogator-actions {
    display: flex;
    gap: 1rem;
}

.lodestone-interrogator-status {
    align-items: center;
    justify-content: space-between;
}

.lodestone-interrogator-main {
    align-items: stretch;
}

.lodestone-interrogator-image,
.lodestone-interrogator-controls,
.lodestone-interrogator-results {
    display: flex;
    flex-direction: column;
    gap: 0.5rem;
}

.lodestone-interrogator-results {
    flex: 1;
}

.lodestone-interrogator-preview {
    min-width: 18rem;
    min-height: 18rem;
    border: 1px solid var(--border-color);
    display: flex;
    align-items: center;
    justify-content: center;
}
```

- [ ] **Step 6: Static validation**

Run:

```bash
rg -n "var |const |===|!==|} else \\{|#[A-Za-z0-9_-]" src/Extensions/LodestoneImageInterrogator
```

Expected: no `var`, no `const`, no unnecessary strict equality, no `} else {`, and no CSS id selectors.

- [ ] **Step 7: Commit**

```bash
git add src/Extensions/LodestoneImageInterrogator
git commit -m "Add Lodestone interrogator extension skeleton"
```

## Task 2: API Models, Permissions, And Status Route

**Files:**
- Create: `src/Extensions/LodestoneImageInterrogator/src/LodestoneModels.cs`
- Create: `src/Extensions/LodestoneImageInterrogator/src/LodestoneSetupManager.cs`
- Create: `src/Extensions/LodestoneImageInterrogator/WebAPI/LodestoneInterrogatorAPI.cs`
- Modify: `src/Extensions/LodestoneImageInterrogator/LodestoneImageInterrogator.cs`

- [ ] **Step 1: Create DTOs**

Create `src/Extensions/LodestoneImageInterrogator/src/LodestoneModels.cs`:

```csharp
using Newtonsoft.Json.Linq;

namespace LodestoneImageInterrogatorExtension;

public class LodestoneSetupStatus
{
    public bool IsReady;
    public bool IsSetupRunning;
    public bool HasPythonEnv;
    public bool HasModelFile;
    public bool HasVocabFile;
    public string Message = "";

    public JObject ToJson()
    {
        return new JObject()
        {
            ["isReady"] = IsReady,
            ["isSetupRunning"] = IsSetupRunning,
            ["hasPythonEnv"] = HasPythonEnv,
            ["hasModelFile"] = HasModelFile,
            ["hasVocabFile"] = HasVocabFile,
            ["message"] = Message
        };
    }
}
```

- [ ] **Step 2: Create setup manager status logic**

Create `src/Extensions/LodestoneImageInterrogator/src/LodestoneSetupManager.cs`:

```csharp
using System.IO;

namespace LodestoneImageInterrogatorExtension;

public static class LodestoneSetupManager
{
    private static readonly object SetupLock = new();

    public static bool IsSetupRunning;

    public static string ExtensionRoot => "src/Extensions/LodestoneImageInterrogator";

    public static string DataRoot => $"{ExtensionRoot}/Data";

    public static string PythonEnvPath => $"{DataRoot}/python_env";

    public static string ModelPath => $"{DataRoot}/models/tagger_proto.safetensors";

    public static string VocabPath => $"{DataRoot}/models/tagger_vocab_with_categories_and_alias_updated.json";

    public static LodestoneSetupStatus GetStatus()
    {
        bool hasPythonEnv = Directory.Exists(PythonEnvPath);
        bool hasModelFile = File.Exists(ModelPath);
        bool hasVocabFile = File.Exists(VocabPath);
        bool isReady = hasPythonEnv && hasModelFile && hasVocabFile;
        return new LodestoneSetupStatus()
        {
            IsReady = isReady,
            IsSetupRunning = IsSetupRunning,
            HasPythonEnv = hasPythonEnv,
            HasModelFile = hasModelFile,
            HasVocabFile = hasVocabFile,
            Message = isReady ? "Ready." : "Setup is required before first use."
        };
    }

    public static bool TryMarkSetupRunning()
    {
        lock (SetupLock)
        {
            if (IsSetupRunning)
            {
                return false;
            }
            IsSetupRunning = true;
            return true;
        }
    }

    public static void MarkSetupFinished()
    {
        lock (SetupLock)
        {
            IsSetupRunning = false;
        }
    }
}
```

- [ ] **Step 3: Create API route registration**

Create `src/Extensions/LodestoneImageInterrogator/WebAPI/LodestoneInterrogatorAPI.cs`:

```csharp
using Newtonsoft.Json.Linq;
using SwarmUI.Accounts;
using SwarmUI.WebAPI;

namespace LodestoneImageInterrogatorExtension.WebAPI;

public static class LodestoneInterrogatorPermissions
{
    public static readonly PermInfoGroup Group = new("Lodestone Image Interrogator", "Permissions for Lodestone image interrogation.");
    public static readonly PermInfo Use = Permissions.Register(new("use_lodestone_image_interrogator", "Use Lodestone Image Interrogator", "Allows using the Lodestone Image Interrogator extension.", PermissionDefault.POWERUSERS, Group));
}

[API.APIClass("API routes for the Lodestone Image Interrogator extension")]
public static class LodestoneInterrogatorAPI
{
    public static void Register()
    {
        API.RegisterAPICall(LodestoneInterrogatorStatus, false, LodestoneInterrogatorPermissions.Use);
    }

    [API.APIDescription("Gets Lodestone Image Interrogator setup status", "Returns whether the extension has local dependencies and model files ready.")]
    public static async Task<JObject> LodestoneInterrogatorStatus(Session session)
    {
        LodestoneSetupStatus status = LodestoneSetupManager.GetStatus();
        return new JObject()
        {
            ["success"] = true,
            ["status"] = status.ToJson()
        };
    }
}
```

- [ ] **Step 4: Static validation**

Run:

```bash
rg -n "\\bvar\\b|[^/]//|PLACEHOLDER_MARKER" src/Extensions/LodestoneImageInterrogator
```

Expected: no C# `var` and no placeholder markers. Inspect any comment hits manually.

- [ ] **Step 5: Commit**

```bash
git add src/Extensions/LodestoneImageInterrogator
git commit -m "Add Lodestone interrogator status API"
```

## Task 3: Setup Execution

**Files:**
- Modify: `src/Extensions/LodestoneImageInterrogator/src/LodestoneSetupManager.cs`
- Modify: `src/Extensions/LodestoneImageInterrogator/WebAPI/LodestoneInterrogatorAPI.cs`
- Create: `src/Extensions/LodestoneImageInterrogator/Runner/requirements.txt`

- [ ] **Step 1: Add requirements file**

Create `src/Extensions/LodestoneImageInterrogator/Runner/requirements.txt`:

```text
torch
torchvision
safetensors
Pillow
requests
```

- [ ] **Step 2: Add setup method**

Add this method to `LodestoneSetupManager`:

```csharp
public static async Task<LodestoneSetupStatus> RunSetup()
{
    if (!TryMarkSetupRunning())
    {
        LodestoneSetupStatus runningStatus = GetStatus();
        runningStatus.Message = "Setup is already running.";
        return runningStatus;
    }

    try
    {
        Directory.CreateDirectory($"{DataRoot}/models");
        if (!Directory.Exists(PythonEnvPath))
        {
            await SwarmUI.Utils.Utilities.QuickRunProcess("python3", ["-m", "venv", PythonEnvPath], ExtensionRoot);
        }

        string pythonExe = $"{PythonEnvPath}/bin/python";
        await SwarmUI.Utils.Utilities.QuickRunProcess(pythonExe, ["-m", "pip", "install", "-r", "Runner/requirements.txt"], ExtensionRoot);
        await DownloadIfMissing("https://huggingface.co/lodestones/taggerine/resolve/main/tagger_proto.safetensors", ModelPath);
        await DownloadIfMissing("https://huggingface.co/lodestones/taggerine/resolve/main/tagger_vocab_with_categories_and_alias_updated.json", VocabPath);

        LodestoneSetupStatus status = GetStatus();
        status.Message = status.IsReady ? "Setup complete." : "Setup finished, but required files are still missing.";
        return status;
    }
    finally
    {
        MarkSetupFinished();
    }
}

private static async Task DownloadIfMissing(string url, string targetPath)
{
    if (File.Exists(targetPath))
    {
        return;
    }
    using HttpClient client = new();
    using HttpResponseMessage response = await client.GetAsync(url);
    response.EnsureSuccessStatusCode();
    await using Stream remote = await response.Content.ReadAsStreamAsync();
    await using FileStream local = File.Create(targetPath);
    await remote.CopyToAsync(local);
}
```

If `python3` is not acceptable on the target platform, replace with a repo-local Python helper after inspecting `src/Utils/PythonLaunchHelper.cs` and `src/Backends/NetworkBackendUtils.cs`.

- [ ] **Step 3: Add setup API route**

Register the new call in `LodestoneInterrogatorAPI.Register()`:

```csharp
API.RegisterAPICall(LodestoneInterrogatorSetup, true, LodestoneInterrogatorPermissions.Use);
```

Add the method:

```csharp
[API.APIDescription("Runs Lodestone Image Interrogator setup", "Creates local dependencies and downloads required model files after explicit user request.")]
public static async Task<JObject> LodestoneInterrogatorSetup(Session session)
{
    try
    {
        LodestoneSetupStatus status = await LodestoneSetupManager.RunSetup();
        return new JObject()
        {
            ["success"] = status.IsReady,
            ["status"] = status.ToJson(),
            ["error"] = status.IsReady ? null : status.Message
        };
    }
    catch (Exception ex)
    {
        LodestoneSetupManager.MarkSetupFinished();
        return new JObject()
        {
            ["success"] = false,
            ["error"] = ex.Message,
            ["status"] = LodestoneSetupManager.GetStatus().ToJson()
        };
    }
}
```

- [ ] **Step 4: Static validation**

Run:

```bash
rg -n "\\bvar\\b|PLACEHOLDER_MARKER|RunSetup|DownloadIfMissing|LodestoneInterrogatorSetup" src/Extensions/LodestoneImageInterrogator
```

Expected: route and setup symbols are present, no `var`, no placeholder markers.

- [ ] **Step 5: Commit**

```bash
git add src/Extensions/LodestoneImageInterrogator
git commit -m "Add Lodestone setup workflow"
```

## Task 4: Python Inference Runner

**Files:**
- Create: `src/Extensions/LodestoneImageInterrogator/Runner/lodestone_interrogate.py`

- [ ] **Step 1: Create runner CLI**

Create `src/Extensions/LodestoneImageInterrogator/Runner/lodestone_interrogate.py`:

Use the Hugging Face source file `lodestones/taggerine/inference_tagger_standalone.py` as the runner base. It is Apache-2.0 and already contains the standalone DINOv3 ViT-H/16+ model classes, checkpoint loading, ImageNet preprocessing, sigmoid scoring, and top-k/threshold CLI. Save the adapted file as `src/Extensions/LodestoneImageInterrogator/Runner/lodestone_interrogate.py`.

Required adaptations:

- Keep the upstream architecture, `_split_and_clean_state_dict`, `_build_head_from_checkpoint`, `preprocess_image`, and `Tagger.predict` logic.
- Change the CLI from `--images` to a single `--image` argument.
- Add `--max-tags` as the output cap.
- Load `tag2category` from `tagger_vocab_with_categories_and_alias_updated.json`.
- Emit one JSON object instead of pretty text, tag text, or JSON arrays.
- Send informational loader messages to `stderr` so `stdout` remains parseable JSON.

The final `main()` function should end with this shape:

```python
def category_for(tag, tag2category):
    return tag2category.get(tag, "general")


def main():
    parser = argparse.ArgumentParser(description="Lodestone tagger JSON inference")
    parser.add_argument("--checkpoint", required=True)
    parser.add_argument("--vocab", required=True)
    parser.add_argument("--image", required=True)
    parser.add_argument("--threshold", type=float, default=0.35)
    parser.add_argument("--max-tags", type=int, default=80)
    parser.add_argument("--device", default="cuda")
    parser.add_argument("--max-size", type=int, default=1024)
    args = parser.parse_args()

    try:
        with open(args.vocab, "r", encoding="utf-8") as vocab_file:
            vocab_data = json.load(vocab_file)
        tag2category = vocab_data.get("tag2category", {})
        tagger = Tagger(
            checkpoint_path=args.checkpoint,
            vocab_path=args.vocab,
            device=args.device,
            max_size=args.max_size,
        )
        raw_results = tagger.predict(args.image, topk=None, threshold=args.threshold)
        limited = raw_results[:args.max_tags]
        tags = []
        groups = {}
        for tag, score in limited:
            category = category_for(tag, tag2category)
            item = {
                "name": tag,
                "probability": round(float(score), 6),
                "category": category,
            }
            tags.append(item)
            groups.setdefault(category, []).append(item)
        print(json.dumps({
            "success": True,
            "prompt": ", ".join(item["name"] for item in tags),
            "tags": tags,
            "groups": groups,
            "device": str(tagger.device),
        }, ensure_ascii=False))
        return 0
    except Exception as ex:
        print(json.dumps({"success": False, "error": str(ex)}, ensure_ascii=False))
        return 1
```

- [ ] **Step 2: Static validation**

Run:

```bash
python3 -m py_compile src/Extensions/LodestoneImageInterrogator/Runner/lodestone_interrogate.py
```

Expected: command exits successfully. This is syntax validation only, not a project test or model inference run.

- [ ] **Step 3: Commit**

```bash
git add src/Extensions/LodestoneImageInterrogator/Runner/lodestone_interrogate.py
git commit -m "Add Lodestone Python runner scaffold"
```

## Task 5: C# Runner Bridge And Interrogate API

**Files:**
- Create: `src/Extensions/LodestoneImageInterrogator/src/LodestoneRunner.cs`
- Modify: `src/Extensions/LodestoneImageInterrogator/WebAPI/LodestoneInterrogatorAPI.cs`

- [ ] **Step 1: Create runner bridge**

Create `src/Extensions/LodestoneImageInterrogator/src/LodestoneRunner.cs`:

```csharp
using Newtonsoft.Json.Linq;
using SwarmUI.Utils;
using System.IO;

namespace LodestoneImageInterrogatorExtension;

public static class LodestoneRunner
{
    public static async Task<JObject> Interrogate(string imageBase64, double threshold, int maxTags)
    {
        LodestoneSetupStatus status = LodestoneSetupManager.GetStatus();
        if (!status.IsReady)
        {
            return new JObject()
            {
                ["success"] = false,
                ["error"] = "Lodestone Image Interrogator setup is not complete."
            };
        }

        string tempPath = Path.GetTempFileName() + ".png";
        try
        {
            byte[] imageBytes = Convert.FromBase64String(imageBase64.After(","));
            await File.WriteAllBytesAsync(tempPath, imageBytes);
            string pythonExe = $"{LodestoneSetupManager.PythonEnvPath}/bin/python";
            string output = await Utilities.QuickRunProcess(pythonExe, [
                "Runner/lodestone_interrogate.py",
                "--checkpoint", LodestoneSetupManager.ModelPath,
                "--vocab", LodestoneSetupManager.VocabPath,
                "--image", tempPath,
                "--threshold", threshold.ToString(System.Globalization.CultureInfo.InvariantCulture),
                "--max-tags", maxTags.ToString()
            ], LodestoneSetupManager.ExtensionRoot);
            return JObject.Parse(output);
        }
        finally
        {
            if (File.Exists(tempPath))
            {
                File.Delete(tempPath);
            }
        }
    }
}
```

- [ ] **Step 2: Add interrogate route**

Register the new call:

```csharp
API.RegisterAPICall(LodestoneInterrogatorInterrogate, true, LodestoneInterrogatorPermissions.Use);
```

Add the route:

```csharp
[API.APIDescription("Interrogates one image with Lodestone", "Returns prompt-ready tags and grouped confidence data for one image.")]
public static async Task<JObject> LodestoneInterrogatorInterrogate(Session session, string image, double threshold = 0.35, int maxTags = 80)
{
    try
    {
        if (string.IsNullOrWhiteSpace(image))
        {
            return new JObject()
            {
                ["success"] = false,
                ["error"] = "No image was provided."
            };
        }
        return await LodestoneRunner.Interrogate(image, threshold, maxTags);
    }
    catch (Exception ex)
    {
        return new JObject()
        {
            ["success"] = false,
            ["error"] = ex.Message
        };
    }
}
```

- [ ] **Step 3: Static validation**

Run:

```bash
rg -n "\\bvar\\b|PLACEHOLDER_MARKER|LodestoneInterrogatorInterrogate|QuickRunProcess" src/Extensions/LodestoneImageInterrogator
```

Expected: interrogate symbols are present, no `var`, no placeholder markers.

- [ ] **Step 4: Commit**

```bash
git add src/Extensions/LodestoneImageInterrogator
git commit -m "Add Lodestone interrogation API bridge"
```

## Task 6: Panel JavaScript

**Files:**
- Modify: `src/Extensions/LodestoneImageInterrogator/Assets/lodestone_interrogator.js`

- [ ] **Step 1: Replace placeholder JS with panel behavior**

Replace `Assets/lodestone_interrogator.js` with:

```javascript
class LodestoneInterrogatorHelper {
    /** Initializes the panel once the DOM is ready. */
    init() {
        this.imageData = '';
        this.lastPrompt = '';
        let panel = document.getElementById('lodestone_interrogator_panel');
        if (!panel) {
            return;
        }
        getRequiredElementById('lodestone_interrogator_setup_button').addEventListener('click', this.runSetup.bind(this));
        getRequiredElementById('lodestone_interrogator_file').addEventListener('change', this.loadFile.bind(this));
        getRequiredElementById('lodestone_interrogator_run_button').addEventListener('click', this.interrogate.bind(this));
        getRequiredElementById('lodestone_interrogator_copy_button').addEventListener('click', this.copyPrompt.bind(this));
        getRequiredElementById('lodestone_interrogator_replace_button').addEventListener('click', () => this.sendToGenerate('replace'));
        getRequiredElementById('lodestone_interrogator_append_button').addEventListener('click', () => this.sendToGenerate('append'));
        this.refreshStatus();
    }

    /** Refreshes setup status from the server. */
    refreshStatus() {
        genericRequest('LodestoneInterrogatorStatus', {}, data => {
            let status = data.status;
            getRequiredElementById('lodestone_interrogator_status_text').innerText = status.message;
            getRequiredElementById('lodestone_interrogator_setup_button').disabled = status.isReady || status.isSetupRunning;
            getRequiredElementById('lodestone_interrogator_run_button').disabled = !status.isReady;
        });
    }

    /** Starts explicit first-use setup. */
    runSetup() {
        getRequiredElementById('lodestone_interrogator_status_text').innerText = 'Running setup. This can take a long time for the 5.27 GB model download.';
        getRequiredElementById('lodestone_interrogator_setup_button').disabled = true;
        genericRequest('LodestoneInterrogatorSetup', {}, data => {
            if (!data.success) {
                getRequiredElementById('lodestone_interrogator_status_text').innerText = data.error || 'Setup failed.';
            }
            this.refreshStatus();
        }, 0, error => {
            getRequiredElementById('lodestone_interrogator_status_text').innerText = `Setup failed: ${error}`;
            this.refreshStatus();
        }, 1000 * 60 * 60);
    }

    /** Loads a user-selected local image. */
    loadFile(event) {
        let file = event.target.files[0];
        if (!file) {
            return;
        }
        let reader = new FileReader();
        reader.onload = () => {
            this.imageData = reader.result;
            getRequiredElementById('lodestone_interrogator_preview').innerHTML = `<img src="${escapeHtml(this.imageData)}" alt="Selected image">`;
        };
        reader.readAsDataURL(file);
    }

    /** Sends the selected image to the server for interrogation. */
    interrogate() {
        if (!this.imageData) {
            showError('Select an image before interrogating.');
            return;
        }
        let threshold = parseFloat(getRequiredElementById('lodestone_interrogator_threshold').value);
        let maxTags = parseInt(getRequiredElementById('lodestone_interrogator_max_tags').value);
        genericRequest('LodestoneInterrogatorInterrogate', { image: this.imageData, threshold: threshold, maxTags: maxTags }, data => {
            if (!data.success) {
                showError(data.error || 'Interrogation failed.');
                return;
            }
            this.renderResults(data);
        }, 0, error => showError(`Interrogation failed: ${error}`), 1000 * 60 * 10);
    }

    /** Renders returned tags. */
    renderResults(data) {
        this.lastPrompt = data.prompt || '';
        getRequiredElementById('lodestone_interrogator_prompt').value = this.lastPrompt;
        getRequiredElementById('lodestone_interrogator_grouped_results').innerText = JSON.stringify(data.groups || {}, null, 2);
    }

    /** Copies prompt output to clipboard. */
    copyPrompt() {
        navigator.clipboard.writeText(getRequiredElementById('lodestone_interrogator_prompt').value);
    }

    /** Sends prompt output to the Generate tab prompt box. */
    sendToGenerate(mode) {
        let promptBox = getRequiredElementById('alt_prompt_textbox');
        let text = getRequiredElementById('lodestone_interrogator_prompt').value.trim();
        if (!text) {
            return;
        }
        if (mode == 'replace') {
            promptBox.value = text;
        }
        else {
            promptBox.value = promptBox.value.trim() ? `${promptBox.value.trim()}, ${text}` : text;
        }
        getRequiredElementById('text2imagetabbutton').click();
    }
}

let lodestoneInterrogator = new LodestoneInterrogatorHelper();

document.addEventListener('DOMContentLoaded', () => {
    lodestoneInterrogator.init();
});
```

- [ ] **Step 2: Static validation**

Run:

```bash
rg -n "\\bvar\\b|\\bconst\\b|===|!==|} else \\{|PLACEHOLDER_MARKER" src/Extensions/LodestoneImageInterrogator/Assets/lodestone_interrogator.js
```

Expected: no matches.

- [ ] **Step 3: Commit**

```bash
git add src/Extensions/LodestoneImageInterrogator/Assets/lodestone_interrogator.js
git commit -m "Add Lodestone interrogator panel behavior"
```

## Task 7: Generate-Tab Send Bridge

**Files:**
- Modify: `src/Extensions/LodestoneImageInterrogator/Assets/lodestone_interrogator.js`
- Modify: `src/Extensions/LodestoneImageInterrogator/Assets/lodestone_interrogator.css`

- [ ] **Step 1: Add bridge button injection**

Add this method to `LodestoneInterrogatorHelper` and call it from `init()` after `this.refreshStatus();`:

```javascript
    /** Adds a Generate-tab button to send the visible current image to this panel. */
    addGenerateBridgeButton() {
        let currentImage = document.getElementById('current_image');
        if (!currentImage || document.getElementById('lodestone_interrogator_send_current_button')) {
            return;
        }
        let button = document.createElement('button');
        button.type = 'button';
        button.id = 'lodestone_interrogator_send_current_button';
        button.className = 'basic-button lodestone-interrogator-send-current translate';
        button.innerText = 'Interrogate Image';
        button.addEventListener('click', this.takeCurrentImage.bind(this));
        currentImage.parentElement.insertBefore(button, currentImage);
    }

    /** Copies the current displayed generated image into the interrogator panel. */
    takeCurrentImage() {
        let currentImage = document.getElementById('current_image');
        let image = currentImage ? currentImage.querySelector('img') : null;
        if (!image || !image.src) {
            showError('No current Generate image is available to interrogate.');
            return;
        }
        this.imageData = image.src;
        getRequiredElementById('lodestone_interrogator_preview').innerHTML = `<img src="${escapeHtml(this.imageData)}" alt="Selected image">`;
        let tabButton = document.querySelector('[href="#Image_Interrogator"], [href="#ImageInterrogator"], [href="#Image\\ Interrogator"]');
        if (tabButton) {
            tabButton.click();
        }
    }
```

If the extension tab ID generated by Swarm differs, inspect rendered tab markup and update the selector to the exact tab ID.

- [ ] **Step 2: Add bridge button style**

Add to `Assets/lodestone_interrogator.css`:

```css
.lodestone-interrogator-send-current {
    align-self: flex-start;
    margin-bottom: 0.5rem;
}

.lodestone-interrogator-preview img {
    max-width: 100%;
    max-height: 28rem;
    object-fit: contain;
}
```

- [ ] **Step 3: Static validation**

Run:

```bash
rg -n "\\bvar\\b|\\bconst\\b|===|!==|} else \\{|#[A-Za-z0-9_-]" src/Extensions/LodestoneImageInterrogator/Assets
```

Expected: no `var`, no `const`, no unnecessary strict equality, no `} else {`, and no CSS id selectors.

- [ ] **Step 4: Commit**

```bash
git add src/Extensions/LodestoneImageInterrogator/Assets
git commit -m "Add Generate tab bridge for Lodestone interrogator"
```

## Task 8: Runner Robustness And Category Fidelity

**Files:**
- Modify: `src/Extensions/LodestoneImageInterrogator/Runner/lodestone_interrogate.py`

- [ ] **Step 1: Confirm runner output contract**

The final JSON shape emitted by Python must be:

```json
{
  "success": true,
  "prompt": "tag_one, tag_two",
  "tags": [
    { "name": "tag_one", "probability": 0.91, "category": "general" }
  ],
  "groups": {
    "general": [
      { "name": "tag_one", "probability": 0.91, "category": "general" }
    ]
  },
  "device": "cuda"
}
```

- [ ] **Step 2: Add category fallback normalization**

Ensure every tag has a category string and normalize missing/empty values to `general`:

```python
def category_for(tag, tag2category):
    category = tag2category.get(tag, "general")
    if not category:
        return "general"
    return str(category)
```

- [ ] **Step 3: Keep stdout JSON-only**

If the adapted upstream file still prints loader status with `print(...)`, change those informational calls to:

```python
print(message, file=sys.stderr)
```

The C# bridge parses stdout as a single JSON object, so stdout must not contain loader banners or progress text.

- [ ] **Step 4: Static validation**

Run:

```bash
python3 -m py_compile src/Extensions/LodestoneImageInterrogator/Runner/lodestone_interrogate.py
```

Expected: syntax validation passes.

- [ ] **Step 5: Commit**

```bash
git add src/Extensions/LodestoneImageInterrogator/Runner/lodestone_interrogate.py
git commit -m "Implement Lodestone tagger inference"
```

## Task 9: Full Tag Controls And Group Rendering

**Files:**
- Modify: `src/Extensions/LodestoneImageInterrogator/Tabs/Text2Image/Image Interrogator.html`
- Modify: `src/Extensions/LodestoneImageInterrogator/Assets/lodestone_interrogator.js`
- Modify: `src/Extensions/LodestoneImageInterrogator/Assets/lodestone_interrogator.css`

- [ ] **Step 1: Add category controls**

Add checkboxes to the controls block:

```html
<label><input type="checkbox" class="lodestone-interrogator-category" value="rating" checked> Rating</label>
<label><input type="checkbox" class="lodestone-interrogator-category" value="character" checked> Character</label>
<label><input type="checkbox" class="lodestone-interrogator-category" value="general" checked> General</label>
<label><input type="checkbox" class="lodestone-interrogator-category" value="style" checked> Style</label>
```

- [ ] **Step 2: Filter client-side output**

Add methods to JS:

```javascript
    /** Returns selected category names. */
    selectedCategories() {
        let selected = [];
        let boxes = document.getElementsByClassName('lodestone-interrogator-category');
        for (let i = 0; i < boxes.length; i++) {
            if (boxes[i].checked) {
                selected.push(boxes[i].value);
            }
        }
        return selected;
    }

    /** Converts tag result objects to a prompt string. */
    formatPrompt(tags) {
        let selected = this.selectedCategories();
        let output = [];
        for (let i = 0; i < tags.length; i++) {
            let tag = tags[i];
            if (selected.indexOf(tag.category) >= 0) {
                output.push(tag.name);
            }
        }
        return output.join(', ');
    }
```

Update `renderResults(data)` so it stores `this.lastTags = data.tags || [];`, formats prompt with `formatPrompt`, and builds grouped HTML with escaped tag names and percentages.

- [ ] **Step 3: Static validation**

Run:

```bash
rg -n "\\bvar\\b|\\bconst\\b|===|!==|} else \\{|#[A-Za-z0-9_-]" src/Extensions/LodestoneImageInterrogator/Assets src/Extensions/LodestoneImageInterrogator/Tabs
```

Expected: no matches for style violations.

- [ ] **Step 4: Commit**

```bash
git add src/Extensions/LodestoneImageInterrogator/Tabs src/Extensions/LodestoneImageInterrogator/Assets
git commit -m "Add Lodestone tag filtering controls"
```

## Task 10: Docs, License, And Final Static Review

**Files:**
- Create: `src/Extensions/LodestoneImageInterrogator/README.md`
- Create: `src/Extensions/LodestoneImageInterrogator/LICENSE`
- Review: all files under `src/Extensions/LodestoneImageInterrogator`

- [ ] **Step 1: Add README**

Create `README.md` with:

```markdown
# Lodestone Image Interrogator

SwarmUI extension for local image interrogation with the Lodestone tagger.

## Setup

Install the extension, restart or rebuild SwarmUI as required for extensions, then open the **Image Interrogator** panel. Click **Setup** to create local Python dependencies and download the required Hugging Face model files.

The model file `tagger_proto.safetensors` is about 5.27 GB. Setup downloads from `lodestones/taggerine` on Hugging Face only after you click the setup button.

## Privacy

Inference runs locally. Images are sent only to the local runner process. This extension does not use remote hosted inference.

## Content Notice

The tag vocabulary is based on e621 and Danbooru annotations and can include rating or adult-content tags. Use the panel filters to include or exclude categories.

## License

MIT.
```

- [ ] **Step 2: Add MIT license**

Create `LICENSE` with the standard MIT license text and the current year/name:

```text
MIT License

Copyright (c) 2026 Reaper176

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.
```

- [ ] **Step 3: Final static review**

Run:

```bash
rg -n "\\bvar\\b|PLACEHOLDER_MARKER|} else \\{|#[A-Za-z0-9_-]" src/Extensions/LodestoneImageInterrogator
```

Expected: no C# `var`, no placeholder markers, no JS `} else {`, no CSS id selectors.

Run:

```bash
python3 -m py_compile src/Extensions/LodestoneImageInterrogator/Runner/lodestone_interrogate.py
```

Expected: syntax validation passes.

- [ ] **Step 4: Commit**

```bash
git add src/Extensions/LodestoneImageInterrogator
git commit -m "Document Lodestone image interrogator extension"
```

## Manual Verification Checklist For Developer

- [ ] Install extension from a fresh clone/folder.
- [ ] Rebuild or launch SwarmUI with extension compilation enabled.
- [ ] Open Generate page and confirm Image Interrogator panel appears.
- [ ] Confirm no Hugging Face download starts before clicking Setup.
- [ ] Click Setup and confirm model/dependencies install.
- [ ] Restart SwarmUI and confirm setup state remains ready.
- [ ] Upload an image in the panel and interrogate it.
- [ ] Send tags to Generate prompt with replace.
- [ ] Send tags to Generate prompt with append.
- [ ] Send a current Generate image to the panel.
- [ ] Delete or rename model file and confirm missing-file error is clear.
