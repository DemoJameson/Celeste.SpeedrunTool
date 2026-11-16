using System.Diagnostics;

namespace Celeste.Mod.SpeedrunTool.SaveLoad;

/// <summary>
/// DEBUG-only repair diagnostics split out of GraphicResourcesHandler so its repair flow reads as
/// plain calls. [Conditional("DEBUG")] removes these calls from Release, and the stopwatch only
/// exists there.
/// </summary>
internal static partial class GraphicResourcesHandler {
    [Conditional("DEBUG")]
    internal static void Debug_RepairStart() {
#if DEBUG
        debugStopwatch.Restart();
#endif
    }

    [Conditional("DEBUG")]
    internal static void Debug_RepairEnd(Level level) {
#if DEBUG
        Logger.Info("SpeedrunTool/GraphicResourcesHandler",
            $"RepairDisposedGraphicsResources: {debugStopwatch.Elapsed.TotalMilliseconds:F2} ms, " +
            $"entities={level.Entities.Count}");
#endif
    }

    [Conditional("DEBUG")]
    internal static void Debug_LogReloadedAssets(int total, int reloaded) {
#if DEBUG
        Logger.Info("SpeedrunTool/GraphicResourcesHandler",
            $"Saved graph assets: {total}, reloaded {reloaded} disposed one(s)");
#endif
    }

#if DEBUG
    private static readonly Stopwatch debugStopwatch = new();
#endif
}
