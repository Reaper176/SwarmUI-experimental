# Generate Watermark

## Goal

Add an optional final-output watermark feature to the Generate tab. It must
work for still images, image batches, and video frame batches while leaving
generation, previews, intermediate outputs, audio, and frame-rate metadata
unchanged.

The feature is inspired by the installed `Quick Watermark Video` custom node,
but SwarmUI will provide an independent implementation and independently
created preset assets. SwarmUI must not require the third-party custom node.

## User Interface

Register a collapsed `Watermark` group under the Advanced Generate controls.
The group has an enable toggle. When disabled, none of its parameters affect
the generated workflow.

Expose these controls:

| Parameter | Type | Default | Behavior |
| --- | --- | --- | --- |
| Watermark Preset | dropdown | `speaker-white` | Built-in fallback when no custom watermark is supplied; values are `speaker-white` and `speaker-black`. |
| Watermark Image | image input | unset | Optional custom image. When set, it overrides the preset. Embedded PNG alpha is used automatically. |
| Watermark Mask | image input | unset | Optional explicit opacity mask. When set, it overrides embedded alpha. |
| Watermark Alignment | dropdown | `bottom-right` | Values are `bottom-right`, `center`, `top-center`, `top-left`, `top-right`, `center-right`, `center-left`, `bottom-center`, and `bottom-left`. |
| Watermark Offset Percentage | float slider | `2.0` | Range 0-100, step 0.1. Horizontal and vertical offsets are calculated independently from final output width and height. |
| Watermark Resize Percentage | float slider | `20.0` | Range 0.1-200, step 0.1. Sets watermark width as a percentage of final output width while preserving aspect ratio. |
| Watermark Opacity | float slider | `60.0` | Range 0-100, step 1. Applies a global multiplier to preset, embedded, or explicit alpha. |

The explicit mask follows SwarmUI's normal image-mask convention: white is
opaque and black is transparent. It may have a different resolution from the
watermark image and is resized to the watermark's final dimensions.

## Components

### Core parameters

Add the advanced group and typed parameters to the core text-to-image
parameter registry. Parameters remain available to presets, metadata reuse,
and API requests through the existing parameter system.

The controls use a dedicated Comfy backend feature flag, `swarm_watermark`.
They are shown only when an eligible backend reports the managed watermark
node. A forced API request to an unsupported backend fails with a clear
unsupported-feature error rather than producing an invalid workflow.

### Managed Comfy node

Add a `SwarmWatermark` node to the Swarm-managed Comfy `ExtraNodes` package.
It accepts an `IMAGE` batch, preset selection, alignment, offset percentage,
resize percentage, opacity, an optional custom `IMAGE`, and an optional
`MASK`. It returns an `IMAGE` batch.

The node performs tensor compositing on the input tensor's device and dtype.
It supports one watermark for the whole batch or a watermark batch matching
the input frame count. It validates tensor shapes and batch compatibility,
preserves non-RGB channels on the destination, clips partially off-canvas
watermarks, and clamps the result to the valid image range.

The two transparent speaker presets are SwarmUI-owned assets created for this
feature. Preset alpha is used directly. A custom watermark overrides the
preset; an explicit mask overrides custom embedded alpha; an opaque custom
image with no mask is treated as fully opaque before the global opacity is
applied.

Register the node in the Comfy node-name and capability mappings so workflow
generation uses a stable constant and backend capability discovery can emit
`swarm_watermark`.

### Workflow integration

Add a workflow-generator helper that:

1. Returns without changing the graph when the Watermark group is disabled.
2. Ensures the current output is decoded to an image/frame batch.
3. Loads a custom watermark without resizing it to generation dimensions.
4. Uses the loader's alpha output for embedded PNG transparency.
5. Converts a separately supplied Watermark Mask image to a standard mask.
6. Creates `SwarmWatermark` with all selected values.
7. Replaces `CurrentMedia` with the watermarked output while preserving its
   attached audio, FPS, dimensions, compatibility metadata, and other media
   bookkeeping.

Invoke this helper explicitly only on terminal output paths:

- final still-image or direct text-to-video output after decode, segmentation,
  background removal, restoration, trimming, and interpolation;
- final image-to-video output after video generation, restoration, and
  interpolation;
- final extended-video output after all segments are conjoined, restored, and
  interpolated.

Do not invoke it before image-to-video generation, on previews, or before
intermediate `SaveOutput` calls. This keeps the watermark out of model inputs
and avoids repeated compositing during multi-stage workflows.

## Data Flow

When enabled, the final decoded `IMAGE` batch and the resolved watermark data
enter `SwarmWatermark`. Target width is the final frame width multiplied by
the resize percentage. Target height preserves the watermark aspect ratio.
Offsets are based on final frame dimensions, and alignment determines the
placement origin. RGB is alpha-composited across every frame, after which the
existing output path saves a still/image batch or encodes the frames as video.

Opacity zero produces an unchanged clone. Oversized or off-canvas placement is
cropped to the intersection with the destination rather than failing. Audio
and FPS never enter the node and continue through their current paths.

## Error Handling

- Enabling the feature for an audio-only output produces a user-readable
  image/video-required error.
- Unknown presets and missing preset assets produce direct errors naming the
  invalid or missing preset.
- Non-tensor, empty, malformed, or incompatible image/mask batches produce
  concise validation errors.
- Percentage values are bounded by parameter validation and defensively
  clamped in the node.
- A backend without `SwarmWatermark` is rejected through capability handling
  before workflow submission.

## Compatibility and Scope

This feature applies to Swarm-generated workflows. It does not rewrite custom
workflows opened in the Comfy Workflow tab and does not add a Utilities action.
It does not alter the third-party `ComfyUI-QuickWatermark` installation.

With the group disabled, generated workflows must remain unchanged. Existing
save formats, metadata behavior, image batching, video encoding, audio
attachment, FPS selection, frame interpolation, and video extension behavior
remain in scope only to ensure the final watermark insertion preserves them.

## Verification

Repository policy forbids agents from running builds or automated tests. The
implementation will therefore be checked through static workflow tracing and
available non-test linters only. Static review must confirm that terminal save
branches apply the helper exactly once and intermediate branches never apply
it.

The maintainer's manual verification matrix is:

- disabled group produces the prior workflow;
- both built-in presets;
- opaque custom PNG;
- transparent custom PNG using embedded alpha;
- explicit mask overriding embedded alpha;
- all nine alignments;
- zero, partial, and full opacity;
- oversized and partially clipped placement;
- a still image and a multi-image batch;
- direct text-to-video with unchanged audio and FPS;
- image-to-video;
- frame-interpolated video;
- extended video;
- unsupported backend and audio-only error messages.
