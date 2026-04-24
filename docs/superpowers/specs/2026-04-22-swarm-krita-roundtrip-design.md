# SwarmUI <-> Krita Image Round-Trip Design

Date: 2026-04-22
Status: Draft approved for planning

## Goal

Provide a simple local-only workflow to move images between SwarmUI and Krita with explicit user actions in both directions.

Version 1 is intentionally limited to flattened images only:

- SwarmUI sends the current image to Krita
- Krita sends the current flattened document back to SwarmUI
- SwarmUI replaces the active init/editor image with the returned image

Version 1 does not attempt to preserve Krita layers, Swarm editor layers, masks, prompt metadata, or Krita AI Diffusion plugin state.

## Scope

In scope:

- A SwarmUI button to send the current image to Krita
- A Krita plugin action to send the current flattened image back to SwarmUI
- A SwarmUI API endpoint that accepts the returned image
- Local-only operation on the same machine
- Clear user-facing error handling for common failure modes

Out of scope:

- Remote or browser-only clients
- Synchronizing layered document state
- Mapping Swarm metadata into Krita AI Diffusion settings
- Automatic live sync or file polling
- Multi-image session management
- Full project serialization between the two applications

## User Experience

### SwarmUI to Krita

1. The user has an image open in SwarmUI.
2. The user clicks `Send to Krita`.
3. SwarmUI exports the current image to a temporary PNG file.
4. A local launcher opens that PNG in Krita.

Expected result:

- Krita opens with the exported image ready for editing.

### Krita to SwarmUI

1. The user edits the image in Krita.
2. The user clicks `Send to Swarm`.
3. The Krita plugin flattens the current document.
4. The plugin uploads the flattened image to a local SwarmUI API endpoint.
5. SwarmUI replaces the current init/editor image with the uploaded image.

Expected result:

- The currently active image in SwarmUI updates to the flattened Krita result.

## Recommended Approach

Use an explicit two-part integration:

- SwarmUI exports a PNG and invokes a local Krita launcher path
- A lightweight Krita plugin posts a flattened PNG back to a dedicated SwarmUI endpoint

This approach is preferred over polling a shared folder because it is more deterministic and easier to reason about. It is preferred over a more ambitious document-state integration because it keeps version 1 focused on a reliable image-only workflow.

## Architecture

### SwarmUI responsibilities

- Add a UI action to export the current active image to Krita
- Resolve or use a configured Krita executable path
- Write the exported image to a temp file owned by SwarmUI
- Launch Krita with that file path
- Expose a local API endpoint to receive a returned image from the Krita plugin
- Update the active init/editor image after successful upload

### Krita plugin responsibilities

- Add a menu or toolbar action labeled `Send to Swarm`
- Flatten the current document into a single image
- Encode that image as PNG
- POST the image to SwarmUI
- Show success or error feedback inside Krita

## Data Contract

### SwarmUI -> Krita

Transport:

- Local temp PNG file on disk

Payload:

- Flattened PNG only

No metadata is sent in version 1.

### Krita -> SwarmUI

Transport:

- HTTP POST to a SwarmUI local API route

Payload:

- Flattened PNG image

Suggested request fields:

- image file body or multipart file field
- optional source marker such as `source=krita`

Suggested response:

- success boolean
- optional message for UI display

Version 1 should not require prompt, seed, mask, or workflow metadata.

## API Behavior

SwarmUI should expose a dedicated route for Krita image return, logically separate from generic upload behavior if possible. The endpoint should:

- accept only local image uploads relevant to this integration
- validate the uploaded content is a supported image
- reject malformed requests with a clear message
- replace the currently active init/editor image on success
- return a compact success or failure response for the Krita plugin

Because this feature is local-only, the route should be designed with loopback usage in mind and should not assume remote cross-machine workflows.

## Launch Strategy

Version 1 should be local desktop only. The launch flow should assume:

- SwarmUI server and Krita are on the same machine
- the user intends to open a desktop app from a local SwarmUI session

Implementation should prefer an explicit configured Krita executable path or an existing local app-launch pattern already used by the repo. If automatic discovery is attempted, it should be a convenience layer rather than the only supported path.

## Error Handling

### SwarmUI side

- If no current image is available, disable or reject `Send to Krita`
- If the export fails, show an error and do not launch Krita
- If the Krita executable path is not configured or invalid, show a clear local setup error
- If SwarmUI receives an invalid returned image, reject it and preserve the current editor state

### Krita side

- If there is no open document, disable or reject `Send to Swarm`
- If document flattening/export fails, show an error and do not send anything
- If SwarmUI cannot be reached, show the network or connection error
- If SwarmUI rejects the image, show the returned error message

## Security and Boundaries

- Treat the integration as local-only
- Default target URL should be loopback, such as `127.0.0.1`
- Do not broaden SwarmUI's exposure to remote launch control
- Keep the returned-image endpoint narrowly scoped to this workflow

Version 1 does not need authentication beyond local-machine assumptions unless SwarmUI already has an established local API auth pattern that should be reused.

## Non-Goals and Deferred Work

The following are explicitly deferred until after version 1 is stable:

- passing prompt or generation metadata to Krita AI Diffusion
- preserving editor masks or layer stacks
- returning multiple layers from Krita
- session pairing across several open images
- automatic reimport when a Krita document changes
- remote support for non-local SwarmUI deployments

## Success Criteria

The design is successful when:

- a user can click `Send to Krita` in SwarmUI and get the current image opened in Krita
- a user can click `Send to Swarm` in Krita and have the active SwarmUI init/editor image replaced
- the round-trip works without manual file browsing
- failure cases produce actionable error messages instead of silent failure

## Testing Approach

This repository does not use automated tests for this class of feature, so validation will rely on static review and manual developer verification.

Manual verification should cover:

- sending a normal image from SwarmUI to Krita
- sending a modified image back from Krita to SwarmUI
- handling missing Krita executable configuration
- handling no document open in Krita
- handling SwarmUI unavailable from the Krita plugin
- handling invalid upload payloads

## Open Implementation Decisions

These are implementation details to settle during planning, not product-scope questions:

- exact SwarmUI API route name and whether it reuses existing upload plumbing
- exact placement of the `Send to Krita` button in the SwarmUI editor UI
- how Krita executable configuration is stored and surfaced
- whether the Krita plugin sends raw request bytes or multipart form data
- whether SwarmUI imports the returned image directly into the editor canvas or through an existing image-load helper
