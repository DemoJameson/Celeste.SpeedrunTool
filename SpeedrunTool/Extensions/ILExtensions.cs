using System;
using System.Collections.Generic;
using Mono.Cecil.Cil;
using Monocle;
using MonoMod.Cil;

namespace Celeste.Mod.SpeedrunTool.Extensions {
    public static class IlExtensions {
        public static bool TryGotoNextAddCoroutine<T>(this ILCursor cursor, string methodName, out Instruction skipInstruction, int instructionCounts = 6, MoveType moveType = MoveType.Before) {
            skipInstruction = null;
            
            List<Func<Instruction, bool>> predicates = new List<Func<Instruction, bool>> {
                instruction => instruction.OpCode == OpCodes.Ldarg_0,
                instruction => instruction.OpCode == OpCodes.Ldarg_0,
                instruction => instruction.MatchCallvirt<T>(methodName),
                instruction => instruction.OpCode == OpCodes.Ldc_I4_1,
                instruction => instruction.OpCode == OpCodes.Newobj,
                instruction => instruction.MatchCall(typeof(Entity).GetMethod("Add", new[] {typeof(Component)}))
            };

            for (int i = 6; i < instructionCounts; i++) {
                predicates.Insert(2, instruction => true);
            }
            
            if (cursor.TryGotoNext(moveType, predicates.ToArray())) {
                switch (moveType) {
                    case MoveType.Before:
                        skipInstruction = cursor.Instrs[cursor.Index + instructionCounts];
                        break;
                    case MoveType.After:
                        skipInstruction = cursor.Instrs[cursor.Index - instructionCounts];
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