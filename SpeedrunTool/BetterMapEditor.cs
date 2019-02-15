using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Celeste.Editor;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using Monocle;

namespace Celeste.Mod.SpeedrunTool
{
    public class BetterMapEditor
    {
        // @formatter:off
        private BetterMapEditor() { }
        public static BetterMapEditor Instance { get; } = new BetterMapEditor();
        // @formatter:on 

        private const string StartChasingLevel = "3";

        // 3A 杂乱房间部分的光线调暗
        private readonly List<string> _darkRooms = new List<string>
        {
            "09-b", "08-x", "10-x", "11-x", "11-y", "12-y", "11-z", "10-z",
            "10-y", "11-a", "12-x", "13-x", "13-a", "13-b", "12-b", "11-b",
            "10-c", "10-d", "11-d", "12-d", "12-c", "11-c"
        };

        // LevelName
        private readonly List<string> _dreamDashRooms = new List<string>
        {
            "d1", "d2", "d4", "d5", "d9", "3x",
            "3", "4", "5", "6", "7", "8", "9", "9b", "10",
            "11", "12b", "12c", "12d", "12", "13",
            "2", "1" // TODO 根据复活点判读
        };

        private readonly List<string> _iceRooms = new List<string>
        {
            // 8A
            "9b-05", "9c-00", "9c-00b", "9c-02", "9c-03", "9d-03", "9d-10",

            // 8B
            "9Ha-03", "9Ha-04", "9Ha-05", "9Hb-02", "9Hb-03", "9Hc-01", "9Hc-06"
        };

        private long _zoomWaitFrames;
        public VirtualButton OpenDebugButton;

        public void Load()
        {
            On.Celeste.Editor.MapEditor.LoadLevel += MapEditorOnLoadLevel;
            On.Celeste.Editor.MapEditor.Update += MakeControllerWork;
            On.Celeste.Level.Update += AddedOpenDebugMapButton;
        }

        public void Unload()
        {
            On.Celeste.Editor.MapEditor.LoadLevel -= MapEditorOnLoadLevel;
            On.Celeste.Editor.MapEditor.Update -= MakeControllerWork;
            On.Celeste.Level.Update -= AddedOpenDebugMapButton;
        }

        public void Init()
        {
            SpeedrunToolModuleSettings settings = SpeedrunToolModule.Settings;
            OpenDebugButton = new VirtualButton(0.08f);
            OpenDebugButton.Nodes.AddRange(
                settings.KeyboardOpenDebugMap.Select(keys => new VirtualButton.KeyboardKey(keys)));
            if (settings.ControllerOpenDebugMap != null)
            {
                OpenDebugButton.Nodes.Add(new VirtualButton.PadButton(Input.Gamepad,
                    (Buttons) settings.ControllerOpenDebugMap));
            }
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
                    self.GetPrivateMethod("TestCheck").Invoke(self, new object[] {mousePosition}) as LevelTemplate;
                if (level != null)
                {
                    if (level.Type == LevelTemplateType.Filler)
                        return;

                    self.GetPrivateMethod("LoadLevel").Invoke(self, new object[] {level, mousePosition * 8f});
                }
            }

            // right stick zoom the map
            GamePadState currentState = MInput.GamePads[Input.Gamepad].CurrentState;
            Camera camera = typeof(MapEditor).GetField("Camera", BindingFlags.Static | BindingFlags.NonPublic)
                ?.GetValue(null) as Camera;
            if (_zoomWaitFrames < 0 && Math.Abs(currentState.ThumbSticks.Right.X) >= 0.5f)
            {
                if (camera != null)
                {
                    float newZoom = camera.Zoom + Math.Sign(currentState.ThumbSticks.Right.X) * 1f;
                    if (newZoom >= 1f)
                    {
                        camera.Zoom = newZoom;
                        _zoomWaitFrames = 5;
                    }
                }
            }

            // move faster when zoom out
            if (camera != null && camera.Zoom < 6f)
            {
                camera.Position += new Vector2(Input.MoveX.Value, Input.MoveY.Value) * 300f * Engine.DeltaTime *
                                   ((float) Math.Pow(1.3, 6 - camera.Zoom) - 1);
            }
        }

        private void AddedOpenDebugMapButton(On.Celeste.Level.orig_Update orig, Level self)
        {
            orig(self);

            if (OpenDebugButton.Pressed && !self.Paused)
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
            if (session.StartCheckpoint == null)
            {
                FixBadelineChase(session);
                FixHugeMessRoomLight(session);
                FixCoreMode(session);
            }

            orig(self, session, startPosition);
        }

        private void FixBadelineChase(Session session)
        {
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