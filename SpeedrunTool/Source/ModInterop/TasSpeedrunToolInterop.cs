using Celeste.Mod.SpeedrunTool.Other;
using Celeste.Mod.SpeedrunTool.SaveLoad;
using MonoMod.ModInterop;

namespace Celeste.Mod.SpeedrunTool.ModInterop;

// only used for tas
internal static class TasSpeedrunToolInterop {
    [Load]
    private static void Initialize() {
        typeof(Exports).ModInterop();
    }


    [ModExportName("SpeedrunTool.TasAction")]
    internal static class Exports {
        /// <summary>
        /// Save to the tas save slot
        /// </summary>
        public static bool SaveState(string slot) {
            return SaveSlotsManager.SaveStateTas(slot);
        }

        /// <summary>
        /// Load from the tas save slot
        /// </summary>
        public static bool LoadState(string slot) {
            return SaveSlotsManager.LoadStateTas(slot);
        }

        /// <summary>
        /// Clear the tas save slot
        /// </summary>
        public static void ClearState(string slot) {
            SaveSlotsManager.ClearStateTas(slot);
        }
        /// <summary>
        /// If the tas save slot is saved
        /// </summary>
        public static bool TasIsSaved(string slot) => SaveSlotsManager.IsSaved(slot);

        public static void InputDeregister() {
            // do nothing
        }
    }
}


