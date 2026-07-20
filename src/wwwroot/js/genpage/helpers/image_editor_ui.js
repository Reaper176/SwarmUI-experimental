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
let imageEditingToolRailButtons = {};
let imageEditingSplittersWired = false;
let imageEditingLeftSidebarDrag = false;
let imageEditingRightSidebarDrag = false;
let imageEditingPausedGenerateEditor = false;
let imageEditingTabLifecyclePending = false;
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
let imageEditingToolGroupDefinitions = [
    { id: 'paint', label: 'Paint', toolIds: ['brush', 'eraser', 'paintbucket', 'shape', 'picker'] },
    { id: 'select', label: 'Select', toolIds: ['select', 'ellipse-select', 'lasso-select', 'polygon-select', 'magic-wand', 'color-select'] },
    { id: 'transform', label: 'Transform', toolIds: ['move', 'crop'] },
    { id: 'ai_mask', label: 'AI Mask', toolIds: ['sam3points', 'sam3bbox', 'sam3text'] }
];
let imageEditingPaintToolIds = ['brush', 'eraser', 'paintbucket', 'shape', 'picker'];
let imageEditingSelectionContextToolIds = ['select', 'ellipse-select', 'lasso-select', 'polygon-select', 'magic-wand', 'color-select'];
let imageEditingCropContextToolIds = ['crop'];
let imageEditingTransformContextToolIds = ['move'];
let imageEditingAiMaskContextToolIds = ['sam3points', 'sam3bbox', 'sam3text'];
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
 * Gets the Image Editing grouped tool rail.
 */
function imageEditingGetToolRail() {
    return document.getElementById('imageediting_tool_rail');
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

function imageEditingGetActiveToolOptionsHeader() {
    return document.getElementById('imageediting_active_tool_options_header');
}

function imageEditingGetActiveToolOptionsMount() {
    return document.getElementById('imageediting_active_tool_options_mount');
}

function imageEditingGetActiveToolOptionsEmpty() {
    return document.getElementById('imageediting_active_tool_options_empty');
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
 * Shows or hides an Image Editing input group.
 */
function imageEditingSetInputGroupVisible(element, visible) {
    if (!element || !element.parentElement) {
        return;
    }
    element.parentElement.style.display = visible ? '' : 'none';
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
    for (let [toolId, button] of Object.entries(imageEditingToolRailButtons)) {
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
        button.classList.toggle('imageediting_tool_icon_button_active', imageEditingTabEditor.activeTool && imageEditingTabEditor.activeTool.id == toolId);
    }
    if (imageEditingTabEditor.activeTool && typeof imageEditingTabEditor.activeTool.color == 'string' && imageEditingTabEditor.activeTool.color != imageEditingColor) {
        imageEditingSetColor(imageEditingTabEditor.activeTool.color);
    }
    imageEditingRefreshPenOptions();
    imageEditingRefreshActiveToolOptions();
    imageEditingRefreshContextPanel();
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

function imageEditingRefreshActiveToolOptions() {
    let mount = imageEditingGetActiveToolOptionsMount();
    let empty = imageEditingGetActiveToolOptionsEmpty();
    if (!mount || !empty) {
        return;
    }
    let tool = imageEditingTabEditor ? imageEditingTabEditor.activeTool : null;
    if (!tool || !tool.configDiv || tool.configDiv.children.length <= 0) {
        empty.style.display = '';
        return;
    }
    empty.style.display = 'none';
    if (tool.configDiv.parentElement != mount) {
        mount.appendChild(tool.configDiv);
    }
}

/**
 * Refreshes which control sections appear in the Image Editing context panel.
 */
function imageEditingRefreshContextPanel() {
    if (!imageEditingTabEditor || !imageEditingTabEditor.activeTool) {
        return;
    }
    let toolId = imageEditingTabEditor.activeTool.id;
    let isPaint = imageEditingPaintToolIds.includes(toolId);
    let isSelection = imageEditingSelectionContextToolIds.includes(toolId);
    let isCrop = imageEditingCropContextToolIds.includes(toolId);
    let isTransform = imageEditingTransformContextToolIds.includes(toolId);
    let isAiMask = imageEditingAiMaskContextToolIds.includes(toolId);
    imageEditingSetInputGroupVisible(imageEditingGetToolsHeader(), false);
    imageEditingSetInputGroupVisible(imageEditingGetActiveToolOptionsHeader(), true);
    imageEditingSetInputGroupVisible(imageEditingGetPenOptionsHeader(), isPaint || isAiMask);
    imageEditingSetInputGroupVisible(imageEditingGetActionsHeader(), isTransform || isAiMask);
    imageEditingSetInputGroupVisible(imageEditingGetLayerOptionsHeader(), true);
    imageEditingSetInputGroupVisible(imageEditingGetImageOptionsHeader(), isPaint || isTransform);
    imageEditingSetInputGroupVisible(imageEditingGetSelectionCropHeader(), isSelection || isCrop);
    imageEditingSetInputGroupVisible(imageEditingGetEffectsPresetsHeader(), isPaint || isTransform);
}

/**
 * Builds the grouped icon rail for the Image Editing tab.
 */
function imageEditingBuildToolRail() {
    if (!imageEditingTabEditor) {
        return;
    }
    let rail = imageEditingGetToolRail();
    if (!rail) {
        return;
    }
    rail.innerHTML = '';
    imageEditingToolRailButtons = {};
    for (let group of imageEditingToolGroupDefinitions) {
        let groupDiv = document.createElement('div');
        groupDiv.className = 'imageediting_tool_rail_group';
        let label = document.createElement('div');
        label.className = 'imageediting_tool_rail_group_label translate';
        label.innerText = group.label;
        groupDiv.appendChild(label);
        let buttonGrid = document.createElement('div');
        buttonGrid.className = 'imageediting_tool_rail_grid';
        for (let toolId of group.toolIds) {
            let tool = imageEditingTabEditor.tools[toolId];
            if (!tool || tool.isTempTool) {
                continue;
            }
            let button = document.createElement('button');
            button.className = 'basic-button imageediting_tool_icon_button';
            button.classList.add(`imageediting_tool_icon_button_${tool.id}`);
            button.type = 'button';
            button.style.backgroundImage = `url(imgs/${tool.icon}.png)`;
            button.setAttribute('aria-label', tool.name);
            button.addEventListener('click', () => {
                imageEditingTabEditor.activateTool(tool.id);
                imageEditingRefreshToolButtons();
            });
            buttonGrid.appendChild(button);
            imageEditingToolRailButtons[tool.id] = button;
        }
        groupDiv.appendChild(buttonGrid);
        rail.appendChild(groupDiv);
    }
    imageEditingRefreshToolButtons();
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
        button.setAttribute('aria-label', tool.name);
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
    imageEditingTabEditor.bottomBar.style.display = 'none';
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
    imageEditingBuildToolRail();
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
    imageEditingRefreshContextPanel();
    imageEditingRefreshLayerOpacityControl();
    imageEditingRefreshSelectionControls();
    imageEditingRefreshCropControls();
    imageEditingRefreshEffectControls();
    imageEditingApplyZoom();
    if (imageEditingTabLifecyclePending && imageEditingTopTabButton && imageEditingTopTabButton.classList.contains('active') && imageEditingTabEditor) {
        imageEditingTabLifecyclePending = false;
        imageEditingApplyActiveTabLifecycle();
    }
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
    if (imageEditor.tools['sam3points']) {
        imageEditor.tools['sam3points'].layerPoints = new Map();
    }
    if (imageEditor.tools['sam3bbox']) {
        imageEditor.tools['sam3bbox'].bboxStartX = null;
        imageEditor.tools['sam3bbox'].bboxStartY = null;
        imageEditor.tools['sam3bbox'].bboxEndX = null;
        imageEditor.tools['sam3bbox'].bboxEndY = null;
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

/** Applies the Image Editing tab's active editor lifecycle once its UI exists. */
function imageEditingApplyActiveTabLifecycle() {
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

/** Synchronizes editor lifecycle state after a top-level tab is shown. */
function imageEditingHandleTopTabShown(tabButton) {
    if (tabButton.id == 'imageeditingtabbutton') {
        imageEditingTabLifecyclePending = true;
        imageEditingEnsureUiReady();
    }
    else {
        imageEditingTabLifecyclePending = false;
        if (imageEditingTabEditor && imageEditingTabEditor.active) {
            imageEditingTabEditor.deactivate();
        }
        if (tabButton.id == 'text2imagetabbutton' && imageEditingPausedGenerateEditor) {
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
}

let imageEditingTopTabButton = document.getElementById('imageeditingtabbutton');
$('#toptablist').on('shown.bs.tab', function (e) {
    imageEditingHandleTopTabShown(e.target);
});
if (imageEditingTopTabButton && imageEditingTopTabButton.classList.contains('active')) {
    imageEditingHandleTopTabShown(imageEditingTopTabButton);
}
