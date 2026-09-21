# Require Enter for Generate Filters

Approved behavior: add a disabled-by-default user UI setting named Require Enter To Apply Filters. When enabled, parameter and asset/history browser text filters apply on Enter. Clear buttons and explicit model-history actions still apply immediately. Dropdowns and other tabs' searches retain their behavior.

Implementation plan:
- [x] Add the boolean to `Settings.User.UserUIData` so the existing settings editor persists it.
- [x] Add a shared text-filter event policy in `util.js`, including Enter default prevention and IME composition handling.
- [x] Keep draft browser text separate from the applied filter; preserve existing debounce for live input and submit immediately for Enter/clear.
- [x] Store the applied parameter query separately so unrelated parameter updates cannot apply draft input. Route clear and unaltered-filter actions through explicit submission.
- [x] Preserve the model menu's history-search shortcut through explicit submission.
- [x] Check JavaScript syntax and whitespace, and trace both modes, clear, composition, refresh, and shortcuts. Per repository instructions, leave builds and runtime tests to the maintainer.

Manual verification after building: enable the setting, type in parameters and each browser, confirm results stay unchanged until Enter, then clear. Repeat with empty draft text and an active filter. Change browser view/folder while a draft is pending and confirm it is not applied. Use a model's history shortcut. Disable the setting and verify live filtering resumes. Confirm the setting persists after saving and reloading.
