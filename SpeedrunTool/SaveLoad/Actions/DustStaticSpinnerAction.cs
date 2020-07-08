using System.Collections.Generic;
using Celeste.Mod.SpeedrunTool.SaveLoad.EntityIdPlus;
using Microsoft.Xna.Framework;

namespace Celeste.Mod.SpeedrunTool.SaveLoad.Actions {
    public class DustStaticSpinnerAction : AbstractEntityAction {
        private Dictionary<EntityId2, DustStaticSpinner> savedDustStaticSpinners =
            new Dictionary<EntityId2, DustStaticSpinner>();

        public override void OnSaveSate(Level level) {
            savedDustStaticSpinners = level.Entities.FindAllToDict<DustStaticSpinner>();
        }

        private void RestoreDustStaticSpinnerPosition(On.Celeste.DustStaticSpinner.orig_ctor_EntityData_Vector2 orig,
            DustStaticSpinner self, EntityData data,
            Vector2 offset) {
            EntityId2 entityId = data.ToEntityId2(self.GetType());
            self.SetEntityId2(entityId);
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
    }
}