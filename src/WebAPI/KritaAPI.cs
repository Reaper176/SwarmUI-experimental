using FreneticUtilities.FreneticExtensions;
using Microsoft.AspNetCore.Http;
using Newtonsoft.Json.Linq;
using SwarmUI.Accounts;
using SwarmUI.Core;
using SwarmUI.Media;
using SwarmUI.Utils;
using System.IO;

namespace SwarmUI.WebAPI;

[API.APIClass("API routes for local Krita integration.")]
public static class KritaAPI
{
    [API.APIDescription("Export the current Swarm image to a temporary PNG and open it in the local Krita application.", "\"success\": true")]
    public static async Task<JObject> SendImageToKrita(Session session,
        [API.APIParameter("The PNG-or-dataURL image content to export to Krita.")] string imageData)
    {
        if (string.IsNullOrWhiteSpace(imageData))
        {
            return KritaImageBridge.Error("No image was provided.");
        }
        try
        {
            ImageFile image = ImageFile.FromDataString(imageData).ForceToPng();
            string path = KritaImageBridge.CreateTempPngPath();
            await File.WriteAllBytesAsync(path, image.RawData);
            KritaImageBridge.SetActiveSession(session.ID);
            KritaImageBridge.LaunchKrita(path);
            return new JObject() { ["success"] = true, ["path"] = path };
        }
        catch (Exception ex)
        {
            Logs.Error($"Failed to export image to Krita: {ex.ReadableString()}");
            return KritaImageBridge.Error("Failed to open Krita with the exported image.");
        }
    }

    [API.APIDescription("Accept a flattened Krita image and store it as a pending import for the target Swarm session.", "\"success\": true")]
    public static async Task<JObject> ImportKritaImage(Session session,
        [API.APIParameter("Base64-encoded PNG bytes from the Krita bridge plugin.")] string imageBase64,
        [API.APIParameter("The session ID that should receive the returned Krita image.")] string targetSession)
    {
        if (string.IsNullOrWhiteSpace(imageBase64))
        {
            return KritaImageBridge.Error("No image payload was provided.");
        }
        if (string.IsNullOrWhiteSpace(targetSession))
        {
            return KritaImageBridge.Error("No target session was provided.");
        }
        try
        {
            ImageFile image = ImageFile.FromBase64(imageBase64, MediaType.ImagePng).ForceToPng();
            KritaImageBridge.StorePendingImport(targetSession, image.AsDataString());
            return new JObject() { ["success"] = true };
        }
        catch (Exception ex)
        {
            Logs.Error($"Failed to import Krita image: {ex.ReadableString()}");
            return KritaImageBridge.Error("Failed to import the Krita image.");
        }
    }

    [API.APIDescription("Check whether the current Swarm session has a pending Krita image import waiting to be applied.", "\"success\": true, \"image\": \"data:image/png;base64,...\"")]
    public static async Task<JObject> CheckPendingKritaImage(Session session)
    {
        string image = KritaImageBridge.TakePendingImport(session.ID);
        JObject result = new() { ["success"] = true };
        if (image is not null)
        {
            result["image"] = image;
        }
        return result;
    }

    [API.APIDescription("Get the current active local Swarm session targeted for Krita round-trips.", "\"success\": true, \"session_id\": \"...\"")]
    public static async Task<JObject> GetActiveKritaSession(HttpContext context)
    {
        string ip = WebUtil.GetIPString(context);
        if ((ip != "127.0.0.1" && ip != "::1" && ip != "::ffff:127.0.0.1") || context.Request.Headers.ContainsKey("X-Forwarded-For"))
        {
            return KritaImageBridge.Error("This route is only available from local loopback connections.");
        }
        string activeSession = KritaImageBridge.GetActiveSession();
        JObject result = new() { ["success"] = true };
        if (!string.IsNullOrWhiteSpace(activeSession))
        {
            result["session_id"] = activeSession;
        }
        return result;
    }
}
