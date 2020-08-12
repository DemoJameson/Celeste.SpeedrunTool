using System;
using System.Collections.Generic;
using Celeste.Mod.SpeedrunTool.Extensions;
using ExtendedVariants.Variants;

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

        internal static void OnInit() {
            bool extendedVariantModeInstalled = Everest.Loader.DependencyLoaded(new EverestModuleMetadata
                {Name = "ExtendedVariantMode", Version = new Version(0, 15, 20)});
            if (extendedVariantModeInstalled) {
                all.Add(new SaveLoadAction(
                    (savedValues, level) => {
                        savedValues.Add("jumpBuffer", JumpCount.GetJumpBuffer());
                    },
                    (savedValues, level) => {
                        typeof(JumpCount).SetFieldValue("jumpBuffer", savedValues["jumpBuffer"]);
                    }
                ));
            }
        }
    }
}