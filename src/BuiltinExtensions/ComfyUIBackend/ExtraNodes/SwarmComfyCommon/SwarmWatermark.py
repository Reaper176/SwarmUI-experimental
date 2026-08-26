import math
from pathlib import Path

import numpy as np
import torch
from PIL import Image


_ASSET_DIRECTORY = Path(__file__).resolve().parent / "assets"
_PRESET_FILES = {
    "speaker-white": "WatermarkSpeakerWhite.png",
    "speaker-black": "WatermarkSpeakerBlack.png",
}


def _clamp_number(value, minimum, maximum, fallback):
    value = float(value)
    if not math.isfinite(value):
        value = fallback
    return max(minimum, min(maximum, value))


def _validate_image(image, name):
    if not isinstance(image, torch.Tensor) or image.dim() != 4:
        raise ValueError(f"{name} must be a nonempty [B,H,W,C] IMAGE tensor")
    if any(size <= 0 for size in image.shape[:3]) or image.shape[3] < 3:
        raise ValueError(f"{name} must be a nonempty [B,H,W,C] IMAGE tensor with at least 3 channels")


def _load_preset(preset):
    filename = _PRESET_FILES.get(preset)
    if filename is None:
        raise ValueError(f"Unknown watermark preset: {preset}")
    path = _ASSET_DIRECTORY / filename
    if not path.is_file():
        raise FileNotFoundError(f"Watermark preset file not found: {path}")
    with Image.open(path) as image:
        rgba = np.array(image.convert("RGBA"), dtype=np.float32, copy=True) / 255.0
    return torch.from_numpy(rgba).unsqueeze(0)


def _normalize_mask(mask):
    if not isinstance(mask, torch.Tensor) or mask.dim() < 2 or mask.dim() > 4:
        raise ValueError("watermark_mask must be a 2D, 3D, or 4D MASK tensor")
    if any(size <= 0 for size in mask.shape):
        raise ValueError("watermark_mask must be nonempty")

    if mask.dim() == 2:
        return mask.unsqueeze(0).unsqueeze(-1)
    if mask.dim() == 3:
        return mask.unsqueeze(-1)

    if mask.shape[-1] == 1:
        return mask
    if mask.shape[1] == 1:
        return mask.permute(0, 2, 3, 1)[..., :1]
    return mask[..., :1]


def _sampling_coordinates(source_height, source_width, target_width, target_height, position_x, position_y, destination_x_start, destination_x_end, destination_y_start, destination_y_end, device):
    coordinate_dtype = torch.float32
    destination_x = torch.arange(destination_x_start, destination_x_end, device=device, dtype=coordinate_dtype)
    destination_y = torch.arange(destination_y_start, destination_y_end, device=device, dtype=coordinate_dtype)
    source_x = ((destination_x - position_x + 0.5) * source_width / target_width - 0.5).clamp(0, source_width - 1)
    source_y = ((destination_y - position_y + 0.5) * source_height / target_height - 0.5).clamp(0, source_height - 1)
    source_x_floor = source_x.floor().long()
    source_y_floor = source_y.floor().long()
    source_x_next = (source_x_floor + 1).clamp(max=source_width - 1)
    source_y_next = (source_y_floor + 1).clamp(max=source_height - 1)
    source_x_weight = source_x - source_x_floor.to(coordinate_dtype)
    source_y_weight = source_y - source_y_floor.to(coordinate_dtype)
    return (source_x_floor, source_x_next, source_x_weight, source_y_floor, source_y_next, source_y_weight)


def _blend_corners(top_left, top_right, bottom_left, bottom_right, source_x_weight, source_y_weight):
    top = top_left * (1.0 - source_x_weight[None, :, None]) + top_right * source_x_weight[None, :, None]
    bottom = bottom_left * (1.0 - source_x_weight[None, :, None]) + bottom_right * source_x_weight[None, :, None]
    return top * (1.0 - source_y_weight[:, None, None]) + bottom * source_y_weight[:, None, None]


def _sample_bilinear(source, target_width, target_height, position_x, position_y, destination_x_start, destination_x_end, destination_y_start, destination_y_end):
    source_height, source_width, _ = source.shape
    coordinates = _sampling_coordinates(
        source_height,
        source_width,
        target_width,
        target_height,
        position_x,
        position_y,
        destination_x_start,
        destination_x_end,
        destination_y_start,
        destination_y_end,
        source.device,
    )
    source_x_floor, source_x_next, source_x_weight, source_y_floor, source_y_next, source_y_weight = coordinates
    top_left = source[source_y_floor[:, None], source_x_floor[None, :]]
    top_right = source[source_y_floor[:, None], source_x_next[None, :]]
    bottom_left = source[source_y_next[:, None], source_x_floor[None, :]]
    bottom_right = source[source_y_next[:, None], source_x_next[None, :]]
    return _blend_corners(top_left, top_right, bottom_left, bottom_right, source_x_weight, source_y_weight).to(dtype=source.dtype)


def _sample_alpha_at_watermark_taps(alpha, watermark_height, watermark_width, source_x_floor, source_x_next, source_y_floor, source_y_next):
    alpha_height, alpha_width, _ = alpha.shape

    def alpha_coordinates(watermark_indices, watermark_size, alpha_size):
        position = ((watermark_indices.to(torch.float32) + 0.5) * alpha_size / watermark_size - 0.5).clamp(0, alpha_size - 1)
        floor = position.floor().long()
        next_index = (floor + 1).clamp(max=alpha_size - 1)
        weight = position - floor.to(torch.float32)
        return floor, next_index, weight

    alpha_x_floor, alpha_x_floor_next, alpha_x_floor_weight = alpha_coordinates(source_x_floor, watermark_width, alpha_width)
    alpha_x_next, alpha_x_next_next, alpha_x_next_weight = alpha_coordinates(source_x_next, watermark_width, alpha_width)
    alpha_y_floor, alpha_y_floor_next, alpha_y_floor_weight = alpha_coordinates(source_y_floor, watermark_height, alpha_height)
    alpha_y_next, alpha_y_next_next, alpha_y_next_weight = alpha_coordinates(source_y_next, watermark_height, alpha_height)

    def sample_alpha_at_position(x_floor, x_next, x_weight, y_floor, y_next, y_weight):
        top_left = alpha[y_floor[:, None], x_floor[None, :]]
        top_right = alpha[y_floor[:, None], x_next[None, :]]
        bottom_left = alpha[y_next[:, None], x_floor[None, :]]
        bottom_right = alpha[y_next[:, None], x_next[None, :]]
        return _blend_corners(top_left, top_right, bottom_left, bottom_right, x_weight, y_weight).clamp(0.0, 1.0)

    return (
        sample_alpha_at_position(alpha_x_floor, alpha_x_floor_next, alpha_x_floor_weight, alpha_y_floor, alpha_y_floor_next, alpha_y_floor_weight),
        sample_alpha_at_position(alpha_x_next, alpha_x_next_next, alpha_x_next_weight, alpha_y_floor, alpha_y_floor_next, alpha_y_floor_weight),
        sample_alpha_at_position(alpha_x_floor, alpha_x_floor_next, alpha_x_floor_weight, alpha_y_next, alpha_y_next_next, alpha_y_next_weight),
        sample_alpha_at_position(alpha_x_next, alpha_x_next_next, alpha_x_next_weight, alpha_y_next, alpha_y_next_next, alpha_y_next_weight),
    )


def _sample_premultiplied(watermark, alpha, target_width, target_height, position_x, position_y, destination_x_start, destination_x_end, destination_y_start, destination_y_end):
    source_height, source_width, _ = watermark.shape
    coordinates = _sampling_coordinates(
        source_height,
        source_width,
        target_width,
        target_height,
        position_x,
        position_y,
        destination_x_start,
        destination_x_end,
        destination_y_start,
        destination_y_end,
        watermark.device,
    )
    source_x_floor, source_x_next, source_x_weight, source_y_floor, source_y_next, source_y_weight = coordinates
    if alpha.shape[:2] == watermark.shape[:2]:
        top_left_alpha = alpha[source_y_floor[:, None], source_x_floor[None, :]].clamp(0.0, 1.0)
        top_right_alpha = alpha[source_y_floor[:, None], source_x_next[None, :]].clamp(0.0, 1.0)
        bottom_left_alpha = alpha[source_y_next[:, None], source_x_floor[None, :]].clamp(0.0, 1.0)
        bottom_right_alpha = alpha[source_y_next[:, None], source_x_next[None, :]].clamp(0.0, 1.0)
    else:
        # Align differently-sized masks to each RGB tap before premultiplying, without building a resized watermark.
        top_left_alpha, top_right_alpha, bottom_left_alpha, bottom_right_alpha = _sample_alpha_at_watermark_taps(
            alpha,
            source_height,
            source_width,
            source_x_floor,
            source_x_next,
            source_y_floor,
            source_y_next,
        )
    top_left = watermark[source_y_floor[:, None], source_x_floor[None, :]] * top_left_alpha
    top_right = watermark[source_y_floor[:, None], source_x_next[None, :]] * top_right_alpha
    bottom_left = watermark[source_y_next[:, None], source_x_floor[None, :]] * bottom_left_alpha
    bottom_right = watermark[source_y_next[:, None], source_x_next[None, :]] * bottom_right_alpha
    premultiplied = _blend_corners(top_left, top_right, bottom_left, bottom_right, source_x_weight, source_y_weight)
    if alpha.shape[:2] == watermark.shape[:2]:
        sampled_alpha = _blend_corners(top_left_alpha, top_right_alpha, bottom_left_alpha, bottom_right_alpha, source_x_weight, source_y_weight)
    else:
        # Keep direct mask-to-final alpha separate from the source-grid alpha used for premultiplication.
        resized_source_alpha = _blend_corners(top_left_alpha, top_right_alpha, bottom_left_alpha, bottom_right_alpha, source_x_weight, source_y_weight)
        sampled_alpha = _sample_bilinear(
            alpha,
            target_width,
            target_height,
            position_x,
            position_y,
            destination_x_start,
            destination_x_end,
            destination_y_start,
            destination_y_end,
        ).clamp(0.0, 1.0)
        safe_source_alpha = resized_source_alpha.clamp_min(1e-8)
        unpremultiplied = torch.where(
            resized_source_alpha > 1e-8,
            premultiplied / safe_source_alpha,
            torch.zeros_like(premultiplied),
        )
        premultiplied = (unpremultiplied * sampled_alpha).to(dtype=watermark.dtype)
    return (premultiplied, sampled_alpha.to(dtype=watermark.dtype))


class SwarmWatermark:
    @classmethod
    def INPUT_TYPES(cls):
        return {
            "required": {
                "image": ("IMAGE",),
                "watermark_preset": (["speaker-white", "speaker-black"],),
                "alignment": ([
                    "bottom-left",
                    "bottom-right",
                    "center",
                    "top-center",
                    "top-left",
                    "top-right",
                    "center-right",
                    "center-left",
                    "bottom-center",
                ],),
                "offset_percentage": ("FLOAT", {"default": 2.0, "min": 0.0, "max": 100.0, "step": 0.1}),
                "resize_percentage": ("FLOAT", {"default": 20.0, "min": 0.1, "max": 200.0, "step": 0.1}),
                "opacity": ("FLOAT", {"default": 100.0, "min": 0.0, "max": 100.0, "step": 1.0}),
            },
            "optional": {
                "watermark": ("IMAGE",),
                "watermark_mask": ("MASK",),
            },
        }

    CATEGORY = "SwarmUI/images"
    RETURN_TYPES = ("IMAGE",)
    FUNCTION = "apply_watermark"
    DESCRIPTION = "Overlays a preset or custom watermark on each image frame."

    def apply_watermark(self, image, watermark_preset, alignment, offset_percentage, resize_percentage, opacity, watermark=None, watermark_mask=None):
        _validate_image(image, "image")
        frame_count, frame_height, frame_width, _ = image.shape

        offset = _clamp_number(offset_percentage, 0.0, 100.0, 2.0)
        resize = _clamp_number(resize_percentage, 0.1, 200.0, 20.0)
        opacity_value = _clamp_number(opacity, 0.0, 100.0, 100.0) / 100.0
        destination = image.clone()
        if opacity_value == 0:
            return (destination,)

        if watermark is None:
            watermark = _load_preset(watermark_preset)
        else:
            _validate_image(watermark, "watermark")

        watermark_count, watermark_height, watermark_width, watermark_channels = watermark.shape
        if watermark_count not in (1, frame_count):
            raise ValueError("watermark batch must contain one image or match the input image batch")

        watermark = watermark.to(device=image.device, dtype=image.dtype)
        if watermark_channels > 3 and watermark_mask is None:
            alpha = watermark[..., 3:4]
        else:
            alpha = torch.ones((watermark_count, watermark_height, watermark_width, 1), device=image.device, dtype=image.dtype)

        if watermark_mask is not None:
            normalized_mask = _normalize_mask(watermark_mask)
            mask_count = normalized_mask.shape[0]
            if mask_count not in (1, watermark_count, frame_count):
                raise ValueError("watermark_mask batch must contain one image or match the watermark/input batch")
            alpha = normalized_mask.to(device=image.device, dtype=image.dtype)

        new_width = max(1, int(round(frame_width * resize / 100.0)))
        new_height = max(1, int(round(watermark_height * new_width / watermark_width)))

        offset_x = frame_width * offset / 100.0
        offset_y = frame_height * offset / 100.0
        positions = {
            "bottom-right": (frame_width - new_width - offset_x, frame_height - new_height - offset_y),
            "center": ((frame_width - new_width) / 2.0, (frame_height - new_height) / 2.0),
            "top-center": ((frame_width - new_width) / 2.0, offset_y),
            "top-left": (offset_x, offset_y),
            "top-right": (frame_width - new_width - offset_x, offset_y),
            "center-right": (frame_width - new_width - offset_x, (frame_height - new_height) / 2.0),
            "center-left": (offset_x, (frame_height - new_height) / 2.0),
            "bottom-center": ((frame_width - new_width) / 2.0, frame_height - new_height - offset_y),
            "bottom-left": (offset_x, frame_height - new_height - offset_y),
        }
        if alignment not in positions:
            raise ValueError(f"Unknown watermark alignment: {alignment}")
        position_x, position_y = positions[alignment]
        position_x = int(round(position_x))
        position_y = int(round(position_y))

        destination_x_start = max(0, position_x)
        destination_y_start = max(0, position_y)
        destination_x_end = min(frame_width, position_x + new_width)
        destination_y_end = min(frame_height, position_y + new_height)
        if destination_x_start < destination_x_end and destination_y_start < destination_y_end:
            for frame_index in range(frame_count):
                watermark_index = 0 if watermark_count == 1 else frame_index
                mask_index = 0 if alpha.shape[0] == 1 else frame_index
                watermark_source = watermark[watermark_index, ..., :3]
                alpha_source = alpha[mask_index]
                watermark_slice, alpha_slice = _sample_premultiplied(
                    watermark_source,
                    alpha_source,
                    new_width,
                    new_height,
                    position_x,
                    position_y,
                    destination_x_start,
                    destination_x_end,
                    destination_y_start,
                    destination_y_end,
                )
                alpha_slice = alpha_slice.clamp(0.0, 1.0) * opacity_value
                watermark_slice = watermark_slice * opacity_value
                destination_slice = destination[frame_index, destination_y_start:destination_y_end, destination_x_start:destination_x_end, :3]
                destination[frame_index, destination_y_start:destination_y_end, destination_x_start:destination_x_end, :3] = (
                    destination_slice * (1.0 - alpha_slice) + watermark_slice
                )

        destination[..., :3] = destination[..., :3].clamp(0.0, 1.0)
        return (destination,)


NODE_CLASS_MAPPINGS = {"SwarmWatermark": SwarmWatermark}
