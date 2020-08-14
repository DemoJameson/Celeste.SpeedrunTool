using System;
using System.Collections.Generic;
using Celeste.Mod.SpeedrunTool.Extensions;
using ExtendedVariants.Variants;
using FMOD.Studio;
using Monocle;

namespace Celeste.Mod.SpeedrunTool.SaveLoad {
    public class SaveLoadAction {
        private static readonly List<SaveLoadAction> all = new List<SaveLoadAction>();

        private readonly Dictionary<string, object> savedValues = new Dictionary<string, object>();
        private readonly Action<Dictionary<string, object>, Level> saveState;
        private readonly Action<Dictionary<string, object>, Level, List<Entity>> loadState;

        public SaveLoadAction(
            Action<Dictionary<string, object>, Level> saveState,
            Action<Dictionary<string, object>, Level, List<Entity>> loadState) {
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

        internal static void OnLoadState(Level level, List<Entity> savedEntities) {
            foreach (SaveLoadAction saveLoadAction in all) {
                saveLoadAction.loadState(saveLoadAction.savedValues, level, savedEntities);
            }
        }

        internal static void OnClearState() {
            foreach (SaveLoadAction saveLoadAction in all) {
                saveLoadAction.savedValues.Clear();
            }
        }

        internal static void OnLoad() {
            // ExtendedVariantMode JumpCount
            bool extendedVariantModeInstalled = Everest.Loader.DependencyLoaded(new EverestModuleMetadata
                {Name = "ExtendedVariantMode", Version = new Version(0, 15, 20)});
            if (extendedVariantModeInstalled) {
                all.Add(new SaveLoadAction(
                    (savedValues, level) => { savedValues.Add("jumpBuffer", JumpCount.GetJumpBuffer()); },
                    (savedValues, level, savedEntities) => {
                        typeof(JumpCount).SetFieldValue("jumpBuffer", savedValues["jumpBuffer"]);
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
                (savedValues, level, savedEntities) => {
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