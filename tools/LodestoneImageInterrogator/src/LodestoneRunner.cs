using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using SwarmUI.Core;
using SwarmUI.Utils;
using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Threading.Tasks;

namespace LodestoneImageInterrogatorExtension;

/// <summary>Runs Lodestone image interrogation through the local Python runner.</summary>
public static class LodestoneRunner
{
    /// <summary>Minimum accepted interrogation threshold.</summary>
    private const double MinimumThreshold = 0.0;

    /// <summary>Maximum accepted interrogation threshold.</summary>
    private const double MaximumThreshold = 1.0;

    /// <summary>Minimum accepted tag count.</summary>
    private const int MinimumMaxTags = 1;

    /// <summary>Maximum accepted tag count.</summary>
    private const int MaximumMaxTags = 1000;

    /// <summary>Runs Lodestone interrogation for a browser image data URL.</summary>
    public static async Task<JObject> Interrogate(string image, double threshold, int maxTags)
    {
        if (!TryDecodeDataUrlImage(image, out byte[] imageBytes, out JObject error))
        {
            return error;
        }

        LodestoneSetupStatus status = LodestoneSetupManager.GetStatus();
        if (!status.IsReady)
        {
            return new JObject()
            {
                ["success"] = false,
                ["error"] = status.Message,
                ["status"] = status.ToJson()
            };
        }

        double clampedThreshold = ClampThreshold(threshold);
        int clampedMaxTags = ClampMaxTags(maxTags);
        string tempImagePath = Path.Combine(Path.GetTempPath(), $"lodestone-interrogate-{Guid.NewGuid():N}.png");
        try
        {
            await File.WriteAllBytesAsync(tempImagePath, imageBytes, Program.GlobalProgramCancel);
            (int ExitCode, string Stdout, string Stderr) result = await RunProcess(
                LodestoneSetupManager.PythonExePath,
                [
                    Path.Combine(LodestoneSetupManager.RunnerRoot, "Runner", "lodestone_interrogate.py"),
                    "--checkpoint",
                    LodestoneSetupManager.ModelPath,
                    "--vocab",
                    LodestoneSetupManager.VocabPath,
                    "--image",
                    tempImagePath,
                    "--threshold",
                    clampedThreshold.ToString(CultureInfo.InvariantCulture),
                    "--max-tags",
                    clampedMaxTags.ToString(CultureInfo.InvariantCulture),
                    "--device",
                    LodestoneSetupManager.GetRuntimeDevice()
                ],
                LodestoneSetupManager.RunnerRoot);

            if (result.ExitCode == 0)
            {
                return ParseRunnerJson(result.Stdout);
            }

            if (TryParseRunnerJson(result.Stdout, out JObject parsedFailure))
            {
                return parsedFailure;
            }

            Logs.Error($"Lodestone runner exited with code {result.ExitCode}.\nSTDOUT:\n{result.Stdout}\nSTDERR:\n{result.Stderr}");
            return new JObject()
            {
                ["success"] = false,
                ["error"] = BuildRunnerError(result.ExitCode, result.Stdout, result.Stderr),
                ["stderr"] = result.Stderr,
                ["stdout"] = result.Stdout,
                ["exitCode"] = result.ExitCode
            };
        }
        finally
        {
            if (File.Exists(tempImagePath))
            {
                File.Delete(tempImagePath);
            }
        }
    }

    /// <summary>Clamps the requested threshold to the supported range.</summary>
    public static double ClampThreshold(double threshold)
    {
        if (double.IsNaN(threshold))
        {
            return 0.35;
        }
        return Math.Clamp(threshold, MinimumThreshold, MaximumThreshold);
    }

    /// <summary>Clamps the requested tag count to the supported range.</summary>
    public static int ClampMaxTags(int maxTags)
    {
        return Math.Clamp(maxTags, MinimumMaxTags, MaximumMaxTags);
    }

    /// <summary>Decodes browser data URL image content into raw bytes.</summary>
    private static bool TryDecodeDataUrlImage(string image, out byte[] imageBytes, out JObject error)
    {
        imageBytes = [];
        error = null;
        int commaIndex = image.IndexOf(',');
        if (commaIndex < 0)
        {
            error = new JObject()
            {
                ["success"] = false,
                ["error"] = "Invalid image data URL."
            };
            return false;
        }

        string base64 = image[(commaIndex + 1)..];
        try
        {
            imageBytes = Convert.FromBase64String(base64);
            return true;
        }
        catch (FormatException)
        {
            error = new JObject()
            {
                ["success"] = false,
                ["error"] = "Invalid image base64 data."
            };
            return false;
        }
    }

    /// <summary>Runs a process while preserving stdout, stderr, and exit code separately.</summary>
    private static async Task<(int ExitCode, string Stdout, string Stderr)> RunProcess(string process, string[] args, string workingDirectory)
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
        return (proc.ExitCode, stdout, stderr);
    }

    /// <summary>Builds a concise runner failure message with the most useful available process output.</summary>
    private static string BuildRunnerError(int exitCode, string stdout, string stderr)
    {
        string detail = MostUsefulLine(stderr);
        if (string.IsNullOrWhiteSpace(detail))
        {
            detail = MostUsefulLine(stdout);
        }
        if (string.IsNullOrWhiteSpace(detail))
        {
            return $"Lodestone runner exited with code {exitCode}.";
        }
        return $"Lodestone runner exited with code {exitCode}: {detail}";
    }

    /// <summary>Returns the most useful process output line, trimmed to a reasonable UI size.</summary>
    private static string MostUsefulLine(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return "";
        }
        string[] lines = text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
        for (int i = lines.Length - 1; i >= 0; i--)
        {
            string trimmed = lines[i].Trim();
            if (!string.IsNullOrWhiteSpace(trimmed))
            {
                return trimmed.Length > 500 ? $"{trimmed[..500]}..." : trimmed;
            }
        }
        return "";
    }

    /// <summary>Parses runner stdout as a JSON object.</summary>
    private static JObject ParseRunnerJson(string stdout)
    {
        if (TryParseRunnerJson(stdout, out JObject parsed))
        {
            return parsed;
        }
        return new JObject()
        {
            ["success"] = false,
            ["error"] = "Lodestone runner did not return valid JSON.",
            ["stdout"] = stdout
        };
    }

    /// <summary>Attempts to parse runner stdout as a JSON object.</summary>
    private static bool TryParseRunnerJson(string stdout, out JObject parsed)
    {
        parsed = null;
        try
        {
            parsed = JObject.Parse(stdout);
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }
}
