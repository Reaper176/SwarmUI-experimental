class ImageHistoryComparison {
    /** Builds an image-history comparison collaborator around controller-owned services. */
    constructor(services) {
        this.services = services;
        this.files = null;
        this.pan = { x: 0, y: 0, active: false, startX: 0, startY: 0, baseX: 0, baseY: 0 };
    }

    /**
     * Ensures the image history compare modal exists.
     */
    ensureModal() {
        let modal = document.getElementById('image_history_compare_modal');
        if (modal) {
            return modal;
        }
        modal = createDiv('image_history_compare_modal', 'modal modal-fullscreen image-history-compare-modal');
        modal.tabIndex = -1;
        modal.setAttribute('role', 'dialog');
        modal.innerHTML = `
            <div class="image-history-compare-toolbar">
                <span id="image_history_compare_title" class="image-history-compare-title"></span>
                <label for="image_history_compare_zoom">Zoom</label>
                <input id="image_history_compare_zoom" class="image-history-compare-zoom" type="range" min="25" max="200" value="100">
                <label for="image_history_compare_diff"><input id="image_history_compare_diff" type="checkbox" autocomplete="off"> Diff</label>
                <label for="image_history_compare_metadata"><input id="image_history_compare_metadata" type="checkbox" autocomplete="off"> Metadata</label>
                <button type="button" class="basic-button translate" id="image_history_compare_fit">Fit</button>
                <button type="button" class="basic-button translate" id="image_history_compare_swap">Swap</button>
                <button type="button" class="basic-button translate" id="image_history_compare_reuse_a">A Settings</button>
                <button type="button" class="basic-button translate" id="image_history_compare_reuse_b">B Settings</button>
                <button type="button" class="basic-button translate" id="image_history_compare_star_a">A Star</button>
                <button type="button" class="basic-button translate" id="image_history_compare_star_b">B Star</button>
                <button type="button" class="basic-button translate" id="image_history_compare_rate_a">A Rating</button>
                <button type="button" class="basic-button translate" id="image_history_compare_rate_b">B Rating</button>
                <button type="button" class="basic-button translate" id="image_history_compare_close">Close</button>
            </div>
            <div class="image-history-compare-body">
                <div class="image-history-compare-viewport">
                    <div class="image-history-compare-stage">
                        <img id="image_history_compare_img_a" class="image-history-compare-img image-history-compare-img-base">
                        <img id="image_history_compare_img_b" class="image-history-compare-img image-history-compare-img-top">
                        <canvas id="image_history_compare_diff_canvas" class="image-history-compare-diff-canvas"></canvas>
                        <div id="image_history_compare_divider" class="image-history-compare-divider"></div>
                    </div>
                </div>
                <div id="image_history_compare_metadata_panel" class="image-history-compare-metadata-panel"></div>
            </div>`;
        document.body.appendChild(modal);
        getRequiredElementById('image_history_compare_close').onclick = () => {
            this.closeModal();
        };
        getRequiredElementById('image_history_compare_fit').onclick = () => {
            this.setZoom(100);
        };
        getRequiredElementById('image_history_compare_swap').onclick = () => {
            this.swapImages();
        };
        getRequiredElementById('image_history_compare_reuse_a').onclick = () => {
            this.reuseSettings('first');
        };
        getRequiredElementById('image_history_compare_reuse_b').onclick = () => {
            this.reuseSettings('second');
        };
        getRequiredElementById('image_history_compare_star_a').onclick = () => {
            this.starImage('first');
        };
        getRequiredElementById('image_history_compare_star_b').onclick = () => {
            this.starImage('second');
        };
        getRequiredElementById('image_history_compare_rate_a').onclick = () => {
            this.rateImage('first');
        };
        getRequiredElementById('image_history_compare_rate_b').onclick = () => {
            this.rateImage('second');
        };
        getRequiredElementById('image_history_compare_zoom').addEventListener('input', e => {
            this.setZoom(e.target.value);
        });
        getRequiredElementById('image_history_compare_diff').addEventListener('change', e => {
            this.setDiffMode(e.target.checked);
        });
        getRequiredElementById('image_history_compare_metadata').addEventListener('change', e => {
            this.setMetadataMode(e.target.checked);
        });
        let stage = modal.querySelector('.image-history-compare-stage');
        stage.addEventListener('pointermove', e => this.updateRevealFromPointer(e));
        stage.addEventListener('pointerdown', e => this.startPan(e));
        stage.addEventListener('pointerup', e => this.endPan(e));
        stage.addEventListener('pointercancel', e => this.endPan(e));
        getRequiredElementById('image_history_compare_img_a').draggable = false;
        getRequiredElementById('image_history_compare_img_b').draggable = false;
        getRequiredElementById('image_history_compare_img_a').addEventListener('dragstart', e => e.preventDefault());
        getRequiredElementById('image_history_compare_img_b').addEventListener('dragstart', e => e.preventDefault());
        return modal;
    }

    /**
     * Sends one compared image's generation settings back to the Generate tab.
     */
    reuseSettings(side) {
        if (!this.files) {
            return;
        }
        let file = this.files[side];
        if (!file?.data?.metadata) {
            showError('Selected compare image has no reusable metadata.');
            return;
        }
        this.services.selectCurrentImage(file.data.src, file.data.metadata, 'history');
        copy_current_image_params();
        this.closeModal();
    }

    /**
     * Toggles starred state for one compared image.
     */
    starImage(side) {
        if (!this.files) {
            return;
        }
        let file = this.files[side];
        if (!file?.data?.fullsrc || !file?.data?.src) {
            return;
        }
        toggleStar(file.data.fullsrc, file.data.src);
    }

    /**
     * Sets rating for one compared image.
     */
    rateImage(side) {
        if (!this.files) {
            return;
        }
        let file = this.files[side];
        if (!file?.data?.fullsrc) {
            return;
        }
        let value = prompt('Rating 0-5:', '5');
        if (value == null) {
            return;
        }
        let rating = Number.parseInt(value.trim());
        if (!Number.isFinite(rating) || rating < 0 || rating > 5) {
            showError('Rating must be from 0 through 5.');
            return;
        }
        genericRequest('SetImageRating', { path: file.data.fullsrc, rating: rating }, data => {
            file.data.metadata = this.services.setMetadataValue(file.data.metadata ?? '{}', 'rating', data.rating);
            this.services.requestRefresh();
            doNoticePopover(`Rated ${file.data.name || file.name}.`, 'notice-pop-green');
        });
    }

    /**
     * Loads the current compare pair into the overlay view.
     */
    renderPair() {
        if (!this.files) {
            return;
        }
        let first = this.files.first;
        let second = this.files.second;
        getRequiredElementById('image_history_compare_title').innerText = `${first.data.name || first.name} / ${second.data.name || second.name}`;
        getRequiredElementById('image_history_compare_img_a').src = first.data.src;
        getRequiredElementById('image_history_compare_img_b').src = second.data.src;
        if (getRequiredElementById('image_history_compare_diff').checked) {
            this.renderDiff();
        }
        if (getRequiredElementById('image_history_compare_metadata').checked) {
            this.renderMetadata();
        }
    }

    /**
     * Swaps image A and image B in the compare view.
     */
    swapImages() {
        if (!this.files) {
            return;
        }
        let oldFirst = this.files.first;
        this.files.first = this.files.second;
        this.files.second = oldFirst;
        this.renderPair();
    }

    /**
     * Returns focused generation metadata fields for comparison.
     */
    getMetadataFields(file) {
        let metadata = this.services.parseMetadata(file?.data?.metadata);
        let params = metadata.sui_image_params || {};
        let extra = metadata.sui_extra_data || {};
        let resolution = params.width && params.height ? `${params.width}x${params.height}` : '';
        let finalResolution = extra.final_width && extra.final_height ? `${extra.final_width}x${extra.final_height}` : '';
        return {
            Prompt: params.prompt || extra.original_prompt || '',
            Negative: params.negativeprompt || extra.original_negativeprompt || '',
            Model: params.model || '',
            LoRAs: this.services.valueToSearchText(params.loras),
            VAE: params.vae || '',
            Sampler: params.sampler || '',
            Scheduler: params.scheduler || '',
            Seed: params.seed || '',
            CFG: params.cfgscale || params.cfg || '',
            Steps: params.steps || '',
            Resolution: resolution,
            'Final Resolution': finalResolution,
            Rating: metadata.rating || '',
            Tags: this.services.valueToSearchText(metadata.tags || extra.tags),
            Notes: metadata.notes || extra.notes || '',
            'Prompt Lab': extra.prompt_lab_id || extra.prompt_lab_prompt_id || '',
            Wildcards: extra.prompt_lab_wildcard_values || ''
        };
    }

    /**
     * Enables or disables metadata compare mode.
     */
    setMetadataMode(enabled) {
        let modal = getRequiredElementById('image_history_compare_modal');
        modal.classList.toggle('image-history-compare-metadata-active', !!enabled);
        if (enabled) {
            this.renderMetadata();
        }
    }

    /**
     * Renders metadata differences for the two compared images.
     */
    renderMetadata() {
        let panel = getRequiredElementById('image_history_compare_metadata_panel');
        if (!this.files) {
            panel.innerHTML = '';
            return;
        }
        let firstFields = this.getMetadataFields(this.files.first);
        let secondFields = this.getMetadataFields(this.files.second);
        let html = '<table class="image-history-compare-metadata-table"><thead><tr><th>Field</th><th>A</th><th>B</th></tr></thead><tbody>';
        for (let key of Object.keys(firstFields)) {
            let firstValue = this.services.valueToSearchText(firstFields[key]);
            let secondValue = this.services.valueToSearchText(secondFields[key]);
            let className = firstValue == secondValue ? 'image-history-compare-metadata-same' : 'image-history-compare-metadata-different';
            html += `<tr class="${className}"><td>${escapeHtml(key)}</td><td>${escapeHtml(firstValue)}</td><td>${escapeHtml(secondValue)}</td></tr>`;
        }
        html += '</tbody></table>';
        panel.innerHTML = html;
    }

    /**
     * Closes the image history compare modal.
     */
    closeModal() {
        this.cleanupModal();
        this.showGenerateTabAfterClose();
    }

    /**
     * Clears any leftover compare modal state.
     */
    cleanupModal() {
        let modal = getRequiredElementById('image_history_compare_modal');
        if (window.bootstrap?.Modal) {
            bootstrap.Modal.getInstance(modal)?.dispose();
        }
        modal.classList.remove('show');
        modal.style.display = 'none';
        modal.setAttribute('aria-hidden', 'true');
        modal.removeAttribute('aria-modal');
        modal.removeAttribute('role');
        document.body.classList.remove('modal-open');
        for (let backdrop of document.querySelectorAll('.modal-backdrop')) {
            backdrop.remove();
        }
    }

    /**
     * Returns the UI to the normal Generate image view after compare closes.
     */
    showGenerateTabAfterClose() {
        let generateTab = document.getElementById('text2imagetabbutton');
        if (generateTab && window.bootstrap?.Tab) {
            bootstrap.Tab.getOrCreateInstance(generateTab).show();
        }
        let imageTab = document.querySelector('[href="#Image-Result-Tab"]');
        if (imageTab && window.bootstrap?.Tab) {
            bootstrap.Tab.getOrCreateInstance(imageTab).show();
        }
    }

    /**
     * Shows the image history compare modal.
     */
    openModal() {
        let modal = getRequiredElementById('image_history_compare_modal');
        this.cleanupModal();
        modal.classList.add('show');
        modal.style.display = 'block';
        modal.setAttribute('aria-modal', 'true');
        modal.setAttribute('role', 'dialog');
        modal.removeAttribute('aria-hidden');
        document.body.classList.add('modal-open');
    }

    /**
     * Updates reveal from mouse or pointer position over the image.
     */
    updateRevealFromPointer(e) {
        if (this.pan.active) {
            this.pan.x = this.pan.baseX + e.clientX - this.pan.startX;
            this.pan.y = this.pan.baseY + e.clientY - this.pan.startY;
            this.applyPan();
            return;
        }
        let base = getRequiredElementById('image_history_compare_img_a');
        let rect = base.getBoundingClientRect();
        if (rect.width <= 0) {
            return;
        }
        let reveal = ((e.clientX - rect.left) / rect.width) * 100;
        this.setReveal(reveal);
    }

    /**
     * Starts image panning from the pointer position.
     */
    startPan(e) {
        this.updateRevealFromPointer(e);
        if (e.button != 0) {
            return;
        }
        this.pan.active = true;
        this.pan.startX = e.clientX;
        this.pan.startY = e.clientY;
        this.pan.baseX = this.pan.x;
        this.pan.baseY = this.pan.y;
        e.currentTarget.setPointerCapture(e.pointerId);
    }

    /**
     * Ends image panning.
     */
    endPan(e) {
        this.pan.active = false;
        if (e.currentTarget.hasPointerCapture(e.pointerId)) {
            e.currentTarget.releasePointerCapture(e.pointerId);
        }
    }

    /**
     * Applies the current image pan offset.
     */
    applyPan() {
        let stage = document.querySelector('#image_history_compare_modal .image-history-compare-stage');
        if (stage) {
            stage.style.transform = `translate(${this.pan.x}px, ${this.pan.y}px)`;
        }
    }

    /**
     * Sets the overlay reveal split.
     */
    setReveal(value) {
        let reveal = Math.max(0, Math.min(100, parseFloat(value) || 0));
        let base = getRequiredElementById('image_history_compare_img_a');
        let divider = getRequiredElementById('image_history_compare_divider');
        let width = base.clientWidth || 0;
        let height = base.clientHeight || 0;
        getRequiredElementById('image_history_compare_img_b').style.clipPath = `inset(0 ${100 - reveal}% 0 0)`;
        getRequiredElementById('image_history_compare_diff_canvas').style.clipPath = `inset(0 ${100 - reveal}% 0 0)`;
        divider.style.left = `${width * reveal / 100}px`;
        divider.style.height = `${height}px`;
    }

    /**
     * Enables or disables highlighted pixel diff mode.
     */
    setDiffMode(enabled) {
        let modal = getRequiredElementById('image_history_compare_modal');
        modal.classList.toggle('image-history-compare-diff-active', !!enabled);
        if (enabled) {
            this.renderDiff();
        }
    }

    /**
     * Renders a red-pink diff overlay from image A to image B.
     */
    renderDiff() {
        let imgA = getRequiredElementById('image_history_compare_img_a');
        let imgB = getRequiredElementById('image_history_compare_img_b');
        if (!imgA.complete || !imgB.complete || imgA.naturalWidth == 0 || imgB.naturalWidth == 0) {
            setTimeout(() => this.renderDiff(), 80);
            return;
        }
        let width = imgA.naturalWidth;
        let height = imgA.naturalHeight;
        let canvasA = document.createElement('canvas');
        let canvasB = document.createElement('canvas');
        let diffCanvas = getRequiredElementById('image_history_compare_diff_canvas');
        canvasA.width = width;
        canvasA.height = height;
        canvasB.width = width;
        canvasB.height = height;
        diffCanvas.width = width;
        diffCanvas.height = height;
        let ctxA = canvasA.getContext('2d');
        let ctxB = canvasB.getContext('2d');
        let diffCtx = diffCanvas.getContext('2d');
        ctxA.drawImage(imgA, 0, 0, width, height);
        ctxB.drawImage(imgB, 0, 0, width, height);
        let dataA = ctxA.getImageData(0, 0, width, height);
        let dataB = ctxB.getImageData(0, 0, width, height);
        let diffData = diffCtx.createImageData(width, height);
        for (let i = 0; i < dataA.data.length; i += 4) {
            let delta = Math.abs(dataB.data[i] - dataA.data[i]) + Math.abs(dataB.data[i + 1] - dataA.data[i + 1]) + Math.abs(dataB.data[i + 2] - dataA.data[i + 2]);
            if (delta > 30) {
                diffData.data[i] = 255;
                diffData.data[i + 1] = 45;
                diffData.data[i + 2] = 120;
                diffData.data[i + 3] = Math.min(230, 80 + delta / 2);
            }
        }
        diffCtx.putImageData(diffData, 0, 0);
    }

    /**
     * Sets both compare images to the same zoom.
     */
    setZoom(value) {
        let zoom = Math.max(25, Math.min(200, parseInt(value) || 100));
        getRequiredElementById('image_history_compare_zoom').value = zoom;
        for (let id of ['image_history_compare_img_a', 'image_history_compare_img_b', 'image_history_compare_diff_canvas']) {
            let img = getRequiredElementById(id);
            img.style.width = `${zoom}%`;
            img.style.maxWidth = zoom <= 100 ? '100%' : 'none';
        }
        if (zoom <= 100) {
            this.pan.x = 0;
            this.pan.y = 0;
            this.applyPan();
        }
    }

    /**
     * Opens the side-by-side compare viewer for two selected history images.
     */
    show(paths) {
        if (paths.length != 2) {
            showError('Select exactly two images to compare.');
            return;
        }
        let first = this.services.getFile(paths[0]);
        let second = this.services.getFile(paths[1]);
        if (!first || !second) {
            showError('Selected images are not loaded in history.');
            return;
        }
        this.ensureModal();
        this.files = { first, second };
        let diffDefault = window.userFeatureToggles?.imageHistoryCompareDiffDefault == true;
        let metadataDefault = window.userFeatureToggles?.imageHistoryCompareMetadataDefault == true;
        getRequiredElementById('image_history_compare_diff').checked = diffDefault;
        getRequiredElementById('image_history_compare_metadata').checked = metadataDefault;
        this.renderPair();
        this.setDiffMode(diffDefault);
        this.setMetadataMode(metadataDefault);
        this.setZoom(100);
        this.pan = { x: 0, y: 0, active: false, startX: 0, startY: 0, baseX: 0, baseY: 0 };
        this.applyPan();
        this.setReveal(50);
        this.openModal();
    }
}
