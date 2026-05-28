# Latent Tools Blend Design

## Goal

Add optional `LTBlendLatent` support to the existing Latent Tools latent init
integration. Users can blend Swarm's normal empty latent with the selected
Gaussian or Uniform latent before sampling.

## Scope

- Add Blend Mode and Blend Ratio parameters under the existing `Latent Tools`
  group.
- Support latent-tools `LTBlendLatent`.
- Apply only when `[LatentTools] Init Mode` is `Gaussian` or `Uniform`.
- Preserve existing guards for init-image and non-image latent workflows.

Out of scope:

- Blending arbitrary user-provided latents.
- Blend scheduling over steps.
- Sampler replacement.
- Preview output.

## Workflow Behavior

The existing latent init step keeps Swarm's current empty latent as `latent1`,
creates the selected Gaussian or Uniform latent as `latent2`, then:

- If blend mode is `Disabled`, uses `latent2` directly.
- If blend mode is enabled, creates `LTBlendLatent` with:
  - `latent1`: Swarm's original empty latent
  - `latent2`: generated Gaussian or Uniform latent
  - `mode`: selected blend mode
  - `ratio`: selected blend ratio
  - `seed`: Swarm seed

The blended node output becomes `g.CurrentMedia`.

## Validation

Agents do not run SwarmUI builds or tests in this repo. Static validation:

- Confirm `LTBlendLatent` is in `NodeToFeatureMap`.
- Confirm blend params are registered.
- Confirm workflow creates `LTBlendLatent` only when blend mode is not disabled.
- Confirm `assets/latent_tools.js` updates field visibility.
