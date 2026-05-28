using System.IO;
using Newtonsoft.Json.Linq;
using SwarmUI.Builtin_ComfyUIBackend;
using SwarmUI.Core;
using SwarmUI.Media;
using SwarmUI.Text2Image;
using SwarmUI.Utils;

namespace SwarmUI.Extensions.LatentColorTools;

/// <summary>SwarmUI extension for DenRakEiw's Latent_Nodes ComfyUI latent color tools.</summary>
public class LatentColorToolsExtension : Extension
{
    /// <summary>Generate parameter group for latent color controls.</summary>
    public static T2IParamGroup LatentColorGroup;

    /// <summary>Controls which latent color operation should run.</summary>
    public static T2IRegisteredParam<string> Mode;

    /// <summary>Hue adjustment in degrees.</summary>
    public static T2IRegisteredParam<double> Hue;

    /// <summary>Saturation multiplier.</summary>
    public static T2IRegisteredParam<double> Saturation;

    /// <summary>Brightness additive adjustment.</summary>
    public static T2IRegisteredParam<double> Brightness;

    /// <summary>Contrast multiplier.</summary>
    public static T2IRegisteredParam<double> Contrast;

    /// <summary>Sharpness multiplier.</summary>
    public static T2IRegisteredParam<double> Sharpness;

    /// <summary>Processing device selection passed to the Comfy node.</summary>
    public static T2IRegisteredParam<string> Device;

    /// <summary>Batch size selection passed to the Comfy node.</summary>
    public static T2IRegisteredParam<int> BatchSize;

    /// <summary>Reference image used for color matching.</summary>
    public static T2IRegisteredParam<Image> ReferenceImage;

    /// <summary>Color matching method selection.</summary>
    public static T2IRegisteredParam<string> MatchMethod;

    /// <summary>Color matching strength factor.</summary>
    public static T2IRegisteredParam<double> MatchFactor;

    /// <inheritdoc/>
    public override void OnPreInit()
    {
        string nodeDirectory = Path.GetFullPath(Path.Join(FilePath, "LatentColorNodes"));
        ComfyUISelfStartBackend.CustomNodePaths.Add(nodeDirectory);
        Logs.Init($"Latent Color Tools: added {nodeDirectory} to ComfyUI custom node paths");
    }

    /// <inheritdoc/>
    public override void OnInit()
    {
        ExtensionName = "Latent Color Tools";
        Version = "1.0.0";
        ExtensionAuthor = "DenRakEiw, SwarmUI extension by Reaper176";
        Description = "Adds Swarm generate controls for DenRakEiw's latent image adjustment and color matching ComfyUI nodes.";
        License = "MIT";
        Tags = ["parameters", "latent", "color"];

        LatentColorGroup = new("Latent Color Tools", Toggles: false, Open: false, IsAdvanced: false,
            Description: "Adjust hue, saturation, brightness, contrast, sharpness, and reference color matching directly in latent space.");
        int order = 0;
        Mode = T2IParamTypes.Register<string>(new("[LatentColor] Mode", "Applies DenRakEiw Latent_Nodes directly to Swarm's active latent before sampling.",
            "Disabled", Group: LatentColorGroup, OrderPriority: order++, IgnoreIf: "Disabled", ChangeWeight: 1,
            GetValues: _ => ["Disabled", "Image Adjust", "Color Match", "Image Adjust + Color Match"]));
        Hue = T2IParamTypes.Register<double>(new("[LatentColor] Hue", "Hue shift in degrees for Image Adjust.",
            "0", Min: -180, Max: 180, Step: 1, Group: LatentColorGroup, OrderPriority: order++, DependNonDefault: Mode.Type.ID));
        Saturation = T2IParamTypes.Register<double>(new("[LatentColor] Saturation", "Saturation multiplier for Image Adjust.",
            "1", Min: 0, Max: 3, Step: 0.05, Group: LatentColorGroup, OrderPriority: order++, DependNonDefault: Mode.Type.ID));
        Brightness = T2IParamTypes.Register<double>(new("[LatentColor] Brightness", "Brightness adjustment for Image Adjust.",
            "0", Min: -1, Max: 1, Step: 0.05, Group: LatentColorGroup, OrderPriority: order++, DependNonDefault: Mode.Type.ID));
        Contrast = T2IParamTypes.Register<double>(new("[LatentColor] Contrast", "Contrast multiplier for Image Adjust.",
            "1", Min: 0, Max: 3, Step: 0.05, Group: LatentColorGroup, OrderPriority: order++, DependNonDefault: Mode.Type.ID));
        Sharpness = T2IParamTypes.Register<double>(new("[LatentColor] Sharpness", "Sharpness multiplier for Image Adjust.",
            "1", Min: 0, Max: 3, Step: 0.05, Group: LatentColorGroup, OrderPriority: order++, DependNonDefault: Mode.Type.ID));
        ReferenceImage = T2IParamTypes.Register<Image>(new("[LatentColor] Reference Image", "Reference image to VAE-encode and use for Color Match.",
            null, Group: LatentColorGroup, OrderPriority: order++, ChangeWeight: 2, DependNonDefault: Mode.Type.ID));
        MatchMethod = T2IParamTypes.Register<string>(new("[LatentColor] Match Method", "Color matching algorithm for Color Match.",
            "LAB", Group: LatentColorGroup, OrderPriority: order++, DependNonDefault: Mode.Type.ID,
            GetValues: _ => ["LAB", "YCbCr", "RGB", "LUV", "YUV", "XYZ", "mkl", "hm", "reinhard", "mvgd", "hm-mvgd-hm", "hm-mkl-hm"]));
        MatchFactor = T2IParamTypes.Register<double>(new("[LatentColor] Match Factor", "Strength of the Color Match operation.",
            "1", Min: 0, Max: 3, Step: 0.05, ViewType: ParamViewType.SLIDER, Group: LatentColorGroup, OrderPriority: order++, DependNonDefault: Mode.Type.ID));
        Device = T2IParamTypes.Register<string>(new("[LatentColor] Device", "Processing device used by the Latent_Nodes operations.",
            "auto", Group: LatentColorGroup, OrderPriority: order++, IsAdvanced: true, DependNonDefault: Mode.Type.ID,
            GetValues: _ => ["auto", "cpu", "gpu"]));
        BatchSize = T2IParamTypes.Register<int>(new("[LatentColor] Batch Size", "Batch size for Latent_Nodes processing. 0 processes the whole batch at once.",
            "0", Min: 0, Max: 1024, Step: 1, Group: LatentColorGroup, OrderPriority: order++, IsAdvanced: true, DependNonDefault: Mode.Type.ID));

        WorkflowGenerator.AddStep(g =>
        {
            if (!g.UserInput.TryGet(Mode, out string mode) || mode == "Disabled")
            {
                return;
            }
            if (g.CurrentMedia is null)
            {
                return;
            }
            if (g.CurrentMedia.DataType != WGNodeData.DT_LATENT_IMAGE)
            {
                return;
            }
            WGNodeData latent = g.CurrentMedia;
            if (mode == "Image Adjust" || mode == "Image Adjust + Color Match")
            {
                string adjustNode = g.CreateNode("LatentImageAdjust", new JObject()
                {
                    ["latent"] = latent.Path,
                    ["hue"] = g.UserInput.Get(Hue, 0),
                    ["saturation"] = g.UserInput.Get(Saturation, 1),
                    ["brightness"] = g.UserInput.Get(Brightness, 0),
                    ["contrast"] = g.UserInput.Get(Contrast, 1),
                    ["sharpness"] = g.UserInput.Get(Sharpness, 1),
                    ["device"] = g.UserInput.Get(Device, "auto"),
                    ["batch_size"] = g.UserInput.Get(BatchSize, 0)
                });
                latent = latent.WithPath([adjustNode, 0], WGNodeData.DT_LATENT_IMAGE, g.CurrentCompat());
            }
            if (mode == "Color Match" || mode == "Image Adjust + Color Match")
            {
                if (!g.UserInput.TryGet(ReferenceImage, out Image referenceImage) || referenceImage is null)
                {
                    throw new SwarmUserErrorException("Latent Color Tools Color Match requires a reference image");
                }
                WGNodeData reference = g.LoadImage(referenceImage, "Latent Color Reference Image", true).AsLatentImage(g.CurrentVae);
                string matchNode = g.CreateNode("LatentColorMatch", new JObject()
                {
                    ["latent"] = latent.Path,
                    ["reference"] = reference.Path,
                    ["method"] = g.UserInput.Get(MatchMethod, "LAB"),
                    ["factor"] = g.UserInput.Get(MatchFactor, 1),
                    ["device"] = g.UserInput.Get(Device, "auto"),
                    ["batch_size"] = g.UserInput.Get(BatchSize, 0)
                });
                latent = latent.WithPath([matchNode, 0], WGNodeData.DT_LATENT_IMAGE, g.CurrentCompat());
            }
            g.CurrentMedia = latent;
        }, -8.6);

        Logs.Init("Latent Color Tools extension initialized.");
    }
}
