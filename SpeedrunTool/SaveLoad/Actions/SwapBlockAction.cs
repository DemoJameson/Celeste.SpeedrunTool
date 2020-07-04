using System.Collections.Generic;
using Celeste.Mod.SpeedrunTool.Extensions;
using Microsoft.Xna.Framework;

namespace Celeste.Mod.SpeedrunTool.SaveLoad.Actions {
    public class SwapBlockAction : AbstractEntityAction {
        private Dictionary<EntityID, SwapBlock> swapBlocks = new Dictionary<EntityID, SwapBlock>();

        public override void OnQuickSave(Level level) {
            swapBlocks = level.Entities.GetDictionary<SwapBlock>();
        }

        private void RestoreSwapBlockState(On.Celeste.SwapBlock.orig_ctor_EntityData_Vector2 orig, SwapBlock self,
            EntityData data, Vector2 offset) {
            EntityID entityId = data.ToEntityId();
            self.SetEntityId(entityId);
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