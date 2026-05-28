using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using FreneticUtilities.FreneticExtensions;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using SwarmUI.Core;
using SwarmUI.Utils;

namespace LodestoneImageInterrogatorExtension;

/// <summary>Tracks setup paths and status for the Lodestone Image Interrogator extension.</summary>
public static class LodestoneSetupManager
{
    /// <summary>Lock used to guard setup state transitions.</summary>
    private static readonly object SetupLock = new object();

    /// <summary>Whether setup is currently running.</summary>
    private static bool IsSetupRunningInternal;

    /// <summary>Current setup progress message.</summary>
    private static string SetupMessageInternal = "";

    /// <summary>Current setup marker version. Increment when setup requirements change.</summary>
    private const string SetupMarkerVersion = "4";

    /// <summary>Automatic backend selection value.</summary>
    public const string BackendAuto = "auto";

    /// <summary>NVIDIA CUDA backend selection value.</summary>
    public const string BackendCuda = "cuda";

    /// <summary>AMD ROCm backend selection value.</summary>
    public const string BackendRocm = "rocm";

    /// <summary>CPU backend selection value.</summary>
    public const string BackendCpu = "cpu";

    /// <summary>PyTorch CUDA wheel index for NVIDIA GPU installs.</summary>
    private const string CudaWheelIndex = "https://download.pytorch.org/whl/cu130";

    /// <summary>PyTorch ROCm wheel index for Linux AMD GPU installs.</summary>
    private const string RocmWheelIndex = "https://download.pytorch.org/whl/rocm7.1";

    /// <summary>PyTorch CPU wheel index.</summary>
    private const string CpuWheelIndex = "https://download.pytorch.org/whl/cpu";

    /// <summary>Root folder for the Lodestone Image Interrogator extension source.</summary>
    private static string ExtensionRootInternal = "";

    /// <summary>Root folder for Lodestone runner files.</summary>
    private static string RunnerRootInternal = "";

    /// <summary>Data folder for local Lodestone setup artifacts.</summary>
    private static string DataRootInternal = "";

    /// <summary>Whether setup is currently running.</summary>
    public static bool IsSetupRunning
    {
        get
        {
            lock (SetupLock)
            {
                return IsSetupRunningInternal;
            }
        }
    }

    /// <summary>Initializes extension and runtime data paths.</summary>
    public static void Initialize(string extensionRoot)
    {
        lock (SetupLock)
        {
            ExtensionRootInternal = Path.GetFullPath(extensionRoot);
            RunnerRootInternal = ExtensionRootInternal;
            DataRootInternal = Utilities.CombinePathWithAbsolute(Program.DataDir, "LodestoneImageInterrogator");
        }
    }

    /// <summary>Root folder for the Lodestone Image Interrogator extension.</summary>
    public static string ExtensionRoot
    {
        get
        {
            lock (SetupLock)
            {
                return ExtensionRootInternal;
            }
        }
    }

    /// <summary>Root folder used as the working directory for Lodestone runner scripts.</summary>
    public static string RunnerRoot
    {
        get
        {
            lock (SetupLock)
            {
                return RunnerRootInternal;
            }
        }
    }

    /// <summary>Data folder for local Lodestone setup artifacts.</summary>
    public static string DataRoot
    {
        get
        {
            lock (SetupLock)
            {
                return DataRootInternal;
            }
        }
    }

    /// <summary>Local Python virtual environment path.</summary>
    public static string PythonEnvPath => Path.Combine(DataRoot, "python_env");

    /// <summary>Local Python executable path.</summary>
    public static string PythonExePath => Path.Combine(PythonEnvPath, RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "Scripts/python.exe" : "bin/python");

    /// <summary>Local Lodestone model file path.</summary>
    public static string ModelPath => Path.Combine(DataRoot, "models", "tagger_proto.safetensors");

    /// <summary>Local Lodestone vocabulary file path.</summary>
    public static string VocabPath => Path.Combine(DataRoot, "models", "tagger_vocab_with_categories_and_alias_updated.json");

    /// <summary>Local setup marker file path.</summary>
    public static string SetupMarkerPath => Path.Combine(DataRoot, "setup_version.txt");

    /// <summary>Gets the current setup status.</summary>
    public static LodestoneSetupStatus GetStatus()
    {
        bool isSetupRunning;
        string setupMessage;
        string pythonEnvPath;
        string pythonExePath;
        string modelPath;
        string vocabPath;
        string setupMarkerPath;
        lock (SetupLock)
        {
            isSetupRunning = IsSetupRunningInternal;
            setupMessage = SetupMessageInternal;
            pythonEnvPath = Path.Combine(DataRootInternal, "python_env");
            pythonExePath = Path.Combine(pythonEnvPath, RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "Scripts/python.exe" : "bin/python");
            modelPath = Path.Combine(DataRootInternal, "models", "tagger_proto.safetensors");
            vocabPath = Path.Combine(DataRootInternal, "models", "tagger_vocab_with_categories_and_alias_updated.json");
            setupMarkerPath = Path.Combine(DataRootInternal, "setup_version.txt");
        }
        bool hasPythonEnv = File.Exists(pythonExePath);
        bool hasModelFile = IsExistingFileValid(modelPath);
        bool hasVocabFile = IsExistingFileValid(vocabPath);
        bool hasSetupMarker = HasCurrentSetupMarker(setupMarkerPath);
        string backend = hasSetupMarker ? ReadSetupBackend(setupMarkerPath) : "";
        bool isReady = hasPythonEnv && hasModelFile && hasVocabFile && hasSetupMarker;
        return new LodestoneSetupStatus()
        {
            IsReady = isReady,
            IsSetupRunning = isSetupRunning,
            HasPythonEnv = hasPythonEnv,
            HasModelFile = hasModelFile,
            HasVocabFile = hasVocabFile,
            HasSetupMarker = hasSetupMarker,
            Backend = backend,
            BackendName = GetBackendDisplayName(backend),
            Message = isSetupRunning && !string.IsNullOrWhiteSpace(setupMessage) ? setupMessage : isReady ? $"Ready ({GetBackendDisplayName(backend)})." : BuildSetupRequiredMessage(hasPythonEnv, hasModelFile, hasVocabFile, hasSetupMarker)
        };
    }

    /// <summary>Runs local Lodestone dependency and model setup.</summary>
    public static async Task<LodestoneSetupStatus> RunSetup(string backend)
    {
        string normalizedBackend = NormalizeBackend(backend);
        if (!TryMarkSetupRunning())
        {
            LodestoneSetupStatus runningStatus = GetStatus();
            runningStatus.Message = "Setup is already running.";
            return runningStatus;
        }

        try
        {
            SetSetupMessage($"Starting Lodestone Image Interrogator setup for {GetBackendDisplayName(normalizedBackend)}.");
            if (normalizedBackend == BackendRocm && !RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            {
                throw new InvalidOperationException("Lodestone AMD ROCm setup is only supported on Linux.");
            }
            Directory.CreateDirectory(DataRoot);
            Directory.CreateDirectory(Path.Combine(DataRoot, "models"));
            if (!File.Exists(PythonExePath))
            {
                SetSetupMessage("Creating Lodestone Python environment.");
                string bootstrapPython = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "python" : "python3";
                await RunProcessChecked(bootstrapPython, ["-m", "venv", PythonEnvPath], ExtensionRoot);
            }

            SetSetupMessage("Installing Lodestone Python dependencies.");
            await RunProcessChecked(PythonExePath, ["-m", "pip", "install", "-r", "Runner/requirements.txt"], ExtensionRoot);
            await InstallTorchForBackend(normalizedBackend);
            await ValidateBackend(normalizedBackend);
            SetSetupMessage("Downloading Lodestone model file. This is about 5.27 GB.");
            await DownloadIfMissing("https://huggingface.co/lodestones/taggerine/resolve/main/tagger_proto.safetensors", ModelPath);
            SetSetupMessage("Downloading Lodestone tag vocabulary.");
            await DownloadIfMissing("https://huggingface.co/lodestones/taggerine/resolve/main/tagger_vocab_with_categories_and_alias_updated.json", VocabPath);
            await WriteSetupMarker(normalizedBackend);

            MarkSetupFinished();
            LodestoneSetupStatus status = GetStatus();
            status.Message = status.IsReady ? $"Setup complete for {GetBackendDisplayName(normalizedBackend)}." : "Setup finished, but required files are still missing.";
            return status;
        }
        finally
        {
            MarkSetupFinished();
        }
    }

    /// <summary>Returns the runtime device argument for the currently installed backend.</summary>
    public static string GetRuntimeDevice()
    {
        string backend = ReadSetupBackend(SetupMarkerPath);
        if (backend == BackendCpu)
        {
            return BackendCpu;
        }
        if (backend == BackendAuto)
        {
            return BackendAuto;
        }
        return "cuda";
    }

    /// <summary>Installs PyTorch packages for the selected backend.</summary>
    private static async Task InstallTorchForBackend(string backend)
    {
        if (backend == BackendCuda)
        {
            SetSetupMessage("Installing NVIDIA CUDA PyTorch wheels.");
            await RunProcessChecked(PythonExePath, ["-m", "pip", "install", "--upgrade", "--force-reinstall", "--index-url", CudaWheelIndex, "torch", "torchvision"], ExtensionRoot);
            return;
        }
        if (backend == BackendRocm)
        {
            SetSetupMessage("Installing AMD ROCm PyTorch wheels.");
            await RunProcessChecked(PythonExePath, ["-m", "pip", "install", "--upgrade", "--force-reinstall", "--index-url", RocmWheelIndex, "torch", "torchvision"], ExtensionRoot);
            return;
        }
        if (backend == BackendCpu)
        {
            SetSetupMessage("Installing CPU PyTorch wheels.");
            await RunProcessChecked(PythonExePath, ["-m", "pip", "install", "--upgrade", "--force-reinstall", "--index-url", CpuWheelIndex, "torch", "torchvision"], ExtensionRoot);
            return;
        }
        SetSetupMessage("Installing default PyTorch wheels.");
        await RunProcessChecked(PythonExePath, ["-m", "pip", "install", "--upgrade", "torch", "torchvision"], ExtensionRoot);
    }

    /// <summary>Validates PyTorch and the selected backend after package installation.</summary>
    private static async Task ValidateBackend(string backend)
    {
        if (backend == BackendCuda)
        {
            SetSetupMessage("Validating NVIDIA CUDA PyTorch dependencies.");
            await RunProcessChecked(PythonExePath, ["-c", "import torch, packaging, safetensors, PIL, requests; assert torch.cuda.is_available(), 'CUDA GPU is not available to PyTorch'; assert torch.version.cuda, 'PyTorch is not a CUDA build'; print(torch.cuda.get_device_name(0)); print(torch.version.cuda)"], ExtensionRoot);
            return;
        }
        if (backend == BackendRocm)
        {
            SetSetupMessage("Validating AMD ROCm PyTorch dependencies.");
            await RunProcessChecked(PythonExePath, ["-c", "import torch, packaging, safetensors, PIL, requests; assert torch.cuda.is_available(), 'ROCm GPU is not available to PyTorch'; assert torch.version.hip, 'PyTorch is not a ROCm/HIP build'; print(torch.cuda.get_device_name(0)); print(torch.version.hip)"], ExtensionRoot);
            return;
        }
        if (backend == BackendCpu)
        {
            SetSetupMessage("Validating CPU PyTorch dependencies.");
            await RunProcessChecked(PythonExePath, ["-c", "import torch, packaging, safetensors, PIL, requests; assert not torch.version.cuda and not torch.version.hip, 'Expected CPU PyTorch build'; print(torch.__version__)"], ExtensionRoot);
            return;
        }
        SetSetupMessage("Validating default PyTorch dependencies.");
        await RunProcessChecked(PythonExePath, ["-c", "import torch, packaging, safetensors, PIL, requests; print(torch.__version__); print(torch.cuda.get_device_name(0) if torch.cuda.is_available() else 'cpu')"], ExtensionRoot);
    }

    /// <summary>Normalizes a backend value from the API or marker file.</summary>
    public static string NormalizeBackend(string backend)
    {
        string normalized = string.IsNullOrWhiteSpace(backend) ? BackendAuto : backend.Trim().ToLowerFast();
        if (normalized == BackendCuda || normalized == BackendRocm || normalized == BackendCpu || normalized == BackendAuto)
        {
            return normalized;
        }
        return BackendAuto;
    }

    /// <summary>Returns a user-visible backend name.</summary>
    public static string GetBackendDisplayName(string backend)
    {
        string normalized = NormalizeBackend(backend);
        if (normalized == BackendCuda)
        {
            return "NVIDIA CUDA";
        }
        if (normalized == BackendRocm)
        {
            return "AMD ROCm";
        }
        if (normalized == BackendCpu)
        {
            return "CPU";
        }
        return "Auto / Existing PyTorch";
    }

    /// <summary>Writes the current setup marker after successful dependency setup.</summary>
    private static async Task WriteSetupMarker(string backend)
    {
        JObject marker = new JObject()
        {
            ["version"] = SetupMarkerVersion,
            ["backend"] = NormalizeBackend(backend)
        };
        await File.WriteAllTextAsync(SetupMarkerPath, marker.ToString(), Program.GlobalProgramCancel);
    }

    /// <summary>Reads the backend stored in the setup marker.</summary>
    public static string ReadSetupBackend(string markerPath)
    {
        if (!File.Exists(markerPath))
        {
            return "";
        }
        try
        {
            string markerText = File.ReadAllText(markerPath).Trim();
            if (markerText.StartsWith('{'))
            {
                JObject marker = JObject.Parse(markerText);
                string version = $"{marker["version"]}";
                string backend = $"{marker["backend"]}";
                if (version == SetupMarkerVersion)
                {
                    return NormalizeBackend(backend);
                }
                return "";
            }
            if (markerText == SetupMarkerVersion)
            {
                return BackendAuto;
            }
            return "";
        }
        catch (IOException)
        {
            return "";
        }
        catch (JsonException)
        {
            return "";
        }
    }

    /// <summary>Sets and logs current setup progress.</summary>
    private static void SetSetupMessage(string message)
    {
        lock (SetupLock)
        {
            SetupMessageInternal = message;
        }
        Logs.Info($"Lodestone Image Interrogator setup: {message}");
    }

    /// <summary>Builds a concise reason for setup being required.</summary>
    private static string BuildSetupRequiredMessage(bool hasPythonEnv, bool hasModelFile, bool hasVocabFile, bool hasSetupMarker)
    {
        if (!hasPythonEnv)
        {
            return "Setup is required before first use.";
        }
        if (!hasSetupMarker)
        {
            return "Setup is required to update Lodestone Python dependencies.";
        }
        if (!hasModelFile || !hasVocabFile)
        {
            return "Setup is required to download missing Lodestone model files.";
        }
        return "Setup is required before first use.";
    }

    /// <summary>Returns whether the dependency setup marker matches this extension version.</summary>
    private static bool HasCurrentSetupMarker(string markerPath)
    {
        return !string.IsNullOrWhiteSpace(ReadSetupBackend(markerPath));
    }

    /// <summary>Downloads a remote file to a local target path when it is not already present.</summary>
    private static async Task DownloadIfMissing(string url, string targetPath)
    {
        if (IsExistingFileValid(targetPath))
        {
            return;
        }

        string targetDirectory = Path.GetDirectoryName(targetPath);
        if (string.IsNullOrWhiteSpace(targetDirectory))
        {
            throw new IOException($"Target path '{targetPath}' does not include a valid target directory.");
        }
        Directory.CreateDirectory(targetDirectory);

        string tempPath = Path.Combine(targetDirectory, $"{Path.GetFileName(targetPath)}.{Guid.NewGuid()}.tmp");
        try
        {
            using HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Get, url);
            using HttpResponseMessage response = await Utilities.DownloaderWebClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, Program.GlobalProgramCancel);
            response.EnsureSuccessStatusCode();
            long? expectedLength = response.Content.Headers.ContentLength;
            long copiedBytes = 0;
            await using Stream remote = await response.Content.ReadAsStreamAsync(Program.GlobalProgramCancel);
            await using FileStream local = File.Create(tempPath);
            byte[] buffer = new byte[1024 * 1024];
            while (true)
            {
                int read = await remote.ReadAsync(buffer, Program.GlobalProgramCancel);
                if (read == 0)
                {
                    break;
                }
                await local.WriteAsync(buffer.AsMemory(0, read), Program.GlobalProgramCancel);
                copiedBytes += read;
            }
            if (expectedLength.HasValue && copiedBytes != expectedLength.Value)
            {
                throw new IOException($"Download size mismatch for {Path.GetFileName(targetPath)}: expected {expectedLength.Value} bytes, copied {copiedBytes} bytes.");
            }
            if (!IsDownloadedFileValid(tempPath, targetPath))
            {
                throw new IOException($"Downloaded file {Path.GetFileName(targetPath)} is missing or too small.");
            }
            File.Move(tempPath, targetPath, true);
        }
        catch
        {
            if (File.Exists(tempPath))
            {
                File.Delete(tempPath);
            }
            throw;
        }
    }

    /// <summary>Runs a process and throws if it fails.</summary>
    private static async Task RunProcessChecked(string process, string[] args, string workingDirectory)
    {
        ProcessStartInfo start = new ProcessStartInfo()
        {
            FileName = process,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };
        if (!string.IsNullOrWhiteSpace(workingDirectory))
        {
            start.WorkingDirectory = workingDirectory;
        }
        foreach (string arg in args)
        {
            start.ArgumentList.Add(arg);
        }
        Process proc = Process.Start(start);
        if (proc is null)
        {
            throw new InvalidOperationException($"Failed to start process '{process}'.");
        }
        Task<string> stdoutTask = proc.StandardOutput.ReadToEndAsync(Program.GlobalProgramCancel);
        Task<string> stderrTask = proc.StandardError.ReadToEndAsync(Program.GlobalProgramCancel);
        await proc.WaitForExitAsync(Program.GlobalProgramCancel);
        string stdout = await stdoutTask;
        string stderr = await stderrTask;
        if (proc.ExitCode != 0)
        {
            throw new InvalidOperationException($"Process '{process}' exited with code {proc.ExitCode}.\nSTDOUT:\n{stdout}\nSTDERR:\n{stderr}");
        }
    }

    /// <summary>Returns whether an existing target file meets the minimum expected size for its filename.</summary>
    private static bool IsExistingFileValid(string targetPath)
    {
        if (!File.Exists(targetPath))
        {
            return false;
        }
        return new FileInfo(targetPath).Length > GetMinimumValidSize(targetPath);
    }

    /// <summary>Returns whether a downloaded temp file meets the minimum expected size for its final target path.</summary>
    private static bool IsDownloadedFileValid(string tempPath, string targetPath)
    {
        if (!File.Exists(tempPath))
        {
            return false;
        }
        return new FileInfo(tempPath).Length > GetMinimumValidSize(targetPath);
    }

    /// <summary>Returns the minimum valid byte size for a Lodestone setup target file.</summary>
    private static long GetMinimumValidSize(string targetPath)
    {
        string fileName = Path.GetFileName(targetPath);
        if (fileName == "tagger_proto.safetensors")
        {
            return 1024L * 1024L * 1024L;
        }
        if (fileName == "tagger_vocab_with_categories_and_alias_updated.json")
        {
            return 100L;
        }
        return 0L;
    }

    /// <summary>Attempts to mark setup as running.</summary>
    public static bool TryMarkSetupRunning()
    {
        lock (SetupLock)
        {
            if (IsSetupRunningInternal)
            {
                return false;
            }
            IsSetupRunningInternal = true;
            return true;
        }
    }

    /// <summary>Marks setup as finished.</summary>
    public static void MarkSetupFinished()
    {
        lock (SetupLock)
        {
            IsSetupRunningInternal = false;
            SetupMessageInternal = "";
        }
    }
}
