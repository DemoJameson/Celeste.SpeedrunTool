using System;
using System.Linq;
using Celeste.Mod.SpeedrunTool.Extensions;
using Celeste.Mod.SpeedrunTool.RoomTimer;
using Microsoft.Xna.Framework;
using Mono.Cecil.Cil;
using Monocle;
using MonoMod.Cil;

namespace Celeste.Mod.SpeedrunTool.SaveLoad {
    public static class StateMarkUtils {
        private const string StartFromSaveSate = nameof(StartFromSaveSate);

        public static void OnLoad() {
            On.Celeste.Strawberry.Added += StrawberryOnAdded;
            IL.Celeste.SpeedrunTimerDisplay.DrawTime += SetSaveStateColor;

            SaveLoadAction.Add(new SaveLoadAction(loadState: (savedValues, level) => {
                // recolor golden berry
                foreach (Strawberry berry in level.Entities.FindAll<Strawberry>().Where(strawberry => strawberry.Golden)) {
                    if (TryRecolorSprite(berry)) {
                        break;
                    }
                }

                // recolor timer
                level.SetExtendedBoolean(StartFromSaveSate, true);
            }));
        }

        public static void OnUnload() {
            On.Celeste.Strawberry.Added -= StrawberryOnAdded;
            IL.Celeste.SpeedrunTimerDisplay.DrawTime -= SetSaveStateColor;
        }

        private static void StrawberryOnAdded(On.Celeste.Strawberry.orig_Added orig, Strawberry self, Scene scene) {
            orig(self, scene);
            if (self.Golden && scene.GetExtendedBoolean(StartFromSaveSate)) {
                TryRecolorSprite(self);
            }
        }

        private static bool TryRecolorSprite(Strawberry berry) {
            if (StateManager.Instance.SavedByTas) return true;
            if (berry.GetFieldValue("sprite") is not Sprite sprite) return false;
            string spriteId = "speedrun_tool_goldberry";
            if (berry.GetType().FullName == "Celeste.Mod.CollabUtils2.Entities.SpeedBerry") {
                spriteId = "speedrun_tool_speedberry";
            } else if (berry.GetType().FullName == "Celeste.Mod.CollabUtils2.Entities.SilverBerry") {
                spriteId = "speedrun_tool_silverberry";
            }

            GFX.SpriteBank.CreateOn(sprite, spriteId);

            return false;
        }

        // Copy from https://github.com/rhelmot/CelesteRandomizer/blob/master/Randomizer/Patches/sessionLifecycle.cs#L144
        private static void SetSaveStateColor(ILContext il) {
            ILCursor cursor = new ILCursor(il);
            if (!cursor.TryGotoNext(MoveType.After, instr => instr.MatchLdarg(3))) {
                return;
            }

            if (!cursor.TryGotoNext(MoveType.Before
                , instr => instr.MatchLdcI4(0)
                , instr => instr.OpCode == OpCodes.Stloc_S)) {
                return;
            }

            var afterInstr = cursor.MarkLabel();

            cursor.Index = 0;
            if (!cursor.TryGotoNext(MoveType.AfterLabel, instr => instr.MatchLdarg(3))) {
                return;
            }

            cursor.EmitDelegate<Func<bool>>(() =>
                SpeedrunToolModule.Settings.RoomTimerType == RoomTimerType.Off
                && Engine.Scene is Level {Completed: false} level && level.GetExtendedBoolean(StartFromSaveSate) && !StateManager.Instance.SavedByTas
            );

            var beforeInstr = cursor.DefineLabel();
            cursor.Emit(OpCodes.Brfalse, beforeInstr);

            cursor.Emit(OpCodes.Ldstr, "c2e6f2");
            cursor.Emit(OpCodes.Call, typeof(Calc).GetMethod("HexToColor", new[] {typeof(string)}));
            cursor.Emit(OpCodes.Ldarg, 6);
            cursor.Emit(OpCodes.Call, typeof(Color).GetMethod("op_Multiply"));
            cursor.Emit(OpCodes.Stloc, 5);

            cursor.Emit(OpCodes.Ldstr, "93c0cf");
            cursor.Emit(OpCodes.Call, typeof(Calc).GetMethod("HexToColor", new[] {typeof(string)}));
            cursor.Emit(OpCodes.Ldarg, 6);
            cursor.Emit(OpCodes.Call, typeof(Color).GetMethod("op_Multiply"));
            cursor.Emit(OpCodes.Stloc, 6);

            cursor.Emit(OpCodes.Br, afterInstr);
            cursor.MarkLabel(beforeInstr);
        }
    }
}