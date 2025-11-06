#if DEBUG
global using static Celeste.Mod.SpeedrunTool.DebugTool.Config;

namespace Celeste.Mod.SpeedrunTool.DebugTool;
internal static class Config {
    // SaveLoadAction log
    public const bool Log_WhenSaving = false;
    public const bool Log_WhenLoading = false;
    public const bool Log_SavedLevel = false;
    public const bool Log_AllActions = true;

    // Assets
    public const bool Log_Assets = false;

    // Profile
    public const bool JetBrains_Profiling = false;
    public const bool InGame_Profiling = true;
}
#endif