using Celeste.Mod.SpeedrunTool.Utils;
using System.Reflection;

namespace Celeste.Mod.SpeedrunTool.SaveLoad.ThirdPartySupport;

/// <summary>
/// Yet Another Helper (used by Strawberry Jam's Grandmaster chapter) registers a collision hook
/// from <c>SpikeJumpThruController.Awake</c> and removes it in <c>Removed</c>/<c>SceneEnd</c>.
/// Loaded save states do not go through Awake (the entities come from the pre-clone graph), so
/// after a load the hook can be missing while the mod's own <c>SpikeHooked</c> static flag may
/// have been restored as true - leaving spike jump-throughs broken until the room is reloaded.
/// </summary>
internal static class YetAnotherHelperUtils {
    private const string ControllerTypeName = "Celeste.Mod.YetAnotherHelper.Entities.SpikeJumpThruController";

    private static Type controllerType;
    private static FieldInfo spikeHookedField;
    private static On.Celeste.Spikes.hook_OnCollide onCollideHook;

    [Initialize]
    private static void Initialize() {
        try {
            controllerType = ModUtils.GetType("YetAnotherHelper", ControllerTypeName);
            if (controllerType is null) {
                return;
            }

            Tracker.AddTypeToTracker(controllerType);
            spikeHookedField = controllerType.GetFieldInfo("SpikeHooked");
            if (spikeHookedField is null || controllerType.GetMethodInfo("OnCollideHook") is not { } onCollideHookMethod) {
                controllerType = null;
                return;
            }

            onCollideHook = (On.Celeste.Spikes.hook_OnCollide)Delegate.CreateDelegate(typeof(On.Celeste.Spikes.hook_OnCollide), onCollideHookMethod);
        }
        catch (Exception exception) {
            controllerType = null;
            Logger.Warn("SpeedrunTool/YetAnotherHelperUtils", $"Failed to set up SpikeJumpThruController support: {exception.Message}");
        }
    }

    internal static void Support() {
        if (controllerType is null || onCollideHook is null || spikeHookedField is null) {
            return;
        }

        SaveLoadAction.InternalSafeAdd(
            loadState: (_, level) => {
                // The hook delegate is static, so removing then adding it is idempotent.
                On.Celeste.Spikes.OnCollide -= onCollideHook;

                bool controllerPresent = level.Tracker.GetEntitiesTrackIfNeeded(controllerType) is { Count: > 0 };
                if (controllerPresent) {
                    On.Celeste.Spikes.OnCollide += onCollideHook;
                    spikeHookedField.SetValue(null, true);
                }
                else {
                    spikeHookedField.SetValue(null, false);
                }
            }
        );
    }
}
