using Celeste.Mod.SpeedrunTool.SaveLoad;
using MonoMod.ModInterop;
using System.Collections.Generic;

namespace Celeste.Mod.SpeedrunTool.ModInterop;

/// <summary>
/// Reserved contract for a mod that owns Lua state and wants to tell SpeedrunTool when that state is
/// at rest. The full provider (save/restore callbacks) is intentionally not implemented yet; this
/// version only consumes <see cref="IsAtRest"/> as an instance-level safe predicate.
/// </summary>
public interface ILuaStateProvider {
    /// <summary>Short name used in logs.</summary>
    string Name { get; }

    /// <summary>
    /// Return true when this entity's Lua state is at rest and may be snapshotted. Returning false
    /// (or throwing) leaves the normal fail-closed checker in charge.
    /// </summary>
    bool IsAtRest(Entity entity);
}

/// <summary>
/// Public desync-checker API exported as <c>SpeedrunTool.Desync</c> (ModInterop). New API: there is
/// no historical contract to preserve (the old <c>ExternalSafeOracle</c> was never exported).
/// Registering here never disables the generic "Lua is executing" probe for other entities; it only
/// declares the caller's own types/instances safe. See docs/desync-checker.md for the full contract.
/// </summary>
public static class DesyncInterop {
    private static readonly Dictionary<Func<Entity, bool>, Func<object, bool>> entityPredicateWrappers = new();
    private static readonly Dictionary<Func<Level, bool>, Func<object, bool>> levelPredicateWrappers = new();
    private static readonly Dictionary<ILuaStateProvider, Func<object, bool>> luaStateProviders = new();
    private static bool defaultsInstalled;

    /// <summary>
    /// Historical hardcoded safe maps (the old internal <c>ExternalSafeOracle.SafeMaps</c>). Preserved
    /// as a default level predicate; other maps should be declared through <c>SpeedrunTool.Desync</c>.
    /// </summary>
    internal static readonly HashSet<string> DefaultSafeMaps = ["StrawberryJam2021/5-Grandmaster/maya"];

    private static bool IsDefaultSafeLevel(Level level) {
        return level?.Session is { } session && DefaultSafeMaps.Contains(session.Area.SID);
    }

    [Load]
    private static void Load() {
        typeof(DesyncExports).ModInterop();
    }

    /// <summary>
    /// Called once from <c>DesyncRiskAnalyzer</c>'s <c>[Initialize]</c>: installs the error logging
    /// hooks and the default level predicates (the old hardcoded safe map, preserved as a predicate).
    /// </summary>
    internal static void InstallDefaults() {
        if (defaultsInstalled) {
            return;
        }

        defaultsInstalled = true;
        DesyncRegistration.PredicateErrorHook =
            exception => Logger.Warn("SpeedrunTool/Desync", $"A SpeedrunTool.Desync registration threw and was ignored (fail-closed): {exception}");
        DesyncRegistration.AddLevelPredicate(
            level => level is Level realLevel && IsDefaultSafeLevel(realLevel));
        Logger.Info("SpeedrunTool/Desync",
            $"SpeedrunTool.Desync API ready (default safe maps: {string.Join(", ", DefaultSafeMaps)}).");
    }

    // ReSharper disable once MemberCanBePrivate.Global
    /// <summary>ModInterop surface: import with <c>[ModImportName("SpeedrunTool.Desync")]</c>.</summary>
    [ModExportName("SpeedrunTool.Desync")]
    public static class DesyncExports {
        /// <summary>
        /// Declare a type safe for the desync checker even if its fields contain Lua objects. Use this
        /// when the mod itself restores that state (e.g. through a SaveLoadAction) or knows it is
        /// immutable. The declaration covers subclasses and also suppresses the "Lua is executing"
        /// probe for entities of this type.
        /// </summary>
        public static void AddSafeType(Type type) {
            DesyncRegistration.AddSafeType(type);
        }

        /// <summary>Remove a previous <see cref="AddSafeType"/> declaration.</summary>
        public static bool RemoveSafeType(Type type) {
            return DesyncRegistration.RemoveSafeType(type);
        }

        /// <summary>
        /// Instance-level at-rest predicate: return true when this entity is currently safe to
        /// snapshot. Evaluated on the main thread during save (and while probing coroutines); keep it
        /// cheap and side-effect free. Returning false means "no opinion" (the fail-closed checker
        /// still applies). An exception is logged and treated as false.
        /// </summary>
        public static void AddSafeEntityPredicate(Func<Entity, bool> predicate) {
            if (predicate is null) {
                return;
            }

            Func<object, bool> wrapper = value => value is Entity entity && predicate(entity);
            if (DesyncRegistration.AddEntityPredicate(wrapper)) {
                entityPredicateWrappers[predicate] = wrapper;
            }
        }

        /// <summary>Remove a previous <see cref="AddSafeEntityPredicate"/> predicate.</summary>
        public static bool RemoveSafeEntityPredicate(Func<Entity, bool> predicate) {
            if (predicate is null || !entityPredicateWrappers.TryGetValue(predicate, out Func<object, bool> wrapper)) {
                return false;
            }

            entityPredicateWrappers.Remove(predicate);
            return DesyncRegistration.RemoveEntityPredicate(wrapper);
        }

        /// <summary>
        /// Level-level predicate: returning true skips the whole desync checker for this save (the same
        /// escape hatch the old internal <c>ExternalSafeOracle</c> provided). Use sparingly; a false
        /// positive here means a real desync will not be detected.
        /// </summary>
        public static void AddSafeLevelPredicate(Func<Level, bool> predicate) {
            if (predicate is null) {
                return;
            }

            Func<object, bool> wrapper = value => value is Level level && predicate(level);
            if (DesyncRegistration.AddLevelPredicate(wrapper)) {
                levelPredicateWrappers[predicate] = wrapper;
            }
        }

        /// <summary>Remove a previous <see cref="AddSafeLevelPredicate"/> predicate.</summary>
        public static bool RemoveSafeLevelPredicate(Func<Level, bool> predicate) {
            if (predicate is null || !levelPredicateWrappers.TryGetValue(predicate, out Func<object, bool> wrapper)) {
                return false;
            }

            levelPredicateWrappers.Remove(predicate);
            return DesyncRegistration.RemoveLevelPredicate(wrapper);
        }

        /// <summary>
        /// Reserved: register a Lua state provider. In this version only its at-rest predicate is used
        /// (equivalent to <see cref="AddSafeEntityPredicate"/>); save/restore callbacks are planned for
        /// a later release once a mod actually needs them. Returns the provider, to be passed to
        /// <see cref="UnregisterLuaStateProvider"/> on unload.
        /// </summary>
        public static object RegisterLuaStateProvider(ILuaStateProvider provider) {
            if (provider is null || luaStateProviders.ContainsKey(provider)) {
                return provider;
            }

            Func<object, bool> wrapper = value => value is Entity entity && provider.IsAtRest(entity);
            luaStateProviders[provider] = wrapper;
            DesyncRegistration.AddEntityPredicate(wrapper);
            Logger.Info("SpeedrunTool/Desync",
                $"Lua state provider [{provider.Name}] registered. Only its IsAtRest(Entity) predicate is active in this version; "
                + "save/restore callbacks are reserved.");
            return provider;
        }

        /// <summary>Remove a previous <see cref="RegisterLuaStateProvider"/> provider.</summary>
        public static bool UnregisterLuaStateProvider(ILuaStateProvider provider) {
            if (provider is null || !luaStateProviders.TryGetValue(provider, out Func<object, bool> wrapper)) {
                return false;
            }

            luaStateProviders.Remove(provider);
            return DesyncRegistration.RemoveEntityPredicate(wrapper);
        }
    }
}
