# Chants

Chants are named, reusable prompt fragments. Select a JSON file under **User Settings → AutoComplete → Chant Source**, then open **Chants** beside the model and preset browsers.

Search cards by name, search terms, description, or prompt content. Switch between cards, thumbnails, and list views with the browser's display controls. Click a card to insert `<chant:Name>` into the positive prompt. Its menu also offers **Insert into Negative Prompt**, **View Details**, and **Edit Chant**. The server expands the tag into the chant's content during generation.

**Create New** and **Edit Chant** require the **Edit Chants** permission. This edits the selected shared JSON file, so everyone using that source sees the changes. Renaming a chant requires updating existing tags in saved prompts. Save rejects concurrent changes instead of overwriting them; refresh the browser and reopen the editor if it reports a conflict.

Each chant uses the existing `name`, `terms`, `content`, and `color` fields. The editor also supports optional `description` and `thumbnail` fields. Existing files continue to work without them. Upload a PNG, JPEG, or WebP thumbnail up to 2 MiB, or remove it to return to the default text icon. Descriptions and thumbnails do not become part of the generated prompt. Saving preserves other entries and custom fields and refreshes autocomplete.

If no cards appear, select a Chant Source and refresh parameter values. After editing a file outside SwarmUI, refresh parameter values to update autocomplete and generation's cached definitions, then refresh the Chants browser to update its cards.

## Example collection

[demo-chants.json](Examples/demo-chants.json) contains 407 chants, mixing short fragments and longer combinations across quality, styles, lighting, composition, materials, atmosphere, and concepts. Copy it into `Data/Autocompletions`, refresh parameter values, and select it as your Chant Source. Back up any existing file with that name before copying. The local Data folder remains excluded from Git.
