using System;
using Celeste.Mod.SpeedrunTool.RoomTimer;
using Celeste.Mod.SpeedrunTool.SaveLoad;
using Celeste.Mod.SpeedrunTool.TeleportRoom;

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
            DeathStatisticsUtils.Load();
            RespawnSpeedUtils.Load();
            RoomTimerManager.Instance.Load();
            TeleportRoomUtils.Load();
            StateManager.Instance.Load();
        }

        // Unload the entirety of your mod's content, remove any event listeners and undo all hooks.
        public override void Unload() {
            BetterMapEditor.Instance.Unload();
            DeathStatisticsUtils.Unload();
            RespawnSpeedUtils.Unload();
            RoomTimerManager.Instance.Unload();
            TeleportRoomUtils.Unload();
            StateManager.Instance.Unload();
        }

        // Optional, initialize anything after Celeste has initialized itself properly.
        public override void Initialize() {
            BetterMapEditor.Init();
            DeathStatisticsUtils.Init();
            RoomTimerManager.Instance.Init();
            TeleportRoomUtils.Init();
            StateManager.Instance.Init();
        }
    }
}