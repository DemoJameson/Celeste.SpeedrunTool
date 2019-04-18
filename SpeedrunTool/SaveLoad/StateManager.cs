using System;
using System.Collections.Generic;
using Celeste.Mod.SpeedrunTool.Extensions;
using Celeste.Mod.SpeedrunTool.RoomTimer;
using Celeste.Mod.SpeedrunTool.SaveLoad.Actions;
using Microsoft.Xna.Framework;
using Monocle;

namespace Celeste.Mod.SpeedrunTool.SaveLoad {
    public sealed class StateManager {
        public const float FrozenTime = 34 * 0.017f;

        private readonly List<AbstractEntityAction> entityActions = new List<AbstractEntityAction> {
            new BadelineBoostAction(),
            new BadelineOldsiteAction(),
            new BoosterAction(),
            new BounceBlockAction(),
            new BumperAction(),
            new CloudAction(),
            new ClutterSwitchAction(),
            new CrumblePlatformAction(),
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
            new FlyFeatherAction(),
            new ExitBlockAction(),
            new KeyAction(),
            new MoveBlockAction(),
            new MovingPlatformAction(),
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
            new StrawberryAction(),
            new SwapBlockAction(),
            new SwitchGateAction(),
            new TempleCrackedBlockAction(),
            new TempleGateAction(),
            new TheoCrystalAction(),
            new TouchSwitchAction(),
            new TrackSpinnerAction(),
            new TriggerSpikesAction(),
            new WindControllerAction(),
            new ZipMoverAction()
        };

        private Camera camera;
        private LoadState loadState = LoadState.None;
        public Player SavedPlayer;

        private Session session;
        private Session.CoreModes sessionCoreModeBackup;

        public bool IsLoadStart => loadState == LoadState.LoadStart;
        public bool IsLoadFrozen => loadState == LoadState.LoadFrozen;
        public bool IsLoading => loadState == LoadState.Loading;
        public bool IsLoadComplete => loadState == LoadState.LoadComplete;


        private bool IsSaved => session != null && SavedPlayer != null && camera != null;

        public void Load() {
            On.Celeste.Level.DoScreenWipe += QuickLoadWhenDeath;
            On.Celeste.Level.Update += LevelOnUpdate;
            entityActions.ForEach(action => action.OnLoad());
        }

        public void Unload() {
            On.Celeste.Level.DoScreenWipe -= QuickLoadWhenDeath;
            On.Celeste.Level.Update -= LevelOnUpdate;
            entityActions.ForEach(action => action.OnUnload());
        }

        public void Init() {
            // enter debug map auto clear state
            Engine.Commands.FunctionKeyActions[5] += Clear;

            entityActions.ForEach(action => action.OnInit());

            ButtonConfigUi.UpdateSaveButton();
            ButtonConfigUi.UpdateLoadButton();
            ButtonConfigUi.UpdateClearButton();
        }

        private void LevelOnUpdate(On.Celeste.Level.orig_Update orig, Level self) {
            if (!SpeedrunToolModule.Enabled) {
                orig(self);
                return;
            }

            orig(self);

            Player player = self.Tracker.GetEntity<Player>();

            if (CheckButton(self, player)) {
                return;
            }

            // 章节切换时清除保存的状态
            if (IsSaved && (session.Area.ID != self.Session.Area.ID ||
                            session.Area.Mode != self.Session.Area.Mode)) {
                Clear();
            }

            // 尽快设置人物的位置与镜头，然后冻结游戏等待人物复活
            if (IsSaved && IsLoadStart && player != null) {
                QuickLoadStart(self, player);
                return;
            }

            // 冻结时允许人物复活
            if (IsSaved && IsLoadFrozen) {
                UpdateEntitiesWhenFreeze(self, player);
            }

            // 人物复活完毕后设置人物相关属性
            if (IsSaved && (IsLoading || IsLoadFrozen) && player != null &&
                (player.StateMachine.State == Player.StNormal || player.StateMachine.State == Player.StSwim)) {
                QuickLoading(self, player);
            }
        }

        private bool CheckButton(Level level, Player player) {
            if (ButtonConfigUi.SaveButton.Value.Pressed && !level.Paused && !level.Transitioning && !level.PauseLock &&
                !level.InCutscene &&
                !level.SkippingCutscene && player != null && !player.Dead) {
                ButtonConfigUi.SaveButton.Value.ConsumePress();
                int state = player.StateMachine.State;
                List<int> disabledSaveState = new List<int> {
                    Player.StReflectionFall,
                    Player.StTempleFall,
                    Player.StCassetteFly,
                    Player.StIntroJump,
                    Player.StIntroWalk,
                    Player.StIntroRespawn,
                    Player.StIntroWakeUp
                };

                if (!disabledSaveState.Contains(state)) {
                    QuickSave(level, player);
                    return true;
                }
            }

            if (ButtonConfigUi.LoadButton.Value.Pressed && !level.Paused) {
                ButtonConfigUi.LoadButton.Value.ConsumePress();
                if (IsSaved) {
                    QuickLoad();
                }
                else if (!level.Frozen) {
                    level.Add(new MiniTextbox(DialogIds.DialogNotSaved));
                }

                return true;
            }

            if (ButtonConfigUi.ClearButton.Value.Pressed && !level.Paused) {
                ButtonConfigUi.ClearButton.Value.ConsumePress();
                Clear();
                RoomTimerManager.Instance.ClearPbTimes();
                if (!level.Frozen) {
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
            session = level.Session.DeepClone();
            session.CoreMode = level.CoreMode;
            level.Session.CoreMode = level.CoreMode;
            SavedPlayer = player;
            camera = level.Camera;

            // 防止被恢复了位置的熔岩烫死
            On.Celeste.Player.Die -= DisableDie;
            On.Celeste.Player.Die += DisableDie;

            Engine.Scene = new LevelLoader(level.Session, level.Session.RespawnPoint);
        }

        public void QuickLoad() {
            if (!IsSaved) {
                return;
            }

            loadState = LoadState.LoadStart;
            Session sessionCopy = session.DeepClone();
            On.Celeste.Player.Die -= DisableDie;
            On.Celeste.Player.Die += DisableDie;
            Engine.Scene = new LevelLoader(sessionCopy, sessionCopy.RespawnPoint);
        }

        private void QuickLoadStart(Level level, Player player) {
            player.JustRespawned = SavedPlayer.JustRespawned;
            player.Position = SavedPlayer.Position;
            player.CameraAnchor = SavedPlayer.CameraAnchor;
            player.CameraAnchorLerp = SavedPlayer.CameraAnchorLerp;
            player.CameraAnchorIgnoreX = SavedPlayer.CameraAnchorIgnoreX;
            player.CameraAnchorIgnoreY = SavedPlayer.CameraAnchorIgnoreY;
            player.Dashes = SavedPlayer.Dashes;

            level.Camera.CopyFrom(camera);
            level.CoreMode = session.CoreMode;
            level.Session.CoreMode = sessionCoreModeBackup;

            entityActions.ForEach(action => action.OnQuickLoadStart(level));

            if (player.StateMachine.State == Player.StIntroRespawn) {
                level.Frozen = true;
                level.PauseLock = true;
                loadState = LoadState.LoadFrozen;
            }
            else {
                loadState = LoadState.Loading;
            }
        }

        private void UpdateEntitiesWhenFreeze(Level level, Player player) {
            if (player == null) {
                level.Frozen = false;
            }
            else if (player.StateMachine.State != Player.StNormal) {
                player.Update();

                entityActions.ForEach(action => action.OnUpdateEntitiesWhenFreeze(level));
            }
        }

        // 等待人物重生完毕后设置各项状态
        private void QuickLoading(Level level, Player player) {
            player.Facing = SavedPlayer.Facing;
            player.Ducking = SavedPlayer.Ducking;
            player.Speed = SavedPlayer.Speed;
            player.Stamina = SavedPlayer.Stamina;
            if (SavedPlayer.StateMachine.State == Player.StStarFly) {
                player.StateMachine.State = Player.StStarFly;
                On.Celeste.Player.StarFlyUpdate += RestoreStarFlyTimer;
            }

            level.Frozen = false;
            level.PauseLock = false;

            loadState = LoadState.LoadComplete;
            On.Celeste.Player.Die -= DisableDie;
        }

        private int RestoreStarFlyTimer(On.Celeste.Player.orig_StarFlyUpdate orig, Player self) {
            int result = orig(self);

            if (!(bool) self.GetPrivateField("starFlyTransforming")) {
                self.CopyPrivateField("starFlyTimer", SavedPlayer);
                On.Celeste.Player.StarFlyUpdate -= RestoreStarFlyTimer;
            }

            return result;
        }

        private void Clear() {
            if (Engine.Scene is Level level) {
                level.Frozen = false;
            }

            On.Celeste.Player.Die -= DisableDie;
            session = null;
            SavedPlayer = null;
            camera = null;
            loadState = LoadState.None;

            entityActions.ForEach(action => action.OnClear());
        }
        
        private void QuickLoadWhenDeath(On.Celeste.Level.orig_DoScreenWipe orig, Level self, bool wipeIn, Action onComplete, bool hiresSnow) {
            if (SpeedrunToolModule.Settings.Enabled && SpeedrunToolModule.Settings.AutoLoadAfterDeath && IsSaved && onComplete == self.Reload) {
                onComplete = QuickLoad;
            }
            
            orig(self, wipeIn, onComplete, hiresSnow);
        }

        private PlayerDeadBody DisableDie(On.Celeste.Player.orig_Die orig, Player self, Vector2 direction,
            bool evenIfInvincible, bool registerDeathInStats) {
            return null;
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