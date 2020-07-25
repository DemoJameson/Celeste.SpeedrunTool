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
            new EntityId2("You can do it. —— 《Celeste》", "2018-01-25", typeof(Player));


        public readonly string RoomName;
        public readonly string SID;
        public readonly Type Type;

        public EntityId2(string roomName, string sid, Type type) {
            RoomName = roomName ?? Engine.Scene.GetSession()?.Level ?? "";
            SID = sid ?? "";
            Type = type;

            if (!type.IsSameOrSubclassOf(typeof(Entity))) {
                throw new ArgumentException("type must be Entity");
            }
        }
        
        public EntityId2(EntityID entityId, Type type): this(entityId.Level, entityId.ID.ToString(), type) {}

        public override bool Equals(object obj) {
            return obj is EntityId2 entityId2 && entityId2.RoomName == RoomName && entityId2.SID == SID && entityId2.Type == Type;
        }

        public override int GetHashCode() {
            return ToString().GetHashCode();
        }

        public override string ToString() {
            return $"EntityId2: Type={Type.FullName} RoomName={RoomName} SID={SID}";
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
        private const string EntityStartPositionKey = "SpeedrunTool_Entity_StartPosition_Key";

        public static EntityId2 ToEntityId2(this EntityID entityId, Type type) {
            return new EntityId2(entityId.Level, entityId.ID.ToString(), type);
        }

        public static EntityId2 ToEntityId2(this EntityData entityData, Type type) {
            return new EntityId2(entityData.ToEntityId(), type);
        }

        public static EntityId2 ToEntityId2(this EntityData entityData, Entity entity) {
            return entityData.ToEntityId2(entity.GetType());
        }

        public static EntityID ToEntityId(this EntityData entityData) {
            return new EntityID(entityData.Level?.Name ?? Engine.Scene.GetSession()?.Level ?? "", entityData.ID);
        }

        public static EntityId2 GetEntityId2(this Entity entity) {
            return entity.GetExtendedDataValue<EntityId2>(EntityId2Key);
        }

        public static void SetEntityId2(this Entity entity, EntityId2 entityId2, bool @override = true) {
            if (@override || entity.NoEntityId2()) {
                entity.SetExtendedDataValue(EntityId2Key, entityId2);
            }
        }
        public static void SetEntityId2(this Entity entity, IEnumerable<object> id, bool @override = true) {
            List<string> sid = id.Select(obj => {
                if (obj is Entity e && e.HasEntityId2()) {
                    return e.GetEntityId2().ToString();
                }

                return obj?.ToString() ?? "null";
            }).ToList();
            entity.SetEntityId2(null, string.Join(", ", sid), @override);
        }

        public static void SetEntityId2(this Entity entity, EntityID entityId, bool @override = true) {
            entity.SetEntityId2(entityId.ToEntityId2(entity.GetType()), @override);
        }
        
        public static void SetEntityId2(this Entity entity, string roomName, string sid, bool @override = true) {
            entity.SetEntityId2(new EntityId2(roomName, sid, entity.GetType()), @override);
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
        
        public static bool IsSidEmpty(this Entity entity) {
            return entity.NoEntityId2() || string.IsNullOrEmpty(entity.GetEntityId2().SID);
        }

        public static Vector2 GetStartPosition(this Entity entity) {
            return entity.GetExtendedDataValue<Vector2>(EntityStartPositionKey);
        }

        public static void SetStartPosition(this Entity entity, Vector2 startPosition) {
            entity.SetExtendedDataValue(EntityStartPositionKey, startPosition);
        }

        public static void CopyStartPosition(this Entity entity, Entity otherEntity) {
            if (otherEntity.GetStartPosition() != default) {
                entity.SetStartPosition(otherEntity.GetStartPosition());
            }
        }

        public static Entity FindFirst(this Scene scene, EntityId2? entityId2) {
            if (entityId2 == null) return null;
            if (entityId2 == default(EntityId2)) return null;
            Entity entity = scene.Entities.FirstOrDefault(e => e.GetEntityId2() == entityId2);

            if (entity == null) {
                $"Can't find entity in scene: {entityId2}".DebugLog();
            }

            return entity;
        }

        public static Dictionary<EntityId2, T> FindAllToDict<T>(this EntityList entityList, out List<T> duplicateIdList)
            where T : Entity {
            Dictionary<EntityId2, T> result = new Dictionary<EntityId2, T>();
            duplicateIdList = new List<T>();

            List<T> findAll = entityList.FindAll<T>();
            foreach (T entity in findAll) {
                if (entity.IsGlobalButExcludeSomeTypes()) continue;
                if (entity.NoEntityId2()) continue;

                EntityId2 entityId2 = entity.GetEntityId2();
                if (result.ContainsKey(entityId2)) {
                    $"EntityId2 Duplication: {entityId2}".DebugLog();
                    duplicateIdList.Add(entity);
                    continue;
                }

                result[entityId2] = entity;
            }

            return result;
        }

        public static Dictionary<EntityId2, T> FindAllToDict<T>(this Scene scene, out List<T> duplicateIdList)
            where T : Entity {
            return FindAllToDict(scene.Entities, out duplicateIdList);
        }

        public static Dictionary<EntityId2, T> FindAllToDict<T>(this Scene scene) where T : Entity {
            return FindAllToDict(scene.Entities, out List<T> _);
        }
    }
}