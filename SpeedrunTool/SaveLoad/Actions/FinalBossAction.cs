using System.Collections.Generic;
using System.Linq;
using Celeste.Mod.SpeedrunTool.Extensions;
using Celeste.Mod.SpeedrunTool.SaveLoad.EntityIdPlus;
using Microsoft.Xna.Framework;
using Monocle;

namespace Celeste.Mod.SpeedrunTool.SaveLoad.Actions {
    public class FinalBossAction : AbstractEntityAction {
        private Dictionary<EntityId2, FinalBoss> savedFinalBosses = new Dictionary<EntityId2, FinalBoss>();

        public override void OnQuickSave(Level level) {
            savedFinalBosses = level.Entities.FindAllToDict<FinalBoss>();
        }

        private void RestoreFinalBossState(On.Celeste.FinalBoss.orig_ctor_EntityData_Vector2 orig, FinalBoss self,
            EntityData data,
            Vector2 offset) {
            orig(self, data, offset);
            EntityId2 entityId = data.ToEntityId2(self.GetType());
            self.SetEntityId2(entityId);

            if (IsLoadStart && savedFinalBosses.ContainsKey(entityId)) {
                FinalBoss savedFinalBoss = savedFinalBosses[entityId];

                int nodeIndex = (int) savedFinalBoss.GetField(typeof(FinalBoss), "nodeIndex");
                Vector2[] nodes = (Vector2[]) savedFinalBoss.GetField(typeof(FinalBoss), "nodes");
                bool startHit = (bool) savedFinalBoss.GetField(typeof(FinalBoss), "startHit");

                self.Position = nodes[nodeIndex];

                if (data.Int("patternIndex") == 0 && nodeIndex >= 1) {
                    self.SetField(typeof(FinalBoss), "patternIndex", 1);
                }

                if (startHit) {
                    nodeIndex--;
                }

                self.SetField(typeof(FinalBoss), "nodeIndex", nodeIndex);
                self.Add(new RestoreFinalBossStateComponent(savedFinalBoss));
                
                self.CopyFields(typeof(FinalBoss), savedFinalBoss, "playerHasMoved");
            }
        }

        public override void OnClear() {
            savedFinalBosses.Clear();
        }

        public override void OnLoad() {
            On.Celeste.FinalBoss.ctor_EntityData_Vector2 += RestoreFinalBossState;
        }

        public override void OnUnload() {
            On.Celeste.FinalBoss.ctor_EntityData_Vector2 -= RestoreFinalBossState;
        }

        private class RestoreFinalBossStateComponent : Component {
            private readonly FinalBoss savedFinalBoss;

            public RestoreFinalBossStateComponent(FinalBoss savedFinalBoss) : base(true, false) {
                this.savedFinalBoss = savedFinalBoss;
            }

            public override void Update() {
                List<Entity> fallingBlocks = Entity.GetField(typeof(FinalBoss), "fallingBlocks") as List<Entity>;
                List<Entity> savedFallingBlocks = savedFinalBoss.GetField(typeof(FinalBoss), "fallingBlocks") as List<Entity>;

                if (fallingBlocks == null || savedFallingBlocks == null) {
                    RemoveSelf();
                    return;
                }

                if (fallingBlocks.Count == 0) {
                    RemoveSelf();
                    return;
                }

                if (fallingBlocks.Count != savedFallingBlocks.Count) {
                    fallingBlocks.RemoveAll(entity =>
                        savedFallingBlocks.All(savedEntity => savedEntity.Position != entity.Position));
                    RemoveSelf();
                }
            }
        }
    }
}