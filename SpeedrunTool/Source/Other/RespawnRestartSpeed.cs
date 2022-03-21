using System.Collections.Generic;
using Celeste.Mod.SpeedrunTool.Utils;
using Mono.Cecil.Cil;
using MonoMod.Cil;
using MonoMod.RuntimeDetour;

namespace Celeste.Mod.SpeedrunTool.Other;

public static class RespawnRestartSpeed {
    private static ILHook ilHook;

    [Load]
    private static void Load() {
        using (new DetourContext {After = new List<string> {"*"}}) {
            On.Monocle.Engine.Update += RespawnSpeed;
        }


        if (ModUtils.VanillaAssembly.GetType("Celeste.Level+<>c__DisplayClass150_0")?.GetMethodInfo("<GiveUp>b__0") is { } methodInfo) {
            ilHook = new ILHook(methodInfo, Manipulator);
        }
    }

    [Unload]
    private static void Unload() {
        On.Monocle.Engine.Update -= RespawnSpeed;
        ilHook?.Dispose();
    }

    private static void RespawnSpeed(On.Monocle.Engine.orig_Update orig, Engine self, GameTime time) {
        orig(self, time);

        if (!ModSettings.Enabled || ModSettings.RespawnSpeed == 1 && ModSettings.RestartChapterSpeed == 1 || TasUtils.Running) {
            return;
        }

        if (Engine.Scene is not Level level) {
            return;
        }

        if (level.Paused) {
            return;
        }

        Player player = level.GetPlayer();

        // 加速复活过程
        for (int i = 1; i < ModSettings.RespawnSpeed && (player == null || player.StateMachine.State == Player.StIntroRespawn); i++) {
            orig(self, time);
        }

        // 加速章节启动
        for (int i = 1; i < ModSettings.RestartChapterSpeed && RequireFastRestart(level, player); i++) {
            orig(self, time);
        }
    }

    private static bool RequireFastRestart(Level level, Player player) {
        if (level.Session.GetFlag("StopFastRestart")) {
            return false;
        }

        bool result = !level.TimerStarted && level.Session.Area.ID != 8 && !level.SkippingCutscene && player?.StateMachine.State != Player.StIntroRespawn ||
                      level.TimerStarted && !level.InCutscene && level.Session.FirstLevel && player?.InControl != true;

        if (!result) {
            level.Session.SetFlag("StopFastRestart");
        }

        return result;
    }

    // 移除重启章节前面的黑屏
    private static void Manipulator(ILContext il) {
        ILCursor ilCursor = new(il);
        if (ilCursor.TryGotoNext(
                MoveType.After,
                ins => ins.OpCode == OpCodes.Ldfld && ins.Operand.ToString().EndsWith("::restartArea"),
                ins => ins.OpCode == OpCodes.Brfalse_S
            )) {
            object skipScreenWipe = ilCursor.Prev.Operand;
            ilCursor.EmitDelegate<Func<bool>>(() => {
                if (ModSettings.Enabled && ModSettings.SkipRestartChapterScreenWipe && Engine.Scene is Level level && !TasUtils.Running) {
                    Engine.Scene = new LevelLoader(level.Session.Restart());
                    return true;
                } else {
                    return false;
                }
            });
            ilCursor.Emit(OpCodes.Brtrue_S, skipScreenWipe);
        }
    }
}