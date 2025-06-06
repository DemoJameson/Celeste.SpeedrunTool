
using Celeste.Mod.SpeedrunTool.Utils;

namespace Celeste.Mod.SpeedrunTool.SaveLoad.ThirdPartySupport;
internal static class SpringCollab2020Utils {


    internal static void Support() {
        if (ModUtils.GetType("SpringCollab2020", "Celeste.Mod.SpringCollab2020.Entities.RainbowSpinnerColorController") is { } colorControllerType
            && colorControllerType.GetFieldInfo("colorControllerType") != null
            && Delegate.CreateDelegate(typeof(On.Celeste.CrystalStaticSpinner.hook_GetHue),
                    colorControllerType.GetMethodInfo("getRainbowSpinnerHue")) is
                On.Celeste.CrystalStaticSpinner.hook_GetHue hookGetHue
           ) {
            SaveLoadAction.InternalSafeAdd(
                loadState: (_, _) => {
                    if (colorControllerType.GetFieldValue<bool>("rainbowSpinnerHueHooked")) {
                        On.Celeste.CrystalStaticSpinner.GetHue -= hookGetHue;
                        On.Celeste.CrystalStaticSpinner.GetHue += hookGetHue;
                    }
                    else {
                        On.Celeste.CrystalStaticSpinner.GetHue -= hookGetHue;
                    }
                }
            );
        }

        if (ModUtils.GetType("SpringCollab2020", "Celeste.Mod.SpringCollab2020.Entities.RainbowSpinnerColorAreaController") is { } colorAreaControllerType
            && colorAreaControllerType.GetFieldInfo("rainbowSpinnerHueHooked") != null
            && Delegate.CreateDelegate(typeof(On.Celeste.CrystalStaticSpinner.hook_GetHue),
                    colorAreaControllerType.GetMethodInfo("getRainbowSpinnerHue")) is
                On.Celeste.CrystalStaticSpinner.hook_GetHue hookSpinnerGetHue
           ) {
            SaveLoadAction.InternalSafeAdd(
                loadState: (_, _) => {
                    if (colorAreaControllerType.GetFieldValue<bool>("rainbowSpinnerHueHooked")) {
                        On.Celeste.CrystalStaticSpinner.GetHue -= hookSpinnerGetHue;
                        On.Celeste.CrystalStaticSpinner.GetHue += hookSpinnerGetHue;
                    }
                    else {
                        On.Celeste.CrystalStaticSpinner.GetHue -= hookSpinnerGetHue;
                    }
                }
            );
        }

        if (ModUtils.GetType("SpringCollab2020", "Celeste.Mod.SpringCollab2020.Entities.SpikeJumpThroughController") is { } spikeJumpThroughControllerType
            && spikeJumpThroughControllerType.GetFieldInfo("SpikeHooked") != null
            && Delegate.CreateDelegate(typeof(On.Celeste.Spikes.hook_OnCollide),
                spikeJumpThroughControllerType.GetMethodInfo("OnCollideHook")) is On.Celeste.Spikes.hook_OnCollide onCollideHook
           ) {
            SaveLoadAction.InternalSafeAdd(
                loadState: (_, _) => {
                    if (spikeJumpThroughControllerType.GetFieldValue<bool>("SpikeHooked")) {
                        On.Celeste.Spikes.OnCollide -= onCollideHook;
                        On.Celeste.Spikes.OnCollide += onCollideHook;
                    }
                    else {
                        On.Celeste.Spikes.OnCollide -= onCollideHook;
                    }
                }
            );
        }
    }
}
