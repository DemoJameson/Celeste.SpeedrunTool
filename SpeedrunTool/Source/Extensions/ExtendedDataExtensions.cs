using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace Celeste.Mod.SpeedrunTool.Extensions;

// source from: https://stackoverflow.com/a/17264480
internal static class ExtendedDataExtensions {
    private static readonly ConditionalWeakTable<object, object> ExtendedData =
        new();

    private static IDictionary<string, object> CreateDictionary(object o) {
        return new Dictionary<string, object>();
    }

    public static void SetExtendedDataValue(this object o, string name, object value) {
        if (string.IsNullOrWhiteSpace(name)) {
            throw new ArgumentException("Invalid name");
        }

        name = name.Trim();

        IDictionary<string, object> values =
            (IDictionary<string, object>) ExtendedData.GetValue(o, CreateDictionary);

        if (value != null) {
            values[name] = value;
        } else {
            values.Remove(name);
        }
    }

    public static T GetExtendedDataValue<T>(this object o, string name) {
        if (string.IsNullOrWhiteSpace(name)) {
            throw new ArgumentException("Invalid name");
        }

        name = name.Trim();

        IDictionary<string, object> values =
            (IDictionary<string, object>) ExtendedData.GetValue(o, CreateDictionary);

        if (values.ContainsKey(name)) {
            return (T) values[name];
        }

        return default;
    }

    public static bool GetExtendedBoolean(this object o, string name) {
        return GetExtendedDataValue<bool>(o, name);
    }

    public static void SetExtendedBoolean(this object o, string name, bool value) {
        SetExtendedDataValue(o, name, value);
    }

    public static int GetExtendedInt(this object o, string name) {
        return GetExtendedDataValue<int>(o, name);
    }

    public static void SetExtendedInt(this object o, string name, int value) {
        SetExtendedDataValue(o, name, value);
    }

    public static float GetExtendedFloat(this object o, string name) {
        return GetExtendedDataValue<float>(o, name);
    }

    public static void SetExtendedFloat(this object o, string name, float value) {
        SetExtendedDataValue(o, name, value);
    }

    public static string GetExtendedString(this object o, string name) {
        return GetExtendedDataValue<string>(o, name);
    }

    public static void SetExtendedString(this object o, string name, string value) {
        SetExtendedDataValue(o, name, value);
    }
}