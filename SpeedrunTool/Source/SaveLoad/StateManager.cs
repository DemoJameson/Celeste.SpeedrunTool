//#define LAG_TEST
using Celeste.Mod.SpeedrunTool.Message;
using Celeste.Mod.SpeedrunTool.ModInterop;
using Celeste.Mod.SpeedrunTool.Other;
using Celeste.Mod.SpeedrunTool.Utils;
using Force.DeepCloner;
using Force.DeepCloner.Helpers;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Runtime.Serialization;
using System.Threading.Tasks;
using EventInstance = FMOD.Studio.EventInstance;

namespace Celeste.Mod.SpeedrunTool.SaveLoad;

public sealed class StateManager {
    public static StateManager Instance => SaveSlotsManager.StateManagerInstance;
    internal StateManager() { }

    private static readonly Lazy<PropertyInfo> InGameOverworldHelperIsOpen = new(
        () => ModUtils.GetType("CollabUtils2", "Celeste.Mod.CollabUtils2.UI.InGameOverworldHelper")?.GetPropertyInfo("IsOpen")
    );

    private readonly Dictionary<VirtualInput, bool> lastChecks = new();

    private readonly List<VirtualInput> unfreezeInputs = new();

    private List<VirtualInput> UnfreezeInputs {
        get {
            unfreezeInputs.Clear();
            unfreezeInputs.Add(Input.Dash);
            unfreezeInputs.Add(Input.Jump);
            unfreezeInputs.Add(Input.Grab);
            unfreezeInputs.Add(Input.MoveX);
            unfreezeInputs.Add(Input.MoveY);
            unfreezeInputs.Add(Input.Dash);
            unfreezeInputs.Add(Input.Aim);
            unfreezeInputs.Add(Input.Pause);

            // 反射兼容 v1312
            if (typeof(Input).GetFieldValue("DemoDash") is VirtualInput demoDash) {
                unfreezeInputs.Add(demoDash);
            }

            if (typeof(Input).GetFieldValue("CrouchDash") is VirtualInput crouchDash) {
                unfreezeInputs.Add(crouchDash);
            }

            return unfreezeInputs;
        }
    }

    internal static bool AllowSaveLoadWhenWaiting = false;

    // public for tas
    public bool IsSaved => savedLevel != null;
    public State State { get; private set; } = State.None;
    public bool SavedByTas { get; private set; }
    public bool LoadByTas { get; private set; }
    public bool ClearBeforeSave { get; private set; }
    public Level SavedLevel => savedLevel;
    private Level savedLevel;
    private SaveData savedSaveData;
    private Task<DeepCloneState> preCloneTask;
    private FreezeType freezeType;
    private Process celesteProcess;
    private int savedTasCycleGroupCounter;
    public string SlotName;

    private enum FreezeType {
        None,
        Save,
        Load
    }

    private readonly HashSet<EventInstance> playingEventInstances = new();

    #region Hook

    // manually call this to ensure it's 
    internal static void Load() {
        On.Monocle.Scene.BeforeUpdate += SceneOnBeforeUpdate;
        On.Celeste.Level.Update += UpdateBackdropWhenWaiting;
        On.Monocle.Scene.Begin += ClearStateWhenSwitchScene;
        On.Celeste.PlayerDeadBody.End += AutoLoadStateWhenDeath;
        SaveLoadAction.InternalSafeAdd(
            (_, _) => Instance.UpdateLastChecks(),
            (_, _) => Instance.UpdateLastChecks(),
            () => Instance.lastChecks.Clear()
        );
    }

    internal static void Unload() {
        On.Monocle.Scene.BeforeUpdate -= SceneOnBeforeUpdate;
        On.Celeste.Level.Update -= UpdateBackdropWhenWaiting;
        On.Monocle.Scene.Begin -= ClearStateWhenSwitchScene;
        On.Celeste.PlayerDeadBody.End -= AutoLoadStateWhenDeath;
    }


    private void UpdateLastChecks() {
        if (ModSettings.FreezeAfterLoadStateType != FreezeAfterLoadStateType.IgnoreHoldingKeys) {
            return;
        }

        foreach (VirtualInput virtualInput in UnfreezeInputs) {
            lastChecks[virtualInput] = virtualInput.IsCheck();
        }
    }

    private bool IsUnfreeze(VirtualInput input) {
        if (input.IsPressed()) {
            return true;
        }

        if (!input.IsCheck()) {
            return false;
        }

        bool lastCheck = ModSettings.FreezeAfterLoadStateType == FreezeAfterLoadStateType.IgnoreHoldingKeys &&
                         lastChecks.TryGetValue(input, out bool value) && value;
        return !lastCheck;
    }

    private static void SceneOnBeforeUpdate(On.Monocle.Scene.orig_BeforeUpdate orig, Scene self) {
        if (ModSettings.Enabled && self is Level level && Instance.State == State.Waiting) {
            if (Instance.UnfreezeInputs.Any(Instance.IsUnfreeze) || Hotkey.CheckDeathStatistics.Pressed() || Hotkey.LoadState.Pressed()) {
                Instance.lastChecks.Clear();
                Instance.OutOfFreeze(level);
            }

            if (Instance.State == State.Waiting) {
                Instance.UpdateLastChecks();
            }
        }

        orig(self);
    }

    // this makes game freeze after save / load
    private static void UpdateBackdropWhenWaiting(On.Celeste.Level.orig_Update orig, Level level) {
        if (Instance.State != State.None) {
            level.Wipe?.Update(level);
            level.HiresSnow?.Update(level);
            level.Foreground.Update(level);
            level.Background.Update(level);
            level.Tracker.GetEntity<Tooltip>()?.Update();
            level.Tracker.GetEntity<NonFrozenMiniTextbox>()?.Update();
            return;
        }

        orig(level);
    }

    private static void ClearStateWhenSwitchScene(On.Monocle.Scene.orig_Begin orig, Scene self) {
        orig(self);
        foreach (SaveSlot slot in SaveSlotsManager.SaveSlots) {
            slot.StateManager.ClearStateWhenSwitchSceneImpl(self);
        }
    }

    private void ClearStateWhenSwitchSceneImpl(Scene self) {
        if (IsSaved) {
            if (self is Overworld && !SavedByTas && InGameOverworldHelperIsOpen.Value?.GetValue(null) as bool? != true) {
                ClearStateImpl();
            }

            // 重启章节 Level 实例变更，所以之前预克隆的实体作废，需要重新克隆
            if (self is Level) {
                State = State.None;
                PreCloneSavedEntities();
            }

            if (self.GetSession() is { } session && session.Area != savedLevel.Session.Area) {
                ClearStateImpl();
            }
        }
    }

    private static void AutoLoadStateWhenDeath(On.Celeste.PlayerDeadBody.orig_End orig, PlayerDeadBody self) {
        if (ModSettings.Enabled
            && ModSettings.AutoLoadStateAfterDeath
            && Instance.IsSaved
            && !Instance.SavedByTas
            && !self.finished
            && Engine.Scene is Level level
            && level.Entities.FindFirst<PlayerSeeker>() == null
           ) {
            level.OnEndOfFrame += () => {
                if (Instance.IsSaved) {
                    Instance.LoadStateImpl(false);
                }
                else {
                    level.DoScreenWipe(wipeIn: false, self.DeathAction ?? level.Reload);
                }
            };
            self.RemoveSelf();
        }
        else {
            orig(self);
        }
    }

    #endregion Hook

#if LAG_TEST
    private static int SaveCount = 0;
    private static int LoadCount = 0;
    private static long SaveElapsedMs = 0;
    private static long LoadElapsedMs = 0;
#endif

    internal bool SaveStateImpl(bool tas) {
        if (Engine.Scene is not Level level) {
            return false;
        }

        if (!IsAllowSave(level, tas)) {
            return false;
        }

        // 不允许在春游图打开章节面板时存档
        if (InGameOverworldHelperIsOpen.Value?.GetValue(null) as bool? == true) {
            return false;
        }

#if LAG_TEST
        Stopwatch sw = new Stopwatch();
        sw.Start();
#endif

        if (IsSaved) {
            ClearBeforeSave = true;
            ClearStateImpl();
            ClearBeforeSave = false;
        }

        SaveLoadAction.InitSlots();

        State = State.Saving;
        SavedByTas = tas;

        SaveLoadAction.OnBeforeSaveState(level);
        level.DeepCloneToShared(savedLevel = (Level)FormatterServices.GetUninitializedObject(typeof(Level)));
        savedSaveData = SaveData.Instance.DeepCloneShared();
        savedTasCycleGroupCounter = TasImports.GroupCounter;
        SaveLoadAction.OnSaveState(level);
        DeepClonerUtils.ClearSharedDeepCloneState();
        PreCloneSavedEntities();
        if (tas) {
            State = State.None;
        }
        else {
            FreezeGame(FreezeType.Save);
            if (TasUtils.HideGamePlay) {
                DoScreenWipe(level);
            }
            else {
                level.Add(new WaitingEntity());
            }
        }

        SaveLoadAction.LogSavedValues(saving: true);
#if LAG_TEST
        sw.Stop();
        SaveCount++;
        SaveElapsedMs += sw.ElapsedMilliseconds;
        Logger.Log("SpeedrunTool", $"Save Cost / Average: {sw.ElapsedMilliseconds}ms / {SaveElapsedMs/SaveCount}ms");
#endif

        Logger.Log("SpeedrunTool", $"Save to [{SlotName}]");

        return true;
    }


    internal bool LoadStateImpl(bool tas) {
        if (Engine.Scene is not Level level) {
            return false;
        }

        if (!tas && level.Paused || State == State.Loading || State == State.Waiting && !AllowSaveLoadWhenWaiting || !IsSaved) {
            return false;
        }

        if (tas && !SavedByTas) {
            return false;
        }

#if LAG_TEST
        Stopwatch sw = new Stopwatch();
        sw.Start();
#endif

        LoadByTas = tas;
        State = State.Loading;

        SaveLoadAction.LogSavedValues(saving: false);

        SaveLoadAction.OnBeforeLoadState(level);

        DeepClonerUtils.SetSharedDeepCloneState(preCloneTask?.Result);

        UpdateTimeAndDeaths(level);
        UnloadLevel(level);

        savedLevel.DeepCloneToShared(level);
        SaveData.Instance = savedSaveData.DeepCloneShared();

        RestoreAudio1(level);
        RestoreCassetteBlockManager1(level);
        SaveLoadAction.OnLoadState(level);
        PreCloneSavedEntities();
        GcCollect();

        if (tas) {
            LoadStateComplete(level);
        }
        else {
            // restore cycle hitbox color
            RestoreLevelTime(level);
            FreezeGame(FreezeType.Load);
            DoScreenWipe(level);
        }

#if LAG_TEST
        sw.Stop();
        LoadCount++;
        LoadElapsedMs += sw.ElapsedMilliseconds;
        Logger.Log("SpeedrunTool", $"Load Cost / Average: {sw.ElapsedMilliseconds}ms / {LoadElapsedMs / LoadCount}ms");
        // strawberry jam heartside load time: 90 - 190 ms (now + only 1 saveslot) / 100 - 300 ms (now + multiple saveslots) /  70 - 170 ms (v3.22)
#endif

        Logger.Log("SpeedrunTool", $"Load from [{SlotName}]");
        return true;
    }

    // 32 位应用且使用内存超过 2GB 才回收垃圾
    private void GcCollect() {
        if (ModSettings.NoGcAfterLoadState || Environment.Is64BitProcess) {
            return;
        }

        if (celesteProcess == null) {
            celesteProcess = Process.GetCurrentProcess();
        }
        else {
            celesteProcess.Refresh();
        }

        if (celesteProcess.PrivateMemorySize64 > 1024L * 1024L * 1024L * 2.5) {
            GC.Collect();
            GC.WaitForPendingFinalizers();
        }
    }

    // 释放资源，停止正在播放的声音等
    private void UnloadLevel(Level level) {
        List<Entity> entities = new();

        // Player 必须最早移除，不然在处理 player.triggersInside 字段时会产生空指针异常
        entities.AddRange(level.Tracker.GetEntities<Player>());

        // 移除当前房间的实体，照抄 level.UnloadLevel() 方法，不直接调用是因为 BingUI 在该方法中将其存储的 level 设置为了 null
        AddNonGlobalEntities(level, entities);

        // 恢復主音乐
        if (level.Tracker.GetEntity<CassetteBlockManager>() is { } cassetteBlockManager) {
            entities.Add(cassetteBlockManager);
        }

        foreach (Entity entity in entities.Distinct()) {
            try {
                entity.Removed(level);
            }
            catch (NullReferenceException) {
                // ignore https://discord.com/channels/403698615446536203/954507384183738438/954507384183738438
            }
        }

        // 移除剩下声音组件
        level.Tracker.GetComponentsCopy<SoundSource>().ForEach(component => component.RemoveSelf());
    }

    private void AddNonGlobalEntities(Level level, List<Entity> entities) {
        int global = (int)Tags.Global;
        foreach (Entity entity in level.Entities) {
            if ((entity.tag & global) == 0) {
                entities.Add(entity);
                continue;
            }

            SaveLoadAction.OnUnloadLevel(level, entities, entity);
        }
    }

    private void UpdateTimeAndDeaths(Level level) {
        if (SavedByTas || ModSettings.SaveTimeAndDeaths) {
            return;
        }

        Session session = level.Session;
        Session savedSession = savedLevel.Session;
        Session clonedSession = savedSession.DeepCloneShared();
        SaveData clonedSaveData = savedSaveData.DeepCloneShared();
        AreaKey areaKey = session.Area;

        clonedSession.Time = savedSession.Time = Math.Max(session.Time, clonedSession.Time);
        clonedSaveData.Time = SaveData.Instance.Time;
        clonedSaveData.Areas_Safe[areaKey.ID].Modes[(int)areaKey.Mode].TimePlayed =
            SaveData.Instance.Areas_Safe[areaKey.ID].Modes[(int)areaKey.Mode].TimePlayed;

        // 修复：切屏时存档，若干秒后读档游戏会误以为卡死自动重生
        if (savedLevel.transition is { } coroutine && coroutine.Current() is { } enumerator
                                                   && enumerator.GetType().GetFields(BindingFlags.Instance | BindingFlags.NonPublic)
                                                       .FirstOrDefault(info => info.Name.StartsWith("<playerStuck>")) is { } playerStuck
           ) {
            playerStuck.SetValue(enumerator, TimeSpan.FromTicks(session.Time));
            playerStuck.SetValue(enumerator.DeepCloneShared(), TimeSpan.FromTicks(session.Time));
        }

        int increaseDeath = level.IsPlayerDead() ? 0 : 1;
        clonedSession.Deaths = savedSession.Deaths = Math.Max(session.Deaths + increaseDeath, clonedSession.Deaths);
        clonedSession.DeathsInCurrentLevel = savedSession.DeathsInCurrentLevel =
            Math.Max(session.DeathsInCurrentLevel + increaseDeath, clonedSession.DeathsInCurrentLevel);
        clonedSaveData.TotalDeaths = savedSaveData.TotalDeaths = SaveData.Instance.TotalDeaths + increaseDeath;
        clonedSaveData.Areas_Safe[areaKey.ID].Modes[(int)areaKey.Mode].Deaths =
            savedSaveData.Areas_Safe[areaKey.ID].Modes[(int)areaKey.Mode].Deaths =
                SaveData.Instance.Areas_Safe[areaKey.ID].Modes[(int)areaKey.Mode].Deaths + increaseDeath;
    }

    private void LoadStateComplete(Level level) {
        RestoreLevelTime(level);
        RestoreAudio2();
        RestoreCassetteBlockManager2(level);
        DeepClonerUtils.ClearSharedDeepCloneState();
        State = State.None;
    }

    private void RestoreLevelTime(Level level) {
        level.TimeActive = savedLevel.TimeActive;
        level.RawTimeActive = savedLevel.RawTimeActive;
    }

    private void RestoreCycleGroupCounter() {
        TasImports.GroupCounter = savedTasCycleGroupCounter;
    }

    // 收集需要继续播放的声音
    private void RestoreAudio1(Level level) {
        playingEventInstances.Clear();

        foreach (Component component in level.Entities.SelectMany(entity => entity.Components.ToArray())) {
            if (component is SoundSource { Playing: true, instance: { } eventInstance }) {
                playingEventInstances.Add(eventInstance);
            }
        }
    }

    // 等 ScreenWipe 完毕再开始播放
    private void RestoreAudio2() {
        foreach (EventInstance instance in playingEventInstances) {
            instance.start();
        }

        playingEventInstances.Clear();
    }

    // 分两步的原因是更早的停止音乐，听起来更舒服更好一点
    // 第一步：停止播放主音乐
    private void RestoreCassetteBlockManager1(Level level) {
        if (level.Tracker.GetEntity<CassetteBlockManager>() is { } manager) {
            manager.snapshot?.start();
        }
    }

    // 第二步：播放节奏音乐
    private void RestoreCassetteBlockManager2(Level level) {
        if (level.Tracker.GetEntity<CassetteBlockManager>() is { } manager) {
            if (manager.sfx is { } sfx && !manager.isLevelMusic && manager.leadBeats <= 0) {
                sfx.start();
            }
        }
    }

    // ReSharper disable once MemberCanBePrivate.Global
    // 为了照顾使用体验，不主动触发内存回收（会卡顿，增加 SaveState 时间）
    public void ClearStateImpl() {
        preCloneTask?.Wait();

        // fix: 读档冻结时被TAS清除状态后无法解除冻结
        if (State == State.Waiting && Engine.Scene is Level level) {
            OutOfFreeze(level);
        }

        playingEventInstances.Clear();
        savedLevel = null;
        savedSaveData = null;
        preCloneTask = null;
        celesteProcess?.Dispose();
        celesteProcess = null;
        SaveLoadAction.OnClearState();
        State = State.None;
        Logger.Log("SpeedrunTool", $"Clear [{SlotName}]");
    }

    public void ClearStateAndShowMessage() {
        ClearStateImpl();
    }

    private void PreCloneSavedEntities() {
        if (IsSaved) {
            SaveLoadAction.OnPreCloneEntities();
            preCloneTask = Task.Run(() => {
                DeepCloneState deepCloneState = new();
                savedLevel.Entities.DeepClone(deepCloneState);
                savedLevel.RendererList.DeepClone(deepCloneState);
                savedSaveData.DeepClone(deepCloneState);
                return deepCloneState;
            });
        }
    }

    private bool IsAllowSave(Level level, bool tas) {
        // 正常游玩时禁止死亡或者跳过过场时存档，TAS 则无以上限制
        // 跳过过场时的黑屏与读档后加的黑屏冲突，会导致一直卡在跳过过场的过程中
        return (State == State.None || State == State.Waiting && AllowSaveLoadWhenWaiting) && !level.Paused
            && (!level.IsPlayerDead() && !level.SkippingCutscene || tas);
    }

    private void FreezeGame(FreezeType freeze) {
        freezeType = freeze;
    }

    private void DoScreenWipe(Level level) {
        level.DoScreenWipe(true, () => {
            if (ModSettings.FreezeAfterLoadStateType != FreezeAfterLoadStateType.Off) {
                State = State.Waiting;
            }
            else {
                OutOfFreeze(level);
            }
        });
    }

    private void OutOfFreeze(Level level) {
        if (freezeType == FreezeType.Save || savedLevel == null) {
            if (savedLevel != null) {
                RestoreLevelTime(level);
            }

            State = State.None;
        }
        else {
            LoadStateComplete(level);
        }

        // when freeze by SRT, the CycleGroupCounter still updates, so we restore it when out of freeze
        RestoreCycleGroupCounter();

        freezeType = FreezeType.None;
    }

    private class WaitingEntity : Entity {
        private bool waitOneFrame = true;

        public override void Render() {
            if (waitOneFrame) {
                waitOneFrame = false;
                return;
            }

            Level level = SceneAs<Level>();
            Instance.DoScreenWipe(level);
            RemoveSelf();
        }
    }
}

public enum State {
    None,
    Saving,
    Loading,
    Waiting,
}