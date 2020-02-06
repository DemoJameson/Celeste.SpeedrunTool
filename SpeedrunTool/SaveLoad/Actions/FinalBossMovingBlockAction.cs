using System.Collections.Generic;
using Celeste.Mod.SpeedrunTool.Extensions;
using Celeste.Mod.SpeedrunTool.SaveLoad.Component;
using Microsoft.Xna.Framework;

namespace Celeste.Mod.SpeedrunTool.SaveLoad.Actions {
    public class FinalBossMovingBlockAction : AbstractEntityAction {
        private Dictionary<EntityID, FinalBossMovingBlock> savedFinalBossMovingBlocks =
            new Dictionary<EntityID, FinalBossMovingBlock>();

        public override void OnQuickSave(Level level) {
            savedFinalBossMovingBlocks = level.Entities.GetDictionary<FinalBossMovingBlock>();
        }

        private void RestoreFinalBossMovingBlockPosition(
            On.Celeste.FinalBossMovingBlock.orig_ctor_EntityData_Vector2 orig, FinalBossMovingBlock self,
            EntityData data,
            Vector2 offset) {
            orig(self, data, offset);

            EntityID entityId = data.ToEntityId();
            self.SetEntityId(entityId);

            if (IsLoadStart) {
                if (savedFinalBossMovingBlocks.ContainsKey(entityId)) {
                    FinalBossMovingBlock savedEntity = savedFinalBossMovingBlocks[entityId];
                    self.Position = savedEntity.Position;
                    self.Add(new UpdateComponent());
                    self.Add(new FastForwardComponent<FinalBossMovingBlock>(savedEntity, OnFastForward));
                }
                else {
                    self.Add(new RemoveSelfComponent());
                }
            }
        }

        private void OnFastForward(FinalBossMovingBlock entity, FinalBossMovingBlock savedEntity) {
            // 0.5s
            for (int i = 0; i < 30; i++) {
                entity.Update();
            }
        }

        public override void OnClear() {
            savedFinalBossMovingBlocks.Clear();
        }

        public override void OnLoad() {
            On.Celeste.FinalBossMovingBlock.ctor_EntityData_Vector2 += RestoreFinalBossMovingBlockPosition;
        }

        public override void OnUnload() {
            On.Celeste.FinalBossMovingBlock.ctor_EntityData_Vector2 -= RestoreFinalBossMovingBlockPosition;
        }

        private class UpdateComponent : Monocle.Component {
            public UpdateComponent() : base(true, false) { }

            public override void Update() {
                FinalBoss finalBoss = Scene.Entities.FindFirst<FinalBoss>();
                if (finalBoss == null) {
                    return;
                }
                int nodeIndex = (int) finalBoss.GetField("nodeIndex");
                FinalBossMovingBlock finalBossMovingBlock = EntityAs<FinalBossMovingBlock>();

                if ((bool) finalBoss.GetField("playerHasMoved") && finalBossMovingBlock.BossNodeIndex == nodeIndex) {
                    finalBossMovingBlock.StartMoving(0);
                }
                
                RemoveSelf();
            }
        }
    }
}