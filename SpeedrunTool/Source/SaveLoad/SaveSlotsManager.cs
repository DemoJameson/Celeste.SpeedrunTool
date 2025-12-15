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
        WaitUntilThreadSafe();
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
    public static bool SaveState(out string popup) {
        bool b;
        if (CannotSaveLoad()) {
            b = false;
            popup = "Failed to Save: SpeedrunTool is Busy!";
        }
        else {
            b = StateManagerInstance.SaveStateImpl(false, out string message);
            popup = message;
            if (b) {
                MoreSaveSlotsUI.Snapshot.RequireCaptureSnapshot(SlotName);
            }
        }
        if (!b) {
            Logger.Verbose("SpeedrunTool/SaveState", popup);
        }

        return b;
    }


    public static bool LoadState(out string popup) {
        bool b;
        if (CannotSaveLoad()) {
            b = false;
            popup = "Failed to Save: SpeedrunTool is Busy!";
        }
        else {
            b = StateManagerInstance.LoadStateImpl(false, out string message);
            popup = message;
        }
        if (!b) {
            Logger.Verbose("SpeedrunTool/LoadState", popup);
        }

        return b;
    }


    /// <summary>
    /// Clear current save slot 
    /// </summary>
    public static void ClearState() {
        StateManagerInstance.ClearStateImpl(hasGc: true);
    }

    public static void ClearAll() {
        WaitUntilThreadSafe();
        bool anySaved = false;
        foreach (SaveSlot slot in Dictionary.Values) {
            anySaved = anySaved || slot.StateManager.IsSaved;
            Slot = slot; // 由于 clearState action 可能依赖于 slot, 所以这是必要的
            SlotName = Slot.Name; // Name 当然也要同步修改
            ClearState();
        }
        Dictionary = new Dictionary<string, SaveSlot>();

        MoreSaveSlotsUI.Snapshot.ClearAll();

        SwitchSlot(1);
        if (anySaved) {
            StateManagerInstance.GcCollect(force: true);
        }
    }

    private static void ClearStateWhenSwitchScene(On.Monocle.Scene.orig_Begin orig, Scene self) {
        orig(self);
        SaveSlot current = Slot;
        WaitUntilThreadSafe();
        foreach (SaveSlot slot in SaveSlots) {
            Slot = slot;
            SlotName = Slot.Name;
            slot.StateManager.ClearStateWhenSwitchSceneImpl(self);
        }
        Slot = current;
        SlotName = current.Name;
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

    private static void WaitUntilThreadSafe() {
        // preCloneTask 涉及到多线程, 而 DeepClonerUtils 会多次用到 StateManager.Instance, 这玩意线程不安全
        // 所以我们一定要等它结束了再说
        foreach (SaveSlot s in SaveSlots) {
            s.StateManager.preCloneTask?.Wait();
        }
        // 此外 clearState 等也有可能访问 StateManager.Instance
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

    [Load]
    private static void Load() {
        On.Monocle.Scene.Begin += ClearStateWhenSwitchScene;
    }

    [Unload]
    private static void Unload() {
        On.Monocle.Scene.Begin -= ClearStateWhenSwitchScene;
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
        bool result = StateManagerInstance.SaveStateImpl(true, out _);
        return result;
    }
    public static bool LoadStateTas(string slot) {
        if (CannotSaveLoad()) {
            return false;
        }

        SwitchSlot(slot);
        bool result = StateManagerInstance.LoadStateImpl(true, out _);
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