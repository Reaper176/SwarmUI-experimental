# Transactional Server-Settings Persistence Design

**Date:** 2026-07-22

**Status:** Approved design; implementation pending

## Goal

Make runtime server-setting changes one observable transaction so API responses, the live `Program.ServerSettings` tree, the saved settings FDS, and the settings editor agree after both success and failure. Migrate every maintained settings-file caller away from the current silent-save contract while preserving the public compatibility surface and existing settings schema.

This project addresses rank 4, Core F6, from the maintainability architecture refresh.

## Confirmed Boundary

`AdminAPI.ChangeServerSettings` currently applies accepted fields directly to `Program.ServerSettings`, logs them, calls the void `Program.SaveSettingsFile`, and only afterward validates model directories. On path failure it restores only `Paths`, re-saves, and returns an error. Accepted non-path fields from the same rejected request therefore remain live and durable. The maintained settings editor clears/reloads altered values only after a success callback, so that partial commit also disagrees with the browser's pending state.

`Program.SaveSettingsFile` catches and logs settings-file or `always_pull` marker failures, returns no outcome, and treats `LockSettings` as a silent no-op. A caller can therefore report success after the settings FDS was not written, and `ChangeServerSettings` can mutate live settings while locked.

The exact maintained inventory contains eleven `SaveSettingsFile` invocations plus the method definition:

1. normalized-settings re-save during startup;
2. installation completion;
3. the normal `ChangeServerSettings` save;
4. its path-failure compensating save;
5. known-extension installation preparation;
6. extension enable/disable;
7. disabled-extension uninstall preparation;
8. IOPaint settings save;
9. IOPaint install completion;
10. IOPaint uninstall; and
11. IOPaint new-install-path selection.

The current `ChangeServerSettings` implementation accounts for two of those eleven invocations. Its candidate transaction replaces both with one commit operation.

Backend catalog persistence, per-user settings, roles, model metadata, and extension-owned stores have separate owners and are outside this project.

## Chosen Architecture

### Observable persistence owner

`Program` remains the settings-persistence owner. Add a small explicit result with three outcomes:

- `Saved`: the authoritative settings FDS was written;
- `Locked`: persistence is disabled by `LockSettings`; and
- `Failed`: the authoritative settings FDS write failed.

A new result-returning persistence method owns the write and centralized exception logging. The existing public `void SaveSettingsFile()` remains as a compatibility facade for external extensions and delegates to the observable owner. Every maintained invocation migrates to the result-returning contract.

One settings-transaction lock serializes maintained runtime mutations and settings-file writes. Candidate construction, validation, persistence, and publication for `ChangeServerSettings` occur under that owner so another maintained save cannot interleave between the durable write and live publication.

### Candidate settings transaction

`ChangeServerSettings` creates a separate `Settings` candidate from a complete FDS snapshot of the live configuration. It does not replace the public `Program.ServerSettings` singleton.

Submitted fields are reflected and converted against the candidate. Unknown fields, hidden fields, unchanged secret placeholders, and values that cannot be converted retain their current skip-and-log behavior. All accepted values form one transaction. The authorization-enablement lockout guard examines the complete proposed request before any candidate can commit, so earlier accepted values cannot leak into live state when that guard rejects the request.

Candidate model paths are validated before persistence. The existing trigger remains any submitted `paths.*` or `performance.allowgpuspecific*` property. The same model-folder fields, semicolon splitting, absolute/root combination, and directory-creation behavior remain. Missing directories may be created during validation; they are not deleted if a later persistence step fails because they may have existed already or acquired user content concurrently.

After validation, the candidate settings FDS is persisted. Only `Saved` permits publication into the existing singleton. Publication loads the complete candidate into that owner so readers transition from the previous committed state to the new committed state without observing per-field tentative mutation. `Locked`, `Failed`, path rejection, and authorization rejection discard the candidate and leave live settings unchanged.

## Data and Control Flow

### ChangeServerSettings success

1. Resolve the submitted `settings` object under the existing route and permission contract.
2. Acquire the settings transaction lock.
3. Snapshot the current complete settings FDS and load a candidate `Settings` tree.
4. Reflect, convert, filter, and apply accepted fields to the candidate while recording accepted keys.
5. Run the authorization lockout guard against the complete candidate/request.
6. If the established trigger is present, validate/create the candidate's model directories.
7. Persist the candidate through the observable owner.
8. On `Saved`, publish the candidate into the existing `Program.ServerSettings` singleton and release the lock.
9. Log only committed field names.
10. For path-triggered changes, run the existing model-list rebuild, refresh, and `ModelPathsChangedEvent` sequence as one ordered post-commit action group.
11. Run the existing `Program.ReapplySettings` call as a second post-commit action.
12. Return the existing success object when both actions succeed, or the same success plus a fixed warning when either action fails.

The route does not call the legacy facade and no longer needs the path-only compensating save.

### ChangeServerSettings failure

- `Locked` returns a fixed settings-locked API error before mutation.
- Authorization lockout retains its existing client message and leaves all submitted fields uncommitted.
- Invalid candidate paths retain the existing `Model paths settings are invalid, rejected change.` response and leave all accepted fields uncommitted.
- `Failed` returns a fixed server-settings persistence error. The settings editor's normal error handling retains its altered values because its success callback is not invoked.
- Unsupported submitted fields remain logged/skipped and do not independently reject otherwise accepted fields.

Save exceptions remain server-logged through the persistence owner and are not reproduced in client responses.

### Post-commit runtime effects

Model rebuild/refresh/events and `ReapplySettings` retain their current relative order after a successful durable commit. They are not moved inside the settings-file transaction and are not used to roll back an already durable settings change. Core F7 separately owns model-catalog coordination during path rebuild.

The path-triggered model sequence is one error boundary because its operations are dependent. `ReapplySettings` is attempted afterward under its own boundary even if the model sequence failed. Each failure is logged with server-side detail. If either boundary fails, the route returns:

```json
{
  "success": true,
  "warning": "Settings were saved, but one or more runtime refresh actions failed. A restart may be required."
}
```

This response deliberately reports the authoritative transaction as committed so the settings editor runs its normal success reload. It does not expose exception detail or claim that the runtime refresh succeeded.

## Authoritative FDS and Derived Launcher Marker

The settings FDS is the authoritative persistence result. The observable status reports whether that file write succeeded.

`src/bin/always_pull` remains a derived launcher marker based on `Maintenance.AutoPullDevUpdates`. After an authoritative save succeeds, the owner attempts the same create/delete synchronization. A marker-specific failure is logged separately and does not falsely report that the settings FDS was not saved. A later successful save may repair the derived marker.

This project does not redesign the FDS writer, introduce a new settings-file format, or claim atomic operating-system replacement semantics beyond the established `FDSUtility.SaveToFile` completion contract.

## Maintained Caller Migration

### Startup normalization

The startup re-save observes the result. A failure is visible in logs but does not discard the settings already loaded for the running process. The established `LockSettings` branch continues to skip normalization intentionally.

### Installation completion

Installation settings are not reported as durably completed when persistence returns `Locked` or `Failed`. The completion path surfaces a readable failure through its existing installation error handling. Already-completed installer filesystem/backend work is not destructively undone.

### Extension operations

Known-extension install preparation, extension enable/disable, and disabled-extension uninstall preparation check the locked state before mutation. They snapshot the complete `DisabledExtensions` list, perform the established list mutation, and use the observable save while holding the transaction owner. On failure they restore the snapshot and return an API error.

When persistence is a prerequisite to clone/delete/restart-visible behavior, the external operation does not continue after failed persistence. The extension discovery model, folder names, enable/disable semantics, Git operations, recycle/delete behavior, and restart requirements remain unchanged.

### IOPaint operations

IOPaint settings save, install completion, uninstall, and new-install-path selection retain their existing early locked errors. Each snapshots the complete IOPaint settings section before its final mutation/save. On `Failed`, it restores that section and returns an API error rather than reporting a durable settings success.

External work that safely cannot be reversed—such as a created virtual environment or a removed managed environment—remains on disk if persistence later fails. The response reports the settings failure, and no destructive compensating filesystem action is added.

## Concurrency and Publication

All maintained settings mutations covered by this project acquire the same transaction owner around snapshot/mutation/save/rollback or candidate commit. This prevents two maintained API writes from acknowledging conflicting snapshots or publishing a candidate after another settings save has overwritten it.

Ordinary readers retain their existing lock-free access to `Program.ServerSettings`. `ChangeServerSettings` keeps tentative values isolated in its candidate, so those readers see the prior committed tree until publication. Other migrated routes retain their narrow live mutation plus complete affected-section rollback model; their mutation/save window is serialized against maintained writers.

The project does not add a repository-wide read lock, replace public settings fields, or promise coordination with external extensions that mutate public settings without using the maintained persistence owner.

## API and Compatibility Requirements

- Preserve `ChangeServerSettings` route name, request object, permissions, and successful `{ "success": true }` shape; add only the fixed optional `warning` property after a committed save whose runtime refresh fails.
- Preserve the authorization lockout and invalid-model-path client messages.
- Add fixed error responses for locked or failed persistence without exposing exception details.
- Preserve unknown/hidden/type-invalid skip-and-log behavior and unchanged secret placeholders.
- Preserve `Settings` field names/types, reflection metadata, defaults, FDS layout/path, and legacy load migrations.
- Preserve the `Program.ServerSettings` object identity.
- Preserve the public `void SaveSettingsFile()` compatibility facade while migrating all maintained invocations.
- Preserve model-path trigger fields, directory interpretation, model rebuild/refresh/event order, and runtime reapplication order.
- Preserve extension route names, external operation ordering after successful prerequisites, and restart semantics.
- Preserve IOPaint route names, status shapes on success, managed-path rules, and external environment ownership.
- Preserve `LockSettings` startup intent while making user-facing mutation attempts explicit no-mutation failures.
- Preserve `Maintenance.AutoPullDevUpdates` and launcher marker semantics, with marker failures logged separately from authoritative FDS status.
- Do not require a frontend JavaScript or payload-schema migration.

## Files and Ownership

Expected production owners are:

- `src/Core/Program.cs`: observable result, transaction lock/owner, compatibility facade, authoritative save and marker synchronization;
- `src/WebAPI/AdminAPI.cs`: candidate server-settings transaction and extension caller migration;
- `src/Core/Installation.cs`: installation persistence outcome;
- `src/WebAPI/BackendAPI.cs`: IOPaint caller migration; and
- `src/Core/Settings.cs` only if the result/candidate helper has a settings-domain reason to live beside `Settings` rather than `Program`.

`src/wwwroot/js/genpage/helpers/settings_editor.js` is a maintained consumer used for static and manual validation but is not expected to change. No launcher, settings FDS, user data, backend catalog, user settings, extension source, model owner, generated file, or API documentation file is included.

## Static Verification

Repository policy prohibits agents from running builds, automated tests, browsers, servers, backends, installers, launchers, or live settings mutations. Static verification will:

1. inventory the eleven maintained invocations and the compatibility-facade definition;
2. prove every maintained invocation observes the new result or delegates through an explicitly justified compatibility path;
3. trace `ChangeServerSettings` from candidate snapshot through filtering, lockout, path validation, persistence, publication, post-commit model effects, `ReapplySettings`, and response;
4. prove no failure branch publishes any candidate field or logs fields as committed;
5. prove locked routes mutate neither candidate-owned public state nor live sections;
6. prove extension and IOPaint failure paths restore their complete affected settings snapshot;
7. prove only a successful authoritative FDS write yields `Saved` and only `Saved` permits candidate publication/success responses;
8. prove marker failures are distinguished from authoritative FDS failures while preserving marker synchronization attempts;
9. prove the public singleton, compatibility facade, FDS schema, reflection metadata, secrets, route names, editor callback, and post-commit ordering remain compatible, and that only committed runtime-refresh failures add the fixed warning;
10. prove Core F7 model-catalog coordination, backend persistence, user settings, and external filesystem compensation remain outside scope; and
11. run repository-permitted exact-scope, committed-range, whitespace, and static-search checks without inspecting excluded backup or user-data paths.

## Maintainer Validation

Maintainer Reaper176 should use the normal build/launch workflow and validate:

1. valid single and mixed server-setting edits;
2. a mixed non-path plus invalid-path request, confirming nothing from the request remains in memory, editor reload, or restart state;
3. unknown, hidden, unchanged secret-placeholder, and type-invalid fields mixed with valid fields, confirming established filtering plus atomic accepted-field commit;
4. authorization enablement without a password, confirming no earlier field commits;
5. `--lock_settings` across `ChangeServerSettings`, extension operations, and all IOPaint mutation routes;
6. temporarily unwritable/full settings storage followed by recovery, comparing API response, live values, editor pending state, file contents, and restart state;
7. extension enable, disable, known install preparation, and disabled uninstall under persistence failure;
8. IOPaint settings, install completion, uninstall, and new-install-path mutation under persistence failure, separately noting non-reversible environment work;
9. startup normalization and installation-completion persistence failure behavior;
10. concurrent settings mutations, confirming serialized commits and final memory/disk agreement;
11. successful model-path edits, model rebuild/refresh/event behavior, and runtime log-setting reapplication;
12. injected model-refresh/event and `ReapplySettings` failures after a durable commit, confirming a success response with the fixed warning, committed editor reload, server detail, and no exception detail in the response; and
13. an `always_pull` marker synchronization failure separately from authoritative settings-file persistence.

No benchmark or performance measurement is part of this validation.

## Non-Goals

- No transactional rewrite of user settings, roles, backend catalogs, model metadata, or extension-owned stores.
- No Core F7 model-catalog locking/refactor in this project.
- No settings editor redesign, optimistic UI, or detailed exception transport; the only response extension is the fixed optional post-commit warning.
- No FDS schema, settings filename/path, legacy migration, or general filesystem atomic-write redesign.
- No deletion of directories created during candidate path validation.
- No destructive rollback of installer, Git, extension-folder, or IOPaint environment work already completed.
- No public settings-tree encapsulation or repository-wide reader lock.
- No requirement that external extensions migrate from the retained compatibility facade.
- No performance optimization or benchmark claim.

## Success Criteria

- `ChangeServerSettings` returns success only when every accepted field is durably saved and published as one candidate.
- Rejected/locked/failed requests leave the live settings tree and authoritative FDS unchanged.
- The browser's pending/reloaded state agrees with the API outcome and committed server state.
- A post-commit runtime-action failure cannot misreport the durable transaction as rejected; it returns success with the fixed warning and remains detailed only in server logs.
- Every maintained settings-file invocation observes persistence success, lock, or failure and does not falsely report durable completion.
- Extension and IOPaint settings sections are restored after save failure; unsafe external compensation is not attempted.
- Valid settings behavior, FDS compatibility, singleton identity, runtime side-effect ordering, secrets, and API success contracts remain unchanged.
- Rank 4 remains independent of Core F7 and later persistence projects.

## Risks and Rollback

Primary risks are candidate publication semantics in the reflective settings tree, interleaving maintained mutations, accidentally treating skipped fields as request-wide errors, overclaiming rollback for external work, or reporting a derived marker failure as an authoritative FDS failure.

The complete candidate, one transaction owner, explicit result, full affected-section snapshots, fixed client errors, retained compatibility facade, and separated marker status contain those risks.

Rollback can return `ChangeServerSettings` to live mutation and route maintained callers through the compatibility facade without changing the FDS schema. The observable persistence owner should be retained if any migrated caller depends on it; rollback must not restore false durable-success responses or delete directories/external work as compensation.
