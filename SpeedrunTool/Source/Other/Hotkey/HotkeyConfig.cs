using Microsoft.Xna.Framework.Input;
using System.Collections.Generic;
using System.Linq;

namespace Celeste.Mod.SpeedrunTool.Other;

public enum Hotkey {
    ToggleHotkeys,
    SaveState,
    LoadState,
    ClearState,
    OpenDebugMap,
    ResetRoomTimerPb,
    SwitchRoomTimer,
    IncreaseTimedRooms,
    DecreaseTimedRooms,
    SetEndPoint,
    SetAdditionalEndPoint,
    CheckDeathStatistics,
    TeleportToPreviousRoom,
    TeleportToNextRoom,
    ToggleFreezeMode,
    SwitchAutoLoadState,
    SpawnTowerViewer,
    ToggleFullscreen,
    ExportRoomTimes,
    SaveSlot1,
    SaveSlot2,
    SaveSlot3,
    SaveSlot4,
    SaveSlot5,
    SaveSlot6,
    SaveSlot7,
    SaveSlot8,
    SaveSlot9,
    ClearAllState,
    SaveToNextSlot,
    LoadFromLastSlot,
    SwitchToNextSlot,
    SwitchToPreviousSlot,
    ToggleSaveLoadUI
}

public static class HotkeyRegistry {
    // this determines hotkey's order in settings
    public static readonly Dictionary<Hotkey, HotkeyConfig> All = new List<HotkeyConfig> {
        new(Hotkey.ToggleHotkeys),
        new(Hotkey.SaveState, Keys.F7),
        new(Hotkey.LoadState, Keys.F8),
        new(Hotkey.ClearState, Keys.F4),
        new(Hotkey.ClearAllState),
        new(Hotkey.OpenDebugMap),
        new(Hotkey.ToggleSaveLoadUI, Keys.Tab),
        new(Hotkey.ResetRoomTimerPb, Keys.F9),
        new(Hotkey.SwitchRoomTimer, Keys.F10),
        new(Hotkey.IncreaseTimedRooms),
        new(Hotkey.DecreaseTimedRooms),
        new(Hotkey.SetEndPoint, Keys.F11),
        new(Hotkey.SetAdditionalEndPoint),
        new(Hotkey.CheckDeathStatistics),
        new(Hotkey.TeleportToPreviousRoom, Keys.PageUp),
        new(Hotkey.TeleportToNextRoom, Keys.PageDown),
        new(Hotkey.SwitchAutoLoadState),
        new(Hotkey.ToggleFreezeMode),
        new(Hotkey.SpawnTowerViewer),
        new(Hotkey.ToggleFullscreen),
        new(Hotkey.ExportRoomTimes),
        new(Hotkey.SwitchToNextSlot),
        new(Hotkey.SwitchToPreviousSlot),
        new(Hotkey.SaveToNextSlot),
        new(Hotkey.LoadFromLastSlot),
        new(Hotkey.SaveSlot1),
        new(Hotkey.SaveSlot2),
        new(Hotkey.SaveSlot3),
        new(Hotkey.SaveSlot4),
        new(Hotkey.SaveSlot5),
        new(Hotkey.SaveSlot6),
        new(Hotkey.SaveSlot7),
        new(Hotkey.SaveSlot8),
        new(Hotkey.SaveSlot9),
    }.ToDictionary(info => info.Hotkey, info => info);
}

public class HotkeyConfig {
    public readonly Keys[] DefaultKeys;
    public readonly Hotkey Hotkey;
    internal HotkeyDriver.HotkeyBinding Binding;
    public Action<Scene> OnPressed;

    public HotkeyConfig(Hotkey hotkey, params Keys[] defaultKeys) {
        Hotkey = hotkey;
        DefaultKeys = defaultKeys;
    }

    public void UpdateVirtualButton() {
        Binding = HotkeyDriver.CreateBinding(GetKeys(), GetButtons());
    }

    public List<Buttons> GetButtons() {
        List<Buttons> result = (List<Buttons>)ModSettings.GetPropertyValue($"Controller{Hotkey}");
        if (result == null) {
            result = new List<Buttons>();
            SetButtons(result);
        }

        return result;
    }

    public void SetButtons(List<Buttons> buttons) {
        ModSettings.SetPropertyValue($"Controller{Hotkey}", buttons);
    }

    public List<Keys> GetKeys() {
        List<Keys> result = (List<Keys>)ModSettings.GetPropertyValue($"Keyboard{Hotkey}");
        if (result == null) {
            result = [];
            SetKeys(result);
        }

        return result;
    }

    public void SetKeys(List<Keys> keys) {
        ModSettings.SetPropertyValue($"Keyboard{Hotkey}", keys);
    }

    public string GetLabel() {
        if (typeof(DialogIds).GetFieldValue(Hotkey.ToString()) is string label) {
            return label.DialogClean();
        }
        else {
            return $"DialogIds.{Hotkey} not found";
        }
    }
}

internal static class HotkeyExtensions {
    public static HotkeyConfig GetHotkeyConfig(this Hotkey hotkey) {
        return HotkeyRegistry.All[hotkey];
    }

    public static void RegisterPressedAction(this Hotkey hotkey, Action<Scene> onPressed) {
        hotkey.GetHotkeyConfig().OnPressed = onPressed;
    }

    public static List<Keys> GetDefaultKeys(this Hotkey hotkey) {
        return hotkey.GetHotkeyConfig().DefaultKeys.ToList();
    }

    // 检查键盘或者手柄全部按键按下
    public static bool Pressed(this Hotkey hotkey) {
        HotkeyConfig hotkeyConfig = hotkey.GetHotkeyConfig();
        return hotkeyConfig.Binding.Pressed;
    }
}
