# LoRA Browser Scroll Preservation

## Problem

Activating any LoRA from the LoRAs browser updates the selected LoRA parameters and bottom information bar. The bottom-bar update schedules a full layout recalculation. After that asynchronous recalculation, the LoRA browser returns to the top instead of retaining the user's position.

## Design

Preserve the LoRA browser's numeric scroll offset across activation:

1. Capture the browser content element's `scrollTop` immediately before invoking the LoRA selection callback.
2. Allow the existing selection, parameter synchronization, selected-card styling, and delayed layout recalculation to run unchanged.
3. Restore the captured `scrollTop` after the queued layout work.

The preservation applies only to the LoRA model browser. Checkpoint, VAE, embedding, ControlNet, wildcard, folder navigation, filtering, and explicit refresh behavior remain unchanged.

## Error Handling

If the browser content element is unavailable, selection proceeds normally without attempting restoration. Restoration must also confirm that the same content element is still connected so navigation or a rebuild is not overwritten.

## Validation

Static validation will confirm that:

- the capture occurs before the LoRA callback;
- restoration occurs after the existing delayed layout recalculation;
- other browser subtypes retain their current behavior;
- no unrelated files are changed.

Per repository policy, the developer performs the live browser check: scroll down the LoRA list, activate several LoRAs, and confirm the list stays at the same offset after each activation.
