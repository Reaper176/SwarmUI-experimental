using System.IO;
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
