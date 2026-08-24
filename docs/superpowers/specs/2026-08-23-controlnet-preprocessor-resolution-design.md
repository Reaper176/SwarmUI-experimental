# ControlNet Preprocessor Resolution Design

## Goal

Expose a per-ControlNet resolution slider so users can bound the work and VRAM
used by ControlNet preprocessors such as Lineart.

## Scope

Register a `ControlNet Preprocessor Resolution` integer parameter for each of
the three existing ControlNet slots. Each slider defaults to 1024, accepts
values from 64 through 4096, and moves in 64-pixel increments. It appears in
the same ControlNet parameter group directly after the preprocessor selector.

When creating a preprocessor node, SwarmUI will supply that slot's slider value
to a preprocessor's `resolution` input. Preprocessors without a `resolution`
input remain unchanged. The parameter affects generation and the existing
ControlNet-preprocessor preview path because both use the same workflow
generation code.

## Verification

Agents do not run builds or tests in this repository. Review the parameter
registration and generated preprocessor-node input statically. Manual
verification is to set Lineart's ControlNet Preprocessor Resolution to 512 and
confirm that its workflow/preview receives `resolution: 512`; leave a
preprocessor without that input selected and confirm it is unaffected.
