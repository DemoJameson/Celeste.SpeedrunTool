using System.Collections.Generic;
using Celeste.Mod.SpeedrunTool.Extensions;
using Celeste.Mod.SpeedrunTool.SaveLoad.Component;
using Microsoft.Xna.Framework;
using MonoMod.Cil;

namespace Celeste.Mod.SpeedrunTool.SaveLoad.Actions {
    public class FallingBlockAction : AbstractEntityAction {
        private Dictionary<EntityId2, FallingBlock> fallingBlocks = new Dictionary<EntityId2, FallingBlock>();

        public override void OnQuickSave(Level level) {
            fallingBlocks = level.Entities.FindAllToDict<FallingBlock>();
        }

        public override void OnClear() {
            fallingBlocks.Clear();
        }

        private void OnFallingBlockOnCtorEntityDataVector2(On.Celeste.FallingBlock.orig_ctor_EntityData_Vector2 orig,
            FallingBlock self, EntityData data, Vector2 offset) {
            EntityId2 entityId2 = data.ToEntityId2(self.GetType());
            self.SetEntityId2(entityId2);
            orig(self, data, offset);
            RestoreState(self, entityId2);
        }

        private FallingBlock FallingBlockOnCreateFinalBossBlock(On.Celeste.FallingBlock.orig_CreateFinalBossBlock orig,
            EntityData data, Vector2 offset) {
            FallingBlock self = orig(data, offset);
            EntityId2 entityId = data.ToEntityId2(self.GetType());
            self.SetEntityId2(entityId);

            RestoreState(self, entityId);

            return self;
        }

        private void RestoreState(FallingBlock self, EntityId2 entityId) {
            if (IsLoadStart) {
                if (fallingBlocks.ContainsKey(entityId)) {
                    FallingBlock savedFallingBlock = fallingBlocks[entityId];
                    self.Position = savedFallingBlock.Position;
                    self.FallDelay = savedFallingBlock.FallDelay;
                    self.Triggered = savedFallingBlock.Triggered;
                    self.SetProperty("HasStartedFalling", savedFallingBlock.HasStartedFalling);
                } else {
                    self.Add(new RemoveSelfComponent());
                }
            }
        }

        private void BlockCoroutineStart(ILContext il) {
            il.SkipAddCoroutine<FallingBlock>("Sequence", () => IsLoadStart);
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