using System;
using System.Collections.Generic;
using System.Linq;
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
        WorkflowGenerator.AddStep(ApplyDetailDaemon, 9);

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

    private static void ApplyDetailDaemon(WorkflowGenerator g)
    {
        if (!g.UserInput.TryGet(DetailAmount, out double detailAmount))
        {
            return;
        }
        if (!g.Features.Contains(FeatureId))
        {
            throw new SwarmUserErrorException("Detail Daemon parameters specified, but the Detail Daemon ComfyUI node pack is not installed.");
        }

        int rewritten = 0;
        foreach (JProperty property in g.NodesOfClasses(new HashSet<string>() { "KSamplerAdvanced", "SwarmKSampler" }).ToArray())
        {
            if (TryRewriteSampler(g, property.Name, property.Value as JObject, detailAmount))
            {
                rewritten++;
            }
        }
        if (rewritten == 0)
        {
            Logs.Warning("Detail Daemon was enabled, but no compatible Swarm sampler nodes were found to rewrite.");
        }
    }

    private static bool TryRewriteSampler(WorkflowGenerator g, string nodeId, JObject node, double detailAmount)
    {
        if (node?["inputs"] is not JObject inputs)
        {
            return false;
        }
        if (!TryGetPath(inputs, "model", out JArray model)
            || !TryGetPath(inputs, "positive", out JArray positive)
            || !TryGetPath(inputs, "negative", out JArray negative)
            || !TryGetPath(inputs, "latent_image", out JArray latentImage))
        {
            return false;
        }

        long seed = GetLong(inputs, "noise_seed", GetLong(inputs, "seed", g.UserInput.Get(T2IParamTypes.Seed, 0)));
        int steps = Math.Max(1, GetInt(inputs, "steps", g.UserInput.Get(T2IParamTypes.Steps, 20)));
        int startStep = Math.Max(0, GetInt(inputs, "start_at_step", 0));
        int endStep = GetInt(inputs, "end_at_step", 10000);
        string samplerName = GetString(inputs, "sampler_name", g.UserInput.Get(ComfyUIBackendExtension.SamplerParam, "euler"));
        string schedulerName = GetString(inputs, "scheduler", g.UserInput.Get(ComfyUIBackendExtension.SchedulerParam, "normal"));
        double cfg = GetDouble(inputs, "cfg", g.UserInput.Get(T2IParamTypes.CFGScale, 7));
        bool addNoise = IsEnabled(inputs, "add_noise", true);

        JArray noise = addNoise
            ? WorkflowGenerator.NodePath(g.CreateNode("RandomNoise", new JObject() { ["noise_seed"] = seed }), 0)
            : WorkflowGenerator.NodePath(g.CreateNode("DisableNoise", new JObject()), 0);
        string samplerSelect = g.CreateNode("KSamplerSelect", new JObject() { ["sampler_name"] = samplerName });
        string detailSampler = g.CreateNode("DetailDaemonSamplerNode", new JObject()
        {
            ["sampler"] = WorkflowGenerator.NodePath(samplerSelect, 0),
            ["detail_amount"] = detailAmount,
            ["start"] = g.UserInput.Get(Start, 0.2),
            ["end"] = g.UserInput.Get(End, 0.8),
            ["bias"] = g.UserInput.Get(Bias, 0.5),
            ["exponent"] = g.UserInput.Get(Exponent, 1),
            ["start_offset"] = g.UserInput.Get(StartOffset, 0),
            ["end_offset"] = g.UserInput.Get(EndOffset, 0),
            ["fade"] = g.UserInput.Get(Fade, 0),
            ["smooth"] = g.UserInput.Get(Smooth, true),
            ["cfg_scale_override"] = g.UserInput.Get(CFGScaleOverride, 0)
        });
        string guider = g.CreateNode("CFGGuider", new JObject()
        {
            ["model"] = model,
            ["positive"] = positive,
            ["negative"] = negative,
            ["cfg"] = cfg
        });
        JArray sigmas = CreateScheduler(g, model, schedulerName, steps);
        if (startStep > 0)
        {
            string split = g.CreateNode("SplitSigmas", new JObject()
            {
                ["sigmas"] = sigmas,
                ["step"] = startStep
            });
            sigmas = WorkflowGenerator.NodePath(split, 1);
        }
        if (endStep < steps)
        {
            string split = g.CreateNode("SplitSigmas", new JObject()
            {
                ["sigmas"] = sigmas,
                ["step"] = endStep
            });
            sigmas = WorkflowGenerator.NodePath(split, 0);
        }

        node["class_type"] = "SamplerCustomAdvanced";
        node["inputs"] = new JObject()
        {
            ["noise"] = noise,
            ["guider"] = WorkflowGenerator.NodePath(guider, 0),
            ["sampler"] = WorkflowGenerator.NodePath(detailSampler, 0),
            ["sigmas"] = sigmas,
            ["latent_image"] = latentImage
        };
        return true;
    }

    private static JArray CreateScheduler(WorkflowGenerator g, JArray model, string schedulerName, int steps)
    {
        string scheduler = schedulerName.ToLowerInvariant();
        if (scheduler == "turbo")
        {
            string turbo = g.CreateNode("SDTurboScheduler", new JObject()
            {
                ["model"] = model,
                ["steps"] = steps,
                ["denoise"] = 1
            });
            return WorkflowGenerator.NodePath(turbo, 0);
        }
        if (scheduler == "karras")
        {
            string karras = g.CreateNode("KarrasScheduler", new JObject()
            {
                ["steps"] = steps,
                ["sigma_max"] = 14.614642,
                ["sigma_min"] = 0.0291675,
                ["rho"] = g.UserInput.Get(T2IParamTypes.SamplerRho, 7)
            });
            return WorkflowGenerator.NodePath(karras, 0);
        }
        string basic = g.CreateNode("BasicScheduler", new JObject()
        {
            ["model"] = model,
            ["steps"] = steps,
            ["scheduler"] = scheduler,
            ["denoise"] = 1
        });
        return WorkflowGenerator.NodePath(basic, 0);
    }

    private static bool TryGetPath(JObject inputs, string key, out JArray path)
    {
        path = inputs[key] as JArray;
        return path is not null && path.Count == 2;
    }

    private static string GetString(JObject inputs, string key, string fallback)
    {
        JToken token = inputs[key];
        string value = token?.ToString();
        return string.IsNullOrWhiteSpace(value) ? fallback : value;
    }

    private static int GetInt(JObject inputs, string key, int fallback)
    {
        JToken token = inputs[key];
        return token is null ? fallback : token.Value<int>();
    }

    private static long GetLong(JObject inputs, string key, long fallback)
    {
        JToken token = inputs[key];
        return token is null ? fallback : token.Value<long>();
    }

    private static double GetDouble(JObject inputs, string key, double fallback)
    {
        JToken token = inputs[key];
        return token is null ? fallback : token.Value<double>();
    }

    private static bool IsEnabled(JObject inputs, string key, bool fallback)
    {
        JToken token = inputs[key];
        if (token is null)
        {
            return fallback;
        }
        string text = token.ToString();
        return text.Equals("enable", StringComparison.OrdinalIgnoreCase)
            || text.Equals("true", StringComparison.OrdinalIgnoreCase);
    }
}
