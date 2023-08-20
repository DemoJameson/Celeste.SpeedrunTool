using System.Runtime.CompilerServices;
using TAS;
using TAS.Module;

namespace Celeste.Mod.SpeedrunTool.Utils;

internal static class TasUtils {
    private static bool installed;
    private static bool running {
        [MethodImpl(MethodImplOptions.NoInlining)]
        get => Manager.Running;
    }

    private static bool showGamePlay {
        [MethodImpl(MethodImplOptions.NoInlining)]
        get => CelesteTasSettings.Instance?.ShowGameplay ?? true;
    }

    public static bool Running => installed && running;
    public static bool HideGamePlay => installed && !showGamePlay;

    [Initialize]
    private static void Initialize() {
        installed = ModUtils.GetType("CelesteTAS", "TAS.Module.CelesteTasSettings")?.GetPropertyInfo("ShowGameplay") != null;
    }
}