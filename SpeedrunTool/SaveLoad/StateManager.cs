using System;
using System.Collections.Generic;
using System.Linq;
using Celeste.Mod.SpeedrunTool.Extensions;
using Celeste.Mod.SpeedrunTool.RoomTimer;
using Celeste.Mod.SpeedrunTool.SaveLoad.Actions;
using Celeste.Mod.SpeedrunTool.SaveLoad.Actions.DSide;
using Celeste.Mod.SpeedrunTool.SaveLoad.Actions.Everest;
using Celeste.Mod.SpeedrunTool.SaveLoad.Actions.FrostHelper;
using Celeste.Mod.SpeedrunTool.SaveLoad.Actions.Glyph;
using Celeste.Mod.SpeedrunTool.SaveLoad.Actions.ShroomHelper;
using Monocle;
using static Celeste.Mod.SpeedrunTool.ButtonConfigUi;

namespace Celeste.Mod.SpeedrunTool.SaveLoad {
    public sealed class StateManager {
        private readonly List<AbstractEntityAction> entityActions = new List<AbstractEntityAction> {
            new PlayerAction(),
            new CoroutineAction(),
            
            new AscendManagerAction(),
            new AudioAction(),
            // new BadelineBoostAction(),
            new BadelineBoostAction2(),
            new BadelineDummyAction(),
            new BadelineOldsiteAction(),
            new BoosterAction(),
            new BounceBlockAction(),
            new BumperAction(),
            new CassetteBlockManagerAction(),
            new CloudAction(),
            new ClutterSwitchAction(),
            new CrumblePlatformAction(),
            new CrumbleWallOnRumbleAction(),
            new CrushBlockAction(),
            new CrystalStaticSpinnerAction(),
            new DashBlockAction(),
            new DreamBlockAction(),
            new DustStaticSpinnerAction(),
            new DashSwitchAction(),
            new FallingBlockAction(),
            new FinalBossMovingBlockAction(),
            new FinalBossAction(),
            new FireBallAction(),
            new FlingBirdAction(),
            new FloatySpaceBlockAction(),
            new FlyFeatherAction(),
            new EntityAction(),
            new ExitBlockAction(),
            new GliderAction(),
            new IntroCrusherAction(),
            new JumpthruPlatformAction(),
            new KeyAction(),
            new LightningAction(),
            new LightningBreakerBoxAction(),
            new MoveBlockAction(),
            new MovingPlatformAction(),
            new OshiroTriggerAction(),
            new PlatformAction(),
            new PufferAction(),
            new ReflectionTentaclesAction(),
            new RefillAction(),
            new RisingLavaAction(),
            new RotateSpinnerAction(),
            new SandwichLavaAction(),
            new SeekerAction(),
            new SinkingPlatformAction(),
            new SnowballAction(),
            new SpikesAction(),
            new SpringAction(),
            new StarJumpBlockAction(),
            new StaticMoverAction(),
            new StrawberryAction(),
            new StrawberrySeedAction(),
            new SwapBlockAction(),
            new SwitchGateAction(),
            new TalkComponentUIAction(),
            new TempleCrackedBlockAction(),
            new TempleGateAction(),
            new TempleMirrorPortalAction(),
            new TheoCrystalAction(),
            new TouchSwitchAction(),
            new TrackSpinnerAction(),
            new TriggerSpikesAction(),
            new WindControllerAction(),
            new ZipMoverAction(),
            // DSide
            new FastOshiroTriggerAction(),
            // Everest
            new TriggerSpikesOriginalAction(),
            // FrostHelper
            new ToggleSwapBlockAction(),
            new CustomCrystalSpinnerAction(),
            // Glyph
            new AttachedWallBoosterAction(),
            // ShroomHelper
            new AttachedIceWallAction(),
            new CrumbleBlockOnTouchAction(),
        };

        public Player SavedPlayer;
        private Level SavedLevel => SavedPlayer?.SceneAs<Level>();
        private LoadState loadState = LoadState.None;

        private Session savedSession;
        private Dictionary<EverestModule, EverestModuleSession> savedModSessions;
        private Session.CoreModes sessionCoreModeBackup;

        public bool IsLoadStart => loadState == LoadState.LoadStart;
        public bool IsLoadFrozen => loadState == LoadState.LoadFrozen;
        public bool IsLoading => loadState == LoadState.Loading;
        public bool IsLoadComplete => loadState == LoadState.LoadComplete;

        public bool IsSaved => savedSession != null && SavedPlayer != null;

        public PlayerDeadBody CurrentPlayerDeadBody;

        public void Load() {
            On.Celeste.AreaData.DoScreenWipe += QuickLoadWhenDeath;
            On.Celeste.Level.Update += LevelOnUpdate;
            On.Celeste.Overworld.ctor += ClearStateAndPbTimes;
            entityActions.ForEach(action => action.OnLoad());
        }

        public void Unload() {
            On.Celeste.AreaData.DoScreenWipe -= QuickLoadWhenDeath;
            On.Celeste.Level.Update -= LevelOnUpdate;
            On.Celeste.Overworld.ctor -= ClearStateAndPbTimes;
            entityActions.ForEach(action => action.OnUnload());
        }

        public void Init() {
            // enter debug map auto clear state
            Engine.Commands.FunctionKeyActions[5] += Clear;

            entityActions.ForEach(action => action.OnInit());
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
                QuickLoadStart(self, player);

                // 设置完等待一帧允许所有 Entity 更新然后再冻结游戏
                // Wait for a frame so entities update, then freeze game.
                orig(self);

                // 冻结游戏或者进入下一状态
                // Freeze the game or enter the next state
                if (player.StateMachine.State == Player.StIntroRespawn) {
                    self.Frozen = true;
                    self.PauseLock = true;
                    loadState = LoadState.LoadFrozen;
                } else {
                    loadState = LoadState.Loading;
                }

                return;
            }

            // 冻结时允许人物复活
            // Allow player to respawn while level is frozen
            if (IsSaved && IsLoadFrozen) {
                UpdatePlayerWhenFreeze(self, player);
            }

            // 人物复活完毕后设置人物相关属性
            // Set more player data after the player respawns
            if (IsSaved && (IsLoading || IsLoadFrozen) && player != null &&
                (player.StateMachine.State == Player.StNormal || player.StateMachine.State == Player.StSwim ||
                 player.StateMachine.State == Player.StFlingBird)) {
                QuickLoading(self, player);
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
                !level.SkippingCutscene && player != null && !player.Dead) {
                GetVirtualButton(Mappings.Save).ConsumePress();
                int state = player.StateMachine.State;

                if (!disabledSaveStates.Contains(state)) {
                    QuickSave(level, player);
                    return true;
                }
            }

            if (GetVirtualButton(Mappings.Load).Pressed && !level.Paused) {
                GetVirtualButton(Mappings.Load).ConsumePress();
                if (IsSaved) {
                    QuickLoad();
                } else if (!level.Frozen) {
                    level.Add(new MiniTextbox(DialogIds.DialogNotSaved));
                }

                return true;
            }

            if (GetVirtualButton(Mappings.Clear).Pressed && !level.Paused && IsLoadComplete) {
                GetVirtualButton(Mappings.Clear).ConsumePress();
                Clear();
                RoomTimerManager.Instance.ClearPbTimes();
                if (!level.Frozen && level.Entities.FindAll<HeartGem>().All(gem => false == (bool) gem.GetField(typeof(HeartGem), "collected"))) {
                    level.Add(new MiniTextbox(DialogIds.DialogClear));
                }
            }

            return false;
        }

        private void QuickSave(Level level, Player player) {
            Clear();

            loadState = LoadState.LoadStart;

            entityActions.ForEach(action => action.OnQuickSave(level));

            sessionCoreModeBackup = level.Session.CoreMode;
            savedSession = level.Session.DeepClone();
            savedSession.CoreMode = level.CoreMode;
            level.Session.CoreMode = level.CoreMode;
            SavedPlayer = player;

            // save all mod sessions
            savedModSessions = new Dictionary<EverestModule, EverestModuleSession>();
            foreach (EverestModule module in Everest.Modules) {
                if (module._Session != null) {
                    savedModSessions[module] = module._Session.DeepCloneYAML<EverestModuleSession>(module.SessionType);
                }
            }

            Engine.Scene = new LevelLoader(level.Session, level.Session.RespawnPoint);
        }

        private void QuickLoad() {
            if (!IsSaved) {
                return;
            }

            loadState = LoadState.LoadStart;
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
				QuickSave(level, player);
				return true;
			}
			return false;
        }

        // ReSharper disable once UnusedMember.Global
        // Public for TAS Mod
        public bool ExternalLoad() {
            QuickLoad();
			return IsSaved;
        }

        // 尽快设置人物的位置与镜头，然后冻结游戏等待人物复活
        // Set player position ASAP, then freeze game and wait for the player to respawn (? - euni)
        private void QuickLoadStart(Level level, Player player) {
            level.Session.Inventory = savedSession.Inventory;
            level.Camera.CopyFrom(SavedLevel.Camera);
            level.CameraLockMode = SavedLevel.CameraLockMode;
            level.CameraOffset = SavedLevel.CameraOffset;
            level.CoreMode = savedSession.CoreMode;
            level.Session.CoreMode = sessionCoreModeBackup;

            entityActions.ForEach(action => action.OnQuickLoadStart(level, player, SavedPlayer));
        }

        // 人物复活完毕后设置人物相关属性
        // Set more player data after the player respawns
        private void QuickLoading(Level level, Player player) {
            entityActions.ForEach(action => action.OnQuickLoading(level, player, SavedPlayer));

            level.Frozen = false;
            level.PauseLock = false;
            level.TimeActive = SavedLevel.TimeActive;

            loadState = LoadState.LoadComplete;
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
            loadState = LoadState.None;

            entityActions.ForEach(action => action.OnClear());
        }

        // Everest 的 Bug，另外的 Mod Hook 了 PlayerDeadBody.End 方法后 Level.DoScreenWipe Hook 的方法 wipeIn 为 false 时就不触发了
        // 所以改成了 Hook AreaData.DoScreenWipe 方法
        private void QuickLoadWhenDeath(On.Celeste.AreaData.orig_DoScreenWipe orig, AreaData self, Scene scene,
            bool wipeIn, Action onComplete) {
            if (SpeedrunToolModule.Settings.Enabled && SpeedrunToolModule.Settings.AutoLoadAfterDeath && IsSaved &&
                !wipeIn && scene is Level level &&
                onComplete != null && (onComplete == level.Reload || CurrentPlayerDeadBody?.HasGolden == true)) {
                Action complete = onComplete;
                CurrentPlayerDeadBody = null;
                onComplete = () => {
                    if (IsSaved) {
                        QuickLoad();
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
        LoadStart,
        LoadFrozen,
        Loading,
        LoadComplete
    }
}