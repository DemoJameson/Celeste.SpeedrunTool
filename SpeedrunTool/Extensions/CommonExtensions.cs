using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Serialization.Formatters.Binary;

namespace Celeste.Mod.SpeedrunTool.Extensions {
    public static class CommonExtensions {
        private static readonly ConditionalWeakTable<object, object> ExtendedData =
            new ConditionalWeakTable<object, object>();

        public static T DeepClone<T>(this T obj) {
            using (MemoryStream ms = new MemoryStream()) {
                BinaryFormatter formatter = new BinaryFormatter();
                formatter.Serialize(ms, obj);
                ms.Position = 0;

                return (T) formatter.Deserialize(ms);
            }
        }

        public static TValue GetValueOrDefault<TKey, TValue>
        (this IDictionary<TKey, TValue> dictionary,
            TKey key,
            TValue defaultValue) {
            if (dictionary.ContainsKey(key)) {
                return dictionary[key];
            }

            return defaultValue;
        }

        public static object GetPrivateField(this object obj, string name) {
            return obj.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)?.GetValue(obj);
        }

        public static void SetPrivateField(this object obj, string name, object value) {
            obj.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)?.SetValue(obj, value);
        }

        public static void CopyPrivateField(this object obj, string name, object fromObj) {
            obj.SetPrivateField(name, fromObj.GetPrivateField(name));
        }

        public static object GetPrivateProperty(this object obj, string name) {
            return obj.GetType().GetProperty(name, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)
                ?.GetValue(obj);
        }

        public static void SetPrivateProperty(this object obj, string name, object value) {
            obj.GetType().GetProperty(name, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)
                ?.SetValue(obj, value);
        }

        public static MethodInfo GetPrivateMethod(this object obj, string name) {
            return obj.GetType().GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic);
        }

        public static object InvokePrivateMethod(this object obj, string methodName, params object[] parameters) {
            return obj.GetType().GetMethod(methodName, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)
                ?.Invoke(obj, parameters);
        }

        // from https://stackoverflow.com/a/17264480
        internal static IDictionary<string, object> CreateDictionary(object o) {
            return new Dictionary<string, object>();
        }

        public static void SetExtendedDataValue(this object o, string name, object value) {
            if (string.IsNullOrWhiteSpace(name)) {
                throw new ArgumentException("Invalid name");
            }

            name = name.Trim();

            IDictionary<string, object> values =
                (IDictionary<string, object>) ExtendedData.GetValue(o, CreateDictionary);

            if (value != null) {
                values[name] = value;
            }
            else {
                values.Remove(name);
            }
        }

        public static T GetExtendedDataValue<T>(this object o, string name) {
            if (string.IsNullOrWhiteSpace(name)) {
                throw new ArgumentException("Invalid name");
            }

            name = name.Trim();

            IDictionary<string, object> values =
                (IDictionary<string, object>) ExtendedData.GetValue(o, CreateDictionary);

            if (values.ContainsKey(name)) {
                return (T) values[name];
            }

            return default(T);
        }

        public static T With<T>(this T item, Action<T> action)
        {
            action(item);
            return item;
        }
    }
}