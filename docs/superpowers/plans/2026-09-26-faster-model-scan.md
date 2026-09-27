# Faster Model Scan Implementation Plan

**Goal:** Reduce startup model-scan disk work without changing the published model catalog or metadata cache format.

**Approved approach:** Optimize the existing synchronous scan in an isolated worktree. The measured baseline was 254.66 seconds for model listing, including 13,785 LoRAs. The running process exceeded 700 threads; the model HDD was saturated during a sample. These observations identify the slow phase, not the relative cost of each operation.

**Architecture:** Traverse subdirectories sequentially and process at most four files concurrently in each folder. Reuse the folder's file-name snapshot to skip definitely absent sidecar probes; still stat present sidecars. Retain the public LoadMetadata and AddAllFromFolder signatures, existing fingerprint strings, duplicate-root precedence, hidden-folder filtering, and synchronous catalog publication. Snapshot membership is case-insensitive for ASCII-only folders so mixed-case names do not suppress filesystem checks. Folders containing any non-ASCII file names retain live probes, preserving filesystem Unicode normalization and case semantics. A fresh scan observes added and removed sidecars; concurrent filesystem changes retain the existing best-effort scan semantics.

**Files:** `src/Text2Image/T2IModelHandler.cs`, `SwarmUITests/ModelScanTests.cs`.

## Tasks

- [x] Write isolated filesystem regression tests first: unchanged fingerprint format; existing sidecar size/timestamp changes; absent sidecars; fresh snapshots after add/delete; case-sensitive and insensitive lookup compatibility.
- [x] Add a private fingerprint overload accepting the folder snapshot. Keep the original live-filesystem path for callers outside scanning.
- [x] Remove recursive parallelism, cap file processing at four workers, and retain cancellation checks between folders and files.
- [x] Add per-model-type wall time, enumeration worker time, metadata worker time, cache lookup worker time, cache hits, and rebuild counts. Distinguish summed worker times from elapsed wall time.
- [x] Review diffs and run `git diff --check`. Do not build or execute tests under AGENTS.md; record this limitation.
- [ ] Maintainer: run `launchtools/run_tests.sh`, build, then compare startup with the same model paths/settings and Debug logging. Compare both first and repeat launches; warm OS caches can distort results. Confirm model counts and metadata edits/additions/deletions still refresh correctly.

No user data, model files, cache placement, extension code, or launcher changes are part of this patch. Live measurements are recorded below; cold and warm results must not be conflated.

## Verification record

- `git diff --check` passed.
- Static review covered public method compatibility, fingerprint compatibility, metadata invalidation, duplicate roots, hidden files, cancellation boundaries, and snapshot lifetime. A Unicode filesystem-equivalence issue was corrected by falling back to live checks in folders with non-ASCII filenames.
- Seven isolated NUnit regression tests were added but not run under AGENTS.md. At the user's request, the Release build was subsequently compiled and launched: zero warnings and zero errors. Model metadata mutation regression tests remain unexecuted.
- The worktree starts from committed `caf3ec47`; unrelated uncommitted changes in the original checkout were not copied.

## Live startup measurements

The user authorized starting the worktree build using the existing runtime directory and settings. The same optimized binary was used for the three comparisons below. Cold runs followed user-performed Linux filesystem cache clearing; warm restarts did not clear caches. Times start at process launch and exclude shutdown. Backend response means HTTP 200 from ComfyUI's `/system_stats`, not verification of generation or completion of all Swarm capability synchronization.

| Runtime metadata configuration | Model listing | Web HTTP available | Backend HTTP responding |
| --- | ---: | ---: | ---: |
| Per-folder cache, cold | 163.91 s | 165.440 s | 180.796 s |
| Central cache on SSD, cold | 84.44 s | 85.977 s | 102.142 s |
| Central cache on SSD, warm repeat | 3.51 s | 4.274 s | 16.042 s |

All runs retained 13,785 LoRAs. The original unmodified run listed models in 254.66 seconds, but its OS-cache state was not controlled; it is not an equivalent cold baseline. Central cache placement was a user-applied server setting, not a code default change. Its first population rebuilt 13,631 LoRA metadata entries and took 1,332.68 seconds for model listing; this setup cost is separate from repeat startup performance. These are individual observations, not a controlled benchmark series.
