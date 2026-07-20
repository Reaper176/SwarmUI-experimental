using FreneticUtilities.FreneticExtensions;
using Newtonsoft.Json.Linq;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Processing;
using SwarmUI.Accounts;
using SwarmUI.Core;
using SwarmUI.Media;
using SwarmUI.Utils;
using System.Diagnostics;
using System.Linq;
using Image = SwarmUI.Utils.Image;
using ISImageRGBA = SixLabors.ImageSharp.Image<SixLabors.ImageSharp.PixelFormats.Rgba32>;

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

    public static void PrepareClassicInpaintMask(ISImageRGBA maskImage, int expandMask, int feather)
    {
        maskImage.Mutate(ctx =>
        {
            for (int y = 0; y < maskImage.Height; y++)
            {
                for (int x = 0; x < maskImage.Width; x++)
                {
                    SixLabors.ImageSharp.PixelFormats.Rgba32 pixel = maskImage[x, y];
                    byte value = pixel.A > 0 || pixel.R > 0 || pixel.G > 0 || pixel.B > 0 ? (byte)255 : (byte)0;
                    maskImage[x, y] = new(value, value, value, 255);
                }
            }
            if (expandMask > 0)
            {
                ctx.GaussianBlur(Math.Max(0.5f, expandMask * 0.6f));
                ctx.BinaryThreshold(0.02f);
            }
            if (feather > 0)
            {
                ctx.GaussianBlur(Math.Max(0.5f, feather * 0.5f));
            }
        });
    }

    public static async Task<HashSet<string>> GetSupportedClassicInpaintBackends()
    {
        HashSet<string> supported = ["lama", "mat"];
        foreach (string candidate in GetIOPaintCommandCandidates())
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
        Logs.Info($"ClassicInpaint request received from user '{session.User?.UserID ?? "unknown"}' with backend '{backend}', feather={feather}, expandMask={expandMask}, imageBytes={imageData?.Length ?? 0}, maskBytes={maskData?.Length ?? 0}.");
        backend = backend.ToLowerInvariant();
        HashSet<string> supportedBackends = await GetSupportedClassicInpaintBackends();
        if (!supportedBackends.Contains(backend))
        {
            return new JObject() { ["error"] = $"Classic Inpaint backend '{backend}' is not supported by the installed IOPaint version. Supported backends: {supportedBackends.OrderBy(x => x).JoinString(", ")}" };
        }
        if (string.IsNullOrWhiteSpace(imageData) || string.IsNullOrWhiteSpace(maskData))
        {
            return new JObject() { ["error"] = "Missing image or mask data." };
        }
        if (!Program.ServerSettings.IOPaint.Enabled)
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
            using ISImageRGBA maskImage = SixLabors.ImageSharp.Image.Load<SixLabors.ImageSharp.PixelFormats.Rgba32>(maskFile.RawData);
            PrepareClassicInpaintMask(maskImage, Math.Max(0, expandMask), Math.Max(0, feather));
            maskImage.SaveAsPng(maskPath);
            List<string[]> commands = [];
            string device = string.IsNullOrWhiteSpace(Program.ServerSettings.IOPaint.Device) ? "cpu" : Program.ServerSettings.IOPaint.Device;
            commands.Add(["run", "--model", backend, "--device", device, "--image", imagePath, "--mask", maskPath, "--output", outputDir]);
            commands.Add(["run", "--model", backend, "--device", device, "--input", imagePath, "--mask", maskPath, "--output", outputDir]);
            if (!string.IsNullOrWhiteSpace(Program.ServerSettings.IOPaint.ModelCachePath))
            {
                List<string[]> updatedCommands = [];
                foreach (string[] command in commands)
                {
                    updatedCommands.Add([.. command, "--model-dir", Program.ServerSettings.IOPaint.ModelCachePath]);
                }
                commands = updatedCommands;
            }
            List<string> errors = [];
            bool producedOutput = false;
            foreach (string candidate in GetIOPaintCommandCandidates())
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
