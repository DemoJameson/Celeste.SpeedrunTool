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

        [YamlIgnore] [SettingIgnore] public RoomTimerType RoomTimerType { get; set; } = RoomTimerType.Off;

        [SettingRange(1, 99)]
        [SettingName(DialogIds.NumberOfRooms)]
        public int NumberOfRooms { get; set; } = 1;

        [YamlIgnore] [SettingIgnore] public EndPoint.SpriteStyle EndPointStyle { get; set; } = EndPoint.SpriteStyle.Flag;

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

        #endregion

        #region ButtonConfig

        [SettingIgnore] public Buttons? ControllerQuickSave { get; set; }
        [SettingIgnore] public Buttons? ControllerQuickLoad { get; set; }
        [SettingIgnore] public Buttons? ControllerQuickClear { get; set; }
        [SettingIgnore] public Buttons? ControllerOpenDebugMap { get; set; }
        [SettingIgnore] public Buttons? ControllerResetRoomPb { get; set; }
        [SettingIgnore] public Buttons? ControllerSwitchRoomTimer { get; set; }
        [SettingIgnore] public Buttons? ControllerSetEndPoint { get; set; }
        [SettingIgnore] public Buttons? ControllerSetAdditionalEndPoint { get; set; }
        [SettingIgnore] public Buttons? ControllerCheckDeathStatistics { get; set; }
        [SettingIgnore] public Buttons? ControllerLastRoom { get; set; }
        [SettingIgnore] public Buttons? ControllerNextRoom { get; set; }
        [SettingIgnore] public Buttons? ControllerAutoLoadStateAfterDeath { get; set; }
        [SettingIgnore] public Buttons? ControllerToggleFullscreen { get; set; }

        [SettingIgnore] public List<Keys> KeyboardQuickSave { get; set; } = GetButtonInfo(Mappings.Save).DefaultKeys.ToList();

        [SettingIgnore] public List<Keys> KeyboardQuickLoad { get; set; } = GetButtonInfo(Mappings.Load).DefaultKeys.ToList();

        [SettingIgnore] public List<Keys> KeyboardQuickClear { get; set; } = GetButtonInfo(Mappings.Clear).DefaultKeys.ToList();

        [SettingIgnore]
        public List<Keys> KeyboardOpenDebugMap { get; set; } =
            GetButtonInfo(Mappings.OpenDebugMap).DefaultKeys.ToList();

        [SettingIgnore] public List<Keys> KeyboardResetRoomPb { get; set; } = GetButtonInfo(Mappings.ResetRoomPb).DefaultKeys.ToList();

        [SettingIgnore]
        public List<Keys> KeyboardSwitchRoomTimer { get; set; } =
            GetButtonInfo(Mappings.SwitchRoomTimer).DefaultKeys.ToList();

        [SettingIgnore] public List<Keys> KeyboardSetEndPoint { get; set; } = GetButtonInfo(Mappings.SetEndPoint).DefaultKeys.ToList();

        [SettingIgnore]
        public List<Keys> KeyboardSetAdditionalEndPoint { get; set; } = GetButtonInfo(Mappings.SetAdditionalEndPoint).DefaultKeys.ToList();

        [SettingIgnore]
        public List<Keys> KeyboardCheckDeathStatistics { get; set; } =
            GetButtonInfo(Mappings.CheckDeathStatistics).DefaultKeys.ToList();

        [SettingIgnore] public List<Keys> KeyboardLastRoom { get; set; } = GetButtonInfo(Mappings.LastRoom).DefaultKeys.ToList();

        [SettingIgnore] public List<Keys> KeyboardNextRoom { get; set; } = GetButtonInfo(Mappings.NextRoom).DefaultKeys.ToList();
        
        [SettingIgnore]
        public List<Keys> KeyboardAutoLoadStateAfterDeath { get; set; } = GetButtonInfo(Mappings.SwitchAutoLoadState).DefaultKeys.ToList();
        
        [SettingIgnore]
        public List<Keys> KeyboardToggleFullscreen { get; set; } = GetButtonInfo(Mappings.ToggleFullscreen).DefaultKeys.ToList();

        #endregion ButtonConfig
    }
}