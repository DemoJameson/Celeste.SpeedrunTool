using System.Collections.Generic;
using Celeste.Mod.SpeedrunTool.Extensions;
using Celeste.Mod.SpeedrunTool.SaveLoad.Component;
using Celeste.Mod.SpeedrunTool.SaveLoad.EntityIdPlus;
using Microsoft.Xna.Framework;
using Monocle;

namespace Celeste.Mod.SpeedrunTool.SaveLoad.Actions {
    public class FinalBossMovingBlockAction : AbstractEntityAction {
        private Dictionary<EntityId2, FinalBossMovingBlock> savedFinalBossMovingBlocks =
            new Dictionary<EntityId2, FinalBossMovingBlock>();

        public override void OnQuickSave(Level level) {
            savedFinalBossMovingBlocks = level.Entities.FindAllToDict<FinalBossMovingBlock>();
        }

        private void RestoreFinalBossMovingBlockPosition(
            On.Celeste.FinalBossMovingBlock.orig_ctor_EntityData_Vector2 orig, FinalBossMovingBlock self,
            EntityData data,
            Vector2 offset) {
            orig(self, data, offset);

            EntityId2 entityId = data.ToEntityId2(self.GetType());
            self.SetEntityId2(entityId);

            if (!IsLoadStart) return;
            
            if (savedFinalBossMovingBlocks.ContainsKey(entityId)) {
                FinalBossMovingBlock savedBlock = savedFinalBossMovingBlocks[entityId];
                self.Position = savedBlock.Position;
                self.CopyFields(savedBlock, "startDelay", "nodeIndex", "isHighlighted");
                self.CopyTileGrid(savedBlock, "sprite");
                self.CopyTileGrid(savedBlock, "highlight");

                Tween savedTween = savedBlock.Get<Tween>();
                if (savedTween == null) {
                    return;
                }
                    
                Vector2[] nodes = data.NodesWithPosition(offset);
                int nodeIndex = (int) savedBlock.GetField("nodeIndex");
                    
                var from = nodeIndex == 1 ? data.Position + offset : nodes[1];
                var to = nodes[nodeIndex];

                Tween tween = Tween.Create(Tween.TweenMode.Oneshot, Ease.CubeIn, 0.8f, true);
                tween.OnUpdate = t => { self.MoveTo(Vector2.Lerp(@from, to, t.Eased)); };
                tween.OnComplete = t => {
                    if (self.CollideCheck<SolidTiles>(self.Position + (to - @from).SafeNormalize() * 2f)) {
                        Audio.Play("event:/game/06_reflection/fallblock_boss_impact", self.Center);
                        self.InvokeMethod("ImpactParticles", to - @from);
                    }
                    else {
                        self.InvokeMethod("StopParticles", to - @from);
                    }
                };
                tween.CopyFrom(savedTween);
                self.Add(tween);
            }
            else {
                self.Add(new RemoveSelfComponent());
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
    }
}