using System.Collections.Generic;
using Celeste.Mod.SpeedrunTool.Extensions;
using Celeste.Mod.SpeedrunTool.SaveLoad.Component;
using Microsoft.Xna.Framework;

namespace Celeste.Mod.SpeedrunTool.SaveLoad.Actions {
    public class SpikesAction : AbstractEntityAction {
        private Dictionary<EntityID, Spikes> savedSpikes = new Dictionary<EntityID, Spikes>();

        public override void OnQuickSave(Level level) {
            savedSpikes = level.Entities.GetDictionary<Spikes>();
        }

        private void SpikesOnCtorEntityDataVector2Directions(
            On.Celeste.Spikes.orig_ctor_EntityData_Vector2_Directions orig, Spikes self, EntityData data,
            Vector2 offset, Spikes.Directions dir) {
            EntityID entityId = data.ToEntityId();
            self.SetEntityId(entityId);
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