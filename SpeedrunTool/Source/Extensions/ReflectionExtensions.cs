using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Mono.Cecil.Cil;
using MonoMod.Utils;

namespace Celeste.Mod.SpeedrunTool.Extensions;

internal static class ReflectionExtensions {
    private const BindingFlags StaticInstanceAnyVisibility =
        BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;

    // ReSharper disable UnusedMember.Local
    private record struct MemberKey(Type Type, string Name) {
        public readonly Type Type = Type;
        public readonly string Name = Name;
    }

    private record struct MethodKey(Type Type, string Name, long Types) {
        public readonly Type Type = Type;
        public readonly string Name = Name;
        public readonly long Types = Types;
    }

    private record struct DelegateKey(Type Type, string Name, Type ReturnType) {
        public readonly Type Type = Type;
        public readonly string Name = Name;
        public readonly Type ReturnType = ReturnType;
    }

    private record struct ConstructorKey(Type Type, long Types) {
        public readonly Type Type = Type;
        public readonly long Types = Types;
    }
    // ReSharper restore UnusedMember.Local

    public delegate T GetDelegate<out T>(object instance);

    public delegate void SetDelegate<in T>(object instance, T value);

    private static readonly ConcurrentDictionary<MemberKey, FieldInfo> CachedFieldInfos = new();
    private static readonly ConcurrentDictionary<MemberKey, PropertyInfo> CachedPropertyInfos = new();
    private static readonly ConcurrentDictionary<MethodKey, MethodInfo> CachedMethodInfos = new();
    private static readonly ConcurrentDictionary<MethodKey, FastReflectionDelegate> CachedMethodDelegates = new();
    private static readonly ConcurrentDictionary<ConstructorKey, ConstructorInfo> CachedConstructorInfos = new();
    private static readonly ConcurrentDictionary<DelegateKey, Delegate> CachedFieldGetDelegates = new();
    private static readonly ConcurrentDictionary<DelegateKey, Delegate> CachedFieldSetDelegates = new();

    private static readonly object[] NoArg = { };
    private static readonly object[] NullArg = {null};
    private static readonly Type[] EmptyTypes = { };

    public static FieldInfo GetFieldInfo(this Type type, string name) {
        var key = new MemberKey(type, name);
        if (CachedFieldInfos.TryGetValue(key, out var result)) {
            return result;
        }

        do {
            result = type.GetField(name, StaticInstanceAnyVisibility);
        } while (result == null && (type = type.BaseType) != null);

        return CachedFieldInfos[key] = result;
    }

    public static PropertyInfo GetPropertyInfo(this Type type, string name) {
        var key = new MemberKey(type, name);
        if (CachedPropertyInfos.TryGetValue(key, out var result)) {
            return result;
        }

        do {
            result = type.GetProperty(name, StaticInstanceAnyVisibility);
        } while (result == null && (type = type.BaseType) != null);

        return CachedPropertyInfos[key] = result;
    }

    public static MethodInfo GetMethodInfo(this Type type, string name, Type[] types = null) {
        var key = new MethodKey(type, name, types.GetCustomHashCode());
        if (CachedMethodInfos.TryGetValue(key, out MethodInfo result)) {
            return result;
        }

        do {
            MethodInfo[] methodInfos = type.GetMethods(StaticInstanceAnyVisibility);
            result = methodInfos.FirstOrDefault(info =>
                info.Name == name && types?.SequenceEqual(info.GetParameters().Select(i => i.ParameterType)) != false);
        } while (result == null && (type = type.BaseType) != null);

        return CachedMethodInfos[key] = result;
    }

    public static FastReflectionDelegate GetMethodDelegate(this Type type, string name, Type[] types = null) {
        var key = new MethodKey(type, name, types.GetCustomHashCode());
        if (CachedMethodDelegates.TryGetValue(key, out var result)) {
            return result;
        }

        return CachedMethodDelegates[key] = type.GetMethodInfo(name, types)?.CreateFastDelegate();
    }
    
    public static ConstructorInfo GetConstructorInfo(this Type type, params Type[] types) {
        var key = new ConstructorKey(type, types.GetCustomHashCode());
        if (CachedConstructorInfos.TryGetValue(key, out ConstructorInfo result)) {
            return result;
        }

        ConstructorInfo[] constructors = type.GetConstructors(StaticInstanceAnyVisibility);
        result = constructors.FirstOrDefault(info => types.SequenceEqual(info.GetParameters().Select(i => i.ParameterType)));
        return CachedConstructorInfos[key] = result;
    }
    
    public static object GetFieldValue(this object obj, string name) {
        return GetFieldValueImpl<object>(obj, obj.GetType(), name);
    }

    public static object GetFieldValue(this Type type, string name) {
        return GetFieldValueImpl<object>(null, type, name);
    }

    public static T GetFieldValue<T>(this object obj, string name) {
        return GetFieldValueImpl<T>(obj, obj.GetType(), name);
    }

    public static T GetFieldValue<T>(this Type type, string name) {
        return GetFieldValueImpl<T>(null, type, name);
    }

    private static T GetFieldValueImpl<T>(object obj, Type type, string name) {
        object result = type.GetFieldGetDelegate<object>(name)?.Invoke(obj);
        if (result == null) {
            return default;
        } else {
            return (T)result;
        }
    }

    public static GetDelegate<TReturn> GetFieldGetDelegate<TReturn>(this Type type, string fieldName) {
        var key = new DelegateKey(type, fieldName, typeof(TReturn));
        if (CachedFieldGetDelegates.TryGetValue(key, out var result)) {
            return (GetDelegate<TReturn>)result;
        }

        return (GetDelegate<TReturn>)(CachedFieldGetDelegates[key] = type.GetFieldInfo(fieldName)?.CreateGetDelegate<TReturn>());
    }

    public static SetDelegate<TValue> GetFieldSetDelegate<TValue>(this Type type, string fieldName) {
        var key = new DelegateKey(type, fieldName, typeof(TValue));
        if (CachedFieldSetDelegates.TryGetValue(key, out var result)) {
            return (SetDelegate<TValue>)result;
        }

        return (SetDelegate<TValue>)(CachedFieldSetDelegates[key] = type.GetFieldInfo(fieldName)?.CreateSetDelegate<TValue>());
    }

    private static GetDelegate<TReturn> CreateGetDelegate<TReturn>(this FieldInfo field) {
        if (field == null) {
            throw new ArgumentException("Field cannot be null.", nameof(field));
        }

        Type returnType = typeof(TReturn);
        Type fieldType = field.FieldType;
        if (!returnType.IsAssignableFrom(fieldType)) {
            throw new InvalidCastException($"{field.Name} is of type {fieldType}, it cannot be assigned to the type {returnType}.");
        }

        if (field.IsConst()) {
            object value = field.GetValue(null);
            TReturn returnValue = value == null ? default : (TReturn)value;
            Func<object, TReturn> func = _ => returnValue;

            return (GetDelegate<TReturn>)func.Method.CreateDelegate(typeof(GetDelegate<TReturn>), func.Target);
        }

        using var method = new DynamicMethodDefinition($"{field} Getter", returnType, new[] {typeof(object)});
        var il = method.GetILProcessor();

        if (field.IsStatic) {
            il.Emit(OpCodes.Ldsfld, field);
        } else {
            il.Emit(OpCodes.Ldarg_0);
            if (field.DeclaringType.IsValueType) {
                il.Emit(OpCodes.Unbox_Any, field.DeclaringType);
            }

            il.Emit(OpCodes.Ldfld, field);
        }

        if (fieldType.IsValueType && !returnType.IsValueType) {
            il.Emit(OpCodes.Box, fieldType);
        }

        il.Emit(OpCodes.Ret);

        return (GetDelegate<TReturn>)method.Generate().CreateDelegate(typeof(GetDelegate<TReturn>));
    }

    private static SetDelegate<TValue> CreateSetDelegate<TValue>(this FieldInfo field) {
        if (field == null) {
            throw new ArgumentException("Field cannot be null.", nameof(field));
        }

        Type parameterType = typeof(TValue);
        Type fieldType = field.FieldType;
        if (parameterType != typeof(object) && !fieldType.IsAssignableFrom(parameterType)) {
            throw new InvalidCastException($"{field.Name} is of type {fieldType}. An instance of {parameterType} cannot be assigned to it.");
        }

        if (field.IsConst()) {
            throw new FieldAccessException($"Unable to set constant field {field.Name} of type {field.DeclaringType.FullName}.");
        }

        using var method = new DynamicMethodDefinition($"{field} Setter", typeof(void), new[] {typeof(object), parameterType});
        var il = method.GetILProcessor();

        if (!field.IsStatic) {
            il.Emit(OpCodes.Ldarg_0);
        }

        il.Emit(OpCodes.Ldarg_1);

        if (!fieldType.IsValueType && parameterType.IsValueType) {
            il.Emit(OpCodes.Box, parameterType);
        } else if (fieldType.IsValueType && !parameterType.IsValueType) {
            il.Emit(OpCodes.Unbox_Any, fieldType);
        }

        il.Emit(!field.IsStatic ? OpCodes.Stfld : OpCodes.Stsfld, field);

        il.Emit(OpCodes.Ret);

        return (SetDelegate<TValue>)method.Generate().CreateDelegate(typeof(SetDelegate<TValue>));
    }


    public static void SetFieldValue(this object obj, string name, object value) {
        SetFieldValueImpl(obj, obj.GetType(), name, value);
    }

    public static void SetFieldValue(this Type type, string name, object value) {
        SetFieldValueImpl(null, type, name, value);
    }

    private static void SetFieldValueImpl(object obj, Type type, string name, object value) {
        type.GetFieldSetDelegate<object>(name)?.Invoke(obj, value);
    }

    public static object GetPropertyValue(this object obj, string name) {
        return GetPropertyValueImpl(obj, obj.GetType(), name);
    }

    public static object GetPropertyValue(this Type type, string name) {
        return GetPropertyValueImpl(null, type, name);
    }

    private static object GetPropertyValueImpl(object obj, Type type, string name) {
        return GetPropertyInfo(type, name)?.GetValue(obj, NoArg);
    }

    public static void SetPropertyValue(this object obj, string name, object value) {
        SetPropertyValueImpl(obj, obj.GetType(), name, value);
    }

    public static void SetPropertyValue(this Type type, string name, object value) {
        SetPropertyValueImpl(null, type, name, value);
    }

    private static void SetPropertyValueImpl(object obj, Type type, string name, object value) {
        GetPropertyInfo(type, name)?.SetValue(obj, value);
    }

    public static object InvokeMethod(this object obj, string name, params object[] parameters) {
        return InvokeMethodImpl(obj, obj.GetType(), name, parameters);
    }

    public static object InvokeMethod(this Type type, string name, params object[] parameters) {
        return InvokeMethodImpl(null, type, name, parameters);
    }

    private static object InvokeMethodImpl(this object obj, Type type, string name, params object[] parameters) {
        parameters ??= NullArg;
        return GetMethodDelegate(type, name)?.Invoke(obj, parameters);
    }

    public static object InvokeOverloadedMethod(this object obj, string name, Type[] types = null, params object[] parameters) {
        return InvokeOverloadedMethodImpl(obj, obj.GetType(), name, types, parameters);
    }

    public static object InvokeOverloadedMethod(this Type type, string name, Type[] types = null, params object[] parameters) {
        return InvokeOverloadedMethodImpl(null, type, name, types, parameters);
    }

    private static object InvokeOverloadedMethodImpl(object obj, Type type, string name, Type[] types = null, params object[] parameters) {
        types ??= EmptyTypes;
        parameters ??= NullArg;
        return GetMethodDelegate(type, name, types)?.Invoke(obj, types, parameters);
    }
}

internal static class HashCodeExtensions {
    public static long GetCustomHashCode<T>(this IEnumerable<T> enumerable) {
        if (enumerable == null) {
            return 0;
        }

        unchecked {
            long hash = 17;
            foreach (T item in enumerable) {
                hash = hash * -1521134295 + EqualityComparer<T>.Default.GetHashCode(item);
            }

            return hash;
        }
    }
}