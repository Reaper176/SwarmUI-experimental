using Newtonsoft.Json.Linq;
using SwarmUI.Core;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;

namespace SwarmUI.Utils;

/// <summary>Helpers for the local SwarmUI/Krita image bridge.</summary>
public static class KritaImageBridge
{
    /// <summary>Pending per-session Krita image imports.</summary>
    public static ConcurrentDictionary<string, string> PendingImports = [];

    /// <summary>The most recently selected local Swarm session for Krita round-trips.</summary>
    public static string ActiveSessionId;

    /// <summary>Gets the directory used for temporary Krita bridge image exports.</summary>
    public static string GetTempDirectory()
    {
        string relative = Program.ServerSettings.KritaBridge.TempPath;
        string baseDir = Utilities.CombinePathWithAbsolute(Environment.CurrentDirectory, Program.ServerSettings.Paths.DataPath);
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
        string fullPath = Path.GetFullPath(imagePath);
        string executable = ResolveKritaExecutable();
        ProcessStartInfo start = new(executable, $"\"{fullPath}\"")
        {
            UseShellExecute = true
        };
        Process.Start(start);
    }

    /// <summary>Stores a pending image import for a target session.</summary>
    public static void StorePendingImport(string sessionId, string imageData)
    {
        PendingImports[sessionId] = imageData;
    }

    /// <summary>Marks a session as the active local Krita target.</summary>
    public static void SetActiveSession(string sessionId)
    {
        ActiveSessionId = sessionId;
    }

    /// <summary>Gets the active local Krita target session.</summary>
    public static string GetActiveSession()
    {
        return ActiveSessionId;
    }

    /// <summary>Takes and clears a pending image import for a target session.</summary>
    public static string TakePendingImport(string sessionId)
    {
        PendingImports.TryRemove(sessionId, out string imageData);
        return imageData;
    }

    /// <summary>Returns a compact API error object.</summary>
    public static JObject Error(string message)
    {
        return new JObject() { ["error"] = message };
    }
}
