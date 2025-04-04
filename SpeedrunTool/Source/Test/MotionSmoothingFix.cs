using Celeste.Mod.SpeedrunTool.Utils;
using Celeste.Mod.SpeedrunTool.SaveLoad;
using System.Reflection;
namespace Celeste.Mod.SpeedrunTool.Test;
internal static class MotionSmoothingFix {

    private static object handler;

    private static MethodInfo method;

    [Initialize]

    private static void Initialize() {
        if (ModUtils.GetType("MotionSmoothing", "Celeste.Mod.MotionSmoothing.MotionSmoothingModule")?.GetPropertyValue("Instance")?.GetPropertyValue("MotionSmoothing") is { } motionSmoothHandler &&            
            ModUtils.GetType("MotionSmoothing", "Celeste.Mod.MotionSmoothing.Smoothing.MotionSmoothingHandler")?.GetMethodInfo("SmoothAllObjects") is { } smoothAll){

            handler = motionSmoothHandler;
            method = smoothAll;

            // the original one is a method call of a singleton, which may be wrong after deepclone?
            // (note save load actions are deepcloned to each save slot)
            SaveLoadAction.SafeAdd(null, ((_, _) => {
                SmoothAllObjects();
            }), null, null, null);
        }
    }

    private static void SmoothAllObjects() {
        method.Invoke(handler, new object[] {});
    }
}
