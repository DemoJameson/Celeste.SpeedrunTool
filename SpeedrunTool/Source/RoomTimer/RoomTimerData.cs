using System.Collections.Generic;
using System.Linq;

namespace Celeste.Mod.SpeedrunTool.RoomTimer;

internal class RoomTimerData {
    public long LastPbTime;
    public long Time;

    private readonly Dictionary<string, long> thisRunTimes = new();
    private readonly Dictionary<string, long> pbTimes = new();
    private readonly Dictionary<string, long> lastPbTimes = new();
    private readonly RoomTimerType roomTimerType;
    private int roomNumber;
    private string timeKeyPrefix = "";
    private string thisRunTimeKey = "";
    private string pbTimeKey = "";
    private TimerState timerState;
    private bool hitEndPoint = false;

    public RoomTimerData(RoomTimerType roomTimerType) {
        this.roomTimerType = roomTimerType;
        ResetTime();
    }

    public long GetSelectedRoomTime => IsCompleted ? thisRunTimes[pbTimeKey] : Time;
    public long GetSelectedPbTime => pbTimes.GetValueOrDefault(pbTimeKey, 0);
    public long GetSelectedLastPbTime => lastPbTimes.GetValueOrDefault(pbTimeKey, 0);
    private bool IsNextRoomType => roomTimerType == RoomTimerType.NextRoom;
    public string TimeString => FormatTime(GetSelectedRoomTime, false);
    public string PbTimeString => FormatTime(GetSelectedPbTime, true);
    public bool IsCompleted => timerState == TimerState.Completed;
    public bool BeatBestTime => IsCompleted && (GetSelectedRoomTime < GetSelectedLastPbTime || GetSelectedLastPbTime == 0);

    public void UpdateTimeKeys(Level level) {
        if (timeKeyPrefix == "") {
            Session session = level.Session;
            timeKeyPrefix = session.Area + session.Level;
            string closestFlag = session.Flags.Where(flagName => flagName.StartsWith(RoomTimerManager.FlagPrefix))
                .OrderBy(flagName => {
                    flagName = flagName.Replace(RoomTimerManager.FlagPrefix, "");
                    return int.Parse(flagName);
                }).FirstOrDefault();
            timeKeyPrefix += closestFlag;
        }
        if (!EndPoint.IsExist) {
            pbTimeKey = timeKeyPrefix + ModSettings.NumberOfRooms;
            thisRunTimeKey = timeKeyPrefix + roomNumber;
        } else {
            pbTimeKey = timeKeyPrefix + "EndPoint";
            thisRunTimeKey = timeKeyPrefix + "EndPoint";
        }
    }

    public void Timing(Level level) {
        if (level.TimerStopped || timerState == TimerState.WaitToStart) {
            return;
        }

        if (roomNumber > ModSettings.NumberOfRooms && !EndPoint.IsExist || hitEndPoint || level is { Completed: true }) {
            timerState = TimerState.Completed;
        } else {
            timerState = TimerState.Timing;
        }

        Time += TimeSpan.FromSeconds(Engine.RawDeltaTime).Ticks;
    }

    public void UpdateTimerState(bool endPoint) {
        switch (timerState) {
            case TimerState.WaitToStart:
                if (!endPoint) {
                    timerState = TimerState.Timing;
                    roomNumber = 1;
                }

                break;
            case TimerState.Timing:
                Level level = Engine.Scene as Level;

                if (!EndPoint.IsExist) {
                    thisRunTimes[thisRunTimeKey] = Time;
                    LastPbTime = pbTimes.GetValueOrDefault(thisRunTimeKey, 0);
                    if (Time < LastPbTime || LastPbTime == 0) {
                        pbTimes[thisRunTimeKey] = Time;
                    }
                    roomNumber++;
                    if (roomNumber >= ModSettings.NumberOfRooms || level is { Completed: true }) {
                        timerState = TimerState.Completed;
                    }
                } else if (endPoint) {
                    thisRunTimes[thisRunTimeKey] = Time;
                    LastPbTime = pbTimes.GetValueOrDefault(thisRunTimeKey, 0);
                    if (Time < LastPbTime || LastPbTime == 0) {
                        pbTimes[thisRunTimeKey] = Time;
                    }
                    timerState = TimerState.Completed;
                    hitEndPoint = true;
                    if (level is { Completed: false }) {
                        EndPoint.All.ForEach(point => point.StopTime());
                    }
                }
                break;
            case TimerState.Completed:
                if (!EndPoint.IsExist) {
                    thisRunTimes[thisRunTimeKey] = Time;
                    roomNumber++;
                    LastPbTime = pbTimes.GetValueOrDefault(thisRunTimeKey, 0);
                    if (Time < LastPbTime || LastPbTime == 0) {
                        pbTimes[thisRunTimeKey] = Time;
                    }
                }
                break;
            default:
                throw new ArgumentOutOfRangeException();
        }
    }

    public void ResetTime() {
        foreach (KeyValuePair<string, long> timePair in pbTimes) {
            lastPbTimes[timePair.Key] = timePair.Value;
        }
        timeKeyPrefix = "";
        thisRunTimeKey = "";
        pbTimeKey = "";
        timerState = IsNextRoomType ? TimerState.WaitToStart : TimerState.Timing;
        roomNumber = 1;
        Time = 0;
        LastPbTime = 0;
        thisRunTimes.Clear();
        hitEndPoint = false;
    }

    public void Clear() {
        ResetTime();
        pbTimes.Clear();
        lastPbTimes.Clear();
    }

    private static string FormatTime(long time, bool isPbTime) {
        if (time == 0 && isPbTime) {
            return "";
        }

        TimeSpan timeSpan = TimeSpan.FromTicks(time);
        return timeSpan.ToString(timeSpan.TotalSeconds < 60 ? "s\\.fff" : "m\\:ss\\.fff");
    }
}