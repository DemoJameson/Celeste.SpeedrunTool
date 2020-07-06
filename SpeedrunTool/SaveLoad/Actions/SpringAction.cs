using System.Collections.Generic;
using Celeste.Mod.SpeedrunTool.SaveLoad.Components;
using Celeste.Mod.SpeedrunTool.SaveLoad.EntityIdPlus;
using Microsoft.Xna.Framework;

namespace Celeste.Mod.SpeedrunTool.SaveLoad.Actions {
    public class SpringAction : AbstractEntityAction {
        private Dictionary<EntityId2, Spring> springs = new Dictionary<EntityId2, Spring>();

        public override void OnQuickSave(Level level) {
            springs = level.Entities.FindAllToDict<Spring>();
        }

        private void SpringOnCtorEntityDataVector2Orientations(
            On.Celeste.Spring.orig_ctor_EntityData_Vector2_Orientations orig, Spring self, EntityData data,
            Vector2 offset, Spring.Orientations orientation) {
            EntityId2 entityId = data.ToEntityId2(self.GetType());
            self.SetEntityId2(entityId);
            orig(self, data, offset, orientation);

            if (!IsLoadStart) return;
            
            if (springs.ContainsKey(entityId)) {
                var savedSpring = springs[entityId];
                var platform = savedSpring.Get<StaticMover>()?.Platform;
                    
                if (platform is CassetteBlock) {
                    return;
                }
                    
                if (platform is FloatySpaceBlock) {
                    self.Add(new RestorePositionComponent(self, savedSpring));
                } else {
                    self.Position = savedSpring.Position;
                }
            }
            else {
                self.Add(new RemoveSelfComponent());
            }
        }

        public override void OnClear() {
            springs.Clear();
        }

        public override void OnLoad() {
            On.Celeste.Spring.ctor_EntityData_Vector2_Orientations += SpringOnCtorEntityDataVector2Orientations;
        }

        public override void OnUnload() {
            On.Celeste.Spring.ctor_EntityData_Vector2_Orientations -= SpringOnCtorEntityDataVector2Orientations;
        }
    }
}