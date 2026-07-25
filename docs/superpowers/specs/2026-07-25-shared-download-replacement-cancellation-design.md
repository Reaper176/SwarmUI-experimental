# Shared Download Replacement and Cancellation Design

**Status:** Implemented and maintainer-validated

**Date:** 2026-07-25

## Purpose

Correct the two confirmed lifecycle defects in `Utilities.DownloadFile` without redesigning its producer/consumer pipeline:

1. writing a shorter response over an existing longer destination must not retain stale trailing bytes; and
2. caller cancellation must stop initial and range-retry request acquisition rather than taking effect only after response headers.

The change remains bounded to the shared download owner. It preserves the public method signature, caller behavior, retry policy, progress reporting, verification, destinations, refresh behavior, and global-shutdown cancellation.

## Original Boundary and Evidence

Before the production implementation, `Utilities.DownloadFile`:

- created a token linked from `Program.GlobalProgramCancel` and the optional caller `CancellationTokenSource`;
- opened the destination with `File.OpenWrite`, which started at offset zero but did not truncate an existing longer file;
- sent the initial request and range-retry requests with only `Program.GlobalProgramCancel`;
- used the linked token for later reads, writes, and polling;
- buffered downloaded chunks between a network producer and file writer;
- reported progress through a third task;
- verified response length and optional SHA-256;
- deleted the destination on a known-length mismatch, hash mismatch, or writer failure, while an unknown-length producer failure or cancellation could leave a partial destination because the length check was skipped; and
- preserved up to four partial-content range retries.

The maintained caller inventory contains seven direct calls:

1. three Comfy archive downloads in `Installation`;
2. the Visual C++ redistributable download in `Installation`;
3. `CommonModels.ModelInfo.DownloadNow`;
4. `ModelsAPI.DoModelDownloadWS`; and
5. `WorkflowGenerator.DownloadModel`.

`ModelInfo.DownloadNow` has three maintained indirect flows:

1. installation-selected common models;
2. the automatic VAE branch in `WorkflowGeneratorModelSupport`; and
3. the LTX audio-VAE branch in `WorkflowGeneratorModelSupport`.

The model UI supplies the only maintained caller cancellation source. Existing direct workflow/model UI callers pre-delete temporary targets, while `ModelInfo.DownloadNow` refuses a pre-existing final destination. Fixed installation destinations do not universally establish a fresh-target precondition.

## Selected Approach

Validate the initial HTTP response before opening the destination. After a successful response is accepted, open the destination with explicit truncating create semantics and run the existing producer/consumer/progress pipeline.

Use the existing linked caller/global cancellation token consistently for:

- initial request send and response-header acquisition;
- range-retry request send and response-header acquisition;
- initial and retry response-stream acquisition;
- network reads;
- file writes; and
- task polling/delays.

This approach is preferred over:

- changing only `File.OpenWrite` to `FileMode.Create`, which could truncate a destination before a failed or canceled initial request obtains headers; and
- downloading to a new internal temporary file and atomically replacing the destination, which would redesign the helper and duplicate temporary-file ownership already held by important callers.

## Data Flow

The successful flow is:

1. Normalize the display URL and create the linked caller/global cancellation token.
2. Create the destination directory.
3. Build the initial request and apply caller-supplied headers.
4. Send the request with `ResponseHeadersRead` and the linked token.
5. Reject a non-OK response before creating or changing the destination.
6. Acquire the response stream with the linked token.
7. Open the destination using `FileMode.Create`.
8. Start the established network producer, file writer/hash verifier, and progress reporter.
9. On a recoverable partial read failure, dispose the active stream/response, send the existing range request with the linked token, require `PartialContent`, and continue.
10. Complete length and optional SHA-256 verification.
11. Leave exactly the verified response bytes at the destination.

The network producer, chunk queue, progress queue, buffering thresholds, retry count, progress cadence, and verification algorithms remain unchanged.

## Cancellation and Failure Semantics

Pre-header caller cancellation and global shutdown cancel the active initial send. Because the destination has not yet been opened, a pre-existing destination remains unchanged and no new incomplete file is created.

After the initial response is accepted and the destination is opened:

- `FileMode.Create` replaces and truncates the destination;
- linked cancellation reaches range sends, stream acquisition, reads, writes, and polling;
- the existing terminal-chunk and task-failure paths continue to surface cancellation or the established readable failure;
- known-length mismatch, hash mismatch, and writer failure retain their established deletion paths, while an unknown-length producer failure or cancellation may leave a partial replacement because the length check is skipped; and
- successful completion leaves exact bytes with no stale tail.

Request, response, response-stream, file-stream, and linked-token lifetimes remain explicitly bounded. The linked `CancellationTokenSource` is disposed; caller-owned sources are not disposed by the helper; and the helper-created default source from `cancel ??= new()` retains its legacy undisposed lifetime. The SHA-256 instance also retains its legacy undisposed lifetime, and this change makes no new hashing-lifetime guarantee.

## Compatibility Requirements

The implementation must preserve:

- the public `DownloadFile` signature, including the optional `CancellationTokenSource`;
- URL and alternate display-URL behavior;
- custom request headers;
- the initial `OK` and retry `PartialContent` status requirements;
- the four-retry limit and existing range offset/length convention;
- chunk buffering and five-minute stalled-read warnings/failure;
- progress callback arguments and cadence;
- content-length and SHA-256 verification;
- established readable error categories and contextual logging;
- deletion on known-length mismatch, hash mismatch, and writer failure, while preserving the legacy possibility that an unknown-length producer failure or cancellation leaves a partial destination because the length check is skipped;
- installation paths and progress behavior;
- `ModelInfo.DownloadNow` destination refusal and indirect flows;
- model UI temporary target, WebSocket cancellation, move, and refresh behavior;
- workflow-generator temporary target and cleanup;
- Comfy model-support refresh behavior; and
- `Program.GlobalProgramCancel` participation.

This project does not redesign resume policy, introduce an internal atomic replacement file, change caller destinations, add performance work, or broaden download ownership beyond `Utilities.DownloadFile`.

## Static Verification

Agents will not build, launch, or run tests. Static verification must:

1. repeat the seven-direct/three-indirect caller inventory;
2. prove the destination is opened only after a successful initial response;
3. prove the successful-response destination open uses truncating create semantics;
4. trace the linked token through initial and retry sends, stream acquisition, reads, writes, and polling;
5. prove the linked cancellation source is disposed, caller-owned sources are not disposed, and the helper-created default source retains its legacy undisposed lifetime;
6. trace pre-header cancellation without destination mutation;
7. trace midstream cancellation and the cleanup split between known-length/hash/writer failures and unknown-length producer failure or cancellation;
8. confirm retry count, range behavior, status requirements, progress, length/hash checks, URLs, headers, and logging remain unchanged;
9. confirm model UI target selection/pre-deletion and success move/refresh, workflow-generator catch cleanup, helper deletion split, and fixed-path behavior remain unchanged;
10. inspect the exact changed-file and commit range; and
11. run `git diff --check`.

## Implementation Record

The production implementation is exactly the inclusive range `24157f35` through `051a5bb5` (`24157f35^..051a5bb5`):

1. `24157f35 fix: truncate shared download replacements`
2. `051a5bb5 fix: cancel shared download requests`

The fixed approved-design boundary is `23c77542`. The intervening commits `93e1a50c`, `9558e289`, `75c93bda`, and `329b7cce` are plan or lifetime/ownership documentation corrections, not production changes. The exact production changed-source list is one file:

- `src/Utils/Utilities.cs`

Static caller inventory repeated seven direct calls: three Comfy archive downloads and the Visual C++ redistributable in `Installation`, `CommonModels.ModelInfo.DownloadNow`, `ModelsAPI.DoModelDownloadWS`, and `WorkflowGenerator.DownloadModel`. The three indirect `ModelInfo.DownloadNow` flows remain installation-selected common models plus the automatic-VAE and LTX audio-VAE branches in `WorkflowGeneratorModelSupport`. Ownership remains nuanced: model UI is the sole maintained caller that supplies a cancellation source, selects and pre-deletes its temporary target, and owns the success move and refresh; `Utilities.DownloadFile` owns deletion on known-length mismatch, hash mismatch, and writer failure, with the recorded unknown-length gap. `WorkflowGenerator.DownloadModel` separately owns its temporary target and catch cleanup. `ModelInfo.DownloadNow` refuses a pre-existing final destination, while fixed installation destinations do not universally establish a fresh-target precondition.

The implemented successful-response order is exactly initial linked-token send, `OK` validation, linked-token response-stream acquisition, destination open with `FileMode.Create`, and then producer/writer/progress worker startup. Thus failed status or pre-stream failure occurs before destination mutation, while an accepted response truncates an existing longer target. The linked caller/global token now reaches initial and retry sends, initial and retry stream acquisition, network reads, file writes, and chunk/progress polling. `Program.GlobalProgramCancel` and the optional caller source remain linked inputs. The linked source is disposed, a caller-owned source is not disposed by the helper, and a helper-created default source from `cancel ??= new()` remains legacy-undisposed.

The public signature and all callers are unchanged. The four-retry limit, `PartialContent` requirement, buffering, progress cadence and callback arguments, content-length and SHA-256 checks, URLs, headers, logs, temporary-file ownership, move/cleanup/refresh behavior, and fixed installer paths are preserved. Known-length mismatch, hash mismatch, and writer failure retain deletion; an unknown-length producer failure or cancellation may leave a partial destination because the length check is skipped. The implementation also deliberately preserves adjacent legacy behavior: an unknown response length still uses a 1024-byte buffer and can truncate the download to 1024 bytes while returning false success; retry requests still use the literal inclusive `Range(totalRead, length)` convention without `Content-Range` validation; terminal progress is still enqueued before length/hash validation; sibling workers are not explicitly canceled when another worker fails; the helper-created default cancellation source remains undisposed; and the SHA-256 instance remains undisposed.

Static review used:

- `rg -n "Utilities\.DownloadFile\(" src --glob '*.cs'` and the `DownloadNow` caller search, yielding seven direct calls and three indirect flows;
- `git diff --name-only 24157f35^..051a5bb5`, yielding only `src/Utils/Utilities.cs`;
- `git diff --check 24157f35^..051a5bb5`, yielding no whitespace errors;
- fixed-range diff/log inspection, distinguishing the two production commits from the four documentation commits after `23c77542`; and
- targeted `rg`/source inspection of send/status/stream/open ordering, linked-token phases, retries, progress, verification, cleanup, and caller behavior.

Protected maintainer changes and the protected backup reported by repository status were excluded and not inspected or modified. No agent ran a build, test, launcher, download, network request, fault injection, cancellation exercise, or benchmark. Runtime behavior, performance, and platform-specific behavior remain unvalidated.

## Maintainer Validation

On 2026-07-25, maintainer Reaper176 confirmed on Linux that all 14 approved cases passed:

1. a pre-existing destination longer than a successful response was replaced with exactly the response bytes;
2. caller cancellation before response headers returned promptly without changing a pre-existing destination;
3. caller cancellation during known-length streaming deleted the incomplete replacement through the established length-mismatch or writer-task failure cleanup paths, while unknown-length cancellation retained its documented possibility of leaving a partial replacement;
4. global shutdown cancellation still interrupted acquisition and streaming;
5. successful range continuation produced exact bytes;
6. failed or non-partial range continuation preserved its established known-length failure cleanup;
7. content-length mismatch deleted the target and reported failure;
8. SHA-256 mismatch deleted the target and reported failure;
9. fixed-path installer downloads and progress remained functional;
10. installation-selected common-model downloads remained functional;
11. model UI success, progress, cancellation, target selection/pre-deletion, success move/refresh, and the helper's recorded cleanup split remained functional;
12. `WorkflowGenerator.DownloadModel` success and cleanup remained functional;
13. both `WorkflowGeneratorModelSupport` common-model paths downloaded and refreshed correctly; and
14. ordinary successful downloads and progress callbacks remained unchanged.

The adjacent unknown-length 1024-byte truncation/false-success behavior remains uncorrected and is not claimed fixed; the unknown-length partial-replacement cleanup gap also remains documented. Windows runtime behavior and performance remain unvalidated. No agent runtime claim is made.

## Rollback

Replacement opening/order and linked-token request propagation are deliberately separate implementation stages and may be reverted independently if necessary. A complete rollback restores the prior destination-open timing/mode and prior request tokens while leaving all callers unchanged.
