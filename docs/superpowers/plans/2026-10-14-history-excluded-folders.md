# Excluded History Folders Implementation Plan

> **For agentic workers:** Execute inline; use superpowers:executing-plans for task checkpoints.

**Goal:** Let each user hide selected output folders from history without changing files.

**Architecture:** Existing reflected user settings provide list editing and persistence. A path matcher is used by indexed listings and filesystem traversals before aggregation and limits.

**Tech Stack:** C# 12, existing AutoConfiguration settings and NUnit.

- [x] Add `SwarmUITests/HistoryFolderExclusionsTests.cs`: selected folders, descendants, sibling boundaries, separators, dot segments, empty/escaping entries and independent user lists. NUnit execution is deferred under repository policy.
- [x] Add `src/Utils/HistoryFolderExclusions.cs`: normalize configured relative folders once, expose `IsExcluded(string)` using whole folder boundaries and platform case comparison.
- [x] Add `ExcludedHistoryFolders` as an empty `List<string>` beside history settings in `src/Core/Settings.cs`, with a user-facing explanation and example.
- [x] Update `src/WebAPI/ImageHistoryAPI.cs`: return empty listings for explicitly excluded requests; pass the matcher to the indexed branch; exclude indexed paths and special folders before aggregation; prune both fast-first and normal folder traversal before file enumeration. Preserve shared indexing and existing hidden-file behavior.
- [x] Document usage in `docs/User Settings.md`. Review `git diff --check`, changed files, and every history listing branch. No build or GPU work.

The reflected settings serializer displays list values with `||`, but the existing parser only accepted commas. Updated `AdminAPI.DataToType` to accept the displayed delimiter while preserving legacy comma parsing, with focused regression cases. Static checks performed; NUnit/build execution deferred under AGENTS.md.
