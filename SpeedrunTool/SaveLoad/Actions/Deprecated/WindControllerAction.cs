using System;
using Celeste.Mod.SpeedrunTool.Extensions;
using Microsoft.Xna.Framework;

namespace Celeste.Mod.SpeedrunTool.SaveLoad.Actions.Deprecated {
    public class WindControllerAction : ComponentAction {
        private Vector2 levelWind = Vector2.Zero;
        private WindController savedWindController;

        public override void OnSaveSate(Level level) {
            savedWindController = level.Entities.FindFirst<WindController>();
            levelWind = level.Wind;
        }

        public override void OnLoadStart(Level level, Player player, Player savedPlayer) {
            if (savedWindController == null) {
                return;
            }

            if (Math.Abs(levelWind.X) > 0) {
                level.Wind = levelWind;
            }

            WindController windController = level.Entities.FindFirst<WindController>();
            WindController.Patterns savedPattern =
                (WindController.Patterns) savedWindController.GetField(typeof(WindController), "pattern");
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