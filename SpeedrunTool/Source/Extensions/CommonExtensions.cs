using System.Collections;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace Celeste.Mod.SpeedrunTool.Extensions;

internal static class CommonExtensions {
    public static T With<T>(this T item, Action<T> action) {
        action(item);
        return item;
    }

    public static TValue GetValueOrDefault<TKey, TValue>(this IDictionary<TKey, TValue> dictionary, TKey key,
        TValue defaultValue) {
        return dictionary.TryGetValue(key, out TValue result) ? result : defaultValue;
    }

    public static void SetRange(this IDictionary dict, IDictionary other) {
        foreach (DictionaryEntry dictionaryEntry in other) {
            dict[dictionaryEntry.Key] = dictionaryEntry.Value;
        }
    }

    public static void Set<TKey, TValue>(this ConditionalWeakTable<TKey, TValue> weakTable, TKey key, TValue value) where TKey : class where TValue : class {
        weakTable.Remove(key);
        weakTable.Add(key, value);
    }

    public static bool ContainsKey<TKey, TValue>(this ConditionalWeakTable<TKey, TValue> weakTable, TKey key) where TKey : class where TValue : class {
        return weakTable.TryGetValue(key, out TValue _);
    }

    public static bool IsNullOrEmpty(this string str) {
        return string.IsNullOrEmpty(str);
    }

    public static bool IsNotNullAndEmpty(this string str) {
        return !string.IsNullOrEmpty(str);
    }
}