using System;
using Celeste.Mod.SpeedrunTool.SaveLoad;
using Microsoft.Xna.Framework;
using On.Monocle;

namespace Celeste.Mod.SpeedrunTool {
    // ReSharper disable once ClassNeverInstantiated.Global
    public class SpeedrunToolModule : EverestModule {
        public static SpeedrunToolModule Instance;
        //public static ExampleSaveData SaveData => (ExampleSaveData)Instance._SaveData;

        public SpeedrunToolModule() {
            Instance = this;
        }

        // If you don't need to store any settings, => null
        public override Type SettingsType => typeof(SpeedrunToolModuleSettings);
        public static SpeedrunToolModuleSettings Settings => (SpeedrunToolModuleSettings) Instance._Settings;

        // If you don't need to store any save data, => null
        public override Type SaveDataType => null; //typeof(ExampleSaveData);

        // Set up any hooks, event handlers and your mod in general here.
        // Load runs before Celeste itself has initialized properly.
        public override void Load() {
            BetterMapEditor.Instance.Load();
            StateManager.Instance.Load();
            RoomTimerManager.Instance.Load();
            On.Celeste.PlayerDeadBody.End += QuickLoadWhenDeath;
            On.Celeste.LevelEnter.Go += SkipChapterIntro;
            On.Celeste.LevelExit.ctor += SkipChapterComplete;
            Engine.Update += RespawnSpeed;
        }

        // Unload the entirety of your mod's content, remove any event listeners and undo all hooks.
        public override void Unload() {
            BetterMapEditor.Instance.Unload();
            StateManager.Instance.Unload();
            RoomTimerManager.Instance.Unload();
            On.Celeste.PlayerDeadBody.End -= QuickLoadWhenDeath;
            On.Celeste.LevelEnter.Go -= SkipChapterIntro;
            On.Celeste.LevelExit.ctor -= SkipChapterComplete;
            Engine.Update -= RespawnSpeed;
        }

        // Optional, initialize anything after Celeste has initialized itself properly.
        public override void Initialize() {
            BetterMapEditor.Init();
            StateManager.Instance.Init();
            RoomTimerManager.Instance.Init();
        }

        private static void RespawnSpeed(Engine.orig_Update orig, Monocle.Engine self, GameTime time) {
            orig(self, time);

            if (!Settings.Enabled) return;
            if (!(Monocle.Engine.Scene is Level level)) return;

            Player player = level.Tracker.GetEntity<Player>();

            // level 场景中 player == null 代表人物死亡
            if (player != null && player.StateMachine.State == Player.StIntroRespawn || player == null)
                for (int i = 1; i < Settings.RespawnSpeedInt; i++)
                    orig(self, time);
        }

        private static void QuickLoadWhenDeath(On.Celeste.PlayerDeadBody.orig_End orig, PlayerDeadBody self) {
            orig(self);
            if (Settings.Enabled && Settings.AutoLoadAfterDeath) StateManager.Instance.QuickLoad();
        }

        private static void SkipChapterIntro(On.Celeste.LevelEnter.orig_Go orig, Session session, bool data) {
            if (!Settings.Enabled) {
                orig(session, data);
                return;
            }

            bool skipIntro = (Settings.SkipSceneOption & SkipSceneOption.Intro) != 0;
            orig(session, skipIntro || data);
        }

        private static void SkipChapterComplete(On.Celeste.LevelExit.orig_ctor orig, LevelExit self,
            LevelExit.Mode mode, Session session, HiresSnow snow) {
            if (!Settings.Enabled) {
                orig(self, mode, session, snow);
                return;
            }

            bool skipComplete = (Settings.SkipSceneOption & SkipSceneOption.Complete) != 0;
            if (skipComplete && mode == LevelExit.Mode.Completed) mode = LevelExit.Mode.CompletedInterlude;

            orig(self, mode, session, snow);
        }
    }
}