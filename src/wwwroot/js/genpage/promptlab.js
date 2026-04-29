class PromptLab {
    /** Builds the Prompt Lab helper. */
    constructor() {
        this.data = { prompts: [], fragments: [], wildcards: [], history: [] };
        this.currentPromptId = null;
        this.currentParentId = null;
        this.currentFragmentId = null;
        this.currentWildcardId = null;
        this.hasLoaded = false;
        this.pendingWildcardGenerations = [];
        this.isStartingWildcardGenerationSocket = false;
        this.searchRenderTimeouts = {};
    }

    /** Initializes Prompt Lab once the page is ready. */
    init() {
        promptTabComplete.enableFor(getRequiredElementById('prompt_lab_positive'));
        promptTabComplete.enableFor(getRequiredElementById('prompt_lab_negative'));
        this.load();
    }

    /** Loads Prompt Lab data from the server. */
    load() {
        genericRequest('PromptLabList', {}, data => {
            this.data = data.data || this.data;
            this.hasLoaded = true;
            this.renderPromptList();
            this.renderFragmentList();
            this.renderWildcardList();
            this.renderHistoryList();
            this.renderCompareSelect();
            this.refreshPreview();
        });
    }

    /** Schedules a filtered list render without rebuilding on every keystroke. */
    scheduleSearchRender(kind) {
        if (this.searchRenderTimeouts[kind]) {
            clearTimeout(this.searchRenderTimeouts[kind]);
        }
        this.searchRenderTimeouts[kind] = setTimeout(() => {
            this.searchRenderTimeouts[kind] = null;
            if (kind == 'prompts') {
                this.renderPromptList();
            }
            else if (kind == 'fragments') {
                this.renderFragmentList();
            }
            else if (kind == 'wildcards') {
                this.renderWildcardList();
            }
        }, 250);
    }

    /** Starts a blank prompt. */
    newPrompt() {
        this.currentPromptId = null;
        this.currentParentId = null;
        getRequiredElementById('prompt_lab_name').value = '';
        getRequiredElementById('prompt_lab_positive').value = '';
        getRequiredElementById('prompt_lab_negative').value = '';
        getRequiredElementById('prompt_lab_tags').value = '';
        getRequiredElementById('prompt_lab_notes').value = '';
        this.renderCompareSelect();
        this.refreshPreview();
    }

    /** Returns the editor contents as a Prompt Lab prompt object. */
    currentPromptObject() {
        let tags = getRequiredElementById('prompt_lab_tags').value.split(',').map(t => t.trim()).filter(t => t);
        let existing = this.data.prompts.find(p => p.id == this.currentPromptId);
        let item = {
            id: this.currentPromptId,
            name: getRequiredElementById('prompt_lab_name').value || 'Untitled Prompt',
            positive: getRequiredElementById('prompt_lab_positive').value,
            negative: getRequiredElementById('prompt_lab_negative').value,
            tags: tags,
            notes: getRequiredElementById('prompt_lab_notes').value,
            parent_id: this.currentParentId,
            favorite: existing?.favorite || false
        };
        return item;
    }

    /** Saves the current prompt. */
    savePrompt() {
        genericRequest('PromptLabSave', { collection: 'prompts', item: this.currentPromptObject() }, data => {
            let saved = data.item;
            this.currentPromptId = saved.id;
            let existing = this.data.prompts.findIndex(p => p.id == saved.id);
            if (existing == -1) {
                this.data.prompts.push(saved);
            }
            else {
                this.data.prompts[existing] = saved;
            }
            this.renderPromptList();
            this.renderCompareSelect();
        });
    }

    /** Saves the current prompt as a child variant. */
    savePromptVariant() {
        let parentId = this.currentPromptId || this.currentParentId;
        let item = this.currentPromptObject();
        item.id = null;
        item.parent_id = parentId;
        item.name = `${item.name} Variant`;
        genericRequest('PromptLabSave', { collection: 'prompts', item: item }, data => {
            let saved = data.item;
            this.data.prompts.push(saved);
            this.loadPrompt(saved.id);
        });
    }

    /** Resets the current editor fields back to the selected saved prompt. */
    resetPrompt() {
        if (this.currentPromptId) {
            this.loadPrompt(this.currentPromptId);
            return;
        }
        this.newPrompt();
    }

    /** Loads a prompt into the editor. */
    loadPrompt(id) {
        let prompt = this.data.prompts.find(p => p.id == id);
        if (!prompt) {
            return;
        }
        this.currentPromptId = prompt.id;
        this.currentParentId = prompt.parent_id || null;
        getRequiredElementById('prompt_lab_name').value = prompt.name || '';
        getRequiredElementById('prompt_lab_positive').value = prompt.positive || '';
        getRequiredElementById('prompt_lab_negative').value = prompt.negative || '';
        getRequiredElementById('prompt_lab_tags').value = (prompt.tags || []).join(', ');
        getRequiredElementById('prompt_lab_notes').value = prompt.notes || '';
        this.renderPromptList();
        this.renderCompareSelect();
        this.refreshPreview();
    }

    /** Duplicates the selected prompt. */
    duplicatePrompt() {
        if (!this.currentPromptId) {
            return;
        }
        genericRequest('PromptLabDuplicate', { collection: 'prompts', id: this.currentPromptId }, data => {
            this.data.prompts.push(data.item);
            this.loadPrompt(data.item.id);
        });
    }

    /** Deletes the selected prompt. */
    deletePrompt() {
        if (!this.currentPromptId) {
            return;
        }
        let deleting = this.currentPromptId;
        genericRequest('PromptLabDelete', { collection: 'prompts', id: deleting }, data => {
            this.data.prompts = this.data.prompts.filter(p => p.id != deleting);
            this.newPrompt();
            this.renderPromptList();
        });
    }

    /** Toggles favorite for the current prompt. */
    togglePromptFavorite() {
        this.toggleFavorite('prompts', this.currentPromptId, () => this.renderPromptList());
    }

    /** Toggles favorite for an item in a Prompt Lab collection. */
    toggleFavorite(collection, id, callback) {
        if (!id) {
            return;
        }
        let item = this.data[collection].find(i => i.id == id);
        if (!item) {
            return;
        }
        item.favorite = !item.favorite;
        genericRequest('PromptLabSave', { collection: collection, item: item }, data => {
            let saved = data.item;
            let existing = this.data[collection].findIndex(i => i.id == saved.id);
            if (existing != -1) {
                this.data[collection][existing] = saved;
            }
            callback();
        });
    }

    /** Renders the saved prompt list. */
    renderPromptList() {
        let list = document.getElementById('prompt_lab_prompt_list');
        if (!list) {
            return;
        }
        let search = (document.getElementById('prompt_lab_search')?.value || '').toLowerCase();
        let html = '';
        let prompts = this.sortedWithFavorites(this.data.prompts);
        for (let prompt of prompts) {
            let text = `${prompt.name || ''} ${(prompt.tags || []).join(' ')} ${prompt.notes || ''} ${prompt.positive || ''} ${prompt.negative || ''}`.toLowerCase();
            if (search && !text.includes(search)) {
                continue;
            }
            let selected = prompt.id == this.currentPromptId ? ' prompt-lab-list-item-selected' : '';
            let favorite = prompt.favorite ? '<span class="prompt-lab-favorite-marker">Favorite</span> ' : '';
            let variant = prompt.parent_id ? ' <span class="prompt-lab-count">variant</span>' : '';
            html += `<button class="prompt-lab-list-item${selected}" onclick="promptLab.loadPrompt('${escapeHtmlNoBr(escapeJsString(prompt.id))}')">${favorite}${escapeHtml(prompt.name || 'Untitled Prompt')}${variant}</button>`;
        }
        list.innerHTML = html || '<div class="prompt-lab-empty translate">No saved prompts.</div>';
    }

    /** Sorts Prompt Lab items with favorites first. */
    sortedWithFavorites(items) {
        return (items || []).slice().sort((a, b) => Number(b.favorite || false) - Number(a.favorite || false) || (a.name || '').localeCompare(b.name || ''));
    }

    /** Renders the prompt compare selector. */
    renderCompareSelect() {
        let select = document.getElementById('prompt_lab_compare_select');
        if (!select) {
            return;
        }
        let preferred = select.value || this.currentParentId || '';
        let html = '<option value="">Select prompt to compare</option>';
        for (let prompt of this.data.prompts) {
            if (prompt.id == this.currentPromptId) {
                continue;
            }
            let selected = prompt.id == preferred ? ' selected' : '';
            html += `<option value="${escapeHtmlNoBr(escapeJsString(prompt.id))}"${selected}>${escapeHtml(prompt.name || 'Untitled Prompt')}</option>`;
        }
        select.innerHTML = html;
    }

    /** Exports the full Prompt Lab library as JSON. */
    exportLibrary() {
        let stamp = new Date().toISOString().replaceAll(':', '-').replaceAll('.', '-');
        let data = {
            prompts: this.data.prompts || [],
            fragments: this.data.fragments || [],
            wildcards: this.data.wildcards || []
        };
        downloadPlainText(`prompt-lab-library-${stamp}.json`, JSON.stringify(data, null, 2));
    }

    /** Opens the Prompt Lab import file picker. */
    openImportPicker() {
        getRequiredElementById('prompt_lab_import_file').click();
    }

    /** Imports a Prompt Lab JSON file. */
    importLibraryFile(input) {
        if (!input.files || input.files.length == 0) {
            return;
        }
        let file = input.files[0];
        let reader = new FileReader();
        reader.onload = () => {
            input.value = '';
            this.importLibraryText(reader.result);
        };
        reader.readAsText(file);
    }

    /** Imports Prompt Lab JSON text. */
    importLibraryText(text) {
        let parsed;
        try {
            parsed = JSON.parse(text);
        }
        catch (err) {
            showError(`Failed to import Prompt Lab JSON: ${err}`);
            return;
        }
        this.importCollection('prompts', parsed.prompts || [], () => {
            this.importCollection('fragments', parsed.fragments || [], () => {
                this.importCollection('wildcards', parsed.wildcards || [], () => this.load());
            });
        });
    }

    /** Imports one Prompt Lab collection through the normal save API. */
    importCollection(collection, items, callback) {
        if (!Array.isArray(items) || items.length == 0) {
            callback();
            return;
        }
        let index = 0;
        let next = () => {
            if (index >= items.length) {
                callback();
                return;
            }
            let item = JSON.parse(JSON.stringify(items[index]));
            item.id = null;
            index++;
            genericRequest('PromptLabSave', { collection: collection, item: item }, () => next());
        };
        next();
    }

    /** Starts a blank fragment. */
    newFragment() {
        this.currentFragmentId = null;
        getRequiredElementById('prompt_lab_fragment_name').value = '';
        getRequiredElementById('prompt_lab_fragment_text').value = '';
        getRequiredElementById('prompt_lab_fragment_category').value = '';
        getRequiredElementById('prompt_lab_fragment_tags').value = '';
        this.renderFragmentList();
    }

    /** Returns the fragment editor contents as a Prompt Lab fragment object. */
    currentFragmentObject() {
        let tags = getRequiredElementById('prompt_lab_fragment_tags').value.split(',').map(t => t.trim()).filter(t => t);
        let existing = this.data.fragments.find(f => f.id == this.currentFragmentId);
        let item = {
            id: this.currentFragmentId,
            name: getRequiredElementById('prompt_lab_fragment_name').value.trim() || 'Untitled Fragment',
            text: getRequiredElementById('prompt_lab_fragment_text').value,
            category: getRequiredElementById('prompt_lab_fragment_category').value.trim(),
            tags: tags,
            favorite: existing?.favorite || false
        };
        return item;
    }

    /** Saves the current fragment. */
    saveFragment() {
        let item = this.currentFragmentObject();
        if (!item.text.trim()) {
            return;
        }
        genericRequest('PromptLabSave', { collection: 'fragments', item: item }, data => {
            let saved = data.item;
            this.currentFragmentId = saved.id;
            let existing = this.data.fragments.findIndex(f => f.id == saved.id);
            if (existing == -1) {
                this.data.fragments.push(saved);
            }
            else {
                this.data.fragments[existing] = saved;
            }
            this.renderFragmentList();
        });
    }

    /** Loads a fragment into the editor. */
    loadFragment(id) {
        let fragment = this.data.fragments.find(f => f.id == id);
        if (!fragment) {
            return;
        }
        this.currentFragmentId = fragment.id;
        getRequiredElementById('prompt_lab_fragment_name').value = fragment.name || '';
        getRequiredElementById('prompt_lab_fragment_text').value = fragment.text || '';
        getRequiredElementById('prompt_lab_fragment_category').value = fragment.category || '';
        getRequiredElementById('prompt_lab_fragment_tags').value = (fragment.tags || []).join(', ');
        this.renderFragmentList();
    }

    /** Deletes the selected fragment. */
    deleteFragment() {
        if (!this.currentFragmentId) {
            return;
        }
        let deleting = this.currentFragmentId;
        genericRequest('PromptLabDelete', { collection: 'fragments', id: deleting }, data => {
            this.data.fragments = this.data.fragments.filter(f => f.id != deleting);
            this.newFragment();
            this.renderFragmentList();
        });
    }

    /** Toggles favorite for the current fragment. */
    toggleFragmentFavorite() {
        this.toggleFavorite('fragments', this.currentFragmentId, () => this.renderFragmentList());
    }

    /** Renders the saved fragment list. */
    renderFragmentList() {
        let list = document.getElementById('prompt_lab_fragment_list');
        if (!list) {
            return;
        }
        let search = (document.getElementById('prompt_lab_fragment_search')?.value || '').toLowerCase();
        let html = '';
        let fragments = this.sortedWithFavorites(this.data.fragments);
        for (let fragment of fragments) {
            let text = `${fragment.name || ''} ${fragment.category || ''} ${(fragment.tags || []).join(' ')} ${fragment.text || ''}`.toLowerCase();
            if (search && !text.includes(search)) {
                continue;
            }
            let selected = fragment.id == this.currentFragmentId ? ' prompt-lab-list-item-selected' : '';
            let favorite = fragment.favorite ? '<span class="prompt-lab-favorite-marker">Favorite</span> ' : '';
            let category = fragment.category ? ` <span class="prompt-lab-count">${escapeHtml(fragment.category)}</span>` : '';
            html += `<button class="prompt-lab-list-item${selected}" onclick="promptLab.loadFragment('${escapeHtmlNoBr(escapeJsString(fragment.id))}')">${favorite}${escapeHtml(fragment.name || 'Untitled Fragment')}${category}</button>`;
        }
        list.innerHTML = html || '<div class="prompt-lab-empty translate">No saved fragments.</div>';
    }

    /** Inserts the selected fragment into the positive prompt. */
    insertSelectedFragment() {
        let text = getRequiredElementById('prompt_lab_fragment_text').value.trim();
        if (!text) {
            return;
        }
        this.insertTextIntoPositivePrompt(text);
    }

    /** Inserts text into the positive prompt at cursor position. */
    insertTextIntoPositivePrompt(insertText) {
        let box = getRequiredElementById('prompt_lab_positive');
        let range = getTextSelRange(box);
        let text = getTextContent(box);
        let prefix = text.substring(0, range[0]).trimEnd();
        let suffix = text.substring(range[1]).trimStart();
        let separatorBefore = prefix ? ', ' : '';
        let separatorAfter = suffix ? ', ' : '';
        let result = `${prefix}${separatorBefore}${insertText}${separatorAfter}${suffix}`;
        setTextContent(box, result);
        let cursor = prefix.length + separatorBefore.length + insertText.length;
        setTextSelRange(box, cursor, cursor);
        box.focus();
        this.refreshPreview();
    }

    /** Starts a blank wildcard set. */
    newWildcardSet() {
        this.currentWildcardId = null;
        getRequiredElementById('prompt_lab_wildcard_name').value = '';
        getRequiredElementById('prompt_lab_wildcard_values').value = '';
        getRequiredElementById('prompt_lab_wildcard_tags').value = '';
        this.renderWildcardList();
        this.refreshPreview();
    }

    /** Returns the wildcard editor contents as a Prompt Lab wildcard object. */
    currentWildcardObject() {
        let tags = getRequiredElementById('prompt_lab_wildcard_tags').value.split(',').map(t => t.trim()).filter(t => t);
        let values = getRequiredElementById('prompt_lab_wildcard_values').value.split('\n').map(t => t.trim()).filter(t => t);
        let existing = this.data.wildcards.find(w => w.id == this.currentWildcardId);
        let item = {
            id: this.currentWildcardId,
            name: getRequiredElementById('prompt_lab_wildcard_name').value.trim(),
            values: values,
            tags: tags,
            favorite: existing?.favorite || false
        };
        return item;
    }

    /** Saves the current wildcard set. */
    saveWildcardSet() {
        let item = this.currentWildcardObject();
        if (!item.name) {
            return;
        }
        genericRequest('PromptLabSave', { collection: 'wildcards', item: item }, data => {
            let saved = data.item;
            this.currentWildcardId = saved.id;
            let existing = this.data.wildcards.findIndex(w => w.id == saved.id);
            if (existing == -1) {
                this.data.wildcards.push(saved);
            }
            else {
                this.data.wildcards[existing] = saved;
            }
            this.renderWildcardList();
            this.refreshPreview();
        });
    }

    /** Loads a wildcard set into the editor. */
    loadWildcardSet(id) {
        let wildcard = this.data.wildcards.find(w => w.id == id);
        if (!wildcard) {
            return;
        }
        this.currentWildcardId = wildcard.id;
        getRequiredElementById('prompt_lab_wildcard_name').value = wildcard.name || '';
        getRequiredElementById('prompt_lab_wildcard_values').value = (wildcard.values || []).join('\n');
        getRequiredElementById('prompt_lab_wildcard_tags').value = (wildcard.tags || []).join(', ');
        this.renderWildcardList();
        this.refreshPreview();
    }

    /** Deletes the selected wildcard set. */
    deleteWildcardSet() {
        if (!this.currentWildcardId) {
            return;
        }
        let deleting = this.currentWildcardId;
        genericRequest('PromptLabDelete', { collection: 'wildcards', id: deleting }, data => {
            this.data.wildcards = this.data.wildcards.filter(w => w.id != deleting);
            this.newWildcardSet();
            this.renderWildcardList();
        });
    }

    /** Toggles favorite for the current wildcard set. */
    toggleWildcardFavorite() {
        this.toggleFavorite('wildcards', this.currentWildcardId, () => this.renderWildcardList());
    }

    /** Renders the saved wildcard set list. */
    renderWildcardList() {
        let list = document.getElementById('prompt_lab_wildcard_list');
        if (!list) {
            return;
        }
        let search = (document.getElementById('prompt_lab_wildcard_search')?.value || '').toLowerCase();
        let html = '';
        let wildcards = this.sortedWithFavorites(this.data.wildcards);
        for (let wildcard of wildcards) {
            let text = `${wildcard.name || ''} ${(wildcard.tags || []).join(' ')} ${(wildcard.values || []).join(' ')}`.toLowerCase();
            if (search && !text.includes(search)) {
                continue;
            }
            let selected = wildcard.id == this.currentWildcardId ? ' prompt-lab-list-item-selected' : '';
            let favorite = wildcard.favorite ? '<span class="prompt-lab-favorite-marker">Favorite</span> ' : '';
            let count = (wildcard.values || []).length;
            html += `<button class="prompt-lab-list-item${selected}" onclick="promptLab.loadWildcardSet('${escapeHtmlNoBr(escapeJsString(wildcard.id))}')">${favorite}${escapeHtml(wildcard.name || 'Untitled Wildcard')} <span class="prompt-lab-count">${count}</span></button>`;
        }
        list.innerHTML = html || '<div class="prompt-lab-empty translate">No saved wildcards.</div>';
    }

    /** Saves a recent Prompt Lab action. */
    addHistory(kind, positive, negative) {
        let item = {
            name: kind,
            positive: positive,
            negative: negative,
            source_prompt_id: this.currentPromptId || '',
            created_at: Date.now()
        };
        genericRequest('PromptLabSave', { collection: 'history', item: item }, data => {
            this.data.history.unshift(data.item);
            if (this.data.history.length > 25) {
                this.data.history = this.data.history.slice(0, 25);
            }
            this.renderHistoryList();
        });
    }

    /** Renders recent Prompt Lab history. */
    renderHistoryList() {
        let list = document.getElementById('prompt_lab_history_list');
        if (!list) {
            return;
        }
        let history = (this.data.history || []).slice().sort((a, b) => (b.created_at || 0) - (a.created_at || 0)).slice(0, 25);
        let html = '';
        for (let item of history) {
            let title = item.name || 'Recent Prompt';
            let preview = (item.positive || '').substring(0, 60);
            html += `<button class="prompt-lab-list-item" onclick="promptLab.loadHistoryPrompt('${escapeHtmlNoBr(escapeJsString(item.id))}')">${escapeHtml(title)} <span class="prompt-lab-history-preview">${escapeHtml(preview)}</span></button>`;
        }
        list.innerHTML = html || '<div class="prompt-lab-empty translate">No recent prompts.</div>';
    }

    /** Clears recent Prompt Lab history locally and from storage. */
    clearHistory() {
        let history = (this.data.history || []).slice();
        this.data.history = [];
        this.renderHistoryList();
        for (let item of history) {
            genericRequest('PromptLabDelete', { collection: 'history', id: item.id }, data => {});
        }
    }

    /** Loads a history prompt into the editor. */
    loadHistoryPrompt(id) {
        let item = (this.data.history || []).find(h => h.id == id);
        if (!item) {
            return;
        }
        this.currentPromptId = null;
        this.currentParentId = item.source_prompt_id || null;
        getRequiredElementById('prompt_lab_name').value = item.name || 'Recent Prompt';
        getRequiredElementById('prompt_lab_positive').value = item.positive || '';
        getRequiredElementById('prompt_lab_negative').value = item.negative || '';
        getRequiredElementById('prompt_lab_tags').value = '';
        getRequiredElementById('prompt_lab_notes').value = '';
        this.renderPromptList();
        this.renderCompareSelect();
        this.refreshPreview();
    }

    /** Inserts the selected wildcard token into the positive prompt. */
    insertSelectedWildcard() {
        let name = getRequiredElementById('prompt_lab_wildcard_name').value.trim();
        if (!name) {
            return;
        }
        let box = getRequiredElementById('prompt_lab_positive');
        let range = getTextSelRange(box);
        let token = `<wildcard:${name}>`;
        let text = getTextContent(box);
        setTextContent(box, text.substring(0, range[0]) + token + text.substring(range[1]));
        setTextSelRange(box, range[0] + token.length, range[0] + token.length);
        box.focus();
        this.refreshPreview();
    }

    /** Gets the Prompt Lab value count for a wildcard token. */
    getPromptLabWildcardCount(token) {
        if (token.toLowerCase().startsWith('random:')) {
            let values = token.substring('random:'.length);
            let splitChar = values.includes('|') ? '|' : ',';
            return values.split(splitChar).map(v => v.trim()).filter(v => v).length;
        }
        let wildcard = this.data.wildcards.find(w => (w.name || '').toLowerCase() == token.toLowerCase());
        if (!wildcard) {
            return null;
        }
        return (wildcard.values || []).filter(v => v).length;
    }

    /** Detects local wildcard tokens in the editor. */
    detectWildcardTokens() {
        let positive = getRequiredElementById('prompt_lab_positive').value;
        let negative = getRequiredElementById('prompt_lab_negative').value;
        let matcher = /<(wildcard|random):([^>]+)>/gi;
        let tokens = [];
        for (let text of [positive, negative]) {
            let match;
            while ((match = matcher.exec(text)) != null) {
                let type = match[1].trim().toLowerCase();
                let token = match[2].trim();
                if (type == 'random') {
                    token = `random:${token}`;
                }
                if (token && !tokens.some(t => t.toLowerCase() == token.toLowerCase())) {
                    tokens.push(token);
                }
            }
        }
        return tokens;
    }

    /** Renders the detected wildcard summary. */
    renderDetectedWildcards(tokens) {
        if (tokens.length == 0) {
            return '<span class="translate">None</span>';
        }
        let html = '';
        for (let token of tokens) {
            let count = this.getPromptLabWildcardCount(token);
            let countText = count == null ? '' : ` <span class="prompt-lab-count">${count}</span>`;
            let label = token.toLowerCase().startsWith('random:') ? `&lt;${escapeHtml(token)}&gt;` : `&lt;wildcard:${escapeHtml(token)}&gt;`;
            html += `<div>${label}${countText}</div>`;
        }
        return html;
    }

    /** Builds local prompt warnings and counts. */
    getPromptDiagnostics() {
        let positive = getRequiredElementById('prompt_lab_positive').value || '';
        let negative = getRequiredElementById('prompt_lab_negative').value || '';
        let warnings = [];
        this.addPromptTextWarnings(warnings, positive, 'Positive');
        this.addPromptTextWarnings(warnings, negative, 'Negative');
        return {
            positive_chars: positive.length,
            negative_chars: negative.length,
            warnings: warnings
        };
    }

    /** Adds prompt text warnings for a single prompt field. */
    addPromptTextWarnings(warnings, text, label) {
        this.addBalanceWarning(warnings, text, label, '(', ')');
        this.addBalanceWarning(warnings, text, label, '[', ']');
        this.addBalanceWarning(warnings, text, label, '{', '}');
        if (text.match(/<wildcard:\s*>/i)) {
            warnings.push(`${label}: empty wildcard name.`);
        }
        if (text.match(/<wildcard:[^>]*$/i)) {
            warnings.push(`${label}: incomplete wildcard token.`);
        }
        let seen = {};
        let duplicates = [];
        let parts = text.split(',');
        for (let part of parts) {
            let clean = part.trim().toLowerCase();
            if (!clean || clean.length < 3) {
                continue;
            }
            if (seen[clean] && !duplicates.includes(clean)) {
                duplicates.push(clean);
            }
            seen[clean] = true;
        }
        if (duplicates.length > 0) {
            warnings.push(`${label}: duplicate terms: ${duplicates.join(', ')}.`);
        }
    }

    /** Adds a bracket balance warning when a prompt is uneven. */
    addBalanceWarning(warnings, text, label, open, close) {
        let balance = 0;
        for (let i = 0; i < text.length; i++) {
            if (text[i] == open) {
                balance++;
            }
            else if (text[i] == close) {
                balance--;
            }
            if (balance < 0) {
                warnings.push(`${label}: unmatched ${close}.`);
                return;
            }
        }
        if (balance > 0) {
            warnings.push(`${label}: unmatched ${open}.`);
        }
    }

    /** Renders warning text. */
    renderWarnings(warnings) {
        return warnings.length ? warnings.map(w => `<div>${escapeHtml(w)}</div>`).join('') : '<span class="translate">None</span>';
    }

    /** Refreshes the local preview panel. */
    refreshPreview() {
        let wildcardBox = document.getElementById('prompt_lab_wildcards');
        let previewBox = document.getElementById('prompt_lab_preview');
        let warningBox = document.getElementById('prompt_lab_warnings');
        let diffBox = document.getElementById('prompt_lab_diff');
        if (!wildcardBox || !previewBox || !warningBox || !diffBox) {
            return;
        }
        let tokens = this.detectWildcardTokens();
        let diagnostics = this.getPromptDiagnostics();
        wildcardBox.innerHTML = this.renderDetectedWildcards(tokens);
        let positive = escapeHtml(getRequiredElementById('prompt_lab_positive').value || '');
        let negative = escapeHtml(getRequiredElementById('prompt_lab_negative').value || '');
        previewBox.innerHTML = `<div>Positive chars: ${diagnostics.positive_chars} | Negative chars: ${diagnostics.negative_chars}</div><br><b>Positive</b><br>${positive}<br><br><b>Negative</b><br>${negative}`;
        warningBox.innerHTML = this.renderWarnings(diagnostics.warnings);
        diffBox.innerHTML = this.renderCurrentDiff();
    }

    /** Renders a diff between the current editor prompt and selected compare prompt. */
    renderCurrentDiff() {
        let select = document.getElementById('prompt_lab_compare_select');
        let compareId = select?.value || this.currentParentId;
        if (!compareId) {
            return '<span class="translate">Select a prompt to compare.</span>';
        }
        let compare = this.data.prompts.find(p => p.id == compareId);
        if (!compare) {
            return '<span class="translate">Compare prompt not found.</span>';
        }
        let current = this.currentPromptObject();
        return `<b>Positive</b>${this.renderTextDiff(compare.positive || '', current.positive || '')}<br><b>Negative</b>${this.renderTextDiff(compare.negative || '', current.negative || '')}`;
    }

    /** Renders added/removed comma-separated prompt terms. */
    renderTextDiff(before, after) {
        let beforeParts = this.splitPromptForDiff(before);
        let afterParts = this.splitPromptForDiff(after);
        let removed = beforeParts.filter(part => !afterParts.includes(part));
        let added = afterParts.filter(part => !beforeParts.includes(part));
        if (removed.length == 0 && added.length == 0) {
            return '<div class="prompt-lab-diff-same translate">No changes.</div>';
        }
        let html = '';
        for (let part of removed) {
            html += `<div class="prompt-lab-diff-removed">- ${escapeHtml(part)}</div>`;
        }
        for (let part of added) {
            html += `<div class="prompt-lab-diff-added">+ ${escapeHtml(part)}</div>`;
        }
        return html;
    }

    /** Splits a prompt into stable diff terms. */
    splitPromptForDiff(text) {
        return text.split(',').map(part => part.trim()).filter(part => part);
    }

    /** Requests wildcard expansion preview from the backend. */
    getWildcardExpansionRequest(modeOverride = null) {
        return {
            positive: getRequiredElementById('prompt_lab_positive').value,
            negative: getRequiredElementById('prompt_lab_negative').value,
            mode: modeOverride || getRequiredElementById('prompt_lab_wildcard_mode').value,
            sample_count: parseInt(getRequiredElementById('prompt_lab_sample_count').value) || 25,
            max_combinations: parseInt(getRequiredElementById('prompt_lab_max_combinations').value) || 1000
        };
    }

    /** Requests wildcard expansion preview from the backend. */
    previewWildcards() {
        let request = this.getWildcardExpansionRequest();
        genericRequest('PromptLabExpandWildcards', request, data => {
            let wildcardBox = getRequiredElementById('prompt_lab_wildcards');
            let previewBox = getRequiredElementById('prompt_lab_preview');
            let warningBox = getRequiredElementById('prompt_lab_warnings');
            wildcardBox.innerHTML = this.renderDetectedWildcards(data.tokens || []);
            let promptHtml = '';
            for (let prompt of data.prompts) {
                promptHtml += `<div class="prompt-lab-expanded-prompt">${escapeHtml(prompt.positive)}<br><span class="text-muted">${escapeHtml(prompt.negative || '')}</span></div>`;
            }
            previewBox.innerHTML = `<div>${data.returned_combinations} / ${data.total_possible_combinations}</div>${promptHtml}`;
            let diagnostics = this.getPromptDiagnostics();
            let warnings = diagnostics.warnings.concat(data.warnings || []);
            warningBox.innerHTML = this.renderWarnings(warnings);
        });
    }

    /** Generates all currently expanded wildcard combinations with normal Generate settings. */
    generateWildcardCombinations() {
        let request = this.getWildcardExpansionRequest('all');
        genericRequest('PromptLabExpandWildcards', request, data => {
            if (!data.prompts || data.prompts.length == 0) {
                showError('No wildcard combinations to generate.');
                return;
            }
            if (data.returned_combinations < data.total_possible_combinations) {
                showError(`Wildcard combinations exceed the max limit. Increase max combinations to generate all ${data.total_possible_combinations}.`);
                return;
            }
            if (data.total_possible_combinations > 1000 && !confirm(`This will create ${data.total_possible_combinations} generation jobs. Continue?`)) {
                return;
            }
            this.addHistory('Generated Combinations', getRequiredElementById('prompt_lab_positive').value, getRequiredElementById('prompt_lab_negative').value);
            this.pendingWildcardGenerations = data.prompts.slice();
            this.runNextWildcardGeneration();
            openGenPageTab('text2imagetabbutton');
        });
    }

    /** Exports all expanded wildcard combinations. */
    exportWildcardCombinations(format) {
        let request = this.getWildcardExpansionRequest('all');
        genericRequest('PromptLabExpandWildcards', request, data => {
            if (!data.prompts || data.prompts.length == 0) {
                showError('No wildcard combinations to export.');
                return;
            }
            if (data.returned_combinations < data.total_possible_combinations) {
                showError(`Wildcard combinations exceed the max limit. Increase max combinations to export all ${data.total_possible_combinations}.`);
                return;
            }
            let stamp = new Date().toISOString().replaceAll(':', '-').replaceAll('.', '-');
            if (format == 'json') {
                downloadPlainText(`prompt-lab-combinations-${stamp}.json`, JSON.stringify(data.prompts, null, 2));
                return;
            }
            if (format == 'csv') {
                let csv = 'index,positive,negative\n';
                for (let i = 0; i < data.prompts.length; i++) {
                    let prompt = data.prompts[i];
                    csv += `${i + 1},"${(`${prompt.positive || ''}`).replaceAll('"', '""')}","${(`${prompt.negative || ''}`).replaceAll('"', '""')}"\n`;
                }
                downloadPlainText(`prompt-lab-combinations-${stamp}.csv`, csv);
                return;
            }
            let text = '';
            for (let i = 0; i < data.prompts.length; i++) {
                let prompt = data.prompts[i];
                text += `# ${i + 1}\nPositive: ${prompt.positive}\nNegative: ${prompt.negative || ''}\n\n`;
            }
            downloadPlainText(`prompt-lab-combinations-${stamp}.txt`, text);
        });
    }

    /** Runs the next queued wildcard generation after the normal generation socket is usable. */
    runNextWildcardGeneration() {
        if (this.pendingWildcardGenerations.length == 0) {
            return;
        }
        let socket = mainGenHandler.sockets.normal;
        if (socket && socket.readyState == WebSocket.CONNECTING) {
            setTimeout(() => this.runNextWildcardGeneration(), 50);
            return;
        }
        if (!socket && this.isStartingWildcardGenerationSocket) {
            setTimeout(() => this.runNextWildcardGeneration(), 50);
            return;
        }
        if (socket && socket.readyState == WebSocket.OPEN) {
            this.isStartingWildcardGenerationSocket = false;
        }
        let prompt = this.pendingWildcardGenerations.shift();
        if (!socket) {
            this.isStartingWildcardGenerationSocket = true;
        }
        this.generateExpandedPrompt(prompt);
        setTimeout(() => this.runNextWildcardGeneration(), 0);
    }

    /** Queues one expanded prompt for generation. */
    generateExpandedPrompt(prompt) {
        let overrides = {
            prompt: prompt.positive,
            negativeprompt: prompt.negative
        };
        mainGenHandler.doGenerate(overrides, {}, actualInput => {
            actualInput.extra_metadata = actualInput.extra_metadata || {};
            actualInput.extra_metadata.prompt_lab_id = this.currentPromptId || '';
            actualInput.extra_metadata.prompt_lab_wildcard_values = JSON.stringify(prompt.wildcard_values || {});
        });
    }

    /** Sends the editor prompt pair to the Generate tab. */
    sendToGenerate() {
        let promptParam = getParamById('prompt');
        let negativeParam = getParamById('negativeprompt');
        if (promptParam) {
            setDirectParamValue(promptParam, getRequiredElementById('prompt_lab_positive').value);
        }
        if (negativeParam) {
            setDirectParamValue(negativeParam, getRequiredElementById('prompt_lab_negative').value);
        }
        this.addHistory('Sent to Generate', getRequiredElementById('prompt_lab_positive').value, getRequiredElementById('prompt_lab_negative').value);
        openGenPageTab('text2imagetabbutton');
    }
}

let promptLab = new PromptLab();
sessionReadyCallbacks.push(() => promptLab.init());
