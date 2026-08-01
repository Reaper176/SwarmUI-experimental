# Firefox History-Tab Refresh Coalescing Design

## Problem

Rank 31 validation passes all 16 assertions in Chromium but fails the
History-tab update-count assertion in Firefox 153. When the History tab becomes
visible, `ImageHistoryController.handleHistoryTabShown()` immediately queues a
visible-window update through `ImageHistoryWindowManager.attach()`. Firefox
then emits a settling `scroll` event after that first animation-frame update,
which queues a second update. Chromium coalesces the equivalent lifecycle into
one update.

Temporary harness diagnostics confirmed the sequence: the first invocation
originates from the tab-show attachment, a `scroll` event occurs after it, and
the second invocation originates from the manager's existing scroll listener.
The failure is therefore a visibility-layout scheduling race, not an image
mapping, hydration, or browser-data defect.

## Scope

Change only the History-tab visibility scheduling in
`src/wwwroot/js/genpage/gentab/outputhistory.js`. Preserve normal scroll,
resize, browser-build, image hydration/dehydration, lazy loading, and initial
navigation behavior. Do not add browser detection, timing constants,
instrumentation, or unrelated refactoring.

## Design

Extend `ImageHistoryWindowManager.attach(content)` with an optional flag that
controls whether attachment immediately queues an update. The default remains
enabled, preserving every existing caller and the manager's current public
behavior.

`ImageHistoryController.handleHistoryTabShown()` will:

1. retain initial-load scheduling;
2. retain `browserUtil.queueMakeVisible(historyContent)`;
3. attach the window manager with immediate updating disabled; and
4. request the manager update on the next animation frame, with the existing
   timer-style fallback for environments without `requestAnimationFrame`.

The manager's scroll listener is active before the deferred callback. If
Firefox emits its settling scroll first, that event queues the update and the
deferred callback is coalesced by `updateQueued`. If the callback runs first,
the queued state coalesces a same-frame settling scroll. Chromium, which does
not emit the extra scroll in the validated path, still receives the deferred
explicit update.

## Error and Compatibility Behavior

The change introduces no new failure surface and does not suppress user scroll
events. A missing History content element retains the current no-op behavior.
The fallback continues to schedule asynchronously when animation frames are
unavailable. The optional attachment argument defaults to current behavior, so
other attachment sites require no changes.

## Validation

Use the existing disposable rank-31 Firefox harness as the test-first
regression: it must fail at `15/16` before the production change with
`tab invocations 2`, then pass `16/16` afterward. Require the same `16/16`
result in Chromium.

After the focused test passes, rerun rank 30's Firefox history suite, the
Firefox core browser suite, the 64-case static contract suite, the consolidated
43-case C# suite, and the rank 26–29 and 32 post-removal harnesses. Finally,
rerun the full ranks 1–32 accounting and update the audit without converting
dependency-bound Windows, OAuth, GPU/model, multi-backend, or external-extension
coverage into passes.
