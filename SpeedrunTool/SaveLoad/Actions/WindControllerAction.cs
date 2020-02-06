using System;
using Celeste.Mod.SpeedrunTool.Extensions;
using Microsoft.Xna.Framework;

namespace Celeste.Mod.SpeedrunTool.SaveLoad.Actions {
    public class WindControllerAction : AbstractEntityAction {
        private Vector2 levelWind = Vector2.Zero;
        private WindController savedWindController;

        public override void OnQuickSave(Level level) {
            savedWindController = level.Entities.FindFirst<WindController>();
            levelWind = level.Wind;
        }

        public override void OnQuickLoadStart(Level level) {
            if (savedWindController == null) {
                return;
            }

            if (Math.Abs(levelWind.X) > 0) {
                level.Wind = levelWind;
            }

            WindController windController = level.Entities.FindFirst<WindController>();
            WindController.Patterns savedPattern =
                (WindController.Patterns) savedWindController.GetField("pattern");
            if (windController == null) {
                level.Add(new WindController(savedPattern));
            }
            else {
                windController.SetPattern(savedPattern);
            }
        }

        public override void OnClear() {
            savedWindController = null;
            levelWind = Vector2.Zero;
        }

        public override void OnLoad() { }

        public override void OnUnload() { }
    }
}