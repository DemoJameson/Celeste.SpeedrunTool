using Celeste.Mod.SpeedrunTool.Message;
using Celeste.Mod.SpeedrunTool.Other;
using Mono.Cecil.Cil;
using MonoMod.Cil;

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

public static class RoomTimerManager {
    private static readonly Color BestColor1 = Calc.HexToColor("fad768");
    private static readonly Color BestColor2 = Calc.HexToColor("cfa727");
    private static readonly Color FinishedColor1 = Calc.HexToColor("6ded87");
    private static readonly Color FinishedColor2 = Calc.HexToColor("43d14c");

    public const string FlagPrefix = "summit_checkpoint_";
    private static readonly RoomTimerData CurrentRoomTimerData = new(RoomTimerType.CurrentRoom);
    private static readonly RoomTimerData NextRoomTimerData = new(RoomTimerType.NextRoom);

    private static string previousRoom;

    [Load]
    private static void Load() {
        IL.Celeste.SpeedrunTimerDisplay.Update += SpeedrunTimerDisplayOnUpdate;
        On.Celeste.SpeedrunTimerDisplay.Render += Render;
        On.Celeste.Level.Update += Timing;
        On.Celeste.SummitCheckpoint.Update += UpdateTimerStateOnTouchFlag;
        On.Celeste.LevelExit.ctor += LevelExitOnCtor;
        TryTurnOffRoomTimer();
        RegisterHotkeys();
    }

    [Unload]
    private static void Unload() {
        IL.Celeste.SpeedrunTimerDisplay.Update -= SpeedrunTimerDisplayOnUpdate;
        On.Celeste.SpeedrunTimerDisplay.Render -= Render;
        On.Celeste.Level.Update -= Timing;
        On.Celeste.SummitCheckpoint.Update -= UpdateTimerStateOnTouchFlag;
        On.Celeste.LevelExit.ctor -= LevelExitOnCtor;
    }

    private static void RegisterHotkeys() {
        Hotkey.ResetRoomTimerPb.RegisterPressedAction(scene => {
            if (scene is Level {Paused: false}) {
                ClearPbTimes();
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

                Tooltip.Show(string.Format(Dialog.Get(DialogIds.OptionState), DialogIds.NumberOfRooms.DialogClean(), ModSettings.NumberOfRooms));
            }
        });

        Hotkey.DecreaseTimedRooms.RegisterPressedAction(scene => {
            if (scene is Level { Paused: false }) {
                if (ModSettings.NumberOfRooms > 1) {
                    ModSettings.NumberOfRooms--;
                    SpeedrunToolModule.Instance.SaveSettings();
                }

                Tooltip.Show(string.Format(Dialog.Get(DialogIds.OptionState), DialogIds.NumberOfRooms.DialogClean(), ModSettings.NumberOfRooms));
            }
        });

        Hotkey.SetEndPoint.RegisterPressedAction(scene => {
            if (scene is Level {Paused: false} level) {
                ClearPbTimes();
                CreateEndPoint(level);
            }
        });

        Hotkey.SetAdditionalEndPoint.RegisterPressedAction(scene => {
            if (scene is Level {Paused: false} level) {
                if (!EndPoint.IsExist) {
                    ClearPbTimes();
                }

                CreateEndPoint(level, true);
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
            ilCursor.EmitDelegate<Func<bool, bool>>(showTimer => showTimer || ModSettings.RoomTimerType != RoomTimerType.Off);
        }
    }

    private static void CreateEndPoint(Level level, bool additional = false) {
        if (level.GetPlayer() is {Dead: false} player) {
            if (!additional) {
                EndPoint.All.ForEach(point => point.RemoveSelf());
            }

            level.Add(new EndPoint(player));
        }
    }

    public static void SwitchRoomTimer(RoomTimerType roomTimerType) {
        ModSettings.RoomTimerType = roomTimerType;

        //if (roomTimerType == RoomTimerType.Off) {
        //    ClearPbTimes();
        //}

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
            EndPoint.All.ForEach(point => point.RemoveSelf());
        }
    }

    private static void Timing(On.Celeste.Level.orig_Update orig, Level self) {
        orig(self);

        string currentRoom = self.Session.Level;
        bool nextRoom = previousRoom != null && previousRoom != currentRoom;
        previousRoom = currentRoom;
        if (self.Completed || nextRoom) {
            UpdateTimerState();
        }

        NextRoomTimerData.UpdateTimeKeys(self);
        CurrentRoomTimerData.UpdateTimeKeys(self);
        if (!self.Completed && self.TimerStarted) {
            NextRoomTimerData.Timing(self);
            CurrentRoomTimerData.Timing(self);
        }
    }

    private static void UpdateTimerStateOnTouchFlag(On.Celeste.SummitCheckpoint.orig_Update orig, SummitCheckpoint self) {
        bool lastActivated = self.Activated;
        orig(self);
        if (!ModSettings.RoomTimerIgnoreFlag && !lastActivated && self.Activated) {
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
        switch (ModSettings.RoomTimerType) {
            case RoomTimerType.NextRoom:
            case RoomTimerType.CurrentRoom:
                NextRoomTimerData.UpdateTimerState(endPoint);
                CurrentRoomTimerData.UpdateTimerState(endPoint);
                break;
            case RoomTimerType.Off:
                break;
            default:
                throw new ArgumentOutOfRangeException();
        }
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

    private static readonly Lazy<float> NumberWidth = new(() => typeof(SpeedrunTimerDisplay).GetFieldValue<float>("numberWidth"));
    private static readonly Lazy<float> SpacerWidth = new(() => typeof(SpeedrunTimerDisplay).GetFieldValue<float>("spacerWidth"));

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

            float num2 = (float)((ch is ':' or '.' ? SpacerWidth.Value : NumberWidth.Value) + 4.0) * currentScale;
            font.DrawOutline(fontFaceSize, ch.ToString(), new Vector2(x + num2 / 2f, y), new Vector2(0.5f, 1f),
                Vector2.One * currentScale, color3, 2f, Color.Black);
            x += num2;
        }
    }

    private static void TryTurnOffRoomTimer() {
        if (ModSettings.AutoResetRoomTimer) {
            SwitchRoomTimer(RoomTimerType.Off);
        }
    }
}