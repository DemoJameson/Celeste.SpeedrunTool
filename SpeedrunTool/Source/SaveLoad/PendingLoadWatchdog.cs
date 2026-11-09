namespace Celeste.Mod.SpeedrunTool.SaveLoad;

/// <summary>
/// How a load has to be performed. <see cref="Direct"/> keeps the historical fast path (the state is
/// cloned into the current Level); <see cref="Pending"/> first has to enter the saved chapter so the
/// game builds that area's map data / tilesets / audio (see StateManager.PendingLoad.cs).
/// </summary>
internal enum LoadStateRoute {
    Direct,
    Pending,
}

internal static class LoadStateRouteDecider {
    internal static LoadStateRoute Decide(bool hasLiveLevel, bool sameTargetArea) {
        return hasLiveLevel && sameTargetArea ? LoadStateRoute.Direct : LoadStateRoute.Pending;
    }
}

/// <summary>
/// Why a pending (cross-chapter) load was given up on. Pure decision logic so it can be unit-tested
/// without the game (see tests/SceneStateTests).
/// </summary>
internal enum PendingLoadFailure {
    None,
    TransitionDidNotStart,
    LevelEnterError,
    ReturnedToChapterSelect,
    LevelLoaderGaveUp,
    LoadedLevelIsNotTheTarget,
    TimedOut,
}

/// <summary>
/// Watchdog for a pending load: the game is frozen by <c>State = Loading</c> while the saved chapter
/// loads, so every way that transition can silently fail has to be detected and aborted (never leave
/// the player in a loading scene forever).
/// </summary>
internal static class PendingLoadWatchdog {
    internal static PendingLoadFailure Decide(
        bool sameAsStartScene,
        bool graceExpired,
        bool levelEnterError,
        bool sceneIsOverworld,
        bool sceneIsLevel,
        bool sceneIsLevelEnter,
        bool seenLevelEnter,
        bool enteredLevelLoader,
        bool timedOut) {
        if (sameAsStartScene) {
            // Engine.Scene is deferred to the end of the update, so a few frames of "nothing happened"
            // are expected. After that the transition was swallowed (e.g. another mod overwrote it).
            return graceExpired ? PendingLoadFailure.TransitionDidNotStart : PendingLoadFailure.None;
        }

        if (levelEnterError) {
            return PendingLoadFailure.LevelEnterError;
        }

        if (sceneIsOverworld) {
            return PendingLoadFailure.ReturnedToChapterSelect;
        }

        if (sceneIsLevelEnter && seenLevelEnter && enteredLevelLoader) {
            // We already had our LevelLoader; a second LevelEnter means it gave up and the game is
            // showing the "failed to load" postcard.
            return PendingLoadFailure.LevelLoaderGaveUp;
        }

        if (sceneIsLevel) {
            // Level.Begin should have finished or aborted the pending load already; reaching this
            // point means something intercepted the Level instance.
            return PendingLoadFailure.LoadedLevelIsNotTheTarget;
        }

        if (timedOut) {
            return PendingLoadFailure.TimedOut;
        }

        return PendingLoadFailure.None;
    }
}
