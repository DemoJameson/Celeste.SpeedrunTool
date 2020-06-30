using System;
using Mono.Cecil.Cil;
using Monocle;
using MonoMod.Cil;

namespace Celeste.Mod.SpeedrunTool.Extensions {
    public static class IlExtensions {
        public static bool TryGotoNextAddCoroutine<T>(this ILCursor cursor, string methodName, out Instruction skipInstruction, MoveType moveType = MoveType.Before) {
            skipInstruction = null;
            
            if (cursor.TryGotoNext(moveType,
                instruction => instruction.OpCode == OpCodes.Ldarg_0,
                instruction => instruction.OpCode == OpCodes.Ldarg_0,
                instruction => instruction.MatchCallvirt<T>(methodName),
                instruction => instruction.OpCode == OpCodes.Ldc_I4_1,
                instruction => instruction.OpCode == OpCodes.Newobj,
                instruction => instruction.MatchCall(typeof(Entity).GetMethod("Add", new[] {typeof(Component)})))) {
                switch (moveType) {
                    case MoveType.Before:
                        skipInstruction = cursor.Instrs[cursor.Index + 6];
                        break;
                    case MoveType.After:
                        skipInstruction = cursor.Instrs[cursor.Index - 6];
                        break;
                    default:
                        throw new ArgumentException("MoveType only allow MoveType.Before and MoveType.After");
                }

                return true;
            }
            
            return false;
        }
        
        public static bool TryGotoPrevAddCoroutine<T>(this ILCursor cursor, string methodName, out Instruction skipInstruction, MoveType moveType = MoveType.Before) {
            skipInstruction = null;
            
            if (cursor.TryGotoPrev(moveType,
                instruction => instruction.OpCode == OpCodes.Ldarg_0,
                instruction => instruction.OpCode == OpCodes.Ldarg_0,
                instruction => instruction.MatchCallvirt<T>(methodName),
                instruction => instruction.OpCode == OpCodes.Ldc_I4_1,
                instruction => instruction.OpCode == OpCodes.Newobj,
                instruction => instruction.MatchCall(typeof(Entity).GetMethod("Add", new[] {typeof(Component)})))) {
                switch (moveType) {
                    case MoveType.Before:
                        skipInstruction = cursor.Instrs[cursor.Index + 6];
                        break;
                    case MoveType.After:
                        skipInstruction = cursor.Instrs[cursor.Index - 6];
                        break;
                    default:
                        throw new ArgumentException("MoveType only allow MoveType.Before and MoveType.After");
                }

                return true;
            }
            
            return false;
        }

        public static void SkipAddCoroutine<T>(this ILContext il, string methodName, Func<bool> condition) {
            ILCursor cursor = new ILCursor(il);
            if (cursor.TryGotoNextAddCoroutine<T>(methodName, out var skipInstruction)) {
                cursor.EmitDelegate(condition);
                cursor.Emit(OpCodes.Brtrue, skipInstruction);
            }
        }
    }
}