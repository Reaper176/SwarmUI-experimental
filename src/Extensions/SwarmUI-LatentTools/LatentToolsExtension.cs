using SwarmUI.Builtin_ComfyUIBackend;
using SwarmUI.Core;
using SwarmUI.Media;
using SwarmUI.Text2Image;
using SwarmUI.Utils;
using Newtonsoft.Json.Linq;

namespace MachinesOfDisruption.Extensions.LatentTools;

public class LatentToolsExtension : Extension
{
    /// <summary>Feature flag ID for the latent-tools ComfyUI node pack.</summary>
    public const string FeatureId = "latent_tools";

    /// <summary>Generate parameter group for latent-tools controls.</summary>
    public static T2IParamGroup LatentToolsGroup;

    /// <summary>Controls which latent-tools random latent source to use.</summary>
    public static T2IRegisteredParam<string> LatentInitMode;

    /// <summary>Number of channels to generate in the latent tensor.</summary>
    public static T2IRegisteredParam<int> LatentChannels;

    /// <summary>Distribution controls for Gaussian and Uniform latent initialization.</summary>
    public static T2IRegisteredParam<double> GaussianMean, GaussianStd, UniformMin, UniformMax;

    public override void OnInit()
    {
        InstallableFeatures.RegisterInstallableFeature(new("Latent Tools", FeatureId, "https://github.com/Machines-of-Disruption/latent-tools", "Machines-of-Disruption"));
        ScriptFiles.Add("assets/latent_tools.js");
        ComfyUIBackendExtension.NodeToFeatureMap["LTPreviewLatent"] = FeatureId;
        ComfyUIBackendExtension.NodeToFeatureMap["LTGaussianLatent"] = FeatureId;
        ComfyUIBackendExtension.NodeToFeatureMap["LTUniformLatent"] = FeatureId;

        LatentToolsGroup = new("Latent Tools", Toggles: false, Open: false, IsAdvanced: false, Description: "Installs and configures the latent-tools ComfyUI custom node pack.");
        int order = 0;
        LatentInitMode = T2IParamTypes.Register<string>(new("[LatentTools] Init Mode", "Replaces Swarm's normal empty starting latent with a random latent from latent-tools.",
            "Disabled", Group: LatentToolsGroup, FeatureFlag: FeatureId, OrderPriority: order++, IgnoreIf: "Disabled", ChangeWeight: 1,
            GetValues: _ => ["Disabled", "Gaussian", "Uniform"]));
        LatentChannels = T2IParamTypes.Register<int>(new("[LatentTools] Channels", "Number of latent channels to generate. Most image models use 4.",
            "4", Min: 1, Max: 128, Step: 1, Group: LatentToolsGroup, FeatureFlag: FeatureId, OrderPriority: order++, DependNonDefault: LatentInitMode.Type.ID));
        GaussianMean = T2IParamTypes.Register<double>(new("[LatentTools] Gaussian Mean", "Mean value for Gaussian latent initialization.",
            "0", Min: -100, Max: 100, Step: 0.0001, Group: LatentToolsGroup, FeatureFlag: FeatureId, OrderPriority: order++, DependNonDefault: LatentInitMode.Type.ID));
        GaussianStd = T2IParamTypes.Register<double>(new("[LatentTools] Gaussian Std", "Standard deviation for Gaussian latent initialization.",
            "1", Min: 0, Max: 100, Step: 0.0001, Group: LatentToolsGroup, FeatureFlag: FeatureId, OrderPriority: order++, DependNonDefault: LatentInitMode.Type.ID));
        UniformMin = T2IParamTypes.Register<double>(new("[LatentTools] Uniform Min", "Minimum value for Uniform latent initialization.",
            "-1", Min: -1000, Max: 1000, Step: 0.0001, Group: LatentToolsGroup, FeatureFlag: FeatureId, OrderPriority: order++, DependNonDefault: LatentInitMode.Type.ID));
        UniformMax = T2IParamTypes.Register<double>(new("[LatentTools] Uniform Max", "Maximum value for Uniform latent initialization.",
            "1", Min: -1000, Max: 1000, Step: 0.0001, Group: LatentToolsGroup, FeatureFlag: FeatureId, OrderPriority: order++, DependNonDefault: LatentInitMode.Type.ID));

        WorkflowGenerator.AddStep(g =>
        {
            if (!g.UserInput.TryGet(LatentInitMode, out string mode) || mode == "Disabled")
            {
                return;
            }
            if (!g.Features.Contains(FeatureId))
            {
                throw new SwarmUserErrorException("Latent Tools parameters specified, but feature isn't installed");
            }
            if (g.UserInput.TryGet(T2IParamTypes.InitImage, out Image _))
            {
                return;
            }
            if (g.CurrentMedia is null || g.CurrentMedia.DataType != WGNodeData.DT_LATENT_IMAGE)
            {
                return;
            }
            int width = g.UserInput.GetImageWidth();
            int height = g.UserInput.GetImageHeight();
            JObject inputs = new()
            {
                ["channels"] = g.UserInput.Get(LatentChannels, 4),
                ["width"] = width,
                ["height"] = height,
                ["batch_size"] = g.UserInput.Get(T2IParamTypes.BatchSize, 1),
                ["seed"] = g.UserInput.Get(T2IParamTypes.Seed, 0)
            };
            string nodeType;
            if (mode == "Gaussian")
            {
                nodeType = "LTGaussianLatent";
                inputs["mean"] = g.UserInput.Get(GaussianMean, 0);
                inputs["std"] = g.UserInput.Get(GaussianStd, 1);
            }
            else if (mode == "Uniform")
            {
                nodeType = "LTUniformLatent";
                inputs["min"] = g.UserInput.Get(UniformMin, -1);
                inputs["max"] = g.UserInput.Get(UniformMax, 1);
            }
            else
            {
                throw new SwarmUserErrorException($"Unknown Latent Tools init mode '{mode}'");
            }
            string latentNode = g.CreateNode(nodeType, inputs);
            g.CurrentMedia = g.CurrentMedia.WithPath([latentNode, 0], WGNodeData.DT_LATENT_IMAGE, g.CurrentCompat());
            g.CurrentMedia.Width = width;
            g.CurrentMedia.Height = height;
        }, -8.75);
    }
}
