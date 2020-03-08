using System;
using System.Collections.Generic;
using System.IO;
using System.Linq.Expressions;
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

        // deep clone an object using YAML (de)serialization.
        public static T DeepCloneYAML<T>(this object obj, Type type) {
            string yaml = YamlHelper.Serializer.Serialize(obj);
            return (T) YamlHelper.Deserializer.Deserialize(yaml, type);
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

        private static MethodInfo GetMethodInfo(Type type, string name) {
            MethodInfo methodInfo = type.GetExtendedDataValue<MethodInfo>(name);
            if (methodInfo == null) {
                methodInfo = type.GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
                if (methodInfo != null) {
                    type.SetExtendedDataValue(name, methodInfo);
                }
            }

            return methodInfo;
        }

        private static FieldInfo GetFieldInfo(Type type, string name) {
            FieldInfo fieldInfo = type.GetExtendedDataValue<FieldInfo>(name);
            if (fieldInfo == null) {
                fieldInfo = type.GetField(name, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
                if (fieldInfo != null) {
                    type.SetExtendedDataValue(name, fieldInfo);
                }
                else {
                    return null;
                }
            }

            return fieldInfo;
        }

        public static object GetField(this object obj, string name) {
            return obj.GetField(obj.GetType(), name);
        }

        public static object GetField(this object obj, Type type, string name) {
            FieldInfo fieldInfo = GetFieldInfo(type, name);
            return fieldInfo?.GetValue(obj);
        }

        public static void SetField(this object obj, string name, object value) {
            obj.SetField(obj.GetType(), name, value);
        }

        public static void SetField(this object obj, Type type, string name, object value) {
            FieldInfo fieldInfo = GetFieldInfo(type, name);
            fieldInfo?.SetValue(obj, value);
        }

        public static void CopyField(this object obj, string name, object fromObj) {
            obj.SetField(name, fromObj.GetField(name));
        }

        public static void CopyField(this object obj, Type type, string name, object fromObj) {
            obj.SetField(type, name, fromObj.GetField(type, name));
        }

        public static object GetProperty(this object obj, string name) {
            return obj.GetProperty(obj.GetType(), name);
        }

        public static object GetProperty(this object obj, Type type, string name) {
            Func<object, object> getter = type.GetExtendedDataValue<Func<object, object>>("getter" + name);
            if (getter == null) {
                var propertyInfo = type.GetProperty(name, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
                if (propertyInfo == null) {
                    return null;
                }

                var method = propertyInfo.GetGetMethod(true);
                var exprObj = Expression.Parameter(typeof(object), "instance");

                Expression<Func<object, object>> expr =
                    Expression.Lambda<Func<object, object>>(
                        Expression.Convert(
                            Expression.Call(
                                Expression.Convert(exprObj, method.DeclaringType),
                                method),
                            typeof(object)),
                        exprObj);

                getter = expr.Compile();
                type.SetExtendedDataValue("getter" + name, getter);
            }

            return getter(obj);
        }

        public static void SetProperty(this object obj, string name, object value) {
            obj.SetProperty(obj.GetType(), name, value);
        }

        public static void SetProperty(this object obj, Type type, string name, object value) {
            Action<object, object> setter = type.GetExtendedDataValue<Action<object, object>>("setter" + name);
            if (setter == null) {
                var propertyInfo = type.GetProperty(name, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
                if (propertyInfo == null) {
                    return;
                }

                var method = propertyInfo.GetSetMethod(true);
                if (method == null) {
                    return;
                }
                
                var exprObj = Expression.Parameter(typeof(object), "obj");
                var exprValue = Expression.Parameter(typeof(object), "value");

                Expression<Action<object, object>> expr =
                    Expression.Lambda<Action<object, object>>(
                        Expression.Call(
                            Expression.Convert(exprObj, method.DeclaringType),
                            method,
                            Expression.Convert(exprValue, method.GetParameters()[0].ParameterType)),
                        exprObj,
                        exprValue);
                setter = expr.Compile();
                type.SetExtendedDataValue("setter" + name, setter);
            }

            setter(obj, value);
        }

        public static object InvokeMethod(this object obj, string name, params object[] parameters) {
            return obj.InvokeMethod(obj.GetType(), name, parameters);
        }

        public static object InvokeMethod(this object obj, Type type, string name, params object[] parameters) {
            return GetMethodInfo(type, name)?.Invoke(obj, parameters);
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

        public static bool GetExtendedBoolean(this object o, string name) {
            return GetExtendedDataValue<bool>(o, name);
        }
        
        public static void SetExtendedBoolean(this object o, string name, bool value) {
            SetExtendedDataValue(o, name, value);
        }
        
        public static T With<T>(this T item, Action<T> action) {
            action(item);
            return item;
        }
    }
}