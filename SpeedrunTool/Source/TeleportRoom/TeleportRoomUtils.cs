using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Celeste.Mod.SpeedrunTool.DeathStatistics;
using Celeste.Mod.SpeedrunTool.Message;
using Celeste.Mod.SpeedrunTool.Other;
using Celeste.Mod.SpeedrunTool.RoomTimer;
using Celeste.Mod.SpeedrunTool.SaveLoad;
using Celeste.Pico8;
using Force.DeepCloner;
using On.Celeste.Editor;
using LevelTemplate = Celeste.Editor.LevelTemplate;

namespace Celeste.Mod.SpeedrunTool.TeleportRoom;

public static class TeleportRoomUtils {
    private const string FlagPrefix = "summit_checkpoint_";
    private static readonly List<Session> RoomHistory = new();
    private static int historyIndex = -1;
    private static bool allowRecord;
    private static Vector2? respawnPoint;

    [Load]
    private static void Load() {
        On.Celeste.Level.LoadLevel += LevelOnLoadLevel;
        On.Celeste.Level.TransitionRoutine += LevelOnTransitionRoutine;
        On.Celeste.LevelExit.ctor += LevelExitOnCtor;
        On.Celeste.SummitCheckpoint.Update += SummitCheckpointOnUpdate;
        MapEditor.LoadLevel += MapEditorOnLoadLevel;
        On.Celeste.LevelLoader.ctor += LevelLoaderOnCtor;
        RegisterHotkeys();
    }

    [Unload]
    private static void Unload() {
        On.Celeste.Level.LoadLevel -= LevelOnLoadLevel;
        On.Celeste.Level.TransitionRoutine -= LevelOnTransitionRoutine;
        On.Celeste.LevelExit.ctor -= LevelExitOnCtor;
        On.Celeste.SummitCheckpoint.Update -= SummitCheckpointOnUpdate;
        MapEditor.LoadLevel -= MapEditorOnLoadLevel;
        On.Celeste.LevelLoader.ctor -= LevelLoaderOnCtor;
    }

    private static void RegisterHotkeys() {
        Hotkey.TeleportToPreviousRoom.RegisterPressedAction(scene => {
            if (scene is Level {Paused: false} level && StateManager.Instance.State == State.None) {
                if (TeleportToPreviousRoom(level) == false) {
                    PopupMessageUtils.Show(DialogIds.AlreadyFirstRoomTooltip.DialogClean(), DialogIds.AlreadyFirstRoomDialog);
                }
            } else if (scene is Emulator {gameActive: true, game: { } game}) {
                PreviousPico8Room(game);
            }
        });

        Hotkey.TeleportToNextRoom.RegisterPressedAction(scene => {
            if (scene is Level {Paused: false} level && StateManager.Instance.State == State.None) {
                if (TeleportToNextRoom(level) == false) {
                    PopupMessageUtils.Show(DialogIds.AlreadyLastRoomTooltip.DialogClean(), DialogIds.AlreadyLastRoomDialog);
                }
            } else if (scene is Emulator {gameActive: true, game: { } game}) {
                NextPico8Room(game);
            }
        });
    }

    private static void PreviousPico8Room(Classic game) {
        Point room = LevelIndexToRoom(game.level_index() - 1);
        game.load_room(room.X, room.Y);
        CorrectPico8State(game);
    }

    private static void NextPico8Room(Classic game) {
        Point room = LevelIndexToRoom(game.level_index() + 1);
        game.load_room(room.X, room.Y);
        CorrectPico8State(game);
    }

    private static Point LevelIndexToRoom(int levelIndex) {
        levelIndex += 32;
        levelIndex %= 32;
        return new Point(levelIndex % 8, levelIndex / 8);
    }

    private static void CorrectPico8State(Classic game) {
        int levelIndex = game.level_index();
        bool doubleJump = levelIndex is > 21 and < 31;
        game.max_djump = doubleJump ? 2 : 1;
        game.new_bg = doubleJump;
        game.start_game = false;

        int music = levelIndex switch {
            31 => 40,
            >= 0 and <= 10 => 0,
            >= 22 and <= 29 => 10,
            11 or 21 or 30 => 30,
            _ => 20
        };

        game.E.music(music, 0, 0);
        game.music_timer = 0;
    }

    private static void SummitCheckpointOnUpdate(On.Celeste.SummitCheckpoint.orig_Update orig, SummitCheckpoint self) {
        bool lastActivated = self.Activated;
        orig(self);
        if (!lastActivated && self.Activated) {
            if (Engine.Scene is Level level && level.GetPlayer() is { } player) {
                player.Add(new Coroutine(WaitSessionReady(level.Session)));
            }
        }
    }

    private static void LevelLoaderOnCtor(On.Celeste.LevelLoader.orig_ctor orig, LevelLoader self, Session session,
        Vector2? startPosition) {
        orig(self, session, startPosition);
        if (respawnPoint.HasValue) {
            session.RespawnPoint = respawnPoint;
            respawnPoint = null;
        }

        if (allowRecord) {
            RecordTransitionRoom(session);
        }
    }

    private static void MapEditorOnLoadLevel(MapEditor.orig_LoadLevel orig, Editor.MapEditor self,
        LevelTemplate level, Vector2 at) {
        allowRecord = true;
        orig(self, level, at);
        allowRecord = false;
    }

    private static IEnumerator WaitSessionReady(Session self) {
        yield return null;
        RecordTransitionRoom(self);
    }

    private static void TeleportTo(Session session, bool fromHistory = false) {
        if (Engine.Scene is not Level level) {
            return;
        }

        // 修复问题：死亡瞬间传送 PlayerDeadBody 没被清除，导致传送完毕后 madeline 自动爆炸
        level.Entities.UpdateLists();
        level.RendererList.Renderers.ForEach(renderer => (renderer as ScreenWipe)?.Cancel());

        // External
        RoomTimerManager.ResetTime();
        DeathStatisticsManager.Clear();

        level.transition = null; // 允许切换房间时传送
        Glitch.Value = 0f;
        Engine.TimeRate = 1f;
        Engine.FreezeTimer = 0f;
        Distort.Anxiety = 0f;
        Distort.GameRate = 1f;
        Audio.SetMusicParam("fade", 1f);
        FallEffects.Show(false);
        level.Displacement.Clear(); // 避免冲刺后残留爆破效果
        level.Particles.Clear();
        level.ParticlesBG.Clear();
        level.ParticlesFG.Clear();
        TrailManager.Clear(); // 清除冲刺的残影

        if (!fromHistory) {
            BetterMapEditor.FixTeleportProblems(session, session.RespawnPoint);
        }

        bool savedState = StateMarkUtils.GetSavedStateFlag(level);
        long time = Math.Max(session.Time, level.Session.Time);
        int increaseDeath = level.IsPlayerDead() || level.GetPlayer().JustRespawned ? 0 : 1;
        int deaths = Math.Max(session.Deaths, level.Session.Deaths) + increaseDeath;
        int deathsInCurrentLevel = Math.Max(session.DeathsInCurrentLevel, level.Session.DeathsInCurrentLevel) + increaseDeath;

        session.DeepCloneTo(level.Session);

        if (savedState) {
            StateMarkUtils.SetSavedStateFlag(level);
        }

        level.Session.Time = time;
        level.Session.Deaths = deaths;
        level.Session.DeathsInCurrentLevel = deathsInCurrentLevel;

        // 修改自 level.TeleportTo(player, session.Level, Player.IntroTypes.Respawn);
        level.Tracker.GetEntitiesCopy<Player>().ForEach(entity => entity.RemoveSelf());

        if (level.Entities.FindFirst<SpeedrunTimerDisplay>() is Entity timer) {
            level.Remove(timer);
        }

        level.UnloadLevel();
        GC.Collect();
        GC.WaitForPendingFinalizers();

        level.Completed = false;
        level.InCutscene = false;
        level.SkippingCutscene = false;

        // 修复：章节计时器在章节完成隐藏后传送无法重新显示
        level.Add(new SpeedrunTimerDisplay());
        level.LoadLevel(Player.IntroTypes.Respawn);
        level.Entities.UpdateLists();

        // 节奏块房间传送出来时恢复音乐
        if (level.Tracker.GetEntities<CassetteBlock>().Count == 0) {
            level.Tracker.GetEntity<CassetteBlockManager>()?.RemoveSelf();
        }

        // new player instance
        if (level.GetPlayer() != null) {
            level.Camera.Position = level.GetPlayer().CameraTarget;
        }

        level.Update();
    }

    private static void LevelExitOnCtor(On.Celeste.LevelExit.orig_ctor orig, LevelExit self, LevelExit.Mode mode,
        Session session, HiresSnow snow) {
        orig(self, mode, session, snow);

        if (mode != LevelExit.Mode.GoldenBerryRestart) {
            Reset();
        }
    }

    private static void Reset() {
        RoomHistory.Clear();
        historyIndex = -1;
    }

    private static bool? TeleportToPreviousRoom(Level level) {
        if (historyIndex > 0 && historyIndex < RoomHistory.Count) {
            // Glyph 这种传送到其他房间是不做记录的，所以只回到当前记录的房间
            if (level.Session.Level == RoomHistory[historyIndex].Level) {
                historyIndex--;
            }

            TeleportTo(RoomHistory[historyIndex], true);
            return true;
        }

        if (SearchSummitCheckpoint(false, level)) {
            TeleportTo(level.Session);
            return true;
        }

        List<ReorderedData> levelDataList = LevelDataReorderUtils.GetReorderedLevelDataList(level);
        int index = IndexOf(levelDataList, level);

        if (index == 0) {
            return false;
        } else if (index == -1) {
            if (ModSettings.TeleportRoomCategory != TeleportRoomCategory.Default) {
                var defaultLevelDataList = LevelDataReorderUtils.GetReorderedLevelDataList(level, TeleportRoomCategory.Default);
                index = IndexOf(defaultLevelDataList, level);
                if (index == 0) {
                    return false;
                } else if (index > 0) {
                    levelDataList = defaultLevelDataList;
                } else {
                    index = 1;
                }
            } else {
                index = 1;
            }
        }

        index--;
        ReorderedData lastReorderedData = levelDataList[index];
        while (lastReorderedData.LevelData.Dummy && index > 0) {
            index--;
            lastReorderedData = levelDataList[index];
        }

        if (lastReorderedData.LevelData.Dummy) {
            return false;
        }

        level.Session.Level = lastReorderedData.LevelData.Name;
        level.Session.RespawnPoint = lastReorderedData.RespawnPoint;
        level.StartPosition = null;

        SearchSummitCheckpoint(false, lastReorderedData.LevelData, level);
        TeleportTo(level.Session);
        return true;
    }

    private static bool? TeleportToNextRoom(Level level) {
        if (historyIndex >= 0 && historyIndex < RoomHistory.Count - 1) {
            historyIndex++;
            TeleportTo(RoomHistory[historyIndex], true);
            return true;
        }

        if (SearchSummitCheckpoint(true, level)) {
            RecordAndTeleportToNextRoom(level.Session);
            return true;
        }

        List<ReorderedData> levelDataList = LevelDataReorderUtils.GetReorderedLevelDataList(level);
        int index = IndexOf(levelDataList, level);

        if (index == levelDataList.Count - 1) {
            return false;
        } else if (index == -1 && ModSettings.TeleportRoomCategory != TeleportRoomCategory.Default) {
            var defaultLevelDataList = LevelDataReorderUtils.GetReorderedLevelDataList(level, TeleportRoomCategory.Default);
            index = IndexOf(defaultLevelDataList, level);
            if (index == defaultLevelDataList.Count - 1) {
                return false;
            } else if (index > 0) {
                levelDataList = defaultLevelDataList;
            }
        }

        index++;
        ReorderedData nextReorderedData = levelDataList[index];
        while (nextReorderedData.LevelData.Dummy && index < levelDataList.Count - 1) {
            index++;
            nextReorderedData = levelDataList[index];
        }

        if (nextReorderedData.LevelData.Dummy) {
            return false;
        }

        level.Session.Level = nextReorderedData.LevelData.Name;
        level.Session.RespawnPoint = nextReorderedData.RespawnPoint;
        level.StartPosition = null;

        SearchSummitCheckpoint(true, nextReorderedData.LevelData, level);
        RecordAndTeleportToNextRoom(level.Session);
        return true;
    }

    private static int IndexOf(List<ReorderedData> levelDataList, Level level) {
        LevelData currentLevelData = level.Session.LevelData;
        int index = levelDataList.FindIndex(data => data.LevelData == currentLevelData && data.RespawnPoint == level.Session.RespawnPoint);
        if (index == -1) {
            index = levelDataList.FindIndex(data => data.LevelData == currentLevelData);
        }

        return index;
    }

    private static bool SearchSummitCheckpoint(bool next, Level level) {
        // 查找当前房间是否有未触发的旗子，如果有则跳到旗子处
        int? currentFlagNumber = null;

        foreach (string flag in level.Session.Flags.Where(flag => flag.StartsWith(FlagPrefix))) {
            if (int.TryParse(flag.Replace(FlagPrefix, ""), out int flagNumber)) {
                currentFlagNumber = currentFlagNumber == null ? flagNumber : Math.Min(currentFlagNumber.Value, flagNumber);
            }
        }

        currentFlagNumber ??= next ? int.MaxValue : int.MinValue;

        List<SummitCheckpoint> flagList = level.Entities.FindAll<SummitCheckpoint>().Where(checkpoint => !checkpoint.Activated).ToList();

        // from small to big
        flagList.Sort((first, second) => first.Number - second.Number);

        if (flagList.Count > 0) {
            SummitCheckpoint summitCheckpoint = null;
            if (next && flagList.LastOrDefault(checkpoint => checkpoint.Number < currentFlagNumber) is { } biggest) {
                summitCheckpoint = biggest;
            } else if (!next && flagList.FirstOrDefault(checkpoint => checkpoint.Number > currentFlagNumber) is { } smallest) {
                summitCheckpoint = smallest;
            }

            if (summitCheckpoint == null) {
                return false;
            }

            level.Session.RespawnPoint = level.GetSpawnPoint(summitCheckpoint.Position);
            level.Session.SetFlag(FlagPrefix + summitCheckpoint.Number);
            return true;
        }

        return false;
    }

    private static void SearchSummitCheckpoint(bool next, LevelData levelData, Level level) {
        // 查找上/下个房间是否有旗子，如果有则跳到旗子处
        List<EntityData> flagList = levelData.Entities.Where(data => data.Name == "summitcheckpoint").ToList();

        flagList.Sort((first, second) => first.Int("number") - second.Int("number"));
        if (flagList.Count > 0) {
            EntityData summitCheckpoint = next ? flagList.Last() : flagList.First();
            level.Session.RespawnPoint = level.GetSpawnPoint(levelData.Position + summitCheckpoint.Position);
            level.Session.SetFlag(FlagPrefix + summitCheckpoint.Int("number"));
        }
    }

    private static void LevelOnLoadLevel(On.Celeste.Level.orig_LoadLevel orig, Level self,
        Player.IntroTypes playerIntro, bool isFromLoader) {
        orig(self, playerIntro, isFromLoader);

        // 切换章节清理历史记录
        if (RoomHistory.Count > 0 && RoomHistory[0].Area != self.Session.Area) {
            Reset();
        }

        // 非初始房间
        if (historyIndex != -1) {
            return;
        }

        // 进入章节的第一个房间
        RoomHistory.Add(self.Session.DeepClone());
        historyIndex = 0;
    }

    private static IEnumerator LevelOnTransitionRoutine(On.Celeste.Level.orig_TransitionRoutine orig, Level self,
        LevelData next, Vector2 direction) {
        IEnumerator enumerator = orig(self, next, direction);
        while (enumerator.MoveNext()) {
            yield return enumerator.Current;
        }

        // 切图结束后
        RecordTransitionRoom(self.Session);
    }

    // 记录自行进入的房间或者触碰的旗子
    private static void RecordTransitionRoom(Session currentSession) {
        // 如果不是指向末尾证明曾经后退过，所以要记录新数据前必须清除后面的记录
        if (historyIndex < RoomHistory.Count - 1) {
            RoomHistory.RemoveRange(historyIndex + 1, RoomHistory.Count - historyIndex - 1);
        }

        // 增加记录
        RecordRoom(currentSession);
    }

    // 记录通过查找地图数据传送的房间
    private static void RecordAndTeleportToNextRoom(Session session) {
        session.StartedFromBeginning = false;
        RecordRoom(session);
        TeleportTo(session);
    }

    private static void RecordRoom(Session session) {
        // 如果存在相同的房间且存档点相同则先清除
        for (int i = RoomHistory.Count - 1; i >= 0; i--) {
            if (RoomHistory[i].Level == session.Level && RoomHistory[i].RespawnPoint == session.RespawnPoint) {
                RoomHistory.RemoveAt(i);
            }
        }

        RoomHistory.Add(session.DeepClone());
        historyIndex = RoomHistory.Count - 1;
    }
}