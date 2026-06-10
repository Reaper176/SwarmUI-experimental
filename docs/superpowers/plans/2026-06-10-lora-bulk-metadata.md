# LoRA Bulk Metadata Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add LoRA-only multi-select bulk metadata editing on the Generate page.

**Architecture:** Reuse the shared `GenPageBrowserClass` multi-select UI, extend it with one-shot bulk actions, and add a dedicated LoRA bulk metadata modal. The frontend sends a patch object to a new `BulkEditModelMetadata` API route that validates once, edits selected local LoRA models best-effort, and reports per-model failures.

**Tech Stack:** SwarmUI JavaScript frontend, Razor modal markup, C# .NET 8 WebAPI, Newtonsoft `JObject`/`JArray`.

---

## File Structure

- Modify `src/wwwroot/js/genpage/helpers/browsers.js`: add support for bulk actions that run once with the full selected file list.
- Modify `src/wwwroot/js/genpage/gentab/models.js`: enable LoRA multi-select, expose `Bulk Edit Metadata`, implement modal population, save, tag parsing helpers, and response handling.
- Modify `src/Pages/_Generate/GenTabModals.cshtml`: add a dedicated `bulk_edit_model_modal` for LoRA patch edits.
- Modify `src/WebAPI/ModelsAPI.cs`: register and implement `BulkEditModelMetadata`, including field validation, tag modes, and model update logic.
- Modify `src/wwwroot/css/genpage.css`: add compact styling for the bulk modal selected-model list and patch rows.
- Do not run builds or automated tests. `AGENTS.md` forbids agents from running tests/builds in this repository.

---

### Task 1: Add One-Shot Browser Bulk Actions

**Files:**
- Modify: `src/wwwroot/js/genpage/helpers/browsers.js`

- [ ] **Step 1: Update bulk action label discovery to include one-shot actions**

In `getCommonMultiSelectActionLabels()`, replace:

```js
if (button.can_multi && (button.max_selected == null || files.length <= button.max_selected)) {
    labels.push(button.label);
}
```

with:

```js
if ((button.can_multi || button.bulk_once) && (button.max_selected == null || files.length <= button.max_selected)) {
    labels.push(button.label);
}
```

- [ ] **Step 2: Add one-shot execution to `runMultiSelectAction(label)`**

At the start of `runMultiSelectAction(label)`, immediately after:

```js
let files = this.getMultiSelectedFiles();
let failed = 0;
```

insert:

```js
let firstFile = files.length > 0 ? files[0] : null;
if (firstFile) {
    let firstDesc = this.describe(firstFile);
    for (let button of firstDesc.buttons || []) {
        if (button.label == label && button.bulk_once && button.onclick) {
            try {
                button.onclick(files, this);
            }
            catch (err) {
                console.error('Browser bulk action error:', err);
                showError(`Bulk action failed - see console for details.`);
            }
            this.syncMultiSelectHeader();
            return;
        }
    }
}
```

- [ ] **Step 3: Static verification**

Run:

```bash
sed -n '1560,1670p' src/wwwroot/js/genpage/helpers/browsers.js
```

Expected inspection result:

- `getCommonMultiSelectActionLabels()` includes `button.bulk_once`.
- `runMultiSelectAction(label)` checks for a matching `bulk_once` button before the existing per-file loop.
- Existing `can_multi` per-file behavior remains unchanged.

- [ ] **Step 4: Commit**

```bash
git add src/wwwroot/js/genpage/helpers/browsers.js
git commit -m "Add one-shot browser bulk actions"
```

---

### Task 2: Add the Bulk Metadata Modal Markup and Styling

**Files:**
- Modify: `src/Pages/_Generate/GenTabModals.cshtml`
- Modify: `src/wwwroot/css/genpage.css`

- [ ] **Step 1: Add modal markup after the existing `edit_model_modal`**

In `src/Pages/_Generate/GenTabModals.cshtml`, after the existing `@WebUtil.ModalFooter()` that closes `edit_model_modal`, add:

```cshtml
@WebUtil.ModalHeader("bulk_edit_model_modal", "Bulk Edit LoRA Metadata")
    <div class="modal-body">
        <div id="bulk_edit_model_summary"></div>
        <div id="bulk_edit_model_list" class="bulk-edit-model-list"></div>
        <hr>
        <div class="bulk-edit-model-row">
            <label><input type="checkbox" id="bulk_edit_model_architecture_enabled"> Architecture</label>
            <select id="bulk_edit_model_architecture" class="modal_text_extra">
                @foreach (T2IModelClass arch in T2IModelClassSorter.ModelClasses.Values.OrderBy(x => x.ID))
                {
                    <option value="@arch.ID" class="translate">@arch.Name</option>
                }
            </select>
        </div>
        <div class="bulk-edit-model-row">
            <label><input type="checkbox" id="bulk_edit_model_usage_hint_enabled"> Usage Hint</label>
            <input type="text" id="bulk_edit_model_usage_hint" class="modal_text_extra translate" placeholder="Model Usage Hint" />
        </div>
        <div class="bulk-edit-model-row">
            <label><input type="checkbox" id="bulk_edit_model_trigger_phrase_enabled"> Trigger Phrase</label>
            <input type="text" id="bulk_edit_model_trigger_phrase" class="modal_text_extra translate" placeholder="Model Trigger Phrase" />
        </div>
        <div class="bulk-edit-model-row">
            <label><input type="checkbox" id="bulk_edit_model_lora_default_weight_enabled"> Default LoRA Weight</label>
            <input type="number" min="-10" max="10" step="0.05" id="bulk_edit_model_lora_default_weight" class="modal_text_extra translate" placeholder="Default LoRA Weight" />
        </div>
        <div class="bulk-edit-model-row">
            <label><input type="checkbox" id="bulk_edit_model_lora_default_confinement_enabled"> Default LoRA Confinement</label>
            <select id="bulk_edit_model_lora_default_confinement" class="modal_text_extra translate" placeholder="Default LoRA Confinement">
                <option value="0">Global</option>
                <option value="5">Base</option>
                <option value="1">Refiner</option>
                <option value="2">Video</option>
                <option value="3">VideoSwap</option>
            </select>
        </div>
        <div class="bulk-edit-model-row">
            <label><input type="checkbox" id="bulk_edit_model_tags_enabled"> Tags</label>
            <select id="bulk_edit_model_tags_mode" class="modal_text_extra translate">
                <option value="add">Add Tags</option>
                <option value="remove">Remove Tags</option>
                <option value="replace">Replace Tags</option>
            </select>
            <input type="text" id="bulk_edit_model_tags" class="modal_text_extra translate" placeholder="Model Tags" />
        </div>
        <div id="bulk_edit_model_error" class="modal_error_bottom"></div>
    </div>
    <div class="modal-footer">
        <button type="button" class="btn btn-primary basic-button translate" onclick="bulkEditModelMetadataSave()">Save</button>
        <button type="button" class="btn btn-secondary basic-button translate" onclick="$('#bulk_edit_model_modal').modal('hide')">Cancel</button>
    </div>
@WebUtil.ModalFooter()
```

- [ ] **Step 2: Add compact CSS**

Append to `src/wwwroot/css/genpage.css`:

```css
.bulk-edit-model-list {
    max-height: 8rem;
    overflow-y: auto;
    margin-top: 0.5rem;
    padding: 0.4rem;
    border: 1px solid var(--border-color);
    border-radius: 0.25rem;
    scrollbar-width: thin;
}

.bulk-edit-model-list-entry {
    display: block;
    white-space: nowrap;
    overflow: hidden;
    text-overflow: ellipsis;
}

.bulk-edit-model-row {
    display: grid;
    grid-template-columns: minmax(12rem, 16rem) 1fr;
    gap: 0.5rem;
    align-items: center;
    margin-bottom: 0.5rem;
}

.bulk-edit-model-row label {
    margin-bottom: 0;
}
```

- [ ] **Step 3: Static verification**

Run:

```bash
rg -n "bulk_edit_model|bulk-edit-model" src/Pages/_Generate/GenTabModals.cshtml src/wwwroot/css/genpage.css
```

Expected inspection result:

- Modal IDs and CSS classes are present.
- Each editable field has an `_enabled` checkbox except tag mode, which is controlled by `bulk_edit_model_tags_enabled`.

- [ ] **Step 4: Commit**

```bash
git add src/Pages/_Generate/GenTabModals.cshtml src/wwwroot/css/genpage.css
git commit -m "Add LoRA bulk metadata modal"
```

---

### Task 3: Add Frontend LoRA Bulk Metadata Logic

**Files:**
- Modify: `src/wwwroot/js/genpage/gentab/models.js`

- [ ] **Step 1: Enable multi-select for the LoRA browser**

In `ModelBrowserWrapper` constructor, after:

```js
this.browser = new GenPageBrowserClass(container, this.listModelFolderAndFiles.bind(this), id, format, this.describeModel.bind(this), this.selectModel.bind(this), extraHeader);
```

add:

```js
if (subType == 'LoRA') {
    this.browser.allowMultiSelect = true;
}
```

- [ ] **Step 2: Add bulk edit state and modal helpers near existing model edit globals**

After:

```js
let starredModels = null;
```

add:

```js
let bulkEditModelFiles = [];
let bulkEditModelBrowser = null;
let bulkEditModelWrapper = null;

function bulkEditModelMetadata(files, browser, wrapper) {
    bulkEditModelFiles = files || [];
    bulkEditModelBrowser = browser;
    bulkEditModelWrapper = wrapper;
    if (bulkEditModelFiles.length == 0) {
        showError('No LoRAs selected.');
        return;
    }
    getRequiredElementById('bulk_edit_model_summary').innerText = `${bulkEditModelFiles.length} LoRAs selected. Only enabled fields will be changed.`;
    let list = getRequiredElementById('bulk_edit_model_list');
    list.innerHTML = '';
    for (let file of bulkEditModelFiles) {
        let entry = createSpan(null, 'bulk-edit-model-list-entry');
        entry.innerText = file.name;
        list.appendChild(entry);
    }
    for (let id of ['architecture', 'usage_hint', 'trigger_phrase', 'lora_default_weight', 'lora_default_confinement', 'tags']) {
        getRequiredElementById(`bulk_edit_model_${id}_enabled`).checked = false;
    }
    let architectureSelector = getRequiredElementById('bulk_edit_model_architecture');
    for (let opt of architectureSelector.options) {
        let slash = opt.value.indexOf('/');
        let postSlash = slash > 0 ? opt.value.substring(slash + 1) : '';
        opt.style.display = bulkEditModelWrapper.subIds.includes(postSlash) ? 'block' : 'none';
    }
    getRequiredElementById('bulk_edit_model_usage_hint').value = '';
    getRequiredElementById('bulk_edit_model_trigger_phrase').value = '';
    getRequiredElementById('bulk_edit_model_lora_default_weight').value = '';
    getRequiredElementById('bulk_edit_model_lora_default_confinement').value = '0';
    getRequiredElementById('bulk_edit_model_tags_mode').value = 'add';
    getRequiredElementById('bulk_edit_model_tags').value = '';
    getRequiredElementById('bulk_edit_model_error').innerText = '';
    $('#bulk_edit_model_modal').modal('show');
}

function bulkEditModelMetadataAddField(fields, id, key = null) {
    if (!getRequiredElementById(`bulk_edit_model_${id}_enabled`).checked) {
        return;
    }
    fields[key || id] = getRequiredElementById(`bulk_edit_model_${id}`).value;
}

function bulkEditModelMetadataSave() {
    if (!bulkEditModelBrowser || bulkEditModelFiles.length == 0) {
        showError('No LoRAs selected.');
        return;
    }
    let fields = {};
    bulkEditModelMetadataAddField(fields, 'architecture');
    bulkEditModelMetadataAddField(fields, 'usage_hint');
    bulkEditModelMetadataAddField(fields, 'trigger_phrase');
    bulkEditModelMetadataAddField(fields, 'lora_default_weight');
    bulkEditModelMetadataAddField(fields, 'lora_default_confinement');
    if (getRequiredElementById('bulk_edit_model_tags_enabled').checked) {
        fields.tags_mode = getRequiredElementById('bulk_edit_model_tags_mode').value;
        fields.tags = getRequiredElementById('bulk_edit_model_tags').value;
    }
    if (Object.keys(fields).length == 0) {
        getRequiredElementById('bulk_edit_model_error').innerText = 'Enable at least one field to edit.';
        return;
    }
    let request = {
        subtype: 'LoRA',
        models: bulkEditModelFiles.map(file => file.name),
        fields: fields
    };
    genericRequest('BulkEditModelMetadata', request, data => {
        let failed = data.failed || 0;
        if (failed > 0) {
            showError(`Bulk metadata edit finished with ${failed} failure(s).`);
            console.warn('Bulk metadata edit failures:', data.errors || []);
        }
        $('#bulk_edit_model_modal').modal('hide');
        bulkEditModelBrowser.lightRefresh();
    });
}
```

- [ ] **Step 3: Add the bulk action to LoRA card buttons**

In `describeModel(model)`, inside:

```js
else if (this.subType == 'LoRA') {
    buttons = [{ label: 'Add To Prompt', onclick: () => {
```

keep the existing `Add To Prompt` button and add this after the button array is created:

```js
            if (model.data.local && permissions.hasPermission('edit_model_metadata')) {
                buttons.push({ label: 'Bulk Edit Metadata', onclick: (files, browser) => bulkEditModelMetadata(files, browser, this), bulk_once: true });
            }
```

The resulting LoRA branch should contain both `Add To Prompt` and `Bulk Edit Metadata` for eligible local LoRAs.

- [ ] **Step 4: Static verification**

Run:

```bash
rg -n "bulkEditModel|Bulk Edit Metadata|allowMultiSelect" src/wwwroot/js/genpage/gentab/models.js
```

Expected inspection result:

- LoRA browser sets `allowMultiSelect = true`.
- `bulkEditModelMetadata()` opens the modal.
- `bulkEditModelMetadataSave()` calls `BulkEditModelMetadata`.
- `Bulk Edit Metadata` uses `bulk_once: true`.

- [ ] **Step 5: Commit**

```bash
git add src/wwwroot/js/genpage/gentab/models.js
git commit -m "Add LoRA bulk metadata frontend"
```

---

### Task 4: Add Backend Bulk Metadata API

**Files:**
- Modify: `src/WebAPI/ModelsAPI.cs`

- [ ] **Step 1: Register the API route**

In `Register()`, after:

```csharp
API.RegisterAPICall(EditModelMetadata, true, Permissions.EditModelMetadata);
```

add:

```csharp
API.RegisterAPICall(BulkEditModelMetadata, true, Permissions.EditModelMetadata);
```

- [ ] **Step 2: Add helper methods near `EditModelMetadata`**

Before `EditModelMetadata`, add:

```csharp
    /// <summary>Parses comma-separated tag text for model metadata edits.</summary>
    public static string[] ParseBulkModelTags(string tags)
    {
        if (string.IsNullOrWhiteSpace(tags))
        {
            return [];
        }
        return [.. tags.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries).DistinctBy(t => t.ToLowerFast())];
    }

    /// <summary>Applies a bulk tag edit to an existing tag set.</summary>
    public static string[] ApplyBulkModelTags(string[] existing, string mode, string tags)
    {
        string[] parsed = ParseBulkModelTags(tags);
        if (mode == "replace")
        {
            return parsed.Length == 0 ? null : parsed;
        }
        existing ??= [];
        if (mode == "remove")
        {
            HashSet<string> remove = [.. parsed.Select(t => t.ToLowerFast())];
            string[] kept = [.. existing.Where(t => !remove.Contains((t ?? "").ToLowerFast()))];
            return kept.Length == 0 ? null : kept;
        }
        List<string> result = [.. existing];
        HashSet<string> seen = [.. existing.Select(t => (t ?? "").ToLowerFast())];
        foreach (string tag in parsed)
        {
            string lowered = tag.ToLowerFast();
            if (!seen.Contains(lowered))
            {
                result.Add(tag);
                seen.Add(lowered);
            }
        }
        return result.Count == 0 ? null : [.. result];
    }
```

- [ ] **Step 3: Add `BulkEditModelMetadata` before `EditModelMetadata`**

Before `EditModelMetadata`, add:

```csharp
    [API.APIDescription("Modifies selected metadata fields on multiple LoRA models. Returns edit counts and per-model errors.", "{ \"success\": true, \"edited\": 2, \"failed\": 0, \"errors\": [] }")]
    public static async Task<JObject> BulkEditModelMetadata(Session session,
        [API.APIParameter("The model's sub-type. Only `LoRA` is supported.")] string subtype,
        [API.APIParameter("Exact filepath names of models.")] JArray models,
        [API.APIParameter("Patch object containing only metadata fields to edit.")] JObject fields)
    {
        if (subtype != "LoRA")
        {
            return new JObject() { ["error"] = "Bulk metadata editing currently only supports LoRA models." };
        }
        if (models is null || models.Count == 0)
        {
            return new JObject() { ["error"] = "No models selected." };
        }
        if (fields is null || !fields.Properties().Any())
        {
            return new JObject() { ["error"] = "No metadata fields selected." };
        }
        string architecture = fields["architecture"]?.ToString();
        T2IModelClass architectureClass = null;
        if (!string.IsNullOrWhiteSpace(architecture))
        {
            architectureClass = T2IModelClassSorter.ModelClasses.GetValueOrDefault(architecture.ToLowerFast());
            if (architectureClass is null)
            {
                return new JObject() { ["error"] = "Invalid architecture." };
            }
        }
        string tagsMode = fields["tags_mode"]?.ToString();
        if (tagsMode is not null && tagsMode is not ("add" or "remove" or "replace"))
        {
            return new JObject() { ["error"] = "Invalid tag edit mode." };
        }
        using ManyReadOneWriteLock.ReadClaim claim = Program.RefreshLock.LockRead();
        if (!Program.T2IModelSets.TryGetValue("LoRA", out T2IModelHandler handler))
        {
            return new JObject() { ["error"] = "LoRA model handler not found." };
        }
        int edited = 0;
        JArray errors = [];
        foreach (JToken modelToken in models)
        {
            string model = modelToken?.ToString();
            if (string.IsNullOrWhiteSpace(model))
            {
                errors.Add(new JObject() { ["model"] = model ?? "", ["error"] = "Model name is empty." });
                continue;
            }
            if (TryGetRefusalForModel(session, model, out JObject refusal))
            {
                errors.Add(new JObject() { ["model"] = model, ["error"] = refusal["error"]?.ToString() ?? "Model edit refused." });
                continue;
            }
            if (!handler.Models.TryGetValue(model, out T2IModel actualModel))
            {
                errors.Add(new JObject() { ["model"] = model, ["error"] = "Model not found." });
                continue;
            }
            lock (handler.ModificationLock)
            {
                if (architectureClass is not null)
                {
                    actualModel.ModelClass = architectureClass;
                }
                actualModel.Metadata ??= new();
                if (fields.TryGetValue("usage_hint", out JToken usageHint))
                {
                    actualModel.Metadata.UsageHint = usageHint?.ToString();
                }
                if (fields.TryGetValue("trigger_phrase", out JToken triggerPhrase))
                {
                    actualModel.Metadata.TriggerPhrase = triggerPhrase?.ToString();
                }
                if (fields.TryGetValue("lora_default_weight", out JToken loraDefaultWeight))
                {
                    actualModel.Metadata.LoraDefaultWeight = loraDefaultWeight?.ToString();
                }
                if (fields.TryGetValue("lora_default_confinement", out JToken loraDefaultConfinement))
                {
                    actualModel.Metadata.LoraDefaultConfinement = loraDefaultConfinement?.ToString();
                }
                if (tagsMode is not null)
                {
                    actualModel.Metadata.Tags = ApplyBulkModelTags(actualModel.Metadata.Tags, tagsMode, fields["tags"]?.ToString());
                }
            }
            handler.ResetMetadataFrom(actualModel);
            _ = Utilities.RunCheckedTask(() => actualModel.ResaveModel(), "model resave");
            edited++;
        }
        if (edited > 0)
        {
            Interlocked.Increment(ref ModelEditID);
        }
        return new JObject()
        {
            ["success"] = true,
            ["edited"] = edited,
            ["failed"] = errors.Count,
            ["errors"] = errors
        };
    }
```

- [ ] **Step 4: Static verification**

Run:

```bash
rg -n "BulkEditModelMetadata|ParseBulkModelTags|ApplyBulkModelTags|RegisterAPICall\\(BulkEditModelMetadata" src/WebAPI/ModelsAPI.cs
```

Expected inspection result:

- Route is registered with `Permissions.EditModelMetadata`.
- Helper methods are present.
- `BulkEditModelMetadata` validates subtype, model list, selected fields, architecture, and tag mode.
- The method uses explicit C# types and no `var`.

- [ ] **Step 5: Commit**

```bash
git add src/WebAPI/ModelsAPI.cs
git commit -m "Add LoRA bulk metadata API"
```

---

### Task 5: Final Static Review and Manual Verification Notes

**Files:**
- Inspect: `src/wwwroot/js/genpage/helpers/browsers.js`
- Inspect: `src/wwwroot/js/genpage/gentab/models.js`
- Inspect: `src/Pages/_Generate/GenTabModals.cshtml`
- Inspect: `src/WebAPI/ModelsAPI.cs`
- Inspect: `src/wwwroot/css/genpage.css`

- [ ] **Step 1: Check for JavaScript style conflicts**

Run:

```bash
rg -n "\\bconst\\b|===|!==|\\} else \\{|\\.forEach\\(" src/wwwroot/js/genpage/helpers/browsers.js src/wwwroot/js/genpage/gentab/models.js
```

Expected inspection result:

- No newly added `const`.
- No newly added `===` or `!==`.
- No newly added `} else {`.
- No newly added `.forEach(`.

- [ ] **Step 2: Check for C# style conflicts**

Run:

```bash
rg -n "\\bvar\\b|\\} else \\{" src/WebAPI/ModelsAPI.cs
```

Expected inspection result:

- No newly added `var`.
- No newly added `} else {`.

- [ ] **Step 3: Review diffs for unrelated changes**

Run:

```bash
git diff HEAD~4..HEAD -- src/wwwroot/js/genpage/helpers/browsers.js src/wwwroot/js/genpage/gentab/models.js src/Pages/_Generate/GenTabModals.cshtml src/WebAPI/ModelsAPI.cs src/wwwroot/css/genpage.css
```

Expected inspection result:

- Diffs only cover browser one-shot actions, LoRA bulk modal/frontend/API/styling.
- No changes to `Data/`, `Output/`, `Models/`, `dlbackend/`, `src/bin`, `src/obj`, or `src/Extensions/`.

- [ ] **Step 4: Manual verification instructions for Reaper176**

Ask the developer to verify in the live app:

```text
1. Open Generate > LoRAs.
2. Enable the browser multi-select toggle.
3. Select two or more local LoRAs.
4. Choose Bulk Edit Metadata from the action dropdown.
5. Save with no fields enabled and confirm it refuses.
6. Enable Usage Hint only, save, and confirm selected cards update while other fields remain unchanged.
7. Add tags and confirm existing tags are preserved and deduplicated.
8. Remove tags and confirm only matching tags are removed.
9. Replace tags with blank input and confirm tags clear.
10. Enable Architecture, choose a LoRA architecture, save, and confirm the cards update.
11. Confirm normal card clicks still add/remove LoRAs from generation when multi-select mode is off.
```

- [ ] **Step 5: Commit any final review-only fixes**

If static review finds a fix, commit only that fix:

```bash
git add <changed-files>
git commit -m "Polish LoRA bulk metadata edit"
```

If static review finds no fix, do not create an empty commit.
