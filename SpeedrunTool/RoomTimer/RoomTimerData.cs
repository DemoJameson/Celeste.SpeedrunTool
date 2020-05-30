using System;
using System.Collections.Generic;
using System.Linq;
using Celeste.Mod.SpeedrunTool.Extensions;
using Monocle;

namespace Celeste.Mod.SpeedrunTool.RoomTimer {
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

        public void UpdateTimerState(bool endPoint) {
            switch (timerState) {
                case TimerState.WaitToStart:
                    if (!endPoint) {
                        timerState = TimerState.Timing;
                        numberOfRooms = SpeedrunToolModule.Settings.NumberOfRooms;
                    }

                    break;
                case TimerState.Timing:
                    Level level = Engine.Scene as Level;
                    if (numberOfRooms <= 1 && RoomTimerManager.Instance.SavedEndPoint == null
                        || endPoint && RoomTimerManager.Instance.SavedEndPoint != null
                        || level != null && level.Completed) {
                        timerState = TimerState.Completed;
                        LastPbTime = pbTimes.GetValueOrDefault(pbTimeKey, 0);
                        if (Time < LastPbTime || LastPbTime == 0) {
                            pbTimes[pbTimeKey] = Time;
                        }

                        if (level != null && !level.Completed) {
                            RoomTimerManager.Instance.SavedEndPoint?.StopTime();
                        }
                    } else {
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
            return timeSpan.ToString(timeSpan.TotalSeconds < 60 ? "s\\.fff" : "m\\:ss\\.fff");
        }
    }
}