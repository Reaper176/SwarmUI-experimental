namespace SwarmUI.Builtin_ComfyUIBackend;

/// <summary>Provides the built-in mapping of ComfyUI node names to Swarm feature IDs.</summary>
public static class ComfyCapabilityCatalog
{
    /// <summary>Creates a mutable mapping of known ComfyUI node names to their feature IDs.</summary>
    public static Dictionary<string, string> CreateNodeToFeatureMap()
    {
        return new()
        {
            [ComfyNodeNames.LoadImageB64] = "comfy_loadimage_b64",
            [ComfyNodeNames.SaveImageWS] = "comfy_saveimage_ws",
            [ComfyNodeNames.JustLoadTheModelPlease] = "comfy_just_load_model",
            [ComfyNodeNames.LatentBlendMasked] = "comfy_latent_blend_masked",
            [ComfyNodeNames.KSampler] = "variation_seed",
            ["FreeU"] = "freeu",
            ["AITemplateLoader"] = "aitemplate",
            ["IPAdapter"] = "ipadapter",
            ["IPAdapterApply"] = "ipadapter",
            ["IPAdapterModelLoader"] = "cubiqipadapter",
            ["IPAdapterUnifiedLoader"] = "cubiqipadapterunified",
            ["MiDaS-DepthMapPreprocessor"] = "controlnetpreprocessors",
            ["RIFE VFI"] = "frameinterps",
            ["GIMMVFI_interpolate"] = "frameinterps_gimmvfi",
            ["SAM3Segmentation"] = "sam3",
            ["SAM3Grounding"] = "sam3",
            [ComfyNodeNames.YoloDetection] = "yolov8",
            ["PixArtCheckpointLoader"] = "extramodelspixart",
            ["SanaCheckpointLoader"] = "extramodelssana",
            ["CheckpointLoaderNF4"] = "bnb_nf4",
            ["UnetLoaderGGUF"] = "gguf",
            ["NunchakuFluxDiTLoader"] = "nunchaku",
            ["TensorRTLoader"] = "tensorrt",
            ["TeaCache"] = "teacache",
            ["TeaCacheForVidGen"] = "teacache",
            ["TeaCacheForImgGen"] = "teacache_oldvers",
            ["OverrideCLIPDevice"] = "set_clip_device",
            ["INPAINT_LoadInpaintModel"] = "inpaintnodes",
            ["INPAINT_InpaintWithModel"] = "inpaintnodes"
        };
    }

    /// <summary>Updates feature support based on a detected ComfyUI node name.</summary>
    public static void ApplyDetectedNodeFeature(string nodeName, Dictionary<string, string> nodeToFeatureMap, HashSet<string> featuresSupported, HashSet<string> featuresDiscardIfNotFound)
    {
        if (nodeToFeatureMap.TryGetValue(nodeName, out string featureId))
        {
            featuresSupported.Add(featureId);
            featuresDiscardIfNotFound.Remove(featureId);
        }
    }
}
