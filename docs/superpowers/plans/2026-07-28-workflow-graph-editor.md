# Facade-Preserving Workflow Graph Editor Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Extract `WorkflowGenerator`'s graph mechanics into one internal editor while preserving the complete public facade, already-compiled extension compatibility, exact generated graphs, partial mutations, and exceptions.

**Architecture:** Add an internal `WorkflowGraphEditor` that stores only its owning `WorkflowGenerator`. Existing public generator methods delegate to the editor, which reads and writes the generator's current public `Workflow`, `LastID`, `NodeHelpers`, and `UsedInputs` fields on every call; no graph state is mirrored.

**Tech Stack:** C# 12, .NET 8, Newtonsoft.Json `JObject`/`JArray`, SwarmUI extension step APIs, Git/static source inspection, maintainer-run build and exact workflow/ABI validation on Garuda Linux/Btrfs.

---

## Repository Policy and Fixed Boundaries

- Approved maintainer: Reaper176.
- Approved source/audit base: `e9d99dead294740265676c78a711b03593906211`.
- Approved design head: `5ce36366ca64482f59e965ab62a86a7d779bdc1f`.
- Approved design: `docs/superpowers/specs/2026-07-28-workflow-graph-editor-design.md`.
- Baseline production blob:
  - `src/BuiltinExtensions/ComfyUIBackend/WorkflowGenerator.cs` — `6e30ceae04b61d7a026478f8e3958ac16f5d25fa`
- Baseline audit blob:
  - `docs/superpowers/audits/2026-07-21-maintainability-architecture-refresh.md` — `dd3cf6a5da81328c5a4aeedf00ac17fcd6edd5b7`
- Approved design blob:
  - `docs/superpowers/specs/2026-07-28-workflow-graph-editor-design.md` — `543c7284a0df33095cbf77e0604f24176a651eed`
- Production scope is exactly:
  - `src/BuiltinExtensions/ComfyUIBackend/WorkflowGenerator.cs`
  - `src/BuiltinExtensions/ComfyUIBackend/WorkflowGraphEditor.cs`
- Documentation scope is:
  - the approved design;
  - this implementation plan; and
  - Comfy F26, roadmap Rank 24, recommendation, and final-status passages in `docs/superpowers/audits/2026-07-21-maintainability-architecture-refresh.md`.
- Agents must not build, test, execute a probe/comparator or test-running linter, launch SwarmUI or a backend, automate a browser, call a live API, generate a workflow, load an extension, or perform runtime/platform/filesystem/ABI/performance exercises.
- The maintainer performs every build and runtime case. Agent evidence remains static.
- No production change is authorized in `WorkflowGeneratorSteps.cs`, `WorkflowGeneratorModelSupport.cs`, `WGNodeData.cs`, Dynamic Thresholding, repository tool examples, external extensions, project files, APIs, frontend, settings, launchers, data, models, outputs, generated documentation, or downloaded repositories.
- Do not change `Generate`, `NodePath`, model/media trackers, public fields, public lists, step registration, node/input names, IDs, ordering, cache invalidation, null handling, exceptions, locks, or direct graph access.
- Do not add a committed test project, comparison utility, fixture extension, or production instrumentation.
- Use `apply_patch` for every repository edit and exact-path staging for every commit.

### Execution workspace gate

Rank 24 is high risk and should normally execute in an isolated worktree created through `superpowers:using-git-worktrees`. Direct `master` execution is permitted only after Reaper176 explicitly chooses it for Rank 24. Rank 23's direct-master authorization does not automatically carry forward.

Whichever workspace is selected:

- begin from a descendant of the approved design head;
- do not copy protected working files into an isolated worktree;
- keep the approved base available separately for baseline capture;
- do not switch the dirty primary worktree to the approved base; and
- record the chosen branch/worktree path before source edits.

### Protected primary working tree

The approved primary worktree contains unrelated maintainer changes:

- `src/Data/Settings.fds` — `83 insertions / 3 deletions`;
- `src/Pages/Text2Image.cshtml` — `9 insertions / 2 deletions`;
- `src/wwwroot/js/genpage/gentab/loras.js` — `2 insertions / 0 deletions`;
- `src/wwwroot/js/genpage/main.js` — `0 insertions / 1 deletion`; and
- untracked `Data.pre-restore-2026-07-19/`.

Rank 24 authorizes none of them. If execution is direct on `master`, those exact changes must remain unstaged and unchanged. If execution uses a worktree, the primary worktree must remain untouched.

## File Responsibility Map

- `src/BuiltinExtensions/ComfyUIBackend/WorkflowGraphEditor.cs`
  - Own node existence and stable-ID selection.
  - Own both node-creation overloads and generic deduplication.
  - Own class queries and snapshot traversal.
  - Own connection replacement.
  - Own connectivity-index construction and lookup.
  - Own single-pass and fixed-point unused-node cleanup.
  - Store only the owning generator reference.
- `src/BuiltinExtensions/ComfyUIBackend/WorkflowGenerator.cs`
  - Remain the public facade and public state authority.
  - Lazily bind one internal editor without adding an explicit constructor.
  - Preserve every existing public field/method/property/list signature.
  - Delegate only the ten selected method names, comprising eleven public
    declarations because `CreateNode` has two overloads.
  - Keep `Generate`, `NodePath`, step orchestration, model/media behavior, and direct public state unchanged.
- `docs/superpowers/specs/2026-07-28-workflow-graph-editor-design.md`
  - Record implementation provenance, review results, raw maintainer validation, normalization, and evidence limits.
- `docs/superpowers/audits/2026-07-21-maintainability-architecture-refresh.md`
  - Record the exact source projection and validation result.
  - Remove Rank 24 from recommended-next only after complete validation.

## Exact 42-Case Maintainer Matrix

The approved base and every candidate use the same maintainer-owned capture fixture and frozen inputs. Successful workflow captures are serialized with `Formatting.None` and compared byte for byte. Object properties and arrays are never sorted or renumbered. Expected-failure records compare phase, fully qualified exception type, message, compact post-state graph, `LastID`, ordered `NodeHelpers`, and null/set membership for `UsedInputs`; only validation display of `UsedInputs` membership may be ordinal-sorted.

Primitive/public-state cases:

1. `HasNode` absent/present results, followed by direct replacement of `Workflow` and repeated lookup.
2. `GetStableDynamicID` returns the first free `1000 + index + offset` candidate and does not reserve it or mutate `LastID`.
3. Stable-ID collision skipping and nonzero offset preserve decimal IDs and first-free ordering.
4. Stable-ID exhaustion after all 99,999 candidates preserves the explicit exception type/message and unchanged state.
5. Callback `CreateNode` with automatic ID preserves post-increment, `class_type`-first ordering, callback mutation, publication, and return.
6. Callback creation failure after automatic ID selection consumes the ID, publishes no node, and preserves the thrown exception.
7. Callback creation with a fixed ID preserves no `LastID` change and existing-node replacement ordering/state.
8. Generic-input creation with null ID preserves exact lookup text, first creation, helper publication, and second-call deduplication.
9. Generic-input creation with explicit ID and `idMandatory: true` bypasses an existing helper and overwrites the helper after success.
10. Generic-input creation with explicit ID and `idMandatory: false` returns an existing helper without creating/replacing a node.
11. Direct replacement of `NodeHelpers` between facade calls is immediately authoritative.
12. Direct assignment of `LastID` between facade calls controls the next automatic ID.
13. `NodesOfClass`/`NodesOfClasses` preserve workflow property order, stringified class matching, and snapshots with mixed/missing/odd `class_type` tokens.
14. `RunOnNodesOfClass` preserves snapshot order when its callback removes current and later workflow properties.
15. `ReplaceNodeConnection` rewrites every exact two-token match in workflow/input order and assigns the supplied replacement path.
16. Replacement ignores different source/output pairs, nested arrays, scalars, and arrays whose count is not two.
17. Replacement over the approved malformed/null shapes preserves exception type/message, completed prior rewrites, and cache state.
18. First connectivity build preserves output-specific and `-1` wildcard membership and returns.
19. Initial `exclude`, repeated calls with a different `exclude`, direct graph mutation after caching, and reuse of the existing cache preserve approved behavior.
20. A directly supplied public `UsedInputs` set is used without scanning or replacement.
21. `RemoveClassIfUnused` clears the cache once, uses one snapshot/index pass, and preserves removal order and retained nodes.
22. `RemoveClassesIfUnused` clears/rebuilds per pass and preserves cascading fixed-point removal order.
23. Approved malformed/null query, connectivity, and cleanup inputs preserve exception and partial-state records.
24. One external fixture assembly compiled against the approved base loads unchanged against the candidate, registers ordered priority steps, calls every facade primitive, replaces all four graph-related public fields, directly mutates `Workflow`, and updates one non-graph tracker with identical output and exceptions.

Representative generated-workflow cases:

25. Installed Stable Diffusion 1.x/2.x text-to-image model with fixed seed.
26. Installed SDXL text-to-image model with fixed seed.
27. Installed Flux-family text-to-image model with fixed seed.
28. Installed SD3/SD3.5-family text-to-image model with fixed seed.
29. Installed Chroma-family text-to-image model with fixed seed.
30. Installed Qwen Image-family text-to-image model with fixed seed.
31. LoRA application plus a scheduled LoRA using frozen model/LoRA metadata and seed.
32. Init image plus mask/inpaint path with frozen media and dimensions.
33. Regional prompting path, including the installed regional implementation and fixed prompt/seed.
34. ControlNet path with frozen preprocessor/model/media selections.
35. Installed adapter path, including IP-Adapter or the environment's maintained equivalent.
36. Base-plus-refiner workflow with frozen model metadata, switch point, and seed.
37. Video or image-to-video workflow with frozen frames/FPS/frame-operation inputs.
38. Installed audio or combined audio/video workflow path.
39. Raw custom workflow submission path with frozen raw JSON and parameter substitutions.
40. Stored custom workflow load/publication path using the same frozen stored content.
41. Dynamic Thresholding disabled/enabled pair with frozen parameters and seed.
42. Optional-node absent/present pair using the same base request and frozen capability snapshot.

An unavailable installed model family or optional dependency is reported as **unrun**, never silently substituted or passed. A nondeterministic graph case is blocked until its input/capability source is frozen; broader normalization is prohibited.

## Task 1: Reconfirm boundaries and establish approved-base captures

**Files:**
- Inspect: `AGENTS.md`
- Inspect: `docs/project-memory.md`
- Inspect: `docs/superpowers/specs/2026-07-28-workflow-graph-editor-design.md`
- Inspect: `docs/superpowers/audits/2026-07-21-maintainability-architecture-refresh.md`
- Inspect: `src/BuiltinExtensions/ComfyUIBackend/WorkflowGenerator.cs`
- Inspect: `src/BuiltinExtensions/ComfyUIBackend/WorkflowGeneratorModelSupport.cs`
- Inspect: `src/BuiltinExtensions/ComfyUIBackend/WorkflowGeneratorSteps.cs`
- Inspect: `src/BuiltinExtensions/ComfyUIBackend/WGNodeData.cs`
- Inspect: `src/BuiltinExtensions/ComfyUIBackend/ComfyUIAPIAbstractBackend.cs`
- Inspect: `src/BuiltinExtensions/DynamicThresholding/DynamicThresholdingExtension.cs`
- Inspect: `tools/SwarmUI-LatentTools/LatentToolsExtension.cs`
- Inspect: `tools/SwarmUI-LatentColorTools/LatentColorToolsExtension.cs`
- No repository edits

- [ ] **Step 1: Select and record the Rank 24 execution workspace**

Use the execution skill's workspace flow. Record either:

- isolated candidate worktree/branch created from the current planning HEAD
  containing this committed plan, which must remain a descendant of
  `5ce36366ca64482f59e965ab62a86a7d779bdc1f`; or
- explicit Reaper176 authorization for direct Rank 24 work on `master`.

Do not edit source until this choice and the exact candidate path are recorded.

- [ ] **Step 2: Verify ancestry and fixed blobs**

Run static Git inspection:

```bash
git merge-base --is-ancestor e9d99dead294740265676c78a711b03593906211 HEAD
git merge-base --is-ancestor 5ce36366ca64482f59e965ab62a86a7d779bdc1f HEAD
git rev-parse e9d99dead294740265676c78a711b03593906211:src/BuiltinExtensions/ComfyUIBackend/WorkflowGenerator.cs
git rev-parse e9d99dead294740265676c78a711b03593906211:docs/superpowers/audits/2026-07-21-maintainability-architecture-refresh.md
git rev-parse 5ce36366ca64482f59e965ab62a86a7d779bdc1f:docs/superpowers/specs/2026-07-28-workflow-graph-editor-design.md
```

Expected: both ancestry checks exit `0`; returned blobs are respectively `6e30ceae04b61d7a026478f8e3958ac16f5d25fa`, `dd3cf6a5da81328c5a4aeedf00ac17fcd6edd5b7`, and `543c7284a0df33095cbf77e0604f24176a651eed`.

- [ ] **Step 3: Read instructions, design, and repository-local skills**

Read the complete root `AGENTS.md`, `docs/project-memory.md`, approved design, and every relevant `.agents/skills/**/SKILL.md` if present. Confirm the no-build/no-test/no-runtime agent boundary.

- [ ] **Step 4: Record working-tree protection**

In the candidate workspace run:

```bash
git status --short --branch
git diff --cached --name-only
git diff --numstat -- src/Data/Settings.fds src/Pages/Text2Image.cshtml src/wwwroot/js/genpage/gentab/loras.js src/wwwroot/js/genpage/main.js
```

Expected in an isolated worktree: no source working changes and an empty index.

Expected on direct `master`: exactly the four protected numstats and untracked backup recorded above, with an empty index. Stop rather than absorb any unexpected user change.

- [ ] **Step 5: Re-inventory the exact primitive and public surface**

Run:

```bash
rg -n '^    public (static )?.*(GetStableDynamicID|CreateNode|Generate|HasNode|NodesOfClass|NodesOfClasses|RunOnNodesOfClass|ReplaceNodeConnection|NodeIsConnectedAnywhere|RemoveClassIfUnused|RemoveClassesIfUnused)|^    public (JObject Workflow|Dictionary<string, string> NodeHelpers|int LastID|HashSet<string> UsedInputs|static List<WorkflowGenStep> Steps|static List<WorkflowGenStep> ModelGenSteps)' src/BuiltinExtensions/ComfyUIBackend/WorkflowGenerator.cs
rg -n '\\.(CreateNode|GetStableDynamicID|HasNode|NodesOfClass|NodesOfClasses|RunOnNodesOfClass|ReplaceNodeConnection|NodeIsConnectedAnywhere|RemoveClassIfUnused|RemoveClassesIfUnused)\\(' src/BuiltinExtensions src tools --glob '*.cs' --glob '!src/Extensions/**'
rg -n '\\.(Workflow|NodeHelpers|LastID|UsedInputs)\\b' src/BuiltinExtensions/ComfyUIBackend src/BuiltinExtensions/DynamicThresholding tools --glob '*.cs' --glob '!src/Extensions/**'
```

Expected: the ten selected public method identities, four graph-related fields, core/Dynamic Thresholding/tool consumers, and no existing `WorkflowGraphEditor`.

- [ ] **Step 6: Have the maintainer create the approved-base capture set**

Reaper176 builds/runs the approved base separately and captures exact cases 1–42 using the maintainer-owned fixture and frozen inputs described above. The external fixture assembly is compiled once against the approved base and retained byte-for-byte for candidate case 24.

Required evidence before source editing:

- raw approved-base capture directory retained;
- compact successful graph files;
- expected-failure state records;
- fixture assembly digest;
- frozen-input/capability manifest;
- per-case available/unrun list; and
- explicit maintainer token `RANK24_BASELINE_CAPTURED`.

Agents do not create, compile, execute, inspect secret fixture inputs, or infer completion.

- [ ] **Step 7: Report the fixed boundary**

Report the selected workspace, approved commits/blobs, exact primitive/public-state inventory, protected-state result, baseline token, and available/unrun matrix cases. Do not commit in this task.

## Task 2: Add the editor shell and delegate existence/stable IDs

**Files:**
- Create: `src/BuiltinExtensions/ComfyUIBackend/WorkflowGraphEditor.cs`
- Modify: `src/BuiltinExtensions/ComfyUIBackend/WorkflowGenerator.cs:155-225`
- Modify: `src/BuiltinExtensions/ComfyUIBackend/WorkflowGenerator.cs:924-928`

- [ ] **Step 1: Create the initial internal editor**

Use `apply_patch` to add:

```csharp
using System;
using Newtonsoft.Json.Linq;

namespace SwarmUI.Builtin_ComfyUIBackend;

/// <summary>Internal owner of low-level workflow graph editing mechanics.</summary>
internal sealed class WorkflowGraphEditor
{
    /// <summary>Public workflow-generator facade whose current graph state is authoritative.</summary>
    private readonly WorkflowGenerator Generator;

    /// <summary>Creates an editor bound to one workflow-generator facade.</summary>
    public WorkflowGraphEditor(WorkflowGenerator generator)
    {
        Generator = generator;
    }

    /// <summary>Returns true if the current workflow contains the given node ID.</summary>
    public bool HasNode(string id)
    {
        return Generator.Workflow.ContainsKey(id);
    }

    /// <summary>Gets a dynamic ID within the generator's semi-stable registration set.</summary>
    public string GetStableDynamicID(int index, int offset)
    {
        for (int i = 0; i < 99999; i++)
        {
            int id = 1000 + index + offset + i;
            string result = $"{id}";
            if (!HasNode(result))
            {
                return result;
            }
        }
        throw new Exception("Failed to find a stable dynamic ID.");
    }
}
```

Do not add graph/helper/cache fields to the editor.

- [ ] **Step 2: Add the private lazy facade binding**

Immediately after public `LastID`, add:

```csharp
    /// <summary>Internal owner for low-level edits against this generator's current public graph state.</summary>
    private WorkflowGraphEditor GraphEditor = null;

    /// <summary>Gets the internal graph editor, creating it only when a graph primitive is first used.</summary>
    private WorkflowGraphEditor GetGraphEditor()
    {
        GraphEditor ??= new(this);
        return GraphEditor;
    }
```

Do not add an explicit `WorkflowGenerator` constructor.

- [ ] **Step 3: Replace only the two facade method bodies**

Keep the existing public declarations and XML summaries. Replace `GetStableDynamicID` with:

```csharp
    public string GetStableDynamicID(int index, int offset)
    {
        return GetGraphEditor().GetStableDynamicID(index, offset);
    }
```

Replace `HasNode` with:

```csharp
    public bool HasNode(string id)
    {
        return GetGraphEditor().HasNode(id);
    }
```

- [ ] **Step 4: Perform focused static parity inspection**

Run:

```bash
git diff --check -- src/BuiltinExtensions/ComfyUIBackend/WorkflowGenerator.cs src/BuiltinExtensions/ComfyUIBackend/WorkflowGraphEditor.cs
git diff -- src/BuiltinExtensions/ComfyUIBackend/WorkflowGenerator.cs src/BuiltinExtensions/ComfyUIBackend/WorkflowGraphEditor.cs
git show e9d99dead294740265676c78a711b03593906211:src/BuiltinExtensions/ComfyUIBackend/WorkflowGenerator.cs | sed -n '204,248p'
rg -n 'WorkflowGraphEditor|GetGraphEditor|GetStableDynamicID|HasNode|Workflow|NodeHelpers|LastID|UsedInputs' src/BuiltinExtensions/ComfyUIBackend/WorkflowGenerator.cs src/BuiltinExtensions/ComfyUIBackend/WorkflowGraphEditor.cs
```

Expected: the moved loop and exception are statement-equivalent; the editor has one generator field; no constructor/public field/list/`Generate`/`NodePath` change exists.

- [ ] **Step 5: Stage and commit only stage 1**

```bash
git add -- src/BuiltinExtensions/ComfyUIBackend/WorkflowGenerator.cs src/BuiltinExtensions/ComfyUIBackend/WorkflowGraphEditor.cs
git diff --cached --name-status
git diff --cached --check
git diff --cached --stat
git commit -m "refactor: extract workflow node identity mechanics"
```

Expected: exactly the two production files are committed. Record commit and resulting blobs.

- [ ] **Step 6: Obtain focused maintainer validation**

Reaper176 builds the committed candidate and compares cases 1–4 plus unchanged precompiled fixture case 24 against the approved-base captures.

Required token: `RANK24_STAGE1_PARITY_PASSED`, or exact failed/unrun cases and diffs. Do not proceed on a mismatch.

## Task 3: Delegate node creation and deduplication

**Files:**
- Modify: `src/BuiltinExtensions/ComfyUIBackend/WorkflowGraphEditor.cs`
- Modify: `src/BuiltinExtensions/ComfyUIBackend/WorkflowGenerator.cs:225-246`

- [ ] **Step 1: Add both editor creation methods after `GetStableDynamicID`**

```csharp
    /// <summary>Creates a node with a configuration callback and optional manual ID.</summary>
    public string CreateNode(string classType, Action<string, JObject> configure, string id = null)
    {
        id ??= $"{Generator.LastID++}";
        JObject obj = new() { ["class_type"] = classType };
        configure(id, obj);
        Generator.Workflow[id] = obj;
        return id;
    }

    /// <summary>Creates a node with input data, optional manual ID, and compatible deduplication.</summary>
    public string CreateNode(string classType, JObject input, string id = null, bool idMandatory = true)
    {
        string lookup = $"__generic_node__{classType}___{input}";
        if ((id is null || !idMandatory) && Generator.NodeHelpers.TryGetValue(lookup, out string existingNode))
        {
            return existingNode;
        }
        string result = CreateNode(classType, (_, n) => n["inputs"] = input, id);
        Generator.NodeHelpers[lookup] = result;
        return result;
    }
```

- [ ] **Step 2: Replace only the two facade bodies**

```csharp
    public string CreateNode(string classType, Action<string, JObject> configure, string id = null)
    {
        return GetGraphEditor().CreateNode(classType, configure, id);
    }
```

```csharp
    public string CreateNode(string classType, JObject input, string id = null, bool idMandatory = true)
    {
        return GetGraphEditor().CreateNode(classType, input, id, idMandatory);
    }
```

- [ ] **Step 3: Prove statement and partial-mutation parity statically**

```bash
git diff --check -- src/BuiltinExtensions/ComfyUIBackend/WorkflowGenerator.cs src/BuiltinExtensions/ComfyUIBackend/WorkflowGraphEditor.cs
git diff -- src/BuiltinExtensions/ComfyUIBackend/WorkflowGenerator.cs src/BuiltinExtensions/ComfyUIBackend/WorkflowGraphEditor.cs
git show e9d99dead294740265676c78a711b03593906211:src/BuiltinExtensions/ComfyUIBackend/WorkflowGenerator.cs | sed -n '220,248p'
rg -n 'LastID\\+\\+|__generic_node__|NodeHelpers|configure\\(|Workflow\\[id\\]' src/BuiltinExtensions/ComfyUIBackend/WorkflowGenerator.cs src/BuiltinExtensions/ComfyUIBackend/WorkflowGraphEditor.cs
```

Expected: post-increment remains before callback; publication and helper update remain after success; exact lookup and `idMandatory` condition remain; no cloning/guard/catch/invalidation appears.

- [ ] **Step 4: Commit stage 2**

```bash
git add -- src/BuiltinExtensions/ComfyUIBackend/WorkflowGenerator.cs src/BuiltinExtensions/ComfyUIBackend/WorkflowGraphEditor.cs
git diff --cached --name-status
git diff --cached --check
git commit -m "refactor: extract workflow node creation"
```

Record commit/blobs and confirm the index excludes all protected work.

- [ ] **Step 5: Obtain focused maintainer validation**

Reaper176 builds and compares cases 5–12 plus case 24.

Required token: `RANK24_STAGE2_PARITY_PASSED`, or exact failed/unrun cases and diffs. Do not proceed on a mismatch.

## Task 4: Delegate class queries and snapshot traversal

**Files:**
- Modify: `src/BuiltinExtensions/ComfyUIBackend/WorkflowGraphEditor.cs`
- Modify: `src/BuiltinExtensions/ComfyUIBackend/WorkflowGenerator.cs:3316-3337`

- [ ] **Step 1: Add collection/LINQ imports**

At the top of `WorkflowGraphEditor.cs`, retain `using System;` and add:

```csharp
using System.Collections.Generic;
using System.Linq;
```

- [ ] **Step 2: Add query and traversal methods**

```csharp
    /// <summary>Returns a property-order snapshot of nodes with one class type.</summary>
    public JProperty[] NodesOfClass(string classType)
    {
        return [.. Generator.Workflow.Properties().Where(p => $"{p.Value["class_type"]}" == classType)];
    }

    /// <summary>Returns a property-order snapshot of nodes with any requested class type.</summary>
    public JProperty[] NodesOfClasses(HashSet<string> classTypes)
    {
        return [.. Generator.Workflow.Properties().Where(p => classTypes.Contains($"{p.Value["class_type"]}"))];
    }

    /// <summary>Runs an action against the property-order snapshot of one class type.</summary>
    public void RunOnNodesOfClass(string classType, Action<string, JObject> action)
    {
        foreach (JProperty property in NodesOfClass(classType))
        {
            action(property.Name, property.Value as JObject);
        }
    }
```

- [ ] **Step 3: Replace the three facade bodies**

```csharp
    public JProperty[] NodesOfClass(string classType)
    {
        return GetGraphEditor().NodesOfClass(classType);
    }
```

```csharp
    public JProperty[] NodesOfClasses(HashSet<string> classTypes)
    {
        return GetGraphEditor().NodesOfClasses(classTypes);
    }
```

```csharp
    public void RunOnNodesOfClass(string classType, Action<string, JObject> action)
    {
        GetGraphEditor().RunOnNodesOfClass(classType, action);
    }
```

- [ ] **Step 4: Inspect snapshot/order parity**

```bash
git diff --check -- src/BuiltinExtensions/ComfyUIBackend/WorkflowGenerator.cs src/BuiltinExtensions/ComfyUIBackend/WorkflowGraphEditor.cs
git diff -- src/BuiltinExtensions/ComfyUIBackend/WorkflowGenerator.cs src/BuiltinExtensions/ComfyUIBackend/WorkflowGraphEditor.cs
git show e9d99dead294740265676c78a711b03593906211:src/BuiltinExtensions/ComfyUIBackend/WorkflowGenerator.cs | sed -n '3318,3352p'
rg -n 'Properties\\(\\).*Where|ToArray|JProperty\\[\\]|RunOnNodesOfClass|foreach \\(JProperty' src/BuiltinExtensions/ComfyUIBackend/WorkflowGraphEditor.cs
```

Expected: both queries materialize arrays in workflow order; callbacks iterate that snapshot; class matching and casts are exact; no live iterator, guard, sort, comparer, or clone appears.

- [ ] **Step 5: Commit stage 3**

```bash
git add -- src/BuiltinExtensions/ComfyUIBackend/WorkflowGenerator.cs src/BuiltinExtensions/ComfyUIBackend/WorkflowGraphEditor.cs
git diff --cached --name-status
git diff --cached --check
git commit -m "refactor: extract workflow node traversal"
```

- [ ] **Step 6: Obtain focused maintainer validation**

Reaper176 builds and compares cases 13–14, 23, and 24.

Required token: `RANK24_STAGE3_PARITY_PASSED`, or exact failed/unrun cases and diffs.

## Task 5: Delegate connection replacement

**Files:**
- Modify: `src/BuiltinExtensions/ComfyUIBackend/WorkflowGraphEditor.cs`
- Modify: `src/BuiltinExtensions/ComfyUIBackend/WorkflowGenerator.cs:3339-3353`

- [ ] **Step 1: Add exact editor replacement logic**

```csharp
    /// <summary>Replaces exact node-output references in current workflow inputs.</summary>
    public void ReplaceNodeConnection(JArray oldNode, JArray newNode)
    {
        string target0 = $"{oldNode[0]}", target1 = $"{oldNode[1]}";
        foreach (JObject node in Generator.Workflow.Values().Cast<JObject>())
        {
            JObject inputs = node["inputs"] as JObject;
            foreach (JProperty property in inputs.Properties().ToArray())
            {
                if (property.Value is JArray jarr && jarr.Count == 2 && $"{jarr[0]}" == target0 && $"{jarr[1]}" == target1)
                {
                    inputs[property.Name] = newNode;
                }
            }
        }
    }
```

- [ ] **Step 2: Replace only the facade body**

```csharp
    public void ReplaceNodeConnection(JArray oldNode, JArray newNode)
    {
        GetGraphEditor().ReplaceNodeConnection(oldNode, newNode);
    }
```

- [ ] **Step 3: Inspect recognition/order/error parity**

```bash
git diff --check -- src/BuiltinExtensions/ComfyUIBackend/WorkflowGenerator.cs src/BuiltinExtensions/ComfyUIBackend/WorkflowGraphEditor.cs
git diff -- src/BuiltinExtensions/ComfyUIBackend/WorkflowGenerator.cs src/BuiltinExtensions/ComfyUIBackend/WorkflowGraphEditor.cs
git show e9d99dead294740265676c78a711b03593906211:src/BuiltinExtensions/ComfyUIBackend/WorkflowGenerator.cs | sed -n '3348,3372p'
rg -n 'ReplaceNodeConnection|Values\\(\\)\\.Cast|inputs\\.Properties\\(\\)\\.ToArray|jarr.Count == 2|UsedInputs = null' src/BuiltinExtensions/ComfyUIBackend/WorkflowGraphEditor.cs
```

Expected: exact two-token matching and snapshot order moved unchanged; the final search finds no `UsedInputs = null` inside replacement.

- [ ] **Step 4: Commit stage 4**

```bash
git add -- src/BuiltinExtensions/ComfyUIBackend/WorkflowGenerator.cs src/BuiltinExtensions/ComfyUIBackend/WorkflowGraphEditor.cs
git diff --cached --name-status
git diff --cached --check
git commit -m "refactor: extract workflow connection replacement"
```

- [ ] **Step 5: Obtain focused maintainer validation**

Reaper176 builds and compares cases 15–17 plus case 24.

Required token: `RANK24_STAGE4_PARITY_PASSED`, or exact failed/unrun cases and diffs.

## Task 6: Delegate connectivity indexing

**Files:**
- Modify: `src/BuiltinExtensions/ComfyUIBackend/WorkflowGraphEditor.cs`
- Modify: `src/BuiltinExtensions/ComfyUIBackend/WorkflowGenerator.cs:3355-3382`
- Preserve: public `WorkflowGenerator.UsedInputs`

- [ ] **Step 1: Add exact connectivity logic**

```csharp
    /// <summary>Returns whether a node output has an outbound connection in the current cached index.</summary>
    public bool NodeIsConnectedAnywhere(string nodeId, int ind = -1, string exclude = null)
    {
        if (Generator.UsedInputs is null)
        {
            Generator.UsedInputs = [];
            foreach (JProperty node in Generator.Workflow.Properties())
            {
                if (node.Name == exclude)
                {
                    continue;
                }
                JObject inputs = node.Value["inputs"] as JObject;
                foreach (JProperty property in inputs.Properties().ToArray())
                {
                    if (property.Value is JArray jarr && jarr.Count == 2)
                    {
                        Generator.UsedInputs.Add($"{jarr[0]}:-1");
                        Generator.UsedInputs.Add($"{jarr[0]}:{jarr[1]}");
                    }
                }
            }
        }
        return Generator.UsedInputs.Contains($"{nodeId}:{ind}");
    }
```

- [ ] **Step 2: Replace only the facade body**

Keep the public `UsedInputs` field in its exact location/type. Replace the method body with:

```csharp
    public bool NodeIsConnectedAnywhere(string nodeId, int ind = -1, string exclude = null)
    {
        return GetGraphEditor().NodeIsConnectedAnywhere(nodeId, ind, exclude);
    }
```

- [ ] **Step 3: Inspect public-cache and exclusion parity**

```bash
git diff --check -- src/BuiltinExtensions/ComfyUIBackend/WorkflowGenerator.cs src/BuiltinExtensions/ComfyUIBackend/WorkflowGraphEditor.cs
git diff -- src/BuiltinExtensions/ComfyUIBackend/WorkflowGenerator.cs src/BuiltinExtensions/ComfyUIBackend/WorkflowGraphEditor.cs
git show e9d99dead294740265676c78a711b03593906211:src/BuiltinExtensions/ComfyUIBackend/WorkflowGenerator.cs | sed -n '3368,3404p'
rg -n 'public HashSet<string> UsedInputs|UsedInputs is null|node.Name == exclude|:-1|Contains\\(' src/BuiltinExtensions/ComfyUIBackend/WorkflowGenerator.cs src/BuiltinExtensions/ComfyUIBackend/WorkflowGraphEditor.cs
```

Expected: the public field declaration is unchanged; exclusion applies only during a null-cache build; wildcard/specific strings are exact; no invalidation was added elsewhere.

- [ ] **Step 4: Commit stage 5**

```bash
git add -- src/BuiltinExtensions/ComfyUIBackend/WorkflowGenerator.cs src/BuiltinExtensions/ComfyUIBackend/WorkflowGraphEditor.cs
git diff --cached --name-status
git diff --cached --check
git commit -m "refactor: extract workflow connectivity indexing"
```

- [ ] **Step 5: Obtain focused maintainer validation**

Reaper176 builds and compares cases 18–20, 23, and 24.

Required token: `RANK24_STAGE5_PARITY_PASSED`, or exact failed/unrun cases and diffs.

## Task 7: Delegate unused-node cleanup

**Files:**
- Modify: `src/BuiltinExtensions/ComfyUIBackend/WorkflowGraphEditor.cs`
- Modify: `src/BuiltinExtensions/ComfyUIBackend/WorkflowGenerator.cs:3384-3413`

- [ ] **Step 1: Add single-class cleanup**

```csharp
    /// <summary>Removes nodes of one class when they have no outbound connection.</summary>
    public void RemoveClassIfUnused(string classType)
    {
        Generator.UsedInputs = null;
        RunOnNodesOfClass(classType, (id, data) =>
        {
            if (!NodeIsConnectedAnywhere(id))
            {
                Generator.Workflow.Remove(id);
            }
        });
    }
```

- [ ] **Step 2: Add fixed-point multi-class cleanup**

```csharp
    /// <summary>Repeatedly removes requested classes until no disconnected candidate remains.</summary>
    public void RemoveClassesIfUnused(HashSet<string> classTypes)
    {
        bool run = true;
        while (run)
        {
            Generator.UsedInputs = null;
            run = false;
            foreach (JProperty property in NodesOfClasses(classTypes))
            {
                if (!NodeIsConnectedAnywhere(property.Name))
                {
                    Generator.Workflow.Remove(property.Name);
                    run = true;
                }
            }
        }
    }
```

- [ ] **Step 3: Replace both facade bodies**

```csharp
    public void RemoveClassIfUnused(string classType)
    {
        GetGraphEditor().RemoveClassIfUnused(classType);
    }
```

```csharp
    public void RemoveClassesIfUnused(HashSet<string> classTypes)
    {
        GetGraphEditor().RemoveClassesIfUnused(classTypes);
    }
```

- [ ] **Step 4: Inspect pass/cache/removal parity**

```bash
git diff --check -- src/BuiltinExtensions/ComfyUIBackend/WorkflowGenerator.cs src/BuiltinExtensions/ComfyUIBackend/WorkflowGraphEditor.cs
git diff -- src/BuiltinExtensions/ComfyUIBackend/WorkflowGenerator.cs src/BuiltinExtensions/ComfyUIBackend/WorkflowGraphEditor.cs
git show e9d99dead294740265676c78a711b03593906211:src/BuiltinExtensions/ComfyUIBackend/WorkflowGenerator.cs | sed -n '3398,3420p'
rg -n 'RemoveClassIfUnused|RemoveClassesIfUnused|UsedInputs = null|bool run = true|run = false|run = true|Workflow.Remove' src/BuiltinExtensions/ComfyUIBackend/WorkflowGraphEditor.cs
```

Expected: single cleanup clears once and uses one cached pass; multi cleanup clears each pass and repeats only after removal; no helper cleanup, sort, guard, or renumbering appears.

- [ ] **Step 5: Commit stage 6**

```bash
git add -- src/BuiltinExtensions/ComfyUIBackend/WorkflowGenerator.cs src/BuiltinExtensions/ComfyUIBackend/WorkflowGraphEditor.cs
git diff --cached --name-status
git diff --cached --check
git commit -m "refactor: extract workflow unused-node cleanup"
```

- [ ] **Step 6: Obtain focused maintainer validation**

Reaper176 builds and compares cases 21–24.

Required token: `RANK24_STAGE6_PARITY_PASSED`, or exact failed/unrun cases and diffs.

## Task 8: Perform complete source projection and compatibility review

**Files:**
- Inspect: `src/BuiltinExtensions/ComfyUIBackend/WorkflowGenerator.cs`
- Inspect: `src/BuiltinExtensions/ComfyUIBackend/WorkflowGraphEditor.cs`
- Compare only: all approved-base consumers
- No source edit unless a finding is confirmed

- [ ] **Step 1: Prove the exact production projection**

Resolve the first and last source commits by the exact production paths, verify
both were found, and inspect their range:

```bash
rank24_stage1_commit=$(git log --reverse --format=%H 5ce36366ca64482f59e965ab62a86a7d779bdc1f..HEAD -- src/BuiltinExtensions/ComfyUIBackend/WorkflowGenerator.cs src/BuiltinExtensions/ComfyUIBackend/WorkflowGraphEditor.cs | head -n 1)
rank24_source_head_commit=$(git log -1 --format=%H 5ce36366ca64482f59e965ab62a86a7d779bdc1f..HEAD -- src/BuiltinExtensions/ComfyUIBackend/WorkflowGenerator.cs src/BuiltinExtensions/ComfyUIBackend/WorkflowGraphEditor.cs)
test -n "$rank24_stage1_commit"
test -n "$rank24_source_head_commit"
git diff --name-status "$rank24_stage1_commit^..$rank24_source_head_commit"
git diff --stat "$rank24_stage1_commit^..$rank24_source_head_commit"
git diff --check "$rank24_stage1_commit^..$rank24_source_head_commit"
git diff --numstat "$rank24_stage1_commit^..$rank24_source_head_commit"
```

Expected: exactly the two intended production files. Record commit range, numstat, and resulting blobs.

- [ ] **Step 2: Compare the public facade declaration inventory**

Compare approved-base and candidate declarations without writing temporary
files:

```bash
diff -u <(git show e9d99dead294740265676c78a711b03593906211:src/BuiltinExtensions/ComfyUIBackend/WorkflowGenerator.cs | rg '^    public ') <(rg '^    public ' src/BuiltinExtensions/ComfyUIBackend/WorkflowGenerator.cs)
```

Expected: no output.

- [ ] **Step 3: Prove collaborator state and delegation coverage**

```bash
rg -n '^    private readonly WorkflowGenerator Generator;|Generator\\.(Workflow|LastID|NodeHelpers|UsedInputs)' src/BuiltinExtensions/ComfyUIBackend/WorkflowGraphEditor.cs
rg -n 'return GetGraphEditor\\(\\)\\.|GetGraphEditor\\(\\)\\.' src/BuiltinExtensions/ComfyUIBackend/WorkflowGenerator.cs
rg -n 'private .* (Workflow|NodeHelpers|LastID|UsedInputs)( =|;)' src/BuiltinExtensions/ComfyUIBackend/WorkflowGraphEditor.cs || true
```

Expected: one stored generator field; all ten selected method names/eleven public
declarations delegate; the final search emits nothing.

- [ ] **Step 4: Prove protected and out-of-scope consumers are unchanged**

```bash
git diff --exit-code e9d99dead294740265676c78a711b03593906211 -- src/BuiltinExtensions/ComfyUIBackend/WorkflowGeneratorModelSupport.cs src/BuiltinExtensions/ComfyUIBackend/WorkflowGeneratorSteps.cs src/BuiltinExtensions/ComfyUIBackend/WGNodeData.cs src/BuiltinExtensions/ComfyUIBackend/ComfyUIAPIAbstractBackend.cs src/BuiltinExtensions/DynamicThresholding/DynamicThresholdingExtension.cs tools/SwarmUI-LatentTools tools/SwarmUI-LatentColorTools
git diff --exit-code e9d99dead294740265676c78a711b03593906211 -- src/SwarmUI.csproj
```

Expected: both exit `0`.

- [ ] **Step 5: Inspect prohibited semantic drift**

```bash
rg -n 'lock \\(|try|catch|throw new|OrderBy|Sort|DeepClone|Clone|UsedInputs = null|Workflow = \\[\\]|NodeHelpers =|LastID =' src/BuiltinExtensions/ComfyUIBackend/WorkflowGraphEditor.cs src/BuiltinExtensions/ComfyUIBackend/WorkflowGenerator.cs
git diff -U8 e9d99dead294740265676c78a711b03593906211 -- src/BuiltinExtensions/ComfyUIBackend/WorkflowGenerator.cs
```

Manually account for:

- the one approved stable-ID explicit throw;
- approved cleanup cache clears;
- pre-existing unrelated matches in `WorkflowGenerator`;
- no new lock/catch/clone/sort;
- no `Generate` reset change; and
- no public state initializer change.

- [ ] **Step 6: Complete independent source specification review**

Review the fixed source range against every design goal, non-goal, primitive contract, ABI constraint, and rollback stage. Return exactly `SOURCE_SPEC_APPROVED` if no finding remains.

If a finding exists, report file/line/evidence and stop. Correct only the confirmed issue in a separate source commit, rerun its focused maintainer cases, and repeat this review.

- [ ] **Step 7: Complete independent source quality review**

Review ownership clarity, naming, XML field documentation, C# style, needless allocation/state, method order, exact exception/partial-mutation behavior, and out-of-scope drift. Return exactly `SOURCE_QUALITY_APPROVED` if no finding remains.

Correct any finding separately, rerun affected focused cases, and repeat both source reviews.

## Task 9: Run the complete maintainer build and exact parity matrix

**Files:**
- No repository edits
- Maintainer-owned base/candidate captures outside repository

- [ ] **Step 1: Present the exact unchanged 42-case matrix**

Provide Reaper176 the numbered matrix in this plan without rewording, merging, or adding cases after implementation. Identify any case already marked unavailable in the baseline manifest.

- [ ] **Step 2: Have the maintainer build the final source head**

Reaper176 performs the normal build in the selected candidate workspace. Agents do not provide or run a build command unless the maintainer requests documentation for their own execution.

Record the maintainer's exact build result. A failed build blocks runtime validation and completion.

- [ ] **Step 3: Compare primitive and ABI cases 1–24**

The maintainer runs the candidate with the unchanged approved-base fixture assembly and compares exact records. The fixture digest must equal the approved-base manifest.

Any assembly-load failure, missing member, graph/state mismatch, ordering difference, or exception difference fails its case.

- [ ] **Step 4: Compare representative workflow cases 25–42**

The maintainer uses the frozen input/capability manifest and compares compact graph captures byte for byte. Generated media similarity is not a substitute for graph equality.

Unavailable cases remain unrun. Nondeterministic differences must be frozen and rerun rather than normalized away.

- [ ] **Step 5: Request exact maintainer evidence**

Ask Reaper176 to report:

- exact passed, failed, and unrun case numbers;
- whether the final build passed;
- whether the unchanged precompiled fixture loaded;
- fixture digest match;
- whether every successful graph was byte-identical after `Formatting.None`;
- whether every expected-failure record matched;
- date;
- OS/distribution;
- filesystem;
- relevant backend/Comfy topology;
- browser/version only if browser-mediated capture was used; and
- any unavailable model family/optional dependency.

Preserve the raw wording exactly. Do not infer omitted setup or outcomes.

- [ ] **Step 6: Gate documentation**

Proceed to Task 10 only if the supplied evidence is internally consistent. Rank 24 is fully validated only if all 42 cases pass. Failed or unrun cases produce an implemented/partially validated record, not a full completion claim.

## Task 10: Record implementation, review, and validation evidence

**Files:**
- Modify: `docs/superpowers/specs/2026-07-28-workflow-graph-editor-design.md`
- Modify: `docs/superpowers/audits/2026-07-21-maintainability-architecture-refresh.md`

- [ ] **Step 1: Update the design implementation record**

Use `apply_patch` to change the status and add:

- approved source/audit base;
- design and plan commits;
- all six production commits and final source head;
- exact two-file source projection, numstat, and blobs;
- each focused parity token/result;
- `SOURCE_SPEC_APPROVED` and `SOURCE_QUALITY_APPROVED`;
- unchanged public declaration comparison;
- unchanged out-of-scope consumer comparison;
- raw final maintainer evidence;
- disclosed normalization only where necessary;
- exact passed/failed/unrun counts;
- supplied environment/topology/browser details;
- explicit unprovided details;
- static-only agent boundary; and
- remaining platform/filesystem/model/extension/performance caveats.

- [ ] **Step 2: Update Comfy F26**

Record the internal editor as the low-level implementation owner while keeping `WorkflowGenerator` public state/facade authoritative. Include exact production range, stage boundaries, ABI preservation, graph/error parity result, reviews, and evidence limits.

- [ ] **Step 3: Update roadmap Rank 24 and recommendation**

If all 42 cases passed, mark Rank 24 **Implemented and maintainer-validated** and remove it as Recommended Next Project. State that ranks 25–32 are measurement prerequisites and that no production refactor is recommended next without a measurement gate.

If any case failed or was unrun, record the exact partial result and retain the appropriate unresolved recommendation/status without calling it fully validated.

- [ ] **Step 4: Check documentation consistency**

```bash
rg -n 'Rank 24|workflow graph editor|Comfy F26|Recommended Next Project|SOURCE_SPEC_APPROVED|SOURCE_QUALITY_APPROVED' docs/superpowers/specs/2026-07-28-workflow-graph-editor-design.md docs/superpowers/audits/2026-07-21-maintainability-architecture-refresh.md
git diff --check -- docs/superpowers/specs/2026-07-28-workflow-graph-editor-design.md docs/superpowers/audits/2026-07-21-maintainability-architecture-refresh.md
git diff -- docs/superpowers/specs/2026-07-28-workflow-graph-editor-design.md docs/superpowers/audits/2026-07-21-maintainability-architecture-refresh.md
```

Expected: every status/result/provenance statement agrees and no claim exceeds the raw evidence.

- [ ] **Step 5: Commit the implementation/validation record**

```bash
git add -- docs/superpowers/specs/2026-07-28-workflow-graph-editor-design.md docs/superpowers/audits/2026-07-21-maintainability-architecture-refresh.md
git diff --cached --name-status
git diff --cached --check
git commit -m "docs: record workflow graph editor extraction"
```

Expected: exactly the design and audit are committed.

- [ ] **Step 6: Complete validation specification review**

Review raw evidence, normalization, case counts, environment, fixture/build/graph/exception claims, and partial/full status. Return exactly `VALIDATION_SPEC_APPROVED` if the record matches the matrix and supplied evidence.

- [ ] **Step 7: Complete validation quality review**

Review reproducibility, provenance, wording precision, static/runtime separation, unavailable-case treatment, and remaining caveats. Return exactly `VALIDATION_QUALITY_APPROVED` if no finding remains.

Correct documentation findings in separate documentation-only commits and rerun both validation reviews.

- [ ] **Step 8: Complete final integrated review**

Review the entire range from the approved design through final documentation. Confirm:

- six staged delegates and independent rollback boundaries;
- exactly two production source files;
- no source changes after the reviewed final source head;
- unchanged public declaration inventory;
- unchanged out-of-scope consumers;
- all focused parity evidence;
- final 42-case evidence;
- all four approval tokens;
- consistent Rank 24/Comfy F26/recommendation status;
- empty index; and
- protected primary-worktree changes remain exact and unstaged.

Return exactly `FINAL_INTEGRATED_APPROVED` if no finding remains. Correct any documentation finding separately and rerun.

## Completion Evidence

Rank 24 is complete only when all applicable evidence is recorded:

- approved base `e9d99dead294740265676c78a711b03593906211`;
- approved design head `5ce36366ca64482f59e965ab62a86a7d779bdc1f`;
- committed implementation plan;
- selected execution workspace authorization;
- `RANK24_BASELINE_CAPTURED`;
- six production commits;
- `RANK24_STAGE1_PARITY_PASSED`;
- `RANK24_STAGE2_PARITY_PASSED`;
- `RANK24_STAGE3_PARITY_PASSED`;
- `RANK24_STAGE4_PARITY_PASSED`;
- `RANK24_STAGE5_PARITY_PASSED`;
- `RANK24_STAGE6_PARITY_PASSED`;
- exact two-file source projection and blobs;
- `SOURCE_SPEC_APPROVED`;
- `SOURCE_QUALITY_APPROVED`;
- maintainer build result;
- raw final 42-case evidence and exact normalized counts;
- `VALIDATION_SPEC_APPROVED`;
- `VALIDATION_QUALITY_APPROVED`;
- `FINAL_INTEGRATED_APPROVED`;
- consistent design/audit status; and
- protected worktree/accounting evidence.

If any matrix case is failed or unrun, implementation may be recorded, but Rank 24 must not be described as completely maintainer-validated.

## Rollback

Rollback in reverse production order:

1. restore both unused-node cleanup bodies to `WorkflowGenerator`;
2. restore connectivity indexing;
3. restore connection replacement;
4. restore class queries and traversal;
5. restore node creation and deduplication;
6. restore existence/stable-ID bodies; and
7. remove the now-unused editor file and private lazy binding.

After any rollback, Reaper176 reruns the affected focused cases and the external ABI case. If the failure cannot be isolated, revert all six Rank 24 production commits together and retain the approved-base implementation.
