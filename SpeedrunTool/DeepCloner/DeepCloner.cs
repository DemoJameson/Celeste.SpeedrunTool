using Force.DeepCloner.Helpers;

namespace Force.DeepCloner {
    public delegate bool? KnownTypesProcessor(Type type);
    public delegate object PostCloneProcessor(object sourceObj, object clonedObj, DeepCloneState deepCloneState);
    public delegate object PreCloneProcessor(object sourceObj, DeepCloneState deepCloneState);

    public static class DeepCloner {
        private static KnownTypesProcessor _knownTypesProcessor;
        private static PreCloneProcessor _preCloneProcessor;
        private static PostCloneProcessor _postCloneProcessor;

        public static void SetKnownTypesProcessor(KnownTypesProcessor knownTypesProcessor) {
            _knownTypesProcessor = knownTypesProcessor;
        }

        public static void ClearKnownTypesProcessor() {
            _knownTypesProcessor = null;
        }

        internal static bool? InvokeKnownTypesProcessor(Type type) {
            return _knownTypesProcessor?.Invoke(type);
        }

        public static void SetPreCloneProcessor(PreCloneProcessor preCloneProcessor) {
            _preCloneProcessor = preCloneProcessor;
        }

        public static void ClearPreCloneProcessor() {
            _preCloneProcessor = null;
        }

        internal static object InvokePreCloneProcessor(object sourceObj, DeepCloneState deepCloneState) {
            if (_preCloneProcessor == null) {
                return null;
            }

            object clonedObj = _preCloneProcessor?.Invoke(sourceObj, deepCloneState);

            if (clonedObj != null) {
                deepCloneState.AddKnownRef(sourceObj, clonedObj);
            }

            return clonedObj;
        }

        public static void SetPostCloneProcessor(PostCloneProcessor postCloneProcessor) {
            _postCloneProcessor = postCloneProcessor;
        }

        public static void ClearPostCloneProcessor() {
            _postCloneProcessor = null;
        }

        /// <summary>
        /// Generates and caches the cloner delegate for a type without cloning anything.
        /// Reflection-only and thread-safe, so it can be used to warm up the cloner cache from a
        /// background thread (e.g. while a level is loading) so the first real clone does not pay
        /// the Expression.Compile cost.
        /// </summary>
        public static void WarmUpCloner(Type type) {
            DeepClonerGenerator.WarmUpCloner(type);
        }

        /// <summary>
        /// Same as <see cref="WarmUpCloner"/> for the <c>DeepCloneTo</c> cloner used on root
        /// objects (Level, SaveData, Session, Everest module sessions/save data, ...).
        /// </summary>
        public static void WarmUpCloneToCloner(Type type) {
            DeepClonerGenerator.WarmUpCloneToCloner(type);
        }

        internal static object InvokePostCloneProcessor(object sourceObj, object clonedObj, DeepCloneState deepCloneState) {
            if (_postCloneProcessor == null) {
                return clonedObj;
            }

            return _postCloneProcessor?.Invoke(sourceObj, clonedObj, deepCloneState);
        }
    }
}