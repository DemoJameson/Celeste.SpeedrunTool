using System.Collections.Generic;
using Celeste.Mod.SpeedrunTool.Extensions;
using Microsoft.Xna.Framework;

namespace Celeste.Mod.SpeedrunTool.SaveLoad.Actions {
    public class BadelineOldsiteAction : AbstractEntityAction {
        private Dictionary<EntityID, BadelineOldsite> savedBadelineOldsites =
            new Dictionary<EntityID, BadelineOldsite>();

        public override void OnQuickSave(Level level) {
            savedBadelineOldsites = level.Entities.GetDictionary<BadelineOldsite>();
        }

        private void RestoreBadelineOldsitePosition(On.Celeste.BadelineOldsite.orig_ctor_EntityData_Vector2_int orig,
            BadelineOldsite self, EntityData data,
            Vector2 offset, int index) {
            EntityID entityId = data.ToEntityId();
            self.SetEntityId(entityId);
            orig(self, data, offset, index);

            if (IsLoadStart && savedBadelineOldsites.ContainsKey(entityId)) {
                self.Position = savedBadelineOldsites[entityId].Position;
            }
        }

        public override void OnClear() {
            savedBadelineOldsites.Clear();
        }

        public override void OnLoad() {
            On.Celeste.BadelineOldsite.ctor_EntityData_Vector2_int += RestoreBadelineOldsitePosition;
        }

        public override void OnUnload() {
            On.Celeste.BadelineOldsite.ctor_EntityData_Vector2_int -= RestoreBadelineOldsitePosition;
        }
    }
}