# Redact Shared Browser API Diagnostics Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Stop the shared browser HTTP error path from serializing request payload values into the console while preserving its complete transport and error-handling contract.

**Architecture:** Make one deletion inside `src/wwwroot/js/site.js::genericRequest`: remove the console statement that serializes `in_data`, leaving the adjacent endpoint/server-error diagnostic and `fail(data.error)` path unchanged. No formatter, caller migration, server change, or transport refactor is introduced.

**Tech Stack:** Browser JavaScript using SwarmUI's existing classic-script conventions; Git and ripgrep for static verification.

**Execution context:** Work directly on `master` as requested by the maintainer. Do not create a worktree. Preserve the existing modified files and the untracked `Data.pre-restore-2026-07-19/` directory; never inspect or stage that directory.

**Repository verification constraint:** `AGENTS.md` prohibits agents from running builds, automated tests, browsers, or the live application. This plan therefore uses static checks followed by explicit maintainer-run validation.

---

### Task 1: Remove shared request-payload logging

**Files:**
- Modify: `src/wwwroot/js/site.js:207-211`
- Reference: `docs/superpowers/specs/2026-07-21-redact-browser-api-diagnostics-design.md`

- [ ] **Step 1: Confirm the protected working state and exact implementation baseline**

Run:

```bash
git status --short --branch --untracked-files=normal
git diff -- src/wwwroot/js/site.js
sed -n '165,215p' src/wwwroot/js/site.js
```

Expected:

- `master` is the active branch.
- The maintainer's existing changes remain visible but do not include `src/wwwroot/js/site.js`.
- `git diff -- src/wwwroot/js/site.js` has no output.
- `genericRequest` has the exact signature `genericRequest(url, in_data, callback, depth = 0, errorHandle = null, timeoutMs = null)`.
- Its `data.error` branch contains both the endpoint/error log and `console.log(`Input was ${JSON.stringify(in_data)}`);`.

If `site.js` is already modified, stop and reconcile ownership with the maintainer before editing it.

- [ ] **Step 2: Establish the static disclosure baseline**

Run:

```bash
git grep -n -E 'console\.(log|error).*JSON\.stringify\(in_data\)|Input was.*JSON\.stringify\(in_data\)' -- src ':!src/Extensions/**'
git grep -n 'genericRequest(' -- 'src/**/*.js' ':!src/Extensions/**' | wc -l
```

Expected:

```text
src/wwwroot/js/site.js:209:            console.log(`Input was ${JSON.stringify(in_data)}`);
175
```

The line number may move if unrelated upstream changes precede the function, but there must be exactly one matching diagnostic. The maintained occurrence count includes the definition and callers; it is evidence of the shared compatibility boundary, not a target for caller edits.

- [ ] **Step 3: Apply the minimal implementation**

Use `apply_patch` to change only this branch:

```js
        if (data.error) {
            console.log(`Tried making generic request ${url} but failed with error: ${data.error}`);
            fail(data.error);
            return;
        }
```

Do not change the function signature, `session_id` mutation, `sendJsonToServer`, retry branches, recursive argument order, `fail`, callbacks, timeout handling, or WebSocket code.

- [ ] **Step 4: Verify the diff and absence of client-added payload diagnostics**

Run:

```bash
git diff --check -- src/wwwroot/js/site.js
git diff -- src/wwwroot/js/site.js
if rg -n 'Input was.*JSON\.stringify\(in_data\)|console\.(log|error).*JSON\.stringify\(in_data\)' src/wwwroot/js src/BuiltinExtensions --glob '*.js' -g '!src/Extensions/**'; then exit 1; fi
rg -n '^function genericRequest\(url, in_data, callback, depth = 0, errorHandle = null, timeoutMs = null\)' src/wwwroot/js/site.js
rg -n 'genericRequest\(url, in_data, callback, depth \+ 1, errorHandle, timeoutMs\)|sendJsonToServer\(`API/\$\{url\}`|Tried making generic request.*failed with error|fail\(data\.error\)' src/wwwroot/js/site.js
```

Expected:

- The diff removes exactly one console statement and changes no other production line.
- The negative diagnostic scan exits successfully without output.
- The exact six-argument signature is present once.
- Both recursive calls retain all six positional semantics.
- The request call, endpoint/server-error diagnostic, and `fail(data.error)` remain present.

The unchanged `socket.send(JSON.stringify(in_data))` transport serialization is expected and must not be removed.

- [ ] **Step 5: Verify scope and commit the production change**

Run:

```bash
git status --short --untracked-files=normal
git add src/wwwroot/js/site.js
git diff --cached --check
git diff --cached --stat
git diff --cached -- src/wwwroot/js/site.js
test "$(git diff --cached --name-only)" = "src/wwwroot/js/site.js"
git commit -m "fix: redact browser API request diagnostics"
```

Expected:

- The staged diff contains only `src/wwwroot/js/site.js`.
- The commit is a one-line deletion.
- The maintainer's pre-existing modified files and backup directory remain unstaged and untouched.

### Task 2: Run independent static review and hand off runtime validation

**Files:**
- Review: `src/wwwroot/js/site.js`
- Review: the Task 1 commit
- Modify: none unless review finds a verified defect

- [ ] **Step 1: Review the committed change against the design**

Review the Task 1 commit and confirm:

- only the client payload diagnostic was removed;
- endpoint identity and verbatim `data.error` logging remain;
- `fail(data.error)` and the return remain;
- the exact six-argument signature and both recursive calls are unchanged;
- network, timeout, missing-response, impersonation, invalid-session, callback, UI, and WebSocket paths are unchanged;
- no caller migration or server change occurred; and
- the implementation does not add a formatter, field list, logging abstraction, or structural summary.

If a verified defect is found, correct only `src/wwwroot/js/site.js` with `apply_patch`, repeat Task 1 Step 4, and commit:

```bash
git add src/wwwroot/js/site.js
git diff --cached --check
git commit -m "fix: correct browser API diagnostic redaction"
```

- [ ] **Step 2: Run final agent-permitted static verification**

Run:

```bash
if rg -n 'Input was.*JSON\.stringify\(in_data\)|console\.(log|error).*JSON\.stringify\(in_data\)' src/wwwroot/js src/BuiltinExtensions --glob '*.js' -g '!src/Extensions/**'; then exit 1; fi
git diff --check
git status --short --branch --untracked-files=normal
git log --oneline --decorate -5
```

Expected:

- No client diagnostic serializes `in_data`.
- Whitespace checks pass.
- `master` contains the implementation commit.
- The pre-existing dirty files and backup directory remain present and unstaged.

Do not run a build, automated test, browser, live server, launcher, installer, or backend process.

- [ ] **Step 3: Hand off maintainer validation**

Ask the maintainer to validate these live scenarios:

1. Reject an invalid API key and confirm the client console adds no key or session ID.
2. Trigger an ordinary HTTP API error and confirm endpoint text, verbatim server error, UI handling, and callback handling remain unchanged.
3. Trigger an image- or metadata-bearing request error and confirm submitted payload content is not added by the client diagnostic.
4. Request an invalid Image History sort mode and confirm the server-provided value still appears, proving `data.error` remains verbatim.
5. Exercise invalid-session retry and a network or timeout failure and confirm their behavior remains unchanged.

Do not claim runtime completion until the maintainer reports these scenarios pass. If validation fails, collect the exact endpoint, console output, callback/UI behavior, and reproduction steps before proposing a correction.
