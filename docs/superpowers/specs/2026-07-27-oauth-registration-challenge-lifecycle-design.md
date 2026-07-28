# OAuth Registration Challenge Lifecycle Design

**Status:** Approved; implementation not started

**Date:** 2026-07-27

**Rank:** 19 — Bound and atomically consume OAuth registration challenges

**Approved source/audit base:** `1426c55c0f18176486442180c67a86c133f8d6d0`

## Summary

SwarmUI currently stores pending OAuth registration challenges in the public process-wide `SessionHandler.TempAuths` field, a `ConcurrentDictionary<string, string>` from opaque tracker to verified email address. `CheckOAuth` creates one entry for every valid Google identity that is not already registered. `RegisterOAuth` reads the entry without consuming it, creates and links the account, and removes the entry only after success.

This leaves abandoned email-bearing entries in memory until process shutdown and lets concurrent requests observe the same tracker before either removes it. Rank 19 gives maintained challenges a 15-minute validity lifetime, keeps at most 256 entries, retains only the newest pending tracker for one email, and atomically consumes a valid tracker immediately before account creation.

The public dictionary declaration, generic type, and object identity remain unchanged for extension source and binary compatibility. Private metadata and one dedicated lock provide the maintained lifecycle without changing the browser wire key, route, parameters, OAuth verification, UI, error identifiers, settings, or persistence.

## Current Boundary

### Maintained owners

- `src/Accounts/SessionHandler.cs`
  - public `TempAuths` compatibility field;
  - `CheckOAuth` Google verification and challenge issuance;
  - new private timestamp, lock, normalization, expiry, capacity, issuance, and consumption ownership.
- `src/WebAPI/BasicAPIFeatures.cs`
  - `RegisterOAuth` validation ordering;
  - one call to the `SessionHandler` atomic consumption helper.

### Maintained consumers

- `src/Pages/GoogleOAuthVerify.cshtml` calls `CheckOAuth`, distinguishes registered login from unregistered registration, and emits the opaque tracker as `oauthTrackerKey`.
- `src/wwwroot/js/registerpage.js` submits the tracker through the existing `oauth_tracker_key` field to the existing sessionless `RegisterOAuth` route.

No other maintained `TempAuths` add, lookup, or removal path exists at the approved base.

## Goals

- Make one tracker successful in at most one maintained registration attempt.
- Make a tracker invalid at an age of 15 minutes or more.
- Keep maintained challenge state at no more than 256 entries after a maintained lifecycle operation.
- Remove every older pending tracker when the same email receives a new tracker.
- Admit new verified challenges at capacity by evicting the oldest remaining tracker.
- Preserve correctable username and rate-limit retries until the tracker expires, is superseded, is evicted, or is consumed.
- Preserve the public `TempAuths` field declaration and existing external contract.
- Preserve all current browser, API, verification, account, setting, logging, and error contracts outside the challenge lifecycle.

## Non-Goals

Rank 19 does not:

- add a background cleanup timer or worker;
- persist challenges or timestamps;
- add configuration settings for lifetime or capacity;
- bind a tracker to an IP address, browser, user agent, cookie, session, or OAuth provider field;
- validate or reinterpret the existing `oauth_type` parameter;
- redesign Google credential verification, audience/domain checks, or login;
- change when the Razor page checks registration settings;
- add a new registration-setting check to `RegisterOAuth`;
- change username validation, rate limits, error identifiers, or UI messages;
- make account creation and OAuth email linking transactional;
- roll back a partially created account;
- restore a consumed tracker after any account/link failure;
- change `SetOAuthEmail` uniqueness behavior;
- guarantee lifecycle bounds against unsupported extensions that mutate the public dictionary directly between maintained operations;
- add logs containing verified emails or tracker values; or
- claim a performance improvement or benchmark result.

## Decisions

The maintainer approved these choices:

1. Username normalization, username checks, and both rate limits run before consumption. A valid tracker is atomically consumed immediately before `RegisterUser` and is never restored.
2. Tracker validity lasts 15 minutes from issuance.
3. Capacity is 256 entries. After expiry cleanup, issuance evicts the oldest remaining tracker rather than rejecting the new verified challenge.
4. A new tracker for an email invalidates every older pending tracker for that email.
5. The public `ConcurrentDictionary<string, string> TempAuths` field remains the compatibility surface. Private metadata and a dedicated lock coordinate maintained operations.

## Considered Approaches

### Selected: retain the public dictionary and add private coordinated metadata

Keep `TempAuths` unchanged, add private creation-time metadata keyed by tracker, and serialize maintained issuance and consumption under one dedicated lock.

This is the smallest production change, preserves the public member ABI, keeps the existing email view available to extensions, and permits atomic maintained flow without changing browser or API contracts. The cost is coordinated state in two collections and an explicit compatibility rule for entries inserted directly by extensions.

### Rejected: change `TempAuths` to store challenge records

Replacing the string value with a record would make email and creation time one value and simplify cleanup. It would change the public field's constructed generic type, breaking source and binary compatibility for precompiled extensions. Rank 19 does not accept that ABI change.

### Rejected: authoritative private coordinator plus mirrored legacy dictionary

A separate authoritative store could encapsulate the full lifecycle while mirroring email values into the public field. It would create two independently visible data stores, introduce drift and rollback complexity, and make direct legacy mutations ambiguous. The selected design retains the existing field as the maintained email store instead.

## Architecture

### Compatibility field

`SessionHandler.TempAuths` remains the same public mutable field:

```csharp
public ConcurrentDictionary<string, string> TempAuths = [];
```

The implementation does not replace its object or change its type. All maintained source stops indexing, reading, or removing the field directly and uses the lifecycle helpers.

### Private lifecycle state

`SessionHandler` adds:

- a private tracker-to-creation-tick map;
- a private dedicated lock;
- a private 15-minute lifetime constant;
- a private 256-entry capacity constant;
- private normalization/removal/issuance helpers; and
- an assembly-internal atomic consumption helper for `RegisterOAuth`.

Every field receives the repository-required XML documentation. Timestamps use the process-monotonic `Environment.TickCount64`, because challenges are process-local and must not become longer-lived when the wall clock moves backward.

The challenge lock protects only in-memory challenge bookkeeping. No Google request, database call, user creation, email linking, response construction, or logging runs while it is held.

### Metadata reconciliation

At the start of every maintained issuance or consumption operation, while holding the challenge lock:

1. Remove timestamp metadata whose tracker is absent from `TempAuths`.
2. Give each `TempAuths` entry without metadata the current monotonic tick.
3. Remove entries whose age is at least 15 minutes from both collections.
4. If direct external mutation left more than 256 entries, remove oldest entries until at most 256 remain.

Treating a metadata-free entry as newly observed preserves the behavior of a legacy or extension-added email entry: the next maintained helper can still consume it. The helper then places it under the same lifetime and capacity policy. Direct extension mutation after the helper releases the lock remains outside the maintained guarantee, but the next maintained operation reconciles it again.

### Deterministic ordering

Oldest-entry selection uses the lowest creation tick. If multiple entries share that tick, ordinal tracker ordering selects the eviction candidate. This makes the result deterministic without exposing or changing tracker text.

Every coordinated removal deletes the tracker from both `TempAuths` and the timestamp map. A missing key in either collection is tolerated during reconciliation.

## Issuance Flow

The Google token-info HTTP request, JSON parsing, email/audience validation, allowed-domain check, and registered-email lookup remain unchanged and occur before challenge locking.

When a verified email is not already registered, `CheckOAuth` calls the issuance helper:

1. Capture the current monotonic tick.
2. Enter the challenge lock.
3. Reconcile metadata, expire old entries, and reduce externally expanded state to the capacity.
4. Remove every tracker whose stored email ordinally equals the newly verified email.
5. While 256 entries remain, remove the deterministically oldest entry so the new tracker has capacity.
6. Generate the existing `Utilities.SecureRandomHex(32)` tracker.
7. Add it with `TryAdd`; on the cryptographically improbable collision, generate another tracker.
8. Record the same current tick as its creation time.
9. Release the lock and return the tracker through the unchanged `(false, tracker)` result.

The maintained field contains at most 256 entries after issuance. A new tracker is always admitted; it is never rejected merely because the map was full.

## Consumption Flow

`RegisterOAuth` preserves this existing order before challenge consumption:

1. normalize the username;
2. reject invalid length;
3. charge/check the IP rate limiter;
4. charge/check the username rate limiter;
5. reject an existing username;
6. reject an invalid first character; and
7. reject a reserved username.

These failures retain the tracker so the browser can correct the username or retry after rate limiting, subject to normal expiry, supersession, and eviction.

Immediately before `RegisterUser`, `RegisterOAuth` calls the atomic consumption helper:

1. Capture the current monotonic tick.
2. Enter the challenge lock.
3. Reconcile metadata, remove expired entries, and reduce externally expanded state to the capacity.
4. Remove the requested tracker from `TempAuths`.
5. Remove its timestamp metadata.
6. Return the email only if the tracker existed and remained valid.

Only one maintained request can remove and receive one tracker. A sequential replay or concurrent loser receives no email and follows the existing invalid-tracker branch.

After successful consumption, the lock is released before `RegisterUser` and `SetOAuthEmail`. The tracker is never restored if `RegisterUser` returns null, `SetOAuthEmail` rejects the email, an exception occurs, or response delivery fails.

## Error and Retry Semantics

| Condition | Tracker state | Existing response behavior |
|---|---|---|
| Username is too short or long | Retained | `invalid_input` |
| IP or username rate limit rejects | Retained | `ratelimit` |
| Username exists, starts incorrectly, or is reserved | Retained | `username_exists` |
| Tracker is missing or unknown | Absent | `invalid_input` |
| Tracker is at least 15 minutes old | Removed | `invalid_input` |
| Tracker was evicted at capacity | Absent | `invalid_input` |
| Tracker was superseded by a newer tracker for the email | Absent | `invalid_input` |
| Tracker was already consumed or loses a concurrent consume | Absent | `invalid_input` |
| `RegisterUser` returns null after consumption | Permanently consumed | `registration_failed` |
| OAuth linking or later work throws after consumption | Permanently consumed | Existing exception handling |
| Registration is disabled before/after verification | Existing flow retained | Existing redirect/route behavior |

No new error ID or client message is introduced. The existing JavaScript callback continues to re-enable the registration button on failure.

## Concurrency Properties

- Maintained issuance, normalization, expiry, capacity eviction, same-email replacement, and consumption are serialized by one lock.
- Google verification and account/database work never execute under that lock.
- A tracker is returned to at most one maintained consumer.
- Two concurrent issuances for one email leave only the later lock winner's tracker pending.
- A concurrent issuance for the same email may occur after an earlier tracker has been consumed but before its account/email link completes. Existing `SetOAuthEmail` uniqueness remains the final email-link authority; Rank 19 does not add an in-flight email reservation or account transaction.
- Public `ConcurrentDictionary` operations remain safe for legacy readers. Unsupported external writes outside the lock can temporarily violate the count/metadata view until the next maintained operation reconciles state.

## Compatibility Contract

Rank 19 preserves:

- the public `SessionHandler.TempAuths` field name, type, mutability, and object identity;
- the `CheckOAuth(string)` signature and tuple meaning;
- the `RegisterOAuth` route name, signature, sessionless status, and response IDs;
- `oauthTrackerKey`, `oauth_tracker_key`, and opaque 32-character hexadecimal tracker format;
- the accepted but otherwise unchanged `oauth_type` parameter;
- Google token-info URL, credential parsing, audience and allowed-domain checks;
- existing-account OAuth login, cookie, redirect, and session behavior;
- username normalization/validation and both rate limiters;
- user role selection, user creation, and OAuth email uniqueness;
- registration-setting timing and behavior;
- Razor and JavaScript markup, scripts, and visible messages;
- process-local/non-persistent tracker behavior, including invalidation on restart;
- logs and their existing data exposure; and
- unrelated server routes, permissions, persistence, and public C# members.

Expected production changes are limited to `src/Accounts/SessionHandler.cs` and `src/WebAPI/BasicAPIFeatures.cs`. `GoogleOAuthVerify.cshtml` and `registerpage.js` are validation-only compatibility surfaces and should not change.

## Static Verification

Agents do not build, test, launch, automate a browser, start a server/backend, call live APIs, or perform runtime/platform/filesystem/performance validation in this repository.

Static verification must:

1. inventory every `TempAuths` reference before and after implementation;
2. prove the public field declaration and constructed generic type are textually unchanged;
3. prove maintained source has no direct add, lookup, or removal outside the helpers;
4. inspect every timestamp-map access and confirm it occurs under the dedicated lock;
5. trace normalization, age comparison, same-email replacement, deterministic eviction, collision retry, and dual-collection removal;
6. prove the 15-minute and 256-entry constants and `age >= lifetime` boundary;
7. prove `RegisterOAuth` validation/rate-limit ordering is unchanged before consumption;
8. prove consumption is immediately before `RegisterUser` and no restoration path exists;
9. prove no external/network/database/user work occurs under the challenge lock;
10. compare `CheckOAuth` verification and registered-login branches against the approved base;
11. confirm `GoogleOAuthVerify.cshtml`, `registerpage.js`, API route registration, settings, and other authentication owners are unchanged;
12. inspect public/protected member changes and extension ABI implications;
13. run fixed-range whitespace checks; and
14. confirm protected maintainer work remains unstaged and absent from Rank 19 commits.

## Maintainer Validation Matrix

The maintainer performs all runtime validation and records the browser, browser version when available, operating system, and filesystem.

1. An already registered Google OAuth email logs in with unchanged cookie, redirect, session, and logout behavior and creates no pending registration challenge.
2. A new allowed Google OAuth email receives an opaque tracker and completes ordinary registration with the configured default role and linked OAuth email.
3. Invalid credentials, wrong audience, missing email/audience, and a disallowed domain create no usable challenge and retain their current redirect/log behavior.
4. Too-short and too-long usernames return `invalid_input`, re-enable the button, and leave the same tracker usable for a corrected username before expiry.
5. Existing, invalid-starting-character, and reserved usernames return `username_exists`, re-enable the button, and leave the tracker usable for a valid username before expiry.
6. IP and username rate-limit rejection returns `ratelimit`, re-enables the button, and does not consume the tracker.
7. A tracker younger than 15 minutes remains usable; at an age of 15 minutes or more it returns `invalid_input` and cannot be reused.
8. A second successful verification for the same email invalidates the first tracker; only the newest tracker can register.
9. With 256 unexpired distinct-email challenges, issuing the 257th admits the new tracker, evicts the oldest tracker, and leaves the other 255 prior trackers pending.
10. Expired entries are removed before live-entry eviction, so a new issuance does not evict a live tracker when expired capacity is available.
11. A random, missing, evicted, expired, or superseded tracker returns the same `invalid_input` response and UI behavior.
12. After one successful consumption, a sequential second submission with the same tracker returns `invalid_input` and does not create or link another account.
13. Two concurrent submissions of the same valid tracker allow at most one request past challenge consumption; the other returns `invalid_input`.
14. An induced `RegisterUser` failure after consumption returns `registration_failed`, and retrying the same tracker returns `invalid_input`.
15. An induced OAuth-linking failure after consumption retains the existing account/error boundary, and the tracker is not restored.
16. Disabling registration at the existing verification-page decision point retains the current redirect behavior; pending state remains bounded and later expires.
17. Changing registration settings after tracker issuance retains the pre-Rank-19 route behavior; Rank 19 does not add a new route-level settings gate.
18. A legacy/extension-style email entry added directly to `TempAuths` remains consumable when first encountered by a maintained helper and then participates in expiry/capacity metadata.
19. Restarting SwarmUI invalidates all process-local pending trackers exactly as before.
20. Repeated mixed issuance, invalid-input retry, supersession, expiry, eviction, and consumption never leaves more than 256 entries after a maintained lifecycle operation and never exposes email or tracker values in new logs.

## Success Criteria

Rank 19 is successful when:

- every maintained tracker is valid for less than 15 minutes;
- every maintained lifecycle operation finishes with at most 256 entries;
- the newest pending tracker is the only pending tracker for one email;
- a valid tracker reaches at most one maintained account-creation attempt;
- correctable pre-consumption failures preserve the tracker;
- post-consumption failures never restore it;
- oldest eviction admits a new verified challenge at capacity;
- missing/expired/evicted/superseded/replayed trackers retain `invalid_input`;
- all compatibility contracts and unchanged surfaces remain intact;
- static review finds no uncovered maintained access, lock escape, sensitive log, ABI drift, or whitespace error;
- the maintainer confirms the complete matrix; and
- protected maintainer work remains untouched.

## Rollback

Rollback removes the private timestamp/lock/helpers, restores direct issuance assignment in `CheckOAuth`, restores the `TryGetValue` plus success-only removal sequence in `RegisterOAuth`, and leaves the public `TempAuths` field intact.

Partial rollback is not valid: retaining atomic consumption without coordinated issuance metadata would lose expiry/capacity behavior, while retaining metadata without helper-owned consumption would restore the replay window.

## Deferred Boundaries

- Background cleanup could remove expired email-bearing entries without later OAuth activity, but is not required for strict validity or the 256-entry memory bound and would add lifecycle ownership.
- Replacing the public field with a record-valued private store requires an explicit extension ABI migration.
- An in-flight email reservation or transaction spanning account creation and OAuth linking would address a wider email/account race and rollback problem; it is not part of tracker one-use semantics.
- New registration-setting enforcement in the sessionless API route requires a separate behavior/security design.
- Provider, IP, browser, cookie, or session binding requires separate compatibility and proxy/privacy analysis.
