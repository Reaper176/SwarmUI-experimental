# Magick.NET migration for this fork

This fork incorporates the ImageSharp replacement from upstream [PR #1578](https://github.com/mcmonkeyprojects/SwarmUI/pull/1578), commit `d951a39dd9313c7795e485628a47c87131f82ab6`. That proposal was still an unmerged preview when integrated on 2026-10-22. The fork uses **Magick.NET-Q16-AnyCPU 15.0.0** instead of the proposal's 14.16.0. ImageSharp and ImageSharp.Drawing are removed. Magick.NET is [Apache-2.0 licensed](https://github.com/dlemstra/Magick.NET/blob/15.0.0/License.txt) and needs no build-time license key.

Core compatibility fixes preserve filename reservations and grid history, port Classic Inpaint masks, preserve ICC color profiles, retain Unicode EXIF comments, and avoid cloned-pixel-cache corruption in WebP alpha stealth metadata. Decoding accepts only the existing raster formats, selected from their actual file headers; vector and script formats are rejected. GIF and animated WebP frames and millisecond WebP timings are retained. **APNG decoding is unsupported and rejected explicitly** because the PNG codec otherwise silently reads only the default frame. Use animated WebP or GIF for those uploads.

## Native font compatibility

Server text rendering uses TTF copies of the existing bundled Inter and Unifont fonts. The native renderer cannot load the browser's WOFF2 files, and treating those paths as font-family names silently selects unrelated glyphs. The WOFF2 assets remain for the browser. To reproduce the format-only conversion, install FontTools with WOFF2 support in a development environment and run this from the repository root:

```python
from pathlib import Path
from fontTools.ttLib import TTFont

fonts = Path("src/wwwroot/fonts")
for name in ("Inter", "unifont-12.0.01"):
    with TTFont(fonts / f"{name}.woff2", recalcTimestamp=False) as font:
        font.flavor = None
        font.save(fonts / f"{name}.ttf")
```

The generated TTF files are committed; users do not need FontTools to run or build Swarm. The original [Unifont source archive](font-sources/unifont-12.0.01.tar.gz) is included alongside its license and the conversion instructions.

See the [bundled font notices](../../../../src/wwwroot/fonts/README.md) for the original asset versions and retained licenses.

## Installed external extensions

External extension repositories are separate from the Swarm fork. Apply these patches to their source before restarting Swarm with the new core. They contain no credentials or user settings.

| Extension | Tested source revision | Patch |
| --- | --- | --- |
| [MagicPrompt](https://github.com/HartsyAI/SwarmUI-MagicPromptExtension) | `8f3f8428c667c7ff95cd4672ad7a2c50e8af589a` | [MagicPrompt.patch](MagicPrompt.patch) |
| [WhatTheDuck](https://github.com/jtreminio/SwarmUI-WhatTheDuckExtension) | `c0eeedde07f67eb374d5f7d2af81195edbc65b23` | [WhatTheDuck.patch](WhatTheDuck.patch) |

Run from the Swarm repository root:

```sh
git -C src/Extensions/SwarmUI-MagicPromptExtension apply --check "$(pwd)/docs/ReferenceSetups/2026-10-22/magick-compat/MagicPrompt.patch"
git -C src/Extensions/SwarmUI-WhatTheDuckExtension apply --check "$(pwd)/docs/ReferenceSetups/2026-10-22/magick-compat/WhatTheDuck.patch"
git -C src/Extensions/SwarmUI-MagicPromptExtension apply "$(pwd)/docs/ReferenceSetups/2026-10-22/magick-compat/MagicPrompt.patch"
git -C src/Extensions/SwarmUI-WhatTheDuckExtension apply "$(pwd)/docs/ReferenceSetups/2026-10-22/magick-compat/WhatTheDuck.patch"
```

Apply each patch once. If `--check` fails on another extension revision, inspect its changes before applying. MagicPrompt thumbnails now use the correct PNG MIME type and clamp extremely narrow dimensions. WhatTheDuck edits only the EXIF chunk: it preserves compressed image data, animation chunks, other EXIF fields, and both optional EXIF-header conventions. Swarm's core-aware extension cache recompiles extensions for the new core at startup. Future extension updates can conflict with these local patches; recheck them after updating.

The [optional extension smoke runner](ExtensionChecks/README.md) verifies actual patched source files. Normal Swarm tests do not require these extensions to be installed.

## Security scope

[Magick.NET 15.0.0](https://github.com/dlemstra/Magick.NET/releases/tag/15.0.0) was the latest published stable package at integration time and bundles ImageMagick 7.1.2-33. It includes security fixes absent from 14.16.0. The direct and transitive NuGet audit reported no vulnerable packages, but that does not establish that every native operation is unaffected by every advisory.

Two newer low-severity native advisories are fixed in ImageMagick 7.1.2-34, which was not yet in a published Magick.NET release:

- [GHSA-g5j8-ff59-wp88](https://github.com/ImageMagick/ImageMagick/security/advisories/GHSA-g5j8-ff59-wp88): a TIFF stack overflow requiring the DNG decoder to be disabled. The tested Linux x64 package has DNG registered, decoding enabled, and read policy enabled. This fork does not disable it. Reassess if the native package or ImageMagick policy changes; disabling DNG globally is not a mitigation for this issue.
- [GHSA-x48p-6655-8qgv](https://github.com/ImageMagick/ImageMagick/security/advisories/GHSA-x48p-6655-8qgv): a heap overwrite in the Morph operation. No Morph calls occur in the checked core or installed extensions.

The triggering prerequisites were absent in this checked setup; these findings are not a universal vulnerability-free claim for all platforms or configurations. Update the pinned package when a release incorporating those native fixes becomes available.

## Validation

CPU checks cover PNG/JPEG/WebP metadata and MIME consistency, Unicode EXIF byte order, transparency, stealth payloads and channel preservation, ICC profiles, raster-only decoding, explicit APNG rejection, GIF/WebP animation frames and delays, Classic Inpaint mask expansion/feathering, and known bundled-font metrics. The optional extension runner covers 25 thumbnail and EXIF-preservation cases. Builds and tests do not start Swarm, ComfyUI, an LLM backend, or image generation.
