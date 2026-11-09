using Celeste.Mod.SpeedrunTool.SaveLoad;

namespace SceneStateTests;

/// <summary>
/// Offline tests for the pure decision logic used by the cross-chapter (pending) load.
/// No game assembly is referenced: these are the "which route / should the watchdog abort" rules.
/// </summary>
internal static class Program {
    private static int failures;

    private static int Main() {
        // ---- Which load route? -------------------------------------------------------------
        ExpectRoute("same chapter, live Level -> historical direct restore",
            LoadStateRoute.Direct, LoadStateRouteDecider.Decide(hasLiveLevel: true, sameTargetArea: true));
        ExpectRoute("different chapter, live Level -> enter the saved chapter first",
            LoadStateRoute.Pending, LoadStateRouteDecider.Decide(hasLiveLevel: true, sameTargetArea: false));
        ExpectRoute("overworld (no Level) -> enter the saved chapter",
            LoadStateRoute.Pending, LoadStateRouteDecider.Decide(hasLiveLevel: false, sameTargetArea: false));
        ExpectRoute("no Level but a matching session was reported -> still pending",
            LoadStateRoute.Pending, LoadStateRouteDecider.Decide(hasLiveLevel: false, sameTargetArea: true));

        // ---- Pending load watchdog ---------------------------------------------------------
        // Still in the start scene during the scene-switch grace period: not a failure yet.
        ExpectFailure("grace period is not a failure", PendingLoadFailure.None,
            Decide(sameAsStart: true, grace: false));
        ExpectFailure("scene transition never started", PendingLoadFailure.TransitionDidNotStart,
            Decide(sameAsStart: true, grace: true));

        ExpectFailure("LevelEnter.ErrorMessage was set", PendingLoadFailure.LevelEnterError,
            Decide(levelEnterError: true));
        ExpectFailure("game fell back to the chapter select", PendingLoadFailure.ReturnedToChapterSelect,
            Decide(overworld: true));
        ExpectFailure("a second LevelEnter after our LevelLoader (loader gave up)", PendingLoadFailure.LevelLoaderGaveUp,
            Decide(levelEnter: true, seenEnter: true, enteredLoader: true));
        ExpectFailure("a Level.Begin we never finished is not the target", PendingLoadFailure.LoadedLevelIsNotTheTarget,
            Decide(level: true));
        ExpectFailure("a pending load that takes too long times out", PendingLoadFailure.TimedOut,
            Decide(timedOut: true));

        // Normal progress: LevelEnter -> LevelLoader, no failure; the timeout is still honoured later.
        ExpectFailure("first LevelEnter is normal progress", PendingLoadFailure.None,
            Decide(levelEnter: true, seenEnter: true, enteredLoader: false));
        ExpectFailure("a loading LevelLoader is normal progress", PendingLoadFailure.None,
            Decide(loader: false, timedOut: false));
        ExpectFailure("timeout fires even while the loader is still busy", PendingLoadFailure.TimedOut,
            Decide(timedOut: true));
        // A later LevelEnter after the LevelLoader is a failure even if a timeout also passed.
        ExpectFailure("loader-gave-up has priority over the timeout", PendingLoadFailure.LevelLoaderGaveUp,
            Decide(levelEnter: true, seenEnter: true, enteredLoader: true, timedOut: true));

        if (failures == 0) {
            Console.WriteLine("SceneStateTests: all checks passed.");
            return 0;
        }

        Console.WriteLine($"SceneStateTests: {failures} check(s) FAILED.");
        return 1;

        static PendingLoadFailure Decide(
            bool sameAsStart = false,
            bool grace = true,
            bool levelEnterError = false,
            bool overworld = false,
            bool level = false,
            bool levelEnter = false,
            bool seenEnter = false,
            bool enteredLoader = false,
            bool timedOut = false,
            bool loader = false) {
            // `loader` is informational only: a LevelLoader always presents the same inputs as any
            // other in-between scene (no failure flags set). Kept so the test reads naturally.
            _ = loader;
            return PendingLoadWatchdog.Decide(
                sameAsStartScene: sameAsStart,
                graceExpired: grace,
                levelEnterError: levelEnterError,
                sceneIsOverworld: overworld,
                sceneIsLevel: level,
                sceneIsLevelEnter: levelEnter,
                seenLevelEnter: seenEnter,
                enteredLevelLoader: enteredLoader,
                timedOut: timedOut);
        }
    }

    private static void ExpectRoute(string name, LoadStateRoute expected, LoadStateRoute actual) {
        if (expected == actual) {
            Console.WriteLine($"ok   route: {name}");
            return;
        }

        failures++;
        Console.WriteLine($"FAIL route: {name} (expected {expected}, got {actual})");
    }

    private static void ExpectFailure(string name, PendingLoadFailure expected, PendingLoadFailure actual) {
        if (expected == actual) {
            Console.WriteLine($"ok   watchdog: {name}");
            return;
        }

        failures++;
        Console.WriteLine($"FAIL watchdog: {name} (expected {expected}, got {actual})");
    }
}
