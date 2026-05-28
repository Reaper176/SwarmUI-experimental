using SwarmUI.Builtin_ComfyUIBackend;
using SwarmUI.Core;
using SwarmUI.Text2Image;

namespace MachinesOfDisruption.Extensions.LatentTools;

public class LatentToolsExtension : Extension
{
    public const string FeatureId = "latent_tools";

    public static T2IParamGroup LatentToolsGroup;

    public static T2IRegisteredParam<bool> LatentToolsAvailable;

    public override void OnInit()
    {
        InstallableFeatures.RegisterInstallableFeature(new("Latent Tools", FeatureId, "https://github.com/Machines-of-Disruption/latent-tools", "Machines-of-Disruption"));
        ScriptFiles.Add("assets/latent_tools.js");
        ComfyUIBackendExtension.NodeToFeatureMap["LTPreviewLatent"] = FeatureId;

        LatentToolsGroup = new("Latent Tools", Toggles: false, Open: false, IsAdvanced: true, Description: "Installs the latent-tools ComfyUI custom node pack.");
        LatentToolsAvailable = T2IParamTypes.Register<bool>(new("[LatentTools] Available", "Internal marker used to show the Latent Tools installer button.",
            "true", Group: LatentToolsGroup, FeatureFlag: FeatureId, VisibleNormally: false, ExtraHidden: true, IntentionalUnused: true, HideFromMetadata: true, DoNotSave: true));
    }
}
