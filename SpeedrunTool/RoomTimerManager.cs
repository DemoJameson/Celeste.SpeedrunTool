using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Xna.Framework;
using Monocle;

namespace Celeste.Mod.SpeedrunTool
{
    public sealed class RoomTimerManager
    {
        public const string FlagPrefix = "summit_checkpoint_";
        private readonly RoomTimerData _currentRoomTimerData = new RoomTimerData(RoomTimerType.CurrentRoom);

        private readonly RoomTimerData _nextRoomTimerData = new RoomTimerData(RoomTimerType.NextRoom);

        public SpeedrunType? OriginalSpeedrunType;

        public void Load()
        {
            On.Celeste.SpeedrunTimerDisplay.Render += Render;
            On.Celeste.MenuOptions.SetSpeedrunClock += SaveOriginalSpeedrunClock;
            On.Celeste.Level.Update += Timing;
            On.Celeste.Level.NextLevel += UpdateTimerStateOnNextLevel;
            On.Celeste.Session.SetFlag += UpdateTimerStateOnTouchFlag;
            On.Celeste.LevelLoader.ctor += ResetTime;
        }

        public void Init()
        {
            OriginalSpeedrunType = Settings.Instance.SpeedrunClock;
        }

        public void Unload()
        {
            On.Celeste.SpeedrunTimerDisplay.Render -= Render;
            On.Celeste.MenuOptions.SetSpeedrunClock -= SaveOriginalSpeedrunClock;
            On.Celeste.Level.Update -= Timing;
            On.Celeste.Level.NextLevel -= UpdateTimerStateOnNextLevel;
            On.Celeste.Session.SetFlag -= UpdateTimerStateOnTouchFlag;
            On.Celeste.LevelLoader.ctor -= ResetTime;
        }

        public void ClearPbTimes()
        {
            _nextRoomTimerData.Clear();
            _currentRoomTimerData.Clear();
        }

        private void Timing(On.Celeste.Level.orig_Update orig, Level self)
        {
            if (!self.Completed && self.TimerStarted)
            {
                _nextRoomTimerData.Timing(self.Session);
                _currentRoomTimerData.Timing(self.Session);
            }
            else if (self.Completed)
            {
                UpdateTimerState();
            }

            orig(self);
        }

        private void UpdateTimerStateOnNextLevel(On.Celeste.Level.orig_NextLevel orig, Level self, Vector2 at,
            Vector2 dir)
        {
            orig(self, at, dir);
            UpdateTimerState();
        }

        private void UpdateTimerStateOnTouchFlag(On.Celeste.Session.orig_SetFlag origSetFlag, Session session,
            string flag, bool setTo)
        {
            origSetFlag(session, flag, setTo);

            // 似乎通过地图选择旗子作为传送点会预设旗子，所以从第二面碰到旗子开始才改变计时状态
            // F1 F2 F3 因为有保存旗子状态所以不受影响
            if (flag.StartsWith(FlagPrefix) && setTo && session.Flags.Count(input => input.StartsWith(FlagPrefix)) >= 2)
                UpdateTimerState();
        }

        private void UpdateTimerState()
        {
            switch (SpeedrunToolModule.Settings.RoomTimerType)
            {
                case RoomTimerType.NextRoom:
                    _nextRoomTimerData.UpdateTimerState();
                    break;
                case RoomTimerType.CurrentRoom:
                    _currentRoomTimerData.UpdateTimerState();
                    break;
            }
        }

        private void ResetTime(On.Celeste.LevelLoader.orig_ctor orig, LevelLoader self, Session session,
            Vector2? startPosition)
        {
            orig(self, session, startPosition);

            _nextRoomTimerData.ResetTime();
            _currentRoomTimerData.ResetTime();
        }

        private void Render(On.Celeste.SpeedrunTimerDisplay.orig_Render orig, SpeedrunTimerDisplay self)
        {
            SpeedrunToolModuleSettings settings = SpeedrunToolModule.Settings;
            if (!settings.Enabled || settings.RoomTimerType == RoomTimerType.OFF)
            {
                orig(self);
                return;
            }

            // 强制显示时间
            Settings.Instance.SpeedrunClock = SpeedrunType.File;

            RoomTimerType roomTimeType = SpeedrunToolModule.Settings.RoomTimerType;

            RoomTimerData roomTimerData = roomTimeType == RoomTimerType.NextRoom
                ? _nextRoomTimerData
                : _currentRoomTimerData;

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
            if (roomTimerData.IsCompleted)
            {
                Wiggler wiggler = (Wiggler) self.GetPrivateField("wiggler");
                if (wiggler != null) roomTimeScale = 1f + wiggler.Value * 0.15f;
            }

            SpeedrunTimerDisplay.DrawTime(new Vector2(x + timeMarginLeft, self.Y + 44f), roomTimeString, roomTimeScale,
                true,
                roomTimerData.IsCompleted, roomTimerData.BeatBestTime);

            if (roomTimerData.IsCompleted)
            {
                string comparePbString = ComparePb(roomTimerData.Time, roomTimerData.LastPbTime);
                DrawTime(
                    new Vector2(x + timeMarginLeft + SpeedrunTimerDisplay.GetTimeWidth(roomTimeString), self.Y + 36f),
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

        private void SaveOriginalSpeedrunClock(On.Celeste.MenuOptions.orig_SetSpeedrunClock orig, int val)
        {
            OriginalSpeedrunType = (SpeedrunType) val;
            orig(val);
        }

        private static string ComparePb(long time, long pbTime)
        {
            if (pbTime == 0)
                return "";

            long difference = time - pbTime;

            TimeSpan timeSpan = TimeSpan.FromTicks(Math.Abs(difference));
            string result = difference >= 0 ? "+" : "-";
            result += (int) timeSpan.TotalSeconds + timeSpan.ToString("\\.ff");
            return result;
        }

        private static void DrawTime(Vector2 position, string timeString, float scale = 1f, bool valid = true,
            bool finished = false, bool bestTime = false, float alpha = 1f)
        {
            float numberWidth = 0f;
            float spacerWidth = 0f;
            PixelFontSize pixelFontSize =
                Dialog.Languages["english"].Font.Get(Dialog.Languages["english"].FontFaceSize);
            for (int index = 0; index < 10; ++index)
            {
                float x1 = pixelFontSize.Measure(index.ToString()).X;
                if ((double) x1 > numberWidth)
                    numberWidth = x1;
            }

            spacerWidth = pixelFontSize.Measure('.').X;

            PixelFont font = Dialog.Languages["english"].Font;
            float fontFaceSize = Dialog.Languages["english"].FontFaceSize;
            float num1 = scale;
            float x = position.X;
            float y = position.Y;
            Color color1 = Color.White * alpha;
            Color color2 = Color.LightGray * alpha;
            if (!valid)
            {
                color1 = Calc.HexToColor("918988") * alpha;
                color2 = Calc.HexToColor("7a6f6d") * alpha;
            }
            else if (bestTime)
            {
                color1 = Calc.HexToColor("fad768") * alpha;
                color2 = Calc.HexToColor("cfa727") * alpha;
            }
            else if (finished)
            {
                color1 = Calc.HexToColor("6ded87") * alpha;
                color2 = Calc.HexToColor("43d14c") * alpha;
            }

            for (int index = 0; index < timeString.Length; ++index)
            {
                char ch = timeString[index];

                Color color3 = ch == ':' || ch == '.' || (double) num1 < (double) scale ? color2 : color1;

                float num2 = (float) ((ch == ':' || ch == '.' ? spacerWidth : numberWidth) + 4.0) * num1;
                font.DrawOutline(fontFaceSize, ch.ToString(), new Vector2(x + num2 / 2f, y), new Vector2(0.5f, 1f),
                    Vector2.One * num1, color3, 2f, Color.Black);
                x += num2;
            }
        }

        // @formatter:off
        private RoomTimerManager() { }
        public static RoomTimerManager Instance { get; } = new RoomTimerManager();
        // @formatter:on
    }

    internal class RoomTimerData
    {
        private readonly Dictionary<string, long> _pbTimes = new Dictionary<string, long>();
        private readonly RoomTimerType _roomTimerType;
        private string _pbTimeKey = "";
        public long LastPbTime;

        public long Time;
        private TimerState _timerState;

        public RoomTimerData(RoomTimerType roomTimerType)
        {
            _roomTimerType = roomTimerType;
            ResetTime();
        }

        private bool IsNextRoomType => _roomTimerType == RoomTimerType.NextRoom;
        public string TimeString => FormatTime(Time, false);
        public long PbTime => _pbTimes.GetValueOrDefault(_pbTimeKey, 0);
        public string PbTimeString => FormatTime(PbTime, true);
        public bool IsCompleted => _timerState == TimerState.Completed;
        public bool BeatBestTime => _timerState == TimerState.Completed && Time == PbTime;

        public void Timing(Session session)
        {
            if (_timerState != TimerState.Timing)
                return;

            if (_pbTimeKey == "")
            {
                _pbTimeKey = session.Area + session.Level;
                string closestFlag = session.Flags.Where(flagName => flagName.StartsWith(RoomTimerManager.FlagPrefix))
                    .OrderBy(flagName =>
                    {
                        flagName = flagName.Replace(RoomTimerManager.FlagPrefix, "");
                        return int.Parse(flagName);
                    }).FirstOrDefault();
                _pbTimeKey += closestFlag;
            }

            Time += TimeSpan.FromSeconds(Engine.RawDeltaTime).Ticks;
        }

        public void UpdateTimerState()
        {
            switch (_timerState)
            {
                case TimerState.WaitToStart:
                    _timerState = TimerState.Timing;
                    break;
                case TimerState.Timing:
                    _timerState = TimerState.Completed;
                    LastPbTime = _pbTimes.GetValueOrDefault(_pbTimeKey, 0);
                    if (Time < LastPbTime || LastPbTime == 0)
                        _pbTimes[_pbTimeKey] = Time;
                    break;
            }
        }

        public void ResetTime()
        {
            _pbTimeKey = "";
            _timerState = IsNextRoomType ? TimerState.WaitToStart : TimerState.Timing;
            Time = 0;
            LastPbTime = 0;
        }

        public void Clear()
        {
            ResetTime();
            _pbTimes.Clear();
        }

        private static string FormatTime(long time, bool isPbTime)
        {
            if (time == 0 && isPbTime)
                return "";

            TimeSpan timeSpan = TimeSpan.FromTicks(time);
            return (int) timeSpan.TotalSeconds + timeSpan.ToString("\\.fff");
        }
    }

    internal enum TimerState
    {
        WaitToStart,
        Timing,
        Completed
    }
}