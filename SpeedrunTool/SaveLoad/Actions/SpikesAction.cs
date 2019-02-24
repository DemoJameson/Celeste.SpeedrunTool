using System.Collections.Generic;
using Celeste.Mod.SpeedrunTool.SaveLoad.Component;
using Microsoft.Xna.Framework;

namespace Celeste.Mod.SpeedrunTool.SaveLoad.Actions {
    public class SpikesAction : AbstractEntityAction {
        private Dictionary<EntityID, Spikes> savedSpikes = new Dictionary<EntityID, Spikes>();

        public override void OnQuickSave(Level level) {
            savedSpikes = level.Tracker.GetDictionary<Spikes>();
        }

        private void SpikesOnCtorEntityDataVector2Directions(
            On.Celeste.Spikes.orig_ctor_EntityData_Vector2_Directions orig, Spikes self, EntityData data,
            Vector2 offset, Spikes.Directions dir) {
            EntityID entityId = data.ToEntityId();
            self.SetEntityId(entityId);
            orig(self, data, offset, dir);

            if (IsLoadStart) {
                if (savedSpikes.ContainsKey(entityId)) {
                    self.Position = savedSpikes[entityId].Position;
                    self.Collidable = savedSpikes[entityId].Collidable;
                    self.Visible = savedSpikes[entityId].Visible;
                }
                else {
                    self.Add(new RemoveSelfComponent());
                }
            }
        }

        public override void OnClear() {
            savedSpikes.Clear();
        }

        public override void OnLoad() {
            On.Celeste.Spikes.ctor_EntityData_Vector2_Directions += SpikesOnCtorEntityDataVector2Directions;
        }

        public override void OnUnload() {
            On.Celeste.Spikes.ctor_EntityData_Vector2_Directions -= SpikesOnCtorEntityDataVector2Directions;
        }

        public override void OnUpdateEntitiesWhenFreeze(Level level) {
            level.UpdateEntities<Spikes>();
        }
    }
}