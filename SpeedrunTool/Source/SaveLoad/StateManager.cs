using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Celeste.Mod.SpeedrunTool.Extensions;
using Celeste.Mod.SpeedrunTool.Message;
using Celeste.Mod.SpeedrunTool.Other;
using FMOD.Studio;
using Force.DeepCloner;
using Force.DeepCloner.Helpers;
using Microsoft.Xna.Framework;
using Monocle;

namespace Celeste.Mod.SpeedrunTool.SaveLoad {
    public sealed class StateManager {
        private static SpeedrunToolSettings Settings => SpeedrunToolModule.Settings;

        private static readonly Lazy<PropertyInfo> InGameOverworldHelperIsOpen = new(
            () => Type.GetType("Celeste.Mod.CollabUtils2.UI.InGameOverworldHelper, CollabUtils2")?.GetPropertyInfo("IsOpen")
        );

        // public for tas
        public bool IsSaved => savedLevel != null;
        public States State { get; private set; } = States.None;
        public bool SavedByTas { get; private set; }
        private Level savedLevel;
        private SaveData savedSaveData;
        private Task<DeepCloneState> preCloneTask;

        public enum States {
            None,
            Saving,
            Loading,
            Waiting,
        }

        private readonly HashSet<EventInstance> playingEventInstances = new();

        #region Hook

        public void Load() {
            On.Celeste.Level.Update += UpdateBackdropWhenWaiting;
            On.Monocle.Scene.Begin += ClearStateWhenSwitchScene;
            On.Celeste.PlayerDeadBody.End += AutoLoadStateWhenDeath;
            On.Monocle.Scene.BeforeUpdate += SceneOnBeforeUpdate;
            RegisterHotkeys();
        }

        public void Unload() {
            On.Celeste.Level.Update -= UpdateBackdropWhenWaiting;
            On.Monocle.Scene.Begin -= ClearStateWhenSwitchScene;
            On.Celeste.PlayerDeadBody.End -= AutoLoadStateWhenDeath;
            On.Monocle.Scene.BeforeUpdate -= SceneOnBeforeUpdate;
        }

        private void RegisterHotkeys() {
            Hotkeys.SaveState.RegisterPressedAction(scene => {
                if (scene is Level) {
                    SaveState(false);
                }
            });

            Hotkeys.LoadState.RegisterPressedAction(scene => {
                if (scene is Level level && !level.PausedNew() && State == States.None) {
                    if (IsSaved) {
                        LoadState(false);
                    } else {
                        PopupMessageUtils.Show(level, DialogIds.NotSavedStateTooltip.DialogClean(), DialogIds.NotSavedStateYetDialog);
                    }
                }
            });

            Hotkeys.ClearState.RegisterPressedAction(scene => {
                if (scene is Level level && !level.PausedNew() && State == States.None) {
                    ClearState(true);
                    PopupMessageUtils.Show(level, DialogIds.ClearStateToolTip.DialogClean(), DialogIds.ClearStateDialog);
                }
            });

            Hotkeys.SwitchAutoLoadState.RegisterPressedAction(scene => {
                if (scene is Level level && !level.PausedNew()) {
                    Settings.AutoLoadStateAfterDeath = !Settings.AutoLoadStateAfterDeath;
                    SpeedrunToolModule.Instance.SaveSettings();
                    string state = (Settings.AutoLoadStateAfterDeath ? DialogIds.On : DialogIds.Off).DialogClean();
                    PopupMessageUtils.ShowOptionState(level, DialogIds.AutoLoadStateAfterDeath.DialogClean(), state);
                }
            });
        }

        private void SceneOnBeforeUpdate(On.Monocle.Scene.orig_BeforeUpdate orig, Scene self) {
            if (Settings.Enabled && self is Level level && State == States.Waiting && !level.PausedNew()
                && (Input.Dash.Pressed
                    || Input.Grab.Check
                    || Input.Jump.Check
                    || Input.Pause.Check
                    || Input.Talk.Check
                    || Input.MoveX != 0
                    || Input.MoveY != 0
                    || Input.Aim.Value != Vector2.Zero
                    || HotkeyConfigUi.GetVirtualButton(Hotkeys.LoadState).Released
                    || typeof(Input).GetFieldValue("DemoDash")?.GetPropertyValue("Pressed") as bool? == true
                    || typeof(Input).GetFieldValue("CrouchDash")?.GetPropertyValue("Pressed") as bool? == true
                )) {
                LoadStateComplete(level);
            }

            orig(self);
        }

        private void UpdateBackdropWhenWaiting(On.Celeste.Level.orig_Update orig, Level level) {
            orig(level);

            if (State == States.Waiting && level.Frozen) {
                level.Foreground.Update(level);
                level.Background.Update(level);
            }
        }

        private void ClearStateWhenSwitchScene(On.Monocle.Scene.orig_Begin orig, Scene self) {
            orig(self);
            if (IsSaved) {
                if (self is Overworld && !SavedByTas && InGameOverworldHelperIsOpen.Value?.GetValue(null) as bool? != true) {
                    ClearState(true);
                }

                if (self is Level) {
                    State = States.None; // 修复：读档途中按下 PageDown/Up 后无法存档
                    PreCloneSavedEntities();
                }

                if (self.GetSession() is { } session && session.Area != savedLevel.Session.Area) {
                    ClearState(true);
                }
            }
        }

        private void AutoLoadStateWhenDeath(On.Celeste.PlayerDeadBody.orig_End orig, PlayerDeadBody self) {
            if (SpeedrunToolModule.Settings.Enabled
                && SpeedrunToolModule.Settings.AutoLoadStateAfterDeath
                && IsSaved
                && !SavedByTas
                && !(bool)self.GetFieldValue("finished")
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

            ClearState(false);

            State = States.Saving;
            SavedByTas = tas;

            SaveLoadAction.OnBeforeSaveState(level);
            savedLevel = new Level();
            level.DeepCloneToShared(savedLevel);
            savedSaveData = SaveData.Instance.DeepCloneShared();
            SaveLoadAction.OnSaveState(level);
            DeepClonerUtils.ClearSharedDeepCloneState();

            State = States.None;
            return LoadState(tas);
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

            if (level.PausedNew() || State is States.Loading or States.Waiting || !IsSaved) {
                return false;
            }

            if (tas && !SavedByTas) {
                return false;
            }

            State = States.Loading;
            DeepClonerUtils.SetSharedDeepCloneState(preCloneTask?.Result);

            DoNotRestoreTimeAndDeaths(level);

            UnloadLevel(level);
            savedLevel.DeepCloneToShared(level);
            IgnoreSaveLoadComponent.ReAddAll(level);
            SaveData.Instance = savedSaveData.DeepCloneShared();
            RestoreAudio1(level);
            RestoreCassetteBlockManager1(level);
            SaveLoadAction.OnLoadState(level);

            // 假如放在 UnloadLevel 前面，则 FNA+非D3D 的版本读档时会卡死在 preCloneTask?.Result，为什么呢
            PreCloneSavedEntities();

            GC.Collect();
            GC.WaitForPendingFinalizers();

            if (tas) {
                LoadStateComplete(level);
            } else {
                // 加一个转场等待，避免太突兀
                // Add a pause to avoid being too abrupt
                level.Frozen = true;
                level.TimerStopped = true;
                level.PauseLock = true;
                level.DoScreenWipe(true, () => {
                    if (Settings.FreezeAfterLoadState) {
                        State = States.Waiting;
                    } else {
                        LoadStateComplete(level);
                    }
                });
            }

            return true;
        }

        // 释放资源，停止正在播放的声音等
        private void UnloadLevel(Level level) {
            // Player 必须最早移除，不然在处理 player.triggersInside 字段时会产生空指针异常
            level.Tracker.GetEntitiesCopy<Player>().ForEach(level.Remove);

            // 移除当前房间的实体
            level.UnloadLevel();

            // 剩下则通过 SceneEnd 处理可以不用顾忌移除顺序
            level.Entities.ToList().ForEach(entity => entity.SceneEnd(level));
        }

        private void DoNotRestoreTimeAndDeaths(Level level) {
            if (!SavedByTas && Settings.DoNotRestoreTimeAndDeaths) {
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
                }

                int increaseDeath = 1;
                if (level.IsPlayerDead()) {
                    increaseDeath = 0;
                }

                clonedSession.Deaths = savedSession.Deaths = Math.Max(session.Deaths + increaseDeath, clonedSession.Deaths);
                clonedSession.DeathsInCurrentLevel = savedSession.DeathsInCurrentLevel =
                    Math.Max(session.DeathsInCurrentLevel + increaseDeath, clonedSession.DeathsInCurrentLevel);
                clonedSaveData.TotalDeaths = SaveData.Instance.TotalDeaths + increaseDeath;
                clonedSaveData.Areas_Safe[areaKey.ID].Modes[(int)areaKey.Mode].Deaths =
                    SaveData.Instance.Areas_Safe[areaKey.ID].Modes[(int)areaKey.Mode].Deaths + increaseDeath;
            }
        }

        private void LoadStateComplete(Level level) {
            level.Frozen = savedLevel.Frozen;
            level.TimerStopped = savedLevel.TimerStopped;
            level.PauseLock = savedLevel.PauseLock;
            level.TimeActive = savedLevel.TimeActive;
            level.RawTimeActive = savedLevel.RawTimeActive;
            RestoreAudio2();
            RestoreCassetteBlockManager2(level);
            DeepClonerUtils.ClearSharedDeepCloneState();
            State = States.None;
        }

        // 收集需要继续播放的声音
        private void RestoreAudio1(Level level) {
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
                if (manager.GetFieldValue("sfx") is EventInstance sfx &&
                    !(bool)manager.GetFieldValue("isLevelMusic")) {
                    if ((int)manager.GetFieldValue("leadBeats") <= 0) {
                        sfx.start();
                    }
                }
            }
        }

        // public for tas
        // ReSharper disable once UnusedMember.Global
        // ReSharper disable once MemberCanBePrivate.Global
        public void ClearState() {
            ClearState(false);
        }

        private void ClearState(bool fullClear) {
            playingEventInstances.Clear();
            savedLevel = null;
            savedSaveData = null;
            preCloneTask = null;
            SaveLoadAction.OnClearState(fullClear);
            State = States.None;
        }

        private void PreCloneSavedEntities() {
            preCloneTask = Task.Run(() => {
                DeepCloneState deepCloneState = new();
                savedLevel.Entities.DeepClone(deepCloneState);
                savedLevel.RendererList.DeepClone(deepCloneState);
                savedSaveData.DeepClone(deepCloneState);
                return deepCloneState;
            });
        }

        private bool IsAllowSave(Level level, bool tas) {
            // 正常游玩时禁止死亡或者跳过过场时读档，TAS 则无以上限制
            // 跳过过场时的黑屏与读档后加的黑屏冲突，会导致一直卡在跳过过场的过程中
            return State == States.None && !level.PausedNew() && (!level.IsPlayerDead() && !level.SkippingCutscene || tas);
        }

        // @formatter:off
        private static readonly Lazy<StateManager> Lazy = new(() => new StateManager());
        public static StateManager Instance => Lazy.Value;

        private StateManager() { }
        // @formatter:on
    }
}