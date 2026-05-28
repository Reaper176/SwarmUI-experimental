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
}

postParamBuildSteps.push(() => {
    let modeInput = document.getElementById('input_latenttoolsinitmode');
    if (modeInput) {
        modeInput.addEventListener('change', updateLatentToolsModeVisibility);
    }
});

hideParamCallbacks.push(updateLatentToolsModeVisibility);
