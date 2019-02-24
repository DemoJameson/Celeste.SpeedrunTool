using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Xna.Framework;
using Monocle;

namespace Celeste.Mod.SpeedrunTool.SaveLoad.Actions {
    public class MoveBlockAction : AbstractEntityAction {
        private Dictionary<EntityID, MoveBlock> movingBlocks = new Dictionary<EntityID, MoveBlock>();

        public override void OnQuickSave(Level level) {
            movingBlocks = level.Tracker.GetCastEntities<MoveBlock>()
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

                if ((int) movingBlocks[entityId].GetPrivateField("state") == 1)
                    self.Add(new Coroutine(TriggerBlock(self)));
            }
        }

        private static IEnumerator TriggerBlock(MoveBlock self) {
            self.OnStaticMoverTrigger();
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

        public override void OnInit() {
            typeof(MoveBlock).AddToTracker();
        }
    }
}