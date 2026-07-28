# Comfy Object-Info Warm Fallback Design

**Status:** Implemented and maintainer-validated; environment and backend topology not provided

**Date:** 2026-07-28

**Rank:** 21 — Use cached object-info safely after fresh-fetch failure

**Approved source/audit base:** `0a814fc86942a15dcdfa3062834a6c7d9ef95718`

**Approved design:** `9369ef993583fc5e8bcc34b18c1a73de4d99e0f8`

**Implementation plan:** `84f21937d121a463177305f539aea8f10cc06ae6`

**Planning/design review corrections:** `c9b9ea4927ecec417fa85e840d1c7df15ad31809` corrected the baseline inventory in the plan and design; `ec68ec35dbbc55a9c411696c43280b4aee2dcdda` clarified the empty-object-info fallthrough in the design; and `ed2e9dd1a5571072c80f95317224906e48e9473c` staged source-review provenance requirements in the plan.

**Production source/head:** `87a7282e04b3452b7607d8c1be25591f06c4b630`

**Exact production projection:** only `src/BuiltinExtensions/ComfyUIBackend/ComfyUIRedirectHelper.cs`, `8 insertions(+), 2 deletions(-)`; resulting blob `693fdffadf09f3abf5e0cf6448e9695470d062b5`.

**Independent source reviews:** `SOURCE_SPEC_APPROVED` and `SOURCE_QUALITY_APPROVED`, with no findings.

**Independent documentation reviews:** `DOCS_SPEC_APPROVED` and `DOCS_QUALITY_APPROVED`, with no findings.

**Validation-document review history:** The initial validation specification review found that two current-architecture statements in the audit still said Comfy F24 awaited maintainer validation. Focused correction commit `8e2601c2891c63bc9ebbed24651ff8f53abd9ee7` (`docs: update Comfy object-info validation status`) fixed both statements, after which the same reviewer's pre-correction re-review returned `VALIDATION_SPEC_APPROVED`. The pre-correction validation quality review returned `VALIDATION_QUALITY_APPROVED` with no findings, and the pre-correction final integrated review returned `FINAL_INTEGRATED_APPROVED` with no findings. Controller final verification then found matrix-section drift: validation evidence had replaced the approved matrix introduction and trailing environment-limitation paragraph, so the full section was not byte-identical to design commit `9369ef993583fc5e8bcc34b18c1a73de4d99e0f8`. Focused correction commit `1cc76ab795d87ac109001e574b231ca8769bab1e` (`docs: restore Comfy validation matrix identity`) restored the matrix section byte-for-byte and moved the validation evidence outside it. The post-correction validation specification and validation quality reviews approved the corrected state through commit `c708bcf364dd5157579eccfb18a1c61d4cc2db94` and returned `VALIDATION_SPEC_APPROVED` and `VALIDATION_QUALITY_APPROVED`; their findings are clear. The current post-correction final integrated review returned `FINAL_INTEGRATED_APPROVED` with no findings.

## Summary

`ComfyUIRedirectHelper.ObjectInfoReadCacher` serves merged Comfy `object_info` to the embedded/direct Comfy UI through a ten-minute cache. A successful calculation fetches the first direct backend's current `object_info`, adds missing node definitions from the local raw information of every available Comfy backend, publishes the merged object as `LastObjectInfo`, and returns it.

At the approved base, the factory attempted to tolerate a later fresh-fetch failure when `LastObjectInfo` existed. That fallback was defective: the catch did not assign the prior object to the private `result`, so the following union faulted if it reached any backend-local property. If the union reached no property, publication was skipped and the prior `LastObjectInfo` was returned without becoming the private merge target. Rank 21 makes the fallback explicit and safe by deep-cloning the prior snapshot before the existing union. The first-ever thrown failure still propagates.

The implemented factory now keeps first-backend selection, fetch, parse, and an explicit null-result failure inside one catch boundary. A cold failure logs and rethrows the active exception. A warm failure captures `LastObjectInfo` once, deep-clones that snapshot, performs the existing ordered missing-only union, and publishes only the completed private result. Every path reaching the merge therefore has a non-null private result, and a union failure occurs before publication. Unexpired cache hits retain the cache wrapper's unchanged behavior.

Agent evidence is static-only. Agents did not build, test, launch, execute scripts, start services or backends, automate a browser, call live APIs, run test-executing lint, or measure runtime or performance. The exact unchanged 20-case Maintainer Validation Matrix remains the authority for the separately recorded maintainer validation.

## Current Boundary

The production owner is the `ObjectInfoReadCacher` factory in:

```text
src/BuiltinExtensions/ComfyUIBackend/ComfyUIRedirectHelper.cs
```

Its maintained consumers are:

- `ComfyBackendDirectHandler`, for cached `object_info`, queried `object_info`, `api/object_info`, and queried `api/object_info` routes when backend-data caching is enabled, including its internal null-data `ForceExpire()` safeguard;
- `ComfyUIBackendExtension.Refresh`, which forces cache expiry before backend value refresh; and
- `ComfyUIWebAPI.ComfyEnsureRefreshable`, which forces cache expiry on request.

These are three maintained `ForceExpire()` call sites: the handler's internal null-data safeguard and the two named external manual-expiry callers.

`LastObjectInfo` has no other maintained reader or writer. `SingleValueExpiringCacheAsync<JObject>` serializes refresh calculation and returns the current value without recalculation until expiry.

Backend-local `ComfyUIAPIAbstractBackend.RawObjectInfo` and capability snapshots are separate owners. Rank 21 reads their existing published values during the union but does not change their construction, publication, or consumers.

## Confirmed Defect

The approved-base factory initializes:

```csharp
JObject result = null;
```

It then attempted a synchronous first-backend GET and parse. Its catch logged the exception and rethrew only when `LastObjectInfo` was null. With a prior snapshot, it continued while `result` was still null.

If the next loop reached any property, it evaluated:

```csharp
result.ContainsKey(property.Name)
```

and could also assign:

```csharp
result[property.Name] = property.Value;
```

The intended stale fallback therefore faulted before it could return `LastObjectInfo` when the union enumerated at least one backend-local property and called `result.ContainsKey`. An empty `RawObjectInfo`, like any path that reached no property, followed the fallthrough described below. The prior published object was not used as the merge target.

If no backend-local property was reached, `result` remained null, publication was skipped, and `return LastObjectInfo` returned the prior object on a warm thrown-failure path. A fresh parse that yielded null followed the same union behavior without entering the catch: reaching any property faulted, while reaching none returned the prior object when warm or null when cold. After a cold null return, `ComfyBackendDirectHandler` invoked its internal `ForceExpire()` safeguard and could then fault when response construction called `data.ToString()`.

At the approved base, first-backend selection occurred before the exception boundary. A transient race that left no direct backend between the handler's initial availability check and cache calculation therefore could not use a warm fallback either.

## Goals

- Return usable cached object-info after a later fresh selection, fetch, parse, or null-result failure.
- Preserve first-ever failure propagation when no prior snapshot exists.
- Deep-clone the prior snapshot before merging so fallback never mutates a published object.
- Continue adding missing node definitions from currently available backend-local raw information.
- Preserve first-backend precedence and union-without-overwrite behavior.
- Keep the correction inside the existing cache factory.

## Non-Goals

- Do not change the Comfy `object_info` schema or reinterpret any node definition.
- Do not add retries, alternate fetch targets, background refresh, timers, cache bounds, or persistence.
- Do not change the ten-minute expiry, the two external manual-expiry callers, or the handler's internal null-data expiry safeguard.
- Do not change backend ordering, first-backend preference, or duplicate-node precedence.
- Do not treat cached node information as proof that a backend or node is currently available.
- Do not modify backend-local `RawObjectInfo`, capability snapshots, capability aggregation, generation, validation, model lists, or workflow construction.
- Do not change permissions, backend-selection headers, route matching, cache-disabled proxying, response shape, status, content type, or transport behavior.
- Do not change the declarations or accessibility of `LastObjectInfo` or `ObjectInfoReadCacher`.
- Do not add a helper, setting, API, schema field, or public member.
- Do not make a performance claim.

## Considered Approaches

### 1. Minimal in-place clone then union

Move first-backend selection inside the existing exception boundary. On failure, rethrow when no prior snapshot exists; otherwise deep-clone the prior snapshot into `result`, then continue through the existing union and publication flow.

This is the chosen approach. It fixes both the null dereference and the transient zero-backend race while preserving the existing owner, ordering, and union behavior.

### 2. Extract refresh and merge helpers

Separate first-backend fetch, fallback acquisition, and union into private methods.

This would make each stage individually named, but the factory is the only consumer and the required correction is one bounded branch. The additional structure would enlarge the review and rollback surface without establishing a reusable policy.

### 3. Return the prior clone immediately

Deep-clone and return `LastObjectInfo` directly from the catch.

This avoids mutation and null dereference but skips the existing union with currently available backend-local raw information. The maintainer explicitly selected clone-then-union behavior.

## Chosen Control Flow

The cache factory retains one private `JObject result`.

1. Enter the existing failure boundary.
2. Select the first direct backend.
3. Fetch and parse its `/object_info`.
4. Treat a null parsed result as a failure before entering the merge.
5. On selection, fetch, parse, or null-result failure:
   1. emit the existing object-info failure log;
   2. read `LastObjectInfo` once into a local;
   3. rethrow the active exception when that local is null; or
   4. deep-clone the local into `result`.
6. Enumerate the currently available direct backends through the existing union loop.
7. For each non-null backend-local `RawObjectInfo`, add only properties whose names are absent from `result`.
8. Publish the complete private result to `LastObjectInfo`.
9. Return that same complete result through the unchanged cache wrapper.

Conceptually:

```text
cache calculation
    → try first-backend selection and fresh fetch/parse
        → non-null result: use fresh private object
        → failure:
            → no prior snapshot: log and rethrow original failure
            → prior snapshot: deep-clone it into private result
    → union missing properties from current backend-local raw info
    → publish completed private result
    → return completed result for ten minutes
```

The zero-backend race follows the same failure branch. A cold race still throws. A warm race clones and returns the prior snapshot, with any backend-local data that becomes available before the union added under the unchanged missing-only policy.

## Snapshot and Concurrency Contract

`LastObjectInfo` is a public volatile reference. Rank 21 preserves that field and does not attempt to control unsupported external mutation.

For maintained operations:

- cache refresh calculation remains serialized by `SingleValueExpiringCacheAsync<JObject>`;
- a successful fresh fetch produces a new private `JObject`;
- a warm fallback produces a deep clone of one locally captured prior reference;
- union writes target only that private fresh object or clone;
- `LastObjectInfo` is replaced only after union completion; and
- no maintained code mutates the previously published object during fallback.

This is snapshot replacement, not in-place repair. A request already holding the prior object may finish against it while a later refresh constructs and publishes another complete object.

The design does not promise that every caller receives a distinct object. Unexpired cache hits continue returning the cache wrapper's current published value.

The prior published snapshot is not a maintained mutation target. Unsupported mutation through the preserved public field remains outside the maintained contract, and broader topology, concurrency, and performance behavior remains unvalidated.

## Merge and Precedence Contract

Successful refresh behavior remains:

1. the first direct backend's fetched object supplies the initial node definitions;
2. available backend-local `RawObjectInfo` objects are visited in existing enumeration order; and
3. only missing property names are added.

Warm fallback behavior becomes:

1. the prior merged snapshot supplies the initial node definitions;
2. available backend-local `RawObjectInfo` objects are visited in the same existing order; and
3. only missing property names are added.

Existing names are never overwritten. A warm fallback can add a node newly known by an available backend, but it cannot refresh or replace a prior definition for an existing node name. That staleness is intentional fallback behavior and ends after a later successful fresh calculation.

## Error Handling

The existing log form remains:

```text
object_info read failure: <readable exception>
```

Fresh HTTP and parse failures retain that diagnostic. Moving first-backend selection into the same boundary means a transient zero-backend selection failure now uses the same diagnostic before cold rethrow or warm fallback.

When no prior snapshot exists, the active selection, fetch, parse, or explicit null-result failure is rethrown. Rank 21 does not convert a first-ever failure into an empty or successful response.

When a prior snapshot exists, the error is still logged, but the factory returns a merged private fallback snapshot. The cache wrapper then retains that successful fallback value under its existing expiration behavior. Rank 21 does not add a shortened retry period after fallback.

Errors thrown during the subsequent union remain outside the fetch fallback and retain their existing propagation. The design fixes the confirmed null target; it does not mask arbitrary malformed or concurrently externally mutated backend-local tokens.

## Compatibility Contract

Rank 21 preserves:

- the public `LastObjectInfo` field declaration;
- the public `ObjectInfoReadCacher` field declaration and cache object;
- ten-minute expiration and manual `ForceExpire` behavior;
- existing `ComfyBackendsDirect()` ordering;
- first-backend fetch preference;
- union-without-overwrite semantics;
- successful response JSON, status, and content type;
- the four cached object-info route forms;
- cache-disabled direct proxy behavior;
- permissions and backend-selection header handling;
- existing backend/client/address selection outside this factory;
- the existing object-info error log text;
- `Refresh` and `ComfyEnsureRefreshable`;
- backend-local raw object-info and capability ownership;
- Comfy UI, workflow, generation, validation, and model behavior;
- every public signature and extension binary-compatibility surface; and
- all unrelated audit ranks and performance gates.

The production change also leaves backend-local `RawObjectInfo`, per-backend capability publication, workflow construction, generation, model handling, and validation unchanged. It does not add alternate-backend retries, a shorter fallback expiry, or freshness guarantees: a fallback may remain stale until a later expired calculation succeeds. Permissions, backend-selection headers, response construction, the four cached routes, cache-disabled proxying, all three `ForceExpire()` sites, and the ten-minute cache remain unchanged.

## Static Verification

Agents perform static verification only. They do not build, test, launch SwarmUI or ComfyUI, automate a browser, execute scripts, start backends or services, call live APIs, run test-executing lint, or make runtime, platform, filesystem, or performance claims.

Static review must:

1. pin the approved source/audit base, branch, index, and protected maintainer state;
2. inventory every maintained `ObjectInfoReadCacher` and `LastObjectInfo` reader/writer;
3. inventory all three `ForceExpire` call sites—the two external manual-expiry callers and the handler's internal null-data safeguard—and all four cached route forms;
4. compare the complete factory before and after;
5. prove first-backend selection occurs within the failure boundary;
6. prove a null fresh result becomes an explicit failure;
7. prove cold failure rethrows when the captured prior snapshot is null;
8. prove warm failure deep-clones one captured prior reference;
9. prove every path reaching the union has a non-null private `result`;
10. prove the union remains missing-only and retains enumeration order;
11. prove publication occurs only after union completion;
12. prove the previously published snapshot is never a maintained mutation target;
13. prove cache duration, expiry callers, routes, permissions, headers, response construction, and cache-disabled behavior are unchanged;
14. prove backend-local capability and generation owners are unchanged;
15. inspect public/member and source-projection diffs;
16. run fixed-range whitespace checks; and
17. confirm the index and protected maintainer work remain isolated.

On 2026-07-28, the authoritative raw maintainer message was exactly `mark as passed and continue`.

The controller disclosed that it interprets this response to the exact unchanged 20-case Rank 21 matrix as instruction and confirmation to mark all 20 cases passed. The normalized result is **20 passed, 0 failed, 0 unrun**. No more specific per-case prose is attributed to the maintainer, and no particular injection method is claimed.

The operating system, filesystem, browser and browser version, Comfy version, number or type of direct or linked backends, and backend topology were not provided for this Rank 21 validation. None is inferred from earlier ranks. No environment or topology behavior beyond the maintainer-confirmed exact matrix is claimed.

Fallback data may remain stale until a later successful expired calculation and is not proof that any node or backend is currently available. Unsupported external mutation through the preserved public field remains outside the maintained contract. Broader platform, filesystem, browser and browser-version, Comfy-version, backend-topology, concurrency, and performance behavior remains unvalidated or unmeasured. Agent evidence remains static-only.

## Maintainer Validation Matrix

The maintainer performs runtime validation and records the date, operating system, filesystem, browser, and Comfy/backend arrangement actually exercised.

1. A cache-enabled cold request with valid first-backend object-info succeeds, publishes the merged snapshot, and returns the existing JSON response shape.
2. A cold first-backend HTTP failure emits the existing object-info error and propagates failure without publishing a fallback.
3. A cold first-backend parse failure emits the existing object-info error and propagates failure without publishing a fallback.
4. A cold transient zero-backend calculation failure propagates and does not create an empty successful snapshot.
5. Repeated unexpired warm-cache reads reuse the cached value without another first-backend fetch.
6. After successful warm-up and forced expiry, a first-backend HTTP failure logs the error and returns cached fallback data.
7. After successful warm-up and forced expiry, a first-backend parse failure logs the error and returns cached fallback data.
8. After successful warm-up and forced expiry, a transient zero-backend calculation returns cached fallback data.
9. A warm fallback leaves the previously published `JObject` and its nested content unchanged.
10. A warm fallback adds a node name present only in currently available backend-local `RawObjectInfo`.
11. A warm fallback does not overwrite an existing cached node definition when backend-local raw information contains the same node name.
12. A successful multi-backend fresh calculation retains first-backend definitions and adds only missing node names in the existing union order.
13. A fallback result is fully constructed before publication and remains stable while it is served.
14. After a fallback, the next expired calculation with a successful fresh fetch replaces stale fallback data with current first-backend data.
15. `ComfyUIBackendExtension.Refresh` still forces the next cached object-info request to calculate again.
16. `ComfyEnsureRefreshable` still forces the next cached object-info request to calculate again.
17. With backend-data caching disabled, object-info requests retain direct proxy behavior and do not use the fallback cache.
18. `object_info`, queried `object_info`, `api/object_info`, and queried `api/object_info` retain their existing cached response behavior.
19. Permissions, backend-selection headers, response status/content type, first-backend preference, and one-calculation-per-expiry serialization remain unchanged.
20. Backend-local capability publication, raw workflow validation, standard generation, and ordinary embedded/direct Comfy UI use remain unchanged.

Results are recorded only for environments and arrangements actually exercised. Other browsers, operating systems, filesystems, Comfy versions, backend topologies, concurrency conditions, and performance remain unvalidated or unmeasured unless explicitly supplied.

## Success Criteria

Rank 21 succeeds when:

- every cache-factory path reaching the union has a non-null private result;
- cold selection, fetch, parse, and null-result failures still fail;
- warm equivalents clone and use prior data;
- fallback union never mutates the previously published snapshot;
- successful refresh and missing-only union precedence remain unchanged;
- cache duration, expiry, routing, responses, permissions, headers, public fields, and backend-local owners remain unchanged;
- independent static specification and quality reviews approve the exact source projection;
- the unchanged 20-case matrix is confirmed by the maintainer; and
- validation evidence distinguishes exercised runtime behavior from static-only and unvalidated claims.

## Rollback

Rollback restores first-backend selection to its original position and removes the explicit null-result and deep-clone fallback logic together. No data, schema, setting, migration, cache cleanup, or persistent-state step is required.
