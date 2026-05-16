# Lodestone Image Interrogator Extension Design

## Summary

Build a self-contained SwarmUI extension named **Lodestone Image Interrogator**. The extension adds a dedicated image interrogation panel that runs the Lodestone tagger locally, using `lodestones/taggerine` model files from Hugging Face after the user explicitly clicks a setup button.

The extension is intended to be shareable as a normal SwarmUI extension folder. A user should be able to install the extension, open SwarmUI, click setup, and then interrogate images without manually cloning a separate inference project.

## Goals

- Add a dedicated **Image Interrogator** panel in the Generate page.
- Let users send the current or selected Generate-tab image into the interrogator panel.
- Let users send generated tags back to the Generate prompt field.
- Keep setup explicit before any Hugging Face download or dependency installation.
- Run the model locally through a bundled Python runner managed by the extension.
- Support full single-image tag controls in v1: threshold, max tags, category filtering, category grouping, rating tag handling, and copy/append/replace prompt actions.

## Non-Goals

- Batch interrogation in v1.
- Remote hosted inference.
- Comfy workflow/node integration for the first version.
- Training dataset management.
- Editing SwarmUI core unless an existing extension hook is missing and a focused core hook is approved separately.

## External Model

The extension targets the Hugging Face model currently published as `lodestones/taggerine`, formerly linked through `lodestones/tagger-experiment`.

Required files:

- `tagger_proto.safetensors`: model weights, about 5.27 GB.
- `tagger_vocab_with_categories_and_alias_updated.json`: tag vocabulary and category metadata.

The model card describes a DINOv3 ViT-H/16+ multi-label image tagger trained on e621 and Danbooru annotations. It emits raw logits that must be passed through sigmoid to produce per-tag probabilities. The extension must clearly communicate that explicit/rating tags may be present because the vocabulary includes booru rating and adult-content tags.

## Extension Shape

Folder and class naming:

- Extension folder: `src/Extensions/LodestoneImageInterrogator`
- Main class: `LodestoneImageInterrogator`
- UI display name: `Lodestone Image Interrogator`

Core extension files:

- Root `.csproj` importing `../../SwarmUI.extension.props`.
- Root C# extension class deriving from `Extension`.
- C# API routes for setup state, setup execution, runner lifecycle, and interrogation.
- `Assets/lodestone_interrogator.js` for client UI behavior.
- `Assets/lodestone_interrogator.css` for panel-specific styling.
- `Tabs/Text2Image/Image Interrogator.html` for the dedicated panel.
- Bundled Python runner files under an extension-owned directory such as `Runner/`.
- README documenting install, setup download size, dependencies, local files, and network behavior.
- MIT license unless a different compatible license is chosen before implementation.

## Setup Flow

The panel opens in an unconfigured state when required files or dependencies are missing. It shows a **Setup** button and concise setup status.

When setup starts:

1. The UI calls a Swarm extension API route.
2. The API checks whether setup is already running and rejects duplicate setup requests.
3. The extension creates or verifies an isolated local Python environment owned by the extension.
4. The extension installs required Python packages.
5. The extension downloads the model and vocabulary files from Hugging Face.
6. The extension validates file presence and expected basic metadata, including non-empty vocab and model file existence.
7. The UI reports completion or a clear failure message.

Setup must not start automatically on page load. External web connections happen only after the user starts setup.

The design should prefer resumable or idempotent setup where practical: if dependencies are present and one model file is already downloaded, setup should not repeat completed work unnecessarily.

## Local Runner

The bundled Python runner performs single-image inference. It should avoid depending on the upstream tagger UI templates or web server. A simple command-line or long-lived local worker process is acceptable, with preference decided during implementation after inspecting Swarm's process helpers.

Runner responsibilities:

- Load the safetensors model and vocab.
- Preprocess images with ImageNet mean/std and an input resolution compatible with the model.
- Run inference on CUDA when available, with CPU fallback if feasible.
- Apply sigmoid to logits.
- Return tags, probabilities, categories, and aliases when available.
- Return structured JSON errors for dependency, model load, CUDA memory, invalid image, and inference failures.

The C# layer owns request validation, permissions, process lifecycle, temporary file handling, and translating runner responses to API JSON.

## UI Design

The panel should match SwarmUI's existing Generate-page styling and avoid a marketing-style page. The primary screen is the working tool.

Panel regions:

- Setup/status strip with model state, device state when known, and setup action.
- Image input area with preview and controls for upload/drop/select-from-Generate.
- Tag control area with threshold, max tag count, category filters, rating tag handling, and output formatting.
- Results area with grouped tags and confidence values.
- Prompt action area with copy, replace Generate prompt, append to Generate prompt, and optional prepend behavior if it fits existing prompt utilities.

The Generate tab should gain a minimal action to send its current/selected image to the Image Interrogator panel. The panel should have a clear button to send the current tag output back to the Generate prompt.

If SwarmUI already has reusable prompt/image helpers, the implementation should use them rather than duplicating prompt-field or image-selection logic.

## API Design

Initial API routes should cover:

- Get setup/status state.
- Start setup.
- Interrogate one image.
- Optionally unload the model/stop the runner.

The API must use an extension-specific permission group. Power users should be the likely default permission level, matching comparable utility extensions.

Interrogation input should support either:

- An uploaded image payload from the panel, or
- A server-known image reference from the Generate/history flow if Swarm exposes a safe helper for that.

The API response should include:

- Success flag.
- Error message when failed.
- Ordered flat tag list.
- Grouped tag list by category.
- Probability/confidence for each tag.
- Prompt-ready output string based on requested filters.

## Error Handling

Important failures should produce actionable UI messages:

- Setup already running.
- Hugging Face download failed.
- Python environment creation failed.
- Dependency install failed.
- Model files missing or incomplete.
- No compatible GPU found, with CPU fallback note if CPU is used.
- CUDA out of memory.
- Invalid or unsupported image input.
- Runner crashed or timed out.

The extension should log detailed server-side errors through SwarmUI logging while returning concise client-facing messages.

## Data And Privacy

The model runs locally. The only intended external network access is setup-time download from Hugging Face after user confirmation. Images selected for interrogation remain local and are passed only to the local runner.

The README and panel setup text must disclose:

- The model download source.
- The approximate model size.
- That tags may include adult/rating vocabulary.
- That no remote inference is used by this extension design.

## Validation

Per repository policy, agents must not run builds or tests. Static validation should include:

- Confirm extension files follow SwarmUI extension structure.
- Confirm C# code avoids `var`, uses braced blocks, and uses XML docs for fields.
- Confirm JS uses `let`, full braced blocks, and existing utilities where practical.
- Confirm setup routes are idempotent and guarded against duplicate runs.
- Confirm UI has explicit setup before external network access.
- Confirm error paths return structured responses.

Manual developer verification should include:

- Fresh install with no model files.
- Setup download/install path.
- Reopening SwarmUI after setup.
- Interrogating a local image.
- Sending tags to the Generate prompt as replace and append.
- Handling missing model files and failed setup gracefully.

