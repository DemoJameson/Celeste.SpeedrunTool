using Celeste.Mod.SpeedrunTool.Message;
using Celeste.Mod.SpeedrunTool.Other;
using Celeste.Mod.SpeedrunTool.SaveLoad;

namespace Celeste.Mod.SpeedrunTool.MoreSaveSlotsUI;
internal static class SwitchAndSaveLoad {

    private static int SlotsCount => PeriodicTableOfSlots.RegularSlotsCount;

    private enum SlotState { Any, Saved, NotSaved };

    private enum Results { Busy, Success, Fail };

    [Load]

    internal static void RegisterHotkeys() {
        Hotkey.SaveToNextSlot.RegisterPressedAction(scene => {
            if (scene is Level) {
#if DEBUG
                if (JetBrains_Profiling) {
                    JetBrains.Profiler.Api.MeasureProfiler.StartCollectingData();
                    SaveToNextAvailableSlot();
                    JetBrains.Profiler.Api.MeasureProfiler.SaveData();
                }
                else {
                    SaveToNextAvailableSlot();
                }
#else
                SaveToNextAvailableSlot();
#endif
            }
        });
        Hotkey.LoadFromLastSlot.RegisterPressedAction(scene => {
            if (scene is Level { Paused: false }) {
#if DEBUG
                if (JetBrains_Profiling) {
                    JetBrains.Profiler.Api.MeasureProfiler.StartCollectingData();
                    LoadFromLastAvailableSlot();
                    JetBrains.Profiler.Api.MeasureProfiler.SaveData();
                }
                else {
                    LoadFromLastAvailableSlot();
                }
#else
                LoadFromLastAvailableSlot();
#endif
            }
        });
    }

    // if state = any, then naive move left / right by 1 unit
    // if state = Saved, then move until a slot is saved (starting from next). If no such slot, then stick to original slot
    // if state = NotSaved, then move until a slot is empty (starting from current). If no such slot, then overwrite next slot of the original slot
    private static Results SwitchToNextAvailableSlot(int dir, SlotState state) {
        if (!SaveSlotsManager.IsAllFree()) {
            return Results.Busy;
        }

        int currentSlot = PeriodicTableOfSlots.CurrentSlotIndex;
        if (currentSlot < 0) {
            currentSlot = state == SlotState.Saved ? ModuloAdd(1, -dir) : 1;
        }

        int stepCount = 0;

        switch (state) {
            case SlotState.Any: {
                currentSlot = ModuloAdd(currentSlot, dir);
                return SaveSlotsManager.SwitchSlot(currentSlot) ? Results.Success : Results.Busy;
            }
            case SlotState.Saved: {
                do {
                    currentSlot = ModuloAdd(currentSlot, dir);
                    if (SaveSlotsManager.IsSaved(currentSlot)) {
                        return SaveSlotsManager.SwitchSlot(currentSlot) ? Results.Success : Results.Busy;
                    }
                    stepCount++;
                } while (stepCount < SlotsCount);
                return Results.Fail;
            }
            case SlotState.NotSaved: {
                do {
                    if (!SaveSlotsManager.IsSaved(currentSlot)) {
                        // find an empty slot
                        return SaveSlotsManager.SwitchSlot(currentSlot) ? Results.Success : Results.Busy;
                    }
                    currentSlot = ModuloAdd(currentSlot, dir);
                    stepCount++;
                } while (stepCount < SlotsCount);

                currentSlot = ModuloAdd(currentSlot, dir);
                // overwrite it
                return SaveSlotsManager.SwitchSlot(currentSlot) ? Results.Success : Results.Busy;
            }
            default:
                return Results.Busy;
        }
    }
    private static int ModuloAdd(int num, int dir) => PeriodicTableOfSlots.ModuloAdd(num, dir);

    private static void SaveToNextAvailableSlot() {
        bool allow = StateManager.AllowSaveLoadWhenWaiting;
        StateManager.AllowSaveLoadWhenWaiting = true;

        Results result = SwitchToNextAvailableSlot(1, SlotState.NotSaved);
        if (result == Results.Success) {
            SaveSlotsManager.SaveState(out string popup);
            PopupMessageUtils.Show(popup, null);
        }
        else {
            PopupMessageUtils.Show("Failed to Save: SpeedrunTool is Busy!", null);
        }

        StateManager.AllowSaveLoadWhenWaiting = allow;
    }

    private static void LoadFromLastAvailableSlot() {
        bool allow = StateManager.AllowSaveLoadWhenWaiting;
        StateManager.AllowSaveLoadWhenWaiting = true;

        Results result = SwitchToNextAvailableSlot(-1, SlotState.Saved);
        if (result == Results.Success) {
            SaveSlotsManager.LoadState(out string popup);
            PopupMessageUtils.Show(popup, null);
        }
        else if (result == Results.Busy) {
            PopupMessageUtils.Show("Failed to Load: SpeedrunTool is Busy!", null);
        }
        else if (result == Results.Fail) {
            if (PeriodicTableOfSlots.CurrentSlotIndex < 0) {
                SaveSlotsManager.SwitchSlot(1);
            }
            PopupMessageUtils.Show(DialogIds.NotSavedStateTooltip.DialogClean() + $" [{SlotName}]", null);
        }

        StateManager.AllowSaveLoadWhenWaiting = allow;
    }

    private static string SlotName => SaveSlotsManager.SlotName;
}
