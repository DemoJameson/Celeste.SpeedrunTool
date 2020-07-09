using System;
using System.Collections.Generic;
using System.Linq.Expressions;
using System.Reflection;

namespace Celeste.Mod.SpeedrunTool.Extensions {
    public static class TypeExtensions {
        public static bool IsSimple(this Type type) {
            return type.IsPrimitive || 
                   type.IsValueType && type.FullName != "Celeste.TriggerSpikes+SpikeInfo" &&
                   type.FullName != "Celeste.Mod.Entities.TriggerSpikesOriginal+SpikeInfo" ||
                   type.IsEnum || type == typeof(string) ||
                   type == typeof(decimal);
        }

        public static bool IsList(this Type type, out Type genericType) {
            bool result = type.IsGenericType && type.GetGenericTypeDefinition().IsAssignableFrom(typeof(List<>))
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

        public static bool IsSameOrSubclassOf(this Type potentialDescendant, Type potentialBase) {
            return potentialDescendant.IsSubclassOf(potentialBase)
                   || potentialDescendant == potentialBase;
        }

        public static bool IsSameOrSuperclassOf(this Type potentialBase, Type potentialDescendant) {
            return potentialDescendant.IsSubclassOf(potentialBase)
                   || potentialBase == potentialDescendant;
        }

        public static object ForceCreateInstance(this Type type, string Tag = "") {
            object newObject = null;
            try {
                // 具有空参构造函数的类型可以创建
                newObject = Activator.CreateInstance(type);
            } catch (Exception) {
                type.DebugLog("Activator.CreateInstance Failed:", Tag);
            }

            if (newObject != null) {
                "ForceCreateInstance Success".DebugLog(type, Tag);
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

        public static object GetField(this object obj, string name) {
            return obj.GetField(obj.GetType(), name);
        }

        public static object GetField(this object obj, Type type, string name) {
            FieldInfo fieldInfo = GetFieldInfo(type, name);
            return fieldInfo?.GetValue(obj);
        }

        public static object GetField<T>(this T obj, string name) {
            FieldInfo fieldInfo = GetFieldInfo(typeof(T), name);
            return fieldInfo?.GetValue(obj);
        }

        public static void SetField(this object obj, string name, object value) {
            obj.SetField(obj.GetType(), name, value);
        }

        public static void SetField(this object obj, Type type, string name, object value) {
            FieldInfo fieldInfo = GetFieldInfo(type, name);
            fieldInfo?.SetValue(obj, value);
        }

        public static void SetField<T>(this T obj, string name, object value) {
            FieldInfo fieldInfo = GetFieldInfo(typeof(T), name);
            fieldInfo?.SetValue(obj, value);
        }

        public static void CopyFields(this object obj, object fromObj, params string[] names) {
            foreach (string name in names)
                obj.SetField(name, fromObj.GetField(name));
        }

        public static void CopyFields(this object obj, Type type, object fromObj, params string[] names) {
            foreach (string name in names)
                obj.SetField(type, name, fromObj.GetField(type, name));
        }

        public static void CopyFields<T>(this T obj, T fromObj, params string[] names) {
            foreach (string name in names)
                obj.SetField(name, fromObj.GetField(name));
        }

        public static object GetProperty(this object obj, string name) {
            return obj.GetProperty(obj.GetType(), name);
        }

        public static object GetProperty<T>(this T obj, string name) {
            return obj.GetProperty(typeof(T), name);
        }

        public static object GetProperty(this object obj, Type type, string name) {
            Func<object, object> getter = type.GetExtendedDataValue<Func<object, object>>("getter" + name);
            if (getter == null) {
                var propertyInfo = type.GetProperty(name,
                    BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
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

        public static void SetProperty<T>(this T obj, string name, object value) {
            obj.SetProperty(typeof(T), name, value);
        }

        public static void SetProperty(this object obj, Type type, string name, object value) {
            Action<object, object> setter = type.GetExtendedDataValue<Action<object, object>>("setter" + name);
            if (setter == null) {
                var propertyInfo = type.GetProperty(name,
                    BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
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

        public static void CopyProperties(this object obj, object fromObj, params string[] names) {
            foreach (string name in names)
                obj.SetProperty(name, fromObj.GetProperty(name));
        }

        public static void CopyProperties(this object obj, Type type, object fromObj, params string[] names) {
            foreach (string name in names)
                obj.SetProperty(type, name, fromObj.GetProperty(type, name));
        }

        public static void CopyProperties<T>(this T obj, T fromObj, params string[] names) {
            foreach (string name in names)
                obj.SetProperty(name, fromObj.GetProperty(name));
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