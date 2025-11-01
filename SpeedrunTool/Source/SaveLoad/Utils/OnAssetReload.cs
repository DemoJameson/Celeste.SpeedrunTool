using Celeste.Mod.SpeedrunTool.Utils;
using System.Threading.Tasks;

namespace Celeste.Mod.SpeedrunTool.SaveLoad.Utils;
internal static class OnAssetReload {

    [Initialize]
    private static void Initialize() {
        // after several reloads, multiple same actions are added... yeah but it doesn't matter

        Everest.Events.AssetReload.OnBeforeReload += _ => {
            SaveSlotsManager.ClearAll();
        };
        Everest.Events.AssetReload.OnAfterReload += _ => {
            SaveSlotsManager.AfterAssetReload();
        };

        typeof(AssetReloadHelper)
            .GetMethodInfo(nameof(AssetReloadHelper.Do), [typeof(string), typeof(Func<bool, Task>), typeof(bool), typeof(bool)])!
            .ILHook((cursor, _) => {
                cursor.EmitDelegate(MoreSaveSlotsUI.SnapshotUI.Close);
            });
    }
}
