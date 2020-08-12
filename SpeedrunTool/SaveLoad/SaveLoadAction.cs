using System;
using System.Collections.Generic;
using Celeste.Mod.SpeedrunTool.Extensions;
using ExtendedVariants.Variants;

namespace Celeste.Mod.SpeedrunTool.SaveLoad {
    public class SaveLoadAction {
        private static readonly List<SaveLoadAction> all = new List<SaveLoadAction>();

        private readonly Dictionary<string, object> savedValues = new Dictionary<string, object>();
        private readonly Action<Dictionary<string, object>, Level> OnSaveState;
        private readonly Action<Dictionary<string, object>, Level> OnLoadState;

        public SaveLoadAction(
            Action<Dictionary<string, object>, Level> onSaveState,
            Action<Dictionary<string, object>, Level> onLoadState) {
            OnSaveState = onSaveState;
            OnLoadState = onLoadState;
        }

        public static void InvokeSaveState(Level level) {
            foreach (SaveLoadAction saveLoadAction in all) {
                saveLoadAction.OnSaveState(saveLoadAction.savedValues, level);
            }
        }

        public static void InvokeLoadState(Level level) {
            foreach (SaveLoadAction saveLoadAction in all) {
                saveLoadAction.OnLoadState(saveLoadAction.savedValues, level);
            }
        }

        public static void InvokeClearState() {
            foreach (SaveLoadAction saveLoadAction in all) {
                saveLoadAction.savedValues.Clear();
            }
        }

        public static void OnInit() {
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