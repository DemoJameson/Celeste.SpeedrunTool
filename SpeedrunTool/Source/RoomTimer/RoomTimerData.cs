using System.Collections.Generic;
using System.Linq;
using Celeste.Mod.SpeedrunTool.SaveLoad;

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

    public long GetSelectedRoomTime => IsCompleted ? thisRunTimes.GetValueOrDefault(pbTimeKey, 0) : Time;
    public long GetSelectedPbTime => pbTimes.GetValueOrDefault(pbTimeKey, 0);
    public long GetSelectedLastPbTime => lastPbTimes.GetValueOrDefault(pbTimeKey, 0);
    public string TimeString => FormatTime(GetSelectedRoomTime, false);
    public string PbTimeString => FormatTime(GetSelectedPbTime, true);
    private bool IsNextRoomType => roomTimerType == RoomTimerType.NextRoom;
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
        UpdateTimeKeys(level);

        if (timerState == TimerState.WaitToStart) {
            return;
        }
        if (level.Completed || !level.TimerStarted || level.TimerStopped) {
            return;
        }
        if (StateManager.Instance.State != State.None) {
            return;
        }

        // need to continually poll this condition because you can now
        // change number of timed rooms to reactivate the timer
        if (roomNumber > ModSettings.NumberOfRooms && !EndPoint.IsExist || hitEndPoint || level is { Completed: true }) {
            timerState = TimerState.Completed;
        } else {
            timerState = TimerState.Timing;
        }

        Time += TimeSpan.FromSeconds(Engine.RawDeltaTime).Ticks;
    }

    public void UpdateTimerState(bool endPoint) {
        Level level = Engine.Scene as Level;
        switch (timerState) {
            case TimerState.WaitToStart:
                if (!endPoint) {
                    timerState = TimerState.Timing;
                    roomNumber = 1;
                }
                break;

            case TimerState.Timing:

                // if not using endpoint, track this run's time and pb times for each room number
                if (!EndPoint.IsExist) {
                    thisRunTimes[thisRunTimeKey] = Time;
                    LastPbTime = pbTimes.GetValueOrDefault(thisRunTimeKey, 0);
                    if (Time < LastPbTime || LastPbTime == 0) {
                        pbTimes[thisRunTimeKey] = Time;
                    }
                    // don't overflow room number at level end
                    if (level is { Completed: false }) {
                        roomNumber++;
                    }
                    if (roomNumber >= ModSettings.NumberOfRooms || level is { Completed: true }) {
                        timerState = TimerState.Completed;
                    }
                    // preserve behavior of reporting the finish time on level end even if number of rooms is too large
                    if (level is { Completed: true } && roomNumber < ModSettings.NumberOfRooms) {
                        thisRunTimes[pbTimeKey] = Time;
                        LastPbTime = pbTimes.GetValueOrDefault(pbTimeKey, 0);
                        if (Time < LastPbTime || LastPbTime == 0) {
                            pbTimes[pbTimeKey] = Time;
                        }
                    }
                } 
                // if using endpoint, ignore room count and only track a single complete time and pb time
                else if (endPoint || level is { Completed: true }) {
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
                // if not using endpoint, still track room times in the background
                if (!EndPoint.IsExist) {
                    thisRunTimes[thisRunTimeKey] = Time;
                    // don't overflow room number at level end
                    if (level is { Completed: false }) {
                        roomNumber++;
                    }
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