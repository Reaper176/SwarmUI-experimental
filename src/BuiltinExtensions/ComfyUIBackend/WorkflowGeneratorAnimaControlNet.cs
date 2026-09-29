using Newtonsoft.Json.Linq;
using SwarmUI.Text2Image;
using SwarmUI.Utils;

namespace SwarmUI.Builtin_ComfyUIBackend;

public partial class WorkflowGenerator
{
    /// <summary>Returns the backend capability needed for a classified Anima ControlNet, or null for other controls.</summary>
    public static string GetAnimaControlNetFeature(T2IModel controlModel)
    {
        return controlModel?.ModelClass?.ID switch
        {
            "anima/controlnet-vace" => ComfyCapabilityCatalog.AnimaVaceControlNetFeature,
            "anima/controlnet" => ComfyCapabilityCatalog.AnimaLLLiteRemapFeature,
            _ => null
        };
    }

    /// <summary>Applies Anima guidance using the custom nodes' automatic host-block remapping.</summary>
    public void ApplyAnimaControlNet(T2IModel controlModel, WGNodeData image, double strength, double start, double end)
    {
        if (!IsAnima())
        {
            throw new SwarmUserErrorException("Anima ControlNets require an Anima generation model.");
        }
        string feature = GetAnimaControlNetFeature(controlModel);
        bool isVace = feature == ComfyCapabilityCatalog.AnimaVaceControlNetFeature;
        if (feature is null || RestrictCustomNodes || !Features.Contains(feature))
        {
            string requiredNodes = isVace
                ? "ComfyUI-Anima-Remap and the Anima VACE-capable PineCookie ComfyUI-Advanced-ControlNet fork (fix/anima-vace-hardening)"
                : "ComfyUI-Anima-Remap (AnimaLLLiteRemapApply)";
            throw new SwarmUserErrorException($"Anima ControlNet requires {requiredNodes}. Enable custom nodes and install/update these nodes on the selected ComfyUI backend, then restart it.");
        }
        if (isVace)
        {
            string loader = CreateNode(ComfyNodeNames.AdvancedControlNetLoader, new JObject()
            {
                ["cnet"] = controlModel.ToString(ModelFolderFormat)
            });
            string remap = CreateNode(ComfyNodeNames.AnimaVaceRemap, new JObject()
            {
                ["control_net"] = NodePath(loader, 0),
                ["model"] = CurrentModel.Path,
                ["auto_remap"] = true,
                ["manifest"] = "Auto (Recommended)"
            });
            string apply = CreateNode(ComfyNodeNames.AdvancedControlNetApply, new JObject()
            {
                ["positive"] = FinalPrompt,
                ["negative"] = FinalNegativePrompt,
                ["control_net"] = NodePath(remap, 0),
                ["image"] = image.Path,
                ["vae_optional"] = CurrentVae.Path,
                ["strength"] = strength,
                ["start_percent"] = start,
                ["end_percent"] = end
            });
            FinalPrompt = NodePath(apply, 0);
            FinalNegativePrompt = NodePath(apply, 1);
        }
        else
        {
            JObject inputs = new()
            {
                ["model"] = CurrentModel.Path,
                ["lllite_name"] = controlModel.ToString(ModelFolderFormat),
                ["image"] = image.Path,
                ["strength"] = strength,
                ["start_percent"] = start,
                ["end_percent"] = end,
                ["auto_remap"] = true,
                ["manifest"] = "Auto (Recommended)",
                ["extend_to_new_layers"] = false,
                ["extend_strength"] = 0.5,
                ["preserve_wrapper"] = true
            };
            if (FinalMask is not null)
            {
                inputs["mask"] = FinalMask;
            }
            string apply = CreateNode(ComfyNodeNames.AnimaLLLiteRemap, inputs);
            CurrentModel = CurrentModel.WithPath(NodePath(apply, 0));
        }
    }
}
