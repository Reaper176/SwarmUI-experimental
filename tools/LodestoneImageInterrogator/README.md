# Lodestone Image Interrogator

Lodestone Image Interrogator is a SwarmUI extension for local image interrogation with the Lodestone tagger. It adds an Image Interrogator panel that can analyze an image and return Lodestone tag categories for use in prompts.

## Setup

1. Install the extension into SwarmUI.
2. Restart SwarmUI, and rebuild if your SwarmUI install requires it for extension changes.
3. Open the Image Interrogator panel.
4. Choose a **GPU Backend** and click **Setup**.

The Setup button creates the extension's local Python dependencies and downloads the required Hugging Face model files. Model downloads happen only after Setup is clicked.

The backend selector controls which PyTorch wheels are installed:

- **Auto / Existing PyTorch** installs the default PyPI PyTorch wheels and lets the runner choose GPU when PyTorch exposes one, otherwise CPU.
- **NVIDIA CUDA** installs PyTorch and Torchvision from the CUDA wheel index.
- **AMD ROCm** installs PyTorch and Torchvision from the stable PyTorch ROCm wheel index and validates a Linux ROCm/HIP build.
- **CPU** installs CPU PyTorch wheels and runs inference on CPU.

PyTorch uses the `cuda` device name for both NVIDIA CUDA and AMD ROCm builds. Changing the backend selector after setup requires running Setup again so the local environment matches the selected backend.

The main model file, `tagger_proto.safetensors`, is about 5.27 GB. Setup downloads it from `lodestones/taggerine` on Hugging Face, along with the required vocabulary file.

## Privacy

Inference runs locally. Images are sent only to the local runner process used by this extension. The extension does not use remote hosted inference.

## Content Notice

The Lodestone vocabulary is based on e621 and Danbooru annotations. Results can include adult-content tags. The panel's category filters control which tag categories are included in the prompt output.

## License

MIT
