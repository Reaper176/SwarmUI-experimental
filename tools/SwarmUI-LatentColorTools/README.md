# SwarmUI Latent Color Tools

This extension exposes
[DenRakEiw/Latent_Nodes](https://github.com/DenRakEiw/Latent_Nodes)
as native SwarmUI Generate controls while keeping all changes contained inside
this extension folder.

## Generate Controls

The `Latent Color Tools` parameter group can apply:

- `Image Adjust`: hue, saturation, brightness, contrast, and sharpness.
- `Color Match`: VAE-encodes a reference image and matches the active latent to it.
- `Image Adjust + Color Match`: applies Image Adjust first, then Color Match.

The extension injects `LatentImageAdjust` and `LatentColorMatch` into Swarm's
generated Comfy workflow before sampling. When disabled, it does nothing.

## Dependencies

The upstream nodes can run with reduced functionality if optional dependencies
are missing. For full color-space and advanced color-matcher support, install:

```bash
pip install -r src/Extensions/SwarmUI-LatentColorTools/LatentColorNodes/requirements.txt
```

Use the Python environment that runs the SwarmUI ComfyUI backend.

## Upstream

- Repository: https://github.com/DenRakEiw/Latent_Nodes
- License: MIT
