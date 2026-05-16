using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
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

    /// <summary>Gets the current setup status.</summary>
    public static LodestoneSetupStatus GetStatus()
    {
        bool isSetupRunning;
        string setupMessage;
        string pythonEnvPath;
        string pythonExePath;
        string modelPath;
        string vocabPath;
        lock (SetupLock)
        {
            isSetupRunning = IsSetupRunningInternal;
            setupMessage = SetupMessageInternal;
            pythonEnvPath = Path.Combine(DataRootInternal, "python_env");
            pythonExePath = Path.Combine(pythonEnvPath, RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "Scripts/python.exe" : "bin/python");
            modelPath = Path.Combine(DataRootInternal, "models", "tagger_proto.safetensors");
            vocabPath = Path.Combine(DataRootInternal, "models", "tagger_vocab_with_categories_and_alias_updated.json");
        }
        bool hasPythonEnv = File.Exists(pythonExePath);
        bool hasModelFile = IsExistingFileValid(modelPath);
        bool hasVocabFile = IsExistingFileValid(vocabPath);
        bool isReady = hasPythonEnv && hasModelFile && hasVocabFile;
        return new LodestoneSetupStatus()
        {
            IsReady = isReady,
            IsSetupRunning = isSetupRunning,
            HasPythonEnv = hasPythonEnv,
            HasModelFile = hasModelFile,
            HasVocabFile = hasVocabFile,
            Message = isSetupRunning && !string.IsNullOrWhiteSpace(setupMessage) ? setupMessage : isReady ? "Ready." : "Setup is required before first use."
        };
    }

    /// <summary>Runs local Lodestone dependency and model setup.</summary>
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
            SetSetupMessage("Starting Lodestone Image Interrogator setup.");
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
            SetSetupMessage("Validating Lodestone Python dependencies.");
            await RunProcessChecked(PythonExePath, ["-c", "import torch, safetensors, PIL, requests"], ExtensionRoot);
            SetSetupMessage("Downloading Lodestone model file. This is about 5.27 GB.");
            await DownloadIfMissing("https://huggingface.co/lodestones/taggerine/resolve/main/tagger_proto.safetensors", ModelPath);
            SetSetupMessage("Downloading Lodestone tag vocabulary.");
            await DownloadIfMissing("https://huggingface.co/lodestones/taggerine/resolve/main/tagger_vocab_with_categories_and_alias_updated.json", VocabPath);

            MarkSetupFinished();
            LodestoneSetupStatus status = GetStatus();
            status.Message = status.IsReady ? "Setup complete." : "Setup finished, but required files are still missing.";
            return status;
        }
        finally
        {
            MarkSetupFinished();
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
