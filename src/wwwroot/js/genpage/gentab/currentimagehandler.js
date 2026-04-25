let swarmImageCardRegistry = {
    byCanonicalSrc: new Map(),
    cardToKeys: new Map(),
    currentCards: new Set(),

    canonicalize(src) {
        if (!src) {
            return null;
        }
        try {
            if (typeof getImageFullSrc == 'function') {
                let canonical = getImageFullSrc(src);
                if (canonical) {
                    return canonical;
                }
            }
        }
        catch (err) {
            // Fallback to the raw key when canonicalization is unavailable.
        }
        return src;
    },

    keysForCard(card) {
        let keys = new Set();
        let addKey = (value) => {
            let key = this.canonicalize(value);
            if (key) {
                keys.add(key);
            }
        };
        addKey(card?.dataset?.src);
        addKey(card?.dataset?.name);
        return keys;
    },

    unregister(card) {
        let keys = this.cardToKeys.get(card);
        if (keys) {
            for (let key of keys) {
                let cards = this.byCanonicalSrc.get(key);
                if (!cards) {
                    continue;
                }
                cards.delete(card);
                if (cards.size == 0) {
                    this.byCanonicalSrc.delete(key);
                }
            }
        }
        this.cardToKeys.delete(card);
        this.currentCards.delete(card);
    },

    register(card) {
        this.unregister(card);
        if (!card || !card.isConnected) {
            return;
        }
        let keys = this.keysForCard(card);
        this.cardToKeys.set(card, keys);
        for (let key of keys) {
            let cards = this.byCanonicalSrc.get(key);
            if (!cards) {
                cards = new Set();
                this.byCanonicalSrc.set(key, cards);
            }
            cards.add(card);
        }
    },

    reindex(card) {
        this.register(card);
    },

    pruneDisconnected() {
        for (let [card] of this.cardToKeys) {
            if (!card || !card.isConnected) {
                this.unregister(card);
            }
        }
        for (let [key, cards] of this.byCanonicalSrc) {
            if (!cards || cards.size == 0) {
                this.byCanonicalSrc.delete(key);
                continue;
            }
            for (let card of [...cards]) {
                if (!card || !card.isConnected) {
                    cards.delete(card);
                }
            }
            if (cards.size == 0) {
                this.byCanonicalSrc.delete(key);
            }
        }
        for (let card of [...this.currentCards]) {
            if (!card || !card.isConnected) {
                this.currentCards.delete(card);
            }
        }
    },

    forSource(src, callback) {
        this.pruneDisconnected();
        let key = this.canonicalize(src);
        if (!key) {
            return;
        }
        let cards = this.byCanonicalSrc.get(key);
        if (!cards || cards.size == 0) {
            return;
        }
        for (let card of [...cards]) {
            if (!card.isConnected) {
                this.unregister(card);
                continue;
            }
            callback(card);
        }
    },

    setCurrentSource(src) {
        for (let card of this.currentCards) {
            if (card.setCurrent) {
                card.setCurrent(false);
            }
            else {
                card.classList.remove('image-block-current');
            }
        }
        this.currentCards.clear();
        if (!src) {
            return;
        }
        this.forSource(src, card => {
            if (card.classList.contains('image-block-placeholder')) {
                return;
            }
            if (card.setCurrent) {
                card.setCurrent(true);
            }
            else {
                card.classList.add('image-block-current');
            }
            this.currentCards.add(card);
        });
    }
};

class SwarmImageCard extends HTMLElement {
    static get observedAttributes() {
        return ['data-src', 'data-name'];
    }

    connectedCallback() {
        swarmImageCardRegistry.register(this);
    }

    disconnectedCallback() {
        swarmImageCardRegistry.unregister(this);
    }

    attributeChangedCallback(name, oldValue, newValue) {
        if (oldValue != newValue) {
            swarmImageCardRegistry.reindex(this);
        }
    }

    getMetadataObject() {
        if (!this.dataset.metadata) {
            return {};
        }
        let metadata = this.dataset.metadata;
        try {
            metadata = interpretMetadata(metadata);
        }
        catch (err) {
            // Ignore metadata parse errors and fallback to raw JSON parse.
        }
        try {
            return JSON.parse(metadata) || {};
        }
        catch (err) {
            return {};
        }
    }

    setMetadataObject(metadata) {
        this.dataset.metadata = JSON.stringify(metadata || {});
    }

    setMetadataFlag(key, enabled) {
        let metadata = this.getMetadataObject();
        metadata[key] = !!enabled;
        this.setMetadataObject(metadata);
        return metadata;
    }

    setStarred(enabled) {
        this.setMetadataFlag('is_starred', enabled);
        this.classList.toggle('image-block-starred', !!enabled);
    }

    setHidden(enabled) {
        this.setMetadataFlag('is_hidden', enabled);
        this.classList.toggle('image-block-hidden', !!enabled);
    }

    setCurrent(enabled) {
        this.classList.toggle('image-block-current', !!enabled);
    }
}

if (window.customElements && !customElements.get('swarm-image-card')) {
    customElements.define('swarm-image-card', SwarmImageCard);
}

function forEachSwarmImageCardForSrc(src, callback) {
    swarmImageCardRegistry.forSource(src, callback);
}

/** Central helper class to handle the 'image full view' modal. */
class ImageFullViewHelper {
    constructor() {
        this.zoomRate = 1.1;
        this.modal = getRequiredElementById('image_fullview_modal');
        this.content = getRequiredElementById('image_fullview_modal_content');
        this.modalJq = $('#image_fullview_modal');
        this.noClose = false;
        document.addEventListener('click', (e) => {
            if (e.target.tagName == 'BODY') {
                return; // it's impossible on the genpage to actually click body, so this indicates a bugged click, so ignore it
            }
            if (!this.noClose && this.modal.style.display == 'block' && !findParentOfClass(e.target, 'imageview_popup_modal_undertext') && !findParentOfClass(e.target, 'video-controls') && !findParentOfClass(e.target, 'audio-controls') && !findParentOfClass(e.target, 'audio-waveform-wrap') && !findParentOfClass(e.target, 'image_fullview_extra_buttons')) {
                this.close();
                e.preventDefault();
                e.stopPropagation();
                return false;
            }
            this.noClose = false;
        }, true);
        this.modalJq.on('hidden.bs.modal', () => {
            this.close();
        });
        this.lastMouseX = 0;
        this.lastMouseY = 0;
        this.isDragging = false;
        this.didDrag = false;
        this.content.addEventListener('wheel', this.onWheel.bind(this));
        this.content.addEventListener('mousedown', this.onMouseDown.bind(this));
        document.addEventListener('mouseup', this.onGlobalMouseUp.bind(this));
        document.addEventListener('mousemove', this.onGlobalMouseMove.bind(this));
        this.fixButtonDelay = null;
        this.lastClosed = 0;
        this.showMetadata = true;
        this.didPasteState = false;
        this.pendingDragX = 0;
        this.pendingDragY = 0;
        this.dragMoveRaf = null;
        this.pendingWheelSteps = 0;
        this.pendingWheelMouseX = 0;
        this.pendingWheelMouseY = 0;
        this.wheelRaf = null;
        this.imageWrap = null;
        this.buttonsWrap = null;
        this.metadataWrap = null;
    }

    ensureScaffold() {
        if (this.imageWrap?.isConnected && this.buttonsWrap?.isConnected && this.metadataWrap?.isConnected) {
            return;
        }
        this.content.innerHTML = `
        <div class="modal-dialog" style="display:none">(click outside image to close)</div>
        <div class="imageview_modal_inner_div">
            <div class="imageview_modal_imagewrap" id="imageview_modal_imagewrap" style="text-align:center;"></div>
            <div class="imageview_popup_modal_undertext">
                <div class="image_fullview_extra_buttons"></div>
                <div class="image_fullview_metadata"></div>
            </div>
        </div>`;
        this.imageWrap = this.content.querySelector('#imageview_modal_imagewrap');
        this.buttonsWrap = this.content.querySelector('.image_fullview_extra_buttons');
        this.metadataWrap = this.content.querySelector('.image_fullview_metadata');
    }

    createMediaElement(src, isVideo, isAudio) {
        let encodedSrc = escapeHtmlForUrl(src);
        if (isVideo) {
            let container = document.createElement('div');
            container.className = 'video-container imageview_popup_modal_img';
            container.id = 'imageview_popup_modal_img';
            let video = document.createElement('video');
            video.className = 'imageview_popup_modal_img';
            video.style.cursor = 'grab';
            video.style.maxWidth = '100%';
            video.style.objectFit = 'contain';
            video.autoplay = true;
            video.loop = true;
            video.muted = true;
            video.addEventListener('loadeddata', () => this.onImgLoad(), { once: true });
            let source = document.createElement('source');
            source.src = encodedSrc;
            source.type = isVideo;
            video.appendChild(source);
            container.appendChild(video);
            return container;
        }
        if (isAudio) {
            let container = document.createElement('div');
            container.className = 'audio-container imageview_popup_modal_img';
            container.id = 'imageview_popup_modal_img';
            container.style.cursor = 'grab';
            container.style.maxWidth = '100%';
            let audio = document.createElement('audio');
            audio.className = 'imageview_popup_modal_img';
            audio.preload = 'metadata';
            audio.src = encodedSrc;
            audio.addEventListener('loadedmetadata', () => this.onImgLoad(), { once: true });
            container.appendChild(audio);
            return container;
        }
        let img = document.createElement('img');
        img.className = 'imageview_popup_modal_img';
        img.id = 'imageview_popup_modal_img';
        img.style.cursor = 'grab';
        img.style.maxWidth = '100%';
        img.style.objectFit = 'contain';
        img.src = encodedSrc;
        img.addEventListener('load', () => this.onImgLoad(), { once: true });
        return img;
    }

    getImgOrContainer() {
        return this.content.querySelector('#imageview_popup_modal_img');
    }

    getImg() {
        let container = this.getImgOrContainer();
        if (!container) {
            return null;
        }
        if (container.classList.contains('video-container')) {
            return container.querySelector('video');
        }
        if (container.classList.contains('audio-container')) {
            return container.querySelector('audio');
        }
        return container;
    }

    getHeightPercent() {
        return parseFloat((this.getImgOrContainer().style.height || '100%').replaceAll('%', ''));
    }

    getImgLeft() {
        return parseFloat((this.getImgOrContainer().style.left || '0').replaceAll('px', ''));
    }

    getImgTop() {
        return parseFloat((this.getImgOrContainer().style.top || '0').replaceAll('px', ''));
    }

    getFitGeometry() {
        let wrap = this.imageWrap;
        if (!wrap || !wrap.isConnected) {
            wrap = this.content.querySelector('#imageview_modal_imagewrap');
            this.imageWrap = wrap;
        }
        let img = this.getImg();
        if (!wrap || !img) {
            return null;
        }
        let intrinsicWidth = img.naturalWidth ?? img.videoWidth ?? 0;
        let intrinsicHeight = img.naturalHeight ?? img.videoHeight ?? 0;
        if (intrinsicWidth <= 0 || intrinsicHeight <= 0 || wrap.offsetWidth <= 0 || wrap.offsetHeight <= 0) {
            return null;
        }
        let imgAspectRatio = intrinsicWidth / intrinsicHeight;
        let fittedWidth = wrap.offsetHeight * imgAspectRatio;
        let fittedHeight = wrap.offsetHeight;
        let fittedLeft = 0;
        let fittedTop = 0;
        if (fittedWidth > wrap.offsetWidth) {
            fittedWidth = wrap.offsetWidth;
            fittedHeight = wrap.offsetWidth / imgAspectRatio;
            fittedTop = (wrap.offsetHeight - fittedHeight) / 2;
        }
        else {
            fittedLeft = (wrap.offsetWidth - fittedWidth) / 2;
        }
        return {
            wrap,
            intrinsicWidth,
            intrinsicHeight,
            fittedWidth,
            fittedHeight,
            fittedLeft,
            fittedTop,
            fittedHeightPercent: (fittedHeight / wrap.offsetHeight) * 100
        };
    }

    getCurrentRect() {
        let geometry = this.getFitGeometry();
        if (!geometry) {
            return null;
        }
        let currentHeightPercent = this.getHeightPercent();
        let scale = currentHeightPercent / geometry.fittedHeightPercent;
        return {
            geometry,
            left: this.getImgLeft(),
            top: this.getImgTop(),
            width: geometry.fittedWidth * scale,
            height: geometry.fittedHeight * scale,
            heightPercent: currentHeightPercent
        };
    }

    onMouseDown(e) {
        if (this.modal.style.display != 'block') {
            return;
        }
        if (e.button == 2) { // right-click
            return;
        }
        if (!findParentOfClass(e.target, 'imageview_modal_imagewrap') || findParentOfClass(e.target, 'video-controls') || findParentOfClass(e.target, 'audio-controls') || findParentOfClass(e.target, 'audio-waveform-wrap') || e.ctrlKey || e.shiftKey) {
            return;
        }
        this.lastMouseX = e.clientX;
        this.lastMouseY = e.clientY;
        this.isDragging = true;
        this.getImgOrContainer().style.cursor = 'grabbing';
        e.preventDefault();
        e.stopPropagation();
    }

    onGlobalMouseUp(e) {
        if (!this.isDragging) {
            return;
        }
        this.flushPendingDragMove();
        this.getImgOrContainer().style.cursor = 'grab';
        this.isDragging = false;
        this.noClose = this.didDrag;
        this.didDrag = false;
    }

    moveImg(xDiff, yDiff) {
        let img = this.getImgOrContainer();
        let geometry = this.getFitGeometry();
        if (!geometry) {
            return;
        }
        let width = img.offsetWidth;
        let height = img.offsetHeight;
        let targetLeft = this.getImgLeft() + xDiff;
        let targetTop = this.getImgTop() + yDiff;
        let clamped = this.clampPosition(targetLeft, targetTop, width, height, geometry.wrap);
        img.style.left = `${clamped.left}px`;
        img.style.top = `${clamped.top}px`;
    }

    flushPendingDragMove() {
        if (this.pendingDragX == 0 && this.pendingDragY == 0) {
            return;
        }
        this.detachImg();
        this.moveImg(this.pendingDragX, this.pendingDragY);
        this.pendingDragX = 0;
        this.pendingDragY = 0;
    }

    onGlobalMouseMove(e) {
        if (!this.isDragging) {
            return;
        }
        let xDiff = e.clientX - this.lastMouseX;
        let yDiff = e.clientY - this.lastMouseY;
        this.lastMouseX = e.clientX;
        this.lastMouseY = e.clientY;
        this.pendingDragX += xDiff;
        this.pendingDragY += yDiff;
        if (!this.dragMoveRaf) {
            this.dragMoveRaf = requestAnimationFrame(() => {
                this.dragMoveRaf = null;
                this.flushPendingDragMove();
            });
        }
        if (Math.abs(xDiff) > 1 || Math.abs(yDiff) > 1) {
            this.didDrag = true;
        }
    }

    detachImg() {
        let geometry = this.getFitGeometry();
        if (!geometry) {
            return;
        }
        if (geometry.wrap.style.textAlign == 'center') {
            let img = this.getImgOrContainer();
            img.style.position = 'absolute';
            img.style.left = `${geometry.fittedLeft}px`;
            img.style.top = `${geometry.fittedTop}px`;
            img.style.height = `${geometry.fittedHeightPercent}%`;
            img.style.objectFit = '';
            img.style.maxWidth = '';
            geometry.wrap.style.textAlign = 'left';
        }
    }

    applyImageRendering(targetHeightPercent, maxHeight, img) {
        if (targetHeightPercent > maxHeight / 5) {
            img.style.imageRendering = 'pixelated';
        }
        else {
            img.style.imageRendering = '';
        }
    }

    /** Clamp media position within the current wrap bounds using rendered dimensions. */
    clampPosition(left, top, width, height, wrap) {
        let overWidth = wrap.offsetWidth / 2;
        let overHeight = wrap.offsetHeight / 2;
        return {
            left: Math.min(overWidth, Math.max(left, wrap.offsetWidth - width - overWidth)),
            top: Math.min(overHeight, Math.max(top, wrap.offsetHeight - height - overHeight))
        };
    }

    /** Keep pointer-relative anchor math inside the rendered media even when the pointer is in empty margins. */
    clampMediaRatio(value) {
        return Math.min(1, Math.max(0, value));
    }

    shouldShowMetadata(targetHeightPercent, fitPercent) {
        return targetHeightPercent <= fitPercent;
    }

    /** Blend anchored zoom placement back toward the exact fit-center position only while zooming out. */
    resolveZoomPosition(targetLeft, targetTop, targetWidth, targetHeight, geometry, targetHeightPercent, isZoomingOut) {
        let fitPercent = geometry.fittedHeightPercent;
        let fitLeft = geometry.fittedLeft;
        let fitTop = geometry.fittedTop;
        if (targetHeightPercent <= fitPercent) {
            return {
                left: fitLeft,
                top: fitTop,
                heightPercent: fitPercent
            };
        }
        let clamped = this.clampPosition(targetLeft, targetTop, targetWidth, targetHeight, geometry.wrap);
        if (!isZoomingOut) {
            return {
                left: clamped.left,
                top: clamped.top,
                heightPercent: targetHeightPercent
            };
        }
        let blendRange = Math.max(5, fitPercent * 0.35);
        let blendAmount = Math.min(1, Math.max(0, (targetHeightPercent - fitPercent) / blendRange));
        return {
            left: fitLeft + (clamped.left - fitLeft) * blendAmount,
            top: fitTop + (clamped.top - fitTop) * blendAmount,
            heightPercent: targetHeightPercent
        };
    }

    copyState() {
        let img = this.getImgOrContainer();
        if (img.style.objectFit) {
            return {};
        }
        return {
            left: this.getImgLeft(),
            top: this.getImgTop(),
            height: this.getHeightPercent(),
            showMetadata: this.showMetadata
        };
    }

    pasteState(state) {
        if (!state || state.left == undefined || state.top == undefined || state.height == undefined) {
            return;
        }
        let img = this.getImgOrContainer();
        this.detachImg();
        img.style.left = `${state.left}px`;
        img.style.top = `${state.top}px`;
        img.style.height = `${state.height}%`;
        this.toggleMetadataVisibility(state.showMetadata);
        this.didPasteState = true;
    }

    applyWheelZoom(stepDelta, clientX, clientY) {
        this.detachImg();
        let rect = this.getCurrentRect();
        if (!rect) {
            return;
        }
        let img = this.getImg();
        let container = this.getImgOrContainer();
        if (!container) {
            return;
        }
        let zoom = Math.pow(this.zoomRate, stepDelta);
        let maxHeight = Math.sqrt(rect.geometry.intrinsicWidth * rect.geometry.intrinsicHeight) * 2;
        let targetHeightPercent = Math.max(10, Math.min(rect.heightPercent * zoom, maxHeight));
        let wrapRect = rect.geometry.wrap.getBoundingClientRect();
        let pointerX = clientX - wrapRect.left;
        let pointerY = clientY - wrapRect.top;
        let mediaX = this.clampMediaRatio((pointerX - rect.left) / rect.width);
        let mediaY = this.clampMediaRatio((pointerY - rect.top) / rect.height);
        let scale = targetHeightPercent / rect.geometry.fittedHeightPercent;
        let targetWidth = rect.geometry.fittedWidth * scale;
        let targetHeight = rect.geometry.fittedHeight * scale;
        let targetLeft = pointerX - mediaX * targetWidth;
        let targetTop = pointerY - mediaY * targetHeight;
        let resolved = this.resolveZoomPosition(targetLeft, targetTop, targetWidth, targetHeight, rect.geometry, targetHeightPercent, targetHeightPercent < rect.heightPercent);
        container.style.height = `${resolved.heightPercent}%`;
        container.style.left = `${resolved.left}px`;
        container.style.top = `${resolved.top}px`;
        this.applyImageRendering(resolved.heightPercent, maxHeight, img);
        this.toggleMetadataVisibility(this.shouldShowMetadata(resolved.heightPercent, rect.geometry.fittedHeightPercent));
        container.style.cursor = 'grab';
    }

    onWheel(e) {
        if (!findParentOfClass(e.target, 'imageview_modal_imagewrap') || e.ctrlKey || e.shiftKey) {
            return;
        }
        e.preventDefault();
        e.stopPropagation();
        this.pendingWheelSteps += -e.deltaY / 100;
        this.pendingWheelMouseX = e.clientX;
        this.pendingWheelMouseY = e.clientY;
        if (this.wheelRaf) {
            return;
        }
        this.wheelRaf = requestAnimationFrame(() => {
            this.wheelRaf = null;
            let stepDelta = this.pendingWheelSteps;
            let mouseX = this.pendingWheelMouseX;
            let mouseY = this.pendingWheelMouseY;
            this.pendingWheelSteps = 0;
            this.applyWheelZoom(stepDelta, mouseX, mouseY);
        });
    }

    toggleMetadataVisibility(showMetadata) {
        this.showMetadata = showMetadata;
        this.ensureScaffold();
        let undertext = this.metadataWrap?.parentElement;
        let imagewrap = this.imageWrap;
        if (!undertext || !imagewrap) {
            return;
        }
        if (showMetadata) {
            undertext.classList.remove('minimized-mode');
            imagewrap.classList.remove('expanded-mode');
        }
        else {
            undertext.classList.add('minimized-mode');
            imagewrap.classList.add('expanded-mode');
        }
    }

    /** Format fixes that need to run after the image content has loaded. */
    onImgLoad() {
        if (this.didPasteState) {
            return;
        }
        if (internalSiteJsGetUserSetting('ui.defaulthidemetadatainfullview', false)) {
            let img = this.getImg();
            let width = img.naturalWidth ?? img.videoWidth;
            let height = img.naturalHeight ?? img.videoHeight;
            let aspectRatio = width / height;
            let screenAspectRatio = window.innerWidth / window.innerHeight;
            if (aspectRatio <= screenAspectRatio) {
                this.toggleMetadataVisibility(false);
            }
            else {
                this.toggleMetadataVisibility(true);
            }
        }
    }

    showImage(src, metadata, batchId = null) {
        this.didPasteState = false;
        this.currentSrc = src;
        this.currentMetadata = metadata;
        this.currentBatchId = batchId;
        this.updateCounter();
        let wasAlreadyOpen = this.isOpen();
        let isVideo = isVideoExt(src);
        let isAudio = isAudioExt(src);
        this.ensureScaffold();
        let mediaElem = this.createMediaElement(src, isVideo, isAudio);
        this.imageWrap.replaceChildren(mediaElem);
        this.buttonsWrap.replaceChildren();
        this.metadataWrap.innerHTML = formatMetadata(metadata);
        let subDiv = this.buttonsWrap;
        for (let added of buttonsForImage(getImageFullSrc(src), src, metadata)) {
            if (added.href) {
                if (added.is_download) {
                    subDiv.appendChild(createDiv(null, 'inline-block', `<a class="text_button basic-button translate" href="${added.href}" title="${added.title}" download>${added.label}</a>`));
                }
                else {
                    subDiv.appendChild(createDiv(null, 'inline-block', `<a class="text_button basic-button translate" href="${added.href}" title="${added.title}">${added.label}</a>`));
                }
            }
            else {
                quickAppendButton(subDiv, added.label, (e, button) => added.onclick(button), added.className || '', added.title);
            }
        }
        if (internalSiteJsGetUserSetting('ui.defaulthidemetadatainfullview', false)) {
            this.getImgOrContainer().style.height = '100.2%';
            this.toggleMetadataVisibility(false);
        }
        else {
            this.toggleMetadataVisibility(true);
        }
        this.modalJq.modal('show');
        if (isVideo) {
            new VideoControls(this.getImg());
        }
        else if (isAudio) {
            new AudioControls(this.getImg());
        }
        if (isVideo || isAudio) {
            let curImgElem = currentImageHelper.getCurrentImage();
            if (curImgElem) {
                if (curImgElem.tagName == 'VIDEO' || curImgElem.tagName == 'AUDIO') {
                    curImgElem.pause();
                }
            }
        }
        if (this.fixButtonDelay) {
            clearTimeout(this.fixButtonDelay);
        }
        if (Date.now() - this.lastClosed > 200 && !wasAlreadyOpen) {
            subDiv.style.pointerEvents = 'none';
            for (let button of subDiv.getElementsByTagName('button')) {
                button.disabled = true;
                button.classList.add('simpler-button-disable');
            }
            this.fixButtonDelay = setTimeout(() => {
                if (subDiv && subDiv.parentElement) {
                    subDiv.style.pointerEvents = 'auto';
                    for (let button of subDiv.getElementsByTagName('button')) {
                        button.disabled = false;
                    }
                }
                this.fixButtonDelay = null;
            }, 500);
        }
    }

    close() {
        if (this.isOpen()) {
            this.modalJq.modal('hide');
            this.lastClosed = Date.now();
        }
        this.isDragging = false;
        this.didDrag = false;
        this.pendingDragX = 0;
        this.pendingDragY = 0;
        this.pendingWheelSteps = 0;
        if (this.dragMoveRaf) {
            cancelAnimationFrame(this.dragMoveRaf);
            this.dragMoveRaf = null;
        }
        if (this.wheelRaf) {
            cancelAnimationFrame(this.wheelRaf);
            this.wheelRaf = null;
        }
        let media = this.getImg();
        if (media && (media.tagName == 'VIDEO' || media.tagName == 'AUDIO')) {
            media.pause();
        }
    }

    isOpen() {
        return this.modalJq.is(':visible');
    }

    updateCounter() {
        let counterElem = getRequiredElementById('image_fullview_modal_counter');
        if (!this.currentSrc) {
            counterElem.textContent = ``;
            return;
        }
        let items = [];
        let index = -1;
        if (this.currentBatchId == 'history' && lastHistoryImageDiv && lastHistoryImageDiv.parentElement) {
            items = [...lastHistoryImageDiv.parentElement.children].filter(div => div.classList.contains('image-block'));
            index = items.findIndex(div => div == lastHistoryImageDiv);
        }
        else {
            let currentImageBatchDiv = getRequiredElementById('current_image_batch');
            items = [...currentImageBatchDiv.getElementsByClassName('image-block')].filter(block => !block.classList.contains('image-block-placeholder'));
            index = items.findIndex(block => block.dataset.src == this.currentSrc);
        }
        if (index != -1 && items.length > 0) {
            counterElem.textContent = `${index + 1}/${items.length} `;
        }
        else {
            counterElem.textContent = `1/${items.length} `;
        }
    }
}

let imageFullView = new ImageFullViewHelper();

class CurrentImageHelper {

    getCurrentImage() {
        return document.getElementById('current_image_img');
    }

    getCurrentImageContainer() {
        let img = this.getCurrentImage();
        if (!img) {
            return null;
        }
        if (img.tagName == 'VIDEO' && img.parentElement.classList.contains('video-container')) {
            return img.parentElement;
        }
        if (img.tagName == 'AUDIO' && img.parentElement.classList.contains('audio-container')) {
            return img.parentElement;
        }
        return img;
    }
}

currentImageHelper = new CurrentImageHelper();

/** Called when the user clicks the clear batch button. */
function clearBatch() {
    let currentImageBatchDiv = getRequiredElementById('current_image_batch');
    currentImageBatchDiv.innerHTML = '';
    currentImageBatchDiv.dataset.numImages = 0;
}

/** Reference to the auto-clear-batch toggle checkbox. */
let autoClearBatchElem = getRequiredElementById('auto_clear_batch_checkbox');
autoClearBatchElem.checked = localStorage.getItem('autoClearBatch') == 'true';
/** Called when the user changes auto-clear-batch toggle to update local storage. */
function toggleAutoClearBatch() {
    localStorage.setItem('autoClearBatch', `${autoClearBatchElem.checked}`);
}

/** Reference to the auto-load-previews toggle checkbox. */
let autoLoadPreviewsElem = getRequiredElementById('auto_load_previews_checkbox');
autoLoadPreviewsElem.checked = localStorage.getItem('autoLoadPreviews') == 'true';
/** Called when the user changes auto-load-previews toggle to update local storage. */
function toggleAutoLoadPreviews() {
    localStorage.setItem('autoLoadPreviews', `${autoLoadPreviewsElem.checked}`);
}

/** Reference to the auto-load-images toggle checkbox. */
let autoLoadImagesElem = getRequiredElementById('auto_load_images_checkbox');
autoLoadImagesElem.checked = localStorage.getItem('autoLoadImages') != 'false';
/** Called when the user changes auto-load-images toggle to update local storage. */
function toggleAutoLoadImages() {
    localStorage.setItem('autoLoadImages', `${autoLoadImagesElem.checked}`);
}

/** Reference to the auto-clear-batch toggle checkbox. */
let showLoadSpinnersElem = getRequiredElementById('show_load_spinners_checkbox');
showLoadSpinnersElem.checked = localStorage.getItem('showLoadSpinners') != 'false';
/** Called when the user changes show-load-spinners toggle to update local storage. */
function toggleShowLoadSpinners() {
    localStorage.setItem('showLoadSpinners', `${showLoadSpinnersElem.checked}`);
}

/** Reference to the separate-batches toggle checkbox. */
let separateBatchesElem = getRequiredElementById('separate_batches_checkbox');
separateBatchesElem.checked = localStorage.getItem('separateBatches') == 'true';
/** Called when the user changes separate-batches toggle to update local storage. */
function toggleSeparateBatches() {
    localStorage.setItem('separateBatches', `${separateBatchesElem.checked}`);
}

let batchCardDelegationReady = false;

function ensureBatchCardDelegationReady() {
    if (batchCardDelegationReady) {
        return;
    }
    let batchRoot = getRequiredElementById('current_image_batch');
    batchRoot.addEventListener('click', (e) => {
        if (e.defaultPrevented || e.button != 0) {
            return;
        }
        let div = e.target.closest('.image-block');
        if (!div || !batchRoot.contains(div)) {
            return;
        }
        clickImageInBatch(div);
    });
    batchRoot.addEventListener('contextmenu', (e) => {
        let div = e.target.closest('.image-block');
        if (!div || !batchRoot.contains(div)) {
            return;
        }
        rightClickImageInBatch(e, div);
    });
    batchCardDelegationReady = true;
}

function clickImageInBatch(div) {
    let imgElem = div.getElementsByTagName('img')[0];
    if (currentImgSrc == div.dataset.src) {
        imageFullView.showImage(div.dataset.src, div.dataset.metadata, div.dataset.batch_id);
        return;
    }
    setCurrentImage(div.dataset.src, div.dataset.metadata, div.dataset.batch_id ?? '', imgElem && imgElem.dataset.previewGrow == 'true', false, true, div.dataset.is_placeholder == 'true');
}

function cleanupBatchContainerIfEmpty(div) {
    let parent = div?.parentElement;
    if (!parent || parent.id == 'current_image_batch') {
        return;
    }
    if (parent.children.length > 0) {
        return;
    }
    parent.remove();
}

/** Removes a preview thumbnail and moves to either previous or next image. */
function removeImageBlockFromBatch(div, shift = false) {
    if (!div.classList.contains('image-block-current')) {
        div.remove();
        cleanupBatchContainerIfEmpty(div);
        return;
    }
    let chosen = div.previousElementSibling || div.nextElementSibling;
    div.remove();
    cleanupBatchContainerIfEmpty(div);
    if (shift && chosen) {
        clickImageInBatch(chosen);
    }
}

function rightClickImageInBatch(e, div) {
    if (e.shiftKey || e.ctrlKey) {
        return;
    }
    let src = div.dataset.src;
    let fullsrc = getImageFullSrc(src);
    let metadata = div.dataset.metadata;
    let popoverActions = [];
    for (let added of buttonsForImage(fullsrc, src, metadata)) {
        if (added.href) {
            popoverActions.push({ key: added.label, href: added.href, is_download: added.is_download, title: added.title });
        }
        else {
            popoverActions.push({ key: added.label, action: added.onclick, title: added.title });
        }
    }
    popoverActions.push({ key: 'Remove From Batch View', action: () => removeImageBlockFromBatch(div, true) })
    let popover = new AdvancedPopover('image_batch_context_menu', popoverActions, false, mouseX, mouseY, document.body, null);
    e.preventDefault();
    e.stopPropagation();
    return false;
}

/** "Reuse Parameters" button impl. */
function copy_current_image_params() {
    if (!currentMetadataVal) {
        alert('No parameters to copy!');
        return;
    }
    let readable;
    let metadataFull;
    try {
        readable = interpretMetadata(currentMetadataVal);
        if (!readable) {
            alert('Failed to read image parameters.');
            return;
        }
        metadataFull = JSON.parse(readable);
    }
    catch (e) {
        console.log(`Failed to parse current image metadata for reuse: ${e}`);
        alert('Failed to parse image parameters for reuse.');
        return;
    }
    let metadata = metadataFull.sui_image_params;
    if (!metadata) {
        alert('Selected image does not contain reusable parameters.');
        return;
    }
    let extra = metadataFull.sui_extra_data || metadata;
    for (let param of Object.keys(metadata)) {
        let remapId = window.parameter_remaps[param];
        if (remapId) {
            metadata[remapId] = metadata[param];
            delete metadata[param];
        }
    }
    if ('original_prompt' in extra) {
        metadata.prompt = extra.original_prompt;
    }
    if ('original_negativeprompt' in extra) {
        metadata.negativeprompt = extra.original_negativeprompt;
    }
    // Special hacks to repair edge cases in LoRA reuse
    // There should probably just be a direct "for lora in list, set lora X with weight Y" instead of this
    if ('lorasectionconfinement' in metadata && 'loras' in metadata && 'loraweights' in metadata) {
        let confinements = metadata.lorasectionconfinement;
        let loras = metadata.loras;
        let weights = metadata.loraweights;
        let promptedLoras = extra.prompted_loras || [];
        let isOldSwarmVers = !metadata.swarm_version || metadata.swarm_version.match(/^0\.9\.[0-6]\./);
        if (confinements.length == loras.length && loras.length == weights.length) {
            let newLoras = [];
            let newWeights = [];
            let newConfinements = [];
            for (let i = 0; i < confinements.length; i++) {
                if (isOldSwarmVers ? confinements[i] == -1 : !promptedLoras.includes(loras[i])) {
                    newLoras.push(loras[i]);
                    newWeights.push(weights[i]);
                    newConfinements.push(confinements[i]);
                }
            }
            metadata.loras = newLoras;
            metadata.loraweights = newWeights;
            if (isOldSwarmVers) {
                delete metadata.lorasectionconfinement;
            }
            else {
                metadata.lorasectionconfinement = newConfinements;
            }
        }
    }
    if ('loras' in metadata && 'loraweights' in metadata && document.getElementById('input_loras') && metadata.loras.length == metadata.loraweights.length) {
        let loraElem = getRequiredElementById('input_loras');
        for (let val of metadata.loras) {
            if (val && !$(loraElem).find(`option[value="${val}"]`).length) {
                $(loraElem).append(new Option(val, val, false, false));
            }
        }
        let valSet = [...loraElem.options].map(option => option.value);
        let newLoras = [];
        let newWeights = [];
        for (let val of valSet) {
            let index = metadata.loras.indexOf(val);
            if (index != -1) {
                newLoras.push(metadata.loras[index]);
                newWeights.push(metadata.loraweights[index]);
            }
        }
        metadata.loras = newLoras;
        metadata.loraweights = newWeights;
    }
    if (!('aspectratio' in metadata) && 'width' in metadata && 'height' in metadata) {
        metadata.aspectratio = 'Custom';
    }
    let exclude = getUserSetting('reuseparamexcludelist', '').split(',').map(s => cleanParamName(s));
    let resetExclude = [...exclude, ...Object.keys(metadata), ...Object.keys(extra).map(e => e.endsWith('_filename') ? e.substring(0, e.length - '_filename'.length) : null).filter(e => e != null)];
    resetParamsToDefault(resetExclude, false);
    for (let param of gen_param_types) {
        if (param.nonreusable || exclude.includes(param.id)) {
            continue;
        }
        let elem = document.getElementById(`input_${param.id}`);
        let val = metadata[param.id];
        if (elem && val !== undefined && val !== null && val !== '') {
            let group = param.group;
            while (group) {
                if (group.toggles) {
                    let toggle = document.getElementById(`input_group_content_${group.id}_toggle`);
                    if (toggle && !toggle.checked) {
                        toggle.click();
                    }
                }
                group = group.parent;
            }
            setDirectParamValue(param, val);
        }
        else if (elem && param.toggleable && param.visible && !resetExclude.includes(param.id)) {
            let toggle = document.getElementById(`input_${param.id}_toggle`);
            if (toggle) {
                toggle.checked = false;
                doToggleEnable(elem.id);
            }
        }
    }
    hideUnsupportableParams();
}

/**
 * Shifts the current image view (and full-view if open) to the next or previous image.
 * Returns true if the shift was successful, returns false if there was nothing to shift to.
 */
function shiftToNextImagePreview(next = true, expand = false, isArrows = false) {
    let curImgElem = currentImageHelper.getCurrentImage();
    if (!curImgElem) {
        return false;
    }
    let doCycle = getUserSetting('ui.imageshiftingcycles', 'true');
    doCycle = doCycle == 'true' || (isArrows && doCycle == 'only_arrows');
    let expandedState = imageFullView.isOpen() ? imageFullView.copyState() : {};
    if (curImgElem.dataset.batch_id == 'history') {
        if (lastHistoryImageDiv == null || lastHistoryImageDiv.parentElement == null) {
            return false;
        }
        let divs = [...lastHistoryImageDiv.parentElement.children].filter(div => div.classList.contains('image-block') || div.classList.contains('model-block'));
        let index = divs.findIndex(div => div == lastHistoryImageDiv);
        if (index == -1) {
            console.log(`Image preview shift failed as current image ${lastHistoryImage} is not in history area`);
            return false;
        }
        let newIndex = index + (next ? 1 : -1);
        if (newIndex < 0) {
            if (!doCycle) {
                return false;
            }
            newIndex = divs.length - 1;
        }
        else if (newIndex >= divs.length) {
            if (!doCycle) {
                return false;
            }
            newIndex = 0;
        }
        if (newIndex == index) {
            return false;
        }
        let target = divs[newIndex].querySelector('.image-block-img-inner');
        if (!target) {
            return false;
        }
        target.click();
        if (expand) {
            target.click();
            imageFullView.showImage(currentImgSrc, currentMetadataVal, 'history');
            imageFullView.pasteState(expandedState);
        }
        return true;
    }
    let batch_area = getRequiredElementById('current_image_batch');
    let imgs = [...batch_area.getElementsByClassName('image-block-img-inner')].filter(i => findParentOfClass(i, 'image-block-placeholder') == null);
    function getSrc(elem) {
        if (elem.tagName == 'VIDEO') {
            return elem.querySelector('source').src;
        }
        return elem.src;
    }
    let curImgSrc = getSrc(curImgElem);
    let index = imgs.findIndex(img => getSrc(img) == curImgSrc);
    if (index == -1) {
        let cleanSrc = (src) => src.length > 200 ? src.substring(0, 200) + '...' : src;
        console.log(`Image preview shift failed as current image ${cleanSrc(curImgSrc)} is not in batch area set [${imgs.map(getSrc).map(cleanSrc).join(', ')}]`);
        return false;
    }
    let newIndex = index + (next ? 1 : -1);
    if (newIndex < 0) {
        if (!doCycle) {
            return false;
        }
        newIndex = imgs.length - 1;
    }
    else if (newIndex >= imgs.length) {
        if (!doCycle) {
            return false;
        }
        newIndex = 0;
    }
    if (newIndex == index) {
        return false;
    }
    let newImg = imgs[newIndex];
    let block = findParentOfClass(newImg, 'image-block');
    setCurrentImage(block.dataset.src, block.dataset.metadata, block.dataset.batch_id, newImg.dataset.previewGrow == 'true');
    if (expand) {
        imageFullView.showImage(block.dataset.src, block.dataset.metadata, block.dataset.batch_id);
        imageFullView.pasteState(expandedState);
    }
    return true;
}

window.addEventListener('keydown', function(kbevent) {
    let isFullView = imageFullView.isOpen();
    let isCurImgFocused = document.activeElement &&
        (findParentOfClass(document.activeElement, 'current_image')
        || findParentOfClass(document.activeElement, 'current_image_batch')
        || document.activeElement.tagName == 'BODY');
    if (isFullView && kbevent.key == 'Escape') {
        $('#image_fullview_modal').modal('toggle');
    }
    else if ((kbevent.key == 'ArrowLeft' || kbevent.key == 'ArrowUp') && (isFullView || isCurImgFocused)) {
        shiftToNextImagePreview(false, isFullView, true);
    }
    else if ((kbevent.key == 'ArrowRight' || kbevent.key == 'ArrowDown') && (isFullView || isCurImgFocused)) {
        shiftToNextImagePreview(true, isFullView, true);
    }
    else if (kbevent.key === "Enter" && kbevent.ctrlKey && internalSiteJsGetUserSetting('ctrlenterkeygenerates', true) && isVisible(getRequiredElementById('main_image_area'))) {
        getRequiredElementById('alt_generate_button').click();
    }
    else if (kbevent.key === "Enter" && kbevent.ctrlKey && internalSiteJsGetUserSetting('ctrlenterkeygenerates', true) && isVisible(getRequiredElementById('simple_generate_button'))) {
        getRequiredElementById('simple_generate_button').click();
    }
    else {
        return;
    }
    kbevent.preventDefault();
    kbevent.stopPropagation();
    return false;
});

function alignImageDataFormat() {
    let curImg = getRequiredElementById('current_image');
    let img = currentImageHelper.getCurrentImage();
    if (!img) {
        curImg.classList.remove('current_image_sideblock');
        return;
    }
    let curImgContainer = currentImageHelper.getCurrentImageContainer();
    let format = getUserSetting('ImageMetadataFormat', 'auto');
    let extrasWrapper = curImg.querySelector('.current-image-extras-wrapper');
    let scale = img.dataset.previewGrow == 'true' ? 8 : 1;
    let imgWidth = (img.naturalWidth ?? img.videoWidth) * scale;
    let imgHeight = (img.naturalHeight ?? img.videoHeight) * scale;
    let ratio = imgWidth / imgHeight;
    let height = Math.min(imgHeight, curImg.offsetHeight);
    let width = Math.min(imgWidth, height * ratio);
    let remainingWidth = curImg.clientWidth - width - 30;
    curImgContainer.style.maxWidth = `calc(min(100%, ${width}px))`;
    if ((remainingWidth > 30 * 16 && format == 'auto') || format == 'side') {
        curImg.classList.remove('current_image_small');
        curImg.classList.add('current_image_sideblock');
        extrasWrapper.style.display = 'inline-block';
        extrasWrapper.classList.add('extras-wrapper-sideblock');
        curImgContainer.style.maxHeight = `calc(max(15rem, 100%))`;
        if (remainingWidth < 30 * 16) {
            extrasWrapper.style.width = `${30 * 16}px`;
            extrasWrapper.style.maxWidth = `${30 * 16}px`;
            curImgContainer.style.maxWidth = `calc(min(100%, ${curImg.clientWidth - 30 * 16 - 30}px))`;
        }
        else {
            extrasWrapper.style.width = `${remainingWidth}px`;
            extrasWrapper.style.maxWidth = `${remainingWidth}px`;
        }
    }
    else {
        curImg.classList.add('current_image_small');
        curImg.classList.remove('current_image_sideblock');
        extrasWrapper.style.width = '100%';
        extrasWrapper.style.maxWidth = `100%`;
        extrasWrapper.style.display = 'block';
        extrasWrapper.classList.remove('extras-wrapper-sideblock');
        curImgContainer.style.maxHeight = `calc(max(15rem, 100% - 5.1rem))`;
    }
}

function toggleStar(path, rawSrc) {
    genericRequest('ToggleImageStarred', {'path': path}, data => {
        let curImgImg = currentImageHelper.getCurrentImage();
        if (curImgImg && curImgImg.dataset.src == rawSrc) {
            let oldMetadata = JSON.parse(curImgImg.dataset.metadata);
            let newMetadata = { ...oldMetadata, is_starred: data.new_state };
            curImgImg.dataset.metadata = JSON.stringify(newMetadata);
            let button = getRequiredElementById('current_image').querySelector('.star-button');
            if (button) {
                if (data.new_state) {
                    button.classList.add('button-starred-image');
                    button.innerText = 'Starred';
                }
                else {
                    button.classList.remove('button-starred-image');
                    button.innerText = 'Star';
                }
            }
        }
        forEachSwarmImageCardForSrc(rawSrc, card => {
            if (card.setStarred) {
                card.setStarred(data.new_state);
            }
            else {
                card.classList.toggle('image-block-starred', data.new_state);
            }
        });
        if (imageFullView.isOpen() && imageFullView.currentSrc == rawSrc) {
            let oldMetadata = JSON.parse(imageFullView.currentMetadata);
            let newMetadata = { ...oldMetadata, is_starred: data.new_state };
            let state = imageFullView.copyState();
            imageFullView.showImage(rawSrc, JSON.stringify(newMetadata), imageFullView.currentBatchId);
            imageFullView.pasteState(state);
        }
    });
}

function getSaveInputFromMetadata(metadata) {
    let fallback = getGenInput();
    fallback.donotsave = false;
    fallback.images = 1;
    if (!metadata) {
        return fallback;
    }
    try {
        let readable = interpretMetadata(metadata);
        let metaObj = readable ? JSON.parse(readable) : null;
        if (!metaObj?.sui_image_params || typeof metaObj.sui_image_params != 'object') {
            return fallback;
        }
        let input = { ...metaObj.sui_image_params };
        input.donotsave = false;
        input.images = 1;
        if (metaObj.sui_extra_data && typeof metaObj.sui_extra_data == 'object' && !Array.isArray(metaObj.sui_extra_data)) {
            input.extra_metadata = { ...metaObj.sui_extra_data };
        }
        return input;
    }
    catch (e) {
        console.log(`Failed to parse metadata for manual save, using current params instead: ${e}`);
        return fallback;
    }
}

function saveCurrentImageToHistory(img, button = null) {
    if (!img || img.tagName != 'IMG') {
        showError('Manual save is only supported for images.');
        return;
    }
    if (button) {
        button.disabled = true;
    }
    let oldSrc = img.dataset.src || img.src;
    let batchId = img.dataset.batch_id || '';
    let requestData = getSaveInputFromMetadata(img.dataset.metadata || currentMetadataVal || '');
    let releaseButton = () => {
        if (!button) {
            return;
        }
        button.disabled = false;
    };
    let waitForHistoryToContain = (savedPath) => {
        if (typeof imageHistoryBrowser === 'undefined') {
            return;
        }
        let expected = getImageFullSrc(savedPath);
        if (!expected) {
            return;
        }
        let attempts = 0;
        let maxAttempts = 8;
        let hasExpected = () => {
            if (!imageHistoryBrowser?.lastFiles) {
                return false;
            }
            for (let file of imageHistoryBrowser.lastFiles) {
                if (!file) {
                    continue;
                }
                if (file.name == expected) {
                    return true;
                }
                let fileSrc = file.data?.fullsrc || file.data?.src || file.name;
                if (getImageFullSrc(fileSrc) == expected) {
                    return true;
                }
            }
            return false;
        };
        let tryRefresh = () => {
            if (hasExpected()) {
                return;
            }
            attempts++;
            if (attempts <= 6 && imageHistoryBrowser?.lightRefresh) {
                imageHistoryBrowser.lightRefresh();
            }
            else if (imageHistoryBrowser?.refresh) {
                imageHistoryBrowser.refresh();
            }
            if (attempts < maxAttempts) {
                let delay = attempts < 4 ? 250 : 500;
                setTimeout(tryRefresh, delay);
            }
        };
        setTimeout(tryRefresh, 100);
    };
    let finish = (imageData) => {
        requestData.image = imageData;
        genericRequest('AddImageToHistory', requestData, res => {
            releaseButton();
            let saved = res.images?.[0];
            if (!saved?.image) {
                showError('Image save did not return an output file.');
                return;
            }
            let savedMetadata = img.dataset.metadata || currentMetadataVal || saved.metadata || '{}';
            setCurrentImage(saved.image, savedMetadata, batchId);
            forEachSwarmImageCardForSrc(oldSrc, card => {
                card.dataset.src = saved.image;
                card.dataset.metadata = savedMetadata;
                let media = card.querySelector('img, video, audio');
                if (!media) {
                    return;
                }
                if (media.tagName == 'VIDEO') {
                    let source = media.querySelector('source');
                    if (source) {
                        source.src = saved.image;
                        media.load();
                    }
                    else {
                        media.src = saved.image;
                    }
                }
                else {
                    media.src = saved.image;
                }
            });
            if (imageFullView.isOpen() && imageFullView.currentSrc == oldSrc) {
                let state = imageFullView.copyState();
                imageFullView.showImage(saved.image, savedMetadata, imageFullView.currentBatchId);
                imageFullView.pasteState(state);
            }
            waitForHistoryToContain(saved.image);
            doNoticePopover('Saved image and metadata.', 'notice-pop-green');
        }, 0, error => {
            releaseButton();
            showError(error);
        });
    };
    finish(img.src);
}

defaultButtonChoices = 'Use As Init,Edit Image,Send To Image Edit Tab,Star,Reuse Parameters,Save Image';

/**
 * Current zoom value for the Image Editing tab editor.
 */
let imageEditingZoomLevel = 1;
let imageEditingZoomMin = 0.1;
let imageEditingZoomMax = 16;
let imageEditingColorWired = false;
let imageEditingColor = '#ffffff';
let imageEditingInlineColorPicker = null;
let imageEditingTabEditor = null;
let imageEditingToolButtons = {};
let imageEditingSelectionToolButtons = {};
let imageEditingSplittersWired = false;
let imageEditingLeftSidebarDrag = false;
let imageEditingRightSidebarDrag = false;
let imageEditingPausedGenerateEditor = false;
let imageEditingLeftSidebarWidth = parseInt(localStorage.getItem('barspot_imageediting_leftSidebar') || `${convertRemToPixels(28)}`);
let imageEditingRightSidebarWidth = parseInt(localStorage.getItem('barspot_imageediting_rightSidebar') || `${convertRemToPixels(16)}`);
let imageEditingToolsCollapsed = localStorage.getItem('imageediting_toolsCollapsed') == 'true';
let imageEditingPenOptionsCollapsed = localStorage.getItem('imageediting_penOptionsCollapsed') == 'true';
let imageEditingActionsCollapsed = localStorage.getItem('imageediting_actionsCollapsed') == 'true';
let imageEditingLayerOptionsCollapsed = localStorage.getItem('imageediting_layerOptionsCollapsed') == 'true';
let imageEditingImageOptionsCollapsed = localStorage.getItem('imageediting_imageOptionsCollapsed') == 'true';
let imageEditingSelectionCropCollapsed = localStorage.getItem('imageediting_selectionCropCollapsed') == 'true';
let imageEditingEffectsPresetsCollapsed = localStorage.getItem('imageediting_effectsPresetsCollapsed') == 'true';
let imageEditingLayerOptionsWired = false;
let imageEditingSelectionEffectsWired = false;
let imageEditingToneBalanceRanges = ['shadows', 'midtones', 'highlights'];
let imageEditingToneBalanceChannels = ['r', 'g', 'b'];
let imageEditingSelectionToolIds = ['select', 'ellipse-select', 'lasso-select', 'polygon-select', 'magic-wand', 'color-select', 'crop'];
let imageEditingLayerAdjustmentDefinitions = [
    { key: 'saturation', property: 'saturation', defaultValue: 1, sliderMin: 0, sliderMax: 200, sliderDefault: 100, sliderToProperty: value => value / 100, propertyToSlider: value => Math.round(value * 100), format: value => `${value}%`, contextId: 'imageediting_layer_saturation_context' },
    { key: 'light_value', property: 'lightValue', defaultValue: 1, sliderMin: 0, sliderMax: 200, sliderDefault: 100, sliderToProperty: value => value / 100, propertyToSlider: value => Math.round(value * 100), format: value => `${value}%`, contextId: 'imageediting_layer_light_value_context' },
    { key: 'contrast', property: 'contrast', defaultValue: 1, sliderMin: 0, sliderMax: 200, sliderDefault: 100, sliderToProperty: value => value / 100, propertyToSlider: value => Math.round(value * 100), format: value => `${value}%` },
    { key: 'hue_shift', property: 'hueShift', defaultValue: 0, sliderMin: -180, sliderMax: 180, sliderDefault: 0, sliderToProperty: value => value, propertyToSlider: value => Math.round(value), format: value => `${value}\u00B0` },
    { key: 'gamma', property: 'gamma', defaultValue: 1, sliderMin: 10, sliderMax: 300, sliderDefault: 100, sliderToProperty: value => value / 100, propertyToSlider: value => Math.round(value * 100), format: value => `${(value / 100).toFixed(2)}` },
    { key: 'temperature', property: 'temperature', defaultValue: 0, sliderMin: -100, sliderMax: 100, sliderDefault: 0, sliderToProperty: value => value / 100, propertyToSlider: value => Math.round(value * 100), format: value => imageEditingFormatSignedPercent(value) },
    { key: 'tint', property: 'tint', defaultValue: 0, sliderMin: -100, sliderMax: 100, sliderDefault: 0, sliderToProperty: value => value / 100, propertyToSlider: value => Math.round(value * 100), format: value => imageEditingFormatSignedPercent(value) },
    { key: 'shadows', property: 'shadows', defaultValue: 0, sliderMin: -100, sliderMax: 100, sliderDefault: 0, sliderToProperty: value => value / 100, propertyToSlider: value => Math.round(value * 100), format: value => imageEditingFormatSignedPercent(value) },
    { key: 'highlights', property: 'highlights', defaultValue: 0, sliderMin: -100, sliderMax: 100, sliderDefault: 0, sliderToProperty: value => value / 100, propertyToSlider: value => Math.round(value * 100), format: value => imageEditingFormatSignedPercent(value) },
    { key: 'whites', property: 'whites', defaultValue: 0, sliderMin: -100, sliderMax: 100, sliderDefault: 0, sliderToProperty: value => value / 100, propertyToSlider: value => Math.round(value * 100), format: value => imageEditingFormatSignedPercent(value) },
    { key: 'blacks', property: 'blacks', defaultValue: 0, sliderMin: -100, sliderMax: 100, sliderDefault: 0, sliderToProperty: value => value / 100, propertyToSlider: value => Math.round(value * 100), format: value => imageEditingFormatSignedPercent(value) }
];
let imageEditingEffectDefinitions = [
    { key: 'blur', labelKey: 'imageediting_effect_blur_value', sliderId: 'imageediting_effect_blur_slider', defaultValue: 0, sliderToProperty: value => value / 4, propertyToSlider: value => Math.round(value * 4), format: value => `${value}` },
    { key: 'sharpen', labelKey: 'imageediting_effect_sharpen_value', sliderId: 'imageediting_effect_sharpen_slider', defaultValue: 0, sliderToProperty: value => value / 4, propertyToSlider: value => Math.round(value * 4), format: value => `${value}` },
    { key: 'noiseReduction', labelKey: 'imageediting_effect_noise_reduction_value', sliderId: 'imageediting_effect_noise_reduction_slider', defaultValue: 0, sliderToProperty: value => value / 4, propertyToSlider: value => Math.round(value * 4), format: value => `${value}` },
    { key: 'vignette', labelKey: 'imageediting_effect_vignette_value', sliderId: 'imageediting_effect_vignette_slider', defaultValue: 0, sliderToProperty: value => value / 100, propertyToSlider: value => Math.round(value * 100), format: value => `${value}%` },
    { key: 'glow', labelKey: 'imageediting_effect_glow_value', sliderId: 'imageediting_effect_glow_slider', defaultValue: 0, sliderToProperty: value => value / 100, propertyToSlider: value => Math.round(value * 100), format: value => `${value}%` }
];

/**
 * Gets the Image Editing editor area.
 */
function imageEditingGetEditorArea() {
    return document.getElementById('imageediting_editor_area');
}

/**
 * Gets the zoom label element for the Image Editing tab.
 */
function imageEditingGetZoomText() {
    return document.getElementById('imageediting_zoom_level');
}

/**
 * Gets the Image Editing color selector text input.
 */
function imageEditingGetColorText() {
    return document.getElementById('imageediting_color_text');
}

/**
 * Gets the Image Editing color selector swatch.
 */
function imageEditingGetColorSwatch() {
    return document.getElementById('imageediting_color_swatch');
}

/**
 * Gets the Image Editing inline color picker mount.
 */
function imageEditingGetInlineColorPickerMount() {
    return document.getElementById('imageediting_inline_color_picker');
}

/**
 * Gets the Image Editing tool button container.
 */
function imageEditingGetToolButtonsArea() {
    return document.getElementById('imageediting_tool_buttons');
}

/**
 * Gets the Image Editing option button container.
 */
function imageEditingGetOptionButtonsArea() {
    return document.getElementById('imageediting_option_buttons');
}

function imageEditingGetPenOptionsBody() {
    return document.getElementById('imageediting_pen_options_body');
}

function imageEditingGetPenOptionsMount() {
    return document.getElementById('imageediting_pen_options_mount');
}

function imageEditingGetPenOptionsEmpty() {
    return document.getElementById('imageediting_pen_options_empty');
}

/**
 * Gets the Image Editing tools section header.
 */
function imageEditingGetToolsHeader() {
    return document.getElementById('imageediting_tools_header');
}

/**
 * Gets the Image Editing actions section header.
 */
function imageEditingGetActionsHeader() {
    return document.getElementById('imageediting_actions_header');
}

function imageEditingGetPenOptionsHeader() {
    return document.getElementById('imageediting_pen_options_header');
}

/**
 * Gets the Image Editing layer options section header.
 */
function imageEditingGetLayerOptionsHeader() {
    return document.getElementById('imageediting_layer_options_header');
}

/**
 * Gets the Image Editing image options section header.
 */
function imageEditingGetImageOptionsHeader() {
    return document.getElementById('imageediting_image_options_header');
}

function imageEditingGetSelectionCropHeader() {
    return document.getElementById('imageediting_selection_crop_header');
}

function imageEditingGetEffectsPresetsHeader() {
    return document.getElementById('imageediting_effects_presets_header');
}

/**
 * Gets the Image Editing tools section toggle marker.
 */
function imageEditingGetToolsToggleState() {
    return document.getElementById('imageediting_tools_toggle_state');
}

/**
 * Gets the Image Editing actions section toggle marker.
 */
function imageEditingGetActionsToggleState() {
    return document.getElementById('imageediting_actions_toggle_state');
}

function imageEditingGetPenOptionsToggleState() {
    return document.getElementById('imageediting_pen_options_toggle_state');
}

/**
 * Gets the Image Editing layer options section toggle marker.
 */
function imageEditingGetLayerOptionsToggleState() {
    return document.getElementById('imageediting_layer_options_toggle_state');
}

/**
 * Gets the Image Editing image options section toggle marker.
 */
function imageEditingGetImageOptionsToggleState() {
    return document.getElementById('imageediting_image_options_toggle_state');
}

function imageEditingGetSelectionCropToggleState() {
    return document.getElementById('imageediting_selection_crop_toggle_state');
}

function imageEditingGetEffectsPresetsToggleState() {
    return document.getElementById('imageediting_effects_presets_toggle_state');
}

/**
 * Gets the Image Editing layer options section body.
 */
function imageEditingGetLayerOptionsBody() {
    return document.getElementById('imageediting_layer_options_body');
}

/**
 * Gets the Image Editing image options section body.
 */
function imageEditingGetImageOptionsBody() {
    return document.getElementById('imageediting_image_options_body');
}

function imageEditingGetSelectionCropBody() {
    return document.getElementById('imageediting_selection_crop_body');
}

function imageEditingGetEffectsPresetsBody() {
    return document.getElementById('imageediting_effects_presets_body');
}

function imageEditingEnsurePenOptionsSectionExists() {
    if (imageEditingGetPenOptionsBody()) {
        return;
    }
    let toolsArea = imageEditingGetToolButtonsArea();
    if (!toolsArea || !toolsArea.parentElement) {
        return;
    }
    let group = document.createElement('div');
    group.className = 'imageediting_input_group';
    group.innerHTML = `<div class="imageediting_section_header" id="imageediting_pen_options_header" onclick="imageEditingToggleInputSection('pen_options')">
            <span class="imageediting_section_header_title translate">Pen Options</span>
            <span class="imageediting_section_header_state" id="imageediting_pen_options_toggle_state">-</span>
        </div>
        <div class="imageediting_layer_options_body imageediting_pen_options_body" id="imageediting_pen_options_body">
            <div class="imageediting_pen_options_empty translate" id="imageediting_pen_options_empty">Select the brush or eraser to edit pen options.</div>
            <div class="imageediting_pen_options_mount" id="imageediting_pen_options_mount"></div>
        </div>`;
    toolsArea.parentElement.insertAdjacentElement('afterend', group);
}

/**
 * Gets the Image Editing layer opacity slider.
 */
function imageEditingGetLayerOpacitySlider() {
    return document.getElementById('imageediting_layer_opacity_slider');
}

/**
 * Gets the Image Editing layer opacity label.
 */
function imageEditingGetLayerOpacityValue() {
    return document.getElementById('imageediting_layer_opacity_value');
}

/**
 * Gets the Image Editing layer opacity context text.
 */
function imageEditingGetLayerOpacityContext() {
    return document.getElementById('imageediting_layer_opacity_context');
}

/**
 * Gets the Image Editing layer blend-mode select.
 */
function imageEditingGetLayerBlendModeSelect() {
    return document.getElementById('imageediting_layer_blend_mode_select');
}

/**
 * Gets the Image Editing layer blend-mode context text.
 */
function imageEditingGetLayerBlendModeContext() {
    return document.getElementById('imageediting_layer_blend_mode_context');
}

/**
 * Gets an Image Editing image-adjustment slider by key.
 */
function imageEditingGetLayerAdjustmentSlider(key) {
    return document.getElementById(`imageediting_layer_${key}_slider`);
}

/**
 * Gets an Image Editing image-adjustment label by key.
 */
function imageEditingGetLayerAdjustmentValueLabel(key) {
    return document.getElementById(`imageediting_layer_${key}_value`);
}

/**
 * Gets all Image Options tone-balance sliders.
 */
function imageEditingGetToneBalanceSliders() {
    let imageOptionsBody = imageEditingGetImageOptionsBody();
    if (!imageOptionsBody) {
        return [];
    }
    return Array.from(imageOptionsBody.querySelectorAll('.imageediting_tone_balance_slider'));
}

/**
 * Gets the tone-balance context text.
 */
function imageEditingGetToneBalanceContext() {
    return document.getElementById('imageediting_tone_balance_context');
}

/**
 * Gets the tone-balance value label for a range/channel.
 */
function imageEditingGetToneBalanceValueLabel(range, channel) {
    return document.getElementById(`imageediting_tone_${range}_${channel}_value`);
}

/**
 * Gets the Layer Options delete button.
 */
function imageEditingGetLayerDeleteButton() {
    return document.getElementById('imageediting_layer_delete_button');
}

/**
 * Gets the Layer Options duplicate button.
 */
function imageEditingGetLayerDuplicateButton() {
    return document.getElementById('imageediting_layer_duplicate_button');
}

/**
 * Gets the Layer Options convert-to-image button.
 */
function imageEditingGetLayerConvertToImageButton() {
    return document.getElementById('imageediting_layer_convert_to_image_button');
}

/**
 * Gets the Layer Options invert-mask button.
 */
function imageEditingGetLayerInvertMaskButton() {
    return document.getElementById('imageediting_layer_invert_mask_button');
}

/**
 * Gets the Layer Options convert-to-mask button.
 */
function imageEditingGetLayerConvertToMaskButton() {
    return document.getElementById('imageediting_layer_convert_to_mask_button');
}

/**
 * Gets the Layer Options invert-colors button.
 */
function imageEditingGetLayerInvertColorsButton() {
    return document.getElementById('imageediting_layer_invert_colors_button');
}

/**
 * Gets the Layer Options flip/mirror-horizontal button.
 */
function imageEditingGetLayerFlipMirrorHorizontalButton() {
    return document.getElementById('imageediting_layer_flip_mirror_horizontal_button');
}

/**
 * Gets the Layer Options flip/mirror-vertical button.
 */
function imageEditingGetLayerFlipMirrorVerticalButton() {
    return document.getElementById('imageediting_layer_flip_mirror_vertical_button');
}

/**
 * Gets the Image Editing left input sidebar area.
 */
function imageEditingGetInputSidebar() {
    return document.getElementById('imageediting_input_sidebar');
}

/**
 * Gets the Image Editing left splitter.
 */
function imageEditingGetLeftSplitter() {
    return document.getElementById('imageediting_left_splitter');
}

/**
 * Gets the Image Editing right sidebar area.
 */
function imageEditingGetRightSidebar() {
    return document.getElementById('imageediting_editor_sidebar');
}

/**
 * Gets the Image Editing right splitter.
 */
function imageEditingGetRightSplitter() {
    return document.getElementById('imageediting_right_splitter');
}

/**
 * Gets the Image Editing right sidebar content area.
 */
function imageEditingGetRightSidebarContent() {
    return document.getElementById('imageediting_editor_sidebar_content');
}

/**
 * Clamps the input sidebar width.
 */
function imageEditingClampLeftSidebarWidth(width) {
    if (isNaN(width)) {
        width = convertRemToPixels(28);
    }
    let maxWidth = Math.max(220, window.innerWidth - 220);
    return Math.min(maxWidth, Math.max(192, Math.round(width)));
}

/**
 * Clamps the right options/sidebar width.
 */
function imageEditingClampRightSidebarWidth(width) {
    if (isNaN(width)) {
        width = convertRemToPixels(16);
    }
    let maxWidth = Math.max(220, window.innerWidth - 220);
    return Math.min(maxWidth, Math.max(192, Math.round(width)));
}

/**
 * Applies current left sidebar width to the Image Editing layout.
 */
function imageEditingApplyLeftSidebarWidth() {
    let sidebar = imageEditingGetInputSidebar();
    if (!sidebar) {
        return;
    }
    imageEditingLeftSidebarWidth = imageEditingClampLeftSidebarWidth(imageEditingLeftSidebarWidth);
    sidebar.style.width = `${imageEditingLeftSidebarWidth}px`;
    sidebar.style.flex = `0 0 ${imageEditingLeftSidebarWidth}px`;
}

/**
 * Applies current right options/sidebar width to the Image Editing editor.
 */
function imageEditingApplyRightSidebarWidth() {
    let sidebar = imageEditingGetRightSidebar();
    if (!sidebar) {
        return;
    }
    imageEditingRightSidebarWidth = imageEditingClampRightSidebarWidth(imageEditingRightSidebarWidth);
    sidebar.style.width = `${imageEditingRightSidebarWidth}px`;
    sidebar.style.flex = `0 0 ${imageEditingRightSidebarWidth}px`;
    if (imageEditingTabEditor && imageEditingTabEditor.canvas) {
        imageEditingTabEditor.resize();
    }
}

/**
 * Gets page X coordinate from a mouse/touch event.
 */
function imageEditingGetEventPageX(e) {
    if (e.touches && e.touches.length > 0) {
        return e.touches.item(0).pageX;
    }
    if (e.changedTouches && e.changedTouches.length > 0) {
        return e.changedTouches.item(0).pageX;
    }
    return e.pageX;
}

/**
 * Wires draggable splitters for Image Editing left and right sidebars.
 */
function imageEditingEnsureSplittersWired() {
    if (imageEditingSplittersWired) {
        return;
    }
    let leftSplitter = imageEditingGetLeftSplitter();
    let rightSplitter = imageEditingGetRightSplitter();
    if (!leftSplitter || !rightSplitter) {
        return;
    }
    let startLeftResize = (e) => {
        imageEditingLeftSidebarDrag = true;
        document.body.style.userSelect = 'none';
        e.preventDefault();
    };
    let startRightResize = (e) => {
        imageEditingRightSidebarDrag = true;
        document.body.style.userSelect = 'none';
        e.preventDefault();
    };
    leftSplitter.addEventListener('mousedown', startLeftResize, true);
    leftSplitter.addEventListener('touchstart', startLeftResize, true);
    rightSplitter.addEventListener('mousedown', startRightResize, true);
    rightSplitter.addEventListener('touchstart', startRightResize, true);
    let moveEvt = (e) => {
        if (!imageEditingLeftSidebarDrag && !imageEditingRightSidebarDrag) {
            return;
        }
        let offX = imageEditingGetEventPageX(e);
        if (imageEditingLeftSidebarDrag) {
            let layout = document.querySelector('#ImageEditing .imageediting_layout');
            if (layout) {
                imageEditingLeftSidebarWidth = imageEditingClampLeftSidebarWidth(offX - layout.getBoundingClientRect().left);
                localStorage.setItem('barspot_imageediting_leftSidebar', imageEditingLeftSidebarWidth);
                imageEditingApplyLeftSidebarWidth();
            }
        }
        if (imageEditingRightSidebarDrag) {
            let layout = document.querySelector('#ImageEditing .imageediting_layout');
            if (layout) {
                imageEditingRightSidebarWidth = imageEditingClampRightSidebarWidth(layout.getBoundingClientRect().right - offX);
                localStorage.setItem('barspot_imageediting_rightSidebar', imageEditingRightSidebarWidth);
                imageEditingApplyRightSidebarWidth();
            }
        }
        imageEditingApplyZoom();
        e.preventDefault();
    };
    let upEvt = () => {
        imageEditingLeftSidebarDrag = false;
        imageEditingRightSidebarDrag = false;
        document.body.style.userSelect = '';
    };
    document.addEventListener('mousemove', moveEvt);
    document.addEventListener('touchmove', moveEvt, { passive: false });
    document.addEventListener('mouseup', upEvt);
    document.addEventListener('touchend', upEvt);
    imageEditingSplittersWired = true;
}

/**
 * Refreshes tool button visibility and active-state markers.
 */
function imageEditingRefreshToolButtons() {
    if (!imageEditingTabEditor) {
        return;
    }
    for (let [toolId, button] of Object.entries(imageEditingToolButtons)) {
        let tool = imageEditingTabEditor.tools[toolId];
        if (!tool) {
            button.style.display = 'none';
            continue;
        }
        if (tool.div && tool.div.style.display == 'none') {
            button.style.display = 'none';
        }
        else {
            button.style.display = '';
        }
        button.classList.toggle('imageediting_tool_button_active', imageEditingTabEditor.activeTool && imageEditingTabEditor.activeTool.id == toolId);
    }
    for (let [toolId, button] of Object.entries(imageEditingSelectionToolButtons)) {
        let tool = imageEditingTabEditor.tools[toolId];
        button.style.display = tool ? '' : 'none';
        button.classList.toggle('imageediting_tool_button_active', imageEditingTabEditor.activeTool && imageEditingTabEditor.activeTool.id == toolId);
    }
    if (imageEditingTabEditor.activeTool && typeof imageEditingTabEditor.activeTool.color == 'string' && imageEditingTabEditor.activeTool.color != imageEditingColor) {
        imageEditingSetColor(imageEditingTabEditor.activeTool.color);
    }
    imageEditingRefreshPenOptions();
}

function imageEditingSetupPenOptions() {
    if (!imageEditingTabEditor) {
        return;
    }
    for (let toolId of ['brush', 'eraser']) {
        let tool = imageEditingTabEditor.tools[toolId];
        if (!tool || !tool.configDiv) {
            continue;
        }
        let presetBlock = tool.configDiv.querySelector('.id-preset-block');
        let presetButtonsBlock = tool.configDiv.querySelector('.id-preset-buttons-block');
        let pressureBlocks = tool.configDiv.querySelectorAll('.id-pressure-min-block, .id-pressure-curve-block');
        let pressureToggleBlock = tool.configDiv.querySelector('.id-pressure-size');
        let spotHealBlock = tool.configDiv.querySelector('.id-spotheal-block');
        if (!presetBlock && !presetButtonsBlock && (!pressureBlocks || pressureBlocks.length <= 0) && !pressureToggleBlock && !spotHealBlock) {
            continue;
        }
        let wrapper = document.createElement('div');
        wrapper.className = 'imageediting_pen_options_tool';
        let toggleBlock = pressureToggleBlock ? pressureToggleBlock.closest('.image-editor-tool-block') : null;
        if (toggleBlock) {
            wrapper.appendChild(toggleBlock);
        }
        for (let block of pressureBlocks) {
            wrapper.appendChild(block);
        }
        if (presetBlock) {
            wrapper.appendChild(presetBlock);
        }
        if (presetButtonsBlock) {
            wrapper.appendChild(presetButtonsBlock);
        }
        if (spotHealBlock) {
            wrapper.appendChild(spotHealBlock);
        }
        tool.penOptionsDiv = wrapper;
    }
}

function imageEditingRefreshPenOptions() {
    let mount = imageEditingGetPenOptionsMount();
    let empty = imageEditingGetPenOptionsEmpty();
    if (!mount || !empty) {
        return;
    }
    mount.innerHTML = '';
    if (!imageEditingTabEditor || !imageEditingTabEditor.activeTool || !imageEditingTabEditor.activeTool.penOptionsDiv) {
        empty.style.display = '';
        return;
    }
    empty.style.display = 'none';
    mount.appendChild(imageEditingTabEditor.activeTool.penOptionsDiv);
}

/**
 * Builds the labeled tool buttons for the Image Editing inputs area.
 */
function imageEditingBuildToolButtons() {
    if (!imageEditingTabEditor) {
        return;
    }
    let toolsArea = imageEditingGetToolButtonsArea();
    if (!toolsArea) {
        return;
    }
    toolsArea.innerHTML = '';
    imageEditingToolButtons = {};
    for (let tool of Object.values(imageEditingTabEditor.tools)) {
        if (tool.isTempTool || tool.id == 'options') {
            continue;
        }
        let button = document.createElement('button');
        button.className = 'basic-button imageediting_tool_button translate';
        button.type = 'button';
        let label = tool.name;
        if (tool.hotkey) {
            label += ` (${tool.hotkey.toUpperCase()})`;
        }
        button.innerText = label;
        button.setAttribute('aria-label', tool.name);
        button.title = tool.description;
        button.addEventListener('click', () => {
            imageEditingTabEditor.activateTool(tool.id);
            imageEditingRefreshToolButtons();
        });
        toolsArea.appendChild(button);
        imageEditingToolButtons[tool.id] = button;
    }
    imageEditingRefreshToolButtons();
}

function imageEditingBuildSelectionToolButtons() {
    if (!imageEditingTabEditor) {
        return;
    }
    let toolsArea = document.getElementById('imageediting_selection_tool_buttons');
    if (!toolsArea) {
        return;
    }
    toolsArea.innerHTML = '';
    imageEditingSelectionToolButtons = {};
    for (let toolId of imageEditingSelectionToolIds) {
        let tool = imageEditingTabEditor.tools[toolId];
        if (!tool) {
            continue;
        }
        let button = document.createElement('button');
        button.className = 'basic-button imageediting_tool_button translate';
        button.type = 'button';
        button.innerText = tool.name;
        button.title = tool.description;
        button.addEventListener('click', () => {
            imageEditingTabEditor.activateTool(tool.id);
            imageEditingRefreshToolButtons();
        });
        toolsArea.appendChild(button);
        imageEditingSelectionToolButtons[tool.id] = button;
    }
    imageEditingRefreshToolButtons();
}

/**
 * Builds the labeled option/action buttons for the Image Editing inputs area.
 */
function imageEditingBuildOptionButtons() {
    if (!imageEditingTabEditor) {
        return;
    }
    let optionsArea = imageEditingGetOptionButtonsArea();
    let optionsTool = imageEditingTabEditor.tools['options'];
    if (!optionsArea || !optionsTool) {
        return;
    }
    optionsArea.innerHTML = '';
    for (let option of optionsTool.optionButtons) {
        let button = document.createElement('button');
        button.className = 'basic-button imageediting_option_button translate';
        button.type = 'button';
        button.innerText = option.key;
        button.setAttribute('aria-label', option.key);
        button.title = option.key;
        button.addEventListener('click', () => {
            option.action();
        });
        optionsArea.appendChild(button);
    }
}

/**
 * Refreshes contextual visibility for Layer Options action buttons.
 */
function imageEditingRefreshLayerOptionActionButtons() {
    let deleteButton = imageEditingGetLayerDeleteButton();
    let duplicateButton = imageEditingGetLayerDuplicateButton();
    let convertToImageButton = imageEditingGetLayerConvertToImageButton();
    let invertMaskButton = imageEditingGetLayerInvertMaskButton();
    let convertToMaskButton = imageEditingGetLayerConvertToMaskButton();
    let invertColorsButton = imageEditingGetLayerInvertColorsButton();
    let flipMirrorHorizontalButton = imageEditingGetLayerFlipMirrorHorizontalButton();
    let flipMirrorVerticalButton = imageEditingGetLayerFlipMirrorVerticalButton();
    if (!deleteButton || !duplicateButton || !convertToImageButton || !invertMaskButton || !convertToMaskButton || !invertColorsButton || !flipMirrorHorizontalButton || !flipMirrorVerticalButton) {
        return;
    }
    let activeLayer = imageEditingTabEditor ? imageEditingTabEditor.activeLayer : null;
    if (!activeLayer) {
        deleteButton.disabled = true;
        duplicateButton.style.display = 'none';
        convertToImageButton.style.display = 'none';
        invertMaskButton.style.display = 'none';
        convertToMaskButton.style.display = 'none';
        invertColorsButton.style.display = 'none';
        flipMirrorHorizontalButton.style.display = 'none';
        flipMirrorVerticalButton.style.display = 'none';
        return;
    }
    deleteButton.disabled = false;
    duplicateButton.style.display = '';
    if (activeLayer.layerType == 'adjustment') {
        flipMirrorHorizontalButton.style.display = 'none';
        flipMirrorVerticalButton.style.display = 'none';
        convertToImageButton.style.display = 'none';
        invertMaskButton.style.display = 'none';
        convertToMaskButton.style.display = 'none';
        invertColorsButton.style.display = 'none';
    }
    else if (activeLayer.isMask) {
        flipMirrorHorizontalButton.style.display = '';
        flipMirrorVerticalButton.style.display = '';
        convertToImageButton.style.display = '';
        invertMaskButton.style.display = '';
        convertToMaskButton.style.display = 'none';
        invertColorsButton.style.display = 'none';
    }
    else {
        flipMirrorHorizontalButton.style.display = '';
        flipMirrorVerticalButton.style.display = '';
        convertToImageButton.style.display = 'none';
        invertMaskButton.style.display = 'none';
        convertToMaskButton.style.display = '';
        invertColorsButton.style.display = '';
    }
}

/**
 * Deletes the currently selected layer from the Image Editing tab.
 */
function imageEditingDeleteActiveLayer() {
    if (!imageEditingTabEditor || !imageEditingTabEditor.activeLayer) {
        return;
    }
    if (imageEditingTabEditor.layers.length <= 1) {
        showError('Cannot delete the final layer.');
        return;
    }
    imageEditingTabEditor.removeLayer(imageEditingTabEditor.activeLayer);
    imageEditingRefreshLayerOpacityControl();
}

/**
 * Duplicates the currently selected layer in the Image Editing tab.
 */
function imageEditingDuplicateActiveLayer() {
    if (!imageEditingTabEditor || !imageEditingTabEditor.activeLayer) {
        return;
    }
    imageEditingTabEditor.duplicateLayer(imageEditingTabEditor.activeLayer);
    imageEditingRefreshLayerOpacityControl();
}

/**
 * Converts the selected layer to an image layer.
 */
function imageEditingConvertActiveLayerToImage() {
    if (!imageEditingTabEditor || !imageEditingTabEditor.activeLayer) {
        return;
    }
    let layer = imageEditingTabEditor.activeLayer;
    if (!layer.isMask || layer.layerType == 'adjustment') {
        return;
    }
    layer.layerType = 'image';
    if (layer.infoSubDiv) {
        layer.infoSubDiv.innerText = 'Image';
    }
    layer.createButtons();
    imageEditingTabEditor.sortLayers();
    imageEditingTabEditor.markOutputChanged();
    imageEditingTabEditor.queueSceneRedraw();
    imageEditingRefreshLayerOpacityControl();
}

/**
 * Converts the selected layer to a mask layer.
 */
function imageEditingConvertActiveLayerToMask() {
    if (!imageEditingTabEditor || !imageEditingTabEditor.activeLayer) {
        return;
    }
    let layer = imageEditingTabEditor.activeLayer;
    if (layer.isMask || layer.layerType == 'adjustment') {
        return;
    }
    layer.layerType = 'mask';
    if (layer.infoSubDiv) {
        layer.infoSubDiv.innerText = 'Mask';
    }
    layer.createButtons();
    imageEditingTabEditor.sortLayers();
    imageEditingTabEditor.markOutputChanged();
    imageEditingTabEditor.queueSceneRedraw();
    imageEditingRefreshLayerOpacityControl();
}

/**
 * Inverts the selected layer as a mask operation.
 */
function imageEditingInvertActiveLayerMask() {
    if (!imageEditingTabEditor || !imageEditingTabEditor.activeLayer) {
        return;
    }
    let layer = imageEditingTabEditor.activeLayer;
    if (!layer.isMask) {
        return;
    }
    layer.invert();
    imageEditingRefreshLayerOpacityControl();
}

/**
 * Inverts the selected layer as an image-color operation.
 */
function imageEditingInvertActiveLayerColors() {
    if (!imageEditingTabEditor || !imageEditingTabEditor.activeLayer) {
        return;
    }
    let layer = imageEditingTabEditor.activeLayer;
    if (layer.isMask) {
        return;
    }
    layer.invert();
    imageEditingRefreshLayerOpacityControl();
}

/**
 * Flip/mirror the selected layer horizontally.
 */
function imageEditingFlipMirrorActiveLayerHorizontal() {
    if (!imageEditingTabEditor || !imageEditingTabEditor.activeLayer) {
        return;
    }
    let layer = imageEditingTabEditor.activeLayer;
    layer.flipHorizontal();
    imageEditingRefreshLayerOpacityControl();
}

/**
 * Flip/mirror the selected layer vertically.
 */
function imageEditingFlipMirrorActiveLayerVertical() {
    if (!imageEditingTabEditor || !imageEditingTabEditor.activeLayer) {
        return;
    }
    let layer = imageEditingTabEditor.activeLayer;
    layer.flipVertical();
    imageEditingRefreshLayerOpacityControl();
}

/**
 * Applies the current layer's opacity from the Layer Options slider.
 */
function imageEditingSetActiveLayerOpacityFromSlider() {
    if (!imageEditingTabEditor || !imageEditingTabEditor.activeLayer) {
        return;
    }
    let slider = imageEditingGetLayerOpacitySlider();
    if (!slider) {
        return;
    }
    let opacityValue = parseInt(slider.value);
    if (isNaN(opacityValue)) {
        return;
    }
    opacityValue = Math.max(0, Math.min(100, opacityValue));
    let layer = imageEditingTabEditor.activeLayer;
    layer.opacity = opacityValue / 100;
    layer.canvas.style.opacity = layer.opacity;
    imageEditingTabEditor.markOutputChanged();
    imageEditingTabEditor.queueSceneRedraw();
    imageEditingRefreshLayerOpacityControl();
}

function imageEditingGetLayerAdjustmentDefinition(key) {
    return imageEditingLayerAdjustmentDefinitions.find(def => def.key == key) || null;
}

function imageEditingGetLayerContextText(activeLayer) {
    if (!activeLayer) {
        return 'No active layer selected';
    }
    if (typeof activeLayer.getTypeLabel == 'function') {
        return `Active Layer: ${activeLayer.getTypeLabel()}`;
    }
    if (activeLayer.layerType == 'adjustment') {
        return 'Active Layer: Adjustment';
    }
    return `Active Layer: ${activeLayer.isMask ? 'Mask' : 'Image'}`;
}

function imageEditingEnsureLayerAdjustmentDefaults(layer) {
    if (!layer) {
        return;
    }
    for (let def of imageEditingLayerAdjustmentDefinitions) {
        let value = parseFloat(layer[def.property]);
        if (isNaN(value)) {
            value = def.defaultValue;
        }
        let min = Math.min(def.sliderToProperty(def.sliderMin), def.sliderToProperty(def.sliderMax));
        let max = Math.max(def.sliderToProperty(def.sliderMin), def.sliderToProperty(def.sliderMax));
        layer[def.property] = Math.max(min, Math.min(max, value));
    }
}

function imageEditingSetActiveLayerAdjustmentFromSliderKey(key) {
    if (!imageEditingTabEditor || !imageEditingTabEditor.activeLayer) {
        return;
    }
    let def = imageEditingGetLayerAdjustmentDefinition(key);
    let slider = imageEditingGetLayerAdjustmentSlider(key);
    if (!def || !slider) {
        return;
    }
    let sliderValue = parseInt(slider.value);
    if (isNaN(sliderValue)) {
        return;
    }
    sliderValue = Math.max(def.sliderMin, Math.min(def.sliderMax, sliderValue));
    let layer = imageEditingTabEditor.activeLayer;
    layer[def.property] = def.sliderToProperty(sliderValue);
    imageEditingTabEditor.markOutputChanged();
    imageEditingTabEditor.queueSceneRedraw();
    imageEditingRefreshLayerAdjustmentControls();
}

function imageEditingRefreshLayerAdjustmentControl(key) {
    let def = imageEditingGetLayerAdjustmentDefinition(key);
    let slider = imageEditingGetLayerAdjustmentSlider(key);
    let valueLabel = imageEditingGetLayerAdjustmentValueLabel(key);
    if (!def || !slider || !valueLabel) {
        return;
    }
    let contextLabel = def.contextId ? document.getElementById(def.contextId) : null;
    let activeLayer = imageEditingTabEditor ? imageEditingTabEditor.activeLayer : null;
    if (!activeLayer) {
        slider.disabled = true;
        slider.value = `${def.sliderDefault}`;
        valueLabel.innerText = 'N/A';
        if (contextLabel) {
            contextLabel.innerText = 'No active layer selected';
        }
        updateRangeStyle(slider);
        return;
    }
    imageEditingEnsureLayerAdjustmentDefaults(activeLayer);
    let sliderValue = def.propertyToSlider(activeLayer[def.property]);
    sliderValue = Math.max(def.sliderMin, Math.min(def.sliderMax, sliderValue));
    slider.disabled = false;
    slider.value = `${sliderValue}`;
    valueLabel.innerText = def.format(sliderValue);
    if (contextLabel) {
        contextLabel.innerText = imageEditingGetLayerContextText(activeLayer);
    }
    updateRangeStyle(slider);
}

function imageEditingRefreshLayerAdjustmentControls() {
    for (let def of imageEditingLayerAdjustmentDefinitions) {
        imageEditingRefreshLayerAdjustmentControl(def.key);
    }
    imageEditingRefreshToneBalanceControls();
}

/**
 * Ensures a layer has complete tone-balance defaults.
 */
function imageEditingEnsureLayerToneBalanceDefaults(layer) {
    if (!layer) {
        return;
    }
    if (!layer.toneBalance || typeof layer.toneBalance != 'object') {
        layer.toneBalance = {};
    }
    for (let range of imageEditingToneBalanceRanges) {
        if (!layer.toneBalance[range] || typeof layer.toneBalance[range] != 'object') {
            layer.toneBalance[range] = {};
        }
        for (let channel of imageEditingToneBalanceChannels) {
            let value = parseFloat(layer.toneBalance[range][channel]);
            if (isNaN(value)) {
                value = 0;
            }
            layer.toneBalance[range][channel] = Math.max(-1, Math.min(1, value));
        }
    }
}

/**
 * Formats a signed percent for tone-balance labels.
 */
function imageEditingFormatSignedPercent(value) {
    if (value > 0) {
        return `+${value}%`;
    }
    return `${value}%`;
}

/**
 * Applies a tone-balance slider value to the selected layer.
 */
function imageEditingSetActiveLayerToneBalanceFromSlider(slider) {
    if (!slider || !imageEditingTabEditor || !imageEditingTabEditor.activeLayer) {
        return;
    }
    let range = slider.dataset.range;
    let channel = slider.dataset.channel;
    if (!range || !channel) {
        return;
    }
    let sliderValue = parseInt(slider.value);
    if (isNaN(sliderValue)) {
        return;
    }
    sliderValue = Math.max(-100, Math.min(100, sliderValue));
    let layer = imageEditingTabEditor.activeLayer;
    imageEditingEnsureLayerToneBalanceDefaults(layer);
    if (!layer.toneBalance[range]) {
        return;
    }
    layer.toneBalance[range][channel] = sliderValue / 100;
    imageEditingTabEditor.markOutputChanged();
    imageEditingTabEditor.queueSceneRedraw();
    imageEditingRefreshToneBalanceControls();
}

/**
 * Refreshes tone-balance controls for the currently selected layer.
 */
function imageEditingRefreshToneBalanceControls() {
    let sliders = imageEditingGetToneBalanceSliders();
    if (sliders.length == 0) {
        return;
    }
    let activeLayer = imageEditingTabEditor ? imageEditingTabEditor.activeLayer : null;
    let contextLabel = imageEditingGetToneBalanceContext();
    if (!activeLayer) {
        for (let slider of sliders) {
            slider.disabled = true;
            slider.value = '0';
            let valueLabel = imageEditingGetToneBalanceValueLabel(slider.dataset.range, slider.dataset.channel);
            if (valueLabel) {
                valueLabel.innerText = 'N/A';
            }
            updateRangeStyle(slider);
        }
        if (contextLabel) {
            contextLabel.innerText = 'No active layer selected';
        }
        return;
    }
    imageEditingEnsureLayerToneBalanceDefaults(activeLayer);
    for (let slider of sliders) {
        let range = slider.dataset.range;
        let channel = slider.dataset.channel;
        if (!range || !channel || !activeLayer.toneBalance[range]) {
            slider.disabled = true;
            continue;
        }
        let rawValue = parseFloat(activeLayer.toneBalance[range][channel]);
        if (isNaN(rawValue)) {
            rawValue = 0;
        }
        rawValue = Math.max(-1, Math.min(1, rawValue));
        let percentValue = Math.round(rawValue * 100);
        slider.disabled = false;
        slider.value = `${percentValue}`;
        let valueLabel = imageEditingGetToneBalanceValueLabel(range, channel);
        if (valueLabel) {
            valueLabel.innerText = imageEditingFormatSignedPercent(percentValue);
        }
        updateRangeStyle(slider);
    }
    if (contextLabel) {
        contextLabel.innerText = imageEditingGetLayerContextText(activeLayer);
    }
}

/**
 * Applies the current layer's blend mode from the Layer Options select.
 */
function imageEditingSetActiveLayerBlendModeFromSelect() {
    if (!imageEditingTabEditor || !imageEditingTabEditor.activeLayer) {
        return;
    }
    let select = imageEditingGetLayerBlendModeSelect();
    if (!select) {
        return;
    }
    let layer = imageEditingTabEditor.activeLayer;
    layer.globalCompositeOperation = select.value || 'source-over';
    imageEditingTabEditor.markOutputChanged();
    imageEditingTabEditor.queueSceneRedraw();
    imageEditingRefreshLayerBlendModeControl();
}

/**
 * Refreshes the current layer blend-mode control.
 */
function imageEditingRefreshLayerBlendModeControl() {
    let select = imageEditingGetLayerBlendModeSelect();
    let contextLabel = imageEditingGetLayerBlendModeContext();
    if (!select || !contextLabel) {
        return;
    }
    let activeLayer = imageEditingTabEditor ? imageEditingTabEditor.activeLayer : null;
    if (!activeLayer) {
        select.disabled = true;
        select.value = 'source-over';
        contextLabel.innerText = 'No active layer selected';
        return;
    }
    if (!activeLayer.globalCompositeOperation) {
        activeLayer.globalCompositeOperation = 'source-over';
    }
    select.disabled = false;
    select.value = activeLayer.globalCompositeOperation;
    contextLabel.innerText = imageEditingGetLayerContextText(activeLayer);
}

/**
 * Refreshes Layer Options controls for the currently selected layer.
 */
function imageEditingRefreshLayerOpacityControl() {
    let slider = imageEditingGetLayerOpacitySlider();
    let valueLabel = imageEditingGetLayerOpacityValue();
    let contextLabel = imageEditingGetLayerOpacityContext();
    if (!slider || !valueLabel || !contextLabel) {
        return;
    }
    let activeLayer = imageEditingTabEditor ? imageEditingTabEditor.activeLayer : null;
    if (!activeLayer) {
        slider.disabled = true;
        slider.value = '100';
        valueLabel.innerText = 'N/A';
        contextLabel.innerText = 'No active layer selected';
        updateRangeStyle(slider);
        imageEditingRefreshLayerOptionActionButtons();
        imageEditingRefreshLayerBlendModeControl();
        imageEditingRefreshLayerAdjustmentControls();
        imageEditingRefreshCropControls();
        imageEditingRefreshEffectControls();
        return;
    }
    let opacity = 1;
    if (typeof activeLayer.opacity == 'number') {
        opacity = activeLayer.opacity;
    }
    opacity = Math.max(0, Math.min(1, opacity));
    let percentOpacity = Math.round(opacity * 100);
    slider.disabled = false;
    slider.value = `${percentOpacity}`;
    valueLabel.innerText = `${percentOpacity}%`;
    contextLabel.innerText = imageEditingGetLayerContextText(activeLayer);
    updateRangeStyle(slider);
    imageEditingRefreshLayerOptionActionButtons();
    imageEditingRefreshLayerBlendModeControl();
    imageEditingRefreshLayerAdjustmentControls();
    imageEditingRefreshCropControls();
    imageEditingRefreshEffectControls();
}

/**
 * Wires Layer Options controls for the Image Editing tab.
 */
function imageEditingEnsureLayerOptionsWired() {
    if (imageEditingLayerOptionsWired) {
        return;
    }
    let slider = imageEditingGetLayerOpacitySlider();
    let blendModeSelect = imageEditingGetLayerBlendModeSelect();
    let toneBalanceSliders = imageEditingGetToneBalanceSliders();
    let deleteButton = imageEditingGetLayerDeleteButton();
    let duplicateButton = imageEditingGetLayerDuplicateButton();
    let convertToImageButton = imageEditingGetLayerConvertToImageButton();
    let invertMaskButton = imageEditingGetLayerInvertMaskButton();
    let convertToMaskButton = imageEditingGetLayerConvertToMaskButton();
    let invertColorsButton = imageEditingGetLayerInvertColorsButton();
    let flipMirrorHorizontalButton = imageEditingGetLayerFlipMirrorHorizontalButton();
    let flipMirrorVerticalButton = imageEditingGetLayerFlipMirrorVerticalButton();
    if (!slider || !blendModeSelect || !deleteButton || !duplicateButton || !convertToImageButton || !invertMaskButton || !convertToMaskButton || !invertColorsButton || !flipMirrorHorizontalButton || !flipMirrorVerticalButton) {
        return;
    }
    for (let def of imageEditingLayerAdjustmentDefinitions) {
        if (!imageEditingGetLayerAdjustmentSlider(def.key) || !imageEditingGetLayerAdjustmentValueLabel(def.key)) {
            return;
        }
    }
    slider.addEventListener('input', () => {
        imageEditingSetActiveLayerOpacityFromSlider();
    });
    slider.addEventListener('change', () => {
        imageEditingSetActiveLayerOpacityFromSlider();
    });
    blendModeSelect.addEventListener('change', () => {
        imageEditingSetActiveLayerBlendModeFromSelect();
    });
    for (let def of imageEditingLayerAdjustmentDefinitions) {
        let adjustmentSlider = imageEditingGetLayerAdjustmentSlider(def.key);
        adjustmentSlider.addEventListener('input', () => {
            imageEditingSetActiveLayerAdjustmentFromSliderKey(def.key);
        });
        adjustmentSlider.addEventListener('change', () => {
            imageEditingSetActiveLayerAdjustmentFromSliderKey(def.key);
        });
    }
    for (let toneSlider of toneBalanceSliders) {
        toneSlider.addEventListener('input', () => {
            imageEditingSetActiveLayerToneBalanceFromSlider(toneSlider);
        });
        toneSlider.addEventListener('change', () => {
            imageEditingSetActiveLayerToneBalanceFromSlider(toneSlider);
        });
    }
    deleteButton.addEventListener('click', () => {
        imageEditingDeleteActiveLayer();
    });
    duplicateButton.addEventListener('click', () => {
        imageEditingDuplicateActiveLayer();
    });
    convertToImageButton.addEventListener('click', () => {
        imageEditingConvertActiveLayerToImage();
    });
    invertMaskButton.addEventListener('click', () => {
        imageEditingInvertActiveLayerMask();
    });
    convertToMaskButton.addEventListener('click', () => {
        imageEditingConvertActiveLayerToMask();
    });
    invertColorsButton.addEventListener('click', () => {
        imageEditingInvertActiveLayerColors();
    });
    flipMirrorHorizontalButton.addEventListener('click', () => {
        imageEditingFlipMirrorActiveLayerHorizontal();
    });
    flipMirrorVerticalButton.addEventListener('click', () => {
        imageEditingFlipMirrorActiveLayerVertical();
    });
    imageEditingLayerOptionsWired = true;
    imageEditingRefreshLayerOpacityControl();
}

function imageEditingEnsureLayerEffectsDefaults(layer) {
    if (!layer) {
        return;
    }
    if (typeof ImageEditorLayer != 'undefined' && typeof ImageEditorLayer.cloneEffects == 'function') {
        layer.effects = ImageEditorLayer.cloneEffects(layer.effects);
    }
    else if (!layer.effects || typeof layer.effects != 'object') {
        layer.effects = { blur: 0, sharpen: 0, noiseReduction: 0, artisticFilter: 'none', vignette: 0, glow: 0 };
    }
    if (!layer.effectPresetId) {
        layer.effectPresetId = 'neutral';
    }
}

function imageEditingRefreshSelectionControls() {
    if (!imageEditingTabEditor) {
        return;
    }
    let selectionMode = document.getElementById('imageediting_selection_mode_select');
    let toleranceSlider = document.getElementById('imageediting_selection_tolerance_slider');
    let toleranceValue = document.getElementById('imageediting_selection_tolerance_value');
    let sampleSource = document.getElementById('imageediting_selection_sample_source_select');
    let contiguousToggle = document.getElementById('imageediting_selection_contiguous_toggle');
    let featherSlider = document.getElementById('imageediting_selection_feather_slider');
    let featherValue = document.getElementById('imageediting_selection_feather_value');
    let expandSlider = document.getElementById('imageediting_selection_expand_slider');
    let expandValue = document.getElementById('imageediting_selection_expand_value');
    let smoothSlider = document.getElementById('imageediting_selection_smooth_slider');
    let smoothValue = document.getElementById('imageediting_selection_smooth_value');
    let clearSelectionButton = document.getElementById('imageediting_clear_selection_button');
    if (!selectionMode || !toleranceSlider || !sampleSource || !contiguousToggle || !featherSlider || !expandSlider || !smoothSlider || !clearSelectionButton) {
        return;
    }
    selectionMode.value = imageEditingTabEditor.selectionMode || 'replace';
    toleranceSlider.value = `${Math.round(imageEditingTabEditor.selectionTolerance || 0)}`;
    toleranceValue.innerText = toleranceSlider.value;
    sampleSource.value = imageEditingTabEditor.selectionSampleSource || 'composite';
    contiguousToggle.checked = !!imageEditingTabEditor.selectionContiguous;
    featherSlider.value = `${Math.round(imageEditingTabEditor.selectionFeatherPx || 0)}`;
    featherValue.innerText = `${featherSlider.value}px`;
    expandSlider.value = `${Math.round(imageEditingTabEditor.selectionExpandPx || 0)}`;
    expandValue.innerText = `${expandSlider.value}px`;
    smoothSlider.value = `${Math.round(imageEditingTabEditor.selectionSmoothPasses || 0)}`;
    smoothValue.innerText = smoothSlider.value;
    updateRangeStyle(toleranceSlider);
    updateRangeStyle(featherSlider);
    updateRangeStyle(expandSlider);
    updateRangeStyle(smoothSlider);
    clearSelectionButton.disabled = !imageEditingTabEditor.hasSelectionMask();
}

function imageEditingReapplyCurrentSelectionMask() {
    if (!imageEditingTabEditor || !imageEditingTabEditor.hasSelectionMask()) {
        return;
    }
    if (typeof imageEditingTabEditor.rebuildSelectionMaskFromSource == 'function') {
        imageEditingTabEditor.rebuildSelectionMaskFromSource(true);
    }
}

function imageEditingRefreshCropControls() {
    let widthInput = document.getElementById('imageediting_crop_display_width');
    let heightInput = document.getElementById('imageediting_crop_display_height');
    let commitButton = document.getElementById('imageediting_crop_commit_button');
    let cancelButton = document.getElementById('imageediting_crop_cancel_button');
    let resetButton = document.getElementById('imageediting_crop_reset_button');
    if (!widthInput || !heightInput || !commitButton || !cancelButton || !resetButton) {
        return;
    }
    let activeLayer = imageEditingTabEditor ? imageEditingTabEditor.activeLayer : null;
    let disable = !activeLayer || activeLayer.layerType == 'adjustment';
    widthInput.disabled = disable;
    heightInput.disabled = disable;
    commitButton.disabled = disable;
    cancelButton.disabled = disable;
    resetButton.disabled = disable;
    if (disable) {
        widthInput.value = '0';
        heightInput.value = '0';
        return;
    }
    let previewLayer = activeLayer;
    if (imageEditingTabEditor.previewState && imageEditingTabEditor.previewState.targetLayer == activeLayer) {
        previewLayer = imageEditingTabEditor.previewState.previewLayer;
    }
    widthInput.value = `${Math.round(previewLayer.width)}`;
    heightInput.value = `${Math.round(previewLayer.height)}`;
}

function imageEditingApplyActiveLayerEffectValue(key) {
    if (!imageEditingTabEditor || !imageEditingTabEditor.activeLayer || imageEditingTabEditor.activeLayer.isMask) {
        return;
    }
    let def = imageEditingEffectDefinitions.find(effect => effect.key == key);
    if (!def) {
        return;
    }
    imageEditingEnsureLayerEffectsDefaults(imageEditingTabEditor.activeLayer);
    let slider = document.getElementById(def.sliderId);
    if (!slider) {
        return;
    }
    let sliderValue = parseInt(slider.value);
    if (isNaN(sliderValue)) {
        return;
    }
    imageEditingTabEditor.activeLayer.effects[key] = def.sliderToProperty(sliderValue);
    imageEditingTabEditor.activeLayer.markVisualChanged();
    imageEditingTabEditor.markOutputChanged();
    imageEditingTabEditor.queueSceneRedraw();
    imageEditingRefreshEffectControls();
}

function imageEditingRefreshEffectControls() {
    let presetSelect = document.getElementById('imageediting_effect_preset_select');
    let artisticSelect = document.getElementById('imageediting_artistic_filter_select');
    let newAdjustmentButton = document.getElementById('imageediting_new_adjustment_layer_button');
    let editMaskButton = document.getElementById('imageediting_edit_adjustment_mask_button');
    let toggleMaskButton = document.getElementById('imageediting_toggle_adjustment_mask_button');
    if (!presetSelect || !artisticSelect || !newAdjustmentButton || !editMaskButton || !toggleMaskButton) {
        return;
    }
    let activeLayer = imageEditingTabEditor ? imageEditingTabEditor.activeLayer : null;
    let canEditEffects = !!(activeLayer && !activeLayer.isMask);
    newAdjustmentButton.disabled = !imageEditingTabEditor;
    editMaskButton.disabled = !(activeLayer && activeLayer.layerType == 'adjustment');
    toggleMaskButton.disabled = !(activeLayer && activeLayer.layerType == 'adjustment');
    presetSelect.disabled = !canEditEffects;
    artisticSelect.disabled = !canEditEffects;
    if (toggleMaskButton.disabled) {
        toggleMaskButton.innerText = 'Hide Adjustment Mask';
    }
    else {
        toggleMaskButton.innerText = imageEditingTabEditor.showAdjustmentMaskOverlay ? 'Hide Adjustment Mask' : 'Show Adjustment Mask';
    }
    if (!canEditEffects) {
        presetSelect.value = 'neutral';
        artisticSelect.value = 'none';
    }
    else {
        imageEditingEnsureLayerEffectsDefaults(activeLayer);
        presetSelect.value = activeLayer.effectPresetId || 'neutral';
        artisticSelect.value = activeLayer.effects.artisticFilter || 'none';
    }
    for (let def of imageEditingEffectDefinitions) {
        let slider = document.getElementById(def.sliderId);
        let label = document.getElementById(def.labelKey);
        if (!slider || !label) {
            continue;
        }
        slider.disabled = !canEditEffects;
        if (!canEditEffects) {
            slider.value = `${def.propertyToSlider(def.defaultValue)}`;
            label.innerText = def.format(parseInt(slider.value));
            updateRangeStyle(slider);
            continue;
        }
        let rawValue = activeLayer.effects[def.key];
        if (typeof rawValue != 'number') {
            rawValue = def.defaultValue;
        }
        slider.value = `${def.propertyToSlider(rawValue)}`;
        label.innerText = def.format(parseInt(slider.value));
        updateRangeStyle(slider);
    }
}

function imageEditingEnsureSelectionEffectsControlsWired() {
    if (imageEditingSelectionEffectsWired) {
        return;
    }
    let selectionMode = document.getElementById('imageediting_selection_mode_select');
    let toleranceSlider = document.getElementById('imageediting_selection_tolerance_slider');
    let sampleSource = document.getElementById('imageediting_selection_sample_source_select');
    let contiguousToggle = document.getElementById('imageediting_selection_contiguous_toggle');
    let featherSlider = document.getElementById('imageediting_selection_feather_slider');
    let expandSlider = document.getElementById('imageediting_selection_expand_slider');
    let smoothSlider = document.getElementById('imageediting_selection_smooth_slider');
    let widthInput = document.getElementById('imageediting_crop_display_width');
    let heightInput = document.getElementById('imageediting_crop_display_height');
    let commitButton = document.getElementById('imageediting_crop_commit_button');
    let cancelButton = document.getElementById('imageediting_crop_cancel_button');
    let resetButton = document.getElementById('imageediting_crop_reset_button');
    let presetSelect = document.getElementById('imageediting_effect_preset_select');
    let artisticSelect = document.getElementById('imageediting_artistic_filter_select');
    let newAdjustmentButton = document.getElementById('imageediting_new_adjustment_layer_button');
    let editMaskButton = document.getElementById('imageediting_edit_adjustment_mask_button');
    let clearSelectionButton = document.getElementById('imageediting_clear_selection_button');
    let toggleMaskButton = document.getElementById('imageediting_toggle_adjustment_mask_button');
    if (!selectionMode || !toleranceSlider || !sampleSource || !contiguousToggle || !featherSlider || !expandSlider || !smoothSlider || !widthInput || !heightInput || !commitButton || !cancelButton || !resetButton || !presetSelect || !artisticSelect || !newAdjustmentButton || !editMaskButton || !clearSelectionButton || !toggleMaskButton) {
        return;
    }
    selectionMode.addEventListener('change', () => {
        imageEditingTabEditor.selectionMode = selectionMode.value;
        imageEditingRefreshSelectionControls();
    });
    toleranceSlider.addEventListener('input', () => {
        imageEditingTabEditor.selectionTolerance = parseInt(toleranceSlider.value) || 0;
        imageEditingRefreshSelectionControls();
    });
    toleranceSlider.addEventListener('change', () => {
        imageEditingTabEditor.selectionTolerance = parseInt(toleranceSlider.value) || 0;
        imageEditingRefreshSelectionControls();
    });
    sampleSource.addEventListener('change', () => {
        imageEditingTabEditor.selectionSampleSource = sampleSource.value;
        imageEditingRefreshSelectionControls();
    });
    contiguousToggle.addEventListener('change', () => {
        imageEditingTabEditor.selectionContiguous = contiguousToggle.checked;
        imageEditingRefreshSelectionControls();
    });
    featherSlider.addEventListener('input', () => {
        imageEditingTabEditor.selectionFeatherPx = parseInt(featherSlider.value) || 0;
        imageEditingRefreshSelectionControls();
    });
    featherSlider.addEventListener('change', () => {
        imageEditingTabEditor.selectionFeatherPx = parseInt(featherSlider.value) || 0;
        imageEditingReapplyCurrentSelectionMask();
        imageEditingRefreshSelectionControls();
    });
    expandSlider.addEventListener('input', () => {
        imageEditingTabEditor.selectionExpandPx = parseInt(expandSlider.value) || 0;
        imageEditingRefreshSelectionControls();
    });
    expandSlider.addEventListener('change', () => {
        imageEditingTabEditor.selectionExpandPx = parseInt(expandSlider.value) || 0;
        imageEditingReapplyCurrentSelectionMask();
        imageEditingRefreshSelectionControls();
    });
    smoothSlider.addEventListener('input', () => {
        imageEditingTabEditor.selectionSmoothPasses = parseInt(smoothSlider.value) || 0;
        imageEditingRefreshSelectionControls();
    });
    smoothSlider.addEventListener('change', () => {
        imageEditingTabEditor.selectionSmoothPasses = parseInt(smoothSlider.value) || 0;
        imageEditingReapplyCurrentSelectionMask();
        imageEditingRefreshSelectionControls();
    });
    clearSelectionButton.addEventListener('click', () => {
        if (!imageEditingTabEditor) {
            return;
        }
        imageEditingTabEditor.clearSelectionMask(true);
        imageEditingRefreshSelectionControls();
    });
    widthInput.addEventListener('change', () => {
        if (!imageEditingTabEditor || !imageEditingTabEditor.activeLayer) {
            return;
        }
        imageEditingTabEditor.setLayerDisplaySize(imageEditingTabEditor.activeLayer, parseInt(widthInput.value) || imageEditingTabEditor.activeLayer.width, parseInt(heightInput.value) || imageEditingTabEditor.activeLayer.height);
        imageEditingRefreshCropControls();
    });
    heightInput.addEventListener('change', () => {
        if (!imageEditingTabEditor || !imageEditingTabEditor.activeLayer) {
            return;
        }
        imageEditingTabEditor.setLayerDisplaySize(imageEditingTabEditor.activeLayer, parseInt(widthInput.value) || imageEditingTabEditor.activeLayer.width, parseInt(heightInput.value) || imageEditingTabEditor.activeLayer.height);
        imageEditingRefreshCropControls();
    });
    commitButton.addEventListener('click', () => {
        if (!imageEditingTabEditor) {
            return;
        }
        imageEditingTabEditor.commitCropSession();
        imageEditingRefreshCropControls();
    });
    cancelButton.addEventListener('click', () => {
        if (!imageEditingTabEditor) {
            return;
        }
        imageEditingTabEditor.cancelCropSession();
        imageEditingRefreshCropControls();
    });
    resetButton.addEventListener('click', () => {
        if (!imageEditingTabEditor || !imageEditingTabEditor.activeLayer) {
            return;
        }
        imageEditingTabEditor.resetCropForLayer(imageEditingTabEditor.activeLayer);
        imageEditingRefreshCropControls();
    });
    presetSelect.addEventListener('change', () => {
        if (!imageEditingTabEditor || !imageEditingTabEditor.activeLayer || imageEditingTabEditor.activeLayer.isMask) {
            return;
        }
        imageEditingEnsureLayerEffectsDefaults(imageEditingTabEditor.activeLayer);
        imageEditingTabEditor.activeLayer.effectPresetId = presetSelect.value;
        imageEditingTabEditor.activeLayer.markVisualChanged();
        imageEditingTabEditor.markOutputChanged();
        imageEditingTabEditor.queueSceneRedraw();
        imageEditingRefreshEffectControls();
    });
    artisticSelect.addEventListener('change', () => {
        if (!imageEditingTabEditor || !imageEditingTabEditor.activeLayer || imageEditingTabEditor.activeLayer.isMask) {
            return;
        }
        imageEditingEnsureLayerEffectsDefaults(imageEditingTabEditor.activeLayer);
        imageEditingTabEditor.activeLayer.effects.artisticFilter = artisticSelect.value;
        imageEditingTabEditor.activeLayer.markVisualChanged();
        imageEditingTabEditor.markOutputChanged();
        imageEditingTabEditor.queueSceneRedraw();
        imageEditingRefreshEffectControls();
    });
    for (let def of imageEditingEffectDefinitions) {
        let slider = document.getElementById(def.sliderId);
        if (!slider) {
            continue;
        }
        slider.addEventListener('input', () => {
            imageEditingApplyActiveLayerEffectValue(def.key);
        });
        slider.addEventListener('change', () => {
            imageEditingApplyActiveLayerEffectValue(def.key);
        });
    }
    newAdjustmentButton.addEventListener('click', () => {
        if (!imageEditingTabEditor) {
            return;
        }
        imageEditingTabEditor.addEmptyAdjustmentLayer();
        imageEditingRefreshLayerOpacityControl();
        imageEditingRefreshEffectControls();
    });
    editMaskButton.addEventListener('click', () => {
        if (!imageEditingTabEditor || !imageEditingTabEditor.activeLayer || imageEditingTabEditor.activeLayer.layerType != 'adjustment') {
            return;
        }
        imageEditingTabEditor.activateTool('brush');
        imageEditingRefreshToolButtons();
    });
    toggleMaskButton.addEventListener('click', () => {
        if (!imageEditingTabEditor || !imageEditingTabEditor.activeLayer || imageEditingTabEditor.activeLayer.layerType != 'adjustment') {
            return;
        }
        imageEditingTabEditor.showAdjustmentMaskOverlay = !imageEditingTabEditor.showAdjustmentMaskOverlay;
        imageEditingTabEditor.queueSceneRedraw();
        imageEditingRefreshEffectControls();
    });
    imageEditingSelectionEffectsWired = true;
    imageEditingRefreshSelectionControls();
    imageEditingRefreshCropControls();
    imageEditingRefreshEffectControls();
}

/**
 * Sets collapsed/expanded state for an Image Editing input section.
 */
function imageEditingSetInputSectionCollapsed(section, collapsed, save = true) {
    let key = null;
    let body = null;
    let header = null;
    let marker = null;
    if (section == 'tools') {
        imageEditingToolsCollapsed = collapsed;
        key = 'imageediting_toolsCollapsed';
        body = imageEditingGetToolButtonsArea();
        header = imageEditingGetToolsHeader();
        marker = imageEditingGetToolsToggleState();
    }
    else if (section == 'pen_options') {
        imageEditingPenOptionsCollapsed = collapsed;
        key = 'imageediting_penOptionsCollapsed';
        body = imageEditingGetPenOptionsBody();
        header = imageEditingGetPenOptionsHeader();
        marker = imageEditingGetPenOptionsToggleState();
    }
    else if (section == 'actions') {
        imageEditingActionsCollapsed = collapsed;
        key = 'imageediting_actionsCollapsed';
        body = imageEditingGetOptionButtonsArea();
        header = imageEditingGetActionsHeader();
        marker = imageEditingGetActionsToggleState();
    }
    else if (section == 'layer_options') {
        imageEditingLayerOptionsCollapsed = collapsed;
        key = 'imageediting_layerOptionsCollapsed';
        body = imageEditingGetLayerOptionsBody();
        header = imageEditingGetLayerOptionsHeader();
        marker = imageEditingGetLayerOptionsToggleState();
    }
    else if (section == 'image_options') {
        imageEditingImageOptionsCollapsed = collapsed;
        key = 'imageediting_imageOptionsCollapsed';
        body = imageEditingGetImageOptionsBody();
        header = imageEditingGetImageOptionsHeader();
        marker = imageEditingGetImageOptionsToggleState();
    }
    else if (section == 'selection_crop') {
        imageEditingSelectionCropCollapsed = collapsed;
        key = 'imageediting_selectionCropCollapsed';
        body = imageEditingGetSelectionCropBody();
        header = imageEditingGetSelectionCropHeader();
        marker = imageEditingGetSelectionCropToggleState();
    }
    else if (section == 'effects_presets') {
        imageEditingEffectsPresetsCollapsed = collapsed;
        key = 'imageediting_effectsPresetsCollapsed';
        body = imageEditingGetEffectsPresetsBody();
        header = imageEditingGetEffectsPresetsHeader();
        marker = imageEditingGetEffectsPresetsToggleState();
    }
    else {
        return;
    }
    if (save && key) {
        localStorage.setItem(key, `${collapsed}`);
    }
    if (body) {
        body.style.display = collapsed ? 'none' : '';
    }
    if (header) {
        header.classList.toggle('imageediting_section_header_collapsed', collapsed);
    }
    if (marker) {
        marker.innerText = collapsed ? '+' : '-';
    }
}

/**
 * Applies current section-collapse state to the Image Editing input sections.
 */
function imageEditingApplyInputSectionState() {
    imageEditingSetInputSectionCollapsed('tools', imageEditingToolsCollapsed, false);
    imageEditingSetInputSectionCollapsed('pen_options', imageEditingPenOptionsCollapsed, false);
    imageEditingSetInputSectionCollapsed('actions', imageEditingActionsCollapsed, false);
    imageEditingSetInputSectionCollapsed('layer_options', imageEditingLayerOptionsCollapsed, false);
    imageEditingSetInputSectionCollapsed('image_options', imageEditingImageOptionsCollapsed, false);
    imageEditingSetInputSectionCollapsed('selection_crop', imageEditingSelectionCropCollapsed, false);
    imageEditingSetInputSectionCollapsed('effects_presets', imageEditingEffectsPresetsCollapsed, false);
}

/**
 * Toggles collapse state for an Image Editing input section.
 */
function imageEditingToggleInputSection(section) {
    if (section == 'tools') {
        imageEditingSetInputSectionCollapsed(section, !imageEditingToolsCollapsed);
    }
    else if (section == 'pen_options') {
        imageEditingSetInputSectionCollapsed(section, !imageEditingPenOptionsCollapsed);
    }
    else if (section == 'actions') {
        imageEditingSetInputSectionCollapsed(section, !imageEditingActionsCollapsed);
    }
    else if (section == 'layer_options') {
        imageEditingSetInputSectionCollapsed(section, !imageEditingLayerOptionsCollapsed);
    }
    else if (section == 'image_options') {
        imageEditingSetInputSectionCollapsed(section, !imageEditingImageOptionsCollapsed);
    }
    else if (section == 'selection_crop') {
        imageEditingSetInputSectionCollapsed(section, !imageEditingSelectionCropCollapsed);
    }
    else if (section == 'effects_presets') {
        imageEditingSetInputSectionCollapsed(section, !imageEditingEffectsPresetsCollapsed);
    }
}

/**
 * Clamps a requested zoom level to allowed bounds.
 */
function imageEditingClampZoom(level) {
    return Math.min(imageEditingZoomMax, Math.max(imageEditingZoomMin, level));
}

/**
 * Applies current zoom state to the Image Editing editor.
 */
function imageEditingApplyZoom() {
    if (imageEditingTabEditor && imageEditingTabEditor.canvas) {
        imageEditingZoomLevel = imageEditingTabEditor.zoomLevel;
    }
    let zoomText = imageEditingGetZoomText();
    if (zoomText) {
        zoomText.innerText = `${Math.round(imageEditingZoomLevel * 100)}%`;
    }
}

/**
 * Sets absolute zoom level for the Image Editing editor.
 */
function imageEditingSetZoom(level) {
    imageEditingZoomLevel = imageEditingClampZoom(level);
    if (imageEditingTabEditor && imageEditingTabEditor.canvas) {
        imageEditingTabEditor.zoomLevel = imageEditingZoomLevel;
        imageEditingTabEditor.redraw();
    }
    imageEditingApplyZoom();
}

/**
 * Zooms in on the Image Editing editor.
 */
function imageEditingZoomIn() {
    imageEditingSetZoom(imageEditingZoomLevel * 1.25);
}

/**
 * Zooms out on the Image Editing editor.
 */
function imageEditingZoomOut() {
    imageEditingSetZoom(imageEditingZoomLevel / 1.25);
}

/**
 * Resets Image Editing editor zoom to 100%.
 */
function imageEditingZoomReset() {
    imageEditingSetZoom(1);
}

/**
 * Parses and applies a hex color to the inline picker state.
 */
function imageEditingInlinePickerSetColor(newColor) {
    if (!imageEditingInlineColorPicker) {
        return;
    }
    let rgb = imageEditingInlineColorPicker.hexToRgb(newColor);
    imageEditingInlineColorPicker.currentR = rgb.r;
    imageEditingInlineColorPicker.currentG = rgb.g;
    imageEditingInlineColorPicker.currentB = rgb.b;
    let hsv = imageEditingInlineColorPicker.rgbToHsv(rgb.r, rgb.g, rgb.b);
    imageEditingInlineColorPicker.currentH = hsv.h;
    imageEditingInlineColorPicker.currentS = hsv.s;
    imageEditingInlineColorPicker.currentV = hsv.v;
    imageEditingInlineColorPicker.refreshUI();
}

/**
 * Sets the selected Image Editing color and updates UI.
 */
function imageEditingSetColor(newColor) {
    imageEditingColor = newColor;
    let colorText = imageEditingGetColorText();
    if (colorText) {
        colorText.value = newColor;
    }
    let colorSwatch = imageEditingGetColorSwatch();
    if (colorSwatch) {
        colorSwatch.style.backgroundColor = newColor;
    }
    if (imageEditingInlineColorPicker && imageEditingInlineColorPicker.getCurrentColor() != newColor) {
        imageEditingInlinePickerSetColor(newColor);
    }
    if (imageEditingTabEditor && imageEditingTabEditor.activeTool && typeof imageEditingTabEditor.activeTool.setColor == 'function') {
        imageEditingTabEditor.activeTool.setColor(newColor);
        imageEditingTabEditor.queueOverlayRedraw();
    }
}

/**
 * Ensures the persistent Image Editing color selector is wired.
 */
function imageEditingEnsureColorSelectorWired() {
    if (imageEditingColorWired) {
        return;
    }
    let colorText = imageEditingGetColorText();
    let colorSwatch = imageEditingGetColorSwatch();
    let colorPickerMount = imageEditingGetInlineColorPickerMount();
    if (!colorText || !colorSwatch || !colorPickerMount) {
        return;
    }
    colorText.readOnly = true;
    colorText.style.cursor = 'default';
    imageEditingInlineColorPicker = new ColorPickerHelper();
    imageEditingInlineColorPicker.container.classList.add('color-picker-inline');
    if (imageEditingInlineColorPicker.container.parentElement) {
        imageEditingInlineColorPicker.container.parentElement.removeChild(imageEditingInlineColorPicker.container);
    }
    colorPickerMount.appendChild(imageEditingInlineColorPicker.container);
    imageEditingInlineColorPicker.container.style.display = 'block';
    imageEditingInlineColorPicker.isOpen = true;
    imageEditingInlineColorPicker.anchorElement = document.body;
    imageEditingInlineColorPicker.close = () => {
    };
    imageEditingInlineColorPicker.okayButton.style.display = 'none';
    imageEditingInlineColorPicker.onChange = (newColor) => {
        imageEditingSetColor(newColor);
    };
    imageEditingInlinePickerSetColor(imageEditingColor);
    imageEditingSetColor(imageEditingColor);
    imageEditingColorWired = true;
}

/**
 * Ensures the full editor for the Image Editing tab is ready.
 */
function imageEditingEnsureEditorReady() {
    if (imageEditingTabEditor) {
        return;
    }
    let editorArea = imageEditingGetEditorArea();
    if (!editorArea) {
        return;
    }
    editorArea.innerHTML = '';
    imageEditingTabEditor = new ImageEditor(editorArea, true, true, () => {
    }, () => {
    });
    imageEditingTabEditor.doParamHides = () => {
    };
    imageEditingTabEditor.unhideParams = () => {
    };
    imageEditingTabEditor.leftBar.style.display = 'none';
    imageEditingTabEditor.rightResizeBar = null;
    let rightSidebarContent = imageEditingGetRightSidebarContent();
    if (rightSidebarContent) {
        rightSidebarContent.innerHTML = '';
        rightSidebarContent.appendChild(imageEditingTabEditor.rightBar);
    }
    let closeButton = imageEditingTabEditor.rightBar.querySelector('.image-editor-close-button');
    if (closeButton) {
        closeButton.classList.remove('interrupt-button');
        closeButton.innerText = 'Send To Generate Tab';
        closeButton.title = 'Sends the current Image Editing layers to the Generate tab editor';
        closeButton.onclick = null;
        closeButton.addEventListener('click', () => {
            sendImageEditingLayersToGenerateEditor();
        });
    }
    let optionButtons = imageEditingTabEditor.tools['options'].optionButtons;
    imageEditingTabEditor.tools['options'].optionButtons = [
        ...optionButtons,
        { key: 'Send Layers To Generate Editor', action: () => {
            sendImageEditingLayersToGenerateEditor();
        }},
        { key: 'Store Current Image To History', action: () => {
            let img = imageEditingTabEditor.getFinalImageData();
            storeImageToHistoryWithCurrentParams(img);
        }},
        { key: 'Store Full Canvas To History', action: () => {
            let img = imageEditingTabEditor.getMaximumImageData();
            storeImageToHistoryWithCurrentParams(img);
        }}
    ];
    let rawActivateTool = imageEditingTabEditor.activateTool.bind(imageEditingTabEditor);
    imageEditingTabEditor.activateTool = (toolId) => {
        rawActivateTool(toolId);
        imageEditingRefreshToolButtons();
        imageEditingRefreshCropControls();
    };
    let rawSetActiveLayer = imageEditingTabEditor.setActiveLayer.bind(imageEditingTabEditor);
    imageEditingTabEditor.setActiveLayer = (layer) => {
        rawSetActiveLayer(layer);
        imageEditingRefreshToolButtons();
        imageEditingRefreshLayerOpacityControl();
        imageEditingRefreshSelectionControls();
        imageEditingRefreshCropControls();
        imageEditingRefreshEffectControls();
    };
    let rawCommitSelectionMask = imageEditingTabEditor.commitSelectionMask.bind(imageEditingTabEditor);
    imageEditingTabEditor.commitSelectionMask = (maskCanvas, combineMode = null) => {
        rawCommitSelectionMask(maskCanvas, combineMode);
        imageEditingRefreshSelectionControls();
    };
    let rawClearSelectionMask = imageEditingTabEditor.clearSelectionMask.bind(imageEditingTabEditor);
    imageEditingTabEditor.clearSelectionMask = (queueOverlay = true) => {
        rawClearSelectionMask(queueOverlay);
        imageEditingRefreshSelectionControls();
    };
    if (typeof imageEditingTabEditor.rebuildSelectionMaskFromSource == 'function') {
        let rawRebuildSelectionMaskFromSource = imageEditingTabEditor.rebuildSelectionMaskFromSource.bind(imageEditingTabEditor);
        imageEditingTabEditor.rebuildSelectionMaskFromSource = (queueOverlay = true) => {
            rawRebuildSelectionMaskFromSource(queueOverlay);
            imageEditingRefreshSelectionControls();
        };
    }
    imageEditingBuildToolButtons();
    imageEditingBuildSelectionToolButtons();
    imageEditingBuildOptionButtons();
    imageEditingSetupPenOptions();
    imageEditingRefreshPenOptions();
    imageEditingRefreshLayerOpacityControl();
    imageEditingApplyRightSidebarWidth();
    let initialCanvas = document.createElement('canvas');
    initialCanvas.width = 512;
    initialCanvas.height = 512;
    let initialCtx = initialCanvas.getContext('2d');
    initialCtx.fillStyle = 'white';
    initialCtx.fillRect(0, 0, initialCanvas.width, initialCanvas.height);
    let initialImage = new Image();
    initialImage.onload = () => {
        if (!imageEditingTabEditor || imageEditingTabEditor.layers.length > 0) {
            return;
        }
        imageEditingTabEditor.clearVars();
        imageEditingTabEditor.setBaseImage(initialImage);
        imageEditingRefreshToolButtons();
        imageEditingRefreshLayerOpacityControl();
        imageEditingApplyZoom();
    };
    initialImage.src = initialCanvas.toDataURL();
}

/**
 * Ensures Image Editing tab UI controls are initialized.
 */
function imageEditingEnsureUiReady() {
    imageEditingEnsureColorSelectorWired();
    imageEditingEnsurePenOptionsSectionExists();
    imageEditingEnsureEditorReady();
    imageEditingEnsureLayerOptionsWired();
    imageEditingEnsureSelectionEffectsControlsWired();
    imageEditingEnsureSplittersWired();
    imageEditingApplyLeftSidebarWidth();
    imageEditingApplyRightSidebarWidth();
    imageEditingApplyInputSectionState();
    imageEditingRefreshLayerOpacityControl();
    imageEditingRefreshSelectionControls();
    imageEditingRefreshCropControls();
    imageEditingRefreshEffectControls();
    imageEditingApplyZoom();
}

/**
 * Clones tone-balance values from a source layer into a normalized object.
 */
function imageEditingCloneToneBalance(toneBalance) {
    let cloned = {};
    for (let range of imageEditingToneBalanceRanges) {
        cloned[range] = {};
        for (let channel of imageEditingToneBalanceChannels) {
            let value = 0;
            if (toneBalance && toneBalance[range]) {
                value = parseFloat(toneBalance[range][channel]);
                if (isNaN(value)) {
                    value = 0;
                }
            }
            cloned[range][channel] = Math.max(-1, Math.min(1, value));
        }
    }
    return cloned;
}

/**
 * Opens the Generate tab edit-image area with a provided image.
 */
async function openGenerateTabEditorForImage(img, actionLabel = 'Edit Image', retryCount = 0) {
    let initImageGroupToggle = document.getElementById('input_group_content_initimage_toggle');
    if (initImageGroupToggle) {
        initImageGroupToggle.checked = true;
        triggerChangeFor(initImageGroupToggle);
    }
    let initImageParam = document.getElementById('input_initimage');
    if (!initImageParam) {
        if (retryCount < 20) {
            setTimeout(() => {
                openGenerateTabEditorForImage(img, actionLabel, retryCount + 1);
            }, 50);
            return false;
        }
        showError(`Cannot use "${actionLabel}": Init Image parameter not found\nIf you have a custom workflow, deactivate it, or add an Init Image parameter.`);
        return false;
    }
    let inputWidth = document.getElementById('input_width');
    let inputHeight = document.getElementById('input_height');
    let inputAspectRatio = document.getElementById('input_aspectratio');
    if (inputWidth && inputHeight) {
        inputWidth.value = img.naturalWidth;
        inputHeight.value = img.naturalHeight;
        triggerChangeFor(inputWidth);
        triggerChangeFor(inputHeight);
    }
    if (inputAspectRatio) {
        inputAspectRatio.value = 'Custom';
        triggerChangeFor(inputAspectRatio);
    }
    try {
        if (!await ensureGenerateImageEditorReady()) {
            showError(`Cannot use "${actionLabel}": Generate tab editor is unavailable.`);
            return false;
        }
    }
    catch (e) {
        showError(`${e}`);
        return false;
    }
    imageEditor.setBaseImage(img);
    imageEditor.activate();
    return true;
}

/**
 * Opens the Generate tab edit-image area with full editor layer data.
 */
async function openGenerateTabEditorForEditorData(sourceEditor, actionLabel = 'Send Layers To Generate Editor', retryCount = 0) {
    if (!sourceEditor || !sourceEditor.layers || sourceEditor.layers.length == 0) {
        showError(`Cannot use "${actionLabel}": no editor layers are available.`);
        return false;
    }
    let initImageGroupToggle = document.getElementById('input_group_content_initimage_toggle');
    if (initImageGroupToggle) {
        initImageGroupToggle.checked = true;
        triggerChangeFor(initImageGroupToggle);
    }
    let initImageParam = document.getElementById('input_initimage');
    if (!initImageParam) {
        if (retryCount < 20) {
            setTimeout(() => {
                openGenerateTabEditorForEditorData(sourceEditor, actionLabel, retryCount + 1);
            }, 50);
            return false;
        }
        showError(`Cannot use "${actionLabel}": Init Image parameter not found\nIf you have a custom workflow, deactivate it, or add an Init Image parameter.`);
        return false;
    }
    let inputWidth = document.getElementById('input_width');
    let inputHeight = document.getElementById('input_height');
    let inputAspectRatio = document.getElementById('input_aspectratio');
    if (inputWidth && inputHeight) {
        inputWidth.value = sourceEditor.realWidth;
        inputHeight.value = sourceEditor.realHeight;
        triggerChangeFor(inputWidth);
        triggerChangeFor(inputHeight);
    }
    if (inputAspectRatio) {
        inputAspectRatio.value = 'Custom';
        triggerChangeFor(inputAspectRatio);
    }
    try {
        if (!await ensureGenerateImageEditorReady()) {
            showError(`Cannot use "${actionLabel}": Generate tab editor is unavailable.`);
            return false;
        }
    }
    catch (e) {
        showError(`${e}`);
        return false;
    }
    let wasActive = imageEditor.active;
    imageEditor.clearVars();
    imageEditor.clearLayers();
    imageEditor.realWidth = sourceEditor.realWidth;
    imageEditor.realHeight = sourceEditor.realHeight;
    imageEditor.finalOffsetX = sourceEditor.finalOffsetX;
    imageEditor.finalOffsetY = sourceEditor.finalOffsetY;
    if (imageEditor.tools['sam2points']) {
        imageEditor.tools['sam2points'].layerPoints = new Map();
    }
    if (imageEditor.tools['sam2bbox']) {
        imageEditor.tools['sam2bbox'].bboxStartX = null;
        imageEditor.tools['sam2bbox'].bboxStartY = null;
        imageEditor.tools['sam2bbox'].bboxEndX = null;
        imageEditor.tools['sam2bbox'].bboxEndY = null;
    }
    let activeLayerIndex = sourceEditor.layers.indexOf(sourceEditor.activeLayer);
    for (let sourceLayer of sourceEditor.layers) {
        let copiedLayer = new ImageEditorLayer(imageEditor, sourceLayer.canvas.width, sourceLayer.canvas.height);
        copiedLayer.ctx.drawImage(sourceLayer.canvas, 0, 0);
        copiedLayer.width = sourceLayer.width;
        copiedLayer.height = sourceLayer.height;
        copiedLayer.offsetX = sourceLayer.offsetX;
        copiedLayer.offsetY = sourceLayer.offsetY;
        copiedLayer.rotation = sourceLayer.rotation;
        copiedLayer.opacity = sourceLayer.opacity;
        copiedLayer.saturation = typeof sourceLayer.saturation == 'number' ? sourceLayer.saturation : 1;
        copiedLayer.lightValue = typeof sourceLayer.lightValue == 'number' ? sourceLayer.lightValue : 1;
        copiedLayer.contrast = typeof sourceLayer.contrast == 'number' ? sourceLayer.contrast : 1;
        copiedLayer.hueShift = typeof sourceLayer.hueShift == 'number' ? sourceLayer.hueShift : 0;
        copiedLayer.gamma = typeof sourceLayer.gamma == 'number' ? sourceLayer.gamma : 1;
        copiedLayer.temperature = typeof sourceLayer.temperature == 'number' ? sourceLayer.temperature : 0;
        copiedLayer.tint = typeof sourceLayer.tint == 'number' ? sourceLayer.tint : 0;
        copiedLayer.shadows = typeof sourceLayer.shadows == 'number' ? sourceLayer.shadows : 0;
        copiedLayer.highlights = typeof sourceLayer.highlights == 'number' ? sourceLayer.highlights : 0;
        copiedLayer.whites = typeof sourceLayer.whites == 'number' ? sourceLayer.whites : 0;
        copiedLayer.blacks = typeof sourceLayer.blacks == 'number' ? sourceLayer.blacks : 0;
        copiedLayer.toneBalance = imageEditingCloneToneBalance(sourceLayer.toneBalance);
        copiedLayer.globalCompositeOperation = sourceLayer.globalCompositeOperation;
        copiedLayer.layerType = sourceLayer.layerType || (sourceLayer.isMask ? 'mask' : 'image');
        copiedLayer.cropX = sourceLayer.cropX || 0;
        copiedLayer.cropY = sourceLayer.cropY || 0;
        copiedLayer.cropWidth = sourceLayer.cropWidth || sourceLayer.canvas.width;
        copiedLayer.cropHeight = sourceLayer.cropHeight || sourceLayer.canvas.height;
        copiedLayer.effects = typeof ImageEditorLayer != 'undefined' && typeof ImageEditorLayer.cloneEffects == 'function' ? ImageEditorLayer.cloneEffects(sourceLayer.effects) : (sourceLayer.effects || {});
        copiedLayer.effectPresetId = sourceLayer.effectPresetId || 'neutral';
        copiedLayer.hasAnyContent = sourceLayer.hasAnyContent;
        imageEditor.addLayer(copiedLayer, true);
        if (sourceEditor.baseImageLayerId == sourceLayer.id) {
            imageEditor.baseImageLayerId = copiedLayer.id;
        }
    }
    if (activeLayerIndex >= 0 && activeLayerIndex < imageEditor.layers.length) {
        imageEditor.setActiveLayer(imageEditor.layers[activeLayerIndex]);
    }
    if (!wasActive) {
        imageEditor.activate();
    }
    else if (imageEditor.canvas) {
        imageEditor.resize();
    }
    imageEditor.offsetX = 0;
    imageEditor.offsetY = 0;
    if (imageEditor.canvas) {
        imageEditor.autoZoom();
    }
    imageEditor.redraw();
    return true;
}

/**
 * Sends an image source to the Image Editing tab's editor and activates that tab.
 */
async function sendToImageEditingTabPreview(src, metadata = '{}') {
    try {
        if (document.getElementById('imageeditingtabbutton')) {
            if (!await openGenPageTabAsync('imageeditingtabbutton')) {
                return;
            }
        }
    }
    catch (e) {
        showError(`${e}`);
        return;
    }
    imageEditingEnsureUiReady();
    if (!imageEditingTabEditor) {
        showError('Cannot send image: Image Editing editor is unavailable.');
        return;
    }
    let image = new Image();
    image.crossOrigin = 'Anonymous';
    image.onload = () => {
        if (!imageEditingTabEditor.active) {
            imageEditingTabEditor.activate();
        }
        imageEditingTabEditor.clearVars();
        imageEditingTabEditor.setBaseImage(image);
        imageEditingTabEditor.resize();
        imageEditingRefreshLayerOpacityControl();
        imageEditingZoomLevel = imageEditingTabEditor.zoomLevel;
        imageEditingApplyZoom();
    };
    image.onerror = () => {
        showError('Unable to load image preview in Image Editing tab.');
    };
    image.src = src;
}

/**
 * Sends Image Editing layer data back to Generate tab edit-image area.
 */
function sendImageEditingLayersToGenerateEditor() {
    if (!imageEditingTabEditor) {
        showError('Cannot send image: Image Editing editor is unavailable.');
        return;
    }
    let doTransfer = () => {
        openGenerateTabEditorForEditorData(imageEditingTabEditor, 'Send Layers To Generate Editor').catch((e) => {
            showError(`${e}`);
        });
    };
    let generateTopTabButton = document.getElementById('text2imagetabbutton');
    if (!generateTopTabButton) {
        doTransfer();
        return;
    }
    if (!generateTopTabButton.classList.contains('active')) {
        let eventNs = '.sendLayersToGenerateEditor';
        let onShown = (e) => {
            if (e.target.id != 'text2imagetabbutton') {
                return;
            }
            $('#toptablist').off(`shown.bs.tab${eventNs}`, onShown);
            doTransfer();
        };
        $('#toptablist').off(`shown.bs.tab${eventNs}`).on(`shown.bs.tab${eventNs}`, onShown);
        generateTopTabButton.click();
        return;
    }
    doTransfer();
}

let imageEditingTopTabButton = document.getElementById('imageeditingtabbutton');
if (imageEditingTopTabButton) {
    imageEditingTopTabButton.addEventListener('shown.bs.tab', () => {
        imageEditingEnsureUiReady();
    });
}
$('#toptablist').on('shown.bs.tab', function (e) {
    if (e.target.id == 'imageeditingtabbutton') {
        imageEditingEnsureUiReady();
        if (!imageEditingTabEditor) {
            return;
        }
        if (window.imageEditor && window.imageEditor.active) {
            window.imageEditor.deactivate();
            imageEditingPausedGenerateEditor = true;
        }
        if (!imageEditingTabEditor.active) {
            imageEditingTabEditor.activate();
        }
        imageEditingApplyLeftSidebarWidth();
        imageEditingApplyRightSidebarWidth();
        imageEditingTabEditor.resize();
        imageEditingRefreshToolButtons();
        imageEditingRefreshLayerOpacityControl();
        imageEditingApplyZoom();
    }
    else {
        if (imageEditingTabEditor && imageEditingTabEditor.active) {
            imageEditingTabEditor.deactivate();
        }
        if (e.target.id == 'text2imagetabbutton' && imageEditingPausedGenerateEditor) {
            ensureGenerateImageEditorReady().then(() => {
                if (window.imageEditor && !window.imageEditor.active) {
                    window.imageEditor.activate();
                }
                imageEditingPausedGenerateEditor = false;
            }).catch((e) => {
                showError(`${e}`);
            });
        }
    }
});

function getImageFullSrc(src) {
    if (src == null) {
        return null;
    }
    let fullSrc = src;
    if (fullSrc.startsWith("http://") || fullSrc.startsWith("https://")) {
        fullSrc = fullSrc.substring(fullSrc.indexOf('/', fullSrc.indexOf('/') + 2));
    }
    if (fullSrc.startsWith('/')) {
        fullSrc = fullSrc.substring(1);
    }
    if (fullSrc.startsWith('Output/')) {
        fullSrc = fullSrc.substring('Output/'.length);
    }
    if (fullSrc.startsWith('View/')) {
        fullSrc = fullSrc.substring('View/'.length);
        let firstSlash = fullSrc.indexOf('/');
        if (firstSlash != -1) {
            fullSrc = fullSrc.substring(firstSlash + 1);
        }
    }
    return fullSrc;
}

function normalizeCurrentImageMetadata(metadata) {
    if (!metadata) {
        return null;
    }
    if (typeof metadata == 'object' && !(metadata instanceof Uint8Array) && !Array.isArray(metadata)) {
        try {
            metadata = JSON.stringify(metadata);
        }
        catch (e) {
            console.log(`Failed to stringify current image metadata: ${e}`);
            return null;
        }
    }
    return interpretMetadata(metadata);
}

function setCurrentImage(src, metadata = '', batchId = '', previewGrow = false, smoothAdd = false, canReparse = true, isPlaceholder = false) {
    currentImgSrc = src;
    if (metadata) {
        try {
            metadata = normalizeCurrentImageMetadata(metadata);
        }
        catch (e) {
            console.log(`Failed to interpret current image metadata: ${e}`);
            metadata = null;
        }
    }
    currentMetadataVal = metadata;
    if (src == null) {
        highlightSelectedImage(src);
        forceShowWelcomeMessage();
        return;
    }
    let mediaType = getMediaType(src);
    if ((smoothAdd || !metadata) && canReparse && mediaType == 'image') {
        let image = new Image();
        image.onload = () => {
            if (!metadata) {
                parseMetadata(image, (data, parsedMetadata) => {
                    setCurrentImage(src, parsedMetadata, batchId, previewGrow, false, false);
                });
            }
            else {
                setCurrentImage(src, metadata, batchId, previewGrow, false, false);
            }
        };
        image.src = src;
        return;
    }
    let curImg = getRequiredElementById('current_image');
    if (isPlaceholder) {
        curImg.classList.add('current_image_placeholder');
    }
    else {
        curImg.classList.remove('current_image_placeholder');
    }
    let container;
    let img;
    let isReuse = false;
    let srcTarget;
    if (mediaType == 'video') {
        container = createDiv(null, 'video-container current-image-img');
        curImg.innerHTML = '';
        img = document.createElement('video');
        img.className = 'current-image-img';
        img.loop = true;
        img.autoplay = true;
        let sourceObj = document.createElement('source');
        srcTarget = sourceObj;
        sourceObj.type = isVideoExt(src);
        img.appendChild(sourceObj);
        container.appendChild(img);
    }
    else if (mediaType == 'audio') {
        curImg.innerHTML = '';
        container = createDiv(null, 'audio-container current-image-img');
        img = document.createElement('audio');
        img.preload = 'metadata';
        srcTarget = img;
        container.appendChild(img);
    }
    else {
        img = currentImageHelper.getCurrentImage();
        if (!img || img.tagName != 'IMG') {
            curImg.innerHTML = '';
            img = document.createElement('img');
        }
        else {
            isReuse = true;
            delete img.dataset.previewGrow;
            img.removeAttribute('width');
            img.removeAttribute('height');
        }
        srcTarget = img;
        container = img;
    }
    function naturalDim() {
        if (mediaType == 'video') {
            return [img.videoWidth, img.videoHeight];
        }
        else if (mediaType == 'audio') {
            return [320, 140];
        }
        else {
            return [img.naturalWidth, img.naturalHeight];
        }
    }
    img.onload = () => {
        let [width, height] = naturalDim();
        if (previewGrow || internalSiteJsGetUserSetting('centerimagealwaysgrow', false)) {
            img.width = width * 8;
            img.height = height * 8;
            img.dataset.previewGrow = 'true';
        }
        alignImageDataFormat();
    };
    if (mediaType == 'video' || mediaType == 'audio') {
        img.addEventListener('loadeddata', function() {
            if (img) {
                img.onload();
            }
        }, false);
    }
    srcTarget.src = src;
    container.classList.add('current-image-img');
    img.id = 'current_image_img';
    img.dataset.src = src;
    img.dataset.metadata = metadata || '{}';
    img.dataset.batch_id = batchId;
    img.onclick = () => imageFullView.showImage(img.dataset.src, img.dataset.metadata, img.dataset.batch_id);
    let extrasWrapper = isReuse ? document.getElementById('current-image-extras-wrapper') : createDiv('current-image-extras-wrapper', 'current-image-extras-wrapper');
    extrasWrapper.innerHTML = '';
    let buttons = createDiv(null, 'current-image-buttons');
    let imagePathClean = getImageFullSrc(src);
    let buttonsChoice = getUserSetting('ButtonsUnderMainImages', '');
    let isUsingDefaults = buttonsChoice == '';
    if (isUsingDefaults) {
        buttonsChoice = defaultButtonChoices;
    }
    let buttonDefs = {};
    let subButtons = [];
    let buttonsChoiceOrdered = [];
    function normalizeButtonKey(name) {
        let normalized = (name || '').toLowerCase().replaceAll(' ', '');
        if (normalized == 'starred') {
            normalized = 'star';
        }
        return normalized;
    }
    function includeButton(name, action, extraClass = '', title = '', mediaTypes = null, can_multi = false, multi_only = false) {
        buttonDefs[normalizeButtonKey(name)] = { name, action, extraClass, title, mediaTypes, can_multi, multi_only };
    }
    function includeLinkButton(name, href, isDownload = false, title = '', mediaTypes = null) {
        buttonDefs[normalizeButtonKey(name)] = { name, href, is_download: isDownload, title, mediaTypes };
    }
    function renderButtonsFromDefs() {
        for (let key of buttonsChoiceOrdered) {
            let def = buttonDefs[key];
            if (def) {
                delete buttonDefs[key];
                if (def.mediaTypes && !def.mediaTypes.includes(mediaType)) {
                    continue;
                }
                if (def.href) {
                    let link = document.createElement('a');
                    link.className = `basic-button${def.extraClass || ''}`;
                    link.innerHTML = def.name;
                    link.title = def.title || '';
                    link.href = def.href;
                    if (def.is_download) {
                        link.download = '';
                    }
                    buttons.appendChild(link);
                }
                else {
                    quickAppendButton(buttons, def.name, (e, button) => def.action(button), def.extraClass, def.title);
                }
            }
        }
        for (let def of Object.values(buttonDefs)) {
            if (def.multi_only) {
                continue;
            }
            if (def.mediaTypes && !def.mediaTypes.includes(mediaType)) {
                continue;
            }
            if (def.href) {
                subButtons.push({ key: def.name, href: def.href, is_download: def.is_download, title: def.title });
            }
            else {
                subButtons.push({ key: def.name, action: def.action, title: def.title });
            }
        }
    }
    let rawButtonsChoice = buttonsChoice.toLowerCase().split(',');
    for (let name of rawButtonsChoice) {
        let key = normalizeButtonKey(name);
        if (key) {
            buttonsChoiceOrdered.push(key);
        }
    }
    if (isUsingDefaults) {
        for (let reg of registeredMediaButtons) {
            if (reg.isDefault) {
                let key = normalizeButtonKey(reg.name);
                if (key && !buttonsChoiceOrdered.includes(key)) {
                    buttonsChoiceOrdered.push(key);
                }
            }
        }
    }
    let isDataImage = src.startsWith('data:');
    includeButton('Use As Init', () => {
        let initImageParam = document.getElementById('input_initimage');
        if (initImageParam) {
            let type = img.src.substring(img.src.lastIndexOf('.') + 1);
            let set = (blob) => {
                let file = new File([blob], imagePathClean, { type: `image/${type.length > 0 && type.length < 20 ? type : 'png'}` });
                let container = new DataTransfer();
                container.items.add(file);
                initImageParam.files = container.files;
                triggerChangeFor(initImageParam);
                toggleGroupOpen(initImageParam, true);
                let toggler = getRequiredElementById('input_group_content_initimage_toggle');
                toggler.checked = true;
                triggerChangeFor(toggler);
            };
            if (img.dataset.src && (img.dataset.src.startsWith('data:') || img.dataset.src.startsWith('/') || img.dataset.src.startsWith('View/'))) {
                fetch(img.dataset.src).then(response => response.blob()).then(blob => {
                    set(blob);
                });
            }
            else {
                let tmpImg = new Image();
                tmpImg.crossOrigin = 'Anonymous';
                tmpImg.onload = () => {
                    let canvas = document.createElement('canvas');
                    canvas.width = tmpImg.naturalWidth;
                    canvas.height = tmpImg.naturalHeight;
                    let ctx = canvas.getContext('2d');
                    ctx.drawImage(tmpImg, 0, 0);
                    canvas.toBlob(blob => {
                        set(blob);
                    });
                };
                tmpImg.src = img.src;
            }
        }
    }, '', 'Sets this image as the Init Image parameter input', ['image', 'video']);
    includeButton('Use As Image Prompt', () => {
        let altPromptRegion = document.getElementById('alt_prompt_region');
        if (!altPromptRegion) {
            return;
        }
        let tmpImg = new Image();
        tmpImg.crossOrigin = 'Anonymous';
        tmpImg.onload = () => {
            let canvas = document.createElement('canvas');
            canvas.width = tmpImg.naturalWidth;
            canvas.height = tmpImg.naturalHeight;
            let ctx = canvas.getContext('2d');
            ctx.drawImage(tmpImg, 0, 0);
            canvas.toBlob(blob => {
                let type = img.src.substring(img.src.lastIndexOf('.') + 1);
                let file = new File([blob], imagePathClean, { type: `image/${type.length > 0 && type.length < 20 ? type : 'png'}` });
                imagePromptAddImage(file);
            });
        };
        tmpImg.src = img.src;
    }, '', 'Uses this image as an Image Prompt input', ['image']);
    includeButton('Edit Image', () => {
        openGenerateTabEditorForImage(img, 'Edit Image').catch((e) => {
            showError(`${e}`);
        });
    }, '', 'Opens an Image Editor for this image', ['image']);
    includeButton('Send to Krita', () => {
        sendImageToKrita(img.src);
    }, '', 'Exports this image to a temporary PNG and opens it in Krita', ['image']);
    if (mediaType == 'image') {
        includeButton('Send To Image Edit Tab', () => {
            sendToImageEditingTabPreview(img.src, img.dataset.metadata).catch((e) => {
                showError(`${e}`);
            });
        }, '', 'Sends this image to the Image Editing tab preview area');
    }
    includeButton('Upscale 2x', () => {
        toDataURL(img.src, (url => {
            let [width, height] = naturalDim();
            let input_overrides = {
                'initimage': url,
                'images': 1,
                'aspectratio': 'Custom',
                'width': width * 2,
                'height': height * 2
            };
            mainGenHandler.doGenerate(input_overrides, { 'initimagecreativity': 0.4 });
        }));
    }, '', 'Runs an instant generation with this image as the input and scale doubled', ['image', 'video']);
    includeButton('Refine Image', () => {
        toDataURL(img.src, (url => {
            let input_overrides = {
                'initimage': url,
                'initimagecreativity': 0,
                'images': 1
            };
            if (currentMetadataVal) {
                let readable = interpretMetadata(currentMetadataVal);
                let metadata = readable ? JSON.parse(readable).sui_image_params : {};
                if ('seed' in metadata && !('refinercontrolpercentage' in metadata)) { // (Special case to not seed-burn on double-refine)
                    input_overrides['seed'] = metadata.seed;
                }
            }
            let togglerInit = getRequiredElementById('input_group_content_initimage_toggle');
            let togglerRefine = getRequiredElementById('input_group_content_refineupscale_toggle');
            let togglerInitOriginal = togglerInit.checked;
            let togglerRefineOriginal = togglerRefine.checked;
            togglerInit.checked = false;
            togglerRefine.checked = true;
            triggerChangeFor(togglerInit);
            triggerChangeFor(togglerRefine);
            mainGenHandler.doGenerate(input_overrides, {}, () => {
                togglerInit.checked = togglerInitOriginal;
                togglerRefine.checked = togglerRefineOriginal;
                triggerChangeFor(togglerInit);
                triggerChangeFor(togglerRefine);
            });
        }));
    }, '', 'Runs an instant generation with Refine / Upscale turned on');
    let metaParsed = { is_starred: false };
    if (metadata) {
        try {
            metaParsed = JSON.parse(metadata) || metaParsed;
        }
        catch (e) {
            console.log(`Error parsing metadata for image: '${e}', metadata was '${metadata}'`);
        }
    }
    if (!isDataImage) {
        includeButton(metaParsed.is_starred ? 'Starred' : 'Star', (e, button) => {
            toggleStar(imagePathClean, src);
        }, (metaParsed.is_starred ? ' star-button button-starred-image' : ' star-button'), 'Toggles this image as starred - starred images get moved to a separate folder and highlighted');
    }
    includeButton('Reuse Parameters', copy_current_image_params, '', 'Copies the parameters used to generate this image to the current generation settings');
    if (isDataImage && mediaType == 'image') {
        includeButton('Save Image', button => saveCurrentImageToHistory(img, button), '', 'Saves this image and metadata into history. Useful when Do Not Save is enabled.');
    }
    if (!isDataImage) {
        includeButton('View In History', () => {
            let folder = imagePathClean;
            let lastSlash = folder.lastIndexOf('/');
            if (lastSlash != -1) {
                folder = folder.substring(0, lastSlash);
            }
            getRequiredElementById('imagehistorytabclickable').click();
            imageHistoryBrowser.navigate(folder);
        }, '', 'Jumps the History browser to where this file is at.');
    }
    for (let added of buttonsForImage(imagePathClean, src, metadata, true)) {
        if (added.label == 'Star' || added.label == 'Unstar') {
            continue;
        }
        if (added.href) {
            includeLinkButton(added.label, added.href, added.is_download, added.title);
        }
        else {
            includeButton(added.label, added.onclick, '', added.title);
        }
    }
    renderButtonsFromDefs();
    quickAppendButton(buttons, 'More &#x2B9F;', (e, button) => {
        let rect = button.getBoundingClientRect();
        new AdvancedPopover('image_more_popover', subButtons, false, rect.x, rect.y + button.offsetHeight + 6, document.body, null);
    });
    extrasWrapper.appendChild(buttons);
    let data = createDiv(null, 'current-image-data');
    data.innerHTML = formatMetadata(metadata);
    extrasWrapper.appendChild(data);
    if (!isReuse) {
        curImg.appendChild(container);
        curImg.appendChild(extrasWrapper);
        if (mediaType == 'video') {
            new VideoControls(img);
        }
        else if (mediaType == 'audio') {
            new AudioControls(img);
        }
    }
    highlightSelectedImage(src);
}

/**
 * Sends the specified image URL to the local Krita bridge.
 */
function sendImageToKrita(imgSrc) {
    toDataURL(imgSrc, (dataUrl) => {
        genericRequest('SendImageToKrita', { imageData: dataUrl }, data => {
            if (!data.success) {
                showError(data.error || 'Failed to send image to Krita.');
            }
        }, 0, error => {
            showError(`${error}`);
        });
    });
}

function highlightSelectedImage(src) {
    swarmImageCardRegistry.setCurrentSource(src);
    let historyContainer = document.getElementById('imagehistorybrowser-content');
    if (historyContainer) {
        for (let i of historyContainer.getElementsByClassName('model-block')) {
            i.classList.toggle('model-selected', i.dataset.src == src);
        }
    }
}

/** Gets the container div element for a generated image to put into, in the batch output view. If Separate Batches is enabled, will use or create a per-batch container. */
function getPreferredBatchContainer(batchId) {
    let mainContainer = getRequiredElementById('current_image_batch');
    if (separateBatchesElem.checked) {
        let reqId = batchId.split('_')[0];
        let batchContainer = document.getElementById(`current_image_batch_${reqId}`);
        if (!batchContainer) {
            batchContainer = createDiv(`current_image_batch_${reqId}`, null);
            mainContainer.prepend(batchContainer);
        }
        return batchContainer;
    }
    return mainContainer;
}

function appendImage(container, imageSrc, batchId, textPreview, metadata = '', type = 'legacy', prepend = true) {
    ensureBatchCardDelegationReady();
    if (typeof container == 'string') {
        container = getRequiredElementById(container);
    }
    container.dataset.numImages = parseInt(container.dataset.numImages ?? 0) + 1;
    let div = document.createElement('swarm-image-card');
    div.className = `image-block image-block-${type} image-batch-${batchId == "folder" ? "folder" : (container.dataset.numImages % 2 ? "1" : "0")}`;
    div.dataset.batch_id = batchId;
    if (batchId.includes('_')) {
        div.dataset.request_id = batchId.split('_')[0];
    }
    div.dataset.preview_text = textPreview;
    if (imageSrc.startsWith('DOPLACEHOLDER:')) {
        let model = imageSrc.substring('DOPLACEHOLDER:'.length);
        let cache = modelIconUrlCache[model] || modelIconUrlCache[`${model}.safetensors`];
        if (model && cache) {
            imageSrc = cache;
        }
        else {
            imageSrc = 'imgs/model_placeholder.jpg';
        }
        div.dataset.is_placeholder = true;
        div.classList.add('image-block-placeholder');
    }
    div.dataset.src = imageSrc;
    div.dataset.metadata = typeof metadata == 'object' && !(metadata instanceof Uint8Array) && !Array.isArray(metadata) ? JSON.stringify(metadata) : metadata;
    let isVideo = isVideoExt(imageSrc);
    let isAudio = isAudioExt(imageSrc);
    let img, srcTarget;
    if (isVideo) {
        img = document.createElement('video');
        img.loop = true;
        img.autoplay = true;
        img.muted = true;
        img.width = 16 * 10;
        let sourceObj = document.createElement('source');
        srcTarget = sourceObj;
        sourceObj.type = isVideo;
        img.appendChild(sourceObj);
    }
    else if (isAudio) {
        imageSrc = 'imgs/audio_placeholder.jpg';
        img = document.createElement('img');
        srcTarget = img;
    }
    else {
        img = document.createElement('img');
        srcTarget = img;
    }
    img.addEventListener(isVideo ? 'loadeddata' : 'load', () => {
        if (batchId != "folder") {
            let ratio = (img.naturalWidth || img.videoWidth) / (img.naturalHeight || img.videoHeight);
            div.style.width = `calc(${roundToStr(ratio * 10, 2)}rem + 2px)`;
        }
    });
    srcTarget.src = imageSrc;
    img.classList.add('image-block-img-inner');
    div.appendChild(img);
    if (type == 'legacy') {
        let textBlock = createDiv(null, 'image-preview-text');
        textBlock.innerText = textPreview;
        div.appendChild(textBlock);
    }
    if (prepend) {
        container.prepend(div);
    }
    else {
        container.appendChild(div);
    }
    return div;
}

function gotImageResult(image, metadata, batchId) {
    updateGenCount();
    let src = image;
    let fname = src && src.includes('/') ? src.substring(src.lastIndexOf('/') + 1) : src;
    let batch_div = appendImage(getPreferredBatchContainer(batchId), src, batchId, fname, metadata, 'batch');
    if (batch_div.dataset.request_id) {
        let insertAfter = null;
        for (let c of batch_div.parentElement.children) {
            if (c.dataset.is_generating == 'true' && c.dataset.request_id == batch_div.dataset.request_id && c.dataset.batch_id != batch_div.dataset.batch_id) {
                insertAfter = c;
                break;
            }
        }
        if (insertAfter) {
            batch_div.parentElement.insertBefore(batch_div, insertAfter.nextSibling);
        }
    }
    if (!currentImageHelper.getCurrentImage() || autoLoadImagesElem.checked) {
        setCurrentImage(src, metadata, batchId, false, true);
    }
    if ((internalSiteJsGetUserSetting('AutoSwapImagesIncludesFullView', false) || imageFullView.currentBatchId == batchId) && imageFullView.isOpen()) {
        imageFullView.showImage(src, metadata, batchId);
    }
    return batch_div;
}

function gotImagePreview(image, metadata, batchId) {
    updateGenCount();
    let src = image;
    let fname = src && src.includes('/') ? src.substring(src.lastIndexOf('/') + 1) : src;
    let batch_div = appendImage(getPreferredBatchContainer(batchId), src, batchId, fname, metadata, 'batch', true);
    batch_div.querySelector('img').dataset.previewGrow = 'true';
    batch_div.dataset.is_generating = 'true';
    if (showLoadSpinnersElem.checked) {
        let spinnerDiv = createDiv(null, "loading-spinner-parent", `<div class="loading-spinner"><div class="loadspin1"></div><div class="loadspin2"></div><div class="loadspin3"></div></div>`);
        batch_div.appendChild(spinnerDiv);
        uiImprover.runLoadSpinner(spinnerDiv);
    }
    if ((!currentImageHelper.getCurrentImage() || autoLoadPreviewsElem.checked) && !image.startsWith('DOPLACEHOLDER:')) {
        setCurrentImage(src, metadata, batchId, true);
    }
    return batch_div;
}

function imageInputHandler() {
    let imageArea = getRequiredElementById('current_image');
    imageArea.addEventListener('dragover', (e) => {
        e.preventDefault();
        e.stopPropagation();
    });
    imageArea.addEventListener('drop', (e) => {
        if (e.dataTransfer.files && e.dataTransfer.files.length > 0) {
            e.preventDefault();
            e.stopPropagation();
            let file = e.dataTransfer.files[0];
            if (file.type.startsWith('image/')) {
                let reader = new FileReader();
                reader.onload = (e) => {
                    try {
                        parseMetadata(e.target.result, (data, metadata) => { setCurrentImage(data, metadata); });
                    }
                    catch (e) {
                        setCurrentImage(e.target.result, null);
                    }
                }
                reader.readAsDataURL(file);
            }
            else if (file.name.endsWith('.json') || file.type == 'application/json') {
                let reader = new FileReader();
                reader.onload = (e) => {
                    try {
                        let metadata = interpretMetadata(e.target.result);
                        if (metadata) {
                            setCurrentImage('imgs/model_placeholder.jpg', metadata, '', false, false, false, true);
                        }
                    }
                    catch (e) {
                        showError(`Failed to parse JSON metadata: ${e}`);
                    }
                }
                reader.readAsText(file);
            }
        }
    });
}
imageInputHandler();
