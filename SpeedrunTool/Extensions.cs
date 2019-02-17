using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Serialization.Formatters.Binary;
using Monocle;

namespace Celeste.Mod.SpeedrunTool
{
    public static class Extensions
    {
        private static readonly ConditionalWeakTable<object, object> extendedData =
            new ConditionalWeakTable<object, object>();

        private static readonly string EntityIdKey = "EntityId";

        public static T DeepClone<T>(this T obj)
        {
            using (MemoryStream ms = new MemoryStream())
            {
                BinaryFormatter formatter = new BinaryFormatter();
                formatter.Serialize(ms, obj);
                ms.Position = 0;

                return (T) formatter.Deserialize(ms);
            }
        }

        public static TValue GetValueOrDefault<TKey, TValue>
        (this IDictionary<TKey, TValue> dictionary,
            TKey key,
            TValue defaultValue)
        {
            if (dictionary.ContainsKey(key))
                return dictionary[key];
            return defaultValue;
        }

        public static object GetPrivateField(this object obj, string name)
        {
            return obj.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(obj);
        }

        public static void SetPrivateField(this object obj, string name, object value)
        {
            obj.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)?.SetValue(obj, value);
        }

        public static void CopyPrivateField(this object obj, string name, object fromObj)
        {
            obj.SetPrivateField(name, fromObj.GetPrivateField(name));
        }

        public static object GetPrivateProperty(this object obj, string name)
        {
            return obj.GetType().GetProperty(name, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)
                ?.GetValue(obj);
        }

        public static void SetPrivateProperty(this object obj, string name, object value)
        {
            obj.GetType().GetProperty(name, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)
                ?.SetValue(obj, value);
        }

        public static MethodInfo GetPrivateMethod(this object obj, string name)
        {
            return obj.GetType().GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic);
        }

        public static object InvokePrivateMethod(this object obj, string methodName, params object[] parameters)
        {
            return obj.GetType().GetMethod(methodName, BindingFlags.Instance | BindingFlags.NonPublic)
                ?.Invoke(obj, parameters);
        }

        public static void AddToTracker(this Type type)
        {
            if (!Tracker.StoredEntityTypes.Contains(type)) Tracker.StoredEntityTypes.Add(type);

            if (!Tracker.TrackedEntityTypes.ContainsKey(type))
                Tracker.TrackedEntityTypes[type] = new List<Type> {type};
            else if (!Tracker.TrackedEntityTypes[type].Contains(type)) Tracker.TrackedEntityTypes[type].Add(type);
        }

        // from https://stackoverflow.com/a/17264480
        internal static IDictionary<string, object> CreateDictionary(object o)
        {
            return new Dictionary<string, object>();
        }

        public static void SetExtendedDataValue(this object o, string name, object value)
        {
            if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("Invalid name");
            name = name.Trim();

            IDictionary<string, object> values =
                (IDictionary<string, object>) extendedData.GetValue(o, CreateDictionary);

            if (value != null)
                values[name] = value;
            else
                values.Remove(name);
        }

        public static T GetExtendedDataValue<T>(this object o, string name)
        {
            if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("Invalid name");
            name = name.Trim();

            IDictionary<string, object> values =
                (IDictionary<string, object>) extendedData.GetValue(o, CreateDictionary);

            if (values.ContainsKey(name))
                return (T) values[name];

            return default(T);
        }

        public static void SetEntityId(this Entity entity, EntityID entityId)
        {
            entity.SetExtendedDataValue(EntityIdKey, entityId);
        }

        public static void SetEntityId(this Entity entity, EntityData entityData)
        {
            entity.SetExtendedDataValue(EntityIdKey, entityData.ToEntityId());
        }

        public static EntityID GetEntityId(this Entity entity)
        {
            return entity.GetExtendedDataValue<EntityID>(EntityIdKey);
        }

        public static EntityID ToEntityId(this EntityData entityData)
        {
            // 因为 ID 有可能重复，所以加上起点坐标的信息
            return new EntityID(entityData.Level.Name + entityData.Name,
                entityData.ID + entityData.Position.GetHashCode());
        }

        public static IEnumerable<T> GetCastEntities<T>(this Tracker tracker) where T : Entity
        {
            return tracker.GetEntities<T>().Cast<T>();
        }

        public static Dictionary<EntityID, T> GetDictionary<T>(this Tracker tracker) where T : Entity
        {
            Dictionary<EntityID, T> result = new Dictionary<EntityID, T>();
            foreach (T entity in tracker.GetCastEntities<T>())
            {
                EntityID entityId = entity.GetEntityId();
                if (entityId.Equals(default(EntityID)) || result.ContainsKey(entityId))
                    continue;

                result[entityId] = entity;
            }

            return result;
        }

        public static void UpdateEntities<T>(this Level level) where T : Entity
        {
            level.Tracker.GetEntities<T>().ForEach(entity => entity.Update());
        }

        public static void SetTime(this SoundSource soundSource, int time)
        {
            object eventInstance = soundSource.GetPrivateField("instance");
            eventInstance.GetType().GetMethod("setTimelinePosition").Invoke(eventInstance, new object[] {time});
        }

        public static void CopyFrom(this Tween tween, Tween otherTween)
        {
            tween.SetPrivateProperty("TimeLeft", otherTween.TimeLeft);
            tween.SetPrivateProperty("Reverse", otherTween.Reverse);
        }
    }
}