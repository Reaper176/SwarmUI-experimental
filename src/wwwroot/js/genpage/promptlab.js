class PromptLab {
    /** Builds the Prompt Lab helper. */
    constructor() {
        this.data = { prompts: [], fragments: [], wildcards: [] };
        this.currentPromptId = null;
        this.currentParentId = null;
        this.currentWildcardId = null;
        this.hasLoaded = false;
        this.pendingWildcardGenerations = [];
        this.isStartingWildcardGenerationSocket = false;
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
            this.renderWildcardList();
            this.renderCompareSelect();
            this.refreshPreview();
        });
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
        let item = {
            id: this.currentPromptId,
            name: getRequiredElementById('prompt_lab_name').value || 'Untitled Prompt',
            positive: getRequiredElementById('prompt_lab_positive').value,
            negative: getRequiredElementById('prompt_lab_negative').value,
            tags: tags,
            notes: getRequiredElementById('prompt_lab_notes').value,
            parent_id: this.currentParentId,
            favorite: false
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

    /** Renders the saved prompt list. */
    renderPromptList() {
        let list = document.getElementById('prompt_lab_prompt_list');
        if (!list) {
            return;
        }
        let search = (document.getElementById('prompt_lab_search')?.value || '').toLowerCase();
        let html = '';
        for (let prompt of this.data.prompts) {
            let text = `${prompt.name || ''} ${(prompt.tags || []).join(' ')}`.toLowerCase();
            if (search && !text.includes(search)) {
                continue;
            }
            let selected = prompt.id == this.currentPromptId ? ' prompt-lab-list-item-selected' : '';
            let variant = prompt.parent_id ? ' <span class="prompt-lab-count">variant</span>' : '';
            html += `<button class="prompt-lab-list-item${selected}" onclick="promptLab.loadPrompt('${escapeHtmlNoBr(escapeJsString(prompt.id))}')">${escapeHtml(prompt.name || 'Untitled Prompt')}${variant}</button>`;
        }
        list.innerHTML = html || '<div class="prompt-lab-empty translate">No saved prompts.</div>';
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
        let item = {
            id: this.currentWildcardId,
            name: getRequiredElementById('prompt_lab_wildcard_name').value.trim(),
            values: values,
            tags: tags
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

    /** Renders the saved wildcard set list. */
    renderWildcardList() {
        let list = document.getElementById('prompt_lab_wildcard_list');
        if (!list) {
            return;
        }
        let search = (document.getElementById('prompt_lab_wildcard_search')?.value || '').toLowerCase();
        let html = '';
        for (let wildcard of this.data.wildcards) {
            let text = `${wildcard.name || ''} ${(wildcard.tags || []).join(' ')}`.toLowerCase();
            if (search && !text.includes(search)) {
                continue;
            }
            let selected = wildcard.id == this.currentWildcardId ? ' prompt-lab-list-item-selected' : '';
            let count = (wildcard.values || []).length;
            html += `<button class="prompt-lab-list-item${selected}" onclick="promptLab.loadWildcardSet('${escapeHtmlNoBr(escapeJsString(wildcard.id))}')">${escapeHtml(wildcard.name || 'Untitled Wildcard')} <span class="prompt-lab-count">${count}</span></button>`;
        }
        list.innerHTML = html || '<div class="prompt-lab-empty translate">No saved wildcards.</div>';
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
            this.pendingWildcardGenerations = data.prompts.slice();
            this.runNextWildcardGeneration();
            openGenPageTab('text2imagetabbutton');
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
        openGenPageTab('text2imagetabbutton');
    }
}

let promptLab = new PromptLab();
sessionReadyCallbacks.push(() => promptLab.init());
