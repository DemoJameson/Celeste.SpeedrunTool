using Celeste.Mod.SpeedrunTool.SaveLoad;
using FMOD.Studio;

namespace Celeste.Mod.SpeedrunTool;

// ReSharper disable once ClassNeverInstantiated.Global
public class SpeedrunToolModule : EverestModule {
    public static SpeedrunToolModule Instance { get; private set; }

    public static SpeedrunToolSaveData SaveData {
        get {
            // copy from max480
            // failsafe: if DeathInfos is null, initialize it. THIS SHOULD NEVER HAPPEN, but already happened in a case of a corrupted save.
            if (((SpeedrunToolSaveData)Instance._SaveData)?.DeathInfos == null) {
                Logger.Log("SpeedrunTool/DeathStatisticsManager",
                    "WARNING: SaveData was null. This should not happen. Initializing it to an empty save data.");
                Instance._SaveData = new SpeedrunToolSaveData();
            }

            return (SpeedrunToolSaveData)Instance._SaveData;
        }
    }

    public SpeedrunToolModule() {
        Instance = this;
        AttributeUtils.CollectMethods<LoadAttribute>();
        AttributeUtils.CollectMethods<UnloadAttribute>();
        AttributeUtils.CollectMethods<LoadContentAttribute>();
        AttributeUtils.CollectMethods<InitializeAttribute>();
    }

    public override Type SettingsType => typeof(SpeedrunToolSettings);

    public override Type SaveDataType => typeof(SpeedrunToolSaveData);

    public override void Load() {
        SaveSlotsManager.SwitchSlot(1); // i don't want to do a bunch of nullity checks
        StateManager.Load();
        AttributeUtils.Invoke<LoadAttribute>();
    }

    public override void Unload() {
        StateManager.Unload();
        AttributeUtils.Invoke<UnloadAttribute>();
    }

    public override void Initialize() {
        AttributeUtils.Invoke<InitializeAttribute>();
    }

    public override void LoadContent(bool firstLoad) {
        if (firstLoad) {
            AttributeUtils.Invoke<LoadContentAttribute>();
        }
    }

    public override void CreateModMenuSection(TextMenu menu, bool inGame, EventInstance snapshot) {
        CreateModMenuSectionHeader(menu, inGame, snapshot);
        SpeedrunToolMenu.Create(menu, inGame, snapshot);
    }
}