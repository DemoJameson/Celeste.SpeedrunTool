
namespace Celeste.Mod.SpeedrunTool.SaveLoad.ThirdPartySupport;
internal static class CommunalHelperUtils {

    [Obsolete]
    internal static void Support() {
        // these partially exist in CommunalHelper, we plan to remove it from SRT

        SaveLoadAction.CloneModTypeFields("CommunalHelper", "Celeste.Mod.CommunalHelper.DashStates.DreamTunnelDash",
            "StDreamTunnelDash",
            "hasDreamTunnelDash",
            "dreamTunnelDashAttacking",
            "dreamTunnelDashTimer",
            "nextDashFeather",
            "FeatherMode",
            "overrideDreamDashCheck",
            "DreamTrailColorIndex");

        SaveLoadAction.CloneModTypeFields("CommunalHelper", "Celeste.Mod.CommunalHelper.DashStates.SeekerDash",
            "hasSeekerDash",
            "seekerDashAttacking",
            "seekerDashTimer",
            "seekerDashLaunched",
            "launchPossible");
    }
}
