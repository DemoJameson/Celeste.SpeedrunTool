using System.Diagnostics;
using System.Text;

namespace Celeste.Mod.SpeedrunTool.SaveLoad.Profiling;

/// <summary>
/// Lightweight, Release-build profiling for the save/load pipeline.
/// Enabled by setting the environment variable <c>SRT_PROFILE=1</c> before launching
/// the game; when disabled every method is a single branch on a readonly bool.
///
/// Output goes to log.txt as one line per operation, e.g.
/// <code>SRT_PROFILE load beforeLoad=0.31 waitPreclone=0.02 mergeTime=1.10 unload=4.20 ... heapMB=... gc=...</code>
/// so performance work does not depend on a DEBUG build (whose JIT/GC behaviour differs).
/// </summary>
internal static class SaveLoadProfiler {
    internal static readonly bool Enabled = Environment.GetEnvironmentVariable("SRT_PROFILE") == "1";

    private static readonly Stopwatch Stopwatch = new();
    private static long lastTicks;
    private static readonly StringBuilder Line = new();

    internal static void Begin(string tag) {
        if (!Enabled) {
            return;
        }

        Stopwatch.Restart();
        lastTicks = 0L;
        Line.Clear();
        Line.Append("SRT_PROFILE ").Append(tag);
    }

    internal static void Mark(string segment) {
        if (!Enabled) {
            return;
        }

        long now = Stopwatch.ElapsedTicks;
        double milliseconds = (now - lastTicks) * 1000.0 / Stopwatch.Frequency;
        lastTicks = now;
        Line.Append(' ').Append(segment).Append('=').Append(milliseconds.ToString("F2"));
    }

    internal static void End() {
        if (!Enabled) {
            return;
        }

        GCMemoryInfo info = GC.GetGCMemoryInfo();
        Line.Append(" heapMB=").Append((info.HeapSizeBytes / 1048576.0).ToString("F1"))
            .Append(" fragMB=").Append((info.FragmentedBytes / 1048576.0).ToString("F1"))
            .Append(" gc=").Append(GC.CollectionCount(0)).Append('/').Append(GC.CollectionCount(1)).Append('/')
            .Append(GC.CollectionCount(2));

        Logger.Info("SpeedrunTool/Profile", Line.ToString());
    }

    /// <summary>Logs a section that ran on a background thread (e.g. the pre-clone task).</summary>
    internal static void LogBackgroundSection(string tag, long startTicks, long startAllocatedBytes) {
        if (!Enabled) {
            return;
        }

        double milliseconds = (Stopwatch.GetTimestamp() - startTicks) * 1000.0 / Stopwatch.Frequency;
        long allocated = GC.GetAllocatedBytesForCurrentThread() - startAllocatedBytes;
        Logger.Info("SpeedrunTool/Profile",
            $"SRT_PROFILE {tag} ms={milliseconds:F1} allocMB={allocated / 1048576.0:F1}");
    }
}
