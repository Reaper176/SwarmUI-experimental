namespace SwarmUI.Builtin_ComfyUIBackend;

/// <summary>Contains the registered names of Swarm-maintained ComfyUI nodes.</summary>
public static class ComfyNodeNames
{
    #region Saving and workflow metadata

    /// <summary>Comfy class name for adding metadata to a save operation.</summary>
    public const string AddSaveMetadataWS = "SwarmAddSaveMetadataWS";

    /// <summary>Comfy class name for saving animated WebP output.</summary>
    public const string SaveAnimatedWebpWS = "SwarmSaveAnimatedWebpWS";

    /// <summary>Comfy class name for saving animation output.</summary>
    public const string SaveAnimationWS = "SwarmSaveAnimationWS";

    /// <summary>Comfy class name for saving image output.</summary>
    public const string SaveImageWS = "SwarmSaveImageWS";

    /// <summary>Comfy class name for assigning a workflow description.</summary>
    public const string WorkflowDescription = "SwarmWorkflowDescription";

    #endregion

    #region Inputs and loading

    /// <summary>Comfy class name for exposing the embedding-name list through object-info.</summary>
    public const string EmbedLoaderListProvider = "SwarmEmbedLoaderListProvider";

    /// <summary>Comfy class name for an audio workflow input.</summary>
    public const string InputAudio = "SwarmInputAudio";

    /// <summary>Comfy class name for a Boolean workflow input.</summary>
    public const string InputBoolean = "SwarmInputBoolean";

    /// <summary>Comfy class name for a checkpoint workflow input.</summary>
    public const string InputCheckpoint = "SwarmInputCheckpoint";

    /// <summary>Comfy class name for a dropdown workflow input.</summary>
    public const string InputDropdown = "SwarmInputDropdown";

    /// <summary>Comfy class name for a floating-point workflow input.</summary>
    public const string InputFloat = "SwarmInputFloat";

    /// <summary>Comfy class name for grouping workflow inputs.</summary>
    public const string InputGroup = "SwarmInputGroup";

    /// <summary>Comfy class name for an image workflow input.</summary>
    public const string InputImage = "SwarmInputImage";

    /// <summary>Comfy class name for an integer workflow input.</summary>
    public const string InputInteger = "SwarmInputInteger";

    /// <summary>Comfy class name for a model-name workflow input.</summary>
    public const string InputModelName = "SwarmInputModelName";

    /// <summary>Comfy class name for a text workflow input.</summary>
    public const string InputText = "SwarmInputText";

    /// <summary>Comfy class name for a video workflow input.</summary>
    public const string InputVideo = "SwarmInputVideo";

    /// <summary>Comfy class name for loading a model without sampling.</summary>
    public const string JustLoadTheModelPlease = "SwarmJustLoadTheModelPlease";

    /// <summary>Comfy class name for loading an LTXV audio VAE.</summary>
    public const string LTXVAudioVAELoader = "SwarmLTXVAudioVAELoader";

    /// <summary>Comfy class name for loading Base64-encoded audio.</summary>
    public const string LoadAudioB64 = "SwarmLoadAudioB64";

    /// <summary>Comfy class name for loading Base64-encoded images.</summary>
    public const string LoadImageB64 = "SwarmLoadImageB64";

    /// <summary>Comfy class name for loading Base64-encoded video.</summary>
    public const string LoadVideoB64 = "SwarmLoadVideoB64";

    /// <summary>Comfy class name for loading a LoRA.</summary>
    public const string LoraLoader = "SwarmLoraLoader";

    #endregion

    #region Image and mask processing

    /// <summary>Comfy class name for overlap-mask cleaning.</summary>
    public const string CleanOverlapMasks = "SwarmCleanOverlapMasks";

    /// <summary>Comfy class name for normalizing one mask against an overmerged set of masks.</summary>
    public const string CleanOverlapMasksExceptSelf = "SwarmCleanOverlapMasksExceptSelf";

    /// <summary>Comfy class name for excluding one mask from another.</summary>
    public const string ExcludeFromMask = "SwarmExcludeFromMask";

    /// <summary>Comfy class name for color-corrected masked image compositing.</summary>
    public const string ImageCompositeMaskedColorCorrecting = "SwarmImageCompositeMaskedColorCorrecting";

    /// <summary>Comfy class name for cropping images.</summary>
    public const string ImageCrop = "SwarmImageCrop";

    /// <summary>Comfy class name for retrieving image height.</summary>
    public const string ImageHeight = "SwarmImageHeight";

    /// <summary>Comfy class name for adding noise to images.</summary>
    public const string ImageNoise = "SwarmImageNoise";

    /// <summary>Comfy class name for scaling images to a megapixel target.</summary>
    public const string ImageScaleForMP = "SwarmImageScaleForMP";

    /// <summary>Comfy class name for retrieving image width.</summary>
    public const string ImageWidth = "SwarmImageWidth";

    /// <summary>Comfy class name for blurring masks.</summary>
    public const string MaskBlur = "SwarmMaskBlur";

    /// <summary>Comfy class name for finding mask bounds.</summary>
    public const string MaskBounds = "SwarmMaskBounds";

    /// <summary>Comfy class name for expanding masks.</summary>
    public const string MaskGrow = "SwarmMaskGrow";

    /// <summary>Comfy class name for thresholding masks.</summary>
    public const string MaskThreshold = "SwarmMaskThreshold";

    /// <summary>Comfy class name for merging masks to correct overlaps.</summary>
    public const string OverMergeMasksForOverlapFix = "SwarmOverMergeMasksForOverlapFix";

    /// <summary>Comfy class name for removing image backgrounds.</summary>
    public const string RemBg = "SwarmRemBg";

    /// <summary>Comfy class name for creating a square mask from percentages.</summary>
    public const string SquareMaskFromPercent = "SwarmSquareMaskFromPercent";

    #endregion

    #region Sampling, latents, and models

    /// <summary>Comfy class name for applying Anima ControlNet-LLLite weights.</summary>
    public const string AnimaLLLite = "SwarmAnimaLLLite";

    /// <summary>Comfy class name for configuring Detail Daemon options.</summary>
    public const string DetailDaemonOptions = "SwarmDetailDaemonOptions";

    /// <summary>Comfy class name for extracting a LoRA from model differences.</summary>
    public const string ExtractLora = "SwarmExtractLora";

    /// <summary>Comfy class name for Swarm's KSampler implementation.</summary>
    public const string KSampler = "SwarmKSampler";

    /// <summary>Comfy class name for masked latent blending.</summary>
    public const string LatentBlendMasked = "SwarmLatentBlendMasked";

    /// <summary>Comfy class name for configuring model tiling.</summary>
    public const string ModelTiling = "SwarmModelTiling";

    /// <summary>Comfy class name for creating an offset empty latent image.</summary>
    public const string OffsetEmptyLatentImage = "SwarmOffsetEmptyLatentImage";

    /// <summary>Comfy class name for reference-only conditioning.</summary>
    public const string ReferenceOnly = "SwarmReferenceOnly";

    /// <summary>Comfy class name for configuring a tileable VAE.</summary>
    public const string TileableVAE = "SwarmTileableVAE";

    /// <summary>Comfy class name for reversing latent sampling.</summary>
    public const string Unsampler = "SwarmUnsampler";

    #endregion

    #region Text, segmentation, and detection

    /// <summary>Comfy class name for coupling attention between prompt regions.</summary>
    public const string AttentionCouple = "SwarmAttentionCouple";

    /// <summary>Comfy class name for CLIPSeg image segmentation.</summary>
    public const string ClipSeg = "SwarmClipSeg";

    /// <summary>Comfy class name for advanced CLIP text encoding.</summary>
    public const string ClipTextEncodeAdvanced = "SwarmClipTextEncodeAdvanced";

    /// <summary>Comfy class name for reading SAM2 bounding boxes from JSON.</summary>
    public const string Sam2BBoxFromJson = "SwarmSam2BBoxFromJson";

    /// <summary>Comfy class name for post-processing SAM2 masks.</summary>
    public const string Sam2MaskPostProcess = "SwarmSam2MaskPostProcess";

    /// <summary>Comfy class name for reading SAM3 bounding boxes from JSON.</summary>
    public const string Sam3BBoxFromJson = "SwarmSam3BBoxFromJson";

    /// <summary>Comfy class name for post-processing SAM3 masks.</summary>
    public const string Sam3MaskPostProcess = "SwarmSam3MaskPostProcess";

    /// <summary>Comfy class name for reading SAM3 points from JSON.</summary>
    public const string Sam3PointsFromJson = "SwarmSam3PointsFromJson";

    /// <summary>Comfy class name for YOLO object detection.</summary>
    public const string YoloDetection = "SwarmYoloDetection";

    #endregion

    #region Audio, video, and utility nodes

    /// <summary>Comfy class name for counting frames.</summary>
    public const string CountFrames = "SwarmCountFrames";

    /// <summary>Comfy class name for inspecting audio data.</summary>
    public const string DebugAudio = "SwarmDebugAudio";

    /// <summary>Comfy class name for ensuring audio data is present.</summary>
    public const string EnsureAudio = "SwarmEnsureAudio";

    /// <summary>Comfy class name for integer addition.</summary>
    public const string IntAdd = "SwarmIntAdd";

    /// <summary>Comfy class name for trimming frame sequences.</summary>
    public const string TrimFrames = "SwarmTrimFrames";

    /// <summary>Comfy class name for creating boomerang video loops.</summary>
    public const string VideoBoomerang = "SwarmVideoBoomerang";

    /// <summary>Comfy class name for resampling video frame rates.</summary>
    public const string VideoResampleFPS = "SwarmVideoResampleFPS";

    #endregion
}
