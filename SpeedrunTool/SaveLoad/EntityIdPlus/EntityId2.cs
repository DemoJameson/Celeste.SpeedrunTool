using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Celeste.Mod.SpeedrunTool.Extensions;
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
            RoomName = roomName ?? "";
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

    internal static class EntityId2Extension {
        private static readonly HashSet<Type> ExcludeTypes = new HashSet<Type> {
            // 装饰
            typeof(Decal),
            typeof(ParticleSystem),
            typeof(WaterSurface),
            
            // 对话
            typeof(MiniTextbox),
        };
        
        private const string EntityId2Key = "SpeedrunTool-EntityId2-Key";
        private const string EntityDataKey = "SpeedrunTool-EntityData-Key";
        private const string EntityStartPositionKey = "SpeedrunTool-Entity-StartPosition-Key";

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
            return new EntityID(entityData.Level?.Name, entityData.ID);
        }

        public static EntityId2 GetEntityId2(this Entity entity) {
            return entity.GetExtendedDataValue<EntityId2>(EntityId2Key);
        }

        public static void SetEntityId2(this Entity entity, EntityId2 entityId2, bool @override = true) {
            Type type = entity.GetType();
            if(ExcludeTypes.Contains(type)) return;
            if (type.Assembly == Assembly.GetExecutingAssembly()) return;
            
            if (@override || entity.NoEntityId2()) {
                entity.SetExtendedDataValue(EntityId2Key, entityId2);
            }
        }
        public static void SetEntityId2(this Entity entity, IEnumerable<object> id, bool @override = true) {
            List<string> sid = id.Select(obj => {
                if (obj == null) return "null";

                if (obj is Entity e && e.HasEntityId2()) {
                    return e.GetEntityId2().ToString();
                }

                if (obj is Component component && component.Entity.HasEntityId2()) {
                    return component.Entity.GetEntityId2().ToString();
                }

                if (obj.GetType().IsArray && obj.GetType().GetArrayRank() == 1 && obj is Array array) {
                    string result = "[";
                    for (int i = 0; i < array.Length; i++) {
                        if (i > 0) {
                            result += ", ";
                        }
                        result += array.GetValue(i).ToString();
                    }
                    return result + "]";
                }

                return obj.ToString();
            }).ToList();
            entity.SetEntityId2(string.Join(", ", sid), @override);
        }

        public static void SetEntityId2(this Entity entity, EntityID entityId, bool @override = true) {
            entity.SetEntityId2(entityId.ToEntityId2(entity.GetType()), @override);
        }
        
        public static void SetEntityId2(this Entity entity, string sid, bool @override = true) {
            entity.SetEntityId2(new EntityId2(Engine.Scene.GetSession()?.Level, sid, entity.GetType()), @override);
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

        public static Entity FindFirst(this Scene scene, EntityId2? entityId2) {
            if (entityId2 == null) return null;
            if (entityId2 == default(EntityId2)) return null;
            return scene.Entities.FirstOrDefault(e => e.GetEntityId2() == entityId2);
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