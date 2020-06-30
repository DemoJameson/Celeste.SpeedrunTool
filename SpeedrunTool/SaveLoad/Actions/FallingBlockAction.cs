using System;
using System.Collections.Generic;
using Celeste.Mod.SpeedrunTool.Extensions;
using Celeste.Mod.SpeedrunTool.SaveLoad.Component;
using Microsoft.Xna.Framework;
using Mono.Cecil.Cil;
using MonoMod.Cil;

namespace Celeste.Mod.SpeedrunTool.SaveLoad.Actions {
    public class FallingBlockAction : AbstractEntityAction {
        private Dictionary<EntityID, FallingBlock> fallingBlocks = new Dictionary<EntityID, FallingBlock>();

        public override void OnQuickSave(Level level) {
            fallingBlocks = level.Entities.GetDictionary<FallingBlock>();
        }

        public override void OnClear() {
            fallingBlocks.Clear();
        }

        private void OnFallingBlockOnCtorEntityDataVector2(On.Celeste.FallingBlock.orig_ctor_EntityData_Vector2 orig,
            FallingBlock self, EntityData data, Vector2 offset) {
            EntityID entityId = data.ToEntityId();
            self.SetEntityId(entityId);
            orig(self, data, offset);
            RestoreState(self, entityId);
        }

        private FallingBlock FallingBlockOnCreateFinalBossBlock(On.Celeste.FallingBlock.orig_CreateFinalBossBlock orig,
            EntityData data, Vector2 offset) {
            FallingBlock self = orig(data, offset);
            EntityID entityId = data.ToEntityId();
            self.SetEntityId(entityId);

            RestoreState(self, entityId);

            return self;
        }

        private void RestoreState(FallingBlock self, EntityID entityId) {
            if (IsLoadStart) {
                if (fallingBlocks.ContainsKey(entityId)) {
                    FallingBlock savedFallingBlock = fallingBlocks[entityId];
                    self.Position = savedFallingBlock.Position;
                    self.FallDelay = savedFallingBlock.FallDelay;
                    self.Triggered = savedFallingBlock.Triggered;
                    self.SetProperty("HasStartedFalling", savedFallingBlock.HasStartedFalling);
                }
                else {
                    self.Add(new RemoveSelfComponent());
                }
            }
        }

        private void BlockCoroutineStart(ILContext il) {
            ILCursor cursor = new ILCursor(il);

            if (!cursor.TryGotoNextAddCoroutine<FallingBlock>("Sequence", out var skipCoroutine)) {
                return;
            }

            ILLabel label = cursor.MarkLabel();

            cursor.EmitDelegate<Func<bool>>(() => IsLoadStart);
            cursor.Emit(OpCodes.Brtrue, skipCoroutine);

            if (cursor.TryGotoPrev(MoveType.After, i => i.OpCode == OpCodes.Ldarg_S && i.Operand.ToString() == "finalBoss",
                i => i.OpCode == OpCodes.Brfalse_S)) {
                cursor.Prev.Operand = label;
            }
        }

        public override void OnLoad() {
            On.Celeste.FallingBlock.CreateFinalBossBlock += FallingBlockOnCreateFinalBossBlock;
            On.Celeste.FallingBlock.ctor_EntityData_Vector2 += OnFallingBlockOnCtorEntityDataVector2;
            IL.Celeste.FallingBlock.ctor_Vector2_char_int_int_bool_bool_bool += BlockCoroutineStart;
        }

        public override void OnUnload() {
            On.Celeste.FallingBlock.CreateFinalBossBlock -= FallingBlockOnCreateFinalBossBlock;
            On.Celeste.FallingBlock.ctor_EntityData_Vector2 -= OnFallingBlockOnCtorEntityDataVector2;
            IL.Celeste.FallingBlock.ctor_Vector2_char_int_int_bool_bool_bool -= BlockCoroutineStart;
        }
    }
}