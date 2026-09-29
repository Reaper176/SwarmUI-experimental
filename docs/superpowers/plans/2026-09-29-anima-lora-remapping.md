# Anima LoRA remapping implementation plan

Approved scope: extend Swarm's built-in Generate-page LoRA support to the 28-, 40-, and 52-block Anima layouts. Preserve explicit strengths, scheduling, regional confinement, and legacy saved node contracts. Do not amplify weights or populate inserted blocks.

1. Add pure mapping regression cases for 28→40, 28→52, 40→52, native identity, and rejected down-mapping. Check that tensor objects (including alpha) retain their values.
2. Add a runtime-aware Swarm Anima loader with full, model-only, and hook forms. Detect target depth from the connected diffusion model; cache raw weights, not target-specific remaps. Retain the existing 52-block bridge's legacy adapter exclusion.
3. Route both `anima` and `anima-3_8b` through these nodes. Pass the correct model to hooks during loading and segmentation. Require the new backend capabilities, retaining legacy node registration for saved workflows.
4. Update routing regression expectations and add node-level coverage for strengths, model switching, and hook chaining. Document use and limits.
5. Perform syntax/whitespace checks and independent review. Under AGENTS.md, builds and test execution remain with the user; do not claim runtime verification.

## Implementation record

- Added `SwarmAnimaLora.py` and regression coverage in `test_anima_lora_mapping.py`; retained old saved-workflow nodes.
- Generator emits the new full/model-only/hook nodes for both Anima classifications. Dynamic hooks and conditioning are cached per host model.
- Added source-depth metadata override and blockless passthrough after static review identified ambiguity and compatibility cases.
- Updated routing harness and workflow assertions, and documented source-depth limits in `docs/Features/Anima-LoRAs.md`.
- Python AST syntax and whitespace checks completed. Independent static review findings addressed. Build, automated tests, and GPU validation remain unrun under AGENTS.md.
