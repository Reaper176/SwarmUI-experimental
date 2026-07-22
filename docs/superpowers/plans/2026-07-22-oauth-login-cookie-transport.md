# OAuth Login Cookie Transport Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make Google OAuth login apply the same request-aware `Secure` decision to `swarm_token` as password login and logout, without changing authentication, session, cookie, or proxy policy.

**Architecture:** Modify only the existing Google OAuth cookie-options initializer to read `HttpContext.Request.IsHttps`. Keep password login, logout, readers, token/session ownership, and the Web pipeline unchanged; record the direct/reverse-proxy interpretation and implementation status in the existing design and architecture audit.

**Tech Stack:** C# 12 Razor Pages, .NET 8, ASP.NET Core `HttpContext` and `CookieOptions`.

**Execution context:** Work directly on `master` as requested by maintainer Reaper176. Do not create a worktree. Preserve the existing modified files and untracked `Data.pre-restore-2026-07-19/`; never inspect or stage that directory.

**Repository verification constraint:** `AGENTS.md` prohibits agents from running builds, automated tests, browsers, servers, backends, or launchers. The maintainer supplies compilation and runtime validation. Agents use static source tracing, exact-text/count assertions, diff/whitespace checks, and independent reviews.

**Approved design:** `docs/superpowers/specs/2026-07-22-oauth-login-cookie-transport-design.md` at commit `74bf9306`.

**Implementation outcome:** Production is implemented, statically reviewed, and maintainer-validated for the named HTTP/HTTPS/proxy authentication matrix.

---

### Task 1: Align the Google OAuth cookie transport flag

**Files:**
- Modify: `src/Pages/GoogleOAuthVerify.cshtml:34`
- Reference: `src/WebAPI/BasicAPIFeatures.cs:130`
- Reference: `src/WebAPI/BasicAPIFeatures.cs:322`
- Reference: `src/Utils/WebUtil.cs:257-322`

- [ ] **Step 1: Reconfirm the exact source boundary and protected worktree**

Run:

```bash
git status --short --branch --untracked-files=no
nl -ba src/Pages/GoogleOAuthVerify.cshtml | sed -n '24,38p'
nl -ba src/WebAPI/BasicAPIFeatures.cs | sed -n '124,132p;314,324p'
nl -ba src/Utils/WebUtil.cs | sed -n '257,322p'
```

Expected: OAuth is the only `swarm_token` writer missing `Secure`; password login and logout use `context.Request.IsHttps`; the tracked working tree contains only the known four maintainer-owned modifications; cookie readers do not branch on login method.

- [ ] **Step 2: Add only the approved request-aware property**

Use `apply_patch` to replace:

```csharp
HttpContext.Response.Cookies.Append("swarm_token", tok, new CookieOptions() { HttpOnly = true, Expires = DateTimeOffset.UtcNow.AddYears(1), SameSite = SameSiteMode.Lax });
```

with:

```csharp
HttpContext.Response.Cookies.Append("swarm_token", tok, new CookieOptions() { HttpOnly = true, Expires = DateTimeOffset.UtcNow.AddYears(1), SameSite = SameSiteMode.Lax, Secure = HttpContext.Request.IsHttps });
```

Do not change credential verification, account lookup, session creation, token handling, logs, redirects, registration, password login, logout, readers, Web middleware, settings, or launchers.

- [ ] **Step 3: Statically prove writer alignment and exact scope**

Run:

```bash
test "$(rg -F -o 'Secure = HttpContext.Request.IsHttps' src/Pages/GoogleOAuthVerify.cshtml | wc -l)" = "1"
test "$(rg -F -o 'Secure = context.Request.IsHttps' src/WebAPI/BasicAPIFeatures.cs | wc -l)" = "2"
test "$(rg -F -o 'Response.Cookies.Append("swarm_token"' src/Pages/GoogleOAuthVerify.cshtml src/WebAPI/BasicAPIFeatures.cs | wc -l)" = "3"
test "$(git diff --name-only -- src/Pages/GoogleOAuthVerify.cshtml src/WebAPI/BasicAPIFeatures.cs src/Utils/WebUtil.cs src/Core/WebServer.cs)" = "src/Pages/GoogleOAuthVerify.cshtml"
git diff --check -- src/Pages/GoogleOAuthVerify.cshtml
git diff -- src/Pages/GoogleOAuthVerify.cshtml
```

Expected: all three maintained cookie append/clear sites use their request's `IsHttps`; the diff adds exactly one property to the OAuth initializer; comparison writers, reader, and Web pipeline are unchanged.

- [ ] **Step 4: Commit the production fix alone**

```bash
git add src/Pages/GoogleOAuthVerify.cshtml
git diff --cached --check
test "$(git diff --cached --name-only)" = "src/Pages/GoogleOAuthVerify.cshtml"
git commit -m "fix: secure OAuth cookie on HTTPS"
```

---

### Task 2: Record implementation without claiming runtime validation

**Files:**
- Modify: `docs/superpowers/specs/2026-07-22-oauth-login-cookie-transport-design.md`
- Modify: `docs/superpowers/audits/2026-07-21-maintainability-architecture-refresh.md`
- Modify: `docs/superpowers/plans/2026-07-22-oauth-login-cookie-transport.md`

- [ ] **Step 1: Update the design status**

Use `apply_patch` to replace:

```markdown
**Status:** Approved for implementation planning
```

with:

```markdown
**Status:** Implemented; awaiting maintainer validation
```

- [ ] **Step 2: Update only Core F9 and rank-3 audit records**

Use `apply_patch` to update the Core F9 finding, risk-register row, rank-3 roadmap entry, Recommended Next Project, finding dispositions, and verification-boundary status with these implementation facts:

```markdown
**Implemented; awaiting maintainer validation.** The Google OAuth writer now sets `Secure = HttpContext.Request.IsHttps`, exactly matching the request-aware decision used by password login and logout. Cookie name/value, HttpOnly, one-year expiry, SameSite, session creation/storage/validation, redirect, registration, readers, and protected consumers are unchanged. No forwarded-header, trusted-proxy, Kestrel, settings, launcher, or Web-pipeline behavior changed.
```

Retain the following facts in every edited audit section:

```markdown
Direct HTTPS, or trusted hosting that already establishes the ASP.NET request scheme as HTTPS, produces a Secure OAuth cookie. A TLS-terminating proxy whose Swarm-facing request remains HTTP is a separate scheme-establishment limitation and is not solved by trusting raw forwarding headers in this change. Rank 3 remains the recommended project until maintainer validation covers HTTP, direct HTTPS, supported proxy, OAuth/password login, and logout.
```

Do not mark Core F9 maintainer-validated, advance Recommended Next Project to rank 4, alter the 24-production/eight-measurement/32-rank totals, or edit unrelated findings.

- [ ] **Step 3: Record the implementation outcome in this plan**

Use `apply_patch` to insert immediately after the approved-design line:

```markdown
**Implementation outcome:** Production is implemented and statically reviewed; maintainer compilation and HTTP/HTTPS/proxy authentication validation are pending.
```

- [ ] **Step 4: Verify documentation/source agreement**

Run:

```bash
rg -n 'Status:|Implementation outcome|Core F9|OAuth|Secure = HttpContext.Request.IsHttps|maintainer validation|rank 3|Recommended Next Project|forwarded|proxy' docs/superpowers/specs/2026-07-22-oauth-login-cookie-transport-design.md docs/superpowers/plans/2026-07-22-oauth-login-cookie-transport.md docs/superpowers/audits/2026-07-21-maintainability-architecture-refresh.md
test "$(rg -F -o '### 3. Align OAuth login cookie transport flags' docs/superpowers/audits/2026-07-21-maintainability-architecture-refresh.md | wc -l)" = "2"
if rg -n 'Core F9.*maintainer-validated|[Rr]ank 3 (is )?implemented and maintainer-validated' docs/superpowers/audits/2026-07-21-maintainability-architecture-refresh.md; then exit 1; fi
git diff --check -- docs/superpowers/specs/2026-07-22-oauth-login-cookie-transport-design.md docs/superpowers/plans/2026-07-22-oauth-login-cookie-transport.md docs/superpowers/audits/2026-07-21-maintainability-architecture-refresh.md
```

Then manually inspect the matched audit paragraphs. Expected: rank 3 is implemented awaiting validation, its proxy limitation is explicit, rank 4 has not been promoted, and no runtime result is claimed.

- [ ] **Step 5: Commit the implementation record**

```bash
git add docs/superpowers/specs/2026-07-22-oauth-login-cookie-transport-design.md docs/superpowers/plans/2026-07-22-oauth-login-cookie-transport.md docs/superpowers/audits/2026-07-21-maintainability-architecture-refresh.md
git diff --cached --check
test "$(git diff --cached --name-only | wc -l)" = "3"
git commit -m "docs: record OAuth cookie transport alignment"
```

---

### Task 3: Complete whole-project review and maintainer handoff

**Files:**
- Review: `src/Pages/GoogleOAuthVerify.cshtml`
- Review: `src/WebAPI/BasicAPIFeatures.cs`
- Review: `src/Utils/WebUtil.cs`
- Review: `src/Core/WebServer.cs`
- Review: the approved design, implementation plan, and Core F9/rank-3 audit records
- Modify: only an approved file if review proves a defect

- [ ] **Step 1: Run independent specification review**

Review the complete implementation range and prove:

- the OAuth writer uses `HttpContext.Request.IsHttps`;
- password login and logout retain their existing `context.Request.IsHttps` decisions;
- cookie name/value, HttpOnly, expiry, SameSite, session/token behavior, logs, redirects, registration, readers, and protected consumers are unchanged;
- HTTP behavior remains compatible when `IsHttps` is false;
- no raw forwarding header is trusted and no proxy/Web-pipeline behavior changed;
- only the approved production file and documentation changed; and
- documentation does not claim maintainer compilation or runtime validation.

Correct and re-review every Critical or Important finding before continuing.

- [ ] **Step 2: Run independent code-quality/security review**

Review property ordering and C# conventions, request-context ownership, all maintained cookie append/clear sites, logout symmetry, reverse-proxy interpretation, accidental neighboring Razor changes, dirty-file overlap, and exact committed scope. Correct and re-review every Critical or Important finding.

- [ ] **Step 3: Run final agent-permitted static verification**

Run:

```bash
test "$(rg -F -o 'Secure = HttpContext.Request.IsHttps' src/Pages/GoogleOAuthVerify.cshtml | wc -l)" = "1"
test "$(rg -F -o 'Secure = context.Request.IsHttps' src/WebAPI/BasicAPIFeatures.cs | wc -l)" = "2"
test "$(rg -F -o 'Response.Cookies.Append("swarm_token"' src/Pages/GoogleOAuthVerify.cshtml src/WebAPI/BasicAPIFeatures.cs | wc -l)" = "3"
rg -n 'swarm_token|GetValidLogin|GetSwarmTokenFor|IsHttps|UseForwardedHeaders|ForwardedHeadersOptions' src/Pages/GoogleOAuthVerify.cshtml src/WebAPI/BasicAPIFeatures.cs src/Utils/WebUtil.cs src/Core/WebServer.cs
git diff --check 74bf9306^..2361d504
git diff --name-only 74bf9306^..2361d504
git status --short --branch --untracked-files=no
git log --oneline --decorate -8
```

Verify the committed path inventory has exactly these four unique paths: `src/Pages/GoogleOAuthVerify.cshtml`, `docs/superpowers/specs/2026-07-22-oauth-login-cookie-transport-design.md`, `docs/superpowers/plans/2026-07-22-oauth-login-cookie-transport.md`, and `docs/superpowers/audits/2026-07-21-maintainability-architecture-refresh.md`. Confirm the known four maintainer-owned tracked modifications remain untouched.

Do not inspect the backup directory.

- [ ] **Step 4: Hand off maintainer compilation and runtime validation**

Ask maintainer Reaper176 to use their normal build/launch workflow and validate:

1. HTTP OAuth login: cookie remains usable, protected pages/API sessions work, and logout removes the session.
2. Direct HTTPS OAuth login: `swarm_token` is Secure, the session persists, protected pages/API sessions work, and logout succeeds.
3. Password login/logout over the same HTTP and HTTPS paths: behavior remains unchanged.
4. Supported HTTPS proxy: record whether Swarm observes the callback as HTTPS; require a Secure OAuth cookie when it does. If Swarm observes HTTP, record the trusted scheme-establishment limitation for a separate project.
5. OAuth registration and redirects: behavior remains unchanged.

Do not mark rank 3 maintainer-validated or advance the roadmap until the maintainer confirms the applicable matrix.

**Rollback:** remove only `Secure = HttpContext.Request.IsHttps` from the OAuth cookie initializer. Do not change cookie/token/session contracts or add unreviewed forwarding-header trust as a workaround.
