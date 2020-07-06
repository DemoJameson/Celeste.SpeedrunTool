using System.Collections.Generic;
using Celeste.Mod.SpeedrunTool.Extensions;
using Celeste.Mod.SpeedrunTool.SaveLoad.EntityIdPlus;
using Microsoft.Xna.Framework;

namespace Celeste.Mod.SpeedrunTool.SaveLoad.Actions {
    public class TouchSwitchAction : AbstractEntityAction {
        private Dictionary<EntityId2, TouchSwitch> savedTouchSwitchs = new Dictionary<EntityId2, TouchSwitch>();

        public override void OnQuickSave(Level level) {
            savedTouchSwitchs = level.Entities.FindAllToDict<TouchSwitch>();
        }

        private void TouchSwitchOnctor_EntityData_Vector2(On.Celeste.TouchSwitch.orig_ctor_EntityData_Vector2 orig, TouchSwitch self, EntityData data, Vector2 offset) {
            EntityId2 entityId = data.ToEntityId2(self.GetType());
            self.SetEntityId2(entityId);
            orig(self, data, offset);

            if (IsLoadStart && savedTouchSwitchs.ContainsKey(entityId)) {
                var savedTouchSwitch = savedTouchSwitchs[entityId];
                var savedSwitch = savedTouchSwitch.Switch;
                if (!savedSwitch.Activated) {
                    return;
                }
                
                self.CopySprite(savedTouchSwitch, "icon");
                self.CopyFields(savedTouchSwitch, "ease", "timer");
                
                var selfSwitch = self.Switch;
                selfSwitch.GroundReset = savedSwitch.GroundReset;
                selfSwitch.SetProperty("Activated", savedSwitch.Active);
                selfSwitch.SetProperty("Finished", savedSwitch.Finished);
            } 
        }

        public override void OnClear() {
            savedTouchSwitchs.Clear();
        }

        public override void OnLoad() {
            On.Celeste.TouchSwitch.ctor_EntityData_Vector2 += TouchSwitchOnctor_EntityData_Vector2;
        }

        public override void OnUnload() {
            On.Celeste.TouchSwitch.ctor_EntityData_Vector2 -= TouchSwitchOnctor_EntityData_Vector2;
        }
    }
}