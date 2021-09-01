using System.Collections.Generic;
using Celeste.Mod.SpeedrunTool.Other;
using Celeste.Mod.SpeedrunTool.RoomTimer;
using Microsoft.Xna.Framework.Input;

namespace Celeste.Mod.SpeedrunTool {
    [SettingName(DialogIds.SpeedrunTool)]
    public class SpeedrunToolSettings : EverestModuleSettings {
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

        [SettingName(DialogIds.FreezeAfterLoadState)]
        public bool FreezeAfterLoadState { get; set; } = true;
        
        [SettingName(DialogIds.DoNotRestoreTimeAndDeaths)]
        public bool DoNotRestoreTimeAndDeaths { get; set; } = true;

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

        [SettingName(DialogIds.FastTeleport)]
        [SettingSubText(DialogIds.FastTeleportDescription)]
        public bool FastTeleport { get; set; } = true;
        public bool MuteInBackground { get; set; }

        #endregion

        #region HotkeyConfig

        [SettingIgnore] public Buttons? ControllerSaveState { get; set; }
        [SettingIgnore] public Buttons? ControllerLoadState { get; set; }
        [SettingIgnore] public Buttons? ControllerClearState { get; set; }
        [SettingIgnore] public Buttons? ControllerOpenDebugMap { get; set; }
        [SettingIgnore] public Buttons? ControllerResetRoomTimerPb { get; set; }
        [SettingIgnore] public Buttons? ControllerSwitchRoomTimer { get; set; }
        [SettingIgnore] public Buttons? ControllerSetEndPoint { get; set; }
        [SettingIgnore] public Buttons? ControllerSetAdditionalEndPoint { get; set; }
        [SettingIgnore] public Buttons? ControllerCheckDeathStatistics { get; set; }
        [SettingIgnore] public Buttons? ControllerTeleportToLastRoom { get; set; }
        [SettingIgnore] public Buttons? ControllerTeleportToNextRoom { get; set; }
        [SettingIgnore] public Buttons? ControllerSwitchAutoLoadState { get; set; }
        [SettingIgnore] public Buttons? ControllerToggleFullscreen { get; set; }

        [SettingIgnore] public List<Keys> KeyboardSaveState { get; set; } = Hotkeys.SaveState.GetDefaultKeys();

        [SettingIgnore] public List<Keys> KeyboardLoadState { get; set; } = Hotkeys.LoadState.GetDefaultKeys();

        [SettingIgnore] public List<Keys> KeyboardClearState { get; set; } = Hotkeys.ClearState.GetDefaultKeys();

        [SettingIgnore]
        public List<Keys> KeyboardOpenDebugMap { get; set; } = Hotkeys.OpenDebugMap.GetDefaultKeys();

        [SettingIgnore] public List<Keys> KeyboardResetRoomTimerPb { get; set; } = Hotkeys.ResetRoomTimerPb.GetDefaultKeys();

        [SettingIgnore]
        public List<Keys> KeyboardSwitchRoomTimer { get; set; } = Hotkeys.SwitchRoomTimer.GetDefaultKeys();

        [SettingIgnore] public List<Keys> KeyboardSetEndPoint { get; set; } = Hotkeys.SetEndPoint.GetDefaultKeys();

        [SettingIgnore]
        public List<Keys> KeyboardSetAdditionalEndPoint { get; set; } = Hotkeys.SetAdditionalEndPoint.GetDefaultKeys();

        [SettingIgnore]
        public List<Keys> KeyboardCheckDeathStatistics { get; set; } = Hotkeys.CheckDeathStatistics.GetDefaultKeys();

        [SettingIgnore] public List<Keys> KeyboardTeleportToLastRoom { get; set; } = Hotkeys.TeleportToLastRoom.GetDefaultKeys();

        [SettingIgnore] public List<Keys> KeyboardTeleportToNextRoom { get; set; } = Hotkeys.TeleportToNextRoom.GetDefaultKeys();
        
        [SettingIgnore]
        public List<Keys> KeyboardSwitchAutoLoadState { get; set; } = Hotkeys.SwitchAutoLoadState.GetDefaultKeys();
        
        [SettingIgnore]
        public List<Keys> KeyboardToggleFullscreen { get; set; } = Hotkeys.ToggleFullscreen.GetDefaultKeys();

        #endregion HotkeyConfig
    }
}