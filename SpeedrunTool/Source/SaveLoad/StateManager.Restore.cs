using Celeste.Mod.SpeedrunTool.ModInterop;
using System.Linq;
using System.Reflection;
using EventInstance = FMOD.Studio.EventInstance;

namespace Celeste.Mod.SpeedrunTool.SaveLoad;

/// <summary>
/// Logical save-data / time / audio / cassette state restoration after a load.
/// </summary>
public sealed partial class StateManager {
    private void UpdateTimeAndDeaths(Level level) {
        MergeTimeAndDeaths(level?.Session, SaveData.Instance, level);
    }

    /// <summary>
    /// Merges the live run's time/deaths/monotonic counters into the saved templates when
    /// "Save Time, Deaths and Counters" is off.
    /// The live data is read from the scene that is still active, so a pending (cross-scene) load
    /// calls this BEFORE leaving for the saved chapter; the actual restore then runs without merging.
    /// Loading a state saved from another save file keeps the template unchanged: both files have
    /// their own totals and per-area stats, and mixing them would corrupt the restored SaveData.
    /// </summary>
    private void MergeTimeAndDeaths(Session session, SaveData currentSaveData, Level level) {
        if (SavedByTas || ModSettings.SaveTimeAndDeaths) {
            return;
        }

        if (savedLevel?.Session is not { } savedSession || savedSaveData is null || currentSaveData is null) {
            return;
        }

        if (currentSaveData.FileSlot != savedSaveData.FileSlot) {
            Logger.Info("SpeedrunTool/LoadState",
                $"Not merging time/deaths: state was saved on save file {savedSaveData.FileSlot}, current file is {currentSaveData.FileSlot}");
            return;
        }

        // The overworld has no Session; fall back to the last session of the save file so time and
        // deaths still never roll back when loading from the chapter select.
        session ??= currentSaveData.CurrentSession_Safe;

        int increaseDeath = level is null || level.IsPlayerDead() ? 0 : 1;

        // The restore installs whatever the shared clone state returns for savedSaveData, and the
        // pre-clone task registered a copy of it before this merge runs. Writing only the template
        // would be discarded by that copy (the merge then took effect every other load, and after a
        // remap it costs a full SaveData clone on every load), so the same merge is applied to both.
        //
        // The Session is in that state for a less obvious reason: SaveData.StartSession stores the
        // level's session in savedSaveData.CurrentSession_Safe, so at save time the level clone had
        // already registered that instance and the SaveData clone reused it. Cloning savedSaveData in
        // the pre-clone therefore clones the Session too, and savedLevel.Session is that same instance.
        // The restore installs that copy, so the template write alone would not survive a load.
        SaveData cachedSaveData = DeepClonerUtils.GetSharedClone(savedSaveData) as SaveData;
        Session cachedSession = DeepClonerUtils.GetSharedClone(savedSession) as Session;

        MergeSaveData(savedSaveData, currentSaveData, increaseDeath);
        if (cachedSaveData is not null) {
            MergeSaveData(cachedSaveData, currentSaveData, increaseDeath);
        }

        if (session is null) {
            return;
        }

        AreaKey areaKey = session.Area;

        // Session time/deaths belong to the chapter the state was saved in. A cross-chapter load
        // carries the live session of a different chapter, and taking the Max would hand the target
        // chapter the current chapter's timer and death count -- the "load a state from another
        // chapter and B's timer shows A's time" bug. Merge it only when both are the same chapter;
        // the file totals (MergeSaveData) and the current chapter's AreaStats still merge below.
        if (session.Area == savedSession.Area) {
            MergeSession(savedSession, session, increaseDeath);
            if (cachedSession is not null) {
                MergeSession(cachedSession, session, increaseDeath);
            }

            // 修复：切屏时存档，若干秒后读档游戏会误以为卡死自动重生
            //
            // 2025.08.25: 这里我们不能用 level.transition, 因为有可能 TransitionRoutine 已经被 hook 过
            // 使得这个 coroutine 的类型不一样, 这里写的反射获取 <playerStuck> 就不对了
            // 不过理论上多写一些代码去解开 Coroutine 的嵌套应该也行...
            // 这个 bug 被 DJ 在 2023.10.07 修复了, 不过我把多存档合并进来的时候可能不小心丢失了这个提交
            // 总之我们采用 DJ 的写法
            //
            // Inside the same-chapter guard on purpose: the value written is the live session's time,
            // and only a same-chapter merge makes the restored session carry that time. A
            // cross-chapter load keeps the saved session's time, so writing the current chapter's
            // time here would mix the two chapters this guard exists to keep apart.
            if (savedTransitionRoutine is not null) {
                Type routineType = savedTransitionRoutine.GetType();
                FieldInfo playerStuck = routineType
                    .GetFields(BindingFlags.Instance | BindingFlags.NonPublic)
                    .FirstOrDefault(info => info.Name.StartsWith("<playerStuck>"));

                // The name and type are compiler-generated: a refactored coroutine renames the field,
                // and a changed one makes SetValue throw *after* Time/Deaths were written to the
                // template, so a retried load counts the death twice. Check before writing; the
                // warning fires on every load, which is fine -- a renamed field is a persistent
                // defect, not a transient one.
                if (playerStuck is null || playerStuck.FieldType != typeof(TimeSpan)) {
                    Logger.Warn("SpeedrunTool/LoadState", playerStuck is null
                        ? $"{routineType.FullName} has no <playerStuck> field; its saved transition cannot be adjusted."
                        : $"{routineType.FullName}.<playerStuck> is a {playerStuck.FieldType.Name}, not a TimeSpan; its saved transition cannot be adjusted.");
                }
                else {
                    playerStuck.SetValue(savedTransitionRoutine, TimeSpan.FromTicks(session.Time));
                    playerStuck.SetValue(savedTransitionRoutine.DeepCloneShared(), TimeSpan.FromTicks(session.Time));
                }
            }
        }

        MergeAreaStats(savedSaveData, currentSaveData, areaKey, increaseDeath);
        if (cachedSaveData is not null) {
            MergeAreaStats(cachedSaveData, currentSaveData, areaKey, increaseDeath);
        }
    }

    private static void MergeSaveData(SaveData target, SaveData current, int increaseDeath) {
        target.Time = current.Time;
        target.TotalDeaths = current.TotalDeaths + increaseDeath;
        target.TotalDashes = current.TotalDashes;
        target.TotalJumps = current.TotalJumps;
        target.TotalWallJumps = current.TotalWallJumps;
    }

    private static void MergeSession(Session target, Session current, int increaseDeath) {
        target.Time = Math.Max(current.Time, target.Time);
        target.Deaths = Math.Max(current.Deaths + increaseDeath, target.Deaths);
        target.DeathsInCurrentLevel = Math.Max(current.DeathsInCurrentLevel + increaseDeath, target.DeathsInCurrentLevel);
    }

    // A mod map may not have an AreaStats entry yet (or a shorter Modes array): the old unchecked
    // indexing threw ArgumentOutOfRangeException and aborted the whole load.
    private static void MergeAreaStats(SaveData target, SaveData current, AreaKey areaKey, int increaseDeath) {
        if (areaKey.ID < 0
            || areaKey.ID >= target.Areas_Safe.Count
            || areaKey.ID >= current.Areas_Safe.Count
            || (int)areaKey.Mode >= target.Areas_Safe[areaKey.ID].Modes.Length
            || (int)areaKey.Mode >= current.Areas_Safe[areaKey.ID].Modes.Length) {
            return;
        }

        target.Areas_Safe[areaKey.ID].Modes[(int)areaKey.Mode].TimePlayed =
            current.Areas_Safe[areaKey.ID].Modes[(int)areaKey.Mode].TimePlayed;
        target.Areas_Safe[areaKey.ID].Modes[(int)areaKey.Mode].Deaths =
            current.Areas_Safe[areaKey.ID].Modes[(int)areaKey.Mode].Deaths + increaseDeath;
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
}
