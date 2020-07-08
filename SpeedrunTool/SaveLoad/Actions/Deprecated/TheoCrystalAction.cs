using Celeste.Mod.SpeedrunTool.SaveLoad.Components;
using Microsoft.Xna.Framework;

namespace Celeste.Mod.SpeedrunTool.SaveLoad.Actions.Deprecated {
    public class TheoCrystalAction : ComponentAction {
        private TheoCrystal savedTheoCrystal;

        public override void OnSaveSate(Level level) {
            savedTheoCrystal = level.Entities.FindFirst<TheoCrystal>();
        }

        private void RestoreTheoCrystalPosition(On.Celeste.TheoCrystal.orig_ctor_Vector2 orig, TheoCrystal self,
            Vector2 position) {
            orig(self, position);

            if (IsLoadStart) {
                if (savedTheoCrystal != null) {
                    self.Position = savedTheoCrystal.Position;
                    self.Speed = savedTheoCrystal.Speed;
                }
                else {
                    self.Add(new RemoveSelfComponent());
                }
            }
        }

        public override void OnClear() {
            savedTheoCrystal = null;
        }

        public override void OnLoad() {
            On.Celeste.TheoCrystal.ctor_Vector2 += RestoreTheoCrystalPosition;
        }

        public override void OnUnload() {
            On.Celeste.TheoCrystal.ctor_Vector2 -= RestoreTheoCrystalPosition;
        }
    }
}