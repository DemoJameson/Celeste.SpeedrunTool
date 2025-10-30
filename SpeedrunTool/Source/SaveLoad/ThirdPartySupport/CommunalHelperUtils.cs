
namespace Celeste.Mod.SpeedrunTool.SaveLoad.ThirdPartySupport;
internal static class CommunalHelperUtils {

    [Obsolete("these partially exist in CommunalHelper, we plan to remove it from SRT")]
    internal static void Support() {

        SaveLoadAction.CloneModTypeFields("CommunalHelper", "Celeste.Mod.CommunalHelper.DashStates.SeekerDash",
            "hasSeekerDash",
            "seekerDashAttacking",
            "seekerDashTimer",
            "seekerDashLaunched",
            "launchPossible");
    }
}
