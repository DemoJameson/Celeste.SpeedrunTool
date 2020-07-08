using System.Collections.Generic;
using Celeste.Mod.SpeedrunTool.Extensions;
using Celeste.Mod.SpeedrunTool.SaveLoad.EntityIdPlus;
using Microsoft.Xna.Framework;

namespace Celeste.Mod.SpeedrunTool.SaveLoad.Actions {
    public class FloatySpaceBlockAction : AbstractEntityAction {
        private Dictionary<EntityId2, FloatySpaceBlock> savedFloatySpaceBlocks =
            new Dictionary<EntityId2, FloatySpaceBlock>();

        public override void OnSaveSate(Level level) {
            savedFloatySpaceBlocks = level.Entities.FindAllToDict<FloatySpaceBlock>();
        }

        private void RestoreFloatySpaceBlockPosition(On.Celeste.FloatySpaceBlock.orig_ctor_EntityData_Vector2 orig,
            FloatySpaceBlock self, EntityData data,
            Vector2 offset) {
            EntityId2 entityId = data.ToEntityId2(self.GetType());
            self.SetEntityId2(entityId);
            orig(self, data, offset);

            if (IsLoadStart && savedFloatySpaceBlocks.ContainsKey(entityId)) {
                FloatySpaceBlock saved = savedFloatySpaceBlocks[entityId];
                self.CopyFields(saved, "yLerp", "sinkTimer", "sineWave",
                    "dashEase", "dashDirection");
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