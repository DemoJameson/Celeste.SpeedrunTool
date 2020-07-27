using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Celeste.Mod.SpeedrunTool.Extensions;
using Celeste.Mod.SpeedrunTool.SaveLoad.EntityIdPlus;
using Monocle;

namespace Celeste.Mod.SpeedrunTool.SaveLoad.RestoreActions.Base {
    public static class RestoreEntityUtils {
        private static Dictionary<EntityId2, Entity> SavedEntitiesDict => StateManager.Instance.SavedEntitiesDict;
        private static List<Entity> SavedDuplicateIdList => StateManager.Instance.SavedDuplicateIdList;

        public static void OnLoad() {
            RestoreAction.All.ForEach(restoreAction => restoreAction.OnHook());
        }

        public static void Unload() {
            RestoreAction.All.ForEach(restoreAction => restoreAction.OnUnhook());
        }

        public static void OnSaveState(Level level) {
            RestoreAction.All.ForEach(restoreAction => restoreAction.OnSaveState(level));
        }

        public static void OnLoadStart(Level level) {
            CopyCore.ClearCachedObjects();
            RestoreAction.All.ForEach(restoreAction => restoreAction.OnLoadStart(level));
        }

        public static void OnLoadComplete(Level level) {
            RestoreAction.All.ForEach(restoreAction => restoreAction.OnLoadComplete(level));
        }

        public static void OnClearState() {
            RestoreAction.All.ForEach(restoreAction => restoreAction.OnClearState());
        }

        public static void FindNotLoadedEntities(Level level) {
            var loadedDict = level.FindAllToDict<Entity>();

            var notLoadedEntities = SavedEntitiesDict.Where(pair => !loadedDict.ContainsKey(pair.Key))
                .ToDictionary(p => p.Key, p => p.Value);
            if (notLoadedEntities.Count > 0) {
                RecreateNotLoadedEntities(level, notLoadedEntities);
            }
        }

        public static void AfterEntityAwake(Level level) {
            var loadedEntitiesDict = level.FindAllToDict<Entity>();

            var notSavedEntities = loadedEntitiesDict.Where(pair => !SavedEntitiesDict.ContainsKey(pair.Key))
                .ToDictionary(p => p.Key, p => p.Value);

            RemoveNotSavedEntities(notSavedEntities);

            foreach (KeyValuePair<EntityId2, Entity> pair in loadedEntitiesDict.Where(loaded =>
                SavedEntitiesDict.ContainsKey(loaded.Key))) {
                RestoreAction.All.ForEach(restoreAction => {
                    if (restoreAction.EntityType != null &&
                        pair.Value.GetType().IsSameOrSubclassOf(restoreAction.EntityType)) {
                        restoreAction.AfterEntityAwake(pair.Value, SavedEntitiesDict[pair.Key],
                            SavedDuplicateIdList);
                    }
                });
            }
        }

        public static void AfterPlayerRespawn(Level level) {
            Dictionary<EntityId2, Entity> loadedEntitiesDict = level.FindAllToDict<Entity>();

            foreach (KeyValuePair<EntityId2, Entity> pair in loadedEntitiesDict.Where(loaded =>
                SavedEntitiesDict.ContainsKey(loaded.Key))) {
                RestoreAction.All.ForEach(restoreAction => {
                    if (restoreAction.EntityType != null &&
                        pair.Value.GetType().IsSameOrSubclassOf(restoreAction.EntityType)) {
                        restoreAction.AfterPlayerRespawn(pair.Value, SavedEntitiesDict[pair.Key]);
                    }
                });
            }
        }

        // 用于处理保存了当是没有被重新创建的物体，一般是手动创建新的实例然后添加到 Level 中。
        // 例如草莓，红泡泡，Theo，水母等跨房间的物体就需要处理，也就是附加了 Tags.Persistent 的物体。
        // 还有一些是游戏过程中代码创建出来没有 EntityData 的，但是也需要处理，例如 BadelinDummy 和 SlashFx
        private static void RecreateNotLoadedEntities(Level level, Dictionary<EntityId2, Entity> savedEntities) {
            foreach (var pair in savedEntities) {
                Entity savedEntity = pair.Value;
                Type entityType = savedEntity.GetType();
                
                // Entity 与 CrystalStaticSpinner+Border 实在是太多了，重新创建影响性能
                if (entityType != typeof(Entity) 
                    && !entityType.IsNestedPrivate
                    && CloneEntity(savedEntity) is Entity entity) {
                    // 创建添加到 Level 后还要 update 三次才会开始还原
                    // 这时如果不停止 update 有可能出现异常
                    // 用于修复：ch6 boss-00 撞击 boss 一次后等待 boss 发子弹再保存游戏会崩溃
                    entity.Active = false;
                    level.Add(entity);
                }
            }
        }

        public static Entity CloneEntity(Entity savedEntity, string tag = "Recreate not loaded entity") {
            Entity loadedEntity = savedEntity.Recreate();

            if (loadedEntity == null) {
                $"{tag} failed: {(savedEntity.HasEntityId2() ? savedEntity.GetEntityId2().ToString() : savedEntity.ToString())}"
                    .Log();
                return null;
            }

            $"{tag} succeed: {(savedEntity.HasEntityId2() ? savedEntity.GetEntityId2().ToString() : savedEntity.ToString())}"
                .DebugLog();

            // Pooled 的 Entity 一般都是空构造函数，需要 Init 方法初始化，这里直接用 CopyAll 代替
            if (loadedEntity.GetType().GetCustomAttribute<Pooled>() != null) {
                CopyCore.DeepCopyMembers(loadedEntity, savedEntity);
            }

            if (loadedEntity is SoundEmitter soundEmitter) {
                soundEmitter.Source.Stop();
            }

            loadedEntity.Position = savedEntity.Position;
            loadedEntity.CopyEntityId2(savedEntity);
            return loadedEntity;
        }

        // 与 AfterEntityAwake 是同样的时刻，用于处理不存在于保存数据中的 Entity，删除就好
        private static void RemoveNotSavedEntities(Dictionary<EntityId2, Entity> notSavedEntities) {
            foreach (var pair in notSavedEntities) {
                if (pair.Value.IsGlobalButExcludeSomeTypes()) return;
                pair.Value.RemoveSelf();
                $"Remove not saved entity: {pair.Value.GetEntityId2()}".DebugLog();
            }
        }
    }
}