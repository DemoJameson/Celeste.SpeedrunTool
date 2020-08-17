using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Celeste.Mod.SpeedrunTool.DeathStatistics;
using Celeste.Mod.SpeedrunTool.Extensions;
using Celeste.Mod.SpeedrunTool.RoomTimer;
using FMOD.Studio;
using Force.DeepCloner;
using Force.DeepCloner.Helpers;
using Microsoft.Xna.Framework.Input;
using Monocle;
using MonoMod.Utils;
using static Celeste.Mod.SpeedrunTool.ButtonConfigUi;

namespace Celeste.Mod.SpeedrunTool.SaveLoad {
    public sealed class StateManager {
        private static SpeedrunToolSettings Settings => SpeedrunToolModule.Settings;

        private static readonly List<int> DisabledSaveStates = new List<int> {
            Player.StReflectionFall,
            Player.StTempleFall,
            Player.StCassetteFly,
            Player.StIntroJump,
            Player.StIntroWalk,
            Player.StIntroRespawn,
            Player.StIntroWakeUp,
        };

        private bool IsSaved => savedLevel != null;

        private Level savedLevel;
        private List<Entity> savedEntities;

        private float savedFreezeTimer;
        private float savedTimeRate;
        private float savedGlitchValue;
        private float savedDistortAnxiety;
        private float savedDistortGameRate;

        private Dictionary<EverestModule, EverestModuleSession> savedModSessions;

        private States state = States.None;

        // 共用 DeepCloneState 可使多次 DeepClone 复用想同对象避免多次克隆同一对象
        public DeepCloneState SharedCloneState { get; private set; }

        private enum States {
            None,
            Loading,
        }

        private readonly HashSet<EventInstance> playingEventInstances = new HashSet<EventInstance>();

        #region Hook

        public void OnLoad() {
            DeepCloneUtils.Config();
            SaveLoadAction.OnLoad();
            EventInstanceUtils.OnHook();
            On.Celeste.Level.Update += CheckButtonsOnLevelUpdate;
            On.Monocle.Scene.Begin += ClearStateWhenSwitchScene;
            On.Celeste.PlayerDeadBody.End += AutoLoadStateWhenDeath;
        }

        public void OnUnload() {
            DeepCloneUtils.Clear();
            SaveLoadAction.OnUnload();
            EventInstanceUtils.OnUnhook();
            On.Celeste.Level.Update -= CheckButtonsOnLevelUpdate;
            On.Monocle.Scene.Begin -= ClearStateWhenSwitchScene;
            On.Celeste.PlayerDeadBody.End -= AutoLoadStateWhenDeath;
        }

        private void CheckButtonsOnLevelUpdate(On.Celeste.Level.orig_Update orig, Level self) {
            orig(self);
            CheckButton(self);
        }

        private void ClearStateWhenSwitchScene(On.Monocle.Scene.orig_Begin orig, Scene self) {
            orig(self);
            if (self is Overworld) ClearState();
            if (IsSaved) {
                if (self is Level) state = States.None; // 修复：读档途中按下 PageDown/Up 后无法存档
                if (self.GetSession() is Session session && session.Area != savedLevel.Session.Area) {
                    ClearState();
                }
            }
        }

        private void AutoLoadStateWhenDeath(On.Celeste.PlayerDeadBody.orig_End orig, PlayerDeadBody self) {
            if (SpeedrunToolModule.Settings.Enabled
                && SpeedrunToolModule.Settings.AutoLoadAfterDeath
                && IsSaved
                && !(bool) self.GetFieldValue("finished")
                && Engine.Scene is Level level
            ) {
                level.OnEndOfFrame += () => LoadState();
                self.RemoveSelf();
            } else {
                orig(self);
            }
        }

        #endregion

        // public for TAS Mod
        // ReSharper disable once MemberCanBePrivate.Global UnusedMethodReturnValue.Global
        public bool SaveState() {
            if (!(Engine.Scene is Level level)) return false;
            if (!IsAllowSave(level, level.GetPlayer())) return false;

            ClearState(false);

            SharedCloneState = new DeepCloneState();

            savedLevel = level.ShallowClone();
            savedLevel.Lighting = level.Lighting.ShallowClone();
            savedLevel.Session = level.Session.DeepClone(SharedCloneState);
            savedLevel.Camera = level.Camera.DeepClone(SharedCloneState);
            savedLevel.Bloom = level.Bloom.DeepClone(SharedCloneState);
            savedLevel.Background = level.Background.DeepClone(SharedCloneState);
            savedLevel.Foreground = level.Foreground.DeepClone(SharedCloneState);

            savedEntities = DeepCloneEntities(GetEntitiesNeedDeepClone(level));

            // External
            savedFreezeTimer = Engine.FreezeTimer;
            savedTimeRate = Engine.TimeRate;
            savedGlitchValue = Glitch.Value;
            savedDistortAnxiety = Distort.Anxiety;
            savedDistortGameRate = Distort.GameRate;

            // Mod
            SaveLoadAction.OnSaveState(level);

            // save all mod sessions
            savedModSessions = new Dictionary<EverestModule, EverestModuleSession>();
            foreach (EverestModule module in Everest.Modules) {
                if (module._Session != null) {
                    savedModSessions[module] = module._Session.DeepCloneYaml(module.SessionType);
                }
            }

            return LoadState();
        }

        // public for TAS Mod
        // ReSharper disable once MemberCanBePrivate.Global
        public bool LoadState() {
            if (!(Engine.Scene is Level level)) return false;
            if (level.Paused || state != States.None || !IsSaved) return false;

            state = States.Loading;

            SharedCloneState = new DeepCloneState();

            // External
            RoomTimerManager.Instance.ResetTime();
            DeathStatisticsManager.Instance.Died = false;

            // Mod
            SaveLoadAction.OnLoadState(level, savedEntities);

            level.SetFieldValue("transition", null); // 允许切换房间时读档  // Allow reading fields when switching rooms
            level.Displacement.Clear(); // 避免冲刺后读档残留爆破效果  // Remove dash displacement effect
            level.ParticlesBG.Clear();
            level.Particles.Clear();
            level.ParticlesFG.Clear();
            TrailManager.Clear(); // 清除冲刺的残影  // Remove dash trail

            UnloadLevelEntities(level);
            RestoreLevelEntities(level);
            RestoreCassetteBlockManager1(level); // 停止播放主音乐，等待播放节奏音乐
            RestoreLevel(level);

            // restore all mod sessions
            foreach (EverestModule module in Everest.Modules) {
                if (savedModSessions.TryGetValue(module, out EverestModuleSession savedModSession)) {
                    module._Session = savedModSession.DeepCloneYaml(module.SessionType);
                }
            }

            // 修复问题：未打开自动读档时，死掉按下确认键后读档完成会接着执行 Reload 复活方法
            // Fix: When AutoLoadStateAfterDeath is off, if manually LoadState() after death, level.Reload() will still be executed.
            if (level.RendererList.Renderers.FirstOrDefault(renderer => renderer is ScreenWipe) is ScreenWipe wipe) {
                wipe.Cancel();
            }

            level.Frozen = true; // 加一个转场等待，避免太突兀   // Add a pause to avoid being too abrupt
            level.TimerStopped = true; // 停止计时器  // Stop timer

            level.DoScreenWipe(true, () => {
                RestoreLevel(level);
                RestoreCassetteBlockManager2(level);
                RoomTimerManager.Instance.SavedEndPoint?.ReadyForTime();
                foreach (EventInstance instance in playingEventInstances) instance.start();
                playingEventInstances.Clear();

                state = States.None;
            });

            return true;
        }

        private void RestoreCassetteBlockManager1(Level level) {
            if (level.Entities.FindFirst<CassetteBlockManager>() is CassetteBlockManager manager) {
                if (manager.GetFieldValue("snapshot") is EventInstance snapshot) {
                    snapshot.start();
                }
            }
        }

        private void RestoreCassetteBlockManager2(Level level) {
            if (level.Entities.FindFirst<CassetteBlockManager>() is CassetteBlockManager manager) {
                if (manager.GetFieldValue("sfx") is EventInstance sfx &&
                    !(bool) manager.GetFieldValue("isLevelMusic")) {
                    if ((int) manager.GetFieldValue("leadBeats") <= 0) {
                        sfx.start();
                    }
                }
            }
        }

        private void ClearState(bool clearEndPoint = true) {
            if (Engine.Scene is Level level && IsNotCollectingHeart(level)) {
                level.Frozen = false;
                level.PauseLock = false;
            }

            try {
                RoomTimerManager.Instance.ClearPbTimes(clearEndPoint);
            } catch (NullReferenceException) {
                // Don't know why it happened
                // SpeedrunToolModule.Instance = null Exception
            }

            playingEventInstances.Clear();

            savedModSessions = null;
            savedLevel = null;
            savedEntities = null;

            SharedCloneState = null;

            // Mod
            SaveLoadAction.OnClearState();

            state = States.None;
        }

        private List<Entity> DeepCloneEntities(List<Entity> entities) {
            List<Entity> clonedEntities = entities.DeepClone(SharedCloneState);

            // Find the dynData.Data that need to be cloned
            for (int i = 0; i < entities.Count; i++) {
                Entity entity = entities[i];
                if (DynDataUtils.GetDataMap(entity.GetType())?.Count == 0) continue;

                if (DynDataUtils.GetDate(entity) is Dictionary<string, object> data && data.Count > 0) {
                    Entity clonedEntity = clonedEntities[i];
                    if (DynDataUtils.GetDate(clonedEntity) is Dictionary<string, object> needClonedData) {
                        data.DeepCloneTo(needClonedData, SharedCloneState);
                    }
                }
            }

            return clonedEntities;
        }

        private void UnloadLevelEntities(Level level) {
            List<Entity> entities = GetEntitiesExcludingGlobal(level);
            level.Remove(entities);
            level.Entities.UpdateLists();
            // 修复：Retry 后读档依然执行 PlayerDeadBody.End 的问题
            // 由 level.CopyAllSimpleTypeFields(savedLevel) 自动处理了
            // level.RetryPlayerCorpse = null;
        }

        private void RestoreLevelEntities(Level level) {
            List<Entity> deepCloneEntities = DeepCloneEntities(savedEntities);

            // just follow the level.LoadLevel add player last. There must some black magic in it.
            // fixed: Player can't perform a really spike jump when wind blow up.
            deepCloneEntities.Reverse();

            // Re Add Entities
            List<Entity> entities = (List<Entity>) level.Entities.GetFieldValue("entities");
            HashSet<Entity> current = (HashSet<Entity>) level.Entities.GetFieldValue("current");
            foreach (Entity entity in deepCloneEntities) {
                if (entities.Contains(entity)) continue;

                current.Add(entity);
                entities.Add(entity);

                level.TagLists.InvokeMethod("EntityAdded", entity);
                level.Tracker.InvokeMethod("EntityAdded", entity);
                entity.Components?.ToList()
                    .ForEach(component => {
                        level.Tracker.InvokeMethod("ComponentAdded", component);

                        // 等 ScreenWipe 完毕再重新播放
                        if (component is SoundSource source && source.Playing &&
                            source.GetFieldValue("instance") is EventInstance eventInstance) {
                            playingEventInstances.Add(eventInstance);
                        }
                    });
                level.InvokeMethod("SetActualDepth", entity);
                Dictionary<Type, Queue<Entity>> pools =
                    (Dictionary<Type, Queue<Entity>>) Engine.Pooler.GetPropertyValue("Pools");
                Type type = entity.GetType();
                if (pools.ContainsKey(type) && pools[type].Count > 0) {
                    pools[type].Dequeue();
                }
            }
        }

        private void RestoreLevel(Level level) {
            level.Camera.CopyFrom(savedLevel.Camera);
            savedLevel.Session.DeepCloneTo(level.Session, SharedCloneState);
            savedLevel.Bloom.DeepCloneTo(level.Bloom, SharedCloneState);
            savedLevel.Background.DeepCloneTo(level.Background, SharedCloneState);
            savedLevel.Foreground.DeepCloneTo(level.Foreground, SharedCloneState);
            level.CopyAllSimpleTypeFieldsAndNull(savedLevel);
            level.Lighting.CopyAllSimpleTypeFieldsAndNull(savedLevel.Lighting);

            // External Static Field
            Engine.FreezeTimer = savedFreezeTimer;
            Engine.TimeRate = savedTimeRate;
            Glitch.Value = savedGlitchValue;
            Distort.Anxiety = savedDistortAnxiety;
            Distort.GameRate = savedDistortGameRate;
        }

        private List<Entity> GetEntitiesExcludingGlobal(Level level) {
            var result = level.Entities.Where(
                entity => !entity.TagCheck(Tags.Global) || entity is CassetteBlockManager).ToList();

            if (level.GetPlayer() is Player player) {
                // Player 被 Remove 时会触发其他 Trigger，所以必须最早清除
                result.Remove(player);
                result.Insert(0, player);
            }

            // 存储的 Entity 被清除时会调用 Renderer，所以 Renderer 应该放到最后
            if (level.Entities.FindFirst<SeekerBarrierRenderer>() is Entity seekerBarrierRenderer) {
                result.Add(seekerBarrierRenderer);
            }

            if (level.Entities.FindFirst<LightningRenderer>() is Entity lightningRenderer) {
                result.Add(lightningRenderer);
            }

            if (level.Entities.FirstOrDefault(entity =>
                    entity.GetType().FullName == "Celeste.Mod.AcidHelper.Entities.InstantTeleporterRenderer") is Entity
                teleporterRenderer) {
                result.Add(teleporterRenderer);
            }

            return result;
        }

        private List<Entity> GetEntitiesNeedDeepClone(Level level) {
            return GetEntitiesExcludingGlobal(level).Where(entity => {
                // 不恢复设置了 IgnoreSaveLoadComponent 的物体
                // SpeedrunTool 里有 ConfettiRenderer 和一些 MiniTextbox
                if (entity.IsIgnoreSaveLoad()) return false;

                // 不恢复 CelesteNet/CelesteTAS 的物体
                // Do not restore CelesteNet/CelesteTAS objects
                if (entity.GetType().FullName is string name &&
                    (name.StartsWith("Celeste.Mod.CelesteNet.") || name.StartsWith("TAS.")))
                    return false;

                return true;
            }).ToList();
        }

        private bool IsAllowSave(Level level, Player player) {
            return state == States.None
                   && !level.Paused && !level.Transitioning && !level.InCutscene && !level.SkippingCutscene
                   && player != null && !player.Dead && !DisabledSaveStates.Contains(player.StateMachine.State)
                   && IsNotCollectingHeart(level);
        }

        private bool IsNotCollectingHeart(Level level) {
            return !level.Entities.FindAll<HeartGem>().Any(heart => (bool) heart.GetFieldValue("collected"));
        }

        private void CheckButton(Level level) {
            if (!SpeedrunToolModule.Enabled) return;

            if (GetVirtualButton(Mappings.Save).Pressed) {
                GetVirtualButton(Mappings.Save).ConsumePress();
                SaveState();
            } else if (GetVirtualButton(Mappings.Load).Pressed && !level.Paused && state == States.None) {
                GetVirtualButton(Mappings.Load).ConsumePress();
                if (IsSaved) {
                    LoadState();
                } else if (!level.Frozen) {
                    level.Add(new MiniTextbox(DialogIds.DialogNotSaved).IgnoreSaveLoad());
                }
            } else if (GetVirtualButton(Mappings.Clear).Pressed && !level.Paused) {
                GetVirtualButton(Mappings.Clear).ConsumePress();
                ClearState();
                if (IsNotCollectingHeart(level)) {
                    level.Add(new MiniTextbox(DialogIds.DialogClear).IgnoreSaveLoad());
                }
            } else if (MInput.Keyboard.Check(Keys.F5)) {
                ClearState();
            } else if (GetVirtualButton(Mappings.SwitchAutoLoadState).Pressed && !level.Paused) {
                GetVirtualButton(Mappings.SwitchAutoLoadState).ConsumePress();
                Settings.AutoLoadAfterDeath = !Settings.AutoLoadAfterDeath;
                SpeedrunToolModule.Instance.SaveSettings();
            }
        }


        // @formatter:off
        private static readonly Lazy<StateManager> Lazy = new Lazy<StateManager>(() => new StateManager());
        public static StateManager Instance => Lazy.Value;
        private StateManager() { }
        // @formatter:on
    }

    internal static class DynDataUtils {
        private static object CreateDynData(object obj) {
            Type type = obj.GetType();
            string key = $"DynDataUtils-CreateDynData-{type.FullName}";

            ConstructorInfo constructorInfo = type.GetExtendedDataValue<ConstructorInfo>(key);

            if (constructorInfo == null) {
                constructorInfo = typeof(DynData<>).MakeGenericType(type).GetConstructor(new[] {type});
                type.SetExtendedDataValue(key, constructorInfo);
            }

            return constructorInfo?.Invoke(new[] {obj});
        }

        public static IDictionary GetDataMap(Type type) {
            string key = $"DynDataUtils-GetDataMap-{type}";

            FieldInfo fieldInfo = type.GetExtendedDataValue<FieldInfo>(key);

            if (fieldInfo == null) {
                fieldInfo = typeof(DynData<>).MakeGenericType(type)
                    .GetField("_DataMap", BindingFlags.Static | BindingFlags.NonPublic);
                type.SetExtendedDataValue(key, fieldInfo);
            }

            return fieldInfo?.GetValue(null) as IDictionary;
        }

        public static Dictionary<string, object> GetDate(object obj) {
            return CreateDynData(obj)?.GetPropertyValue("Data") as Dictionary<string, object>;
        }
    }
}