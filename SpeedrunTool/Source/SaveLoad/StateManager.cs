using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Celeste.Mod.SpeedrunTool.Message;
using Celeste.Mod.SpeedrunTool.Other;
using Celeste.Mod.SpeedrunTool.Utils;
using FMOD.Studio;
using Force.DeepCloner;
using Force.DeepCloner.Helpers;

namespace Celeste.Mod.SpeedrunTool.SaveLoad;

public sealed class StateManager {
    private static readonly Lazy<StateManager> Lazy = new(() => new StateManager());
    public static StateManager Instance => Lazy.Value;
    private StateManager() { }

    private static readonly Lazy<PropertyInfo> InGameOverworldHelperIsOpen = new(
        () => ModUtils.GetType("CollabUtils2", "Celeste.Mod.CollabUtils2.UI.InGameOverworldHelper")?.GetPropertyInfo("IsOpen")
    );

    // public for tas
    public bool IsSaved => savedLevel != null;
    public State State { get; private set; } = State.None;
    public bool SavedByTas { get; private set; }
    private Level savedLevel;
    private SaveData savedSaveData;
    private Task<DeepCloneState> preCloneTask;
    private FreezeType freezeType;

    private enum FreezeType {
        None,
        Save,
        Load
    }

    private readonly HashSet<EventInstance> playingEventInstances = new();

    #region Hook

    public void Load() {
        On.Monocle.Scene.BeforeUpdate += SceneOnBeforeUpdate;
        On.Celeste.Level.Update += UpdateBackdropWhenWaiting;
        On.Monocle.Scene.Begin += ClearStateWhenSwitchScene;
        On.Celeste.PlayerDeadBody.End += AutoLoadStateWhenDeath;
        RegisterHotkeys();
    }

    public void Unload() {
        On.Monocle.Scene.BeforeUpdate -= SceneOnBeforeUpdate;
        On.Celeste.Level.Update -= UpdateBackdropWhenWaiting;
        On.Monocle.Scene.Begin -= ClearStateWhenSwitchScene;
        On.Celeste.PlayerDeadBody.End -= AutoLoadStateWhenDeath;
    }

    private void RegisterHotkeys() {
        Hotkey.SaveState.RegisterPressedAction(scene => {
            if (scene is Level) {
#if DEBUG
                JetBrains.Profiler.Api.MeasureProfiler.StartCollectingData();
                SaveState(false);
                JetBrains.Profiler.Api.MeasureProfiler.SaveData();
#else
                SaveState(false);
#endif
            }
        });

        Hotkey.LoadState.RegisterPressedAction(scene => {
            if (scene is Level {Paused: false} && State == State.None) {
                if (IsSaved) {
                    LoadState(false);
                } else {
                    PopupMessageUtils.Show(DialogIds.NotSavedStateTooltip.DialogClean(), DialogIds.NotSavedStateYetDialog);
                }
            }
        });

        Hotkey.ClearState.RegisterPressedAction(scene => {
            if (scene is Level {Paused: false} && State == State.None) {
                ClearStateAndShowMessage();
            }
        });

        Hotkey.SwitchAutoLoadState.RegisterPressedAction(scene => {
            if (scene is Level {Paused: false}) {
                ModSettings.AutoLoadStateAfterDeath = !ModSettings.AutoLoadStateAfterDeath;
                SpeedrunToolModule.Instance.SaveSettings();
                string state = (ModSettings.AutoLoadStateAfterDeath ? DialogIds.On : DialogIds.Off).DialogClean();
                PopupMessageUtils.ShowOptionState(DialogIds.AutoLoadStateAfterDeath.DialogClean(), state);
            }
        });
    }

    private void SceneOnBeforeUpdate(On.Monocle.Scene.orig_BeforeUpdate orig, Scene self) {
        if (ModSettings.Enabled && self is Level level && State == State.Waiting && !level.Paused
            && (Input.Dash.Pressed
                || Input.Grab.Check
                || Input.Jump.Check
                || Input.Pause.Check
                || Input.Talk.Check
                || Input.MoveX != 0
                || Input.MoveY != 0
                || Input.Aim.Value != Vector2.Zero
                || typeof(Input).GetFieldValue("DemoDash")?.GetPropertyValue("Pressed") as bool? == true
                || typeof(Input).GetFieldValue("CrouchDash")?.GetPropertyValue("Pressed") as bool? == true
            )) {
            OutOfFreeze(level);
        }

        orig(self);
    }

    private void UpdateBackdropWhenWaiting(On.Celeste.Level.orig_Update orig, Level level) {
        if (State != State.None) {
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

    private void ClearStateWhenSwitchScene(On.Monocle.Scene.orig_Begin orig, Scene self) {
        orig(self);
        if (IsSaved) {
            if (self is Overworld && !SavedByTas && InGameOverworldHelperIsOpen.Value?.GetValue(null) as bool? != true) {
                ClearState();
            }

            // 重启章节 Level 实例变更，所以之前预克隆的实体作废，需要重新克隆
            if (self is Level) {
                State = State.None;
                PreCloneSavedEntities();
            }

            if (self.GetSession() is { } session && session.Area != savedLevel.Session.Area) {
                ClearState();
            }
        }
    }

    private void AutoLoadStateWhenDeath(On.Celeste.PlayerDeadBody.orig_End orig, PlayerDeadBody self) {
        if (ModSettings.Enabled
            && ModSettings.AutoLoadStateAfterDeath
            && IsSaved
            && !SavedByTas
            && !self.GetFieldValue<bool>("finished")
            && Engine.Scene is Level level
            && level.Entities.FindFirst<PlayerSeeker>() == null
           ) {
            level.OnEndOfFrame += () => {
                if (IsSaved) {
                    LoadState(false);
                } else {
                    level.DoScreenWipe(wipeIn: false, self.DeathAction ?? level.Reload);
                }
            };
            self.RemoveSelf();
        } else {
            orig(self);
        }
    }

    #endregion Hook

    // public for TAS Mod
    // ReSharper disable once UnusedMember.Global
    // ReSharper disable once MemberCanBePrivate.Global
    public bool SaveState() {
        return SaveState(true);
    }

    private bool SaveState(bool tas) {
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

        SaveLoadAction.InitActions();

        State = State.Saving;
        SavedByTas = tas;

        SaveLoadAction.OnBeforeSaveState(level);
        level.DeepCloneToShared(savedLevel = new Level());
        savedSaveData = SaveData.Instance.DeepCloneShared();
        SaveLoadAction.OnSaveState(level);
        DeepClonerUtils.ClearSharedDeepCloneState();
        PreCloneSavedEntities();
        if (tas) {
            State = State.None;
        } else {
            FreezeGame(FreezeType.Save);
            level.Add(new WaitingEntity());
        }

        return true;
    }

    // public for TAS Mod
    // ReSharper disable once UnusedMember.Global
    public bool LoadState() {
        return LoadState(true);
    }

    private bool LoadState(bool tas) {
        if (Engine.Scene is not Level level) {
            return false;
        }

        if (!tas && level.Paused || State is State.Loading or State.Waiting || !IsSaved) {
            return false;
        }

        if (tas && !SavedByTas) {
            return false;
        }

        State = State.Loading;
        DeepClonerUtils.SetSharedDeepCloneState(preCloneTask?.Result);

        UpdateTimeAndDeaths(level);
        UnloadLevel(level);

        savedLevel.DeepCloneToShared(level);
        SaveData.Instance = savedSaveData.DeepCloneShared();

        RestoreAudio1(level);
        RestoreCassetteBlockManager1(level);
        SaveLoadAction.OnLoadState(level);
        PreCloneSavedEntities();

        GC.Collect();
        GC.WaitForPendingFinalizers();

        if (tas) {
            LoadStateComplete(level);
        } else {
            FreezeGame(FreezeType.Load);
            DoScreenWipe(level);
        }

        return true;
    }

    // 释放资源，停止正在播放的声音等
    private void UnloadLevel(Level level) {
        List<Entity> entities = new();

        // Player 必须最早移除，不然在处理 player.triggersInside 字段时会产生空指针异常
        entities.AddRange(level.Tracker.GetEntities<Player>());

        // 移除当前房间的实体，照抄 level.UnloadLevel() 方法，不直接调用是因为 BingUI 在该方法中将其存储的 level 设置为了 null
        entities.AddRange(level.GetEntitiesExcludingTagMask((int)Tags.Global));

        // 恢復主音乐
        if (level.Tracker.GetEntity<CassetteBlockManager>() is { } cassetteBlockManager) {
            entities.Add(cassetteBlockManager);
        }

        foreach (Entity entity in entities.Distinct()) {
            try {
                entity.Removed(level);
            } catch (NullReferenceException) {
                // ignore https://discord.com/channels/403698615446536203/954507384183738438/954507384183738438
            }
        }

        // 移除剩下声音组件
        level.Tracker.GetComponentsCopy<SoundSource>().ForEach(component => component.RemoveSelf());
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
        if (savedLevel.GetFieldValue("transition") is Coroutine coroutine
            && coroutine.Current() is { } enumerator
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
        level.TimeActive = savedLevel.TimeActive;
        level.RawTimeActive = savedLevel.RawTimeActive;
        RestoreAudio2();
        RestoreCassetteBlockManager2(level);
        DeepClonerUtils.ClearSharedDeepCloneState();
        State = State.None;
    }

    // 收集需要继续播放的声音
    private void RestoreAudio1(Level level) {
        playingEventInstances.Clear();

        foreach (Component component in level.Entities.SelectMany(entity => entity.Components.ToArray())) {
            if (component is SoundSource {Playing: true} source && source.GetFieldValue("instance") is EventInstance eventInstance) {
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
            if (manager.GetFieldValue("snapshot") is EventInstance snapshot) {
                snapshot.start();
            }
        }
    }

    // 第二步：播放节奏音乐
    private void RestoreCassetteBlockManager2(Level level) {
        if (level.Tracker.GetEntity<CassetteBlockManager>() is { } manager) {
            if (manager.GetFieldValue("sfx") is EventInstance sfx && !manager.GetFieldValue<bool>("isLevelMusic") && manager.GetFieldValue<int>("leadBeats") <= 0) {
                sfx.start();
            }
        }
    }

    // public for tas
    // ReSharper disable once MemberCanBePrivate.Global
    // 为了照顾使用体验（会卡顿，增加 SaveState 时间），不主动触发内存回收
    public void ClearState() {
        // fix: 读档冻结时被TAS清除状态后无法解除冻结
        if (State == State.Waiting && Engine.Scene is Level level) {
            OutOfFreeze(level);
        }

        playingEventInstances.Clear();
        savedLevel = null;
        savedSaveData = null;
        preCloneTask = null;
        SaveLoadAction.OnClearState();
        State = State.None;
    }

    public void ClearStateAndShowMessage() {
        ClearState();
        PopupMessageUtils.Show(DialogIds.ClearStateToolTip.DialogClean(), DialogIds.ClearStateDialog);
    }

    private void PreCloneSavedEntities() {
        SaveLoadAction.OnPreCloneEntities();
        preCloneTask = Task.Run(() => {
            DeepCloneState deepCloneState = new();
            savedLevel.Entities.DeepClone(deepCloneState);
            savedLevel.RendererList.DeepClone(deepCloneState);
            savedSaveData.DeepClone(deepCloneState);
            return deepCloneState;
        });
    }

    private bool IsAllowSave(Level level, bool tas) {
        // 正常游玩时禁止死亡或者跳过过场时存档，TAS 则无以上限制
        // 跳过过场时的黑屏与读档后加的黑屏冲突，会导致一直卡在跳过过场的过程中
        return State == State.None && !level.Paused && (!level.IsPlayerDead() && !level.SkippingCutscene || tas);
    }

    private void FreezeGame(FreezeType freeze) {
        freezeType = freeze;
    }

    private void DoScreenWipe(Level level) {
        level.DoScreenWipe(true, () => {
            if (ModSettings.FreezeAfterLoadState) {
                State = State.Waiting;
            } else {
                OutOfFreeze(level);
            }
        });
    }

    private void OutOfFreeze(Level level) {
        if (freezeType == FreezeType.Save || savedLevel == null) {
            if (savedLevel != null) {
                level.TimeActive = savedLevel.TimeActive;
                level.RawTimeActive = savedLevel.RawTimeActive;
            }

            State = State.None;
        } else {
            LoadStateComplete(level);
        }

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