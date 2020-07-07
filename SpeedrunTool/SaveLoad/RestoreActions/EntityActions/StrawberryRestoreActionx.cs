using Celeste.Mod.SpeedrunTool.Extensions;
using Monocle;

namespace Celeste.Mod.SpeedrunTool.SaveLoad.RestoreActions.EntityActions {
    public class StrawberryRestoreAction : AbstractRestoreAction {
        public StrawberryRestoreAction() : base(typeof(Strawberry)) { }

        public override void AfterEntityCreateAndUpdate1Frame(Entity loadedEntity, Entity savedEntity) {
            Strawberry loaded = (Strawberry) loadedEntity;
            Strawberry saved = (Strawberry) savedEntity;
            
            loaded.Follower.CopyFrom(saved.Follower);

            // TODO 还原 Seeds
            // public List<StrawberrySeed> Seeds;
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