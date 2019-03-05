using System.Collections.Generic;
using Celeste.Mod.SpeedrunTool.Extensions;
using Microsoft.Xna.Framework;

namespace Celeste.Mod.SpeedrunTool.SaveLoad.Actions {
    public class CrystalStaticSpinnerAction : AbstractEntityAction {
        private Dictionary<EntityID, CrystalStaticSpinner> savedSpinners =
            new Dictionary<EntityID, CrystalStaticSpinner>();

        public override void OnQuickSave(Level level) {
            savedSpinners = level.Tracker.GetDictionary<CrystalStaticSpinner>();
        }

        private void RestoreSpinnerPosition(
            On.Celeste.CrystalStaticSpinner.orig_ctor_EntityData_Vector2_CrystalColor orig, CrystalStaticSpinner self,
            EntityData data,
            Vector2 offset, CrystalColor color) {
            EntityID entityId = data.ToEntityId();
            self.SetEntityId(entityId);
            orig(self, data, offset, color);

            if (IsLoadStart && savedSpinners.ContainsKey(entityId)) {
                self.Position = savedSpinners[entityId].Position;
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

        public override void OnInit() {
            typeof(CrystalStaticSpinner).AddToTracker();
        }

        public override void OnUpdateEntitiesWhenFreeze(Level level) {
            level.UpdateEntities<CrystalStaticSpinner>();
        }
    }
}