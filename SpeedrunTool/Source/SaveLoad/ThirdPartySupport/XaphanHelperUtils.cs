
namespace Celeste.Mod.SpeedrunTool.SaveLoad.ThirdPartySupport;
internal static class XaphanHelperUtils {

    internal static void Support() {
        SaveLoadAction.CloneModTypeFields("XaphanHelper", "Celeste.Mod.XaphanHelper.Upgrades.SpaceJump", "jumpBuffer");
    }
}
