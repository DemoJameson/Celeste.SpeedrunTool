using System;
using Microsoft.Xna.Framework;

namespace Celeste.Mod.SpeedrunTool.SaveLoad.Actions {
    public class WindControllerAction : AbstractEntityAction {
        private WindController _savedWindController;
        private Vector2 _levelWind = Vector2.Zero;

        public override void OnQuickSave(Level level) {
            _savedWindController = level.Tracker.GetEntity<WindController>();
            _levelWind = level.Wind;
        }

        public override void OnQuickLoadStart(Level level) {
            if (_savedWindController == null)
                return;

            if (Math.Abs(_levelWind.X) > 0)
                level.Wind = _levelWind;

            WindController windController = level.Tracker.GetEntity<WindController>();
            WindController.Patterns savedPattern = (WindController.Patterns) _savedWindController.GetPrivateField("pattern");
            if (windController == null)
                level.Add(new WindController(savedPattern));
            else
                windController.SetPattern(savedPattern);
        }

        public override void OnClear() {
            _savedWindController = null;
            _levelWind = Vector2.Zero;
        }

        public override void OnLoad() { }

        public override void OnUnload() { }

        public override void OnInit() {
            typeof(WindController).AddToTracker();
        }
    }
}