using System.Collections.Generic;
using Microsoft.Xna.Framework;

namespace Celeste.Mod.SpeedrunTool.SaveLoad.Actions {
    public class DustStaticSpinnerAction : AbstractEntityAction {
        private Dictionary<EntityID, DustStaticSpinner> savedDustStaticSpinners =
            new Dictionary<EntityID, DustStaticSpinner>();

        public override void OnQuickSave(Level level) {
            savedDustStaticSpinners = level.Tracker.GetDictionary<DustStaticSpinner>();
        }

        private void RestoreDustStaticSpinnerPosition(On.Celeste.DustStaticSpinner.orig_ctor_EntityData_Vector2 orig,
            DustStaticSpinner self, EntityData data,
            Vector2 offset) {
            EntityID entityId = data.ToEntityId();
            self.SetEntityId(entityId);
            orig(self, data, offset);

            if (IsLoadStart && savedDustStaticSpinners.ContainsKey(entityId)) {
                self.Position = savedDustStaticSpinners[entityId].Position;
            }
        }

        public override void OnClear() {
            savedDustStaticSpinners.Clear();
        }

        public override void OnLoad() {
            On.Celeste.DustStaticSpinner.ctor_EntityData_Vector2 += RestoreDustStaticSpinnerPosition;
        }

        public override void OnUnload() {
            On.Celeste.DustStaticSpinner.ctor_EntityData_Vector2 -= RestoreDustStaticSpinnerPosition;
        }

        public override void OnInit() {
            typeof(DustStaticSpinner).AddToTracker();
        }

        public override void OnUpdateEntitiesWhenFreeze(Level level) {
            level.UpdateEntities<DustStaticSpinner>();
        }
    }
}