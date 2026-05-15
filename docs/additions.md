SwarmUI Feature Implementation Plan
Target Features

Implement four related features:

Prompt Lab
A dedicated workspace for creating, saving, testing, comparing, and reusing prompts.
Wildcard Combination Generator
Generate all possible combinations from wildcard sets and optionally send them to generation.
Gallery Search and Organization
Search/filter generated images by metadata, prompt text, model, LoRA, seed, resolution, date, rating, and tags.
Side-by-Side Image Compare
Select two gallery images and compare them in a synchronized viewer.
1. Prompt Lab
Goal

Add a new workspace where users can build, store, mutate, compare, and send prompts into the normal generation flow.

This should not replace the normal Generate tab. It should act as a prompt-development layer that can hand off prompts/settings to generation.

User Stories
Basic prompt management

As a user, I want to:

Save named prompt presets.
Save prompt fragments.
Group fragments by category.
Combine fragments into a final prompt.
Send a prompt directly to Generate.
Save positive and negative prompt pairs.
Duplicate, rename, delete, import, and export prompt entries.
Prompt iteration

As a user, I want to:

Create prompt variants.
Compare variants side by side.
See what changed between variants.
Randomize selected fragments.
Lock some parts of a prompt while varying others.
Generate a batch from selected variants.
Prompt debugging

As a user, I want to:

See a rendered/expanded version of the final prompt.
Detect empty wildcard results.
Detect duplicated terms.
Detect unbalanced parentheses/brackets.
Detect suspicious syntax mistakes.
Preview token/character count if that information is available.
Proposed UI

Add a new top-level tab:

Prompt Lab
Layout

Use a three-column layout.

┌──────────────────────┬──────────────────────────┬──────────────────────┐
│ Prompt Library       │ Prompt Editor             │ Preview / Actions    │
│                      │                          │                      │
│ - Saved prompts      │ Positive prompt           │ Expanded prompt      │
│ - Fragments          │ Negative prompt           │ Diff view            │
│ - Wildcard sets      │ Notes                     │ Send to Generate     │
│ - Recent prompts     │ Tags                      │ Queue batch          │
│                      │                          │ Save variant         │
└──────────────────────┴──────────────────────────┴──────────────────────┘
Left panel: Prompt Library

Sections:

Saved Prompts
Prompt Fragments
Wildcard Sets
Recent Prompts
Favorites

Each item should support:

Click to load.
Drag into prompt editor.
Right-click menu:
Rename
Duplicate
Delete
Export
Add to Favorites
Add Tag
Center panel: Prompt Editor

Fields:

Name
Positive Prompt
Negative Prompt
Notes
Tags
Optional default model/settings link

Buttons:

Save
Save As Variant
Duplicate
Reset
Send to Generate
Generate Variants
Expand Wildcards
Generate All Wildcard Combinations
Right panel: Preview / Actions

Tabs:

Preview | Diff | Wildcards | History
Preview

Shows:

Final positive prompt.
Final negative prompt.
Expanded wildcard output.
Warnings.
Diff

Compare current prompt against:

Previous saved version.
Another saved prompt.
Prompt from selected gallery image.

Show additions/removals in a readable diff view.

Wildcards

Show:

Detected wildcard tokens.
Available values per wildcard.
Estimated number of combinations.
Combination limit warnings.
Buttons for:
Preview sample
Generate random sample
Generate all combinations
Export combinations as text/json/csv
History

Show recent edits and recently sent prompts.

Data Model

Create persistent prompt lab storage.

Suggested JSON-backed storage first, database-backed later if needed.

Example file:

Data/PromptLab/prompts.json
Data/PromptLab/fragments.json
Data/PromptLab/wildcards.json
Prompt object
{
  "id": "uuid",
  "name": "Cinematic forest creature",
  "positive": "cinematic photo of __species__ in a misty forest, __lighting__",
  "negative": "blurry, low quality, distorted",
  "notes": "Good for creature concepts",
  "tags": ["creature", "cinematic", "forest"],
  "favorite": false,
  "created_at": "2026-04-28T00:00:00Z",
  "updated_at": "2026-04-28T00:00:00Z",
  "parent_id": null,
  "linked_settings": {
    "model": null,
    "sampler": null,
    "scheduler": null,
    "cfg": null,
    "steps": null,
    "resolution": null
  }
}
Fragment object
{
  "id": "uuid",
  "name": "Soft cinematic lighting",
  "text": "soft cinematic lighting, volumetric light, gentle rim light",
  "category": "Lighting",
  "tags": ["lighting", "cinematic"],
  "favorite": true,
  "created_at": "2026-04-28T00:00:00Z",
  "updated_at": "2026-04-28T00:00:00Z"
}
Wildcard set object
{
  "id": "uuid",
  "name": "species",
  "values": [
    "wolf",
    "fox",
    "feline",
    "raptor",
    "dragon"
  ],
  "tags": ["character", "species"],
  "created_at": "2026-04-28T00:00:00Z",
  "updated_at": "2026-04-28T00:00:00Z"
}
2. Wildcard Combination Generator
Goal

Add an option to generate all possible combinations from wildcard sets.

Example prompt:

portrait of a __species__ wearing __outfit__ in __location__

Wildcard sets:

species = wolf, cat, dragon
outfit = armor, cloak
location = forest, spaceship

Total combinations:

3 × 2 × 2 = 12

Generated prompts:

portrait of a wolf wearing armor in forest
portrait of a wolf wearing armor in spaceship
portrait of a wolf wearing cloak in forest
...
Wildcard Syntax

Support at least one existing/common syntax style first.

Recommended supported syntax:

__wildcard_name__

Optional later support:

{red|blue|green}
[wildcard:species]

Start with __name__ because it is simple and widely recognizable in image generation tooling.

Required Behavior
Detect wildcard tokens

Input:

"a __color__ __animal__ in a __place__"

Detected:

["color", "animal", "place"]
Resolve wildcard values

Resolve from:

Prompt Lab wildcard sets.
Existing SwarmUI wildcard files, if SwarmUI already has a wildcard storage convention.
Inline values, later optional.
Count combinations

Formula:

total = len(set1) × len(set2) × len(set3) ...

Show warning if total exceeds a configured limit.

Default limit:

1,000 combinations

Allow advanced users to override.

Combination Modes

Add four modes:

Random Single
Random Batch
All Combinations
Sample N Combinations
Random Single

Pick one random value for each wildcard.

Random Batch

Generate N random combinations.

All Combinations

Generate every possible Cartesian product.

Sample N Combinations

Generate a random non-repeating sample from the total combination space.

UI for All Combinations

In Prompt Lab > Wildcards panel:

Detected wildcards:

__species__   5 values
__outfit__    4 values
__location__  8 values

Total combinations: 160

[Preview First 25]
[Export All]
[Send All To Queue]
[Generate All]

Add safety confirmation only when total exceeds threshold:

This will create 8,000 jobs. Continue?
Backend Function Design

Add a wildcard expansion service.

Pseudo-interface:

public class WildcardExpansionRequest
{
    public string PositivePrompt { get; set; }
    public string NegativePrompt { get; set; }
    public Dictionary<string, List<string>> WildcardSets { get; set; }
    public WildcardExpansionMode Mode { get; set; }
    public int? SampleCount { get; set; }
    public int MaxCombinations { get; set; }
    public bool ShuffleResults { get; set; }
}

public class WildcardExpansionResult
{
    public int TotalPossibleCombinations { get; set; }
    public int ReturnedCombinations { get; set; }
    public List<ExpandedPromptPair> Prompts { get; set; }
    public List<string> Warnings { get; set; }
}

public class ExpandedPromptPair
{
    public string PositivePrompt { get; set; }
    public string NegativePrompt { get; set; }
    public Dictionary<string, string> ChosenValues { get; set; }
}
Cartesian Product Algorithm

Pseudo-code:

function expandAll(prompt, wildcardMap):
    tokens = detectWildcardTokens(prompt)
    valueLists = tokens.map(token => wildcardMap[token])

    combinations = cartesianProduct(valueLists)

    for each combo in combinations:
        expanded = prompt
        for each token/value in combo:
            expanded = expanded.replace("__" + token + "__", value)
        yield expanded

Important details:

Preserve wildcard order as found in the prompt.
If the same wildcard appears multiple times, use the same selected value for all occurrences in that prompt.
If a wildcard is missing, warn and leave it unchanged unless user selects “fail on missing wildcard.”
Add optional shuffle.
Add max-combinations guard.
Acceptance Criteria

The implementation is complete when:

Prompt Lab detects wildcard tokens.
Users can define wildcard sets.
Users can preview all combinations.
Users can send all combinations to the generation queue.
Same wildcard token repeats consistently within one expanded prompt.
Missing wildcard sets produce a visible warning.
Large combination counts are blocked or confirmed according to settings.
Generated image metadata records the resolved wildcard choices.
3. Gallery Search and Organization
Goal

Improve the gallery so users can quickly find images based on generation metadata and organize large collections.

SwarmUI already operates as a local AI image generation UI with a focus on powerful workflows, so gallery indexing should remain local-first and avoid cloud dependencies.

User Stories

As a user, I want to search by:

Prompt text.
Negative prompt.
Model.
LoRA.
VAE.
Sampler.
Scheduler.
Seed.
Resolution.
Date.
Rating.
Favorite status.
Tags.
Output folder.
Generation session.
Wildcard values.
Prompt Lab prompt ID.

As a user, I want to:

Favorite images.
Rate images.
Add/remove tags.
Bulk edit metadata.
Bulk delete/move/export images.
Open image metadata.
Send image settings back to Generate.
Compare two images side by side.
Proposed UI Changes

Add a gallery toolbar:

Search prompt/model/metadata...     [Filters] [Sort] [View] [Compare] [Bulk Actions]
Filter drawer

Filters:

Date range
Model
LoRA
Sampler
Scheduler
Seed
Resolution
Favorite
Rating
Tags
Prompt Lab source
Wildcard value
Has metadata
File type
Sort options
Newest first
Oldest first
Rating high to low
Rating low to high
Model
Resolution
Seed
File size
View modes
Grid
Compact Grid
Details
Contact Sheet
Compare
Gallery Index

Create a gallery index for fast searching.

Possible storage:

Data/Gallery/index.sqlite

SQLite is recommended over plain JSON for gallery search because users may have thousands or tens of thousands of images.

Tables
images
CREATE TABLE images (
    id TEXT PRIMARY KEY,
    file_path TEXT UNIQUE NOT NULL,
    thumbnail_path TEXT,
    created_at TEXT,
    modified_at TEXT,
    width INTEGER,
    height INTEGER,
    file_size INTEGER,
    file_type TEXT,
    prompt TEXT,
    negative_prompt TEXT,
    model TEXT,
    vae TEXT,
    sampler TEXT,
    scheduler TEXT,
    seed TEXT,
    cfg REAL,
    steps INTEGER,
    prompt_lab_id TEXT,
    favorite INTEGER DEFAULT 0,
    rating INTEGER DEFAULT 0,
    notes TEXT
);
image_tags
CREATE TABLE image_tags (
    image_id TEXT NOT NULL,
    tag TEXT NOT NULL,
    PRIMARY KEY (image_id, tag)
);
image_loras
CREATE TABLE image_loras (
    image_id TEXT NOT NULL,
    lora_name TEXT NOT NULL,
    lora_weight REAL
);
image_wildcards
CREATE TABLE image_wildcards (
    image_id TEXT NOT NULL,
    wildcard_name TEXT NOT NULL,
    wildcard_value TEXT NOT NULL
);
image_metadata_raw
CREATE TABLE image_metadata_raw (
    image_id TEXT PRIMARY KEY,
    raw_json TEXT
);
Indexing Behavior
On startup
Check if gallery index exists.
If not, create it.
Optionally scan output folders in the background.
Do not block normal UI startup.
On new image generation
Insert image into index immediately.
Extract metadata.
Generate or register thumbnail.
Store Prompt Lab ID if generated from Prompt Lab.
Store wildcard choices if generated from wildcard expansion.
On manual rescan

Add button:

Gallery > Rescan Images

Options:

Quick rescan
Full rescan
Rebuild index
File movement/deletion

If files are missing:

Mark as missing.
Hide by default.
Add “clean missing entries” command.
Search Implementation

Support simple search and advanced search.

Simple search

User types:

dragon armor forest

Search across:

prompt
negative prompt
model
tags
notes
LoRA names
wildcard values
Advanced filters

Use structured query parameters internally.

Example request:

{
  "text": "dragon armor",
  "model": ["ponyDiffusionV6", "sdxlBase"],
  "tags": ["favorite", "character"],
  "rating_min": 4,
  "date_start": "2026-04-01",
  "date_end": "2026-04-28",
  "width_min": 1024,
  "height_min": 1024,
  "loras": ["character_lora"],
  "wildcards": {
    "species": ["dragon"]
  }
}
Bulk Actions

Allow selecting multiple images and applying:

Add tag.
Remove tag.
Favorite.
Unfavorite.
Set rating.
Move to folder.
Copy to folder.
Delete.
Export metadata.
Create contact sheet.
Send selected settings to Prompt Lab.
Acceptance Criteria

The gallery overhaul is complete when:

Gallery images are indexed locally.
Users can search by prompt text.
Users can filter by model, LoRA, seed, resolution, date, rating, favorite, and tags.
Users can favorite/rate/tag images.
New generated images appear in the index automatically.
Existing images can be rescanned.
Bulk actions work on selected images.
Search remains responsive with at least 10,000 indexed images.
4. Side-by-Side Image Compare
Goal

Add an option to select two images and compare them side by side.

This should be integrated into Gallery, not hidden in a separate tool.

User Stories

As a user, I want to:

Select two images from the gallery.
Click Compare.
View them side by side.
Zoom both at the same time.
Pan both at the same time.
Toggle synchronized zoom/pan.
View metadata differences.
Swap left/right.
Send either image settings back to Generate.
Mark one as favorite/winner.
UI
Entry points

Add Compare button to gallery toolbar:

[Compare]

Behavior:

Disabled until exactly two images are selected.
If one image is selected, allow “Compare with next selected.”
If more than two are selected, show “Compare first two” or open a selection picker.
Compare layout
┌──────────────────────────────┬──────────────────────────────┐
│ Image A                      │ Image B                      │
│                              │                              │
│ [zoom/pan viewer]            │ [zoom/pan viewer]            │
│                              │                              │
└──────────────────────────────┴──────────────────────────────┘

[Sync Zoom] [Sync Pan] [Fit] [100%] [Swap] [A wins] [B wins]

Metadata Diff:
Model:       same
Seed:        12345 vs 67890
Prompt:      show diff
Sampler:     Euler vs DPM++ 2M
CFG:         6.5 vs 7.0
Compare Features
Required
Side-by-side display.
Fit to screen.
100% zoom.
Zoom in/out.
Pan.
Synchronized zoom.
Synchronized pan.
Metadata diff.
Swap left/right.
Favorite/rate from compare view.
Optional later
Onion-skin overlay.
Slider wipe compare.
Difference blend.
Pixel difference heatmap.
A/B vote history.
Compare more than two images.
Metadata Diff

Compare these fields:

Prompt
Negative prompt
Model
LoRAs
VAE
Sampler
Scheduler
Seed
Steps
CFG
Resolution
Clip skip
Wildcard choices
Prompt Lab source
Date generated

For prompt diff, use a word-level diff rather than line-only diff.

Acceptance Criteria

The compare feature is complete when:

User can select exactly two images and open compare view.
Images display side by side.
Zoom and pan work.
Sync zoom/pan toggles work.
Metadata diff is visible.
User can favorite/rate either image from the compare view.
User can send either image’s generation settings back to Generate.
5. Integration Between Prompt Lab and Gallery
Required metadata link

When an image is generated from Prompt Lab, store:

{
  "prompt_lab_id": "uuid",
  "prompt_lab_name": "Creature prompt test",
  "prompt_variant_id": "uuid",
  "wildcard_values": {
    "species": "dragon",
    "outfit": "armor",
    "location": "forest"
  }
}

This allows Gallery search to answer:

Show images from this Prompt Lab entry.
Show images using wildcard species=dragon.
Show all images from this wildcard combination batch.
Compare images generated from the same prompt variant.
6. Implementation Phases
Phase 1: Foundation

Implement storage and basic APIs.

Tasks:

Add Prompt Lab data storage.
Add CRUD APIs for prompts, fragments, and wildcard sets.
Add wildcard detection.
Add wildcard preview expansion.
Add gallery SQLite index.
Add basic image indexing from existing metadata.
Add new metadata fields for prompt lab and wildcard choices.

Deliverable:

Backend supports Prompt Lab objects and gallery index.
No polished UI required yet.
Phase 2: Prompt Lab MVP

Tasks:

Add Prompt Lab tab.
Implement prompt list.
Implement prompt editor.
Implement save/load/delete/duplicate.
Implement send to Generate.
Implement wildcard detection panel.
Implement random wildcard expansion.
Implement all-combinations preview.

Deliverable:

User can create prompts and send them to Generate.
User can preview wildcard combinations.
Phase 3: Wildcard Batch Generation

Tasks:

Implement all-combinations batch queue.
Add total-combinations warning.
Add max-combinations setting.
Add shuffle option.
Add sample-N option.
Save chosen wildcard values into generated image metadata.
Add export combinations as .txt, .json, and .csv.

Deliverable:

User can generate every combination of selected wildcard sets.
Phase 4: Gallery Search MVP

Tasks:

Add search bar.
Add filter drawer.
Add model/date/resolution/seed/prompt filters.
Add favorite/rating/tags.
Add bulk tagging.
Add rescan/rebuild index buttons.
Add automatic indexing for new images.

Deliverable:

Gallery becomes searchable and organizable.
Phase 5: Side-by-Side Compare

Tasks:

Add two-image selection mode.
Add Compare button.
Implement compare view.
Add synchronized zoom/pan.
Add metadata diff.
Add favorite/rating controls.
Add “Send A to Generate” and “Send B to Generate.”

Deliverable:

User can compare two images directly from gallery.
Phase 6: Polish and Power Features

Tasks:

Prompt diff viewer.
Prompt fragment drag/drop.
Recent prompts.
Prompt-to-gallery linking.
Gallery search by wildcard value.
Contact sheet export.
Compare winner marking.
Import/export Prompt Lab library.

Deliverable:

Features feel integrated and production-ready.
7. Settings to Add

Add settings under a new or existing UI preferences section.

Prompt Lab:
- Enable Prompt Lab
- Default wildcard syntax: __name__
- Max wildcard combinations before warning: 1000
- Max wildcard combinations hard limit: 10000
- Shuffle wildcard combinations by default
- Save wildcard values into image metadata
- Auto-save prompt edits

Gallery:
- Enable gallery index
- Auto-index new images
- Index existing images on startup
- Thumbnail cache size
- Show missing files
- Default gallery sort
- Default gallery view mode

Compare:
- Sync zoom by default
- Sync pan by default
- Show metadata diff by default
8. Edge Cases
Wildcards

Handle:

Missing wildcard set.
Empty wildcard set.
Duplicate wildcard names.
Same wildcard used multiple times in one prompt.
Nested wildcard syntax.
Extremely large combination counts.
Special characters in wildcard values.
Wildcards in negative prompt.
Wildcards in prompt fragments.

Recommended MVP behavior:

No nested wildcard expansion initially.
Same wildcard name resolves to the same value within one output.
Missing wildcard produces warning.
Empty wildcard blocks generation.
Gallery

Handle:

Missing image files.
Images without metadata.
Corrupt metadata.
Duplicate files.
Moved files.
Very large galleries.
Slow network folders.
Non-image files in output folders.
Video outputs if SwarmUI gallery includes video generations.

SwarmUI supports image and video model workflows, so avoid assuming every gallery item is a still image forever.

Compare

Handle:

Different aspect ratios.
Different resolutions.
Missing metadata.
Animated outputs.
Huge images.
Deleted file while compare view is open.
9. Testing Plan
Unit tests

Add tests for:

Wildcard token detection.
Wildcard expansion.
Cartesian product generation.
Missing wildcard behavior.
Repeated wildcard consistency.
Combination count calculation.
Gallery metadata parsing.
Gallery search query generation.
Metadata diff generation.
Integration tests

Add tests for:

Save prompt.
Load prompt.
Send prompt to Generate.
Generate wildcard combination batch.
Generated image stores wildcard metadata.
Gallery indexes generated image.
Gallery search finds image by wildcard value.
Compare opens with two selected images.
Performance tests

Test with:

1,000 images.
10,000 images.
50,000 images if feasible.
100 wildcard combinations.
1,000 wildcard combinations.
10,000 wildcard combinations.

Acceptance target:

Search results should return in under 300 ms for common filters on a 10,000-image gallery.
UI should remain responsive while indexing.
Large wildcard batches should queue progressively, not freeze the browser.
10. Suggested API Endpoints / Internal Commands

Exact naming should match SwarmUI’s existing backend style, but conceptually add:

GET    /api/promptlab/prompts
POST   /api/promptlab/prompts
PUT    /api/promptlab/prompts/{id}
DELETE /api/promptlab/prompts/{id}

GET    /api/promptlab/fragments
POST   /api/promptlab/fragments
PUT    /api/promptlab/fragments/{id}
DELETE /api/promptlab/fragments/{id}

GET    /api/promptlab/wildcards
POST   /api/promptlab/wildcards
PUT    /api/promptlab/wildcards/{id}
DELETE /api/promptlab/wildcards/{id}

POST   /api/promptlab/wildcards/detect
POST   /api/promptlab/wildcards/expand
POST   /api/promptlab/wildcards/generate-batch

GET    /api/gallery/search
POST   /api/gallery/rescan
POST   /api/gallery/rebuild-index
POST   /api/gallery/bulk-tag
POST   /api/gallery/bulk-rate
POST   /api/gallery/bulk-favorite

GET    /api/gallery/compare
11. Definition of Done

The feature set is done when:

Prompt Lab exists as a visible tab.
Users can save, edit, duplicate, delete, tag, and favorite prompts.
Users can save reusable prompt fragments.
Users can define wildcard sets.
Users can preview wildcard expansion.
Users can generate all possible wildcard combinations.
Wildcard batch jobs save chosen wildcard values into metadata.
Gallery has a local searchable index.
Gallery can search/filter by prompt, model, LoRA, seed, date, resolution, favorite, rating, tags, and wildcard values.
Users can bulk edit gallery tags/favorites/ratings.
Users can select two images and compare them side by side.
Compare view supports synchronized zoom/pan.
Compare view shows metadata differences.
Compare view can send either image settings back to Generate.
Existing workflows remain unaffected.
Feature can be disabled in settings.
Large galleries and large wildcard batches do not freeze the UI.
12. Recommended MVP Cut

For the first pull request, keep the scope tight:

PR 1: Prompt Lab + Wildcard Preview

Implement:

Prompt Lab tab.
Save/load/delete prompts.
Save/load/delete wildcard sets.
Detect __wildcard__ syntax.
Preview all combinations.
Export combinations.

Do not generate batches yet.

PR 2: Wildcard Batch Generation

Implement:

Send combinations to queue.
Max-combination warning.
Save wildcard metadata into generated image metadata.
PR 3: Gallery Index/Search

Implement:

SQLite index.
Basic search.
Filters for prompt, model, seed, resolution, date.
Favorite/rating/tags.
PR 4: Side-by-Side Compare

Implement:

Select two images.
Compare view.
Sync zoom/pan.
Metadata diff.