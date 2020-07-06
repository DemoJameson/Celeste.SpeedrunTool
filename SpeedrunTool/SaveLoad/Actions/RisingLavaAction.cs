using System.Collections;
using System.Collections.Generic;
using Celeste.Mod.SpeedrunTool.Extensions;
using Celeste.Mod.SpeedrunTool.SaveLoad.Component;
using Microsoft.Xna.Framework;

namespace Celeste.Mod.SpeedrunTool.SaveLoad.Actions {
    public class RisingLavaAction : AbstractEntityAction {
        private Dictionary<EntityId2, RisingLava> savedRisingLavas = new Dictionary<EntityId2, RisingLava>();

        public override void OnQuickSave(Level level) {
            savedRisingLavas = level.Entities.FindAllToDict<RisingLava>();
        }

        private void RestoreRisingLavaState(On.Celeste.RisingLava.orig_ctor_EntityData_Vector2 orig,
            RisingLava self, EntityData data,
            Vector2 offset) {
            EntityId2 entityId = data.ToEntityId2(self.GetType());
            self.SetEntityId2(entityId);
            orig(self, data, offset);

            if (IsLoadStart && savedRisingLavas.ContainsKey(entityId)) {
                RisingLava savedRisingLava = savedRisingLavas[entityId];
                self.CopyFields(typeof(RisingLava), savedRisingLava, "intro");
                self.CopyFields(typeof(RisingLava), savedRisingLava, "delay");
                self.CopyFields(typeof(RisingLava), savedRisingLava, "waiting");
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