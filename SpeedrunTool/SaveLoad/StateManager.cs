using System;
using System.Collections.Generic;
using System.Linq;
using Celeste.Mod.SpeedrunTool.Extensions;
using Celeste.Mod.SpeedrunTool.RoomTimer;
using Celeste.Mod.SpeedrunTool.SaveLoad.EntityIdPlus;
using Celeste.Mod.SpeedrunTool.SaveLoad.RestoreActions.Base;
using Microsoft.Xna.Framework;
using Monocle;
using static Celeste.Mod.SpeedrunTool.ButtonConfigUi;

namespace Celeste.Mod.SpeedrunTool.SaveLoad {
    public sealed class StateManager {
        // ReSharper disable once MemberCanBePrivate.Global
        // Public for TAS
        public Player SavedPlayer;
        public Level SavedLevel;

        public Dictionary<EntityId2, Entity> SavedEntitiesDict = new Dictionary<EntityId2, Entity>();
        public List<Entity> SavedDuplicateIdList = new List<Entity>();

        private LoadState loadState = SaveLoad.LoadState.None;

        private float savedFreezeTimer;
        private float savedTimeRate;

        private Session savedSession;
        private Dictionary<EverestModule, EverestModuleSession> savedModSessions;

        public bool IsLoadStart => loadState == SaveLoad.LoadState.Start;
        public bool IsLoadFrozen => loadState == SaveLoad.LoadState.Frozen;
        public bool IsPlayerRespawned => loadState == SaveLoad.LoadState.PlayerRespawned;
        public bool IsLoadComplete => loadState == SaveLoad.LoadState.Complete;

        public bool IsSaved => savedSession != null && SavedPlayer != null;

        private PlayerDeadBody currentPlayerDeadBody;

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

        public void OnLoad() {
            On.Celeste.AreaData.DoScreenWipe += QuickLoadWhenDeath;
            On.Celeste.Level.Update += LevelOnUpdate;
            On.Celeste.Overworld.ctor += ClearStateAndPbTimes;
            On.Celeste.Player.Die += PlayerOnDie;
            AttachEntityId2Utils.OnLoad();
            RestoreEntityUtils.OnLoad();
        }

        public void OnUnload() {
            On.Celeste.AreaData.DoScreenWipe -= QuickLoadWhenDeath;
            On.Celeste.Level.Update -= LevelOnUpdate;
            On.Celeste.Overworld.ctor -= ClearStateAndPbTimes;
            On.Celeste.Player.Die -= PlayerOnDie;
            AttachEntityId2Utils.Unload();
            RestoreEntityUtils.Unload();
        }

        public void OnInit() {
            // enter debug map auto clear state
            Engine.Commands.FunctionKeyActions[5] += ClearState;
        }

        private void ClearStateAndPbTimes(On.Celeste.Overworld.orig_ctor orig, Overworld self, OverworldLoader loader) {
            orig(self, loader);
            ClearState();
            RoomTimerManager.Instance.ClearPbTimes();
        }

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
            if (IsSaved && (savedSession.Area.ID != level.Session.Area.ID ||
                            savedSession.Area.Mode != level.Session.Area.Mode)) {
                ClearState();
                RoomTimerManager.Instance.ClearPbTimes();
            }

            // 尽快设置人物的位置与镜头，然后冻结游戏等待人物复活
            // Set player position ASAP, then freeze game and wait for the player to respawn (? - euni)
            if (IsSaved && IsLoadStart && player != null) {
                RestoreLevel(level);

                LoadStart(level);

                // 调用 Level.Update 多次使所有 Entity 更新绘完毕后后再冻结游戏
                // Wait for some frames so entities can be updated and rendered, then freeze game.
                for (int i = 0; i < 2; i++) orig(level);

                // 预先还原位置与可见性，有些 Entity 需要 1 帧来渲染新的状态，例如 Spinner 的 border 和 MoveBlock 的销毁后不可见状态
                RestoreAllEntitiesPosition(level);

                // wait 1 frame let entities render at new position.
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
                } else {
                    loadState = SaveLoad.LoadState.PlayerRespawned;
                }

                return;
            }

            // 冻结时允许人物 Update 以便复活
            // Allow player to respawn while level is frozen
            if (IsSaved && IsLoadFrozen) {
                UpdatePlayerWhenFreeze(level, player);
                level.Session.Time = savedSession.Time;
            }

            // 人物复活完毕后设置人物相关属性
            // Set more player data after the player respawns
            if (IsSaved && (IsPlayerRespawned || IsLoadFrozen) && player != null &&
                (player.StateMachine.State == Player.StNormal || player.StateMachine.State == Player.StSwim ||
                 player.StateMachine.State == Player.StFlingBird)) {
                RestoreEntityUtils.AfterPlayerRespawn(level);

                level.Frozen = false;
                level.PauseLock = false;
                level.TimeActive = SavedLevel.TimeActive;
                level.RawTimeActive = SavedLevel.RawTimeActive;
                level.Session.Time = savedSession.Time;
                Engine.FreezeTimer = savedFreezeTimer;
                Engine.TimeRate = savedTimeRate;
                loadState = SaveLoad.LoadState.Complete;

                RestoreEntityUtils.OnLoadComplete(level);
            }

            orig(level);
        }

        private void RestoreAllEntitiesPosition(Level level) {
            var loadedEntitiesDict = level.FindAllToDict<Entity>();

            foreach (var pair in loadedEntitiesDict.Where(loaded => SavedEntitiesDict.ContainsKey(loaded.Key))) {
                var savedEntity = SavedEntitiesDict[pair.Key];
                var loadedEntity = pair.Value;
                loadedEntity.Position = savedEntity.Position;
                loadedEntity.Visible = savedEntity.Visible;
                loadedEntity.Collidable = false; // 避免 orig(level) 时死亡，AfterEntitiesAwake 时会设置正确的值
            }
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

            return false;
        }

        private bool IsAllowSave(Level level, Player player) {
            return !level.Paused && !level.Transitioning && !level.PauseLock && !level.InCutscene &&
                   !level.SkippingCutscene && player != null && !player.Dead &&
                   !disabledSaveStates.Contains(player.StateMachine.State) && IsNotCollectingHeart(level);
        }

        private bool IsNotCollectingHeart(Level level) {
            return !level.Entities.FindAll<HeartGem>().Any(heart => (bool) heart.GetField("collected"));
        }

        private void SaveState(Level level, Player player) {
            ClearState();

            loadState = SaveLoad.LoadState.Start;

            savedSession = level.Session.DeepClone();
            SavedPlayer = player;
            SavedLevel = level;
            SavedEntitiesDict = level.FindAllToDict(out SavedDuplicateIdList);
            savedFreezeTimer = Engine.FreezeTimer;
            savedTimeRate = Engine.TimeRate;

            // save all mod sessions
            savedModSessions = new Dictionary<EverestModule, EverestModuleSession>();
            foreach (EverestModule module in Everest.Modules) {
                if (module._Session != null) {
                    savedModSessions[module] = module._Session.DeepCloneYaml<EverestModuleSession>(module.SessionType);
                }
            }

            RestoreEntityUtils.OnSaveState(level);

            Engine.Scene = new LevelLoader(level.Session, level.Session.RespawnPoint);
        }

        private void LoadState() {
            if (!IsSaved) {
                return;
            }

            loadState = SaveLoad.LoadState.Start;
            Session sessionCopy = savedSession.DeepClone();
            Engine.Scene = new LevelLoader(sessionCopy, sessionCopy.RespawnPoint);

            // restore all mod sessions
            foreach (EverestModule module in Everest.Modules) {
                if (savedModSessions.TryGetValue(module, out EverestModuleSession savedModSession)) {
                    module._Session = savedModSession.DeepCloneYaml<EverestModuleSession>(module.SessionType);
                }
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
            level.Session.Inventory = savedSession.Inventory;
            level.CopyAllFrom(SavedLevel, true);
            level.Camera.CopyFrom(SavedLevel.Camera);
            WindController windController = level.Entities.FindFirst<WindController>();
            WindController savedWindController = Instance.SavedLevel.Entities.FindFirst<WindController>();
            windController.CopyFields(savedWindController, "pattern");
        }

        // ReSharper disable once MemberCanBeMadeStatic.Local
        private void UpdatePlayerWhenFreeze(Level level, Player player) {
            if (player == null) {
                level.Frozen = false;
            } else if (player.StateMachine.State != Player.StNormal) {
                // Don't call player.update, it will trigger playerCollider
                // 不要使用 player.update 会触发其他 Entity 的 playerCollider
                // 例如保存时与 Spring 过近，恢复时会被弹起。
                player.Components.InvokeMethod("Update");
                
                // 某些情况会被墙壁弹开？
                // player.Position = SavedPlayer.Position;
                level.Background.Update(level);
                level.Foreground.Update(level);
            }
        }

        private void ClearState() {
            if (Engine.Scene is Level level && IsNotCollectingHeart(level)) {
                level.Frozen = false;
            }

            savedSession = null;
            savedModSessions = null;
            SavedPlayer = null;
            SavedLevel = null;
            SavedEntitiesDict.Clear();
            SavedDuplicateIdList.Clear();
            loadState = SaveLoad.LoadState.None;

            RestoreEntityUtils.OnClearState();
        }


        private PlayerDeadBody PlayerOnDie(On.Celeste.Player.orig_Die orig, Player self, Vector2 direction,
            bool evenIfInvincible, bool registerDeathInStats) {
            currentPlayerDeadBody = orig(self, direction, evenIfInvincible, registerDeathInStats);
            return currentPlayerDeadBody;
        }

        // Everest 的 Bug，另外的 Mod Hook 了 PlayerDeadBody.End 方法后 Level.DoScreenWipe Hook 的方法 wipeIn 为 false 时就不触发了
        // 所以改成了 Hook AreaData.DoScreenWipe 方法
        private void QuickLoadWhenDeath(On.Celeste.AreaData.orig_DoScreenWipe orig, AreaData self, Scene scene,
            bool wipeIn, Action onComplete) {
            if (SpeedrunToolModule.Settings.Enabled && SpeedrunToolModule.Settings.AutoLoadAfterDeath && IsSaved &&
                !wipeIn && scene is Level level &&
                onComplete != null && (onComplete == level.Reload || currentPlayerDeadBody?.HasGolden == true)) {
                Action complete = onComplete;
                currentPlayerDeadBody = null;
                onComplete = () => {
                    if (IsSaved) {
                        LoadState();
                    } else {
                        complete();
                    }
                };
            }

            orig(self, scene, wipeIn, onComplete);
        }

        // @formatter:off
        private static readonly Lazy<StateManager> Lazy = new Lazy<StateManager>(() => new StateManager());
        public static StateManager Instance => Lazy.Value;
        private StateManager() { }
        // @formatter:on
    }

    public enum LoadState {
        None,
        Start,
        Frozen,
        PlayerRespawned,
        Complete
    }
}