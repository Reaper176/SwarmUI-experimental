
/** Helper utilities for browsers. */
class BrowserUtil {
    constructor() {
        this.makeVisibleQueued = new WeakSet();
    }

    /**
     * Schedules makeVisible on the next animation frame.
     */
    queueMakeVisible(elem) {
        if (!elem || !elem.querySelectorAll) {
            return;
        }
        if (this.makeVisibleQueued.has(elem)) {
            return;
        }
        this.makeVisibleQueued.add(elem);
        let run = () => {
            this.makeVisibleQueued.delete(elem);
            if (!elem.querySelectorAll) {
                return;
            }
            this.makeVisible(elem);
        };
        if (window.requestAnimationFrame) {
            requestAnimationFrame(run);
        }
        else {
            setTimeout(run, 16);
        }
    }

    /**
     * Make any visible images within a container actually load now.
     */
    makeVisible(elem) {
        if (!elem || !elem.querySelectorAll) {
            return;
        }
        let elementsToLoad = Array.from(elem.querySelectorAll('.lazyload')).filter(e => {
            let top = e.getBoundingClientRect().top;
            return top != 0 && top < window.innerHeight + 512; // Note top=0 means not visible
        });
        for (let subElem of elementsToLoad) {
            subElem.classList.remove('lazyload');
            if (subElem.tagName == 'IMG') {
                if (!subElem.dataset.src) {
                    continue;
                }
                subElem.src = subElem.dataset.src;
                delete subElem.dataset.src;
            }
            else if (subElem.classList.contains('browser-section-loader')) {
                subElem.click();
                subElem.remove();
            }
        }
    }
}

let browserUtil = new BrowserUtil();

class BrowserMediaWindowManager {
    constructor(rowBuffer = 8, minRowsToUnload = 2) {
        this.rowBuffer = rowBuffer;
        this.minRowsToUnload = minRowsToUnload;
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

    getEntries() {
        if (!this.content) {
            return [];
        }
        return Array.from(this.content.children).filter(entry => entry?.dataset?.name);
    }

    getEntryImage(entry) {
        return entry.querySelector('img.image-block-img-inner');
    }

    hydrateEntry(entry) {
        let img = this.getEntryImage(entry);
        if (!img || !img.dataset.origSrc || img.getAttribute('src')) {
            return false;
        }
        img.dataset.src = img.dataset.origSrc;
        img.classList.add('lazyload');
        return true;
    }

    dehydrateEntry(entry) {
        let img = this.getEntryImage(entry);
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
            }
            else {
                lastRow.entries.push(entry);
                lastRow.bottom = Math.max(lastRow.bottom, bottom);
            }
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
        let keepStart = Math.max(0, visibleStart - this.rowBuffer);
        let keepEnd = Math.min(rows.length - 1, visibleEnd + this.rowBuffer);
        let hydrateQueued = false;
        for (let i = 0; i < rows.length; i++) {
            let row = rows[i];
            if (i >= keepStart && i <= keepEnd) {
                for (let entry of row.entries) {
                    hydrateQueued = this.hydrateEntry(entry) || hydrateQueued;
                }
            }
            else if (i < keepStart - this.minRowsToUnload || i > keepEnd + this.minRowsToUnload) {
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

/**
 * Hack to attempt to prevent callback recursion.
 * In practice this seems to not work.
 * Either JavaScript itself or Firefox seems to really love tracking the stack and refusing to let go.
 * TODO: Guarantee it actually works so we can't stack overflow from file browsing ffs.
 */
class BrowserCallHelper {
    constructor(path, loadCaller) {
        this.path = path;
        this.loadCaller = loadCaller;
    }
    call() {
        this.loadCaller(this.path);
    }
}

/**
 * Part of a browser tree.
 */
class BrowserTreePart {
    constructor(name, hasOpened, isOpen, fileData = null, fullPath = '') {
        this.name = name;
        this.children = {};
        this.childrenKeys = [];
        this.hasOpened = hasOpened;
        this.isOpen = isOpen;
        this.fileData = fileData;
        this.fullPath = fullPath.startsWith('/') ? fullPath.substring(1) : fullPath;
        this.clickme = null;
    }

    addChild(name, part) {
        if (!(name in this.children)) {
            this.childrenKeys.push(name);
        }
        this.children[name] = part;
    }
}

/**
 * Class that handles browsable content sections (eg models list, presets list, etc).
 */
class GenPageBrowserClass {

    constructor(container, listFoldersAndFiles, id, defaultFormat, describe, select, extraHeader = '', defaultDepth = 3) {
        this.container = getRequiredElementById(container);
        this.listFoldersAndFiles = listFoldersAndFiles;
        this.id = id;
        this.format = localStorage.getItem(`browser_${this.id}_format`) || getCookie(`${id}_format`) || defaultFormat; // TODO: Remove the old cookie
        this.describe = describe;
        this.select = select;
        this.folder = '';
        this.selected = null;
        this.extraHeader = extraHeader;
        this.navCaller = this.navigate.bind(this);
        this.tree = new BrowserTreePart('', false, true, null, '');
        this.depth = localStorage.getItem(`browser_${id}_depth`) || defaultDepth;
        this.filter = localStorage.getItem(`browser_${id}_filter`) || '';
        this.folderTreeVerticalSpacing = '0';
        this.splitterMinWidth = 100;
        this.everLoaded = false;
        this.showDisplayFormat = true;
        this.showDepth = true;
        this.showRefresh = true;
        this.showUpFolder = true;
        this.showFilter = true;
        this.folderTreeShowFiles = false;
        this.folderSelectedEvent = null;
        this.builtEvent = null;
        this.sizeChangedEvent = null;
        this.updateFailedEvent = null;
        this.filterMatcher = null;
        this.filterSorter = null;
        this.filterUpdateTimeout = null;
        this.filterUpdateDelayMs = 250;
        this.lastFiles = [];
        this.lastFilesMap = new Map();
        this.describeCache = new Map();
        this.enableDescriptionCache = false;
        this.maxPreBuild = 512;
        this.chunksRendered = 0;
        this.rerenderPlanned = false;
        this.updatePendingSince = null;
        this.noContentUpdates = false;
        this.lastListCache = null;
        this.runAfterUpdate = [];
        this.refreshHandler = (callback) => callback();
        this.pendingRefreshPath = null;
        this.mediaWindowManager = null;
        this.checkIsSmall();
    }

    /**
     * Checks if the window is small, setting isSmallWindow (mostly for mobile compat).
     */
    checkIsSmall() {
        let mobileDesktopLayout = localStorage.getItem('layout_mobileDesktop') || 'auto';
        this.isSmallWindow = mobileDesktopLayout == 'auto' ? window.innerWidth < 768 : mobileDesktopLayout == 'mobile';
    }

    /**
     * Schedules a rerender with a small delay.
     */
    planRerender(timeout) {
        if (this.rerenderPlanned) {
            return;
        }
        this.rerenderPlanned = true;
        setTimeout(() => {
            this.rerenderPlanned = false;
            this.rerender();
        }, timeout);
    }

    /**
     * Ensures the browser shell exists even before any data has loaded.
     */
    ensureBuilt() {
        if (!this.hasGenerated) {
            this.build(this.folder || '', [], []);
        }
    }

    /**
     * Wraps a set of header controls into a movable group.
     */
    createHeaderControlGroup(elements, className = '', isPinned = false) {
        let group = createDiv(null, `browser-header-control-group ${className}`.trim());
        group.dataset.headerPinned = isPinned ? 'true' : 'false';
        for (let element of elements) {
            group.appendChild(element);
        }
        return group;
    }

    /**
     * Flattens extra-header elements into ordered movable groups.
     */
    flattenHeaderControlElements(elements) {
        let groups = [];
        for (let i = 0; i < elements.length; i++) {
            let element = elements[i];
            if (!element || element.style.display == 'none') {
                continue;
            }
            if ((element.tagName == 'SPAN' || element.tagName == 'DIV') && element.children.length > 0 && !element.classList.contains('input_filter_container')) {
                let childElements = [...element.children].filter(child => child.nodeType == 1);
                if (childElements.length > 0) {
                    groups.push(...this.flattenHeaderControlElements(childElements));
                    continue;
                }
            }
            if (element.tagName == 'LABEL') {
                let next = elements[i + 1];
                if (next && (next.tagName == 'SELECT' || next.tagName == 'INPUT') && (!element.htmlFor || element.htmlFor == next.id)) {
                    let isSortLabel = element.innerText.trim() == 'Sort:';
                    groups.push(this.createHeaderControlGroup([element, next], isSortLabel ? 'browser-header-control-group-sort' : '', isSortLabel));
                    i++;
                    continue;
                }
            }
            if (element.tagName == 'INPUT' && element.type == 'checkbox') {
                let next = elements[i + 1];
                if (next && next.tagName == 'LABEL' && next.htmlFor == element.id) {
                    groups.push(this.createHeaderControlGroup([element, next]));
                    i++;
                    continue;
                }
            }
            groups.push(this.createHeaderControlGroup([element]));
        }
        return groups;
    }

    /**
     * Closes the browser header overflow menu.
     */
    closeHeaderOverflowMenu() {
        if (!this.headerOverflowPopover) {
            return;
        }
        this.headerOverflowPopover.classList.remove('sui-popover-visible');
        if (this.headerMoreButton) {
            this.headerMoreButton.setAttribute('aria-expanded', 'false');
        }
    }

    /**
     * Repositions the browser header overflow menu to the More button.
     */
    repositionHeaderOverflowMenu() {
        if (!this.headerOverflowPopover || !this.headerMoreButton || this.headerOverflowPopover.classList.contains('sui-popover-visible') == false) {
            return;
        }
        let rect = this.headerMoreButton.getBoundingClientRect();
        let left = Math.max(8, Math.min(rect.right - this.headerOverflowPopover.offsetWidth, window.innerWidth - this.headerOverflowPopover.offsetWidth - 8));
        let top = Math.min(rect.bottom + 6, window.innerHeight - this.headerOverflowPopover.offsetHeight - 8);
        this.headerOverflowPopover.style.left = `${left}px`;
        this.headerOverflowPopover.style.top = `${top}px`;
    }

    /**
     * Toggles the browser header overflow menu.
     */
    toggleHeaderOverflowMenu() {
        if (!this.headerOverflowPopover || !this.headerMoreButton || this.headerOverflowGroups.length == 0) {
            return;
        }
        let shouldOpen = !this.headerOverflowPopover.classList.contains('sui-popover-visible');
        this.closeHeaderOverflowMenu();
        if (!shouldOpen) {
            return;
        }
        this.headerOverflowPopover.classList.add('sui-popover-visible');
        this.headerMoreButton.setAttribute('aria-expanded', 'true');
        this.repositionHeaderOverflowMenu();
    }

    /**
     * Queues header layout recalculation.
     */
    queueHeaderLayout() {
        if (this.headerLayoutQueued) {
            return;
        }
        this.headerLayoutQueued = true;
        let run = () => {
            this.headerLayoutQueued = false;
            this.layoutHeaderControls();
        };
        if (window.requestAnimationFrame) {
            requestAnimationFrame(run);
        }
        else {
            setTimeout(run, 16);
        }
    }

    /**
     * Lays out browser header control groups and overflows right-side candidates.
     */
    layoutHeaderControls() {
        if (!this.headerControlRow || !this.headerMoreButton || !this.headerOverflowPopover || !this.headerControlGroups) {
            return;
        }
        this.closeHeaderOverflowMenu();
        this.headerOverflowGroups = [];
        for (let group of this.headerControlGroups) {
            this.headerControlRow.insertBefore(group, this.headerMoreButton);
        }
        this.headerMoreButton.style.display = 'none';
        this.headerOverflowPopover.innerHTML = '';
        let candidates = this.headerControlGroups.filter(group => group.dataset.headerPinned != 'true');
        while (this.headerControlRow.scrollWidth > this.headerControlRow.clientWidth && candidates.length > 0) {
            let group = candidates.pop();
            this.headerMoreButton.style.display = '';
            this.headerOverflowPopover.appendChild(group);
            this.headerOverflowGroups.unshift(group);
        }
        if (this.headerOverflowGroups.length == 0) {
            this.headerMoreButton.style.display = 'none';
        }
    }

    /**
     * Marks the current update as completed successfully.
     */
    completeUpdate(callback = null) {
        this.pendingRefreshPath = null;
        this.updatePendingSince = null;
        if (callback) {
            setTimeout(() => callback(), 100);
        }
        if (this.runAfterUpdate.length > 0) {
            let first = this.runAfterUpdate.shift();
            first();
        }
    }

    /**
     * Marks the current update as failed and releases any pending retry lockout.
     */
    failUpdate(error = null) {
        if (this.pendingRefreshPath != null) {
            this.folder = this.pendingRefreshPath;
            this.pendingRefreshPath = null;
        }
        this.noContentUpdates = false;
        this.updatePendingSince = null;
        if (this.updateFailedEvent) {
            this.updateFailedEvent(error);
        }
        if (this.runAfterUpdate.length > 0) {
            let first = this.runAfterUpdate.shift();
            first();
        }
    }

    /**
     * Navigates the browser to a given folder path.
     */
    navigate(folder, callback = null) {
        this.chunksRendered = 0;
        this.folder = folder;
        this.selected = null;
        this.update(false, callback);
    }

    /**
     * Clicks repeatedly into a path to fully open it.
     */
    clickPath(path) {
        this.noContentUpdates = true;
        let tree = this.tree;
        if (!tree.isOpen) {
            tree.clickme(() => {
                this.clickPath(path);
            });
            return;
        }
        if (path.length == 0) {
            this.noContentUpdates = false;
            this.rerender();
            return;
        }
        let split = path.split('/');
        for (let part of split) {
            if (part == '') {
                continue;
            }
            if (!(part in tree.children)) {
                this.noContentUpdates = false;
                this.rerender();
                return;
            }
            tree = tree.children[part];
            if (tree.fileData) {
                tree.clickme(() => {
                    this.noContentUpdates = false;
                    this.rerender();
                });
                return;
            }
            else if (!tree.isOpen) {
                tree.clickme(() => {
                    this.clickPath(path);
                });
                return;
            }
        }
        this.noContentUpdates = false;
        this.rerender();
    }

    /**
     * Refreshes the browser view from source.
     */
    refresh() {
        this.refreshHandler(() => {
            this.lastListCache = null;
            this.chunksRendered = 0;
            let path = this.folder;
            this.pendingRefreshPath = path;
            this.folder = '';
            let depth = this.depth;
            this.noContentUpdates = true;
            this.update(true, () => {
                this.clickPath(path);
            });
        });
    }

    /**
     * Performs a 'light' refresh: cacheless update, but no server refresh call.
     */
    lightRefresh() {
        this.lastListCache = null;
        this.describeCache.clear();
        this.update();
    }

    /**
     * Updates/refreshes the browser view.
     */
    update(isRefresh = false, callback = null) {
        if (this.updatePendingSince && new Date().getTime() - this.updatePendingSince < 5000) {
            this.runAfterUpdate.push(() => this.update(isRefresh, callback));
            return;
        }
        this.updatePendingSince = new Date().getTime();
        if (isRefresh) {
            this.tree = new BrowserTreePart('', false, null, null, '');
            this.contentDiv.scrollTop = 0;
            this.describeCache.clear();
        }
        let folder = this.folder;
        let parseContent = (folders, files) => {
            this.lastListCache = { folder, folders, files };
            this.build(folder, folders, files);
            this.completeUpdate(callback);
        };
        if (!isRefresh && this.lastListCache && this.lastListCache.folder == folder) {
            parseContent(this.lastListCache.folders, this.lastListCache.files);
            return;
        }
        try {
            this.listFoldersAndFiles(folder, isRefresh, parseContent, this.depth, this.failUpdate.bind(this));
        }
        catch (error) {
            console.error(`Browser '${this.id}' failed to update`, error);
            this.failUpdate(error);
        }
    }

    /**
     * Generates the path list span for the current path view, and returns it.
     */
    genPath(path, upButton) {
        let pathGen = createSpan(`${this.id}-path`, 'browser-path');
        if (path == '') {
            upButton.disabled = true;
            return pathGen;
        }
        let rootPathPrefix = 'Root/';
        let partial = '';
        for (let part of (rootPathPrefix + path).split('/')) {
            partial += part + '/';
            let span = document.createElement('span');
            span.className = 'path-list-part';
            span.innerText = part;
            let route = partial.substring(rootPathPrefix.length);
            let helper = new BrowserCallHelper(route, this.navCaller);
            span.onclick = helper.call.bind(helper);
            pathGen.appendChild(span);
            pathGen.appendChild(document.createTextNode('/'));
        }
        upButton.disabled = false;
        let above = path.split('/').slice(0, -1).join('/');
        let helper = new BrowserCallHelper(above, this.navCaller);
        upButton.onclick = helper.call.bind(helper);
        return pathGen;
    }

    /**
     * Updates tree tracker for the given path.
     */
    refillTree(path, folders, isFile = false) {
        if (path.endsWith('/')) {
            path = path.substring(0, path.length - 1);
        }
        if (path.startsWith('/')) {
            path = path.substring(1);
        }
        let otherFolders = folders.filter(f => f.includes('/'));
        if (otherFolders.length > 0) {
            let baseFolders = folders.filter(f => !f.includes('/'));
            this.refillTree(path, baseFolders, isFile);
            while (otherFolders.length > 0) {
                let folder = otherFolders[0];
                let slash = folder.indexOf('/');
                let base = folder.substring(0, slash + 1);
                let same = otherFolders.filter(f => f.startsWith(base)).map(f => f.substring(base.length));
                this.refillTree(`${path}/${base}`, same, isFile);
                otherFolders = otherFolders.filter(f => !f.startsWith(base));
            }
            return;
        }
        if (path == '') {
            let copy = Object.assign({}, this.tree.children);
            this.tree.children = {};
            this.tree.childrenKeys = [];
            for (let folder of folders) {
                this.tree.addChild(folder, copy[folder] || new BrowserTreePart(folder, isFile, false, isFile ? this.getFileFor(folder) : null, folder));
            }
            this.tree.hasOpened = true;
            return;
        }
        let tree = this.tree, parent = this.tree;
        let parts = path.split('/');
        for (let part of parts) {
            parent = tree;
            if (!(part in parent.children)) {
                parent.addChild(part, new BrowserTreePart(part, false, false, null, parent.fullPath + '/' + part));
            }
            tree = parent.children[part];
        }
        let lastName = parts[parts.length - 1];
        let copy = Object.assign({}, tree.children);
        tree = new BrowserTreePart(lastName, true, tree.isOpen, null, tree.fullPath);
        parent.addChild(lastName, tree);
        for (let folder of folders) {
            tree.addChild(folder, copy[folder] || new BrowserTreePart(folder, isFile, false, isFile ? this.getFileFor(tree.fullPath + '/' + folder) : null, tree.fullPath + '/' + folder));
        }
    }

    /**
     * Builds the element view of the folder tree.
     */
    buildTreeElements(container, path, tree, offset = 16, isRoot = true) {
        if (isRoot) {
            let spacer = createDiv(null, 'browser-folder-tree-spacer');
            spacer.style.height = this.folderTreeVerticalSpacing;
            container.appendChild(spacer);
        }
        let span = createSpan(`${this.id}-foldertree-${tree.name}`, 'browser-folder-tree-part');
        let trueOffset = offset;
        if (this.isSmallWindow) {
            trueOffset /= 2;
        }
        span.style.left = `${trueOffset}px`;
        span.innerHTML = `<span class="browser-folder-tree-part-symbol" data-issymbol="true"></span> ${escapeHtml(tree.name || 'Root')}`;
        span.dataset.path = path;
        container.appendChild(span);
        if ((Object.keys(tree.children).length == 0 && tree.hasOpened) || tree.fileData) {
            // Default: no class
        }
        else if (tree.isOpen) {
            span.classList.add('browser-folder-tree-part-open');
            let subContainer = createDiv(`${this.id}-foldertree-${tree.name}-container`, 'browser-folder-tree-part-container');
            for (let name of tree.childrenKeys) {
                let subTree = tree.children[name];
                this.buildTreeElements(subContainer, `${path}${name}/`, subTree, offset + 16, false);
            }
            container.appendChild(subContainer);
        }
        else {
            span.classList.add('browser-folder-tree-part-closed');
        }
        let matchMe = this.selected || this.folder;
        if (matchMe == path || `${matchMe}/` == path) {
            span.classList.add('browser-folder-tree-part-selected');
        }
        if (tree.fileData) {
            span.onclick = (e) => {
                this.select(tree.fileData, null);
            };
            tree.clickme = (callback) => this.select(tree.fileData, callback);
        }
        else {
            let clicker = (isSymbol, callback) => {
                if (this.folderSelectedEvent) {
                    this.folderSelectedEvent(path);
                }
                tree.hasOpened = true;
                if (isSymbol) {
                    tree.isOpen = !tree.isOpen;
                }
                else {
                    tree.isOpen = true;
                }
                this.navigate(path, callback);
            };
            span.onclick = (e) => clicker(e.target.dataset.issymbol, null);
            tree.clickme = (callback) => clicker(false, callback);
        }
        tree.span = span;
    }

    /**
     * Returns the lowercase searchable text for a browser entry.
     */
    getSearchableText(desc) {
        let searchable = desc.searchable;
        if (searchable == null) {
            return '';
        }
        if (typeof searchable == 'string') {
            return searchable.toLowerCase();
        }
        if (typeof searchable == 'object') {
            if (typeof searchable.allFields == 'string') {
                return searchable.allFields.toLowerCase();
            }
            if (typeof searchable.all_fields == 'string') {
                return searchable.all_fields.toLowerCase();
            }
            return Object.values(searchable).join(' ').toLowerCase();
        }
        return `${searchable}`.toLowerCase();
    }

    /**
     * Checks whether the current filter matches a browser entry.
     */
    filterMatchesEntry(desc) {
        if (!this.filter) {
            return true;
        }
        if (this.filterMatcher) {
            return this.filterMatcher(desc, this.filter);
        }
        return this.getSearchableText(desc).includes(this.filter);
    }

    /**
     * Fills the container with the content list.
     */
    buildContentList(container, files, before = null, startId = 0) {
        let entries = [];
        let requiresDescribedEntries = !!this.filter;
        let allowCachedDescriptions = !(this.filter && (this.filterMatcher || this.filterSorter));
        for (let i = 0; i < files.length; i++) {
            let file = files[i];
            if (file?.file != null) {
                if (file.browserSortIndex == null) {
                    file.browserSortIndex = i;
                }
                entries.push(file);
            }
            else {
                entries.push({ file, desc: null, browserSortIndex: i });
            }
        }
        if (requiresDescribedEntries) {
            for (let entry of entries) {
                if (entry.desc == null) {
                    entry.desc = this.describeEntry(entry.file, allowCachedDescriptions);
                }
            }
            entries = entries.filter(entry => this.filterMatchesEntry(entry.desc));
        }
        if (this.filter && this.filterSorter) {
            let sortedEntries = this.filterSorter(entries, this.filter);
            if (sortedEntries) {
                entries = sortedEntries;
            }
        }
        let id = startId;
        let maxBuildNow = this.maxPreBuild;
        if (startId == 0) {
            maxBuildNow += this.chunksRendered * Math.min(this.maxPreBuild / 2, 100);
            this.chunksRendered = 0;
        }
        else {
            this.chunksRendered++;
        }
        for (let i = 0; i < entries.length; i++) {
            let entry = entries[i];
            let file = entry.file;
            let desc = entry.desc;
            if (desc == null) {
                desc = this.describeEntry(file, allowCachedDescriptions);
                entry.desc = desc;
            }
            id++;
            if (i > maxBuildNow) {
                let remainingEntries = entries.slice(i);
                while (remainingEntries.length > 0) {
                    let chunkSize = Math.min(this.maxPreBuild / 2, remainingEntries.length, 100);
                    let chunk = remainingEntries.splice(0, chunkSize);
                    let sectionDiv = createDiv(null, 'lazyload browser-section-loader');
                    sectionDiv.onclick = () => {
                        this.buildContentList(container, chunk, sectionDiv, id);
                    };
                    container.appendChild(sectionDiv);
                }
                break;
            }
            let div;
            if (this.id == 'imagehistorybrowser' && this.format.includes('Thumbnails')) {
                div = document.createElement('swarm-image-card');
                div.className = desc.className || '';
            }
            else {
                div = createDiv(null, `${desc.className}`);
            }
            if (desc.checkbox) {
                div.classList.add('browser-entry-has-checkbox');
            }
            let popoverId = `${this.id}-${id}`;
            let buttons = (desc.buttons || []).filter(b => !b.multi_only);
            if (buttons.length > 0) {
                let menuDiv = createDiv(`popover_${popoverId}`, 'sui-popover sui_popover_model');
                for (let button of buttons) {
                    let buttonElem;
                    if (button.href) {
                        buttonElem = document.createElement('a');
                        buttonElem.href = button.href;
                        if (button.is_download) {
                            buttonElem.download = '';
                        }
                    }
                    else {
                        buttonElem = document.createElement('div');
                    }
                    buttonElem.className = 'sui_popover_model_button';
                    buttonElem.innerText = button.label;
                    if (button.onclick) {
                        buttonElem.onclick = () => button.onclick(div);
                    }
                    menuDiv.appendChild(buttonElem);
                }
                if (before) {
                    container.insertBefore(menuDiv, before);
                }
                else {
                    container.appendChild(menuDiv);
                }
            }
            let img = document.createElement('img');
            img.loading = 'lazy';
            img.decoding = 'async';
            img.setAttribute('fetchpriority', 'low');
            img.addEventListener('click', () => {
                this.select(file, div);
            });
            img.classList.add('image-block-img-inner');
            div.appendChild(img);
            if (desc.checkbox) {
                let checkboxWrap = createSpan(null, 'browser-entry-checkbox-wrap');
                checkboxWrap.addEventListener('click', (e) => {
                    e.stopPropagation();
                });
                let checkbox = document.createElement('input');
                checkbox.className = 'browser-entry-checkbox';
                checkbox.type = 'checkbox';
                checkbox.checked = !!desc.checkbox.checked;
                checkbox.title = desc.checkbox.title || 'Select';
                checkbox.addEventListener('click', (e) => {
                    e.stopPropagation();
                });
                checkbox.addEventListener('change', (e) => {
                    e.stopPropagation();
                    if (desc.checkbox.onchange) {
                        desc.checkbox.onchange(checkbox.checked, file, div, checkbox);
                    }
                });
                checkboxWrap.appendChild(checkbox);
                div.appendChild(checkboxWrap);
            }
            if (this.format.includes('Cards')) {
                div.className += ' model-block model-block-hoverable';
                if (this.format.startsWith('Small')) { div.classList.add('model-block-small'); }
                else if (this.format.startsWith('Big')) { div.classList.add('model-block-big'); }
                let textBlock = createDiv(null, 'model-descblock');
                textBlock.tabIndex = 0;
                textBlock.innerHTML = desc.description;
                div.appendChild(textBlock);
            }
            else if (this.format.includes('Thumbnails')) {
                div.className += ' image-block image-block-legacy';
                let factor = 8;
                if (this.format.startsWith('Big')) { factor = 15; div.classList.add('image-block-big'); }
                else if (this.format.startsWith('Giant')) { factor = 25; div.classList.add('image-block-giant'); }
                else if (this.format.startsWith('Small')) { factor = 5; div.classList.add('image-block-small'); }
                if (desc.aspectRatio) {
                    div.style.width = `${(desc.aspectRatio * factor) + 1}rem`;
                }
                else {
                    div.style.width = `${factor + 1}rem`;
                    img.addEventListener('load', () => {
                        let ratio = img.naturalWidth / img.naturalHeight;
                        div.style.width = `${(ratio * factor) + 1}rem`;
                    });
                }
                let textBlock = createDiv(null, 'image-preview-text');
                textBlock.innerText = desc.display || desc.name;
                if (this.format == "Small Thumbnails" || textBlock.innerText.length > 40) {
                    textBlock.classList.add('image-preview-text-small');
                }
                else if (textBlock.innerText.length > 20) {
                    textBlock.classList.add('image-preview-text-medium');
                }
                else {
                    textBlock.classList.add('image-preview-text-large');
                }
                div.appendChild(textBlock);
            }
            else if (this.format == 'List') {
                div.className += ' browser-list-entry';
                let textBlock = createSpan(null, 'browser-list-entry-text');
                textBlock.innerText = desc.display || desc.name;
                textBlock.addEventListener('click', () => {
                    this.select(file, div);
                });
                div.appendChild(textBlock);
            }
            else if (this.format == 'Details List') {
                img.style.width = '1.3rem';
                div.className += ' browser-details-list-entry';
                let detail_list = desc.detail_list;
                if (!detail_list) {
                    detail_list = [escapeHtml(desc.display || desc.name), desc.description.replaceAll('<br>', '&emsp;')];
                }
                let percent = 98 / detail_list.length;
                let imgAdj = 1.3 / detail_list.length;
                for (let detail of detail_list) {
                    let textBlock = createSpan(null, 'browser-details-list-entry-text');
                    textBlock.style.width = `calc(${percent}% - ${imgAdj}rem)`;
                    textBlock.innerHTML = detail;
                    textBlock.addEventListener('click', () => {
                        this.select(file, div);
                    });
                    div.appendChild(textBlock);
                }
            }
            if (buttons.length > 0) {
                let menu = createDiv(null, 'model-block-menu-button');
                menu.innerHTML = '&#x2630;';
                menu.addEventListener('click', () => {
                    doPopover(popoverId);
                });
                div.appendChild(menu);
            }
            if (!this.format.includes('Cards')) {
                div.addEventListener('mouseenter', () => div.title = stripHtmlToText(desc.description), { once: true });
            }
            div.dataset.name = file.name;
            if (file.data && file.data.src) {
                div.dataset.src = file.data.src;
            }
            img.classList.add('lazyload');
            img.dataset.src = desc.image;
            img.dataset.origSrc = desc.image;
            if (desc.dragimage) {
                img.addEventListener('dragstart', (e) => {
                    chromeIsDumbFileHack(e.dataTransfer.files[0], desc.dragimage);
                    e.dataTransfer.clearData();
                    e.dataTransfer.setDragImage(img, 0, 0);
                    e.dataTransfer.setData('text/uri-list', desc.dragimage);
                });
            }
            if (before) {
                container.insertBefore(div, before);
            }
            else {
                container.appendChild(div);
            }
        }
        setTimeout(() => {
            browserUtil.queueMakeVisible(container);
        }, 100);
    }

    /**
     * Gets a described browser entry, using the optional cache when enabled.
     */
    describeEntry(file, allowCache = true) {
        if (!allowCache || !this.enableDescriptionCache || !file?.name) {
            return this.describe(file);
        }
        let cached = this.describeCache.get(file.name);
        if (cached) {
            return cached;
        }
        let desc = this.describe(file);
        this.describeCache.set(file.name, desc);
        return desc;
    }

    /**
     * Triggers an immediate in-place rerender of the current browser view.
     */
    rerender() {
        if (this.lastPath != null) {
            this.build(this.lastPath, null, this.lastFiles);
        }
    }

    /**
     * Returns the file object for a given path.
     */
    getFileFor(path) {
        return this.lastFilesMap.get(path);
    }

    /**
     * Returns the visible element block for a given file name.
     */
    getVisibleEntry(name) {
        for (let child of this.contentDiv.children) {
            if (child.dataset.name == name) {
                return child;
            }
        }
        return null;
    }

    /**
     * Computes a simple stable hash for a string.
     */
    hashStringQuick(text) {
        let hash = 0;
        for (let i = 0; i < text.length; i++) {
            hash = ((hash * 31) + text.charCodeAt(i)) >>> 0;
        }
        return hash;
    }

    /**
     * Computes a lightweight signature for an entry list.
     */
    listSignature(list) {
        if (!list) {
            return '0:0';
        }
        let hash = list.length;
        for (let i = 0; i < list.length; i++) {
            let entry = list[i];
            let text = '';
            if (typeof entry == 'string') {
                text = entry;
            }
            else if (entry?.name) {
                text = entry.name;
            }
            else {
                text = `${entry}`;
            }
            hash = ((hash * 131) + this.hashStringQuick(text)) >>> 0;
        }
        return `${list.length}:${hash}`;
    }

    /**
     * Computes a render signature for skip-when-unchanged behavior.
     */
    computeRenderSignature(path, folders, files) {
        return `${path}|${this.format}|${this.filter}|${this.depth}|${this.folderTreeShowFiles}|${this.isSmallWindow}|${this.noContentUpdates}|${this.listSignature(folders)}|${this.listSignature(files)}`;
    }

    /**
     * Central call to build the browser content area.
     */
    build(path, folders, files) {
        this.checkIsSmall();
        if (path.endsWith('/')) {
            path = path.substring(0, path.length - 1);
        }
        let scrollOffset = 0;
        this.lastPath = path;
        if (folders) {
            this.refillTree(path, folders, false);
        }
        else if (folders == null && this.contentDiv) {
            scrollOffset = this.contentDiv.scrollTop;
        }
        if (files == null) {
            files = this.lastFiles;
        }
        let canSkipUnchangedBuild = this.hasGenerated && folders != null && files != null;
        if (canSkipUnchangedBuild) {
            let signature = this.computeRenderSignature(path, folders, files);
            if (signature == this.lastRenderSignature) {
                this.everLoaded = true;
                if (this.builtEvent) {
                    this.builtEvent();
                }
                return;
            }
            this.lastRenderSignature = signature;
        }
        else {
            this.lastRenderSignature = null;
        }
        this.lastFiles = files;
        this.lastFilesMap = new Map();
        if (this.lastFiles) {
            for (let file of this.lastFiles) {
                if (!file || !file.name) {
                    continue;
                }
                this.lastFilesMap.set(file.name, file);
            }
        }
        if (files && this.folderTreeShowFiles) {
            this.refillTree(path, files.map(f => {
                let name = f.name.substring(path.length);
                if (name.startsWith('/')) {
                    name = name.substring(1);
                }
                return name;
            }), true);
        }
        let folderScroll = this.folderTreeDiv ? this.folderTreeDiv.scrollTop : 0;
        if (!this.hasGenerated) {
            this.hasGenerated = true;
            this.container.innerHTML = '';
            this.folderTreeDiv = createDiv(`${this.id}-foldertree`, 'browser-folder-tree-container');
            let folderTreeSplitter = createDiv(`${this.id}-splitter`, 'browser-folder-tree-splitter splitter-bar');
            this.headerBar = createDiv(`${this.id}-header`, 'browser-header-bar');
            this.headerControlRow = createDiv(`${this.id}-header-row`, 'browser-header-control-row');
            this.fullContentDiv = createDiv(`${this.id}-fullcontent`, 'browser-fullcontent-container');
            this.headerBar.appendChild(this.headerControlRow);
            this.container.appendChild(this.folderTreeDiv);
            this.container.appendChild(folderTreeSplitter);
            this.container.appendChild(this.fullContentDiv);
            let formatSelector = document.createElement('select');
            formatSelector.id = `${this.id}-format-selector`;
            formatSelector.title = 'Display format';
            formatSelector.className = 'browser-format-selector';
            for (let format of ['Cards', 'Small Cards', 'Big Cards', 'Thumbnails', 'Small Thumbnails', 'Big Thumbnails', 'Giant Thumbnails', 'List', 'Details List']) {
                let option = document.createElement('option');
                option.value = format;
                option.className = 'translate';
                option.innerText = translate(format);
                if (format == this.format) {
                    option.selected = true;
                }
                formatSelector.appendChild(option);
            }
            formatSelector.addEventListener('change', () => {
                this.format = formatSelector.value;
                localStorage.setItem(`browser_${this.id}_format`, this.format);
                this.update();
            });
            if (!this.showDisplayFormat) {
                formatSelector.style.display = 'none';
            }
            let buttons = createSpan(`${this.id}-button-container`, 'browser-header-buttons', 
                `<button id="${this.id}_refresh_button" title="Refresh" class="refresh-button translate translate-no-text">&#x21BB;</button>\n`
                + `<button id="${this.id}_up_button" class="refresh-button translate translate-no-text" disabled autocomplete="off" title="Go back up 1 folder">&#x21d1;</button>\n`
                + `<span><span class="translate">Depth</span>: <input id="${this.id}_depth_input" class="depth-number-input translate translate-no-text" type="number" min="1" max="10" value="${this.depth}" title="Depth of subfolders to show" autocomplete="off"></span>\n`
                + `<div class="input_filter_container bottom_filter"><input id="${this.id}_filter_input" type="text" value="${this.filter}" title="Text filter, only show items that contain this text." rows="1" autocomplete="off" class="translate translate-no-text" placeholder="${translate('Filter...')}"><span class="clear_input_icon bottom_filter">&#x2715;</span></div>\n`
                + this.extraHeader);
            let inputArr = buttons.getElementsByTagName('input');
            let depthInput = inputArr[0];
            depthInput.addEventListener('change', () => {
                this.depth = depthInput.value;
                localStorage.setItem(`browser_${this.id}_depth`, this.depth);
                this.lightRefresh();
            });
            if (!this.showDepth) {
                depthInput.parentElement.style.display = 'none';
            }
            let clearFilterBtn = buttons.getElementsByClassName('clear_input_icon')[0];
            let filterInput = inputArr[1];
            filterInput.addEventListener('input', () => {
                this.filter = filterInput.value.toLowerCase();
                localStorage.setItem(`browser_${this.id}_filter`, this.filter);
                if (this.filter.length > 0) {
                    clearFilterBtn.style.display = 'block';
                }
                else {
                    clearFilterBtn.style.display = 'none';
                }
                if (this.filterUpdateTimeout) {
                    clearTimeout(this.filterUpdateTimeout);
                }
                let delayMs = this.filterUpdateDelayMs;
                if ((this.lastFiles?.length || 0) > 1000) {
                    delayMs = Math.max(delayMs, 450);
                }
                this.filterUpdateTimeout = setTimeout(() => {
                    this.filterUpdateTimeout = null;
                    let hadFocus = document.activeElement == filterInput;
                    let selectionStart = filterInput.selectionStart;
                    let selectionEnd = filterInput.selectionEnd;
                    this.update(false, () => {
                        if (!hadFocus) {
                            return;
                        }
                        let newFilterInput = document.getElementById(`${this.id}_filter_input`);
                        if (!newFilterInput) {
                            return;
                        }
                        newFilterInput.focus({ preventScroll: true });
                        if (selectionStart != null && selectionEnd != null) {
                            newFilterInput.setSelectionRange(selectionStart, selectionEnd);
                        }
                    });
                }, delayMs);
            });
            if (!this.showFilter) {
                filterInput.parentElement.style.display = 'none';
            }
            clearFilterBtn.addEventListener('click', () => {
                filterInput.value = '';
                filterInput.focus();
                filterInput.dispatchEvent(new Event('input'));
            });
            if (this.filter.length > 0) {
                clearFilterBtn.style.display = 'block';
            }
            let buttonArr = buttons.getElementsByTagName('button');
            let refreshButton = buttonArr[0];
            this.upButton = buttonArr[1];
            if (!this.showRefresh) {
                refreshButton.style.display = 'none';
            }
            if (!this.showUpFolder) {
                this.upButton.style.display = 'none';
            }
            this.headerMoreButton = document.createElement('button');
            this.headerMoreButton.type = 'button';
            this.headerMoreButton.className = 'refresh-button browser-header-more-button';
            this.headerMoreButton.innerText = 'More';
            this.headerMoreButton.style.display = 'none';
            this.headerMoreButton.setAttribute('aria-expanded', 'false');
            this.headerOverflowPopover = createDiv(`${this.id}-header-more-popover`, 'sui-popover sui_popover_model browser-header-more-popover');
            document.body.appendChild(this.headerOverflowPopover);
            let extraElements = [...buttons.children].slice(4);
            this.headerControlGroups = [
                this.createHeaderControlGroup([formatSelector], '', true),
                this.createHeaderControlGroup([refreshButton], '', true),
                this.createHeaderControlGroup([this.upButton], '', true),
                this.createHeaderControlGroup([depthInput.parentElement], '', true),
                this.createHeaderControlGroup([filterInput.parentElement], 'browser-header-control-group-filter', true),
                ...this.flattenHeaderControlElements(extraElements)
            ];
            this.headerPathGroup = this.createHeaderControlGroup([], 'browser-header-control-group-path', true);
            this.headerCountGroup = this.createHeaderControlGroup([], 'browser-header-control-group-count', true);
            this.headerControlGroups.push(this.headerPathGroup, this.headerCountGroup);
            for (let group of this.headerControlGroups) {
                this.headerControlRow.appendChild(group);
            }
            this.headerControlRow.appendChild(this.headerMoreButton);
            this.headerMoreButton.addEventListener('click', (e) => {
                e.preventDefault();
                e.stopPropagation();
                this.toggleHeaderOverflowMenu();
            });
            if (!this.headerOuterClickHandler) {
                this.headerOuterClickHandler = (e) => {
                    if (this.headerOverflowPopover?.contains(e.target) || this.headerMoreButton?.contains(e.target)) {
                        return;
                    }
                    this.closeHeaderOverflowMenu();
                };
                document.addEventListener('mousedown', this.headerOuterClickHandler);
            }
            if (!this.headerResizeHandler) {
                this.headerResizeHandler = () => {
                    this.closeHeaderOverflowMenu();
                    this.queueHeaderLayout();
                };
                window.addEventListener('resize', this.headerResizeHandler);
            }
            if (window.ResizeObserver && !this.headerResizeObserver) {
                this.headerResizeObserver = new ResizeObserver(() => this.queueHeaderLayout());
                this.headerResizeObserver.observe(this.headerBar);
            }
            refreshButton.onclick = this.refresh.bind(this);
            this.fullContentDiv.appendChild(this.headerBar);
            this.contentDiv = createDiv(`${this.id}-content`, 'browser-content-container');
            this.contentDiv.addEventListener('scroll', () => {
                browserUtil.queueMakeVisible(this.contentDiv);
                this.mediaWindowManager?.queueUpdate();
            });
            this.fullContentDiv.appendChild(this.contentDiv);
            this.barSpot = 0;
            let setBar = () => {
                let barSpot = this.barSpot;
                if (this.isSmallWindow) {
                    barSpot = 100; // TODO: Swipeable width
                }
                this.folderTreeDiv.style.width = `${barSpot}px`;
                this.fullContentDiv.style.width = `calc(100% - ${barSpot + 1}px - 0.6rem)`;
                if (this.sizeChangedEvent) {
                    this.sizeChangedEvent();
                }
                this.queueHeaderLayout();
            }
            this.lastReset = () => {
                this.barSpot = parseInt(localStorage.getItem(`barspot_browser_${this.id}`) || convertRemToPixels(20));
                setBar();
            };
            this.lastReset();
            let isDrag = false;
            folderTreeSplitter.addEventListener('mousedown', (e) => {
                e.preventDefault();
                if (this.isSmallWindow) {
                    return;
                }
                isDrag = true;
            }, true);
            this.lastListen = (e) => {
                let offX = e.pageX - this.container.getBoundingClientRect().left;
                offX = Math.min(Math.max(offX, this.splitterMinWidth), window.innerWidth - 100);
                if (isDrag) {
                    this.barSpot = offX - 5;
                    localStorage.setItem(`barspot_browser_${this.id}`, this.barSpot);
                    setBar();
                }
            };
            this.lastListenUp = () => {
                isDrag = false;
            };
            document.addEventListener('mousemove', this.lastListen);
            document.addEventListener('mouseup', this.lastListenUp);
            genTabLayout.layoutResets.push(() => {
                localStorage.removeItem(`barspot_browser_${this.id}`);
                this.lastReset();
            });
        }
        else {
            this.folderTreeDiv.innerHTML = '';
            this.contentDiv.innerHTML = '';
            this.headerPath.remove();
            this.headerCount.remove();
        }
        this.headerPath = this.genPath(path, this.upButton);
        this.headerCount = createSpan(null, 'browser-header-count');
        this.headerCount.innerText = files.length;
        this.headerPathGroup.appendChild(this.headerPath);
        this.headerCountGroup.appendChild(this.headerCount);
        this.buildTreeElements(this.folderTreeDiv, '', this.tree);
        applyTranslations(this.headerBar);
        this.queueHeaderLayout();
        if (!this.noContentUpdates) {
            this.buildContentList(this.contentDiv, files);
            this.mediaWindowManager?.attach(this.contentDiv);
            browserUtil.queueMakeVisible(this.contentDiv);
            if (scrollOffset) {
                this.contentDiv.scrollTop = scrollOffset;
            }
            this.mediaWindowManager?.queueUpdate();
            applyTranslations(this.contentDiv);
        }
        if (folderScroll) {
            this.folderTreeDiv.scrollTop = folderScroll;
        }
        this.everLoaded = true;
        if (this.builtEvent) {
            this.builtEvent();
        }
    }
}
