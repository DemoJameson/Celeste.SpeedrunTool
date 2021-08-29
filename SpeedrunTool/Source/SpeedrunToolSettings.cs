using System.Collections.Generic;
using System.Linq;
using Celeste.Mod.SpeedrunTool.RoomTimer;
using Microsoft.Xna.Framework.Input;
using YamlDotNet.Serialization;
using static Celeste.Mod.SpeedrunTool.Other.ButtonConfigUi;

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

        #region ButtonConfig

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

        [SettingIgnore] public List<Keys> KeyboardSaveState { get; set; } = GetButtonInfo(Mappings.SaveState).DefaultKeys.ToList();

        [SettingIgnore] public List<Keys> KeyboardLoadState { get; set; } = GetButtonInfo(Mappings.LoadState).DefaultKeys.ToList();

        [SettingIgnore] public List<Keys> KeyboardClearState { get; set; } = GetButtonInfo(Mappings.ClearState).DefaultKeys.ToList();

        [SettingIgnore]
        public List<Keys> KeyboardOpenDebugMap { get; set; } =
            GetButtonInfo(Mappings.OpenDebugMap).DefaultKeys.ToList();

        [SettingIgnore] public List<Keys> KeyboardResetRoomTimerPb { get; set; } = GetButtonInfo(Mappings.ResetRoomTimerPb).DefaultKeys.ToList();

        [SettingIgnore]
        public List<Keys> KeyboardSwitchRoomTimer { get; set; } =
            GetButtonInfo(Mappings.SwitchRoomTimer).DefaultKeys.ToList();

        [SettingIgnore] public List<Keys> KeyboardSetEndPoint { get; set; } = GetButtonInfo(Mappings.SetEndPoint).DefaultKeys.ToList();

        [SettingIgnore]
        public List<Keys> KeyboardSetAdditionalEndPoint { get; set; } = GetButtonInfo(Mappings.SetAdditionalEndPoint).DefaultKeys.ToList();

        [SettingIgnore]
        public List<Keys> KeyboardCheckDeathStatistics { get; set; } =
            GetButtonInfo(Mappings.CheckDeathStatistics).DefaultKeys.ToList();

        [SettingIgnore] public List<Keys> KeyboardTeleportToLastRoom { get; set; } = GetButtonInfo(Mappings.TeleportToLastRoom).DefaultKeys.ToList();

        [SettingIgnore] public List<Keys> KeyboardTeleportToNextRoom { get; set; } = GetButtonInfo(Mappings.TeleportToNextRoom).DefaultKeys.ToList();
        
        [SettingIgnore]
        public List<Keys> KeyboardSwitchAutoLoadState { get; set; } = GetButtonInfo(Mappings.SwitchAutoLoadState).DefaultKeys.ToList();
        
        [SettingIgnore]
        public List<Keys> KeyboardToggleFullscreen { get; set; } = GetButtonInfo(Mappings.ToggleFullscreen).DefaultKeys.ToList();

        #endregion ButtonConfig
    }
}