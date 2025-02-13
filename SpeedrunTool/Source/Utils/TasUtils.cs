using System.Reflection;
using System.Runtime.CompilerServices;
using TAS;
using TAS.Module;

namespace Celeste.Mod.SpeedrunTool.Utils;

internal static class TasUtils {
    private static bool hasGameplay;

    private static bool hasRunning_Latest; // CelesteTAS >= v3.42.0

    private static bool hasRunning_Legacy; // CelesteTAS < v3.42.0
    private static bool running_Latest {
        [MethodImpl(MethodImplOptions.NoInlining)]
        get => Manager.Running; // it's a property instead of a field now
    }

    // some people are still using CelesteTAS 3.39
    private static bool running_Legacy {
        get => (bool)running_LegacyFieldInfo.GetValue(null);
    }

    private static FieldInfo running_LegacyFieldInfo;

    private static bool showGamePlay {
        [MethodImpl(MethodImplOptions.NoInlining)]
        get => CelesteTasSettings.Instance?.ShowGameplay ?? true;
    }

    public static bool Running => hasRunning_Latest ? running_Latest : (hasRunning_Legacy && running_Legacy);
    public static bool HideGamePlay => hasGameplay && !showGamePlay;

    [Initialize]
    private static void Initialize() {
        hasGameplay = ModUtils.GetType("CelesteTAS", "TAS.Module.CelesteTasSettings")?.GetPropertyInfo("ShowGameplay") != null;
        hasRunning_Latest = ModUtils.GetType("CelesteTAS", "TAS.Manager")?.GetPropertyInfo("Running") != null;
        running_LegacyFieldInfo = ModUtils.GetType("CelesteTAS", "TAS.Manager")?.GetFieldInfo("Running");
        hasRunning_Legacy = running_LegacyFieldInfo != null;
    }
}