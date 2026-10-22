# Local SwarmUI reference setup, 2026-10-22

This snapshot accompanies the fork's current source. Pull `master` from `https://github.com/Reaper176/SwarmUI-experimental`, then fully restart SwarmUI and its ComfyUI backend so the C# workflow and Python nodes both reload. The native Anima Attention Couple fixes first appeared in `e11de73b` and `39b26e8d`.

## Regional prompting

Anima Attention Couple is included in `SwarmComfyCommon/SwarmAttentionCouple.py`. ComfyUI-ppm and Strata are not required for this path. Select **Attention Couple** under Regional Prompting. Individual region strength controls regional coverage; Global Region Factor controls shared-prompt blending inside regions.

The former local `strata_image_controls` startup callback replaced Swarm's method with PPM. The accompanying `strata_image_controls.py` removes that replacement and delegates saved Strata Anima nodes to Swarm. If this custom node is already installed, replace its `__init__.py` with this file while the backend is stopped. Update any source copy used to install it too. If it is absent, there is no reason to install it for Anima. The optional explicit Strata Cosmos node still requires PPM; its other upscale and SAM3 utilities are retained.

From this reference directory, `python -m unittest -v test_strata_native_attention.py` checks the corrected adapter using isolated modules and no model or GPU imports. Set `STRATA_IMAGE_CONTROLS_PATH` to an installed `__init__.py` to check that copy instead. These checks simulate startup; a running backend must still be restarted.

## Backend reference

- ComfyUI: official `https://github.com/Comfy-Org/ComfyUI`, commit `8f04fd6ecf4a548937971e840af2a985529fab76`.
- `comfy-local.patch` records this machine's three tracked ComfyUI changes for queue wakeup and host-memory cleanup. These are separate from Anima coupling. To reproduce them on a checkout at that commit, run `git apply --unidiff-zero --check /path/to/comfy-local.patch`, then `git apply --unidiff-zero /path/to/comfy-local.patch`.
- Python 3.14.7; PyTorch 2.14.0+rocm10.1.0; torchvision 0.29.0a0+rocm10.1.0; torchaudio 2.11.0.3+rocm10.1.0; Triton 3.8.0+git669b31ac.rocm10.1.0; comfy-kitchen 0.2.39; comfy-aimdo 0.5.6; NumPy 2.4.6. These are observed versions, not a portable requirements file. Use the runtime appropriate to the receiving machine's GPU.
- Backend arguments: `--preview-method auto --cache-classic --reserve-vram 4 --force-non-blocking --bf16-unet --disable-dynamic-vram --disable-xformers --disable-cuda-malloc --fast fp16_accumulation --use-pytorch-cross-attention`. These memory and attention choices can affect performance and numerical results; adjust hardware-specific choices deliberately.
- Optional PPM package: 1.1.10, registry snapshot without Git history. Its `src/attention_couple/cosmos_couple.py` SHA256 was `8121567b71a5f3cadb738d4ba06b01c09542e5a1be2da39682f22b6aa4a8d9c6`.
- Optional `comfyui-anima-3-8B`: `https://github.com/GumGum10/comfyui-anima-3-8B`, commit `381c13af328b958febf86c155d2f4b007cd0f55b`; this machine also has three untracked embedded-loader files. This is not a complete reproduction of that optional pack.
- Optional Anima Remap: `https://github.com/shin131002/ComfyUI-Anima-Remap`, commit `ad6a2c4af587b434f452932a0b1cb67199dcbdc5`.

This is a source and backend reference, not a complete environment lock. Other custom nodes and hardware-specific behavior can still differ. Models, user databases, recovery data, outputs, private settings, and the machine-specific GPU admission guard configuration are not included.

## Comparison test

Use the same actual checkpoint on both machines: `Anima/2.9B/novaFurryAM_v4029B.safetensors`. The saved image reported model hash `0x940540db8d5ac637cf6a8a45c400bb7e7ad29028b711d3f3dc302d796ce054c6`; image reuse selects by model name and does not verify checkpoint bytes.

Use seed `523371448`, 1024×1024, 35 steps, CFG 3, an empty negative prompt, Attention Couple, Global Region Factor 0.5, and:

```text
duo, a cat and a dog,

<region:0,0,0.5,1,0.1> cat
<region:0.5,0,0.5,1,0.1> dog
```

Explicitly match sampler and scheduler: the supplied image metadata omitted them, so they were not established by this comparison. Then repeat with both individual region strengths set to 1. Matching source, parameters, and model improves reproducibility; GPU/runtime differences can still prevent pixel-identical images.

## Validation limits

Native coupling, override removal, the legacy Strata bridge, and encoder discovery passed 40 isolated CPU tests in total. Both changed frontend scripts passed Node syntax checks. History/settings changes were reviewed statically; C# builds and NUnit execution were deferred under the repository's agent policy. The pre-removal successful image used the PPM override. A generation after restarting with the native path is still needed to validate visual behavior. No builds, live generations, model loads, or GPU operations were performed during this change.
