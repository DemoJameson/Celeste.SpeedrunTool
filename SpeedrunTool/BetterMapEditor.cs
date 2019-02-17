using System;
using System.Collections.Generic;
using System.Reflection;
using Celeste.Editor;
using Celeste.Mod.SpeedrunTool.SaveLoad;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using Monocle;

namespace Celeste.Mod.SpeedrunTool
{
    public class BetterMapEditor
    {
        // @formatter:off
        private static readonly Lazy<BetterMapEditor> Lazy = new Lazy<BetterMapEditor>(() => new BetterMapEditor());
        public static BetterMapEditor Instance => Lazy.Value;
        private BetterMapEditor() { }
        // @formatter:on 

        private const string StartChasingLevel = "3";

        // LevelName
        private readonly List<string> _dreamDashRooms = new List<string>
        {
            "0", "1", "2",
            "d1", "d2", "d3", "d4", "d6", "d5", "d9", "3x",
            "3", "4", "5", "6", "7", "8", "9", "9b", "10",
            "11", "12b", "12c", "12d", "12", "13"
        };

        private readonly List<Vector2> _excludeDreamRespawnPoints = new List<Vector2>
        {
            new Vector2(288, 152),
            new Vector2(632, 144),
            new Vector2(648, 144),
            new Vector2(800, 168),
            new Vector2(952, 160),
            new Vector2(968, 160),
            new Vector2(1608, 720),
            new Vector2(1616, 600)
        };

        // 3A 杂乱房间部分的光线调暗
        private readonly List<string> _darkRooms = new List<string>
        {
            "09-b", "08-x", "10-x", "11-x", "11-y", "12-y", "11-z", "10-z",
            "10-y", "11-a", "12-x", "13-x", "13-a", "13-b", "12-b", "11-b",
            "10-c", "10-d", "11-d", "12-d", "12-c", "11-c"
        };

        private readonly List<string> _iceRooms = new List<string>
        {
            // 8A
            "9b-05", "9c-00", "9c-00b", "9c-02", "9c-03", "9d-03", "9d-10",

            // 8B
            "9Ha-03", "9Ha-04", "9Ha-05", "9Hb-02", "9Hb-03", "9Hc-01", "9Hc-06"
        };

        private long _zoomWaitFrames;

        public void Load()
        {
            On.Celeste.Editor.MapEditor.LoadLevel += MapEditorOnLoadLevel;
            On.Celeste.Editor.MapEditor.Update += MakeControllerWork;
            On.Celeste.Level.Update += AddedOpenDebugMapButton;
            On.Celeste.WindController.SetAmbienceStrength += FixWindSoundNotPlay;
        }

        public void Unload()
        {
            On.Celeste.Editor.MapEditor.LoadLevel -= MapEditorOnLoadLevel;
            On.Celeste.Editor.MapEditor.Update -= MakeControllerWork;
            On.Celeste.Level.Update -= AddedOpenDebugMapButton;
        }

        public static void Init()
        {
            ButtonConfig.UpdateOpenDebugMapButton();
        }

        private static void FixWindSoundNotPlay(On.Celeste.WindController.orig_SetAmbienceStrength orig,
            WindController self,
            bool strong)
        {
            if (Audio.CurrentAmbienceEventInstance == null)
            {
                Audio.SetAmbience("event:/env/amb/04_main");
            }

            orig(self, strong);
        }


        private void MakeControllerWork(On.Celeste.Editor.MapEditor.orig_Update orig, Editor.MapEditor self)
        {
            _zoomWaitFrames--;
            orig(self);

            // pressed confirm button teleport to the select room
            if (Input.MenuConfirm.Pressed)
            {
                Vector2 mousePosition = (Vector2) self.GetPrivateField("mousePosition");
                LevelTemplate level =
                    self.InvokePrivateMethod("TestCheck", mousePosition) as LevelTemplate;
                if (level != null)
                {
                    if (level.Type == LevelTemplateType.Filler)
                        return;

                    self.InvokePrivateMethod("LoadLevel", level, mousePosition * 8f);
                }
            }

            // right stick zoom the map
            GamePadState currentState = MInput.GamePads[Input.Gamepad].CurrentState;
            Camera camera = typeof(MapEditor).GetField("Camera", BindingFlags.Static | BindingFlags.NonPublic)
                ?.GetValue(null) as Camera;
            if (_zoomWaitFrames < 0 && camera != null)
            {
                float newZoom = 0f;
                if (Math.Abs(currentState.ThumbSticks.Right.X) >= 0.5f)
                {
                    newZoom = camera.Zoom + Math.Sign(currentState.ThumbSticks.Right.X) * 1f;
                }
                else if (Math.Abs(currentState.ThumbSticks.Right.Y) >= 0.5f)
                {
                    newZoom = camera.Zoom + Math.Sign(currentState.ThumbSticks.Right.Y) * 1f;
                }

                if (newZoom >= 1f)
                {
                    camera.Zoom = newZoom;
                    _zoomWaitFrames = 5;
                }
            }

            // move faster when zoom out
            if (camera != null && camera.Zoom < 6f)
            {
                camera.Position += new Vector2(Input.MoveX.Value, Input.MoveY.Value) * 300f * Engine.DeltaTime *
                                   ((float) Math.Pow(1.3, 6 - camera.Zoom) - 1);
            }
        }

        private static void AddedOpenDebugMapButton(On.Celeste.Level.orig_Update orig, Level self)
        {
            orig(self);

            if (ButtonConfig.OpenDebugButton.Value.Pressed && !self.Paused)
            {
                Engine.Commands.FunctionKeyActions[5]();
            }
        }

        private void MapEditorOnLoadLevel(On.Celeste.Editor.MapEditor.orig_LoadLevel orig, Editor.MapEditor self,
            LevelTemplate level, Vector2 at)
        {
            On.Celeste.LevelLoader.ctor += FixTeleportProblems;
            orig(self, level, at);
            On.Celeste.LevelLoader.ctor -= FixTeleportProblems;
        }

        private void FixTeleportProblems(On.Celeste.LevelLoader.orig_ctor orig, LevelLoader self, Session session,
            Vector2? startPosition)
        {
            if (session.StartCheckpoint == null && startPosition != null)
            {
                Vector2 spawnPoint = session.GetSpawnPoint((Vector2) startPosition);

                FixBadelineChase(session, spawnPoint);
                FixHugeMessRoomLight(session);
                FixCoreMode(session);
            }

            orig(self, session, startPosition);
        }

        private void FixBadelineChase(Session session, Vector2 spawnPoint)
        {
            // Logger.Log("Exclude Respawn Point", $"new Vector2({spawnPoint.X}, {spawnPoint.Y}),");
            if (_excludeDreamRespawnPoints.Contains(spawnPoint))
                return;

            if (session.Area.ToString() == "2" && _dreamDashRooms.Contains(session.Level))
            {
                session.Inventory.DreamDash = true;

                // 根据 BadelineOldsite 的代码得知设置这两个 Flag 后才会启动追逐
                if (_dreamDashRooms.IndexOf(session.Level) >= _dreamDashRooms.IndexOf(StartChasingLevel))
                    session.SetFlag(CS02_BadelineIntro.Flag);

                session.LevelFlags.Add(StartChasingLevel);
            }
        }

        private void FixHugeMessRoomLight(Session session)
        {
            if (session.Area.ToString() == "3" && _darkRooms.Contains(session.Level)) session.LightingAlphaAdd = 0.15f;
        }

        private void FixCoreMode(Session session)
        {
            if (_iceRooms.Contains(session.Area + session.Level)) session.CoreMode = Session.CoreModes.Cold;
        }
    }
}