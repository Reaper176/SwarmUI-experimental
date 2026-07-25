# Shared Download Replacement and Cancellation Design

**Status:** Approved

**Date:** 2026-07-25

## Purpose

Correct the two confirmed lifecycle defects in `Utilities.DownloadFile` without redesigning its producer/consumer pipeline:

1. writing a shorter response over an existing longer destination must not retain stale trailing bytes; and
2. caller cancellation must stop initial and range-retry request acquisition rather than taking effect only after response headers.

The change remains bounded to the shared download owner. It preserves the public method signature, caller behavior, retry policy, progress reporting, verification, destinations, refresh behavior, and global-shutdown cancellation.

## Current Boundary and Evidence

`Utilities.DownloadFile` currently:

- creates a token linked from `Program.GlobalProgramCancel` and the optional caller `CancellationTokenSource`;
- opens the destination with `File.OpenWrite`, which starts at offset zero but does not truncate an existing longer file;
- sends the initial request and range-retry requests with only `Program.GlobalProgramCancel`;
- uses the linked token for later reads, writes, and polling;
- buffers downloaded chunks between a network producer and file writer;
- reports progress through a third task;
- verifies response length and optional SHA-256;
- deletes the destination on detected midstream, length, hash, or writer failure; and
- preserves up to four partial-content range retries.

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
- detected incomplete, length-invalid, hash-invalid, or write-failed replacements are deleted; and
- successful completion leaves exact bytes with no stale tail.

Request, response, response-stream, file-stream, hashing, and linked-token lifetimes remain explicitly bounded. Caller-owned `CancellationTokenSource` instances are never disposed by the helper.

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
- deletion of detected incomplete or invalid downloads;
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
5. prove caller-owned cancellation sources are not disposed;
6. trace pre-header cancellation without destination mutation;
7. trace midstream cancellation and detected failure cleanup;
8. confirm retry count, range behavior, status requirements, progress, length/hash checks, URLs, headers, and logging remain unchanged;
9. confirm caller temporary-file, move, cleanup, refresh, and fixed-path behavior remains unchanged;
10. inspect the exact changed-file and commit range; and
11. run `git diff --check`.

## Maintainer Validation

Maintainer Reaper176 will validate:

1. a pre-existing destination longer than a successful response is replaced with exactly the response bytes;
2. caller cancellation before response headers returns promptly without changing a pre-existing destination;
3. caller cancellation during streaming deletes the incomplete replacement;
4. global shutdown cancellation still interrupts acquisition and streaming;
5. successful range continuation produces exact bytes;
6. failed or non-partial range continuation preserves established failure cleanup;
7. content-length mismatch deletes the target and reports failure;
8. SHA-256 mismatch deletes the target and reports failure;
9. fixed-path installer downloads and progress remain functional;
10. installation-selected common-model downloads remain functional;
11. model UI success, progress, cancellation, temporary cleanup, move, and refresh remain functional;
12. `WorkflowGenerator.DownloadModel` success and cleanup remain functional;
13. both `WorkflowGeneratorModelSupport` common-model paths download and refresh correctly; and
14. ordinary successful downloads and progress callbacks remain unchanged.

No performance improvement or unvalidated platform behavior will be claimed.

## Rollback

Replacement opening/order and linked-token request propagation are deliberately separate implementation stages and may be reverted independently if necessary. A complete rollback restores the prior destination-open timing/mode and prior request tokens while leaving all callers unchanged.
