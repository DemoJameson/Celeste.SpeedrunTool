using System.Collections.Generic;
using System.Linq;
using Microsoft.Xna.Framework;

namespace Celeste.Mod.SpeedrunTool.SaveLoad.Actions {
    public class DashSwitchAction : AbstractEntityAction {
        private IEnumerable<string> pressedDashSwitches = Enumerable.Empty<string>();

        public override void OnQuickSave(Level level) {
            pressedDashSwitches = level.Tracker.GetEntities<DashSwitch>()
                .Where(dashSwitch => !dashSwitch.Collidable).Select(
                    entity => entity.GetPrivateProperty("FlagName") as string);

            foreach (string flagName in pressedDashSwitches) {
                level.Session.SetFlag(flagName);
            }
        }

        private DashSwitch RestoreDashSwitchState(On.Celeste.DashSwitch.orig_Create origCreate, EntityData data,
            Vector2 position,
            EntityID entityId) {
            DashSwitch dashSwitch = origCreate(data, position, entityId);
            dashSwitch.SetEntityId(entityId);

            if (IsLoadStart) {
                string flagName = DashSwitch.GetFlagName(entityId);
                if (pressedDashSwitches.Contains(flagName)) {
                    dashSwitch.SetPrivateField("persistent", true);
                }
            }

            return dashSwitch;
        }

        public override void OnClear() {
            pressedDashSwitches = Enumerable.Empty<string>();
        }

        public override void OnLoad() {
            On.Celeste.DashSwitch.Create += RestoreDashSwitchState;
        }

        public override void OnUnload() {
            On.Celeste.DashSwitch.Create -= RestoreDashSwitchState;
        }

        public override void OnInit() {
            typeof(DashSwitch).AddToTracker();
        }
    }
}