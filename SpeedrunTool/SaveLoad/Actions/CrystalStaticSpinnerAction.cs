using System.Collections.Generic;
using Celeste.Mod.SpeedrunTool.SaveLoad.Component;
using Celeste.Mod.SpeedrunTool.SaveLoad.EntityIdPlus;
using Microsoft.Xna.Framework;

namespace Celeste.Mod.SpeedrunTool.SaveLoad.Actions {
    public class CrystalStaticSpinnerAction : AbstractEntityAction {
        private Dictionary<EntityId2, CrystalStaticSpinner> savedSpinners =
            new Dictionary<EntityId2, CrystalStaticSpinner>();

        public override void OnQuickSave(Level level) {
            savedSpinners = level.Entities.FindAllToDict<CrystalStaticSpinner>();
        }

        private void RestoreSpinnerPosition(
            On.Celeste.CrystalStaticSpinner.orig_ctor_EntityData_Vector2_CrystalColor orig, CrystalStaticSpinner self,
            EntityData data,
            Vector2 offset, CrystalColor color) {
            EntityId2 entityId = data.ToEntityId2(self.GetType());
            self.SetEntityId2(entityId);
            orig(self, data, offset, color);

            if (IsLoadStart && savedSpinners.ContainsKey(entityId)) {
                var savedSpinner = savedSpinners[entityId];
                var platform = savedSpinner.Get<StaticMover>()?.Platform;
                if (platform is FloatySpaceBlock) {
                    self.Add(new RestorePositionComponent(self, savedSpinner));
                }
                else {
                    self.Position = savedSpinner.Position;
                }
            }
        }

        public override void OnClear() {
            savedSpinners.Clear();
        }

        public override void OnLoad() {
            On.Celeste.CrystalStaticSpinner.ctor_EntityData_Vector2_CrystalColor += RestoreSpinnerPosition;
        }

        public override void OnUnload() {
            On.Celeste.CrystalStaticSpinner.ctor_EntityData_Vector2_CrystalColor -= RestoreSpinnerPosition;
        }
    }
}