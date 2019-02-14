using System.Collections.Generic;
using System.Linq;
using Celeste.Mod.SpeedrunTool.SaveLoad.Component;
using Microsoft.Xna.Framework;

namespace Celeste.Mod.SpeedrunTool.SaveLoad.Actions
{
    public class TheoCrystalAction : AbstractEntityAction
    {
        private TheoCrystal _savedTheoCrystal;

        public override void OnQuickSave(Level level)
        {
            _savedTheoCrystal = level.Tracker.GetEntity<TheoCrystal>();
        }

        private void RestoreTheoCrystalPosition(On.Celeste.TheoCrystal.orig_ctor_Vector2 orig, TheoCrystal self,
            Vector2 position)
        {
            orig(self, position);

            if (IsLoadStart)
            {
                if (_savedTheoCrystal != null)
                {
                    self.Position = _savedTheoCrystal.Position;
                    self.Speed = _savedTheoCrystal.Speed;
                }
                else
                    self.Add(new RemoveSelfComponent());
            }
        }

        public override void OnClear()
        {
            _savedTheoCrystal = null;
        }

        public override void OnLoad()
        {
            On.Celeste.TheoCrystal.ctor_Vector2 += RestoreTheoCrystalPosition;
        }

        public override void OnUnload()
        {
            On.Celeste.TheoCrystal.ctor_Vector2 -= RestoreTheoCrystalPosition;
        }
    }
}