using Celeste.Mod.SpeedrunTool.SaveLoad;
using Celeste.Mod.SpeedrunTool.SaveLoad.Utils;
using MonoMod.ModInterop;
using System.Collections.Generic;
using System.Linq;

namespace Celeste.Mod.SpeedrunTool.ModInterop;

#if false
/* How to use the SpeedrunTool ModInterop API
 *
 *   1. Copy this whole class into your mod (any namespace) and keep it in one file.
 *   2. Call SrtImports.Init(...) from your module's Initialize(), and SrtImports.Unload() from
 *      its Unload():
 *
 *        // in your EverestModule
 *        public override void Initialize() {
 *            SrtImports.Init();
 *        }
 *
 *        public override void Unload() {
 *            SrtImports.Unload();
 *        }
 *
 * Init() binds the ModInterop fields and runs the registrations listed inside it; Unload() drops
 * every registration this class made, so a reloaded mod leaves nothing behind. RegisterStatic() and
 * Register() add more, one call each -- all of them are tracked for Unload().
 *
 * Call these from Initialize()/Unload(), not from [Load]. Everest runs every mod's Load() before it
 * calls Initialize() on any mod, and SpeedrunTool registers its exports from its own [Load], so the
 * fields are bound by the time Initialize() runs. A [Load] registration only works when
 * SpeedrunTool happens to load first, and a call made too early is dropped without an error.
 *
 * Running without SpeedrunTool is supported: the fields stay null and Init() does nothing.
 * This template assumes <Nullable>enable</Nullable>; drop the "?" annotations if yours is off.
 */

using Celeste;
using Celeste.Mod;
using MonoMod.ModInterop;
using Monocle;
using System;
using System.Collections.Generic;
using System.Linq;

namespace YourMod;

public static class SrtImports {
    private static readonly List<object> handles = new();

    /// <summary>True once SpeedrunTool's exports were found.</summary>
    public static bool IsInstalled => Imports.RegisterStaticTypes != null;

    /// <summary>
    /// Binds the ModInterop fields and runs the registrations listed below. Call once, from your
    /// module's Initialize(). Everything registered through this class -- here, or through
    /// RegisterStatic()/Register() later -- is dropped again by Unload().
    /// <para>
    /// Replace MyModule and the member names with your own type and fields/properties; a member that
    /// does not exist is a compile error, which is the point.
    /// </para>
    /// </summary>
    public static void Init() {
        typeof(Imports).ModInterop();

        // TODO: point this at the state that must survive a savestate.
        RegisterStatic(typeof(MyModule), nameof(MyModule.Foo), nameof(MyModule.Bar));
    }

    /// <summary>
    /// Registers the named static fields and properties of <paramref name="type"/>, to be saved and
    /// restored with the savestate. Add one call per type.
    /// </summary>
    public static void RegisterStatic(Type type, params string[] memberNames) {
        if (Imports.RegisterStaticTypes == null) {
            WarnIfLoadedButUnbound();
            return;
        }

        Track(Imports.RegisterStaticTypes(type, memberNames));
    }

    /// <summary>
    /// Registers save/load callbacks for state that does not live in static members. Write into the
    /// dictionary handed to the callbacks: a value parked in a plain field is not per-slot, so with
    /// several save slots only the last write survives.
    /// </summary>
    public static void Register(
        Action<Dictionary<Type, Dictionary<string, object>>, Level> saveState,
        Action<Dictionary<Type, Dictionary<string, object>>, Level> loadState,
        Action? clearState = null) {
        if (Imports.RegisterSaveLoadAction == null) {
            WarnIfLoadedButUnbound();
            return;
        }

        Track(Imports.RegisterSaveLoadAction(saveState, loadState, clearState, null, null, null));
    }

    /// <summary>Drops every registration made through this class. Call from your module's Unload().</summary>
    public static void Unload() {
        foreach (object handle in handles) {
            Imports.Unregister?.Invoke(handle);
        }

        handles.Clear();
    }

    private static void Track(object? handle) {
        if (handle != null) {
            handles.Add(handle);
        }
    }

    private static bool warned;

    private static void WarnIfLoadedButUnbound() {
        if (warned) {
            return;
        }

        warned = true;
        if (Everest.Modules?.Any(module => module.Metadata?.Name == "SpeedrunTool") == true) {
            Logger.Warn("SrtImports",
                "SpeedrunTool is loaded but its SaveLoad exports are not bound yet, so the registration was dropped. " +
                "Call Init() from Initialize(), which Everest runs after every mod's Load.");
        }
    }
    /// <summary>
    /// The raw ModInterop fields, filled in by Init(). Use them directly only for what Init() does
    /// not cover -- RegisterSaveLoadAction for instance state (its callbacks write into the per-slot
    /// dictionary), the cloner rules, IgnoreSaveState, DeepClone.
    /// </summary>
    [ModImportName("SpeedrunTool.SaveLoad")]
    public static class Imports {
        public delegate object RegisterStaticTypesDelegate(Type type, params string[] memberNames);

        public static RegisterStaticTypesDelegate? RegisterStaticTypes;

        public delegate object RegisterSaveLoadActionDelegate(
            Action<Dictionary<Type, Dictionary<string, object>>, Level> saveState,
            Action<Dictionary<Type, Dictionary<string, object>>, Level> loadState,
            Action? clearState,
            Action<Level>? beforeSaveState,
            Action<Level>? beforeLoadState,
            Action? preCloneEntities);

        public static RegisterSaveLoadActionDelegate? RegisterSaveLoadAction;

        public static Action<object>? Unregister;

        public static Action<Entity, bool>? IgnoreSaveState;

        public static Action<Type>? AddReturnSameObjectType;

        public static Action<Type>? RemoveReturnSameObjectType;

        public static Action<Func<Type, bool>>? AddReturnSameObjectProcessor;

        public static Action<Func<Type, bool>>? RemoveReturnSameObjectProcessor;

        public static Action<Func<object, object>>? AddCustomDeepCloneProcessor;

        public static Action<Func<object, object>>? RemoveCustomDeepCloneProcessor;

        public static Func<object, object>? DeepClone;
    }
}
#endif

/// <summary>
/// The entry point for other mods. Most mods need exactly one thing from this file --
/// <see cref="SaveLoadExports.RegisterStaticTypes"/> -- to hand over the static state that has to
/// survive a savestate. A mod whose state lives on instances uses
/// <see cref="SaveLoadExports.RegisterSaveLoadAction"/> instead; the cloner hooks are for types the
/// cloner would otherwise copy wrongly.
/// <para>
/// The surfaces most mods never need are under <c>ModInterop/Advanced</c>: driving the pipeline
/// (SpeedrunTool.SaveLoadControl), TAS save/load (SpeedrunTool.TasAction), benchmarks
/// (SpeedrunTool.Bench) and the desync oracle (SpeedrunTool.Desync).
/// </para>
/// </summary>
public static class SaveLoadInterop {
    private static readonly List<Func<Type, bool>> returnSameObjectPredicates = new();
    private static readonly List<Func<object, object>> customDeepCloneProcessors = new();
    private static readonly HashSet<Type> returnSameObjectTypes = new();

    // RegisterStaticTypes() is called by several mods for the same static types. Because mods are
    // never unloaded in a running session (a mod unload requires restarting the game in practice),
    // it is enough to remember identical registrations and hand back the already registered
    // action instead of adding duplicate actions/reflection work.
    private static readonly Dictionary<string, SaveLoadAction> registeredStaticTypeActions = new();

    internal static bool CanReturnSameObject(Type type) {
        return returnSameObjectTypes.Contains(type) || returnSameObjectPredicates.Any(predicate => predicate(type));
    }

    internal static object CustomDeepCloneObject(object sourceObject) {
        foreach (Func<object, object> processor in customDeepCloneProcessors) {
            if (processor.Invoke(sourceObject) is { } clonedObject) {
                return clonedObject;
            }
        }

        return null;
    }

    [Load]
    private static void Load() {
        typeof(SaveLoadExports).ModInterop();
    }

    // ReSharper disable once MemberCanBePrivate.Global
    [ModExportName("SpeedrunTool.SaveLoad")]
    public static class SaveLoadExports {
        /// <summary>
        /// Saves and restores the named static members of <paramref name="type"/>: fields and
        /// properties, public or private. Several mods registering the same type and names share
        /// one action, so a second registration is not a duplicate save.
        /// <para>
        /// An empty <paramref name="memberNames"/> registers an action that saves nothing -- name
        /// at least one member, otherwise the call is a silent no-op.
        /// </para>
        /// </summary>
        /// <param name="type">Type whose static members are snapshotted. Null returns null.</param>
        /// <param name="memberNames">Field or property names on <paramref name="type"/>.</param>
        /// <returns>Handle to pass to <see cref="Unregister"/>, or null if nothing was registered.</returns>
        /// <remarks>Available since SpeedrunTool v3.26.4.</remarks>
        public static object RegisterStaticTypes(Type type, params string[] memberNames) {
            if (type is null) {
                return null;
            }

            memberNames ??= [];
            if (memberNames.Length == 0) {
                Logger.Warn("SpeedrunTool/Interop",
                    $"RegisterStaticTypes({type.FullName}) was called without member names, so it will save nothing. " +
                    "Name the static members that should survive a savestate, e.g. RegisterStaticTypes(typeof(M), nameof(M.Foo)).");
            }

            string key = type.AssemblyQualifiedName + "|" + string.Join("", memberNames.OrderBy(name => name, StringComparer.Ordinal));
            if (registeredStaticTypeActions.TryGetValue(key, out SaveLoadAction existing)) {
                return existing;
            }

            SaveLoadAction action = (SaveLoadAction)SaveLoadAction.SafeAdd(
                (savedValues, _) => SaveLoadAction.SaveStaticMemberValues(savedValues, type, memberNames),
                (savedValues, _) => SaveLoadAction.LoadStaticMemberValues(savedValues),
                null, null, null, null
            );
            if (action is not null) {
                registeredStaticTypeActions[key] = action;
            }
            return action;
        }

        /// <summary>
        /// Saves and restores state that does not live in static members: the callbacks receive the
        /// per-slot dictionary to write into on save and to read from on load.
        /// <para>
        /// Putting values in a plain field instead of the dictionary works with a single save slot
        /// but not with several -- the dictionary is what makes a savestate per-slot.
        /// </para>
        /// </summary>
        /// <param name="saveState">Called before a savestate is captured. May be null.</param>
        /// <param name="loadState">Called after a savestate is restored. May be null.</param>
        /// <param name="clearState">Called when the state is cleared. May be null.</param>
        /// <param name="beforeSaveState">Called earlier than <paramref name="saveState"/>, for
        /// entities that must be adjusted before the snapshot. May be null.</param>
        /// <param name="beforeLoadState">Called before <paramref name="loadState"/>. May be null.</param>
        /// <param name="preCloneEntities">Called when the entity graph is pre-cloned on the
        /// background thread, so it must not touch the save dictionary. May be null.</param>
        /// <returns>Handle to pass to <see cref="Unregister"/>, or null if nothing was registered.</returns>
        /// <remarks>Available since SpeedrunTool v3.26.4. Pass static methods, not calls on a singleton.</remarks>
        public static object RegisterSaveLoadAction(Action<Dictionary<Type, Dictionary<string, object>>, Level> saveState,
            Action<Dictionary<Type, Dictionary<string, object>>, Level> loadState, Action clearState,
            Action<Level> beforeSaveState, Action<Level> beforeLoadState, Action preCloneEntities) {
            return SaveLoadAction.SafeAdd(saveState, loadState, clearState, beforeSaveState, beforeLoadState, preCloneEntities);
        }

        /// <summary>
        /// Removes an action returned by <see cref="RegisterSaveLoadAction"/> or
        /// <see cref="RegisterStaticTypes"/>. Anything else -- including null -- is ignored.
        /// Call it from [Unload] so a reloaded mod does not keep a dangling registration.
        /// </summary>
        /// <param name="obj">The handle returned by a registration call.</param>
        /// <remarks>Available since SpeedrunTool v3.26.4.</remarks>
        public static void Unregister(object obj) {
            if (obj is SaveLoadAction action) {
                foreach (KeyValuePair<string, SaveLoadAction> pair in registeredStaticTypeActions
                             .Where(pair => ReferenceEquals(pair.Value, action))
                             .ToList()) {
                    registeredStaticTypeActions.Remove(pair.Key);
                }
                SaveLoadAction.Remove(action);
            }
        }

        /// <summary>
        /// Keeps an entity out of the snapshot: it is removed before saving and added back into the
        /// level after loading. Use it for entities that cannot be cloned meaningfully (a live
        /// native handle, a bridge to another mod).
        /// </summary>
        /// <param name="entity">Entity to exclude.</param>
        /// <param name="based">When true the entity's Added/Removed calls are not triggered by this.</param>
        /// <remarks>Available since SpeedrunTool v3.26.4.</remarks>
        public static void IgnoreSaveState(Entity entity, bool based = false) {
            IgnoreSaveLoadComponent.Ignore(entity, based);
        }

        /// <summary>
        /// Same as the entity overload, for every entity of a type: excluded before saving,
        /// re-added after loading.
        /// </summary>
        /// <param name="entityType">Entity type to exclude.</param>
        /// <param name="based">When true the entities' Added/Removed calls are not triggered by this.</param>
        /// <remarks>Available since SpeedrunTool v3.27.15.</remarks>
        public static void IgnoreSaveState(Type entityType, bool based = false) {
            IgnoreSaveLoadComponent.Ignore(entityType, based);
        }

        /// <summary>
        /// Teaches the cloner that objects of this type must be shared instead of copied. For entity
        /// types this means the entity is kept out of the snapshot as well.
        /// <para>
        /// Must run before the first save/load of the session: cloners are generated and cached per
        /// type, so a later registration keeps the decision made earlier.
        /// </para>
        /// </summary>
        /// <remarks>Available since SpeedrunTool v3.27.16.</remarks>
        public static void AddReturnSameObjectType(Type type) {
            if (type.IsSameOrSubclassOf(typeof(Entity))) {
                IgnoreSaveLoadComponent.Ignore(type, false);
            }
            else {
                returnSameObjectTypes.Add(type);
                // NOTE: cloners are generated and cached per type, so this must be called before
                // the first save/load; registering later keeps the old decision (audit D14).
                // (Clearing DeepClonerSafeTypes.KnownTypes here is not possible: it is pre-seeded
                // with types like System.RuntimeType and clearing it crashes the game natively.)
            }
        }

        /// <summary>
        /// Undoes <see cref="AddReturnSameObjectType"/>. It cannot undo a clone decision that was
        /// already made for a type it had to clone.
        /// </summary>
        /// <remarks>Available since SpeedrunTool v3.27.16.</remarks>
        public static void RemoveReturnSameObjectType(Type type) {
            if (type.IsSameOrSubclassOf(typeof(Entity))) {
                IgnoreSaveLoadComponent.RemoveIgnore(type);
            }
            else {
                returnSameObjectTypes.Remove(type);
            }
        }

        /// <summary>
        /// Predicate form of <see cref="AddReturnSameObjectType"/>, for whole families of types
        /// (for example an interface several mods implement).
        /// <para>Must run before the first save/load, see <see cref="AddReturnSameObjectType"/>.</para>
        /// </summary>
        /// <param name="predicate">Returning true means objects of that type are shared, not cloned.</param>
        /// <remarks>Available since SpeedrunTool v3.26.4.</remarks>
        public static void AddReturnSameObjectProcessor(Func<Type, bool> predicate) {
            returnSameObjectPredicates.Add(predicate);
        }

        /// <summary>
        /// Removes a predicate added by <see cref="AddReturnSameObjectProcessor"/>.
        /// </summary>
        /// <remarks>Available since SpeedrunTool v3.26.4.</remarks>
        public static void RemoveReturnSameObjectProcessor(Func<Type, bool> predicate) {
            returnSameObjectPredicates.Remove(predicate);
        }

        /// <summary>
        /// Takes over the cloning of some objects: return the original, build a replacement, or
        /// return null to let the normal deep clone run.
        /// </summary>
        /// <param name="processor">Return an object to use it, or null for normal cloning.</param>
        /// <remarks>Available since SpeedrunTool v3.26.4.</remarks>
        public static void AddCustomDeepCloneProcessor(Func<object, object> processor) {
            customDeepCloneProcessors.Add(processor);
        }

        /// <summary>
        /// Removes a processor added by <see cref="AddCustomDeepCloneProcessor"/>.
        /// </summary>
        /// <remarks>Available since SpeedrunTool v3.26.4.</remarks>
        public static void RemoveCustomDeepCloneProcessor(Func<object, object> processor) {
            customDeepCloneProcessors.Remove(processor);
        }

        /// <summary>
        /// Deep copy of an object and its graph, using the same cloner and clone state as the
        /// save/load pipeline.
        /// </summary>
        /// <param name="from">Object to copy.</param>
        /// <returns>The copy.</returns>
        /// <remarks>
        /// Available since SpeedrunTool v3.26.4 (multi save slot update).
        /// Main thread only: the clone state is not thread safe.
        /// </remarks>
        public static object DeepClone(object from) {
            return from.DeepCloneShared();
        }
    }
}
