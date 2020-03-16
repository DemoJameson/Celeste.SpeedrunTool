using System;
using System.Collections;
using System.Collections.Generic;
using Celeste.Mod.SpeedrunTool.Extensions;
using Celeste.Mod.SpeedrunTool.SaveLoad.Component;
using Microsoft.Xna.Framework;
using Monocle;

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
                    if (savedFallingBlock.HasStartedFalling || savedFallingBlock.Triggered) {
                        self.SetExtendedBoolean(DisableShakeSfx, true);
                        self.FallDelay = savedFallingBlock.FallDelay;
                        self.Triggered = true;
                        if (OnGround(savedFallingBlock)) {
                            self.Add(new FastForwardComponent<FallingBlock>(savedFallingBlock, SkipFallingGroundEffect));
                            self.SetExtendedBoolean(DisableImpactSfx, true);
                            self.SetExtendedBoolean(DisableLandParticles, true);
                        } else if(self.FallDelay > 0){
                            self.StartShaking(self.FallDelay);
                        } else {
                            self.Add(new FastForwardComponent<FallingBlock>(savedFallingBlock, SkipShake));
                        }
                    }
                }
                else {
                    self.Add(new RemoveSelfComponent());
                }
            }
        }

        private void SkipFallingGroundEffect(FallingBlock entity, FallingBlock savedEntity) {
            for (int i = 0; i < 60; i++) {
                entity.Update();
            }
        }

        private void SkipShake(FallingBlock entity, FallingBlock savedEntity) {
            int shakeTime = savedEntity.GetExtendedDataValue<int>(nameof(shakeTime));
            for (int i = 0; i < shakeTime; i++) {
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

        private IEnumerator FallingBlockOnSequence(On.Celeste.FallingBlock.orig_Sequence orig, FallingBlock self) {
                int shakeTime = 0;
                IEnumerator enumerator = orig(self);
                while (enumerator.MoveNext()) {
                    object result = enumerator.Current;
                    if (result is float shakeTimer && self.HasStartedFalling && Math.Abs(shakeTimer - 0.2f) < 0.01) {
                        shakeTime = 0;
                        shakeTimer += 0.016f;
                        while (shakeTimer > 0f) {
                            shakeTimer -= Engine.DeltaTime;
                            shakeTime++;
                            self.SetExtendedDataValue(nameof(shakeTime), shakeTime);
                            yield return null;
                        }
                        continue;
                    }

                    if (result == null && shakeTime > 0 && (bool) self.GetField(typeof(Platform), "shaking")) {
                        shakeTime++;
                        self.SetExtendedDataValue(nameof(shakeTime), shakeTime);
                    }

                    yield return result;
                }
        }

        public override void OnLoad() {
            On.Celeste.FallingBlock.CreateFinalBossBlock += FallingBlockOnCreateFinalBossBlock;
            On.Celeste.FallingBlock.ctor_EntityData_Vector2 += OnFallingBlockOnCtorEntityDataVector2;
            On.Celeste.FallingBlock.Sequence += FallingBlockOnSequence;
            On.Celeste.FallingBlock.ShakeSfx += FallingBlockOnShakeSfx;
            On.Celeste.FallingBlock.ImpactSfx += FallingBlockOnImpactSfx;
            On.Celeste.FallingBlock.LandParticles += FallingBlockOnLandParticles;
        }

        public override void OnUnload() {
            On.Celeste.FallingBlock.CreateFinalBossBlock -= FallingBlockOnCreateFinalBossBlock;
            On.Celeste.FallingBlock.ctor_EntityData_Vector2 -= OnFallingBlockOnCtorEntityDataVector2;
            On.Celeste.FallingBlock.Sequence -= FallingBlockOnSequence;
            On.Celeste.FallingBlock.ShakeSfx -= FallingBlockOnShakeSfx;
            On.Celeste.FallingBlock.ImpactSfx -= FallingBlockOnImpactSfx;
            On.Celeste.FallingBlock.LandParticles -= FallingBlockOnLandParticles;
        }
    }
}