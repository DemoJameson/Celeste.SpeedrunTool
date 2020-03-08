using System.Collections.Generic;
using Celeste.Mod.SpeedrunTool.Extensions;
using Microsoft.Xna.Framework;

namespace Celeste.Mod.SpeedrunTool.SaveLoad.Actions {
    public class SwitchGateAction : AbstractEntityAction {
        private Dictionary<EntityID, SwitchGate> savedSwitchGates = new Dictionary<EntityID, SwitchGate>();

        public override void OnQuickSave(Level level) {
            savedSwitchGates = level.Entities.GetDictionary<SwitchGate>();
        }

        private void RestoreSwitchGatePosition(On.Celeste.SwitchGate.orig_ctor_EntityData_Vector2 orig, SwitchGate self,
            EntityData data,
            Vector2 offset) {
            EntityID entityId = data.ToEntityId();
            self.SetEntityId(entityId);
            orig(self, data, offset);

            if (IsLoadStart && savedSwitchGates.ContainsKey(entityId)) {
                self.Position = savedSwitchGates[entityId].Position;
            }
        }

        public override void OnClear() {
            savedSwitchGates.Clear();
        }

        public override void OnLoad() {
            On.Celeste.SwitchGate.ctor_EntityData_Vector2 += RestoreSwitchGatePosition;
        }

        public override void OnUnload() {
            On.Celeste.SwitchGate.ctor_EntityData_Vector2 -= RestoreSwitchGatePosition;
        }
    }
}