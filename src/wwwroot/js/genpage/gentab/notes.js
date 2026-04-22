let notesTab = null;

class NotesTab {

    constructor() {
        this.browser = new GenPageBrowserClass('notes_browser', this.listNotesFolderAndFiles.bind(this), 'notesbrowser', 'Details List', this.describeNoteFile.bind(this), this.selectNoteFile.bind(this),
            '<button class="refresh-button translate" id="notes_new_folder_button" title="Create a new folder">&#x1F4C1;+</button>\n'
            + '<button class="refresh-button translate" id="notes_new_note_button" title="Create a new markdown note">&#x270E;+</button>\n'
            + '<button class="refresh-button translate" id="notes_rename_button" title="Rename the current note or folder">&#x270F;</button>\n'
            + '<button class="refresh-button translate" id="notes_delete_button" title="Delete the current note or folder">&#x1F5D1;</button>', 8);
        this.browser.showDisplayFormat = false;
        this.browser.folderTreeShowFiles = true;
        this.browser.filterUpdateDelayMs = 60;
        this.browser.folderSelectedEvent = this.onFolderSelected.bind(this);
        this.browser.builtEvent = this.onBrowserBuilt.bind(this);
        this.browser.sizeChangedEvent = () => fixTabHeights();
        this.browser.format = 'Details List';
        this.transparentImage = 'data:image/gif;base64,R0lGODlhAQABAIAAAAAAAP///ywAAAAAAQABAAACAUwAOw==';
        this.currentPath = null;
        this.currentContent = '';
        this.savedContent = '';
        this.currentModified = 0;
        this.hasLoaded = false;
        this.rootConfigured = false;
        this.rootExists = false;
        this.emptyMessage = 'Select or create a note to begin.';
        this.editor = getRequiredElementById('notes_editor_input');
        this.previewPanel = getRequiredElementById('notes_preview_panel');
        this.previewToggle = getRequiredElementById('notes_preview_toggle');
        this.statusLine = getRequiredElementById('notes_status_line');
        this.emptyState = getRequiredElementById('notes_empty_state');
        this.currentPathElem = getRequiredElementById('notes_current_path');
        this.saveButton = getRequiredElementById('notes_save_button');
        this.notesTabButton = document.querySelector('a[href="#Notes-Tab"]');
        this.previewToggle.checked = localStorage.getItem('notes_preview_enabled') == 'true';
        this.previewToggle.addEventListener('change', this.onPreviewToggleChanged.bind(this));
        this.editor.addEventListener('input', this.onEditorInput.bind(this));
        this.browser.ensureBuilt();
        if (this.notesTabButton) {
            this.notesTabButton.addEventListener('shown.bs.tab', this.onNotesTabActivated.bind(this));
        }
        window.addEventListener('beforeunload', this.handlePageUnload.bind(this));
        window.addEventListener('pagehide', this.handlePageUnload.bind(this));
        this.applyPreviewMode();
        this.updateEditorState();
    }

    /** Returns whether the current note has unsaved changes. */
    isDirty() {
        return this.currentPath && this.currentContent != this.savedContent;
    }

    /** Handles the Notes tab being activated. */
    onNotesTabActivated() {
        if (!this.hasLoaded) {
            this.browser.navigate('');
            this.hasLoaded = true;
        }
    }

    /** Handles a saved user-settings change that may affect the notes root. */
    onUserSettingsSaved() {
        this.hasLoaded = false;
        this.clearCurrentNote('Notes settings changed.');
        if (this.notesTabButton && this.notesTabButton.classList.contains('active')) {
            this.onNotesTabActivated();
        }
    }

    /** Handles folder selection in the browser tree. */
    onFolderSelected(path) {
        this.browser.selected = path;
        this.browser.rerender();
        this.updateActionButtons();
    }

    /** Handles browser rebuild events. */
    onBrowserBuilt() {
        this.wireHeaderButtons();
        this.updateActionButtons();
    }

    /** Wires the notes browser header buttons after the browser shell exists. */
    wireHeaderButtons() {
        let newFolder = document.getElementById('notes_new_folder_button');
        let newNote = document.getElementById('notes_new_note_button');
        let rename = document.getElementById('notes_rename_button');
        let del = document.getElementById('notes_delete_button');
        let headerNewFolder = document.getElementById('notes_header_new_folder_button');
        let headerNewNote = document.getElementById('notes_header_new_note_button');
        let headerRename = document.getElementById('notes_header_rename_button');
        let headerDelete = document.getElementById('notes_header_delete_button');
        if (newFolder && !newFolder.dataset.ready) {
            newFolder.dataset.ready = 'true';
            newFolder.addEventListener('click', this.createFolderPrompt.bind(this));
        }
        if (headerNewFolder && !headerNewFolder.dataset.ready) {
            headerNewFolder.dataset.ready = 'true';
            headerNewFolder.addEventListener('click', this.createFolderPrompt.bind(this));
        }
        if (newNote && !newNote.dataset.ready) {
            newNote.dataset.ready = 'true';
            newNote.addEventListener('click', this.createNotePrompt.bind(this));
        }
        if (headerNewNote && !headerNewNote.dataset.ready) {
            headerNewNote.dataset.ready = 'true';
            headerNewNote.addEventListener('click', this.createNotePrompt.bind(this));
        }
        if (rename && !rename.dataset.ready) {
            rename.dataset.ready = 'true';
            rename.addEventListener('click', this.renameCurrentPrompt.bind(this));
        }
        if (headerRename && !headerRename.dataset.ready) {
            headerRename.dataset.ready = 'true';
            headerRename.addEventListener('click', this.renameCurrentPrompt.bind(this));
        }
        if (del && !del.dataset.ready) {
            del.dataset.ready = 'true';
            del.addEventListener('click', this.deleteCurrentPrompt.bind(this));
        }
        if (headerDelete && !headerDelete.dataset.ready) {
            headerDelete.dataset.ready = 'true';
            headerDelete.addEventListener('click', this.deleteCurrentPrompt.bind(this));
        }
    }

    /** Updates action button disabled states. */
    updateActionButtons() {
        let rename = document.getElementById('notes_rename_button');
        let del = document.getElementById('notes_delete_button');
        let headerRename = document.getElementById('notes_header_rename_button');
        let headerDelete = document.getElementById('notes_header_delete_button');
        let headerNewNote = document.getElementById('notes_header_new_note_button');
        let headerNewFolder = document.getElementById('notes_header_new_folder_button');
        let hasTarget = !!this.getCurrentTargetPath();
        let canCreate = this.rootConfigured;
        if (rename) {
            rename.disabled = !hasTarget;
        }
        if (del) {
            del.disabled = !hasTarget;
        }
        if (headerRename) {
            headerRename.disabled = !hasTarget;
        }
        if (headerDelete) {
            headerDelete.disabled = !hasTarget;
        }
        if (headerNewNote) {
            headerNewNote.disabled = !canCreate;
        }
        if (headerNewFolder) {
            headerNewFolder.disabled = !canCreate;
        }
        this.saveButton.disabled = !this.currentPath;
    }

    /** Handles preview toggle updates. */
    onPreviewToggleChanged() {
        localStorage.setItem('notes_preview_enabled', this.previewToggle.checked);
        this.applyPreviewMode();
    }

    /** Applies the current preview/editor visibility mode. */
    applyPreviewMode() {
        let previewActive = this.previewToggle.checked;
        this.previewPanel.style.display = previewActive ? 'block' : 'none';
        this.editor.style.display = previewActive ? 'none' : 'block';
        if (previewActive) {
            this.previewPanel.innerHTML = this.renderMarkdown(this.editor.value);
        }
    }

    /** Handles editor input changes. */
    onEditorInput() {
        this.currentContent = this.editor.value;
        if (this.previewToggle.checked) {
            this.previewPanel.innerHTML = this.renderMarkdown(this.currentContent);
        }
        this.updateEditorState();
    }

    /** Updates editor visible state based on current content and note selection. */
    updateEditorState() {
        let hasNote = !!this.currentPath;
        this.editor.disabled = !hasNote;
        this.previewToggle.disabled = !hasNote;
        this.updateActionButtons();
        this.statusLine.classList.remove('notes-status-error');
        this.statusLine.classList.remove('notes-status-dirty');
        if (!hasNote) {
            this.emptyState.style.display = 'flex';
            this.editor.style.display = 'none';
            this.previewPanel.style.display = 'none';
            this.currentPathElem.innerText = 'No note selected';
            this.statusLine.innerText = this.rootConfigured ? this.emptyMessage : 'Set a Notes Root in User Settings to enable notes.';
            return;
        }
        this.emptyState.style.display = 'none';
        this.applyPreviewMode();
        this.currentPathElem.innerText = this.currentPath;
        if (this.isDirty()) {
            this.statusLine.classList.add('notes-status-dirty');
            this.statusLine.innerText = 'Unsaved changes.';
        }
        else {
            this.statusLine.innerText = 'Saved.';
        }
    }

    /** Sets the current note state from loaded server data. */
    setCurrentNote(path, content, modified) {
        this.currentPath = path;
        this.currentContent = content ?? '';
        this.savedContent = this.currentContent;
        this.currentModified = modified || 0;
        this.browser.selected = path;
        this.editor.value = this.currentContent;
        this.applyPreviewMode();
        this.browser.rerender();
        this.updateEditorState();
    }

    /** Clears the current note selection. */
    clearCurrentNote(message = 'Select or create a note to begin.') {
        this.emptyMessage = message;
        this.currentPath = null;
        this.currentContent = '';
        this.savedContent = '';
        this.currentModified = 0;
        this.editor.value = '';
        this.previewPanel.innerHTML = '';
        this.browser.selected = this.browser.folder;
        this.statusLine.innerText = message;
        this.updateEditorState();
    }

    /** Updates the info message for root configuration state. */
    updateRootState(rootConfigured, rootExists) {
        this.rootConfigured = rootConfigured;
        this.rootExists = rootExists;
        if (!rootConfigured) {
            this.clearCurrentNote('Set a Notes Root in User Settings to enable notes.');
        }
        else if (!rootExists) {
            this.clearCurrentNote('Notes root is configured but does not exist yet. Create a note or folder to initialize it.');
        }
        else if (!this.currentPath) {
            this.updateEditorState();
        }
    }

    /** Lists notes for the browser widget. */
    listNotesFolderAndFiles(path, isRefresh, callback, depth, onError = null) {
        genericRequest('ListNotes', { path: path, depth: depth }, (data) => {
            this.updateRootState(data.root_configured, data.root_exists);
            let folders = (data.folders || []).sort((a, b) => a.localeCompare(b));
            let files = (data.files || []).map((file) => {
                let fullName = path ? `${path}/${file.name}` : file.name;
                return {
                    name: fullName,
                    data: {
                        path: fullName,
                        name: file.name.includes('/') ? file.name.split('/').slice(-1)[0] : file.name,
                        modified: file.modified || 0
                    }
                };
            }).sort((a, b) => a.name.localeCompare(b.name));
            callback(folders, files);
        }, 0, (error) => {
            this.setError(error);
            callback([], []);
            if (onError) {
                onError(error);
            }
        });
    }

    /** Describes a note file for the browser widget. */
    describeNoteFile(file) {
        let display = file.data.name;
        let modified = file.data.modified ? formatDateTime(new Date(file.data.modified * 1000)) : '';
        return {
            name: display,
            display: display,
            description: `<b>${escapeHtmlNoBr(display)}</b><br>${escapeHtmlNoBr(modified)}`,
            detail_list: [escapeHtmlNoBr(display), escapeHtmlNoBr(modified)],
            image: this.transparentImage,
            searchable: `${file.name} ${modified}`,
            buttons: [
                { label: 'Rename', onclick: () => this.renamePathPrompt(file.name) },
                { label: 'Delete', onclick: () => this.deletePathPrompt(file.name) }
            ]
        };
    }

    /** Selects a note file from the browser widget. */
    selectNoteFile(file, callback = null) {
        let doSelect = () => {
            genericRequest('ReadNote', { path: file.name }, (data) => {
                this.setCurrentNote(data.path, data.content, data.modified);
                if (callback) {
                    callback();
                }
            }, 0, (error) => {
                this.setError(error);
                if (callback) {
                    callback();
                }
            });
        };
        this.runWithDirtyCheck(doSelect);
    }

    /** Sets an error status line message. */
    setError(message) {
        this.statusLine.classList.remove('notes-status-dirty');
        this.statusLine.classList.add('notes-status-error');
        this.statusLine.innerText = `${message}`;
    }

    /** Saves the current note if available. */
    saveCurrentNote(callback = null) {
        if (!this.currentPath) {
            if (callback) {
                callback(false);
            }
            return;
        }
        genericRequest('SaveNote', { path: this.currentPath, content: this.editor.value }, (data) => {
            this.savedContent = this.editor.value;
            this.currentContent = this.savedContent;
            this.statusLine.classList.remove('notes-status-error');
            this.statusLine.classList.remove('notes-status-dirty');
            this.statusLine.innerText = 'Saved.';
            this.browser.lightRefresh();
            if (callback) {
                callback(true);
            }
        }, 0, (error) => {
            this.setError(error);
            if (callback) {
                callback(false);
            }
        });
    }

    /** Saves the current note if possible before shutdown. */
    saveBeforeShutdown(callback) {
        if (!this.isDirty()) {
            callback();
            return;
        }
        this.saveCurrentNote((success) => {
            if (success || confirm('Failed to save the current note before shutdown. Continue shutting down anyway?')) {
                callback();
            }
        });
    }

    /** Attempts a fire-and-forget autosave during page unload. */
    handlePageUnload() {
        if (!this.isDirty()) {
            return;
        }
        this.sendBackgroundSave();
    }

    /** Sends the current note content using sendBeacon or a synchronous XHR fallback. */
    sendBackgroundSave() {
        if (!this.currentPath) {
            return false;
        }
        let payload = JSON.stringify({ path: this.currentPath, content: this.editor.value, session_id: session_id });
        if (navigator.sendBeacon) {
            let blob = new Blob([payload], { type: 'application/json' });
            return navigator.sendBeacon('API/SaveNote', blob);
        }
        let xhr = new XMLHttpRequest();
        xhr.open('POST', 'API/SaveNote', false);
        xhr.setRequestHeader('Content-Type', 'application/json');
        try {
            xhr.send(payload);
            return true;
        }
        catch (e) {
            return false;
        }
    }

    /** Runs an action after resolving unsaved changes when needed. */
    runWithDirtyCheck(action) {
        if (!this.isDirty()) {
            action();
            return;
        }
        if (confirm('Save changes to the current note first?\nPress OK to save, or Cancel for more options.')) {
            this.saveCurrentNote((success) => {
                if (success) {
                    action();
                }
            });
            return;
        }
        if (confirm('Discard unsaved changes?\nPress OK to discard them, or Cancel to stay on the current note.')) {
            action();
        }
    }

    /** Returns the current folder-relative destination for new note or folder operations. */
    getActiveFolder() {
        return this.browser.folder || '';
    }

    /** Returns the current target path for rename/delete actions. */
    getCurrentTargetPath() {
        if (this.currentPath) {
            return this.currentPath;
        }
        let folder = this.getActiveFolder();
        if (folder) {
            return folder;
        }
        return null;
    }

    /** Builds a child path beneath the currently active folder. */
    buildChildPath(name) {
        let folder = this.getActiveFolder();
        if (!folder) {
            return name;
        }
        return `${folder}/${name}`;
    }

    /** Prompts for a new folder name and creates it. */
    createFolderPrompt() {
        let name = prompt('New folder name:');
        if (!name) {
            return;
        }
        name = name.trim().replaceAll('\\', '/').replace(/^\/+|\/+$/g, '');
        if (!name) {
            return;
        }
        genericRequest('CreateNoteFolder', { path: this.buildChildPath(name) }, (data) => {
            this.browser.lightRefresh();
        }, 0, this.setError.bind(this));
    }

    /** Prompts for a new note name and creates it. */
    createNotePrompt() {
        let name = prompt('New note name:', 'untitled.md');
        if (!name) {
            return;
        }
        name = name.trim().replaceAll('\\', '/');
        if (!name.toLowerCase().endsWith('.md')) {
            name += '.md';
        }
        let fullPath = this.buildChildPath(name);
        this.runWithDirtyCheck(() => {
            genericRequest('CreateNote', { path: fullPath }, (data) => {
                this.browser.lightRefresh();
                this.selectNoteFile({ name: fullPath, data: { name: name, modified: 0 } });
            }, 0, this.setError.bind(this));
        });
    }

    /** Prompts to rename the current target. */
    renameCurrentPrompt() {
        let target = this.getCurrentTargetPath();
        if (!target) {
            return;
        }
        this.renamePathPrompt(target);
    }

    /** Prompts to delete the current target. */
    deleteCurrentPrompt() {
        let target = this.getCurrentTargetPath();
        if (!target) {
            return;
        }
        this.deletePathPrompt(target);
    }

    /** Prompts to rename a specific note or folder path. */
    renamePathPrompt(path) {
        let isCurrentNote = path == this.currentPath;
        let affectsCurrentNote = this.currentPath && (this.currentPath == path || this.currentPath.startsWith(`${path}/`));
        let currentName = path.includes('/') ? path.split('/').slice(-1)[0] : path;
        let newName = prompt('Rename to:', currentName);
        if (!newName) {
            return;
        }
        newName = newName.trim().replaceAll('\\', '/').replace(/^\/+|\/+$/g, '');
        if (!newName) {
            return;
        }
        let parent = path.includes('/') ? path.split('/').slice(0, -1).join('/') : '';
        let targetPath = parent ? `${parent}/${newName}` : newName;
        if (path.toLowerCase().endsWith('.md') && !targetPath.toLowerCase().endsWith('.md')) {
            targetPath += '.md';
        }
        let run = () => {
            genericRequest('RenameNotePath', { oldPath: path, newPath: targetPath }, (data) => {
                if (affectsCurrentNote) {
                    if (isCurrentNote) {
                        this.currentPath = targetPath;
                    }
                    else {
                        this.currentPath = `${targetPath}${this.currentPath.substring(path.length)}`;
                    }
                    this.currentPathElem.innerText = this.currentPath;
                }
                if (this.browser.folder == path || this.browser.folder.startsWith(`${path}/`)) {
                    this.browser.folder = `${targetPath}${this.browser.folder.substring(path.length)}`;
                }
                this.browser.selected = affectsCurrentNote ? this.currentPath : targetPath;
                this.browser.lightRefresh();
                this.updateEditorState();
            }, 0, this.setError.bind(this));
        };
        if (affectsCurrentNote) {
            this.runWithDirtyCheck(run);
        }
        else {
            run();
        }
    }

    /** Prompts to delete a specific note or folder path. */
    deletePathPrompt(path) {
        let label = path.toLowerCase().endsWith('.md') ? 'note' : 'folder';
        if (!confirm(`Delete this ${label}?\n${path}`)) {
            return;
        }
        let affectsCurrent = this.currentPath && (this.currentPath == path || this.currentPath.startsWith(`${path}/`));
        let run = () => {
            genericRequest('DeleteNotePath', { path: path }, (data) => {
                if (affectsCurrent) {
                    this.clearCurrentNote('Note deleted.');
                }
                if (this.browser.folder == path || this.browser.folder.startsWith(`${path}/`)) {
                    this.browser.folder = path.includes('/') ? path.split('/').slice(0, -1).join('/') : '';
                }
                this.browser.selected = this.browser.folder;
                this.browser.lightRefresh();
            }, 0, this.setError.bind(this));
        };
        if (affectsCurrent) {
            this.runWithDirtyCheck(run);
        }
        else {
            run();
        }
    }

    /** Escapes HTML without converting newlines to breaks. */
    escapeHtmlText(text) {
        return escapeHtmlNoBr(text ?? '');
    }

    /** Applies inline markdown replacements on already-escaped text. */
    applyInlineMarkdown(text) {
        text = text.replace(/`([^`]+)`/g, '<code>$1</code>');
        text = text.replace(/\*\*([^*]+)\*\*/g, '<strong>$1</strong>');
        text = text.replace(/\*([^*]+)\*/g, '<em>$1</em>');
        text = text.replace(/\[([^\]]+)\]\(([^)\s]+)\)/g, (match, label, target) => {
            let safeTarget = this.sanitizePreviewLinkTarget(target);
            return `<a href="${safeTarget}" target="_blank" rel="noreferrer noopener">${label}</a>`;
        });
        return text;
    }

    /** Sanitizes preview links to basic safe targets. */
    sanitizePreviewLinkTarget(target) {
        let lowered = `${target}`.toLowerCase();
        if (lowered.startsWith('http://') || lowered.startsWith('https://') || lowered.startsWith('mailto:') || lowered.startsWith('/') || lowered.startsWith('#')) {
            return target;
        }
        return '#';
    }

    /** Renders markdown into safe basic HTML for the preview pane. */
    renderMarkdown(text) {
        let lines = `${text ?? ''}`.replaceAll('\r\n', '\n').split('\n');
        let html = [];
        let inCode = false;
        let inUl = false;
        let inOl = false;
        let paragraph = [];
        let flushParagraph = () => {
            if (paragraph.length == 0) {
                return;
            }
            html.push(`<p>${this.applyInlineMarkdown(paragraph.join('<br>'))}</p>`);
            paragraph = [];
        };
        let closeLists = () => {
            if (inUl) {
                html.push('</ul>');
                inUl = false;
            }
            if (inOl) {
                html.push('</ol>');
                inOl = false;
            }
        };
        for (let rawLine of lines) {
            let escapedLine = this.escapeHtmlText(rawLine);
            let trimmed = rawLine.trim();
            if (trimmed.startsWith('```')) {
                flushParagraph();
                closeLists();
                if (!inCode) {
                    html.push('<pre><code>');
                    inCode = true;
                }
                else {
                    html.push('</code></pre>');
                    inCode = false;
                }
                continue;
            }
            if (inCode) {
                html.push(`${escapedLine}\n`);
                continue;
            }
            if (trimmed == '') {
                flushParagraph();
                closeLists();
                continue;
            }
            if (trimmed == '---' || trimmed == '***') {
                flushParagraph();
                closeLists();
                html.push('<hr>');
                continue;
            }
            if (/^#{1,6}\s/.test(trimmed)) {
                flushParagraph();
                closeLists();
                let level = trimmed.match(/^#+/)[0].length;
                let content = trimmed.substring(level).trim();
                html.push(`<h${level}>${this.applyInlineMarkdown(this.escapeHtmlText(content))}</h${level}>`);
                continue;
            }
            if (trimmed.startsWith('> ')) {
                flushParagraph();
                closeLists();
                html.push(`<blockquote>${this.applyInlineMarkdown(this.escapeHtmlText(trimmed.substring(2).trim()))}</blockquote>`);
                continue;
            }
            if (/^[-*]\s+/.test(trimmed)) {
                flushParagraph();
                if (inOl) {
                    html.push('</ol>');
                    inOl = false;
                }
                if (!inUl) {
                    html.push('<ul>');
                    inUl = true;
                }
                html.push(`<li>${this.applyInlineMarkdown(this.escapeHtmlText(trimmed.substring(2).trim()))}</li>`);
                continue;
            }
            if (/^\d+\.\s+/.test(trimmed)) {
                flushParagraph();
                if (inUl) {
                    html.push('</ul>');
                    inUl = false;
                }
                if (!inOl) {
                    html.push('<ol>');
                    inOl = true;
                }
                let content = trimmed.replace(/^\d+\.\s+/, '');
                html.push(`<li>${this.applyInlineMarkdown(this.escapeHtmlText(content))}</li>`);
                continue;
            }
            closeLists();
            paragraph.push(this.escapeHtmlText(trimmed));
        }
        flushParagraph();
        closeLists();
        if (inCode) {
            html.push('</code></pre>');
        }
        if (html.length == 0) {
            return '<p><em>No content.</em></p>';
        }
        return html.join('\n');
    }
}

notesTab = new NotesTab();
window.notesTab = notesTab;
