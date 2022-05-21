using System.Collections.Generic;
using Celeste.Mod.SpeedrunTool.Message;
using Celeste.Mod.SpeedrunTool.Other;
using Celeste.Mod.SpeedrunTool.RoomTimer;
using Microsoft.Xna.Framework.Input;
using HotkeyEnum = Celeste.Mod.SpeedrunTool.Other.Hotkey;

namespace Celeste.Mod.SpeedrunTool;

[SettingName(DialogIds.SpeedrunTool)]
public class SpeedrunToolSettings : EverestModuleSettings {
    public static SpeedrunToolSettings Instance { get; private set; }

    public SpeedrunToolSettings() {
        Instance = this;
    }

    [SettingName(DialogIds.Enabled)] public bool Enabled { get; set; } = true;

    #region RoomTimer

    public RoomTimerType RoomTimerType { get; set; } = RoomTimerType.Off;

    [SettingRange(1, 99)]
    [SettingName(DialogIds.NumberOfRooms)]
    public int NumberOfRooms { get; set; } = 1;

    public EndPoint.SpriteStyle EndPointStyle { get; set; } = EndPoint.SpriteStyle.Flag;

    [SettingName(DialogIds.RoomTimerIgnoreFlag)]
    public bool RoomTimerIgnoreFlag { get; set; } = false;

    [SettingName(DialogIds.AutoTurnOffRoomTimer)]
    public bool AutoResetRoomTimer { get; set; } = true;

    #endregion

    #region State

    [SettingName(DialogIds.AutoLoadStateAfterDeath)]
    public bool AutoLoadStateAfterDeath { get; set; } = true;

    [SettingName(DialogIds.AutoClearStateOnScreenTransition)]
    public bool AutoClearStateOnScreenTransition { get; set; } = false;

    [SettingName(DialogIds.FreezeAfterLoadState)]
    public FreezeAfterLoadStateType FreezeAfterLoadStateType { get; set; } = FreezeAfterLoadStateType.On;

    [SettingName(DialogIds.SaveTimeAndDeaths)]
    public bool SaveTimeAndDeaths { get; set; } = false;

    [SettingName(DialogIds.SaveExtendedVariants)]
    public bool SaveExtendedVariants { get; set; } = true;

    #endregion

    #region DeathStatistics

    [SettingName(DialogIds.DeathStatistics)]
    public bool DeathStatistics { get; set; } = false;

    [SettingRange(1, 9)]
    [SettingName(DialogIds.MaxNumberOfDeathData)]
    public int MaxNumberOfDeathData { get; set; } = 2;

    #endregion

    #region MoreOptions

    public TeleportRoomCategory TeleportRoomCategory { get; set; } = TeleportRoomCategory.Any;
    [SettingRange(1, 9)] public int RespawnSpeed { get; set; } = 1;
    [SettingRange(1, 9)] public int RestartChapterSpeed { get; set; } = 1;
    public bool SkipRestartChapterScreenWipe { get; set; } = false;
    public bool AllowPauseDuringDeath { get; set; } = false;
    public bool MuteInBackground { get; set; }
    public PopupMessageStyle PopupMessageStyle { get; set; } = PopupMessageStyle.Tooltip;
    public bool Hotkeys { get; set; } = true;

    #endregion

    #region HotkeyConfig

    [SettingIgnore] public List<Keys> KeyboardToggleHotkeys { get; set; } = Hotkey.ToggleHotkeys.GetDefaultKeys();
    [SettingIgnore] public List<Keys> KeyboardSaveState { get; set; } = Hotkey.SaveState.GetDefaultKeys();
    [SettingIgnore] public List<Keys> KeyboardLoadState { get; set; } = Hotkey.LoadState.GetDefaultKeys();
    [SettingIgnore] public List<Keys> KeyboardClearState { get; set; } = Hotkey.ClearState.GetDefaultKeys();
    [SettingIgnore] public List<Keys> KeyboardOpenDebugMap { get; set; } = Hotkey.OpenDebugMap.GetDefaultKeys();
    [SettingIgnore] public List<Keys> KeyboardResetRoomTimerPb { get; set; } = Hotkey.ResetRoomTimerPb.GetDefaultKeys();
    [SettingIgnore] public List<Keys> KeyboardSwitchRoomTimer { get; set; } = Hotkey.SwitchRoomTimer.GetDefaultKeys();
    [SettingIgnore] public List<Keys> KeyboardIncreaseTimedRooms { get; set; } = Hotkey.IncreaseTimedRooms.GetDefaultKeys();
    [SettingIgnore] public List<Keys> KeyboardDecreaseTimedRooms { get; set; } = Hotkey.DecreaseTimedRooms.GetDefaultKeys();
    [SettingIgnore] public List<Keys> KeyboardSetEndPoint { get; set; } = Hotkey.SetEndPoint.GetDefaultKeys();
    [SettingIgnore] public List<Keys> KeyboardSetAdditionalEndPoint { get; set; } = Hotkey.SetAdditionalEndPoint.GetDefaultKeys();
    [SettingIgnore] public List<Keys> KeyboardCheckDeathStatistics { get; set; } = Hotkey.CheckDeathStatistics.GetDefaultKeys();
    [SettingIgnore] public List<Keys> KeyboardTeleportToPreviousRoom { get; set; } = Hotkey.TeleportToPreviousRoom.GetDefaultKeys();
    [SettingIgnore] public List<Keys> KeyboardTeleportToNextRoom { get; set; } = Hotkey.TeleportToNextRoom.GetDefaultKeys();
    [SettingIgnore] public List<Keys> KeyboardSwitchAutoLoadState { get; set; } = Hotkey.SwitchAutoLoadState.GetDefaultKeys();
    [SettingIgnore] public List<Keys> KeyboardSpawnTowerViewer { get; set; } = Hotkey.SpawnTowerViewer.GetDefaultKeys();
    [SettingIgnore] public List<Keys> KeyboardToggleFullscreen { get; set; } = Hotkey.ToggleFullscreen.GetDefaultKeys();

    [SettingIgnore] public List<Buttons> ControllerToggleHotkeys { get; set; } = new List<Buttons>();
    [SettingIgnore] public List<Buttons> ControllerSaveState { get; set; } = new List<Buttons>();
    [SettingIgnore] public List<Buttons> ControllerLoadState { get; set; } = new List<Buttons>();
    [SettingIgnore] public List<Buttons> ControllerClearState { get; set; } = new List<Buttons>();
    [SettingIgnore] public List<Buttons> ControllerOpenDebugMap { get; set; } = new List<Buttons>();
    [SettingIgnore] public List<Buttons> ControllerResetRoomTimerPb { get; set; } = new List<Buttons>();
    [SettingIgnore] public List<Buttons> ControllerSwitchRoomTimer { get; set; } = new List<Buttons>();
    [SettingIgnore] public List<Buttons> ControllerIncreaseTimedRooms { get; set; } = new List<Buttons>();
    [SettingIgnore] public List<Buttons> ControllerDecreaseTimedRooms { get; set; } = new List<Buttons>();
    [SettingIgnore] public List<Buttons> ControllerSetEndPoint { get; set; } = new List<Buttons>();
    [SettingIgnore] public List<Buttons> ControllerSetAdditionalEndPoint { get; set; } = new List<Buttons>();
    [SettingIgnore] public List<Buttons> ControllerCheckDeathStatistics { get; set; } = new List<Buttons>();
    [SettingIgnore] public List<Buttons> ControllerTeleportToPreviousRoom { get; set; } = new List<Buttons>();
    [SettingIgnore] public List<Buttons> ControllerTeleportToNextRoom { get; set; } = new List<Buttons>();
    [SettingIgnore] public List<Buttons> ControllerSwitchAutoLoadState { get; set; } = new List<Buttons>();
    [SettingIgnore] public List<Buttons> ControllerSpawnTowerViewer { get; set; } = new List<Buttons>();
    [SettingIgnore] public List<Buttons> ControllerToggleFullscreen { get; set; } = new List<Buttons>();

    #endregion HotkeyConfig
}

public enum FreezeAfterLoadStateType {
    Off, On, IgnoreHoldingKeys
}

public enum TeleportRoomCategory {
    Default, Any
}