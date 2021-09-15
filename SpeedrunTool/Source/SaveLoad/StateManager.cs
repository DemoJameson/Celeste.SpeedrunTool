using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Celeste.Mod.SpeedrunTool.DeathStatistics;
using Celeste.Mod.SpeedrunTool.Extensions;
using Celeste.Mod.SpeedrunTool.Message;
using Celeste.Mod.SpeedrunTool.Other;
using Celeste.Mod.SpeedrunTool.RoomTimer;
using FMOD.Studio;
using Force.DeepCloner;
using Force.DeepCloner.Helpers;
using Microsoft.Xna.Framework;
using Mono.Cecil.Cil;
using Monocle;
using MonoMod.Cil;
using MonoMod.RuntimeDetour;

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
        private List<Entity> savedEntities;
        private SaveData savedSaveData;
        private object transitionRoutine;
        private object savedTransitionRoutine;
        private FieldInfo playerStuckFieldInfo;
        private Dictionary<Type, Dictionary<Entity, int>> savedOrderedTrackerEntities;
        private Dictionary<Type, Dictionary<Component, int>> savedOrderedTrackerComponents;

        private Task<DeepCloneState> preCloneTask;

        public enum States {
            None,
            Saving,
            Loading,
            Waiting,
        }

        private readonly HashSet<EventInstance> playingEventInstances = new();

        private ILHook ilHook;

        #region Hook

        public void Load() {
            On.Celeste.Level.Update += UpdateBackdropWhenWaiting;
            On.Monocle.Scene.Begin += ClearStateWhenSwitchScene;
            On.Celeste.PlayerDeadBody.End += AutoLoadStateWhenDeath;
            On.Monocle.Scene.BeforeUpdate += SceneOnBeforeUpdate;
            ilHook = new ILHook(typeof(Level).GetMethodInfo("TransitionRoutine"), LevelOnTransitionRoutine);
            RegisterHotkeys();
        }

        public void Unload() {
            On.Celeste.Level.Update -= UpdateBackdropWhenWaiting;
            On.Monocle.Scene.Begin -= ClearStateWhenSwitchScene;
            On.Celeste.PlayerDeadBody.End -= AutoLoadStateWhenDeath;
            On.Monocle.Scene.BeforeUpdate -= SceneOnBeforeUpdate;
            ilHook?.Dispose();
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
                if (preCloneTask == null) {
                    // savestate 之后的解冻
                    WaitSaveStateEntity.OutOfWaiting(level);
                } else {
                    // loadstate 之后的解冻
                    LoadStateComplete(level);
                }
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

        private void LevelOnTransitionRoutine(ILContext il) {
            ILCursor ilCursor = new(il);
            if (ilCursor.TryGotoNext(MoveType.After, ins => ins.OpCode == OpCodes.Newobj)) {
                ilCursor.Emit(OpCodes.Dup).EmitDelegate<Action<object>>(obj => {
                    transitionRoutine = obj;
                    if (obj == null || playerStuckFieldInfo != null) {
                        return;
                    }

                    foreach (FieldInfo fieldInfo in obj.GetType().GetFields(BindingFlags.Instance | BindingFlags.NonPublic)) {
                        if (fieldInfo.Name.StartsWith("<playerStuck>")) {
                            playerStuckFieldInfo = fieldInfo;
                        }
                    }
                });
            }
        }

        #endregion Hook

        // public for TAS Mod
        // ReSharper disable once UnusedMember.Global
        public bool SaveState() {
            return SaveState(true);
        }

        private bool SaveState(bool tas) {
            if (Engine.Scene is not Level level) {
                return false;
            }

            if (!IsAllowSave(level, level.GetPlayer())) {
                return false;
            }

            // 不允许玩家在黑屏时保存状态，因为如果在黑屏结束一瞬间之前保存，读档后没有黑屏等待时间感觉会很突兀
            if (!tas && level.Wipe != null) {
                return false;
            }

            // 不允许在春游图打开章节面板时存档
            if (InGameOverworldHelperIsOpen.Value?.GetValue(null) as bool? == true) {
                return false;
            }

            ClearState(false);

            State = States.Saving;

            SavedByTas = tas;

            savedLevel = level.ShallowClone();
            savedLevel.FormationBackdrop = level.FormationBackdrop.ShallowClone();
            savedLevel.Session = level.Session.DeepCloneShared();
            savedLevel.Camera = level.Camera.DeepCloneShared();

            // Renderer
            savedLevel.Bloom = level.Bloom.DeepCloneShared();
            savedLevel.Background = level.Background.DeepCloneShared();
            savedLevel.Foreground = level.Foreground.DeepCloneShared();
            // savedLevel.HudRenderer = level.HudRenderer.DeepCloneShared();
            // savedLevel.SubHudRenderer = level.SubHudRenderer.DeepCloneShared();
            // savedLevel.Displacement = level.Displacement.DeepCloneShared();

            // 只需浅克隆
            savedLevel.Lighting = level.Lighting.ShallowClone();

            // 无需克隆，且里面有 camera
            // savedLevel.GameplayRenderer = level.GameplayRenderer.DeepCloneShared();

            // Renderer nullable
            // savedLevel.Wipe = level.Wipe.DeepCloneShared();

            savedLevel.SetFieldValue("transition", level.GetFieldValue("transition").DeepCloneShared());
            savedLevel.SetFieldValue("skipCoroutine", level.GetFieldValue("skipCoroutine").DeepCloneShared());
            savedLevel.SetFieldValue("onCutsceneSkip", level.GetFieldValue("onCutsceneSkip").DeepCloneShared());
            // savedLevel.SetPropertyValue<Scene>("RendererList", level.RendererList.DeepCloneShared());

            savedEntities = GetEntitiesNeedDeepClone(level).DeepCloneShared();

            savedOrderedTrackerEntities = new Dictionary<Type, Dictionary<Entity, int>>();
            foreach (Entity savedEntity in savedEntities) {
                Type type = savedEntity.GetType();
                if (savedOrderedTrackerEntities.ContainsKey(type)) {
                    continue;
                }

                Dictionary<Type, List<Entity>> trackerEntities = level.Tracker.Entities;
                if (trackerEntities.ContainsKey(type) && trackerEntities[type].Count > 0) {
                    List<Entity> clonedEntities = trackerEntities[type].DeepCloneShared();
                    Dictionary<Entity, int> dictionary = new();
                    for (int i = 0; i < clonedEntities.Count; i++) {
                        dictionary[clonedEntities[i]] = i;
                    }

                    savedOrderedTrackerEntities[type] = dictionary;
                }
            }

            savedOrderedTrackerComponents = new Dictionary<Type, Dictionary<Component, int>>();
            foreach (Component component in savedEntities.SelectMany(entity => entity.Components)) {
                Type type = component.GetType();
                if (savedOrderedTrackerComponents.ContainsKey(type)) {
                    continue;
                }

                Dictionary<Type, List<Component>> trackerComponents = level.Tracker.Components;
                if (trackerComponents.ContainsKey(type) && trackerComponents[type].Count > 0) {
                    List<Component> clonedComponents = trackerComponents[type].DeepCloneShared();
                    Dictionary<Component, int> dictionary = new();
                    for (int i = 0; i < clonedComponents.Count; i++) {
                        dictionary[clonedComponents[i]] = i;
                    }

                    savedOrderedTrackerComponents[type] = dictionary;
                }
            }

            savedSaveData = SaveData.Instance.DeepCloneShared();
            savedTransitionRoutine = transitionRoutine.DeepCloneShared();

            // Mod 和其他
            SaveLoadAction.OnSaveState(level);

            DeepClonerUtils.ClearSharedDeepCloneState();

            if (tas) {
                State = States.None;
                return LoadState(true);
            } else {
                level.Add(new WaitSaveStateEntity(level));
                return true;
            }
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

            // 修复问题：死亡瞬间读档 PlayerDeadBody 没被清除，导致读档完毕后 madeline 自动 retry
            level.Entities.UpdateLists();

            // External
            RoomTimerManager.ResetTime();
            DeathStatisticsManager.Clear();

            DoNotRestoreTimeAndDeaths(level);

            level.Displacement.Clear();
            level.Particles.Clear();
            level.ParticlesBG.Clear();
            level.ParticlesFG.Clear();
            level.Tracker.GetEntities<TrailManager.Snapshot>().ForEach(entity => entity.Position = savedLevel.Camera.Position - Vector2.One * 100);

            UnloadLevelEntities(level);

            GC.Collect();
            GC.WaitForPendingFinalizers();

            RestoreLevelEntities(level);
            RestoreCassetteBlockManager1(level); // 停止播放主音乐，等待播放节奏音乐
            RestoreLevel(level);

            SaveData.Instance = savedSaveData.DeepCloneShared();

            // Mod 和其他
            SaveLoadAction.OnLoadState(level);

            // 修复问题：未打开自动读档时，死掉按下确认键后读档完成会接着执行 Reload 复活方法
            // Fix: When AutoLoadStateAfterDeath is off, if manually LoadState() after death, level.Reload() will still be executed.
            ClearScreenWipe(level);

            if (tas) {
                LoadStateComplete(level);
                return true;
            }

            // 加一个转场等待，避免太突兀
            // Add a pause to avoid being too abrupt
            level.Frozen = true;
            level.TimerStopped = true;
            level.PauseLock = true;
            level.DoScreenWipe(true, () => {
                // 修复问题：死亡后出现黑屏的一瞬间手动读档后游戏崩溃，因为 ScreenWipe 执行了 level.Reload() 方法
                // System.NullReferenceException: 未将对象引用设置到对象的实例。
                // 在 Celeste.CameraTargetTrigger.OnLeave(Player player)
                // 在 Celeste.Player.Removed(Scene scene)
                ClearScreenWipe(level);

                LoadStateEnd(level);
            });

            return true;
        }

        private void DoNotRestoreTimeAndDeaths(Level level) {
            if (!SavedByTas && Settings.DoNotRestoreTimeAndDeaths) {
                Session session = level.Session;
                Session clonedSession = savedLevel.Session.DeepCloneShared();
                SaveData clonedSaveData = savedSaveData.DeepCloneShared();
                AreaKey areaKey = session.Area;

                clonedSession.Time = session.Time;
                clonedSaveData.Time = SaveData.Instance.Time;
                clonedSaveData.Areas_Safe[areaKey.ID].Modes[(int)areaKey.Mode].TimePlayed =
                    SaveData.Instance.Areas_Safe[areaKey.ID].Modes[(int)areaKey.Mode].TimePlayed;

                // 修复：切换房间时存档后读档导致游戏误以为卡死自动重生
                if (savedLevel.Transitioning && savedTransitionRoutine != null) {
                    transitionRoutine = savedTransitionRoutine.DeepCloneShared();
                    playerStuckFieldInfo?.SetValue(transitionRoutine, TimeSpan.FromTicks(session.Time));
                }

                int increaseDeath = 1;
                if (level.IsPlayerDead() || level.GetPlayer().JustRespawned) {
                    increaseDeath = 0;
                }

                clonedSession.Deaths = session.Deaths + increaseDeath;
                clonedSession.DeathsInCurrentLevel = session.DeathsInCurrentLevel + increaseDeath;
                clonedSaveData.TotalDeaths = SaveData.Instance.TotalDeaths + increaseDeath;
                clonedSaveData.Areas_Safe[areaKey.ID].Modes[(int)areaKey.Mode].Deaths =
                    SaveData.Instance.Areas_Safe[areaKey.ID].Modes[(int)areaKey.Mode].Deaths + increaseDeath;
            }
        }

        private void ClearScreenWipe(Level level) {
            level.RendererList.Renderers.ForEach(renderer => {
                if (renderer is ScreenWipe wipe) {
                    wipe.Cancel();
                }
            });
        }

        private void LoadStateEnd(Level level) {
            if (Settings.FreezeAfterLoadState) {
                State = States.Waiting;
                level.PauseLock = true;
            } else {
                LoadStateComplete(level);
            }
        }

        private void LoadStateComplete(Level level) {
            RestoreLevel(level);
            RestoreCassetteBlockManager2(level);
            EndPoint.All.ForEach(point => point.ReadyForTime());
            foreach (EventInstance instance in playingEventInstances) {
                instance.start();
            }

            playingEventInstances.Clear();
            DeepClonerUtils.ClearSharedDeepCloneState();
            State = States.None;
        }

        // 分两步的原因是更早的停止音乐，听起来更舒服更好一点
        private void RestoreCassetteBlockManager1(Level level) {
            if (level.Tracker.GetEntity<CassetteBlockManager>() is { } manager) {
                if (manager.GetFieldValue("snapshot") is EventInstance snapshot) {
                    snapshot.start();
                }
            }
        }

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
        public void ClearState() {
            ClearState(false);
        }

        private void ClearState(bool fullClear) {
            if (fullClear) {
                transitionRoutine = null;
            }

            RoomTimerManager.ClearPbTimes(fullClear);

            playingEventInstances.Clear();

            savedLevel = null;
            savedEntities?.Clear();
            savedEntities = null;
            savedSaveData = null;
            savedTransitionRoutine = null;
            savedOrderedTrackerEntities?.Clear();
            savedOrderedTrackerEntities = null;
            savedOrderedTrackerComponents?.Clear();
            savedOrderedTrackerComponents = null;

            preCloneTask = null;

            DeepClonerUtils.ClearSharedDeepCloneState();

            // Mod
            SaveLoadAction.OnClearState();

            DynDataUtils.OnClearState();

            State = States.None;
        }

        private void UnloadLevelEntities(Level level) {
            List<Entity> entities = GetEntitiesExcludingGlobal(level, true);
            level.Remove(entities);
            level.Entities.UpdateLists();
        }

        private void PreCloneSavedEntities() {
            preCloneTask = Task.Run(() => {
                DeepCloneState deepCloneState = new();
                savedEntities.DeepClone(deepCloneState);
                return deepCloneState;
            });
        }

        private void RestoreLevelEntities(Level level) {
            List<Entity> deepCloneEntities = savedEntities.DeepCloneShared();
            PreCloneSavedEntities();

            // Re Add Entities
            List<Entity> entities = (List<Entity>)level.Entities.GetFieldValue("entities");
            HashSet<Entity> current = (HashSet<Entity>)level.Entities.GetFieldValue("current");
            foreach (Entity entity in deepCloneEntities) {
                if (entities.Contains(entity)) {
                    continue;
                }

                current.Add(entity);
                entities.Add(entity);

                level.TagLists.InvokeMethod("EntityAdded", entity);
                level.Tracker.InvokeMethod("EntityAdded", entity);
                entity.Components?.ToList()
                    .ForEach(component => {
                        level.Tracker.InvokeMethod("ComponentAdded", component);

                        // 等 ScreenWipe 完毕再重新播放
                        if (component is SoundSource { Playing: true } source && source.GetFieldValue("instance") is EventInstance eventInstance) {
                            playingEventInstances.Add(eventInstance);
                        }
                    });
                level.InvokeMethod("SetActualDepth", entity);
                Dictionary<Type, Queue<Entity>> pools = (Dictionary<Type, Queue<Entity>>)Engine.Pooler.GetPropertyValue("Pools");
                Type type = entity.GetType();
                if (pools.ContainsKey(type) && pools[type].Count > 0) {
                    pools[type].Dequeue();
                }
            }

            level.Entities.SetFieldValue("unsorted", false);
            entities.Sort(EntityList.CompareDepth);

            RestoreTrackerOrder(level.Tracker.Entities, savedOrderedTrackerEntities);
            RestoreTrackerOrder(level.Tracker.Components, savedOrderedTrackerComponents);
        }

        private void RestoreTrackerOrder<T>(Dictionary<Type, List<T>> objects, Dictionary<Type, Dictionary<T, int>> orderedTrackerObjects) {
            orderedTrackerObjects = orderedTrackerObjects.DeepCloneShared();
            foreach (Type type in orderedTrackerObjects.Keys) {
                if (!objects.ContainsKey(type)) {
                    continue;
                }

                Dictionary<T, int> orderedDict = orderedTrackerObjects[type];
                List<T> unorderedList = objects[type];
                unorderedList.Sort((object1, object2) => {
                    if (orderedDict.ContainsKey(object1) && orderedDict.ContainsKey(object2)) {
                        return orderedDict[object1] - orderedDict[object2];
                    }

                    return 0;
                });
            }
        }

        private void RestoreLevel(Level level) {
            level.Camera.CopyFrom(savedLevel.Camera);
            level.Session = savedLevel.Session.DeepCloneShared();
            level.FormationBackdrop.CopyAllSimpleTypeFieldsAndNull(savedLevel.FormationBackdrop);

            savedLevel.Bloom.DeepCloneToShared(level.Bloom);
            savedLevel.Background.DeepCloneToShared(level.Background);
            savedLevel.Foreground.DeepCloneToShared(level.Foreground);
            // savedLevel.HudRenderer.DeepCloneToShared(level.HudRenderer);
            // savedLevel.SubHudRenderer.DeepCloneToShared(level.SubHudRenderer);
            // savedLevel.Displacement.DeepCloneToShared(level.Displacement);

            // 不要 DeepClone level.Lighting 会造成光源偏移
            level.Lighting.CopyAllSimpleTypeFieldsAndNull(savedLevel.Lighting);

            // 里面有 camera 且没什么需要克隆还原的
            // level.GameplayRenderer.CopyAllSimpleTypeFieldsAndNull(savedLevel.GameplayRenderer);

            // level.Wipe = savedLevel.Wipe.DeepCloneShared();

            level.SetFieldValue("transition", savedLevel.GetFieldValue("transition").DeepCloneShared());
            level.SetFieldValue("skipCoroutine", savedLevel.GetFieldValue("skipCoroutine").DeepCloneShared());
            level.SetFieldValue("onCutsceneSkip", savedLevel.GetFieldValue("onCutsceneSkip").DeepCloneShared());
            // level.SetPropertyValue<Scene>("RendererList", savedLevel.RendererList.DeepCloneShared());

            level.CopyAllSimpleTypeFieldsAndNull(savedLevel);
        }

        // movePlayerToFirst = true: 调用游戏本身方法移除房间内 entities 时必须最早移除 Player，因为它关联着许多 Trigger
        // movePlayerToFirst = false: 克隆和恢复 entities 时必须严格按照相同的顺序，因为这会影响到 entity.Depth 从而影响到 entity.Update 的顺序
        private List<Entity> GetEntitiesExcludingGlobal(Level level, bool movePlayerToFirst) {
            List<Entity> result = level.Entities.Where(
                entity => !entity.TagCheck(Tags.Global) || IsRequireClonedGlobalEntity(entity)).ToList();

            if (movePlayerToFirst) {
                // Player 被 Remove 时会调用进入其中的 Trigger.OnLeave 方法，必须最早清除，不然会抛出异常
                // System.NullReferenceException: 未将对象引用设置到对象的实例。
                // 在 Celeste.CameraTargetTrigger.OnLeave(Player player)
                foreach (Entity player in level.Tracker.GetEntities<Player>()) {
                    result.Remove(player);
                    result.Insert(0, player);
                }
            }

            // renderer 一般是 Global 并且携带其它 entity 的引用，所以需要克隆并且放到最后才移除
            result.AddRange(level.Entities.Where(IsRequireClonedRenderer));

            return result;
        }

        public bool IsRequireClonedRenderer(Entity entity) {
            return entity.TagCheck(Tags.Global) && entity.GetType().FullName.EndsWith("Renderer");
        }

        public bool IsRequireClonedGlobalEntity(Entity entity) {
            return entity is CassetteBlockManager or SpeedrunTimerDisplay or TotalStrawberriesDisplay;
        }

        private List<Entity> GetEntitiesNeedDeepClone(Level level) {
            return GetEntitiesExcludingGlobal(level, false).Where(entity => {
                // 不恢复设置了 IgnoreSaveLoadComponent 的物体
                // SpeedrunTool 里有 ConfettiRenderer 和一些 MiniTextbox
                if (entity.IsIgnoreSaveLoad()) {
                    return false;
                }

                // 不恢复 CelesteNet 的物体
                // Do not restore CelesteNet Entity
                if (entity.GetType().FullName is { } name && name.StartsWith("Celeste.Mod.CelesteNet.")) {
                    return false;
                }

                return true;
            }).ToList();
        }

        private bool IsAllowSave(Level level, Player player) {
            return State == States.None && player is { Dead: false } && !level.PausedNew() && !level.SkippingCutscene;
        }

        // @formatter:off
        private static readonly Lazy<StateManager> Lazy = new(() => new StateManager());
        public static StateManager Instance => Lazy.Value;

        private StateManager() { }
        // @formatter:on

        class WaitSaveStateEntity : Entity {
            private static bool origFrozen;
            private static bool origTimerStopped;
            private static bool origPauseLock;
            private static float origTimeActive;
            private static float origRawTimeActive;

            public WaitSaveStateEntity(Level level) {
                // 避免被 Save
                Tag = Tags.Global;

                origFrozen = level.Frozen;
                origTimerStopped = level.TimerStopped;
                origPauseLock = level.PauseLock;
                origTimeActive = level.TimeActive;
                origRawTimeActive = level.RawTimeActive;

                level.Frozen = true;
                level.TimerStopped = true;
                level.PauseLock = true;
            }

            public override void Render() {
                // console load SpringCollab2020/3-Advanced/NeoKat
                // 很奇怪，如果在这时候预克隆，部分图读档时就会游戏崩溃例如春游 nyoom
                // System.ObjectDisposedException: Cannot access a disposed object.
                // Instance.PreCloneSavedEntities();
                Level level = SceneAs<Level>();
                level.DoScreenWipe(true, () => {
                    if (Settings.FreezeAfterLoadState) {
                        level.Frozen = true;
                        level.TimerStopped = true;
                        level.PauseLock = true;
                        Instance.State = States.Waiting;
                    } else {
                        OutOfWaiting(level);
                    }
                });
                RemoveSelf();
            }

            public static void OutOfWaiting(Level level) {
                level.Frozen = Instance.savedLevel?.Frozen ?? origFrozen;
                level.TimerStopped = Instance.savedLevel?.TimerStopped ?? origTimerStopped;
                level.PauseLock = Instance.savedLevel?.PauseLock ?? origPauseLock;
                level.TimeActive = Instance.savedLevel?.TimeActive ?? origTimeActive;
                level.RawTimeActive = Instance.savedLevel?.RawTimeActive ?? origRawTimeActive;
                EndPoint.All.ForEach(point => point.ReadyForTime());
                Instance.State = States.None;
            }
        }
    }
}