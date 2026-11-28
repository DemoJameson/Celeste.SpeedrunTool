using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;

namespace Celeste.Mod.SpeedrunTool.SaveLoad.Profiling;

#if SRT_PROFILE
/// <summary>
/// Lightweight, Release-build profiling for the save/load pipeline.
/// Only compiled when the <c>SRT_PROFILE</c> symbol is defined
/// (<c>dotnet build -p:SrtProfiling=true</c>); the shipped Release build contains no call sites
/// at all (see the stub below), so this is free on low-end machines.
/// Enabled at runtime by <c>SRT_PROFILE=1</c> in the game's environment or by a file named
/// <c>srt-profile.flag</c> next to SpeedrunTool.dll, in the game's working directory, or in
/// <c>Mods/SpeedrunTool</c> under it (Everest may load mod assemblies from bytes, so the assembly
/// location is not always available).
///
/// Output goes to log.txt as one line per operation, e.g.
/// <code>SRT_PROFILE load beforeLoad=0.31 waitPreclone=0.02 mergeTime=1.10 unload=4.20 ... heapMB=... gc=...</code>
/// so performance work does not depend on a DEBUG build (whose JIT/GC behaviour differs).
/// </summary>
internal static class SaveLoadProfiler {
    internal static readonly bool Enabled = IsEnabled();
    internal static string EnabledBy { get; private set; }

    private static bool IsEnabled() {
        if (Environment.GetEnvironmentVariable("SRT_PROFILE") == "1") {
            EnabledBy = "SRT_PROFILE=1";
            return true;
        }

        try {
            foreach (string candidate in FlagCandidates()) {
                if (!string.IsNullOrEmpty(candidate) && File.Exists(candidate)) {
                    EnabledBy = candidate;
                    return true;
                }
            }
        }
        catch {
        }

        return false;
    }

    private static IEnumerable<string> CandidateDirectories() {
        string workingDirectory = Directory.GetCurrentDirectory();
        yield return workingDirectory;
        yield return Path.Combine(workingDirectory, "Mods", "SpeedrunTool");

        string assemblyLocation = typeof(SaveLoadProfiler).Assembly.Location;
        if (!string.IsNullOrEmpty(assemblyLocation)) {
            yield return Path.GetDirectoryName(assemblyLocation) ?? "";
        }
    }

    private static IEnumerable<string> FlagCandidates() {
        foreach (string directory in CandidateDirectories()) {
            if (!string.IsNullOrEmpty(directory)) {
                yield return Path.Combine(directory, "srt-profile.flag");
            }
        }
    }

    private static readonly Stopwatch Stopwatch = new();
    private static long lastTicks;
    private static readonly StringBuilder Line = new();
    private static bool loggedEnabled;

    internal static void Begin(string tag) {
        if (!Enabled) {
            return;
        }

        if (!loggedEnabled) {
            loggedEnabled = true;
            Logger.Info("SpeedrunTool/Profile", $"SRT_PROFILE enabled via {EnabledBy}");
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
#else
/// <summary>
/// Compile-time no-op stub for the normal Release build.
/// <see cref="ConditionalAttribute"/> on every method makes the C# compiler erase the call
/// sites from the assembly entirely when <c>SRT_PROFILE</c> is not defined, so the shipped
/// build has no profiling branch, string or allocation to pay for on a low-end machine.
/// Build a profiling variant with <c>dotnet build -p:SrtProfiling=true</c>.
/// </summary>
internal static class SaveLoadProfiler {
    [Conditional("SRT_PROFILE")]
    internal static void Begin(string tag) {
    }

    [Conditional("SRT_PROFILE")]
    internal static void Mark(string segment) {
    }

    [Conditional("SRT_PROFILE")]
    internal static void End() {
    }

    [Conditional("SRT_PROFILE")]
    internal static void LogBackgroundSection(string tag, long startTicks, long startAllocatedBytes) {
    }
}
#endif
