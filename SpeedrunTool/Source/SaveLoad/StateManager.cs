using Celeste.Mod.SpeedrunTool.ModInterop;
using Celeste.Mod.SpeedrunTool.SaveLoad.Profiling;
using Celeste.Mod.SpeedrunTool.Utils;
using Force.DeepCloner;
using Force.DeepCloner.Helpers;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using EventInstance = FMOD.Studio.EventInstance;

namespace Celeste.Mod.SpeedrunTool.SaveLoad;

public sealed partial class StateManager {
    public static StateManager Instance => SaveSlotsManager.StateManagerInstance;
    internal StateManager() { }

    internal static bool AllowSaveLoadWhenWaiting = false;

    public bool IsSaved => savedLevel != null;

    // True only during the one-time scene-load warm-up. Save callbacks that paint
    // user-visible state (timer/berry recoloring) must not run then.
    internal bool IsWarmingUp { get; private set; }
    public State State { get; private set; } = State.None;
    public bool SavedByTas { get; private set; }
    public bool LoadByTas { get; private set; }
    public bool ClearBeforeSave { get; private set; }
    public Level SavedLevel => savedLevel;
    private Level savedLevel;
    private SaveData savedSaveData;
    // Set while a cross-scene load is waiting for the saved chapter's LevelLoader/Level.Begin.
    // See StateManager.PendingLoad.cs.
    private PendingLoad pendingLoad;
    internal bool HasPendingLoad => pendingLoad is not null;
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
        // Cross-scene (pending) loads: the restore has to run once the saved chapter's Level really
        // exists. See StateManager.PendingLoad.cs for the whole flow.
        On.Celeste.Level.Begin += OnLevelBeginFinishPendingLoad;
        On.Celeste.Level.LoadLevel += OnLevelLoadLevelRestoreModuleStateEarly;
        On.Monocle.Scene.BeforeUpdate += WatchPendingLoad;
    }

    internal static void Unload() {
        On.Celeste.PlayerDeadBody.End -= AutoLoadStateWhenDeath;
        IL.Celeste.Level.TransitionRoutine -= IL_LevelTransitionRoutine;
        On.Celeste.Level.TransitionRoutine -= On_LevelTransitionRoutine;
        On.Celeste.Level.End -= LevelOnEnd;
        On.Celeste.Level.Begin -= OnLevelBeginFinishPendingLoad;
        On.Celeste.Level.LoadLevel -= OnLevelLoadLevelRestoreModuleStateEarly;
        On.Monocle.Scene.BeforeUpdate -= WatchPendingLoad;
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
        using OperationProgress progress = BusyIndicator.Begin("SAVE");
        SaveLoadProfiler.Begin("save");
        try {
            // Rebuild/validate the value dictionaries before touching a previously saved state:
            // clearing it uses positional action ids, which may have changed if a mod registered
            // or removed actions since the state was saved.
            SaveLoadAction.InitSlots();
            SaveLoadProfiler.Mark("initSlots");
            progress?.Report("CHECK_STATE", SlotName);

            if (IsSaved) {
                ClearBeforeSave = true;
                ClearStateImpl(hasGc: false);
                ClearBeforeSave = false;
            }
            SaveLoadProfiler.Mark("clearOldState");

            Debug_SaveStart();

            State = State.Saving;
            SavedByTas = tas;
            SaveLoadAction.OnBeforeSaveState(level);
            SaveLoadProfiler.Mark("beforeSave");

            level.DeepCloneToShared(savedLevel = (Level)RuntimeHelpers.GetUninitializedObject(typeof(Level)));
            SaveLoadProfiler.Mark("cloneLevel");
            progress?.Report("CLONE_LEVEL", SlotName);
            savedSaveData = SaveData.Instance.DeepCloneShared();
            SaveLoadProfiler.Mark("cloneSaveData");
            progress?.Report("CLONE_SAVE_DATA", SlotName);
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

            Debug_SaveEnd();

            return true;
        }
        catch (Exception exception) {
            // Never let an exception escape with State = Saving: GameFreezeManager skips
            // level.Update for every non-None state and all save/load entry points are blocked
            // by the busy/allow checks, which used to leave the session frozen / crash the game.
            popup = "Failed to Save: please check the log!";
            Logger.Warn("SpeedrunTool/SaveState", $"Failed to save state: {exception.GetType().Name}: {exception.Message}");
            Logger.LogDetailed(exception, "SpeedrunTool");
            RecoverFromFailedSave();
            return false;
        }
    }

    /// <summary>
    /// Warm up the expensive save path once, during the first level load.
    /// This must never enter the user-facing save lifecycle: no freeze, no wipe, no
    /// WaitingEntity, no desync check, no pre-clone task and no snapshot. Those side
    /// effects used to survive ClearStateImpl and left the game waiting for input.
    /// </summary>
    internal void WarmUpSaveState(Level level) {
        if (!ModSettings.WarmUpFirstSave || State != State.None || IsSaved) {
            return;
        }

        IsWarmingUp = true;
        System.Diagnostics.Stopwatch sw = System.Diagnostics.Stopwatch.StartNew();
        Level temporaryLevel = null;
        SaveData temporarySaveData = null;
        try {
            SaveLoadAction.InitSlots();

            // Match the real save's clone semantics for one-time JIT/codegen cost,
            // but keep the temporary objects local to this method.
            State = State.Saving;
            SaveLoadAction.OnBeforeSaveState(level);
            temporaryLevel = (Level)RuntimeHelpers.GetUninitializedObject(typeof(Level));
            level.DeepCloneToShared(temporaryLevel);
            temporarySaveData = SaveData.Instance.DeepCloneShared();
            SaveLoadAction.OnSaveState(level);
        }
        catch (Exception exception) {
            Logger.Warn("SpeedrunTool/SaveLoadAction",
                $"First-save warm-up failed: {exception.GetType().Name}: {exception.Message}");
            Logger.LogDetailed(exception, "SpeedrunTool");
        }
        finally {
            temporaryLevel = null;
            temporarySaveData = null;
            try {
                DeepClonerUtils.ClearSharedDeepCloneState();
                SaveLoadAction.OnClearState(false);
            }
            catch (Exception exception) {
                Logger.Warn("SpeedrunTool/SaveLoadAction",
                    $"First-save warm-up cleanup failed: {exception.GetType().Name}: {exception.Message}");
            }

            IsWarmingUp = false;
            State = State.None;
            freezeType = FreezeType.None;
            ClearBeforeSave = false;
            ResetOperationFlags();

            Logger.Log("SpeedrunTool/SaveLoadAction",
                $"Warmed the first-save path during level load: {sw.ElapsedMilliseconds} ms (temporary clone only)");
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
        // These flags are consumed by the next save/clear; a failed save must not leak them
        // (a stale ClearBeforeSave used to make the following clear-state call index stale ids).
        ClearBeforeSave = false;
        ResetOperationFlags();
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
        Level level = Engine.Scene as Level;

        if (!IsAllowLoad(level, tas, out string reason)) {
            popup = reason;
            return false;
        }

        if (tas && !SavedByTas) {
            popup = "Cannot Load a state saved by TAS!";
            return false;
        }

        // Same chapter: the current Level already owns the map data, tilesets and area resources of
        // the saved state, so the historical in-place restore is both safe and fastest.
        if (LoadStateRouteDecider.Decide(level is not null, IsSameTargetArea(level?.Session, savedLevel?.Session)) == LoadStateRoute.Direct) {
            return LoadStateCore(level, tas, mergeTimeAndDeaths: true, out popup);
        }

        // Another chapter (or the chapter select / another scene): the target area's map bin,
        // tilesets, audio and area metadata only exist after the LevelLoader pipeline ran, so the
        // saved chapter has to be entered first. The restore then runs from its Level.Begin hook.
        if (!IsSceneAllowedForPendingLoad(Engine.Scene)) {
            popup = "Cannot Load: Scene is not Level!";
            return false;
        }

        if (tas) {
            // TAS loading must be synchronous (the state is applied before the caller's next frame),
            // which a chapter transition cannot guarantee. Refuse and keep the state for a manual load.
            popup = "Cannot Load: TAS savestates cannot be loaded across chapters!";
            Logger.Warn("SpeedrunTool/LoadState", popup);
            return false;
        }

        return BeginPendingLoad(out popup);
    }

    /// <summary>
    /// The restore half of a load. Assumes <paramref name="level"/> is the Level the state was saved
    /// in: either the current Level of the same chapter, or the freshly loaded Level of the saved
    /// chapter after a pending (cross-scene) load. <paramref name="mergeTimeAndDeaths"/> is false for
    /// the pending path because it merged the live time/deaths before leaving the old scene.
    /// </summary>
    private bool LoadStateCore(Level level, bool tas, bool mergeTimeAndDeaths, out string popup) {
        popup = "";
        // Once UnloadLevel() started, the live level is half destroyed and needs a reload
        // before the player can continue (see the catch block below).
        bool levelWasUnloaded = false;
        using OperationProgress progress = BusyIndicator.Begin("LOAD");
        SaveLoadProfiler.Begin("load");
        try {
            Debug_LoadStart();

            LoadByTas = tas;
            State = State.Loading;

            SaveLoadAction.OnBeforeLoadState(level);
            SaveLoadProfiler.Mark("beforeLoad");


            BusyIndicator.Wait(preCloneTask);
            DeepClonerUtils.SetSharedDeepCloneState(GetPreCloneResult());
            SaveLoadProfiler.Mark("waitPreclone");


            if (mergeTimeAndDeaths) {
                UpdateTimeAndDeaths(level);
            }
            SaveLoadProfiler.Mark("mergeTime");

            levelWasUnloaded = true;
            UnloadLevel(level);
            SaveLoadProfiler.Mark("unload");
            progress?.Report("UNLOAD_LEVEL", SlotName);

            Tracker.Refresh(savedLevel); // 不能在 OnLoadState 里做. 否则多次读档会需要反复 Refresh Tracker
            savedLevel.DeepCloneToShared(level);
            progress?.Report("RESET_GRAPHICS", SlotName);
            GraphicResourcesHandler.RepairDisposedGraphicsResources(level);
            SaveLoadProfiler.Mark("cloneBack");
            progress?.Report("CLONE_LEVEL", SlotName);
            SaveData.Instance = savedSaveData.DeepCloneShared();
            SaveLoadProfiler.Mark("restoreSaveData");
            progress?.Report("CLONE_SAVE_DATA", SlotName);
            if (savedTransitionRoutine != null) {
                transitionRoutine = new WeakReference<IEnumerator>(savedTransitionRoutine.DeepCloneShared());
            }

            RestoreAudio_1(level);
            SaveLoadProfiler.Mark("restoreAudio");
            progress?.Report("RESTORE_AUDIO", SlotName);
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

            Debug_LoadEnd();

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
        // Wait for a still-running pre-clone before dropping the reference: otherwise the old task and
        // a new one could clone the same savedLevel concurrently. The failed load's clone state is
        // unusable either way, so the reference goes too.
        WaitForPreClone();
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

    /// <summary>
    /// The TAS markers describe the state that is currently held. Every path that releases the state
    /// (clear, failed save, warm-up) has to drop them with it, or the next save/load keeps treating
    /// an empty slot as a TAS state.
    /// </summary>
    private void ResetOperationFlags() {
        SavedByTas = false;
        LoadByTas = false;
    }

    internal bool ClearStateImpl(bool hasGc = true) {
        // Clearing while a cross-scene load is entering the saved chapter would leave that half
        // finished transition behind (and ClearStateImpl is about to null savedLevel). Give up on
        // the pending load first and restore the live SaveData/module references it replaced.
        if (pendingLoad is not null) {
            AbortPendingLoadForClear();
        }

        using OperationProgress progress = BusyIndicator.Begin("CLEAR");
        BusyIndicator.Wait(preCloneTask);

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
        ResetOperationFlags();
        MoreSaveSlotsUI.Snapshot.RemoveSnapshot(SlotName);
        // 2025.10.08 fix: clear 之后读档更加卡顿 (这个问题在老版本好像也有, 之前在这里压根不 Gc)
        // 2025.10.19: 不过似乎不是每个人都喜欢卡顿一下, 姑且先做成可选项, 使得用户可以保留之前的体验
        if (hasGc && ModSettings.GcAfterClearState) {
            progress?.Refresh();
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
    private static bool preCloneEnabled = true;

    public static bool PreCloneEnabled {
        get => preCloneEnabled;
        set {
            if (preCloneEnabled == value) {
                return;
            }
            preCloneEnabled = value;
            if (!value) {
                // Turning it off means "stop keeping a second copy of the object graph": wait for the
                // task and drop the reference. Only the current slot is handled here -- the other
                // slots are synchronized by their own entry points (WaitForRunningPreClones / the
                // load path), and waiting for all of them could stall the settings menu.
                if (SaveSlotsManager.Slot?.StateManager is { } manager) {
                    manager.WaitForPreClone();
                    manager.preCloneTask = null;
                }
            }
        }
    }

    /// <summary>
    /// Waits for a running pre-clone without consuming its result: the load path reads it, and the
    /// entry points that only need exclusive access (Scene.Begin, switching slots) must not drop it.
    /// Unbounded on purpose: every caller runs on the main thread at a point where the player is
    /// already waiting (a scene load, a state load, the settings menu), and cloning concurrently with
    /// a still-running pre-clone is never safe.
    /// </summary>
    internal void WaitForPreClone() {
        try {
            preCloneTask?.Wait();
        }
        catch (Exception exception) {
            // A third-party clone processor threw. The state it was building is unusable, and
            // GetPreCloneResult() reports null for it, so the next load falls back to a synchronous clone.
            Logger.LogDetailed(exception, "SpeedrunTool");
        }
    }

    /// <summary>
    /// The clone state a restore may reuse, or null when there is none: pre-clone disabled or already
    /// dropped, or the clone processor threw. Blocks until a still-running task finishes, which is why
    /// this is a method and not a property.
    /// </summary>
    private DeepCloneState GetPreCloneResult() {
        WaitForPreClone();
        return preCloneTask is { IsCompletedSuccessfully: true } ? preCloneTask.Result : null;
    }

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
            Debug_CheckFragmentation("GC Start");
            Stopwatch sw = Stopwatch.StartNew();

            // LOH compaction is by far the most expensive part of this collect (it can move
            // hundreds of MB), and it is only worth paying when the large object heap is
            // actually fragmented. Compacting on every clear used to be a guaranteed
            // multi-hundred-ms stall on a low-memory machine, exactly when the player hits
            // the clear-state hotkey or changes chapter.
            GCMemoryInfo memoryInfo = GC.GetGCMemoryInfo();
            long fragmentedBytes = memoryInfo.FragmentedBytes;
            long heapBytes = memoryInfo.HeapSizeBytes;
            bool compactLoh = fragmentedBytes > 64L * 1024L * 1024L
                              || (heapBytes > 0 && fragmentedBytes * 100L / heapBytes > 5L);

            if (compactLoh) {
                GCSettings.LargeObjectHeapCompactionMode = GCLargeObjectHeapCompactionMode.CompactOnce;
            }

            GC.Collect(2, GCCollectionMode.Forced, blocking: true);
            GC.WaitForPendingFinalizers();
            // 这里一般也没太多 IDisposable, 所以我们不再跑一次 GC.Collect()
            Logger.Info("SpeedrunTool",
                $"GC latency: {sw.ElapsedMilliseconds}ms. LOH compaction: {(compactLoh ? "yes" : "no")} " +
                $"(fragmented {fragmentedBytes / 1048576.0:F1} MB / heap {heapBytes / 1048576.0:F1} MB).");
            Debug_CheckFragmentation("GC End");
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
                // Global entities survive the manual unload. Some of them own GPU resources that
                // are disposed when their old area unloads (MirrorSurfaces/TrailManager/WaveDash);
                // log only the ones that actually carry such resources so a crash report names the
                // entity instead of only the later SpriteBatch.Draw stack.
                Debug_LogKeptGlobalEntity(entity);
                continue;
            }
            try {
                entity.Removed(level); // 触发各式各样的副作用
                // Scene.TagLists / Scene.Tracker / Engine.Pooler 本身都会被克隆出来, 大部分情况下无需处理.
                // 但小部分情况下 Removed 的副作用依赖这些表的内部状态 (例如实体带 hook, 只有移除到最后一个才解除 hook).
                // 四步共用一个 try: 一旦某一步抛了, 这个实体已经处于不确定状态, 后面的清理按 best effort 放弃.
                level.TagLists.EntityRemoved(entity);
                level.Tracker.EntityRemoved(entity);
                Engine.Pooler.EntityRemoved(entity);
            }
            catch (Exception exception) {
                Logger.Warn("SpeedrunTool/LoadState", $"Failed to remove {entity.GetType().FullName}: {exception.GetType().Name}: {exception.Message}");
            }
        }

        // 移除剩下声音组件（避免每次 load 都分配一个 ForEach delegate）
        List<Component> remainingSounds = level.Tracker.GetComponentsCopy<SoundSource>();
        for (int i = 0; i < remainingSounds.Count; i++) {
            remainingSounds[i].RemoveSelf();
        }
    }

    private static readonly HashSet<Type> GlobalEntitiesToRemove = [typeof(CassetteBlockManager)];

    [Initialize]
    private static void InitGlobalEntitiesToRemove() {
        if (ModUtils.GetType("SpirialisHelper", "Celeste.Mod.Spirialis.TimeController") is { } timeControllerType) {
            GlobalEntitiesToRemove.Add(timeControllerType);
        }
    }

    private void PreCloneSavedEntities() {
        if (!PreCloneEnabled) {
            preCloneTask = null;
            return;
        }

        if (IsSaved) {
            SaveLoadAction.OnPreCloneEntities();

            // Bind the slot on this side of the task: the clone processors write what they recreate into
            // that slot's lists, and reading SaveSlotsManager.Slot once the task is running would file
            // them under whichever slot is current by then.
            SaveSlot slot = SaveSlotsManager.Slot;
            preCloneTask = Task.Run(() => {
                DeepClonerUtils.PreCloneEventInstanceTarget = slot.ClonedEventInstancesWhenPreClone;
                try {
#if SRT_PROFILE
                    bool profile = SaveLoadProfiler.Enabled;
                    long startTicks = profile ? Stopwatch.GetTimestamp() : 0L;
                    long startAllocated = profile ? GC.GetAllocatedBytesForCurrentThread() : 0L;
#endif

                    DeepCloneState deepCloneState = new();
                    savedLevel.Entities.DeepClone(deepCloneState);
                    savedLevel.RendererList.DeepClone(deepCloneState);
                    savedSaveData.DeepClone(deepCloneState);

#if SRT_PROFILE
                    if (profile) {
                        SaveLoadProfiler.LogBackgroundSection("preclone", startTicks, startAllocated);
                    }
#endif

                    return deepCloneState;
                }
                finally {
                    DeepClonerUtils.PreCloneEventInstanceTarget = null;
                }
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
        if (!tas && level is { Paused: true }) {
            failReason = "Cannot Load while Level is Paused!";
            return false;
        }
        return true;
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