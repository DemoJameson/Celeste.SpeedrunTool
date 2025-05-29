using Celeste.Mod.SpeedrunTool.ModInterop;
using System.Reflection;
using System.Runtime.CompilerServices;

namespace Celeste.Mod.SpeedrunTool.Utils;

internal static class TasUtils {
    public static bool HideGamePlay => hasGameplay && !showGamePlay;

    private static bool hasGameplay;

    private static bool showGamePlay {
        [MethodImpl(MethodImplOptions.NoInlining)]
        get => TAS.Module.CelesteTasSettings.Instance?.ShowGameplay ?? true;
    }
    public static bool Running {
        get {
            if (TasImports.Installed) {
                // >= CelesteTAS v3.45.0
                return TasImports.ManagerIsRunning;
            }
            if (hasRunning_BeforeModInterop) {
                // v3.42 - 3.44
                return (bool)running_BeforeModInterop.GetValue(null);
            }
            if (hasRunning_Legacy) {
                // < v3.42
                return (bool)running_LegacyFieldInfo.GetValue(null);
            }
            return false;
        }
    }

    private static bool hasRunning_BeforeModInterop; // CelesteTAS >= v3.42.0

    private static bool hasRunning_Legacy; // CelesteTAS < v3.42.0
                                           // some people are still using CelesteTAS 3.39

    private static FieldInfo running_LegacyFieldInfo;

    private static PropertyInfo running_BeforeModInterop; // it's a property instead of a field now


    [Initialize]
    private static void Initialize() {
        hasGameplay = ModUtils.GetType("CelesteTAS", "TAS.Module.CelesteTasSettings")?.GetPropertyInfo("ShowGameplay") != null;
        running_BeforeModInterop = ModUtils.GetType("CelesteTAS", "TAS.Manager")?.GetPropertyInfo("Running");
        hasRunning_BeforeModInterop = running_BeforeModInterop != null;
        running_LegacyFieldInfo = ModUtils.GetType("CelesteTAS", "TAS.Manager")?.GetFieldInfo("Running");
        hasRunning_Legacy = running_LegacyFieldInfo != null;
    }
}