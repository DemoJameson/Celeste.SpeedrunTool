using System;
using System.Collections.Generic;
using System.Linq;
using Celeste.Mod.SpeedrunTool.Extensions;
using Celeste.Mod.SpeedrunTool.RoomTimer;
using Celeste.Mod.SpeedrunTool.SaveLoad.EntityIdPlus;
using Celeste.Mod.SpeedrunTool.SaveLoad.RestoreActions.Base;
using FMOD.Studio;
using Force.DeepCloner;
using Mono.Cecil.Cil;
using Monocle;
using MonoMod.Cil;
using static Celeste.Mod.SpeedrunTool.ButtonConfigUi;

namespace Celeste.Mod.SpeedrunTool.SaveLoad {
    public sealed class StateManager {
        private static SpeedrunToolSettings Settings => SpeedrunToolModule.Settings;
        private static bool FastLoadStateEnabled => Settings.FastLoadState;

        private Level savedLevel;

        public Dictionary<EntityId2, Entity> SavedEntitiesDict = new Dictionary<EntityId2, Entity>();
        public List<Entity> SavedDuplicateIdList = new List<Entity>();

        private LoadState loadState = SaveLoad.LoadState.None;

        private float savedFreezeTimer;
        private float savedTimeRate;
        private float savedGlitchValue;
        private float savedDistortAnxiety;
        private float savedDistortGameRate;

        private Dictionary<EverestModule, EverestModuleSession> savedModSessions;

        private int levelUpdateCounts = -1;

        private Session SavedSession => savedLevel?.Session;

        // ReSharper disable once MemberCanBePrivate.Global
        // Public for TAS
        public Player SavedPlayer;

        // ReSharper disable MemberCanBePrivate.Global
        public bool IsFastSaveSate => loadState == SaveLoad.LoadState.FastSaveState;
        public bool IsSaveSate => loadState == SaveLoad.LoadState.SaveState;
        public bool IsLoadStart => loadState == SaveLoad.LoadState.Start;
        public bool IsLoadFrozen => loadState == SaveLoad.LoadState.Frozen;
        public bool IsLoadPlayerRespawned => loadState == SaveLoad.LoadState.PlayerRespawned;

        public bool IsLoadComplete => loadState == SaveLoad.LoadState.Complete;
        // ReSharper restore MemberCanBePrivate.Global

        public bool IsSaved => SavedSession != null && SavedPlayer != null;

        private readonly List<int> disabledSaveStates = new List<int> {
            Player.StReflectionFall,
            Player.StTempleFall,
            Player.StCassetteFly,
            Player.StIntroJump,
            Player.StIntroWalk,
            Player.StIntroRespawn,
            Player.StIntroWakeUp,
            Player.StDummy,
        };

        #region Hook

        public void OnLoad() {
            On.Celeste.Level.Update += LevelOnUpdate;
            On.Celeste.Overworld.ctor += ClearStateAndPbTimes;
            AttachEntityId2Utils.OnLoad();
            RestoreEntityUtils.OnLoad();

            On.Celeste.Level.LoadLevel += LevelOnLoadLevel;
            On.Celeste.Level.UnloadLevel += LevelOnUnloadLevel;

            AutoLoadStateUtils.OnHook();

            On.Monocle.Scene.End += SceneOnEnd;
            On.Celeste.Player.SceneEnd += PlayerOnSceneEnd;
        }

        private void SceneOnEnd(On.Monocle.Scene.orig_End orig, Scene self) {
            orig(self);
            if (self is Level && IsSaveSate) {
                loadState = SaveLoad.LoadState.Start;
            }
        }

        // 避免 triggersInside 与 temp 被清空，从 Everest v1883 开始被清除了
        private void PlayerOnSceneEnd(On.Celeste.Player.orig_SceneEnd orig, Player self, Scene scene) {
            if (IsSaveSate || IsFastSaveSate) {
                Audio.Stop(self.GetField("conveyorLoopSfx") as EventInstance);
            } else {
                orig(self, scene);
            }
        }

        public void OnUnload() {
            On.Celeste.Level.Update -= LevelOnUpdate;
            On.Celeste.Overworld.ctor -= ClearStateAndPbTimes;
            AttachEntityId2Utils.Unload();
            RestoreEntityUtils.Unload();

            On.Celeste.Level.LoadLevel -= LevelOnLoadLevel;
            On.Celeste.Level.UnloadLevel -= LevelOnUnloadLevel;

            AutoLoadStateUtils.OnUnhook();
        }

        public void OnInit() {
            // reload map and enter debug map auto clear state
            Engine.Commands.FunctionKeyActions[4] += ClearState;
            Engine.Commands.FunctionKeyActions[5] += ClearState;
        }

        #endregion

        #region Fast Load State

        // TODO 现在是重写整个 UnloadLevel 方法，用 ILHook 修改 EntityList.UpdateLists 更简洁
        private void LevelOnUnloadLevel(On.Celeste.Level.orig_UnloadLevel orig, Level self) {
            if (IsSaved && IsFastSaveSate) {
                List<Entity> entitiesExcludingTagMask = self.GetEntitiesExcludingTagMask(Tags.Global);
                entitiesExcludingTagMask.AddRange(self.Tracker.GetEntities<Textbox>());

                // CassetteBlockManager 需要移除出 Level 来让它的状态不再改变
                if (self.Entities.FindFirst<CassetteBlockManager>() is Entity cassetteBlockManager) {
                    entitiesExcludingTagMask.Add(cassetteBlockManager);
                }

                List<Entity> entities = (List<Entity>) self.Entities.GetField("entities");
                HashSet<Entity> current = (HashSet<Entity>) self.Entities.GetField("current");
                foreach (Entity entity in entitiesExcludingTagMask) {
                    if (!entities.Contains(entity)) continue;
                    current.Remove(entity);
                    entities.Remove(entity);
                    if (entity.Components != null) {
                        foreach (Component component in entity.Components) {
                            component.EntityRemoved(self.Entities.Scene);
                        }
                    }

                    self.TagLists.InvokeMethod("EntityRemoved", entity);
                    self.Tracker.InvokeMethod("EntityRemoved", entity);
                    Engine.Pooler.InvokeMethod("EntityRemoved", entity);

                    // 执行 SceneEnd 一般是停止播放声音，不过有时也会造成一些问题，例如导致 TalkComponent 的字段 UI 变成 null
                    entity.SceneEnd(self);
                }

                loadState = SaveLoad.LoadState.Start;
                return;
            }

            // 读档执行 UnloadLevel 前需要先移除 Player 清空 Player 存着的 Trigger
            if (IsSaved && IsLoadStart) self.GetPlayer()?.Removed(self);

            orig(self);
        }

        // TODO 不懂，改完直接报错，现在是重写整个 UnloadLevel 方法
        // ReSharper disable once UnusedMember.Local
        private void EntityListOnUpdateLists(ILContext il) {
            ILCursor cursor = new ILCursor(il);

            if (cursor.TryGotoNext(
                MoveType.After,
                i => i.OpCode == OpCodes.Ldloc_3
                , i => i.OpCode == OpCodes.Ldarg_0
                , i => i.MatchCallvirt<EntityList>("get_Scene")
                , i => i.MatchCallvirt<Entity>("Removed")
            )) { }
        }

        private void LevelOnLoadLevel(On.Celeste.Level.orig_LoadLevel orig, Level self, Player.IntroTypes playerIntro,
            bool isFromLoader) {
            if (IsSaved && IsLoadStart && FastLoadStateEnabled && playerIntro == Player.IntroTypes.Respawn &&
                isFromLoader == false) {
                SavedSession.DeepCloneTo(self.Session);
            }

            orig(self, playerIntro, isFromLoader);
        }

        #endregion

        #region Core

        private void LevelOnUpdate(On.Celeste.Level.orig_Update orig, Level level) {
            if (!SpeedrunToolModule.Enabled) {
                orig(level);
                return;
            }

            Player player = level.Entities.FindFirst<Player>();

            if (CheckButton(level, player)) {
                return;
            }

            // 章节切换时清除保存的状态以及房间计时器自定终点
            // Clear the savestate and custom end point when switching chapters
            if (IsSaved && (SavedSession.Area.ID != level.Session.Area.ID ||
                            SavedSession.Area.Mode != level.Session.Area.Mode)) {
                ClearState();
                RoomTimerManager.Instance.ClearPbTimes();
            }

            // 尽快设置人物的位置与镜头，然后冻结游戏等待人物复活
            // Set player position ASAP, then freeze game and wait for the player to respawn (? - euni)
            if (IsSaved && IsLoadStart && player != null) {
                levelUpdateCounts++;

                if (levelUpdateCounts == 0) {
                    // 避免触发复活区域的 Trigger，例如 Glyph 的 peace-24
                    player.Collidable = player.Active = false;

                    RestoreLevel(level);
                    LoadStart(level);
                    orig(level);
                    return;
                }

                // 等待 Level.Update 多次使所有 Entity 更新绘完毕后后再冻结游戏
                // Wait for some frames so entities can be updated and rendered, then freeze game.
                if (levelUpdateCounts == 1) {
                    // 等待大部分 Entity 创建添加到 Scene 中再检查是否有保留了但是没有创建的 Entity
                    // 等待 Level.Update 一次是因为部分 Entity 是在第一次 Entity.Update 中创建的，例如 TalkComponentUI
                    RestoreEntityUtils.FindNotLoadedEntities(level);
                    orig(level);
                    return;
                }

                if (levelUpdateCounts == 2) {
                    // 预先还原位置与可见性，有些 Entity 需要 1 帧来渲染新的状态，例如 Spinner 的 border 和 MoveBlock 的销毁后不可见状态
                    // wait 1 frame let some entities render at new position. ex spinner's border and moveblock.
                    RestoreAllEntitiesPosition(level);
                    orig(level);

                    // Restore Again For Camera
                    RestoreLevel(level);

                    // 等所有 Entity 创建完毕并渲染完成后再统一在此时机还原状态
                    RestoreEntityUtils.AfterEntityAwake(level);

                    // 冻结游戏等待 Madeline 复活
                    // Freeze the game wait for madeline respawn.
                    if (player.StateMachine.State == Player.StIntroRespawn) {
                        level.Frozen = true;
                        level.PauseLock = true;
                        loadState = SaveLoad.LoadState.Frozen;

                        // sync for tas
                        for (int i = 0; i < 5; i++) {
                            (player.GetField("respawnTween") as Tween)?.Update();
                        }
                    } else {
                        loadState = SaveLoad.LoadState.PlayerRespawned;
                    }

                    return;
                }
            }

            // 冻结时允许人物 Update 以便复活
            // Allow player to respawn while level is frozen
            if (IsSaved && IsLoadFrozen) {
                UpdatePlayerWhenFreeze(level, player);
                level.Session.Time = SavedSession.Time;
            }

            // 人物复活完毕后设置人物相关属性
            // Set more player data after the player respawns
            if (IsSaved && (IsLoadPlayerRespawned || IsLoadFrozen) && player != null &&
                (player.StateMachine.State == Player.StNormal || player.StateMachine.State == Player.StSwim ||
                 player.StateMachine.State == Player.StFlingBird)) {
                RestoreEntityUtils.AfterPlayerRespawn(level);

                level.Frozen = false;
                level.PauseLock = false;
                level.TimeActive = savedLevel.TimeActive;
                level.RawTimeActive = savedLevel.RawTimeActive;
                level.Session.Time = SavedSession.Time;

                Engine.FreezeTimer = savedFreezeTimer;
                Engine.TimeRate = savedTimeRate;
                Glitch.Value = savedGlitchValue;
                Distort.Anxiety = savedDistortAnxiety;
                Distort.GameRate = savedDistortGameRate;

                loadState = SaveLoad.LoadState.Complete;

                RestoreEntityUtils.OnLoadComplete(level);
            }

            orig(level);
        }

        #endregion


        private void RestoreAllEntitiesPosition(Level level) {
            var loadedEntitiesDict = level.FindAllToDict<Entity>();

            foreach (var pair in loadedEntitiesDict.Where(loaded => SavedEntitiesDict.ContainsKey(loaded.Key))) {
                var savedEntity = SavedEntitiesDict[pair.Key];
                var loadedEntity = pair.Value;

                // let player stay at the safe position. player does not need to be pre-rendered.
                if (loadedEntity.IsType<Player>()) continue;

                loadedEntity.Position = savedEntity.Position;
                loadedEntity.Visible = savedEntity.Visible;
                loadedEntity.Collidable = savedEntity.Collidable;
            }
        }

        private void SaveState(Level level, Player player) {
            ClearState();

            levelUpdateCounts = -1;
            if (FastLoadStateEnabled) {
                loadState = SaveLoad.LoadState.FastSaveState;
            } else {
                loadState = SaveLoad.LoadState.SaveState;
            }

            SavedPlayer = player;
            SavedEntitiesDict = level.FindAllToDict(out SavedDuplicateIdList);

            if (FastLoadStateEnabled) {
                savedLevel = new Level {
                    Session = level.Session.DeepClone(),
                    Camera = level.Camera.DeepClone()
                };
                CopyCore.DeepCopyMembers(savedLevel, level, true);
            } else {
                savedLevel = level;
            }

            savedFreezeTimer = Engine.FreezeTimer;
            savedTimeRate = Engine.TimeRate;
            savedGlitchValue = Glitch.Value;
            savedDistortAnxiety = Distort.Anxiety;
            savedDistortGameRate = Distort.GameRate;

            // save all mod sessions
            savedModSessions = new Dictionary<EverestModule, EverestModuleSession>();
            foreach (EverestModule module in Everest.Modules) {
                if (module._Session != null) {
                    savedModSessions[module] = module._Session.DeepCloneYaml(module.SessionType);
                }
            }

            RestoreEntityUtils.OnSaveState(level);

            ReloadLevel(level);
        }

        public void LoadState() {
            if (!IsSaved || !(Engine.Scene.GetLevel() is Level level) || NotAllowFastLoadState(level)) {
                return;
            }

            levelUpdateCounts = -1;
            loadState = SaveLoad.LoadState.Start;
            ReloadLevel(level);

            // restore all mod sessions
            foreach (EverestModule module in Everest.Modules) {
                if (savedModSessions.TryGetValue(module, out EverestModuleSession savedModSession)) {
                    module._Session = savedModSession.DeepCloneYaml(module.SessionType);
                }
            }
        }

        private void ReloadLevel(Level level) {
            RoomTimerManager.Instance.ResetTime();
            if (FastLoadStateEnabled) {
                // 允许切换房间时读档
                level.SetField("transition", null);
                // TODO 保存后光线亮度问题
                // TODO 第九章雷电样式问题
                // TODO 初始房间保存后读档，冲刺线条缓慢移动的问题
                level.Completed = false;
                level.Displacement.Clear();
                level.Reload();
            } else {
                Session deepCloneSession = SavedSession.DeepClone();
                Engine.Scene = new LevelLoader(deepCloneSession, deepCloneSession.RespawnPoint);
            }
        }

        // ReSharper disable once UnusedMember.Global
        // Public for TAS Mod
        public bool ExternalSave() {
            Level level = Engine.Scene as Level;
            Player player = level?.Entities.FindFirst<Player>();
            if (player == null)
                return false;

            if (IsAllowSave(level, player)) {
                SaveState(level, player);
                return true;
            }

            return false;
        }

        // ReSharper disable once UnusedMember.Global
        // Public for TAS Mod
        public bool ExternalLoad() {
            LoadState();
            return IsSaved;
        }

        // 尽快设置人物的位置与镜头，然后冻结游戏等待人物复活
        // Set player position ASAP, then freeze game and wait for the player to respawn (? - euni)
        private void LoadStart(Level level) {
            RestoreEntityUtils.OnLoadStart(level);
        }

        private void RestoreLevel(Level level) {
            level.Session.Inventory = SavedSession.Inventory;
            CopyCore.DeepCopyMembers(level, savedLevel, true);
            level.Camera.CopyFrom(savedLevel.Camera);
            WindController windController = level.Entities.FindFirst<WindController>();
            WindController savedWindController =
                (WindController) SavedEntitiesDict.FirstOrDefault(pair => pair.Value is WindController).Value;
            windController.CopyFields(savedWindController, "pattern");
        }

        // ReSharper disable once MemberCanBeMadeStatic.Local
        private void UpdatePlayerWhenFreeze(Level level, Player player) {
            if (player == null) {
                level.Frozen = false;
                level.PauseLock = false;
            } else if (player.StateMachine.State != Player.StNormal) {
                // Don't call player.update, it will trigger playerCollider
                // 不要使用 player.update 会触发其他 Entity 的 playerCollider
                // 例如保存时与 Spring 过近，恢复时会被弹起。
                (player.GetField("respawnTween") as Tween)?.Update();

                level.Background.Update(level);
                level.Foreground.Update(level);
            }
        }

        public void ClearState() {
            if (Engine.Scene is Level level && IsNotCollectingHeart(level)) {
                level.Frozen = false;
                level.PauseLock = false;
            }

            savedModSessions = null;
            savedLevel = null;
            SavedPlayer = null;
            SavedEntitiesDict.Clear();
            SavedDuplicateIdList.Clear();
            loadState = SaveLoad.LoadState.None;
            levelUpdateCounts = -1;

            RestoreEntityUtils.OnClearState();
        }

        private bool IsAllowSave(Level level, Player player) {
            return !level.Paused && !level.Transitioning && !level.PauseLock && !level.InCutscene &&
                   !level.SkippingCutscene && player != null && !player.Dead &&
                   !disabledSaveStates.Contains(player.StateMachine.State) && IsNotCollectingHeart(level);
        }

        private bool IsNotCollectingHeart(Level level) {
            return !level.Entities.FindAll<HeartGem>().Any(heart => (bool) heart.GetField("collected"));
        }

        private bool NotAllowFastLoadState(Level level) {
            if (!FastLoadStateEnabled) return false;
            return level.Paused;
        }

        private void ClearStateAndPbTimes(On.Celeste.Overworld.orig_ctor orig, Overworld self, OverworldLoader loader) {
            orig(self, loader);
            ClearState();
            RoomTimerManager.Instance.ClearPbTimes();
        }

        private bool CheckButton(Level level, Player player) {
            if (GetVirtualButton(Mappings.Save).Pressed && IsAllowSave(level, player)) {
                GetVirtualButton(Mappings.Save).ConsumePress();
                SaveState(level, player);
                return true;
            }

            if (GetVirtualButton(Mappings.Load).Pressed && !level.Paused && !IsLoadFrozen) {
                GetVirtualButton(Mappings.Load).ConsumePress();
                if (IsSaved) {
                    LoadState();
                } else if (!level.Frozen) {
                    level.Add(new MiniTextbox(DialogIds.DialogNotSaved));
                }

                return true;
            }

            if (GetVirtualButton(Mappings.Clear).Pressed && !level.Paused) {
                GetVirtualButton(Mappings.Clear).ConsumePress();
                ClearState();
                RoomTimerManager.Instance.ClearPbTimes();
                if (IsNotCollectingHeart(level)) {
                    level.Add(new MiniTextbox(DialogIds.DialogClear));
                }

                return false;
            }

            if (GetVirtualButton(Mappings.SwitchAutoLoadState).Pressed && !level.Paused) {
                GetVirtualButton(Mappings.SwitchAutoLoadState).ConsumePress();
                Settings.AutoLoadAfterDeath = !Settings.AutoLoadAfterDeath;
                SpeedrunToolModule.Instance.SaveSettings();
                return false;
            }

            return false;
        }

        // @formatter:off
        private static readonly Lazy<StateManager> Lazy = new Lazy<StateManager>(() => new StateManager());
        public static StateManager Instance => Lazy.Value;
        private StateManager() { }
        // @formatter:on
    }

    internal enum LoadState {
        None,
        FastSaveState,
        SaveState,
        Start,
        Frozen,
        PlayerRespawned,
        Complete
    }
}