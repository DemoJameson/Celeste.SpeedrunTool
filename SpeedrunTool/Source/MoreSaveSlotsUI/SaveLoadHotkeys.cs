using Celeste.Mod.SpeedrunTool.Message;
using Celeste.Mod.SpeedrunTool.Other;
using Celeste.Mod.SpeedrunTool.SaveLoad;

namespace Celeste.Mod.SpeedrunTool.MoreSaveSlotsUI;
internal static class SaveLoadHotkeys {

    public static string SlotName => SaveSlotsManager.SlotName;

    public static bool SaveLoadStateShowMessage => !ModSettings.NoMessageAfterSaveLoad;
    // if false, make it behave like v3.24.5

    internal static bool SaveStateAndMessage() {
        if (SaveLoadStateShowMessage) {
            if (SaveSlotsManager.SaveState()) {
                PopupMessageUtils.Show($"Save to [{SlotName}]", null);
                return true;
            }
            else {
                if (!StateManager.AllowSaveLoadWhenWaiting && SaveSlotsManager.StateManagerInstance?.State == State.Waiting) {
                    PopupMessageUtils.Show($"[{SlotName}] is already Saved!", null);
                    return false;
                }
                else {
                    PopupMessageUtils.Show("Failed to Save: SpeedrunTool is Busy!", null);
                    return false;
                }
            }
        }
        else {
            return SaveSlotsManager.SaveState();
        }
    }

    internal static bool LoadStateAndMessage() {
        if (SaveSlotsManager.IsSaved()) {
            if (SaveLoadStateShowMessage) {
                if (SaveSlotsManager.LoadState()) {
                    PopupMessageUtils.Show($"Load from [{SlotName}]", null);
                    return true;
                }
                else {
                    PopupMessageUtils.Show("Failed to Load: SpeedrunTool is Busy!", null);
                    return false;
                }
            }
            else {
                return SaveSlotsManager.LoadState();
            }
        }
        else {
            PopupMessageUtils.Show(DialogIds.NotSavedStateTooltip.DialogClean() + $" [{SlotName}]", DialogIds.NotSavedStateYetDialog);
            return false;
        }
    }

    [Load]
    private static void RegisterHotkeys() {
        Hotkey.SaveState.RegisterPressedAction(scene => {
            if (scene is Level { Paused: false }) {
#if DEBUG
                if (JetBrains_Profiling) {
                    JetBrains.Profiler.Api.MeasureProfiler.StartCollectingData();
                    SaveStateAndMessage();
                    JetBrains.Profiler.Api.MeasureProfiler.SaveData();
                }
                else {
                    SaveStateAndMessage();
                }
#else
                SaveStateAndMessage();           
#endif
            }
        });
        Hotkey.LoadState.RegisterPressedAction(scene => {
            if (scene is Level { Paused: false }) {
#if DEBUG
                if (JetBrains_Profiling) {
                    JetBrains.Profiler.Api.MeasureProfiler.StartCollectingData();
                    LoadStateAndMessage();
                    JetBrains.Profiler.Api.MeasureProfiler.SaveData();
                }
                else {
                    LoadStateAndMessage();
                }
#else
                LoadStateAndMessage();
#endif
            }
        });
        Hotkey.ClearState.RegisterPressedAction(scene => {
            if (scene is Level { Paused: false } && SaveSlotsManager.IsAllFree()) {
                SaveSlotsManager.ClearState();
                PopupMessageUtils.Show(DialogIds.ClearStateToolTip.DialogClean() + $" [{SlotName}]", DialogIds.ClearStateDialog);
            }
        });

        Hotkey.ClearAllState.RegisterPressedAction(scene => {
            if (scene is Level { Paused: false } && SaveSlotsManager.IsAllFree()) {
                SaveSlotsManager.ClearAll();
                PopupMessageUtils.Show(DialogIds.ClearAllToolTip.DialogClean(), DialogIds.ClearAllDialog);
            }
        });

        Hotkey.SwitchAutoLoadState.RegisterPressedAction(scene => {
            if (scene is Level { Paused: false }) {
                ModSettings.AutoLoadStateAfterDeath = !ModSettings.AutoLoadStateAfterDeath;
                SpeedrunToolModule.Instance.SaveSettings();
                string state = (ModSettings.AutoLoadStateAfterDeath ? DialogIds.On : DialogIds.Off).DialogClean();
                PopupMessageUtils.ShowOptionState(DialogIds.AutoLoadStateAfterDeath.DialogClean(), state);
            }
        });

        Hotkey.SaveSlot1.RegisterPressedAction(_ => SwitchSlotAndShowMessage(1));
        Hotkey.SaveSlot2.RegisterPressedAction(_ => SwitchSlotAndShowMessage(2));
        Hotkey.SaveSlot3.RegisterPressedAction(_ => SwitchSlotAndShowMessage(3));
        Hotkey.SaveSlot4.RegisterPressedAction(_ => SwitchSlotAndShowMessage(4));
        Hotkey.SaveSlot5.RegisterPressedAction(_ => SwitchSlotAndShowMessage(5));
        Hotkey.SaveSlot6.RegisterPressedAction(_ => SwitchSlotAndShowMessage(6));
        Hotkey.SaveSlot7.RegisterPressedAction(_ => SwitchSlotAndShowMessage(7));
        Hotkey.SaveSlot8.RegisterPressedAction(_ => SwitchSlotAndShowMessage(8));
        Hotkey.SaveSlot9.RegisterPressedAction(_ => SwitchSlotAndShowMessage(9));
        Hotkey.SwitchToNextSlot.RegisterPressedAction(_ => SwitchSlotTowards(1));
        Hotkey.SwitchToPreviousSlot.RegisterPressedAction(_ => SwitchSlotTowards(-1));
    }

    private static void SwitchSlotAndShowMessage(int index) {
        if (SaveSlotsManager.SwitchSlot(index)) {
            PopupMessageUtils.Show($"Switch to [{SlotName}]", null);
        }
        else {
            PopupMessageUtils.Show($"Failed to switch to [{SlotName}]: SpeedrunTool is Busy!", null);
        }
    }

    private static void SwitchSlotTowards(int dir) {
        int index = PeriodicTableOfSlots.CurrentSlotIndex;
        if (index < 0) {
            index = 1;
        }
        else {
            index = PeriodicTableOfSlots.ModuloAdd(index, dir);
        }
        SwitchSlotAndShowMessage(index);
    }
}
