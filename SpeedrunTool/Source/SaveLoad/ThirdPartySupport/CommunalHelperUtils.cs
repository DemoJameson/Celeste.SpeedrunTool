
namespace Celeste.Mod.SpeedrunTool.SaveLoad.ThirdPartySupport;
internal static class CommunalHelperUtils {

    internal static void Support() {
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
