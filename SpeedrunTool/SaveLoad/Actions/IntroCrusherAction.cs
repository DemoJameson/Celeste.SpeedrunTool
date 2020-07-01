using System;
using System.Collections.Generic;
using Celeste.Mod.SpeedrunTool.Extensions;
using Microsoft.Xna.Framework;
using Mono.Cecil.Cil;
using MonoMod.Cil;
using MonoMod.RuntimeDetour;

namespace Celeste.Mod.SpeedrunTool.SaveLoad.Actions {
    public class IntroCrusherAction : AbstractEntityAction {
        private Dictionary<EntityID, IntroCrusher> savedIntroCrushers = new Dictionary<EntityID, IntroCrusher>();

        public override void OnQuickSave(Level level) {
            savedIntroCrushers = level.Entities.GetDictionary<IntroCrusher>();
        }
        
        private ILHook addedHook;

        private void RestoreIntroCrusherPosition(On.Celeste.IntroCrusher.orig_ctor_EntityData_Vector2 orig,
            IntroCrusher self, EntityData data, Vector2 offset) {
            EntityID entityId = data.ToEntityId();
            self.SetEntityId(entityId);
            orig(self, data, offset);

            if (IsLoadStart && savedIntroCrushers.ContainsKey(entityId)) {
                IntroCrusher saved = savedIntroCrushers[entityId];
                self.Position = saved.Position;
                self.CopyFields(saved, "shake", "triggered");
                self.CopyTileGrid(saved, "tilegrid");
            }
        }

        private void IntroCrusherOnAdded(ILContext il) {
            ILCursor cursor = new ILCursor(il);

            if (!cursor.TryGotoNextAddCoroutine<IntroCrusher>("Sequence", out var skipCoroutine)) {
                return;
            }

            ILLabel label = cursor.MarkLabel();

            cursor.EmitDelegate<Func<bool>>(() => IsLoadStart);
            cursor.Emit(OpCodes.Brtrue, skipCoroutine);

            if (cursor.TryGotoPrev(MoveType.After,
                i => i.OpCode == OpCodes.Call || i.OpCode == OpCodes.Callvirt,
                i => i.OpCode == OpCodes.Brfalse_S)) {
                cursor.Prev.Operand = label;
            }
        }

        public override void OnClear() {
            savedIntroCrushers.Clear();
        }

        public override void OnLoad() {
            On.Celeste.IntroCrusher.ctor_EntityData_Vector2 += RestoreIntroCrusherPosition;
            IL.Celeste.IntroCrusher.Added += IntroCrusherOnAdded;
            addedHook = new ILHook(typeof(IntroCrusher).GetMethod("orig_Added"), IntroCrusherOnAdded);
        }

        public override void OnUnload() {
            On.Celeste.IntroCrusher.ctor_EntityData_Vector2 -= RestoreIntroCrusherPosition;
            IL.Celeste.IntroCrusher.Added -= IntroCrusherOnAdded;
            addedHook.Dispose();
        }
    }
}