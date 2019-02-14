using Microsoft.Xna.Framework;

namespace Celeste.Mod.SpeedrunTool.SaveLoad.Actions
{
    public class WindControllerAction : AbstractEntityAction
    {
        private WindController _savedWindController;

        public override void OnQuickSave(Level level)
        {
            _savedWindController = level.Tracker.GetEntity<WindController>();
        }

        public override void OnQuickLoadStart(Level level)
        {
            if (_savedWindController != null)
            {
                level.Wind = (Vector2) _savedWindController.GetPrivateField("targetSpeed");
                
                WindController windController = level.Tracker.GetEntity<WindController>();
                WindController.Patterns pattern = (WindController.Patterns) _savedWindController.GetPrivateField("pattern");
                if (windController == null)
                    level.Add(new WindController(pattern));
                else
                    windController.SetPattern(pattern);
            }
        }

        public override void OnClear()
        {
            _savedWindController = null;
        }

        public override void OnLoad() { }

        public override void OnUnload() { }

        public override void OnInit()
        {
            typeof(WindController).AddToTracker();
        }
    }
}