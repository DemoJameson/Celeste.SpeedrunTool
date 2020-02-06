using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Xna.Framework;
using Monocle;

namespace Celeste.Mod.SpeedrunTool {
    public static class DeathStatisticsUtils {
        private static readonly string LogDir = Path.Combine(Everest.PathSettings, "SpeedrunTool");
        private static readonly string LogFile = Path.Combine(LogDir, "Death Statistics.txt");
        private static readonly string LogFileForStreaming = Path.Combine(LogDir, "Death Statistics for Streaming.txt");
        private static long lastTime;
        private static bool died;
        private static string causeOfDeath;
        private static long totalLostTime = 0;
        private static bool Enabled => SpeedrunToolModule.Settings.DeathStatistics;

        public static void Load() {
            On.Celeste.Player.Die += PlayerOnDie;
            On.Celeste.Level.NextLevel += LevelOnNextLevel;
            On.Celeste.Player.Update += PlayerOnUpdate;
            On.Celeste.OuiFileSelectSlot.EnterFirstArea += OuiFileSelectSlotOnEnterFirstArea;
            On.Celeste.ChangeRespawnTrigger.OnEnter += ChangeRespawnTriggerOnOnEnter;
            On.Celeste.Session.SetFlag += UpdateTimerStateOnTouchFlag;
            On.Celeste.LevelLoader.ctor += LevelLoaderOnCtor;
        }

        public static void Unload() {
            On.Celeste.Player.Die -= PlayerOnDie;
            On.Celeste.Level.NextLevel -= LevelOnNextLevel;
            On.Celeste.Player.Update -= PlayerOnUpdate;
            On.Celeste.OuiFileSelectSlot.EnterFirstArea -= OuiFileSelectSlotOnEnterFirstArea;
            On.Celeste.ChangeRespawnTrigger.OnEnter -= ChangeRespawnTriggerOnOnEnter;
            On.Celeste.Session.SetFlag -= UpdateTimerStateOnTouchFlag;
            On.Celeste.LevelLoader.ctor -= LevelLoaderOnCtor;
        }

        public static void Init() {
            Directory.CreateDirectory(LogDir);
            if (!File.Exists(LogFile)) {
                InitFile();
            }
        }

        private static void LevelLoaderOnCtor(On.Celeste.LevelLoader.orig_ctor orig, LevelLoader self, Session session, Vector2? startPosition) {
            orig(self, session, startPosition);
            
            lastTime = SaveData.Instance.Time;
            SpeedrunToolModule.Settings.LastDeathStatistics = Enabled;
        }

        private static void UpdateTimerStateOnTouchFlag(On.Celeste.Session.orig_SetFlag origSetFlag, Session session,
            string flag, bool setTo) {
            origSetFlag(session, flag, setTo);

            if (flag.StartsWith("summit_checkpoint_") && setTo) {
                lastTime = SaveData.Instance.Time;
            }
        }

        private static void ChangeRespawnTriggerOnOnEnter(On.Celeste.ChangeRespawnTrigger.orig_OnEnter orig, ChangeRespawnTrigger self, Player player) {
            orig(self, player);

            if (self.Scene.CollideCheck<Solid>(self.Target + Vector2.UnitY * -4f)) {
                return;
            }

            lastTime = SaveData.Instance.Time;
        }

        private static void OuiFileSelectSlotOnEnterFirstArea(On.Celeste.OuiFileSelectSlot.orig_EnterFirstArea orig, OuiFileSelectSlot self) {
            orig(self);
            totalLostTime = 0;
            lastTime = 0;
            using (StreamWriter sw = File.AppendText(LogFile)) {
                sw.WriteLine("---------- New Run ----------");
            }

            using (StreamWriter sw = File.AppendText(LogFileForStreaming)) {
                sw.WriteLine("---------- New Run ----------");
            }
        }

        private static void PlayerOnUpdate(On.Celeste.Player.orig_Update orig, Player self) {
            orig(self);

            if (Enabled && died && (self.StateMachine.State == Player.StNormal || self.StateMachine.State == Player.StSwim)) {
                died = false;
                LoggingData(self);
            }
            
            if (Enabled && !SpeedrunToolModule.Settings.LastDeathStatistics) {
                self.SceneAs<Level>().Add(new MiniTextbox(DialogIds.DialogDeathStatisticsDescription));
            }
            SpeedrunToolModule.Settings.LastDeathStatistics = Enabled;
        }

        private static void LevelOnNextLevel(On.Celeste.Level.orig_NextLevel orig, Level self, Vector2 at, Vector2 dir) {
            orig(self, at, dir);

            if (Enabled) {
                lastTime = SaveData.Instance.Time;
            }
        }

        private static PlayerDeadBody PlayerOnDie(On.Celeste.Player.orig_Die orig, Player self, Vector2 direction, bool evenIfInvincible, bool registerDeathInStats) {
            PlayerDeadBody playerDeadBody = orig(self, direction, evenIfInvincible, registerDeathInStats);

            if (playerDeadBody != null && Enabled) {
                died = true;
                causeOfDeath = GetCauseOfDeath();
            }

            return playerDeadBody;
        }

        private static void LoggingData(Player player) {
            Level level = player.SceneAs<Level>();
            if (level == null) {
                return;
            }

            Session session = level.Session;

            TimeSpan timeSpan = TimeSpan.FromTicks(SaveData.Instance.Time);
            int totalHours = (int) timeSpan.TotalHours;
            string fileTime = totalHours + timeSpan.ToString("\\:mm\\:ss\\.fff");

            string chapterTime = TimeSpan.FromTicks(session.Time).ShortGameplayFormat();

            totalLostTime += SaveData.Instance.Time - lastTime;

            TimeSpan lostTimeSpan = TimeSpan.FromTicks(SaveData.Instance.Time - lastTime);
            string lostTime = (int) lostTimeSpan.TotalSeconds + lostTimeSpan.ToString("\\.fff");
            lastTime = SaveData.Instance.Time;


            // LogFile
            StringBuilder contents = new StringBuilder();
            contents.Append(GetLevelName(session) + "\t");
            contents.Append(session.Level + "\t");
            contents.Append(lostTime + "\t");
            contents.Append(causeOfDeath + "\t");
            contents.Append(fileTime + "\t");
            contents.Append(chapterTime + "\t");
            contents.Append(GetTeleportLink(session));

            if (!File.Exists(LogFile)) {
                InitFile();
            }

            using (StreamWriter sw = File.AppendText(LogFile)) {
                sw.WriteLine(contents.ToString());
            }

            // LogFileForStreaming
            if (!File.Exists(LogFileForStreaming)) {
                File.WriteAllText(LogFileForStreaming, "");
            }

            StringBuilder streamingContents = new StringBuilder();
            streamingContents.Append(GetLevelName(session) + "　");
            if (lostTime.Length == 5) {
                lostTime = " " + lostTime;
            }

            streamingContents.Append(lostTime + "　");
            streamingContents.Append(causeOfDeath + "\n");
            streamingContents.Append("Total Deaths: " + SaveData.Instance.TotalDeaths + "　");

            TimeSpan totalLostTimeSpan = TimeSpan.FromTicks(totalLostTime);
            string totalLostTimeText = (int) totalLostTimeSpan.TotalSeconds + totalLostTimeSpan.ToString("\\.fff");

            streamingContents.Append("Lost Time: " + totalLostTimeText);

            List<string> allLines = File.ReadLines(LogFileForStreaming).ToList();
            if (allLines.Count > 1) {
                File.WriteAllLines(LogFileForStreaming, allLines.GetRange(0, allLines.Count - 1));
            }

            using (StreamWriter sw = File.AppendText(LogFileForStreaming)) {
                sw.WriteLine(streamingContents.ToString());
            }
        }

        private static string GetTeleportLink(Session session) {
            string result = "http://localhost:32270/tp?";
            result += "area=" + AreaData.Get(session).SID;
            result += "&side=" + GetSideText(session.Area.Mode);
            result += "&level=" + session.Level;

            return result;
        }

        private static string GetSideText(AreaMode areaMode) {
            switch (areaMode) {
                case AreaMode.Normal:
                    return "A";
                case AreaMode.BSide:
                    return "B";
                case AreaMode.CSide:
                    return "C";
                default:
                    throw new ArgumentOutOfRangeException();
            }
        }

        private static void InitFile() {
            StringBuilder contents = new StringBuilder();
            contents.Append("Level\t");
            contents.Append("Room\t");
            contents.Append("Lost Time\t");
            contents.Append("Cause of Death\t");
            contents.Append("File Time\t");
            contents.Append("Chapter Time\t");
            contents.Append("Teleport To Map\n");

            File.WriteAllText(LogFile, contents.ToString());
        }

        private static string GetLevelName(Session session) {
            string levelName = Dialog.Get(AreaData.Get(session).Name, Dialog.Languages["english"]);
            string levelMode;

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

            switch (session.Area.Mode) {
                case AreaMode.Normal:
                    levelMode = "A";
                    break;
                case AreaMode.BSide:
                    levelMode = "B";
                    break;
                case AreaMode.CSide:
                    levelMode = "C";
                    break;
                default:
                    throw new ArgumentOutOfRangeException();
            }

            if (levelName.Length == 1) {
                return levelName + levelMode;
            }

            if (AreaData.Get(session).Interlude) {
                return levelName;
            }

            return levelName + " " + levelMode;
        }

        private static string GetCauseOfDeath() {
            StackTrace stackTrace = new StackTrace();
            string death = stackTrace.GetFrame(3).GetMethod().ReflectedType?.Name ?? "";

            if (death == "Level") {
                death = "Fall";
            }
            else if (death.Contains("DisplayClass")) {
                death = "Retry";
            }
            else if (death == "Player") {
                death = stackTrace.GetFrame(3).GetMethod().Name;
                if (death == "OnSquish") {
                    death = "Crushed";
                }
                else if (death == "DreamDashUpdate") {
                    death = "Dream Dash";
                }
                else if (death == "BirdDashTutorialCoroutine") {
                    death = "Bird Dash Tutorial";
                }
            }

            else {
                death = Regex.Replace(death, @"([a-z])([A-Z])", "$1 $2");
            }


            return death;
        }
    }
}