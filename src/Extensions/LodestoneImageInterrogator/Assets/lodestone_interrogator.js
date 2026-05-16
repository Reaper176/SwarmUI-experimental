class LodestoneInterrogatorHelper
{
    /**
     * Initializes the Lodestone image interrogator panel.
     */
    init()
    {
        this.panel = document.getElementById("lodestone_interrogator_panel");
        if (!this.panel)
        {
            return;
        }

        this.status = this.panel.querySelector("[data-lodestone-status]");
        this.setupButton = this.panel.querySelector("[data-lodestone-setup-button]");
        this.fileInput = this.panel.querySelector("[data-lodestone-file-input]");
        this.runButton = this.panel.querySelector("[data-lodestone-run-button]");
        this.copyButton = this.panel.querySelector("[data-lodestone-copy-button]");
        this.replaceButton = this.panel.querySelector("[data-lodestone-replace-button]");
        this.appendButton = this.panel.querySelector("[data-lodestone-append-button]");
        this.preview = this.panel.querySelector("[data-lodestone-preview]");
        this.thresholdInput = this.panel.querySelector("[data-lodestone-threshold]");
        this.maxTagsInput = this.panel.querySelector("[data-lodestone-max-tags]");
        this.prompt = this.panel.querySelector("[data-lodestone-prompt]");
        this.ratingResults = this.panel.querySelector("[data-lodestone-rating-results]");
        this.characterResults = this.panel.querySelector("[data-lodestone-character-results]");
        this.generalResults = this.panel.querySelector("[data-lodestone-general-results]");
        this.imageData = null;
        this.lastPrompt = "";
        this.isReady = false;
        this.isRunning = false;

        if (this.setupButton)
        {
            this.setupButton.addEventListener("click", this.runSetup.bind(this));
        }
        if (this.fileInput)
        {
            this.fileInput.addEventListener("change", this.loadFile.bind(this));
        }
        if (this.runButton)
        {
            this.runButton.addEventListener("click", this.interrogate.bind(this));
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
    }

    /**
     * Updates the setup status display and button states from the server.
     */
    refreshStatus()
    {
        genericRequest("LodestoneInterrogatorStatus", {}, function(data)
        {
            let status = data.status || {};
            this.isReady = !!status.isReady;
            this.isRunning = !!status.isSetupRunning;
            this.setStatusText(status.message || "Lodestone interrogator status is unknown.");
            this.updateButtonStates();
        }.bind(this), 0, function(error)
        {
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
        this.isRunning = true;
        this.setStatusText("Setting up Lodestone Image Interrogator. This may download about 5.27 GB of model data.");
        this.updateButtonStates();
        genericRequest("LodestoneInterrogatorSetup", {}, function(data)
        {
            if (data.status && data.status.message)
            {
                this.setStatusText(data.status.message);
            }
            this.refreshStatus();
        }.bind(this), 0, function(error)
        {
            showError(error);
            this.refreshStatus();
        }.bind(this), 60 * 60 * 1000);
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
        genericRequest("LodestoneInterrogatorInterrogate", {
            image: this.imageData,
            threshold: threshold,
            maxTags: maxTags
        }, function(data)
        {
            if (!data.success)
            {
                showError(data.error || "Lodestone interrogation failed.");
                this.updateButtonStates();
                return;
            }
            this.renderResults(data);
            this.setStatusText("Interrogation complete.");
            this.updateButtonStates();
        }.bind(this), 0, function(error)
        {
            showError(error);
            this.updateButtonStates();
        }.bind(this));
    }

    /**
     * Renders the returned prompt and grouped tag results.
     */
    renderResults(data)
    {
        this.lastPrompt = data.prompt || "";
        if (this.prompt)
        {
            this.prompt.value = this.lastPrompt;
        }
        let groups = data.groups || {};
        this.renderGroup(this.ratingResults, groups.rating || []);
        this.renderGroup(this.characterResults, groups.character || groups.characters || []);
        this.renderGroup(this.generalResults, groups.general || []);
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
        if (this.setupButton)
        {
            this.setupButton.disabled = this.isReady || this.isRunning;
        }
        if (this.runButton)
        {
            this.runButton.disabled = !this.isReady || this.isRunning;
        }
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
