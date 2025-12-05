namespace Celeste.Mod.SpeedrunTool.SaveLoad.ThirdPartySupport;
internal static class IsaGrabBagUtils {

    internal static void Support() {
        // 解决读档后冲进 DreamSpinner 会被刺死
        SaveLoadAction.CloneModTypeFields("IsaGrabBag", "Celeste.Mod.IsaGrabBag.GrabBagModule", "ZipLineState", "playerInstance");
        SaveLoadAction.CloneModTypeFields("IsaGrabBag", "Celeste.Mod.IsaGrabBag.BadelineFollower", "booster", "LookForBubble");
    }
}
