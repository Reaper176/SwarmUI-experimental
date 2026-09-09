# LoRA classification inspection

Inspected the safetensors JSON headers and adjacent metadata of the eleven files reported as unknown. Tensor payloads were not read or changed. No builds, generation runs, or automated tests were performed.

| Files | Evidence and conclusion |
| --- | --- |
| `1.5/SD_1.5.safetensors` | Contains `image_proj.*` and `ip_adapter.*.to_k_ip/to_v_ip.weight`, rather than LoRA weights. This is an SD 1.5 IP-Adapter stored in the LoRA directory. It needs an IP-Adapter loader and appropriate model placement, not a LoRA classification override. Subsequently moved, at the maintainer’s request, with its `.cm-info.json` sidecar to `/opt/stabilitymatrix/Data/Models/ipadapter/`; filenames and contents were preserved. |
| `Il/Styles/Masami.safetensors` | Contains complete `lycoris_*.a1/a2/b1/b2.weight` quartets, SDXL block signatures, and sidecar `BaseModel: Illustrious`. This is a GLoRA/LyCORIS adapter for the SDXL family. The installed ComfyUI source supports this layout, but Swarm's adapter gate omitted it. Added recognition and advanced the classification cache revision to 5. Runtime verification remains with the maintainer. |
| `Il/contract_controller_illus01_v1.safetensors`, `Il/contract_controller_illus01_v1.1.safetensors` | Each has only eight tensors targeting `lora_unet_output_blocks_8_0` (embedding, input, output, and skip-connection weights). Adjacent metadata does not provide a reliable base-model declaration. These are sparse LoRAs; the current classifier's identifying attention/text-encoder signatures are absent. No architecture was assigned from their filenames. |
| Seven `anima/animaStyleTest02_*.safetensors` files | Each has 224 tensors in `style_kv_dit_blocks_*_self_attn` modules, including route/payload/output projections and style normalization. Six explicitly declare `anima-preview/style-dual-kv-network`; `reiji` has the same tensor layout without that declaration. These are custom style adapters, not standard LoRAs. No matching loader was found in the inspected Swarm built-in source or installed ComfyUI core/custom-node Python source. Assigning an ordinary Anima LoRA class would not make them loadable. |

Backend reference inspected locally: `ComfyUI-05-03-2026/comfy/weight_adapter/glora.py` and `comfy/lora.py`. GLoRA recognition requires all four weights and leaves architecture selection to existing model signatures/metadata. The cache revision allows previously unknown adapters to be reconsidered once after rebuilding.

See [filename changes](model-filename-renames-2026-09-09.md) for the separate eight-model rename operation. Those renames preserved matching metadata and previews.
