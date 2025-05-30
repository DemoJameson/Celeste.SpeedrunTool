using Celeste.Mod.SpeedrunTool.Other;
using Celeste.Mod.SpeedrunTool.SaveLoad;
using MonoMod.ModInterop;

namespace Celeste.Mod.SpeedrunTool.ModInterop;

// only used for tas
// does not actually support multiple save slots (need SpeedrunTool v3.25.0)
// it's here only for compatibility
internal static class TasSpeedrunToolInterop {
    [Load]
    private static void Initialize() {
        typeof(Exports).ModInterop(); 
    }


    [ModExportName("SpeedrunTool.TasAction")]
    internal static class Exports {
        public static bool SaveState(string _) {
            return StateManager.Instance.SaveState();
        }

        public static bool LoadState(string _) {
            return StateManager.Instance.LoadState();
        }

        public static void ClearState(string _) {
            StateManager.Instance.ClearState();
        }

        public static bool TasIsSaved(string _) => StateManager.Instance.IsSaved && StateManager.Instance.SavedByTas;

        public static void InputDeregister() {
            foreach (HotkeyConfig hotkeyConfig in HotkeyConfigUi.HotkeyConfigs.Values) {
                hotkeyConfig.VirtualButton.Value.Deregister();
            }
        }
    }
}

