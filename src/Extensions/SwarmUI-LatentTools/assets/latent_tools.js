addInstallButton('latenttools', 'latent_tools', 'latent_tools', 'Install Latent Tools');

/** Updates Latent Tools parameter visibility based on the selected mode. */
function updateLatentToolsModeVisibility() {
    if (!currentBackendFeatureSet.includes('latent_tools')) {
        return;
    }
    let modeInput = document.getElementById('input_latenttoolsinitmode');
    if (!modeInput) {
        return;
    }
    let setVisible = (id, visible) => {
        let elem = document.getElementById(`input_${id}`);
        if (!elem) {
            return;
        }
        let box = findParentOfClass(elem, 'auto-input');
        if (!box) {
            return;
        }
        box.dataset.visible_controlled = 'true';
        box.style.display = visible ? '' : 'none';
    };
    let mode = getInputVal(modeInput);
    setVisible('latenttoolschannels', mode == 'Gaussian' || mode == 'Uniform');
    setVisible('latenttoolsgaussianmean', mode == 'Gaussian');
    setVisible('latenttoolsgaussianstd', mode == 'Gaussian');
    setVisible('latenttoolsuniformmin', mode == 'Uniform');
    setVisible('latenttoolsuniformmax', mode == 'Uniform');
    let blendModeInput = document.getElementById('input_latenttoolsblendmode');
    let hasLatentInit = mode == 'Gaussian' || mode == 'Uniform';
    setVisible('latenttoolsblendmode', hasLatentInit);
    let blendMode = blendModeInput ? getInputVal(blendModeInput) : 'Disabled';
    setVisible('latenttoolsblendratio', hasLatentInit && blendMode != 'Disabled');
}

postParamBuildSteps.push(() => {
    let modeInput = document.getElementById('input_latenttoolsinitmode');
    if (modeInput) {
        modeInput.addEventListener('change', updateLatentToolsModeVisibility);
    }
    let blendModeInput = document.getElementById('input_latenttoolsblendmode');
    if (blendModeInput) {
        blendModeInput.addEventListener('change', updateLatentToolsModeVisibility);
    }
});

hideParamCallbacks.push(updateLatentToolsModeVisibility);
