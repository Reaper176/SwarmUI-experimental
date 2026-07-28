# Facade-Preserving Workflow Graph Editor Design

**Status:** Design approved; implementation not started

**Date:** 2026-07-28

**Rank:** 24 — Extract a facade-preserving workflow graph editor

**Approved source/audit base:** `e9d99dead294740265676c78a711b03593906211`

## Summary

At the approved source/audit base, `WorkflowGenerator` owns both high-level
generation behavior and the low-level mechanics used to build and rewrite a
Comfy workflow graph. Its public mutable state and public step APIs are also
extension surfaces. Core generation code, model support, built-in steps,
`WGNodeData`, Dynamic Thresholding, repository tool examples, and unknown
external extensions can all operate on the same `WorkflowGenerator` instance.

Rank 24 extracts only the low-level graph mechanics into an internal
`WorkflowGraphEditor`. `WorkflowGenerator` remains the public facade, and every
existing public graph method continues to be declared there. The editor holds
only a reference to its owning generator and reads or writes the generator's
existing public fields at the time of each operation. It does not mirror graph
state.

This design preserves already-compiled external-extension compatibility. Public
fields remain fields, public methods retain their signatures, the implicit
public parameterless constructor remains available, and static step/list
surfaces remain unchanged. The accepted parity gate is exact graph and exception
parity: comparison may remove insignificant JSON whitespace, but it must not
sort properties, renumber nodes, canonicalize connections, or otherwise weaken
the comparison.

Rank 24 is an ownership refactor, not a correctness or performance project. It
does not change model loading, conditioning, sampling, media behavior, graph
formats, extension orchestration, cache semantics, malformed-graph behavior, or
concurrency.

## Approved-Base Boundary

The primary `WorkflowGenerator.cs` partial contains 3,414 lines at the approved
boundary. File length alone is not the finding. The relevant problem is that the
following graph primitives are embedded beside unrelated generation behavior:

- `GetStableDynamicID`;
- both `CreateNode` overloads;
- `Generate`'s graph reset;
- `HasNode`;
- `NodesOfClass`;
- `NodesOfClasses`;
- `RunOnNodesOfClass`;
- `ReplaceNodeConnection`;
- `NodeIsConnectedAnywhere`;
- `RemoveClassIfUnused`; and
- `RemoveClassesIfUnused`.

`WorkflowGenerator.NodePath` constructs a two-token connection value but does
not inspect or mutate graph state. It remains on the facade and is outside the
extraction.

The graph-related public mutable state is:

- `Workflow`, the current `JObject` graph;
- `NodeHelpers`, including generic-node deduplication entries;
- `LastID`, the automatic sequential-ID counter; and
- `UsedInputs`, the lazily built connectivity index.

Other public state—including model/media trackers, flags, compatibility
properties, `Steps`, `ModelGenSteps`, and their registration methods—constrains
the seam but does not move into the editor.

At the approved base:

- `Generate` replaces `Workflow` with a new empty `JObject`, then invokes the
  current ordered `Steps` against the full generator.
- `AddStep` and `AddModelGenStep` replace their public lists with priority-sorted
  lists.
- Built-in and extension delegates receive the whole generator and can call
  public graph methods, replace public fields, mutate `Workflow` directly, and
  update unrelated trackers.
- Final built-in cleanup steps combine facade graph methods with direct
  `Workflow` lookup and removal.
- `WGNodeData` reads source-node data directly from `Gen.Workflow`.
- Dynamic Thresholding registers an ordinary priority step, creates a node
  through the facade, and replaces `CurrentModel`.
- Repository tool examples under `tools/` also call facade node-creation
  methods; they are compatibility evidence outside the maintained core consumer
  inventory.
- `ComfyUIAPIAbstractBackend` constructs `WorkflowGenerator` through its public
  parameterless constructor and object initializer.
- External extensions may have been compiled against any existing public field,
  method, list, or constructor and cannot be completely inventoried.

## Problem and Risk

Graph ownership is currently inseparable from the generator's larger mutable
feature surface. A behavior-preserving change to ID allocation, node creation,
traversal, rewriting, connectivity indexing, or cleanup therefore requires
reasoning through a large class containing unrelated model, conditioning,
sampling, image, video, and audio behavior.

The extraction itself is high risk because small mechanical differences are
observable:

- changing when `LastID` increments changes later node IDs;
- changing insertion or traversal order changes serialized JSON;
- retaining a replaced `Workflow` reference hides external mutations;
- changing deduplication keys changes node reuse;
- eager cache invalidation changes `UsedInputs` behavior;
- safer null handling changes exception behavior;
- live traversal instead of a snapshot changes callback/removal behavior; and
- converting fields to properties can break already-compiled extensions.

Rank 24 narrows the owner while deliberately preserving these behaviors.

## Goals

- Give low-level workflow graph mechanics one internal owner.
- Preserve `WorkflowGenerator` as the only public facade for existing graph
  methods.
- Preserve every existing public field as a field with its current name and
  declared type.
- Preserve public methods, static step APIs, mutable public lists, obsolete
  properties, and the public parameterless constructor.
- Preserve extension-visible direct replacement and mutation of graph-related
  public fields.
- Preserve exact node IDs, node/property order, values, connections,
  deduplication, traversal snapshots, cleanup order, and partial mutations.
- Preserve exception type, message where explicitly supplied, timing, and
  post-failure state.
- Delegate primitives in independently reversible stages.
- Establish an exact before/after graph-comparison method and a precompiled
  external-extension compatibility probe.

## Non-Goals

- Extract model loading, conditioning, sampling, prompt handling, media
  conversion, or generation steps.
- Change `WorkflowGeneratorSteps`, `WorkflowGeneratorModelSupport`, `WGNodeData`,
  Dynamic Thresholding, repository tool examples, or external extension source.
- Replace, wrap, sort, or canonicalize `JObject`.
- Convert public fields to properties or introduce a new public graph API.
- Replace anonymous generation steps or change step registration.
- Rename nodes, steps, inputs, helper keys, or public members.
- Change fixed, sequential, or stable dynamic ID ranges.
- Correct cache invalidation or make `UsedInputs` private.
- Add validation, null guards, exception wrapping, fallback, logging, rollback,
  locks, or thread-safety guarantees.
- Remove direct `Workflow` reads, writes, or removals from existing consumers.
- Add committed comparison instrumentation or a new automated-test framework.
- Claim a performance improvement or authorize measurement project P7.

## Considered Approaches

### Facade-bound internal editor

Create one internal editor per generator. The editor retains its generator and
reads or writes the facade's current public fields for every operation. Existing
public methods delegate to it.

This is the selected approach. It creates a real implementation owner while
preserving external field replacement, direct mutation, public signatures, and
binary compatibility.

### Mirrored editor state

Move graph state into the editor and synchronize public facade state before and
after every call.

This offers stronger internal encapsulation, but public fields can be replaced
or mutated between arbitrary extension steps. Synchronization would be
bidirectional, ambiguous, and vulnerable to stale references. It also invites a
later field-to-property migration that Rank 24 does not authorize.

### Stateless graph helpers

Move method bodies into static helpers and pass each required state value or
callback explicitly.

This is mechanically small but leaves state ownership distributed across the
facade and call sites. ID and cache updates would require ref/out plumbing or
facade callbacks, making the boundary harder to understand without materially
narrowing mutation ownership.

## Chosen Architecture

### Internal collaborator

Add `src/BuiltinExtensions/ComfyUIBackend/WorkflowGraphEditor.cs` containing an
internal sealed `WorkflowGraphEditor` in the existing
`SwarmUI.Builtin_ComfyUIBackend` namespace.

The editor receives one `WorkflowGenerator` reference. It must not retain
separate references to:

- `Workflow`;
- `NodeHelpers`;
- `UsedInputs`; or
- any model/media tracker.

Every method observes the generator's current fields when invoked. If an
external step replaces `Workflow`, `NodeHelpers`, or `UsedInputs`, or assigns
`LastID`, the next facade operation sees that assignment.

`WorkflowGenerator` lazily creates its editor through private implementation
state. Every new explicit field, including the editor's generator reference and
the facade's lazy collaborator field, receives repository-required XML
documentation. No explicit public constructor is added, so the existing
implicit public parameterless constructor remains the construction surface.

The editor introduces no locks and assumes no new lifecycle. A generator and
its editor continue to have the same effective request-local usage as the
approved base.

### Public facade

The following public methods remain declared on `WorkflowGenerator` with their
existing signatures and documentation:

- `GetStableDynamicID`;
- both `CreateNode` overloads;
- `HasNode`;
- `NodesOfClass`;
- `NodesOfClasses`;
- `RunOnNodesOfClass`;
- `ReplaceNodeConnection`;
- `NodeIsConnectedAnywhere`;
- `RemoveClassIfUnused`; and
- `RemoveClassesIfUnused`.

Their bodies delegate to the internal editor. No existing caller is changed to
reference `WorkflowGraphEditor`.

`Generate` remains on `WorkflowGenerator`. It continues to replace `Workflow`
before enumerating `Steps`, preserves current `SkipFurtherSteps` behavior, and
returns the public `Workflow` field. Because the editor follows the generator
rather than a stored graph reference, no editor reset or rebind is needed.

`NodePath` remains a static facade helper with its current implementation.

### Public state remains authoritative

The editor uses the public facade fields as the sole state authority:

| Concern | Authoritative state |
| --- | --- |
| Graph contents and property order | `WorkflowGenerator.Workflow` |
| Automatic sequential ID | `WorkflowGenerator.LastID` |
| Generic-node deduplication | `WorkflowGenerator.NodeHelpers` |
| Cached outgoing connection keys | `WorkflowGenerator.UsedInputs` |

The design intentionally does not hide, duplicate, snapshot across calls, or
replace these fields. This limitation is required for already-compiled
extension compatibility.

## Primitive Contracts

### Node existence and stable dynamic IDs

`HasNode` continues to call `Workflow.ContainsKey(id)` against the current
public graph.

`GetStableDynamicID(index, offset)` continues to:

1. test integer offsets with `i` from zero while `i < 99999`;
2. calculate `1000 + index + offset + i`;
3. format the candidate as a decimal string;
4. return the first candidate not present in the current graph; and
5. throw `Exception("Failed to find a stable dynamic ID.")` after exhaustion.

The editor does not reserve the returned ID or mutate `LastID`.

### Configured node creation

The callback overload continues to:

1. use the supplied ID, or consume the current `LastID` through post-increment;
2. create a `JObject` whose first property is `class_type`;
3. invoke the configuration callback with the resolved ID and new object;
4. insert or replace `Workflow[id]` only after the callback returns; and
5. return the resolved ID.

If the callback throws after an automatic ID was selected, `LastID` remains
incremented and the new node is not inserted. The editor must not restore the
counter, wrap the exception, or publish the partially configured object.

### Generic-input node creation and deduplication

The `JObject` overload continues to:

1. construct the exact lookup string
   `__generic_node__{classType}___{input}`;
2. consult `NodeHelpers` only when `id` is null or `idMandatory` is false;
3. return the existing helper value immediately when found;
4. otherwise call the callback overload, assigning the supplied `input` object
   as the node's `inputs`; and
5. assign `NodeHelpers[lookup]` only after creation succeeds.

String construction, `JObject.ToString()` behavior, reference reuse of the input
object, explicit-ID replacement, and helper overwrite behavior remain
unchanged.

### Class queries and traversal

`NodesOfClass` and `NodesOfClasses` continue to enumerate
`Workflow.Properties()` in `JObject` property order and materialize a
`JProperty[]` snapshot.

Class matching continues to interpolate the `class_type` token as a string. No
shape validation or comparer changes are added.

`RunOnNodesOfClass` continues to obtain the snapshot first and invoke callbacks
in snapshot order. A callback may directly remove graph properties without
invalidating or changing the pending snapshot.

### Connection replacement

`ReplaceNodeConnection` continues to:

1. stringify both tokens of `oldNode`;
2. enumerate current workflow values and cast each to `JObject`;
3. read each node's `inputs` as `JObject`;
4. snapshot the input properties;
5. recognize only `JArray` values whose count is exactly two and whose two
   stringified tokens match; and
6. assign the supplied `newNode` object to the matching input property.

It does not clone `newNode`, inspect nested arrays, rewrite output declarations,
invalidate `UsedInputs`, or tolerate shapes that currently fail.

### Connectivity indexing

`NodeIsConnectedAnywhere` continues to build the public `UsedInputs` set only
when that field is null.

During a build it:

1. scans workflow properties in order;
2. skips a node only when its property name equals the current `exclude`;
3. scans the node's `inputs` properties;
4. recognizes only two-element `JArray` values; and
5. adds both `{sourceNode}:-1` and `{sourceNode}:{outputIndex}` string keys.

After the cache exists, later `exclude` values do not rebuild it. The final
lookup remains `UsedInputs.Contains($"{nodeId}:{ind}")`. Rank 24 does not alter
this behavior or add invalidation to creation, replacement, or direct graph
mutation.

### Unused-node cleanup

`RemoveClassIfUnused` continues to:

1. assign `UsedInputs = null`;
2. traverse a snapshot of the requested class; and
3. remove each node for which `NodeIsConnectedAnywhere(id)` returns false.

The first connectivity query creates one cache used for the remainder of that
single pass.

`RemoveClassesIfUnused` continues fixed-point cleanup:

1. begin with another pass required;
2. clear `UsedInputs` at the start of each pass;
3. traverse a snapshot of all requested classes;
4. remove disconnected nodes in workflow order;
5. request another pass after any removal; and
6. stop after a pass removes nothing.

The editor does not remove helper entries, renumber nodes, alter the class set,
or change the order in which candidate nodes disappear.

## Compatibility Contract

Rank 24 requires source and managed-binary compatibility for existing external
extensions.

The following remain unchanged:

- `WorkflowGenerator` namespace, type name, visibility, and partial nature;
- its implicit public parameterless constructor;
- every existing public field name, visibility, and declared type;
- every existing public method/property signature;
- obsolete compatibility properties and their behavior;
- `WorkflowGenStep`, `Steps`, `ModelGenSteps`, `AddStep`, and
  `AddModelGenStep`;
- ascending step priority and current equal-priority ordering;
- `SkipFurtherSteps` handling;
- fixed IDs below 100 and automatic IDs beginning at 100;
- stable dynamic-ID calculations and reserved conventions;
- direct public `Workflow`/helper/cache/counter access;
- direct graph mutation between facade calls;
- JSON token, property, and node order;
- node class, input, output, and helper-key strings; and
- all API, workflow submission, model, settings, and extension behavior outside
  the delegated primitives.

Adding an internal class and private lazy collaborator state does not authorize
changes to public ABI. A precompiled fixture extension built once against the
approved base must load unchanged with the candidate build and observe the same
members and behavior.

## Error and Partial-Mutation Contract

Delegation adds no validation, exception handling, logging, fallback, cleanup,
or rollback.

Current incidental exceptions from null fields, missing `inputs`, non-object
workflow values, malformed paths, callback failures, and other invalid shapes
remain observable at the same logical operation. Where the approved base
supplies an explicit exception type/message, the candidate must match both.

The comparison must also preserve state at failure:

- whether `LastID` was consumed;
- whether a node was inserted or replaced;
- whether `NodeHelpers` changed;
- whether `UsedInputs` was created or cleared; and
- which graph edits completed before the failure.

No design requirement treats more defensive behavior as an improvement within
Rank 24. Any desired hardening requires a separate design.

## Delegation Stages

Each stage is independently reviewable and reversible:

1. Add the internal editor shell and lazy facade binding; delegate `HasNode` and
   `GetStableDynamicID`.
2. Delegate both `CreateNode` overloads and generic deduplication.
3. Delegate `NodesOfClass`, `NodesOfClasses`, and `RunOnNodesOfClass`.
4. Delegate `ReplaceNodeConnection`.
5. Delegate `NodeIsConnectedAnywhere` while retaining public `UsedInputs`.
6. Delegate `RemoveClassIfUnused` and `RemoveClassesIfUnused`.
7. Perform integrated static review and the complete maintainer parity matrix.

Every production commit must preserve a buildable source state for maintainer
validation. A failed stage can be reverted without reverting previously
validated delegates.

## Exact Graph-Comparison Method

The approved base and each candidate use the same maintainer-controlled capture
fixture and frozen case inputs.

For successful generation, capture:

- the `WorkflowGenerator.Generate()` result serialized with
  `Formatting.None`;
- the case identifier;
- the exact seed and relevant input/preset identity;
- model and model-metadata identity;
- backend capability/object-info identity;
- enabled built-in/external extension set; and
- settings that affect workflow construction.

Compare the compact JSON strings byte for byte. `Formatting.None` removes only
insignificant presentation whitespace. The comparison must not:

- parse and sort object properties;
- renumber nodes;
- reorder arrays;
- normalize numeric values;
- collapse equivalent connections;
- discard optional nodes; or
- compare only generated media.

A digest may accompany a capture for inventory, but a mismatch must retain the
actual graph diff as evidence. Digest equality alone is not the review method.

For expected failure cases, the fixture records:

- a phase marker identifying the primitive operation;
- fully qualified exception type;
- exception message;
- compact post-failure `Workflow`;
- `LastID`;
- ordered `NodeHelpers` entries; and
- null/non-null state plus ordinal-sorted membership of `UsedInputs`.

Those records must match the approved base exactly.

Sorting is permitted only for the validation representation of the
`HashSet<string>` membership because set enumeration order is not graph order.
The fixture must not sort or mutate the runtime set itself. Graph object
properties, arrays, and `NodeHelpers` enumeration remain order-sensitive.

If a case contains nondeterministic graph data, the relevant input or capability
must be frozen. The comparison must not hide the difference through broader
normalization. A case that cannot be made reproducible does not satisfy the
parity gate.

The capture fixture and comparison utility are maintainer validation tools, not
committed production instrumentation. Agents do not build, execute the fixture,
run SwarmUI, submit workflows, automate a browser, or perform runtime
comparisons.

## Maintainer Validation Matrix

The implementation plan must turn these categories into an exact numbered
matrix before source implementation begins.

### Primitive and public-state cases

- Automatic ID allocation beginning from a controlled `LastID`.
- Explicit fixed ID creation and replacement.
- Stable dynamic-ID first candidate, collision skip, offset, and exhaustion
  error.
- Configured-node callback success and callback failure after automatic ID
  consumption.
- Generic-input deduplication for null ID.
- `idMandatory` true and false with an explicit ID.
- `NodeHelpers` replacement between calls.
- `Workflow` replacement between calls.
- `LastID` assignment between calls.
- Class-query ordering with mixed classes and missing/odd `class_type` tokens.
- Snapshot traversal while the callback removes nodes.
- Connection replacement with matching, nonmatching, nested, and non-two-token
  arrays.
- First connectivity build, output-specific lookup, wildcard lookup, exclusion,
  cached repeat, and externally supplied `UsedInputs`.
- Single-pass class cleanup.
- Multi-class cascading fixed-point cleanup.
- Preserved malformed-graph and null-state failures.

### Representative workflow cases

- Major supported image model families available in the maintainer environment.
- LoRA application and scheduled LoRA behavior.
- Text-to-image and deterministic seed reproduction.
- Init image, mask/inpaint, and regional prompting.
- ControlNet and adapter paths.
- Base plus refiner.
- Video generation, image-to-video, and applicable frame operations.
- Audio or audio/video workflow paths available in the environment.
- Raw custom workflow and stored custom workflow paths.
- Optional-node present and absent variants.
- Dynamic Thresholding enabled and disabled.

### External compatibility cases

Build one fixture extension against the approved-base core and do not rebuild it
for candidate validation. It must:

- register a step at a priority that tests ordering against core steps;
- create automatic and fixed-ID nodes through public facade methods;
- replace `Workflow`, `NodeHelpers`, `UsedInputs`, and `LastID` between facade
  calls;
- directly insert, inspect, and remove `Workflow` properties;
- exercise class traversal, replacement, connectivity, and cleanup; and
- update a non-graph tracker so the full-generator delegate contract remains
  covered.

The unchanged compiled DLL must load and produce the same graph, state record,
step ordering, and exceptions with the candidate core.

## Static Verification

Agent static verification must:

1. confirm the approved source/audit base and source projection;
2. inventory all delegated methods and graph-related public fields;
3. confirm every existing public member remains declared with the same
   signature/type;
4. confirm no explicit public constructor replaced or narrowed the implicit
   constructor;
5. confirm the editor stores only the generator reference;
6. trace each facade method to its corresponding editor method;
7. compare the moved control flow statement by statement against the approved
   base;
8. confirm `Generate` and `NodePath` remain on the facade;
9. confirm no consumer was changed to reference the internal editor;
10. confirm direct graph access in steps and `WGNodeData` remains unchanged;
11. confirm Dynamic Thresholding and step/list registration remain unchanged;
12. confirm no eager cache invalidation, cloning, sorting, new guard, catch,
    logging, lock, or rollback was introduced;
13. confirm each production commit contains only its declared delegate stage;
14. run whitespace/error checks that do not build, execute tests, or launch
    runtime code;
15. verify the index excludes protected maintainer work; and
16. record that all runtime, graph, ABI, platform, and filesystem evidence comes
    from the maintainer.

Static inspection cannot establish compiled ABI loading or runtime graph parity.
Those claims require the maintainer matrix.

## Production and Documentation Boundary

The intended production source files are exactly:

- `src/BuiltinExtensions/ComfyUIBackend/WorkflowGenerator.cs`; and
- `src/BuiltinExtensions/ComfyUIBackend/WorkflowGraphEditor.cs`.

The intended documentation files are:

- this design;
- the Rank 24 implementation plan; and
- the Comfy F26, roadmap Rank 24, recommendation, and final-status passages in
  `docs/superpowers/audits/2026-07-21-maintainability-architecture-refresh.md`.

No project file change is expected because the SDK project includes C# source
files by convention. No step, model-support, `WGNodeData`, Dynamic Thresholding,
tool, extension, API documentation, data, generated, downloaded, launch,
frontend, settings, model, or backend API file is a production target.

## Protected Maintainer Work

The approved working tree contains unrelated maintainer changes:

- `src/Data/Settings.fds` — `83 insertions / 3 deletions`;
- `src/Pages/Text2Image.cshtml` — `9 insertions / 2 deletions`;
- `src/wwwroot/js/genpage/gentab/loras.js` — `2 insertions / 0 deletions`;
- `src/wwwroot/js/genpage/main.js` — `0 insertions / 1 deletion`; and
- untracked `Data.pre-restore-2026-07-19/`.

Rank 24 does not authorize any part of that work. Every documentation and
production commit must be staged by exact path and inspected before commit.

## Success Criteria

Rank 24 succeeds only when:

- the internal editor owns every selected graph primitive;
- every existing public facade field/method/list/constructor contract remains
  compatible;
- exact approved-base graph captures equal candidate captures for every
  successful numbered case;
- expected-failure records match type, message, phase, and post-failure state;
- the precompiled approved-base fixture extension loads unchanged and passes;
- source changes remain limited to the two intended production files;
- independent static specification and quality reviews have no unresolved
  findings;
- maintainer Reaper176 provides the exact numbered matrix result and environment
  details; and
- documentation distinguishes static evidence from maintainer runtime evidence
  without inferring unreported setup or results.

## Rollback

Rollback follows delegation ownership in reverse:

1. restore cleanup method bodies to `WorkflowGenerator`;
2. restore connectivity indexing;
3. restore connection replacement;
4. restore traversal and class queries;
5. restore node creation and deduplication;
6. restore node existence and stable-ID selection; and
7. remove the unused editor and private lazy binding.

Each rollback restores the approved-base facade implementation without changing
public members or unrelated generation behavior. If integrated parity fails and
the failing stage cannot be isolated, revert all Rank 24 production delegates
together.

## Evidence Boundary

This document records an approved design, not an implementation or validation
result. No source implementation has begun. No agent build, test, test-running
lint, launcher, server, browser, backend, live API, workflow-generation,
extension-loading, platform, filesystem, ABI, or performance exercise is
claimed.

The recorded runtime target is the maintainer's Garuda Linux (Arch-based), Btrfs
environment. Browser details are relevant only to any browser-mediated capture
procedure and must be recorded if used. Wider platforms, filesystems, model
families, optional dependencies, external extensions, and performance remain
unvalidated unless the final evidence explicitly states otherwise.
