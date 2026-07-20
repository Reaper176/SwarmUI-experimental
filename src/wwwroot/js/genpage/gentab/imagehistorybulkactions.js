class ImageHistoryBulkActions {
    /** Builds bulk workflows around snapshots and controller callbacks. */
    constructor(services) {
        this.services = services;
    }

    /** Runs an asynchronous action while the controller owns the busy flag. */
    async runBusy(action) {
        if (this.services.isBusy()) {
            return null;
        }
        this.services.setBusy(true);
        try {
            return await action();
        }
        finally {
            this.services.setBusy(false);
        }
    }

    /** Hides the currently selected history images. */
    hideSelected() {
        this.setHidden(true);
    }

    /** Unhides the currently selected history images. */
    unhideSelected() {
        this.setHidden(false);
    }

    /** Deletes the currently selected history images. */
    deleteSelected() {
        this.deleteImages();
    }

    /** Stars the currently selected history images. */
    starSelected() {
        this.setStarred(true);
    }

    /** Unstars the currently selected history images. */
    unstarSelected() {
        this.setStarred(false);
    }

    /** Prompts for and applies a rating to the selected history images. */
    promptRating() {
        let value = prompt('Rating 0-5:', '5');
        if (value == null) {
            return;
        }
        let rating = Number.parseInt(value.trim());
        if (!Number.isFinite(rating) || rating < 0 || rating > 5) {
            showError('Rating must be from 0 through 5.');
            return;
        }
        this.setRating(rating);
    }

    /** Prompts for and updates tags on the selected history images. */
    promptTags(mode) {
        let value = prompt(`${mode == 'add' ? 'Add' : 'Remove'} tags, comma-separated:`, '');
        if (value == null) {
            return;
        }
        let tags = value.split(',').map(t => t.trim()).filter(t => t.length > 0);
        if (tags.length == 0) {
            showError('No tags were provided.');
            return;
        }
        this.setTags(tags.join(','), mode);
    }

    /** Prompts for and updates notes on the selected history images. */
    promptNotes() {
        let value = prompt('Set notes for selected images:', '');
        if (value == null) {
            return;
        }
        this.setNotes(value);
    }

    /** Prompts for and moves or copies the selected history images. */
    promptMove(mode) {
        let folder = prompt(`${mode == 'copy' ? 'Copy' : 'Move'} selected images to output folder:`, '');
        if (folder == null) {
            return;
        }
        folder = folder.trim();
        if (!folder) {
            showError('No output folder was provided.');
            return;
        }
        this.move(folder, mode);
    }

    /** Exports metadata for a snapshot of the selected history images. */
    exportMetadata() {
        let selected = this.services.getSelectedPaths();
        if (selected.length == 0) {
            return;
        }
        let exported = [];
        for (let fullsrc of selected) {
            let file = this.services.getFile(fullsrc);
            if (!file) {
                continue;
            }
            exported.push({
                path: fullsrc,
                name: file.data.name,
                file_size: file.data.file_size || 0,
                file_time: file.data.file_time || 0,
                file_date: file.data.file_time ? new Date(file.data.file_time * 1000).toISOString() : '',
                metadata: this.services.parseMetadata(file.data.metadata),
                raw_metadata: file.data.metadata
            });
        }
        let stamp = new Date().toISOString().replaceAll(':', '-').replaceAll('.', '-');
        downloadPlainText(`image-history-metadata-${stamp}.json`, JSON.stringify(exported, null, 2));
    }

    /** Copies a snapshot of the selected history paths. */
    copyPaths() {
        let selected = this.services.getSelectedPaths();
        if (selected.length == 0) {
            return;
        }
        copyText(selected.join('\n'));
        doNoticePopover(`Copied ${selected.length} path${selected.length == 1 ? '' : 's'}.`, 'notice-pop-green');
    }

    /** Loads one image for contact-sheet rendering. */
    loadContactSheetImage(src) {
        return new Promise(resolve => {
            let image = new Image();
            image.onload = () => resolve(image);
            image.onerror = () => resolve(null);
            image.src = src;
        });
    }

    /** Creates a contact sheet from a snapshot of selected still images. */
    async createContactSheet() {
        if (this.services.isBusy()) {
            return;
        }
        let selected = this.services.getSelectedPaths();
        if (selected.length == 0) {
            return;
        }
        let autoColumns = Math.ceil(Math.sqrt(selected.length));
        let options = prompt('Contact sheet columns, thumb size:', `${autoColumns},220`);
        if (options == null) {
            return;
        }
        let splitOptions = options.split(',').map(v => Number.parseInt(v.trim()));
        let requestedColumns = Number.isFinite(splitOptions[0]) && splitOptions[0] > 0 ? splitOptions[0] : autoColumns;
        let thumbSize = Number.isFinite(splitOptions[1]) && splitOptions[1] >= 64 ? splitOptions[1] : 220;
        thumbSize = Math.min(1024, thumbSize);
        let entries = await this.runBusy(async () => {
            let loadedEntries = [];
            for (let fullsrc of selected.slice(0, 200)) {
                let file = this.services.getFile(fullsrc);
                if (!file || getMediaType(file.data.src) != 'image') {
                    continue;
                }
                let image = await this.loadContactSheetImage(file.data.src);
                if (!image) {
                    continue;
                }
                loadedEntries.push({ image: image, label: file.data.name || file.name || fullsrc });
            }
            return loadedEntries;
        });
        if (entries == null) {
            return;
        }
        if (entries.length == 0) {
            showError('No selected still images could be loaded for a contact sheet.');
            return;
        }
        let labelHeight = 26;
        let gap = 10;
        let columns = Math.min(requestedColumns, entries.length);
        let rows = Math.ceil(entries.length / columns);
        let canvas = document.createElement('canvas');
        canvas.width = columns * (thumbSize + gap) + gap;
        canvas.height = rows * (thumbSize + labelHeight + gap) + gap;
        let ctx = canvas.getContext('2d');
        ctx.fillStyle = '#1f1f1f';
        ctx.fillRect(0, 0, canvas.width, canvas.height);
        ctx.font = '13px sans-serif';
        ctx.textBaseline = 'top';
        for (let i = 0; i < entries.length; i++) {
            let entry = entries[i];
            let col = i % columns;
            let row = Math.floor(i / columns);
            let x = gap + col * (thumbSize + gap);
            let y = gap + row * (thumbSize + labelHeight + gap);
            let scale = Math.min(thumbSize / entry.image.naturalWidth, thumbSize / entry.image.naturalHeight);
            let width = Math.max(1, Math.round(entry.image.naturalWidth * scale));
            let height = Math.max(1, Math.round(entry.image.naturalHeight * scale));
            let drawX = x + Math.floor((thumbSize - width) / 2);
            let drawY = y + Math.floor((thumbSize - height) / 2);
            ctx.fillStyle = '#111';
            ctx.fillRect(x, y, thumbSize, thumbSize);
            ctx.drawImage(entry.image, drawX, drawY, width, height);
            ctx.fillStyle = '#eee';
            ctx.fillText(entry.label, x, y + thumbSize + 6, thumbSize);
        }
        canvas.toBlob(blob => {
            let url = URL.createObjectURL(blob);
            let link = document.createElement('a');
            link.href = url;
            link.download = `image-history-contact-sheet-${new Date().toISOString().replaceAll(':', '-').replaceAll('.', '-')}.png`;
            link.click();
            URL.revokeObjectURL(url);
        }, 'image/png');
        doNoticePopover(`Created contact sheet with ${entries.length} image${entries.length == 1 ? '' : 's'}.`, 'notice-pop-green');
    }

    /** Extracts Prompt Lab prompt fields from image metadata. */
    metadataToPromptLabPrompt(file) {
        let metadata = this.services.parseMetadata(file.data.metadata);
        let params = metadata.sui_image_params || {};
        let extra = metadata.sui_extra_data || {};
        return {
            name: file.data.name || file.name || 'History Prompt',
            positive: extra.original_prompt || params.prompt || '',
            negative: extra.original_negativeprompt || params.negativeprompt || '',
            notes: `Imported from history image: ${file.data.fullsrc || file.name}`,
            tags: ['history'],
            favorite: false
        };
    }

    /** Sends prompts from a snapshot of selected history images to Prompt Lab. */
    async sendToPromptLab() {
        if (window.userFeatureToggles?.promptLab == false) {
            return;
        }
        if (this.services.isBusy()) {
            return;
        }
        let selected = this.services.getSelectedPaths();
        if (selected.length == 0) {
            return;
        }
        let counts = await this.runBusy(async () => {
            let saved = 0;
            let skipped = 0;
            let failed = 0;
            for (let fullsrc of selected) {
                let file = this.services.getFile(fullsrc);
                if (!file) {
                    skipped++;
                    continue;
                }
                let item = this.metadataToPromptLabPrompt(file);
                if (!item.positive && !item.negative) {
                    skipped++;
                    continue;
                }
                let result = await new Promise(resolve => {
                    genericRequest('PromptLabSave', { collection: 'prompts', item: item }, data => resolve(data), 0, error => resolve({ error }));
                });
                if (result.error) {
                    failed++;
                    console.log(`Failed to send history image '${fullsrc}' to Prompt Lab: ${result.error}`);
                }
                else {
                    saved++;
                }
            }
            return { saved, skipped, failed };
        });
        if (counts == null) {
            return;
        }
        if (counts.saved > 0 && window.promptLab?.load) {
            promptLab.load();
        }
        if (counts.failed > 0) {
            showError(`Sent ${counts.saved} prompt(s) to Prompt Lab. Skipped ${counts.skipped}. Failed ${counts.failed}.`);
        }
        else {
            doNoticePopover(`Sent ${counts.saved} prompt${counts.saved == 1 ? '' : 's'} to Prompt Lab${counts.skipped > 0 ? `, skipped ${counts.skipped}` : ''}.`, 'notice-pop-green');
        }
    }

    /** Sets starred state for a snapshot of selected history images. */
    async setStarred(targetStarred) {
        if (this.services.isBusy()) {
            return;
        }
        let selected = this.services.getSelectedPaths();
        if (selected.length == 0) {
            return;
        }
        let counts = await this.runBusy(async () => {
            let changed = 0;
            let failed = 0;
            for (let fullsrc of selected) {
                let file = this.services.getFile(fullsrc);
                if (!file || this.services.parseMetadata(file.data.metadata).is_starred == targetStarred) {
                    continue;
                }
                let result = await new Promise(resolve => {
                    genericRequest('ToggleImageStarred', { path: fullsrc }, data => resolve(data), 0, error => resolve({ error }));
                });
                if (result.error) {
                    failed++;
                    console.log(`Failed to ${targetStarred ? 'star' : 'unstar'} image '${fullsrc}': ${result.error}`);
                    continue;
                }
                changed++;
                file.data.metadata = this.services.setMetadataBoolValue(file.data.metadata ?? '{}', 'is_starred', result.new_state);
                this.services.updateStarredCards(file.data.src, result.new_state);
            }
            return { changed, failed };
        });
        if (counts == null) {
            return;
        }
        if (counts.changed > 0) {
            this.services.requestRefresh();
        }
        if (counts.failed > 0) {
            showError(`${targetStarred ? 'Starred' : 'Unstarred'} ${counts.changed} image(s). Failed ${counts.failed}.`);
        }
        else if (counts.changed > 0) {
            doNoticePopover(`${targetStarred ? 'Starred' : 'Unstarred'} ${counts.changed} image${counts.changed == 1 ? '' : 's'}.`, 'notice-pop-green');
        }
    }

    /** Sets a rating for a snapshot of selected history images. */
    async setRating(rating) {
        if (this.services.isBusy()) {
            return;
        }
        let selected = this.services.getSelectedPaths();
        if (selected.length == 0) {
            return;
        }
        let counts = await this.runBusy(async () => {
            let changed = 0;
            let failed = 0;
            for (let fullsrc of selected) {
                let file = this.services.getFile(fullsrc);
                if (!file) {
                    continue;
                }
                let result = await new Promise(resolve => {
                    genericRequest('SetImageRating', { path: fullsrc, rating: rating }, data => resolve(data), 0, error => resolve({ error }));
                });
                if (result.error) {
                    failed++;
                    console.log(`Failed to rate image '${fullsrc}': ${result.error}`);
                    continue;
                }
                changed++;
                file.data.metadata = this.services.setMetadataValue(file.data.metadata ?? '{}', 'rating', result.rating);
            }
            return { changed, failed };
        });
        if (counts == null) {
            return;
        }
        if (counts.changed > 0) {
            this.services.requestRefresh();
        }
        if (counts.failed > 0) {
            showError(`Rated ${counts.changed} image(s). Failed ${counts.failed}.`);
        }
        else if (counts.changed > 0) {
            doNoticePopover(`Rated ${counts.changed} image${counts.changed == 1 ? '' : 's'}.`, 'notice-pop-green');
        }
    }

    /** Updates tags for a snapshot of selected history images. */
    async setTags(tags, mode) {
        if (this.services.isBusy()) {
            return;
        }
        let selected = this.services.getSelectedPaths();
        if (selected.length == 0) {
            return;
        }
        let counts = await this.runBusy(async () => {
            let changed = 0;
            let failed = 0;
            for (let fullsrc of selected) {
                let file = this.services.getFile(fullsrc);
                if (!file) {
                    continue;
                }
                let result = await new Promise(resolve => {
                    genericRequest('SetImageTags', { path: fullsrc, tags: tags, mode: mode }, data => resolve(data), 0, error => resolve({ error }));
                });
                if (result.error) {
                    failed++;
                    console.log(`Failed to ${mode} tags for image '${fullsrc}': ${result.error}`);
                    continue;
                }
                changed++;
                file.data.metadata = this.services.setMetadataValue(file.data.metadata ?? '{}', 'tags', result.tags || []);
            }
            return { changed, failed };
        });
        if (counts == null) {
            return;
        }
        if (counts.changed > 0) {
            this.services.requestRefresh();
        }
        if (counts.failed > 0) {
            showError(`${mode == 'add' ? 'Added tags to' : 'Removed tags from'} ${counts.changed} image(s). Failed ${counts.failed}.`);
        }
        else if (counts.changed > 0) {
            doNoticePopover(`${mode == 'add' ? 'Added tags to' : 'Removed tags from'} ${counts.changed} image${counts.changed == 1 ? '' : 's'}.`, 'notice-pop-green');
        }
    }

    /** Sets notes for a snapshot of selected history images. */
    async setNotes(notes) {
        if (this.services.isBusy()) {
            return;
        }
        let selected = this.services.getSelectedPaths();
        if (selected.length == 0) {
            return;
        }
        let counts = await this.runBusy(async () => {
            let changed = 0;
            let failed = 0;
            for (let fullsrc of selected) {
                let file = this.services.getFile(fullsrc);
                if (!file) {
                    continue;
                }
                let result = await new Promise(resolve => {
                    genericRequest('SetImageNotes', { path: fullsrc, notes: notes }, data => resolve(data), 0, error => resolve({ error }));
                });
                if (result.error) {
                    failed++;
                    console.log(`Failed to set notes for image '${fullsrc}': ${result.error}`);
                    continue;
                }
                changed++;
                file.data.metadata = this.services.setMetadataValue(file.data.metadata ?? '{}', 'notes', result.notes || '');
            }
            return { changed, failed };
        });
        if (counts == null) {
            return;
        }
        if (counts.changed > 0) {
            this.services.requestRefresh();
        }
        if (counts.failed > 0) {
            showError(`Set notes on ${counts.changed} image(s). Failed ${counts.failed}.`);
        }
        else if (counts.changed > 0) {
            doNoticePopover(`Set notes on ${counts.changed} image${counts.changed == 1 ? '' : 's'}.`, 'notice-pop-green');
        }
    }

    /** Moves or copies a snapshot of selected history images. */
    async move(folder, mode) {
        if (this.services.isBusy()) {
            return;
        }
        let selected = this.services.getSelectedPaths();
        if (selected.length == 0) {
            return;
        }
        let result = await this.runBusy(async () => {
            return await new Promise(resolve => {
                genericRequest('BulkMoveImages', { paths: selected, targetFolder: folder, mode: mode }, data => resolve(data), 0, error => resolve({ error }));
            });
        });
        if (result == null) {
            return;
        }
        if (result.error) {
            showError(result.error);
            return;
        }
        if (mode == 'move') {
            this.services.clearSelection();
        }
        this.services.requestRefresh();
        doNoticePopover(`${mode == 'copy' ? 'Copied' : 'Moved'} ${result.changed || 0} image${result.changed == 1 ? '' : 's'}${result.failed ? `, failed ${result.failed}` : ''}.`, 'notice-pop-green');
    }

    /** Sets hidden state for a snapshot of selected history images. */
    async setHidden(targetHidden) {
        if (this.services.isBusy()) {
            return;
        }
        let selected = this.services.getSelectedPaths();
        if (selected.length == 0) {
            return;
        }
        let counts = await this.runBusy(async () => {
            let updated = 0;
            let skipped = 0;
            let failed = 0;
            for (let fullsrc of selected) {
                let current = this.services.getFile(fullsrc);
                let isHidden = this.services.parseMetadata(current?.data?.metadata).is_hidden === true;
                if (isHidden == targetHidden) {
                    skipped++;
                    continue;
                }
                let src = this.services.getImageSrc(fullsrc);
                let res = await this.services.toggleHidden(fullsrc, src, false, () => {});
                if (res.success) {
                    updated++;
                }
                else {
                    failed++;
                    console.log(`Failed to ${targetHidden ? 'hide' : 'unhide'} image '${fullsrc}': ${res.error}`);
                }
            }
            return { updated, skipped, failed };
        });
        if (counts == null) {
            return;
        }
        if (counts.updated > 0) {
            this.services.requestRefresh();
        }
        if (counts.failed > 0) {
            showError(`${targetHidden ? 'Hid' : 'Unhid'} ${counts.updated} image(s), skipped ${counts.skipped}, failed ${counts.failed}.`);
        }
        else if (counts.updated > 0 || counts.skipped > 0) {
            doNoticePopover(`${targetHidden ? 'Hid' : 'Unhid'} ${counts.updated} image${counts.updated == 1 ? '' : 's'}${counts.skipped > 0 ? ` (${counts.skipped} already ${targetHidden ? 'hidden' : 'visible'})` : ''}.`, 'notice-pop-green');
        }
    }

    /** Deletes a snapshot of selected history images. */
    async deleteImages() {
        if (this.services.isBusy()) {
            return;
        }
        let selected = this.services.getSelectedPaths();
        if (selected.length == 0) {
            return;
        }
        let imgWord = selected.length == 1 ? 'image' : 'images';
        if (!uiImprover.lastShift && getUserSetting('ui.checkifsurebeforedelete', true) && !confirm(`Are you sure you want to delete ${selected.length} ${imgWord}?\nHold shift to bypass.`)) {
            return;
        }
        let counts = await this.runBusy(async () => {
            let deleted = 0;
            let failed = 0;
            for (let fullsrc of selected) {
                let src = this.services.getImageSrc(fullsrc);
                let res = await this.services.deleteSingle(fullsrc, src, null, () => {});
                if (res.success) {
                    deleted++;
                }
                else {
                    failed++;
                    console.log(`Failed to delete image '${fullsrc}': ${res.error}`);
                }
            }
            return { deleted, failed };
        });
        if (counts == null) {
            return;
        }
        if (counts.deleted > 0) {
            this.services.requestRefresh();
        }
        if (counts.failed > 0) {
            showError(`Deleted ${counts.deleted} image(s). Failed to delete ${counts.failed} image(s).`);
        }
        else if (counts.deleted > 0) {
            doNoticePopover(`Deleted ${counts.deleted} image${counts.deleted == 1 ? '' : 's'}.`, 'notice-pop-green');
        }
    }
}
