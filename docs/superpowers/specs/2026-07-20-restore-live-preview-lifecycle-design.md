# Restore Live Preview Lifecycle Design

## Problem

The asynchronous preview sender in `SwarmKSampler.py` rejects every preview because `_preview_sampler_active` is initialized to `False` and is never activated around sampling. Progress events still reach the frontend, so SwarmUI displays a placeholder and spinner until the final image arrives.

## Design

Restore the lifecycle wrapper introduced with the asynchronous preview implementation:

1. Immediately before constructing and running the sampler callback, acquire `_preview_lock`, set `_preview_sampler_active` to `True`, and reset `_last_preview_step_sent` to `-1`.
2. Preserve the existing Detail Daemon model-wrapping and sampling logic without alteration.
3. Run callback construction, optional Detail Daemon setup, sampling, and output assignment inside a `try` block.
4. In a `finally` block, acquire `_preview_lock` and set `_preview_sampler_active` to `False`, ensuring cleanup also occurs when sampling fails.

Only `src/BuiltinExtensions/ComfyUIBackend/ExtraNodes/SwarmComfyCommon/SwarmKSampler.py` will be changed.

## Error Handling

The `finally` block guarantees that a failed generation cannot leave preview delivery marked active. Existing sampling exceptions continue to propagate unchanged.

## Verification

Repository policy prohibits agents from running builds or tests. Verification is limited to:

- Python syntax parsing without executing project code.
- Reviewing the focused diff against the known-good lifecycle implementation.
- Confirming that every asynchronous preview acceptance check is reachable while the sampler is active and that deactivation occurs on all exits.

The developer must restart the ComfyUI backend and manually generate an image to verify live previews in the running application.
