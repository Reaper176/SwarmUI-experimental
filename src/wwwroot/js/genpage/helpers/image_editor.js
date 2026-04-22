
/**
 * A single layer within an image editing interface.
 * This can be real (user-controlled) OR sub-layers (sometimes user-controlled) OR temporary buffers.
 */
class ImageEditorLayer {
    constructor(editor, width, height, parent = null) {
        this.editor = editor;
        this.parent = parent;
        this.canvas = document.createElement('canvas');
        this.width = width;
        this.height = height;
        this.canvas.width = width;
        this.canvas.height = height;
        this.ctx = this.canvas.getContext('2d');
        this.offsetX = 0;
        this.offsetY = 0;
        this.rotation = 0;
        this.opacity = 1;
        this.saturation = 1;
        this.lightValue = 1;
        this.contrast = 1;
        this.hueShift = 0;
        this.gamma = 1;
        this.temperature = 0;
        this.tint = 0;
        this.shadows = 0;
        this.highlights = 0;
        this.whites = 0;
        this.blacks = 0;
        this.toneBalance = {
            shadows: { r: 0, g: 0, b: 0 },
            midtones: { r: 0, g: 0, b: 0 },
            highlights: { r: 0, g: 0, b: 0 }
        };
        this.toneBalanceCacheCanvas = null;
        this.toneBalanceCacheKey = null;
        this.effectCacheCanvas = null;
        this.effectCacheKey = null;
        this.adjustmentCacheCanvas = null;
        this.adjustmentCacheKey = null;
        this.globalCompositeOperation = 'source-over';
        this.childLayers = [];
        this.buffer = null;
        this.layerType = 'image';
        Object.defineProperty(this, 'isMask', {
            get: () => this.layerType == 'mask',
            set: (value) => {
                this.layerType = value ? 'mask' : 'image';
            }
        });
        this.maskCanvas = this.canvas;
        this.maskCtx = this.ctx;
        this.cropX = 0;
        this.cropY = 0;
        this.cropWidth = width;
        this.cropHeight = height;
        this.effects = ImageEditorLayer.createDefaultEffects();
        this.effectPresetId = 'neutral';
        this.hasAnyContent = false;
        this.contentVersion = 0;
    }

    static createDefaultEffects() {
        return {
            blur: 0,
            sharpen: 0,
            noiseReduction: 0,
            artisticFilter: 'none',
            vignette: 0,
            glow: 0
        };
    }

    static cloneEffects(effects) {
        let cloned = ImageEditorLayer.createDefaultEffects();
        if (!effects || typeof effects != 'object') {
            return cloned;
        }
        for (let key of Object.keys(cloned)) {
            if (key == 'artisticFilter') {
                if (typeof effects[key] == 'string' && effects[key]) {
                    cloned[key] = effects[key];
                }
            }
            else {
                let value = parseFloat(effects[key]);
                if (!isNaN(value)) {
                    cloned[key] = value;
                }
            }
        }
        return cloned;
    }

    getTypeLabel() {
        if (this.layerType == 'mask') {
            return 'Mask';
        }
        if (this.layerType == 'adjustment') {
            return 'Adjustment';
        }
        return 'Image';
    }

    isAdjustmentLayer() {
        return this.layerType == 'adjustment';
    }

    cloneToneBalance(toneBalance) {
        let cloned = {
            shadows: { r: 0, g: 0, b: 0 },
            midtones: { r: 0, g: 0, b: 0 },
            highlights: { r: 0, g: 0, b: 0 }
        };
        if (!toneBalance || typeof toneBalance != 'object') {
            return cloned;
        }
        for (let range of ['shadows', 'midtones', 'highlights']) {
            for (let channel of ['r', 'g', 'b']) {
                let value = NaN;
                if (toneBalance[range]) {
                    value = parseFloat(toneBalance[range][channel]);
                }
                if (isNaN(value)) {
                    value = 0;
                }
                cloned[range][channel] = Math.max(-1, Math.min(1, value));
            }
        }
        return cloned;
    }

    ensureToneBalance() {
        this.toneBalance = this.cloneToneBalance(this.toneBalance);
    }

    getNumericAdjustmentValue(prop, defaultValue = 0) {
        let value = parseFloat(this[prop]);
        if (isNaN(value)) {
            return defaultValue;
        }
        return value;
    }

    hasToneBalanceAdjustments() {
        this.ensureToneBalance();
        for (let range of ['shadows', 'midtones', 'highlights']) {
            for (let channel of ['r', 'g', 'b']) {
                if (Math.abs(this.toneBalance[range][channel]) > 0.0001) {
                    return true;
                }
            }
        }
        if (Math.abs(this.getNumericAdjustmentValue('gamma', 1) - 1) > 0.0001
            || Math.abs(this.getNumericAdjustmentValue('temperature', 0)) > 0.0001
            || Math.abs(this.getNumericAdjustmentValue('tint', 0)) > 0.0001
            || Math.abs(this.getNumericAdjustmentValue('shadows', 0)) > 0.0001
            || Math.abs(this.getNumericAdjustmentValue('highlights', 0)) > 0.0001
            || Math.abs(this.getNumericAdjustmentValue('whites', 0)) > 0.0001
            || Math.abs(this.getNumericAdjustmentValue('blacks', 0)) > 0.0001) {
            return true;
        }
        return false;
    }

    getToneBalancedSourceCanvas() {
        this.ensureToneBalance();
        let gamma = Math.max(0.01, this.getNumericAdjustmentValue('gamma', 1));
        let temperature = this.getNumericAdjustmentValue('temperature', 0);
        let tint = this.getNumericAdjustmentValue('tint', 0);
        let shadowsValue = this.getNumericAdjustmentValue('shadows', 0);
        let highlightsValue = this.getNumericAdjustmentValue('highlights', 0);
        let whitesValue = this.getNumericAdjustmentValue('whites', 0);
        let blacksValue = this.getNumericAdjustmentValue('blacks', 0);
        let key = `${this.contentVersion}|${this.canvas.width}x${this.canvas.height}|${gamma}|${temperature}|${tint}|${shadowsValue}|${highlightsValue}|${whitesValue}|${blacksValue}|${this.toneBalance.shadows.r},${this.toneBalance.shadows.g},${this.toneBalance.shadows.b},${this.toneBalance.midtones.r},${this.toneBalance.midtones.g},${this.toneBalance.midtones.b},${this.toneBalance.highlights.r},${this.toneBalance.highlights.g},${this.toneBalance.highlights.b}`;
        if (this.toneBalanceCacheCanvas && this.toneBalanceCacheKey == key) {
            return this.toneBalanceCacheCanvas;
        }
        let tempCanvas = document.createElement('canvas');
        tempCanvas.width = this.canvas.width;
        tempCanvas.height = this.canvas.height;
        let tempCtx = tempCanvas.getContext('2d');
        tempCtx.drawImage(this.canvas, 0, 0);
        let imageData = tempCtx.getImageData(0, 0, tempCanvas.width, tempCanvas.height);
        let data = imageData.data;
        let shadows = this.toneBalance.shadows;
        let midtones = this.toneBalance.midtones;
        let highlights = this.toneBalance.highlights;
        for (let i = 0; i < data.length; i += 4) {
            if (data[i + 3] == 0) {
                continue;
            }
            let r = data[i];
            let g = data[i + 1];
            let b = data[i + 2];
            let luminance = (0.2126 * r + 0.7152 * g + 0.0722 * b) / 255;
            let shadowWeight = Math.max(0, 1 - (luminance / 0.5));
            let highlightWeight = Math.max(0, (luminance - 0.5) / 0.5);
            let midtoneWeight = Math.max(0, 1 - Math.abs(luminance - 0.5) / 0.5);
            let blackWeight = Math.max(0, 1 - luminance / 0.25);
            let whiteWeight = Math.max(0, (luminance - 0.75) / 0.25);
            blackWeight *= blackWeight;
            whiteWeight *= whiteWeight;
            let tonalLift = 255 * (shadowsValue * shadowWeight * 0.35 + highlightsValue * highlightWeight * 0.35 + whitesValue * whiteWeight * 0.45 + blacksValue * blackWeight * 0.45);
            let adjustR = 255 * (shadows.r * shadowWeight + midtones.r * midtoneWeight + highlights.r * highlightWeight);
            let adjustG = 255 * (shadows.g * shadowWeight + midtones.g * midtoneWeight + highlights.g * highlightWeight);
            let adjustB = 255 * (shadows.b * shadowWeight + midtones.b * midtoneWeight + highlights.b * highlightWeight);
            let temperatureAdjust = temperature * 32;
            let tintAdjust = tint * 24;
            let newR = r + tonalLift + adjustR + temperatureAdjust + tintAdjust * 0.35;
            let newG = g + tonalLift + adjustG - tintAdjust * 0.7;
            let newB = b + tonalLift + adjustB - temperatureAdjust + tintAdjust * 0.35;
            if (Math.abs(gamma - 1) > 0.0001) {
                newR = 255 * Math.pow(Math.max(0, Math.min(1, newR / 255)), 1 / gamma);
                newG = 255 * Math.pow(Math.max(0, Math.min(1, newG / 255)), 1 / gamma);
                newB = 255 * Math.pow(Math.max(0, Math.min(1, newB / 255)), 1 / gamma);
            }
            data[i] = Math.max(0, Math.min(255, Math.round(newR)));
            data[i + 1] = Math.max(0, Math.min(255, Math.round(newG)));
            data[i + 2] = Math.max(0, Math.min(255, Math.round(newB)));
        }
        tempCtx.putImageData(imageData, 0, 0);
        this.toneBalanceCacheCanvas = tempCanvas;
        this.toneBalanceCacheKey = key;
        return tempCanvas;
    }

    resetToneBalanceCache() {
        this.toneBalanceCacheCanvas = null;
        this.toneBalanceCacheKey = null;
    }

    resetEffectCache() {
        this.effectCacheCanvas = null;
        this.effectCacheKey = null;
        this.adjustmentCacheCanvas = null;
        this.adjustmentCacheKey = null;
    }

    markContentChanged() {
        this.contentVersion++;
        this.resetToneBalanceCache();
        this.resetEffectCache();
    }

    markVisualChanged() {
        this.resetEffectCache();
        this.resetToneBalanceCache();
    }

    ensureEffects() {
        this.effects = ImageEditorLayer.cloneEffects(this.effects);
        if (!this.effectPresetId) {
            this.effectPresetId = 'neutral';
        }
    }

    getCropData() {
        let sourceWidth = this.canvas.width;
        let sourceHeight = this.canvas.height;
        let cropX = Math.round(parseFloat(this.cropX));
        let cropY = Math.round(parseFloat(this.cropY));
        let cropWidth = Math.round(parseFloat(this.cropWidth));
        let cropHeight = Math.round(parseFloat(this.cropHeight));
        if (isNaN(cropX)) {
            cropX = 0;
        }
        if (isNaN(cropY)) {
            cropY = 0;
        }
        if (isNaN(cropWidth) || cropWidth <= 0) {
            cropWidth = sourceWidth;
        }
        if (isNaN(cropHeight) || cropHeight <= 0) {
            cropHeight = sourceHeight;
        }
        cropX = Math.max(0, Math.min(sourceWidth - 1, cropX));
        cropY = Math.max(0, Math.min(sourceHeight - 1, cropY));
        cropWidth = Math.max(1, Math.min(sourceWidth - cropX, cropWidth));
        cropHeight = Math.max(1, Math.min(sourceHeight - cropY, cropHeight));
        this.cropX = cropX;
        this.cropY = cropY;
        this.cropWidth = cropWidth;
        this.cropHeight = cropHeight;
        return { cropX, cropY, cropWidth, cropHeight };
    }

    resetCrop() {
        this.cropX = 0;
        this.cropY = 0;
        this.cropWidth = this.canvas.width;
        this.cropHeight = this.canvas.height;
        this.markVisualChanged();
    }

    copyVisualStateFrom(otherLayer, includeTransform = true) {
        if (!otherLayer) {
            return;
        }
        if (includeTransform) {
            this.width = otherLayer.width;
            this.height = otherLayer.height;
            this.offsetX = otherLayer.offsetX;
            this.offsetY = otherLayer.offsetY;
            this.rotation = otherLayer.rotation;
            let crop = otherLayer.getCropData();
            this.cropX = crop.cropX;
            this.cropY = crop.cropY;
            this.cropWidth = crop.cropWidth;
            this.cropHeight = crop.cropHeight;
        }
        this.opacity = typeof otherLayer.opacity == 'number' ? otherLayer.opacity : 1;
        this.saturation = typeof otherLayer.saturation == 'number' ? otherLayer.saturation : 1;
        this.lightValue = typeof otherLayer.lightValue == 'number' ? otherLayer.lightValue : 1;
        this.contrast = typeof otherLayer.contrast == 'number' ? otherLayer.contrast : 1;
        this.hueShift = typeof otherLayer.hueShift == 'number' ? otherLayer.hueShift : 0;
        this.gamma = typeof otherLayer.gamma == 'number' ? otherLayer.gamma : 1;
        this.temperature = typeof otherLayer.temperature == 'number' ? otherLayer.temperature : 0;
        this.tint = typeof otherLayer.tint == 'number' ? otherLayer.tint : 0;
        this.shadows = typeof otherLayer.shadows == 'number' ? otherLayer.shadows : 0;
        this.highlights = typeof otherLayer.highlights == 'number' ? otherLayer.highlights : 0;
        this.whites = typeof otherLayer.whites == 'number' ? otherLayer.whites : 0;
        this.blacks = typeof otherLayer.blacks == 'number' ? otherLayer.blacks : 0;
        this.toneBalance = this.cloneToneBalance(otherLayer.toneBalance);
        this.globalCompositeOperation = otherLayer.globalCompositeOperation || 'source-over';
        this.layerType = otherLayer.layerType || 'image';
        this.effects = ImageEditorLayer.cloneEffects(otherLayer.effects);
        this.effectPresetId = otherLayer.effectPresetId || 'neutral';
    }

    getDisplayScaleX() {
        let crop = this.getCropData();
        return this.width / crop.cropWidth;
    }

    getDisplayScaleY() {
        let crop = this.getCropData();
        return this.height / crop.cropHeight;
    }

    configureImageToLayerTransform(ctx) {
        let crop = this.getCropData();
        let [offsetX, offsetY] = this.getOffset();
        let relWidth = this.width / crop.cropWidth;
        let relHeight = this.height / crop.cropHeight;
        let cx = this.width / 2;
        let cy = this.height / 2;
        let cosR = Math.cos(-this.rotation);
        let sinR = Math.sin(-this.rotation);
        ctx.setTransform(
            cosR / relWidth, sinR / relHeight,
            -sinR / relWidth, cosR / relHeight,
            (-cosR * (offsetX + cx) + sinR * (offsetY + cy) + cx) / relWidth + crop.cropX,
            (-sinR * (offsetX + cx) - cosR * (offsetY + cy) + cy) / relHeight + crop.cropY
        );
    }

    getVisualSignature(extraKey = '') {
        this.ensureEffects();
        let crop = this.getCropData();
        return [
            extraKey,
            this.layerType,
            this.contentVersion,
            this.canvas.width,
            this.canvas.height,
            crop.cropX,
            crop.cropY,
            crop.cropWidth,
            crop.cropHeight,
            this.saturation,
            this.lightValue,
            this.contrast,
            this.hueShift,
            this.gamma,
            this.temperature,
            this.tint,
            this.shadows,
            this.highlights,
            this.whites,
            this.blacks,
            this.effectPresetId,
            this.effects.blur,
            this.effects.sharpen,
            this.effects.noiseReduction,
            this.effects.artisticFilter,
            this.effects.vignette,
            this.effects.glow,
            `${this.toneBalance?.shadows?.r || 0},${this.toneBalance?.shadows?.g || 0},${this.toneBalance?.shadows?.b || 0},${this.toneBalance?.midtones?.r || 0},${this.toneBalance?.midtones?.g || 0},${this.toneBalance?.midtones?.b || 0},${this.toneBalance?.highlights?.r || 0},${this.toneBalance?.highlights?.g || 0},${this.toneBalance?.highlights?.b || 0}`
        ].join('|');
    }

    getRenderedSourceCanvas(sourceCanvas = null, extraKey = '') {
        this.ensureEffects();
        let useAdjustmentCache = sourceCanvas != null;
        let cacheCanvasKey = useAdjustmentCache ? 'adjustmentCacheCanvas' : 'effectCacheCanvas';
        let cacheKeyKey = useAdjustmentCache ? 'adjustmentCacheKey' : 'effectCacheKey';
        let crop = this.getCropData();
        let key = this.getVisualSignature(`${extraKey}|${useAdjustmentCache ? 'adjustment' : 'layer'}`);
        if (sourceCanvas) {
            key += `|${sourceCanvas.width}x${sourceCanvas.height}`;
        }
        if (this[cacheCanvasKey] && this[cacheKeyKey] == key) {
            return this[cacheCanvasKey];
        }
        let inputCanvas = sourceCanvas || this.canvas;
        let workingCanvas = document.createElement('canvas');
        if (sourceCanvas) {
            workingCanvas.width = inputCanvas.width;
            workingCanvas.height = inputCanvas.height;
            let workingCtx = workingCanvas.getContext('2d');
            workingCtx.drawImage(inputCanvas, 0, 0);
            if (this.hasToneBalanceAdjustments()) {
                workingCanvas = imageEditorApplyAdvancedAdjustmentsToCanvas(workingCanvas, this);
            }
        }
        else {
            let source = this.hasToneBalanceAdjustments() ? this.getToneBalancedSourceCanvas() : this.canvas;
            workingCanvas.width = crop.cropWidth;
            workingCanvas.height = crop.cropHeight;
            let workingCtx = workingCanvas.getContext('2d');
            workingCtx.drawImage(source, crop.cropX, crop.cropY, crop.cropWidth, crop.cropHeight, 0, 0, crop.cropWidth, crop.cropHeight);
        }
        workingCanvas = imageEditorApplyBasicAdjustmentsToCanvas(workingCanvas, this);
        workingCanvas = imageEditorApplyPresetToCanvas(workingCanvas, this.effectPresetId || 'neutral');
        workingCanvas = imageEditorApplyLayerEffectsToCanvas(workingCanvas, this.effects);
        this[cacheCanvasKey] = workingCanvas;
        this[cacheKeyKey] = key;
        return workingCanvas;
    }

    getAdjustmentOutputCanvas(sourceCanvas, lowerStackKey = '') {
        return this.getRenderedSourceCanvas(sourceCanvas, lowerStackKey);
    }

    createButtons() {
        let popId = `image_editor_layer_preview_${this.id}`;
        this.menuPopover.innerHTML = '';
        let buttonDelete = createDiv(null, 'sui_popover_model_button');
        buttonDelete.innerText = 'Delete Layer';
        buttonDelete.addEventListener('click', (e) => {
            e.preventDefault();
            hidePopover(popId);
            this.editor.removeLayer(this);
        }, true);
        this.menuPopover.appendChild(buttonDelete);
        let buttonDuplicate = createDiv(null, 'sui_popover_model_button');
        buttonDuplicate.innerText = 'Duplicate Layer';
        buttonDuplicate.addEventListener('click', (e) => {
            e.preventDefault();
            hidePopover(popId);
            this.editor.duplicateLayer(this);
        }, true);
        this.menuPopover.appendChild(buttonDuplicate);
        let buttonConvert = createDiv(null, 'sui_popover_model_button');
        buttonConvert.innerText = this.isAdjustmentLayer() ? 'Convert To Image Layer' : `Convert To ${(this.isMask ? `Image` : `Mask`)} Layer`;
        buttonConvert.addEventListener('click', (e) => {
            e.preventDefault();
            hidePopover(popId);
            if (this.isAdjustmentLayer()) {
                this.layerType = 'image';
            }
            else {
                this.isMask = !this.isMask;
            }
            this.infoSubDiv.innerText = this.getTypeLabel();
            this.createButtons();
            this.editor.sortLayers();
            this.editor.markOutputChanged();
            this.editor.redraw();
        }, true);
        this.menuPopover.appendChild(buttonConvert);
        let buttonInvert = createDiv(null, 'sui_popover_model_button');
        buttonInvert.innerText = `Invert ${(this.isMask || this.isAdjustmentLayer() ? `Mask` : `Colors`)}`;
        buttonInvert.addEventListener('click', (e) => {
            e.preventDefault();
            hidePopover(popId);
            this.invert();
        }, true);
        this.menuPopover.appendChild(buttonInvert);
        let buttonFlipMirrorHorizontal = createDiv(null, 'sui_popover_model_button');
        buttonFlipMirrorHorizontal.innerText = 'Flip / Mirror Horizontal';
        buttonFlipMirrorHorizontal.addEventListener('click', (e) => {
            e.preventDefault();
            hidePopover(popId);
            this.flipHorizontal();
        }, true);
        this.menuPopover.appendChild(buttonFlipMirrorHorizontal);
        let buttonFlipMirrorVertical = createDiv(null, 'sui_popover_model_button');
        buttonFlipMirrorVertical.innerText = 'Flip / Mirror Vertical';
        buttonFlipMirrorVertical.addEventListener('click', (e) => {
            e.preventDefault();
            hidePopover(popId);
            this.flipVertical();
        }, true);
        this.menuPopover.appendChild(buttonFlipMirrorVertical);
        let sliderWrapper = createDiv(null, 'auto-slider-range-wrapper');
        let opacitySlider = document.createElement('input');
        opacitySlider.type = 'range';
        opacitySlider.className = 'auto-slider-range';
        opacitySlider.min = '0';
        opacitySlider.max = '100';
        opacitySlider.step = '1';
        opacitySlider.value = this.opacity * 100;
        opacitySlider.oninput = e => updateRangeStyle(e);
        opacitySlider.onchange = e => updateRangeStyle(e);
        opacitySlider.addEventListener('input', () => {
            this.opacity = parseInt(opacitySlider.value) / 100;
            this.canvas.style.opacity = this.opacity;
            this.editor.markOutputChanged();
            this.editor.queueSceneRedraw();
        });
        let opacityLabel = document.createElement('label');
        opacityLabel.innerHTML = 'Opacity&nbsp;';
        let opacityDiv = createDiv(null, 'sui-popover-inline-block');
        opacityDiv.appendChild(opacityLabel);
        sliderWrapper.appendChild(opacitySlider);
        opacityDiv.appendChild(sliderWrapper);
        this.menuPopover.appendChild(opacityDiv);
        updateRangeStyle(opacitySlider);
    }

    getOffset() {
        let offseter = this;
        let [x, y] = [0, 0];
        while (offseter) {
            x += offseter.offsetX;
            y += offseter.offsetY;
            offseter = offseter.parent;
        }
        return [Math.round(x), Math.round(y)];
    }

    ensureSize() {
        if (this.canvas.width != this.width || this.canvas.height != this.height) {
            this.resize(this.width, this.height);
        }
    }

    resize(width, height) {
        width = Math.round(width);
        height = Math.round(height);
        let oldWidth = this.canvas.width;
        let oldHeight = this.canvas.height;
        let newCanvas = document.createElement('canvas');
        newCanvas.width = width;
        newCanvas.height = height;
        let newCtx = newCanvas.getContext('2d');
        newCtx.drawImage(this.canvas, 0, 0, width, height);
        this.canvas = newCanvas;
        this.ctx = newCtx;
        this.maskCanvas = this.canvas;
        this.maskCtx = this.ctx;
        this.width = width;
        this.height = height;
        if (this.cropX == 0 && this.cropY == 0 && this.cropWidth == oldWidth && this.cropHeight == oldHeight) {
            this.cropWidth = width;
            this.cropHeight = height;
        }
        else {
            this.getCropData();
        }
        this.markContentChanged();
    }

    invert() {
        this.saveBeforeEdit();
        let oldCanvas = document.createElement('canvas');
        oldCanvas.width = this.canvas.width;
        oldCanvas.height = this.canvas.height;
        let oldCtx = oldCanvas.getContext('2d');
        oldCtx.drawImage(this.canvas, 0, 0);
        this.ctx.save();
        this.ctx.clearRect(0, 0, this.canvas.width, this.canvas.height);
        this.ctx.filter = 'invert(1)';
        this.ctx.drawImage(oldCanvas, 0, 0, this.canvas.width, this.canvas.height);
        this.ctx.restore();
        this.markContentChanged();
        this.editor.markOutputChanged();
        this.editor.redraw();
    }

    cloneLayerData() {
        let clone = new ImageEditorLayer(this.editor, this.canvas.width, this.canvas.height);
        clone.ctx.drawImage(this.canvas, 0, 0);
        clone.copyVisualStateFrom(this);
        clone.hasAnyContent = this.hasAnyContent;
        clone.contentVersion = this.contentVersion;
        return clone;
    }

    flipHorizontal() {
        this.saveBeforeEdit();
        let oldCanvas = this.canvas;
        let oldCopyCanvas = document.createElement('canvas');
        oldCopyCanvas.width = oldCanvas.width;
        oldCopyCanvas.height = oldCanvas.height;
        let oldCopyCtx = oldCopyCanvas.getContext('2d');
        oldCopyCtx.drawImage(oldCanvas, 0, 0);
        this.ctx.save();
        this.ctx.clearRect(0, 0, oldCanvas.width, oldCanvas.height);
        this.ctx.translate(oldCanvas.width, 0);
        this.ctx.scale(-1, 1);
        this.ctx.drawImage(oldCopyCanvas, 0, 0);
        this.ctx.restore();
        this.markContentChanged();
        this.editor.markOutputChanged();
        this.editor.redraw();
    }

    flipVertical() {
        this.saveBeforeEdit();
        let oldCanvas = this.canvas;
        let oldCopyCanvas = document.createElement('canvas');
        oldCopyCanvas.width = oldCanvas.width;
        oldCopyCanvas.height = oldCanvas.height;
        let oldCopyCtx = oldCopyCanvas.getContext('2d');
        oldCopyCtx.drawImage(oldCanvas, 0, 0);
        this.ctx.save();
        this.ctx.clearRect(0, 0, oldCanvas.width, oldCanvas.height);
        this.ctx.translate(0, oldCanvas.height);
        this.ctx.scale(1, -1);
        this.ctx.drawImage(oldCopyCanvas, 0, 0);
        this.ctx.restore();
        this.markContentChanged();
        this.editor.markOutputChanged();
        this.editor.redraw();
    }

    mirrorHorizontal() {
        this.flipHorizontal();
    }

    mirrorVertical() {
        this.flipVertical();
    }

    canvasCoordToLayerCoord(x, y) {
        let [x2, y2] = this.editor.canvasCoordToImageCoord(x, y);
        let [offsetX, offsetY] = this.getOffset();
        let crop = this.getCropData();
        let relWidth = this.width / crop.cropWidth;
        let relHeight = this.height / crop.cropHeight;
        [x2, y2] = [x2 - offsetX, y2 - offsetY];
        let angle = -this.rotation;
        let [cx, cy] = [this.width / 2, this.height / 2];
        let [x3, y3] = [x2 - cx, y2 - cy];
        [x3, y3] = [x3 * Math.cos(angle) - y3 * Math.sin(angle), x3 * Math.sin(angle) + y3 * Math.cos(angle)];
        [x2, y2] = [x3 + cx, y3 + cy];
        [x2, y2] = [crop.cropX + (x2 / relWidth), crop.cropY + (y2 / relHeight)];
        return [x2, y2];
    }

    layerCoordToCanvasCoord(x, y) {
        let [offsetX, offsetY] = this.getOffset();
        let crop = this.getCropData();
        let relWidth = this.width / crop.cropWidth;
        let relHeight = this.height / crop.cropHeight;
        let [x2, y2] = [(x - crop.cropX) * relWidth, (y - crop.cropY) * relHeight];
        let angle = this.rotation;
        let [cx, cy] = [this.width / 2, this.height / 2];
        let [x3, y3] = [x2 - cx, y2 - cy];
        [x3, y3] = [x3 * Math.cos(angle) - y3 * Math.sin(angle), x3 * Math.sin(angle) + y3 * Math.cos(angle)];
        [x2, y2] = [x3 + cx, y3 + cy];
        [x2, y2] = [x2 + offsetX, y2 + offsetY];
        [x2, y2] = this.editor.imageCoordToCanvasCoord(x2, y2);
        return [x2, y2];
    }

    drawFilledCircle(x, y, radius, color) {
        this.ctx.fillStyle = color;
        this.ctx.beginPath();
        this.ctx.arc(x, y, radius, 0, 2 * Math.PI);
        this.ctx.fill();
    }

    drawFilledCircleStrokeBetween(x1, y1, x2, y2, radius, color) {
        let angle = Math.atan2(y2 - y1, x2 - x1) + Math.PI / 2;
        let [rx, ry] = [radius * Math.cos(angle), radius * Math.sin(angle)];
        this.ctx.fillStyle = color;
        this.ctx.beginPath();
        this.ctx.moveTo(x1 + rx, y1 + ry);
        this.ctx.lineTo(x2 + rx, y2 + ry);
        this.ctx.lineTo(x2 - rx, y2 - ry);
        this.ctx.lineTo(x1 - rx, y1 - ry);
        this.ctx.closePath();
        this.ctx.fill();
    }

    drawToBackDirect(ctx, offsetX, offsetY, zoom) {
        if (this.isAdjustmentLayer()) {
            return;
        }
        ctx.save();
        let [thisOffsetX, thisOffsetY] = this.getOffset();
        let x = offsetX + thisOffsetX;
        let y = offsetY + thisOffsetY;
        let sourceCanvas = this.getRenderedSourceCanvas();
        ctx.globalAlpha = this.opacity;
        ctx.filter = 'none';
        ctx.globalCompositeOperation = this.globalCompositeOperation;
        let [cx, cy] = [this.width / 2, this.height / 2];
        ctx.translate((x + cx) * zoom, (y + cy) * zoom);
        ctx.rotate(this.rotation);
        if (zoom > 5) {
            ctx.imageSmoothingEnabled = false;
        }
        ctx.drawImage(sourceCanvas, 0, 0, sourceCanvas.width, sourceCanvas.height, -cx * zoom, -cy * zoom, this.width * zoom, this.height * zoom);
        ctx.restore();
    }

    drawToBack(ctx, offsetX, offsetY, zoom) {
        if (this.childLayers.length > 0) {
            if (this.buffer == null) {
                this.buffer = new ImageEditorLayer(this.editor, this.canvas.width, this.canvas.height);
                this.buffer.width = this.width;
                this.buffer.height = this.height;
                this.buffer.rotation = this.rotation;
            }
            let offset = this.getOffset();
            this.buffer.offsetX = this.offsetX;
            this.buffer.offsetY = this.offsetY;
            this.buffer.opacity = this.opacity;
            this.buffer.saturation = typeof this.saturation == 'number' ? this.saturation : 1;
            this.buffer.lightValue = typeof this.lightValue == 'number' ? this.lightValue : 1;
            this.buffer.contrast = typeof this.contrast == 'number' ? this.contrast : 1;
            this.buffer.hueShift = typeof this.hueShift == 'number' ? this.hueShift : 0;
            this.buffer.gamma = typeof this.gamma == 'number' ? this.gamma : 1;
            this.buffer.temperature = typeof this.temperature == 'number' ? this.temperature : 0;
            this.buffer.tint = typeof this.tint == 'number' ? this.tint : 0;
            this.buffer.shadows = typeof this.shadows == 'number' ? this.shadows : 0;
            this.buffer.highlights = typeof this.highlights == 'number' ? this.highlights : 0;
            this.buffer.whites = typeof this.whites == 'number' ? this.whites : 0;
            this.buffer.blacks = typeof this.blacks == 'number' ? this.blacks : 0;
            this.buffer.toneBalance = this.cloneToneBalance(this.toneBalance);
            this.buffer.globalCompositeOperation = this.globalCompositeOperation;
            this.buffer.ctx.globalAlpha = 1;
            this.buffer.ctx.globalCompositeOperation = 'source-over';
            this.buffer.ctx.clearRect(0, 0, this.canvas.width, this.canvas.height);
            this.buffer.ctx.drawImage(this.canvas, 0, 0);
            for (let layer of this.childLayers) {
                layer.drawToBack(this.buffer.ctx, -offset[0], -offset[1], 1);
            }
            this.buffer.drawToBackDirect(ctx, offsetX, offsetY, zoom);
        }
        else {
            this.buffer = null;
            this.drawToBackDirect(ctx, offsetX, offsetY, zoom);
        }
    }

    /** Saves undo state, clears all content, and marks the layer as empty. */
    clearToEmpty() {
        this.saveBeforeEdit();
        this.ctx.clearRect(0, 0, this.canvas.width, this.canvas.height);
        this.hasAnyContent = false;
        this.markContentChanged();
        this.editor.markOutputChanged();
    }

    applyMaskFromImage(img) {
        this.saveBeforeEdit();
        this.ctx.clearRect(0, 0, this.canvas.width, this.canvas.height);
        let imageData;
        if (this.rotation == 0) {
            let [offsetX, offsetY] = this.getOffset();
            this.ctx.drawImage(img, offsetX, offsetY, this.width, this.height, 0, 0, this.canvas.width, this.canvas.height);
            imageData = this.ctx.getImageData(0, 0, this.canvas.width, this.canvas.height);
        }
        else {
            let tempCanvas = document.createElement('canvas');
            tempCanvas.width = this.canvas.width;
            tempCanvas.height = this.canvas.height;
            let tempCtx = tempCanvas.getContext('2d');
            let [offsetX, offsetY] = this.getOffset();
            let relWidth = this.width / this.canvas.width;
            let relHeight = this.height / this.canvas.height;
            let cx = this.width / 2;
            let cy = this.height / 2;
            let cosR = Math.cos(-this.rotation);
            let sinR = Math.sin(-this.rotation);
            tempCtx.setTransform(
                cosR / relWidth, sinR / relHeight,
                -sinR / relWidth, cosR / relHeight,
                (-cosR * (offsetX + cx) + sinR * (offsetY + cy) + cx) / relWidth,
                (-sinR * (offsetX + cx) - cosR * (offsetY + cy) + cy) / relHeight
            );
            tempCtx.drawImage(img, 0, 0, img.width || this.editor.realWidth, img.height || this.editor.realHeight, 0, 0, this.editor.realWidth, this.editor.realHeight);
            imageData = tempCtx.getImageData(0, 0, tempCanvas.width, tempCanvas.height);
        }
        let data = imageData.data;
        for (let i = 0; i < data.length; i += 4) {
            let brightness = data[i] + data[i + 1] + data[i + 2];
            if (brightness < 128) {
                data[i + 3] = 0;
            }
        }
        this.ctx.putImageData(imageData, 0, 0);
        this.hasAnyContent = true;
        this.markContentChanged();
        this.editor.markOutputChanged();
    }

    saveBeforeEdit() {
        let oldCanvas = document.createElement('canvas');
        oldCanvas.width = this.canvas.width;
        oldCanvas.height = this.canvas.height;
        let oldCtx = oldCanvas.getContext('2d');
        oldCtx.drawImage(this.canvas, 0, 0);
        let history = new ImageEditorHistoryEntry(this.editor, 'layer_canvas_edit', { layer: this, oldCanvas: oldCanvas, oldOffsetX: this.offsetX, oldOffsetY: this.offsetY, oldRotation: this.rotation, oldWidth: this.width, oldHeight: this.height });
        this.editor.addHistoryEntry(history);
    }

    savePositions() {
        let history = new ImageEditorHistoryEntry(this.editor, 'layer_reposition', { layer: this, oldOffsetX: this.offsetX, oldOffsetY: this.offsetY, oldRotation: this.rotation, oldWidth: this.width, oldHeight: this.height });
        this.editor.addHistoryEntry(history);
    }
}

/**
 * A single history entry for the image editor, for Undo processing.
 */
class ImageEditorHistoryEntry {
    constructor(editor, type, data) {
        this.editor = editor;
        this.type = type;
        this.data = data;
    }

    undo() {
        if (this.type == 'layer_canvas_edit') {
            let oldCanvas = this.data.oldCanvas;
            let layer = this.data.layer;
            if (layer.canvas.width != oldCanvas.width || layer.canvas.height != oldCanvas.height) {
                layer.canvas.width = oldCanvas.width;
                layer.canvas.height = oldCanvas.height;
                layer.ctx = layer.canvas.getContext('2d');
            }
            let ctx = layer.ctx;
            ctx.clearRect(0, 0, ctx.canvas.width, ctx.canvas.height);
            ctx.drawImage(oldCanvas, 0, 0);
            layer.offsetX = this.data.oldOffsetX;
            layer.offsetY = this.data.oldOffsetY;
            layer.rotation = this.data.oldRotation;
            layer.width = this.data.oldWidth;
            layer.height = this.data.oldHeight;
            layer.markContentChanged();
            this.editor.markOutputChanged();
        }
        else if (this.type == 'layer_reposition') {
            this.data.layer.offsetX = this.data.oldOffsetX;
            this.data.layer.offsetY = this.data.oldOffsetY;
            this.data.layer.rotation = this.data.oldRotation;
            this.data.layer.width = this.data.oldWidth;
            this.data.layer.height = this.data.oldHeight;
            this.editor.markOutputChanged();
        }
        else if (this.type == 'layer_add' && this.editor.layers.indexOf(this.data.layer) >= 0) {
            this.editor.removeLayer(this.data.layer, true);
        }
        else if (this.type == 'layer_remove') {
            // TODO: Reinsert at proper index
            this.editor.addLayer(this.data.layer, true);
            this.editor.markOutputChanged();
        }
    }
}

/**
 * The central class managing the image editor interface.
 */
class ImageEditor {
    constructor(div, allowMasks = true, useExperimental = true, doFit = null, signalChanged = null) {
        // Configurables:
        this.zoomRate = 1.1;
        this.gridScale = 4;
        this.backgroundColor = '#202020';
        this.gridColor = '#404040';
        this.uiColor = '#606060';
        this.uiBorderColor = '#b0b0b0';
        this.textColor = '#ffffff';
        this.boundaryColor = '#ffff00';
        // Data:
        this.doFit = doFit;
        this.signalChanged = signalChanged;
        this.onActivate = null;
        this.onDeactivate = null;
        this.changeCount = 0;
        this.active = false;
        this.redrawInterval = null;
        this.redrawFrame = null;
        this.redrawQueued = false;
        this.sceneDirty = true;
        this.viewDirty = true;
        this.overlayDirty = true;
        this.previewState = null;
        this.activePointerId = null;
        this.pointerInsideCanvas = false;
        this.canvasHolder = null;
        this.overlayCanvas = null;
        this.overlayCtx = null;
        this.sceneCtx = null;
        this.rightResizeBar = null;
        this.inputDiv = div;
        this.inputDiv.tabIndex = -1;
        this.leftBar = createDiv(null, 'image_editor_leftbar');
        this.inputDiv.appendChild(this.leftBar);
        this.rightBar = createDiv(null, 'image_editor_rightbar');
        this.rightBar.innerHTML = `<div class="image_editor_newlayer_button basic-button image-editor-close-button interrupt-button" title="Close the Image Editor">&times;</div>`;
        this.rightBar.innerHTML += `<div class="image_editor_newlayer_button basic-button new-image-layer-button" title="New Image Layer">+${allowMasks ? 'Image' : 'Layer'}</div>`;
        if (allowMasks) {
            this.rightBar.innerHTML += `<div class="image_editor_newlayer_button basic-button new-mask-layer-button" title="New Mask Layer">+Mask</div>`;
        }
        this.rightBar.innerHTML += `<div class="image_editor_newlayer_button basic-button new-adjustment-layer-button" title="New Adjustment Layer">+Adjust</div>`;
        this.inputDiv.appendChild(this.rightBar);
        this.rightBar.querySelector('.image-editor-close-button').addEventListener('click', () => {
            this.deactivate();
        });
        this.rightBar.querySelector('.new-image-layer-button').addEventListener('click', () => {
            this.addEmptyLayer();
        });
        if (allowMasks) {
            this.rightBar.querySelector('.new-mask-layer-button').addEventListener('click', () => {
                this.addEmptyMaskLayer();
            });
        }
        this.rightBar.querySelector('.new-adjustment-layer-button').addEventListener('click', () => {
            this.addEmptyAdjustmentLayer();
        });
        this.canvasList = createDiv(null, 'image_editor_canvaslist');
        // canvas entries can be dragged
        this.canvasList.addEventListener('dragover', (e) => {
            e.preventDefault();
            e.dataTransfer.dropEffect = 'move';
        });
        this.canvasList.addEventListener('drop', (e) => {
            let target = findParentOfClass(e.target, 'image_editor_layer_preview');
            if (!target) {
                return;
            }
            let dragIndex = this.layers.indexOf(this.draggingLayer);
            let targetIndex = this.layers.indexOf(target.layer);
            if (dragIndex < 0 || targetIndex < 0 || dragIndex == targetIndex) {
                return;
            }
            this.layers.splice(dragIndex, 1);
            targetIndex = this.layers.indexOf(target.layer);
            if (e.offsetY > target.clientHeight / 2) {
                if (target.nextSibling) {
                    this.canvasList.insertBefore(this.draggingLayer.div, target.nextSibling);
                }
                else {
                    this.canvasList.appendChild(this.draggingLayer.div);
                }
            }
            else {
                targetIndex++;
                this.canvasList.insertBefore(this.draggingLayer.div, target);
            }
            this.layers.splice(targetIndex, 0, this.draggingLayer);
            this.sortLayers();
            this.redraw();
        });
        this.canvasList.addEventListener('dragenter', (e) => {
            e.preventDefault();
        });
        this.rightBar.appendChild(this.canvasList);
        this.bottomBar = createDiv(null, 'image_editor_bottombar');
        this.inputDiv.appendChild(this.bottomBar);
        this.layers = [];
        this.activeLayer = null;
        this.clearVars();
        // Tools:
        this.tools = {};
        this.toolHotkeys = {};
        this.addTool(new ImageEditorToolOptions(this));
        this.addTool(new ImageEditorToolGeneral(this));
        this.addTool(new ImageEditorToolMove(this));
        this.addTool(new ImageEditorToolSelect(this));
        this.addTool(new ImageEditorToolEllipseSelect(this));
        this.addTool(new ImageEditorToolLassoSelect(this));
        this.addTool(new ImageEditorToolPolygonSelect(this));
        this.addTool(new ImageEditorToolMagicWand(this));
        this.addTool(new ImageEditorToolColorSelect(this));
        this.addTool(new ImageEditorToolCrop(this));
        this.addTool(new ImageEditorToolBrush(this, 'brush', 'paintbrush', 'Paintbrush', 'Draw on the image.\nHotKey: B', false, 'b'));
        this.addTool(new ImageEditorToolBrush(this, 'eraser', 'eraser', 'Eraser', 'Erase parts of the image.\nHotKey: E', true, 'e'));
        this.addTool(new ImageEditorToolBucket(this));
        this.addTool(new ImageEditorToolShape(this));
        this.pickerTool = new ImageEditorToolPicker(this, 'picker', 'paintbrush', 'Color Picker', 'Pick a color from the image.');
        this.addTool(this.pickerTool);
        this.addTool(new ImageEditorToolSam2Points(this));
        this.addTool(new ImageEditorToolSam2BBox(this));
        this.activateTool('brush');
        this.maxHistory = 15;
        $('#image_editor_debug_modal').on('hidden.bs.modal', () => {
            document.getElementById('image_editor_debug_images').innerHTML = '';
        });
        let pastebox = document.getElementById('image_editor_paste_pastebox');
        if (pastebox) {
            pastebox.onpaste = (e) => this.handlePasteModalPaste(e);
        }
    }

    clearVars() {
        this.totalLayersEver = 0;
        this.baseImageLayerId = null;
        this.mouseDown = false;
        this.activePointerId = null;
        this.pointerInsideCanvas = false;
        this.zoomLevel = 1;
        this.offsetX = 0;
        this.offsetY = 0;
        this.mouseX = 0;
        this.mouseY = 0;
        this.lastMouseX = 0;
        this.lastMouseY = 0;
        this.realWidth = 512;
        this.realHeight = 512;
        this.finalOffsetX = 0;
        this.finalOffsetY = 0;
        this.selectX = 0;
        this.selectY = 0;
        this.selectWidth = 0;
        this.selectHeight = 0;
        this.hasSelection = false;
        this.selectionMode = 'replace';
        this.selectionFeatherPx = 0;
        this.selectionExpandPx = 0;
        this.selectionSmoothPasses = 0;
        this.selectionTolerance = 32;
        this.selectionContiguous = true;
        this.selectionSampleSource = 'composite';
        this.selectionMaskCanvas = null;
        this.selectionMaskCtx = null;
        this.selectionSourceMaskCanvas = null;
        this.selectionSourceMaskCtx = null;
        this.selectionMaskImageData = null;
        this.selectionCacheVersion = 0;
        this.cropSession = null;
        this.adjustmentMaskEditingLayerId = null;
        this.showAdjustmentMaskOverlay = true;
        this.editHistory = [];
    }

    addHistoryEntry(entry) {
        if (this.editHistory.length >= this.maxHistory) {
            this.editHistory.splice(0, 1);
        }
        this.editHistory.push(entry);
    }

    undoOnce() {
        if (this.editHistory.length > 0) {
            let entry = this.editHistory.pop();
            entry.undo();
            this.redraw();
        }
    }

    addTool(tool) {
        this.tools[tool.id] = tool;
        if (tool.hotkey) {
            this.toolHotkeys[tool.hotkey] = tool.id;
        }
    }

    activateTool(id) {
        let newTool = this.tools[id];
        if (!newTool) {
            throw new Error(`Tool ${id} not found`);
        }
        if (newTool.div && newTool.div.style.display == 'none') {
            return;
        }
        if (this.activeTool && !newTool.isTempTool) {
            this.activeTool.setInactive();
        }
        newTool.setActive();
        this.activeTool = newTool;
        this.queueOverlayRedraw();
    }

    createCanvas() {
        let canvasHolder = createDiv(null, 'image-editor-canvas-holder');
        this.canvasHolder = canvasHolder;
        let canvas = document.createElement('canvas');
        canvas.className = 'image-editor-canvas';
        let overlayCanvas = document.createElement('canvas');
        overlayCanvas.className = 'image-editor-overlay-canvas';
        canvasHolder.appendChild(canvas);
        canvasHolder.appendChild(overlayCanvas);
        if (this.rightBar.parentElement == this.inputDiv) {
            this.inputDiv.insertBefore(canvasHolder, this.rightBar);
        }
        else {
            this.inputDiv.appendChild(canvasHolder);
        }
        this.canvas = canvas;
        this.overlayCanvas = overlayCanvas;
        overlayCanvas.addEventListener('wheel', (e) => this.onMouseWheel(e));
        overlayCanvas.addEventListener('pointerdown', (e) => this.onPointerDown(e));
        overlayCanvas.addEventListener('pointermove', (e) => this.onPointerMove(e));
        overlayCanvas.addEventListener('pointerup', (e) => this.onPointerUp(e));
        overlayCanvas.addEventListener('pointercancel', (e) => this.onPointerCancel(e));
        overlayCanvas.addEventListener('pointerleave', (e) => this.onPointerLeave(e));
        this.inputDiv.addEventListener('keydown', (e) => this.onKeyDown(e));
        this.inputDiv.addEventListener('keyup', (e) => this.onKeyUp(e));
        document.addEventListener('keydown', (e) => this.onGlobalKeyDown(e));
        document.addEventListener('keyup', (e) => this.onGlobalKeyUp(e));
        overlayCanvas.addEventListener('dragover', (e) => {
            e.preventDefault();
            e.stopPropagation();
        });
        overlayCanvas.addEventListener('drop', (e) => this.handleCanvasImageDrop(e));
        canvas.addEventListener('drop', (e) => this.handleCanvasImageDrop(e));
        let onCanvasContextMenu = (e) => {
            let handled = this.activeTool && this.activeTool.onContextMenu(e);
            e.preventDefault();
            if (handled) {
                e.stopPropagation();
            }
        };
        overlayCanvas.addEventListener('contextmenu', (e) => {
            onCanvasContextMenu(e);
        });
        canvas.addEventListener('contextmenu', (e) => {
            onCanvasContextMenu(e);
        });
        this.sceneCtx = canvas.getContext('2d');
        this.ctx = this.sceneCtx;
        this.overlayCtx = overlayCanvas.getContext('2d');
        canvas.style.cursor = 'none';
        overlayCanvas.style.cursor = 'none';
        this.maskHelperCanvas = document.createElement('canvas');
        this.maskHelperCtx = this.maskHelperCanvas.getContext('2d');
        this.ensureSelectionMaskCanvas();
        this.resize();
        this.autoZoom();
    }

    autoZoom() {
        this.zoomLevel = Math.min(this.canvas.width / this.realWidth, this.canvas.height / this.realHeight) * 0.9;
        let [x, y] = this.imageCoordToCanvasCoord(this.realWidth / 2, this.realHeight / 2);
        this.offsetX = this.canvas.width / 2 - x;
        this.offsetY = this.canvas.height / 2 - y;
    }

    handleCanvasImageDrop(e) {
        if (!e.dataTransfer.files || e.dataTransfer.files.length <= 0) {
            return;
        }
        e.preventDefault();
        e.stopPropagation();
        for (let file of e.dataTransfer.files) {
            if (!file.type.startsWith('image/')) {
                continue;
            }
            let reader = new FileReader();
            reader.onload = (ev) => {
                this.addImageLayerFromClipboard(ev.target.result);
            };
            reader.readAsDataURL(file);
        }
    }

    handleAltDown() {
        if (!this.preAltTool) {
            this.preAltTool = this.activeTool;
            this.activateTool('general');
            this.queueOverlayRedraw();
        }
    }

    handleAltUp() {
        if (this.preAltTool) {
            this.activateTool(this.preAltTool.id);
            this.preAltTool = null;
            this.queueOverlayRedraw();
        }
    }

    /**
     * Copies the current selection as image data to the clipboard. No-op if there's no selection.
     * @param {boolean} currentLayerOnly - If true, copy only the active layer in the selection; if false, copy the full composited image.
     * Returns true if the copy was initiated, false otherwise.
     */
    copySelectionToClipboard(currentLayerOnly = false) {
        let bounds = this.getSelectionBounds();
        if (!bounds || bounds.width <= 0 || bounds.height <= 0 || (currentLayerOnly && !this.activeLayer)) {
            doNoticePopover('No selection to copy!', 'notice-pop-red');
            return false;
        }
        let layerOnly = currentLayerOnly ? this.activeLayer : null;
        copyImageToClipboard(this.getSelectionImageData('image/png', layerOnly));
        doNoticePopover('Copied!', 'notice-pop-green');
        return true;
    }

    /**
     * Handles paste in the fallback modal textbox: reads image from e.clipboardData and adds as layer.
     */
    handlePasteModalPaste(e) {
        let items = (e.clipboardData || (e.originalEvent && e.originalEvent.clipboardData)) ? (e.clipboardData || e.originalEvent.clipboardData).items : null;
        if (!items) {
            return;
        }
        for (let i = 0; i < items.length; i++) {
            if (items[i].kind == 'file') {
                let file = items[i].getAsFile();
                if (file && file.type.startsWith('image/')) {
                    e.preventDefault();
                    let reader = new FileReader();
                    reader.onload = (ev) => {
                        this.addImageLayerFromClipboard(ev.target.result);
                    };
                    reader.readAsDataURL(file);
                    return;
                }
            }
        }
    }

    /**
     * Pastes the selection from the clipboard to the image editor as a new image layer.
     * No-op if the clipboard does not contain image data. Shows modal fallback when Clipboard API is unavailable.
     */
    pasteSelectionFromClipboard() {
        if (!navigator.clipboard || !navigator.clipboard.read) {
            let box = document.getElementById('image_editor_paste_pastebox');
            box.value = '';
            $('#image_editor_paste_modal').modal('show');
            box.focus();
            return;
        }
        navigator.clipboard.read().then((items) => {
            let found = false;
            for (let item of items) {
                for (let type of item.types) {
                    if (type.startsWith('image/')) {
                        found = true;
                        item.getType(type).then((blob) => {
                            let reader = new FileReader();
                            reader.onload = (ev) => {
                                this.addImageLayerFromClipboard(ev.target.result);
                            };
                            reader.readAsDataURL(blob);
                        });
                        return;
                    }
                }
            }
            if (!found) {
                doNoticePopover('No image in clipboard', 'notice-pop-red');
            }
        });
    }

    activeElementIsAnInput() {
        return document.activeElement.tagName == 'INPUT' || document.activeElement.tagName == 'TEXTAREA';
    }

    onKeyDown(e) {
        if (!this.active) {
            return;
        }
        if (e.key == 'Alt') {
            e.preventDefault();
            this.handleAltDown();
        }
        if (e.ctrlKey && e.key == 'z') {
            e.preventDefault();
            this.undoOnce();
        }
        // TODO: Expose a keydown event to tools rather than this global handler only
        if (e.ctrlKey && e.key == 'c' && !this.activeElementIsAnInput() && this.activeTool && this.activeTool.id == 'select') {
            this.copySelectionToClipboard(this.activeTool.copyMode == 'layer');
            e.preventDefault();
        }
        if (e.ctrlKey && e.key == 'v' && !this.activeElementIsAnInput()) {
            e.preventDefault();
            this.pasteSelectionFromClipboard();
        }
        if (e.key == 'Delete' && !this.activeElementIsAnInput() && this.activeTool && this.activeLayer) {
            if (this.activeTool.id == 'general') {
                e.preventDefault();
                this.removeLayer(this.activeLayer);
            }
            else if (this.activeTool.id == 'select') {
                e.preventDefault();
                this.clearSelectionOnLayer(this.activeLayer);
            }
        }
        if (!e.ctrlKey && !e.altKey && !e.metaKey && !e.shiftKey) {
            let toolId = this.toolHotkeys[e.key];
            if (toolId) {
                this.activateTool(toolId);
            }
        }
    }

    onGlobalKeyDown(e) {
        if (!this.active) {
            return;
        }
        if (e.key == 'Alt') {
            this.altDown = true;
        }
    }

    onKeyUp() {
    }

    onGlobalKeyUp(e) {
        if (!this.active) {
            return;
        }
        if (e.key == 'Alt') {
            this.altDown = false;
            this.handleAltUp();
        }
    }

    onMouseWheel(e) {
        this.activeTool.onMouseWheel(e);
        if (!e.defaultPrevented) {
            let zoom = Math.pow(this.zoomRate, -e.deltaY / 100);
            let rect = this.canvas.getBoundingClientRect();
            let mouseX = e.clientX - rect.left;
            let mouseY = e.clientY - rect.top;
            let [origX, origY] = this.canvasCoordToImageCoord(mouseX, mouseY);
            this.zoomLevel = Math.max(0.01, Math.min(100, this.zoomLevel * zoom));
            let [newX, newY] = this.canvasCoordToImageCoord(mouseX, mouseY);
            this.offsetX += newX - origX;
            this.offsetY += newY - origY;
            this.queueViewRedraw();
        }
        else {
            this.queueOverlayRedraw();
        }
    }

    getPointerSampleEvents(e) {
        if (this.mouseDown && e.getCoalescedEvents) {
            let samples = e.getCoalescedEvents();
            if (samples && samples.length > 0) {
                return samples;
            }
        }
        return [e];
    }

    onPointerDown(e) {
        if (!this.active) {
            return;
        }
        if (this.activePointerId != null && this.activePointerId != e.pointerId) {
            return;
        }
        this.inputDiv.focus();
        this.pointerInsideCanvas = true;
        this.activePointerId = e.pointerId;
        this.overlayCanvas.setPointerCapture(e.pointerId);
        this.updateMousePosFrom(e);
        this.lastMouseX = this.mouseX;
        this.lastMouseY = this.mouseY;
        if (this.altDown || e.button == 1) {
            this.handleAltDown();
        }
        if (e.button == 2 && this.activeTool && !this.activeTool.onRightMouseDown(e)) {
            this.handleAltDown();
        }
        this.mouseDown = true;
        this.activeTool.onMouseDown(e);
        this.queueOverlayRedraw();
    }

    onPointerMove(e) {
        if (!this.active) {
            return;
        }
        if (this.mouseDown && this.activePointerId != null && this.activePointerId != e.pointerId) {
            return;
        }
        this.pointerInsideCanvas = true;
        let draw = false;
        for (let sample of this.getPointerSampleEvents(e)) {
            this.updateMousePosFrom(sample);
            if (this.isMouseInBox(0, 0, this.canvas.width, this.canvas.height)) {
                this.activeTool.onMouseMove(sample);
                draw = true;
            }
            if (this.activeTool.onGlobalMouseMove(sample)) {
                draw = true;
            }
            this.lastMouseX = this.mouseX;
            this.lastMouseY = this.mouseY;
        }
        if (draw) {
            this.queueOverlayRedraw();
        }
    }

    onPointerUp(e) {
        if (!this.active) {
            return;
        }
        if (this.activePointerId != null && this.activePointerId != e.pointerId) {
            return;
        }
        this.updateMousePosFrom(e);
        if (e.button == 1 || e.button == 2) {
            this.handleAltUp();
        }
        let wasDown = this.mouseDown;
        this.mouseDown = false;
        if (wasDown) {
            this.activeTool.onMouseUp(e);
        }
        if (this.activeTool.onGlobalMouseUp(e) || wasDown) {
            this.queueOverlayRedraw();
        }
        if (this.overlayCanvas.hasPointerCapture(e.pointerId)) {
            this.overlayCanvas.releasePointerCapture(e.pointerId);
        }
        this.activePointerId = null;
        this.lastMouseX = this.mouseX;
        this.lastMouseY = this.mouseY;
    }

    onPointerCancel(e) {
        if (!this.active) {
            return;
        }
        if (this.activePointerId != null && this.activePointerId != e.pointerId) {
            return;
        }
        let wasDown = this.mouseDown;
        this.mouseDown = false;
        if (this.activeTool.onGlobalMouseUp(e) || wasDown) {
            this.queueOverlayRedraw();
        }
        if (this.overlayCanvas.hasPointerCapture(e.pointerId)) {
            this.overlayCanvas.releasePointerCapture(e.pointerId);
        }
        this.activePointerId = null;
    }

    onPointerLeave() {
        if (this.mouseDown) {
            return;
        }
        this.pointerInsideCanvas = false;
        this.mouseX = -10000;
        this.mouseY = -10000;
        this.queueOverlayRedraw();
    }

    updateMousePosFrom(e) {
        let eX = e.clientX;
        let eY = e.clientY;
        let rect = this.canvas.getBoundingClientRect();
        this.mouseX = eX - rect.left;
        this.mouseY = eY - rect.top;
    }

    activate() {
        if (this.onActivate) {
            this.onActivate();
        }
        this.active = true;
        this.inputDiv.style.display = 'inline-block';
        this.doParamHides();
        this.doFit();
        if (!this.canvas) {
            this.createCanvas();
            this.redraw();
        }
        else {
            this.resize();
        }
        if (!this.redrawInterval) {
            this.redrawInterval = setInterval(() => this.queueOverlayRedraw(), 250);
        }
    }

    deactivate() {
        if (this.redrawInterval) {
            clearInterval(this.redrawInterval);
            this.redrawInterval = null;
        }
        if (this.redrawFrame != null) {
            cancelAnimationFrame(this.redrawFrame);
            this.redrawFrame = null;
            this.redrawQueued = false;
        }
        this.clearPreviewState(false);
        if (this.onDeactivate) {
            this.onDeactivate();
        }
        for (let tool of Object.values(this.tools)) {
            if (tool.colorControl) {
                tool.colorControl.closePopout();
            }
        }
        this.active = false;
        this.mouseDown = false;
        this.activePointerId = null;
        this.inputDiv.style.display = 'none';
        this.unhideParams();
        this.doFit();
        if (this.overlayCtx && this.overlayCanvas) {
            this.overlayCtx.clearRect(0, 0, this.overlayCanvas.width, this.overlayCanvas.height);
        }
    }

    queueViewRedraw() {
        this.viewDirty = true;
        this.overlayDirty = true;
        this.queueFrameRedraw();
    }

    queueSceneRedraw() {
        this.sceneDirty = true;
        this.overlayDirty = true;
        this.queueFrameRedraw();
    }

    queueOverlayRedraw() {
        this.overlayDirty = true;
        this.queueFrameRedraw();
    }

    queueFrameRedraw() {
        if (this.redrawQueued) {
            return;
        }
        this.redrawQueued = true;
        this.redrawFrame = requestAnimationFrame(() => {
            this.redrawQueued = false;
            this.redrawFrame = null;
            if (!this.active) {
                return;
            }
            let needsScene = this.sceneDirty || this.viewDirty;
            if (needsScene) {
                this.redrawScene();
            }
            if (needsScene || this.overlayDirty) {
                this.redrawOverlay();
            }
            this.sceneDirty = false;
            this.viewDirty = false;
            this.overlayDirty = false;
        });
    }

    /**
     * Queues a full viewport redraw. Kept for compatibility with older editor code paths.
     */
    queueRedraw() {
        this.queueViewRedraw();
    }

    redraw() {
        if (!this.canvas) {
            return;
        }
        this.redrawScene();
        this.redrawOverlay();
        this.sceneDirty = false;
        this.viewDirty = false;
        this.overlayDirty = false;
    }
    canvasCoordToImageCoord(x, y) {
        return [x / this.zoomLevel - this.offsetX, y / this.zoomLevel - this.offsetY];
    }

    imageCoordToCanvasCoord(x, y) {
        return [(x + this.offsetX) * this.zoomLevel, (y + this.offsetY) * this.zoomLevel];
    }

    isMouseInBox(x, y, width, height) {
        return this.mouseX >= x && this.mouseX < x + width && this.mouseY >= y && this.mouseY < y + height;
    }

    isMouseInCircle(x, y, radius) {
        let dx = this.mouseX - x;
        let dy = this.mouseY - y;
        return dx * dx + dy * dy < radius * radius;
    }

    markOutputChanged() {
        this.changeCount++;
        if (this.signalChanged) {
            this.signalChanged();
        }
    }

    markLayerContentChanged(layer) {
        if (layer) {
            layer.markContentChanged();
        }
        this.markOutputChanged();
    }

    ensureSelectionMaskCanvas(clearCanvas = false) {
        if (!this.selectionMaskCanvas || this.selectionMaskCanvas.width != this.realWidth || this.selectionMaskCanvas.height != this.realHeight) {
            this.selectionMaskCanvas = document.createElement('canvas');
            this.selectionMaskCanvas.width = this.realWidth;
            this.selectionMaskCanvas.height = this.realHeight;
            this.selectionMaskCtx = this.selectionMaskCanvas.getContext('2d');
            clearCanvas = true;
        }
        if (!this.selectionSourceMaskCanvas || this.selectionSourceMaskCanvas.width != this.realWidth || this.selectionSourceMaskCanvas.height != this.realHeight) {
            this.selectionSourceMaskCanvas = document.createElement('canvas');
            this.selectionSourceMaskCanvas.width = this.realWidth;
            this.selectionSourceMaskCanvas.height = this.realHeight;
            this.selectionSourceMaskCtx = this.selectionSourceMaskCanvas.getContext('2d');
            clearCanvas = true;
        }
        if (clearCanvas && this.selectionMaskCtx) {
            this.selectionMaskCtx.clearRect(0, 0, this.selectionMaskCanvas.width, this.selectionMaskCanvas.height);
        }
        if (clearCanvas && this.selectionSourceMaskCtx) {
            this.selectionSourceMaskCtx.clearRect(0, 0, this.selectionSourceMaskCanvas.width, this.selectionSourceMaskCanvas.height);
        }
        this.selectionMaskImageData = null;
        return this.selectionMaskCanvas;
    }

    clearSelectionMask(queueOverlay = true) {
        this.ensureSelectionMaskCanvas(true);
        this.selectX = 0;
        this.selectY = 0;
        this.selectWidth = 0;
        this.selectHeight = 0;
        this.hasSelection = false;
        this.selectionCacheVersion++;
        if (queueOverlay) {
            this.queueOverlayRedraw();
        }
    }

    hasSelectionMask() {
        return !!(this.hasSelection && this.selectionMaskCanvas && this.selectWidth > 0 && this.selectHeight > 0);
    }

    getSelectionMaskData() {
        if (!this.hasSelectionMask()) {
            return null;
        }
        if (!this.selectionMaskImageData) {
            this.selectionMaskImageData = this.selectionMaskCtx.getImageData(0, 0, this.selectionMaskCanvas.width, this.selectionMaskCanvas.height);
        }
        return this.selectionMaskImageData;
    }

    updateSelectionBoundsFromMask() {
        this.ensureSelectionMaskCanvas();
        let imageData = this.selectionMaskCtx.getImageData(0, 0, this.selectionMaskCanvas.width, this.selectionMaskCanvas.height);
        this.selectionMaskImageData = imageData;
        let data = imageData.data;
        let minX = this.selectionMaskCanvas.width;
        let minY = this.selectionMaskCanvas.height;
        let maxX = -1;
        let maxY = -1;
        for (let y = 0; y < this.selectionMaskCanvas.height; y++) {
            let rowIndex = y * this.selectionMaskCanvas.width * 4;
            for (let x = 0; x < this.selectionMaskCanvas.width; x++) {
                if (data[rowIndex + x * 4 + 3] <= 0) {
                    continue;
                }
                if (x < minX) {
                    minX = x;
                }
                if (y < minY) {
                    minY = y;
                }
                if (x > maxX) {
                    maxX = x;
                }
                if (y > maxY) {
                    maxY = y;
                }
            }
        }
        if (maxX < minX || maxY < minY) {
            this.selectX = 0;
            this.selectY = 0;
            this.selectWidth = 0;
            this.selectHeight = 0;
            this.hasSelection = false;
        }
        else {
            this.selectX = minX;
            this.selectY = minY;
            this.selectWidth = maxX - minX + 1;
            this.selectHeight = maxY - minY + 1;
            this.hasSelection = true;
        }
        this.selectionCacheVersion++;
        return this.getSelectionBounds();
    }

    getSelectionBounds() {
        if (!this.hasSelectionMask()) {
            return null;
        }
        return {
            x: this.selectX,
            y: this.selectY,
            width: this.selectWidth,
            height: this.selectHeight
        };
    }

    rebuildSelectionMaskFromSource(queueOverlay = true) {
        this.ensureSelectionMaskCanvas();
        let sourceCanvas = this.selectionSourceMaskCanvas;
        let refinedCanvas = imageEditorRefineSelectionMaskCanvas(sourceCanvas, this.selectionExpandPx, this.selectionSmoothPasses, this.selectionFeatherPx);
        this.selectionMaskCtx.clearRect(0, 0, this.selectionMaskCanvas.width, this.selectionMaskCanvas.height);
        this.selectionMaskCtx.drawImage(refinedCanvas, 0, 0);
        this.selectionMaskImageData = null;
        this.updateSelectionBoundsFromMask();
        if (queueOverlay) {
            this.queueOverlayRedraw();
        }
    }

    commitSelectionMask(maskCanvas, combineMode = null) {
        this.ensureSelectionMaskCanvas();
        let mode = combineMode || this.selectionMode || 'replace';
        let combinedCanvas = document.createElement('canvas');
        combinedCanvas.width = this.realWidth;
        combinedCanvas.height = this.realHeight;
        let combinedCtx = combinedCanvas.getContext('2d');
        if (mode != 'replace') {
            combinedCtx.drawImage(this.selectionSourceMaskCanvas, 0, 0);
        }
        if (maskCanvas) {
            if (mode == 'replace' || mode == 'add') {
                combinedCtx.globalCompositeOperation = 'source-over';
                combinedCtx.drawImage(maskCanvas, 0, 0, this.realWidth, this.realHeight);
            }
            else if (mode == 'subtract') {
                combinedCtx.globalCompositeOperation = 'destination-out';
                combinedCtx.drawImage(maskCanvas, 0, 0, this.realWidth, this.realHeight);
            }
            else if (mode == 'intersect') {
                combinedCtx.globalCompositeOperation = 'destination-in';
                combinedCtx.drawImage(maskCanvas, 0, 0, this.realWidth, this.realHeight);
            }
        }
        this.selectionSourceMaskCtx.clearRect(0, 0, this.selectionSourceMaskCanvas.width, this.selectionSourceMaskCanvas.height);
        this.selectionSourceMaskCtx.drawImage(combinedCanvas, 0, 0);
        this.rebuildSelectionMaskFromSource(true);
    }

    sampleSelectionAtImageCoord(x, y, defaultValue = 1) {
        if (!this.hasSelectionMask()) {
            return defaultValue;
        }
        x = Math.floor(x);
        y = Math.floor(y);
        if (x < 0 || y < 0 || x >= this.realWidth || y >= this.realHeight) {
            return 0;
        }
        let imageData = this.getSelectionMaskData();
        if (!imageData) {
            return 0;
        }
        let index = (y * this.realWidth + x) * 4 + 3;
        return imageData.data[index] / 255;
    }

    getSelectionMaskCanvasForLayer(layer) {
        let maskCanvas = document.createElement('canvas');
        maskCanvas.width = layer.canvas.width;
        maskCanvas.height = layer.canvas.height;
        if (!this.hasSelectionMask()) {
            return maskCanvas;
        }
        let maskCtx = maskCanvas.getContext('2d');
        maskCtx.save();
        layer.configureImageToLayerTransform(maskCtx);
        maskCtx.drawImage(this.selectionMaskCanvas, 0, 0);
        maskCtx.restore();
        return maskCanvas;
    }

    applySelectionMaskClip(ctx, layer) {
        if (!this.hasSelectionMask()) {
            return false;
        }
        let localMask = this.getSelectionMaskCanvasForLayer(layer);
        ctx.save();
        ctx.globalCompositeOperation = 'destination-in';
        ctx.drawImage(localMask, 0, 0);
        ctx.restore();
        return true;
    }

    getSelectionImageData(format = 'image/png', layerOnly = null) {
        let bounds = this.getSelectionBounds();
        if (!bounds) {
            return null;
        }
        let canvas = document.createElement('canvas');
        canvas.width = bounds.width;
        canvas.height = bounds.height;
        let ctx = canvas.getContext('2d');
        if (layerOnly && !layerOnly.isAdjustmentLayer()) {
            layerOnly.drawToBack(ctx, this.finalOffsetX - bounds.x, this.finalOffsetY - bounds.y, 1);
        }
        else {
            this.renderImageLayerStackToContext(ctx, this.finalOffsetX - bounds.x, this.finalOffsetY - bounds.y, 1, false);
        }
        let maskCanvas = document.createElement('canvas');
        maskCanvas.width = bounds.width;
        maskCanvas.height = bounds.height;
        let maskCtx = maskCanvas.getContext('2d');
        maskCtx.drawImage(this.selectionMaskCanvas, bounds.x, bounds.y, bounds.width, bounds.height, 0, 0, bounds.width, bounds.height);
        ctx.save();
        ctx.globalCompositeOperation = 'destination-in';
        ctx.drawImage(maskCanvas, 0, 0);
        ctx.restore();
        return canvas.toDataURL(format);
    }

    getLayerDocumentCanvas(layer) {
        let canvas = imageEditorCreateCanvas(this.realWidth, this.realHeight);
        let ctx = canvas.getContext('2d');
        if (!layer) {
            return canvas;
        }
        if (layer.isAdjustmentLayer()) {
            this.renderImageLayerStackToContext(ctx, this.finalOffsetX, this.finalOffsetY, 1, false);
        }
        else {
            layer.drawToBack(ctx, this.finalOffsetX, this.finalOffsetY, 1);
        }
        return canvas;
    }

    getSelectionSourceCanvas(sampleSource = null) {
        let mode = sampleSource || this.selectionSampleSource || 'composite';
        if (mode == 'layer') {
            return this.getLayerDocumentCanvas(this.activeLayer);
        }
        let canvas = imageEditorCreateCanvas(this.realWidth, this.realHeight);
        let ctx = canvas.getContext('2d');
        this.renderImageLayerStackToContext(ctx, this.finalOffsetX, this.finalOffsetY, 1, false);
        return canvas;
    }

    drawLayerMaskToContext(ctx, layer, offsetX, offsetY, zoom) {
        if (!layer) {
            return;
        }
        if (layer.isAdjustmentLayer()) {
            let x = (offsetX + layer.offsetX) * zoom;
            let y = (offsetY + layer.offsetY) * zoom;
            ctx.drawImage(layer.canvas, 0, 0, layer.canvas.width, layer.canvas.height, x, y, layer.width * zoom, layer.height * zoom);
            return;
        }
        layer.drawToBack(ctx, offsetX, offsetY, zoom);
    }

    renderImageLayerStackToContext(ctx, offsetX, offsetY, zoom, skipPreviewTarget = true) {
        let stackParts = [];
        for (let layer of this.layers) {
            if (skipPreviewTarget && this.shouldSkipLayerInScene(layer)) {
                continue;
            }
            if (layer.isMask) {
                continue;
            }
            if (layer.isAdjustmentLayer()) {
                let snapshotCanvas = document.createElement('canvas');
                snapshotCanvas.width = ctx.canvas.width;
                snapshotCanvas.height = ctx.canvas.height;
                let snapshotCtx = snapshotCanvas.getContext('2d');
                snapshotCtx.drawImage(ctx.canvas, 0, 0);
                let processed = layer.getAdjustmentOutputCanvas(snapshotCanvas, stackParts.join(';'));
                let maskedCanvas = document.createElement('canvas');
                maskedCanvas.width = ctx.canvas.width;
                maskedCanvas.height = ctx.canvas.height;
                let maskedCtx = maskedCanvas.getContext('2d');
                maskedCtx.drawImage(processed, 0, 0);
                maskedCtx.globalCompositeOperation = 'destination-in';
                this.drawLayerMaskToContext(maskedCtx, layer, offsetX, offsetY, zoom);
                ctx.save();
                ctx.globalAlpha = layer.opacity;
                ctx.globalCompositeOperation = layer.globalCompositeOperation || 'source-over';
                ctx.drawImage(maskedCanvas, 0, 0);
                ctx.restore();
            }
            else {
                layer.drawToBack(ctx, offsetX, offsetY, zoom);
            }
            stackParts.push(layer.getVisualSignature(`${layer.id}`));
        }
        return stackParts.join(';');
    }

    renderMaskLayerStackToContext(ctx, offsetX, offsetY, zoom, skipPreviewTarget = true) {
        for (let layer of this.layers) {
            if (skipPreviewTarget && this.shouldSkipLayerInScene(layer)) {
                continue;
            }
            if (!layer.isMask) {
                continue;
            }
            layer.drawToBack(ctx, offsetX, offsetY, zoom);
        }
    }

    renderSelectionMaskToOverlay() {
        if (!this.hasSelectionMask()) {
            return;
        }
        let bounds = this.getSelectionBounds();
        if (!bounds) {
            return;
        }
        let offset = Math.floor(Date.now() / 250) % 8;
        let edgeCanvas = document.createElement('canvas');
        edgeCanvas.width = bounds.width;
        edgeCanvas.height = bounds.height;
        let edgeCtx = edgeCanvas.getContext('2d');
        let edgeImage = edgeCtx.createImageData(bounds.width, bounds.height);
        let edgeData = edgeImage.data;
        let selectionData = this.getSelectionMaskData();
        if (!selectionData) {
            return;
        }
        for (let y = 0; y < bounds.height; y++) {
            let imageY = bounds.y + y;
            for (let x = 0; x < bounds.width; x++) {
                let imageX = bounds.x + x;
                let alpha = this.sampleSelectionAtImageCoord(imageX, imageY, 0);
                if (alpha <= 0) {
                    continue;
                }
                let isEdge = this.sampleSelectionAtImageCoord(imageX - 1, imageY, 0) <= 0
                    || this.sampleSelectionAtImageCoord(imageX + 1, imageY, 0) <= 0
                    || this.sampleSelectionAtImageCoord(imageX, imageY - 1, 0) <= 0
                    || this.sampleSelectionAtImageCoord(imageX, imageY + 1, 0) <= 0;
                if (!isEdge) {
                    continue;
                }
                let index = (y * bounds.width + x) * 4;
                let useLight = ((imageX + imageY + offset) % 8) < 4;
                edgeData[index] = useLight ? 255 : 0;
                edgeData[index + 1] = useLight ? 255 : 0;
                edgeData[index + 2] = useLight ? 255 : 0;
                edgeData[index + 3] = 255;
            }
        }
        edgeCtx.putImageData(edgeImage, 0, 0);
        let [canvasX, canvasY] = this.imageCoordToCanvasCoord(bounds.x, bounds.y);
        this.ctx.save();
        this.ctx.imageSmoothingEnabled = false;
        this.ctx.globalAlpha = 0.2;
        this.ctx.drawImage(this.selectionMaskCanvas, 0, 0, this.realWidth, this.realHeight, this.offsetX * this.zoomLevel, this.offsetY * this.zoomLevel, this.realWidth * this.zoomLevel, this.realHeight * this.zoomLevel);
        this.ctx.globalAlpha = 1;
        this.ctx.drawImage(edgeCanvas, 0, 0, bounds.width, bounds.height, canvasX, canvasY, bounds.width * this.zoomLevel, bounds.height * this.zoomLevel);
        this.ctx.restore();
    }

    isLayerMaskLike(layer) {
        return !!(layer && (layer.isMask || layer.isAdjustmentLayer()));
    }

    beginCropSession(layer) {
        if (!layer || layer.isAdjustmentLayer()) {
            this.cropSession = null;
            return;
        }
        let crop = layer.getCropData();
        this.cropSession = {
            layerId: layer.id,
            originalCropX: crop.cropX,
            originalCropY: crop.cropY,
            originalCropWidth: crop.cropWidth,
            originalCropHeight: crop.cropHeight,
            originalOffsetX: layer.offsetX,
            originalOffsetY: layer.offsetY,
            originalWidth: layer.width,
            originalHeight: layer.height,
            draftCropX: crop.cropX,
            draftCropY: crop.cropY,
            draftCropWidth: crop.cropWidth,
            draftCropHeight: crop.cropHeight,
            dragMode: null
        };
        this.updateCropSessionPreview();
    }

    getCropSessionLayer() {
        if (!this.cropSession) {
            return null;
        }
        return this.layers.find(layer => layer.id == this.cropSession.layerId) || null;
    }

    applyCropDraftToLayerState(layer, cropData, sessionData = null) {
        let base = sessionData || this.cropSession;
        if (!layer || !cropData || !base) {
            return;
        }
        let scaleX = base.originalWidth / Math.max(1, base.originalCropWidth);
        let scaleY = base.originalHeight / Math.max(1, base.originalCropHeight);
        let shiftDisplayX = (cropData.cropX - base.originalCropX) * scaleX;
        let shiftDisplayY = (cropData.cropY - base.originalCropY) * scaleY;
        let rotatedShiftX = shiftDisplayX * Math.cos(layer.rotation) - shiftDisplayY * Math.sin(layer.rotation);
        let rotatedShiftY = shiftDisplayX * Math.sin(layer.rotation) + shiftDisplayY * Math.cos(layer.rotation);
        layer.cropX = cropData.cropX;
        layer.cropY = cropData.cropY;
        layer.cropWidth = cropData.cropWidth;
        layer.cropHeight = cropData.cropHeight;
        layer.width = Math.max(1, cropData.cropWidth * scaleX);
        layer.height = Math.max(1, cropData.cropHeight * scaleY);
        layer.offsetX = base.originalOffsetX + rotatedShiftX;
        layer.offsetY = base.originalOffsetY + rotatedShiftY;
    }

    updateCropSessionPreview() {
        let layer = this.getCropSessionLayer();
        if (!layer || !this.cropSession) {
            this.clearPreviewState(false);
            return;
        }
        let previewLayer = layer.cloneLayerData();
        this.applyCropDraftToLayerState(previewLayer, {
            cropX: this.cropSession.draftCropX,
            cropY: this.cropSession.draftCropY,
            cropWidth: this.cropSession.draftCropWidth,
            cropHeight: this.cropSession.draftCropHeight
        }, this.cropSession);
        this.setPreviewState(layer, previewLayer);
    }

    cancelCropSession() {
        this.cropSession = null;
        this.clearPreviewState(true);
    }

    commitCropSession() {
        let layer = this.getCropSessionLayer();
        if (!layer || !this.cropSession) {
            this.cancelCropSession();
            return;
        }
        this.applyCropDraftToLayerState(layer, {
            cropX: this.cropSession.draftCropX,
            cropY: this.cropSession.draftCropY,
            cropWidth: this.cropSession.draftCropWidth,
            cropHeight: this.cropSession.draftCropHeight
        }, this.cropSession);
        layer.markVisualChanged();
        this.cropSession = null;
        this.clearPreviewState(false);
        this.markOutputChanged();
        this.queueSceneRedraw();
    }

    resetCropForLayer(layer) {
        if (!layer || layer.isAdjustmentLayer()) {
            return;
        }
        this.beginCropSession(layer);
        this.cropSession.draftCropX = 0;
        this.cropSession.draftCropY = 0;
        this.cropSession.draftCropWidth = layer.canvas.width;
        this.cropSession.draftCropHeight = layer.canvas.height;
        this.commitCropSession();
    }

    setLayerDisplaySize(layer, width, height) {
        if (!layer || layer.isAdjustmentLayer()) {
            return;
        }
        width = Math.max(1, Math.round(width));
        height = Math.max(1, Math.round(height));
        layer.width = width;
        layer.height = height;
        layer.markVisualChanged();
        this.markOutputChanged();
        this.queueViewRedraw();
    }

    setPreviewState(targetLayer, previewLayer, options = null) {
        this.previewState = {
            targetLayer: targetLayer,
            previewLayer: previewLayer,
            syncVisualStateFromTarget: options?.syncVisualStateFromTarget === true
        };
        this.queueSceneRedraw();
    }

    clearPreviewState(queueScene = true) {
        if (!this.previewState) {
            return;
        }
        this.previewState = null;
        if (queueScene) {
            this.queueSceneRedraw();
        }
    }

    shouldSkipLayerInScene(layer) {
        return this.previewState && this.previewState.targetLayer == layer;
    }


    setActiveLayer(layer) {
        if (this.previewState && this.previewState.targetLayer != layer) {
            this.clearPreviewState(false);
        }
        if (this.cropSession && (!layer || this.cropSession.layerId != layer.id)) {
            this.cropSession = null;
        }
        if (this.activeLayer && this.activeLayer.div) {
            this.activeLayer.div.classList.remove('image_editor_layer_preview-active');
        }
        if (!layer) {
            let oldLayer = this.activeLayer;
            this.activeLayer = null;
            for (let tool of Object.values(this.tools)) {
                tool.onLayerChanged(oldLayer, null);
            }
            this.redraw();
            return;
        }
        if (this.layers.indexOf(layer) == -1) {
            throw new Error(`layer not found, ${layer}`);
        }
        let oldLayer = this.activeLayer;
        this.activeLayer = layer;
        if (layer && layer.div) {
            layer.div.classList.add('image_editor_layer_preview-active');
        }
        for (let tool of Object.values(this.tools)) {
            tool.onLayerChanged(oldLayer, layer);
        }
        this.redraw();
    }

    clearLayers() {
        this.clearPreviewState(false);
        this.layers = [];
        this.activeLayer = null;
        this.baseImageLayerId = null;
        this.realWidth = 512;
        this.realHeight = 512;
        this.finalOffsetX = 0;
        this.finalOffsetY = 0;
        this.canvasList.innerHTML = '';
        this.clearSelectionMask(false);
    }

    addEmptyMaskLayer() {
        let layer = new ImageEditorLayer(this, this.realWidth, this.realHeight);
        layer.isMask = true;
        this.addLayer(layer);
    }

    addEmptyAdjustmentLayer() {
        let layer = new ImageEditorLayer(this, this.realWidth, this.realHeight);
        layer.layerType = 'adjustment';
        layer.width = this.realWidth;
        layer.height = this.realHeight;
        layer.offsetX = this.finalOffsetX;
        layer.offsetY = this.finalOffsetY;
        layer.rotation = 0;
        layer.ctx.fillStyle = '#ffffff';
        layer.ctx.fillRect(0, 0, layer.canvas.width, layer.canvas.height);
        layer.hasAnyContent = true;
        layer.markContentChanged();
        this.addLayer(layer);
        return layer;
    }

    addEmptyLayer() {
        let layer = new ImageEditorLayer(this, this.realWidth, this.realHeight);
        this.addLayer(layer);
    }

    addImageLayer(img) {
        let layer = new ImageEditorLayer(this, img.naturalWidth || img.width, img.naturalHeight || img.height);
        layer.ctx.drawImage(img, 0, 0);
        layer.hasAnyContent = true;
        this.addLayer(layer);
        return layer;
    }

    /** Gets the bottom-most image layer in the current stack. */
    getBaseImageLayer() {
        return this.layers.find(layer => layer.layerType == 'image') || null;
    }

    /**
     * Loads an image from a URL (data URL or object URL) and adds it as a new layer.
     */
    addImageLayerFromClipboard(src) {
        let img = new Image();
        img.onload = () => {
            let layer = this.addImageLayer(img);
            let baseLayer = this.getBaseImageLayer();
            if (baseLayer && baseLayer != layer) {
                layer.offsetX = baseLayer.offsetX;
                layer.offsetY = baseLayer.offsetY;
                layer.rotation = baseLayer.rotation;
            }
            else {
                let [mouseX, mouseY] = this.canvasCoordToImageCoord(this.mouseX, this.mouseY);
                layer.offsetX = mouseX - layer.width / 2;
                layer.offsetY = mouseY - layer.height / 2;
            }
            this.activateTool('general');
            this.markOutputChanged();
            this.redraw();
        };
        img.src = src;
    }

    removeLayer(layer, skipHistory = false) {
        let index = this.layers.indexOf(layer);
        if (index >= 0) {
            if (!skipHistory) {
                this.addHistoryEntry(new ImageEditorHistoryEntry(this, 'layer_remove', { layer: layer, index: index }));
            }
            this.layers.splice(index, 1);
            this.canvasList.removeChild(layer.div);
            this.canvasList.removeChild(layer.menuPopover);
            if (this.previewState && this.previewState.targetLayer == layer) {
                this.clearPreviewState(false);
            }
            if (this.cropSession && this.cropSession.layerId == layer.id) {
                this.cropSession = null;
            }
            if (this.activeLayer == layer) {
                this.setActiveLayer(this.layers[Math.max(0, index - 1)] || null);
            }
            this.markOutputChanged();
            this.redraw();
        }
    }

    addLayer(layer, skipHistory = false) {
        layer.id = this.totalLayersEver++;
        this.layers.push(layer);
        layer.div = createDiv(null, 'image_editor_layer_preview');
        layer.div.appendChild(layer.canvas);
        let infoDiv = createDiv(null, 'image_editor_layer_info');
        let infoSubDiv = createDiv(null, 'image_editor_layer_info_sub');
        infoSubDiv.innerText = layer.getTypeLabel();
        infoDiv.appendChild(infoSubDiv);
        layer.infoSubDiv = infoSubDiv;
        layer.div.appendChild(infoDiv);
        layer.div.addEventListener('click', (e) => {
            if (e.defaultPrevented) {
                return;
            }
            this.setActiveLayer(layer);
            this.redraw();
        }, true);
        // the div is draggable to re-order:
        layer.div.draggable = true;
        layer.div.addEventListener('dragstart', (e) => {
            e.dataTransfer.setData('text/plain', 'dummy');
            e.dataTransfer.effectAllowed = 'move';
            this.draggingLayer = layer;
        });
        layer.div.addEventListener('dragend', (e) => {
            this.draggingLayer = null;
        });
        layer.div.layer = layer;
        let popId = `image_editor_layer_preview_${layer.id}`;
        let menuPopover = createDiv(`popover_${popId}`, 'sui-popover');
        menuPopover.style.minWidth = '15rem';
        layer.menuPopover = menuPopover;
        layer.createButtons();
        layer.canvas.style.opacity = layer.opacity;
        layer.div.addEventListener('contextmenu', (e) => {
            e.preventDefault();
            menuPopover.style.top = `${e.clientY}px`;
            menuPopover.style.left = `${e.clientX}px`;
            showPopover(popId);
        });
        this.canvasList.appendChild(menuPopover);
        this.canvasList.insertBefore(layer.div, this.canvasList.firstChild);
        this.setActiveLayer(layer);
        this.sortLayers();
        if (!skipHistory) {
            this.addHistoryEntry(new ImageEditorHistoryEntry(this, 'layer_add', { layer: layer }));
        }
        this.queueSceneRedraw();
    }

    duplicateLayer(layer) {
        if (!layer) {
            return null;
        }
        let clone = layer.cloneLayerData();
        this.addLayer(clone);
        return clone;
    }

    sortLayers() {
        let maskLayers = this.layers.filter(layer => layer.isMask);
        let otherLayers = this.layers.filter(layer => !layer.isMask);
        let newLayerList = otherLayers.concat(maskLayers);
        if (newLayerList.map(layer => layer.id).join(',') == this.layers.map(layer => layer.id).join(',')) {
            return;
        }
        this.layers = newLayerList;
        for (let layer of Array.from(this.layers).reverse()) {
            this.canvasList.appendChild(layer.div);
        }
    }

    setBaseImage(img) {
        this.clearLayers();
        let layer = new ImageEditorLayer(this, img.naturalWidth, img.naturalHeight);
        layer.ctx.drawImage(img, 0, 0);
        layer.hasAnyContent = true;
        this.addLayer(layer, true);
        this.baseImageLayerId = layer.id;
        let layer2 = new ImageEditorLayer(this, img.naturalWidth, img.naturalHeight);
        this.addLayer(layer2, true);
        let maskLayer = new ImageEditorLayer(this, img.naturalWidth, img.naturalHeight);
        maskLayer.isMask = true;
        this.addLayer(maskLayer, true);
        this.realWidth = img.naturalWidth;
        this.realHeight = img.naturalHeight;
        if (this.tools['sam2points']) {
            this.tools['sam2points'].layerPoints = new Map();
        }
        if (this.tools['sam2bbox']) {
            this.tools['sam2bbox'].bboxStartX = null;
            this.tools['sam2bbox'].bboxStartY = null;
            this.tools['sam2bbox'].bboxEndX = null;
            this.tools['sam2bbox'].bboxEndY = null;
        }
        this.offsetX = 0
        this.offsetY = 0;
        this.clearSelectionMask(false);
        this.ensureSelectionMaskCanvas();
        this.markOutputChanged();
        if (this.active) {
            this.autoZoom();
            this.redraw();
        }
    }

    doParamHides() {
        for (let paramId of ['input_initimage', 'input_maskimage']) {
            let elem = document.getElementById(paramId);
            if (elem) {
                elem.dataset.has_data = 'true';
                let parent = findParentOfClass(elem, 'auto-input');
                parent.style.display = 'none';
                parent.dataset.visible_controlled = 'true';
            }
        }
    }

    unhideParams() {
        for (let paramId of ['input_initimage', 'input_maskimage']) {
            let elem = document.getElementById(paramId);
            if (elem) {
                delete elem.dataset.has_data;
                let parent = findParentOfClass(elem, 'auto-input');
                parent.style.display = '';
                delete parent.dataset.visible_controlled;
            }
        }
    }

    renderFullGrid(scale, width, color) {
        this.ctx.strokeStyle = color;
        this.ctx.beginPath();
        this.ctx.lineWidth = width;
        let [leftX, topY] = this.canvasCoordToImageCoord(0, 0);
        let [rightX, bottomY] = this.canvasCoordToImageCoord(this.canvas.width, this.canvas.height);
        for (let x = Math.floor(leftX / scale) * scale; x < rightX; x += scale) {
            let [canvasX, _] = this.imageCoordToCanvasCoord(x, 0);
            this.ctx.moveTo(canvasX, 0);
            this.ctx.lineTo(canvasX, this.canvas.height);
        }
        for (let y = Math.floor(topY / scale) * scale; y < bottomY; y += scale) {
            let [_, canvasY] = this.imageCoordToCanvasCoord(0, y);
            this.ctx.moveTo(0, canvasY);
            this.ctx.lineTo(this.canvas.width, canvasY);
        }
        this.ctx.stroke();
    }

    autoWrapText(text, maxWidth) {
        let lines = [];
        let rawLines = text.split('\n');
        for (let rawLine of rawLines) {
            let words = rawLine.split(' ');
            let line = '';
            for (let word of words) {
                let newLine = line + word + ' ';
                if (this.ctx.measureText(newLine).width > maxWidth) {
                    lines.push(line);
                    line = word + ' ';
                }
                else {
                    line = newLine;
                }
            }
            lines.push(line);
        }
        return lines;
    }

    drawTextBubble(text, font, x, y, maxWidth) {
        this.ctx.font = font;
        let lines = this.autoWrapText(text, maxWidth - 10);
        let widest = lines.map(line => this.ctx.measureText(line).width).reduce((a, b) => Math.max(a, b));
        let metrics = this.ctx.measureText(text);
        let fontHeight = metrics.fontBoundingBoxAscent + metrics.fontBoundingBoxDescent;
        this.drawBox(x - 1, y - 1, widest + 10, (fontHeight * lines.length) + 10, this.uiColor, this.uiBorderColor);
        let currentY = y;
        this.ctx.fillStyle = this.textColor;
        this.ctx.textBaseline = 'top';
        for (let line of lines) {
            this.ctx.fillText(line, x + 5, currentY + 5);
            currentY += fontHeight;
        }
    }

    drawBox(x, y, width, height, color, borderColor) {
        this.ctx.fillStyle = color;
        this.ctx.lineWidth = 1;
        this.ctx.beginPath();
        this.ctx.moveTo(x, y);
        this.ctx.lineTo(x + width, y);
        this.ctx.lineTo(x + width, y + height);
        this.ctx.lineTo(x, y + height);
        this.ctx.closePath();
        this.ctx.fill();
        if (borderColor) {
            this.ctx.strokeStyle = borderColor;
            this.ctx.stroke();
        }
    }

    markChanged() {
        this.markOutputChanged();
    }

    resize() {
        if (this.canvas) {
            let rightBarWidth = this.rightBar && this.rightBar.parentElement == this.inputDiv ? this.rightBar.clientWidth : 0;
            let rightResizeBarWidth = this.rightResizeBar && this.rightResizeBar.parentElement == this.inputDiv ? this.rightResizeBar.clientWidth : 0;
            this.canvas.width = Math.max(100, this.inputDiv.clientWidth - this.leftBar.clientWidth - rightBarWidth - rightResizeBarWidth - 1);
            this.canvas.height = Math.max(100, this.inputDiv.clientHeight - this.bottomBar.clientHeight - 1);
            if (this.canvasHolder) {
                this.canvasHolder.style.width = `${this.canvas.width}px`;
                this.canvasHolder.style.height = `${this.canvas.height}px`;
            }
            if (this.overlayCanvas) {
                this.overlayCanvas.width = this.canvas.width;
                this.overlayCanvas.height = this.canvas.height;
            }
            if (this.maskHelperCanvas) {
                this.maskHelperCanvas.width = this.canvas.width;
                this.maskHelperCanvas.height = this.canvas.height;
            }
            this.redraw();
        }
    }

    drawSelectionBox(x, y, width, height, color, spacing, angle, offset = 0) {
        this.ctx.save();
        this.ctx.lineWidth = 1;
        this.ctx.beginPath();
        this.ctx.setLineDash([spacing, spacing]);
        this.ctx.lineDashOffset = offset;
        if (color == 'diff') {
            this.ctx.globalCompositeOperation = 'difference';
            this.ctx.strokeStyle = 'white';
        }
        else {
            this.ctx.strokeStyle = color;
        }
        this.ctx.translate(x + width / 2, y + height / 2);
        this.ctx.rotate(angle);
        this.ctx.moveTo(-width / 2 - 1, -height / 2 - 1);
        this.ctx.lineTo(width / 2 + 1, -height / 2 - 1);
        this.ctx.lineTo(width / 2 + 1, height / 2 + 1);
        this.ctx.lineTo(-width / 2 - 1, height / 2 + 1);
        this.ctx.closePath();
        this.ctx.stroke();
        this.ctx.restore();
    }

    refreshFloatingPanels() {
        for (let tool of Object.values(this.tools)) {
            if (tool.colorControl) {
                tool.colorControl.refreshFloatingPanel();
            }
        }
    }

    redrawScene() {
        if (!this.canvas || !this.sceneCtx) {
            return;
        }
        let priorCtx = this.ctx;
        this.ctx = this.sceneCtx;
        this.ctx.save();
        this.ctx.fillStyle = this.backgroundColor;
        this.ctx.fillRect(0, 0, this.canvas.width, this.canvas.height);
        let gridScale = this.gridScale;
        while (gridScale * this.zoomLevel < 32) {
            gridScale *= 8;
        }
        if (gridScale > this.gridScale) {
            let factor = (gridScale * this.zoomLevel - 32) / (32 * 8);
            let frac = factor * 100;
            this.renderFullGrid(gridScale / 8, 1, `color-mix(in srgb, ${this.gridColor} ${frac}%, ${this.backgroundColor})`);
        }
        this.renderFullGrid(gridScale, 3, this.gridColor);
        let contentCanvas = document.createElement('canvas');
        contentCanvas.width = this.canvas.width;
        contentCanvas.height = this.canvas.height;
        let contentCtx = contentCanvas.getContext('2d');
        this.renderImageLayerStackToContext(contentCtx, this.offsetX, this.offsetY, this.zoomLevel, true);
        this.ctx.drawImage(contentCanvas, 0, 0);
        this.maskHelperCtx.clearRect(0, 0, this.maskHelperCanvas.width, this.maskHelperCanvas.height);
        this.renderMaskLayerStackToContext(this.maskHelperCtx, this.offsetX, this.offsetY, this.zoomLevel, true);
        if (this.activeLayer && !this.previewState) {
            this.ctx.save();
            this.ctx.globalAlpha = this.activeLayer.isMask || this.activeLayer.isAdjustmentLayer() ? 0.8 : 0.3;
            this.ctx.globalCompositeOperation = 'luminosity';
            this.ctx.drawImage(this.maskHelperCanvas, 0, 0);
            if (this.activeLayer.isAdjustmentLayer() && this.showAdjustmentMaskOverlay) {
                this.drawLayerMaskToContext(this.ctx, this.activeLayer, this.offsetX, this.offsetY, this.zoomLevel);
            }
            this.ctx.restore();
        }
        this.ctx.restore();
        this.ctx = priorCtx;
    }

    drawPreviewStateToOverlay() {
        if (!this.previewState || !this.previewState.previewLayer) {
            return;
        }
        let previewLayer = this.previewState.previewLayer;
        if (this.previewState.syncVisualStateFromTarget && this.previewState.targetLayer) {
            previewLayer.copyVisualStateFrom(this.previewState.targetLayer);
        }
        if (previewLayer.isMask || previewLayer.isAdjustmentLayer()) {
            return;
        }
        previewLayer.drawToBack(this.ctx, this.offsetX, this.offsetY, this.zoomLevel);
    }

    drawCurrentMaskOverlay(maskLayer = null) {
        if (!this.activeLayer) {
            return;
        }
        this.ctx.save();
        this.ctx.globalAlpha = this.activeLayer.isMask || this.activeLayer.isAdjustmentLayer() ? 0.8 : 0.3;
        this.ctx.globalCompositeOperation = 'luminosity';
        this.ctx.drawImage(this.maskHelperCanvas, 0, 0);
        if (maskLayer && (!maskLayer.isAdjustmentLayer() || this.showAdjustmentMaskOverlay)) {
            this.drawLayerMaskToContext(this.ctx, maskLayer, this.offsetX, this.offsetY, this.zoomLevel);
        }
        else if (this.activeLayer.isAdjustmentLayer() && this.showAdjustmentMaskOverlay) {
            this.drawLayerMaskToContext(this.ctx, this.activeLayer, this.offsetX, this.offsetY, this.zoomLevel);
        }
        this.ctx.restore();
    }

    redrawOverlay() {
        if (!this.overlayCanvas || !this.overlayCtx) {
            return;
        }
        let priorCtx = this.ctx;
        this.ctx = this.overlayCtx;
        this.overlayCanvas.style.cursor = this.activeTool ? this.activeTool.cursor : 'none';
        this.ctx.clearRect(0, 0, this.overlayCanvas.width, this.overlayCanvas.height);
        this.ctx.save();
        this.drawPreviewStateToOverlay();
        if (this.previewState) {
            let previewMaskLayer = null;
            if (this.previewState.previewLayer && (this.previewState.previewLayer.isMask || this.previewState.previewLayer.isAdjustmentLayer())) {
                previewMaskLayer = this.previewState.previewLayer;
            }
            this.drawCurrentMaskOverlay(previewMaskLayer);
        }
        if (this.activeLayer) {
            let [boundaryX, boundaryY] = this.imageCoordToCanvasCoord(this.finalOffsetX, this.finalOffsetY);
            this.drawSelectionBox(boundaryX, boundaryY, this.realWidth * this.zoomLevel, this.realHeight * this.zoomLevel, this.boundaryColor, 16 * this.zoomLevel, 0);
            if (!this.activeLayer.isAdjustmentLayer()) {
                let [offsetX, offsetY] = this.activeLayer.getOffset();
                [offsetX, offsetY] = this.imageCoordToCanvasCoord(offsetX, offsetY);
                this.drawSelectionBox(offsetX, offsetY, this.activeLayer.width * this.zoomLevel, this.activeLayer.height * this.zoomLevel, this.uiBorderColor, 8 * this.zoomLevel, this.activeLayer.rotation);
            }
            this.renderSelectionMaskToOverlay();
            this.activeTool.draw();
        }
        this.ctx.restore();
        this.ctx = priorCtx;
        this.refreshFloatingPanels();
    }

    getImageWithBounds(x, y, width, height, format = 'image/png', layerOnly = null) {
        x = Math.round(x);
        y = Math.round(y);
        width = Math.round(width);
        height = Math.round(height);
        let canvas = document.createElement('canvas');
        canvas.width = width;
        canvas.height = height;
        let ctx = canvas.getContext('2d');
        if (layerOnly != null && !layerOnly.isAdjustmentLayer()) {
            layerOnly.drawToBack(ctx, this.finalOffsetX - x, this.finalOffsetY - y, 1);
        }
        else {
            this.renderImageLayerStackToContext(ctx, this.finalOffsetX - x, this.finalOffsetY - y, 1, false);
        }
        return canvas.toDataURL(format);
    }

    getFinalImageData(format = 'image/png') {
        let canvas = document.createElement('canvas');
        canvas.width = this.realWidth;
        canvas.height = this.realHeight;
        let ctx = canvas.getContext('2d');
        this.renderImageLayerStackToContext(ctx, this.finalOffsetX, this.finalOffsetY, 1, false);
        return canvas.toDataURL(format);
    }

    getMaximumImageData(format = 'image/png') {
        let canvas = document.createElement('canvas');
        let maxX = this.realWidth, maxY = this.realHeight;
        let minX = 0, minY = 0;
        for (let layer of this.layers) {
            if (layer.layerType == 'image') {
                let [x, y] = layer.getOffset();
                let [w, h] = [layer.width, layer.height];
                minX = Math.min(minX, x);
                minY = Math.min(minY, y);
                maxX = Math.max(maxX, x + w);
                maxY = Math.max(maxY, y + h);
            }
        }
        canvas.width = maxX - minX;
        canvas.height = maxY - minY;
        let ctx = canvas.getContext('2d');
        this.renderImageLayerStackToContext(ctx, -minX, -minY, 1, false);
        return canvas.toDataURL(format);
    }

    getFinalMaskData(format = 'image/png') {
        let canvas = document.createElement('canvas');
        canvas.width = this.realWidth;
        canvas.height = this.realHeight;
        let ctx = canvas.getContext('2d');
        ctx.fillStyle = '#ffffff';
        ctx.fillRect(0, 0, canvas.width, canvas.height);
        if (this.layers.some(l => l.isMask && l.hasAnyContent)) {
            // This is a hack to make transparency in the image layer turn into white on the mask (and areas with image go black unless masked)
            let imgCanvas = document.createElement('canvas');
            imgCanvas.width = this.realWidth / 4;
            imgCanvas.height = this.realHeight / 4;
            let imgctx = imgCanvas.getContext('2d');
            imgctx.clearRect(0, 0, imgCanvas.width, imgCanvas.height);
            for (let layer of this.layers) {
                if (layer.layerType == 'image') {
                    layer.drawToBack(imgctx, this.finalOffsetX, this.finalOffsetY, 1.0 / 4);
                }
            }
            let imageData = imgctx.getImageData(0, 0, imgCanvas.width, imgCanvas.height);
            let buffer = new Uint8ClampedArray(imageData.data.buffer);
            let len = buffer.length;
            for (let i = 0; i < len; i += 4) {
                buffer[i] = 0;
                buffer[i + 1] = 0;
                buffer[i + 2] = 0;
            }
            imageData = new ImageData(buffer, imgCanvas.width, imgCanvas.height);
            imgctx.putImageData(imageData, 0, 0);
            ctx.drawImage(imgCanvas, 0, 0, canvas.width, canvas.height);
            for (let layer of this.layers) {
                if (layer.isMask) {
                    layer.drawToBack(ctx, this.finalOffsetX, this.finalOffsetY, 1);
                }
            }
        }
        // Force to black/white
        let canvas2 = document.createElement('canvas');
        canvas2.width = this.realWidth;
        canvas2.height = this.realHeight;
        let ctx2 = canvas2.getContext('2d');
        ctx2.fillStyle = '#000000';
        ctx2.fillRect(0, 0, canvas2.width, canvas2.height);
        ctx2.globalCompositeOperation = 'luminosity';
        ctx2.drawImage(canvas, 0, 0);
        return canvas2.toDataURL(format);
    }

    clearSelectionOnLayer(layer) {
        if (!this.hasSelectionMask()) {
            return;
        }
        layer.saveBeforeEdit();
        let selectionMask = this.getSelectionMaskCanvasForLayer(layer);
        layer.ctx.save();
        layer.ctx.globalCompositeOperation = 'destination-out';
        layer.ctx.drawImage(selectionMask, 0, 0);
        layer.ctx.restore();
        this.markLayerContentChanged(layer);
        this.redraw();
    }

    /** Shows a debug image in a stacking modal. Accepts a data URL, Image, or Canvas. */
    showDebugImage(imageSource) {
        let container = document.getElementById('image_editor_debug_images');
        let modal = document.getElementById('image_editor_debug_modal');
        let img = document.createElement('img');
        img.style.maxWidth = '100%';
        img.style.display = 'block';
        img.style.marginBottom = '8px';
        img.style.border = '1px solid var(--light-border)';
        if (typeof imageSource == 'string') {
            img.src = imageSource;
        }
        else if (imageSource instanceof HTMLCanvasElement) {
            img.src = imageSource.toDataURL('image/png');
        }
        else if (imageSource instanceof Image) {
            img.src = imageSource.src;
        }
        if (!modal.classList.contains('show')) {
            container.innerHTML = '';
            $(modal).modal('show');
        }
        container.appendChild(img);
    }

    /** Closes the debug image modal and clears its contents. */
    closeDebugImages() {
        let container = document.getElementById('image_editor_debug_images');
        container.innerHTML = '';
        $('#image_editor_debug_modal').modal('hide');
    }
}

let hasInitializedImageEditorHelpers = false;

/** Ensures image editor helper globals are initialized exactly once. */
function ensureImageEditorHelpersInitialized() {
    if (hasInitializedImageEditorHelpers) {
        return;
    }
    hasInitializedImageEditorHelpers = true;
}

function imageEditorCreateCanvas(width, height) {
    let canvas = document.createElement('canvas');
    canvas.width = Math.max(1, Math.round(width));
    canvas.height = Math.max(1, Math.round(height));
    return canvas;
}

function imageEditorCloneCanvas(sourceCanvas) {
    let canvas = imageEditorCreateCanvas(sourceCanvas.width, sourceCanvas.height);
    let ctx = canvas.getContext('2d');
    ctx.drawImage(sourceCanvas, 0, 0);
    return canvas;
}

function imageEditorApplyBasicAdjustmentsToCanvas(sourceCanvas, layer) {
    let saturation = typeof layer.saturation == 'number' ? layer.saturation : 1;
    let lightValue = typeof layer.lightValue == 'number' ? layer.lightValue : 1;
    let contrast = typeof layer.contrast == 'number' ? layer.contrast : 1;
    let hueShift = typeof layer.hueShift == 'number' ? layer.hueShift : 0;
    if (Math.abs(saturation - 1) <= 0.0001 && Math.abs(lightValue - 1) <= 0.0001 && Math.abs(contrast - 1) <= 0.0001 && Math.abs(hueShift) <= 0.0001) {
        return sourceCanvas;
    }
    let canvas = imageEditorCreateCanvas(sourceCanvas.width, sourceCanvas.height);
    let ctx = canvas.getContext('2d');
    ctx.filter = `saturate(${Math.max(0, saturation)}) brightness(${Math.max(0, lightValue)}) contrast(${Math.max(0, contrast)}) hue-rotate(${hueShift}deg)`;
    ctx.drawImage(sourceCanvas, 0, 0);
    return canvas;
}

function imageEditorApplyAdvancedAdjustmentsToCanvas(sourceCanvas, layer) {
    layer.ensureToneBalance();
    let gamma = Math.max(0.01, layer.getNumericAdjustmentValue('gamma', 1));
    let temperature = layer.getNumericAdjustmentValue('temperature', 0);
    let tint = layer.getNumericAdjustmentValue('tint', 0);
    let shadowsValue = layer.getNumericAdjustmentValue('shadows', 0);
    let highlightsValue = layer.getNumericAdjustmentValue('highlights', 0);
    let whitesValue = layer.getNumericAdjustmentValue('whites', 0);
    let blacksValue = layer.getNumericAdjustmentValue('blacks', 0);
    let hasAdjustments = layer.hasToneBalanceAdjustments();
    if (!hasAdjustments) {
        return sourceCanvas;
    }
    let tempCanvas = imageEditorCloneCanvas(sourceCanvas);
    let tempCtx = tempCanvas.getContext('2d');
    let imageData = tempCtx.getImageData(0, 0, tempCanvas.width, tempCanvas.height);
    let data = imageData.data;
    let shadows = layer.toneBalance.shadows;
    let midtones = layer.toneBalance.midtones;
    let highlights = layer.toneBalance.highlights;
    for (let i = 0; i < data.length; i += 4) {
        if (data[i + 3] == 0) {
            continue;
        }
        let r = data[i];
        let g = data[i + 1];
        let b = data[i + 2];
        let luminance = (0.2126 * r + 0.7152 * g + 0.0722 * b) / 255;
        let shadowWeight = Math.max(0, 1 - (luminance / 0.5));
        let highlightWeight = Math.max(0, (luminance - 0.5) / 0.5);
        let midtoneWeight = Math.max(0, 1 - Math.abs(luminance - 0.5) / 0.5);
        let blackWeight = Math.max(0, 1 - luminance / 0.25);
        let whiteWeight = Math.max(0, (luminance - 0.75) / 0.25);
        blackWeight *= blackWeight;
        whiteWeight *= whiteWeight;
        let tonalLift = 255 * (shadowsValue * shadowWeight * 0.35 + highlightsValue * highlightWeight * 0.35 + whitesValue * whiteWeight * 0.45 + blacksValue * blackWeight * 0.45);
        let adjustR = 255 * (shadows.r * shadowWeight + midtones.r * midtoneWeight + highlights.r * highlightWeight);
        let adjustG = 255 * (shadows.g * shadowWeight + midtones.g * midtoneWeight + highlights.g * highlightWeight);
        let adjustB = 255 * (shadows.b * shadowWeight + midtones.b * midtoneWeight + highlights.b * highlightWeight);
        let temperatureAdjust = temperature * 32;
        let tintAdjust = tint * 24;
        let newR = r + tonalLift + adjustR + temperatureAdjust + tintAdjust * 0.35;
        let newG = g + tonalLift + adjustG - tintAdjust * 0.7;
        let newB = b + tonalLift + adjustB - temperatureAdjust + tintAdjust * 0.35;
        if (Math.abs(gamma - 1) > 0.0001) {
            newR = 255 * Math.pow(Math.max(0, Math.min(1, newR / 255)), 1 / gamma);
            newG = 255 * Math.pow(Math.max(0, Math.min(1, newG / 255)), 1 / gamma);
            newB = 255 * Math.pow(Math.max(0, Math.min(1, newB / 255)), 1 / gamma);
        }
        data[i] = Math.max(0, Math.min(255, Math.round(newR)));
        data[i + 1] = Math.max(0, Math.min(255, Math.round(newG)));
        data[i + 2] = Math.max(0, Math.min(255, Math.round(newB)));
    }
    tempCtx.putImageData(imageData, 0, 0);
    return tempCanvas;
}

function imageEditorApplyPresetToCanvas(sourceCanvas, presetId) {
    presetId = (presetId || 'neutral').toLowerCase();
    if (presetId == 'neutral') {
        return sourceCanvas;
    }
    let canvas = imageEditorCloneCanvas(sourceCanvas);
    let ctx = canvas.getContext('2d');
    let imageData = ctx.getImageData(0, 0, canvas.width, canvas.height);
    let data = imageData.data;
    for (let i = 0; i < data.length; i += 4) {
        if (data[i + 3] == 0) {
            continue;
        }
        let r = data[i];
        let g = data[i + 1];
        let b = data[i + 2];
        let luminance = (r + g + b) / 3;
        if (presetId == 'cinematic-warm') {
            r = r * 1.08 + 10;
            g = g * 1.02 + 4;
            b = b * 0.92 - 8;
        }
        else if (presetId == 'teal-orange') {
            if (luminance < 128) {
                r -= 8;
                g += 6;
                b += 12;
            }
            else {
                r += 14;
                g += 6;
                b -= 10;
            }
        }
        else if (presetId == 'cool-fade') {
            r = r * 0.94 + 6;
            g = g * 0.98 + 8;
            b = b * 1.08 + 16;
        }
        else if (presetId == 'noir') {
            r = luminance;
            g = luminance;
            b = luminance;
            let contrast = 1.15;
            r = ((r - 128) * contrast) + 128;
            g = ((g - 128) * contrast) + 128;
            b = ((b - 128) * contrast) + 128;
        }
        else if (presetId == 'vintage-fade') {
            r = r * 1.04 + 14;
            g = g * 0.98 + 8;
            b = b * 0.9 + 2;
            let fade = 12;
            r = (r * 0.92) + fade;
            g = (g * 0.92) + fade;
            b = (b * 0.92) + fade;
        }
        data[i] = Math.max(0, Math.min(255, Math.round(r)));
        data[i + 1] = Math.max(0, Math.min(255, Math.round(g)));
        data[i + 2] = Math.max(0, Math.min(255, Math.round(b)));
    }
    ctx.putImageData(imageData, 0, 0);
    return canvas;
}

function imageEditorApplyLayerEffectsToCanvas(sourceCanvas, effects) {
    let safeEffects = ImageEditorLayer.cloneEffects(effects);
    let canvas = sourceCanvas;
    if (safeEffects.blur > 0.01) {
        let blurCanvas = imageEditorCreateCanvas(canvas.width, canvas.height);
        let blurCtx = blurCanvas.getContext('2d');
        blurCtx.filter = `blur(${Math.max(0.25, safeEffects.blur * 2)}px)`;
        blurCtx.drawImage(canvas, 0, 0);
        canvas = blurCanvas;
    }
    if (safeEffects.noiseReduction > 0.01) {
        let nrCanvas = imageEditorCreateCanvas(canvas.width, canvas.height);
        let nrCtx = nrCanvas.getContext('2d');
        nrCtx.globalAlpha = 1;
        nrCtx.drawImage(canvas, 0, 0);
        nrCtx.globalAlpha = Math.max(0, Math.min(1, safeEffects.noiseReduction * 0.55));
        nrCtx.filter = `blur(${Math.max(0.5, safeEffects.noiseReduction * 1.5)}px)`;
        nrCtx.drawImage(canvas, 0, 0);
        canvas = nrCanvas;
    }
    if (safeEffects.sharpen > 0.01) {
        canvas = imageEditorApplyKernelToCanvas(canvas, [0, -1, 0, -1, 5 + safeEffects.sharpen * 2, -1, 0, -1, 0], 1, 0);
    }
    if (safeEffects.artisticFilter && safeEffects.artisticFilter != 'none') {
        let mode = safeEffects.artisticFilter.toLowerCase();
        if (mode == 'posterize') {
            canvas = imageEditorPosterizeCanvas(canvas, 5);
        }
        else if (mode == 'sketch') {
            canvas = imageEditorSketchCanvas(canvas);
        }
        else if (mode == 'emboss') {
            canvas = imageEditorApplyKernelToCanvas(canvas, [-2, -1, 0, -1, 1, 1, 0, 1, 2], 1, 128);
        }
        else if (mode == 'oil paint lite' || mode == 'oil-paint-lite') {
            canvas = imageEditorOilPaintLiteCanvas(canvas, 4);
        }
    }
    if (safeEffects.glow > 0.01) {
        let glowCanvas = imageEditorCreateCanvas(canvas.width, canvas.height);
        let glowCtx = glowCanvas.getContext('2d');
        glowCtx.drawImage(canvas, 0, 0);
        glowCtx.globalCompositeOperation = 'screen';
        glowCtx.globalAlpha = Math.max(0, Math.min(1, safeEffects.glow * 0.55));
        glowCtx.filter = `blur(${Math.max(1, safeEffects.glow * 4)}px)`;
        glowCtx.drawImage(canvas, 0, 0);
        canvas = glowCanvas;
    }
    if (safeEffects.vignette > 0.01) {
        let vignetteCanvas = imageEditorCloneCanvas(canvas);
        let vignetteCtx = vignetteCanvas.getContext('2d');
        let gradient = vignetteCtx.createRadialGradient(vignetteCanvas.width / 2, vignetteCanvas.height / 2, Math.min(vignetteCanvas.width, vignetteCanvas.height) * 0.15, vignetteCanvas.width / 2, vignetteCanvas.height / 2, Math.max(vignetteCanvas.width, vignetteCanvas.height) * 0.75);
        let opacity = Math.max(0, Math.min(0.85, safeEffects.vignette * 0.8));
        gradient.addColorStop(0, 'rgba(0, 0, 0, 0)');
        gradient.addColorStop(1, `rgba(0, 0, 0, ${opacity})`);
        vignetteCtx.fillStyle = gradient;
        vignetteCtx.fillRect(0, 0, vignetteCanvas.width, vignetteCanvas.height);
        canvas = vignetteCanvas;
    }
    return canvas;
}

function imageEditorApplyKernelToCanvas(sourceCanvas, kernel, divisor = 1, bias = 0) {
    let srcCtx = sourceCanvas.getContext('2d');
    let srcImage = srcCtx.getImageData(0, 0, sourceCanvas.width, sourceCanvas.height);
    let srcData = srcImage.data;
    let destCanvas = imageEditorCreateCanvas(sourceCanvas.width, sourceCanvas.height);
    let destCtx = destCanvas.getContext('2d');
    let destImage = destCtx.createImageData(sourceCanvas.width, sourceCanvas.height);
    let destData = destImage.data;
    for (let y = 0; y < sourceCanvas.height; y++) {
        for (let x = 0; x < sourceCanvas.width; x++) {
            let r = 0;
            let g = 0;
            let b = 0;
            let a = 0;
            let kernelIndex = 0;
            for (let ky = -1; ky <= 1; ky++) {
                for (let kx = -1; kx <= 1; kx++) {
                    let sx = Math.max(0, Math.min(sourceCanvas.width - 1, x + kx));
                    let sy = Math.max(0, Math.min(sourceCanvas.height - 1, y + ky));
                    let sampleIndex = (sy * sourceCanvas.width + sx) * 4;
                    let weight = kernel[kernelIndex++];
                    r += srcData[sampleIndex] * weight;
                    g += srcData[sampleIndex + 1] * weight;
                    b += srcData[sampleIndex + 2] * weight;
                    a = Math.max(a, srcData[sampleIndex + 3]);
                }
            }
            let outIndex = (y * sourceCanvas.width + x) * 4;
            destData[outIndex] = Math.max(0, Math.min(255, Math.round(r / divisor + bias)));
            destData[outIndex + 1] = Math.max(0, Math.min(255, Math.round(g / divisor + bias)));
            destData[outIndex + 2] = Math.max(0, Math.min(255, Math.round(b / divisor + bias)));
            destData[outIndex + 3] = a;
        }
    }
    destCtx.putImageData(destImage, 0, 0);
    return destCanvas;
}

function imageEditorPosterizeCanvas(sourceCanvas, levels) {
    let canvas = imageEditorCloneCanvas(sourceCanvas);
    let ctx = canvas.getContext('2d');
    let imageData = ctx.getImageData(0, 0, canvas.width, canvas.height);
    let data = imageData.data;
    let step = 255 / Math.max(2, levels - 1);
    for (let i = 0; i < data.length; i += 4) {
        data[i] = Math.round(Math.round(data[i] / step) * step);
        data[i + 1] = Math.round(Math.round(data[i + 1] / step) * step);
        data[i + 2] = Math.round(Math.round(data[i + 2] / step) * step);
    }
    ctx.putImageData(imageData, 0, 0);
    return canvas;
}

function imageEditorSketchCanvas(sourceCanvas) {
    let edgeCanvas = imageEditorApplyKernelToCanvas(sourceCanvas, [-1, -1, -1, -1, 8, -1, -1, -1, -1], 1, 0);
    let canvas = imageEditorCloneCanvas(edgeCanvas);
    let ctx = canvas.getContext('2d');
    let imageData = ctx.getImageData(0, 0, canvas.width, canvas.height);
    let data = imageData.data;
    for (let i = 0; i < data.length; i += 4) {
        let value = 255 - Math.max(data[i], data[i + 1], data[i + 2]);
        data[i] = value;
        data[i + 1] = value;
        data[i + 2] = value;
    }
    ctx.putImageData(imageData, 0, 0);
    return canvas;
}

function imageEditorOilPaintLiteCanvas(sourceCanvas, blockSize) {
    let canvas = imageEditorCreateCanvas(sourceCanvas.width, sourceCanvas.height);
    let ctx = canvas.getContext('2d');
    let srcCtx = sourceCanvas.getContext('2d');
    let srcData = srcCtx.getImageData(0, 0, sourceCanvas.width, sourceCanvas.height).data;
    for (let y = 0; y < sourceCanvas.height; y += blockSize) {
        for (let x = 0; x < sourceCanvas.width; x += blockSize) {
            let totalR = 0;
            let totalG = 0;
            let totalB = 0;
            let totalA = 0;
            let count = 0;
            for (let by = 0; by < blockSize; by++) {
                for (let bx = 0; bx < blockSize; bx++) {
                    let sx = x + bx;
                    let sy = y + by;
                    if (sx >= sourceCanvas.width || sy >= sourceCanvas.height) {
                        continue;
                    }
                    let index = (sy * sourceCanvas.width + sx) * 4;
                    totalR += srcData[index];
                    totalG += srcData[index + 1];
                    totalB += srcData[index + 2];
                    totalA += srcData[index + 3];
                    count++;
                }
            }
            if (count == 0) {
                continue;
            }
            ctx.fillStyle = `rgba(${Math.round(totalR / count)}, ${Math.round(totalG / count)}, ${Math.round(totalB / count)}, ${(totalA / count) / 255})`;
            ctx.fillRect(x, y, blockSize, blockSize);
        }
    }
    return canvas;
}

function imageEditorRefineSelectionMaskCanvas(sourceCanvas, expandPx, smoothPasses, featherPx) {
    let canvas = imageEditorCloneCanvas(sourceCanvas);
    if (expandPx > 0) {
        let expanded = imageEditorCreateCanvas(canvas.width, canvas.height);
        let expandedCtx = expanded.getContext('2d');
        for (let y = -expandPx; y <= expandPx; y++) {
            for (let x = -expandPx; x <= expandPx; x++) {
                if (x * x + y * y > expandPx * expandPx) {
                    continue;
                }
                expandedCtx.drawImage(canvas, x, y);
            }
        }
        canvas = expanded;
    }
    for (let i = 0; i < smoothPasses; i++) {
        let smooth = imageEditorCreateCanvas(canvas.width, canvas.height);
        let smoothCtx = smooth.getContext('2d');
        smoothCtx.filter = 'blur(1px)';
        smoothCtx.drawImage(canvas, 0, 0);
        let imageData = smoothCtx.getImageData(0, 0, smooth.width, smooth.height);
        let data = imageData.data;
        for (let p = 0; p < data.length; p += 4) {
            let alpha = data[p + 3] >= 96 ? 255 : 0;
            data[p] = 255;
            data[p + 1] = 255;
            data[p + 2] = 255;
            data[p + 3] = alpha;
        }
        smoothCtx.putImageData(imageData, 0, 0);
        canvas = smooth;
    }
    if (featherPx > 0) {
        let feathered = imageEditorCreateCanvas(canvas.width, canvas.height);
        let featherCtx = feathered.getContext('2d');
        featherCtx.filter = `blur(${Math.max(0.5, featherPx)}px)`;
        featherCtx.drawImage(canvas, 0, 0);
        canvas = feathered;
    }
    return canvas;
}
