using System.Collections.Generic;
using Celeste.Mod.SpeedrunTool.Extensions;
using Microsoft.Xna.Framework;

namespace Celeste.Mod.SpeedrunTool.SaveLoad.Actions {
    public class BounceBlockAction : AbstractEntityAction {
        private Dictionary<EntityID, BounceBlock> savedBounceBlocks = new Dictionary<EntityID, BounceBlock>();

        public override void OnQuickSave(Level level) {
            savedBounceBlocks = level.Tracker.GetDictionary<BounceBlock>();
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
                self.CopyPrivateField("bounceDir", savedBounceBlock);
                self.CopyPrivateField("state", savedBounceBlock);
                self.CopyPrivateField("moveSpeed", savedBounceBlock);
                self.CopyPrivateField("windUpStartTimer", savedBounceBlock);
                self.CopyPrivateField("windUpProgress", savedBounceBlock);
                self.CopyPrivateField("bounceEndTimer", savedBounceBlock);
                self.CopyPrivateField("bounceLift", savedBounceBlock);
                self.CopyPrivateField("reappearFlash", savedBounceBlock);
                self.CopyPrivateField("debrisDirection", savedBounceBlock);
                self.CopyPrivateField("iceMode", savedBounceBlock);
                self.CopyPrivateField("iceModeNext", savedBounceBlock);
                float savedRespawnTimer = (float) savedBounceBlock.GetPrivateField("respawnTimer");
                // 避免 player 在此石头中复活时被挤开或挤死
                if (savedRespawnTimer <= 0) {
                    savedRespawnTimer += 0.017f;
                }
                self.SetPrivateField("respawnTimer", savedRespawnTimer);
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

        public override void OnInit() {
            typeof(BounceBlock).AddToTracker();
        }
    }
}