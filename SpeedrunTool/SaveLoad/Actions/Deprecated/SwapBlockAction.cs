using System.Collections.Generic;
using Celeste.Mod.SpeedrunTool.Extensions;
using Celeste.Mod.SpeedrunTool.SaveLoad.EntityIdPlus;
using Microsoft.Xna.Framework;

namespace Celeste.Mod.SpeedrunTool.SaveLoad.Actions.Deprecated {
    public class SwapBlockAction : ComponentAction {
        private Dictionary<EntityId2, SwapBlock> swapBlocks = new Dictionary<EntityId2, SwapBlock>();

        public override void OnSaveSate(Level level) {
            swapBlocks = level.Entities.FindAllToDict<SwapBlock>();
        }

        private void RestoreSwapBlockState(On.Celeste.SwapBlock.orig_ctor_EntityData_Vector2 orig, SwapBlock self,
            EntityData data, Vector2 offset) {
            EntityId2 entityId = data.ToEntityId2(self.GetType());
            self.SetEntityId2(entityId);
            orig(self, data, offset);

            if (IsLoadStart && swapBlocks.ContainsKey(entityId)) {
                SwapBlock swapBlock = swapBlocks[entityId];
                self.Position = swapBlock.Position;
                self.Swapping = swapBlock.Swapping;
                self.CopyFields(typeof(SwapBlock), swapBlock, "target");
                self.CopyFields(typeof(SwapBlock), swapBlock, "speed");
                self.CopyFields(typeof(SwapBlock), swapBlock, "lerp");
                self.CopyFields(typeof(SwapBlock), swapBlock, "returnTimer");
            }
        }

        public override void OnClear() {
            swapBlocks.Clear();
        }

        public override void OnLoad() {
            On.Celeste.SwapBlock.ctor_EntityData_Vector2 += RestoreSwapBlockState;
        }

        public override void OnUnload() {
            On.Celeste.SwapBlock.ctor_EntityData_Vector2 -= RestoreSwapBlockState;
        }
    }
}