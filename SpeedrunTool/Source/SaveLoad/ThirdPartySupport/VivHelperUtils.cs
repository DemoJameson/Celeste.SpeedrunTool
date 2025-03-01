using Celeste.Mod.SpeedrunTool.Utils;

namespace Celeste.Mod.SpeedrunTool.SaveLoad.ThirdPartySupport;
internal static class VivHelperUtils {

    internal static void Support() {
        if (ModUtils.GetAssembly("VivHelper") is not { } vivHelper) {
            return;
        }

        SaveLoadAction.CloneModTypeFields("VivHelper", "VivHelper.Entities.RefillCancel", "inSpace", "DashRefillRestrict", "DashRestrict", "StaminaRefillRestrict", "p");
        SaveLoadAction.CloneModTypeFields("VivHelper", "VivHelper.Entities.SpeedPowerup", "Store", "Launch");
        SaveLoadAction.CloneModTypeFields("VivHelper", "VivHelper.Entities.BooMushroom", "color", "mode");
        SaveLoadAction.CloneModTypeFields("VivHelper", "VivHelper.Entities.Boosters.BoostFunctions", "dyn");
        SaveLoadAction.CloneModTypeFields("VivHelper", "VivHelper.Entities.Boosters.OrangeBoost", "timer");
        SaveLoadAction.CloneModTypeFields("VivHelper", "VivHelper.Entities.Boosters.PinkBoost", "timer");
        SaveLoadAction.CloneModTypeFields("VivHelper", "VivHelper.Entities.Boosters.WindBoost", "timer");
        SaveLoadAction.CloneModTypeFields("VivHelper", "VivHelper.Entities.ExplodeLaunchModifier", "DisableFreeze", "DetectFreeze", "bumperWrapperType");
        SaveLoadAction.CloneModTypeFields("VivHelper", "VivHelper.Entities.Blockout", "alphaFade");
        SaveLoadAction.CloneModTypeFields("VivHelper", "VivHelper.MoonHooks", "FloatyFix");
        SaveLoadAction.CloneModTypeFields("VivHelper", "VivHelper.HelperEntities", "AllUpdateHelperEntity");
        SaveLoadAction.CloneModTypeFields("VivHelper", "VivHelper.Module__Extensions__Etc.TeleportV2Hooks", "HackedFocusPoint");
    }
}
