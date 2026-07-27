# Atomic Process Shutdown Gate Design

**Status:** Implemented and maintainer-validated on Garuda Linux (Arch-based), Btrfs

**Date:** 2026-07-27

**Roadmap scope:** Core F10, rank 13

## Summary

`Program.Shutdown(int code = 0)` is the composition root for process shutdown. At the approved design base, it checked a private volatile Boolean and assigned it in a separate operation. Two thread-capable callers could both observe `false` before either assignment and then overlap the shutdown webhook, pre-shutdown event, global cancellation, owner disposal, metadata shutdown, temporary-directory deletion, and final log flush.

The approved rank-13 change replaces only that non-atomic check/set with an `Interlocked.CompareExchange` one-way gate. The caller that changes the gate from zero to one owns the complete existing shutdown body and its requested exit-code behavior. Every later caller returns immediately. No public API, caller, shutdown step, ordering rule, wait, exception boundary, or child-owner lifecycle changes.

## Goals

1. Permit exactly one caller to enter the process-wide shutdown body.
2. Make gate acquisition atomic across framework, main-thread, background, restart, CI, control-timeout, and admin entry points.
3. Give the gate winner exclusive ownership of its requested exit-code behavior.
4. Preserve the existing admin shutdown permission, early-success response, and half-second delay.
5. Preserve the complete shutdown body, ordering, bounded webhook wait, cancellation timing, extension isolation, and final log flush.
6. Keep the production change confined to `src/Core/Program.cs`.

## Non-Goals

Rank 13 does not:

- redesign shutdown as asynchronous work;
- expose or return a shutdown completion task;
- make losing callers wait for the winner;
- let a later restart or other nonzero request upgrade the winning exit code;
- add exception isolation, retry, rollback, or repair to the shutdown body;
- change the shutdown webhook timeout or failure behavior;
- change `AdminAPI.ShutdownServer`, `RequestRestart`, or any caller signature;
- change the admin permission, response body, delay, update/restart flow, or restart marker;
- reorder `PreShutdownEvent`, global cancellation, webserver, backend, session, proxy, model, extension, metadata, temp, or log operations;
- change child-owner idempotence or lifecycle logic;
- address Backend F18/rank 14's backend-shutdown task ownership;
- change launcher restart-code interpretation;
- add instrumentation, performance measurement, or a performance claim; or
- edit frontend, settings, launchers, extensions, upstream code, generated files, user data, or protected maintainer work.

## Existing Boundary and Caller Inventory

### Guard and shutdown body

At the approved design base, `Program.Shutdown` performed:

1. a separate `HasShutdown` read and write;
2. shutdown-webhook dispatch and a wait bounded at two minutes;
3. nonzero exit-code logging and `Environment.ExitCode` assignment;
4. `PreShutdownEvent`;
5. `GlobalCancelSource.Cancel`;
6. webserver stop;
7. backend shutdown;
8. session shutdown;
9. proxy stop;
10. every model-handler shutdown;
11. extension shutdown and extension-list clear;
12. output-metadata tracker shutdown;
13. best-effort temporary-directory deletion;
14. final completion logs and log-save-thread flush.

The body is synchronous. Exceptions outside the existing temporary-directory catch retain their current propagation and partial-shutdown behavior.

### Direct shutdown callers

The maintained direct `Shutdown` entry points are:

- `AssemblyLoadContext.Default.Unloading`;
- `AppDomain.CurrentDomain.ProcessExit`;
- ASP.NET `ApplicationStopping`;
- the main thread after `WebApp.WaitForShutdown`;
- the three-second CI task;
- the remote-control timeout callback; and
- `AdminAPI.ShutdownServer` after its half-second delay.

### Restart callers

`Program.RequestRestart` schedules `Shutdown(42)` through `Utilities.RunCheckedTask`. Maintained restart requests originate from:

- maintenance auto-restart after its idle/time-window checks;
- update-and-restart after update work and rebuild-marker creation;
- Windows .NET update installation;
- NVIDIA critical-error detection; and
- Comfy critical GPU-error detection.

All callers retain their current timing, logging, permissions, and response behavior.

## Chosen Semantics

### Atomic winner

Replace the private Boolean with an integer gate documented as zero before shutdown and one after a caller acquires ownership:

```csharp
    /// <summary>Zero before process shutdown begins; atomically changed to one by the single shutdown owner.</summary>
    private static int HasShutdown;
```

The first operation in `Shutdown` becomes:

```csharp
        if (Interlocked.CompareExchange(ref HasShutdown, 1, 0) != 0)
        {
            return;
        }
```

The compare-exchange and ownership decision are one atomic operation. A losing caller does not dispatch the webhook, change an exit code, invoke events, cancel work, stop or dispose an owner, delete files, or emit shutdown-body logs.

### First-caller exit-code ownership

The maintainer selected first-caller-wins semantics:

- a winning `Shutdown(42)` retains the existing restart log and sets `Environment.ExitCode` to `42`;
- a winning `Shutdown(0)` retains the existing behavior of not assigning `Environment.ExitCode`, including preserving a prior CI error code;
- a later `Shutdown(42)` cannot upgrade a normal shutdown winner;
- a later `Shutdown(0)` cannot replace a restart winner; and
- no losing caller mutates process state.

This rule is deliberately narrower than a nonzero-wins arbiter. It requires no second state variable, exit-code compare/exchange, completion task, or timing window.

## Data Flow and Ordering

Every caller enters the unchanged public `Shutdown(int code = 0)` method. The atomic gate selects one owner before any fallible or externally visible shutdown work. The owner then executes the existing body in the existing order.

`AdminAPI.ShutdownServer` continues to log the requesting user, schedule its half-second-delayed call, and immediately return `{"success": true}`. If another trigger wins before the delay ends, the admin task reaches `Shutdown`, loses the gate, and returns without duplicating work. The API contract does not report which trigger won.

`RequestRestart` remains nonblocking and continues to schedule `Shutdown(42)`. If it wins, launcher-visible restart behavior remains. If an ordinary shutdown already won, the later restart request is ignored under the approved first-caller rule.

ASP.NET stopping, assembly unloading, process exit, main-thread completion, CI, and control-timeout callbacks may still arrive in any order. Only their gate winner performs composition-root shutdown.

## Error and Recovery Boundary

Gate acquisition itself has no new failure path. The gate is not reset if the winning shutdown body throws or stops partway through. A losing caller cannot retry or repair a partial shutdown.

This is intentional: resetting the gate would allow a second caller to overlap or repeat owners after an unknown partial shutdown. Rank 13 establishes one composition-root owner; it does not make the existing body transactional or exception-isolated.

Existing behavior remains for:

- shutdown webhook faults and its two-minute bound;
- `PreShutdownEvent` exceptions;
- stop/shutdown/disposal exceptions;
- extension isolation provided by `RunOnAllExtensions`;
- best-effort temp-directory deletion; and
- log-thread waiting and final flush.

## Compatibility

The production change preserves:

- `public static void Shutdown(int code = 0)`;
- `public static void RequestRestart()`;
- every direct and indirect caller;
- admin permissions, logs, response, and delay;
- restart code `42`;
- pre-existing `Environment.ExitCode` when the winner supplies zero;
- webhook dispatch and bounded wait;
- event and global-cancellation ordering;
- shutdown owner order;
- extension shutdown isolation;
- temp cleanup behavior;
- final log flush; and
- launcher and external extension source/binary surfaces.

`HasShutdown` is private, so changing its storage type does not alter the public extension ABI.

## Alternatives Considered

### Lock around the existing Boolean

A dedicated lock could serialize the check and assignment, but it adds another field and synchronization object for a one-way transition. No caller needs to wait for state protected by a lock.

### `Interlocked.Exchange`

An atomic exchange would also select one winner. `CompareExchange` expresses the permitted transition explicitly—only zero changes to one—and leaves losing invocations without even a same-value write.

### Shared shutdown task or state machine

A task/state-machine owner could let later callers await completion or arbitrate exit codes, but that changes public behavior, failure propagation, and timing. Rank 13 has no evidence requiring that redesign.

### Later nonzero or restart-code override

Allowing a later nonzero request to replace the winner's code needs a second coordination rule and creates timing-dependent exit behavior. The maintainer rejected it in favor of first-caller ownership.

## Production Scope

Expected production modification:

- `src/Core/Program.cs`
  - replace the private volatile Boolean guard with one documented integer gate; and
  - replace only the separate check/set with the atomic compare-exchange.

Expected unchanged maintained consumers:

- `src/Core/WebServer.cs`;
- `src/WebAPI/AdminAPI.cs`;
- `src/Utils/Utilities.cs`;
- `src/Utils/NvidiaUtil.cs`; and
- `src/BuiltinExtensions/ComfyUIBackend/ComfyUIAPIAbstractBackend.cs`.

No other source or documentation file belongs in the production commit.

## Static Verification

Agents must not build, launch, or test SwarmUI. Static verification must:

1. enumerate every direct `Shutdown` and indirect `RequestRestart` caller;
2. confirm the gate is the first operation in `Shutdown`;
3. prove only the zero-to-one compare-exchange winner reaches the existing body;
4. prove losing callers return before webhook dispatch and exit-code handling;
5. confirm code `42` remains literal at the existing restart owner;
6. confirm a winning zero retains the existing no-assignment behavior;
7. compare the complete post-gate body with the pinned baseline and prove it is unchanged;
8. confirm `Shutdown` and `RequestRestart` public signatures are unchanged;
9. confirm no maintained caller changes;
10. confirm production changes only `src/Core/Program.cs`;
11. inspect the exact commit and fixed-range diff; and
12. run `git diff --check`.

Static evidence can establish atomic ownership, branch placement, source scope, signatures, call sites, literal exit codes, and unchanged body text. It cannot prove runtime trigger timing, framework callback behavior, process exit status, owner idempotence, platform behavior, or exactly-once side effects.

## Implementation Record

### Integrated and production boundaries

The approved design base is `3d244ede73a3cc36343624bdd00329a8a5284e5b`. The integrated design-to-source history `3d244ede73a3cc36343624bdd00329a8a5284e5b..f389da0d13be6bbf4cc81f6c9a24c21a095cdaca` contains the documentation-only implementation-plan commit `8083c644a37ff614099f5f105e87af091cae0615` and production commit `f389da0d13be6bbf4cc81f6c9a24c21a095cdaca`. These boundaries are intentionally distinct: path-filtering that history to production source returns only `f389da0d13be6bbf4cc81f6c9a24c21a095cdaca`.

The production commit changes exactly `src/Core/Program.cs`, with `3 insertions(+), 3 deletions(-)`. It replaces the private volatile Boolean with the XML-documented private static integer specified above and replaces the separate check/set with `Interlocked.CompareExchange(ref HasShutdown, 1, 0)` as the first `Shutdown` operation. The zero-to-one winner continues into the existing body; every loser returns immediately. The complete post-gate body is unchanged.

### Implemented behavior and compatibility

First-caller exit-code ownership matches the approved semantics. A winning `Shutdown(42)` retains the existing assignment of `42`; a winning `Shutdown(0)` makes no assignment and therefore preserves any prior CI exit code. Losing callers cannot alter the exit code or any other shutdown state.

The direct and restart caller inventories, `Shutdown` and `RequestRestart` signatures, admin permission/early-success response/half-second delay, webhook behavior and bound, body order, owner calls, extension isolation, temporary-directory handling, final log flush, and existing failure behavior are unchanged. `HasShutdown` remains private, so its storage change does not alter the public source or binary extension ABI. The gate is deliberately not reset if the winning body fails; later callers cannot retry or repair a partial shutdown.

### Static review and runtime boundary

Static review used the fixed boundaries above:

- `git log --format='%H %s' 3d244ede73a3cc36343624bdd00329a8a5284e5b..f389da0d13be6bbf4cc81f6c9a24c21a095cdaca` returned the plan and production commits, while the same history filtered to `src/Core/Program.cs` returned only the production commit;
- `git show --numstat f389da0d13be6bbf4cc81f6c9a24c21a095cdaca -- src/Core/Program.cs` returned `3` insertions and `3` deletions in that one source file;
- fixed-base source diff and caller searches confirmed gate placement, unchanged post-gate body, unchanged direct/restart inventory and signatures, literal code `42`, winning-zero no-assignment behavior, and unchanged admin and downstream contracts; and
- `git diff --check 3d244ede73a3cc36343624bdd00329a8a5284e5b..f389da0d13be6bbf4cc81f6c9a24c21a095cdaca` reported no whitespace errors.

Independent source reviews returned `SOURCE_SPEC_APPROVED` and `SOURCE_QUALITY_APPROVED`, both with no issues. This is agent static evidence only: agents performed no build, test, launch, runtime, platform, or performance exercise and make no runtime, platform, or performance claim. The exact 16-case maintainer matrix follows; its separate live-validation record appears after the matrix.

## Maintainer Validation Matrix

The maintainer performs all builds and live validation. Record operating system/filesystem and exact outcomes.

1. Ordinary Web stop completes the existing shutdown sequence once.
2. A direct ordinary `Shutdown(0)` completes the sequence once.
3. `AdminAPI.ShutdownServer` retains its permission requirement, immediate success response, and half-second-delayed call.
4. An uncontested restart request produces exit code `42` and retains launcher restart behavior.
5. A winning ordinary shutdown ignores a later restart request and does not upgrade to `42`.
6. A winning restart ignores a later ordinary shutdown and retains code `42`.
7. A winning `Shutdown(0)` preserves a pre-existing CI error exit code rather than overwriting it.
8. Web stop and admin shutdown collision produces one webhook, event, cancellation/disposal, cleanup, and log-flush sequence.
9. Web stop and restart collision follows the approved first-caller exit-code rule and produces one sequence.
10. Admin shutdown and restart collision in both winner orders follows the approved response, delay, code, and one-sequence rules.
11. Control-timeout and restart collision follows the approved first-caller rule and produces one sequence.
12. Assembly-unload or process-exit callbacks arriving after shutdown begins return without duplicating work.
13. The shutdown webhook still runs before exit-code assignment, logs, events, cancellation, and disposal, with the same two-minute bound.
14. `PreShutdownEvent`, global cancellation, webserver, backends, sessions, proxy, model handlers, extensions, metadata, temp cleanup, and log flush retain their existing order.
15. Extension shutdown remains isolated per extension.
16. A shutdown-body failure does not reopen the gate or allow a later caller to retry the partial sequence.

Validation distinguishes observed behavior from static evidence and makes no performance claim.

## Maintainer Validation Record

Maintainer Reaper176 explicitly confirmed that all 16 exact cases above passed on 2026-07-27 on Garuda Linux (Arch-based), Btrfs. That confirmation is maintainer runtime evidence, separate from the agent static evidence recorded above. Windows, other Linux distributions/filesystems, and performance remain unvalidated or unmeasured, and no such claim is made.

## Success Criteria

Rank 13 is successful when:

- one and only one caller can own process-wide shutdown;
- losing calls have no shutdown-body or exit-code side effect;
- first-caller exit-code semantics hold;
- every existing caller and public contract remains;
- the complete shutdown body and order remain unchanged;
- the production diff contains only `src/Core/Program.cs`;
- static specification and quality review pass; and
- the maintainer confirms the validation matrix on a recorded platform.

## Rollback

Rollback is one local guard change in `Program.Shutdown`: restore the private volatile Boolean and the separate check/set. No data migration, caller rollback, API change, or child-owner rollback is involved.
