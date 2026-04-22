let gen_param_types = null, rawGenParamTypesFromServer = null, rawGroupMapFromServer = null;

let swarmHasLoaded = false;

let lastImageDir = '';

let lastModelDir = '';

let num_waiting_gens = 0, num_models_loading = 0, num_live_gens = 0, num_backends_waiting = 0;

let shouldApplyDefault = false;

let sessionReadyCallbacks = [];

let allModels = [];

let coreModelMap = {};

let otherInfoSpanContent = [];

let isGeneratingForever = false, isGeneratingPreviews = false;

let lastHistoryImage = null, lastHistoryImageDiv = null;

let currentMetadataVal = null, currentImgSrc = null;

let autoCompletionsList = null;
let autoCompletionsOptimize = false;
window.imageEditor = window.imageEditor || null;

let mainGenHandler = new GenerateHandler();

let pageTitleSuffix = document.title.split(' - ').slice(1).join(' - ');
let curAutoTitle = "Page is loading...";

let featureSetChangedCallbacks = [];

function setPageTitle(newTitle) {
    document.title = `${newTitle} - ${pageTitleSuffix}`;
}

function autoTitle() {
    let tabList = getRequiredElementById('toptablist');
    let activeTopTab = tabList.querySelector('.active');
    curAutoTitle = activeTopTab.textContent;
    setPageTitle(curAutoTitle);
}

function updateOtherInfoSpan() {
    let span = getRequiredElementById('other_info_span');
    span.innerHTML = otherInfoSpanContent.join(' ');
}

const time_started = Date.now();

let statusBarElem = getRequiredElementById('top_status_bar');

let generatingPreviewsText = translatable('Generating live previews...');
let waitingOnModelLoadText = translatable('waiting on model load');
let generatingText = translatable('generating');

function currentGenString(num_waiting_gens, num_models_loading, num_live_gens, num_backends_waiting) {
    function autoBlock(num, text) {
        if (num == 0) {
            return '';
        }
        return `<span class="interrupt-line-part">${num} ${text.replaceAll('%', autoS(num))},</span> `;
    }
    return `${autoBlock(num_waiting_gens, 'current generation%')}${autoBlock(num_live_gens, 'running')}${autoBlock(num_backends_waiting, 'queued')}${autoBlock(num_models_loading, waitingOnModelLoadText.get())}`;
}

function updateCurrentStatusDirect(data) {
    if (data) {
        num_waiting_gens = data.waiting_gens;
        num_models_loading = data.loading_models;
        num_live_gens = data.live_gens;
        num_backends_waiting = data.waiting_backends;
    }
    let total = num_waiting_gens + num_models_loading + num_live_gens + num_backends_waiting;
    if (isGeneratingPreviews && num_waiting_gens <= getRequiredElementById('usersettings_maxsimulpreviews').value) {
        total = 0;
    }
    getRequiredElementById('alt_interrupt_button').classList.toggle('interrupt-button-none', total == 0);
    let simpleInterruptButton = document.getElementById('simple_interrupt_button');
    if (simpleInterruptButton) {
        simpleInterruptButton.classList.toggle('interrupt-button-none', total == 0);
    }
    let oldInterruptButton = document.getElementById('interrupt_button');
    if (oldInterruptButton) {
        oldInterruptButton.classList.toggle('interrupt-button-none', total == 0);
    }
    let elem = getRequiredElementById('num_jobs_span');
    let timeEstimate = '';
    let avgGenTime = typeof mainGenHandler.getAverageGenTime == 'function' ? mainGenHandler.getAverageGenTime() : 0;
    if (total > 0 && avgGenTime > 0) {
        let estTime = avgGenTime * total;
        timeEstimate = ` (est. ${durationStringify(estTime)})`;
    }
    elem.innerHTML = total == 0 ? (isGeneratingPreviews ? generatingPreviewsText.get() : '') : `${currentGenString(num_waiting_gens, num_models_loading, num_live_gens, num_backends_waiting)} ${timeEstimate}...`;
    let max = Math.max(num_waiting_gens, num_models_loading, num_live_gens, num_backends_waiting);
    setPageTitle(total == 0 ? curAutoTitle : `(${max} ${generatingText.get()}) ${curAutoTitle}`);
}

let doesHaveGenCountUpdateQueued = false;

function updateGenCount() {
    updateCurrentStatusDirect(null);
    if (doesHaveGenCountUpdateQueued) {
        return;
    }
    doesHaveGenCountUpdateQueued = true;
    setTimeout(() => {
        reviseStatusBar();
    }, 500);
}

let hasAppliedFirstRun = false;
let backendsWereLoadingEver = false;
let reviseStatusInterval = null;
let currentBackendFeatureSet = [];
let rawBackendFeatureSet = [];
let hasLoadedBackendTypesMenu = false;
let lastStatusRequestPending = 0;
let lastStatusHiddenPoll = 0;
function reviseStatusBar() {
    if (lastStatusRequestPending + 20 * 1000 > Date.now()) {
        return;
    }
    if (document.hidden) {
        if (lastStatusHiddenPoll + 30 * 1000 > Date.now()) {
            return;
        }
        lastStatusHiddenPoll = Date.now();
    }
    if (session_id == null) {
        statusBarElem.innerText = 'Loading...';
        statusBarElem.className = `top-status-bar status-bar-warn`;
        return;
    }
    lastStatusRequestPending = Date.now();
    genericRequest('GetCurrentStatus', {}, data => {
        lastStatusRequestPending = 0;
        if (!arraysEqual(data.supported_features, currentBackendFeatureSet)) {
            rawBackendFeatureSet = data.supported_features;
            currentBackendFeatureSet = data.supported_features;
            reviseBackendFeatureSet();
            hideUnsupportableParams();
        }
        doesHaveGenCountUpdateQueued = false;
        updateCurrentStatusDirect(data.status);
        let status;
        if (versionIsWrong) {
            status = { 'class': 'error', 'message': 'The server has updated since you opened the page, please refresh.' };
        }
        else {
            status = data.backend_status;
            if (data.backend_status.any_loading) {
                backendsWereLoadingEver = true;
            }
            else {
                if (!hasAppliedFirstRun) {
                    hasAppliedFirstRun = true;
                    refreshParameterValues(backendsWereLoadingEver || window.alwaysRefreshOnLoad);
                }
            }
            if (reviseStatusInterval != null) {
                if (status.class != '') {
                    clearInterval(reviseStatusInterval);
                    reviseStatusInterval = setInterval(reviseStatusBar, 2 * 1000);
                }
                else {
                    clearInterval(reviseStatusInterval);
                    reviseStatusInterval = setInterval(reviseStatusBar, 60 * 1000);
                }
            }
        }
        statusBarElem.innerText = translate(status.message);
        statusBarElem.className = `top-status-bar status-bar-${status.class}`;
    });
}

document.addEventListener('visibilitychange', () => {
    if (document.hidden) {
        if (typeof stopServerResourceLoop == 'function') {
            stopServerResourceLoop();
        }
        return;
    }
    lastStatusHiddenPoll = 0;
    reviseStatusBar();
    if (typeof refreshServerResourceLoopState == 'function') {
        refreshServerResourceLoopState();
    }
});

/** Array of functions called on key events (eg model selection change) to update displayed features.
 * Return format [array addMe, array removeMe]. For example `[[], ['sd3']]` indicates that the 'sd3' feature flag is not currently supported (eg by current model).
 * Can use 'currentModelHelper.curCompatClass', 'currentModelHelper.curArch' to check the current model architecture. Note these values may be null.
 * */
let featureSetChangers = [];

function reviseBackendFeatureSet() {
    currentBackendFeatureSet = Array.from(currentBackendFeatureSet);
    let addMe = [], removeMe = [];
    function doCompatFeature(compatClass, featureFlag) {
        if (currentModelHelper.curCompatClass && currentModelHelper.curCompatClass.startsWith(compatClass)) {
            addMe.push(featureFlag);
        }
        else {
            removeMe.push(featureFlag);
        }
    }
    function doAnyCompatFeature(compatClasses, featureFlag) {
        for (let compatClass of compatClasses) {
            if (currentModelHelper.curCompatClass && currentModelHelper.curCompatClass.startsWith(compatClass)) {
                addMe.push(featureFlag);
                return;
            }
        }
        removeMe.push(featureFlag);
    }
    function doAnyArchFeature(archIds, featureFlag) {
        for (let archId of archIds) {
            if (currentModelHelper.curArch && currentModelHelper.curArch.startsWith(archId)) {
                addMe.push(featureFlag);
                return;
            }
        }
        removeMe.push(featureFlag);
    }
    doCompatFeature('stable-diffusion-v3', 'sd3');
    doCompatFeature('stable-cascade-v1', 'cascade');
    doAnyArchFeature(['Flux.1-dev', 'flux.2-dev', 'flux.2-klein-4b', 'flux.2-klein-9b', 'hunyuan-video'], 'flux-dev');
    doCompatFeature('stable-diffusion-xl-v1', 'sdxl');
    doAnyCompatFeature(['genmo-mochi-1', 'lightricks-ltx-video', 'hunyuan-video', 'nvidia-cosmos-1', `wan-21`, `wan-22`, 'kandinsky5-vidlite', 'kandinsky5-vidpro'], 'text2video');
    doAnyCompatFeature(['ace-step-1_5'], 'text2audio');
    for (let changer of featureSetChangers) {
        let [add, remove] = changer();
        addMe.push(...add);
        removeMe.push(...remove);
    }
    let anyChanged = false;
    for (let add of addMe) {
        if (!currentBackendFeatureSet.includes(add)) {
            currentBackendFeatureSet.push(add);
            anyChanged = true;
        }
    }
    for (let remove of removeMe) {
        let index = currentBackendFeatureSet.indexOf(remove);
        if (index != -1) {
            currentBackendFeatureSet.splice(index, 1);
            anyChanged = true;
        }
    }
    if (anyChanged) {
        hideUnsupportableParams();
        for (let callback of featureSetChangedCallbacks) {
            callback();
        }
    }
}

let toolSelector = getRequiredElementById('tool_selector');
let toolContainer = getRequiredElementById('tool_container');

function genToolsList() {
    let altGenerateButton = getRequiredElementById('alt_generate_button');
    let oldGenerateButton = document.getElementById('generate_button');
    let altGenerateButtonRawText = altGenerateButton.innerText;
    let altGenerateButtonRawOnClick = altGenerateButton.onclick;
    toolSelector.value = '';
    // TODO: Dynamic-from-server option list generation
    toolSelector.addEventListener('change', () => {
        for (let opened of toolContainer.getElementsByClassName('tool-open')) {
            opened.classList.remove('tool-open');
        }
        altGenerateButton.innerText = altGenerateButtonRawText;
        altGenerateButton.onclick = altGenerateButtonRawOnClick;
        if (oldGenerateButton) {
            oldGenerateButton.innerText = altGenerateButtonRawText;
        }
        let tool = toolSelector.value;
        if (tool == '') {
            getRequiredElementById('clear_selected_tool_button').style.display = 'none';
            return;
        }
        let div = getRequiredElementById(`tool_${tool}`);
        div.classList.add('tool-open');
        let override = toolOverrides[tool];
        if (override) {
            altGenerateButton.innerText = override.text;
            altGenerateButton.onclick = override.run;
            if (oldGenerateButton) {
                oldGenerateButton.innerText = override.text;
            }
        }
        div.dispatchEvent(new Event('tool-opened'));
        getRequiredElementById('clear_selected_tool_button').style.display = '';
    });
}

let toolOverrides = {};

function registerNewTool(id, name, genOverride = null, runOverride = null) {
    let option = document.createElement('option');
    option.value = id;
    option.innerText = name;
    toolSelector.appendChild(option);
    let div = createDiv(`tool_${id}`, 'tool');
    toolContainer.appendChild(div);
    if (genOverride) {
        toolOverrides[id] = { 'text': genOverride, 'run': runOverride };
    }
    return div;
}
function disableSelectedTool() {
    toolSelector.value = '';
    triggerChangeFor(toolSelector);
}

let notePadTool = registerNewTool('note_pad', 'Text Notepad');
notePadTool.appendChild(createDiv(`note_pad_tool_wrapper`, `note_pad_tool_wrapper`, `<span class="translate hoverable-minor-hint-text">This is an open text box where you can type any notes you need to keep track of. They will be temporarily persisted in browser session.</span><br><br><textarea id="note_pad_tool" class="auto-text" style="width:100%;height:100%;" placeholder="Type any notes here..."></textarea>`));
let notePadToolElem = getRequiredElementById('note_pad_tool');
notePadToolElem.value = localStorage.getItem('note_pad_tool') || '';
let notePadToolSaveEvent = null;
notePadToolElem.addEventListener('input', () => {
    if (notePadToolSaveEvent) {
        clearTimeout(notePadToolSaveEvent);
    }
    notePadToolSaveEvent = setTimeout(() => {
        localStorage.setItem('note_pad_tool', notePadToolElem.value);
    }, 1000);
    textBoxSizeAdjust(notePadToolElem);
});
notePadTool.addEventListener('tool-opened', () => {
    textBoxSizeAdjust(notePadToolElem);
});

function tweakNegativePromptBox() {
    let altNegText = getRequiredElementById('alt_negativeprompt_textbox');
    let cfgScale = document.getElementById('input_cfgscale');
    let cfgScaleVal = cfgScale ? parseFloat(cfgScale.value) : 7;
    if (cfgScaleVal == 1) {
        altNegText.classList.add('alt-negativeprompt-textbox-invalid');
        altNegText.placeholder = translate(`Negative Prompt is not available when CFG Scale is 1`);
    }
    else {
        altNegText.classList.remove('alt-negativeprompt-textbox-invalid');
        altNegText.placeholder = translate(`Optionally, type a negative prompt here...`);
    }
    altNegText.title = altNegText.placeholder;
}

function loadUserData(callback) {
    genericRequest('GetMyUserData', {}, data => {
        permissions.updateFrom(data.permissions);
        starredModels = data.starred_models;
        autoCompletionsList = {};
        if (data.autocompletions) {
            let allSet = [];
            autoCompletionsList['all'] = allSet;
            for (let val of data.autocompletions) {
                let split = val.split('\n');
                let datalist = autoCompletionsList[val[0]];
                let entry = { name: split[0], low: split[1].replaceAll(' ', '_').toLowerCase(), clean: split[1], raw: val, count: 0, tag: 0 };
                if (split.length > 2) {
                    entry.tag = split[2];
                }
                if (split.length > 3) {
                    count = parseInt(split[3]) || 0;
                    if (count) {
                        entry.count = count;
                        entry.count_display = largeCountStringify(count);
                    }
                }
                if (split.length > 4) {
                    entry.alts = split[4].split(',').map(x => x.trim().toLowerCase());
                    for (let alt of entry.alts) {
                        if (!autoCompletionsList[alt]) {
                            autoCompletionsList[alt] = [];
                        }
                        autoCompletionsList[alt].push(entry);
                    }
                }
                else {
                    entry.alts = [];
                }
                if (!datalist) {
                    datalist = [];
                    autoCompletionsList[val[0]] = datalist;
                }
                datalist.push(entry);
                allSet.push(entry);
            }
        }
        else {
            autoCompletionsList = null;
        }
        if (!language) {
            language = data.language;
        }
        allPresetsUnsorted = data.presets;
        modelPresetLinkManager.loadFromServer(data.model_preset_links);
        sortPresets();
        presetBrowser.lightRefresh();
        if (shouldApplyDefault) {
            shouldApplyDefault = false;
            let defaultPreset = getPresetByTitle('default');
            if (defaultPreset) {
                applyOnePreset(defaultPreset);
            }
        }
        if (callback) {
            callback();
        }
        loadAndApplyTranslations();
    });
}

function updateAllModels(models) {
    simplifiedMap = {};
    for (let key of Object.keys(models)) {
        simplifiedMap[key] = models[key].map(model => {
            return model[0];
        });
    }
    coreModelMap = simplifiedMap;
    allModels = simplifiedMap['Stable-Diffusion'];
    pickle2safetensor_load();
    modelDownloader.reloadFolders();
}

/** Set some element titles via JavaScript (to allow '\n'). */
function setTitles() {
    getRequiredElementById('alt_prompt_textbox').title = "Tell the AI what you want to see, then press Enter to submit.\nConsider 'a photo of a cat', or 'cartoonish drawing of an astronaut'";
    getRequiredElementById('alt_interrupt_button').title = "Interrupt current generation(s)\nRight-click for advanced options.";
    getRequiredElementById('alt_generate_button').title = "Start generating images\nRight-click for advanced options.";
    let oldGenerateButton = document.getElementById('generate_button');
    if (oldGenerateButton) {
        oldGenerateButton.title = getRequiredElementById('alt_generate_button').title;
        getRequiredElementById('interrupt_button').title = getRequiredElementById('alt_interrupt_button').title;
    }
}
setTitles();

function doFeatureInstaller(name, button_div_id, alt_confirm, callback = null, deleteButton = true) {
    if (!confirm(alt_confirm)) {
        return;
    }
    let buttonDiv = button_div_id ? document.getElementById(button_div_id) : null;
    if (buttonDiv) {
        buttonDiv.querySelector('button').disabled = true;
        buttonDiv.appendChild(createDiv('', null, 'Installing...'));
    }
    genericRequest('ComfyInstallFeatures', {'features': name}, data => {
        if (buttonDiv) {
            buttonDiv.appendChild(createDiv('', null, "Installed! Please wait while backends restart. If it doesn't work, you may need to restart Swarm."));
        }
        reviseStatusBar();
        setTimeout(() => {
            if (deleteButton && buttonDiv) {
                buttonDiv.remove();
            }
            hasAppliedFirstRun = false;
            reviseStatusBar();
            if (callback) {
                callback();
            }
        }, 8000);
    }, 0, (e) => {
        showError(e);
        if (buttonDiv) {
            buttonDiv.appendChild(createDiv('', null, 'Failed to install!'));
            buttonDiv.querySelector('button').disabled = false;
        }
    });
}

function installFeatureById(ids, buttonId = null, modalId = null) {
    let notice = '';
    for (let id of ids.split(',')) {
        let feature = comfy_features[id];
        if (!feature) {
            console.error(`Feature ID ${id} not found in comfy_features, can't install`);
            return;
        }
        notice += feature.notice + '\n';
    }
    doFeatureInstaller(ids, buttonId, notice.trim(), () => {
        if (modalId) {
            $(`#${modalId}`).modal('hide');
        }
    });
}

function installTensorRT() {
    doFeatureInstaller('comfyui_tensorrt', 'install_trt_button', `This will install TensorRT support developed by Comfy and NVIDIA.\nDo you wish to install?`, () => {
        getRequiredElementById('tensorrt_mustinstall').style.display = 'none';
        getRequiredElementById('tensorrt_modal_ready').style.display = '';
    });
}

function clearPromptImages(hideRevision = true) {
    let promptImageArea = getRequiredElementById('alt_prompt_image_area');
    promptImageArea.innerHTML = '';
    let clearButton = getRequiredElementById('alt_prompt_image_clear_button');
    clearButton.style.display = 'none';
    if (hideRevision) {
        hideRevisionInputs(false);
    }
}

function hideRevisionInputs(doClear = true) {
    let revisionGroup = document.getElementById('input_group_imageprompting');
    let revisionToggler = document.getElementById('input_group_content_imageprompting_toggle');
    if (revisionGroup) {
        revisionToggler.checked = false;
        triggerChangeFor(revisionToggler);
        toggleGroupOpen(revisionGroup, false);
        revisionGroup.style.display = 'none';
    }
    genTabLayout.altPromptSizeHandle();
    if (doClear) {
        clearPromptImages(false);
    }
}

function showRevisionInputs(toggleOn = false) {
    let revisionGroup = document.getElementById('input_group_imageprompting');
    let revisionToggler = document.getElementById('input_group_content_imageprompting_toggle');
    if (revisionGroup) {
        toggleGroupOpen(revisionGroup, true);
        if (toggleOn) {
            revisionToggler.checked = true;
            triggerChangeFor(revisionToggler);
        }
        revisionGroup.style.display = '';
    }
}

revisionRevealerSources = [];

function autoRevealRevision() {
    let promptImageArea = getRequiredElementById('alt_prompt_image_area');
    if (promptImageArea.children.length > 0 || revisionRevealerSources.some(x => x())) {
        showRevisionInputs();
    }
    else {
        hideRevisionInputs();
    }
}

let promptImageReplaceTarget = null;

function setPromptImageReplaceTarget(target) {
    if (promptImageReplaceTarget) {
        promptImageReplaceTarget.classList.remove('image-drop-replace-target');
    }
    promptImageReplaceTarget = target;
    if (promptImageReplaceTarget) {
        promptImageReplaceTarget.classList.add('image-drop-replace-target');
    }
}

function getPromptImageDropReplaceTarget(e) {
    if (uiImprover.getFileList(e.dataTransfer, e).length == 0) {
        return null;
    }
    let target = e.target.closest('.alt-prompt-image-container');
    if (!target || !target.querySelector('.alt-prompt-image')) {
        return null;
    }
    return target;
}

function imagePromptAddImage(file) {
    let replaceTarget = promptImageReplaceTarget;
    setPromptImageReplaceTarget(null);
    let existingImage = replaceTarget ? replaceTarget.querySelector('.alt-prompt-image') : null;
    if (replaceTarget && !existingImage) {
        replaceTarget = null;
    }
    let reader = new FileReader();
    reader.onload = (e) => {
        let data = e.target.result;
        if (replaceTarget && !replaceTarget.isConnected) {
            imagePromptAddImage(file);
            return;
        }
        if (existingImage) {
            existingImage.src = data;
            existingImage.height = 128;
            existingImage.dataset.filedata = data;
        }
        else {
            let promptImageArea = getRequiredElementById('alt_prompt_image_area');
            let imageContainer = createDiv(null, 'alt-prompt-image-container');
            let imageRemoveButton = createSpan(null, 'alt-prompt-image-container-remove-button', '&times;');
            imageRemoveButton.addEventListener('click', () => {
                imageContainer.remove();
                autoRevealRevision();
                genTabLayout.altPromptSizeHandle();
            });
            imageRemoveButton.title = 'Remove this image';
            imageContainer.appendChild(imageRemoveButton);
            let imageObject = new Image();
            imageObject.src = data;
            imageObject.height = 128;
            imageObject.className = 'alt-prompt-image';
            imageObject.dataset.filedata = data;
            imageContainer.appendChild(imageObject);
            promptImageArea.appendChild(imageContainer);
        }
        let clearButton = getRequiredElementById('alt_prompt_image_clear_button');
        clearButton.style.display = '';
        showRevisionInputs(true);
        genTabLayout.altPromptSizeHandle();
    };
    reader.readAsDataURL(file);
}

function imagePromptInputHandler() {
    let dragArea = getRequiredElementById('alt_prompt_region');
    dragArea.addEventListener('dragover', (e) => {
        e.preventDefault();
        e.stopPropagation();
    });
    let clearButton = getRequiredElementById('alt_prompt_image_clear_button');
    clearButton.addEventListener('click', () => {
        clearPromptImages();
    });
    dragArea.addEventListener('drop', (e) => {
        if (e.dataTransfer.files && e.dataTransfer.files.length > 0) {
            e.preventDefault();
            e.stopPropagation();
            for (let file of e.dataTransfer.files) {
                if (file.type.startsWith('image/')) {
                    imagePromptAddImage(file);
                }
            }
        }
    });
    let updateReplaceTarget = (e) => {
        setPromptImageReplaceTarget(getPromptImageDropReplaceTarget(e));
    };
    dragArea.addEventListener('dragenter', updateReplaceTarget, true);
    dragArea.addEventListener('dragover', updateReplaceTarget, true);
    dragArea.addEventListener('dragleave', (e) => {
        if (!dragArea.contains(e.relatedTarget)) {
            setPromptImageReplaceTarget(null);
        }
    }, true);
    dragArea.addEventListener('drop', (e) => {
        setPromptImageReplaceTarget(getPromptImageDropReplaceTarget(e));
    }, true);
    document.addEventListener('drop', () => {
        setPromptImageReplaceTarget(null);
    }, true);
    document.addEventListener('dragend', () => {
        setPromptImageReplaceTarget(null);
    }, true);
}
imagePromptInputHandler();

function imagePromptImagePaste(e) {
    let items = (e.clipboardData || e.originalEvent.clipboardData).items;
    for (let item of items) {
        if (item.kind === 'file') {
            let file = item.getAsFile();
            if (file.type.startsWith('image/')) {
                imagePromptAddImage(file);
            }
        }
    }
}

async function openEmptyEditor() {
    try {
        if (!await ensureGenerateImageEditorReady()) {
            showError('Image editor is unavailable.');
            return;
        }
    }
    catch (e) {
        showError(`${e}`);
        return;
    }
    let canvas = document.createElement('canvas');
    canvas.width = document.getElementById('input_width').value;
    canvas.height = document.getElementById('input_height').value;
    let ctx = canvas.getContext('2d');
    ctx.fillStyle = 'white';
    ctx.fillRect(0, 0, canvas.width, canvas.height);
    let image = new Image();
    image.onload = () => {
        imageEditor.clearVars();
        imageEditor.setBaseImage(image);
        imageEditor.activate();
    };
    image.src = canvas.toDataURL();
}

/** Ensures the Generate tab image editor exists, creating it only on first use. */
async function ensureGenerateImageEditorReady() {
    await ensureLazyScriptGroup('imageediting');
    if (typeof ensureImageEditorHelpersInitialized == 'function') {
        ensureImageEditorHelpersInitialized();
    }
    if (window.imageEditor) {
        return true;
    }
    let editorInput = document.getElementById('image_editor_input');
    if (!editorInput) {
        return false;
    }
    window.imageEditor = new ImageEditor(editorInput, true, true, () => genTabLayout.reapplyPositions(), () => needsNewPreview());
    let editorSizebar = getRequiredElementById('image_editor_sizebar');
    window.imageEditor.onActivate = () => {
        editorSizebar.style.display = '';
    };
    window.imageEditor.onDeactivate = () => {
        editorSizebar.style.display = 'none';
    };
    window.imageEditor.tools['options'].optionButtons = [
        ...window.imageEditor.tools['options'].optionButtons,
        { key: 'Store Current Image To History', action: () => {
            let img = window.imageEditor.getFinalImageData();
            storeImageToHistoryWithCurrentParams(img);
        }},
        { key: 'Store Full Canvas To History', action: () => {
            let img = window.imageEditor.getMaximumImageData();
            storeImageToHistoryWithCurrentParams(img);
        }},
        { key: 'Auto Segment Image (SAM2)', action: () => {
            if (!currentBackendFeatureSet.includes('sam2')) {
                $('#sam2_installer').modal('show');
            }
            else {
                let img = window.imageEditor.getFinalImageData();
                let genData = getGenInput();
                genData['controlnetimageinput'] = img;
                genData['controlnetstrength'] = 1;
                genData['controlnetpreprocessor'] = 'Segment Anything 2 Global Autosegment base_plus';
                genData['images'] = 1;
                genData['prompt'] = '';
                delete genData['batchsize'];
                genData['donotsave'] = true;
                genData['controlnetpreviewonly'] = true;
                makeWSRequestT2I('GenerateText2ImageWS', genData, data => {
                    if (!data.image) {
                        return;
                    }
                    let newImg = new Image();
                    newImg.onload = () => {
                        imageEditor.addImageLayer(newImg);
                    };
                    newImg.src = data.image;
                });
            }
        }}
    ];
    return true;
}

/** Starts the server resource loop only when server UI is actually used. */
function ensureServerResourceLoopRunning() {
    if (window.resLoopInterval) {
        return;
    }
    window.resLoopInterval = setInterval(serverResourceLoop, 2000);
}

function stopServerResourceLoop() {
    if (!window.resLoopInterval) {
        return;
    }
    clearInterval(window.resLoopInterval);
    window.resLoopInterval = null;
}

function isServerTopTabActive() {
    let serverTab = document.getElementById(window.genpageLazyTabs.server.tabId);
    return serverTab && serverTab.classList.contains('active');
}

function refreshServerResourceLoopState() {
    if (document.hidden || !isServerTopTabActive()) {
        stopServerResourceLoop();
        return;
    }
    if (!lazyTabState.server || !lazyTabState.server.initDone) {
        stopServerResourceLoop();
        return;
    }
    ensureServerResourceLoopRunning();
}

function debugGenAPIDocs() {
    genericRequest('DebugGenDocs', { }, data => { });
}

let lazyTabInfoById = {};
for (let [tabKey, tabInfo] of Object.entries(window.genpageLazyTabs || {})) {
    lazyTabInfoById[tabInfo.tabId] = tabKey;
}

let lazyScriptLoaders = {};
let lazyScriptGroupState = {};
let latestTopTabOpenRequestId = 0;
let suppressHashUpdateDepth = 0;

let lazyTabState = {
    imageediting: { loaded: false, loading: null, initDone: false, activation: null },
    utilities: { loaded: false, loading: null, initDone: false, activation: null },
    user: { loaded: false, loading: null, initDone: false, activation: null },
    server: { loaded: false, loading: null, initDone: false, activation: null }
};

/** Loads a JavaScript file exactly once and resolves when it is ready. */
function loadScript(src) {
    if (lazyScriptLoaders[src]) {
        return lazyScriptLoaders[src];
    }
    for (let existing of document.getElementsByTagName('script')) {
        if (existing.getAttribute('src') == src) {
            lazyScriptLoaders[src] = Promise.resolve();
            return lazyScriptLoaders[src];
        }
    }
    lazyScriptLoaders[src] = new Promise((resolve, reject) => {
        let script = document.createElement('script');
        script.src = src;
        script.async = false;
        script.onload = () => {
            resolve();
        };
        script.onerror = () => {
            delete lazyScriptLoaders[src];
            reject(`Failed to load script "${src}".`);
        };
        document.body.appendChild(script);
    });
    return lazyScriptLoaders[src];
}

/** Ensures a configured lazy script group is loaded in order exactly once. */
async function ensureLazyScriptGroup(groupKey) {
    let scriptList = window.genpageLazyScriptGroups ? window.genpageLazyScriptGroups[groupKey] : null;
    if (!scriptList || scriptList.length == 0) {
        return;
    }
    if (!lazyScriptGroupState[groupKey]) {
        lazyScriptGroupState[groupKey] = {
            loaded: false,
            loading: null
        };
    }
    let state = lazyScriptGroupState[groupKey];
    if (state.loaded) {
        return;
    }
    if (state.loading) {
        await state.loading;
        return;
    }
    state.loading = (async () => {
        for (let src of scriptList) {
            await loadScript(src);
        }
        state.loaded = true;
    })();
    try {
        await state.loading;
    }
    finally {
        if (!state.loaded) {
            state.loading = null;
        }
    }
}

/** Returns the lazy tab key for a top-tab button, if it is lazy-loaded. */
function getLazyTabKeyForTabId(tabId) {
    if (!tabId) {
        return null;
    }
    return lazyTabInfoById[tabId] || null;
}

/** Returns the lazy tab key for a top-tab button, if it is lazy-loaded. */
function getLazyTabKeyForTabButton(tabButton) {
    if (!tabButton) {
        return null;
    }
    let href = tabButton.getAttribute('href');
    if (!href || !href.startsWith('#')) {
        return null;
    }
    return getLazyTabKeyForTabId(href.substring(1));
}

/** Shows a Bootstrap tab button after lazy content is ready. */
function showTabButton(tabButton) {
    if (!tabButton) {
        return;
    }
    if (window.bootstrap && bootstrap.Tab) {
        bootstrap.Tab.getOrCreateInstance(tabButton).show();
        return;
    }
    tabButton.click();
}

/** Ensures a lazy-loaded tab has its server-rendered HTML injected exactly once. */
async function ensureLazyTabMarkup(tabKey) {
    let state = lazyTabState[tabKey];
    let tabInfo = window.genpageLazyTabs ? window.genpageLazyTabs[tabKey] : null;
    if (!state || !tabInfo) {
        return;
    }
    if (state.loaded) {
        return;
    }
    if (state.loading) {
        await state.loading;
        return;
    }
    state.loading = new Promise((resolve, reject) => {
        genericRequest('GetGenPageTabPartial', { tab: tabKey }, data => {
            let target = document.getElementById(tabInfo.tabId);
            if (!target) {
                state.loading = null;
                reject(`Lazy tab target "${tabInfo.tabId}" was not found.`);
                return;
            }
            target.innerHTML = data.html;
            enableSlidersIn(target);
            if (typeof applyTranslations == 'function') {
                applyTranslations(target);
            }
            state.loaded = true;
            state.loading = null;
            resolve();
        }, 0, e => {
            state.loading = null;
            reject(e);
        });
    });
    await state.loading;
}

function isTopTabOpenRequestStale(expectedRequestId) {
    return expectedRequestId != null && expectedRequestId != latestTopTabOpenRequestId;
}

function beginTopTabOpenRequest(tabButton = null, allowReuse = false) {
    if (allowReuse && tabButton) {
        let existingRequestId = getTopTabOpenRequestId(tabButton);
        if (existingRequestId && existingRequestId == latestTopTabOpenRequestId) {
            return existingRequestId;
        }
    }
    let requestId = ++latestTopTabOpenRequestId;
    if (tabButton) {
        tabButton.dataset.topTabOpenRequestId = `${requestId}`;
    }
    return requestId;
}

function getTopTabOpenRequestId(tabButton) {
    if (!tabButton || !tabButton.dataset.topTabOpenRequestId) {
        return null;
    }
    return parseInt(tabButton.dataset.topTabOpenRequestId);
}

async function runWithHashUpdatesSuppressed(callback) {
    suppressHashUpdateDepth++;
    try {
        return await callback();
    }
    finally {
        suppressHashUpdateDepth--;
    }
}

function findSubTabButton(topTabButton, subTabSpecifier) {
    if (!subTabSpecifier) {
        return null;
    }
    let subTabButton = document.getElementById(subTabSpecifier);
    if (subTabButton) {
        return subTabButton;
    }
    let topTabHref = topTabButton ? topTabButton.getAttribute('href') : null;
    if (!topTabHref || !topTabHref.startsWith('#')) {
        return null;
    }
    let subMapping = hashSubTabMapping[topTabHref.substring(1)];
    let subTabList = subMapping ? document.getElementById(subMapping) : null;
    if (!subTabList) {
        return null;
    }
    return subTabList.querySelector(`a[href='#${subTabSpecifier}']`);
}

/** Ensures a top tab and optional sub-tab are shown, including lazy setup when needed. */
async function openGenPageTabAsync(topTabButtonId, subTabSpecifier = null, expectedRequestId = null, historyMode = 'push') {
    let topTabButton = getRequiredElementById(topTabButtonId);
    if (expectedRequestId == null && topTabButton.classList.contains('active')) {
        expectedRequestId = getTopTabOpenRequestId(topTabButton);
    }
    expectedRequestId = expectedRequestId || beginTopTabOpenRequest(topTabButton);
    if (isTopTabOpenRequestStale(expectedRequestId)) {
        return false;
    }
    return await runWithHashUpdatesSuppressed(async () => {
        await showTabButtonAndWait(topTabButton);
        if (isTopTabOpenRequestStale(expectedRequestId)) {
            return false;
        }
        let lazyTabKey = getLazyTabKeyForTabButton(topTabButton);
        if (lazyTabKey) {
            if (!await activateLazyTab(lazyTabKey, expectedRequestId)) {
                return false;
            }
        }
        if (isTopTabOpenRequestStale(expectedRequestId)) {
            return false;
        }
        let subTabButton = findSubTabButton(topTabButton, subTabSpecifier);
        if (subTabButton) {
            await showTabButtonAndWait(subTabButton);
            if (isTopTabOpenRequestStale(expectedRequestId)) {
                return false;
            }
        }
        updateHash(historyMode);
        return true;
    });
}

/** Starts an async tab open flow from inline handlers without exposing promise errors. */
function openGenPageTab(topTabButtonId, subTabButtonId = null) {
    openGenPageTabAsync(topTabButtonId, subTabButtonId).catch((e) => {
        showError(`${e}`);
    });
    return false;
}

window.openGenPageTab = openGenPageTab;
window.openGenPageTabAsync = openGenPageTabAsync;

let lazyTabHooks = {
    imageediting: async () => {
        if (typeof ensureImageEditorHelpersInitialized == 'function') {
            ensureImageEditorHelpersInitialized();
        }
        if (typeof imageEditingEnsureUiReady == 'function') {
            imageEditingEnsureUiReady();
        }
    },
    utilities: async () => {
        if (typeof ensureUtilitiesTabInitialized == 'function') {
            ensureUtilitiesTabInitialized();
        }
        if (typeof refreshUtilitiesTab == 'function') {
            refreshUtilitiesTab();
        }
        else if (typeof initUtilitiesTab == 'function') {
            initUtilitiesTab();
        }
    },
    user: async () => {
        if (typeof notifySettingsEditorTabReady == 'function') {
            notifySettingsEditorTabReady('user');
        }
        if (typeof ensureUserTabInitialized == 'function') {
            ensureUserTabInitialized();
        }
        if (typeof refreshUserTab == 'function') {
            refreshUserTab();
        }
        else if (typeof initUserTab == 'function') {
            initUserTab();
        }
    },
    server: async () => {
        if (typeof notifySettingsEditorTabReady == 'function') {
            notifySettingsEditorTabReady('server');
        }
        if (typeof initServerTab == 'function') {
            initServerTab();
        }
        else if (typeof ensureServerTabInitialized == 'function') {
            ensureServerTabInitialized();
        }
        if (typeof ensureServerLogsTabInitialized == 'function' && typeof initServerTab != 'function') {
            ensureServerLogsTabInitialized();
        }
        else if (typeof initServerLogsTab == 'function') {
            initServerLogsTab();
        }
        if (!hasLoadedBackendTypesMenu && permissions.hasPermission('view_backends_list')) {
            hasLoadedBackendTypesMenu = true;
            if (typeof loadBackendTypesMenuOnce == 'function') {
                loadBackendTypesMenuOnce();
            }
            else if (typeof loadBackendTypesMenu == 'function') {
                loadBackendTypesMenu();
            }
        }
    }
};

sessionReadyCallbacks.push(() => {
    let activeTopTab = getRequiredElementById('toptablist').querySelector('.active');
    let activeLazyTabKey = getLazyTabKeyForTabButton(activeTopTab);
    if (activeLazyTabKey == 'server') {
        activateLazyTab(activeLazyTabKey).catch((e) => {
            showError(`${e}`);
        });
    }
});

/** Runs first-open lazy tab work for a lazy top tab exactly once. */
async function activateLazyTab(tabKey, expectedRequestId = null) {
    let state = lazyTabState[tabKey];
    if (!state) {
        return false;
    }
    if (isTopTabOpenRequestStale(expectedRequestId)) {
        return false;
    }
    if (!state.activation) {
        state.activation = (async () => {
            await ensureLazyScriptGroup(tabKey);
            await ensureLazyTabMarkup(tabKey);
            if (!state.initDone && lazyTabHooks[tabKey]) {
                await lazyTabHooks[tabKey]();
                state.initDone = true;
            }
        })();
        state.activation.catch(() => {
            state.activation = null;
        });
    }
    await state.activation;
    if (isTopTabOpenRequestStale(expectedRequestId)) {
        return false;
    }
    let tabInfo = window.genpageLazyTabs ? window.genpageLazyTabs[tabKey] : null;
    bindHashTrackingForTabListId(tabInfo ? hashSubTabMapping[tabInfo.tabId] : null);
    let tabBody = tabInfo ? document.getElementById(tabInfo.tabId) : null;
    if (tabBody && tabBody.classList.contains('active') && suppressHashUpdateDepth == 0) {
        updateHash();
    }
    return true;
}

function bindHashTrackingForTabListId(tabListId) {
    if (!tabListId) {
        return;
    }
    let tabList = document.getElementById(tabListId);
    if (!tabList) {
        return;
    }
    if (tabList.dataset.hashTracked == 'true') {
        return;
    }
    tabList.dataset.hashTracked = 'true';
    tabList.addEventListener('shown.bs.tab', (e) => {
        if (suppressHashUpdateDepth > 0) {
            return;
        }
        if (tabList.id == 'toptablist') {
            let lazyTabKey = getLazyTabKeyForTabButton(e.target);
            if (lazyTabKey) {
                let state = lazyTabState[lazyTabKey];
                if (state && (!state.loaded || !state.initDone)) {
                    return;
                }
            }
        }
        updateHash();
    });
}

async function showTabButtonAndWait(tabButton) {
    if (!tabButton || tabButton.classList.contains('active')) {
        return;
    }
    await new Promise((resolve) => {
        let onShown = (e) => {
            if (e.target != tabButton) {
                return;
            }
            resolve();
        };
        tabButton.addEventListener('shown.bs.tab', onShown, { once: true });
        showTabButton(tabButton);
    });
}

let hashSubTabMapping = {
    [window.genpageLazyTabs.utilities.tabId]: 'utilitiestablist',
    [window.genpageLazyTabs.user.tabId]: 'usertablist',
    [window.genpageLazyTabs.server.tabId]: 'servertablist',
};

function updateHash(historyMode = 'push') {
    let tabList = getRequiredElementById('toptablist');
    let bottomTabList = getRequiredElementById('bottombartabcollection');
    let activeTopTab = tabList.querySelector('.active');
    let activeBottomTab = bottomTabList.querySelector('.active');
    let activeBottomTabHref = activeBottomTab ? activeBottomTab.href.split('#')[1] : '';
    let activeTopTabHref = activeTopTab ? activeTopTab.href.split('#')[1] : '';
    let hash = `#${activeBottomTabHref},${activeTopTabHref}`;
    let subMapping = hashSubTabMapping[activeTopTabHref];
    if (subMapping) {
        let subTabList = document.getElementById(subMapping);
        let activeSubTab = subTabList ? subTabList.querySelector('.active') : null;
        if (activeSubTab) {
            hash += `,${activeSubTab.href.split('#')[1]}`;
        }
    }
    else if (activeTopTabHref == 'Simple') {
        let target = simpleTab.browser.selected || simpleTab.browser.folder;
        if (target) {
            hash += `,${encodeURIComponent(target)}`;
        }
    }
    if (location.hash != hash) {
        if (historyMode == 'replace') {
            history.replaceState(null, null, hash);
        }
        else {
            history.pushState(null, null, hash);
        }
    }
    autoTitle();
}

async function loadHashHelper() {
    let tabList = getRequiredElementById('toptablist');
    let bottomTabList = getRequiredElementById('bottombartabcollection');
    bindHashTrackingForTabListId('toptablist');
    bindHashTrackingForTabListId('bottombartabcollection');
    for (let subMapping of Object.values(hashSubTabMapping)) {
        bindHashTrackingForTabListId(subMapping);
    }
    if (location.hash) {
        let split = location.hash.substring(1).split(',');
        let bottomTarget = bottomTabList.querySelector(`a[href='#${split[0]}']`);
        try {
            if (split[1] == 'Simple' && split.length > 2) {
                let target = decodeURIComponent(split[2]);
                simpleTab.mustSelectTarget = target;
            }
            let requestId = beginTopTabOpenRequest();
            await runWithHashUpdatesSuppressed(async () => {
                if (bottomTarget && bottomTarget.style.display != 'none') {
                    await showTabButtonAndWait(bottomTarget);
                }
                let target = tabList.querySelector(`a[href='#${split[1]}']`);
                if (target) {
                    target.dataset.topTabOpenRequestId = `${requestId}`;
                    await openGenPageTabAsync(target.id, split.length > 2 ? split[2] : null, requestId, 'replace');
                }
            });
        }
        catch (e) {
            showError(`${e}`);
        }
        autoTitle();
    }
}

function clearParamFilterInput() {
    let filter = getRequiredElementById('main_inputs_filter');
    let filterClearer = getRequiredElementById('clear_input_icon');
    if (filter.value.length > 0) {
        filter.value = '';
        filter.focus();
        hideUnsupportableParams();
    }
    filterClearer.style.display = 'none';
}

function genpageLoad() {
    $('#toptablist').on('show.bs.tab', function (e) {
        beginTopTabOpenRequest(e.target, true);
    });
    $('#toptablist').on('shown.bs.tab', function (e) {
        let versionDisp = getRequiredElementById('version_display');
        if (e.target.id == 'maintab_comfyworkflow') {
            versionDisp.style.display = 'none';
        }
        else {
            versionDisp.style.display = '';
        }
        let shownLazyTabKey = getLazyTabKeyForTabButton(e.target);
        if (shownLazyTabKey) {
            let wasLazyTabInitialized = lazyTabState[shownLazyTabKey] ? lazyTabState[shownLazyTabKey].initDone : false;
            let requestId = getTopTabOpenRequestId(e.target);
            activateLazyTab(shownLazyTabKey, requestId).then((wasActivated) => {
                if (!wasActivated) {
                    return;
                }
                if (shownLazyTabKey == 'user' && wasLazyTabInitialized && typeof refreshUserTab == 'function') {
                    refreshUserTab();
                }
                if (shownLazyTabKey == 'server' && typeof queueServerTabHeightFix == 'function') {
                    queueServerTabHeightFix();
                }
                refreshServerResourceLoopState();
            }).catch((e) => {
                showError(`${e}`);
            });
            return;
        }
        refreshServerResourceLoopState();
    });
    genTabLayout.init();
    reviseStatusBar();
    loadHashHelper().catch((e) => {
        showError(`${e}`);
    });
    getSession(() => {
        ensureImageHistoryBrowserShellReady();
        imageHistoryBrowser.navigate('');
        genericRequest('ListT2IParams', {}, data => {
            modelsHelpers.loadClassesFromServer(data.models, data.model_compat_classes, data.model_classes);
            updateAllModels(data.models);
            wildcardHelpers.newWildcardList(data.wildcards);
            [rawGenParamTypesFromServer, rawGroupMapFromServer] = buildParameterList(data.list, data.groups);
            gen_param_types = rawGenParamTypesFromServer;
            paramConfig.preInit();
            paramConfig.applyParamEdits(data.param_edits);
            paramConfig.loadUserParamConfigTab();
            autoRepersistParams();
            if (window.autoRepersistParamsInterval) {
                clearInterval(window.autoRepersistParamsInterval);
            }
            window.autoRepersistParamsInterval = setInterval(autoRepersistParams, 60 * 60 * 1000); // Re-persist again hourly if UI left over
            genInputs();
            genToolsList();
            reviseStatusBar();
            getRequiredElementById('advanced_options_checkbox').checked = localStorage.getItem('display_advanced') == 'true';
            toggle_advanced();
            currentModelHelper.ensureCurrentModel();
            loadUserData(() => {
                selectInitialPresetList();
            });
            for (let callback of sessionReadyCallbacks) {
                callback();
            }
            automaticWelcomeMessage();
            autoTitle();
            swarmHasLoaded = true;
        });
        if (reviseStatusInterval) {
            clearInterval(reviseStatusInterval);
        }
        reviseStatusInterval = setInterval(reviseStatusBar, 2000);
        if (window.resLoopInterval) {
            clearInterval(window.resLoopInterval);
            window.resLoopInterval = null;
        }
    });
}
