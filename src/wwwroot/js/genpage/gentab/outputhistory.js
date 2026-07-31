let IMAGE_HISTORY_AUTO_RETRY_DELAY_MS = 1500;
let IMAGE_HISTORY_FAST_FIRST_LIMIT = 128;
let IMAGE_HISTORY_BACKGROUND_WATCHDOG_DELAY_MS = 10000;
let IMAGE_HISTORY_BACKGROUND_MAX_RETRIES = 2;
let IMAGE_HISTORY_SAVED_REFRESH_MAX_ATTEMPTS = 8;
let IMAGE_HISTORY_UNLOAD_ROW_BUFFER = 10;
let IMAGE_HISTORY_MIN_MEDIA_ROWS_TO_UNLOAD = 2;

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
        img.classList.remove('lazyload');
        img.removeAttribute('data-src');
        img.src = img.dataset.origSrc;
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
        return hydrateQueued;
    }
}

/** Header controls used by the image-history browser. */
let IMAGE_HISTORY_HEADER_HTML = `<label for="image_history_sort_by">Sort:</label> <select id="image_history_sort_by"><option>Name</option><option value="DateCreated">Date-Created</option><option value="DateEdited">Date-Edited</option><option>Rating</option><option>Resolution</option><option>Model</option><option>Seed</option><option value="FileSize">File Size</option></select> <input type="checkbox" id="image_history_sort_reverse"> <label for="image_history_sort_reverse">Reverse</label> &emsp; <input type="checkbox" id="image_history_allow_anims" checked autocomplete="off"> <label for="image_history_allow_anims">Allow Animation</label> &emsp; <input type="checkbox" id="image_history_show_hidden" autocomplete="off"> <label for="image_history_show_hidden">Show Hidden</label> &emsp; <input type="checkbox" id="image_history_hide_grids" checked autocomplete="off"> <label for="image_history_hide_grids">Hide Grids</label> <button type="button" id="image_history_rescan_metadata" class="refresh-button" onclick="rescanImageHistoryMetadata()">Rescan Metadata</button> <span id="image_history_bulk_controls" class="image-history-bulk-controls"><span id="image_history_selected_count" class="image-history-selected-count">0 selected</span> <button type="button" id="image_history_select_all" class="refresh-button" onclick="selectAllImageHistory()">Select All</button> <button type="button" id="image_history_clear_selection" class="refresh-button" onclick="clearSelectedImageHistory()">Clear</button> <button type="button" id="image_history_compare_selected" class="refresh-button" onclick="compareSelectedImageHistory()">Compare</button> <button type="button" id="image_history_copy_paths_selected" class="refresh-button" onclick="copySelectedImageHistoryPaths()">Copy Paths</button> <button type="button" id="image_history_contact_sheet_selected" class="refresh-button" onclick="createSelectedImageHistoryContactSheet()">Contact Sheet</button> <button type="button" id="image_history_set_rating_selected" class="refresh-button" onclick="setSelectedImageHistoryRatingPrompt()">Set Rating</button> <button type="button" id="image_history_add_tags_selected" class="refresh-button" onclick="setSelectedImageHistoryTagsPrompt('add')">Add Tags</button> <button type="button" id="image_history_remove_tags_selected" class="refresh-button" onclick="setSelectedImageHistoryTagsPrompt('remove')">Remove Tags</button> <button type="button" id="image_history_set_notes_selected" class="refresh-button" onclick="setSelectedImageHistoryNotesPrompt()">Set Notes</button> <button type="button" id="image_history_copy_to_selected" class="refresh-button" onclick="moveSelectedImageHistoryPrompt('copy')">Copy To</button> <button type="button" id="image_history_move_to_selected" class="refresh-button" onclick="moveSelectedImageHistoryPrompt('move')">Move To</button> <button type="button" id="image_history_export_metadata_selected" class="refresh-button" onclick="exportSelectedImageHistoryMetadata()">Export Metadata</button> <button type="button" id="image_history_send_prompt_lab_selected" class="refresh-button" onclick="sendSelectedImageHistoryToPromptLab()">Send to Prompt Lab</button> <button type="button" id="image_history_star_selected" class="refresh-button" onclick="starSelectedImageHistory()">Star Selected</button> <button type="button" id="image_history_unstar_selected" class="refresh-button" onclick="unstarSelectedImageHistory()">Unstar Selected</button> <button type="button" id="image_history_hide_selected" class="refresh-button" onclick="hideSelectedImageHistory()">Hide Selected</button> <button type="button" id="image_history_unhide_selected" class="refresh-button" onclick="unhideSelectedImageHistory()">Unhide Selected</button> <button type="button" id="image_history_delete_selected" class="interrupt-button" onclick="deleteSelectedImageHistory()">Delete Selected</button></span> <span id="image_history_request_status" class="image-history-request-status" data-state="idle"><span id="image_history_request_status_text" class="image-history-request-status-text"></span> <button type="button" id="image_history_retry_button" class="refresh-button" style="display:none;">Retry</button></span>`;

class ImageHistoryController {
    /** Creates image-history state without starting a browser request. */
    constructor() {
        this.browser = null;
        this.selected = new Set();
        this.bulkActionRunning = false;
        this.showHidden = localStorage.getItem('image_history_show_hidden') == null ? window.userFeatureToggles?.imageHistoryShowHiddenDefault == true : localStorage.getItem('image_history_show_hidden') == 'true';
        this.hideGrids = localStorage.getItem('image_history_hide_grids') == null ? true : localStorage.getItem('image_history_hide_grids') == 'true';
        this.refreshQueued = false;
        this.hasLoadedOnce = false;
        this.initialAutoRetryUsed = false;
        this.autoRetryTimer = null;
        this.nextLoadIsRetry = false;
        this.startupStage = 'pending';
        this.loadToken = 0;
        this.backgroundLoadToken = 0;
        this.backgroundRetryCount = 0;
        this.backgroundRequestKey = null;
        this.backgroundWatchdog = null;
        this.backgroundRequestInFlight = false;
        this.initialLoadScheduled = false;
        this.savedRefreshTimer = null;
        this.savedRefreshAttempts = 0;
        this.savedRefreshTargets = new Set();
        this.registeredMediaButtons = [];
        this.windowManager = new ImageHistoryWindowManager();
        this.filter = null;
        this.comparison = null;
        this.bulkActions = null;
    }

    /** Handles image-history register media button orchestration. */
    registerMediaButton(name, action, title = '', mediaTypes = null, isDefault = false, showInHistory = true, href = null, is_download = false, can_multi = false, multi_only = false, max_selected = null) {
        this.registeredMediaButtons.push({ name, action, title, mediaTypes, isDefault, showInHistory, href, is_download, can_multi, multi_only, max_selected });
    }

    /** Handles image-history get history image src orchestration. */
    getHistoryImageSrc(fullSrc) {
        let safePath = fullSrc.split('/').map(part => encodeURIComponent(part)).join('/');
        return `${getImageOutPrefix()}/${safePath}`;
    }

    /** Handles image-history request refresh orchestration. */
    requestRefresh() {
        if (!this.browser || this.refreshQueued) {
            return;
        }
        this.refreshQueued = true;
        let run = () => {
            this.refreshQueued = false;
            if (this.browser) {
                this.browser.lightRefresh();
            }
        };
        if (window.requestAnimationFrame) {
            requestAnimationFrame(run);
        }
        else {
            setTimeout(run, 0);
        }
    }

    /** Handles image-history has file orchestration. */
    hasFile(fullSrc) {
        if (!this.browser?.lastFiles) {
            return false;
        }
        for (let file of this.browser.lastFiles) {
            if (!file) {
                continue;
            }
            if (file.name == fullSrc) {
                return true;
            }
            let fileSrc = file.data?.fullsrc || file.data?.src || file.name;
            if (typeof getImageFullSrc == 'function' && getImageFullSrc(fileSrc) == fullSrc) {
                return true;
            }
        }
        return false;
    }

    /** Handles image-history can include path orchestration. */
    canIncludePath(fullSrc) {
        if (!this.browser?.lastListCache) {
            return false;
        }
        let folder = this.browser.folder || '';
        let prefix = folder == '' ? '' : `${folder.replace(/\/+$/, '')}/`;
        if (prefix && !fullSrc.startsWith(prefix)) {
            return false;
        }
        let relative = prefix ? fullSrc.substring(prefix.length) : fullSrc;
        if (!relative || relative.startsWith('/')) {
            return false;
        }
        let slashCount = relative.split('/').length - 1;
        return slashCount <= Number.parseInt(this.browser.depth || 0);
    }

    /** Handles image-history add folders for path orchestration. */
    addFoldersForPath(folders, fullSrc) {
        let folder = this.browser.folder || '';
        let prefix = folder == '' ? '' : `${folder.replace(/\/+$/, '')}/`;
        let relative = prefix ? fullSrc.substring(prefix.length) : fullSrc;
        if (!relative.includes('/')) {
            return folders;
        }
        let copy = [...folders];
        let existing = new Set(copy);
        let parts = relative.split('/');
        let current = '';
        for (let i = 0; i < parts.length - 1; i++) {
            current = current ? `${current}/${parts[i]}` : parts[i];
            if (!existing.has(current)) {
                existing.add(current);
                copy.push(current);
            }
        }
        return copy.sort((a, b) => b.toLowerCase().localeCompare(a.toLowerCase()));
    }

    /** Handles image-history try add saved image orchestration. */
    tryAddSavedImage(savedPath, metadata = null) {
        if (!this.browser?.lastListCache || !savedPath) {
            return false;
        }
        let fullSrc = typeof getImageFullSrc == 'function' ? getImageFullSrc(savedPath) : savedPath;
        if (!fullSrc || this.hasFile(fullSrc) || !this.canIncludePath(fullSrc)) {
            return false;
        }
        let sortBy = this.normalizeSortBy(localStorage.getItem('image_history_sort_by') ?? window.userFeatureToggles?.imageHistoryDefaultSort ?? 'Name');
        if (sortBy == 'FileSize') {
            return false;
        }
        let folder = this.browser.folder || '';
        let prefix = folder == '' ? '' : `${folder.replace(/\/+$/, '')}/`;
        let relativeName = prefix ? fullSrc.substring(prefix.length) : fullSrc;
        let mappedFile = this.mapFiles(prefix, [{
            src: relativeName,
            metadata: metadata || '{}',
            file_size: 0,
            file_time: Math.floor(Date.now() / 1000),
            file_created_time: Math.floor(Date.now() / 1000)
        }])[0];
        let reverse = localStorage.getItem('image_history_sort_reverse') == 'true';
        let files = this.browser.lastFiles ? [...this.browser.lastFiles, mappedFile] : [mappedFile];
        files = this.sortFilesForDisplay(files, sortBy, reverse);
        let folders = this.addFoldersForPath(this.browser.lastListCache.folders || [], fullSrc);
        this.replaceBrowserContents(folder, folders, files);
        return this.hasFile(fullSrc);
    }

    /** Handles image-history schedule saved refresh orchestration. */
    scheduleSavedRefresh(delayMs) {
        if (this.savedRefreshTimer) {
            return;
        }
        this.savedRefreshTimer = setTimeout(() => {
            this.savedRefreshTimer = null;
            if (this.savedRefreshAttempts > 0) {
                for (let target of [...this.savedRefreshTargets]) {
                    if (this.hasFile(target)) {
                        this.savedRefreshTargets.delete(target);
                    }
                }
            }
            if (this.savedRefreshTargets.size == 0) {
                this.savedRefreshAttempts = 0;
                return;
            }
            if (!this.browser) {
                this.savedRefreshTargets.clear();
                this.savedRefreshAttempts = 0;
                return;
            }
            this.savedRefreshAttempts++;
            this.requestRefresh();
            if (this.savedRefreshAttempts < IMAGE_HISTORY_SAVED_REFRESH_MAX_ATTEMPTS) {
                this.scheduleSavedRefresh(this.savedRefreshAttempts < 4 ? 250 : 600);
            }
            else {
                this.savedRefreshTargets.clear();
                this.savedRefreshAttempts = 0;
            }
        }, delayMs);
    }

    /** Handles image-history notify saved path orchestration. */
    notifySavedPath(savedPath, metadata = null) {
        if (!savedPath || savedPath.startsWith('data:') || savedPath.startsWith('DOPLACEHOLDER:')) {
            return;
        }
        if (!this.browser) {
            return;
        }
        let expected = typeof getImageFullSrc == 'function' ? getImageFullSrc(savedPath) : savedPath;
        if (!expected) {
            return;
        }
        this.savedRefreshTargets.add(expected);
        this.tryAddSavedImage(savedPath, metadata);
        this.savedRefreshAttempts = 0;
        this.scheduleSavedRefresh(100);
    }

    /** Handles image-history rescan metadata orchestration. */
    rescanMetadata(rebuild = true) {
        if (!this.browser || this.bulkActionRunning) {
            return;
        }
        this.bulkActionRunning = true;
        let folder = this.browser.folder || '';
        this.setRequestStatus('loading', 'Rescanning metadata...');
        genericRequest('RescanImageMetadata', { path: folder, rebuild }, data => {
            this.bulkActionRunning = false;
            this.setRequestStatus('idle', `Rescanned ${data.indexed} files.`);
            this.browser.lightRefresh();
        }, 0, e => {
            this.bulkActionRunning = false;
            this.setRequestStatus('error', `${e}`);
            showError(e);
        });
    }

    /** Handles image-history clear auto retry orchestration. */
    clearAutoRetry() {
        if (this.autoRetryTimer) {
            clearTimeout(this.autoRetryTimer);
            this.autoRetryTimer = null;
        }
        this.nextLoadIsRetry = false;
    }

    /** Handles image-history clear background watchdog orchestration. */
    clearBackgroundWatchdog() {
        if (this.backgroundWatchdog) {
            clearTimeout(this.backgroundWatchdog);
            this.backgroundWatchdog = null;
        }
    }

    /** Handles image-history cancel background load orchestration. */
    cancelBackgroundLoad() {
        this.backgroundLoadToken++;
        this.backgroundRequestInFlight = false;
        this.clearBackgroundWatchdog();
    }

    /** Handles image-history get request key orchestration. */
    getRequestKey(path, depth, sortBy, reverse, showHidden, hideGrids) {
        return JSON.stringify({ path, depth, sortBy, reverse, showHidden, hideGrids });
    }

    /** Handles image-history is background request relevant orchestration. */
    isBackgroundRequestRelevant(path, requestKey, backgroundToken) {
        if (!this.browser || this.browser.folder != path) {
            return false;
        }
        if (backgroundToken != this.backgroundLoadToken) {
            return false;
        }
        return requestKey == this.backgroundRequestKey;
    }

    /** Handles image-history schedule background watchdog orchestration. */
    scheduleBackgroundWatchdog(path, depth, sortBy, reverse, showHidden, hideGrids, requestKey, backgroundToken) {
        this.clearBackgroundWatchdog();
        this.backgroundWatchdog = setTimeout(() => {
            if (!this.isBackgroundRequestRelevant(path, requestKey, backgroundToken) || this.startupStage != 'recent_loaded') {
                return;
            }
            if (this.backgroundRequestInFlight) {
                this.setRequestStatus('loading', 'Still loading older history...');
                this.scheduleBackgroundWatchdog(path, depth, sortBy, reverse, showHidden, hideGrids, requestKey, backgroundToken);
                return;
            }
            if (this.backgroundRetryCount >= IMAGE_HISTORY_BACKGROUND_MAX_RETRIES) {
                this.setRequestStatus('error', 'History is taking too long to fully load. Retry when ready.');
                return;
            }
            this.backgroundRetryCount++;
            this.setRequestStatus('loading', 'Still loading older history...');
            this.queueFullLoad(path, depth, sortBy, reverse, showHidden, hideGrids);
        }, IMAGE_HISTORY_BACKGROUND_WATCHDOG_DELAY_MS);
    }

    /** Handles image-history retry manually orchestration. */
    retryManually() {
        this.clearAutoRetry();
        if (this.browser) {
            this.browser.lightRefresh();
        }
    }

    /** Handles image-history ensure status ready orchestration. */
    ensureStatusReady() {
        let statusElem = document.getElementById('image_history_request_status');
        if (!statusElem || statusElem.dataset.ready) {
            return;
        }
        statusElem.dataset.ready = 'true';
        getRequiredElementById('image_history_retry_button').addEventListener('click', () => {
            this.retryManually();
        });
    }

    /** Handles image-history apply feature toggles orchestration. */
    applyFeatureToggles() {
        let advancedEnabled = window.userFeatureToggles?.imageHistoryAdvancedTools != false;
        let compareEnabled = window.userFeatureToggles?.imageHistoryCompare != false;
        let advancedButtons = ['image_history_contact_sheet_selected', 'image_history_set_rating_selected', 'image_history_add_tags_selected', 'image_history_remove_tags_selected', 'image_history_set_notes_selected', 'image_history_copy_to_selected', 'image_history_move_to_selected', 'image_history_export_metadata_selected', 'image_history_send_prompt_lab_selected', 'image_history_rescan_metadata'];
        for (let id of advancedButtons) {
            let elem = document.getElementById(id);
            if (elem) {
                elem.style.display = advancedEnabled ? '' : 'none';
            }
        }
        let compareButton = document.getElementById('image_history_compare_selected');
        if (compareButton) {
            compareButton.style.display = compareEnabled ? '' : 'none';
        }
        let sendPromptLabButton = document.getElementById('image_history_send_prompt_lab_selected');
        if (sendPromptLabButton && window.userFeatureToggles?.promptLab == false) {
            sendPromptLabButton.style.display = 'none';
        }
    }

    /** Handles image-history set request status orchestration. */
    setRequestStatus(state, message = '') {
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

    /** Handles image-history ensure browser shell ready orchestration. */
    ensureBrowserShellReady() {
        if (!this.browser) {
            return;
        }
        this.browser.ensureBuilt();
        this.ensureStatusReady();
    }

    /** Handles image-history schedule initial load orchestration. */
    scheduleInitialLoad(delayMs = 0) {
        if (!this.browser || this.hasLoadedOnce || this.initialLoadScheduled) {
            return;
        }
        this.initialLoadScheduled = true;
        setTimeout(() => {
            this.initialLoadScheduled = false;
            if (!this.browser || this.hasLoadedOnce) {
                return;
            }
            this.ensureBrowserShellReady();
            this.browser.navigate('');
        }, delayMs);
    }

    /** Handles image-history ensure header controls ready orchestration. */
    ensureHeaderControlsReady(sortBy, reverse, allowAnims, showHidden, hideGrids) {
        this.ensureBrowserShellReady();
        let sortElem = document.getElementById('image_history_sort_by');
        let sortReverseElem = document.getElementById('image_history_sort_reverse');
        let allowAnimsElem = document.getElementById('image_history_allow_anims');
        let showHiddenElem = document.getElementById('image_history_show_hidden');
        let hideGridsElem = document.getElementById('image_history_hide_grids');
        if (!sortElem || !sortReverseElem || !allowAnimsElem || !showHiddenElem || !hideGridsElem) {
            return null;
        }
        let normalizedSortBy = this.normalizeSortBy(sortBy);
        if (normalizedSortBy != sortBy) {
            localStorage.setItem('image_history_sort_by', normalizedSortBy);
            sortBy = normalizedSortBy;
        }
        sortElem.value = sortBy;
        if (!sortElem.value) {
            sortElem.value = 'Name';
            localStorage.setItem('image_history_sort_by', 'Name');
        }
        sortReverseElem.checked = reverse;
        allowAnimsElem.checked = allowAnims;
        showHiddenElem.checked = showHidden;
        hideGridsElem.checked = hideGrids;
        if (!sortElem.dataset.ready) {
            sortElem.dataset.ready = 'true';
            sortElem.addEventListener('change', () => {
                localStorage.setItem('image_history_sort_by', sortElem.value);
                this.requestRefresh();
            });
            sortReverseElem.addEventListener('change', () => {
                localStorage.setItem('image_history_sort_reverse', sortReverseElem.checked);
                this.requestRefresh();
            });
            allowAnimsElem.addEventListener('change', () => {
                localStorage.setItem('image_history_allow_anims', allowAnimsElem.checked);
                this.requestRefresh();
            });
            showHiddenElem.addEventListener('change', () => {
                this.showHidden = showHiddenElem.checked;
                localStorage.setItem('image_history_show_hidden', showHiddenElem.checked);
                this.requestRefresh();
            });
            hideGridsElem.addEventListener('change', () => {
                this.hideGrids = hideGridsElem.checked;
                localStorage.setItem('image_history_hide_grids', hideGridsElem.checked);
                this.requestRefresh();
            });
        }
        this.ensureBulkControlsReady();
        return { sortElem, sortReverseElem, allowAnimsElem, showHiddenElem, hideGridsElem };
    }

    /** Handles image-history schedule auto retry orchestration. */
    scheduleAutoRetry() {
        if (this.hasLoadedOnce || this.initialAutoRetryUsed || this.autoRetryTimer) {
            return false;
        }
        this.initialAutoRetryUsed = true;
        this.autoRetryTimer = setTimeout(() => {
            this.autoRetryTimer = null;
            this.nextLoadIsRetry = true;
            this.setRequestStatus('retrying', 'Retrying history load...');
            if (this.browser) {
                this.browser.lightRefresh();
            }
        }, IMAGE_HISTORY_AUTO_RETRY_DELAY_MS);
        return true;
    }

    /** Handles image-history order files for display orchestration. */
    orderFilesForDisplay(files) {
        function isPreSortFile(file) {
            return file.src == 'index.html';
        }
        let preFiles = files.filter(file => isPreSortFile(file));
        let postFiles = files.filter(file => !isPreSortFile(file));
        return preFiles.concat(postFiles);
    }

    /** Handles image-history map files orchestration. */
    mapFiles(prefix, files) {
        return files.map(file => {
            let fullSrc = `${prefix}${file.src}`;
            return { 'name': fullSrc, 'data': { 'src': this.getHistoryImageSrc(fullSrc), 'fullsrc': fullSrc, 'name': file.src, 'metadata': file.metadata, 'file_size': file.file_size || 0, 'file_time': file.file_time || 0, 'file_created_time': file.file_created_time || 0 } };
        });
    }

    /** Handles image-history normalize sort by orchestration. */
    normalizeSortBy(sortBy) {
        return sortBy == 'Date' ? 'DateEdited' : sortBy;
    }

    /** Handles image-history sort supported by server orchestration. */
    sortSupportedByServer(sortBy) {
        return sortBy == 'Name' || sortBy == 'DateCreated' || sortBy == 'DateEdited';
    }

    /** Handles image-history get sort number orchestration. */
    getSortNumber(file, sortBy) {
        let metadata = this.filter.parseMetadata(file?.data?.metadata);
        let params = metadata.sui_image_params || {};
        let extra = metadata.sui_extra_data || {};
        if (sortBy == 'Rating') {
            return Number.parseFloat(metadata.rating || extra.rating || 0) || 0;
        }
        if (sortBy == 'Resolution') {
            let width = Number.parseInt(extra.final_width || params.width || 0) || 0;
            let height = Number.parseInt(extra.final_height || params.height || 0) || 0;
            return width * height;
        }
        if (sortBy == 'Seed') {
            return Number.parseFloat(params.seed || 0) || 0;
        }
        if (sortBy == 'FileSize') {
            return Number.parseInt(file?.data?.file_size || 0) || 0;
        }
        return 0;
    }

    /** Handles image-history get sort text orchestration. */
    getSortText(file, sortBy) {
        let metadata = this.filter.parseMetadata(file?.data?.metadata);
        let params = metadata.sui_image_params || {};
        if (sortBy == 'Model') {
            return `${params.model || ''}`.toLowerCase();
        }
        return '';
    }

    /** Handles image-history apply client sort orchestration. */
    applyClientSort(files, sortBy, reverse) {
        if (this.sortSupportedByServer(sortBy)) {
            return files;
        }
        files.sort((a, b) => {
            if (sortBy == 'Model') {
                return this.getSortText(b, sortBy).localeCompare(this.getSortText(a, sortBy));
            }
            return this.getSortNumber(b, sortBy) - this.getSortNumber(a, sortBy);
        });
        if (reverse) {
            files.reverse();
        }
        return files;
    }

    /** Handles image-history sort files for display orchestration. */
    sortFilesForDisplay(files, sortBy, reverse) {
        if (sortBy == 'Name') {
            files.sort((a, b) => b.name.localeCompare(a.name));
            if (reverse) {
                files.reverse();
            }
            return files;
        }
        if (sortBy == 'Date' || sortBy == 'DateEdited') {
            files.sort((a, b) => (b.data?.file_time || 0) - (a.data?.file_time || 0));
            if (reverse) {
                files.reverse();
            }
            return files;
        }
        if (sortBy == 'DateCreated') {
            files.sort((a, b) => (b.data?.file_created_time || 0) - (a.data?.file_created_time || 0));
            if (reverse) {
                files.reverse();
            }
            return files;
        }
        return this.applyClientSort(files, sortBy, reverse);
    }

    /** Handles image-history perf text orchestration. */
    perfText(perf) {
        if (!perf) {
            return '';
        }
        return `, server=${Number(perf.total_ms || 0).toFixed(1)}ms (dirs=${Number(perf.dir_scan_ms || 0).toFixed(1)}ms, files=${Number(perf.file_scan_ms || 0).toFixed(1)}ms, sort=${Number(perf.final_sort_ms || 0).toFixed(1)}ms)`;
    }

    /** Handles image-history is grid folder orchestration. */
    isGridFolder(path) {
        let cleanPath = (path || '').replaceAll('\\', '/').replace(/^\/+|\/+$/g, '').toLowerCase();
        return cleanPath == 'grids' || cleanPath.startsWith('grids/');
    }

    /** Handles image-history filter grid files orchestration. */
    filterGridFiles(files, path, hideGrids) {
        if (!hideGrids || this.isGridFolder(path)) {
            return files;
        }
        return files.filter(file => !file.data?.fullsrc?.replaceAll('\\', '/').toLowerCase().startsWith('grids/'));
    }

    /** Handles image-history replace browser contents orchestration. */
    replaceBrowserContents(path, folders, mapped) {
        if (!this.browser) {
            return;
        }
        this.browser.lastListCache = { folder: path, folders, files: mapped, filter: this.browser.filterServerSide ? this.browser.filter : '' };
        this.browser.build(path, folders, mapped);
    }

    /** Handles image-history set metadata bool value orchestration. */
    setMetadataBoolValue(metadata, key, value) {
        return this.setMetadataValue(metadata, key, value);
    }

    /** Handles image-history set metadata value orchestration. */
    setMetadataValue(metadata, key, value) {
        if (!metadata) {
            return JSON.stringify({ [key]: value });
        }
        try {
            let parsed = { ...this.filter.parseMetadata(metadata) };
            parsed[key] = value;
            return JSON.stringify(parsed);
        }
        catch (e) {
            return metadata;
        }
    }

    /** Handles image-history get file orchestration. */
    getFile(path) {
        return this.browser?.lastFilesMap?.get(path) || null;
    }

    /** Handles image-history get entries orchestration. */
    getEntries() {
        let historySection = document.getElementById('imagehistorybrowser-content');
        if (!historySection) {
            return [];
        }
        return Array.from(historySection.children).filter(c => c.dataset?.name);
    }

    /** Handles image-history prune selection orchestration. */
    pruneSelection() {
        if (!this.browser?.lastFiles) {
            return;
        }
        let currentFiles = new Set(this.browser.lastFiles.map(f => f.name));
        for (let path of this.selected) {
            if (!currentFiles.has(path)) {
                this.selected.delete(path);
            }
        }
    }

    /** Handles image-history get checked paths orchestration. */
    getCheckedPaths() {
        let selected = [];
        for (let entry of this.getEntries()) {
            let checkbox = entry.querySelector('.browser-entry-checkbox');
            if (checkbox?.checked && entry.dataset?.name) {
                selected.push(entry.dataset.name);
            }
        }
        return selected;
    }

    /** Synchronizes the controller selection from history checkboxes. */
    syncSelectionFromDOM() {
        let checkedPaths = this.getCheckedPaths();
        if (checkedPaths.length == 0) {
            if (this.selected.size == 0) {
                return;
            }
            this.selected.clear();
            for (let entry of this.getEntries()) {
                entry.classList.remove('browser-entry-selected');
            }
            return;
        }
        this.selected = new Set(checkedPaths);
        for (let entry of this.getEntries()) {
            entry.classList.toggle('browser-entry-selected', this.selected.has(entry.dataset.name));
        }
    }

    /** Handles image-history update bulk controls orchestration. */
    updateBulkControls() {
        this.syncSelectionFromDOM();
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
        let count = this.selected.size;
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
        let copyPathsButton = document.getElementById('image_history_copy_paths_selected');
        let contactSheetButton = document.getElementById('image_history_contact_sheet_selected');
        let ratingButton = document.getElementById('image_history_set_rating_selected');
        let addTagsButton = document.getElementById('image_history_add_tags_selected');
        let removeTagsButton = document.getElementById('image_history_remove_tags_selected');
        let notesButton = document.getElementById('image_history_set_notes_selected');
        let copyToButton = document.getElementById('image_history_copy_to_selected');
        let moveToButton = document.getElementById('image_history_move_to_selected');
        let starButton = document.getElementById('image_history_star_selected');
        let unstarButton = document.getElementById('image_history_unstar_selected');
        let anyEntries = this.getEntries().length > 0;
        if (selectAllButton) {
            selectAllButton.disabled = !anyEntries || this.bulkActionRunning;
        }
        if (clearButton) {
            clearButton.disabled = count == 0 || this.bulkActionRunning;
        }
        if (hideButton) {
            hideButton.style.display = canHide ? '' : 'none';
            hideButton.disabled = count == 0 || this.bulkActionRunning;
        }
        if (unhideButton) {
            unhideButton.style.display = canHide ? '' : 'none';
            unhideButton.disabled = count == 0 || this.bulkActionRunning;
        }
        if (deleteButton) {
            deleteButton.style.display = canDelete ? '' : 'none';
            deleteButton.disabled = count == 0 || this.bulkActionRunning;
        }
        if (compareButton) {
            compareButton.disabled = count < 2 || this.bulkActionRunning;
            compareButton.innerText = count > 2 ? 'Compare First Two' : 'Compare';
        }
        if (exportMetadataButton) {
            exportMetadataButton.disabled = count == 0 || this.bulkActionRunning;
        }
        if (sendPromptLabButton) {
            sendPromptLabButton.disabled = count == 0 || this.bulkActionRunning;
        }
        if (copyPathsButton) {
            copyPathsButton.disabled = count == 0 || this.bulkActionRunning;
        }
        if (contactSheetButton) {
            contactSheetButton.disabled = count == 0 || this.bulkActionRunning;
        }
        if (ratingButton) {
            ratingButton.disabled = count == 0 || this.bulkActionRunning;
        }
        if (addTagsButton) {
            addTagsButton.disabled = count == 0 || this.bulkActionRunning;
        }
        if (removeTagsButton) {
            removeTagsButton.disabled = count == 0 || this.bulkActionRunning;
        }
        if (notesButton) {
            notesButton.disabled = count == 0 || this.bulkActionRunning;
        }
        if (copyToButton) {
            copyToButton.disabled = count == 0 || this.bulkActionRunning;
        }
        if (moveToButton) {
            moveToButton.disabled = count == 0 || this.bulkActionRunning;
        }
        if (starButton) {
            starButton.style.display = canStar ? '' : 'none';
            starButton.disabled = count == 0 || this.bulkActionRunning;
        }
        if (unstarButton) {
            unstarButton.style.display = canStar ? '' : 'none';
            unstarButton.disabled = count == 0 || this.bulkActionRunning;
        }
    }

    /** Handles image-history set selection orchestration. */
    setSelection(fullsrc, isSelected, entry = null) {
        if (isSelected) {
            this.selected.add(fullsrc);
        }
        else {
            this.selected.delete(fullsrc);
        }
        if (!entry) {
            entry = this.getEntries().find(e => e.dataset.name == fullsrc);
        }
        if (entry) {
            entry.classList.toggle('browser-entry-selected', isSelected);
            let checkbox = entry.querySelector('.browser-entry-checkbox');
            if (checkbox) {
                checkbox.checked = isSelected;
            }
        }
        this.updateBulkControls();
    }

    /** Handles image-history clear selection orchestration. */
    clearSelection() {
        this.selected.clear();
        for (let entry of this.getEntries()) {
            entry.classList.remove('browser-entry-selected');
            let checkbox = entry.querySelector('.browser-entry-checkbox');
            if (checkbox) {
                checkbox.checked = false;
            }
        }
        this.updateBulkControls();
    }

    /** Handles image-history select all orchestration. */
    selectAll() {
        for (let entry of this.getEntries()) {
            this.setSelection(entry.dataset.name, true, entry);
        }
        this.updateBulkControls();
    }

    /** Handles image-history compare selected orchestration. */
    compareSelected() {
        if (window.userFeatureToggles?.imageHistoryCompare == false) {
            return;
        }
        this.comparison.show([...this.selected].slice(0, 2));
    }

    /** Synchronizes selection and returns the selected history paths. */
    getSelectedPaths() {
        this.syncSelectionFromDOM();
        return [...this.selected];
    }

    /** Returns whether an image-history bulk action is running. */
    isBusy() {
        return this.bulkActionRunning;
    }

    /** Updates the image-history busy state and its controls. */
    setBusy(value) {
        this.bulkActionRunning = value;
        this.updateBulkControls();
    }

    /** Handles image-history ensure bulk controls ready orchestration. */
    ensureBulkControlsReady() {
        let controls = document.getElementById('image_history_bulk_controls');
        if (!controls || controls.dataset.ready) {
            this.updateBulkControls();
            return;
        }
        controls.dataset.ready = 'true';
        getRequiredElementById('image_history_select_all').onclick = (e) => {
            e.preventDefault();
            this.selectAll();
        };
        getRequiredElementById('image_history_clear_selection').onclick = (e) => {
            e.preventDefault();
            this.clearSelection();
        };
        getRequiredElementById('image_history_hide_selected').onclick = (e) => {
            e.preventDefault();
            this.bulkActions.hideSelected();
        };
        getRequiredElementById('image_history_unhide_selected').onclick = (e) => {
            e.preventDefault();
            this.bulkActions.unhideSelected();
        };
        getRequiredElementById('image_history_delete_selected').onclick = (e) => {
            e.preventDefault();
            this.bulkActions.deleteSelected();
        };
        getRequiredElementById('image_history_star_selected').onclick = (e) => {
            e.preventDefault();
            this.bulkActions.starSelected();
        };
        getRequiredElementById('image_history_unstar_selected').onclick = (e) => {
            e.preventDefault();
            this.bulkActions.unstarSelected();
        };
        getRequiredElementById('image_history_compare_selected').onclick = (e) => {
            e.preventDefault();
            this.compareSelected();
        };
        getRequiredElementById('image_history_export_metadata_selected').onclick = (e) => {
            e.preventDefault();
            this.bulkActions.exportMetadata();
        };
        getRequiredElementById('image_history_send_prompt_lab_selected').onclick = (e) => {
            e.preventDefault();
            this.bulkActions.sendToPromptLab();
        };
        getRequiredElementById('image_history_copy_paths_selected').onclick = (e) => {
            e.preventDefault();
            this.bulkActions.copyPaths();
        };
        getRequiredElementById('image_history_contact_sheet_selected').onclick = (e) => {
            e.preventDefault();
            this.bulkActions.createContactSheet();
        };
        getRequiredElementById('image_history_set_rating_selected').onclick = (e) => {
            e.preventDefault();
            this.bulkActions.promptRating();
        };
        getRequiredElementById('image_history_add_tags_selected').onclick = (e) => {
            e.preventDefault();
            this.bulkActions.promptTags('add');
        };
        getRequiredElementById('image_history_remove_tags_selected').onclick = (e) => {
            e.preventDefault();
            this.bulkActions.promptTags('remove');
        };
        getRequiredElementById('image_history_set_notes_selected').onclick = (e) => {
            e.preventDefault();
            this.bulkActions.promptNotes();
        };
        getRequiredElementById('image_history_copy_to_selected').onclick = (e) => {
            e.preventDefault();
            this.bulkActions.promptMove('copy');
        };
        getRequiredElementById('image_history_move_to_selected').onclick = (e) => {
            e.preventDefault();
            this.bulkActions.promptMove('move');
        };
        this.updateBulkControls();
    }

    /** Removes one image from the rendered history and current-image UI. */
    removeImageFromUI(fullsrc, src, explicitEntry = null) {
        this.selected.delete(fullsrc);
        let historySection = document.getElementById('imagehistorybrowser-content');
        if (historySection) {
            let entry = explicitEntry || this.getEntries().find(e => e.dataset.name == fullsrc || e.dataset.name == src);
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
        this.updateBulkControls();
    }

    /** Handles image-history delete single orchestration. */
    deleteSingle(fullsrc, src, explicitEntry = null, errorHandle = null) {
        return new Promise(resolve => {
            let onSuccess = () => {
                this.removeImageFromUI(fullsrc, src, explicitEntry);
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

    /** Handles image-history toggle hidden orchestration. */
    toggleHidden(path, rawSrc, refreshAfter = true, errorHandle = null) {
        return new Promise(resolve => {
            genericRequest('ToggleImageHidden', { 'path': path }, data => {
                let setHidden = metadata => this.setMetadataBoolValue(metadata, 'is_hidden', data.new_state);
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
                if (this.browser) {
                    let file = this.browser.getFileFor(path);
                    if (file?.data) {
                        file.data.metadata = setHidden(file.data.metadata ?? '{}');
                    }
                    if (refreshAfter) {
                        this.requestRefresh();
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

    /** Updates all rendered cards for a changed starred state. */
    updateStarredCards(src, starred) {
        forEachSwarmImageCardForSrc(src, card => {
            if (card.setStarred) {
                card.setStarred(starred);
            }
            else {
                card.classList.toggle('image-block-starred', starred);
            }
        });
    }

    /** Handles image-history list folder and files orchestration. */
    listFolderAndFiles(path, isRefresh, callback, depth, onError = null) {
        this.ensureBrowserShellReady();
        let requestStart = performance.now();
        let sortBy = this.normalizeSortBy(localStorage.getItem('image_history_sort_by') ?? window.userFeatureToggles?.imageHistoryDefaultSort ?? 'Name');
        let reverse = localStorage.getItem('image_history_sort_reverse') == 'true';
        let allowAnims = localStorage.getItem('image_history_allow_anims') != 'false';
        let showHidden = this.showHidden;
        let hideGrids = this.hideGrids;
        let filter = this.browser?.filter || '';
        let controlElems = this.ensureHeaderControlsReady(sortBy, reverse, allowAnims, showHidden, hideGrids);
        if (controlElems) {
            sortBy = controlElems.sortElem.value;
            reverse = controlElems.sortReverseElem.checked;
            allowAnims = controlElems.allowAnimsElem.checked;
            showHidden = controlElems.showHiddenElem.checked;
            hideGrids = controlElems.hideGridsElem.checked;
            this.showHidden = showHidden;
            this.hideGrids = hideGrids;
        }
        let isRetryLoad = this.nextLoadIsRetry;
        this.nextLoadIsRetry = false;
        let loadToken = ++this.loadToken;
        let useFastFirst = this.startupStage == 'pending' && path == '' && !isRefresh && !filter;
        if (!useFastFirst) {
            this.cancelBackgroundLoad();
        }
        if (useFastFirst || path != '' || isRefresh) {
            this.clearBackgroundWatchdog();
        }
        let serverSortBy = this.sortSupportedByServer(sortBy) ? sortBy : 'DateEdited';
        let serverReverse = this.sortSupportedByServer(sortBy) ? reverse : false;
        let request = { 'path': path, 'depth': depth, 'sortBy': serverSortBy, 'sortReverse': serverReverse, 'includeHidden': showHidden };
        if (filter) {
            request.filter = filter;
        }
        if (isRefresh) {
            request.forceScan = true;
        }
        if (useFastFirst) {
            request.fastFirst = true;
            request.fastFirstLimit = IMAGE_HISTORY_FAST_FIRST_LIMIT;
        }
        this.setRequestStatus(isRetryLoad ? 'retrying' : 'loading', isRetryLoad ? 'Retrying history load...' : 'Loading history...');
        genericRequest('ListImages', request, data => {
            if (loadToken != this.loadToken) {
                return;
            }
            let responseMs = performance.now() - requestStart;
            let mapStart = performance.now();
            this.clearAutoRetry();
            this.hasLoadedOnce = true;
            let prefix = path == '' ? '' : (path.endsWith('/') ? path : `${path}/`);
            let folders = data.folders.sort((a, b) => b.toLowerCase().localeCompare(a.toLowerCase()));
            let mapped = this.filterGridFiles(this.applyClientSort(this.mapFiles(prefix, this.orderFilesForDisplay(data.files)), sortBy, reverse), path, hideGrids);
            let mapMs = performance.now() - mapStart;
            let renderStart = performance.now();
            callback(folders, mapped);
            let renderMs = performance.now() - renderStart;
            console.debug(`History load: path='${path || '/'}', refresh=${isRefresh}, fastFirst=${useFastFirst}, folders=${folders.length}, files=${mapped.length}, request=${responseMs.toFixed(1)}ms, map=${mapMs.toFixed(1)}ms, render=${renderMs.toFixed(1)}ms${this.perfText(data.perf)}`);
            if (useFastFirst) {
                this.startupStage = 'recent_loaded';
                this.backgroundRetryCount = 0;
                this.backgroundRequestKey = this.getRequestKey(path, depth, sortBy, reverse, showHidden, hideGrids);
                this.queueFullLoad(path, depth, sortBy, reverse, showHidden, hideGrids);
                return;
            }
            this.startupStage = 'complete';
            this.clearBackgroundWatchdog();
            this.setRequestStatus('idle');
        }, 0, error => {
            if (loadToken != this.loadToken) {
                return;
            }
            showError(error);
            let shouldRetry = !isRetryLoad && this.scheduleAutoRetry();
            let errorMessage = `History failed to load: ${error}`;
            if (shouldRetry) {
                errorMessage += ' Retrying once...';
            }
            this.setRequestStatus('error', errorMessage);
            if (onError) {
                onError(error);
            }
        }, 300000);
    }

    /** Handles image-history queue full load orchestration. */
    queueFullLoad(path, depth, sortBy, reverse, showHidden, hideGrids) {
        sortBy = this.normalizeSortBy(sortBy);
        let requestKey = this.getRequestKey(path, depth, sortBy, reverse, showHidden, hideGrids);
        let serverSortBy = this.sortSupportedByServer(sortBy) ? sortBy : 'DateEdited';
        let serverReverse = this.sortSupportedByServer(sortBy) ? reverse : false;
        let backgroundToken = ++this.backgroundLoadToken;
        this.backgroundRequestKey = requestKey;
        this.backgroundRequestInFlight = true;
        this.scheduleBackgroundWatchdog(path, depth, sortBy, reverse, showHidden, hideGrids, requestKey, backgroundToken);
        setTimeout(() => {
            let requestStart = performance.now();
            genericRequest('ListImages', { 'path': path, 'depth': depth, 'sortBy': serverSortBy, 'sortReverse': serverReverse, 'includeHidden': showHidden }, data => {
                if (!this.isBackgroundRequestRelevant(path, requestKey, backgroundToken)) {
                    return;
                }
                let responseMs = performance.now() - requestStart;
                let mapStart = performance.now();
                this.backgroundRequestInFlight = false;
                let prefix = path == '' ? '' : (path.endsWith('/') ? path : `${path}/`);
                let folders = data.folders.sort((a, b) => b.toLowerCase().localeCompare(a.toLowerCase()));
                let mapped = this.filterGridFiles(this.applyClientSort(this.mapFiles(prefix, this.orderFilesForDisplay(data.files)), sortBy, reverse), path, hideGrids);
                let mapMs = performance.now() - mapStart;
                this.startupStage = 'complete';
                this.backgroundRetryCount = 0;
                this.clearBackgroundWatchdog();
                let renderStart = performance.now();
                this.replaceBrowserContents(path, folders, mapped);
                let renderMs = performance.now() - renderStart;
                console.debug(`History background load: path='${path || '/'}', folders=${folders.length}, files=${mapped.length}, request=${responseMs.toFixed(1)}ms, map=${mapMs.toFixed(1)}ms, render=${renderMs.toFixed(1)}ms${this.perfText(data.perf)}`);
                this.setRequestStatus('idle');
            }, 0, error => {
                if (!this.isBackgroundRequestRelevant(path, requestKey, backgroundToken)) {
                    return;
                }
                this.backgroundRequestInFlight = false;
                console.log(`Background history fill failed: ${error}`);
                this.clearBackgroundWatchdog();
                if (this.backgroundRetryCount >= IMAGE_HISTORY_BACKGROUND_MAX_RETRIES) {
                    this.setRequestStatus('error', `History failed to load fully: ${error}`);
                    return;
                }
                this.backgroundRetryCount++;
                this.setRequestStatus('loading', 'Retrying older history...');
                this.queueFullLoad(path, depth, sortBy, reverse, showHidden, hideGrids);
            }, 300000);
        }, 0);
    }

    /** Handles image-history buttons for image orchestration. */
    buttonsForImage(fullsrc, src, metadata, parsedMetadata = null, isCurrentImage = false) {
        if (typeof parsedMetadata == 'boolean' && !isCurrentImage) {
            isCurrentImage = parsedMetadata;
            parsedMetadata = null;
        }
        let isDataImage = src.startsWith('data:');
        parsedMetadata = parsedMetadata || this.filter.parseMetadata(metadata);
        let mediaType = getMediaType(src);
        let buttons = [];
        if (permissions.hasPermission('user_star_images') && !isDataImage) {
            let getMeta = (metadata) => metadata ? this.filter.parseMetadata(metadata) : {};
            let metaParsed = getMeta(metadata);
            let isStarred = (e) => {
                let currentMeta = getMeta(e?.dataset?.metadata);
                if (Object.keys(currentMeta).length == 0) {
                    currentMeta = metaParsed;
                }
                return currentMeta.is_starred;
            };
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
                    // TODO: Pull the reference from the event, not from register context - or register specifically as a bulk handler
                    if (!isStarred(e)) {
                        toggleStar(fullsrc, src);
                    }
                },
                can_multi: true,
                multi_only: true
            });
            buttons.push({
                label: 'Disable Starred',
                title: 'Marks all selected images as NOT starred if they are currently starred',
                onclick: (e) => {
                    if (isStarred(e)) {
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
                    this.toggleHidden(fullsrc, src);
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
                    this.deleteSingle(fullsrc, src, e);
                },
                can_multi: true
            });
        }
        if (mediaType == 'image' || mediaType == 'video') {
            buttons.push({
                label: 'Compare',
                title: 'Compare 2 images or 2 videos',
                onclick: (e) => {
                    // TODO: Give browsers.js a real "run once with the full selection" bulk handler
                    let items = this.browser.getMultiSelectedFiles().map(f => ({ src: f.data.src, mediaType: getMediaType(f.data.src), metadata: f.data.metadata }));
                    let valid = imageCompareHelper.evaluateSelection(items);
                    if (valid.state != 'ready') {
                        showError(valid.reason || 'Cannot compare current selection.');
                        return;
                    }
                    if (imageCompareHelper.isShowingPair(items[0], items[1])) {
                        return;
                    }
                    imageCompareHelper.reset();
                    imageCompareHelper.showComparison(items[0], items[1]);
                },
                can_multi: true,
                multi_only: true,
                max_selected: 2
            });
        }
        for (let reg of this.registeredMediaButtons) {
            if ((isCurrentImage || reg.showInHistory) && (!reg.mediaTypes || reg.mediaTypes.includes(mediaType))) {
                buttons.push({
                    label: reg.name,
                    title: reg.title,
                    href: reg.href,
                    is_download: reg.is_download,
                    can_multi: reg.can_multi,
                    multi_only: reg.multi_only,
                    max_selected: reg.max_selected,
                    media_types: reg.mediaTypes,
                    onclick: () => reg.action(src)
                });
            }
        }
        return buttons;
    }

    /** Handles image-history describe output file orchestration. */
    describeOutputFile(image) {
        let parsedMeta = this.filter.parseMetadata(image.data.metadata);
        let buttons = this.buttonsForImage(image.data.fullsrc, image.data.src, image.data.metadata, parsedMeta);
        let canHide = permissions.hasPermission('view_image_history') && !image.data.src.startsWith('data:');
        let canDelete = permissions.hasPermission('user_delete_image') && !image.data.src.startsWith('data:');
        let canBulkSelect = canHide || canDelete;
        let isSelected = this.selected.has(image.data.fullsrc);
        let format = this.browser ? this.browser.format : 'Thumbnails';
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
        let searchable = this.filter.getSearchFields(image, parsedMeta);
        let detailMetadata = formattedMetadata ? formattedMetadata.replaceAll('<br>', '&emsp;') : escapeHtml(metadataPreview);
        let detail_list = [escapeHtml(image.data.name), detailMetadata];
        let userDetails = [];
        if (parsedMeta.rating != null && `${parsedMeta.rating}` != '') {
            userDetails.push(`Rating: ${escapeHtml(`${parsedMeta.rating}`)}`);
        }
        let tagText = this.filter.valueToSearchText(parsedMeta.tags);
        if (tagText) {
            userDetails.push(`Tags: ${escapeHtml(tagText)}`);
        }
        if (parsedMeta.notes) {
            userDetails.push(`Notes: ${escapeHtml(parsedMeta.notes)}`);
        }
        if (userDetails.length > 0) {
            detail_list.splice(1, 0, userDetails.join('&emsp;'));
        }
        if (image.data.file_size) {
            detail_list.splice(1, 0, `Size: ${escapeHtml(largeCountStringify(image.data.file_size))}B`);
        }
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
                this.setSelection(image.data.fullsrc, checked, div);
            }
        } : null;
        return { name, description, buttons, checkbox, 'image': imageSrc, 'dragimage': dragImage, className, searchable, display: name, detail_list, aspectRatio };
    }

    /** Handles image-history select output orchestration. */
    selectOutput(image, div) {
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

    /** Handles image-history store with current params orchestration. */
    storeWithCurrentParams(img) {
        let data = getGenInput();
        data['image'] = img;
        delete data['initimage'];
        delete data['maskimage'];
        genericRequest('AddImageToHistory', data, res => {
            mainGenHandler.gotImageResult(res.images[0].image, res.images[0].metadata, '0');
        });
    }

    /** Initializes the browser and wires the image-history collaborators and events. */
    initialize(filter, comparison, bulkActions) {
        this.filter = filter;
        this.comparison = comparison;
        this.bulkActions = bulkActions;
        this.browser = new GenPageBrowserClass(
            'image_history',
            (path, isRefresh, callback, depth, onError) => this.listFolderAndFiles(path, isRefresh, callback, depth, onError),
            'imagehistorybrowser',
            window.userFeatureToggles?.imageHistoryDefaultView || 'Thumbnails',
            image => this.describeOutputFile(image),
            (image, div) => this.selectOutput(image, div),
            IMAGE_HISTORY_HEADER_HTML
        );
        this.browser.allowMultiSelect = true;
        this.browser.maxPreBuild = IMAGE_HISTORY_FAST_FIRST_LIMIT;
        this.browser.filterMatcher = (desc, filter) => this.filter.matches(desc, filter);
        this.browser.filterServerSide = true;
        this.browser.folderSelectedEvent = () => {
            this.clearSelection();
        };
        this.browser.builtEvent = () => {
            this.handleBrowserBuilt();
        };
        getRequiredElementById('imagehistorytabclickable').addEventListener('shown.bs.tab', () => {
            this.handleHistoryTabShown();
        });
    }

    /** Applies browser-built initialization to the image-history shell. */
    handleBrowserBuilt() {
        this.filter.updateHint();
        this.ensureStatusReady();
        this.applyFeatureToggles();
        this.pruneSelection();
        this.updateBulkControls();
        this.windowManager.attach(this.browser.contentDiv);
    }

    /** Handles the History tab becoming visible. */
    handleHistoryTabShown() {
        this.scheduleInitialLoad();
        let historyContent = document.getElementById('imagehistorybrowser-content');
        if (historyContent) {
            browserUtil.queueMakeVisible(historyContent);
            this.windowManager.attach(historyContent);
            this.windowManager.queueUpdate();
        }
    }
}

let imageHistoryController = new ImageHistoryController();
let imageHistoryFilter = new ImageHistoryFilter();
let imageHistoryComparison = new ImageHistoryComparison({
    getFile: path => imageHistoryController.getFile(path),
    parseMetadata: metadata => imageHistoryFilter.parseMetadata(metadata),
    valueToSearchText: value => imageHistoryFilter.valueToSearchText(value),
    setMetadataValue: (metadata, key, value) => imageHistoryController.setMetadataValue(metadata, key, value),
    requestRefresh: () => imageHistoryController.requestRefresh(),
    selectCurrentImage: (src, metadata, batchId) => setCurrentImage(src, metadata, batchId)
});
let imageHistoryBulkActions = new ImageHistoryBulkActions({
    getSelectedPaths: () => imageHistoryController.getSelectedPaths(),
    getFile: path => imageHistoryController.getFile(path),
    getImageSrc: path => imageHistoryController.getHistoryImageSrc(path),
    parseMetadata: metadata => imageHistoryFilter.parseMetadata(metadata),
    setMetadataBoolValue: (metadata, key, value) => imageHistoryController.setMetadataBoolValue(metadata, key, value),
    setMetadataValue: (metadata, key, value) => imageHistoryController.setMetadataValue(metadata, key, value),
    isBusy: () => imageHistoryController.isBusy(),
    setBusy: value => imageHistoryController.setBusy(value),
    clearSelection: () => imageHistoryController.clearSelection(),
    requestRefresh: () => imageHistoryController.requestRefresh(),
    deleteSingle: (fullsrc, src, explicitEntry, errorHandle) => imageHistoryController.deleteSingle(fullsrc, src, explicitEntry, errorHandle),
    toggleHidden: (path, src, refreshAfter, errorHandle) => imageHistoryController.toggleHidden(path, src, refreshAfter, errorHandle),
    updateStarredCards: (src, starred) => imageHistoryController.updateStarredCards(src, starred)
});
imageHistoryController.initialize(imageHistoryFilter, imageHistoryComparison, imageHistoryBulkActions);

Object.defineProperty(globalThis, 'imageHistoryBrowser', {
    configurable: true,
    get: () => imageHistoryController.browser
});

Object.defineProperty(globalThis, 'registeredMediaButtons', {
    configurable: true,
    get: () => imageHistoryController.registeredMediaButtons
});

/** Registers a media button for extensions. 'mediaTypes' filters by type eg ['audio'], null means all. 'isDefault' promotes to visible (vs More dropdown). 'showInHistory' controls whether button appears in the History panel. */
function registerMediaButton(name, action, title = '', mediaTypes = null, isDefault = false, showInHistory = true, href = null, is_download = false, can_multi = false, multi_only = false, max_selected = null) {
    return imageHistoryController.registerMediaButton(name, action, title, mediaTypes, isDefault, showInHistory, href, is_download, can_multi, multi_only, max_selected);
}

/** Gets an encoded output URL through the image-history controller. */
function getHistoryImageSrc(fullSrc) {
    return imageHistoryController.getHistoryImageSrc(fullSrc);
}

/** Requests a coalesced image-history refresh from the controller. */
function requestImageHistoryRefresh() {
    return imageHistoryController.requestRefresh();
}

/** Checks the controller's loaded history for a file. */
function imageHistoryHasFile(fullSrc) {
    return imageHistoryController.hasFile(fullSrc);
}

/** Checks whether the controller's current folder can include a path. */
function imageHistoryCanIncludePath(fullSrc) {
    return imageHistoryController.canIncludePath(fullSrc);
}

/** Adds folders for a path through the image-history controller. */
function addHistoryFoldersForPath(folders, fullSrc) {
    return imageHistoryController.addFoldersForPath(folders, fullSrc);
}

/** Attempts immediate saved-image insertion through the controller. */
function tryAddSavedImageToHistory(savedPath, metadata = null) {
    return imageHistoryController.tryAddSavedImage(savedPath, metadata);
}

/** Schedules the controller's saved-image refresh retry loop. */
function scheduleImageHistorySavedRefresh(delayMs) {
    return imageHistoryController.scheduleSavedRefresh(delayMs);
}

/** Notifies the controller that an image was saved. */
function notifyImageHistorySavedPath(savedPath, metadata = null) {
    return imageHistoryController.notifySavedPath(savedPath, metadata);
}

/** Requests a metadata rescan through the image-history controller. */
function rescanImageHistoryMetadata(rebuild = true) {
    return imageHistoryController.rescanMetadata(rebuild);
}

/** Clears the controller's automatic retry state. */
function clearImageHistoryAutoRetry() {
    return imageHistoryController.clearAutoRetry();
}

/** Clears the controller's background-load watchdog. */
function clearImageHistoryBackgroundWatchdog() {
    return imageHistoryController.clearBackgroundWatchdog();
}

/** Cancels the controller's current background history load. */
function cancelImageHistoryBackgroundLoad() {
    return imageHistoryController.cancelBackgroundLoad();
}

/** Builds a background request key through the controller. */
function getImageHistoryRequestKey(path, depth, sortBy, reverse, showHidden, hideGrids) {
    return imageHistoryController.getRequestKey(path, depth, sortBy, reverse, showHidden, hideGrids);
}

/** Checks whether a background request remains relevant. */
function isImageHistoryBackgroundRequestStillRelevant(path, requestKey, backgroundToken) {
    return imageHistoryController.isBackgroundRequestRelevant(path, requestKey, backgroundToken);
}

/** Schedules the controller's background-load watchdog. */
function scheduleImageHistoryBackgroundWatchdog(path, depth, sortBy, reverse, showHidden, hideGrids, requestKey, backgroundToken) {
    return imageHistoryController.scheduleBackgroundWatchdog(path, depth, sortBy, reverse, showHidden, hideGrids, requestKey, backgroundToken);
}

/** Starts a manual history retry through the controller. */
function retryImageHistoryManually() {
    return imageHistoryController.retryManually();
}

/** Ensures the controller's request-status controls are ready. */
function ensureImageHistoryStatusReady() {
    return imageHistoryController.ensureStatusReady();
}

/** Applies history feature toggles through the controller. */
function applyImageHistoryFeatureToggles() {
    return imageHistoryController.applyFeatureToggles();
}

/** Updates history request status through the controller. */
function setImageHistoryRequestStatus(state, message = '') {
    return imageHistoryController.setRequestStatus(state, message);
}

/** Ensures the controller's browser shell is ready. */
function ensureImageHistoryBrowserShellReady() {
    return imageHistoryController.ensureBrowserShellReady();
}

/** Schedules the controller's initial history load. */
function scheduleInitialImageHistoryLoad(delayMs = 0) {
    return imageHistoryController.scheduleInitialLoad(delayMs);
}

/** Ensures the controller's history header controls are ready. */
function ensureImageHistoryHeaderControlsReady(sortBy, reverse, allowAnims, showHidden, hideGrids) {
    return imageHistoryController.ensureHeaderControlsReady(sortBy, reverse, allowAnims, showHidden, hideGrids);
}

/** Schedules the controller's one automatic load retry. */
function scheduleImageHistoryAutoRetry() {
    return imageHistoryController.scheduleAutoRetry();
}

/** Parses metadata through the image-history filter cache. */
function parseHistoryMetadata(metadata) {
    return imageHistoryFilter.parseMetadata(metadata);
}

/** Converts a metadata value to search text through the history filter. */
function imageHistoryValueToSearchText(value) {
    return imageHistoryFilter.valueToSearchText(value);
}

/** Builds structured search fields through the image-history filter. */
function getImageHistorySearchFields(image, parsedMeta) {
    return imageHistoryFilter.getSearchFields(image, parsedMeta);
}

/** Splits a query through the image-history filter. */
function splitImageHistoryFilterQuery(filter) {
    return imageHistoryFilter.splitQuery(filter);
}

/** Normalizes a query field through the image-history filter. */
function normalizeImageHistoryFilterField(field) {
    return imageHistoryFilter.normalizeField(field);
}

/** Compiles a query through the image-history filter. */
function compileImageHistoryFilterQuery(filter) {
    return imageHistoryFilter.compileQuery(filter);
}

/** Gets searchable entry text through the image-history filter. */
function getImageHistorySearchableText(desc, searchable) {
    return imageHistoryFilter.getSearchableText(desc, searchable);
}

/** Evaluates a numeric comparison through the image-history filter. */
function imageHistoryNumericFilterMatches(fieldText, value) {
    return imageHistoryFilter.numericMatches(fieldText, value);
}

/** Evaluates a date comparison through the image-history filter. */
function imageHistoryDateFilterMatches(fieldText, value) {
    return imageHistoryFilter.dateMatches(fieldText, value);
}

/** Matches an entry through the image-history filter. */
function imageHistoryFilterMatches(desc, filter) {
    return imageHistoryFilter.matches(desc, filter);
}

/** Updates the query hint through the image-history filter. */
function updateImageHistoryFilterHint() {
    return imageHistoryFilter.updateHint();
}

/** Gets a loaded file from the image-history controller. */
function getImageHistoryFile(path) {
    return imageHistoryController.getFile(path);
}

/** Ensures the image-history comparison modal exists. */
function ensureImageHistoryCompareModal() {
    return imageHistoryComparison.ensureModal();
}

/** Reuses generation settings from one comparison side. */
function reuseImageHistoryCompareSettings(side) {
    return imageHistoryComparison.reuseSettings(side);
}

/** Toggles starred state for one comparison image. */
function starImageHistoryCompareImage(side) {
    return imageHistoryComparison.starImage(side);
}

/** Prompts for a rating on one comparison image. */
function rateImageHistoryCompareImage(side) {
    return imageHistoryComparison.rateImage(side);
}

/** Renders the active image-history comparison pair. */
function renderImageHistoryComparePair() {
    return imageHistoryComparison.renderPair();
}

/** Swaps the two image-history comparison sides. */
function swapImageHistoryCompareImages() {
    return imageHistoryComparison.swapImages();
}

/** Gets comparison metadata fields for one history file. */
function getImageHistoryCompareMetadataFields(file) {
    return imageHistoryComparison.getMetadataFields(file);
}

/** Toggles image-history comparison metadata mode. */
function setImageHistoryCompareMetadataMode(enabled) {
    return imageHistoryComparison.setMetadataMode(enabled);
}

/** Renders image-history comparison metadata. */
function renderImageHistoryCompareMetadata() {
    return imageHistoryComparison.renderMetadata();
}

/** Closes the image-history comparison modal. */
function closeImageHistoryCompareModal() {
    return imageHistoryComparison.closeModal();
}

/** Cleans up image-history comparison modal state. */
function cleanupImageHistoryCompareModal() {
    return imageHistoryComparison.cleanupModal();
}

/** Shows the Generate tab after comparison closes. */
function showGenerateTabAfterImageHistoryCompareClose() {
    return imageHistoryComparison.showGenerateTabAfterClose();
}

/** Opens the image-history comparison modal. */
function openImageHistoryCompareModal() {
    return imageHistoryComparison.openModal();
}

/** Updates comparison reveal from a pointer event. */
function updateImageHistoryCompareRevealFromPointer(e) {
    return imageHistoryComparison.updateRevealFromPointer(e);
}

/** Starts panning the image-history comparison. */
function startImageHistoryComparePan(e) {
    return imageHistoryComparison.startPan(e);
}

/** Ends panning the image-history comparison. */
function endImageHistoryComparePan(e) {
    return imageHistoryComparison.endPan(e);
}

/** Applies the current image-history comparison pan. */
function applyImageHistoryComparePan() {
    return imageHistoryComparison.applyPan();
}

/** Sets the image-history comparison reveal position. */
function setImageHistoryCompareReveal(value) {
    return imageHistoryComparison.setReveal(value);
}

/** Toggles image-history comparison difference mode. */
function setImageHistoryCompareDiffMode(enabled) {
    return imageHistoryComparison.setDiffMode(enabled);
}

/** Renders the image-history comparison difference view. */
function renderImageHistoryCompareDiff() {
    return imageHistoryComparison.renderDiff();
}

/** Sets the image-history comparison zoom. */
function setImageHistoryCompareZoom(value) {
    return imageHistoryComparison.setZoom(value);
}

/** Shows the selected history paths in the comparison modal. */
function showImageHistoryCompare(paths) {
    return imageHistoryComparison.show(paths);
}

/** Orders history files through the controller's display rules. */
function orderHistoryFilesForDisplay(files) {
    return imageHistoryController.orderFilesForDisplay(files);
}

/** Maps server files through the image-history controller. */
function mapHistoryFiles(prefix, files) {
    return imageHistoryController.mapFiles(prefix, files);
}

/** Normalizes a history sort name through the controller. */
function normalizeImageHistorySortBy(sortBy) {
    return imageHistoryController.normalizeSortBy(sortBy);
}

/** Checks server sort support through the controller. */
function imageHistorySortSupportedByServer(sortBy) {
    return imageHistoryController.sortSupportedByServer(sortBy);
}

/** Gets a numeric history sort value through the controller. */
function getImageHistorySortNumber(file, sortBy) {
    return imageHistoryController.getSortNumber(file, sortBy);
}

/** Gets a textual history sort value through the controller. */
function getImageHistorySortText(file, sortBy) {
    return imageHistoryController.getSortText(file, sortBy);
}

/** Applies client-side history sorting through the controller. */
function applyImageHistoryClientSort(files, sortBy, reverse) {
    return imageHistoryController.applyClientSort(files, sortBy, reverse);
}

/** Sorts mapped history files through the controller. */
function sortHistoryFilesForDisplay(files, sortBy, reverse) {
    return imageHistoryController.sortFilesForDisplay(files, sortBy, reverse);
}

/** Formats history timing diagnostics through the controller. */
function imageHistoryPerfText(perf) {
    return imageHistoryController.perfText(perf);
}

/** Checks for a grid folder through the image-history controller. */
function isImageHistoryGridFolder(path) {
    return imageHistoryController.isGridFolder(path);
}

/** Filters grid files through the image-history controller. */
function filterImageHistoryGridFiles(files, path, hideGrids) {
    return imageHistoryController.filterGridFiles(files, path, hideGrids);
}

/** Replaces browser contents through the image-history controller. */
function replaceHistoryBrowserContents(path, folders, mapped) {
    return imageHistoryController.replaceBrowserContents(path, folders, mapped);
}

/** Updates a boolean metadata value through the controller. */
function setMetadataBoolValue(metadata, key, value) {
    return imageHistoryController.setMetadataBoolValue(metadata, key, value);
}

/** Updates a metadata value through the controller. */
function setMetadataValue(metadata, key, value) {
    return imageHistoryController.setMetadataValue(metadata, key, value);
}

/** Gets rendered history entries through the controller. */
function getImageHistoryEntries() {
    return imageHistoryController.getEntries();
}

/** Prunes selection to loaded files through the controller. */
function pruneImageHistorySelectionToCurrentFiles() {
    return imageHistoryController.pruneSelection();
}

/** Gets checked history paths through the controller. */
function getCheckedImageHistoryPaths() {
    return imageHistoryController.getCheckedPaths();
}

/** Synchronizes controller selection from history checkboxes. */
function syncImageHistorySelectionFromDOM() {
    return imageHistoryController.syncSelectionFromDOM();
}

/** Updates bulk controls through the image-history controller. */
function updateImageHistoryBulkControls() {
    return imageHistoryController.updateBulkControls();
}

/** Updates one selected history path through the controller. */
function setImageHistorySelection(fullsrc, isSelected, entry = null) {
    return imageHistoryController.setSelection(fullsrc, isSelected, entry);
}

/** Clears history selection through the controller. */
function clearImageHistorySelection() {
    return imageHistoryController.clearSelection();
}

/** Selects all rendered history entries through the controller. */
function selectAllImageHistory() {
    return imageHistoryController.selectAll();
}

/** Clears selected history entries through the controller. */
function clearSelectedImageHistory() {
    return imageHistoryController.clearSelection();
}

/** Hides selected images through the bulk-actions collaborator. */
function hideSelectedImageHistory() {
    return imageHistoryBulkActions.hideSelected();
}

/** Unhides selected images through the bulk-actions collaborator. */
function unhideSelectedImageHistory() {
    return imageHistoryBulkActions.unhideSelected();
}

/** Deletes selected images through the bulk-actions collaborator. */
function deleteSelectedImageHistory() {
    return imageHistoryBulkActions.deleteSelected();
}

/** Stars selected images through the bulk-actions collaborator. */
function starSelectedImageHistory() {
    return imageHistoryBulkActions.starSelected();
}

/** Unstars selected images through the bulk-actions collaborator. */
function unstarSelectedImageHistory() {
    return imageHistoryBulkActions.unstarSelected();
}

/** Opens the bulk-actions rating prompt for selected images. */
function setSelectedImageHistoryRatingPrompt() {
    return imageHistoryBulkActions.promptRating();
}

/** Opens the bulk-actions tag prompt for selected images. */
function setSelectedImageHistoryTagsPrompt(mode) {
    return imageHistoryBulkActions.promptTags(mode);
}

/** Opens the bulk-actions notes prompt for selected images. */
function setSelectedImageHistoryNotesPrompt() {
    return imageHistoryBulkActions.promptNotes();
}

/** Opens the bulk-actions move or copy prompt. */
function moveSelectedImageHistoryPrompt(mode) {
    return imageHistoryBulkActions.promptMove(mode);
}

/** Compares selected images through the image-history controller. */
function compareSelectedImageHistory() {
    return imageHistoryController.compareSelected();
}

/** Exports selected metadata through the bulk-actions collaborator. */
function exportSelectedImageHistoryMetadata() {
    return imageHistoryBulkActions.exportMetadata();
}

/** Copies selected paths through the bulk-actions collaborator. */
function copySelectedImageHistoryPaths() {
    return imageHistoryBulkActions.copyPaths();
}

/** Loads a contact-sheet image through the bulk-actions collaborator. */
function loadImageHistoryContactSheetImage(src) {
    return imageHistoryBulkActions.loadContactSheetImage(src);
}

/** Creates a contact sheet through the bulk-actions collaborator. */
async function createSelectedImageHistoryContactSheet() {
    return await imageHistoryBulkActions.createContactSheet();
}

/** Converts metadata for Prompt Lab through the bulk-actions collaborator. */
function imageHistoryMetadataToPromptLabPrompt(file) {
    return imageHistoryBulkActions.metadataToPromptLabPrompt(file);
}

/** Sends selected images to Prompt Lab through the bulk-actions collaborator. */
async function sendSelectedImageHistoryToPromptLab() {
    return await imageHistoryBulkActions.sendToPromptLab();
}

/** Sets selected starred state through the bulk-actions collaborator. */
async function setSelectedHistoryImagesStarred(targetStarred) {
    return await imageHistoryBulkActions.setStarred(targetStarred);
}

/** Sets selected ratings through the bulk-actions collaborator. */
async function setSelectedImageHistoryRating(rating) {
    return await imageHistoryBulkActions.setRating(rating);
}

/** Sets selected tags through the bulk-actions collaborator. */
async function setSelectedImageHistoryTags(tags, mode) {
    return await imageHistoryBulkActions.setTags(tags, mode);
}

/** Sets selected notes through the bulk-actions collaborator. */
async function setSelectedImageHistoryNotes(notes) {
    return await imageHistoryBulkActions.setNotes(notes);
}

/** Moves or copies selected images through the bulk-actions collaborator. */
async function moveSelectedImageHistory(folder, mode) {
    return await imageHistoryBulkActions.move(folder, mode);
}

/** Ensures bulk controls are ready through the image-history controller. */
function ensureImageHistoryBulkControlsReady() {
    return imageHistoryController.ensureBulkControlsReady();
}

/** Delegates rendered history removal to the image-history controller. */
function removeImageFromHistoryUI(fullsrc, src, explicitEntry = null) {
    return imageHistoryController.removeImageFromUI(fullsrc, src, explicitEntry);
}

/** Deletes one history image through the controller. */
function deleteSingleHistoryImage(fullsrc, src, explicitEntry = null, errorHandle = null) {
    return imageHistoryController.deleteSingle(fullsrc, src, explicitEntry, errorHandle);
}

/** Toggles one image's hidden state through the controller. */
function toggleImageHidden(path, rawSrc, refreshAfter = true, errorHandle = null) {
    return imageHistoryController.toggleHidden(path, rawSrc, refreshAfter, errorHandle);
}

/** Sets selected hidden state through the bulk-actions collaborator. */
async function setSelectedHistoryImagesHidden(targetHidden) {
    return await imageHistoryBulkActions.setHidden(targetHidden);
}

/** Deletes selected history images through the bulk-actions collaborator. */
async function deleteSelectedHistoryImages() {
    return await imageHistoryBulkActions.deleteImages();
}

/** Lists a history folder through the image-history controller. */
function listOutputHistoryFolderAndFiles(path, isRefresh, callback, depth, onError = null) {
    return imageHistoryController.listFolderAndFiles(path, isRefresh, callback, depth, onError);
}

/** Queues a full history load through the controller. */
function queueFullImageHistoryLoad(path, depth, sortBy, reverse, showHidden, hideGrids) {
    return imageHistoryController.queueFullLoad(path, depth, sortBy, reverse, showHidden, hideGrids);
}

/** Builds image actions through the image-history controller. */
function buttonsForImage(fullsrc, src, metadata, parsedMetadata = null, isCurrentImage = false) {
    return imageHistoryController.buttonsForImage(fullsrc, src, metadata, parsedMetadata, isCurrentImage);
}

/** Describes an output file through the image-history controller. */
function describeOutputFile(image) {
    return imageHistoryController.describeOutputFile(image);
}

/** Selects an output through the image-history controller. */
function selectOutputInHistory(image, div) {
    return imageHistoryController.selectOutput(image, div);
}

/** Stores an image with current parameters through the controller. */
function storeImageToHistoryWithCurrentParams(img) {
    return imageHistoryController.storeWithCurrentParams(img);
}
