using Celeste.Mod.SpeedrunTool.Utils;
using MonoMod.Cil;

namespace Celeste.Mod.SpeedrunTool.SaveLoad.ThirdPartySupport;
internal static class MaxHelpingHandUtils {

    internal static void Support() {
        if (ModUtils.GetType("MaxHelpingHand", "Celeste.Mod.MaxHelpingHand.Entities.RainbowSpinnerColorController") is { } colorControllerType
            && colorControllerType.GetFieldInfo("rainbowSpinnerHueHooked") != null
            && Delegate.CreateDelegate(typeof(On.Celeste.CrystalStaticSpinner.hook_GetHue),
                    colorControllerType.GetMethodInfo("getRainbowSpinnerHue")) is
                On.Celeste.CrystalStaticSpinner.hook_GetHue hookGetHue
           ) {
            SaveLoadAction.InternalSafeAdd(
                loadState: (_, _) => {
                    if (colorControllerType.GetFieldValue<bool>("rainbowSpinnerHueHooked")) {
                        On.Celeste.CrystalStaticSpinner.GetHue -= hookGetHue;
                        On.Celeste.CrystalStaticSpinner.GetHue += hookGetHue;
                    } else {
                        On.Celeste.CrystalStaticSpinner.GetHue -= hookGetHue;
                    }
                }
            );
        }

        if (ModUtils.GetType("MaxHelpingHand", "Celeste.Mod.MaxHelpingHand.Entities.RainbowSpinnerColorAreaController") is { } colorAreaControllerType
            && colorAreaControllerType.GetFieldInfo("rainbowSpinnerHueHooked") != null
            && Delegate.CreateDelegate(typeof(On.Celeste.CrystalStaticSpinner.hook_GetHue),
                    colorAreaControllerType.GetMethodInfo("getRainbowSpinnerHue")) is
                On.Celeste.CrystalStaticSpinner.hook_GetHue hookGetHue2
           ) {
            SaveLoadAction.InternalSafeAdd(
                loadState: (_, _) => {
                    if (colorAreaControllerType.GetFieldValue<bool>("rainbowSpinnerHueHooked")) {
                        On.Celeste.CrystalStaticSpinner.GetHue -= hookGetHue2;
                        On.Celeste.CrystalStaticSpinner.GetHue += hookGetHue2;
                    } else {
                        On.Celeste.CrystalStaticSpinner.GetHue -= hookGetHue2;
                    }
                }
            );
        }

        if (ModUtils.GetType("MaxHelpingHand", "Celeste.Mod.MaxHelpingHand.Entities.SeekerBarrierColorController") is { } seekerBarrierColorControllerType
            && seekerBarrierColorControllerType.GetFieldInfo("seekerBarrierRendererHooked") != null
           ) {
            SaveLoadAction.InternalSafeAdd(
                loadState: (_, _) => {
                    if (seekerBarrierColorControllerType.GetFieldValue<bool>("seekerBarrierRendererHooked")) {
                        seekerBarrierColorControllerType.InvokeMethod("unhookSeekerBarrierRenderer");
                        seekerBarrierColorControllerType.InvokeMethod("hookSeekerBarrierRenderer");
                    } else {
                        seekerBarrierColorControllerType.InvokeMethod("unhookSeekerBarrierRenderer");
                    }
                }
            );
        }

        if (ModUtils.GetType("MaxHelpingHand", "Celeste.Mod.MaxHelpingHand.Triggers.GradientDustTrigger") is { } gradientDustTriggerType
            && gradientDustTriggerType.GetFieldInfo("hooked") != null
           ) {
            SaveLoadAction.InternalSafeAdd(
                loadState: (_, _) => {
                    if (gradientDustTriggerType.GetFieldValue<bool>("hooked")) {
                        gradientDustTriggerType.InvokeMethod("unhook");
                        gradientDustTriggerType.InvokeMethod("hook");
                    } else {
                        // hooked 为 true 时，unhook 方法才能够正常执行
                        gradientDustTriggerType.SetFieldValue("hooked", true);
                        gradientDustTriggerType.InvokeMethod("unhook");
                    }
                }
            );
        }

        if (ModUtils.GetType("MaxHelpingHand", "Celeste.Mod.MaxHelpingHand.Entities.ParallaxFadeOutController") is { } parallaxFadeOutControllerType
            && parallaxFadeOutControllerType.GetFieldInfo("backdropRendererHooked") != null
            && Delegate.CreateDelegate(typeof(ILContext.Manipulator),
                parallaxFadeOutControllerType.GetMethodInfo("onBackdropRender")) is ILContext.Manipulator onBackdropRender
           ) {
            SaveLoadAction.InternalSafeAdd(
                loadState: (_, _) => {
                    if (parallaxFadeOutControllerType.GetFieldValue<bool>("backdropRendererHooked")) {
                        IL.Celeste.BackdropRenderer.Render -= onBackdropRender;
                        IL.Celeste.BackdropRenderer.Render += onBackdropRender;
                    } else {
                        IL.Celeste.BackdropRenderer.Render -= onBackdropRender;
                    }
                }
            );
        }

        if (ModUtils.GetType("MaxHelpingHand", "Celeste.Mod.MaxHelpingHand.Entities.ParallaxFadeSpeedController") is { } parallaxFadeSpeedControllerType
            && parallaxFadeSpeedControllerType.GetFieldInfo("backdropHooked") != null
            && Delegate.CreateDelegate(typeof(ILContext.Manipulator),
                parallaxFadeSpeedControllerType.GetMethodInfo("modBackdropUpdate")) is ILContext.Manipulator modBackdropUpdate
           ) {
            SaveLoadAction.InternalSafeAdd(
                loadState: (_, _) => {
                    if (parallaxFadeSpeedControllerType.GetFieldValue<bool>("backdropHooked")) {
                        IL.Celeste.Parallax.Update -= modBackdropUpdate;
                        IL.Celeste.Parallax.Update += modBackdropUpdate;
                    } else {
                        IL.Celeste.Parallax.Update -= modBackdropUpdate;
                    }
                }
            );
        }

        SaveLoadAction.CloneModTypeFields("MaxHelpingHand", "Celeste.Mod.MaxHelpingHand.Effects.BlackholeCustomColors", "colorsMild");
    }
}
