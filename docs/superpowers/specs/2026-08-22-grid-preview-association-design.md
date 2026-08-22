# Grid Preview Association Design

## Goal

Ensure every live Grid Generator preview is associated with the same unique batch identifier as its completed image, so each grid cell updates independently and the full image replaces its preview in SwarmUI.

## Cause

The shared generation handler indexes preview cards by `gen_progress.batch_index` and final images by `batch_index`. A backend can emit each separately scheduled grid job with a local batch index, while the Grid Generator gives final files the grid-wide `iteration` index. When those differ, subsequent previews overwrite one card and the final file cannot replace that card or the active main image.

## Design

At the Grid Generator boundary, wrap the output callback passed to `T2IEngine.CreateImageTask`. For `gen_progress` messages, copy the grid runner's current `iteration` into the nested `batch_index` before enqueuing the message. Final output messages already use that iteration and are unchanged.

This keeps the shared frontend handler backend-agnostic, preserves normal generation behavior, and gives previews and final images the common identifier `<request_id>_<iteration>`.

## Scope

- Modify only `src/BuiltinExtensions/GridGenerator/GridGeneratorExtension.cs`.
- Do not change image saving, grid output files, or the general generation frontend.
- Do not add automated tests: this repository explicitly prohibits agents from running tests and has no usable automated test setup for this GPU-driven behavior.

## Validation

Perform static data-flow review: verify that each grid task passes its unique iteration into the progress payload and that the existing final callback already emits the same iteration. The maintainer will then run a multi-cell grid and confirm that multiple previews appear and each final image opens inside SwarmUI.
