using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using Celeste.Editor;
using Celeste.Mod.SpeedrunTool.Extensions;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using Monocle;
using static Celeste.Mod.SpeedrunTool.ButtonConfigUi;
using LevelTemplate = Celeste.Editor.LevelTemplate;
using MapEditor = On.Celeste.Editor.MapEditor;

namespace Celeste.Mod.SpeedrunTool {
    public class BetterMapEditor {
        public static bool FixTeleportProblems = false;
        private static readonly Lazy<bool> _EnableQoL = new Lazy<bool>(()=>Everest.Version <= new Version(1, 1078, 0));
        private static bool EnableQoL => _EnableQoL.Value;
        
        private const string StartChasingLevel = "3";

        // 3A 杂乱房间部分的光线调暗
        private readonly List<string> darkRooms = new List<string> {
            "09-b", "08-x", "10-x", "11-x", "11-y", "12-y", "11-z", "10-z",
            "10-y", "11-a", "12-x", "13-x", "13-a", "13-b", "12-b", "11-b",
            "10-c", "10-d", "11-d", "12-d", "12-c", "11-c"
        };

        // LevelName
        private readonly List<string> dreamDashRooms = new List<string> {
            "0", "1", "2",
            "d0", "d1", "d2", "d3", "d4", "d6", "d5", "d9", "3x",
            "3", "4", "5", "6", "7", "8", "9", "9b", "10",
            "11", "12b", "12c", "12d", "12", "13"
        };

        private readonly List<Vector2> excludeFarewellCassettePoints = new List<Vector2> {
            new Vector2(43632, -9976),
            new Vector2(49448, -10296)
        };

        private readonly List<string> farewellCassetteRooms = new List<string> {
            "i-00","i-00b","i-01","i-02","i-03","i-04","i-05","j-00"
        };

        private readonly List<Vector2> excludeDreamRespawnPoints = new List<Vector2> {
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

        private readonly List<string> iceRooms = new List<string> {
            // 8A
            "9b-05", "9c-00", "9c-00b", "9c-02", "9c-03", "9d-03", "9d-10",

            // 8B
            "9Ha-03", "9Ha-04", "9Ha-05", "9Hb-02", "9Hb-03", "9Hc-01", "9Hc-06"
        };

        private long zoomWaitFrames;

        public void Load() {
            MapEditor.LoadLevel += MapEditorOnLoadLevel;
            MapEditor.Update += MakeControllerWork;
            On.Celeste.Level.Update += AddedOpenDebugMapButton;
            On.Celeste.WindController.SetAmbienceStrength += FixWindSoundNotPlay;
            MapEditor.Update += PressCancelToReturnGame;
            On.Celeste.OshiroTrigger.ctor += RestoreOshiroTrigger;
            On.Celeste.Commands.CmdLoad += CommandsOnCmdLoad;
            On.Celeste.LevelLoader.ctor += LevelLoaderOnCtor;
        }

        public void Unload() {
            MapEditor.LoadLevel -= MapEditorOnLoadLevel;
            MapEditor.Update -= MakeControllerWork;
            On.Celeste.Level.Update -= AddedOpenDebugMapButton;
            On.Celeste.WindController.SetAmbienceStrength -= FixWindSoundNotPlay;
            MapEditor.Update -= PressCancelToReturnGame;
            On.Celeste.OshiroTrigger.ctor -= RestoreOshiroTrigger;
            On.Celeste.Commands.CmdLoad -= CommandsOnCmdLoad;
            On.Celeste.LevelLoader.ctor -= LevelLoaderOnCtor;
        }

        private void CommandsOnCmdLoad(On.Celeste.Commands.orig_CmdLoad orig, int id, string level) {
            if (!SpeedrunToolModule.Enabled) {
                orig(id, level);
                return;
            }

            FixTeleportProblems = true;
            orig(id, level);
            FixTeleportProblems = false;
        }

        // 修复 3C 第三面最后的传送点 Oshiro 不出现的问题
        private void RestoreOshiroTrigger(On.Celeste.OshiroTrigger.orig_ctor orig, OshiroTrigger self,
            EntityData data,
            Vector2 offset) {
            orig(self, data, offset);

            Vector2 oshiro3C = new Vector2(1520, -272);
            Level level = CelesteExtensions.GetLevel();

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

        private static void PressCancelToReturnGame(MapEditor.orig_Update orig, Editor.MapEditor self) {
            Session currentSession = (Engine.Scene as Level)?.Session ?? SaveData.Instance?.CurrentSession;
            if ((Input.ESC.Pressed || Input.MenuCancel.Pressed) && currentSession != null) {
                Input.ESC.ConsumePress();
                Input.MenuCancel.ConsumePress();
                Engine.Scene = new LevelLoader(currentSession);
            }

            orig(self);
        }

        public static void Init() {
            UpdateVirtualButton(Mappings.OpenDebugMap);
        }

        private static void FixWindSoundNotPlay(On.Celeste.WindController.orig_SetAmbienceStrength orig,
            WindController self,
            bool strong) {
            if (Audio.CurrentAmbienceEventInstance == null) {
                Audio.SetAmbience("event:/env/amb/04_main");
            }

            orig(self, strong);
        }

        private void MakeControllerWork(MapEditor.orig_Update orig, Editor.MapEditor self) {
            orig(self);
            if (!SpeedrunToolModule.Enabled || !EnableQoL) {
                return;
            }

            zoomWaitFrames--;

            // pressed confirm button teleport to the select room
            if (Input.MenuConfirm.Pressed) {
                Input.MenuConfirm.ConsumePress();
                Vector2 mousePosition = (Vector2) self.GetField("mousePosition");
                LevelTemplate level =
                    self.InvokeMethod("TestCheck", mousePosition) as LevelTemplate;
                if (level != null) {
                    if (level.Type == LevelTemplateType.Filler) {
                        return;
                    }

                    self.InvokeMethod("LoadLevel", level, mousePosition * 8f);
                }
            }

            // right stick zoom the map
            GamePadState currentState = MInput.GamePads[Input.Gamepad].CurrentState;
            Camera camera = typeof(Editor.MapEditor).GetField("Camera", BindingFlags.Static | BindingFlags.NonPublic)
                ?.GetValue(null) as Camera;
            if (zoomWaitFrames < 0 && camera != null) {
                float newZoom = 0f;
                if (Math.Abs(currentState.ThumbSticks.Right.X) >= 0.5f) {
                    newZoom = camera.Zoom + Math.Sign(currentState.ThumbSticks.Right.X) * 1f;
                }
                else if (Math.Abs(currentState.ThumbSticks.Right.Y) >= 0.5f) {
                    newZoom = camera.Zoom + Math.Sign(currentState.ThumbSticks.Right.Y) * 1f;
                }

                if (newZoom >= 1f) {
                    camera.Zoom = newZoom;
                    zoomWaitFrames = 5;
                }
            }

            // move faster when zoom out
            if (camera != null && camera.Zoom < 6f) {
                camera.Position += new Vector2(Input.MoveX.Value, Input.MoveY.Value) * 300f * Engine.DeltaTime *
                                   ((float) Math.Pow(1.3, 6 - camera.Zoom) - 1);
            }
        }

        private static void AddedOpenDebugMapButton(On.Celeste.Level.orig_Update orig, Level self) {
            orig(self);
            
            if (!SpeedrunToolModule.Enabled) {
                return;
            }

            if (GetVirtualButton(Mappings.OpenDebugMap).Pressed && !self.Paused) {
                GetVirtualButton(Mappings.OpenDebugMap).ConsumePress();
                Engine.Commands.FunctionKeyActions[5]();
            }
        }

        private void MapEditorOnLoadLevel(MapEditor.orig_LoadLevel orig, Editor.MapEditor self,
            LevelTemplate level, Vector2 at) {
            if (!SpeedrunToolModule.Enabled) {
                orig(self, level, at);
                return;
            }

            FixTeleportProblems = true;
            orig(self, level, at);
            FixTeleportProblems = false;
        }

        private void LevelLoaderOnCtor(On.Celeste.LevelLoader.orig_ctor orig, LevelLoader self, Session session,
            Vector2? startPosition) {
            if (FixTeleportProblems && session.StartCheckpoint == null && session.LevelData != null) {
                Vector2 spawnPoint;
                if (startPosition != null) {
                    spawnPoint = session.GetSpawnPoint((Vector2) startPosition);
                }
                else {
                    Rectangle bounds = session.LevelData.Bounds;
                    spawnPoint = session.GetSpawnPoint(new Vector2(bounds.Left, bounds.Bottom));
                }

                FixCoreMode(session);
                FixBadelineChase(session, spawnPoint);
                FixHugeMessRoomLight(session);
                FixFarewellCassetteRoomLight(session, spawnPoint);
                FixFarewellIntro02LaunchDashes(session);
            }

            orig(self, session, startPosition);
        }

        private void FixFarewellCassetteRoomLight(Session session, Vector2 spawnPoint) {
//            Logger.Log("Exclude Respawn Point", $"new Vector2({spawnPoint.X}, {spawnPoint.Y}),");
            if (excludeFarewellCassettePoints.Contains(spawnPoint)) {
                return;
            }

            if (session.Area.ToString() == "10" && farewellCassetteRooms.Contains(session.Level)) {
                session.ColorGrade = "feelingdown";
            }
        }

        private void FixFarewellIntro02LaunchDashes(Session session) {
            if (session.Area.ToString() == "10" && session.Level == "intro-02-launch") {
                session.Inventory.Dashes = 2;
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
            if (session.Area.ToString() == "3" && darkRooms.Contains(session.Level)) {
                session.LightingAlphaAdd = 0.15f;
            }
        }

        private void FixCoreMode(Session session) {
            if (iceRooms.Contains(session.Area + session.Level)) {
                session.CoreMode = Session.CoreModes.Cold;
            }
        }

        // @formatter:off
        private static readonly Lazy<BetterMapEditor> Lazy = new Lazy<BetterMapEditor>(() => new BetterMapEditor());
        public static BetterMapEditor Instance => Lazy.Value;
        private BetterMapEditor() { }
        // @formatter:on 
    }
}