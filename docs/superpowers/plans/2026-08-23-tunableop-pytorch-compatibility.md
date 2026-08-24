# TunableOp PyTorch Compatibility Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Stop SwarmUI's TunableOp flush helper from repeatedly calling the removed `write_file()` API on PyTorch 2.10+, while preserving periodic flushing on PyTorch 2.9.

**Architecture:** Add one public-API feature check inside the existing helper after its enabled-state check. Older PyTorch versions continue into the unchanged flush loop; newer versions log once and return because PyTorch manages persistence internally.

**Tech Stack:** Python, `torch.cuda.tunable`, Python AST static validation

---

### Task 1: Guard the explicit TunableOp flush loop

**Files:**
- Modify: `src/BuiltinExtensions/ComfyUIBackend/ExtraNodes/SwarmComfyExtra/SwarmTunableOp.py:17`

- [ ] **Step 1: Confirm the installed API exhibits the incompatible case**

Run this read-only introspection from the paired ComfyUI checkout:

```bash
./venv/bin/python -c "import torch; print(torch.__version__); print(hasattr(torch.cuda.tunable, 'write_file'))"
```

Expected output includes PyTorch `2.12.0+rocm7.14.0` followed by `False`. This confirms the current unconditional call cannot work with the installed API.

- [ ] **Step 2: Add the minimal public-API compatibility guard**

Immediately after the existing `tunable.is_enabled()` block, add:

```python
    if not hasattr(tunable, "write_file"):
        print("[Swarm] TunableOp persistence is managed automatically by this PyTorch version.")
        return
```

Do not change the existing interval parsing, loop, or exception handling. Do not call any private `torch._C` member.

- [ ] **Step 3: Validate syntax without importing or executing the helper**

Run:

```bash
python -c "import ast, pathlib; path = pathlib.Path('src/BuiltinExtensions/ComfyUIBackend/ExtraNodes/SwarmComfyExtra/SwarmTunableOp.py'); ast.parse(path.read_text()); print('AST parse passed')"
```

Expected output: `AST parse passed`.

- [ ] **Step 4: Validate both compatibility branches by static inspection**

Run:

```bash
sed -n '7,48p' src/BuiltinExtensions/ComfyUIBackend/ExtraNodes/SwarmComfyExtra/SwarmTunableOp.py
```

Confirm the control flow in this order:

1. Import errors return.
2. Disabled TunableOp returns.
3. Missing `write_file` logs once and returns.
4. Available `write_file` continues into the unchanged interval and flush-loop logic.

- [ ] **Step 5: Inspect the scoped diff**

Run:

```bash
git diff --check -- src/BuiltinExtensions/ComfyUIBackend/ExtraNodes/SwarmComfyExtra/SwarmTunableOp.py
git diff -- src/BuiltinExtensions/ComfyUIBackend/ExtraNodes/SwarmComfyExtra/SwarmTunableOp.py
```

Expected: no whitespace errors and only the three-line feature guard is added.

- [ ] **Step 6: Commit the compatibility fix**

```bash
git add src/BuiltinExtensions/ComfyUIBackend/ExtraNodes/SwarmComfyExtra/SwarmTunableOp.py
git commit -m "Fix TunableOp compatibility with PyTorch 2.10+"
```

- [ ] **Step 7: Hand off live verification to the maintainer**

Restart the ComfyUI backend through SwarmUI and confirm the log contains exactly one instance of:

```text
[Swarm] TunableOp persistence is managed automatically by this PyTorch version.
```

Leave the backend running for at least 30 seconds and confirm there are no recurring `TunableOp flush failed` messages. Generate an image to verify normal Swarm-to-Comfy operation; this live test must be performed by the maintainer under the repository policy.
