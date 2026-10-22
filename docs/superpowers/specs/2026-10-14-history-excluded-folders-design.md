# Excluded history folders

Approved behavior: add an empty-by-default Excluded History Folders list to User Options. Each entry is an exact path relative to the user's Output directory. Exclude that folder and descendants from folder browsing, recursive search, direct history listing, indexed results, fast startup scans, and forced filesystem scans. Removing an entry restores visibility without changing images or the shared index. This is a visibility preference, not an access restriction.

Normalize slash direction, repeated separators, and dot segments; ignore empty entries and paths escaping the output root. Match whole folder boundaries using platform path case conventions. No wildcard matching or automatic exclusions. Existing image-hidden toggles do not override excluded folders.

Use the existing reflected settings UI and a small reusable path matcher. Filter indexed entries before folder aggregation and result limits; prune filesystem traversal before scanning excluded folders. Do not modify index warm-up content because the index can serve multiple users. Document how to save the option and refresh history. Add isolated NUnit path-matching cases and statically review all listing branches. Do not build or run NUnit per AGENTS.md.
