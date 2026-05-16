using LodestoneImageInterrogatorExtension;
using Newtonsoft.Json.Linq;
using SwarmUI.Accounts;
using SwarmUI.WebAPI;
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
        API.RegisterAPICall(LodestoneInterrogatorStatus, false, LodestoneInterrogatorPermissions.View);
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
}
