# Restore Live Preview Lifecycle Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Restore live generation previews by reactivating asynchronous preview delivery for the duration of Swarm sampler execution.

**Architecture:** Reinstate the lifecycle guard originally introduced with asynchronous preview delivery. The existing module-level lock protects activation state, and a `try`/`finally` boundary guarantees deactivation after successful or failed sampling while leaving Detail Daemon behavior unchanged.

**Tech Stack:** Python, PyTorch, ComfyUI custom nodes

---

## File Structure

- Modify `src/BuiltinExtensions/ComfyUIBackend/ExtraNodes/SwarmComfyCommon/SwarmKSampler.py`: own the asynchronous preview state and sampler lifecycle.
- No automated test files: SwarmUI repository policy prohibits agents from running tests, and this backend behavior requires live ComfyUI/GPU integration for behavioral verification.

### Task 1: Restore the Preview State Lifecycle

**Files:**
- Modify: `src/BuiltinExtensions/ComfyUIBackend/ExtraNodes/SwarmComfyCommon/SwarmKSampler.py:575-597`

- [ ] **Step 1: Record the broken-state evidence**

Run:

```bash
rg -n "_preview_sampler_active|_last_preview_step_sent" src/BuiltinExtensions/ComfyUIBackend/ExtraNodes/SwarmComfyCommon/SwarmKSampler.py
```

Expected: the variables are initialized and read, but there is no assignment of `_preview_sampler_active = True` around `sample_sample(...)`.

- [ ] **Step 2: Add the minimal lifecycle guard**

Replace the current `if steps > 0:` block with:

```python
if steps > 0:
    global _preview_sampler_active, _last_preview_step_sent
    with _preview_lock:
        _preview_sampler_active = True
        _last_preview_step_sent = -1
    try:
        callback = make_swarm_sampler_callback(steps, device, model, previews)
        sample_model = model
        if detail_daemon is not None:
            sampler = comfy.samplers.KSampler(model, steps=steps, device=model.load_device, sampler=sampler_name, scheduler=scheduler, denoise=1.0, model_options=model.model_options)
            active_sigmas = sigmas if sigmas is not None else sampler.sigmas
            if end_at_step is not None and end_at_step < (len(active_sigmas) - 1):
                active_sigmas = active_sigmas[:end_at_step + 1].clone()
                if return_with_leftover_noise == "disable":
                    active_sigmas[-1] = 0
            if start_at_step is not None:
                if start_at_step < (len(active_sigmas) - 1):
                    active_sigmas = active_sigmas[start_at_step:]
                else:
                    return (out, )
            sample_model = detail_daemon_wrap_model(model, active_sigmas, cfg, detail_daemon)

        samples = sample_sample(sample_model, noise, steps, cfg, sampler_name, scheduler, positive, negative, latent_samples,
                                denoise=1.0, disable_noise=disable_noise, start_step=start_at_step, last_step=end_at_step,
                                force_full_denoise=return_with_leftover_noise == "disable", noise_mask=noise_mask, sigmas=sigmas, callback=callback, seed=noise_seed, model_negative=model_negative)
        out["samples"] = samples
    finally:
        with _preview_lock:
            _preview_sampler_active = False
```

- [ ] **Step 3: Parse the modified Python file without executing project code**

Run:

```bash
python -m py_compile src/BuiltinExtensions/ComfyUIBackend/ExtraNodes/SwarmComfyCommon/SwarmKSampler.py
```

Expected: exit code 0 and no output. Remove only the generated `__pycache__/SwarmKSampler.*.pyc` artifact afterward.

- [ ] **Step 4: Verify lifecycle reachability and diff scope**

Run:

```bash
rg -n "_preview_sampler_active|_last_preview_step_sent" src/BuiltinExtensions/ComfyUIBackend/ExtraNodes/SwarmComfyCommon/SwarmKSampler.py
git diff --check -- src/BuiltinExtensions/ComfyUIBackend/ExtraNodes/SwarmComfyCommon/SwarmKSampler.py
git diff -- src/BuiltinExtensions/ComfyUIBackend/ExtraNodes/SwarmComfyCommon/SwarmKSampler.py
```

Expected: activation and reset occur before callback construction, deactivation occurs in `finally`, `git diff --check` reports no errors, and no unrelated logic changes appear.

- [ ] **Step 5: Commit only the sampler fix**

```bash
git add src/BuiltinExtensions/ComfyUIBackend/ExtraNodes/SwarmComfyCommon/SwarmKSampler.py
git commit -m "Fix asynchronous generation previews"
```

Expected: the commit contains only `SwarmKSampler.py`.

### Task 2: Live Developer Verification

**Files:**
- No files modified.

- [ ] **Step 1: Restart the ComfyUI backend**

Use SwarmUI's backend controls to restart the active ComfyUI backend so it reloads `SwarmKSampler.py`.

- [ ] **Step 2: Generate a multi-step image with previews enabled**

Ensure the advanced `No Previews` parameter is disabled, then generate a normal image with enough sampling steps to observe intermediate output.

Expected: the placeholder is replaced by intermediate preview images during sampling, and the final image replaces the last preview when generation completes.

- [ ] **Step 3: Verify cleanup after an interrupted generation**

Start another generation, interrupt it after at least one preview, then start a fresh generation.

Expected: the fresh generation receives previews, demonstrating that lifecycle state was deactivated and reset after interruption.
