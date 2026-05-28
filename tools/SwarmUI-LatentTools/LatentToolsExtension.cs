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

    /// <summary>Controls whether and how to blend the generated latent with Swarm's normal starting latent.</summary>
    public static T2IRegisteredParam<string> LatentBlendMode;

    /// <summary>Controls which latent operation to apply to the generated latent.</summary>
    public static T2IRegisteredParam<string> LatentOpMode;

    /// <summary>Number of channels to generate in the latent tensor.</summary>
    public static T2IRegisteredParam<int> LatentChannels;

    /// <summary>Controls whether Latent Tools should replace the base sampler with LTKSampler.</summary>
    public static T2IRegisteredParam<bool> UseLTKSampler;

    /// <summary>Distribution controls for Gaussian and Uniform latent initialization.</summary>
    public static T2IRegisteredParam<double> GaussianMean, GaussianStd, UniformMin, UniformMax, LatentBlendRatio, LatentOpArg;

    public override void OnInit()
    {
        InstallableFeatures.RegisterInstallableFeature(new("Latent Tools", FeatureId, "https://github.com/Machines-of-Disruption/latent-tools", "Machines-of-Disruption"));
        ScriptFiles.Add("assets/latent_tools.js");
        ComfyUIBackendExtension.NodeToFeatureMap["LTPreviewLatent"] = FeatureId;
        ComfyUIBackendExtension.NodeToFeatureMap["LTGaussianLatent"] = FeatureId;
        ComfyUIBackendExtension.NodeToFeatureMap["LTUniformLatent"] = FeatureId;
        ComfyUIBackendExtension.NodeToFeatureMap["LTBlendLatent"] = FeatureId;
        ComfyUIBackendExtension.NodeToFeatureMap["LTLatentOp"] = FeatureId;
        ComfyUIBackendExtension.NodeToFeatureMap["LTKSampler"] = FeatureId;

        LatentToolsGroup = new("Latent Tools", Toggles: false, Open: false, IsAdvanced: false, Description: "Installs and configures the latent-tools ComfyUI custom node pack.");
        int order = 0;
        LatentInitMode = T2IParamTypes.Register<string>(new("[LatentTools] Init Mode", "Replaces Swarm's normal empty starting latent with a random latent from latent-tools.",
            "Disabled", Group: LatentToolsGroup, FeatureFlag: FeatureId, OrderPriority: order++, IgnoreIf: "Disabled", ChangeWeight: 1,
            GetValues: _ => ["Disabled", "Gaussian", "Uniform", "Gaussian + Uniform"]));
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
        LatentBlendMode = T2IParamTypes.Register<string>(new("[LatentTools] Blend Mode", "Optionally blends Swarm's normal empty latent with the selected Latent Tools latent.",
            "Disabled", Group: LatentToolsGroup, FeatureFlag: FeatureId, OrderPriority: order++, IgnoreIf: "Disabled", DependNonDefault: LatentInitMode.Type.ID,
            GetValues: _ => ["Disabled", "interpolate", "add", "multiply", "abs_max", "abs_min", "max", "min", "sample"]));
        LatentBlendRatio = T2IParamTypes.Register<double>(new("[LatentTools] Blend Ratio", "Blend ratio used by interpolate and sample blend modes.",
            "0.5", Min: 0, Max: 1, Step: 0.001, ViewType: ParamViewType.SLIDER, Group: LatentToolsGroup, FeatureFlag: FeatureId, OrderPriority: order++, DependNonDefault: LatentBlendMode.Type.ID));
        LatentOpMode = T2IParamTypes.Register<string>(new("[LatentTools] Op", "Optionally applies a latent-tools operation to the selected/generated latent before sampling.",
            "Disabled", Group: LatentToolsGroup, FeatureFlag: FeatureId, OrderPriority: order++, IgnoreIf: "Disabled", DependNonDefault: LatentInitMode.Type.ID,
            GetValues: _ => ["Disabled", "add", "mul", "pow", "exp", "abs", "clamp_bottom", "clamp_top", "norm", "mean", "std", "sigmoid", "nop"]));
        LatentOpArg = T2IParamTypes.Register<double>(new("[LatentTools] Op Arg", "Argument used by latent-tools operations that need one. Ignored by exp, abs, norm, sigmoid, and nop.",
            "0", Min: -99999, Max: 99999, Step: 0.001, Group: LatentToolsGroup, FeatureFlag: FeatureId, OrderPriority: order++, DependNonDefault: LatentOpMode.Type.ID));
        UseLTKSampler = T2IParamTypes.Register<bool>(new("[LatentTools] Use LTKSampler", "If checked, replaces Swarm's base sampler with latent-tools LTKSampler and uses the generated Latent Tools latent as sampler noise.",
            "false", Group: LatentToolsGroup, FeatureFlag: FeatureId, OrderPriority: order++, DependNonDefault: LatentInitMode.Type.ID));

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
                if (g.UserInput.Get(UseLTKSampler, false))
                {
                    throw new SwarmUserErrorException("Latent Tools LTKSampler is only supported for text-to-image workflows without an init image");
                }
                return;
            }
            if (g.CurrentMedia is null || g.CurrentMedia.DataType != WGNodeData.DT_LATENT_IMAGE)
            {
                return;
            }
            WGNodeData swarmLatent = g.CurrentMedia;
            int width = g.UserInput.GetImageWidth();
            int height = g.UserInput.GetImageHeight();
            int channels = g.UserInput.Get(LatentChannels, 4);
            int batchSize = g.UserInput.Get(T2IParamTypes.BatchSize, 1);
            long seed = g.UserInput.Get(T2IParamTypes.Seed, 0);
            JArray finalLatent;
            if (mode == "Gaussian")
            {
                finalLatent = CreateGaussianLatent(g, channels, width, height, batchSize, seed);
            }
            else if (mode == "Uniform")
            {
                finalLatent = CreateUniformLatent(g, channels, width, height, batchSize, seed);
            }
            else if (mode == "Gaussian + Uniform")
            {
                JArray gaussianLatent = CreateGaussianLatent(g, channels, width, height, batchSize, seed);
                JArray uniformLatent = CreateUniformLatent(g, channels, width, height, batchSize, seed + 1);
                if (!g.UserInput.TryGet(LatentBlendMode, out string twoSourceBlendMode) || twoSourceBlendMode == "Disabled")
                {
                    throw new SwarmUserErrorException("Latent Tools 'Gaussian + Uniform' mode requires a blend mode");
                }
                string twoSourceBlendNode = g.CreateNode("LTBlendLatent", new JObject()
                {
                    ["latent1"] = gaussianLatent,
                    ["latent2"] = uniformLatent,
                    ["mode"] = twoSourceBlendMode,
                    ["ratio"] = g.UserInput.Get(LatentBlendRatio, 0.5),
                    ["seed"] = seed
                });
                finalLatent = [twoSourceBlendNode, 0];
            }
            else
            {
                throw new SwarmUserErrorException($"Unknown Latent Tools init mode '{mode}'");
            }
            if (mode != "Gaussian + Uniform" && g.UserInput.TryGet(LatentBlendMode, out string blendMode) && blendMode != "Disabled")
            {
                string blendNode = g.CreateNode("LTBlendLatent", new JObject()
                {
                    ["latent1"] = swarmLatent.Path,
                    ["latent2"] = finalLatent,
                    ["mode"] = blendMode,
                    ["ratio"] = g.UserInput.Get(LatentBlendRatio, 0.5),
                    ["seed"] = g.UserInput.Get(T2IParamTypes.Seed, 0)
                });
                finalLatent = [blendNode, 0];
            }
            if (g.UserInput.TryGet(LatentOpMode, out string opMode) && opMode != "Disabled")
            {
                string opNode = g.CreateNode("LTLatentOp", new JObject()
                {
                    ["latent"] = finalLatent,
                    ["op"] = opMode,
                    ["arg"] = g.UserInput.Get(LatentOpArg, 0)
                });
                finalLatent = [opNode, 0];
            }
            g.NodeHelpers["latent_tools_noise"] = $"{finalLatent[0]}";
            if (g.UserInput.Get(UseLTKSampler, false))
            {
                g.CurrentMedia = swarmLatent;
            }
            else
            {
                g.CurrentMedia = g.CurrentMedia.WithPath(finalLatent, WGNodeData.DT_LATENT_IMAGE, g.CurrentCompat());
            }
            g.CurrentMedia.Width = width;
            g.CurrentMedia.Height = height;
        }, -8.75);

        WorkflowGenerator.AddStep(g =>
        {
            if (!g.UserInput.Get(UseLTKSampler, false))
            {
                return;
            }
            if (!g.Features.Contains(FeatureId))
            {
                throw new SwarmUserErrorException("Latent Tools LTKSampler specified, but feature isn't installed");
            }
            if (!g.NodeHelpers.TryGetValue("latent_tools_noise", out string latentNoiseNode))
            {
                throw new SwarmUserErrorException("Latent Tools LTKSampler requires a Latent Tools init mode");
            }
            JObject samplerNode = g.Workflow["10"] as JObject;
            if (samplerNode is null)
            {
                throw new SwarmUserErrorException("Latent Tools LTKSampler could not find Swarm's base sampler node");
            }
            string classType = $"{samplerNode["class_type"]}";
            if (classType != "KSamplerAdvanced" && classType != "SwarmKSampler")
            {
                throw new SwarmUserErrorException($"Latent Tools LTKSampler can only replace Swarm's base sampler, not '{classType}'");
            }
            JObject samplerInputs = samplerNode["inputs"] as JObject;
            if (samplerInputs is null)
            {
                throw new SwarmUserErrorException("Latent Tools LTKSampler could not read Swarm's base sampler inputs");
            }
            int steps = samplerInputs["steps"]?.Value<int>() ?? 0;
            int startStep = samplerInputs["start_at_step"]?.Value<int>() ?? 0;
            int endStep = samplerInputs["end_at_step"]?.Value<int>() ?? steps;
            string addNoise = $"{samplerInputs["add_noise"]}";
            string leftoverNoise = $"{samplerInputs["return_with_leftover_noise"]}";
            if (startStep != 0 || endStep < steps || addNoise == "disable" || leftoverNoise == "enable")
            {
                throw new SwarmUserErrorException("Latent Tools LTKSampler currently supports only full base text-to-image sampling");
            }
            if (samplerInputs.TryGetValue("var_seed_strength", out JToken varSeedStrengthToken) && varSeedStrengthToken.Value<double>() != 0)
            {
                throw new SwarmUserErrorException("Latent Tools LTKSampler does not support Swarm variation seed strength");
            }
            if (samplerInputs.ContainsKey("detail_daemon"))
            {
                throw new SwarmUserErrorException("Latent Tools LTKSampler does not support Detail Daemon");
            }
            if (samplerInputs.TryGetValue("tile_sample", out JToken tileSampleToken) && tileSampleToken.Value<bool>())
            {
                throw new SwarmUserErrorException("Latent Tools LTKSampler does not support tiled sampling");
            }
            samplerNode["class_type"] = "LTKSampler";
            samplerNode["inputs"] = new JObject()
            {
                ["model"] = samplerInputs["model"],
                ["extra_seed"] = samplerInputs["noise_seed"],
                ["steps"] = samplerInputs["steps"],
                ["cfg"] = samplerInputs["cfg"],
                ["sampler_name"] = samplerInputs["sampler_name"],
                ["scheduler"] = samplerInputs["scheduler"],
                ["positive"] = samplerInputs["positive"],
                ["negative"] = samplerInputs["negative"],
                ["latent_image"] = samplerInputs["latent_image"],
                ["latent_noise"] = WorkflowGenerator.NodePath(latentNoiseNode, 0),
                ["denoise"] = 1
            };
        }, -4.9);
    }

    /// <summary>Creates an LTGaussianLatent node and returns its latent output path.</summary>
    public static JArray CreateGaussianLatent(WorkflowGenerator g, int channels, int width, int height, int batchSize, long seed)
    {
        string latentNode = g.CreateNode("LTGaussianLatent", new JObject()
        {
            ["channels"] = channels,
            ["width"] = width,
            ["height"] = height,
            ["batch_size"] = batchSize,
            ["seed"] = seed,
            ["mean"] = g.UserInput.Get(GaussianMean, 0),
            ["std"] = g.UserInput.Get(GaussianStd, 1)
        });
        return [latentNode, 0];
    }

    /// <summary>Creates an LTUniformLatent node and returns its latent output path.</summary>
    public static JArray CreateUniformLatent(WorkflowGenerator g, int channels, int width, int height, int batchSize, long seed)
    {
        string latentNode = g.CreateNode("LTUniformLatent", new JObject()
        {
            ["channels"] = channels,
            ["width"] = width,
            ["height"] = height,
            ["batch_size"] = batchSize,
            ["seed"] = seed,
            ["min"] = g.UserInput.Get(UniformMin, -1),
            ["max"] = g.UserInput.Get(UniformMax, 1)
        });
        return [latentNode, 0];
    }
}
