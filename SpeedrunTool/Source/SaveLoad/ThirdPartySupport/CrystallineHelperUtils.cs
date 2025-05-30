
namespace Celeste.Mod.SpeedrunTool.SaveLoad.ThirdPartySupport;
internal static class CrystallineHelperUtils {

    internal static void Support() {
        SaveLoadAction.CloneModTypeFields("CrystallineHelper", "vitmod.VitModule", "timeStopScaleTimer", "timeStopType", "noMoveScaleTimer");
        SaveLoadAction.CloneModTypeFields("CrystallineHelper", "vitmod.TriggerTrigger", "collidedEntities");
    }
}
