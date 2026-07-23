# Transactional Server-Settings Persistence Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make all maintained server-settings writes observable and make `ChangeServerSettings` publish accepted values only as one validated, durable candidate.

**Architecture:** Add a result-returning settings persistence owner and serialized transaction callback in `Program`, while retaining the public void compatibility facade. `ChangeServerSettings` validates and saves a separate `Settings` candidate before publishing it; extension, installation, and IOPaint callers snapshot their affected settings and roll back on failed persistence.

**Tech Stack:** C# 12, .NET 8, ASP.NET Core API handlers, FreneticUtilities `AutoConfiguration`/FDS persistence, Newtonsoft.Json.

**Execution context:** Work directly on `master` as requested by maintainer Reaper176. Do not create a worktree. Preserve the existing modified files and never inspect or stage `Data.pre-restore-2026-07-19/`.

**Repository verification constraint:** `AGENTS.md` prohibits agents from running builds, automated tests, browsers, servers, backends, installers, launchers, or live settings mutations. The maintainer supplies compilation and runtime validation. Agents use static source tracing, exact-count assertions, diff/whitespace checks, and independent reviews.

**Approved design:** `docs/superpowers/specs/2026-07-22-transactional-server-settings-persistence-design.md` at commit `78bcca6f`.

**Implementation outcome:** Production is implemented and statically reviewed; maintainer compilation and the named settings transaction/persistence matrix are pending.

**Implementation provenance:** Corrective commit `77e1ea66` uses canonical `userauthorization.authorizationrequired` and case-insensitive path-trigger matching, superseding those two literal snippets below for historical execution accuracy.

---

### Task 1: Add the observable settings persistence owner

**Files:**
- Modify: `src/Core/Program.cs:19-55`
- Modify: `src/Core/Program.cs:168-172`
- Modify: `src/Core/Program.cs:699-724`

- [ ] **Step 1: Confirm the owner and inventory**

Run:

```bash
git status --short --branch --untracked-files=no
rg -n 'SaveSettingsFile\(' src --glob '*.cs'
nl -ba src/Core/Program.cs | sed -n '35,60p;160,176p;695,728p'
```

Expected: eleven invocations plus the definition; save is void, catches failures, and silently returns when locked; only the four known tracked user modifications appear.

- [ ] **Step 2: Add the explicit result and transaction owner**

Insert before `Program`:

```csharp
/// <summary>Outcome of an attempted authoritative server-settings persistence operation.</summary>
public enum SettingsSaveResult
{
    /// <summary>The authoritative settings FDS was written successfully.</summary>
    Saved,

    /// <summary>Settings persistence is disabled by the process lock-settings option.</summary>
    Locked,

    /// <summary>The authoritative settings FDS could not be written.</summary>
    Failed
}
```

Insert beside `LockSettings`:

```csharp
/// <summary>Serializes maintained runtime settings mutations and authoritative saves.</summary>
private static readonly object SettingsTransactionLock = new();
```

Add before the persistence methods:

```csharp
/// <summary>Runs a maintained settings mutation under the shared transaction boundary.</summary>
internal static T RunSettingsTransaction<T>(Func<T> action)
{
    lock (SettingsTransactionLock)
    {
        return action();
    }
}
```

- [ ] **Step 3: Replace the silent save with the observable owner and facade**

Replace `SaveSettingsFile` with:

```csharp
/// <summary>Attempts to save the current server settings and returns the authoritative persistence outcome.</summary>
public static SettingsSaveResult TrySaveSettingsFile()
{
    return TrySaveSettingsFile(ServerSettings);
}

/// <summary>Attempts to save a supplied server-settings candidate and returns the authoritative persistence outcome.</summary>
public static SettingsSaveResult TrySaveSettingsFile(Settings settings)
{
    lock (SettingsTransactionLock)
    {
        if (LockSettings)
        {
            return SettingsSaveResult.Locked;
        }
        try
        {
            FDSUtility.SaveToFile(settings.Save(true), SettingsFilePath);
        }
        catch (Exception ex)
        {
            Logs.Error($"Error saving settings file: {ex.ReadableString()}");
            return SettingsSaveResult.Failed;
        }
        try
        {
            bool hasAlwaysPullFile = File.Exists("./src/bin/always_pull");
            if (settings.Maintenance.AutoPullDevUpdates && !hasAlwaysPullFile)
            {
                File.WriteAllText("./src/bin/always_pull", "true");
            }
            else if (!settings.Maintenance.AutoPullDevUpdates && hasAlwaysPullFile)
            {
                File.Delete("./src/bin/always_pull");
            }
        }
        catch (Exception ex)
        {
            Logs.Error($"Error synchronizing always-pull marker after settings save: {ex.ReadableString()}");
        }
        return SettingsSaveResult.Saved;
    }
}

/// <summary>Compatibility facade that saves current server settings and logs failures through the observable owner.</summary>
public static void SaveSettingsFile()
{
    TrySaveSettingsFile();
}
```

- [ ] **Step 4: Observe startup normalization**

Replace its call inside the existing `if (!LockSettings)` block with:

```csharp
SettingsSaveResult settingsSaveResult = TrySaveSettingsFile();
if (settingsSaveResult != SettingsSaveResult.Saved)
{
    Logs.Error($"Startup settings normalization was not persisted: {settingsSaveResult}.");
}
```

- [ ] **Step 5: Verify and commit Task 1**

Run:

```bash
test "$(rg -F -o 'public enum SettingsSaveResult' src/Core/Program.cs | wc -l)" = "1"
test "$(rg -F -o 'public static SettingsSaveResult TrySaveSettingsFile' src/Core/Program.cs | wc -l)" = "2"
test "$(rg -F -o 'public static void SaveSettingsFile()' src/Core/Program.cs | wc -l)" = "1"
rg -n 'SettingsSaveResult|RunSettingsTransaction|TrySaveSettingsFile|always-pull|Startup settings normalization' src/Core/Program.cs
git diff --check -- src/Core/Program.cs
git diff -- src/Core/Program.cs
git add src/Core/Program.cs
git diff --cached --check
test "$(git diff --cached --name-only)" = "src/Core/Program.cs"
git commit -m "refactor: expose server settings save result"
```

---

### Task 2: Make ChangeServerSettings a candidate transaction

**Files:**
- Modify: `src/WebAPI/AdminAPI.cs:150-221`
- Reference: `src/wwwroot/js/genpage/helpers/settings_editor.js:300-305`

- [ ] **Step 1: Extract model-path validation**

Add before `ChangeServerSettings`:

```csharp
/// <summary>Validates and creates the maintained model directories for a server-settings candidate.</summary>
private static void ValidateModelPaths(Settings candidate)
{
    string[] paths =
    [
        candidate.Paths.SDModelFolder, candidate.Paths.SDVAEFolder,
        candidate.Paths.SDLoraFolder, candidate.Paths.SDControlNetsFolder,
        candidate.Paths.SDClipVisionFolder
    ];
    foreach (string path in paths)
    {
        foreach (string subpath in path.Split(';').Where(p => !string.IsNullOrWhiteSpace(p)))
        {
            Utilities.EnsureDirectory(Utilities.CombinePathWithAbsolute(candidate.Paths.ActualModelRoot, subpath));
        }
    }
}
```

- [ ] **Step 2: Replace the route body**

Use this complete body without changing its signature or attributes:

```csharp
{
    JObject settings = (JObject)rawData["settings"];
    List<string> changed = [];
    bool pathsChanged = settings.Properties().Any(p => p.Name.StartsWith("paths.") || p.Name.StartsWith("performance.allowgpuspecific"));
    JObject transactionError = Program.RunSettingsTransaction(() =>
    {
        if (Program.LockSettings)
        {
            return new JObject() { ["error"] = "Settings are locked." };
        }
        Settings candidate = new();
        candidate.Load(Program.ServerSettings.Save(true));
        foreach ((string key, JToken val) in settings)
        {
            AutoConfiguration.Internal.SingleFieldData field = candidate.TryGetFieldInternalData(key, out _);
            if (field is null)
            {
                Logs.Error($"User '{session.User.UserID}' tried to set unknown server setting '{key}' to '{val}'.");
                continue;
            }
            if (field.Field.GetCustomAttribute<SettingHiddenAttribute>() is not null)
            {
                Logs.Error($"User '{session.User.UserID}' tried to set server setting '{key}' of type '{field.Field.FieldType.Name}' to '{val}', but that setting is marked as hidden from the normal interface.");
                continue;
            }
            bool isSecret = field.Field.GetCustomAttribute<ValueIsSecretAttribute>() is not null;
            object obj;
            try
            {
                obj = DataToType(val, field.Field.FieldType);
            }
            catch (Exception)
            {
                Logs.Error($"User '{session.User.UserID}' tried to set server setting '{key}' of type '{field.Field.FieldType.Name}' to '{val}', but type-conversion failed.");
                continue;
            }
            if (obj is null)
            {
                Logs.Error($"User '{session.User.UserID}' tried to set server setting '{key}' of type '{field.Field.FieldType.Name}' to '{val}', but type-conversion failed.");
                continue;
            }
            if (isSecret && obj is string str && str == "\t<secret>")
            {
                continue;
            }
            if (key.ToLowerFast() == "authorization.authorizationrequired" && $"{obj}".ToLowerFast() == "true" && session.User.Data.PasswordHashed == "")
            {
                return new JObject() { ["error"] = "Tried to enable authorization mode, but your account does not have a password. Configure your account login information before enabling authorization, so you don't get locked out." };
            }
            candidate.TrySetFieldValue(key, obj);
            changed.Add(key);
        }
        if (pathsChanged)
        {
            try
            {
                ValidateModelPaths(candidate);
            }
            catch (Exception ex)
            {
                Logs.Error($"Failed to create one or more directories: {ex.Message}");
                return new JObject() { ["error"] = "Model paths settings are invalid, rejected change." };
            }
        }
        SettingsSaveResult saveResult = Program.TrySaveSettingsFile(candidate);
        if (saveResult != SettingsSaveResult.Saved)
        {
            return new JObject() { ["error"] = saveResult == SettingsSaveResult.Locked ? "Settings are locked." : "Failed to save server settings." };
        }
        Program.ServerSettings.Load(candidate.Save(true));
        return null;
    });
    if (transactionError is not null)
    {
        return transactionError;
    }
    Logs.Warning($"User {session.User.UserID} changed server settings: {changed.JoinString(", ")}");
    bool runtimeWarning = false;
    if (pathsChanged)
    {
        try
        {
            Program.BuildModelLists();
            Program.RefreshAllModelSets();
            Program.ModelPathsChangedEvent?.Invoke();
        }
        catch (Exception ex)
        {
            Logs.Error($"Server settings were saved, but model path refresh failed: {ex.ReadableString()}");
            runtimeWarning = true;
        }
    }
    try
    {
        Program.ReapplySettings();
    }
    catch (Exception ex)
    {
        Logs.Error($"Server settings were saved, but runtime settings reapplication failed: {ex.ReadableString()}");
        runtimeWarning = true;
    }
    JObject result = new() { ["success"] = true };
    if (runtimeWarning)
    {
        result["warning"] = "Settings were saved, but one or more runtime refresh actions failed. A restart may be required.";
    }
    return result;
}
```

- [ ] **Step 3: Verify and commit Task 2**

Run:

```bash
test "$(rg -F -o 'Settings candidate = new();' src/WebAPI/AdminAPI.cs | wc -l)" = "1"
test "$(rg -F -o 'Program.TrySaveSettingsFile(candidate)' src/WebAPI/AdminAPI.cs | wc -l)" = "1"
test "$(rg -F -o 'Program.ServerSettings.Load(candidate.Save(true))' src/WebAPI/AdminAPI.cs | wc -l)" = "1"
test "$(rg -F -o 'Settings were saved, but one or more runtime refresh actions failed. A restart may be required.' src/WebAPI/AdminAPI.cs | wc -l)" = "1"
if sed -n '150,280p' src/WebAPI/AdminAPI.cs | rg -n 'origPaths|Program\.SaveSettingsFile\('; then exit 1; fi
rg -n 'RunSettingsTransaction|ValidateModelPaths|authorizationrequired|SettingsSaveResult|BuildModelLists|RefreshAllModelSets|ModelPathsChangedEvent|ReapplySettings|runtimeWarning' src/WebAPI/AdminAPI.cs
git diff --check -- src/WebAPI/AdminAPI.cs
git diff -- src/WebAPI/AdminAPI.cs
git add src/WebAPI/AdminAPI.cs
git diff --cached --check
test "$(git diff --cached --name-only)" = "src/WebAPI/AdminAPI.cs"
git commit -m "fix: commit server settings transactionally"
```

---

### Task 3: Migrate extension settings mutations

**Files:**
- Modify: `src/WebAPI/AdminAPI.cs:838-941`
- Reference: `src/Core/ExtensionsManager.cs:341-383`

- [ ] **Step 1: Add disabled-extension transaction helpers**

Add near the extension routes:

```csharp
/// <summary>Runs a disabled-extension settings mutation and restores the complete list if it is rejected or persistence fails.</summary>
private static (SettingsSaveResult SaveResult, bool Changed) SaveDisabledExtensionsChange(Func<bool> mutation)
{
    return Program.RunSettingsTransaction(() =>
    {
        if (Program.LockSettings)
        {
            return (SettingsSaveResult.Locked, false);
        }
        List<string> original = [.. Program.ServerSettings.DisabledExtensions];
        bool changed = mutation();
        if (!changed)
        {
            Program.ServerSettings.DisabledExtensions.Clear();
            Program.ServerSettings.DisabledExtensions.AddRange(original);
            return (SettingsSaveResult.Saved, false);
        }
        SettingsSaveResult result = Program.TrySaveSettingsFile();
        if (result != SettingsSaveResult.Saved)
        {
            Program.ServerSettings.DisabledExtensions.Clear();
            Program.ServerSettings.DisabledExtensions.AddRange(original);
        }
        return (result, true);
    });
}

/// <summary>Builds the fixed API error for a failed settings persistence prerequisite.</summary>
private static JObject SettingsSaveError(SettingsSaveResult result)
{
    return new JObject() { ["error"] = result == SettingsSaveResult.Locked ? "Settings are locked." : "Failed to save server settings." };
}
```

- [ ] **Step 2: Migrate extension install, enable/disable, and disabled uninstall**

For known-extension install, keep lookup/existing-folder checks first, then use:

```csharp
(SettingsSaveResult saveResult, _) = SaveDisabledExtensionsChange(() =>
{
    Program.Extensions.CleanDisabledExtensions();
    foreach (string folderName in ext.FolderNames)
    {
        Program.Extensions.RemoveDisabledExtensionSetting(folderName);
    }
    return true;
});
if (saveResult != SettingsSaveResult.Saved)
{
    return SettingsSaveError(saveResult);
}
```

For enable/disable, retain unknown/core checks, define `Extension extension` before the transaction, then use:

```csharp
(SettingsSaveResult saveResult, bool changed) = SaveDisabledExtensionsChange(() =>
{
    return enabled
        ? Program.Extensions.RemoveDisabledExtensionSetting(extensionName)
        : Program.Extensions.AddDisabledExtensionSetting(ExtensionsManager.GetFolderNameFromPath(extension.FilePath));
});
if (saveResult != SettingsSaveResult.Saved)
{
    return SettingsSaveError(saveResult);
}
if (!changed)
{
    return new JObject() { ["error"] = enabled ? "Unknown extension." : "Extension is already disabled." };
}
```

For the disabled-extension uninstall branch, use:

```csharp
(SettingsSaveResult saveResult, bool removed) = SaveDisabledExtensionsChange(() =>
{
    return Program.Extensions.RemoveDisabledExtensionSetting(extensionName);
});
if (saveResult != SettingsSaveResult.Saved)
{
    return SettingsSaveError(saveResult);
}
if (!removed)
{
    return new JObject() { ["error"] = "Unknown extension." };
}
```

Keep Git clone and folder recycle/delete after successful persistence.

- [ ] **Step 3: Verify and commit Task 3**

Run:

```bash
test "$(rg -F -o 'SaveDisabledExtensionsChange(' src/WebAPI/AdminAPI.cs | wc -l)" = "4"
test "$(rg -F -o 'Program.ServerSettings.DisabledExtensions.AddRange(original);' src/WebAPI/AdminAPI.cs | wc -l)" = "2"
rg -n 'SaveDisabledExtensionsChange|SettingsSaveError|RunGitProcess|FileSystem.DeleteDirectory|SetExtensionEnabled|UninstallExtension' src/WebAPI/AdminAPI.cs
git diff --check -- src/WebAPI/AdminAPI.cs
git diff -- src/WebAPI/AdminAPI.cs
git add src/WebAPI/AdminAPI.cs
git diff --cached --check
test "$(git diff --cached --name-only)" = "src/WebAPI/AdminAPI.cs"
git commit -m "fix: observe extension settings persistence"
```

---

### Task 4: Migrate installation completion

**Files:**
- Modify: `src/Core/Installation.cs:291-302`
- Reference: `src/Core/Installation.cs:305-341`
- Reference: `src/WebAPI/BasicAPIFeatures.cs:338-354`

- [ ] **Step 1: Make SettingsApply rollback and surface failure**

Replace `SettingsApply` with:

```csharp
/// <summary>Applies and durably saves final installation settings.</summary>
public static void SettingsApply()
{
    SettingsSaveResult saveResult = Program.RunSettingsTransaction(() =>
    {
        FDSSection original = Program.ServerSettings.Save(true);
        Program.ServerSettings.IsInstalled = true;
        Program.ServerSettings.InstallDate = $"{DateTimeOffset.Now:yyyy-MM-dd}";
        Program.ServerSettings.InstallVersion = Utilities.Version;
        if (Program.ServerSettings.LaunchMode == "webinstall")
        {
            Program.ServerSettings.LaunchMode = "web";
        }
        SettingsSaveResult result = Program.TrySaveSettingsFile();
        if (result != SettingsSaveResult.Saved)
        {
            Program.ServerSettings.Load(original);
        }
        return result;
    });
    if (saveResult != SettingsSaveResult.Saved)
    {
        throw new SwarmReadableErrorException(saveResult == SettingsSaveResult.Locked
            ? "Installation settings are locked and could not be saved."
            : "Installation settings could not be saved.");
    }
}
```

Add `using FreneticUtilities.FreneticDataSyntax;` if needed. Preserve the call before model download/reload and the existing readable installer catch.

- [ ] **Step 2: Verify and commit Task 4**

Run:

```bash
rg -n 'SettingsApply|FDSSection original|TrySaveSettingsFile|ServerSettings.Load|SwarmReadableErrorException|Installed!' src/Core/Installation.cs src/WebAPI/BasicAPIFeatures.cs
git diff --check -- src/Core/Installation.cs
git diff -- src/Core/Installation.cs
git add src/Core/Installation.cs
git diff --cached --check
test "$(git diff --cached --name-only)" = "src/Core/Installation.cs"
git commit -m "fix: require durable installation settings"
```

---

### Task 5: Migrate IOPaint settings mutations

**Files:**
- Modify: `src/WebAPI/BackendAPI.cs:294-418`

- [ ] **Step 1: Add IOPaint transaction helpers**

Add near the IOPaint routes:

```csharp
/// <summary>Applies an IOPaint settings mutation and restores the complete section if persistence fails.</summary>
private static SettingsSaveResult SaveIOPaintSettingsChange(Action<Settings.IOPaintServiceData> mutation)
{
    return Program.RunSettingsTransaction(() =>
    {
        if (Program.LockSettings)
        {
            return SettingsSaveResult.Locked;
        }
        FDSSection original = Program.ServerSettings.IOPaint.Save(true);
        mutation(Program.ServerSettings.IOPaint);
        SettingsSaveResult result = Program.TrySaveSettingsFile();
        if (result != SettingsSaveResult.Saved)
        {
            Program.ServerSettings.IOPaint.Load(original);
        }
        return result;
    });
}

/// <summary>Builds the fixed API error for an IOPaint settings persistence failure.</summary>
private static JObject IOPaintSettingsSaveError(SettingsSaveResult result)
{
    return new JObject() { ["error"] = result == SettingsSaveResult.Locked ? "Settings are locked." : "Failed to save server settings." };
}
```

Add `using FreneticUtilities.FreneticDataSyntax;` if needed.

- [ ] **Step 2: Migrate the four IOPaint mutation routes**

Replace `SaveIOPaintServiceSettings` direct mutation/save with:

```csharp
SettingsSaveResult saveResult = SaveIOPaintSettingsChange(settings =>
{
    settings.Enabled = enabled;
    settings.BootstrapPython = bootstrap_python?.Trim() ?? "";
    settings.VenvPath = venv_path?.Trim() ?? "";
    settings.Device = string.IsNullOrWhiteSpace(device) ? "cpu" : device.Trim().ToLowerInvariant();
    settings.ModelCachePath = model_cache_path?.Trim() ?? "";
});
if (saveResult != SettingsSaveResult.Saved)
{
    return IOPaintSettingsSaveError(saveResult);
}
return await BuildIOPaintServiceStatus();
```

After successful IOPaint installation, use:

```csharp
SettingsSaveResult saveResult = SaveIOPaintSettingsChange(settings =>
{
    settings.VenvPath = venvPath;
    settings.BootstrapPython = bootstrapPython;
    settings.Enabled = true;
});
if (saveResult != SettingsSaveResult.Saved)
{
    return IOPaintSettingsSaveError(saveResult);
}
return await BuildIOPaintServiceStatus();
```

After the existing managed-path check and environment deletion, uninstall uses:

```csharp
SettingsSaveResult saveResult = SaveIOPaintSettingsChange(settings =>
{
    settings.Enabled = false;
});
if (saveResult != SettingsSaveResult.Saved)
{
    return IOPaintSettingsSaveError(saveResult);
}
return await BuildIOPaintServiceStatus();
```

New-install-path selection uses:

```csharp
SettingsSaveResult saveResult = SaveIOPaintSettingsChange(settings =>
{
    settings.VenvPath = GetNextIOPaintVenvPath();
    settings.Enabled = false;
});
if (saveResult != SettingsSaveResult.Saved)
{
    return IOPaintSettingsSaveError(saveResult);
}
return await BuildIOPaintServiceStatus();
```

Retain the existing early locked checks as fast-path compatibility, and do not delete or recreate environments as save compensation.

- [ ] **Step 3: Verify and commit Task 5**

Run:

```bash
test "$(rg -F -o 'SaveIOPaintSettingsChange(' src/WebAPI/BackendAPI.cs | wc -l)" = "5"
test "$(rg -F -o 'Program.ServerSettings.IOPaint.Load(original);' src/WebAPI/BackendAPI.cs | wc -l)" = "1"
if sed -n '285,470p' src/WebAPI/BackendAPI.cs | rg -n 'Program\.SaveSettingsFile\('; then exit 1; fi
rg -n 'SaveIOPaintSettingsChange|IOPaintSettingsSaveError|TrySaveSettingsFile|Directory.Delete|BuildIOPaintServiceStatus' src/WebAPI/BackendAPI.cs
git diff --check -- src/WebAPI/BackendAPI.cs
git diff -- src/WebAPI/BackendAPI.cs
git add src/WebAPI/BackendAPI.cs
git diff --cached --check
test "$(git diff --cached --name-only)" = "src/WebAPI/BackendAPI.cs"
git commit -m "fix: observe IOPaint settings persistence"
```

---

### Task 6: Reconcile architecture records and complete static review

**Files:**
- Modify: `docs/superpowers/specs/2026-07-22-transactional-server-settings-persistence-design.md`
- Modify: `docs/superpowers/plans/2026-07-22-transactional-server-settings-persistence.md`
- Modify: `docs/superpowers/audits/2026-07-21-maintainability-architecture-refresh.md`
- Review: `src/Core/Program.cs`
- Review: `src/WebAPI/AdminAPI.cs`
- Review: `src/Core/Installation.cs`
- Review: `src/WebAPI/BackendAPI.cs`
- Review: `src/wwwroot/js/genpage/helpers/settings_editor.js`

- [ ] **Step 1: Record implementation without claiming runtime validation**

Change the design status to:

```markdown
**Status:** Implemented; awaiting maintainer validation
```

Insert after the plan's approved-design line:

```markdown
**Implementation outcome:** Production is implemented and statically reviewed; maintainer compilation and the named settings transaction/persistence matrix are pending.
```

Update only Core F6/rank-4 audit records to state implementation is complete awaiting maintainer validation, identify actual production commits/files, preserve Core F7 separation, and keep rank 4 as Recommended Next Project. Do not advance rank 5 or change unrelated statuses.

- [ ] **Step 2: Run whole-project specification review**

Prove every approved requirement has a source path: explicit result, compatibility facade, all eleven invocations, candidate isolation, precommit validation, durable-before-publish, locked behavior, extension/IOPaint/installation rollback, authoritative-marker separation, post-commit warning, unchanged editor callback, and all non-goals. Correct and re-review every Critical or Important finding.

- [ ] **Step 3: Run whole-project quality/security review**

Review lock reentrancy/order, candidate completeness, reflective conversion exceptions, secret placeholders, publication failure, rollback aliasing, extension semantic errors, IOPaint external side effects, marker classification, post-commit exception detail, C# conventions, public compatibility, and exact scope. Correct and re-review every Critical or Important finding.

- [ ] **Step 4: Run final static verification**

Run:

```bash
test "$(rg -F -o 'public static void SaveSettingsFile()' src/Core/Program.cs | wc -l)" = "1"
test "$(rg -F -o 'public static SettingsSaveResult TrySaveSettingsFile' src/Core/Program.cs | wc -l)" = "2"
test "$(rg -F -o 'Program.TrySaveSettingsFile(candidate)' src/WebAPI/AdminAPI.cs | wc -l)" = "1"
test "$(rg -F -o 'Program.ServerSettings.Load(candidate.Save(true))' src/WebAPI/AdminAPI.cs | wc -l)" = "1"
test "$(rg -F -o 'Settings were saved, but one or more runtime refresh actions failed. A restart may be required.' src/WebAPI/AdminAPI.cs | wc -l)" = "1"
test "$(rg -F -o 'SaveDisabledExtensionsChange(' src/WebAPI/AdminAPI.cs | wc -l)" = "4"
test "$(rg -F -o 'SaveIOPaintSettingsChange(' src/WebAPI/BackendAPI.cs | wc -l)" = "5"
if rg -n 'Program\.SaveSettingsFile\(' src/Core/Installation.cs src/WebAPI/AdminAPI.cs src/WebAPI/BackendAPI.cs; then exit 1; fi
rg -n 'SettingsSaveResult|RunSettingsTransaction|TrySaveSettingsFile|SaveDisabledExtensionsChange|SaveIOPaintSettingsChange|runtimeWarning' src/Core/Program.cs src/Core/Installation.cs src/WebAPI/AdminAPI.cs src/WebAPI/BackendAPI.cs
git diff --check 78bcca6f..HEAD
git diff --check -- docs/superpowers/specs/2026-07-22-transactional-server-settings-persistence-design.md docs/superpowers/plans/2026-07-22-transactional-server-settings-persistence.md docs/superpowers/audits/2026-07-21-maintainability-architecture-refresh.md
git status --short --branch --untracked-files=no
git log --oneline --decorate -12
```

Verify the committed range changes only the four approved production files and three approved documents. Confirm the four tracked user modifications remain untouched. Do not inspect the backup directory.

- [ ] **Step 5: Commit the implementation record**

```bash
git add docs/superpowers/specs/2026-07-22-transactional-server-settings-persistence-design.md docs/superpowers/plans/2026-07-22-transactional-server-settings-persistence.md docs/superpowers/audits/2026-07-21-maintainability-architecture-refresh.md
git diff --cached --check
test "$(git diff --cached --name-only | wc -l)" = "3"
git commit -m "docs: record transactional server settings persistence"
```

- [ ] **Step 6: Hand off maintainer validation**

Ask maintainer Reaper176 to run the normal build/launch workflow and all thirteen cases from the approved design: valid/mixed edits; mixed invalid paths; unsupported fields; authorization lockout; locked APIs; storage failure/recovery; extension operations; IOPaint operations; startup/installation failure; concurrent mutations; model/runtime side effects; post-commit warning paths; and separate marker failure.

Do not mark rank 4 maintainer-validated or advance the roadmap until confirmation.

**Rollback:** retain the observable owner if any caller depends on it; do not restore false durable-success responses, partial `ChangeServerSettings` publication, destructive directory/environment compensation, or detailed exception transport.
