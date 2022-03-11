using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Microsoft.Xna.Framework.Graphics;

namespace Celeste.Mod.SpeedrunTool.Extensions; 

internal static class ReflectionExtensions {
    private const BindingFlags InstanceAnyVisibility = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
    private const BindingFlags InstanceAnyVisibilityDeclaredOnly = InstanceAnyVisibility | BindingFlags.DeclaredOnly;
    private const BindingFlags StaticInstanceAnyVisibility = InstanceAnyVisibility | BindingFlags.Static;

    private static readonly object[] NoArg = { };

    private static readonly HashSet<Type> SimpleTypes = new() {
        typeof(byte), typeof(short), typeof(ushort), typeof(int), typeof(uint), typeof(long), typeof(ulong),
        typeof(float), typeof(double), typeof(decimal), typeof(char), typeof(string), typeof(bool),
        typeof(DateTime), typeof(TimeSpan), typeof(DateTimeOffset), typeof(Vector2), typeof(Vector3),
        typeof(VertexPositionColor), typeof(Color)
    };

    public static bool IsSimple(this Type type, Func<Type, bool> extraTypes = null) {
        if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(Nullable<>)) {
            // nullable type, check if the nested type is simple.
            return IsSimple(type.GetGenericArguments()[0], extraTypes);
        }

        return SimpleTypes.Contains(type) || type.IsEnum || extraTypes?.Invoke(type) == true;
    }

    public static bool IsSimpleClass(this Type type, Func<Type, bool> extraGenericTypes = null) {
        return IsSimple(type, extraGenericTypes)
               || IsSimpleArray(type, extraGenericTypes)
               || IsSimpleList(type, extraGenericTypes)
               || IsSimpleStack(type, extraGenericTypes)
               || IsSimpleHashSet(type, extraGenericTypes)
               || IsSimpleDictionary(type, extraGenericTypes);
    }

    public static void CopyAllSimpleTypeFieldsAndNull(this object to, object from) {
        if (to.GetType() != from.GetType()) {
            throw new ArgumentException("object to and from not the same type");
        }

        foreach (FieldInfo fieldInfo in to.GetType().GetAllFieldInfos(InstanceAnyVisibilityDeclaredOnly)) {
            object fromValue = fieldInfo.GetValue(from);
            if (fromValue == null) {
                fieldInfo.SetValue(to, null);
            } else if (fieldInfo.FieldType.IsSimple()) {
                fieldInfo.SetValue(to, fromValue);
            }
        }
    }

    public static bool IsSimpleArray(this Type type, Func<Type, bool> extraGenericTypes = null) {
        return type.IsSingleRankArray() && type.GetElementType().IsSimple(extraGenericTypes);
    }

    public static bool IsSimpleList(this Type type, Func<Type, bool> extraGenericTypes = null) {
        return type.IsList(out Type genericType) && genericType.IsSimple(extraGenericTypes);
    }

    public static bool IsSimpleStack(this Type type, Func<Type, bool> extraGenericTypes = null) {
        return type.IsStack(out Type genericType) && genericType.IsSimple(extraGenericTypes);
    }

    public static bool IsSimpleHashSet(this Type type, Func<Type, bool> extraGenericTypes = null) {
        return type.IsHashSet(out Type genericType) && genericType.IsSimple(extraGenericTypes);
    }

    public static bool IsSimpleDictionary(this Type type, Func<Type, bool> extraGenericTypes = null) {
        return type.IsDictionary(out Type keyType, out Type valueType) && keyType.IsSimple(extraGenericTypes) &&
               valueType.IsSimple(extraGenericTypes);
    }

    public static bool IsSimpleWeakReference(this Type type, Func<Type, bool> extraGenericTypes = null) {
        return type.IsWeakReference(out Type genericType) && genericType.IsSimple(extraGenericTypes);
    }

    public static bool IsSingleRankArray(this Type type) {
        return type.IsArray && type.GetArrayRank() == 1;
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

    public static bool IsIDictionary(this Type type, out Type keyType, out Type valueType) {
        bool result = type.IsGenericType && type.GetInterfaces()
                                             .Any(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IDictionary<,>))
                                         && type.GenericTypeArguments.Length == 2;

        keyType = result ? type.GenericTypeArguments[0] : null;
        valueType = result ? type.GenericTypeArguments[1] : null;

        return result;
    }

    public static bool IsWeakReference(this Type type, out Type genericType) {
        bool result = type.IsGenericType && type.GetGenericTypeDefinition().IsAssignableFrom(typeof(WeakReference<>))
                                         && type.GenericTypeArguments.Length == 1;

        genericType = result ? type.GenericTypeArguments[0] : null;

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

    public static bool IsOverride(this MethodInfo methodInfo) {
        return methodInfo.GetBaseDefinition().DeclaringType != methodInfo.DeclaringType;
    }

    public static string ToBackingField(this string fieldName) {
        return $"<{fieldName}>k__BackingField";
    }

    public static MethodInfo GetMethodInfo(this Type type, string name, BindingFlags bindingFlags = StaticInstanceAnyVisibility) {
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

    public static FieldInfo GetFieldInfo(this Type type, string name, BindingFlags bindingFlags = StaticInstanceAnyVisibility) {
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

    public static FieldInfo[] GetFieldInfos(this Type type, BindingFlags bindingFlags = StaticInstanceAnyVisibility,
        bool filterBackingField = false) {
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

    public static List<FieldInfo> GetAllFieldInfos(this Type type, BindingFlags bindingFlags = StaticInstanceAnyVisibility,
        bool filterBackingField = false) {
        string key = $"ReflectionExtensions-GetAllFieldInfos-{bindingFlags}-{filterBackingField}";
        List<FieldInfo> result = type.GetExtendedDataValue<List<FieldInfo>>(key);
        if (result == null) {
            result = new List<FieldInfo>();
            while (type != null && type.IsSubclassOf(typeof(object))) {
                FieldInfo[] fieldInfos = type.GetFieldInfos(bindingFlags, filterBackingField);
                foreach (FieldInfo fieldInfo in fieldInfos) {
                    if (result.Contains(fieldInfo)) {
                        continue;
                    }

                    result.Add(fieldInfo);
                }

                type = type.BaseType;
            }

            type.SetExtendedDataValue(key, result);
        }

        return result;
    }

    public static PropertyInfo GetPropertyInfo(this Type type, string name, BindingFlags bindingFlags = StaticInstanceAnyVisibility) {
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

    public static PropertyInfo[] GetPropertyInfos(this Type type, BindingFlags bindingFlags = StaticInstanceAnyVisibility) {
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

    public static object GetFieldValue<T>(this T obj, string name) where T : class {
        return obj.GetFieldValue(typeof(T), name);
    }

    public static object GetFieldValue(this Type type, string name) {
        return GetFieldValue(null, type, name);
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

    public static void SetFieldValue(this Type type, string name, object value) {
        SetFieldValue(null, type, name, value);
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

    public static object GetPropertyValue(this Type type, string name) {
        return GetPropertyValue(null, type, name);
    }

    public static object GetPropertyValue(this object obj, Type type, string name) {
        return GetPropertyGetMethod(type, name)?.CreateFastDelegate().Invoke(obj, NoArg);
    }

    public static void SetPropertyValue(this object obj, string name, object value) {
        obj.SetPropertyValue(obj.GetType(), name, value);
    }

    public static void SetPropertyValue<T>(this T obj, string name, object value) {
        obj.SetPropertyValue(typeof(T), name, value);
    }

    public static void SetPropertyValue(this Type type, string name, object value) {
        SetPropertyValue(null, type, name, value);
    }

    public static void SetPropertyValue(this object obj, Type type, string name, object value) {
        GetPropertySetMethod(type, name)?.CreateFastDelegate().Invoke(obj, value);
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

    public static object InvokeMethod(this Type type, string name, params object[] parameters) {
        return InvokeMethod(null, type, name, parameters);
    }

    public static object InvokeMethod(this object obj, Type type, string name, params object[] parameters) {
        return GetMethodInfo(type, name)?.CreateFastDelegate().Invoke(obj, parameters);
    }
}