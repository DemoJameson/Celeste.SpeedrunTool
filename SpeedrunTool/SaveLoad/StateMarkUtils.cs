using System;
using System.Linq;
using Celeste.Mod.SpeedrunTool.Extensions;
using Monocle;
using MonoMod.Cil;

namespace Celeste.Mod.SpeedrunTool.SaveLoad {
    public static class StateMarkUtils {
        public static void OnLoad() {
            IL.Celeste.SpeedrunTimerDisplay.DrawTime += SetSaveStateColor;
            SaveLoadAction.Add(new SaveLoadAction(loadState: (savedValues, level) => {
                foreach (Strawberry berry in level.Entities.FindAll<Strawberry>().Where(strawberry => strawberry.Golden)) {
                    if (!(berry.GetFieldValue("sprite") is Sprite sprite)) return;
                    sprite.RemoveSelf();

                    sprite = SpeedrunToolModule.SpriteBank.Create("speedrun_tool_goldberry");
                    berry.SetFieldValue("sprite", sprite);
                    berry.Add(sprite);
                }
            }));
        }

        public static void OnUnload() {
            IL.Celeste.SpeedrunTimerDisplay.DrawTime -= SetSaveStateColor;
        }

        // Copy from https://github.com/rhelmot/CelesteRandomizer/blob/master/Randomizer/Patches/sessionLifecycle.cs#L144
        private static void SetSaveStateColor(ILContext il) {
            ILCursor cursor = new ILCursor(il);
            if (!cursor.TryGotoNext(MoveType.After, instr => instr.MatchLdarg(5))) {
                return;
            }

            if (!cursor.TryGotoNext(MoveType.Before, instr => instr.MatchLdcI4(0))) {
                return;
            }

            var afterInstr = cursor.MarkLabel();

            cursor.Index = 0;
            if (!cursor.TryGotoNext(MoveType.AfterLabel, instr => instr.MatchLdarg(5))) {
                return;
            }

            cursor.EmitDelegate<Func<bool>>(() => StateManager.Instance.IsSaved && SpeedrunToolModule.Settings.RoomTimerType == RoomTimerType.Off);

            var beforeInstr = cursor.DefineLabel();
            cursor.Emit(Mono.Cecil.Cil.OpCodes.Brfalse, beforeInstr);

            cursor.Emit(Mono.Cecil.Cil.OpCodes.Ldstr, "ffe0e0");
            cursor.Emit(Mono.Cecil.Cil.OpCodes.Call, typeof(Monocle.Calc).GetMethod("HexToColor", new[] {typeof(string)}));
            cursor.Emit(Mono.Cecil.Cil.OpCodes.Ldarg, 6);
            cursor.Emit(Mono.Cecil.Cil.OpCodes.Call, typeof(Microsoft.Xna.Framework.Color).GetMethod("op_Multiply"));
            cursor.Emit(Mono.Cecil.Cil.OpCodes.Stloc, 5);

            cursor.Emit(Mono.Cecil.Cil.OpCodes.Ldstr, "dda9a9");
            cursor.Emit(Mono.Cecil.Cil.OpCodes.Call, typeof(Monocle.Calc).GetMethod("HexToColor", new[] {typeof(string)}));
            cursor.Emit(Mono.Cecil.Cil.OpCodes.Ldarg, 6);
            cursor.Emit(Mono.Cecil.Cil.OpCodes.Call, typeof(Microsoft.Xna.Framework.Color).GetMethod("op_Multiply"));
            cursor.Emit(Mono.Cecil.Cil.OpCodes.Stloc, 6);

            cursor.Emit(Mono.Cecil.Cil.OpCodes.Br, afterInstr);
            cursor.MarkLabel(beforeInstr);
        }
    }
}