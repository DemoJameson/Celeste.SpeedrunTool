using Celeste.Mod.SpeedrunTool.SaveLoad;
using MonoMod.ModInterop;

namespace Celeste.Mod.SpeedrunTool.ModInterop;

/// <summary>
/// Control-flow surface of the savestate system: pick a slot, trigger a save or a load, ask what is
/// in a slot. Deliberately separate from <see cref="SaveLoadInterop"/>, which is about letting other
/// mods participate in a savestate (registering actions, static types and clone rules) rather than
/// driving one. Cells that only drive the pipeline should import this surface so they never bind
/// anything the registration contract owns.
/// </summary>
public static class SaveLoadControlInterop {
    [Load]
    private static void Load() {
        typeof(SaveLoadControlExports).ModInterop();
    }

    [ModExportName("SpeedrunTool.SaveLoadControl")]
    public static class SaveLoadControlExports {
        /// <summary>
        /// Name of the currently using save slot, appear after v3.25.0
        /// </summary>
        public static string GetSlotName() {
            return SaveSlotsManager.SlotName;
        }

        /// <summary>
        /// Save the current level state into the current save slot.
        /// </summary>
        public static bool SaveState() {
            return SaveSlotsManager.SaveState(out _);
        }

        /// <summary>
        /// Save the current level state into the given save slot (switch to it first).
        /// </summary>
        public static bool SaveState(string slot) {
            return SaveSlotsManager.SwitchSlot(slot) && SaveSlotsManager.SaveState(out _);
        }

        /// <summary>
        /// Load the current save slot.
        /// </summary>
        public static bool LoadState() {
            return SaveSlotsManager.LoadState(out _);
        }

        /// <summary>
        /// Load the given save slot (switch to it first).
        /// </summary>
        public static bool LoadState(string slot) {
            return SaveSlotsManager.SwitchSlot(slot) && SaveSlotsManager.LoadState(out _);
        }

        /// <summary>
        /// Clear the current save slot.
        /// </summary>
        public static bool ClearState() {
            return SaveSlotsManager.ClearState();
        }

        /// <summary>
        /// Clear every save slot.
        /// </summary>
        public static void ClearAllStates() {
            SaveSlotsManager.ClearAll();
        }

        /// <summary>
        /// Switch to the given save slot, returns false if a state is busy.
        /// </summary>
        public static bool SwitchSlot(string slot) {
            return SaveSlotsManager.SwitchSlot(slot);
        }

        /// <summary>
        /// Whether the given slot (or the current one when null) holds a state.
        /// </summary>
        public static bool IsSaved(string slot) {
            return slot is null ? SaveSlotsManager.IsSaved() : SaveSlotsManager.IsSaved(slot);
        }
    }
}
