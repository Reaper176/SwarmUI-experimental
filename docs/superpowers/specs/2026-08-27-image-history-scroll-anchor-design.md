# Image History Scroll Anchor Preservation

## Problem

The image-history browser clears and rebuilds its content for normal refreshes, the startup background fill, newly saved images, filtering, sorting, display-format changes, and other updates. The shared browser only captures `scrollTop` for a narrow rerender path, and an explicit refresh directly resets `scrollTop` to zero. As a result, History can return to the top even though the user did not scroll there.

The required behavior is stronger than retaining a numeric offset: while the same image remains available, it must remain at the same viewport position. Programmatic rebuilds, restoration, refreshes, and page reloads must never redefine the user's chosen History position.

## Design

Add a History-specific scroll-position collaborator owned by `ImageHistoryController`. It records:

- the path of the first visible history image;
- that image's pixel offset from the top of the History viewport; and
- the current numeric `scrollTop` as a fallback.

The collaborator updates this desired position only from genuine user scrolling. It persists the position in `localStorage` so the initial History load after a full page reload can restore it.

Before any History content replacement, scroll recording is suspended and the current anchor is captured if the rendered content represents the user's current position. After the replacement finishes, the collaborator finds the anchored image by its stable path and adjusts `scrollTop` until that image returns to the saved viewport offset. Restoration runs after layout has been established so asynchronous rendering does not leave the anchor displaced. Scroll events caused by clearing content or applying the restoration are ignored and cannot overwrite the saved state with zero.

The shared browser gains only the smallest opt-out needed to prevent its explicit refresh-to-top assignment. Image History enables that opt-out; other browser instances retain their current refresh behavior.

## Missing Anchors

If filtering, deletion, hiding, folder contents, or another operation makes the anchored image unavailable, History restores the saved numeric offset, clamped to the available content, instead of jumping to the top. The image anchor remains saved rather than being replaced by programmatic movement. If the image becomes available again before the user deliberately scrolls elsewhere, it is restored to its saved viewport position.

A subsequent genuine user scroll selects a new anchor and replaces the prior persisted position.

## Scope

Preservation applies to all image-history rebuild paths, including:

- initial fast and background full loads;
- explicit refresh and retry;
- saved-image insertion and follow-up refreshes;
- filtering, sorting, reverse sorting, depth, and display-format changes;
- metadata and bulk-action refreshes; and
- full page reloads in the same browser storage profile.

Folder navigation remains functional. If the saved image exists in the resulting rendered History list, it remains the anchor; otherwise the missing-anchor fallback applies. No other SwarmUI browser changes behavior.

## Static Validation

Per repository policy, the agent will not run builds or tests. Static validation will confirm that:

- every History content replacement passes through the preservation boundary;
- explicit History refresh no longer assigns a top position;
- programmatic clear and restoration scroll events cannot overwrite persisted state;
- the anchor is keyed by the stable image path rather than DOM identity;
- other `GenPageBrowserClass` instances keep existing defaults; and
- JavaScript follows repository syntax conventions.

The developer will live-check by scrolling to a recognizable image and exercising refresh, background completion, new generations, filtering and clearing the filter, sorting, format changes, tab switches, and a full page reload. The recognizable image should remain at the same viewport offset whenever it is present.
