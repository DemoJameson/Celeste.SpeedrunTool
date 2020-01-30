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

        public static TValue GetValueOrDefault<TKey, TValue>
        (this IDictionary<TKey, TValue> dictionary,
            TKey key,
            TValue defaultValue) {
            if (dictionary.ContainsKey(key)) {
                return dictionary[key];
            }

            return defaultValue;
        }

        private static MethodInfo GetMethodInfo(object obj, string name) {
            Type type = obj.GetType();
            MethodInfo methodInfo = type.GetExtendedDataValue<MethodInfo>(name);
            if (methodInfo == null) {
                methodInfo = type.GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
                if (methodInfo != null) {
                    type.SetExtendedDataValue(name, methodInfo);
                }
            }

            return methodInfo;
        }

        private static FieldInfo GetFieldInfo(this object obj, string name) {
            Type type = obj.GetType();
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
        
        public static object GetPrivateField(this object obj, string name) {
            FieldInfo fieldInfo = GetFieldInfo(obj, name);
            return fieldInfo?.GetValue(obj);
        }

        public static void SetPrivateField(this object obj, string name, object value) {
            FieldInfo fieldInfo = GetFieldInfo(obj, name);
            fieldInfo?.SetValue(obj, value);
        }

        public static void CopyPrivateField(this object obj, string name, object fromObj) {
            obj.SetPrivateField(name, fromObj.GetPrivateField(name));
        }

        public static object GetPrivateProperty(this object obj, string name) {
            Type type = obj.GetType();
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

        public static void SetPrivateProperty(this object obj, string name, object value) {
            Type type = obj.GetType();
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

        public static object InvokePrivateMethod(this object obj, string name, params object[] parameters) {
            return GetMethodInfo(obj, name)?.Invoke(obj, parameters);
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