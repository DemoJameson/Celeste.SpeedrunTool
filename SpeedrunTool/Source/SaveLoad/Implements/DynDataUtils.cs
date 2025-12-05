using MonoMod.Utils;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace Celeste.Mod.SpeedrunTool.SaveLoad;

internal static class DynDataUtils {
    // DynData
    private static readonly Dictionary<Type, object> CachedDataMaps = [];
    public static ConditionalWeakTable<object, object> IgnoreObjects = [];
    private static readonly HashSet<Type> IgnoreTypes = [];

    private static readonly Lazy<int> EmptyTableEntriesLength =
        new(() => new ConditionalWeakTable<object, object>().GetFieldValue<Array>("_entries").Length);
    private static readonly Lazy<int> EmptyTableFreeList = new(() => new ConditionalWeakTable<object, object>().GetFieldValue<int>("_freeList"));
    private static readonly Lazy<int> EmptyContainerEntriesLength =
        new(() => new ConditionalWeakTable<object, object>().GetFieldValue("_container").GetFieldValue<Array>("_entries").Length);
    private static readonly Lazy<int> EmptyContainerFirstFreeEntry = new(() => new ConditionalWeakTable<object, object>().GetFieldValue("_container").GetFieldValue<int>("_freeList"));
    private static Func<object, bool> checkEmpty;

    public static void ClearCached() {
        IgnoreObjects = [];
        IgnoreTypes.Clear();
    }

    public static bool NotExistDynData(Type type, out object dataMap) {
        if (IgnoreTypes.Contains(type)) {
            dataMap = null;
            return true;
        }

        dataMap = GetDataMap(type);
        if (CheckEmpty(dataMap)) {
            IgnoreTypes.Add(type);
            return true;
        }
        else {
            return false;
        }
    }

    private static bool CheckEmpty(object weakTable) {
        if (checkEmpty == null) {
            if (Type.GetType("Mono.Runtime") != null) {
                // Mono
                checkEmpty = static o => o.GetFieldValue<int>("size") == 0;
            }
            else if (weakTable.GetType().GetFieldInfo("_entries") != null) {
                // .net framework
                checkEmpty = static o => o.GetFieldValue<Array>("_entries").Length == EmptyTableEntriesLength.Value &&
                             o.GetFieldValue<int>("_freeList") == EmptyTableFreeList.Value;
            }
            else {
                // .net7
                checkEmpty = static o => o.GetFieldValue("_container") is { } container && container.GetFieldValue<Array>("_entries").Length == EmptyContainerEntriesLength.Value &&
                                  container.GetFieldValue<int>("_firstFreeEntry") == EmptyContainerFirstFreeEntry.Value;
            }
        }

        return checkEmpty(weakTable);
    }

    private static object GetDataMap(Type type) {
        if (CachedDataMaps.TryGetValue(type, out var result)) {
            return result;
        }
        else {
            result = typeof(DynData<>).MakeGenericType(type).GetFieldValue("_DataMap");
            return CachedDataMaps[type] = result;
        }
    }
}