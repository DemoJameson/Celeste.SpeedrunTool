using System.Collections.Generic;
using Celeste.Mod.SpeedrunTool.Extensions;
using Celeste.Mod.SpeedrunTool.SaveLoad.Component;
using Microsoft.Xna.Framework;

namespace Celeste.Mod.SpeedrunTool.SaveLoad.Actions {
    public class DashBlockAction : AbstractEntityAction {
        private Dictionary<EntityID, DashBlock> savedDashBlocks = new Dictionary<EntityID, DashBlock>();

        public override void OnQuickSave(Level level) {
            savedDashBlocks = level.Tracker.GetDictionary<DashBlock>();
        }

        private void RestoreDashBlockPosition(On.Celeste.DashBlock.orig_ctor_EntityData_Vector2_EntityID orig,
            DashBlock self, EntityData data,
            Vector2 offset, EntityID id) {
            EntityID entityId = data.ToEntityId();
            self.SetEntityId(entityId);
            orig(self, data, offset, id);

            if (IsLoadStart) {
                if (savedDashBlocks.ContainsKey(entityId)) {
                    self.Position = savedDashBlocks[entityId].Position;
                }
                else {
                    self.Add(new RemoveSelfComponent());
                }
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

        public override void OnInit() {
            typeof(DashBlock).AddToTracker();
        }
    }
}