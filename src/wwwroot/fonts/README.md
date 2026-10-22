# Bundled font assets

- `Inter.woff2` is Inter 3.019 (`git-0a5106e0b`), copyright 2020 The Inter Project Authors. [Inter.LICENSE.txt](Inter.LICENSE.txt) is the SIL Open Font License 1.1 notice from the official [v3.19 release](https://github.com/rsms/inter/tree/v3.19).
- `unifont-12.0.01.woff2` is GNU Unifont 12.0.01, copyright 1998-2019 Roman Czyborra, Paul Hardy, Qianqian Fang, Andrew Miller, Johnnie Weaver, David Corbett, et al. It is GPL version 2 or later **with the GNU font embedding exception**, as declared in its embedded font metadata and the original release's [COPYING](Unifont-12.0.01-COPYING.txt). The complete original COPYING is retained verbatim; its font-files section applies to these assets. Original source: [unifont-12.0.01.tar.gz](https://unifoundry.com/pub/unifont/unifont-12.0.01/unifont-12.0.01.tar.gz).

On 2026-10-22, `Inter.ttf` and `unifont-12.0.01.ttf` were generated from those WOFF2 files by removing only the WOFF2 container compression with FontTools. Font outlines, names, copyright records, and licenses were retained. The GNU font embedding exception is expressly retained for the converted Unifont. The TTF files support native server text rendering; the browser continues to use WOFF2.

The complete original [Unifont source archive](../../../docs/ReferenceSetups/2026-10-22/magick-compat/font-sources/unifont-12.0.01.tar.gz) accompanies the font, including its editable glyph sources, build scripts, and license. The conversion input WOFF2 and instructions are included in this repository as well.

The reproducible conversion is documented in the [migration notes](../../../docs/ReferenceSetups/2026-10-22/magick-compat/README.md). FontTools is a development-only conversion tool; it is not required to build or run Swarm.

Material Symbols retains its separate [Apache-2.0 license notice](MaterialSymbolsOutlined.LICENSE.txt).
