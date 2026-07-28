# Comfy Object-Info Warm Fallback Design

**Status:** Approved; not implemented

**Date:** 2026-07-28

**Rank:** 21 — Use cached object-info safely after fresh-fetch failure

**Approved source/audit base:** `0a814fc86942a15dcdfa3062834a6c7d9ef95718`

## Summary

`ComfyUIRedirectHelper.ObjectInfoReadCacher` serves merged Comfy `object_info` to the embedded/direct Comfy UI through a ten-minute cache. A successful calculation fetches the first direct backend's current `object_info`, adds missing node definitions from the local raw information of every available Comfy backend, publishes the merged object as `LastObjectInfo`, and returns it.

The factory already attempts to tolerate a later fresh-fetch failure when `LastObjectInfo` exists. That fallback is defective: the catch does not assign the prior object to the private `result`, so the following union loop dereferences null. Rank 21 makes the fallback explicit and safe by deep-cloning the prior snapshot before the existing union. The first-ever failure still propagates.

## Current Boundary

The production owner is the `ObjectInfoReadCacher` factory in:

```text
src/BuiltinExtensions/ComfyUIBackend/ComfyUIRedirectHelper.cs
```

Its maintained consumers are:

- `ComfyBackendDirectHandler`, for cached `object_info`, queried `object_info`, `api/object_info`, and queried `api/object_info` routes when backend-data caching is enabled;
- `ComfyUIBackendExtension.Refresh`, which forces cache expiry before backend value refresh; and
- `ComfyUIWebAPI.ComfyEnsureRefreshable`, which forces cache expiry on request.

`LastObjectInfo` has no other maintained reader or writer. `SingleValueExpiringCacheAsync<JObject>` serializes refresh calculation and returns the current value without recalculation until expiry.

Backend-local `ComfyUIAPIAbstractBackend.RawObjectInfo` and capability snapshots are separate owners. Rank 21 reads their existing published values during the union but does not change their construction, publication, or consumers.

## Confirmed Defect

The approved-base factory initializes:

```csharp
JObject result = null;
```

It then attempts a synchronous first-backend GET and parse. Its catch logs the exception and rethrows only when `LastObjectInfo` is null. With a prior snapshot, it continues while `result` is still null.

The next loop unconditionally evaluates:

```csharp
result.ContainsKey(property.Name)
```

and can also assign:

```csharp
result[property.Name] = property.Value;
```

The intended stale fallback therefore faults before it can return `LastObjectInfo` whenever the loop reaches an available backend with non-null `RawObjectInfo`. The prior published object is not used as the merge target.

First-backend selection currently occurs before the exception boundary. A transient race that leaves no direct backend between the handler's initial availability check and cache calculation therefore cannot use a warm fallback either.

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
- Do not change the ten-minute expiry or either manual-expiry caller.
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

## Static Verification

Agents perform static verification only. They do not build, test, launch SwarmUI or ComfyUI, automate a browser, execute scripts, start backends or services, call live APIs, run test-executing lint, or make runtime, platform, filesystem, or performance claims.

Static review must:

1. pin the approved source/audit base, branch, index, and protected maintainer state;
2. inventory every maintained `ObjectInfoReadCacher` and `LastObjectInfo` reader/writer;
3. inventory both `ForceExpire` callers and all four cached route forms;
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
- the unchanged 20-case matrix is handed to Reaper176; and
- validation evidence distinguishes exercised runtime behavior from static-only and unvalidated claims.

## Rollback

Rollback restores first-backend selection to its original position and removes the explicit null-result and deep-clone fallback logic together. No data, schema, setting, migration, cache cleanup, or persistent-state step is required.
