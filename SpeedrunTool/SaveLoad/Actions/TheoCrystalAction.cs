using Celeste.Mod.SpeedrunTool.SaveLoad.Component;
using Microsoft.Xna.Framework;

namespace Celeste.Mod.SpeedrunTool.SaveLoad.Actions {
    public class TheoCrystalAction : AbstractEntityAction {
        private TheoCrystal savedTheoCrystal;

        public override void OnQuickSave(Level level) {
            savedTheoCrystal = level.Tracker.GetEntity<TheoCrystal>();
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