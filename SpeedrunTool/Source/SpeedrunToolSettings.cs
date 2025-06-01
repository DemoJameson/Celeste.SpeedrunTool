using Celeste.Mod.SpeedrunTool.Message;
using Celeste.Mod.SpeedrunTool.Other;
using Celeste.Mod.SpeedrunTool.RoomTimer;
using Microsoft.Xna.Framework.Input;
using System.Collections.Generic;

namespace Celeste.Mod.SpeedrunTool;

[SettingName(DialogIds.SpeedrunTool)]
public class SpeedrunToolSettings : EverestModuleSettings {
    public static SpeedrunToolSettings Instance { get; private set; }

    public SpeedrunToolSettings() {
        Instance = this;
    }

    public bool Enabled { get; set; } = true;

    #region RoomTimer

    public RoomTimerType RoomTimerType { get; set; } = RoomTimerType.Off;
    public int NumberOfRooms { get; set; } = 1;
    public EndPoint.SpriteStyle EndPointStyle { get; set; } = EndPoint.SpriteStyle.Flag;
    public bool TimeSummitFlag { get; set; } = true;
    public bool TimeHeartCassette { get; set; } = true;
    public bool AutoResetRoomTimer { get; set; } = true;
    public bool DisplayRoomGold { get; set; } = true;
    public RoomTimerExportType RoomTimerExportType { get; set; } = RoomTimerExportType.Clipboard;

    #endregion

    #region State

    public bool AutoLoadStateAfterDeath { get; set; } = false;
    public bool AutoClearStateOnScreenTransition { get; set; } = false;
    public FreezeAfterLoadStateType FreezeAfterLoadStateType { get; set; } = FreezeAfterLoadStateType.On;
    public bool NoGcAfterLoadState { get; set; } = false;
    public bool SaveTimeAndDeaths { get; set; } = false;
    public bool SaveExtendedVariants { get; set; } = true;

    public bool NoMessageAfterSaveLoad { get; set; } = false;

    #endregion

    #region DeathStatistics

    public bool DeathStatistics { get; set; } = false;
    public int MaxNumberOfDeathData { get; set; } = 2;

    #endregion

    #region MoreOptions

    public TeleportRoomCategory TeleportRoomCategory { get; set; } = TeleportRoomCategory.Any;
    public int RespawnSpeed { get; set; } = 1;
    public int RestartChapterSpeed { get; set; } = 1;
    public bool SkipRestartChapterScreenWipe { get; set; } = false;
    public bool AllowPauseDuringDeath { get; set; } = false;
    public bool MuteInBackground { get; set; } = false;
    public bool FixCoreRefillDashAfterTeleport { get; set; } = true;
    public PopupMessageStyle PopupMessageStyle { get; set; } = PopupMessageStyle.Tooltip;
    public SpeedrunType AreaCompleteEnableTimerType { get; set; } = SpeedrunType.Off;
    public bool Hotkeys { get; set; } = true;
    public bool UnlockCamera { get; set; } = true;

    #endregion

    #region HotkeyConfig

    public List<Keys> KeyboardToggleHotkeys { get; set; } = Hotkey.ToggleHotkeys.GetDefaultKeys();
    public List<Keys> KeyboardSaveState { get; set; } = Hotkey.SaveState.GetDefaultKeys();
    public List<Keys> KeyboardLoadState { get; set; } = Hotkey.LoadState.GetDefaultKeys();
    public List<Keys> KeyboardClearState { get; set; } = Hotkey.ClearState.GetDefaultKeys();
    public List<Keys> KeyboardClearAllState { get; set; } = Hotkey.ClearAllState.GetDefaultKeys();
    public List<Keys> KeyboardOpenDebugMap { get; set; } = Hotkey.OpenDebugMap.GetDefaultKeys();
    public List<Keys> KeyboardResetRoomTimerPb { get; set; } = Hotkey.ResetRoomTimerPb.GetDefaultKeys();
    public List<Keys> KeyboardSwitchRoomTimer { get; set; } = Hotkey.SwitchRoomTimer.GetDefaultKeys();
    public List<Keys> KeyboardIncreaseTimedRooms { get; set; } = Hotkey.IncreaseTimedRooms.GetDefaultKeys();
    public List<Keys> KeyboardDecreaseTimedRooms { get; set; } = Hotkey.DecreaseTimedRooms.GetDefaultKeys();
    public List<Keys> KeyboardSetEndPoint { get; set; } = Hotkey.SetEndPoint.GetDefaultKeys();
    public List<Keys> KeyboardSetAdditionalEndPoint { get; set; } = Hotkey.SetAdditionalEndPoint.GetDefaultKeys();
    public List<Keys> KeyboardCheckDeathStatistics { get; set; } = Hotkey.CheckDeathStatistics.GetDefaultKeys();
    public List<Keys> KeyboardTeleportToPreviousRoom { get; set; } = Hotkey.TeleportToPreviousRoom.GetDefaultKeys();
    public List<Keys> KeyboardTeleportToNextRoom { get; set; } = Hotkey.TeleportToNextRoom.GetDefaultKeys();
    public List<Keys> KeyboardSwitchAutoLoadState { get; set; } = Hotkey.SwitchAutoLoadState.GetDefaultKeys();
    public List<Keys> KeyboardSpawnTowerViewer { get; set; } = Hotkey.SpawnTowerViewer.GetDefaultKeys();
    public List<Keys> KeyboardToggleFullscreen { get; set; } = Hotkey.ToggleFullscreen.GetDefaultKeys();
    public List<Keys> KeyboardExportRoomTimes { get; set; } = Hotkey.ExportRoomTimes.GetDefaultKeys();
    public List<Keys> KeyboardSaveSlot1 { get; set; } = Hotkey.SaveSlot1.GetDefaultKeys();
    public List<Keys> KeyboardSaveSlot2 { get; set; } = Hotkey.SaveSlot2.GetDefaultKeys();
    public List<Keys> KeyboardSaveSlot3 { get; set; } = Hotkey.SaveSlot3.GetDefaultKeys();
    public List<Keys> KeyboardSaveSlot4 { get; set; } = Hotkey.SaveSlot4.GetDefaultKeys();
    public List<Keys> KeyboardSaveSlot5 { get; set; } = Hotkey.SaveSlot5.GetDefaultKeys();
    public List<Keys> KeyboardSaveSlot6 { get; set; } = Hotkey.SaveSlot6.GetDefaultKeys();
    public List<Keys> KeyboardSaveSlot7 { get; set; } = Hotkey.SaveSlot7.GetDefaultKeys();
    public List<Keys> KeyboardSaveSlot8 { get; set; } = Hotkey.SaveSlot8.GetDefaultKeys();

    public List<Keys> KeyboardSaveSlot9 { get; set; } = Hotkey.SaveSlot9.GetDefaultKeys();

    public List<Keys> KeyboardSwitchToNextSlot { get; set; } = Hotkey.SwitchToNextSlot.GetDefaultKeys();

    public List<Keys> KeyboardSwitchToPreviousSlot { get; set; } = Hotkey.SwitchToPreviousSlot.GetDefaultKeys();

    public List<Keys> KeyboardSaveToNextSlot { get; set; } = Hotkey.SaveToNextSlot.GetDefaultKeys();

    public List<Keys> KeyboardLoadFromLastSlot { get; set; } = Hotkey.LoadFromLastSlot.GetDefaultKeys();

    public List<Buttons> ControllerToggleHotkeys { get; set; } = new List<Buttons>();
    public List<Buttons> ControllerSaveState { get; set; } = new List<Buttons>();
    public List<Buttons> ControllerLoadState { get; set; } = new List<Buttons>();
    public List<Buttons> ControllerClearState { get; set; } = new List<Buttons>();
    public List<Buttons> ControllerClearAllState { get; set; } = new List<Buttons>();
    public List<Buttons> ControllerOpenDebugMap { get; set; } = new List<Buttons>();
    public List<Buttons> ControllerResetRoomTimerPb { get; set; } = new List<Buttons>();
    public List<Buttons> ControllerSwitchRoomTimer { get; set; } = new List<Buttons>();
    public List<Buttons> ControllerIncreaseTimedRooms { get; set; } = new List<Buttons>();
    public List<Buttons> ControllerDecreaseTimedRooms { get; set; } = new List<Buttons>();
    public List<Buttons> ControllerSetEndPoint { get; set; } = new List<Buttons>();
    public List<Buttons> ControllerSetAdditionalEndPoint { get; set; } = new List<Buttons>();
    public List<Buttons> ControllerSetRoomIdEndPoint { get; set; } = new List<Buttons>();
    public List<Buttons> ControllerClearRoomIdEndPoint { get; set; } = new List<Buttons>();
    public List<Buttons> ControllerCheckDeathStatistics { get; set; } = new List<Buttons>();
    public List<Buttons> ControllerTeleportToPreviousRoom { get; set; } = new List<Buttons>();
    public List<Buttons> ControllerTeleportToNextRoom { get; set; } = new List<Buttons>();
    public List<Buttons> ControllerSwitchAutoLoadState { get; set; } = new List<Buttons>();
    public List<Buttons> ControllerSpawnTowerViewer { get; set; } = new List<Buttons>();
    public List<Buttons> ControllerToggleFullscreen { get; set; } = new List<Buttons>();
    public List<Buttons> ControllerExportRoomTimes { get; set; } = new List<Buttons>();
    public List<Buttons> ControllerSaveSlot1 { get; set; } = new List<Buttons>();

    public List<Buttons> ControllerSaveSlot2 { get; set; } = new List<Buttons>();

    public List<Buttons> ControllerSaveSlot3 { get; set; } = new List<Buttons>();

    public List<Buttons> ControllerSaveSlot4 { get; set; } = new List<Buttons>();

    public List<Buttons> ControllerSaveSlot5 { get; set; } = new List<Buttons>();

    public List<Buttons> ControllerSaveSlot6 { get; set; } = new List<Buttons>();

    public List<Buttons> ControllerSaveSlot7 { get; set; } = new List<Buttons>();

    public List<Buttons> ControllerSaveSlot8 { get; set; } = new List<Buttons>();

    public List<Buttons> ControllerSaveSlot9 { get; set; } = new List<Buttons>();

    public List<Buttons> ControllerSwitchToNextSlot { get; set; } = new List<Buttons>();

    public List<Buttons> ControllerSwitchToPreviousSlot { get; set; } = new List<Buttons>();

    public List<Buttons> ControllerSaveToNextSlot { get; set; } = new List<Buttons>();

    public List<Buttons> ControllerLoadFromLastSlot { get; set; } = new List<Buttons>();


    #endregion HotkeyConfig
}

public enum FreezeAfterLoadStateType {
    Off,
    On,
    IgnoreHoldingKeys
}

public enum TeleportRoomCategory {
    Default,
    Any
}
