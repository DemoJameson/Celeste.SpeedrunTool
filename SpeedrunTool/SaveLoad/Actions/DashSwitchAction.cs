using System.Collections.Generic;
using System.Linq;
using Microsoft.Xna.Framework;

namespace Celeste.Mod.SpeedrunTool.SaveLoad.Actions {
    public class DashSwitchAction : AbstractEntityAction {
        private IEnumerable<string> _pressedDashSwitches = Enumerable.Empty<string>();

        public override void OnQuickSave(Level level) {
            _pressedDashSwitches = level.Tracker.GetEntities<DashSwitch>()
                .Where(dashSwitch => !dashSwitch.Collidable).Select(
                    entity => entity.GetPrivateProperty("FlagName") as string);

            foreach (string flagName in _pressedDashSwitches) {
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
                if (_pressedDashSwitches.Contains(flagName)) dashSwitch.SetPrivateField("persistent", true);
            }

            return dashSwitch;
        }

        public override void OnClear() {
            _pressedDashSwitches = Enumerable.Empty<string>();
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