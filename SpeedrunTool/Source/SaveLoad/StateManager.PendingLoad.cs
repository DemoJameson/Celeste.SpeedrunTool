using Celeste.Mod.SpeedrunTool.Message;
using Force.DeepCloner;
using System.Collections.Generic;
using System.Diagnostics;

namespace Celeste.Mod.SpeedrunTool.SaveLoad;

/// <summary>
/// Cross-scene ("pending") load: loading a state that was saved in another chapter, or from the
/// chapter select.
///
/// A savestate is a deep clone of a Level, so the restore used to assume the current scene already
/// is a Level of the same chapter: the map bin, tilesets, autotilers, sprite bank, backdrops and
/// audio of the target area are only built by the LevelLoader pipeline, and cloning the template
/// into a Level of another chapter produced a hybrid with the wrong (or missing) resources.
///
/// The pending path therefore:
///   1. merges the live time/deaths into the template (same semantics as UpdateTimeAndDeaths),
///   2. remembers the live SaveData / module session references so a failure can roll back,
///   3. installs the saved SaveData and asks the game to enter the saved chapter through
///      <see cref="LevelEnter.Go"/> (fromSaveData: true, i.e. no postcards/vignettes),
///   4. finishes the normal restore from a Level.Begin hook once that chapter's Level exists.
///
/// A failure or timeout before the target Level appears rolls the replaced references back, keeps
/// the savestate (so the player can retry) and escapes to the chapter select instead of leaving the
/// game stuck in a loading scene.
/// </summary>
public sealed partial class StateManager {
    private const int PendingLoadSceneSwitchGraceFrames = 4;
    private static readonly long PendingLoadTimeoutTicks = (long)(30 * Stopwatch.Frequency);

    // Shown once the abort fallback reached a normal scene (a Tooltip added to a scene that is being
    // replaced would be lost).
    private static string deferredPendingPopup;

    private sealed class PendingLoad {
        internal Scene StartScene;
        internal ulong StartFrame;
        internal long StartTimestamp;
        internal long DeadlineTicks;
        internal Session TargetSession;
        internal SaveData OriginalSaveData;
        internal AreaKey OriginalLastAreaSafe;
        internal Session OriginalCurrentSessionSafe;
        internal List<ModuleStateSnapshot> OriginalModuleStates = [];
        internal bool SeenLevelEnter;
        internal bool EnteredLevelLoader;
    }

    /// <summary>
    /// The per-module state that SaveData.StartSession replaces when a chapter is entered. Kept so an
    /// aborted pending load can put the original objects back.
    /// </summary>
    private readonly struct ModuleStateSnapshot {
        internal readonly EverestModule Module;
        internal readonly EverestModuleSession Session;
        internal readonly EverestModuleSaveData SaveData;

        internal ModuleStateSnapshot(EverestModule module, EverestModuleSession session, EverestModuleSaveData saveData) {
            Module = module;
            Session = session;
            SaveData = saveData;
        }
    }

    #region Decision helpers

    /// <summary>
    /// Whether the live session already belongs to the chapter the state was saved in. Same chapter
    /// means the Level owns the target area's map data / tilesets / audio, so the in-place restore is
    /// safe; otherwise the Level has to be rebuilt by the game first.
    /// </summary>
    private static bool IsSameTargetArea(Session current, Session saved) {
        if (current is null || saved is null) {
            return false;
        }

        AreaKey currentArea = current.Area;
        AreaKey savedArea = saved.Area;
        if (currentArea != savedArea) {
            return false;
        }

        // Mod areas are identified by id+mode. Requiring the SID to agree as well keeps a state saved
        // before a mod list change from being restored into a different map that reused the id.
        string savedSid = savedArea.GetSID();
        return string.IsNullOrEmpty(savedSid) || string.Equals(currentArea.GetSID(), savedSid, StringComparison.Ordinal);
    }

    private static bool IsSceneAllowedForPendingLoad(Scene scene) {
        // The chapter select deliberately cannot start a load: hotkeys only fire in a Level and
        // the pending operation's user-visible semantics are "load this state from the current run".
        return scene is Level or LevelLoader or LevelEnter;
    }

    private static bool IsTargetAreaAvailable(AreaKey area, out string reason) {
        reason = "";
        // AreaData.Get(int) does not bound-check the upper end (it indexes Areas[id]), and a state
        // saved with a mod that has since been disabled can carry an id past the current list.
        if (area.ID < 0 || area.ID >= AreaData.Areas.Count) {
            reason = "the chapter of the savestate is not loaded by any mod!";
            return false;
        }

        AreaData areaData = AreaData.Get(area);
        if (areaData is null) {
            reason = "the chapter of the savestate is not loaded by any mod!";
            return false;
        }

        string savedSid = area.GetSID();
        if (!string.IsNullOrEmpty(savedSid) && !string.Equals(areaData.SID, savedSid, StringComparison.Ordinal)) {
            reason = "the chapter of the savestate changed its id, save the state again!";
            return false;
        }

        if (!areaData.HasMode(area.Mode)) {
            reason = "the mode of the saved chapter is not available, save the state again!";
            return false;
        }

        return true;
    }

    #endregion

    #region Entry point (called by LoadStateImpl)

    private bool BeginPendingLoad(out string popup) {
        popup = "";

        if (pendingLoad is not null) {
            popup = "Failed to Load: SpeedrunTool is Busy!";
            return false;
        }

        if (SaveData.Instance is null) {
            popup = "Cannot Load: no save file is loaded!";
            return false;
        }

        if (savedLevel?.Session is not { } savedSession || savedSaveData is null) {
            popup = "Failed to Load: the saved state is incomplete!";
            return false;
        }

        // The game shows a postcard for a pending LevelEnter.ErrorMessage; entering the chapter would
        // only end up in the chapter select. Ask the player to re-enter the chapter first.
        if (LevelEnter.ErrorMessage is not null) {
            popup = "Cannot Load: the previous chapter failed to load, re-enter the chapter first!";
            Logger.Warn("SpeedrunTool/PendingLoad", popup);
            return false;
        }

        if (!IsTargetAreaAvailable(savedSession.Area, out string areaReason)) {
            popup = "Cannot Load: " + areaReason;
            Logger.Warn("SpeedrunTool/PendingLoad", popup);
            return false;
        }

        // The background pre-clone task reads the template concurrently; never mutate it while it runs.
        WaitForPreCloneTask();
        // ...and start the new clone graph from a clean state (a previous failed operation may have
        // left a mapping in the shared state).
        DeepClonerUtils.ClearSharedDeepCloneState();

        PendingLoad pending = new() {
            StartScene = Engine.Scene,
            StartFrame = Engine.FrameCounter,
            StartTimestamp = Stopwatch.GetTimestamp(),
            DeadlineTicks = Stopwatch.GetTimestamp() + PendingLoadTimeoutTicks,
            OriginalSaveData = SaveData.Instance,
            OriginalLastAreaSafe = SaveData.Instance.LastArea_Safe,
            OriginalCurrentSessionSafe = SaveData.Instance.CurrentSession_Safe,
            OriginalModuleStates = CaptureModuleStates(),
        };
        pendingLoad = pending;

        try {
            // The merge reads the scene that is still active, so it has to happen before we leave it.
            Level currentLevel = Engine.Scene as Level;
            MergeTimeAndDeaths(currentLevel?.Session, SaveData.Instance, currentLevel);

            SaveData.Instance = savedSaveData.DeepCloneShared();
            pending.TargetSession = savedSession.DeepClone();

            State = State.Loading;
            LoadByTas = false;
            Logger.Info("SpeedrunTool/PendingLoad",
                $"Entering '{savedSession.Area.GetSID()} [{savedSession.Level}]' to load the state from [{SlotName}]");

            // Setting Engine.Scene right now can be overwritten later this frame by the current
            // scene's own Update (LevelLoader starting its Level, OverworldLoader entering the
            // overworld, LevelEnter switching to a LevelLoader, ...). The scene switch happens after
            // Scene.AfterUpdate, so defer the request there to make it win regardless of the scene
            // the load was started from.
            Engine.FreezeTimer = 0f;
            if (pending.StartScene is { } startScene) {
                startScene.OnEndOfFrame += () => EnterSavedChapter(pending);
            }
            else {
                EnterSavedChapter(pending);
            }
        }
        catch (Exception exception) {
            Logger.Warn("SpeedrunTool/PendingLoad", $"Failed to start entering the saved chapter: {exception.GetType().Name}: {exception.Message}");
            Logger.LogDetailed(exception, "SpeedrunTool");
            AbortPendingLoad("failed to start entering the saved chapter", out _, showPopup: false, restoreScene: true);
            popup = "Failed to Load: please check the log!";
            return false;
        }

        popup = $"Load from [{SlotName}]";
        return true;
    }

    /// <summary>
    /// Runs at the end of the current scene's update, just before Monocle's deferred scene switch, so
    /// this request cannot be overwritten by the scene we started from (LevelLoader/OverworldLoader/
    /// LevelEnter set Engine.Scene in their own Update).
    /// </summary>
    private void EnterSavedChapter(PendingLoad pending) {
        if (!ReferenceEquals(pendingLoad, pending) || State != State.Loading) {
            return;
        }

        try {
            // fromSaveData: true -> no area postcard / B-Side title / intro vignette, and mod-provided
            // vignettes are normally skipped for save-data entries as well.
            LevelEnter.Go(pending.TargetSession, fromSaveData: true);
        }
        catch (Exception exception) {
            Logger.Warn("SpeedrunTool/PendingLoad", $"Failed to enter the saved chapter: {exception.GetType().Name}: {exception.Message}");
            Logger.LogDetailed(exception, "SpeedrunTool");
            AbortPendingLoad("failed to enter the saved chapter", out _, showPopup: true, restoreScene: true);
        }
    }

    #endregion

    #region Finish

    private static void OnLevelBeginFinishPendingLoad(On.Celeste.Level.orig_Begin orig, Level level) {
        orig(level);

        // Runs after the base Scene.Begin hooks, so SaveSlotsManager.HandleSwitchScene already
        // invalidated and re-ran the pre-clone for the new Level instance.
        // Only the hooks below touch the slot table, and none of them adds/removes slots, so the
        // dictionary can be enumerated directly (no per-call list allocation on a hot hook).
        foreach (SaveSlot slot in SaveSlotsManager.SaveSlots) {
            if (slot.StateManager.pendingLoad is null) {
                continue;
            }

            try {
                SaveSlotsManager.RunWithSlot(slot, () => slot.StateManager.FinishPendingLoadOnTargetLevel(level));
            }
            catch (Exception exception) {
                // Never let a broken third-party load action escape a Scene.Begin hook (crashes the game).
                Logger.Warn("SpeedrunTool/PendingLoad", $"Failed to finish the pending load: {exception.GetType().Name}: {exception.Message}");
                Logger.LogDetailed(exception, "SpeedrunTool");
                SaveSlotsManager.RunWithSlot(slot, () => {
                    slot.StateManager.RecoverFromFailedLoad(level, levelWasUnloaded: true);
                    PopupMessageUtils.Show("Failed to Load: please check the log!", null);
                });
            }
        }
    }

    private void FinishPendingLoadOnTargetLevel(Level level) {
        PendingLoad pending = pendingLoad;
        if (pending is null) {
            return;
        }

        if (!IsSameTargetArea(level.Session, savedLevel?.Session)) {
            AbortPendingLoad("Level.Begin is not the chapter of the savestate", out _, showPopup: true, restoreScene: true);
            return;
        }

        pendingLoad = null;
        double enterMs = (Stopwatch.GetTimestamp() - pending.StartTimestamp) * 1000.0 / Stopwatch.Frequency;
        Logger.Info("SpeedrunTool/PendingLoad",
            $"'{level.Session.Area.GetSID()} [{level.Session.Level}]' is ready after {enterMs:F0} ms, restoring the state from [{SlotName}]");

        try {
            if (!LoadStateCore(level, tas: false, mergeTimeAndDeaths: false, out string popup)) {
                Logger.Warn("SpeedrunTool/PendingLoad", popup);
                PopupMessageUtils.Show(popup, null);
            }
        }
        catch (Exception exception) {
            // LoadStateCore already has its own recovery, this is only a last resort.
            Logger.Warn("SpeedrunTool/PendingLoad", $"Unexpected failure while restoring: {exception.GetType().Name}: {exception.Message}");
            Logger.LogDetailed(exception, "SpeedrunTool");
            RecoverFromFailedLoad(level, levelWasUnloaded: true);
            PopupMessageUtils.Show("Failed to Load: please check the log!", null);
        }
        finally {
            // Level.LoadLevel catches its own exceptions and only records a message; the template
            // restore above may have recovered the Level anyway, so a stale message must not poison
            // the next chapter entry with an old "failed to load" postcard.
            if (LevelEnter.ErrorMessage is not null) {
                Logger.Warn("SpeedrunTool/PendingLoad", "Clearing a level-load error left by the target chapter");
                LevelEnter.ErrorMessage = null;
            }
        }
    }

    #endregion

    #region Restore saved mod sessions before the chapter is built

    private static void OnLevelLoadLevelRestoreModuleStateEarly(On.Celeste.Level.orig_LoadLevel orig, Level level,
        Player.IntroTypes playerIntro, bool isFromLoader) {
        try {
            if (level?.Session is { } session) {
                foreach (SaveSlot slot in SaveSlotsManager.SaveSlots) {
                    if (slot.StateManager.pendingLoad is null
                        || !IsSameTargetArea(session, slot.StateManager.savedLevel?.Session)) {
                        continue;
                    }

                    // LevelLoader already called SaveData.StartSession, which replaces every module's
                    // _Session with a fresh empty object. Put the saved ones back before the entities
                    // are created so mod load-time code sees the session it saw when the state was saved.
                    SaveSlotsManager.RunWithSlot(slot, static () => SaveLoadAction.TryRestoreSavedModuleStateEarly());
                }
            }
        }
        catch (Exception exception) {
            Logger.Warn("SpeedrunTool/PendingLoad", $"Failed to restore saved mod sessions before Level.LoadLevel: {exception.GetType().Name}: {exception.Message}");
            Logger.LogDetailed(exception, "SpeedrunTool");
        }

        orig(level, playerIntro, isFromLoader);
    }

    #endregion

    #region Watchdog / abort

    private static void WatchPendingLoad(On.Monocle.Scene.orig_BeforeUpdate orig, Scene self) {
        orig(self);
        ShowDeferredPendingPopup();

        if (Engine.Scene is null) {
            return;
        }

        foreach (SaveSlot slot in SaveSlotsManager.SaveSlots) {
            if (slot.StateManager.pendingLoad is null) {
                continue;
            }

            try {
                SaveSlotsManager.RunWithSlot(slot, slot.StateManager.CheckPendingLoadProgress);
            }
            catch (Exception exception) {
                Logger.Warn("SpeedrunTool/PendingLoad", $"Pending load watchdog failed: {exception.GetType().Name}: {exception.Message}");
                Logger.LogDetailed(exception, "SpeedrunTool");
            }
        }
    }

    private void CheckPendingLoadProgress() {
        PendingLoad pending = pendingLoad;
        Scene scene = Engine.Scene;
        if (pending is null || scene is null) {
            return;
        }

        bool sameAsStartScene = ReferenceEquals(scene, pending.StartScene);
        bool graceExpired = Engine.FrameCounter > pending.StartFrame + PendingLoadSceneSwitchGraceFrames;
        if (sameAsStartScene && !graceExpired) {
            // Engine.Scene is deferred to the end of the update, give the switch a few frames.
            return;
        }

        if (scene is LevelEnter) {
            pending.SeenLevelEnter = true;
        }
        else if (scene is LevelLoader) {
            pending.EnteredLevelLoader = true;
        }

        PendingLoadFailure failure = PendingLoadWatchdog.Decide(
            sameAsStartScene: sameAsStartScene,
            graceExpired: graceExpired,
            levelEnterError: LevelEnter.ErrorMessage is not null,
            sceneIsOverworld: scene is Overworld or OverworldLoader,
            sceneIsLevel: scene is Level,
            sceneIsLevelEnter: scene is LevelEnter,
            seenLevelEnter: pending.SeenLevelEnter,
            enteredLevelLoader: pending.EnteredLevelLoader,
            timedOut: Stopwatch.GetTimestamp() > pending.DeadlineTicks);

        if (failure == PendingLoadFailure.None) {
            return;
        }

        string reason = DescribePendingLoadFailure(failure);
        Logger.Warn("SpeedrunTool/PendingLoad", $"Pending load watchdog: {failure} ({reason})");
        AbortPendingLoad(reason, out _, showPopup: true, restoreScene: true);
    }

    private static string DescribePendingLoadFailure(PendingLoadFailure failure) {
        return failure switch {
            PendingLoadFailure.TransitionDidNotStart => "the scene transition to the saved chapter did not start",
            PendingLoadFailure.LevelEnterError => "the game failed to load the saved chapter",
            PendingLoadFailure.ReturnedToChapterSelect => "the game returned to the chapter select while entering the saved chapter",
            PendingLoadFailure.LevelLoaderGaveUp => "the level loader gave up while entering the saved chapter",
            PendingLoadFailure.LoadedLevelIsNotTheTarget => "the loaded Level is not the saved chapter",
            PendingLoadFailure.TimedOut => "timed out waiting for the saved chapter to load",
            _ => "the saved chapter could not be entered",
        };
    }

    /// <summary>
    /// Give up on the pending load, put the replaced SaveData / module references back and, when the
    /// game is stuck in a loading scene, escape to the chapter select. The savestate itself is kept so
    /// the player can retry.
    /// </summary>
    private bool AbortPendingLoad(string reason, out string popup, bool showPopup, bool restoreScene) {
        popup = "Failed to Load: could not enter the saved chapter, please check the log!";
        PendingLoad pending = pendingLoad;
        if (pending is null) {
            return false;
        }

        pendingLoad = null;
        Logger.Warn("SpeedrunTool/PendingLoad", $"Aborting the pending load from [{SlotName}]: {reason}");

        RollbackPendingReferences(pending);

        State = State.None;
        freezeType = FreezeType.None;
        Engine.FreezeTimer = 0f;

        if (LevelEnter.ErrorMessage is not null) {
            // The stale message would make the next chapter entry show the old failure postcard.
            Logger.Warn("SpeedrunTool/PendingLoad", "Clearing LevelEnter.ErrorMessage from the failed chapter load");
            LevelEnter.ErrorMessage = null;
        }

        if (restoreScene && !AssetReloadHelper.IsReloading) {
            Scene scene = Engine.Scene;
            bool sceneMatchesTarget = scene is Level level && IsSameTargetArea(level.Session, savedLevel?.Session);
            // Already back on the chapter select: nothing to escape from, show the popup right there.
            if (scene is Overworld) {
                showPopup = true;
            }
            else if (!sceneMatchesTarget && !ReferenceEquals(scene, pending.StartScene)) {
                // Half-way through a chapter transition: let the game's own "chapter quit" flow take
                // over so no half-built Level / loading scene is left behind.
                Engine.Scene = new OverworldLoader(Overworld.StartMode.AreaQuit);
                if (showPopup) {
                    // A Tooltip added to a scene that is about to be replaced would be lost.
                    deferredPendingPopup = popup;
                }
                showPopup = false;
            }
        }

        if (showPopup) {
            PopupMessageUtils.Show(popup, null);
        }

        return true;
    }

    private void AbortPendingLoadForClear() {
        AbortPendingLoad("the state is being cleared", out _, showPopup: false, restoreScene: true);
    }

    internal void CancelPendingLoadForAssetReload() {
        AbortPendingLoad("asset reload", out _, showPopup: false, restoreScene: false);
    }

    private static void ShowDeferredPendingPopup() {
        if (deferredPendingPopup is null || Engine.Scene is not (Overworld or Level)) {
            return;
        }

        string message = deferredPendingPopup;
        deferredPendingPopup = null;
        PopupMessageUtils.Show(message, null);
    }

    #endregion

    #region Book-keeping

    private static List<ModuleStateSnapshot> CaptureModuleStates() {
        List<ModuleStateSnapshot> states = [];
        try {
            foreach (EverestModule module in Everest.Modules) {
                states.Add(new ModuleStateSnapshot(module, module._Session, module._SaveData));
            }
        }
        catch (Exception exception) {
            Logger.LogDetailed(exception, "SpeedrunTool/PendingLoad");
        }

        return states;
    }

    private static void RollbackPendingReferences(PendingLoad pending) {
        try {
            if (pending.OriginalSaveData is not null) {
                pending.OriginalSaveData.LastArea_Safe = pending.OriginalLastAreaSafe;
                pending.OriginalSaveData.CurrentSession_Safe = pending.OriginalCurrentSessionSafe;
                SaveData.Instance = pending.OriginalSaveData;
            }

            foreach (ModuleStateSnapshot state in pending.OriginalModuleStates) {
                if (state.Module is null) {
                    continue;
                }

                state.Module._Session = state.Session;
                state.Module._SaveData = state.SaveData;
            }
        }
        catch (Exception exception) {
            Logger.LogDetailed(exception, "SpeedrunTool/PendingLoad");
        }
    }

    #endregion
}
