using System;
using Celeste.Mod.SpeedrunTool.DeathStatistics;
using Celeste.Mod.SpeedrunTool.RoomTimer;
using Celeste.Mod.SpeedrunTool.SaveLoad;
using Celeste.Mod.SpeedrunTool.TeleportRoom;

namespace Celeste.Mod.SpeedrunTool {
    // ReSharper disable once ClassNeverInstantiated.Global
    public class SpeedrunToolModule : EverestModule {
        public static SpeedrunToolModule Instance;

        public static SpeedrunToolSaveData SaveData {
            get {
                // copy from max480
                // failsafe: if DeathInfos is null, initialize it. THIS SHOULD NEVER HAPPEN, but already happened in a case of a corrupted save.
                if (((SpeedrunToolSaveData) Instance._SaveData)?.DeathInfos == null) {
                    Logger.Log("SpeedrunTool/DeathStatisticsManager",
                        "WARNING: SaveData was null. This should not happen. Initializing it to an empty save data.");
                    Instance._SaveData = new SpeedrunToolSaveData();
                }

                return (SpeedrunToolSaveData) Instance._SaveData;
            }
            set => Instance._SaveData = value;
        }

        public static SpeedrunToolSettings Settings => (SpeedrunToolSettings) Instance._Settings;

        public SpeedrunToolModule() {
            Instance = this;
        }

        // If you don't need to store any settings, => null
        public override Type SettingsType => typeof(SpeedrunToolSettings);
        public static bool Enabled => Settings.Enabled;

        // If you don't need to store any save data, => null
        public override Type SaveDataType => typeof(SpeedrunToolSaveData);

        // Set up any hooks, event handlers and your mod in general here.
        // Load runs before Celeste itself has initialized properly.
        public override void Load() {
            BetterMapEditor.Instance.Load();
            DeathStatisticsManager.Instance.Load();
            RespawnSpeedUtils.Load();
            RoomTimerManager.Instance.Load();
            TeleportRoomUtils.Load();
            StateManager.Instance.OnLoad();
        }

        // Unload the entirety of your mod's content, remove any event listeners and undo all hooks.
        public override void Unload() {
            BetterMapEditor.Instance.Unload();
            DeathStatisticsManager.Instance.Unload();
            RespawnSpeedUtils.Unload();
            RoomTimerManager.Instance.Unload();
            TeleportRoomUtils.Unload();
            StateManager.Instance.OnUnload();
        }

        // Optional, initialize anything after Celeste has initialized itself properly.
        public override void Initialize() {
            RoomTimerManager.Instance.Init();
            ButtonConfigUi.Init();
        }
    }
}