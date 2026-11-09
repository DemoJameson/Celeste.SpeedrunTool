using Celeste.Mod.SpeedrunTool.ModInterop;
using Celeste.Mod.SpeedrunTool.SaveLoad.Profiling;
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
using System.Runtime;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using EventInstance = FMOD.Studio.EventInstance;

namespace Celeste.Mod.SpeedrunTool.SaveLoad;

public sealed class StateManager {
    public static StateManager Instance => SaveSlotsManager.StateManagerInstance;
    internal StateManager() { }

    internal static bool AllowSaveLoadWhenWaiting = false;

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
    private static string GenerateSlotDescription() {
        string levelInfo = Engine.Scene.GetSession() is { } session ? $"'{session.Area.SID} [{session.Level}]'" : "";
        string frames = Engine.Scene is not null ? "(frame: " + (int)Math.Round(Engine.Scene.RawTimeActive / 0.0166667) + ")" : "";
        return levelInfo + frames;
    }
    private enum FreezeType {
        None,
        Save,
        Load
    }

    private readonly HashSet<EventInstance> playingEventInstances = [];


    #region Hook

    // manually call this to ensure it's first called
    internal static void Load() {
        On.Celeste.PlayerDeadBody.End += AutoLoadStateWhenDeath;
        IL.Celeste.Level.TransitionRoutine += IL_LevelTransitionRoutine;
        On.Celeste.Level.TransitionRoutine += On_LevelTransitionRoutine;
        On.Celeste.Level.End += LevelOnEnd;
    }

    internal static void Unload() {
        On.Celeste.PlayerDeadBody.End -= AutoLoadStateWhenDeath;
        IL.Celeste.Level.TransitionRoutine -= IL_LevelTransitionRoutine;
        On.Celeste.Level.TransitionRoutine -= On_LevelTransitionRoutine;
        On.Celeste.Level.End -= LevelOnEnd;
    }

    private static void IL_LevelTransitionRoutine(ILContext context) {
        ILCursor cursor = new(context);
        if (cursor.TryGotoNext(MoveType.After, ins => ins.OpCode == OpCodes.Newobj && ins.Operand.ToString().Contains("Level/<TransitionRoutine>"))) {
            cursor.EmitDelegate(SaveTransitionRoutine);
        }
    }

    private static IEnumerator SaveTransitionRoutine(IEnumerator enumerator) {
        Instance.transitionRoutine = new WeakReference<IEnumerator>(enumerator);
        return enumerator;
    }

    private static IEnumerator On_LevelTransitionRoutine(On.Celeste.Level.orig_TransitionRoutine orig, Level self, LevelData next, Vector2 direction) {
        IEnumerator enumerator = orig(self, next, direction);
        while (enumerator.MoveNext()) {
            yield return enumerator.Current;
        }

        Instance.transitionRoutine = null;
    }

    private static void LevelOnEnd(On.Celeste.Level.orig_End orig, Level self) {
        orig(self);
        Instance.transitionRoutine = null;
        DesyncRiskAnalyzer.CheckAll = true;
    }



    private static readonly Lazy<PropertyInfo> InGameOverworldHelperIsOpen = new(
        () => ModUtils.GetType("CollabUtils2", "Celeste.Mod.CollabUtils2.UI.InGameOverworldHelper")?.GetPropertyInfo("IsOpen")
    );

    internal void HandleSwitchScene(Scene self) {
        if (!IsSaved) {
            return;
        }

        // v3.28 新功能: 切换 Scene 时不再自动删除 SaveState.
        // 由于我们还保存了 Celeste.SaveData.Instance, 因此我们的多存档甚至可以存储 不同的原版存档文件上的不同章节
        // todo: 目前这个功能还有问题, 比如资源有时候好像没有正确加载

        if (ModSettings.AutoClearStateOnSceneSwitch) {
            if (self is Overworld && !SavedByTas && InGameOverworldHelperIsOpen.Value?.GetValue(null) as bool? != true
                || self.GetSession() is { } session && session.Area != savedLevel.Session.Area) {
                ClearStateImpl(hasGc: true);
            }
        }

        // 重启章节 Level 实例变更，所以之前预克隆的实体作废，需要重新克隆
        // 因为 preClone 期间若字段为 Level, 并且 Engine.Scene 也确实是 level, 则会直接把字段设为 level
        if (self is Level) {
            State = State.None;
            PreCloneSavedEntities();
        }
    }

    private static void AutoLoadStateWhenDeath(On.Celeste.PlayerDeadBody.orig_End orig, PlayerDeadBody self) {
        if (ModSettings.Enabled
            && ModSettings.AutoLoadStateAfterDeath
            && Instance.IsSaved
            && !TasUtils.Running
            && !Instance.SavedByTas
            && !self.finished
            && Engine.Scene is Level level
            && level.Entities.FindFirst<PlayerSeeker>() == null
           ) {
            level.OnEndOfFrame += () => {
                if (Instance.IsSaved) {
                    Instance.LoadStateImpl(false, out _);
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
    internal bool SaveStateImpl(bool tas, out string popup) {
        if (Engine.Scene is not Level level) {
            popup = "Cannot Save: Scene is not Level!";
            return false;
        }

        if (!IsAllowSave(level, tas, out string reason)) {
            popup = reason;
            return false;
        }

        // 不允许在春游图打开章节面板时存档
        if (InGameOverworldHelperIsOpen.Value?.GetValue(null) as bool? == true) {
            popup = "Cannot Save while ChapterPanel is Open!";
            return false;
        }

        bool shouldDesyncCheck = DesyncRiskAnalyzer.GetAndRefreshIfDesyncCheck();
        if (shouldDesyncCheck && DesyncRiskAnalyzer.OnBeforeSaveState(level, out string reason2)) {
            popup = reason2;
            return false;
        }

        popup = "";
        SaveLoadProfiler.Begin("save");
        try {
            if (IsSaved) {
                ClearBeforeSave = true;
                ClearStateImpl(hasGc: false);
                ClearBeforeSave = false;
            }
#if DEBUG
            DebugTool.MemoryTracker.CheckFragmentation("Save Start");
            DebugTool.MemoryTracker.GcSample before = DebugTool.MemoryTracker.TakeGcSample();
            Stopwatch sw = Stopwatch.StartNew();
#endif

            SaveLoadAction.InitSlots();
            SaveLoadProfiler.Mark("initSlots");
            State = State.Saving;
            SavedByTas = tas;
            SaveLoadAction.OnBeforeSaveState(level);
            SaveLoadProfiler.Mark("beforeSave");
            level.DeepCloneToShared(savedLevel = (Level)RuntimeHelpers.GetUninitializedObject(typeof(Level)));
            SaveLoadProfiler.Mark("cloneLevel");
            savedSaveData = SaveData.Instance.DeepCloneShared();
            SaveLoadProfiler.Mark("cloneSaveData");
            savedTasCycleGroupCounter = TasUtils.GroupCounter;
            savedTransitionRoutine = transitionRoutine?.TryGetTarget(out IEnumerator enumerator) == true ? enumerator.DeepCloneShared() : null;
            SaveLoadAction.OnSaveState(level);
            SaveLoadProfiler.Mark("saveActions");
            DeepClonerUtils.ClearSharedDeepCloneState();

            if (shouldDesyncCheck && DesyncRiskAnalyzer.OnAfterSaveState(out string reason3)) {
                popup = reason3;
                ClearStateImpl(hasGc: false);
                return false;
            }

            PreCloneSavedEntities();
            SaveLoadProfiler.Mark("precloneStart");
            SaveLoadProfiler.End();

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

            popup = $"Save to [{SlotName}]";
            SlotDescription = GenerateSlotDescription();
            Logger.Info("SpeedrunTool/SaveState", $"Save to {FullSlotDescription}");

#if DEBUG
            if (InGame_Profiling) {
                Logger.Debug("SpeedrunTool", $"{"Save",20}: {sw.ElapsedMilliseconds,7} ms");
                Logger.Debug("SpeedrunTool", $"{"Allocated",20}: {DebugTool.MemoryTracker.FormatDiff(before, DebugTool.MemoryTracker.TakeGcSample(), simplified: false)}");
                Logger.Debug("SpeedrunTool", $"{"ProcessMemoryUsage",20}: {Process.GetCurrentProcess().PrivateMemorySize64 / 1024.0 / 1024.0 / 1024.0,7:F2} GB");
            }
            DebugTool.MemoryTracker.CheckFragmentation("Save End");
            if (Log_WhenSaving) {
                SaveLoadAction.LogSavedValues(level: savedLevel);
            }
#endif

            return true;
        }
        catch (Exception exception) {
            // Never let an exception escape with State = Saving: GameFreezeManager skips
            // level.Update for every non-None state and all save/load entry points are blocked
            // by CannotSaveLoad(), which used to leave the session frozen / crash the game.
            popup = "Failed to Save: please check the log!";
            Logger.Warn("SpeedrunTool/SaveState", $"Failed to save state: {exception.GetType().Name}: {exception.Message}");
            Logger.LogDetailed(exception, "SpeedrunTool");
            RecoverFromFailedSave();
            return false;
        }
    }

    /// <summary>
    /// A save that threw mid-way leaves a partially built template and the state machine in
    /// <see cref="State.Saving"/>. Release everything and go back to a clean idle state.
    /// </summary>
    private void RecoverFromFailedSave() {
        Logger.Warn("SpeedrunTool/SaveState", "Recovering from a failed save...");
        State = State.None;
        freezeType = FreezeType.None;
        Engine.FreezeTimer = 0f;
        DeepClonerUtils.ClearSharedDeepCloneState();
        try {
            ClearStateImpl(hasGc: false);
        }
        catch (Exception exception) {
            Logger.LogDetailed(exception, "SpeedrunTool");
        }
    }

    internal bool LoadStateImpl(bool tas, out string popup) {
        popup = "";
        if (Engine.Scene is not Level level) {
            popup = "Cannot Load: Scene is not Level!";
            return false;
        }

        if (!IsAllowLoad(level, tas, out string reason)) {
            popup = reason;
            return false;
        }

        if (tas && !SavedByTas) {
            popup = "Cannot Load a state saved by TAS!";
            return false;
        }

        // Once UnloadLevel() started, the live level is half destroyed and needs a reload
        // before the player can continue (see the catch block below).
        bool levelWasUnloaded = false;
        SaveLoadProfiler.Begin("load");
        try {
#if DEBUG
            if (Log_WhenLoading) {
                SaveLoadAction.LogSavedValues(level: savedLevel);
            }
            DebugTool.MemoryTracker.CheckFragmentation("Load Start");
            DebugTool.MemoryTracker.GcSample before = DebugTool.MemoryTracker.TakeGcSample();
            Stopwatch sw = Stopwatch.StartNew();
#endif

            LoadByTas = tas;
            State = State.Loading;

            SaveLoadAction.OnBeforeLoadState(level);
            SaveLoadProfiler.Mark("beforeLoad");

            DeepClonerUtils.SetSharedDeepCloneState(preCloneTask?.Result);
            SaveLoadProfiler.Mark("waitPreclone");

            UpdateTimeAndDeaths(level);
            SaveLoadProfiler.Mark("mergeTime");
            levelWasUnloaded = true;
            UnloadLevel(level);
            SaveLoadProfiler.Mark("unload");

            Tracker.Refresh(savedLevel); // 不能在 OnLoadState 里做. 否则多次读档会需要反复 Refresh Tracker
            savedLevel.DeepCloneToShared(level);
            SaveLoadProfiler.Mark("cloneBack");
            SaveData.Instance = savedSaveData.DeepCloneShared();
            SaveLoadProfiler.Mark("restoreSaveData");
            if (savedTransitionRoutine != null) {
                transitionRoutine = new WeakReference<IEnumerator>(savedTransitionRoutine.DeepCloneShared());
            }

            RestoreAudio_1(level);
            SaveLoadProfiler.Mark("restoreAudio");
            RestoreCassetteBlockManager1(level);
            SaveLoadAction.OnLoadState(level);
            SaveLoadProfiler.Mark("loadActions");

            PreCloneSavedEntities();
            SaveLoadProfiler.Mark("precloneStart");

            if (tas) {
                LoadStateComplete(level);
            }
            else {
                // restore cycle hitbox color
                RestoreLevelTime(level);
                FreezeGame(FreezeType.Load);
                DoScreenWipe(level);
            }

            if (!tas && ModSettings.GcAfterLoadState) {
                GcCollect(force: false);
            }

            SaveLoadProfiler.End();

            popup = $"Load from [{SlotName}]";
            Logger.Info("SpeedrunTool/LoadState", $"Load from {FullSlotDescription}");

#if DEBUG
            if (InGame_Profiling) {
                Logger.Debug("SpeedrunTool", $"{"Load",20}: {sw.ElapsedMilliseconds,7} ms");
                Logger.Debug("SpeedrunTool", $"{"Allocated",20}: {DebugTool.MemoryTracker.FormatDiff(before, DebugTool.MemoryTracker.TakeGcSample(), simplified: false)}");
                Logger.Debug("SpeedrunTool", $"{"ProcessMemoryUsage",20}: {Process.GetCurrentProcess().PrivateMemorySize64 / 1024.0 / 1024.0 / 1024.0,7:F2} GB");
            }
            DebugTool.MemoryTracker.CheckFragmentation("Load End");
#endif

            return true;
        }
        catch (Exception exception) {
            popup = "Failed to Load: please check the log!";
            Logger.Warn("SpeedrunTool/LoadState", $"Failed to load state: {exception.GetType().Name}: {exception.Message}");
            Logger.LogDetailed(exception, "SpeedrunTool");
            RecoverFromFailedLoad(level, levelWasUnloaded);
            return false;
        }
    }

    /// <summary>
    /// A load that threw mid-way used to leave State = Loading forever (level.Update skipped,
    /// all hotkeys reporting "busy"). If the entities were already removed from the live level,
    /// schedule a level reload so the session becomes playable again; the saved state itself is
    /// kept, the pre-clone result is dropped so the next load falls back to a synchronous clone.
    /// </summary>
    private void RecoverFromFailedLoad(Level level, bool levelWasUnloaded) {
        Logger.Warn("SpeedrunTool/LoadState", "Recovering from a failed load...");
        State = State.None;
        freezeType = FreezeType.None;
        Engine.FreezeTimer = 0f;
        preCloneTask = null;
        DeepClonerUtils.ClearSharedDeepCloneState();

        if (levelWasUnloaded && Engine.Scene == level) {
            level.OnEndOfFrame += () => {
                if (Engine.Scene == level) {
                    level.Reload();
                }
            };
        }
    }

    internal bool ClearStateImpl(bool hasGc = true) {
        preCloneTask?.Wait();

        // fix: 读档冻结时被TAS清除状态后无法解除冻结
        if (State == State.Waiting && Engine.Scene is Level level) {
            OutOfFreeze(level);
        }

        playingEventInstances.Clear();
        bool doSomething = savedLevel is not null;
        if (!doSomething) {
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
        MoreSaveSlotsUI.Snapshot.RemoveSnapshot(SlotName);
        // 2025.10.08 fix: clear 之后读档更加卡顿 (这个问题在老版本好像也有, 之前在这里压根不 Gc)
        // 2025.10.19: 不过似乎不是每个人都喜欢卡顿一下, 姑且先做成可选项, 使得用户可以保留之前的体验
        if (hasGc && ModSettings.GcAfterClearState) {
            GcCollect(force: true);
        }
        if (doSomething) {
            Logger.Info("SpeedrunTool/ClearState", $"Clear {FullSlotDescription}");
        }
        SlotDescription = "";
        return doSomething;
    }

    internal static float MemoryThreshold = 2.5f; // GB

    /// <summary>
    /// When enabled, a background task pre-computes the clone graph needed by the next load
    /// (entities, renderers and save data). This makes loading noticeably faster, at the cost
    /// of keeping a second copy of the saved object graph alive while a state is saved.
    /// Disable it on memory-constrained machines to roughly halve the retained savestate memory.
    /// </summary>
    public static bool PreCloneEnabled { get; set; } = true;

    internal void GcCollect(bool force = false) {
        // 使用内存很难低于这个值, 干脆此时强制执行. ModSettings 里也同样这般设置
        force = force || MemoryThreshold < 1f;
        if (force) {
            Logger.Log("SpeedrunTool", "Force GC Collecting...");
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
                Logger.Info("SpeedrunTool", $"ProcessMemoryUsage: {memorySize:0.00} GB > Threshold: {MemoryThreshold:0.00} GB. Waiting for GC Collecting...");
                GcCollectCore();
            }
        }

        static void GcCollectCore() {
            // 以现在卡顿一些为代价, 保证之后游戏流程中尽量不卡顿 (后者更致命)
            // 作为推论, 我们不应该放在其他线程执行此事
#if DEBUG
            DebugTool.MemoryTracker.CheckFragmentation("GC Start");
#endif
            Stopwatch sw = Stopwatch.StartNew();
            GCSettings.LargeObjectHeapCompactionMode = GCLargeObjectHeapCompactionMode.CompactOnce;
            GC.Collect();
            GC.WaitForPendingFinalizers();
            // 这里一般也没太多 IDisposable, 所以我们不再跑一次 GC.Collect()
            Logger.Info("SpeedrunTool", $"GC latency: {sw.ElapsedMilliseconds}ms.");
#if DEBUG
            DebugTool.MemoryTracker.CheckFragmentation("GC End");
#endif
        }
    }

    // 触发 Entity.Removed, 从而释放资源，停止正在播放的声音, 解除 hook 等
    private static readonly List<Entity> unloadEntities = [];
    private static readonly HashSet<Entity> unloadEntitiesSeen = [];

    private static void UnloadLevel(Level level) {
        unloadEntities.Clear();
        unloadEntitiesSeen.Clear();

        // Player 必须最早移除，不然在处理 player.triggersInside 字段时会产生空指针异常
        unloadEntities.AddRange(level.Tracker.GetEntities<Player>());
        unloadEntities.AddRange(level.Entities);

        // 等效于 Level.UnloadEntities 然后 UpdateLists
        // (the list is a copy, and "seen" keeps the Player entry from being processed twice;
        //  the previous LINQ Distinct() allocated a HashSet and an iterator on every load)

        foreach (Entity entity in unloadEntities) {
            if (!unloadEntitiesSeen.Add(entity)) {
                continue;
            }
            // TODO: 确定到底哪些 Global entity 可以移除
            // 这里知道的是 MirrorSurfaces 被移除会导致一些 VirtualRenderTarget Dispose
            // 于是如果在存档 1 存档, 在存档 2 清除, 再读档 1, 会崩溃
            if (entity.TagCheck(Tags.Global) && !GlobalEntitiesToRemove.Contains(entity.GetType())) {
                continue;
            }
            try {
                entity.Removed(level); // 触发各式各样的副作用
                // Scene.TagLists / Scene.Tracker / Engine.Pooler 本身都将被克隆出来, 大部分情况下无需处理. 同理也不用 UpdateLists
                // 但是小部分情况下, Removed 副作用会依赖于 Tracker 等的内部状态 (例如实体带 hook, 只有移除到最后一个才解除 hook)
                // 所以我们还是做整套流程
                level.TagLists.EntityRemoved(entity);
                level.Tracker.EntityRemoved(entity);
                Engine.Pooler.EntityRemoved(entity);
            }
            catch (Exception exception) {
                // A broken entity (third-party mod code) must not abort the whole load and leave
                // the state machine frozen; log it and continue with the remaining entities.
                Logger.Warn("SpeedrunTool/LoadState", $"Failed to remove {entity.GetType().FullName}: {exception.GetType().Name}: {exception.Message}");
            }
        }

        // 移除剩下声音组件
        level.Tracker.GetComponentsCopy<SoundSource>().ForEach(component => component.RemoveSelf());
    }

    private static readonly HashSet<Type> GlobalEntitiesToRemove = [typeof(CassetteBlockManager)];

    [Initialize]
    private static void InitGlobalEntitiesToRemove() {
        if (ModUtils.GetType("SpirialisHelper", "Celeste.Mod.Spirialis.TimeController") is { } timeControllerType) {
            GlobalEntitiesToRemove.Add(timeControllerType);
        }
    }

    private void UpdateTimeAndDeaths(Level level) {
        if (SavedByTas || ModSettings.SaveTimeAndDeaths) {
            return;
        }

        Session session = level.Session;
        Session savedSession = savedLevel.Session;
        AreaKey areaKey = session.Area;

        // The template is updated in place. This used to first DeepCloneShared() the Session and
        // the SaveData, but the clones were never installed anywhere - they were pure garbage on
        // every load (a SaveData clone is not free: it clones the whole Areas_Safe list).
        savedSession.Time = Math.Max(session.Time, savedSession.Time);
        savedSaveData.Time = SaveData.Instance.Time;

        // A mod map may not have an AreaStats entry yet (or a shorter Modes array): the old
        // unchecked indexing threw ArgumentOutOfRangeException and aborted the whole load.
        if (areaKey.ID >= 0
            && areaKey.ID < savedSaveData.Areas_Safe.Count
            && areaKey.ID < SaveData.Instance.Areas_Safe.Count
            && (int)areaKey.Mode < savedSaveData.Areas_Safe[areaKey.ID].Modes.Length
            && (int)areaKey.Mode < SaveData.Instance.Areas_Safe[areaKey.ID].Modes.Length) {
            savedSaveData.Areas_Safe[areaKey.ID].Modes[(int)areaKey.Mode].TimePlayed =
                SaveData.Instance.Areas_Safe[areaKey.ID].Modes[(int)areaKey.Mode].TimePlayed;
        }

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
        savedSession.Deaths = Math.Max(session.Deaths + increaseDeath, savedSession.Deaths);
        savedSession.DeathsInCurrentLevel =
            Math.Max(session.DeathsInCurrentLevel + increaseDeath, savedSession.DeathsInCurrentLevel);
        savedSaveData.TotalDeaths = SaveData.Instance.TotalDeaths + increaseDeath;
        if (areaKey.ID >= 0
            && areaKey.ID < savedSaveData.Areas_Safe.Count
            && areaKey.ID < SaveData.Instance.Areas_Safe.Count
            && (int)areaKey.Mode < savedSaveData.Areas_Safe[areaKey.ID].Modes.Length
            && (int)areaKey.Mode < SaveData.Instance.Areas_Safe[areaKey.ID].Modes.Length) {
            savedSaveData.Areas_Safe[areaKey.ID].Modes[(int)areaKey.Mode].Deaths =
                SaveData.Instance.Areas_Safe[areaKey.ID].Modes[(int)areaKey.Mode].Deaths + increaseDeath;
        }
    }

    private void LoadStateComplete(Level level) {
        RestoreLevelTime(level);
        RestoreAudio_2();
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
    private void RestoreAudio_1(Level level) {
        playingEventInstances.Clear();

        // Iterate the component lists directly: the LINQ version allocated a Component[]
        // (and a "SelectMany" iterator) per entity, i.e. one array per entity per load.
        foreach (Entity entity in level.Entities) {
            foreach (Component component in entity.Components) {
                if (component is SoundSource { Playing: true, instance: { } eventInstance }) {
                    playingEventInstances.Add(eventInstance);
                }
            }
        }
    }

    // 等 ScreenWipe 完毕再开始播放
    private void RestoreAudio_2() {
        foreach (EventInstance instance in playingEventInstances) {
            instance.start();
        }

        playingEventInstances.Clear();
    }

    // 分两步的原因是更早的停止音乐，听起来更舒服更好一点
    // 第一步：停止播放主音乐
    private static void RestoreCassetteBlockManager1(Level level) {
        if (level.Tracker.GetEntity<CassetteBlockManager>() is { } manager) {
            manager.snapshot?.start();
        }
    }

    // 第二步：播放节奏音乐
    // https://discord.com/channels/403698615446536203/429775260108324865/1422017531098566776
    // 理论上, 在 Cassette cycle 图中 LoadState 后暂停会引入额外的读图优势, 但这显然不是我们的问题.
    private static void RestoreCassetteBlockManager2(Level level) {
        if (level.Tracker.GetEntity<CassetteBlockManager>() is { } manager) {
            if (manager.sfx is { } sfx && !manager.isLevelMusic && manager.leadBeats <= 0) {
                sfx.start();
            }
        }
    }

    private void PreCloneSavedEntities() {
        if (!PreCloneEnabled) {
            preCloneTask = null;
            return;
        }

        if (IsSaved) {
            SaveLoadAction.OnPreCloneEntities();
            preCloneTask = Task.Run(() => {
                bool profile = SaveLoadProfiler.Enabled;
                long startTicks = profile ? Stopwatch.GetTimestamp() : 0L;
                long startAllocated = profile ? GC.GetAllocatedBytesForCurrentThread() : 0L;

                DeepCloneState deepCloneState = new();
                savedLevel.Entities.DeepClone(deepCloneState);
                savedLevel.RendererList.DeepClone(deepCloneState);
                savedSaveData.DeepClone(deepCloneState);

                if (profile) {
                    SaveLoadProfiler.LogBackgroundSection("preclone", startTicks, startAllocated);
                }

                return deepCloneState;
            });
        }
    }

    private bool IsAllowSave(Level level, bool tas, out string failReason) {
        // 正常游玩时禁止死亡或者跳过过场时存档，TAS 则无以上限制
        // 跳过过场时的黑屏与读档后加的黑屏冲突，会导致一直卡在跳过过场的过程中
        failReason = "";
        if (State == State.Saving || State == State.Loading) {
            failReason = "Failed to Save: SpeedrunTool is Busy!";
            return false;
        }
        if (State == State.Waiting && !AllowSaveLoadWhenWaiting) {
            failReason = $"Just Saved/Loaded!";
            return false;
        }
        if (tas) {
            return true;
        }
        if (level.Paused) {
            failReason = "Cannot Save while Level is Paused!";
            return false;
        }
        if (!ModSettings.AllowStateDuringDeath && level.IsPlayerDead()) {
            failReason = "Cannot Save while Player is Dead!";
            return false;
        }
        if (level.SkippingCutscene) {
            failReason = "Cannot Save while Skipping Cutscene!";
            return false;
        }
        return true;
    }

    private bool IsAllowLoad(Level level, bool tas, out string failReason) {
        failReason = "";
        if (!IsSaved) {
            failReason = DialogIds.NotSavedStateTooltip.DialogClean() + $" [{SlotName}]";
            return false;
        }
        // A mod that registered/unregistered SaveLoadActions after this state was saved changes
        // the positional ids of the value dictionaries, so replaying the stored values would
        // write them into the wrong actions (and used to NRE on the null dictionaries).
        if (!SaveLoadAction.AreValueDictionariesComplete()) {
            failReason = "Cannot Load: a mod changed save/load handlers after this state was saved. Please save the state again.";
            return false;
        }
        if (State == State.Loading || State == State.Saving) {
            failReason = "Failed to Load: SpeedrunTool is Busy!";
            return false;
        }
        if (State == State.Waiting && !AllowSaveLoadWhenWaiting) {
            failReason = "Just Saved/Loaded!";
            return false;
        }
        if (!tas && level.Paused) {
            failReason = "Cannot Save while Level is Paused!";
        }
        return true;
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

    internal void OutOfFreeze(Level level) {
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
