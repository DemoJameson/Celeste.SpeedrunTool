using System;
using Celeste.Mod.SpeedrunTool.SaveLoad.RestoreActions.Base;
using Mono.Cecil.Cil;
using MonoMod.Cil;
using MonoMod.RuntimeDetour;

namespace Celeste.Mod.SpeedrunTool.SaveLoad.RestoreActions {
    public class KeyRestoreAction : RestoreAction {
        private ILHook origLoadLevelHook;

        public override void OnHook() {
            origLoadLevelHook = new ILHook(typeof(Level).GetMethod("orig_LoadLevel"), ModOrigLoadLevel);
        }

        public override void OnUnhook() {
            origLoadLevelHook.Dispose();
        }

        // skip level.orig_LoadLevel: foreach (EntityID key in Session.Keys) Add(new Key(player, key)); 
        // let's RestoreAction create the key.
        private void ModOrigLoadLevel(ILContext il) {
            ILCursor cursor = new ILCursor(il);

            if (!cursor.TryGotoNext(
                i => i.OpCode == OpCodes.Newobj &&
                     i.Operand.ToString().Contains(".Key::.ctor(Celeste.Player,Celeste.EntityID)")
            )) {
                return;
            }

            if (!cursor.TryGotoNext(
                MoveType.After,
                i => i.OpCode == OpCodes.Ldloca_S,
                i => i.OpCode == OpCodes.Constrained && i.Operand.ToString().Contains("Celeste.EntityID"),
                i => i.OpCode == OpCodes.Callvirt,
                i => i.OpCode == OpCodes.Endfinally
            )) {
                return;
            }

            Instruction skipInstruction = cursor.Next;

            if (!cursor.TryGotoPrev(i =>
                i.OpCode == OpCodes.Stfld && i.Operand.ToString().Contains("CameraUpwardMaxY"))) {
                return;
            }

            cursor.Index++;
            cursor.EmitDelegate<Func<bool>>(() => IsLoadStart);
            cursor.Emit(OpCodes.Brtrue, skipInstruction);
        }
    }
}