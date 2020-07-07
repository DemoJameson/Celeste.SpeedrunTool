using System;
using System.Collections.Generic;
using System.Reflection;
using Celeste.Mod.SpeedrunTool.SaveLoad.EntityIdPlus;
using Microsoft.Xna.Framework;
using Monocle;

namespace Celeste.Mod.SpeedrunTool.SaveLoad.RestoreActions {
    public abstract class AbstractRestoreAction {
        protected static bool IsLoadStart => StateManager.Instance.IsLoadStart;

        public Type Type;
        public List<AbstractRestoreAction> SubclassRestoreActions;

        protected AbstractRestoreAction(Type type, List<AbstractRestoreAction> subclassRestoreActions = null) {
            Type = type;
            SubclassRestoreActions = subclassRestoreActions ?? new List<AbstractRestoreAction>();
        }

        public virtual void Load() { }
        public virtual void Unload() { }

        // 执行循序从上至下
        public virtual void Added(Entity loadedEntity, Entity savedEntity) { }

        public virtual void Awake(Entity loadedEntity, Entity savedEntity) { }

        // 用于处理保存了当是没有被重新创建的物体，一般是手动创建新的实例然后添加到 Level 中。
        // 例如草莓，红泡泡，Theo，水母等跨房间的物体就需要处理，也就是附加了 Tags.Persistent 的物体。
        public static void EntitiesSavedButNotLoaded(Level level, Dictionary<EntityId2, Entity> savedEntities) {
            foreach (var pair in savedEntities) {
                Entity savedEntity = pair.Value;
                if (savedEntity.GetEntityData() == null) continue;

                Type type = savedEntity.GetType();
                ConstructorInfo constructorInfo = type.GetConstructor(new[] {typeof(EntityData), typeof(Vector2)});
                if (constructorInfo == null) {
                    constructorInfo =
                        type.GetConstructor(new[] {typeof(EntityData), typeof(Vector2), typeof(EntityID)});
                }

                if (constructorInfo == null) {
                    continue;
                }

                var parameters = new object[] {savedEntity.GetEntityData(), Vector2.Zero};
                if (constructorInfo.GetParameters().Length == 3) {
                    parameters = new object[]
                        {savedEntity.GetEntityData(), Vector2.Zero, savedEntity.GetEntityId2().EntityId};
                }

                object loaded = constructorInfo.Invoke(parameters);
                if (loaded is Entity loadedEntity) {
                    loadedEntity.Position = savedEntity.Position;
                    loadedEntity.CopyEntityData(savedEntity);
                    loadedEntity.CopyEntityId2(savedEntity);
                    level.Add(loadedEntity);
                }
            }
        }

        // 此时恢复状态可以避免很多问题，例如刺的依附和第九章鸟的节点处理
        public virtual void AfterEntityCreateAndUpdate1Frame(Entity loadedEntity, Entity savedEntity) { }

        // 与 AfterEntityCreateAndUpdate1Frame 是同样的时刻，用于处理不存在于保存数据中的 Entity，删除就好
        public static void EntitiesLoadedButNotSaved(Dictionary<EntityId2, Entity> notSavedEntities) {
            foreach (var pair in notSavedEntities) {
                Entity loadedEntity = pair.Value;
                if (loadedEntity.TagCheck(Tags.Global)) return;
                loadedEntity.RemoveSelf();
            }
        }

        // Madelin 复活完毕的时刻，主要用于恢复 Player 的状态
        public virtual void AfterPlayerRespawn(Entity loadedEntity, Entity savedEntity) { }
    }
}