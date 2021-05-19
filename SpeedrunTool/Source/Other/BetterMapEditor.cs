using System;
using System.Collections;
using System.Collections.Generic;
using Celeste.Mod.SpeedrunTool.Extensions;
using Microsoft.Xna.Framework;
using Monocle;
using On.Celeste.Editor;
using static Celeste.Mod.SpeedrunTool.Other.ButtonConfigUi;
using LevelTemplate = Celeste.Editor.LevelTemplate;

namespace Celeste.Mod.SpeedrunTool.Other {
    public class BetterMapEditor {
        public static bool ShouldFixTeleportProblems;
        private const string StartChasingLevel = "3";

        // 3A 杂乱房间部分的光线调暗
        private readonly HashSet<string> darkRooms = new() {
            "09-b", "08-x", "10-x", "11-x", "11-y", "12-y", "11-z", "10-z",
            "10-y", "11-a", "12-x", "13-x", "13-a", "13-b", "12-b", "11-b",
            "10-c", "10-d", "11-d", "12-d", "12-c", "11-c"
        };

        // 2A 需要激活果冻的房间
        private readonly List<string> dreamDashRooms = new() {
            "0", "1", "2",
            "d0", "d1", "d2", "d3", "d4", "d6", "d5", "d9", "3x",
            "3", "4", "5", "6", "7", "8", "9", "9b", "10",
            "11", "12b", "12c", "12d", "12", "13"
        };

        private readonly HashSet<Vector2> excludeFarewellCassettePoints = new() {
            new Vector2(43632, -9976),
            new Vector2(49448, -10296)
        };

        private readonly HashSet<string> farewellCassetteRooms = new() {
            "i-00", "i-00b", "i-01", "i-02", "i-03", "i-04", "i-05", "j-00"
        };

        private readonly HashSet<Vector2> excludeDreamRespawnPoints = new() {
            new Vector2(288, 152),
            new Vector2(632, 144),
            new Vector2(648, 144),
            new Vector2(800, 168),
            new Vector2(792, 248),
            new Vector2(648, 272),
            new Vector2(648, 512),
            new Vector2(952, 328),
            new Vector2(952, 520),
            new Vector2(648, 704),
            new Vector2(952, 712),
            new Vector2(952, 160),
            new Vector2(968, 160),
            new Vector2(1608, 720),
            new Vector2(1616, 600)
        };

        private readonly HashSet<string> coreIceRooms = new() {
            // 8A
            "9b-05", "9c-00", "9c-00b", "9c-02", "9c-03", "9d-03", "9d-10",

            // 8B
            "9Ha-03", "9Ha-04", "9Ha-05", "9Hb-02", "9Hb-03", "9Hc-01", "9Hc-06"
        };

        private readonly HashSet<string> coreRefillDashRooms = new() {
            // 8A
            "90X", "900", "901", "9space",
            // 8B
            "9H00", "9H01", "9Hspace",
            // 8C
            "9HHintro"
        };

        public void Load() {
            MapEditor.LoadLevel += MapEditorOnLoadLevel;
            On.Celeste.Level.Update += AddedOpenDebugMapButton;
            On.Celeste.WindController.SetAmbienceStrength += FixWindSoundNotPlay;
            On.Celeste.OshiroTrigger.ctor += RestoreOshiroTrigger;
            On.Celeste.Commands.CmdLoad += CommandsOnCmdLoad;
            On.Celeste.LevelLoader.ctor += LevelLoaderOnCtor;
        }

        public void Unload() {
            MapEditor.LoadLevel -= MapEditorOnLoadLevel;
            On.Celeste.Level.Update -= AddedOpenDebugMapButton;
            On.Celeste.WindController.SetAmbienceStrength -= FixWindSoundNotPlay;
            On.Celeste.OshiroTrigger.ctor -= RestoreOshiroTrigger;
            On.Celeste.Commands.CmdLoad -= CommandsOnCmdLoad;
            On.Celeste.LevelLoader.ctor -= LevelLoaderOnCtor;
        }

        private void CommandsOnCmdLoad(On.Celeste.Commands.orig_CmdLoad orig, int id, string level) {
            if (!SpeedrunToolModule.Enabled) {
                orig(id, level);
                return;
            }

            ShouldFixTeleportProblems = true;
            orig(id, level);
        }

        // 修复 3C 第三面最后的传送点 Oshiro 不出现的问题
        private void RestoreOshiroTrigger(On.Celeste.OshiroTrigger.orig_ctor orig, OshiroTrigger self,
            EntityData data,
            Vector2 offset) {
            orig(self, data, offset);

            if (!SpeedrunToolModule.Enabled) {
                return;
            }

            Vector2 oshiro3C = new(1520, -272);
            Level level = Engine.Scene.GetLevel();

            if (level != null && level.Session.Area.ToString() == "3HH" && level.StartPosition != null &&
                level.Session.GetSpawnPoint((Vector2) level.StartPosition) == oshiro3C) {
                self.Add(new Coroutine(OnEnter(self)));
            }
        }

        private IEnumerator OnEnter(OshiroTrigger self) {
            Player player = self.SceneAs<Level>().GetPlayer();
            if (player != null) {
                self.OnEnter(player);
            }

            yield break;
        }

        private static void FixWindSoundNotPlay(On.Celeste.WindController.orig_SetAmbienceStrength orig, WindController self, bool strong) {
            if (SpeedrunToolModule.Enabled && Audio.CurrentAmbienceEventInstance == null && Engine.Scene.GetSession()?.Area.LevelSet == "Celeste") {
                Audio.SetAmbience("event:/env/amb/04_main");
            }

            orig(self, strong);
        }

        private static void AddedOpenDebugMapButton(On.Celeste.Level.orig_Update orig, Level self) {
            orig(self);

            if (!SpeedrunToolModule.Enabled) {
                return;
            }

            if (Mappings.OpenDebugMap.Pressed() && !self.Paused) {
                Mappings.OpenDebugMap.ConsumePress();
                Engine.Commands.FunctionKeyActions[5]();
            }
        }

        private void MapEditorOnLoadLevel(MapEditor.orig_LoadLevel orig, Editor.MapEditor self,
            LevelTemplate level, Vector2 at) {
            if (!SpeedrunToolModule.Enabled) {
                orig(self, level, at);
                return;
            }

            ShouldFixTeleportProblems = true;
            orig(self, level, at);
        }

        private void LevelLoaderOnCtor(On.Celeste.LevelLoader.orig_ctor orig, LevelLoader self, Session session,
            Vector2? startPosition) {
            if (ShouldFixTeleportProblems) {
                ShouldFixTeleportProblems = false;
                FixTeleportProblems(session, startPosition);
            }

            orig(self, session, startPosition);
        }

        public void FixTeleportProblems(Session session, Vector2? startPosition) {
            if (SpeedrunToolModule.Enabled && session.LevelData != null) {
                Vector2 spawnPoint;
                if (startPosition != null) {
                    spawnPoint = session.GetSpawnPoint(startPosition.Value);
                } else {
                    Rectangle bounds = session.LevelData.Bounds;
                    spawnPoint = session.GetSpawnPoint(new Vector2(bounds.Left, bounds.Bottom));
                }

                FixCoreMode(session);
                FixCoreRefillDash(session);
                FixBadelineChase(session, spawnPoint);
                FixHugeMessRoomLight(session);
                FixFarewellCassetteRoomColorGrade(session, spawnPoint);
                FixFarewellIntro02LaunchDashes(session);
            }
        }

        private void FixFarewellCassetteRoomColorGrade(Session session, Vector2 spawnPoint) {
            // Logger.Log("Exclude Respawn Point", $"new Vector2({spawnPoint.X}, {spawnPoint.Y}),");
            if (excludeFarewellCassettePoints.Contains(spawnPoint)) {
                return;
            }

            if (session.Area.ToString() == "10") {
                if (farewellCassetteRooms.Contains(session.Level)) {
                    session.ColorGrade = "feelingdown";
                } else if (session.Level == "end-golden") {
                    session.ColorGrade = "golden";
                } else {
                    session.ColorGrade = "none";
                }
            }
        }

        private void FixFarewellIntro02LaunchDashes(Session session) {
            if (session.Area.ToString() == "10") {
                session.Inventory.Dashes = session.Level == "intro-02-launch" ? 2 : 1;
            }
        }

        private void FixBadelineChase(Session session, Vector2 spawnPoint) {
            // Logger.Log("Exclude Respawn Point", $"new Vector2({spawnPoint.X}, {spawnPoint.Y}),");
            if (excludeDreamRespawnPoints.Contains(spawnPoint)) {
                return;
            }

            if (session.Area.ToString() == "2" && dreamDashRooms.Contains(session.Level)) {
                session.Inventory.DreamDash = true;

                // 根据 BadelineOldsite 的代码得知设置这两个 Flag 后才会启动追逐
                if (dreamDashRooms.IndexOf(session.Level) >= dreamDashRooms.IndexOf(StartChasingLevel)) {
                    session.SetFlag(CS02_BadelineIntro.Flag);
                }

                session.LevelFlags.Add(StartChasingLevel);
            }
        }

        private void FixHugeMessRoomLight(Session session) {
            if (session.Area.ToString() == "3") {
                if (darkRooms.Contains(session.Level)) {
                    session.LightingAlphaAdd = 0.15f;
                } else {
                    session.LightingAlphaAdd = 0;
                }
            }
        }

        private void FixCoreMode(Session session) {
            if (session.Area.ID == 9) {
                session.CoreMode = coreIceRooms.Contains(session.Area + session.Level) ? Session.CoreModes.Cold : Session.CoreModes.Hot;
            }
        }

        private void FixCoreRefillDash(Session session) {
            if (session.Area.ID == 9) {
                session.Inventory.NoRefills = !coreRefillDashRooms.Contains(session.Level);
            }
        }

        // @formatter:off
        private static readonly Lazy<BetterMapEditor> Lazy = new(() => new BetterMapEditor());
        public static BetterMapEditor Instance => Lazy.Value;
        private BetterMapEditor() { }
        // @formatter:on 
    }
}