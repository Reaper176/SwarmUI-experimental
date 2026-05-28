# SwarmUI Latent Tools

This extension registers the
[Machines-of-Disruption/latent-tools](https://github.com/Machines-of-Disruption/latent-tools)
ComfyUI custom node pack as an installable feature in SwarmUI.

Latent Tools provides ComfyUI nodes for previewing, generating, blending,
reshaping, concatenating, and mathematically manipulating latent tensors. It
also includes numeric helper nodes and a sampler variant that can accept
additional latent noise.

## Generate Controls

When latent-tools is installed, this extension adds a `Latent Tools` parameter
group to the Generate tab.

- `Init Mode`: choose Gaussian, Uniform, or Gaussian + Uniform latent creation.
- `Blend Mode`: optionally blend single-source modes with Swarm's normal empty
  latent, or blend Gaussian and Uniform together in two-source mode.
- `Op`: optionally applies `LTLatentOp` before sampling.
- `Use LTKSampler`: opt-in only. When checked, Swarm's base sampler node is
  replaced with `LTKSampler` and the generated Latent Tools latent is passed as
  `latent_noise`.

`LTKSampler` is intentionally limited to compatible full base text-to-image
sampling. It will report an error instead of silently replacing unsupported
sampler setups.

## Installation

1. Rebuild or relaunch SwarmUI so this extension is loaded.
2. Open SwarmUI's installable feature UI.
3. Install `Latent Tools`.
4. Restart or reload the ComfyUI backend if needed.
5. Confirm the latent-tools nodes are available in ComfyUI.

## Upstream

- Repository: https://github.com/Machines-of-Disruption/latent-tools
- Maintainer: Machines-of-Disruption
