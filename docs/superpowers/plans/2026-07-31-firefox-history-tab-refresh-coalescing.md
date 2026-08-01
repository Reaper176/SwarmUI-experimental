# Firefox History-Tab Refresh Coalescing Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use
> superpowers:subagent-driven-development (recommended) or
> superpowers:executing-plans to implement this plan task-by-task. Steps use
> checkbox (`- [ ]`) syntax for tracking.

**Goal:** Coalesce Firefox's settling History-tab scroll with the explicit
tab-show visible-window refresh while preserving existing Chromium, scroll,
resize, build, and hydration behavior.

**Architecture:** Keep the window manager's leading-edge animation-frame
coalescer unchanged. Add an opt-out for the immediate update performed by
`attach`, then use that mode only on History-tab show and defer the explicit
update by one frame so browser layout events and the explicit request share the
same queued update.

**Tech Stack:** Browser JavaScript, Bootstrap tab events, animation frames,
Playwright Firefox, Puppeteer Chromium, Bash, .NET 8.

---

### Task 1: Preserve the Firefox regression as the red test

**Files:**
- Test: `/tmp/swarmui-ranks-1-32-BQ5HdB/browser/rank31-post-browser-harness.js`
- Source: `src/wwwroot/js/genpage/gentab/outputhistory.js`

- [ ] **Step 1: Verify the disposable harness is back to its recorded source**

Run:

```bash
sha256sum /tmp/swarmui-ranks-1-32-BQ5HdB/browser/rank31-post-browser-harness.js
```

Expected SHA-256:
`12df63736d516f30aa111ce67b9d64e2877c4a5e364f0ffb6bb9a6570bae9157`.

- [ ] **Step 2: Start the unchanged validation server**

Run from `/tmp/swarmui-ranks-1-32-BQ5HdB/source`:

```bash
dotnet /tmp/swarmui-ranks-1-32-BQ5HdB/publish/SwarmUI.dll \
  --data_dir /tmp/swarmui-ranks-1-32-BQ5HdB/browser-data \
  --host 127.0.0.1 --port 17844 --launch_mode none \
  --no_persist true --loglevel warning
```

Expected: `http://127.0.0.1:17844/Text2Image` responds successfully.

- [ ] **Step 3: Run the Firefox regression and verify RED**

Run:

```bash
node /tmp/swarmui-ranks-1-32-BQ5HdB/browser/rank31-post-browser-harness.js
```

Expected: exit 1 with
`RANK31_POST_RESULT assertions=16 failures=1` and
`tab invocations 2`. Stop the server normally afterward.

### Task 2: Implement the scoped coalescing change

**Files:**
- Modify: `src/wwwroot/js/genpage/gentab/outputhistory.js:17-32`
- Modify: `src/wwwroot/js/genpage/gentab/outputhistory.js:1599-1607`

- [ ] **Step 1: Let attachment skip its immediate update when requested**

Change the manager method to preserve its default while guarding both update
sites:

```javascript
    /** Attaches the visible-window manager to content and optionally queues an update. */
    attach(content, queueUpdate = true) {
        if (this.content == content) {
            if (queueUpdate) {
                this.queueUpdate();
            }
            return;
        }
        if (this.content) {
            this.content.removeEventListener('scroll', this.boundScroll);
        }
        this.content = content;
        if (this.content) {
            this.content.addEventListener('scroll', this.boundScroll);
        }
        window.removeEventListener('resize', this.boundResize);
        window.addEventListener('resize', this.boundResize);
        if (queueUpdate) {
            this.queueUpdate();
        }
    }
```

- [ ] **Step 2: Defer only the tab-show update**

Replace the three visibility-manager calls inside the existing content guard
with:

```javascript
            browserUtil.queueMakeVisible(historyContent);
            this.windowManager.attach(historyContent, false);
            let queueUpdate = () => this.windowManager.queueUpdate();
            if (window.requestAnimationFrame) {
                requestAnimationFrame(queueUpdate);
            }
            else {
                setTimeout(queueUpdate, 16);
            }
```

- [ ] **Step 3: Run static syntax and whitespace checks**

Run:

```bash
node --check src/wwwroot/js/genpage/gentab/outputhistory.js
git diff --check
git diff -- src/wwwroot/js/genpage/gentab/outputhistory.js
```

Expected: syntax and whitespace checks exit 0; the diff contains only the
approved attachment flag and deferred tab-show scheduling.

### Task 3: Verify green in Firefox and Chromium

**Files:**
- Test: `/tmp/swarmui-ranks-1-32-BQ5HdB/browser/rank31-post-browser-harness.js`
- Test: `/tmp/swarmui-ranks-1-32-BQ5HdB/browser/rank31-post-chromium.js`
- Test: `/tmp/swarmui-ranks-1-32-BQ5HdB/browser/rank30-post-browser-harness.js`
- Test: `/tmp/swarmui-ranks-1-32-BQ5HdB/browser/core-firefox.js`

- [ ] **Step 1: Start the server from the fixed worktree root**

Run from the validation worktree:

```bash
dotnet /tmp/swarmui-ranks-1-32-BQ5HdB/publish/SwarmUI.dll \
  --data_dir /tmp/swarmui-ranks-1-32-BQ5HdB/browser-data \
  --host 127.0.0.1 --port 17844 --launch_mode none \
  --no_persist true --loglevel warning
```

Expected: the server uses the changed maintained JavaScript from the worktree.

- [ ] **Step 2: Verify the focused Firefox and Chromium tests are green**

Run sequentially:

```bash
node /tmp/swarmui-ranks-1-32-BQ5HdB/browser/rank31-post-browser-harness.js
node /tmp/swarmui-ranks-1-32-BQ5HdB/browser/rank31-post-chromium.js
```

Expected from each: `assertions=16 failures=0`.

- [ ] **Step 3: Verify adjacent browser behavior**

Run sequentially:

```bash
node /tmp/swarmui-ranks-1-32-BQ5HdB/browser/rank30-post-browser-harness.js
node /tmp/swarmui-ranks-1-32-BQ5HdB/browser/core-firefox.js
```

Expected: rank 30 reports `assertions=19 failures=0`; the core Firefox result
contains 17 passed assertions and no failures or unexpected page errors. Stop
the server normally afterward.

- [ ] **Step 4: Commit the production fix**

```bash
git add src/wwwroot/js/genpage/gentab/outputhistory.js
git commit -m "fix: coalesce Firefox history tab refresh"
```

### Task 4: Rerun the complete available rank validation

**Files:**
- Read: `/tmp/swarmui-ranks-1-32-BQ5HdB/static-contracts.sh`
- Test: `/tmp/swarmui-ranks-1-32-BQ5HdB/harnesses/core/CoreContracts.csproj`
- Test: `/tmp/swarmui-ranks-1-32-BQ5HdB/harnesses/rank26/Rank26.csproj`
- Test: `/tmp/swarmui-ranks-1-32-BQ5HdB/harnesses/rank27/Rank27.csproj`
- Test: `/tmp/swarmui-ranks-1-32-BQ5HdB/harnesses/rank28/Rank28.csproj`
- Test: `/tmp/swarmui-ranks-1-32-BQ5HdB/harnesses/rank29/Rank29.csproj`
- Test: `/tmp/swarmui-ranks-1-32-BQ5HdB/harnesses/rank32/Rank32.csproj`

- [ ] **Step 1: Archive and publish the fixed committed source externally**

Run from the validation worktree:

```bash
fixed_root="$(mktemp -d /tmp/swarmui-ranks-1-32-fixed-XXXXXX)"
mkdir -p "$fixed_root/source"
git archive HEAD | tar -x -C "$fixed_root/source"
dotnet publish "$fixed_root/source/src/SwarmUI.csproj" -c Release \
  -p:BaseIntermediateOutputPath="$fixed_root/obj/" \
  -p:OutputPath="$fixed_root/bin/" \
  -o "$fixed_root/publish" \
  2>&1 | tee "$fixed_root/build.log"
```

Expected: publish exits 0 with zero warnings and zero errors. Do not use or
modify the primary checkout's data, models, output, extensions, `src/bin`, or
`src/obj`.

- [ ] **Step 2: Run all static and synthetic suites against the fixed source**

Run with `fixed_root` retained from Step 1:

```bash
cp /tmp/swarmui-ranks-1-32-BQ5HdB/static-contracts.sh \
  "$fixed_root/static-contracts.sh"
perl -pi -e "s#/tmp/swarmui-ranks-1-32-BQ5HdB#${fixed_root}#g" \
  "$fixed_root/static-contracts.sh"
bash "$fixed_root/static-contracts.sh"

cp -a /tmp/swarmui-ranks-1-32-BQ5HdB/harnesses "$fixed_root/harnesses"
find "$fixed_root/harnesses" -type d \( -name bin -o -name obj \) \
  -prune -exec rm -rf -- {} +
rg -l -0 '/tmp/swarmui-ranks-1-32-BQ5HdB' "$fixed_root/harnesses" \
  | xargs -0 -r perl -pi -e \
  "s#/tmp/swarmui-ranks-1-32-BQ5HdB#${fixed_root}#g"
dotnet run --project "$fixed_root/harnesses/core/CoreContracts.csproj" -c Release
dotnet run --project "$fixed_root/harnesses/rank26/Rank26.csproj" -c Release
dotnet run --project "$fixed_root/harnesses/rank27/Rank27.csproj" -c Release
dotnet run --project "$fixed_root/harnesses/rank28/Rank28.csproj" -c Release
dotnet run --project "$fixed_root/harnesses/rank29/Rank29.csproj" -c Release
dotnet run --project "$fixed_root/harnesses/rank32/Rank32.csproj" -c Release
```

Expected: static `64/0`, core `43/0`, rank 26 passed, rank 27 `19/0`, rank 28
`18/0`, rank 29 `38/0`, and rank 32 `29/0`.

- [ ] **Step 3: Rerun the full browser set from the fixed archive**

Start the server from `$fixed_root/source`:

```bash
dotnet "$fixed_root/publish/SwarmUI.dll" \
  --data_dir /tmp/swarmui-ranks-1-32-BQ5HdB/browser-data \
  --host 127.0.0.1 --port 17844 --launch_mode none \
  --no_persist true --loglevel warning
```

In a second shell, run sequentially:

```bash
node /tmp/swarmui-ranks-1-32-BQ5HdB/browser/core-firefox.js
node /tmp/swarmui-ranks-1-32-BQ5HdB/browser/rank30-post-browser-harness.js
node /tmp/swarmui-ranks-1-32-BQ5HdB/browser/rank31-post-browser-harness.js
node /tmp/swarmui-ranks-1-32-BQ5HdB/browser/rank31-post-chromium.js
```

Expected: core Firefox `17/0`, rank 30 `19/0`, and both rank-31 browsers
`16/0`. Stop the server normally.

### Task 5: Update the audit and verify repository boundaries

**Files:**
- Modify: `docs/superpowers/audits/2026-07-31-ranks-1-32-validation.md`

- [ ] **Step 1: Record the resolved result without overstating coverage**

Update the audit aggregate from `298 passed, 1 failed` to
`299 passed, 0 failed`, and rank 31 from `33/1` to `34/0`. Record the root
cause, production commit, Firefox and Chromium passing results, and fresh
evidence root. Leave every Windows, OAuth, GPU/model, heterogeneous-backend,
external-extension, and other environment-dependent case marked unrun.

- [ ] **Step 2: Run final documentation and boundary checks**

Run:

```bash
git diff --check
git status --short
git log -3 --oneline
```

Expected: maintained production changes are limited to
`outputhistory.js`; task documentation is limited to the approved spec, plan,
and audit. The primary checkout's pre-existing dirty-state list is unchanged.

- [ ] **Step 3: Commit the audit update**

```bash
git add docs/superpowers/audits/2026-07-31-ranks-1-32-validation.md
git commit -m "docs: record Firefox history refresh validation"
```
