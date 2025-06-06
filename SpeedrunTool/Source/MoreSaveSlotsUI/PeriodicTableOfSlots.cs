using Celeste.Mod.SpeedrunTool.SaveLoad;
using System.Collections.Generic;

namespace Celeste.Mod.SpeedrunTool.MoreSaveSlotsUI;
internal class PeriodicTableOfSlots {

    private static readonly Dictionary<string, int> ReverseDictionary = new Dictionary<string, int>();

    public static int RegularSlotsCount = 9;

    public static int CurrentSlotIndex {
        get {
            if (ReverseDictionary.TryGetValue(SaveSlotsManager.SlotName, out int num)) {
                return num;
            }
            return -1;
        }
    }

    [Load]

    private static void Load() {
        for (int i = 1; i <= RegularSlotsCount; i++) {
            ReverseDictionary.Add(SaveSlotsManager.GetSlotName(i), i);
        }
    }

    internal static int ModuloAdd(int num, int dir) {
        // assume 1 <= a <= SlotsCount, and dir = plus minus 1
        if (dir == 1) {
            if (num < RegularSlotsCount) {
                num++;
            }
            else {
                num = 1;
            }
        }
        else {
            if (num > 1) {
                num--;
            }
            else {
                num = RegularSlotsCount;
            }
        }
        return num;
    }
}
