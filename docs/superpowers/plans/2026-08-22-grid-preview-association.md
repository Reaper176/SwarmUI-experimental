# Grid Preview Association Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Match each Grid Generator live preview to its completed image so previews do not overwrite one another and final images replace their preview in SwarmUI.

**Architecture:** Normalize the nested `gen_progress.batch_index` at the Grid Generator's server-side output boundary. Each grid task already owns a unique `iteration`, which is also used for its final output event; using it for the task's progress event gives the shared frontend a stable common key.

**Tech Stack:** C# 12, .NET 8, Newtonsoft.Json, SwarmUI Grid Generator extension.

---

### Task 1: Normalize Grid Generator progress identifiers

**Files:**
- Modify: `src/BuiltinExtensions/GridGenerator/GridGeneratorExtension.cs:212-214`
- Test: Manual live grid generation by the maintainer; automated tests are prohibited by this repository's `AGENTS.md`.

- [ ] **Step 1: Record the failing reproduction**

Run a Grid Generator job with at least two cells and `Show Outputs` enabled. Observe backend progress payloads whose nested `gen_progress.batch_index` is reused across grid cells while final image payloads use their grid-wide iteration. The UI will repeatedly update one preview and the active preview will not be replaced by its completed image.

- [ ] **Step 2: Add the minimal progress-payload normalization**

Immediately after `int iteration = runner.Iteration;`, add a callback that retains every payload but replaces only the nested progress index:

```csharp
void outputGridProgress(JObject output)
{
    if (output["gen_progress"] is JObject progress)
    {
        progress["batch_index"] = $"{iteration}";
    }
    data.AddOutput(output);
}
```

Then pass `outputGridProgress` instead of `data.AddOutput` to the existing `T2IEngine.CreateImageTask` call:

```csharp
Task t = Task.Run(() => T2IEngine.CreateImageTask(thisParams, $"{iteration}", data.Claim, outputGridProgress, setError, true,
```

- [ ] **Step 3: Static data-flow verification**

Inspect the modified call and confirm:

- Live `gen_progress` events leave the Grid Generator with `batch_index == iteration`.
- Existing final image events continue to emit `batch_index == iteration` in both Web Page and non-Web Page paths.
- Non-progress payloads continue unchanged through `data.AddOutput`.
- The shared `GenerateHandler` therefore forms the same `<request_id>_<iteration>` key for preview and final events.

- [ ] **Step 4: Maintainer live verification**

Run a two-or-more-cell grid with `Show Outputs` enabled. Confirm that each active cell gets its own preview, each preview turns into its matching final image, and clicking the active result opens the full saved image within SwarmUI.

- [ ] **Step 5: Commit the implementation**

```bash
git add src/BuiltinExtensions/GridGenerator/GridGeneratorExtension.cs
git commit -m "fix: match grid previews to final outputs"
```
