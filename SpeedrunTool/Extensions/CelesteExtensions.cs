using Monocle;

namespace Celeste.Mod.SpeedrunTool.Extensions {
    public static class CelesteExtensions {
        // public static void AddToTracker(this Type type) {
        //     if (!Tracker.StoredEntityTypes.Contains(type)) {
        //         Tracker.StoredEntityTypes.Add(type);
        //     }
        //
        //     if (!Tracker.TrackedEntityTypes.ContainsKey(type)) {
        //         Tracker.TrackedEntityTypes[type] = new List<Type> {type};
        //     }
        //     else if (!Tracker.TrackedEntityTypes[type].Contains(type)) {
        //         Tracker.TrackedEntityTypes[type].Add(type);
        //     }
        // }
        
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
            if (scene.GetLevel()?.Entities.FindFirst<Player>() is Player player) {
                return player;
            }

            return null;
        }

        public static bool IsGlobalButNotCassetteManager(this Entity entity) {
            return entity.TagCheck(Tags.Global) && entity.GetType() != typeof(CassetteBlockManager);
        }
    }
}