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

        internal static object InvokePostCloneProcessor(object sourceObj, object clonedObj, DeepCloneState deepCloneState) {
            if (_postCloneProcessor == null) {
                return clonedObj;
            }

            return _postCloneProcessor?.Invoke(sourceObj, clonedObj, deepCloneState);
        }
    }
}