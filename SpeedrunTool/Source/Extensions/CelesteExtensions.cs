using System.Reflection;
using Monocle;

namespace Celeste.Mod.SpeedrunTool.Extensions {
    internal static class CelesteExtensions {
        public static Level GetLevel(this Scene scene) {
            if (scene is Level level) {
                return level;
            }

            if (scene is LevelLoader levelLoader) {
                return levelLoader.Level;
            }

            return null;
        }

        public static Session GetSession(this Scene scene) {
            return scene.GetLevel()?.Session;
        }

        public static Player GetPlayer(this Scene scene) {
            if (scene.GetLevel()?.Tracker.GetEntity<Player>() is Player player) {
                return player;
            }

            return null;
        }

        public static bool IsGlobalButExcludeSomeTypes(this Entity entity) {
            return entity.TagCheck(Tags.Global) && entity.IsNotType<CassetteBlockManager>();
        }

        public static bool PausedNew(this Level level) {
            return level.Paused || level.GetFieldValue("unpauseTimer") is float unpauseTimer && unpauseTimer > 0f;
        }
    }
}