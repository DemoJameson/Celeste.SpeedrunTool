using System.Collections.Generic;
using Celeste.Mod.SpeedrunTool.Extensions;
using Microsoft.Xna.Framework;

namespace Celeste.Mod.SpeedrunTool.SaveLoad.Actions {
    public class FloatySpaceBlockAction : AbstractEntityAction {
        private Dictionary<EntityID, FloatySpaceBlock> savedFloatySpaceBlocks = new Dictionary<EntityID, FloatySpaceBlock>();

        public override void OnQuickSave(Level level) {
            savedFloatySpaceBlocks = level.Entities.GetDictionary<FloatySpaceBlock>();
        }

        private void RestoreFloatySpaceBlockPosition(On.Celeste.FloatySpaceBlock.orig_ctor_EntityData_Vector2 orig,
            FloatySpaceBlock self, EntityData data,
            Vector2 offset) {
            EntityID entityId = data.ToEntityId();
            self.SetEntityId(entityId);
            orig(self, data, offset);

            if (IsLoadStart && savedFloatySpaceBlocks.ContainsKey(entityId)) {
                FloatySpaceBlock savedFloatySpaceBlock = savedFloatySpaceBlocks[entityId];
                self.CopyFields(typeof(FloatySpaceBlock), savedFloatySpaceBlock, "yLerp");
                self.CopyFields(typeof(FloatySpaceBlock), savedFloatySpaceBlock, "sinkTimer");
                self.CopyFields(typeof(FloatySpaceBlock), savedFloatySpaceBlock, "sineWave");
                self.CopyFields(typeof(FloatySpaceBlock), savedFloatySpaceBlock, "dashEase");
                self.CopyFields(typeof(FloatySpaceBlock), savedFloatySpaceBlock, "dashDirection");
            }
        }

        public override void OnClear() {
            savedFloatySpaceBlocks.Clear();
        }

        public override void OnLoad() {
            On.Celeste.FloatySpaceBlock.ctor_EntityData_Vector2 += RestoreFloatySpaceBlockPosition;
        }

        public override void OnUnload() {
            On.Celeste.FloatySpaceBlock.ctor_EntityData_Vector2 -= RestoreFloatySpaceBlockPosition;
        }
    }
}