# LoRA Bulk Metadata Design

## Context

The Generate page LoRAs tab displays LoRA model cards through the shared model browser. The browser already has an optional multi-select mode and derives bulk actions from per-card menu buttons. Single-model metadata editing is handled by the `EditModelMetadata` API route and the existing model metadata modal.

This feature adds a LoRA-only bulk metadata operation for selected LoRA model cards.

## Scope

In scope:

- Enable multi-select on the Generate page LoRAs tab.
- Add a `Bulk Edit Metadata` action for selected local LoRA models.
- Add a dedicated patch-style bulk metadata modal.
- Add a server API route that applies enabled metadata fields to selected LoRA models.
- Refresh the LoRA browser after a successful bulk edit.

Out of scope:

- Bulk editing non-LoRA model browsers.
- Bulk editing title, description, preview image, linked preset, Civitai metadata, or raw headers.
- Running builds or automated tests. This repository does not permit agents to run those.

## User Behavior

The LoRAs tab exposes the shared browser multi-select toggle. While multi-select mode is active, clicking LoRA cards toggles bulk selection instead of adding or removing LoRAs from the current generation parameters.

When one or more selected LoRAs support the action, the bulk action dropdown includes `Bulk Edit Metadata`. Choosing it opens a modal that shows the selected count and a compact list of selected LoRA names.

The modal is patch-based. Each editable field has its own enable control. Disabled fields are ignored entirely and preserve existing metadata on every selected model.

Fields:

- Architecture
- Usage Hint
- Trigger Phrase
- Default LoRA Weight
- Default LoRA Confinement
- Tags

Architecture is never enabled by default. Users must explicitly enable it before a bulk edit can reclassify selected LoRAs.

Tags support three modes:

- `Add`: append parsed tags without duplicating existing tags case-insensitively.
- `Remove`: remove matching parsed tags case-insensitively.
- `Replace`: replace the full tag list with parsed tags, or clear tags when the input is blank.

## Frontend Design

The LoRA `ModelBrowserWrapper` sets `browser.allowMultiSelect = true`.

LoRA card descriptions add a `Bulk Edit Metadata` button only when:

- The browser subtype is `LoRA`.
- The model is local.
- The user has `edit_model_metadata`.

The shared browser bulk action system will be extended with an optional once-per-selection action path. Existing `can_multi` actions continue to run once per selected item. A new action shape can run once with the selected files and the browser instance, so `Bulk Edit Metadata` opens one modal for the whole selection.

The bulk metadata modal is separate from the single-model edit modal. This avoids accidental interaction with single-model features such as preview image replacement, linked presets, Civitai loading, and title or description edits.

On save, the browser builds a request with:

- `subtype: "LoRA"`
- `models`: exact selected model filenames
- `fields`: only enabled patch fields

Example:

```json
{
  "subtype": "LoRA",
  "models": ["foo.safetensors", "folder/bar.safetensors"],
  "fields": {
    "architecture": "stable-diffusion-xl-v1/lora",
    "usage_hint": "Use around 0.7 strength.",
    "trigger_phrase": "example trigger",
    "lora_default_weight": "0.7",
    "lora_default_confinement": "5",
    "tags_mode": "add",
    "tags": "style, character"
  }
}
```

After success, the modal closes and the LoRA browser light-refreshes so updated metadata appears on cards.

## Backend Design

Add a new API route guarded by `Permissions.EditModelMetadata` named `BulkEditModelMetadata`.

Inputs:

- `subtype`: model subtype. The first implementation accepts only `LoRA`.
- `models`: exact model filenames.
- `fields`: metadata patch object.

Validation:

- Reject an empty model list.
- Reject requests with no enabled fields.
- Reject subtype values other than `LoRA`.
- Validate `architecture` against known model classes before editing any model.
- Ignore unsupported field keys server-side rather than trusting the browser.

For each valid model:

- Resolve the model from `Program.T2IModelSets["LoRA"]`.
- Reuse existing model refusal checks.
- Lock the handler modification lock while updating metadata.
- Ensure `actualModel.Metadata` exists.
- Apply only fields present in the patch.
- Call `handler.ResetMetadataFrom(actualModel)`.
- Queue `actualModel.ResaveModel()` through the existing checked task pattern.

The route increments `ModelsAPI.ModelEditID` after any successful edit and returns:

- `success`
- `edited`
- `failed`
- per-model error details for missing, refused, or failed models

Partial failures are best-effort per model. Valid models are updated even if some selected names are invalid, missing, or refused. The frontend shows a warning when the response reports failures.

## Validation and Manual Verification

Static validation before handoff:

- Confirm JavaScript syntax and control flow by inspection.
- Confirm C# route signatures and explicit types follow repository style.
- Confirm unsupported or disabled fields cannot overwrite metadata.
- Confirm architecture validation happens before any model is edited.

Manual verification for the developer:

- Open Generate, LoRAs tab.
- Enable multi-select.
- Select multiple local LoRAs.
- Use `Bulk Edit Metadata`.
- Verify disabled fields remain unchanged.
- Verify tag add, remove, and replace behavior.
- Verify Architecture only changes when enabled.
- Verify updated card metadata appears after the browser refresh.
