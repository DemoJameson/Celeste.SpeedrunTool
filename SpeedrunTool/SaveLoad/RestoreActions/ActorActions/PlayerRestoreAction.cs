using System.Linq;
using System.Reflection;
using Celeste.Mod.SpeedrunTool.Extensions;
using Celeste.Mod.SpeedrunTool.SaveLoad.EntityIdPlus;
using Microsoft.Xna.Framework;
using Monocle;

namespace Celeste.Mod.SpeedrunTool.SaveLoad.RestoreActions.ActorActions {
    public class PlayerRestoreAction : AbstractRestoreAction {
        public PlayerRestoreAction() : base(typeof(Player)) { }

        public override void AfterEntityCreateAndUpdate1Frame(Entity loadedEntity, Entity savedEntity) {
            Player loaded = (Player) loadedEntity;
            Player saved = (Player) savedEntity;
            
            loaded.CopyEntity(saved);
            
            // 避免复活时的光圈被背景遮住
            loaded.Depth = Depths.Top; 

            loaded.JustRespawned = saved.JustRespawned;
            loaded.CameraAnchor = saved.CameraAnchor;
            loaded.CameraAnchorLerp = saved.CameraAnchorLerp;
            loaded.CameraAnchorIgnoreX = saved.CameraAnchorIgnoreX;
            loaded.CameraAnchorIgnoreY = saved.CameraAnchorIgnoreY;
            loaded.ForceCameraUpdate = saved.ForceCameraUpdate;
            loaded.EnforceLevelBounds = saved.EnforceLevelBounds;
            loaded.MuffleLanding = saved.MuffleLanding;
            loaded.Dashes = saved.Dashes;
        }

        public override void AfterPlayerRespawn(Entity loadedEntity, Entity savedEntity) {
            Player loaded = (Player) loadedEntity;
            Player saved = (Player) savedEntity;

            loaded.Facing = saved.Facing;
            loaded.Ducking = saved.Ducking;
            loaded.Speed = saved.Speed;
            loaded.Stamina = saved.Stamina;

            loaded.AutoJump = saved.AutoJump;
            loaded.AutoJumpTimer = saved.AutoJumpTimer;
            loaded.DashDir = saved.DashDir;
            loaded.StateMachine.SetField("state", saved.StateMachine.State);

            // too lazy to restore private List<ChaserStateSound> activeSounds
            loaded.ChaserStates.Clear();
            loaded.ChaserStates.AddRange(saved.ChaserStates);

            loaded.CopySprite(saved, "sweatSprite");
            loaded.Hair.CopyPlayerHairAndSprite(saved.Hair);
            loaded.Collidable = saved.Collidable;

            loaded.StrawberriesBlocked = saved.StrawberriesBlocked;
            loaded.StrawberryCollectIndex = saved.StrawberryCollectIndex;
            loaded.StrawberryCollectResetTimer = saved.StrawberryCollectResetTimer;

            loaded.OverrideHairColor = saved.OverrideHairColor;
            loaded.Depth = saved.Depth;

            loaded.DummyMoving = saved.DummyMoving;
            loaded.DummyGravity = saved.DummyGravity;
            loaded.DummyFriction = saved.DummyFriction;
            loaded.DummyMaxspeed = saved.DummyMaxspeed;

            loaded.SetProperty("OnSafeGround", saved.OnSafeGround);
            loaded.SetProperty("StartedDashing", saved.StartedDashing);

            loaded.CopyFields(saved,
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

            loaded.CopyEntity2(saved, "climbHopSolid");
            loaded.CopyEntity2(saved, "CurrentBooster");
            loaded.CopyEntity2(saved, "LastBooster");
            loaded.CopyEntity2(saved, "flingBird");
            loaded.CopyEntity2(saved, "dreamBlock");

            switch (saved.StateMachine.State) {
                case Player.StDreamDash:
                    SoundSource dreamSFX = new SoundSource();
                    loaded.Add(dreamSFX);
                    loaded.Loop(dreamSFX, "event:/char/madeline/dreamblock_travel");
                    loaded.SetField("dreamSfxLoop", dreamSFX);
                    break;
                case Player.StStarFly:
                    BloomPoint starFlyBloom = new BloomPoint(new Vector2(0f, -6f), 0f, 16f);
                    loaded.SetField("starFlyBloom", starFlyBloom);
                    SoundSource featherSFX = new SoundSource();
                    SoundSource featherWarningSFX = new SoundSource();
                    featherSFX.DisposeOnTransition = false;
                    featherWarningSFX.DisposeOnTransition = false;
                    featherSFX.Play("event:/game/06_reflection/feather_state_loop", "feather_speed", 1f);
                    featherWarningSFX.Stop();
                    loaded.Add(featherSFX);
                    loaded.Add(featherWarningSFX);
                    loaded.SetField("starFlyLoopSfx", featherSFX);
                    loaded.SetField("starFlyWarningSfx", featherWarningSFX);
                    break;
            }

            RestoreLeader(loaded, saved);
        }

        private void RestoreLeader(Player loaded, Player saved) {
            Leader loadedLeader = loaded.Leader;
            Leader savedLeader = saved.Leader;

            loadedLeader.Position = savedLeader.Position;
            loadedLeader.PastPoints.Clear();
            loadedLeader.PastPoints.AddRange(savedLeader.PastPoints);
            savedLeader.Followers.ForEach(savedFollower => {
                Entity entity = loaded.Scene.FindFirst(savedFollower.Entity.GetEntityId2());
                if (entity == null) return;
                FieldInfo followerFieldInfo = entity.GetType()
                    .GetFields(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)
                    .FirstOrDefault(fieldInfo => fieldInfo.FieldType == typeof(Follower));
                if (followerFieldInfo?.GetValue(entity) is Follower follower) {
                    loadedLeader.Followers.Add(follower);
                }               
            });
        }

        public override void Load() {
            On.Celeste.Player.ctor += PlayerOnCtor;
        }

        public override void Unload() {
            On.Celeste.Player.ctor -= PlayerOnCtor;
        }

        private static void PlayerOnCtor(On.Celeste.Player.orig_ctor orig, Player self, Vector2 position,
            PlayerSpriteMode spriteMode) {
            // Give Player a fixed EntityId2.
            self.SetEntityId2(new EntityID("You can do it. —— 《Celeste》", 20180125));
            orig(self, position, spriteMode);
        }
    }
}