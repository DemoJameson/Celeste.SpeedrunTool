using Celeste.Mod.SpeedrunTool.Message;
using Celeste.Mod.SpeedrunTool.Other;
using System.Collections.Generic;

namespace Celeste.Mod.SpeedrunTool.SaveLoad;
internal static class SaveSlotsManager {
    public static IEnumerable<SaveSlot> SaveSlots => Dictionary.Values;

    public static Dictionary<string, SaveSlot> Dictionary = new Dictionary<string, SaveSlot>();

    internal static SaveSlot Slot;

    internal static string SlotName = GetSlotName(1);

    public static StateManager StateManagerInstance => Slot.StateManager;

    public static bool IsSaved(string name) {
        if (Dictionary.TryGetValue(name, out SaveSlot slot)) {
            return slot.StateManager.IsSaved;
        }
        return false;
    }
    public static bool IsSaved() {
        return Slot.StateManager.IsSaved;
    }

    public static bool SwitchSlot(int index) {
        return SwitchSlot(GetSlotName(index));
    }

    public static bool SwitchSlot(string name) {
        if (name != SlotName && !IsAllFree()) {
            return false;
        }
        if (name != SlotName) {
            Logger.Log("SpeedrunTool", $"Switch to [{name}]");
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

    public static bool CannotSaveLoad() {
        if (Engine.Scene is not Level) {
            return true;
        }

        if (!IsAllFree()) {
            return true;
        }
        return false;
    }
    public static bool SaveState() {
        if (CannotSaveLoad()) {
            return false;
        }

        return StateManagerInstance.SaveStateImpl(false);
    }


    public static bool LoadState() {
        if (CannotSaveLoad()) {
            return false;
        }

        return StateManagerInstance.LoadStateImpl(false);
    }


    /// <summary>
    /// Clear current save slot 
    /// </summary>
    public static void ClearState() {
        StateManagerInstance.ClearStateImpl();
    }

    public static void ClearStateAndShowMessage() {
        StateManagerInstance.ClearStateAndShowMessage();
    }

    public static void ClearAll() {
        foreach (SaveSlot slot in Dictionary.Values) {
            slot.StateManager.ClearStateImpl();
        }
        PopupMessageUtils.Show(DialogIds.ClearAllToolTip.DialogClean(), DialogIds.ClearAllDialog);
    }

    /// <summary>
    /// When StateManager is busy (saving/loading/waiting), we shouldn't do anything
    /// </summary>
    public static bool IsFree(this StateManager manager) {
        return manager.State == State.None;
    }
    /// <summary>
    /// When any StateManager is busy (saving/loading/waiting), we shouldn't do anything
    /// </summary>
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

    internal static void AfterAssetReload() {
        Dictionary = new Dictionary<string, SaveSlot>();
        SwitchSlot(1);
        ClearState();
    }

    #region Tas
    public static bool SaveStateTas(string slot) {
        if (CannotSaveLoad()) {
            return false;
        }

        string orig = SlotName;
        SwitchSlot(slot);
        bool result = StateManagerInstance.SaveStateImpl(true);
        SwitchSlot(orig);
        return result;
    }
    public static bool LoadStateTas(string slot) {
        if (CannotSaveLoad()) {
            return false;
        }

        string orig = SlotName;
        SwitchSlot(slot);
        bool result = StateManagerInstance.LoadStateImpl(true);
        SwitchSlot(orig);
        return result;
    }
    public static void ClearStateTas(string slot) {
        string orig = SlotName;
        if (SwitchSlot(slot)) {
            StateManagerInstance.ClearStateImpl();
            SwitchSlot(orig);
        }
    }
    #endregion
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