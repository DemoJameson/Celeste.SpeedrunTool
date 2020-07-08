using System;
using System.Collections.Generic;

namespace Celeste.Mod.SpeedrunTool.Extensions {
    // from https://stackoverflow.com/a/17264480
    public static class CommonExtensions {
        public static T With<T>(this T item, Action<T> action) {
            action(item);
            return item;
        }

        public static TValue GetValueOrDefault<TKey, TValue>
        (this IDictionary<TKey, TValue> dictionary,
            TKey key,
            TValue defaultValue) {
            if (dictionary.ContainsKey(key)) {
                return dictionary[key];
            }

            return defaultValue;
        }
    }
}