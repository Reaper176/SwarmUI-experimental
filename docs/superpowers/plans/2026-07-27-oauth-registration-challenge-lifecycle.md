# OAuth Registration Challenge Lifecycle Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Give maintained OAuth registration challenges a 15-minute validity lifetime, a deterministic 256-entry capacity, newest-tracker-only email ownership, and one-way atomic consumption without changing public ABI, browser/API contracts, or surrounding registration behavior.

**Architecture:** Keep the public `SessionHandler.TempAuths` field exactly `ConcurrentDictionary<string, string>` and make it the compatibility email view. Add private monotonic creation metadata and a dedicated in-memory lock in `SessionHandler`; maintained helpers reconcile legacy entries, expire and evict deterministically, issue unique trackers, and atomically consume one tracker immediately before `RegisterUser`.

**Tech Stack:** C# 12, .NET 8, ASP.NET Core, `ConcurrentDictionary`, `Dictionary`, `Environment.TickCount64`, FreneticUtilities `LockObject`, existing SwarmUI OAuth/account helpers.

---

## Repository Constraints

- Work directly on `master`; do not create or use a worktree. This repository/user-specific requirement overrides the generic worktree recommendation.
- Reaper176 is an approved maintainer under `AGENTS.md`.
- Agents must not build, launch, test, automate a browser, start a server/backend, call a live API, or run test-executing lint.
- Do not add automated tests. Repository policy states that automated tests are not used and agents cannot run any form of testing.
- The maintainer performs the complete runtime matrix after static source and documentation review.
- Agents may use source searches, numbered source inspection, exact-range Git inspection, whitespace checks, index/scope checks, and step-by-step control-flow tracing.
- Use `apply_patch` for every file edit.
- Stage explicit paths only. Confirm the index is empty before and after every commit.
- Never edit generated `docs/APIRoutes`, downloaded upstream code, external extensions, backup files, build output, user data, or launchers.
- Preserve these unrelated maintainer paths exactly and keep them unstaged:
  - `src/Data/Settings.fds` (`83/3`);
  - `src/Pages/Text2Image.cshtml` (`9/2`);
  - `src/wwwroot/js/genpage/gentab/loras.js` (`2/0`);
  - `src/wwwroot/js/genpage/main.js` (`4/5`, comprising the existing declaration deletion and four lazy-tab state changes); and
  - untracked `Data.pre-restore-2026-07-19/`.
- Approved design: `docs/superpowers/specs/2026-07-27-oauth-registration-challenge-lifecycle-design.md`.
- Approved design commit: `bb88532e94e59b422728245fa10d29884bc1a810`.
- Approved source/audit base recorded by the design: `1426c55c0f18176486442180c67a86c133f8d6d0`.
- The design commit changes documentation only. Its committed production source is identical to the approved source/audit base.
- Keep production scope to exactly:
  - `src/Accounts/SessionHandler.cs`; and
  - `src/WebAPI/BasicAPIFeatures.cs`.
- Treat these as review-only compatibility surfaces:
  - `src/Pages/GoogleOAuthVerify.cshtml`;
  - `src/wwwroot/js/registerpage.js`;
  - `src/Pages/Register.cshtml`;
  - `src/WebAPI/API.cs`;
  - `src/Core/Settings.cs`;
  - `src/Accounts/User.cs`; and
  - maintained login/session/cookie owners.
- Preserve the public declaration and constructed generic type exactly:

```csharp
public ConcurrentDictionary<string, string> TempAuths = [];
```

- Do not replace the public dictionary object, change the field to a property, change its generic type, or remove its mutability.
- Use the process-monotonic `Environment.TickCount64` for process-local age tracking.
- A tracker expires when `now - created >= 15 * 60 * 1000`.
- Maintained lifecycle operations finish with at most 256 entries.
- Issuance removes every older tracker whose stored email equals the newly verified email.
- After expiry cleanup and same-email replacement, issuance evicts the oldest live entry while count is at least 256 so the new tracker is admitted.
- Creation-tick ties use ordinal tracker ordering.
- Metadata-free legacy/extension entries receive the current tick when first reconciled by a maintained helper.
- A valid tracker is consumed after current username/rate-limit checks and immediately before `RegisterUser`.
- Never restore a consumed tracker.
- Do not hold the challenge lock across Google HTTP, database, user, OAuth-link, response, or logging work.
- Preserve `CheckOAuth(string)`, `RegisterOAuth`, route/sessionless behavior, tracker format, `oauthTrackerKey`, `oauth_tracker_key`, `oauth_type`, verification, username checks, rate limits, error IDs, settings timing, redirects, UI, cookies, roles, persistence, and logs.
- Do not add background cleanup, settings, persistence, IP/browser/session/provider binding, account rollback, in-flight email ownership, telemetry, benchmarks, or performance claims.

## File Map

- Modify `src/Accounts/SessionHandler.cs`
  - retain the public `TempAuths` declaration;
  - add documented private lifetime/capacity fields, creation-tick metadata, and a dedicated lock;
  - add locked reconciliation, coordinated removal, deterministic oldest eviction, issuance, and atomic consumption helpers;
  - route the unregistered branch of `CheckOAuth` through the issuance helper.
- Modify `src/WebAPI/BasicAPIFeatures.cs`
  - replace the non-consuming `TempAuths.TryGetValue` with the `SessionHandler` atomic consume helper;
  - remove the success-only direct dictionary removal;
  - preserve every validation, rate-limit, creation, linking, log, and response statement around those two lines.
- Review but do not modify:
  - `src/Pages/GoogleOAuthVerify.cshtml`;
  - `src/wwwroot/js/registerpage.js`;
  - `src/Pages/Register.cshtml`;
  - `src/WebAPI/API.cs`;
  - `src/Core/Settings.cs`;
  - `src/Accounts/User.cs`; and
  - external extensions and backup files.
- Modify after source review:
  - `docs/superpowers/specs/2026-07-27-oauth-registration-challenge-lifecycle-design.md`; and
  - `docs/superpowers/audits/2026-07-21-maintainability-architecture-refresh.md`.

## Task 1: Pin the Baseline and Complete the Access Inventory

**Files:**

- Review only: `src/Accounts/SessionHandler.cs:545-597`
- Review only: `src/WebAPI/BasicAPIFeatures.cs:189-249`
- Review only: `src/Pages/GoogleOAuthVerify.cshtml:1-87`
- Review only: `src/wwwroot/js/registerpage.js:91-125`

- [ ] **Step 1: Confirm branch, commits, index, and protected state**

Run:

```bash
git rev-parse HEAD
git rev-parse bb88532e
git branch --show-current
git status --short
git diff --cached --name-only
git diff --numstat -- \
  src/Data/Settings.fds \
  src/Pages/Text2Image.cshtml \
  src/wwwroot/js/genpage/gentab/loras.js \
  src/wwwroot/js/genpage/main.js
```

Expected:

- branch is `master`;
- HEAD contains design `bb88532e` and may contain only the committed implementation plan after this document is saved;
- the index is empty;
- the four protected tracked paths remain `83/3`, `9/2`, `2/0`, and `4/5`;
- `Data.pre-restore-2026-07-19/` remains untracked.

If either production file has an unrelated working diff, stop and report the exact overlap before editing.

- [ ] **Step 2: Pin the committed baseline source**

Run:

```bash
git show bb88532e:src/Accounts/SessionHandler.cs | nl -ba | sed -n '545,605p'
git show bb88532e:src/WebAPI/BasicAPIFeatures.cs | nl -ba | sed -n '189,255p'
git diff bb88532e -- src/Accounts/SessionHandler.cs src/WebAPI/BasicAPIFeatures.cs
git diff -- src/Accounts/SessionHandler.cs src/WebAPI/BasicAPIFeatures.cs
```

Expected:

- `TempAuths` is `ConcurrentDictionary<string, string>` with the existing cleanup comment;
- `CheckOAuth` creates `SecureRandomHex(32)`, indexes `TempAuths`, and returns it;
- `RegisterOAuth` performs all username/rate-limit checks before `TempAuths.TryGetValue`;
- direct removal occurs only after `SetOAuthEmail`;
- both production files match the committed design boundary.

- [ ] **Step 3: Inventory every maintained owner and compatibility surface**

Run:

```bash
git grep -n -E 'TempAuths|CheckOAuth\(|RegisterOAuth\(|oauthTrackerKey|oauth_tracker_key|oauth_type' bb88532e \
  -- 'src/**' \
  ':(exclude)src/Extensions/**' \
  ':(exclude)**/*.bak'

git grep -n -E 'RegisterUser\(|SetOAuthEmail\(|LoginRateLimiterBy(IP|User)|ReservedUsernames|UsernameValidator' bb88532e \
  -- src/WebAPI/BasicAPIFeatures.cs \
  src/Accounts/SessionHandler.cs \
  src/Accounts/User.cs
```

Confirm:

- only `SessionHandler` creates a tracker;
- only `RegisterOAuth` reads/removes a tracker;
- the Razor page and `registerpage.js` are the maintained wire consumers;
- `oauth_type` remains accepted but unvalidated;
- `SetOAuthEmail` remains the final email uniqueness owner;
- no background cleanup or settings owner exists.

- [ ] **Step 4: Record the baseline public ABI and control-flow text**

Run:

```bash
git show bb88532e:src/Accounts/SessionHandler.cs \
  | rg -n -F 'public ConcurrentDictionary<string, string> TempAuths = [];'

git show bb88532e:src/Accounts/SessionHandler.cs \
  | sed -n '/public async Task<(bool, string)> CheckOAuth/,/^    }/p'

git show bb88532e:src/WebAPI/BasicAPIFeatures.cs \
  | sed -n '/public static async Task<JObject> RegisterOAuth/,/^    }/p'
```

Save the design commit as the implementation comparison point:

```bash
rank19_design_commit="$(git rev-parse bb88532e)"
printf '%s\n' "$rank19_design_commit"
```

Expected: `bb88532e94e59b422728245fa10d29884bc1a810`.

## Task 2: Add the Bounded Challenge Lifecycle Owner

**Files:**

- Modify: `src/Accounts/SessionHandler.cs:555-595`

- [ ] **Step 1: Replace the cleanup comment with documented compatibility and private lifecycle state**

Use `apply_patch` to retain the public field line and add these fields immediately around it:

```csharp
    /// <summary>Public compatibility map of temporary OAuth registration trackers to verified email addresses.</summary>
    public ConcurrentDictionary<string, string> TempAuths = [];

    /// <summary>Maximum valid age of a temporary OAuth registration tracker, in milliseconds.</summary>
    private const long TempAuthLifetimeMilliseconds = 15 * 60 * 1000;

    /// <summary>Maximum number of temporary OAuth registration trackers retained by maintained lifecycle operations.</summary>
    private const int TempAuthMaxCount = 256;

    /// <summary>Process-monotonic creation ticks for temporary OAuth registration trackers.</summary>
    private readonly Dictionary<string, long> TempAuthCreationTicks = [];

    /// <summary>Coordinates maintained temporary OAuth registration tracker lifecycle operations.</summary>
    private readonly LockObject TempAuthLock = new();
```

Do not change the public field line, constructed generic type, or object initializer.

- [ ] **Step 2: Add coordinated removal and deterministic oldest eviction**

Use `apply_patch` to add:

```csharp
    /// <summary>Removes a temporary OAuth tracker and its creation metadata. Must be called while holding <see cref="TempAuthLock"/>.</summary>
    private void RemoveTempAuthLocked(string tracker)
    {
        TempAuths.TryRemove(tracker, out _);
        TempAuthCreationTicks.Remove(tracker);
    }

    /// <summary>Removes the deterministically oldest temporary OAuth tracker. Must be called while holding <see cref="TempAuthLock"/>.</summary>
    private bool RemoveOldestTempAuthLocked()
    {
        string oldestTracker = null;
        long oldestTick = 0;
        foreach (KeyValuePair<string, long> entry in TempAuthCreationTicks)
        {
            if (!TempAuths.ContainsKey(entry.Key))
            {
                continue;
            }
            if (oldestTracker is null
                || entry.Value < oldestTick
                || (entry.Value == oldestTick && string.CompareOrdinal(entry.Key, oldestTracker) < 0))
            {
                oldestTracker = entry.Key;
                oldestTick = entry.Value;
            }
        }
        if (oldestTracker is null)
        {
            return false;
        }
        RemoveTempAuthLocked(oldestTracker);
        return true;
    }
```

Keep the full braced C# style and explicit types.

- [ ] **Step 3: Add metadata reconciliation, expiry, and external-overflow normalization**

Use `apply_patch` to add:

```csharp
    /// <summary>Reconciles compatibility entries, expires old trackers, and restores the maintained capacity bound. Must be called while holding <see cref="TempAuthLock"/>.</summary>
    private void NormalizeTempAuthsLocked(long now)
    {
        foreach (string tracker in TempAuthCreationTicks.Keys.Where(tracker => !TempAuths.ContainsKey(tracker)).ToArray())
        {
            TempAuthCreationTicks.Remove(tracker);
        }
        foreach (string tracker in TempAuths.Keys)
        {
            if (!TempAuthCreationTicks.ContainsKey(tracker))
            {
                TempAuthCreationTicks[tracker] = now;
            }
        }
        foreach (KeyValuePair<string, long> entry in TempAuthCreationTicks.ToArray())
        {
            if (now - entry.Value >= TempAuthLifetimeMilliseconds)
            {
                RemoveTempAuthLocked(entry.Key);
            }
        }
        while (TempAuths.Count > TempAuthMaxCount)
        {
            if (!RemoveOldestTempAuthLocked())
            {
                break;
            }
        }
    }
```

This exact order preserves legacy entries, removes orphan metadata, applies the `>=` expiry boundary, then normalizes externally expanded state.

- [ ] **Step 4: Add bounded newest-email-only issuance**

Use `apply_patch` to add:

```csharp
    /// <summary>Creates a bounded temporary OAuth registration tracker for a verified email address.</summary>
    private string CreateTempAuth(string email)
    {
        long now = Environment.TickCount64;
        lock (TempAuthLock)
        {
            NormalizeTempAuthsLocked(now);
            foreach (KeyValuePair<string, string> entry in TempAuths.ToArray())
            {
                if (entry.Value == email)
                {
                    RemoveTempAuthLocked(entry.Key);
                }
            }
            while (TempAuths.Count >= TempAuthMaxCount)
            {
                if (!RemoveOldestTempAuthLocked())
                {
                    break;
                }
            }
            while (true)
            {
                string tracker = Utilities.SecureRandomHex(32);
                if (TempAuths.TryAdd(tracker, email))
                {
                    TempAuthCreationTicks[tracker] = now;
                    return tracker;
                }
            }
        }
    }
```

Do not log the email or tracker and do not move Google/network/database work under the lock.

- [ ] **Step 5: Add one-way atomic consumption**

Use `apply_patch` to add:

```csharp
    /// <summary>Atomically consumes a valid temporary OAuth registration tracker.</summary>
    internal bool TryConsumeTempAuth(string tracker, out string email)
    {
        long now = Environment.TickCount64;
        lock (TempAuthLock)
        {
            NormalizeTempAuthsLocked(now);
            if (!TempAuths.TryRemove(tracker, out email))
            {
                TempAuthCreationTicks.Remove(tracker);
                return false;
            }
            TempAuthCreationTicks.Remove(tracker);
            return true;
        }
    }
```

Do not add any restoration API.

- [ ] **Step 6: Route `CheckOAuth` issuance through the owner**

Use `apply_patch` to replace:

```csharp
        string cypherText = Utilities.SecureRandomHex(32);
        TempAuths[cypherText] = email;
        return (false, cypherText);
```

with:

```csharp
        return (false, CreateTempAuth(email));
```

Do not change any preceding verification or registered-user branch.

- [ ] **Step 7: Inspect the complete `SessionHandler` diff**

Run:

```bash
git diff -- src/Accounts/SessionHandler.cs
git diff --check -- src/Accounts/SessionHandler.cs
git diff --numstat -- src/Accounts/SessionHandler.cs
nl -ba src/Accounts/SessionHandler.cs | sed -n '545,750p'
```

Confirm:

- the public field line is unchanged;
- every new field has XML documentation;
- every new conditional/loop has braces;
- no `var` is introduced;
- all creation-tick accesses are textually inside lifecycle helpers;
- `Environment.TickCount64` is captured before each lock;
- Google HTTP and LiteDB lookup remain before issuance locking;
- no sensitive log is added.

- [ ] **Step 8: Run focused state-owner searches**

Run:

```bash
rg -n 'TempAuths|TempAuthCreationTicks|TempAuthLock|TempAuthLifetimeMilliseconds|TempAuthMaxCount|CreateTempAuth|TryConsumeTempAuth' \
  src/Accounts/SessionHandler.cs \
  src/WebAPI/BasicAPIFeatures.cs

rg -n 'TempAuths\\[|TempAuths\\.TryGetValue|TempAuths\\.Remove\\(' \
  src/Accounts/SessionHandler.cs \
  src/WebAPI/BasicAPIFeatures.cs

rg -n 'Logs\\..*(email|tracker)|TempAuth.*Logs|Logs.*TempAuth' \
  src/Accounts/SessionHandler.cs \
  || true
```

Expected before Task 3:

- direct issuance indexing is gone;
- `RegisterOAuth` still contains its old lookup/removal and will be migrated next;
- no new email/tracker log exists.

- [ ] **Step 9: Commit only the bounded owner**

Run:

```bash
test -z "$(git diff --cached --name-only)"
git add -- src/Accounts/SessionHandler.cs
git diff --cached --name-only
git diff --cached --check
git diff --cached -- src/Accounts/SessionHandler.cs
git commit -m "fix: bound OAuth registration challenges"
```

Expected committed scope: exactly `src/Accounts/SessionHandler.cs`.

- [ ] **Step 10: Verify commit and protected state**

Run:

```bash
git show --check --oneline --stat HEAD
git show --format= --name-only HEAD
git diff --cached --name-only
git status --short
git diff --numstat -- \
  src/Data/Settings.fds \
  src/Pages/Text2Image.cshtml \
  src/wwwroot/js/genpage/gentab/loras.js \
  src/wwwroot/js/genpage/main.js
```

Confirm the index is empty and the protected state is unchanged.

## Task 3: Atomically Consume Before Account Creation

**Files:**

- Modify: `src/WebAPI/BasicAPIFeatures.cs:201-248`

- [ ] **Step 1: Reconfirm the exact pre-consumption validation order**

Run:

```bash
nl -ba src/WebAPI/BasicAPIFeatures.cs | sed -n '196,252p'
git diff bb88532e -- src/WebAPI/BasicAPIFeatures.cs
```

Confirm the file is still unchanged from design commit `bb88532e` and that lookup remains after all current username/rate-limit checks and immediately before `RegisterUser`.

- [ ] **Step 2: Replace non-consuming lookup with atomic consumption**

Use `apply_patch` to replace:

```csharp
        if (!Program.Sessions.TempAuths.TryGetValue(oauth_tracker_key, out string email))
```

with:

```csharp
        if (!Program.Sessions.TryConsumeTempAuth(oauth_tracker_key, out string email))
```

Keep the invalid-tracker log and `invalid_input` response unchanged.

- [ ] **Step 3: Remove success-only direct dictionary removal**

Use `apply_patch` to delete only:

```csharp
        Program.Sessions.TempAuths.Remove(oauth_tracker_key, out _);
```

Do not add restoration after `RegisterUser`, `SetOAuthEmail`, exceptions, or response failure.

- [ ] **Step 4: Inspect the complete route diff and ordering**

Run:

```bash
git diff -- src/WebAPI/BasicAPIFeatures.cs
git diff --unified=20 -- src/WebAPI/BasicAPIFeatures.cs
git diff --check -- src/WebAPI/BasicAPIFeatures.cs
nl -ba src/WebAPI/BasicAPIFeatures.cs | sed -n '196,252p'
```

Expected:

- one call-site replacement;
- one success-only removal deletion;
- normalization, length checks, both rate limits, username existence, first-character, and reserved-name checks remain before consumption;
- `RegisterUser` immediately follows successful consumption;
- `SetOAuthEmail`, logs, error IDs, and responses are unchanged;
- no restore path exists.

- [ ] **Step 5: Run the complete post-migration access inventory**

Run:

```bash
rg -n 'TempAuths|TempAuthCreationTicks|TempAuthLock|CreateTempAuth|TryConsumeTempAuth' \
  src \
  --glob '!src/Extensions/**' \
  --glob '!**/*.bak'

rg -n 'TempAuths\\[|TempAuths\\.TryGetValue|TempAuths\\.Remove\\(' \
  src \
  --glob '!src/Extensions/**' \
  --glob '!**/*.bak' \
  || true

rg -n 'TryConsumeTempAuth' src/WebAPI/BasicAPIFeatures.cs
```

Confirm:

- maintained issuance occurs only through `CreateTempAuth`;
- maintained consumption occurs only through `TryConsumeTempAuth`;
- no old direct add/lookup/removal remains outside lifecycle helper internals.

- [ ] **Step 6: Commit only the atomic route migration**

Run:

```bash
test -z "$(git diff --cached --name-only)"
git add -- src/WebAPI/BasicAPIFeatures.cs
git diff --cached --name-only
git diff --cached --check
git diff --cached -- src/WebAPI/BasicAPIFeatures.cs
git commit -m "fix: consume OAuth registration challenges atomically"
```

Expected committed scope: exactly `src/WebAPI/BasicAPIFeatures.cs`.

- [ ] **Step 7: Verify both production commits and protected state**

Run:

```bash
rank19_source_head="$(git rev-parse HEAD)"
git log --oneline --reverse bb88532e.."$rank19_source_head"
git log --oneline bb88532e.."$rank19_source_head" -- \
  src/Accounts/SessionHandler.cs \
  src/WebAPI/BasicAPIFeatures.cs
git diff --name-only bb88532e.."$rank19_source_head" -- \
  src/Accounts/SessionHandler.cs \
  src/WebAPI/BasicAPIFeatures.cs
git diff --check bb88532e.."$rank19_source_head" -- \
  src/Accounts/SessionHandler.cs \
  src/WebAPI/BasicAPIFeatures.cs
git diff --cached --name-only
git status --short
git diff --numstat -- \
  src/Data/Settings.fds \
  src/Pages/Text2Image.cshtml \
  src/wwwroot/js/genpage/gentab/loras.js \
  src/wwwroot/js/genpage/main.js
```

Expected production paths: exactly `src/Accounts/SessionHandler.cs` and `src/WebAPI/BasicAPIFeatures.cs`; protected state remains unchanged and unstaged.

## Task 4: Complete Static Source Verification and Independent Reviews

**Files:**

- Review only: `src/Accounts/SessionHandler.cs`
- Review only: `src/WebAPI/BasicAPIFeatures.cs`
- Review only: compatibility surfaces in the file map

- [ ] **Step 1: Separate integrated history from production projection**

Run:

```bash
rank19_design_commit="$(git rev-parse bb88532e)"
rank19_integrated_head="$(git rev-parse HEAD)"

git log --oneline --reverse "$rank19_design_commit..$rank19_integrated_head"
git log --oneline "$rank19_design_commit..$rank19_integrated_head" -- \
  src/Accounts/SessionHandler.cs \
  src/WebAPI/BasicAPIFeatures.cs

git diff --name-only "$rank19_design_commit..$rank19_integrated_head" -- \
  src/Accounts/SessionHandler.cs \
  src/WebAPI/BasicAPIFeatures.cs

git diff --numstat "$rank19_design_commit..$rank19_integrated_head" -- \
  src/Accounts/SessionHandler.cs \
  src/WebAPI/BasicAPIFeatures.cs

git diff --check "$rank19_design_commit..$rank19_integrated_head" -- \
  src/Accounts/SessionHandler.cs \
  src/WebAPI/BasicAPIFeatures.cs
```

Expected:

- integrated history includes the plan and two focused source commits;
- source-path history includes only the two production commits;
- source projection contains exactly the two approved production files;
- whitespace check is silent.

- [ ] **Step 2: Prove public ABI preservation**

Run:

```bash
git show "$rank19_design_commit":src/Accounts/SessionHandler.cs \
  | rg -n -F 'public ConcurrentDictionary<string, string> TempAuths = [];'

git show "$rank19_integrated_head":src/Accounts/SessionHandler.cs \
  | rg -n -F 'public ConcurrentDictionary<string, string> TempAuths = [];'

git diff "$rank19_design_commit..$rank19_integrated_head" -- src/Accounts/SessionHandler.cs \
  | rg -n '^[+-].*(public ConcurrentDictionary<string, string> TempAuths|public .*TempAuth|CheckOAuth\\(string credential\\))' \
  || true
```

Expected:

- the field line appears once at both endpoints;
- no field/property/type conversion exists;
- `CheckOAuth(string)` is unchanged;
- the only new non-private member is the intended assembly-internal consume helper.

- [ ] **Step 3: Trace expiry, capacity, same-email, and deterministic ordering**

Run:

```bash
rg -n 'TempAuthLifetimeMilliseconds|TempAuthMaxCount|Environment.TickCount64|>= TempAuthLifetimeMilliseconds|TempAuths.Count|CompareOrdinal|entry.Value == email|SecureRandomHex\\(32\\)|TryAdd' \
  src/Accounts/SessionHandler.cs

nl -ba src/Accounts/SessionHandler.cs | sed -n '555,750p'
```

Confirm:

- lifetime is exactly 900,000 milliseconds;
- expiry comparison is `>=`;
- external overflow is reduced while count is greater than 256;
- issuance makes room while count is at least 256;
- timestamp then ordinal tracker determines oldest;
- every prior same-email tracker is removed;
- collision retry remains under the in-memory lock;
- the new tracker receives the issuance tick.

- [ ] **Step 4: Trace atomic consumption and no-restoration behavior**

Run:

```bash
nl -ba src/WebAPI/BasicAPIFeatures.cs | sed -n '196,252p'

rg -n 'TryConsumeTempAuth|RegisterUser|SetOAuthEmail|registration_failed|TempAuth' \
  src/WebAPI/BasicAPIFeatures.cs \
  src/Accounts/SessionHandler.cs

rg -n 'Restore|Reinsert|TryAdd\\(oauth_tracker_key|TempAuths\\[oauth_tracker_key\\]' \
  src/Accounts/SessionHandler.cs \
  src/WebAPI/BasicAPIFeatures.cs \
  || true
```

Confirm:

- all correctable validation/rate-limit branches precede consumption;
- consumption immediately precedes `RegisterUser`;
- the helper removes under one lock;
- no success-only removal remains;
- no restoration path exists.

- [ ] **Step 5: Prove lock-span and sensitive-data boundaries**

Run:

```bash
rg -n 'lock \\(TempAuthLock\\)|GetStringAsync|UserOAuthLookupDB|RegisterUser|SetOAuthEmail|Logs\\.' \
  src/Accounts/SessionHandler.cs \
  src/WebAPI/BasicAPIFeatures.cs

git diff "$rank19_design_commit..$rank19_integrated_head" -- \
  src/Accounts/SessionHandler.cs \
  src/WebAPI/BasicAPIFeatures.cs \
  | rg -n '^\\+.*Logs\\..*(email|tracker)|^\\+.*(GetStringAsync|UserOAuthLookupDB|RegisterUser|SetOAuthEmail)' \
  || true
```

Confirm no network/database/user/log statement was moved under `TempAuthLock` and no sensitive log was added.

- [ ] **Step 6: Prove unchanged compatibility surfaces**

Run:

```bash
git diff --name-only "$rank19_design_commit..$rank19_integrated_head" -- \
  src/Pages/GoogleOAuthVerify.cshtml \
  src/wwwroot/js/registerpage.js \
  src/Pages/Register.cshtml \
  src/WebAPI/API.cs \
  src/Core/Settings.cs \
  src/Accounts/User.cs \
  src/Extensions \
  '**/*.bak'

git diff "$rank19_design_commit..$rank19_integrated_head" -- \
  src/WebAPI/BasicAPIFeatures.cs \
  | rg -n '^[+-].*(oauth_type|error_id|LoginRateLimiter|UsernameValidator|ReservedUsernames|RegisterUser|SetOAuthEmail|Logs\\.|success)' \
  || true
```

Expected:

- no compatibility-surface path changed;
- route behavior changes are limited to atomic tracker ownership;
- no UI/error/settings/provider/account behavior drift appears.

- [ ] **Step 7: Obtain independent source specification approval**

Require a fresh reviewer to verify the complete approved design against the source projection and return `SOURCE_SPEC_APPROVED` only when there are no Critical or Important findings.

The reviewer must inspect:

- 15-minute `>=` validity;
- 256-entry post-operation bound;
- expiry before live eviction;
- oldest-tick/ordinal tie-break;
- newest tracker only per email;
- metadata-free legacy reconciliation;
- unique secure tracker generation;
- pre-consumption validation ordering;
- atomic one-way consumption;
- no restore after failure;
- public field/signature ABI;
- unchanged Razor/JavaScript/API/settings/account surfaces;
- lock-span and logging boundaries;
- protected state.

- [ ] **Step 8: Obtain independent source quality approval**

After specification approval, require a separate fresh reviewer to check:

- clarity and cohesion of helper ownership;
- explicit C# types, braces, XML field docs, and repository style;
- dual-collection consistency;
- deterministic iteration/eviction;
- unnecessary complexity or duplicated logic;
- exception and external-mutation caveats;
- minimal source scope.

The reviewer returns `SOURCE_QUALITY_APPROVED` only with no remaining Critical or Important findings. Fix findings in focused commits and repeat the affected review.

## Task 5: Record Static Closure and Hand Off Runtime Validation

**Files:**

- Modify: `docs/superpowers/specs/2026-07-27-oauth-registration-challenge-lifecycle-design.md`
- Modify: `docs/superpowers/audits/2026-07-21-maintainability-architecture-refresh.md`

- [ ] **Step 1: Record implementation provenance and current status**

Use `apply_patch` after both source reviews approve to record:

- approved source/audit base `1426c55c0f18176486442180c67a86c133f8d6d0`;
- design commit `bb88532e94e59b422728245fa10d29884bc1a810`;
- plan commit hash and subject;
- both production commit hashes and subjects;
- exact two-file source projection and final numstat;
- `SOURCE_SPEC_APPROVED` and `SOURCE_QUALITY_APPROVED`;
- static-only evidence and prohibited-runtime boundary;
- all preserved contracts and deferred caveats;
- status `Implemented; awaiting maintainer validation`;
- Rank 19 remains the sole Recommended Next Project until validation is recorded.

Do not change the approved 20-case matrix.

- [ ] **Step 2: Verify documentation invariants**

Run:

```bash
design='docs/superpowers/specs/2026-07-27-oauth-registration-challenge-lifecycle-design.md'
audit='docs/superpowers/audits/2026-07-21-maintainability-architecture-refresh.md'

rg -c '^## ' "$audit"
sed -n '/^## Ranked Refactoring Roadmap$/,/^## Recommended Next Project$/p' "$audit" \
  | rg -c '^### [0-9]+\\.'

sed -n '/^## Maintainer Validation Matrix$/,/^## Success Criteria$/p' "$design" \
  | rg -c '^[0-9]+\\.'

git diff bb88532e -- "$design" \
  | sed -n '/Maintainer Validation Matrix/,/Success Criteria/p'

git diff --check -- "$design" "$audit"
```

Expected:

- audit retains 12 H2 sections;
- ranked roadmap retains 32 sequential entries;
- design retains exactly 20 matrix cases;
- matrix text is byte-identical to `bb88532e`;
- whitespace check is silent.

- [ ] **Step 3: Commit static closure documentation**

Run:

```bash
test -z "$(git diff --cached --name-only)"
git add -- \
  docs/superpowers/specs/2026-07-27-oauth-registration-challenge-lifecycle-design.md \
  docs/superpowers/audits/2026-07-21-maintainability-architecture-refresh.md
git diff --cached --name-only
git diff --cached --check
git diff --cached
git commit -m "docs: record OAuth registration challenge lifecycle"
```

Expected committed scope: exactly the design and audit documents.

- [ ] **Step 4: Obtain documentation specification and quality approvals**

Sequentially require fresh reviewers to return:

- `DOCS_SPEC_APPROVED` after verifying provenance, source projection, statuses, exact matrix, preserved contracts, and Rank 19 roadmap state;
- `DOCS_QUALITY_APPROVED` after verifying historical/current wording, consistency, clarity, caveats, and no unsupported runtime/performance claim.

Fix findings in focused documentation commits and repeat the affected review.

- [ ] **Step 5: Hand the exact unchanged 20-case matrix to Reaper176**

The maintainer records:

- result for all 20 cases;
- date;
- operating system;
- filesystem;
- browser;
- browser version when available.

Agents perform no runtime action and do not infer omitted environment details.

- [ ] **Step 6: Record maintainer evidence after it is supplied**

Use `apply_patch` to update the design and audit with:

- the maintainer's raw message exactly;
- any explicitly disclosed normalization;
- the normalized outcome;
- environment/browser/version without inference;
- exact matrix limitation;
- retained caveats and other unvalidated environments;
- Rank 19 `Implemented and maintainer-validated` status;
- Rank 20 as sole Recommended Next Project, neither designed nor implemented.

Commit only the two documents with:

```bash
git commit -m "docs: validate OAuth registration challenge lifecycle"
```

- [ ] **Step 7: Complete validation-document and final integrated reviews**

Sequentially require:

- `VALIDATION_SPEC_APPROVED`;
- `VALIDATION_QUALITY_APPROVED`; and
- after recording those tokens, `FINAL_INTEGRATED_APPROVED`.

The final reviewer inspects the complete design-to-head history, exact production projection, review provenance, maintainer evidence, 12/32/20 document invariants, Rank 20 next-project status, whitespace, empty index, and protected state.

- [ ] **Step 8: Run final controller static verification**

Run:

```bash
git branch --show-current
git rev-parse HEAD
git log --oneline --reverse bb88532e..HEAD
git diff --check bb88532e..HEAD
git log --oneline bb88532e..HEAD -- \
  src/Accounts/SessionHandler.cs \
  src/WebAPI/BasicAPIFeatures.cs
git diff --name-only bb88532e..HEAD -- \
  src/Accounts/SessionHandler.cs \
  src/WebAPI/BasicAPIFeatures.cs
git diff --cached --name-only
git status --short
git diff --numstat -- \
  src/Data/Settings.fds \
  src/Pages/Text2Image.cshtml \
  src/wwwroot/js/genpage/gentab/loras.js \
  src/wwwroot/js/genpage/main.js
```

Confirm all approvals, source/document invariants, maintainer result, empty index, and protected state before claiming Rank 19 complete.
