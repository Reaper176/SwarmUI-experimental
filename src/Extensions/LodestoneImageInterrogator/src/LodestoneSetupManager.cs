using System.IO;

namespace LodestoneImageInterrogatorExtension;

/// <summary>Tracks setup paths and status for the Lodestone Image Interrogator extension.</summary>
public static class LodestoneSetupManager
{
    /// <summary>Lock used to guard setup state transitions.</summary>
    private static readonly object SetupLock = new object();

    /// <summary>Whether setup is currently running.</summary>
    public static bool IsSetupRunning;

    /// <summary>Root folder for the Lodestone Image Interrogator extension.</summary>
    public static string ExtensionRoot => "src/Extensions/LodestoneImageInterrogator";

    /// <summary>Data folder for local Lodestone setup artifacts.</summary>
    public static string DataRoot => $"{ExtensionRoot}/Data";

    /// <summary>Local Python virtual environment path.</summary>
    public static string PythonEnvPath => $"{DataRoot}/python_env";

    /// <summary>Local Lodestone model file path.</summary>
    public static string ModelPath => $"{DataRoot}/models/tagger_proto.safetensors";

    /// <summary>Local Lodestone vocabulary file path.</summary>
    public static string VocabPath => $"{DataRoot}/models/tagger_vocab_with_categories_and_alias_updated.json";

    /// <summary>Gets the current setup status.</summary>
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

    /// <summary>Attempts to mark setup as running.</summary>
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

    /// <summary>Marks setup as finished.</summary>
    public static void MarkSetupFinished()
    {
        lock (SetupLock)
        {
            IsSetupRunning = false;
        }
    }
}
