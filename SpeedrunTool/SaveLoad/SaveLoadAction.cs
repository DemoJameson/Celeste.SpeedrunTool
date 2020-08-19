using System;
using System.Collections.Generic;
using Celeste.Mod.SpeedrunTool.Extensions;
using FMOD.Studio;
using Monocle;

namespace Celeste.Mod.SpeedrunTool.SaveLoad {
    public class SaveLoadAction {
        private static readonly List<SaveLoadAction> All = new List<SaveLoadAction>();

        private readonly Dictionary<string, object> savedValues = new Dictionary<string, object>();
        private readonly Action<Dictionary<string, object>, Level> saveState;
        private readonly Action<Dictionary<string, object>, Level> loadState;

        public SaveLoadAction(
            Action<Dictionary<string, object>, Level> saveState,
            Action<Dictionary<string, object>, Level> loadState) {
            this.saveState = saveState;
            this.loadState = loadState;
        }

        public static void Add(SaveLoadAction saveLoadAction) {
            All.Add(saveLoadAction);
        }

        internal static void OnSaveState(Level level) {
            foreach (SaveLoadAction saveLoadAction in All) {
                saveLoadAction.saveState(saveLoadAction.savedValues, level);
            }
        }

        internal static void OnLoadState(Level level) {
            foreach (SaveLoadAction saveLoadAction in All) {
                saveLoadAction.loadState(saveLoadAction.savedValues, level);
            }
        }

        internal static void OnClearState() {
            foreach (SaveLoadAction saveLoadAction in All) {
                saveLoadAction.savedValues.Clear();
            }
        }

        private static void SaveStaticFieldValues(Dictionary<string, object> values, Type type,
            params string[] fieldNames) {
            foreach (var fieldName in fieldNames) {
                values[fieldName] = type.GetFieldValue(fieldName).DeepCloneShared();
            }
        }

        private static void LoadStaticFieldValues(Dictionary<string, object> values, Type type) {
            foreach (string fieldName in values.Keys) {
                type.SetFieldValue(fieldName, values[fieldName].DeepCloneShared());
            }
        }

        internal static void OnLoad() {
            // ExtendedVariantMode JumpCount
            if (Type.GetType("ExtendedVariants.Variants.JumpCount, ExtendedVariantMode") is Type jumpCountType) {
                All.Add(new SaveLoadAction(
                    (savedValues, level) => { SaveStaticFieldValues(savedValues, jumpCountType, "jumpBuffer"); },
                    (savedValues, level) => { LoadStaticFieldValues(savedValues, jumpCountType); }
                ));
            }

            // MaxHelpingHand RainbowSpinnerColorController
            if (Type.GetType("Celeste.Mod.MaxHelpingHand.Entities.RainbowSpinnerColorController, MaxHelpingHand") is
                Type colorControllerType) {
                All.Add(new SaveLoadAction(
                    (savedValues, level) => {
                        SaveStaticFieldValues(savedValues, colorControllerType,
                            "rainbowSpinnerHueHooked", "transitionProgress", "spinnerControllerOnScreen",
                            "nextSpinnerController");
                    },
                    (savedValues, level) => {
                        LoadStaticFieldValues(savedValues, colorControllerType);
                        if ((bool) savedValues["rainbowSpinnerHueHooked"] &&
                            colorControllerType.GetFieldValue("spinnerControllerOnScreen") is Entity entity) {
                            object nextSpinnerController = colorControllerType.GetFieldValue("nextSpinnerController");
                            colorControllerType.SetFieldValue("rainbowSpinnerHueHooked", false);
                            // 借助 awake 方法 执行 On.Celeste.CrystalStaticSpinner.GetHue += getRainbowSpinnerHue;
                            entity.Awake(entity.Scene);
                            colorControllerType.SetFieldValue("nextSpinnerController", nextSpinnerController);
                        }
                    }
                ));
            }

            // PandorasBox TimeField
            if (Type.GetType("Celeste.Mod.PandorasBox.TimeField, PandorasBox") is Type timeFieldType) {
                All.Add(new SaveLoadAction(
                    (savedValues, level) => {
                        SaveStaticFieldValues(savedValues, timeFieldType,
                            "baseTimeRate",
                            "ourLastTimeRate",
                            "playerTimeRate",
                            "hookAdded"
                            );
                        // 直接克隆 WeakReference 里面的 Target 不会被克隆，而且似乎会造成一些问题原因未知，体现为保存状态后自杀报错：
                        // player.collider 的类型都变了
                        // System.Exception: Collisions against the collider type are not implemented!
                        // 在 Monocle.Collider.Collide(Collider collider)
                        savedValues["-player-"] = timeFieldType.GetFieldValue("targetPlayer").GetPropertyValue("Target").DeepCloneShared();
                        savedValues["-timeField-"] = timeFieldType.GetFieldValue("lingeringTarget").GetPropertyValue("Target").DeepCloneShared();
                    },
                    (savedValues, level) => {
                        if ((bool) savedValues["hookAdded"]) {
                            timeFieldType.InvokeMethod("AddHook");
                        }
                        LoadStaticFieldValues(savedValues, timeFieldType);
                        timeFieldType.GetFieldValue("targetPlayer").SetPropertyValue("Target", savedValues["-player-"].DeepCloneShared());
                        timeFieldType.GetFieldValue("lingeringTarget").SetPropertyValue("Target", savedValues["-timeField-"].DeepCloneShared());
                    }
                ));
            }

            // Audio Music
            All.Add(new SaveLoadAction(
                (savedValues, level) => {
                    savedValues.Add("CurrentMusic", Audio.CurrentMusic);
                    savedValues.Add("CurrentAmbience", Audio.GetEventName(Audio.CurrentAmbienceEventInstance));
                    savedValues.Add("CurrentAltMusic",
                        Audio.GetEventName(typeof(Audio).GetFieldValue("currentAltMusicEvent") as EventInstance));
                    savedValues.Add("MusicUnderwater", Audio.MusicUnderwater);
                },
                (savedValues, level) => {
                    Audio.SetMusic(savedValues["CurrentMusic"] as string);
                    Audio.SetAmbience(savedValues["CurrentAmbience"] as string);
                    Audio.SetAltMusic(savedValues["CurrentAltMusic"] as string);
                    Audio.MusicUnderwater = (bool) savedValues["MusicUnderwater"];
                }
            ));
        }

        internal static void OnUnload() {
            All.Clear();
        }
    }
}