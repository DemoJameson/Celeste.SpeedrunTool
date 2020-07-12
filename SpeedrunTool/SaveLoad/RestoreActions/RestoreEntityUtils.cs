using System.Collections.Generic;
using System.Linq;
using Celeste.Mod.SpeedrunTool.Extensions;
using Celeste.Mod.SpeedrunTool.SaveLoad.EntityIdPlus;
using Monocle;

namespace Celeste.Mod.SpeedrunTool.SaveLoad.RestoreActions {
    public static class RestoreEntityUtils {
        private static bool IsLoadStart => StateManager.Instance.IsLoadStart;
        private static Dictionary<EntityId2, Entity> SavedEntitiesDict => StateManager.Instance.SavedEntitiesDict;
        private static List<Entity> SavedDuplicateIdList => StateManager.Instance.SavedDuplicateIdList;

        public static void OnSaveState(Level level) {
            RestoreAction.All.ForEach(restoreAction => restoreAction.OnSaveState(level));
        }

        public static void OnLoadStart(Level level) {
            EntityCopyCore.ClearCachedObjects();
            RestoreAction.All.ForEach(restoreAction => restoreAction.OnLoadStart(level));
        }

        public static void OnLoadComplete(Level level) {
            RestoreAction.All.ForEach(restoreAction => restoreAction.OnLoadComplete(level));
        }

        public static void OnClearState() {
            RestoreAction.All.ForEach(restoreAction => restoreAction.OnClearState());
        }

        private static void LevelOnBegin(On.Celeste.Level.orig_Begin orig, Level level) {
            orig(level);
            if (!IsLoadStart) return;

            var loadedDict = level.FindAllToDict<Entity>();

            var notLoadedEntities = SavedEntitiesDict.Where(pair => !loadedDict.ContainsKey(pair.Key))
                .ToDictionary(p => p.Key, p => p.Value);
            if (notLoadedEntities.Count > 0) {
                RestoreAction.EntitiesSavedButNotLoaded(level, notLoadedEntities);
            }
        }

        public static void AfterEntityAwake(Level level) {
            var loadedEntitiesDict = level.FindAllToDict<Entity>();

            var notSavedEntities = loadedEntitiesDict.Where(pair => !SavedEntitiesDict.ContainsKey(pair.Key))
                .ToDictionary(p => p.Key, p => p.Value);

            RestoreAction.EntitiesLoadedButNotSaved(notSavedEntities);

            foreach (var pair in loadedEntitiesDict.Where(loaded => SavedEntitiesDict.ContainsKey(loaded.Key))) {
                RestoreAction.All.ForEach(restoreAction => {
                    if (pair.Value.GetType().IsSameOrSubclassOf(restoreAction.EntityType)) {
                        restoreAction.AfterEntityAwake(pair.Value, SavedEntitiesDict[pair.Key],
                            SavedDuplicateIdList);
                    }
                });
            }
        }

        public static void AfterPlayerRespawn(Level level) {
            var loadedEntitiesDict = level.FindAllToDict<Entity>();

            foreach (var pair in loadedEntitiesDict.Where(loaded => SavedEntitiesDict.ContainsKey(loaded.Key))) {
                RestoreAction.All.ForEach(restoreAction => {
                    if (pair.Value.GetType().IsSameOrSubclassOf(restoreAction.EntityType)) {
                        restoreAction.AfterPlayerRespawn(pair.Value, SavedEntitiesDict[pair.Key]);
                    }
                });
            }
        }

        public static void OnLoad() {
            On.Celeste.Level.Begin += LevelOnBegin;
            RestoreAction.All.ForEach(restoreAction => restoreAction.OnHook());
        }

        public static void Unload() {
            On.Celeste.Level.Begin -= LevelOnBegin;
            RestoreAction.All.ForEach(restoreAction => restoreAction.OnUnhook());
        }
    }
}