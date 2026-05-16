using Newtonsoft.Json.Linq;

namespace LodestoneImageInterrogatorExtension;

/// <summary>Setup status for the Lodestone Image Interrogator extension.</summary>
public class LodestoneSetupStatus
{
    /// <summary>Whether all required local dependencies and model files are present.</summary>
    public bool IsReady;

    /// <summary>Whether setup is currently running.</summary>
    public bool IsSetupRunning;

    /// <summary>Whether the local Python environment exists.</summary>
    public bool HasPythonEnv;

    /// <summary>Whether the Lodestone model file exists.</summary>
    public bool HasModelFile;

    /// <summary>Whether the Lodestone vocabulary file exists.</summary>
    public bool HasVocabFile;

    /// <summary>User-visible setup status message.</summary>
    public string Message = "";

    /// <summary>Converts this status to the API JSON payload shape.</summary>
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
