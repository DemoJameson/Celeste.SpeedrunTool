using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Microsoft.Xna.Framework.Graphics;

namespace Celeste.Mod.SpeedrunTool.Extensions;

public static class ReflectionExtensions {
    private const BindingFlags StaticInstanceAnyVisibility = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;

    private static readonly Dictionary<int, FieldInfo> CachedFieldInfos = new();
    private static readonly Dictionary<int, PropertyInfo> CachedPropertyInfos = new();
    private static readonly Dictionary<int, MethodInfo> CachedMethodInfos = new();
    private static readonly Dictionary<int, MethodInfo> CachedGetMethodInfos = new();
    private static readonly Dictionary<int, MethodInfo> CachedSetMethodInfos = new();

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

    public static FieldInfo GetFieldInfo(this Type type, string name) {
        int key = type.CombineHashCode(name);
        if (CachedFieldInfos.TryGetValue(key, out var result)) {
            return result;
        }

        return CachedFieldInfos[key] = type.GetField(name, StaticInstanceAnyVisibility);
    }

    public static PropertyInfo GetPropertyInfo(this Type type, string name) {
        int key = type.CombineHashCode(name);
        if (CachedPropertyInfos.TryGetValue(key, out var result)) {
            return result;
        }

        return CachedPropertyInfos[key] = type.GetProperty(name, StaticInstanceAnyVisibility);
    }

    public static MethodInfo GetPropertyGetMethod(this Type type, string name) {
        int key = type.CombineHashCode(name);
        if (CachedGetMethodInfos.TryGetValue(key, out var result)) {
            return result;
        }

        return CachedGetMethodInfos[key] = type.GetPropertyInfo(name)?.GetGetMethod(true);
    }

    public static MethodInfo GetPropertySetMethod(this Type type, string name) {
        int key = type.CombineHashCode(name);
        if (CachedSetMethodInfos.TryGetValue(key, out var result)) {
            return result;
        }

        return CachedSetMethodInfos[key] = type.GetPropertyInfo(name)?.GetSetMethod(true);
    }

    public static MethodInfo GetMethodInfo(this Type type, string name) {
        int key = type.CombineHashCode(name);
        if (CachedMethodInfos.TryGetValue(key, out var result)) {
            return result;
        }

        return CachedMethodInfos[key] = type.GetMethod(name, StaticInstanceAnyVisibility);
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

internal static class HashCodeExtensions {
    public static int GetCustomHashCode<T>(this IEnumerable<T> enumerable) {
        if (enumerable == null) {
            return 0;
        }

        unchecked {
            int hash = 17;
            foreach (T item in enumerable) {
                hash = hash * -1521134295 + EqualityComparer<T>.Default.GetHashCode(item);
            }

            return hash;
        }
    }

    public static int CombineHashCode<T1, T2>(this T1 t1, T2 t2) {
        unchecked {
            return EqualityComparer<T1>.Default.GetHashCode(t1) * -1521134295 + EqualityComparer<T2>.Default.GetHashCode(t2);
        }
    }
}