import json

import numpy as np
import torch
import cv2


def fill_mask_holes(mask: np.ndarray, kernel_size: int = 5) -> np.ndarray:
    """Fill small holes in a binary mask using morphological close + flood fill."""
    mask = np.squeeze(mask)
    if mask.ndim == 0:
        return np.array([[255]], dtype=np.uint8)
    if mask.ndim > 2:
        mask = mask[:, :, 0]
    if mask.dtype != np.uint8:
        if mask.dtype == bool or (mask.max() <= 1 and mask.dtype in [np.float32, np.float64]):
            mask = (mask * 255).astype(np.uint8)
        else:
            mask = mask.astype(np.uint8)
    kernel = cv2.getStructuringElement(cv2.MORPH_ELLIPSE, (kernel_size, kernel_size))
    closed_mask = cv2.morphologyEx(mask, cv2.MORPH_CLOSE, kernel)
    filled_mask = closed_mask.copy()
    h, w = filled_mask.shape
    canvas = np.zeros((h + 2, w + 2), dtype=np.uint8)
    canvas[1:-1, 1:-1] = filled_mask
    cv2.floodFill(canvas, None, (0, 0), 128)
    filled_mask = np.where(canvas[1:-1, 1:-1] == 128, 0, 255).astype(np.uint8)
    return filled_mask


class SwarmSam3PointsFromJson:
    """Converts Swarm point JSON strings into a SAM3_POINTS_PROMPT object."""

    @classmethod
    def INPUT_TYPES(s):
        return {
            "required": {
                "image": ("IMAGE",),
                "points_json": ("STRING", {"forceInput": True}),
                "is_foreground": ("BOOLEAN", {"default": True}),
            }
        }

    RETURN_TYPES = ("SAM3_POINTS_PROMPT",)
    RETURN_NAMES = ("points_prompt",)
    FUNCTION = "convert"
    CATEGORY = "SAM3"

    def convert(self, image, points_json, is_foreground=True):
        coords = json.loads(points_json)
        img_width = image.shape[2]
        img_height = image.shape[1]
        label = 1 if is_foreground else 0
        points = []
        labels = []
        for point in coords:
            points.append([float(point["x"]) / img_width, float(point["y"]) / img_height])
            labels.append(label)
        return ({"points": points, "labels": labels},)


class SwarmSam3BBoxFromJson:
    """Converts a JSON bounding box string '[x1,y1,x2,y2]' into a SAM3_BOXES_PROMPT object."""

    @classmethod
    def INPUT_TYPES(s):
        return {
            "required": {
                "image": ("IMAGE",),
                "bbox_json": ("STRING", {"forceInput": True}),
            }
        }

    RETURN_TYPES = ("SAM3_BOXES_PROMPT",)
    RETURN_NAMES = ("bboxes_prompt",)
    FUNCTION = "convert"
    CATEGORY = "SAM3"

    def convert(self, image, bbox_json):
        coords = json.loads(bbox_json)
        img_width = image.shape[2]
        img_height = image.shape[1]
        x1 = float(coords[0]) / img_width
        y1 = float(coords[1]) / img_height
        x2 = float(coords[2]) / img_width
        y2 = float(coords[3]) / img_height
        center_x = (x1 + x2) / 2
        center_y = (y1 + y2) / 2
        width = x2 - x1
        height = y2 - y1
        return ({"boxes": [[center_x, center_y, width, height]], "labels": [True]},)


class SwarmSam3MaskPostProcess:
    """Post-processes SAM3 segmentation masks with hole-filling and unions batches."""

    @classmethod
    def INPUT_TYPES(s):
        return {
            "required": {
                "mask": ("MASK",),
            },
            "optional": {
                "fill_holes": ("BOOLEAN", {"default": True}),
                "hole_kernel_size": ("INT", {"default": 5, "min": 1, "max": 21, "step": 2}),
            },
        }

    RETURN_TYPES = ("MASK",)
    RETURN_NAMES = ("mask",)
    FUNCTION = "process"
    CATEGORY = "SAM3"

    def process(self, mask, fill_holes=True, hole_kernel_size=5):
        if mask.ndim == 2:
            mask = mask.unsqueeze(0)
        out_list = []
        for i in range(mask.shape[0]):
            m = mask[i].cpu().numpy()
            m_uint8 = (m * 255).astype(np.uint8)
            if fill_holes:
                m_uint8 = fill_mask_holes(m_uint8, kernel_size=hole_kernel_size)
            out_list.append(torch.from_numpy(m_uint8.astype(np.float32) / 255.0))
        if not out_list:
            return (torch.zeros(1, 1, 1),)
        masks = torch.stack(out_list, dim=0)
        if masks.shape[0] > 1:
            masks = torch.max(masks, dim=0).values.unsqueeze(0)
        return (masks,)


NODE_CLASS_MAPPINGS = {
    "SwarmSam3PointsFromJson": SwarmSam3PointsFromJson,
    "SwarmSam3BBoxFromJson": SwarmSam3BBoxFromJson,
    "SwarmSam3MaskPostProcess": SwarmSam3MaskPostProcess,
}
