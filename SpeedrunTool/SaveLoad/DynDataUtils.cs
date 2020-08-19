using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using Celeste.Mod.SpeedrunTool.Extensions;
using MonoMod.Utils;

namespace Celeste.Mod.SpeedrunTool.SaveLoad {
    internal static class DynDataUtils {
        private static object CreateDynData(object obj) {
            Type type = obj.GetType();
            string key = $"DynDataUtils-CreateDynData-{type.FullName}";

            ConstructorInfo constructorInfo = type.GetExtendedDataValue<ConstructorInfo>(key);

            if (constructorInfo == null) {
                constructorInfo = typeof(DynData<>).MakeGenericType(type).GetConstructor(new[] {type});
                type.SetExtendedDataValue(key, constructorInfo);
            }

            return constructorInfo?.Invoke(new[] {obj});
        }

        public static IDictionary GetDataMap(Type type) {
            string key = $"DynDataUtils-GetDataMap-{type}";

            FieldInfo fieldInfo = type.GetExtendedDataValue<FieldInfo>(key);

            if (fieldInfo == null) {
                fieldInfo = typeof(DynData<>).MakeGenericType(type)
                    .GetField("_DataMap", BindingFlags.Static | BindingFlags.NonPublic);
                type.SetExtendedDataValue(key, fieldInfo);
            }

            return fieldInfo?.GetValue(null) as IDictionary;
        }

        public static Dictionary<string, object> GetDate(object obj) {
            return CreateDynData(obj)?.GetPropertyValue("Data") as Dictionary<string, object>;
        }
    }
}