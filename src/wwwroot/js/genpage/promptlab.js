class PromptLab {
    /** Builds the Prompt Lab helper. */
    constructor() {
        this.data = { prompts: [], fragments: [], wildcards: [] };
        this.currentPromptId = null;
        this.hasLoaded = false;
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
            this.refreshPreview();
        });
    }

    /** Starts a blank prompt. */
    newPrompt() {
        this.currentPromptId = null;
        getRequiredElementById('prompt_lab_name').value = '';
        getRequiredElementById('prompt_lab_positive').value = '';
        getRequiredElementById('prompt_lab_negative').value = '';
        getRequiredElementById('prompt_lab_tags').value = '';
        getRequiredElementById('prompt_lab_notes').value = '';
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
        });
    }

    /** Loads a prompt into the editor. */
    loadPrompt(id) {
        let prompt = this.data.prompts.find(p => p.id == id);
        if (!prompt) {
            return;
        }
        this.currentPromptId = prompt.id;
        getRequiredElementById('prompt_lab_name').value = prompt.name || '';
        getRequiredElementById('prompt_lab_positive').value = prompt.positive || '';
        getRequiredElementById('prompt_lab_negative').value = prompt.negative || '';
        getRequiredElementById('prompt_lab_tags').value = (prompt.tags || []).join(', ');
        getRequiredElementById('prompt_lab_notes').value = prompt.notes || '';
        this.refreshPreview();
        this.renderPromptList();
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
            html += `<button class="prompt-lab-list-item${selected}" onclick="promptLab.loadPrompt('${escapeHtmlNoBr(escapeJsString(prompt.id))}')">${escapeHtml(prompt.name || 'Untitled Prompt')}</button>`;
        }
        list.innerHTML = html || '<div class="prompt-lab-empty translate">No saved prompts.</div>';
    }

    /** Detects local wildcard tokens in the editor. */
    detectWildcardTokens() {
        let positive = getRequiredElementById('prompt_lab_positive').value;
        let negative = getRequiredElementById('prompt_lab_negative').value;
        let matcher = /<wildcard:([^>]+)>/gi;
        let tokens = [];
        for (let text of [positive, negative]) {
            let match;
            while ((match = matcher.exec(text)) != null) {
                let token = match[1].trim();
                if (token && !tokens.some(t => t.toLowerCase() == token.toLowerCase())) {
                    tokens.push(token);
                }
            }
        }
        return tokens;
    }

    /** Refreshes the local preview panel. */
    refreshPreview() {
        let wildcardBox = document.getElementById('prompt_lab_wildcards');
        let previewBox = document.getElementById('prompt_lab_preview');
        let warningBox = document.getElementById('prompt_lab_warnings');
        if (!wildcardBox || !previewBox || !warningBox) {
            return;
        }
        let tokens = this.detectWildcardTokens();
        wildcardBox.innerHTML = tokens.length ? tokens.map(t => `<div>&lt;wildcard:${escapeHtml(t)}&gt;</div>`).join('') : '<span class="translate">None</span>';
        let positive = escapeHtml(getRequiredElementById('prompt_lab_positive').value || '');
        let negative = escapeHtml(getRequiredElementById('prompt_lab_negative').value || '');
        previewBox.innerHTML = `<b>Positive</b><br>${positive}<br><br><b>Negative</b><br>${negative}`;
        warningBox.innerHTML = '';
    }

    /** Requests wildcard expansion preview from the backend. */
    previewWildcards() {
        let request = {
            positive: getRequiredElementById('prompt_lab_positive').value,
            negative: getRequiredElementById('prompt_lab_negative').value,
            mode: 'all',
            max_combinations: 25
        };
        genericRequest('PromptLabExpandWildcards', request, data => {
            let wildcardBox = getRequiredElementById('prompt_lab_wildcards');
            let previewBox = getRequiredElementById('prompt_lab_preview');
            let warningBox = getRequiredElementById('prompt_lab_warnings');
            wildcardBox.innerHTML = data.tokens.length ? data.tokens.map(t => `<div>&lt;wildcard:${escapeHtml(t)}&gt;</div>`).join('') : '<span class="translate">None</span>';
            let promptHtml = '';
            for (let prompt of data.prompts) {
                promptHtml += `<div class="prompt-lab-expanded-prompt">${escapeHtml(prompt.positive)}<br><span class="text-muted">${escapeHtml(prompt.negative || '')}</span></div>`;
            }
            previewBox.innerHTML = `<div>${data.returned_combinations} / ${data.total_possible_combinations}</div>${promptHtml}`;
            warningBox.innerHTML = (data.warnings || []).map(w => `<div>${escapeHtml(w)}</div>`).join('');
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
