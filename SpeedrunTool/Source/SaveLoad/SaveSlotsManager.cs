using System.Collections.Generic;
using EventInstance = FMOD.Studio.EventInstance;

namespace Celeste.Mod.SpeedrunTool.SaveLoad;
internal static class SaveSlotsManager {
    public static IEnumerable<SaveSlot> SaveSlots => Dictionary.Values;

    public static Dictionary<string, SaveSlot> Dictionary = new Dictionary<string, SaveSlot>();

    internal static SaveSlot Slot;

    internal static string SlotName { get; private set; } = GetSlotName(1);

    public static StateManager StateManagerInstance => Slot.StateManager;

    public static bool IsSaved(int index) {
        return IsSaved(GetSlotName(index));
    }

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

    /// <summary>
    /// Switch to the slot with the specified name. Will be automatically created if the slot does not exist.
    /// </summary>
    /// <returns> Success or not </returns>
    public static bool SwitchSlot(string name) {
        if (!IsAllFree()) {
            // even we are not actually switching slot (i.e name == SlotName), still return false
            // to ensure safety
            return false;
        }
        if (name != SlotName) {
            Logger.Info("SpeedrunTool", $"Switch to [{name}]");
        }

        // execute even if name == SlotName, so the resulting slot will always be right (it's not necessarily created yet!)
        SlotName = name;
        if (Dictionary.TryGetValue(name, out SaveSlot slot)) {
            Slot = slot;
        }
        else {
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

        bool b = StateManagerInstance.SaveStateImpl(false);
        if (b) {
            MoreSaveSlotsUI.Snapshot.RequireCaptureSnapshot(SlotName);
        }
        return b;
    }


    public static bool LoadState() {
        if (CannotSaveLoad()) {
            return false;
        }

        return StateManagerInstance.LoadStateImpl(tas: false);
    }


    /// <summary>
    /// Clear current save slot 
    /// </summary>
    public static void ClearState() {
        StateManagerInstance.ClearStateImpl(hasGc: true);
    }

    public static void ClearAll() {
        bool anySaved = false;
        foreach (SaveSlot slot in Dictionary.Values) {
            anySaved = anySaved || slot.StateManager.IsSaved;
            slot.StateManager.ClearStateImpl(hasGc: false);
        }
        Dictionary = new Dictionary<string, SaveSlot>();

        MoreSaveSlotsUI.Snapshot.ClearAll();

        SwitchSlot(1);
        if (anySaved) {
            StateManagerInstance.GcCollect(force: true);
        }
    }

    /// <summary>
    /// When StateManager is busy (saving/loading), we shouldn't do anything
    /// </summary>
    public static bool IsFree(this StateManager manager) {
        return manager.State == State.None || manager.State == State.Waiting;
    }
    /// <summary>
    /// When any StateManager is busy (saving/loading), we shouldn't do anything
    /// </summary>
    public static bool IsAllFree() {
        foreach (SaveSlot slot in SaveSlots) {
            if (!IsFree(slot.StateManager)) {
                return false;
            }
        }
        return true;
    }

    internal static void RequireReInit() {
        foreach (SaveSlot slot in SaveSlots) {
            slot.ValueDictionaryInitialized = false;
            slot.AllSavedValues.Clear();
        }
    }
    internal static void AfterAssetReload() {
        Dictionary = new Dictionary<string, SaveSlot>();
        SwitchSlot(1);
        ClearState();
    }

    #region Tas
    public static void OnTasDisableRun() {
        SwitchSlot(1);
    }
    public static bool SaveStateTas(string slot) {
        if (CannotSaveLoad()) {
            return false;
        }

        SwitchSlot(slot);
        bool result = StateManagerInstance.SaveStateImpl(true);
        return result;
    }
    public static bool LoadStateTas(string slot) {
        if (CannotSaveLoad()) {
            return false;
        }

        SwitchSlot(slot);
        bool result = StateManagerInstance.LoadStateImpl(true);
        return result;
    }
    public static void ClearStateTas(string slot) {
        if (SwitchSlot(slot)) {
            StateManagerInstance.ClearStateImpl(hasGc: true);
        }
    }
    #endregion
}

public class SaveSlot {
    public string Name;

    public StateManager StateManager;

    public bool ValueDictionaryInitialized = false;

    public Dictionary<int, Dictionary<Type, Dictionary<string, object>>> AllSavedValues = new();

    public readonly List<EventInstance> ClonedEventInstancesWhenSave = new();
    public readonly List<EventInstance> ClonedEventInstancesWhenPreClone = new();

    public SaveSlot(string name) {
        Name = name;
        ValueDictionaryInitialized = false;
        AllSavedValues = new();
        StateManager = new();
        StateManager.SlotName = Name;
        ClonedEventInstancesWhenSave = new();
        ClonedEventInstancesWhenPreClone = new();
    }
}