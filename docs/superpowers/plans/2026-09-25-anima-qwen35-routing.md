# Anima Qwen3.5-2B routing

Goal: make the projection-enabled Anima checkpoint work from the normal Generate tab.

Design: inspect the safetensors header in the existing ordinary-Anima loader branch. Require the Anima signature and a source projection of shape `[1024, 2048]`, excluding 52-block Anima. Route only that variant through the already installed `SwarmLoadAnimaQwen35Clip` node. Use the existing Qwen Model override or default to the installed `qwen3.5-2B-ntuned.safetensors`. Missing encoder or node produces a readable error; no downloads or fallback to the incompatible standard encoder.

Filename matching would break on renames and miss equivalent checkpoints. A new architecture class would require metadata cache migration. Header inspection keeps this change confined to encoder selection and works with existing cached classifications.

Implementation:
- Add focused NUnit coverage for prefixed tensor signatures, standard Anima, wrong projection dimensions, and 52-block Anima.
- Add the tensor predicate in `T2IModelClassSorter.cs` and node capability in `ComfyCapabilityCatalog.cs`.
- In `WorkflowGeneratorModelSupport.cs`, inspect supported local safetensors headers within the ordinary Anima branch and select the custom loader only on a matching signature. Preserve the current VAE and subsequent CLIP routing.
- Review diff and whitespace. Do not build or execute tests, per repository policy.

Developer validation after rebuilding/restarting: generate with the special checkpoint and default Qwen selection; confirm the workflow uses `SwarmLoadAnimaQwen35Clip`. Repeat with an explicit compatible Qwen override, standard Anima, and Anima 3.8B. Check missing encoder and missing backend node errors. Run `launchtools/run_tests.sh` for the isolated signature tests.

Implemented and statically reviewed. Both local `anima-v3-000002.safetensors` and `Original/animal-v2.1.safetensors` have the required tensor signature. Header inspection is limited to existing local safetensors/sft files; remote-only and other formats retain their previous route. The 3.8B branch precedes this branch and is also excluded by the predicate. Diff whitespace checks passed. NUnit tests were added but not executed; compilation and live workflow behavior remain for developer validation.
