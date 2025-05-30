namespace Celeste.Mod.SpeedrunTool.SaveLoad.Utils;
internal static class OnAssetReload {
    
    [Initialize]
    private static void Initialize() {
        Everest.Events.AssetReload.OnBeforeReload += _ => {
            SaveSlotsManager.ClearState();
        };
        Everest.Events.AssetReload.OnAfterReload += _ => {
            SaveSlotsManager.AfterAssetReload();
        };
    }
}
