class LodestoneInterrogatorHelper
{
    /**
     * Initializes the Lodestone image interrogator panel.
     */
    init()
    {
        this.registerGenerateBridgeAction();
        this.initPanelWithRetry();
    }

    /**
     * Initializes the panel, retrying briefly because extension tab HTML can load after this script.
     */
    initPanelWithRetry()
    {
        if (this.initPanel())
        {
            return;
        }
        this.panelInitAttempts = (this.panelInitAttempts || 0) + 1;
        if (this.panelInitAttempts < 80)
        {
            setTimeout(this.initPanelWithRetry.bind(this), 250);
        }
    }

    /**
     * Initializes the Lodestone image interrogator panel when its DOM is available.
     */
    initPanel()
    {
        if (this.panelInitialized)
        {
            return true;
        }
        this.panel = document.getElementById("lodestone_interrogator_panel");
        if (!this.panel)
        {
            return false;
        }

        this.status = this.panel.querySelector("[data-lodestone-status]");
        this.setupButton = this.panel.querySelector("[data-lodestone-setup-button]");
        this.backendInput = this.panel.querySelector("[data-lodestone-backend]");
        this.fileInput = this.panel.querySelector("[data-lodestone-file-input]");
        this.runButton = this.panel.querySelector("[data-lodestone-run-button]");
        this.copyButton = this.panel.querySelector("[data-lodestone-copy-button]");
        this.replaceButton = this.panel.querySelector("[data-lodestone-replace-button]");
        this.appendButton = this.panel.querySelector("[data-lodestone-append-button]");
        this.preview = this.panel.querySelector("[data-lodestone-preview]");
        this.thresholdInput = this.panel.querySelector("[data-lodestone-threshold]");
        this.maxTagsInput = this.panel.querySelector("[data-lodestone-max-tags]");
        this.categoryCheckboxes = this.panel.querySelectorAll(".lodestone-interrogator-category");
        this.prompt = this.panel.querySelector("[data-lodestone-prompt]");
        this.ratingResults = this.panel.querySelector("[data-lodestone-rating-results]");
        this.characterResults = this.panel.querySelector("[data-lodestone-character-results]");
        this.generalResults = this.panel.querySelector("[data-lodestone-general-results]");
        this.styleResults = this.panel.querySelector("[data-lodestone-style-results]");
        this.imageData = null;
        this.lastData = null;
        this.lastPrompt = "";
        this.statusKnown = false;
        this.statusError = false;
        this.isReady = false;
        this.isRunning = false;
        this.readyBackend = "";
        this.backendWasChanged = false;

        if (this.setupButton)
        {
            this.setupButton.addEventListener("click", this.runSetup.bind(this));
        }
        if (this.backendInput)
        {
            this.backendInput.addEventListener("change", this.onBackendChanged.bind(this));
        }
        if (this.fileInput)
        {
            this.fileInput.addEventListener("change", this.loadFile.bind(this));
        }
        if (this.runButton)
        {
            this.runButton.addEventListener("click", this.interrogate.bind(this));
        }
        for (let i = 0; i < this.categoryCheckboxes.length; i++)
        {
            this.categoryCheckboxes[i].addEventListener("change", this.rerenderLastResults.bind(this));
        }
        if (this.copyButton)
        {
            this.copyButton.addEventListener("click", this.copyPrompt.bind(this));
        }
        if (this.replaceButton)
        {
            this.replaceButton.addEventListener("click", this.sendToGenerate.bind(this, "replace"));
        }
        if (this.appendButton)
        {
            this.appendButton.addEventListener("click", this.sendToGenerate.bind(this, "append"));
        }

        this.refreshStatus();
        this.panelInitialized = true;
        return true;
    }

    /**
     * Adds a Generate tab More-menu action that sends the selected image into this interrogator.
     */
    registerGenerateBridgeAction()
    {
        if (this.hasRegisteredGenerateBridge)
        {
            return;
        }
        if (typeof AdvancedPopover != "function")
        {
            this.generateBridgeRegisterAttempts = (this.generateBridgeRegisterAttempts || 0) + 1;
            if (this.generateBridgeRegisterAttempts < 20)
            {
                setTimeout(this.registerGenerateBridgeAction.bind(this), 250);
            }
            return;
        }
        this.hasRegisteredGenerateBridge = true;
        let originalAdvancedPopover = AdvancedPopover;
        let helper = this;
        AdvancedPopover = class extends originalAdvancedPopover
        {
            constructor(id, buttons, canSearch, x, y, root, preSelect = null, flipYHeight = null, heightLimit = 999999, canSelect = true)
            {
                if (id == "image_more_popover")
                {
                    buttons = helper.addCurrentImagePopoverButton(buttons);
                }
                super(id, buttons, canSearch, x, y, root, preSelect, flipYHeight, heightLimit, canSelect);
            }
        };
    }

    /**
     * Adds the Lodestone action to the current image More popover button list.
     */
    addCurrentImagePopoverButton(buttons)
    {
        if (!Array.isArray(buttons))
        {
            return buttons;
        }
        let source = this.getCurrentImageSource();
        if (!source)
        {
            return buttons;
        }
        let updatedButtons = buttons.slice();
        let helper = this;
        updatedButtons.push({
            key: "Interrogate Image",
            title: "Send this image to the Lodestone Image Interrogator.",
            action: function()
            {
                helper.takeCurrentImage(source);
            }
        });
        return updatedButtons;
    }

    /**
     * Returns the current center image source if it is an image.
     */
    getCurrentImageSource()
    {
        if (typeof currentImageHelper == "undefined")
        {
            return "";
        }
        let image = currentImageHelper.getCurrentImage();
        if (!image)
        {
            return "";
        }
        let source = image.dataset.src || image.currentSrc || image.src || "";
        if (!source)
        {
            return "";
        }
        if (typeof getMediaType == "function" && getMediaType(source) != "image")
        {
            return "";
        }
        return source;
    }

    /**
     * Copies the selected Generate tab image into the interrogator preview and opens the tab.
     */
    async takeCurrentImage(source)
    {
        if (!source)
        {
            showError("No current Generate image is available to interrogate.");
            return;
        }
        try
        {
            this.imageData = await this.convertImageSourceToDataUrl(source);
        }
        catch (error)
        {
            this.imageData = null;
            showError(`Failed to read current Generate image: ${error}`);
            return;
        }
        let hash = String.fromCharCode(35);
        let tab = document.getElementById("maintab_Image_Interrogator");
        if (!tab)
        {
            tab = document.getElementById("maintab_imageinterrogator");
        }
        if (!tab)
        {
            tab = document.querySelector(`[href="${hash}Image_Interrogator"]`);
        }
        if (!tab)
        {
            tab = document.querySelector(`[href="${hash}imageinterrogator"]`);
        }
        if (!tab)
        {
            tab = document.querySelector(`[href="${hash}Image-Interrogator"]`);
        }
        if (tab)
        {
            tab.click();
        }
        this.initPanelWithRetry();
        this.renderPreview(this.imageData);
    }

    /**
     * Converts an image URL to the data URL format expected by the backend.
     */
    async convertImageSourceToDataUrl(source)
    {
        if (!source)
        {
            throw new Error("No image URL is available.");
        }
        if (source.startsWith("data:"))
        {
            return source;
        }
        let response = await fetch(source);
        if (!response.ok)
        {
            throw new Error(`Image request failed with status ${response.status}.`);
        }
        let blob = await response.blob();
        return await this.blobToDataUrl(blob);
    }

    /**
     * Reads a Blob as a data URL.
     */
    blobToDataUrl(blob)
    {
        return new Promise(function(resolve, reject)
        {
            let reader = new FileReader();
            reader.onload = function(loadEvent)
            {
                resolve(loadEvent.target.result);
            };
            reader.onerror = function()
            {
                reject(new Error("Unable to convert image data."));
            };
            reader.readAsDataURL(blob);
        });
    }

    /**
     * Updates the setup status display and button states from the server.
     */
    refreshStatus()
    {
        genericRequest("LodestoneInterrogatorStatus", {}, function(data)
        {
            if (!data || !data.status || typeof data.status != "object" || typeof data.status.isReady != "boolean" || typeof data.status.isSetupRunning != "boolean")
            {
                this.statusKnown = false;
                this.statusError = true;
                this.isReady = false;
                this.isRunning = false;
                this.setStatusText("Lodestone interrogator status is unknown.");
                this.updateButtonStates();
                return;
            }
            let status = data.status;
            this.statusKnown = true;
            this.statusError = false;
            this.isReady = !!status.isReady;
            this.isRunning = !!status.isSetupRunning;
            this.readyBackend = status.backend || "";
            if (!this.backendWasChanged && this.backendInput && this.readyBackend)
            {
                this.backendInput.value = this.readyBackend;
            }
            this.setStatusText(status.message || "Lodestone interrogator status is unknown.");
            this.updateButtonStates();
        }.bind(this), 0, function(error)
        {
            this.statusKnown = false;
            this.statusError = true;
            this.isReady = false;
            this.isRunning = false;
            this.setStatusText(`Failed to check Lodestone interrogator status: ${error}`);
            this.updateButtonStates();
        }.bind(this));
    }

    /**
     * Starts first-use setup, including dependency installation and model download.
     */
    runSetup()
    {
        let backend = this.readBackendSelection();
        this.backendWasChanged = false;
        this.isRunning = true;
        this.setStatusText("Setting up Lodestone Image Interrogator. This may download about 5.27 GB of model data.");
        this.updateButtonStates();
        this.startSetupStatusPolling();
        genericRequest("LodestoneInterrogatorSetup", { backend: backend }, function(data)
        {
            this.stopSetupStatusPolling();
            if (data.status && data.status.message)
            {
                this.setStatusText(data.status.message);
            }
            this.refreshStatus();
        }.bind(this), 0, function(error)
        {
            this.stopSetupStatusPolling();
            showError(error);
            this.refreshStatus();
        }.bind(this), 60 * 60 * 1000);
    }

    /**
     * Polls setup status while the long-running setup request is active.
     */
    startSetupStatusPolling()
    {
        this.stopSetupStatusPolling();
        this.setupStatusPoll = setInterval(function()
        {
            this.refreshStatus();
        }.bind(this), 3000);
    }

    /**
     * Stops setup status polling.
     */
    stopSetupStatusPolling()
    {
        if (this.setupStatusPoll)
        {
            clearInterval(this.setupStatusPoll);
            this.setupStatusPoll = null;
        }
    }

    /**
     * Reads the selected image file and renders a local preview.
     */
    loadFile(event)
    {
        let files = event.target.files;
        if (!files || files.length < 1)
        {
            return;
        }
        let file = files[0];
        let reader = new FileReader();
        reader.onload = function(loadEvent)
        {
            this.imageData = loadEvent.target.result;
            this.renderPreview(this.imageData);
        }.bind(this);
        reader.onerror = function()
        {
            this.imageData = null;
            showError("Failed to read selected image file.");
        }.bind(this);
        reader.readAsDataURL(file);
    }

    /**
     * Sends the selected image to Lodestone and renders returned tags.
     */
    interrogate()
    {
        if (!this.imageData)
        {
            showError("Select an image before interrogating.");
            return;
        }
        let threshold = this.readNumber(this.thresholdInput, 0.35);
        let maxTags = Math.round(this.readNumber(this.maxTagsInput, 40));
        this.setStatusText("Interrogating image...");
        if (this.runButton)
        {
            this.runButton.disabled = true;
        }
        let payload = {
            image: this.imageData,
            threshold: threshold,
            maxTags: maxTags,
            session_id: session_id
        };
        sendJsonToServer("API/LodestoneInterrogatorInterrogate", payload, function(status, data)
        {
            if (!data)
            {
                showError("Lodestone interrogation failed.");
                this.updateButtonStates();
                return;
            }
            if (data.error_id == "invalid_session_id")
            {
                showError("Invalid session ID. Refresh the page and try again.");
                this.updateButtonStates();
                return;
            }
            if (!data.success)
            {
                showError(this.formatApiError(data, "Lodestone interrogation failed."));
                this.updateButtonStates();
                return;
            }
            this.renderResults(data);
            this.setStatusText("Interrogation complete.");
            this.updateButtonStates();
        }.bind(this), function(error)
        {
            showError(error);
            this.updateButtonStates();
        }.bind(this));
    }

    /**
     * Formats API errors with process output when the backend provides it.
     */
    formatApiError(data, fallback)
    {
        if (!data)
        {
            return fallback;
        }
        let message = data.error || fallback;
        let detail = this.mostUsefulLine(data.stderr) || this.mostUsefulLine(data.stdout);
        if (detail && !message.includes(detail))
        {
            message = `${message}\n${detail}`;
        }
        return message;
    }

    /**
     * Extracts the most useful concise line from process output.
     */
    mostUsefulLine(text)
    {
        if (!text)
        {
            return "";
        }
        let lines = `${text}`.replaceAll("\r\n", "\n").replaceAll("\r", "\n").split("\n");
        for (let i = lines.length - 1; i >= 0; i--)
        {
            let trimmed = lines[i].trim();
            if (trimmed)
            {
                return trimmed.length > 500 ? `${trimmed.substring(0, 500)}...` : trimmed;
            }
        }
        return "";
    }

    /**
     * Renders the returned prompt and grouped tag results.
     */
    renderResults(data)
    {
        this.lastData = data;
        let renderedData = this.getRenderableData(data);
        this.lastPrompt = renderedData.prompt;
        if (this.prompt)
        {
            this.prompt.value = this.lastPrompt;
        }
        let groups = renderedData.groups;
        let categories = renderedData.categories;
        this.renderCategoryGroup(this.ratingResults, groups.rating || [], categories, "rating");
        this.renderCategoryGroup(this.characterResults, groups.character || [], categories, "character");
        this.renderCategoryGroup(this.generalResults, groups.general || [], categories, "general");
        this.renderCategoryGroup(this.styleResults, groups.style || [], categories, "style");
    }

    /**
     * Re-renders the last returned results after a display option changes.
     */
    rerenderLastResults()
    {
        if (!this.lastData)
        {
            return;
        }
        this.renderResults(this.lastData);
    }

    /**
     * Copies the current prompt to the clipboard.
     */
    copyPrompt()
    {
        let text = this.getPromptText();
        if (navigator.clipboard && navigator.clipboard.writeText)
        {
            navigator.clipboard.writeText(text).catch(function()
            {
                this.copyPromptFallback();
            }.bind(this));
            return;
        }
        this.copyPromptFallback();
    }

    /**
     * Sends the current prompt to the Generate tab prompt box.
     */
    sendToGenerate(mode)
    {
        let text = this.getPromptText();
        if (!text)
        {
            showError("No Lodestone prompt is available.");
            return;
        }
        let promptBox = document.getElementById("alt_prompt_textbox");
        if (!promptBox)
        {
            showError("Generate prompt textbox is not available.");
            return;
        }
        if (mode == "append")
        {
            let existing = promptBox.value.trim();
            if (existing)
            {
                promptBox.value = `${existing}, ${text}`;
            }
            else
            {
                promptBox.value = text;
            }
        }
        else
        {
            promptBox.value = text;
        }
        promptBox.dispatchEvent(new Event("input", { bubbles: true }));
        let generateTab = document.getElementById("text2imagetabbutton");
        if (generateTab)
        {
            generateTab.click();
        }
    }

    /**
     * Sets the visible status text.
     */
    setStatusText(text)
    {
        if (this.status)
        {
            this.status.textContent = text;
        }
    }

    /**
     * Applies setup/run button enabled states from current status.
     */
    updateButtonStates()
    {
        let selectedBackend = this.readBackendSelection();
        let backendMatches = this.readyBackend && selectedBackend == this.readyBackend;
        if (this.setupButton)
        {
            this.setupButton.disabled = !this.statusKnown || this.statusError || (this.isReady && backendMatches) || this.isRunning;
        }
        if (this.backendInput)
        {
            this.backendInput.disabled = this.isRunning;
        }
        if (this.runButton)
        {
            this.runButton.disabled = !this.statusKnown || this.statusError || !this.isReady || !backendMatches || this.isRunning;
        }
    }

    /**
     * Handles backend selector changes.
     */
    onBackendChanged()
    {
        this.backendWasChanged = true;
        if (this.isReady && this.readyBackend && this.readBackendSelection() != this.readyBackend)
        {
            this.setStatusText("Setup is required for the selected GPU backend.");
        }
        this.updateButtonStates();
    }

    /**
     * Reads the selected backend value.
     */
    readBackendSelection()
    {
        if (!this.backendInput)
        {
            return "auto";
        }
        let value = `${this.backendInput.value || "auto"}`.trim().toLowerCase();
        if (value == "cuda" || value == "rocm" || value == "cpu" || value == "auto")
        {
            return value;
        }
        return "auto";
    }

    /**
     * Returns the selected tag category names.
     */
    selectedCategories()
    {
        let categories = [];
        if (!this.categoryCheckboxes || this.categoryCheckboxes.length < 1)
        {
            return ["rating", "character", "general", "style"];
        }
        for (let i = 0; i < this.categoryCheckboxes.length; i++)
        {
            let checkbox = this.categoryCheckboxes[i];
            if (checkbox.checked)
            {
                categories.push(this.normalizeCategory(checkbox.value));
            }
        }
        return categories;
    }

    /**
     * Builds prompt and groups for the current category selection.
     */
    getRenderableData(data)
    {
        let groups = this.normalizeGroups(data.groups || {}, data.tags || []);
        let tags = [];
        if (Array.isArray(data.tags))
        {
            tags = data.tags;
        }
        else
        {
            tags = this.flattenGroups(groups);
        }
        let categories = this.selectedCategories();
        return {
            categories: categories,
            prompt: this.formatPrompt(tags),
            groups: groups
        };
    }

    /**
     * Normalizes tag groups and category aliases.
     */
    normalizeGroups(sourceGroups, sourceTags)
    {
        let groups = {
            rating: [],
            character: [],
            general: [],
            style: []
        };
        for (let key in sourceGroups)
        {
            if (Object.prototype.hasOwnProperty.call(sourceGroups, key))
            {
                this.addTagsToGroup(groups, key, sourceGroups[key]);
            }
        }
        if (!sourceGroups || Object.keys(sourceGroups).length < 1)
        {
            this.addTagsToGroup(groups, "", sourceTags);
        }
        return groups;
    }

    /**
     * Adds tags into a normalized category group.
     */
    addTagsToGroup(groups, category, tags)
    {
        if (!Array.isArray(tags))
        {
            return;
        }
        let fallbackCategory = this.normalizeCategory(category);
        for (let i = 0; i < tags.length; i++)
        {
            let tag = tags[i];
            if (tag)
            {
                let normalizedCategory = this.normalizeCategory(tag.category || fallbackCategory);
                if (!tag.category)
                {
                    tag = Object.assign({}, tag);
                    tag.category = normalizedCategory;
                }
                groups[normalizedCategory].push(tag);
            }
        }
    }

    /**
     * Flattens normalized groups in prompt order.
     */
    flattenGroups(groups)
    {
        let tags = [];
        let order = ["rating", "character", "general", "style"];
        for (let i = 0; i < order.length; i++)
        {
            let groupTags = groups[order[i]] || [];
            for (let j = 0; j < groupTags.length; j++)
            {
                tags.push(groupTags[j]);
            }
        }
        return tags;
    }

    /**
     * Converts selected tag categories to a prompt string.
     */
    formatPrompt(tags)
    {
        let parts = [];
        let categories = this.selectedCategories();
        for (let i = 0; i < tags.length; i++)
        {
            let tag = tags[i];
            if (tag && tag.name && categories.indexOf(this.normalizeCategory(tag.category)) >= 0)
            {
                parts.push(`${tag.name}`);
            }
        }
        return parts.join(", ");
    }

    /**
     * Normalizes backend category names and aliases.
     */
    normalizeCategory(category)
    {
        let normalized = `${category || ""}`.trim().toLowerCase();
        if (normalized == "characters")
        {
            return "character";
        }
        if (normalized == "styles")
        {
            return "style";
        }
        if (normalized == "rating" || normalized == "character" || normalized == "style")
        {
            return normalized;
        }
        return "general";
    }

    /**
     * Reads a numeric input, falling back when parsing fails.
     */
    readNumber(input, fallback)
    {
        if (!input)
        {
            return fallback;
        }
        let value = parseFloat(input.value);
        if (Number.isNaN(value))
        {
            return fallback;
        }
        return value;
    }

    /**
     * Renders a safe image preview from a browser data URL.
     */
    renderPreview(dataUrl)
    {
        if (!this.preview)
        {
            return;
        }
        this.preview.textContent = "";
        let img = document.createElement("img");
        img.src = dataUrl;
        img.alt = "Selected Lodestone interrogation image";
        this.preview.appendChild(img);
    }

    /**
     * Renders one tag result group.
     */
    renderGroup(groupElement, tags)
    {
        if (!groupElement)
        {
            return;
        }
        let list = groupElement.querySelector(".lodestone-interrogator-result-list");
        if (!list)
        {
            return;
        }
        list.textContent = "";
        if (!tags || tags.length < 1)
        {
            let empty = document.createElement("div");
            empty.className = "lodestone-interrogator-result-empty";
            empty.textContent = "No tags";
            list.appendChild(empty);
            return;
        }
        for (let i = 0; i < tags.length; i++)
        {
            let tag = tags[i];
            let item = document.createElement("div");
            item.className = "lodestone-interrogator-result-item";
            item.textContent = this.formatTag(tag);
            list.appendChild(item);
        }
    }

    /**
     * Renders a group when its category is selected and hides it otherwise.
     */
    renderCategoryGroup(groupElement, tags, categories, category)
    {
        if (!groupElement)
        {
            return;
        }
        if (categories.indexOf(category) < 0)
        {
            groupElement.style.display = "none";
            return;
        }
        groupElement.style.display = "";
        this.renderGroup(groupElement, tags);
    }

    /**
     * Formats one tag and score for display.
     */
    formatTag(tag)
    {
        let name = `${tag.name || ""}`;
        let probability = Number(tag.probability);
        if (Number.isNaN(probability))
        {
            return name;
        }
        return `${name} (${Math.round(probability * 1000) / 10}%)`;
    }

    /**
     * Returns the current prompt text.
     */
    getPromptText()
    {
        if (this.prompt)
        {
            return this.prompt.value.trim();
        }
        return this.lastPrompt.trim();
    }

    /**
     * Copies prompt text using the legacy textarea selection fallback.
     */
    copyPromptFallback()
    {
        if (!this.prompt)
        {
            return;
        }
        this.prompt.focus();
        this.prompt.select();
        document.execCommand("copy");
    }
}

let lodestoneInterrogator = new LodestoneInterrogatorHelper();

if (document.readyState == "loading")
{
    document.addEventListener("DOMContentLoaded", function()
    {
        lodestoneInterrogator.init();
    });
}
else
{
    lodestoneInterrogator.init();
}
