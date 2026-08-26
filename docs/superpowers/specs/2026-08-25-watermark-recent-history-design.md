# Watermark Recent History and Defaults Design

## Goal

Improve the Generate tab watermark controls by retaining the five most recently submitted custom watermark images and changing the default placement and opacity.

## User Experience

- The Advanced `Watermark` group remains toggleable and collapsed by default.
- A row of up to five clickable recent-watermark thumbnails appears directly beneath `Watermark Image`.
- The row is hidden when it has no entries.
- Clicking a thumbnail restores that image as the current `Watermark Image` value and preview.
- A custom watermark is added or promoted only when a generation is submitted while the Watermark group is enabled and the custom watermark image has a value.
- Selecting, uploading, pasting, or restoring an image without submitting a generation does not alter history.
- Reusing an existing entry promotes it to newest rather than creating a duplicate.
- Only custom watermark images are tracked. Explicit watermark masks are not tracked.

## Persistence

- Recent history is local to the browser profile and survives browser and SwarmUI restarts.
- Reusable Swarm media references such as `inputs/...` are stored as paths.
- Uploaded or pasted image data is stored in IndexedDB so full image data does not consume the small synchronous `localStorage` quota.
- Lightweight ordering and display metadata may be stored with the IndexedDB records.
- At most five unique entries are retained; inserting a sixth removes the oldest record and its stored data.
- If a stored server-path thumbnail can no longer load, its stale history entry is removed rather than repeatedly displaying a broken item.
- Storage failures must not block generation. The generation proceeds and the failure is logged to the browser console.

## Defaults

Defaults must agree across Generate parameter metadata, workflow fallback values, and the managed Comfy node:

- Alignment: `bottom-left`
- Resize percentage: `20`
- Opacity: `100`

The resize default is already 20 and remains explicit to prevent drift.

## Implementation Boundaries

- Extend the existing generic image-input rendering only through a watermark-specific hook or metadata flag; other image parameters must not gain recent-history UI or persistence.
- Keep history logic in a small frontend helper class loaded on the Generate page.
- Hook recording into the established Generate submission data path after inputs have been collected, using the same enabled-parameter result that is sent to generation.
- Reuse `setMediaFileDirect`/existing parameter utilities to restore thumbnails instead of duplicating image-input behavior.
- Do not add server APIs, write managed history files, or alter generated output history.

## Verification

Repository policy prohibits agents from running builds or tests. Static review will verify loading order, parameter targeting, submit-time gating, deduplication/capacity logic, IndexedDB error handling, and consistent defaults. The maintainer will manually verify persistence, promotion, eviction, stale-path removal, and generation behavior in the live application.
