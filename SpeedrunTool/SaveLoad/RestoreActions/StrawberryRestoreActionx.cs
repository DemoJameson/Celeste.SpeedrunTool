using System.Collections.Generic;
using System.Linq;
using Celeste.Mod.SpeedrunTool.Extensions;
using Microsoft.Xna.Framework;
using Monocle;

namespace Celeste.Mod.SpeedrunTool.SaveLoad.RestoreActions {
    public class StrawberryRestoreAction : AbstractRestoreAction {
        public StrawberryRestoreAction() : base(typeof(Strawberry)) { }

        public override void Added(Entity loadedEntity, Entity savedEntity) {
            Strawberry loaded = (Strawberry) loadedEntity;
        }

        public override void AfterEntityCreateAndUpdate1Frame(Entity loadedEntity, Entity savedEntity) {
            Strawberry loaded = (Strawberry) loadedEntity;
            Strawberry saved = (Strawberry) savedEntity;
            
            loaded.CopyEntity(saved);
            loaded.CopySprite(saved, "sprite");
            loaded.CopyFields(saved, 
                "collected", "collectTimer",
                "flyingAway", "flapSpeed",
                "wobble");
            loaded.SetProperty("Winged", saved.Winged);
            loaded.ReturnHomeWhenLost = saved.ReturnHomeWhenLost;
            loaded.WaitingOnSeeds = saved.WaitingOnSeeds;
            loaded.Follower.CopyFrom(saved.Follower);

            // TODO 还原 Seeds
            // public List<StrawberrySeed> Seeds;
        }

        public override void CantFoundLoadedEntity(Level level, List<Entity> savedEntityList) {
            foreach (Strawberry saved in savedEntityList.Cast<Strawberry>()) {
                Strawberry loaded = new Strawberry(saved.GetEntityData(), Vector2.Zero, saved.ID);
                loaded.CopyEntityId2(saved);
                // TODO 移除 CopyEntityId
                loaded.CopyEntityId(saved);
                loaded.CopyEntityData(saved);
                level.Add(loaded);
            }
        }
    }

    public static class FollowerExtensions {
        public static void CopyFrom(this Follower follower, Follower otherFollower) {
            if (otherFollower.HasLeader && follower.Scene.GetPlayer() is Player player) {
                Leader leader = player.Leader;
                follower.Leader = leader;
            }

            follower.PersistentFollow = otherFollower.PersistentFollow;
            follower.FollowDelay = otherFollower.FollowDelay;
            follower.DelayTimer = otherFollower.DelayTimer;
            follower.MoveTowardsLeader = otherFollower.MoveTowardsLeader;
        }
    }
}