# OAuth Login Cookie Transport Design

**Date:** 2026-07-22

**Status:** Implemented; awaiting maintainer validation

## Goal

Align the Google OAuth login cookie's transport-security decision with the existing password-login and logout behavior. An OAuth login handled as HTTPS must emit the long-lived `swarm_token` cookie with the `Secure` attribute, while an HTTP login must retain its current compatibility behavior.

This project addresses rank 3, Core F9, from the maintainability architecture refresh.

## Confirmed Boundary

`src/Pages/GoogleOAuthVerify.cshtml` is the sole maintained Google OAuth login-cookie writer. After Google credential verification finds an existing registered account, the page creates the same persistent login-session token used by password login and appends `swarm_token` with `HttpOnly`, a one-year expiry, and `SameSite=Lax`. Before production commit `c6de780d`, the writer omitted `CookieOptions.Secure`, whose default is `false`; the implemented writer now sets `Secure = HttpContext.Request.IsHttps`.

`BasicAPIFeatures.Login` writes the equivalent password-login cookie with `Secure = context.Request.IsHttps`. `BasicAPIFeatures.Logout` clears the cookie using the same secure decision. `WebUtil.GetValidLogin` and its protected-page, output-route, and API-session consumers read and validate the cookie without depending on how the login was performed.

The confirmed production change is limited to the OAuth writer's `CookieOptions` initializer.

## Chosen Design

Add `Secure = HttpContext.Request.IsHttps` to the existing `swarm_token` options in `GoogleOAuthVerify.cshtml`.

This deliberately mirrors the current password-login and logout pattern rather than introducing a new policy or abstraction. The request's established ASP.NET scheme interpretation remains the single decision source:

- when `HttpContext.Request.IsHttps` is `true`, the OAuth cookie is restricted to secure transport;
- when it is `false`, the OAuth cookie remains usable over HTTP, preserving local and existing non-TLS deployments.

The cookie name, plaintext wire token, stored hashed session representation, `HttpOnly`, expiry, `SameSite`, redirect, logging, and all OAuth verification and registration behavior remain unchanged.

## HTTPS and Reverse-Proxy Interpretation

The design treats `Request.IsHttps` exactly as the existing password-login and logout paths do. Direct HTTPS, or a deployment whose trusted hosting configuration correctly establishes the ASP.NET request scheme as HTTPS, receives a Secure OAuth cookie.

Swarm's maintained Web pipeline does not currently install ASP.NET forwarded-header middleware. A TLS-terminating reverse proxy that forwards HTTP to Swarm therefore must not be assumed to make `Request.IsHttps` true merely by sending an unprocessed forwarding header. Adding or changing forwarded-header trust, known-proxy configuration, scheme rewriting, TLS hosting, or proxy documentation is outside this project. Those changes affect a broader security boundary and require a separate design.

For this project, a “supported HTTPS reverse proxy” means a deployment in which the trusted hosting/proxy arrangement already causes Swarm to observe the callback request as HTTPS. Validation must record the observed `Request.IsHttps`/cookie outcome rather than assuming it from the browser-facing URL alone.

## Considered Alternatives

### Shared cookie-options helper

A helper could centralize the password-login, OAuth-login, and logout options. This is not selected because the immediate mismatch is one missing property, logout has intentionally different lifetime semantics, and an abstraction would enlarge the change without improving the current contract.

### Forwarded-header middleware in the same change

The Web pipeline could process `X-Forwarded-Proto` or standardized forwarding headers before cookie creation. This is not selected because trusting forwarded headers requires explicit proxy/network boundaries and changes scheme interpretation for the entire application. It is not a safe incidental addition to the OAuth fix.

### Always-Secure OAuth cookie

The OAuth writer could set `Secure = true` unconditionally. This is not selected because it would diverge from password login and make OAuth sessions unusable on intentional HTTP deployments, including local configurations.

## Compatibility Requirements

- Preserve the `swarm_token` cookie name and token format.
- Preserve `HttpOnly = true`, the one-year OAuth-login expiry, and `SameSite = SameSiteMode.Lax`.
- Preserve persistent login-session creation, hashing, database records, validation, and revocation.
- Preserve Google credential verification, registered-account lookup, OAuth registration flow, logs, and redirects.
- Preserve password login and logout behavior without editing their owners.
- Preserve HTTP login compatibility when `Request.IsHttps` is false.
- Preserve all cookie readers and protected-route behavior.
- Do not infer HTTPS directly from untrusted request headers.
- Do not add proxy trust, forwarded-header, Kestrel TLS, or deployment configuration.

The only intentional behavior change is that the OAuth writer emits `Secure` when ASP.NET identifies the callback request as HTTPS.

## Files and Ownership

- Production owner: `src/Pages/GoogleOAuthVerify.cshtml`
- Existing comparison writers: `src/WebAPI/BasicAPIFeatures.cs`
- Existing cookie reader: `src/Utils/WebUtil.cs`
- Architecture record: `docs/superpowers/audits/2026-07-21-maintainability-architecture-refresh.md`

Only the production owner and project documentation are expected to change. No JavaScript, CSS, API schema, token/session model, settings, launcher, generated file, extension, or backend code is included.

## Static Verification

Repository policy prohibits agents from running builds, automated tests, browsers, servers, backends, or launchers. Static verification will:

1. inventory every maintained `swarm_token` append and clear site;
2. confirm OAuth login, password login, and logout all base `Secure` on their request's `IsHttps` value;
3. compare the OAuth options before and after the change and prove no other property changed;
4. trace the OAuth token through session creation, cookie writing, `WebUtil.GetValidLogin`, and protected consumers;
5. confirm no forwarded-header, proxy, Kestrel, session, token, redirect, registration, or reader code changed;
6. inspect the exact approved-file diff; and
7. run repository-permitted whitespace and static search checks.

## Maintainer Validation

The maintainer should inspect browser cookie attributes and complete the relevant flows in these environments:

1. HTTP: complete OAuth login and confirm the session persists with behavior matching password login; then log out and confirm the session is removed.
2. Direct HTTPS: complete OAuth login and confirm `swarm_token` is marked Secure and the session persists; then validate password login and logout still behave unchanged.
3. Supported HTTPS proxy: first confirm whether Swarm observes the callback as HTTPS under the trusted deployment configuration, then complete OAuth login and inspect the cookie. If Swarm observes HTTPS, the cookie must be Secure. If it observes HTTP, record that as a proxy scheme-establishment limitation for the separate proxy project rather than weakening or broadening this fix.

Across all environments, confirm existing OAuth users can log in, protected pages and API sessions accept the resulting cookie, page redirects remain correct, and registration behavior is unchanged.

## Non-Goals

- No change to token generation, hashing, storage, expiry, rotation, validation, or revocation.
- No change to cookie name, value, `HttpOnly`, `SameSite`, lifetime, domain, or path.
- No shared cookie framework or general authentication refactor.
- No OAuth provider, credential-verification, account-linking, registration, or redirect change.
- No password-login, logout, cookie-reader, protected-route, or API-session change.
- No forwarded-header middleware, trusted-proxy policy, scheme rewriting, native TLS hosting, or launcher change.
- No attempt to classify proxy requests from raw forwarding headers in the Razor page.

## Success Criteria

- The OAuth writer uses `Secure = HttpContext.Request.IsHttps`, matching the established password-login and logout decision.
- OAuth callbacks observed as HTTPS emit a Secure `swarm_token` cookie.
- OAuth callbacks observed as HTTP retain current cookie usability.
- Cookie contents, lifetime, SameSite/HttpOnly flags, sessions, redirects, registration, readers, and protected consumers remain unchanged.
- The production diff is isolated to the OAuth cookie options initializer.

## Risks and Rollback

The primary deployment risk is a TLS-terminating proxy whose browser-facing request is HTTPS but whose Swarm-facing request is HTTP. In that configuration `Request.IsHttps` remains false unless trusted hosting infrastructure establishes the forwarded scheme. This project intentionally matches existing password-login behavior and does not claim to solve that broader trust problem.

The change is a single reversible cookie-option addition. Rollback removes the `Secure` assignment from the OAuth writer, although doing so restores the confirmed HTTPS transport-flag mismatch.
