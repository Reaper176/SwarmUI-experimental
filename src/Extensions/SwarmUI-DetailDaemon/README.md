# SwarmUI Detail Daemon

Portable SwarmUI extension for Jonseed's [ComfyUI-Detail-Daemon](https://github.com/Jonseed/ComfyUI-Detail-Daemon).

The extension adds a **Detail Daemon** advanced parameter group to Swarm's Generate UI. When enabled, Swarm wraps compatible ComfyUI sampler nodes with `DetailDaemonSamplerNode` and `SamplerCustomAdvanced` so the normal Swarm generation workflow can use the Detail Daemon sampler wrapper.

## Install

1. Place this folder at `SwarmUI/src/Extensions/SwarmUI-DetailDaemon`.
2. Rebuild or update SwarmUI.
3. Launch SwarmUI and install the prompted `Detail Daemon` ComfyUI node pack if it is not already installed.
4. Restart the ComfyUI backend after node installation.

## Usage

Enable advanced options, open the **Detail Daemon** group, and enable **[DD] Detail Amount**. The upstream defaults are used by default:

- `detail_amount`: `0.1`
- `start`: `0.2`
- `end`: `0.8`
- `bias`: `0.5`
- `exponent`: `1.0`
- `start_offset`: `0`
- `end_offset`: `0`
- `fade`: `0`
- `smooth`: `true`
- `cfg_scale_override`: `0`

Detail amounts between `0` and `1.0` are a practical starting range. Higher values can become oversharpened or produce an HDR-like effect.

## Scope

This extension targets normal Swarm-generated image sampler nodes: `KSamplerAdvanced` and `SwarmKSampler`. It does not rewrite custom workflows or other extensions that already replace Swarm's sampler with their own custom sampler node.
