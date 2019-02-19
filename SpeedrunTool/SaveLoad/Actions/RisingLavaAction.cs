using System.Collections;
using System.Collections.Generic;
using Celeste.Mod.SpeedrunTool.SaveLoad.Component;
using Microsoft.Xna.Framework;

namespace Celeste.Mod.SpeedrunTool.SaveLoad.Actions {
    public class RisingLavaAction : AbstractEntityAction {
        private Dictionary<EntityID, RisingLava> _savedRisingLavas = new Dictionary<EntityID, RisingLava>();

        public override void OnQuickSave(Level level) {
            _savedRisingLavas = level.Tracker.GetDictionary<RisingLava>();
        }

        private void RestoreRisingLavaState(On.Celeste.RisingLava.orig_ctor_EntityData_Vector2 orig,
            RisingLava self, EntityData data,
            Vector2 offset) {
            EntityID entityId = data.ToEntityId();
            self.SetEntityId(entityId);
            orig(self, data, offset);

            if (IsLoadStart && _savedRisingLavas.ContainsKey(entityId)) {
                RisingLava savedRisingLava = _savedRisingLavas[entityId];
                self.CopyPrivateField("intro", savedRisingLava);
                self.CopyPrivateField("delay", savedRisingLava);
                self.CopyPrivateField("waiting", savedRisingLava);
                self.Add(new RestorePositionComponent(self, savedRisingLava));
            }
        }

        private static IEnumerator SetPosition(RisingLava self, RisingLava savedRisingLava) {
            self.Position = savedRisingLava.Position;
            yield break;
        }

        public override void OnClear() {
            _savedRisingLavas.Clear();
        }

        public override void OnLoad() {
            On.Celeste.RisingLava.ctor_EntityData_Vector2 += RestoreRisingLavaState;
        }

        public override void OnUnload() {
            On.Celeste.RisingLava.ctor_EntityData_Vector2 -= RestoreRisingLavaState;
        }

        public override void OnInit() {
            typeof(RisingLava).AddToTracker();
        }
    }
}