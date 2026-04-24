using FreneticUtilities.FreneticDataSyntax;
using FreneticUtilities.FreneticExtensions;
using Newtonsoft.Json.Linq;
using SwarmUI.Accounts;
using SwarmUI.Backends;
using SwarmUI.Core;
using SwarmUI.Utils;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;

namespace SwarmUI.WebAPI;

[API.APIClass("API routes to manage the server's backends.")]
public class BackendAPI
{
    public static void Register()
    {
        API.RegisterAPICall(ListBackendTypes, false, Permissions.ViewBackendsList);
        API.RegisterAPICall(ListBackends, false, Permissions.ViewBackendsList);
        API.RegisterAPICall(GetIOPaintServiceStatus, false, Permissions.ViewBackendsList);
        API.RegisterAPICall(DeleteBackend, true, Permissions.AddRemoveBackends);
        API.RegisterAPICall(ToggleBackend, true, Permissions.ToggleBackends);
        API.RegisterAPICall(EditBackend, true, Permissions.EditBackends);
        API.RegisterAPICall(AddNewBackend, true, Permissions.AddRemoveBackends);
        API.RegisterAPICall(RestartBackends, true, Permissions.RestartBackends);
        API.RegisterAPICall(FreeBackendMemory, true, Permissions.ControlMemClean);
        API.RegisterAPICall(SaveIOPaintServiceSettings, true, Permissions.EditServerSettings);
        API.RegisterAPICall(InstallIOPaintService, true, Permissions.EditServerSettings);
        API.RegisterAPICall(UninstallIOPaintService, true, Permissions.EditServerSettings);
        API.RegisterAPICall(CreateNewIOPaintServiceInstall, true, Permissions.EditServerSettings);
    }

    public static string GetDefaultIOPaintRoot()
    {
        return Utilities.CombinePathWithAbsolute(Program.DataDir, "tools/iopaint");
    }

    public static string GetDefaultIOPaintVenvPath()
    {
        return Utilities.CombinePathWithAbsolute(GetDefaultIOPaintRoot(), "venv");
    }

    public static string GetDefaultIOPaintBootstrapPython()
    {
        string[] candidates =
        [
            GetPyenvPython311Path(),
            RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "python3.11" : "python3.11",
            RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "python" : "python3",
            "python"
        ];
        return candidates.FirstOrDefault(c => !string.IsNullOrWhiteSpace(c)) ?? "python3";
    }

    public static async Task<string> ResolveEffectiveIOPaintBootstrapPython(string configuredBootstrapPython, string workingDir)
    {
        string pyenvPython = GetPyenvPython311Path();
        if (!string.IsNullOrWhiteSpace(pyenvPython))
        {
            string pyenvVersion = await GetCommandPythonVersionString(pyenvPython, workingDir);
            if (IsPythonVersionCompatibleForIOPaint(pyenvVersion))
            {
                return pyenvPython;
            }
        }
        if (!string.IsNullOrWhiteSpace(configuredBootstrapPython))
        {
            string configuredVersion = await GetCommandPythonVersionString(configuredBootstrapPython, workingDir);
            if (IsPythonVersionCompatibleForIOPaint(configuredVersion) && IsExplicitPythonPath(configuredBootstrapPython))
            {
                return configuredBootstrapPython;
            }
        }
        string fallback = GetDefaultIOPaintBootstrapPython();
        if (string.IsNullOrWhiteSpace(fallback))
        {
            return configuredBootstrapPython;
        }
        return fallback;
    }

    public static string GetNextIOPaintVenvPath()
    {
        string root = GetDefaultIOPaintRoot();
        string firstPath = GetDefaultIOPaintVenvPath();
        if (!Directory.Exists(firstPath))
        {
            return firstPath;
        }
        for (int i = 2; i < 1000; i++)
        {
            string testPath = Utilities.CombinePathWithAbsolute(root, $"venv-{i}");
            if (!Directory.Exists(testPath))
            {
                return testPath;
            }
        }
        return Utilities.CombinePathWithAbsolute(root, $"venv-{Guid.NewGuid().ToString().Replace("-", "").ToLowerInvariant()}");
    }

    public static bool IsManagedIOPaintPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }
        string fullPath = Path.GetFullPath(path);
        string fullRoot = Path.GetFullPath(GetDefaultIOPaintRoot());
        return fullPath.StartsWith(fullRoot + Path.DirectorySeparatorChar) || fullPath == fullRoot;
    }

    public static string GetPyenvPython311Path()
    {
        string userHome = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (string.IsNullOrWhiteSpace(userHome))
        {
            return null;
        }
        string pyenvVersions = Path.Combine(userHome, ".pyenv/versions");
        if (!Directory.Exists(pyenvVersions))
        {
            return null;
        }
        string match = Directory.GetDirectories(pyenvVersions, "3.11*").OrderByDescending(d => d).FirstOrDefault();
        if (match is null)
        {
            return null;
        }
        string pythonPath = Path.Combine(match, "bin/python");
        return File.Exists(pythonPath) ? pythonPath : null;
    }

    public static string GetIOPaintPythonPath(Settings.IOPaintServiceData settings)
    {
        string venvPath = string.IsNullOrWhiteSpace(settings.VenvPath) ? GetDefaultIOPaintVenvPath() : settings.VenvPath;
        string subPath = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "Scripts/python.exe" : "bin/python";
        return Path.Combine(venvPath, subPath);
    }

    public static string GetIOPaintExePath(Settings.IOPaintServiceData settings)
    {
        string venvPath = string.IsNullOrWhiteSpace(settings.VenvPath) ? GetDefaultIOPaintVenvPath() : settings.VenvPath;
        string subPath = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "Scripts/iopaint.exe" : "bin/iopaint";
        return Path.Combine(venvPath, subPath);
    }

    public static async Task<string> GetPythonVersionString(string pythonPath, string workingDir)
    {
        if (string.IsNullOrWhiteSpace(pythonPath) || !File.Exists(pythonPath))
        {
            return null;
        }
        try
        {
            (int exitCode, string outputText) = await T2IAPI.RunProcessCapture(pythonPath, ["-c", "import sys; print(f'{sys.version_info.major}.{sys.version_info.minor}')"], workingDir);
            if (exitCode != 0)
            {
                return null;
            }
            return outputText.Trim();
        }
        catch
        {
            return null;
        }
    }

    public static async Task<string> GetCommandPythonVersionString(string pythonCommand, string workingDir)
    {
        if (string.IsNullOrWhiteSpace(pythonCommand))
        {
            return null;
        }
        try
        {
            (int exitCode, string outputText) = await T2IAPI.RunProcessCapture(pythonCommand, ["-c", "import sys; print(f'{sys.version_info.major}.{sys.version_info.minor}')"], workingDir);
            if (exitCode != 0)
            {
                return null;
            }
            return outputText.Trim();
        }
        catch
        {
            return null;
        }
    }

    public static bool IsPythonVersionCompatibleForIOPaint(string version)
    {
        if (string.IsNullOrWhiteSpace(version))
        {
            return false;
        }
        string[] parts = version.Split('.');
        if (parts.Length < 2 || !int.TryParse(parts[0], out int major) || !int.TryParse(parts[1], out int minor))
        {
            return false;
        }
        return major == 3 && minor >= 10 && minor <= 11;
    }

    public static bool IsExplicitPythonPath(string pythonCommand)
    {
        return !string.IsNullOrWhiteSpace(pythonCommand) && Path.IsPathRooted(pythonCommand);
    }

    public static async Task RunMonitoredProcess(string fileName, string[] args, string workingDir, string name, string identifier)
    {
        ProcessStartInfo start = new(fileName)
        {
            WorkingDirectory = workingDir
        };
        foreach (string arg in args)
        {
            start.ArgumentList.Add(arg);
        }
        await NetworkBackendUtils.RunProcessWithMonitoring(start, name, identifier);
    }

    public static async Task<JObject> BuildIOPaintServiceStatus()
    {
        Settings.IOPaintServiceData settings = Program.ServerSettings.IOPaint;
        string venvPath = string.IsNullOrWhiteSpace(settings.VenvPath) ? GetDefaultIOPaintVenvPath() : settings.VenvPath;
        string configuredBootstrapPython = string.IsNullOrWhiteSpace(settings.BootstrapPython) ? GetDefaultIOPaintBootstrapPython() : settings.BootstrapPython;
        string bootstrapPython = await ResolveEffectiveIOPaintBootstrapPython(configuredBootstrapPython, Path.GetDirectoryName(venvPath) ?? Program.DataDir);
        string pythonPath = GetIOPaintPythonPath(settings);
        string exePath = GetIOPaintExePath(settings);
        bool pythonExists = File.Exists(pythonPath);
        bool exeExists = File.Exists(exePath);
        string pythonVersion = pythonExists ? await GetPythonVersionString(pythonPath, Path.GetDirectoryName(pythonPath)) : null;
        bool pythonCompatible = IsPythonVersionCompatibleForIOPaint(pythonVersion);
        bool ready = false;
        string detail = exeExists ? "Installed." : "Not installed.";
        if (pythonExists && !pythonCompatible)
        {
            detail = $"Managed venv is using incompatible Python {pythonVersion}. Reinstall with Python 3.10 or 3.11.";
        }
        if (exeExists)
        {
            try
            {
                (int exitCode, string outputText) = await T2IAPI.RunProcessCapture(exePath, ["--help"], Path.GetDirectoryName(exePath));
                ready = exitCode == 0;
                if (!ready && !string.IsNullOrWhiteSpace(outputText))
                {
                    detail = outputText;
                }
            }
            catch (Exception ex)
            {
                detail = ex.Message;
            }
        }
        return new JObject()
        {
            ["enabled"] = settings.Enabled,
            ["venv_path"] = venvPath,
            ["bootstrap_python"] = bootstrapPython,
            ["python_path"] = pythonPath,
            ["python_version"] = pythonVersion ?? "",
            ["python_compatible"] = pythonCompatible,
            ["exe_path"] = exePath,
            ["device"] = settings.Device,
            ["model_cache_path"] = settings.ModelCachePath ?? "",
            ["python_exists"] = pythonExists,
            ["installed"] = exeExists,
            ["ready"] = ready,
            ["detail"] = detail
        };
    }

    [API.APIDescription("Returns status and configuration details for the managed IOPaint service.",
        """
            "enabled": true,
            "venv_path": "Data/tools/iopaint/venv",
            "bootstrap_python": "python3",
            "python_path": "Data/tools/iopaint/venv/bin/python",
            "exe_path": "Data/tools/iopaint/venv/bin/iopaint",
            "device": "cpu",
            "model_cache_path": "",
            "python_exists": true,
            "installed": true,
            "ready": true,
            "detail": "Installed."
        """)]
    public static async Task<JObject> GetIOPaintServiceStatus(Session session)
    {
        return await BuildIOPaintServiceStatus();
    }

    [API.APIDescription("Saves configuration for the managed IOPaint service.", "\"success\": true")]
    public static async Task<JObject> SaveIOPaintServiceSettings(Session session,
        [API.APIParameter("If true, enable the service.")] bool enabled,
        [API.APIParameter("Bootstrap python executable or path.")] string bootstrap_python,
        [API.APIParameter("Dedicated venv path.")] string venv_path,
        [API.APIParameter("Preferred device: cpu, cuda, or mps.")] string device,
        [API.APIParameter("Optional model cache path.")] string model_cache_path = "")
    {
        if (Program.LockSettings)
        {
            return new() { ["error"] = "Settings are locked." };
        }
        Settings.IOPaintServiceData settings = Program.ServerSettings.IOPaint;
        settings.Enabled = enabled;
        settings.BootstrapPython = bootstrap_python?.Trim() ?? "";
        settings.VenvPath = venv_path?.Trim() ?? "";
        settings.Device = string.IsNullOrWhiteSpace(device) ? "cpu" : device.Trim().ToLowerInvariant();
        settings.ModelCachePath = model_cache_path?.Trim() ?? "";
        Program.SaveSettingsFile();
        return await BuildIOPaintServiceStatus();
    }

    [API.APIDescription("Installs or reinstalls the managed IOPaint service into its dedicated virtual environment.", "\"success\": true")]
    public static async Task<JObject> InstallIOPaintService(Session session,
        [API.APIParameter("If true, reinstall even if an environment already exists.")] bool reinstall = false)
    {
        if (Program.LockSettings)
        {
            return new() { ["error"] = "Settings are locked." };
        }
        Settings.IOPaintServiceData settings = Program.ServerSettings.IOPaint;
        string venvPath = string.IsNullOrWhiteSpace(settings.VenvPath) ? GetDefaultIOPaintVenvPath() : settings.VenvPath;
        string rootPath = Path.GetDirectoryName(venvPath);
        if (string.IsNullOrWhiteSpace(rootPath))
        {
            return new() { ["error"] = "Invalid IOPaint venv path." };
        }
        string configuredBootstrapPython = string.IsNullOrWhiteSpace(settings.BootstrapPython) ? GetDefaultIOPaintBootstrapPython() : settings.BootstrapPython;
        string bootstrapPython = await ResolveEffectiveIOPaintBootstrapPython(configuredBootstrapPython, rootPath);
        string bootstrapVersion = await GetCommandPythonVersionString(bootstrapPython, rootPath);
        if (!IsPythonVersionCompatibleForIOPaint(bootstrapVersion))
        {
            return new()
            {
                ["error"] = $"Bootstrap interpreter '{bootstrapPython}' resolves to incompatible Python {bootstrapVersion ?? "unknown"}. Use an absolute Python 3.10 or 3.11 path."
            };
        }
        if (!IsExplicitPythonPath(bootstrapPython))
        {
            return new()
            {
                ["error"] = $"Bootstrap interpreter '{bootstrapPython}' is not an absolute path. Use an explicit Python 3.10 or 3.11 binary path to avoid shim/version mismatches."
            };
        }
        Directory.CreateDirectory(rootPath);
        if (reinstall && Directory.Exists(venvPath))
        {
            Directory.Delete(venvPath, true);
        }
        if (!Directory.Exists(venvPath) || !File.Exists(GetIOPaintPythonPath(settings)))
        {
            await RunMonitoredProcess(bootstrapPython, ["-m", "venv", venvPath], rootPath, "IOPaint Install (venv)", "iopaintinstall");
        }
        string pythonPath = GetIOPaintPythonPath(settings);
        string pythonVersion = await GetPythonVersionString(pythonPath, rootPath);
        if (!IsPythonVersionCompatibleForIOPaint(pythonVersion))
        {
            if (Directory.Exists(venvPath))
            {
                Directory.Delete(venvPath, true);
            }
            await RunMonitoredProcess(bootstrapPython, ["-m", "venv", venvPath], rootPath, "IOPaint Install (venv)", "iopaintinstall");
            pythonPath = GetIOPaintPythonPath(settings);
            pythonVersion = await GetPythonVersionString(pythonPath, rootPath);
            if (!IsPythonVersionCompatibleForIOPaint(pythonVersion))
            {
                return new()
                {
                    ["error"] = $"Managed IOPaint requires Python 3.10 or 3.11, but bootstrap interpreter created Python {pythonVersion ?? "unknown"}."
                };
            }
        }
        await RunMonitoredProcess(pythonPath, ["-m", "pip", "install", "--upgrade", "pip", "setuptools", "wheel"], rootPath, "IOPaint Install (pip)", "iopaintinstall");
        await RunMonitoredProcess(pythonPath, ["-m", "pip", "install", "iopaint"], rootPath, "IOPaint Install (iopaint)", "iopaintinstall");
        settings.VenvPath = venvPath;
        settings.BootstrapPython = bootstrapPython;
        settings.Enabled = true;
        Program.SaveSettingsFile();
        return await BuildIOPaintServiceStatus();
    }

    [API.APIDescription("Deletes the current managed IOPaint install if it is under the Swarm-managed tools directory.", "\"success\": true")]
    public static async Task<JObject> UninstallIOPaintService(Session session)
    {
        if (Program.LockSettings)
        {
            return new() { ["error"] = "Settings are locked." };
        }
        Settings.IOPaintServiceData settings = Program.ServerSettings.IOPaint;
        string venvPath = string.IsNullOrWhiteSpace(settings.VenvPath) ? GetDefaultIOPaintVenvPath() : settings.VenvPath;
        if (!IsManagedIOPaintPath(venvPath))
        {
            return new() { ["error"] = "Refusing to delete a non-managed IOPaint path." };
        }
        if (Directory.Exists(venvPath))
        {
            Directory.Delete(venvPath, true);
        }
        settings.Enabled = false;
        Program.SaveSettingsFile();
        return await BuildIOPaintServiceStatus();
    }

    [API.APIDescription("Switches the managed IOPaint service to a new dedicated install path under the Swarm-managed tools directory.", "\"success\": true")]
    public static async Task<JObject> CreateNewIOPaintServiceInstall(Session session)
    {
        if (Program.LockSettings)
        {
            return new() { ["error"] = "Settings are locked." };
        }
        Settings.IOPaintServiceData settings = Program.ServerSettings.IOPaint;
        settings.VenvPath = GetNextIOPaintVenvPath();
        settings.Enabled = false;
        Program.SaveSettingsFile();
        return await BuildIOPaintServiceStatus();
    }

    [API.APIDescription("Returns of a list of all available backend types.",
        """
            "list":
            [
                "id": "idhere",
                "name": "namehere",
                "description": "descriptionhere",
                "settings":
                [
                    {
                        "name": "namehere",
                        "type": "typehere",
                        "description": "descriptionhere",
                        "placeholder": "placeholderhere",
                        "values": ["a", "b"], // For dropdowns only
                        "value_names": ["Alpha", "Beta"] // For dropdowns only, optional even then
                    }
                ],
                "is_standard": false
            ]
        """)]
    public static async Task<JObject> ListBackendTypes(Session session)
    {
        return new() { ["list"] = JToken.FromObject(Program.Backends.BackendTypes.Values.Select(b => b.NetDescription).ToList()) };
    }

    /// <summary>Create a network object to represent a backend cleanly.</summary>
    public static JObject BackendToNet(BackendHandler.BackendData backend, bool full = false)
    {
        long timeLastRelease = backend.TimeLastRelease;
        long timeSinceUsed = timeLastRelease == 0 ? 0 : (Environment.TickCount64 - timeLastRelease) / 1000;
        JObject data = new()
        {
            ["type"] = backend.AbstractBackend.HandlerTypeData.ID,
            ["status"] = backend.AbstractBackend.Status.ToString().ToLowerFast(),
            ["id"] = backend.ID,
            ["settings"] = JToken.FromObject(backend.AbstractBackend.SettingsRaw.SaveAllWithoutSecretValues("\t<secret>", "").ToSimple()),
            ["modcount"] = backend.ModCount,
            ["features"] = new JArray(backend.AbstractBackend.SupportedFeatures.ToArray()),
            ["enabled"] = backend.AbstractBackend.IsEnabled,
            ["title"] = backend.AbstractBackend.Title,
            ["max_usages"] = backend.AbstractBackend.MaxUsages,
            ["seconds_since_used"] = timeSinceUsed,
            ["time_since_used"] = timeLastRelease == 0 ? "Never" : TimeSpan.FromSeconds(-timeSinceUsed).SimpleFormat(true, false)
        };
        if (backend is BackendHandler.T2IBackendData t2i)
        {
            data["can_load_models"] = t2i.Backend.CanLoadModels;
            if (full)
            {
                data["current_model"] = t2i.Backend.CurrentModelName;
            }
        }
        return data;
    }

    [API.APIDescription("Shuts down and deletes a registered backend by ID.",
        """
            "result": "Deleted."
            // OR
            "result": "Already didn't exist."
        """)]
    public static async Task<JObject> DeleteBackend(Session session,
        [API.APIParameter("ID of the backend to delete.")] int backend_id)
    {
        Logs.Warning($"User {session.User.UserID} requested delete of backend {backend_id}.");
        if (Program.LockSettings)
        {
            return new() { ["error"] = "Settings are locked." };
        }
        if (await Program.Backends.DeleteById(backend_id))
        {
            return new JObject() { ["result"] = "Deleted." };
        }
        return new JObject() { ["result"] = "Already didn't exist." };
    }

    [API.APIDescription("Disables or re-enables a backend by ID.",
        """
            "result": "Success."
            // OR
            "result": "No change."
        """)]
    public static async Task<JObject> ToggleBackend(Session session,
        [API.APIParameter("ID of the backend to toggle.")] int backend_id,
        [API.APIParameter("If true, backend should be enabled. If false, backend should be disabled.")] bool enabled)
    {
        Logs.Warning($"User {session.User.UserID} requested toggle of backend {backend_id}, enabled={enabled}.");
        if (Program.LockSettings)
        {
            return new() { ["error"] = "Settings are locked." };
        }
        if (!Program.Backends.AllBackends.TryGetValue(backend_id, out BackendHandler.BackendData backend))
        {
            return new() { ["error"] = $"Invalid backend ID {backend_id}" };
        }
        if (backend.AbstractBackend.IsEnabled == enabled)
        {
            return new JObject() { ["result"] = "No change." };
        }
        backend.AbstractBackend.IsEnabled = enabled;
        backend.AbstractBackend.ShutDownReserve = true;
        Program.Backends.BackendsEdited = true;
        while (backend.CheckIsInUse && backend.AbstractBackend.MaxUsages > 0)
        {
            if (Program.GlobalProgramCancel.IsCancellationRequested)
            {
                return null;
            }
            await Task.Delay(TimeSpan.FromSeconds(0.5));
        }
        if (backend.AbstractBackend.Status != BackendStatus.DISABLED && backend.AbstractBackend.Status != BackendStatus.ERRORED)
        {
            await backend.AbstractBackend.DoShutdownNow();
        }
        if (enabled)
        {
            backend.AbstractBackend.Status = BackendStatus.WAITING;
            Program.Backends.BackendsToInit.Enqueue(backend);
        }
        backend.AbstractBackend.ShutDownReserve = false;
        return new JObject() { ["result"] = "Success." };
    }

    [API.APIDescription("Modify and re-init an already registered backend.",
        """
            "id": "idhere",
            "type": "typehere",
            "status": "statushere",
            "settings":
            {
                "namehere": valuehere
            },
            "modcount": 0,
            "features": [ "featureidhere", ... ],
            "enabled": true,
            "title": "titlehere",
            "can_load_models": true,
            "max_usages": 0
        """)]
    public static async Task<JObject> EditBackend(Session session,
        [API.APIParameter("ID of the backend to edit.")] int backend_id,
        [API.APIParameter("New title of the backend.")] string title,
        [API.APIParameter(" Input should contain a map of `\"settingname\": value`.")] JObject raw_inp,
        [API.APIParameter("Optional new ID to change the backend to.")] int new_id = -1)
    {
        Logs.Warning($"User {session.User.UserID} requested edit of backend {backend_id}.");
        if (Program.LockSettings)
        {
            return new() { ["error"] = "Settings are locked." };
        }
        if (!raw_inp.TryGetValue("settings", out JToken jval) || jval is not JObject settings)
        {
            return new() { ["error"] = "Missing settings." };
        }
        if (new_id == backend_id)
        {
            new_id = -1;
        }
        if (new_id >= 0 && Program.Backends.AllBackends.ContainsKey(new_id))
        {
            return new() { ["error"] = $"Backend ID {new_id} is already in use." };
        }
        FDSSection parsed = FDSSection.FromSimple(settings.ToBasicObject());
        Logs.Verbose($"New settings to apply: {parsed}");
        BackendHandler.BackendData result = await Program.Backends.EditById(backend_id, parsed, title, new_id);
        if (result is null)
        {
            return new() { ["error"] = $"Invalid backend ID {backend_id}" };
        }
        return BackendToNet(result);
    }

    [API.APIDescription("Returns a list of currently registered backends.",
        """
            "idhere":
            {
                "id": "idhere",
                "type": "typehere",
                "status": "statushere",
                "settings":
                {
                    "namehere": valuehere
                },
                "modcount": 0,
                "features": [ "featureidhere", ... ],
                "enabled": true,
                "title": "titlehere",
                "can_load_models": true,
                "max_usages": 0,
                "current_model": "modelnamehere" // Only if `full_data` is true
            }
        """)]
    public static async Task<JObject> ListBackends(Session session,
        [API.APIParameter("If true, include 'nonreal' backends (ones that were spawned temporarily/internally).")] bool nonreal = false,
        [API.APIParameter("If true, include nonessential data about backends (eg what model is currently loaded).")] bool full_data = false)
    {
        JObject toRet = [];
        foreach (BackendHandler.BackendData data in Program.Backends.AllBackends.Values.OrderBy(d => d.ID))
        {
            if (!data.AbstractBackend.IsReal && !nonreal)
            {
                continue;
            }
            toRet[data.ID.ToString()] = BackendToNet(data, full_data);
        }
        return toRet;
    }

    [API.APIDescription("Add a new backend of the specified type.",
        """
            "id": "idhere",
            "type": "typehere",
            "status": "statushere",
            "settings":
            {
                "namehere": valuehere
            },
            "modcount": 0,
            "features": [ "featureidhere", ... ],
            "enabled": true,
            "title": "titlehere",
            "can_load_models": true,
            "max_usages": 0
        """)]
    public static async Task<JObject> AddNewBackend(Session session,
        [API.APIParameter("ID of what type of backend to add (see `ListBackendTypes`).")] string type_id)
    {
        Logs.Warning($"User {session.User.UserID} requested add-new-backend of type {type_id}.");
        if (Program.LockSettings)
        {
            return new() { ["error"] = "Settings are locked." };
        }
        if (!Program.Backends.BackendTypes.TryGetValue(type_id, out BackendHandler.BackendType type))
        {
            return new() { ["error"] = $"Invalid backend type: {type_id}" };
        }
        BackendHandler.BackendData data = Program.Backends.AddNewOfType(type);
        return BackendToNet(data);
    }

    [API.APIDescription("Restart all backends or a specific one.",
        """
            "result": "Success.",
            "count": 1 // Number of backends restarted
        """)]
    public static async Task<JObject> RestartBackends(Session session,
        [API.APIParameter("What backend ID to restart, or `all` for all.")] string backend = "all")
    {
        Logs.Warning($"User {session.User.UserID} requested restart of backend {backend}.");
        if (Program.LockSettings)
        {
            return new() { ["error"] = "Settings are locked." };
        }
        int count = 0;
        foreach (BackendHandler.BackendData data in Program.Backends.AllBackends.Values)
        {
            if (backend != "all" && backend != $"{data.ID}")
            {
                continue;
            }
            if (data.AbstractBackend.Status == BackendStatus.RUNNING || data.AbstractBackend.Status == BackendStatus.ERRORED)
            {
                await Program.Backends.ShutdownBackendCleanly(data);
                Program.Backends.DoInitBackend(data);
                count++;
            }
        }
        return new JObject() { ["result"] = "Success.", ["count"] = count };
    }

    [API.APIDescription("Free memory from all backends or a specific one.",
        """
            "result": true,
            "count": 1 // Number of backends memory was freed from
        """)]
    public static async Task<JObject> FreeBackendMemory(Session session,
        [API.APIParameter("If true, system RAM should be cleared too. If false, only VRAM should be cleared.")] bool system_ram = false,
        [API.APIParameter("What backend ID to restart, or `all` for all.")] string backend = "all")
    {
        if (system_ram)
        {
            Session.RecentlyBlockedFilenames.Clear();
        }
        List<Task<bool>> tasks = [];
        foreach (AbstractBackend target in Program.Backends.RunningBackendsOfType<AbstractBackend>())
        {
            if (backend != "all" && backend != $"{target.AbstractBackendData.ID}")
            {
                continue;
            }
            tasks.Add(target.FreeMemory(system_ram));
        }
        if (tasks.IsEmpty())
        {
            return new JObject() { ["result"] = false, ["count"] = 0 };
        }
        await Task.WhenAll(tasks);
        Utilities.CleanRAM();
        return new JObject() { ["result"] = true, ["count"] = tasks.Where(t => t.Result).Count() };
    }
}
