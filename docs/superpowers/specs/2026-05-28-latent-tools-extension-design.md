# Latent Tools Extension Design

## Goal

Create a SwarmUI extension that makes the upstream
`Machines-of-Disruption/latent-tools` ComfyUI custom node pack available through
SwarmUI's existing installable feature flow.

This first pass is intentionally limited to installation and feature detection.
It does not expose new Generate tab parameters or modify generated workflows.

## Scope

- Add a self-contained extension under `src/Extensions/SwarmUI-LatentTools/`.
- Register `https://github.com/Machines-of-Disruption/latent-tools` as an
  installable ComfyUI feature.
- Map one stable latent-tools node class to a Swarm feature flag so installed
  backends can report support for the feature.
- Add a short README explaining what the extension installs and where the
  upstream node functionality lives.

Out of scope:

- Copying upstream Python node files into SwarmUI.
- Adding Generate tab parameters.
- Replacing SwarmUI's sampler workflow with `LTKSampler`.
- Building a custom UI for latent preview or latent operation graph editing.

## Extension Structure

The extension folder will contain:

- `LatentToolsExtension.cs`: the Swarm extension entrypoint.
- `SwarmUI-LatentTools.csproj`: standard extension project file importing
  `../../SwarmUI.extension.props`.
- `README.md`: usage and maintenance notes.

The C# class will use a non-`SwarmUI` namespace because Swarm reserves that
namespace for built-ins.

## Runtime Behavior

On initialization, the extension will:

1. Register an installable feature:
   - Display name: `Latent Tools`
   - Feature id: `latent_tools`
   - URL: `https://github.com/Machines-of-Disruption/latent-tools`
   - Author: `Machines-of-Disruption`
2. Register a node-to-feature mapping for a representative node class from the
   upstream pack. `LTPreviewLatent` is suitable because it is documented as a
   core preview/debug node in the upstream README.

The extension will not run external network calls itself. SwarmUI's existing
installable feature system will perform the clone/update when the user chooses
to install the feature.

## Error Handling

There is no generated workflow step in this first pass, so no new per-generation
error paths are introduced.

If the upstream node pack fails to install or load, SwarmUI's existing ComfyUI
backend feature detection and installable feature UI will surface that state.

## Validation

Repository rules state that agents do not run builds or tests for SwarmUI. The
change will be validated through static inspection:

- Confirm the extension class follows the established extension pattern.
- Confirm the project file imports the shared extension props.
- Confirm the installable feature metadata and node mapping are present.
- Confirm the change does not touch user data folders, generated folders, or
  upstream downloaded backend code.

Manual developer validation after rebuild/relaunch:

- Open SwarmUI's extension/installable feature UI.
- Install `Latent Tools`.
- Restart or reload the ComfyUI backend as needed.
- Confirm latent-tools nodes are available in ComfyUI.
