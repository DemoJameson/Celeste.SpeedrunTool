using System.Collections.Generic;
using Celeste.Mod.SpeedrunTool.Extensions;
using Microsoft.Xna.Framework;

namespace Celeste.Mod.SpeedrunTool.SaveLoad.Actions {
    public class BounceBlockAction : AbstractEntityAction {
        private Dictionary<EntityID, BounceBlock> savedBounceBlocks = new Dictionary<EntityID, BounceBlock>();

        public override void OnQuickSave(Level level) {
            savedBounceBlocks = level.Entities.GetDictionary<BounceBlock>();
        }

        private void RestoreBounceBlockState(On.Celeste.BounceBlock.orig_ctor_EntityData_Vector2 orig, BounceBlock self,
            EntityData data,
            Vector2 offset) {
            EntityID entityId = data.ToEntityId();
            self.SetEntityId(entityId);
            orig(self, data, offset);

            if (IsLoadStart && savedBounceBlocks.ContainsKey(entityId)) {
                BounceBlock savedBounceBlock = savedBounceBlocks[entityId];
                self.Position = savedBounceBlock.Position;
                self.Collidable = savedBounceBlock.Collidable;
                self.CopyFields(savedBounceBlock, "bounceDir",
                    "state",
                    "moveSpeed",
                    "windUpStartTimer",
                    "windUpProgress",
                    "bounceEndTimer",
                    "bounceLift",
                    "reappearFlash",
                    "debrisDirection",
                    "iceMode",
                    "iceModeNext",
                    "respawnTimer",
                    "reformed"
                    );
            }
        }

        public override void OnClear() {
            savedBounceBlocks.Clear();
        }

        public override void OnLoad() {
            On.Celeste.BounceBlock.ctor_EntityData_Vector2 += RestoreBounceBlockState;
        }

        public override void OnUnload() {
            On.Celeste.BounceBlock.ctor_EntityData_Vector2 -= RestoreBounceBlockState;
        }
    }
}