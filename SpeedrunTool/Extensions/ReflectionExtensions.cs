using System;
using System.Collections.Generic;
using System.Reflection;
using Fasterflect;
using Monocle;

namespace Celeste.Mod.SpeedrunTool.Extensions {
    public static class ReflectionExtensions {
        private const BindingFlags instanceFlags = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
        
        private enum MemberType {
            Field,
            Property
        }

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
            return potentialDescendant.IsSubclassOf(potentialBase) || potentialDescendant == potentialBase;
        }

        public static bool IsSameOrBaseclassOf(this Type potentialBase, Type potentialDescendant) {
            return potentialDescendant.IsSubclassOf(potentialBase) || potentialBase == potentialDescendant;
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
                methodInfo = type.GetMethod(name, instanceFlags);
                if (methodInfo != null) {
                    type.SetExtendedDataValue(name, methodInfo);
                }
            }

            return methodInfo;
        }

        private static FieldInfo GetFieldInfo(Type type, string name) {
            FieldInfo fieldInfo = type.GetExtendedDataValue<FieldInfo>(name);
            if (fieldInfo == null) {
                fieldInfo = type.GetField(name, instanceFlags);
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
                perpertyInfo = type.GetProperty(name, instanceFlags);
                if (perpertyInfo != null) {
                    type.SetExtendedDataValue(name, perpertyInfo);
                } else {
                    return null;
                }
            }

            return perpertyInfo;
        }

        private static MemberGetter GetMemberGetter(Type type, string name, MemberType memberType) {
            string key = $"MemberGetter_{name}";

            MemberGetter memberGetter = type.GetExtendedDataValue<MemberGetter>(key);
            if (memberGetter == null) {
                memberGetter = memberType == MemberType.Field
                    ? type.DelegateForGetFieldValue(name, instanceFlags)
                    : type.DelegateForGetPropertyValue(name, instanceFlags);
                type.SetExtendedDataValue(key, memberGetter);
            }

            return memberGetter;
        }

        private static MemberSetter GetMemberSetter(Type type, string name, MemberType memberType) {
            string key = $"MemberSetter_{name}";

            MemberSetter memberSetter = type.GetExtendedDataValue<MemberSetter>(key);
            if (memberSetter == null) {
                memberSetter = memberType == MemberType.Field
                    ? type.DelegateForSetFieldValue(name, instanceFlags)
                    : type.DelegateForSetPropertyValue(name, instanceFlags);
                type.SetExtendedDataValue(key, memberSetter);
            }

            return memberSetter;
        }

        public static object GetField(this object obj, string name) {
            return obj.GetField(obj.GetType(), name);
        }

        public static object GetField<T>(this T obj, string name) {
            return obj.GetField(typeof(T), name);
        }

        public static object GetField(this object obj, Type type, string name) {
            return GetMemberGetter(type, name, MemberType.Field)?.Invoke(obj);
        }

        public static void SetField(this object obj, string name, object value) {
            obj.SetField(obj.GetType(), name, value);
        }

        public static void SetField<T>(this T obj, string name, object value) {
            obj.SetField(typeof(T), name, value);
        }

        public static void SetField(this object obj, Type type, string name, object value) {
            GetMemberSetter(type, name, MemberType.Field)?.Invoke(obj, value);
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
            return GetMemberGetter(type, name, MemberType.Property)?.Invoke(obj);
        }

        public static void SetProperty(this object obj, string name, object value) {
            obj.SetProperty(obj.GetType(), name, value);
        }

        public static void SetProperty<T>(this T obj, string name, object value) {
            obj.SetProperty(typeof(T), name, value);
        }

        public static void SetProperty(this object obj, Type type, string name, object value) {
            GetMemberSetter(type, name, MemberType.Property)?.Invoke(obj, value);
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
            string key = $"MethodInvoker_{name}";

            MethodInvoker methodInvoker = type.GetExtendedDataValue<MethodInvoker>(key);
            if (methodInvoker == null) {
                $"{type}; {name}".DebugLog();
                methodInvoker = GetMethodInfo(type, name).DelegateForCallMethod();
                type.SetExtendedDataValue(key, methodInvoker);
            }

            return methodInvoker?.Invoke(obj, parameters);
        }
    }
}