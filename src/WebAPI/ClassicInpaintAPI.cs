using FreneticUtilities.FreneticExtensions;
using ImageMagick;
using Newtonsoft.Json.Linq;
using SwarmUI.Accounts;
using SwarmUI.Core;
using SwarmUI.Media;
using SwarmUI.Utils;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Image = SwarmUI.Utils.Image;

namespace SwarmUI.WebAPI;

[API.APIClass("API routes for classic image inpainting.")]
public static class ClassicInpaintAPI
{
    public static string[] GetIOPaintCommandCandidates()
    {
        List<string> candidates = [];
        Settings.IOPaintServiceData settings = Program.ServerSettings.IOPaint;
        string exePath = BackendAPI.GetIOPaintExePath(settings);
        string pythonPath = BackendAPI.GetIOPaintPythonPath(settings);
        if (!string.IsNullOrWhiteSpace(exePath))
        {
            candidates.Add(exePath);
        }
        if (!string.IsNullOrWhiteSpace(pythonPath))
        {
            candidates.Add(pythonPath);
        }
        candidates.Add("iopaint");
        candidates.Add("python3");
        candidates.Add("python");
        return [.. candidates.Distinct()];
    }

    public static async Task<(int, string)> RunProcessCapture(string fileName, string[] args, string workingDirectory = null)
    {
        ProcessStartInfo start = new(fileName)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };
        if (workingDirectory is not null)
        {
            start.WorkingDirectory = workingDirectory;
        }
        foreach (string arg in args)
        {
            start.ArgumentList.Add(arg);
        }
        Process process = Process.Start(start);
        Task<string> stdOutRead = process.StandardOutput.ReadToEndAsync();
        Task<string> stdErrRead = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync(Program.GlobalProgramCancel);
        string stdout = await stdOutRead;
        string stderr = await stdErrRead;
        string result = stdout;
        if (!string.IsNullOrWhiteSpace(stderr))
        {
            result = $"{stdout}\n{stderr}";
        }
        return (process.ExitCode, result.Trim());
    }

    /// <summary>Converts any nonzero RGBA channel to an opaque mask, then expands and feathers its selected pixels.</summary>
    public static void PrepareClassicInpaintMask(IMagickImage<ushort> maskImage, int expandMask, int feather)
    {
        maskImage.ColorSpace = ColorSpace.sRGB;
        maskImage.ColorType = ColorType.TrueColorAlpha;
        maskImage.Depth = 8;
        using (IPixelCollection<ushort> pixels = maskImage.GetPixels())
        {
            byte[] rgba = pixels.ToByteArray(PixelMapping.RGBA);
            for (int i = 0; i < rgba.Length; i += 4)
            {
                byte value = rgba[i] > 0 || rgba[i + 1] > 0 || rgba[i + 2] > 0 || rgba[i + 3] > 0 ? (byte)255 : (byte)0;
                rgba[i] = value;
                rgba[i + 1] = value;
                rgba[i + 2] = value;
                rgba[i + 3] = 255;
            }
            pixels.SetPixels(Array.ConvertAll(rgba, value => (ushort)(value * 257)));
        }
        if (expandMask > 0)
        {
            maskImage.GaussianBlur(0, Math.Max(0.5, expandMask * 0.6), Channels.RGB);
            maskImage.Threshold(new Percentage(2), Channels.RGB);
        }
        if (feather > 0)
        {
            maskImage.GaussianBlur(0, Math.Max(0.5, feather * 0.5), Channels.RGB);
        }
    }

    public static async Task<HashSet<string>> GetSupportedClassicInpaintBackends()
    {
        await BackendAPI.IOPaintLifecycleSemaphore.WaitAsync(Program.GlobalProgramCancel);
        try
        {
            string[] commandCandidates = GetIOPaintCommandCandidates();
            return await GetSupportedClassicInpaintBackendsUnlocked(commandCandidates);
        }
        finally
        {
            BackendAPI.IOPaintLifecycleSemaphore.Release();
        }
    }

    /// <summary>Probes Classic Inpaint capabilities while the caller holds the IOPaint lifecycle semaphore.</summary>
    private static async Task<HashSet<string>> GetSupportedClassicInpaintBackendsUnlocked(string[] commandCandidates)
    {
        HashSet<string> supported = ["lama", "mat"];
        foreach (string candidate in commandCandidates)
        {
            bool isDirectIopaint = Path.GetFileName(candidate).ToLowerInvariant().StartsWith("iopaint");
            string[] args = isDirectIopaint ? ["run", "--help"] : ["-m", "iopaint", "run", "--help"];
            try
            {
                (int exitCode, string outputText) = await RunProcessCapture(candidate, args);
                if (exitCode == 0 && !string.IsNullOrWhiteSpace(outputText) && outputText.ToLowerInvariant().Contains("zits"))
                {
                    supported.Add("zits");
                    Logs.Info($"ClassicInpaint backend probe resolved support: {supported.OrderBy(x => x).JoinString(", ")}");
                    return supported;
                }
            }
            catch (Exception)
            {
            }
        }
        Logs.Info($"ClassicInpaint backend probe resolved support: {supported.OrderBy(x => x).JoinString(", ")}");
        return supported;
    }

    public static async Task<JObject> GetClassicInpaintBackends(Session session)
    {
        HashSet<string> supportedBackends = await GetSupportedClassicInpaintBackends();
        return new JObject()
        {
            ["backends"] = new JArray(supportedBackends.OrderBy(x => x).ToArray())
        };
    }

    public static async Task<JObject> ClassicInpaint(Session session, string imageData, string maskData, string backend = "lama", int feather = 8, int expandMask = 4)
    {
        await BackendAPI.IOPaintLifecycleSemaphore.WaitAsync(Program.GlobalProgramCancel);
        try
        {
            return await ClassicInpaintUnlocked(session, imageData, maskData, backend, feather, expandMask);
        }
        finally
        {
            BackendAPI.IOPaintLifecycleSemaphore.Release();
        }
    }

    /// <summary>Runs a Classic Inpaint request while the caller holds the IOPaint lifecycle semaphore.</summary>
    private static async Task<JObject> ClassicInpaintUnlocked(Session session, string imageData, string maskData, string backend, int feather, int expandMask)
    {
        string[] commandCandidates = GetIOPaintCommandCandidates();
        bool enabled;
        string configuredDevice;
        string modelCachePath;
        {
            Settings.IOPaintServiceData currentSettings = Program.ServerSettings.IOPaint;
            enabled = currentSettings.Enabled;
            configuredDevice = currentSettings.Device;
            modelCachePath = currentSettings.ModelCachePath;
        }
        string device = string.IsNullOrWhiteSpace(configuredDevice) ? "cpu" : configuredDevice;
        Logs.Info($"ClassicInpaint request received from user '{session.User?.UserID ?? "unknown"}' with backend '{backend}', feather={feather}, expandMask={expandMask}, imageBytes={imageData?.Length ?? 0}, maskBytes={maskData?.Length ?? 0}.");
        backend = backend.ToLowerInvariant();
        HashSet<string> supportedBackends = await GetSupportedClassicInpaintBackendsUnlocked(commandCandidates);
        if (!supportedBackends.Contains(backend))
        {
            return new JObject() { ["error"] = $"Classic Inpaint backend '{backend}' is not supported by the installed IOPaint version. Supported backends: {supportedBackends.OrderBy(x => x).JoinString(", ")}" };
        }
        if (string.IsNullOrWhiteSpace(imageData) || string.IsNullOrWhiteSpace(maskData))
        {
            return new JObject() { ["error"] = "Missing image or mask data." };
        }
        if (!enabled)
        {
            return new JObject() { ["error"] = "IOPaint is not enabled. Configure it under Server > Backends first." };
        }
        string tempRoot = Program.TempDir ?? Utilities.CombinePathWithAbsolute(Program.DataDir, "tmp");
        string taskDir = Path.Combine(tempRoot, $"classic-inpaint-{Utilities.SecureRandomHex(8)}");
        Directory.CreateDirectory(taskDir);
        try
        {
            ImageFile imageFile = ImageFile.FromDataString(imageData).ForceToPng();
            ImageFile maskFile = ImageFile.FromDataString(maskData).ForceToPng();
            string imagePath = Path.Combine(taskDir, "input.png");
            string maskPath = Path.Combine(taskDir, "mask.png");
            string outputDir = Path.Combine(taskDir, "output");
            Directory.CreateDirectory(outputDir);
            File.WriteAllBytes(imagePath, imageFile.RawData);
            using IMagickImage<ushort> maskImage = maskFile.ToMagick.Clone();
            PrepareClassicInpaintMask(maskImage, Math.Max(0, expandMask), Math.Max(0, feather));
            maskImage.Write(maskPath, MagickFormat.Png);
            List<string[]> commands = [];
            commands.Add(["run", "--model", backend, "--device", device, "--image", imagePath, "--mask", maskPath, "--output", outputDir]);
            commands.Add(["run", "--model", backend, "--device", device, "--input", imagePath, "--mask", maskPath, "--output", outputDir]);
            if (!string.IsNullOrWhiteSpace(modelCachePath))
            {
                List<string[]> updatedCommands = [];
                foreach (string[] command in commands)
                {
                    updatedCommands.Add([.. command, "--model-dir", modelCachePath]);
                }
                commands = updatedCommands;
            }
            List<string> errors = [];
            bool producedOutput = false;
            foreach (string candidate in commandCandidates)
            {
                foreach (string[] args in commands)
                {
                    bool isDirectIopaint = Path.GetFileName(candidate).ToLowerInvariant().StartsWith("iopaint");
                    string[] actualArgs = isDirectIopaint ? args : ["-m", "iopaint", .. args];
                    Logs.Info($"ClassicInpaint attempting backend '{backend}' via candidate '{candidate}' with args '{actualArgs.JoinString(" ")}'.");
                    try
                    {
                        (int exitCode, string outputText) = await RunProcessCapture(candidate, actualArgs, taskDir);
                        string outPath = Path.Combine(outputDir, Path.GetFileName(imagePath));
                        Logs.Info($"ClassicInpaint candidate '{candidate}' finished with exitCode={exitCode}, outputExists={File.Exists(outPath)}.");
                        if (exitCode == 0 && File.Exists(outPath))
                        {
                            byte[] resultData = await File.ReadAllBytesAsync(outPath, Program.GlobalProgramCancel);
                            Image resultImage = new(resultData, MediaType.ImagePng);
                            producedOutput = true;
                            return new JObject() { ["image"] = resultImage.AsDataString() };
                        }
                        Logs.Warning($"ClassicInpaint candidate '{candidate}' failed for backend '{backend}'. Output: {outputText}");
                        errors.Add($"{candidate} {actualArgs.JoinString(" ")} => {outputText}");
                    }
                    catch (Exception ex)
                    {
                        Logs.Warning($"ClassicInpaint candidate '{candidate}' threw for backend '{backend}': {ex.Message}");
                        errors.Add($"{candidate} {actualArgs.JoinString(" ")} => {ex.Message}");
                    }
                }
            }
            if (!producedOutput)
            {
                return new JObject() { ["error"] = $"Classic Inpaint failed for backend '{backend}'. Make sure IOPaint is installed and that this backend is supported by the installed IOPaint version. Details: {errors.JoinString(" | ")}" };
            }
            return new JObject() { ["error"] = "Classic Inpaint failed unexpectedly." };
        }
        catch (Exception ex)
        {
            Logs.Error($"ClassicInpaint crashed for backend '{backend}': {ex}");
            return new JObject() { ["error"] = $"Classic Inpaint crashed for backend '{backend}': {ex.Message}" };
        }
        finally
        {
            try
            {
                if (Directory.Exists(taskDir))
                {
                    Directory.Delete(taskDir, true);
                }
            }
            catch (Exception)
            {
            }
        }
    }
}
