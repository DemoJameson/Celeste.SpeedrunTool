using System.Collections.Generic;
using Celeste.Mod.SpeedrunTool.Message;
using Celeste.Mod.SpeedrunTool.Other;
using Celeste.Mod.SpeedrunTool.RoomTimer;
using Microsoft.Xna.Framework.Input;
using HotkeyEnum = Celeste.Mod.SpeedrunTool.Other.Hotkey;

namespace Celeste.Mod.SpeedrunTool {
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
        public bool FreezeAfterLoadState { get; set; } = true;

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

        [SettingRange(1, 9)] public int RespawnSpeed { get; set; } = 1;
        [SettingRange(1, 9)] public int RestartChapterSpeed { get; set; } = 1;
        public bool SkipRestartChapterScreenWipe { get; set; } = false;
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
        [SettingIgnore] public List<Keys> KeyboardSetEndPoint { get; set; } = Hotkey.SetEndPoint.GetDefaultKeys();
        [SettingIgnore] public List<Keys> KeyboardSetAdditionalEndPoint { get; set; } = Hotkey.SetAdditionalEndPoint.GetDefaultKeys();
        [SettingIgnore] public List<Keys> KeyboardCheckDeathStatistics { get; set; } = Hotkey.CheckDeathStatistics.GetDefaultKeys();
        [SettingIgnore] public List<Keys> KeyboardTeleportToPreviousRoom { get; set; } = Hotkey.TeleportToPreviousRoom.GetDefaultKeys();
        [SettingIgnore] public List<Keys> KeyboardTeleportToNextRoom { get; set; } = Hotkey.TeleportToNextRoom.GetDefaultKeys();
        [SettingIgnore] public List<Keys> KeyboardSwitchAutoLoadState { get; set; } = Hotkey.SwitchAutoLoadState.GetDefaultKeys();
        [SettingIgnore] public List<Keys> KeyboardToggleFullscreen { get; set; } = Hotkey.ToggleFullscreen.GetDefaultKeys();
        [SettingIgnore] public List<Keys> KeyboardIncreaseTimedRooms { get; set; } = Hotkey.IncreaseTimedRooms.GetDefaultKeys();
        [SettingIgnore] public List<Keys> KeyboardDecreaseTimedRooms { get; set; } = Hotkey.DecreaseTimedRooms.GetDefaultKeys();

        [SettingIgnore] public Buttons? ControllerToggleHotkeys { get; set; }
        [SettingIgnore] public Buttons? ControllerSaveState { get; set; }
        [SettingIgnore] public Buttons? ControllerLoadState { get; set; }
        [SettingIgnore] public Buttons? ControllerClearState { get; set; }
        [SettingIgnore] public Buttons? ControllerOpenDebugMap { get; set; }
        [SettingIgnore] public Buttons? ControllerResetRoomTimerPb { get; set; }
        [SettingIgnore] public Buttons? ControllerSwitchRoomTimer { get; set; }
        [SettingIgnore] public Buttons? ControllerSetEndPoint { get; set; }
        [SettingIgnore] public Buttons? ControllerSetAdditionalEndPoint { get; set; }
        [SettingIgnore] public Buttons? ControllerCheckDeathStatistics { get; set; }
        [SettingIgnore] public Buttons? ControllerTeleportToPreviousRoom { get; set; }
        [SettingIgnore] public Buttons? ControllerTeleportToNextRoom { get; set; }
        [SettingIgnore] public Buttons? ControllerSwitchAutoLoadState { get; set; }
        [SettingIgnore] public Buttons? ControllerToggleFullscreen { get; set; }

        #endregion HotkeyConfig
    }
}