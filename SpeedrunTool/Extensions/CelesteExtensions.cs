using System;
using System.Collections.Generic;
using System.Linq;
using Monocle;

namespace Celeste.Mod.SpeedrunTool.Extensions {
    public static class CelesteExtensions {
        private const string EntityIdKey = "EntityId";

        public static void AddToTracker(this Type type) {
            if (!Tracker.StoredEntityTypes.Contains(type)) {
                Tracker.StoredEntityTypes.Add(type);
            }

            if (!Tracker.TrackedEntityTypes.ContainsKey(type)) {
                Tracker.TrackedEntityTypes[type] = new List<Type> {type};
            }
            else if (!Tracker.TrackedEntityTypes[type].Contains(type)) {
                Tracker.TrackedEntityTypes[type].Add(type);
            }
        }

        public static void SetEntityId(this Entity entity, EntityID entityId) {
            entity.SetExtendedDataValue(EntityIdKey, entityId);
        }

        public static void SetEntityId(this Entity entity, EntityData entityData) {
            entity.SetExtendedDataValue(EntityIdKey, entityData.ToEntityId());
        }

        public static EntityID GetEntityId(this Entity entity) {
            return entity.GetExtendedDataValue<EntityID>(EntityIdKey);
        }

        public static EntityID ToEntityId(this EntityData entityData) {
            // 因为 ID 有可能重复，所以加上起点坐标的信息
            return new EntityID(entityData.Level.Name + entityData.Name,
                entityData.ID + entityData.Position.GetHashCode());
        }

        public static IEnumerable<T> GetCastEntities<T>(this Tracker tracker) where T : Entity {
            return tracker.GetEntities<T>().Cast<T>();
        }

        public static Dictionary<EntityID, T> GetDictionary<T>(this Tracker tracker) where T : Entity {
            Dictionary<EntityID, T> result = new Dictionary<EntityID, T>();
            foreach (T entity in tracker.GetCastEntities<T>()) {
                EntityID entityId = entity.GetEntityId();
                if (entityId.Equals(default(EntityID)) || result.ContainsKey(entityId)) {
                    continue;
                }

                result[entityId] = entity;
            }

            return result;
        }

        public static void UpdateEntities<T>(this Level level) where T : Entity {
            level.Tracker.GetEntities<T>().ForEach(entity => entity.Update());
        }

        public static void SetTime(this SoundSource soundSource, int time) {
            object eventInstance = soundSource.GetPrivateField("instance");
            eventInstance.GetType().GetMethod("setTimelinePosition")?.Invoke(eventInstance, new object[] {time});
        }

        public static void CopyFrom(this Tween tween, Tween otherTween) {
            tween.SetPrivateProperty("TimeLeft", otherTween.TimeLeft);
            tween.SetPrivateProperty("Reverse", otherTween.Reverse);
        }

        public static void AddRange<T>(this Dictionary<EntityID, T> dict, IEnumerable<T> entities) where T : Entity {
            foreach (T entity in entities) {
                EntityID entityId = entity.GetEntityId();
                if (!dict.ContainsKey(entityId)) {
                    dict[entityId] = entity;
                }
            }
        }
    }
}