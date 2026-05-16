using System.IO;
using System.Net.Http;
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
    public static string PythonEnvPath => $"{DataRoot}/python_env";

    /// <summary>Local Lodestone model file path.</summary>
    public static string ModelPath => $"{DataRoot}/models/tagger_proto.safetensors";

    /// <summary>Local Lodestone vocabulary file path.</summary>
    public static string VocabPath => $"{DataRoot}/models/tagger_vocab_with_categories_and_alias_updated.json";

    /// <summary>Gets the current setup status.</summary>
    public static LodestoneSetupStatus GetStatus()
    {
        bool isSetupRunning;
        string pythonEnvPath;
        string modelPath;
        string vocabPath;
        lock (SetupLock)
        {
            isSetupRunning = IsSetupRunningInternal;
            pythonEnvPath = $"{DataRootInternal}/python_env";
            modelPath = $"{DataRootInternal}/models/tagger_proto.safetensors";
            vocabPath = $"{DataRootInternal}/models/tagger_vocab_with_categories_and_alias_updated.json";
        }
        bool hasPythonEnv = Directory.Exists(pythonEnvPath);
        bool hasModelFile = File.Exists(modelPath);
        bool hasVocabFile = File.Exists(vocabPath);
        bool isReady = hasPythonEnv && hasModelFile && hasVocabFile;
        return new LodestoneSetupStatus()
        {
            IsReady = isReady,
            IsSetupRunning = isSetupRunning,
            HasPythonEnv = hasPythonEnv,
            HasModelFile = hasModelFile,
            HasVocabFile = hasVocabFile,
            Message = isReady ? "Ready." : "Setup is required before first use."
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
            Directory.CreateDirectory(DataRoot);
            Directory.CreateDirectory($"{DataRoot}/models");
            if (!Directory.Exists(PythonEnvPath))
            {
                await Utilities.QuickRunProcess("python3", ["-m", "venv", PythonEnvPath], ExtensionRoot);
            }

            string pythonExe = $"{PythonEnvPath}/bin/python";
            await Utilities.QuickRunProcess(pythonExe, ["-m", "pip", "install", "-r", "Runner/requirements.txt"], ExtensionRoot);
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

    /// <summary>Downloads a remote file to a local target path when it is not already present.</summary>
    private static async Task DownloadIfMissing(string url, string targetPath)
    {
        if (File.Exists(targetPath))
        {
            return;
        }

        string targetDirectory = Path.GetDirectoryName(targetPath);
        if (!string.IsNullOrWhiteSpace(targetDirectory))
        {
            Directory.CreateDirectory(targetDirectory);
        }

        using HttpClient client = new HttpClient();
        using HttpResponseMessage response = await client.GetAsync(url);
        response.EnsureSuccessStatusCode();
        await using Stream remote = await response.Content.ReadAsStreamAsync();
        await using FileStream local = File.Create(targetPath);
        await remote.CopyToAsync(local);
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
        }
    }
}
