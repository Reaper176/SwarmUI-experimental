let imageHistorySelected = new Set();
let imageHistoryBulkActionRunning = false;
let imageHistoryCompareFiles = null;
let imageHistoryShowHidden = localStorage.getItem('image_history_show_hidden') == 'true';
let imageHistoryRefreshQueued = false;
let imageHistoryHasLoadedOnce = false;
let imageHistoryInitialAutoRetryUsed = false;
let imageHistoryAutoRetryTimer = null;
let imageHistoryNextLoadIsRetry = false;
let imageHistoryStartupStage = 'pending';
let imageHistoryLoadToken = 0;
let imageHistoryBackgroundLoadToken = 0;
let imageHistoryBackgroundRetryCount = 0;
let imageHistoryBackgroundRequestKey = null;
let imageHistoryBackgroundWatchdog = null;
const IMAGE_HISTORY_METADATA_CACHE_LIMIT = 1024;
const IMAGE_HISTORY_AUTO_RETRY_DELAY_MS = 1500;
const IMAGE_HISTORY_FAST_FIRST_LIMIT = 128;
const IMAGE_HISTORY_BACKGROUND_WATCHDOG_DELAY_MS = 10000;
const IMAGE_HISTORY_BACKGROUND_MAX_RETRIES = 2;
let IMAGE_HISTORY_UNLOAD_ROW_BUFFER = 10;
let IMAGE_HISTORY_MIN_MEDIA_ROWS_TO_UNLOAD = 2;
const imageHistoryMetadataCache = new Map();
let registeredMediaButtons = [];

class ImageHistoryWindowManager {
    constructor() {
        this.content = null;
        this.updateQueued = false;
        this.boundScroll = this.queueUpdate.bind(this);
        this.boundResize = this.queueUpdate.bind(this);
    }

    attach(content) {
        if (this.content == content) {
            this.queueUpdate();
            return;
        }
        if (this.content) {
            this.content.removeEventListener('scroll', this.boundScroll);
        }
        this.content = content;
        if (this.content) {
            this.content.addEventListener('scroll', this.boundScroll);
        }
        window.removeEventListener('resize', this.boundResize);
        window.addEventListener('resize', this.boundResize);
        this.queueUpdate();
    }

    getEntries() {
        if (!this.content) {
            return [];
        }
        return Array.from(this.content.children).filter(entry => entry?.dataset?.name);
    }

    queueUpdate() {
        if (!this.content || this.updateQueued) {
            return;
        }
        this.updateQueued = true;
        let run = () => {
            this.updateQueued = false;
            this.updateVisibleWindow();
        };
        if (window.requestAnimationFrame) {
            requestAnimationFrame(run);
        }
        else {
            setTimeout(run, 16);
        }
    }

    hydrateEntry(entry) {
        let img = entry.querySelector('img.image-block-img-inner');
        if (!img || !img.dataset.origSrc) {
            return false;
        }
        if (img.getAttribute('src')) {
            return false;
        }
        img.dataset.src = img.dataset.origSrc;
        img.classList.add('lazyload');
        return true;
    }

    dehydrateEntry(entry) {
        let img = entry.querySelector('img.image-block-img-inner');
        if (!img || !img.dataset.origSrc) {
            return;
        }
        if (!img.getAttribute('src') && img.dataset.src == img.dataset.origSrc) {
            return;
        }
        img.classList.add('lazyload');
        img.dataset.src = img.dataset.origSrc;
        img.removeAttribute('src');
    }

    buildRows(entries) {
        let rows = [];
        for (let entry of entries) {
            let top = entry.offsetTop;
            let bottom = top + entry.offsetHeight;
            let lastRow = rows.length > 0 ? rows[rows.length - 1] : null;
            if (!lastRow || Math.abs(lastRow.top - top) > 4) {
                rows.push({ top, bottom, entries: [entry] });
                continue;
            }
            lastRow.entries.push(entry);
            lastRow.bottom = Math.max(lastRow.bottom, bottom);
        }
        return rows;
    }

    updateVisibleWindow() {
        if (!this.content || !this.content.isConnected) {
            return;
        }
        let entries = this.getEntries();
        if (entries.length == 0) {
            return;
        }
        let rows = this.buildRows(entries);
        if (rows.length == 0) {
            return;
        }
        let scrollTop = this.content.scrollTop;
        let scrollBottom = scrollTop + this.content.clientHeight;
        let visibleStart = 0;
        let visibleEnd = rows.length - 1;
        for (let i = 0; i < rows.length; i++) {
            if (rows[i].bottom >= scrollTop) {
                visibleStart = i;
                break;
            }
        }
        for (let i = rows.length - 1; i >= 0; i--) {
            if (rows[i].top <= scrollBottom) {
                visibleEnd = i;
                break;
            }
        }
        let keepStart = Math.max(0, visibleStart - IMAGE_HISTORY_UNLOAD_ROW_BUFFER);
        let keepEnd = Math.min(rows.length - 1, visibleEnd + IMAGE_HISTORY_UNLOAD_ROW_BUFFER);
        let hydrateQueued = false;
        for (let i = 0; i < rows.length; i++) {
            let row = rows[i];
            if (i >= keepStart && i <= keepEnd) {
                for (let entry of row.entries) {
                    hydrateQueued = this.hydrateEntry(entry) || hydrateQueued;
                }
            }
            else if (i < keepStart - IMAGE_HISTORY_MIN_MEDIA_ROWS_TO_UNLOAD || i > keepEnd + IMAGE_HISTORY_MIN_MEDIA_ROWS_TO_UNLOAD) {
                for (let entry of row.entries) {
                    this.dehydrateEntry(entry);
                }
            }
        }
        if (hydrateQueued) {
            browserUtil.queueMakeVisible(this.content);
        }
    }
}

let imageHistoryWindowManager = new ImageHistoryWindowManager();

/** Registers a media button for extensions. 'mediaTypes' filters by type eg ['audio'], null means all. 'isDefault' promotes to visible (vs More dropdown). 'showInHistory' controls whether button appears in the History panel. */
function registerMediaButton(name, action, title = '', mediaTypes = null, isDefault = false, showInHistory = true, href = null, is_download = false, can_multi = false, multi_only = false) {
    registeredMediaButtons.push({ name, action, title, mediaTypes, isDefault, showInHistory, href, is_download, can_multi, multi_only });
}

function getHistoryImageSrc(fullSrc) {
    let safePath = fullSrc.split('/').map(part => encodeURIComponent(part)).join('/');
    return `${getImageOutPrefix()}/${safePath}`;
}

function requestImageHistoryRefresh() {
    if (!imageHistoryBrowser || imageHistoryRefreshQueued) {
        return;
    }
    imageHistoryRefreshQueued = true;
    let run = () => {
        imageHistoryRefreshQueued = false;
        if (imageHistoryBrowser) {
            imageHistoryBrowser.lightRefresh();
        }
    };
    if (window.requestAnimationFrame) {
        requestAnimationFrame(run);
    }
    else {
        setTimeout(run, 0);
    }
}

function clearImageHistoryAutoRetry() {
    if (imageHistoryAutoRetryTimer) {
        clearTimeout(imageHistoryAutoRetryTimer);
        imageHistoryAutoRetryTimer = null;
    }
    imageHistoryNextLoadIsRetry = false;
}

function clearImageHistoryBackgroundWatchdog() {
    if (imageHistoryBackgroundWatchdog) {
        clearTimeout(imageHistoryBackgroundWatchdog);
        imageHistoryBackgroundWatchdog = null;
    }
}

function getImageHistoryRequestKey(path, depth, sortBy, reverse, showHidden) {
    return JSON.stringify({ path, depth, sortBy, reverse, showHidden });
}

function isImageHistoryBackgroundRequestStillRelevant(path, requestKey, backgroundToken) {
    if (!imageHistoryBrowser || imageHistoryBrowser.folder != path) {
        return false;
    }
    if (backgroundToken != imageHistoryBackgroundLoadToken) {
        return false;
    }
    return requestKey == imageHistoryBackgroundRequestKey;
}

function scheduleImageHistoryBackgroundWatchdog(path, depth, sortBy, reverse, showHidden, requestKey, backgroundToken) {
    clearImageHistoryBackgroundWatchdog();
    imageHistoryBackgroundWatchdog = setTimeout(() => {
        if (!isImageHistoryBackgroundRequestStillRelevant(path, requestKey, backgroundToken) || imageHistoryStartupStage != 'recent_loaded') {
            return;
        }
        if (imageHistoryBackgroundRetryCount >= IMAGE_HISTORY_BACKGROUND_MAX_RETRIES) {
            setImageHistoryRequestStatus('error', 'History is taking too long to fully load. Retry when ready.');
            return;
        }
        imageHistoryBackgroundRetryCount++;
        setImageHistoryRequestStatus('loading', 'Still loading older history...');
        queueFullImageHistoryLoad(path, depth, sortBy, reverse, showHidden);
    }, IMAGE_HISTORY_BACKGROUND_WATCHDOG_DELAY_MS);
}

function retryImageHistoryManually() {
    clearImageHistoryAutoRetry();
    if (imageHistoryBrowser) {
        imageHistoryBrowser.lightRefresh();
    }
}

function ensureImageHistoryStatusReady() {
    let statusElem = document.getElementById('image_history_request_status');
    if (!statusElem || statusElem.dataset.ready) {
        return;
    }
    statusElem.dataset.ready = 'true';
    getRequiredElementById('image_history_retry_button').addEventListener('click', () => {
        retryImageHistoryManually();
    });
}

function setImageHistoryRequestStatus(state, message = '') {
    let statusElem = document.getElementById('image_history_request_status');
    let textElem = document.getElementById('image_history_request_status_text');
    let retryButton = document.getElementById('image_history_retry_button');
    if (!statusElem || !textElem || !retryButton) {
        return;
    }
    statusElem.dataset.state = state;
    textElem.innerText = message;
    textElem.title = message;
    retryButton.style.display = state == 'error' ? '' : 'none';
}

function ensureImageHistoryBrowserShellReady() {
    if (!imageHistoryBrowser) {
        return;
    }
    imageHistoryBrowser.ensureBuilt();
    ensureImageHistoryStatusReady();
}

function ensureImageHistoryHeaderControlsReady(sortBy, reverse, allowAnims, showHidden) {
    ensureImageHistoryBrowserShellReady();
    let sortElem = document.getElementById('image_history_sort_by');
    let sortReverseElem = document.getElementById('image_history_sort_reverse');
    let allowAnimsElem = document.getElementById('image_history_allow_anims');
    let showHiddenElem = document.getElementById('image_history_show_hidden');
    if (!sortElem || !sortReverseElem || !allowAnimsElem || !showHiddenElem) {
        return null;
    }
    sortElem.value = sortBy;
    sortReverseElem.checked = reverse;
    allowAnimsElem.checked = allowAnims;
    showHiddenElem.checked = showHidden;
    if (!sortElem.dataset.ready) {
        sortElem.dataset.ready = 'true';
        sortElem.addEventListener('change', () => {
            localStorage.setItem('image_history_sort_by', sortElem.value);
            requestImageHistoryRefresh();
        });
        sortReverseElem.addEventListener('change', () => {
            localStorage.setItem('image_history_sort_reverse', sortReverseElem.checked);
            requestImageHistoryRefresh();
        });
        allowAnimsElem.addEventListener('change', () => {
            localStorage.setItem('image_history_allow_anims', allowAnimsElem.checked);
            requestImageHistoryRefresh();
        });
        showHiddenElem.addEventListener('change', () => {
            imageHistoryShowHidden = showHiddenElem.checked;
            localStorage.setItem('image_history_show_hidden', showHiddenElem.checked);
            requestImageHistoryRefresh();
        });
    }
    ensureImageHistoryBulkControlsReady();
    return { sortElem, sortReverseElem, allowAnimsElem, showHiddenElem };
}

function scheduleImageHistoryAutoRetry() {
    if (imageHistoryHasLoadedOnce || imageHistoryInitialAutoRetryUsed || imageHistoryAutoRetryTimer) {
        return false;
    }
    imageHistoryInitialAutoRetryUsed = true;
    imageHistoryAutoRetryTimer = setTimeout(() => {
        imageHistoryAutoRetryTimer = null;
        imageHistoryNextLoadIsRetry = true;
        setImageHistoryRequestStatus('retrying', 'Retrying history load...');
        if (imageHistoryBrowser) {
            imageHistoryBrowser.lightRefresh();
        }
    }, IMAGE_HISTORY_AUTO_RETRY_DELAY_MS);
    return true;
}

function parseHistoryMetadata(metadata) {
    if (!metadata) {
        return {};
    }
    if (typeof metadata == 'object') {
        return metadata;
    }
    if (imageHistoryMetadataCache.has(metadata)) {
        return imageHistoryMetadataCache.get(metadata);
    }
    let parsed = {};
    try {
        parsed = JSON.parse(interpretMetadata(metadata)) || {};
    }
    catch (e) {
        parsed = {};
    }
    if (imageHistoryMetadataCache.size >= IMAGE_HISTORY_METADATA_CACHE_LIMIT) {
        let firstKey = imageHistoryMetadataCache.keys().next().value;
        imageHistoryMetadataCache.delete(firstKey);
    }
    imageHistoryMetadataCache.set(metadata, parsed);
    return parsed;
}

/**
 * Converts a metadata value into searchable text.
 */
function imageHistoryValueToSearchText(value) {
    if (value == null) {
        return '';
    }
    if (Array.isArray(value)) {
        return value.map(v => imageHistoryValueToSearchText(v)).join(' ');
    }
    if (typeof value == 'object') {
        return Object.values(value).map(v => imageHistoryValueToSearchText(v)).join(' ');
    }
    return `${value}`;
}

/**
 * Builds field-specific searchable metadata for the image history filter.
 */
function getImageHistorySearchFields(image, parsedMeta) {
    let params = parsedMeta.sui_image_params || {};
    let extra = parsedMeta.sui_extra_data || {};
    let name = image.data.name || '';
    let fullsrc = image.data.fullsrc || '';
    let folder = fullsrc.includes('/') ? fullsrc.substring(0, fullsrc.lastIndexOf('/')) : '';
    let extension = name.includes('.') ? name.split('.').pop().toLowerCase() : '';
    let rawMetadata = typeof image.data.metadata == 'string' ? image.data.metadata : imageHistoryValueToSearchText(image.data.metadata);
    let hasMetadataText = rawMetadata ? 'true yes metadata' : 'false no none';
    let generationResolution = params.width && params.height ? `${params.width}x${params.height}` : '';
    let finalResolution = extra.final_width && extra.final_height ? `${extra.final_width}x${extra.final_height}` : generationResolution;
    let favoriteText = parsedMeta.is_starred ? 'true yes starred favorite' : 'false no unstarred';
    let fields = {
        name: name,
        path: fullsrc,
        folder: folder,
        type: extension,
        filetype: extension,
        has: hasMetadataText,
        date: fullsrc,
        metadata: `${rawMetadata} ${imageHistoryValueToSearchText(parsedMeta)}`,
        prompt: `${params.prompt || ''} ${extra.original_prompt || ''}`,
        negative: params.negativeprompt || '',
        model: params.model || '',
        lora: `${imageHistoryValueToSearchText(params.loras)} ${imageHistoryValueToSearchText(params.loraweights)}`,
        vae: params.vae || '',
        sampler: params.sampler || '',
        scheduler: params.scheduler || '',
        seed: params.seed || '',
        resolution: `${generationResolution} ${finalResolution}`,
        rating: parsedMeta.rating || extra.rating || '',
        favorite: favoriteText,
        tags: `${imageHistoryValueToSearchText(parsedMeta.tags)} ${imageHistoryValueToSearchText(extra.tags)}`,
        session: `${params.session_id || ''} ${extra.session_id || ''}`,
        wildcard: `${extra.prompt_lab_wildcard_values || ''} ${imageHistoryValueToSearchText(extra.prompt_lab_wildcards)}`,
        promptlab: `${extra.prompt_lab_id || ''} ${extra.prompt_lab_prompt_id || ''}`
    };
    fields.allFields = Object.values(fields).join(' ');
    return fields;
}

/**
 * Splits a search query into terms while preserving quoted text.
 */
function splitImageHistoryFilterQuery(filter) {
    let terms = [];
    let current = '';
    let quote = null;
    for (let i = 0; i < filter.length; i++) {
        let char = filter[i];
        if ((char == '"' || char == "'") && (quote == null || quote == char)) {
            quote = quote == char ? null : char;
            continue;
        }
        if (!quote && /\s/.test(char)) {
            if (current.trim()) {
                terms.push(current.trim());
            }
            current = '';
            continue;
        }
        current += char;
    }
    if (current.trim()) {
        terms.push(current.trim());
    }
    return terms;
}

/**
 * Normalizes aliases for image history structured search fields.
 */
function normalizeImageHistoryFilterField(field) {
    let aliases = {
        fav: 'favorite',
        starred: 'favorite',
        tag: 'tags',
        neg: 'negative',
        negativeprompt: 'negative',
        loras: 'lora',
        res: 'resolution',
        size: 'resolution',
        prompt_lab: 'promptlab',
        prompt_lab_id: 'promptlab',
        ext: 'filetype',
        file_type: 'filetype',
        has_metadata: 'has',
        wildcard_values: 'wildcard'
    };
    return aliases[field] || field;
}

/**
 * Matches image history entries against text and field:value search terms.
 */
function imageHistoryFilterMatches(desc, filter) {
    if (!filter) {
        return true;
    }
    let searchable = desc.searchable || {};
    let allFields = `${searchable.allFields || ''}`.toLowerCase();
    let terms = splitImageHistoryFilterQuery(filter);
    for (let term of terms) {
        let fieldSplit = term.indexOf(':');
        if (fieldSplit > 0) {
            let field = normalizeImageHistoryFilterField(term.substring(0, fieldSplit).toLowerCase());
            let value = term.substring(fieldSplit + 1).toLowerCase();
            if (!value) {
                continue;
            }
            let fieldText = searchable[field];
            if (fieldText == null) {
                if (!allFields.includes(term.toLowerCase())) {
                    return false;
                }
                continue;
            }
            if (!`${fieldText}`.toLowerCase().includes(value)) {
                return false;
            }
        }
        else if (!allFields.includes(term.toLowerCase())) {
            return false;
        }
    }
    return true;
}

/**
 * Updates the history filter input hint after the browser builds its header.
 */
function updateImageHistoryFilterHint() {
    let filterInput = document.getElementById('imagehistorybrowser_filter_input');
    if (!filterInput || filterInput.dataset.historyFilterHint == 'true') {
        return;
    }
    filterInput.dataset.historyFilterHint = 'true';
    filterInput.placeholder = 'Search prompt/model/metadata...';
    filterInput.title = 'Search text, or use field:value terms like model:sdxl seed:123 res:1024x1024 wildcard:dragon.';
}

/**
 * Gets a loaded history file by relative path.
 */
function getImageHistoryFile(path) {
    return imageHistoryBrowser?.lastFilesMap?.get(path) || null;
}

/**
 * Ensures the image history compare modal exists.
 */
function ensureImageHistoryCompareModal() {
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
        closeImageHistoryCompareModal();
    };
    getRequiredElementById('image_history_compare_fit').onclick = () => {
        setImageHistoryCompareZoom(100);
    };
    getRequiredElementById('image_history_compare_swap').onclick = () => {
        swapImageHistoryCompareImages();
    };
    getRequiredElementById('image_history_compare_reuse_a').onclick = () => {
        reuseImageHistoryCompareSettings('first');
    };
    getRequiredElementById('image_history_compare_reuse_b').onclick = () => {
        reuseImageHistoryCompareSettings('second');
    };
    getRequiredElementById('image_history_compare_star_a').onclick = () => {
        starImageHistoryCompareImage('first');
    };
    getRequiredElementById('image_history_compare_star_b').onclick = () => {
        starImageHistoryCompareImage('second');
    };
    getRequiredElementById('image_history_compare_zoom').addEventListener('input', e => {
        setImageHistoryCompareZoom(e.target.value);
    });
    getRequiredElementById('image_history_compare_diff').addEventListener('change', e => {
        setImageHistoryCompareDiffMode(e.target.checked);
    });
    getRequiredElementById('image_history_compare_metadata').addEventListener('change', e => {
        setImageHistoryCompareMetadataMode(e.target.checked);
    });
    let stage = modal.querySelector('.image-history-compare-stage');
    stage.addEventListener('pointermove', updateImageHistoryCompareRevealFromPointer);
    stage.addEventListener('pointerdown', updateImageHistoryCompareRevealFromPointer);
    return modal;
}

/**
 * Sends one compared image's generation settings back to the Generate tab.
 */
function reuseImageHistoryCompareSettings(side) {
    if (!imageHistoryCompareFiles) {
        return;
    }
    let file = imageHistoryCompareFiles[side];
    if (!file?.data?.metadata) {
        showError('Selected compare image has no reusable metadata.');
        return;
    }
    setCurrentImage(file.data.src, file.data.metadata, 'history');
    copy_current_image_params();
    closeImageHistoryCompareModal();
}

/**
 * Toggles starred state for one compared image.
 */
function starImageHistoryCompareImage(side) {
    if (!imageHistoryCompareFiles) {
        return;
    }
    let file = imageHistoryCompareFiles[side];
    if (!file?.data?.fullsrc || !file?.data?.src) {
        return;
    }
    toggleStar(file.data.fullsrc, file.data.src);
}

/**
 * Loads the current compare pair into the overlay view.
 */
function renderImageHistoryComparePair() {
    if (!imageHistoryCompareFiles) {
        return;
    }
    let first = imageHistoryCompareFiles.first;
    let second = imageHistoryCompareFiles.second;
    getRequiredElementById('image_history_compare_title').innerText = `${first.data.name || first.name} / ${second.data.name || second.name}`;
    getRequiredElementById('image_history_compare_img_a').src = first.data.src;
    getRequiredElementById('image_history_compare_img_b').src = second.data.src;
    if (getRequiredElementById('image_history_compare_diff').checked) {
        renderImageHistoryCompareDiff();
    }
    if (getRequiredElementById('image_history_compare_metadata').checked) {
        renderImageHistoryCompareMetadata();
    }
}

/**
 * Swaps image A and image B in the compare view.
 */
function swapImageHistoryCompareImages() {
    if (!imageHistoryCompareFiles) {
        return;
    }
    let oldFirst = imageHistoryCompareFiles.first;
    imageHistoryCompareFiles.first = imageHistoryCompareFiles.second;
    imageHistoryCompareFiles.second = oldFirst;
    renderImageHistoryComparePair();
}

/**
 * Returns focused generation metadata fields for comparison.
 */
function getImageHistoryCompareMetadataFields(file) {
    let metadata = parseHistoryMetadata(file?.data?.metadata);
    let params = metadata.sui_image_params || {};
    let extra = metadata.sui_extra_data || {};
    let resolution = params.width && params.height ? `${params.width}x${params.height}` : '';
    let finalResolution = extra.final_width && extra.final_height ? `${extra.final_width}x${extra.final_height}` : '';
    return {
        Prompt: params.prompt || extra.original_prompt || '',
        Negative: params.negativeprompt || extra.original_negativeprompt || '',
        Model: params.model || '',
        LoRAs: imageHistoryValueToSearchText(params.loras),
        VAE: params.vae || '',
        Sampler: params.sampler || '',
        Scheduler: params.scheduler || '',
        Seed: params.seed || '',
        CFG: params.cfgscale || params.cfg || '',
        Steps: params.steps || '',
        Resolution: resolution,
        'Final Resolution': finalResolution,
        'Prompt Lab': extra.prompt_lab_id || extra.prompt_lab_prompt_id || '',
        Wildcards: extra.prompt_lab_wildcard_values || ''
    };
}

/**
 * Enables or disables metadata compare mode.
 */
function setImageHistoryCompareMetadataMode(enabled) {
    let modal = getRequiredElementById('image_history_compare_modal');
    modal.classList.toggle('image-history-compare-metadata-active', !!enabled);
    if (enabled) {
        renderImageHistoryCompareMetadata();
    }
}

/**
 * Renders metadata differences for the two compared images.
 */
function renderImageHistoryCompareMetadata() {
    let panel = getRequiredElementById('image_history_compare_metadata_panel');
    if (!imageHistoryCompareFiles) {
        panel.innerHTML = '';
        return;
    }
    let firstFields = getImageHistoryCompareMetadataFields(imageHistoryCompareFiles.first);
    let secondFields = getImageHistoryCompareMetadataFields(imageHistoryCompareFiles.second);
    let html = '<table class="image-history-compare-metadata-table"><thead><tr><th>Field</th><th>A</th><th>B</th></tr></thead><tbody>';
    for (let key of Object.keys(firstFields)) {
        let firstValue = imageHistoryValueToSearchText(firstFields[key]);
        let secondValue = imageHistoryValueToSearchText(secondFields[key]);
        let className = firstValue == secondValue ? 'image-history-compare-metadata-same' : 'image-history-compare-metadata-different';
        html += `<tr class="${className}"><td>${escapeHtml(key)}</td><td>${escapeHtml(firstValue)}</td><td>${escapeHtml(secondValue)}</td></tr>`;
    }
    html += '</tbody></table>';
    panel.innerHTML = html;
}

/**
 * Closes the image history compare modal.
 */
function closeImageHistoryCompareModal() {
    cleanupImageHistoryCompareModal();
    showGenerateTabAfterImageHistoryCompareClose();
}

/**
 * Clears any leftover compare modal state.
 */
function cleanupImageHistoryCompareModal() {
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
function showGenerateTabAfterImageHistoryCompareClose() {
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
function openImageHistoryCompareModal() {
    let modal = getRequiredElementById('image_history_compare_modal');
    cleanupImageHistoryCompareModal();
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
function updateImageHistoryCompareRevealFromPointer(e) {
    let base = getRequiredElementById('image_history_compare_img_a');
    let rect = base.getBoundingClientRect();
    if (rect.width <= 0) {
        return;
    }
    let reveal = ((e.clientX - rect.left) / rect.width) * 100;
    setImageHistoryCompareReveal(reveal);
}

/**
 * Sets the overlay reveal split.
 */
function setImageHistoryCompareReveal(value) {
    let reveal = Math.max(0, Math.min(100, parseFloat(value) || 0));
    getRequiredElementById('image_history_compare_img_b').style.clipPath = `inset(0 ${100 - reveal}% 0 0)`;
    getRequiredElementById('image_history_compare_diff_canvas').style.clipPath = `inset(0 ${100 - reveal}% 0 0)`;
    getRequiredElementById('image_history_compare_divider').style.left = `${reveal}%`;
}

/**
 * Enables or disables highlighted pixel diff mode.
 */
function setImageHistoryCompareDiffMode(enabled) {
    let modal = getRequiredElementById('image_history_compare_modal');
    modal.classList.toggle('image-history-compare-diff-active', !!enabled);
    if (enabled) {
        renderImageHistoryCompareDiff();
    }
}

/**
 * Renders a red-pink diff overlay from image A to image B.
 */
function renderImageHistoryCompareDiff() {
    let imgA = getRequiredElementById('image_history_compare_img_a');
    let imgB = getRequiredElementById('image_history_compare_img_b');
    if (!imgA.complete || !imgB.complete || imgA.naturalWidth == 0 || imgB.naturalWidth == 0) {
        setTimeout(renderImageHistoryCompareDiff, 80);
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
function setImageHistoryCompareZoom(value) {
    let zoom = Math.max(25, Math.min(200, parseInt(value) || 100));
    getRequiredElementById('image_history_compare_zoom').value = zoom;
    for (let id of ['image_history_compare_img_a', 'image_history_compare_img_b', 'image_history_compare_diff_canvas']) {
        let img = getRequiredElementById(id);
        img.style.width = `${zoom}%`;
        img.style.maxWidth = zoom <= 100 ? '100%' : 'none';
    }
}

/**
 * Opens the side-by-side compare viewer for two selected history images.
 */
function showImageHistoryCompare(paths) {
    if (paths.length != 2) {
        showError('Select exactly two images to compare.');
        return;
    }
    let first = getImageHistoryFile(paths[0]);
    let second = getImageHistoryFile(paths[1]);
    if (!first || !second) {
        showError('Selected images are not loaded in history.');
        return;
    }
    ensureImageHistoryCompareModal();
    imageHistoryCompareFiles = { first, second };
    getRequiredElementById('image_history_compare_diff').checked = false;
    getRequiredElementById('image_history_compare_metadata').checked = false;
    renderImageHistoryComparePair();
    setImageHistoryCompareDiffMode(false);
    setImageHistoryCompareMetadataMode(false);
    setImageHistoryCompareZoom(100);
    setImageHistoryCompareReveal(50);
    openImageHistoryCompareModal();
}

/**
 * Interpret history metadata when possible, but never let a bad blob abort the list render.
 */
function safeInterpretHistoryMetadata(metadata, fullsrc = '') {
    if (!metadata) {
        return metadata;
    }
    try {
        let interpreted = interpretMetadata(metadata);
        return interpreted ?? metadata;
    }
    catch (e) {
        console.log(`Failed to interpret history metadata${fullsrc ? ` for '${fullsrc}'` : ''}: ${e}`);
        return metadata;
    }
}

function orderHistoryFilesForDisplay(files) {
    function isPreSortFile(file) {
        return file.src == 'index.html';
    }
    let preFiles = files.filter(file => isPreSortFile(file));
    let postFiles = files.filter(file => !isPreSortFile(file));
    return preFiles.concat(postFiles);
}

function mapHistoryFiles(prefix, files) {
    return files.map(file => {
        let fullSrc = `${prefix}${file.src}`;
        return { 'name': fullSrc, 'data': { 'src': getHistoryImageSrc(fullSrc), 'fullsrc': fullSrc, 'name': file.src, 'metadata': safeInterpretHistoryMetadata(file.metadata, fullSrc) } };
    });
}

function replaceHistoryBrowserContents(path, folders, mapped) {
    if (!imageHistoryBrowser) {
        return;
    }
    imageHistoryBrowser.lastListCache = { folder: path, folders, files: mapped };
    imageHistoryBrowser.build(path, folders, mapped);
}

function setMetadataBoolValue(metadata, key, value) {
    if (!metadata) {
        return JSON.stringify({ [key]: value });
    }
    try {
        let parsed = { ...parseHistoryMetadata(metadata) };
        parsed[key] = value;
        return JSON.stringify(parsed);
    }
    catch (e) {
        return metadata;
    }
}

function getImageHistoryEntries() {
    let historySection = document.getElementById('imagehistorybrowser-content');
    if (!historySection) {
        return [];
    }
    return Array.from(historySection.children).filter(c => c.dataset?.name);
}

function pruneImageHistorySelectionToCurrentFiles() {
    if (!imageHistoryBrowser?.lastFiles) {
        return;
    }
    let currentFiles = new Set(imageHistoryBrowser.lastFiles.map(f => f.name));
    for (let path of imageHistorySelected) {
        if (!currentFiles.has(path)) {
            imageHistorySelected.delete(path);
        }
    }
}

function getCheckedImageHistoryPaths() {
    let selected = [];
    for (let entry of getImageHistoryEntries()) {
        let checkbox = entry.querySelector('.browser-entry-checkbox');
        if (checkbox?.checked && entry.dataset?.name) {
            selected.push(entry.dataset.name);
        }
    }
    return selected;
}

function syncImageHistorySelectionFromDOM() {
    let checkedPaths = getCheckedImageHistoryPaths();
    if (checkedPaths.length == 0) {
        if (imageHistorySelected.size == 0) {
            return;
        }
        imageHistorySelected.clear();
        for (let entry of getImageHistoryEntries()) {
            entry.classList.remove('browser-entry-selected');
        }
        return;
    }
    imageHistorySelected = new Set(checkedPaths);
    for (let entry of getImageHistoryEntries()) {
        entry.classList.toggle('browser-entry-selected', imageHistorySelected.has(entry.dataset.name));
    }
}

function updateImageHistoryBulkControls() {
    syncImageHistorySelectionFromDOM();
    let controls = document.getElementById('image_history_bulk_controls');
    if (!controls) {
        return;
    }
    let canHide = permissions.hasPermission('view_image_history');
    let canDelete = permissions.hasPermission('user_delete_image');
    let canStar = permissions.hasPermission('user_star_images');
    let canCompare = true;
    controls.style.display = canDelete || canHide || canStar || canCompare ? '' : 'none';
    if (!canDelete && !canHide && !canStar && !canCompare) {
        return;
    }
    let count = imageHistorySelected.size;
    let countElem = document.getElementById('image_history_selected_count');
    if (countElem) {
        countElem.innerText = `${count} selected`;
    }
    let selectAllButton = document.getElementById('image_history_select_all');
    let clearButton = document.getElementById('image_history_clear_selection');
    let hideButton = document.getElementById('image_history_hide_selected');
    let unhideButton = document.getElementById('image_history_unhide_selected');
    let deleteButton = document.getElementById('image_history_delete_selected');
    let compareButton = document.getElementById('image_history_compare_selected');
    let exportMetadataButton = document.getElementById('image_history_export_metadata_selected');
    let sendPromptLabButton = document.getElementById('image_history_send_prompt_lab_selected');
    let starButton = document.getElementById('image_history_star_selected');
    let unstarButton = document.getElementById('image_history_unstar_selected');
    let anyEntries = getImageHistoryEntries().length > 0;
    if (selectAllButton) {
        selectAllButton.disabled = !anyEntries || imageHistoryBulkActionRunning;
    }
    if (clearButton) {
        clearButton.disabled = count == 0 || imageHistoryBulkActionRunning;
    }
    if (hideButton) {
        hideButton.style.display = canHide ? '' : 'none';
        hideButton.disabled = count == 0 || imageHistoryBulkActionRunning;
    }
    if (unhideButton) {
        unhideButton.style.display = canHide ? '' : 'none';
        unhideButton.disabled = count == 0 || imageHistoryBulkActionRunning;
    }
    if (deleteButton) {
        deleteButton.style.display = canDelete ? '' : 'none';
        deleteButton.disabled = count == 0 || imageHistoryBulkActionRunning;
    }
    if (compareButton) {
        compareButton.disabled = count < 2 || imageHistoryBulkActionRunning;
        compareButton.innerText = count > 2 ? 'Compare First Two' : 'Compare';
    }
    if (exportMetadataButton) {
        exportMetadataButton.disabled = count == 0 || imageHistoryBulkActionRunning;
    }
    if (sendPromptLabButton) {
        sendPromptLabButton.disabled = count == 0 || imageHistoryBulkActionRunning;
    }
    if (starButton) {
        starButton.style.display = canStar ? '' : 'none';
        starButton.disabled = count == 0 || imageHistoryBulkActionRunning;
    }
    if (unstarButton) {
        unstarButton.style.display = canStar ? '' : 'none';
        unstarButton.disabled = count == 0 || imageHistoryBulkActionRunning;
    }
}

function setImageHistorySelection(fullsrc, isSelected, entry = null) {
    if (isSelected) {
        imageHistorySelected.add(fullsrc);
    }
    else {
        imageHistorySelected.delete(fullsrc);
    }
    if (!entry) {
        entry = getImageHistoryEntries().find(e => e.dataset.name == fullsrc);
    }
    if (entry) {
        entry.classList.toggle('browser-entry-selected', isSelected);
        let checkbox = entry.querySelector('.browser-entry-checkbox');
        if (checkbox) {
            checkbox.checked = isSelected;
        }
    }
    updateImageHistoryBulkControls();
}

function clearImageHistorySelection() {
    imageHistorySelected.clear();
    for (let entry of getImageHistoryEntries()) {
        entry.classList.remove('browser-entry-selected');
        let checkbox = entry.querySelector('.browser-entry-checkbox');
        if (checkbox) {
            checkbox.checked = false;
        }
    }
    updateImageHistoryBulkControls();
}

function selectAllImageHistory() {
    for (let entry of getImageHistoryEntries()) {
        setImageHistorySelection(entry.dataset.name, true, entry);
    }
    updateImageHistoryBulkControls();
}

function clearSelectedImageHistory() {
    clearImageHistorySelection();
}

function hideSelectedImageHistory() {
    setSelectedHistoryImagesHidden(true);
}

function unhideSelectedImageHistory() {
    setSelectedHistoryImagesHidden(false);
}

function deleteSelectedImageHistory() {
    deleteSelectedHistoryImages();
}

function starSelectedImageHistory() {
    setSelectedHistoryImagesStarred(true);
}

function unstarSelectedImageHistory() {
    setSelectedHistoryImagesStarred(false);
}

function compareSelectedImageHistory() {
    showImageHistoryCompare([...imageHistorySelected].slice(0, 2));
}

function exportSelectedImageHistoryMetadata() {
    syncImageHistorySelectionFromDOM();
    let selected = [...imageHistorySelected];
    if (selected.length == 0) {
        return;
    }
    let exported = [];
    for (let fullsrc of selected) {
        let file = getImageHistoryFile(fullsrc);
        if (!file) {
            continue;
        }
        exported.push({
            path: fullsrc,
            name: file.data.name,
            metadata: parseHistoryMetadata(file.data.metadata),
            raw_metadata: file.data.metadata
        });
    }
    let stamp = new Date().toISOString().replaceAll(':', '-').replaceAll('.', '-');
    downloadPlainText(`image-history-metadata-${stamp}.json`, JSON.stringify(exported, null, 2));
}

/**
 * Extracts Prompt Lab prompt fields from image metadata.
 */
function imageHistoryMetadataToPromptLabPrompt(file) {
    let metadata = parseHistoryMetadata(file.data.metadata);
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

async function sendSelectedImageHistoryToPromptLab() {
    if (imageHistoryBulkActionRunning) {
        return;
    }
    syncImageHistorySelectionFromDOM();
    let selected = [...imageHistorySelected];
    if (selected.length == 0) {
        return;
    }
    imageHistoryBulkActionRunning = true;
    updateImageHistoryBulkControls();
    let saved = 0;
    let skipped = 0;
    let failed = 0;
    for (let fullsrc of selected) {
        let file = getImageHistoryFile(fullsrc);
        if (!file) {
            skipped++;
            continue;
        }
        let item = imageHistoryMetadataToPromptLabPrompt(file);
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
    imageHistoryBulkActionRunning = false;
    updateImageHistoryBulkControls();
    if (saved > 0 && window.promptLab?.load) {
        promptLab.load();
    }
    if (failed > 0) {
        showError(`Sent ${saved} prompt(s) to Prompt Lab. Skipped ${skipped}. Failed ${failed}.`);
    }
    else {
        doNoticePopover(`Sent ${saved} prompt${saved == 1 ? '' : 's'} to Prompt Lab${skipped > 0 ? `, skipped ${skipped}` : ''}.`, 'notice-pop-green');
    }
}

async function setSelectedHistoryImagesStarred(targetStarred) {
    if (imageHistoryBulkActionRunning) {
        return;
    }
    syncImageHistorySelectionFromDOM();
    let selected = [...imageHistorySelected];
    if (selected.length == 0) {
        return;
    }
    imageHistoryBulkActionRunning = true;
    updateImageHistoryBulkControls();
    let changed = 0;
    let failed = 0;
    for (let fullsrc of selected) {
        let file = getImageHistoryFile(fullsrc);
        if (!file || parseHistoryMetadata(file.data.metadata).is_starred == targetStarred) {
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
        file.data.metadata = setMetadataBoolValue(file.data.metadata ?? '{}', 'is_starred', result.new_state);
        forEachSwarmImageCardForSrc(file.data.src, card => {
            if (card.setStarred) {
                card.setStarred(result.new_state);
            }
            else {
                card.classList.toggle('image-block-starred', result.new_state);
            }
        });
    }
    imageHistoryBulkActionRunning = false;
    updateImageHistoryBulkControls();
    if (changed > 0) {
        requestImageHistoryRefresh();
    }
    if (failed > 0) {
        showError(`${targetStarred ? 'Starred' : 'Unstarred'} ${changed} image(s). Failed ${failed}.`);
    }
    else if (changed > 0) {
        doNoticePopover(`${targetStarred ? 'Starred' : 'Unstarred'} ${changed} image${changed == 1 ? '' : 's'}.`, 'notice-pop-green');
    }
}

function ensureImageHistoryBulkControlsReady() {
    let controls = document.getElementById('image_history_bulk_controls');
    if (!controls || controls.dataset.ready) {
        updateImageHistoryBulkControls();
        return;
    }
    controls.dataset.ready = 'true';
    getRequiredElementById('image_history_select_all').onclick = (e) => {
        e.preventDefault();
        selectAllImageHistory();
    };
    getRequiredElementById('image_history_clear_selection').onclick = (e) => {
        e.preventDefault();
        clearSelectedImageHistory();
    };
    getRequiredElementById('image_history_hide_selected').onclick = (e) => {
        e.preventDefault();
        hideSelectedImageHistory();
    };
    getRequiredElementById('image_history_unhide_selected').onclick = (e) => {
        e.preventDefault();
        unhideSelectedImageHistory();
    };
    getRequiredElementById('image_history_delete_selected').onclick = (e) => {
        e.preventDefault();
        deleteSelectedImageHistory();
    };
    getRequiredElementById('image_history_star_selected').onclick = (e) => {
        e.preventDefault();
        starSelectedImageHistory();
    };
    getRequiredElementById('image_history_unstar_selected').onclick = (e) => {
        e.preventDefault();
        unstarSelectedImageHistory();
    };
    getRequiredElementById('image_history_compare_selected').onclick = (e) => {
        e.preventDefault();
        compareSelectedImageHistory();
    };
    getRequiredElementById('image_history_export_metadata_selected').onclick = (e) => {
        e.preventDefault();
        exportSelectedImageHistoryMetadata();
    };
    getRequiredElementById('image_history_send_prompt_lab_selected').onclick = (e) => {
        e.preventDefault();
        sendSelectedImageHistoryToPromptLab();
    };
    updateImageHistoryBulkControls();
}

function removeImageFromHistoryUI(fullsrc, src, explicitEntry = null) {
    imageHistorySelected.delete(fullsrc);
    let historySection = document.getElementById('imagehistorybrowser-content');
    if (historySection) {
        let entry = explicitEntry || getImageHistoryEntries().find(e => e.dataset.name == fullsrc || e.dataset.name == src);
        if (entry) {
            entry.remove();
        }
    }
    let currentImage = currentImageHelper.getCurrentImage();
    if (currentImage && currentImage.dataset.src == src) {
        setCurrentImage(null);
    }
    let currentBatch = document.getElementById('current_image_batch');
    if (currentBatch) {
        let batchEntry = Array.from(currentBatch.children).find(e => e.dataset?.src == src);
        if (batchEntry) {
            removeImageBlockFromBatch(batchEntry);
        }
    }
    updateImageHistoryBulkControls();
}

function deleteSingleHistoryImage(fullsrc, src, explicitEntry = null, errorHandle = null) {
    return new Promise(resolve => {
        let onSuccess = () => {
            removeImageFromHistoryUI(fullsrc, src, explicitEntry);
            resolve({ success: true });
        };
        genericRequest('DeleteImage', { 'path': fullsrc }, onSuccess, 0, error => {
            if (errorHandle) {
                errorHandle(error);
            }
            else {
                showError(error);
            }
            resolve({ success: false, error });
        });
    });
}

function toggleImageHidden(path, rawSrc, refreshAfter = true, errorHandle = null) {
    return new Promise(resolve => {
        genericRequest('ToggleImageHidden', { 'path': path }, data => {
            let setHidden = metadata => setMetadataBoolValue(metadata, 'is_hidden', data.new_state);
            let curImgImg = currentImageHelper.getCurrentImage();
            if (curImgImg && curImgImg.dataset.src == rawSrc) {
                curImgImg.dataset.metadata = setHidden(curImgImg.dataset.metadata ?? '{}');
            }
            if (typeof forEachSwarmImageCardForSrc == 'function') {
                forEachSwarmImageCardForSrc(rawSrc, card => {
                    if (card.setHidden) {
                        card.setHidden(data.new_state);
                    }
                    else {
                        card.dataset.metadata = setHidden(card.dataset.metadata ?? '{}');
                        card.classList.toggle('image-block-hidden', data.new_state);
                    }
                });
            }
            if (imageFullView.isOpen() && imageFullView.currentSrc == rawSrc) {
                let state = imageFullView.copyState();
                imageFullView.showImage(rawSrc, setHidden(imageFullView.currentMetadata), imageFullView.currentBatchId);
                imageFullView.pasteState(state);
            }
            if (imageHistoryBrowser) {
                let file = imageHistoryBrowser.getFileFor(path);
                if (file?.data) {
                    file.data.metadata = setHidden(file.data.metadata ?? '{}');
                }
                if (refreshAfter) {
                    requestImageHistoryRefresh();
                }
            }
            resolve({ success: true, new_state: data.new_state });
        }, 0, error => {
            if (errorHandle) {
                errorHandle(error);
            }
            else {
                showError(error);
            }
            resolve({ success: false, error });
        });
    });
}

async function setSelectedHistoryImagesHidden(targetHidden) {
    if (imageHistoryBulkActionRunning) {
        return;
    }
    syncImageHistorySelectionFromDOM();
    let selected = [...imageHistorySelected];
    if (selected.length == 0) {
        return;
    }
    imageHistoryBulkActionRunning = true;
    updateImageHistoryBulkControls();
    let updated = 0;
    let skipped = 0;
    let failed = 0;
    for (let fullsrc of selected) {
        let current = imageHistoryBrowser?.getFileFor(fullsrc);
        let isHidden = parseHistoryMetadata(current?.data?.metadata).is_hidden === true;
        if (isHidden == targetHidden) {
            skipped++;
            continue;
        }
        let src = getHistoryImageSrc(fullsrc);
        let res = await toggleImageHidden(fullsrc, src, false, () => {});
        if (res.success) {
            updated++;
        }
        else {
            failed++;
            console.log(`Failed to ${targetHidden ? 'hide' : 'unhide'} image '${fullsrc}': ${res.error}`);
        }
    }
    imageHistoryBulkActionRunning = false;
    updateImageHistoryBulkControls();
    if (updated > 0) {
        requestImageHistoryRefresh();
    }
    if (failed > 0) {
        showError(`${targetHidden ? 'Hid' : 'Unhid'} ${updated} image(s), skipped ${skipped}, failed ${failed}.`);
    }
    else if (updated > 0 || skipped > 0) {
        doNoticePopover(`${targetHidden ? 'Hid' : 'Unhid'} ${updated} image${updated == 1 ? '' : 's'}${skipped > 0 ? ` (${skipped} already ${targetHidden ? 'hidden' : 'visible'})` : ''}.`, 'notice-pop-green');
    }
}

async function deleteSelectedHistoryImages() {
    if (imageHistoryBulkActionRunning) {
        return;
    }
    syncImageHistorySelectionFromDOM();
    let selected = [...imageHistorySelected];
    if (selected.length == 0) {
        return;
    }
    let imgWord = selected.length == 1 ? 'image' : 'images';
    if (!uiImprover.lastShift && getUserSetting('ui.checkifsurebeforedelete', true) && !confirm(`Are you sure you want to delete ${selected.length} ${imgWord}?\nHold shift to bypass.`)) {
        return;
    }
    imageHistoryBulkActionRunning = true;
    updateImageHistoryBulkControls();
    let deleted = 0;
    let failed = 0;
    for (let fullsrc of selected) {
        let src = getHistoryImageSrc(fullsrc);
        let res = await deleteSingleHistoryImage(fullsrc, src, null, () => {});
        if (res.success) {
            deleted++;
        }
        else {
            failed++;
            console.log(`Failed to delete image '${fullsrc}': ${res.error}`);
        }
    }
    imageHistoryBulkActionRunning = false;
    updateImageHistoryBulkControls();
    if (deleted > 0) {
        requestImageHistoryRefresh();
    }
    if (failed > 0) {
        showError(`Deleted ${deleted} image(s). Failed to delete ${failed} image(s).`);
    }
    else if (deleted > 0) {
        doNoticePopover(`Deleted ${deleted} image${deleted == 1 ? '' : 's'}.`, 'notice-pop-green');
    }
}

function listOutputHistoryFolderAndFiles(path, isRefresh, callback, depth, onError = null) {
    ensureImageHistoryBrowserShellReady();
    let sortBy = localStorage.getItem('image_history_sort_by') ?? 'Name';
    let reverse = localStorage.getItem('image_history_sort_reverse') == 'true';
    let allowAnims = localStorage.getItem('image_history_allow_anims') != 'false';
    let showHidden = imageHistoryShowHidden;
    let controlElems = ensureImageHistoryHeaderControlsReady(sortBy, reverse, allowAnims, showHidden);
    if (controlElems) {
        sortBy = controlElems.sortElem.value;
        reverse = controlElems.sortReverseElem.checked;
        allowAnims = controlElems.allowAnimsElem.checked;
        showHidden = controlElems.showHiddenElem.checked;
        imageHistoryShowHidden = showHidden;
    }
    let isRetryLoad = imageHistoryNextLoadIsRetry;
    imageHistoryNextLoadIsRetry = false;
    let loadToken = ++imageHistoryLoadToken;
    let useFastFirst = imageHistoryStartupStage == 'pending' && path == '' && !isRefresh;
    if (useFastFirst || path != '' || isRefresh) {
        clearImageHistoryBackgroundWatchdog();
    }
    let request = { 'path': path, 'depth': depth, 'sortBy': sortBy, 'sortReverse': reverse, 'includeHidden': showHidden };
    if (useFastFirst) {
        request.fastFirst = true;
        request.fastFirstLimit = IMAGE_HISTORY_FAST_FIRST_LIMIT;
    }
    setImageHistoryRequestStatus(isRetryLoad ? 'retrying' : 'loading', isRetryLoad ? 'Retrying history load...' : 'Loading history...');
    genericRequest('ListImages', request, data => {
        clearImageHistoryAutoRetry();
        imageHistoryHasLoadedOnce = true;
        let prefix = path == '' ? '' : (path.endsWith('/') ? path : `${path}/`);
        let folders = data.folders.sort((a, b) => b.toLowerCase().localeCompare(a.toLowerCase()));
        let mapped = mapHistoryFiles(prefix, orderHistoryFilesForDisplay(data.files));
        callback(folders, mapped);
        if (useFastFirst) {
            imageHistoryStartupStage = 'recent_loaded';
            imageHistoryBackgroundRetryCount = 0;
            imageHistoryBackgroundRequestKey = getImageHistoryRequestKey(path, depth, sortBy, reverse, showHidden);
            queueFullImageHistoryLoad(path, depth, sortBy, reverse, showHidden);
            return;
        }
        imageHistoryStartupStage = 'complete';
        clearImageHistoryBackgroundWatchdog();
        setImageHistoryRequestStatus('idle');
    }, 0, error => {
        showError(error);
        let shouldRetry = !isRetryLoad && scheduleImageHistoryAutoRetry();
        let errorMessage = `History failed to load: ${error}`;
        if (shouldRetry) {
            errorMessage += ' Retrying once...';
        }
        setImageHistoryRequestStatus('error', errorMessage);
        if (onError) {
            onError(error);
        }
    });
}

function queueFullImageHistoryLoad(path, depth, sortBy, reverse, showHidden) {
    let requestKey = getImageHistoryRequestKey(path, depth, sortBy, reverse, showHidden);
    let backgroundToken = ++imageHistoryBackgroundLoadToken;
    imageHistoryBackgroundRequestKey = requestKey;
    scheduleImageHistoryBackgroundWatchdog(path, depth, sortBy, reverse, showHidden, requestKey, backgroundToken);
    setTimeout(() => {
        genericRequest('ListImages', { 'path': path, 'depth': depth, 'sortBy': sortBy, 'sortReverse': reverse, 'includeHidden': showHidden }, data => {
            if (!isImageHistoryBackgroundRequestStillRelevant(path, requestKey, backgroundToken)) {
                return;
            }
            let prefix = path == '' ? '' : (path.endsWith('/') ? path : `${path}/`);
            let folders = data.folders.sort((a, b) => b.toLowerCase().localeCompare(a.toLowerCase()));
            let mapped = mapHistoryFiles(prefix, orderHistoryFilesForDisplay(data.files));
            imageHistoryStartupStage = 'complete';
            imageHistoryBackgroundRetryCount = 0;
            clearImageHistoryBackgroundWatchdog();
            replaceHistoryBrowserContents(path, folders, mapped);
            setImageHistoryRequestStatus('idle');
        }, 0, error => {
            if (!isImageHistoryBackgroundRequestStillRelevant(path, requestKey, backgroundToken)) {
                return;
            }
            console.log(`Background history fill failed: ${error}`);
            clearImageHistoryBackgroundWatchdog();
            if (imageHistoryBackgroundRetryCount >= IMAGE_HISTORY_BACKGROUND_MAX_RETRIES) {
                setImageHistoryRequestStatus('error', `History failed to load fully: ${error}`);
                return;
            }
            imageHistoryBackgroundRetryCount++;
            setImageHistoryRequestStatus('loading', 'Retrying older history...');
            queueFullImageHistoryLoad(path, depth, sortBy, reverse, showHidden);
        });
    }, 0);
}

function buttonsForImage(fullsrc, src, metadata, parsedMetadata = null, isCurrentImage = false) {
    if (typeof parsedMetadata == 'boolean' && !isCurrentImage) {
        isCurrentImage = parsedMetadata;
        parsedMetadata = null;
    }
    let isDataImage = src.startsWith('data:');
    parsedMetadata = parsedMetadata || parseHistoryMetadata(metadata);
    let mediaType = getMediaType(src);
    let buttons = [];
    if (permissions.hasPermission('user_star_images') && !isDataImage) {
        buttons.push({
            label: parsedMetadata.is_starred ? 'Unstar' : 'Star',
            title: 'Star or unstar this image - starred images get moved to a separate folder and highlighted.',
            className: parsedMetadata.is_starred ? ' star-button button-starred-image' : ' star-button',
            onclick: (e) => {
                toggleStar(fullsrc, src);
            }
        });
        buttons.push({
            label: 'Enable Starred',
            title: 'Marks all selected images as starred if they are not already',
            onclick: (e) => {
                if (!parsedMetadata.is_starred) {
                    toggleStar(fullsrc, src);
                }
            },
            can_multi: true,
            multi_only: true
        });
        buttons.push({
            label: 'Disabled Starred',
            title: 'Marks all selected images as NOT starred if they are currently starred',
            onclick: (e) => {
                if (parsedMetadata.is_starred) {
                    toggleStar(fullsrc, src);
                }
            },
            can_multi: true,
            multi_only: true
        });
    }
    if (!isDataImage) {
        buttons.push({
            label: parsedMetadata.is_hidden ? 'Unhide' : 'Hide',
            title: 'Hide this image from normal history view without deleting it.',
            onclick: (e) => {
                toggleImageHidden(fullsrc, src);
            }
        });
    }
    if (metadata) {
        buttons.push({
            label: 'Copy Raw Metadata',
            title: `Copies the raw form of the image's metadata to your clipboard (usually JSON text).`,
            onclick: (e) => {
                copyText(metadata);
                doNoticePopover('Copied!', 'notice-pop-green');
            }
        });
    }
    if (!isDataImage) {
        buttons.push({
            label: 'Copy Path',
            title: 'Copies the relative file path of this image to your clipboard.',
            onclick: (e) => {
                copyText(fullsrc);
                doNoticePopover('Copied!', 'notice-pop-green');
            }
        });
    }
    if (permissions.hasPermission('local_image_folder') && !isDataImage) {
        buttons.push({
            label: 'Open In Folder',
            title: 'Opens the folder containing this image in your local PC file explorer.',
            onclick: (e) => {
                genericRequest('OpenImageFolder', {'path': fullsrc}, data => {});
            }
        });
    }
    buttons.push({
        label: 'Download',
        title: 'Downloads this image to your PC.',
        href: escapeHtmlForUrl(src),
        is_download: true
    });
    // TODO: Multi-compat Download (create a zip?)
    if (permissions.hasPermission('user_delete_image') && !isDataImage) {
        buttons.push({
            label: 'Delete',
            title: 'Deletes this image from the server.',
            onclick: (e) => {
                if (!uiImprover.lastShift && getUserSetting('ui.checkifsurebeforedelete', true) && !confirm('Are you sure you want to delete this image?\nHold shift to bypass.')) {
                    return;
                }
                let deleteBehavior = getUserSetting('ui.deleteimagebehavior', 'next');
                let shifted = deleteBehavior == 'nothing' ? false : shiftToNextImagePreview(deleteBehavior == 'next', imageFullView.isOpen());
                if (!shifted) {
                    imageFullView.close();
                }
                deleteSingleHistoryImage(fullsrc, src, e);
            },
            can_multi: true
        });
    }
    for (let reg of registeredMediaButtons) {
        if ((isCurrentImage || reg.showInHistory) && (!reg.mediaTypes || reg.mediaTypes.includes(mediaType))) {
            buttons.push({
                label: reg.name,
                title: reg.title,
                href: reg.href,
                is_download: reg.is_download,
                can_multi: reg.can_multi,
                multi_only: reg.multi_only,
                onclick: () => reg.action(src)
            });
        }
    }
    return buttons;
}

function describeOutputFile(image) {
    let parsedMeta = parseHistoryMetadata(image.data.metadata);
    let buttons = buttonsForImage(image.data.fullsrc, image.data.src, image.data.metadata, parsedMeta);
    let canHide = permissions.hasPermission('view_image_history') && !image.data.src.startsWith('data:');
    let canDelete = permissions.hasPermission('user_delete_image') && !image.data.src.startsWith('data:');
    let canBulkSelect = canHide || canDelete;
    let isSelected = imageHistorySelected.has(image.data.fullsrc);
    let format = imageHistoryBrowser ? imageHistoryBrowser.format : 'Thumbnails';
    let shouldFormatMetadata = format.includes('Cards') || format == 'Details List';
    let formattedMetadata = shouldFormatMetadata ? formatMetadata(image.data.metadata) : '';
    let quickMetadata = '';
    if (image.data.metadata) {
        if (typeof image.data.metadata == 'string') {
            quickMetadata = image.data.metadata;
        }
        else {
            try {
                quickMetadata = JSON.stringify(image.data.metadata);
            }
            catch (e) {
                quickMetadata = '';
            }
        }
    }
    let metadataPreview = quickMetadata.length > 600 ? `${quickMetadata.substring(0, 600)}...` : quickMetadata;
    let description = image.data.name + (formattedMetadata ? `\n${formattedMetadata}` : (metadataPreview ? `\n${metadataPreview}` : ''));
    let name = image.data.name;
    let allowAnims = localStorage.getItem('image_history_allow_anims') != 'false';
    let allowAnimToggle = allowAnims ? '' : '&noanim=true';
    let forceImage = null, forcePreview = null;
    let extension = image.data.src.split('.').pop();
    if (extension == 'html') {
        forceImage = 'imgs/html.jpg';
        forcePreview = forceImage;
    }
    else if (['wav', 'mp3', 'aac', 'ogg', 'flac'].includes(extension)) {
        forcePreview = 'imgs/audio_placeholder.jpg';
    }
    let dragImage = forceImage ?? `${image.data.src}`;
    let imageSrc = forcePreview ?? `${image.data.src}?preview=true${allowAnimToggle}`;
    let searchable = getImageHistorySearchFields(image, parsedMeta);
    let detailMetadata = formattedMetadata ? formattedMetadata.replaceAll('<br>', '&emsp;') : escapeHtml(metadataPreview);
    let detail_list = [escapeHtml(image.data.name), detailMetadata];
    let aspectRatio = parsedMeta.sui_image_params?.width && parsedMeta.sui_image_params?.height ? parsedMeta.sui_image_params.width / parsedMeta.sui_image_params.height : null;
    let className = parsedMeta.is_starred ? 'image-block-starred' : '';
    if (parsedMeta.is_hidden) {
        className = `${className} image-block-hidden`.trim();
    }
    if (isSelected) {
        className = `${className} browser-entry-selected`.trim();
    }
    let checkbox = canBulkSelect ? {
        checked: isSelected,
        title: 'Select image',
        onchange: (checked, file, div) => {
            setImageHistorySelection(image.data.fullsrc, checked, div);
        }
    } : null;
    return { name, description, buttons, checkbox, 'image': imageSrc, 'dragimage': dragImage, className, searchable, display: name, detail_list, aspectRatio };
}

function selectOutputInHistory(image, div) {
    lastHistoryImage = image.data.src;
    lastHistoryImageDiv = div;
    let curImg = currentImageHelper.getCurrentImage();
    if (curImg && curImg.dataset.src == image.data.src) {
        curImg.dataset.batch_id = 'history';
        curImg.click();
        return;
    }
    if (image.data.name.endsWith('.html')) {
        window.open(image.data.src, '_blank');
    }
    else {
        if (!div.dataset.metadata) {
            div.dataset.metadata = image.data.metadata;
            div.dataset.src = image.data.src;
        }
        setCurrentImage(image.data.src, div.dataset.metadata, 'history');
    }
}

let imageHistoryBrowser = new GenPageBrowserClass('image_history', listOutputHistoryFolderAndFiles, 'imagehistorybrowser', 'Thumbnails', describeOutputFile, selectOutputInHistory,
    `<label for="image_history_sort_by">Sort:</label> <select id="image_history_sort_by"><option>Name</option><option>Date</option></select> <input type="checkbox" id="image_history_sort_reverse"> <label for="image_history_sort_reverse">Reverse</label> &emsp; <input type="checkbox" id="image_history_allow_anims" checked autocomplete="off"> <label for="image_history_allow_anims">Allow Animation</label> &emsp; <input type="checkbox" id="image_history_show_hidden" autocomplete="off"> <label for="image_history_show_hidden">Show Hidden</label> <span id="image_history_bulk_controls" class="image-history-bulk-controls"><span id="image_history_selected_count" class="image-history-selected-count">0 selected</span> <button type="button" id="image_history_select_all" class="refresh-button" onclick="selectAllImageHistory()">Select All</button> <button type="button" id="image_history_clear_selection" class="refresh-button" onclick="clearSelectedImageHistory()">Clear</button> <button type="button" id="image_history_compare_selected" class="refresh-button" onclick="compareSelectedImageHistory()">Compare</button> <button type="button" id="image_history_export_metadata_selected" class="refresh-button" onclick="exportSelectedImageHistoryMetadata()">Export Metadata</button> <button type="button" id="image_history_send_prompt_lab_selected" class="refresh-button" onclick="sendSelectedImageHistoryToPromptLab()">Send to Prompt Lab</button> <button type="button" id="image_history_star_selected" class="refresh-button" onclick="starSelectedImageHistory()">Star Selected</button> <button type="button" id="image_history_unstar_selected" class="refresh-button" onclick="unstarSelectedImageHistory()">Unstar Selected</button> <button type="button" id="image_history_hide_selected" class="refresh-button" onclick="hideSelectedImageHistory()">Hide Selected</button> <button type="button" id="image_history_unhide_selected" class="refresh-button" onclick="unhideSelectedImageHistory()">Unhide Selected</button> <button type="button" id="image_history_delete_selected" class="interrupt-button" onclick="deleteSelectedImageHistory()">Delete Selected</button></span> <span id="image_history_request_status" class="image-history-request-status" data-state="idle"><span id="image_history_request_status_text" class="image-history-request-status-text"></span> <button type="button" id="image_history_retry_button" class="refresh-button" style="display:none;">Retry</button></span>`);
imageHistoryBrowser.filterMatcher = imageHistoryFilterMatches;
imageHistoryBrowser.folderSelectedEvent = () => {
    clearImageHistorySelection();
};
imageHistoryBrowser.builtEvent = () => {
    updateImageHistoryFilterHint();
    ensureImageHistoryStatusReady();
    pruneImageHistorySelectionToCurrentFiles();
    updateImageHistoryBulkControls();
    imageHistoryWindowManager.attach(imageHistoryBrowser.contentDiv);
};

getRequiredElementById('imagehistorytabclickable').addEventListener('shown.bs.tab', () => {
    let historyContent = document.getElementById('imagehistorybrowser-content');
    if (historyContent) {
        browserUtil.queueMakeVisible(historyContent);
        imageHistoryWindowManager.attach(historyContent);
        imageHistoryWindowManager.queueUpdate();
    }
});

function storeImageToHistoryWithCurrentParams(img) {
    let data = getGenInput();
    data['image'] = img;
    delete data['initimage'];
    delete data['maskimage'];
    genericRequest('AddImageToHistory', data, res => {
        mainGenHandler.gotImageResult(res.images[0].image, res.images[0].metadata, '0');
    });
}
