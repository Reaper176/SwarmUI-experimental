# Startup recovery and model diagnostics

**Goal:** Recover browser initialization after slow model refreshes or transient connection failures, and remove redundant model diagnostics.

**Design:** Retain the catalog lock and existing classification decisions. Share concurrent session requests and propagate failures. Retry only startup reads, with bounded backoff, so generation and other mutations are never automatically replayed. Retain actionable unknown-model diagnostics and summarize special-character scan warnings without renaming user files.

**Validation:** Repository instructions prohibit builds and tests. Review control flow and diffs statically; the maintainer validates the running application.

**Maintainer result:** Reaper176 confirmed UI loading was much better and subsequently loading correctly, and authorized committing and pushing the fixes. This confirms the observed UI improvement; it does not establish runtime validation of the later GLoRA classification change or every failure scenario below.

- [x] In `src/wwwroot/js/site.js`, queue session callers behind one request, release all callers on success/failure, and forward session errors to HTTP/WebSocket callers. Retry rejected impersonation directly with the impersonation field removed.
- [x] In `src/wwwroot/js/genpage/main.js`, retry initial session, parameter, and user-data requests up to three times, at 2/4/8 seconds after failures. Show the endpoint and failure to the user, and schedule history once, independently of parameter success. Keep initialization callbacks outside retry error handling so partially applied UI initialization is not replayed.
- [x] In `src/Text2Image/T2IModelClassSorter.cs`, retain the three-argument public method and add a diagnostic-control overload for the preliminary embedded-header classification in `T2IModelHandler.cs`.
- [x] In `T2IModel.cs` and `T2IModelHandler.cs`, retain per-model runtime warnings but summarize special-character warnings during scans, preserving the existing path report and debug details.
- [x] In `T2IModelHandler.cs`, advance stale unknown classifications to the current cache revision when the header was successfully read, even when classification remains unknown. Retain retries after failed reads and preserve previously known classes when classification fails. Model/sidecar changes and future classifier revisions still invalidate the cache.
- [x] Review diffs, call sites, retry limits, session queue release, impersonation fallback, and public C# signatures. JavaScript syntax checks (`node --check`) and `git diff --check` passed. Static review identified and removed redundant history scheduling that could replace a slow in-flight request. No builds, tests, or live runtime validation were performed.

Manual verification after the maintainer builds: open the UI during a model refresh; allow a startup request to time out and verify recovery without refreshing the page; temporarily interrupt connectivity and verify session retry; verify invalid impersonation falls back once; inspect a stale-cache model scan for a single final unknown-class message and a filename-warning summary. Unknown model formats still require actual header evidence before classifier changes.
