using Celeste.Mod.SpeedrunTool.Extensions;
using Microsoft.Xna.Framework;

namespace Celeste.Mod.SpeedrunTool.SaveLoad.Actions {
    public class PlatformAction : AbstractEntityAction {
        public override void OnSaveSate(Level level) { }
        public override void OnClear() { }

        private void PlatformOnCtor(On.Celeste.Platform.orig_ctor orig, Platform self, Vector2 position, bool safe) {
            orig(self, position, safe);
            self.TrySetEntityId2(position.ToString(), safe.ToString());
        }

        public override void OnLoad() {
            On.Celeste.Platform.ctor += PlatformOnCtor;
        }

        public override void OnUnload() {
            On.Celeste.Platform.ctor -= PlatformOnCtor;
        }
    }
}