namespace Celeste.Mod.SpeedrunTool.Test;
internal class MotionSmoothingFix {
    /*
     * don't know why, something becomes null (not level.camera)
    [Initialize]

    public static void Initialize() {
        if (ModUtils.GetType("MotionSmoothing", "Celeste.Mod.MotionSmoothing.Smoothing.Targets.UnlockedCameraSmoother")?.GetMethodInfo("GetCameraOffset") is { } method) {
            method.ILHook((cursor, _) => {
                if (cursor.TryGotoNext(ins => ins.OpCode == OpCodes.Stloc_1)) {
                    Instruction target = cursor.Next;
                    cursor.Emit(OpCodes.Dup);
                    cursor.EmitDelegate(Checker);
                    cursor.Emit(OpCodes.Brtrue, target);
                    cursor.Emit(OpCodes.Pop);
                    cursor.EmitDelegate(GetZero);
                    cursor.Emit(OpCodes.Ret);
                }
            });
        }

        static bool Checker(object ob) {
            if (ob is null) {
                Logger.Log("srt", $"{(Engine.Scene as Level).Camera is null}");
            }
            return ob is not null;
        }

        static Vector2 GetZero() {
            return Vector2.Zero;
        }
    }
    */
}
