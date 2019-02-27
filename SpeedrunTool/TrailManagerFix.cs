using Microsoft.Xna.Framework;
using Monocle;

namespace Celeste.Mod.SpeedrunTool {
    public static class TrailManagerFix {
        public static void Load() {
            On.Celeste.TrailManager.Snapshot.Init += SnapshotOnInit;
        }

        public static void Unload() {
            On.Celeste.TrailManager.Snapshot.Init -= SnapshotOnInit;
        }

        private static void SnapshotOnInit(On.Celeste.TrailManager.Snapshot.orig_Init orig, TrailManager.Snapshot self,
            TrailManager manager, int index, Vector2 position, Image sprite, PlayerHair hair, Color color,
            float duration, int depth) {
            orig(self, manager, index, position, sprite, hair, color, duration, depth);
            if (self != null && sprite != null && hair != null) {
                self.SpriteScale.X = self.SpriteScale.Abs().X * (int) hair.Facing;
            }
        }
    }
}