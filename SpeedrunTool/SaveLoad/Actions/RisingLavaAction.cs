using System.Collections;
using System.Collections.Generic;
using Celeste.Mod.SpeedrunTool.Extensions;
using Celeste.Mod.SpeedrunTool.SaveLoad.Component;
using Microsoft.Xna.Framework;

namespace Celeste.Mod.SpeedrunTool.SaveLoad.Actions {
    public class RisingLavaAction : AbstractEntityAction {
        private Dictionary<EntityID, RisingLava> savedRisingLavas = new Dictionary<EntityID, RisingLava>();

        public override void OnQuickSave(Level level) {
            savedRisingLavas = level.Entities.GetDictionary<RisingLava>();
        }

        private void RestoreRisingLavaState(On.Celeste.RisingLava.orig_ctor_EntityData_Vector2 orig,
            RisingLava self, EntityData data,
            Vector2 offset) {
            EntityID entityId = data.ToEntityId();
            self.SetEntityId(entityId);
            orig(self, data, offset);

            if (IsLoadStart && savedRisingLavas.ContainsKey(entityId)) {
                RisingLava savedRisingLava = savedRisingLavas[entityId];
                self.CopyField(typeof(RisingLava), "intro", savedRisingLava);
                self.CopyField(typeof(RisingLava), "delay", savedRisingLava);
                self.CopyField(typeof(RisingLava), "waiting", savedRisingLava);
                self.Add(new RestorePositionComponent(self, savedRisingLava));
            }
        }

        private static IEnumerator SetPosition(RisingLava self, RisingLava savedRisingLava) {
            self.Position = savedRisingLava.Position;
            yield break;
        }

        public override void OnClear() {
            savedRisingLavas.Clear();
        }

        public override void OnLoad() {
            On.Celeste.RisingLava.ctor_EntityData_Vector2 += RestoreRisingLavaState;
        }

        public override void OnUnload() {
            On.Celeste.RisingLava.ctor_EntityData_Vector2 -= RestoreRisingLavaState;
        }
    }
}