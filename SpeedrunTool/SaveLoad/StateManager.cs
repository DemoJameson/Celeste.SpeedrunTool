using System;
using System.Collections.Generic;
using Celeste.Mod.SpeedrunTool.SaveLoad.Actions;
using Microsoft.Xna.Framework;
using Monocle;

namespace Celeste.Mod.SpeedrunTool.SaveLoad
{
    public sealed class SaveLoadManager
    {
        // @formatter:off
        private static readonly Lazy<SaveLoadManager> Lazy = new Lazy<SaveLoadManager>(() => new SaveLoadManager());
        public static SaveLoadManager Instance => Lazy.Value;
        private SaveLoadManager() { }
        // @formatter:on
        
        private readonly List<AbstractEntityAction> _entityActions = new List<AbstractEntityAction>
        {
            new BadelineBoostAction(),
            new BadelineOldsiteAction(),
            new BladeTrackSpinnerAction(),
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
            new DustTrackSpinnerAction(),
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
            new SwapBlockAction(),
            new SwitchGateAction(),
            new TempleCrackedBlockAction(),
            new TempleGateAction(),
            new TheoCrystalAction(),
            new TouchSwitchAction(),
            new TriggerSpikesAction(),
            new WindControllerAction(),
            new ZipMoverAction()
        };

        private Session _session;
        private Camera _camera;
        private Player _player;
        private Session.CoreModes _sessionCoreModeBackup;

        public bool IsLoadStart => _loadState == LoadState.LoadStart;
        public bool IsLoadFrozen => _loadState == LoadState.LoadFrozen;
        public bool IsLoading => _loadState == LoadState.Loading;
        public bool IsLoadComplete => _loadState == LoadState.LoadComplete;
        private LoadState _loadState = LoadState.None;


        private bool IsSaved => _session != null && _player != null && _camera != null;

        public void Load()
        {
            On.Celeste.Level.Update += LevelOnUpdate;
            _entityActions.ForEach(action => action.OnLoad());
        }

        public void Unload()
        {
            On.Celeste.Level.Update -= LevelOnUpdate;
            _entityActions.ForEach(action => action.OnUnload());
        }

        public void Init()
        {
            // enter debug map auto clear state
            Engine.Commands.FunctionKeyActions[5] += Clear;
            
            _entityActions.ForEach(action => action.OnInit());

            ButtonConfigUi.UpdateSaveButton();
            ButtonConfigUi.UpdateLoadButton();
            ButtonConfigUi.UpdateClearButton();
        }

        private void LevelOnUpdate(On.Celeste.Level.orig_Update orig, Level self)
        {
            orig(self);
            if (!SpeedrunToolModule.Settings.Enabled)
                return;

            Player player = self.Tracker.GetEntity<Player>();

            if (ButtonConfigUi.SaveButton.Value.Pressed && !self.Paused && !self.Transitioning && !self.PauseLock && !self.InCutscene &&
                !self.SkippingCutscene && player != null && !player.Dead)
            {
                int state = player.StateMachine.State;
                List<int> disabledSaveState = new List<int>
                {
                    Player.StReflectionFall,
                    Player.StTempleFall,
                    Player.StCassetteFly,
                    Player.StIntroJump,
                    Player.StIntroWalk,
                    Player.StIntroRespawn,
                    Player.StIntroWakeUp
                };

                if (!disabledSaveState.Contains(state))
                {
                    QuickSave(self, player);
                    return;
                }
            }

            if (ButtonConfigUi.LoadButton.Value.Pressed && !self.Paused)
            {
                if (IsSaved)
                {
                    QuickLoad();
                }
                else
                {
                    self.Add(new MiniTextbox("DIALOG_NOT_SAVED"));
                }

                return;
            }

            if (ButtonConfigUi.ClearButton.Value.Pressed && !self.Paused)
            {
                Clear();
                self.Add(new MiniTextbox("DIALOG_CLEAR"));
                return;
            }

            // 尽快设置人物的位置与镜头，然后冻结游戏等待人物复活
            if (IsSaved && IsLoadStart && player != null)
                QuickLoadStart(self, player);

            // 冻结时允许人物复活
            if (IsSaved && IsLoadFrozen)
                UpdateEntitiesWhenFreeze(self, player);

            // 人物复活完毕后设置人物相关属性
            if (IsSaved && (IsLoading || IsLoadFrozen) && player != null &&
                player.StateMachine.State == Player.StNormal)
                QuickLoading(self, player);


            // 章节切换时清除保存的状态
            if (IsSaved && (_session.Area.ID != self.Session.Area.ID ||
                            _session.Area.Mode != self.Session.Area.Mode))
                Clear();
        }

        private void QuickSave(Level level, Player player)
        {
            Clear();

            _loadState = LoadState.LoadStart;

            _entityActions.ForEach(action => action.OnQuickSave(level));

            _sessionCoreModeBackup = level.Session.CoreMode;
            _session = level.Session.DeepClone();
            _session.CoreMode = level.CoreMode;
            level.Session.CoreMode = level.CoreMode;
            _player = player;
            _camera = level.Camera;

            // 防止被恢复了位置的熔岩烫死
            On.Celeste.Player.Die += DisableDie;
            Engine.Scene = new LevelLoader(level.Session, level.Session.RespawnPoint);
        }

        public void QuickLoad()
        {
            if (!IsSaved) return;

            _loadState = LoadState.LoadStart;

            Session sessionCopy = _session.DeepClone();

            On.Celeste.Player.Die -= DisableDie;
            On.Celeste.Player.Die += DisableDie;
            Engine.Scene = new LevelLoader(sessionCopy, sessionCopy.RespawnPoint);
        }

        private void QuickLoadStart(Level level, Player player)
        {
            player.JustRespawned = _player.JustRespawned;
            player.Position = _player.Position;
            player.CameraAnchor = _player.CameraAnchor;
            player.CameraAnchorLerp = _player.CameraAnchorLerp;
            player.CameraAnchorIgnoreX = _player.CameraAnchorIgnoreX;
            player.CameraAnchorIgnoreY = _player.CameraAnchorIgnoreY;
            player.Dashes = _player.Dashes;

            level.Camera.CopyFrom(_camera);
            level.CoreMode = _session.CoreMode;
            level.Session.CoreMode = _sessionCoreModeBackup;

            _entityActions.ForEach(action => action.OnQuickLoadStart(level));

            if (player.StateMachine.State == Player.StIntroRespawn)
            {
                level.Frozen = true;
                level.PauseLock = true;
                _loadState = LoadState.LoadFrozen;
            }
            else
            {
                _loadState = LoadState.Loading;
            }
        }

        private void UpdateEntitiesWhenFreeze(Level level, Player player)
        {
            if (player == null)
            {
                level.Frozen = false;
            }
            else if (player.StateMachine.State != Player.StNormal)
            {
                player.Update();

                _entityActions.ForEach(action => action.OnUpdateEntitiesWhenFreeze(level));
            }
        }

        // 等待人物重生完毕后设置各项状态
        private void QuickLoading(Level level, Player player)
        {
            player.Facing = _player.Facing;
            player.Ducking = _player.Ducking;
            player.Speed = _player.Speed;
            player.Stamina = _player.Stamina;
            if (_player.StateMachine.State == Player.StStarFly)
            {
                player.StateMachine.State = Player.StStarFly;
                On.Celeste.Player.StarFlyUpdate += RestoreStarFlyTimer;
            }

            level.Frozen = false;
            level.PauseLock = false;

            _loadState = LoadState.LoadComplete;
            On.Celeste.Player.Die -= DisableDie;
        }

        private int RestoreStarFlyTimer(On.Celeste.Player.orig_StarFlyUpdate orig, Player self)
        {
            int result = orig(self);

            if (!(bool) self.GetPrivateField("starFlyTransforming"))
            {
                self.CopyPrivateField("starFlyTimer", _player);
                On.Celeste.Player.StarFlyUpdate -= RestoreStarFlyTimer;
            }

            return result;
        }

        private PlayerDeadBody DisableDie(On.Celeste.Player.orig_Die orig, Player self, Vector2 direction,
            bool evenIfInvincible, bool registerDeathInStats)
        {
            return null;
        }

        private void Clear()
        {
            if (Engine.Scene is Level level)
            {
                level.Frozen = false;
            }

            On.Celeste.Player.Die -= DisableDie;

            _session = null;
            _player = null;
            _camera = null;
            _loadState = LoadState.None;

            _entityActions.ForEach(action => action.OnClear());
        }
    }

    public enum LoadState
    {
        None,
        LoadStart,
        LoadFrozen,
        Loading,
        LoadComplete
    }
}