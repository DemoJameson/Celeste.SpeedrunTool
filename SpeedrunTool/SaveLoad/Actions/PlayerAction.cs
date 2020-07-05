using Celeste.Mod.SpeedrunTool.Extensions;
using Microsoft.Xna.Framework;
using Monocle;

namespace Celeste.Mod.SpeedrunTool.SaveLoad.Actions {
    class PlayerAction : AbstractEntityAction {
        public override void OnLoad() {
            On.Celeste.Player.ctor += PlayerOnCtor;
        }

        public override void OnUnload() {
            On.Celeste.Player.ctor -= PlayerOnCtor;
        }

        private void PlayerOnCtor(On.Celeste.Player.orig_ctor orig, Player self, Vector2 position, PlayerSpriteMode spriteMode) {
            // Give Player a fixed EntityID.
            self.SetEntityId(new EntityID("You can do it. —— 《Celeste》", 20180125));
            orig(self, position, spriteMode);
        }

        private void PlayerOnAdded(On.Celeste.Player.orig_Added orig, Player self, Scene scene) {
            orig(self, scene);

            // seems seeker will chase player immediately, so restore player position asap.
            if (IsLoadStart) {
                RestorePlayerPosition(self, StateManager.Instance.SavedPlayer);
            }
        }

        private void RestorePlayerPosition(Player loadedPlayer, Player savedPlayer) {
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

        public override void OnQuickLoadStart(Level level, Player player, Player savedPlayer) {
            RestorePlayerPosition(player, savedPlayer);
        }

        public override void OnQuickLoading(Level level, Player loadedPlayer, Player savedPlayer) {
            loadedPlayer.Facing = savedPlayer.Facing;
            loadedPlayer.Ducking = savedPlayer.Ducking;
            loadedPlayer.Speed = savedPlayer.Speed;
            loadedPlayer.Stamina = savedPlayer.Stamina;

            loadedPlayer.AutoJump = savedPlayer.AutoJump;
            loadedPlayer.AutoJumpTimer = savedPlayer.AutoJumpTimer;
            loadedPlayer.DashDir = savedPlayer.DashDir;
            loadedPlayer.StateMachine.SetField("state", savedPlayer.StateMachine.State);

            // too lazy to restore private List<ChaserStateSound> activeSounds
            loadedPlayer.ChaserStates.Clear();
            loadedPlayer.ChaserStates.AddRange(savedPlayer.ChaserStates);

            loadedPlayer.CopySprite(savedPlayer, "sweatSprite");
            loadedPlayer.Hair.CopyPlayerHairAndSprite(savedPlayer.Hair);
            loadedPlayer.Collidable = savedPlayer.Collidable;
            loadedPlayer.Collider = savedPlayer.Collider;

            loadedPlayer.StrawberriesBlocked = savedPlayer.StrawberriesBlocked;
            loadedPlayer.StrawberryCollectIndex = savedPlayer.StrawberryCollectIndex;
            loadedPlayer.StrawberryCollectResetTimer = savedPlayer.StrawberryCollectResetTimer;

            loadedPlayer.OverrideHairColor = savedPlayer.OverrideHairColor;
            loadedPlayer.Depth = savedPlayer.Depth;

            loadedPlayer.DummyMoving = savedPlayer.DummyMoving;
            loadedPlayer.DummyGravity = savedPlayer.DummyGravity;
            loadedPlayer.DummyFriction = savedPlayer.DummyFriction;
            loadedPlayer.DummyMaxspeed = savedPlayer.DummyMaxspeed;

            loadedPlayer.SetProperty("OnSafeGround", savedPlayer.OnSafeGround);
            loadedPlayer.SetProperty("StartedDashing", savedPlayer.StartedDashing);

            loadedPlayer.CopyFields(savedPlayer,
                "attractTo",
                "boostRed", "boostTarget",
                "beforeDashSpeed",
                "canCurveDash",
                "calledDashEvents",
                "carryOffset",
                "cassetteFlyCurve", "cassetteFlyLerp",
                "climbHopSolidPosition", "climbNoMoveTimer", "climbTriggerDir",
                "dashAttackTimer", "dashStartedOnGround", "dashTrailTimer", "dashTrailCounter", "dashCooldownTimer",
                "dashRefillCooldownTimer",
                "deadOffset",
                "dreamDashCanEndTimer", "dreamJump",
                "fastJump",
                "flash",
                "forceMoveX", "forceMoveXTimer",
                "gliderBoostDir", "gliderBoostTimer",
                "hairFlashTimer",
                "hiccupTimer",
                "highestAirY",
                "hitSquashNoMoveTimer",
                "holdCannotDuck",
                "hopWaitX", "hopWaitXSpeed",
                "hurtbox",
                "idleTimer",
                "jumpGraceTimer",
                "lastAim",
                "lastClimbMove",
                "lastDashes",
                "launched", "launchedTimer", "launchApproachX",
                "lowFrictionStopTimer",
                "maxFall",
                "minHoldTimer",
                "moveX",
                "noWindTimer",
                "onGround",
                "playFootstepOnLand",
                "starFlyTimer", "starFlyTransforming", "starFlySpeedLerp", "starFlyLastDir",
                "startHairCalled", "startHairCount",
                "summitLaunchTargetX", "summitLaunchParticleTimer",
                "varJumpTimer", "varJumpSpeed",
                "wallBoosting", "wallBoostDir", "wallBoostTimer",
                "wallSlideDir", "wallSlideTimer",
                "wallSpeedRetentionTimer", "wallSpeedRetained",
                "wasDashB",
                "wasDucking",
                "wasOnGround",
                "wasTired",
                "windMovedUp", "windDirection", "windTimeout", "windHairTimer"
            );

            // too lazy to restore this field, hope its ok.
            // private HashSet<Trigger> triggersInside;

            loadedPlayer.CopyEntity<Solid>(savedPlayer, "climbHopSolid");
            loadedPlayer.CopyEntity<Booster>(savedPlayer, "CurrentBooster");
            loadedPlayer.CopyEntity<Booster>(savedPlayer, "LastBooster");
            loadedPlayer.CopyEntity<FlingBird>(savedPlayer, "flingBird");
            loadedPlayer.CopyEntity<DreamBlock>(savedPlayer, "dreamBlock");

            switch (savedPlayer.StateMachine.State) {
                case Player.StDreamDash:
                    loadedPlayer.TreatNaive = true;
                    SoundSource dreamSFX = new SoundSource();
                    loadedPlayer.Add(dreamSFX);
                    loadedPlayer.Loop(dreamSFX, "event:/char/madeline/dreamblock_travel");
                    loadedPlayer.SetField("dreamSfxLoop", dreamSFX);
                    break;
                case Player.StStarFly:
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
                    break;
            }
        }

        public override void OnQuickSave(Level level) { }

        public override void OnClear() { }
    }
}