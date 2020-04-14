using System;
using System.Diagnostics;
using System.Reflection;
using System.Text.RegularExpressions;
using Celeste.Mod.SpeedrunTool.Extensions;
using Microsoft.Xna.Framework;
using Monocle;
using static Celeste.Mod.SpeedrunTool.ButtonConfigUi;

namespace Celeste.Mod.SpeedrunTool.DeathStatistics {
    public class DeathStatisticsManager {
        // @formatter:off
        private static readonly Lazy<DeathStatisticsManager> Lazy = new Lazy<DeathStatisticsManager>(() => new DeathStatisticsManager());
        public static DeathStatisticsManager Instance => Lazy.Value;
        private DeathStatisticsManager() { }
        // @formatter:on

        private long lastTime;
        private bool died;
        private string causeOfDeath;
        private Vector2 deathPosition;

        private DeathInfo teleportDeathInfo;
        private bool Enabled => SpeedrunToolModule.Settings.DeathStatistics;

        public void Load() {
            On.Celeste.Player.Die += PlayerOnDie;
            On.Celeste.Level.NextLevel += LevelOnNextLevel;
            On.Celeste.Player.Update += PlayerOnUpdate;
            On.Celeste.OuiFileSelectSlot.EnterFirstArea += OuiFileSelectSlotOnEnterFirstArea;
            On.Celeste.ChangeRespawnTrigger.OnEnter += ChangeRespawnTriggerOnOnEnter;
            On.Celeste.Session.SetFlag += UpdateTimerStateOnTouchFlag;
            On.Celeste.LevelLoader.ctor += LevelLoaderOnCtor;
            On.Celeste.Level.Update += LevelOnUpdate;
            On.Celeste.Player.Added += PlayerOnAdded;
            On.Celeste.LevelExit.ctor += LevelExitOnCtor;
        }

        public void Unload() {
            On.Celeste.Player.Die -= PlayerOnDie;
            On.Celeste.Level.NextLevel -= LevelOnNextLevel;
            On.Celeste.Player.Update -= PlayerOnUpdate;
            On.Celeste.OuiFileSelectSlot.EnterFirstArea -= OuiFileSelectSlotOnEnterFirstArea;
            On.Celeste.ChangeRespawnTrigger.OnEnter -= ChangeRespawnTriggerOnOnEnter;
            On.Celeste.Session.SetFlag -= UpdateTimerStateOnTouchFlag;
            On.Celeste.LevelLoader.ctor -= LevelLoaderOnCtor;
            On.Celeste.Level.Update -= LevelOnUpdate;
            On.Celeste.Player.Added -= PlayerOnAdded;
        }

        private void LevelExitOnCtor(On.Celeste.LevelExit.orig_ctor orig, LevelExit self, LevelExit.Mode mode, Session session, HiresSnow snow) {
            orig(self, mode, session, snow);
            
            Clear();
        }

        private void PlayerOnAdded(On.Celeste.Player.orig_Added orig, Player self, Scene scene) {
            orig(self, scene);
            if (scene is Level level && teleportDeathInfo != null && teleportDeathInfo.Area == level.Session.Area &&
                teleportDeathInfo.Room == level.Session.Level) {
                scene.Add(new DeathMarker(teleportDeathInfo.DeathPosition));
            }
        }

        private void LevelOnUpdate(On.Celeste.Level.orig_Update orig, Level level) {
            orig(level);
            if (!SpeedrunToolModule.Enabled) {
                return;
            }

            if (level.Paused) {
                return;
            }

            CheckDeathStatisticsButton(level);
        }

        private void CheckDeathStatisticsButton(Level level) {
            VirtualButton virtualButton = GetVirtualButton(Mappings.CheckDeathStatistics);
            if (virtualButton.Pressed) {
                virtualButton.ConsumePress();

                level.Paused = true;
                DeathStatisticsUi buttonConfigUi = new DeathStatisticsUi {
                    OnClose = () => level.Paused = false
                };
                level.Add(buttonConfigUi);
                level.OnEndOfFrame += (Action) (() => level.Entities.UpdateLists());
            }
        }

        private void LevelLoaderOnCtor(On.Celeste.LevelLoader.orig_ctor orig, LevelLoader self, Session session,
            Vector2? startPosition) {
            orig(self, session, startPosition);

            lastTime = SaveData.Instance.Time;
        }

        private void UpdateTimerStateOnTouchFlag(On.Celeste.Session.orig_SetFlag origSetFlag, Session session,
            string flag, bool setTo) {
            origSetFlag(session, flag, setTo);

            if (flag.StartsWith("summit_checkpoint_") && setTo) {
                lastTime = SaveData.Instance.Time;
            }
        }

        private void ChangeRespawnTriggerOnOnEnter(On.Celeste.ChangeRespawnTrigger.orig_OnEnter orig,
            ChangeRespawnTrigger self, Player player) {
            orig(self, player);

            if (self.Scene.CollideCheck<Solid>(self.Target + Vector2.UnitY * -4f)) {
                return;
            }

            lastTime = SaveData.Instance.Time;
        }

        private void OuiFileSelectSlotOnEnterFirstArea(On.Celeste.OuiFileSelectSlot.orig_EnterFirstArea orig,
            OuiFileSelectSlot self) {
            orig(self);

            if (!Enabled) {
                return;
            }

            lastTime = 0;
        }

        private void PlayerOnUpdate(On.Celeste.Player.orig_Update orig, Player self) {
            orig(self);

            if (Enabled && died &&
                (self.StateMachine.State == Player.StNormal || self.StateMachine.State == Player.StSwim)) {
                died = false;
                LoggingData(self);
            }
        }

        private void LevelOnNextLevel(On.Celeste.Level.orig_NextLevel orig, Level self, Vector2 at, Vector2 dir) {
            orig(self, at, dir);

            if (Enabled) {
                lastTime = SaveData.Instance.Time;
            }
        }

        private PlayerDeadBody PlayerOnDie(On.Celeste.Player.orig_Die orig, Player self, Vector2 direction,
            bool evenIfInvincible, bool registerDeathInStats) {
            PlayerDeadBody playerDeadBody = orig(self, direction, evenIfInvincible, registerDeathInStats);

            if (playerDeadBody != null && Enabled) {
                died = true;
                causeOfDeath = GetCauseOfDeath();
                deathPosition = self.Position;
            }

            return playerDeadBody;
        }

        private void LoggingData(Player player) {
            Level level = player.SceneAs<Level>();
            if (level == null) {
                return;
            }

            Session session = level.Session;
            Session cloneSession = session.DeepClone();

            long lostTime = SaveData.Instance.Time - lastTime;

            DeathInfo deathInfo = new DeathInfo {
                Chapter = GetChapterName(session),
                Room = session.Level,
                LostTime = lostTime,
                CauseOfDeath = causeOfDeath,
                DeathPosition = deathPosition,
                Area = cloneSession.Area,
                RespawnPoint = (Vector2) cloneSession.RespawnPoint,
                Inventory = cloneSession.Inventory,
                Flags = cloneSession.Flags,
                LevelFlags = cloneSession.LevelFlags,
                Strawberries = cloneSession.Strawberries,
                DoNotLoad = cloneSession.DoNotLoad,
                Keys = cloneSession.Keys,
                SummitGems = cloneSession.SummitGems,
                FurthestSeenLevel = cloneSession.FurthestSeenLevel,
                Time = cloneSession.Time,
                StartedFromBeginning = cloneSession.StartedFromBeginning,
                Deaths = cloneSession.Deaths,
                Dashes = cloneSession.Dashes,
                DashesAtLevelStart = cloneSession.DashesAtLevelStart,
                DeathsInCurrentLevel = cloneSession.DeathsInCurrentLevel,
                InArea = cloneSession.InArea,
                StartCheckpoint = cloneSession.StartCheckpoint,
                FirstLevel = cloneSession.FirstLevel,
                Cassette = cloneSession.Cassette,
                HeartGem = cloneSession.HeartGem,
                Dreaming = cloneSession.Dreaming,
                ColorGrade = cloneSession.ColorGrade,
                LightingAlphaAdd = cloneSession.LightingAlphaAdd,
                BloomBaseAdd = cloneSession.BloomBaseAdd,
                DarkRoomAlpha = cloneSession.DarkRoomAlpha,
                CoreMode = cloneSession.CoreMode,
                GrabbedGolden = cloneSession.GrabbedGolden,
                HitCheckpoint = cloneSession.HitCheckpoint,
            };
            SpeedrunToolModule.SaveData.Add(deathInfo);
            lastTime = SaveData.Instance.Time;
        }

        private string GetSideText(AreaMode areaMode) {
            switch (areaMode) {
                case AreaMode.Normal:
                    return "A";
                case AreaMode.BSide:
                    return "B";
                case AreaMode.CSide:
                    return "C";
                default:
                    return "Unknown";
            }
        }

        private string GetChapterName(Session session) {
            string levelName = Dialog.Get(AreaData.Get(session).Name, Dialog.Languages["english"]);
            string levelMode = GetSideText(session.Area.Mode);

            switch (levelName) {
                case "Forsaken City":
                    levelName = "1";
                    break;
                case "Old Site":
                    levelName = "2";
                    break;
                case "Celestial Resort":
                    levelName = "3";
                    break;
                case "Golden Ridge":
                    levelName = "4";
                    break;
                case "Mirror Temple":
                    levelName = "5";
                    break;
                case "Reflection":
                    levelName = "6";
                    break;
                case "The Summit":
                    levelName = "7";
                    break;
                case "Core":
                    levelName = "8";
                    break;
            }

            if (levelName.Length == 1) {
                return levelName + levelMode;
            }

            if (AreaData.Get(session).Interlude) {
                return levelName;
            }

            if (levelName == "Farewell") {
                return levelName;
            }

            return levelName + " " + levelMode;
        }

        private string GetCauseOfDeath() {
            StackTrace stackTrace = new StackTrace();
            MethodBase deathMethod = stackTrace.GetFrame(6).GetMethod();
            string death = deathMethod.ReflectedType?.Name ?? "";

            if (death == "Level") {
                death = "Fall";
            } else if (death.Contains("DisplayClass")) {
                death = "Retry";
            } else if (death == "Player") {
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

        public void SetTeleportDeathInfo(DeathInfo deathInfo) {
            this.teleportDeathInfo = deathInfo;
        }

        public void Clear() {
            teleportDeathInfo = null;
        }
    }
}