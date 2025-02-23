using Celeste.Mod.SpeedrunTool.Message;
using Celeste.Mod.SpeedrunTool.Other;
using System.Collections.Generic;

namespace Celeste.Mod.SpeedrunTool.SaveLoad;
public static class SaveSlotsManager {
    public static IEnumerable<SaveSlot> SaveSlots => Dictionary.Values;

    public static Dictionary<string, SaveSlot> Dictionary = new Dictionary<string, SaveSlot>();

    public static SaveSlot Slot;

    public static string SlotName = GetSlotName(1);

    public static StateManager StateManagerInstance => Slot.StateManager;

    public const string TasSlot = "tas";

    public static bool IsSaved() {
        return Slot.StateManager.IsSaved;
    }

    public static bool SwitchSlot(int index) {
        return SwitchSlot(GetSlotName(index));
    }

    [Command("switch_slot", "Switch to another SRT save slot")]
    public static bool SwitchSlot(string name) {
        if (name != SlotName && !IsAllFree()) {
            return false;
        }
        SlotName = name;
        if (Dictionary.TryGetValue(name, out SaveSlot slot)) {
            Slot = slot;
        } else {
            Slot = new SaveSlot(name);
            Dictionary.Add(name, Slot);
        }
        return true;
    }
    public static string GetSlotName(int index) {
        return index == 1 ? "Default Slot" : $"SaveSlot@{index}";
    }
    public static bool SaveState(bool tas = false) {
        if (Engine.Scene is not Level) {
            return false;
        }

        if (!IsAllFree()) {
            return false;
        }

        if (tas) {
            string orig = SlotName;
            SwitchSlot(TasSlot);
            bool result = StateManagerInstance.SaveStateImpl(true);
            SwitchSlot(orig);
            return result;
        } else {
            return StateManagerInstance.SaveStateImpl(false);
        }

    }

    public static bool LoadState(bool tas = false) {
        if (Engine.Scene is not Level) {
            return false;
        }

        if (!IsAllFree()) {
            return false;
        }

        if (tas) {
            string orig = SlotName;
            SwitchSlot(TasSlot);
            bool result = StateManagerInstance.LoadStateImpl(true);
            SwitchSlot(orig);
            return result;
        } else {
            return StateManagerInstance.LoadStateImpl(false);
        }
    }

    public static void ClearState(bool tas = false) {
        if (tas) {
            string orig = SlotName;
            if (SwitchSlot(TasSlot)) {
                StateManagerInstance.ClearStateImpl();
                SwitchSlot(orig);
            }
        } else {
            StateManagerInstance.ClearStateImpl();
        }
    }

    public static void ClearStateAndShowMessage() {
        StateManagerInstance.ClearStateAndShowMessage();
    }

    public static bool IsFree(this StateManager manager) {
        return manager.State == State.None;
    }

    public static bool IsAllFree() {
        foreach (SaveSlot slot in SaveSlots) {
            if (!IsFree(slot.StateManager)) {
                return false;
            }
        }
        return true;
    }

    internal static void ModRequireReInit() {
        foreach (SaveSlot slot in SaveSlots) {
            slot.SaveLoadActionInitialized = false;
            slot.All.Clear();
        }
    }

    internal static void RegisterHotkeys() {
        Hotkey.SaveSlot1.RegisterPressedAction(_ => SwitchSlotAndShowMessage(1));
        Hotkey.SaveSlot2.RegisterPressedAction(_ => SwitchSlotAndShowMessage(2));
        Hotkey.SaveSlot3.RegisterPressedAction(_ => SwitchSlotAndShowMessage(3));
        Hotkey.SaveSlot4.RegisterPressedAction(_ => SwitchSlotAndShowMessage(4));
        Hotkey.SaveSlot5.RegisterPressedAction(_ => SwitchSlotAndShowMessage(5));
        Hotkey.SaveSlot6.RegisterPressedAction(_ => SwitchSlotAndShowMessage(6));
        Hotkey.SaveSlot7.RegisterPressedAction(_ => SwitchSlotAndShowMessage(7));
        Hotkey.SaveSlot8.RegisterPressedAction(_ => SwitchSlotAndShowMessage(8));
        Hotkey.SaveSlot9.RegisterPressedAction(_ => SwitchSlotAndShowMessage(9));
    }

    private static void SwitchSlotAndShowMessage(int index) {
        if (SwitchSlot(index)) {
            PopupMessageUtils.Show($"Switch to {SlotName}", null);
        }
    }
}

public class SaveSlot {
    public string Name;

    public StateManager StateManager;

    public bool SaveLoadActionInitialized = false;

    public List<SaveLoadAction> All = new();

    public SaveSlot(string name) {
        Name = name;
        SaveLoadActionInitialized = false;
        All = new();
        StateManager = new();
    }
}