using System.Collections.Generic;
using System.Linq;
using Celeste.Mod.SpeedrunTool.SaveLoad;

namespace Celeste.Mod.SpeedrunTool.RoomTimer;

internal class RoomTimerData {
    private const string FlagPrefix = "summit_checkpoint_";

    private long lastPbTime;
    private long time;

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

    public long GetSelectedRoomTime => IsCompleted ? thisRunTimes.GetValueOrDefault(pbTimeKey, 0) : time;
    public long GetSelectedPbTime => pbTimes.GetValueOrDefault(pbTimeKey, 0);
    public long GetSelectedLastPbTime => lastPbTimes.GetValueOrDefault(pbTimeKey, 0);
    public string TimeString => FormatTime(GetSelectedRoomTime, false);
    public string PbTimeString => FormatTime(GetSelectedPbTime, true);
    private bool IsNextRoomType => roomTimerType == RoomTimerType.NextRoom;
    public bool IsCompleted => timerState == TimerState.Completed;
    public bool BeatBestTime => IsCompleted && (GetSelectedRoomTime < GetSelectedLastPbTime || GetSelectedLastPbTime == 0);

    private void UpdateTimeKeys(Level level) {
        if (timeKeyPrefix == "") {
            Session session = level.Session;
            timeKeyPrefix = session.Area + session.Level;
            string closestFlag = session.Flags.Where(flagName => flagName.StartsWith(FlagPrefix))
                .OrderBy(flagName => {
                    flagName = flagName.Replace(FlagPrefix, "");
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

        time += TimeSpan.FromSeconds(Engine.RawDeltaTime).Ticks;
    }

    public void UpdateTimerState(bool endPoint) {
        if (Engine.Scene is not Level level) {
            return;
        }

        switch (timerState) {
            case TimerState.WaitToStart:
                if (!endPoint) {
                    timerState = TimerState.Timing;
                    roomNumber = 1;
                }

                break;

            case TimerState.Timing:

                // if not using endpoint/room id, track this run's time and pb times for each room number
                if (!EndPoint.IsExist) {
                    thisRunTimes[thisRunTimeKey] = time;
                    lastPbTime = pbTimes.GetValueOrDefault(thisRunTimeKey, 0);
                    if (time < lastPbTime || lastPbTime == 0) {
                        pbTimes[thisRunTimeKey] = time;
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
                        thisRunTimes[pbTimeKey] = time;
                        lastPbTime = pbTimes.GetValueOrDefault(pbTimeKey, 0);
                        if (time < lastPbTime || lastPbTime == 0) {
                            pbTimes[pbTimeKey] = time;
                        }
                    }
                }
                // if using endpoint/room id, ignore room count and only track a single complete time and pb time
                else if (endPoint || level is { Completed: true }) {
                    thisRunTimes[thisRunTimeKey] = time;
                    lastPbTime = pbTimes.GetValueOrDefault(thisRunTimeKey, 0);
                    if (time < lastPbTime || lastPbTime == 0) {
                        pbTimes[thisRunTimeKey] = time;
                    }
                    timerState = TimerState.Completed;
                    hitEndPoint = true;
                    if (level is { Completed: false }) {
                        EndPoint.AllStopTime();
                    }
                }
                break;

            case TimerState.Completed:
                // if not using endpoint/room id, still track room times in the background
                if (!EndPoint.IsExist) {
                    thisRunTimes[thisRunTimeKey] = time;
                    // don't overflow room number at level end
                    if (level is { Completed: false }) {
                        roomNumber++;
                    }
                    lastPbTime = pbTimes.GetValueOrDefault(thisRunTimeKey, 0);
                    if (time < lastPbTime || lastPbTime == 0) {
                        pbTimes[thisRunTimeKey] = time;
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
        time = 0;
        lastPbTime = 0;
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