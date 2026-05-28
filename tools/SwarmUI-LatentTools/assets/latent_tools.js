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
    let hasLatentInit = mode == 'Gaussian' || mode == 'Uniform' || mode == 'Gaussian + Uniform';
    setVisible('latenttoolschannels', hasLatentInit);
    setVisible('latenttoolsgaussianmean', mode == 'Gaussian' || mode == 'Gaussian + Uniform');
    setVisible('latenttoolsgaussianstd', mode == 'Gaussian' || mode == 'Gaussian + Uniform');
    setVisible('latenttoolsuniformmin', mode == 'Uniform' || mode == 'Gaussian + Uniform');
    setVisible('latenttoolsuniformmax', mode == 'Uniform' || mode == 'Gaussian + Uniform');
    let blendModeInput = document.getElementById('input_latenttoolsblendmode');
    setVisible('latenttoolsblendmode', hasLatentInit);
    let blendMode = blendModeInput ? getInputVal(blendModeInput) : 'Disabled';
    setVisible('latenttoolsblendratio', hasLatentInit && blendMode != 'Disabled');
    let opModeInput = document.getElementById('input_latenttoolsop');
    setVisible('latenttoolsop', hasLatentInit);
    let opMode = opModeInput ? getInputVal(opModeInput) : 'Disabled';
    setVisible('latenttoolsoparg', hasLatentInit && ['add', 'mul', 'pow', 'clamp_bottom', 'clamp_top', 'mean', 'std'].includes(opMode));
    setVisible('latenttoolsuseltksampler', hasLatentInit);
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
    let opModeInput = document.getElementById('input_latenttoolsop');
    if (opModeInput) {
        opModeInput.addEventListener('change', updateLatentToolsModeVisibility);
    }
});

hideParamCallbacks.push(updateLatentToolsModeVisibility);
