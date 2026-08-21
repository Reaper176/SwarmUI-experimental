# Upstream Sync Design

## Goal

Integrate the complete upstream range from `c28a34c5` through `fff8db6c` into the fork while preserving the fork's behavior, recent refactors, and Anima/Qwen 3.5 support.

## Scope

The integration covers all 64 upstream commits and all 41 affected files. This includes the mobile layout work, Anima text-encoder LoRA support, H3 updates, LTX 2.5 support, MiniMax Music support, SeedVR2 support, model downloader improvements, server-tab refinements, and the smaller fixes and documentation updates in the range.

No pre-existing uncommitted user files or generated/local data are part of the integration.

## Integration Strategy

Create an isolated worktree and integration branch from the current `master`, then merge `upstream/master` with a merge commit. A real merge preserves upstream ancestry and reduces repeated conflict work during later upstream syncs.

Cleanly merged upstream changes remain intact. For conflicts, the fork is authoritative for architecture and existing behavior, while upstream is authoritative for the new feature intent in this range. Conflict resolution therefore ports upstream behavior into the fork's current abstractions instead of restoring superseded upstream structure.

## Adaptation Rules

- Preserve the fork's refactored JavaScript ownership, classes, helpers, and compatibility exports. Integrate upstream mobile and UI behavior at the corresponding current owners.
- Preserve the fork's Comfy capability catalog, workflow graph editor, persistence boundaries, and diagnostic/input-name abstractions. Express new upstream workflow and model-support behavior through those APIs.
- Preserve all fork-specific Anima/Qwen 3.5 loaders and validation while incorporating upstream Anima LoRA support.
- Preserve fork fixes and performance behavior unless an upstream change intentionally supersedes them.
- Accept upstream documentation, dependency metadata, and isolated fixes directly when they do not conflict with fork behavior.
- Do not include unrelated cleanup or refactoring.

## Conflict Resolution Process

Resolve conflicts by functional area so related behavior can be reviewed together:

1. Backend model and workflow generation changes.
2. Frontend layout, mobile behavior, and parameter handling.
3. Server, utility, and user tabs.
4. Styles and shared page markup.
5. Documentation and configuration metadata.

For each conflict, compare the common ancestor, fork version, and upstream version; identify the upstream behavior being introduced; then implement that behavior at the fork's current architectural boundary. Remove all merge markers and review the completed diff for accidental deletion of either fork features or upstream additions.

## Safety and Verification

The integration occurs outside the dirty primary worktree. The existing uncommitted `AGENTS.md`, data files, `Text2Image.cshtml`, and untracked local-data paths remain untouched.

Repository policy prohibits agents from running builds or tests. Verification is therefore limited to allowed static checks:

- confirm the merge has both expected parents;
- confirm every upstream commit is reachable from the result;
- scan for unresolved merge markers and unmerged index entries;
- inspect the complete merge diff and compare feature coverage against the 64-commit upstream range;
- run available non-test syntax or lint checks where they do not invoke a build;
- document areas requiring maintainer validation in the live application.

## Expected Result

An integration branch containing one merge of `upstream/master` into the current fork, with the entire upstream range represented and conflicts adapted to the fork's refactored architecture. The branch will be ready for Reaper176 to review and manually validate before it is brought onto `master`.
