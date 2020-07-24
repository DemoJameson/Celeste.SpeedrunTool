using System;
using System.Collections.Generic;
using System.Reflection;
using Monocle;
using MonoMod.Utils;

namespace Celeste.Mod.SpeedrunTool.Extensions {
    public static class TypeExtensions {
        public static bool IsSimple(this Type type) {
            return type.IsPrimitive ||
                   type.IsValueType &&
                   type.FullName != "Celeste.TriggerSpikes+SpikeInfo" && //SpikeInfo 里有 Entity 所以不能算做简单数据类型
                   type.FullName != "Celeste.Mod.Entities.TriggerSpikesOriginal+SpikeInfo" ||
                   type.IsEnum || type == typeof(string) ||
                   type == typeof(decimal) ||
                   type == typeof(object) ||
                   type.IsSameOrSubclassOf(typeof(Collider)) ||
                   type == typeof(LevelData)
                ;
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

        public static bool IsCompilerGenerated(this object obj) {
            return IsCompilerGenerated(obj.GetType());
        }

        public static bool IsCompilerGenerated(this Type type) {
            return type.Name.StartsWith("<");
            // return type.GetCustomAttribute<System.Runtime.CompilerServices.CompilerGeneratedAttribute>() != null;
        }

        public static bool IsProperty(this MemberInfo memberInfo) {
            return (memberInfo.MemberType & MemberTypes.Property) != 0;
        }

        public static bool IsField(this MemberInfo memberInfo) {
            return (memberInfo.MemberType & MemberTypes.Field) != 0;
        }

        public static bool IsType<T>(this object obj) {
            return obj.GetType() == typeof(T);
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
            return potentialDescendant.IsSubclassOf(potentialBase)
                   || potentialDescendant == potentialBase;
        }

        public static bool IsSameOrSuperclassOf(this Type potentialBase, Type potentialDescendant) {
            return potentialDescendant.IsSubclassOf(potentialBase)
                   || potentialBase == potentialDescendant;
        }

        public static object ForceCreateInstance(this object obj, string tag = "") {
            return ForceCreateInstance(obj.GetType(), tag);
        }

        public static object ForceCreateInstance(this Type type, string tag = "") {
            object newObject = null;
            try {
                // 具有空参构造函数的类型可以创建
                newObject = Activator.CreateInstance(type);
            } catch (Exception) {
                // try {
                //     newObject = FormatterServices.GetUninitializedObject(type);
                // } catch (Exception) {
                $"ForceCreateInstance Failed: {type} at {tag}".Log();
                // }
            }

            if (newObject != null) {
                $"ForceCreateInstance Success: {type} at {tag}".Log();
            }

            return newObject;
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
                } else {
                    return null;
                }
            }

            return fieldInfo;
        }

        private static PropertyInfo GetPropertyInfo(Type type, string name) {
            PropertyInfo perpertyInfo = type.GetExtendedDataValue<PropertyInfo>(name);
            if (perpertyInfo == null) {
                perpertyInfo = type.GetProperty(name,
                    BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
                if (perpertyInfo != null) {
                    type.SetExtendedDataValue(name, perpertyInfo);
                } else {
                    return null;
                }
            }

            return perpertyInfo;
        }

        public static object GetField(this object obj, string name) {
            return obj.GetField(obj.GetType(), name);
        }

        public static object GetField<T>(this T obj, string name) {
            return obj.GetField(typeof(T), name);
        }

        public static object GetField(this object obj, Type type, string name) {
            FieldInfo fieldInfo = GetFieldInfo(type, name);
            return fieldInfo?.GetValue(obj);
        }

        public static void SetField(this object obj, string name, object value) {
            obj.SetField(obj.GetType(), name, value);
        }

        public static void SetField<T>(this T obj, string name, object value) {
            obj.SetField(typeof(T), name, value);
        }

        public static void SetField(this object obj, Type type, string name, object value) {
            FieldInfo fieldInfo = GetFieldInfo(type, name);
            fieldInfo?.SetValue(obj, value);
        }

        public static void CopyFields(this object obj, object fromObj, params string[] names) {
            obj.CopyFields(obj.GetType(), fromObj, names);
        }

        public static void CopyFields<T>(this T obj, T fromObj, params string[] names) {
            obj.CopyFields(typeof(T), fromObj, names);
        }

        public static void CopyFields(this object obj, Type type, object fromObj, params string[] names) {
            foreach (string name in names)
                obj.SetField(type, name, fromObj.GetField(type, name));
        }

        public static object GetProperty(this object obj, string name) {
            return obj.GetProperty(obj.GetType(), name);
        }

        public static object GetProperty<T>(this T obj, string name) {
            return obj.GetProperty(typeof(T), name);
        }

        public static object GetProperty(this object obj, Type type, string name) {
            FastReflectionDelegate getterDelegate = type.GetExtendedDataValue<FastReflectionDelegate>("getter" + name);
            if (getterDelegate == null) {
                var method = GetPropertyInfo(type, name)?.GetGetMethod(true);
                if (method == null) return null;

                getterDelegate = method.GetFastDelegate();
                type.SetExtendedDataValue("getter" + name, getterDelegate);
            }

            return getterDelegate(obj);
        }

        public static void SetProperty(this object obj, string name, object value) {
            obj.SetProperty(obj.GetType(), name, value);
        }

        public static void SetProperty<T>(this T obj, string name, object value) {
            obj.SetProperty(typeof(T), name, value);
        }

        public static void SetProperty(this object obj, Type type, string name, object value) {
            FastReflectionDelegate setterDelegate = type.GetExtendedDataValue<FastReflectionDelegate>("setter" + name);
            if (setterDelegate == null) {
                var method = GetPropertyInfo(type, name)?.GetSetMethod(true);
                if (method == null) return;

                setterDelegate = method.GetFastDelegate();
                type.SetExtendedDataValue("setter" + name, setterDelegate);
            }

            setterDelegate(obj, value);
        }

        public static void CopyProperties(this object obj, object fromObj, params string[] names) {
            obj.CopyProperties(obj.GetType(), fromObj, names);
        }

        public static void CopyProperties<T>(this T obj, T fromObj, params string[] names) {
            obj.CopyProperties(typeof(T), fromObj, names);
        }

        public static void CopyProperties(this object obj, Type type, object fromObj, params string[] names) {
            foreach (string name in names) {
                obj.SetProperty(type, name, fromObj.GetProperty(type, name));
            }
        }

        public static object InvokeMethod(this object obj, string name, params object[] parameters) {
            return obj.InvokeMethod(obj.GetType(), name, parameters);
        }

        public static object InvokeMethod<T>(this T obj, string name, params object[] parameters) {
            return obj.InvokeMethod(typeof(T), name, parameters);
        }

        public static object InvokeMethod(this object obj, Type type, string name, params object[] parameters) {
            return GetMethodInfo(type, name)?.GetFastDelegate().Invoke(obj, parameters);
        }
    }
}