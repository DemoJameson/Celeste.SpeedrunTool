using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using Celeste.Mod.SpeedrunTool.DeathStatistics;
using Celeste.Mod.SpeedrunTool.Extensions;
using Celeste.Mod.SpeedrunTool.Other;
using Celeste.Mod.SpeedrunTool.RoomTimer;
using FMOD.Studio;
using Monocle;

namespace Celeste.Mod.SpeedrunTool {
    public static class SpeedrunToolMenu {
        private static readonly Regex RegexFormatName = new Regex(@"([a-z])([A-Z])", RegexOptions.Compiled);
        private static SpeedrunToolSettings Settings => SpeedrunToolModule.Settings;
        private static List<TextMenu.Item> options;

        public static void Create(TextMenu menu, bool inGame, EventInstance snapshot) {
            menu.Add(new TextMenu.OnOff(Dialog.Clean(DialogIds.Enabled), Settings.Enabled).Change((value) => {
                Settings.Enabled = value;
                foreach (TextMenu.Item item in options) {
                    item.Visible = value;
                }
            }));
            CreateOptions(menu, inGame);
            foreach (TextMenu.Item item in options) {
                menu.Add(item);
                item.Visible = Settings.Enabled;
            }
        }

        private static IEnumerable<KeyValuePair<T, string>> CreateEnumerableOptions<T>() where T : struct, IConvertible {
            List<KeyValuePair<T, string>> results = new List<KeyValuePair<T, string>>();
            foreach (T value in Enum.GetValues(typeof(T))) {
                results.Add(new KeyValuePair<T, string>(value,
                    Dialog.Clean(DialogIds.Prefix + RegexFormatName.Replace(value.ToString(), "$1_$2").ToUpper())));
            }

            return results;
        }

        private static void CreateOptions(TextMenu menu, bool inGame) {
            options = new List<TextMenu.Item> {
                new TextMenuExt.SubMenu(Dialog.Clean(DialogIds.RoomTimer), false).With(subMenu => {
                    subMenu.Add(new TextMenuExt.EnumerableSlider<RoomTimerType>(Dialog.Clean(DialogIds.Enabled),
                        CreateEnumerableOptions<RoomTimerType>(), Settings.RoomTimerType).Change(timerType => { RoomTimerManager.Instance.SwitchRoomTimer(timerType); }));

                    subMenu.Add(new TextMenuExt.IntSlider(Dialog.Clean(DialogIds.NumberOfRooms), 1, 99, Settings.NumberOfRooms).Change(i =>
                        Settings.NumberOfRooms = i));

                    subMenu.Add(new TextMenuExt.EnumerableSlider<EndPoint.SpriteStyle>(Dialog.Clean(DialogIds.EndPointStyle),
                        CreateEnumerableOptions<EndPoint.SpriteStyle>(), Settings.EndPointStyle).Change(value => {
                        Settings.EndPointStyle = value;
                        EndPoint.All.ForEach(endPoint => endPoint.ResetSprite());
                    }));

                    subMenu.Add(new TextMenu.OnOff(Dialog.Clean(DialogIds.RoomTimerIgnoreFlag), Settings.RoomTimerIgnoreFlag).Change(b =>
                        Settings.RoomTimerIgnoreFlag = b));

                    subMenu.Add(new TextMenu.OnOff(Dialog.Clean(DialogIds.AutoTurnOffRoomTimer), Settings.AutoResetRoomTimer).Change(b =>
                        Settings.AutoResetRoomTimer = b));
                }),

                new TextMenuExt.SubMenu(Dialog.Clean(DialogIds.State), false).With(subMenu => {
                    subMenu.Add(new TextMenu.OnOff(Dialog.Clean(DialogIds.AutoLoadStateAfterDeath), Settings.AutoLoadStateAfterDeath).Change(b =>
                        Settings.AutoLoadStateAfterDeath = b));

                    subMenu.Add(new TextMenu.OnOff(Dialog.Clean(DialogIds.FreezeAfterLoadState), Settings.FreezeAfterLoadState).Change(b =>
                        Settings.FreezeAfterLoadState = b));
                }),

                new TextMenuExt.SubMenu(Dialog.Clean(DialogIds.DeathStatistics), false).With(subMenu => {
                    subMenu.Add(new TextMenu.OnOff(Dialog.Clean(DialogIds.Enabled), Settings.DeathStatistics).Change(b =>
                        Settings.DeathStatistics = b));

                    subMenu.Add(new TextMenu.Slider(
                        DialogIds.MaxNumberOfDeathData.DialogClean(),
                        value => (value * 10).ToString(),
                        1,
                        9,
                        Settings.MaxNumberOfDeathData
                    ).Change(i => Settings.MaxNumberOfDeathData = i));

                    subMenu.Add(new TextMenu.Button(Dialog.Clean(DialogIds.CheckDeathStatistics)).Pressed(() => {
                        menu.Focused = false;
                        DeathStatisticsUi buttonConfigUi = new DeathStatisticsUi {OnClose = () => menu.Focused = true};
                        Engine.Scene.Add(buttonConfigUi);
                        Engine.Scene.OnEndOfFrame += (Action) (() => Engine.Scene.Entities.UpdateLists());
                    }));
                }),

                new TextMenuExt.SubMenu(Dialog.Clean(DialogIds.MoreOptions), false).With(subMenu => {
                    subMenu.Add(new TextMenuExt.IntSlider(Dialog.Clean(DialogIds.RespawnSpeed), 1, 9, Settings.RespawnSpeed).Change(i =>
                        Settings.RespawnSpeed = i));

                    subMenu.Add(
                        new TextMenu.OnOff(Dialog.Clean(DialogIds.FastTeleport), Settings.FastTeleport).Change(b => Settings.FastTeleport = b));

                    subMenu.Add(new TextMenu.Button(Dialog.Clean(DialogIds.ButtonConfig)).Pressed(() => {
                        menu.Focused = false;
                        ButtonConfigUi buttonConfigUi = new ButtonConfigUi {OnClose = () => menu.Focused = true};
                        Engine.Scene.Add(buttonConfigUi);
                        Engine.Scene.OnEndOfFrame += (Action) (() => Engine.Scene.Entities.UpdateLists());
                    }));
                }),
            };
        }
    }
}