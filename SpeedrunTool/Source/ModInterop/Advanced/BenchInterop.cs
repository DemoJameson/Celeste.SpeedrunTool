using Celeste.Mod.SpeedrunTool.SaveLoad;
using MonoMod.ModInterop;

namespace Celeste.Mod.SpeedrunTool.ModInterop;

/// <summary>
/// Interop for benchmarks, automation and test harnesses - deliberately kept out of the public
/// <see cref="SaveLoadInterop"/> contract used by other mods. Everything here changes behaviour
/// that normal mods should not touch (for example the PreClone memory/speed trade-off or the
/// "allow save while the game is frozen" escape hatch for harnesses).
/// </summary>
public static class BenchInterop {
    [Load]
    private static void Load() {
        typeof(BenchExports).ModInterop();
    }

    [ModExportName("SpeedrunTool.Bench")]
    public static class BenchExports {
        /// <summary>
        /// Allow save/load while the game is frozen after a save/load (used by TAS/benchmark
        /// harnesses that drive the save/load pipeline frame by frame).
        /// </summary>
        public static void SetAllowSaveLoadWhenWaiting(bool allow) {
            StateManager.AllowSaveLoadWhenWaiting = allow;
        }

        /// <summary>
        /// Override the PreClone mode for this session (A/B benchmarking). Main thread only.
        /// </summary>
        public static void SetPreCloneEnabled(bool enabled) {
            StateManager.PreCloneEnabled = enabled;
        }

        public static bool IsPreCloneEnabled() {
            return StateManager.PreCloneEnabled;
        }
    }
}
