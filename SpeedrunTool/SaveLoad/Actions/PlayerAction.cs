using Celeste.Mod.SpeedrunTool.Extensions;
using Microsoft.Xna.Framework;
using Monocle;

namespace Celeste.Mod.SpeedrunTool.SaveLoad.Actions {
    class PlayerAction : AbstractEntityAction {

        private EntityID flingBird;
        private EntityID dreamBlock;

        public override void OnLoad() {
            On.Celeste.Player.Die += PlayerOnDie;
            On.Celeste.Player.Die += DisableDie;
        }
        public override void OnUnload() {
            On.Celeste.Player.Die -= PlayerOnDie;
            On.Celeste.Player.Die -= DisableDie;
        }
        public void OnQuickLoadStart(Level level, Player loadedPlayer, Player savedPlayer) {
            loadedPlayer.JustRespawned = savedPlayer.JustRespawned;
            loadedPlayer.Position = savedPlayer.Position;
            loadedPlayer.SetField<Actor>("movementCounter", savedPlayer.PositionRemainder);
            loadedPlayer.CameraAnchor = savedPlayer.CameraAnchor;
            loadedPlayer.CameraAnchorLerp = savedPlayer.CameraAnchorLerp;
            loadedPlayer.CameraAnchorIgnoreX = savedPlayer.CameraAnchorIgnoreX;
            loadedPlayer.CameraAnchorIgnoreY = savedPlayer.CameraAnchorIgnoreY;
            loadedPlayer.ForceCameraUpdate = savedPlayer.ForceCameraUpdate;
            loadedPlayer.EnforceLevelBounds = savedPlayer.EnforceLevelBounds;

            loadedPlayer.MuffleLanding = savedPlayer.MuffleLanding;
            loadedPlayer.Dashes = savedPlayer.Dashes;
        }
        public void QuickLoading(Player loadedPlayer, Player savedPlayer) {
            loadedPlayer.Facing = savedPlayer.Facing;
            loadedPlayer.Ducking = savedPlayer.Ducking;
            loadedPlayer.Speed = savedPlayer.Speed;
            loadedPlayer.Stamina = savedPlayer.Stamina;

            loadedPlayer.AutoJump = savedPlayer.AutoJump;
            loadedPlayer.AutoJumpTimer = savedPlayer.AutoJumpTimer;
            loadedPlayer.DashDir = savedPlayer.DashDir;
            loadedPlayer.StateMachine.SetField("state", savedPlayer.StateMachine.State);

			loadedPlayer.CopyFields(savedPlayer,
				"jumpGraceTimer",
				"varJumpSpeed",
				"varJumpTimer",
				"forceMoveX",
				"forceMoveXTimer",
				"hopWaitX",
				"hopWaitXSpeed",
				"lastAim",
				"wallSlideDir",
				"climbNoMoveTimer",
				"carryOffset",
				"wallSpeedRetentionTimer",
				"wallSpeedRetained",
				"wallBoostDir",
				"wallBoostTimer",
				"lastClimbMove",
				"noWindTimer",
				"climbHopSolidPosition",
				"minHoldTimer"
			);

            switch (savedPlayer.StateMachine.State) {
                case Player.StDash:
                case Player.StRedDash:
					loadedPlayer.CopyFields(savedPlayer,
						"dashAttackTimer", "dashStartedOnGround", "dashCooldownTimer", "dashRefillCooldownTimer", "DashDir"
					);
                    break;
                case Player.StDreamDash:
                    loadedPlayer.CopyFields(savedPlayer, "dreamDashCanEndTimer");
                    var dreamBlocks = Engine.Scene.Entities.GetDictionary<DreamBlock>();
                    dreamBlocks.TryGetValue(dreamBlock, out DreamBlock db);
                    loadedPlayer.SetField("dreamBlock", db);
					loadedPlayer.TreatNaive = true;
					SoundSource dreamSFX = new SoundSource();
					loadedPlayer.Add(dreamSFX);
					loadedPlayer.Loop(dreamSFX, "event:/char/madeline/dreamblock_travel");
					loadedPlayer.SetField("dreamSfxLoop", dreamSFX);
					break;
                case Player.StStarFly:
					loadedPlayer.CopyFields(savedPlayer,
						"starFlyTimer", "starFlyTransforming", "starFlySpeedLerp", "starFlyLastDir"
					);
					BloomPoint starFlyBloom = new BloomPoint(new Vector2(0f, -6f), 0f, 16f);
					loadedPlayer.SetField("starFlyBloom", starFlyBloom);
					SoundSource featherSFX = new SoundSource();
					SoundSource featherWarningSFX = new SoundSource();
					featherSFX.DisposeOnTransition = false;
					featherWarningSFX.DisposeOnTransition = false;
					featherSFX.Play("event:/game/06_reflection/feather_state_loop", "feather_speed", 1f);
					featherWarningSFX.Stop();
					loadedPlayer.Add(featherSFX);
					loadedPlayer.Add(featherWarningSFX);
					loadedPlayer.SetField("starFlyLoopSfx", featherSFX);
					loadedPlayer.SetField("starFlyWarningSfx", featherWarningSFX);
					loadedPlayer.Collider = new Hitbox(8f, 8f, -4f, -10f);
					loadedPlayer.SetField("hurtbox", new Hitbox(6f, 6f, -3f, -9f));
					break;
                case Player.StFlingBird:
                    var flingBirds = Engine.Scene.Entities.GetDictionary<FlingBird>();
                    flingBirds.TryGetValue(flingBird, out FlingBird fb);
                    loadedPlayer.SetField("flingBird", fb);
                    break;
            }
        }

        public override void OnQuickSave(Level level) {
        }

        public void OnQuickSave(Player player) {
            if (player.StateMachine.State == Player.StFlingBird)
                flingBird = ((FlingBird)player.GetField("flingBird")).GetEntityId();
            else if (player.StateMachine.State == Player.StDreamDash)
                dreamBlock = ((DreamBlock)player.GetField("dreamBlock")).GetEntityId();
        }

        public override void OnClear() {
        }
        private PlayerDeadBody PlayerOnDie(On.Celeste.Player.orig_Die orig, Player self, Vector2 direction, 
            bool evenifinvincible, bool registerdeathinstats) {
            StateManager.Instance.currentPlayerDeadBody = orig(self, direction, evenifinvincible, registerdeathinstats);
            return StateManager.Instance.currentPlayerDeadBody;
        }
        private PlayerDeadBody DisableDie(On.Celeste.Player.orig_Die orig, Player self, Vector2 direction,
            bool evenIfInvincible, bool registerDeathInStats) {
            if (StateManager.Instance.preventDie) {
                return null;
            }

            return orig(self, direction, evenIfInvincible, registerDeathInStats);
        }
    }
}
