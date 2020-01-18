using System.Collections.Generic;
using System.Linq;
using Celeste.Mod.SpeedrunTool.Extensions;
using Microsoft.Xna.Framework;

namespace Celeste.Mod.SpeedrunTool.SaveLoad.Actions {
    public class DashSwitchAction : AbstractEntityAction {
        private IEnumerable<string> pressedDashSwitches = Enumerable.Empty<string>();
        private Dictionary<EntityID, DashSwitch> savedDashSwitches = new Dictionary<EntityID, DashSwitch>();

        public override void OnQuickSave(Level level) {
            savedDashSwitches = level.Entities.GetDictionary<DashSwitch>();
            
            pressedDashSwitches = level.Entities.FindAll<DashSwitch>()
                .Where(dashSwitch => !dashSwitch.Collidable).Select(
                    entity => DashSwitch.GetFlagName(entity.GetEntityId()));

            foreach (string flagName in pressedDashSwitches) {
                level.Session.SetFlag(flagName);
            }
        }

        private DashSwitch RestoreDashSwitchState(On.Celeste.DashSwitch.orig_Create orig, EntityData data,
            Vector2 position,
            EntityID entityId) {
            DashSwitch self = orig(data, position, entityId);
            self.SetEntityId(entityId);

            if (IsLoadStart) {
                string flagName = DashSwitch.GetFlagName(entityId);
                if (pressedDashSwitches.Contains(flagName)) {
                    self.SetPrivateField("persistent", true);
                }

                if (savedDashSwitches.ContainsKey(entityId)) {
                    DashSwitch savedDashSwitch = savedDashSwitches[entityId];
                    self.Position = savedDashSwitch.Position;
                }
            }

            return self;
        }

        public override void OnClear() {
            pressedDashSwitches = Enumerable.Empty<string>();
            savedDashSwitches.Clear();
        }

        public override void OnLoad() {
            On.Celeste.DashSwitch.Create += RestoreDashSwitchState;
        }

        public override void OnUnload() {
            On.Celeste.DashSwitch.Create -= RestoreDashSwitchState;
        }
    }
}