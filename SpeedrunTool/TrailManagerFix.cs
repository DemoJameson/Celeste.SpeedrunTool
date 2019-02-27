using Monocle;

namespace Celeste.Mod.SpeedrunTool {
    public static class TrailManagerFix {
        public static void Load() {
            On.Celeste.TrailManager.BeforeRender += TrailManagerOnBeforeRender;
        }

        public static void Unload() {
            On.Celeste.TrailManager.BeforeRender += TrailManagerOnBeforeRender;
        }

        private static void TrailManagerOnBeforeRender(On.Celeste.TrailManager.orig_BeforeRender orig, TrailManager self) {
            On.Celeste.PlayerSprite.Render += PlayerSpriteOnRender;
            orig(self);
            On.Celeste.PlayerSprite.Render -= PlayerSpriteOnRender;
        }

        private static void PlayerSpriteOnRender(On.Celeste.PlayerSprite.orig_Render orig, PlayerSprite self) {
            if (Engine.Scene is Level level && level.Tracker.GetEntity<Player>() is Player player) {
                self.Scale.X *= (int) player.Facing;
            }

            orig(self);
        }
    }
}