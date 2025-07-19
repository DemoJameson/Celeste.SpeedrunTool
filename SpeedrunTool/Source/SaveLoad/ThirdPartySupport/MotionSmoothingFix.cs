using Celeste.Mod.SpeedrunTool.Utils;
using System.Reflection;

namespace Celeste.Mod.SpeedrunTool.SaveLoad.ThirdPartySupport;
internal static class MotionSmoothingFix {

    internal static bool MotionSmoothingInstalled = false;

    private static object handler;

    private static MethodInfo method;

    [Initialize]

    private static void Initialize() {
        if (ModUtils.GetType("MotionSmoothing", "Celeste.Mod.MotionSmoothing.MotionSmoothingModule")
                ?.GetPropertyValue("Instance")?.GetPropertyValue("MotionSmoothing") is { } motionSmoothHandler
            && ModUtils.GetType("MotionSmoothing", "Celeste.Mod.MotionSmoothing.Smoothing.MotionSmoothingHandler")
                ?.GetMethodInfo("SmoothAllObjects") is { } smoothAll
            ) {

            MotionSmoothingInstalled = true;
            handler = motionSmoothHandler;
            method = smoothAll;

            // First remove MotionSmoothing's action from the SharedActions list
            // MotionSmoothing handles mod interop when loading (it shouldn't), so the action may have been added
            SaveLoadAction.Remove(action => action.loadState?.Target?.GetType().Assembly.GetName().Name == "MotionSmoothing");

            // the original one is a method call of a singleton, which technically would work since SRT v3.26.0
            // but it's not recommended anyway
            // we replace it by a static one
            SaveLoadAction.InternalSafeAdd(
                null,
                loadState: (_, _) => {
                    SmoothAllObjects();
                },
                null, null, null
            );
        }
    }

    private static void SmoothAllObjects() {
        method.Invoke(handler, Array.Empty<object>());
    }
}