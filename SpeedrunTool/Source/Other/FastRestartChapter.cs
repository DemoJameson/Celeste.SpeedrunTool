using System;
using Celeste.Mod.SpeedrunTool.Extensions;
using Mono.Cecil.Cil;
using Monocle;
using MonoMod.Cil;
using MonoMod.RuntimeDetour;

namespace Celeste.Mod.SpeedrunTool.Other {
    public static class FastRestartChapter {
        private static bool FastRestart => SpeedrunToolModule.Settings.Enabled && SpeedrunToolModule.Settings.FastRestartChapter;
        private static ILHook ilHook;

        [Load]
        private static void Load() {
            if (Type.GetType("Celeste.Level+<>c__DisplayClass150_0, Celeste")?.GetMethodInfo("<GiveUp>b__0") is { } methodInfo) {
                ilHook = new ILHook(methodInfo, Manipulator);
            }

            On.Celeste.Level.LoadLevel += LevelOnLoadLevel;
        }

        [Unload]
        private static void Unload() {
            ilHook?.Dispose();
            On.Celeste.Level.LoadLevel -= LevelOnLoadLevel;
        }

        private static void Manipulator(ILContext il) {
            ILCursor ilCursor = new(il);
            if (ilCursor.TryGotoNext(
                    MoveType.After,
                    ins => ins.OpCode == OpCodes.Ldfld && ins.Operand.ToString().EndsWith("::restartArea"),
                    ins => ins.OpCode == OpCodes.Brfalse_S
                )) {
                object skipScreenWipe = ilCursor.Prev.Operand;
                ilCursor.EmitDelegate<Func<bool>>(() => {
                    if (FastRestart && Engine.Scene is Level level) {
                        Engine.Scene = new LevelLoader(level.Session.Restart());
                        return true;
                    } else {
                        return false;
                    }
                });
                ilCursor.Emit(OpCodes.Brtrue_S, skipScreenWipe);
            }
        }

        private static void LevelOnLoadLevel(On.Celeste.Level.orig_LoadLevel orig, Level self, Player.IntroTypes playerIntro, bool isFromLoader) {
            if (FastRestart && isFromLoader) {
                playerIntro = Player.IntroTypes.Respawn;
            }

            orig(self, playerIntro, isFromLoader);
        }
    }
}