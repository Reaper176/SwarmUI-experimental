# Autoscaling Launch-Script Platform Validation Design

**Status:** Approved; implementation not started

**Date:** 2026-07-27

**Rank:** 20 — Correct autoscaling launch-script platform validation

**Approved source/audit base:** `e65550b0b90b62504f0495723db95567fe192673`

## Summary

`AutoScalingBackend.Init` is intended to reject launch scripts whose final extension is inappropriate for the current operating-system family. The current guard does not do that: `Path.GetExtension` returns a leading dot, the comparisons omit that dot, and the error branch rejects a matching expression instead of rejecting its negation. Ordinary extension-bearing paths therefore pass regardless of platform.

Rank 20 replaces only that defective condition with one positive local appropriateness predicate using dotted lowercase extensions, then rejects the predicate's negation. Windows continues to accept `.bat` and `.ps1`; every non-Windows platform continues to accept `.sh`.

## Current Boundary

The production owner is `AutoScalingBackend.Init` in `src/Backends/AutoScalingBackend.cs`.

Its current validation and initialization order is:

1. Set `CanLoadModels` false and reset start/stop timing.
2. Disable when `MaxBackends` is non-positive or `StartScript` is blank.
3. Reject invalid numeric/settings relationships.
4. Read the final `StartScript` extension and apply the ineffective platform guard.
5. Reject a missing file.
6. Enter `LOADING`.
7. Fill to the configured minimum.
8. Register tick, pre-shutdown, and new-backend-needed hooks.
9. Enter `RUNNING`.

Only step 4 changes.

## Defect

The current code derives:

```csharp
string scriptExt = Path.GetExtension(Settings.StartScript).ToLowerInvariant();
```

For an ordinary path such as `worker.sh`, this returns `.sh`. The guard compares that result to `sh`, `bat`, and `ps1`, so no ordinary extension matches. The guard also enters the rejection branch when its expression is true, even though the expression is written as an allow-list.

These defects mask each other:

- adding dots without changing the polarity would reject appropriate scripts;
- negating the current undotted comparison would reject every ordinary extension;
- both the dotted comparisons and the polarity must be corrected together.

## Goals

- Accept `.bat` and `.ps1` launch scripts on Windows.
- Accept `.sh` launch scripts on every non-Windows platform.
- Match extensions case-insensitively.
- Reject empty, trailing-dot, and unrelated final extensions.
- Preserve the current validation order and error boundary.
- Leave all process-launch and autoscaling lifecycle behavior unchanged.

## Non-Goals

- Do not change `ProcessStartInfo`, `UseShellExecute`, arguments, environment, working directory, or output parsing.
- Do not add interpreter selection for PowerShell, batch, or shell scripts.
- Do not guarantee that an accepted file is executable or launchable.
- Do not distinguish Linux, macOS, or other non-Windows platforms.
- Do not inspect shebangs, file content, MIME type, or executable permissions.
- Do not trim, normalize, resolve, or rewrite `StartScript`.
- Do not add a helper, collection, setting, schema field, API, or public member.
- Do not refactor scaling, child ownership, timing, queue, ping, stop, or shutdown behavior.
- Do not change documentation outside the Rank 20 design/plan/closure records unless static review finds a directly contradictory current-status statement.
- Do not make a performance claim.

## Chosen Design

Keep the existing `scriptExt` local and add one local Boolean:

```csharp
bool isAppropriate = RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
    ? scriptExt == ".bat" || scriptExt == ".ps1"
    : scriptExt == ".sh";
```

The existing rejection block then becomes:

```csharp
if (!isAppropriate)
```

The block body remains textually unchanged.

This is preferred over a private helper because there is one call site and no independent reusable platform policy. It is preferred over a collection because the allow-list is fixed, tiny, and platform-binary.

## Platform Truth Table

`Path.GetExtension(...).ToLowerInvariant()` determines the final extension.

| Final extension | Windows | Non-Windows |
|---|---:|---:|
| `.bat` / mixed case | accept | reject |
| `.ps1` / mixed case | accept | reject |
| `.sh` / mixed case | reject | accept |
| empty | reject | reject |
| `.` | reject | reject |
| any other extension | reject | reject |

Only the final extension participates:

- `worker.launch.sh` is accepted on non-Windows;
- `worker.sh.txt` is rejected on all platforms;
- `worker` and `worker.` are rejected on all platforms.

## Data and Control Flow

No data model changes.

The flow remains:

```text
configured StartScript
    → current disabled/settings checks
    → final extension normalized to lowercase
    → platform-appropriate predicate
        → false: existing error log, ERRORED, return
        → true: existing File.Exists check
            → missing: existing missing-file log, ERRORED, return
            → present: existing loading/min-fill/subscription/running flow
```

An inappropriate path remains rejected before filesystem probing. A platform-appropriate missing path reaches the existing missing-file error. No process can start and no lifecycle hook can be registered from the inappropriate-extension branch.

## Error Handling

The existing platform error remains unchanged:

```text
AutoScalingBackend cannot handle start script: '<path>', not an OS-appropriate shell script. Use 'sh' for Linux/Mac, or 'bat'/'ps1' for Windows.
```

The status remains `BackendStatus.ERRORED`, followed by an immediate return.

The existing missing-file error remains unchanged and still occurs only after platform acceptance. Exceptions or failures after initialization reaches process launch retain their current handling. Acceptance by this guard means only that the final extension matches the documented platform family; it does not prove interpreter availability, file permissions, or launch success.

## Compatibility Contract

Rank 20 preserves:

- the `autoscalingbackend` backend type ID and display metadata;
- `AutoScalingBackendSettings`, every setting name/type/default/comment, and FDS/API serialization;
- the public `StartScript` field;
- every public field, property, method, and signature;
- disabled and invalid-settings behavior;
- the validation order around the platform and file-existence checks;
- the existing platform-error and missing-file log text;
- `DISABLED`, `ERRORED`, `LOADING`, and `RUNNING` status transitions;
- `FillToMin`, minimum/maximum counts, queue thresholds, start/stop delays, and failure delays;
- `ProcessStartInfo`, arguments, standard-stream handling, working directory, and managed output protocol;
- controlled non-real backend creation, configuration, ping, idle stop, and deletion;
- tick, pre-shutdown, and new-backend-needed subscriptions and removals;
- shutdown cleanup and all caller behavior; and
- extension source and binary compatibility.

## Static Verification

Agents perform static verification only. They do not build, test, launch SwarmUI, execute scripts, start backends, automate a browser, call live APIs, or perform platform/filesystem/performance validation.

Static review must:

1. pin the source/audit base and protected working state;
2. inventory every `StartScript`, platform, extension, `Init`, and `LaunchOne` owner/caller;
3. compare the exact `Init` method before and after;
4. evaluate the complete platform truth table;
5. confirm dotted lowercase comparisons and negated rejection;
6. prove disabled/settings checks remain before the guard;
7. prove file existence remains after the guard;
8. prove the platform error text, status, and return are unchanged;
9. prove no process launch or hook registration moved before validation;
10. prove all code after the guard is unchanged;
11. inspect public/member and settings-schema diffs;
12. confirm the source projection is only `src/Backends/AutoScalingBackend.cs`;
13. run fixed-range whitespace checks; and
14. confirm the index and protected maintainer work remain isolated.

## Maintainer Validation Matrix

The maintainer performs runtime validation and records the date, operating system, filesystem, and shell/runtime details. Windows runtime behavior is recorded only if a Windows environment is supplied; otherwise it remains explicitly static-only and unvalidated.

1. `MaxBackends <= 0` or a blank `StartScript` still produces `DISABLED` without platform/file/process work.
2. Invalid numeric/settings relationships still produce the existing settings error and `ERRORED` before platform/file/process work.
3. On non-Windows, an existing lowercase `.sh` script passes the platform guard.
4. On non-Windows, an existing mixed-case `.SH` script passes the platform guard.
5. On non-Windows, an existing `.bat` path is rejected with the unchanged platform error and `ERRORED`.
6. On non-Windows, an existing `.ps1` path is rejected with the unchanged platform error and `ERRORED`.
7. On non-Windows, a no-extension path is rejected with the unchanged platform error and `ERRORED`.
8. On non-Windows, a trailing-dot path is rejected with the unchanged platform error and `ERRORED`.
9. On non-Windows, an unrelated extension is rejected with the unchanged platform error and `ERRORED`.
10. Final-extension handling accepts an existing multi-dot `worker.launch.sh` path and rejects an existing `worker.sh.txt` path.
11. A missing but platform-appropriate `.sh` path passes the platform guard, then produces the unchanged missing-file error and `ERRORED`.
12. Every inappropriate-extension rejection occurs before process launch and before tick, pre-shutdown, or new-backend-needed hook registration.
13. A minimal valid `.sh` script with `MinBackends = 0` initializes through the unchanged loading/subscription flow and reaches `RUNNING` without launching a child.
14. A minimal valid `.sh` script with a positive minimum exercises minimum fill, emits the existing managed `NewURL` protocol, and creates the expected controlled non-real child.
15. Queue-triggered expansion retains the existing minimum-queue, maximum-count, pending-launch, and start-delay gates.
16. A script-declared or process launch failure retains the existing warning/failure-delay/pending-launch cleanup behavior.
17. Idle monitoring retains minimum-count protection, idle-time selection, stop spacing, remote shutdown, and local deletion.
18. Autoscaler shutdown removes its hooks, stops/removes controlled children, clears tracking, and reaches `DISABLED` as before.

If Windows runtime is available, additionally confirm:

19. Lowercase and mixed-case `.bat` and `.ps1` paths pass the Windows platform guard.
20. `.sh`, empty, trailing-dot, and unrelated extensions are rejected by the Windows platform guard before process launch or hook registration.

## Success Criteria

Rank 20 succeeds when:

- the source diff is limited to the positive dotted-extension predicate and its negated rejection;
- every truth-table row has the intended result;
- inappropriate scripts cannot progress to file probing, process launch, or hook registration;
- appropriate missing scripts retain the existing missing-file boundary;
- all settings, logs, statuses, launch arguments, lifecycle behavior, and public contracts remain unchanged;
- independent static source specification and quality reviews approve the exact source projection;
- the unchanged maintainer matrix is handed to Reaper176; and
- validation evidence records only the platforms actually exercised.

## Rollback

Rollback is the single predicate change in `AutoScalingBackend.Init`. No schema, migration, helper, or cleanup step is required.
