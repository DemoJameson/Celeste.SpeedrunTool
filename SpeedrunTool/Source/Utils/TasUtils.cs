using TAS;
using TAS.Module;

namespace Celeste.Mod.SpeedrunTool.Utils;

internal static class TasUtils {
    private static bool installed;
    private static bool running => Manager.Running;
    private static bool showGamePlay => CelesteTasSettings.Instance?.ShowGameplay ?? true;
    public static bool Running => installed && running;
    public static bool HideGamePlay => installed && !showGamePlay;

    [Initialize]
    private static void Initialize() {
        installed = ModUtils.GetType("CelesteTAS", "TAS.Manager") != null;
    }
}