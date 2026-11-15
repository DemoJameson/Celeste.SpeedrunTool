#if DEBUG
global using static Celeste.Mod.SpeedrunTool.DebugTool.Config;

namespace Celeste.Mod.SpeedrunTool.DebugTool;
internal static class Config {
    // SaveLoadAction log
    public const bool Log_WhenSaving = false;
    public const bool Log_WhenLoading = false;
    public const bool Log_SavedLevelEntities = false;
    public const bool Log_AllActions = true;

    // Assets
    public const bool Log_Assets = false;
    public const bool Log_IDisposable = true;

    // Profile
    public const bool JetBrains_Profiling = false;
    public const bool InGame_Profiling = true;
    public const bool EachAction_Profilling = true;
    public const bool MemoryTracker_CheckFragmentation = true;


    [Load]
    private static void Load() {
        Logger.Warn("SpeedrunTool/DebugTool", "This is a debug build!");
    }
}
#endif