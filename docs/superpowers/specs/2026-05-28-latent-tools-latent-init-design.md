# Latent Tools Latent Init Design

## Goal

Add a narrow SwarmUI Generate tab integration for the installed
`latent-tools` ComfyUI node pack. Users can replace Swarm's normal initial
empty image latent with a Gaussian or Uniform random latent.

## Scope

- Add Generate parameters under the existing `Latent Tools` group.
- Support `LTGaussianLatent` and `LTUniformLatent`.
- Use Swarm's current width, height, batch size, and seed.
- Apply only when generating from an empty image latent.

Out of scope:

- Init-image/img2img replacement.
- Video or audio latent generation.
- `LTKSampler` sampler replacement.
- `LTPreviewLatent` preview insertion.
- Latent blend, reshape, concat, or math operation controls.

## Parameters

- `[LatentTools] Init Mode`
  - Values: `Disabled`, `Gaussian`, `Uniform`
  - Default: `Disabled`
- `[LatentTools] Channels`
  - Integer, default `4`
- `[LatentTools] Gaussian Mean`
  - Decimal, default `0`
- `[LatentTools] Gaussian Std`
  - Decimal, default `1`
- `[LatentTools] Uniform Min`
  - Decimal, default `-1`
- `[LatentTools] Uniform Max`
  - Decimal, default `1`

The existing installer button remains in the same group when the backend does
not report the `latent_tools` feature.

## Workflow Behavior

The extension adds a workflow step after Swarm creates the initial media and
before prompt conditioning/sampling. If mode is disabled, it does nothing.

When mode is enabled:

1. Verify the backend reports `latent_tools`.
2. Skip if `Init Image` is set.
3. Skip if the current media is not `LATENT_IMAGE`.
4. Create either `LTGaussianLatent` or `LTUniformLatent`.
5. Replace `g.CurrentMedia` with the new latent node path, preserving the
   current image compat class and recording width/height.

The node receives:

- `channels`: `[LatentTools] Channels`
- `width`: current image width
- `height`: current image height
- `batch_size`: current Swarm batch size
- `seed`: current Swarm seed
- distribution-specific numeric parameters

## Validation

Agents do not run SwarmUI builds or tests in this repo. Validation is static:

- Confirm parameters are registered in `LatentToolsExtension.cs`.
- Confirm `assets/latent_tools.js` still renders the installer button.
- Confirm workflow step creates only `LTGaussianLatent`/`LTUniformLatent`.
- Confirm the step explicitly skips init-image and non-image-latent cases.

Manual developer validation:

- Rebuild/relaunch SwarmUI.
- Confirm `Latent Tools` shows the new controls after the backend reports
  `latent_tools`.
- Generate with `Gaussian` and `Uniform` on a text-to-image model.
- Confirm init-image workflows are unaffected when `Init Image` is set.
