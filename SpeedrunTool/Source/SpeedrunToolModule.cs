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
#if RELEASE
        string todo = "Boss Desync (应该好了)" +
            "/ ModSupportNotWell (应该正常了)" +
            "/ speedrun tool 计时器升级优化 (dc 私信)" +
            "/ 增加选项使得 AssetReload 后不要清理存档 (有 mapper 需要, 做好了)" +
            "/ 此外也测一测 entity removed 到底发挥作用没有, 特别是带 hook 副作用的 (目测有, 懒得测了)" +
            "/ ModInterop 加入 IgnoreSaveLoad/ReturnSameObject(type) 的版本 (应该好了)" +
            "/ 引入 Trigger, 使得可以告诉 SRT 哪些关卡是可以安全地存档的 (LuaCutscene 相关). 并在 SRT 发布时告诉 jesss#6307" +
            "       可能此事也加入 ModInterop" +
            "/ 引入 Trigger 或者某种全局实体, 使得可以告诉 SRT 传送的顺序" +
            "/ SRT 群反馈的问题" +
            "/ 安全账户反应的吃心 1f 误差问题" +
            "/ 试着在 Busy 的时候多在控制台 log 一点东西" +
            "/ 跨 level 的 sl 还是很危险, 比如月之佛塔里外存档, 读档的时候会发现估计是资源没加载之类的问题";
        
        throw new NotImplementedException(todo);
#endif

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

    public override void LoadSettings() {
        base.LoadSettings();
        ModSettings.OnLoadSettings();
    }

    public override void CreateModMenuSection(TextMenu menu, bool inGame, EventInstance snapshot) {
        SpeedrunToolMenu.Create(menu, inGame, snapshot);
    }
}