using System;
using System.Collections.Generic;
using System.Linq;
using Celeste.Mod.SpeedrunTool.Extensions;
using Microsoft.Xna.Framework;
using Monocle;

namespace Celeste.Mod.SpeedrunTool.SaveLoad.EntityIdPlus {
    // 用来替换 EntityID 避免 ID 重复
    // 官图中 Trigger 的 ID 与 Entity 的 ID 有很大几率重复
    public readonly struct EntityId2 {
        public static readonly EntityId2 PlayerFixedEntityId2 =
            new EntityId2(new EntityID("You can do it. —— 《Celeste》", 20180125), typeof(Player));


        public readonly EntityID EntityId;
        public readonly Type Type;

        public EntityId2(EntityID entityId, Type type) {
            EntityId = entityId;
            Type = type;

            if (!type.IsSameOrSubclassOf(typeof(Entity))) {
                throw new ArgumentException("type must be Entity");
            }
        }

        public override bool Equals(object obj) {
            return obj is EntityId2 id && id.EntityId.Equals(EntityId) && id.Type == Type;
        }

        public override int GetHashCode() {
            return ToString().GetHashCode();
        }

        public override string ToString() {
            return $"EntityId2: Type={Type.FullName} Level={EntityId.Level} ID={EntityId.ID}";
        }

        public static bool operator ==(EntityId2 value1, EntityId2 value2) {
            return value1.Equals(value2);
        }

        public static bool operator !=(EntityId2 value1, EntityId2 value2) {
            return !(value1 == value2);
        }
    }

    public static class EntityId2Extension {
        private const string EntityId2Key = "SpeedrunTool_EntityId2_Key";
        private const string EntityDataKey = "SpeedrunTool_EntityData_Key";
        private const string StartPositionKey = "SpeedrunTool_StartPosition_Key";

        public static EntityId2 ToEntityId2(this EntityID entityId, Type type) {
            return new EntityId2(entityId, type);
        }

        public static EntityId2 ToEntityId2(this EntityID entityId, Entity entity) {
            return entityId.ToEntityId2(entity.GetType());
        }

        public static EntityId2 ToEntityId2(this EntityData entityData, Type type) {
            return new EntityId2(new EntityID(entityData.Level.Name, entityData.ID), type);
        }

        public static EntityId2 ToEntityId2(this EntityData entityData, Entity entity) {
            return entityData.ToEntityId2(entity.GetType());
        }

        public static EntityId2 GetEntityId2(this Entity entity) {
            return entity.GetExtendedDataValue<EntityId2>(EntityId2Key);
        }

        public static void SetEntityId2(this Entity entity, EntityId2 entityId2) {
            entity.SetExtendedDataValue(EntityId2Key, entityId2);
        }

        public static void SetEntityId2(this Entity entity, EntityID entityId) {
            entity.SetEntityId2(entityId.ToEntityId2(entity.GetType()));
        }

        public static void CopyEntityId2(this Entity entity, Entity otherEntity) {
            if (otherEntity.HasEntityId2()) {
                entity.SetEntityId2(otherEntity.GetEntityId2());
            }
        }

        public static EntityData GetEntityData(this Entity entity) {
            return entity.GetExtendedDataValue<EntityData>(EntityDataKey);
        }

        public static void SetEntityData(this Entity entity, EntityData entityData) {
            entity.SetExtendedDataValue(EntityDataKey, entityData);
        }

        public static void CopyEntityData(this Entity entity, Entity otherEntity) {
            if (otherEntity.GetEntityData() is EntityData data) {
                entity.SetEntityData(data);
            }
        }

        public static bool HasEntityId2(this Entity entity) {
            return entity.GetEntityId2() != default;
        }

        public static bool NoEntityId2(this Entity entity) {
            return !entity.HasEntityId2();
        }

        public static EntityId2 CreateEntityId2(this Entity entity, params string[] id) {
            if (entity.SceneAs<Level>() is Level level && level.Session?.Level != null) {
                return new EntityID(level.Session.Level, string.Join("-", id).GetHashCode()).ToEntityId2(entity);
            }

            return default;
        }
        
        public static Vector2 GetStartPosition(this Entity entity) {
            return entity.GetExtendedDataValue<Vector2>(StartPositionKey);
        }

        public static void SetStartPosition(this Entity entity, Vector2 startPosition) {
            entity.SetExtendedDataValue(StartPositionKey, startPosition);
        }
        
        public static void CopyStartPosition(this Entity entity, Entity otherEntity) {
            if (otherEntity.GetStartPosition() != default) {
                entity.SetStartPosition(otherEntity.GetStartPosition());
            }
        }

        public static Entity FindFirst(this Scene scene, EntityId2? entityId2) {
            if (entityId2 == null) return null;
            if (entityId2 == default(EntityId2)) return null;
            return scene.Entities.FirstOrDefault(e => e.GetEntityId2() == entityId2);
        }

        public static Dictionary<EntityId2, T> FindAllToDict<T>(this EntityList entityList) where T : Entity {
            Dictionary<EntityId2, T> result = new Dictionary<EntityId2, T>();
            List<T> findAll = entityList.FindAll<T>();
            foreach (T entity in findAll) {
                if (entity.TagCheck(Tags.Global)) continue;
                if (entity.NoEntityId2()) {
                    continue;
                }

                EntityId2 entityId2 = entity.GetEntityId2();
                if (result.ContainsKey(entityId2)) {
                    Logger.Log("SpeedrunTool", $"EntityId2 Duplication: {entityId2}");
                    continue;
                }

                result[entityId2] = entity;
            }

            return result;
        }

        public static Dictionary<EntityId2, Entity> FindAllToDict(this EntityList entityList, Type type,
            bool includeSubclass = false) {
            Dictionary<EntityId2, Entity> result = new Dictionary<EntityId2, Entity>();
            foreach (Entity entity in entityList) {
                if (entity.TagCheck(Tags.Global)) continue;
                if (entity.NoEntityId2()) continue;

                if (includeSubclass && entity.GetType().IsSameOrSubclassOf(type) ||
                    !includeSubclass && entity.GetType() == type) {
                    EntityId2 entityId2 = entity.GetEntityId2();
                    if (result.ContainsKey(entityId2)) {
                        Logger.Log("SpeedrunTool", $"EntityId2 Duplication: {entityId2}");
                        continue;
                    }

                    result[entityId2] = entity;
                }
            }

            return result;
        }

        public static Dictionary<EntityId2, T> FindAllToDict<T>(this Scene scene) where T : Entity {
            return FindAllToDict<T>(scene.Entities);
        }

        public static Dictionary<EntityId2, Entity> FindAllToDict(this Scene scene, Type type,
            bool includeSubclass = false) {
            return FindAllToDict(scene.Entities, type, includeSubclass);
        }
    }
}