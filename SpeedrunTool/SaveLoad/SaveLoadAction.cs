using System;
using System.Collections.Generic;
using System.Linq;
using Celeste.Mod.SpeedrunTool.Extensions;
using FMOD.Studio;
using Monocle;

namespace Celeste.Mod.SpeedrunTool.SaveLoad {
    public sealed class SaveLoadAction {
        private static readonly List<SaveLoadAction> All = new List<SaveLoadAction>();

        private readonly Dictionary<Type, Dictionary<string, object>> savedValues = new Dictionary<Type, Dictionary<string, object>>();
        private readonly Action<Dictionary<Type, Dictionary<string, object>>, Level> saveState;
        private readonly Action<Dictionary<Type, Dictionary<string, object>>, Level> loadState;

        public SaveLoadAction(
            Action<Dictionary<Type, Dictionary<string, object>>, Level> saveState = null,
            Action<Dictionary<Type, Dictionary<string, object>>, Level> loadState = null) {
            this.saveState = saveState;
            this.loadState = loadState;
        }

        public static void Add(SaveLoadAction saveLoadAction) {
            All.Add(saveLoadAction);
        }

        internal static void OnSaveState(Level level) {
            foreach (SaveLoadAction saveLoadAction in All) {
                saveLoadAction.saveState?.Invoke(saveLoadAction.savedValues, level);
            }
        }

        internal static void OnLoadState(Level level) {
            foreach (SaveLoadAction saveLoadAction in All) {
                saveLoadAction.loadState?.Invoke(saveLoadAction.savedValues, level);
            }
        }

        internal static void OnClearState() {
            foreach (SaveLoadAction saveLoadAction in All) {
                saveLoadAction.savedValues.Clear();
            }
        }

        private static void SaveStaticFieldValues(Dictionary<Type, Dictionary<string, object>> values, Type type,
            params string[] fieldNames) {
            if (type == null) return;

            if (!values.ContainsKey(type)) {
                values[type] = new Dictionary<string, object>();
            }

            foreach (var fieldName in fieldNames) {
                values[type][fieldName] = type.GetFieldValue(fieldName).DeepCloneShared();
            }
        }

        private static void LoadStaticFieldValues(Dictionary<Type, Dictionary<string, object>> values) {
            foreach (KeyValuePair<Type, Dictionary<string, object>> pair in values) {
                foreach (string fieldName in pair.Value.Keys) {
                    pair.Key.SetFieldValue(fieldName, pair.Value[fieldName].DeepCloneShared());
                }
            }
        }

        internal static void OnLoad() {
            SupportAudioMusic();
            SupportExtendedVariants();
            SupportMaxHelpingHand();
            SupportPandorasBox();
            SupportCrystallineHelper();
            SupportSpringCollab2020();
        }

        private static void SupportCrystallineHelper() {
            Type vitModuleType = Type.GetType("vitmod.VitModule, vitmod");
            Type timeCrystalType = Type.GetType("vitmod.TimeCrystal, vitmod");
            Type noMoveTriggerType = Type.GetType("vitmod.NoMoveTrigger, vitmod");
            Type starCrystalType = Type.GetType("vitmod.StarCrystal, vitmod");

            if (vitModuleType == null && timeCrystalType == null && noMoveTriggerType == null && starCrystalType == null) return;

            All.Add(new SaveLoadAction(
                (savedValues, level) => {
                    SaveStaticFieldValues(savedValues, vitModuleType, "timeStopScaleTimer", "noMoveScaleTimer");
                    SaveStaticFieldValues(savedValues, timeCrystalType, "stopTimer", "stopStage");
                    SaveStaticFieldValues(savedValues, noMoveTriggerType, "stopTimer", "stopStage", "alreadyIn");
                    SaveStaticFieldValues(savedValues, starCrystalType, "starTimer");
                },
                (savedValues, level) => LoadStaticFieldValues(savedValues)
            ));
        }

        private static void SupportAudioMusic() {
            All.Add(new SaveLoadAction(
                (savedValues, level) => {
                    Dictionary<string, object> saved = new Dictionary<string, object>();
                    saved.Add("CurrentMusic", Audio.CurrentMusic);
                    saved.Add("CurrentAmbience", Audio.GetEventName(Audio.CurrentAmbienceEventInstance));
                    saved.Add("CurrentAltMusic", Audio.GetEventName(typeof(Audio).GetFieldValue("currentAltMusicEvent") as EventInstance));
                    saved.Add("MusicUnderwater", Audio.MusicUnderwater);
                    if (Audio.CurrentAmbienceEventInstance != null) {
                        Audio.CurrentAmbienceEventInstance.getParameterValue("strong_wind", out var strongWindValue, out var _);
                        saved.Add("strong_wind", strongWindValue > 0f);
                    }

                    savedValues[typeof(Audio)] = saved;
                },
                (savedValues, level) => {
                    Dictionary<string, object> saved = savedValues[typeof(Audio)];
                    Audio.SetMusic(saved["CurrentMusic"] as string);
                    Audio.SetAmbience(saved["CurrentAmbience"] as string);
                    Audio.SetAltMusic(saved["CurrentAltMusic"] as string);
                    Audio.MusicUnderwater = (bool) saved["MusicUnderwater"];
                    if (saved.ContainsKey("strong_wind") && level.Entities.FindFirst<WindController>() is WindController controller) {
                        controller.InvokeMethod("SetAmbienceStrength", saved["strong_wind"]);
                    }
                }
            ));
        }

        private static void SupportPandorasBox() {
            if (Type.GetType("Celeste.Mod.PandorasBox.TimeField, PandorasBox") is Type timeFieldType
                && Delegate.CreateDelegate(typeof(On.Celeste.Player.hook_Update), timeFieldType.GetMethodInfo("PlayerUpdateHook")) is
                    On.Celeste.Player.hook_Update hookUpdate) {
                All.Add(new SaveLoadAction(
                    (savedValues, level) => {
                        SaveStaticFieldValues(savedValues, timeFieldType,
                            "baseTimeRate",
                            "ourLastTimeRate",
                            "playerTimeRate",
                            "hookAdded"
                        );
                        // 直接克隆 WeakReference<T> 里面的 Target 不会被克隆，而且似乎会造成一些问题原因未知，体现为 Remirrored 3C 保存状态后自杀报错：
                        // player.collider 的类型都变了
                        // System.Exception: Collisions against the collider type are not implemented!
                        // 在 Monocle.Collider.Collide(Collider collider)
                        savedValues[timeFieldType]["-player-"] =
                            timeFieldType.GetFieldValue("targetPlayer").GetPropertyValue("Target").DeepCloneShared();
                        savedValues[timeFieldType]["-timeField-"] =
                            timeFieldType.GetFieldValue("lingeringTarget").GetPropertyValue("Target").DeepCloneShared();
                    },
                    (savedValues, level) => {
                        if ((bool) savedValues[timeFieldType]["hookAdded"]) {
                            On.Celeste.Player.Update += hookUpdate;
                        }

                        LoadStaticFieldValues(savedValues);
                        timeFieldType.GetFieldValue("targetPlayer")
                            .SetPropertyValue("Target", savedValues[timeFieldType]["-player-"].DeepCloneShared());
                        timeFieldType.GetFieldValue("lingeringTarget")
                            .SetPropertyValue("Target", savedValues[timeFieldType]["-timeField-"].DeepCloneShared());
                    }
                ));
            }

            // Fixed: Game crashes after save DustSpriteColorController
            All.Add(new SaveLoadAction(
                (savedValues, level) => { SaveStaticFieldValues(savedValues, typeof(DustStyles), "Styles"); },
                (savedValues, level) => LoadStaticFieldValues(savedValues)
            ));
        }

        private static void SupportMaxHelpingHand() {
            if (Type.GetType("Celeste.Mod.MaxHelpingHand.Entities.RainbowSpinnerColorController, MaxHelpingHand") is Type colorControllerType
                && Delegate.CreateDelegate(typeof(On.Celeste.CrystalStaticSpinner.hook_GetHue),
                        colorControllerType.GetMethodInfo("getRainbowSpinnerHue")) is
                    On.Celeste.CrystalStaticSpinner.hook_GetHue hookGetHue
            ) {
                All.Add(new SaveLoadAction(
                    (savedValues, level) => {
                        SaveStaticFieldValues(savedValues, colorControllerType,
                            "rainbowSpinnerHueHooked", "transitionProgress", "spinnerControllerOnScreen",
                            "nextSpinnerController");
                    },
                    (savedValues, level) => {
                        if ((bool) savedValues[colorControllerType]["rainbowSpinnerHueHooked"]) {
                            On.Celeste.CrystalStaticSpinner.GetHue += hookGetHue;
                        }

                        LoadStaticFieldValues(savedValues);
                    }
                ));
            }
        }

        private static void SupportSpringCollab2020() {
            if (Type.GetType("Celeste.Mod.SpringCollab2020.Entities.RainbowSpinnerColorController, SpringCollab2020") is Type colorControllerType
                && Delegate.CreateDelegate(typeof(On.Celeste.CrystalStaticSpinner.hook_GetHue),
                        colorControllerType.GetMethodInfo("getRainbowSpinnerHue")) is
                    On.Celeste.CrystalStaticSpinner.hook_GetHue hookGetHue
            ) {
                All.Add(new SaveLoadAction(
                    (savedValues, level) => {
                        SaveStaticFieldValues(savedValues, colorControllerType,
                            "rainbowSpinnerHueHooked", "transitionProgress", "spinnerControllerOnScreen",
                            "nextSpinnerController");
                    },
                    (savedValues, level) => {
                        if ((bool) savedValues[colorControllerType]["rainbowSpinnerHueHooked"]) {
                            On.Celeste.CrystalStaticSpinner.GetHue += hookGetHue;
                        }

                        LoadStaticFieldValues(savedValues);
                    }
                ));
            }

            if (Type.GetType("Celeste.Mod.SpringCollab2020.Entities.RainbowSpinnerColorAreaController, SpringCollab2020") is Type
                    colorAreaControllerType
                && Delegate.CreateDelegate(typeof(On.Celeste.CrystalStaticSpinner.hook_GetHue),
                        colorAreaControllerType.GetMethodInfo("getRainbowSpinnerHue")) is
                    On.Celeste.CrystalStaticSpinner.hook_GetHue hookSpinnerGetHue
            ) {
                All.Add(new SaveLoadAction(
                    (savedValues, level) => { SaveStaticFieldValues(savedValues, colorAreaControllerType, "rainbowSpinnerHueHooked"); },
                    (savedValues, level) => {
                        if ((bool) savedValues[colorAreaControllerType]["rainbowSpinnerHueHooked"]) {
                            On.Celeste.CrystalStaticSpinner.GetHue += hookSpinnerGetHue;
                        }

                        LoadStaticFieldValues(savedValues);
                    }
                ));
            }
        }


        private static void SupportExtendedVariants() {
            // 修复：ExtendedVariantTrigger 设置的值在 SL 之后失效
            if (Type.GetType("ExtendedVariants.ExtendedVariantTrigger, ExtendedVariantMode") is Type extendedVariantTrigger) {
                All.Add(new SaveLoadAction(
                    null,
                    (savedValues, level) => {
                        if (!(Engine.Scene.GetPlayer() is Player player) ||
                            !(player.GetFieldValue("triggersInside") is HashSet<Trigger> triggersInside)) return;
                        foreach (Trigger trigger in triggersInside.Where(trigger =>
                            trigger.GetType() == extendedVariantTrigger && (bool) trigger.GetFieldValue(trigger.GetType(), "revertOnLeave"))) {
                            trigger.OnEnter(player);
                        }
                    }));
            }

            if (Type.GetType("ExtendedVariants.Variants.JumpCount, ExtendedVariantMode") is Type jumpCountType) {
                All.Add(new SaveLoadAction(
                    (savedValues, level) => SaveStaticFieldValues(savedValues, jumpCountType, "jumpBuffer"),
                    (savedValues, level) => LoadStaticFieldValues(savedValues)));
            }
        }

        internal static void OnUnload() {
            All.Clear();
        }
    }
}