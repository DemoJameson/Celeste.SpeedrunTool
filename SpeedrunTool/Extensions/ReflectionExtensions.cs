using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Fasterflect;
using Monocle;

namespace Celeste.Mod.SpeedrunTool.Extensions {
    internal static class ReflectionExtensions {
        private const BindingFlags InstanceAnyVisibility =
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;

        private const BindingFlags InstanceAnyVisibilityDeclaredOnly =
            InstanceAnyVisibility | BindingFlags.DeclaredOnly;

        public static bool IsSimple(this Type type) {
            if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(Nullable<>)) {
                // nullable type, check if the nested type is simple.
                return IsSimple(type.GetGenericArguments()[0]);
            }

            return type.IsPrimitive
                   || type.IsEnum
                   || type == typeof(string)
                   || type == typeof(decimal)
                   || type.IsValueType
                   && type.FullName != "Celeste.TriggerSpikes+SpikeInfo" // SpikeInfo 里有 Entity 所以不能算做简单数据类型
                   && type.FullName != "Celeste.Mod.Entities.TriggerSpikesOriginal+SpikeInfo"
                   || type == typeof(object) // Coroutine
                   || type == typeof(MapData)
                   || type == typeof(AreaData)
                   || type == typeof(LevelData)
                   || type == typeof(EntityData)
                   || type == typeof(DecalData)
                   || type == typeof(Type)
                   || type.FullName != null && type.FullName.StartsWith("System.Reflection")
                ;
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

        public static bool IsSimpleReference(this Type type) {
            if (type.IsSimple()) return true;

            // 常见非简单引用类型，先排除
            if (type.IsSameOrSubclassOf(typeof(Scene))
                || type.IsSameOrSubclassOf(typeof(Entity))
                || type.IsSameOrSubclassOf(typeof(Component))
                || type.IsSameOrSubclassOf(typeof(Collide))
                || type.IsSameOrSubclassOf(typeof(ComponentList))
                || type.IsSameOrSubclassOf(typeof(EntityList))
                || typeof(Delegate).IsAssignableFrom(type.BaseType)
                || type.IsArray
            ) return false;

            // TODO 现在只做了简单的判断，引用了其他简单的引用类型也会被判断为复杂类型
            var allFieldTypes = type.GetAllFieldTypes(InstanceAnyVisibilityDeclaredOnly);
            return allFieldTypes.All(fieldType =>
                fieldType.IsSimple()
                || fieldType.IsSameOrBaseclassOf(type)
                || fieldType.IsSimpleArray()
                || fieldType.IsSimpleList()
                || fieldType.IsArray && fieldType.GetArrayRank() ==1 && type.IsSameOrSubclassOf(fieldType.GetElementType())
                || fieldType.IsList(out Type genericType) && type.IsSameOrSubclassOf(genericType)
            );
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

        public static object ForceCreateInstance(this object obj, string tag = "") {
            return ForceCreateInstance(obj.GetType(), tag);
        }

        public static object ForceCreateInstance(this Type type, string tag = "") {
            object newObject = null;
            try {
                // 具有空参构造函数的类型可以创建
                newObject = Activator.CreateInstance(type);
            } catch (Exception) {
                $"ForceCreateInstance Failed: {type} at {tag}".Log();
            }

            if (newObject != null) {
                $"ForceCreateInstance Success: {type} at {tag}".DebugLog();
            }

            return newObject;
        }

        private static MethodInfo GetMethodInfo(Type type, string name) {
            string key = $"GetMethodInfo-{name}";

            MethodInfo methodInfo = type.GetExtendedDataValue<MethodInfo>(key);
            if (methodInfo == null) {
                methodInfo = type.GetMethod(name, InstanceAnyVisibility);
                if (methodInfo != null) {
                    type.SetExtendedDataValue(key, methodInfo);
                }
            }

            return methodInfo;
        }

        private static FieldInfo GetFieldInfo(Type type, string name) {
            string key = $"GetFieldInfo-{name}";
            FieldInfo fieldInfo = type.GetExtendedDataValue<FieldInfo>(key);
            if (fieldInfo == null) {
                fieldInfo = type.GetField(name, InstanceAnyVisibility);
                if (fieldInfo != null) {
                    type.SetExtendedDataValue(key, fieldInfo);
                } else {
                    return null;
                }
            }

            return fieldInfo;
        }

        public static FieldInfo[] GetFieldInfos(this Type type, BindingFlags bindingFlags,
            bool filterBackingField = false) {
            string key = $"GetFieldInfos-{bindingFlags}-{filterBackingField}";

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

        public static HashSet<Type> GetAllFieldTypes(this Type type, BindingFlags bindingFlags,
            bool filterBackingField = false) {
            HashSet<Type> result = new HashSet<Type>();
            while (type.IsSubclassOf(typeof(object))) {
                var fieldTypes = type.GetFieldInfos(bindingFlags, filterBackingField).Select(info => info.FieldType);
                foreach (Type fieldType in fieldTypes) {
                    if (result.Contains(fieldType)) continue;
                    result.Add(fieldType);
                }

                type = type.BaseType;
            }

            return result;
        }

        private static PropertyInfo GetPropertyInfo(Type type, string name) {
            string key = $"GetPropertyInfo-{name}";
            PropertyInfo propertyInfo = type.GetExtendedDataValue<PropertyInfo>(key);
            if (propertyInfo == null) {
                propertyInfo = type.GetProperty(name, InstanceAnyVisibility);
                if (propertyInfo != null) {
                    type.SetExtendedDataValue(key, propertyInfo);
                } else {
                    return null;
                }
            }

            return propertyInfo;
        }

        public static PropertyInfo[] GetPropertyInfos(this Type type, BindingFlags bindingFlags) {
            string key = $"GetPropertyInfos-{bindingFlags}";

            PropertyInfo[] propertyInfos = type.GetExtendedDataValue<PropertyInfo[]>(key);
            if (propertyInfos == null) {
                propertyInfos = type.GetProperties(bindingFlags);
                type.SetExtendedDataValue(key, propertyInfos);
            }

            return propertyInfos;
        }

        private static MemberGetter GetMemberGetter(Type type, string name) {
            string key = $"GetMemberGetter-{name}";

            MemberGetter memberGetter = type.GetExtendedDataValue<MemberGetter>(key);
            if (memberGetter == null) {
                memberGetter = Reflect.Getter(type, name, InstanceAnyVisibility);
                type.SetExtendedDataValue(key, memberGetter);
            }

            return memberGetter;
        }

        private static MemberSetter GetMemberSetter(Type type, string name) {
            string key = $"GetMemberSetter-{name}";

            MemberSetter memberSetter = type.GetExtendedDataValue<MemberSetter>(key);
            if (memberSetter == null) {
                memberSetter = Reflect.Setter(type, name, InstanceAnyVisibility);
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
            return GetMemberGetter(type, name)?.Invoke(obj.GetType().IsValueType ? new ValueTypeHolder(obj) : obj);
        }

        public static void SetField(this object obj, string name, object value) {
            obj.SetField(obj.GetType(), name, value);
        }

        public static void SetField<T>(this T obj, string name, object value) {
            obj.SetField(typeof(T), name, value);
        }

        public static void SetField(this object obj, Type type, string name, object value) {
            if (obj.GetType().IsValueType) {
                GetFieldInfo(type, name)?.SetValue(obj, value);
            } else {
                GetMemberSetter(type, name)?.Invoke(obj, value);
            }
        }

        public static void CopyFields(this object obj, object fromObj, params string[] names) {
            obj.CopyFields(obj.GetType(), fromObj, names);
        }

        public static void CopyFields<T>(this T obj, T fromObj, params string[] names) {
            obj.CopyFields(typeof(T), fromObj, names);
        }

        public static void CopyFields(this object obj, Type type, object fromObj, params string[] names) {
            foreach (string name in names) {
                obj.SetField(type, name, fromObj.GetField(type, name));
            }
        }

        public static object GetProperty(this object obj, string name) {
            return obj.GetProperty(obj.GetType(), name);
        }

        public static object GetProperty<T>(this T obj, string name) {
            return obj.GetProperty(typeof(T), name);
        }

        public static object GetProperty(this object obj, Type type, string name) {
            return GetMemberGetter(type, name)?.Invoke(obj.GetType().IsValueType ? new ValueTypeHolder(obj) : obj);
        }

        public static void SetProperty(this object obj, string name, object value) {
            obj.SetProperty(obj.GetType(), name, value);
        }

        public static void SetProperty<T>(this T obj, string name, object value) {
            obj.SetProperty(typeof(T), name, value);
        }

        public static void SetProperty(this object obj, Type type, string name, object value) {
            if (obj.GetType().IsValueType) {
                GetPropertyInfo(type, name)?.GetSetMethod(true)?.Invoke(obj, new[] {value});
            } else {
                GetMemberSetter(type, name)?.Invoke(obj, value);
            }
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
            string key = $"InvokeMethod-{name}";

            MethodInvoker methodInvoker = type.GetExtendedDataValue<MethodInvoker>(key);
            if (methodInvoker == null) {
                methodInvoker = Reflect.Method(GetMethodInfo(type, name));
                type.SetExtendedDataValue(key, methodInvoker);
            }

            return methodInvoker?.Invoke(obj, parameters);
        }
    }
}