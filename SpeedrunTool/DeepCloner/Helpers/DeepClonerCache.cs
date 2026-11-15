using System.Collections.Concurrent;

namespace Force.DeepCloner.Helpers {
    internal static class DeepClonerCache {
        internal static readonly ConcurrentDictionary<Type, object> TypeCache = new();
        internal static readonly ConcurrentDictionary<Type, object> TypeCacheDeepTo = new();
        internal static readonly ConcurrentDictionary<Type, object> TypeCacheShallowTo = new();
        internal static readonly ConcurrentDictionary<Type, object> StructAsObjectCache = new();

        /// <summary>
        /// This method can be used when we switch between safe / unsafe variants (for testing)
        /// </summary>
        public static void ClearCache() {
            TypeCache.Clear();
            TypeCacheDeepTo.Clear();
            TypeCacheShallowTo.Clear();
            StructAsObjectCache.Clear();
        }
    }
}