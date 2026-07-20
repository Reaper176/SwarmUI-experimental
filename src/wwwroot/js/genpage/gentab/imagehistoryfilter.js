class ImageHistoryFilter {
    /** Creates the image-history metadata and query cache. */
    constructor() {
        this.metadataCacheLimit = 1024;
        this.metadataCache = new Map();
        this.compiledFilterText = null;
        this.compiledFilterTerms = [];
    }

    /**
     * Parses image-history metadata through the bounded collaborator cache.
     */
    parseMetadata(metadata) {
        if (!metadata) {
            return {};
        }
        if (typeof metadata == 'object') {
            return metadata;
        }
        if (this.metadataCache.has(metadata)) {
            return this.metadataCache.get(metadata);
        }
        let parsed = {};
        try {
            parsed = JSON.parse(interpretMetadata(metadata)) || {};
        }
        catch (e) {
            parsed = {};
        }
        if (this.metadataCache.size >= this.metadataCacheLimit) {
            let firstKey = this.metadataCache.keys().next().value;
            this.metadataCache.delete(firstKey);
        }
        this.metadataCache.set(metadata, parsed);
        return parsed;
    }

    /**
     * Converts a metadata value into searchable text.
     */
    valueToSearchText(value) {
        if (value == null) {
            return '';
        }
        if (Array.isArray(value)) {
            return value.map(v => this.valueToSearchText(v)).join(' ');
        }
        if (typeof value == 'object') {
            return Object.values(value).map(v => this.valueToSearchText(v)).join(' ');
        }
        return `${value}`;
    }

    /**
     * Builds field-specific searchable metadata for the image history filter.
     */
    getSearchFields(image, parsedMeta) {
        let params = parsedMeta.sui_image_params || {};
        let extra = parsedMeta.sui_extra_data || {};
        let name = image.data.name || '';
        let fullsrc = image.data.fullsrc || '';
        let folder = fullsrc.includes('/') ? fullsrc.substring(0, fullsrc.lastIndexOf('/')) : '';
        let extension = name.includes('.') ? name.split('.').pop().toLowerCase() : '';
        let rawMetadata = typeof image.data.metadata == 'string' ? image.data.metadata : this.valueToSearchText(image.data.metadata);
        let fileSize = Number.parseInt(image.data.file_size || 0) || 0;
        let fileTime = Number.parseInt(image.data.file_time || 0) || 0;
        let fileDate = fileTime ? new Date(fileTime * 1000).toISOString() : '';
        let hasMetadataText = rawMetadata ? 'true yes metadata' : 'false no none';
        let generationResolution = params.width && params.height ? `${params.width}x${params.height}` : '';
        let finalResolution = extra.final_width && extra.final_height ? `${extra.final_width}x${extra.final_height}` : generationResolution;
        let favoriteText = parsedMeta.is_starred ? 'true yes starred favorite' : 'false no unstarred';
        let hiddenText = parsedMeta.is_hidden ? 'true yes hidden' : 'false no visible';
        let width = Number.parseInt(params.width || 0) || 0;
        let height = Number.parseInt(params.height || 0) || 0;
        let finalWidth = Number.parseInt(extra.final_width || width || 0) || 0;
        let finalHeight = Number.parseInt(extra.final_height || height || 0) || 0;
        let fields = {
            name: name,
            path: fullsrc,
            folder: folder,
            type: extension,
            filetype: extension,
            filesize: fileSize ? `${fileSize} ${largeCountStringify(fileSize)}` : '',
            has: hasMetadataText,
            date: `${fileDate} ${fileDate.substring(0, 10)} ${fullsrc}`,
            metadata: `${rawMetadata} ${this.valueToSearchText(parsedMeta)}`,
            prompt: `${params.prompt || ''} ${extra.original_prompt || ''}`,
            negative: params.negativeprompt || '',
            model: params.model || '',
            lora: `${this.valueToSearchText(params.loras)} ${this.valueToSearchText(params.loraweights)}`,
            vae: params.vae || '',
            sampler: params.sampler || '',
            scheduler: params.scheduler || '',
            seed: params.seed || '',
            resolution: `${generationResolution} ${finalResolution}`,
            width: `${width} ${finalWidth}`,
            height: `${height} ${finalHeight}`,
            finalwidth: finalWidth,
            finalheight: finalHeight,
            steps: params.steps || '',
            cfg: params.cfgscale || '',
            rating: parsedMeta.rating || extra.rating || '',
            favorite: favoriteText,
            hidden: hiddenText,
            notes: `${parsedMeta.notes || ''} ${extra.notes || ''}`,
            tags: `${this.valueToSearchText(parsedMeta.tags)} ${this.valueToSearchText(extra.tags)}`,
            session: `${params.session_id || ''} ${extra.session_id || ''}`,
            wildcard: `${extra.prompt_lab_wildcard_values || ''} ${this.valueToSearchText(extra.prompt_lab_wildcards)}`,
            promptlab: `${extra.prompt_lab_id || ''} ${extra.prompt_lab_prompt_id || ''}`
        };
        fields.allFields = Object.values(fields).join(' ');
        return fields;
    }

    /**
     * Splits a search query into terms while preserving quoted text.
     */
    splitQuery(filter) {
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
    normalizeField(field) {
        let aliases = {
            fav: 'favorite',
            starred: 'favorite',
            hide: 'hidden',
            is_hidden: 'hidden',
            tag: 'tags',
            neg: 'negative',
            negativeprompt: 'negative',
            loras: 'lora',
            res: 'resolution',
            file_size: 'filesize',
            size: 'filesize',
            final_width: 'finalwidth',
            final_height: 'finalheight',
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
     * Compiles a history filter once per input string so each image does not repeat the same parsing work.
     */
    compileQuery(filter) {
        if (this.compiledFilterText == filter) {
            return this.compiledFilterTerms;
        }
        this.compiledFilterText = filter;
        this.compiledFilterTerms = [];
        let rawTerms = this.splitQuery(filter);
        for (let term of rawTerms) {
            let fieldSplit = term.indexOf(':');
            if (fieldSplit > 0) {
                let field = this.normalizeField(term.substring(0, fieldSplit).toLowerCase());
                let value = term.substring(fieldSplit + 1).toLowerCase();
                if (!value) {
                    continue;
                }
                this.compiledFilterTerms.push({ field, value, raw: term.toLowerCase() });
            }
            else {
                this.compiledFilterTerms.push({ field: null, value: term.toLowerCase(), raw: term.toLowerCase() });
            }
        }
        return this.compiledFilterTerms;
    }

    /**
     * Returns searchable text for object, string, or fallback entry descriptions.
     */
    getSearchableText(desc, searchable) {
        let allFields = '';
        if (typeof searchable == 'string') {
            allFields = searchable;
        }
        else if (typeof searchable == 'object') {
            allFields = searchable.allFields || searchable.all_fields || Object.values(searchable).join(' ');
        }
        else if (searchable != null) {
            allFields = `${searchable}`;
        }
        return `${allFields} ${desc.description || ''} ${desc.name || ''} ${desc.display || ''}`.toLowerCase();
    }

    /**
     * Matches a numeric comparison filter against a field value.
     */
    numericMatches(fieldText, value) {
        let match = value.match(/^(>=|<=|>|<|=)(-?\d+(?:\.\d+)?)$/);
        if (!match) {
            return null;
        }
        let actual = Number.parseFloat(fieldText);
        let expected = Number.parseFloat(match[2]);
        if (!Number.isFinite(actual) || !Number.isFinite(expected)) {
            return false;
        }
        if (match[1] == '>=') {
            return actual >= expected;
        }
        if (match[1] == '<=') {
            return actual <= expected;
        }
        if (match[1] == '>') {
            return actual > expected;
        }
        if (match[1] == '<') {
            return actual < expected;
        }
        return actual == expected;
    }

    /**
     * Matches a date comparison filter against a field value.
     */
    dateMatches(fieldText, value) {
        let match = value.match(/^(>=|<=|>|<|=)(\d{4}-\d{2}-\d{2}(?:T[^ ]*)?)$/);
        if (!match) {
            return null;
        }
        let actual = Date.parse(`${fieldText}`.split(' ')[0]);
        let expected = Date.parse(match[2]);
        if (!Number.isFinite(actual) || !Number.isFinite(expected)) {
            return false;
        }
        if (match[1] == '>=') {
            return actual >= expected;
        }
        if (match[1] == '<=') {
            return actual <= expected;
        }
        if (match[1] == '>') {
            return actual > expected;
        }
        if (match[1] == '<') {
            return actual < expected;
        }
        return actual == expected;
    }

    /**
     * Matches image history entries against text and field:value search terms.
     */
    matches(desc, filter) {
        if (!filter) {
            return true;
        }
        let searchable = desc.searchable || {};
        let allFields = this.getSearchableText(desc, searchable);
        let terms = this.compileQuery(filter);
        for (let term of terms) {
            if (term.field) {
                let field = term.field;
                let value = term.value;
                let fieldText = searchable[field];
                if (fieldText == null) {
                    if (!allFields.includes(term.raw)) {
                        return false;
                    }
                    continue;
                }
                let numericMatch = this.numericMatches(fieldText, value);
                if (numericMatch != null) {
                    if (!numericMatch) {
                        return false;
                    }
                    continue;
                }
                let dateMatch = this.dateMatches(fieldText, value);
                if (dateMatch != null) {
                    if (!dateMatch) {
                        return false;
                    }
                    continue;
                }
                if (!`${fieldText}`.toLowerCase().includes(value)) {
                    return false;
                }
            }
            else if (!allFields.includes(term.value)) {
                return false;
            }
        }
        return true;
    }

    /**
     * Updates the history filter input hint after the browser builds its header.
     */
    updateHint() {
        let filterInput = document.getElementById('imagehistorybrowser_filter_input');
        if (!filterInput || filterInput.dataset.historyFilterHint == 'true') {
            return;
        }
        filterInput.dataset.historyFilterHint = 'true';
        filterInput.placeholder = 'Search prompt/model/metadata...';
        filterInput.title = 'Search text, or use field:value terms like model:sdxl seed:123 date:>=2026-04-01 rating:>=4 hidden:true wildcard:dragon.';
    }
}
