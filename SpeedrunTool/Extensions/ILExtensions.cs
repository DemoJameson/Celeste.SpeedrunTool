using System;
using Mono.Cecil.Cil;
using Monocle;
using MonoMod.Cil;

namespace Celeste.Mod.SpeedrunTool.Extensions {
    public static class IlExtensions {
        public static bool TryGotoNextAddCoroutine<T>(this ILCursor cursor, string methodName,
            out Instruction endInstruction) {
            endInstruction = null;


            if (cursor.TryGotoNext(instruction => instruction.MatchCallvirt<T>(methodName),
                instruction => instruction.OpCode == OpCodes.Ldc_I4_1,
                instruction => instruction.OpCode == OpCodes.Newobj,
                instruction => instruction.MatchCall(typeof(Entity).GetMethod("Add", new[] {typeof(Component)})))) {
                endInstruction = cursor.Instrs[cursor.Index + 4];

                cursor.GotoPrev(i => i.OpCode == OpCodes.Ldarg_0,
                    i => i.OpCode == OpCodes.Ldarg_0
                );

                while (cursor.Prev.OpCode == OpCodes.Ldarg_0) {
                    cursor.GotoPrev();
                }

                return true;
            }

            return false;
        }

        public static void SkipAddCoroutine<T>(this ILContext il, string methodName, Func<bool> condition) {
            ILCursor cursor = new ILCursor(il);
            if (cursor.TryGotoNextAddCoroutine<T>(methodName, out var endInstruction)) {
                Instruction startInstruction = cursor.Next;
                ILLabel startLabel = cursor.MarkLabel();
                cursor.EmitDelegate(condition);
                cursor.Emit(OpCodes.Brtrue, endInstruction);

                while (cursor.TryGotoPrev(i =>
                    i.Operand is ILLabel jumpLabel && jumpLabel.Target == startInstruction)) {
                    cursor.Next.Operand = startLabel;
                }
            }
        }
    }
}