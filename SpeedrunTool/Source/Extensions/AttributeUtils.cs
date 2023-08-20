using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace Celeste.Mod.SpeedrunTool.Extensions;

internal static class AttributeUtils {
    private static readonly object[] Parameterless = { };
    private static readonly IDictionary<Type, IEnumerable<MethodInfo>> MethodInfos = new Dictionary<Type, IEnumerable<MethodInfo>>();

    public static void CollectMethods<T>() where T : Attribute {
        MethodInfos[typeof(T)] = typeof(AttributeUtils).Assembly.GetTypesSafe().SelectMany(type => type
            .GetMethods(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
            .Where(info => info.GetParameters().Length == 0 && info.GetCustomAttribute<T>() != null));
    }

    public static void Invoke<T>() where T : Attribute {
        if (MethodInfos.TryGetValue(typeof(T), out var methodInfos)) {
            foreach (MethodInfo methodInfo in methodInfos) {
                methodInfo.Invoke(null, Parameterless);
            }
        }
    }
}

[AttributeUsage(AttributeTargets.Method)]
internal class LoadAttribute : Attribute { }

[AttributeUsage(AttributeTargets.Method)]
internal class UnloadAttribute : Attribute { }

[AttributeUsage(AttributeTargets.Method)]
internal class LoadContentAttribute : Attribute { }

[AttributeUsage(AttributeTargets.Method)]
internal class InitializeAttribute : Attribute { }