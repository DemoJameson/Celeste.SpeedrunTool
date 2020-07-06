using System.Collections.Generic;
using Celeste.Mod.SpeedrunTool.Extensions;
using Celeste.Mod.SpeedrunTool.SaveLoad.Component;
using Celeste.Mod.SpeedrunTool.SaveLoad.EntityIdPlus;
using Microsoft.Xna.Framework;
using MonoMod.Cil;

namespace Celeste.Mod.SpeedrunTool.SaveLoad.Actions {
    public class CrushBlockAction : AbstractEntityAction {
        private Dictionary<EntityId2, CrushBlock> savedCrushBlocks = new Dictionary<EntityId2, CrushBlock>();

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
            savedCrushBlocks = level.Entities.FindAllToDict<CrushBlock>();
        }

        private void RestoreCrushBlockState(On.Celeste.CrushBlock.orig_ctor_EntityData_Vector2 orig, CrushBlock self,
            EntityData data,
            Vector2 offset) {
            EntityId2 entityId = data.ToEntityId2(self.GetType());
            self.SetEntityId2(entityId);
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
                        self.CopySprite(savedCrushBlock, "face");
                        self.CopyImageList(savedCrushBlock, "idleImages");
                        self.CopyImageList(savedCrushBlock, "activeTopImages");
                        self.CopyImageList(savedCrushBlock, "activeRightImages");
                        self.CopyImageList(savedCrushBlock, "activeLeftImages");
                        self.CopyImageList(savedCrushBlock, "activeBottomImages");
                    }
                } else {
                    self.Add(new RemoveSelfComponent());
                }
            }
        }

        private void BlockCoroutineStart(ILContext il) {
            il.SkipAddCoroutine<CrushBlock>("AttackSequence",
                () => IsLoadStart && CoroutineAction.HasRoutine("<AttackSequence>d__41"));
        }
    }
}