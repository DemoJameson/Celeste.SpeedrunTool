using Celeste.Mod.SpeedrunTool.Message;
using Celeste.Mod.SpeedrunTool.ModInterop;
using Celeste.Mod.SpeedrunTool.Other;
using Celeste.Mod.SpeedrunTool.Utils;
using Force.DeepCloner;
using Force.DeepCloner.Helpers;
using Mono.Cecil.Cil;
using MonoMod.Cil;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
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

    private static List<VirtualInput> unfreezeInputs = new();

    private static void Input_OnInitialize() {
        // 每次重置游戏键位后, 这些 VirtualInput 都是新的对象, 因此必须重新获取
        unfreezeInputs = new List<VirtualInput>() { Input.Dash, Input.Jump, Input.Grab, Input.MoveX, Input.MoveY, Input.Dash, Input.Aim, Input.Pause, Input.CrouchDash };
        Instance?.lastChecks?.Clear();
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
    internal Task<DeepCloneState> preCloneTask;
    private FreezeType freezeType;
    private Process celesteProcess;
    private int savedTasCycleGroupCounter;
    private WeakReference<IEnumerator> transitionRoutine;
    private IEnumerator savedTransitionRoutine;

    public string SlotName;
    public string SlotDescription = "";
    public string FullSlotDescription => string.IsNullOrWhiteSpace(SlotDescription) ? $"[{SlotName}]" : $"[{SlotName}],  {SlotDescription}";

    private enum FreezeType {
        None,
        Save,
        Load
    }

    private readonly HashSet<EventInstance> playingEventInstances = new();

    #region Hook

    // manually call this to ensure it's first called
    internal static void Load() {
        On.Monocle.Scene.BeforeUpdate += SceneOnBeforeUpdate;
        On.Celeste.Level.Update += UpdateBackdropWhenWaiting;
        On.Monocle.Scene.Begin += ClearStateWhenSwitchScene;
        On.Celeste.PlayerDeadBody.End += AutoLoadStateWhenDeath;
        IL.Celeste.Level.TransitionRoutine += LevelOnTransitionRoutine;
        On.Celeste.Level.TransitionRoutine += LevelOnTransitionRoutine;
        On.Celeste.Level.End += LevelOnEnd;
        SaveLoadAction.InternalSafeAdd(
            (_, _) => Instance.UpdateLastChecks(),
            (_, _) => Instance.UpdateLastChecks(),
            () => Instance.lastChecks.Clear()
        );
        Everest.Events.Input.OnInitialize += Input_OnInitialize;
    }

    internal static void Unload() {
        On.Monocle.Scene.BeforeUpdate -= SceneOnBeforeUpdate;
        On.Celeste.Level.Update -= UpdateBackdropWhenWaiting;
        On.Monocle.Scene.Begin -= ClearStateWhenSwitchScene;
        On.Celeste.PlayerDeadBody.End -= AutoLoadStateWhenDeath;
        IL.Celeste.Level.TransitionRoutine -= LevelOnTransitionRoutine;
        On.Celeste.Level.TransitionRoutine -= LevelOnTransitionRoutine;
        On.Celeste.Level.End -= LevelOnEnd;
        Everest.Events.Input.OnInitialize -= Input_OnInitialize;
    }
    private static void LevelOnTransitionRoutine(ILContext context) {
        ILCursor cursor = new(context);
        if (cursor.TryGotoNext(MoveType.After, ins => ins.OpCode == OpCodes.Newobj && ins.Operand.ToString().Contains("Level/<TransitionRoutine>"))) {
            cursor.EmitDelegate(SaveTransitionRoutine);
        }
    }

    private static IEnumerator SaveTransitionRoutine(IEnumerator enumerator) {
        Instance.transitionRoutine = new WeakReference<IEnumerator>(enumerator);
        return enumerator;
    }

    private static IEnumerator LevelOnTransitionRoutine(On.Celeste.Level.orig_TransitionRoutine orig, Level self, LevelData next, Vector2 direction) {
        IEnumerator enumerator = orig(self, next, direction);
        while (enumerator.MoveNext()) {
            yield return enumerator.Current;
        }

        Instance.transitionRoutine = null;
    }

    private static void LevelOnEnd(On.Celeste.Level.orig_End orig, Level self) {
        orig(self);
        Instance.transitionRoutine = null;
    }


    private void UpdateLastChecks() {
        if (ModSettings.FreezeAfterLoadStateType != FreezeAfterLoadStateType.IgnoreHoldingKeys) {
            return;
        }

        foreach (VirtualInput virtualInput in unfreezeInputs) {
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
            if (unfreezeInputs.Any(Instance.IsUnfreeze) || Hotkey.CheckDeathStatistics.Pressed() || Hotkey.LoadState.Pressed()) {
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
                ClearStateImpl(hasGc: true);
            }

            // 重启章节 Level 实例变更，所以之前预克隆的实体作废，需要重新克隆
            if (self is Level) {
                State = State.None;
                PreCloneSavedEntities();
            }

            if (self.GetSession() is { } session && session.Area != savedLevel.Session.Area) {
                ClearStateImpl(hasGc: true);
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

        if (IsSaved) {
            ClearBeforeSave = true;
            ClearStateImpl(hasGc: false);
            ClearBeforeSave = false;
        }
#if DEBUG
        Stopwatch sw = new Stopwatch();
        sw.Start();
#endif

        SaveLoadAction.InitSlots();

        State = State.Saving;
        SavedByTas = tas;

        SaveLoadAction.OnBeforeSaveState(level);
        level.DeepCloneToShared(savedLevel = (Level)RuntimeHelpers.GetUninitializedObject(typeof(Level)));
        savedSaveData = SaveData.Instance.DeepCloneShared();
        savedTasCycleGroupCounter = TasUtils.GroupCounter;
        savedTransitionRoutine = transitionRoutine?.TryGetTarget(out IEnumerator enumerator) == true ? enumerator.DeepCloneShared() : null;
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

        SetSlotDescription();
        Logger.Info("SpeedrunTool", $"Save to {FullSlotDescription}");

#if DEBUG
        sw.Stop();
        if (InGame_Profiling) {
            Logger.Debug("SpeedrunTool", $"Save in {sw.ElapsedMilliseconds} ms");
        }
        if (Log_WhenSaving) {
            SaveLoadAction.LogSavedValues(level: savedLevel);
        }
#endif

        return true;
    }

    private void SetSlotDescription() {
        string levelInfo = Engine.Scene.GetSession() is { } session ? $"'{session.Area.SID} [{session.Level}]'" : "";
        string frames = Engine.Scene is not null ? "(frame: " + (int)Math.Round(Engine.Scene.RawTimeActive / 0.0166667) + ")" : "";
        SlotDescription = levelInfo + frames;
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

#if DEBUG
        if (Log_WhenLoading) {
            SaveLoadAction.LogSavedValues(level: savedLevel);
        }
        Stopwatch sw = new Stopwatch();
        sw.Start();
#endif

        LoadByTas = tas;
        State = State.Loading;

        SaveLoadAction.OnBeforeLoadState(level);

        DeepClonerUtils.SetSharedDeepCloneState(preCloneTask?.Result);

        UpdateTimeAndDeaths(level);
        UnloadLevel(level);

        savedLevel.DeepCloneToShared(level);
        SaveData.Instance = savedSaveData.DeepCloneShared();
        if (savedTransitionRoutine != null) {
            transitionRoutine = new WeakReference<IEnumerator>(savedTransitionRoutine.DeepCloneShared());
        }

        RestoreAudio1(level);
        RestoreCassetteBlockManager1(level);
        SaveLoadAction.OnLoadState(level);

        PreCloneSavedEntities();
        if (!tas && ModSettings.GcAfterLoadState) {
            GcCollect(force: false);
        }

        if (tas) {
            LoadStateComplete(level);
        }
        else {
            // restore cycle hitbox color
            RestoreLevelTime(level);
            FreezeGame(FreezeType.Load);
            DoScreenWipe(level);
        }

        Logger.Info("SpeedrunTool", $"Load from {FullSlotDescription}");

#if DEBUG
        sw.Stop();
        if (InGame_Profiling) {
            Logger.Debug("SpeedrunTool", $"Load in {sw.ElapsedMilliseconds} ms");
            float memorySize = ((float)Process.GetCurrentProcess().PrivateMemorySize64) / (1024L * 1024L * 1024L);
            Logger.Debug("SpeedrunTool", $"MemoryUsage: {memorySize:0.00} GB");
        }
#endif
        return true;
    }


    internal static float MemoryThreshold = 2.5f; // GB

    internal void GcCollect(bool force = false) {
        // 使用内存很难低于这个值, 干脆此时强制执行. ModSettings 里也同样这般设置
        force = force || MemoryThreshold < 1f;
        if (force) {
            Logger.Log("SpeedrunTool", "Force GC Collecting...");
            celesteProcess ??= Process.GetCurrentProcess();
            GcCollectCore();
        }
        else {
            if (celesteProcess == null) {
                celesteProcess = Process.GetCurrentProcess();
            }
            else {
                celesteProcess.Refresh();
            }

            // 使用内存超过阈值才回收垃圾
            float memorySize = ((float)celesteProcess.PrivateMemorySize64) / (1024L * 1024L * 1024L);
            if (memorySize > MemoryThreshold) {
                Logger.Info("SpeedrunTool", $"MemoryUsage: {memorySize:0.00} GB > Threshold: {MemoryThreshold:0.00} GB. Waiting for GC Collecting...");
                GcCollectCore();
            }
        }

        static void GcCollectCore() {
            // 以现在卡顿一些为代价, 保证之后游戏流程中尽量不卡顿 (后者更致命)
            // 作为推论, 我们不应该放在其他线程执行此事
            Stopwatch sw = Stopwatch.StartNew();
            GC.Collect();
            GC.WaitForPendingFinalizers();
            sw.Stop();
            Logger.Info("SpeedrunTool", $"GC latency: {sw.ElapsedMilliseconds}ms.");
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

        // 2025.08.25: 这里我们不能用 level.transition, 因为有可能 TransitionRoutine 已经被 hook 过
        // 使得这个 coroutine 的类型不一样, 这里写的反射获取 <playerStuck> 就不对了
        // 不过理论上多写一些代码去解开 Coroutine 的嵌套应该也行...
        // 这个 bug 被 DJ 在 2023.10.07 修复了, 不过我把多存档合并进来的时候可能不小心丢失了这个提交
        // 总之我们采用 DJ 的写法
        if (savedTransitionRoutine?.GetType().GetFields(BindingFlags.Instance | BindingFlags.NonPublic)
                .FirstOrDefault(info => info.Name.StartsWith("<playerStuck>")) is { } playerStuck) {
            playerStuck.SetValue(savedTransitionRoutine, TimeSpan.FromTicks(session.Time));
            playerStuck.SetValue(savedTransitionRoutine.DeepCloneShared(), TimeSpan.FromTicks(session.Time));
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
        TasUtils.GroupCounter = savedTasCycleGroupCounter;
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
    // https://discord.com/channels/403698615446536203/429775260108324865/1422017531098566776
    // 理论上, 在 Cassette cycle 图中 LoadState 后暂停会引入额外的读图优势, 但这显然不是我们的问题.
    private void RestoreCassetteBlockManager2(Level level) {
        if (level.Tracker.GetEntity<CassetteBlockManager>() is { } manager) {
            if (manager.sfx is { } sfx && !manager.isLevelMusic && manager.leadBeats <= 0) {
                sfx.start();
            }
        }
    }

    internal void ClearStateImpl(bool hasGc = true) {
        // TODO: 这里 Task.Wait() 可能可以试着让它更快结束?

        preCloneTask?.Wait();

        // fix: 读档冻结时被TAS清除状态后无法解除冻结
        if (State == State.Waiting && Engine.Scene is Level level) {
            OutOfFreeze(level);
        }

        playingEventInstances.Clear();
        if (savedLevel is null) {
            hasGc = false;
        }
        savedLevel = null;
        savedSaveData = null;
        preCloneTask = null;
        savedTransitionRoutine = null;
        celesteProcess?.Dispose();
        celesteProcess = null;
        SaveLoadAction.OnClearState(ClearBeforeSave);
        State = State.None;
        // 2025.10.08 fix: clear 之后读档更加卡顿 (这个问题在老版本好像也有, 之前在这里压根不 Gc)
        // 2025.10.19: 不过似乎不是每个人都喜欢卡顿一下, 姑且先做成可选项, 使得用户可以保留之前的体验
        if (hasGc && ModSettings.GcAfterClearState) {
            GcCollect(force: true);
        }
        MoreSaveSlotsUI.Snapshot.RemoveSnapshot(SlotName);
        Logger.Info("SpeedrunTool", $"Clear {FullSlotDescription}");
        SlotDescription = "";
    }

    public void ClearStateAndShowMessage() {
        ClearStateImpl(hasGc: true);
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
        return (State == State.None || State == State.Waiting && AllowSaveLoadWhenWaiting) && (tas || !level.Paused
            && !level.IsPlayerDead() && !level.SkippingCutscene);
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