using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Celeste.Mod.SpeedrunTool.Extensions;
using Microsoft.Xna.Framework;
using Monocle;

namespace Celeste.Mod.SpeedrunTool.SaveLoad.Actions {
    public class MoveBlockAction : AbstractEntityAction {
        private Dictionary<EntityID, MoveBlock> movingBlocks = new Dictionary<EntityID, MoveBlock>();

        public override void OnQuickSave(Level level) {
            movingBlocks = level.Entities.FindAll<MoveBlock>()
                .Where(block => (int) block.GetPrivateField("state") == 1).ToDictionary(block => block.GetEntityId());
        }


        private void RestoreMoveBlockStateOnCreate(On.Celeste.MoveBlock.orig_ctor_EntityData_Vector2 orig,
            MoveBlock self,
            EntityData data, Vector2 offset) {
            EntityID entityId = data.ToEntityId();
            self.SetEntityId(entityId);
            orig(self, data, offset);

            if (IsLoadStart && movingBlocks.ContainsKey(entityId)) {
                self.Position = movingBlocks[entityId].Position;

                int state = (int) movingBlocks[entityId].GetPrivateField("state");
                if (state == 1) {
                    // MovementState.Moving
                    self.Add(new Coroutine(TriggerBlock(self)));
                } else if (state == 2) {
                    // MovementState.Breaking
                    // TODO 还原破碎后的状态
                }
            }
        }

        private static IEnumerator TriggerBlock(MoveBlock self) {
            self.OnStaticMoverTrigger(null);
            yield break;
        }

        public override void OnClear() {
            movingBlocks.Clear();
        }

        public override void OnLoad() {
            On.Celeste.MoveBlock.ctor_EntityData_Vector2 += RestoreMoveBlockStateOnCreate;
        }

        public override void OnUnload() {
            On.Celeste.MoveBlock.ctor_EntityData_Vector2 -= RestoreMoveBlockStateOnCreate;
        }
    }
}