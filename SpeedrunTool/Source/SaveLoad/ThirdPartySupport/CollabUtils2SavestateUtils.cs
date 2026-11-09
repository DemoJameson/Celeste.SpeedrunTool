using Celeste.Mod.SpeedrunTool.Utils;

namespace Celeste.Mod.SpeedrunTool.SaveLoad.ThirdPartySupport;

/// <summary>
/// Collab Utils 2's <c>CollabCrystalHeart</c> registers the collab area-complete hooks from
/// <c>Added</c> and removes them in <c>Removed</c>/<c>SceneEnd</c>. Loaded save states never call
/// Added, so a state saved in a room with a crystal heart would lose those hooks (area completion
/// bookkeeping and the <c>exitToLobby</c> IL patch). The type is not used by Strawberry Jam's
/// Grandmaster chapter itself, but it is cheap to keep working for the other collab maps.
/// </summary>
internal static class CollabUtils2SavestateUtils {
    private const string CrystalHeartTypeName = "Celeste.Mod.CollabUtils2.Entities.CollabCrystalHeart";

    private static Type crystalHeartType;

    [Initialize]
    private static void Initialize() {
        crystalHeartType = ModUtils.GetType("CollabUtils2", CrystalHeartTypeName);
        if (crystalHeartType is null) {
            return;
        }

        try {
            Tracker.AddTypeToTracker(crystalHeartType);
            if (crystalHeartType.GetMethodInfo("hook") is null || crystalHeartType.GetMethodInfo("unhook") is null) {
                crystalHeartType = null;
            }
        }
        catch (Exception exception) {
            crystalHeartType = null;
            Logger.Warn("SpeedrunTool/CollabUtils2SavestateUtils", $"Failed to set up CollabCrystalHeart support: {exception.Message}");
        }
    }

    internal static void Support() {
        if (crystalHeartType is null) {
            return;
        }

        SaveLoadAction.InternalSafeAdd(
            loadState: (_, level) => {
                // Both methods are guarded by the mod's own 'hooked' flag and are idempotent, but the
                // static flag itself may have been restored with a stale value, so clear it first.
                crystalHeartType.InvokeMethod("unhook");

                if (level.Tracker.GetEntitiesTrackIfNeeded(crystalHeartType) is { Count: > 0 }) {
                    crystalHeartType.InvokeMethod("hook");
                }
            }
        );
    }
}
