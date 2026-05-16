class LodestoneInterrogatorHelper
{
    /**
     * Initializes the Lodestone image interrogator placeholder UI.
     */
    init()
    {
        let panel = document.getElementById("lodestone_interrogator_panel");
        if (!panel)
        {
            return;
        }
        let status = panel.querySelector("[data-lodestone-status]");
        if (status)
        {
            status.textContent = "Setup is required before first use.";
        }
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
