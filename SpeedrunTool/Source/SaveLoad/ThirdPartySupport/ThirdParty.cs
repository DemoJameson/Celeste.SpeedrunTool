using Celeste.Mod.SpeedrunTool.Utils;

namespace Celeste.Mod.SpeedrunTool.SaveLoad.ThirdPartySupport;

internal static class ThirdParty {

    internal static void Support() {
        EasyMods.Support();
        ComplexModsSupport();
    }

    private static void ComplexModsSupport() {
        PandorasBoxUtils.Support();
        SpringCollab2020Utils.Support();
        ExtendedVariantsUtils.Support();
        IsaGrabBagUtils.Support();
        SpirialisHelperUtils.Support();
        DeathTrackerHelperUtils.Support();
        BrokemiaHelperUtils.Support();
    }


    private static class EasyMods {

        internal static void Support() {
            CommunalHelperSupport();
            CrystallineHelperSupport();
            MaxHelpingHandSupport();
            VivHelperSupport();
            XaphanHelperSupport();
            LocksmithHelperSupport();
        }

        [Obsolete("these partially exist in CommunalHelper, we plan to remove it from SRT")]
        private static void CommunalHelperSupport() {

            SaveLoadAction.CloneModTypeFields("CommunalHelper", "Celeste.Mod.CommunalHelper.DashStates.SeekerDash",
                "hasSeekerDash",
                "seekerDashAttacking",
                "seekerDashTimer",
                "seekerDashLaunched",
                "launchPossible");
        }
        private static void CrystallineHelperSupport() {
            SaveLoadAction.CloneModTypeFields("CrystallineHelper", "vitmod.VitModule", "timeStopScaleTimer", "timeStopType", "noMoveScaleTimer");
            SaveLoadAction.CloneModTypeFields("CrystallineHelper", "vitmod.TriggerTrigger", "collidedEntities");
        }
        private static void MaxHelpingHandSupport() {
            SaveLoadAction.CloneModTypeFields("MaxHelpingHand", "Celeste.Mod.MaxHelpingHand.Effects.BlackholeCustomColors", "colorsMildOverride");
            SaveLoadAction.CloneModTypeFields("MaxHelpingHand", "Celeste.Mod.MaxHelpingHand.Entities.MovingFlagTouchSwitch", "flagMapping");
        }
        private static void VivHelperSupport() {
            if (ModUtils.GetAssembly("VivHelper") is not { }) {
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
        private static void XaphanHelperSupport() {
            SaveLoadAction.CloneModTypeFields("XaphanHelper", "Celeste.Mod.XaphanHelper.Upgrades.SpaceJump", "jumpBuffer");
        }
        private static void LocksmithHelperSupport() {
            SaveLoadAction.CloneModTypeFields("LocksmithHelper", "Celeste.Mod.LocksmithHelper.Entities.Key", "Inventory");
        }
    }

}


