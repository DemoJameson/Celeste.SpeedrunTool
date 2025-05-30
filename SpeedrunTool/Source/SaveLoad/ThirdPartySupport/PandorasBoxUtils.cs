
using Celeste.Mod.SpeedrunTool.Utils;

namespace Celeste.Mod.SpeedrunTool.SaveLoad.ThirdPartySupport;
internal static class PandorasBoxUtils {

    internal static void Support() {
        // TimeField.targetPlayer 和 TimeField.lingeringTarget 等
        // WeakReference<T> 类型的实例在 SL 多次并且内存回收之后 target 可能会指向错误的对象，原因未知
        if (ModUtils.GetType("PandorasBox", "Celeste.Mod.PandorasBox.TimeField") is { } timeFieldType
            && Delegate.CreateDelegate(typeof(On.Celeste.Player.hook_Update), timeFieldType.GetMethodInfo("PlayerUpdateHook")) is
                On.Celeste.Player.hook_Update hookUpdate) {
            SaveLoadAction.InternalSafeAdd(
                loadState: (_, _) => {
                    if (timeFieldType.GetFieldValue<bool>("hookAdded")) {
                        On.Celeste.Player.Update -= hookUpdate;
                        On.Celeste.Player.Update += hookUpdate;
                    } else {
                        On.Celeste.Player.Update -= hookUpdate;
                    }
                }
            );
        }

        if (ModUtils.GetType("PandorasBox", "Celeste.Mod.PandorasBox.MarioClearPipeHelper") is { } pipeHelper) {
            if (pipeHelper.GetFieldInfo("CurrentlyTransportedEntities") != null) {
                SaveLoadAction.InternalSafeAdd(
                    (savedValues, _) => SaveLoadAction.SaveStaticMemberValues(savedValues, pipeHelper, "CurrentlyTransportedEntities"),
                    (savedValues, _) => SaveLoadAction.LoadStaticMemberValues(savedValues)
                );
            }

            if (pipeHelper.GetMethodInfo("AllowComponentsForList") != null && pipeHelper.GetMethodInfo("ShouldAddComponentsForList") != null) {
                SaveLoadAction.InternalSafeAdd((_, level) => {
                    if (pipeHelper.InvokeMethod("ShouldAddComponentsForList", level.Entities) as bool? == true) {
                        pipeHelper.InvokeMethod("AllowComponentsForList", StateManager.Instance.SavedLevel.Entities);
                    }
                }, (_, level) => {
                    if (pipeHelper.InvokeMethod("ShouldAddComponentsForList", StateManager.Instance.SavedLevel.Entities) as bool? == true) {
                        pipeHelper.InvokeMethod("AllowComponentsForList", level.Entities);
                    }
                });
            }
        }

        // Fixed: Game crashes after save DustSpriteColorController
        SaveLoadAction.InternalSafeAdd(
            (savedValues, _) => SaveLoadAction.SaveStaticMemberValues(savedValues, typeof(DustStyles), "Styles"),
            (savedValues, _) => SaveLoadAction.LoadStaticMemberValues(savedValues)
        );
    }
}
