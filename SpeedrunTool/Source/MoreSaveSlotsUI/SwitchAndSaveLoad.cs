using Celeste.Mod.SpeedrunTool.Message;
using Celeste.Mod.SpeedrunTool.Other;
using Celeste.Mod.SpeedrunTool.SaveLoad;
using System.Collections.Generic;

namespace Celeste.Mod.SpeedrunTool.MoreSaveSlotsUI;
internal static class SwitchAndSaveLoad {

    private static int currentSlot = 1;

    private static readonly Dictionary<string, int> ReverseDictionary = new Dictionary<string, int>();

    public static int SlotsCount = 10;

    private enum SlotState { Any, Saved, NotSaved };

    private enum Results { Busy, Success, Fail };

    [Load]

    internal static void RegisterHotkeys() {
        Hotkey.SaveToNextSlot.RegisterPressedAction(scene => {
            if (scene is Level) {
                SaveToNextAvailableSlot();
            }
        });
        Hotkey.LoadFromLastSlot.RegisterPressedAction(scene => {
            if (scene is Level { Paused: false }) {
                LoadFromLastAvailableSlot();
            }
        });
        for (int i = 1; i <= SlotsCount; i++) {
            ReverseDictionary.Add(SaveSlotsManager.GetSlotName(i), i);
        }
    }

    // if state = any, then naive move left / right by 1 unit
    // if state = Saved, then move until a slot is saved (starting from next). If no such slot, then stick to original slot
    // if state = NotSaved, then move until a slot is empty (starting from current). If no such slot, then overwrite next slot of the original slot
    private static Results SwitchToNextAvailableSlot(int dir, SlotState state) {
        if (!SaveSlotsManager.IsAllFree()) {
            return Results.Busy;
        }

        // slot may have been changed by other hotkeys
        if (ReverseDictionary.TryGetValue(SaveSlotsManager.SlotName, out int num)) {
            currentSlot = num;
        } else {
            // stick to last value, which should be a legal one
        }

        int stepCount = 0;
        int orig = currentSlot;

        switch (state) {
            case SlotState.Any: {
                currentSlot = ModuloAdd(currentSlot, dir);
                if (SaveSlotsManager.SwitchSlot(currentSlot)) {
                    return Results.Success;
                } else {
                    currentSlot = orig;
                    return Results.Busy;
                }
            }
            case SlotState.Saved: {
                do {
                    currentSlot = ModuloAdd(currentSlot, dir);
                    if (SaveSlotsManager.IsSaved(currentSlot)) {
                        if (SaveSlotsManager.SwitchSlot(currentSlot)) {
                            return Results.Success;
                        } else {
                            currentSlot = orig;
                            return Results.Busy;
                        }
                    }
                    stepCount++;
                } while (stepCount < SlotsCount);
                return Results.Fail;
            }
            case SlotState.NotSaved: {
                do {
                    if (!SaveSlotsManager.IsSaved(currentSlot)) {
                        if (SaveSlotsManager.SwitchSlot(currentSlot)) {
                            return Results.Success; // find an empty slot
                        } else {
                            currentSlot = orig;
                            return Results.Busy;
                        }
                    }
                    currentSlot = ModuloAdd(currentSlot, dir);
                    stepCount++;
                } while (stepCount < SlotsCount);

                currentSlot = ModuloAdd(currentSlot, dir);
                if (SaveSlotsManager.SwitchSlot(currentSlot)) {
                    return Results.Success; // overwrite it
                } else {
                    currentSlot = orig;
                    return Results.Busy;
                }
            }
            default:
                return Results.Busy;
        }
    }

    private static int ModuloAdd(int num, int dir) {
        // assume 1 <= a <= SlotsCount, and dir = plus minus 1
        if (dir == 1) {
            if (num < SlotsCount) {
                num++;
            } else {
                num = 1;
            }
        } else {
            if (num > 1) {
                num--;
            } else {
                num = SlotsCount;
            }
        }
        return num;
    }

    private static void SaveToNextAvailableSlot() {
        int orig = currentSlot;
        bool allow = StateManager.AllowSaveLoadWhenWaiting;
        StateManager.AllowSaveLoadWhenWaiting = true;

        Results result = SwitchToNextAvailableSlot(1, SlotState.NotSaved);
        if (result == Results.Success && SaveSlotsManager.SaveState()) {
            PopupMessageUtils.Show($"Save to {SaveSlotsManager.SlotName}", null);
            return;
        } else {
            currentSlot = orig;
            PopupMessageUtils.Show("Failed to Save: SpeedrunTool is Busy!", null);
        }

        StateManager.AllowSaveLoadWhenWaiting = allow;
    }

    private static void LoadFromLastAvailableSlot() {
        int orig = currentSlot;
        bool allow = StateManager.AllowSaveLoadWhenWaiting;
        StateManager.AllowSaveLoadWhenWaiting = true;

        Results result = SwitchToNextAvailableSlot(-1, SlotState.Saved);
        if (result == Results.Success) {
            if (SaveSlotsManager.LoadState()) {
                PopupMessageUtils.Show($"Load from {SaveSlotsManager.SlotName}", null);
            } else {
                result = Results.Busy;
            }
        }
        if (result == Results.Busy) {
            PopupMessageUtils.Show("Failed to Load: SpeedrunTool is Busy!", null);
            currentSlot = orig;
        } else if (result == Results.Fail) {
            PopupMessageUtils.Show("No saved states yet!", null);
            currentSlot = orig;
        }

        StateManager.AllowSaveLoadWhenWaiting = allow;
    }
}
