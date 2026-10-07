/** Browses and edits chants in the user's selected shared JSON source. */
class ChantHelpers {
    /** Initializes the shared browser and editor event handlers. */
    constructor() {
        this.chants = [];
        this.source = '';
        this.revision = '';
        this.loaded = false;
        this.dataVersion = 0;
        this.editorSource = '';
        this.editorRevision = '';
        this.originalName = '';
        this.thumbnail = '';
        this.imageReadId = 0;
        this.uploading = false;
        this.saving = false;
        this.browser = new GenPageBrowserClass('chant_list', this.list.bind(this), 'chantbrowser', 'Cards', this.describe.bind(this),
            (file) => this.insert(file.data, false),
            '<button type="button" id="chant_create_button" class="basic-button translate">Create New</button>');
        this.browser.showDepth = false;
        this.browser.showUpFolder = false;
        this.browser.container.addEventListener('click', event => this.activateCardText(event));
        this.browser.container.addEventListener('keydown', event => {
            if (event.key == 'Enter' || event.key == ' ') {
                this.activateCardText(event);
            }
        });
        this.browser.builtEvent = () => {
            let button = document.getElementById('chant_create_button');
            if (button) {
                button.disabled = !this.source || !permissions.hasPermission('edit_chants');
                button.onclick = () => this.openEditor(null, true);
            }
        };
        getRequiredElementById('chantstabheader').addEventListener('shown.bs.tab', () => this.browser.update());
        getRequiredElementById('chant_editor_save').addEventListener('click', () => this.save());
        getRequiredElementById('chant_editor_image').addEventListener('change', () => this.readThumbnail());
        getRequiredElementById('chant_editor_remove_image').addEventListener('click', () => {
            this.imageReadId++;
            this.uploading = false;
            this.thumbnail = '';
            getRequiredElementById('chant_editor_image').value = '';
            this.updatePreview();
            this.updateSaveButton();
        });
        permissions.registerApplyCallback(() => {
            if (this.browser.everLoaded) {
                this.browser.rerender();
            }
        });
    }

    /** Makes the card's text preview usable with mouse, touch, or keyboard. */
    activateCardText(event) {
        let text = event.target.closest('.model-descblock');
        let card = text?.closest('.chant-card');
        if (!card) {
            return;
        }
        let chant = this.chants.find(entry => entry.name == card.dataset.name);
        if (chant) {
            event.preventDefault();
            this.insert(chant, false);
        }
    }

    /** Invalidates browser data when the source or user settings change. */
    userDataChanged(data) {
        this.dataVersion++;
        this.loaded = false;
        this.browser.lastListCache = null;
        this.browser.lastRenderSignature = null;
        if (data.chant_source != this.source) {
            this.chants = [];
            this.source = data.chant_source || '';
            this.revision = '';
        }
        if (getRequiredElementById('Chants-Tab').classList.contains('active')) {
            this.browser.update();
        }
    }

    /** Fetches full chant cards, or supplies cached data to the browser's filter. */
    list(path, isRefresh, callback, depth, fail) {
        let finish = () => {
            callback([], this.chants.map(chant => ({ name: chant.name, data: chant })));
            getRequiredElementById('chant_source_status').textContent = this.source
                ? `${this.source} · ${this.chants.length} chants · Click a card to insert into the positive prompt; use its menu for details, editing, or the negative prompt.`
                : 'Select a Chant Source under User Settings → AutoComplete to browse chants.';
        };
        if (this.loaded && !isRefresh) {
            finish();
            return;
        }
        let version = this.dataVersion;
        genericRequest('GetChantList', {}, data => {
            if (version != this.dataVersion) {
                this.list(path, true, callback, depth, fail);
                return;
            }
            this.source = data.source;
            this.revision = data.revision;
            this.chants = data.chants;
            this.loaded = true;
            this.browser.lastRenderSignature = null;
            finish();
        }, 0, error => {
            if (version != this.dataVersion) {
                this.list(path, true, callback, depth, fail);
                return;
            }
            this.loaded = false;
            this.chants = [];
            getRequiredElementById('chant_source_status').textContent = error;
            fail(error);
        });
    }

    /** Allows only embedded raster thumbnails, never remote or active image URLs. */
    safeThumbnail(value) {
        return typeof value == 'string' && /^data:image\/(png|jpeg|webp);base64,[A-Za-z0-9+/=\r\n]+$/.test(value) && value.length <= 2800000 ? value : '';
    }

    /** Describes a card, with escaped text and the browser's touch-friendly menu. */
    describe(file) {
        let chant = file.data;
        let description = typeof chant.description == 'string' ? chant.description : '';
        let buttons = [
            { label: 'Insert into Positive Prompt', onclick: () => this.insert(chant, false) },
            { label: 'Insert into Negative Prompt', onclick: () => this.insert(chant, true) },
            { label: 'View Details', onclick: () => this.openEditor(chant, false) }
        ];
        if (permissions.hasPermission('edit_chants')) {
            buttons.push({ label: 'Edit Chant', onclick: () => this.openEditor(chant, true) });
        }
        let color = Number.isInteger(chant.color) && chant.color >= 0 && chant.color <= 5 ? chant.color : 0;
        let fallback = `<svg xmlns="http://www.w3.org/2000/svg" width="256" height="256" viewBox="0 0 256 256"><rect width="256" height="256" fill="#283444"/><text x="128" y="145" text-anchor="middle" font-family="sans-serif" font-size="60" fill="#e2e8f0">Aa</text></svg>`;
        return {
            name: chant.name,
            description: `<strong class="tag-text tag-type-${color}">${escapeHtml(chant.name)}</strong><div class="chant-card-description">${escapeHtml(description)}</div><div class="chant-content-preview">${escapeHtml(chant.content)}</div>`,
            image: this.safeThumbnail(chant.thumbnail) || `data:image/svg+xml,${encodeURIComponent(fallback)}`,
            className: 'chant-card',
            searchable: `${chant.name} ${chant.terms || ''} ${description} ${chant.content}`,
            detail_list: [chant.name, description, chant.content].map(text => escapeHtmlNoBr(text)),
            buttons
        };
    }

    /** Inserts a named chant at the remembered selection in the requested prompt. */
    insert(chant, negative) {
        let [selectedBox] = uiImprover.getLastSelectedTextbox();
        let id = negative ? 'negativeprompt' : 'prompt';
        let altBox = getRequiredElementById(`alt_${id}_textbox`);
        let standardBox = document.getElementById(`input_${id}`);
        let box = selectedBox == altBox || selectedBox == standardBox ? selectedBox : altBox;
        let start = box.selectionStart ?? box.value.length;
        let end = box.selectionEnd ?? start;
        let before = box.value.substring(0, start);
        let after = box.value.substring(end);
        let tag = `${before && !/\s$/.test(before) ? ' ' : ''}<chant:${chant.name}>${after && !/^\s/.test(after) ? ' ' : ''}`;
        box.value = before + tag + after;
        triggerChangeFor(box);
        box.focus();
        box.setSelectionRange(start + tag.length, start + tag.length);
    }

    /** Opens a view-only or editable form while capturing the exact source revision. */
    openEditor(chant, editable) {
        if (this.saving) {
            return;
        }
        if (editable && (!this.source || !permissions.hasPermission('edit_chants'))) {
            return;
        }
        this.editorSource = this.source;
        this.editorRevision = this.revision;
        this.originalName = chant?.name || '';
        this.editable = editable;
        this.imageReadId++;
        this.uploading = false;
        this.thumbnail = this.safeThumbnail(chant?.thumbnail);
        getRequiredElementById('chant_editor_title').textContent = editable ? (chant ? 'Edit Chant' : 'Create Chant') : 'Chant Details';
        getRequiredElementById('chant_editor_source').textContent = this.source;
        getRequiredElementById('chant_editor_error').textContent = '';
        for (let field of ['name', 'terms', 'content', 'description', 'color']) {
            let input = getRequiredElementById(`chant_editor_${field}`);
            input.value = chant?.[field] ?? (field == 'color' ? 0 : '');
            input.disabled = !editable;
        }
        getRequiredElementById('chant_editor_image').value = '';
        getRequiredElementById('chant_editor_image').disabled = !editable;
        getRequiredElementById('chant_editor_remove_image').hidden = !editable;
        getRequiredElementById('chant_editor_save').hidden = !editable;
        this.updatePreview();
        this.updateSaveButton();
        $('#chant_editor_modal').modal('show');
    }

    /** Shows or hides the embedded thumbnail. */
    updatePreview() {
        let preview = getRequiredElementById('chant_editor_preview');
        preview.hidden = !this.thumbnail;
        if (this.thumbnail) {
            preview.src = this.thumbnail;
        }
        else {
            preview.removeAttribute('src');
        }
    }

    /** Keeps saving disabled while an upload or save is in progress. */
    updateSaveButton() {
        getRequiredElementById('chant_editor_save').disabled = this.saving || this.uploading;
    }

    /** Reads a bounded raster file into a data URL without uploading elsewhere. */
    readThumbnail() {
        let file = getRequiredElementById('chant_editor_image').files[0];
        let readId = ++this.imageReadId;
        this.uploading = false;
        this.updateSaveButton();
        let error = getRequiredElementById('chant_editor_error');
        error.textContent = '';
        if (!file) {
            return;
        }
        if (!['image/png', 'image/jpeg', 'image/webp'].includes(file.type) || file.size > 2 * 1024 * 1024) {
            error.textContent = 'Choose a PNG, JPEG, or WebP image under 2 MiB.';
            return;
        }
        this.uploading = true;
        this.updateSaveButton();
        let reader = new FileReader();
        let finish = () => {
            if (readId == this.imageReadId) {
                this.uploading = false;
                this.updateSaveButton();
            }
        };
        reader.onload = () => {
            if (readId != this.imageReadId) {
                return;
            }
            this.thumbnail = this.safeThumbnail(reader.result);
            this.updatePreview();
            finish();
        };
        reader.onerror = () => {
            if (readId == this.imageReadId) {
                error.textContent = 'Could not read the thumbnail.';
            }
            finish();
        };
        reader.readAsDataURL(file);
    }

    /** Saves the form, preserving it on validation failures or concurrent edit conflicts. */
    save() {
        if (!this.editable || this.saving || this.uploading || !permissions.hasPermission('edit_chants')) {
            return;
        }
        let error = getRequiredElementById('chant_editor_error');
        let request = { source: this.editorSource, revision: this.editorRevision, originalName: this.originalName, thumbnail: this.thumbnail };
        for (let field of ['name', 'terms', 'content', 'description', 'color']) {
            request[field] = getRequiredElementById(`chant_editor_${field}`).value;
        }
        request.name = request.name.trim();
        request.color = Number(request.color);
        if (!request.name || !request.content.trim()) {
            error.textContent = 'Provide a name and prompt content.';
            return;
        }
        error.textContent = '';
        this.saving = true;
        this.updateSaveButton();
        genericRequest('SaveChant', request, () => {
            this.saving = false;
            this.updateSaveButton();
            $('#chant_editor_modal').modal('hide');
            this.loaded = false;
            loadUserData();
            this.browser.update(true);
        }, 0, message => {
            this.saving = false;
            this.updateSaveButton();
            error.textContent = message;
        });
    }
}

/** Shared chant browser and editor. */
let chantHelpers = new ChantHelpers();
