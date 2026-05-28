using LodestoneImageInterrogatorExtension.WebAPI;
using SwarmUI.Core;
using SwarmUI.Utils;

namespace LodestoneImageInterrogatorExtension;

public class LodestoneImageInterrogator : Extension
{
    public override void OnPreInit()
    {
        ScriptFiles.Add("Assets/lodestone_interrogator.js");
        StyleSheetFiles.Add("Assets/lodestone_interrogator.css");
    }

    public override void OnInit()
    {
        Logs.Info("Lodestone Image Interrogator extension initializing.");
        LodestoneSetupManager.Initialize(FilePath);
        LodestoneInterrogatorAPI.Register();
    }
}
