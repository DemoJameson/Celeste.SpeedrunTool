using System;
using System.Collections.Generic;

namespace Celeste.Mod.SpeedrunTool.SaveLoad;

// -------------------------------------------------------------------------------------------------
// Registration store behind the public SpeedrunTool.Desync API.
//
// This file is deliberately game-free (BCL types only): the public ModInterop layer wraps Entity /
// Level into object delegates, and tests/DesyncCheckerTests links this file to test the registration
// / override semantics offline. Keep it that way: no Celeste/Monocle usings here.
// -------------------------------------------------------------------------------------------------

/// <summary>
/// Session-wide registry of "safe" declarations made by mods through <c>SpeedrunTool.Desync</c>.
/// </summary>
internal static class DesyncRegistration {
    private static readonly HashSet<Type> safeTypes = [];
    private static readonly List<Func<object, bool>> entityPredicates = [];
    private static readonly List<Func<object, bool>> levelPredicates = [];

    /// <summary>Invoked when a predicate throws; the predicate is treated as "not safe" (fail-closed).</summary>
    internal static Action<Exception> PredicateErrorHook { get; set; }

    /// <summary>Invoked when a type becomes safe, so the checker can drop stale negative caches.</summary>
    internal static Action<Type> SafeTypeRegisteredHook { get; set; }

    internal static int SafeTypeCount => safeTypes.Count;
    internal static int EntityPredicateCount => entityPredicates.Count;
    internal static int LevelPredicateCount => levelPredicates.Count;

    internal static bool AddSafeType(Type type) {
        if (type is null) {
            return false;
        }

        if (!safeTypes.Add(type)) {
            return false;
        }

        SafeTypeRegisteredHook?.Invoke(type);
        return true;
    }

    internal static bool RemoveSafeType(Type type) {
        return type is not null && safeTypes.Remove(type);
    }

    /// <summary>True when <paramref name="type"/> (or one of its base types) was declared safe.</summary>
    internal static bool IsTypeSafe(Type type) {
        if (type is null) {
            return false;
        }

        foreach (Type safeType in safeTypes) {
            if (type == safeType || type.IsSubclassOf(safeType)) {
                return true;
            }
        }

        return false;
    }

    internal static bool AddEntityPredicate(Func<object, bool> predicate) {
        if (predicate is null) {
            return false;
        }

        entityPredicates.Add(predicate);
        return true;
    }

    internal static bool RemoveEntityPredicate(Func<object, bool> predicate) {
        return predicate is not null && entityPredicates.Remove(predicate);
    }

    /// <summary>True when any registered entity predicate claims this instance is safe.</summary>
    internal static bool IsEntitySafe(object entity) {
        return AnyPredicateSaysSafe(entityPredicates, entity);
    }

    internal static bool AddLevelPredicate(Func<object, bool> predicate) {
        if (predicate is null) {
            return false;
        }

        levelPredicates.Add(predicate);
        return true;
    }

    internal static bool RemoveLevelPredicate(Func<object, bool> predicate) {
        return predicate is not null && levelPredicates.Remove(predicate);
    }

    /// <summary>True when any registered level predicate declares this level safe (whole checker off).</summary>
    internal static bool IsLevelSafe(object level) {
        return AnyPredicateSaysSafe(levelPredicates, level);
    }

    private static bool AnyPredicateSaysSafe(List<Func<object, bool>> predicates, object argument) {
        if (argument is null) {
            return false;
        }

        // Copy on write: a predicate is allowed to register/unregister during iteration.
        Func<object, bool>[] snapshot = predicates.ToArray();
        foreach (Func<object, bool> predicate in snapshot) {
            try {
                if (predicate(argument)) {
                    return true;
                }
            }
            catch (Exception exception) {
                // A throwing predicate must never make the checker fail open.
                PredicateErrorHook?.Invoke(exception);
            }
        }

        return false;
    }

    /// <summary>Test hook: drop every registration and hook.</summary>
    internal static void Reset() {
        safeTypes.Clear();
        entityPredicates.Clear();
        levelPredicates.Clear();
        PredicateErrorHook = null;
        SafeTypeRegisteredHook = null;
    }
}
