using System.Collections.Generic;
using Microsoft.Xna.Framework;

namespace Celeste.Mod.SpeedrunTool.SaveLoad.Actions
{
    public class SwapBlockAction : AbstractEntityAction
    {
        private Dictionary<EntityID, SwapBlock> _swapBlocks = new Dictionary<EntityID, SwapBlock>();

        public override void OnQuickSave(Level level)
        {
            _swapBlocks = level.Tracker.GetDictionary<SwapBlock>();
        }

        private void RestoreSwapBlockState(On.Celeste.SwapBlock.orig_ctor_EntityData_Vector2 orig, SwapBlock self,
            EntityData data, Vector2 offset)
        {
            EntityID entityId = data.ToEntityId();
            self.SetEntityId(entityId);
            orig(self, data, offset);

            if (IsLoadStart && _swapBlocks.ContainsKey(entityId))
            {
                SwapBlock swapBlock = _swapBlocks[entityId];
                self.Position = swapBlock.Position;
                self.Swapping = swapBlock.Swapping;
                self.CopyPrivateField("target", swapBlock);
                self.CopyPrivateField("speed", swapBlock);
                self.CopyPrivateField("lerp", swapBlock);
                self.CopyPrivateField("returnTimer", swapBlock);
            }
        }

        public override void OnClear()
        {
            _swapBlocks.Clear();
        }

        public override void OnLoad()
        {
            On.Celeste.SwapBlock.ctor_EntityData_Vector2 += RestoreSwapBlockState;
        }

        public override void OnUnload()
        {
            On.Celeste.SwapBlock.ctor_EntityData_Vector2 -= RestoreSwapBlockState;
        }

        public override void OnInit()
        {
            typeof(SwapBlock).AddToTracker();
        }
    }
}