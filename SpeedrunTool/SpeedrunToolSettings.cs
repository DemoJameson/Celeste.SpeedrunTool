using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Celeste.Mod.SpeedrunTool.RoomTimer;
using Microsoft.Xna.Framework.Input;
using Monocle;
using YamlDotNet.Serialization;

// ReSharper disable MemberCanBePrivate.Global
// ReSharper disable AutoPropertyCanBeMadeGetOnly.Global
namespace Celeste.Mod.SpeedrunTool {
    [SettingName(DialogIds.SpeedrunTool)]
    public class SpeedrunToolSettings : EverestModuleSettings {
        private static readonly List<string> RespawnSpeedStrings =
            Enumerable.Range(1, 9).Select(intValue => intValue + "00%").ToList();

        private static readonly List<string> SkipSceneStrings = GetEnumNames<SkipSceneOption>();
        private static readonly List<string> RoomTimerStrings = GetEnumNames<RoomTimerType>();
        private static readonly List<string> EndPointStyleStrings = GetEnumNames<EndPoint.SpriteStyle>();

        [SettingName(DialogIds.Enabled)] public bool Enabled { get; set; } = true;

        [SettingName(DialogIds.AutoLoadAfterDeath)]
        public bool AutoLoadAfterDeath { get; set; } = true;

        [YamlIgnore] [SettingIgnore] public RoomTimerType RoomTimerType => GetEnumFromName<RoomTimerType>(RoomTimer);

        public string RespawnSpeed { get; set; } = RespawnSpeedStrings.First();

        [YamlIgnore] [SettingIgnore] public int RespawnSpeedInt => RespawnSpeedStrings.IndexOf(RespawnSpeed) + 1;

        public string SkipScene { get; set; } = SkipSceneStrings.Last();

        [YamlIgnore] [SettingIgnore] public SkipSceneOption SkipSceneOption => GetEnumFromName<SkipSceneOption>(SkipScene);

        public string RoomTimer { get; set; } = RoomTimerStrings.First();
       
        [SettingRange(1, 99)]
        [SettingName(DialogIds.NumberOfRooms)]
        public int NumberOfRooms { get; set; } = 1; 
        public string EndPointStyle { get; set; } = EndPointStyleStrings.First();
        [YamlIgnore] [SettingIgnore] public EndPoint.SpriteStyle EndPointSpriteStyle => GetEnumFromName<EndPoint.SpriteStyle>(EndPointStyle);

        

        // ReSharper disable once UnusedMember.Global
        [YamlIgnore] public string ButtonConfig { get; set; } = "";

        [SettingIgnore] public Buttons? ControllerQuickSave { get; set; }
        [SettingIgnore] public Buttons? ControllerQuickLoad { get; set; }
        [SettingIgnore] public Buttons? ControllerQuickClear { get; set; }
        [SettingIgnore] public Buttons? ControllerOpenDebugMap { get; set; }
        [SettingIgnore] public Buttons? ControllerResetRoomPb { get; set; }
        [SettingIgnore] public Buttons? ControllerSetEndPoint { get; set; }

        [SettingIgnore] public Keys KeyboardQuickSave { get; set; } = ButtonConfigUi.DefaultKeyboardSave;
        [SettingIgnore] public Keys KeyboardQuickLoad { get; set; } = ButtonConfigUi.DefaultKeyboardLoad;
        [SettingIgnore] public List<Keys> KeyboardQuickClear { get; set; } = ButtonConfigUi.FixedClearKeys.ToList();
        [SettingIgnore] public List<Keys> KeyboardOpenDebugMap { get; set; } = ButtonConfigUi.FixedOpenDebugMapKeys.ToList();
        [SettingIgnore] public Keys KeyboardResetRoomPb { get; set; } = ButtonConfigUi.DefaultKeyboardResetPb;
        [SettingIgnore] public Keys KeyboardSetEndPoint { get; set; } = ButtonConfigUi.DefaultKeyboardSetEndPoint;


        // ReSharper disable once UnusedMember.Global
        public void CreateRespawnSpeedEntry(TextMenu textMenu, bool inGame) {
            textMenu.Add(
                new TextMenu.Slider(Dialog.Clean(DialogIds.RespawnSpeed),
                    index => RespawnSpeedStrings[index],
                    0,
                    RespawnSpeedStrings.Count - 1,
                    Math.Max(0, RespawnSpeedStrings.IndexOf(RespawnSpeed))
                ).Change(index => RespawnSpeed = RespawnSpeedStrings[index]));
        }

        // ReSharper disable once UnusedMember.Global
        public void CreateSkipSceneEntry(TextMenu textMenu, bool inGame) {
            textMenu.Add(
                new TextMenu.Slider(Dialog.Clean(DialogIds.SkipChapterScene),
                    index => Dialog.Clean(DialogIds.Prefix + SkipSceneStrings[index]),
                    0,
                    SkipSceneStrings.Count - 1,
                    Math.Max(0, SkipSceneStrings.IndexOf(SkipScene))
                ).Change(index => SkipScene = SkipSceneStrings[index]));
        }

        // ReSharper disable once UnusedMember.Global
        public void CreateRoomTimerEntry(TextMenu textMenu, bool inGame) {
            textMenu.Add(
                new TextMenu.Slider(Dialog.Clean(DialogIds.RoomTimer),
                    index => Dialog.Clean(DialogIds.Prefix + RoomTimerStrings[index]),
                    0,
                    RoomTimerStrings.Count - 1,
                    Math.Max(0, RoomTimerStrings.IndexOf(RoomTimer))
                ).Change(index => {
                    RoomTimer = RoomTimerStrings[index];

                    if (RoomTimerType != RoomTimerType.Off) {
                        return;
                    }

                    RoomTimerManager.Instance.ClearPbTimes();
                    SpeedrunType? speedrunType = RoomTimerManager.Instance.OriginalSpeedrunType;
                    if (speedrunType != null) {
                        Settings.Instance.SpeedrunClock = (SpeedrunType) speedrunType;
                    }
                }));
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
        public void CreateButtonConfigEntry(TextMenu textMenu, bool inGame) {
            textMenu.Add(new TextMenu.Button(Dialog.Clean(DialogIds.ButtonConfig)).Pressed(() => {
                textMenu.Focused = false;
                ButtonConfigUi buttonConfigUi = new ButtonConfigUi {OnClose = () => textMenu.Focused = true};
                Engine.Scene.Add(buttonConfigUi);
                Engine.Scene.OnEndOfFrame += (Action) (() => Engine.Scene.Entities.UpdateLists());
            }));
        }

        private static List<string> GetEnumNames<T>() where T : struct, IConvertible {
            return new List<string>(Enum.GetNames(typeof(T))
                .Select(name => Regex.Replace(name, @"([a-z])([A-Z])", "$1_$2").ToUpper()));
        }

        private static T GetEnumFromName<T>(string name) where T : struct, IConvertible {
            try {
                string enumName = Enum.GetNames(typeof(T))[GetEnumNames<T>().IndexOf(name)];
                return (T) Enum.Parse(typeof(T), enumName);
            }
            catch (ArgumentException e) {
                e.LogDetailed();
                return (T) Enum.Parse(typeof(T), Enum.GetNames(typeof(T))[0]);
            }
        }
    }

    [Flags]
    public enum SkipSceneOption {
        // ReSharper disable UnusedMember.Global
        Off = 0,
        Intro = 1,
        Complete = 2,

        All = Intro | Complete
        // ReSharper restore UnusedMember.Global
    }

    public enum RoomTimerType {
        Off,
        NextRoom,
        CurrentRoom
    }
}