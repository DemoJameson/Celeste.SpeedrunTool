using System.Collections.Generic;

namespace Celeste.Mod.SpeedrunTool.SaveLoad;

/// <summary>
/// Types SRT queries while saving and loading. Tracking them at Initialize means every Level's tracker is
/// built with them already, so a query is an index lookup instead of a walk over every entity in the room.
/// <para>
/// <c>Tracker.AddTypeToTracker</c> only affects trackers created after the call, and Initialize runs before
/// any Level exists, so this costs one version bump at startup and nothing afterwards.
/// </para>
/// </summary>
internal static class TrackerWarmup {
    // type -> whether its subclasses are tracked under it as well. AddTypeToTracker only affects
    // trackers created after the call, so everything is applied once in Initialize.
    private static readonly Dictionary<Type, bool> Types = [];
    private static bool initialized;

    internal static void Register(Type type, bool inheritAll = false) {
        if (type is null) {
            return;
        }

        if (Types.TryGetValue(type, out bool already) && (already || !inheritAll)) {
            return;
        }

        Types[type] = inheritAll;
        if (initialized) {
            // Initialize already ran and a dependency registered a type later: apply it now. The next
            // Level is still built with it, because AddTypeToTracker bumps the version.
            Tracker.AddTypeToTracker(type, null, inheritAll);
        }
    }

    [Initialize]
    private static void Initialize() {
        // inheritAll matters: the actions that query these types used to use FindAll<T>, which includes
        // subclasses. Strawberry is not one type in practice -- 33 installed mods ship their own
        // subclass, and the golden ones (CornerGoldenBerry, JumplessGoldenBerry, ...) are exactly what
        // the saved-state recolor marks. SoundEmitter gets it for the same reason, even though no
        // installed mod subclasses it today.
        Register(typeof(SoundEmitter), inheritAll: true);
        Register(typeof(Strawberry), inheritAll: true);

        foreach ((Type type, bool inheritAll) in Types) {
            Tracker.AddTypeToTracker(type, null, inheritAll);
        }

        initialized = true;
    }

    /// <summary>
    /// The level's indexed entities of that type, or an empty list when the query cannot be answered.
    /// Falls back to tracking on demand, so a type registered after this ran still works.
    /// </summary>
    internal static List<Entity> GetEntities(Level level, Type type) {
        if (level?.Tracker is null || type is null) {
            return [];
        }

        try {
            return level.Tracker.GetEntitiesTrackIfNeeded(type) ?? [];
        }
        catch (Exception exception) {
            Logger.LogDetailed(exception, "SpeedrunTool/TrackerWarmup");
            return [];
        }
    }
}
