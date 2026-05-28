from .latent_colormatch import LatentColorMatch, LatentColorMatchSimple
from .latent_adjust import LatentImageAdjust

NODE_CLASS_MAPPINGS = {
    "LatentColorMatch": LatentColorMatch,
    "LatentColorMatchSimple": LatentColorMatchSimple,
    "LatentImageAdjust": LatentImageAdjust
}

NODE_DISPLAY_NAME_MAPPINGS = {
    "LatentColorMatch": "🎨 Latent Color Match",
    "LatentColorMatchSimple": "🎨 Latent Color Match (Simple)",
    "LatentImageAdjust": "🎛️ Latent Image Adjust"
}

__all__ = ['NODE_CLASS_MAPPINGS', 'NODE_DISPLAY_NAME_MAPPINGS']
