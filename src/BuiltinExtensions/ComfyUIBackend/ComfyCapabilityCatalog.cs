namespace SwarmUI.Builtin_ComfyUIBackend;

/// <summary>Provides the built-in mapping of ComfyUI node names to Swarm feature IDs.</summary>
public static class ComfyCapabilityCatalog
{
    /// <summary>Feature ID requiring the Swarm MiniMax H3 joint audio/video empty-latent node.</summary>
    public const string EmptyMiniMaxH3LatentAVFeature = "comfy_empty_minimax_h3_latent_av";

    /// <summary>Feature ID requiring the Swarm watermark node.</summary>
    public const string WatermarkFeature = "swarm_watermark";

    /// <summary>Prefix for backend-local model-attention option feature IDs.</summary>
    public const string ModelAttentionBackendValueFeaturePrefix = "model_attention_backend_value_";

    /// <summary>Encodes an exact Comfy model-attention option as a safe backend feature ID.</summary>
    /// <param name="value">The exact option value accepted by the Comfy node.</param>
    /// <returns>A deterministic feature ID containing the UTF-8 option bytes as hexadecimal.</returns>
    public static string ModelAttentionBackendValueFeature(string value)
    {
        return $"{ModelAttentionBackendValueFeaturePrefix}{Convert.ToHexString(System.Text.Encoding.UTF8.GetBytes(value))}";
    }

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
            [ComfyNodeNames.ModelAttentionBackend] = "model_attention_backend",
            [ComfyNodeNames.EmptyMiniMaxH3LatentAV] = EmptyMiniMaxH3LatentAVFeature,
            [ComfyNodeNames.AudioSilentMaskPrefixSuffix] = "audio_silent_mask_prefix_suffix",
            [ComfyNodeNames.Watermark] = WatermarkFeature,
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

    /// <summary>Interprets detected ComfyUI node types as a self-contained set of supported features.</summary>
    /// <param name="nodeTypes">The ComfyUI node types exposed by the backend.</param>
    /// <param name="baselineFeatures">The feature IDs presumed to be supported before node detection.</param>
    /// <param name="discardIfNotFound">The presumed feature IDs to remove unless a mapped node confirms them.</param>
    /// <param name="nodeToFeatureMap">The mapping of ComfyUI node types to feature IDs.</param>
    /// <param name="suppressedFeatures">The feature IDs to remove from the interpreted result.</param>
    /// <param name="modelFolderFormat">The path separator format used by the backend's model folders.</param>
    /// <returns>A new mutable set containing the interpreted feature IDs.</returns>
    public static HashSet<string> Interpret(
        IReadOnlySet<string> nodeTypes,
        IEnumerable<string> baselineFeatures,
        IReadOnlySet<string> discardIfNotFound,
        IReadOnlyDictionary<string, string> nodeToFeatureMap,
        IReadOnlySet<string> suppressedFeatures,
        string modelFolderFormat)
    {
        HashSet<string> features = [.. baselineFeatures];
        HashSet<string> unresolvedDiscardFeatures = [.. discardIfNotFound];
        foreach (string nodeType in nodeTypes)
        {
            if (nodeToFeatureMap.TryGetValue(nodeType, out string featureId))
            {
                features.Add(featureId);
                unresolvedDiscardFeatures.Remove(featureId);
            }
        }

        features.ExceptWith(unresolvedDiscardFeatures);

        string hookFeature = "hook_lora_scheduling";
        string interpolatedHookFeature = "hook_lora_interpolated_scheduling";
        string[] requiredHookNodes = ["CreateHookLora", "CreateHookKeyframe", "SetHookKeyframes", "SetClipHooks"];
        bool hookSchedulingSupported = requiredHookNodes.All(nodeTypes.Contains);
        if (hookSchedulingSupported)
        {
            features.Add(hookFeature);
            if (nodeTypes.Contains("CreateHookKeyframesInterpolated"))
            {
                features.Add(interpolatedHookFeature);
            }
            else
            {
                features.Remove(interpolatedHookFeature);
            }
        }
        else
        {
            features.Remove(hookFeature);
            features.Remove(interpolatedHookFeature);
        }

        string seedVRFeature = "seedvr2";
        string[] requiredSeedVRNodes = [ComfyNodeNames.SeedVR2Preprocess, ComfyNodeNames.SeedVR2Conditioning, ComfyNodeNames.SeedVR2PostProcessing];
        if (requiredSeedVRNodes.All(nodeTypes.Contains))
        {
            features.Add(seedVRFeature);
        }
        else
        {
            features.Remove(seedVRFeature);
        }

        string seedVRTemporalChunkingFeature = "seedvr2_temporal_chunking";
        bool seedVRTemporalChunkingSupported = nodeTypes.Contains(ComfyNodeNames.SeedVR2TemporalChunk) && nodeTypes.Contains(ComfyNodeNames.SeedVR2TemporalMerge);
        if (seedVRTemporalChunkingSupported)
        {
            features.Add(seedVRTemporalChunkingFeature);
        }
        else
        {
            features.Remove(seedVRTemporalChunkingFeature);
        }

        features.ExceptWith(suppressedFeatures);
        features.Remove("folderbackslash");
        features.Remove("folderslash");
        features.Add(modelFolderFormat == "\\" ? "folderbackslash" : "folderslash");
        return features;
    }
}
