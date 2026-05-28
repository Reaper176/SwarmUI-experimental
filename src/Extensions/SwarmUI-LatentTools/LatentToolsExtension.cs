using SwarmUI.Builtin_ComfyUIBackend;
using SwarmUI.Core;

namespace MachinesOfDisruption.Extensions.LatentTools;

public class LatentToolsExtension : Extension
{
    public const string FeatureId = "latent_tools";

    public override void OnInit()
    {
        InstallableFeatures.RegisterInstallableFeature(new("Latent Tools", FeatureId, "https://github.com/Machines-of-Disruption/latent-tools", "Machines-of-Disruption"));
        ComfyUIBackendExtension.NodeToFeatureMap["LTPreviewLatent"] = FeatureId;
    }
}
