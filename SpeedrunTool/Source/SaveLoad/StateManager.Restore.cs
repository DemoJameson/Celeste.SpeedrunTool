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

        // The template is updated in place. This used to first DeepCloneShared() the Session and
        // the SaveData, but the clones were never installed anywhere - they were pure garbage on
        // every load (a SaveData clone is not free: it clones the whole Areas_Safe list).
        savedSaveData.Time = currentSaveData.Time;
        int increaseDeath = level is null || level.IsPlayerDead() ? 0 : 1;
        savedSaveData.TotalDeaths = currentSaveData.TotalDeaths + increaseDeath;
        savedSaveData.TotalDashes = currentSaveData.TotalDashes;
        savedSaveData.TotalJumps = currentSaveData.TotalJumps;
        savedSaveData.TotalWallJumps = currentSaveData.TotalWallJumps;

        if (session is null) {
            return;
        }

        AreaKey areaKey = session.Area;
        savedSession.Time = Math.Max(session.Time, savedSession.Time);

        // A mod map may not have an AreaStats entry yet (or a shorter Modes array): the old
        // unchecked indexing threw ArgumentOutOfRangeException and aborted the whole load.
        if (areaKey.ID >= 0
            && areaKey.ID < savedSaveData.Areas_Safe.Count
            && areaKey.ID < currentSaveData.Areas_Safe.Count
            && (int)areaKey.Mode < savedSaveData.Areas_Safe[areaKey.ID].Modes.Length
            && (int)areaKey.Mode < currentSaveData.Areas_Safe[areaKey.ID].Modes.Length) {
            savedSaveData.Areas_Safe[areaKey.ID].Modes[(int)areaKey.Mode].TimePlayed =
                currentSaveData.Areas_Safe[areaKey.ID].Modes[(int)areaKey.Mode].TimePlayed;
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

        savedSession.Deaths = Math.Max(session.Deaths + increaseDeath, savedSession.Deaths);
        savedSession.DeathsInCurrentLevel =
            Math.Max(session.DeathsInCurrentLevel + increaseDeath, savedSession.DeathsInCurrentLevel);
        if (areaKey.ID >= 0
            && areaKey.ID < savedSaveData.Areas_Safe.Count
            && areaKey.ID < currentSaveData.Areas_Safe.Count
            && (int)areaKey.Mode < savedSaveData.Areas_Safe[areaKey.ID].Modes.Length
            && (int)areaKey.Mode < currentSaveData.Areas_Safe[areaKey.ID].Modes.Length) {
            savedSaveData.Areas_Safe[areaKey.ID].Modes[(int)areaKey.Mode].Deaths =
                currentSaveData.Areas_Safe[areaKey.ID].Modes[(int)areaKey.Mode].Deaths + increaseDeath;
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
}
