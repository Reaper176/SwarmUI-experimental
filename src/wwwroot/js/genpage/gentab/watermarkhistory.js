/** Stores and displays the recently submitted custom watermark images. */
class WatermarkRecentHistory {

    /** Initializes the recent watermark history settings. */
    constructor() {
        this.databaseName = 'swarm-watermark-history';
        this.databaseVersion = 1;
        this.storeName = 'recent-watermarks';
        this.maxEntries = 5;
        this.containerId = 'watermark_recent_history';
        this.inputId = 'input_watermarkimage';
        this.writeQueue = Promise.resolve();
        this.lastUpdated = 0;
        this.pendingDeletionKeys = new Set();
    }

    /** Opens the IndexedDB database used for recent watermark entries. */
    openDatabase() {
        return new Promise((resolve, reject) => {
            let request;
            let rejected = false;
            try {
                request = indexedDB.open(this.databaseName, this.databaseVersion);
            }
            catch (error) {
                reject(error);
                return;
            }
            request.onupgradeneeded = () => {
                let database = request.result;
                if (!database.objectStoreNames.contains(this.storeName)) {
                    database.createObjectStore(this.storeName, { keyPath: 'id' });
                }
            };
            request.onsuccess = () => {
                if (rejected) {
                    request.result.close();
                    return;
                }
                resolve(request.result);
            };
            request.onerror = () => {
                rejected = true;
                reject(request.error || new Error('Unable to open recent watermark history.'));
            };
            request.onblocked = () => {
                rejected = true;
                reject(new Error('Recent watermark history database is blocked.'));
            };
        });
    }

    /** Waits for an IndexedDB transaction to finish or fail. */
    waitForTransaction(transaction) {
        return new Promise((resolve, reject) => {
            transaction.oncomplete = () => {
                resolve();
            };
            transaction.onerror = () => {
                reject(transaction.error || new Error('Recent watermark history transaction failed.'));
            };
            transaction.onabort = () => {
                reject(transaction.error || new Error('Recent watermark history transaction was aborted.'));
            };
        });
    }

    /** Gets at most the five newest stored watermark entries. */
    async getEntries() {
        let database = await this.openDatabase();
        try {
            let transaction = database.transaction(this.storeName, 'readonly');
            let request = transaction.objectStore(this.storeName).getAll();
            await this.waitForTransaction(transaction);
            return request.result.sort((first, second) => second.updated - first.updated).slice(0, this.maxEntries);
        }
        finally {
            database.close();
        }
    }

    /** Gets a preview URL while preserving the stored source value. */
    getPreviewSource(source) {
        if (isValidMediaPath(source)) {
            return `${getImageOutPrefix()}/${source}`;
        }
        return source;
    }

    /** Attaches the thumbnail row below the current watermark image input. */
    attach() {
        try {
            let input = document.getElementById(this.inputId);
            if (!input) {
                return;
            }
            let inputContainer = findParentOfClass(input, 'auto-input');
            if (!inputContainer) {
                return;
            }
            let container = document.getElementById(this.containerId);
            if (!container) {
                container = createDiv(this.containerId, 'watermark-recent-history');
                container.hidden = true;
            }
            container.setAttribute('role', 'group');
            container.setAttribute('aria-label', 'Recent watermark images');
            if (container.parentElement != inputContainer) {
                inputContainer.append(container);
            }
            this.render().catch(error => {
                console.warn('Unable to render recent watermark history.', error);
            });
        }
        catch (error) {
            console.warn('Unable to attach recent watermark history.', error);
        }
    }

    /** Restores a stored watermark entry into the live watermark input. */
    restoreEntry(entry) {
        let input = document.getElementById(this.inputId);
        if (!input) {
            return;
        }
        let source = entry.source;
        let name = entry.name || this.getFallbackName(source);
        setMediaFileDirect(input, this.getPreviewSource(source), 'image', name, source, () => {
            input.dataset.filedata = source;
            input.dataset.filename = name;
        });
        input.dataset.filedata = source;
        input.dataset.filename = name;
    }

    /** Deletes a rendered watermark entry through the serialized write queue. */
    deleteEntry(entry) {
        this.writeQueue = this.writeQueue.catch(() => { }).then(() => this.deleteEntryDirect(entry));
        return this.writeQueue;
    }

    /** Deletes a stored entry only when it still matches the rendered version. */
    async deleteEntryDirect(entry) {
        let database = await this.openDatabase();
        try {
            let transaction = database.transaction(this.storeName, 'readwrite');
            let store = transaction.objectStore(this.storeName);
            let request = store.get(entry.id);
            request.onsuccess = () => {
                let currentEntry = request.result;
                if (currentEntry && currentEntry.id == entry.id && currentEntry.source == entry.source && currentEntry.updated == entry.updated) {
                    store.delete(entry.id);
                }
            };
            await this.waitForTransaction(transaction);
        }
        finally {
            database.close();
        }
    }

    /** Renders the stored recent watermark thumbnails. */
    async render() {
        let entries = await this.getEntries();
        let container = document.getElementById(this.containerId);
        if (!container) {
            return;
        }
        container.innerHTML = '';
        container.hidden = entries.length == 0;
        for (let entry of entries) {
            let button = document.createElement('button');
            button.type = 'button';
            button.className = 'watermark-recent-thumbnail';
            let name = entry.name || this.getFallbackName(entry.source);
            button.title = name;
            button.setAttribute('aria-label', `Use recent watermark: ${name}`);
            let image = document.createElement('img');
            image.alt = '';
            image.src = this.getPreviewSource(entry.source);
            image.onerror = () => {
                image.onerror = null;
                button.remove();
                this.hideContainerIfEmpty(container);
                let deletionKey = `${entry.id}:${entry.updated}`;
                if (this.pendingDeletionKeys.has(deletionKey)) {
                    return;
                }
                this.pendingDeletionKeys.add(deletionKey);
                this.deleteEntry(entry).then(() => this.render()).catch(error => {
                    this.hideContainerIfEmpty(container);
                    console.warn('Unable to remove stale recent watermark entry.', error);
                }).finally(() => {
                    this.pendingDeletionKeys.delete(deletionKey);
                });
            };
            button.addEventListener('click', () => {
                try {
                    this.restoreEntry(entry);
                }
                catch (error) {
                    console.warn('Unable to restore recent watermark entry.', error);
                }
            });
            button.append(image);
            container.append(button);
        }
    }

    /** Gets a trusted filename from a watermark input matching the submitted source. */
    captureSubmittedName(actualInput) {
        if (!actualInput || typeof actualInput.watermarkimage != 'string' || !actualInput.watermarkimage) {
            return null;
        }
        let inputs = document.querySelectorAll('input.auto-file[data-param_id="watermarkimage"]');
        for (let input of inputs) {
            if (input.dataset.filedata != actualInput.watermarkimage) {
                continue;
            }
            if (input.dataset.filename) {
                return input.dataset.filename;
            }
            if (input.files && input.files[0] && input.files[0].name) {
                return input.files[0].name;
            }
        }
        return null;
    }

    /** Records a submitted watermark image without blocking the generation request. */
    recordSubmitted(actualInput, name = null) {
        if (!actualInput || typeof actualInput.watermarkimage != 'string' || !actualInput.watermarkimage) {
            return Promise.resolve();
        }
        let source = actualInput.watermarkimage;
        this.writeQueue = this.writeQueue.catch(() => { }).then(() => this.recordSource(source, name));
        return this.writeQueue;
    }

    /** Records one source, promotes it to newest, and evicts older entries. */
    async recordSource(source, name) {
        let database = await this.openDatabase();
        try {
            let transaction = database.transaction(this.storeName, 'readwrite');
            let store = transaction.objectStore(this.storeName);
            let request = store.getAll();
            request.onsuccess = () => {
                let newestUpdated = Number.isFinite(this.lastUpdated) ? this.lastUpdated : 0;
                for (let entry of request.result) {
                    let entryUpdated = Number(entry.updated);
                    if (Number.isFinite(entryUpdated)) {
                        newestUpdated = Math.max(newestUpdated, entryUpdated);
                    }
                }
                let updated = Math.max(Date.now(), newestUpdated + 1);
                this.lastUpdated = updated;
                let existingEntry = request.result.find(entry => entry.source == source);
                let id = existingEntry ? existingEntry.id : this.createEntryId(request.result);
                let entryName = this.getFallbackName(source);
                if (existingEntry && existingEntry.name) {
                    entryName = existingEntry.name;
                }
                if (typeof name == 'string' && name) {
                    entryName = name;
                }
                let entries = request.result.filter(entry => entry.source != source);
                entries.push({ id: id, source: source, name: entryName, updated: updated });
                entries.sort((first, second) => second.updated - first.updated);
                store.put({ id: id, source: source, name: entryName, updated: updated });
                for (let entry of request.result) {
                    if (entry.source == source && entry.id != id) {
                        store.delete(entry.id);
                    }
                }
                for (let i = this.maxEntries; i < entries.length; i++) {
                    store.delete(entries[i].id);
                }
            };
            await this.waitForTransaction(transaction);
        }
        finally {
            database.close();
        }
        this.render().catch(error => {
            console.warn('Unable to render recent watermark history.', error);
        });
    }

    /** Hides a history container that no longer has any thumbnail buttons. */
    hideContainerIfEmpty(container) {
        if (!container.querySelector('.watermark-recent-thumbnail')) {
            container.hidden = true;
        }
    }

    /** Creates a compact ID for a newly stored watermark entry. */
    createEntryId(entries) {
        let id = `${Date.now()}-${Math.random().toString(36).substring(2)}`;
        while (entries.some(entry => entry.id == id)) {
            id = `${Date.now()}-${Math.random().toString(36).substring(2)}`;
        }
        return id;
    }

    /** Gets a compact display name when the input has no original filename. */
    getFallbackName(source) {
        if (source.startsWith('data:')) {
            return 'Pasted watermark';
        }
        let filename = source.substring(source.lastIndexOf('/') + 1);
        return filename || 'Watermark image';
    }
}

let watermarkRecentHistory = new WatermarkRecentHistory();
