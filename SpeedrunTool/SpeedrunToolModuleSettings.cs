using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Celeste.Mod.SpeedrunTool.SaveLoad;
using Microsoft.Xna.Framework.Input;
using Monocle;
using YamlDotNet.Serialization;

namespace Celeste.Mod.SpeedrunTool
{
    [SettingName("SPEEDRUN_TOOL")]
    public class SpeedrunToolModuleSettings : EverestModuleSettings
    {
        private static readonly List<string> RespawnSpeedStrings =
            Enumerable.Range(1, 9).Select(intValue => intValue + "00%").ToList();

        private static readonly List<string> SkipSceneStrings = GetEnumNames<SkipSceneOption>();
        private static readonly List<string> RoomTimerStrings = GetEnumNames<RoomTimerType>();


        [SettingName("ENABLED")] public bool Enabled { get; set; } = true;

        [SettingName("AUTO_LOAD_AFTER_DEATH")] public bool AutoLoadAfterDeath { get; set; } = true;

        public string RespawnSpeed { get; set; } = RespawnSpeedStrings.First();

        [YamlIgnore] [SettingIgnore] public int RespawnSpeedInt => RespawnSpeedStrings.IndexOf(RespawnSpeed) + 1;

        public string SkipScene { get; set; } = SkipSceneStrings.Last();

        [YamlIgnore]
        [SettingIgnore]
        public SkipSceneOption SkipSceneOption => GetEnumFromName<SkipSceneOption>(SkipScene);

        public string RoomTimer { get; set; } = RoomTimerStrings.First();
        [YamlIgnore] [SettingIgnore] public RoomTimerType RoomTimerType => GetEnumFromName<RoomTimerType>(RoomTimer);
        
        [SettingRange(1,99)]
        [SettingName("NUMBER_OF_ROOMS")]
        public int NumberOfRooms { get; set; } = 1;

        [SettingIgnore] public Buttons ControllerQuickSave { get; set; } = Buttons.LeftStick;
        [SettingIgnore] public Buttons ControllerQuickLoad { get; set; } = Buttons.RightStick;
        [SettingIgnore] public Buttons ControllerQuickClear { get; set; } = Buttons.Back;
        [SettingIgnore] public Buttons? ControllerOpenDebugMap { get; set; }
        [SettingIgnore] public Buttons? ControllerResetRoomPb { get; set; }

        [SettingIgnore] public Keys KeyboardQuickSave { get; set; } = Keys.F7;
        [SettingIgnore] public Keys KeyboardQuickLoad { get; set; } = Keys.F8;
        [SettingIgnore] public List<Keys> KeyboardOpenDebugMap { get; set; } = ButtonConfigUi.FixedOpenDebugMapKeys.ToList();
        
        [SettingIgnore] public List<Keys> KeyboardQuickClear { get; set; } = ButtonConfigUi.FixedClearKeys.ToList();
        [SettingIgnore] public Keys KeyboardResetRoomPb { get; set; } = Keys.F9;

        [YamlIgnore] public string ButtonConfig { get; set; } = "";

        public void CreateRespawnSpeedEntry(TextMenu textMenu, bool inGame)
        {
            textMenu.Add(
                new TextMenu.Slider(Dialog.Clean("RESPAWN_SPEED"),
                    index => RespawnSpeedStrings[index],
                    0,
                    RespawnSpeedStrings.Count - 1,
                    Math.Max(0, RespawnSpeedStrings.IndexOf(RespawnSpeed))
                ).Change(index => RespawnSpeed = RespawnSpeedStrings[index]));
        }

        public void CreateSkipSceneEntry(TextMenu textMenu, bool inGame)
        {
            textMenu.Add(
                new TextMenu.Slider(Dialog.Clean("SKIP_CHAPTER_SCENE"),
                    index => Dialog.Clean(SkipSceneStrings[index]),
                    0,
                    SkipSceneStrings.Count - 1,
                    Math.Max(0, SkipSceneStrings.IndexOf(SkipScene))
                ).Change(index => SkipScene = SkipSceneStrings[index]));
        }

        public void CreateRoomTimerEntry(TextMenu textMenu, bool inGame)
        {
            textMenu.Add(
                new TextMenu.Slider(Dialog.Clean("ROOM_TIMER"),
                    index => Dialog.Clean(RoomTimerStrings[index]),
                    0,
                    RoomTimerStrings.Count - 1,
                    Math.Max(0, RoomTimerStrings.IndexOf(RoomTimer))
                ).Change(index =>
                {
                    RoomTimer = RoomTimerStrings[index];

                    if (RoomTimerType != RoomTimerType.OFF) return;
                    RoomTimerManager.Instance.ClearPbTimes();
                    SpeedrunType? speedrunType = RoomTimerManager.Instance.OriginalSpeedrunType;
                    if (speedrunType != null)
                        Settings.Instance.SpeedrunClock = (SpeedrunType) speedrunType;
                }));
        }

        public void CreateButtonConfigEntry(TextMenu textMenu, bool inGame)
        {
            textMenu.Add(new TextMenu.Button(Dialog.Clean("BUTTON_CONFIG")).Pressed(() =>
            {
                textMenu.Focused = false;
                ButtonConfigUi buttonConfigUi = new ButtonConfigUi {OnClose = () => textMenu.Focused = true};
                Engine.Scene.Add(buttonConfigUi);
                Engine.Scene.OnEndOfFrame += (Action) (() => Engine.Scene.Entities.UpdateLists());
            }));
        }

        private static List<string> GetEnumNames<T>() where T : struct, IConvertible
        {
            return new List<string>(Enum.GetNames(typeof(T))
                .Select(name => Regex.Replace(name, @"([a-z])([A-Z])", "$1_$2").ToUpper()));
        }

        private static T GetEnumFromName<T>(string name) where T : struct, IConvertible
        {
            try
            {
                string enumName = Enum.GetNames(typeof(T))[GetEnumNames<T>().IndexOf(name)];
                return (T) Enum.Parse(typeof(T), enumName);
            }
            catch (ArgumentException e)
            {
                e.LogDetailed();
                return (T) Enum.Parse(typeof(T), Enum.GetNames(typeof(T))[0]);
            }
        }
    }

    [Flags]
    public enum SkipSceneOption
    {
        OFF = 0,
        Intro = 1,
        Complete = 2,
        ALL = Intro | Complete
    }

    public enum RoomTimerType
    {
        OFF,
        NextRoom,
        CurrentRoom
    }
}