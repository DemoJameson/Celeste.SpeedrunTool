using System.Collections.Generic;
using System.Linq;

namespace Celeste.Mod.SpeedrunTool.RoomTimer;

internal class RoomTimerData {
    public long LastPbTime;
    public long Time;

    private readonly Dictionary<string, long> thisRunTimes = new();
    private readonly Dictionary<string, long> pbTimes = new();
    private readonly RoomTimerType roomTimerType;
    private int roomNumber;
    private string timeKeyPrefix = "";
    private string thisRunTimeKey = "";
    private string pbTimeKey = "";
    private TimerState timerState;

    public RoomTimerData(RoomTimerType roomTimerType) {
        this.roomTimerType = roomTimerType;
        ResetTime();
    }

    public long GetSelectedRoomTime => IsCompleted ? thisRunTimes[pbTimeKey] : Time;
    public long GetSelectedPbTime => PbTime;
    private bool IsNextRoomType => roomTimerType == RoomTimerType.NextRoom;
    public string TimeString => FormatTime(GetSelectedRoomTime, false);
    private long PbTime => pbTimes.GetValueOrDefault(pbTimeKey, 0);
    public string PbTimeString => FormatTime(GetSelectedPbTime, true);
    public bool IsCompleted => timerState == TimerState.Completed;
    public bool BeatBestTime => timerState == TimerState.Completed && (GetSelectedRoomTime < GetSelectedPbTime || GetSelectedPbTime == 0);

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
        pbTimeKey = timeKeyPrefix + ModSettings.NumberOfRooms;
        thisRunTimeKey = timeKeyPrefix + roomNumber;
    }

    public void Timing(Level level) {
        UpdateTimeKeys(level);

        if (level.TimerStopped || timerState == TimerState.WaitToStart) {
            return;
        }

        if (roomNumber > ModSettings.NumberOfRooms && !EndPoint.IsExist || level is { Completed: true }) {
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
                thisRunTimes[thisRunTimeKey] = Time;
                roomNumber++;

                Level level = Engine.Scene as Level;
                if (roomNumber >= ModSettings.NumberOfRooms && !EndPoint.IsExist
                        || endPoint && EndPoint.IsExist
                        || level is { Completed: true }) {
                    timerState = TimerState.Completed;

                    if (level is { Completed: false }) {
                        EndPoint.All.ForEach(point => point.StopTime());
                    }
                }

                break;
            case TimerState.Completed:
                thisRunTimes[thisRunTimeKey] = Time;
                roomNumber++;
                break;
            default:
                throw new ArgumentOutOfRangeException();
        }
    }

    public void ResetTime() {
        foreach (KeyValuePair<string, long> timePair in thisRunTimes) {
            LastPbTime = pbTimes.GetValueOrDefault(timePair.Key, 0);
            if (timePair.Value < LastPbTime || LastPbTime == 0) {
                pbTimes[timePair.Key] = timePair.Value;
            }
        }
        timeKeyPrefix = "";
        thisRunTimeKey = "";
        pbTimeKey = "";
        timerState = IsNextRoomType ? TimerState.WaitToStart : TimerState.Timing;
        roomNumber = 1;
        Time = 0;
        LastPbTime = 0;
        thisRunTimes.Clear();
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