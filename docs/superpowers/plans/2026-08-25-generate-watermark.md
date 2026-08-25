# Generate Watermark Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add an optional Advanced Generate-tab watermark group that composites built-in or custom watermarks onto final still images and final video frames.

**Architecture:** A focused `SwarmWatermark` Comfy node performs device-local tensor compositing and owns two independently created speaker presets. Core typed parameters expose every control, backend capability discovery gates them, and a workflow helper is called explicitly at the three terminal save paths so previews and intermediate outputs remain untouched.

**Tech Stack:** C# 12/.NET 8, Newtonsoft JSON workflow generation, Python 3, PyTorch, Pillow, ComfyUI node APIs, ImageMagick for independently authored PNG assets.

**Repository constraint:** `AGENTS.md` forbids agents from running builds or any form of automated test. The test-driven steps normally required by the planning skill are therefore replaced with static inspection, syntax-oriented linting where available, and a maintainer-run manual verification matrix.

---

## File Map

- Create `src/BuiltinExtensions/ComfyUIBackend/ExtraNodes/SwarmComfyCommon/SwarmWatermark.py`: validate, resolve, resize, place, and alpha-composite watermark tensors.
- Create `src/BuiltinExtensions/ComfyUIBackend/ExtraNodes/SwarmComfyCommon/assets/WatermarkSpeakerWhite.png`: transparent white speaker preset.
- Create `src/BuiltinExtensions/ComfyUIBackend/ExtraNodes/SwarmComfyCommon/assets/WatermarkSpeakerBlack.png`: transparent black speaker preset.
- Modify `src/BuiltinExtensions/ComfyUIBackend/ExtraNodes/SwarmComfyCommon/__init__.py`: register `SwarmWatermark` with the common managed nodes.
- Modify `src/BuiltinExtensions/ComfyUIBackend/ComfyNodeNames.cs`: define the stable node class name.
- Modify `src/BuiltinExtensions/ComfyUIBackend/ComfyNodeInputNames.cs`: define the node's input-name contract.
- Modify `src/BuiltinExtensions/ComfyUIBackend/ComfyCapabilityCatalog.cs`: map the managed node to `swarm_watermark`.
- Modify `src/Text2Image/T2IParamTypes.cs`: register the Advanced Watermark group and its seven typed parameters.
- Modify `src/BuiltinExtensions/ComfyUIBackend/WorkflowGenerator.cs`: add one reusable final-watermark graph helper.
- Modify `src/BuiltinExtensions/ComfyUIBackend/WorkflowGeneratorSteps.cs`: invoke the helper only before terminal still/direct-video, image-to-video, and extended-video saves.

### Task 1: Add the managed Comfy watermark node and presets

**Files:**
- Create: `src/BuiltinExtensions/ComfyUIBackend/ExtraNodes/SwarmComfyCommon/SwarmWatermark.py`
- Create: `src/BuiltinExtensions/ComfyUIBackend/ExtraNodes/SwarmComfyCommon/assets/WatermarkSpeakerWhite.png`
- Create: `src/BuiltinExtensions/ComfyUIBackend/ExtraNodes/SwarmComfyCommon/assets/WatermarkSpeakerBlack.png`
- Modify: `src/BuiltinExtensions/ComfyUIBackend/ExtraNodes/SwarmComfyCommon/__init__.py`

- [ ] **Step 1: Create independently authored transparent speaker presets**

Run these exact commands from the repository root. They draw a geometric speaker and two sound-wave strokes without using third-party artwork:

```bash
mkdir -p src/BuiltinExtensions/ComfyUIBackend/ExtraNodes/SwarmComfyCommon/assets
magick -size 256x256 xc:none -fill white -draw "polygon 28,96 80,96 144,44 144,212 80,160 28,160" -fill none -stroke white -strokewidth 16 -draw "path 'M 164,88 C 190,106 190,150 164,168' path 'M 184,60 C 230,94 230,162 184,196'" src/BuiltinExtensions/ComfyUIBackend/ExtraNodes/SwarmComfyCommon/assets/WatermarkSpeakerWhite.png
magick -size 256x256 xc:none -fill black -draw "polygon 28,96 80,96 144,44 144,212 80,160 28,160" -fill none -stroke black -strokewidth 16 -draw "path 'M 164,88 C 190,106 190,150 164,168' path 'M 184,60 C 230,94 230,162 184,196'" src/BuiltinExtensions/ComfyUIBackend/ExtraNodes/SwarmComfyCommon/assets/WatermarkSpeakerBlack.png
```

Expected: two 256x256 RGBA PNGs with transparent backgrounds.

- [ ] **Step 2: Add the complete managed node implementation**

Create `SwarmWatermark.py` with this implementation:

```python
from pathlib import Path

import numpy as np
import torch
import torch.nn.functional as F
from PIL import Image


ALIGNMENTS = [
    "bottom-right",
    "center",
    "top-center",
    "top-left",
    "top-right",
    "center-right",
    "center-left",
    "bottom-center",
    "bottom-left",
]

PRESETS = {
    "speaker-white": "WatermarkSpeakerWhite.png",
    "speaker-black": "WatermarkSpeakerBlack.png",
}


class SwarmWatermark:
    @classmethod
    def INPUT_TYPES(cls):
        return {
            "required": {
                "image": ("IMAGE",),
                "watermark_preset": (list(PRESETS.keys()),),
                "alignment": (ALIGNMENTS,),
                "offset_percentage": (
                    "FLOAT",
                    {"default": 2.0, "min": 0.0, "max": 100.0, "step": 0.1},
                ),
                "resize_percentage": (
                    "FLOAT",
                    {"default": 20.0, "min": 0.1, "max": 200.0, "step": 0.1},
                ),
                "opacity": (
                    "FLOAT",
                    {"default": 60.0, "min": 0.0, "max": 100.0, "step": 1.0},
                ),
            },
            "optional": {
                "watermark": ("IMAGE",),
                "watermark_mask": ("MASK",),
            },
        }

    RETURN_TYPES = ("IMAGE",)
    RETURN_NAMES = ("image",)
    FUNCTION = "apply"
    CATEGORY = "SwarmUI/images"
    DESCRIPTION = "Applies a preset or custom watermark to every image or video frame in a batch."

    def apply(
        self,
        image,
        watermark_preset,
        alignment,
        offset_percentage,
        resize_percentage,
        opacity,
        watermark=None,
        watermark_mask=None,
    ):
        self._validate_image(image, "image")
        frame_count, frame_height, frame_width, frame_channels = image.shape
        if frame_channels < 3:
            raise ValueError("image must contain at least three color channels")

        opacity_value = self._percentage(opacity, 0.0, 100.0) / 100.0
        if opacity_value == 0.0:
            return (image.clone(),)

        watermark_rgb, alpha = self._resolve_watermark(
            image,
            watermark_preset,
            watermark,
            watermark_mask,
        )
        self._validate_batches(watermark_rgb, alpha, frame_count)

        resize_value = self._percentage(resize_percentage, 0.1, 200.0)
        target_width = max(1, int(round(frame_width * resize_value / 100.0)))
        target_height = max(
            1,
            int(round(watermark_rgb.shape[1] * target_width / watermark_rgb.shape[2])),
        )
        watermark_rgb = self._resize(watermark_rgb, target_height, target_width)
        alpha = self._resize(alpha, target_height, target_width).clamp(0.0, 1.0)
        alpha = alpha * opacity_value

        offset_value = self._percentage(offset_percentage, 0.0, 100.0)
        offset_x = int(round(frame_width * offset_value / 100.0))
        offset_y = int(round(frame_height * offset_value / 100.0))
        x, y = self._placement(
            alignment,
            frame_width,
            frame_height,
            target_width,
            target_height,
            offset_x,
            offset_y,
        )

        output = image.clone()
        for frame_index in range(frame_count):
            watermark_index = 0 if watermark_rgb.shape[0] == 1 else frame_index
            alpha_index = 0 if alpha.shape[0] == 1 else frame_index
            self._composite(
                output,
                frame_index,
                watermark_rgb[watermark_index],
                alpha[alpha_index],
                x,
                y,
            )
        return (output.clamp(0.0, 1.0),)

    @staticmethod
    def _validate_image(value, name):
        if not isinstance(value, torch.Tensor):
            raise TypeError(f"{name} must be a torch.Tensor")
        if value.ndim != 4:
            raise ValueError(f"{name} must have shape [B,H,W,C]")
        if min(value.shape) <= 0:
            raise ValueError(f"{name} must not contain empty dimensions")

    def _resolve_watermark(self, image, preset_name, watermark, watermark_mask):
        if watermark is None:
            return self._load_preset(preset_name, image)

        self._validate_image(watermark, "watermark")
        if watermark.shape[-1] < 3:
            raise ValueError("watermark must contain at least three color channels")
        rgb = watermark[..., :3].to(device=image.device, dtype=image.dtype)
        if watermark_mask is not None:
            alpha = self._normalize_mask(watermark_mask, image)
        elif watermark.shape[-1] >= 4:
            alpha = watermark[..., 3:4].to(device=image.device, dtype=image.dtype)
        else:
            alpha = torch.ones(
                watermark.shape[0],
                watermark.shape[1],
                watermark.shape[2],
                1,
                device=image.device,
                dtype=image.dtype,
            )
        return rgb, alpha

    @staticmethod
    def _load_preset(preset_name, image):
        if preset_name not in PRESETS:
            raise ValueError(f"unknown watermark preset: {preset_name}")
        asset_path = Path(__file__).with_name("assets") / PRESETS[preset_name]
        if not asset_path.exists():
            raise FileNotFoundError(f"watermark preset asset is missing: {asset_path}")
        with Image.open(asset_path) as preset_image:
            rgba = np.asarray(preset_image.convert("RGBA"), dtype=np.float32) / 255.0
        tensor = torch.from_numpy(rgba).unsqueeze(0).to(
            device=image.device,
            dtype=image.dtype,
        )
        return tensor[..., :3], tensor[..., 3:4]

    @staticmethod
    def _normalize_mask(mask, image):
        if not isinstance(mask, torch.Tensor):
            raise TypeError("watermark_mask must be a torch.Tensor")
        mask = mask.to(device=image.device, dtype=image.dtype)
        if mask.ndim == 2:
            mask = mask.unsqueeze(0).unsqueeze(-1)
        elif mask.ndim == 3:
            mask = mask.unsqueeze(-1)
        elif mask.ndim == 4 and mask.shape[-1] != 1:
            if mask.shape[1] == 1:
                mask = mask.movedim(1, -1)
            else:
                mask = mask[..., :1]
        elif mask.ndim != 4:
            raise ValueError("watermark_mask must have shape [H,W], [B,H,W], or [B,H,W,C]")
        return mask.clamp(0.0, 1.0)

    @staticmethod
    def _validate_batches(watermark, alpha, frame_count):
        if watermark.shape[0] not in (1, frame_count):
            raise ValueError("watermark batch must contain one image or match the frame count")
        if alpha.shape[0] not in (1, watermark.shape[0], frame_count):
            raise ValueError("watermark mask batch must contain one mask or match the watermark/frame count")

    @staticmethod
    def _percentage(value, minimum, maximum):
        return max(minimum, min(maximum, float(value)))

    @staticmethod
    def _resize(tensor, height, width):
        channel_first = tensor.movedim(-1, 1)
        resized = F.interpolate(
            channel_first,
            size=(height, width),
            mode="bilinear",
            align_corners=False,
        )
        return resized.movedim(1, -1)

    @staticmethod
    def _placement(alignment, frame_width, frame_height, width, height, offset_x, offset_y):
        center_x = (frame_width - width) // 2
        center_y = (frame_height - height) // 2
        placements = {
            "center": (center_x, center_y),
            "top-center": (center_x, offset_y),
            "top-left": (offset_x, offset_y),
            "top-right": (frame_width - width - offset_x, offset_y),
            "center-right": (frame_width - width - offset_x, center_y),
            "center-left": (offset_x, center_y),
            "bottom-center": (center_x, frame_height - height - offset_y),
            "bottom-right": (frame_width - width - offset_x, frame_height - height - offset_y),
            "bottom-left": (offset_x, frame_height - height - offset_y),
        }
        if alignment not in placements:
            raise ValueError(f"unknown watermark alignment: {alignment}")
        return placements[alignment]

    @staticmethod
    def _composite(output, frame_index, watermark, alpha, x, y):
        frame_height, frame_width = output.shape[1], output.shape[2]
        watermark_height, watermark_width = watermark.shape[0], watermark.shape[1]
        frame_x0 = max(0, x)
        frame_y0 = max(0, y)
        frame_x1 = min(frame_width, x + watermark_width)
        frame_y1 = min(frame_height, y + watermark_height)
        if frame_x0 >= frame_x1 or frame_y0 >= frame_y1:
            return

        watermark_x0 = frame_x0 - x
        watermark_y0 = frame_y0 - y
        watermark_x1 = watermark_x0 + frame_x1 - frame_x0
        watermark_y1 = watermark_y0 + frame_y1 - frame_y0
        source = watermark[watermark_y0:watermark_y1, watermark_x0:watermark_x1, :]
        source_alpha = alpha[watermark_y0:watermark_y1, watermark_x0:watermark_x1, :]
        target = output[frame_index, frame_y0:frame_y1, frame_x0:frame_x1, :3]
        output[frame_index, frame_y0:frame_y1, frame_x0:frame_x1, :3] = (
            source * source_alpha + target * (1.0 - source_alpha)
        )


NODE_CLASS_MAPPINGS = {
    "SwarmWatermark": SwarmWatermark,
}
```

- [ ] **Step 3: Register the module with common Swarm nodes**

In `SwarmComfyCommon/__init__.py`, add `SwarmWatermark` to the import line and union its mapping into `NODE_CLASS_MAPPINGS`:

```python
from . import SwarmAnimaLLLite, SwarmAnimaQwen35, SwarmAttentionCouple, SwarmBlending, SwarmImages, SwarmInternalUtil, SwarmKSampler, SwarmLoadImageB64, SwarmLoraLoader, SwarmMasks, SwarmSaveImageWS, SwarmTiling, SwarmExtractLora, SwarmUnsampler, SwarmLatents, SwarmInputNodes, SwarmTextHandling, SwarmReference, SwarmMath, SwarmSam2, SwarmSam3, SwarmAudio, SwarmVideo, SwarmModels, SwarmWatermark
```

Add this union next to the other common mappings:

```python
    | SwarmWatermark.NODE_CLASS_MAPPINGS
```

- [ ] **Step 4: Perform permitted static checks**

Run:

```bash
git diff --check -- src/BuiltinExtensions/ComfyUIBackend/ExtraNodes/SwarmComfyCommon
rg -n "SwarmWatermark|WatermarkSpeaker" src/BuiltinExtensions/ComfyUIBackend/ExtraNodes/SwarmComfyCommon
identify src/BuiltinExtensions/ComfyUIBackend/ExtraNodes/SwarmComfyCommon/assets/WatermarkSpeakerWhite.png src/BuiltinExtensions/ComfyUIBackend/ExtraNodes/SwarmComfyCommon/assets/WatermarkSpeakerBlack.png
```

Expected: no whitespace errors; node registration appears in the new module and `__init__.py`; both images report 256x256 PNG with alpha.

- [ ] **Step 5: Commit the managed node**

```bash
git add src/BuiltinExtensions/ComfyUIBackend/ExtraNodes/SwarmComfyCommon/SwarmWatermark.py src/BuiltinExtensions/ComfyUIBackend/ExtraNodes/SwarmComfyCommon/assets/WatermarkSpeakerWhite.png src/BuiltinExtensions/ComfyUIBackend/ExtraNodes/SwarmComfyCommon/assets/WatermarkSpeakerBlack.png src/BuiltinExtensions/ComfyUIBackend/ExtraNodes/SwarmComfyCommon/__init__.py
git commit -m "Add managed watermark Comfy node"
```

### Task 2: Add node contracts, capability discovery, and Generate parameters

**Files:**
- Modify: `src/BuiltinExtensions/ComfyUIBackend/ComfyNodeNames.cs`
- Modify: `src/BuiltinExtensions/ComfyUIBackend/ComfyNodeInputNames.cs`
- Modify: `src/BuiltinExtensions/ComfyUIBackend/ComfyCapabilityCatalog.cs`
- Modify: `src/Text2Image/T2IParamTypes.cs`

- [ ] **Step 1: Add the stable node-name constant**

In the image-processing region of `ComfyNodeNames.cs`, add:

```csharp
    /// <summary>Comfy class name for final image/video-frame watermark compositing.</summary>
    public const string Watermark = "SwarmWatermark";
```

- [ ] **Step 2: Add the complete input-name contract**

In the image-processing region of `ComfyNodeInputNames.cs`, add:

```csharp
    /// <summary>Input names for the final-output watermark node.</summary>
    public static class Watermark
    {
        /// <summary>Input name for watermark alignment.</summary>
        public const string Alignment = "alignment";
        /// <summary>Input name for the destination image or frame batch.</summary>
        public const string Image = "image";
        /// <summary>Input name for the edge-offset percentage.</summary>
        public const string OffsetPercentage = "offset_percentage";
        /// <summary>Input name for global watermark opacity.</summary>
        public const string Opacity = "opacity";
        /// <summary>Input name for watermark-width percentage.</summary>
        public const string ResizePercentage = "resize_percentage";
        /// <summary>Input name for an optional custom watermark image.</summary>
        public const string WatermarkImage = "watermark";
        /// <summary>Input name for an optional custom watermark opacity mask.</summary>
        public const string WatermarkMask = "watermark_mask";
        /// <summary>Input name for the built-in watermark preset.</summary>
        public const string WatermarkPreset = "watermark_preset";
    }
```

- [ ] **Step 3: Add capability discovery**

In `ComfyCapabilityCatalog.cs`, add the named feature constant:

```csharp
    /// <summary>Feature ID requiring the Swarm final-output watermark node.</summary>
    public const string WatermarkFeature = "swarm_watermark";
```

Add the node mapping inside `CreateNodeToFeatureMap()`:

```csharp
            [ComfyNodeNames.Watermark] = WatermarkFeature,
```

- [ ] **Step 4: Declare the typed parameters and group**

Extend the existing declarations in `T2IParamTypes.cs` exactly as follows:

```csharp
    public static T2IRegisteredParam<string> Prompt, NegativePrompt, AspectRatio, BackendType, RefinerMethod, FreeUApplyTo, FreeUVersion, PersonalNote, VideoFormat, VideoResolution, UnsamplerPrompt, ImageFormat, MaskBehavior, ColorCorrectionBehavior, RawResolution, SeamlessTileable, SD3TextEncs, BitDepth, Webhooks, Text2VideoFormat, WildcardSeedBehavior, PromptTokenNormalization, PromptWeightInterpretation, SegmentSortOrder, SegmentTargetResolution, SegmentApplyAfter, TorchCompile, VideoExtendFormat, ExactBackendID, OverridePredictionType, OverrideOutpathFormat, HistorySaveFolder, Text2AudioTimeSignature, Text2AudioLanguage, Text2AudioKeyScale, Text2AudioStyle, WatermarkPreset, WatermarkAlignment;
```

Append the watermark numeric fields to the existing `double` declaration:

```csharp
        FreeUBlock1, FreeUBlock2, FreeUSkip1, FreeUSkip2, GlobalRegionFactor, EndStepsEarly, SamplerSigmaMin, SamplerSigmaMax, SamplerRho, VideoAugmentationLevel, VideoCFG, VideoMinCFG, Video2VideoCreativity, VideoSwapPercent, VideoExtendSwapPercent, IP2PCFG2, RegionalObjectCleanupFactor, SigmaShift, SegmentThresholdMax, SegmentCFGScale, FluxGuidanceScale, Text2AudioDuration, AudioSilentPrefixDuration, AudioSilentSuffixDuration, ConditioningMultiplier, NegativeConditioningMultiplier, WatermarkOffsetPercentage, WatermarkResizePercentage, WatermarkOpacity;
```

Extend the image declaration and group declaration:

```csharp
    public static T2IRegisteredParam<Image> InitImage, MaskImage, VideoEndImage, WatermarkImage, WatermarkMask;
```

```csharp
        GroupAdvancedModelAddons, GroupSwarmInternal, GroupFreeU, GroupRegionalPrompting, GroupSegmentRefining, GroupSegmentOverrides, GroupAdvancedSampling, GroupAlternateGuidance, GroupVideo, GroupText2Video, GroupAdvancedVideo, GroupAdvancedVideoObscure, GroupVideoExtend, GroupText2Audio, GroupWatermark,
        GroupStarred, GroupUser1, GroupUser2, GroupUser3;
```

- [ ] **Step 5: Register the Advanced Watermark group and all controls**

Immediately before `GroupOtherFixes` initialization, add:

```csharp
        // ================================================ Watermark ================================================
        GroupWatermark = new("Watermark", Open: false, OrderPriority: 55, IsAdvanced: true, Toggles: true,
            Description: "Optionally apply a preset or custom watermark to final still images and video frames.");
        WatermarkPreset = Register<string>(new("Watermark Preset", "Built-in watermark used when no custom Watermark Image is supplied.",
            "speaker-white", GetValues: _ => ["speaker-white", "speaker-black"], Group: GroupWatermark, FeatureFlag: "swarm_watermark", OrderPriority: 1, DoNotPreview: true
            ));
        WatermarkImage = Register<Image>(new("Watermark Image", "Optional custom watermark image. This overrides Watermark Preset and automatically uses embedded PNG transparency.",
            null, Group: GroupWatermark, FeatureFlag: "swarm_watermark", OrderPriority: 2, DoNotPreview: true, ChangeWeight: 1
            ));
        WatermarkMask = Register<Image>(new("Watermark Mask", "Optional opacity mask for a custom watermark. White is opaque and black is transparent. This overrides embedded PNG transparency.",
            null, Group: GroupWatermark, FeatureFlag: "swarm_watermark", OrderPriority: 3, DoNotPreview: true, ChangeWeight: 1, DependNonDefault: WatermarkImage.Type.ID
            ));
        WatermarkAlignment = Register<string>(new("Watermark Alignment", "Where to place the watermark on the final output.",
            "bottom-right", GetValues: _ => ["bottom-right", "center", "top-center", "top-left", "top-right", "center-right", "center-left", "bottom-center", "bottom-left"], Group: GroupWatermark, FeatureFlag: "swarm_watermark", OrderPriority: 4, DoNotPreview: true
            ));
        WatermarkOffsetPercentage = Register<double>(new("Watermark Offset Percentage", "Margin from the selected edges as a percentage of final width and height.",
            "2", Min: 0, Max: 100, Step: 0.1, ViewType: ParamViewType.SLIDER, Group: GroupWatermark, FeatureFlag: "swarm_watermark", OrderPriority: 5, DoNotPreview: true
            ));
        WatermarkResizePercentage = Register<double>(new("Watermark Resize Percentage", "Watermark width as a percentage of final output width. Aspect ratio is preserved.",
            "20", Min: 0.1, Max: 200, Step: 0.1, ViewType: ParamViewType.SLIDER, Group: GroupWatermark, FeatureFlag: "swarm_watermark", OrderPriority: 6, DoNotPreview: true
            ));
        WatermarkOpacity = Register<double>(new("Watermark Opacity", "Global watermark opacity percentage.",
            "60", Min: 0, Max: 100, Step: 1, ViewType: ParamViewType.SLIDER, Group: GroupWatermark, FeatureFlag: "swarm_watermark", OrderPriority: 7, DoNotPreview: true
            ));
```

Because the group has `Toggles: true`, its parameters are omitted when the group is disabled. `TryGet(WatermarkPreset, ...)` is therefore the workflow-level enable check; do not add a redundant Boolean parameter.

- [ ] **Step 6: Perform permitted static checks**

Run:

```bash
git diff --check -- src/BuiltinExtensions/ComfyUIBackend/ComfyNodeNames.cs src/BuiltinExtensions/ComfyUIBackend/ComfyNodeInputNames.cs src/BuiltinExtensions/ComfyUIBackend/ComfyCapabilityCatalog.cs src/Text2Image/T2IParamTypes.cs
rg -n "WatermarkFeature|SwarmWatermark|WatermarkPreset|GroupWatermark" src/BuiltinExtensions/ComfyUIBackend src/Text2Image/T2IParamTypes.cs
```

Expected: no whitespace errors; the Python class name, C# node constant, capability map, feature flags, and parameter declarations all use the exact same identifiers.

- [ ] **Step 7: Commit contracts and parameters**

```bash
git add src/BuiltinExtensions/ComfyUIBackend/ComfyNodeNames.cs src/BuiltinExtensions/ComfyUIBackend/ComfyNodeInputNames.cs src/BuiltinExtensions/ComfyUIBackend/ComfyCapabilityCatalog.cs src/Text2Image/T2IParamTypes.cs
git commit -m "Expose advanced watermark parameters"
```

### Task 3: Insert watermarking only at terminal output paths

**Files:**
- Modify: `src/BuiltinExtensions/ComfyUIBackend/WorkflowGenerator.cs`
- Modify: `src/BuiltinExtensions/ComfyUIBackend/WorkflowGeneratorSteps.cs`

- [ ] **Step 1: Add a focused workflow helper**

Add this public method near the existing image load/save helpers in `WorkflowGenerator.cs`:

```csharp
    /// <summary>Applies the configured final-output watermark, or returns the media unchanged when watermarking is disabled.</summary>
    /// <param name="media">Final media to watermark.</param>
    /// <param name="vae">VAE used when the final media still requires decoding.</param>
    /// <returns>Final raw media with its path replaced by the watermark node output.</returns>
    public WGNodeData ApplyFinalWatermark(WGNodeData media, WGNodeData vae)
    {
        if (!UserInput.TryGet(T2IParamTypes.WatermarkPreset, out string preset))
        {
            return media;
        }
        if (!Features.Contains(ComfyCapabilityCatalog.WatermarkFeature))
        {
            throw new SwarmUserErrorException("The selected backend does not support final-output watermarking.");
        }
        if (media.DataType == WGNodeData.DT_AUDIO || media.DataType == WGNodeData.DT_LATENT_AUDIO)
        {
            throw new SwarmUserErrorException("Watermarking requires an image or video output, but this generation produced audio only.");
        }

        WGNodeData rawMedia = media.AsRawImage(vae);
        JObject inputs = new()
        {
            [ComfyNodeInputNames.Watermark.Image] = rawMedia.Path,
            [ComfyNodeInputNames.Watermark.WatermarkPreset] = preset,
            [ComfyNodeInputNames.Watermark.Alignment] = UserInput.Get(T2IParamTypes.WatermarkAlignment, "bottom-right"),
            [ComfyNodeInputNames.Watermark.OffsetPercentage] = UserInput.Get(T2IParamTypes.WatermarkOffsetPercentage, 2.0),
            [ComfyNodeInputNames.Watermark.ResizePercentage] = UserInput.Get(T2IParamTypes.WatermarkResizePercentage, 20.0),
            [ComfyNodeInputNames.Watermark.Opacity] = UserInput.Get(T2IParamTypes.WatermarkOpacity, 60.0)
        };

        if (UserInput.TryGet(T2IParamTypes.WatermarkImage, out Image customImage))
        {
            WGNodeData customLoader = LoadImage(customImage, "${watermarkimage}", false);
            inputs[ComfyNodeInputNames.Watermark.WatermarkImage] = customLoader.Path;
            JArray alphaMask;
            if (UserInput.TryGet(T2IParamTypes.WatermarkMask, out Image customMask))
            {
                WGNodeData maskLoader = LoadImage(customMask, "${watermarkmask}", false);
                string imageToMask = CreateNode("ImageToMask", new JObject()
                {
                    ["image"] = maskLoader.Path,
                    ["channel"] = "red"
                });
                alphaMask = NodePath(imageToMask, 0);
            }
            else
            {
                string invertMask = CreateNode("InvertMask", new JObject()
                {
                    ["mask"] = NodePath($"{customLoader.Path[0]}", 1)
                });
                alphaMask = NodePath(invertMask, 0);
            }
            inputs[ComfyNodeInputNames.Watermark.WatermarkMask] = alphaMask;
        }

        string watermarkNode = CreateNode(ComfyNodeNames.Watermark, inputs);
        return rawMedia.WithPath([watermarkNode, 0]);
    }
```

This deliberately uses `WithPath` without changing `DataType`, so a `DT_VIDEO` frame sequence remains video for `SaveOutput`. The copied `WGNodeData` also retains attached audio, FPS, dimensions, frame count, and compatibility metadata.

- [ ] **Step 2: Apply it to final still and direct text-to-video output**

In the `SaveImage` step of `WorkflowGeneratorSteps.cs`, replace the terminal `nodeId` block with:

```csharp
                if (nodeId is not null)
                {
                    if (!willHaveFollowupVideo)
                    {
                        g.CurrentMedia = g.ApplyFinalWatermark(g.CurrentMedia, g.CurrentVae);
                    }
                    g.CurrentMedia.SaveOutput(g.CurrentVae, g.CurrentAudioVae, nodeId);
                }
```

The `willHaveFollowupVideo` guard prevents the base still or pre-extension frames from being watermarked before later video stages.

- [ ] **Step 3: Apply it to terminal image-to-video output**

Immediately before the image-to-video step's final `SaveOutput`, add the `!hasExtend` guard:

```csharp
                if (!hasExtend)
                {
                    g.CurrentMedia = g.ApplyFinalWatermark(g.CurrentMedia, genInfo.Vae);
                }
                g.CurrentMedia.SaveOutput(genInfo.Vae, g.CurrentAudioVae, nodeId);
```

When extension blocks exist, this save is intermediate and remains unwatermarked.

- [ ] **Step 4: Apply it to terminal extended-video output**

At the end of the Extend Video step, immediately before the fixed final `SaveOutput(..., "9")`, add:

```csharp
                g.CurrentMedia = g.ApplyFinalWatermark(g.CurrentMedia, extendVae ?? g.CurrentVae);
                g.CurrentMedia.SaveOutput(g.CurrentVae, g.CurrentAudioVae, "9");
```

Keep every intermediate `SaveOutput` inside the extension loop unchanged.

- [ ] **Step 5: Trace every save branch statically**

Run:

```bash
rg -n "ApplyFinalWatermark|SaveOutput" src/BuiltinExtensions/ComfyUIBackend/WorkflowGenerator.cs src/BuiltinExtensions/ComfyUIBackend/WorkflowGeneratorSteps.cs
git diff --check -- src/BuiltinExtensions/ComfyUIBackend/WorkflowGenerator.cs src/BuiltinExtensions/ComfyUIBackend/WorkflowGeneratorSteps.cs
```

Inspect the surrounding code and confirm:

- one call in the step-10 terminal still/direct-video branch;
- one call in the step-11 non-extended image-to-video branch;
- one call in the step-12 extended-video terminal branch;
- no calls before sampler/refiner/model input;
- no calls before an `OutputIntermediateImages` save;
- `WithPath` retains media bookkeeping;
- embedded loader alpha is inverted once because Comfy loaders expose transparency as a white transparent-region mask;
- an explicit user mask uses white as opacity without inversion.

- [ ] **Step 6: Commit workflow integration**

```bash
git add src/BuiltinExtensions/ComfyUIBackend/WorkflowGenerator.cs src/BuiltinExtensions/ComfyUIBackend/WorkflowGeneratorSteps.cs
git commit -m "Apply watermarks to final generated media"
```

### Task 4: Final static review and maintainer handoff

**Files:**
- Review: all files from Tasks 1-3
- Reference: `docs/superpowers/specs/2026-08-25-generate-watermark-design.md`

- [ ] **Step 1: Check the implementation against every spec requirement**

Run:

```bash
git diff --check HEAD~3..HEAD
git diff --stat HEAD~3..HEAD
rg -n "speaker-white|speaker-black|bottom-right|top-center|center-right|bottom-center|swarm_watermark|DoNotPreview" src/BuiltinExtensions/ComfyUIBackend src/Text2Image/T2IParamTypes.cs
```

Expected: no whitespace errors; only the planned files changed; both presets, all nine alignments, capability gating, Advanced group parameters, and preview exclusion are present.

- [ ] **Step 2: Inspect repository status without touching unrelated work**

Run:

```bash
git status --short
git log -4 --oneline
```

Expected: the three feature commits are present after the existing design/plan commits. Any pre-existing user changes remain uncommitted and are not included in feature commits.

- [ ] **Step 3: Give the maintainer the manual verification matrix**

Do not run SwarmUI, ComfyUI, a build, or automated tests. Ask the maintainer to restart the managed Comfy backend and verify:

1. The Watermark group is hidden when Advanced is closed, visible when Advanced is open, collapsed by default, and disabled by default.
2. A disabled group produces a workflow with no `SwarmWatermark` node.
3. White and black speaker presets work on a still image.
4. A custom opaque PNG overrides the selected preset.
5. A custom transparent PNG uses its embedded alpha.
6. A separate white/black mask overrides embedded alpha with white visible and black transparent.
7. All nine alignments, offset, width percentage, and opacity controls work.
8. Zero opacity is visually unchanged; oversized placement clips safely.
9. Multi-image batches watermark every image.
10. Direct text-to-video, image-to-video, interpolated video, and extended video watermark every final frame exactly once.
11. Video audio and FPS remain unchanged.
12. `Output Intermediate Images` outputs remain unwatermarked.
13. A backend without `SwarmWatermark` is not selected for a watermark request.
14. An audio-only request with watermark parameters gives the intended readable error.

- [ ] **Step 4: Record manual results before any completion claim**

If any check fails, capture the exact workflow branch and symptom and use `superpowers:systematic-debugging` before changing implementation. If all checks pass, record the maintainer's confirmation in the handoff; repository policy does not permit the agent to substitute its own test run.
