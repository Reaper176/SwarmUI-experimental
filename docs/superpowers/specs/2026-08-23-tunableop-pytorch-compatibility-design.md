# TunableOp PyTorch Compatibility Design

## Context

SwarmUI's ComfyUI extra-node helper periodically calls
`torch.cuda.tunable.write_file()` to preserve TunableOp results. PyTorch 2.9
provides that API, while PyTorch 2.10 and later remove both the public Python
function and its Python-accessible C binding. Newer PyTorch versions persist
TunableOp results internally and on normal process termination.

## Design

Keep the existing periodic flush behavior when `write_file` is available. After
confirming that TunableOp is enabled, feature-detect `tunable.write_file` before
configuring or entering the flush loop. When the function is absent, emit one
informational message explaining that persistence is managed by PyTorch and
return without starting the loop.

Do not call private PyTorch APIs and do not substitute `read_file()` or the
offline GEMM tuning APIs. This keeps compatibility with PyTorch 2.9 without
depending on internals that are absent from the installed PyTorch 2.12 build.

## Error Handling

Retain the existing import, disabled-state, interval, and per-flush exception
handling for PyTorch versions that support explicit flushing. The newer-version
path must terminate normally and must not repeatedly log an exception.

## Verification

Because this repository prohibits agent-run tests, validate by static analysis:

- A TunableOp module with `write_file` reaches the existing flush behavior.
- A TunableOp module without `write_file` returns before interval setup and the
  infinite loop.
- The edited Python file compiles syntactically using a non-executing parser.

The maintainer will perform live backend verification. Expected behavior on
PyTorch 2.12 is one persistence-management message and no recurring
`TunableOp flush failed` messages.
