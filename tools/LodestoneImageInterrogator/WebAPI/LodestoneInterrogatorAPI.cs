using LodestoneImageInterrogatorExtension;
using Newtonsoft.Json.Linq;
using SwarmUI.Accounts;
using SwarmUI.Utils;
using SwarmUI.WebAPI;
using System;
using System.Threading.Tasks;

namespace LodestoneImageInterrogatorExtension.WebAPI;

/// <summary>Permissions for Lodestone Image Interrogator API routes.</summary>
public static class LodestoneInterrogatorPermissions
{
    /// <summary>Permission group for Lodestone Image Interrogator routes.</summary>
    public static readonly PermInfoGroup Group = new PermInfoGroup("Lodestone Image Interrogator", "Permissions for Lodestone image interrogation.");

    /// <summary>Permission to view Lodestone Image Interrogator status and panel routes.</summary>
    public static readonly PermInfo View = Permissions.Register(new PermInfo("view_lodestone_image_interrogator", "View Lodestone Image Interrogator", "Allows viewing/status for the Lodestone Image Interrogator panel.", PermissionDefault.USER, Group));

    /// <summary>Permission to use Lodestone Image Interrogator routes.</summary>
    public static readonly PermInfo Use = Permissions.Register(new PermInfo("use_lodestone_image_interrogator", "Use Lodestone Image Interrogator", "Allows using the Lodestone Image Interrogator extension.", PermissionDefault.POWERUSERS, Group));

    /// <summary>Permission to run Lodestone Image Interrogator setup.</summary>
    public static readonly PermInfo Setup = Permissions.Register(new PermInfo("setup_lodestone_image_interrogator", "Setup Lodestone Image Interrogator", "Allows installing Python dependencies and downloading model files for the Lodestone Image Interrogator extension.", PermissionDefault.ADMINS, Group, PermSafetyLevel.RISKY));
}

/// <summary>API routes for the Lodestone Image Interrogator extension.</summary>
[API.APIClass("API routes for the Lodestone Image Interrogator extension")]
public static class LodestoneInterrogatorAPI
{
    /// <summary>Registers Lodestone Image Interrogator API calls.</summary>
    public static void Register()
    {
        PermInfo viewPermission = LodestoneInterrogatorPermissions.View;
        if (viewPermission is null)
        {
            throw new System.InvalidOperationException("Lodestone Image Interrogator view permission failed to register.");
        }
        PermInfo usePermission = LodestoneInterrogatorPermissions.Use;
        if (usePermission is null)
        {
            throw new System.InvalidOperationException("Lodestone Image Interrogator use permission failed to register.");
        }
        PermInfo setupPermission = LodestoneInterrogatorPermissions.Setup;
        if (setupPermission is null)
        {
            throw new System.InvalidOperationException("Lodestone Image Interrogator setup permission failed to register.");
        }
        API.RegisterAPICall(LodestoneInterrogatorStatus, false, LodestoneInterrogatorPermissions.View);
        API.RegisterAPICall(LodestoneInterrogatorSetup, true, LodestoneInterrogatorPermissions.Setup);
        API.RegisterAPICall(LodestoneInterrogatorInterrogate, true, LodestoneInterrogatorPermissions.Use);
    }

    /// <summary>Gets Lodestone Image Interrogator setup status.</summary>
    [API.APIDescription("Gets Lodestone Image Interrogator setup status", "Returns whether the extension has local dependencies and model files ready.")]
    public static async Task<JObject> LodestoneInterrogatorStatus(Session session)
    {
        LodestoneSetupStatus status = LodestoneSetupManager.GetStatus();
        return await Task.FromResult(new JObject()
        {
            ["success"] = true,
            ["status"] = status.ToJson()
        });
    }

    /// <summary>Runs Lodestone Image Interrogator setup after an explicit user request.</summary>
    [API.APIDescription("Runs Lodestone Image Interrogator setup", "Creates local dependencies and downloads required model files after explicit user request.")]
    public static async Task<JObject> LodestoneInterrogatorSetup(
        Session session,
        [API.APIParameter("Backend to install: auto, cuda, rocm, or cpu.")] string backend = "auto")
    {
        try
        {
            LodestoneSetupStatus status = await LodestoneSetupManager.RunSetup(backend);
            return new JObject()
            {
                ["success"] = status.IsReady,
                ["status"] = status.ToJson(),
                ["error"] = status.IsReady ? null : status.Message
            };
        }
        catch (Exception ex)
        {
            LodestoneSetupManager.MarkSetupFinished();
            Logs.Error($"Lodestone Image Interrogator setup failed: {ex}");
            return new JObject()
            {
                ["success"] = false,
                ["error"] = ex.Message,
                ["status"] = LodestoneSetupManager.GetStatus().ToJson()
            };
        }
    }

    /// <summary>Runs Lodestone Image Interrogator against a browser image data URL.</summary>
    [API.APIDescription("Runs Lodestone Image Interrogator", "Returns tag predictions for a provided image data URL.")]
    public static async Task<JObject> LodestoneInterrogatorInterrogate(
        Session session,
        [API.APIParameter("Data URL containing base64 image data.")] string image,
        [API.APIParameter("Minimum tag probability threshold, clamped to 0.0 through 1.0.")] double threshold = 0.35,
        [API.APIParameter("Maximum number of tags to return, clamped to 1 through 1000.")] int maxTags = 80)
    {
        if (string.IsNullOrWhiteSpace(image))
        {
            return new JObject()
            {
                ["success"] = false,
                ["error"] = "No image was provided."
            };
        }

        try
        {
            return await LodestoneRunner.Interrogate(
                image,
                LodestoneRunner.ClampThreshold(threshold),
                LodestoneRunner.ClampMaxTags(maxTags));
        }
        catch (Exception ex)
        {
            return new JObject()
            {
                ["success"] = false,
                ["error"] = ex.Message
            };
        }
    }
}
