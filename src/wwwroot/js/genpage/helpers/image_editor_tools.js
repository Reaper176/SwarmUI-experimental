
/**
 * Base class for an image editor tool, such as Paintbrush or the General tool.
 */
class ImageEditorTool {
    constructor(editor, id, icon, name, description, hotkey = null) {
        this.editor = editor;
        this.isTempTool = false;
        this.isMaskOnly = false;
        this.id = id;
        this.icon = icon;
        this.iconImg = new Image();
        this.iconImg.src = `imgs/${icon}.png`;
        this.name = name;
        this.description = description;
        this.active = false;
        this.cursor = 'crosshair';
        this.hotkey = hotkey;
        this.makeDivs();
    }

    makeDivs() {
        this.infoBubble = createDiv(null, 'sui-popover');
        this.infoBubble.innerHTML = `<div class="image-editor-info-bubble-title">${escapeHtml(this.name)}</div><div class="image-editor-info-bubble-description">${escapeHtml(this.description)}</div>`;
        this.div = document.createElement('div');
        this.div.className = 'image-editor-tool';
        this.div.style.backgroundImage = `url(imgs/${this.icon}.png)`;
        this.div.addEventListener('click', () => this.onClick());
        this.div.addEventListener('mouseenter', () => {
            this.infoBubble.style.top = `${this.div.offsetTop}px`;
            this.infoBubble.style.left = `${this.div.offsetLeft + this.div.clientWidth + 5}px`;
            this.infoBubble.classList.add('sui-popover-visible');
        });
        this.div.addEventListener('mouseleave', () => {
            this.infoBubble.classList.remove('sui-popover-visible');
        });
        this.editor.leftBar.appendChild(this.infoBubble);
        this.editor.leftBar.appendChild(this.div);
        this.configDiv = document.createElement('div');
        this.configDiv.className = 'image-editor-tool-bottombar';
        this.configDiv.style.display = 'none';
        this.editor.bottomBar.appendChild(this.configDiv);
    }

    onClick() {
        this.editor.activateTool(this.id);
    }

    setActive() {
        if (this.active) {
            return;
        }
        this.active = true;
        this.div.classList.add('image-editor-tool-selected');
        this.configDiv.style.display = 'flex';
    }

    setInactive() {
        if (!this.active) {
            return;
        }
        this.active = false;
        this.div.classList.remove('image-editor-tool-selected');
        this.configDiv.style.display = 'none';
    }

    draw() {
    }

    drawCircleBrush(x, y, radius) {
        this.editor.ctx.strokeStyle = '#ffffff';
        this.editor.ctx.lineWidth = 1;
        this.editor.ctx.globalCompositeOperation = 'difference';
        this.editor.ctx.beginPath();
        this.editor.ctx.arc(x, y, radius, 0, 2 * Math.PI);
        this.editor.ctx.stroke();
        this.editor.ctx.globalCompositeOperation = 'source-over';
    }

    onMouseDown(e) {
    }

    onMouseUp(e) {
    }

    onMouseMove(e) {
    }

    onMouseWheel(e) {
    }

    onGlobalMouseMove(e) {
        return false;
    }

    onGlobalMouseUp(e) {
        return false;
    }

    onContextMenu(e) {
        return false;
    }

    onLayerChanged(oldLayer, newLayer) {
        if (this.isMaskOnly) {
            let isMask = newLayer && newLayer.isMask;
            this.div.style.display = isMask ? '' : 'none';
            if (!isMask && this.active) {
                this.editor.activateTool('brush');
            }
        }
    }

    /** Returns the current selection rectangle in layer-local pixel coordinates, or null if no selection is active. */
    getSelectionBoundsInLayer(layer) {
        if (!this.editor.hasSelectionMask()) {
            return null;
        }
        let selectionMask = this.editor.getSelectionMaskCanvasForLayer(layer);
        let imageData = selectionMask.getContext('2d').getImageData(0, 0, selectionMask.width, selectionMask.height).data;
        let minX = selectionMask.width;
        let minY = selectionMask.height;
        let maxX = -1;
        let maxY = -1;
        for (let y = 0; y < selectionMask.height; y++) {
            for (let x = 0; x < selectionMask.width; x++) {
                if (imageData[(y * selectionMask.width + x) * 4 + 3] <= 0) {
                    continue;
                }
                minX = Math.min(minX, x);
                minY = Math.min(minY, y);
                maxX = Math.max(maxX, x);
                maxY = Math.max(maxY, y);
            }
        }
        if (maxX < minX || maxY < minY) {
            return null;
        }
        return {
            minX: minX,
            minY: minY,
            maxX: maxX + 1,
            maxY: maxY + 1
        };
    }

    /** Returns the current selection quadrilateral in layer-local pixel coordinates, or null if no selection is active. */
    getSelectionQuadInLayer(layer) {
        let bounds = this.editor.getSelectionBounds();
        if (!bounds) {
            return null;
        }
        let x1 = bounds.x;
        let y1 = bounds.y;
        let x2 = bounds.x + bounds.width;
        let y2 = bounds.y + bounds.height;
        let corners = [
            [x1, y1],
            [x2, y1],
            [x2, y2],
            [x1, y2]
        ];
        let quad = [];
        for (let i = 0; i < corners.length; i++) {
            let [ix, iy] = corners[i];
            let [cx, cy] = this.editor.imageCoordToCanvasCoord(ix, iy);
            let [lx, ly] = layer.canvasCoordToLayerCoord(cx, cy);
            quad.push([lx, ly]);
        }
        return quad;
    }

    /** Applies the current selection as a clip path on the given canvas context, in layer-local coordinates. No-op if no selection. */
    applySelectionClip(ctx, layer) {
        return this.editor.applySelectionMaskClip(ctx, layer);
    }
}

/**
 * A special temporary tool, a wrapper of the base tool class that prevents default behaviors.
 */
class ImageEditorTempTool extends ImageEditorTool {
    constructor(editor, id, icon, name, description, hotkey = null) {
        super(editor, id, icon, name, description, hotkey);
        this.isTempTool = true;
    }

    makeDivs() {
    }

    setActive() {
        if (this.active) {
            return;
        }
        this.active = true;
    }

    setInactive() {
        if (!this.active) {
            return;
        }
        this.active = false;
    }
}

/**
 * A middle-class for tools with color controls (color picker, swatch, eyedropper, mask/image color memory).
 */
class ImageEditorToolWithColor extends ImageEditorTool {
    constructor(editor, id, icon, name, description, defaultColor = '#ffffff', hotkey = null) {
        super(editor, id, icon, name, description, hotkey);
        this.color = defaultColor;
        this.imageColor = defaultColor;
        this.maskColor = '#ffffff';
    }

    getColorControlsHTML() {
        return `
        <div class="image-editor-tool-block tool-block-nogrow">
            <label>Color:&nbsp;</label>
            <input type="text" class="auto-number id-col1" style="width:75px;flex-grow:0;" value="${this.color}">
            <div class="color-picker-swatch-inline id-col2" style="background-color:${this.color};" title="Open color picker"></div>
            <button class="basic-button color-picker-eyedrop-button id-col3" title="Pick color from canvas"></button>
        </div>`;
    }

    wireColorControls() {
        this.colorText = this.configDiv.querySelector('.id-col1');
        this.colorSelector = this.configDiv.querySelector('.id-col2');
        this.colorPickButton = this.configDiv.querySelector('.id-col3');
        this.colorText.readOnly = true;
        this.colorText.style.cursor = 'pointer';
        this.colorBlock = this.colorText.closest('.image-editor-tool-block');
        let openPickerForThis = (focusHex) => {
            if (colorPickerHelper.isOpen && colorPickerHelper.anchorElement == this.colorBlock) {
                colorPickerHelper.close();
            }
            else {
                let isMask = this.editor.isLayerMaskLike(this.editor.activeLayer);
                colorPickerHelper.open(this.colorBlock, this.color, (newColor) => {
                    this.colorText.value = newColor;
                    this.colorSelector.style.backgroundColor = newColor;
                    this.onConfigChange();
                }, isMask);
                if (focusHex) {
                    colorPickerHelper.focusHex();
                }
            }
        };
        this.colorText.addEventListener('click', () => {
            openPickerForThis(true);
        });
        this.colorSelector.addEventListener('click', () => {
            openPickerForThis(false);
        });
        this.colorPickButton.addEventListener('click', () => {
            if (this.colorPickButton.classList.contains('interrupt-button')) {
                this.colorPickButton.classList.remove('interrupt-button');
                this.editor.activateTool(this.id);
            }
            else {
                this.colorPickButton.classList.add('interrupt-button');
                this.editor.pickerTool.toolFor = this;
                this.editor.activateTool('picker');
            }
        });
    }

    setColor(col) {
        this.color = col;
        this.colorText.value = col;
        this.colorSelector.style.backgroundColor = col;
        this.colorPickButton.classList.remove('interrupt-button');
    }

    onLayerChanged(oldLayer, newLayer) {
        super.onLayerChanged(oldLayer, newLayer);
        if (!this.colorText) {
            return;
        }
        let wasMask = this.editor.isLayerMaskLike(oldLayer);
        let isMask = this.editor.isLayerMaskLike(newLayer);
        if (wasMask) {
            this.maskColor = this.color;
        }
        else {
            this.imageColor = this.color;
        }
        if (isMask) {
            this.setColor(colorPickerHelper.hexToGrayscale(this.maskColor));
        }
        else {
            this.setColor(this.imageColor);
        }
    }
}

/**
 * The special extra options tool.
 */
class ImageEditorToolOptions extends ImageEditorTool {
    constructor(editor) {
        super(editor, 'options', 'dotdotdot', 'Options', 'Additional advanced options for the image editor.');
        this.optionButtons = [
            { key: 'Download Current Image', action: () => {
                let link = document.createElement('a');
                link.href = this.editor.getFinalImageData();
                link.download = 'image.png';
                link.click();
            }},
            { key: 'Download Full Canvas', action: () => {
                let link = document.createElement('a');
                link.href = this.editor.getMaximumImageData();
                link.download = 'canvas.png';
                link.click();
            }},
            { key: 'Download Mask', action: () => {
                let link = document.createElement('a');
                link.href = this.editor.getFinalMaskData();
                link.download = 'mask.png';
                link.click();
            }},
            { key: 'Copy Selection (Final Image)', action: () => {
                this.editor.copySelectionToClipboard(false);
            }},
            { key: 'Copy Selection (Current Layer)', action: () => {
                this.editor.copySelectionToClipboard(true);
            }},
            { key: 'Paste Image as Layer', action: () => {
                this.editor.pasteSelectionFromClipboard();
            }},
            { key: 'Flip / Mirror Active Layer Horizontal', action: () => {
                if (!this.editor.activeLayer) {
                    doNoticePopover('No active layer selected!', 'notice-pop-red');
                    return;
                }
                this.editor.activeLayer.flipHorizontal();
            }},
            { key: 'Flip / Mirror Active Layer Vertical', action: () => {
                if (!this.editor.activeLayer) {
                    doNoticePopover('No active layer selected!', 'notice-pop-red');
                    return;
                }
                this.editor.activeLayer.flipVertical();
            }},
        ];
    }

    onClick() {
        let rect = this.div.getBoundingClientRect();
        new AdvancedPopover('imageeditor_options_popover', this.optionButtons, false, rect.x, rect.y + this.div.offsetHeight + 6, document.body, null, null, 999999, false);
    }
}

/**
 * The generic common tool (can be activated freely with the Alt key).
 */
class ImageEditorToolGeneral extends ImageEditorTool {
    constructor(editor) {
        super(editor, 'general', 'mouse', 'General', 'General tool. Lets you move around the canvas, or adjust size of current layer.\nWhile resizing an object, hold CTRL to snap-to-grid, or hold SHIFT to disable aspect preservation.\nResize edges also snap near the base image edges.\nThe general tool can be activated at any time with the Alt key.\nHotKey: G', 'g');
        this.currentDragCircle = null;
        this.rotateIcon = new Image();
        this.rotateIcon.src = 'imgs/canvas_rotate.png';
        this.moveIcon = new Image();
        this.moveIcon.src = 'imgs/canvas_move.png';
    }

    fixCursor() {
        this.cursor = this.editor.mouseDown ? 'grabbing' : 'crosshair';
    }

    activeLayerControlCircles() {
        if (!this.editor.activeLayer || this.editor.activeLayer.isAdjustmentLayer()) {
            return [];
        }
        let [offsetX, offsetY] = this.editor.imageCoordToCanvasCoord(this.editor.activeLayer.offsetX, this.editor.activeLayer.offsetY);
        let [width, height] = [this.editor.activeLayer.width * this.editor.zoomLevel, this.editor.activeLayer.height * this.editor.zoomLevel];
        let circles = [];
        let radius = 4;
        circles.push({name: 'top-left', radius: radius, x: offsetX - radius / 2, y: offsetY - radius / 2});
        circles.push({name: 'top-right', radius: radius, x: offsetX + width + radius / 2, y: offsetY - radius / 2});
        circles.push({name: 'bottom-left', radius: radius, x: offsetX - radius / 2, y: offsetY + height + radius / 2});
        circles.push({name: 'bottom-right', radius: radius, x: offsetX + width + radius / 2, y: offsetY + height + radius / 2});
        circles.push({name: 'center-top', radius: radius, x: offsetX + width / 2, y: offsetY - radius / 2});
        circles.push({name: 'center-bottom', radius: radius, x: offsetX + width / 2, y: offsetY + height + radius / 2});
        circles.push({name: 'center-left', radius: radius, x: offsetX - radius / 2, y: offsetY + height / 2});
        circles.push({name: 'center-right', radius: radius, x: offsetX + width + radius / 2, y: offsetY + height / 2});
        circles.push({name: 'positioner', radius: radius * 2, x: offsetX + width / 2, y: offsetY - radius * 8, icon: this.moveIcon});
        circles.push({name: 'rotator', radius: radius * 2, x: offsetX + width / 2, y: offsetY - radius * 16, icon: this.rotateIcon});
        let angle = this.editor.activeLayer.rotation;
        if (angle != 0) {
            for (let circle of circles) {
                circle.x = Math.round(circle.x);
                circle.y = Math.round(circle.y);
                let [cx, cy] = [offsetX + width / 2, offsetY + height / 2];
                let [x, y] = [circle.x - cx, circle.y - cy];
                [x, y] = [x * Math.cos(angle) - y * Math.sin(angle), x * Math.sin(angle) + y * Math.cos(angle)];
                [circle.x, circle.y] = [x + cx, y + cy];
            }
        }
        return circles;
    }

    getControlCircle(name) {
        return this.activeLayerControlCircles().find(c => c.name == name);
    }

    getResizeSnapThreshold() {
        return 12 / this.editor.zoomLevel;
    }

    snapLayerResizeToBaseImage(target, handleDef) {
        let baseLayer = this.editor.getBaseImageLayer();
        if (!baseLayer || baseLayer == target || Math.abs(baseLayer.rotation - target.rotation) > 0.0001) {
            return;
        }
        let [moveLeft, moveRight, moveTop, moveBottom] = handleDef;
        let threshold = this.getResizeSnapThreshold();
        let targetRight = target.offsetX + target.width;
        let targetBottom = target.offsetY + target.height;
        let baseRight = baseLayer.offsetX + baseLayer.width;
        let baseBottom = baseLayer.offsetY + baseLayer.height;
        if (moveLeft && Math.abs(target.offsetX - baseLayer.offsetX) <= threshold) {
            target.offsetX = baseLayer.offsetX;
            target.width = Math.max(1, targetRight - target.offsetX);
        }
        if (moveRight && Math.abs(targetRight - baseRight) <= threshold) {
            target.width = Math.max(1, baseRight - target.offsetX);
        }
        if (moveTop && Math.abs(target.offsetY - baseLayer.offsetY) <= threshold) {
            target.offsetY = baseLayer.offsetY;
            target.height = Math.max(1, targetBottom - target.offsetY);
        }
        if (moveBottom && Math.abs(targetBottom - baseBottom) <= threshold) {
            target.height = Math.max(1, baseBottom - target.offsetY);
        }
    }

    draw() {
        this.fixCursor();
        for (let circle of this.activeLayerControlCircles()) {
            this.editor.ctx.strokeStyle = '#ffffff';
            this.editor.ctx.fillStyle = '#000000';
            if (this.editor.isMouseInCircle(circle.x, circle.y, circle.radius)) {
                if (this.editor.overlayCanvas) {
                    this.editor.overlayCanvas.style.cursor = 'grab';
                }
                this.editor.ctx.strokeStyle = '#000000';
                this.editor.ctx.fillStyle = '#ffffff';
            }
            this.editor.ctx.lineWidth = 1;
            if (circle.icon) {
                this.editor.ctx.save();
                this.editor.ctx.filter = 'invert(1)';
                for (let offset of [[-1, -1], [1, -1], [-1, 1], [1, 1]]) {
                    this.editor.ctx.drawImage(circle.icon, circle.x - circle.radius + offset[0], circle.y - circle.radius + offset[1], circle.radius * 2, circle.radius * 2);
                }
                this.editor.ctx.restore();
                this.editor.ctx.drawImage(circle.icon, circle.x - circle.radius, circle.y - circle.radius, circle.radius * 2, circle.radius * 2);
            }
            else {
                this.editor.ctx.beginPath();
                this.editor.ctx.arc(circle.x, circle.y, circle.radius, 0, 2 * Math.PI);
                this.editor.ctx.fill();
                this.editor.ctx.stroke();
            }
        }
    }

    onMouseDown(e) {
        this.fixCursor();
        this.currentDragCircle = null;
        if (!this.editor.activeLayer || this.editor.activeLayer.isAdjustmentLayer()) {
            return;
        }
        for (let circle of this.activeLayerControlCircles()) {
            if (this.editor.isMouseInCircle(circle.x, circle.y, circle.radius)) {
                this.editor.activeLayer.savePositions();
                this.currentDragCircle = circle.name;
                break;
            }
        }
    }

    onMouseUp(e) {
        this.fixCursor();
        this.currentDragCircle = null;
    }

    onGlobalMouseMove(e) {
        if (this.editor.mouseDown) {
            let dx = (this.editor.mouseX - this.editor.lastMouseX) / this.editor.zoomLevel;
            let dy = (this.editor.mouseY - this.editor.lastMouseY) / this.editor.zoomLevel;
            let target = this.editor.activeLayer;
            if (!target) {
                return false;
            }
            let [mouseX, mouseY] = this.editor.canvasCoordToImageCoord(this.editor.mouseX, this.editor.mouseY);
            if (this.currentDragCircle == 'rotator') {
                let centerX = target.offsetX + target.width / 2;
                let centerY = target.offsetY + target.height / 2;
                target.rotation = Math.atan2(mouseY - centerY, mouseX - centerX) + Math.PI / 2;
                if (e.ctrlKey) {
                    target.rotation = Math.round(target.rotation / (Math.PI / 16)) * (Math.PI / 16);
                }
                this.editor.markOutputChanged();
                this.editor.queueViewRedraw();
            }
            else if (this.currentDragCircle) {
                let current = this.getControlCircle(this.currentDragCircle);
                let [circleX, circleY] = this.editor.canvasCoordToImageCoord(current.x, current.y);
                let roundFactor = 1;
                if (e.ctrlKey) {
                    roundFactor = 8;
                    while (roundFactor * this.editor.zoomLevel < 16) {
                        roundFactor *= 4;
                    }
                }
                function applyRotate(x, y, angle = null) {
                    let [cx, cy] = [target.offsetX + target.width / 2, target.offsetY + target.height / 2];
                    if (angle == null) {
                        angle = target.rotation;
                    }
                    [x, y] = [x - cx, y - cy];
                    [x, y] = [x * Math.cos(angle) - y * Math.sin(angle), x * Math.sin(angle) + y * Math.cos(angle)];
                    [x, y] = [x + cx, y + cy];
                    return [x, y];
                }
                if (!e.shiftKey && !current.name.startsWith('center') && current.name != 'positioner') {
                    let [cX, cY] = [target.offsetX + target.width / 2, target.offsetY + target.height / 2];
                    let [dirX, dirY] = [circleX - cX, circleY - cY];
                    let lineLen = Math.sqrt(dirX * dirX + dirY * dirY);
                    [dirX, dirY] = [dirX / lineLen, dirY / lineLen];
                    let [vX, vY] = [mouseX - cX, mouseY - cY];
                    let d = vX * dirX + vY * dirY;
                    [mouseX, mouseY] = [cX + dirX * d, cY + dirY * d];
                }
                let dx = Math.round(mouseX / roundFactor) * roundFactor - circleX;
                let dy = Math.round(mouseY / roundFactor) * roundFactor - circleY;
                if (current.name == 'positioner') {
                    target.offsetX += dx;
                    target.offsetY += dy;
                }
                else {
                    [dx, dy] = [dx * Math.cos(-target.rotation) - dy * Math.sin(-target.rotation), dx * Math.sin(-target.rotation) + dy * Math.cos(-target.rotation)];
                    let [origX, origY] = [target.offsetX, target.offsetY];
                    let [origWidth, origHeight] = [target.width, target.height];
                    let handleDef = {
                        'top-left': [true, false, true, false],
                        'top-right': [false, true, true, false],
                        'bottom-left': [true, false, false, true],
                        'bottom-right': [false, true, false, true],
                        'center-top': [false, false, true, false],
                        'center-bottom': [false, false, false, true],
                        'center-left': [true, false, false, false],
                        'center-right': [false, true, false, false],
                    }[current.name];
                    if (handleDef) {
                        let [moveLeft, moveRight, moveTop, moveBottom] = handleDef;
                        let anchorXFrac = moveLeft ? 1 : (moveRight ? 0 : 0.5);
                        let anchorYFrac = moveTop ? 1 : (moveBottom ? 0 : 0.5);
                        let [origAnchorX, origAnchorY] = applyRotate(origX + anchorXFrac * origWidth, origY + anchorYFrac * origHeight);
                        if (moveLeft) {
                            let wc = Math.min(dx, target.width - 1);
                            target.offsetX += wc;
                            target.width -= wc;
                        }
                        else if (moveRight) {
                            target.width += Math.max(dx, 1 - target.width);
                        }
                        if (moveTop) {
                            let hc = Math.min(dy, target.height - 1);
                            target.offsetY += hc;
                            target.height -= hc;
                        }
                        else if (moveBottom) {
                            target.height += Math.max(dy, 1 - target.height);
                        }
                        let [newAnchorX, newAnchorY] = applyRotate(target.offsetX + anchorXFrac * target.width, target.offsetY + anchorYFrac * target.height);
                        target.offsetX += origAnchorX - newAnchorX;
                        target.offsetY += origAnchorY - newAnchorY;
                        this.snapLayerResizeToBaseImage(target, handleDef);
                    }
                }
                this.editor.markOutputChanged();
                this.editor.queueViewRedraw();
            }
            else {
                this.editor.offsetX += dx;
                this.editor.offsetY += dy;
                this.editor.queueViewRedraw();
            }
            return true;
        }
        return false;
    }
}

/**
 * The layer-move tool.
 */
class ImageEditorToolMove extends ImageEditorTool {
    constructor(editor) {
        super(editor, 'move', 'move', 'Move', 'Free-move the current layer.\nHold SHIFT to lock to flat directions (45/90 degree movements only).\nHold CTRL to snap to grid (32px).\nHotKey: M', 'm');
        this.startingX = null;
        this.startingY = null;
    }

    onMouseDown(e) {
        if (!this.editor.activeLayer || this.editor.activeLayer.isAdjustmentLayer()) {
            return;
        }
        this.startingX = this.editor.activeLayer.offsetX;
        this.startingY = this.editor.activeLayer.offsetY;
        this.moveX = 0;
        this.moveY = 0;
        this.editor.activeLayer.savePositions();
    }

    onGlobalMouseMove(e) {
        if (this.editor.mouseDown && this.startingX != null) {
            this.moveX += (this.editor.mouseX - this.editor.lastMouseX) / this.editor.zoomLevel;
            this.moveY += (this.editor.mouseY - this.editor.lastMouseY) / this.editor.zoomLevel;
            let actualX = this.moveX, actualY = this.moveY;
            if (e.shiftKey) {
                let absX = Math.abs(actualX), absY = Math.abs(actualY);
                if (absX > absY * 2) {
                    actualY = 0;
                }
                else if (absY > absX * 2) {
                    actualX = 0;
                }
                else {
                    let dist = Math.sqrt(actualX * actualX + actualY * actualY);
                    actualX = dist * Math.sign(actualX);
                    actualY = dist * Math.sign(actualY);
                }
            }
            let layer = this.editor.activeLayer;
            layer.offsetX = this.startingX + actualX;
            layer.offsetY = this.startingY + actualY;
            if (e.ctrlKey) {
                layer.offsetX = Math.round(layer.offsetX / 32) * 32;
                layer.offsetY = Math.round(layer.offsetY / 32) * 32;
            }
            this.editor.markOutputChanged();
            this.editor.queueViewRedraw();
            return true;
        }
        return false;
    }

    onGlobalMouseUp(e) {
        this.startingX = null;
        this.startingY = null;
        return false;
    }
}

/**
 * Shared base class for marquee-style selection tools.
 */
class ImageEditorToolMarqueeBase extends ImageEditorTool {
    constructor(editor, id, icon, name, description, hotkey = null) {
        super(editor, id, icon, name, description, hotkey);
        this.copyMode = 'final';
        this.dragging = false;
        this.startX = 0;
        this.startY = 0;
        this.currentX = 0;
        this.currentY = 0;
        let copyDropdown = `<div class="image-editor-tool-block">
            <label>Copy:&nbsp;</label>
            <select class="id-copy-mode" style="width:120px;">
                <option value="final">Final Image</option>
                <option value="layer">Current Layer</option>
            </select>
        </div>`;
        let makeRegionButton = `<div class="image-editor-tool-block">
            <button class="basic-button id-make-region">Make Region</button>
        </div>`;
        this.configDiv.innerHTML = copyDropdown + makeRegionButton;
        this.copyModeSelect = this.configDiv.querySelector('.id-copy-mode');
        this.copyModeSelect.addEventListener('change', () => {
            this.copyMode = this.copyModeSelect.value;
        });
        this.configDiv.querySelector('.id-make-region').addEventListener('click', () => {
            let bounds = this.editor.getSelectionBounds();
            if (bounds) {
                let promptBox = getRequiredElementById('alt_prompt_textbox');
                function roundClean(v) {
                    return Math.round(v * 1000) / 1000;
                }
                let regionText = `\n<region:${roundClean(bounds.x / this.editor.realWidth)},${roundClean(bounds.y / this.editor.realHeight)},${roundClean(bounds.width / this.editor.realWidth)},${roundClean(bounds.height / this.editor.realHeight)}>`;
                promptBox.value += regionText;
                triggerChangeFor(promptBox);
            }
        });
    }

    createMaskCanvas() {
        return document.createElement('canvas');
    }

    buildSelectionMask(maskCtx, minX, minY, width, height) {
        maskCtx.fillRect(minX, minY, width, height);
    }

    drawSelectionPreview(minX, minY, width, height) {
        let [selectX, selectY] = this.editor.imageCoordToCanvasCoord(minX, minY);
        this.editor.drawSelectionBox(selectX, selectY, width * this.editor.zoomLevel, height * this.editor.zoomLevel, 'diff', 8 * this.editor.zoomLevel, 0, 0);
    }

    onMouseDown(e) {
        if (e.button != 0) {
            return;
        }
        let [mouseX, mouseY] = this.editor.canvasCoordToImageCoord(this.editor.mouseX, this.editor.mouseY);
        this.dragging = true;
        this.startX = mouseX;
        this.startY = mouseY;
        this.currentX = mouseX;
        this.currentY = mouseY;
    }

    onMouseMove(e) {
        if (!this.dragging) {
            return;
        }
        let [mouseX, mouseY] = this.editor.canvasCoordToImageCoord(this.editor.mouseX, this.editor.mouseY);
        this.currentX = mouseX;
        this.currentY = mouseY;
    }

    draw() {
        if (!this.dragging) {
            return;
        }
        let minX = Math.round(Math.min(this.startX, this.currentX));
        let minY = Math.round(Math.min(this.startY, this.currentY));
        let width = Math.max(1, Math.round(Math.abs(this.currentX - this.startX)));
        let height = Math.max(1, Math.round(Math.abs(this.currentY - this.startY)));
        this.drawSelectionPreview(minX, minY, width, height);
    }

    onGlobalMouseMove(e) {
        if (this.dragging && this.editor.mouseDown) {
            this.onMouseMove(e);
            return true;
        }
        return false;
    }

    finishSelection() {
        if (!this.dragging) {
            return false;
        }
        this.dragging = false;
        let minX = Math.max(0, Math.min(this.editor.realWidth - 1, Math.round(Math.min(this.startX, this.currentX))));
        let minY = Math.max(0, Math.min(this.editor.realHeight - 1, Math.round(Math.min(this.startY, this.currentY))));
        let maxX = Math.max(minX + 1, Math.min(this.editor.realWidth, Math.round(Math.max(this.startX, this.currentX))));
        let maxY = Math.max(minY + 1, Math.min(this.editor.realHeight, Math.round(Math.max(this.startY, this.currentY))));
        let maskCanvas = document.createElement('canvas');
        maskCanvas.width = this.editor.realWidth;
        maskCanvas.height = this.editor.realHeight;
        let maskCtx = maskCanvas.getContext('2d');
        maskCtx.fillStyle = '#ffffff';
        this.buildSelectionMask(maskCtx, minX, minY, maxX - minX, maxY - minY);
        this.editor.commitSelectionMask(maskCanvas);
        return true;
    }

    onMouseUp(e) {
        this.finishSelection();
    }

    onGlobalMouseUp(e) {
        return this.finishSelection();
    }
}

/**
 * Rectangular marquee selection.
 */
class ImageEditorToolSelect extends ImageEditorToolMarqueeBase {
    constructor(editor) {
        super(editor, 'select', 'select', 'Rect Select', 'Rectangular marquee selection.\nHotKey: S', 's');
    }
}

/**
 * Ellipse marquee selection.
 */
class ImageEditorToolEllipseSelect extends ImageEditorToolMarqueeBase {
    constructor(editor) {
        super(editor, 'ellipse-select', 'ellipse_select', 'Ellipse Select', 'Ellipse marquee selection.');
    }

    buildSelectionMask(maskCtx, minX, minY, width, height) {
        maskCtx.beginPath();
        maskCtx.ellipse(minX + width / 2, minY + height / 2, Math.max(1, width / 2), Math.max(1, height / 2), 0, 0, 2 * Math.PI);
        maskCtx.fill();
    }
}

/**
 * Freehand lasso selection.
 */
class ImageEditorToolLassoSelect extends ImageEditorTool {
    constructor(editor) {
        super(editor, 'lasso-select', 'lasso', 'Lasso Select', 'Freehand lasso selection.');
        this.points = [];
        this.dragging = false;
    }

    onMouseDown(e) {
        if (e.button != 0) {
            return;
        }
        let [mouseX, mouseY] = this.editor.canvasCoordToImageCoord(this.editor.mouseX, this.editor.mouseY);
        this.points = [[mouseX, mouseY]];
        this.dragging = true;
    }

    onMouseMove(e) {
        if (!this.dragging) {
            return;
        }
        let [mouseX, mouseY] = this.editor.canvasCoordToImageCoord(this.editor.mouseX, this.editor.mouseY);
        this.points.push([mouseX, mouseY]);
    }

    onGlobalMouseMove(e) {
        if (this.dragging && this.editor.mouseDown) {
            this.onMouseMove(e);
            return true;
        }
        return false;
    }

    draw() {
        if (!this.dragging || this.points.length < 2) {
            return;
        }
        this.editor.ctx.save();
        this.editor.ctx.strokeStyle = '#ffffff';
        this.editor.ctx.globalCompositeOperation = 'difference';
        this.editor.ctx.lineWidth = 1;
        this.editor.ctx.beginPath();
        let [startX, startY] = this.editor.imageCoordToCanvasCoord(this.points[0][0], this.points[0][1]);
        this.editor.ctx.moveTo(startX, startY);
        for (let i = 1; i < this.points.length; i++) {
            let [x, y] = this.editor.imageCoordToCanvasCoord(this.points[i][0], this.points[i][1]);
            this.editor.ctx.lineTo(x, y);
        }
        this.editor.ctx.stroke();
        this.editor.ctx.restore();
    }

    finishSelection() {
        if (!this.dragging) {
            return false;
        }
        this.dragging = false;
        if (this.points.length < 2) {
            this.points = [];
            return false;
        }
        let maskCanvas = document.createElement('canvas');
        maskCanvas.width = this.editor.realWidth;
        maskCanvas.height = this.editor.realHeight;
        let maskCtx = maskCanvas.getContext('2d');
        maskCtx.fillStyle = '#ffffff';
        maskCtx.beginPath();
        maskCtx.moveTo(this.points[0][0], this.points[0][1]);
        for (let i = 1; i < this.points.length; i++) {
            maskCtx.lineTo(this.points[i][0], this.points[i][1]);
        }
        maskCtx.closePath();
        maskCtx.fill();
        this.editor.commitSelectionMask(maskCanvas);
        this.points = [];
        return true;
    }

    onMouseUp(e) {
        this.finishSelection();
    }

    onGlobalMouseUp(e) {
        return this.finishSelection();
    }
}

/**
 * Polygon lasso selection.
 */
class ImageEditorToolPolygonSelect extends ImageEditorTool {
    constructor(editor) {
        super(editor, 'polygon-select', 'polygon_lasso', 'Polygon Select', 'Click to place polygon points. Right click to finish.');
        this.points = [];
        this.hoverPoint = null;
    }

    onMouseDown(e) {
        if (e.button != 0) {
            return;
        }
        let [mouseX, mouseY] = this.editor.canvasCoordToImageCoord(this.editor.mouseX, this.editor.mouseY);
        if (this.points.length >= 3) {
            let [firstX, firstY] = this.points[0];
            let dx = mouseX - firstX;
            let dy = mouseY - firstY;
            if (dx * dx + dy * dy <= 64) {
                this.finishSelection();
                return;
            }
        }
        this.points.push([mouseX, mouseY]);
        this.hoverPoint = [mouseX, mouseY];
        this.editor.queueOverlayRedraw();
    }

    onMouseMove(e) {
        if (this.points.length == 0) {
            return;
        }
        let [mouseX, mouseY] = this.editor.canvasCoordToImageCoord(this.editor.mouseX, this.editor.mouseY);
        this.hoverPoint = [mouseX, mouseY];
    }

    onContextMenu(e) {
        if (this.points.length >= 3) {
            e.preventDefault();
            this.finishSelection();
            return true;
        }
        return false;
    }

    draw() {
        if (this.points.length == 0) {
            return;
        }
        this.editor.ctx.save();
        this.editor.ctx.strokeStyle = '#ffffff';
        this.editor.ctx.globalCompositeOperation = 'difference';
        this.editor.ctx.lineWidth = 1;
        this.editor.ctx.beginPath();
        let [startX, startY] = this.editor.imageCoordToCanvasCoord(this.points[0][0], this.points[0][1]);
        this.editor.ctx.moveTo(startX, startY);
        for (let i = 1; i < this.points.length; i++) {
            let [x, y] = this.editor.imageCoordToCanvasCoord(this.points[i][0], this.points[i][1]);
            this.editor.ctx.lineTo(x, y);
        }
        if (this.hoverPoint) {
            let [hoverX, hoverY] = this.editor.imageCoordToCanvasCoord(this.hoverPoint[0], this.hoverPoint[1]);
            this.editor.ctx.lineTo(hoverX, hoverY);
        }
        this.editor.ctx.stroke();
        this.editor.ctx.restore();
    }

    finishSelection() {
        if (this.points.length < 3) {
            this.points = [];
            this.hoverPoint = null;
            this.editor.queueOverlayRedraw();
            return false;
        }
        let maskCanvas = document.createElement('canvas');
        maskCanvas.width = this.editor.realWidth;
        maskCanvas.height = this.editor.realHeight;
        let maskCtx = maskCanvas.getContext('2d');
        maskCtx.fillStyle = '#ffffff';
        maskCtx.beginPath();
        maskCtx.moveTo(this.points[0][0], this.points[0][1]);
        for (let i = 1; i < this.points.length; i++) {
            maskCtx.lineTo(this.points[i][0], this.points[i][1]);
        }
        maskCtx.closePath();
        maskCtx.fill();
        this.editor.commitSelectionMask(maskCanvas);
        this.points = [];
        this.hoverPoint = null;
        return true;
    }
}

/**
 * Shared color-threshold selection base.
 */
class ImageEditorToolColorSelectionBase extends ImageEditorTool {
    constructor(editor, id, icon, name, description) {
        super(editor, id, icon, name, description);
    }

    shouldUseContiguousSelection() {
        return this.editor.selectionContiguous;
    }

    sampleTargetColor(imageData, x, y, width) {
        let index = (y * width + x) * 4;
        return [imageData[index], imageData[index + 1], imageData[index + 2], imageData[index + 3]];
    }

    isWithinTolerance(sourceData, x, y, width, targetColor, tolerance) {
        let index = (y * width + x) * 4;
        let diff = Math.abs(sourceData[index] - targetColor[0]) + Math.abs(sourceData[index + 1] - targetColor[1]) + Math.abs(sourceData[index + 2] - targetColor[2]) + Math.abs(sourceData[index + 3] - targetColor[3]);
        return diff <= tolerance * 4;
    }

    buildSelectionMask(targetX, targetY, useContiguous) {
        let sourceCanvas = this.editor.getSelectionSourceCanvas();
        let sourceCtx = sourceCanvas.getContext('2d');
        let imageData = sourceCtx.getImageData(0, 0, sourceCanvas.width, sourceCanvas.height);
        let data = imageData.data;
        let width = sourceCanvas.width;
        let height = sourceCanvas.height;
        if (targetX < 0 || targetY < 0 || targetX >= width || targetY >= height) {
            return null;
        }
        let maskCanvas = document.createElement('canvas');
        maskCanvas.width = width;
        maskCanvas.height = height;
        let maskCtx = maskCanvas.getContext('2d');
        let maskImage = maskCtx.createImageData(width, height);
        let maskData = maskImage.data;
        let targetColor = this.sampleTargetColor(data, targetX, targetY, width);
        let tolerance = Math.max(0, this.editor.selectionTolerance || 0);
        if (useContiguous) {
            let visited = new Uint8Array(width * height);
            let stack = [[targetX, targetY]];
            while (stack.length > 0) {
                let [x, y] = stack.pop();
                if (x < 0 || y < 0 || x >= width || y >= height) {
                    continue;
                }
                let visitIndex = y * width + x;
                if (visited[visitIndex]) {
                    continue;
                }
                visited[visitIndex] = 1;
                if (!this.isWithinTolerance(data, x, y, width, targetColor, tolerance)) {
                    continue;
                }
                let outIndex = visitIndex * 4;
                maskData[outIndex] = 255;
                maskData[outIndex + 1] = 255;
                maskData[outIndex + 2] = 255;
                maskData[outIndex + 3] = 255;
                stack.push([x - 1, y]);
                stack.push([x + 1, y]);
                stack.push([x, y - 1]);
                stack.push([x, y + 1]);
            }
        }
        else {
            for (let y = 0; y < height; y++) {
                for (let x = 0; x < width; x++) {
                    if (!this.isWithinTolerance(data, x, y, width, targetColor, tolerance)) {
                        continue;
                    }
                    let outIndex = (y * width + x) * 4;
                    maskData[outIndex] = 255;
                    maskData[outIndex + 1] = 255;
                    maskData[outIndex + 2] = 255;
                    maskData[outIndex + 3] = 255;
                }
            }
        }
        maskCtx.putImageData(maskImage, 0, 0);
        return maskCanvas;
    }

    onMouseDown(e) {
        if (e.button != 0) {
            return;
        }
        let [mouseX, mouseY] = this.editor.canvasCoordToImageCoord(this.editor.mouseX, this.editor.mouseY);
        let maskCanvas = this.buildSelectionMask(Math.round(mouseX), Math.round(mouseY), this.shouldUseContiguousSelection());
        if (maskCanvas) {
            this.editor.commitSelectionMask(maskCanvas);
        }
    }
}

/**
 * Magic-wand contiguous color selection.
 */
class ImageEditorToolMagicWand extends ImageEditorToolColorSelectionBase {
    constructor(editor) {
        super(editor, 'magic-wand', 'wand', 'Magic Wand', 'Select neighboring pixels by color.');
    }
}

/**
 * Color-select across the whole source by sampled color.
 */
class ImageEditorToolColorSelect extends ImageEditorToolColorSelectionBase {
    constructor(editor) {
        super(editor, 'color-select', 'colorselect', 'Color Select', 'Select all matching pixels by color.');
    }

    shouldUseContiguousSelection() {
        return false;
    }
}

/**
 * Non-destructive crop tool.
 */
class ImageEditorToolCrop extends ImageEditorTool {
    constructor(editor) {
        super(editor, 'crop', 'crop', 'Crop', 'Non-destructive crop for the active image or mask layer.');
        this.cursor = 'crosshair';
        this.dragMode = null;
        this.startX = 0;
        this.startY = 0;
        this.startCrop = null;
    }

    setActive() {
        super.setActive();
        if (this.editor.activeLayer && !this.editor.activeLayer.isAdjustmentLayer()) {
            this.editor.beginCropSession(this.editor.activeLayer);
        }
    }

    setInactive() {
        if (this.editor.cropSession) {
            this.editor.cancelCropSession();
        }
        super.setInactive();
    }

    onLayerChanged(oldLayer, newLayer) {
        super.onLayerChanged(oldLayer, newLayer);
        if (!this.active) {
            return;
        }
        if (!newLayer || newLayer.isAdjustmentLayer()) {
            this.editor.cancelCropSession();
            return;
        }
        this.editor.beginCropSession(newLayer);
    }

    getPreviewLayer() {
        if (this.editor.previewState && this.editor.previewState.targetLayer == this.editor.activeLayer) {
            return this.editor.previewState.previewLayer;
        }
        return this.editor.activeLayer;
    }

    getCropControlCircles() {
        let previewLayer = this.getPreviewLayer();
        if (!previewLayer) {
            return [];
        }
        let [offsetX, offsetY] = previewLayer.getOffset();
        let [canvasX, canvasY] = this.editor.imageCoordToCanvasCoord(offsetX, offsetY);
        let width = previewLayer.width * this.editor.zoomLevel;
        let height = previewLayer.height * this.editor.zoomLevel;
        let radius = 5;
        let circles = [];
        circles.push({ name: 'top-left', radius: radius, x: canvasX - radius / 2, y: canvasY - radius / 2 });
        circles.push({ name: 'top-right', radius: radius, x: canvasX + width + radius / 2, y: canvasY - radius / 2 });
        circles.push({ name: 'bottom-left', radius: radius, x: canvasX - radius / 2, y: canvasY + height + radius / 2 });
        circles.push({ name: 'bottom-right', radius: radius, x: canvasX + width + radius / 2, y: canvasY + height + radius / 2 });
        circles.push({ name: 'top', radius: radius, x: canvasX + width / 2, y: canvasY - radius / 2 });
        circles.push({ name: 'bottom', radius: radius, x: canvasX + width / 2, y: canvasY + height + radius / 2 });
        circles.push({ name: 'left', radius: radius, x: canvasX - radius / 2, y: canvasY + height / 2 });
        circles.push({ name: 'right', radius: radius, x: canvasX + width + radius / 2, y: canvasY + height / 2 });
        let angle = previewLayer.rotation;
        if (angle != 0) {
            let centerX = canvasX + width / 2;
            let centerY = canvasY + height / 2;
            for (let circle of circles) {
                let relativeX = circle.x - centerX;
                let relativeY = circle.y - centerY;
                circle.x = relativeX * Math.cos(angle) - relativeY * Math.sin(angle) + centerX;
                circle.y = relativeX * Math.sin(angle) + relativeY * Math.cos(angle) + centerY;
            }
        }
        return circles;
    }

    getHoveredCropControlCircle() {
        for (let circle of this.getCropControlCircles()) {
            if (this.editor.isMouseInCircle(circle.x, circle.y, circle.radius * 1.5)) {
                return circle;
            }
        }
        return null;
    }

    draw() {
        let previewLayer = this.getPreviewLayer();
        if (!previewLayer) {
            return;
        }
        let [offsetX, offsetY] = previewLayer.getOffset();
        let [canvasX, canvasY] = this.editor.imageCoordToCanvasCoord(offsetX, offsetY);
        this.editor.drawSelectionBox(canvasX, canvasY, previewLayer.width * this.editor.zoomLevel, previewLayer.height * this.editor.zoomLevel, 'diff', 8 * this.editor.zoomLevel, previewLayer.rotation);
        let hoveredCircle = this.getHoveredCropControlCircle();
        if (this.editor.overlayCanvas) {
            this.editor.overlayCanvas.style.cursor = hoveredCircle ? 'grab' : this.cursor;
        }
        for (let circle of this.getCropControlCircles()) {
            this.editor.ctx.strokeStyle = '#ffffff';
            this.editor.ctx.fillStyle = '#000000';
            if (hoveredCircle && hoveredCircle.name == circle.name) {
                this.editor.ctx.strokeStyle = '#000000';
                this.editor.ctx.fillStyle = '#ffffff';
            }
            this.editor.ctx.lineWidth = 1;
            this.editor.ctx.beginPath();
            this.editor.ctx.arc(circle.x, circle.y, circle.radius, 0, 2 * Math.PI);
            this.editor.ctx.fill();
            this.editor.ctx.stroke();
        }
    }

    hitTest() {
        let hoveredCircle = this.getHoveredCropControlCircle();
        if (hoveredCircle) {
            return hoveredCircle.name;
        }
        return 'new';
    }

    clampDraft(session) {
        let layer = this.editor.getCropSessionLayer();
        if (!layer) {
            return;
        }
        session.draftCropX = Math.max(0, Math.min(layer.canvas.width - 1, Math.round(session.draftCropX)));
        session.draftCropY = Math.max(0, Math.min(layer.canvas.height - 1, Math.round(session.draftCropY)));
        session.draftCropWidth = Math.max(1, Math.min(layer.canvas.width - session.draftCropX, Math.round(session.draftCropWidth)));
        session.draftCropHeight = Math.max(1, Math.min(layer.canvas.height - session.draftCropY, Math.round(session.draftCropHeight)));
    }

    onMouseDown(e) {
        if (e.button != 0 || !this.editor.activeLayer || this.editor.activeLayer.isAdjustmentLayer()) {
            return;
        }
        if (!this.editor.cropSession || this.editor.cropSession.layerId != this.editor.activeLayer.id) {
            this.editor.beginCropSession(this.editor.activeLayer);
        }
        let session = this.editor.cropSession;
        let layer = this.editor.activeLayer;
        let [layerX, layerY] = layer.canvasCoordToLayerCoord(this.editor.mouseX, this.editor.mouseY);
        this.dragMode = this.hitTest();
        this.startX = layerX;
        this.startY = layerY;
        this.startCrop = {
            cropX: session.draftCropX,
            cropY: session.draftCropY,
            cropWidth: session.draftCropWidth,
            cropHeight: session.draftCropHeight
        };
        if (this.dragMode == 'new') {
            session.draftCropX = Math.round(layerX);
            session.draftCropY = Math.round(layerY);
            session.draftCropWidth = 1;
            session.draftCropHeight = 1;
            this.clampDraft(session);
            this.startCrop = {
                cropX: session.draftCropX,
                cropY: session.draftCropY,
                cropWidth: 1,
                cropHeight: 1
            };
            this.editor.updateCropSessionPreview();
        }
    }

    onGlobalMouseMove(e) {
        if (!this.dragMode || !this.editor.mouseDown || !this.editor.cropSession) {
            return false;
        }
        let layer = this.editor.getCropSessionLayer();
        if (!layer) {
            return false;
        }
        let session = this.editor.cropSession;
        let [layerX, layerY] = layer.canvasCoordToLayerCoord(this.editor.mouseX, this.editor.mouseY);
        let dx = layerX - this.startX;
        let dy = layerY - this.startY;
        session.draftCropX = this.startCrop.cropX;
        session.draftCropY = this.startCrop.cropY;
        session.draftCropWidth = this.startCrop.cropWidth;
        session.draftCropHeight = this.startCrop.cropHeight;
        if (this.dragMode == 'move') {
            session.draftCropX += dx;
            session.draftCropY += dy;
        }
        else if (this.dragMode == 'left' || this.dragMode == 'top-left' || this.dragMode == 'bottom-left') {
            session.draftCropX += dx;
            session.draftCropWidth -= dx;
        }
        else if (this.dragMode == 'right' || this.dragMode == 'top-right' || this.dragMode == 'bottom-right') {
            session.draftCropWidth += dx;
        }
        if (this.dragMode == 'top' || this.dragMode == 'top-left' || this.dragMode == 'top-right') {
            session.draftCropY += dy;
            session.draftCropHeight -= dy;
        }
        else if (this.dragMode == 'bottom' || this.dragMode == 'bottom-left' || this.dragMode == 'bottom-right') {
            session.draftCropHeight += dy;
        }
        if (this.dragMode == 'new') {
            session.draftCropX = Math.min(this.startCrop.cropX, Math.round(layerX));
            session.draftCropY = Math.min(this.startCrop.cropY, Math.round(layerY));
            session.draftCropWidth = Math.abs(Math.round(layerX) - this.startCrop.cropX);
            session.draftCropHeight = Math.abs(Math.round(layerY) - this.startCrop.cropY);
        }
        this.clampDraft(session);
        this.editor.updateCropSessionPreview();
        return true;
    }

    onMouseUp(e) {
        this.dragMode = null;
    }

    onGlobalMouseUp(e) {
        if (this.dragMode) {
            this.dragMode = null;
            return true;
        }
        return false;
    }
}

/**
 * The Paintbrush tool (also the base used for other brush-likes, such as the Eraser).
 */
class ImageEditorToolBrush extends ImageEditorToolWithColor {
    constructor(editor, id, icon, name, description, isEraser, hotkey = null) {
        super(editor, id, icon, name, description, '#ffffff', hotkey);
        this.cursor = 'none';
        this.radius = 10;
        this.opacity = 1;
        this.brushing = false;
        this.isEraser = isEraser;
        let radiusHtml = `<div class="image-editor-tool-block id-rad-block">
                <label>Radius:&nbsp;</label>
                <input type="number" style="width: 40px;" class="auto-number id-rad1" min="1" max="1024" step="1" value="10">
                <div class="auto-slider-range-wrapper" style="${getRangeStyle(10, 1, 1024)}">
                    <input type="range" style="flex-grow: 2" data-ispot="true" class="auto-slider-range id-rad2" min="1" max="1024" step="1" value="10" oninput="updateRangeStyle(arguments[0])" onchange="updateRangeStyle(arguments[0])">
                </div>
            </div>`;
        let opacityHtml = `<div class="image-editor-tool-block id-opac-block">
                <label>Opacity:&nbsp;</label>
                <input type="number" style="width: 40px;" class="auto-number id-opac1" min="1" max="100" step="1" value="100">
                <div class="auto-slider-range-wrapper" style="${getRangeStyle(100, 1, 100)}">
                    <input type="range" style="flex-grow: 2" class="auto-slider-range id-opac2" min="1" max="100" step="1" value="100" oninput="updateRangeStyle(arguments[0])" onchange="updateRangeStyle(arguments[0])">
                </div>
            </div>`;
        if (isEraser) {
            this.configDiv.innerHTML = radiusHtml + opacityHtml;
        }
        else {
            this.configDiv.innerHTML = this.getColorControlsHTML() + radiusHtml + opacityHtml;
            this.wireColorControls();
        }
        enableSliderForBox(this.configDiv.querySelector('.id-rad-block'));
        enableSliderForBox(this.configDiv.querySelector('.id-opac-block'));
        this.radiusNumber = this.configDiv.querySelector('.id-rad1');
        this.radiusSelector = this.configDiv.querySelector('.id-rad2');
        this.opacityNumber = this.configDiv.querySelector('.id-opac1');
        this.opacitySelector = this.configDiv.querySelector('.id-opac2');
        this.radiusNumber.addEventListener('change', () => { this.onConfigChange(); });
        this.opacityNumber.addEventListener('change', () => { this.onConfigChange(); });
        this.targetLayer = null;
    }

    onConfigChange() {
        if (!this.isEraser) {
            this.color = this.colorText.value;
        }
        this.radius = parseInt(this.radiusNumber.value);
        this.opacity = parseInt(this.opacityNumber.value) / 100;
        this.editor.queueOverlayRedraw();
    }

    draw() {
        this.drawCircleBrush(this.editor.mouseX, this.editor.mouseY, this.radius * this.editor.zoomLevel);
    }

    brush(force = 1) {
        if (!this.targetLayer || !this.bufferLayer || !this.strokeLayer) {
            return;
        }
        let [lastX, lastY] = this.targetLayer.canvasCoordToLayerCoord(this.editor.lastMouseX, this.editor.lastMouseY);
        let [x, y] = this.targetLayer.canvasCoordToLayerCoord(this.editor.mouseX, this.editor.mouseY);
        this.strokeLayer.ctx.clearRect(0, 0, this.strokeLayer.canvas.width, this.strokeLayer.canvas.height);
        let drawColor = this.isEraser ? '#ffffff' : this.color;
        this.strokeLayer.drawFilledCircle(lastX, lastY, this.radius * force, drawColor);
        this.strokeLayer.drawFilledCircleStrokeBetween(lastX, lastY, x, y, this.radius * force, drawColor);
        this.strokeLayer.drawFilledCircle(x, y, this.radius * force, drawColor);
        this.applySelectionClip(this.strokeLayer.ctx, this.targetLayer);
        this.bufferLayer.ctx.save();
        this.bufferLayer.ctx.globalAlpha = this.opacity;
        this.bufferLayer.ctx.globalCompositeOperation = this.isEraser ? 'destination-out' : 'source-over';
        this.bufferLayer.ctx.drawImage(this.strokeLayer.canvas, 0, 0);
        this.bufferLayer.ctx.restore();
        this.bufferLayer.markContentChanged();
    }

    getForceFrom(e) {
        if (e.pointerType && e.pointerType != 'mouse' && typeof e.pressure == 'number' && e.pressure > 0) {
            return e.pressure;
        }
        return 1;
    }

    onMouseDown(e) {
        if (this.brushing) {
            return;
        }
        let target = this.editor.activeLayer;
        if (!target) {
            return;
        }
        this.brushing = true;
        this.targetLayer = target;
        this.bufferLayer = target.cloneLayerData();
        this.strokeLayer = new ImageEditorLayer(this.editor, target.canvas.width, target.canvas.height);
        this.editor.setPreviewState(target, this.bufferLayer, { syncVisualStateFromTarget: true });
        this.brush(this.getForceFrom(e));
    }

    onMouseMove(e) {
        if (this.brushing) {
            this.brush(this.getForceFrom(e));
        }
    }

    onMouseWheel(e) {
        if (e.ctrlKey) {
            e.preventDefault();
            let newRadius = parseInt(this.radius * Math.pow(1.1, -e.deltaY / 100));
            if (newRadius == this.radius) {
                newRadius += e.deltaY > 0 ? -1 : 1;
            }
            this.radiusNumber.value = Math.max(1, Math.min(1024, newRadius));
            this.radiusNumber.dispatchEvent(new Event('input'));
            this.radiusNumber.dispatchEvent(new Event('change'));
        }
    }

    onGlobalMouseUp(e) {
        if (this.brushing) {
            this.targetLayer.saveBeforeEdit();
            this.targetLayer.ctx.clearRect(0, 0, this.targetLayer.canvas.width, this.targetLayer.canvas.height);
            this.targetLayer.ctx.drawImage(this.bufferLayer.canvas, 0, 0);
            this.targetLayer.hasAnyContent = true;
            this.editor.markLayerContentChanged(this.targetLayer);
            this.editor.clearPreviewState();
            this.bufferLayer = null;
            this.strokeLayer = null;
            this.targetLayer = null;
            this.brushing = false;
            return true;
        }
        return false;
    }
}


/**
 * The Paint Bucket tool.
 */
class ImageEditorToolBucket extends ImageEditorToolWithColor {
    constructor(editor) {
        super(editor, 'paintbucket', 'paintbucket', 'Paint Bucket', 'Fill an area with a color.\nHotKey: P', '#ffffff', 'p');
        this.cursor = 'crosshair';
        this.threshold = 10;
        this.opacity = 1;
        let thresholdHtml = `<div class="image-editor-tool-block id-thresh-block">
                <label>Threshold:&nbsp;</label>
                <input type="number" style="width: 40px;" class="auto-number id-thresh1" min="1" max="256" step="1" value="10">
                <div class="auto-slider-range-wrapper" style="${getRangeStyle(10, 1, 256)}">
                    <input type="range" style="flex-grow: 2" data-ispot="true" class="auto-slider-range id-thresh2" min="1" max="256" step="1" value="10" oninput="updateRangeStyle(arguments[0])" onchange="updateRangeStyle(arguments[0])">
                </div>
            </div>`;
        this.configDiv.innerHTML = this.getColorControlsHTML() + thresholdHtml;
        this.wireColorControls();
        enableSliderForBox(this.configDiv.querySelector('.id-thresh-block'));
        this.thresholdNumber = this.configDiv.querySelector('.id-thresh1');
        this.thresholdSelector = this.configDiv.querySelector('.id-thresh2');
        this.thresholdNumber.addEventListener('change', () => { this.onConfigChange(); });
        this.lastTouch = null;
    }

    onConfigChange() {
        this.color = this.colorText.value;
        this.threshold = parseInt(this.thresholdNumber.value);
        this.editor.queueOverlayRedraw();
    }

    doBucket(x, y) {
        let layer = this.editor.activeLayer;
        let [targetX, targetY] = layer.canvasCoordToLayerCoord(x, y);
        targetX = Math.round(targetX);
        targetY = Math.round(targetY);
        if (targetX < 0 || targetY < 0 || targetX >= layer.canvas.width || targetY >= layer.canvas.height) {
            return;
        }
        let selBounds = this.getSelectionBoundsInLayer(layer);
        if (selBounds && (targetX < selBounds.minX || targetY < selBounds.minY || targetX >= selBounds.maxX || targetY >= selBounds.maxY)) {
            return;
        }
        layer.saveBeforeEdit();
        layer.hasAnyContent = true;
        let canvas = layer.canvas;
        let ctx = layer.ctx;
        let refImage = document.createElement('canvas');
        refImage.width = canvas.width;
        refImage.height = canvas.height;
        let refCtx = refImage.getContext('2d');
        let offset = layer.getOffset();
        let relWidth = layer.width / canvas.width;
        let relHeight = layer.height / canvas.height;
        let halfW = layer.width / 2;
        let halfH = layer.height / 2;
        let cosR = Math.cos(-layer.rotation);
        let sinR = Math.sin(-layer.rotation);
        refCtx.setTransform(
            cosR / relWidth, sinR / relHeight,
            -sinR / relWidth, cosR / relHeight,
            (-cosR * halfW + sinR * halfH + halfW) / relWidth,
            (-sinR * halfW - cosR * halfH + halfH) / relHeight
        );
        for (let i = 0; i < this.editor.layers.length; i++) {
            let belowLayer = this.editor.layers[i];
            if (belowLayer.isMask) {
                continue;
            }
            belowLayer.drawToBack(refCtx, -offset[0], -offset[1], 1);
            if (belowLayer == layer) {
                break;
            }
        }
        refCtx.setTransform(1, 0, 0, 1, 0, 0);
        let refData = refCtx.getImageData(0, 0, refImage.width, refImage.height);
        let refRawData = refData.data;
        let imageData = ctx.getImageData(0, 0, canvas.width, canvas.height);
        let [width, height] = [imageData.width, imageData.height];
        let maskData = new Uint8Array(width * height);
        let rawData = imageData.data;
        let threshold = this.threshold;
        let newColor = [parseInt(this.color.substring(1, 3), 16), parseInt(this.color.substring(3, 5), 16), parseInt(this.color.substring(5, 7), 16)];
        let boundsMinX = selBounds ? selBounds.minX : 0;
        let boundsMinY = selBounds ? selBounds.minY : 0;
        let boundsMaxX = selBounds ? Math.min(selBounds.maxX, width) : width;
        let boundsMaxY = selBounds ? Math.min(selBounds.maxY, height) : height;
        let selCX = layer.width / 2;
        let selCY = layer.height / 2;
        let selAngle = layer.rotation;
        let editor = this.editor;
        function layerPixelToImageCoord(x, y) {
            let x2 = x * relWidth;
            let y2 = y * relHeight;
            let [x3, y3] = [x2 - selCX, y2 - selCY];
            let [xr, yr] = [x3 * Math.cos(selAngle) - y3 * Math.sin(selAngle), x3 * Math.sin(selAngle) + y3 * Math.cos(selAngle)];
            let ix = xr + selCX + offset[0];
            let iy = yr + selCY + offset[1];
            return [ix, iy];
        }
        function getPixelIndex(x, y) {
            return (y * width + x) * 4;
        }
        function getColorAt(x, y) {
            let index = getPixelIndex(x, y);
            return [refRawData[index], refRawData[index + 1], refRawData[index + 2], refRawData[index + 3]];
        }
        let startColor = getColorAt(targetX, targetY);
        function isInRange(targetColor) {
            return Math.abs(targetColor[0] - startColor[0]) + Math.abs(targetColor[1] - startColor[1]) + Math.abs(targetColor[2] - startColor[2]) + Math.abs(targetColor[3] - startColor[3]) <= threshold;
        }
        let hits = 0;
        function setPixel(x, y) {
            maskData[y * width + x] = 1;
            let index = getPixelIndex(x, y);
            rawData[index] = newColor[0];
            rawData[index + 1] = newColor[1];
            rawData[index + 2] = newColor[2];
            let imageCoords = layerPixelToImageCoord(x, y);
            let selectionAlpha = editor.sampleSelectionAtImageCoord(imageCoords[0], imageCoords[1], 1);
            rawData[index + 3] = Math.max(1, Math.round(255 * selectionAlpha));
            hits++;
        }
        function canInclude(x, y) {
            if (x < boundsMinX || y < boundsMinY || x >= boundsMaxX || y >= boundsMaxY || maskData[y * width + x] != 0) {
                return false;
            }
            if (editor.hasSelectionMask()) {
                let imgPos = layerPixelToImageCoord(x, y);
                if (editor.sampleSelectionAtImageCoord(imgPos[0], imgPos[1], 0) <= 0) {
                    return false;
                }
            }
            return isInRange(getColorAt(x, y));
        }
        let stack = [[targetX, targetY]];
        while (stack.length > 0) {
            let [x, y] = stack.pop();
            if (!canInclude(x, y)) {
                continue;
            }
            setPixel(x, y);
            if (canInclude(x - 1, y)) { stack.push([x - 1, y]); }
            if (canInclude(x + 1, y)) { stack.push([x + 1, y]); }
            if (canInclude(x, y - 1)) { stack.push([x, y - 1]); }
            if (canInclude(x, y + 1)) { stack.push([x, y + 1]); }
        }
        ctx.putImageData(imageData, 0, 0);
        layer.markContentChanged();
        this.editor.markOutputChanged();
        this.editor.queueSceneRedraw();
    }

    onMouseDown(e) {
        this.doBucket(this.editor.mouseX, this.editor.mouseY);
    }
}

/**
 * The Shape tool.
 */
class ImageEditorToolShape extends ImageEditorToolWithColor {
    constructor(editor) {
        super(editor, 'shape', 'shape', 'Shape', 'Create basic colored shape outlines.\nClick and drag to draw a shape.\nHotKey: X', '#ff0000', 'x');
        this.cursor = 'crosshair';
        this.strokeWidth = 4;
        this.shape = 'rectangle';
        this.isDrawing = false;
        this.startX = 0;
        this.startY = 0;
        this.currentX = 0;
        this.currentY = 0;
        this.startLayerX = 0;
        this.startLayerY = 0;
        this.currentLayerX = 0;
        this.currentLayerY = 0;
        this.bufferLayer = null;
        this.hasDrawn = false;
        this.fill = false;
        let shapeHTML = `
        <div class="image-editor-tool-block tool-block-nogrow">
            <label>Shape:&nbsp;</label>
            <select class="id-shape" style="width:100px;">
                <option value="rectangle">Rectangle</option>
                <option value="circle">Circle</option>
            </select>
        </div>`;
        let fillHTML = `
        <div class="image-editor-tool-block tool-block-nogrow">
            <label><input type="checkbox" class="id-fill"> Fill</label>
        </div>`;
        let strokeHTML = `
        <div class="image-editor-tool-block id-stroke-block">
            <label>Width:&nbsp;</label>
            <input type="number" style="width: 40px;" class="auto-number id-stroke1" min="1" max="20" step="1" value="4">
            <div class="auto-slider-range-wrapper" style="${getRangeStyle(4, 1, 20)}">
                <input type="range" style="flex-grow: 2" class="auto-slider-range id-stroke2" min="1" max="20" step="1" value="4" oninput="updateRangeStyle(arguments[0])" onchange="updateRangeStyle(arguments[0])">
            </div>
        </div>`;
        this.configDiv.innerHTML = this.getColorControlsHTML() + shapeHTML + fillHTML + strokeHTML;
        this.wireColorControls();
        this.shapeSelect = this.configDiv.querySelector('.id-shape');
        this.fillCheckbox = this.configDiv.querySelector('.id-fill');
        this.strokeNumber = this.configDiv.querySelector('.id-stroke1');
        this.strokeSelector = this.configDiv.querySelector('.id-stroke2');
        this.shapeSelect.addEventListener('change', () => {
            this.shape = this.shapeSelect.value;
            this.editor.queueOverlayRedraw();
        });
        this.fillCheckbox.addEventListener('change', () => {
            this.fill = this.fillCheckbox.checked;
            this.updateStrokeDisabled();
            this.editor.queueOverlayRedraw();
        });
        this.strokeBlock = this.configDiv.querySelector('.id-stroke-block');
        enableSliderForBox(this.strokeBlock);
        this.strokeNumber.addEventListener('change', () => { this.onConfigChange(); });
        this.baseCanvas = null;
        this.targetLayer = null;
        this.stampLayer = null;
    }
    
    onConfigChange() {
        this.color = this.colorText.value;
        this.strokeWidth = parseInt(this.strokeNumber.value);
        this.editor.queueOverlayRedraw();
    }

    updateStrokeDisabled() {
        this.strokeBlock.style.opacity = this.fill ? '0.5' : '';
        this.strokeBlock.style.pointerEvents = this.fill ? 'none' : '';
    }

    getEffectiveStrokeWidth() {
        return this.fill ? 1 : this.strokeWidth;
    }

    drawRectangleBorder(ctx, x, y, width, height, thickness) {
        width = Math.max(1, Math.floor(width));
        height = Math.max(1, Math.floor(height));
        thickness = Math.max(1, Math.floor(thickness));
        thickness = Math.min(thickness, width, height);
        ctx.fillRect(x, y, width, thickness);
        ctx.fillRect(x, y + height - thickness, width, thickness);
        let verticalHeight = height - thickness * 2;
        if (verticalHeight > 0) {
            ctx.fillRect(x, y + thickness, thickness, verticalHeight);
            ctx.fillRect(x + width - thickness, y + thickness, thickness, verticalHeight);
        }
    }

    drawShapeToCanvas(ctx, type, x, y, width, height, fill = false) {
        ctx.beginPath();
        if (type == 'rectangle') {
            ctx.rect(Math.round(x), Math.round(y), Math.round(width), Math.round(height));
        }
        else if (type == 'circle') {
            let radius = Math.sqrt(width * width + height * height) / 2;
            ctx.arc(Math.round(x + width / 2), Math.round(y + height / 2), Math.round(radius), 0, 2 * Math.PI);
        }
        if (fill) {
            ctx.fill();
        }
        ctx.stroke();
    }

    draw() {
    }
    
    onMouseDown(e) {
        if (e.button != 0) {
            return;
        }
        if (this.isDrawing) {
            this.finishDrawing();
        }
        this.editor.updateMousePosFrom(e);
        let [mouseX, mouseY] = this.editor.canvasCoordToImageCoord(this.editor.mouseX, this.editor.mouseY);
        mouseX = Math.round(mouseX);
        mouseY = Math.round(mouseY);
        this.isDrawing = true;
        this.startX = mouseX;
        this.startY = mouseY;
        this.currentX = mouseX;
        this.currentY = mouseY;
        this.hasDrawn = false;
        let target = this.editor.activeLayer;
        if (!target) {
            this.bufferLayer = null;
            this.isDrawing = false;
            return;
        }
        let [canvasX, canvasY] = target.editor.imageCoordToCanvasCoord(mouseX, mouseY);
        let [layerX, layerY] = target.canvasCoordToLayerCoord(canvasX, canvasY);
        layerX = Math.round(layerX);
        layerY = Math.round(layerY);
        this.startLayerX = layerX;
        this.startLayerY = layerY;
        this.currentLayerX = layerX;
        this.currentLayerY = layerY;
        this.targetLayer = target;
        this.bufferLayer = target.cloneLayerData();
        this.stampLayer = new ImageEditorLayer(this.editor, target.canvas.width, target.canvas.height);
        this.baseCanvas = document.createElement('canvas');
        this.baseCanvas.width = target.canvas.width;
        this.baseCanvas.height = target.canvas.height;
        this.baseCanvas.getContext('2d').drawImage(target.canvas, 0, 0);
        this.editor.setPreviewState(target, this.bufferLayer, { syncVisualStateFromTarget: true });
    }
    
    finishDrawing() {
        if (this.isDrawing && this.bufferLayer) {
            let parent = this.targetLayer;
            if (!parent) {
                this.bufferLayer = null;
                this.baseCanvas = null;
                this.stampLayer = null;
                this.targetLayer = null;
                this.isDrawing = false;
                this.hasDrawn = false;
                this.editor.clearPreviewState();
                return;
            }
            if (!this.hasDrawn) {
                this.bufferLayer = null;
                this.baseCanvas = null;
                this.stampLayer = null;
                this.targetLayer = null;
                this.isDrawing = false;
                this.hasDrawn = false;
                this.editor.clearPreviewState();
                return;
            }
            this.drawShape();
            parent.saveBeforeEdit();
            parent.ctx.clearRect(0, 0, parent.canvas.width, parent.canvas.height);
            parent.ctx.drawImage(this.bufferLayer.canvas, 0, 0);
            parent.hasAnyContent = true;
            this.bufferLayer = null;
            this.baseCanvas = null;
            this.stampLayer = null;
            this.targetLayer = null;
            this.isDrawing = false;
            this.hasDrawn = false;
            this.editor.markLayerContentChanged(parent);
            this.editor.clearPreviewState();
        }
    }
    
    updateCurrentShapePosition() {
        let [mouseX, mouseY] = this.editor.canvasCoordToImageCoord(this.editor.mouseX, this.editor.mouseY);
        mouseX = Math.round(mouseX);
        mouseY = Math.round(mouseY);
        this.currentX = mouseX;
        this.currentY = mouseY;
        let target = this.editor.activeLayer;
        if (target) {
            let [canvasX, canvasY] = target.editor.imageCoordToCanvasCoord(mouseX, mouseY);
            let [layerX, layerY] = target.canvasCoordToLayerCoord(canvasX, canvasY);
            this.currentLayerX = Math.round(layerX);
            this.currentLayerY = Math.round(layerY);
        }
    }

    onMouseMove(e) {
        if (!this.isDrawing) {
            return;
        }
        this.updateCurrentShapePosition();
        this.drawShape();
    }

    onGlobalMouseMove(e) {
        if (!this.isDrawing) {
            return;
        }
        this.editor.updateMousePosFrom(e);
        this.updateCurrentShapePosition();
        this.drawShape();
    }

    onMouseUp(e) {
        if (e.button != 0 || !this.isDrawing) {
            return;
        }
        this.updateCurrentShapePosition();
        this.finishDrawing();
    }

    onGlobalMouseUp(e) {
        if (e.button != 0 || !this.isDrawing) {
            return;
        }
        this.updateCurrentShapePosition();
        this.finishDrawing();
    }

    drawShape() {
        if (!this.isDrawing || !this.bufferLayer || !this.baseCanvas || !this.stampLayer) {
            return;
        }
        let parent = this.targetLayer;
        if (!parent) {
            return;
        }
        this.bufferLayer.ctx.clearRect(0, 0, this.bufferLayer.canvas.width, this.bufferLayer.canvas.height);
        this.bufferLayer.ctx.drawImage(this.baseCanvas, 0, 0);
        this.stampLayer.ctx.clearRect(0, 0, this.stampLayer.canvas.width, this.stampLayer.canvas.height);
        let startX = Math.round(Math.min(this.startX, this.currentX));
        let startY = Math.round(Math.min(this.startY, this.currentY));
        let endX = Math.round(Math.max(this.startX, this.currentX));
        let endY = Math.round(Math.max(this.startY, this.currentY));
        let width = endX - startX;
        let height = endY - startY;
        if (width == 0 && height == 0) {
            this.bufferLayer.hasAnyContent = false;
            this.hasDrawn = false;
            this.editor.queueOverlayRedraw();
            return;
        }
        this.stampLayer.ctx.save();
        let [offsetX, offsetY] = parent.getOffset();
        this.stampLayer.ctx.imageSmoothingEnabled = false;
        this.stampLayer.ctx.setLineDash([]);
        this.stampLayer.ctx.fillStyle = this.color;
        parent.configureImageToLayerTransform(this.stampLayer.ctx);
        if (this.shape == 'rectangle') {
            if (this.fill) {
                this.stampLayer.ctx.fillRect(startX, startY, width, height);
            }
            else {
                let thickness = Math.max(1, Math.round(this.getEffectiveStrokeWidth()));
                this.drawRectangleBorder(this.stampLayer.ctx, startX, startY, width, height, thickness);
            }
        }
        else {
            this.stampLayer.ctx.strokeStyle = this.color;
            this.stampLayer.ctx.lineWidth = Math.max(1, Math.round(this.getEffectiveStrokeWidth()));
            this.drawShapeToCanvas(this.stampLayer.ctx, this.shape, startX, startY, width, height, this.fill);
        }
        this.stampLayer.ctx.restore();
        this.applySelectionClip(this.stampLayer.ctx, parent);
        this.bufferLayer.ctx.drawImage(this.stampLayer.canvas, 0, 0);
        this.bufferLayer.hasAnyContent = true;
        this.hasDrawn = true;
        this.bufferLayer.markContentChanged();
        this.editor.queueOverlayRedraw();
    }
}

/**
 * The Color Picker tool, a special hidden sub-tool.
 */
class ImageEditorToolPicker extends ImageEditorTempTool {
    constructor(editor, id, icon, name, description, hotkey = null) {
        super(editor, id, icon, name, description, hotkey);
        this.cursor = 'none';
        this.color = '#ffffff';
        this.picking = false;
        this.toolFor = null;
    }

    draw() {
        this.drawCircleBrush(this.editor.mouseX, this.editor.mouseY, 2);
    }

    pickNow() {
        let imageData = this.editor.ctx.getImageData(this.editor.mouseX, this.editor.mouseY, 1, 1).data;
        this.color = `#${imageData[0].toString(16).padStart(2, '0')}${imageData[1].toString(16).padStart(2, '0')}${imageData[2].toString(16).padStart(2, '0')}`;
        if (this.editor.isLayerMaskLike(this.editor.activeLayer)) {
            this.color = colorPickerHelper.hexToGrayscale(this.color);
        }
        this.toolFor.setColor(this.color);
        this.editor.queueOverlayRedraw();
    }

    onMouseDown(e) {
        if (this.picking || !this.toolFor) {
            return;
        }
        this.picking = true;
        this.pickNow();
    }

    onMouseMove(e) {
        if (this.picking) {
            this.pickNow();
        }
    }

    onGlobalMouseUp(e) {
        if (this.picking) {
            this.picking = false;
            this.toolFor.setColor(this.color);
            this.editor.activateTool(this.toolFor.id);
            return true;
        }
        return false;
    }
}

/**
 * Shared base class for SAM2-based mask tools (warmup, clear mask, request tracking).
 */
class ImageEditorToolSam2Base extends ImageEditorTool {
    constructor(editor, id, icon, name, description, hotkey = null) {
        super(editor, id, icon, name, description, hotkey);
        this.cursor = 'crosshair';
        this.requestSerial = 0;
        this.activeRequestId = 0;
        this.maskRequestInFlight = false;
        this.modelWarmed = false;
        this.isWarmingUp = false;
        this.controlsHTML = `
        <div class="image-editor-tool-block tool-block-nogrow">
            <button class="basic-button id-clear-mask">Clear Mask</button>
        </div>`;
        this.warmupHTML = `<div class="image-editor-tool-block tool-block-nogrow" style="opacity:0.8; font-style:italic;">Warming up SAM2 model...</div>`;
        this.showControls();
        this.isMaskOnly = true;
        this.div.style.display = 'none';
    }

    showControls() {
        this.configDiv.innerHTML = this.controlsHTML;
        this.configDiv.querySelector('.id-clear-mask').addEventListener('click', () => {
            this.onClearMask();
        });
    }

    onClearMask() {
        let maskLayer = this.editor.activeLayer;
        if (!maskLayer || !maskLayer.isMask) {
            return;
        }
        maskLayer.clearToEmpty();
        this.editor.queueSceneRedraw();
    }

    setActive() {
        super.setActive();
        if (!this.modelWarmed && !this.isWarmingUp && currentBackendFeatureSet.includes('sam2') && this.editor.getFinalImageData?.()) {
            this.triggerWarmup();
        }
    }

    addWarmupGenData(genData, cx, cy) {
    }

    triggerWarmup() {
        this.isWarmingUp = true;
        this.cursor = 'wait';
        if (this.editor.overlayCanvas) {
            this.editor.overlayCanvas.style.cursor = 'wait';
        }
        this.configDiv.innerHTML = this.warmupHTML;
        try {
            let img = this.editor.getFinalImageData();
            let genData = getGenInput();
            genData['initimage'] = img;
            genData['images'] = 1;
            genData['prompt'] = '';
            delete genData['batchsize'];
            genData['donotsave'] = true;
            let cx = Math.floor((this.editor.realWidth || 64) / 2);
            let cy = Math.floor((this.editor.realHeight || 64) / 2);
            this.addWarmupGenData(genData, cx, cy);
            makeWSRequestT2I('GenerateText2ImageWS', genData, data => {
                if (data.image || data.error) {
                    this.finishWarmup();
                }
            });
        }
        catch (e) {
            this.finishWarmup();
        }
    }

    finishWarmup() {
        this.modelWarmed = true;
        this.isWarmingUp = false;
        this.cursor = 'crosshair';
        if (this.editor.overlayCanvas) {
            this.editor.overlayCanvas.style.cursor = 'crosshair';
        }
        this.showControls();
    }

    /** Returns the image data and coordinate offset for SAM2 requests, cropped to the selection if active. */
    getImageForSam() {
        let bounds = this.editor.getSelectionBounds();
        let width, height;
        if (!bounds) {
            width = Math.round(this.editor.realWidth);
            height = Math.round(this.editor.realHeight);
            return { image: this.editor.getFinalImageData(), offsetX: 0, offsetY: 0, width, height };
        }
        width = Math.round(bounds.width);
        height = Math.round(bounds.height);
        let image = this.editor.getSelectionImageData('image/png');
        return { image: image, offsetX: bounds.x, offsetY: bounds.y, width: width, height: height };
    }

    /** Returns the general mask request inputs for SAM2 requests, cropped to the selection if active. */
    getGeneralMaskRequestInputs() {
        let samInput = this.getImageForSam();
        let genData = getGenInput();
        genData['initimage'] = samInput.image;
        genData['images'] = 1;
        genData['prompt'] = '';
        genData['width'] = samInput.width;
        genData['height'] = samInput.height;
        delete genData['rawresolution'];
        delete genData['sidelength'];
        delete genData['batchsize'];
        genData['donotsave'] = true;
        return [genData, samInput];
    }

    /** Applies a SAM2 mask result image to the active mask layer, handling selection cropping if active. */
    applyMaskResult(maskImg) {
        if (!this.editor.activeLayer || !this.editor.activeLayer.isMask) {
            return;
        }
        let fullMask = document.createElement('canvas');
        fullMask.width = this.editor.realWidth;
        fullMask.height = this.editor.realHeight;
        let fullCtx = fullMask.getContext('2d');
        let bounds = this.editor.getSelectionBounds();
        if (bounds) {
            fullCtx.drawImage(maskImg, 0, 0, maskImg.width || bounds.width, maskImg.height || bounds.height, bounds.x, bounds.y, bounds.width, bounds.height);
        }
        else {
            let imgW = maskImg.width || this.editor.realWidth;
            let imgH = maskImg.height || this.editor.realHeight;
            fullCtx.drawImage(maskImg, 0, 0, imgW, imgH, 0, 0, this.editor.realWidth, this.editor.realHeight);
        }
        this.editor.activeLayer.applyMaskFromImage(fullMask);
        this.clipMaskToSelection();
        this.editor.queueSceneRedraw();
    }

    /** Erases any mask pixels outside the current selection. No-op if no selection is active. */
    clipMaskToSelection() {
        let maskLayer = this.editor.activeLayer;
        if (!maskLayer || !maskLayer.isMask) {
            return;
        }
        if (!this.editor.hasSelectionMask()) {
            return;
        }
        this.editor.applySelectionMaskClip(maskLayer.ctx, maskLayer);
        maskLayer.markContentChanged();
    }
}

/**
 * The SAM2 Point Segmentation tool - click to place positive/negative points and auto-generate a mask.
 */
class ImageEditorToolSam2Points extends ImageEditorToolSam2Base {
    constructor(editor) {
        super(editor, 'sam2points', 'crosshair', 'SAM2 Points', 'Left click to add positive points. Right click to add negative points.\nEach click regenerates the mask.\nRequires SAM2 to be installed.\nHotKey: Y', 'y');
        // TODO: This map is a pretty iffy way to do things, probably stray persistence.
        this.layerPoints = new Map();
        this.pendingMaskUpdate = false;
    }

    getActivePoints() {
        let layer = this.editor.activeLayer;
        if (!layer || !layer.isMask) {
            return { positive: [], negative: [] };
        }
        if (!this.layerPoints.has(layer.id)) {
            this.layerPoints.set(layer.id, { positive: [], negative: [] });
        }
        return this.layerPoints.get(layer.id);
    }

    clearMaskAndEndRequest() {
        let maskLayer = this.editor.activeLayer;
        if (maskLayer && maskLayer.isMask) {
            maskLayer.clearToEmpty();
        }
        this.activeRequestId = ++this.requestSerial;
        this.maskRequestInFlight = false;
        this.pendingMaskUpdate = false;
        this.editor.queueSceneRedraw();
    }

    onClearMask() {
        let maskLayer = this.editor.activeLayer;
        if (!maskLayer || !maskLayer.isMask) {
            return;
        }
        let points = this.getActivePoints();
        points.positive = [];
        points.negative = [];
        this.clearMaskAndEndRequest();
    }

    drawPoint(ctx, x, y, fillColor, showX) {
        let [cx, cy] = this.editor.imageCoordToCanvasCoord(x, y);
        let radius = Math.max(3, Math.round(4 * this.editor.zoomLevel));
        ctx.save();
        ctx.lineWidth = Math.max(1, Math.round(2 * this.editor.zoomLevel));
        ctx.strokeStyle = '#000000';
        ctx.fillStyle = fillColor;
        ctx.beginPath();
        ctx.arc(cx, cy, radius, 0, 2 * Math.PI);
        ctx.fill();
        ctx.stroke();
        if (showX) {
            let cross = Math.max(3, Math.round(radius * 0.9));
            ctx.beginPath();
            ctx.moveTo(cx - cross, cy - cross);
            ctx.lineTo(cx + cross, cy + cross);
            ctx.moveTo(cx - cross, cy + cross);
            ctx.lineTo(cx + cross, cy - cross);
            ctx.stroke();
        }
        ctx.restore();
    }

    draw() {
        let ctx = this.editor.ctx;
        let points = this.getActivePoints();
        for (let point of points.positive) {
            this.drawPoint(ctx, point.x, point.y, '#33ff99', false);
        }
        for (let point of points.negative) {
            this.drawPoint(ctx, point.x, point.y, '#ff3355', true);
        }
    }

    onContextMenu(e) {
        e.preventDefault();
        return true;
    }

    addWarmupGenData(genData, cx, cy) {
        genData['sampositivepoints'] = JSON.stringify([{ x: cx, y: cy }]);
    }

    onMouseDown(e) {
        if (this.isWarmingUp || (e.button != 0 && e.button != 2)) {
            return;
        }
        this.editor.updateMousePosFrom(e);
        let [mouseX, mouseY] = this.editor.canvasCoordToImageCoord(this.editor.mouseX, this.editor.mouseY);
        mouseX = Math.round(mouseX);
        mouseY = Math.round(mouseY);
        if (mouseX < 0 || mouseY < 0 || mouseX >= this.editor.realWidth || mouseY >= this.editor.realHeight) {
            return;
        }
        if (this.editor.hasSelectionMask() && this.editor.sampleSelectionAtImageCoord(mouseX, mouseY, 0) <= 0) {
            return;
        }
        let points = this.getActivePoints();
        let oppositeList = e.button == 2 ? points.positive : points.negative;
        let canvasMouseX = this.editor.mouseX;
        let canvasMouseY = this.editor.mouseY;
        let nearIndex = oppositeList.findIndex(p => {
            let [cx, cy] = this.editor.imageCoordToCanvasCoord(p.x, p.y);
            return (cx - canvasMouseX) ** 2 + (cy - canvasMouseY) ** 2 < 100;
        });
        if (nearIndex >= 0) {
            e.preventDefault();
            oppositeList.splice(nearIndex, 1);
            if (points.positive.length == 0) {
                this.clearMaskAndEndRequest();
            }
            else {
                this.queueMaskUpdate();
            }
            return;
        }
        let point = { x: mouseX, y: mouseY };
        if (e.button == 2) {
            e.preventDefault();
            points.negative.push(point);
        }
        else {
            points.positive.push(point);
        }
        this.queueMaskUpdate();
        this.editor.queueOverlayRedraw();
    }

    queueMaskUpdate() {
        if (!currentBackendFeatureSet.includes('sam2')) {
            $('#sam2_installer').modal('show');
            return;
        }
        if (this.getActivePoints().positive.length == 0) {
            return;
        }
        if (this.maskRequestInFlight) {
            this.pendingMaskUpdate = true;
            return;
        }
        this.requestMaskUpdate();
    }

    finishMaskUpdate(requestId) {
        if (requestId != this.activeRequestId) {
            return;
        }
        this.maskRequestInFlight = false;
        if (this.pendingMaskUpdate) {
            this.pendingMaskUpdate = false;
            this.requestMaskUpdate();
        }
    }

    requestMaskUpdate() {
        this.maskRequestInFlight = true;
        let requestId = ++this.requestSerial;
        this.activeRequestId = requestId;
        let [genData, samInput] = this.getGeneralMaskRequestInputs();
        let points = this.getActivePoints();
        let offX = samInput.offsetX;
        let offY = samInput.offsetY;
        genData['sampositivepoints'] = JSON.stringify(points.positive.map(p => ({ x: p.x - offX, y: p.y - offY })));
        if (points.negative.length > 0) {
            genData['samnegativepoints'] = JSON.stringify(points.negative.map(p => ({ x: p.x - offX, y: p.y - offY })));
        }
        makeWSRequestT2I('GenerateText2ImageWS', genData, data => {
            if (requestId != this.activeRequestId || !data.image) {
                return;
            }
            let newImg = new Image();
            newImg.onload = () => {
                if (requestId != this.activeRequestId) {
                    return;
                }
                if (!this.editor.activeLayer || !this.editor.activeLayer.isMask) {
                    this.finishMaskUpdate(requestId);
                    return;
                }
                this.applyMaskResult(newImg);
                this.finishMaskUpdate(requestId);
            };
            newImg.src = data.image;
        });
    }
}

/**
 * The SAM2 Bounding Box segmentation tool - drag to define a box and auto-generate a mask.
 */
class ImageEditorToolSam2BBox extends ImageEditorToolSam2Base {
    constructor(editor) {
        super(editor, 'sam2bbox', 'bbox', 'SAM2 BBox', 'Click and drag to create a bounding box. Release to generate mask.\nRequires SAM2 to be installed.', null);
        this.bboxStartX = null;
        this.bboxStartY = null;
        this.bboxEndX = null;
        this.bboxEndY = null;
        this.isDrawing = false;
    }

    draw() {
        if (this.isDrawing && this.bboxStartX != null && this.bboxEndX != null) {
            let ctx = this.editor.ctx;
            let [x1, y1] = this.editor.imageCoordToCanvasCoord(this.bboxStartX, this.bboxStartY);
            let [x2, y2] = this.editor.imageCoordToCanvasCoord(this.bboxEndX, this.bboxEndY);
            let minX = Math.min(x1, x2);
            let minY = Math.min(y1, y2);
            let maxX = Math.max(x1, x2);
            let maxY = Math.max(y1, y2);
            ctx.save();
            ctx.strokeStyle = '#33ff99';
            ctx.lineWidth = 2;
            ctx.setLineDash([5, 5]);
            ctx.strokeRect(minX, minY, maxX - minX, maxY - minY);
            ctx.restore();
        }
    }

    addWarmupGenData(genData, cx, cy) {
        genData['sambbox'] = JSON.stringify([cx - 1, cy - 1, cx + 1, cy + 1]);
    }

    onMouseDown(e) {
        if (this.isWarmingUp || e.button != 0) {
            return;
        }
        this.editor.updateMousePosFrom(e);
        let [mouseX, mouseY] = this.editor.canvasCoordToImageCoord(this.editor.mouseX, this.editor.mouseY);
        mouseX = Math.round(mouseX);
        mouseY = Math.round(mouseY);
        this.isDrawing = true;
        this.bboxStartX = mouseX;
        this.bboxStartY = mouseY;
        this.bboxEndX = mouseX;
        this.bboxEndY = mouseY;
    }

    onMouseMove(e) {
        if (!this.isDrawing) {
            return;
        }
        this.editor.updateMousePosFrom(e);
        let [mouseX, mouseY] = this.editor.canvasCoordToImageCoord(this.editor.mouseX, this.editor.mouseY);
        this.bboxEndX = Math.round(mouseX);
        this.bboxEndY = Math.round(mouseY);
        this.editor.queueOverlayRedraw();
    }

    onGlobalMouseUp(e) {
        if (this.isWarmingUp || !this.isDrawing) {
            return;
        }
        this.isDrawing = false;
        this.requestMaskUpdate();
    }

    requestMaskUpdate() {
        if (!currentBackendFeatureSet.includes('sam2')) {
            $('#sam2_installer').modal('show');
            return;
        }
        if (this.bboxStartX == null || this.bboxEndX == null) {
            return;
        }
        this.maskRequestInFlight = true;
        let requestId = ++this.requestSerial;
        this.activeRequestId = requestId;
        let [genData, samInput] = this.getGeneralMaskRequestInputs();
        let minX = Math.max(0, Math.min(this.bboxStartX, this.bboxEndX));
        let minY = Math.max(0, Math.min(this.bboxStartY, this.bboxEndY));
        let maxX = Math.min(this.editor.realWidth - 1, Math.max(this.bboxStartX, this.bboxEndX));
        let maxY = Math.min(this.editor.realHeight - 1, Math.max(this.bboxStartY, this.bboxEndY));
        let selectionBounds = this.editor.getSelectionBounds();
        if (selectionBounds) {
            minX = Math.max(minX, selectionBounds.x);
            minY = Math.max(minY, selectionBounds.y);
            maxX = Math.min(maxX, selectionBounds.x + selectionBounds.width);
            maxY = Math.min(maxY, selectionBounds.y + selectionBounds.height);
        }
        if (maxX <= minX || maxY <= minY) {
            return;
        }
        let offX = samInput.offsetX;
        let offY = samInput.offsetY;
        genData['sambbox'] = JSON.stringify([minX - offX, minY - offY, maxX - offX, maxY - offY]);
        makeWSRequestT2I('GenerateText2ImageWS', genData, data => {
            if (requestId != this.activeRequestId) {
                return;
            }
            if (!data.image) {
                return;
            }
            let newImg = new Image();
            newImg.onload = () => {
                if (requestId != this.activeRequestId) {
                    return;
                }
                this.maskRequestInFlight = false;
                if (!this.editor.activeLayer || !this.editor.activeLayer.isMask) {
                    return;
                }
                this.applyMaskResult(newImg);
            };
            newImg.src = data.image;
        });
    }
}
