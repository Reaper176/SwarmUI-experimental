# Backend Persistence Generation Acknowledgement Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make configured-backend persistence acknowledge exact mutation generations, retry after transient failures, and contain final-save failures without changing backend API timing or lifecycle behavior.

**Architecture:** `BackendHandler` owns monotonic mutation and saved generations behind the existing `BackendsEdited` compatibility surface. Its serialized save owner captures one generation, writes the unchanged real-backend FDS snapshot, classifies authoritative commit outcomes, and acknowledges only that captured generation; `Program` polls this owner every ten seconds, while shutdown makes one contained final attempt.

**Tech Stack:** C# 12, .NET 8, `ConcurrentDictionary`, `Interlocked`, `Volatile`, FreneticDataSyntax FDS persistence, existing SwarmUI logging and lifecycle owners.

---

## Repository Execution Constraints

- Work directly on `master`; maintainer Reaper176 explicitly declined a worktree.
- Do not run builds, tests, launchers, servers, browsers, backends, installers, or live storage mutations. `AGENTS.md` reserves runtime verification for the maintainer.
- Use static inspection, `rg`, `git diff --check`, and staged-diff review only.
- Preserve the existing user-owned modifications in:
  - `src/Data/Settings.fds`
  - `src/Pages/Text2Image.cshtml`
  - `src/wwwroot/js/genpage/gentab/loras.js`
  - `src/wwwroot/js/genpage/main.js`
- Never inspect or modify `Data.pre-restore-2026-07-19/`.
- Stage only the files named by each task.

## File Structure

- Modify `src/Backends/BackendHandler.cs`: own generation state, compatibility facade, mutation publication, save result/classification, and final shutdown persistence.
- Modify `src/WebAPI/BackendAPI.cs`: publish the already-visible toggle through the explicit generation owner.
- Modify `src/Core/Program.cs`: replace clear-before-save with result-observing periodic retries.
- Modify `docs/superpowers/specs/2026-07-23-backend-persistence-generation-acknowledgement-design.md`: record implementation and validation status without changing the approved design.
- Modify `docs/superpowers/audits/2026-07-21-maintainability-architecture-refresh.md`: reconcile rank 5 and advance the roadmap only after maintainer validation.

Automated tests are intentionally absent because this repository prohibits agents from running any form of testing. Each production task instead has a narrow static proof, and Task 6 provides the required maintainer runtime matrix.

### Task 1: Establish the generation state and compatibility surface

**Files:**
- Modify: `src/Backends/BackendHandler.cs:45-58`

- [ ] **Step 1: Replace the Boolean field with generation ownership and the save-result contract**

Replace the current `BackendsEdited` field and extend the persistence state immediately before `SaveFilePath`:

```csharp
    /// <summary>The latest generation containing a persistence-relevant backend mutation.</summary>
    private long BackendMutationGeneration = 0;

    /// <summary>The latest backend mutation generation represented by the authoritative save file.</summary>
    private long BackendSavedGeneration = 0;

    /// <summary>Whether persistence-relevant backend mutations are waiting to be saved.</summary>
    public bool BackendsEdited
    {
        get
        {
            long savedGeneration = Volatile.Read(ref BackendSavedGeneration);
            return Volatile.Read(ref BackendMutationGeneration) > savedGeneration;
        }
        set
        {
            if (value)
            {
                MarkBackendsEdited();
            }
        }
    }

    /// <summary>Publishes a persistence-relevant backend mutation after it becomes visible.</summary>
    internal void MarkBackendsEdited()
    {
        Interlocked.Increment(ref BackendMutationGeneration);
    }

    /// <summary>Possible outcomes of an observable configured-backend save request.</summary>
    public enum BackendSaveResult
    {
        /// <summary>No backend mutations are waiting to be saved.</summary>
        NoChanges,

        /// <summary>The captured generation was saved and no newer mutation is pending.</summary>
        Saved,

        /// <summary>The captured generation was saved, but a newer mutation remains pending.</summary>
        SavedWithNewerChangesPending,

        /// <summary>The authoritative backend file was not confirmed and nothing was acknowledged.</summary>
        Failed
    }
```

Keep `SaveFilePath` and `SaveLock` at their current locations. Do not initialize generations in `Load`; both zero values intentionally represent the loaded baseline.

Read the saved generation before the mutation generation. A concurrent acknowledgement can then cause only a conservative `true`, while a mutation published between the reads is still observed and cannot be transiently hidden.

- [ ] **Step 2: Prove the compatibility semantics statically**

Run:

```bash
rg -n "BackendMutationGeneration|BackendSavedGeneration|public bool BackendsEdited|set|MarkBackendsEdited|enum BackendSaveResult" src/Backends/BackendHandler.cs
rg -n "public bool BackendsEdited =|BackendsEdited = false" src/Backends/BackendHandler.cs
git diff --check -- src/Backends/BackendHandler.cs
```

Expected:

- the first command shows both counters, the property, true-only setter path, publication method, and result enum;
- the second command returns no matches; and
- `git diff --check` returns no output.

- [ ] **Step 3: Review and commit only the generation contract**

Run:

```bash
git diff -- src/Backends/BackendHandler.cs
git status --short --untracked-files=no
git add -- src/Backends/BackendHandler.cs
git diff --cached --check
git commit -m "refactor: establish backend persistence generations"
```

Expected: the commit contains only `src/Backends/BackendHandler.cs`; the four user-owned tracked modifications remain unstaged.

### Task 2: Publish real mutations after visibility

**Files:**
- Modify: `src/Backends/BackendHandler.cs:375-475`
- Modify: `src/WebAPI/BackendAPI.cs:626-646`

- [ ] **Step 1: Move add and delete publication after successful catalog mutation**

In `AddNewOfType`, remove the leading `BackendsEdited = true;` and publish after the `CentralLock` block:

```csharp
        lock (CentralLock)
        {
            data.ID = LastBackendID++;
            AllBackends.TryAdd(data.ID, data);
        }
        MarkBackendsEdited();
        DoInitBackend(data);
```

Leave `AddNewNonrealBackend` unchanged.

Replace `DeleteById` with:

```csharp
    /// <summary>Shutdown and delete a given backend.</summary>
    public async Task<bool> DeleteById(int id)
    {
        if (!AllBackends.TryRemove(id, out BackendData data))
        {
            return false;
        }
        if (data.AbstractBackend.IsReal)
        {
            MarkBackendsEdited();
        }
        await ShutdownBackendCleanly(data);
        ReassignLoadedModelsList();
        return true;
    }
```

This retains the existing ID allocation and lifecycle ordering while ensuring missing and non-real removals publish nothing.

- [ ] **Step 2: Guard edit publication across partially applied mutations**

Replace the mutation body after `await ShutdownBackendCleanly(data);` and before `return data;` with:

```csharp
        bool persistenceMutationAttempted = false;
        try
        {
            if (new_id >= 0)
            {
                if (!AllBackends.TryAdd(new_id, data))
                {
                    throw new SwarmReadableErrorException($"Backend new ID {new_id} is already in use!");
                }
                persistenceMutationAttempted = true;
                data.ID = new_id;
                AllBackends.TryRemove(id, out _);
            }
            newSettings = data.AbstractBackend.SettingsRaw.ExcludeSecretValuesThatMatch(newSettings, "\t<secret>");
            persistenceMutationAttempted = true;
            data.AbstractBackend.SettingsRaw.Load(newSettings);
            Logs.Verbose($"Settings applied, now: {data.AbstractBackend.SettingsRaw.Save(true)}");
            if (title is not null)
            {
                data.AbstractBackend.Title = title;
            }
        }
        finally
        {
            if (persistenceMutationAttempted && data.AbstractBackend.IsReal)
            {
                MarkBackendsEdited();
            }
        }
        data.ModCount++;
        DoInitBackend(data);
```

The flag is set only after an ID insertion is visible or immediately before the settings object is asked to mutate. The `finally` publishes after that attempt completes or throws, covering partial `Load` application without moving backend lifecycle work under a lock.

- [ ] **Step 3: Publish toggle after the visible real-backend change**

In `BackendAPI.ToggleBackend`, replace:

```csharp
        Program.Backends.BackendsEdited = true;
```

with:

```csharp
        if (backend.AbstractBackend.IsReal)
        {
            Program.Backends.MarkBackendsEdited();
        }
```

Keep it immediately after `IsEnabled` and `ShutDownReserve` are assigned and before the usage-drain loop.

- [ ] **Step 4: Prove every maintained publication edge**

Run:

```bash
rg -n "BackendsEdited\\s*=|MarkBackendsEdited\\(" src/Backends/BackendHandler.cs src/WebAPI/BackendAPI.cs src/Core/Installation.cs
nl -ba src/Backends/BackendHandler.cs | sed -n '370,490p'
nl -ba src/WebAPI/BackendAPI.cs | sed -n '638,655p'
git diff --check -- src/Backends/BackendHandler.cs src/WebAPI/BackendAPI.cs
```

Expected:

- maintained production mutations call `MarkBackendsEdited`;
- no maintained mutation assigns `BackendsEdited = false`;
- add publishes after insertion;
- delete publishes after successful removal and behind `IsReal`;
- edit uses the guarded `finally`;
- toggle publishes after `IsEnabled` changes and behind `IsReal`;
- non-real add has no publication; and
- `git diff --check` returns no output.

- [ ] **Step 5: Review and commit the mutation edges**

Run:

```bash
git diff -- src/Backends/BackendHandler.cs src/WebAPI/BackendAPI.cs
git status --short --untracked-files=no
git add -- src/Backends/BackendHandler.cs src/WebAPI/BackendAPI.cs
git diff --cached --check
git commit -m "fix: publish backend mutations after visibility"
```

Expected: the commit contains only the two named production files.

### Task 3: Implement captured-generation persistence and failure classification

**Files:**
- Modify: `src/Backends/BackendHandler.cs:736-758`

- [ ] **Step 1: Extract the unchanged real-backend snapshot builder**

Replace the existing `Save` method with this snapshot helper followed by the save implementation from Step 2:

```csharp
    /// <summary>Builds the configured real-backend FDS snapshot.</summary>
    private FDSSection BuildSaveFile()
    {
        FDSSection saveFile = new();
        foreach (BackendData data in AllBackends.Values)
        {
            if (!data.AbstractBackend.IsReal)
            {
                continue;
            }
            FDSSection dataSection = new();
            dataSection.Set("type", data.AbstractBackend.HandlerTypeData.ID);
            dataSection.Set("title", data.AbstractBackend.Title);
            dataSection.Set("enabled", data.AbstractBackend.IsEnabled);
            dataSection.Set("settings", data.AbstractBackend.SettingsRaw.Save(true));
            saveFile.Set(data.ID.ToString(), dataSection);
        }
        return saveFile;
    }
```

The field names, real-only selection, settings serialization, and ID keys are intentionally identical to the old `Save`.

- [ ] **Step 2: Add the serialized save owner and compatibility facade**

Place the following directly after `BuildSaveFile`:

```csharp
    /// <summary>Returns the saved result after acknowledging the captured generation.</summary>
    private BackendSaveResult AcknowledgeSavedGeneration(long targetGeneration)
    {
        Volatile.Write(ref BackendSavedGeneration, targetGeneration);
        return Volatile.Read(ref BackendMutationGeneration) == targetGeneration
            ? BackendSaveResult.Saved
            : BackendSaveResult.SavedWithNewerChangesPending;
    }

    /// <summary>Saves the backend file, optionally skipping clean state and isolating persistence failures.</summary>
    private BackendSaveResult SaveInternal(bool onlyIfPending, bool isolateFailures)
    {
        lock (SaveLock)
        {
            long savedGeneration = Volatile.Read(ref BackendSavedGeneration);
            long targetGeneration = Volatile.Read(ref BackendMutationGeneration);
            if (onlyIfPending && targetGeneration <= savedGeneration)
            {
                return BackendSaveResult.NoChanges;
            }
            Logs.Info("Saving backends...");
            FDSSection saveFile;
            string serializedSaveFile;
            try
            {
                saveFile = BuildSaveFile();
                serializedSaveFile = saveFile.SaveToString();
            }
            catch (Exception ex)
            {
                if (!isolateFailures)
                {
                    throw;
                }
                Logs.Error($"Error serializing backend file: {ex.ReadableString()}");
                return BackendSaveResult.Failed;
            }
            try
            {
                FDSUtility.SaveToFile(saveFile, SaveFilePath);
            }
            catch (Exception ex)
            {
                bool authoritativeFileMatches = false;
                Exception verificationException = null;
                try
                {
                    authoritativeFileMatches = File.Exists(SaveFilePath) && File.ReadAllText(SaveFilePath) == serializedSaveFile;
                }
                catch (Exception verificationEx)
                {
                    verificationException = verificationEx;
                }
                if (authoritativeFileMatches)
                {
                    BackendSaveResult result = AcknowledgeSavedGeneration(targetGeneration);
                    Logs.Error($"Backends were committed, but backend journal cleanup reported an error: {ex.ReadableString()}");
                    if (!isolateFailures)
                    {
                        throw;
                    }
                    return result;
                }
                Logs.Error($"Error saving backend file: {ex.ReadableString()}");
                if (verificationException is not null)
                {
                    Logs.Error($"Error verifying authoritative backend file after the save failure: {verificationException.ReadableString()}");
                }
                if (!isolateFailures)
                {
                    throw;
                }
                return BackendSaveResult.Failed;
            }
            return AcknowledgeSavedGeneration(targetGeneration);
        }
    }

    /// <summary>Attempts to save pending backend changes and returns the authoritative persistence outcome.</summary>
    public BackendSaveResult TrySavePending()
    {
        return SaveInternal(true, true);
    }

    /// <summary>Force-saves the backends list while preserving direct-caller exception behavior.</summary>
    public void Save()
    {
        SaveInternal(false, false);
    }
```

The outer `catch` remains active when `throw;` executes, so direct `Save()` callers receive the original serialization or `SaveToFile` exception. A verified authoritative commit is acknowledged before a journal-cleanup exception is rethrown to a direct caller.

- [ ] **Step 3: Prove acknowledgement and schema invariants**

Run:

```bash
rg -n "BuildSaveFile|SaveToString|SaveToFile|File\\.ReadAllText|AcknowledgeSavedGeneration|TrySavePending|SaveInternal" src/Backends/BackendHandler.cs
rg -n 'Set\\(\"(type|title|enabled|settings)\"|IsReal|data\\.ID\\.ToString' src/Backends/BackendHandler.cs
rg -n "BackendSavedGeneration" src/Backends/BackendHandler.cs
git diff --check -- src/Backends/BackendHandler.cs
```

Expected:

- the captured target is read before snapshot construction;
- only `AcknowledgeSavedGeneration` writes `BackendSavedGeneration`;
- acknowledgement occurs after successful `SaveToFile` or an exact authoritative match;
- mismatched, missing, or unreadable authoritative state returns `Failed`;
- the four FDS fields, ID key, and real-only filter remain; and
- `git diff --check` returns no output.

- [ ] **Step 4: Review and commit the persistence owner**

Run:

```bash
git diff -- src/Backends/BackendHandler.cs
git status --short --untracked-files=no
git add -- src/Backends/BackendHandler.cs
git diff --cached --check
git commit -m "fix: acknowledge durable backend generations"
```

Expected: the commit contains only `src/Backends/BackendHandler.cs`.

### Task 4: Keep periodic retries alive and contain final-save failure

**Files:**
- Modify: `src/Core/Program.cs:417-432`
- Modify: `src/Backends/BackendHandler.cs:814-866`

- [ ] **Step 1: Replace clear-before-save with an observable periodic attempt**

Replace the inner body after the cancellation check in the ten-second task:

```csharp
                BackendHandler.BackendSaveResult saveResult = Backends.TrySavePending();
                if (saveResult == BackendHandler.BackendSaveResult.Failed)
                {
                    Logs.Error("Backend persistence remains pending and will retry on the next interval.");
                }
```

Remove the `if (Backends.BackendsEdited)` wrapper, the `Backends.BackendsEdited = false;` assignment, and the direct `Backends.Save()` call. Keep the interval, cancellation token, cancellation check, task ownership, and surrounding startup code unchanged.

- [ ] **Step 2: Make one contained final attempt during backend shutdown**

Replace the conditional save block at the end of `BackendHandler.Shutdown` with:

```csharp
        if (BackendsEdited)
        {
            Logs.Info("All backends shut down, saving file...");
            BackendSaveResult saveResult = TrySavePending();
            if (saveResult == BackendSaveResult.Failed)
            {
                Logs.Error("Final backend persistence failed; later shutdown cleanup will continue.");
            }
            else if (saveResult == BackendSaveResult.SavedWithNewerChangesPending)
            {
                Logs.Error("A newer backend mutation remained pending after the final save attempt.");
            }
            Logs.Info("Backend handler shutdown complete.");
        }
        else
        {
            Logs.Info("Backend handler shutdown complete without saving.");
        }
```

Do not add a retry delay, alter `HasShutdown`, move `WebhookManager.TryMarkDoneGenerating`, or catch unrelated backend-drain failures.

- [ ] **Step 3: Prove the retry and shutdown boundaries**

Run:

```bash
rg -n "BackendsEdited = false|Backends\\.Save\\(\\)" src/Core/Program.cs src/Backends/BackendHandler.cs
rg -n "TrySavePending|will retry|later shutdown cleanup|newer backend mutation" src/Core/Program.cs src/Backends/BackendHandler.cs
nl -ba src/Core/Program.cs | sed -n '417,435p'
nl -ba src/Backends/BackendHandler.cs | sed -n '850,885p'
nl -ba src/Core/Program.cs | sed -n '579,635p'
git diff --check -- src/Core/Program.cs src/Backends/BackendHandler.cs
```

Expected:

- the first command returns no matches;
- the periodic task calls `TrySavePending` every interval and does not clear state;
- shutdown calls the nonthrowing owner once and reaches its completion log for every result;
- `Program.Shutdown` still proceeds from backends to sessions, proxy, models, extensions, metadata, temporary data, and logs;
- exit-code assignment remains before backend shutdown and unchanged; and
- `git diff --check` returns no output.

- [ ] **Step 4: Review and commit the maintained consumers**

Run:

```bash
git diff -- src/Core/Program.cs src/Backends/BackendHandler.cs
git status --short --untracked-files=no
git add -- src/Core/Program.cs src/Backends/BackendHandler.cs
git diff --cached --check
git commit -m "fix: retry and isolate backend persistence"
```

Expected: the commit contains only the two named production files.

### Task 5: Perform the complete static implementation review

**Files:**
- Inspect: `src/Backends/BackendHandler.cs`
- Inspect: `src/WebAPI/BackendAPI.cs`
- Inspect: `src/Core/Program.cs`

- [ ] **Step 1: Inventory all mutation and compatibility callers**

Run:

```bash
rg -n "BackendsEdited|MarkBackendsEdited|AddNewOfType|AddNewNonrealBackend|DeleteById|EditById|ToggleBackend" src --glob '*.cs' --glob '!src/Extensions/**' --glob '!src/bin/**' --glob '!src/obj/**'
```

Expected:

- real creation through API and installation reaches post-insertion publication;
- missing/non-real deletion does not publish;
- edit and real toggle publish after visible mutation;
- public `BackendsEdited = true` remains supported;
- false assignment is absent; and
- no maintained mutation owner was missed.

- [ ] **Step 2: Trace every persistence result**

Run:

```bash
rg -n "BackendSaveResult|TrySavePending|SaveInternal|AcknowledgeSavedGeneration|BackendSavedGeneration|BackendMutationGeneration" src --glob '*.cs' --glob '!src/Extensions/**'
rg -n "SaveToFile\\(saveFile, SaveFilePath\\)|File\\.ReadAllText\\(SaveFilePath\\)|SaveToString\\(\\)" src/Backends/BackendHandler.cs
```

Expected:

- `NoChanges`, `Saved`, `SavedWithNewerChangesPending`, and `Failed` have explicit return paths;
- failed or unverified writes never acknowledge;
- journal-cleanup failure acknowledges only after exact authoritative comparison;
- newer generations remain pending;
- periodic failure does not terminate the loop; and
- final failure does not escape backend shutdown.

- [ ] **Step 3: Check API, lifecycle, schema, and scope preservation**

Run:

```bash
git diff HEAD~4..HEAD -- src/Backends/BackendHandler.cs src/WebAPI/BackendAPI.cs src/Core/Program.cs
rg -n 'Set\\(\"(type|title|enabled|settings)\"|SaveFilePath = \"Data/Backends\\.fds\"|ExcludeSecretValuesThatMatch|LockSettings|ShutDownReserve|DoShutdownNow' src/Backends/BackendHandler.cs src/WebAPI/BackendAPI.cs
git diff --check HEAD~4..HEAD
git status --short --untracked-files=no
```

Expected:

- routes, permissions, payloads, immediate response timing, lifecycle calls, lock-settings behavior, FDS path/schema, secret handling, IDs, and non-real exclusion are unchanged;
- no async lifecycle work moved under `SaveLock`;
- the four pre-existing user modifications are still present and unstaged;
- no unrelated tracked file changed; and
- the diff check returns no output.

- [ ] **Step 4: Stop for source review**

Present the four production commits and the static proof to Reaper176. Do not run or claim a build or runtime success. Address any source-review correction in a narrowly scoped follow-up commit, then repeat Steps 1-3 before requesting runtime validation.

### Task 6: Record implementation status and request maintainer validation

**Files:**
- Modify: `docs/superpowers/specs/2026-07-23-backend-persistence-generation-acknowledgement-design.md:5`
- Modify: `docs/superpowers/audits/2026-07-21-maintainability-architecture-refresh.md:716-721,889-896`

- [ ] **Step 1: Mark the approved design as implemented but unvalidated**

Change the design status to:

```markdown
**Status:** Implemented; awaiting maintainer validation
```

- [ ] **Step 2: Mark rank 5 as implemented but keep it active**

In the roadmap's rank-5 entry and Recommended Next Project introduction, add this exact status:

```markdown
- **Implementation status:** **Implemented, awaiting maintainer validation.** The monotonic mutation/saved-generation protocol, post-visibility real-mutation publication, exact authoritative-file classification, periodic retry isolation, and contained final shutdown attempt are present. Runtime durability, failure recovery, restart, and downstream cleanup remain for maintainer validation.
```

Keep rank 5 as the recommended active project until validation is confirmed. Do not mark F14/F21 fully mitigated and do not advance rank 6 yet.

- [ ] **Step 3: Check and commit the implementation-status documentation**

Run:

```bash
rg -n "Status:|Implementation status|Rank 5|Recommended Next Project|awaiting maintainer validation" docs/superpowers/specs/2026-07-23-backend-persistence-generation-acknowledgement-design.md docs/superpowers/audits/2026-07-21-maintainability-architecture-refresh.md
git diff --check -- docs/superpowers/specs/2026-07-23-backend-persistence-generation-acknowledgement-design.md docs/superpowers/audits/2026-07-21-maintainability-architecture-refresh.md
git add -- docs/superpowers/specs/2026-07-23-backend-persistence-generation-acknowledgement-design.md docs/superpowers/audits/2026-07-21-maintainability-architecture-refresh.md
git diff --cached --check
git commit -m "docs: record backend persistence implementation"
```

Expected: only the design and audit documents are committed.

- [ ] **Step 4: Hand the runtime matrix to Reaper176**

Ask the maintainer to use the normal build/launch workflow and validate:

1. real backend API add, edit, enable, disable, and delete survive restart;
2. installation-created real backend survives restart;
3. autoscaler and linked-remote non-real removal does not independently schedule a backend-file write;
4. a mutation overlapping periodic snapshot/write remains pending and is present after the following successful interval;
5. temporarily unwritable or full backend storage logs each failed interval, stays pending, recovers after storage is restored, and survives restart;
6. rapid mixed mutations during periodic overlap converge to the complete real-backend catalog;
7. shutdown overlapping a pending mutation performs one final attempt;
8. forced final-save failure still allows session, proxy, model, extension, output-metadata, temporary-data, and log cleanup;
9. restart/nonzero requested exit code remains unchanged during forced final-save failure;
10. authoritative commit followed by forced journal-cleanup failure is classified as committed and does not cause a false retry;
11. public/direct `Save()` still force-writes and reports write exceptions to its caller; and
12. public `BackendsEdited = true` schedules a save while `BackendsEdited = false` cannot clear pending state.

Stop here until Reaper176 reports the build/launch and matrix result.

### Task 7: Reconcile validated rank 5 and advance the roadmap

**Files:**
- Modify: `docs/superpowers/specs/2026-07-23-backend-persistence-generation-acknowledgement-design.md`
- Modify: `docs/superpowers/audits/2026-07-21-maintainability-architecture-refresh.md`

- [ ] **Step 1: Record maintainer confirmation only after it is received**

Change the design status to:

```markdown
**Status:** Implemented and maintainer-validated
```

Add this section after `Success Criteria`:

```markdown
## Validation Record

Maintainer Reaper176 confirmed the normal build/launch workflow and the complete twelve-case backend persistence matrix: real API and installation mutations, non-real removal, periodic overlap and retry, storage failure and recovery, rapid convergence, final shutdown persistence and failure isolation, exit-code preservation, journal-cleanup classification, and both public compatibility facades.
```

If any case was not run or failed, retain `awaiting maintainer validation` and add a concise sentence naming the incomplete case instead of adding this completed record.

- [ ] **Step 2: Reconcile F14, F21, rank 5, and Recommended Next**

Add this line directly after the Backend F14 heading:

```markdown
- **Implementation status:** **Implemented and maintainer-validated.** Real configured-backend mutations publish monotonic generations only after visibility, non-real removal does not independently publish, and persistence acknowledges only a confirmed captured generation. Maintainer Reaper176 confirmed the normal build/launch workflow and the complete backend persistence matrix.
```

Add this line directly after the Backend F21 heading:

```markdown
- **Implementation status:** **Implemented and maintainer-validated.** Failed periodic writes retain pending work and retry, verified authoritative commits are distinguished from true failure, and one failed final attempt cannot abort later shutdown cleanup or change the requested exit code. Maintainer Reaper176 confirmed the normal build/launch workflow and the complete backend persistence matrix.
```

Add this line directly after both `### 5. Make backend persistence generation-acknowledged and failure-isolated` headings:

```markdown
- **Implementation status:** **Implemented and maintainer-validated.** The complete generation publication, captured acknowledgement, retry, and shutdown-isolation protocol is the rollback unit. Maintainer Reaper176 confirmed the normal build/launch workflow and complete twelve-case matrix; no performance claim is made.
```

Add this line to `Current finding dispositions`:

```markdown
- **Backend F14/Backend F21/rank 5:** **Implemented and maintainer-validated.** The configured-backend generation, acknowledgement, retry, and shutdown-isolation protocol remains one coordinated rollback unit.
```

Replace the Recommended Next Project heading with:

```markdown
### 6. Make custom-workflow replacement durable before publication

- **Boundary and owner:** Comfy workflow storage in `ComfyUIBackendExtension`/`ComfyUIWebAPI`, especially replacement save/delete and `CustomWorkflows`. **Evidence/consumers:** Comfy F23; list/read/generate/delete/replace routes and refresh consume the shared map/files.
- **Payoff:** very high workflow-data reliability and a coherent store boundary; no performance claim; high leverage, medium feasibility, medium cross-platform filesystem risk. **Prerequisites:** preserve JSON format, names, example markers, route responses, and public dictionary identity.
- **Stages/non-goals:** validate/parse candidate, write a temporary sibling and atomically replace where supported, publish cache only after durable success, retire predecessor last. Do not change hydration (P8) or workflow schema. **Verification/validation:** parse/write/rename failures, concurrent list/read/generate, replace/rename/delete, restart on supported platforms. **Success:** visible cache and durable files never disagree after a response. **Rollback:** restore prior save order without schema migration.
```

Remove the former rank-5 body from the Recommended Next section so it is not duplicated beneath the new heading.

- [ ] **Step 3: Statically reconcile all rank references**

Run:

```bash
rg -n "Backend F14|Backend F21|### 5\\.|rank 5|Rank 5|Recommended Next Project|### 6\\.|awaiting maintainer validation|maintainer-validated" docs/superpowers/specs/2026-07-23-backend-persistence-generation-acknowledgement-design.md docs/superpowers/audits/2026-07-21-maintainability-architecture-refresh.md
git diff --check -- docs/superpowers/specs/2026-07-23-backend-persistence-generation-acknowledgement-design.md docs/superpowers/audits/2026-07-21-maintainability-architecture-refresh.md
```

Expected:

- every F14/F21 and rank-5 status agrees;
- Recommended Next is rank 6;
- no rank-5 awaiting-validation status remains; and
- the diff check returns no output.

- [ ] **Step 4: Commit the validation record**

Run:

```bash
git add -- docs/superpowers/specs/2026-07-23-backend-persistence-generation-acknowledgement-design.md docs/superpowers/audits/2026-07-21-maintainability-architecture-refresh.md
git diff --cached --check
git diff --cached
git commit -m "docs: record backend persistence validation"
git status --short --untracked-files=no
```

Expected: the commit contains only the two documentation files, and final status still shows only the four preserved user-owned tracked modifications.

## Final Completion Gate

Before claiming rank 5 complete:

- confirm all production and documentation commits are present on `master`;
- rerun Task 5's complete static review after any correction;
- obtain Reaper176's explicit normal build/launch and twelve-case validation confirmation;
- confirm the design and audit both say implemented and maintainer-validated;
- confirm rank 6, not rank 5, is Recommended Next; and
- report that no agent-run build or test was performed.
