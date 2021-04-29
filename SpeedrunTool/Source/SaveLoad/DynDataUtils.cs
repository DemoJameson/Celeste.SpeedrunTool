using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using Celeste.Mod.SpeedrunTool.Extensions;
using MonoMod.Utils;

namespace Celeste.Mod.SpeedrunTool.SaveLoad {
    internal static class DynDataUtils {
        public static readonly HashSet<Type> IgnoreTypes = new();
        public static readonly Lazy<object> DynamicDataMap = new(() => typeof(DynamicData).GetFieldValue("_DataMap"));

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

        public static IDictionary GetSpecialGetters(Type type) {
            string key = $"DynDataUtils-GetSpecialGetters-{type}";

            IDictionary result = type.GetExtendedDataValue<IDictionary>(key);

            if (result == null) {
                result = typeof(DynData<>).MakeGenericType(type).GetField("_SpecialGetters", BindingFlags.Static | BindingFlags.NonPublic)?.GetValue(null) as IDictionary;
                type.SetExtendedDataValue(key, result);
            }

            return result;
        }
    }
}