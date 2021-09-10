using System;
using System.Collections.Generic;
using System.Linq;
using Celeste.Mod.SpeedrunTool.Extensions;
using Celeste.Mod.SpeedrunTool.RoomTimer;
using Microsoft.Xna.Framework;
using Mono.Cecil.Cil;
using Monocle;
using MonoMod.Cil;

namespace Celeste.Mod.SpeedrunTool.Other {
    public static class StateMarkUtils {
        private const string StartFromSaveSate = nameof(StartFromSaveSate);

        public static void OnLoad() {
            On.Celeste.Strawberry.Added += StrawberryOnAdded;
            IL.Celeste.SpeedrunTimerDisplay.DrawTime += SetSaveStateColor;
            SaveLoadAction.Add(new SaveLoadAction(ReColor, ReColor));
        }

        public static void OnUnload() {
            On.Celeste.Strawberry.Added -= StrawberryOnAdded;
            IL.Celeste.SpeedrunTimerDisplay.DrawTime -= SetSaveStateColor;
        }

        private static void ReColor(Dictionary<Type, Dictionary<string, object>> savedValues, Level level) {
            if (StateManager.Instance.SavedByTas) {
                return;
            }

            // recolor golden berry
            foreach (Strawberry berry in level.Entities.FindAll<Strawberry>().Where(strawberry => strawberry.Golden)) {
                TryRecolorSprite(berry);
            }

            // recolor timer
            level.SetExtendedBoolean(StartFromSaveSate, true);
        }

        private static void StrawberryOnAdded(On.Celeste.Strawberry.orig_Added orig, Strawberry self, Scene scene) {
            orig(self, scene);
            if (self.Golden && !StateManager.Instance.SavedByTas && scene.GetExtendedBoolean(StartFromSaveSate)) {
                TryRecolorSprite(self);
            }
        }

        private static void TryRecolorSprite(Strawberry berry) {
            if (berry.GetFieldValue("sprite") is not Sprite sprite) {
                return;
            }

            string spriteId = berry.GetType().FullName switch {
                "Celeste.Mod.CollabUtils2.Entities.SpeedBerry" => "speedrun_tool_speedberry",
                "Celeste.Mod.CollabUtils2.Entities.SilverBerry" => "speedrun_tool_silverberry",
                _ => "speedrun_tool_goldberry"
            };

            GFX.SpriteBank.CreateOn(sprite, spriteId);

            return;
        }

        // Copy from https://github.com/rhelmot/CelesteRandomizer/blob/master/Randomizer/Patches/sessionLifecycle.cs#L144
        private static void SetSaveStateColor(ILContext il) {
            ILCursor cursor = new(il);
            if (!cursor.TryGotoNext(MoveType.After, instr => instr.MatchLdarg(3))) {
                return;
            }

            if (!cursor.TryGotoNext(MoveType.Before
                , instr => instr.MatchLdcI4(0)
                , instr => instr.OpCode == OpCodes.Stloc_S)) {
                return;
            }

            ILLabel afterInstr = cursor.MarkLabel();

            cursor.Index = 0;
            if (!cursor.TryGotoNext(MoveType.AfterLabel, instr => instr.MatchLdarg(3))) {
                return;
            }

            cursor.EmitDelegate<Func<bool>>(() =>
                SpeedrunToolModule.Settings.RoomTimerType == RoomTimerType.Off
                && Engine.Scene is Level {Completed: false} level && level.GetExtendedBoolean(StartFromSaveSate) && !StateManager.Instance.SavedByTas
            );

            ILLabel beforeInstr = cursor.DefineLabel();
            cursor.Emit(OpCodes.Brfalse, beforeInstr);

            cursor.Emit(OpCodes.Ldstr, "afdded");
            cursor.Emit(OpCodes.Call, typeof(Calc).GetMethod("HexToColor", new[] {typeof(string)}));
            cursor.Emit(OpCodes.Ldarg, 6);
            cursor.Emit(OpCodes.Call, typeof(Color).GetMethod("op_Multiply"));
            cursor.Emit(OpCodes.Stloc, 5);

            cursor.Emit(OpCodes.Ldstr, "8fc7db");
            cursor.Emit(OpCodes.Call, typeof(Calc).GetMethod("HexToColor", new[] {typeof(string)}));
            cursor.Emit(OpCodes.Ldarg, 6);
            cursor.Emit(OpCodes.Call, typeof(Color).GetMethod("op_Multiply"));
            cursor.Emit(OpCodes.Stloc, 6);

            cursor.Emit(OpCodes.Br, afterInstr);
            cursor.MarkLabel(beforeInstr);
        }
    }
}