using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Sockets;
using Microsoft.Xna.Framework;
using Monocle;

namespace Celeste.Mod.SpeedrunTool {
    public sealed class RoomTimerManager {
        public const string FlagPrefix = "summit_checkpoint_";
        private readonly RoomTimerData currentRoomTimerData = new RoomTimerData(RoomTimerType.CurrentRoom);
        private readonly RoomTimerData nextRoomTimerData = new RoomTimerData(RoomTimerType.NextRoom);

        public SpeedrunType? OriginalSpeedrunType;

        public void Load() {
            On.Celeste.SpeedrunTimerDisplay.Render += Render;
            On.Celeste.MenuOptions.SetSpeedrunClock += SaveOriginalSpeedrunClock;
            On.Celeste.Level.Update += Timing;
            On.Celeste.Level.Update += ProcessButtons;
            On.Celeste.Level.NextLevel += UpdateTimerStateOnNextLevel;
            On.Celeste.Session.SetFlag += UpdateTimerStateOnTouchFlag;
            On.Celeste.LevelLoader.ctor += ResetTime;
        }

        public void Init() {
            OriginalSpeedrunType = Settings.Instance.SpeedrunClock;
            ButtonConfigUi.UpdateResetRoomPbButton();
            ButtonConfigUi.UpdateSetEndPointButton();
        }

        public void Unload() {
            On.Celeste.SpeedrunTimerDisplay.Render -= Render;
            On.Celeste.MenuOptions.SetSpeedrunClock -= SaveOriginalSpeedrunClock;
            On.Celeste.Level.Update -= Timing;
            On.Celeste.Level.NextLevel -= UpdateTimerStateOnNextLevel;
            On.Celeste.Session.SetFlag -= UpdateTimerStateOnTouchFlag;
            On.Celeste.LevelLoader.ctor -= ResetTime;
        }

        private void ProcessButtons(On.Celeste.Level.orig_Update orig, Level self) {
            orig(self);

            if (ButtonConfigUi.ResetRoomPbButton.Value.Pressed && !self.Paused) {
                ButtonConfigUi.ResetRoomPbButton.Value.ConsumePress();
                ClearPbTimes();
            }

            if (ButtonConfigUi.SetEndPointButton.Value.Pressed && !self.Paused) {
                ButtonConfigUi.SetEndPointButton.Value.ConsumePress();
                SetEndPoint(self);
            }
        }

        private void SetEndPoint(Level level) {
            if (level.Tracker.GetEntity<Player>() is Player player && !player.Dead) {
                level.Add(new EndPoint(player));
            }
        }

        internal class EndPoint : Entity {
            private PlayerSprite playerSprite;
            private PlayerHair playerHair;

            public EndPoint(Player player) {
                // player.normalHitbox;
                Collider = new Hitbox(8f, 11f, -4f, -11f);
                Position = player.Position;
                Depth = player.Depth + 1;

                playerSprite = new PlayerSprite(player.Sprite.Mode) {
                    Position = player.Sprite.Position,
                    Rotation = player.Sprite.Rotation,
                    HairCount = player.Sprite.HairCount,
                    Color = player.Sprite.Color,
                    Scale = player.Sprite.Scale,
                    Rate = player.Sprite.Rate,
                    Justify = player.Sprite.Justify
                };
                playerSprite.Scale.X = playerSprite.Scale.X * (float) player.Facing;

                playerHair = new PlayerHair(playerSprite) {
                    Color = player.Hair.Color,
                    Alpha = player.Hair.Alpha,
                    Facing = player.Facing,
                    SimulateMotion = player.Hair.SimulateMotion,
                    StepYSinePerSegment = player.Hair.StepYSinePerSegment,
                    Border = player.Hair.Border,
                    DrawPlayerSpriteOutline = player.Hair.DrawPlayerSpriteOutline
                };
                Vector2[] nodes = new Vector2[player.Hair.Nodes.Count];
                player.Hair.Nodes.CopyTo(nodes);
                playerHair.Nodes = nodes.ToList();

                Add(playerHair);
                Add(playerSprite);

                try {
                    playerSprite.Play(player.Sprite.CurrentAnimationID);
                    playerSprite.SetAnimationFrame(player.Sprite.CurrentAnimationFrame);
                }
                // ReSharper disable once EmptyGeneralCatchClause
                catch (Exception) { }
                
                Add(new PlayerCollider(OnCollidePlayer));
            }

            private void OnCollidePlayer(Player player) {
                // TODO: Set End Point WIP
                Instance.UpdateTimerState();
            }

            public override void Render() {
                base.Render();
                playerHair.Active = false;
                playerSprite.Active = false;
            }
        }

        public void ClearPbTimes() {
            nextRoomTimerData.Clear();
            currentRoomTimerData.Clear();
        }

        private void Timing(On.Celeste.Level.orig_Update orig, Level self) {
            if (!self.Completed && self.TimerStarted) {
                nextRoomTimerData.Timing(self.Session);
                currentRoomTimerData.Timing(self.Session);
            }
            else if (self.Completed) {
                UpdateTimerState();
            }

            orig(self);
        }

        private void UpdateTimerStateOnNextLevel(On.Celeste.Level.orig_NextLevel orig, Level self, Vector2 at,
            Vector2 dir) {
            orig(self, at, dir);
            UpdateTimerState();
        }

        private void UpdateTimerStateOnTouchFlag(On.Celeste.Session.orig_SetFlag origSetFlag, Session session,
            string flag, bool setTo) {
            origSetFlag(session, flag, setTo);

            // 似乎通过地图选择旗子作为传送点会预设旗子，所以从第二面碰到的旗子开始才改变计时状态
            // F1 F2 F3 因为有保存旗子状态所以不受影响
            if (flag.StartsWith(FlagPrefix) && setTo && session.Flags.Count(input => input.StartsWith(FlagPrefix)) >= 2) {
                UpdateTimerState();
            }
        }

        private void UpdateTimerState() {
            switch (SpeedrunToolModule.Settings.RoomTimerType) {
                case RoomTimerType.NextRoom:
                    nextRoomTimerData.UpdateTimerState();
                    break;
                case RoomTimerType.CurrentRoom:
                    currentRoomTimerData.UpdateTimerState();
                    break;
                case RoomTimerType.Off:
                    break;
                default:
                    throw new ArgumentOutOfRangeException();
            }
        }

        private void ResetTime(On.Celeste.LevelLoader.orig_ctor orig, LevelLoader self, Session session,
            Vector2? startPosition) {
            orig(self, session, startPosition);

            nextRoomTimerData.ResetTime();
            currentRoomTimerData.ResetTime();
        }

        private void Render(On.Celeste.SpeedrunTimerDisplay.orig_Render orig, SpeedrunTimerDisplay self) {
            SpeedrunToolModuleSettings settings = SpeedrunToolModule.Settings;
            if (!settings.Enabled || settings.RoomTimerType == RoomTimerType.Off) {
                if (OriginalSpeedrunType != null) {
                    Settings.Instance.SpeedrunClock = (SpeedrunType) OriginalSpeedrunType;
                }

                orig(self);
                return;
            }

            // 强制显示时间
            Settings.Instance.SpeedrunClock = SpeedrunType.File;

            RoomTimerType roomTimeType = SpeedrunToolModule.Settings.RoomTimerType;

            RoomTimerData roomTimerData = roomTimeType == RoomTimerType.NextRoom
                ? nextRoomTimerData
                : currentRoomTimerData;

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
                Wiggler wiggler = (Wiggler) self.GetPrivateField("wiggler");
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
                    comparePbString, 0.5f, true,
                    roomTimerData.IsCompleted, roomTimerData.BeatBestTime);
            }

            // 遮住上下两块的间隙，游戏原本的问题
            Draw.Rect(x, self.Y + topTimeHeight - 1, pbWidth + bg.Width * pbScale, 1f, Color.Black);

            Draw.Rect(x, self.Y + topTimeHeight, pbWidth + 2, bg.Height * pbScale + 1f, Color.Black);
            bg.Draw(new Vector2(x + pbWidth, self.Y + topTimeHeight), Vector2.Zero, Color.White, pbScale);
            DrawTime(new Vector2(x + timeMarginLeft, (float) (self.Y + 66.4)), pbTimeString, pbScale,
                true, false, false, 0.6f);
        }

        private void SaveOriginalSpeedrunClock(On.Celeste.MenuOptions.orig_SetSpeedrunClock orig, int val) {
            OriginalSpeedrunType = (SpeedrunType) val;
            orig(val);
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

        private static void DrawTime(Vector2 position, string timeString, float scale = 1f, bool valid = true,
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
            if (!valid) {
                color1 = Calc.HexToColor("918988") * alpha;
                color2 = Calc.HexToColor("7a6f6d") * alpha;
            }
            else if (bestTime) {
                color1 = Calc.HexToColor("fad768") * alpha;
                color2 = Calc.HexToColor("cfa727") * alpha;
            }
            else if (finished) {
                color1 = Calc.HexToColor("6ded87") * alpha;
                color2 = Calc.HexToColor("43d14c") * alpha;
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

    internal class RoomTimerData {
        public long LastPbTime;
        public long Time;

        private readonly Dictionary<string, long> pbTimes = new Dictionary<string, long>();
        private readonly RoomTimerType roomTimerType;
        private int numberOfRooms;
        private string pbTimeKey = "";
        private TimerState timerState;

        public RoomTimerData(RoomTimerType roomTimerType) {
            this.roomTimerType = roomTimerType;
            ResetTime();
        }

        private bool IsNextRoomType => roomTimerType == RoomTimerType.NextRoom;
        public string TimeString => FormatTime(Time, false);
        private long PbTime => pbTimes.GetValueOrDefault(pbTimeKey, 0);
        public string PbTimeString => FormatTime(PbTime, true);
        public bool IsCompleted => timerState == TimerState.Completed;
        public bool BeatBestTime => timerState == TimerState.Completed && (Time < LastPbTime || LastPbTime == 0);

        public void Timing(Session session) {
            if (timerState != TimerState.Timing) {
                return;
            }

            if (pbTimeKey == "") {
                pbTimeKey = session.Area + session.Level;
                string closestFlag = session.Flags.Where(flagName => flagName.StartsWith(RoomTimerManager.FlagPrefix))
                    .OrderBy(flagName => {
                        flagName = flagName.Replace(RoomTimerManager.FlagPrefix, "");
                        return int.Parse(flagName);
                    }).FirstOrDefault();
                pbTimeKey += closestFlag;
                pbTimeKey += numberOfRooms;
            }

            Time += TimeSpan.FromSeconds(Engine.RawDeltaTime).Ticks;
        }

        public void UpdateTimerState() {
            switch (timerState) {
                case TimerState.WaitToStart:
                    timerState = TimerState.Timing;
                    numberOfRooms = SpeedrunToolModule.Settings.NumberOfRooms;
                    break;
                case TimerState.Timing:
                    if (numberOfRooms <= 1) {
                        timerState = TimerState.Completed;
                        LastPbTime = pbTimes.GetValueOrDefault(pbTimeKey, 0);
                        if (Time < LastPbTime || LastPbTime == 0) {
                            pbTimes[pbTimeKey] = Time;
                        }
                    }
                    else {
                        numberOfRooms--;
                    }

                    break;
                case TimerState.Completed:
                    break;
                default:
                    throw new ArgumentOutOfRangeException();
            }
        }

        public void ResetTime() {
            pbTimeKey = "";
            timerState = IsNextRoomType ? TimerState.WaitToStart : TimerState.Timing;
            numberOfRooms = SpeedrunToolModule.Settings.NumberOfRooms;
            Time = 0;
            LastPbTime = 0;
        }

        public void Clear() {
            ResetTime();
            pbTimes.Clear();
        }

        private static string FormatTime(long time, bool isPbTime) {
            if (time == 0 && isPbTime) {
                return "";
            }

            TimeSpan timeSpan = TimeSpan.FromTicks(time);
            return (int) timeSpan.TotalSeconds + timeSpan.ToString("\\.fff");
        }
    }

    internal enum TimerState {
        WaitToStart,
        Timing,
        Completed
    }
}