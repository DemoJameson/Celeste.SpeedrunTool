using System;
using System.Reflection;
using System.Runtime.CompilerServices;
using Celeste.Mod.SpeedrunTool.Extensions;
using Mono.Cecil.Cil;
using MonoMod.Cil;
using MonoMod.RuntimeDetour;
using MonoMod.Utils;

namespace Celeste.Mod.SpeedrunTool.SaveLoad {
    internal static class DynDataUtils {
        // DynData
        public static ConditionalWeakTable<object, object> IgnoreObjects = new();
        
        // DynamicData
        public static readonly object DynamicDataMap = typeof(DynamicData).GetFieldValue("_DataMap");
        private static readonly ConditionalWeakTable<object, object> DynamicDataObjects = new();
        private static ILHook dynamicDataHook;

        public static void OnLoad() {
            dynamicDataHook = new ILHook(typeof(DynamicData).GetConstructor(new []{typeof(Type), typeof(object),typeof(bool)}), il => {
                ILCursor ilCursor = new(il);
                ilCursor.Emit(OpCodes.Ldarg_2).EmitDelegate<Action<object>>(RecordDynamicDataObject);
            });
        }

        public static void OnUnload() {
            dynamicDataHook?.Dispose();
        }

        public static void OnClearState() {
            IgnoreObjects = new ConditionalWeakTable<object, object>();
        }

        public static void RecordDynamicDataObject(object target) {
            if (!DynamicDataObjects.TryGetValue(target, out object _)) {
                DynamicDataObjects.Add(target, null);
            }
        }

        public static bool ExistDynamicData(object target) {
            return DynamicDataObjects.TryGetValue(target, out object _);
        }

        public static object GetDataMap(Type type) {
            string key = $"DynDataUtils-GetDataMap-{type}";

            object result = type.GetExtendedDataValue<object>(key);

            if (result == null) {
                result = typeof(DynData<>).MakeGenericType(type)
                    .GetField("_DataMap", BindingFlags.Static | BindingFlags.NonPublic)?.GetValue(null);
                type.SetExtendedDataValue(key, result);
            }

            return result;
        }
    }
}