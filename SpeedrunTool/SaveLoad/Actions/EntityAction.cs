using Celeste.Mod.SpeedrunTool.Extensions;
using Microsoft.Xna.Framework;
using Monocle;

namespace Celeste.Mod.SpeedrunTool.SaveLoad.Actions {
    public class EntityAction : AbstractEntityAction {
        public override void OnQuickSave(Level level) { }
        public override void OnClear() { }

        private void PlatformOnCtor(On.Celeste.Platform.orig_ctor orig, Platform self, Vector2 position, bool safe) {
            orig(self, position, safe);
            self.SetEntityId(self.ToString().GetHashCode() + position.GetRealHashCode() + safe.GetHashCode());
        }

        private void EntityOnCtor_Vector2(On.Monocle.Entity.orig_ctor_Vector2 orig, Entity self, Vector2 position) {
            orig(self, position);
            self.SetEntityId(self.ToString().GetHashCode() + position.GetRealHashCode());
        }


        public override void OnLoad() {
            On.Celeste.Platform.ctor += PlatformOnCtor;
            On.Monocle.Entity.ctor_Vector2 += EntityOnCtor_Vector2;
        }

        public override void OnUnload() {
            On.Celeste.Platform.ctor -= PlatformOnCtor;
            On.Monocle.Entity.ctor_Vector2 -= EntityOnCtor_Vector2;
        }
    }
}