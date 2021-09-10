using System;
using Celeste.Mod.SpeedrunTool.DeathStatistics;
using Celeste.Mod.SpeedrunTool.Other;
using Celeste.Mod.SpeedrunTool.RoomTimer;
using Celeste.Mod.SpeedrunTool.TeleportRoom;
using FMOD.Studio;

namespace Celeste.Mod.SpeedrunTool {
    // ReSharper disable once ClassNeverInstantiated.Global
    public class SpeedrunToolModule : EverestModule {
        public static SpeedrunToolModule Instance { get; private set; }

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
        public static bool Enabled => Settings.Enabled;

        public SpeedrunToolModule() {
            Instance = this;
        }

        // If you don't need to store any settings, => null
        public override Type SettingsType => typeof(SpeedrunToolSettings);

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
            HotkeyConfigUi.Load();
            MuteInBackground.Load();
        }

        // Unload the entirety of your mod's content, remove any event listeners and undo all hooks.
        public override void Unload() {
            BetterMapEditor.Instance.Unload();
            DeathStatisticsManager.Instance.Unload();
            RespawnSpeedUtils.Unload();
            RoomTimerManager.Instance.Unload();
            TeleportRoomUtils.Unload();
            StateManager.Instance.OnUnload();
            HotkeyConfigUi.Unload();
            MuteInBackground.Unload();
        }

        // Optional, initialize anything after Celeste has initialized itself properly.
        public override void Initialize() {
            HotkeyConfigUi.Init();
        }

        public override void LoadContent(bool firstLoad) {
            if (firstLoad) {
                SaveLoadAction.OnLoadContent();
            }
        }

        public override void CreateModMenuSection(TextMenu menu, bool inGame, EventInstance snapshot) {
            CreateModMenuSectionHeader(menu, inGame, snapshot);
            SpeedrunToolMenu.Create(menu, inGame, snapshot);
        }
    }
}