using System.Collections;
using System.Collections.Generic;

namespace Celeste.Mod.SpeedrunTool.Extensions;

internal static class CommonExtensions {
    public static T With<T>(this T item, Action<T> action) {
        action(item);
        return item;
    }

    public static TValue GetValueOrDefault<TKey, TValue>(this IDictionary<TKey, TValue> dictionary, TKey key,
        TValue defaultValue) {
        if (dictionary.ContainsKey(key)) {
            return dictionary[key];
        }

        return defaultValue;
    }

    public static void AddRangeSafe(this IDictionary dict, IDictionary other) {
        foreach (DictionaryEntry dictionaryEntry in other) {
            if (!dict.Contains(other)) {
                dict.Add(dictionaryEntry.Key, dictionaryEntry.Value);
            }
        }
    }
}