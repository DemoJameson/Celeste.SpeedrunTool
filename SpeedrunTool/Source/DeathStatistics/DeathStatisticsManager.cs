using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Text.RegularExpressions;
using Celeste.Mod.Helpers;
using Celeste.Mod.SpeedrunTool.Other;
using Celeste.Mod.SpeedrunTool.SaveLoad;
using Force.DeepCloner;
using MonoMod.RuntimeDetour;

namespace Celeste.Mod.SpeedrunTool.DeathStatistics;

public static class DeathStatisticsManager {
    private static readonly Lazy<bool> EverestCore = new(() => Everest.Loader.DependencyLoaded(new() {
        Name = "EverestCore"
    }));

    public static string SavePath => typeof(UserIO).GetFieldInfo("SavePath").GetValue(null).ToString();

    // don't touch it, since the UserIO.SavePath is different between xna and fna
    public static readonly string PlaybackDir = Path.Combine(SavePath, "SpeedrunTool", "DeathPlayback");
    public static string PlaybackSlotDir => Path.Combine(PlaybackDir, SaveData.Instance?.FileSlot.ToString() ?? "-1");
    private static bool Enabled => ModSettings.Enabled && ModSettings.DeathStatistics;
    private static long lastTime;
    private static bool died;
    private static DeathInfo currentDeathInfo;
    private static DeathInfo playbackDeathInfo;

    [Load]
    private static void Hook() {
        // 尽量晚的 Hook Player.Die 方法，以便可以稳定的从指定的 StackTrace 中找出死亡原因
        using (new DetourContext {After = new List<string> {"*"}}) {
            On.Celeste.Player.Die += PlayerOnDie;
        }
    }

    [Load]
    private static void Load() {
        On.Celeste.PlayerDeadBody.End += PlayerDeadBodyOnEnd;
        On.Celeste.Level.NextLevel += LevelOnNextLevel;
        On.Celeste.Player.Update += PlayerOnUpdate;
        On.Celeste.OuiFileSelectSlot.EnterFirstArea += OuiFileSelectSlotOnEnterFirstArea;
        On.Celeste.ChangeRespawnTrigger.OnEnter += ChangeRespawnTriggerOnOnEnter;
        On.Celeste.Session.SetFlag += UpdateTimerStateOnTouchFlag;
        On.Celeste.LevelLoader.ctor += LevelLoaderOnCtor;
        On.Celeste.Level.LoadLevel += LevelOnLoadLevel;
        On.Monocle.Scene.Begin += SceneOnBegin;

        Hotkey.CheckDeathStatistics.RegisterPressedAction(scene => {
            if (scene.Tracker.GetEntity<DeathStatisticsUi>() is { } deathStatisticsUi) {
                deathStatisticsUi.OnESC?.Invoke();
            } else if (scene is Level {Paused: false} level && !level.IsPlayerDead()) {
                level.Paused = true;
                DeathStatisticsUi buttonConfigUi = new() {
                    OnClose = () => level.Paused = false
                };
                level.Add(buttonConfigUi);
                level.OnEndOfFrame += level.Entities.UpdateLists;
            }
        });
    }

    [Unload]
    private static void Unload() {
        On.Celeste.Player.Die -= PlayerOnDie;
        On.Celeste.PlayerDeadBody.End -= PlayerDeadBodyOnEnd;
        On.Celeste.Level.NextLevel -= LevelOnNextLevel;
        On.Celeste.Player.Update -= PlayerOnUpdate;
        On.Celeste.OuiFileSelectSlot.EnterFirstArea -= OuiFileSelectSlotOnEnterFirstArea;
        On.Celeste.ChangeRespawnTrigger.OnEnter -= ChangeRespawnTriggerOnOnEnter;
        On.Celeste.Session.SetFlag -= UpdateTimerStateOnTouchFlag;
        On.Celeste.LevelLoader.ctor -= LevelLoaderOnCtor;
        On.Celeste.Level.LoadLevel -= LevelOnLoadLevel;
        On.Monocle.Scene.Begin -= SceneOnBegin;
    }

    private static void SceneOnBegin(On.Monocle.Scene.orig_Begin orig, Scene self) {
        orig(self);
        if (self is Overworld or LevelExit) {
            Clear();
        }
    }

    private static void LevelOnLoadLevel(On.Celeste.Level.orig_LoadLevel orig, Level level, Player.IntroTypes playerIntro, bool isFromLoader) {
        if (IsPlayback()) {
            level.Add(new DeathMark(playbackDeathInfo.DeathPosition));

            if (playbackDeathInfo.PlaybackFilePath.IsNotNullAndEmpty() && File.Exists(playbackDeathInfo.PlaybackFilePath)) {
                List<Player.ChaserState> chaserStates = PlaybackData.Import(FileProxy.ReadAllBytes(playbackDeathInfo.PlaybackFilePath));
                PlayerSpriteMode spriteMode = level.Session.Inventory.Backpack ? PlayerSpriteMode.Madeline : PlayerSpriteMode.MadelineNoBackpack;
                if (SaveData.Instance.Assists.PlayAsBadeline) {
                    spriteMode = PlayerSpriteMode.MadelineAsBadeline;
                }

                PlayerPlayback playerPlayback = new(playbackDeathInfo.PlaybackStartPosition, spriteMode, chaserStates) {
                    Depth = Depths.Player
                };
                playerPlayback.Sprite.Color *= 0.8f;
                playerPlayback.Add(new ClearBeforeSaveComponent());
                level.Add(playerPlayback);
            }
        }

        orig(level, playerIntro, isFromLoader);
    }

    private static void LevelLoaderOnCtor(On.Celeste.LevelLoader.orig_ctor orig, LevelLoader self, Session session,
        Vector2? startPosition) {
        orig(self, session, startPosition);

        lastTime = SaveData.Instance.Time;
    }

    private static void UpdateTimerStateOnTouchFlag(On.Celeste.Session.orig_SetFlag origSetFlag, Session session,
        string flag, bool setTo) {
        origSetFlag(session, flag, setTo);

        if (flag.StartsWith("summit_checkpoint_") && setTo) {
            lastTime = SaveData.Instance.Time;
        }
    }

    private static void ChangeRespawnTriggerOnOnEnter(On.Celeste.ChangeRespawnTrigger.orig_OnEnter orig, ChangeRespawnTrigger self,
        Player player) {
        Level level = player.SceneAs<Level>();
        Vector2? oldPoint = level.Session.RespawnPoint;
        orig(self, player);
        Vector2? newPoint = level.Session.RespawnPoint;

        if (oldPoint != newPoint) {
            lastTime = SaveData.Instance.Time;
        }
    }

    private static void OuiFileSelectSlotOnEnterFirstArea(On.Celeste.OuiFileSelectSlot.orig_EnterFirstArea orig,
        OuiFileSelectSlot self) {
        orig(self);

        if (!Enabled) {
            return;
        }

        lastTime = 0;
    }

    private static void PlayerOnUpdate(On.Celeste.Player.orig_Update orig, Player player) {
        orig(player);

        if (Enabled && died && player.StateMachine.State is Player.StNormal or Player.StSwim) {
            LoggingData(false);
        }
    }

    private static void ExportPlayback(Player player) {
        string filePath = Path.Combine(PlaybackSlotDir, $"{DateTime.Now.Ticks}.bin");
        if (!Directory.Exists(PlaybackSlotDir)) {
            Directory.CreateDirectory(PlaybackSlotDir);
        }

        if (player.ChaserStates.Count > 0) {
            PlaybackData.Export(player.ChaserStates, filePath);
            currentDeathInfo.PlaybackStartPosition = player.ChaserStates[0].Position;
            currentDeathInfo.PlaybackFilePath = filePath;
        } else {
            currentDeathInfo.PlaybackStartPosition = default;
            currentDeathInfo.PlaybackFilePath = string.Empty;
        }
    }

    private static void LevelOnNextLevel(On.Celeste.Level.orig_NextLevel orig, Level self, Vector2 at, Vector2 dir) {
        orig(self, at, dir);

        if (Enabled) {
            lastTime = SaveData.Instance.Time;
        }
    }

    private static PlayerDeadBody PlayerOnDie(On.Celeste.Player.orig_Die orig, Player self, Vector2 direction,
        bool evenIfInvincible, bool registerDeathInStats) {
        PlayerDeadBody playerDeadBody = orig(self, direction, evenIfInvincible, registerDeathInStats);

        if (playerDeadBody != null && Enabled) {
            if (IsPlayback()) {
                currentDeathInfo = null;
                playbackDeathInfo = null;
            } else {
                currentDeathInfo = new DeathInfo {
                    CauseOfDeath = GetCauseOfDeath(),
                    DeathPosition = self.Position
                };
                ExportPlayback(self);

                if (playerDeadBody.HasGolden) {
                    LoggingData(true);
                }
            }
        }

        return playerDeadBody;
    }

    private static void PlayerDeadBodyOnEnd(On.Celeste.PlayerDeadBody.orig_End orig, PlayerDeadBody self) {
        orig(self);
        if (Enabled) {
            died = true;
        }
    }

    private static bool IsPlayback() {
        Level level = Engine.Scene switch {
            Level lvl => lvl,
            LevelLoader levelLoader => levelLoader.Level,
            _ => null
        };
        return level != null && playbackDeathInfo != null && playbackDeathInfo.Area == level.Session.Area &&
               playbackDeathInfo.Room == level.Session.Level;
    }

    private static void LoggingData(bool golden) {
        // 传送到死亡地点练习时产生的第一次死亡不记录，清除死亡地点
        if (Engine.Scene is not Level level || IsPlayback() || currentDeathInfo == null) {
            Clear();
            return;
        }

        died = false;
        currentDeathInfo.CopyFromSession(level.Session);
        if (golden) {
            currentDeathInfo.LostTime = level.Session.Time;
        } else {
            currentDeathInfo.LostTime = SaveData.Instance.Time - lastTime;
        }

        SpeedrunToolModule.SaveData.Add(currentDeathInfo);
        lastTime = SaveData.Instance.Time;
        currentDeathInfo = null;
    }

    private static string GetCauseOfDeath() {
        StackTrace stackTrace = new(EverestCore.Value ? 4 : 3);
        MethodBase deathMethod = stackTrace.GetFrame(0).GetMethod();
        string death = deathMethod.ReflectedType?.Name ?? "";

        if (death == "Level") {
            death = "Fall";
        } else if (death.Contains("DisplayClass")) {
            death = "Retry";
        } else if (death == "SpikeInfo") {
            death = "Trigger Spike";
        }  else if (death == "Player") {
            death = deathMethod.Name;
            if (death == "OnSquish") {
                death = "Crushed";
            } else if (death == "DreamDashUpdate") {
                death = "Dream Dash";
            } else if (death == "BirdDashTutorialCoroutine") {
                death = "Bird Dash Tutorial";
            }
        } else {
            death = Regex.Replace(death, @"([a-z])([A-Z])", "$1 $2");
        }

        return death;
    }

    public static void TeleportToDeathPosition(DeathInfo deathInfo) {
        playbackDeathInfo = deathInfo;
        Engine.Scene = new LevelLoader(deathInfo.Session.DeepClone());
    }

    public static void Clear() {
        died = false;
        lastTime = SaveData.Instance?.Time ?? 0;
        currentDeathInfo = null;
        playbackDeathInfo = null;
    }
}