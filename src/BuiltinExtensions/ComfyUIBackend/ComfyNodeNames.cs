namespace SwarmUI.Builtin_ComfyUIBackend;

/// <summary>Contains the registered names of Swarm-maintained ComfyUI nodes.</summary>
public static class ComfyNodeNames
{
    #region Saving and workflow metadata

    /// <summary>The SwarmAddSaveMetadataWS node name.</summary>
    public const string AddSaveMetadataWS = "SwarmAddSaveMetadataWS";

    /// <summary>The SwarmSaveAnimatedWebpWS node name.</summary>
    public const string SaveAnimatedWebpWS = "SwarmSaveAnimatedWebpWS";

    /// <summary>The SwarmSaveAnimationWS node name.</summary>
    public const string SaveAnimationWS = "SwarmSaveAnimationWS";

    /// <summary>The SwarmSaveImageWS node name.</summary>
    public const string SaveImageWS = "SwarmSaveImageWS";

    /// <summary>The SwarmWorkflowDescription node name.</summary>
    public const string WorkflowDescription = "SwarmWorkflowDescription";

    #endregion

    #region Inputs and loading

    /// <summary>The SwarmEmbedLoaderListProvider node name.</summary>
    public const string EmbedLoaderListProvider = "SwarmEmbedLoaderListProvider";

    /// <summary>The SwarmInputAudio node name.</summary>
    public const string InputAudio = "SwarmInputAudio";

    /// <summary>The SwarmInputBoolean node name.</summary>
    public const string InputBoolean = "SwarmInputBoolean";

    /// <summary>The SwarmInputCheckpoint node name.</summary>
    public const string InputCheckpoint = "SwarmInputCheckpoint";

    /// <summary>The SwarmInputDropdown node name.</summary>
    public const string InputDropdown = "SwarmInputDropdown";

    /// <summary>The SwarmInputFloat node name.</summary>
    public const string InputFloat = "SwarmInputFloat";

    /// <summary>The SwarmInputGroup node name.</summary>
    public const string InputGroup = "SwarmInputGroup";

    /// <summary>The SwarmInputImage node name.</summary>
    public const string InputImage = "SwarmInputImage";

    /// <summary>The SwarmInputInteger node name.</summary>
    public const string InputInteger = "SwarmInputInteger";

    /// <summary>The SwarmInputModelName node name.</summary>
    public const string InputModelName = "SwarmInputModelName";

    /// <summary>The SwarmInputText node name.</summary>
    public const string InputText = "SwarmInputText";

    /// <summary>The SwarmInputVideo node name.</summary>
    public const string InputVideo = "SwarmInputVideo";

    /// <summary>The SwarmJustLoadTheModelPlease node name.</summary>
    public const string JustLoadTheModelPlease = "SwarmJustLoadTheModelPlease";

    /// <summary>The SwarmLTXVAudioVAELoader node name.</summary>
    public const string LTXVAudioVAELoader = "SwarmLTXVAudioVAELoader";

    /// <summary>The SwarmLoadAudioB64 node name.</summary>
    public const string LoadAudioB64 = "SwarmLoadAudioB64";

    /// <summary>The SwarmLoadImageB64 node name.</summary>
    public const string LoadImageB64 = "SwarmLoadImageB64";

    /// <summary>The SwarmLoadVideoB64 node name.</summary>
    public const string LoadVideoB64 = "SwarmLoadVideoB64";

    /// <summary>The SwarmLoraLoader node name.</summary>
    public const string LoraLoader = "SwarmLoraLoader";

    #endregion

    #region Image and mask processing

    /// <summary>The SwarmCleanOverlapMasks node name.</summary>
    public const string CleanOverlapMasks = "SwarmCleanOverlapMasks";

    /// <summary>The SwarmCleanOverlapMasksExceptSelf node name.</summary>
    public const string CleanOverlapMasksExceptSelf = "SwarmCleanOverlapMasksExceptSelf";

    /// <summary>The SwarmExcludeFromMask node name.</summary>
    public const string ExcludeFromMask = "SwarmExcludeFromMask";

    /// <summary>The SwarmImageCompositeMaskedColorCorrecting node name.</summary>
    public const string ImageCompositeMaskedColorCorrecting = "SwarmImageCompositeMaskedColorCorrecting";

    /// <summary>The SwarmImageCrop node name.</summary>
    public const string ImageCrop = "SwarmImageCrop";

    /// <summary>The SwarmImageHeight node name.</summary>
    public const string ImageHeight = "SwarmImageHeight";

    /// <summary>The SwarmImageNoise node name.</summary>
    public const string ImageNoise = "SwarmImageNoise";

    /// <summary>The SwarmImageScaleForMP node name.</summary>
    public const string ImageScaleForMP = "SwarmImageScaleForMP";

    /// <summary>The SwarmImageWidth node name.</summary>
    public const string ImageWidth = "SwarmImageWidth";

    /// <summary>The SwarmMaskBlur node name.</summary>
    public const string MaskBlur = "SwarmMaskBlur";

    /// <summary>The SwarmMaskBounds node name.</summary>
    public const string MaskBounds = "SwarmMaskBounds";

    /// <summary>The SwarmMaskGrow node name.</summary>
    public const string MaskGrow = "SwarmMaskGrow";

    /// <summary>The SwarmMaskThreshold node name.</summary>
    public const string MaskThreshold = "SwarmMaskThreshold";

    /// <summary>The SwarmOverMergeMasksForOverlapFix node name.</summary>
    public const string OverMergeMasksForOverlapFix = "SwarmOverMergeMasksForOverlapFix";

    /// <summary>The SwarmRemBg node name.</summary>
    public const string RemBg = "SwarmRemBg";

    /// <summary>The SwarmSquareMaskFromPercent node name.</summary>
    public const string SquareMaskFromPercent = "SwarmSquareMaskFromPercent";

    #endregion

    #region Sampling, latents, and models

    /// <summary>The SwarmAnimaLLLite node name.</summary>
    public const string AnimaLLLite = "SwarmAnimaLLLite";

    /// <summary>The SwarmDetailDaemonOptions node name.</summary>
    public const string DetailDaemonOptions = "SwarmDetailDaemonOptions";

    /// <summary>The SwarmExtractLora node name.</summary>
    public const string ExtractLora = "SwarmExtractLora";

    /// <summary>The SwarmKSampler node name.</summary>
    public const string KSampler = "SwarmKSampler";

    /// <summary>The SwarmLatentBlendMasked node name.</summary>
    public const string LatentBlendMasked = "SwarmLatentBlendMasked";

    /// <summary>The SwarmModelTiling node name.</summary>
    public const string ModelTiling = "SwarmModelTiling";

    /// <summary>The SwarmOffsetEmptyLatentImage node name.</summary>
    public const string OffsetEmptyLatentImage = "SwarmOffsetEmptyLatentImage";

    /// <summary>The SwarmReferenceOnly node name.</summary>
    public const string ReferenceOnly = "SwarmReferenceOnly";

    /// <summary>The SwarmTileableVAE node name.</summary>
    public const string TileableVAE = "SwarmTileableVAE";

    /// <summary>The SwarmUnsampler node name.</summary>
    public const string Unsampler = "SwarmUnsampler";

    #endregion

    #region Text, segmentation, and detection

    /// <summary>The SwarmAttentionCouple node name.</summary>
    public const string AttentionCouple = "SwarmAttentionCouple";

    /// <summary>The SwarmClipSeg node name.</summary>
    public const string ClipSeg = "SwarmClipSeg";

    /// <summary>The SwarmClipTextEncodeAdvanced node name.</summary>
    public const string ClipTextEncodeAdvanced = "SwarmClipTextEncodeAdvanced";

    /// <summary>The SwarmSam2BBoxFromJson node name.</summary>
    public const string Sam2BBoxFromJson = "SwarmSam2BBoxFromJson";

    /// <summary>The SwarmSam2MaskPostProcess node name.</summary>
    public const string Sam2MaskPostProcess = "SwarmSam2MaskPostProcess";

    /// <summary>The SwarmSam3BBoxFromJson node name.</summary>
    public const string Sam3BBoxFromJson = "SwarmSam3BBoxFromJson";

    /// <summary>The SwarmSam3MaskPostProcess node name.</summary>
    public const string Sam3MaskPostProcess = "SwarmSam3MaskPostProcess";

    /// <summary>The SwarmSam3PointsFromJson node name.</summary>
    public const string Sam3PointsFromJson = "SwarmSam3PointsFromJson";

    /// <summary>The SwarmYoloDetection node name.</summary>
    public const string YoloDetection = "SwarmYoloDetection";

    #endregion

    #region Audio, video, and utility nodes

    /// <summary>The SwarmCountFrames node name.</summary>
    public const string CountFrames = "SwarmCountFrames";

    /// <summary>The SwarmDebugAudio node name.</summary>
    public const string DebugAudio = "SwarmDebugAudio";

    /// <summary>The SwarmEnsureAudio node name.</summary>
    public const string EnsureAudio = "SwarmEnsureAudio";

    /// <summary>The SwarmIntAdd node name.</summary>
    public const string IntAdd = "SwarmIntAdd";

    /// <summary>The SwarmTrimFrames node name.</summary>
    public const string TrimFrames = "SwarmTrimFrames";

    /// <summary>The SwarmVideoBoomerang node name.</summary>
    public const string VideoBoomerang = "SwarmVideoBoomerang";

    /// <summary>The SwarmVideoResampleFPS node name.</summary>
    public const string VideoResampleFPS = "SwarmVideoResampleFPS";

    #endregion
}
