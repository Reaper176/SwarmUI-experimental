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
        this.batchPreviewActive = false;
        this.batchPreviewVersion = 0;
        this.batchPreviewTimer = null;
        this.batchPreviewLastImage = '';
        this.batchPreviewStatus = '';
        this.currentImageButton = document.getElementById('chant_editor_use_current_image');
        // Static scripts can update before compiled Razor markup is rebuilt.
        if (!this.currentImageButton) {
            this.currentImageButton = document.createElement('button');
            this.currentImageButton.id = 'chant_editor_use_current_image';
            this.currentImageButton.type = 'button';
            this.currentImageButton.className = 'basic-button translate';
            this.currentImageButton.textContent = 'Use Current Image';
            let preview = getRequiredElementById('chant_editor_preview');
            preview.parentNode.insertBefore(this.currentImageButton, preview);
        }
        this.browser = new GenPageBrowserClass('chant_list', this.list.bind(this), 'chantbrowser', 'Cards', this.describe.bind(this),
            (file) => this.insert(file.data, false),
            '<button type="button" id="chant_create_button" class="basic-button translate">Create New</button> <button type="button" id="chant_batch_preview_button" class="basic-button translate">Watch Batch Preview</button> <span id="chant_batch_preview_status" role="status"></span>');
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
            this.updateBatchPreviewControls();
        };
        getRequiredElementById('chantstabheader').addEventListener('shown.bs.tab', () => this.browser.update());
        getRequiredElementById('chant_editor_save').addEventListener('click', () => this.save());
        getRequiredElementById('chant_editor_image').addEventListener('change', () => this.readThumbnail());
        this.currentImageButton.addEventListener('click', () => this.useCurrentImage());
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

    /** Updates the preview-following controls after browser headers are rebuilt. */
    updateBatchPreviewControls() {
        let button = document.getElementById('chant_batch_preview_button');
        let status = document.getElementById('chant_batch_preview_status');
        if (button) {
            button.textContent = this.batchPreviewActive ? 'Stop Watching Preview' : 'Watch Batch Preview';
            button.onclick = () => this.toggleBatchPreview();
        }
        if (status) {
            status.textContent = this.batchPreviewStatus;
        }
    }

    /** Starts or stops following completed images from the latest chant-thumbnail batch. */
    toggleBatchPreview() {
        this.batchPreviewActive = !this.batchPreviewActive;
        let version = ++this.batchPreviewVersion;
        clearTimeout(this.batchPreviewTimer);
        this.batchPreviewStatus = '';
        this.batchPreviewLastImage = '';
        this.updateBatchPreviewControls();
        if (!this.batchPreviewActive) {
            return;
        }
        genericRequest('ListImages', { path: '', depth: 1, sortBy: 'DateEdited', fastFirst: true, fastFirstLimit: 1 }, data => {
            if (version != this.batchPreviewVersion) {
                return;
            }
            let folders = (data.folders || []).filter(folder => /^chant-thumbnails(?:-|$)/.test(folder)).sort();
            if (!folders.length) {
                this.failBatchPreview('No chant-thumbnail batch folder was found in your image history.', version);
                return;
            }
            this.batchPreviewFolder = folders[folders.length - 1];
            this.pollBatchPreview(version);
        }, 0, message => this.failBatchPreview(message, version));
    }

    /** Stops preview following on an error without interrupting the generation batch. */
    failBatchPreview(message, version) {
        if (version != this.batchPreviewVersion) {
            return;
        }
        this.batchPreviewActive = false;
        this.batchPreviewVersion++;
        clearTimeout(this.batchPreviewTimer);
        this.batchPreviewStatus = message;
        this.updateBatchPreviewControls();
    }

    /** Loads only the newest saved batch image and displays it without growing the batch strip. */
    pollBatchPreview(version) {
        if (!this.batchPreviewActive || version != this.batchPreviewVersion) {
            return;
        }
        let schedule = () => {
            if (this.batchPreviewActive && version == this.batchPreviewVersion) {
                this.batchPreviewTimer = setTimeout(() => this.pollBatchPreview(version), 3000);
            }
        };
        if (document.visibilityState == 'hidden') {
            schedule();
            return;
        }
        genericRequest('ListImages', { path: this.batchPreviewFolder, depth: 1, sortBy: 'DateEdited', sortReverse: false, fastFirst: true, fastFirstLimit: 1 }, data => {
            if (version != this.batchPreviewVersion) {
                return;
            }
            let file = data.files?.[0];
            if (file && file.src != this.batchPreviewLastImage) {
                this.batchPreviewLastImage = file.src;
                let metadata = file.metadata || '';
                let chantName = '';
                try {
                    let parsed = typeof metadata == 'string' ? JSON.parse(metadata) : metadata;
                    chantName = parsed?.sui_extra_data?.chant_thumbnail_name || '';
                }
                catch (error) {
                    // The image can still be displayed when its metadata is unavailable.
                }
                let src = `${getImageOutPrefix()}/${this.batchPreviewFolder}/${file.src}`;
                setCurrentImage(src, metadata, `chant-thumbnail-preview-${file.src}`, false, true);
                this.batchPreviewStatus = chantName ? `Latest chant: ${chantName}` : `Watching ${this.batchPreviewFolder}`;
                this.updateBatchPreviewControls();
            }
            schedule();
        }, 0, message => this.failBatchPreview(message, version));
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
        this.currentImageButton.hidden = !editable;
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
        this.currentImageButton.disabled = this.saving || this.uploading;
    }

    /** Uses the displayed still image as a small embedded thumbnail, preserving the previous one on failure. */
    useCurrentImage() {
        if (!this.editable || this.saving || this.uploading || !permissions.hasPermission('edit_chants')) {
            return;
        }
        let error = getRequiredElementById('chant_editor_error');
        let image = currentImageHelper.getCurrentImage();
        if (!image || image.tagName != 'IMG' || !image.src) {
            error.textContent = 'Select or display a still image first.';
            return;
        }
        let readId = ++this.imageReadId;
        this.uploading = true;
        error.textContent = '';
        this.updateSaveButton();
        let timeout = setTimeout(() => {
            if (readId == this.imageReadId) {
                this.imageReadId++;
                this.uploading = false;
                error.textContent = 'Could not load the current image. Try again or upload a thumbnail.';
                this.updateSaveButton();
            }
        }, 30000);
        let finish = data => {
            clearTimeout(timeout);
            if (readId != this.imageReadId) {
                return;
            }
            this.uploading = false;
            let thumbnail = this.safeThumbnail(data);
            if (thumbnail) {
                this.thumbnail = thumbnail;
                getRequiredElementById('chant_editor_image').value = '';
                this.updatePreview();
            }
            else {
                error.textContent = 'Could not use the current image. Try again or upload a thumbnail.';
            }
            this.updateSaveButton();
        };
        try {
            imageToData(image.src, finish, true);
        }
        catch (exception) {
            finish(null);
        }
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
