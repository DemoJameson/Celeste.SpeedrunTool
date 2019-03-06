using System.Collections.Generic;
using Celeste.Mod.SpeedrunTool.Extensions;
using Microsoft.Xna.Framework;

namespace Celeste.Mod.SpeedrunTool.SaveLoad.Actions {
    public class StrawberryAction : AbstractEntityAction {
        private readonly Dictionary<EntityID, Strawberry> savedBerries = new Dictionary<EntityID, Strawberry>();
        private const string EntityDataKey = "EntityDataKey";

        public override void OnQuickSave(Level level) {
            if (!(level.Tracker.GetEntity<Player>() is Player player)) {
                return;
            }

            foreach (Follower follower in player.Leader.Followers) {
                if (follower.Entity is Strawberry berry) {
                    savedBerries.Add(berry.ID, berry);
                }
            }
        }

        private void RestoreStrawberryPosition(On.Celeste.Strawberry.orig_ctor orig, Strawberry self,
            EntityData entityData, Vector2 offset, EntityID entityId) {
            self.SetExtendedDataValue(EntityDataKey, entityData);
            orig(self, entityData, offset, entityId);

            if (IsLoadStart && savedBerries.ContainsKey(entityId)) {
                Strawberry savedBerry = savedBerries[entityId];
                self.Position = savedBerry.Position;
                self.SetPrivateProperty("Winged", false);
            }
        }

        public override void OnQuickLoadStart(Level level) {
            if (!(level.Tracker.GetEntity<Player>() is Player player)) {
                return;
            }

            List<Strawberry> addedBerries = level.Entities.FindAll<Strawberry>();

            foreach (Strawberry savedBerry in savedBerries.Values) {
                Strawberry restoreBerry;
                if (addedBerries.Find(strawberry => strawberry.ID.Equals(savedBerry.ID)) is Strawberry addedBerry) {
                    restoreBerry = addedBerry;
                }
                else {
                    restoreBerry = new Strawberry(savedBerry.GetExtendedDataValue<EntityData>(EntityDataKey),
                        Vector2.Zero, savedBerry.ID);
                    level.Add(restoreBerry);
                }

                restoreBerry.Follower.FollowDelay = 0f;
                restoreBerry.Follower.DelayTimer = 0f;
                restoreBerry.Position = savedBerry.Position;

                player.Leader.GainFollower(restoreBerry.Follower);
            }
        }

        public override void OnClear() {
            savedBerries.Clear();
        }

        public override void OnLoad() {
            On.Celeste.Strawberry.ctor += RestoreStrawberryPosition;
        }

        public override void OnUnload() {
            On.Celeste.Strawberry.ctor -= RestoreStrawberryPosition;
        }
    }
}