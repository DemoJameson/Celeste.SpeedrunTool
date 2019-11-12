using System.Collections.Generic;
using Celeste.Mod.SpeedrunTool.Extensions;
using Celeste.Mod.SpeedrunTool.SaveLoad.Component;
using Microsoft.Xna.Framework;

namespace Celeste.Mod.SpeedrunTool.SaveLoad.Actions {
    public class FallingBlockAction : AbstractEntityAction {
        private bool disableShakeSfx;
        private Dictionary<EntityID, FallingBlock> fallingBlocks = new Dictionary<EntityID, FallingBlock>();

        public override void OnQuickSave(Level level) {
            fallingBlocks = level.Tracker.GetDictionary<FallingBlock>();
        }

        public override void OnClear() {
            fallingBlocks.Clear();
            disableShakeSfx = false;
        }

        private void OnFallingBlockOnCtorVector2CharIntIntBoolBoolBool(
            On.Celeste.FallingBlock.orig_ctor_Vector2_char_int_int_bool_bool_bool orig, FallingBlock self,
            Vector2 position, char tile, int width, int height, bool boss, bool behind, bool fall) {
            orig(self, position, tile, width, height, boss, behind, fall);

            EntityID entityId = self.GetEntityId();
            if (!entityId.Equals(default(EntityID))) {
                RestoreState(self, entityId);
            }
        }

        private void OnFallingBlockOnCtorEntityDataVector2(On.Celeste.FallingBlock.orig_ctor_EntityData_Vector2 orig,
            FallingBlock self, EntityData data, Vector2 offset) {
            EntityID entityId = data.ToEntityId();
            self.SetEntityId(entityId);
            orig(self, data, offset);
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
                    FallingBlock fallingBlock = fallingBlocks[entityId];
                    self.Position = fallingBlock.Position;
                    if (fallingBlock.HasStartedFalling && !OnGround(fallingBlock)) {
                        disableShakeSfx = true;
                        self.Triggered = true;
                    }
                }
                else {
                    self.Add(new RemoveSelfComponent());
                }
            }
        }

        private static bool OnGround(FallingBlock fallingBlock, int downCheck = 1) {
            if (fallingBlock.CollideCheck<Solid>(fallingBlock.Position + Vector2.UnitY * downCheck)) {
                return true;
            }

            return fallingBlock.CollideCheckOutside<JumpThru>(
                fallingBlock.Position + Vector2.UnitY * downCheck);
        }

        private void DisableShakeSfx(On.Celeste.FallingBlock.orig_ShakeSfx orig, FallingBlock self) {
            if (disableShakeSfx) {
                disableShakeSfx = false;
                return;
            }

            orig(self);
        }

        public override void OnLoad() {
            On.Celeste.FallingBlock.CreateFinalBossBlock += FallingBlockOnCreateFinalBossBlock;
            On.Celeste.FallingBlock.ctor_EntityData_Vector2 += OnFallingBlockOnCtorEntityDataVector2;
            On.Celeste.FallingBlock.ctor_Vector2_char_int_int_bool_bool_bool +=
                OnFallingBlockOnCtorVector2CharIntIntBoolBoolBool;
            On.Celeste.FallingBlock.ShakeSfx += DisableShakeSfx;
        }

        public override void OnUnload() {
            On.Celeste.FallingBlock.CreateFinalBossBlock -= FallingBlockOnCreateFinalBossBlock;
            On.Celeste.FallingBlock.ctor_EntityData_Vector2 -= OnFallingBlockOnCtorEntityDataVector2;
            On.Celeste.FallingBlock.ctor_Vector2_char_int_int_bool_bool_bool -=
                OnFallingBlockOnCtorVector2CharIntIntBoolBoolBool;
            On.Celeste.FallingBlock.ShakeSfx -= DisableShakeSfx;
        }

        public override void OnInit() {
            typeof(FallingBlock).AddToTracker();
        }

        public override void OnUpdateEntitiesWhenFreeze(Level level) {
            level.UpdateEntities<FallingBlock>();
        }
    }
}