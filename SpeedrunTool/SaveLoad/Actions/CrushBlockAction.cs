using System;
using System.Collections.Generic;
using Celeste.Mod.SpeedrunTool.Extensions;
using Celeste.Mod.SpeedrunTool.SaveLoad.Component;
using Microsoft.Xna.Framework;
using Mono.Cecil.Cil;
using Monocle;
using MonoMod.Cil;

namespace Celeste.Mod.SpeedrunTool.SaveLoad.Actions {
    public class CrushBlockAction : AbstractEntityAction {
        private Dictionary<EntityID, CrushBlock> savedCrushBlocks = new Dictionary<EntityID, CrushBlock>();

        public override void OnClear() {
            savedCrushBlocks.Clear();
        }

        public override void OnLoad() {
            On.Celeste.CrushBlock.ctor_EntityData_Vector2 += RestoreCrushBlockState;
            IL.Celeste.CrushBlock.ctor_Vector2_float_float_Axes_bool += BlockCoroutineStart;
        }

        public override void OnUnload() {
            On.Celeste.CrushBlock.ctor_EntityData_Vector2 -= RestoreCrushBlockState;
            IL.Celeste.CrushBlock.ctor_Vector2_float_float_Axes_bool -= BlockCoroutineStart;
        }

        public override void OnQuickSave(Level level) {
            savedCrushBlocks = level.Entities.GetDictionary<CrushBlock>();
        }

        private void RestoreCrushBlockState(On.Celeste.CrushBlock.orig_ctor_EntityData_Vector2 orig, CrushBlock self,
            EntityData data,
            Vector2 offset) {
            EntityID entityId = data.ToEntityId();
            self.SetEntityId(entityId);
            orig(self, data, offset);

            if (IsLoadStart) {
                if (savedCrushBlocks.ContainsKey(entityId)) {
                    CrushBlock savedCrushBlock = savedCrushBlocks[entityId];
                    if (self.Position != savedCrushBlock.Position) {
                        self.Position = savedCrushBlock.Position;
                        object returnStack = savedCrushBlock.GetField("returnStack").Copy();
                        self.SetField("returnStack", returnStack);
                        self.CopyFields(savedCrushBlock,
                            "chillOut",
                            "canActivate",
                            "crushDir",
                            "nextFaceDirection",
                            "currentMoveLoopSfx",
                            "returnLoopSfx"
                        );
                    }
                } else {
                    self.Add(new RemoveSelfComponent());
                }
            }
        }

        private void BlockCoroutineStart(ILContext il) {
            ILCursor c = new ILCursor(il);
            c.GotoNext((i) => i.MatchCall(typeof(Entity).GetMethod("Add", new Type[] {typeof(Monocle.Component)})));
            Instruction skipCoroutine = c.Next.Next;
            c.GotoPrev((i) => i.MatchStfld(typeof(CrushBlock), "canActivate"));
            c.GotoNext();
            c.EmitDelegate<Func<bool>>(() => IsLoadStart && CoroutineAction.HasRoutine("<AttackSequence>d__41"));
            //this also skips setting attackSequence - that's treated as a special case in CoroutineAction.
            c.Emit(OpCodes.Brtrue, skipCoroutine);
        }
    }
}