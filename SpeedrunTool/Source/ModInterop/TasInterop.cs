using Celeste.Mod.SpeedrunTool.SaveLoad;
using MonoMod.ModInterop;

namespace Celeste.Mod.SpeedrunTool.ModInterop;

[ModExportName("SpeedrunTool.SaveLoadTas")]
internal static class TasInterop {

    /// <summary>
    /// Save to the tas save slot
    /// </summary>
    public static bool SaveState() {
        return SaveSlotsManager.SaveState(tas: true);
    }

    /// <summary>
    /// Load from the tas save slot
    /// </summary>
    public static bool LoadState() {
        return SaveSlotsManager.LoadState(tas: true);
    }
    /// <summary>
    /// If the tas save slot is saved
    /// </summary>
    public static bool TasIsSaved() => SaveSlotsManager.IsSaved(SaveSlotsManager.TasSlot);
}
