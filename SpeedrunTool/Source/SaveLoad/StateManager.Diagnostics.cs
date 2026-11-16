using System.Diagnostics;

namespace Celeste.Mod.SpeedrunTool.SaveLoad;

/// <summary>
/// DEBUG-only diagnostics split out of StateManager so its main flow reads as plain calls.
/// [Conditional("DEBUG")] removes these calls -- and the evaluation of their arguments -- from
/// Release builds, and the state they use only exists in DEBUG.
/// </summary>
public sealed partial class StateManager {
    [Conditional("DEBUG")]
    internal void Debug_SaveStart() {
#if DEBUG
        DebugTool.MemoryTracker.CheckFragmentation("Save Start");
        debugBefore = DebugTool.MemoryTracker.TakeGcSample();
        debugSw.Restart();
#endif
    }

    [Conditional("DEBUG")]
    internal void Debug_SaveEnd() {
#if DEBUG
        Debug_Report("Save");
        DebugTool.MemoryTracker.CheckFragmentation("Save End");
        if (Log_WhenSaving) {
            SaveLoadAction.LogSavedValues(level: savedLevel);
        }
#endif
    }

    [Conditional("DEBUG")]
    internal void Debug_LoadStart() {
#if DEBUG
        if (Log_WhenLoading) {
            SaveLoadAction.LogSavedValues(level: savedLevel);
        }
        DebugTool.MemoryTracker.CheckFragmentation("Load Start");
        debugBefore = DebugTool.MemoryTracker.TakeGcSample();
        debugSw.Restart();
#endif
    }

    [Conditional("DEBUG")]
    internal void Debug_LoadEnd() {
#if DEBUG
        Debug_Report("Load");
        DebugTool.MemoryTracker.CheckFragmentation("Load End");
#endif
    }

    [Conditional("DEBUG")]
    internal static void Debug_CheckFragmentation(string stage) {
#if DEBUG
        DebugTool.MemoryTracker.CheckFragmentation(stage);
#endif
    }

    [Conditional("DEBUG")]
    internal static void Debug_LogKeptGlobalEntity(Entity entity) {
#if DEBUG
        string resources = DescribeGlobalResources(entity.GetType());
        if (resources.Length > 0) {
            Logger.Info("SpeedrunTool/UnloadLevel",
                $"Keeping global resource entity [{entity.GetType().FullName}]: {resources}");
        }
#endif
    }

#if DEBUG
    private static readonly Stopwatch debugSw = new();
    private static DebugTool.MemoryTracker.GcSample debugBefore;

    private static void Debug_Report(string stage) {
        if (!InGame_Profiling) {
            return;
        }

        Logger.Debug("SpeedrunTool", $"{stage,20}: {debugSw.ElapsedMilliseconds,7} ms");
        Logger.Debug("SpeedrunTool", $"{"Allocated",20}: {DebugTool.MemoryTracker.FormatDiff(debugBefore, DebugTool.MemoryTracker.TakeGcSample(), simplified: false)}");
        Logger.Debug("SpeedrunTool", $"{"ProcessMemoryUsage",20}: {Process.GetCurrentProcess().PrivateMemorySize64 / 1024.0 / 1024.0 / 1024.0,7:F2} GB");
    }

    // Reflection is only used once per type, on load. Cache the result: the same handful of
    // vanilla/mod global entities is encountered for every save slot.
    private static readonly System.Collections.Generic.Dictionary<Type, string> GlobalResourceDescriptionCache = [];

    private static string DescribeGlobalResources(Type type) {
        if (GlobalResourceDescriptionCache.TryGetValue(type, out string cached)) {
            return cached;
        }

        System.Collections.Generic.HashSet<string> names = [];
        try {
            for (Type current = type; current is not null && current != typeof(Entity); current = current.BaseType) {
                foreach (System.Reflection.FieldInfo field in current.GetFields(System.Reflection.BindingFlags.Instance |
                                                                           System.Reflection.BindingFlags.Public |
                                                                           System.Reflection.BindingFlags.NonPublic |
                                                                           System.Reflection.BindingFlags.DeclaredOnly)) {
                    if (ContainsGpuResource(field.FieldType)) {
                        names.Add(field.Name);
                    }
                }
            }
        }
        catch (Exception exception) {
            Logger.Warn("SpeedrunTool/UnloadLevel",
                $"Could not inspect global entity [{type.FullName}]: {exception.GetType().Name}: {exception.Message}");
        }

        System.Collections.Generic.List<string> sortedNames = [.. names];
        sortedNames.Sort(StringComparer.Ordinal);
        string result = string.Join(", ", sortedNames);
        GlobalResourceDescriptionCache[type] = result;
        return result;

        static bool ContainsGpuResource(Type fieldType) {
            if (fieldType.IsArray) {
                return ContainsGpuResource(fieldType.GetElementType());
            }
            if (fieldType.IsGenericType) {
                foreach (Type argument in fieldType.GetGenericArguments()) {
                    if (ContainsGpuResource(argument)) {
                        return true;
                    }
                }
                return false;
            }
            return typeof(VirtualAsset).IsAssignableFrom(fieldType)
                   || typeof(Microsoft.Xna.Framework.Graphics.GraphicsResource).IsAssignableFrom(fieldType)
                   || fieldType == typeof(Atlas);
        }
    }
#endif
}
