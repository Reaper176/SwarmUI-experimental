# Anima ControlNet-LLLite Workflow Design

## Goal

Make SwarmUI's generated Anima ControlNet workflow use the bundled
`SwarmAnimaLLLite` node, so compatible Anima ControlNet-LLLite weights can be
applied successfully.

## Scope

When an Anima-compatible ControlNet is selected, the workflow generator will
create one `SwarmAnimaLLLite` node. It will pass the current diffusion model,
the selected control image, the optional mask, strength, start and end
percentages, and the selected ControlNet path through `lllite_name`.

The old generic `ModelPatchLoader` and `AnimaLLLiteApply` chain will be
removed from this branch. Other ControlNet branches remain unchanged.

## Invalid Weight Handling

`SwarmAnimaLLLite` will verify that the selected weight file has the expected
Anima LLLite named-key structure before attempting to construct or patch a
model. A regular ControlNet or other unsupported file will produce a clear
error identifying the selected file and stating that an Anima
ControlNet-LLLite model is required.

Existing errors for a missing file, legacy LLLite weight format, and missing
inpaint mask remain intact.

## Verification

Automated tests and builds are not run by agents in this repository. The
change will be checked by static data-flow inspection. Manual verification is
to generate with `anima_baseV10` and a known Anima LLLite ControlNet, then
confirm that a regular ControlNet yields the new explanatory error.
