using Celeste.Mod.SpeedrunTool.Utils;
using Mono.Cecil.Cil;
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
            && ModUtils.GetType("MotionSmoothing", "Celeste.Mod.MotionSmoothing.Smoothing.MotionSmoothingHandler")
                ?.GetMethodInfo("Load") is { } load
            ) {

            MotionSmoothingInstalled = true;
            handler = motionSmoothHandler;
            method = smoothAll;

            // the original one is a method call of a singleton, which may be wrong after deepclone?
            // (note save load actions are deepcloned to each save slot)
            SaveLoadAction.SafeAdd(
                null,
                loadState: (_, _) => {
                    SmoothAllObjects();
                },
                null, null, null
            );

            // skip the original RegisterSaveLoadAction
            load.ILHook((cursor, _) => {
                cursor.Index += 2;
                cursor.Emit(OpCodes.Ret);
            });
        }
    }

    private static void SmoothAllObjects() {
        method.Invoke(handler, Array.Empty<object>());
    }
}