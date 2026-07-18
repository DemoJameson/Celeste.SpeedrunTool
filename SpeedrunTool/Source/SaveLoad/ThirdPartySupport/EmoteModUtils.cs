using Celeste.Mod.SpeedrunTool.Utils;
using System.Collections.Generic;

namespace Celeste.Mod.SpeedrunTool.SaveLoad.ThirdPartySupport;
internal static class EmoteModUtils {

    internal static bool Installed;

    // https://discord.com/channels/403698615446536203/455088502548463627/1410423111332921495
    // Make this feature toggleable
    private static bool ShouldSave => !ModSettings.EmoteModPULP;

    [Initialize]
    private static void Initialize() {
        Installed = ModUtils.IsInstalled("EmoteMod");
    }
    internal static void Support() {
        if (!Installed || ModUtils.GetType("EmoteMod", "Celeste.Mod.EmoteMod.GravityModule")?.GetFieldInfo("playerY") is not { } fieldInfo) {
            return;
        }

        SaveLoadAction.InternalSafeAdd(
            (savedValues, _) => {
                if (!ShouldSave) {
                    return;
                }
                savedValues[typeof(EmoteModUtils)] = new Dictionary<string, object>() {
                    ["playerY"] = fieldInfo.GetValue(null)
                };
            },
            (savedValues, _) => {
                if (!ShouldSave) {
                    return;
                }
                if (savedValues.TryGetValue(typeof(EmoteModUtils), out Dictionary<string, object> dict)
                    && dict.TryGetValue("playerY", out object y) && y is float playerY) {
                    fieldInfo.SetValue(null, playerY);
                }
            }
        );
    }
}
