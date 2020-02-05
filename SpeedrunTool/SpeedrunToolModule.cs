using System;
using Celeste.Mod.SpeedrunTool.RoomTimer;
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
        public override Type SettingsType => typeof(SpeedrunToolSettings);
        public static SpeedrunToolSettings Settings => (SpeedrunToolSettings) Instance._Settings;
        public static bool Enabled => Settings.Enabled;

        // If you don't need to store any save data, => null
        public override Type SaveDataType => null; //typeof(ExampleSaveData);

        // Set up any hooks, event handlers and your mod in general here.
        // Load runs before Celeste itself has initialized properly.
        public override void Load() {
            BetterMapEditor.Instance.Load();
            RoomTimerManager.Instance.Load();
            StateManager.Instance.Load();
            DeathStatistics.Load();
            
            Engine.Update += RespawnSpeed;
        }

        // Unload the entirety of your mod's content, remove any event listeners and undo all hooks.
        public override void Unload() {
            BetterMapEditor.Instance.Unload();
            RoomTimerManager.Instance.Unload();
            StateManager.Instance.Unload();
            DeathStatistics.Unload();
            
            Engine.Update -= RespawnSpeed;
        }

        // Optional, initialize anything after Celeste has initialized itself properly.
        public override void Initialize() {
            BetterMapEditor.Init();
            StateManager.Instance.Init();
            RoomTimerManager.Instance.Init();
            DeathStatistics.Init();
        }

        private static void RespawnSpeed(Engine.orig_Update orig, Monocle.Engine self, GameTime time) {
            orig(self, time);

            if (!Settings.Enabled) {
                return;
            }

            if (!(Monocle.Engine.Scene is Level level)) {
                return;
            }

            Player player = level.Entities.FindFirst<Player>();

            // level 场景中 player == null 代表人物死亡
            if (player != null && player.StateMachine.State == Player.StIntroRespawn || player == null) {
                for (int i = 1; i < Settings.RespawnSpeedInt; i++) {
                    orig(self, time);
                }
            }
        }
    }
}