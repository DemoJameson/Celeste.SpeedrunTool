using System.Collections.Generic;
using System.Linq;
using Celeste.Mod.SpeedrunTool.Extensions;
using Celeste.Mod.SpeedrunTool.SaveLoad.EntityIdPlus;
using Monocle;

namespace Celeste.Mod.SpeedrunTool.SaveLoad.RestoreActions {
    public static class RestoreEntityUtils {
        private static List<AbstractRestoreAction> AllRestoreActions => EntityRestoreAction.AllRestoreActions;
        private static bool IsLoadStart => StateManager.Instance.IsLoadStart;
        private static Level SavedLevel => StateManager.Instance.SavedLevel;
        private static Dictionary<EntityId2, Entity> SavedEntitiesDict => StateManager.Instance.SavedEntitiesDict;

        private delegate void Found(AbstractRestoreAction restoreAction, Entity loaded, Entity saved);

        private delegate void NotFound(AbstractRestoreAction restoreAction, Entity loaded);

        private static void InvokeAction(Entity loaded, Found found, NotFound notFound = null) {
            AllRestoreActions.ForEach(restoreAction => {
                if(loaded.TagCheck(Tags.Global)) return;
                if (!loaded.GetType().IsSameOrSubclassOf(restoreAction.Type)) return;
                if (loaded.NoEntityId2()) return;

                if (SavedEntitiesDict.ContainsKey(loaded.GetEntityId2())) {
                    found(restoreAction, loaded, SavedEntitiesDict[loaded.GetEntityId2()]);
                } else {
                    notFound?.Invoke(restoreAction, loaded);
                }
            });
        }

        private static void EntityOnAdded(On.Monocle.Entity.orig_Added orig, Entity self, Scene scene) {
            orig(self, scene);

            if (IsLoadStart) {
                InvokeAction(self, (action, loaded, saved) => action.Added(loaded, saved));
            }
        }

        private static void EntityOnAwake(On.Monocle.Entity.orig_Awake orig, Entity self, Scene scene) {
            orig(self, scene);

            if (IsLoadStart) {
                InvokeAction(self, (action, loaded, saved) => action.Awake(loaded, saved));
            }
        }

        private static void LevelOnBegin(On.Celeste.Level.orig_Begin orig, Level level) {
            orig(level);
            if (!IsLoadStart) return;

            Dictionary<EntityId2,Entity> loadedEntitiesDict = level.FindAllToDict<Entity>();
            AllRestoreActions.ForEach(restoreAction => {
                var loadedDict = level.FindAllToDict(restoreAction.Type, true);
                var savedDict = SavedLevel.FindAllToDict(restoreAction.Type, true);

                List<Entity> list = savedDict.Where(pair => !loadedDict.ContainsKey(pair.Key)).Select(pair => pair.Value).ToList();
                if (list.Count > 0) {
                    restoreAction.NotLoadedEntitiesButSaved(level, list);
                }
            });
        }

        public static void AfterEntityCreateAndUpdate1Frame(Level level) {
            Dictionary<EntityId2,Entity> loadedEntitiesDict = level.FindAllToDict<Entity>();
            AllRestoreActions.ForEach(restoreAction => {
                var loadedDict = level.FindAllToDict(restoreAction.Type, true);
                var savedDict = SavedLevel.FindAllToDict(restoreAction.Type, true);

                foreach (var loaded in loadedDict) {
                    if (savedDict.ContainsKey(loaded.Key)) {
                        restoreAction.AfterEntityCreateAndUpdate1Frame(loaded.Value, savedDict[loaded.Key]);
                    } else {
                        restoreAction.NotSavedEntityButLoaded(loaded.Value);
                    }
                }
            });
        }

        public static void AfterPlayerRespawn(Level level) {
            AllRestoreActions.ForEach(restoreAction => {
                var loadedDict = level.FindAllToDict(restoreAction.Type, true);
                var savedDict = SavedLevel.FindAllToDict(restoreAction.Type, true);

                foreach (var loaded in loadedDict) {
                    if (savedDict.ContainsKey(loaded.Key)) {
                        restoreAction.AfterPlayerRespawn(loaded.Value, savedDict[loaded.Key]);
                    }
                }
            });
        }

        public static void Load() {
            On.Monocle.Entity.Added += EntityOnAdded;
            On.Monocle.Entity.Awake += EntityOnAwake;
            On.Celeste.Level.Begin += LevelOnBegin;
            AllRestoreActions.ForEach(action => action.Load());
        }

        public static void Unload() {
            On.Monocle.Entity.Added -= EntityOnAdded;
            On.Monocle.Entity.Awake -= EntityOnAwake;
            On.Celeste.Level.Begin -= LevelOnBegin;
            AllRestoreActions.ForEach(action => action.Unload());
        }
    }
}