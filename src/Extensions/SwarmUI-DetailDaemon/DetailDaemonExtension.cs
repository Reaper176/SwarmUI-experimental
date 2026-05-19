using Newtonsoft.Json.Linq;
using SwarmUI.Builtin_ComfyUIBackend;
using SwarmUI.Core;
using SwarmUI.Text2Image;
using SwarmUI.Utils;

namespace Quaggles.Extensions.DetailDaemon;

public class DetailDaemonExtension : Extension
{
    private const string FeatureId = "detail_daemon";
    private const string Prefix = "[DD] ";

    public static T2IParamGroup DetailDaemonGroup;

    public static T2IRegisteredParam<double> DetailAmount,
        Start,
        End,
        Bias,
        Exponent,
        StartOffset,
        EndOffset,
        Fade,
        CFGScaleOverride;

    public static T2IRegisteredParam<bool> Smooth;

    public override void OnInit()
    {
        InstallableFeatures.RegisterInstallableFeature(new(
            "Detail Daemon",
            FeatureId,
            "https://github.com/Jonseed/ComfyUI-Detail-Daemon",
            "Jonseed",
            "This will install Detail Daemon sampler support developed by Jonseed.\nDo you wish to install?"));

        ComfyUIBackendExtension.NodeToFeatureMap["DetailDaemonSamplerNode"] = FeatureId;

        DetailDaemonGroup = new("Detail Daemon", Toggles: true, Open: false, IsAdvanced: true,
            Description: "Wraps compatible ComfyUI samplers with Detail Daemon sigma adjustment.");

        int order = 0;
        DetailAmount = T2IParamTypes.Register<double>(new($"{Prefix}Detail Amount",
            "[Detail Daemon]\nMain detail adjustment amount. Positive values lower sigmas during the selected portion of sampling, generally increasing detail.",
            "0.1", Min: -5, Max: 5, Step: 0.01, Group: DetailDaemonGroup, FeatureFlag: FeatureId,
            ViewType: ParamViewType.SLIDER, OrderPriority: order++, Examples: ["0", "0.1", "0.25", "0.5", "1"]));
        Start = T2IParamTypes.Register<double>(new($"{Prefix}Start",
            "[Detail Daemon]\nFraction of sampling progress where adjustment starts.",
            "0.2", Min: 0, Max: 1, Step: 0.01, Group: DetailDaemonGroup, FeatureFlag: FeatureId,
            ViewType: ParamViewType.SLIDER, OrderPriority: order++));
        End = T2IParamTypes.Register<double>(new($"{Prefix}End",
            "[Detail Daemon]\nFraction of sampling progress where adjustment ends.",
            "0.8", Min: 0, Max: 1, Step: 0.01, Group: DetailDaemonGroup, FeatureFlag: FeatureId,
            ViewType: ParamViewType.SLIDER, OrderPriority: order++));
        Bias = T2IParamTypes.Register<double>(new($"{Prefix}Bias",
            "[Detail Daemon]\nMoves the peak adjustment earlier or later between Start and End.",
            "0.5", Min: 0, Max: 1, Step: 0.01, Group: DetailDaemonGroup, FeatureFlag: FeatureId,
            ViewType: ParamViewType.SLIDER, OrderPriority: order++));
        Exponent = T2IParamTypes.Register<double>(new($"{Prefix}Exponent",
            "[Detail Daemon]\nChanges the curve shape of the adjustment schedule.",
            "1", Min: 0, Max: 10, Step: 0.05, Group: DetailDaemonGroup, FeatureFlag: FeatureId,
            ViewType: ParamViewType.SLIDER, OrderPriority: order++));
        StartOffset = T2IParamTypes.Register<double>(new($"{Prefix}Start Offset",
            "[Detail Daemon]\nAdjustment amount before Start. Usually leave at 0.",
            "0", Min: -1, Max: 1, Step: 0.01, Group: DetailDaemonGroup, FeatureFlag: FeatureId,
            ViewType: ParamViewType.SLIDER, OrderPriority: order++, IsAdvanced: true));
        EndOffset = T2IParamTypes.Register<double>(new($"{Prefix}End Offset",
            "[Detail Daemon]\nAdjustment amount after End. Usually leave at 0.",
            "0", Min: -1, Max: 1, Step: 0.01, Group: DetailDaemonGroup, FeatureFlag: FeatureId,
            ViewType: ParamViewType.SLIDER, OrderPriority: order++, IsAdvanced: true));
        Fade = T2IParamTypes.Register<double>(new($"{Prefix}Fade",
            "[Detail Daemon]\nReduces the full adjustment curve.",
            "0", Min: 0, Max: 1, Step: 0.05, Group: DetailDaemonGroup, FeatureFlag: FeatureId,
            ViewType: ParamViewType.SLIDER, OrderPriority: order++, IsAdvanced: true));
        Smooth = T2IParamTypes.Register<bool>(new($"{Prefix}Smooth",
            "[Detail Daemon]\nSmooth the adjustment curve.",
            "true", Group: DetailDaemonGroup, FeatureFlag: FeatureId, OrderPriority: order++, IsAdvanced: true));
        CFGScaleOverride = T2IParamTypes.Register<double>(new($"{Prefix}CFG Scale Override",
            "[Detail Daemon]\nIf enabled and greater than 0, overrides the CFG scale used by Detail Daemon. Leave disabled or 0 to auto-detect.",
            "0", Min: 0, Max: 100, Step: 0.5, Group: DetailDaemonGroup, FeatureFlag: FeatureId,
            Toggleable: true, OrderPriority: order++, IsAdvanced: true));
    }
}
