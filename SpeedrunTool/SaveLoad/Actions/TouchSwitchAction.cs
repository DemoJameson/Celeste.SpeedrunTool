using System.Collections.Generic;
using System.Linq;
using Microsoft.Xna.Framework;

namespace Celeste.Mod.SpeedrunTool.SaveLoad.Actions {
    public class TouchSwitchAction : AbstractEntityAction {
        private IEnumerable<TouchSwitch> activatedTouchSwitches = Enumerable.Empty<TouchSwitch>();

        public override void OnQuickSave(Level level) {
            activatedTouchSwitches = level.Tracker.GetCastEntities<TouchSwitch>()
                .Where(touchSwitch => touchSwitch.Switch.Activated);
        }

        private void RestoreTouchSwitchState(On.Celeste.TouchSwitch.orig_ctor_Vector2 orig, TouchSwitch self,
            Vector2 position) {
            orig(self, position);
            TouchSwitch savedTouchSwitch =
                activatedTouchSwitches.FirstOrDefault(touchSwitch => touchSwitch.Position == position);

            if (IsLoadStart && savedTouchSwitch != null) {
                self.Add(new TurnOnSwitchComponent());
            }
        }

        public override void OnClear() {
            activatedTouchSwitches = Enumerable.Empty<TouchSwitch>();
        }

        public override void OnLoad() {
            On.Celeste.TouchSwitch.ctor_Vector2 += RestoreTouchSwitchState;
        }

        public override void OnUnload() {
            On.Celeste.TouchSwitch.ctor_Vector2 -= RestoreTouchSwitchState;
        }

        private class TurnOnSwitchComponent : Monocle.Component {
            public TurnOnSwitchComponent() : base(true, false) { }

            public override void Update() {
                EntityAs<TouchSwitch>().Switch.OnActivate = null;
                EntityAs<TouchSwitch>().Switch.Activate();
                RemoveSelf();
            }
        }
    }
}