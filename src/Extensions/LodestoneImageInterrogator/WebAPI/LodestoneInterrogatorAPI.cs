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
        PermInfo usePermission = LodestoneInterrogatorPermissions.Use;
        if (usePermission is null)
        {
            throw new System.InvalidOperationException("Lodestone Image Interrogator use permission failed to register.");
        }
        API.RegisterAPICall(LodestoneInterrogatorStatus, false, Permissions.FundamentalGenerateTabAccess);
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
