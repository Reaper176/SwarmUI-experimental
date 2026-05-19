# Detail Daemon SwarmUI Extension Design

## Goal

Add Jonseed's ComfyUI Detail Daemon sampler as a portable SwarmUI extension that can be copied or published independently of a local SwarmUI checkout.

The extension will follow SwarmUI's extension conventions from `docs/Making Extensions.md`: it will live under `src/Extensions/`, include its own project file, register an installable ComfyUI node pack, and expose normal Swarm text-to-image parameters without modifying Swarm core.

## Scope

The extension supports normal Swarm-generated ComfyUI image sampling paths where Swarm emits `KSamplerAdvanced` or `SwarmKSampler` nodes. It applies to base generation and refiner generation when the user enables the Detail Daemon parameter group.

The extension does not initially target custom workflows, video-specific sampler chains, DAAM-style sampler replacements, or other workflows that already replace Swarm's sampler with a custom node type. Those paths can be added later if the generated node contracts are verified.

## Architecture

Create `src/Extensions/SwarmUI-DetailDaemon/` with:

- `SwarmUI-DetailDaemon.csproj`, importing `../../SwarmUI.extension.props`
- `DetailDaemonExtension.cs`, containing the extension class
- `README.md`, documenting install, rebuild, and usage

The extension namespace will not use the reserved `SwarmUI` namespace. The extension will rely only on public Swarm extension APIs and workflow generator helpers.

## ComfyUI Feature Registration

The extension registers Jonseed's ComfyUI node pack:

- Display name: `Detail Daemon`
- Feature id: `detail_daemon`
- URL: `https://github.com/Jonseed/ComfyUI-Detail-Daemon`
- Author: `Jonseed`

It maps the Comfy node class `DetailDaemonSamplerNode` to `detail_daemon` through `ComfyUIBackendExtension.NodeToFeatureMap`. This allows Swarm to detect when the node is installed and show the install flow when the user enables parameters before the node exists.

The first implementation will not pin a commit. That keeps the portable extension tracking upstream fixes by default. If upstream later introduces a breaking change, the extension can add `ComfyUISelfStartBackend.ComfyNodeGitPins` with a known-good commit.

## User Parameters

Register a toggleable advanced parameter group named `Detail Daemon`.

Parameters mirror the Comfy node inputs:

- `Detail Amount`: double, default `0.1`
- `Start`: double, default `0.2`, range `0` to `1`
- `End`: double, default `0.8`, range `0` to `1`
- `Bias`: double, default `0.5`, range `0` to `1`
- `Exponent`: double, default `1.0`
- `Start Offset`: double, default `0`
- `End Offset`: double, default `0`
- `Fade`: double, default `0`
- `Smooth`: bool, default `true`
- `CFG Scale Override`: double, toggleable, default `0`

The group is inactive unless the user enables it. `Detail Amount` is the primary parameter used to determine activation.

## Workflow Generation

The extension adds a workflow generator step after Swarm's main and refiner sampler steps and before final output saving.

When inactive, the step returns immediately.

When active, the step verifies that `detail_daemon` is available in `g.Features`. If it is unavailable, it throws a `SwarmUserErrorException` explaining that the Detail Daemon Comfy node pack must be installed.

For each eligible sampler node:

1. Read the original sampler inputs: model, seed or noise seed, steps, cfg, sampler name, scheduler, positive, negative, latent image, start step, end step, add noise, and return-with-leftover-noise.
2. Create a noise node:
   - `RandomNoise` when `add_noise` is enabled.
   - `DisableNoise` when `add_noise` is disabled.
3. Create `KSamplerSelect` using the original sampler name.
4. Create `CFGGuider` using the original model, conditioning, and CFG scale.
5. Create a scheduler node from the original scheduler value:
   - `KarrasScheduler` for `karras`
   - `SDTurboScheduler` for `turbo`
   - `BasicScheduler` for all other scheduler values
6. Apply `SplitSigmas` for nonzero start step and bounded end step.
7. Create `DetailDaemonSamplerNode` with the selected sampler and Detail Daemon parameter values.
8. Replace the original sampler node object in place with `SamplerCustomAdvanced`, keeping the original node id so downstream connections stay valid.

After replacement, downstream decode and save nodes continue to reference the original node id and receive the wrapped sampler output.

## Error Handling

The extension avoids silent partial behavior:

- Missing Comfy node feature while parameters are enabled produces a user-facing error.
- Unsupported sampler node shapes are skipped rather than rewritten incorrectly.
- If no eligible sampler nodes are found while the group is enabled, the extension logs a warning.

## Testing

Verification will include:

- Build SwarmUI with the extension enabled.
- Confirm the extension project is discovered by Swarm's extension build system.
- Confirm the registered parameters compile and appear under a feature-gated group.
- Generate or inspect a workflow where Detail Daemon is enabled and verify that an eligible sampler is rewritten to include `DetailDaemonSamplerNode` and `SamplerCustomAdvanced`.
- Generate or inspect a workflow where Detail Daemon is disabled and verify that the workflow is unchanged.

If no direct workflow test harness exists for this extension, validation will use `dotnet build` plus workflow JSON inspection from Swarm's normal generation path.
