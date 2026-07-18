using Celeste.Mod.SpeedrunTool.Utils;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

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
        SpirialisHelperUtils.Support();
        DeathTrackerHelperUtils.Support();
        BrokemiaHelperUtils.Support();
    }


    private static class EasyMods {

        internal static void Support() {
            CommunalHelperSupport();
            SpirialisHelperSupport();
            CrystallineHelperSupport();
            SpringCollab2020Support();
            VivHelperSupport();
            XaphanHelperSupport();
            IsaGrabBagSupport();
            LocksmithHelperSupport();
            EmoteModSupport();
        }


        private static void CloneModTypeFields(string modName, params TypeFieldsTuple[] tuples) {
            if (ModUtils.GetAssembly(modName) is not { }) {
                return;
            }

            List<(Type, string[])> list = [];
            foreach (TypeFieldsTuple tuple in tuples) {
                if (ModUtils.GetType(modName, tuple.TypeFullName) is { } modType) {
                    string[] arr = [.. tuple.Fields.Where(memberName =>
                modType.GetMember(memberName, BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic) is not null)];
                    if (arr.Length != 0) {
                        list.Add((modType, arr));
                    }
                }
            }
            if (list.IsNullOrEmpty()) {
                return;
            }
            (Type, string[])[] array = [.. list];

            SaveLoadAction.InternalSafeAdd(
                (savedValues, _) => {
                    foreach ((Type, string[]) pair in array) {
                        SaveLoadAction.SaveStaticMemberValues(savedValues, pair.Item1, pair.Item2);
                    }
                },
                (savedValues, _) => SaveLoadAction.LoadStaticMemberValues(savedValues)
            );
        }

        private class TypeFieldsTuple(string typeFullName, params string[] fields) {

            internal string TypeFullName = typeFullName;

            internal string[] Fields = fields;
        }

        [Obsolete("these partially exist in CommunalHelper, we plan to remove it from SRT")]
        private static void CommunalHelperSupport() {

            CloneModTypeFields("CommunalHelper",
                new TypeFieldsTuple("Celeste.Mod.CommunalHelper.DashStates.SeekerDash",
                    "hasSeekerDash", "seekerDashAttacking", "seekerDashTimer", "seekerDashLaunched", "launchPossible")
            );
        }

        private static void SpirialisHelperSupport() {
            CloneModTypeFields("SpirialisHelper",
                new TypeFieldsTuple("Celeste.Mod.Spirialis.TimePlayerSettings", "instance", "stoppedX", "stoppedY"),
                new TypeFieldsTuple("Celeste.Mod.Spirialis.CustomRainBG", "timeSinceFreeze")
            );
        }
        private static void CrystallineHelperSupport() {
            CloneModTypeFields("CrystallineHelper",
                new TypeFieldsTuple("vitmod.VitModule", "timeStopScaleTimer", "timeStopType", "noMoveScaleTimer"),
                new TypeFieldsTuple("vitmod.TriggerTrigger", "collidedEntities")
            );
        }

        private static void SpringCollab2020Support() {
            CloneModTypeFields("SpringCollab2020",
                new TypeFieldsTuple("Celeste.Mod.SpringCollab2020.Entities.RainbowSpinnerColorController",
                    "spinnerControllerOnScreen", "nextSpinnerController", "transitionProgress")
            );
        }

        private static void VivHelperSupport() {
            CloneModTypeFields("VivHelper",
                new TypeFieldsTuple("VivHelper.Entities.RefillCancel", "inSpace", "DashRefillRestrict", "DashRestrict", "StaminaRefillRestrict", "p"),
                new TypeFieldsTuple("VivHelper.Entities.SpeedPowerup", "Store", "Launch"),
                new TypeFieldsTuple("VivHelper.Entities.BooMushroom", "color", "mode"),
                new TypeFieldsTuple("VivHelper.Entities.Boosters.BoostFunctions", "dyn"),
                new TypeFieldsTuple("VivHelper.Entities.Boosters.OrangeBoost", "timer"),
                new TypeFieldsTuple("VivHelper.Entities.Boosters.PinkBoost", "timer"),
                new TypeFieldsTuple("VivHelper.Entities.Boosters.WindBoost", "timer"),
                new TypeFieldsTuple("VivHelper.Entities.ExplodeLaunchModifier", "DisableFreeze", "DetectFreeze", "bumperWrapperType"),
                new TypeFieldsTuple("VivHelper.Entities.Blockout", "alphaFade"),
                new TypeFieldsTuple("VivHelper.MoonHooks", "FloatyFix"),
                new TypeFieldsTuple("VivHelper.HelperEntities", "AllUpdateHelperEntity"),
                new TypeFieldsTuple("VivHelper.Module__Extensions__Etc.TeleportV2Hooks", "HackedFocusPoint")
            );
        }
        private static void XaphanHelperSupport() {
            CloneModTypeFields("XaphanHelper",
                new TypeFieldsTuple("Celeste.Mod.XaphanHelper.Upgrades.SpaceJump", "jumpBuffer")
            );
        }

        private static void IsaGrabBagSupport() {
            // 解决读档后冲进 DreamSpinner 会被刺死
            CloneModTypeFields("IsaGrabBag",
                new TypeFieldsTuple("Celeste.Mod.IsaGrabBag.GrabBagModule", "ZipLineState", "playerInstance"),
                new TypeFieldsTuple("Celeste.Mod.IsaGrabBag.BadelineFollower", "booster", "LookForBubble")
            );
        }
        private static void LocksmithHelperSupport() {
            CloneModTypeFields("LocksmithHelper",
                new TypeFieldsTuple("Celeste.Mod.LocksmithHelper.Entities.Key", "Inventory")
            );
        }

        private static void EmoteModSupport() {
            CloneModTypeFields("EmoteMod",
                new TypeFieldsTuple("Celeste.Mod.EmoteMod.GravityModule", "playerY"),
                new TypeFieldsTuple("Celeste.Mod.EmoteMod.EmoteModMain", "anim_by_game"),
                new TypeFieldsTuple("Celeste.Mod.EmoteMod.MadhuntModule", "inRound"),
                new TypeFieldsTuple("Celeste.Mod.EmoteMod.SpeedModule", "currentDelay"),
                new TypeFieldsTuple("Celeste.Mod.EmoteMod.EmoteModule", "bounced", "playback", "defaultAnimationsCount"),
                new TypeFieldsTuple("Celeste.Mod.EmoteMod.EmoteStretcher", "x_stretch", "y_stretch", "stretch_lock"),
                new TypeFieldsTuple("Celeste.Mod.EmoteMod.EmoteCancelModule", "invincibilityDefault", "interactDefault", "customEmote")
            );
        }
    }

}


