# Chants tab implementation plan

**Goal:** Browse and edit the selected chant JSON alongside Presets and LoRAs.

**Architecture:** Reuse GenPageBrowserClass. A dedicated ChantsAPI exposes the selected source and saves individual entries with a revision check and atomic replacement. Existing name/terms/content/color files stay compatible; description and thumbnail are optional.

**Tech stack:** C# 12, Newtonsoft JSON, Razor, classic JavaScript, existing Bootstrap modals.

- [x] Add isolated document-edit tests for preservation of unknown metadata, duplicate names, and unsafe thumbnail inputs to SwarmUITests/ChantTests.cs. The user runs NUnit under the repository testing policy.
- [x] Add src/WebAPI/ChantsAPI.cs: register GetChantList and SaveChant, authorize shared edits, validate discovered selected paths, serialize writes, reject stale revisions, preserve unrelated JSON fields, atomically replace the selected file and invalidate its cache.
- [x] Add edit_chants permission in src/Accounts/Permissions.cs and register routes in BasicAPIFeatures.cs.
- [x] Add Chants tab and editor markup in GenerateTab.cshtml and GenTabModals.cshtml.
- [x] Add class-based chants.js using the shared browser, escaped card text, optional embedded raster thumbnails, explicit positive/negative insertion actions, upload/remove thumbnail and editing controls. Load after browsers.js.
- [x] Refresh cards when user data changes, so source-setting changes and autocomplete updates stay consistent.
- [x] Check JavaScript syntax and git diff, then statically trace disabled source, malformed JSON, duplicate name, stale revision, permission denial, successful save, insertion, and upload paths. Do not build or run NUnit without a user override.

Manual verification after the user builds: select demo-chants.json, open Chants, filter by name/terms/content, insert into both prompts at the cursor, edit and reload, upload/remove a thumbnail, create a chant, verify expansion, and attempt a stale save in two sessions. Check mobile card menus and modal scrolling.

Verification: JavaScript syntax checks and isolated frontend checks passed. Static independent review findings were corrected. C# compilation, NUnit execution, and live desktop/mobile UI checks remain for the user under repository policy.
