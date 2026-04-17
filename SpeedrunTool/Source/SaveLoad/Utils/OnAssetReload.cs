using Celeste.Mod.SpeedrunTool.Utils;
using System.Threading.Tasks;

namespace Celeste.Mod.SpeedrunTool.SaveLoad.Utils;
internal static class OnAssetReload {

    [Initialize]
    private static void Initialize() {
        // after several reloads, multiple same actions are added... yeah but it doesn't matter

        Everest.Events.AssetReload.OnBeforeReload += BeforeReload;

        Everest.Events.AssetReload.OnAfterReload += AfterReload;

        typeof(AssetReloadHelper)
            .GetMethodInfo(nameof(AssetReloadHelper.Do), [typeof(string), typeof(Func<bool, Task>), typeof(bool), typeof(bool)])!
            .ILHook((cursor, _) => {
                cursor.EmitDelegate(MoreSaveSlotsUI.SnapshotUI.Close);
            });
    }

    private static void BeforeReload(bool silent) {
        if (ModSettings.ClearStateOnHotReload) {
            SaveSlotsManager.ClearAll();
        }
    }

    private static void AfterReload(bool silent) {
        if (ModSettings.ClearStateOnHotReload) {
            SaveSlotsManager.AfterAssetReload();
        }
    }
}
