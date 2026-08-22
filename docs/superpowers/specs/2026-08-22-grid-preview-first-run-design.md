# Grid First-Run Preview Design

## Goal

Allow the first Grid Generator run after a model becomes unloaded to display live previews, without requiring an interrupt and restart.

## Cause

Normal generation calls `mainGenHandler.preloadCurrentModelForPreviews` before opening its generation socket. That helper was explicitly added for cold-storage first runs that otherwise cannot emit previews. Grid Generator validates the selected model but directly calls `doGenerate`, skipping the preload.

## Design

In Grid Generator's existing `doGenWrapper`, call the existing `mainGenHandler.preloadCurrentModelForPreviews` with the selected model and use its callback to invoke `this.doGenerate()`. Keep the existing model-selection validation and error path unchanged.

## Scope and Validation

- Modify only `src/BuiltinExtensions/GridGenerator/Assets/grid_gen.js`.
- Reuse the existing normal-generation helper; do not duplicate model-loading logic or touch backend code.
- Agent-run builds and tests are prohibited by this repository. Review the callback flow statically, then manually verify a cold-model first grid run with Show Outputs enabled.
