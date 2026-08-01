# Ranks 1–32 Full Validation — 2026-07-31

## Disposition

Maintainer Reaper176 granted a one-time exception to the repository's normal
agent build/test prohibition. Validation targeted production commit
`aebbe81640ff94b1b5ce0ec11806759d5b3e564d` from an isolated worktree and
external archive. Accepted source, build, log, harness-copy, result, and
provenance artifacts are retained under
`/tmp/swarmui-ranks-1-32-fixed-sanitized-yt40cG`; the rejected unsanitized
attempt is excluded from this audit's accepted evidence.

The clean Release publish passed with zero warnings and zero errors. Isolated
server startup, root redirect, Text2Image page, core CSS/JS, and
`API/GetNewSession` passed. The rank suite executed 299 rank-attributed passing
assertions and zero failures.

The initial authorized run found that Firefox 153 emitted a settling scroll
when returning to the History tab and therefore invoked the visible-window
update twice. Production commit `aebbe81640ff94b1b5ce0ec11806759d5b3e564d`
defers only the History-tab explicit update so it coalesces with that settling
scroll. The post-fix runs passed all 16/16 rank-31 browser assertions in both
Firefox 153 and Chrome for Testing 148.0.7778.97.

## Environment

- Garuda Linux (Arch-based), kernel `7.1.4-1-cachyos`, x86-64
- Btrfs root filesystem
- AMD Ryzen 7 7800X3D, 16 logical CPUs
- .NET SDK 10.0.110; target/runtime .NET 8.0.29
- Node.js 20.20.2
- Playwright 1.62.1 with managed Firefox 153.0 fallback build
- Chrome for Testing 148.0.7778.97 for the rank-31 comparison
- AMD Radeon Navi 31 present; no isolated test models or configured test
  backends were present

## Per-rank executed results

Counts include two static source/removal contracts per rank. Runtime/browser
counts are additional assertions against the exact fresh assembly. A repeated
contract in two browsers is counted once per browser execution. The total is a
rank-attributed aggregate, not a count of unique executed assertions: the same
retained 19-assertion rank-27 workflow graph cleanup/post-removal harness was
executed once and intentionally attributed to both rank 24's graph-editor
cleanup sanity and rank 27's workflow cleanup post-removal sanity.

| Rank | Passed | Failed | Executed evidence |
|---:|---:|---:|---|
| 1 | 6 | 0 | Browser diagnostic redaction and sentinel-free failure handling in Firefox |
| 2 | 5 | 0 | Comfy workflow/envelope/exception redaction with private sentinels |
| 3 | 2 | 0 | OAuth cookie writer/logout static scheme contracts |
| 4 | 6 | 0 | External settings save, repeat parity, and lock refusal |
| 5 | 6 | 0 | Backend mutation generation, durable save, and acknowledgement |
| 6 | 2 | 0 | Durable workflow journal/commit static contracts |
| 7 | 5 | 0 | Catalog read/write exclusion and subsequent read progress |
| 8 | 6 | 0 | Two-owner immutable capability snapshots without feature borrowing |
| 9 | 5 | 0 | Local HTTP exact-byte download and completion progress |
| 10 | 6 | 0 | Queued WebSocket frames, generic fault frame, false result, and success path |
| 11 | 4 | 0 | Throwing model load returns false and releases reservation |
| 12 | 7 | 0 | Active/inactive exact-owner reservation and administrative clear lifecycle |
| 13 | 2 | 0 | Atomic shutdown storage/gate static contracts |
| 14 | 4 | 0 | Snapshot shutdown and exactly-once backend shutdown |
| 15 | 2 | 0 | Both request-local generation ceiling predicates |
| 16 | 4 | 0 | Missing/invalid backend storage completes loading phase |
| 17 | 5 | 0 | Firefox callback order, fault isolation, and visible error |
| 18 | 7 | 0 | Firefox renewal socket retains open/error/result handlers and renewed session |
| 19 | 6 | 0 | OAuth tracker replacement, atomic one-time consume, and 256-entry bound |
| 20 | 5 | 0 | Linux extension policy, uppercase `.SH`, disabled precedence |
| 21 | 2 | 0 | Warm object-info snapshot/clone static contracts |
| 22 | 2 | 0 | Sidecar fingerprint/cache-key static contracts |
| 23 | 7 | 0 | Firefox concurrent lazy activation, partial load, state, and asset deduplication |
| 24 | 21 | 0 | Graph facade/editor static contracts plus the shared 19 graph cleanup assertions |
| 25 | 2 | 0 | Instrumentation absent and output filename owner retained |
| 26 | 3 | 0 | Instrumentation absent plus scheduler capacity/release/shutdown sanity |
| 27 | 21 | 0 | Instrumentation absent plus the shared 19 workflow cleanup assertions |
| 28 | 20 | 0 | Instrumentation absent plus 18 media conversion assertions |
| 29 | 40 | 0 | Instrumentation absent plus 38 cold/warm workflow hydration assertions |
| 30 | 21 | 0 | Instrumentation absent plus 19 Firefox image-history assertions |
| 31 | 34 | 0 | Two static contracts; 16/16 Firefox and 16/16 Chromium History-tab assertions |
| 32 | 31 | 0 | Instrumentation absent plus 29 sidecar add/edit/delete/cache assertions |

## Cross-cutting build and launch evidence

- `dotnet publish ... -c Release` completed with exit 0 and zero
  warnings/errors.
- Committed `src/wwwroot/js/genpage/gentab/outputhistory.js` SHA-256:
  `eb627b59288a3f65e510af413d1cace5f1ab28fe0cea1f1372b30dfd14e21721`.
- Accepted Release `SwarmUI.dll` SHA-256:
  `c2d7ad47443056882991f641c6eb1d682db68a4d71f2f56fe5bbd1cfb23eaa19`.
- Launch from the external repository root reached
  `http://127.0.0.1:17844` and shut down through authenticated
  `ShutdownServer` with a successful response and observed exit 0.
- `/` returned the expected redirect to `Text2Image`.
- `Text2Image`, `css/site.css`, and `js/site.js` returned successfully.
- `API/GetNewSession` returned a non-empty session ID.
- Static contract suite: `64 passed, 0 failed`.
- Core C# suite: `43 passed, 0 failed`.
- Post-removal suites: rank 26 passed; rank 27 `19/0`; rank 28 `18/0`;
  rank 29 `38/0`; rank 32 `29/0`.
- Firefox core suite: ranks 1, 17, 18, and 23 passed `17/0` with no
  unexpected page errors.
- Rank 30 Firefox post-removal suite: `19/0`.
- Rank 31 post-fix suites: Firefox `16/16` and Chromium `16/16`; with the two
  static contracts this is rank 31 `34/0`.

The browser page reported the already-known
`featureSetChangedCallbacks is not defined` startup error in the browser runs.
The retained harnesses record it separately from assertions and report no
unexpected page errors in the Firefox core run.

The initial attempt to launch from the bare publish directory stopped before
hosting because SwarmUI intentionally resolves `launchtools` and
`src/BuiltinExtensions` relative to the repository root. Re-running the same
assembly from the external archived repository root passed. This was an
invocation/environment constraint, not counted as a rank failure.

All reviewer-identified Release configuration, retained script/hash, traced
command, shutdown, repository-provenance, and scoped cleanup issues were
corrected.

## Isolation and repository boundaries

The committed source archive excluded protected `Data` and `src/Data`, real
`Models` and `Output`, external `src/Extensions`, build outputs, and
`dlbackend`. Only synthetic Output fixtures were copied into the accepted
archive. The accepted run made no extension compile attempts. The primary
checkout's recorded pre-existing dirty state remained unchanged and was not
used as accepted source.

## Deliberately unrun dependency-bound coverage

The following were not converted into passes:

- native Windows runtime matrices, including rank 20's Windows-only cases;
  Wine being installed does not provide a native Windows validation host;
- real OAuth provider redirects/credentials, direct HTTPS cookie transport,
  and trusted-proxy scheme establishment; no test OAuth credentials were
  present;
- real multi-Comfy-backend heterogeneous node/topology cases for ranks 8 and
  21; only isolated synthetic owner snapshots were run;
- GPU generation, model loading with real model libraries, and representative
  end-to-end generation flows; the isolated archive had no test models or
  configured backends;
- rank 24 cases 25–42 requiring representative real workflows, models, nodes,
  or external extensions;
- external-extension compatibility/runtime cases; external extensions and
  protected user data were excluded from the test archive;
- other operating systems, filesystems, and browser versions.

Ranks 25–32 are completed measurement dispositions whose instrumentation is
supposed to be absent. This run verified removal and current-production sanity;
it did not reintroduce instrumentation or recollect performance measurements.

## Evidence location

Accepted source, build, log, harness-copy, result, and provenance artifacts are
retained under `/tmp/swarmui-ranks-1-32-fixed-sanitized-yt40cG`, including
TSV static results, Release C# harness sources/logs, synthetic fixtures, and
retained browser scripts/hashes/logs. Exact external browser-automation runtime
dependencies were:

- Playwright module:
  `/home/john/.npm/_npx/e41f203b7505f1fb/node_modules/playwright`;
- managed Firefox executable:
  `/tmp/swarmui-ranks-1-32-BQ5HdB/playwright-browsers/firefox-1538/firefox/firefox`;
- Puppeteer module:
  `/opt/stabilitymatrix/Data/Packages/SwarmUI-exp/node_modules/puppeteer`;
- Chromium executable:
  `/home/john/.cache/puppeteer/chrome/linux-148.0.7778.97/chrome-linux64/chrome`.

The retained harness copies hardcode these module and executable paths. The
retained Chromium harness hardcoded and loaded
`/opt/stabilitymatrix/Data/Packages/SwarmUI-exp/node_modules/puppeteer`. The
sanitized source archive also contains `source/node_modules/puppeteer`,
byte-identical to that external tree, but the retained harness did not load the
sanitized copy. Only the external installed Puppeteer instance was loaded;
primary maintained source, dirty data/settings, and user content were not used
as validation source. This does not admit the rejected unsanitized source run
as accepted evidence.
