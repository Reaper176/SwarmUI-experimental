using Newtonsoft.Json.Linq;
using SwarmUI.Core;
using System.Collections.Concurrent;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;

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
            return ExpandPathVariables(configured);
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

    /// <summary>Expands environment-variable and home-directory tokens within a configured path.</summary>
    public static string ExpandPathVariables(string path)
    {
        string expanded = Environment.ExpandEnvironmentVariables(path);
        if (expanded.StartsWith("~"))
        {
            string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            if (!string.IsNullOrWhiteSpace(home))
            {
                if (expanded == "~")
                {
                    expanded = home;
                }
                else if (expanded.StartsWith("~/") || expanded.StartsWith("~\\"))
                {
                    expanded = Path.Combine(home, expanded[2..]);
                }
            }
        }
        expanded = Regex.Replace(expanded, @"\$\{([A-Za-z_][A-Za-z0-9_]*)\}", match =>
        {
            string value = Environment.GetEnvironmentVariable(match.Groups[1].Value);
            return value ?? match.Value;
        });
        expanded = Regex.Replace(expanded, @"\$([A-Za-z_][A-Za-z0-9_]*)", match =>
        {
            string value = Environment.GetEnvironmentVariable(match.Groups[1].Value);
            return value ?? match.Value;
        });
        return expanded;
    }

    /// <summary>Starts Krita with the given local image path.</summary>
    public static void LaunchKrita(string imagePath)
    {
        string fullPath = Path.GetFullPath(imagePath);
        string executable = ResolveKritaExecutable();
        try
        {
            ProcessStartInfo start = new(executable, $"\"{fullPath}\"")
            {
                UseShellExecute = false
            };
            Process.Start(start);
        }
        catch (Win32Exception)
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            {
                string appImagePath = FindKritaAppImage();
                if (!string.IsNullOrWhiteSpace(appImagePath))
                {
                    ProcessStartInfo start = new(appImagePath, $"\"{fullPath}\"")
                    {
                        UseShellExecute = false
                    };
                    Process.Start(start);
                    return;
                }
            }
            throw;
        }
    }

    /// <summary>Finds a Krita AppImage in common locations on Linux.</summary>
    private static string FindKritaAppImage()
    {
        string[] searchDirs = new[]
        {
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Applications"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".local", "bin"),
            "/opt",
            "/usr/local/bin",
            "/usr/bin"
        };
        foreach (string dir in searchDirs)
        {
            if (Directory.Exists(dir))
            {
                string[] files = Directory.GetFiles(dir, "krita*.AppImage");
                if (files.Length > 0)
                {
                    return files[0];
                }
            }
        }
        return null;
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
