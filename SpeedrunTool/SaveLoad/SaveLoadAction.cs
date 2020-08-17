using System;
using System.Collections.Generic;
using Celeste.Mod.SpeedrunTool.Extensions;
using FMOD.Studio;
using Force.DeepCloner;
using Monocle;

namespace Celeste.Mod.SpeedrunTool.SaveLoad {
    public class SaveLoadAction {
        private static readonly List<SaveLoadAction> all = new List<SaveLoadAction>();

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
            all.Add(saveLoadAction);
        }

        internal static void OnSaveState(Level level) {
            foreach (SaveLoadAction saveLoadAction in all) {
                saveLoadAction.saveState(saveLoadAction.savedValues, level);
            }
        }

        internal static void OnLoadState(Level level) {
            foreach (SaveLoadAction saveLoadAction in all) {
                saveLoadAction.loadState(saveLoadAction.savedValues, level);
            }
        }

        internal static void OnClearState() {
            foreach (SaveLoadAction saveLoadAction in all) {
                saveLoadAction.savedValues.Clear();
            }
        }

        private static void saveStaticFieldValues(Dictionary<string, object> values, Type type, params string[] fieldNames) {
            foreach (var fieldName in fieldNames) {
                values[fieldName] = type.GetFieldValue(fieldName).DeepClone(StateManager.Instance.SharedCloneState);
            }
        }

        private static void loadStaticFieldValues(Dictionary<string, object> values, Type type) {
            foreach (string fieldName in values.Keys) {
                type.SetFieldValue(fieldName, values[fieldName].DeepClone(StateManager.Instance.SharedCloneState));
            }
        }

        internal static void OnLoad() {
            // ExtendedVariantMode JumpCount
            bool extendedVariantModeInstalled = Everest.Loader.DependencyLoaded(new EverestModuleMetadata
                {Name = "ExtendedVariantMode", Version = new Version(0, 15, 20)});
            if (extendedVariantModeInstalled) {
                Type type = Type.GetType("ExtendedVariants.Variants.JumpCount, ExtendedVariantMode");
                if (type == null) return;

                all.Add(new SaveLoadAction(
                    (savedValues, level) => { saveStaticFieldValues(savedValues, type, "jumpBuffer"); },
                    (savedValues, level) => { loadStaticFieldValues(savedValues, type); }
                ));
            }

            // MaxHelpingHand RainbowSpinnerColorController
            bool maxHelpingHandInstalled = Everest.Loader.DependencyLoaded(new EverestModuleMetadata
                {Name = "MaxHelpingHand", Version = new Version(1, 5, 2)});
            if (maxHelpingHandInstalled) {
                Type type = Type.GetType("Celeste.Mod.MaxHelpingHand.Entities.RainbowSpinnerColorController, MaxHelpingHand");
                if (type == null) return;

                all.Add(new SaveLoadAction(
                    (savedValues, level) => {
                        saveStaticFieldValues(savedValues, type,
                            "rainbowSpinnerHueHooked", "transitionProgress", "spinnerControllerOnScreen", "nextSpinnerController");
                    },
                    (savedValues, level) => {
                        loadStaticFieldValues(savedValues, type);
                        if ((bool) savedValues["rainbowSpinnerHueHooked"] && type.GetFieldValue("spinnerControllerOnScreen") is Entity entity) {
                            object nextSpinnerController = type.GetFieldValue("nextSpinnerController");
                            type.SetFieldValue("rainbowSpinnerHueHooked", false);
                            // 借助 awake 方法 执行 On.Celeste.CrystalStaticSpinner.GetHue += getRainbowSpinnerHue;
                            entity.Awake(entity.Scene);
                            type.SetFieldValue("nextSpinnerController", nextSpinnerController);
                        }
                    }
                ));
            }

            // Audio Music
            all.Add(new SaveLoadAction(
                (savedValues, level) => {
                    savedValues.Add("CurrentMusic", Audio.CurrentMusic);
                    savedValues.Add("CurrentAmbience", Audio.GetEventName(Audio.CurrentAmbienceEventInstance));
                    savedValues.Add("CurrentAltMusic", Audio.GetEventName(typeof(Audio).GetFieldValue("currentAltMusicEvent") as EventInstance));
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
            all.Clear();
        }
    }
}