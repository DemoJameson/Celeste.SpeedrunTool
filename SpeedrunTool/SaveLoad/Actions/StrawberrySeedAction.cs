using System.Collections.Generic;
using Celeste.Mod.SpeedrunTool.Extensions;
using Celeste.Mod.SpeedrunTool.SaveLoad.Component;
using Microsoft.Xna.Framework;

namespace Celeste.Mod.SpeedrunTool.SaveLoad.Actions {
    public class StrawberrySeedAction : AbstractEntityAction {
        private Dictionary<EntityID, StrawberrySeed> savedBerrySeeds = new Dictionary<EntityID, StrawberrySeed>();
        private readonly Dictionary<EntityID, StrawberrySeed> savedCollectedBerrySeeds = new Dictionary<EntityID, StrawberrySeed>();

        public override void OnQuickSave(Level level) {
            if (!(level.Entities.FindFirst<Player>() is Player player)) {
                return;
            }

            savedBerrySeeds = level.Entities.GetDictionary<StrawberrySeed>();

            foreach (Follower follower in player.Leader.Followers) {
                // multi-room strawberry seeds (Spring Collab 2020) don't need save states.
                if (follower.Entity is StrawberrySeed berry && berry.GetType().Name != "MultiRoomStrawberrySeed") {
                    savedCollectedBerrySeeds.Add(berry.GetEntityId(), berry);
                }
            }
        }

        private void RestoreStrawberrySeedPosition(On.Celeste.StrawberrySeed.orig_ctor orig, StrawberrySeed self,
            Strawberry strawberry, Vector2 position, int index, bool ghost) {
            orig(self, strawberry, position, index, ghost);

            // multi-room strawberry seeds (Spring Collab 2020) don't need save states.
            if (self.GetType().Name == "MultiRoomStrawberrySeed") {
                return;
            }

            var entityId = new EntityID(strawberry.ID.Level, (strawberry.ID + "-" + index).GetHashCode());
            self.SetEntityId(entityId);

            if (IsLoadStart && savedBerrySeeds.ContainsKey(entityId)) {
                StrawberrySeed savedBerrySeed = savedBerrySeeds[entityId];

                // 确保 StaticMover 起作用
                if (savedBerrySeed.Get<StaticMover>()?.Platform is Platform platform) {
                    self.Position = platform.Center;
                }

                // 处于还原动画中的种子，设置到起始点
                if (!savedCollectedBerrySeeds.ContainsKey(entityId) && !savedBerrySeed.Collidable) {
                    self.Position = (Vector2) savedBerrySeed.GetField(typeof(StrawberrySeed), "start");
                    if (savedBerrySeed.GetField(typeof(StrawberrySeed), "attached") is Platform savedAttached) {
                        self.Position += savedAttached.Position;
                    }
                }
                else {
                    self.Add(new RestorePositionComponent(self, savedBerrySeed));
                }
            }
        }

        public override void OnQuickLoadStart(Level level) {
            if (!(level.Entities.FindFirst<Player>() is Player player)) {
                return;
            }

            List<StrawberrySeed> addedBerrySeeds = level.Entities.FindAll<StrawberrySeed>();

            foreach (StrawberrySeed savedBerrySeed in savedCollectedBerrySeeds.Values) {
                if (addedBerrySeeds.Find(strawberrySeed => strawberrySeed.GetEntityId().Equals(savedBerrySeed.GetEntityId())) is StrawberrySeed addedBerrySeed) {
                    Follower follower = (Follower) addedBerrySeed.GetField(typeof(StrawberrySeed), "follower");
                    follower.FollowDelay = 0f;
                    follower.DelayTimer = 0f;
                    addedBerrySeed.Position = savedBerrySeed.Position;

                    addedBerrySeed.SetField(typeof(StrawberrySeed), "player", player);
                    player.Leader.GainFollower(follower);
                    addedBerrySeed.Collidable = false;
                    addedBerrySeed.Depth = -1000000;
                }
            }
        }

        public override void OnClear() {
            savedCollectedBerrySeeds.Clear();
            savedBerrySeeds.Clear();
        }

        public override void OnLoad() {
            On.Celeste.StrawberrySeed.ctor += RestoreStrawberrySeedPosition;
        }

        public override void OnUnload() {
            On.Celeste.StrawberrySeed.ctor -= RestoreStrawberrySeedPosition;
        }
    }
}