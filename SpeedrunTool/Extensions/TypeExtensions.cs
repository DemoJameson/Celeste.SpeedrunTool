using System;
using System.Collections.Generic;

namespace Celeste.Mod.SpeedrunTool.Extensions {
    public static class TypeExtensions {
        public static bool IsSimple(this Type type) {
            // seems celeste not use decimal type.
            return type.IsPrimitive || type.IsValueType || type.IsEnum || type == typeof(string) || type == typeof(decimal);
        }

        public static bool IsList(this Type type, out Type genericType) {
            bool result = type.IsGenericType && type.GetGenericTypeDefinition().IsAssignableFrom(typeof(List<>))
                                             && type.GenericTypeArguments.Length == 1;

            genericType = result ? type.GenericTypeArguments[0] : null;

            return result;
        }
    }
}