/** Data about a tab within the Generate UI that can be moved to different containers. */
class MovableGenTab {
    constructor(navLink, handler) {
        this.handler = handler;
        this.navElem = navLink;
        this.id = this.navElem.getAttribute('href').substring(1);
        this.contentElem = getRequiredElementById(this.id);
        this.title = this.navElem.innerText;
        this.defaultGroup = findParentOfClass(this.navElem, 'swarm-gen-tab-subnav');
        this.currentGroup = this.defaultGroup;
        this.targetGroupId = getCookie(`tabloc_${this.id}`) || this.defaultGroup.id;
        this.visible = true;
        this.navElem.removeAttribute('data-bs-toggle');
        this.navElem.addEventListener('click', this.clickOn.bind(this));
    }

    /** Alternate click handler for tabs, as bootstrap click handler gets confused. */
    clickOn(e) {
        e.preventDefault();
        this.setSelected();
        for (let tab of this.handler.managedTabs.filter(t => t.currentGroup.id == this.currentGroup.id && t.id != this.id)) {
            tab.setNotSelected();
        }
        setTimeout(() => {
            this.handler.reapplyPositions();
        }, 1);
    }

    /** Marks this tab as not selected, visually hiding it. */
    setNotSelected() {
        this.navElem.classList.remove('active');
        this.contentElem.classList.remove('active');
        this.contentElem.classList.remove('show');
    }

    /** Marks this tab as currently selected, visually hiding it. */
    setSelected() {
        this.navElem.classList.add('active');
        this.contentElem.classList.add('active');
        this.contentElem.classList.add('show');
    }

    /** Click a different entry in the current group, to deselect this. */
    clickOther() {
        let nextTab = this.navElem.parentElement.nextElementSibling || this.navElem.parentElement.previousElementSibling;
        if (nextTab) {
            nextTab.querySelector('.nav-link').click();
        }
    }

    /** Triggers an update, moving this to where it's meant to be. */
    update() {
        if (this.targetGroupId != this.currentGroup.id) {
            if (this.visible && this.navElem.classList.contains('active')) {
                this.clickOther();
                this.setNotSelected();
            }
            let targetGroup = document.getElementById(this.targetGroupId) || this.defaultGroup;
            if (!targetGroup) {
                return;
            }
            this.currentGroup = targetGroup;
            this.currentGroup.appendChild(this.navElem.parentElement);
            let newContentContainer = getRequiredElementById(this.currentGroup.dataset.content);
            newContentContainer.appendChild(this.contentElem);
            if (this.visible && [... this.currentGroup.querySelectorAll('.nav-link')].length == 1) {
                this.navElem.click();
            }
        }
        if (this.targetGroupId != this.defaultGroup.id) {
            setCookie(`tabloc_${this.id}`, this.targetGroupId, 365);
        }
        else {
            deleteCookie(`tabloc_${this.id}`);
        }
        if (!this.visible && this.navElem.classList.contains('active')) {
            this.clickOther();
            this.setNotSelected();
        }
        this.navElem.style.display = this.visible ? '' : 'none';
        this.contentElem.style.display = this.visible ? '' : 'none';
    }
}

/** Central handler for generate main tab layout logic. */
class GenTabLayout {

    /** List of functions to run when the layout is reset to default. This should remove any variables in browser storage related to layout. */
    layoutResets = [];
    
    /** Whether the left section should be shut. */
    leftShut = localStorage.getItem('barspot_leftShut') == 'true';

    /** Whether the bottom section should be shut. */
    bottomShut = localStorage.getItem('barspot_midForceToBottom') == 'true';

    /** Position of the image-editor alignment bar (the split between image editor and output area). -1 if unset. */
    imageEditorBarPos = parseInt(getCookie('barspot_imageEditorSizeBar') || '-1');
    
    /** Position of the left section bar. -1 if unset. */
    leftSectionBarPos = parseInt(getCookie('barspot_pageBarTop') || '-1');

    /** Position of the right section bar. -1 if unset. */
    rightSectionBarPos = parseInt(getCookie('barspot_pageBarTop2') || '-1');

    /** Position of the bottom section bar. -1 if unset. */
    bottomSectionBarPos = parseInt(getCookie('barspot_pageBarMidPx') || '-1');

    /** Tabs to hide. */
    hideTabs = (getCookie('layout_hidetabs') || '').split(',');

    /** Layout to use as mobile/desktop/auto. */
    mobileDesktopLayout = localStorage.getItem('layout_mobileDesktop') || 'auto';

    constructor() {
        this.leftSplitBar = getRequiredElementById('t2i-top-split-bar');
        this.rightSplitBar = getRequiredElementById('t2i-top-2nd-split-bar');
        this.leftSplitBarButton = getRequiredElementById('t2i-top-split-quickbutton');
        this.bottomSplitBar = getRequiredElementById('t2i-mid-split-bar');
        this.bottomSplitBarButton = getRequiredElementById('t2i-mid-split-quickbutton');
        this.topSection = getRequiredElementById('t2i_top_bar');
        this.bottomInfoBar = getRequiredElementById('bottom_info_bar');
        this.bottomBar = getRequiredElementById('t2i_bottom_bar');
        this.inputSidebar = getRequiredElementById('input_sidebar');
        this.mainImageArea = getRequiredElementById('main_image_area');
        this.currentImage = getRequiredElementById('current_image');
        this.mainInputsArea = getRequiredElementById('main_inputs_area_wrapper');
        this.currentImageWrapbox = getRequiredElementById('current_image_wrapbox');
        this.currentImageBatch = getRequiredElementById('current_image_batch_wrapper');
        this.currentImageBatchCore = getRequiredElementById('current_image_batch');
        this.altRegion = getRequiredElementById('alt_prompt_region');
        this.altText = getRequiredElementById('alt_prompt_textbox');
        this.altNegText = getRequiredElementById('alt_negativeprompt_textbox');
        this.altImageRegion = getRequiredElementById('alt_prompt_extra_area');
        this.editorSizebar = getRequiredElementById('image_editor_sizebar');
        this.tabCollections = document.querySelectorAll('.swarm-gen-tab-subnav');
        this.layoutConfigArea = document.getElementById('layoutconfigarea');
        this.toolContainer = getRequiredElementById('tool_container');
        this.t2iRootDiv = getRequiredElementById('Text2Image');
        this.t2iRootDiv.classList.add('text2image-tab');
        this.quickToolsButton = getRequiredElementById('quicktools-button');
        this.managedTabs = [...this.tabCollections].flatMap(e => [...e.querySelectorAll('.nav-link')]).map(e => new MovableGenTab(e, this));
        this.managedTabContainers = [];
        this.leftBarDrag = false;
        this.rightBarDrag = false;
        this.bottomBarDrag = false;
        this.imageEditorSizeBarDrag = false;
        this.isSmallWindow = this.mobileDesktopLayout == 'auto' ? window.innerWidth < 768 : this.mobileDesktopLayout == 'mobile';
        this.antiDup = false;
        this.swipeStartX = -1;
        this.swipeStartY = -1;
        this.minSwipeDelta = Math.min(100, window.innerWidth * 0.4);
        this.promptLayoutFrame = null;
        this.queuedReapplyFrame = null;
        this.promptInputTimers = {};
        /** Active follow-finger mobile panel drag target, or null. */
        this.mobileDragPanel = null;
        /** Whether a mobile panel drag has locked onto an axis. */
        this.mobileDragActive = false;
        /** Page X where the current mobile drag started. */
        this.mobileDragStartX = 0;
        /** Page Y where the current mobile drag started. */
        this.mobileDragStartY = 0;
        /** Whether the drag is opening (true) or closing (false) a panel. */
        this.mobileDragOpening = false;
        /** Scroll container that must be at its upper boundary before a bottom-panel close drag can start. */
        this.mobileDragScrollElement = null;
        /** Whether the active bottom-panel gesture candidate requires a scroll-boundary check. */
        this.mobileDragRequiresBoundary = false;
        /** Whether a follow-finger drag has temporarily replaced the bottom bar's canonical height. */
        this.mobileDragAdjustedBottomHeight = false;
        /** Scrim element behind mobile overlays. */
        this.mobileScrim = null;
        /** Ignore synthetic/ghost clicks until this timestamp (iOS tap-dismiss). */
        this.mobileIgnoreClicksUntil = 0;
        /** Left edge swipe-hint button. */
        this.mobileHintLeft = null;
        /** Right edge swipe-hint button. */
        this.mobileHintRight = null;
        /** Whether the mobile prompt textboxes currently have focus. */
        this.mobilePromptFocused = false;
        /** How many px the mobile top tab bar is currently collapsed by. */
        this.mobileTopbarCollapsePx = 0;
        /** True while a finger is driving topbar collapse. */
        this.mobileTopbarDragging = false;
        /** Collapse px when the current topbar finger-drag began. */
        this.mobileTopbarDragFrom = 0;
        /** Whether the current touch may drive topbar collapse. */
        this.mobileTopbarCanDrag = false;
        /** Transient mobile input-panel state, independent from persisted desktop split state. */
        this.mobileLeftOpen = false;
        /** Transient mobile batch-panel state, independent from persisted desktop split state. */
        this.mobileRightOpen = false;
        /** Transient mobile bottom-panel state, independent from persisted desktop split state. */
        this.mobileBottomOpen = false;
        /** Transient mobile image-editor split percentage, independent from the persisted desktop split. */
        this.mobileImageEditorBarPos = 50;
        /** Responsive mode applied during the previous layout pass, or null before first layout. */
        this.lastAppliedSmallWindow = null;
    }

    /** Height used for Generate layout geometry. */
    getViewportHeight() {
        return window.innerHeight;
    }

    /** Soft-keyboard overlap below the visual viewport, in px. */
    getKeyboardInset() {
        if (!this.isSmallWindow || !this.mobilePromptFocused || !window.visualViewport) {
            return 0;
        }
        let inset = Math.max(0, Math.round(window.innerHeight - (window.visualViewport.height + window.visualViewport.offsetTop)));
        if (inset < 60) {
            return 0;
        }
        return inset;
    }

    /** Syncs keyboard-related body classes and CSS vars. Returns true if the bottom peek should hide. */
    syncMobileKeyboardState() {
        let inset = this.getKeyboardInset();
        let vvShrank = window.visualViewport
            && (window.innerHeight - window.visualViewport.height) > 100;
        let hidePeek = this.isSmallWindow && (this.mobilePromptFocused || vvShrank);
        document.documentElement.style.setProperty('--mobile-keyboard-inset', `${inset}px`);
        document.body.classList.toggle('mobile-keyboard-open', hidePeek);
        document.body.classList.toggle('mobile-keyboard-pin', inset > 0);
        return hidePeek;
    }

    /** Whether the mobile left (inputs) overlay should be open. */
    isMobileLeftOpen() {
        return this.mobileLeftOpen;
    }

    /** Whether the mobile right (batch) overlay should be open. */
    isMobileRightOpen() {
        return this.mobileRightOpen;
    }

    /** Whether the mobile bottom overlay should be open. */
    isMobileBottomOpen() {
        return this.mobileBottomOpen;
    }

    /** Whether no mobile overlays are open. */
    areAllMobilePanelsShut() {
        return !this.mobileLeftOpen && !this.mobileRightOpen && !this.mobileBottomOpen;
    }

    /** Returns the DOM element for a mobile panel id. */
    getMobilePanelElem(which) {
        if (which == 'left') {
            return this.inputSidebar;
        }
        if (which == 'right') {
            return this.currentImageBatch;
        }
        if (which == 'bottom') {
            return this.bottomBar;
        }
        return null;
    }

    /** Clears follow-finger inline transforms from mobile panels. */
    clearMobileDragTransforms() {
        for (let which of ['left', 'right', 'bottom']) {
            let elem = this.getMobilePanelElem(which);
            if (elem) {
                elem.style.transform = '';
            }
        }
        if (this.mobileScrim) {
            this.mobileScrim.style.opacity = '';
        }
    }

    /** Restores canonical mobile panel styling after a completed or canceled touch gesture. */
    resetMobileDragState(reapply = false) {
        let mustRestoreHeight = this.mobileDragAdjustedBottomHeight;
        this.mobileDragActive = false;
        this.mobileDragPanel = null;
        this.mobileDragOpening = false;
        this.mobileDragScrollElement = null;
        this.mobileDragRequiresBoundary = false;
        this.mobileDragAdjustedBottomHeight = false;
        this.mobileTopbarCanDrag = false;
        this.mobileTopbarDragging = false;
        this.swipeStartX = -1;
        this.swipeStartY = -1;
        document.body.classList.remove('mobile-panel-dragging');
        this.clearMobileDragTransforms();
        if (mustRestoreHeight) {
            this.bottomBar.style.height = '';
        }
        if (reapply && this.isSmallWindow) {
            this.reapplyPositions();
        }
    }

    /** Initializes transient overlay state when crossing between desktop and mobile layout modes. */
    applyResponsiveModeTransition() {
        if (this.lastAppliedSmallWindow == this.isSmallWindow) {
            return;
        }
        this.mobileLeftOpen = false;
        this.mobileRightOpen = false;
        this.mobileBottomOpen = false;
        this.mobileImageEditorBarPos = 50;
        this.leftBarDrag = false;
        this.rightBarDrag = false;
        this.bottomBarDrag = false;
        this.imageEditorSizeBarDrag = false;
        this.resetMobileDragState(false);
        if (!this.isSmallWindow) {
            this.clearMobileInlineStyles();
        }
        this.lastAppliedSmallWindow = this.isSmallWindow;
    }

    /** Clears mobile overlay state when leaving small-window mode. */
    clearMobileInlineStyles() {
        this.clearMobileDragTransforms();
        this.bottomBar.style.height = '';
        this.altRegion.style.visibility = '';
        document.documentElement.style.removeProperty('--mobile-keyboard-inset');
        document.documentElement.style.removeProperty('--mobile-topbar-collapse');
        document.body.classList.remove('mobile-panel-left-open', 'mobile-panel-right-open', 'mobile-panel-bottom-open', 'mobile-panels-all-shut', 'mobile-panel-dragging', 'mobile-keyboard-pin', 'mobile-keyboard-open');
        this.mobileTopbarCollapsePx = 0;
        this.mobileTopbarDragging = false;
    }

    /** Progressive mobile top-tab collapse (0 = open). Follows finger / scroll. */
    setMobileTopbarCollapse(px) {
        if (!this.isSmallWindow) {
            px = 0;
        }
        let tabs = getRequiredElementById('toptablist');
        let max = Math.max(0, (tabs.scrollHeight || tabs.offsetHeight) - 10);
        this.mobileTopbarCollapsePx = Math.max(0, Math.min(max, px));
        document.documentElement.style.setProperty('--mobile-topbar-collapse', `${this.mobileTopbarCollapsePx}px`);
        let rootTop = this.t2iRootDiv.getBoundingClientRect().top;
        this.quickToolsButton.style.top = `${Math.max(2, rootTop - 12)}px`;
        this.quickToolsButton.style.right = '0.5rem';
        let viewH = this.getViewportHeight();
        let bottomPeek = document.body.classList.contains('mobile-keyboard-open') ? 0 : this.getMobileBottomPeekPx();
        let topHeight = Math.max(120, viewH - rootTop - bottomPeek);
        this.mainImageArea.style.height = `${topHeight}px`;
        this.topSection.style.height = `${topHeight}px`;
        let altHeight = this.altRegion.style.display == 'none' || this.isMobileBottomOpen() ? 0 : this.altRegion.offsetHeight;
        this.currentImageWrapbox.style.height = `calc(${topHeight}px - ${altHeight}px)`;
        this.editorSizebar.style.height = `calc(${topHeight}px - ${altHeight}px)`;
    }

    /** Fully hides the mobile top tab bar (leaves the Quick Tools peek). */
    hideMobileTopbar() {
        this.setMobileTopbarCollapse(1e9);
    }

    /** Syncs body classes that drive mobile overlay CSS. */
    syncMobilePanelClasses() {
        if (!this.isSmallWindow) {
            document.body.classList.remove('mobile-panel-left-open', 'mobile-panel-right-open', 'mobile-panel-bottom-open', 'mobile-panels-all-shut', 'mobile-panel-dragging');
            return;
        }
        document.body.classList.toggle('mobile-panel-left-open', this.isMobileLeftOpen());
        document.body.classList.toggle('mobile-panel-right-open', this.isMobileRightOpen());
        document.body.classList.toggle('mobile-panel-bottom-open', this.isMobileBottomOpen());
        document.body.classList.toggle('mobile-panels-all-shut', this.areAllMobilePanelsShut());
    }

    /** Opens a mobile overlay panel, closing the others. */
    openMobilePanel(which) {
        this.hideMobileTopbar();
        this.mobileLeftOpen = which == 'left';
        this.mobileRightOpen = which == 'right';
        this.mobileBottomOpen = which == 'bottom';
        this.clearMobileDragTransforms();
        this.syncMobilePanelClasses();
        this.reapplyPositions();
    }

    /** Closes a mobile overlay panel (or all if which is null). */
    closeMobilePanel(which = null) {
        if (which == null || which == 'left') {
            this.mobileLeftOpen = false;
        }
        if (which == null || which == 'right') {
            this.mobileRightOpen = false;
        }
        if (which == null || which == 'bottom') {
            this.mobileBottomOpen = false;
        }
        this.clearMobileDragTransforms();
        this.syncMobilePanelClasses();
        this.reapplyPositions();
    }

    /** Closes every mobile overlay panel. */
    closeAllMobilePanels() {
        this.closeMobilePanel(null);
    }

    /** Which mobile overlay is open, if any. */
    getOpenMobilePanel() {
        if (this.isMobileBottomOpen()) {
            return 'bottom';
        }
        if (this.isMobileLeftOpen()) {
            return 'left';
        }
        if (this.isMobileRightOpen()) {
            return 'right';
        }
        return null;
    }

    /** Dismiss the current mobile overlay (same path as a completed swipe-close). */
    dismissMobileOverlayFromScrim() {
        let which = this.getOpenMobilePanel();
        if (which) {
            this.closeMobilePanel(which);
        }
        else {
            this.closeAllMobilePanels();
        }
    }

    /** Applies a follow-finger transform for the active mobile drag. */
    applyMobileDragTransform(pageX, pageY) {
        let elem = this.getMobilePanelElem(this.mobileDragPanel);
        if (!elem) {
            return;
        }
        let deltaX = pageX - this.mobileDragStartX;
        let deltaY = pageY - this.mobileDragStartY;
        let width = window.innerWidth;
        let height = this.getViewportHeight();
        let progress = 0;
        if (this.mobileDragPanel == 'left') {
            if (this.mobileDragOpening) {
                let x = Math.min(0, -width + Math.max(0, deltaX));
                elem.style.transform = `translateX(${x}px)`;
                progress = Math.min(1, Math.max(0, (deltaX) / width));
            }
            else {
                let x = Math.max(-width, Math.min(0, deltaX));
                elem.style.transform = `translateX(${x}px)`;
                progress = Math.min(1, Math.max(0, 1 + (x / width)));
            }
        }
        else if (this.mobileDragPanel == 'right') {
            if (this.mobileDragOpening) {
                let x = Math.max(0, width + Math.min(0, deltaX));
                elem.style.transform = `translateX(${x}px)`;
                progress = Math.min(1, Math.max(0, (-deltaX) / width));
            }
            else {
                let x = Math.min(width, Math.max(0, deltaX));
                elem.style.transform = `translateX(${x}px)`;
                progress = Math.min(1, Math.max(0, 1 - (x / width)));
            }
        }
        else if (this.mobileDragPanel == 'bottom') {
            let peek = this.getMobileBottomPeekPx();
            let dismissGap = Math.min(52, Math.round(height * 0.07));
            let full = Math.max(200, height - this.t2iRootDiv.getBoundingClientRect().top - dismissGap);
            let travel = Math.max(1, full - peek);
            let h;
            if (this.mobileDragOpening) {
                h = Math.min(full, Math.max(peek, peek - deltaY));
                progress = Math.min(1, Math.max(0, (h - peek) / travel));
            }
            else {
                h = Math.min(full, Math.max(peek, full - deltaY));
                progress = Math.min(1, Math.max(0, (h - peek) / travel));
            }
            elem.style.transform = '';
            elem.style.height = `${h}px`;
            this.mobileDragAdjustedBottomHeight = true;
        }
        if (this.mobileScrim) {
            this.mobileScrim.style.opacity = `${Math.min(0.45, 0.15 + progress * 0.3)}`;
        }
    }

    /** Pixel height of the bottom info+tabs peek when the bottom panel is shut. */
    getMobileBottomPeekPx() {
        let infoH = this.bottomInfoBar ? this.bottomInfoBar.offsetHeight : 0;
        let tabs = getRequiredElementById('bottombartabcollection');
        let tabsH = tabs.getBoundingClientRect().height;
        if (!tabsH || tabsH < 24) {
            tabsH = 2.75 * parseFloat(getComputedStyle(document.documentElement).fontSize);
        }
        let safeBottom = parseFloat(getComputedStyle(this.bottomBar).paddingBottom) || 0;
        return Math.ceil(infoH + tabsH + safeBottom + 4);
    }

    /** True if a touch target is inside a scrollable mobile content area (not an edge gesture). */
    isMobileScrollableTouchTarget(target) {
        if (!target || !target.closest) {
            return false;
        }
        if (target.closest('textarea, input, select, button, a, .nav-link, .basic-button, .interrupt-button, .model-block, .sui-popover, .mobile-layout-scrim, .mobile-edge-hint, #image_fullview_modal, #image_compare_modal')) {
            return true;
        }
        if (target.closest('.main_inputs_area_wrapper, .current_image_batch_core, .browser-content-container, .browser_container, .scroll-within-tab')) {
            return true;
        }
        return false;
    }

    /** Finds the nearest vertically scrollable ancestor within an open mobile panel. */
    getMobileScrollableAncestor(target) {
        if (!(target instanceof Element)) {
            return null;
        }
        let panel = this.getMobilePanelElem(this.getOpenMobilePanel());
        for (let element = target; element && element != panel; element = element.parentElement) {
            let style = getComputedStyle(element);
            let canScroll = ['auto', 'scroll', 'overlay'].includes(style.overflowY) && element.scrollHeight > element.clientHeight + 1;
            if (canScroll) {
                return element;
            }
        }
        return null;
    }

    /** True if a touch is on a splitter/drag handle that must not start a panel gesture. */
    isMobileSplitterTouchTarget(target) {
        return !!(target && target.closest && target.closest('.splitter-bar, .browser-folder-tree-splitter'));
    }

    /** Determines which mobile panel gesture (if any) a touch should start. */
    getMobileGestureForTouch(startX, startY, target) {
        if (this.isMobileSplitterTouchTarget(target)) {
            return null;
        }
        let width = window.innerWidth;
        let height = this.getViewportHeight();
        let edgeX = width / 6;
        let edgeY = height / 6;
        let allShut = this.areAllMobilePanelsShut();
        let inScrollable = this.isMobileScrollableTouchTarget(target);
        if (this.isMobileLeftOpen()) {
            return { panel: 'left', opening: false };
        }
        if (this.isMobileRightOpen()) {
            return { panel: 'right', opening: false };
        }
        if (this.isMobileBottomOpen()) {
            let onHandle = !!(target && target.closest && target.closest('.bottom-info-bar-container, .t2i-bottom-bar > .swarm-gen-tab-subnav'));
            let scrollElement = onHandle ? null : this.getMobileScrollableAncestor(target);
            return { panel: 'bottom', opening: false, scrollElement, requiresBoundary: !!scrollElement };
        }
        if (!allShut) {
            return null;
        }
        if (startX < edgeX && (!inScrollable || startX < 24)) {
            return { panel: 'left', opening: true };
        }
        if (startX > width - edgeX && (!inScrollable || startX > width - 24)) {
            return { panel: 'right', opening: true };
        }
        if (startY > height - edgeY && (!inScrollable || startY > height - 24)) {
            return { panel: 'bottom', opening: true };
        }
        return null;
    }

    /** Completes or cancels the current mobile follow-finger drag. */
    finishMobileDrag(pageX, pageY) {
        if (!this.mobileDragActive || !this.mobileDragPanel) {
            this.resetMobileDragState(true);
            return;
        }
        let deltaX = pageX - this.mobileDragStartX;
        let deltaY = pageY - this.mobileDragStartY;
        let panel = this.mobileDragPanel;
        let opening = this.mobileDragOpening;
        let commit = false;
        if (panel == 'left') {
            commit = opening ? deltaX > this.minSwipeDelta : deltaX < -this.minSwipeDelta;
        }
        else if (panel == 'right') {
            commit = opening ? deltaX < -this.minSwipeDelta : deltaX > this.minSwipeDelta;
        }
        else if (panel == 'bottom') {
            commit = opening ? deltaY < -this.minSwipeDelta : deltaY > this.minSwipeDelta;
        }
        this.resetMobileDragState(false);
        if (commit) {
            if (opening) {
                this.openMobilePanel(panel);
            }
            else {
                this.closeMobilePanel(panel);
            }
        }
        else {
            this.syncMobilePanelClasses();
            this.reapplyPositions();
        }
    }

    /** Builds mobile-only overlay helpers (scrim + edge hints). */
    initMobileOverlayUi() {
        let host = this.t2iRootDiv;
        this.mobileScrim = createDiv('mobile_layout_scrim', 'mobile-layout-scrim');
        this.mobileScrim.addEventListener('touchend', (e) => {
            if (!this.isSmallWindow || e.changedTouches.length != 1) {
                return;
            }
            this.mobileIgnoreClicksUntil = Date.now() + 500;
            this.resetMobileDragState(false);
            if (e.cancelable) {
                e.preventDefault();
            }
            e.stopPropagation();
            this.dismissMobileOverlayFromScrim();
        }, { passive: false });
        this.mobileScrim.addEventListener('click', (e) => {
            if (Date.now() < this.mobileIgnoreClicksUntil) {
                e.preventDefault();
                e.stopPropagation();
                return;
            }
            this.dismissMobileOverlayFromScrim();
        });
        document.addEventListener('click', (e) => {
            if (Date.now() < this.mobileIgnoreClicksUntil) {
                e.preventDefault();
                e.stopPropagation();
                e.stopImmediatePropagation();
            }
        }, true);
        host.appendChild(this.mobileScrim);
        this.mobileHintLeft = createDiv('mobile_edge_hint_left', 'mobile-edge-hint mobile-edge-hint-left', '&#x2039;');
        this.mobileHintRight = createDiv('mobile_edge_hint_right', 'mobile-edge-hint mobile-edge-hint-right', '&#x203A;');
        this.mobileHintLeft.title = 'Swipe right for inputs';
        this.mobileHintRight.title = 'Swipe left for batch';
        this.mobileHintLeft.addEventListener('click', () => this.openMobilePanel('left'));
        this.mobileHintRight.addEventListener('click', () => this.openMobilePanel('right'));
        host.appendChild(this.mobileHintLeft);
        host.appendChild(this.mobileHintRight);
        document.addEventListener('scroll', (e) => {
            if (!this.isSmallWindow || this.mobileTopbarDragging || document.body.classList.contains('modal-open')) {
                return;
            }
            let el = e.target;
            if (!(el instanceof Element) || !el.matches('.main_inputs_area_wrapper, .current_image_batch_core, .browser-content-container, .browser_container, .scroll-within-tab')) {
                return;
            }
            this.setMobileTopbarCollapse(el.scrollTop);
        }, true);
        document.addEventListener('focusin', (e) => {
            if (!this.isSmallWindow || !e.target || !e.target.closest) {
                return;
            }
            if (e.target.closest('#toptablist')) {
                return;
            }
            if (e.target.matches('input, textarea, select') || e.target.isContentEditable) {
                this.hideMobileTopbar();
            }
        });
    }

    /** Resets the entire page layout to default, and removes all stored browser layout state info. */
    resetLayout() {
        for (let localStore of Object.keys(localStorage).filter(k => k.startsWith('barspot_'))) {
            localStorage.removeItem(localStore);
        }
        for (let cookie of ['barspot_pageBarTop', 'barspot_pageBarTop2', 'barspot_pageBarMidPx', 'barspot_imageEditorSizeBar']) {
            deleteCookie(cookie);
        }
        this.leftSectionBarPos = -1;
        this.rightSectionBarPos = -1;
        this.bottomSectionBarPos = -1;
        this.imageEditorBarPos = -1;
        this.bottomShut = false;
        this.leftShut = false;
        this.mobileLeftOpen = false;
        this.mobileRightOpen = false;
        this.mobileBottomOpen = false;
        this.mobileImageEditorBarPos = 50;
        this.reapplyPositions();
        for (let runnable of this.layoutResets) {
            runnable();
        }
    }

    /** Sets whether the bottom section should be shut (does not trigger rerendering). */
    setBottomShut(val) {
        if (this.isSmallWindow) {
            this.mobileBottomOpen = !val;
            return;
        }
        this.bottomShut = val;
        localStorage.setItem('barspot_midForceToBottom', `${this.bottomShut}`);
    }

    /** Sets whether the left section should be shut (does not trigger rerendering). */
    setLeftShut(val) {
        if (this.isSmallWindow) {
            this.mobileLeftOpen = !val;
            return;
        }
        this.leftShut = val;
        localStorage.setItem('barspot_leftShut', `${this.leftShut}`);
    }
    
    /** Signal a possible update to the size of the prompt box. */
    altPromptSizeHandle() {
        if (!this.isSmallWindow) {
            this.altRegion.style.top = `calc(-${this.altText.offsetHeight + this.altNegText.offsetHeight + this.altImageRegion.offsetHeight}px - 1rem - 7px)`;
        }
        if (!this.antiDup) {
            this.antiDup = true;
            this.reapplyPositions();
            setTimeout(() => {
                this.antiDup = false;
            }, 1);
        }
    }

    scheduleReapplyPositions() {
        if (this.queuedReapplyFrame) {
            return;
        }
        this.queuedReapplyFrame = requestAnimationFrame(() => {
            this.queuedReapplyFrame = null;
            this.reapplyPositions();
        });
    }

    schedulePromptLayoutUpdate() {
        if (this.promptLayoutFrame) {
            return;
        }
        this.promptLayoutFrame = requestAnimationFrame(() => {
            this.promptLayoutFrame = null;
            let priorHeight = this.altText.offsetHeight + this.altNegText.offsetHeight;
            dynamicSizeTextBox(this.altText);
            dynamicSizeTextBox(this.altNegText);
            this.altRegion.style.top = `calc(-${this.altText.offsetHeight + this.altNegText.offsetHeight + this.altImageRegion.offsetHeight}px - 1rem - 7px)`;
            let nextHeight = this.altText.offsetHeight + this.altNegText.offsetHeight;
            if (nextHeight != priorHeight) {
                this.scheduleReapplyPositions();
            }
        });
    }

    schedulePromptHeavyInputWork(key, runnable, immediate = false) {
        if (this.promptInputTimers[key]) {
            clearTimeout(this.promptInputTimers[key]);
            delete this.promptInputTimers[key];
        }
        if (immediate) {
            runnable();
            return;
        }
        this.promptInputTimers[key] = setTimeout(() => {
            delete this.promptInputTimers[key];
            runnable();
        }, 80);
    }
    
    /** Does the full position update logic. */
    reapplyPositions() {
        this.isSmallWindow = this.mobileDesktopLayout == 'auto' ? window.innerWidth < 768 : this.mobileDesktopLayout == 'mobile';
        this.applyResponsiveModeTransition();
        this.minSwipeDelta = Math.min(100, window.innerWidth * 0.4);
        if (this.isSmallWindow) {
            document.body.classList.add('small-window');
            document.body.classList.remove('large-window');
        }
        else {
            document.body.classList.remove('small-window');
            document.body.classList.add('large-window');
            this.clearMobileInlineStyles();
        }
        fixTabHeights();
        tweakNegativePromptBox();
        if (this.altRegion.style.display != 'none') {
            dynamicSizeTextBox(this.altText);
            dynamicSizeTextBox(this.altNegText);
            if (!this.isSmallWindow) {
                this.altRegion.style.top = `calc(-${this.altText.offsetHeight + this.altNegText.offsetHeight + this.altImageRegion.offsetHeight}px - 1rem - 7px)`;
            }
        }
        if (this.isSmallWindow) {
            this.setMobileTopbarCollapse(this.mobileTopbarCollapsePx);
        }
        let rootTop = this.t2iRootDiv.getBoundingClientRect().top;
        let bottomShut = this.bottomShut;
        let leftShut = this.leftShut;
        let viewH = this.getViewportHeight();
        if (!this.isSmallWindow) {
            this.quickToolsButton.style.top = `${rootTop - 18}px`;
            this.quickToolsButton.style.right = '';
        }
        if (!this.isSmallWindow) {
            setCookie('barspot_pageBarTop', this.leftSectionBarPos, 365);
            setCookie('barspot_pageBarTop2', this.rightSectionBarPos, 365);
            setCookie('barspot_pageBarMidPx', this.bottomSectionBarPos, 365);
            setCookie('barspot_imageEditorSizeBar', this.imageEditorBarPos, 365);
        }
        this.toolContainer.style.minHeight = `calc(100% - ${this.toolContainer.getBoundingClientRect().top - this.toolContainer.parentElement.getBoundingClientRect().top}px - 1.5rem)`;
        this.leftSplitBarButton.innerHTML = leftShut ? '&#x21DB;' : '&#x21DA;';
        this.bottomSplitBarButton.innerHTML = bottomShut ? '&#x290A;' : '&#x290B;';
        if (this.isSmallWindow) {
            this.reapplyMobilePositions(rootTop, viewH);
        }
        else {
            this.reapplyDesktopPositions(rootTop, leftShut, bottomShut);
        }
        if (window.imageEditor) {
            window.imageEditor.resize();
        }
        alignImageDataFormat();
        for (let collection of this.tabCollections) {
            collection.style.display = [...collection.querySelectorAll('.nav-link')].length > 1 ? '' : 'none';
        }
        for (let container of this.managedTabContainers) {
            let parent = container.parentElement;
            let offset = container.getBoundingClientRect().top - parent.getBoundingClientRect().top;
            container.style.height = `calc(100% - ${offset}px)`;
        }
        for (let tab of this.managedTabs) {
            if (!tab.visible || !tab.navElem.classList.contains('active')) {
                continue;
            }
            browserUtil.queueMakeVisible(tab.contentElem);
        }
    }

    /** Mobile overlay positioning path (small-window only). */
    reapplyMobilePositions(rootTop, viewH) {
        let hideBottomPeek = this.syncMobileKeyboardState();
        let peek = hideBottomPeek ? 0 : this.getMobileBottomPeekPx();
        let topHeight = Math.max(120, viewH - rootTop - peek);
        this.syncMobilePanelClasses();
        this.inputSidebar.style.display = '';
        this.inputSidebar.style.width = '100%';
        this.inputSidebar.style.height = '';
        this.mainImageArea.style.width = '100%';
        this.mainImageArea.style.height = `${topHeight}px`;
        this.mainImageArea.scrollTop = 0;
        this.topSection.style.height = `${topHeight}px`;
        this.currentImageBatch.style.width = '100%';
        this.currentImageBatch.style.height = '';
        let fullBottom = Math.max(200, viewH - rootTop);
        if (this.isMobileBottomOpen()) {
            fullBottom = Math.max(200, fullBottom - Math.min(52, Math.round(viewH * 0.07)));
        }
        this.bottomBar.style.height = `${this.isMobileBottomOpen() ? fullBottom : peek}px`;
        this.altRegion.style.width = '100%';
        this.altRegion.style.top = '';
        let altHeight = this.altRegion.style.display == 'none' || this.isMobileBottomOpen() ? 0 : this.altRegion.offsetHeight;
        this.currentImageWrapbox.style.width = '100%';
        this.currentImageWrapbox.style.height = `calc(${topHeight}px - ${altHeight}px)`;
        this.editorSizebar.style.height = `calc(${topHeight}px - ${altHeight}px)`;
        if (window.imageEditor && window.imageEditor.active) {
            let imageEditorSizePercent = this.mobileImageEditorBarPos / 100.0;
            window.imageEditor.inputDiv.style.width = `calc((100%) * ${imageEditorSizePercent})`;
            this.currentImage.style.width = `calc((100%) * ${(1.0 - imageEditorSizePercent)} - 6px)`;
        }
        else {
            this.currentImage.style.width = '100%';
        }
        if (this.currentImageBatchCore.offsetWidth < 425) {
            this.currentImageBatchCore.classList.add('current_image_batch_core_small');
        }
        else {
            this.currentImageBatchCore.classList.remove('current_image_batch_core_small');
        }
        if (!this.mobileDragActive) {
            this.clearMobileDragTransforms();
        }
    }

    /** Desktop split-bar positioning path (large-window only). */
    reapplyDesktopPositions(rootTop, leftShut, bottomShut) {
        let fontRem = parseFloat(getComputedStyle(document.documentElement).fontSize);
        let barTopLeftPx = leftShut ? 0 : this.leftSectionBarPos == -1 ? 28 * fontRem : this.leftSectionBarPos;
        let barTopRightPx = this.rightSectionBarPos == -1 ? 21 * fontRem : this.rightSectionBarPos;
        let barTopLeft = `${barTopLeftPx}px`;
        let barTopRight = `${barTopRightPx}px`;
        let curImgWidth = `100vw - ${barTopLeft} - ${barTopRight} - 10px`;
        let curImgWidthNum = window.innerWidth - barTopLeftPx - barTopRightPx - 10;
        if (curImgWidthNum < 400) {
            barTopRightPx += 400 - curImgWidthNum;
            barTopRight = `${barTopRightPx}px`;
            curImgWidth = `100vw - ${barTopLeft} - ${barTopRight} - 10px`;
        }
        this.altRegion.style.visibility = '';
        this.inputSidebar.style.width = `${barTopLeft}`;
        this.inputSidebar.style.display = leftShut ? 'none' : '';
        this.altRegion.style.width = `calc(100vw - ${barTopLeft} - ${barTopRight} - 10px)`;
        this.mainImageArea.style.width = `calc(100vw - ${barTopLeft})`;
        this.mainImageArea.scrollTop = 0;
        if (window.imageEditor && window.imageEditor.active) {
            let imageEditorSizePercent = this.imageEditorBarPos < 0 ? 0.5 : (this.imageEditorBarPos / 100.0);
            window.imageEditor.inputDiv.style.width = `calc((${curImgWidth}) * ${imageEditorSizePercent})`;
            this.currentImage.style.width = `calc((${curImgWidth}) * ${(1.0 - imageEditorSizePercent)} - 6px)`;
        }
        else {
            this.currentImage.style.width = `calc(${curImgWidth})`;
        }
        this.currentImageWrapbox.style.width = `calc(${curImgWidth})`;
        this.currentImageBatch.style.width = `calc(${barTopRight} - 6px)`;
        if (this.currentImageBatchCore.offsetWidth < 425) {
            this.currentImageBatchCore.classList.add('current_image_batch_core_small');
        }
        else {
            this.currentImageBatchCore.classList.remove('current_image_batch_core_small');
        }
        let altHeight = this.altRegion.style.display == 'none' ? '0px' : `${this.altRegion.offsetHeight}px`;
        if (this.bottomSectionBarPos != -1 || bottomShut) {
            let bottomBarHeight = this.bottomInfoBar.offsetHeight;
            let addedHeight = '2.8rem';
            let fixed = bottomShut ? `(${rootTop}px + ${addedHeight} + ${bottomBarHeight}px)` : `${this.bottomSectionBarPos}px`;
            this.leftSplitBar.style.height = `calc(100vh - ${fixed})`;
            this.rightSplitBar.style.height = `calc(100vh - ${fixed} - 5px)`;
            this.inputSidebar.style.height = `calc(100vh - ${fixed})`;
            this.mainImageArea.style.height = `calc(100vh - ${fixed})`;
            this.currentImageWrapbox.style.height = `calc(100vh - ${fixed} - ${altHeight})`;
            this.editorSizebar.style.height = `calc(100vh - ${fixed} - ${altHeight})`;
            this.currentImageBatch.style.height = `calc(100vh - ${fixed})`;
            this.topSection.style.height = `calc(100vh - ${fixed})`;
            this.bottomBar.style.height = `calc(${fixed} - 45px)`;
        }
        else {
            this.leftSplitBar.style.height = 'calc(49vh)';
            this.rightSplitBar.style.height = 'calc(49vh)';
            this.inputSidebar.style.height = '';
            this.mainImageArea.style.height = '';
            this.currentImageWrapbox.style.height = `calc(49vh - ${altHeight} + 1rem)`;
            this.editorSizebar.style.height = `calc(49vh - ${altHeight})`;
            this.currentImageBatch.style.height = '';
            this.topSection.style.height = '';
            this.bottomBar.style.height = `calc(49vh - 30px)`;
        }
    }

    /** Internal initialization of the generate tab. */
    init() {
        for (let tab of this.managedTabs) {
            tab.contentElem.style.height = '100%';
            tab.contentElem.style.width = '100%';
            if (!this.managedTabContainers.includes(tab.contentElem.parentElement)) {
                this.managedTabContainers.push(tab.contentElem.parentElement);
            }
            if (this.hideTabs.includes(tab.id)) {
                tab.visible = false;
            }
            tab.update();
            tab.navElem.addEventListener('click', () => {
                browserUtil.queueMakeVisible(tab.contentElem);
            });
        }
        this.reapplyPositions();
        this.leftSplitBar.addEventListener('mousedown', (e) => {
            if (this.isSmallWindow) {
                return;
            }
            this.leftBarDrag = true;
            e.preventDefault();
        }, true);
        this.rightSplitBar.addEventListener('mousedown', (e) => {
            if (this.isSmallWindow) {
                return;
            }
            this.rightBarDrag = true;
            e.preventDefault();
        }, true);
        this.leftSplitBar.addEventListener('touchstart', (e) => {
            if (this.isSmallWindow) {
                return;
            }
            this.leftBarDrag = true;
            e.preventDefault();
        }, true);
        this.rightSplitBar.addEventListener('touchstart', (e) => {
            if (this.isSmallWindow) {
                return;
            }
            this.rightBarDrag = true;
            e.preventDefault();
        }, true);
        this.editorSizebar.addEventListener('mousedown', (e) => {
            this.imageEditorSizeBarDrag = true;
            e.preventDefault();
        }, true);
        this.editorSizebar.addEventListener('touchstart', (e) => {
            this.imageEditorSizeBarDrag = true;
            e.preventDefault();
        }, true);
        this.bottomSplitBar.addEventListener('mousedown', (e) => {
            if (this.isSmallWindow) {
                return;
            }
            if (e.target == this.bottomSplitBarButton) {
                return;
            }
            this.bottomBarDrag = true;
            this.setBottomShut(false);
            e.preventDefault();
        }, true);
        this.bottomSplitBar.addEventListener('touchstart', (e) => {
            if (this.isSmallWindow) {
                return;
            }
            if (e.target == this.bottomSplitBarButton) {
                return;
            }
            this.bottomBarDrag = true;
            this.setBottomShut(false);
            e.preventDefault();
        }, true);
        this.bottomSplitBarButton.addEventListener('click', (e) => {
            e.preventDefault();
            this.bottomBarDrag = false;
            if (this.isSmallWindow) {
                return;
            }
            this.setBottomShut(!this.bottomShut);
            this.bottomSectionBarPos = Math.max(this.bottomSectionBarPos, 400);
            this.reapplyPositions();
        }, true);
        this.leftSplitBarButton.addEventListener('click', (e) => {
            e.preventDefault();
            this.leftBarDrag = false;
            if (this.isSmallWindow) {
                return;
            }
            this.setLeftShut(!this.leftShut);
            this.leftSectionBarPos = Math.max(this.leftSectionBarPos, 400);
            this.reapplyPositions();
            triggerChangeFor(this.altText);
            triggerChangeFor(this.altNegText);
        }, true);
        let moveEvt = (e, x, y) => {
            let offX = x;
            offX = Math.min(Math.max(offX, 100), window.innerWidth - 10);
            if (this.leftBarDrag) {
                this.leftSectionBarPos = Math.min(offX - 3, 51 * 16);
                this.setLeftShut(this.leftSectionBarPos < 290);
                this.reapplyPositions();
            }
            if (this.rightBarDrag) {
                this.rightSectionBarPos = window.innerWidth - offX;
                if (this.rightSectionBarPos < 100) {
                    this.rightSectionBarPos = 22;
                }
                this.reapplyPositions();
            }
            if (this.imageEditorSizeBarDrag) {
                let maxAreaWidth = window.imageEditor.inputDiv.offsetWidth + this.currentImage.offsetWidth + 10;
                let imageAreaLeft = window.imageEditor.inputDiv.getBoundingClientRect().left;
                let val = Math.min(Math.max(offX - imageAreaLeft + 3, 200), maxAreaWidth - 200);
                let nextImageEditorBarPos = Math.min(90, Math.max(10, val / maxAreaWidth * 100));
                if (this.isSmallWindow) {
                    this.mobileImageEditorBarPos = nextImageEditorBarPos;
                }
                else {
                    this.imageEditorBarPos = nextImageEditorBarPos;
                }
                this.reapplyPositions();
            }
            if (this.bottomBarDrag) {
                let MID_OFF = 85;
                let refY = Math.min(Math.max(e.pageY, MID_OFF), window.innerHeight - MID_OFF);
                this.setBottomShut(refY >= window.innerHeight - MID_OFF);
                this.bottomSectionBarPos = window.innerHeight - refY + this.topSection.getBoundingClientRect().top + 3;
                this.reapplyPositions();
            }
        };
        document.addEventListener('mousemove', (e) => moveEvt(e, e.pageX, e.pageY));
        document.addEventListener('touchmove', (e) => {
            if (this.isSmallWindow && this.swipeStartX != -1) {
                let touch = e.touches.item(0);
                if (!touch) {
                    return;
                }
                let deltaX = touch.pageX - this.swipeStartX;
                let deltaY = touch.pageY - this.swipeStartY;
                if (this.mobileTopbarDragging) {
                    this.setMobileTopbarCollapse(this.mobileTopbarDragFrom - deltaY);
                    if (e.cancelable) {
                        e.preventDefault();
                    }
                    return;
                }
                if (!this.mobileDragActive && this.mobileDragPanel) {
                    let absX = Math.abs(deltaX);
                    let absY = Math.abs(deltaY);
                    if (absX > 8 || absY > 8) {
                        let wantHorizontal = this.mobileDragPanel == 'left' || this.mobileDragPanel == 'right';
                        if (wantHorizontal && absX > absY) {
                            this.mobileDragActive = true;
                            if (this.mobileDragOpening) {
                                this.hideMobileTopbar();
                            }
                            document.body.classList.add('mobile-panel-dragging');
                            if (this.mobileDragOpening) {
                                if (this.mobileDragPanel == 'left') {
                                    document.body.classList.add('mobile-panel-left-open');
                                }
                                else if (this.mobileDragPanel == 'right') {
                                    document.body.classList.add('mobile-panel-right-open');
                                }
                                else if (this.mobileDragPanel == 'bottom') {
                                    document.body.classList.add('mobile-panel-bottom-open');
                                }
                                document.body.classList.remove('mobile-panels-all-shut');
                            }
                        }
                        else if (!wantHorizontal && absY > absX) {
                            let directionMatches = this.mobileDragOpening ? deltaY < -8 : deltaY > 8;
                            let boundaryAllowsDrag = !this.mobileDragRequiresBoundary || !this.mobileDragScrollElement || this.mobileDragScrollElement.scrollTop <= 0;
                            if (directionMatches && boundaryAllowsDrag) {
                                this.mobileDragActive = true;
                                if (this.mobileDragOpening) {
                                    this.hideMobileTopbar();
                                }
                                document.body.classList.add('mobile-panel-dragging');
                                if (this.mobileDragOpening) {
                                    document.body.classList.add('mobile-panel-bottom-open');
                                    document.body.classList.remove('mobile-panels-all-shut');
                                }
                            }
                            else {
                                this.mobileDragPanel = null;
                                this.mobileDragScrollElement = null;
                                this.mobileDragRequiresBoundary = false;
                            }
                        }
                        else {
                            this.mobileDragPanel = null;
                        }
                    }
                }
                if (this.mobileDragActive) {
                    this.applyMobileDragTransform(touch.pageX, touch.pageY);
                    if (e.cancelable) {
                        e.preventDefault();
                    }
                    return;
                }
                if (this.mobileTopbarCanDrag && !this.mobileDragPanel && Math.abs(deltaY) > 8 && Math.abs(deltaY) > Math.abs(deltaX)) {
                    this.mobileTopbarDragging = true;
                    this.mobileTopbarDragFrom = this.mobileTopbarCollapsePx;
                    this.setMobileTopbarCollapse(this.mobileTopbarDragFrom - deltaY);
                    if (e.cancelable) {
                        e.preventDefault();
                    }
                    return;
                }
            }
            if (e.touches.length > 0) {
                moveEvt(e, e.touches.item(0).pageX, e.touches.item(0).pageY);
            }
        }, { passive: false });
        document.addEventListener('mouseup', (e) => {
            this.leftBarDrag = false;
            this.rightBarDrag = false;
            this.bottomBarDrag = false;
            this.imageEditorSizeBarDrag = false;
        });
        document.addEventListener('touchstart', (e) => {
            this.resetMobileDragState(this.mobileDragAdjustedBottomHeight);
            if (this.isSmallWindow && document.body.classList.contains('modal-open')) {
                this.swipeStartX = -1;
                this.swipeStartY = -1;
                return;
            }
            if (this.isSmallWindow && this.isMobileSplitterTouchTarget(e.target)) {
                this.swipeStartX = -1;
                this.swipeStartY = -1;
                return;
            }
            if (e.touches.length == 1 && !['BUTTON', 'INPUT', 'SELECT', 'TEXTAREA'].includes(e.target.tagName) && !findParentOfClass(e.target, 'model-block')) {
                this.swipeStartX = e.touches.item(0).pageX;
                this.swipeStartY = e.touches.item(0).pageY;
                this.mobileDragStartX = this.swipeStartX;
                this.mobileDragStartY = this.swipeStartY;
                if (this.isSmallWindow) {
                    let gesture = this.getMobileGestureForTouch(this.swipeStartX, this.swipeStartY, e.target);
                    if (gesture) {
                        this.mobileDragPanel = gesture.panel;
                        this.mobileDragOpening = gesture.opening;
                        this.mobileDragScrollElement = gesture.scrollElement || null;
                        this.mobileDragRequiresBoundary = !!gesture.requiresBoundary;
                    }
                    let onTopChrome = !!(e.target.closest && e.target.closest('#toptablist, .t2i-area-quicktools'));
                    this.mobileTopbarCanDrag = onTopChrome || (!this.isMobileScrollableTouchTarget(e.target) && this.areAllMobilePanelsShut());
                }
            }
            else {
                this.swipeStartX = -1;
                this.swipeStartY = -1;
            }
        }, { passive: true });
        document.addEventListener('touchend', (e) => {
            this.leftBarDrag = false;
            this.rightBarDrag = false;
            this.bottomBarDrag = false;
            this.imageEditorSizeBarDrag = false;
            let touch = e.changedTouches.length == 1 ? e.changedTouches.item(0) : null;
            if (this.mobileTopbarDragging) {
                this.resetMobileDragState(true);
                return;
            }
            if (!touch) {
                this.resetMobileDragState(true);
                return;
            }
            if (this.isSmallWindow && this.mobileDragActive) {
                this.finishMobileDrag(touch.pageX, touch.pageY);
                this.swipeStartX = -1;
                this.swipeStartY = -1;
                this.mobileTopbarCanDrag = false;
                return;
            }
            this.resetMobileDragState(false);
        });
        document.addEventListener('touchcancel', () => {
            this.leftBarDrag = false;
            this.rightBarDrag = false;
            this.bottomBarDrag = false;
            this.imageEditorSizeBarDrag = false;
            this.resetMobileDragState(true);
        });
        for (let tab of getRequiredElementById('bottombartabcollection').getElementsByTagName('a')) {
            tab.addEventListener('click', (e) => {
                if (swarmHasLoaded) {
                    if (this.isSmallWindow) {
                        this.openMobilePanel('bottom');
                    }
                    else {
                        this.setBottomShut(false);
                        this.reapplyPositions();
                    }
                }
            });
        }
        this.altText.addEventListener('keydown', (e) => {
            let shouldGenerate = e.key == 'Enter' && !e.shiftKey
                && ((e.ctrlKey && internalSiteJsGetUserSetting('ctrlenterkeygenerates', true))
                    || (!e.ctrlKey && internalSiteJsGetUserSetting('enterkeygenerates', true)));
            if (shouldGenerate) {
                this.altText.dispatchEvent(new Event('change'));
                getRequiredElementById('alt_generate_button').click();
                e.preventDefault();
                e.stopPropagation();
                return false;
            }
        });
        this.altNegText.addEventListener('keydown', (e) => {
            let shouldGenerate = e.key == 'Enter' && !e.shiftKey
                && ((e.ctrlKey && internalSiteJsGetUserSetting('ctrlenterkeygenerates', true))
                    || (!e.ctrlKey && internalSiteJsGetUserSetting('enterkeygenerates', true)));
            if (shouldGenerate) {
                this.altNegText.dispatchEvent(new Event('change'));
                getRequiredElementById('alt_generate_button').click();
                e.preventDefault();
                e.stopPropagation();
                return false;
            }
        });
        this.altText.addEventListener('input', (e) => {
            let inputPrompt = document.getElementById('input_prompt');
            if (inputPrompt) {
                inputPrompt.value = this.altText.value;
            }
            this.schedulePromptLayoutUpdate();
            this.schedulePromptHeavyInputWork('positive', () => {
                setCookie(`lastparam_input_prompt`, this.altText.value, getParamMemoryDays());
                textPromptDoCount(this.altText, getRequiredElementById('alt_text_tokencount'));
                monitorPromptChangeForEmbed(this.altText.value, 'positive');
                setGroupAdvancedOverride('regionalprompting', this.altText.value.includes('<segment:') || this.altText.value.includes('<region:'));
            });
        });
        this.altText.addEventListener('change', () => {
            this.schedulePromptHeavyInputWork('positive', () => {
                setCookie(`lastparam_input_prompt`, this.altText.value, getParamMemoryDays());
                textPromptDoCount(this.altText, getRequiredElementById('alt_text_tokencount'));
                monitorPromptChangeForEmbed(this.altText.value, 'positive');
                setGroupAdvancedOverride('regionalprompting', this.altText.value.includes('<segment:') || this.altText.value.includes('<region:'));
            }, true);
        });
        this.altNegText.addEventListener('input', (e) => {
            let inputNegPrompt = document.getElementById('input_negativeprompt');
            if (inputNegPrompt) {
                inputNegPrompt.value = this.altNegText.value;
            }
            let negTokCount = getRequiredElementById('alt_negtext_tokencount');
            if (this.altNegText.value == '') {
                negTokCount.style.display = 'none';
            }
            else {
                negTokCount.style.display = '';
            }
            this.schedulePromptLayoutUpdate();
            this.schedulePromptHeavyInputWork('negative', () => {
                setCookie(`lastparam_input_negativeprompt`, this.altNegText.value, getParamMemoryDays());
                textPromptDoCount(this.altNegText, negTokCount, ', Neg: ');
                monitorPromptChangeForEmbed(this.altNegText.value, 'negative');
            });
        });
        this.altNegText.addEventListener('change', () => {
            let negTokCount = getRequiredElementById('alt_negtext_tokencount');
            this.schedulePromptHeavyInputWork('negative', () => {
                setCookie(`lastparam_input_negativeprompt`, this.altNegText.value, getParamMemoryDays());
                textPromptDoCount(this.altNegText, negTokCount, ', Neg: ');
                monitorPromptChangeForEmbed(this.altNegText.value, 'negative');
            }, true);
        });
        this.altPromptSizeHandle();
        new ResizeObserver(this.altPromptSizeHandle.bind(this)).observe(this.altText);
        new ResizeObserver(this.altPromptSizeHandle.bind(this)).observe(this.altNegText);
        textPromptAddKeydownHandler(this.altText);
        textPromptAddKeydownHandler(this.altNegText);
        addEventListener("resize", this.reapplyPositions.bind(this));
        let editWildcardContents = document.getElementById('edit_wildcard_contents');
        if (editWildcardContents) {
            textPromptAddKeydownHandler(editWildcardContents);
        }
        if (window.visualViewport) {
            let vvHandler = () => {
                if (this.isSmallWindow && this.mobilePromptFocused) {
                    this.reapplyPositions();
                }
            };
            window.visualViewport.addEventListener('resize', vvHandler);
            window.visualViewport.addEventListener('scroll', vvHandler);
        }
        let promptFocusHandler = () => {
            this.mobilePromptFocused = true;
            if (this.isSmallWindow) {
                this.reapplyPositions();
            }
        };
        let promptBlurHandler = () => {
            setTimeout(() => {
                let active = document.activeElement;
                this.mobilePromptFocused = active == this.altText || active == this.altNegText;
                if (this.isSmallWindow) {
                    this.reapplyPositions();
                }
            }, 50);
        };
        this.altText.addEventListener('focus', promptFocusHandler);
        this.altNegText.addEventListener('focus', promptFocusHandler);
        this.altText.addEventListener('blur', promptBlurHandler);
        this.altNegText.addEventListener('blur', promptBlurHandler);
        this.initMobileOverlayUi();
        this.reapplyPositions();
        this.buildConfigArea();
    }

    rebuildVisibleCookie() {
        setCookie('layout_hidetabs', this.managedTabs.filter(t => !t.visible).map(t => t.id).join(','), 365);
    }

    updateConfigFor(id) {
        let tab = this.managedTabs.find(t => t.id == id);
        if (tab) {
            tab.visible = getRequiredElementById(`tabconfig_${id}_visible`).checked;
            tab.targetGroupId = getRequiredElementById(`tabconfig_${id}_group`).value;
            tab.update();
            this.buildConfigArea();
        }
    }

    buildConfigArea() {
        this.layoutConfigArea = document.getElementById('layoutconfigarea');
        if (!this.layoutConfigArea) {
            return;
        }
        let html = '<table class="simple-table">\n<tr><th>Tab</th><th>Group</th><th>Visible</th></tr>\n';
        let selectOptions = filterDistinctBy(this.managedTabs.map(t => t.defaultGroup), g => g.id).map(e => `<option value="${e.id}">${escapeHtml(e.dataset.title)}</option>`).join('\n');
        for (let tab of this.managedTabs) {
            html += `<tr>
                    <td><b>${escapeHtml(tab.title)}</b></td>
                    <td><select id="tabconfig_${tab.id}_group">${selectOptions}</select></td>
                    <td><input type="checkbox" id="tabconfig_${tab.id}_visible" ${tab.visible ? 'checked' : ''}></td>
                </tr>`;
        }
        html += '</table>';
        this.layoutConfigArea.innerHTML = html;
        for (let tab of this.managedTabs) {
            getRequiredElementById(`tabconfig_${tab.id}_group`).value = tab.targetGroupId;
            getRequiredElementById(`tabconfig_${tab.id}_visible`).addEventListener('change', () => this.updateConfigFor(tab.id));
            getRequiredElementById(`tabconfig_${tab.id}_group`).addEventListener('change', () => this.updateConfigFor(tab.id));
        }
        this.rebuildVisibleCookie();
    }

    resetSubTabs() {
        if (confirm('Are you sure you want to reset the layout of the subtabs?\nThis will make all sub-tabs visible, and put them in their default locations.')) {
            for (let tab of this.managedTabs) {
                tab.targetGroupId = tab.defaultGroup.id;
                tab.visible = true;
                tab.update();
            }
            this.reapplyPositions();
            this.buildConfigArea();
        }
    }

    onMobileDesktopLayoutChange() {
        let selector = document.getElementById('mobile_desktop_layout_selector');
        if (!selector) {
            return;
        }
        this.mobileDesktopLayout = selector.value;
        localStorage.setItem('layout_mobileDesktop', this.mobileDesktopLayout);
        this.reapplyPositions();
        window.dispatchEvent(new Event('swarm-layout-mode-change'));
    }
}

/** Central handler for generate main tab layout logic. */
let genTabLayout = new GenTabLayout();
