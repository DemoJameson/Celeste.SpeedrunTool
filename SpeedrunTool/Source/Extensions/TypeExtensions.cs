using System.Collections.Generic;
using System.Reflection;
using Microsoft.Xna.Framework.Graphics;

namespace Celeste.Mod.SpeedrunTool.Extensions;

internal static class TypeExtensions {
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
               || IsSimpleDictionary(type, extraGenericTypes)
               || IsSimpleWeakReference(type, extraGenericTypes);
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
        genericType = null;

        if (!type.IsGenericType || type.IsGenericTypeDefinition || !type.IsClass) {
            return false;
        }

        Type[] genericTypeArguments = type.GetGenericArguments();

        if (genericTypeArguments.Length != 1) {
            return false;
        }

        if (type.GetGenericTypeDefinition().IsAssignableFrom(typeof(HashSet<>))) {
            genericType = genericTypeArguments[0];
            return true;
        }

        return false;
    }

    public static bool IsDictionary(this Type type, out Type keyType, out Type valueType) {
        bool result = type.IsGenericType && type.GetGenericTypeDefinition().IsAssignableFrom(typeof(Dictionary<,>))
                                         && type.GenericTypeArguments.Length == 2;

        keyType = result ? type.GenericTypeArguments[0] : null;
        valueType = result ? type.GenericTypeArguments[1] : null;

        return result;
    }

    public static bool IsIDictionary(this Type type, out Type keyType, out Type valueType) {
        keyType = null;
        valueType = null;

        if (!type.IsGenericType || type.IsGenericTypeDefinition || !type.IsClass) {
            return false;
        }

        Type[] genericTypeArguments = type.GetGenericArguments();

        if (genericTypeArguments.Length != 2) {
            return false;
        } else {
            foreach (Type @interface in type.GetInterfaces()) {
                if (@interface.IsGenericType && @interface.GetGenericTypeDefinition() == typeof(IDictionary<,>)) {
                    keyType = genericTypeArguments[0];
                    valueType = genericTypeArguments[1];
                    return true;
                }
            }
        }

        return false;
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

    public static bool IsConst(this FieldInfo fieldInfo) {
        return fieldInfo.IsLiteral && !fieldInfo.IsInitOnly;
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
}