using System.Collections.Generic;
using Celeste.Mod.SpeedrunTool.Extensions;
using Celeste.Mod.SpeedrunTool.SaveLoad.Component;
using Microsoft.Xna.Framework;

namespace Celeste.Mod.SpeedrunTool.SaveLoad.Actions {
    public class FallingBlockAction : AbstractEntityAction {
        private Dictionary<EntityID, FallingBlock> fallingBlocks = new Dictionary<EntityID, FallingBlock>();
        private const string DisableShakeSfx = "DisableShakeSfx";
        private const string DisableImpactSfx = "DisableImpactSfx";
        private const string DisableLandParticles = "DisableLandParticles";

        public override void OnQuickSave(Level level) {
            fallingBlocks = level.Entities.GetDictionary<FallingBlock>();
        }

        public override void OnClear() {
            fallingBlocks.Clear();
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
                    FallingBlock savedFallingBlock = fallingBlocks[entityId];
                    self.Position = savedFallingBlock.Position;
                    if (savedFallingBlock.HasStartedFalling) {
                        self.SetExtendedBoolean(DisableShakeSfx, true);
                        self.Triggered = true;
                        if (OnGround(savedFallingBlock)) {
                            self.Add(new FastForwardComponent<FallingBlock>(savedFallingBlock, OnFastForward));
                            self.SetExtendedBoolean(DisableImpactSfx, true);
                            self.SetExtendedBoolean(DisableLandParticles, true);
                        }
                    }
                }
                else {
                    self.Add(new RemoveSelfComponent());
                }
            }
        }

        private void OnFastForward(FallingBlock entity, FallingBlock savedentity) {
            for (var i = 0; i < 60; i++) {
                entity.Update();
            }
        }

        private static bool OnGround(FallingBlock fallingBlock, int downCheck = 1) {
            if (fallingBlock.CollideCheck<Solid>(fallingBlock.Position + Vector2.UnitY * downCheck)) {
                return true;
            }

            return fallingBlock.CollideCheckOutside<JumpThru>(
                fallingBlock.Position + Vector2.UnitY * downCheck);
        }

        private void FallingBlockOnShakeSfx(On.Celeste.FallingBlock.orig_ShakeSfx orig, FallingBlock self) {
            if (self.GetExtendedBoolean(DisableShakeSfx)) {
                self.SetExtendedBoolean(DisableShakeSfx, false);
                return;
            }

            orig(self);
        }

        private void FallingBlockOnImpactSfx(On.Celeste.FallingBlock.orig_ImpactSfx orig, FallingBlock self) {
            if (self.GetExtendedBoolean(DisableImpactSfx)) {
                self.SetExtendedBoolean(DisableImpactSfx, false);
                return;
            }

            orig(self);
        }

        private void FallingBlockOnLandParticles(On.Celeste.FallingBlock.orig_LandParticles orig, FallingBlock self) {
            if (self.GetExtendedBoolean(DisableLandParticles)) {
                self.SetExtendedBoolean(DisableLandParticles, false);
                return;
            }

            orig(self);
        }

        public override void OnLoad() {
            On.Celeste.FallingBlock.CreateFinalBossBlock += FallingBlockOnCreateFinalBossBlock;
            On.Celeste.FallingBlock.ctor_EntityData_Vector2 += OnFallingBlockOnCtorEntityDataVector2;
            On.Celeste.FallingBlock.ctor_Vector2_char_int_int_bool_bool_bool +=
                OnFallingBlockOnCtorVector2CharIntIntBoolBoolBool;
            On.Celeste.FallingBlock.ShakeSfx += FallingBlockOnShakeSfx;
            On.Celeste.FallingBlock.ImpactSfx += FallingBlockOnImpactSfx;
            On.Celeste.FallingBlock.LandParticles += FallingBlockOnLandParticles;
        }

        public override void OnUnload() {
            On.Celeste.FallingBlock.CreateFinalBossBlock -= FallingBlockOnCreateFinalBossBlock;
            On.Celeste.FallingBlock.ctor_EntityData_Vector2 -= OnFallingBlockOnCtorEntityDataVector2;
            On.Celeste.FallingBlock.ctor_Vector2_char_int_int_bool_bool_bool -=
                OnFallingBlockOnCtorVector2CharIntIntBoolBoolBool;
            On.Celeste.FallingBlock.ShakeSfx -= FallingBlockOnShakeSfx;
            On.Celeste.FallingBlock.ImpactSfx -= FallingBlockOnImpactSfx;
            On.Celeste.FallingBlock.LandParticles -= FallingBlockOnLandParticles;
        }
    }
}