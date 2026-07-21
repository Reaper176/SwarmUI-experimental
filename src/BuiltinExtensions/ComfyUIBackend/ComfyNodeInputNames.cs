namespace SwarmUI.Builtin_ComfyUIBackend;

/// <summary>Contains schema-backed input names for ComfyUI nodes used by built-in Swarm code.</summary>
public static class ComfyNodeInputNames
{
    #region Sampling, latents, and models

    /// <summary>Input names for the Anima ControlNet-LLLite node.</summary>
    public static class AnimaLLLite
    {
        /// <summary>Input name for the ending application percentage.</summary>
        public const string EndPercent = "end_percent";
        /// <summary>Input name for the source image.</summary>
        public const string Image = "image";
        /// <summary>Input name for the LLLite model.</summary>
        public const string LLLiteName = "lllite_name";
        /// <summary>Input name for the application mask.</summary>
        public const string Mask = "mask";
        /// <summary>Input name for the diffusion model.</summary>
        public const string Model = "model";
        /// <summary>Input name for the starting application percentage.</summary>
        public const string StartPercent = "start_percent";
        /// <summary>Input name for the application strength.</summary>
        public const string Strength = "strength";
    }

    /// <summary>Input names for the Detail Daemon options node.</summary>
    public static class DetailDaemonOptions
    {
        /// <summary>Input name for the detail bias.</summary>
        public const string Bias = "bias";
        /// <summary>Input name for the CFG scale override.</summary>
        public const string CFGScaleOverride = "cfg_scale_override";
        /// <summary>Input name for the detail amount.</summary>
        public const string DetailAmount = "detail_amount";
        /// <summary>Input name for the ending sigma percentage.</summary>
        public const string End = "end";
        /// <summary>Input name for the ending offset.</summary>
        public const string EndOffset = "end_offset";
        /// <summary>Input name for the adjustment exponent.</summary>
        public const string Exponent = "exponent";
        /// <summary>Input name for the fade amount.</summary>
        public const string Fade = "fade";
        /// <summary>Input name for the smoothing amount.</summary>
        public const string Smooth = "smooth";
        /// <summary>Input name for the starting sigma percentage.</summary>
        public const string Start = "start";
        /// <summary>Input name for the starting offset.</summary>
        public const string StartOffset = "start_offset";
    }

    /// <summary>Input names for the LoRA extraction node.</summary>
    public static class ExtractLora
    {
        /// <summary>Input name for the base model.</summary>
        public const string BaseModel = "base_model";
        /// <summary>Input name for extraction metadata.</summary>
        public const string Metadata = "metadata";
        /// <summary>Input name for the other model.</summary>
        public const string OtherModel = "other_model";
        /// <summary>Input name for the extraction rank.</summary>
        public const string Rank = "rank";
        /// <summary>Input name for the save filename.</summary>
        public const string SaveFilename = "save_filename";
        /// <summary>Input name for the raw save path.</summary>
        public const string SaveRawPath = "save_rawpath";
    }

    /// <summary>Input names for the model-only loader node.</summary>
    public static class JustLoadTheModelPlease
    {
        /// <summary>Input name for the CLIP model.</summary>
        public const string CLIP = "clip";
        /// <summary>Input name for the diffusion model.</summary>
        public const string Model = "model";
        /// <summary>Input name for the VAE model.</summary>
        public const string VAE = "vae";
    }

    /// <summary>Input names for Swarm's KSampler node.</summary>
    public static class KSampler
    {
        /// <summary>Input name controlling whether noise is added.</summary>
        public const string AddNoise = "add_noise";
        /// <summary>Input name for the classifier-free guidance scale.</summary>
        public const string CFG = "cfg";
        /// <summary>Input name for Detail Daemon options.</summary>
        public const string DetailDaemon = "detail_daemon";
        /// <summary>Input name for the ending sampling step.</summary>
        public const string EndAtStep = "end_at_step";
        /// <summary>Input name for the latent image.</summary>
        public const string LatentImage = "latent_image";
        /// <summary>Input name for the diffusion model.</summary>
        public const string Model = "model";
        /// <summary>Input name for the negative diffusion model.</summary>
        public const string ModelNegative = "model_negative";
        /// <summary>Input name for negative conditioning.</summary>
        public const string Negative = "negative";
        /// <summary>Input name for the noise seed.</summary>
        public const string NoiseSeed = "noise_seed";
        /// <summary>Input name for positive conditioning.</summary>
        public const string Positive = "positive";
        /// <summary>Input name for preview behavior.</summary>
        public const string Previews = "previews";
        /// <summary>Input name controlling leftover-noise return.</summary>
        public const string ReturnWithLeftoverNoise = "return_with_leftover_noise";
        /// <summary>Input name for the scheduler rho value.</summary>
        public const string Rho = "rho";
        /// <summary>Input name for the sampler.</summary>
        public const string SamplerName = "sampler_name";
        /// <summary>Input name for the scheduler.</summary>
        public const string Scheduler = "scheduler";
        /// <summary>Input name for the maximum sigma.</summary>
        public const string SigmaMax = "sigma_max";
        /// <summary>Input name for the minimum sigma.</summary>
        public const string SigmaMin = "sigma_min";
        /// <summary>Input name for the starting sampling step.</summary>
        public const string StartAtStep = "start_at_step";
        /// <summary>Input name for the sampling step count.</summary>
        public const string Steps = "steps";
        /// <summary>Input name controlling tiled sampling.</summary>
        public const string TileSample = "tile_sample";
        /// <summary>Input name for the sampling tile size.</summary>
        public const string TileSize = "tile_size";
        /// <summary>Input name for the variation seed.</summary>
        public const string VarSeed = "var_seed";
        /// <summary>Input name for the variation-seed strength.</summary>
        public const string VarSeedStrength = "var_seed_strength";
    }

    /// <summary>Input names for the masked latent blending node.</summary>
    public static class LatentBlendMasked
    {
        /// <summary>Input name for the blend factor.</summary>
        public const string BlendFactor = "blend_factor";
        /// <summary>Input name for the blend mask.</summary>
        public const string Mask = "mask";
        /// <summary>Input name for the first latent samples.</summary>
        public const string Samples0 = "samples0";
        /// <summary>Input name for the second latent samples.</summary>
        public const string Samples1 = "samples1";
    }

    /// <summary>Input names for the LTXV audio VAE loader node.</summary>
    public static class LTXVAudioVAELoader
    {
        /// <summary>Input name for the VAE model name.</summary>
        public const string VAEName = "vae_name";
    }

    /// <summary>Input names for the model tiling node.</summary>
    public static class ModelTiling
    {
        /// <summary>Input name for the diffusion model.</summary>
        public const string Model = "model";
        /// <summary>Input name for the tiled axis.</summary>
        public const string TileAxis = "tile_axis";
    }

    /// <summary>Input names for the offset empty latent image node.</summary>
    public static class OffsetEmptyLatentImage
    {
        /// <summary>Input name for the batch size.</summary>
        public const string BatchSize = "batch_size";
        /// <summary>Input name for the latent height.</summary>
        public const string Height = "height";
        /// <summary>Input name for the first latent offset.</summary>
        public const string OffA = "off_a";
        /// <summary>Input name for the second latent offset.</summary>
        public const string OffB = "off_b";
        /// <summary>Input name for the third latent offset.</summary>
        public const string OffC = "off_c";
        /// <summary>Input name for the fourth latent offset.</summary>
        public const string OffD = "off_d";
        /// <summary>Input name for the latent width.</summary>
        public const string Width = "width";
    }

    /// <summary>Input names for the reference-only conditioning node.</summary>
    public static class ReferenceOnly
    {
        /// <summary>Input name for the latent samples.</summary>
        public const string Latent = "latent";
        /// <summary>Input name for the diffusion model.</summary>
        public const string Model = "model";
        /// <summary>Input name for the reference latent samples.</summary>
        public const string Reference = "reference";
    }

    /// <summary>Input names for the tileable VAE node.</summary>
    public static class TileableVAE
    {
        /// <summary>Input name for the tiled axis.</summary>
        public const string TileAxis = "tile_axis";
        /// <summary>Input name for the VAE model.</summary>
        public const string VAE = "vae";
    }

    /// <summary>Input names for the latent unsampler node.</summary>
    public static class Unsampler
    {
        /// <summary>Input name for the latent image.</summary>
        public const string LatentImage = "latent_image";
        /// <summary>Input name for the diffusion model.</summary>
        public const string Model = "model";
        /// <summary>Input name for negative conditioning.</summary>
        public const string Negative = "negative";
        /// <summary>Input name for positive conditioning.</summary>
        public const string Positive = "positive";
        /// <summary>Input name for preview behavior.</summary>
        public const string Previews = "previews";
        /// <summary>Input name for the sampler.</summary>
        public const string SamplerName = "sampler_name";
        /// <summary>Input name for the scheduler.</summary>
        public const string Scheduler = "scheduler";
        /// <summary>Input name for the starting sampling step.</summary>
        public const string StartAtStep = "start_at_step";
        /// <summary>Input name for the sampling step count.</summary>
        public const string Steps = "steps";
    }

    #endregion

    #region Text, segmentation, and detection

    /// <summary>Input names for the attention coupling node.</summary>
    public static class AttentionCouple
    {
        /// <summary>Input name for the base conditioning.</summary>
        public const string BaseCondition = "base_cond";
        /// <summary>Input name for the base mask.</summary>
        public const string BaseMask = "base_mask";
        /// <summary>Input-name prefix for regional conditioning; caller appends a one-based index.</summary>
        public const string ConditionPrefix = "cond_";
        /// <summary>Input-name prefix for regional masks; caller appends a one-based index.</summary>
        public const string MaskPrefix = "mask_";
        /// <summary>Input name for the diffusion model.</summary>
        public const string Model = "model";
        /// <summary>Input name for the JSON region data.</summary>
        public const string RegionsJson = "regions_json";
    }

    /// <summary>Input names for the CLIPSeg node.</summary>
    public static class ClipSeg
    {
        /// <summary>Input name for the source images.</summary>
        public const string Images = "images";
        /// <summary>Input name for the text to match.</summary>
        public const string MatchText = "match_text";
        /// <summary>Input name for the segmentation threshold.</summary>
        public const string Threshold = "threshold";
    }

    /// <summary>Input names for the advanced CLIP text encoding node.</summary>
    public static class ClipTextEncodeAdvanced
    {
        /// <summary>Input name for the CLIP model.</summary>
        public const string CLIP = "clip";
        /// <summary>Input name for CLIP Vision output.</summary>
        public const string CLIPVisionOutput = "clip_vision_output";
        /// <summary>Input name for the guidance value.</summary>
        public const string Guidance = "guidance";
        /// <summary>Input name for the image height.</summary>
        public const string Height = "height";
        /// <summary>Input name for reference images.</summary>
        public const string Images = "images";
        /// <summary>Input name for the Llama prompt template.</summary>
        public const string LlamaTemplate = "llama_template";
        /// <summary>Input name for the prompt text.</summary>
        public const string Prompt = "prompt";
        /// <summary>Input name for the sampling step count.</summary>
        public const string Steps = "steps";
        /// <summary>Input name for the target height.</summary>
        public const string TargetHeight = "target_height";
        /// <summary>Input name for the target width.</summary>
        public const string TargetWidth = "target_width";
        /// <summary>Input name for the token normalization mode.</summary>
        public const string TokenNormalization = "token_normalization";
        /// <summary>Input name for the weight interpretation mode.</summary>
        public const string WeightInterpretation = "weight_interpretation";
        /// <summary>Input name for the image width.</summary>
        public const string Width = "width";
    }

    /// <summary>Input names for the SAM3 bounding-box JSON node.</summary>
    public static class Sam3BBoxFromJson
    {
        /// <summary>Input name for the bounding-box JSON.</summary>
        public const string BBoxJson = "bbox_json";
        /// <summary>Input name for the source image.</summary>
        public const string Image = "image";
    }

    /// <summary>Input names for the SAM3 mask post-processing node.</summary>
    public static class Sam3MaskPostProcess
    {
        /// <summary>Input name controlling hole filling.</summary>
        public const string FillHoles = "fill_holes";
        /// <summary>Input name for the hole-filling kernel size.</summary>
        public const string HoleKernelSize = "hole_kernel_size";
        /// <summary>Input name for the source mask.</summary>
        public const string Mask = "mask";
    }

    /// <summary>Input names for the SAM3 point JSON node.</summary>
    public static class Sam3PointsFromJson
    {
        /// <summary>Input name for the source image.</summary>
        public const string Image = "image";
        /// <summary>Input name controlling foreground-point interpretation.</summary>
        public const string IsForeground = "is_foreground";
        /// <summary>Input name for the point JSON.</summary>
        public const string PointsJson = "points_json";
    }

    /// <summary>Input names for the YOLO detection node.</summary>
    public static class YoloDetection
    {
        /// <summary>Input name for the class filter.</summary>
        public const string ClassFilter = "class_filter";
        /// <summary>Input name for the source image.</summary>
        public const string Image = "image";
        /// <summary>Input name for the detection index.</summary>
        public const string Index = "index";
        /// <summary>Input name for the model name.</summary>
        public const string ModelName = "model_name";
        /// <summary>Input name for the detection sort order.</summary>
        public const string SortOrder = "sort_order";
        /// <summary>Input name for the detection threshold.</summary>
        public const string Threshold = "threshold";
    }

    #endregion

    #region Image and mask processing

    /// <summary>Input names for the overlap-mask cleaning node.</summary>
    public static class CleanOverlapMasksExceptSelf
    {
        /// <summary>Input name for the merged mask.</summary>
        public const string MaskMerged = "mask_merged";
        /// <summary>Input name for the mask to preserve.</summary>
        public const string MaskSelf = "mask_self";
    }

    /// <summary>Input names for the mask exclusion node.</summary>
    public static class ExcludeFromMask
    {
        /// <summary>Input name for the mask to exclude.</summary>
        public const string ExcludeMask = "exclude_mask";
        /// <summary>Input name for the main mask.</summary>
        public const string MainMask = "main_mask";
    }

    /// <summary>Input names for the color-corrected masked image composite node.</summary>
    public static class ImageCompositeMaskedColorCorrecting
    {
        /// <summary>Input name for the color-correction method.</summary>
        public const string CorrectionMethod = "correction_method";
        /// <summary>Input name for the destination image.</summary>
        public const string Destination = "destination";
        /// <summary>Input name for the composite mask.</summary>
        public const string Mask = "mask";
        /// <summary>Input name for the source image.</summary>
        public const string Source = "source";
        /// <summary>Input name for the horizontal offset.</summary>
        public const string X = "x";
        /// <summary>Input name for the vertical offset.</summary>
        public const string Y = "y";
    }

    /// <summary>Input names for the image crop node.</summary>
    public static class ImageCrop
    {
        /// <summary>Input name for the crop height.</summary>
        public const string Height = "height";
        /// <summary>Input name for the source image.</summary>
        public const string Image = "image";
        /// <summary>Input name for the crop width.</summary>
        public const string Width = "width";
        /// <summary>Input name for the horizontal crop offset.</summary>
        public const string X = "x";
        /// <summary>Input name for the vertical crop offset.</summary>
        public const string Y = "y";
    }

    /// <summary>Input names for the image height node.</summary>
    public static class ImageHeight
    {
        /// <summary>Input name for the source image.</summary>
        public const string Image = "image";
    }

    /// <summary>Input names for the image noise node.</summary>
    public static class ImageNoise
    {
        /// <summary>Input name for the noise amount.</summary>
        public const string Amount = "amount";
        /// <summary>Input name for the source image.</summary>
        public const string Image = "image";
        /// <summary>Input name for the noise mask.</summary>
        public const string Mask = "mask";
        /// <summary>Input name for the noise seed.</summary>
        public const string Seed = "seed";
    }

    /// <summary>Input names for the megapixel image scaling node.</summary>
    public static class ImageScaleForMP
    {
        /// <summary>Input name controlling whether the image can shrink.</summary>
        public const string CanShrink = "can_shrink";
        /// <summary>Input name for the megapixel height ratio.</summary>
        public const string Height = "height";
        /// <summary>Input name for the source image.</summary>
        public const string Image = "image";
        /// <summary>Input name for the megapixel width ratio.</summary>
        public const string Width = "width";
    }

    /// <summary>Input names for the image width node.</summary>
    public static class ImageWidth
    {
        /// <summary>Input name for the source image.</summary>
        public const string Image = "image";
    }

    /// <summary>Input names for the mask blur node.</summary>
    public static class MaskBlur
    {
        /// <summary>Input name for the blur radius.</summary>
        public const string BlurRadius = "blur_radius";
        /// <summary>Input name for the source mask.</summary>
        public const string Mask = "mask";
        /// <summary>Input name for the blur sigma.</summary>
        public const string Sigma = "sigma";
    }

    /// <summary>Input names for the mask bounds node.</summary>
    public static class MaskBounds
    {
        /// <summary>Input name for the horizontal aspect ratio.</summary>
        public const string AspectX = "aspect_x";
        /// <summary>Input name for the vertical aspect ratio.</summary>
        public const string AspectY = "aspect_y";
        /// <summary>Input name for the bounds growth amount.</summary>
        public const string Grow = "grow";
        /// <summary>Input name for the source mask.</summary>
        public const string Mask = "mask";
    }

    /// <summary>Input names for the mask growth node.</summary>
    public static class MaskGrow
    {
        /// <summary>Input name for the growth amount.</summary>
        public const string Grow = "grow";
        /// <summary>Input name for the source mask.</summary>
        public const string Mask = "mask";
    }

    /// <summary>Input names for the mask threshold node.</summary>
    public static class MaskThreshold
    {
        /// <summary>Input name for the source mask.</summary>
        public const string Mask = "mask";
        /// <summary>Input name for the maximum threshold.</summary>
        public const string Max = "max";
        /// <summary>Input name for the minimum threshold.</summary>
        public const string Min = "min";
    }

    /// <summary>Input names for the overlap-fix mask merging node.</summary>
    public static class OverMergeMasksForOverlapFix
    {
        /// <summary>Input name for the first mask.</summary>
        public const string MaskA = "mask_a";
        /// <summary>Input name for the second mask.</summary>
        public const string MaskB = "mask_b";
    }

    /// <summary>Input names for the background removal node.</summary>
    public static class RemBg
    {
        /// <summary>Input name for the source images.</summary>
        public const string Images = "images";
    }

    /// <summary>Input names for the percentage-based square mask node.</summary>
    public static class SquareMaskFromPercent
    {
        /// <summary>Input name for the mask height percentage.</summary>
        public const string Height = "height";
        /// <summary>Input name for the mask strength.</summary>
        public const string Strength = "strength";
        /// <summary>Input name for the mask width percentage.</summary>
        public const string Width = "width";
        /// <summary>Input name for the horizontal mask position.</summary>
        public const string X = "x";
        /// <summary>Input name for the vertical mask position.</summary>
        public const string Y = "y";
    }

    #endregion

    #region Inputs and loading

    /// <summary>Input names for the embedding-list provider node.</summary>
    public static class EmbedLoaderListProvider
    {
        /// <summary>Input name for the embedding name.</summary>
        public const string EmbedName = "embed_name";
    }

    /// <summary>Input names for the Base64 audio loader node.</summary>
    public static class LoadAudioB64
    {
        /// <summary>Input name for Base64-encoded audio.</summary>
        public const string AudioBase64 = "audio_base64";
    }

    /// <summary>Input names for the Base64 image loader node.</summary>
    public static class LoadImageB64
    {
        /// <summary>Input name for a Base64-encoded image.</summary>
        public const string ImageBase64 = "image_base64";
    }

    /// <summary>Input names for the Base64 video loader node.</summary>
    public static class LoadVideoB64
    {
        /// <summary>Input name for Base64-encoded video.</summary>
        public const string VideoBase64 = "video_base64";
    }

    #endregion

    #region Saving and media utilities

    /// <summary>Input names for the frame-counting node.</summary>
    public static class CountFrames
    {
        /// <summary>Input name for the image sequence.</summary>
        public const string Image = "image";
    }

    /// <summary>Input names for the audio normalization node.</summary>
    public static class EnsureAudio
    {
        /// <summary>Input name for the source audio.</summary>
        public const string Audio = "audio";
        /// <summary>Input name for the target duration.</summary>
        public const string TargetDuration = "target_duration";
    }

    /// <summary>Input names for the integer addition node.</summary>
    public static class IntAdd
    {
        /// <summary>Input name for the first integer.</summary>
        public const string A = "a";
        /// <summary>Input name for the second integer.</summary>
        public const string B = "b";
    }

    /// <summary>Input names for the animation saving node.</summary>
    public static class SaveAnimationWS
    {
        /// <summary>Input name for the audio track.</summary>
        public const string Audio = "audio";
        /// <summary>Input name for the output format.</summary>
        public const string Format = "format";
        /// <summary>Input name for the frame rate.</summary>
        public const string FPS = "fps";
        /// <summary>Input name for the image sequence.</summary>
        public const string Images = "images";
        /// <summary>Input name controlling lossless output.</summary>
        public const string Lossless = "lossless";
        /// <summary>Input name for the encoding method.</summary>
        public const string Method = "method";
        /// <summary>Input name for the output quality.</summary>
        public const string Quality = "quality";
    }

    /// <summary>Input names for the image saving node.</summary>
    public static class SaveImageWS
    {
        /// <summary>Input name for the output bit depth.</summary>
        public const string BitDepth = "bit_depth";
        /// <summary>Input name for the images to save.</summary>
        public const string Images = "images";
    }

    /// <summary>Input names for the frame trimming node.</summary>
    public static class TrimFrames
    {
        /// <summary>Input name for the image sequence.</summary>
        public const string Image = "image";
        /// <summary>Input name for the number of ending frames to trim.</summary>
        public const string TrimEnd = "trim_end";
        /// <summary>Input name for the number of starting frames to trim.</summary>
        public const string TrimStart = "trim_start";
    }

    /// <summary>Input names for the video boomerang node.</summary>
    public static class VideoBoomerang
    {
        /// <summary>Input name for the image sequence.</summary>
        public const string Images = "images";
    }

    /// <summary>Input names for the video frame-rate resampling node.</summary>
    public static class VideoResampleFPS
    {
        /// <summary>Input name for the source frame rate.</summary>
        public const string FPSIn = "fps_in";
        /// <summary>Input name for the target frame rate.</summary>
        public const string FPSOut = "fps_out";
        /// <summary>Input name for the image sequence.</summary>
        public const string Images = "images";
        /// <summary>Input name for the resampling method.</summary>
        public const string Method = "method";
    }

    #endregion
}
