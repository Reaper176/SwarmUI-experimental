# Optional extension image checks

This console runner compiles the installed MagicPrompt `BackendSchema.cs` and WhatTheDuck `WebpExifRewriter.cs` against the current SwarmUI project. It uses real Magick.NET image reads and writes on the CPU, starts no application or backend, and sends no network requests. It is intentionally outside the normal SwarmUI test project and solution, so absent optional extensions do not affect normal tests.

From the repository root, after building the core in the same configuration:

```sh
dotnet restore docs/ReferenceSetups/2026-10-22/magick-compat/ExtensionChecks/ExtensionChecks.csproj
dotnet build docs/ReferenceSetups/2026-10-22/magick-compat/ExtensionChecks/ExtensionChecks.csproj --no-restore -c Release -p:BuildProjectReferences=false
dotnet docs/ReferenceSetups/2026-10-22/magick-compat/ExtensionChecks/bin/Release/net8.0/ExtensionChecks.dll
```

Use the repository's normal core build first; the `BuildProjectReferences=false` option keeps this check from rebuilding it. Select a different configuration consistently if needed. The runner requires the two installed extension source files; it reports their expected paths when they are absent. Their locations can be overridden with `-p:MagicPromptSource=/path/to/BackendSchema.cs` and `-p:WhatTheDuckSource=/path/to/WebpExifRewriter.cs`.

The 25 checks cover:

- PNG, WebP, and JPEG thumbnail output, a 256px maximum, unchanged small dimensions, and one-pixel aspect-ratio edges in both directions.
- Unicode EXIF comment edits in lossy and lossless WebP, with opaque, transparent, and animated fixtures, and both EXIF prefix conventions.
- Byte-for-byte preservation of all non-EXIF chunks, including compressed pixels, alpha, animation frame/timing data, and an unknown odd-length chunk; decoded frame/pixel equality; preservation of another EXIF tag; and a correct RIFF length.
- Rejection of invalid input and WebP files with no EXIF chunk.

Fixtures are generated in memory, and the runner returns a nonzero exit code if any assertion fails.
