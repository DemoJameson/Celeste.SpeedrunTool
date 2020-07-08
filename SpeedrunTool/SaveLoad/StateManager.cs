using System;
using System.Collections.Generic;
using System.Linq;
using Celeste.Mod.SpeedrunTool.Extensions;
using Celeste.Mod.SpeedrunTool.RoomTimer;
using Celeste.Mod.SpeedrunTool.SaveLoad.Actions;
using Celeste.Mod.SpeedrunTool.SaveLoad.EntityIdPlus;
using Celeste.Mod.SpeedrunTool.SaveLoad.RestoreActions;
using Microsoft.Xna.Framework;
using Monocle;
using static Celeste.Mod.SpeedrunTool.ButtonConfigUi;

namespace Celeste.Mod.SpeedrunTool.SaveLoad {
    public sealed class StateManager {
        public Player SavedPlayer;
        public Dictionary<EntityId2, Entity> SavedEntitiesDict = new Dictionary<EntityId2, Entity>();

        public Level SavedLevel => SavedPlayer?.SceneAs<Level>();
        private LoadState loadState = SaveLoad.LoadState.None;

        private Session savedSession;
        private Dictionary<EverestModule, EverestModuleSession> savedModSessions;
        private Session.CoreModes sessionCoreModeBackup;

        public bool IsLoadStart => loadState == SaveLoad.LoadState.Start;
        public bool IsLoadFrozen => loadState == SaveLoad.LoadState.Frozen;
        public bool IsLoading => loadState == SaveLoad.LoadState.Loading;
        public bool IsLoadComplete => loadState == SaveLoad.LoadState.Complete;

        public bool IsSaved => savedSession != null && SavedPlayer != null;

        private PlayerDeadBody currentPlayerDeadBody;

        public void OnLoad() {
            On.Celeste.AreaData.DoScreenWipe += QuickLoadWhenDeath;
            On.Celeste.Level.Update += LevelOnUpdate;
            On.Celeste.Overworld.ctor += ClearStateAndPbTimes;
            On.Celeste.Player.Die += PlayerOnDie;
            AttachEntityId2Utils.OnLoad();
            RestoreEntityUtils.OnLoad();
            ComponentAction.All.ForEach(action => action.OnLoad());
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
            Engine.Commands.FunctionKeyActions[5] += Clear;
        }

        private void ClearStateAndPbTimes(On.Celeste.Overworld.orig_ctor orig, Overworld self, OverworldLoader loader) {
            orig(self, loader);
            Clear();
            RoomTimerManager.Instance.ClearPbTimes();
        }

        private void LevelOnUpdate(On.Celeste.Level.orig_Update orig, Level self) {
            if (!SpeedrunToolModule.Enabled) {
                orig(self);
                return;
            }

            Player player = self.Entities.FindFirst<Player>();

            if (CheckButton(self, player)) {
                return;
            }

            // 章节切换时清除保存的状态以及房间计时器自定终点
            // Clear the savestate and custom end point when switching chapters
            if (IsSaved && (savedSession.Area.ID != self.Session.Area.ID ||
                            savedSession.Area.Mode != self.Session.Area.Mode)) {
                Clear();
                RoomTimerManager.Instance.ClearPbTimes();
            }

            // 尽快设置人物的位置与镜头，然后冻结游戏等待人物复活
            // Set player position ASAP, then freeze game and wait for the player to respawn (? - euni)
            if (IsSaved && IsLoadStart && player != null) {
                LoadStart(self, player);

                // 设置完等待一帧允许所有 Entity 更新绘制然后再冻结游戏
                // 等待一帧是因为画面背景和许多 Entity 都需时间要绘制，即使等待里一帧第三章 dust 很多的时候依然能看出绘制不完全
                // Wait for a frame so entities update, then freeze game.
                orig(self);

                // 等所有 Entity 创建完毕并运行一帧后再统一在此时机还原状态
                RestoreEntityUtils.AfterEntityCreateAndUpdate1Frame(self);

                // 冻结游戏等待 Madeline 复活
                // Freeze the game wait for madeline respawn.
                if (player.StateMachine.State == Player.StIntroRespawn) {
                    self.Frozen = true;
                    self.PauseLock = true;
                    loadState = SaveLoad.LoadState.Frozen;
                } else {
                    loadState = SaveLoad.LoadState.Loading;
                }

                return;
            }

            // 冻结时允许人物 Update 以便复活
            // Allow player to respawn while level is frozen
            if (IsSaved && IsLoadFrozen) {
                UpdatePlayerWhenFreeze(self, player);
            }

            // 人物复活完毕后设置人物相关属性
            // Set more player data after the player respawns
            if (IsSaved && (IsLoading || IsLoadFrozen) && player != null &&
                (player.StateMachine.State == Player.StNormal || player.StateMachine.State == Player.StSwim ||
                 player.StateMachine.State == Player.StFlingBird)) {
                RestoreEntityUtils.AfterPlayerRespawn(self);
                Loading(self, player);
                loadState = SaveLoad.LoadState.Complete;
            }

            orig(self);
        }

        private readonly List<int> disabledSaveStates = new List<int> {
            Player.StReflectionFall,
            Player.StTempleFall,
            Player.StCassetteFly,
            Player.StIntroJump,
            Player.StIntroWalk,
            Player.StIntroRespawn,
            Player.StIntroWakeUp
        };

        private bool CheckButton(Level level, Player player) {
            if (GetVirtualButton(Mappings.Save).Pressed && !level.Paused && !level.Transitioning && !level.PauseLock &&
                !level.InCutscene &&
                !level.SkippingCutscene && player != null && !player.Dead
                && level.Tracker.GetEntity<Lookout>()?.GetField("interacting") as bool? != true
                ) {
                GetVirtualButton(Mappings.Save).ConsumePress();
                int state = player.StateMachine.State;

                if (!disabledSaveStates.Contains(state)) {
                    SaveState(level, player);
                    return true;
                }
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

            // Bug: 吃完心后删除存档会解除静止状态
            if (GetVirtualButton(Mappings.Clear).Pressed && !level.Paused) {
                GetVirtualButton(Mappings.Clear).ConsumePress();
                Clear();
                RoomTimerManager.Instance.ClearPbTimes();
                if (!level.Frozen && level.Entities.FindAll<HeartGem>()
                    .All(gem => false == (bool) gem.GetField(typeof(HeartGem), "collected"))) {
                    level.Add(new MiniTextbox(DialogIds.DialogClear));
                }

                return false;
            }

            return false;
        }

        private void SaveState(Level level, Player player) {
            Clear();

            loadState = SaveLoad.LoadState.Start;

            ComponentAction.All.ForEach(action => action.OnSaveSate(level));

            sessionCoreModeBackup = level.Session.CoreMode;
            savedSession = level.Session.DeepClone();
            savedSession.CoreMode = level.CoreMode;
            level.Session.CoreMode = level.CoreMode;
            SavedPlayer = player;
            SavedEntitiesDict = level.FindAllToDict<Entity>();

            // save all mod sessions
            savedModSessions = new Dictionary<EverestModule, EverestModuleSession>();
            foreach (EverestModule module in Everest.Modules) {
                if (module._Session != null) {
                    savedModSessions[module] = module._Session.DeepCloneYAML<EverestModuleSession>(module.SessionType);
                }
            }

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
                    module._Session = savedModSession.DeepCloneYAML<EverestModuleSession>(module.SessionType);
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

            int state = player.StateMachine.State;
            if (!disabledSaveStates.Contains(state)) {
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
        private void LoadStart(Level level, Player player) {
            level.Session.Inventory = savedSession.Inventory;
            level.Camera.CopyFrom(SavedLevel.Camera);
            level.CameraLockMode = SavedLevel.CameraLockMode;
            level.CameraOffset = SavedLevel.CameraOffset;
            level.CoreMode = savedSession.CoreMode;
            level.Session.CoreMode = sessionCoreModeBackup;

            ComponentAction.All.ForEach(action => action.OnLoadStart(level, player, SavedPlayer));
        }

        // 人物复活完毕后设置人物相关属性
        // Set more player data after the player respawns
        private void Loading(Level level, Player player) {
            ComponentAction.All.ForEach(action => action.OnLoading(level, player, SavedPlayer));

            level.Frozen = false;
            level.PauseLock = false;
            level.TimeActive = SavedLevel.TimeActive;
        }

        private void UpdatePlayerWhenFreeze(Level level, Player player) {
            if (player == null) {
                level.Frozen = false;
            } else if (player.StateMachine.State != Player.StNormal) {
                player.Update();
            }
        }

        private void Clear() {
            if (Engine.Scene is Level level) {
                level.Frozen = false;
            }

            savedSession = null;
            savedModSessions = null;
            SavedPlayer = null;
            SavedEntitiesDict.Clear();
            loadState = SaveLoad.LoadState.None;

            ComponentAction.All.ForEach(action => action.OnClear());
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
        Loading,
        Complete
    }
}