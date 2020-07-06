using System.Collections.Generic;
using Celeste.Mod.SpeedrunTool.SaveLoad.Components;
using Celeste.Mod.SpeedrunTool.SaveLoad.EntityIdPlus;
using Microsoft.Xna.Framework;

namespace Celeste.Mod.SpeedrunTool.SaveLoad.Actions {
    public class SpikesAction : AbstractEntityAction {
        private Dictionary<EntityId2, Spikes> savedSpikes = new Dictionary<EntityId2, Spikes>();

        public override void OnQuickSave(Level level) {
            savedSpikes = level.Entities.FindAllToDict<Spikes>();
        }

        private void SpikesOnCtorEntityDataVector2Directions(
            On.Celeste.Spikes.orig_ctor_EntityData_Vector2_Directions orig, Spikes self, EntityData data,
            Vector2 offset, Spikes.Directions dir) {
            EntityId2 entityId = data.ToEntityId2(self.GetType());
            self.SetEntityId2(entityId);
            orig(self, data, offset, dir);

            if (IsLoadStart) {
                if (savedSpikes.ContainsKey(entityId)) {
                    Spikes savedSpike = savedSpikes[entityId];
                    var platform = savedSpike.Get<StaticMover>()?.Platform;
                    if (platform is CassetteBlock) {
                        return;
                    }

                    if (platform is FloatySpaceBlock) {
                        self.Add(new RestorePositionComponent(self, savedSpike));
                    }
                    else {
                        self.Position = savedSpike.Position;
                    }
                    self.Collidable = savedSpike.Collidable;
                    self.Visible = savedSpike.Visible;
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
    }
}