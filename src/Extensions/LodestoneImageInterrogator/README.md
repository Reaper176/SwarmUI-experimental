# Lodestone Image Interrogator

Lodestone Image Interrogator is a SwarmUI extension for local image interrogation with the Lodestone tagger. It adds an Image Interrogator panel that can analyze an image and return Lodestone tag categories for use in prompts.

## Setup

1. Install the extension into SwarmUI.
2. Restart SwarmUI, and rebuild if your SwarmUI install requires it for extension changes.
3. Open the Image Interrogator panel.
4. Click **Setup**.

The Setup button creates the extension's local Python dependencies and downloads the required Hugging Face model files. Model downloads happen only after Setup is clicked.

The default setup path targets Linux AMD ROCm gfx110X GPUs. It installs PyTorch and Torchvision from `https://repo.amd.com/rocm/whl/gfx110X-dgpu/`, validates that PyTorch can see a ROCm GPU, and runs inference on the PyTorch `cuda` device name used by both CUDA and ROCm builds.

The main model file, `tagger_proto.safetensors`, is about 5.27 GB. Setup downloads it from `lodestones/taggerine` on Hugging Face, along with the required vocabulary file.

## Privacy

Inference runs locally. Images are sent only to the local runner process used by this extension. The extension does not use remote hosted inference.

## Content Notice

The Lodestone vocabulary is based on e621 and Danbooru annotations. Results can include rating tags and adult-content tags. The panel's category filters control which tag categories are included in the prompt output.

## License

MIT
