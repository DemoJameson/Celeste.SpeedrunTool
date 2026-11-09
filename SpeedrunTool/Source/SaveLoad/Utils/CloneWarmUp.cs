#nullable enable annotations

using Celeste.Mod.SpeedrunTool.Utils;
using Force.DeepCloner;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;
using System.Threading.Tasks;

namespace Celeste.Mod.SpeedrunTool.SaveLoad.Utils;

/// <summary>
/// Warms up the DeepCloner type caches while a level is loading.
///
/// The first save of a session used to pay several hundred milliseconds for
/// <c>Expression.Compile</c> of every cloner that the level graph / static-field snapshot
/// happens to need (measured: ~216 ms for the Level clone + ~142 ms for the save actions on
/// the dev machine, while later saves take ~13 ms in total). Everything here is
/// reflection-only - no live game object is read - so it is safe to run on a background
/// thread during the level load and the cost is hidden behind the loading screen.
///
/// Only the codegen is warmed; the first invocation of each generated delegate still pays its
/// (much smaller) JIT cost on the first real save.
/// </summary>
internal static class CloneWarmUp {
    /// <summary>
    /// Upper bound on how many types are warmed, to bound the retained cache memory
    /// (each cached cloner delegate is a few KB; 6000 is far above the type count of a big
    /// mod pack's entity/component graph but keeps the worst case bounded).
    /// </summary>
    private const int MaxWarmTypes = 6000;

    private static bool started;

    internal static bool Enabled { get; set; } = true;

    internal static void Start() {
        if (!Enabled || started) {
            return;
        }

        started = true;
        Task.Run(RunWarmUp);
    }

    private static void RunWarmUp() {
        Stopwatch stopwatch = Stopwatch.StartNew();
        int warmedTypes = 0;
        int warmedCloneToTypes = 0;
        int discoveredTypes = 0;

        try {
            HashSet<Type> queued = [];
            Queue<Type> queue = new();
            HashSet<Type> cloneToRoots = [];

            Enqueue(typeof(Level), queue, queued);
            Enqueue(typeof(SaveData), queue, queued);
            Enqueue(typeof(Session), queue, queued);
            cloneToRoots.Add(typeof(Level));
            cloneToRoots.Add(typeof(SaveData));
            cloneToRoots.Add(typeof(Session));

            foreach (EverestModule module in Everest.Modules) {
                if (module.SessionType is { } sessionType) {
                    Enqueue(sessionType, queue, queued);
                    cloneToRoots.Add(sessionType);
                }

                if (module.SaveDataType is { } saveDataType) {
                    Enqueue(saveDataType, queue, queued);
                    cloneToRoots.Add(saveDataType);
                }
            }

            // Third-party support clones this manager with DeepCloneToShared (ExtendedVariantsUtils).
            if (ModUtils.GetType("ExtendedVariantMode", "ExtendedVariants.Module.ExtendedVariantsModule") is { } extendedVariantsModule
                && extendedVariantsModule.GetFieldInfo("TriggerManager") is { } triggerManagerField) {
                Enqueue(triggerManagerField.FieldType, queue, queued);
                cloneToRoots.Add(triggerManagerField.FieldType);
            }

            // Types referenced from the saved static fields / module fields.
            SaveLoadAction.CollectWarmUpTypes(type => Enqueue(type, queue, queued));

            // Every Entity / Component / Renderer subclass (the runtime types of a level graph
            // cannot be derived from the declared field types alone).
            foreach (Type type in ModUtils.GetAllTypes()) {
                if (type is null) {
                    continue;
                }

                if (type.IsSameOrSubclassOf(typeof(Entity))
                    || type.IsSameOrSubclassOf(typeof(Component))
                    || type.IsSameOrSubclassOf(typeof(Renderer))) {
                    Enqueue(type, queue, queued);
                }
            }

            discoveredTypes = queued.Count;

            while (queue.Count > 0) {
                Type type = queue.Dequeue();
                DeepCloner.WarmUpCloner(type);
                warmedTypes++;

                if (cloneToRoots.Contains(type)) {
                    DeepCloner.WarmUpCloneToCloner(type);
                    warmedCloneToTypes++;
                }

                foreach (Type fieldType in GetFieldTypes(type)) {
                    Enqueue(fieldType, queue, queued);
                }
            }

            // The clone-to roots must be warm even if they were only added as roots.
            foreach (Type root in cloneToRoots) {
                DeepCloner.WarmUpCloneToCloner(root);
            }

            warmedCloneToTypes = cloneToRoots.Count;
        }
        catch (Exception exception) {
            Logger.Warn("SpeedrunTool/CloneWarmUp", $"Cloner warm-up failed: {exception.GetType().Name}: {exception.Message}");
        }

        Logger.Info("SpeedrunTool/CloneWarmUp",
            $"Warmed {warmedTypes}/{discoveredTypes} cloner(s) + {warmedCloneToTypes} clone-to root(s) in {stopwatch.ElapsedMilliseconds}ms.");
    }

    private static IEnumerable<Type> GetFieldTypes(Type type) {
        for (Type current = type; current is not null && current != typeof(object); current = current.BaseType) {
            FieldInfo[] fields;
            try {
                fields = current.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
            }
            catch {
                yield break;
            }

            foreach (FieldInfo field in fields) {
                yield return field.FieldType;
            }
        }
    }

    private static void Enqueue(Type type, Queue<Type> queue, HashSet<Type> queued) {
        if (type is null || queued.Count >= MaxWarmTypes) {
            return;
        }

        if (type.IsByRef || type.IsPointer || type.IsGenericParameter || type.IsByRefLike) {
            return;
        }

        if (type.IsArray) {
            Enqueue(type.GetElementType(), queue, queued);
            // The array type itself also has a generated cloner, fall through to warm it.
        }

        if (type.IsGenericType && !type.IsGenericTypeDefinition) {
            foreach (Type argument in type.GetGenericArguments()) {
                Enqueue(argument, queue, queued);
            }
        }

        if (type.IsGenericTypeDefinition || type.IsInterface || type.IsPrimitive || type.IsEnum || type == typeof(string)) {
            return;
        }

        // BCL types are only interesting as generic containers (List<T>, Dictionary<K,V>, ...);
        // their element types were enqueued above.
        string ns = type.Namespace;
        if (ns is not null && (ns.StartsWith("System") || ns.StartsWith("Microsoft")) && !type.IsGenericType) {
            return;
        }

        if (queued.Add(type)) {
            queue.Enqueue(type);
        }
    }
}
