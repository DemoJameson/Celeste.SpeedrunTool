using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Microsoft.Xna.Framework;

namespace Celeste.Mod.SpeedrunTool.Extensions {
    internal static class ReflectionExtensions {
        private static readonly object[] NoArg = {};
        private static readonly HashSet<Type> SimpleTypes = new HashSet<Type> {
            typeof(byte), typeof(short), typeof(ushort), typeof(int), typeof(uint), typeof(long), typeof(ulong),
            typeof(float), typeof(double), typeof(decimal), typeof(char), typeof(string), typeof(bool),
            typeof(DateTime), typeof(TimeSpan), typeof(DateTimeOffset), typeof(Vector2)
        };


        private const BindingFlags InstanceAnyVisibility = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
        private const BindingFlags InstanceAnyVisibilityDeclaredOnly = InstanceAnyVisibility | BindingFlags.DeclaredOnly;
        private const BindingFlags StaticInstanceAnyVisibility = InstanceAnyVisibility | BindingFlags.Static;

        public static bool IsSimple(this Type type) {
            if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(Nullable<>)) {
                // nullable type, check if the nested type is simple.
                return IsSimple(type.GetGenericArguments()[0]);
            }

            return SimpleTypes.Contains(type) || type.IsEnum;
        }

        public static void CopyAllSimpleTypeFieldsAndNull(this object to, object from) {
            if (to.GetType() != from.GetType()) throw new ArgumentException("object to and from not the same type");

            foreach (FieldInfo fieldInfo in to.GetType().GetAllFieldInfos()) {
                object fromValue = fieldInfo.GetValue(from);
                if (fromValue == null) {
                    fieldInfo.SetValue(to, null);
                } else if (fieldInfo.FieldType.IsSimple()) {
                    fieldInfo.SetValue(to, fromValue);
                }
            }
        }

        public static bool IsSingleRankArray(this Type type) {
            return type.IsArray && type.GetArrayRank() == 1;
        }

        public static bool IsSimpleArray(this Type type) {
            return type.IsSingleRankArray() && type.GetElementType().IsSimple();
        }

        public static bool IsSimpleList(this Type type) {
            return type.IsList(out Type genericType) && genericType.IsSimple();
        }

        public static bool IsList(this Type type, out Type genericType) {
            bool result = type.IsGenericType && type.GetGenericTypeDefinition().IsAssignableFrom(typeof(List<>))
                                             && type.GenericTypeArguments.Length == 1;

            genericType = result ? type.GenericTypeArguments[0] : null;

            return result;
        }

        public static bool IsStack(this Type type, out Type genericType) {
            bool result = type.IsGenericType && type.GetGenericTypeDefinition().IsAssignableFrom(typeof(Stack<>))
                                             && type.GenericTypeArguments.Length == 1;

            genericType = result ? type.GenericTypeArguments[0] : null;

            return result;
        }

        public static bool IsHashSet(this Type type, out Type genericType) {
            bool result = type.IsGenericType && type.GetGenericTypeDefinition().IsAssignableFrom(typeof(HashSet<>))
                                             && type.GenericTypeArguments.Length == 1;

            genericType = result ? type.GenericTypeArguments[0] : null;

            return result;
        }

        public static bool IsDictionary(this Type type, out Type keyType, out Type valueType) {
            bool result = type.IsGenericType && type.GetGenericTypeDefinition().IsAssignableFrom(typeof(Dictionary<,>))
                                             && type.GenericTypeArguments.Length == 2;

            keyType = result ? type.GenericTypeArguments[0] : null;
            valueType = result ? type.GenericTypeArguments[1] : null;

            return result;
        }

        public static bool IsCompilerGenerated(this object obj) {
            return IsCompilerGenerated(obj.GetType());
        }

        public static bool IsCompilerGenerated(this Type type) {
            return type.Name.StartsWith("<");
        }

        public static bool IsProperty(this MemberInfo memberInfo) {
            return (memberInfo.MemberType & MemberTypes.Property) != 0;
        }

        public static bool IsField(this MemberInfo memberInfo) {
            return (memberInfo.MemberType & MemberTypes.Field) != 0;
        }

        public static bool IsType<T>(this object obj) {
            return obj?.GetType() == typeof(T);
        }

        public static bool IsType<T>(this Type type) {
            return type == typeof(T);
        }

        public static bool IsNotType<T>(this object obj) {
            return !obj.IsType<T>();
        }

        public static bool IsNotType<T>(this Type type) {
            return !type.IsType<T>();
        }

        public static bool IsSameOrSubclassOf(this Type potentialDescendant, Type potentialBase) {
            return potentialDescendant.IsSubclassOf(potentialBase) || potentialDescendant == potentialBase;
        }

        public static bool IsSameOrBaseclassOf(this Type potentialBase, Type potentialDescendant) {
            return potentialDescendant.IsSubclassOf(potentialBase) || potentialBase == potentialDescendant;
        }

        public static MethodInfo GetMethodInfo(Type type, string name, BindingFlags bindingFlags = InstanceAnyVisibility) {
            string key = $"ReflectionExtensions-GetMethodInfo-{name}-{bindingFlags}";

            MethodInfo methodInfo = type.GetExtendedDataValue<MethodInfo>(key);
            if (methodInfo == null) {
                methodInfo = type.GetMethod(name, bindingFlags);
                if (methodInfo != null) {
                    type.SetExtendedDataValue(key, methodInfo);
                }
            }

            return methodInfo;
        }

        public static FieldInfo GetFieldInfo(Type type, string name, BindingFlags bindingFlags = InstanceAnyVisibility) {
            string key = $"ReflectionExtensions-GetFieldInfo-{name}-{bindingFlags}";
            FieldInfo fieldInfo = type.GetExtendedDataValue<FieldInfo>(key);
            if (fieldInfo == null) {
                fieldInfo = type.GetField(name, bindingFlags);
                if (fieldInfo != null) {
                    type.SetExtendedDataValue(key, fieldInfo);
                } else {
                    return null;
                }
            }

            return fieldInfo;
        }

        public static FieldInfo[] GetFieldInfos(this Type type, BindingFlags bindingFlags = InstanceAnyVisibility, bool filterBackingField = false) {
            string key = $"ReflectionExtensions-GetFieldInfos-{bindingFlags}-{filterBackingField}";

            FieldInfo[] fieldInfos = type.GetExtendedDataValue<FieldInfo[]>(key);
            if (fieldInfos == null) {
                fieldInfos = type.GetFields(bindingFlags);
                if (filterBackingField) {
                    fieldInfos = fieldInfos.Where(info => !info.Name.EndsWith("k__BackingField")).ToArray();
                }

                type.SetExtendedDataValue(key, fieldInfos);
            }

            return fieldInfos;
        }

        public static List<FieldInfo> GetAllFieldInfos(this Type type, BindingFlags bindingFlags = InstanceAnyVisibilityDeclaredOnly,
            bool filterBackingField = false) {
            List<FieldInfo> result = new List<FieldInfo>();
            while (type != null && type.IsSubclassOf(typeof(object))) {
                var fieldInfos = type.GetFieldInfos(bindingFlags, filterBackingField);
                foreach (FieldInfo fieldInfo in fieldInfos) {
                    if (result.Contains(fieldInfo)) continue;
                    result.Add(fieldInfo);
                }

                type = type.BaseType;
            }

            return result;
        }

        public static PropertyInfo GetPropertyInfo(this Type type, string name, BindingFlags bindingFlags = InstanceAnyVisibility) {
            string key = $"ReflectionExtensions-GetPropertyInfo-{name}-{bindingFlags}";
            PropertyInfo propertyInfo = type.GetExtendedDataValue<PropertyInfo>(key);
            if (propertyInfo == null) {
                propertyInfo = type.GetProperty(name, bindingFlags);
                if (propertyInfo != null) {
                    type.SetExtendedDataValue(key, propertyInfo);
                } else {
                    return null;
                }
            }

            return propertyInfo;
        }

        public static PropertyInfo[] GetPropertyInfos(this Type type, BindingFlags bindingFlags = InstanceAnyVisibility) {
            string key = $"ReflectionExtensions-GetPropertyInfos-{bindingFlags}";

            PropertyInfo[] propertyInfos = type.GetExtendedDataValue<PropertyInfo[]>(key);
            if (propertyInfos == null) {
                propertyInfos = type.GetProperties(bindingFlags);
                type.SetExtendedDataValue(key, propertyInfos);
            }

            return propertyInfos;
        }

        public static MethodInfo GetPropertyGetMethod(this Type type, string name) {
            string key = $"ReflectionExtensions-GetPropertyGetMethod-{name}";

            MethodInfo methodInfo = type.GetExtendedDataValue<MethodInfo>(key);
            if (methodInfo == null) {
                methodInfo = type.GetPropertyInfo(name)?.GetGetMethod(true);
                type.SetExtendedDataValue(key, methodInfo);
            }

            return methodInfo;
        }

        public static MethodInfo GetPropertySetMethod(this Type type, string name) {
            string key = $"ReflectionExtensions-GetPropertySetMethod-{name}";

            MethodInfo methodInfo = type.GetExtendedDataValue<MethodInfo>(key);
            if (methodInfo == null) {
                methodInfo = type.GetPropertyInfo(name)?.GetSetMethod(true);
                type.SetExtendedDataValue(key, methodInfo);
            }

            return methodInfo;
        }

        public static object GetFieldValue(this object obj, string name) {
            return obj.GetFieldValue(obj.GetType(), name);
        }

        public static object GetFieldValue<T>(this T obj, string name) {
            return obj.GetFieldValue(typeof(T), name);
        }

        public static object GetFieldValue(this object obj, Type type, string name) {
            return GetFieldInfo(type, name)?.GetValue(obj);
        }

        public static void SetFieldValue(this object obj, string name, object value) {
            obj.SetFieldValue(obj.GetType(), name, value);
        }

        public static void SetFieldValue<T>(this T obj, string name, object value) {
            obj.SetFieldValue(typeof(T), name, value);
        }

        public static void SetFieldValue(this object obj, Type type, string name, object value) {
            GetFieldInfo(type, name)?.SetValue(obj, value);
        }

        public static void CopyFieldValue(this object obj, object fromObj, params string[] names) {
            obj.CopyFieldValue(obj.GetType(), fromObj, names);
        }

        public static void CopyFieldValue<T>(this T obj, T fromObj, params string[] names) {
            obj.CopyFieldValue(typeof(T), fromObj, names);
        }

        public static void CopyFieldValue(this object obj, Type type, object fromObj, params string[] names) {
            foreach (string name in names) {
                obj.SetFieldValue(type, name, fromObj.GetFieldValue(type, name));
            }
        }

        public static object GetPropertyValue(this object obj, string name) {
            return obj.GetPropertyValue(obj.GetType(), name);
        }

        public static object GetPropertyValue<T>(this T obj, string name) {
            return obj.GetPropertyValue(typeof(T), name);
        }

        public static object GetPropertyValue(this object obj, Type type, string name) {
            return GetPropertyGetMethod(type, name)?.Invoke(obj, NoArg);
        }

        public static void SetPropertyValue(this object obj, string name, object value) {
            obj.SetPropertyValue(obj.GetType(), name, value);
        }

        public static void SetPropertyValue<T>(this T obj, string name, object value) {
            obj.SetPropertyValue(typeof(T), name, value);
        }

        public static void SetPropertyValue(this object obj, Type type, string name, object value) {
            GetPropertySetMethod(type, name)?.Invoke(obj, new []{value});
        }

        public static void CopyPropertyValue(this object obj, object fromObj, params string[] names) {
            obj.CopyPropertyValue(obj.GetType(), fromObj, names);
        }

        public static void CopyPropertyValue<T>(this T obj, T fromObj, params string[] names) {
            obj.CopyPropertyValue(typeof(T), fromObj, names);
        }

        public static void CopyPropertyValue(this object obj, Type type, object fromObj, params string[] names) {
            foreach (string name in names) {
                obj.SetPropertyValue(type, name, fromObj.GetPropertyValue(type, name));
            }
        }

        public static object InvokeMethod(this object obj, string name, params object[] parameters) {
            return obj.InvokeMethod(obj.GetType(), name, parameters);
        }

        public static object InvokeMethod<T>(this T obj, string name, params object[] parameters) {
            return obj.InvokeMethod(typeof(T), name, parameters);
        }

        public static object InvokeMethod(this object obj, Type type, string name, params object[] parameters) {
            return GetMethodInfo(type, name)?.Invoke(obj, parameters);
        }
    }
}