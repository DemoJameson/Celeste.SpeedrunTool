using System.Collections.Generic;
using Celeste.Mod.SpeedrunTool.SaveLoad.Component;
using Microsoft.Xna.Framework;

namespace Celeste.Mod.SpeedrunTool.SaveLoad.Actions {
    public class DashBlockAction : AbstractEntityAction {
        private Dictionary<EntityId2, DashBlock> savedDashBlocks = new Dictionary<EntityId2, DashBlock>();

        public override void OnQuickSave(Level level) {
            savedDashBlocks = level.Entities.FindAllToDict<DashBlock>();
        }

        private void RestoreDashBlockPosition(On.Celeste.DashBlock.orig_ctor_EntityData_Vector2_EntityID orig,
            DashBlock self, EntityData data,
            Vector2 offset, EntityID id) {
            EntityId2 entityId = data.ToEntityId2(self.GetType());
            self.SetEntityId2(entityId);
            orig(self, data, offset, id);

            if (!IsLoadStart) return;
            
            if (savedDashBlocks.ContainsKey(entityId)) {
                self.Position = savedDashBlocks[entityId].Position;
            } else {
                self.Add(new RemoveSelfComponent());
            }
        }

        public override void OnClear() {
            savedDashBlocks.Clear();
        }

        public override void OnLoad() {
            On.Celeste.DashBlock.ctor_EntityData_Vector2_EntityID += RestoreDashBlockPosition;
        }

        public override void OnUnload() {
            On.Celeste.DashBlock.ctor_EntityData_Vector2_EntityID -= RestoreDashBlockPosition;
        }
    }
}