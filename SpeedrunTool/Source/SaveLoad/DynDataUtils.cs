using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using Celeste.Mod.SpeedrunTool.Extensions;
using Mono.Cecil.Cil;
using Monocle;
using MonoMod.Cil;
using MonoMod.RuntimeDetour;
using MonoMod.Utils;

namespace Celeste.Mod.SpeedrunTool.SaveLoad {
    internal static class DynDataUtils {
        private static readonly ConditionalWeakTable<object, HashSet<Type>> DynDataObjects = new();
        public static readonly Lazy<object> DynamicDataMap = new(() => typeof(DynamicData).GetFieldValue("_DataMap"));
        private static ILHook dynDataHook;

        public static void OnLoad() {
            dynDataHook = new ILHook(typeof(DynData<>).MakeGenericType(typeof(Entity)).GetConstructors()[1], il => {
                ILCursor ilCursor = new(il);
                ilCursor.Emit(OpCodes.Ldarg_0).Emit(OpCodes.Ldarg_1).EmitDelegate<Action<object, object>>((dynData, target) => {
                    Type type = dynData.GetType().GetGenericArguments()[0];
                    RecordDynDataObject(target, type);
                });
            });
        }

        public static void RecordDynDataObject(object target, Type type) {
            if (DynDataObjects.TryGetValue(target, out HashSet<Type> types)) {
                types.Add(type);
            } else {
                DynDataObjects.Add(target, new HashSet<Type> {type});
            }
        }

        public static bool TryGetDynDataTypes(object target, out HashSet<Type> types) {
            return DynDataObjects.TryGetValue(target, out types);
        }

        public static void OnUnload() {
            dynDataHook?.Dispose();
        }

        public static object CreateDynData(object obj, Type targetType) {
            string key = $"DynDataUtils-CreateDynData-{targetType.FullName}";

            ConstructorInfo constructorInfo = targetType.GetExtendedDataValue<ConstructorInfo>(key);

            if (constructorInfo == null) {
                constructorInfo = typeof(DynData<>).MakeGenericType(targetType).GetConstructor(new[] {targetType});
                targetType.SetExtendedDataValue(key, constructorInfo);
            }

            return constructorInfo?.Invoke(new[] {obj});
        }

        public static object GetDataMap(Type type) {
            string key = $"DynDataUtils-GetDataMap-{type}";

            FieldInfo fieldInfo = type.GetExtendedDataValue<FieldInfo>(key);

            if (fieldInfo == null) {
                fieldInfo = typeof(DynData<>).MakeGenericType(type)
                    .GetField("_DataMap", BindingFlags.Static | BindingFlags.NonPublic);
                type.SetExtendedDataValue(key, fieldInfo);
            }

            return fieldInfo?.GetValue(null);
        }

        public static Dictionary<string, object> GetDate(object obj, Type targetType) {
            return CreateDynData(obj, targetType)?.GetPropertyValue("Data") as Dictionary<string, object>;
        }

        public static bool IsDynData(this Type type, out Type genericType) {
            bool result = type.IsGenericType && type.GetGenericTypeDefinition().IsAssignableFrom(typeof(DynData<>))
                                             && type.GenericTypeArguments.Length == 1;

            genericType = result ? type.GenericTypeArguments[0] : null;

            return result;
        }
    }
}