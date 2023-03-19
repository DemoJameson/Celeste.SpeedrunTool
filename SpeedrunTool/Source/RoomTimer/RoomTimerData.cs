using System.Collections.Generic;
using Celeste.Mod.SpeedrunTool.SaveLoad;

namespace Celeste.Mod.SpeedrunTool.RoomTimer;

internal class RoomTimerData {
    private long lastPbTime;
    private long time;
    private long lastBestSegment;
    private long prevRoomTime;

    public Dictionary<string, long> ThisRunTimes { get; private set; } = new();
    public Dictionary<string, long> PbTimes { get; private set; } = new();
    private readonly Dictionary<string, long> lastPbTimes = new();
    public Dictionary<string, long> BestSegments { get; private set; } = new();
    private readonly RoomTimerType roomTimerType;
    private int roomNumber;
    public string TimeKeyPrefix { get; private set; } = "";
    private string thisRunTimeKey = "";
    private string pbTimeKey = "";
    private string thisRunPrevRoomTimeKey = "";
    private TimerState timerState;
    private bool hitEndPoint = false;
    private float displayGoldRenderTime = 0f;
    private const float DisplayGoldRenderDelay = 0.68f;

    public RoomTimerData(RoomTimerType roomTimerType) {
        this.roomTimerType = roomTimerType;
        ResetTime();
    }

    public long GetSelectedRoomTime => IsCompleted ? ThisRunTimes.GetValueOrDefault(pbTimeKey, 0) : time;
    public long GetSelectedPbTime => PbTimes.GetValueOrDefault(pbTimeKey, 0);
    public long GetSelectedLastPbTime => lastPbTimes.GetValueOrDefault(pbTimeKey, 0);
    public string TimeString => FormatTime(GetSelectedRoomTime, false);
    public string PbTimeString => FormatTime(GetSelectedPbTime, true);
    private bool IsNextRoomType => roomTimerType == RoomTimerType.NextRoom;
    public bool IsCompleted => timerState == TimerState.Completed;
    public bool BeatBestTime => (IsCompleted && (GetSelectedRoomTime < GetSelectedLastPbTime || GetSelectedLastPbTime == 0)) || 
        (displayGoldRenderTime > 0f && timerState is TimerState.Timing && ModSettings.DisplayRoomGold);

    private void UpdateTimeKeys(Level level) {
        if (TimeKeyPrefix == "") {
            Session session = level.Session;
            TimeKeyPrefix = session.Area + session.Level;
        }

        if (!EndPoint.IsExist) {
            pbTimeKey = TimeKeyPrefix + ModSettings.NumberOfRooms;
            thisRunTimeKey = TimeKeyPrefix + roomNumber;
            thisRunPrevRoomTimeKey = TimeKeyPrefix + (roomNumber - 1);
        } else {
            pbTimeKey = TimeKeyPrefix + "EndPoint";
            thisRunTimeKey = TimeKeyPrefix + "EndPoint";
            thisRunPrevRoomTimeKey = TimeKeyPrefix + "EndPoint";
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
        if (roomNumber > ModSettings.NumberOfRooms && !EndPoint.IsExist || hitEndPoint || level is {Completed: true}) {
            timerState = TimerState.Completed;
        } else {
            timerState = TimerState.Timing;
        }

        if (displayGoldRenderTime > 0f) {
            displayGoldRenderTime -= Engine.RawDeltaTime;
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
                // if not using endpoint/room id, track this run's time, pb times, and best segments for each room number
                if (!EndPoint.IsExist) {
                    ThisRunTimes[thisRunTimeKey] = time;
                    lastPbTime = PbTimes.GetValueOrDefault(thisRunTimeKey, 0);
                    if (time < lastPbTime || lastPbTime == 0) {
                        PbTimes[thisRunTimeKey] = time;
                    }

                    lastBestSegment = BestSegments.GetValueOrDefault(thisRunTimeKey, 0);
                    prevRoomTime = ThisRunTimes.GetValueOrDefault(thisRunPrevRoomTimeKey, 0);
                    if (time - prevRoomTime < lastBestSegment || lastBestSegment == 0) {
                        BestSegments[thisRunTimeKey] = time - prevRoomTime;
                        displayGoldRenderTime = DisplayGoldRenderDelay;
                    }

                    // don't overflow room number at level end
                    if (level is {Completed: false}) {
                        roomNumber++;
                    }

                    if (roomNumber >= ModSettings.NumberOfRooms || level is {Completed: true}) {
                        timerState = TimerState.Completed;
                    }

                    // preserve behavior of reporting the finish time on level end even if number of rooms is too large
                    if (level is {Completed: true} && roomNumber < ModSettings.NumberOfRooms) {
                        ThisRunTimes[pbTimeKey] = time;
                        lastPbTime = PbTimes.GetValueOrDefault(pbTimeKey, 0);
                        if (time < lastPbTime || lastPbTime == 0) {
                            PbTimes[pbTimeKey] = time;
                        }

                        lastBestSegment = BestSegments.GetValueOrDefault(pbTimeKey, 0);
                        prevRoomTime = ThisRunTimes.GetValueOrDefault(thisRunPrevRoomTimeKey, 0);
                        if (time - prevRoomTime < lastBestSegment || lastBestSegment == 0) {
                            BestSegments[pbTimeKey] = time - prevRoomTime;
                            displayGoldRenderTime = DisplayGoldRenderDelay;
                        }
                    }
                } else if (endPoint || level is {Completed: true} || EndPoint.IsReachedRoomIdEndPoint) {
                    // if using endpoint/room id, ignore room count and only track a single complete time, pb time and best segment
                    ThisRunTimes[thisRunTimeKey] = time;
                    lastPbTime = PbTimes.GetValueOrDefault(thisRunTimeKey, 0);
                    if (time < lastPbTime || lastPbTime == 0) {
                        PbTimes[thisRunTimeKey] = time;
                    }

                    lastBestSegment = BestSegments.GetValueOrDefault(thisRunTimeKey, 0);
                    prevRoomTime = ThisRunTimes.GetValueOrDefault(thisRunPrevRoomTimeKey, 0);
                    if (time - prevRoomTime < lastBestSegment || lastBestSegment == 0) {
                        BestSegments[thisRunTimeKey] = time - prevRoomTime;
                        displayGoldRenderTime = DisplayGoldRenderDelay;
                    }

                    timerState = TimerState.Completed;
                    hitEndPoint = true;
                    if (level is {Completed: false} && ModSettings.RoomTimerType == roomTimerType) {
                        EndPoint.AllStopTime();
                    }
                }

                break;
            case TimerState.Completed:
                // if not using endpoint/room id, still track room times in the background
                if (!EndPoint.IsExist) {
                    ThisRunTimes[thisRunTimeKey] = time;
                    // don't overflow room number at level end
                    if (level is {Completed: false}) {
                        roomNumber++;
                    }

                    lastPbTime = PbTimes.GetValueOrDefault(thisRunTimeKey, 0);
                    if (time < lastPbTime || lastPbTime == 0) {
                        PbTimes[thisRunTimeKey] = time;
                    }

                    lastBestSegment = BestSegments.GetValueOrDefault(thisRunTimeKey, 0);
                    prevRoomTime = ThisRunTimes.GetValueOrDefault(thisRunPrevRoomTimeKey, 0);
                    if (time - prevRoomTime < lastBestSegment || lastBestSegment == 0) {
                        BestSegments[thisRunTimeKey] = time - prevRoomTime;
                        displayGoldRenderTime = DisplayGoldRenderDelay;
                    }
                }

                break;
            default:
                throw new ArgumentOutOfRangeException();
        }
    }

    public void ResetTime() {
        foreach (KeyValuePair<string, long> timePair in PbTimes) {
            lastPbTimes[timePair.Key] = timePair.Value;
        }

        TimeKeyPrefix = "";
        thisRunTimeKey = "";
        pbTimeKey = "";
        thisRunPrevRoomTimeKey = "";
        timerState = IsNextRoomType ? TimerState.WaitToStart : TimerState.Timing;
        roomNumber = 1;
        time = 0;
        lastPbTime = 0;
        lastBestSegment = 0;
        prevRoomTime = 0;
        displayGoldRenderTime = 0f;
        ThisRunTimes.Clear();
        hitEndPoint = false;
    }

    public void Clear() {
        ResetTime();
        PbTimes.Clear();
        lastPbTimes.Clear();
        BestSegments.Clear();
    }

    public static string FormatTime(long time, bool isPbTime) {
        if (time == 0 && isPbTime) {
            return "";
        }

        TimeSpan timeSpan = TimeSpan.FromTicks(time);
        return timeSpan.ToString(timeSpan.TotalSeconds < 60 ? "s\\.fff" : "m\\:ss\\.fff");
    }
}