using Celeste.Editor;
using Celeste.Mod.SpeedrunTool.Message;
using Celeste.Mod.SpeedrunTool.Other;
using Mono.Cecil.Cil;
using MonoMod.Cil;
using System.IO;
using System.Text;

namespace Celeste.Mod.SpeedrunTool.RoomTimer;

internal enum TimerState {
    WaitToStart,
    Timing,
    Completed
}

public enum RoomTimerType {
    Off,
    NextRoom,
    CurrentRoom
}

public enum RoomTimerExportType {
    Clipboard,
    File
}

public static class RoomTimerManager {
    private static readonly Color BestColor1 = Calc.HexToColor("fad768");
    private static readonly Color BestColor2 = Calc.HexToColor("cfa727");
    private static readonly Color FinishedColor1 = Calc.HexToColor("6ded87");
    private static readonly Color FinishedColor2 = Calc.HexToColor("43d14c");

    private static readonly RoomTimerData CurrentRoomTimerData = new(RoomTimerType.CurrentRoom);
    private static readonly RoomTimerData NextRoomTimerData = new(RoomTimerType.NextRoom);

    private static string previousRoom;

    [Load]
    private static void Load() {
        IL.Celeste.SpeedrunTimerDisplay.Update += SpeedrunTimerDisplayOnUpdate;
        On.Celeste.SpeedrunTimerDisplay.Render += Render;
        On.Celeste.Level.Update += Timing;
        On.Celeste.SummitCheckpoint.Update += UpdateTimerStateOnTouchFlag;
        On.Celeste.HeartGem.RegisterAsCollected += HeartGemOnRegisterAsCollected;
        On.Celeste.SaveData.RegisterCassette += SaveDataOnRegisterCassette;
        On.Celeste.LevelExit.ctor += LevelExitOnCtor;
        IL.Celeste.AutoSplitterInfo.Update += OverwriteAutosplitterChapterTime;
        IL.Celeste.TotalStrawberriesDisplay.Update += MoveStrawberryDisplayDown;
        TryTurnOffRoomTimer();
        RegisterHotkeys();
    }

    [Unload]
    private static void Unload() {
        IL.Celeste.SpeedrunTimerDisplay.Update -= SpeedrunTimerDisplayOnUpdate;
        On.Celeste.SpeedrunTimerDisplay.Render -= Render;
        On.Celeste.Level.Update -= Timing;
        On.Celeste.SummitCheckpoint.Update -= UpdateTimerStateOnTouchFlag;
        On.Celeste.HeartGem.RegisterAsCollected -= HeartGemOnRegisterAsCollected;
        On.Celeste.SaveData.RegisterCassette -= SaveDataOnRegisterCassette;
        On.Celeste.LevelExit.ctor -= LevelExitOnCtor;
        IL.Celeste.AutoSplitterInfo.Update -= OverwriteAutosplitterChapterTime;
        IL.Celeste.TotalStrawberriesDisplay.Update -= MoveStrawberryDisplayDown;
    }

    private static void RegisterHotkeys() {
        Hotkey.ResetRoomTimerPb.RegisterPressedAction(scene => {
            if (scene is Level {Paused: false} or MapEditor) {
                ClearPbTimes();
                PopupMessageUtils.Show(DialogIds.ResetRoomTimerPbTooltip.DialogClean(), DialogIds.ResetRoomTimerPbDialog);
            }
        });

        Hotkey.SwitchRoomTimer.RegisterPressedAction(scene => {
            if (scene is Level {Paused: false}) {
                SwitchRoomTimer((RoomTimerType)(((int)ModSettings.RoomTimerType + 1) % Enum.GetNames(typeof(RoomTimerType)).Length));
                PopupMessageUtils.ShowOptionState(DialogIds.RoomTimer.DialogClean(), ModSettings.RoomTimerType.DialogClean());
            }
        });

        Hotkey.IncreaseTimedRooms.RegisterPressedAction(scene => {
            if (scene is Level {Paused: false}) {
                if (ModSettings.NumberOfRooms < 99) {
                    ModSettings.NumberOfRooms++;
                    SpeedrunToolModule.Instance.SaveSettings();
                }

                PopupMessageUtils.ShowOptionState(DialogIds.NumberOfRooms.DialogClean(), ModSettings.NumberOfRooms.ToString());
            }
        });

        Hotkey.DecreaseTimedRooms.RegisterPressedAction(scene => {
            if (scene is Level { Paused: false }) {
                if (ModSettings.NumberOfRooms > 1) {
                    ModSettings.NumberOfRooms--;
                    SpeedrunToolModule.Instance.SaveSettings();
                }

                PopupMessageUtils.ShowOptionState(DialogIds.NumberOfRooms.DialogClean(), ModSettings.NumberOfRooms.ToString());
            }
        });

        Hotkey.SetEndPoint.RegisterPressedAction(scene => EndPoint.SetEndPoint(scene, false));
        Hotkey.SetAdditionalEndPoint.RegisterPressedAction(scene => EndPoint.SetEndPoint(scene, true));

        Hotkey.ExportRoomTimes.RegisterPressedAction(scene => {
            if (scene is Level) {
                ExportRoomTimes();
                PopupMessageUtils.Show(string.Format(Dialog.Get(DialogIds.ExportRoomTimesSuccess),
                    Dialog.Get(DialogIds.Prefix + ModSettings.RoomTimerExportType.ToString().ToUpper())), null);
            } else {
                PopupMessageUtils.Show(DialogIds.ExportRoomTimesFail.DialogClean(), null);
            }
        });
    }

    private static void SpeedrunTimerDisplayOnUpdate(ILContext il) {
        ILCursor ilCursor = new(il);
        if (ilCursor.TryGotoNext(MoveType.After,
                ins => ins.OpCode == OpCodes.Ldarg_0,
                ins => ins.OpCode == OpCodes.Ldarg_0,
                ins => ins.MatchLdfld<SpeedrunTimerDisplay>("DrawLerp"),
                ins => ins.OpCode == OpCodes.Ldloc_1
            )) {
            ilCursor.EmitDelegate<Func<bool, bool>>(IsShowTimer);
        }
    }

    private static bool IsShowTimer(bool showTimer) {
        return showTimer || ModSettings.RoomTimerType != RoomTimerType.Off;
    }

    public static void SwitchRoomTimer(RoomTimerType roomTimerType) {
        ModSettings.RoomTimerType = roomTimerType;

        // no longer clear room times if switching to off mode
        // to allow cycling through modes while checking times

        SpeedrunToolModule.Instance.SaveSettings();
    }

    public static void ClearPbTimes(bool clearEndPoint = true) {
        previousRoom = null;
        NextRoomTimerData.Clear();
        CurrentRoomTimerData.Clear();
        if (Engine.Scene is Level level) {
            level.Tracker.GetEntities<ConfettiRenderer>().ForEach(entity => entity.RemoveSelf());
        }
        if (clearEndPoint) {
            EndPoint.ClearAll();
        }
    }

    private static void Timing(On.Celeste.Level.orig_Update orig, Level self) {
        orig(self);

        string currentRoom = self.Session.Level;
        bool enteringNextRoom = previousRoom != null && previousRoom != currentRoom;
        previousRoom = currentRoom;
        if (self.Completed || enteringNextRoom || EndPoint.IsReachedRoomIdEndPoint) {
            UpdateTimerState();
        }

        NextRoomTimerData.Timing(self);
        CurrentRoomTimerData.Timing(self);
    }

    private static void UpdateTimerStateOnTouchFlag(On.Celeste.SummitCheckpoint.orig_Update orig, SummitCheckpoint self) {
        bool lastActivated = self.Activated;
        orig(self);
        if (ModSettings.TimeSummitFlag && !lastActivated && self.Activated) {
            UpdateTimerState();
        }
    }

    private static void HeartGemOnRegisterAsCollected(On.Celeste.HeartGem.orig_RegisterAsCollected orig, HeartGem self, Level level, string poemId) {
        orig(self, level, poemId);

        if (ModSettings.TimeHeartCassette) {
            UpdateTimerState();
        }
    }

    private static void SaveDataOnRegisterCassette(On.Celeste.SaveData.orig_RegisterCassette orig, SaveData self, AreaKey area) {
        orig(self, area);

        if (ModSettings.TimeHeartCassette) {
            UpdateTimerState();
        }
    }

    private static void LevelExitOnCtor(On.Celeste.LevelExit.orig_ctor orig, LevelExit self, LevelExit.Mode mode, Session session,
        HiresSnow snow) {
        orig(self, mode, session, snow);
        if (ModSettings.AutoResetRoomTimer && mode == LevelExit.Mode.Restart) {
            SwitchRoomTimer(RoomTimerType.Off);
        }
    }

    public static void UpdateTimerState(bool endPoint = false) {
        // always run both timers; they'll just run in the background if not selected
        NextRoomTimerData.UpdateTimerState(endPoint);
        CurrentRoomTimerData.UpdateTimerState(endPoint);

        NextRoomTimerData.FreezeAutosplitterTime();
        CurrentRoomTimerData.FreezeAutosplitterTime();
    }

    public static void ResetTime() {
        previousRoom = null;
        NextRoomTimerData.ResetTime();
        CurrentRoomTimerData.ResetTime();
    }

    private static void Render(On.Celeste.SpeedrunTimerDisplay.orig_Render orig, SpeedrunTimerDisplay self) {
        if (!ModSettings.Enabled || ModSettings.RoomTimerType == RoomTimerType.Off || self.DrawLerp <= 0f) {
            orig(self);
            return;
        }

        RoomTimerData roomTimerData = ModSettings.RoomTimerType == RoomTimerType.NextRoom ? NextRoomTimerData : CurrentRoomTimerData;

        string roomTimeString = roomTimerData.TimeString;
        string pbTimeString = $"PB {roomTimerData.PbTimeString}";
        string comparePbString = ComparePb(roomTimerData.GetSelectedRoomTime, roomTimerData.GetSelectedLastPbTime);

        float topBlackBarWidth = 0f;
        float pbWidth = 60;
        const float topTimeHeight = 38f;
        const float timeMarginLeft = 32f;
        const float pbScale = 0.6f;

        MTexture bg = GFX.Gui["strawberryCountBG"];
        float x = -300f * Ease.CubeIn(1f - self.DrawLerp);

        if (roomTimerData.IsCompleted) {
            topBlackBarWidth += Math.Max(0, 35 * (roomTimeString.Length - 5)) + Math.Max(0, 20 * (comparePbString.Length - 5));
            if (roomTimeString.Length >= 8) {
                topBlackBarWidth -= 15;
            }
        }

        Draw.Rect(x, self.Y, topBlackBarWidth + 2, topTimeHeight, Color.Black);
        bg.Draw(new Vector2(x + topBlackBarWidth, self.Y));

        DrawTime(new Vector2(x + timeMarginLeft, self.Y + 44f), roomTimeString, 1f,
            roomTimerData.IsCompleted, roomTimerData.BeatBestTime, 1f, true);

        if (roomTimerData.IsCompleted) {
            DrawTime(
                new Vector2(x + timeMarginLeft + SpeedrunTimerDisplay.GetTimeWidth(roomTimeString) + 10,
                    self.Y + 36f),
                comparePbString, 0.5f,
                roomTimerData.IsCompleted, roomTimerData.BeatBestTime);
        }

        float pbTextWidth = Math.Max(0, 18 * (pbTimeString.Length - 8));
        pbWidth += pbTextWidth;

        // 遮住上下两块的间隙，游戏原本的问题
        Draw.Rect(x, self.Y + topTimeHeight - 1, pbWidth + bg.Width * pbScale, 1f, Color.Black);

        // PB
        Draw.Rect(x, self.Y + topTimeHeight, pbWidth + 2, bg.Height * pbScale + 1f, Color.Black);
        bg.Draw(new Vector2(x + pbWidth, self.Y + topTimeHeight), Vector2.Zero, Color.White, pbScale);
        DrawTime(new Vector2(x + timeMarginLeft, (float)(self.Y + 66.4)), pbTimeString, pbScale, false, false, 0.6f);
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
        result += (int)timeSpan.TotalSeconds + timeSpan.ToString("\\.fff");
        return result;
    }

    private static void DrawTime(Vector2 position, string timeString, float scale = 1f,
        bool finished = false, bool bestTime = false, float alpha = 1f, bool mainTime = false) {
        PixelFont font = Dialog.Languages["english"].Font;
        float fontFaceSize = Dialog.Languages["english"].FontFaceSize;
        float x = position.X;
        float y = position.Y;
        Color color1 = Color.White * alpha;
        Color color2 = Color.LightGray * alpha;
        if (bestTime) {
            color1 = BestColor1 * alpha;
            color2 = BestColor2 * alpha;
        } else if (finished) {
            color1 = FinishedColor1 * alpha;
            color2 = FinishedColor2 * alpha;
        }

        float currentScale = scale;
        foreach (char ch in timeString) {
            if (mainTime) {
                if (ch == '.') {
                    currentScale = scale * 0.7f;
                    y -= 5f * scale;
                }
            }
            Color color3 = ch is ':' or '.' ? color2 : color1;

            float num2 = (float)((ch is ':' or '.' ? SpeedrunTimerDisplay.spacerWidth : SpeedrunTimerDisplay.numberWidth) + 4.0) * currentScale;
            font.DrawOutline(fontFaceSize, ch.ToString(), new Vector2(x + num2 / 2f, y), new Vector2(0.5f, 1f),
                Vector2.One * currentScale, color3, 2f, Color.Black);
            x += num2;
        }
    }

    private static void MoveStrawberryDisplayDown(ILContext il) {
        ILCursor cursor = new(il);

        // If the room timer is visible, push the strawberry display down so that it doesn't overlap the timer
        if (cursor.TryGotoNext(MoveType.Before, instr => instr.MatchCall<Engine>("get_DeltaTime") && instr.Next.MatchLdcR4(800))) {
            cursor.EmitDelegate<Func<float, float>>((origTargetPos) => {
                if (ModSettings.Enabled && ModSettings.RoomTimerType is not RoomTimerType.Off) {
                    return 174f; // Same as regular file timer
                }
                return origTargetPos;
            });
        }
    }

    public static void TryTurnOffRoomTimer() {
        if (ModSettings.AutoResetRoomTimer) {
            SwitchRoomTimer(RoomTimerType.Off);
        }
    }

    private static void OverwriteAutosplitterChapterTime(ILContext il) {
        ILCursor cursor = new(il);

        if (cursor.TryGotoNext(MoveType.After, instr => instr.MatchLdfld<Session>("Time"))) {
            cursor.EmitDelegate<Func<long, long>>((origTime) => {
                if (ModSettings.Enabled && ModSettings.RoomTimerType is not RoomTimerType.Off) {
                    return (ModSettings.RoomTimerType is RoomTimerType.NextRoom) ? NextRoomTimerData.AutosplitterTime : CurrentRoomTimerData.AutosplitterTime;
                }
                return origTime;
            });
        }
    }

    private static void ExportRoomTimes() {
        RoomTimerData roomTimerData = (ModSettings.RoomTimerType is RoomTimerType.NextRoom) ? NextRoomTimerData : CurrentRoomTimerData;
        string timeKeyPrefix = roomTimerData.TimeKeyPrefix;
        long lastSplitTime = 0;

        StringBuilder sb = new();

        // Header row
        sb.Append("Room Number,Split,Segment,Best Split,Best Segment");

        for (int roomNumber = 1; roomNumber <= Math.Max(roomTimerData.ThisRunTimes.Count, Math.Max(roomTimerData.PbTimes.Count, roomTimerData.BestSegments.Count)); roomNumber++) {
            string timeKey = timeKeyPrefix + roomNumber;

            // Room Number
            sb.Append($"\n{roomNumber},");

            // Split,Segment
            if (roomTimerData.ThisRunTimes.TryGetValue(timeKey, out long splitTime)) {
                sb.Append($"{RoomTimerData.FormatTime(splitTime, false)},{RoomTimerData.FormatTime(splitTime - lastSplitTime, false)},");
                lastSplitTime = splitTime;
            } else {
                sb.Append(",,");
            }

            // Best Split,
            if (roomTimerData.PbTimes.TryGetValue(timeKey, out long pbTime)) {
                sb.Append($"{RoomTimerData.FormatTime(pbTime, false)},");
            } else {
                sb.Append(",");
            }

            // Best Segment
            if (roomTimerData.BestSegments.TryGetValue(timeKey, out long bestSegment)) {
                sb.Append(RoomTimerData.FormatTime(bestSegment, false));
            }
        }

        if (ModSettings.RoomTimerExportType is RoomTimerExportType.File) {
            Directory.CreateDirectory(Path.Combine(Everest.PathGame, "SRTool_RoomTimeExports"));
            using StreamWriter writer = File.CreateText(Path.Combine(Everest.PathGame, "SRTool_RoomTimeExports", $"{DateTime.Now:yyyyMMdd_HHmmss}.csv"));
            writer.WriteLine(sb.ToString());
        } else {
            TextInput.SetClipboardText(sb.ToString());
        }
    }

    [Command("srt_exportroomtimes", "export room timer data in csv format to clipboard or a file in gamePath/SRTool_RoomTimeExports folder (SpeedrunTool)")]
    public static void CmdExportRoomTimes() {
        if (Engine.Scene is Level) {
            ExportRoomTimes();
            string msgID = DialogIds.Prefix + ModSettings.RoomTimerExportType.ToString().ToUpper();
            Engine.Commands.Log(string.Format(Dialog.Get(DialogIds.ExportRoomTimesSuccess), msgID.DialogClean()));
        } else {
            Engine.Commands.Log(DialogIds.ExportRoomTimesFail.DialogClean());
        }
    }
}