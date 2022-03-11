using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using Mono.Cecil.Cil;
using MonoMod.Cil;
using MonoMod.RuntimeDetour;
using MonoMod.Utils;

namespace Celeste.Mod.SpeedrunTool.SaveLoad;

internal static class DynDataUtils {
    // DynData
    public static ConditionalWeakTable<object, object> IgnoreObjects = new();
    private static readonly HashSet<Type> IgnoreTypes = new();

    private static readonly Lazy<int> EmptyTableEntriesLength =
        new(() => ((Array) new ConditionalWeakTable<object, object>().GetFieldValue("_entries")).Length);

    private static readonly Lazy<int> EmptyTableFreeList = new(() => (int) new ConditionalWeakTable<object, object>().GetFieldValue("_freeList"));

    // DynamicData
    public static readonly object DynamicDataMap = typeof(DynamicData).GetFieldValue("_DataMap");
    private static readonly ConditionalWeakTable<object, object> DynamicDataObjects = new();
    private static ILHook dynamicDataHook;
    private static readonly bool RunningOnMono = Type.GetType("Mono.Runtime") != null;

    [Load]
    private static void Load() {
        dynamicDataHook = new ILHook(typeof(DynamicData).GetConstructor(new[] {typeof(Type), typeof(object), typeof(bool)}), il => {
            ILCursor ilCursor = new(il);
            ilCursor.Emit(OpCodes.Ldarg_2).EmitDelegate<Action<object>>(RecordDynamicDataObject);
        });
    }

    [Unload]
    private static void Unload() {
        dynamicDataHook?.Dispose();
    }

    public static void ClearCached() {
        IgnoreObjects = new ConditionalWeakTable<object, object>();
        IgnoreTypes.Clear();
    }

    public static void RecordDynamicDataObject(object target) {
        if (target != null && !DynamicDataObjects.TryGetValue(target, out object _)) {
            DynamicDataObjects.Add(target, null);
        }
    }

    public static bool ExistDynamicData(object target) {
        return DynamicDataObjects.TryGetValue(target, out object _);
    }

    public static bool NotExistDynData(Type type, out object dataMap) {
        if (IgnoreTypes.Contains(type)) {
            dataMap = null;
            return true;
        }

        dataMap = GetDataMap(type);

        bool result;
        if (RunningOnMono) {
            result = (int) dataMap.GetFieldValue("size") == 0;
        } else {
            result = ((Array) dataMap.GetFieldValue("_entries")).Length == EmptyTableEntriesLength.Value &&
                     (int) dataMap.GetFieldValue("_freeList") == EmptyTableFreeList.Value;
        }

        if (result) {
            IgnoreTypes.Add(type);
        }

        return result;
    }

    private static object GetDataMap(Type type) {
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