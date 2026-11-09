using System.Collections;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace Force.DeepCloner.Helpers {
    public class DeepCloneState {
        private class CustomEqualityComparer : IEqualityComparer<object>, IEqualityComparer {
            public static readonly CustomEqualityComparer Instance = new();

            bool IEqualityComparer<object>.Equals(object x, object y) {
                return ReferenceEquals(x, y);
            }

            bool IEqualityComparer.Equals(object x, object y) {
                return ReferenceEquals(x, y);
            }

            public int GetHashCode(object obj) {
                return RuntimeHelpers.GetHashCode(obj);
            }
        }

        private readonly Dictionary<object, object> _loops = new(CustomEqualityComparer.Instance);

        public object GetKnownRef(object from) {
            return _loops.TryGetValue(from, out object to) ? to : null;
        }

        public void AddKnownRef(object from, object to) {
            _loops[from] = to;
        }

        public void Clear() {
            _loops.Clear();
        }
    }
}