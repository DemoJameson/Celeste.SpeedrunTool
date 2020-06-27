using Celeste.Mod.SpeedrunTool.Extensions;
using Microsoft.Xna.Framework;
using Monocle;

namespace Celeste.Mod.SpeedrunTool.SaveLoad.Actions {
    public class EntityAction : AbstractEntityAction {
        public override void OnQuickSave(Level level) { }
        public override void OnClear() { }

        private static void EntityOnCtor_Vector2(On.Monocle.Entity.orig_ctor_Vector2 orig, Entity self,
            Vector2 position) {
            orig(self, position);
            self.TrySetEntityId(position.ToString());
        }


        public override void OnLoad() {
            On.Monocle.Entity.ctor_Vector2 += EntityOnCtor_Vector2;
        }

        public override void OnUnload() {
            On.Monocle.Entity.ctor_Vector2 -= EntityOnCtor_Vector2;
        }
    }
}