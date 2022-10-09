using System.Collections.Generic;
using System.Runtime.CompilerServices;
using MonoMod.Utils;

namespace Celeste.Mod.SpeedrunTool.SaveLoad;

internal static class DynDataUtils {
    // DynData
    private static readonly Dictionary<Type, object> CachedDataMaps = new();
    public static ConditionalWeakTable<object, object> IgnoreObjects = new();
    private static readonly HashSet<Type> IgnoreTypes = new();

    private static readonly Lazy<int> EmptyTableEntriesLength =
        new(() => new ConditionalWeakTable<object, object>().GetFieldValue<Array>("_entries").Length);

    private static readonly Lazy<int> EmptyTableFreeList = new(() => new ConditionalWeakTable<object, object>().GetFieldValue<int>("_freeList"));

    // DynamicData
    private static readonly bool RunningOnMono = Type.GetType("Mono.Runtime") != null;

    public static void ClearCached() {
        IgnoreObjects = new ConditionalWeakTable<object, object>();
        IgnoreTypes.Clear();
    }

    public static bool NotExistDynData(Type type, out object dataMap) {
        if (IgnoreTypes.Contains(type)) {
            dataMap = null;
            return true;
        }

        dataMap = GetDataMap(type);

        bool isEmpty;
        if (RunningOnMono) {
            isEmpty = dataMap.GetFieldValue<int>("size") == 0;
        } else {
            isEmpty = dataMap.GetFieldValue<Array>("_entries").Length == EmptyTableEntriesLength.Value &&
                     dataMap.GetFieldValue<int>("_freeList") == EmptyTableFreeList.Value;
        }

        if (isEmpty) {
            IgnoreTypes.Add(type);
        }

        return isEmpty;
    }

    private static object GetDataMap(Type type) {
        if (CachedDataMaps.TryGetValue(type, out var result)) {
            return result;
        } else {
            result = typeof(DynData<>).MakeGenericType(type).GetFieldValue("_DataMap");
            return CachedDataMaps[type] = result;
        }
    }
}