using System;
using System.Linq;
using Celeste.Mod.SpeedrunTool.Extensions;
using Celeste.Mod.SpeedrunTool.Other;
using Celeste.Mod.SpeedrunTool.SaveLoad;
using Microsoft.Xna.Framework;
using Mono.Cecil.Cil;
using Monocle;
using MonoMod.Cil;
using static Celeste.Mod.SpeedrunTool.Other.ButtonConfigUi;

namespace Celeste.Mod.SpeedrunTool.RoomTimer {
    internal enum TimerState {
        WaitToStart,
        Timing,
        Completed
    }

    public sealed class RoomTimerManager {
        private static readonly Color bestColor1 = Calc.HexToColor("fad768");
        private static readonly Color bestColor2 = Calc.HexToColor("cfa727");
        private static readonly Color finishedColor1 = Calc.HexToColor("6ded87");
        private static readonly Color finishedColor2 = Calc.HexToColor("43d14c");

        public const string FlagPrefix = "summit_checkpoint_";
        private readonly RoomTimerData currentRoomTimerData = new RoomTimerData(RoomTimerType.CurrentRoom);
        private readonly RoomTimerData nextRoomTimerData = new RoomTimerData(RoomTimerType.NextRoom);

        private string previousRoom = null;

        public void Load() {
            IL.Celeste.SpeedrunTimerDisplay.Update += SpeedrunTimerDisplayOnUpdate;
            On.Celeste.SpeedrunTimerDisplay.Render += Render;
            On.Celeste.Level.Update += Timing;
            On.Celeste.Level.Update += ProcessButtons;
            On.Celeste.Level.NextLevel += UpdateTimerStateOnNextLevel;
            On.Celeste.SummitCheckpoint.Update += UpdateTimerStateOnTouchFlag;
            On.Celeste.LevelExit.ctor += LevelExitOnCtor;
        }

        public void Unload() {
            IL.Celeste.SpeedrunTimerDisplay.Update -= SpeedrunTimerDisplayOnUpdate;
            On.Celeste.SpeedrunTimerDisplay.Render -= Render;
            On.Celeste.Level.Update -= Timing;
            On.Celeste.Level.Update -= ProcessButtons;
            On.Celeste.Level.NextLevel -= UpdateTimerStateOnNextLevel;
            On.Celeste.SummitCheckpoint.Update -= UpdateTimerStateOnTouchFlag;
            On.Celeste.LevelExit.ctor -= LevelExitOnCtor;
        }

        private void SpeedrunTimerDisplayOnUpdate(ILContext il) {
            ILCursor ilCursor = new ILCursor(il);
            if (ilCursor.TryGotoNext(MoveType.After,
                ins => ins.OpCode == OpCodes.Ldarg_0,
                ins => ins.OpCode == OpCodes.Ldarg_0,
                ins => ins.MatchLdfld<SpeedrunTimerDisplay>("DrawLerp"),
                ins => ins.OpCode == OpCodes.Ldloc_1
            )) {
                ilCursor.EmitDelegate<Func<bool, bool>>(showTimer => showTimer || SpeedrunToolModule.Settings.RoomTimerType != RoomTimerType.Off);
            }
        }

        private void ProcessButtons(On.Celeste.Level.orig_Update orig, Level self) {
            orig(self);
            if (!SpeedrunToolModule.Enabled || self.Paused) {
                return;
            }

            if (Mappings.ResetRoomPb.Pressed() && !self.Paused) {
                Mappings.ResetRoomPb.ConsumePress();
                ClearPbTimes();
            }

            if (Mappings.SwitchRoomTimer.Pressed() && !self.Paused) {
                Mappings.SwitchRoomTimer.ConsumePress();
                RoomTimerType roomTimerType = SpeedrunToolModule.Settings.RoomTimerType;
                SwitchRoomTimer(((int) roomTimerType + 1) % Enum.GetNames(typeof(RoomTimerType)).Length);
                SpeedrunToolModule.Instance.SaveSettings();
            }

            if (Mappings.SetEndPoint.Pressed() && !self.Paused) {
                Mappings.SetEndPoint.ConsumePress();
                ClearPbTimes();
                CreateEndPoint(self);
            }

            if (Mappings.SetAdditionalEndPoint.Pressed() && !self.Paused) {
                Mappings.SetAdditionalEndPoint.ConsumePress();
                if (!EndPoint.IsExist) {
                    ClearPbTimes();
                }

                CreateEndPoint(self, true);
            }
        }

        private void CreateEndPoint(Level level, bool additional = false) {
            if (level.GetPlayer() is Player player && !player.Dead) {
                if (!additional) {
                    EndPoint.All.ForEach(point => point.RemoveSelf());
                }

                level.Add(new EndPoint(player));
            }
        }

        public void SwitchRoomTimer(int index) {
            SpeedrunToolSettings speedrunToolSettings = SpeedrunToolModule.Settings;
            speedrunToolSettings.RoomTimer = SpeedrunToolSettings.RoomTimerStrings[index];

            if (speedrunToolSettings.RoomTimerType != RoomTimerType.Off) {
                return;
            }

            ClearPbTimes();
        }

        public void ClearPbTimes(bool clearEndPoint = true) {
            nextRoomTimerData.Clear();
            currentRoomTimerData.Clear();
            if (clearEndPoint) {
                EndPoint.All.ForEach(point => point.RemoveSelf());
            }
        }

        private void Timing(On.Celeste.Level.orig_Update orig, Level self) {
            if (SpeedrunToolModule.LivesplitMode) {
                var currentRoom = Celeste.Instance.AutoSplitterInfo.Level;
                if (previousRoom != null && previousRoom != currentRoom)
                    UpdateTimerState();

                previousRoom = currentRoom;
            } else {
                if (!self.Completed && self.TimerStarted) {
                    nextRoomTimerData.Timing(self);
                    currentRoomTimerData.Timing(self);
                } else if (self.Completed) {
                    UpdateTimerState();
                }
            }

            orig(self);
        }

        private void UpdateTimerStateOnNextLevel(On.Celeste.Level.orig_NextLevel orig, Level self, Vector2 at,
            Vector2 dir) {
            orig(self, at, dir);
            if (!SpeedrunToolModule.LivesplitMode)
                UpdateTimerState();
        }

        private void UpdateTimerStateOnTouchFlag(On.Celeste.SummitCheckpoint.orig_Update orig, SummitCheckpoint self) {
            bool lastActivated = self.Activated;
            orig(self);
            if (!SpeedrunToolModule.LivesplitMode && !lastActivated && self.Activated) {
                UpdateTimerState();
            }
        }

        private void LevelExitOnCtor(On.Celeste.LevelExit.orig_ctor orig, LevelExit self, LevelExit.Mode mode, Session session, HiresSnow snow) {
            orig(self, mode, session, snow);
            previousRoom = null;
            if (mode == LevelExit.Mode.Restart && !StateManager.Instance.IsSaved) {
                SpeedrunToolModule.Settings.RoomTimer = SpeedrunToolSettings.RoomTimerStrings.First();
            }
        }

        public void UpdateTimerState(bool endPoint = false) {
            switch (SpeedrunToolModule.Settings.RoomTimerType) {
                case RoomTimerType.NextRoom:
                    nextRoomTimerData.UpdateTimerState(endPoint);
                    break;
                case RoomTimerType.CurrentRoom:
                    currentRoomTimerData.UpdateTimerState(endPoint);
                    break;
                case RoomTimerType.Off:
                    break;
                default:
                    throw new ArgumentOutOfRangeException();
            }
        }

        public void ResetTime() {
            nextRoomTimerData.ResetTime();
            currentRoomTimerData.ResetTime();
        }

        private void Render(On.Celeste.SpeedrunTimerDisplay.orig_Render orig, SpeedrunTimerDisplay self) {
            SpeedrunToolSettings settings = SpeedrunToolModule.Settings;
            if (!settings.Enabled || settings.RoomTimerType == RoomTimerType.Off || self.DrawLerp <= 0f) {
                orig(self);
                return;
            }

            RoomTimerType roomTimeType = SpeedrunToolModule.Settings.RoomTimerType;
            RoomTimerData roomTimerData = roomTimeType == RoomTimerType.NextRoom ? nextRoomTimerData : currentRoomTimerData;

            string roomTimeString = roomTimerData.TimeString;
            string pbTimeString = roomTimerData.PbTimeString;
            pbTimeString = "PB " + pbTimeString;

            const float topBlackBarWidth = 32f;
            const float topTimeHeight = 38f;
            const float pbWidth = 100;
            const float timeMarginLeft = 32f;
            const float pbScale = 0.6f;

            MTexture bg = GFX.Gui["strawberryCountBG"];
            float x = -300f * Ease.CubeIn(1f - self.DrawLerp);

            Draw.Rect(x, self.Y, topBlackBarWidth + 2, topTimeHeight, Color.Black);
            bg.Draw(new Vector2(x + topBlackBarWidth, self.Y));

            float roomTimeScale = 1f;
            if (roomTimerData.IsCompleted) {
                Wiggler wiggler = (Wiggler) self.GetFieldValue("wiggler");
                if (wiggler != null) {
                    roomTimeScale = 1f + wiggler.Value * 0.15f;
                }
            }

            SpeedrunTimerDisplay.DrawTime(new Vector2(x + timeMarginLeft, self.Y + 44f), roomTimeString, roomTimeScale,
                true,
                roomTimerData.IsCompleted, roomTimerData.BeatBestTime);

            if (roomTimerData.IsCompleted) {
                string comparePbString = ComparePb(roomTimerData.Time, roomTimerData.LastPbTime);
                DrawTime(
                    new Vector2(x + timeMarginLeft + SpeedrunTimerDisplay.GetTimeWidth(roomTimeString) + 10,
                        self.Y + 36f),
                    comparePbString, 0.5f,
                    roomTimerData.IsCompleted, roomTimerData.BeatBestTime);
            }

            // 遮住上下两块的间隙，游戏原本的问题
            Draw.Rect(x, self.Y + topTimeHeight - 1, pbWidth + bg.Width * pbScale, 1f, Color.Black);

            Draw.Rect(x, self.Y + topTimeHeight, pbWidth + 2, bg.Height * pbScale + 1f, Color.Black);
            bg.Draw(new Vector2(x + pbWidth, self.Y + topTimeHeight), Vector2.Zero, Color.White, pbScale);
            DrawTime(new Vector2(x + timeMarginLeft, (float) (self.Y + 66.4)), pbTimeString, pbScale, false, false, 0.6f);
        }

        private static string ComparePb(long time, long pbTime) {
            if (pbTime == 0) {
                return "";
            }

            long difference = time - pbTime;

            if (difference == 0) {
                return "+0.0";
            }

            TimeSpan timeSpan = TimeSpan.FromTicks(Math.Abs(difference));
            string result = difference >= 0 ? "+" : "-";
            result += (int) timeSpan.TotalSeconds + timeSpan.ToString("\\.fff");
            return result;
        }

        private static void DrawTime(Vector2 position, string timeString, float scale = 1f,
            bool finished = false, bool bestTime = false, float alpha = 1f) {
            float numberWidth = 0f;
            float spacerWidth = 0f;
            PixelFontSize pixelFontSize =
                Dialog.Languages["english"].Font.Get(Dialog.Languages["english"].FontFaceSize);
            for (int index = 0; index < 10; ++index) {
                float x1 = pixelFontSize.Measure(index.ToString()).X;
                if ((double) x1 > numberWidth) {
                    numberWidth = x1;
                }
            }

            spacerWidth = pixelFontSize.Measure('.').X;

            PixelFont font = Dialog.Languages["english"].Font;
            float fontFaceSize = Dialog.Languages["english"].FontFaceSize;
            float num1 = scale;
            float x = position.X;
            float y = position.Y;
            Color color1 = Color.White * alpha;
            Color color2 = Color.LightGray * alpha;
            if (bestTime) {
                color1 = bestColor1 * alpha;
                color2 = bestColor2 * alpha;
            } else if (finished) {
                color1 = finishedColor1 * alpha;
                color2 = finishedColor2 * alpha;
            }

            for (int index = 0; index < timeString.Length; ++index) {
                char ch = timeString[index];

                Color color3 = ch == ':' || ch == '.' || (double) num1 < (double) scale ? color2 : color1;

                float num2 = (float) ((ch == ':' || ch == '.' ? spacerWidth : numberWidth) + 4.0) * num1;
                font.DrawOutline(fontFaceSize, ch.ToString(), new Vector2(x + num2 / 2f, y), new Vector2(0.5f, 1f),
                    Vector2.One * num1, color3, 2f, Color.Black);
                x += num2;
            }
        }

    // @formatter:off
    private static readonly Lazy<RoomTimerManager> Lazy = new Lazy<RoomTimerManager>(() => new RoomTimerManager());
    public static RoomTimerManager Instance => Lazy.Value;
    private RoomTimerManager() { }
        // @formatter:on
    }
}