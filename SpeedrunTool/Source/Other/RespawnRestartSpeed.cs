using System;
using System.Collections.Generic;
using Celeste.Mod.SpeedrunTool.Extensions;
using Microsoft.Xna.Framework;
using Mono.Cecil.Cil;
using Monocle;
using MonoMod.Cil;
using MonoMod.RuntimeDetour;

namespace Celeste.Mod.SpeedrunTool.Other {
    public static class RespawnRestartSpeed {
        private static SpeedrunToolSettings Settings => SpeedrunToolModule.Settings;
        private static ILHook ilHook;

        [Load]
        private static void Load() {
            using (new DetourContext {After = new List<string> {"*"}}) {
                On.Monocle.Engine.Update += RespawnSpeed;
            }

            if (Type.GetType("Celeste.Level+<>c__DisplayClass150_0, Celeste")?.GetMethodInfo("<GiveUp>b__0") is { } methodInfo) {
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

            if (!Settings.Enabled || (Settings.RespawnSpeed == 1 && Settings.RestartChapterSpeed == 1)) {
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
            if (Settings.RespawnSpeed > 1 && (player == null || player.StateMachine.State == Player.StIntroRespawn)) {
                for (int i = 1; i < Settings.RespawnSpeed; i++) {
                    orig(self, time);
                }
            }

            // 加速章节启动
            if (Settings.RestartChapterSpeed > 1) {
                if (!level.TimerStarted && player?.StateMachine.State != Player.StIntroRespawn ||
                    level.TimerStarted && level.Session.FirstLevel && !level.InCutscene && player?.StateMachine.State == Player.StDummy
                    ) {
                    for (int i = 1; i < Settings.RestartChapterSpeed; i++) {
                        orig(self, time);
                    }
                }
            }
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
                    if (Settings.Enabled && Settings.RestartChapterSpeed > 1 && Engine.Scene is Level level) {
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
}