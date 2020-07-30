using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Celeste.Mod.SpeedrunTool.DeathStatistics;
using Celeste.Mod.SpeedrunTool.RoomTimer;
using Microsoft.Xna.Framework.Input;
using Monocle;
using YamlDotNet.Serialization;
using static Celeste.Mod.SpeedrunTool.ButtonConfigUi;

// ReSharper disable MemberCanBePrivate.Global
// ReSharper disable AutoPropertyCanBeMadeGetOnly.Global
namespace Celeste.Mod.SpeedrunTool {
    [SettingName(DialogIds.SpeedrunTool)]
    public class SpeedrunToolSettings : EverestModuleSettings {
        public static readonly List<string> RoomTimerStrings = GetEnumNames<RoomTimerType>();

        private static readonly List<string> EndPointStyleStrings = GetEnumNames<EndPoint.SpriteStyle>();

        [SettingName(DialogIds.Enabled)] public bool Enabled { get; set; } = true;

        [YamlIgnore] [SettingIgnore] public RoomTimerType RoomTimerType => GetEnumFromName<RoomTimerType>(RoomTimer);
        public string RoomTimer { get; set; } = RoomTimerStrings.First();

        [SettingRange(1, 99)]
        [SettingName(DialogIds.NumberOfRooms)]
        public int NumberOfRooms { get; set; } = 1;

        [SettingName(DialogIds.AutoLoadAfterDeath)]
        public bool AutoLoadAfterDeath { get; set; } = true;

        // -------------------------- More Option --------------------------
        // ReSharper disable once UnusedMember.Global
        [YamlIgnore] public string MoreOptions { get; set; } = "";

        public bool FastLoadState { get; set; } = false;

        public string EndPointStyle { get; set; } = EndPointStyleStrings.First();

        [YamlIgnore]
        [SettingIgnore]
        public EndPoint.SpriteStyle EndPointSpriteStyle => GetEnumFromName<EndPoint.SpriteStyle>(EndPointStyle);

        [SettingRange(1, 9)] public int RespawnSpeed { get; set; } = 1;

        [SettingName(DialogIds.DeathStatistics)]
        public bool DeathStatistics { get; set; } = false;

        [SettingRange(1, 9)]
        [SettingName(DialogIds.MaxNumberOfDeathData)]
        public int MaxNumberOfDeathData { get; set; } = 2;

        // ReSharper disable once UnusedMember.Global
        [YamlIgnore] public string CheckDeathStatistics { get; set; } = "";

        // ReSharper disable once UnusedMember.Global
        [YamlIgnore] public string ButtonConfig { get; set; } = "";

        #region ButtonConfig

        [SettingIgnore] public Buttons? ControllerQuickSave { get; set; }
        [SettingIgnore] public Buttons? ControllerQuickLoad { get; set; }
        [SettingIgnore] public Buttons? ControllerQuickClear { get; set; }
        [SettingIgnore] public Buttons? ControllerOpenDebugMap { get; set; }
        [SettingIgnore] public Buttons? ControllerResetRoomPb { get; set; }
        [SettingIgnore] public Buttons? ControllerSwitchRoomTimer { get; set; }
        [SettingIgnore] public Buttons? ControllerSetEndPoint { get; set; }
        [SettingIgnore] public Buttons? ControllerCheckDeathStatistics { get; set; }
        [SettingIgnore] public Buttons? ControllerLastRoom { get; set; }
        [SettingIgnore] public Buttons? ControllerNextRoom { get; set; }

        [SettingIgnore] public Buttons? ControllerAutoLoadStateAfterDeath { get; set; }

        [SettingIgnore]
        public List<Keys> KeyboardQuickSave { get; set; } = GetButtonInfo(Mappings.Save).DefaultKeys.ToList();

        [SettingIgnore]
        public List<Keys> KeyboardQuickLoad { get; set; } = GetButtonInfo(Mappings.Load).DefaultKeys.ToList();

        [SettingIgnore]
        public List<Keys> KeyboardQuickClear { get; set; } = GetButtonInfo(Mappings.Clear).DefaultKeys.ToList();

        [SettingIgnore]
        public List<Keys> KeyboardOpenDebugMap { get; set; } =
            GetButtonInfo(Mappings.OpenDebugMap).DefaultKeys.ToList();

        [SettingIgnore]
        public List<Keys> KeyboardResetRoomPb { get; set; } = GetButtonInfo(Mappings.ResetRoomPb).DefaultKeys.ToList();

        [SettingIgnore]
        public List<Keys> KeyboardSwitchRoomTimer { get; set; } =
            GetButtonInfo(Mappings.SwitchRoomTimer).DefaultKeys.ToList();

        [SettingIgnore]
        public List<Keys> KeyboardSetEndPoint { get; set; } = GetButtonInfo(Mappings.SetEndPoint).DefaultKeys.ToList();

        [SettingIgnore]
        public List<Keys> KeyboardCheckDeathStatistics { get; set; } =
            GetButtonInfo(Mappings.CheckDeathStatistics).DefaultKeys.ToList();

        [SettingIgnore]
        public List<Keys> KeyboardLastRoom { get; set; } = GetButtonInfo(Mappings.LastRoom).DefaultKeys.ToList();

        [SettingIgnore]
        public List<Keys> KeyboardNextRoom { get; set; } = GetButtonInfo(Mappings.NextRoom).DefaultKeys.ToList();

        [SettingIgnore]
        public List<Keys> KeyboardAutoLoadStateAfterDeath { get; set; } = GetButtonInfo(Mappings.SwitchAutoLoadState).DefaultKeys.ToList();

        #endregion ButtonConfig


        private TextMenu.Option<bool> firstTextMenu;
        private TextMenu.Item lastTextMenu;
        private TextMenu.Item moreOptionsTextMenu;

        // ReSharper disable once UnusedMember.Global
        public void CreateEnabledEntry(TextMenu textMenu, bool inGame) {
            firstTextMenu = new TextMenu.OnOff(DialogIds.Enabled.DialogClean(), Enabled);
            firstTextMenu.Change(enabled => {
                Enabled = enabled;
                if (enabled) {
                    bool isBeforeMoreOptionsItem = false;
                    foreach (TextMenu.Item item in textMenu.Items) {
                        if (isBeforeMoreOptionsItem) {
                            item.Visible = true;
                        }

                        if (firstTextMenu == item) {
                            isBeforeMoreOptionsItem = true;
                        }

                        if (moreOptionsTextMenu == item) {
                            isBeforeMoreOptionsItem = false;
                        }
                    }
                } else {
                    bool isSpeedrunToolItem = false;
                    foreach (TextMenu.Item item in textMenu.Items) {
                        if (isSpeedrunToolItem) {
                            item.Visible = false;
                        }

                        if (firstTextMenu == item) {
                            isSpeedrunToolItem = true;
                        }

                        if (lastTextMenu == item) {
                            isSpeedrunToolItem = false;
                        }
                    }
                }

                moreOptionsTextMenu.Visible = enabled;
            });
            textMenu.Add(firstTextMenu);
        }

        // ReSharper disable once UnusedMember.Global
        public void CreateMoreOptionsEntry(TextMenu textMenu, bool inGame) {
            textMenu.Add(moreOptionsTextMenu = new TextMenu.Button(Dialog.Clean(DialogIds.MoreOptions)).Pressed(() => {
                ToggleMoreOptionsMenuItem(textMenu, true);
                moreOptionsTextMenu.Visible = false;
                textMenu.Selection += 1;
            }));
        }

        private void ToggleMoreOptionsMenuItem(TextMenu textMenu, bool visible) {
            bool isAfterMoreOptionsTextMenu = false;
            foreach (TextMenu.Item item in textMenu.Items) {
                if (isAfterMoreOptionsTextMenu) {
                    item.Visible = visible;
                }

                if (moreOptionsTextMenu == item) {
                    isAfterMoreOptionsTextMenu = true;
                }

                if (lastTextMenu == item) {
                    isAfterMoreOptionsTextMenu = false;
                }
            }
        }

        // ReSharper disable once UnusedMember.Global
        public void CreateRespawnSpeedEntry(TextMenu textMenu, bool inGame) {
            textMenu.Add(
                new TextMenu.Slider(Dialog.Clean(DialogIds.RespawnSpeed),
                    value => value + "00%",
                    1,
                    9,
                    RespawnSpeed
                ).Change(value => RespawnSpeed = value));
        }

        // ReSharper disable once UnusedMember.Global
        public void CreateRoomTimerEntry(TextMenu textMenu, bool inGame) {
            textMenu.Add(
                new TextMenu.Slider(Dialog.Clean(DialogIds.RoomTimer),
                    index => Dialog.Clean(DialogIds.Prefix + RoomTimerStrings[index]),
                    0,
                    RoomTimerStrings.Count - 1,
                    Math.Max(0, RoomTimerStrings.IndexOf(RoomTimer))
                ).Change(index => { RoomTimerManager.Instance.SwitchRoomTimer(index); }));
        }


        // ReSharper disable once UnusedMember.Global
        public void CreateEndPointStyleEntry(TextMenu textMenu, bool inGame) {
            textMenu.Add(
                new TextMenu.Slider(Dialog.Clean(DialogIds.EndPointStyle),
                    index => Dialog.Clean(DialogIds.Prefix + EndPointStyleStrings[index]),
                    0,
                    EndPointStyleStrings.Count - 1,
                    Math.Max(0, EndPointStyleStrings.IndexOf(EndPointStyle))
                ).Change(index => {
                    EndPointStyle = EndPointStyleStrings[index];
                    RoomTimerManager.Instance.SavedEndPoint?.ResetSprite();
                }));
        }

        // ReSharper disable once UnusedMember.Global
        public void CreateMaxNumberOfDeathDataEntry(TextMenu textMenu, bool inGame) {
            textMenu.Add(new TextMenu.Slider(
                DialogIds.MaxNumberOfDeathData.DialogClean(),
                value => value == 0 ? "90" : (value * 10).ToString(),
                1,
                9,
                MaxNumberOfDeathData
            ) {
                OnValueChange = value => MaxNumberOfDeathData = value
            });
        }

        // ReSharper disable once UnusedMember.Global
        public void CreateCheckDeathStatisticsEntry(TextMenu textMenu, bool inGame) {
            textMenu.Add(new TextMenu.Button(Dialog.Clean(DialogIds.CheckDeathStatistics)).Pressed(() => {
                textMenu.Focused = false;
                DeathStatisticsUi buttonConfigUi = new DeathStatisticsUi {OnClose = () => textMenu.Focused = true};
                Engine.Scene.Add(buttonConfigUi);
                Engine.Scene.OnEndOfFrame += (Action) (() => Engine.Scene.Entities.UpdateLists());
            }));
        }

        // ReSharper disable once UnusedMember.Global
        public void CreateButtonConfigEntry(TextMenu textMenu, bool inGame) {
            textMenu.Add(lastTextMenu = new TextMenu.Button(Dialog.Clean(DialogIds.ButtonConfig)).Pressed(() => {
                textMenu.Focused = false;
                ButtonConfigUi buttonConfigUi = new ButtonConfigUi {OnClose = () => textMenu.Focused = true};
                Engine.Scene.Add(buttonConfigUi);
                Engine.Scene.OnEndOfFrame += (Action) (() => Engine.Scene.Entities.UpdateLists());
            }));
            firstTextMenu.OnValueChange(Enabled);
            ToggleMoreOptionsMenuItem(textMenu, false);
        }

        private static List<string> GetEnumNames<T>() where T : struct, IConvertible {
            return new List<string>(Enum.GetNames(typeof(T))
                .Select(name => Regex.Replace(name, @"([a-z])([A-Z])", "$1_$2").ToUpper()));
        }

        private static T GetEnumFromName<T>(string name) where T : struct, IConvertible {
            try {
                string enumName = Enum.GetNames(typeof(T))[GetEnumNames<T>().IndexOf(name)];
                return (T) Enum.Parse(typeof(T), enumName);
            } catch (ArgumentException) {
                return (T) Enum.Parse(typeof(T), Enum.GetNames(typeof(T))[0]);
            }
        }
    }

    public enum RoomTimerType {
        Off,
        NextRoom,
        CurrentRoom
    }
}