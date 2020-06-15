using Celeste.Mod.SpeedrunTool.Extensions;
using Microsoft.Xna.Framework;
using Monocle;
using Celeste;
using Mono.Cecil;
using MonoMod.Cil;
using MonoMod.RuntimeDetour;
using MonoMod.Utils;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;

namespace Celeste.Mod.SpeedrunTool.SaveLoad.Actions {
    class PlayerAction : AbstractEntityAction {

        private EntityID flingBird = default;
        private EntityID dreamBlock = default;

        public override void OnLoad() {
            On.Celeste.Player.Die += PlayerOnDie;
            On.Celeste.Player.Die += DisableDie;
        }
        public override void OnUnload() {
            On.Celeste.Player.Die += PlayerOnDie;
            On.Celeste.Player.Die -= DisableDie;
        }
        public void OnQuickLoadStart(Level level, Player loadedPlayer, Player savedPlayer) {
            loadedPlayer.JustRespawned = savedPlayer.JustRespawned;
            loadedPlayer.Position = savedPlayer.Position;
            loadedPlayer.SetField(typeof(Actor), "movementCounter", savedPlayer.PositionRemainder);
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



            loadedPlayer.CopyField("jumpGraceTimer", savedPlayer);
            loadedPlayer.CopyField("varJumpSpeed", savedPlayer);
            loadedPlayer.CopyField("varJumpTimer", savedPlayer);
            loadedPlayer.CopyField("forceMoveX", savedPlayer);
            loadedPlayer.CopyField("forceMoveXTimer", savedPlayer);
            loadedPlayer.CopyField("hopWaitX", savedPlayer);
            loadedPlayer.CopyField("hopWaitXSpeed", savedPlayer);
            loadedPlayer.CopyField("lastAim", savedPlayer);
            loadedPlayer.CopyField("wallSlideDir", savedPlayer);
            loadedPlayer.CopyField("climbNoMoveTimer", savedPlayer);
            loadedPlayer.CopyField("carryOffset", savedPlayer);
            loadedPlayer.CopyField("wallSpeedRetentionTimer", savedPlayer);
            loadedPlayer.CopyField("wallSpeedRetained", savedPlayer);
            loadedPlayer.CopyField("wallBoostDir", savedPlayer);
            loadedPlayer.CopyField("wallBoostTimer", savedPlayer);
            loadedPlayer.CopyField("lastClimbMove", savedPlayer);
            loadedPlayer.CopyField("noWindTimer", savedPlayer);
            loadedPlayer.CopyField("climbHopSolidPosition", savedPlayer);
            loadedPlayer.CopyField("minHoldTimer", savedPlayer);

            switch (savedPlayer.StateMachine.State) {
                case Player.StDash:
                case Player.StRedDash:
                    loadedPlayer.CopyField("dashAttackTimer", savedPlayer);
                    loadedPlayer.CopyField("dashStartedOnGround", savedPlayer);
                    loadedPlayer.CopyField("dashCooldownTimer", savedPlayer);
                    loadedPlayer.CopyField("dashRefillCooldownTimer", savedPlayer);
                    loadedPlayer.CopyField("DashDir", savedPlayer);
                    break;
                case Player.StDreamDash:
                    loadedPlayer.CopyField("dreamDashCanEndTimer", savedPlayer);
                    var dreamBlocks = Engine.Scene.Entities.GetDictionary<DreamBlock>();
                    dreamBlocks.TryGetValue(dreamBlock, out DreamBlock db);
                    loadedPlayer.SetField("dreamBlock", db);
                    break;
                case Player.StStarFly:
                    loadedPlayer.CopyField("starFlyTimer", savedPlayer);
                    loadedPlayer.CopyField("starFlyTransforming", savedPlayer);
                    loadedPlayer.CopyField("starFlySpeedLerp", savedPlayer);
                    loadedPlayer.CopyField("starFlyLastDir", savedPlayer);
                    break;
                case Player.StFlingBird:
                    var flingBirds = Engine.Scene.Entities.GetDictionary<FlingBird>();
                    flingBirds.TryGetValue(flingBird, out FlingBird fb);
                    loadedPlayer.SetField("flingBird", fb);
                    break;
                default:
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
